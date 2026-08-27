namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// Hands out and recycles the fixed set of voice slots the facade addresses sounds by.
    /// </summary>
    /// <remarks>
    /// A pre-allocated pool rather than a dictionary keyed by the middleware's handle,
    /// because every lookup on the collection path has to cost nothing. Slot ids are
    /// small dense integers, which is what lets the recorder keep its per-voice state
    /// in plain arrays.
    /// <para>
    /// Running out of slots is reported, never papered over: a game leaking voices
    /// because it holds looping sounds forever should find that out from the trace
    /// rather than from sounds mysteriously failing to start later on.
    /// </para>
    /// </remarks>
    internal sealed class VoiceRegistry
    {
        private readonly int[] _generation;
        private readonly bool[] _inUse;
        private readonly int[] _free;

        private int _freeCount;
        private long _starvedCount;

        public VoiceRegistry(int capacity)
        {
            Capacity = capacity < 1 ? 1 : capacity;

            _generation = new int[Capacity];
            _inUse = new bool[Capacity];
            _free = new int[Capacity];

            for (var i = 0; i < Capacity; i++)
            {
                // Filled back to front so the first slots handed out are 0, 1, 2 —
                // which makes a log far easier to read while debugging the tracer itself.
                _free[i] = Capacity - 1 - i;
                _generation[i] = 1;
            }

            _freeCount = Capacity;
        }

        public int Capacity { get; }

        public int ActiveCount => Capacity - _freeCount;

        /// <summary>Times a post was refused because every slot was taken.</summary>
        public long StarvedCount => _starvedCount;

        public bool TryAcquire(out int voiceId, out int generation)
        {
            if (_freeCount == 0)
            {
                _starvedCount++;
                voiceId = -1;
                generation = 0;
                return false;
            }

            voiceId = _free[--_freeCount];
            _inUse[voiceId] = true;
            generation = _generation[voiceId];
            return true;
        }

        /// <summary>Returns false when the slot was already free — releasing twice is not an error.</summary>
        public bool Release(int voiceId)
        {
            if (voiceId < 0 || voiceId >= Capacity || !_inUse[voiceId])
            {
                return false;
            }

            _inUse[voiceId] = false;

            // Bumped on release so every handle to the sound that just ended stops
            // matching, including ones the game is still holding.
            unchecked
            {
                _generation[voiceId]++;
            }

            if (_generation[voiceId] == 0)
            {
                _generation[voiceId] = 1;
            }

            _free[_freeCount++] = voiceId;
            return true;
        }

        public bool IsAlive(int voiceId, int generation) =>
            voiceId >= 0 &&
            voiceId < Capacity &&
            _inUse[voiceId] &&
            _generation[voiceId] == generation;
    }
}
