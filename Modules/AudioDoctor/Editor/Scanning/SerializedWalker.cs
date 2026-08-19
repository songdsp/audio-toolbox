using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Visits every serialized property on an object, including nested structs and
    /// array elements.
    /// </summary>
    /// <remarks>
    /// Going through SerializedObject rather than reflection is what lets one scanner
    /// find event references on components it has never heard of — a designer's own
    /// MonoBehaviour with an EventReference field is found for the same reason the
    /// middleware's own emitter component is.
    /// </remarks>
    public static class SerializedWalker
    {
        /// <summary>
        /// Calls <paramref name="visit"/> for each property. Return true from the
        /// visitor to stop descending into that property's children — useful when a
        /// struct has been recognised and its internals are not interesting.
        /// </summary>
        public static void Visit(Object target, Func<SerializedProperty, bool> visit)
        {
            if (target == null || visit == null)
            {
                return;
            }

            using (var serialized = new SerializedObject(target))
            {
                var property = serialized.GetIterator();

                // enterChildren: true on the first call steps into the object itself.
                var enterChildren = true;

                while (property.NextVisible(enterChildren))
                {
                    // m_Script is the MonoBehaviour's own script reference; never a payload.
                    if (property.propertyPath == "m_Script")
                    {
                        enterChildren = false;
                        continue;
                    }

                    var handled = visit(property);
                    enterChildren = !handled && property.hasVisibleChildren;
                }
            }
        }

        /// <summary>Walks a GameObject and every component on it and its descendants.</summary>
        public static void VisitComponents(GameObject root, Action<Component> onComponent)
        {
            if (root == null || onComponent == null)
            {
                return;
            }

            foreach (var component in root.GetComponentsInChildren<Component>(includeInactive: true))
            {
                // A missing script serializes as a null component; skipping it here is
                // not our problem to report, but dereferencing it would throw.
                if (component != null)
                {
                    onComponent(component);
                }
            }
        }
    }
}
