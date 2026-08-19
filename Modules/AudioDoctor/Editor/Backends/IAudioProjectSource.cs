using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// One middleware's view of the project. The seam that keeps the rule engine
    /// free of any middleware dependency.
    /// </summary>
    /// <remarks>
    /// Implementations live in their own assemblies behind define constraints, and
    /// are discovered reflectively by <see cref="BackendRegistry"/> — this assembly
    /// must never reference a backend, or the tests would stop compiling on a
    /// machine with no middleware installed.
    /// </remarks>
    public interface IAudioProjectSource
    {
        /// <summary>Stable id used by the CLI's --backend flag: "native", "fmod", "wwise".</summary>
        string BackendId { get; }

        /// <summary>Name shown in the UI.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Higher wins when several backends are available and none was requested.
        /// The Native fallback sits at 0 so any real middleware outranks it.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// False when the integration is present but unusable — an empty event
        /// cache, no source project configured. Report why via
        /// <see cref="GetUnavailableReason"/> rather than throwing.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Human-readable explanation when <see cref="IsAvailable"/> is false.</summary>
        string GetUnavailableReason();

        /// <summary>Which optional data this backend can actually supply.</summary>
        BackendCapability Capabilities { get; }

        IReadOnlyList<EventDef> GetAuthoredEvents(ScanContext context);

        /// <summary>One entry per bank per platform. See <see cref="BankDef"/>.</summary>
        IReadOnlyList<BankDef> GetBanks(ScanContext context);

        /// <summary>Parameters at system scope, owned by no single event.</summary>
        IReadOnlyList<string> GetGlobalParameters(ScanContext context);

        /// <summary>Walks the Unity project and pushes every usage into <paramref name="sink"/>.</summary>
        void FindReferences(ScanContext context, ReferenceSink sink);
    }
}
