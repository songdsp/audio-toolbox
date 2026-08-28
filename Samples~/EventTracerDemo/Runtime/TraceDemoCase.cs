using AudioToolbox.EventTracer;

namespace AudioToolbox.EventTracer.Demo
{
    /// <summary>
    /// One way a sound goes missing, set up so it happens on demand.
    /// </summary>
    /// <remarks>
    /// Every case here is a mistake somebody has actually shipped. They are worth being
    /// able to reproduce on a button because in a real project they are the opposite:
    /// intermittent, silent, and reported as "the gun sometimes doesn't make a noise".
    /// </remarks>
    public enum TraceDemoCase
    {
        /// <summary>The control. Without one, every other row is unfalsifiable.</summary>
        PlaysFine,

        /// <summary>An event key with a typo in it. Nothing is created at all.</summary>
        MisspeltEvent,

        /// <summary>An instance limit with stealing set to None. The second post is refused.</summary>
        RefusedByInstanceLimit,

        /// <summary>An instance limit with stealing set to Oldest. The first sound is cut off.</summary>
        StolenByANewerSound,

        /// <summary>An instance limit with stealing set to Virtualize. The sound plays, silently.</summary>
        VirtualisedByInstanceLimit,

        /// <summary>A 3D sound posted well past its max distance.</summary>
        OutOfEarshot,

        /// <summary>The game's own code stopping a sound part-way through.</summary>
        StoppedByTheGame,
    }

    /// <summary>What each case is, in the words someone would use to report it.</summary>
    public static class TraceDemoCases
    {
        /// <summary>The order the stage lays them out and runs them in: working case first.</summary>
        public static readonly TraceDemoCase[] InPresentationOrder =
        {
            TraceDemoCase.PlaysFine,
            TraceDemoCase.MisspeltEvent,
            TraceDemoCase.RefusedByInstanceLimit,
            TraceDemoCase.StolenByANewerSound,
            TraceDemoCase.VirtualisedByInstanceLimit,
            TraceDemoCase.OutOfEarshot,
            TraceDemoCase.StoppedByTheGame,
        };

        public static string Title(TraceDemoCase demoCase)
        {
            switch (demoCase)
            {
                case TraceDemoCase.PlaysFine: return "Plays fine";
                case TraceDemoCase.MisspeltEvent: return "Event name has a typo";
                case TraceDemoCase.RefusedByInstanceLimit: return "Instance limit, stealing None";
                case TraceDemoCase.StolenByANewerSound: return "Instance limit, stealing Oldest";
                case TraceDemoCase.VirtualisedByInstanceLimit: return "Instance limit, stealing Virtualize";
                case TraceDemoCase.OutOfEarshot: return "Posted out of earshot";
                default: return "The game stopped it";
            }
        }

        /// <summary>A few characters to float over the object in the scene.</summary>
        public static string ShortTag(TraceDemoCase demoCase)
        {
            switch (demoCase)
            {
                case TraceDemoCase.PlaysFine: return "control";
                case TraceDemoCase.MisspeltEvent: return "typo";
                case TraceDemoCase.RefusedByInstanceLimit: return "max 1 · None";
                case TraceDemoCase.StolenByANewerSound: return "max 1 · Oldest";
                case TraceDemoCase.VirtualisedByInstanceLimit: return "max 1 · Virtual";
                case TraceDemoCase.OutOfEarshot: return "40 m away";
                default: return "stopped by code";
            }
        }

        /// <summary>How this would reach you as a bug report.</summary>
        public static string Symptom(TraceDemoCase demoCase)
        {
            switch (demoCase)
            {
                case TraceDemoCase.PlaysFine:
                    return "You hear it. This row is the control.";

                case TraceDemoCase.MisspeltEvent:
                    return "\"The gun is silent\" — after a rename nobody propagated.";

                case TraceDemoCase.RefusedByInstanceLimit:
                    return "\"Only the first footstep plays.\"";

                case TraceDemoCase.StolenByANewerSound:
                    return "\"The tail gets chopped when it retriggers.\"";

                case TraceDemoCase.VirtualisedByInstanceLimit:
                    return "\"It's playing — the profiler says so — but I can't hear it.\"";

                case TraceDemoCase.OutOfEarshot:
                    return "\"Works in the test scene, silent in the level.\" " +
                           "The outcome says Started — the distance on the record is the answer.";

                default:
                    return "\"It cuts out half way.\" Deliberate, and the log should say so.";
            }
        }

