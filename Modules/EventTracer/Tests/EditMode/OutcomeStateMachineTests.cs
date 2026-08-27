#if AUDIOTOOLBOX_TRACE

using AudioToolbox.EventTracer.Recording;
using NUnit.Framework;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// The mapping table, driven directly.
    /// </summary>
    /// <remarks>
    /// This is the module's most valuable test and its cheapest. Every one of the seven
    /// outcomes is a sequence of signals, and getting that sequence wrong produces a
    /// tracer that confidently reports the wrong cause — worse than no tracer, because
    /// someone will act on it. Written before the FMOD backend, so that the backend had
    /// something to be right or wrong against rather than defining correctness by what it
    /// happened to do.
    /// </remarks>
    [TestFixture]
    public sealed class OutcomeStateMachineTests
    {
        private const double EventLength = 4.0;
        private const double Tolerance = 0.1;

        private static PlaybackOutcome Run(double eventLength, params (ProbeSignal signal, double at)[] signals)
        {
            var state = OutcomeStateMachine.Begin();

            foreach (var (signal, at) in signals)
            {
                OutcomeStateMachine.Apply(ref state, signal, at, eventLength, Tolerance);
            }

            return state.Outcome;
        }

        [Test]
        public void CreateFailure_IsHandleInvalid()
        {
            Assert.That(
                Run(EventLength, (ProbeSignal.CreateFailed, 0)),
                Is.EqualTo(PlaybackOutcome.HandleInvalid));
        }

        [Test]
        public void PostBeforeAnySignal_IsHandleInvalid()
        {
            // Nothing has come back yet, so no handle is known to exist. Reporting
            // Started optimistically would make every failure look like a success for
            // however long the callback takes to arrive.
            var state = OutcomeStateMachine.Begin();
            Assert.That(state.Outcome, Is.EqualTo(PlaybackOutcome.HandleInvalid));
        }

        [Test]
        public void CreatedButNeverStarted_IsRejected()
        {
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Destroyed, 0.01)),
                Is.EqualTo(PlaybackOutcome.Rejected));
        }

        [Test]
        public void StartedAndRanToItsEnd_IsStarted()
        {
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.Stopped, EventLength),
                    (ProbeSignal.Destroyed, EventLength)),
                Is.EqualTo(PlaybackOutcome.Started));
        }

        [Test]
        public void StoppedWithoutAnybodyAsking_IsStolen()
        {
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.Stopped, 1.0),
                    (ProbeSignal.Destroyed, 1.0)),
                Is.EqualTo(PlaybackOutcome.Stolen));
        }

        [Test]
        public void StoppedAfterTheGameAskedFor_It_IsStoppedEarly()
        {
            // The only difference from the case above is StopRequested. That one bit is
            // the difference between "your code did this" and "the engine did this".
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.StopRequested, 1.0),
                    (ProbeSignal.Stopped, 1.0),
                    (ProbeSignal.Destroyed, 1.0)),
                Is.EqualTo(PlaybackOutcome.StoppedEarly));
        }

        [Test]
        public void WentVirtualAfterStarting_IsVirtualized()
        {
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.WentVirtual, 0.5)),
                Is.EqualTo(PlaybackOutcome.Virtualized));
        }

        [Test]
        public void StartedWhileAlreadyVirtual_IsVirtualized()
        {
            // FMOD can report the virtualisation before the start when an instance goes
            // virtual the moment it is created - over its own max instances, say.
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.WentVirtual, 0.005),
                    (ProbeSignal.Started, 0.01)),
                Is.EqualTo(PlaybackOutcome.Virtualized));
        }

        [Test]
        public void ComingBackFromVirtual_StaysVirtualized()
        {
            // The record's job is to say a sound spent part of its life inaudible.
            // Reverting to Started would erase the only evidence of the dropout somebody
            // is complaining about.
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.WentVirtual, 0.5),
                    (ProbeSignal.BackToReal, 1.5),
                    (ProbeSignal.Stopped, EventLength),
                    (ProbeSignal.Destroyed, EventLength)),
                Is.EqualTo(PlaybackOutcome.Virtualized));
        }

        [Test]
        public void VirtualisedThenCutShort_ReportsTheCut()
        {
            // Both are true; the one that ends the sound is the one that explains the
            // silence, so it wins.
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.WentVirtual, 0.5),
                    (ProbeSignal.Stopped, 1.0),
                    (ProbeSignal.Destroyed, 1.0)),
                Is.EqualTo(PlaybackOutcome.Stolen));
        }

        [Test]
        public void StoppedJustInsideTheTolerance_CountsAsFinishing()
        {
            // A callback drained a frame or two late must not turn every sound that
            // simply ended into a stolen one.
            Assert.That(
                Run(EventLength,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.Stopped, EventLength - (Tolerance / 2)),
                    (ProbeSignal.Destroyed, EventLength)),
                Is.EqualTo(PlaybackOutcome.Started));
        }

        [Test]
        public void UnknownLength_TreatsAnyStopAsEarly()
        {
            // A length of zero is a looping event or a key the backend never resolved.
            // A sound with no end of its own cannot have reached it.
            Assert.That(
                Run(0,
                    (ProbeSignal.CreateOk, 0),
                    (ProbeSignal.Started, 0.01),
                    (ProbeSignal.Stopped, 900),
                    (ProbeSignal.Destroyed, 900)),
                Is.EqualTo(PlaybackOutcome.Stolen));
        }

        [Test]
        public void DestroyedWithoutAStop_StillFinishesTheVoice()
        {
            // Not every backend sends both. The voice slot has to come back either way,
            // or a long session runs out of them.
            var state = OutcomeStateMachine.Begin();

            OutcomeStateMachine.Apply(ref state, ProbeSignal.CreateOk, 0, EventLength, Tolerance);
            OutcomeStateMachine.Apply(ref state, ProbeSignal.Started, 0.01, EventLength, Tolerance);
            OutcomeStateMachine.Apply(ref state, ProbeSignal.Destroyed, 1.0, EventLength, Tolerance);

            Assert.That(state.IsFinished, Is.True);
            Assert.That(state.Outcome, Is.EqualTo(PlaybackOutcome.Stolen));
        }

        [Test]
        public void SignalsAfterTheVoiceFinished_DoNotRewriteIt()
        {
            var state = OutcomeStateMachine.Begin();

            OutcomeStateMachine.Apply(ref state, ProbeSignal.CreateOk, 0, EventLength, Tolerance);
            OutcomeStateMachine.Apply(ref state, ProbeSignal.Started, 0.01, EventLength, Tolerance);
            OutcomeStateMachine.Apply(ref state, ProbeSignal.Stopped, 1.0, EventLength, Tolerance);
            OutcomeStateMachine.Apply(ref state, ProbeSignal.Destroyed, 1.0, EventLength, Tolerance);
            OutcomeStateMachine.Apply(ref state, ProbeSignal.WentVirtual, 1.1, EventLength, Tolerance);

            Assert.That(state.Outcome, Is.EqualTo(PlaybackOutcome.Stolen));
        }
    }
}

#endif
