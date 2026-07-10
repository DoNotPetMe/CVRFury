using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Read-only "is this scene upload-ready as a ChilloutVR world?" check — the world-side counterpart of
    /// the avatar pre-flight. Catches, before the CCK build, the problems that otherwise appear as a failed
    /// upload or a broken world in-game: no CVRWorld/spawns, spawns with no floor under them (players fall
    /// forever), spawns already below the respawn height (instant respawn loop), leftover VRChat/Udon
    /// components, missing scripts, shader errors, and a world with no light source at all.
    /// </summary>
    internal static class WorldPreflight
    {
        public struct Result { public bool ok; public string label; public string detail; }

        private const string CvrWorldType = "ABI.CCK.Components.CVRWorld";

        public static List<Result> Run()
        {
            var r = new List<Result>();
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            // CVRWorld + spawns — the CCK refuses to build without them.
            var worldT = Reflect.FindType(CvrWorldType);
            Component world = null;
            if (worldT == null)
                r.Add(Bad("CVRWorld", "ChilloutVR CCK (world support) isn't loaded in this project"));
            else
            {
                world = roots.SelectMany(g => g.GetComponentsInChildren(worldT, true).OfType<Component>()).FirstOrDefault();
                if (world == null)
                    r.Add(Bad("CVRWorld", "no CVRWorld component in the scene — run Convert first"));
                else
                    r.Add(Ok("CVRWorld", "present"));
            }

            // Spawn sanity: exist, have ground under them, and sit above the respawn height.
            if (world != null)
            {
                var spawnObjs = SpawnObjects(world);
                if (spawnObjs.Count == 0)
                    r.Add(Bad("Spawns", "CVRWorld has no spawn points — players would spawn at the origin"));
                else
                {
                    float respawnY = -25f;
                    var rh = Reflect.GetField(world, "respawnHeightY");
                    if (rh is float f) respawnY = f;

                    int noGround = 0, belowRespawn = 0;
                    foreach (var s in spawnObjs)
                    {
                        if (s == null) continue;
                        var p = s.transform.position;
                        if (p.y <= respawnY) belowRespawn++;
                        // A player standing here must have a floor: cast from just above the spawn downwards.
                        if (!Physics.Raycast(p + Vector3.up, Vector3.down, 200f)) noGround++;
                    }
                    if (belowRespawn > 0)
                        r.Add(Bad("Spawns", $"{belowRespawn} spawn(s) are BELOW the respawn height ({respawnY}) — instant respawn loop"));
                    else if (noGround > 0)
                        r.Add(Bad("Spawns", $"{noGround} of {spawnObjs.Count} spawn(s) have no collider beneath them — players fall forever"));
                    else
                        r.Add(Ok("Spawns", $"{spawnObjs.Count} spawn(s), all with ground, above respawn height"));
                }
            }

            // Leftover VRChat/Udon components break the CCK build (their scripts don't exist in CVR).
            int vrc = 0;
            foreach (var root in roots)
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var fn = c.GetType().FullName ?? "";
                    if (fn.StartsWith("VRC.") || fn.StartsWith("VRCSDK") || fn.StartsWith("UdonSharp.")) vrc++;
                }
            r.Add(vrc == 0 ? Ok("VRChat leftovers", "none")
                           : Bad("VRChat leftovers", $"{vrc} VRChat/Udon component(s) still in the scene — convert with Strip enabled"));

            // Missing scripts.
            int missing = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                               .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            r.Add(missing == 0 ? Ok("Missing scripts", "none")
                               : Bad("Missing scripts", $"{missing} broken component(s) — strip removes these too"));

            // Shaders compile.
            var badShaders = new HashSet<string>();
            var seen = new HashSet<Shader>();
            foreach (var root in roots)
                foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in rend.sharedMaterials)
                    {
                        if (m == null || m.shader == null || !seen.Add(m.shader)) continue;
                        if (ShaderUtil.GetShaderMessages(m.shader).Any(msg =>
                                msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                            badShaders.Add(m.shader.name);
                    }
            r.Add(badShaders.Count == 0 ? Ok("Shaders compile", "all OK")
                : Bad("Shaders compile", $"{badShaders.Count} failing — {string.Join("; ", badShaders.Take(3))}"));

            // Light sanity: baked lightmaps OR at least one enabled realtime light, else the world loads dark.
            bool hasLightmaps = LightmapSettings.lightmaps != null && LightmapSettings.lightmaps.Length > 0;
            bool hasLight = roots.SelectMany(g => g.GetComponentsInChildren<Light>(true))
                                 .Any(l => l.enabled && l.gameObject.activeInHierarchy);
            r.Add(hasLightmaps || hasLight ? Ok("Lighting", hasLightmaps ? "baked lightmaps present" : "realtime light(s) present")
                                           : Bad("Lighting", "no lightmaps and no active light — the world will render dark"));

            // The CCK builds the SAVED scene; unsaved edits silently don't ship.
            r.Add(!scene.isDirty && !string.IsNullOrEmpty(scene.path)
                ? Ok("Scene saved", scene.path)
                : Bad("Scene saved", string.IsNullOrEmpty(scene.path) ? "scene was never saved" : "unsaved changes — save before uploading"));

            return r;
        }

        public static string Report(out bool allOk)
        {
            var results = Run();
            allOk = results.All(x => x.ok);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(allOk ? "World is upload-ready — all checks pass." : "Not ready — fix the ✗ items:");
            foreach (var x in results)
                sb.AppendLine($"  {(x.ok ? "✓" : "✗")} {x.label}: {x.detail}");
            return sb.ToString().TrimEnd();
        }

        // CVRWorld stores spawns as GameObject[] on current CCKs; tolerate Transform[] as well.
        private static List<GameObject> SpawnObjects(Component world)
        {
            var res = new List<GameObject>();
            var v = Reflect.GetField(world, "spawns");
            if (v is IEnumerable<GameObject> gos) res.AddRange(gos.Where(g => g != null));
            else if (v is IEnumerable<Transform> ts) res.AddRange(ts.Where(t => t != null).Select(t => t.gameObject));
            return res;
        }

        private static Result Ok(string label, string detail) => new Result { ok = true, label = label, detail = detail };
        private static Result Bad(string label, string detail) => new Result { ok = false, label = label, detail = detail };
    }
}
