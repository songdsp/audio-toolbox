using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AudioToolbox.Editor
{
    /// <summary>
    /// Keeps the AUDIOTOOLBOX_FMOD / AUDIOTOOLBOX_WWISE scripting defines in step with
    /// which middleware integrations are actually present.
    /// </summary>
    /// <remarks>
    /// The point is that anyone can clone this repository and have it compile on the
    /// first import, whether they have both middlewares installed, one, or neither.
    /// The backend assemblies carry matching define constraints, so an absent
    /// integration means its backend is never compiled rather than compiled and broken.
    /// </remarks>
    [InitializeOnLoad]
    public static class MiddlewareDetector
    {
        public const string FmodDefine = "AUDIOTOOLBOX_FMOD";
        public const string WwiseDefine = "AUDIOTOOLBOX_WWISE";

        /// <summary>Types that prove an integration is present, in the order they were introduced.</summary>
        private static readonly string[] FmodProbeTypes =
        {
            "FMODUnity.RuntimeManager",
            "FMODUnity.EventReference",
        };

        private static readonly string[] WwiseProbeTypes =
        {
            // Renamed in Wwise 2023.1; probe both so either generation is detected.
            "AkUnitySoundEngine",
            "AkSoundEngine",
        };

        static MiddlewareDetector()
        {
            if (Application.isBatchMode)
            {
                // delayCall never fires under -batchmode -quit: the editor exits before
                // the callback is pumped. A CI run would then compile with the defines
                // it happened to inherit, which is exactly the "works on my machine"
                // failure this class exists to prevent. Note that the defines only take
                // effect on the *next* compile, so a batch pipeline needs one warm-up
                // invocation before the one that runs the tests.
                Synchronize(logWhenChanged: true);
                return;
            }

            // Interactively, defer: touching PlayerSettings from a static constructor
            // during a domain reload can race the asset database on a fresh import.
            EditorApplication.delayCall += () => Synchronize(logWhenChanged: true);
        }

        public static bool IsFmodPresent => AnyTypeExists(FmodProbeTypes);

        public static bool IsWwisePresent => AnyTypeExists(WwiseProbeTypes);

        [MenuItem("Window/Audio Toolbox/Re-detect Middleware", priority = 200)]
        public static void ReDetect()
        {
            if (!Synchronize(logWhenChanged: true))
            {
                Debug.Log(
                    $"[Audio Toolbox] Defines already correct. FMOD: {(IsFmodPresent ? "present" : "absent")}, " +
                    $"Wwise: {(IsWwisePresent ? "present" : "absent")}.");
            }
        }

        /// <summary>Returns true when the define set was changed and a recompile was triggered.</summary>
        public static bool Synchronize(bool logWhenChanged)
        {
            var target = ActiveNamedBuildTarget();
            if (target == null)
            {
                return false;
            }

            var current = PlayerSettings.GetScriptingDefineSymbols(target.Value)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            var desired = new List<string>(current);
            var changed = false;

            changed |= Apply(desired, FmodDefine, IsFmodPresent);
            changed |= Apply(desired, WwiseDefine, IsWwisePresent);

            if (!changed)
            {
                // Writing an unchanged set would still trigger a domain reload, which
                // would run this again on every reload. Bail out and let it settle.
                return false;
            }

            PlayerSettings.SetScriptingDefineSymbols(target.Value, desired.ToArray());

            if (logWhenChanged)
            {
                Debug.Log(
                    $"[Audio Toolbox] Updated scripting defines for {target.Value.TargetName}: " +
                    $"{string.Join(";", desired)}");
            }

            return true;
        }

        private static bool Apply(List<string> defines, string symbol, bool shouldBePresent)
        {
            var isPresent = defines.Contains(symbol, StringComparer.Ordinal);

            if (shouldBePresent && !isPresent)
            {
                defines.Add(symbol);
                return true;
            }

            if (!shouldBePresent && isPresent)
            {
                defines.RemoveAll(d => string.Equals(d, symbol, StringComparison.Ordinal));
                return true;
            }

            return false;
        }

        private static NamedBuildTarget? ActiveNamedBuildTarget()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown)
            {
                return null;
            }

            try
            {
                return NamedBuildTarget.FromBuildTargetGroup(group);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Audio Toolbox] Could not resolve build target group {group}: {e.Message}");
                return null;
            }
        }

        private static bool AnyTypeExists(IReadOnlyList<string> typeNames)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                for (var i = 0; i < typeNames.Count; i++)
                {
                    if (assembly.GetType(typeNames[i], throwOnError: false) != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
