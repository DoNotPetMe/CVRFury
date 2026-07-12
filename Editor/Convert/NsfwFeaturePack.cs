using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Adult (18+) avatar generators — ChilloutVR supports adult content natively, and these are the setups
    /// NSFW avatar creators rebuild by hand on every model. Like the rest of the feature pack, everything
    /// only composes the existing CVRFury components (Toggle / Slider / Modes), so the proven builders bake
    /// it all at upload. Suggestions are name-heuristic and always land in an editable review list — the
    /// tool proposes, the user decides.
    /// </summary>
    internal static class NsfwFeaturePack
    {
        // --- 🚪 Undress stages: Dressed → Underwear → Nude as one ordered dropdown ------------------

        internal sealed class StageRow
        {
            public string name;
            public List<GameObject> on = new List<GameObject>();
        }

        private static readonly string[] UnderwearWords =
            { "bra", "panty", "panties", "lingerie", "thong", "bikini", "underwear", "boxers", "briefs",
              "garter", "stocking", "sock", "pastie" };

        /// <summary>Suggests the classic three stages from what's on the avatar: everything wearable,
        /// underwear-looking items only, nothing. Fully editable before creating.</summary>
        public static List<StageRow> SuggestStages(GameObject avatar)
        {
            var wearables = Wearables(avatar);
            var underwear = wearables.Where(g =>
            {
                var n = g.name.ToLowerInvariant();
                return UnderwearWords.Any(w => n.Contains(w));
            }).ToList();
            return new List<StageRow>
            {
                new StageRow { name = "Dressed", on = new List<GameObject>(wearables) },
                new StageRow { name = "Underwear", on = underwear },
                new StageRow { name = "Nude", on = new List<GameObject>() },
            };
        }

        public static string CreateUndressStages(GameObject avatar, List<StageRow> stages)
        {
            if (avatar == null) return "Pick the avatar first.";
            var union = stages.SelectMany(s => s.on).Where(o => o != null).Distinct().ToList();
            if (union.Count == 0) return "The stages reference no objects — scan or add some first.";

            var modes = Undo.AddComponent<CVRFuryModes>(avatar);
            modes.menuPath = "Undress";
            modes.saved = false;      // always spawn dressed — deliberate
            modes.defaultMode = 0;
            modes.modes = stages.Select(s => new CVRFuryModes.Mode
            {
                name = string.IsNullOrEmpty(s.name) ? "Stage" : s.name,
                state = new FuryState
                {
                    actions = union.Select(o => new FuryAction
                    {
                        type = FuryAction.ActionType.ObjectToggle,
                        targetObject = o,
                        targetState = s.on.Contains(o),
                    }).ToList(),
                },
            }).ToList();
            EditorUtility.SetDirty(avatar);
            return $"Created the \"Undress\" dropdown: {string.Join(" → ", stages.Select(s => s.name))} over " +
                   $"{union.Count} item(s). Not saved between sessions — you always spawn dressed.";
        }

        // --- 🛡 SFW switch: one toggle that makes the avatar stream-safe -----------------------------

        private static readonly string[] NsfwWords =
            { "nipple", "genital", "penis", "dick", "cock", "pussy", "vagina", "vulva", "nsfw", "lewd",
              "plug", "cum", "dildo" };

        public static List<GameObject> SuggestNsfwObjects(GameObject avatar)
        {
            if (avatar == null) return new List<GameObject>();
            return avatar.GetComponentsInChildren<Transform>(true)
                .Select(t => t.gameObject)
                .Where(g => g != avatar && NsfwWords.Any(w => g.name.ToLowerInvariant().Contains(w)))
                .Distinct()
                .ToList();
        }

        /// <summary>One "SFW Mode" toggle, ON by default and saved: while on, the listed NSFW objects are
        /// hidden and the modesty items (optional) are shown. One press to be stream-safe, one to undo.</summary>
        public static string CreateSfwSwitch(GameObject avatar, List<GameObject> hide, List<GameObject> show)
        {
            if (avatar == null) return "Pick the avatar first.";
            hide = hide.Where(o => o != null).Distinct().ToList();
            show = show.Where(o => o != null).Distinct().ToList();
            if (hide.Count == 0 && show.Count == 0) return "Add at least one object to hide or show.";

            var t = Undo.AddComponent<CVRFuryToggle>(avatar);
            t.menuPath = "SFW Mode";
            t.parameterName = "SFWMode";
            t.defaultOn = true;   // safe by default: spawn SFW, opt INTO nsfw
            t.saved = true;
            foreach (var o in hide)
                t.state.actions.Add(new FuryAction { type = FuryAction.ActionType.ObjectToggle, targetObject = o, targetState = false });
            foreach (var o in show)
                t.state.actions.Add(new FuryAction { type = FuryAction.ActionType.ObjectToggle, targetObject = o, targetState = true });
            EditorUtility.SetDirty(avatar);
            return $"Created \"SFW Mode\" (ON by default, saved): hides {hide.Count} object(s)" +
                   (show.Count > 0 ? $", shows {show.Count} modesty item(s)" : "") +
                   " while enabled. One press before streaming, done.";
        }

        // --- 🫦 Touch reactions: a body-part trigger drives a face reaction --------------------------

        // A curated subset of bones that make sense as touch zones.
        public static readonly (string label, HumanBodyBones bone)[] TouchZones =
        {
            ("Head", HumanBodyBones.Head),
            ("Chest", HumanBodyBones.Chest),
            ("Hips", HumanBodyBones.Hips),
            ("Left thigh", HumanBodyBones.LeftUpperLeg),
            ("Right thigh", HumanBodyBones.RightUpperLeg),
            ("Neck", HumanBodyBones.Neck),
        };

        public static string CreateTouchReaction(GameObject avatar, SkinnedMeshRenderer face, string blendshape,
                                                 HumanBodyBones zone, string zoneLabel, bool othersCanTrigger)
        {
            if (avatar == null || face == null || string.IsNullOrEmpty(blendshape))
                return "Pick the face mesh and a reaction blendshape.";
            var anim = avatar.GetComponentInChildren<Animator>();
            var bone = anim != null && anim.isHuman ? anim.GetBoneTransform(zone) : null;
            if (bone == null) return $"No humanoid bone found for {zoneLabel}.";

            var param = "Touch" + zoneLabel.Replace(" ", "");
            var toggle = Undo.AddComponent<CVRFuryToggle>(avatar);
            toggle.menuPath = $"Reactions/{zoneLabel} touch";
            toggle.parameterName = param;
            toggle.momentary = true;
            toggle.saved = false;
            toggle.transitionSeconds = 0.2f; // soft in/out
            toggle.state.actions.Add(new FuryAction
            {
                type = FuryAction.ActionType.BlendShape,
                blendShapeRenderer = face, blendShape = blendshape, blendShapeValue = 100f,
            });
            EditorUtility.SetDirty(avatar);

            var wired = AvatarFeaturePack.TryAddTouchTrigger(param, bone, Vector3.zero, 0.12f, othersCanTrigger);
            return $"\"{zoneLabel}\" reaction added ({blendshape})." +
                   (wired ? othersCanTrigger ? " Touch trigger placed — anyone's hand fires it."
                                             : " Touch trigger placed — only YOUR hands fire it."
                          : " (No CCK trigger component found — it works as a menu button only.)");
        }

        // --- 💧 Wetness slider: one slider drives the skin's gloss/wetness --------------------------

        private static readonly string[] WetProps =
            { "_WetnessStrength", "_Wetness", "_GlossMapScale", "_Glossiness", "_Smoothness" };

        public static string CreateWetnessSlider(GameObject avatar)
        {
            if (avatar == null) return "Pick the avatar first.";
            var byProp = new Dictionary<string, List<Renderer>>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials.Where(m => m != null))
                    foreach (var p in WetProps)
                        if (m.HasProperty(p))
                        {
                            if (!byProp.TryGetValue(p, out var list)) byProp[p] = list = new List<Renderer>();
                            if (!list.Contains(r)) list.Add(r);
                            break;
                        }
            if (byProp.Count == 0)
                return "No gloss/wetness property found (looked for " + string.Join(", ", WetProps) + "). " +
                       "On locked Poiyomi, mark the property Animated and re-lock first.";

            var best = byProp.OrderByDescending(kv => kv.Value.Count).First();
            var slider = Undo.AddComponent<CVRFurySlider>(avatar);
            slider.menuPath = "Wetness";
            slider.saved = false;
            slider.defaultValue = 0f;
            foreach (var r in best.Value)
            {
                // Min = the material's CURRENT look (dry baseline), max = fully glossy.
                float baseline = 0f;
                var mat = r.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty(best.Key));
                if (mat != null) baseline = mat.GetFloat(best.Key);
                slider.minState.actions.Add(new FuryAction
                {
                    type = FuryAction.ActionType.MaterialProperty,
                    propertyRenderer = r, propertyName = best.Key, propertyValue = baseline,
                });
                slider.maxState.actions.Add(new FuryAction
                {
                    type = FuryAction.ActionType.MaterialProperty,
                    propertyRenderer = r, propertyName = best.Key, propertyValue = 1f,
                });
            }
            EditorUtility.SetDirty(avatar);
            return $"Created the \"Wetness\" slider driving {best.Key} on {best.Value.Count} renderer(s): " +
                   "slider at 0 = your current look, 1 = full gloss. Locked Poiyomi: mark the property " +
                   "Animated (then re-lock) or it won't move in-game.";
        }

        // --- 🍑 Jiggle tuner: sensible DynamicBone physics in one click -----------------------------

        internal enum JigglePreset { Soft, Bouncy, Extra }

        private static readonly Dictionary<JigglePreset, (float damping, float elasticity, float stiffness, float inert)> JiggleValues =
            new Dictionary<JigglePreset, (float, float, float, float)>
            {
                { JigglePreset.Soft,   (0.20f, 0.05f, 0.70f, 0.30f) },
                { JigglePreset.Bouncy, (0.10f, 0.08f, 0.40f, 0.10f) },
                { JigglePreset.Extra,  (0.06f, 0.12f, 0.25f, 0.05f) },
            };

        /// <summary>Applies a jiggle preset to the given bones: tunes an existing DynamicBone on the bone, or
        /// adds one rooted there. DynamicBone ships with the CCK, so it's the physics CVR actually runs.</summary>
        public static string ApplyJiggle(List<Transform> bones, JigglePreset preset)
        {
            var dbType = Reflect.FindType(VrcNames.DynamicBoneType);
            if (dbType == null)
                return "DynamicBone isn't in this project — import the ChilloutVR CCK (it bundles it), then retry.";
            var list = bones.Where(b => b != null).Distinct().ToList();
            if (list.Count == 0) return "Add the bone(s) to jiggle (e.g. Breast_L / Breast_R / Butt).";

            var v = JiggleValues[preset];
            int tuned = 0, added = 0;
            foreach (var bone in list)
            {
                var db = bone.GetComponent(dbType);
                if (db == null)
                {
                    db = Undo.AddComponent(bone.gameObject, dbType); // '??' breaks on Unity's fake-null
                    Reflect.SetField(db, VrcNames.DB_Root, bone);
                    added++;
                }
                else tuned++;
                Undo.RecordObject(db, "Jiggle preset");
                Reflect.SetField(db, VrcNames.DB_Damping, v.damping);
                Reflect.SetField(db, VrcNames.DB_Elasticity, v.elasticity);
                Reflect.SetField(db, VrcNames.DB_Stiffness, v.stiffness);
                Reflect.SetField(db, VrcNames.DB_Inert, v.inert);
                EditorUtility.SetDirty(db);
            }
            return $"{preset} jiggle applied: {added} DynamicBone(s) added, {tuned} existing tuned. " +
                   "Test in Play mode and nudge Elasticity/Stiffness to taste.";
        }

        // Wearable-looking objects, same heuristic as the wardrobe (but including already-toggled ones —
        // undress stages need the full picture).
        private static List<GameObject> Wearables(GameObject avatar)
        {
            var res = new List<GameObject>();
            if (avatar == null) return res;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                var go = r.gameObject;
                if (go == avatar || res.Contains(go)) continue;
                var lower = go.name.ToLowerInvariant();
                if (AvatarFeaturePack.BodyWords.Any(w => lower.Contains(w))) continue;
                res.Add(go);
            }
            return res.OrderBy(g => g.name).ToList();
        }
    }
}
