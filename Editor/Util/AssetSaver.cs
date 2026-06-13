using System.IO;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Writes transient generated assets (animator controllers, clips) to a unique folder
    /// under <c>Assets/</c> so Unity can include them in the upload bundle, then cleans them
    /// up afterwards. Generated assets must live under Assets — Unity won't bundle objects
    /// that aren't persisted somewhere it can serialize from.
    /// </summary>
    internal sealed class AssetSaver
    {
        public string Folder { get; }

        private AssetSaver(string folder) { Folder = folder; }

        public static AssetSaver CreateTemp(string avatarName)
        {
            var root = CVRFurySettings.GeneratedFolder;
            EnsureFolder(root);
            var safe = MakeSafe(avatarName);
            var unique = AssetDatabase.GenerateUniqueAssetPath($"{root}/{safe}");
            Directory.CreateDirectory(unique);
            AssetDatabase.Refresh();
            return new AssetSaver(unique);
        }

        /// <summary>A persistent output folder for conversion results (kept, not wiped like build
        /// temp). Lives under "Assets/CVRFury Converted/&lt;avatar&gt;".</summary>
        public static AssetSaver CreatePersistent(string avatarName)
        {
            const string root = "Assets/CVRFury Converted";
            EnsureFolder(root);
            var unique = AssetDatabase.GenerateUniqueAssetPath($"{root}/{MakeSafe(avatarName)}");
            Directory.CreateDirectory(unique);
            AssetDatabase.Refresh();
            return new AssetSaver(unique);
        }

        /// <summary>Persist a generated object and return it. Safe to call on an object that is
        /// already an asset (no-op in that case).</summary>
        public T Save<T>(T obj, string name) where T : Object
        {
            if (obj == null) return null;
            if (AssetDatabase.Contains(obj)) return obj;
            var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{MakeSafe(name)}.asset");
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        /// <summary>Allocate a unique asset path inside the temp folder (without creating it).</summary>
        public string NewPath(string name, string ext = "asset") =>
            AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{MakeSafe(name)}.{ext}");

        /// <summary>Add a generated object as a sub-asset of <paramref name="container"/> (used for
        /// blend trees and other objects the AnimatorController API does not auto-parent).</summary>
        public void AddSubAsset(Object child, Object container)
        {
            if (child == null || container == null) return;
            if (AssetDatabase.Contains(child)) return;
            if (!AssetDatabase.Contains(container)) return;
            child.hideFlags |= HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(child, container);
        }

        public void Flush() => AssetDatabase.SaveAssets();

        public void Cleanup()
        {
            if (!string.IsNullOrEmpty(Folder) && AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, leaf);
        }

        private static string MakeSafe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrEmpty(name) ? "Asset" : name;
        }
    }
}
