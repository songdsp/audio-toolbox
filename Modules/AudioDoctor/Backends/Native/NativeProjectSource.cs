using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using UnityEditor;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Backends.Native
{
    /// <summary>
    /// The no-middleware fallback: plain Unity AudioClips stand in for events and
    /// AssetBundles stand in for banks.
    /// </summary>
    /// <remarks>
    /// This backend exists so the tool has something to do on a machine with neither
    /// FMOD nor Wwise installed — which is how most people will first open the
    /// repository. It is also the honest baseline for the support matrix: Unity's own
    /// audio has no parameters and no per-event spatialization, so the rules that
    /// need those are skipped here rather than approximated.
    /// </remarks>
    public sealed class NativeProjectSource : IAudioProjectSource
    {
        private const string ResourcesPseudoBank = "(Resources)";
        private const string StreamingPseudoBank = "(StreamingAssets)";

        private List<ClipRecord> _clips;

        public string BackendId => "native";

        public string DisplayName => "Unity Native Audio";

        /// <summary>Zero, so any real middleware backend outranks this one.</summary>
        public int Priority => 0;

        public bool IsAvailable => true;

        public string GetUnavailableReason() => string.Empty;

        public BackendCapability Capabilities
        {
            get
            {
                // Every AudioClip under Assets is listed whether or not it is in a
                // bundle, so unpacked assets are visible here.
                var capabilities = BackendCapability.EventLength |
                                   BackendCapability.StreamingFlag |
                                   BackendCapability.UnpackedEvents;

                // Claiming bank membership in a project that uses no AssetBundles would
                // make R002 report every single clip as unpacked. An unclaimed capability
                // produces a skipped rule with a stated reason, which is the truthful
                // outcome; a claimed one would produce a wall of false positives.
                if (LoadClips().Any(c => !string.IsNullOrEmpty(c.BankName)))
                {
                    capabilities |= BackendCapability.BankMembership;
                }

                return capabilities;
            }
        }

        public IReadOnlyList<EventDef> GetAuthoredEvents(ScanContext context)
        {
            var records = LoadClips();
            var events = new List<EventDef>(records.Count);

            for (var i = 0; i < records.Count; i++)
            {
                context.ThrowIfCancelled();
                context.Progress.Report("Authored events", records[i].AssetPath, (float)i / Math.Max(1, records.Count));

                var record = records[i];

                events.Add(new EventDef
                {
                    Key = record.AssetPath,
                    BackendId = record.Guid,
                    Is3D = null,          // A clip is not spatial; its AudioSource is.
                    IsStreaming = record.IsStreaming,
                    LengthSeconds = record.LengthSeconds,
                    Parameters = Array.Empty<string>(),
                    BankNames = string.IsNullOrEmpty(record.BankName)
                        ? Array.Empty<string>()
                        : new[] { record.BankName },
                    Extras = new Dictionary<string, string>
                    {
                        { "Channels", record.Channels.ToString() },
                        { "Frequency", record.Frequency + " Hz" },
                        { "LoadType", record.LoadType },
                    },
                });
            }

            return events;
        }

        public IReadOnlyList<BankDef> GetBanks(ScanContext context)
        {
            var records = LoadClips().Where(c => !string.IsNullOrEmpty(c.BankName));

            return records
                .GroupBy(c => c.BankName, StringComparer.Ordinal)
                .Select(group => new BankDef
                {
                    Name = group.Key,
                    // Unity's own build produces one bundle per target, but the editor
                    // cannot enumerate them before a build, so a single logical platform
                    // is all this backend can honestly report. PlatformBanks stays unset
                    // and R009 is skipped rather than run against fabricated platforms.
                    Platform = "Editor",
                    SizeBytes = group.Sum(c => c.SizeBytes),
                    EventKeys = group.Select(c => c.AssetPath).OrderBy(p => p, StringComparer.Ordinal).ToList(),
                })
                .OrderBy(b => b.Name, StringComparer.Ordinal)
                .ToList();
        }

        public IReadOnlyList<string> GetGlobalParameters(ScanContext context) => Array.Empty<string>();

        public void FindReferences(ScanContext context, ReferenceSink sink)
        {
            if (LoadClips().Count == 0)
            {
                sink.Note(
                    "No AudioClip assets were found under Assets/, so this scan had nothing to " +
                    "reconcile. Install and configure FMOD or Wwise, or add audio assets, to get " +
                    "a meaningful report.");
            }

            ProjectWalker.Walk(new NativeReferenceExtractor(), context, sink);
        }

        private List<ClipRecord> LoadClips()
        {
            if (_clips != null)
            {
                return _clips;
            }

            _clips = new List<ClipRecord>();

            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

                if (clip == null)
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                var settings = importer?.defaultSampleSettings ?? default;

                _clips.Add(new ClipRecord
                {
                    AssetPath = path,
                    Guid = guid,
                    LengthSeconds = clip.length,
                    Channels = clip.channels,
                    Frequency = clip.frequency,
                    IsStreaming = settings.loadType == AudioClipLoadType.Streaming,
                    LoadType = settings.loadType.ToString(),
                    BankName = ResolveBankName(path, importer),
                    SizeBytes = FileSize(path),
                });
            }

            _clips.Sort((a, b) => string.CompareOrdinal(a.AssetPath, b.AssetPath));

            return _clips;
        }

        /// <summary>
        /// The closest Unity analogue of a bank: an AssetBundle, or one of the two
        /// folder conventions that also decide what ships.
        /// </summary>
        private static string ResolveBankName(string assetPath, AssetImporter importer)
        {
            var bundle = importer?.assetBundleName;

            if (!string.IsNullOrEmpty(bundle))
            {
                return bundle;
            }

            if (assetPath.Contains("/Resources/", StringComparison.Ordinal))
            {
                return ResourcesPseudoBank;
            }

            if (assetPath.Contains("/StreamingAssets/", StringComparison.Ordinal))
            {
                return StreamingPseudoBank;
            }

            return null;
        }

        private static long FileSize(string assetPath)
        {
            try
            {
                var absolute = Path.Combine(
                    Directory.GetParent(Application.dataPath)!.FullName, assetPath);

                var info = new FileInfo(absolute);
                return info.Exists ? info.Length : -1;
            }
            catch
            {
                return -1;
            }
        }

        private struct ClipRecord
        {
            public string AssetPath;
            public string Guid;
            public float LengthSeconds;
            public int Channels;
            public int Frequency;
            public bool IsStreaming;
            public string LoadType;
            public string BankName;
            public long SizeBytes;
        }
    }
}
