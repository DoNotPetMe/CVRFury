using System.Collections.Generic;
using CVRFury.Components;
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

        private readonly ParamNameAllocator _params = new ParamNameAllocator();

        // Which synced parameter each Toggle/Modes/Slider feature ended up with, keyed by the
        // component instance. Lets later-running features (e.g. Blendshape Rules, priority 60)
        // find "what parameter drives this GameObject" without re-deriving/guessing the name —
        // there is no other reliable way to recover an allocated name after the fact.
        private readonly Dictionary<CVRFuryComponent, string> _recordedParams =
            new Dictionary<CVRFuryComponent, string>();

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
        public string AllocateParam(string desired) => _params.Allocate(desired);

        /// <summary>Record the parameter a feature ended up with, so a later-running feature
        /// (e.g. Blendshape Rules) can look up "what drives this component" via
        /// <see cref="TryGetRecordedParam"/> instead of re-deriving/guessing the name.</summary>
        public void RecordParam(CVRFuryComponent feature, string param)
        {
            if (feature != null && !string.IsNullOrEmpty(param)) _recordedParams[feature] = param;
        }

        public bool TryGetRecordedParam(CVRFuryComponent feature, out string param) =>
            _recordedParams.TryGetValue(feature, out param);
    }
}
