using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// An event ships inside a bank but nothing in the Unity project references it.
    /// </summary>
    /// <remarks>
    /// A Warning rather than an Error, and deliberately so: this is the one rule whose
    /// evidence is an absence, and a static scan cannot see an event fetched by a name
    /// built at runtime. When the scan admitted to any unresolved dynamic name, that
    /// admission is repeated in every finding here, because otherwise a designer would
    /// delete audio that is genuinely in use.
    /// </remarks>
    public sealed class R004_OrphanEvent : IValidationRule
    {
        public string RuleId => "R004";

        public string Title => "Event is in a bank but unused";

        public Severity DefaultSeverity => Severity.Warning;

        public BackendCapability RequiredCapabilities => BackendCapability.BankMembership;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            if (context.References.Count == 0)
            {
                // Nothing was found to reference anything. Reporting every packed event
                // as an orphan would be technically true and completely useless.
                yield break;
            }

            var caveat = context.Snapshot.Notes.Count > 0
                ? " Note that this scan could not resolve every reference - see the notes section - " +
                  "so confirm the event is genuinely unused before removing it."
                : string.Empty;

            foreach (var authored in context.Events)
            {
                if (string.IsNullOrEmpty(authored.Key))
                {
                    continue;
                }

                var isPacked = authored.BankNames.Count > 0 ||
                               context.PackedEventKeys.Contains(authored.Key);

                if (!isPacked || context.ReferencesByKey[authored.Key].Any())
                {
                    continue;
                }

                var banks = authored.BankNames.Count > 0
                    ? string.Join(", ", authored.BankNames)
                    : string.Join(", ", context.Banks
                        .Where(b => b.EventKeys.Contains(authored.Key, EventKeyComparer.Exact))
                        .Select(b => b.Name)
                        .Distinct());

                yield return context.Issue(
                    this,
                    $"'{authored.Key}' ships in a bank but nothing references it",
                    primaryAssetPath: null,
                    detail:
                    $"Packed into: {banks}. No prefab, scene, script, Timeline clip or AnimationEvent " +
                    "in this project asks for it, so it costs download size and memory for nothing. " +
                    "Either wire it up or remove it from the bank." + caveat);
            }
        }
    }
}
