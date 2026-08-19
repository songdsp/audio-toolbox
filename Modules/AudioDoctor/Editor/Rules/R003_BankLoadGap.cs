using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// A bank the game asks for that nothing ever loads.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than "this scene has no loading logic". A bank loader
    /// living on a prefab that the scene instantiates at runtime is invisible to a
    /// static scan, so per-scene judgement would report working projects as broken -
    /// and acceptance criterion 2 is zero false positives, not maximum coverage.
    ///
    /// What is reported instead is unambiguous: the bank is referenced somewhere, and
    /// across the entire project there is no loader component, no LoadBank call, and no
    /// setting that loads it. Those events cannot play, anywhere, ever.
    /// </remarks>
    public sealed class R003_BankLoadGap : IValidationRule
    {
        public string RuleId => "R003";

        public string Title => "Bank is never loaded";

        public Severity DefaultSeverity => Severity.Error;

        public BackendCapability RequiredCapabilities =>
            BackendCapability.BankMembership | BackendCapability.BankLoadInfo;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            var loaded = new HashSet<string>(
                context.Snapshot.BankLoads
                    .Select(b => b.BankName)
                    .Where(n => !string.IsNullOrEmpty(n)),
                StringComparer.Ordinal);

            // Which banks does the project actually need, and where was each first asked for?
            var neededBanks = new Dictionary<string, EventRefUsage>(StringComparer.Ordinal);
            var eventsPerBank = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var usage in context.References)
            {
                if (string.IsNullOrEmpty(usage.EventKey) ||
                    !context.EventsByKey.TryGetValue(usage.EventKey, out var authored))
                {
                    continue;
                }

                foreach (var bank in authored.BankNames)
                {
                    if (!neededBanks.ContainsKey(bank))
                    {
                        neededBanks[bank] = usage;
                        eventsPerBank[bank] = new List<string>();
                    }

                    if (!eventsPerBank[bank].Contains(authored.Key, EventKeyComparer.Exact))
                    {
                        eventsPerBank[bank].Add(authored.Key);
                    }
                }
            }

            foreach (var pair in neededBanks.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (loaded.Contains(pair.Key))
                {
                    continue;
                }

                var events = eventsPerBank[pair.Key];
                var sample = string.Join(", ", events.Take(3));
                var more = events.Count > 3 ? $" and {events.Count - 3} more" : string.Empty;

                yield return context.Issue(
                    this,
                    $"Bank '{pair.Key}' is used by the game but nothing ever loads it",
                    pair.Value.AssetPath,
                    $"{events.Count} event(s) live in this bank — {sample}{more} — and the project " +
                    "references them, but no StudioBankLoader, no LoadBank call and no setting " +
                    "loads the bank. Those events will fail to play with no error at runtime. " +
                    "Add the bank to the middleware's load list, or place a bank loader in the " +
                    "scene that needs it.",
                    pair.Value.Line);
            }
        }
    }
}
