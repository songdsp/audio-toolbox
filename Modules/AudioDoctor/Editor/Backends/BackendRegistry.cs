using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Finds the available <see cref="IAudioProjectSource"/> implementations.
    /// </summary>
    /// <remarks>
    /// Discovery is reflective on purpose. A hard-coded registry would force this
    /// assembly to reference every backend assembly, which would drag FMOD and
    /// Wwise into the test assembly's dependency graph and break the one constraint
    /// that matters: the rule engine must compile and its tests must pass on a
    /// machine with no middleware installed.
    /// </remarks>
    public static class BackendRegistry
    {
        /// <summary>Every backend whose assembly compiled, available or not.</summary>
        public static IReadOnlyList<IAudioProjectSource> All()
        {
            var found = new List<IAudioProjectSource>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<IAudioProjectSource>())
            {
                if (!TypeDiscovery.IsProductionType(type))
                {
                    continue;
                }

                var backend = TypeDiscovery.TryCreate<IAudioProjectSource>(type, "Backend");

                if (backend != null)
                {
                    found.Add(backend);
                }
            }

            return found
                .OrderByDescending(b => b.Priority)
                .ThenBy(b => b.BackendId, StringComparer.Ordinal)
                .ToList();
        }

        public static IReadOnlyList<IAudioProjectSource> Available() =>
            All().Where(IsUsable).ToList();

        /// <summary>
        /// Picks the backend to scan with. Falls back to the highest-priority
        /// compiled backend so that an unusable-but-installed middleware reports
        /// its own reason instead of silently degrading to the Native fallback.
        /// </summary>
        public static IAudioProjectSource Resolve(string requestedBackendId)
        {
            var all = All();

            if (!string.IsNullOrEmpty(requestedBackendId))
            {
                var match = all.FirstOrDefault(
                    b => string.Equals(b.BackendId, requestedBackendId, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    var known = all.Count == 0 ? "<none>" : string.Join(", ", all.Select(b => b.BackendId));
                    throw new ArgumentException(
                        $"Unknown backend '{requestedBackendId}'. Compiled backends: {known}.");
                }

                return match;
            }

            return all.FirstOrDefault(IsUsable) ?? all.FirstOrDefault();
        }

        private static bool IsUsable(IAudioProjectSource backend)
        {
            try
            {
                return backend.IsAvailable;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[AudioDoctor] Backend '{backend.BackendId}' threw while reporting availability: {e.Message}");
                return false;
            }
        }
    }
}
