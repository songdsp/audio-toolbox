using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// Code that sets a parameter the event does not have.
    /// </summary>
    /// <remarks>
    /// The highest-value rule in the set, because this failure is completely silent at
    /// runtime: no exception, no warning, no log line. The middleware looks the name up,
    /// does not find it, and returns. What the team experiences is "the ducking never
    /// kicks in" — and they go looking in the mixer, the trigger logic and the
    /// automation before anyone suspects a typo in a string.
    ///
    /// It reports only what the scanner could tie to a specific event with certainty.
    /// Calls whose target could not be resolved are surfaced as coverage notes instead;
    /// at Error level a wrong finding costs more than a missing one, because a validator
    /// that cries wolf gets switched off and then catches nothing at all.
    /// </remarks>
    public sealed class R007_UnknownParameter : IValidationRule
    {
        public string RuleId => "R007";

        public string Title => "Parameter does not exist on the event";

        public Severity DefaultSeverity => Severity.Error;

        public BackendCapability RequiredCapabilities => BackendCapability.Parameters;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            foreach (var usage in context.Snapshot.ParameterUsages)
            {
                if (string.IsNullOrEmpty(usage.ParameterName))
                {
                    continue;
                }

                if (usage.IsGlobal)
                {
                    if (context.Supports(BackendCapability.GlobalParameters) &&
                        !context.GlobalParameters.Contains(usage.ParameterName))
                    {
                        yield return context.Issue(
                            this,
                            $"Global parameter '{usage.ParameterName}' does not exist",
                            usage.AssetPath,
                            "This call targets the global parameter scope, but no global parameter " +
                            "by that name is declared in the middleware project. It will be ignored " +
                            "silently at runtime. " + Known(context.GlobalParameters),
                            usage.Line);
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(usage.EventKey) ||
                    !context.EventsByKey.TryGetValue(usage.EventKey, out var authored))
                {
                    // The event itself is missing; R001 reports that and this rule would
                    // only be piling a second, more confusing error onto the same cause.
                    continue;
                }

                if (authored.Parameters.Contains(usage.ParameterName, EventKeyComparer.Exact) ||
                    context.GlobalParameters.Contains(usage.ParameterName))
                {
                    continue;
                }

                var nearMiss = authored.Parameters.FirstOrDefault(
                    p => string.Equals(p, usage.ParameterName, StringComparison.OrdinalIgnoreCase));

                var hint = nearMiss != null
                    ? $"The event declares '{nearMiss}', which differs only by letter case. "
                    : string.Empty;

                yield return context.Issue(
                    this,
                    $"'{usage.EventKey}' has no parameter called '{usage.ParameterName}'",
                    usage.AssetPath,
                    hint +
                    "Setting a parameter that does not exist fails silently — no exception, no log " +
                    "line, the call simply does nothing. " + Known(authored.Parameters) +
                    (string.IsNullOrEmpty(usage.ResolutionNote)
                        ? string.Empty
                        : " Resolved because: " + usage.ResolutionNote),
                    usage.Line);
            }
        }

        private static string Known(IEnumerable<string> names)
        {
            var list = names.ToList();

            return list.Count == 0
                ? "The event declares no parameters at all."
                : "Declared parameters: " + string.Join(", ", list) + ".";
        }
    }
}
