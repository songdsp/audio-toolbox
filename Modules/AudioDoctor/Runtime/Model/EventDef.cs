using System;
using System.Collections.Generic;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// One event as the middleware project declares it, normalized across backends.
    /// </summary>
    /// <remarks>
    /// The nullable fields are deliberate: Wwise and FMOD do not expose the same
    /// capability set, so this model is the union rather than the lowest common
    /// denominator. A backend that cannot supply a value leaves it null and clears
    /// the matching <see cref="BackendCapability"/> flag; rules must skip rather
    /// than guess. Backend-specific concepts that map to nothing here go into
    /// <see cref="Extras"/> verbatim and are surfaced in the UI — never dropped.
    /// </remarks>
    [Serializable]
    public sealed class EventDef
    {
        /// <summary>Normalized identifier — a path ("event:/UI/Click") or a name.</summary>
        public string Key;

        /// <summary>The backend's own native id: FMOD GUID, Wwise ShortID.</summary>
        public string BackendId;

        /// <summary>Null when the backend cannot report spatialization.</summary>
        public bool? Is3D;

        /// <summary>Null when the backend cannot report the loading mode.</summary>
        public bool? IsStreaming;

        /// <summary>Null when the backend cannot report a length.</summary>
        public float? LengthSeconds;

        /// <summary>RTPC / Switch / State / FMOD parameter, all collected here.</summary>
        public IReadOnlyList<string> Parameters = Array.Empty<string>();

        /// <summary>Names of the banks this event is packed into. Empty means unpacked.</summary>
        public IReadOnlyList<string> BankNames = Array.Empty<string>();

        /// <summary>Backend-specific extras, passed through to the UI untouched.</summary>
        public IReadOnlyDictionary<string, string> Extras = EmptyExtras;

        internal static readonly IReadOnlyDictionary<string, string> EmptyExtras =
            new Dictionary<string, string>(0);

        public override string ToString() => Key ?? "<null event>";
    }
}
