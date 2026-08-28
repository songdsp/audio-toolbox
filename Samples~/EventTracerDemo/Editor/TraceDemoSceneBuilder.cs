using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AudioToolbox.EventTracer.Demo.Editor
{
    /// <summary>
    /// Builds the demo scene from nothing, rather than shipping a .unity file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scene asset in a sample is a scene asset that was authored against one render
    /// pipeline, one Unity version and one set of packages, and it arrives broken in
    /// anything else — pink materials, a missing camera component, a serialised reference
    /// to a package that is not installed. Generated at import time it is instead built
    /// from primitives and the project's own default material, which is correct wherever
    /// it lands.
    /// </para>
    /// <para>
    /// It is also readable. What the scene contains and why each object is where it is
    /// says more here, in fifty lines, than it would as a diff of serialised YAML.
    /// </para>
    /// </remarks>
    public static class TraceDemoSceneBuilder
    {
        private const string MenuPath = "Window/Audio Toolbox/EventTracer/Create Demo Scene";

        /// <summary>Distance from the listener to the arc of near emitters.</summary>
        private const float ArcRadius = 5.5f;

        /// <summary>
        /// How far away the out-of-earshot emitter sits.
        /// </summary>
        /// <remarks>
        /// The fixture's Spatial3D event has a max distance of 10 m, so this is comfortably
        /// past the point where FMOD stops giving it a real voice. Far enough to be the
        /// obvious explanation on screen, too: it is visibly a speck.
        /// </remarks>
        private const float FarDistance = 40f;

        [MenuItem(MenuPath, priority = 117)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildFloor();

            var camera = BuildCameraAndListener();
            var emitters = BuildEmitters();

            BuildStage(emitters);

            Selection.activeObject = camera.gameObject;

            var path = SavePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");

            if (!EditorSceneManager.SaveScene(scene, path))
            {
                Debug.LogWarning($"[EventTracer] The demo scene was built but could not be saved to {path}.");
                return;
            }

            AssetDatabase.Refresh();

            Debug.Log(
                $"[EventTracer] Demo scene saved to {path}.\n" +
                "Press Play, then Run All. Each object turns the colour of its outcome. " +
                "Afterwards: Window ▸ Audio Toolbox ▸ EventTracer ▸ Timeline ▸ Latest Session.");

            WarnAboutMissingPieces();
        }

        private static string SavePath()
        {
            // Beside the sample's own scripts when it was imported through Package Manager,
            // so that deleting the sample takes the scene with it.
            var scriptFolder = ScriptFolder();

            return string.IsNullOrEmpty(scriptFolder)
                ? "Assets/EventTracerDemo.unity"
                : $"{scriptFolder}/EventTracerDemo.unity";
        }

        private static string ScriptFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets("TraceDemoStage t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.EndsWith("TraceDemoStage.cs", StringComparison.Ordinal))
                {
                    // .../Runtime/TraceDemoStage.cs -> .../
                    return Path.GetDirectoryName(Path.GetDirectoryName(path))?.Replace('\\', '/');
                }
            }

            return null;
        }

        private static void BuildLighting()
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();

            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f);
        }

        private static void BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);

            floor.name = "Floor";
            floor.transform.localScale = new Vector3(12f, 1f, 12f);

            // Far enough forward to reach past the distant emitter, so the eye reads the
            // gap as distance rather than as the object floating off the edge of the world.
            floor.transform.position = new Vector3(0f, 0f, 14f);
        }

        private static Camera BuildCameraAndListener()
        {
            var go = new GameObject("Main Camera");
            var camera = go.AddComponent<Camera>();

            go.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 2.6f, -9f);
            camera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            camera.backgroundColor = new Color(0.14f, 0.15f, 0.18f);
            camera.clearFlags = CameraClearFlags.SolidColor;

            go.AddComponent<AudioListener>();
            AddFmodListener(go);

            return camera;
        }

        /// <summary>
        /// Adds FMOD's listener when FMOD is installed, by name rather than by reference.
        /// </summary>
        /// <remarks>
        /// A direct reference would mean this assembly needs FMOD to compile, and a define
        /// constraint would delete the whole scene builder in a project without it. Looking
        /// the type up means the sample builds its scene either way and says what is
        /// missing, which is more useful than not appearing in the menu.
        /// </remarks>
        private static void AddFmodListener(GameObject target)
        {
            var type = Type.GetType("FMODUnity.StudioListener, FMODUnity");

            if (type != null)
            {
                target.AddComponent(type);
            }
        }

        private static List<TraceDemoEmitter> BuildEmitters()
        {
            var cases = TraceDemoCases.InPresentationOrder;
            var emitters = new List<TraceDemoEmitter>(cases.Length);

            // Everything but the distant one goes on an arc in front of the listener, so
            // the six that share a stage are visibly peers and the seventh is visibly not.
            var near = new List<TraceDemoCase>();

            foreach (var demoCase in cases)
            {
                if (demoCase != TraceDemoCase.OutOfEarshot)
                {
                    near.Add(demoCase);
                }
            }

            var placed = new Dictionary<TraceDemoCase, TraceDemoEmitter>();

            for (var i = 0; i < near.Count; i++)
            {
                var spread = near.Count > 1 ? i / (float)(near.Count - 1) : 0.5f;
                var angle = Mathf.Lerp(-70f, 70f, spread) * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Sin(angle) * ArcRadius, 0.5f, Mathf.Cos(angle) * ArcRadius);

                placed[near[i]] = CreateEmitter(near[i], position);
            }

            placed[TraceDemoCase.OutOfEarshot] =
                CreateEmitter(TraceDemoCase.OutOfEarshot, new Vector3(0f, 0.5f, FarDistance));

            // Returned in presentation order, not in the order they were placed: the panel
            // and Run All read this list top to bottom.
            foreach (var demoCase in cases)
            {
                emitters.Add(placed[demoCase]);
            }

            return emitters;
        }

        private static TraceDemoEmitter CreateEmitter(TraceDemoCase demoCase, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);

            // The scene path is what the trace records and what the timeline groups by, so
            // the name is not decoration - it is the label on the lane.
            go.name = demoCase.ToString();
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.9f;

            // Nothing in the demo needs physics, and a collider on every emitter is one
            // more thing to explain in a video about audio.
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());

            var emitter = go.AddComponent<TraceDemoEmitter>();
            emitter.Case = demoCase;

            return emitter;
        }

        private static void BuildStage(List<TraceDemoEmitter> emitters)
        {
            var stage = new GameObject("EventTracer Demo").AddComponent<TraceDemoStage>();
            stage.Emitters = emitters;
        }

        /// <summary>
        /// Says what the scene needs that this project does not have, once, at build time.
        /// </summary>
        /// <remarks>
        /// Both of these produce a demo where every button yields
        /// <see cref="PlaybackOutcome.HandleInvalid"/> and nothing explains why — the exact
        /// experience the module exists to prevent, which would be a poor thing for its own
        /// sample to reproduce.
        /// </remarks>
        private static void WarnAboutMissingPieces()
        {
            if (Type.GetType("FMODUnity.StudioListener, FMODUnity") == null)
            {
                Debug.LogWarning(
                    "[EventTracer] FMOD is not installed, so the demo will fall back to the native " +
                    "backend and none of the fixture events will resolve. The demo needs FMOD.");
                return;
            }

            var fixturePresent = false;

            foreach (var guid in AssetDatabase.FindAssets("TraceFixture"))
            {
                if (AssetDatabase.GUIDToAssetPath(guid).Contains("TraceFixture"))
                {
                    fixturePresent = true;
                    break;
                }
            }

            if (!fixturePresent)
            {
                Debug.LogWarning(
                    "[EventTracer] The TraceFixture bank was not found in this project. The demo's " +
                    "events come from it — run Tools/TraceFixture~/build-trace-fixture.ps1 against " +
                    "your FMOD project, then reimport the banks.");
            }
        }
    }
}
