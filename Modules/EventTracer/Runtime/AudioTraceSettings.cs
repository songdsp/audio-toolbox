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
            if (result.FlushIntervalSeconds < 0.1f) { result.FlushIntervalSeconds = 0.1f; }
            if (result.NaturalEndToleranceSeconds < 0) { result.NaturalEndToleranceSeconds = 0; }

            return result;
        }
    }
}
