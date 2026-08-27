using System;

namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// What a log needs to say about itself for someone else's machine to make sense of it.
    /// </summary>
    /// <remarks>
    /// A trace is most useful when it came from a build you are not holding: QA hits a
    /// silent gunshot on a console, mails a file, and someone opens it a week later. By
    /// then nobody remembers which Unity version, which platform or which middleware
    /// build produced it, so the file says so itself.
    /// <para>
    /// <see cref="DroppedRecordCount"/> lives here rather than being inferred, because a
    /// truncated session that looks complete is worse than one that admits it lost data.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class TraceSessionHeader
    {
        public int FormatVersion = TraceFormat.Version;

        /// <summary>UTC, ISO-8601. When the session started, not when it was written.</summary>
        public string StartedUtc = string.Empty;

        public string UnityVersion = string.Empty;
        public string Platform = string.Empty;
        public string ApplicationVersion = string.Empty;

        /// <summary>"fmod", "native" — matches <see cref="IAudioRuntimeProbe.BackendId"/>.</summary>
        public string BackendId = string.Empty;

        /// <summary>The middleware's version string, as the middleware reports it.</summary>
        public string BackendVersion = string.Empty;

        /// <summary>Ring buffer capacity in records, so a reader can tell "full" from "all of it".</summary>
        public int RecordCapacity;

        /// <summary>Records the ring buffer overwrote before they could be written. Must be surfaced, never swallowed.</summary>
        public long DroppedRecordCount;

        /// <summary>Backend signals dropped because the callback queue was full. Same reasoning.</summary>
        public long DroppedSignalCount;

        /// <summary>Strings that did not fit the intern table. Their records carry <see cref="TraceFormat.OverflowStringId"/>.</summary>
        public long DroppedStringCount;

        /// <summary>Records actually present in the file.</summary>
        public int RecordCount;

        public bool IsTruncated => DroppedRecordCount > 0;
    }
}
