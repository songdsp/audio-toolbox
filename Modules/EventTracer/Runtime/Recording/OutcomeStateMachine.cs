#if AUDIOTOOLBOX_TRACE

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>What is known about one voice so far.</summary>
    public struct VoiceOutcomeState
    {
        public PlaybackOutcome Outcome;
        public bool HasStarted;
        public bool StopRequested;
        public bool WentVirtual;
        public bool IsFinished;
    }

    /// <summary>
    /// Turns a sequence of backend signals into one of the seven outcomes.
    /// </summary>
    /// <remarks>
    /// The most important dozen lines in the module, and deliberately the dullest: no
    /// middleware types, no Unity types, no state outside the struct handed in. That is
    /// what lets the whole mapping be driven from a table in an EditMode test on a
    /// machine with neither FMOD nor Wwise installed — and the mapping is exactly the
    /// part most likely to be wrong, because callback ordering is where middleware
    /// documentation and middleware behaviour part company.
    /// <para>
    /// Two facts the callbacks do not carry have to be supplied by the caller: how long
    /// the event runs, and whether the game asked for the stop. Without the first, a
    /// sound that finished cannot be told from one that was cut off; without the second,
    /// a stop cannot be told from a steal. Both distinctions are the difference between
    /// "your code stopped it" and "the engine stopped it", which is the difference
    /// between two entirely different bugs.
    /// </para>
    /// </remarks>
    public static class OutcomeStateMachine
    {
        /// <summary>
        /// The state a voice is in the moment it is posted, before any signal arrives.
        /// <see cref="PlaybackOutcome.HandleInvalid"/> rather than
        /// <see cref="PlaybackOutcome.NotCalled"/>: the call demonstrably happened, and
        /// until something says otherwise no handle was obtained.
        /// </summary>
        public static VoiceOutcomeState Begin() => new VoiceOutcomeState
        {
            Outcome = PlaybackOutcome.HandleInvalid,
        };

        public static void Apply(
            ref VoiceOutcomeState state,
            ProbeSignal signal,
            double elapsedSeconds,
            double eventLengthSeconds,
            double naturalEndToleranceSeconds)
        {
            switch (signal)
            {
                case ProbeSignal.CreateFailed:
                    state.Outcome = PlaybackOutcome.HandleInvalid;
                    state.IsFinished = true;
                    break;

                case ProbeSignal.CreateOk:
                    // An instance exists but has no voice yet. If nothing else ever
                    // happens to it, that refusal is the whole story — so Rejected is
                    // the resting state of a created sound, not a state it has to be
                    // moved into by some later signal that may never come.
                    if (!state.HasStarted)
                    {
                        state.Outcome = PlaybackOutcome.Rejected;
                    }

                    break;

                case ProbeSignal.Started:
                    state.HasStarted = true;
                    state.Outcome = state.WentVirtual
                        ? PlaybackOutcome.Virtualized
                        : PlaybackOutcome.Started;
                    break;

                case ProbeSignal.WentVirtual:
                    state.WentVirtual = true;

                    if (state.HasStarted && !state.IsFinished)
                    {
                        state.Outcome = PlaybackOutcome.Virtualized;
                    }

                    break;

                case ProbeSignal.BackToReal:
                    // Left as Virtualized on purpose. A sound that spent part of its
                    // life inaudible is what someone reporting "it cut out for a second"
                    // is describing, and a record that reverted to Started would hide
                    // the only evidence of it.
                    break;

                case ProbeSignal.StopRequested:
                    state.StopRequested = true;
                    break;

                case ProbeSignal.Stopped:
                    ApplyStop(ref state, elapsedSeconds, eventLengthSeconds, naturalEndToleranceSeconds);
                    break;

                case ProbeSignal.Destroyed:
                    if (!state.IsFinished)
                    {
                        ApplyStop(ref state, elapsedSeconds, eventLengthSeconds, naturalEndToleranceSeconds);
                    }

                    state.IsFinished = true;
                    break;
            }
        }

        private static void ApplyStop(
            ref VoiceOutcomeState state,
            double elapsedSeconds,
            double eventLengthSeconds,
            double naturalEndToleranceSeconds)
        {
            if (state.HasStarted && EndedEarly(elapsedSeconds, eventLengthSeconds, naturalEndToleranceSeconds))
            {
                state.Outcome = state.StopRequested
                    ? PlaybackOutcome.StoppedEarly
                    : PlaybackOutcome.Stolen;
            }

            // A voice that never started keeps Rejected; one that ran its course keeps
            // Started or Virtualized. Neither needs saying again here.
            state.IsFinished = true;
        }

        /// <summary>
        /// A length of zero means the backend could not say — a looping event, or a key
        /// it never resolved. Then every stop counts as early, because a sound with no
        /// end of its own cannot have reached it.
        /// </summary>
        private static bool EndedEarly(double elapsedSeconds, double eventLengthSeconds, double toleranceSeconds)
        {
            if (eventLengthSeconds <= 0)
            {
                return true;
            }

            return elapsedSeconds < eventLengthSeconds - toleranceSeconds;
        }
    }
}

#endif
