using System;
using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>Per-rule switch and severity override.</summary>
    [Serializable]
    public sealed class RuleSetting
    {
        public string RuleId;

        public bool Enabled = true;

        [Tooltip("When off, the rule reports at its own default severity.")]
        public bool OverrideSeverity;

        public Severity Severity = Severity.Warning;
    }

    /// <summary>
    /// The tunable half of the rule engine, kept in an asset so that a team can
    /// version its own audio conventions alongside the project rather than
    /// recompiling the tool to change a threshold.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioDoctorRules", menuName = "Audio Toolbox/AudioDoctor/Rule Set", order = 300)]
    public sealed class RuleSetAsset : ScriptableObject
    {
        [Tooltip("Rules absent from this list run enabled at their default severity.")]
        public List<RuleSetting> Rules = new List<RuleSetting>();

        [Header("R005 — loading strategy")]
        [Tooltip("Events longer than this that are not streamed are reported.")]
        public float LongEventSeconds = 15f;

        [Tooltip("Events shorter than this that are streamed are reported.")]
        public float ShortEventSeconds = 2f;

        [Header("R008 — naming convention")]
        [Tooltip("Event keys must match this regex. Empty disables the check.")]
        public string EventNamingPattern = @"^event:/[A-Za-z0-9_]+(/[A-Za-z0-9_]+)*$";

        [Header("R009 — cross-platform banks")]
        [Tooltip("Platforms every bank is expected to be built for. Empty means 'whatever platforms were found'.")]
        public List<string> RequiredPlatforms = new List<string>();

        [Tooltip("A bank whose size deviates from its cross-platform median by more than this " +
                 "ratio is reported. 0 disables the check, which is the default: platforms " +
                 "legitimately use different encodings, so size differences are usually correct " +
                 "configuration rather than a defect.")]
        public float BankSizeDeviationRatio;

        public RuleSetting Find(string ruleId)
        {
            for (var i = 0; i < Rules.Count; i++)
            {
                if (string.Equals(Rules[i].RuleId, ruleId, StringComparison.Ordinal))
                {
                    return Rules[i];
                }
            }

            return null;
        }

        public bool IsEnabled(string ruleId) => Find(ruleId)?.Enabled ?? true;

        public Severity ResolveSeverity(string ruleId, Severity defaultSeverity)
        {
            var setting = Find(ruleId);
            return setting != null && setting.OverrideSeverity ? setting.Severity : defaultSeverity;
        }

        /// <summary>The settings used when no asset was authored.</summary>
        public static RuleSetAsset CreateDefault()
        {
            var asset = CreateInstance<RuleSetAsset>();
            asset.name = "AudioDoctorRules (defaults)";
            return asset;
        }
    }
}
