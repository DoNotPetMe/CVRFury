using System.Collections.Generic;
using CVRFury.Components;
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

            ctx.Log.Info($"Blendshape Link copied {copied} blendshape value(s) from " +
                         $"'{f.sourceMesh.name}'.");

            if (f.keepLive)
                ctx.Log.Warning("Blendshape Link 'Keep Live' (animator-driven mirroring) is planned " +
                                "but not yet implemented; values were baked once. See ROADMAP.");
        }
    }
}
