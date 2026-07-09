using System.Linq;
using UnityEditor;
using UnityEngine;
using CVRFury.Components;

namespace CVRFury.Builder
{
    /// <summary>Turns a <see cref="FuryState"/> into an <see cref="AnimationClip"/> whose
    /// bindings are relative to the avatar root.</summary>
    internal static class ClipBuilder
    {
        public static AnimationClip Build(Transform avatarRoot, FuryState state, string name)
        {
            var clip = new AnimationClip { name = name };
            if (state == null || state.IsEmpty) return clip;

            foreach (var a in state.actions)
                Apply(avatarRoot, clip, a);

            return clip;
        }

        /// <summary>
        /// Build the "resting" counterpart of a state: for each action it captures the target's
        /// <i>current</i> scene value, so a toggle's Off state restores exactly what the avatar
        /// looked like before. Because our layers run with write-defaults off, the Off clip must
        /// explicitly drive the same bindings the On clip does.
        /// </summary>
        public static AnimationClip BuildResting(Transform avatarRoot, FuryState state, string name)
        {
            var clip = new AnimationClip { name = name };
            if (state == null || state.IsEmpty) return clip;

            foreach (var a in state.actions)
                ApplyResting(avatarRoot, clip, a);

            return clip;
        }

        private static void ApplyResting(Transform root, AnimationClip clip, FuryAction a)
        {
            switch (a.type)
            {
                case FuryAction.ActionType.ObjectToggle:
                    if (a.targetObject == null) return;
                    SetFloat(clip, Path(root, a.targetObject.transform), typeof(GameObject),
                        "m_IsActive", a.targetObject.activeSelf ? 1f : 0f);
                    break;

                case FuryAction.ActionType.BlendShape:
                    if (a.blendShapeRenderer == null || string.IsNullOrEmpty(a.blendShape)) return;
                    var idx = a.blendShapeRenderer.sharedMesh != null
                        ? a.blendShapeRenderer.sharedMesh.GetBlendShapeIndex(a.blendShape) : -1;
                    var cur = idx >= 0 ? a.blendShapeRenderer.GetBlendShapeWeight(idx) : 0f;
                    SetFloat(clip, Path(root, a.blendShapeRenderer.transform), typeof(SkinnedMeshRenderer),
                        $"blendShape.{a.blendShape}", cur);
                    break;

                case FuryAction.ActionType.ScaleFactor:
                    if (a.scaleTarget == null) return;
                    var p = Path(root, a.scaleTarget);
                    var s = a.scaleTarget.localScale;
                    SetFloat(clip, p, typeof(Transform), "m_LocalScale.x", s.x);
                    SetFloat(clip, p, typeof(Transform), "m_LocalScale.y", s.y);
                    SetFloat(clip, p, typeof(Transform), "m_LocalScale.z", s.z);
                    break;

                case FuryAction.ActionType.MaterialSwap:
                    if (a.materialRenderer == null) return;
                    var mats = a.materialRenderer.sharedMaterials;
                    if (a.materialSlot >= 0 && a.materialSlot < mats.Length)
                        SetObjectRef(clip, Path(root, a.materialRenderer.transform), a.materialRenderer.GetType(),
                            $"m_Materials.Array.data[{a.materialSlot}]", mats[a.materialSlot]);
                    break;

                case FuryAction.ActionType.MaterialProperty:
                {
                    // Capture the material's CURRENT value so toggling OFF restores it. Without this
                    // (write-defaults off), a toggled shader property (e.g. Poiyomi glitter) stays at the
                    // ON value forever once enabled.
                    if (a.propertyRenderer == null || string.IsNullOrEmpty(a.propertyName)) return;
                    var mat = a.propertyRenderer.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty(a.propertyName));
                    if (mat == null) return;
                    var path2 = Path(root, a.propertyRenderer.transform);
                    var rt2 = a.propertyRenderer.GetType();
                    if (a.propertyIsColor)
                    {
                        var cur2 = mat.GetColor(a.propertyName);
                        SetFloat(clip, path2, rt2, $"material.{a.propertyName}.r", cur2.r);
                        SetFloat(clip, path2, rt2, $"material.{a.propertyName}.g", cur2.g);
                        SetFloat(clip, path2, rt2, $"material.{a.propertyName}.b", cur2.b);
                        SetFloat(clip, path2, rt2, $"material.{a.propertyName}.a", cur2.a);
                    }
                    else
                        SetFloat(clip, path2, rt2, $"material.{a.propertyName}", mat.GetFloat(a.propertyName));
                    break;
                }
            }
        }

