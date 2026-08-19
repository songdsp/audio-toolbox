using System;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>One place in the Unity project that refers to an audio event.</summary>
    [Serializable]
    public sealed class EventRefUsage
    {
        /// <summary>The event key as written at the usage site — not yet resolved.</summary>
        public string EventKey;

        /// <summary>Prefab, scene, .cs, .playable or .anim asset path.</summary>
        public string AssetPath;

        /// <summary>GameObject hierarchy path inside the asset. Null when not applicable.</summary>
        public string ObjectPath;

        /// <summary>How this usage was collected.</summary>
        public RefSource Source;

        /// <summary>1-based line number; 0 when the source is not a text file.</summary>
        public int Line;

        /// <summary>
        /// True when the call site cannot be spatialized — a positionless PlayOneShot,
        /// for example. Null when the scanner cannot tell. Feeds R006.
        /// </summary>
        public bool? IsSpatializedCallSite;

        public override string ToString() =>
            $"{EventKey} @ {AssetPath}{(Line > 0 ? ":" + Line : string.Empty)}";
    }
}
