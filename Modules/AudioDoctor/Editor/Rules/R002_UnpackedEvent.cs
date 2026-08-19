using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// An event exists in the middleware project but is not in any bank, so nothing
    /// ships it and it cannot play in a build.
    /// </summary>
    /// <remarks>
    /// Only reported for events the Unity project actually references. An unassigned
    /// event nobody uses is work in progress, not a defect, and reporting it would put
    /// a designer's scratch events in the same list as things that will break the game.
    /// </remarks>
    public sealed class R002_UnpackedEvent : IValidationRule
    {
        public string RuleId => "R002";

        public string Title => "Event is not in any bank";

        public Severity DefaultSeverity => Severity.Error;

        public BackendCapability RequiredCapabilities =>
            BackendCapability.BankMembership | BackendCapability.UnpackedEvents;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            foreach (var authored in context.Events)
            {
                if (string.IsNullOrEmpty(authored.Key))
                {
                    continue;
                }

                var declaresBank = authored.BankNames.Count > 0;
                var appearsInABank = context.PackedEventKeys.Contains(authored.Key);

                if (declaresBank || appearsInABank)
                {
                    continue;
                }

                var usages = context.ReferencesByKey[authored.Key].ToList();

                if (usages.Count == 0)
                {
                    continue;
                }

                yield return context.Issue(
                    this,
                    $"'{authored.Key}' is used by the game but is not packed into any bank",
                    usages[0].AssetPath,
                    $"Referenced from {usages.Count} place(s), but no bank contains it, so it will " +
                    "be missing at runtime and silently fail to play. In the middleware project, " +
                    "assign the event to a bank and rebuild the banks.",
                    usages[0].Line,
                    usages.Select(u => u.AssetPath).Distinct().Skip(1).ToList());
            }
        }
    }
}
