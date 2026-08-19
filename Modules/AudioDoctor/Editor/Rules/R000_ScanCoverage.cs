using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// Turns the scanner's own admissions into visible findings.
    /// </summary>
    /// <remarks>
    /// Every static scanner has blind spots — an event name assembled from a
    /// variable, an empty middleware cache, a scene that failed to open. Leaving
    /// those in the console makes a clean report look like proof of a clean project.
    /// Putting them in the report as Info makes the coverage of the scan part of
    /// the deliverable, which is the honest thing to hand an audio designer.
    /// </remarks>
    public sealed class R000_ScanCoverage : IValidationRule
    {
        public string RuleId => "R000";

        public string Title => "Scan coverage";

        public Severity DefaultSeverity => Severity.Info;

        public BackendCapability RequiredCapabilities => BackendCapability.None;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            foreach (var note in context.Snapshot.Notes)
            {
                yield return context.Issue(
                    this,
                    note.Message,
                    note.AssetPath,
                    "The scanner could not fully cover this. Treat a clean report as clean " +
                    "only for the parts it was able to inspect.",
                    note.Line);
            }
        }
    }
}
