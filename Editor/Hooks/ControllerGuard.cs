using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Safety net for the recurring "motorbike pose". CVR drives avatar movement through the AAS
    /// animator's locomotion layers (MovementX / MovementY / Grounded). If that animator ever loses its
    /// locomotion — most commonly because the CCK regenerated <c>avatarSettings.animator</c> after the
    /// avatar inspector was edited (e.g. assigning visemes) <i>after</i> the controller was built — the
    /// avatar loads in CVR's default seated/"motorbike" pose with no movement.
    ///
    /// At upload we check the build instance and, if its AAS animator has no CVR locomotion, re-point it
    /// at one that does (the base controller it was extending, or the CVRFury-generated controller on
    /// disk). This makes the order of operations stop mattering: even if something reset the animator,
    /// the uploaded avatar still moves.
    /// </summary>
    internal static class ControllerGuard
    {
        public static bool HasCvrLocomotion(AnimatorController c)
        {
            if (c == null) return false;
            var names = new HashSet<string>(c.parameters.Select(p => p.name));
            if (!(names.Contains("MovementX") && names.Contains("MovementY") && names.Contains("Grounded")))
                return false;
            // Parameters alone aren't enough: the avatar still motorbikes if nothing actually consumes them
            // (e.g. the locomotion blend tree was dropped when the animator was regenerated). Require a real
            // locomotion blend tree so the "Fix" button stops reporting a broken controller as healthy.
            return c.layers.Any(l => l.stateMachine != null && StateMachineDrivesLocomotion(l.stateMachine));
        }

        private static bool StateMachineDrivesLocomotion(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states)
                if (cs.state.motion is BlendTree bt && BlendTreeDrivesLocomotion(bt)) return true;
            foreach (var sub in sm.stateMachines)
                if (sub.stateMachine != null && StateMachineDrivesLocomotion(sub.stateMachine)) return true;
            return false;
        }

        private static bool BlendTreeDrivesLocomotion(BlendTree bt)
        {
            if (bt.blendParameter == "MovementX" || bt.blendParameter == "MovementY" ||
                bt.blendParameterY == "MovementX" || bt.blendParameterY == "MovementY")
                return true;
            foreach (var ch in bt.children)
                if (ch.motion is BlendTree child && BlendTreeDrivesLocomotion(child)) return true;
            return false;
        }

        public static void ReassertLocomotion(GameObject root, BuildLog log)
        {
            if (root == null) return;
            var cvr = CckAvatar.FindOn(root);
            var settings = cvr?.AdvancedSettings;
            if (settings == null) return; // not an AAS avatar — nothing to guard

            var animator = Reflect.GetField(settings, CckNames.AdvancedSettings_Animator) as AnimatorController;
            if (HasCvrLocomotion(animator)) return; // already fine

            var baseC = Reflect.GetField(settings, CckNames.AdvancedSettings_BaseController) as AnimatorController;
            var fix = HasCvrLocomotion(baseC) ? baseC : FindGeneratedWithLocomotion();
            if (fix == null)
            {
                log?.Warning("The AAS animator has no CVR locomotion (motorbike pose) and no replacement " +
                             "with locomotion was found. Re-run Step 2 (Build & attach) as your last step.");
                return;
            }

            Reflect.SetField(settings, CckNames.AdvancedSettings_Animator, fix);
            Reflect.SetField(settings, CckNames.AdvancedSettings_BaseController, fix);
            Reflect.SetField(settings, CckNames.AdvancedSettings_Initialized, true);
            var anim = root.GetComponent<Animator>();
            if (anim != null) anim.runtimeAnimatorController = fix;

            log?.Warning($"AAS animator had no CVR locomotion (the motorbike pose) — re-asserted '{fix.name}'. " +
                         "This usually means the CCK regenerated the animator after the avatar inspector was " +
                         "edited; building the controller (Step 2) LAST avoids it.");
        }

        private static AnimatorController FindGeneratedWithLocomotion()
        {
            const string dir = "Assets/CVRFury Generated";
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { dir }))
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
                if (HasCvrLocomotion(c)) return c;
            }
            return null;
        }
    }
}
