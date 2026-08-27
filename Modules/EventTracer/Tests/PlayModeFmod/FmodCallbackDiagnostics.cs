using System.Collections;
using System.Text;
using AudioToolbox.EventTracer.Backends.Fmod;
using FMODUnity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioToolbox.EventTracer.Tests.Fmod
{
    /// <summary>
    /// Not an assertion, a microscope. Prints what FMOD actually reports, frame by frame.
    /// </summary>
    [TestFixture]
    [Explicit("Diagnostic: run by name when the outcome mapping needs re-checking against FMOD.")]
    public sealed class FmodCallbackDiagnostics
    {
        private const string Folder = "event:/AudioToolboxTrace/";

        private static IEnumerator Watch(string label, string eventKey, int posts, int frames)
        {
            AudioTraceRuntime.ResetForTests(new FmodRuntimeProbe(), new AudioTraceSettings
            {
                RecordCapacity = 256,
                MaxConcurrentVoices = 16,
                SignalQueueCapacity = 256,
                InternCapacity = 64,
                WriteToDisk = false,
                FlushIntervalSeconds = 60f,
                NaturalEndToleranceSeconds = 0.1,
            });

            for (var i = 0; i < 240 && !RuntimeManager.HaveAllBanksLoaded; i++)
            {
                yield return null;
            }

            var handles = new AudioTraceHandle[posts];
            var report = new StringBuilder();

            report.AppendLine($"### {label} ({eventKey}) backend={AudioTrace.BackendId}");

            for (var i = 0; i < posts; i++)
            {
                handles[i] = AudioTrace.Post(eventKey);
                report.AppendLine($"  post {i}: valid={handles[i].IsValid}");

                for (var f = 0; f < 6; f++)
                {
                    yield return null;
                }
            }

            var previous = new PlaybackOutcome[posts];

            for (var frame = 0; frame < frames; frame++)
            {
                for (var i = 0; i < posts; i++)
                {
                    var outcome = AudioTraceRuntime.GetOutcome(handles[i]);

                    if (frame == 0 || outcome != previous[i])
                    {
                        report.AppendLine($"  frame {frame,3} voice {i}: {outcome}");
                        previous[i] = outcome;
                    }
                }

                yield return null;
            }

            Debug.Log(report.ToString());
            AudioTraceRuntime.Shutdown();

            for (var f = 0; f < 12; f++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// FMOD on its own, with no tracer in the way: does this bank's max-instances
        /// setting actually do anything?
        /// </summary>
        [UnityTest]
        public IEnumerator Watch_RawMaxInstances()
        {
            for (var i = 0; i < 240 && !RuntimeManager.HaveAllBanksLoaded; i++)
            {
                yield return null;
            }

            var report = new StringBuilder();

            foreach (var name in new[] { "MaxOneReject", "MaxOneSteal", "MaxOneVirtualize" })
            {
                var key = Folder + name;
                RuntimeManager.StudioSystem.getEvent(key, out var description);

                report.AppendLine($"### raw {name} valid={description.isValid()}");

                description.createInstance(out var first);
                first.start();

                for (var f = 0; f < 20; f++)
                {
                    yield return null;
                }

                description.createInstance(out var second);
                second.start();

                for (var f = 0; f < 20; f++)
                {
                    yield return null;
                }

                description.getInstanceCount(out var count);
                first.getPlaybackState(out var firstState);
                second.getPlaybackState(out var secondState);
                first.getVolume(out _, out var firstFinal);
                second.getVolume(out _, out var secondFinal);

                report.AppendLine($"   instanceCount={count}");
                report.AppendLine($"   first  state={firstState} finalVolume={firstFinal:0.000}");
                report.AppendLine($"   second state={secondState} finalVolume={secondFinal:0.000}");

                first.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                second.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                first.release();
                second.release();

                for (var f = 0; f < 10; f++)
                {
                    yield return null;
                }
            }

            Debug.Log(report.ToString());
        }

        [UnityTest]
        public IEnumerator Watch_Basic2D() => Watch("Basic2D single post", Folder + "Basic2D", 1, 90);

        [UnityTest]
        public IEnumerator Watch_MaxOneReject() => Watch("MaxOneReject x2", Folder + "MaxOneReject", 2, 90);

        [UnityTest]
        public IEnumerator Watch_MaxOneSteal() => Watch("MaxOneSteal x2", Folder + "MaxOneSteal", 2, 90);

        [UnityTest]
        public IEnumerator Watch_MaxOneVirtualize() => Watch("MaxOneVirtualize x2", Folder + "MaxOneVirtualize", 2, 90);
    }
}
