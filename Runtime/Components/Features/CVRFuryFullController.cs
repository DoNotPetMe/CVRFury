using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Merges one or more prebuilt Animator Controllers into the avatar, bringing their
    /// layers and parameters along. Each parameter can optionally be exposed as a synced
    /// Advanced Avatar Setting so it shows up in the in-game menu. This is the backbone
    /// feature most prefab/clothing creators rely on: a creator ships a controller +
    /// CVRFury Full Controller component, and it "just works" on any avatar.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Full Controller")]
    public class CVRFuryFullController : CVRFuryComponent
    {
        public override string FeatureTitle => "Full Controller";

        public List<RuntimeAnimatorController> controllers = new List<RuntimeAnimatorController>();

        [Tooltip("Optional prefix added to every parameter and layer brought in, to avoid " +
                 "collisions when the same controller is added more than once.")]
        public string parameterPrefix = "";

        [Serializable]
        public class ParamOverride
        {
            [Tooltip("Parameter name as it appears in the controller.")]
            public string name = "";

            [Tooltip("Expose this parameter as a synced Advanced Avatar Setting (menu entry).")]
            public bool exposeToMenu = false;

            [Tooltip("Menu path for the exposed control, e.g. \"Props/Sword\".")]
            public string menuPath = "";

            [Tooltip("Treat as local-only (not synced to other players).")]
            public bool localOnly = false;

            public AasParamType menuType = AasParamType.Toggle;
        }

        public enum AasParamType { Toggle, Slider, Dropdown }

        [Tooltip("Per-parameter handling. Parameters not listed here are merged into the " +
                 "animator but not added to the in-game menu.")]
        public List<ParamOverride> parameters = new List<ParamOverride>();

        [Tooltip("If a controller references a parameter not present after merge, create it " +
                 "instead of failing the build.")]
        public bool createMissingParameters = true;
    }
}
