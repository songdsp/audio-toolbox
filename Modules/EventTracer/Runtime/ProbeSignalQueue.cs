using System.Threading;

namespace AudioToolbox.EventTracer
{
    /// <summary>One observation about one voice, as it crosses from a middleware thread to ours.</summary>
    public struct ProbeEvent
    {
        public int VoiceId;
        public ProbeSignal Signal;
        public int ResultCode;

        /// <summary>
        /// <see cref="System.Diagnostics.Stopwatch"/> ticks, taken in the callback
        /// rather than when the event is drained. A stop that arrives a frame late
        /// would otherwise look later than it was, and "did this stop before the event
        /// was over" is decided in milliseconds.
        /// </summary>
        public long Timestamp;

        /// <summary>0 while the slot is being filled, 1 once the consumer may read it.</summary>
        internal int Ready;
    }

    /// <summary>
    /// A fixed-capacity queue carrying backend observations from whichever thread the
    /// middleware calls back on to the main thread.
    /// </summary>
    /// <remarks>
    /// Multiple producers, one consumer, no locks and no allocation. FMOD Studio
    /// dispatches event callbacks from its own update thread, and a callback that
    /// blocked on a lock held by the main thread would stall the mixer — a tracer that
    /// causes audio dropouts has defeated its own purpose.
    /// <para>
    /// When it fills, the newest signal is dropped and counted. Dropping the newest
    /// rather than the oldest is deliberate: the early signals of a voice (created,
    /// started) are what the outcome is built from, while a late one usually only
    /// refines it. The count is surfaced in the session header either way, because
    /// data quietly lost is worse than data visibly lost.
    /// </para>
    /// </remarks>
    public sealed class ProbeSignalQueue
    {
        private readonly ProbeEvent[] _slots;
        private readonly int _mask;

        private long _writeCursor;
        private long _readCursor;
        private long _dropped;

        /// <param name="capacity">Rounded up to a power of two so the cursor can be masked.</param>
        public ProbeSignalQueue(int capacity)
        {
            var rounded = 1;
            while (rounded < capacity)
            {
                rounded <<= 1;
            }

            _slots = new ProbeEvent[rounded];
            _mask = rounded - 1;
        }

        public int Capacity => _slots.Length;

        /// <summary>Signals dropped because the queue was full.</summary>
        public long DroppedCount => Interlocked.Read(ref _dropped);

        /// <summary>Safe to call from any thread. Returns false when the signal was dropped.</summary>
        public bool TryEnqueue(int voiceId, ProbeSignal signal, int resultCode, long timestamp)
        {
            while (true)
            {
                var write = Volatile.Read(ref _writeCursor);
                var read = Volatile.Read(ref _readCursor);

                if (write - read >= _slots.Length)
                {
                    Interlocked.Increment(ref _dropped);
                    return false;
                }

                if (Interlocked.CompareExchange(ref _writeCursor, write + 1, write) != write)
                {
                    // Another producer claimed this slot; look again rather than
                    // spinning on a lock.
                    continue;
                }

                var index = (int)(write & _mask);
                _slots[index].VoiceId = voiceId;
                _slots[index].Signal = signal;
                _slots[index].ResultCode = resultCode;
                _slots[index].Timestamp = timestamp;

                // Publishes the four writes above to the consumer.
                Volatile.Write(ref _slots[index].Ready, 1);
                return true;
            }
        }

        /// <summary>Main thread only. False when nothing is ready to read.</summary>
        public bool TryDequeue(out ProbeEvent probeEvent)
        {
            var read = _readCursor;

            if (read >= Volatile.Read(ref _writeCursor))
            {
                probeEvent = default;
                return false;
            }

            var index = (int)(read & _mask);

            if (Volatile.Read(ref _slots[index].Ready) == 0)
            {
                // The slot is claimed but its producer has not finished writing. It
                // will be here next frame; reading it now would hand back a half-built
                // event, and there is nothing worth blocking the main thread for.
                probeEvent = default;
                return false;
            }

            probeEvent = _slots[index];
            probeEvent.Ready = 0;

            Volatile.Write(ref _slots[index].Ready, 0);
            Volatile.Write(ref _readCursor, read + 1);
            return true;
        }
    }
}
