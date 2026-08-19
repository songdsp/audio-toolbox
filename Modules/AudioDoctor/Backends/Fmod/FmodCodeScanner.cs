using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;

namespace AudioToolbox.AudioDoctor.Backends.Fmod
{
    /// <summary>
    /// Reads .cs files for FMOD calls that carry a string literal.
    /// </summary>
    /// <remarks>
    /// Regex rather than Roslyn, deliberately: v0.1 needs to run inside the editor
    /// without a compilation pipeline, and the shapes being matched are a handful of
    /// well-known API calls rather than arbitrary C#. The cost is real and is stated
    /// rather than hidden - anything assembled at runtime is reported as a coverage
    /// note so the report never implies it checked more than it did.
    ///
    /// The parameter-to-event linkage is the part that earns the rule set its keep.
    /// A misspelled parameter name fails completely silently at runtime: no exception,
    /// no log line, just an effect that never happens. Tying
    /// <c>instance.setParameterByName("...")</c> back to the
    /// <c>CreateInstance("event:/...")</c> that produced the instance is what makes
    /// that catchable at all.
    /// </remarks>
    internal sealed class FmodCodeScanner
    {
        private static readonly RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

        /// <summary>var instance = RuntimeManager.CreateInstance("event:/X");</summary>
        private static readonly Regex InstanceAssignment = new Regex(
            @"(?:var\s+|EventInstance\s+)?(?<var>\w+)\s*=\s*(?:FMODUnity\s*\.\s*)?RuntimeManager\s*\.\s*CreateInstance\s*\(\s*""(?<key>[^""]+)""",
            Options);

        private static readonly Regex CreateInstance = new Regex(
            @"RuntimeManager\s*\.\s*CreateInstance\s*\(\s*""(?<key>[^""]+)""",
            Options);

        /// <summary>Captures the remaining arguments so a position argument can be detected.</summary>
        private static readonly Regex PlayOneShot = new Regex(
            @"RuntimeManager\s*\.\s*PlayOneShot\s*\(\s*""(?<key>[^""]+)""(?<rest>[^;]*?)\)",
            Options);

        private static readonly Regex PlayOneShotAttached = new Regex(
            @"RuntimeManager\s*\.\s*PlayOneShotAttached\s*\(\s*""(?<key>[^""]+)""",
            Options);

        private static readonly Regex LoadBank = new Regex(
            @"RuntimeManager\s*\.\s*LoadBank\s*\(\s*""(?<bank>[^""]+)""",
            Options);

        private static readonly Regex SetParameterByName = new Regex(
            @"(?<recv>\w+)\s*\.\s*setParameterByName\s*\(\s*""(?<param>[^""]+)""",
            Options);

        /// <summary>StudioEventEmitter.SetParameter, whose receiver cannot be resolved statically.</summary>
        private static readonly Regex EmitterSetParameter = new Regex(
            @"(?<recv>\w+)\s*\.\s*SetParameter\s*\(\s*""(?<param>[^""]+)""",
            Options);

        /// <summary>An event name stitched together from pieces rather than written out.</summary>
        private static readonly Regex ComposedEventArgument = new Regex(
            @"(?:PlayOneShot|PlayOneShotAttached|CreateInstance)\s*\(\s*(?<arg>[^;]*?)\)",
            Options);

        /// <summary>Receivers that resolve to the global parameter scope rather than one event.</summary>
        private static readonly HashSet<string> GlobalReceivers = new HashSet<string>(StringComparer.Ordinal)
        {
            "StudioSystem",
            "studioSystem",
        };

        public void Scan(string assetPath, string[] lines, ReferenceSink sink)
        {
            // Instance variables are assigned before they are used, so one forward pass
            // over the whole file is enough to resolve almost every parameter call.
            var instanceVariables = MapInstanceVariables(lines);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineNumber = i + 1;

                if (IsComment(line))
                {
                    continue;
                }

                foreach (Match match in PlayOneShot.Matches(line))
                {
                    if (IsFragment(line, match, "key"))
                    {
                        continue;
                    }

                    // PlayOneShot(path) defaults the position to zero, which is not the
                    // listener's position - a 3D event played this way is audibly wrong,
                    // and that is what R006 is looking for.
                    var hasPosition = match.Groups["rest"].Value.Contains(",");

                    sink.Add(new EventRefUsage
                    {
                        EventKey = match.Groups["key"].Value,
                        AssetPath = assetPath,
                        Source = RefSource.CodeLiteral,
                        Line = lineNumber,
                        IsSpatializedCallSite = hasPosition,
                    });
                }

                foreach (Match match in PlayOneShotAttached.Matches(line))
                {
                    if (IsFragment(line, match, "key"))
                    {
                        continue;
                    }

                    sink.Add(new EventRefUsage
                    {
                        EventKey = match.Groups["key"].Value,
                        AssetPath = assetPath,
                        Source = RefSource.CodeLiteral,
                        Line = lineNumber,
                        IsSpatializedCallSite = true,
                    });
                }

                foreach (Match match in CreateInstance.Matches(line))
                {
                    if (IsFragment(line, match, "key"))
                    {
                        continue;
                    }

                    sink.Add(new EventRefUsage
                    {
                        EventKey = match.Groups["key"].Value,
                        AssetPath = assetPath,
                        Source = RefSource.CodeLiteral,
                        Line = lineNumber,
                        // Whether the instance gets a 3D attribute is decided later in
                        // the program, so this call site says nothing about spatialization.
                        IsSpatializedCallSite = null,
                    });
                }

                foreach (Match match in LoadBank.Matches(line))
                {
                    if (IsFragment(line, match, "bank"))
                    {
                        continue;
                    }

                    sink.Add(new BankLoadUsage
                    {
                        BankName = match.Groups["bank"].Value,
                        AssetPath = assetPath,
                        Source = BankLoadSource.CodeCall,
                        Line = lineNumber,
                    });
                }

                CollectParameterCalls(assetPath, line, lineNumber, instanceVariables, sink);
                NoteComposedEventNames(assetPath, line, lineNumber, sink);
            }
        }

