using System;
using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;

namespace AudioToolbox.AudioDoctor.Backends.Wwise
{
    /// <summary>
    /// Placeholder for the Wwise backend, which is not implemented in v0.1.
    /// </summary>
    /// <remarks>
    /// This exists rather than being absent for two reasons. An assembly definition
    /// with no scripts in it makes Unity warn on every import for anyone who actually
    /// has Wwise installed — precisely the audience least deserving of noise. And a
    /// Wwise user whose project AudioDoctor silently ignores has no way to tell whether
    /// the tool is broken, misconfigured, or simply does not support them yet; saying so
    /// out loud costs forty lines and answers the question before it is asked.
    ///
    /// It declares no capabilities, so every rule is reported as skipped rather than
    /// passing. Nothing here pretends to have checked anything.
    /// </remarks>
    public sealed class WwiseProjectSource : IAudioProjectSource
    {
        public string BackendId => "wwise";

        public string DisplayName => "Wwise";

        /// <summary>Below Native, so an unimplemented backend never wins the auto-pick.</summary>
        public int Priority => -1;

        public bool IsAvailable => false;

        public string GetUnavailableReason() =>
            "Wwise is installed, but AudioDoctor's Wwise backend is not implemented yet — it is " +
            "planned for v0.2, reading SoundBanksInfo.xml and WwiseProjectData. Until then this " +
            "project is not being validated against Wwise at all. See the support matrix in the " +
            "README for what each backend covers.";

        public BackendCapability Capabilities => BackendCapability.None;

        public IReadOnlyList<EventDef> GetAuthoredEvents(ScanContext context) => Array.Empty<EventDef>();

        public IReadOnlyList<BankDef> GetBanks(ScanContext context) => Array.Empty<BankDef>();

        public IReadOnlyList<string> GetGlobalParameters(ScanContext context) => Array.Empty<string>();

        public void FindReferences(ScanContext context, ReferenceSink sink) { }
    }
}
