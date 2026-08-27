#if AUDIOTOOLBOX_TRACE

using System;
using System.Collections.Generic;

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>
    /// Maps the handful of distinct strings a session actually contains to small ints.
    /// </summary>
    /// <remarks>
    /// The precondition for everything else. A record holding <c>string</c> fields would
    /// keep tens of thousands of references alive for the collector to walk, put the
    /// buffer on the managed heap instead of in one flat array, and write the same event
    /// name to disk once per call. Interning turns all three problems into an int.
    /// <para>
    /// A game posts a few hundred distinct event keys from a few hundred call sites, so
    /// after the first few seconds every lookup is a hit and costs one dictionary probe
    /// and no allocation. The table is bounded anyway: a project that assembles event
    /// names at runtime could otherwise grow it without limit, and the tracer must not
    /// be the reason a build runs out of memory.
    /// </para>
    /// </remarks>
    internal sealed class StringInternTable
    {
        private readonly Dictionary<string, int> _ids;
        private readonly List<string> _values;
        private readonly int _capacity;

        /// <summary>Ids added since the last drain, for the writer to emit.</summary>
        private readonly List<int> _pending = new List<int>();

        private long _droppedCount;

        public StringInternTable(int capacity)
        {
            _capacity = capacity < 1 ? 1 : capacity;

            // Sized up front so that interning never grows a bucket array mid-frame.
            _ids = new Dictionary<string, int>(_capacity, StringComparer.Ordinal);
            _values = new List<string>(_capacity);
        }

        public int Count => _values.Count;

        public int Capacity => _capacity;

        /// <summary>Strings that did not fit. Surfaced in the session header.</summary>
        public long DroppedCount => _droppedCount;

        /// <summary>
        /// Returns the id for <paramref name="value"/>, adding it if new.
        /// <see cref="TraceFormat.NoStringId"/> for null,
        /// <see cref="TraceFormat.OverflowStringId"/> once the table is full.
        /// </summary>
        public int Intern(string value)
        {
            if (value == null)
            {
                return TraceFormat.NoStringId;
            }

            if (_ids.TryGetValue(value, out var existing))
            {
                return existing;
            }

            if (_values.Count >= _capacity)
            {
                _droppedCount++;
                return TraceFormat.OverflowStringId;
            }

            var id = _values.Count;
            _values.Add(value);
            _ids.Add(value, id);
            _pending.Add(id);
            return id;
        }

        /// <summary>
        /// The text for an id. Null ids and overflow ids answer for themselves rather
        /// than throwing, because a reader should be able to show a whole session even
        /// where the session lost track of a name.
        /// </summary>
        public string Resolve(int id)
        {
            if (id == TraceFormat.OverflowStringId)
            {
                return TraceFormat.OverflowStringText;
            }

            if (id < 0 || id >= _values.Count)
            {
                return null;
            }

            return _values[id];
        }

        /// <summary>
        /// Moves the ids added since the last call into <paramref name="destination"/>,
        /// paired with their text. The writer emits these before the records that use
        /// them, so a log truncated by a crash still resolves every name it contains.
        /// </summary>
        public void DrainNewEntries(List<KeyValuePair<int, string>> destination)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                var id = _pending[i];
                destination.Add(new KeyValuePair<int, string>(id, _values[id]));
            }

            _pending.Clear();
        }
    }
}

#endif
