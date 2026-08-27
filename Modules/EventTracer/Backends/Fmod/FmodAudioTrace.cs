using System.Runtime.CompilerServices;
using FMOD.Studio;
using UnityEngine;

namespace AudioToolbox.EventTracer.Backends.Fmod
{
    /// <summary>
    /// The FMOD-shaped part of the facade: attaching an instance the game already made.
    /// </summary>
    /// <remarks>
    /// Lives here rather than on <see cref="AudioTrace"/> because its signature names an
    /// FMOD type, and the core assembly must stay free of middleware references so its
    /// tests can run without one.
    /// <para>
    /// Use it to bring existing code into the trace without rewriting it — a
    /// <c>StudioEventEmitter</c>, a system with its own instance pooling, anything where
    /// moving to <see cref="AudioTrace.Post"/> is a later commit. What it cannot recover
    /// is anything that happened before the attach: a sound refused a voice is already
    /// gone, so <see cref="PlaybackOutcome.Rejected"/> and
    /// <see cref="PlaybackOutcome.HandleInvalid"/> stay invisible on this path. Posting
    /// through the facade is what makes all seven outcomes reachable.
    /// </para>
    /// </remarks>
    public static class FmodAudioTrace
    {
        /// <summary>
        /// Traces an instance the caller created and, usually, has already started.
        /// </summary>
        /// <param name="instance">The instance to follow. Must be valid.</param>
        /// <param name="eventKey">The event path, for the record. FMOD does not hand it back from an instance.</param>
        /// <param name="emitter">What the sound belongs to, for 3D updates and the record.</param>
        /// <returns>A handle usable with <see cref="AudioTrace.Stop"/>, or invalid if it could not be attached.</returns>
        public static AudioTraceHandle Attach(
            EventInstance instance,
            string eventKey,
            Transform emitter = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0)
        {
            if (!instance.isValid())
            {
                return AudioTraceHandle.Invalid;
            }

            if (!(AudioTraceRuntime.ActiveProbe is FmodRuntimeProbe probe))
            {
                // Either nothing initialised, or something other than FMOD is driving
                // the session. Silently doing nothing is right: a game should be able to
                // leave these calls in place on a machine where FMOD is not installed.
                return AudioTraceHandle.Invalid;
            }

            var hasEmitter = emitter != null;

            if (!AudioTraceRuntime.TryBeginExternalVoice(
                    eventKey,
                    emitter,
                    hasEmitter ? emitter.position : Vector3.zero,
                    hasEmitter,
                    callerFilePath,
                    callerLineNumber,
                    out var handle,
                    out var voiceId))
            {
                return AudioTraceHandle.Invalid;
            }

            probe.Adopt(voiceId, instance, emitter);
            return handle;
        }
    }
}
