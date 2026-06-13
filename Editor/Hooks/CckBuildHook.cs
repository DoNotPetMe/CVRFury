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

            var buildUtility = Reflect.FindType(CckNames.BuildUtilityType);
            if (buildUtility == null)
            {
                // CCK not installed (yet). Stay quiet — CVRFury is harmless without it, and
                // the user may import the CCK later, triggering another domain reload.
                return;
            }

            var preAvatar = Reflect.GetStaticField(buildUtility, CckNames.PreAvatarBundleEvent);
            if (preAvatar == null)
            {
                Debug.LogWarning(
                    "[CVRFury] Found the CCK but not its PreAvatarBundleEvent. CVRFury features " +
                    "will NOT be applied on upload. Update Editor/Hooks/CckNames.cs for your CCK version.");
                return;
            }

            UnityAction<GameObject> avatarListener = OnPreAvatarBundle;
            if (Reflect.AddUnityEventListener(preAvatar, avatarListener))
            {
                _subscribed = true;
                if (CVRFurySettings.VerboseLogging)
                    Debug.Log("[CVRFury] Hooked CCK avatar build pipeline (PreAvatarBundleEvent).");
            }

            // Props/spawnables: best-effort. We can't write Advanced Avatar Settings here (props
            // aren't avatars), but structural features like Object State still apply, and we strip
            // CVRFury components so nothing editor-only ships.
            var preProp = Reflect.GetStaticField(buildUtility, CckNames.PrePropBundleEvent);
            if (preProp != null)
            {
                UnityAction<GameObject> propListener = OnPrePropBundle;
                Reflect.AddUnityEventListener(preProp, propListener);
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