        private static void CollectParameterCalls(
            string assetPath,
            string line,
            int lineNumber,
            IReadOnlyDictionary<string, string> instanceVariables,
            ReferenceSink sink)
        {
            foreach (Match match in SetParameterByName.Matches(line))
            {
                var receiver = match.Groups["recv"].Value;
                var parameterName = match.Groups["param"].Value;

                if (GlobalReceivers.Contains(receiver))
                {
                    sink.Add(new ParameterUsage
                    {
                        EventKey = null,
                        ParameterName = parameterName,
                        AssetPath = assetPath,
                        Line = lineNumber,
                        IsGlobal = true,
                        ResolutionNote = $"Set on {receiver}, so it targets the global parameter scope.",
                    });

                    continue;
                }

                if (instanceVariables.TryGetValue(receiver, out var eventKey))
                {
                    sink.Add(new ParameterUsage
                    {
                        EventKey = eventKey,
                        ParameterName = parameterName,
                        AssetPath = assetPath,
                        Line = lineNumber,
                        IsGlobal = false,
                        ResolutionNote =
                            $"'{receiver}' was assigned from CreateInstance(\"{eventKey}\") in this file.",
                    });

                    continue;
                }

                // Reporting this against a guessed event would be worse than not
                // reporting it: R007 is an Error-level rule, and a wrong Error is how a
                // validator gets switched off.
                sink.Note(
                    $"Parameter '{parameterName}' is set on '{receiver}', whose event could not be " +
                    "determined from this file. Assign the instance from " +
                    "RuntimeManager.CreateInstance(\"event:/...\") in the same file to have it checked.",
                    assetPath,
                    lineNumber);
            }

            foreach (Match match in EmitterSetParameter.Matches(line))
            {
                sink.Note(
                    $"Parameter '{match.Groups["param"].Value}' is set on emitter " +
                    $"'{match.Groups["recv"].Value}' in code. Which event that emitter holds is " +
                    "decided in the scene, so this call was not checked against an event.",
                    assetPath,
                    lineNumber);
            }
        }

        private static void NoteComposedEventNames(
            string assetPath, string line, int lineNumber, ReferenceSink sink)
        {
            foreach (Match match in ComposedEventArgument.Matches(line))
            {
                var argument = match.Groups["arg"].Value;

                if (!IsComposedString(argument))
                {
                    continue;
                }

                sink.Note(
                    "An event name is assembled from parts here, so it could not be resolved " +
                    "statically. Dynamically built event names are a documented limit of this scan.",
                    assetPath,
                    lineNumber);
            }
        }

        /// <summary>
        /// True for interpolated or concatenated strings only. A bare
        /// <c>PlayOneShot(myEventReference)</c> is the recommended pattern and is already
        /// covered by the serialized-field pass, so flagging it would be pure noise.
        /// </summary>
        private static bool IsComposedString(string argument) =>
            argument.Contains("$\"") ||
            argument.Contains("\" +") ||
            argument.Contains("+ \"") ||
            argument.Contains("string.Format") ||
            argument.Contains("string.Concat");

        private static Dictionary<string, string> MapInstanceVariables(string[] lines)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var line in lines)
            {
                if (IsComment(line))
                {
                    continue;
                }

                foreach (Match match in InstanceAssignment.Matches(line))
                {
                    if (IsFragment(line, match, "key"))
                    {
                        continue;
                    }

                    // Last assignment wins, matching what the code would actually do at
                    // the point most parameter calls appear.
                    map[match.Groups["var"].Value] = match.Groups["key"].Value;
                }
            }

            return map;
        }

        /// <summary>
        /// True when the captured literal is only one piece of a concatenation, so its
        /// text is not the value the call actually receives.
        /// </summary>
        private static bool IsFragment(string line, Match match, string groupName)
        {
            var group = match.Groups[groupName];
            return CodeLiteral.IsConcatenated(line, group.Index, group.Length);
        }

        private static bool IsComment(string line)
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("//", StringComparison.Ordinal) ||
                   trimmed.StartsWith("*", StringComparison.Ordinal);
        }
    }
}
