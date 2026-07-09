using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Permanently bakes avatar-lock keys (Gonso / Kanna-Protecc-style blendshape keys) into the mesh so the
    /// avatar renders correctly on platforms that never receive the keys as runtime parameters (ChilloutVR).
    ///
    /// IMPORTANT PROPERTY: this cannot unlock anything. It has no knowledge of keys — it only bakes the
    /// CURRENT scene deformation into the vertices. That deformation is only correct when the owner's paid,
    /// working key is already applied in the editor; without it, the scene shows scramble and baking would
    /// just produce a permanently scrambled mesh. Owning the key IS the gate.
    ///
    /// Mechanics: for each selected non-zero blendshape, its weighted deltas are added into the base
    /// vertices/normals/tangents; the baked shapes are removed; every other blendshape is preserved (with
    /// its current weight re-applied by name). The result is saved as a NEW mesh asset — the original mesh
    /// is never modified.
    /// </summary>
    internal static class KeyBaker
    {
        /// <summary>Meshes with bakeable non-zero shapes: (renderer, shape indices, looks-like-key flags).</summary>
        public static List<(SkinnedMeshRenderer smr, List<int> shapes)> Detect(GameObject avatar, bool keyLikeOnly)
        {
            var res = new List<(SkinnedMeshRenderer, List<int>)>();
            if (avatar == null) return res;
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var idx = new List<int>();
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    if (Mathf.Abs(smr.GetBlendShapeWeight(i)) < 0.0001f) continue;
                    if (keyLikeOnly && !LooksLikeKey(mesh.GetBlendShapeName(i))) continue;
                    idx.Add(i);
                }
                if (idx.Count > 0) res.Add((smr, idx));
            }
            return res;
        }

        public static string Bake(GameObject avatar, bool keyLikeOnly)
        {
            var targets = Detect(avatar, keyLikeOnly);
            if (targets.Count == 0)
                return keyLikeOnly
                    ? "No key-like non-zero blendshapes found. If the lock system uses differently-named " +
                      "shapes, enable \"bake ALL non-zero blendshapes\" — but review the list first."
                    : "No non-zero blendshapes found to bake.";

            const string dir = "Assets/CVRFury Baked";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "CVRFury Baked");

            int meshes = 0, shapesBaked = 0;
            var report = new System.Text.StringBuilder();
            foreach (var (smr, shapes) in targets)
            {
                var src = smr.sharedMesh;
                var bakedSet = new HashSet<int>(shapes);

                // Remember every OTHER shape's current weight by NAME (indices shift after rebuilding).
                var keepWeights = new List<(string name, float w)>();
                for (int i = 0; i < src.blendShapeCount; i++)
                    if (!bakedSet.Contains(i))
                        keepWeights.Add((src.GetBlendShapeName(i), smr.GetBlendShapeWeight(i)));

                var baked = Object.Instantiate(src);
                baked.name = src.name + " (keys baked)";

                // Accumulate the selected shapes' weighted deltas into the base geometry.
                var verts = baked.vertices;
                var norms = baked.normals;
                var tans = baked.tangents;
                foreach (var i in shapes)
                {
                    float w = smr.GetBlendShapeWeight(i);
                    int lastFrame = src.GetBlendShapeFrameCount(i) - 1;
                    float fw = src.GetBlendShapeFrameWeight(i, lastFrame);
                    float scale = fw > 0.0001f ? w / fw : 0f;
                    var dv = new Vector3[src.vertexCount];
                    var dn = new Vector3[src.vertexCount];
                    var dt = new Vector3[src.vertexCount];
                    src.GetBlendShapeFrameVertices(i, lastFrame, dv, dn, dt);
                    for (int v = 0; v < verts.Length; v++)
                    {
                        verts[v] += dv[v] * scale;
                        if (norms.Length == verts.Length) norms[v] += dn[v] * scale;
                        if (tans.Length == verts.Length)
                        {
                            var t = tans[v];
                            tans[v] = new Vector4(t.x + dt[v].x * scale, t.y + dt[v].y * scale, t.z + dt[v].z * scale, t.w);
                        }
                    }
                    shapesBaked++;
                }
                baked.vertices = verts;
                if (norms.Length == verts.Length) baked.normals = norms;
                if (tans.Length == verts.Length) baked.tangents = tans;

                // Rebuild the blendshape list without the baked ones.
                baked.ClearBlendShapes();
                for (int i = 0; i < src.blendShapeCount; i++)
                {
                    if (bakedSet.Contains(i)) continue;
                    var name = src.GetBlendShapeName(i);
                    for (int f = 0; f < src.GetBlendShapeFrameCount(i); f++)
                    {
                        var dv = new Vector3[src.vertexCount];
                        var dn = new Vector3[src.vertexCount];
                        var dt = new Vector3[src.vertexCount];
                        src.GetBlendShapeFrameVertices(i, f, dv, dn, dt);
                        baked.AddBlendShapeFrame(name, src.GetBlendShapeFrameWeight(i, f), dv, dn, dt);
                    }
                }
                baked.RecalculateBounds();

                var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{Sanitize(baked.name)}.asset");
                AssetDatabase.CreateAsset(baked, path);

                Undo.RecordObject(smr, "Bake lock keys");
                smr.sharedMesh = baked;
                foreach (var (name, w) in keepWeights)
                {
                    var ni = baked.GetBlendShapeIndex(name);
                    if (ni >= 0) smr.SetBlendShapeWeight(ni, w);
                }
                EditorUtility.SetDirty(smr);

                report.AppendLine($"  • {smr.name}: baked {shapes.Count} shape(s) → {path}");
                meshes++;
            }
            AssetDatabase.SaveAssets();

            return $"Baked {shapesBaked} key shape(s) into {meshes} new mesh asset(s) — originals untouched:\n" +
                   report +
                   "The avatar now renders this look with NO runtime keys needed. Verify it still looks right " +
                   "in the scene, then upload to CVR. (To revert: reassign the original mesh on the renderer.)";
        }

        // Key shapes tend to be generated names: hex-ish strings, "Key…", long no-vowel tokens.
        public static bool LooksLikeKey(string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) return false;
            var lower = n.ToLowerInvariant();
            if (lower.StartsWith("key") || lower.Contains("crypt") || lower.Contains("protec") ||
                lower.Contains("gonso") || lower.Contains("lock")) return true;
            if (n.Length >= 8 && n.All(ch => "0123456789abcdefABCDEF_-".IndexOf(ch) >= 0)) return true;
            int letters = n.Count(char.IsLetter), vowels = lower.Count(c => "aeiou".IndexOf(c) >= 0);
            return letters >= 8 && vowels == 0;
        }

        private static string Sanitize(string s) =>
            string.Join("_", s.Split(System.IO.Path.GetInvalidFileNameChars()));
    }
}
