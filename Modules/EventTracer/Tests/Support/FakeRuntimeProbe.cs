using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace AudioToolbox.EventTracer.TestSupport
{
    /// <summary>
    /// A backend that reports exactly the signals a test tells it to, when it tells it to.
    /// </summary>
    /// <remarks>
    /// Splits one hard question into two answerable ones. "Does a stop before the event's
    /// length with nobody asking come out as Stolen" is settled here, deterministically,
    /// in milliseconds, on any machine. "Does FMOD actually emit that sequence when an
    /// instance is stolen" is settled by the PlayMode tests against a real bank. Testing
    /// only through FMOD would conflate the two, and a red test would leave you guessing
    /// which half was wrong.
    /// </remarks>
    public sealed class FakeRuntimeProbe : IAudioRuntimeProbe
    {
        private long[] _startTimestamps = new long[0];

        /// <summary>
        /// Whether to keep a log of what was asked of it.
        /// </summary>
        /// <remarks>
        /// Off for the allocation tests. Growing a list is an allocation, and a test
        /// asserting that a post allocates nothing must not be measuring its own
        /// bookkeeping.
        /// </remarks>
        public bool TrackCalls = true;

        /// <summary>Every event key passed to <see cref="Play"/>, in order. Empty unless <see cref="TrackCalls"/>.</summary>
        public readonly List<string> PlayedKeys = new List<string>();

        /// <summary>Voices the facade asked to stop.</summary>
        public readonly List<int> StoppedVoices = new List<int>();

        /// <summary>Keys that fail to produce an instance, with the code to report.</summary>
        public readonly Dictionary<string, int> FailingKeys = new Dictionary<string, int>();

        /// <summary>Reported by <see cref="GetEventLengthSeconds"/> for any key not in <see cref="Lengths"/>.</summary>
        public double DefaultLengthSeconds = 4.0;

        public readonly Dictionary<string, double> Lengths = new Dictionary<string, double>();

        /// <summary>When true, Play reports CreateOk and Started by itself.</summary>
        public bool AutoStart = true;

        public Vector3 ListenerPosition = Vector3.zero;

        public bool HasListener = true;

        public ProbeSignalQueue Signals { get; private set; }

        public string BackendId => "fake";

        public string DisplayName => "Test double";

        /// <summary>Above FMOD's, so a test's probe wins wherever both are registered.</summary>
        public int Priority => 1000;

        public bool IsAvailable => true;

        public string GetUnavailableReason() => string.Empty;

        public string GetBackendVersion() => "test";

        public void Initialize(ProbeSignalQueue signals, int maxVoices)
        {
            Signals = signals;
            _startTimestamps = new long[maxVoices];
        }

        public void Shutdown() => Signals = null;

        public bool Play(in PlayRequest request, int voiceId, out int backendResultCode)
        {
            if (TrackCalls)
            {
                PlayedKeys.Add(request.EventKey);
            }

            _startTimestamps[voiceId] = Stopwatch.GetTimestamp();

            if (FailingKeys.Count > 0 && FailingKeys.TryGetValue(request.EventKey, out var failureCode))
            {
                backendResultCode = failureCode;
                Emit(voiceId, ProbeSignal.CreateFailed, failureCode);
                return false;
            }

            backendResultCode = 0;

            if (AutoStart)
            {
                Emit(voiceId, ProbeSignal.CreateOk);
                Emit(voiceId, ProbeSignal.Started);
            }

            return true;
        }

        public void Stop(int voiceId)
        {
            if (TrackCalls)
            {
                StoppedVoices.Add(voiceId);
            }
        }

        public void SetParameter(int voiceId, string name, float value)
        {
        }

        public void SetGlobalParameter(string name, float value)
        {
        }

        /// <summary>
        /// What a poll of this backend finds. Set it to stand in for a parameter the game
        /// changed without going through the facade.
        /// </summary>
        public readonly List<KeyValuePair<string, float>> GlobalParameters =
            new List<KeyValuePair<string, float>>();

        /// <summary>How many times the recorder has polled. Lets a test assert the interval holds.</summary>
        public int GlobalParameterReadCount { get; private set; }

        public int ReadGlobalParameters(string[] names, float[] values)
        {
            GlobalParameterReadCount++;

            var count = Mathf.Min(GlobalParameters.Count, Mathf.Min(names.Length, values.Length));

            for (var i = 0; i < count; i++)
            {
                names[i] = GlobalParameters[i].Key;
                values[i] = GlobalParameters[i].Value;
            }

            return count;
        }

        public void Tick()
        {
        }

        public bool TryGetListenerPosition(out Vector3 position)
        {
            position = ListenerPosition;
            return HasListener;
        }

        public double GetEventLengthSeconds(string eventKey) =>
            Lengths.TryGetValue(eventKey, out var length) ? length : DefaultLengthSeconds;

        /// <summary>Reports a signal as of now.</summary>
        public void Emit(int voiceId, ProbeSignal signal, int resultCode = 0) =>
            Signals.TryEnqueue(voiceId, signal, resultCode, Stopwatch.GetTimestamp());

        /// <summary>
        /// Reports a signal as if it had arrived <paramref name="secondsAfterStart"/>
        /// into the sound. Lets a test say "this stopped three seconds into a four second
        /// event" without waiting three seconds.
        /// </summary>
        public void EmitAt(int voiceId, ProbeSignal signal, double secondsAfterStart, int resultCode = 0)
        {
            var start = _startTimestamps[voiceId];
            var timestamp = start + (long)(secondsAfterStart * Stopwatch.Frequency);
            Signals.TryEnqueue(voiceId, signal, resultCode, timestamp);
        }
    }
}
