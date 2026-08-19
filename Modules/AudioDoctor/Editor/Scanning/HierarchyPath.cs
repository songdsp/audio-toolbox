using System.Text;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>Builds the "Root/Child/Grandchild" paths that make an issue locatable.</summary>
    public static class HierarchyPath
    {
        public static string Of(Component component) =>
            component == null ? string.Empty : Of(component.transform);

        public static string Of(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(transform.name);

            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                builder.Insert(0, '/').Insert(0, parent.name);
            }

            return builder.ToString();
        }
    }
}
