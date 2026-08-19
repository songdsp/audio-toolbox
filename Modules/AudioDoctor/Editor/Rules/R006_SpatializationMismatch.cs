using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// A 3D event played from a call site that carries no position.
    /// </summary>
    /// <remarks>
    /// FMOD's PlayOneShot defaults its position argument to <c>Vector3.zero</c>, which
    /// is the world origin — not the listener, and not the object that triggered the
    /// sound. A spatialized event played this way is not silent, which is what makes it
    /// slippery: it plays, at the wrong place, and in a small test scene the origin is
    /// often close enough that nobody notices until the level gets big.
    ///
    /// The reverse direction — a 2D event played through a positioned call — is
    /// deliberately not reported. Every StudioEventEmitter is positioned by definition,
    /// so flagging 2D events on emitters would fire on every correctly-built UI sound in
    /// the project. The asymmetry is a real judgement, not an oversight: one direction is
    /// an audible bug, the other is how 2D audio is normally wired.
    /// </remarks>
    public sealed class R006_SpatializationMismatch : IValidationRule
    {
        public string RuleId => "R006";

        public string Title => "3D event played without a position";

        public Severity DefaultSeverity => Severity.Warning;

        public BackendCapability RequiredCapabilities => BackendCapability.SpatialFlag;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            foreach (var usage in context.References)
            {
                if (string.IsNullOrEmpty(usage.EventKey) ||
                    !context.EventsByKey.TryGetValue(usage.EventKey, out var authored))
                {
                    // An unresolvable reference is R001's finding, not this rule's.
                    continue;
                }

                // Only a call site that is known to lack a position counts. Null means the
                // scanner could not tell, and guessing would put noise at Warning level.
                if (authored.Is3D != true || usage.IsSpatializedCallSite != false)
                {
                    continue;
                }

                yield return context.Issue(
                    this,
                    $"'{usage.EventKey}' is a 3D event but is played with no position",
                    usage.AssetPath,
                    "The event is spatialized, so it is meant to come from somewhere. This call " +
                    "gives no position, so it plays at the world origin instead of at the object " +
                    "that triggered it — audible, but from the wrong place. Pass a position, use " +
                    "PlayOneShotAttached to follow a GameObject, or put the event on a " +
                    "StudioEventEmitter.",
                    usage.Line);
            }
        }
    }
}
