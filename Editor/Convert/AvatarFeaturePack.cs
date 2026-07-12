using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Quality-of-life generators for the Avatar features tab. Each one only COMPOSES the existing CVRFury
    /// components (Toggle / Slider / Modes with their FuryAction system) — the proven builders bake them at
    /// upload, so these helpers stay pure editor conveniences with no new bake paths to break.
    /// </summary>
    internal static class AvatarFeaturePack
    {
        // --- 🧥 Wardrobe: every clothing item becomes a toggle in one click -------------------------

        internal sealed class WardrobeRow
        {
            public GameObject go;
            public string label;
            public bool include = true;
            public bool defaultOn;
        }

        // Parts of the avatar itself — not clothing, never auto-toggled.
        private static readonly string[] BodyWords =
            { "body", "head", "face", "eye", "lash", "brow", "teeth", "tongue", "mouth", "viseme", "armature" };

        /// <summary>Every renderer-bearing object that looks like a wearable and isn't already covered by a
        /// CVRFury toggle. The default menu state mirrors the scene (currently-off items stay off).</summary>
        public static List<WardrobeRow> ScanWardrobe(GameObject avatar)
        {
            var rows = new List<WardrobeRow>();
            if (avatar == null) return rows;

            // Objects already handled by a toggle (component target or carrier) are skipped.
            var covered = new HashSet<GameObject>();
            foreach (var t in avatar.GetComponentsInChildren<CVRFuryToggle>(true))
            {
                covered.Add(t.gameObject);
                foreach (var a in t.state.actions.Where(a => a.type == FuryAction.ActionType.ObjectToggle && a.targetObject != null))
                    covered.Add(a.targetObject);
            }

            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                var go = r.gameObject;
                if (go == avatar || covered.Contains(go)) continue;
                var lower = go.name.ToLowerInvariant();
                if (BodyWords.Any(w => lower.Contains(w))) continue;
                if (rows.Any(x => x.go == go)) continue;
                rows.Add(new WardrobeRow { go = go, label = Prettify(go.name), defaultOn = go.activeSelf });
            }
            return rows.OrderBy(x => x.label).ToList();
        }

        /// <summary>One CVRFuryToggle per included row, placed on the item itself so it's easy to find later.
        /// The menu label mirrors the object name; existing scene state becomes the default.</summary>
        public static string CreateWardrobeToggles(IEnumerable<WardrobeRow> rows, string menuFolder)
        {
            int made = 0;
            foreach (var row in rows.Where(r => r.include && r.go != null))
            {
                var t = Undo.AddComponent<CVRFuryToggle>(row.go);
                t.menuPath = string.IsNullOrEmpty(menuFolder) ? row.label : $"{menuFolder}/{row.label}";
                t.defaultOn = row.defaultOn;
                t.saved = true;
                t.state.actions.Add(new FuryAction
                {
                    type = FuryAction.ActionType.ObjectToggle,
                    targetObject = row.go,
                    targetState = true,
                });
                EditorUtility.SetDirty(row.go);
                made++;
            }
            return made == 0 ? "Nothing selected to create."
                 : $"Created {made} toggle(s). They bake into the CVR menu automatically at upload — " +
                   "tweak any of them on the item's CVRFury Toggle component.";
        }

        /// <summary>Toggles for whatever is selected in the Hierarchy — the fastest possible path from
        /// "these three props" to "three menu toggles".</summary>
        public static string TogglesFromSelection(GameObject avatar, string menuFolder)
        {
            if (avatar == null) return "Pick the avatar first.";
            var rows = Selection.gameObjects
                .Where(g => g != null && g != avatar && g.transform.IsChildOf(avatar.transform))
                .Where(g => g.GetComponent<CVRFuryToggle>() == null)
                .Select(g => new WardrobeRow { go = g, label = Prettify(g.name), defaultOn = g.activeSelf })
                .ToList();
            if (rows.Count == 0)
                return "Select one or more objects INSIDE the avatar in the Hierarchy (that don't already have " +
                       "a toggle), then click this.";
            return CreateWardrobeToggles(rows, menuFolder);
        }

        // --- 🎨 Material variants: texture/color editions as an in-game dropdown --------------------

        public static string CreateMaterialVariants(GameObject avatar, string menuLabel, Renderer renderer,
                                                    int slot, List<Material> variants)
        {
            if (avatar == null || renderer == null) return "Pick the avatar and the mesh.";
            var mats = variants.Where(m => m != null).ToList();
            if (mats.Count < 2) return "Add at least two materials (the current one + the variants).";
            slot = Mathf.Clamp(slot, 0, renderer.sharedMaterials.Length - 1);

            var modes = Undo.AddComponent<CVRFuryModes>(avatar);
            modes.menuPath = string.IsNullOrEmpty(menuLabel) ? Prettify(renderer.name) + " Style" : menuLabel;
            modes.saved = true;
            var current = renderer.sharedMaterials[slot];
            modes.defaultMode = Mathf.Max(0, mats.IndexOf(current));
            modes.modes = mats.Select(m => new CVRFuryModes.Mode
            {
                name = Prettify(m.name),
                state = new FuryState
                {
                    actions = new List<FuryAction>
                    {
                        new FuryAction
                        {
                            type = FuryAction.ActionType.MaterialSwap,
                            materialRenderer = renderer,
                            materialSlot = slot,
                            material = m,
                        },
                    },
                },
            }).ToList();
            EditorUtility.SetDirty(avatar);
            return $"Created the \"{modes.menuPath}\" dropdown with {mats.Count} material variant(s) on " +
                   $"'{renderer.name}' (slot {slot}). Picking one in-game swaps the material.";
        }

        // --- 📏 Height presets: smol / normal / tall as a dropdown ----------------------------------

        public static string CreateHeightPresets(GameObject avatar, List<(string name, float factor)> presets)
        {
            if (avatar == null) return "Pick the avatar first.";
            var rows = presets.Where(p => p.factor > 0.01f).ToList();
            if (rows.Count < 2) return "Add at least two height presets.";

            var modes = Undo.AddComponent<CVRFuryModes>(avatar);
            modes.menuPath = "Height";
            modes.saved = true;
            // Default to whichever preset is closest to the current (1×) scale.
            modes.defaultMode = rows.IndexOf(rows.OrderBy(p => Mathf.Abs(p.factor - 1f)).First());
            modes.modes = rows.Select(p => new CVRFuryModes.Mode
            {
                name = string.IsNullOrEmpty(p.name) ? $"{p.factor:0.##}×" : p.name,
                state = new FuryState
                {
                    actions = new List<FuryAction>
                    {
                        new FuryAction
                        {
                            type = FuryAction.ActionType.ScaleFactor,
                            scaleTarget = avatar.transform,
                            scaleFactor = p.factor,
                            scaleAxes = Vector3.one,
                        },
                    },
                },
            }).ToList();
            EditorUtility.SetDirty(avatar);
            return $"Created the \"Height\" dropdown with {rows.Count} preset(s): " +
                   string.Join(", ", rows.Select(p => $"{p.name} ({p.factor:0.##}×)")) + ".";
        }

        // --- 🔦 Flashlight: head-mounted spotlight with a menu toggle -------------------------------

        public static string CreateFlashlight(GameObject avatar)
        {
            if (avatar == null) return "Pick the avatar first.";
            var anim = avatar.GetComponentInChildren<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            var parent = head != null ? head : avatar.transform;

            if (parent.Find("CVRFury Flashlight") != null)
                return "There's already a flashlight on this avatar (child \"CVRFury Flashlight\").";

            var go = new GameObject("CVRFury Flashlight");
            Undo.RegisterCreatedObjectUndo(go, "Flashlight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0.09f); // roughly forehead, aiming forward
            go.transform.localRotation = Quaternion.identity;

            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 12f;
            light.spotAngle = 55f;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.95f, 0.85f); // warm white
            light.shadows = LightShadows.None;         // dynamic shadows on avatars are a perf trap

            go.SetActive(false);
            var t = Undo.AddComponent<CVRFuryToggle>(go);
            t.menuPath = "Flashlight";
            t.defaultOn = false;
            t.saved = false; // rejoin dark worlds dark — deliberate
            t.state.actions.Add(new FuryAction
            {
                type = FuryAction.ActionType.ObjectToggle, targetObject = go, targetState = true,
            });
            EditorUtility.SetDirty(avatar);
            return head != null
                ? "Flashlight added to the head with a menu toggle (default off). It follows your gaze."
                : "Flashlight added (no humanoid head found — parented to the avatar root; move it if needed).";
        }

        // --- 🌈 Master hue slider: one slider drives every hue-capable material ---------------------

        private static readonly string[] HueProps = { "_MainHueShift", "_HueShift", "_MainHue", "_Hue" };

        public static string CreateMasterHueSlider(GameObject avatar)
        {
            if (avatar == null) return "Pick the avatar first.";

            // Find the hue property this avatar's shaders actually use, and every renderer carrying it.
            var byProp = new Dictionary<string, List<Renderer>>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials.Where(m => m != null))
                    foreach (var p in HueProps)
                        if (m.HasProperty(p))
                        {
                            if (!byProp.TryGetValue(p, out var list)) byProp[p] = list = new List<Renderer>();
                            if (!list.Contains(r)) list.Add(r);
                            break;
                        }
            if (byProp.Count == 0)
                return "No material with a hue-shift property found (looked for " + string.Join(", ", HueProps) +
                       "). On locked Poiyomi, mark the hue property Animated and re-lock first.";

            var best = byProp.OrderByDescending(kv => kv.Value.Count).First();
            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = "Hue Shift";
            slider.saved = true;
            slider.defaultValue = 0f;
            foreach (var r in best.Value)
            {
                slider.minState.actions.Add(new FuryAction
                {
                    type = FuryAction.ActionType.MaterialProperty,
                    propertyRenderer = r, propertyName = best.Key, propertyValue = 0f,
                });
                slider.maxState.actions.Add(new FuryAction
                {
                    type = FuryAction.ActionType.MaterialProperty,
                    propertyRenderer = r, propertyName = best.Key, propertyValue = 1f,
                });
            }
            EditorUtility.SetDirty(avatar);
            return $"Created one \"Hue Shift\" slider driving {best.Key} on {best.Value.Count} renderer(s) — " +
                   "the whole outfit re-colours together. Locked Poiyomi: the property must be marked " +
                   "Animated (then re-lock) for the slider to have an effect in-game.";
        }

        // --- 👉 Boop: a face reaction on menu press, plus a touch trigger where the CCK allows ------

        public static string CreateBoop(GameObject avatar, SkinnedMeshRenderer face, string blendshape)
        {
            if (avatar == null || face == null || string.IsNullOrEmpty(blendshape))
                return "Pick the face mesh and the reaction blendshape (e.g. a blush or squish shape).";

            var toggle = Undo.AddComponent<CVRFuryToggle>(avatar);
            toggle.menuPath = "Boop";
            toggle.parameterName = "Boop";
            toggle.momentary = true;   // springs back — press, react, release
            toggle.saved = false;
            toggle.transitionSeconds = 0.15f; // ease in/out instead of snapping
            toggle.state.actions.Add(new FuryAction
            {
                type = FuryAction.ActionType.BlendShape,
                blendShapeRenderer = face, blendShape = blendshape, blendShapeValue = 100f,
            });
            EditorUtility.SetDirty(avatar);

            // Best-effort physical trigger: a small CCK trigger volume on the nose that drives the same
            // parameter when someone's hand enters it. Shape-based so CCK field drift degrades to menu-only.
            var trigger = TryAddTouchTrigger(avatar, "Boop");
            return "Boop added as a momentary menu button (press = face reaction)." +
                   (trigger ? " A touch trigger was also placed on the head — booping the nose fires it too."
                            : " (No touch trigger — the CCK's trigger component wasn't found; menu-only.)");
        }

        private static bool TryAddTouchTrigger(GameObject avatar, string parameter)
        {
            var t = Reflect.FindType("ABI.CCK.Components.CVRAdvancedAvatarSettingsTrigger");
            if (t == null) return false;
            var anim = avatar.GetComponentInChildren<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) return false;

            try
            {
                var go = new GameObject("CVRFury Boop Trigger");
                Undo.RegisterCreatedObjectUndo(go, "Boop trigger");
                go.transform.SetParent(head, false);
                go.transform.localPosition = new Vector3(0f, 0f, 0.11f); // nose-ish
                var comp = go.AddComponent(t);

                // Fuzzy field wiring — names drift across CCK versions, all sets are best-effort.
                Reflect.SetField(comp, "settingName", parameter);
                Reflect.SetField(comp, "settingValue", 1f);
                Reflect.SetField(comp, "areaSize", new Vector3(0.06f, 0.06f, 0.06f));
                Reflect.SetField(comp, "allowOthersToTrigger", true);
                Reflect.SetField(comp, "networkInteraction", true);
                EditorUtility.SetDirty(go);
                return true;
            }
            catch { return false; }
        }

        // "AS_Fishnet_top" → "Fishnet top"
        private static string Prettify(string name)
        {
            var s = name.Trim();
            foreach (var prefix in new[] { "AS_", "AS ", "T_", "C_" })
                if (s.StartsWith(prefix)) { s = s.Substring(prefix.Length); break; }
            return s.Replace('_', ' ').Trim();
        }
    }
}