        /// <summary>
        /// What the tracer should say. The demo shows this next to what it actually said.
        /// </summary>
        /// <remarks>
        /// <see cref="TraceDemoCase.OutOfEarshot"/> expects <c>Started</c>, and that is not
        /// a mistake. Distance alone does not virtualise a voice — FMOD only goes virtual
        /// when it needs the channel back — so a sound 49 m past its max distance really is
        /// playing, at nothing. It is the most instructive row in the demo: the outcome is
        /// correct and it is not the answer, and only the distance kept on the record
        /// explains the silence.
        /// </remarks>
        public static PlaybackOutcome Expected(TraceDemoCase demoCase)
        {
            switch (demoCase)
            {
                case TraceDemoCase.PlaysFine: return PlaybackOutcome.Started;
                case TraceDemoCase.MisspeltEvent: return PlaybackOutcome.HandleInvalid;
                case TraceDemoCase.RefusedByInstanceLimit: return PlaybackOutcome.Rejected;
                case TraceDemoCase.StolenByANewerSound: return PlaybackOutcome.Stolen;
                case TraceDemoCase.VirtualisedByInstanceLimit: return PlaybackOutcome.Virtualized;
                case TraceDemoCase.OutOfEarshot: return PlaybackOutcome.Started;
                default: return PlaybackOutcome.StoppedEarly;
            }
        }

        /// <summary>
        /// The fixture event each case needs, from <c>Tools/TraceFixture~</c>.
        /// </summary>
        /// <remarks>
        /// The misspelt one is a transposition — <c>Basci2D</c> for <c>Basic2D</c> — and it
        /// has to be, because <b>FMOD resolves event paths case-insensitively</b>.
        /// <c>Basic2d</c> finds the event and plays it, so a "typo" that differs only in
        /// case makes no demo at all. Worth knowing outside the demo too: a rename that
        /// only changes case will not break FMOD, and will break anything that compares
        /// those strings itself.
        /// </remarks>
        public static string EventKey(TraceDemoCase demoCase)
        {
            switch (demoCase)
            {
                case TraceDemoCase.MisspeltEvent: return "event:/AudioToolboxTrace/Basci2D";
                case TraceDemoCase.RefusedByInstanceLimit: return "event:/AudioToolboxTrace/MaxOneReject";
                case TraceDemoCase.StolenByANewerSound: return "event:/AudioToolboxTrace/MaxOneSteal";
                case TraceDemoCase.VirtualisedByInstanceLimit: return "event:/AudioToolboxTrace/MaxOneVirtualize";
                case TraceDemoCase.OutOfEarshot: return "event:/AudioToolboxTrace/Spatial3D";
                default: return "event:/AudioToolboxTrace/Basic2D";
            }
        }

        /// <summary>
        /// True when the case needs a second sound to interfere with the first.
        /// </summary>
        /// <remarks>
        /// Three of the outcomes only exist in the presence of competition — a voice can
        /// only be refused, stolen or virtualised by something else wanting the same slot.
        /// The demo posts that something else itself rather than asking you to click twice
        /// fast enough.
        /// </remarks>
        public static bool NeedsRival(TraceDemoCase demoCase) =>
            demoCase == TraceDemoCase.RefusedByInstanceLimit ||
            demoCase == TraceDemoCase.StolenByANewerSound ||
            demoCase == TraceDemoCase.VirtualisedByInstanceLimit;

        /// <summary>
        /// Whether the case is about the <em>first</em> sound or the <em>second</em>.
        /// </summary>
        /// <remarks>
        /// Refusal happens to the newcomer: the first sound holds the only voice and the
        /// second is turned away. Stealing and virtualisation happen to the incumbent: the
        /// newcomer takes the voice, or FMOD quietens whichever is least worth hearing —
        /// with two identical sounds, the one already playing.
        /// </remarks>
        public static bool WatchesTheSecondPost(TraceDemoCase demoCase) =>
            demoCase == TraceDemoCase.RefusedByInstanceLimit;
    }
}
