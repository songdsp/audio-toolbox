using System.Runtime.CompilerServices;
using UnityEngine;

namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// Play sounds through this instead of calling the middleware directly, and every
    /// call leaves a record of what became of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a facade at all.</b> The seven ways a sound can fail to be heard are not
    /// visible from a call site. FMOD's <c>PlayOneShot</c> returns nothing; the instance
    /// that was refused a voice, the one that went virtual out of range and the one that
    /// something else stole are indistinguishable from the one that played perfectly.
    /// Posting through here means the tracer holds the instance from creation to
    /// destruction and can say which of the seven happened.
    /// </para>
    /// <para>
    /// <b>What this costs when tracing is off.</b> Without <c>AUDIOTOOLBOX_TRACE</c> the
    /// collection layer is not compiled: no ring buffer, no intern table, no session, no
    /// writer thread. What remains is the dispatch to the backend that any wrapper would
    /// have, plus a voice slot so that handles stay safe to hold.
    /// </para>
    /// <para>
    /// <b>The blind spot.</b> Code that calls FMOD or Wwise directly is not recorded and
    /// cannot be — the tracer never sees the instance. AudioDoctor can find those call
    /// sites statically, but nothing can tell you what they did at runtime. This is a
    /// boundary of the approach, stated here rather than discovered later.
    /// </para>
    /// <example>
    /// <code>
    /// var shot = AudioTrace.Post("event:/SFX/Gunshot", transform);
    /// AudioTrace.SetParameter(shot, "Suppressed", 1f);
    /// // ...
    /// AudioTrace.Stop(shot);
    /// </code>
    /// </example>
    /// </remarks>
    public static class AudioTrace
    {
        /// <summary>
        /// Plays an event at an object's position, following nothing — the position is
        /// sampled once, at the post.
        /// </summary>
        /// <param name="eventKey">The middleware's own identifier, e.g. "event:/SFX/Gunshot".</param>
        /// <param name="emitter">What the sound belongs to. Null posts it without a position.</param>
        /// <param name="callerFilePath">Supplied by the compiler. Do not pass this.</param>
        /// <param name="callerLineNumber">Supplied by the compiler. Do not pass this.</param>
        /// <returns>
        /// A handle to stop or steer the sound, or <see cref="AudioTraceHandle.Invalid"/>
        /// when no instance could be started. The record says why; the return value only
        /// says that something went wrong.
        /// </returns>
        public static AudioTraceHandle Post(
            string eventKey,
            Transform emitter,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var hasEmitter = emitter != null;

            return AudioTraceRuntime.Post(
                eventKey,
                emitter,
                hasEmitter ? emitter.position : Vector3.zero,
                hasEmitter,
                callerFilePath,
                callerLineNumber);
        }

        /// <summary>Plays an event at a world position.</summary>
        public static AudioTraceHandle Post(
            string eventKey,
            Vector3 position,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0) =>
            AudioTraceRuntime.Post(eventKey, null, position, true, callerFilePath, callerLineNumber);

        /// <summary>
        /// Plays an event with no position at all — UI, music, narration.
        /// </summary>
        /// <remarks>
        /// A 3D event posted this way will play from the listener's head. That is the
        /// defect AudioDoctor's R006 reports, and the trace record will show it too:
        /// <see cref="AudioTraceRecord.DistanceToListener"/> comes back as -1 rather than
        /// as a plausible-looking zero.
        /// </remarks>
        public static AudioTraceHandle Post(
            string eventKey,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0) =>
            AudioTraceRuntime.Post(eventKey, null, Vector3.zero, false, callerFilePath, callerLineNumber);

        /// <summary>
        /// Stops a sound. Harmless on a handle whose sound already ended.
        /// </summary>
        /// <remarks>
        /// Going through here rather than stopping the middleware instance directly is
        /// what lets the tracer tell <see cref="PlaybackOutcome.StoppedEarly"/> from
        /// <see cref="PlaybackOutcome.Stolen"/>. Both look identical in a callback; the
        /// only difference is whether anybody asked, and this call is where that is known.
        /// </remarks>
        public static void Stop(AudioTraceHandle handle) => AudioTraceRuntime.Stop(in handle);

        /// <summary>Sets a parameter on one playing sound.</summary>
        public static void SetParameter(AudioTraceHandle handle, string name, float value) =>
            AudioTraceRuntime.SetParameter(in handle, name, value);

        /// <summary>Sets a parameter at system scope, owned by no single event.</summary>
        public static void SetGlobalParameter(string name, float value) =>
            AudioTraceRuntime.SetGlobalParameter(name, value);

        /// <summary>True while the sound this handle refers to is still going.</summary>
        public static bool IsAlive(AudioTraceHandle handle) => AudioTraceRuntime.IsAlive(in handle);

        /// <summary>Applies settings. Must run before the first post; ignored afterwards.</summary>
        public static void Configure(AudioTraceSettings settings) => AudioTraceRuntime.Configure(in settings);

        /// <summary>
        /// Registers a backend. Backends call this themselves at startup; a game only
        /// needs it to install one of its own.
        /// </summary>
        public static void RegisterProbe(IAudioRuntimeProbe probe) => AudioTraceRuntime.RegisterProbe(probe);

        /// <summary>The backend in use: "fmod", "native". Empty when none is available.</summary>
        public static string BackendId => AudioTraceRuntime.ActiveProbe?.BackendId ?? string.Empty;

        /// <summary>False in a build without <c>AUDIOTOOLBOX_TRACE</c>, where posts still play but nothing is kept.</summary>
        public static bool IsRecording => AudioTraceRuntime.IsRecording;

        /// <summary>Where this session's .adtrace file is being written, or empty.</summary>
        public static string SessionPath => AudioTraceRuntime.SessionPath;
    }
}
