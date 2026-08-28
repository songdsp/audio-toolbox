using System.Collections;
using UnityEngine;

namespace AudioToolbox.EventTracer.Demo
{
    /// <summary>
    /// One object in the scene that fails to be heard in one specific way.
    /// </summary>
    /// <remarks>
    /// Each of these is an emitter in the ordinary sense — it has a position, it posts
    /// through <see cref="AudioTrace"/>, and the trace records it under its scene path.
    /// What makes it a demo is that the conditions for its particular failure are set up
    /// in advance, so the failure happens the moment you ask rather than once an hour in
    /// a playtest.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TraceDemoEmitter : MonoBehaviour
    {
        [Tooltip("Which way this emitter fails to be heard.")]
        public TraceDemoCase Case = TraceDemoCase.PlaysFine;

        [Tooltip("Seconds before the game stops the sound. Only used by the StoppedByTheGame case.")]
        public float StopAfterSeconds = 0.6f;

        [Tooltip("Seconds between the sound and the rival sound that interferes with it.")]
        public float RivalDelaySeconds = 0.25f;

        /// <summary>What the tracer currently says became of the sound this case is about.</summary>
        public PlaybackOutcome LastOutcome { get; private set; } = PlaybackOutcome.NotCalled;

        /// <summary>True once the outcome can no longer change.</summary>
        public bool HasSettled { get; private set; }

        /// <summary>True from the moment it is fired until it settles.</summary>
        public bool IsRunning { get; private set; }

        private AudioTraceHandle _subject;
        private bool _fired;

#if AUDIOTOOLBOX_TRACE
        private static readonly System.Collections.Generic.List<AudioTraceRecord> Scratch =
            new System.Collections.Generic.List<AudioTraceRecord>();
#endif

        private Renderer _renderer;
        private MaterialPropertyBlock _properties;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public string EventKey => TraceDemoCases.EventKey(Case);

        public PlaybackOutcome Expected => TraceDemoCases.Expected(Case);

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _properties = new MaterialPropertyBlock();
            Tint(TraceDemoPalette.Idle);
        }

        /// <summary>Runs this case. Safe to call again once it has settled.</summary>
        public void Fire()
        {
            if (IsRunning)
            {
                return;
            }

            StopAllCoroutines();
            StartCoroutine(Run());
        }

        public void ResetCase()
        {
            StopAllCoroutines();
            IsRunning = false;
            HasSettled = false;
            _fired = false;
            LastOutcome = PlaybackOutcome.NotCalled;
            Tint(TraceDemoPalette.Idle);
        }

        private IEnumerator Run()
        {
            IsRunning = true;
            HasSettled = false;
            LastOutcome = PlaybackOutcome.NotCalled;
            Tint(TraceDemoPalette.Running);

            var key = EventKey;
            var first = AudioTrace.Post(key, transform);

            if (!first.IsValid)
            {
                // Nothing was created, so there is no handle to watch and no sound to wait
                // for. The record exists anyway — which is the entire point of the module,
                // and the one case where the facade's return value tells you less than the
                // trace does.
                LastOutcome = LastRecordedOutcome();
                HasSettled = true;
                IsRunning = false;
                Tint(TraceDemoPalette.For(LastOutcome, settled: true));
                yield break;
            }

            _subject = first;
            _fired = true;

            if (TraceDemoCases.NeedsRival(Case))
            {
                yield return new WaitForSeconds(RivalDelaySeconds);

                var second = AudioTrace.Post(key, transform);

                if (TraceDemoCases.WatchesTheSecondPost(Case))
                {
                    _subject = second;
                }
            }
            else if (Case == TraceDemoCase.StoppedByTheGame)
            {
                yield return new WaitForSeconds(StopAfterSeconds);

                // Through the facade rather than straight at the middleware, which is the
                // whole reason this can come back as StoppedEarly instead of Stolen: no
                // callback carries the fact that somebody asked.
                AudioTrace.Stop(first);
            }

            // Callbacks cross two thread hand-offs before the tracer sees them, so the
            // outcome arrives some frames after the post rather than on the same one.
            while (!HasSettled)
            {
                yield return null;
            }

            IsRunning = false;
        }

        /// <summary>
        /// Reads the outcome back after everything else has had its turn this frame.
        /// </summary>
        /// <remarks>
        /// In <c>LateUpdate</c> on purpose: the tracer drains the backend's signals from an
        /// <c>Update</c>, and reading before that would always be one frame stale.
        /// <para>
        /// The read happens once more on the frame the voice is found to be finished, and
        /// then stops. Voice slots are recycled, and
        /// <see cref="AudioTraceRuntime.GetOutcome"/> answers for whatever currently owns
        /// the slot — so a demo that kept polling a finished sound would eventually start
        /// reporting somebody else's outcome under this row's name.
        /// </para>
        /// </remarks>
        private void LateUpdate()
        {
            if (!_fired || HasSettled)
            {
                return;
            }

            var alive = AudioTrace.IsAlive(_subject);

#if AUDIOTOOLBOX_TRACE
            LastOutcome = AudioTraceRuntime.GetOutcome(_subject);
#endif

            if (!alive)
            {
                HasSettled = true;
            }

            Tint(TraceDemoPalette.For(LastOutcome, HasSettled));
        }

        /// <summary>
        /// The outcome on the newest record, for the one case that has no handle.
        /// </summary>
        /// <remarks>
        /// A post that failed outright is final the moment it returns, so the record it
        /// left is the last one in the session. Copying the whole ring to read one record
        /// is wasteful, and acceptable exactly once per button press in a demo; a tool
        /// would read the session file instead.
        /// </remarks>
        private static PlaybackOutcome LastRecordedOutcome()
        {
#if AUDIOTOOLBOX_TRACE
            AudioTraceRuntime.SnapshotRecords(Scratch);

            return Scratch.Count > 0
                ? Scratch[Scratch.Count - 1].Outcome
                : PlaybackOutcome.NotCalled;
#else
            return PlaybackOutcome.NotCalled;
#endif
        }

        private void Tint(Color color)
        {
            if (_renderer == null)
            {
                return;
            }

            _properties.Clear();

            // URP's lit shader reads _BaseColor; the built-in pipeline reads _Color. Set
            // both rather than guess which project this sample was dropped into.
            _properties.SetColor(BaseColorId, color);
            _properties.SetColor(ColorId, color);
            _renderer.SetPropertyBlock(_properties);
        }
    }
}
