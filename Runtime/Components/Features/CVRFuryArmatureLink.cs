using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Attaches a prop's armature to the avatar's skeleton, VRCFury-style. Drop this on
    /// the root of an accessory whose bone names mirror the avatar's (Hips, Spine,
    /// Head, ...). At build time CVRFury walks the prop armature and either reparents
    /// each prop bone under the matching avatar bone (Reparent) or merges the prop's
    /// skinned meshes onto the avatar's existing bones (MergeBones), then removes the
    /// now-empty duplicate skeleton.
    ///
    /// This lets clothing rigged to its own copy of the armature follow the avatar's
    /// bones (and DynamicBones / physics) without manual parenting.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Armature Link")]
    public class CVRFuryArmatureLink : CVRFuryComponent
    {
        public override string FeatureTitle => "Armature Link";

        // Reshapes the hierarchy: run early.
        public override int BuildPriority => -20;

        public enum LinkMode
        {
            /// <summary>Reparent each prop bone under the matching avatar bone. Keeps the
            /// prop's own skinned meshes bound to the prop bones, which now live inside the
            /// avatar skeleton. Safest, works for most clothing.</summary>
            Reparent,

            /// <summary>Rebind the prop's skinned meshes directly to the avatar's bones and
            /// delete the prop skeleton entirely. Produces the fewest bones but requires the
            /// bind poses to line up.</summary>
            MergeBones,
        }

        [Tooltip("Root of the prop armature. Defaults to this GameObject if left empty.")]
        public Transform propArmatureRoot;

        [Tooltip("Avatar bone to link the prop root onto. Defaults to the avatar's matching " +
                 "bone by name (usually Hips).")]
        public Transform linkTargetOverride;

        public LinkMode linkMode = LinkMode.Reparent;

        [Tooltip("Strip this prefix from prop bone names before matching against avatar bones " +
                 "(e.g. \"Armature/\" or a clothing namespace).")]
        public string removeBonePrefix = "";

        [Tooltip("Strip this suffix from prop bone names before matching (e.g. \" 1\", \"_clothing\").")]
        public string removeBoneSuffix = "";

        [Tooltip("Keep the prop bones' world position/rotation when linking, instead of " +
                 "snapping them onto the avatar bone transforms.")]
        public bool keepBoneOffsets = false;
    }
}
