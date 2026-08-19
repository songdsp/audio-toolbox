using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AudioToolbox.AudioDoctor.Core;
using UnityEditor;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>What a single run should do.</summary>
    public sealed class RunOptions
    {
        /// <summary>Null or empty resolves to the highest-priority usable backend.</summary>
        public string BackendId;

        /// <summary>Null falls back to the project's default rule set, then to built-in defaults.</summary>
        public RuleSetAsset RuleSet;

        public IProgressSink Progress = NullProgressSink.Instance;

        public CancellationToken Token = CancellationToken.None;
    }

    /// <summary>
    /// The one entry point shared by the editor window, the tests and the CLI.
    /// Keeping all three on the same path is what makes "it passed in the editor"
    /// and "it passed in CI" mean the same thing.
    /// </summary>
    public static class AudioDoctorRunner
    {
        public static ValidationReport Run(RunOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var backend = BackendRegistry.Resolve(options.BackendId);
            if (backend == null)
            {
                throw new InvalidOperationException(
                    "No audio backend is compiled. At minimum the Native fallback should be present — " +
                    "check that AudioToolbox.AudioDoctor.Backend.Native compiled.");
            }

            var ruleSet = options.RuleSet ?? RuleSetAsset.CreateDefault();
            var context = new ScanContext(options.Progress ?? NullProgressSink.Instance, options.Token);

            var scanWatch = Stopwatch.StartNew();
            var snapshot = ProjectScanner.Scan(backend, context);
            scanWatch.Stop();

            NoteBackendsThatWereNotUsed(backend, snapshot);

            options.Progress?.Report("Rules", "Evaluating", 0.9f);

            var ruleWatch = Stopwatch.StartNew();
            RuleEngine.Run(
                new RuleContext(snapshot, ruleSet),
                RuleEngine.DiscoverRules(),
                out var issues,
                out var skipped);
            ruleWatch.Stop();

            options.Progress?.Report("Done", $"{issues.Count} issue(s)", 1f);

            return new ValidationReport
            {
                BackendId = backend.BackendId,
                BackendDisplayName = backend.DisplayName,
                ProjectName = Application.productName,
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                ScanSeconds = scanWatch.Elapsed.TotalSeconds,
                RuleSeconds = ruleWatch.Elapsed.TotalSeconds,
                Issues = issues,
                SkippedRules = skipped,
                Capabilities = snapshot.Capabilities,
                EventCount = snapshot.Events.Count,
                BankCount = snapshot.Banks.Count,
                ReferenceCount = snapshot.References.Count,
            };
        }

        /// <summary>
        /// Records why a compiled backend was passed over.
        /// </summary>
        /// <remarks>
        /// A project with FMOD installed but no banks built silently falls back to the
        /// Native backend, and the resulting report looks like a clean FMOD scan to
        /// anyone who does not know the fallback exists. Saying so turns a confusing
        /// result into an actionable one.
        /// </remarks>
        private static void NoteBackendsThatWereNotUsed(IAudioProjectSource used, AudioProjectSnapshot snapshot)
        {
            var notes = new List<ScanNote>(snapshot.Notes);

            foreach (var candidate in BackendRegistry.All())
            {
                if (candidate.BackendId == used.BackendId || candidate.Priority <= used.Priority)
                {
                    continue;
                }

                string reason;

                try
                {
                    if (candidate.IsAvailable)
                    {
                        continue;
                    }

                    reason = candidate.GetUnavailableReason();
                }
                catch (Exception e)
                {
                    reason = $"it failed while reporting its availability: {e.Message}";
                }

                notes.Insert(0, new ScanNote
                {
                    Message =
                        $"{candidate.DisplayName} is installed but was not used, so this report " +
                        $"covers {used.DisplayName} instead. {reason}",
                });
            }

            snapshot.Notes = notes;
        }

        /// <summary>
        /// Looks for a rule set asset in the project. Returns null when none exists,
        /// which the runner turns into built-in defaults.
        /// </summary>
        public static RuleSetAsset FindProjectRuleSet()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(RuleSetAsset)}");

            if (guids.Length == 0)
            {
                return null;
            }

            if (guids.Length > 1)
            {
                UnityEngine.Debug.LogWarning(
                    $"[AudioDoctor] Found {guids.Length} rule set assets; using " +
                    $"{AssetDatabase.GUIDToAssetPath(guids[0])}. Keep one per project to avoid ambiguity.");
            }

            return AssetDatabase.LoadAssetAtPath<RuleSetAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
