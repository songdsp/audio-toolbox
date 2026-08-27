#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;
using AudioToolbox.EventTracer.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// The whole pipeline — facade, voice slots, recorder, ring buffer — driven against a
    /// backend that reports exactly what the test says.
    /// </summary>
    /// <remarks>
    /// Where <see cref="OutcomeStateMachineTests"/> checks the mapping in isolation, this
    /// checks that a post actually reaches it: that the facade opens a record, that a
    /// signal drained a frame later finds the right one, and that the outcome ends up on
    /// the record rather than only in the state machine's head.
    /// </remarks>
    [TestFixture]
    public sealed class SessionPipelineTests
    {
        private FakeRuntimeProbe _probe;

        private static AudioTraceSettings TestSettings => new AudioTraceSettings
        {
            RecordCapacity = 64,
            MaxConcurrentVoices = 8,
            SignalQueueCapacity = 64,
            InternCapacity = 64,
            WriteToDisk = false,
            FlushIntervalSeconds = 1f,
            NaturalEndToleranceSeconds = 0.1,
        };

        [SetUp]
        public void SetUp()
        {
            _probe = new FakeRuntimeProbe();
            AudioTraceRuntime.ResetForTests(_probe, TestSettings);
        }

        [TearDown]
        public void TearDown() => AudioTraceRuntime.Shutdown();

        private static AudioTraceRecord LastRecord()
        {
            var records = new List<AudioTraceRecord>();
            AudioTraceRuntime.SnapshotRecords(records);

            Assert.That(records, Is.Not.Empty, "no record was written");
            return records[records.Count - 1];
        }

        [Test]
        public void TheFakeProbeIsTheOneInUse()
        {
            Assert.That(AudioTrace.BackendId, Is.EqualTo("fake"));
            Assert.That(AudioTrace.IsRecording, Is.True);
        }

        [Test]
        public void APostThatPlays_RecordsStarted()
        {
            var handle = AudioTrace.Post("event:/SFX/Gunshot");
            AudioTraceRuntime.PumpForTests();

            Assert.That(handle.IsValid, Is.True);
            Assert.That(AudioTraceRuntime.GetOutcome(handle), Is.EqualTo(PlaybackOutcome.Started));
            Assert.That(LastRecord().Outcome, Is.EqualTo(PlaybackOutcome.Started));
        }

        [Test]
        public void APostThatCannotBeCreated_RecordsHandleInvalidWithTheRawCode()
        {
            _probe.FailingKeys["event:/Missing"] = 74;

            var handle = AudioTrace.Post("event:/Missing");
            AudioTraceRuntime.PumpForTests();

            Assert.That(handle.IsValid, Is.False, "an unplayable sound must not hand back a usable handle");

            var record = LastRecord();
            Assert.That(record.Outcome, Is.EqualTo(PlaybackOutcome.HandleInvalid));
            Assert.That(record.BackendResultCode, Is.EqualTo(74), "the middleware's own code must survive normalisation");
        }

        [Test]
        public void AVoiceThatNeverStarts_RecordsRejected()
        {
            _probe.AutoStart = false;

            var handle = AudioTrace.Post("event:/SFX/Capped");
            _probe.Emit(0, ProbeSignal.CreateOk);
            _probe.EmitAt(0, ProbeSignal.Destroyed, 0.01);
            AudioTraceRuntime.PumpForTests();

            Assert.That(AudioTraceRuntime.GetOutcome(handle), Is.EqualTo(PlaybackOutcome.Rejected));
        }

        [Test]
        public void StoppingThroughTheFacade_RecordsStoppedEarly()
        {
            var handle = AudioTrace.Post("event:/SFX/Loop");
            AudioTraceRuntime.PumpForTests();

            AudioTrace.Stop(handle);
            Assert.That(_probe.StoppedVoices, Does.Contain(0), "the backend was never asked to stop");

            _probe.EmitAt(0, ProbeSignal.Stopped, 1.0);
            _probe.EmitAt(0, ProbeSignal.Destroyed, 1.0);
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().Outcome, Is.EqualTo(PlaybackOutcome.StoppedEarly));
        }

        [Test]
        public void StoppingWithoutAskingFirst_RecordsStolen()
        {
            var handle = AudioTrace.Post("event:/SFX/Loop");
            AudioTraceRuntime.PumpForTests();

            _probe.EmitAt(0, ProbeSignal.Stopped, 1.0);
            _probe.EmitAt(0, ProbeSignal.Destroyed, 1.0);
            AudioTraceRuntime.PumpForTests();

            Assert.That(AudioTraceRuntime.GetOutcome(handle), Is.EqualTo(PlaybackOutcome.Stolen));
        }

        [Test]
        public void GoingVirtual_RecordsVirtualized()
        {
            var handle = AudioTrace.Post("event:/Amb/Torch");
            AudioTraceRuntime.PumpForTests();

            _probe.EmitAt(0, ProbeSignal.WentVirtual, 0.5);
            AudioTraceRuntime.PumpForTests();

            Assert.That(AudioTraceRuntime.GetOutcome(handle), Is.EqualTo(PlaybackOutcome.Virtualized));
            Assert.That(LastRecord().Outcome, Is.EqualTo(PlaybackOutcome.Virtualized));
        }

        [Test]
        public void PlayingToItsEnd_StaysStarted()
        {
            var handle = AudioTrace.Post("event:/SFX/Gunshot");
            AudioTraceRuntime.PumpForTests();

            _probe.EmitAt(0, ProbeSignal.Stopped, _probe.DefaultLengthSeconds);
            _probe.EmitAt(0, ProbeSignal.Destroyed, _probe.DefaultLengthSeconds);
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().Outcome, Is.EqualTo(PlaybackOutcome.Started));
        }

        [Test]
        public void APositionedPost_RecordsTheDistanceToTheListener()
        {
            _probe.ListenerPosition = new Vector3(0, 0, 0);

            AudioTrace.Post("event:/SFX/Distant", new Vector3(3, 0, 4));
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().DistanceToListener, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void APostWithNoPosition_RecordsNoDistanceRatherThanZero()
        {
            // Zero would read as "right on top of the listener", which is a plausible and
            // entirely wrong answer. -1 cannot be mistaken for a measurement.
            AudioTrace.Post("event:/UI/Click");
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().DistanceToListener, Is.EqualTo(-1f));
        }

        [Test]
        public void WithNoListenerInTheScene_NoDistanceIsRecorded()
        {
            _probe.HasListener = false;

            AudioTrace.Post("event:/SFX/Distant", new Vector3(3, 0, 4));
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().DistanceToListener, Is.EqualTo(-1f));
        }

        [Test]
        public void TheCallSiteOfThePostIsRecorded()
        {
            AudioTrace.Post("event:/SFX/Gunshot");
            AudioTraceRuntime.PumpForTests();

            var callSite = AudioTraceRuntime.ResolveString(LastRecord().CallSiteId);

            Assert.That(callSite, Does.Contain("SessionPipelineTests.cs"));
            Assert.That(callSite, Does.Match(@":\d+$"), "the line number is what makes it navigable");
        }

        [Test]
        public void VoiceSlotsAreReturnedWhenSoundsEnd()
        {
            // Eight slots, sixteen sounds. Without recycling, the ninth post would fail -
            // which is the shape of a leak that only shows up in a long session.
            for (var i = 0; i < 16; i++)
            {
                var handle = AudioTrace.Post("event:/SFX/Gunshot");
                Assert.That(handle.IsValid, Is.True, $"post {i} was refused a voice");

                AudioTraceRuntime.PumpForTests();
                _probe.EmitAt(handle.VoiceIdForTests(), ProbeSignal.Stopped, 4.0);
                _probe.EmitAt(handle.VoiceIdForTests(), ProbeSignal.Destroyed, 4.0);
                AudioTraceRuntime.PumpForTests();
            }
        }

        [Test]
        public void AHandleHeldPastTheEndOfItsSound_StopsNothing()
        {
            var handle = AudioTrace.Post("event:/SFX/Gunshot");
            AudioTraceRuntime.PumpForTests();

            _probe.EmitAt(0, ProbeSignal.Stopped, 4.0);
            _probe.EmitAt(0, ProbeSignal.Destroyed, 4.0);
            AudioTraceRuntime.PumpForTests();

            _probe.StoppedVoices.Clear();
            AudioTrace.Post("event:/SFX/Other");
            AudioTraceRuntime.PumpForTests();

            AudioTrace.Stop(handle);

            Assert.That(AudioTrace.IsAlive(handle), Is.False);
            Assert.That(_probe.StoppedVoices, Is.Empty, "a stale handle stopped the sound that took its slot");
        }

        [Test]
        public void RunningOutOfVoices_RefusesRatherThanOverwriting()
        {
            _probe.AutoStart = true;

            for (var i = 0; i < 8; i++)
            {
                Assert.That(AudioTrace.Post("event:/SFX/Gunshot").IsValid, Is.True);
            }

            Assert.That(AudioTrace.Post("event:/SFX/Gunshot").IsValid, Is.False);
        }
    }

    internal static class HandleTestExtensions
    {
        /// <summary>
        /// The voice id behind a handle. Tests need it to address the fake probe, which
        /// speaks in voice ids because that is what a real backend is handed.
        /// </summary>
        public static int VoiceIdForTests(this AudioTraceHandle handle) => handle.VoiceId;
    }
}

#endif
