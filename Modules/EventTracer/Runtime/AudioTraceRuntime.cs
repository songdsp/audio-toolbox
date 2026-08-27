using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

#if AUDIOTOOLBOX_TRACE
using System.IO;
using AudioToolbox.EventTracer.Recording;
#endif

namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// The machinery behind <see cref="AudioTrace"/>: which backend is in play, which
    /// voices are alive, and — when tracing is compiled in — the session recording them.
    /// </summary>
    /// <remarks>
    /// Separate from the facade so the public surface stays small enough to read in one
    /// screen. Everything here is static: a game posts sounds from everywhere, and
    /// threading an instance through all of it would be the first thing anyone worked
    /// around.
    /// </remarks>
    public static class AudioTraceRuntime
    {
        private static readonly List<IAudioRuntimeProbe> Registered = new List<IAudioRuntimeProbe>();

        private static AudioTraceSettings _settings = AudioTraceSettings.Default;
        private static IAudioRuntimeProbe _probe;
        private static VoiceRegistry _voices;
        private static ProbeSignalQueue _signals;
        private static bool _initialized;
        private static bool _configuredBeforeInit;

#if AUDIOTOOLBOX_TRACE
        private static TraceRecorder _recorder;
        private static TraceSessionWriter _writer;
        private static TraceSessionHeader _header;
#endif

        /// <summary>The backend actually in use, or null when nothing is registered.</summary>
        public static IAudioRuntimeProbe ActiveProbe => _probe;

        /// <summary>
        /// True when the collection layer is compiled in and running. False in a build
        /// without <c>AUDIOTOOLBOX_TRACE</c>, where the facade still plays sounds and
        /// records nothing.
        /// </summary>
        public static bool IsRecording
        {
            get
            {
#if AUDIOTOOLBOX_TRACE
                return _recorder != null;
#else
                return false;
#endif
            }
        }

        /// <summary>Where this session is being written, or empty when it is memory-only.</summary>
        public static string SessionPath
        {
            get
            {
#if AUDIOTOOLBOX_TRACE
                return _writer?.FilePath ?? string.Empty;
#else
                return string.Empty;
#endif
            }
        }

        /// <summary>
        /// Registers a backend. Called by each backend assembly's own initialiser, so
        /// this assembly never has to name one.
        /// </summary>
        public static void RegisterProbe(IAudioRuntimeProbe probe)
        {
            if (probe == null || Registered.Contains(probe))
            {
                return;
            }

            Registered.Add(probe);

            if (_initialized)
            {
                // Registering after startup means whatever was chosen may no longer be
                // the best choice. Rare, but silently keeping the wrong backend would be
                // very hard to explain later.
                Debug.LogWarning(
                    $"[EventTracer] Backend '{probe.BackendId}' registered after startup and will not be used " +
                    "for this session.");
            }
        }

        public static IReadOnlyList<IAudioRuntimeProbe> RegisteredProbes => Registered;

        /// <summary>Applies settings. Must be called before the first post; ignored afterwards.</summary>
        public static void Configure(in AudioTraceSettings settings)
        {
            if (_initialized)
            {
                Debug.LogWarning("[EventTracer] Configure() after the session started has no effect.");
                return;
            }

            _settings = settings.Sanitized();
            _configuredBeforeInit = true;
        }

        public static AudioTraceSettings CurrentSettings => _settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            // Statics survive a domain reload when "Reload Domain" is off, so a second
            // Play would otherwise inherit the previous session's probe list and buffers.
            Shutdown();
            Registered.Clear();

            if (!_configuredBeforeInit)
            {
                _settings = AudioTraceSettings.Default;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapForPlaySession()
        {
            EnsureInitialized();
            AudioTracePump.Install();
        }

        internal static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _settings = _settings.Sanitized();

            _probe = SelectProbe();
            _voices = new VoiceRegistry(_settings.MaxConcurrentVoices);
            _signals = new ProbeSignalQueue(_settings.SignalQueueCapacity);

            if (_probe == null)
            {
                Debug.LogWarning(
                    "[EventTracer] No audio backend registered. AudioTrace.Post will do nothing. " +
                    "Check that a backend assembly compiled - the FMOD one needs AUDIOTOOLBOX_FMOD.");
                return;
            }

            _probe.Initialize(_signals, _voices.Capacity);

#if AUDIOTOOLBOX_TRACE
            StartSession();
#endif
        }

        public static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

#if AUDIOTOOLBOX_TRACE
            if (_recorder != null)
            {
                _recorder.Flush(force: true);
                _writer?.WaitForIdle();
            }

            if (_writer != null)
            {
                var failure = _writer.Failure;

                if (!string.IsNullOrEmpty(failure))
                {
                    Debug.LogWarning($"[EventTracer] Session log was not written: {failure}");
                }

                _writer.Dispose();
                _writer = null;
            }

            _recorder = null;
            _header = null;
#endif

            _probe?.Shutdown();
            _probe = null;
            _voices = null;
            _signals = null;
            _initialized = false;
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        /// <summary>
        /// Tears down whatever session is running and starts a fresh one on
        /// <paramref name="probe"/>. For tests only, and compiled out of a release
        /// player — nothing shipping should be able to discard a session mid-flight.
        /// </summary>
        /// <remarks>
        /// The seam that lets the whole pipeline — facade, voice slots, recorder, ring
        /// buffer, writer — be driven from an EditMode test against a probe that reports
        /// exactly the signals the test wants, in exactly the order it wants them. That
        /// is a different question from whether FMOD really emits those signals, which is
        /// what the PlayMode tests are for, and keeping the two apart is what makes a
        /// failure in either one mean something specific.
        /// </remarks>
        public static void ResetForTests(IAudioRuntimeProbe probe, AudioTraceSettings settings)
        {
            Shutdown();
            Registered.Clear();

            _settings = settings.Sanitized();
            _configuredBeforeInit = true;

            if (probe != null)
            {
                Registered.Add(probe);
            }

            EnsureInitialized();
        }

        /// <summary>Drains signals and applies them, without waiting for a frame. For tests.</summary>
        public static void PumpForTests() => Pump();
#endif

        internal static AudioTraceHandle Post(
            string eventKey,
            Transform emitter,
            Vector3 position,
            bool is3D,
            string callerFilePath,
            int callerLineNumber)
        {
            EnsureInitialized();

            if (_probe == null || string.IsNullOrEmpty(eventKey))
            {
                return AudioTraceHandle.Invalid;
            }

            if (!_voices.TryAcquire(out var voiceId, out var generation))
            {
                // Every slot is taken. Warned once per second rather than per call,
                // because the situation that causes it produces a great many calls.
                WarnThrottled(
                    "[EventTracer] Out of voice slots ({0}). Sounds are being dropped. " +
                    "Raise AudioTraceSettings.MaxConcurrentVoices, or look for sounds that never stop.",
                    _voices.Capacity);
                return AudioTraceHandle.Invalid;
            }

            var request = new PlayRequest(eventKey, emitter, position, is3D);
            var created = _probe.Play(in request, voiceId, out var backendResultCode);

#if AUDIOTOOLBOX_TRACE
            if (_recorder != null)
            {
                var hasListener = _probe.TryGetListenerPosition(out var listenerPosition);

                _recorder.BeginVoice(
                    voiceId,
                    eventKey,
                    emitter,
                    position,
                    is3D,
                    hasListener,
                    listenerPosition,
                    callerFilePath,
                    callerLineNumber,
                    created,
                    backendResultCode,
                    _probe.GetEventLengthSeconds(eventKey));
            }
#endif

            if (!created)
            {
                _voices.Release(voiceId);
                return AudioTraceHandle.Invalid;
            }

            return new AudioTraceHandle(voiceId, generation);
        }

        /// <summary>
        /// Takes a voice slot and opens a record for a sound this module did not start.
        /// The caller is responsible for wiring the middleware instance to
        /// <paramref name="voiceId"/> afterwards.
        /// </summary>
        /// <remarks>
        /// The escape hatch, and the reason adoption is possible at all. A project with
        /// thousands of existing FMOD calls cannot switch to the facade in one commit,
        /// and a tool that demands it will simply not be adopted. Attaching gets a sound
        /// into the trace without moving its call site; the record is honest about it,
        /// because the tracer joins the story partway through and says so.
        /// </remarks>
        public static bool TryBeginExternalVoice(
            string eventKey,
            Transform emitter,
            Vector3 position,
            bool is3D,
            string callerFilePath,
            int callerLineNumber,
            out AudioTraceHandle handle,
            out int voiceId)
        {
            EnsureInitialized();

            handle = AudioTraceHandle.Invalid;
            voiceId = -1;

            if (_probe == null || string.IsNullOrEmpty(eventKey))
            {
                return false;
            }

            if (!_voices.TryAcquire(out voiceId, out var generation))
            {
                return false;
            }

#if AUDIOTOOLBOX_TRACE
            if (_recorder != null)
            {
                var hasListener = _probe.TryGetListenerPosition(out var listenerPosition);

                _recorder.BeginVoice(
                    voiceId,
                    eventKey,
                    emitter,
                    position,
                    is3D,
                    hasListener,
                    listenerPosition,
                    callerFilePath,
                    callerLineNumber,
                    created: true,
                    backendResultCode: 0,
                    eventLengthSeconds: _probe.GetEventLengthSeconds(eventKey));
            }
#endif

            handle = new AudioTraceHandle(voiceId, generation);
            return true;
        }

        internal static void Stop(in AudioTraceHandle handle)
        {
            if (_probe == null || !_voices.IsAlive(handle.VoiceId, handle.Generation))
            {
                return;
            }

#if AUDIOTOOLBOX_TRACE
            // Noted before the stop is issued: the callback can arrive on another thread
            // and be drained before the next line would have run.
            _recorder?.NoteStopRequested(handle.VoiceId);
#endif

            _probe.Stop(handle.VoiceId);
        }

        internal static void SetParameter(in AudioTraceHandle handle, string name, float value)
        {
            if (_probe == null || !_voices.IsAlive(handle.VoiceId, handle.Generation))
            {
                return;
            }

            _probe.SetParameter(handle.VoiceId, name, value);
        }

        internal static void SetGlobalParameter(string name, float value)
        {
            EnsureInitialized();

#if AUDIOTOOLBOX_TRACE
            // Recorded from the call rather than read back from the backend. The value is
            // exact and it is known now, whereas a poll would find it up to an interval
            // later and might miss it entirely if something else moved it back first.
            _recorder?.NoteGlobalParameter(name, value);
#endif

            _probe?.SetGlobalParameter(name, value);
        }

        internal static bool IsAlive(in AudioTraceHandle handle) =>
            _voices != null && _voices.IsAlive(handle.VoiceId, handle.Generation);

        /// <summary>
        /// Drains what the backend has reported since last frame and retires the voices
        /// that have finished. Runs whether or not tracing is compiled in — voice slots
        /// have to be recycled either way.
        /// </summary>
        internal static void Pump()
        {
            if (_probe == null)
            {
                return;
            }

            _probe.Tick();

            while (_signals.TryDequeue(out var signal))
            {
                if (signal.VoiceId < 0 || signal.VoiceId >= _voices.Capacity)
                {
                    continue;
                }

                var finished = signal.Signal == ProbeSignal.Destroyed;

#if AUDIOTOOLBOX_TRACE
                if (_recorder != null)
                {
                    finished = _recorder.ApplySignal(
                        signal.VoiceId,
                        signal.Signal,
                        signal.ResultCode,
                        signal.Timestamp) || finished;
                }
#endif

                if (finished)
                {
                    _voices.Release(signal.VoiceId);
                }
            }

#if AUDIOTOOLBOX_TRACE
            _recorder?.SampleGlobalParameters(_probe);
            _recorder?.Flush(force: false);
#endif
        }

        private static IAudioRuntimeProbe SelectProbe()
        {
            IAudioRuntimeProbe best = null;
            IAudioRuntimeProbe bestUnavailable = null;

            for (var i = 0; i < Registered.Count; i++)
            {
                var candidate = Registered[i];

                bool available;

                try
                {
                    available = candidate.IsAvailable;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[EventTracer] Backend '{candidate.BackendId}' threw while reporting availability: {e.Message}");
                    continue;
                }

                if (available)
                {
                    if (best == null || candidate.Priority > best.Priority)
                    {
                        best = candidate;
                    }
                }
                else if (bestUnavailable == null || candidate.Priority > bestUnavailable.Priority)
                {
                    bestUnavailable = candidate;
                }
            }

            if (best == null && bestUnavailable != null)
            {
                // Say why the real middleware was passed over. Degrading silently to a
                // fallback is how someone ends up debugging FMOD behaviour that FMOD
                // was never asked to produce.
                Debug.LogWarning(
                    $"[EventTracer] Backend '{bestUnavailable.BackendId}' is installed but not usable: " +
                    bestUnavailable.GetUnavailableReason());
            }

            return best;
        }

#if AUDIOTOOLBOX_TRACE
        private static void StartSession()
        {
            _header = new TraceSessionHeader
            {
                StartedUtc = DateTime.UtcNow.ToString("o"),
                UnityVersion = Application.unityVersion,
                Platform = Application.platform.ToString(),
                ApplicationVersion = Application.version,
                BackendId = _probe.BackendId,
                BackendVersion = SafeBackendVersion(_probe),
            };

            if (_settings.WriteToDisk)
            {
                var fileName = $"session-{DateTime.Now:yyyyMMdd-HHmmss}{TraceFormat.FileExtension}";
                var path = Path.Combine(Application.persistentDataPath, "AudioToolboxTraces", fileName);

                try
                {
                    _writer = new TraceSessionWriter(path, Math.Min(_settings.RecordCapacity, 8192));
                }
                catch (Exception e)
                {
                    // A read-only or full disk must not stop the game. The session
                    // continues in memory and says so.
                    Debug.LogWarning($"[EventTracer] Could not open a session log at {path}: {e.Message}");
                    _writer = null;
                }
            }

            _recorder = new TraceRecorder(_settings, _voices.Capacity, _header, _writer);
        }

        private static string SafeBackendVersion(IAudioRuntimeProbe probe)
        {
            try
            {
                return probe.GetBackendVersion() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static TraceRecorder Recorder => _recorder;

        public static TraceSessionHeader Header => _recorder?.BuildHeader();

        /// <summary>
        /// What the session currently says became of a handle's sound.
        /// </summary>
        /// <remarks>
        /// Reads the live per-voice state rather than the on-disk record, so it answers
        /// while a sound is still playing. That is what a test asserting
        /// <see cref="PlaybackOutcome.Virtualized"/> needs — by the time the record is
        /// flushed, the voice is over.
        /// </remarks>
        public static PlaybackOutcome GetOutcome(AudioTraceHandle handle)
        {
            if (_recorder == null || handle.VoiceId < 0)
            {
                return PlaybackOutcome.NotCalled;
            }

            return _recorder.GetOutcome(handle.VoiceId);
        }

        /// <summary>Copies the session's resident records out, oldest first. For tests and dumps.</summary>
        public static int SnapshotRecords(List<AudioTraceRecord> destination)
        {
            destination.Clear();

            if (_recorder == null)
            {
                return 0;
            }

            var buffer = _recorder.Buffer;
            var first = Math.Max(0, buffer.WriteSequence - buffer.Capacity);

            for (var sequence = first; sequence < buffer.WriteSequence; sequence++)
            {
                if (buffer.TryGet(sequence, out var record))
                {
                    destination.Add(record);
                }
            }

            return destination.Count;
        }

        /// <summary>The text behind an intern id in the live session.</summary>
        public static string ResolveString(int id) => _recorder?.ResolveString(id);

        /// <summary>Writes everything that can no longer change to the session file.</summary>
        public static void Flush() => _recorder?.Flush(force: true);
#endif

        private static readonly Stopwatch WarnClock = Stopwatch.StartNew();
        private static long _nextWarnMs;

        private static void WarnThrottled(string format, object arg)
        {
            if (WarnClock.ElapsedMilliseconds < _nextWarnMs)
            {
                return;
            }

            _nextWarnMs = WarnClock.ElapsedMilliseconds + 1000;
            Debug.LogWarning(string.Format(format, arg));
        }
    }
}
