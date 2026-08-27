namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// The knobs that decide what a trace session costs.
    /// </summary>
    /// <remarks>
    /// Apply with <see cref="AudioTrace.Configure"/> before the first post; afterwards
    /// the buffers exist and changing their size would mean throwing away a session in
    /// progress. Defaults are chosen against the module's memory budget: 50,000 records
    /// at 68 bytes each is roughly 3.4 MB, comfortably inside the 8 MB the design
    /// allows for, with room for the intern table and signal queue on top.
    /// </remarks>
    public struct AudioTraceSettings
    {
        /// <summary>Records held in memory. Oldest are overwritten once full, and counted.</summary>
        public int RecordCapacity;

        /// <summary>How many sounds may be in flight at once. A post beyond this is refused and counted.</summary>
        public int MaxConcurrentVoices;

        /// <summary>Backend signals that may be in flight between the mixer thread and ours.</summary>
        public int SignalQueueCapacity;

        /// <summary>Distinct strings a session may intern. Beyond it, ids become "(intern table full)".</summary>
        public int InternCapacity;

        /// <summary>
        /// Distinct emitters whose scene path is remembered. Past it, records say
        /// "(intern table full)" for the path rather than paying a string build per post.
        /// </summary>
        public int EmitterPathCapacity;

        /// <summary>
        /// Distinct global parameters a session will track. A project with more than this
        /// many is unusual; the rest are counted as dropped rather than silently ignored.
        /// </summary>
        public int MaxTrackedParameters;

        /// <summary>
        /// Parameter snapshots that may be staged between two flushes. Only states that
        /// actually differ consume one, so this is far larger than it looks.
        /// </summary>
        public int PendingSnapshotCapacity;

        /// <summary>
        /// Seconds between polls of the backend for global parameter values.
        /// </summary>
        /// <remarks>
        /// Parameters set through the facade are recorded the instant they are set, so
        /// this interval only governs how quickly the tracer notices a parameter that some
        /// other code changed behind its back. Zero means no interval — a poll every
        /// frame. A <em>negative</em> value turns polling off entirely and leaves the
        /// facade as the only source, which is the setting for a project that does not
        /// want the tracer talking to its middleware on a timer.
        /// </remarks>
        public float GlobalParameterSampleIntervalSeconds;

        /// <summary>
        /// Whether finished records are written to a .adtrace file under
        /// <c>Application.persistentDataPath</c>. Off leaves the session in memory only,
        /// which is what the performance tests want.
        /// </summary>
        public bool WriteToDisk;

        /// <summary>Seconds between background flushes. Only finished voices are written.</summary>
        public float FlushIntervalSeconds;

        /// <summary>
        /// How much later than an event's own length a stop may arrive and still count
        /// as the sound simply ending. Absorbs the frame or two between the middleware's
        /// callback and our reading of it; too small and every natural end looks stolen.
        /// </summary>
        public double NaturalEndToleranceSeconds;

        public static AudioTraceSettings Default => new AudioTraceSettings
        {
            RecordCapacity = 50_000,
            MaxConcurrentVoices = 512,
            SignalQueueCapacity = 4096,
            InternCapacity = 8192,
            EmitterPathCapacity = 4096,
            MaxTrackedParameters = 256,
            PendingSnapshotCapacity = 1024,
            GlobalParameterSampleIntervalSeconds = 0.25f,
            WriteToDisk = true,
            FlushIntervalSeconds = 2f,
            NaturalEndToleranceSeconds = 0.1,
        };

        /// <summary>Clamps anything nonsensical rather than throwing during a game's startup.</summary>
        public AudioTraceSettings Sanitized()
        {
            var result = this;

            if (result.RecordCapacity < 64) { result.RecordCapacity = 64; }
            if (result.MaxConcurrentVoices < 8) { result.MaxConcurrentVoices = 8; }
            if (result.SignalQueueCapacity < 64) { result.SignalQueueCapacity = 64; }
            if (result.InternCapacity < 16) { result.InternCapacity = 16; }
            if (result.EmitterPathCapacity < 16) { result.EmitterPathCapacity = 16; }
            if (result.MaxTrackedParameters < 1) { result.MaxTrackedParameters = 1; }
            if (result.PendingSnapshotCapacity < 8) { result.PendingSnapshotCapacity = 8; }
            if (result.FlushIntervalSeconds < 0.1f) { result.FlushIntervalSeconds = 0.1f; }
            if (result.NaturalEndToleranceSeconds < 0) { result.NaturalEndToleranceSeconds = 0; }

            return result;
        }
    }
}
