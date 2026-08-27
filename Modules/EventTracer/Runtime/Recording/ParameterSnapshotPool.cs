#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>
    /// The game state a sound was posted under: every parameter the tracer knows a value
    /// for, kept so that a record can point at the whole set with one int.
    /// </summary>
    /// <remarks>
    /// <b>Why this is stored as differences.</b> A full snapshot per post would be the
    /// obvious design and the wrong one. Sounds are posted in bursts, and a burst of
    /// forty footsteps happens under one unchanging set of parameters — writing forty
    /// identical copies of it would dominate the log. Here, a capture taken while nothing
    /// has changed hands back <em>the same id the last one got</em>, so those forty
    /// records share a single snapshot; a capture after a change writes only what
    /// changed, linked to the snapshot it changed from. A reader walks the chain back to
    /// reconstruct the full set.
    /// <para>
    /// <b>Slots, not names.</b> A parameter name is interned once and mapped to a small
    /// dense slot index, so a delta is a pair of primitives and the whole staging area is
    /// two flat arrays. That is what keeps <see cref="Capture"/> free of allocation on
    /// the collection path.
    /// </para>
    /// <para>
    /// <b>What is in here.</b> Global parameters — the ones that describe the world
    /// rather than one sound. They arrive two ways: immediately when the game sets one
    /// through the facade, and by polling the backend on an interval, which is what
    /// catches parameters set by code that never goes through the facade at all. Per
    /// instance parameters are deliberately absent: a snapshot is taken at post time,
    /// before any of them could have been set, so recording them would say nothing.
    /// </para>
    /// </remarks>
    internal sealed class ParameterSnapshotPool
    {
        private readonly StringInternTable _strings;

        // Slot bookkeeping. Parallel arrays indexed by slot.
        private readonly Dictionary<int, int> _slotByNameId;
        private readonly int[] _slotNameId;
        private readonly float[] _current;
        private readonly float[] _emitted;
        private int _slotCount;

        // Slots declared since the last drain. The writer emits these before any snapshot
        // that mentions them, so a truncated log still resolves the names it contains.
        private readonly List<int> _pendingSlots = new List<int>();

        // Snapshots staged since the last drain. Ids are session-global and monotonic;
        // the arenas below are staging only, and are reset on every drain.
        private readonly int[] _deltaSlots;
        private readonly float[] _deltaValues;
        private int _deltaCount;

        private readonly int[] _snapshotId;
        private readonly int[] _snapshotParent;
        private readonly int[] _snapshotOffset;
        private readonly int[] _snapshotLength;
        private int _snapshotCount;

        private int _nextSnapshotId;
        private int _lastSnapshotId = TraceFormat.NoSnapshotId;
        private bool _maybeDirty;

        private long _droppedCount;

        public ParameterSnapshotPool(StringInternTable strings, int maxParameters, int pendingSnapshotCapacity)
        {
            _strings = strings;

            var parameters = maxParameters < 1 ? 1 : maxParameters;
            var snapshots = pendingSnapshotCapacity < 1 ? 1 : pendingSnapshotCapacity;

            _slotByNameId = new Dictionary<int, int>(parameters);
            _slotNameId = new int[parameters];
            _current = new float[parameters];
            _emitted = new float[parameters];

            _snapshotId = new int[snapshots];
            _snapshotParent = new int[snapshots];
            _snapshotOffset = new int[snapshots];
            _snapshotLength = new int[snapshots];

            // Most snapshots carry one or two changes. The floor is the first capture,
            // which touches every parameter at once.
            var deltas = snapshots * 4;
            _deltaSlots = new int[deltas < parameters ? parameters : deltas];
            _deltaValues = new float[_deltaSlots.Length];
        }

        public int ParameterCount => _slotCount;

        /// <summary>Snapshots and parameters that did not fit. Surfaced in the session header.</summary>
        public long DroppedCount => _droppedCount;

        /// <summary>The id the last <see cref="Capture"/> produced, or <see cref="TraceFormat.NoSnapshotId"/>.</summary>
        public int LastSnapshotId => _lastSnapshotId;

        /// <summary>
        /// Notes a parameter's current value. Cheap enough to call every frame for every
        /// parameter; nothing is written down until someone captures.
        /// </summary>
        public void Set(string name, float value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var nameId = _strings.Intern(name);

            if (nameId < 0)
            {
                // The intern table is full, so this parameter has no name to write down.
                // Its own counter has already gone up, and a value with no name attached
                // would be a number nobody can read.
                return;
            }

            if (!_slotByNameId.TryGetValue(nameId, out var slot))
            {
                if (_slotCount >= _slotNameId.Length)
                {
                    _droppedCount++;
                    return;
                }

                slot = _slotCount++;
                _slotByNameId[nameId] = slot;
                _slotNameId[slot] = nameId;
                _pendingSlots.Add(slot);

                // NaN compares equal to nothing, itself included, so a slot's first real
                // value always registers as a change — even when that value is zero and
                // the array it landed in was already full of zeroes.
                _emitted[slot] = float.NaN;
            }

            _current[slot] = value;

            if (_emitted[slot] != value)
            {
                _maybeDirty = true;
            }
        }

        /// <summary>
        /// The id of a snapshot describing the state right now, reusing the previous one
        /// when nothing has changed since. <see cref="TraceFormat.NoSnapshotId"/> when
        /// there is nothing yet to describe, or when staging is full until the next flush.
        /// </summary>
        public int Capture()
        {
            if (!_maybeDirty)
            {
                return _lastSnapshotId;
            }

            var changes = 0;

            for (var slot = 0; slot < _slotCount; slot++)
            {
                if (_emitted[slot] != _current[slot])
                {
                    changes++;
                }
            }

            _maybeDirty = false;

            if (changes == 0)
            {
                // A parameter that moved away and back again between two captures. The
                // state is the one the last snapshot already describes.
                return _lastSnapshotId;
            }

            if (_snapshotCount >= _snapshotId.Length || _deltaCount + changes > _deltaSlots.Length)
            {
                // Staging is full until the next flush drains it. The record gets no
                // snapshot rather than a wrong one, and the count says it happened.
                _droppedCount++;
                _maybeDirty = true;
                return TraceFormat.NoSnapshotId;
            }

            var id = _nextSnapshotId++;
            var offset = _deltaCount;

            for (var slot = 0; slot < _slotCount; slot++)
            {
                if (_emitted[slot] == _current[slot])
                {
                    continue;
                }

                _deltaSlots[_deltaCount] = slot;
                _deltaValues[_deltaCount] = _current[slot];
                _deltaCount++;
                _emitted[slot] = _current[slot];
            }

            _snapshotId[_snapshotCount] = id;
            _snapshotParent[_snapshotCount] = _lastSnapshotId;
            _snapshotOffset[_snapshotCount] = offset;
            _snapshotLength[_snapshotCount] = changes;
            _snapshotCount++;

            _lastSnapshotId = id;
            return id;
        }

        /// <summary>
        /// Appends everything staged since the last call to <paramref name="destination"/>
        /// and empties the staging arenas. Appends rather than replaces, so a flush the
        /// writer could not take is merged into the next one instead of being lost.
        /// </summary>
        public void Drain(ParameterFlushBuffer destination)
        {
            for (var i = 0; i < _pendingSlots.Count; i++)
            {
                var slot = _pendingSlots[i];
                destination.Slots.Add(new ParameterSlotEntry(slot, _slotNameId[slot]));
            }

            _pendingSlots.Clear();

            for (var i = 0; i < _snapshotCount; i++)
            {
                // Offsets are relative to the destination rather than to the staging
                // arena, so that two drains merged into one buffer still point at the
                // right deltas.
                var offset = destination.Deltas.Count;
                var length = _snapshotLength[i];

                for (var d = 0; d < length; d++)
                {
                    var at = _snapshotOffset[i] + d;
                    destination.Deltas.Add(new SnapshotDeltaEntry(_deltaSlots[at], _deltaValues[at]));
                }

                destination.Snapshots.Add(
                    new SnapshotHeaderEntry(_snapshotId[i], _snapshotParent[i], offset, length));
            }

            _snapshotCount = 0;
            _deltaCount = 0;
        }
    }
}

#endif
