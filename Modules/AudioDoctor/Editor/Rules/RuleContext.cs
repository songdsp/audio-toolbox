using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// The snapshot plus the lookups every rule would otherwise rebuild.
    /// </summary>
    /// <remarks>
    /// Building these once instead of once per rule is what keeps a nine-rule pass
    /// over a mid-size project inside the 30-second budget: without it, R001, R002
    /// and R004 alone would each walk the full reference list.
    /// </remarks>
    public sealed class RuleContext
    {
        public RuleContext(AudioProjectSnapshot snapshot, RuleSetAsset settings)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Settings = settings ?? RuleSetAsset.CreateDefault();

            EventsByKey = BuildEventIndex(snapshot.Events);

            ReferencesByKey = snapshot.References
                .Where(r => !string.IsNullOrEmpty(r.EventKey))
                .ToLookup(r => r.EventKey, EventKeyComparer.Exact);

            BanksByName = snapshot.Banks
                .Where(b => !string.IsNullOrEmpty(b.Name))
                .ToLookup(b => b.Name, StringComparer.Ordinal);

            PackedEventKeys = new HashSet<string>(
                snapshot.Banks.SelectMany(b => b.EventKeys ?? Array.Empty<string>()),
                EventKeyComparer.Exact);

            GlobalParameters = new HashSet<string>(snapshot.GlobalParameters, StringComparer.Ordinal);
        }

        public AudioProjectSnapshot Snapshot { get; }

        public RuleSetAsset Settings { get; }

        /// <summary>Authored events by key. Duplicates keep the first and are reported by R008.</summary>
        public IReadOnlyDictionary<string, EventDef> EventsByKey { get; }

        public ILookup<string, EventRefUsage> ReferencesByKey { get; }

        public ILookup<string, BankDef> BanksByName { get; }

        /// <summary>Keys of every event that lives in at least one bank.</summary>
        public HashSet<string> PackedEventKeys { get; }

        public HashSet<string> GlobalParameters { get; }

        public IReadOnlyList<EventDef> Events => Snapshot.Events;

        public IReadOnlyList<BankDef> Banks => Snapshot.Banks;

        public IReadOnlyList<EventRefUsage> References => Snapshot.References;

        public bool Supports(BackendCapability capability) => Snapshot.Supports(capability);

        /// <summary>Creates an issue with this rule's resolved severity already applied.</summary>
        public ValidationIssue Issue(
            IValidationRule rule,
            string message,
            string primaryAssetPath,
            string detail,
            int line = 0,
            IReadOnlyList<string> secondaryAssetPaths = null) =>
            new ValidationIssue
            {
                RuleId = rule.RuleId,
                Severity = Settings.ResolveSeverity(rule.RuleId, rule.DefaultSeverity),
                Message = message,
                PrimaryAssetPath = primaryAssetPath ?? string.Empty,
                Line = line,
                Detail = detail ?? string.Empty,
                SecondaryAssetPaths = secondaryAssetPaths ?? Array.Empty<string>(),
            };

        private static IReadOnlyDictionary<string, EventDef> BuildEventIndex(IReadOnlyList<EventDef> events)
        {
            var index = EventKeyComparer.NewExactMap<EventDef>();

            foreach (var e in events)
            {
                if (!string.IsNullOrEmpty(e.Key) && !index.ContainsKey(e.Key))
                {
                    index.Add(e.Key, e);
                }
            }

            return index;
        }
    }
}
