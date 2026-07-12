using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Reactions — capabilities that don't exist anywhere else in the tool:
    ///
    ///   • Touch reactions: a CCK trigger on a body-part bone drives a face blendshape while touched, with
    ///     two NEW output channels — a sound (AudioSource fired on touch) and a particle burst (hearts) —
    ///     and two styles: Instant, or BUILD-UP, where a generated animator layer ramps the blendshape from
    ///     0→100 over N seconds of continuous touch and eases back on release. The ramp layer is authored as
    ///     a real AnimatorController asset and merged at bake through the Full Controller pipeline.
    ///
    ///   • Breathing: an always-on generated layer loops a subtle chest/breath blendshape — idle life no
    ///     toggle or slider can produce, because it needs a looping clip playing forever.
    /// </summary>
    internal static class ReactionPack
    {
        private const string GenDir = "Assets/CVRFury Generated";
        private const string ReactDir = GenDir + "/Reactions";

        internal enum Style { Instant, BuildUp }

        // A curated subset of humanoid bones that make sense as touch zones.
        public static readonly (string label, HumanBodyBones bone)[] TouchZones =
        {
            ("Head", HumanBodyBones.Head),
            ("Chest", HumanBodyBones.Chest),
            ("Hips", HumanBodyBones.Hips),
            ("Left thigh", HumanBodyBones.LeftUpperLeg),
            ("Right thigh", HumanBodyBones.RightUpperLeg),
            ("Neck", HumanBodyBones.Neck),
        };

        public static string CreateTouchReaction(GameObject avatar, SkinnedMeshRenderer face, string blendshape,
                                                 HumanBodyBones zone, string zoneLabel, bool othersCanTrigger,
                                                 Style style, float buildSeconds, AudioClip sound, bool particles)
        {
            if (avatar == null || face == null || string.IsNullOrEmpty(blendshape))
                return "Pick the face mesh and a reaction blendshape.";
            var anim = avatar.GetComponentInChildren<Animator>();
            var bone = anim != null && anim.isHuman ? anim.GetBoneTransform(zone) : null;
            if (bone == null) return $"No humanoid bone found for {zoneLabel}.";

            var param = "Touch" + zoneLabel.Replace(" ", "");
            var toggle = Undo.AddComponent<CVRFuryToggle>(avatar);
            toggle.menuPath = $"Reactions/{zoneLabel} touch";
            toggle.parameterName = param;
            toggle.momentary = true;
            toggle.saved = false;
            toggle.transitionSeconds = 0.2f;

            var notes = new List<string>();

            if (style == Style.Instant)
                toggle.state.actions.Add(new FuryAction
                {
                    type = FuryAction.ActionType.BlendShape,
                    blendShapeRenderer = face, blendShape = blendshape, blendShapeValue = 100f,
                });
            else
            {
                // Build-up: the blendshape is driven by a generated ramp layer instead of the toggle state,
                // so the reaction GROWS with continuous touch and eases back when it stops.
                var ctrl = BuildRampController(avatar, face, blendshape, param, Mathf.Max(1f, buildSeconds));
                var full = avatar.GetComponent<CVRFuryFullController>();
                if (full == null) full = Undo.AddComponent<CVRFuryFullController>(avatar);
                if (!full.controllers.Contains(ctrl)) full.controllers.Add(ctrl);
                notes.Add($"build-up over {buildSeconds:0.#}s (ramp layer merged at upload)");
            }

            if (sound != null)
            {
                var sGo = new GameObject($"Reaction Sound ({param})");
                Undo.RegisterCreatedObjectUndo(sGo, "Reaction sound");
                sGo.transform.SetParent(bone, false);
                var src = sGo.AddComponent<AudioSource>();
                src.clip = sound;
                src.playOnAwake = true;   // fires the moment the toggle activates the object
                src.loop = false;
                src.spatialBlend = 1f;    // positional, from the touched spot
                src.minDistance = 0.5f;
                src.maxDistance = 6f;
                sGo.SetActive(false);
                toggle.state.actions.Add(new FuryAction
                { type = FuryAction.ActionType.ObjectToggle, targetObject = sGo, targetState = true });
                notes.Add($"plays '{sound.name}' at the {zoneLabel.ToLowerInvariant()}");
            }

            if (particles)
            {
                var pGo = CreateHeartParticles(param, bone);
                toggle.state.actions.Add(new FuryAction
                { type = FuryAction.ActionType.ObjectToggle, targetObject = pGo, targetState = true });
                notes.Add("heart particles while touched");
            }

            EditorUtility.SetDirty(avatar);

            var wired = AvatarFeaturePack.TryAddTouchTrigger(param, bone, Vector3.zero, 0.12f, othersCanTrigger);
            return $"\"{zoneLabel}\" reaction added ({blendshape}" +
                   (notes.Count > 0 ? "; " + string.Join(", ", notes) : "") + ")." +
                   (wired ? othersCanTrigger ? " Touch trigger placed — anyone's hand fires it."
                                             : " Touch trigger placed — only YOUR hands fire it."
                          : " (No CCK trigger component found — it works as a menu button only.)");
        }

        /// <summary>A one-layer controller: Rest (empty, WriteDefaults restores the shape) ⇄ Build (the
        /// blendshape eases 0→100 over the given seconds while the touch parameter stays true; releasing
        /// crossfades back over 0.8s). Merged into the avatar's animator by the Full Controller bake.</summary>
        private static AnimatorController BuildRampController(GameObject avatar, SkinnedMeshRenderer face,
                                                              string blendshape, string param, float seconds)
        {
            if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder("Assets", "CVRFury Generated");
            if (!AssetDatabase.IsValidFolder(ReactDir)) AssetDatabase.CreateFolder(GenDir, "Reactions");

            var clip = new AnimationClip { name = $"{param} Ramp" };
            var path = AnimationUtility.CalculateTransformPath(face.transform, avatar.transform);
            var curve = AnimationCurve.EaseInOut(0f, 0f, seconds, 100f);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendshape), curve);
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath($"{ReactDir}/{param} Ramp.anim"));

            var ctrlPath = AssetDatabase.GenerateUniqueAssetPath($"{ReactDir}/{param} BuildUp.controller");
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter(param, AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;

            var rest = sm.AddState("Rest");           // empty + WD on → writes the shape's default (0) back
            rest.writeDefaultValues = true;
            var build = sm.AddState("Build");
            build.motion = clip;                       // non-looping: clamps at 100 while touch continues
            build.writeDefaultValues = true;
            sm.defaultState = rest;

            var up = rest.AddTransition(build);
            up.hasExitTime = false; up.duration = 0.05f;
            up.AddCondition(AnimatorConditionMode.If, 0f, param);
            var down = build.AddTransition(rest);
            down.hasExitTime = false; down.duration = 0.8f; // ease back instead of snapping
            down.AddCondition(AnimatorConditionMode.IfNot, 0f, param);

            AssetDatabase.SaveAssets();
            return ctrl;
        }

        private static GameObject CreateHeartParticles(string param, Transform bone)
        {
            var go = new GameObject($"Reaction Hearts ({param})");
            Undo.RegisterCreatedObjectUndo(go, "Reaction particles");
            go.transform.SetParent(bone, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, 0.06f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.2f;
            main.startSpeed = 0.35f;
            main.startSize = 0.05f;
            main.startColor = new Color(1f, 0.45f, 0.65f); // pink
            main.gravityModifier = -0.05f;                  // drift upward
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = 9f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            go.SetActive(false); // the toggle activates it while touched
            return go;
        }

        // --- 🫁 Breathing: always-on idle life -------------------------------------------------------

        public static string CreateBreathing(GameObject avatar, SkinnedMeshRenderer mesh, string blendshape,
                                             float cycleSeconds, float intensity)
        {
            if (avatar == null || mesh == null || string.IsNullOrEmpty(blendshape))
                return "Pick the mesh and the breath blendshape (a chest/sternum shape works best).";
            cycleSeconds = Mathf.Clamp(cycleSeconds, 1f, 15f);
            intensity = Mathf.Clamp(intensity, 1f, 100f);

            if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder("Assets", "CVRFury Generated");
            if (!AssetDatabase.IsValidFolder(ReactDir)) AssetDatabase.CreateFolder(GenDir, "Reactions");

            var clip = new AnimationClip { name = "Breathing" };
            var path = AnimationUtility.CalculateTransformPath(mesh.transform, avatar.transform);
            // Inhale to `intensity`, exhale back — smooth ease both ways, looping forever.
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(cycleSeconds * 0.5f, intensity, 0f, 0f),
                new Keyframe(cycleSeconds, 0f, 0f, 0f));
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendshape), curve);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath($"{ReactDir}/Breathing.anim"));

            var ctrlPath = AssetDatabase.GenerateUniqueAssetPath($"{ReactDir}/Breathing.controller");
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var state = ctrl.layers[0].stateMachine.AddState("Breathe");
            state.motion = clip;
            state.writeDefaultValues = true;
            ctrl.layers[0].stateMachine.defaultState = state;
            AssetDatabase.SaveAssets();

            var full = avatar.GetComponent<CVRFuryFullController>();
            if (full == null) full = Undo.AddComponent<CVRFuryFullController>(avatar);
            full.controllers.Add(ctrl);
            EditorUtility.SetDirty(avatar);

            return $"Breathing added: '{blendshape}' cycles to {intensity:0} every {cycleSeconds:0.#}s, always " +
                   "on — subtle idle life that no toggle can produce. Delete the Full Controller entry to remove.";
        }
    }
}
