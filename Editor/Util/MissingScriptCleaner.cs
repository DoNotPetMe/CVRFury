using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Removes MonoBehaviour components whose backing script can't be loaded ("missing scripts").
    /// This is the bread-and-butter cleanup when importing a VRChat avatar into a ChilloutVR
    /// project: every VRChat-only component (VRCAvatarDescriptor, PhysBone, contacts, VRCFury, …)
    /// has no script here, so Unity shows it as a broken component. They're inert, block clean
    /// prefab edits, and must never ship — so CVRFury strips them.
    /// </summary>
    internal static class MissingScriptCleaner
    {
        /// <summary>Count missing-script components across the whole hierarchy.</summary>
        public static int CountInHierarchy(GameObject root)
        {
            if (root == null) return 0;
            var total = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                total += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            return total;
        }

        /// <summary>Remove every missing-script component in the hierarchy. Returns how many were
        /// removed. Operates directly on the given objects (use on a build clone or after recording
        /// undo / inside loaded prefab contents).</summary>
        public static int RemoveInHierarchy(GameObject root)
        {
            if (root == null) return 0;
            var removed = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            return removed;
        }
    }
}
