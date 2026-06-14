using System;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Toggles controlling how aggressively a VRChat avatar is converted to ChilloutVR. The simple,
    /// safe steps default on; the "harder" automated steps (PhysBone physics, expression menus,
    /// playable-layer merging) are opt-in because they are approximate and may need hand-tuning.
    /// </summary>
    [Serializable]
    public class ConversionOptions
    {
        // --- Easy / safe (on by default) ---
        public bool avatarBasics = true;      // viewpoint, visemes, blink, eye look → CVRAvatar
        public bool stripVrcAndBroken = true; // remove VRChat components + missing scripts at the end

        // --- Harder / approximate (auto when toggled) ---
        public bool physBones = false;        // VRCPhysBone → DynamicBone
        public bool physBoneColliders = false;// VRCPhysBoneCollider → DynamicBoneCollider
        public bool expressions = false;      // ExpressionParameters + ExpressionsMenu → CVR AAS
        public bool mergePlayableLayers = false; // FX controller → CVR animator (FX only by default)
        public bool mergeGestureLayer = false;   // also merge the Gesture layer (can conflict with CVR gestures)
        public bool removeFinalIK = false;       // strip RootMotion.FinalIK (VRIK) — often needed for CVR

        /// <summary>Make every converted parameter LOCAL (not network-synced). This drops the synced
        /// bit cost to zero so the CCK can always build the AAS controller — useful to get a heavy
        /// avatar working/testing, at the cost of others not seeing your toggles. When off, CVRFury
        /// still respects VRChat's per-parameter networkSynced flag.</summary>
        public bool forceLocalParameters = false;

        /// <summary>Convenience: flip every "hard" step on at once.</summary>
        public void EnableAllAutomatic()
        {
            physBones = physBoneColliders = expressions = mergePlayableLayers = true;
        }
    }
}
