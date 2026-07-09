using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// VRChat world → ChilloutVR world conversion (beta). Same architecture that made the avatar converter
    /// dependable: everything by reflection (no compile-time SDK/CCK dependency), tolerant member lookup
    /// (SDK field names drift between generations), convert what has a CVR equivalent, and REPORT — never
    /// silently drop — what doesn't yet.
    ///
    /// v1 converts the structural layer: scene descriptor → CVRWorld (spawns, reference camera, respawn
    /// height), mirrors → CVRMirror, pickups → CVRPickupObject, stations → CVRSeat; inventories every Udon
    /// behaviour by program name so the interactivity surface is visible; and optionally strips the VRChat
    /// components afterwards. Udon → CVR interactables/scripting is the next layer and lands on top of the
    /// inventory this produces.
    /// </summary>
    internal static class WorldConverter
    {
        // CCK world-side type names (resolved by reflection; each converter skips with a log if absent).
        private const string CvrWorldType = "ABI.CCK.Components.CVRWorld";
        private const string CvrMirrorType = "ABI.CCK.Components.CVRMirror";
        private const string CvrPickupType = "ABI.CCK.Components.CVRPickupObject";
        private const string CvrSeatType = "ABI.CCK.Components.CVRSeat";

        /// <summary>Read-only inventory of the open scene: what will convert, what won't (yet).</summary>
        public static string Scan()
        {
            var sb = new System.Text.StringBuilder();
            if (Reflect.FindType(VrcNames.SceneDescriptorType) == null &&
                Reflect.FindType(VrcNames.UdonBehaviourType) == null)
                return "VRChat Worlds SDK types aren't loaded — open this scene in a project that has the " +
                       "Worlds SDK imported so the components can be read.";

            sb.AppendLine("Scene inventory:");
            Count(sb, VrcNames.SceneDescriptorType, "Scene descriptor", "→ CVRWorld (spawns, camera, respawn height)");
            Count(sb, VrcNames.MirrorType, "Mirror(s)", "→ CVRMirror");
            Count(sb, VrcNames.PickupType, "Pickup(s)", "→ CVRPickupObject");
            Count(sb, VrcNames.StationType, "Station/chair(s)", "→ CVRSeat");
            Count(sb, VrcNames.AvatarPedestalType, "Avatar pedestal(s)", "— skipped (VRChat avatar IDs don't exist in CVR)");

            var udon = FindAll(VrcNames.UdonBehaviourType);
            if (udon.Count > 0)
            {
                var byProgram = udon.GroupBy(u => ProgramName(u)).OrderByDescending(g => g.Count());
                sb.AppendLine($"  • {udon.Count} Udon behaviour(s) — inventoried, not yet auto-translated:");
                foreach (var g in byProgram.Take(15))
                    sb.AppendLine($"      {g.Count()}× {g.Key}");
                if (byProgram.Count() > 15) sb.AppendLine("      …");
                sb.AppendLine("    Interactivity needs CVR's interactables/scripting — that's the next layer, " +
                              "built on this inventory. Simple worlds (geometry, mirrors, chairs, pickups) " +
                              "already convert fully.");
            }
            sb.AppendLine("\nNote: console spam like \"Could not find drawer type map!\" (EasyEventEditor) is a " +
                          "known VRChat-Worlds-SDK editor bug — harmless, not from your world or CVRFury.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Convert the open scene in place (caller is responsible for using a scene COPY).</summary>
        public static string Convert(bool stripAfter)
        {
            var log = new BuildLog();
            int converted = 0;

            converted += ConvertDescriptor(log);
            converted += Swap(VrcNames.MirrorType, CvrMirrorType, "mirror", log,
                              copy: (src, dst) => CopyAny(src, dst, "m_ReflectLayers"));
            converted += Swap(VrcNames.PickupType, CvrPickupType, "pickup", log, copy: null);
            converted += Swap(VrcNames.StationType, CvrSeatType, "seat", log, copy: null);

            var pedestals = FindAll(VrcNames.AvatarPedestalType);
            if (pedestals.Count > 0)
                log.Warning($"{pedestals.Count} avatar pedestal(s) skipped — VRChat avatar IDs don't exist in CVR.");

            var udon = FindAll(VrcNames.UdonBehaviourType);
            if (udon.Count > 0)
                log.Warning($"{udon.Count} Udon behaviour(s) left in place (inventoried, not yet translated). " +
                            "Their objects keep working as static props; interactivity needs the next layer.");

            if (stripAfter)
            {
                int stripped = 0;
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (var c in root.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null) continue;
                        var fn = c.GetType().FullName ?? "";
                        if (fn.StartsWith("VRC.") || fn.StartsWith("VRCSDK") || fn.StartsWith("UdonSharp."))
                        { Object.DestroyImmediate(c); stripped++; }
                    }
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    MissingScriptCleaner.RemoveInHierarchy(root);
                log.Info($"Stripped {stripped} VRChat/Udon component(s) + missing scripts.");
            }

            log.Info($"World conversion done: {converted} component(s) converted. Add the scene to Build " +
                     "Settings and upload through the CCK's world flow.");
            return string.Join("\n", log.Entries.Select(e =>
                (e.Level == BuildLog.Level.Error ? "✗ " : e.Level == BuildLog.Level.Warning ? "! " : "• ") + e.Message));
        }

        // --- steps -------------------------------------------------------------------------------

        private static int ConvertDescriptor(BuildLog log)
        {
            var descs = FindAll(VrcNames.SceneDescriptorType);
            if (descs.Count == 0) { log.Warning("No VRC Scene Descriptor in this scene."); return 0; }
            var cvrWorldT = Reflect.FindType(CvrWorldType);
            if (cvrWorldT == null)
            { log.Error("ChilloutVR CCK (CVRWorld) not found — import the CCK with world support."); return 0; }

            var desc = descs[0];
            var go = desc.gameObject;
            var world = go.GetComponent(cvrWorldT);
            if (world == null) world = Undo.AddComponent(go, cvrWorldT); // '??' breaks on Unity's fake-null

            // Spawns: VRChat stores Transform[]; CVR wants GameObject[] (tolerate either shape).
            if (GetAny(desc, VrcNames.World_Spawns) is IEnumerable<Transform> spawnTs)
            {
                var gos = spawnTs.Where(t => t != null).Select(t => t.gameObject).ToArray();
                if (!Reflect.SetField(world, "spawns", gos))
                    Reflect.SetField(world, "spawns", spawnTs.Where(t => t != null).ToArray());
                log.Info($"Spawns: {gos.Length} copied.");
            }
            if (GetAny(desc, VrcNames.World_ReferenceCamera) is GameObject cam)
                if (Reflect.SetField(world, "referenceCamera", cam)) log.Info("Reference camera copied.");
            var rh = GetAny(desc, VrcNames.World_RespawnHeight);
            if (rh != null)
                if (Reflect.SetField(world, "respawnHeightY", rh)) log.Info($"Respawn height copied ({rh}).");

            log.Info("Scene descriptor → CVRWorld.");
            return 1;
        }

        /// <summary>Replace every component of a VRChat type with a CVR one on the same GameObject.</summary>
        private static int Swap(string vrcType, string cvrType, string label, BuildLog log,
                                System.Action<Component, Component> copy)
        {
            var found = FindAll(vrcType);
            if (found.Count == 0) return 0;
            var t = Reflect.FindType(cvrType);
            if (t == null) { log.Warning($"{found.Count} {label}(s) found but the CCK type {cvrType} isn't loaded; skipped."); return 0; }

            int n = 0;
            foreach (var src in found)
            {
                var go = src.gameObject;
                var dst = go.GetComponent(t);
                if (dst == null) dst = Undo.AddComponent(go, t); // '??' breaks on Unity's fake-null
                copy?.Invoke(src, dst);
                n++;
            }
            log.Info($"{n} {label}(s) → {t.Name}.");
            return n;
        }

        // --- helpers -----------------------------------------------------------------------------

        private static List<Component> FindAll(string typeName)
        {
            var res = new List<Component>();
            var t = Reflect.FindType(typeName);
            if (t == null) return res;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                res.AddRange(root.GetComponentsInChildren(t, true).OfType<Component>());
            return res;
        }

        private static void Count(System.Text.StringBuilder sb, string typeName, string label, string fate)
        {
            var n = FindAll(typeName).Count;
            if (n > 0) sb.AppendLine($"  • {n} {label} {fate}");
        }

        private static object GetAny(object src, string[] names)
        {
            foreach (var n in names)
            {
                var v = Reflect.GetField(src, n) ?? Reflect.GetProperty(src, n);
                if (v != null) return v;
            }
            return null;
        }

        private static void CopyAny(Component src, Component dst, params string[] fieldNames)
        {
            if (src == null || dst == null) return;
            foreach (var f in fieldNames)
            {
                var v = Reflect.GetField(src, f);
                if (v != null) Reflect.SetField(dst, f, v);
            }
        }

        private static string ProgramName(Component udon)
        {
            var prog = Reflect.GetField(udon, VrcNames.Udon_ProgramSource) as Object;
            return prog != null ? prog.name : "(no program)";
        }
    }
}
