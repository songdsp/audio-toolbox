using System;
using System.Collections.Generic;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// The single place event keys are compared.
    /// </summary>
    /// <remarks>
    /// Matching is ordinal and case-sensitive on purpose. Folding case here would
    /// make "event:/UI/Click" and "event:/ui/click" look identical and quietly
    /// paper over exactly the bug R009 exists to catch — macOS and Windows disagree
    /// about filesystem case sensitivity, so a case mismatch that works on one
    /// machine breaks the build on the other. Use <see cref="CaseInsensitive"/>
    /// only where the intent is to find that near-miss and report it.
    /// </remarks>
    public static class EventKeyComparer
    {
        public static readonly StringComparer Exact = StringComparer.Ordinal;

        public static readonly StringComparer CaseInsensitive = StringComparer.OrdinalIgnoreCase;

        public static bool Equals(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        /// <summary>True when the two keys differ only by letter case.</summary>
        public static bool DiffersOnlyByCase(string a, string b) =>
            !string.Equals(a, b, StringComparison.Ordinal) &&
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static HashSet<string> NewExactSet() => new HashSet<string>(Exact);

        public static Dictionary<string, T> NewExactMap<T>() => new Dictionary<string, T>(Exact);
    }
}
