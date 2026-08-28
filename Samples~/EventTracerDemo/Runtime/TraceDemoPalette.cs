using UnityEngine;

namespace AudioToolbox.EventTracer.Demo
{
    /// <summary>
    /// Outcome colours for the demo, matching the timeline window's.
    /// </summary>
    /// <remarks>
    /// Deliberately the same hues the editor uses, so that someone watching an object in
    /// the scene turn amber and then finding an amber mark in the timeline is looking at
    /// one idea rather than two. The window reads its copy out of USS; a runtime sample
    /// has no stylesheet, so this is the second definition and it is worth saying so.
    /// </remarks>
    public static class TraceDemoPalette
    {
        /// <summary>Not fired yet.</summary>
        public static readonly Color Idle = new Color(0.62f, 0.64f, 0.67f);

        /// <summary>Fired, waiting on the middleware's callbacks.</summary>
        public static readonly Color Running = new Color(0.80f, 0.82f, 0.86f);

        public static Color For(PlaybackOutcome outcome, bool settled)
        {
            var color = Base(outcome);

            // Dimmed until the outcome is final, so a colour you are reading off the
            // screen is never one that is still about to change.
            return settled ? color : Color.Lerp(Running, color, 0.55f);
        }

        private static Color Base(PlaybackOutcome outcome)
        {
            switch (outcome)
            {
                case PlaybackOutcome.HandleInvalid: return new Color(0.78f, 0.16f, 0.16f);
                case PlaybackOutcome.Rejected: return new Color(0.88f, 0.35f, 0.17f);
                case PlaybackOutcome.Virtualized: return new Color(0.79f, 0.59f, 0.10f);
                case PlaybackOutcome.Stolen: return new Color(0.83f, 0.51f, 0.16f);
                case PlaybackOutcome.StoppedEarly: return new Color(0.36f, 0.51f, 0.68f);
                case PlaybackOutcome.Started: return new Color(0.29f, 0.56f, 0.39f);
                default: return Idle;
            }
        }
    }
}
