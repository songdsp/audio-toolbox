using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>A rule that did not run, and why. Reported so the output is never silently partial.</summary>
    public sealed class SkippedRule
    {
        public string RuleId;
        public string Title;
        public string Reason;
    }

    /// <summary>The result of one full pass: scan plus rules.</summary>
    public sealed class ValidationReport
    {
        public string BackendId = string.Empty;

        public string BackendDisplayName = string.Empty;

        public string ProjectName = string.Empty;

        /// <summary>UTC, ISO 8601. Set by the runner, not by rules.</summary>
        public string GeneratedAtUtc = string.Empty;

        public double ScanSeconds;

        public double RuleSeconds;

        public IReadOnlyList<ValidationIssue> Issues = Array.Empty<ValidationIssue>();

        public IReadOnlyList<SkippedRule> SkippedRules = Array.Empty<SkippedRule>();

        public BackendCapability Capabilities = BackendCapability.None;

        public int EventCount;
        public int BankCount;
        public int ReferenceCount;

        public int CountOf(Severity severity) => Issues.Count(i => i.Severity == severity);

        public int ErrorCount => CountOf(Severity.Error);
        public int WarningCount => CountOf(Severity.Warning);
        public int InfoCount => CountOf(Severity.Info);

        /// <summary>True when at least one issue is at or above <paramref name="threshold"/>.</summary>
        public bool HasAtLeast(Severity threshold) => Issues.Any(i => i.Severity >= threshold);
    }
}
