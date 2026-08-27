namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// A reference to one playing sound, valid until that sound ends.
    /// </summary>
    /// <remarks>
    /// A struct holding two ints rather than the middleware's own handle type, so that
    /// game code can hold one without referencing FMOD or Wwise, and so that holding
    /// one costs no allocation.
    /// <para>
    /// The generation counter is the reason this is not just an index. Voice slots are
    /// recycled, and a handle kept past the end of its sound would otherwise silently
    /// address whatever took the slot next — stopping a stranger's footstep instead of
    /// your own engine loop. A stale handle is inert here, not wrong.
    /// </para>
    /// </remarks>
    public readonly struct AudioTraceHandle
    {
        internal readonly int VoiceId;
        internal readonly int Generation;

        internal AudioTraceHandle(int voiceId, int generation)
        {
            VoiceId = voiceId;
            Generation = generation;
        }

        /// <summary>The handle returned when a sound could not be posted at all.</summary>
        public static AudioTraceHandle Invalid => new AudioTraceHandle(-1, 0);

        /// <summary>
        /// True when this handle ever referred to a sound. Says nothing about whether
        /// that sound is still playing — use <see cref="AudioTrace.IsAlive"/> for that.
        /// </summary>
        public bool IsValid => VoiceId >= 0;

        public override string ToString() => IsValid ? $"voice {VoiceId}.{Generation}" : "voice <invalid>";
    }
}
