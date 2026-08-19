using System;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// Something the scanner wants to admit about its own coverage — an event name
    /// built from a variable it could not resolve, an empty middleware cache.
    /// Surfaced as Info-level issues so that the limits of the scan are visible in
    /// the report rather than buried in the source.
    /// </summary>
    [Serializable]
    public sealed class ScanNote
    {
        public string Message;
        public string AssetPath;
        public int Line;

        public override string ToString() =>
            $"{Message} ({AssetPath}{(Line > 0 ? ":" + Line : string.Empty)})";
    }
}
