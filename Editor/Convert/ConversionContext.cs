using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>Shared state for a single VRChat→ChilloutVR conversion run.</summary>
    internal sealed class ConversionContext
    {
        public readonly GameObject AvatarRoot;
        public readonly ConversionOptions Options;
        public readonly BuildLog Log = new BuildLog();
        public readonly AssetSaver Assets;

        /// <summary>The VRChat avatar descriptor instance (reflected), if present.</summary>
        public object VrcDescriptor;

        /// <summary>The CVR avatar wrapper (created/ensured during conversion).</summary>
        public CckAvatar Cvr;

        /// <summary>Working CVR animator controller, lazily created when a step needs one.</summary>
        public AnimatorController Controller;

        /// <summary>Maps a converted VRChat PhysBone collider to its new DynamicBoneCollider, so
        /// bones can re-reference the right collider in a second pass.</summary>
        public readonly Dictionary<Object, Object> ColliderMap = new Dictionary<Object, Object>();

        /// <summary>Machine names already turned into an AAS entry, so the same VRChat parameter
        /// referenced by several menu controls only produces one synced setting.</summary>
        public readonly HashSet<string> AddedParams = new HashSet<string>();

        /// <summary>VRChat expression-parameter name → whether it is network-synced. Parameters not
        /// in this map (or not synced) are made local in CVR, costing zero synced bits.</summary>
        public readonly Dictionary<string, bool> ParamSynced = new Dictionary<string, bool>();

        public ConversionContext(GameObject root, ConversionOptions options, AssetSaver assets)
        {
            AvatarRoot = root;
            Options = options;
            Assets = assets;
        }

        public Transform RootTransform => AvatarRoot.transform;

        /// <summary>Lazily create the CVR animator controller as a persistent asset (so generated
        /// state-machine sub-assets attach correctly). It is seeded from ChilloutVR's default avatar
        /// animator so locomotion / idle are preserved — otherwise the avatar holds a default pose
        /// (the "motorcycle pose"). Merged playable layers are then added on top.</summary>
        public AnimatorController GetOrCreateController()
        {
            if (Controller != null) return Controller;
            var path = Assets.NewPath(AvatarRoot.name + " CVR Controller", "controller");
            Controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            var baseAnim = FindCvrDefaultAvatarAnimator();
            if (baseAnim != null)
            {
                ControllerMerger.Merge(Controller, baseAnim, Assets, "", Log);
                Log.Info($"Seeded CVR animator from default avatar animator '{baseAnim.name}' " +
                         "(locomotion preserved).");
            }
            else
            {
                Log.Warning("Couldn't find ChilloutVR's default avatar animator under ABI.CCK; the " +
                            "merged controller has no locomotion, so the avatar may hold a default pose. " +
                            "Set the Base Controller to ABI.CCK/Animations' avatar animator manually.");
            }
            return Controller;
        }

        /// <summary>CVR's default avatar (locomotion) animator, used as the clean base the AAS
        /// generator extends. Exposed so the controller-generation step can seed from it.</summary>
        public AnimatorController FindCvrLocomotion() => FindCvrDefaultAvatarAnimator();

        /// <summary>Best-effort locate of CVR's default avatar animator controller. The CCK ships it
        /// as <c>…/CCK/Animations/AvatarAnimator.controller</c>, but the install folder varies
        /// (<c>ABI.CCK</c> on older kits, <c>CVR.CCK</c> on newer ones), so match on ".CCK" + the
        /// Animations folder + an avatar-animator-looking file name.</summary>
        private static AnimatorController FindCvrDefaultAvatarAnimator()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var lower = p.ToLowerInvariant();
                if (lower.Contains(".cck") && lower.Contains("/animations/") &&
                    lower.Contains("avatar") && lower.Contains("animator"))
                    return AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
            }
            // Fallback: any AvatarAnimator.controller shipped under a CCK folder.
            foreach (var guid in AssetDatabase.FindAssets("AvatarAnimator t:AnimatorController"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.ToLowerInvariant().Contains(".cck"))
                    return AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
            }
            return null;
        }
    }
}
