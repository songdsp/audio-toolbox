using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// The backend-specific half of a reference scan.
    /// </summary>
    /// <remarks>
    /// <see cref="ProjectWalker"/> owns the expensive, error-prone part — enumerating
    /// assets, opening and restoring scenes, reading source files, reporting progress,
    /// honouring cancellation — and calls into an extractor for the part only the
    /// backend can know: what an event reference actually looks like. FMOD's
    /// EventReference struct and a plain AudioSource.clip share none of that shape,
    /// but they share all of the walking.
    /// </remarks>
    public interface IReferenceExtractor
    {
        /// <summary>Called once per prefab root. Walk children yourself if you need to.</summary>
        void OnPrefab(GameObject root, string assetPath, ReferenceSink sink);

        /// <summary>Called once per root GameObject of an opened scene.</summary>
        void OnSceneRoot(GameObject root, string scenePath, ReferenceSink sink);

        /// <summary>Called once per .cs file. <paramref name="lines"/> is 0-based; report 1-based.</summary>
        void OnScript(string assetPath, string[] lines, ReferenceSink sink);

        /// <summary>Called once per object inside a .playable (the timeline and its clips).</summary>
        void OnTimelineObject(Object timelineObject, string assetPath, ReferenceSink sink);

        /// <summary>Called once per AnimationClip that carries events.</summary>
        void OnAnimationClip(AnimationClip clip, string assetPath, ReferenceSink sink);
    }
}
