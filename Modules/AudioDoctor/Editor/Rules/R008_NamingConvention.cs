using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AudioToolbox.AudioDoctor.Core;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// Event keys that break the project's naming convention, and pairs of events whose
    /// keys differ only by letter case.
    /// </summary>
    /// <remarks>
    /// The case-collision half is not cosmetic. macOS is case-insensitive by default and
    /// Linux is not, so two events differing only in case coexist happily on the machine
    /// they were authored on and collide on a build server. It is grouped here because
    /// the fix is the same one - rename - and it is caught by looking at names alone.
    /// </remarks>
    public sealed class R008_NamingConvention : IValidationRule
    {
        public string RuleId => "R008";

        public string Title => "Naming convention";

        public Severity DefaultSeverity => Severity.Info;

        public BackendCapability RequiredCapabilities => BackendCapability.None;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            foreach (var issue in EvaluatePattern(context))
            {
                yield return issue;
            }

            foreach (var issue in EvaluateCaseCollisions(context))
            {
                yield return issue;
            }
        }

        private IEnumerable<ValidationIssue> EvaluatePattern(RuleContext context)
        {
            var pattern = context.Settings.EventNamingPattern;

            if (string.IsNullOrWhiteSpace(pattern))
            {
                yield break;
            }

            Regex regex;

            try
            {
                regex = new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (System.ArgumentException e)
            {
                // A broken pattern in the rule set must not take the rule down silently.
                Debug.LogWarning($"[AudioDoctor] R008's naming pattern is not a valid regex: {e.Message}");
                yield break;
            }

            foreach (var authored in context.Events)
            {
                if (string.IsNullOrEmpty(authored.Key) || regex.IsMatch(authored.Key))
                {
                    continue;
                }

                yield return context.Issue(
                    this,
                    $"'{authored.Key}' does not follow the project's event naming convention",
                    primaryAssetPath: null,
                    detail:
                    $"Expected to match: {pattern}. Consistent names are what let a team find, " +
                    "group and automate over events; the pattern is configurable on the rule set asset.");
            }
        }

        private IEnumerable<ValidationIssue> EvaluateCaseCollisions(RuleContext context)
        {
            var collisions = context.Events
                .Where(e => !string.IsNullOrEmpty(e.Key))
                .Select(e => e.Key)
                .Distinct(EventKeyComparer.Exact)
                .GroupBy(key => key, EventKeyComparer.CaseInsensitive)
                .Where(group => group.Count() > 1);

            foreach (var group in collisions)
            {
                var keys = group.OrderBy(k => k, System.StringComparer.Ordinal).ToList();

                yield return context.Issue(
                    this,
                    $"{keys.Count} events have keys that differ only by letter case: {string.Join(", ", keys)}",
                    primaryAssetPath: null,
                    detail:
                    "These are distinct events on a case-sensitive filesystem and the same event on a " +
                    "case-insensitive one. A project authored on macOS or Windows can therefore behave " +
                    "differently on a Linux build machine, and references resolve to whichever one wins. " +
                    "Rename so the keys differ by more than case.");
            }
        }
    }
}
