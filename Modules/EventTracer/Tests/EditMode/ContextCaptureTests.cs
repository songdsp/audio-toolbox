#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;
using AudioToolbox.EventTracer.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// Whether a single record, on its own, says enough to judge the call that made it.
    /// </summary>
    /// <remarks>
    /// The bar the module set itself: someone opens one record and can tell whether the
    /// sound should have been triggered at all, without going back to read the code. That
    /// needs four things on the record — which event, which object, which line, and what
    /// the world looked like. The first three are cheap. The fourth is the parameter
    /// snapshot, and it is the reason "why did this play here" is answerable at all.
    /// </remarks>
    [TestFixture]
    public sealed class ContextCaptureTests
    {
        private FakeRuntimeProbe _probe;
        private readonly List<GameObject> _created = new List<GameObject>();

        private static AudioTraceSettings TestSettings => new AudioTraceSettings
        {
            RecordCapacity = 64,
            MaxConcurrentVoices = 8,
            SignalQueueCapacity = 64,
            InternCapacity = 64,
            EmitterPathCapacity = 32,
            MaxTrackedParameters = 8,
            PendingSnapshotCapacity = 32,

            // Zero so that a pump polls every time and a test never waits on a clock.
            GlobalParameterSampleIntervalSeconds = 0f,
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
        public void TearDown()
        {
            AudioTraceRuntime.Shutdown();

            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        private Transform Emitter(string parentName, string childName)
        {
            var parent = new GameObject(parentName);
            var child = new GameObject(childName);
            _created.Add(parent);
            child.transform.SetParent(parent.transform);
            return child.transform;
        }

        private static AudioTraceRecord LastRecord()
        {
            var records = new List<AudioTraceRecord>();
            AudioTraceRuntime.SnapshotRecords(records);

            Assert.That(records, Is.Not.Empty, "no record was written");
            return records[records.Count - 1];
        }

        [Test]
        public void APostFromATransformRecordsWhichObjectItWas()
        {
            AudioTrace.Post("event:/SFX/Footstep", Emitter("Rifleman", "Boots"));
            AudioTraceRuntime.PumpForTests();

            Assert.That(
                AudioTraceRuntime.ResolveString(LastRecord().EmitterPathId),
                Is.EqualTo("/Rifleman/Boots"));
        }

        [Test]
        public void APostWithNoEmitterHasNoPath()
        {
            // Music and UI have no object behind them, and the record says so rather than
            // inventing one.
            AudioTrace.Post("event:/Music/Menu");
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().EmitterPathId, Is.EqualTo(TraceFormat.NoStringId));
        }

        [Test]
        public void APostAtAPlainPositionHasNoPathEither()
        {
            AudioTrace.Post("event:/SFX/Explosion", new Vector3(1, 2, 3));
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().EmitterPathId, Is.EqualTo(TraceFormat.NoStringId));
        }

        [Test]
        public void TheSameEmitterAcrossManyPostsInternsOnePath()
        {
            var emitter = Emitter("Rifleman", "Boots");

            for (var i = 0; i < 5; i++)
            {
                AudioTrace.Post("event:/SFX/Footstep", emitter);
                AudioTraceRuntime.PumpForTests();
            }

            var records = new List<AudioTraceRecord>();
            AudioTraceRuntime.SnapshotRecords(records);

            var first = records[0].EmitterPathId;

            foreach (var record in records)
            {
                Assert.That(record.EmitterPathId, Is.EqualTo(first));
            }
        }

        [Test]
        public void AParameterSetThroughTheFacadeIsOnTheNextRecord()
        {
            AudioTrace.SetGlobalParameter("Tension", 0.8f);
            AudioTrace.Post("event:/SFX/Sting");
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().ParamSnapshotId, Is.Not.EqualTo(TraceFormat.NoSnapshotId));
        }

        [Test]
        public void APostBeforeAnyParameterIsKnownCarriesNoSnapshot()
        {
            // Distinct from "all parameters were zero". Nothing had been observed yet, and
            // a record claiming otherwise would be making it up.
            AudioTrace.Post("event:/SFX/First");
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().ParamSnapshotId, Is.EqualTo(TraceFormat.NoSnapshotId));
        }

        [Test]
        public void PostsUnderUnchangedStateShareASnapshot()
        {
            AudioTrace.SetGlobalParameter("Tension", 0.8f);

            AudioTrace.Post("event:/SFX/One");
            AudioTraceRuntime.PumpForTests();
            var first = LastRecord().ParamSnapshotId;

            AudioTrace.Post("event:/SFX/Two");
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().ParamSnapshotId, Is.EqualTo(first));
        }

        [Test]
        public void APostAfterAParameterMovesGetsANewSnapshot()
        {
            AudioTrace.SetGlobalParameter("Tension", 0.2f);
            AudioTrace.Post("event:/SFX/Calm");
            AudioTraceRuntime.PumpForTests();
            var calm = LastRecord().ParamSnapshotId;

            AudioTrace.SetGlobalParameter("Tension", 0.9f);
            AudioTrace.Post("event:/SFX/Panic");
            AudioTraceRuntime.PumpForTests();

            Assert.That(LastRecord().ParamSnapshotId, Is.Not.EqualTo(calm));
        }

        [Test]
        public void AParameterTheGameSetBehindTheFacadeIsPickedUpByPolling()
        {
            // The case the poll exists for. Nothing went through AudioTrace, and without
            // the poll the log would show a sound triggered under a state it never saw.
            _probe.GlobalParameters.Add(new KeyValuePair<string, float>("Weather", 3f));

            AudioTraceRuntime.PumpForTests();
            AudioTrace.Post("event:/SFX/Thunder");
            AudioTraceRuntime.PumpForTests();

            Assert.That(_probe.GlobalParameterReadCount, Is.GreaterThan(0), "the backend was never polled");
            Assert.That(LastRecord().ParamSnapshotId, Is.Not.EqualTo(TraceFormat.NoSnapshotId));
        }

        [Test]
        public void PollingIsSkippedEntirelyWhenTheIntervalIsNegative()
        {
            // Off is off. A project that does not want the tracer talking to its middleware
            // on a timer must be able to say so, and get no calls at all.
            var settings = TestSettings;
            settings.GlobalParameterSampleIntervalSeconds = -1f;

            _probe = new FakeRuntimeProbe();
            AudioTraceRuntime.ResetForTests(_probe, settings);

            AudioTraceRuntime.PumpForTests();
            AudioTraceRuntime.PumpForTests();

            Assert.That(_probe.GlobalParameterReadCount, Is.Zero);
        }

        [Test]
        public void TheCallSiteIsTheLineThatPosted()
        {
            AudioTrace.Post("event:/SFX/Gunshot");
            AudioTraceRuntime.PumpForTests();

            var callSite = AudioTraceRuntime.ResolveString(LastRecord().CallSiteId);

            Assert.That(callSite, Does.Contain("ContextCaptureTests.cs:"));
        }
    }
}

#endif
