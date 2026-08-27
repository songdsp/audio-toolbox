using UnityEngine;

namespace AudioToolbox.EventTracer
{
    /// <summary>
    /// The one MonoBehaviour in the module. Gives <see cref="AudioTraceRuntime.Pump"/> a
    /// frame to run on, and a shutdown to hook.
    /// </summary>
    /// <remarks>
    /// A hidden, don't-destroy object rather than a player loop insertion: it survives
    /// scene loads, it is visible to anyone profiling, and <c>OnApplicationQuit</c> is a
    /// reliable place to flush a session — which matters, because a trace that is only
    /// written when the game exits cleanly is a trace you will not have on the day you
    /// need it.
    /// </remarks>
    [AddComponentMenu("")]
    internal sealed class AudioTracePump : MonoBehaviour
    {
        private static AudioTracePump _instance;

        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("AudioToolbox EventTracer")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AudioTracePump>();
        }

        private void Update() => AudioTraceRuntime.Pump();

        private void OnApplicationPause(bool paused)
        {
            // Backgrounding a mobile build can be the last thing that happens before the
            // OS kills it, so treat it as the end of the session's writable life.
            if (paused)
            {
                AudioTraceRuntime.Pump();
            }
        }

        private void OnApplicationQuit() => AudioTraceRuntime.Shutdown();

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
