using System;
using System.Collections.Generic;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// One built bank, for one platform.
    /// </summary>
    /// <remarks>
    /// A bank that was built for three platforms becomes three <see cref="BankDef"/>
    /// entries sharing a <see cref="Name"/>. R009 works entirely off that expansion:
    /// a platform missing from the group means the bank was not built for it.
    /// </remarks>
    [Serializable]
    public sealed class BankDef
    {
        /// <summary>Bank name without extension or platform folder, e.g. "Music".</summary>
        public string Name;

        /// <summary>Platform this build of the bank targets, e.g. "Desktop", "iOS".</summary>
        public string Platform;

        /// <summary>Size on disk in bytes; -1 when the backend cannot report it.</summary>
        public long SizeBytes = -1;

        /// <summary>Keys of the events packed into this bank.</summary>
        public IReadOnlyList<string> EventKeys = Array.Empty<string>();

        public override string ToString() => $"{Name} [{Platform}]";
    }
}
