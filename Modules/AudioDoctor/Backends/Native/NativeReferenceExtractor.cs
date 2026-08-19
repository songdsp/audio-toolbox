using System;
using System.Text.RegularExpressions;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioToolbox.AudioDoctor.Backends.Native
{
    /// <summary>
    /// Finds AudioClip references. The fallback backend's half of a scan.
    /// </summary>
    internal sealed class NativeReferenceExtractor : IReferenceExtractor
    {
        /// <summary>Resources.Load("Sfx/Click") and its generic form.</summary>
        private static readonly Regex ResourcesLoad = new Regex(
            @"Resources\s*\.\s*Load(?:Async)?\s*(?:<\s*AudioClip\s*>\s*)?\(\s*""(?<path>[^""]+)""",
            RegexOptions.Compiled);

        /// <summary>An event name that is assembled rather than written out.</summary>
        private static readonly Regex ResourcesLoadDynamic = new Regex(
            @"Resources\s*\.\s*Load(?:Async)?\s*(?:<\s*AudioClip\s*>\s*)?\(\s*(?![""\)])",
            RegexOptions.Compiled);

        public void OnPrefab(GameObject root, string assetPath, ReferenceSink sink) =>
            Collect(root, assetPath, sink);

        public void OnSceneRoot(GameObject root, string scenePath, ReferenceSink sink) =>
            Collect(root, scenePath, sink);

        public void OnScript(string assetPath, string[] lines, ReferenceSink sink)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in ResourcesLoad.Matches(lines[i]))
                {
                    sink.Add(new EventRefUsage
                    {
                        EventKey = match.Groups["path"].Value,
                        AssetPath = assetPath,
                        Source = RefSource.CodeLiteral,
                        Line = i + 1,
                        IsSpatializedCallSite = null,
                    });
                }

                if (ResourcesLoadDynamic.IsMatch(lines[i]) && !ResourcesLoad.IsMatch(lines[i]))
                {
                    sink.Note(
                        "Resources.Load is called with a non-literal path, so this reference " +
                        "could not be resolved. Dynamically built asset names are a known limit " +
                        "of static scanning.",
                        assetPath,
                        i + 1);
                }
            }
        }

        public void OnTimelineObject(Object timelineObject, string assetPath, ReferenceSink sink) =>
            CollectFromSerialized(timelineObject, assetPath, objectPath: null, RefSource.Timeline, sink);

        public void OnAnimationClip(AnimationClip clip, string assetPath, ReferenceSink sink)
        {
            var events = AnimationUtility.GetAnimationEvents(clip);

            foreach (var animationEvent in events)
            {
                if (animationEvent.objectReferenceParameter is AudioClip referenced)
                {
                    sink.Add(new EventRefUsage
                    {
                        EventKey = AssetDatabase.GetAssetPath(referenced),
                        AssetPath = assetPath,
                        ObjectPath = animationEvent.functionName,
                        Source = RefSource.AnimationEvent,
                        IsSpatializedCallSite = null,
                    });
                }
            }
        }

        private static void Collect(GameObject root, string assetPath, ReferenceSink sink)
        {
            SerializedWalker.VisitComponents(root, component =>
            {
                var objectPath = HierarchyPath.Of(component);

                // An AudioSource knows whether it is spatialized; a raw AudioClip field
                // on someone's own script does not, so only the former can feed R006.
                if (component is AudioSource source)
                {
                    if (source.clip != null)
                    {
                        sink.Add(new EventRefUsage
                        {
                            EventKey = AssetDatabase.GetAssetPath(source.clip),
                            AssetPath = assetPath,
                            ObjectPath = objectPath,
                            Source = RefSource.SerializedField,
                            IsSpatializedCallSite = source.spatialBlend > 0f,
                        });
                    }

                    return;
                }

                CollectFromSerialized(component, assetPath, objectPath, RefSource.SerializedField, sink);
            });
        }

        private static void CollectFromSerialized(
            Object target,
            string assetPath,
            string objectPath,
            RefSource source,
            ReferenceSink sink)
        {
            SerializedWalker.Visit(target, property =>
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    return false;
                }

                if (!(property.objectReferenceValue is AudioClip clip))
                {
                    return false;
                }

                sink.Add(new EventRefUsage
                {
                    EventKey = AssetDatabase.GetAssetPath(clip),
                    AssetPath = assetPath,
                    ObjectPath = string.IsNullOrEmpty(objectPath)
                        ? property.propertyPath
                        : objectPath + " (" + property.propertyPath + ")",
                    Source = source,
                    IsSpatializedCallSite = null,
                });

                return true;
            });
        }
    }
}
