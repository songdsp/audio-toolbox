namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// The constants the on-disk trace format is made of.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from both the writer and the reader. The writer ships in
    /// the player behind <c>AUDIOTOOLBOX_TRACE</c>; the reader lives in the editor
    /// assembly and must work whether or not this project has tracing switched on,
    /// because logs arrive from other people's builds. Sharing the constants rather
    /// than the code is what keeps those two from drifting apart.
    /// </remarks>
    public static class TraceFormat
    {
        /// <summary>"ADTR" little-endian. First four bytes of every .adtrace file.</summary>
        public const uint Magic = 0x52544441;

        /// <summary>
        /// Bumped whenever <see cref="AudioTraceRecord"/> or the section layout changes.
        /// The reader refuses a version it does not know rather than misreading it.
        /// </summary>
        public const int Version = 2;

        /// <summary>
        /// The oldest version this reader still understands.
        /// </summary>
        /// <remarks>
        /// Version 2 added the parameter and snapshot chunks. It did not touch the record
        /// layout, so a version 1 log reads back correctly — every record in it simply
        /// carries <see cref="NoSnapshotId"/>, which is the truth about a session that had
        /// no parameter capture. Refusing those logs would throw away working data to
        /// enforce a number.
        /// </remarks>
        public const int MinReadableVersion = 1;

        public const string FileExtension = ".adtrace";

        /// <summary>Intern id meaning "no string", distinct from "the empty string".</summary>
        public const int NoStringId = -1;

        /// <summary>Intern id handed out once the table is full. Its value reads as "(intern table full)".</summary>
        public const int OverflowStringId = -2;

        /// <summary>Snapshot id meaning "no parameter snapshot was taken".</summary>
        public const int NoSnapshotId = -1;

        /// <summary>Text substituted for <see cref="OverflowStringId"/> when a log is read back.</summary>
        public const string OverflowStringText = "(intern table full)";

        /// <summary>
        /// A file is a magic number, a version, and then a stream of tagged chunks.
        /// </summary>
        /// <remarks>
        /// Chunked rather than sectioned because the sessions that matter most are the
        /// ones that ended badly. A layout with a table of contents at the end is
        /// unreadable after a crash — which is precisely when someone most wants to see
        /// what the audio system was doing. Here, a truncated file loses its last chunk
        /// and nothing else, and because every string is emitted before the records that
        /// reference it, what survives still resolves.
        /// </remarks>
        public static class ChunkTag
        {
            /// <summary>An intern table entry: id, then a length-prefixed UTF-8 string.</summary>
            public const byte String = 1;

            /// <summary>One <see cref="AudioTraceRecord"/>, fields in declaration order.</summary>
            public const byte Record = 2;

            /// <summary>
            /// The session header as JSON. Written once at the start and again at close;
            /// the last one wins, so a clean shutdown gets accurate counts and a crash
            /// still leaves the opening one.
            /// </summary>
            public const byte Session = 3;

            /// <summary>
            /// A parameter slot: its index, then the intern id of its name. Emitted once,
            /// before any snapshot that mentions the slot.
            /// </summary>
            public const byte Parameter = 4;

            /// <summary>
            /// One parameter snapshot: its id, the id it differs from (or
            /// <see cref="NoSnapshotId"/> for the first), a count, then that many pairs of
            /// slot index and value. A reader walks the parent chain to rebuild the full
            /// set of parameters in force when a record was written.
            /// </summary>
            public const byte Snapshot = 5;
        }
    }
}
