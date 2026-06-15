using System;
using System.Collections;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Converts VRCPhysBone / VRCPhysBoneCollider into the classic DynamicBone / DynamicBoneCollider
    /// that ChilloutVR uses. The two physics models differ, so this is an <i>approximation</i> with
    /// sensible defaults — chains will swing, but expect to tweak stiffness/elasticity by hand.
    /// Colliders are converted first so bones can re-reference them.
    /// </summary>
    internal sealed class PhysBoneConverter : IConverter
    {
        public string Title => "PhysBones → DynamicBones";
        public int Order => 20;
        public bool ShouldRun(ConversionContext ctx) => ctx.Options.physBones || ctx.Options.physBoneColliders;

        public void Run(ConversionContext ctx)
        {
            var dbType = Reflect.FindType(VrcNames.DynamicBoneType);
            if (dbType == null)
            {
                ctx.Log.Warning("DynamicBone is not in this project, so PhysBones can't be converted. " +
                                "Import DynamicBone (ChilloutVR's CCK bundles it) and re-run.");
                return;
            }

            var root = ctx.AvatarRoot;

            // 1) Colliders first.
            if (ctx.Options.physBoneColliders)
            {
                var pbcType = Reflect.FindType(VrcNames.PhysBoneColliderType);
                var dbcType = Reflect.FindType(VrcNames.DynamicBoneColliderType);
                if (pbcType != null && dbcType != null)
                {
                    foreach (var pbc in root.GetComponentsInChildren(pbcType, true))
                    {
                        var go = ((Component)pbc).gameObject;
                        var dbc = go.AddComponent(dbcType);
                        CopyFloat(pbc, VrcNames.PBC_Radius, dbc, VrcNames.DBC_Radius);
                        CopyFloat(pbc, VrcNames.PBC_Height, dbc, VrcNames.DBC_Height);
                        if (Reflect.GetField(pbc, VrcNames.PBC_Position) is Vector3 center)
                            Reflect.SetField(dbc, VrcNames.DBC_Center, center);
                        ctx.ColliderMap[(UnityEngine.Object)pbc] = (UnityEngine.Object)dbc;
                    }
                    ctx.Log.Info($"Converted {ctx.ColliderMap.Count} collider(s).");
                }
            }

            // 2) Bones.
            if (ctx.Options.physBones)
            {
                var pbType = Reflect.FindType(VrcNames.PhysBoneType);
                if (pbType == null) return;

                var converted = 0;
                foreach (var pb in root.GetComponentsInChildren(pbType, true))
                {
                    var go = ((Component)pb).gameObject;
                    var db = go.AddComponent(dbType);

                    var rootT = Reflect.GetField(pb, VrcNames.PB_Root) as Transform ?? go.transform;
                    Reflect.SetField(db, VrcNames.DB_Root, rootT);

                    var o = ctx.Options;
                    Reflect.SetField(db, VrcNames.DB_Radius, GetFloat(pb, VrcNames.PB_Radius, 0.05f) * o.pbRadiusScale);

                    // Approximate physics mapping, scaled by the tuning options.
                    // Elasticity ≈ how strongly the bone returns to rest: VRChat 'pull' (fallback 'spring').
                    var pull = GetFloat(pb, VrcNames.PB_Pull, GetFloat(pb, VrcNames.PB_Spring, 0.2f));
                    Reflect.SetField(db, VrcNames.DB_Elasticity, Mathf.Clamp01(pull * o.pbElasticityScale));
                    Reflect.SetField(db, VrcNames.DB_Stiffness, Mathf.Clamp01(GetFloat(pb, VrcNames.PB_Stiffness, 0.2f) * o.pbStiffnessScale));
                    Reflect.SetField(db, VrcNames.DB_Inert, GetFloat(pb, VrcNames.PB_Immobile, 0f));
                    Reflect.SetField(db, VrcNames.DB_Damping, Mathf.Clamp01(o.pbDamping));

                    // VRChat gravity is a scalar pulling down; DynamicBone gravity is a vector.
                    var g = GetFloat(pb, VrcNames.PB_Gravity, 0f) * o.pbGravityScale;
                    Reflect.SetField(db, VrcNames.DB_Gravity, new Vector3(0f, -g, 0f));

                    CopyTransformList(pb, VrcNames.PB_IgnoreTransforms, db, VrcNames.DB_Exclusions);
                    AssignColliders(ctx, pb, db);
                    converted++;
                }
                ctx.Log.Info($"Converted {converted} PhysBone(s) (physics values are approximate — tune as needed).");
            }

            // Optionally delete the original VRChat PhysBone/Collider components (colliders were already
            // copied to the DynamicBones, so their references survive the removal).
            if (ctx.Options.removeOriginalPhysBones)
            {
                int removed = 0;
                foreach (var typeName in new[] { VrcNames.PhysBoneType, VrcNames.PhysBoneColliderType })
                {
                    var t = Reflect.FindType(typeName);
                    if (t == null) continue;
                    foreach (var c in root.GetComponentsInChildren(t, true))
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                        removed++;
                    }
                }
                if (removed > 0) ctx.Log.Info($"Removed {removed} original VRChat PhysBone/Collider component(s).");
            }
        }

        private static void AssignColliders(ConversionContext ctx, object pb, object db)
        {
            var pbColliders = Reflect.AsList(Reflect.GetField(pb, VrcNames.PB_Colliders));
            var dbColliders = Reflect.AsList(Reflect.GetField(db, VrcNames.DB_Colliders));
            if (pbColliders == null || dbColliders == null) return;

            foreach (var pbc in pbColliders)
            {
                if (pbc is UnityEngine.Object key && ctx.ColliderMap.TryGetValue(key, out var dbc))
                    dbColliders.Add(dbc);
            }
        }

        private static void CopyTransformList(object src, string srcField, object dst, string dstField)
        {
            var srcList = Reflect.AsList(Reflect.GetField(src, srcField));
            var dstList = Reflect.AsList(Reflect.GetField(dst, dstField));
            if (srcList == null || dstList == null) return;
            foreach (var t in srcList)
                if (t is Transform tr) dstList.Add(tr);
        }

        private static void CopyFloat(object src, string srcField, object dst, string dstField)
        {
            if (Reflect.GetField(src, srcField) is float f)
                Reflect.SetField(dst, dstField, f);
        }

        private static float GetFloat(object obj, string field, float fallback)
        {
            return Reflect.GetField(obj, field) is float f ? f : fallback;
        }
    }
}
