using System.Linq;
using CVRFury.Components;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Bakes the crouch/prone style dropdown. The custom pose states are added INSIDE the
    /// locomotion layer (the SyncDances/emote pattern) rather than as a separate full-body
    /// override layer — an override layer fights CVR's locomotion even when idle (the
    /// "motorbike pose"), while in-layer states simply return to Standard Locomotion.
    /// </summary>
    internal sealed class LocomotionStylesBuilder : FeatureBuilder<CVRFuryLocomotionStyles>
    {
        protected override void Build(BuildContext ctx, CVRFuryLocomotionStyles f)
        {
            if (f.styles == null || f.styles.Count == 0)
            {
                ctx.Log.Warning($"Locomotion Styles '{f.gameObject.name}' has no styles; skipped.");
                return;
            }

            var displayName = MenuLeaf(f.menuPath, f.gameObject.name);
            var param = ctx.AllocateParam(
                !string.IsNullOrEmpty(f.parameterName) ? f.parameterName : displayName);
            var controller = ctx.GetOrCreateController();

            AnimatorUtil.EnsureIntParam(controller, param, 0);
            // Crouching / Prone are read-only Bool core parameters the game drives.
            AnimatorUtil.EnsureBoolParam(controller, CckNames.CrouchingParam);
            AnimatorUtil.EnsureBoolParam(controller, CckNames.ProneParam);

            var layerIdx = ControllerGuard.FindLocomotionLayerIndex(controller);
            if (layerIdx < 0)
            {
                layerIdx = 0;
                if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
                ctx.Log.Warning($"Locomotion Styles '{displayName}': no CVR locomotion layer found; " +
                                "adding pose states to the base layer. If the avatar has no CVR " +
                                "locomotion the styles may not display until that is fixed.");
            }
            var sm = controller.layers[layerIdx].stateMachine;
            var home = sm.defaultState;
            if (home == null)
            {
                home = sm.AddState("CVRFury Locomotion Home");
                home.writeDefaultValues = true;
                sm.defaultState = home;
            }
            var wd = home.writeDefaultValues;

            var built = 0;
            for (var i = 0; i < f.styles.Count; i++)
            {
                var style = f.styles[i];
                var idx = i + 1; // dropdown option index; 0 = CVR default
                var label = string.IsNullOrEmpty(style.name) ? $"Style {idx}" : style.name;

                built += AddPoseState(sm, home, wd, param, idx, CckNames.CrouchingParam,
                    style.crouchClip, $"CVRFury {displayName} Crouch {label}",
                    f.transitionSeconds, new Vector3(420f, 80f + i * 120f, 0f));
                built += AddPoseState(sm, home, wd, param, idx, CckNames.ProneParam,
                    style.proneClip, $"CVRFury {displayName} Prone {label}",
                    f.transitionSeconds, new Vector3(420f, 140f + i * 120f, 0f));
            }

            if (built == 0)
            {
                ctx.Log.Warning($"Locomotion Styles '{displayName}': no style has a crouch or prone " +
                                "clip assigned; skipped.");
                return;
            }

            var options = new[] { "Default" }
                .Concat(f.styles.Select((s, i) => string.IsNullOrEmpty(s.name) ? $"Style {i + 1}" : s.name))
                .ToArray();
            if (!ctx.Avatar.AddDropdown(displayName, param, options, 0, f.localOnly))
                ctx.Log.Warning($"Locomotion Styles '{displayName}' animate correctly but could not " +
                                "be added to the in-game menu (AAS dropdown write failed).");
        }

        /// <summary>One pose state: entered from Any State while the dropdown selects this style AND
        /// the gate core parameter (Crouching / Prone) is true; exits back to locomotion when either
        /// stops being true. Returns 1 if a state was built, 0 if the clip is empty.</summary>
        private static int AddPoseState(AnimatorStateMachine sm, AnimatorState home, bool wd,
                                        string param, int idx, string gateParam, AnimationClip clip,
                                        string stateName, float seconds, Vector3 position)
        {
            if (clip == null) return 0;

            var st = sm.AddState(stateName, position);
            st.motion = clip;
            st.writeDefaultValues = wd;

            var enter = sm.AddAnyStateTransition(st);
            Configure(enter, seconds);
            enter.AddCondition(AnimatorConditionMode.Equals, idx, param);
            enter.AddCondition(AnimatorConditionMode.If, 0f, gateParam);

            var exitStyle = st.AddTransition(home);
            Configure(exitStyle, seconds);
            exitStyle.AddCondition(AnimatorConditionMode.NotEqual, idx, param);

            var exitGate = st.AddTransition(home);
            Configure(exitGate, seconds);
            exitGate.AddCondition(AnimatorConditionMode.IfNot, 0f, gateParam);

            return 1;
        }

        private static void Configure(AnimatorStateTransition t, float seconds)
        {
            t.hasExitTime = false;
            t.exitTime = 0f;
            t.hasFixedDuration = true;
            t.duration = Mathf.Max(0f, seconds);
            t.canTransitionToSelf = false;
        }

        private static string MenuLeaf(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            var leaf = path.Split('/').Last().Trim();
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
    }
}
