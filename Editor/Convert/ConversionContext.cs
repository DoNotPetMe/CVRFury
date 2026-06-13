using System.Collections.Generic;
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

        public ConversionContext(GameObject root, ConversionOptions options, AssetSaver assets)
        {
            AvatarRoot = root;
            Options = options;
            Assets = assets;
        }

        public Transform RootTransform => AvatarRoot.transform;

        /// <summary>Lazily create the CVR animator controller as a persistent asset (so generated
        /// state-machine sub-assets attach correctly), used when merging playable layers.</summary>
        public AnimatorController GetOrCreateController()
        {
            if (Controller != null) return Controller;
            var path = Assets.NewPath(AvatarRoot.name + " CVR Controller", "controller");
            Controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            return Controller;
        }
    }
}
