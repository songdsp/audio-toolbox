using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// Banks that were not built for every platform, or whose names collide by case.
    /// </summary>
    /// <remarks>
    /// The case check is the one that costs teams real days. macOS and Windows are
    /// case-insensitive by default and Linux is not, so two banks differing only in case
    /// coexist happily on the machine they were authored on and collide on a build
    /// server — or resolve to whichever file happens to win. It is the kind of bug that
    /// reproduces on exactly one machine in the studio.
    ///
    /// Cross-platform size deviation is available but off by default. Platforms
    /// legitimately use different encodings, so a bank being half the size on mobile is
    /// normally correct configuration rather than a defect; switching it on by default
    /// would report every well-configured project.
    /// </remarks>
    public sealed class R009_CrossPlatformBanks : IValidationRule
    {
        private const string NotBuilt = "(not built)";

        public string RuleId => "R009";

        public string Title => "Banks differ across platforms";

        public Severity DefaultSeverity => Severity.Error;

        public BackendCapability RequiredCapabilities => BackendCapability.PlatformBanks;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            var groups = context.Banks
                .Where(b => !string.IsNullOrEmpty(b.Name))
                .GroupBy(b => b.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var issue in ReportNeverBuilt(context, groups))
            {
                yield return issue;
            }

            foreach (var issue in ReportMissingPlatforms(context, groups))
            {
                yield return issue;
            }

            foreach (var issue in ReportCaseCollisions(context, groups))
            {
                yield return issue;
            }

            foreach (var issue in ReportSizeDeviation(context, groups))
            {
                yield return issue;
            }
        }

        private IEnumerable<ValidationIssue> ReportNeverBuilt(
            RuleContext context, IReadOnlyList<IGrouping<string, BankDef>> groups)
        {
            foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (group.Any(b => b.Platform != NotBuilt))
                {
                    continue;
                }

                yield return context.Issue(
                    this,
                    $"Bank '{group.Key}' has never been built for any platform",
                    primaryAssetPath: null,
                    detail:
                    "The middleware project declares this bank but no build of it exists, so nothing " +
                    "it contains can ship. Build the banks in the middleware tool.");
            }
        }

        private IEnumerable<ValidationIssue> ReportMissingPlatforms(
            RuleContext context, IReadOnlyList<IGrouping<string, BankDef>> groups)
        {
            var expected = context.Settings.RequiredPlatforms
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (expected.Count == 0)
            {
                // With no list configured, the platforms the other banks were built for
                // are the best available statement of intent.
                expected = context.Banks
                    .Select(b => b.Platform)
                    .Where(p => !string.IsNullOrEmpty(p) && p != NotBuilt)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();
            }

            // A single-platform project has nothing to be inconsistent with.
            if (expected.Count < 2)
            {
                yield break;
            }

            foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var built = group.Select(b => b.Platform).ToList();

                if (built.All(p => p == NotBuilt))
                {
                    continue; // Already reported as never built.
                }

                var missing = expected.Where(p => !built.Contains(p, StringComparer.Ordinal)).ToList();

                if (missing.Count == 0)
                {
                    continue;
                }

                yield return context.Issue(
                    this,
                    $"Bank '{group.Key}' was not built for: {string.Join(", ", missing)}",
                    primaryAssetPath: null,
                    detail:
                    $"Built for {string.Join(", ", built.OrderBy(p => p, StringComparer.Ordinal))}, " +
                    $"while the project builds banks for {string.Join(", ", expected)}. On the " +
                    "missing platform every event in this bank will be absent at runtime. Rebuild " +
                    "the banks with that platform selected.");
            }
        }

        private IEnumerable<ValidationIssue> ReportCaseCollisions(
            RuleContext context, IReadOnlyList<IGrouping<string, BankDef>> groups)
        {
            var collisions = groups
                .Select(g => g.Key)
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var collision in collisions)
            {
                var names = collision.OrderBy(n => n, StringComparer.Ordinal).ToList();

                yield return context.Issue(
                    this,
                    $"Bank names differ only by letter case: {string.Join(", ", names)}",
                    primaryAssetPath: null,
                    detail:
                    "Bank names become filenames. On a case-insensitive filesystem — macOS and " +
                    "Windows by default — these are the same file and one silently overwrites the " +
                    "other; on Linux they are two files. A build that works on the authoring " +
                    "machine can therefore fail on the build server. Rename so the names differ by " +
                    "more than case.");
            }
        }

        private IEnumerable<ValidationIssue> ReportSizeDeviation(
            RuleContext context, IReadOnlyList<IGrouping<string, BankDef>> groups)
        {
            var ratio = context.Settings.BankSizeDeviationRatio;

            if (ratio <= 0f)
            {
                yield break;
            }

            foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var sized = group.Where(b => b.SizeBytes > 0).ToList();

                if (sized.Count < 2)
                {
                    continue;
                }

                var median = Median(sized.Select(b => (double)b.SizeBytes).ToList());

                foreach (var bank in sized.OrderBy(b => b.Platform, StringComparer.Ordinal))
                {
                    var deviation = Math.Abs(bank.SizeBytes - median) / median;

                    if (deviation <= ratio)
                    {
                        continue;
                    }

                    yield return context.Issue(
                        this,
                        $"Bank '{bank.Name}' on {bank.Platform} deviates {deviation:P0} from its " +
                        "size on other platforms",
                        primaryAssetPath: null,
                        detail:
                        $"{bank.SizeBytes:N0} bytes against a cross-platform median of {median:N0}. " +
                        "Different encodings across platforms make some difference expected; a large " +
                        "one usually means that platform's asset settings were never configured. " +
                        "The threshold is on the rule set asset, and setting it to 0 turns this off.");
                }
            }
        }

        private static double Median(List<double> values)
        {
            values.Sort();
            var middle = values.Count / 2;

            return values.Count % 2 == 1
                ? values[middle]
                : (values[middle - 1] + values[middle]) / 2d;
        }
    }
}
