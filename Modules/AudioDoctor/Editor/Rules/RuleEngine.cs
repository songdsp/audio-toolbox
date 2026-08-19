using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using UnityEditor;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>Discovers rules, runs the enabled ones, collects the findings.</summary>
    public static class RuleEngine
    {
        /// <summary>Every compiled rule, ordered by id.</summary>
        public static IReadOnlyList<IValidationRule> DiscoverRules()
        {
            var rules = new List<IValidationRule>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<IValidationRule>())
            {
                if (!TypeDiscovery.IsProductionType(type))
                {
                    continue;
                }

                var rule = TypeDiscovery.TryCreate<IValidationRule>(type, "Rule");

                if (rule != null)
                {
                    rules.Add(rule);
                }
            }

            return rules.OrderBy(r => r.RuleId, StringComparer.Ordinal).ToList();
        }

        public static void Run(
            RuleContext context,
            IReadOnlyList<IValidationRule> rules,
            out IReadOnlyList<ValidationIssue> issues,
            out IReadOnlyList<SkippedRule> skipped)
        {
            var found = new List<ValidationIssue>();
            var notRun = new List<SkippedRule>();

            foreach (var rule in rules)
            {
                if (!context.Settings.IsEnabled(rule.RuleId))
                {
                    notRun.Add(new SkippedRule
                    {
                        RuleId = rule.RuleId,
                        Title = rule.Title,
                        Reason = "Disabled in the rule set.",
                    });
                    continue;
                }

                var missing = rule.RequiredCapabilities & ~context.Snapshot.Capabilities;
                if (missing != BackendCapability.None)
                {
                    notRun.Add(new SkippedRule
                    {
                        RuleId = rule.RuleId,
                        Title = rule.Title,
                        Reason =
                            $"Backend '{context.Snapshot.BackendId}' does not provide: {missing}. " +
                            "Reporting nothing beats reporting a guess.",
                    });
                    continue;
                }

                try
                {
                    found.AddRange(rule.Evaluate(context).Where(i => i != null));
                }
                catch (Exception e)
                {
                    // One broken rule must not take down the whole pass.
                    Debug.LogError($"[AudioDoctor] Rule {rule.RuleId} threw: {e}");
                    notRun.Add(new SkippedRule
                    {
                        RuleId = rule.RuleId,
                        Title = rule.Title,
                        Reason = $"Threw an exception: {e.GetType().Name}: {e.Message}",
                    });
                }
            }

            issues = Sort(found);
            skipped = notRun;
        }

        /// <summary>Worst first, then stable by rule, asset and line so reports diff cleanly.</summary>
        public static IReadOnlyList<ValidationIssue> Sort(IEnumerable<ValidationIssue> issues) =>
            issues
                .OrderByDescending(i => i.Severity)
                .ThenBy(i => i.RuleId, StringComparer.Ordinal)
                .ThenBy(i => i.PrimaryAssetPath, StringComparer.Ordinal)
                .ThenBy(i => i.Line)
                .ThenBy(i => i.Message, StringComparer.Ordinal)
                .ToList();
    }
}
