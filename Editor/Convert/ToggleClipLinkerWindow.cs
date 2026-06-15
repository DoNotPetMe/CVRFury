using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Scans a folder of animation clips and links them onto the avatar's existing ChilloutVR Advanced
    /// Avatar Settings toggle entries as on/off clips. You tell it how on/off clips are named (the word
    /// each clip's name ends with — e.g. "toggled" for ON and "default" for OFF); it pairs the clips by
    /// their base name, matches each base to a toggle entry (by display name or machine-name leaf), and
    /// assigns the pair.
    ///
    /// It is strictly non-destructive: it never clears or removes AAS entries and never touches the
    /// animator controller — it only fills in the on/off clip fields of toggles it can match.
    /// </summary>
    public class ToggleClipLinkerWindow : EditorWindow
    {
        private GameObject _avatar;
        private DefaultAsset _folder;
        private string _onSuffix = "toggled";
        private string _offSuffix = "default";
        private Vector2 _scroll;
        private string _report = "";

        [MenuItem("Tools/CVRFury/Link Toggle Animations from Folder", false, 2)]
        public static void Open()
        {
            var w = GetWindow<ToggleClipLinkerWindow>("Toggle Clip Linker");
            w.minSize = new Vector2(420, 380);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Link Toggle Animations from a Folder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assigns on/off animation clips to your existing CCK toggle entries by matching names.\n\n" +
                "Name the suffix word your clips use: a clip whose name ends with the ON word is the toggle's " +
                "on-clip, the OFF word is the off-clip. The text before that word is the base name, which is " +
                "matched to a toggle (by its menu name or the last part of its Machine Name).\n\n" +
                "Non-destructive: it only fills clip fields. It never clears entries or edits your controller.",
                MessageType.Info);

            _avatar = (GameObject)EditorGUILayout.ObjectField(
                "Avatar", _avatar != null ? _avatar : Selection.activeGameObject, typeof(GameObject), true);
            _folder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Animations Folder", _folder, typeof(DefaultAsset), false);
            _onSuffix = EditorGUILayout.TextField("ON  clip name ends with", _onSuffix);
            _offSuffix = EditorGUILayout.TextField("OFF clip name ends with", _offSuffix);

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

                if (TryStripSuffix(clip.name, on, out var baseOn))
                    Put(pairs, baseOn, clip, true);
                else if (TryStripSuffix(clip.name, off, out var baseOff))
                    Put(pairs, baseOff, clip, false);
            }

            // --- match each toggle entry to a clip pair ---
            int linked = 0;
            var noClip = new List<string>();
            var usedBases = new HashSet<string>();
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var name = CckAvatar.EntryName(entry) ?? "";
                var machine = CckAvatar.EntryMachineName(entry) ?? "";
                var leaf = machine.Contains('/') ? machine.Substring(machine.LastIndexOf('/') + 1) : machine;

                // only toggles can take clips
                string keyName = Norm(name), keyLeaf = Norm(leaf);
                string hitKey = pairs.ContainsKey(keyName) ? keyName : (pairs.ContainsKey(keyLeaf) ? keyLeaf : null);
                if (hitKey == null) { if (!string.IsNullOrEmpty(name)) noClip.Add($"{name}"); continue; }

                var p = pairs[hitKey];
                if (cvr.SetToggleClips(entry, p.onClip, p.offClip)) { linked++; usedBases.Add(hitKey); }
                else noClip.Add($"{name} (not a toggle)");
            }

            cvr.Persist();
            Reselect(_avatar);

            var unusedClips = pairs.Where(kv => !usedBases.Contains(kv.Key)).Select(kv => kv.Value.baseName).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scanned {clipCount} clip(s) → {pairs.Count} on/off pair(s) found in:\n  {folderPath}\n");
            sb.AppendLine($"Linked clips onto {linked} toggle(s).");
            if (noClip.Count > 0)
                sb.AppendLine($"\nToggles with NO matching clip ({noClip.Count}):\n  " + string.Join("\n  ", noClip));
            if (unusedClips.Count > 0)
                sb.AppendLine($"\nClip pairs with NO matching toggle ({unusedClips.Count}) — name mismatch:\n  " +
                              string.Join("\n  ", unusedClips));
            sb.AppendLine("\nNow press the CCK's Create Controller → Attach. Each linked toggle now generates a " +
                          "parameter (clearing the red ❗) and plays its on/off clip.");
            _report = sb.ToString();
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

        /// <summary>If <paramref name="name"/> ends with <paramref name="suffix"/> (case-insensitive, with
        /// an optional space/underscore/dash/dot separator), returns the base name before it.</summary>
        private static bool TryStripSuffix(string name, string suffix, out string baseName)
        {
            baseName = null;
            var n = (name ?? "").Trim();
            if (n.Length < suffix.Length) return false;
            if (!n.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) return false;
            baseName = n.Substring(0, n.Length - suffix.Length).TrimEnd(' ', '_', '-', '.');
            return baseName.Length > 0;
        }

        /// <summary>Normalise a name for matching: letters/digits only, lower-case. So "  Witch Hat",
        /// "Witch_Hat" and "witchhat" all compare equal.</summary>
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var chars = s.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        private static void Reselect(GameObject target)
        {
            Selection.activeObject = null;
            EditorApplication.delayCall += () => { if (target != null) Selection.activeObject = target; };
        }
    }
}
