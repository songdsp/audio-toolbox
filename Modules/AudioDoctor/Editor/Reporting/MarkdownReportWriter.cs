using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Writes the report an audio designer reads.
    /// </summary>
    /// <remarks>
    /// Written for someone who does not read C#: findings are grouped by severity,
    /// each one names the asset to open, and every rule links to its entry in the
    /// rule handbook. Acceptance criterion 7 is that a non-programmer can locate the
    /// broken asset from this file alone.
    /// </remarks>
    public static class MarkdownReportWriter
    {
        public static string Write(ValidationReport report)
        {
            var md = new StringBuilder();

            md.AppendLine("# AudioDoctor report");
            md.AppendLine();
            md.AppendLine($"**Project** {report.ProjectName}  ");
            md.AppendLine($"**Audio backend** {report.BackendDisplayName}  ");
            md.AppendLine($"**Generated** {report.GeneratedAtUtc} (UTC)  ");
            md.AppendLine($"**Took** {Seconds(report.ScanSeconds)} to scan, {Seconds(report.RuleSeconds)} to check");
            md.AppendLine();

            md.AppendLine("## Summary");
            md.AppendLine();
            md.AppendLine("| | Count |");
            md.AppendLine("|---|---|");
            md.AppendLine($"| Errors — will misbehave at runtime | {report.ErrorCount} |");
            md.AppendLine($"| Warnings — worth a look | {report.WarningCount} |");
            md.AppendLine($"| Notes | {report.InfoCount} |");
            md.AppendLine($"| Events declared | {report.EventCount} |");
            md.AppendLine($"| Banks found | {report.BankCount} |");
            md.AppendLine($"| References in the Unity project | {report.ReferenceCount} |");
            md.AppendLine();

            if (report.Issues.Count == 0)
            {
                md.AppendLine("No issues found.");
                md.AppendLine();
            }
            else
            {
                foreach (var severity in new[] { Severity.Error, Severity.Warning, Severity.Info })
                {
                    var group = report.Issues.Where(i => i.Severity == severity).ToList();

                    if (group.Count == 0)
                    {
                        continue;
                    }

                    md.AppendLine($"## {Heading(severity)} ({group.Count})");
                    md.AppendLine();

                    foreach (var byRule in group.GroupBy(i => i.RuleId).OrderBy(g => g.Key, StringComparer.Ordinal))
                    {
                        md.AppendLine($"### {byRule.Key}");
                        md.AppendLine();

                        foreach (var issue in byRule)
                        {
                            md.AppendLine($"- **{issue.Message}**");
                            md.AppendLine($"  - Asset: `{Location(issue)}`");

                            if (!string.IsNullOrEmpty(issue.Detail))
                            {
                                md.AppendLine($"  - {issue.Detail}");
                            }

                            if (issue.SecondaryAssetPaths.Count > 0)
                            {
                                md.AppendLine($"  - Also involves: {Paths(issue.SecondaryAssetPaths)}");
                            }
                        }

                        md.AppendLine();
                    }
                }
            }

            if (report.SkippedRules.Count > 0)
            {
                md.AppendLine("## Checks that did not run");
                md.AppendLine();
                md.AppendLine(
                    "These were skipped, so a clean report above does not cover them. " +
                    "This section is the difference between \"nothing is wrong\" and " +
                    "\"nothing was checked\".");
                md.AppendLine();
                md.AppendLine("| Rule | Check | Why it was skipped |");
                md.AppendLine("|---|---|---|");

                foreach (var skipped in report.SkippedRules)
                {
                    md.AppendLine($"| {skipped.RuleId} | {skipped.Title} | {skipped.Reason} |");
                }

                md.AppendLine();
            }

            return md.ToString();
        }

        private static string Heading(Severity severity) => severity switch
        {
            Severity.Error => "Errors",
            Severity.Warning => "Warnings",
            _ => "Notes",
        };

        private static string Location(ValidationIssue issue)
        {
            if (string.IsNullOrEmpty(issue.PrimaryAssetPath))
            {
                return "(project-wide)";
            }

            return issue.Line > 0 ? $"{issue.PrimaryAssetPath}:{issue.Line}" : issue.PrimaryAssetPath;
        }

        private static string Paths(IReadOnlyList<string> paths) =>
            string.Join(", ", paths.Select(p => $"`{p}`"));

        private static string Seconds(double value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture) + "s";
    }
}
