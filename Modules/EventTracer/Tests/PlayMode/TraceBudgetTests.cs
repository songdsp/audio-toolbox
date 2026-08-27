#if AUDIOTOOLBOX_TRACE

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AudioToolbox.EventTracer.TestSupport;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// The performance budget, as tests rather than as a paragraph in a design document.
    /// </summary>
    /// <remarks>
    /// The collection layer ships in the player, so its budget is a constraint and not an
    /// aspiration: a tracer that allocates once per sound turns a busy scene into a
    /// garbage collection every few seconds, and the frame hitch it introduces is
    /// indistinguishable from the audio problems it was installed to investigate.
    /// <para>
    /// Run against a test double rather than a middleware, on purpose. What is being
    /// measured is the tracer's own cost, and FMOD's would swamp it — the number would
    /// still pass and would no longer mean anything.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class TraceBudgetTests
    {
        private const string EventKey = "event:/Budget/Tone";

        private FakeRuntimeProbe _probe;

        [SetUp]
        public void SetUp()
        {
            _probe = new FakeRuntimeProbe { TrackCalls = false };

            AudioTraceRuntime.ResetForTests(_probe, new AudioTraceSettings
            {
                RecordCapacity = 50_000,
                MaxConcurrentVoices = 512,
                SignalQueueCapacity = 8192,
                InternCapacity = 8192,
                EmitterPathCapacity = 4096,
                MaxTrackedParameters = 256,
                PendingSnapshotCapacity = 1024,

                // Zero polls on every pump, which is the worst case rather than the
                // default one. A budget that only holds when the poll is rare is not a
                // budget.
                GlobalParameterSampleIntervalSeconds = 0f,

                // The disk path is measured separately; a background flush inside the
                // measured window would be attributing the writer's cost to the caller's.
                WriteToDisk = false,
                FlushIntervalSeconds = 2f,
                NaturalEndToleranceSeconds = 0.1,
            });
        }

        [TearDown]
        public void TearDown() => AudioTraceRuntime.Shutdown();

        /// <summary>Post a sound, let it finish, retire its voice.</summary>
        private void OneCompleteSound()
        {
            var handle = AudioTrace.Post(EventKey);
            AudioTraceRuntime.PumpForTests();

            _probe.Emit(handle.VoiceId, ProbeSignal.Stopped);
            _probe.Emit(handle.VoiceId, ProbeSignal.Destroyed);
            AudioTraceRuntime.PumpForTests();
        }

        [Test]
        public void OneRecordIsSmallEnoughForFiftyThousandToFitTheMemoryBudget()
        {
            const int budgetBytes = 8 * 1024 * 1024;
            const int capacity = 50_000;

            var recordSize = Marshal.SizeOf<AudioTraceRecord>();

            Assert.That(
                recordSize * capacity,
                Is.LessThan(budgetBytes),
                $"a record grew to {recordSize} bytes; {capacity} of them no longer fit in 8 MB");
        }

        [UnityTest]
        public IEnumerator PostingAndDrainingAllocatesNothing()
        {
            // Warm-up matters and is not a way of hiding the cost. The first post of a
            // given key interns its name and its call site, and the first call into any
            // method JITs it. Both happen once per session, not once per sound; measuring
            // them would be measuring startup, which has a different budget.
            for (var i = 0; i < 128; i++)
            {
                OneCompleteSound();
            }

            yield return null;

            // Thread-scoped and exact, which a heap-size reading is not: a few hundred
            // bytes of garbage would not move GC.GetTotalMemory at all.
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 2000; i++)
            {
                OneCompleteSound();
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, $"{allocated} bytes allocated across 2000 sounds");
        }

        [UnityTest]
        public IEnumerator CapturingContextAllocatesNothingEither()
        {
            // Everything the context capture adds is a lookup that must not allocate: the
            // emitter path is a dictionary probe on a cached object, the parameter names
            // are interned, and a snapshot taken under unchanged state is an int the pool
            // already had. This is the test that catches the obvious regression — building
            // the scene path per post, or copying the parameter set per record.
            var emitter = new UnityEngine.GameObject("Budget Emitter").transform;

            try
            {
                _probe.GlobalParameters.Add(new KeyValuePair<string, float>("Tension", 0.5f));
                _probe.GlobalParameters.Add(new KeyValuePair<string, float>("Weather", 2f));

                for (var i = 0; i < 128; i++)
                {
                    OneCompleteSoundFrom(emitter);
                }

                yield return null;

                var before = GC.GetAllocatedBytesForCurrentThread();

                for (var i = 0; i < 2000; i++)
                {
                    OneCompleteSoundFrom(emitter);
                }

                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero, $"{allocated} bytes allocated across 2000 sounds with context");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(emitter.gameObject);
            }
        }

        private void OneCompleteSoundFrom(UnityEngine.Transform emitter)
        {
            var handle = AudioTrace.Post(EventKey, emitter);
            AudioTraceRuntime.PumpForTests();

            _probe.Emit(handle.VoiceId, ProbeSignal.Stopped);
            _probe.Emit(handle.VoiceId, ProbeSignal.Destroyed);
            AudioTraceRuntime.PumpForTests();
        }

        [UnityTest]
        public IEnumerator TwoHundredConcurrentSoundsCostUnderHalfAMillisecondPerFrame()
        {
            const int concurrent = 200;
            const int frames = 120;
            const double budgetMilliseconds = 0.5;

            var handles = new AudioTraceHandle[concurrent];

            // Warm up the same paths the measured loop uses.
            for (var i = 0; i < concurrent; i++)
            {
                handles[i] = AudioTrace.Post(EventKey);
            }

            AudioTraceRuntime.PumpForTests();

            for (var i = 0; i < concurrent; i++)
            {
                _probe.Emit(handles[i].VoiceId, ProbeSignal.Stopped);
                _probe.Emit(handles[i].VoiceId, ProbeSignal.Destroyed);
            }

            AudioTraceRuntime.PumpForTests();
            yield return null;

            var clock = new Stopwatch();

            for (var frame = 0; frame < frames; frame++)
            {
                clock.Start();

                for (var i = 0; i < concurrent; i++)
                {
                    handles[i] = AudioTrace.Post(EventKey);
                }

                AudioTraceRuntime.PumpForTests();

                for (var i = 0; i < concurrent; i++)
                {
                    _probe.Emit(handles[i].VoiceId, ProbeSignal.Stopped);
                    _probe.Emit(handles[i].VoiceId, ProbeSignal.Destroyed);
                }

                AudioTraceRuntime.PumpForTests();

                clock.Stop();
            }

            var perFrame = clock.Elapsed.TotalMilliseconds / frames;

            Assert.That(
                perFrame,
                Is.LessThan(budgetMilliseconds),
                $"{perFrame:0.000} ms per frame for {concurrent} concurrent sounds");
        }

        [UnityTest]
        public IEnumerator TheRingBufferKeepsTheNewestRecordsAndSaysWhatItLost()
        {
            // Deliberately overflow a session and check that the result is an honest
            // truncation rather than a quietly short log. Reporting the tail as if it
            // were the whole session is how someone concludes a sound was never posted.
            AudioTraceRuntime.Shutdown();

            _probe = new FakeRuntimeProbe { TrackCalls = false };

            AudioTraceRuntime.ResetForTests(_probe, new AudioTraceSettings
            {
                RecordCapacity = 64,
                MaxConcurrentVoices = 16,
                SignalQueueCapacity = 512,
                InternCapacity = 64,
                WriteToDisk = false,
                FlushIntervalSeconds = 60f,
                NaturalEndToleranceSeconds = 0.1,
            });

            for (var i = 0; i < 200; i++)
            {
                OneCompleteSound();
            }

            yield return null;

            var header = AudioTraceRuntime.Header;

            Assert.That(header.IsTruncated, Is.True, "a session that lost records claimed to be complete");
            Assert.That(header.DroppedRecordCount, Is.EqualTo(200 - 64));
        }
    }
}

#endif
