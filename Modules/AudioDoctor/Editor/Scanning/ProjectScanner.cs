using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Drives one backend through the three collection passes and assembles the
    /// normalized snapshot the rule engine consumes.
    /// </summary>
    public static class ProjectScanner
    {
        public static AudioProjectSnapshot Scan(IAudioProjectSource backend, ScanContext context)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var notes = new List<ScanNote>();

            if (!backend.IsAvailable)
            {
                // An unusable backend is a finding, not an exception. Returning an
                // empty snapshot with the reason attached keeps the report readable
                // instead of dumping a stack trace on an audio designer.
                return new AudioProjectSnapshot
                {
                    BackendId = backend.BackendId,
                    Capabilities = BackendCapability.None,
                    Notes = new[]
                    {
                        new ScanNote
                        {
                            Message =
                                $"Backend '{backend.DisplayName}' is installed but not usable: " +
                                backend.GetUnavailableReason(),
                        },
                    },
                };
            }

            context.Progress.Report("Authored events", backend.DisplayName, 0f);
            context.ThrowIfCancelled();
            var events = backend.GetAuthoredEvents(context) ?? Array.Empty<EventDef>();

            context.Progress.Report("Banks", backend.DisplayName, 0.15f);
            context.ThrowIfCancelled();
            var banks = backend.GetBanks(context) ?? Array.Empty<BankDef>();

            context.Progress.Report("Global parameters", backend.DisplayName, 0.2f);
            context.ThrowIfCancelled();
            var globals = backend.GetGlobalParameters(context) ?? Array.Empty<string>();

            context.Progress.Report("References", "Walking the project", 0.25f);
            context.ThrowIfCancelled();
            var sink = new ReferenceSink();
            backend.FindReferences(context, sink);

            notes.AddRange(sink.Notes);

            // Half the rules reconcile what is authored against what is used. With no
            // usages found there is nothing to reconcile, and the rules that would have
            // done it return nothing - which prints as "No issues found" and reads as a
            // clean bill of health. Saying so is the whole difference between "nothing
            // is wrong" and "nothing was checked".
            if (sink.References.Count == 0 && events.Count > 0)
            {
                notes.Add(new ScanNote
                {
                    Message =
                        $"This scan found {events.Count} authored event(s) but no reference to any of " +
                        "them anywhere in the project - no prefab, scene, script, Timeline clip or " +
                        "AnimationEvent asks for one. The checks that compare authored events against " +
                        "how they are used had nothing to compare, so a clean result above means " +
                        "'nothing was checked', not 'nothing is wrong'.",
                });
            }

            return new AudioProjectSnapshot
            {
                BackendId = backend.BackendId,
                Events = events,
                Banks = banks,
                References = sink.References,
                ParameterUsages = sink.ParameterUsages,
                BankLoads = sink.BankLoads,
                GlobalParameters = globals.ToList(),
                ScenePaths = sink.ScenePaths,
                Capabilities = backend.Capabilities,
                Notes = notes,
            };
        }
    }
}
