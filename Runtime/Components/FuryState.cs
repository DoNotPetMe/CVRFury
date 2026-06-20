using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// A single thing that happens when a state is "on" — toggle an object, push a
    /// blendshape, swap a material, etc. Modelled after VRCFury's "Action", but using a
    /// type enum (rather than <c>[SerializeReference]</c>) so the data survives prefab
    /// variants and CCK's older Unity serializer cleanly.
    /// </summary>
    [Serializable]
    public class FuryAction
    {
        public enum ActionType
        {
            ObjectToggle,
            BlendShape,
            MaterialSwap,
            ScaleFactor,
            MaterialProperty,
        }

        public ActionType type = ActionType.ObjectToggle;

        // --- ObjectToggle ---
        public GameObject targetObject;
        public bool targetState = true;

        // --- BlendShape ---
        public SkinnedMeshRenderer blendShapeRenderer;
        public string blendShape = "";
        [Range(0f, 100f)] public float blendShapeValue = 100f;

        // --- MaterialSwap ---
        public Renderer materialRenderer;
        public int materialSlot = 0;
        public Material material;

        // --- ScaleFactor ---
        public Transform scaleTarget;
        public float scaleFactor = 1f;
        // Which axes the factor applies to (1 = scale this axis, 0 = leave it). (1,1,1) = uniform "size";
        // e.g. (0,0,1) = "length" along Z only. Defaults to uniform for backwards compatibility.
        public Vector3 scaleAxes = Vector3.one;

        // --- MaterialProperty (float / color) ---
        public Renderer propertyRenderer;
        public string propertyName = "";
        public bool propertyIsColor = false;
        public float propertyValue = 1f;
        public Color propertyColor = Color.white;
    }

    /// <summary>
    /// The set of actions that define one end of an animation (e.g. the "on" pose of a
    /// toggle). The builder turns this into an <see cref="UnityEngine.AnimationClip"/>.
    /// An empty state produces an empty clip, which is the correct "do nothing / rest"
    /// behaviour.
    /// </summary>
    [Serializable]
    public class FuryState
    {
        public List<FuryAction> actions = new List<FuryAction>();

        public bool IsEmpty => actions == null || actions.Count == 0;
    }
}
