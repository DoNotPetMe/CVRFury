using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Convenience helper for the fiddly CVRAvatar fields that prefab creators usually want set
    /// for them: the viewpoint and voice positions, the face mesh used for visemes/blink, and the
    /// blink / viseme / eye-movement toggles. Lets a shipped prefab configure these without the
    /// end user hunting through the CVRAvatar inspector.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Avatar Settings")]
    public class CVRFuryAvatarSettings : CVRFuryComponent
    {
        public override string FeatureTitle => "Avatar Settings";

        // Apply before menu-building features (purely cosmetic ordering).
        public override int BuildPriority => -5;

        [Header("Spatial")]
        [Tooltip("If set, the CVRAvatar viewpoint is moved to this transform's position.")]
        public Transform viewpoint;

        [Tooltip("If set, the CVRAvatar voice position is moved to this transform's position.")]
        public Transform voicePosition;

        [Header("Face")]
        [Tooltip("Mesh that carries the viseme / blink blendshapes (usually the body/face).")]
        public SkinnedMeshRenderer faceMesh;

        public bool enableVisemes = false;
        public bool enableBlink = false;
        public bool enableEyeMovement = false;
    }
}
