using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class ArmatureLinkBuilder : FeatureBuilder<CVRFuryArmatureLink>
    {
        protected override void Build(BuildContext ctx, CVRFuryArmatureLink f)
        {
            var propRoot = f.propArmatureRoot != null ? f.propArmatureRoot : f.transform;

            // Map the avatar skeleton by cleaned bone name, excluding the prop subtree so we
            // never match a prop bone against itself.
            var avatarBones = BuildAvatarBoneMap(ctx.RootTransform, propRoot, f);
            if (avatarBones.Count == 0)
            {
                ctx.Log.Error("Armature Link found no avatar bones to link against.");
                return;
            }

            // Pair every prop bone with its avatar counterpart by name.
            var propToAvatar = new Dictionary<Transform, Transform>();
            foreach (var propBone in propRoot.GetComponentsInChildren<Transform>(true))
            {
                var key = Clean(propBone.name, f);
                if (avatarBones.TryGetValue(key, out var avatarBone))
                    propToAvatar[propBone] = avatarBone;
            }

            if (!propToAvatar.ContainsKey(propRoot) && f.linkTargetOverride == null)
                ctx.Log.Warning($"Prop root '{propRoot.name}' didn't match an avatar bone by name. " +
                                "Set a Link Target Override or check the prefix/suffix settings.");

            if (f.linkMode == CVRFuryArmatureLink.LinkMode.Reparent)
                Reparent(ctx, f, propRoot, avatarBones, propToAvatar);
            else
                MergeBones(ctx, f, propRoot, avatarBones, propToAvatar);
        }

        // --- Reparent: nest each prop bone under its matching avatar bone, keeping the prop's
        // own skeleton (so prop DynamicBones/physics keep working) ---
        private static void Reparent(BuildContext ctx, CVRFuryArmatureLink f, Transform propRoot,
                                     Dictionary<string, Transform> avatarBones,
                                     Dictionary<Transform, Transform> propToAvatar)
        {
            var target = ResolveRootTarget(f, propRoot, avatarBones, propToAvatar);
            if (target == null)
            {
                ctx.Log.Error("Could not resolve a target bone for the prop root.");
                return;
            }
            propRoot.SetParent(target, worldPositionStays: f.keepBoneOffsets);
            ctx.Log.Info($"Reparented '{propRoot.name}' under avatar bone '{target.name}'.");
        }

        // --- MergeBones: rebind the prop's skinned meshes onto avatar bones and discard the
        // duplicate prop skeleton ---
        private static void MergeBones(BuildContext ctx, CVRFuryArmatureLink f, Transform propRoot,
                                       Dictionary<string, Transform> avatarBones,
                                       Dictionary<Transform, Transform> propToAvatar)
        {
            // Rebind skinned meshes (search the whole prop GameObject, not just the armature).
            var propGo = f.transform;
            foreach (var smr in propGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                for (var i = 0; i < bones.Length; i++)
                    if (bones[i] != null && propToAvatar.TryGetValue(bones[i], out var mapped))
                        bones[i] = mapped;
                smr.bones = bones;

                if (smr.rootBone != null && propToAvatar.TryGetValue(smr.rootBone, out var mappedRoot))
                    smr.rootBone = mappedRoot;
            }

            // Move any non-bone content (extra attachments) hanging off matched prop bones onto
            // their avatar counterparts, then delete the now-redundant prop skeleton.
            foreach (var pair in propToAvatar)
            {
                var propBone = pair.Key;
                var avatarBone = pair.Value;
                foreach (var child in propBone.Cast<Transform>().ToList())
                {
                    if (propToAvatar.ContainsKey(child)) continue; // another bone, handled by its own entry
                    child.SetParent(avatarBone, worldPositionStays: true);
                }
            }

            // Delete the empty prop armature root if it became a pure skeleton duplicate.
            if (propToAvatar.ContainsKey(propRoot))
            {
                Object.DestroyImmediate(propRoot.gameObject, true);
                ctx.Log.Info("Merged prop bones onto avatar skeleton and removed the duplicate armature.");
            }
        }

        private static Transform ResolveRootTarget(CVRFuryArmatureLink f, Transform propRoot,
                                                    Dictionary<string, Transform> avatarBones,
                                                    Dictionary<Transform, Transform> propToAvatar)
        {
            if (f.linkTargetOverride != null) return f.linkTargetOverride;
            if (propToAvatar.TryGetValue(propRoot, out var matched)) return matched;
            // Fall back to Hips, the conventional armature root.
            return avatarBones.TryGetValue("hips", out var hips) ? hips : null;
        }

        private static Dictionary<string, Transform> BuildAvatarBoneMap(Transform avatarRoot,
                                                                        Transform propRoot,
                                                                        CVRFuryArmatureLink f)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                if (IsDescendantOf(t, propRoot)) continue; // skip the prop's own skeleton
                var key = Clean(t.name, f);
                if (!map.ContainsKey(key)) map[key] = t; // first match wins (closest to root)
            }
            return map;
        }

        private static bool IsDescendantOf(Transform t, Transform ancestor)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur == ancestor) return true;
            return false;
        }

        private static string Clean(string name, CVRFuryArmatureLink f)
        {
            if (!string.IsNullOrEmpty(f.removeBonePrefix) && name.StartsWith(f.removeBonePrefix))
                name = name.Substring(f.removeBonePrefix.Length);
            if (!string.IsNullOrEmpty(f.removeBoneSuffix) && name.EndsWith(f.removeBoneSuffix))
                name = name.Substring(0, name.Length - f.removeBoneSuffix.Length);
            return name.Trim().ToLowerInvariant();
        }
    }
}
