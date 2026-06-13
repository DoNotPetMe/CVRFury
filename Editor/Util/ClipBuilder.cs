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

                // MaterialProperty resting is left to write-defaults / material authoring; we
                // can't reliably read an arbitrary shader property's "rest" value here.
            }
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
                    var s = a.scaleTarget.localScale * a.scaleFactor;
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
