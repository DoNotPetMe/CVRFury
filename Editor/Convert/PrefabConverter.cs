using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// The Prefab Converter: reads VRCFury components (toys, clothing, avatar additions — anything built on
    /// VRCFury) BY REFLECTION and recreates their intent as CVRFury components, which the proven builders
    /// bake for ChilloutVR at upload.
    ///
    /// Ground rules, mirroring the VRChat converter: no compile-time dependency (VRCFury classes are
    /// <c>internal</c> — everything resolves by full type name), convert what has a CVR equivalent, REPORT —
    /// never silently drop — what doesn't. Field names and semantics below come from VRCFury's published
    /// source: the container is <c>VF.Model.VRCFury</c> holding ONE feature in <c>[SerializeReference]
    /// content</c> (legacy multi-feature <c>config.features</c> still read via <c>GetAllFeatures()</c>);
    /// features dispatch on concrete type name; asset refs serialize as GuidWrapper {objRef, id="guid:fileID"}.
    /// Unity upgrades old data on load (OnAfterDeserialize), so in-editor reads always see the LATEST schema.
    /// </summary>
    internal static class PrefabConverter
    {
        private const string VrcFuryType = "VF.Model.VRCFury";

        // Features that are VRChat-platform machinery with no CVR meaning — reported with a reason.
        private static readonly Dictionary<string, string> VrchatOnly = new Dictionary<string, string>
        {
            { "SecurityLock", "PIN security uses VRChat params — no CVR equivalent" },
            { "SecurityRestricted", "PIN security uses VRChat params — no CVR equivalent" },
            { "UnlimitedParameters", "VRC parameter compression — CVR has its own (larger) budget" },
            { "AvatarScale2", "VRC scale params — CVR scales avatars natively (or use a Height dropdown)" },
            { "ShowInFirstPerson", "uses VRCHeadChop — not applicable in CVR" },
            { "HeadChopHead", "uses VRCHeadChop — not applicable in CVR" },
            { "MmdCompatibility", "MMD world compatibility is VRChat-specific" },
            { "Talking", "driven by VRChat's Viseme parameter — no direct CVR analog yet" },
            { "SetIcon", "VRC menu icons — CVR menus don't take per-item icons this way" },
            { "OverrideMenuSettings", "VRC menu paging — not applicable" },
            { "AdvancedCollider", "VRC finger/hand collider config — not applicable" },
            { "ConstraintRetarget", "retargets VRC constraints — strip converts these separately" },
            { "DirectTreeOptimizer", "VRC animator cost optimization — harmless to drop" },
            { "CrossEyeFix2", "VRC eye-look fix — CVR eye look differs" },
            { "DescriptorDebug", "debug feature" },
        };

        // Build-time hygiene features that are safe to ignore (CVR path exists or nothing needed).
        private static readonly HashSet<string> Hygiene = new HashSet<string>
        {
            "AnchorOverrideFix2", "BoundingBoxFix2", "BlendshapeOptimizer", "FixWriteDefaults",
            "RemoveHandGestures2", "Slot4Fix", "Gizmo", "RemoveBlinking",
        };

        public static string Scan(GameObject root) => Run(root, apply: false, removeAfter: false);
        public static string Convert(GameObject root, bool removeAfter) => Run(root, apply: true, removeAfter: removeAfter);

        private static string Run(GameObject root, bool apply, bool removeAfter)
        {
            if (root == null) return "Pick the avatar (with the prefab on it) first.";
            var comps = root.GetComponentsInChildren<MonoBehaviour>(true)
                            .Where(m => m != null && m.GetType().FullName == VrcFuryType)
                            .ToList();
            var log = new List<string>();
            var haptics = root.GetComponentsInChildren<MonoBehaviour>(true)
                              .Count(m => m != null && (m.GetType().FullName ?? "").StartsWith("VF.Component.VRCFuryHaptic"));

            if (comps.Count == 0 && haptics == 0)
                return "No VRCFury components found under this avatar. (VRCFury must be imported in the " +
                       "project for its data to be readable — the prefab's scripts can't be missing.)";

            int converted = 0, partial = 0, skipped = 0;
            foreach (var comp in comps)
            {
                foreach (var feature in FeaturesOf(comp))
                {
                    var kind = feature.GetType().Name;
                    try
                    {
                        switch (kind)
                        {
                            case "Toggle":
                                ConvertToggle(feature, comp.gameObject, root, apply, log, ref converted, ref partial);
                                break;
                            case "FullController":
                                ConvertFullController(feature, comp.gameObject, root, apply, log, ref converted, ref partial);
                                break;
                            case "ArmatureLink":
                                ConvertArmatureLink(feature, comp.gameObject, root, apply, log, ref converted, ref partial);
                                break;
                            case "DeleteDuringUpload":
                                if (apply)
                                {
                                    var os = comp.gameObject.GetComponent<CVRFuryObjectState>();
                                    if (os == null) os = Undo.AddComponent<CVRFuryObjectState>(comp.gameObject);
                                    os.entries.Add(new CVRFuryObjectState.Entry
                                    { target = comp.gameObject, action = CVRFuryObjectState.Action.Delete });
                                }
                                log.Add($"✓ '{comp.gameObject.name}': Delete-during-upload → CVRFury Object State (delete).");
                                converted++;
                                break;
                            default:
                                if (VrchatOnly.TryGetValue(kind, out var why))
                                { log.Add($"✗ '{comp.gameObject.name}': {kind} skipped — {why}."); skipped++; }
                                else if (Hygiene.Contains(kind))
                                { log.Add($"• '{comp.gameObject.name}': {kind} ignored (build hygiene — not needed for CVR)."); skipped++; }
                                else
                                { log.Add($"→ '{comp.gameObject.name}': {kind} not auto-converted yet — recreate with the matching CVRFury component."); partial++; }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Add($"✗ '{comp.gameObject.name}': {kind} failed to convert ({ex.Message}).");
                        partial++;
                    }
                }
                if (apply && removeAfter) Undo.DestroyObjectImmediate(comp);
            }

            if (haptics > 0)
                log.Add($"→ {haptics} SPS/haptic component(s) (plug/socket) found — convert those with Avatar " +
                        "features ▸ SPS (they need mesh work, not a component swap).");

            var verb = apply ? "Converted" : "Would convert";
            var head = $"{verb}: {converted} feature(s) ✓ · {partial} partial/manual → · {skipped} not applicable ✗" +
                       (apply && removeAfter ? " · VRCFury components removed." :
                        apply ? " · VRCFury components left in place (Step 5 Strip removes them)." : "");
            return head + "\n" + string.Join("\n", log);
        }

        // --- feature enumeration (content + legacy config.features, via GetAllFeatures when present) ----

        private static List<object> FeaturesOf(object comp)
        {
            var res = new List<object>();
            try
            {
                var m = comp.GetType().GetMethod("GetAllFeatures", BindingFlags.Public | BindingFlags.Instance);
                if (m != null && m.Invoke(comp, null) is IEnumerable all)
                {
                    foreach (var f in all) if (f != null) res.Add(f);
                    return res;
                }
            }
            catch { /* fall through to raw fields */ }

            if (Reflect.GetField(comp, "content") is object content) res.Add(content);
            var cfg = Reflect.GetField(comp, "config");
            if (cfg != null && Reflect.GetField(cfg, "features") is IEnumerable feats)
                foreach (var f in feats) if (f != null && !res.Contains(f)) res.Add(f);
            return res;
        }

        // --- Toggle → CVRFuryToggle / CVRFurySlider ---------------------------------------------------

        private static void ConvertToggle(object f, GameObject host, GameObject root, bool apply,
                                          List<string> log, ref int converted, ref int partial)
        {
            var name = Str(f, "name");
            if (string.IsNullOrEmpty(name)) name = host.name;
            var state = MapState(Reflect.GetField(f, "state"), root, name, log);

            if (Bool(f, "slider"))
            {
                if (apply)
                {
                    var sl = Undo.AddComponent<CVRFurySlider>(host);
                    sl.menuPath = name;
                    sl.saved = Bool(f, "saved");
                    sl.defaultValue = Mathf.Clamp01(Flt(f, "defaultSliderValue"));
                    sl.maxState = state;
                    if (Bool(f, "useGlobalParam")) sl.parameterName = Str(f, "globalParam");
                }
                log.Add($"✓ '{host.name}': slider '{name}' → CVRFury Slider ({state.actions.Count} action(s)).");
                converted++;
                return;
            }

            if (apply)
            {
                var t = Undo.AddComponent<CVRFuryToggle>(host);
                t.menuPath = name;                        // VRCFury uses '/' for submenus — ours does too
                t.defaultOn = Bool(f, "defaultOn");
                t.saved = Bool(f, "saved");
                t.momentary = Bool(f, "holdButton");
                if (Bool(f, "hasTransition")) t.transitionSeconds = Flt(f, "transitionTimeIn");
                if (Bool(f, "useGlobalParam")) t.parameterName = Str(f, "globalParam");
                t.state = state;
            }
            var notes = new List<string>();
            if (Bool(f, "enableExclusiveTag"))
                notes.Add($"exclusive tag '{Str(f, "exclusiveTag")}' — CVR has no exclusivity; use ONE Modes dropdown if these must be pick-one");
            if (Bool(f, "separateLocal")) notes.Add("had a separate local-only state (merged: main state used)");
            log.Add($"✓ '{host.name}': toggle '{name}' → CVRFury Toggle ({state.actions.Count} action(s))." +
                    (notes.Count > 0 ? " Note: " + string.Join("; ", notes) : ""));
            converted++;
        }

        /// <summary>VRCFury State{actions} → FuryState, action-by-action. Unmappable action types get a
        /// report line instead of vanishing.</summary>
        private static FuryState MapState(object state, GameObject root, string label, List<string> log)
        {
            var res = new FuryState();
            if (state == null || !(Reflect.GetField(state, "actions") is IEnumerable actions)) return res;

            foreach (var a in actions)
            {
                if (a == null) continue;
                switch (a.GetType().Name)
                {
                    case "ObjectToggleAction":
                    {
                        var obj = Reflect.GetField(a, "obj") as GameObject;
                        if (obj == null) break;
                        int mode = EnumInt(a, "mode");        // TurnOn=0 TurnOff=1 Toggle=2
                        res.actions.Add(new FuryAction
                        {
                            type = FuryAction.ActionType.ObjectToggle,
                            targetObject = obj,
                            targetState = mode == 0 || (mode == 2 && !obj.activeSelf),
                        });
                        break;
                    }
                    case "BlendShapeAction":
                    {
                        var shape = Str(a, "blendShape");
                        var value = Flt(a, "blendShapeValue");
                        if (string.IsNullOrEmpty(shape)) break;
                        var targets = new List<SkinnedMeshRenderer>();
                        if (Bool(a, "allRenderers"))
                            targets.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                .Where(s => s.sharedMesh != null && s.sharedMesh.GetBlendShapeIndex(shape) >= 0));
                        else if (Reflect.GetField(a, "renderer") is SkinnedMeshRenderer smr)
                            targets.Add(smr);
                        foreach (var smr in targets)
                            res.actions.Add(new FuryAction
                            {
                                type = FuryAction.ActionType.BlendShape,
                                blendShapeRenderer = smr, blendShape = shape, blendShapeValue = value,
                            });
                        break;
                    }
                    case "MaterialAction":
                    {
                        var rend = Reflect.GetField(a, "renderer") as Renderer;
                        if (rend == null && Reflect.GetField(a, "obj") is GameObject legacy && legacy != null)
                            rend = legacy.GetComponent<Renderer>();
                        var mat = ResolveAsset<Material>(Reflect.GetField(a, "mat"));
                        if (rend == null || mat == null) break;
                        res.actions.Add(new FuryAction
                        {
                            type = FuryAction.ActionType.MaterialSwap,
                            materialRenderer = rend, materialSlot = Int(a, "materialIndex"), material = mat,
                        });
                        break;
                    }
                    case "ScaleAction":
                    {
                        var obj = Reflect.GetField(a, "obj") as GameObject;
                        if (obj == null) break;
                        res.actions.Add(new FuryAction
                        {
                            type = FuryAction.ActionType.ScaleFactor,
                            scaleTarget = obj.transform, scaleFactor = Flt(a, "scale"), scaleAxes = Vector3.one,
                        });
                        break;
                    }
                    case "MaterialPropertyAction":
                    {
                        Renderer rend = null;
                        if (Reflect.GetField(a, "renderer2") is GameObject r2 && r2 != null) rend = r2.GetComponent<Renderer>();
                        if (rend == null) rend = Reflect.GetField(a, "renderer") as Renderer; // pre-v1 layout
                        if (rend == null && Bool(a, "affectAllMeshes"))
                            rend = root.GetComponentsInChildren<Renderer>(true)
                                       .FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.HasProperty(Str(a, "propertyName"))));
                        var prop = Str(a, "propertyName");
                        if (rend == null || string.IsNullOrEmpty(prop)) break;
                        int ptype = EnumInt(a, "propertyType");   // Float=0 Color=1 Vector=2 St=3 LegacyAuto=4
                        res.actions.Add(new FuryAction
                        {
                            type = FuryAction.ActionType.MaterialProperty,
                            propertyRenderer = rend, propertyName = prop,
                            propertyIsColor = ptype == 1,
                            propertyValue = Flt(a, "value"),
                            propertyColor = Reflect.GetField(a, "valueColor") is Color c ? c : Color.white,
                        });
                        break;
                    }
                    case "AnimationClipAction":
                    {
                        var clip = ResolveAsset<AnimationClip>(Reflect.GetField(a, "clip"));
                        log.Add($"→ '{label}': plays AnimationClip '{(clip != null ? clip.name : "?")}' — clips " +
                                "don't ride component actions yet; wire it via Step 2 (toggle clips) or a Full Controller.");
                        break;
                    }
                    default:
                        log.Add($"→ '{label}': action '{a.GetType().Name}' has no CVR mapping — skipped.");
                        break;
                }
            }
            return res;
        }

        // --- FullController → CVRFuryFullController ---------------------------------------------------

        private static void ConvertFullController(object f, GameObject host, GameObject root, bool apply,
                                                  List<string> log, ref int converted, ref int partial)
        {
            var controllers = new List<RuntimeAnimatorController>();
            if (Reflect.GetField(f, "controllers") is IEnumerable entries)
                foreach (var e in entries)
                {
                    var c = ResolveAsset<RuntimeAnimatorController>(Reflect.GetField(e, "controller"));
                    if (c != null) controllers.Add(c);
                }
            if (controllers.Count == 0 &&
                ResolveAsset<RuntimeAnimatorController>(Reflect.GetField(f, "controller")) is RuntimeAnimatorController legacy)
                controllers.Add(legacy);

            // Every synced parameter the prefab declared, exposed to the CVR menu with a best-fit control.
            var overrides = new List<CVRFuryFullController.ParamOverride>();
            if (Reflect.GetField(f, "prms") is IEnumerable prms)
                foreach (var e in prms)
                {
                    var asset = ResolveAsset<Object>(Reflect.GetField(e, "parameters"));
                    if (asset == null) continue;
                    if (!(Reflect.GetField(asset, VrcNames.ExprParams_List) is IEnumerable plist)) continue;
                    foreach (var p in plist)
                    {
                        var pname = Str(p, VrcNames.ExprParam_Name);
                        if (string.IsNullOrEmpty(pname)) continue;
                        int vt = EnumInt(p, VrcNames.ExprParam_ValueType); // VRC: Int=0 Float=1 Bool=2
                        overrides.Add(new CVRFuryFullController.ParamOverride
                        {
                            name = pname,
                            exposeToMenu = true,
                            menuPath = pname,
                            localOnly = Reflect.GetField(p, VrcNames.ExprParam_NetworkSynced) is bool ns && !ns,
                            menuType = vt == 1 ? CVRFuryFullController.AasParamType.Slider
                                     : vt == 0 ? CVRFuryFullController.AasParamType.Dropdown
                                               : CVRFuryFullController.AasParamType.Toggle,
                        });
                    }
                }

            if (controllers.Count == 0 && overrides.Count == 0)
            {
                log.Add($"✗ '{host.name}': Full Controller had no resolvable controller/parameter assets.");
                partial++;
                return;
            }

            if (apply)
            {
                var full = root.GetComponent<CVRFuryFullController>();
                if (full == null) full = Undo.AddComponent<CVRFuryFullController>(root);
                foreach (var c in controllers) if (!full.controllers.Contains(c)) full.controllers.Add(c);
                foreach (var o in overrides) if (full.parameters.All(x => x.name != o.name)) full.parameters.Add(o);

                // toggleParam = "this prop is enabled by parameter X" → give it a real menu toggle.
                var tp = Str(f, "toggleParam");
                if (!string.IsNullOrEmpty(tp))
                {
                    var t = Undo.AddComponent<CVRFuryToggle>(host);
                    t.menuPath = tp;
                    t.parameterName = tp;
                    t.defaultOn = host.activeSelf;
                    t.state.actions.Add(new FuryAction
                    { type = FuryAction.ActionType.ObjectToggle, targetObject = host, targetState = true });
                }
            }

            var extra = new List<string>();
            if (Reflect.GetField(f, "smoothedPrms") is IList sm && sm.Count > 0) extra.Add($"{sm.Count} smoothed param(s) not carried");
            if (Reflect.GetField(f, "rewriteBindings") is IList rw && rw.Count > 0) extra.Add($"{rw.Count} binding rewrite(s) not carried");
            log.Add($"✓ '{host.name}': Full Controller → CVRFury Full Controller ({controllers.Count} " +
                    $"controller(s), {overrides.Count} menu param(s))." +
                    (extra.Count > 0 ? " Note: " + string.Join("; ", extra) + "." : ""));
            converted++;
        }

        // --- ArmatureLink → CVRFuryArmatureLink -------------------------------------------------------

        private static void ConvertArmatureLink(object f, GameObject host, GameObject root, bool apply,
                                                List<string> log, ref int converted, ref int partial)
        {
            var propBone = Reflect.GetField(f, "propBone") as GameObject;
            if (propBone == null)
            {
                log.Add($"✗ '{host.name}': Armature Link has no prop bone set.");
                partial++;
                return;
            }

            Transform target = null;
            if (Reflect.GetField(f, "linkTo") is IList links && links.Count > 0)
            {
                var l = links[0];
                if (Bool(l, "useObj") && Reflect.GetField(l, "obj") is GameObject o && o != null)
                    target = o.transform;
                else if (Bool(l, "useBone"))
                {
                    var anim = root.GetComponentInChildren<Animator>();
                    if (anim != null && anim.isHuman)
                        target = anim.GetBoneTransform((HumanBodyBones)EnumInt(l, "bone"));
                }
                else
                {
                    var offset = Str(l, "offset");
                    if (!string.IsNullOrEmpty(offset)) target = root.transform.Find(offset);
                }
            }

            if (apply)
            {
                var al = Undo.AddComponent<CVRFuryArmatureLink>(host);
                al.propArmatureRoot = propBone.transform;
                al.linkTargetOverride = target;
                al.linkMode = CVRFuryArmatureLink.LinkMode.Reparent; // safest across all VRCFury link modes
                al.removeBoneSuffix = Str(f, "removeBoneSuffix");
                al.keepBoneOffsets = EnumInt(f, "keepBoneOffsets2") == 1; // Auto=0 Yes=1 No=2
            }
            log.Add($"✓ '{host.name}': Armature Link ('{propBone.name}'" +
                    (target != null ? $" → '{target.name}'" : "") + ") → CVRFury Armature Link (Reparent).");
            converted++;
        }

        // --- reflection plumbing ----------------------------------------------------------------------

        /// <summary>GuidWrapper resolution: live objRef first, then the "guid:fileID" id string.</summary>
        private static T ResolveAsset<T>(object guidWrapper) where T : Object
        {
            if (guidWrapper == null) return null;
            if (Reflect.GetField(guidWrapper, "objRef") is T direct && direct != null) return direct;
            if (!(Reflect.GetField(guidWrapper, "id") is string id) || string.IsNullOrEmpty(id)) return null;
            var parts = id.Split(':');
            var path = AssetDatabase.GUIDToAssetPath(parts[0]);
            if (string.IsNullOrEmpty(path)) return null;
            if (parts.Length > 1 && long.TryParse(parts[1], out var fid))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is T t && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out _, out long lfid) && lfid == fid)
                        return t;
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string Str(object o, string field) => Reflect.GetField(o, field) as string ?? "";
        private static bool Bool(object o, string field) => Reflect.GetField(o, field) is bool b && b;
        private static float Flt(object o, string field) =>
            Reflect.GetField(o, field) is float fl ? fl : Reflect.GetField(o, field) is int i ? i : 0f;
        private static int Int(object o, string field) =>
            Reflect.GetField(o, field) is int i ? i : 0;
        private static int EnumInt(object o, string field)
        {
            var v = Reflect.GetField(o, field);
            try { return v != null ? System.Convert.ToInt32(v) : 0; }
            catch { return 0; }
        }
    }
}
