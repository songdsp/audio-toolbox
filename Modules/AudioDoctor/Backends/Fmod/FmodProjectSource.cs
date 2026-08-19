using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using FMODUnity;

namespace AudioToolbox.AudioDoctor.Backends.Fmod
{
    /// <summary>
    /// Reads what FMOD Studio declared, through the integration's editor-side cache.
    /// </summary>
    /// <remarks>
    /// Everything here comes from <see cref="EventManager"/>, which the FMOD Unity
    /// integration populates from the built banks. That means AudioDoctor sees exactly
    /// what the Unity project sees - including the case where the cache is stale, which
    /// is itself worth reporting rather than working around.
    /// </remarks>
    public sealed class FmodProjectSource : IAudioProjectSource
    {
        public string BackendId => "fmod";

        public string DisplayName => "FMOD Studio";

        public int Priority => 100;

        public bool IsAvailable => EventManager.IsValid && EventManager.Events.Count > 0;

        public string GetUnavailableReason()
        {
            if (!EventManager.IsValid)
            {
                return
                    "FMOD's event cache has never been built. Open FMOD Settings, point " +
                    "Source Project (or Source Bank) at your Studio project, build the banks " +
                    "in FMOD Studio, then use FMOD > Refresh Banks.";
            }

            if (EventManager.Events.Count == 0)
            {
                return
                    "FMOD's event cache is present but empty - the banks it was built from " +
                    "contain no events. Build your banks in FMOD Studio and refresh.";
            }

            return string.Empty;
        }

        /// <summary>
        /// FMOD supplies every field in the model. Bank-load configuration lives in the
        /// integration's settings rather than its cache, and is read in
        /// <see cref="FindReferences"/>.
        /// </summary>
        public BackendCapability Capabilities =>
            BackendCapability.EventLength |
            BackendCapability.StreamingFlag |
            BackendCapability.SpatialFlag |
            BackendCapability.Parameters |
            BackendCapability.BankMembership |
            BackendCapability.PlatformBanks |
            BackendCapability.BankLoadInfo |
            BackendCapability.GlobalParameters;

        public IReadOnlyList<EventDef> GetAuthoredEvents(ScanContext context)
        {
            var events = EventManager.Events;
            var result = new List<EventDef>(events.Count);

            for (var i = 0; i < events.Count; i++)
            {
                context.ThrowIfCancelled();

                var source = events[i];

                if (source == null)
                {
                    continue;
                }

                context.Progress.Report("Authored events", source.Path, (float)i / Math.Max(1, events.Count));

                result.Add(new EventDef
                {
                    Key = source.Path,
                    BackendId = source.Guid.ToString(),
                    Is3D = source.Is3D,
                    IsStreaming = source.IsStream,
                    // FMOD reports length in milliseconds; the model is in seconds.
                    LengthSeconds = source.Length / 1000f,
                    Parameters = (source.Parameters ?? new List<EditorParamRef>())
                        .Where(p => p != null)
                        .Select(p => p.Name)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToList(),
                    BankNames = (source.Banks ?? new List<EditorBankRef>())
                        .Where(b => b != null)
                        .Select(b => b.Name)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToList(),
                    Extras = BuildExtras(source),
                });
            }

            return result;
        }

        /// <summary>
        /// One <see cref="BankDef"/> per bank per platform.
        /// </summary>
        /// <remarks>
        /// FMOD records a bank's size once per platform it was built for, so the set of
        /// platforms a bank appears under *is* the record of which builds exist. R009
        /// reads nothing else: a platform missing from one bank's group but present in
        /// its siblings' is a bank that was never built for that target.
        /// </remarks>
        public IReadOnlyList<BankDef> GetBanks(ScanContext context)
        {
            // EditorBankRef does not list its events; the relation is stored the other
            // way round, on each event. Invert it once here.
            var eventsByBank = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var authored in EventManager.Events)
            {
                if (authored?.Banks == null)
                {
                    continue;
                }

                foreach (var bank in authored.Banks.Where(b => b != null))
                {
                    if (!eventsByBank.TryGetValue(bank.Name, out var keys))
                    {
                        keys = new List<string>();
                        eventsByBank[bank.Name] = keys;
                    }

                    keys.Add(authored.Path);
                }
            }

            var result = new List<BankDef>();

