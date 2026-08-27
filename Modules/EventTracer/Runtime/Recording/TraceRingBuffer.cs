#if AUDIOTOOLBOX_TRACE

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>
    /// The session's records, in one fixed array that is never resized and never grows.
    /// </summary>
    /// <remarks>
    /// Addressed by sequence number rather than by index. A record is not final when it
    /// is appended — a sound that started may still turn out to have been virtualized or
    /// stolen — so the recorder has to come back and patch it, and by then the slot may
    /// have been recycled. A sequence number makes that unambiguous:
    /// <see cref="TryPatchOutcome"/> silently declines to patch a record that has
    /// already scrolled out of the buffer, rather than corrupting whoever took the slot.
    /// <para>
    /// Overflow drops the oldest and counts it. Dropping the newest would be worse: a
    /// session where the interesting failure happened ten minutes in would lose exactly
    /// the part being looked for. The count is not a diagnostic detail — a truncated
    /// session that presents itself as complete would have someone conclude a sound was
    /// never posted when in fact the evidence was overwritten.
    /// </para>
    /// </remarks>
    internal sealed class TraceRingBuffer
    {
        private readonly AudioTraceRecord[] _records;
        private readonly int _mask;

        private long _writeSequence;
        private long _drainSequence;
        private long _droppedCount;

        /// <param name="capacity">Rounded up to a power of two so sequences can be masked rather than divided.</param>
        public TraceRingBuffer(int capacity)
        {
            var rounded = 1;
            while (rounded < capacity)
            {
                rounded <<= 1;
            }

            _records = new AudioTraceRecord[rounded];
            _mask = rounded - 1;
        }

        public int Capacity => _records.Length;

        /// <summary>Sequence the next appended record will get; also the count ever appended.</summary>
        public long WriteSequence => _writeSequence;

        /// <summary>Records overwritten before anything had read them.</summary>
        public long DroppedCount => _droppedCount;

        /// <summary>Records appended but not yet drained.</summary>
        public int PendingCount => (int)(_writeSequence - _drainSequence);

        /// <summary>Appends a record and returns the sequence number that addresses it.</summary>
        public long Append(in AudioTraceRecord record)
        {
            var sequence = _writeSequence;
            _records[(int)(sequence & _mask)] = record;
            _writeSequence++;

            if (_writeSequence - _drainSequence > _records.Length)
            {
                // The slot just taken held a record nobody had read. Count it and move
                // the drain point forward, or the next drain would report records that
                // are no longer there.
                _droppedCount++;
                _drainSequence = _writeSequence - _records.Length;
            }

            return sequence;
        }

        public bool IsResident(long sequence) =>
            sequence >= 0 &&
            sequence < _writeSequence &&
            sequence >= _writeSequence - _records.Length;

        /// <summary>
        /// Updates the outcome of a record already appended. False when that record has
        /// scrolled out of the buffer, which is information the caller wants: it means a
        /// voice outlived the buffer and its final outcome will never be known.
        /// </summary>
        public bool TryPatchOutcome(long sequence, PlaybackOutcome outcome, int backendResultCode)
        {
            if (!IsResident(sequence))
            {
                return false;
            }

            var index = (int)(sequence & _mask);
            _records[index].Outcome = outcome;

            // Only overwrite the raw code with a meaningful one. A later callback
            // reporting success would otherwise erase the error that explains the record.
            if (backendResultCode != 0)
            {
                _records[index].BackendResultCode = backendResultCode;
            }

            return true;
        }

        public bool TryGet(long sequence, out AudioTraceRecord record)
        {
            if (!IsResident(sequence))
            {
                record = default;
                return false;
            }

            record = _records[(int)(sequence & _mask)];
            return true;
        }

        /// <summary>
        /// Copies out every drained-but-unwritten record up to
        /// <paramref name="upToSequenceExclusive"/>, and marks them drained.
        /// </summary>
        /// <remarks>
        /// The caller passes a barrier rather than draining everything, because a record
        /// whose voice is still playing may still be patched. Writing it now would put a
        /// provisional outcome on disk and there would be no way to correct it.
        /// </remarks>
        public int Drain(long upToSequenceExclusive, AudioTraceRecord[] destination)
        {
            if (upToSequenceExclusive > _writeSequence)
            {
                upToSequenceExclusive = _writeSequence;
            }

            var available = upToSequenceExclusive - _drainSequence;

            if (available <= 0)
            {
                return 0;
            }

            var count = available > destination.Length ? destination.Length : (int)available;

            for (var i = 0; i < count; i++)
            {
                destination[i] = _records[(int)((_drainSequence + i) & _mask)];
            }

            _drainSequence += count;
            return count;
        }
    }
}

#endif
