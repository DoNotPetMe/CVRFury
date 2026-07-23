using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Generates a "nipple poke" blendshape on a CLOTHING mesh procedurally — makes the shirt bump outward at
    /// points you place, so the nipple reads through it. Unity can't sculpt, but this does the same thing by
    /// math: for every vertex within a radius of each marker it pushes the vertex OUTWARD along its normal
    /// with a smooth cosine falloff (full at the marker, zero at the edge), and bakes that displacement as a
    /// blendshape ("CVRFuryNipplePoke") on a NEW mesh asset (the original is never touched). You then drive
    /// the blendshape with a toggle or slider.
    ///
    /// Marker positions are matched to vertices via the CURRENT skinned pose (BakeMesh), so wherever the
    /// marker sits on the clothing in the scene is where the bump appears — even though the displacement is
    /// applied in the mesh's own space where blendshapes live.
    /// </summary>
    internal static class NippleBumpGenerator
    {
        private const string ShapeName = "CVRFuryNipplePoke";
        private const string BakeDir = "Assets/CVRFury Generated/Bumps";

        public static string Generate(SkinnedMeshRenderer smr, Mesh original, IList<Vector3> markerWorldPos,
                                      IList<float> markerRadii, float strength, out Mesh result)
        {
            result = null;
            if (smr == null) return "Pick the clothing mesh (its Skinned Mesh Renderer).";
            if (original == null) original = smr.sharedMesh;
            if (original == null) return "That renderer has no mesh.";
            if (markerWorldPos == null || markerWorldPos.Count == 0)
                return "Place at least one marker on the clothing first.";

            // Current skinned positions (renderer-local) to map markers → vertex indices.
            var baked = new Mesh();
            smr.BakeMesh(baked);
            var bakedVerts = baked.vertices;
            var srcVerts = original.vertices;
            var srcNormals = original.normals;
            int n = srcVerts.Length;
            if (bakedVerts.Length != n)
                return "Baked/source vertex counts differ — can't map (is the mesh readable / not optimized away?).";

            var toLocal = smr.transform.worldToLocalMatrix;
            var centers = markerWorldPos.Select(w => toLocal.MultiplyPoint3x4(w)).ToArray();

            var delta = new Vector3[n];
            int affected = 0;
            for (int i = 0; i < n; i++)
            {
                float bestF = 0f;
                for (int m = 0; m < centers.Length; m++)
                {
                    float rad = m < markerRadii.Count ? markerRadii[m] : 0.03f;
                    float d = Vector3.Distance(bakedVerts[i], centers[m]);
                    if (d >= rad) continue;
                    float f = 0.5f * (Mathf.Cos(Mathf.PI * d / rad) + 1f); // 1 at centre → 0 at edge
                    bestF = Mathf.Max(bestF, f);                            // overlapping markers take the max
                }
                if (bestF <= 0f) continue;
                var nrm = i < srcNormals.Length ? srcNormals[i] : Vector3.up;
                delta[i] = nrm * strength * bestF;
                affected++;
            }
            Object.DestroyImmediate(baked);
            if (affected == 0)
                return "No vertices fell inside the marker(s) — move them onto the clothing surface or raise the radius.";

            // Rebuild from the ORIGINAL each time (Unity can't remove a blendshape), preserving existing ones.
            var mesh = Object.Instantiate(original);
            mesh.name = original.name + " (CVRFury Bump)";
            // Drop any prior poke frame by rebuilding without it (Instantiate of original already excludes it
            // when 'original' is the true source; if the caller passed a bumped mesh, strip by name).
            if (HasShape(mesh, ShapeName)) mesh = RebuildWithout(mesh, ShapeName);
            mesh.AddBlendShapeFrame(ShapeName, 100f, delta, null, null);

            if (!AssetDatabase.IsValidFolder("Assets/CVRFury Generated"))
                AssetDatabase.CreateFolder("Assets", "CVRFury Generated");
            if (!AssetDatabase.IsValidFolder(BakeDir))
                AssetDatabase.CreateFolder("Assets/CVRFury Generated", "Bumps");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{BakeDir}/{Sanitize(mesh.name)}.asset");
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(smr, "Nipple bump");
            smr.sharedMesh = mesh;
            // Preview it immediately at full strength.
            var idx = mesh.GetBlendShapeIndex(ShapeName);
            if (idx >= 0) smr.SetBlendShapeWeight(idx, 100f);
            EditorUtility.SetDirty(smr);

            result = mesh;
            return $"Baked '{ShapeName}' — {affected} vertices pushed out (previewing at 100). Adjust marker " +
                   "position/radius/strength and regenerate to taste, then add a toggle or slider on this mesh " +
                   $"for the '{ShapeName}' blendshape to control it in-game. Original mesh untouched.";
        }

        private static bool HasShape(Mesh m, string name)
        {
            for (int i = 0; i < m.blendShapeCount; i++) if (m.GetBlendShapeName(i) == name) return true;
            return false;
        }

        // Rebuild a mesh copying every blendshape EXCEPT the named one (Unity offers no direct removal).
        private static Mesh RebuildWithout(Mesh src, string skip)
        {
            var m = Object.Instantiate(src);
            m.ClearBlendShapes();
            var verts = src.vertexCount;
            for (int s = 0; s < src.blendShapeCount; s++)
            {
                var sn = src.GetBlendShapeName(s);
                if (sn == skip) continue;
                int frames = src.GetBlendShapeFrameCount(s);
                for (int f = 0; f < frames; f++)
                {
                    var dv = new Vector3[verts]; var dn = new Vector3[verts]; var dt = new Vector3[verts];
                    src.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    m.AddBlendShapeFrame(sn, src.GetBlendShapeFrameWeight(s, f), dv, dn, dt);
                }
            }
            m.name = src.name;
            return m;
        }

        private static string Sanitize(string s) =>
            string.Join("_", s.Split(System.IO.Path.GetInvalidFileNameChars()));
    }
}
