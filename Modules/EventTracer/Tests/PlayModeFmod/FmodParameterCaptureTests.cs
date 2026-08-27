using System.Collections;
using System.Collections.Generic;
using AudioToolbox.EventTracer.Backends.Fmod;
using FMODUnity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioToolbox.EventTracer.Tests.Fmod
{
    /// <summary>
    /// Reading the world's parameters back out of a real FMOD system.
    /// </summary>
    /// <remarks>
    /// The pool, the differential storage and the file format are all settled without
    /// middleware. What is not settled anywhere else is whether FMOD hands over its global
    /// parameters at all, under the names the project gave them — the description list,
    /// the marshalling of those names out of native memory, and the filtering that keeps
    /// per-instance automatic parameters out of a snapshot that claims to be about the
    /// world.
    /// <para>
    /// The parameter is authored by <c>Tools/TraceFixture~</c> alongside the events. Its
    /// name is deliberately unlike anything a project would already have, so a pass here
    /// cannot come from finding somebody else's parameter.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class FmodParameterCaptureTests
    {
        private const string Parameter = "AudioToolboxTraceTension";
        private const string Basic2D = "event:/AudioToolboxTrace/Basic2D";

        private FmodRuntimeProbe _probe;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _probe = new FmodRuntimeProbe();

            AudioTraceRuntime.ResetForTests(_probe, new AudioTraceSettings
            {
                RecordCapacity = 128,
                MaxConcurrentVoices = 16,
                SignalQueueCapacity = 128,
                InternCapacity = 128,
                EmitterPathCapacity = 32,
                MaxTrackedParameters = 64,
                PendingSnapshotCapacity = 64,

                // No interval: the poll happens on the pump the test drives, so nothing
                // here waits on a wall clock.
                GlobalParameterSampleIntervalSeconds = 0f,
                WriteToDisk = false,
                FlushIntervalSeconds = 60f,
                NaturalEndToleranceSeconds = 0.1,
            });

            Assert.That(AudioTrace.BackendId, Is.EqualTo("fmod"), "FMOD did not initialise");

            for (var frame = 0; frame < 240 && !RuntimeManager.HaveAllBanksLoaded; frame++)
            {
                yield return null;
            }

            Assert.That(RuntimeManager.HaveAllBanksLoaded, Is.True, "FMOD banks never finished loading");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AudioTraceRuntime.Shutdown();

            for (var i = 0; i < 30; i++)
            {
                yield return null;
            }
        }

        private static List<string> ReadNames(FmodRuntimeProbe probe)
        {
            var names = new string[64];
            var values = new float[64];
            var count = probe.ReadGlobalParameters(names, values);
            var found = new List<string>();

            for (var i = 0; i < count; i++)
            {
                found.Add(names[i]);
            }

            return found;
        }

        [Test]
        public void TheFixtureParameterIsAmongTheOnesFmodReports()
        {
            Assert.That(
                ReadNames(_probe),
                Contains.Item(Parameter),
                $"{Parameter} is missing. Run Tools/TraceFixture~/build-trace-fixture.ps1 against the FMOD project.");
        }

        [UnityTest]
        public IEnumerator AValueSetThroughTheFacadeComesBackFromFmod()
        {
            AudioTrace.SetGlobalParameter(Parameter, 0.75f);

            // Studio applies parameter changes on its own update, so the value is not
            // readable back on the same frame it was set.
            yield return null;
            yield return null;

            var names = new string[64];
            var values = new float[64];
            var count = _probe.ReadGlobalParameters(names, values);
            var read = float.NaN;

            for (var i = 0; i < count; i++)
            {
                if (names[i] == Parameter)
                {
                    read = values[i];
                }
            }

            Assert.That(read, Is.EqualTo(0.75f).Within(1e-3f));
        }

        [Test]
        public void EveryNameFmodReportsIsRealText()
        {
            // The failure this catches is marshalling: FMOD hands names over as a
            // StringWrapper around native memory, and getting that wrong produces a list
            // of empty strings rather than an error. The log would then be full of
            // parameters called "".
            var names = ReadNames(_probe);

            Assert.That(names, Is.Not.Empty, "FMOD reported no global parameters at all");

            foreach (var name in names)
            {
                Assert.That(name, Is.Not.Null.And.Not.Empty);
            }
        }

        [UnityTest]
        public IEnumerator AParameterSetWithoutTheFacadeStillReachesTheRecord()
        {
            // The case polling exists for, done properly: nothing goes through AudioTrace,
            // so the only route from this value to the record is the tracer asking FMOD.
            RuntimeManager.StudioSystem.setParameterByName(Parameter, 0.6f);

            // Studio applies parameter changes on its own update, so the value is not
            // readable back on the frame it was set.
            yield return null;
            yield return null;

            AudioTraceRuntime.PumpForTests();

            var handle = AudioTrace.Post(Basic2D);

            Assert.That(handle.IsValid, Is.True, $"{Basic2D} did not play");

            var records = new List<AudioTraceRecord>();
            AudioTraceRuntime.SnapshotRecords(records);

            Assert.That(records, Is.Not.Empty);
            Assert.That(
                records[records.Count - 1].ParamSnapshotId,
                Is.Not.EqualTo(TraceFormat.NoSnapshotId),
                "the post recorded no parameter state, so nothing was polled from FMOD");

            AudioTrace.Stop(handle);
        }
    }
}
