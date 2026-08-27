using System.Runtime.InteropServices;
using UnityEngine;

namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// One attempt at playing a sound, in the form it is kept in memory and on disk.
    /// </summary>
    /// <remarks>
    /// A struct, and only blittable fields, because a session holds tens of thousands
    /// of these in one array that must never be walked by the garbage collector. Every
    /// string is an index into the session's intern table — that indirection is what
    /// lets the collection path allocate nothing per call, and it is also why a ten
    /// minute log is measured in megabytes rather than hundreds.
    /// <para>
    /// <see cref="LayoutKind.Sequential"/> and <see cref="Pack"/> = 4 are not
    /// decoration: the on-disk format writes these fields in declaration order, and a
    /// silent repack would make old logs unreadable without changing the format
    /// version. Adding a field means bumping <see cref="TraceFormat.Version"/>.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct AudioTraceRecord
    {
        /// <summary>Unity frame the call was made on.</summary>
        public long Frame;

        /// <summary>Seconds since the trace session began.</summary>
        public double TimeSeconds;

        /// <summary>Intern id of the event key, e.g. "event:/SFX/Gunshot".</summary>
        public int EventKeyId;

        /// <summary>
        /// Intern id of the emitter's scene path — "/Level/Enemies/Rifleman/Muzzle" —
        /// or <see cref="TraceFormat.NoStringId"/> for a sound with no emitter. Resolved
        /// once per object and cached, so an object renamed or reparented later keeps the
        /// path it had when the tracer first saw it.
        /// </summary>
        public int EmitterPathId;

        /// <summary>Intern id of "file:line" for the call site, or <see cref="TraceFormat.NoStringId"/>.</summary>
        public int CallSiteId;

        /// <summary>Where the sound was asked to play from.</summary>
        public Vector3 EmitterPos;

        /// <summary>Where the listener was at that moment.</summary>
        public Vector3 ListenerPos;

        /// <summary>
        /// Cached rather than derived, because the two positions above are sampled once
        /// at post time and a distance recomputed later would answer a different question.
        /// </summary>
        public float DistanceToListener;

        /// <summary>What became of this call. Patched in place as callbacks arrive.</summary>
        public PlaybackOutcome Outcome;

        /// <summary>
        /// The middleware's own error code, unmapped. Kept because the normalised
        /// outcome is deliberately lossy and the raw code is what a support thread
        /// will ask for.
        /// </summary>
        public int BackendResultCode;

        /// <summary>
        /// The set of global parameters in force when this sound was posted, or
        /// <see cref="TraceFormat.NoSnapshotId"/> when none were known.
        /// </summary>
        /// <remarks>
        /// An id, not a copy: sounds posted under identical state all point at the same
        /// snapshot, and each snapshot stores only what changed from the one before it.
        /// A whole burst of footsteps therefore costs one snapshot between them.
        /// </remarks>
        public int ParamSnapshotId;
    }
}
