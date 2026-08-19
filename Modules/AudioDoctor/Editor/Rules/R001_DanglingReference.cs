using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// The Unity project asks for an event the middleware project does not declare.
    /// </summary>
    /// <remarks>
    /// Reported once per usage site rather than once per event, because the fix is
    /// always at the usage site and a reader needs to be taken to each one. When a
    /// case-insensitive match exists the detail says so: a reference that differs only
    /// by case is a different bug with a different fix, and telling the two apart is
    /// most of the work of resolving it.
    /// </remarks>
    public sealed class R001_DanglingReference : IValidationRule
    {
        public string RuleId => "R001";

        public string Title => "Dangling reference";

        public Severity DefaultSeverity => Severity.Error;

        public BackendCapability RequiredCapabilities => BackendCapability.None;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            if (context.Events.Count == 0)
            {
                // With nothing authored, every reference would be dangling. That is a
                // broken scan, not a broken project, and R000 already says so.
                yield break;
            }

            var caseInsensitive = context.Events
                .Where(e => !string.IsNullOrEmpty(e.Key))
                .GroupBy(e => e.Key, EventKeyComparer.CaseInsensitive)
                .ToDictionary(g => g.Key, g => g.First().Key, EventKeyComparer.CaseInsensitive);

            foreach (var usage in context.References)
            {
                if (string.IsNullOrEmpty(usage.EventKey) || context.EventsByKey.ContainsKey(usage.EventKey))
                {
                    continue;
                }

                var hasCaseVariant = caseInsensitive.TryGetValue(usage.EventKey, out var authored);

                var detail = hasCaseVariant
                    ? $"The middleware project declares '{authored}', which differs from the " +
                      $"reference only by letter case. This may still play in the editor, but bank " +
                      "lookups are case-sensitive on some platforms, so it can break in a build on " +
                      "one operating system while working on another. Correct the reference to match."
                    : "No event with this key exists in the middleware project. Either the event was " +
                      "renamed or deleted after this reference was authored, or the reference has a " +
                      "typo. Open the asset below and repoint it at an existing event.";

                yield return context.Issue(
                    this,
                    $"'{usage.EventKey}' is referenced but does not exist",
                    usage.AssetPath,
                    Describe(usage) + " " + detail,
                    usage.Line);
            }
        }

        private static string Describe(EventRefUsage usage)
        {
            var where = usage.Source switch
            {
                RefSource.CodeLiteral => "Referenced in code",
                RefSource.Timeline => "Referenced by a Timeline clip",
                RefSource.AnimationEvent => "Referenced by an AnimationEvent",
                _ => "Referenced by a serialized field",
            };

            return string.IsNullOrEmpty(usage.ObjectPath)
                ? where + "."
                : $"{where} on '{usage.ObjectPath}'.";
        }
    }
}
