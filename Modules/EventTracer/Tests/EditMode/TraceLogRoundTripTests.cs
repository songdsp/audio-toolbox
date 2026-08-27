#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;
using System.IO;
using AudioToolbox.EventTracer.Editor;
using AudioToolbox.EventTracer.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// Writes a session to disk the way a build does, then reads it back the way the
    /// editor does.
    /// </summary>
    /// <remarks>
    /// The acceptance criterion this covers is "a log exported from a packaged build can
    /// be restored in full in the editor", and it is the one that would otherwise be
    /// discovered broken by a QA engineer on a Friday. Writer and reader share nothing
    /// but <see cref="TraceFormat"/>, so a change to either side without the other fails
    /// here rather than in the field.
    /// </remarks>
    [TestFixture]
    public sealed class TraceLogRoundTripTests
    {
        private FakeRuntimeProbe _probe;
        private string _sessionPath;

        [SetUp]
        public void SetUp()
        {
            _probe = new FakeRuntimeProbe();

            AudioTraceRuntime.ResetForTests(_probe, new AudioTraceSettings
            {
                RecordCapacity = 256,
                MaxConcurrentVoices = 8,
                SignalQueueCapacity = 128,
                InternCapacity = 64,
                EmitterPathCapacity = 32,
                MaxTrackedParameters = 8,
                PendingSnapshotCapacity = 32,
                GlobalParameterSampleIntervalSeconds = 0f,
                WriteToDisk = true,
                FlushIntervalSeconds = 0.1f,
                NaturalEndToleranceSeconds = 0.1,
            });

            _sessionPath = AudioTrace.SessionPath;
        }

        [TearDown]
        public void TearDown()
        {
            AudioTraceRuntime.Shutdown();

            if (!string.IsNullOrEmpty(_sessionPath) && File.Exists(_sessionPath))
            {
                File.Delete(_sessionPath);
            }
        }

        private void PostAndFinish(string eventKey, Vector3 position, ProbeSignal ending, double at)
        {
            var handle = AudioTrace.Post(eventKey, position);
            AudioTraceRuntime.PumpForTests();

            if (ending == ProbeSignal.StopRequested)
            {
                AudioTrace.Stop(handle);
                _probe.EmitAt(handle.VoiceIdForTests(), ProbeSignal.Stopped, at);
            }
            else
            {
                _probe.EmitAt(handle.VoiceIdForTests(), ending, at);
            }

            _probe.EmitAt(handle.VoiceIdForTests(), ProbeSignal.Destroyed, at);
            AudioTraceRuntime.PumpForTests();
        }

        [Test]
        public void ASessionSurvivesBeingWrittenAndReadBack()
        {
            Assert.That(_sessionPath, Is.Not.Empty, "no session file was opened");

            PostAndFinish("event:/SFX/Gunshot", new Vector3(3, 0, 4), ProbeSignal.Stopped, 4.0);
            PostAndFinish("event:/SFX/Footstep", new Vector3(0, 0, 1), ProbeSignal.StopRequested, 0.5);
            PostAndFinish("event:/SFX/Gunshot", new Vector3(0, 0, 0), ProbeSignal.Stopped, 1.0);

            var expected = new List<AudioTraceRecord>();
            AudioTraceRuntime.SnapshotRecords(expected);

            // Shutdown flushes what is left and joins the writer thread, which is what a
            // build does on quit.
            AudioTraceRuntime.Shutdown();

            var session = TraceLogReader.Read(_sessionPath);

            Assert.That(session.EndedAbruptly, Is.False);
            Assert.That(session.Records.Count, Is.EqualTo(expected.Count));

            for (var i = 0; i < expected.Count; i++)
            {
                var wrote = expected[i];
                var read = session.Records[i];

                Assert.That(read.Frame, Is.EqualTo(wrote.Frame), $"record {i} frame");
                Assert.That(read.TimeSeconds, Is.EqualTo(wrote.TimeSeconds).Within(1e-9), $"record {i} time");
                Assert.That(read.EventKeyId, Is.EqualTo(wrote.EventKeyId), $"record {i} event key id");
                Assert.That(read.CallSiteId, Is.EqualTo(wrote.CallSiteId), $"record {i} call site id");
                Assert.That(read.EmitterPathId, Is.EqualTo(wrote.EmitterPathId), $"record {i} emitter path id");
                Assert.That(read.EmitterPos, Is.EqualTo(wrote.EmitterPos), $"record {i} emitter position");
                Assert.That(read.ListenerPos, Is.EqualTo(wrote.ListenerPos), $"record {i} listener position");
                Assert.That(read.DistanceToListener, Is.EqualTo(wrote.DistanceToListener).Within(1e-4f), $"record {i} distance");
                Assert.That(read.Outcome, Is.EqualTo(wrote.Outcome), $"record {i} outcome");
                Assert.That(read.BackendResultCode, Is.EqualTo(wrote.BackendResultCode), $"record {i} backend code");
                Assert.That(read.ParamSnapshotId, Is.EqualTo(wrote.ParamSnapshotId), $"record {i} snapshot id");
            }
        }

        [Test]
        public void TheOutcomesReadBackAreTheOnesThatWereRecorded()
        {
            PostAndFinish("event:/SFX/Gunshot", Vector3.zero, ProbeSignal.Stopped, 4.0);
            PostAndFinish("event:/SFX/Cut", Vector3.zero, ProbeSignal.StopRequested, 0.5);
            PostAndFinish("event:/SFX/Taken", Vector3.zero, ProbeSignal.Stopped, 0.5);

            AudioTraceRuntime.Shutdown();

            var session = TraceLogReader.Read(_sessionPath);

            Assert.That(
                session.Records.ConvertAll(r => r.Outcome),
                Is.EqualTo(new[]
                {
                    PlaybackOutcome.Started,
                    PlaybackOutcome.StoppedEarly,
                    PlaybackOutcome.Stolen,
                }));
        }

        [Test]
        public void EveryStringAReadRecordPointsAtIsInTheFile()
        {
            // The failure this guards against is subtle and total: records that survive
            // but resolve to nothing, leaving a log of numbered outcomes with no event
            // names. It happens the moment strings are emitted after the records that use
            // them.
            PostAndFinish("event:/SFX/Gunshot", Vector3.zero, ProbeSignal.Stopped, 4.0);
            PostAndFinish("event:/Music/Menu", Vector3.zero, ProbeSignal.Stopped, 4.0);

            AudioTraceRuntime.Shutdown();

            var session = TraceLogReader.Read(_sessionPath);
            var keys = new List<string>();

            foreach (var record in session.Records)
            {
                Assert.That(session.Strings.ContainsKey(record.EventKeyId), Is.True, "an event key id resolved to nothing");
                Assert.That(session.Strings.ContainsKey(record.CallSiteId), Is.True, "a call site id resolved to nothing");
                keys.Add(session.Resolve(record.EventKeyId));
            }

            Assert.That(keys, Is.EqualTo(new[] { "event:/SFX/Gunshot", "event:/Music/Menu" }));
        }

        [Test]
        public void TheParametersARecordWasPostedUnderComeBackWhole()
        {
            // The point of the whole differential scheme: what is written is only what
            // changed, and what is read back is the entire state anyway. If this ever
            // fails, the log is smaller than it should be in the worst possible way.
            AudioTrace.SetGlobalParameter("Tension", 0.2f);
            AudioTrace.SetGlobalParameter("Weather", 1f);
            PostAndFinish("event:/SFX/Calm", Vector3.zero, ProbeSignal.Stopped, 4.0);

            AudioTrace.SetGlobalParameter("Tension", 0.9f);
            PostAndFinish("event:/SFX/Panic", Vector3.zero, ProbeSignal.Stopped, 4.0);

            AudioTraceRuntime.Shutdown();

            var session = TraceLogReader.Read(_sessionPath);
            var values = new Dictionary<string, float>();

            Assert.That(session.Records.Count, Is.EqualTo(2));

            Assert.That(session.TryResolveParameters(session.Records[0].ParamSnapshotId, values), Is.True);
            Assert.That(values["Tension"], Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(values["Weather"], Is.EqualTo(1f).Within(1e-5f));

            Assert.That(session.TryResolveParameters(session.Records[1].ParamSnapshotId, values), Is.True);
            Assert.That(values["Tension"], Is.EqualTo(0.9f).Within(1e-5f), "the changed value did not carry");
            Assert.That(
                values["Weather"],
                Is.EqualTo(1f).Within(1e-5f),
                "an unchanged parameter was lost — the snapshot chain is not being walked");
        }

        [Test]
        public void ARecordWithNoSnapshotSaysSoRatherThanResolvingToNothing()
        {
            PostAndFinish("event:/SFX/Gunshot", Vector3.zero, ProbeSignal.Stopped, 4.0);
            AudioTraceRuntime.Shutdown();

            var session = TraceLogReader.Read(_sessionPath);
            var values = new Dictionary<string, float>();

            Assert.That(session.Records[0].ParamSnapshotId, Is.EqualTo(TraceFormat.NoSnapshotId));
            Assert.That(session.TryResolveParameters(session.Records[0].ParamSnapshotId, values), Is.False);
        }

        [Test]
        public void AnEmitterPathSurvivesTheRoundTrip()
        {
            var emitter = new GameObject("Turret").transform;

            try
            {
                var handle = AudioTrace.Post("event:/SFX/Gunshot", emitter);
                AudioTraceRuntime.PumpForTests();

                // A record is only written once its voice can no longer change, so the
                // sound has to end before there is anything on disk to read.
                _probe.EmitAt(handle.VoiceIdForTests(), ProbeSignal.Stopped, 4.0);
                _probe.EmitAt(handle.VoiceIdForTests(), ProbeSignal.Destroyed, 4.0);
                AudioTraceRuntime.PumpForTests();

                AudioTraceRuntime.Shutdown();

                var session = TraceLogReader.Read(_sessionPath);

                Assert.That(session.Records, Is.Not.Empty);
                Assert.That(session.Resolve(session.Records[0].EmitterPathId), Is.EqualTo("/Turret"));
            }
            finally
            {
                Object.DestroyImmediate(emitter.gameObject);
            }
        }

        [Test]
        public void TheHeaderSaysWhereTheSessionCameFrom()
        {
            PostAndFinish("event:/SFX/Gunshot", Vector3.zero, ProbeSignal.Stopped, 4.0);
            AudioTraceRuntime.Shutdown();

            var header = TraceLogReader.Read(_sessionPath).Header;

            Assert.That(header.FormatVersion, Is.EqualTo(TraceFormat.Version));
            Assert.That(header.BackendId, Is.EqualTo("fake"));
            Assert.That(header.UnityVersion, Is.EqualTo(Application.unityVersion));
            Assert.That(header.Platform, Is.EqualTo(Application.platform.ToString()));
            Assert.That(header.StartedUtc, Is.Not.Empty);
            Assert.That(header.RecordCapacity, Is.EqualTo(256));
        }

        [Test]
        public void ALogTruncatedMidChunk_StillYieldsTheRecordsBeforeIt()
        {
            // What a crashed build leaves behind, and the case the chunked format exists
            // for. The records already on disk have to survive it.
            PostAndFinish("event:/SFX/Gunshot", Vector3.zero, ProbeSignal.Stopped, 4.0);
            PostAndFinish("event:/SFX/Footstep", Vector3.zero, ProbeSignal.Stopped, 4.0);

            AudioTraceRuntime.Shutdown();

            var whole = File.ReadAllBytes(_sessionPath);
            var truncated = Path.ChangeExtension(_sessionPath, ".truncated" + TraceFormat.FileExtension);

            try
            {
                // Cut ten bytes: enough to leave the last record half-written.
                using (var stream = File.Create(truncated))
                {
                    stream.Write(whole, 0, whole.Length - 10);
                }

                var session = TraceLogReader.Read(truncated);

                Assert.That(session.EndedAbruptly, Is.True);
                Assert.That(session.Records, Is.Not.Empty, "a truncated log gave up everything");
                Assert.That(session.Strings, Is.Not.Empty, "the strings did not survive the truncation");
            }
            finally
            {
                if (File.Exists(truncated))
                {
                    File.Delete(truncated);
                }
            }
        }

        [Test]
        public void AFileThatIsNotATraceLogIsRefused()
        {
            var bogus = Path.Combine(Path.GetTempPath(), "not-a-trace" + TraceFormat.FileExtension);
            File.WriteAllText(bogus, "this is not a trace log at all, but it is long enough");

            try
            {
                Assert.Throws<InvalidDataException>(() => TraceLogReader.Read(bogus));
            }
            finally
            {
                File.Delete(bogus);
            }
        }
    }
}

#endif
