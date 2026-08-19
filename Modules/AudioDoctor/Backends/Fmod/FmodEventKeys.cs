using FMODUnity;
using UnityEditor;

namespace AudioToolbox.AudioDoctor.Backends.Fmod
{
    /// <summary>
    /// Turns whatever a reference site happens to store into the canonical event path.
    /// </summary>
    /// <remarks>
    /// An EventReference can carry a path, a GUID, or both, depending on the project's
    /// <c>Serialize GUIDs Only</c> setting and how old the asset is. A validator that
    /// only understood one of those would report perfectly good references as dangling.
    /// </remarks>
    internal static class FmodEventKeys
    {
        /// <summary>
        /// Resolves to the authored path when the event exists. Falls back to whatever
        /// the site stored, so a genuinely dangling reference still reaches R001 with
        /// something a human can search for.
        /// </summary>
        public static string Resolve(string storedPath, FMOD.GUID guid)
        {
            if (!guid.IsNull)
            {
                var byGuid = EventManager.EventFromGUID(guid);

                if (byGuid != null)
                {
                    return byGuid.Path;
                }
            }

            if (!string.IsNullOrEmpty(storedPath))
            {
                var byPath = EventManager.EventFromPath(storedPath);
                return byPath != null ? byPath.Path : storedPath;
            }

            return guid.IsNull ? null : guid.ToString();
        }

        public static string Resolve(EventReference reference) => Resolve(reference.Path, reference.Guid);

        /// <summary>Reads an EventReference out of a serialized property of unknown origin.</summary>
        public static string Resolve(SerializedProperty eventReferenceProperty)
        {
            var path = eventReferenceProperty.FindPropertyRelative("Path")?.stringValue;

            var guid = new FMOD.GUID
            {
                Data1 = eventReferenceProperty.FindPropertyRelative("Guid.Data1")?.intValue ?? 0,
                Data2 = eventReferenceProperty.FindPropertyRelative("Guid.Data2")?.intValue ?? 0,
                Data3 = eventReferenceProperty.FindPropertyRelative("Guid.Data3")?.intValue ?? 0,
                Data4 = eventReferenceProperty.FindPropertyRelative("Guid.Data4")?.intValue ?? 0,
            };

            return Resolve(path, guid);
        }
    }
}
