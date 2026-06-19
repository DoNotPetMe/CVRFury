using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Orchestrates a full CVRFury bake on an avatar. Called automatically from the CCK build
    /// hook, or manually via the CVRFury menu. Operates on the GameObject it is given (the
    /// CCK's build instance on upload, or a clone during a manual bake) and never modifies
    /// shared source assets — generated controllers/clips are clones written to a temp folder.
    /// </summary>
    internal static class CVRFuryBuilder
    {
        /// <summary>Returns true if any CVRFury work was performed.</summary>
        public static bool Run(GameObject avatarRoot, BuildTrigger trigger)
        {
            var avatar = CckAvatar.FindOn(avatarRoot);
            if (avatar == null)
            {
                // Not a CVR avatar (no CCK, or this GameObject has no CVRAvatar). Nothing to do.
                return false;
            }

            var features = CollectFeatures(avatarRoot);
            if (features.Count == 0) return false;

            // Clear any leftovers from a previous build, then make a fresh temp folder. We do
            // NOT delete the current folder here — its assets must survive into the upload bundle.
            ClearStaleGenerated();
            var assets = AssetSaver.CreateTemp(avatarRoot.name);
            var ctx = new BuildContext(avatarRoot, avatar, assets, trigger);

            ctx.Log.Info($"Baking {features.Count} feature(s) on '{avatarRoot.name}'.");

            // Strip inert/broken VRChat-and-friends components before anything else, so the upload
            // is clean and later steps see a tidy hierarchy.
            if (CVRFurySettings.CleanMissingScriptsOnBuild)
            {
                var removed = MissingScriptCleaner.RemoveInHierarchy(avatarRoot);
                if (removed > 0)
                    ctx.Log.Info($"Removed {removed} broken/missing-script component(s).");
            }

            foreach (var feature in features)
            {
                var builder = FeatureBuilderRegistry.For(feature);
                if (builder == null)
                {
                    ctx.Log.Warning($"No builder for '{feature.FeatureTitle}' " +
                                    $"({feature.GetType().Name}); skipped.");
                    continue;
                }

                try
                {
                    builder.Apply(ctx, feature);
                    ctx.Log.Info($"Applied {feature.FeatureTitle} on '{feature.gameObject.name}'.");
                }
                catch (System.Exception e)
                {
                    ctx.Log.Error($"{feature.FeatureTitle} on '{feature.gameObject.name}' failed: {e.Message}");
                    if (CVRFurySettings.VerboseLogging) Debug.LogException(e);
                }
            }

            FinalizeAnimators(ctx);
            AutomaticFixes.Run(ctx);
            CheckParameterBudget(ctx);
            StripComponents(avatarRoot);
            assets.Flush();

            ctx.Log.Info("CVRFury bake complete.");
            BuildLogWindow.Publish(ctx.Log);
            return true;
        }

        /// <summary>
        /// Lightweight pass for props/spawnables, which have no CVRAvatar. Only structural features
        /// that don't touch Advanced Avatar Settings are applied (currently Object State); CVRFury
        /// components are always stripped so nothing editor-only ships.
        /// </summary>
        public static bool RunProps(GameObject propRoot)
        {
            var features = propRoot.GetComponentsInChildren<CVRFuryComponent>(true);
            if (features.Length == 0) return false;

            if (CVRFurySettings.CleanMissingScriptsOnBuild)
                MissingScriptCleaner.RemoveInHierarchy(propRoot);

            foreach (var f in features.OfType<CVRFuryObjectState>())
            {
                try { new ObjectStateBuilder().Apply(null, f); }
                catch (System.Exception e) { Debug.LogError($"[CVRFury] Prop Object State failed: {e.Message}"); }
            }

            StripComponents(propRoot);
            return true;
        }

        /// <summary>ChilloutVR syncs a limited budget of bits across all players (~3200). Report the real
        /// estimated bit cost and warn loudly when a build is over it — otherwise the only symptom is the
        /// CCK's cryptic "Build asset failed content validation" abort at upload.</summary>
        private static void CheckParameterBudget(BuildContext ctx)
        {
            var list = ctx.Avatar?.SettingsList;
            if (list == null) return;

            const int Cap = 3200;
            int bits = ctx.Avatar.EstimateSyncedBits();
            if (bits > Cap)
                ctx.Log.Error($"This avatar's synced settings need ~{bits} bits — OVER ChilloutVR's {Cap}-bit " +
                              $"limit ({list.Count} synced settings). The upload fails content validation because " +
                              "of this. Reduce it: delete menu parameters you don't actually use in-game, and set " +
                              "rarely-changed ones to local-only by starting their Machine Name with '#' (CVR " +
                              "doesn't sync '#' parameters). On/off controls should be Bool, not Float, in the " +
                              "CVRAvatar Advanced Settings list.");
            else if (bits > Cap * 3 / 4)
                ctx.Log.Warning($"Synced settings use ~{bits} of ChilloutVR's {Cap}-bit budget " +
                                $"({list.Count} settings) — getting close. Consider localising (#) unused ones.");
            else
                ctx.Log.Info($"Synced settings: {list.Count} (~{bits} of {Cap} synced bits).");
        }

        private static List<CVRFuryComponent> CollectFeatures(GameObject root)
        {
            return root.GetComponentsInChildren<CVRFuryComponent>(true)
                // Stable order: by declared priority, then by hierarchy depth/sibling order.
                .OrderBy(c => c.BuildPriority)
                .ThenBy(c => c.transform.GetSiblingIndex())
                .ToList();
        }

        /// <summary>Once features have populated the working controller, wire it back onto the
        /// avatar's AAS as a fresh override controller so the build instance points at our
        /// clone rather than the original asset.</summary>
        private static void FinalizeAnimators(BuildContext ctx)
        {
            var controller = ctx.Controller;
            if (controller == null) return; // No feature needed an animator.

            ctx.Avatar.EnableAdvancedSettings();
            ctx.Avatar.BaseController = controller;

            var overrides = new AnimatorOverrideController(controller)
            {
                name = controller.name + " (Overrides)",
            };
            ctx.Assets.Save(overrides, overrides.name);
            ctx.Avatar.Overrides = overrides;
        }

        /// <summary>Remove every CVRFury component from the build instance so nothing
        /// editor-only ships in the bundle.</summary>
        private static void StripComponents(GameObject root)
        {
            foreach (var c in root.GetComponentsInChildren<CVRFuryComponent>(true))
                Object.DestroyImmediate(c, true);
        }

        private static void ClearStaleGenerated()
        {
            var root = CVRFurySettings.GeneratedFolder;
            if (UnityEditor.AssetDatabase.IsValidFolder(root))
                UnityEditor.AssetDatabase.DeleteAsset(root);
        }
    }
}
