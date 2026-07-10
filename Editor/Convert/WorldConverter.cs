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
        private const string CvrObjectSyncType = "ABI.CCK.Components.CVRObjectSync";
        private const string CvrVideoPlayerType = "ABI.CCK.Components.CVRVideoPlayer";
        private const string CvrPortalType = "ABI.CCK.Components.CVRPortalMarker";

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
            Count(sb, VrcNames.ObjectSyncType, "Synced object(s)", "→ CVRObjectSync");
            Count(sb, VrcNames.UnityVideoPlayerType, "SDK video player(s)", "→ CVRVideoPlayer");
            Count(sb, VrcNames.AVProVideoPlayerType, "AVPro video player(s)", "→ CVRVideoPlayer");
            Count(sb, VrcNames.PortalMarkerType, "World portal(s)", "→ CVRPortalMarker (re-link destinations — world IDs differ)");
            Count(sb, VrcNames.AvatarPedestalType, "Avatar pedestal(s)", "— skipped (VRChat avatar IDs don't exist in CVR)");

            AppendUdonPlan(sb);
            sb.AppendLine("\nNote: console spam like \"Could not find drawer type map!\" (EasyEventEditor) is a " +
                          "known VRChat-Worlds-SDK editor bug — harmless, not from your world or CVRFury.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The Udon migration plan: every behaviour classified by intent, grouped into
        /// auto-converted / one-component recipe / needs rework, with the concrete CVR path for each.</summary>
        private static void AppendUdonPlan(System.Text.StringBuilder sb)
        {
            var udon = FindAll(VrcNames.UdonBehaviourType);
            if (udon.Count == 0) return;

            var rows = udon.Select(u => new { Udon = u, Program = ProgramName(u), Intent = UdonIntents.Classify(ProgramName(u), u.gameObject) })
                           .GroupBy(x => (x.Program, x.Intent.Kind))
                           .Select(g => new { g.First().Program, g.First().Intent, Sample = g.First().Udon, N = g.Count() })
                           .OrderBy(x => x.Intent.Auto ? 0 : x.Intent.Kind == "Custom logic" ? 2 : 1)
                           .ThenByDescending(x => x.N)
                           .ToList();

            int auto = rows.Where(x => x.Intent.Auto).Sum(x => x.N);
            int recipe = rows.Where(x => !x.Intent.Auto && x.Intent.Kind != "Custom logic").Sum(x => x.N);
            int manual = rows.Where(x => x.Intent.Kind == "Custom logic").Sum(x => x.N);

            sb.AppendLine($"  • {udon.Count} Udon behaviour(s) → migration plan: {auto} auto-convert · " +
                          $"{recipe} rebuild as CVRInteractable (toggle-style ones are wired automatically " +
                          $"at Convert) · {manual} need rework:");
            foreach (var x in rows.Take(25))
            {
                var mark = x.Intent.Auto ? "✓" : x.Intent.Kind == "Custom logic" ? "✗" : "→";
                var sure = x.Intent.Confident ? "" : " (guess — verify)";
                sb.AppendLine($"      {mark} {x.N}× {x.Program} [{x.Intent.Kind}{sure}]: {x.Intent.CvrPath}");

                // For toggle-style behaviours the serialized variables usually name the exact targets —
                // surface them so the plan reads "this button toggles THAT", not just "a toggle exists".
                if (ToggleLikeIntents.Contains(x.Intent.Kind))
                {
                    var targets = UdonVariables.SceneReferences(x.Sample).Select(r => r.target.name).Distinct().Take(5).ToList();
                    if (targets.Count > 0)
                        sb.AppendLine($"          targets: {string.Join(", ", targets)}");
                }
            }
            if (rows.Count > 25) sb.AppendLine($"      … {rows.Count - 25} more program(s)");
        }

        /// <summary>Convert the open scene in place (caller is responsible for using a scene COPY).</summary>
        public static string Convert(bool stripAfter, bool rebuildToggles = true)
        {
            var log = new BuildLog();
            int converted = 0;

            converted += ConvertDescriptor(log);
            converted += Swap(VrcNames.MirrorType, CvrMirrorType, "mirror", log,
                              copy: (src, dst) => CopyAny(src, dst, "m_ReflectLayers"));
            converted += Swap(VrcNames.PickupType, CvrPickupType, "pickup", log, copy: null);
            converted += Swap(VrcNames.StationType, CvrSeatType, "seat", log, copy: null);
            converted += Swap(VrcNames.ObjectSyncType, CvrObjectSyncType, "synced object", log, copy: null);
            converted += Swap(VrcNames.UnityVideoPlayerType, CvrVideoPlayerType, "video player", log, copy: null);
            converted += Swap(VrcNames.AVProVideoPlayerType, CvrVideoPlayerType, "AVPro video player", log, copy: null);
            converted += Swap(VrcNames.PortalMarkerType, CvrPortalType, "world portal", log, copy: null);
            converted += ConvertUdonVideoPlayers(log);
            if (rebuildToggles) converted += ConvertUdonToggles(log);

            var pedestals = FindAll(VrcNames.AvatarPedestalType);
            if (pedestals.Count > 0)
                log.Warning($"{pedestals.Count} avatar pedestal(s) skipped — VRChat avatar IDs don't exist in CVR.");

            ReportUdonPlan(log);

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

        /// <summary>Udon-based video players (USharpVideo, ProTV, iwaSync, VideoTXL…) have no SDK component
        /// to swap — recognise them by intent and put a CVRVideoPlayer on the same object, so the user only
        /// has to assign the screen renderer/audio source in one inspector instead of rebuilding the player.</summary>
        private static int ConvertUdonVideoPlayers(BuildLog log)
        {
            var t = Reflect.FindType(CvrVideoPlayerType);
            if (t == null) return 0;

            int n = 0;
            foreach (var u in FindAll(VrcNames.UdonBehaviourType))
            {
                var intent = UdonIntents.Classify(ProgramName(u), u.gameObject);
                if (intent.Kind != "Video player") continue;
                if (u.GetComponent(t) != null) continue; // one per object even if the prefab stacks scripts
                Undo.AddComponent(u.gameObject, t);
                n++;
            }
            if (n > 0)
                log.Info($"{n} Udon video player(s) → CVRVideoPlayer on the same object — open each and assign " +
                         "its screen renderer / audio source (the Udon script can't be read across platforms).");
            return n;
        }

        // Intents whose behaviour boils down to "set these GameObjects (in)active on use" — the pattern
        // CVRInteractable's set-active operation reproduces directly.
        private static readonly string[] ToggleLikeIntents = { "Object toggle", "Mirror toggle", "Light control", "Audio control" };

        /// <summary>Rebuild recognised toggle-style Udon behaviours as ready-made CVRInteractables: the
        /// target objects are read out of the behaviour's public variables (plain serialized data), so the
        /// new interactable drives the SAME objects the Udon button did. Anything that can't be fully wired
        /// still gets its targets printed — turning "recreate this by hand" into a 30-second inspector job.</summary>
        private static int ConvertUdonToggles(BuildLog log)
        {
            if (!InteractableBuilder.Available) return 0;

            int wired = 0, partial = 0;
            foreach (var u in FindAll(VrcNames.UdonBehaviourType))
            {
                var intent = UdonIntents.Classify(ProgramName(u), u.gameObject);
                if (!ToggleLikeIntents.Contains(intent.Kind)) continue;

                var targets = UdonVariables.SceneReferences(u).Select(r => r.target).Distinct().ToList();
                if (targets.Count == 0)
                {
                    log.Warning($"'{u.gameObject.name}' ({intent.Kind}, {ProgramName(u)}): no scene targets found " +
                                "in its variables — recreate by hand (CVRInteractable → Set GameObject Active).");
                    continue;
                }

                if (InteractableBuilder.AddToggle(u.gameObject, targets, out var detail))
                {
                    log.Info($"'{u.gameObject.name}' ({intent.Kind}) → CVRInteractable, {detail}.");
                    wired++;
                }
                else
                {
                    log.Warning($"'{u.gameObject.name}' ({intent.Kind}): CVRInteractable placed but not fully " +
                                $"wired ({detail}). Its targets were: {string.Join(", ", targets.Select(g => g.name))} " +
                                "— add a Set-GameObject-Active operation with those in the inspector.");
                    partial++;
                }
            }
            if (wired + partial > 0)
                log.Info($"Udon toggles → CVRInteractable: {wired} fully wired, {partial} placed for manual finish.");
            return wired;
        }

        /// <summary>Post-convert TODO list: what the remaining Udon behaviours were FOR and the CVR recipe
        /// for each, so "interactivity" stops being a black box.</summary>
        private static void ReportUdonPlan(BuildLog log)
        {
            var udon = FindAll(VrcNames.UdonBehaviourType);
            if (udon.Count == 0) return;

            var groups = udon.Select(u => UdonIntents.Classify(ProgramName(u), u.gameObject))
                             .GroupBy(i => i.Kind)
                             .OrderByDescending(g => g.Count())
                             .ToList();
            log.Warning($"{udon.Count} Udon behaviour(s) can't carry over their scripts — your rebuild list " +
                        "(each has a one-component CVR recipe unless marked otherwise):");
            foreach (var g in groups)
                log.Warning($"  {g.Count()}× {g.Key}: {g.First().CvrPath}");
        }

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
