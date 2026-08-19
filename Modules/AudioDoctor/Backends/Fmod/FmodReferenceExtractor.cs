using System;
using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using FMODUnity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioToolbox.AudioDoctor.Backends.Fmod
{
    /// <summary>
    /// Finds every place a Unity project reaches for an FMOD event, bank or parameter.
    /// </summary>
    /// <remarks>
    /// Four collection paths, because a reference can be authored four ways and a
    /// validator that only knew one of them would report the other three as orphans:
    /// serialized fields on components, string literals in code, Timeline clips, and
    /// AnimationEvents.
    /// </remarks>
    internal sealed class FmodReferenceExtractor : IReferenceExtractor
    {
        private readonly FmodCodeScanner _codeScanner = new FmodCodeScanner();

        public void OnPrefab(GameObject root, string assetPath, ReferenceSink sink) =>
            CollectFromHierarchy(root, assetPath, sink);

        public void OnSceneRoot(GameObject root, string scenePath, ReferenceSink sink) =>
            CollectFromHierarchy(root, scenePath, sink);

        public void OnScript(string assetPath, string[] lines, ReferenceSink sink) =>
            _codeScanner.Scan(assetPath, lines, sink);

        public void OnTimelineObject(Object timelineObject, string assetPath, ReferenceSink sink)
        {
            // FMODEventPlayable is the clip asset inside a .playable, not the timeline
            // itself, which is why the walker hands over every sub-asset.
            if (timelineObject is FMODEventPlayable playable)
            {
                var key = FmodEventKeys.Resolve(playable.EventReference);

                if (!string.IsNullOrEmpty(key))
                {
                    sink.Add(new EventRefUsage
                    {
                        EventKey = key,
                        AssetPath = assetPath,
                        ObjectPath = playable.name,
                        Source = RefSource.Timeline,
                        // A timeline clip plays through the director's own transform.
                        IsSpatializedCallSite = true,
                    });
                }
            }
        }

        public void OnAnimationClip(AnimationClip clip, string assetPath, ReferenceSink sink)
        {
            foreach (var animationEvent in AnimationUtility.GetAnimationEvents(clip))
            {
                var value = animationEvent.stringParameter;

                if (string.IsNullOrEmpty(value) || !LooksLikeEventPath(value))
                {
                    continue;
                }

                sink.Add(new EventRefUsage
                {
                    EventKey = value,
                    AssetPath = assetPath,
                    ObjectPath = animationEvent.functionName,
                    Source = RefSource.AnimationEvent,
                    IsSpatializedCallSite = null,
                });
            }
        }

        private static void CollectFromHierarchy(GameObject root, string assetPath, ReferenceSink sink)
        {
            SerializedWalker.VisitComponents(root, component =>
            {
                var objectPath = HierarchyPath.Of(component);

                switch (component)
                {
                    case StudioEventEmitter emitter:
                        CollectEmitter(emitter, assetPath, objectPath, sink);
                        return;

                    case StudioBankLoader loader:
                        CollectBankLoader(loader, assetPath, objectPath, sink);
                        return;

                    case StudioParameterTrigger trigger:
                        CollectParameterTrigger(trigger, assetPath, objectPath, sink);
                        return;
                }

                // Anything else might still carry an EventReference field: a designer's
                // own MonoBehaviour is the common case, and it is exactly the one a
                // hard-coded component list would miss.
                CollectGenericReferences(component, assetPath, objectPath, sink);
            });
        }

        private static void CollectEmitter(
            StudioEventEmitter emitter, string assetPath, string objectPath, ReferenceSink sink)
        {
            var key = FmodEventKeys.Resolve(emitter.EventReference);

            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            sink.Add(new EventRefUsage
            {
                EventKey = key,
                AssetPath = assetPath,
                ObjectPath = objectPath,
                Source = RefSource.SerializedField,
                // An emitter lives on a transform, so the event is always positioned.
                IsSpatializedCallSite = true,
            });

            foreach (var param in emitter.Params ?? Array.Empty<ParamRef>())
            {
                if (param == null || string.IsNullOrEmpty(param.Name))
                {
                    continue;
                }

                sink.Add(new ParameterUsage
                {
                    EventKey = key,
                    ParameterName = param.Name,
                    AssetPath = assetPath,
                    IsGlobal = false,
                    ResolutionNote = $"Initial parameter on the emitter at {objectPath}",
                });
            }
        }

        private static void CollectBankLoader(
            StudioBankLoader loader, string assetPath, string objectPath, ReferenceSink sink)
        {
            foreach (var bank in loader.Banks ?? new List<string>())
            {
                if (!string.IsNullOrEmpty(bank))
                {
                    sink.Add(new BankLoadUsage
                    {
                        BankName = bank,
                        AssetPath = assetPath,
                        ObjectPath = objectPath,
                        Source = BankLoadSource.LoaderComponent,
                    });
                }
            }
        }

        private static void CollectParameterTrigger(
            StudioParameterTrigger trigger, string assetPath, string objectPath, ReferenceSink sink)
        {
            foreach (var emitterRef in trigger.Emitters ?? Array.Empty<EmitterRef>())
            {
                if (emitterRef?.Params == null)
                {
                    continue;
                }

                // The trigger points at an emitter, and the emitter knows the event.
                // Without that hop the parameter names would have no event to check against.
                var key = emitterRef.Target != null
                    ? FmodEventKeys.Resolve(emitterRef.Target.EventReference)
                    : null;

                foreach (var param in emitterRef.Params)
                {
                    if (param == null || string.IsNullOrEmpty(param.Name))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(key))
                    {
                        sink.Note(
                            $"A StudioParameterTrigger at {objectPath} sets '{param.Name}' but its " +
                            "target emitter is unassigned, so the parameter could not be checked " +
                            "against an event.",
                            assetPath);
                        continue;
                    }

                    sink.Add(new ParameterUsage
                    {
                        EventKey = key,
                        ParameterName = param.Name,
                        AssetPath = assetPath,
                        IsGlobal = false,
                        ResolutionNote = $"StudioParameterTrigger at {objectPath} targeting {key}",
                    });
                }
            }
        }

        private static void CollectGenericReferences(
            Component component, string assetPath, string objectPath, ReferenceSink sink)
        {
            SerializedWalker.Visit(component, property =>
            {
                if (property.propertyType == SerializedPropertyType.Generic &&
                    property.type == nameof(EventReference))
                {
                    var key = FmodEventKeys.Resolve(property);

                    if (!string.IsNullOrEmpty(key))
                    {
                        sink.Add(new EventRefUsage
                        {
                            EventKey = key,
                            AssetPath = assetPath,
                            ObjectPath = $"{objectPath} ({property.propertyPath})",
                            Source = RefSource.SerializedField,
                            IsSpatializedCallSite = null,
                        });
                    }

                    // The struct has been recognised; its Guid/Path children are noise.
                    return true;
                }

                // The pre-2.x integration stored events as plain strings, and plenty of
                // projects still have those fields. Anything shaped like an event path
                // is worth following up on.
                if (property.propertyType == SerializedPropertyType.String &&
                    LooksLikeEventPath(property.stringValue))
                {
                    sink.Add(new EventRefUsage
                    {
                        EventKey = property.stringValue,
                        AssetPath = assetPath,
                        ObjectPath = $"{objectPath} ({property.propertyPath})",
                        Source = RefSource.SerializedField,
                        IsSpatializedCallSite = null,
                    });

                    return true;
                }

                return false;
            });
        }

        private static bool LooksLikeEventPath(string value) =>
            !string.IsNullOrEmpty(value) && value.StartsWith("event:/", StringComparison.Ordinal);
    }
}
