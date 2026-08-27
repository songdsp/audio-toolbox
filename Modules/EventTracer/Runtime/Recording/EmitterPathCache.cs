#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>
    /// Turns an emitter into the scene path you would type into the hierarchy search
    /// box, and remembers the answer.
    /// </summary>
    /// <remarks>
    /// "Which object made this sound" is the question a trace record has to answer for
    /// the log to be worth reading — an event key alone tells you a gunshot played, not
    /// which of the forty rifles in the level played it. The path is what makes a record
    /// actionable without going back to the code.
    /// <para>
    /// Walking parents and joining names allocates, so it happens once per emitter and
    /// never again: a level with two hundred distinct emitters pays two hundred small
    /// strings across a whole session, and every post after the first is one dictionary
    /// probe.
    /// </para>
    /// <para>
    /// <b>Keyed by reference identity</b>, through a comparer of our own rather than the
    /// default one. Unity overrides <c>Equals</c> and <c>GetHashCode</c> on its objects to
    /// work off an engine id whose very name changed under us — <c>GetInstanceID</c> in
    /// 6000.0, <c>GetEntityId</c> in 6.2, no longer an <c>int</c> in some future version —
    /// and a cache keyed on a moving target is a cache that will break quietly one upgrade
    /// from now. Reference identity is the thing that is actually being asked about here
    /// and it does not change between releases. The cost is that up to
    /// <see cref="AudioTraceSettings.EmitterPathCapacity"/> managed wrappers for destroyed
    /// objects stay reachable for the session — bounded, small, and worth naming.
    /// </para>
    /// <para>
    /// <b>The path is a snapshot, taken the first time an emitter is seen.</b> An object
    /// renamed or reparented afterwards keeps its original path in the log. Re-resolving
    /// on every post would cost the allocation this cache exists to avoid, and the first
    /// sighting is in practice what someone is looking for.
    /// </para>
    /// </remarks>
    internal sealed class EmitterPathCache
    {
        private readonly StringInternTable _strings;
        private readonly Dictionary<Transform, int> _idByEmitter;
        private readonly int _capacity;

        // Reused across calls so that building a path allocates only the path itself.
        private readonly StringBuilder _scratch = new StringBuilder(192);
        private readonly List<Transform> _chain = new List<Transform>(16);

        private long _droppedCount;

        public EmitterPathCache(StringInternTable strings, int capacity)
        {
            _strings = strings;
            _capacity = capacity < 1 ? 1 : capacity;
            _idByEmitter = new Dictionary<Transform, int>(_capacity, ReferenceComparer.Instance);
        }

        public int Count => _idByEmitter.Count;

        /// <summary>Emitters seen after the cache filled up. Surfaced in the session header.</summary>
        public long DroppedCount => _droppedCount;

        /// <summary>
        /// The intern id of <paramref name="emitter"/>'s scene path.
        /// <see cref="TraceFormat.NoStringId"/> for a sound with no emitter,
        /// <see cref="TraceFormat.OverflowStringId"/> once the cache is full.
        /// </summary>
        public int GetPathId(Transform emitter)
        {
            // Unity's overloaded == so that an emitter destroyed between the post and
            // here reads as absent rather than throwing.
            if (emitter == null)
            {
                return TraceFormat.NoStringId;
            }

            if (_idByEmitter.TryGetValue(emitter, out var existing))
            {
                return existing;
            }

            if (_idByEmitter.Count >= _capacity)
            {
                // Bounded for the same reason the intern table is: a game that spawns and
                // destroys emitters for an hour would otherwise grow this without limit,
                // and a tracer has no business being the thing that runs a build out of
                // memory.
                _droppedCount++;
                return TraceFormat.OverflowStringId;
            }

            var id = _strings.Intern(BuildPath(emitter));
            _idByEmitter[emitter] = id;
            return id;
        }

        /// <summary>
        /// Identity, in the plain sense: the same object or not.
        /// </summary>
        /// <remarks>
        /// <see cref="RuntimeHelpers.GetHashCode(object)"/> and
        /// <see cref="object.ReferenceEquals"/> go around Unity's overrides entirely, so
        /// no part of this cache depends on an engine id, on how a destroyed object
        /// compares, or on which release you are on.
        /// </remarks>
        private sealed class ReferenceComparer : IEqualityComparer<Transform>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(Transform a, Transform b) => ReferenceEquals(a, b);

            public int GetHashCode(Transform value) => RuntimeHelpers.GetHashCode(value);
        }

        private string BuildPath(Transform leaf)
        {
            _chain.Clear();

            for (var current = leaf; current != null; current = current.parent)
            {
                _chain.Add(current);
            }

            _scratch.Length = 0;

            for (var i = _chain.Count - 1; i >= 0; i--)
            {
                _scratch.Append('/').Append(_chain[i].name);
            }

            _chain.Clear();
            return _scratch.ToString();
        }
    }
}

#endif
