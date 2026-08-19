using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>Writes the machine-readable report that CI consumes.</summary>
    public static class JsonReportWriter
    {
        /// <summary>Bump when the shape changes so consumers can branch on it.</summary>
        public const int SchemaVersion = 1;

        public static string Write(ValidationReport report)
        {
            var json = new JsonWriter();

            json.BeginObject()
                .Property("schemaVersion", SchemaVersion)
                .Property("generatedAtUtc", report.GeneratedAtUtc)
                .Property("project", report.ProjectName)
                .Property("backend", report.BackendId)
                .Property("backendDisplayName", report.BackendDisplayName)
                .Property("capabilities", report.Capabilities.ToString())
                .Property("scanSeconds", report.ScanSeconds)
                .Property("ruleSeconds", report.RuleSeconds);

            json.Name("counts").BeginObject()
                .Property("events", report.EventCount)
                .Property("banks", report.BankCount)
                .Property("references", report.ReferenceCount)
                .Property("errors", report.ErrorCount)
                .Property("warnings", report.WarningCount)
                .Property("infos", report.InfoCount)
                .EndObject();

            json.Name("issues").BeginArray();

            foreach (var issue in report.Issues)
            {
                json.BeginObject()
                    .Property("ruleId", issue.RuleId)
                    .Property("severity", issue.Severity.ToString())
                    .Property("message", issue.Message)
                    .Property("assetPath", issue.PrimaryAssetPath)
                    .Property("line", issue.Line)
                    .Property("detail", issue.Detail);

                if (issue.SecondaryAssetPaths.Count > 0)
                {
                    json.Name("relatedAssets").BeginArray();

                    foreach (var path in issue.SecondaryAssetPaths)
                    {
                        json.Value(path);
                    }

                    json.EndArray();
                }

                json.EndObject();
            }

            json.EndArray();

            // Skipped rules are part of the payload, not a footnote: a CI job that
            // sees zero errors needs to be able to tell "nothing is wrong" from
            // "half the rules never ran".
            json.Name("skippedRules").BeginArray();

            foreach (var skipped in report.SkippedRules)
            {
                json.BeginObject()
                    .Property("ruleId", skipped.RuleId)
                    .Property("title", skipped.Title)
                    .Property("reason", skipped.Reason)
                    .EndObject();
            }

            json.EndArray();
            json.EndObject();

            return json.ToString() + "\n";
        }
    }
}
