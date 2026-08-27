using System;
using System.Collections.Generic;
using System.Diagnostics;
using AOT;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AudioToolbox.EventTracer.Backends.Fmod
{
    /// <summary>
    /// Plays FMOD events on behalf of the facade and reports what the engine does with them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Instances are created, given a voice id, subscribed to, started and released
    /// immediately. Releasing straight away is what makes
    /// <see cref="PlaybackOutcome.Rejected"/> observable: an instance refused a voice
    /// never starts, and the release turns that into a <c>DESTROYED</c> callback with no
    /// <c>STARTED</c> before it. FMOD keeps the memory alive until the sound actually
    /// stops, so stopping and steering a released instance stays valid — a stale handle
    /// returns an error rather than crashing.
    /// </para>
    /// <para>
    /// The voice id travels through FMOD's own user data as an integer rather than a
    /// <c>GCHandle</c> to a managed object. Callbacks arrive on FMOD's update thread, and
    /// a managed reference there would mean a pinned object per playing sound plus the
    /// allocation to pin it — on a path that is required to allocate nothing.
    /// </para>
    /// </remarks>
    public sealed class FmodRuntimeProbe : IAudioRuntimeProbe
    {
        /// <summary>
        /// Held in a static field for the lifetime of the domain. A delegate passed to
        /// native code and then collected is a crash that reproduces once a week on one
        /// machine; the marshaller does not keep it alive, so we must.
        /// </summary>
        private static readonly EVENT_CALLBACK CallbackDelegate = HandleEventCallback;

        /// <summary>
        /// Read from the callback thread. There is one probe per session, so a static is
        /// the whole of what the callback needs to find its way home.
        /// </summary>
        private static ProbeSignalQueue _sharedSignals;

        /// <summary>
        /// Stamped into every instance's user data alongside its voice id, and checked in
        /// the callback.
        /// </summary>
        /// <remarks>
        /// Sessions end while sounds are still fading out, and those sounds go on
        /// reporting for another second or two. Without an epoch, a destroyed callback
        /// from the previous session lands on voice 3 of the new one and retires a sound
        /// that had only just started — which is exactly how a test suite that passes
        /// individually fails when run in order.
        /// </remarks>
        private static int _sharedEpoch;

        private static int _nextEpoch;

        private int _epoch;

        private readonly Dictionary<string, EventDescription> _descriptions =
            new Dictionary<string, EventDescription>(StringComparer.Ordinal);

        private readonly Dictionary<string, double> _lengths =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private ProbeSignalQueue _signals;
        private EventInstance[] _instances;
        private Transform[] _followTargets;

        // Compact list of the voices that have an emitter to follow, so Tick does not
        // walk every slot every frame.
        private int[] _following;
        private int[] _followSlot;
        private int _followCount;

        // Built once on the first poll and kept, because assembling it marshals a string
        // per parameter. Cleared on shutdown, since a new session may load other banks.
        private FMOD.Studio.PARAMETER_DESCRIPTION[] _globalParameters;
        private string[] _globalParameterNames;

        private string _unavailableReason = string.Empty;

        public string BackendId => "fmod";

        public string DisplayName => "FMOD Studio";

        public int Priority => 100;

        public bool IsAvailable
        {
            get
            {
                try
                {
                    var system = RuntimeManager.StudioSystem;

                    if (!system.isValid())
                    {
                        _unavailableReason = "The FMOD Studio system did not initialise.";
                        return false;
                    }

                    _unavailableReason = string.Empty;
                    return true;
                }
                catch (Exception e)
                {
                    // RuntimeManager throws when the integration is present but has no
                    // usable banks or settings. Report it rather than letting it escape
                    // into whatever was calling AudioTrace.Post.
                    _unavailableReason = e.Message;
                    return false;
                }
            }
        }

        public string GetUnavailableReason() => _unavailableReason;

        public string GetBackendVersion()
        {
            var number = FMOD.VERSION.number;
            return $"{(number >> 16) & 0xFFFF}.{(number >> 8) & 0xFF:00}.{number & 0xFF:00}";
        }

        public void Initialize(ProbeSignalQueue signals, int maxVoices)
        {
            _signals = signals;
            _sharedSignals = signals;
            _epoch = ++_nextEpoch;
            _sharedEpoch = _epoch;

            _instances = new EventInstance[maxVoices];
            _followTargets = new Transform[maxVoices];
            _following = new int[maxVoices];
            _followSlot = new int[maxVoices];

            for (var i = 0; i < maxVoices; i++)
            {
                _followSlot[i] = -1;
            }
        }

        public void Shutdown()
        {
            // Sounds that were still fading out would go on reporting into a queue the
            // next session owns. Cutting them and clearing their user data makes any
            // callback still in flight unattributable, which is what it should be.
            if (_instances != null)
            {
                for (var i = 0; i < _instances.Length; i++)
                {
                    if (!_instances[i].isValid())
                    {
                        continue;
                    }

                    _instances[i].setUserData(IntPtr.Zero);
                    _instances[i].stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                }
            }

            for (var i = 0; i < _followCount; i++)
            {
                _followSlot[_following[i]] = -1;
            }

            _followCount = 0;
            _sharedEpoch = 0;
            _sharedSignals = null;
            _signals = null;
            _descriptions.Clear();
            _lengths.Clear();
            _globalParameters = null;
            _globalParameterNames = null;
        }

        public bool Play(in PlayRequest request, int voiceId, out int backendResultCode)
        {
            if (!TryGetDescription(request.EventKey, out var description, out var lookupResult))
            {
                backendResultCode = (int)lookupResult;
                _signals.TryEnqueue(voiceId, ProbeSignal.CreateFailed, backendResultCode, Stopwatch.GetTimestamp());
                return false;
            }

            var result = description.createInstance(out var instance);

            if (result != FMOD.RESULT.OK)
            {
                backendResultCode = (int)result;
                _signals.TryEnqueue(voiceId, ProbeSignal.CreateFailed, backendResultCode, Stopwatch.GetTimestamp());
                return false;
            }

            backendResultCode = 0;
            _instances[voiceId] = instance;

            // Set before the callback, so the very first callback can already resolve
            // which voice it belongs to. The +1 keeps zero meaning "no user data".
            instance.setUserData(PackUserData(_epoch, voiceId));
            instance.setCallback(CallbackDelegate, FmodSignalMap.Mask);

            if (request.Is3D)
            {
                instance.set3DAttributes(request.Position.To3DAttributes());
            }

            if (request.Emitter != null)
            {
                AddFollower(voiceId, request.Emitter);
            }

            instance.start();

            // Released now, played later. FMOD frees the instance once it stops, which
            // is what turns "never got a voice" into a DESTROYED with no STARTED.
            instance.release();
            return true;
        }

        /// <summary>
        /// Wires an instance somebody else created to a voice slot, so the rest of its
        /// life is traced. Called by <see cref="FmodAudioTrace.Attach"/>.
        /// </summary>
        /// <remarks>
        /// Whatever happened before this point is lost, which is why the facade is the
        /// better path: an instance that was refused a voice has already been destroyed
        /// by the time anyone could attach to it. If it is already playing, a Started is
        /// synthesised here rather than waiting for a callback that has been and gone.
        /// </remarks>
        internal void Adopt(int voiceId, EventInstance instance, Transform emitter)
        {
            _instances[voiceId] = instance;

            instance.setUserData(PackUserData(_epoch, voiceId));
            instance.setCallback(CallbackDelegate, FmodSignalMap.Mask);

            if (emitter != null)
            {
                AddFollower(voiceId, emitter);
            }

            var now = Stopwatch.GetTimestamp();
            _signals.TryEnqueue(voiceId, ProbeSignal.CreateOk, 0, now);

            if (instance.getPlaybackState(out var state) == FMOD.RESULT.OK &&
                (state == PLAYBACK_STATE.PLAYING || state == PLAYBACK_STATE.STARTING || state == PLAYBACK_STATE.SUSTAINING))
            {
                _signals.TryEnqueue(voiceId, ProbeSignal.Started, 0, now);
            }
        }

        public void Stop(int voiceId)
        {
            // ALLOWFADEOUT rather than IMMEDIATE: cutting a sound dead is a different
            // thing from asking it to stop, and a tracer should not change the sound of
            // the game it is measuring.
            // Fully qualified: FMODUnity declares a STOP_MODE of its own for emitters.
            _instances[voiceId].stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            RemoveFollower(voiceId);
        }

        public void SetParameter(int voiceId, string name, float value) =>
            _instances[voiceId].setParameterByName(name, value);

        public void SetGlobalParameter(string name, float value) =>
            RuntimeManager.StudioSystem.setParameterByName(name, value);

        /// <summary>
        /// Reads every global parameter the loaded banks declare.
        /// </summary>
        /// <remarks>
        /// The description list is fetched once and kept, because building it means
        /// allocating an array and marshalling a string per parameter — acceptable at
        /// startup, not on an interval. Names are marshalled out of FMOD's
        /// <c>StringWrapper</c> at the same time and kept as managed strings, so a poll is
        /// nothing but a handful of <c>getParameterByID</c> calls.
        /// <para>
        /// Automatic parameters — distance, event cone angle and the rest — are skipped.
        /// They are properties of one instance rather than of the world, and the system
        /// scope has no meaningful value for them.
        /// </para>
        /// </remarks>
        public int ReadGlobalParameters(string[] names, float[] values)
        {
            if (names == null || values == null)
            {
                return 0;
            }

            EnsureGlobalParameterCache();

            var system = RuntimeManager.StudioSystem;

            if (_globalParameters == null || !system.isValid())
            {
                return 0;
            }

            var limit = Math.Min(names.Length, values.Length);
            var written = 0;

            for (var i = 0; i < _globalParameters.Length && written < limit; i++)
            {
                if (system.getParameterByID(_globalParameters[i].id, out var value) != FMOD.RESULT.OK)
                {
                    continue;
                }

                names[written] = _globalParameterNames[i];
                values[written] = value;
                written++;
            }

            return written;
        }

        private void EnsureGlobalParameterCache()
        {
            if (_globalParameters != null)
            {
                return;
            }

            var system = RuntimeManager.StudioSystem;

            if (!system.isValid() ||
                system.getParameterDescriptionList(out var descriptions) != FMOD.RESULT.OK ||
                descriptions == null)
            {
                return;
            }

            var kept = new List<FMOD.Studio.PARAMETER_DESCRIPTION>(descriptions.Length);
            var keptNames = new List<string>(descriptions.Length);

            for (var i = 0; i < descriptions.Length; i++)
            {
                if ((descriptions[i].flags & FMOD.Studio.PARAMETER_FLAGS.AUTOMATIC) != 0)
                {
                    continue;
                }

                kept.Add(descriptions[i]);
                keptNames.Add((string)descriptions[i].name);
            }

            _globalParameters = kept.ToArray();
            _globalParameterNames = keptNames.ToArray();
        }

        public void Tick()
        {
            for (var i = _followCount - 1; i >= 0; i--)
            {
                var voiceId = _following[i];
                var target = _followTargets[voiceId];

                if (target == null)
                {
                    // The emitter was destroyed while its sound was still playing. Left
                    // playing at its last position rather than cut off: a sound that
                    // stops when its object despawns is a decision for the game, not for
                    // the tracer.
                    RemoveFollowerAt(i);
                    continue;
                }

                _instances[voiceId].set3DAttributes(target.To3DAttributes());
            }
        }

        public bool TryGetListenerPosition(out Vector3 position)
        {
            var system = RuntimeManager.StudioSystem;

            if (!system.isValid() ||
                system.getListenerAttributes(0, out var attributes) != FMOD.RESULT.OK)
            {
                position = Vector3.zero;
                return false;
            }

            position = new Vector3(attributes.position.x, attributes.position.y, attributes.position.z);
            return true;
        }

        public double GetEventLengthSeconds(string eventKey)
        {
            if (_lengths.TryGetValue(eventKey, out var cached))
            {
                return cached;
            }

            if (!TryGetDescription(eventKey, out var description, out _) ||
                description.getLength(out var milliseconds) != FMOD.RESULT.OK)
            {
                return 0;
            }

            var seconds = milliseconds / 1000.0;
            _lengths[eventKey] = seconds;
            return seconds;
        }

        /// <summary>
        /// Descriptions are cached; failures are not.
        /// </summary>
        /// <remarks>
        /// A key that does not resolve today may resolve once its bank loads, and
        /// caching the failure would leave a sound permanently silent for the rest of
        /// the session with no sign of why. Re-asking costs a hash lookup inside FMOD.
        /// </remarks>
        private bool TryGetDescription(string eventKey, out EventDescription description, out FMOD.RESULT result)
        {
            if (_descriptions.TryGetValue(eventKey, out description) && description.isValid())
            {
                result = FMOD.RESULT.OK;
                return true;
            }

            result = RuntimeManager.StudioSystem.getEvent(eventKey, out description);

            if (result != FMOD.RESULT.OK || !description.isValid())
            {
                return false;
            }

            _descriptions[eventKey] = description;
            return true;
        }

        private void AddFollower(int voiceId, Transform target)
        {
            _followTargets[voiceId] = target;

            if (_followSlot[voiceId] >= 0)
            {
                return;
            }

            _followSlot[voiceId] = _followCount;
            _following[_followCount++] = voiceId;
        }

        private void RemoveFollower(int voiceId)
        {
            var slot = _followSlot[voiceId];

            if (slot >= 0)
            {
                RemoveFollowerAt(slot);
            }
        }

        private void RemoveFollowerAt(int slot)
        {
            var voiceId = _following[slot];
            var last = --_followCount;

            _following[slot] = _following[last];
            _followSlot[_following[slot]] = slot;

            _followSlot[voiceId] = -1;
            _followTargets[voiceId] = null;
        }

        /// <summary>
        /// Runs on FMOD's update thread. Does the least it possibly can: resolve the
        /// voice, map the callback, push it into a queue. No Unity API, no allocation,
        /// no lock — a callback that blocks here stalls the mixer, and a tracer causing
        /// audio dropouts would be worse than no tracer.
        /// </summary>
        [MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT HandleEventCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            var queue = _sharedSignals;

            if (queue == null || !FmodSignalMap.TryMap(type, out var signal))
            {
                return FMOD.RESULT.OK;
            }

            var instance = new EventInstance(instancePtr);

            if (instance.getUserData(out var userData) != FMOD.RESULT.OK ||
                !TryUnpackUserData(userData, out var epoch, out var voiceId) ||
                epoch != _sharedEpoch)
            {
                // Either an instance somebody else created - the blind spot the module
                // documents rather than guesses at - or one left over from a session that
                // has already ended.
                return FMOD.RESULT.OK;
            }

            queue.TryEnqueue(voiceId, signal, 0, Stopwatch.GetTimestamp());
            return FMOD.RESULT.OK;
        }

        /// <summary>Epoch in the high half, voice id + 1 in the low half.</summary>
        private static IntPtr PackUserData(int epoch, int voiceId) =>
            new IntPtr(((long)epoch << 32) | (uint)(voiceId + 1));

        private static bool TryUnpackUserData(IntPtr userData, out int epoch, out int voiceId)
        {
            var raw = userData.ToInt64();
            var low = (int)(raw & 0xFFFFFFFF);

            epoch = (int)(raw >> 32);
            voiceId = low - 1;

            // Zero is what an instance nobody wired up carries, and what Shutdown writes
            // over the ones it is abandoning.
            return low != 0;
        }
    }

    /// <summary>Puts the FMOD backend on the register at startup.</summary>
    internal static class FmodProbeRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            try
            {
                AudioTrace.RegisterProbe(new FmodRuntimeProbe());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EventTracer] The FMOD backend could not register: {e.Message}");
            }
        }
    }
}
