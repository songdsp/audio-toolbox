using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Walks every place in a Unity project that can hold an audio reference and
    /// hands each one to an <see cref="IReferenceExtractor"/>.
    /// </summary>
    public static class ProjectWalker
    {
        /// <summary>Folders never worth scanning. Package caches dwarf a real project.</summary>
        private static readonly string[] SearchFolders = { "Assets" };

        public static void Walk(IReferenceExtractor extractor, ScanContext context, ReferenceSink sink)
        {
            if (extractor == null) throw new ArgumentNullException(nameof(extractor));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            WalkPrefabs(extractor, context, sink);
            WalkScripts(extractor, context, sink);
            WalkTimelines(extractor, context, sink);
            WalkAnimationClips(extractor, context, sink);
            WalkScenes(extractor, context, sink);
        }

        public static void WalkPrefabs(IReferenceExtractor extractor, ScanContext context, ReferenceSink sink)
        {
            var paths = PathsOfType("t:Prefab");

            for (var i = 0; i < paths.Count; i++)
            {
                context.ThrowIfCancelled();
                context.Progress.Report("Prefabs", paths[i], (float)i / Math.Max(1, paths.Count));

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (root == null)
                {
                    sink.Note("Prefab could not be loaded and was not scanned.", paths[i]);
                    continue;
                }

                try
                {
                    extractor.OnPrefab(root, paths[i], sink);
                }
                catch (Exception e)
                {
                    sink.Note($"Prefab scan failed: {e.GetType().Name}: {e.Message}", paths[i]);
                }
            }
        }

        public static void WalkScripts(IReferenceExtractor extractor, ScanContext context, ReferenceSink sink)
        {
            var paths = PathsOfType("t:MonoScript").Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();

            for (var i = 0; i < paths.Count; i++)
            {
                context.ThrowIfCancelled();
                context.Progress.Report("Scripts", paths[i], (float)i / Math.Max(1, paths.Count));

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(ToAbsolute(paths[i]));
                }
                catch (Exception e)
                {
                    sink.Note($"Script could not be read: {e.Message}", paths[i]);
                    continue;
                }

                try
                {
                    extractor.OnScript(paths[i], lines, sink);
                }
                catch (Exception e)
                {
                    sink.Note($"Script scan failed: {e.GetType().Name}: {e.Message}", paths[i]);
                }
            }
        }

        public static void WalkTimelines(IReferenceExtractor extractor, ScanContext context, ReferenceSink sink)
        {
            var paths = AssetDatabase.FindAssets(string.Empty, SearchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".playable", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < paths.Count; i++)
            {
                context.ThrowIfCancelled();
                context.Progress.Report("Timelines", paths[i], (float)i / Math.Max(1, paths.Count));

                // A .playable holds the timeline plus every track and clip as sub-assets;
                // the clip that carries an event reference is one of them, not the root.
                var objects = AssetDatabase.LoadAllAssetsAtPath(paths[i]);

                foreach (var obj in objects)
                {
                    if (obj == null)
                    {
                        continue;
                    }

                    try
                    {
                        extractor.OnTimelineObject(obj, paths[i], sink);
                    }
                    catch (Exception e)
                    {
                        sink.Note($"Timeline scan failed: {e.GetType().Name}: {e.Message}", paths[i]);
                    }
                }
            }
        }

        public static void WalkAnimationClips(IReferenceExtractor extractor, ScanContext context, ReferenceSink sink)
        {
            var paths = PathsOfType("t:AnimationClip");

            for (var i = 0; i < paths.Count; i++)
            {
                context.ThrowIfCancelled();
                context.Progress.Report("Animation clips", paths[i], (float)i / Math.Max(1, paths.Count));

                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(paths[i]))
                {
                    if (!(obj is AnimationClip clip))
                    {
                        continue;
                    }

                    try
                    {
                        extractor.OnAnimationClip(clip, paths[i], sink);
                    }
                    catch (Exception e)
                    {
                        sink.Note($"Animation clip scan failed: {e.GetType().Name}: {e.Message}", paths[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Opens every scene in turn and restores the editor's scene setup afterwards.
        /// </summary>
        /// <remarks>
        /// Scenes cannot be inspected without opening them — unlike prefabs, there is
        /// no LoadAssetAtPath for a scene's contents. Refusing to run over unsaved work
        /// is deliberate: losing a designer's in-progress scene to a validation tool
        /// would be a far worse bug than any it could find.
        /// </remarks>
        public static void WalkScenes(IReferenceExtractor extractor, ScanContext context, ReferenceSink sink)
        {
            var paths = PathsOfType("t:SceneAsset");

            if (paths.Count == 0)
            {
                return;
            }

            if (HasUnsavedScenes())
            {
                if (Application.isBatchMode ||
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    sink.Note(
                        "Scenes were skipped: there are unsaved changes in the open scenes. " +
                        "Save them and scan again, or the report covers prefabs and code only.");
                    return;
                }
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    context.ThrowIfCancelled();
                    context.Progress.Report("Scenes", paths[i], (float)i / Math.Max(1, paths.Count));

                    sink.AddScene(paths[i]);

                    Scene scene;
                    try
                    {
                        scene = EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Single);
                    }
                    catch (Exception e)
                    {
                        sink.Note($"Scene could not be opened: {e.Message}", paths[i]);
                        continue;
                    }

                    if (!scene.IsValid())
                    {
                        sink.Note("Scene opened but was not valid; skipped.", paths[i]);
                        continue;
                    }

                    foreach (var root in scene.GetRootGameObjects())
                    {
                        try
                        {
                            extractor.OnSceneRoot(root, paths[i], sink);
                        }
                        catch (Exception e)
                        {
                            sink.Note($"Scene scan failed: {e.GetType().Name}: {e.Message}", paths[i]);
                        }
                    }
                }
            }
            finally
            {
                RestoreSetup(setup, sink);
            }
        }

        private static void RestoreSetup(SceneSetup[] setup, ReferenceSink sink)
        {
            try
            {
                if (setup != null && setup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
            catch (Exception e)
            {
                sink.Note($"Could not restore the editor's scene setup after scanning: {e.Message}");
            }
        }

        private static bool HasUnsavedScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> PathsOfType(string filter) =>
            AssetDatabase.FindAssets(filter, SearchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

        private static string ToAbsolute(string assetPath) =>
            Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath);
    }
}
