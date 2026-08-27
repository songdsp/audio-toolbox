using FMOD.Studio;

namespace AudioToolbox.EventTracer.Backends.Fmod
{
    /// <summary>
    /// Translates FMOD's callback vocabulary into <see cref="ProbeSignal"/>, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately the whole of what this backend knows about outcomes. The judgement —
    /// that a stop before the event's length with nobody asking means stolen — lives in
    /// <c>OutcomeStateMachine</c>, where it can be driven from a table on a machine with
    /// no FMOD installed. Splitting it this way is what makes the riskiest part of the
    /// module testable, because callback ordering is where documentation and behaviour
    /// most often disagree.
    /// </remarks>
    internal static class FmodSignalMap
    {
        /// <summary>
        /// The callbacks worth subscribing to. Every one of them changes an outcome;
        /// subscribing to more would mean waking the mixer thread for nothing.
        /// </summary>
        public const EVENT_CALLBACK_TYPE Mask =
            EVENT_CALLBACK_TYPE.CREATED |
            EVENT_CALLBACK_TYPE.DESTROYED |
            EVENT_CALLBACK_TYPE.STARTED |
            EVENT_CALLBACK_TYPE.RESTARTED |
            EVENT_CALLBACK_TYPE.STOPPED |
            EVENT_CALLBACK_TYPE.REAL_TO_VIRTUAL |
            EVENT_CALLBACK_TYPE.VIRTUAL_TO_REAL;

        public static bool TryMap(EVENT_CALLBACK_TYPE type, out ProbeSignal signal)
        {
            switch (type)
            {
                case EVENT_CALLBACK_TYPE.CREATED:
                    signal = ProbeSignal.CreateOk;
                    return true;

                case EVENT_CALLBACK_TYPE.STARTED:
                case EVENT_CALLBACK_TYPE.RESTARTED:
                    signal = ProbeSignal.Started;
                    return true;

                case EVENT_CALLBACK_TYPE.REAL_TO_VIRTUAL:
                    signal = ProbeSignal.WentVirtual;
                    return true;

                case EVENT_CALLBACK_TYPE.VIRTUAL_TO_REAL:
                    signal = ProbeSignal.BackToReal;
                    return true;

                case EVENT_CALLBACK_TYPE.STOPPED:
                    signal = ProbeSignal.Stopped;
                    return true;

                case EVENT_CALLBACK_TYPE.DESTROYED:
                    signal = ProbeSignal.Destroyed;
                    return true;

                default:
                    signal = default;
                    return false;
            }
        }
    }
}
