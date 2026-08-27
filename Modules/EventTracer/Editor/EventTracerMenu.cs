using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AudioToolbox.EventTracer.Editor
{
    /// <summary>
    /// Menu entry points for the tracer.
    /// </summary>
    /// <remarks>
    /// The timeline window arrives in Phase 4; until then this keeps the whole pipeline
    /// reachable and demonstrable — post some sounds, dump the session, read the seven
    /// outcomes. Following the same shape as AudioDoctor's console-first menu, for the
    /// same reason: a pipeline you cannot see the output of is a pipeline you cannot
    /// tell is broken.
    /// </remarks>
    public static class EventTracerMenu
    {
        public const string TraceDefine = "AUDIOTOOLBOX_TRACE";

        private const string ToggleMenuPath = "Window/Audio Toolbox/EventTracer/Record Traces";

        /// <summary>
        /// Switches the collection layer on and off for the active build target.
        /// </summary>
        /// <remarks>
        /// Left to a person rather than detected the way middleware presence is. Whether
        /// FMOD is installed is a fact about the project; whether this build should carry
        /// a tracer is a decision about the build, and nothing here is entitled to make
        /// it. Note that it applies to the active build target only — a define set for
        /// the editor does not follow a console build.
        /// </remarks>
        [MenuItem(ToggleMenuPath, priority = 120)]
        public static void ToggleTraceDefine()
        {
            var target = ActiveNamedBuildTarget();

            if (target == null)
            {
                Debug.LogWarning("[EventTracer] No active build target group; cannot change scripting defines.");
                return;
            }

            var defines = PlayerSettings.GetScriptingDefineSymbols(target.Value)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            if (defines.Contains(TraceDefine, StringComparer.Ordinal))
            {
                defines.RemoveAll(d => string.Equals(d, TraceDefine, StringComparison.Ordinal));
                Debug.Log("[EventTracer] Trace recording off. The facade still plays sounds; nothing is collected.");
            }
            else
            {
                defines.Add(TraceDefine);
                Debug.Log("[EventTracer] Trace recording on.");
            }

            PlayerSettings.SetScriptingDefineSymbols(target.Value, defines.ToArray());
        }

        [MenuItem(ToggleMenuPath, validate = true)]
        private static bool ValidateToggleTraceDefine()
        {
            Menu.SetChecked(ToggleMenuPath, IsTraceDefineSet());
            return true;
        }

        public static bool IsTraceDefineSet()
        {
            var target = ActiveNamedBuildTarget();

            if (target == null)
            {
                return false;
            }

            return PlayerSettings.GetScriptingDefineSymbols(target.Value)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(d => string.Equals(d.Trim(), TraceDefine, StringComparison.Ordinal));
        }

        [MenuItem("Window/Audio Toolbox/EventTracer/Dump Latest Session (Console)", priority = 121)]
        public static void DumpLatestSession()
        {
            var path = TraceLogReader.FindLatestSession();

            if (string.IsNullOrEmpty(path))
            {
                Debug.Log(
                    $"[EventTracer] No sessions under {TraceLogReader.SessionFolder}. " +
                    "Enter Play mode with tracing on and post a sound.");
                return;
            }

            DumpSession(path);
        }

        /// <summary>Reads one session and writes a readable summary to the console.</summary>
        public static void DumpSession(string path)
        {
            TraceSession session;

            try
            {
                session = TraceLogReader.Read(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EventTracer] Could not read {path}: {e.Message}");
                return;
            }

            var text = new StringBuilder();

            text.AppendLine($"[EventTracer] {Path.GetFileName(path)}");
            text.AppendLine(
                $"  {session.Header.BackendId} {session.Header.BackendVersion} · " +
                $"Unity {session.Header.UnityVersion} · {session.Header.Platform} · started {session.Header.StartedUtc}");
            text.AppendLine($"  {session.Records.Count} record(s), buffer {session.Header.RecordCapacity}");

            if (session.Header.DroppedRecordCount > 0)
            {
                // First, and loudly. Every count below is a lower bound if this is not zero.
                text.AppendLine(
                    $"  INCOMPLETE: {session.Header.DroppedRecordCount} record(s) were overwritten before they " +
                    "could be written. Raise AudioTraceSettings.RecordCapacity.");
            }

            if (session.EndedAbruptly)
            {
                text.AppendLine("  The log ends mid-chunk — the process probably did not exit cleanly.");
            }

            foreach (PlaybackOutcome outcome in Enum.GetValues(typeof(PlaybackOutcome)))
            {
                var count = session.Records.Count(r => r.Outcome == outcome);

                if (count > 0)
                {
                    text.AppendLine($"    {outcome,-14} {count}");
                }
            }

            text.AppendLine();

            foreach (var record in session.Records.Where(r => r.Outcome != PlaybackOutcome.Started).Take(50))
            {
                text.AppendLine(Describe(session, record));
            }

            var silent = session.Records.Count(r => r.Outcome != PlaybackOutcome.Started);

            if (silent > 50)
            {
                text.AppendLine($"    ... and {silent - 50} more that did not simply play.");
            }

            Debug.Log(text.ToString());
        }

        private static string Describe(TraceSession session, in AudioTraceRecord record)
        {
            var distance = record.DistanceToListener < 0
                ? "no listener"
                : $"{record.DistanceToListener:0.0}m";

            return
                $"    f{record.Frame,-7} {record.TimeSeconds,8:0.000}s  {record.Outcome,-14} " +
                $"{session.Resolve(record.EventKeyId)}  [{distance}]  " +
                $"{session.Resolve(record.CallSiteId)}" +
                (record.BackendResultCode != 0 ? $"  (backend code {record.BackendResultCode})" : string.Empty);
        }

        [MenuItem("Window/Audio Toolbox/EventTracer/Open Trace Folder", priority = 122)]
        public static void OpenTraceFolder()
        {
            var folder = TraceLogReader.SessionFolder;
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
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
                Debug.LogWarning($"[EventTracer] Could not resolve build target group {group}: {e.Message}");
                return null;
            }
        }
    }
}
