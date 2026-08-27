using UnityEngine;

namespace AudioToolbox.EventTracer
{
    /// <summary>Everything a backend needs to start one sound.</summary>
    /// <remarks>
    /// Passed by <c>in</c> throughout. It is small enough that copying would be
    /// harmless, but the collection path has a zero-allocation budget and passing
    /// structs by reference keeps the habit visible at every call site.
    /// </remarks>
    public readonly struct PlayRequest
    {
        /// <summary>The middleware's own event identifier, e.g. "event:/SFX/Gunshot".</summary>
        public readonly string EventKey;

        /// <summary>
        /// The object the sound belongs to, when there is one. Backends use it to keep
        /// 3D attributes following the object; the tracer uses it for the scene path.
        /// Null for a sound with no emitter.
        /// </summary>
        public readonly Transform Emitter;

        /// <summary>Where to place the sound. Meaningless unless <see cref="Is3D"/>.</summary>
        public readonly Vector3 Position;

        /// <summary>
        /// False for a sound posted without a position. A 3D event posted this way is
        /// exactly the defect AudioDoctor's R006 reports statically, and it will play
        /// from the listener's head here.
        /// </summary>
        public readonly bool Is3D;

        public PlayRequest(string eventKey, Transform emitter, Vector3 position, bool is3D)
        {
            EventKey = eventKey;
            Emitter = emitter;
            Position = position;
            Is3D = is3D;
        }
    }
}
