using System.Collections.Generic;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class BlendshapeLinkBuilder : FeatureBuilder<CVRFuryBlendshapeLink>
    {
        protected override void Build(BuildContext ctx, CVRFuryBlendshapeLink f)
        {
            if (f.sourceMesh == null || f.sourceMesh.sharedMesh == null)
            {
                ctx.Log.Warning("Blendshape Link has no source mesh; skipped.");
                return;
            }

            var exclude = new HashSet<string>(f.excludeBlendshapes ?? new List<string>());
            var srcMesh = f.sourceMesh.sharedMesh;

            // 1) Copy the current static values onto the targets.
            var copied = 0;
            for (var i = 0; i < srcMesh.blendShapeCount; i++)
            {
                var name = srcMesh.GetBlendShapeName(i);
                if (exclude.Contains(name)) continue;

                var weight = f.sourceMesh.GetBlendShapeWeight(i);
                foreach (var target in f.targetMeshes)
                {
                    if (target == null || target.sharedMesh == null) continue;
                    var ti = target.sharedMesh.GetBlendShapeIndex(name);
                    if (ti < 0) continue;
                    target.SetBlendShapeWeight(ti, weight);
                    copied++;
                }
            }
            ctx.Log.Info($"Blendshape Link copied {copied} static value(s) from '{f.sourceMesh.name}'.");

            // 2) Keep it live: for every clip that animates a source blendshape, mirror that curve
            //    onto the targets so toggle/slider-driven shapes stay in sync.
            if (f.keepLive && ctx.Controller != null)
                MirrorAnimatedCurves(ctx, f, exclude);
        }

        private static void MirrorAnimatedCurves(BuildContext ctx, CVRFuryBlendshapeLink f,
                                                 HashSet<string> exclude)
        {
            var srcPath = HierarchyUtil.GetPath(ctx.RootTransform, f.sourceMesh.transform);
            if (srcPath == null) return;

            var targetInfo = new List<(string path, SkinnedMeshRenderer smr)>();
            foreach (var t in f.targetMeshes)
            {
                if (t == null || t.sharedMesh == null) continue;
                var p = HierarchyUtil.GetPath(ctx.RootTransform, t.transform);
                if (p != null) targetInfo.Add((p, t));
            }
            if (targetInfo.Count == 0) return;

            var mirrored = 0;
            foreach (var clip in AnimatorClips.GetAll(ctx.Controller))
            {
                // Snapshot bindings first; we mutate the clip inside the loop.
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var b in bindings)
                {
                    if (b.type != typeof(SkinnedMeshRenderer)) continue;
                    if (b.path != srcPath) continue;
                    if (!b.propertyName.StartsWith("blendShape.")) continue;

                    var shape = b.propertyName.Substring("blendShape.".Length);
                    if (exclude.Contains(shape)) continue;

                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    if (curve == null) continue;

                    foreach (var (path, smr) in targetInfo)
                    {
                        if (smr.sharedMesh.GetBlendShapeIndex(shape) < 0) continue;
                        var tb = EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), b.propertyName);
                        AnimationUtility.SetEditorCurve(clip, tb, curve);
                        mirrored++;
                    }
                }
            }

            if (mirrored > 0)
                ctx.Log.Info($"Blendshape Link mirrored {mirrored} animated curve(s) onto linked meshes.");
        }
    }
}
