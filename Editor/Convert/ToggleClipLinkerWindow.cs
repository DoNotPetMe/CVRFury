using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Scans a folder of animation clips and links them onto the avatar's existing ChilloutVR Advanced
    /// Avatar Settings toggle entries as on/off clips, then (optionally) builds and attaches a working
    /// controller so the entries' parameters actually exist — clearing the CCK's "parameter not present"
    /// warnings.
    ///
    /// You tell it how on/off clips are named (the word each clip's name ends with — e.g. "toggled" for ON
    /// and "default" for OFF); it pairs the clips by base name, matches each base to a toggle entry (by
    /// display name or machine-name leaf), and assigns the pair. It is non-destructive: it never clears or
    /// removes AAS entries — it only fills clip fields and, when asked, generates a controller asset.
    /// </summary>
    public class ToggleClipLinkerWindow : EditorWindow
    {
        private GameObject _avatar;
        private DefaultAsset _folder;
        private string _onSuffix = "toggled";
        private string _offSuffix = "default";
        private AnimatorController _controller;
        private bool _buildController = true;
        private Vector2 _scroll;
        private string _report = "";

        [MenuItem("Tools/CVRFury/Link Toggle Animations from Folder", false, 2)]
        public static void Open()
        {
            var w = GetWindow<ToggleClipLinkerWindow>("Toggle Clip Linker");
            w.minSize = new Vector2(440, 460);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Link Toggle Animations from a Folder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assigns on/off clips to your CCK toggle entries by matching names: a clip ending with the " +
                "ON word is the on-clip, the OFF word is the off-clip; the text before that word is matched " +
                "to a toggle (by its menu name or the last part of its Machine Name).\n\n" +
                "Non-destructive: it only fills clip fields and (optionally) generates a controller. It never " +
                "clears entries.",
                MessageType.Info);

            _avatar = (GameObject)EditorGUILayout.ObjectField(
                "Avatar", _avatar != null ? _avatar : Selection.activeGameObject, typeof(GameObject), true);
            _folder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Animations Folder", _folder, typeof(DefaultAsset), false);
            _onSuffix = EditorGUILayout.TextField("ON  clip name ends with", _onSuffix);
            _offSuffix = EditorGUILayout.TextField("OFF clip name ends with", _offSuffix);

            EditorGUILayout.Space();
            _buildController = EditorGUILayout.ToggleLeft(
                "Build & attach a controller (creates the parameters → clears the red ❗)", _buildController);
            using (new EditorGUI.DisabledScope(!_buildController))
            {
                EditorGUI.indentLevel++;
                _controller = (AnimatorController)EditorGUILayout.ObjectField(
                    "Controller (optional)", _controller, typeof(AnimatorController), false);
                EditorGUILayout.HelpBox(
                    "Optional. A COPY of this controller is made and the toggle layers are added to the copy " +
                    "(your original is never modified). Leave empty to copy ChilloutVR's stock AvatarAnimator " +
                    "(so the avatar keeps locomotion). The copy is attached as the avatar's AAS controller.",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_avatar == null || _folder == null ||
                                               string.IsNullOrWhiteSpace(_onSuffix) || string.IsNullOrWhiteSpace(_offSuffix)))
            {
                if (GUILayout.Button("Scan & Link", GUILayout.Height(30))) Run();
            }

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void Run()
        {
            var folderPath = AssetDatabase.GetAssetPath(_folder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                _report = "That object isn't a folder. Drag a project folder into 'Animations Folder'.";
                return;
            }

            var cvr = CckAvatar.FindOn(_avatar);
            if (cvr == null)
            {
                _report = "No CVRAvatar found on the selected avatar (is the CCK installed and the component added?).";
                return;
            }
            var entries = cvr.SettingsList;
            if (entries == null || entries.Count == 0)
            {
                _report = "The avatar has no Advanced Avatar Settings entries yet. Run " +
                          "'Link CCK Parameters from VRChat Menu' first to create them, then link clips here.";
                return;
            }

            // --- pair clips by base name ---
            var on = _onSuffix.Trim();
            var off = _offSuffix.Trim();
            var pairs = new Dictionary<string, (AnimationClip onClip, AnimationClip offClip, string baseName)>();
            int clipCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                clipCount++;
                if (TryStripSuffix(clip.name, on, out var baseOn)) Put(pairs, baseOn, clip, true);
                else if (TryStripSuffix(clip.name, off, out var baseOff)) Put(pairs, baseOff, clip, false);
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
                var leaf = Leaf(machine);

                string keyName = Norm(name), keyLeaf = Norm(leaf);
                string hitKey = pairs.ContainsKey(keyName) ? keyName : (pairs.ContainsKey(keyLeaf) ? keyLeaf : null);
                if (hitKey == null) { if (!string.IsNullOrEmpty(name)) noClip.Add(name); continue; }

                var p = pairs[hitKey];
                if (cvr.SetToggleClips(entry, p.onClip, p.offClip)) { linked++; usedBases.Add(hitKey); }
            }

            // --- optionally build + attach a controller so every parameter exists (clears red ❗) ---
            string buildReport = "";
            if (_buildController) buildReport = BuildAndAttach(cvr, entries);

            cvr.Persist();
            Reselect(_avatar);

            var unusedClips = pairs.Where(kv => !usedBases.Contains(kv.Key)).Select(kv => kv.Value.baseName).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scanned {clipCount} clip(s) → {pairs.Count} on/off pair(s) in:\n  {folderPath}\n");
            sb.AppendLine($"Linked clips onto {linked} toggle(s).");
            if (noClip.Count > 0)
                sb.AppendLine($"\nToggles with NO matching clip ({noClip.Count}):\n  " + string.Join("\n  ", noClip));
            if (unusedClips.Count > 0)
                sb.AppendLine($"\nClip pairs with NO matching toggle ({unusedClips.Count}):\n  " + string.Join("\n  ", unusedClips));
            if (!string.IsNullOrEmpty(buildReport)) sb.AppendLine("\n" + buildReport);
            else sb.AppendLine("\nNext: press the CCK's Create Controller → Attach to generate the parameters.");
            _report = sb.ToString();
        }

        /// <summary>Copy a base controller and add a parameter (plus a clip-driven layer for toggles that
        /// have clips) for every AAS entry, then attach it. After this the controller contains every
        /// machine-name parameter, so the CCK's "parameter not present" warnings clear.</summary>
        private string BuildAndAttach(CckAvatar cvr, System.Collections.IList entries)
        {
            var source = _controller != null ? _controller : FindCvrAvatarAnimator();
            if (source == null)
                return "Controller build skipped: no controller given and CVR's stock AvatarAnimator wasn't found. " +
                       "Assign a controller in the 'Controller (optional)' slot and run again.";

            var gen = CopyController(source, _avatar.name);
            if (gen == null)
                return "Controller build skipped: couldn't copy the source controller.";

            var existing = new HashSet<string>(gen.parameters.Select(p => p.name));
            int paramsAdded = 0, layersBuilt = 0;

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
                    if (onClip != null || offClip != null)
                    {
                        AnimatorUtil.AddBoolToggleLayer(gen, "CVRFury: " + Leaf(machine), machine, offClip, onClip, defOn);
                        layersBuilt++;
                    }
                    else AnimatorUtil.EnsureBoolParam(gen, machine, defOn);
                    paramsAdded++;
                }
                else if (slider != null)
                {
                    float dv = ToFloat(Reflect.GetField(slider, CckNames.Setting_DefaultFloat));
                    AnimatorUtil.EnsureFloatParam(gen, machine, dv);
                    paramsAdded++;
                }
                else if (dropdown != null)
                {
                    int dv = ToInt(Reflect.GetField(dropdown, CckNames.Setting_DefaultInt));
                    AnimatorUtil.EnsureIntParam(gen, machine, dv);
                    paramsAdded++;
                }
                existing.Add(machine);
            }

            EditorUtility.SetDirty(gen);
            AssetDatabase.SaveAssets();
            cvr.AttachGeneratedController(gen);
            AssetDatabase.SaveAssets();

            return $"Built and attached controller '{gen.name}':\n  {paramsAdded} parameter(s) added " +
                   $"({layersBuilt} with a clip-driven toggle layer).\n  Saved to {AssetDatabase.GetAssetPath(gen)}.\n" +
                   "The red ❗ should now be gone (every entry's parameter exists in the controller), and " +
                   "toggles with clips will play them. No need to click Create Controller.";
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

        private static string Leaf(string machine) =>
            string.IsNullOrEmpty(machine) ? machine
                : (machine.Contains('/') ? machine.Substring(machine.LastIndexOf('/') + 1) : machine);

        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private static float ToFloat(object o) { try { return o == null ? 0f : System.Convert.ToSingle(o); } catch { return 0f; } }
        private static int ToInt(object o) { try { return o == null ? 0 : System.Convert.ToInt32(o); } catch { return 0; } }

        private static void Reselect(GameObject target)
        {
            Selection.activeObject = null;
            EditorApplication.delayCall += () => { if (target != null) Selection.activeObject = target; };
        }
    }
}
