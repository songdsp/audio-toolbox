using System;
using System.Collections.Generic;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// Everything one scan collected, normalized. This is the only thing the rule
    /// engine ever sees — which is why rules can be unit-tested on a machine with
    /// no middleware installed at all.
    /// </summary>
    public sealed class AudioProjectSnapshot
    {
        public string BackendId = string.Empty;

        /// <summary>What the middleware project declares.</summary>
        public IReadOnlyList<EventDef> Events = Array.Empty<EventDef>();

        /// <summary>What the built banks contain, one entry per bank per platform.</summary>
        public IReadOnlyList<BankDef> Banks = Array.Empty<BankDef>();

        /// <summary>What the Unity project references.</summary>
        public IReadOnlyList<EventRefUsage> References = Array.Empty<EventRefUsage>();

        public IReadOnlyList<ParameterUsage> ParameterUsages = Array.Empty<ParameterUsage>();

        public IReadOnlyList<BankLoadUsage> BankLoads = Array.Empty<BankLoadUsage>();

        /// <summary>Parameters that exist at system scope and belong to no single event.</summary>
        public IReadOnlyList<string> GlobalParameters = Array.Empty<string>();

        /// <summary>Scene asset paths considered by the scan; R003 iterates these.</summary>
        public IReadOnlyList<string> ScenePaths = Array.Empty<string>();

        public BackendCapability Capabilities = BackendCapability.None;

        public IReadOnlyList<ScanNote> Notes = Array.Empty<ScanNote>();

        public bool Supports(BackendCapability capability) =>
            (Capabilities & capability) == capability;
    }
}
