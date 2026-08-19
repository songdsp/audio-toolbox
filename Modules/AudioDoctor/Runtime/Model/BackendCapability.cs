using System;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// What a backend can actually tell us.
    /// </summary>
    /// <remarks>
    /// A rule that needs data the backend does not provide must skip, not guess.
    /// Silently treating "unknown" as "false" is how a validator earns a reputation
    /// for noise; the support matrix in the README is generated from these flags,
    /// so an unset flag shows up as an honest blank rather than a false promise.
    /// </remarks>
    [Flags]
    public enum BackendCapability
    {
        None = 0,
        EventLength = 1 << 0,
        StreamingFlag = 1 << 1,
        SpatialFlag = 1 << 2,
        Parameters = 1 << 3,
        BankMembership = 1 << 4,
        PlatformBanks = 1 << 5,
        BankLoadInfo = 1 << 6,
        GlobalParameters = 1 << 7,

        /// <summary>
        /// The backend can see events that belong to no bank at all.
        /// </summary>
        /// <remarks>
        /// Not a detail: FMOD's Unity integration builds its event list by loading each
        /// built bank and enumerating its contents, so an event assigned to no bank is
        /// invisible to Unity entirely - which is precisely the event R002 exists to
        /// find. Without this flag R002 would run, find nothing, and report a clean
        /// result for a check it was structurally incapable of performing.
        /// </remarks>
        UnpackedEvents = 1 << 8,
    }
}
