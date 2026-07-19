using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Logic for linking a folder of animation clips onto the avatar's existing ChilloutVR Advanced
    /// Avatar Settings toggle entries as on/off clips, and (optionally) building + attaching a working
    /// controller so the entries' parameters exist (clearing the CCK's "parameter not present" warnings).
    /// Hosted by the unified CVRFury window.
    ///
    /// Clips are paired by base name using the on/off suffix words you provide (e.g. "toggled"/"default");
    /// each base is matched to a toggle entry by display name or machine-name leaf. Non-destructive — it
    /// only fills clip fields and generates a controller asset; it never clears AAS entries.
    /// </summary>
    internal static class ToggleClipLinker
    {
        public static string LinkClips(GameObject avatar, string[] folders, string onSuffix, string offSuffix,
                                       bool build, AnimatorController controller)
        {
            if (avatar == null) return "Select your avatar first.";
            var valid = ValidFolders(folders);
            if (valid.Length == 0) return "Pick at least one valid project folder of animation clips.";

            var cvr = CckAvatar.FindOn(avatar);
            if (cvr == null) return "No CVRAvatar found on the selected avatar (run step 1 first).";
            var entries = cvr.SettingsList;
            if (entries == null || entries.Count == 0)
                return "No Advanced Avatar Settings entries yet — run step 1 (Link parameters) first.";

            // ON/OFF suffix inputs accept several comma-separated alternatives (e.g. "toggled, on, enabled"
            // / "default, off, disabled"), so clips named differently from the rest still pair up.
            var onList = SplitWords(onSuffix);
            var offList = SplitWords(offSuffix);
            if (offList.Count == 0)
                return "Set the OFF suffix word(s), e.g. \"off, default, disabled\". Leave ON blank if the " +
                       "on animation is named exactly after the toggle (e.g. \"Tail\" + \"Tail off\").";

            // --- pair clips by base name (every folder is scanned recursively, including subfolders) ---
            var pairs = PairClips(valid, onList, offList, out int clipCount);

            // --- assign clips onto matching toggle entries (non-destructive) ---
            int linked = 0;
            var noClip = new List<string>();
            var usedBases = new HashSet<string>();
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var name = CckAvatar.EntryName(entry) ?? "";
                var machine = CckAvatar.EntryMachineName(entry) ?? "";
                string keyName = Norm(name), keyLeaf = Norm(Leaf(machine));
                string hitKey = pairs.ContainsKey(keyName) ? keyName : (pairs.ContainsKey(keyLeaf) ? keyLeaf : null);
                if (hitKey == null) { if (!string.IsNullOrEmpty(name)) noClip.Add(name); continue; }
                var p = pairs[hitKey];
                // Toggle: on/off clips. Slider/radial: the "off"/default clip is value 0 (min), the
                // "on"/toggled clip is value 1 (max) — so hue-shift radials and hair-on-a-slider animate.
                bool didLink = cvr.SetToggleClips(entry, p.onClip, p.offClip)
                            || cvr.SetSliderClips(entry, p.offClip, p.onClip);
                if (didLink) { linked++; usedBases.Add(hitKey); }
            }

            string buildReport = build ? BuildAndAttach(cvr, avatar, entries, controller) : "";

            cvr.Persist();

            var unusedClips = pairs.Where(kv => !usedBases.Contains(kv.Key)).Select(kv => kv.Value.baseName).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scanned {clipCount} clip(s) → {pairs.Count} on/off pair(s).");
            sb.AppendLine($"Linked clips onto {linked} toggle(s).");
            if (noClip.Count > 0)
                sb.AppendLine($"Toggles with no matching clip ({noClip.Count}): " +
                              string.Join(", ", noClip.Take(20)) + (noClip.Count > 20 ? " …" : ""));
            if (unusedClips.Count > 0)
                sb.AppendLine($"Clip pairs with no matching toggle ({unusedClips.Count}): " +
                              string.Join(", ", unusedClips.Take(20)) + (unusedClips.Count > 20 ? " …" : ""));
            if (!string.IsNullOrEmpty(buildReport)) sb.AppendLine("\n" + buildReport);
            else sb.AppendLine("\nNext: press the CCK's Create Controller → Attach, or enable 'Build & attach' above.");
            return sb.ToString();
        }

        public static string BuildAndAttach(CckAvatar cvr, GameObject avatar, System.Collections.IList entries,
                                             AnimatorController provided)
        {
            // CVR's movement (walk / run / jump / crouch / fly / swim) lives in the stock CCK
            // AvatarAnimator's locomotion layers. We FORCE that as the foundation: the generated AAS
            // controller is always based on a controller that carries CVR locomotion, so the converted
            // avatar always moves. A supplied controller is only used as the base when it already
            // contains that locomotion; otherwise basing on it would strip movement.
            var stock = FindCvrAvatarAnimator();
            AnimatorController source;
            string baseNote;
            if (provided != null && HasCvrLocomotion(provided))
            { source = provided; baseNote = "based on the supplied controller (it already has CVR locomotion)"; }
            else if (provided != null && stock != null)
            { source = stock; baseNote = "supplied controller has no CVR locomotion — used CVR's stock AvatarAnimator instead so movement works"; }
            else { source = stock; baseNote = "based on CVR's stock AvatarAnimator (CCK movement)"; }

            if (source == null)
                return "Controller build skipped: CVR's stock AvatarAnimator wasn't found, and no supplied " +
                       "controller carries CVR locomotion. Make sure the CCK is imported, or leave the Controller field empty.";
            var gen = CopyController(source, avatar.name);
            if (gen == null) return "Controller build skipped: couldn't copy the source controller.";
            if (!HasCvrLocomotion(gen))
                return "Controller build aborted: the base controller has no CVR locomotion, so the avatar " +
                       "wouldn't move. Leave the Controller field empty to use CVR's stock AvatarAnimator.";

            // A clip that animates the humanoid rig (muscles or bones) would pose the whole body when its
            // layer runs — the "motorbike pose". Never build a layer from such a clip.
            var bonePaths = HumanoidBonePaths(avatar);

            // Belt-and-suspenders: even if a body-posing clip slips past detection, every toggle layer gets
            // a mask that disables all humanoid muscles/IK, so a clothing/accessory toggle can physically
            // never move the skeleton. This is the structural cure for the recurring motorbike pose.
            var noHumanoidMask = AnimatorUtil.CreateNoHumanoidMask(gen);

            var existing = new HashSet<string>(gen.parameters.Select(p => p.name));
            int paramsAdded = 0, layersBuilt = 0, posedSkipped = 0;

            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var machine = CckAvatar.EntryMachineName(entry);
                if (string.IsNullOrEmpty(machine) || existing.Contains(machine)) continue;

                var toggle = Reflect.GetField(entry, CckNames.Entry_ToggleSettings);
                var slider = Reflect.GetField(entry, CckNames.Entry_SliderSettings);
                var dropdown = Reflect.GetField(entry, CckNames.Entry_DropdownSettings);

                if (toggle != null)
                {
                    var onClip = Reflect.GetField(toggle, CckNames.Toggle_AnimationClip) as AnimationClip;
                    var offClip = Reflect.GetField(toggle, CckNames.Toggle_OffAnimationClip) as AnimationClip;
                    bool defOn = Reflect.GetField(toggle, CckNames.Setting_DefaultBool) is bool b && b;

                    bool posesBody = HumanoidCurves.PosesHumanoid(onClip, bonePaths) ||
                                     HumanoidCurves.PosesHumanoid(offClip, bonePaths);
                    if ((onClip != null || offClip != null) && !posesBody)
                    {
                        AnimatorUtil.AddBoolToggleLayer(gen, "CVRFury: " + Leaf(machine), machine, offClip, onClip, defOn, noHumanoidMask);
                        layersBuilt++;
                    }
                    else
                    {
                        AnimatorUtil.EnsureBoolParam(gen, machine, defOn); // param only (no body-posing layer)
                        if (posesBody) posedSkipped++;
                    }
                    paramsAdded++;
                }
                else if (slider != null)
                {
                    var minClip = Reflect.GetField(slider, CckNames.Slider_MinAnimationClip) as AnimationClip;
                    var maxClip = Reflect.GetField(slider, CckNames.Slider_MaxAnimationClip) as AnimationClip;
                    float def = ToFloat(Reflect.GetField(slider, CckNames.Setting_DefaultFloat));

                    bool posesBody = HumanoidCurves.PosesHumanoid(minClip, bonePaths) ||
                                     HumanoidCurves.PosesHumanoid(maxClip, bonePaths);
                    if ((minClip != null || maxClip != null) && !posesBody)
                    {
                        // A 1D blend tree from min (0) → max (1) driven by the synced Float — the animator
                        // side of a radial/slider. This is what makes hue-shift sliders and slider-derived
                        // toggles (hair on a radial, etc.) actually move in CVR.
                        AnimatorUtil.AddBlendTreeLayer(gen, "CVRFury: " + Leaf(machine), machine, minClip, maxClip, def, noHumanoidMask);
                        layersBuilt++;
                    }
                    else
                    {
                        AnimatorUtil.EnsureFloatParam(gen, machine, def); // param only (no clips / body-posing)
                        if (posesBody) posedSkipped++;
                    }
                    paramsAdded++;
                }
                else if (dropdown != null)
                {
                    AnimatorUtil.EnsureIntParam(gen, machine, ToInt(Reflect.GetField(dropdown, CckNames.Setting_DefaultInt)));
                    paramsAdded++;
                }
                existing.Add(machine);
            }

            EditorUtility.SetDirty(gen);
            AssetDatabase.SaveAssets();
            cvr.AttachGeneratedController(gen);
            AssetDatabase.SaveAssets();

            return $"Built & attached '{gen.name}': {paramsAdded} parameter(s), {layersBuilt} clip-driven layer(s)" +
                   (posedSkipped > 0 ? $", {posedSkipped} skipped (clip poses the body — would cause the motorbike pose)" : "") +
                   $".\nMovement: {baseNote}.\nSaved to {AssetDatabase.GetAssetPath(gen)}.\nThe red ❗ should be gone — no Create Controller needed.";
        }

        /// <summary>True if a controller carries CVR's locomotion driver parameters, i.e. it can move the
        /// avatar. ChilloutVR feeds MovementX / MovementY / Grounded into the AAS animator at runtime; a
        /// controller missing them yields a static, non-moving avatar — which is why we never let one
        /// become the base.</summary>
        private static bool HasCvrLocomotion(AnimatorController c)
        {
            if (c == null) return false;
            var names = new HashSet<string>(c.parameters.Select(p => p.name));
            return names.Contains("MovementX") && names.Contains("MovementY") && names.Contains("Grounded");
        }

        private static HashSet<string> HumanoidBonePaths(GameObject avatar)
        {
            var set = new HashSet<string>();
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return set;
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(bone);
                if (t != null) set.Add(AnimationUtility.CalculateTransformPath(t, avatar.transform));
            }
            return set;
        }

        private static AnimatorController FindCvrAvatarAnimator()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var lower = path.ToLowerInvariant();
                if (lower.Contains(".cck") && lower.Contains("/animations/") &&
                    lower.Contains("avatar") && lower.Contains("animator"))
                    return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
            return null;
        }

        private static AnimatorController CopyController(AnimatorController source, string avatarName)
        {
            var srcPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(srcPath)) return null;
            const string dir = "Assets/CVRFury Generated";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "CVRFury Generated");
            var dst = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{avatarName} AAS.controller");
            return AssetDatabase.CopyAsset(srcPath, dst)
                ? AssetDatabase.LoadAssetAtPath<AnimatorController>(dst) : null;
        }

        private static void Put(Dictionary<string, (AnimationClip, AnimationClip, string)> pairs,
                                string baseName, AnimationClip clip, bool isOn)
        {
            var key = Norm(baseName);
            pairs.TryGetValue(key, out var cur);
            if (isOn) cur.Item1 = clip; else cur.Item2 = clip;
            cur.Item3 = baseName.Trim();
            pairs[key] = cur;
        }

        private static Dictionary<string, (AnimationClip onClip, AnimationClip offClip, string baseName)> PairClips(
            string[] folders, List<string> onList, List<string> offList, out int clipCount)
        {
            var pairs = new Dictionary<string, (AnimationClip onClip, AnimationClip offClip, string baseName)>();
            clipCount = 0;
            // FindAssets with searchInFolders recurses into every subfolder, so nested folders (e.g.
            // Animations/Clothing/…) are covered automatically; passing several folders also covers siblings.
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", folders))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                clipCount++;
                // OFF is checked first so "Tail off" is the off clip, not a bare-name on clip.
                if (TryStripAny(clip.name, offList, out var baseOff)) Put(pairs, baseOff, clip, false);
                else if (onList.Count > 0 && TryStripAny(clip.name, onList, out var baseOn)) Put(pairs, baseOn, clip, true);
                // "Bare name = ON" mode: when no ON suffix is given, a clip with no off-suffix IS the on clip,
                // and its full name is the base (so "Tail" pairs with "Tail off"). This matches creators who
                // name the on animation exactly after the toggle.
                else if (onList.Count == 0) Put(pairs, clip.name, clip, true);
            }
            return pairs;
        }

        /// <summary>Keep only the entries that are real, existing project folders (and de-dupe).</summary>
        public static string[] ValidFolders(string[] folders)
        {
            if (folders == null) return new string[0];
            return folders.Where(f => !string.IsNullOrEmpty(f) && AssetDatabase.IsValidFolder(f))
                          .Distinct().ToArray();
        }

        /// <summary>One row of the smart-match review: a toggle/slider entry and the clips the tool thinks
        /// belong to it. <see cref="state"/> is 0 = exact match, 1 = fuzzy guess, 2 = nothing found.</summary>
        public struct Assignment
        {
            public string display;
            public string machine;
            public bool isSlider;
            public bool native;   // CVR drives GameObjects directly — no clip needed (and a clip would break it)
            public int state;
            public bool changed;
            public AnimationClip on;
            public AnimationClip off;
        }

        /// <summary>True if a toggle's settings already drive GameObjects directly (CVR-native targets).
        /// Such a toggle works without any clip; assigning one flips it into animation-clip mode and breaks
        /// the native object toggle.</summary>
        private static bool HasTargets(object toggleSettings)
        {
            var list = Reflect.GetField(toggleSettings, CckNames.Setting_GameObjectTargets) as System.Collections.IList;
            return list != null && list.Count > 0;
        }

        /// <summary>Compute, without changing anything, what clips pair to each toggle/slider. Exact
        /// name matches first; for anything left unmatched, fuzzy-guess from the leftover clip pairs so the
        /// user can confirm or correct in the review panel. Saved per-avatar manual picks are overlaid on
        /// top so re-previewing keeps your edits.</summary>
        public static List<Assignment> Preview(GameObject avatar, string[] folders, string onSuffix, string offSuffix)
        {
            var rows = new List<Assignment>();
            var cvr = CckAvatar.FindOn(avatar);
            var entries = cvr?.SettingsList;
            var valid = ValidFolders(folders);
            if (entries == null || valid.Length == 0) return rows;

            var pairs = PairClips(valid, SplitWords(onSuffix), SplitWords(offSuffix), out _);
            var used = new HashSet<string>();

            // Pass 1: exact match by display name or machine-name leaf.
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var machine = CckAvatar.EntryMachineName(entry) ?? "";
                if (string.IsNullOrEmpty(machine)) continue;
                bool isSlider = Reflect.GetField(entry, CckNames.Entry_SliderSettings) != null;
                var toggleObj = Reflect.GetField(entry, CckNames.Entry_ToggleSettings);
                bool isToggle = toggleObj != null;
                if (!isSlider && !isToggle) continue; // dropdowns etc. aren't clip-paired here

                var name = CckAvatar.EntryName(entry) ?? "";

                // A toggle that already drives GameObjects directly (CVR-native) works without any clip —
                // assigning one would flip it into animation-clip mode and break it. Mark it native and leave
                // its clips empty so Apply never touches it (unless the user assigns one by hand).
                if (isToggle && HasTargets(toggleObj))
                {
                    rows.Add(new Assignment { display = name, machine = machine, native = true, state = 0 });
                    continue;
                }

                string keyName = Norm(name), keyLeaf = Norm(Leaf(machine));
                string hit = pairs.ContainsKey(keyName) ? keyName : (pairs.ContainsKey(keyLeaf) ? keyLeaf : null);
                var a = new Assignment { display = name, machine = machine, isSlider = isSlider, state = 2 };
                if (hit != null) { var p = pairs[hit]; a.on = p.onClip; a.off = p.offClip; a.state = 0; used.Add(hit); }
                rows.Add(a);
            }

            // Pass 2: fuzzy-guess the unmatched ones from leftover clip pairs.
            for (int i = 0; i < rows.Count; i++)
            {
                var a = rows[i];
                if (a.state == 0) continue;
                string target = Norm(a.display.Length > 0 ? a.display : Leaf(a.machine));
                string bestKey = null; double best = 0;
                foreach (var kv in pairs)
                {
                    if (used.Contains(kv.Key)) continue;
                    double s = Similarity(target, kv.Key);
                    if (s > best) { best = s; bestKey = kv.Key; }
                }
                if (bestKey != null && best >= 0.45)
                {
                    var p = pairs[bestKey]; a.on = p.onClip; a.off = p.offClip; a.state = 1; used.Add(bestKey);
                    rows[i] = a;
                }
            }

            OverlaySaved(avatar, rows);
            return rows;
        }

        /// <summary>Apply explicit per-toggle clip assignments from the review panel, then (optionally)
        /// build &amp; attach the controller. Also saves the assignments per-avatar.</summary>
        public static string ApplyAndBuild(GameObject avatar, List<Assignment> rows, bool build, AnimatorController controller)
        {
            var cvr = CckAvatar.FindOn(avatar);
            if (cvr == null) return "No CVRAvatar found (run step 1 first).";
            var entries = cvr.SettingsList;
            if (entries == null) return "No Advanced Avatar Settings entries — run step 1 first.";

            var byMachine = new Dictionary<string, Assignment>();
            foreach (var r in rows) if (!string.IsNullOrEmpty(r.machine)) byMachine[r.machine] = r;
            int linked = 0;
            foreach (var entry in entries)
            {
                var machine = CckAvatar.EntryMachineName(entry) ?? "";
                if (!byMachine.TryGetValue(machine, out var a)) continue;
                if (a.on == null && a.off == null) continue;
                bool ok = a.isSlider ? cvr.SetSliderClips(entry, a.off, a.on)  // slider: off=min(0), on=max(1)
                                     : cvr.SetToggleClips(entry, a.on, a.off);
                if (ok) linked++;
            }

            SaveAssignments(avatar, rows);
            string report = build ? BuildAndAttach(cvr, avatar, entries, controller) : "";
            cvr.Persist();

            int still = rows.Count(r => r.on == null && r.off == null);
            return $"Applied {linked} reviewed clip assignment(s)" +
                   (still > 0 ? $", {still} toggle(s) still have no clip" : "") + "." +
                   (string.IsNullOrEmpty(report) ? "" : "\n\n" + report);
        }

        // --- fuzzy similarity (normalized strings; containment + edit-distance ratio) ---
        private static double Similarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            if (a == b) return 1;
            if (a.Contains(b) || b.Contains(a)) return 0.85;
            int d = Levenshtein(a, b);
            return 1.0 - (double)d / Mathf.Max(a.Length, b.Length);
        }

        private static int Levenshtein(string a, string b)
        {
            var dp = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) dp[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int prev = dp[0]; dp[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cur = dp[j];
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[j] = Mathf.Min(Mathf.Min(dp[j] + 1, dp[j - 1] + 1), prev + cost);
                    prev = cur;
                }
            }
            return dp[b.Length];
        }

        // --- per-avatar persistence of reviewed/manual clip picks (EditorPrefs JSON) ---
        [System.Serializable] private class Saved { public List<SavedRow> rows = new List<SavedRow>(); }
        [System.Serializable] private class SavedRow { public string machine, onGuid, offGuid; }

        private static string PersistKey(GameObject avatar) => "CVRFury.ClipReview." + (avatar != null ? avatar.name : "");

        private static void SaveAssignments(GameObject avatar, List<Assignment> rows)
        {
            var s = new Saved();
            foreach (var r in rows)
            {
                if (string.IsNullOrEmpty(r.machine) || (r.on == null && r.off == null)) continue;
                s.rows.Add(new SavedRow { machine = r.machine, onGuid = GuidOf(r.on), offGuid = GuidOf(r.off) });
            }
            EditorPrefs.SetString(PersistKey(avatar), JsonUtility.ToJson(s));
        }

        private static void OverlaySaved(GameObject avatar, List<Assignment> rows)
        {
            var json = EditorPrefs.GetString(PersistKey(avatar), "");
            if (string.IsNullOrEmpty(json)) return;
            Saved s; try { s = JsonUtility.FromJson<Saved>(json); } catch { return; }
            if (s?.rows == null) return;
            var map = s.rows.Where(r => r != null && r.machine != null).ToDictionary(r => r.machine, r => r);
            for (int i = 0; i < rows.Count; i++)
            {
                var a = rows[i];
                if (a.machine == null || !map.TryGetValue(a.machine, out var sr)) continue;
                var on = ClipOf(sr.onGuid); var off = ClipOf(sr.offGuid);
                if (on != null || off != null) { a.on = on; a.off = off; a.state = 0; rows[i] = a; } // manual pick wins
            }
        }

        private static string GuidOf(AnimationClip c) =>
            c == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c));
        private static AnimationClip ClipOf(string guid) =>
            string.IsNullOrEmpty(guid) ? null
                : AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));

        private static bool TryStripSuffix(string name, string suffix, out string baseName)
        {
            baseName = null;
            var n = (name ?? "").Trim();
            if (n.Length < suffix.Length) return false;
            if (!n.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) return false;
            baseName = n.Substring(0, n.Length - suffix.Length).TrimEnd(' ', '_', '-', '.');
            return baseName.Length > 0;
        }

        /// <summary>Split a comma-separated suffix field into individual trimmed words (longest first, so
        /// "toggled on" is tried before "on").</summary>
        private static List<string> SplitWords(string s) =>
            (s ?? "").Split(',').Select(w => w.Trim()).Where(w => w.Length > 0)
                .OrderByDescending(w => w.Length).Distinct().ToList();

        /// <summary>True if the name ends with any of the suffixes; returns the stripped base name.</summary>
        private static bool TryStripAny(string name, List<string> suffixes, out string baseName)
        {
            foreach (var s in suffixes)
                if (TryStripSuffix(name, s, out baseName)) return true;
            baseName = null;
            return false;
        }

        private static string Leaf(string machine) =>
            string.IsNullOrEmpty(machine) ? machine
                : (machine.Contains('/') ? machine.Substring(machine.LastIndexOf('/') + 1) : machine);

        private static string Norm(string s) =>
            string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        private static float ToFloat(object o) { try { return o == null ? 0f : System.Convert.ToSingle(o); } catch { return 0f; } }
        private static int ToInt(object o) { try { return o == null ? 0 : System.Convert.ToInt32(o); } catch { return 0; } }
    }
}
