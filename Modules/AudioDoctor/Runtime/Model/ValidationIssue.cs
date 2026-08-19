using System;
using System.Collections.Generic;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>One finding. Must be locatable, explicable and fixable.</summary>
    [Serializable]
    public sealed class ValidationIssue
    {
        /// <summary>Rule that produced this, e.g. "R001".</summary>
        public string RuleId;

        public Severity Severity;

        /// <summary>One line, written for an audio designer rather than a programmer.</summary>
        public string Message;

        /// <summary>Asset a double-click should select and ping.</summary>
        public string PrimaryAssetPath;

        /// <summary>1-based line in <see cref="PrimaryAssetPath"/>; 0 when not a text file.</summary>
        public int Line;

        /// <summary>Further assets involved — R009 points at several banks at once.</summary>
        public IReadOnlyList<string> SecondaryAssetPaths = Array.Empty<string>();

        /// <summary>The evidence: what was found, what was expected, how to fix it.</summary>
        public string Detail;

        /// <summary>
        /// Stable identity for set-equality assertions in the fixture tests.
        /// Deliberately excludes <see cref="Detail"/>, which is prose and may be reworded.
        /// </summary>
        public string Signature =>
            $"{RuleId}|{Severity}|{PrimaryAssetPath}|{Line}|{Message}";

        public override string ToString() => $"[{RuleId}/{Severity}] {Message} ({PrimaryAssetPath})";
    }
}
