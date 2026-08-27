namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// One thing a backend observed about a voice, in vocabulary no middleware owns.
    /// </summary>
    /// <remarks>
    /// This enum is the seam that makes outcome mapping testable. FMOD speaks in
    /// <c>RESULT</c> and <c>EVENT_CALLBACK_TYPE</c>, Wwise in <c>AKRESULT</c> and
    /// <c>AkCallbackType</c>; if the mapping from those to a
    /// <see cref="PlaybackOutcome"/> lived inside a backend, testing it would require
    /// that middleware installed. Backends translate to these signals and nothing
    /// more — the judgement happens in <see cref="Recording.OutcomeStateMachine"/>,
    /// which depends on no middleware at all.
    /// </remarks>
    public enum ProbeSignal
    {
        /// <summary>No instance could be obtained. The raw error code travels alongside.</summary>
        CreateFailed = 0,

        /// <summary>An instance exists. It has not been granted a voice yet.</summary>
        CreateOk = 1,

        /// <summary>The instance began playing.</summary>
        Started = 2,

        /// <summary>The instance went virtual: audible in principle, producing nothing.</summary>
        WentVirtual = 3,

        /// <summary>The instance became real again.</summary>
        BackToReal = 4,

        /// <summary>The instance stopped, for whatever reason.</summary>
        Stopped = 5,

        /// <summary>The instance was released. Nothing further will be heard about this voice.</summary>
        Destroyed = 6,

        /// <summary>
        /// The game asked for a stop. Raised by the facade rather than the middleware —
        /// it is the one fact no callback carries, and without it a stop cannot be told
        /// apart from a steal.
        /// </summary>
        StopRequested = 7,
    }
}
