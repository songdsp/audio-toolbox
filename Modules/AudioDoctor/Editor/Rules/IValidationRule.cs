using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// One check. Takes the normalized snapshot, returns findings.
    /// </summary>
    /// <remarks>
    /// A rule may only read <see cref="RuleContext"/>. It must not touch the
    /// AssetDatabase, the middleware, or the filesystem — that is what makes the
    /// whole rule set unit-testable from hand-written arrays on a machine with no
    /// middleware installed, which is the single largest payoff of the three-layer
    /// architecture.
    /// </remarks>
    public interface IValidationRule
    {
        /// <summary>"R001" … "R009". Stable; it appears in reports and CI output.</summary>
        string RuleId { get; }

        /// <summary>Short name shown in the UI.</summary>
        string Title { get; }

        Severity DefaultSeverity { get; }

        /// <summary>
        /// Data this rule cannot work without. The engine skips the rule — and says
        /// so in the report — when the backend does not supply it, rather than
        /// letting the rule guess and produce noise.
        /// </summary>
        BackendCapability RequiredCapabilities { get; }

        IEnumerable<ValidationIssue> Evaluate(RuleContext context);
    }
}
