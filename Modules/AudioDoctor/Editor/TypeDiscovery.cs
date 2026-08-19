using System;
using System.Linq;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Decides whether a reflectively-discovered type belongs in a real run.
    /// </summary>
    /// <remarks>
    /// Test assemblies are compiled into the editor, so a plain
    /// TypeCache.GetTypesDerivedFrom sweep happily picks up the fake rules and stub
    /// backends written for the unit tests and reports their findings to the user —
    /// which is precisely what happened the first time this pipeline ran end to end.
    /// Two filters keep them out: the type must be public and top-level (test doubles
    /// are private nested classes by convention), and its assembly must not reference
    /// NUnit.
    /// </remarks>
    public static class TypeDiscovery
    {
        public static bool IsProductionType(Type type)
        {
            if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                return false;
            }

            // Test doubles live as private nested classes inside their fixture.
            if (type.IsNested || !type.IsPublic)
            {
                return false;
            }

            return !IsTestAssembly(type);
        }

        /// <summary>Instantiates the type, or returns null and warns if it cannot.</summary>
        public static T TryCreate<T>(Type type, string kind) where T : class
        {
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                Debug.LogWarning(
                    $"[AudioDoctor] {kind} {type.FullName} has no parameterless constructor and was skipped.");
                return null;
            }

            try
            {
                return (T)Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioDoctor] {kind} {type.FullName} failed to instantiate: {e.Message}");
                return null;
            }
        }

        private static bool IsTestAssembly(Type type)
        {
            try
            {
                return type.Assembly
                    .GetReferencedAssemblies()
                    .Any(a => a.Name.StartsWith("nunit.framework", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}
