#if AUDIOTOOLBOX_TRACE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>
    /// Keeps the session: one record per post, patched as the backend reports what
    /// became of it, drained to disk once a voice can no longer change.
    /// </summary>
    /// <remarks>
    /// Everything here runs on the main thread, on the collection path, and therefore
    /// allocates nothing per call. Per-voice state lives in plain arrays indexed by voice
    /// id — no dictionaries, no lists, no closures — which is only possible because
    /// <see cref="VoiceRegistry"/> hands out small dense ids.
    /// <para>
    /// The awkward part of the design, and worth being clear about: a record is written
    /// when the sound is posted, but its outcome is not known until the sound ends. So
    /// records are appended provisionally and patched in place, and nothing is written
    /// to disk past the oldest voice still playing. A voice that plays longer than the
    /// buffer takes to wrap will lose its record, and that is counted rather than hidden.
    /// </para>
    /// </remarks>
    internal sealed class TraceRecorder
    {
        private readonly AudioTraceSettings _settings;
        private readonly TraceRingBuffer _ring;
        private readonly StringInternTable _strings;
        private readonly TraceSessionWriter _writer;

        // Per-voice state, indexed by voice id.
        private readonly VoiceOutcomeState[] _state;
        private readonly long[] _sequence;
        private readonly long[] _startTicks;
        private readonly double[] _lengthSeconds;
        private readonly bool[] _active;

        // Scratch owned by the recorder so a flush allocates nothing either.
        private readonly AudioTraceRecord[] _flushBuffer;
        private readonly List<KeyValuePair<int, string>> _flushStrings = new List<KeyValuePair<int, string>>();

        // (file intern id, line) -> intern id of "file:line". Composing that string on
        // every post would allocate on the collection path; composing it once per
        // distinct call site costs nothing thereafter, and a game has far fewer call
        // sites than calls.
        private readonly Dictionary<long, int> _callSiteIds = new Dictionary<long, int>();

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly TraceSessionHeader _header;

        private long _lostToWrapCount;
        private long _lostToWriterCount;
        private bool _headerSent;
        private float _nextFlushTime;

        public TraceRecorder(
            in AudioTraceSettings settings,
            int maxVoices,
            TraceSessionHeader header,
            TraceSessionWriter writer)
        {
            _settings = settings;
            _header = header;
            _writer = writer;

            _ring = new TraceRingBuffer(settings.RecordCapacity);
            _strings = new StringInternTable(settings.InternCapacity);

            _state = new VoiceOutcomeState[maxVoices];
            _sequence = new long[maxVoices];
            _startTicks = new long[maxVoices];
            _lengthSeconds = new double[maxVoices];
            _active = new bool[maxVoices];

            // One flush can carry at most a bufferful; anything more would mean the ring
            // had already dropped records, which is counted separately.
            _flushBuffer = new AudioTraceRecord[Math.Min(_ring.Capacity, 8192)];

            _header.RecordCapacity = _ring.Capacity;
        }

        public TraceRingBuffer Buffer => _ring;

        public StringInternTable Strings => _strings;

        /// <summary>Voices whose records were overwritten before their outcome was known.</summary>
        public long LostToWrapCount => _lostToWrapCount;

        /// <summary>
        /// Records a post. <paramref name="created"/> false means the backend could not
        /// produce an instance at all, and the record is final immediately.
        /// </summary>
        public void BeginVoice(
            int voiceId,
            string eventKey,
            Vector3 emitterPos,
            bool isPositioned,
            bool hasListener,
            Vector3 listenerPos,
            string callerFilePath,
            int callerLineNumber,
            bool created,
            int backendResultCode,
            double eventLengthSeconds)
        {
            var state = OutcomeStateMachine.Begin();
            OutcomeStateMachine.Apply(
                ref state,
                created ? ProbeSignal.CreateOk : ProbeSignal.CreateFailed,
                0,
                eventLengthSeconds,
                _settings.NaturalEndToleranceSeconds);

            var record = new AudioTraceRecord
            {
                Frame = Time.frameCount,
                TimeSeconds = _clock.Elapsed.TotalSeconds,
                EventKeyId = _strings.Intern(eventKey),
                EmitterPathId = TraceFormat.NoStringId,
                CallSiteId = InternCallSite(callerFilePath, callerLineNumber),
                EmitterPos = emitterPos,
                ListenerPos = hasListener ? listenerPos : Vector3.zero,

                // -1 rather than 0 whenever there is nothing to measure. Zero reads as
                // "right on top of the listener", which is a plausible answer and an
                // entirely wrong one; a 3D event posted with no position is a real defect
                // and the record has to be able to say so.
                DistanceToListener = isPositioned && hasListener
                    ? Vector3.Distance(emitterPos, listenerPos)
                    : -1f,
                Outcome = state.Outcome,
                BackendResultCode = backendResultCode,
                ParamSnapshotId = TraceFormat.NoSnapshotId,
            };

            var sequence = _ring.Append(in record);

            _state[voiceId] = state;
            _sequence[voiceId] = sequence;
            _startTicks[voiceId] = Stopwatch.GetTimestamp();
            _lengthSeconds[voiceId] = eventLengthSeconds;
            _active[voiceId] = !state.IsFinished;
        }

        /// <summary>
        /// The one fact no callback carries. Raised by the facade when game code asks a
        /// sound to stop, so that the stop can be told from a steal.
        /// </summary>
        public void NoteStopRequested(int voiceId)
        {
            if (!_active[voiceId])
            {
                return;
            }

            _state[voiceId].StopRequested = true;
        }

        /// <summary>
        /// Feeds one backend signal into a voice's outcome and patches its record.
        /// Returns true when the voice has finished and its slot can be recycled.
        /// </summary>
        public bool ApplySignal(int voiceId, ProbeSignal signal, int resultCode, long timestampTicks)
        {
            if (!_active[voiceId])
            {
                return false;
            }

            var elapsed = timestampTicks > 0
                ? (timestampTicks - _startTicks[voiceId]) / (double)Stopwatch.Frequency
                : (Stopwatch.GetTimestamp() - _startTicks[voiceId]) / (double)Stopwatch.Frequency;

            OutcomeStateMachine.Apply(
                ref _state[voiceId],
                signal,
                elapsed,
                _lengthSeconds[voiceId],
                _settings.NaturalEndToleranceSeconds);

            if (!_ring.TryPatchOutcome(_sequence[voiceId], _state[voiceId].Outcome, resultCode))
            {
                // The record scrolled out from under a sound that was still playing.
                // Counted rather than ignored: it means the buffer is too small for
                // this session, and the outcome of that voice is simply not knowable.
                _lostToWrapCount++;
                _active[voiceId] = false;
                return true;
            }

            if (_state[voiceId].IsFinished)
            {
                _active[voiceId] = false;
                return true;
            }

            return false;
        }

        /// <summary>Marks a voice finished without a signal — used when the facade gives up on it.</summary>
        public void AbandonVoice(int voiceId)
        {
            _active[voiceId] = false;
        }

        public PlaybackOutcome GetOutcome(int voiceId) => _state[voiceId].Outcome;

        public bool IsVoiceActive(int voiceId) => _active[voiceId];

        /// <summary>
        /// Reads a record back by sequence. For tests and the console dump; the editor
        /// reads finished sessions from disk instead.
        /// </summary>
        public bool TryGetRecord(long sequence, out AudioTraceRecord record) => _ring.TryGet(sequence, out record);

        public string ResolveString(int id) => _strings.Resolve(id);

        /// <summary>
        /// Hands everything that can no longer change to the writer.
        /// <paramref name="force"/> ignores the flush interval; used at shutdown.
        /// </summary>
        /// <remarks>
        /// The busy check comes before the drain, and that ordering is the whole of it.
        /// Draining first and discovering afterwards that the writer could not take the
        /// batch loses those records for good — they are gone from the ring and never
        /// reached the file. Leaving them in the ring instead costs nothing: they go out
        /// on the next flush, and if the buffer wraps before then the drop is counted.
        /// </remarks>
        public void Flush(bool force)
        {
            if (_writer == null)
            {
                return;
            }

            if (!force && Time.unscaledTime < _nextFlushTime)
            {
                return;
            }

            if (_writer.IsBusy)
            {
                if (!force)
                {
                    return;
                }

                // Shutdown. There is no next frame to try again on, so this is the one
                // place waiting for the disk is the right thing to do.
                _writer.WaitForIdle();
            }

            _nextFlushTime = Time.unscaledTime + _settings.FlushIntervalSeconds;

            var barrier = OldestUnfinishedSequence();
            var count = _ring.Drain(barrier, _flushBuffer);

            _flushStrings.Clear();
            _strings.DrainNewEntries(_flushStrings);

            if (count == 0 && _flushStrings.Count == 0 && !force)
            {
                return;
            }

            UpdateHeader(count);

            // Serialising the header allocates, so it goes out once at the start (so a
            // crashed session still says where it came from) and once at the end (with
            // the counts that matter). Sending it on every flush would put a string
            // allocation on a frame the collection layer is meant to cost nothing on.
            var header = _headerSent && !force ? null : JsonUtility.ToJson(_header);
            _headerSent = true;

            if (!_writer.TrySubmit(_flushBuffer, count, _flushStrings, header))
            {
                // Only reachable if the writer became busy between the check above and
                // here, which nothing else submits to. Counted rather than assumed
                // impossible: a session that quietly lost records is the failure this
                // module exists to avoid producing.
                _lostToWriterCount += count;
            }
        }

        public TraceSessionHeader BuildHeader()
        {
            UpdateHeader(0);
            return _header;
        }

        private void UpdateHeader(int justDrained)
        {
            _header.DroppedRecordCount = _ring.DroppedCount + _lostToWrapCount + _lostToWriterCount;
            _header.DroppedStringCount = _strings.DroppedCount;
            _header.RecordCount = (int)(_writer?.RecordsWritten ?? 0) + justDrained;
        }

        /// <summary>
        /// Turns a compiler-supplied file and line into one intern id.
        /// </summary>
        /// <remarks>
        /// <c>[CallerFilePath]</c> hands over the absolute path on whoever's machine
        /// compiled the code, which is both long and meaningless to anyone else, so it
        /// is trimmed to the project-relative part. That trimming and the string
        /// concatenation happen once per call site — after which posting from that line
        /// again is one dictionary probe.
        /// </remarks>
        private int InternCallSite(string filePath, int line)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return TraceFormat.NoStringId;
            }

            // The path is a compile-time literal, so interning it does not allocate.
            var fileId = _strings.Intern(filePath);
            var key = ((long)fileId << 32) | (uint)line;

            if (_callSiteIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var id = _strings.Intern(Shorten(filePath) + ":" + line);
            _callSiteIds[key] = id;
            return id;
        }

        private static string Shorten(string filePath)
        {
            var normalized = filePath.Replace('\\', '/');

            foreach (var root in new[] { "/Assets/", "/Packages/" })
            {
                var at = normalized.LastIndexOf(root, StringComparison.Ordinal);

                if (at >= 0)
                {
                    return normalized.Substring(at + 1);
                }
            }

            return normalized;
        }

        /// <summary>
        /// The sequence of the oldest record that might still be patched. Nothing at or
        /// after it may go to disk.
        /// </summary>
        private long OldestUnfinishedSequence()
        {
            var oldest = _ring.WriteSequence;

            for (var i = 0; i < _active.Length; i++)
            {
                if (_active[i] && _sequence[i] < oldest)
                {
                    oldest = _sequence[i];
                }
            }

            return oldest;
        }
    }
}

#endif
