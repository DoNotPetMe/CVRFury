using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Declares animator parameters (optionally synced to the in-game menu) without needing a whole
    /// controller. Useful for prefab interop: a clothing prefab can declare the synced parameters a
    /// companion Full Controller expects, so they exist and appear in the menu regardless of build
    /// order.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Parameters")]
    public class CVRFuryParameters : CVRFuryComponent
    {
        public override string FeatureTitle => "Parameters";

        // Declare parameters before features that might reference them.
        public override int BuildPriority => -2;

        public enum ParamType { Float, Int, Bool }
        public enum MenuKind { None, Toggle, Slider, Dropdown }

        [Serializable]
        public class Param
        {
            public string name = "";
            public ParamType type = ParamType.Float;
            public float defaultValue = 0f;

            [Tooltip("Whether/how to expose this parameter in the in-game Advanced Settings menu.")]
            public MenuKind menu = MenuKind.None;

            public string menuPath = "";
            public bool localOnly = false;
        }

        public List<Param> parameters = new List<Param>();
    }
}
