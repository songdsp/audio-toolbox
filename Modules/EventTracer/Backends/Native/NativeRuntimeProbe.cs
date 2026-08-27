using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace AudioToolbox.EventTracer.Backends.Native
{
    /// <summary>
    /// A backend for projects with no middleware: AudioClips stand in for events and
    /// AudioSources for voices.
    /// </summary>
    /// <remarks>
    /// Its real job is to keep the facade, the recorder and their tests honest on a
    /// machine where neither FMOD nor Wwise is installed. It is a genuine backend, not a
    /// null object — clips are loaded, sounds are heard, voices run out — but Unity's
    /// own audio has no vocabulary for most of what the module exists to distinguish.
    /// <para>
    /// What it can report: <see cref="PlaybackOutcome.HandleInvalid"/> (no clip by that
    /// name), <see cref="PlaybackOutcome.Started"/>,
    /// <see cref="PlaybackOutcome.StoppedEarly"/> and
    /// <see cref="PlaybackOutcome.Stolen"/> (the source pool is exhausted and the oldest
    /// is taken). What it cannot: <see cref="PlaybackOutcome.Rejected"/> and
    /// <see cref="PlaybackOutcome.Virtualized"/>, which are concepts belonging to a
    /// virtual voice system Unity does not have. Those are absent from the support
    /// matrix rather than approximated.
    /// </para>
    /// </remarks>
    public sealed class NativeRuntimeProbe : IAudioRuntimeProbe
    {
        /// <summary>Beyond this, the oldest playing source is taken. Unity gets slow long before it.</summary>
        private const int MaxSources = 64;

        /// <summary>Reported as the raw code when a key resolves to no clip.</summary>
        public const int ResultClipNotFound = -1;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        private ProbeSignalQueue _signals;
        private GameObject _host;
        private AudioSource[] _sources;
        private int[] _sourceVoice;
        private long[] _sourceStartedAt;
        private int[] _voiceSource;
        private int _sourceCount;

        private AudioListener _listener;

        public string BackendId => "native";

        public string DisplayName => "Unity Audio";

        /// <summary>Zero, so that any real middleware outranks it.</summary>
        public int Priority => 0;

        public bool IsAvailable => true;

        public string GetUnavailableReason() => string.Empty;

        public string GetBackendVersion() => Application.unityVersion;

        public void Initialize(ProbeSignalQueue signals, int maxVoices)
        {
            _signals = signals;

            _host = new GameObject("AudioToolbox EventTracer (Unity Audio)")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            Object.DontDestroyOnLoad(_host);

            _sources = new AudioSource[MaxSources];
            _sourceVoice = new int[MaxSources];
            _sourceStartedAt = new long[MaxSources];
            _voiceSource = new int[maxVoices];

            for (var i = 0; i < MaxSources; i++)
            {
                _sourceVoice[i] = -1;
            }

            for (var i = 0; i < maxVoices; i++)
            {
                _voiceSource[i] = -1;
            }
        }

        public void Shutdown()
        {
            if (_host != null)
            {
                Object.Destroy(_host);
                _host = null;
            }

            _clips.Clear();
            _signals = null;
        }

        public bool Play(in PlayRequest request, int voiceId, out int backendResultCode)
        {
            var clip = ResolveClip(request.EventKey);

            if (clip == null)
            {
                backendResultCode = ResultClipNotFound;
                _signals.TryEnqueue(voiceId, ProbeSignal.CreateFailed, backendResultCode, Stopwatch.GetTimestamp());
                return false;
            }

            backendResultCode = 0;
            var sourceIndex = AcquireSource();
            var source = _sources[sourceIndex];

            source.clip = clip;
            source.spatialBlend = request.Is3D ? 1f : 0f;
            source.transform.position = request.Position;

            _sourceVoice[sourceIndex] = voiceId;
            _sourceStartedAt[sourceIndex] = Stopwatch.GetTimestamp();
            _voiceSource[voiceId] = sourceIndex;

            _signals.TryEnqueue(voiceId, ProbeSignal.CreateOk, 0, Stopwatch.GetTimestamp());
            source.Play();
            _signals.TryEnqueue(voiceId, ProbeSignal.Started, 0, Stopwatch.GetTimestamp());
            return true;
        }

        public void Stop(int voiceId)
        {
            var sourceIndex = _voiceSource[voiceId];

            if (sourceIndex < 0)
            {
                return;
            }

            _sources[sourceIndex].Stop();
            EndVoice(sourceIndex);
        }

        /// <summary>
        /// Unity's audio has no per-source parameters, so this is a no-op rather than a
        /// warning: a project on this backend is one that has not adopted middleware
        /// yet, and logging on every parameter set would be noise it cannot act on.
        /// </summary>
        public void SetParameter(int voiceId, string name, float value)
        {
        }

        public void SetGlobalParameter(string name, float value)
        {
        }

        public void Tick()
        {
            for (var i = 0; i < _sourceCount; i++)
            {
                if (_sourceVoice[i] < 0 || _sources[i].isPlaying)
                {
                    continue;
                }

                EndVoice(i);
            }
        }

        public bool TryGetListenerPosition(out Vector3 position)
        {
            if (_listener == null)
            {
                // Cached because the search walks the scene. Re-run only when the cached
                // one has been destroyed, which happens on a scene change.
                _listener = Object.FindFirstObjectByType<AudioListener>();
            }

            if (_listener == null)
            {
                position = Vector3.zero;
                return false;
            }

            position = _listener.transform.position;
            return true;
        }

        public double GetEventLengthSeconds(string eventKey)
        {
            var clip = ResolveClip(eventKey);
            return clip == null ? 0 : clip.length;
        }

        /// <summary>
        /// Looks a key up in Resources, once. Misses are cached as null too — a project
        /// posting a misspelled key does it every frame, and a repeated
        /// <c>Resources.Load</c> would allocate on the collection path forever.
        /// </summary>
        private AudioClip ResolveClip(string eventKey)
        {
            if (string.IsNullOrEmpty(eventKey))
            {
                return null;
            }

            if (_clips.TryGetValue(eventKey, out var cached))
            {
                return cached;
            }

            var clip = Resources.Load<AudioClip>(eventKey);
            _clips[eventKey] = clip;
            return clip;
        }

        private int AcquireSource()
        {
            for (var i = 0; i < _sourceCount; i++)
            {
                if (_sourceVoice[i] < 0)
                {
                    return i;
                }
            }

            if (_sourceCount < MaxSources)
            {
                var child = new GameObject($"voice-{_sourceCount}")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };

                child.transform.SetParent(_host.transform, worldPositionStays: false);

                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;

                _sources[_sourceCount] = source;
                return _sourceCount++;
            }

            // Everything is busy: take the one that has been going longest. The voice
            // losing its source is Stolen, and reporting it as such is the point - a
            // pool this size running dry is exactly the kind of thing that makes sounds
            // vanish in a busy scene.
            var oldest = 0;

            for (var i = 1; i < _sourceCount; i++)
            {
                if (_sourceStartedAt[i] < _sourceStartedAt[oldest])
                {
                    oldest = i;
                }
            }

            _sources[oldest].Stop();
            EndVoice(oldest);
            return oldest;
        }

        private void EndVoice(int sourceIndex)
        {
            var voiceId = _sourceVoice[sourceIndex];

            if (voiceId < 0)
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();
            _signals.TryEnqueue(voiceId, ProbeSignal.Stopped, 0, now);
            _signals.TryEnqueue(voiceId, ProbeSignal.Destroyed, 0, now);

            _sourceVoice[sourceIndex] = -1;
            _voiceSource[voiceId] = -1;
            _sources[sourceIndex].clip = null;
        }
    }

    /// <summary>Puts the native backend on the register at startup.</summary>
    /// <remarks>
    /// Self-registration rather than a reflective sweep. Discovery by reflection would
    /// mean walking every loaded assembly at startup on every platform, and would need
    /// keeping alive by hand under IL2CPP's stripping. A backend assembly only compiles
    /// when its middleware is present, so having each one announce itself is both
    /// cheaper and harder to get wrong.
    /// </remarks>
    internal static class NativeProbeRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => AudioTrace.RegisterProbe(new NativeRuntimeProbe());
    }
}