        /// <summary>
        /// Build a clip for one option of an exclusive control (a mode, or a slider endpoint).
        /// Every binding touched by <i>any</i> option (<paramref name="allActions"/>) is first set
        /// to its resting value, then this option's actions are layered on top. That union coverage
        /// is what makes modes/sliders mutually exclusive — switching away from an option restores
        /// whatever it had changed, rather than leaving it stuck on.
        /// </summary>
        public static AnimationClip BuildExclusive(Transform avatarRoot, FuryState option,
                                                   System.Collections.Generic.IEnumerable<FuryAction> allActions,
                                                   string name)
        {
            var clip = new AnimationClip { name = name };
            foreach (var a in allActions)
                ApplyResting(avatarRoot, clip, a);
            if (option != null && !option.IsEmpty)
                foreach (var a in option.actions)
                    Apply(avatarRoot, clip, a);
            return clip;
        }

        private static void Apply(Transform root, AnimationClip clip, FuryAction a)
        {
            switch (a.type)
            {
                case FuryAction.ActionType.ObjectToggle:
                    if (a.targetObject == null) return;
                    SetFloat(clip, Path(root, a.targetObject.transform), typeof(GameObject),
                        "m_IsActive", a.targetState ? 1f : 0f);
                    break;

                case FuryAction.ActionType.BlendShape:
                    if (a.blendShapeRenderer == null || string.IsNullOrEmpty(a.blendShape)) return;
                    SetFloat(clip, Path(root, a.blendShapeRenderer.transform), typeof(SkinnedMeshRenderer),
                        $"blendShape.{a.blendShape}", a.blendShapeValue);
                    break;

                case FuryAction.ActionType.ScaleFactor:
                    if (a.scaleTarget == null) return;
                    var p = Path(root, a.scaleTarget);
                    var b = a.scaleTarget.localScale;
                    // Apply the factor only on the chosen axes (uniform "size" = all three; "length" = one).
                    var s = new Vector3(
                        a.scaleAxes.x != 0f ? b.x * a.scaleFactor : b.x,
                        a.scaleAxes.y != 0f ? b.y * a.scaleFactor : b.y,
                        a.scaleAxes.z != 0f ? b.z * a.scaleFactor : b.z);
                    SetFloat(clip, p, typeof(Transform), "m_LocalScale.x", s.x);
                    SetFloat(clip, p, typeof(Transform), "m_LocalScale.y", s.y);
                    SetFloat(clip, p, typeof(Transform), "m_LocalScale.z", s.z);
                    break;

                case FuryAction.ActionType.MaterialProperty:
                    if (a.propertyRenderer == null || string.IsNullOrEmpty(a.propertyName)) return;
                    var rp = Path(root, a.propertyRenderer.transform);
                    var rt = a.propertyRenderer.GetType();
                    if (a.propertyIsColor)
                    {
                        SetFloat(clip, rp, rt, $"material.{a.propertyName}.r", a.propertyColor.r);
                        SetFloat(clip, rp, rt, $"material.{a.propertyName}.g", a.propertyColor.g);
                        SetFloat(clip, rp, rt, $"material.{a.propertyName}.b", a.propertyColor.b);
                        SetFloat(clip, rp, rt, $"material.{a.propertyName}.a", a.propertyColor.a);
                    }
                    else
                    {
                        SetFloat(clip, rp, rt, $"material.{a.propertyName}", a.propertyValue);
                    }
                    break;

                case FuryAction.ActionType.MaterialSwap:
                    if (a.materialRenderer == null || a.material == null) return;
                    SetObjectRef(clip, Path(root, a.materialRenderer.transform), a.materialRenderer.GetType(),
                        $"m_Materials.Array.data[{a.materialSlot}]", a.material);
                    break;
            }
        }

        private static string Path(Transform root, Transform t) => HierarchyUtil.GetPath(root, t) ?? "";

        private static void SetFloat(AnimationClip clip, string path, System.Type type, string prop, float value)
        {
            var binding = EditorCurveBinding.FloatCurve(path, type, prop);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        }

        private static void SetObjectRef(AnimationClip clip, string path, System.Type type, string prop, Object value)
        {
            var binding = EditorCurveBinding.PPtrCurve(path, type, prop);
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = value },
            });
        }
    }
}
