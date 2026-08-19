using System;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// A call that sets an audio parameter, linked back to the event it targets.
    /// The input to R007 — the rule that catches failures which are completely
    /// silent at runtime.
    /// </summary>
    [Serializable]
    public sealed class ParameterUsage
    {
        /// <summary>Event this call targets. Null when <see cref="IsGlobal"/>.</summary>
        public string EventKey;

        public string ParameterName;

        public string AssetPath;

        /// <summary>1-based line number; 0 when not a text file.</summary>
        public int Line;

        /// <summary>True for a global/system-scope parameter, which has no owning event.</summary>
        public bool IsGlobal;

        /// <summary>
        /// How the scanner tied this call to its event — "local variable 'inst'
        /// assigned at line 12", "StudioEventEmitter on Player/Footsteps".
        /// Reported alongside the issue so a reader can judge the heuristic.
        /// </summary>
        public string ResolutionNote;

        public override string ToString() =>
            $"{(IsGlobal ? "<global>" : EventKey)}.{ParameterName} @ {AssetPath}:{Line}";
    }
}
