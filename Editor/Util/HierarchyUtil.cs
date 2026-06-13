using System.Text;
using UnityEngine;

namespace CVRFury.Builder
{
    internal static class HierarchyUtil
    {
        /// <summary>Animation-binding path of <paramref name="target"/> relative to
        /// <paramref name="root"/> ("" if it is the root). Returns null if not a descendant.</summary>
        public static string GetPath(Transform root, Transform target)
        {
            if (target == null || root == null) return null;
            if (target == root) return "";

            var sb = new StringBuilder();
            var t = target;
            while (t != null && t != root)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, t.name);
                t = t.parent;
            }
            // Walked past the root without finding it → not a descendant.
            return t == root ? sb.ToString() : null;
        }
    }
}
