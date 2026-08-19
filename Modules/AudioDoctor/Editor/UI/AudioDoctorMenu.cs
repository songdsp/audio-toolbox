using System;
using System.IO;
using System.Threading;
using AudioToolbox.AudioDoctor.Core;
using UnityEditor;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Menu entry points. The full UI Toolkit window replaces the console output in
    /// Phase 4; this keeps the whole pipeline reachable and demonstrable before then.
    /// </summary>
    public static class AudioDoctorMenu
    {
        private const string ReportFolder = "AudioDoctorReports";

        [MenuItem("Window/Audio Toolbox/AudioDoctor/Run Validation (Console)", priority = 110)]
        public static void RunValidation()
        {
            ValidationReport report;
            var cancellation = new CancellationTokenSource();

            try
            {
                report = AudioDoctorRunner.Run(new RunOptions
                {
                    RuleSet = AudioDoctorRunner.FindProjectRuleSet(),
                    Token = cancellation.Token,
                    Progress = new DelegateProgressSink((stage, detail, normalized) =>
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "AudioDoctor", $"{stage}: {detail}", normalized))
                        {
                            cancellation.Cancel();
                        }
                    }),
                });
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AudioDoctor] Scan cancelled.");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var folder = WriteReports(report);

            Debug.Log(
                $"[AudioDoctor] {report.BackendDisplayName}: " +
                $"{report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.InfoCount} note(s) " +
                $"across {report.EventCount} event(s), {report.BankCount} bank(s), " +
                $"{report.ReferenceCount} reference(s) in {report.ScanSeconds:0.00}s.\n" +
                $"Reports written to {folder}");

            foreach (var issue in report.Issues)
            {
                switch (issue.Severity)
                {
                    case Severity.Error:
                        Debug.LogError($"[AudioDoctor] {issue}");
                        break;
                    case Severity.Warning:
                        Debug.LogWarning($"[AudioDoctor] {issue}");
                        break;
                    default:
                        Debug.Log($"[AudioDoctor] {issue}");
                        break;
                }
            }
        }

        [MenuItem("Window/Audio Toolbox/AudioDoctor/Open Report Folder", priority = 111)]
        public static void OpenReportFolder()
        {
            var folder = ReportFolderPath();
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        public static string WriteReports(ValidationReport report)
        {
            var folder = ReportFolderPath();
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "audiodoctor-report.json"), JsonReportWriter.Write(report));
            File.WriteAllText(Path.Combine(folder, "audiodoctor-report.md"), MarkdownReportWriter.Write(report));

            return folder;
        }

        private static string ReportFolderPath() =>
            Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, ReportFolder);
    }
}
