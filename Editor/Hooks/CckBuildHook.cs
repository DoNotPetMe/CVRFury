using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace CVRFury.Builder
{
    /// <summary>
    /// Wires CVRFury into the ChilloutVR CCK upload pipeline.
    ///
    /// ChilloutVR exposes no formal preprocess interface (unlike VRChat's
    /// <c>IVRCSDKPreprocessAvatarCallback</c>). Instead, <c>CCK_BuildUtility</c> raises two
    /// static <see cref="UnityEvent{T}"/>s — <c>PreAvatarBundleEvent</c> and
    /// <c>PrePropBundleEvent</c> — right before it packages content for upload. We subscribe
    /// to the avatar one via reflection and run the full CVRFury bake on the GameObject the
    /// CCK is about to bundle.
    ///
    /// Subscription happens once per domain load via <see cref="InitializeOnLoadAttribute"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class CckBuildHook
    {
        private static bool _subscribed;

        static CckBuildHook()
        {
            // Defer until the first editor tick so every assembly (including the CCK,
            // which may load after us) is present.
            EditorApplication.delayCall += TrySubscribe;
        }

        private static void TrySubscribe()
        {
            if (_subscribed) return;

            // Is the CCK even present? If not, stay silent — CVRFury is harmless without it and the
            // user may import the CCK later, triggering another domain reload.
            if (Reflect.FindType(CckNames.AvatarType) == null &&
                Reflect.FindType(CckNames.BuildUtilityType) == null)
                return;

            // Discover the pre-bundle events by reflection rather than trusting one hard-coded name,
            // because CVR has moved/renamed these across CCK generations.
            var events = CckProbe.Discover();

            var hookedAvatar = false;
            var hookedProp = false;
            UnityAction<GameObject> avatarListener = OnPreAvatarBundle;
            UnityAction<GameObject> propListener = OnPrePropBundle;

            foreach (var e in events)
            {
                if (e.EventInstance == null) continue;
                if (e.IsAvatar && Reflect.AddUnityEventListener(e.EventInstance, avatarListener))
                {
                    hookedAvatar = true;
                    if (CVRFurySettings.VerboseLogging)
                        Debug.Log($"[CVRFury] Hooked avatar build event {e.TypeName}.{e.MemberName}");
                }
                else if (e.IsProp && Reflect.AddUnityEventListener(e.EventInstance, propListener))
                {
                    hookedProp = true;
                    if (CVRFurySettings.VerboseLogging)
                        Debug.Log($"[CVRFury] Hooked prop build event {e.TypeName}.{e.MemberName}");
                }
            }

            if (hookedAvatar || hookedProp)
            {
                _subscribed = true;
            }
            else
            {
                Debug.LogWarning(
                    "[CVRFury] The CCK is present but no avatar build event could be hooked, so CVRFury " +
                    "features will NOT be applied on upload. Run Tools ▸ CVRFury ▸ Diagnose CCK " +
                    "Integration and share the output so the hook can be updated for your CCK version.");
            }
        }

        /// <summary>
        /// Invoked by the CCK on the avatar GameObject about to be bundled. Runs the bake and
        /// never throws into the CCK: a CVRFury failure is reported but does not abort the
        /// user's upload of the rest of the avatar.
        /// </summary>
        private static void OnPreAvatarBundle(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;

            try
            {
                CVRFuryBuilder.Run(avatarRoot, BuildTrigger.CckUpload);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[CVRFury] Build failed for '{avatarRoot.name}'. The avatar was uploaded " +
                    $"WITHOUT CVRFury changes. Details:\n{e}");
            }

            // Last line of defence against the motorbike pose: if the AAS animator lost CVR locomotion
            // (e.g. the CCK regenerated it after a viseme/inspector edit), re-assert one that has it so the
            // uploaded avatar still moves. Runs after the bake and never throws into the CCK.
            try
            {
                ControllerGuard.ReassertLocomotion(avatarRoot, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CVRFury] Locomotion guard skipped for '{avatarRoot.name}': {e.Message}");
            }
        }

        private static void OnPrePropBundle(GameObject propRoot)
        {
            if (propRoot == null) return;
            try
            {
                CVRFuryBuilder.RunProps(propRoot);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CVRFury] Prop build failed for '{propRoot.name}':\n{e}");
            }
        }
    }
}
