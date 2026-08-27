using UnityEngine;

namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// One middleware's runtime, reduced to what the facade needs: start a sound, stop
    /// it, steer it, and say what became of it.
    /// </summary>
    /// <remarks>
    /// Implementations live in their own assemblies behind define constraints and
    /// register themselves with <see cref="AudioTrace.RegisterProbe"/>. This assembly
    /// must never reference a backend — that is what lets the module's tests run on a
    /// machine with no middleware installed at all.
    /// <para>
    /// Note what is <em>not</em> here: no notion of an outcome. A probe reports
    /// <see cref="ProbeSignal"/>s and raw result codes; deciding that a particular
    /// sequence of those means "stolen" rather than "stopped early" is
    /// <see cref="Recording.OutcomeStateMachine"/>'s job, and keeping it out of the
    /// backends is what makes that decision testable without FMOD.
    /// </para>
    /// </remarks>
    public interface IAudioRuntimeProbe
    {
        /// <summary>Stable id used in logs and reports: "native", "fmod", "wwise".</summary>
        string BackendId { get; }

        /// <summary>Name shown to people.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Higher wins when several probes are registered. The native fallback sits at
        /// 0 so any real middleware outranks it.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// False when the integration compiled but cannot run — no banks, no system
        /// initialised. Say why via <see cref="GetUnavailableReason"/> rather than throwing.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Human-readable explanation when <see cref="IsAvailable"/> is false.</summary>
        string GetUnavailableReason();

        /// <summary>The middleware's version, for the session header. Empty when unknown.</summary>
        string GetBackendVersion();

        /// <summary>
        /// Called once before any playback. <paramref name="signals"/> is where the
        /// probe reports what it observes; it is safe to write to from any thread,
        /// which matters because middleware callbacks do not arrive on the main one.
        /// </summary>
        void Initialize(ProbeSignalQueue signals, int maxVoices);

        void Shutdown();

        /// <summary>
        /// Starts a sound on the given voice slot. Returns false when no instance could
        /// be obtained at all, in which case <paramref name="backendResultCode"/> carries
        /// the middleware's own error and no further signals will arrive for this voice.
        /// </summary>
        bool Play(in PlayRequest request, int voiceId, out int backendResultCode);

        /// <summary>Stops a sound the caller started. A no-op for a voice that already ended.</summary>
        void Stop(int voiceId);

        void SetParameter(int voiceId, string name, float value);

        void SetGlobalParameter(string name, float value);

        /// <summary>
        /// Called once per frame on the main thread, for backends that need to push
        /// updated 3D attributes or pump their own update loop.
        /// </summary>
        void Tick();

        /// <summary>
        /// Where the middleware currently thinks the listener is. False when there is
        /// no listener yet, in which case distances are not recorded rather than being
        /// recorded as zero.
        /// </summary>
        bool TryGetListenerPosition(out Vector3 position);

        /// <summary>
        /// Writes the backend's global parameters into the caller's arrays and returns how
        /// many were written. Never allocates: the arrays belong to the caller, and the
        /// names should come from a cache the probe built once.
        /// </summary>
        /// <remarks>
        /// The point of polling rather than relying on
        /// <see cref="SetGlobalParameter"/> alone is code that does not go through the
        /// facade — a designer's snapshot, a gameplay system talking to the middleware
        /// directly. Those are exactly the values that explain a sound nobody can account
        /// for, so a tracer that only knew about its own calls would miss them.
        /// <para>
        /// Return 0 when the backend has no such concept. That is a fact about the
        /// backend, not a failure, and the session simply carries no parameters.
        /// </para>
        /// </remarks>
        int ReadGlobalParameters(string[] names, float[] values);

        /// <summary>
        /// How long the event runs, in seconds, or 0 when the middleware cannot say
        /// (a looping event, an unknown key). Used to tell an early stop from a sound
        /// that simply finished, so 0 means "treat any stop as early".
        /// </summary>
        double GetEventLengthSeconds(string eventKey);
    }
}
