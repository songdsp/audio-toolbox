using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AudioToolbox.EventTracer.Demo
{
    /// <summary>
    /// The demo's control panel: seven ways a sound goes missing, each on a button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Run all</b> is the one to press with a screen recorder going. It fires the cases
    /// in order with a gap between them, which gives the session a readable time axis
    /// afterwards — seven distinct columns rather than one pile.
    /// </para>
    /// <para>
    /// Drawn with IMGUI rather than UI Toolkit or uGUI, and that is a deliberate trade. A
    /// sample that arrives needing a PanelSettings asset, a Canvas, an EventSystem and a
    /// working input backend is a sample that fails to open on somebody's machine. This
    /// draws itself with no assets and no input-system dependency, which is the property
    /// worth having in a demo.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TraceDemoStage : MonoBehaviour
    {
        [Tooltip("The emitters, in the order the panel lists and Run All fires them.")]
        public List<TraceDemoEmitter> Emitters = new List<TraceDemoEmitter>();

        [Tooltip("Seconds between cases during Run All. Wide enough that the timeline shows them apart.")]
        public float SecondsBetweenCases = 2.2f;

        private GUIStyle _title;
        private GUIStyle _subtitle;
        private GUIStyle _row;
        private GUIStyle _outcome;
        private GUIStyle _banner;
        private GUIStyle _worldLabel;
        private bool _stylesReady;
        private bool _running;

        private const float PanelWidth = 640f;

        /// <summary>
        /// Top margin. Wide enough to clear FMOD.s own debug overlay, which draws in the
        /// same corner and would otherwise sit on top of the title in every recording.
        /// Switching that overlay off in FMOD.s settings gives a cleaner capture still.
        /// </summary>
        private const float PanelTop = 96f;

        private void OnGUI()
        {
            EnsureStyles();

            GUILayout.BeginArea(new Rect(14f, PanelTop, PanelWidth, Screen.height - PanelTop - 14f));

            GUILayout.Label("EventTracer — seven ways a sound goes missing", _title);
            GUILayout.Label(
                "Every row below is silent or wrong in the game and identical from the call site. " +
                "Fire them, then open Window ▸ Audio Toolbox ▸ EventTracer ▸ Timeline.",
                _subtitle);

            DrawRecordingBanner();

            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();

            GUI.enabled = !_running;

            if (GUILayout.Button("▶  Run all", GUILayout.Height(30f), GUILayout.Width(140f)))
            {
                Run();
            }

            GUI.enabled = true;

            if (GUILayout.Button("Reset", GUILayout.Height(30f), GUILayout.Width(90f)))
            {
                ResetAll();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            for (var i = 0; i < Emitters.Count; i++)
            {
                DrawRow(Emitters[i]);
            }

            DrawFooter();
            GUILayout.EndArea();

            DrawWorldLabels();
        }

        /// <summary>
        /// Floats each case's name over the object it belongs to.
        /// </summary>
        /// <remarks>
        /// Projected in <c>OnGUI</c> rather than placed as <c>TextMesh</c> objects, which
        /// would need a font asset the sample does not ship and cannot rely on finding.
        /// Without these the panel lists seven cases and the scene shows seven identical
        /// boxes, and a viewer has no way to connect the two.
        /// </remarks>
        private void DrawWorldLabels()
        {
            var camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            for (var i = 0; i < Emitters.Count; i++)
            {
                var emitter = Emitters[i];

                if (emitter == null)
                {
                    continue;
                }

                var world = emitter.transform.position + Vector3.up * 0.9f;
                var screen = camera.WorldToScreenPoint(world);

                // Behind the camera projects to a point in front of it, mirrored. Drawing
                // that would put a label on an object nobody can see.
                if (screen.z <= 0f)
                {
                    continue;
                }

                var rect = new Rect(screen.x - 90f, Screen.height - screen.y - 30f, 180f, 40f);
                var settled = emitter.HasSettled;
                var known = emitter.LastOutcome != PlaybackOutcome.NotCalled;

                var previous = GUI.color;
                GUI.color = known ? TraceDemoPalette.For(emitter.LastOutcome, settled) : Color.white;

                GUI.Label(
                    rect,
                    known
                        ? $"{TraceDemoCases.ShortTag(emitter.Case)}\n{emitter.LastOutcome}"
                        : TraceDemoCases.ShortTag(emitter.Case),
                    _worldLabel);

                GUI.color = previous;
            }
        }

        private void DrawRecordingBanner()
        {
#if AUDIOTOOLBOX_TRACE
            if (AudioTrace.IsRecording)
            {
                return;
            }

            // Compiled in but not running: no backend registered, which on this sample
            // almost always means FMOD did not initialise.
            Banner(
                "Tracing is compiled in but no audio backend registered. " +
                "The buttons below will do nothing.",
                new Color(0.78f, 0.16f, 0.16f));
#else
            Banner(
                "AUDIOTOOLBOX_TRACE is off, so nothing is being recorded. Sounds still play. " +
                "Turn it on under Window ▸ Audio Toolbox ▸ EventTracer ▸ Record Traces, " +
                "then enter Play mode again.",
                new Color(0.79f, 0.59f, 0.10f));
#endif
        }

        private void Banner(string text, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, _banner);
            GUI.color = previous;
        }

        private void DrawRow(TraceDemoEmitter emitter)
        {
            if (emitter == null)
            {
                return;
            }

            GUILayout.BeginHorizontal(GUI.skin.box);

            GUI.enabled = !emitter.IsRunning && !_running;

            if (GUILayout.Button("Play", GUILayout.Width(64f), GUILayout.Height(38f)))
            {
                emitter.Fire();
            }

            GUI.enabled = true;

            GUILayout.BeginVertical();
            GUILayout.Label(TraceDemoCases.Title(emitter.Case), _row);
            GUILayout.Label(TraceDemoCases.Symptom(emitter.Case), _subtitle);
            GUILayout.EndVertical();

            DrawOutcome(emitter);

            GUILayout.EndHorizontal();
        }

        private void DrawOutcome(TraceDemoEmitter emitter)
        {
            string text;
            Color color;

            if (emitter.LastOutcome == PlaybackOutcome.NotCalled && !emitter.IsRunning)
            {
                text = "—";
                color = TraceDemoPalette.Idle;
            }
            else if (emitter.LastOutcome == PlaybackOutcome.NotCalled)
            {
                text = "waiting…";
                color = TraceDemoPalette.Running;
            }
            else
            {
                text = emitter.LastOutcome.ToString();
                color = TraceDemoPalette.For(emitter.LastOutcome, emitter.HasSettled);
            }

            var previous = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, _outcome, GUILayout.Width(150f));
            GUI.color = previous;
        }

        private void DrawFooter()
        {
            GUILayout.Space(6f);

#if AUDIOTOOLBOX_TRACE
            var path = AudioTrace.SessionPath;

            GUILayout.Label(
                string.IsNullOrEmpty(path)
                    ? "This session is being kept in memory only."
                    : "Writing to " + path,
                _subtitle);
#endif
        }

        /// <summary>
        /// Fires every case in order, spaced out. What the Run All button does, and the
        /// way to drive the demo from a script or a recording setup.
        /// </summary>
        public void Run()
        {
            if (!_running)
            {
                StartCoroutine(RunAll());
            }
        }

        /// <summary>True while <see cref="Run"/> is working through the cases.</summary>
        public bool IsRunningAll => _running;

        private IEnumerator RunAll()
        {
            _running = true;
            ResetAll();

            yield return new WaitForSeconds(0.4f);

            for (var i = 0; i < Emitters.Count; i++)
            {
                if (Emitters[i] == null)
                {
                    continue;
                }

                Emitters[i].Fire();
                yield return new WaitForSeconds(SecondsBetweenCases);
            }

            _running = false;
        }

        private void ResetAll()
        {
            for (var i = 0; i < Emitters.Count; i++)
            {
                if (Emitters[i] != null)
                {
                    Emitters[i].ResetCase();
                }
            }
        }

        /// <summary>
        /// Builds the styles once, sized for a screen recording rather than for a desk.
        /// </summary>
        /// <remarks>
        /// IMGUI's default font is sized for an editor inspector, which is unreadable once
        /// a capture is scaled down to fit in a pull request. Everything here is a few
        /// points larger than looks right in the Game view for exactly that reason.
        /// </remarks>
        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };

            _subtitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
            };

            _row = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
            };

            _outcome = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };

            _banner = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 6, 6),
            };

            _worldLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerCenter,
                wordWrap = false,
            };

            _stylesReady = true;
        }
    }
}