            foreach (var bank in EventManager.Banks.Concat(EventManager.MasterBanks).Where(b => b != null))
            {
                context.ThrowIfCancelled();

                var keys = eventsByBank.TryGetValue(bank.Name, out var found)
                    ? found.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList()
                    : new List<string>();

                var sizes = bank.FileSizes ?? new List<EditorBankRef.NameValuePair>();

                if (sizes.Count == 0)
                {
                    // A bank the integration knows about but has no size for was never
                    // built for any platform. Recording it with an explicit unknown
                    // platform keeps it visible instead of dropping it from the report.
                    result.Add(new BankDef
                    {
                        Name = bank.Name,
                        Platform = "(not built)",
                        SizeBytes = -1,
                        EventKeys = keys,
                    });

                    continue;
                }

                foreach (var size in sizes)
                {
                    result.Add(new BankDef
                    {
                        Name = bank.Name,
                        Platform = size.Name,
                        SizeBytes = size.Value,
                        EventKeys = keys,
                    });
                }
            }

            return result
                .GroupBy(b => b.Name + " " + b.Platform, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(b => b.Name, StringComparer.Ordinal)
                .ThenBy(b => b.Platform, StringComparer.Ordinal)
                .ToList();
        }

        public IReadOnlyList<string> GetGlobalParameters(ScanContext context) =>
            (EventManager.Parameters ?? new List<EditorParamRef>())
                .Where(p => p != null && p.IsGlobal)
                .Select(p => p.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        public void FindReferences(ScanContext context, ReferenceSink sink)
        {
            CollectSettingsBankLoads(sink);
            ProjectWalker.Walk(new FmodReferenceExtractor(), context, sink);
        }

        /// <summary>
        /// Records the banks the integration loads on its own, before anyone writes a
        /// line of loading code.
        /// </summary>
        /// <remarks>
        /// This is what stops R003 from firing on every bank of every scene. FMOD's
        /// default configuration loads all banks at startup, under which "this scene has
        /// no loading logic" is not a defect at all - it is the whole design. A rule that
        /// judged loading without first reading this setting would bury a real project in
        /// noise, which is the fastest way to get a validator switched off.
        ///
        /// The master and strings banks are always loaded by RuntimeManager regardless of
        /// the setting, so they are recorded unconditionally.
        /// </remarks>
        private static void CollectSettingsBankLoads(ReferenceSink sink)
        {
            var settings = Settings.Instance;

            void Record(string bankName, string why)
            {
                if (!string.IsNullOrEmpty(bankName))
                {
                    sink.Add(new BankLoadUsage
                    {
                        BankName = bankName,
                        AssetPath = string.Empty,
                        ObjectPath = why,
                        Source = BankLoadSource.SettingsAutoLoad,
                    });
                }
            }

            // EventManager exposes MasterBanks but not the strings banks, which sit in the
            // general Banks list distinguished only by their name suffix.
            var alwaysLoaded = EventManager.MasterBanks
                .Concat(EventManager.Banks.Where(
                    b => b != null && b.Name.EndsWith(".strings", StringComparison.Ordinal)))
                .Where(b => b != null);

            foreach (var master in alwaysLoaded)
            {
                Record(master.Name, "RuntimeManager always loads the master and strings banks.");
            }

            switch (settings.BankLoadType)
            {
                case BankLoadType.All:
                    foreach (var bank in EventManager.Banks.Where(b => b != null))
                    {
                        Record(bank.Name, "FMOD Settings has Load Banks set to All.");
                    }

                    break;

                case BankLoadType.Specified:
                    foreach (var name in settings.BanksToLoad ?? new List<string>())
                    {
                        // The setting stores bank paths; the model keys on the bare name.
                        Record(
                            System.IO.Path.GetFileNameWithoutExtension(name),
                            "Listed under Specified Banks in FMOD Settings.");
                    }

                    break;

                case BankLoadType.None:
                    sink.Note(
                        "FMOD Settings has Load Banks set to None, so every bank must be loaded " +
                        "explicitly by a StudioBankLoader or a RuntimeManager.LoadBank call.");
                    break;
            }
        }

        private static IReadOnlyDictionary<string, string> BuildExtras(EditorEventRef source)
        {
            var extras = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "IsOneShot", source.IsOneShot.ToString() },
                { "MinDistance", source.MinDistance.ToString("0.##") },
                { "MaxDistance", source.MaxDistance.ToString("0.##") },
            };

            var globals = source.GlobalParameters;

            if (globals != null && globals.Count > 0)
            {
                extras.Add("GlobalParameters", string.Join(", ", globals.Select(p => p.Name)));
            }

            return extras;
        }
    }
}
