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
        public static string LinkClips(GameObject avatar, string folderPath, string onSuffix, string offSuffix,
                                       bool build, AnimatorController controller)
        {
            if (avatar == null) return "Select your avatar first.";
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return "Pick a valid project folder of animation clips.";

            var cvr = CckAvatar.FindOn(avatar);
            if (cvr == null) return "No CVRAvatar found on the selected avatar (run step 1 first).";
            var entries = cvr.SettingsList;
            if (entries == null || entries.Count == 0)
                return "No Advanced Avatar Settings entries yet — run step 1 (Link parameters) first.";

            // ON/OFF suffix inputs accept several comma-separated alternatives (e.g. "toggled, on, enabled"
            // / "default, off, disabled"), so clips named differently from the rest still pair up.
            var onList = SplitWords(onSuffix);
            var offList = SplitWords(offSuffix);
            if (onList.Count == 0 || offList.Count == 0)
                return "Set both the ON and OFF suffix words. You can list several comma-separated " +
                       "alternatives, e.g. ON: \"toggled, on, enabled\"  OFF: \"default, off, disabled\".";

            // --- pair clips by base name ---
            var pairs = new Dictionary<string, (AnimationClip onClip, AnimationClip offClip, string baseName)>();
            int clipCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                clipCount++;
                if (TryStripAny(clip.name, onList, out var baseOn)) Put(pairs, baseOn, clip, true);
                else if (TryStripAny(clip.name, offList, out var baseOff)) Put(pairs, baseOff, clip, false);
            }

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

        private static string BuildAndAttach(CckAvatar cvr, GameObject avatar, System.Collections.IList entries,
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
