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

        /// <summary>Index of the first layer whose state machine contains a real CVR locomotion
        /// blend tree (MovementX/MovementY), or −1. Used to add in-layer states (dances, custom
        /// crouch/prone poses) to the layer CVR actually drives.</summary>
        public static int FindLocomotionLayerIndex(AnimatorController c)
        {
            if (c == null) return -1;
            for (var i = 0; i < c.layers.Length; i++)
                if (c.layers[i].stateMachine != null &&
                    StateMachineDrivesLocomotion(c.layers[i].stateMachine))
                    return i;
            return -1;
        }

        private static bool StateMachineDrivesLocomotion(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states)
                if (cs.state.motion is BlendTree bt && BlendTreeDrivesLocomotion(bt)) return true;
            foreach (var sub in sm.stateMachines)
                if (sub.stateMachine != null && StateMachineDrivesLocomotion(sub.stateMachine)) return true;
            return false;
        }

        /// <summary>Heuristic: the base layer is a VRChat locomotion system (GoGo Loco and friends), whose
        /// states/params CVR doesn't drive. Recognised by its characteristic state names.</summary>
        public static bool LooksLikeVRChatLocomotion(AnimatorController c)
        {
            if (c == null || c.layers.Length == 0) return false;
            var sm = c.layers[0].stateMachine;
            if (sm == null) return false;
            int hits = 0;
            foreach (var cs in sm.states)
            {
                var n = (cs.state?.name ?? "").ToLowerInvariant();
                if (n.Contains("standard locomotion") || n.Contains("crouching locomotion") ||
                    n.Contains("prone locomotion") || n.Contains("locflying") || n == "emotes")
                    hits++;
            }
            return hits >= 2; // several GoGo-Loco-style states = almost certainly VRChat locomotion
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
                var gogo = LooksLikeVRChatLocomotion(animator) ? " This avatar's locomotion is a VRChat system " +
                    "(e.g. GoGo Loco) — it's driven by VRChat parameters CVR doesn't provide, so it can't work " +
                    "in CVR. Remove the GoGo Loco object, then rebuild with Step 2 (Build & attach) leaving the " +
                    "Controller field EMPTY so it uses CVR's native locomotion." : "";
                log?.Warning("The AAS animator has no CVR locomotion (motorbike pose) and no replacement with " +
                             "CVR locomotion was found. Re-run Step 2 (Build & attach) with an empty Controller " +
                             "field as your last step." + gogo);
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
