namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// What actually happened to one attempt at playing a sound.
    /// </summary>
    /// <remarks>
    /// The whole point of the module: "no sound" is not one symptom but seven, and
    /// they have nothing in common except silence. Each value here is a different
    /// half-hour of debugging, so the tracer refuses to collapse them.
    /// <para>
    /// Ordered by how far the sound got: nothing was called, a handle was never
    /// obtained, an instance was refused, it played. A caller wanting "did this make
    /// noise" should test <c>outcome == Started</c> rather than assume an ordering
    /// beyond that.
    /// </para>
    /// </remarks>
    public enum PlaybackOutcome
    {
        /// <summary>
        /// The code never ran. Reserved for AudioDoctor's static analysis to fill in —
        /// a tracer can only record calls that happened. See the known limits in
        /// Documentation~/EventTracer.md; this is a boundary, not a gap.
        /// </summary>
        NotCalled = 0,

        /// <summary>No usable handle: the bank is not loaded, or the event does not exist.</summary>
        HandleInvalid = 1,

        /// <summary>An instance was created but refused a voice — max instances, stealing set to None.</summary>
        Rejected = 2,

        /// <summary>Playing, or played to its end.</summary>
        Started = 3,

        /// <summary>Started, then went virtual: out of range, too quiet, or over the instance limit.</summary>
        Virtualized = 4,

        /// <summary>Started, then cut off by the engine to make room for something else.</summary>
        Stolen = 5,

        /// <summary>Started, then stopped by game logic before it had finished.</summary>
        StoppedEarly = 6,
    }
}
