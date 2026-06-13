using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    internal enum BuildTrigger { CckUpload, ManualBake }

    /// <summary>
    /// Shared state passed to every feature builder during a single avatar bake. Owns the
    /// working animator controller (a clone — source assets are never touched), the temp
    /// asset folder, and a unique-name allocator for synced parameters.
    /// </summary>
    internal sealed class BuildContext
    {
        public readonly GameObject AvatarRoot;
        public readonly CckAvatar Avatar;
        public readonly AssetSaver Assets;
        public readonly BuildTrigger Trigger;
        public readonly BuildLog Log = new BuildLog();

        public AnimatorController Controller { get; private set; }

        private readonly HashSet<string> _usedParamNames = new HashSet<string>();
        private int _counter;

        public BuildContext(GameObject root, CckAvatar avatar, AssetSaver assets, BuildTrigger trigger)
        {
            AvatarRoot = root;
            Avatar = avatar;
            Assets = assets;
            Trigger = trigger;
        }

        public Transform RootTransform => AvatarRoot.transform;

        /// <summary>Lazily create the working controller as a fresh, on-disk asset, then merge in
        /// the avatar's existing base controller. Creating the asset first is essential: the
        /// AnimatorController API only attaches state-machine/state/transition sub-assets when it
        /// edits a persisted controller. Source assets are never modified — we always build a clone.</summary>
        public AnimatorController GetOrCreateController()
        {
            if (Controller != null) return Controller;

            var path = Assets.NewPath(AvatarRoot.name + " CVRFury Controller", "controller");
            Controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            var existing = Avatar?.BaseController;
            if (existing != null)
                ControllerMerger.Merge(Controller, existing, Assets, "", Log);

            return Controller;
        }

        /// <summary>Reserve a unique, sanitised synced-parameter machine name. If
        /// <paramref name="desired"/> is blank, one is generated.</summary>
        public string AllocateParam(string desired)
        {
            var baseName = Sanitize(desired);
            if (string.IsNullOrEmpty(baseName))
                baseName = $"CVRFury_{_counter++}";

            var name = baseName;
            var i = 1;
            while (!_usedParamNames.Add(name))
                name = $"{baseName}_{i++}";
            return name;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    chars[i] = '_';
            return new string(chars);
        }
    }
}
