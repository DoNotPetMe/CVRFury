using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Finds the dance animations from a "Sync Dances"-style package already in the project and turns them
    /// into a CVR-native dance menu — a synced Advanced Avatar Settings dropdown (Off + one option per dance)
    /// driving an exclusive full-body animator layer. We only use the dance *clips*; none of the package's
    /// VRChat controller/menu is needed. CVR syncs the dropdown's value, so everyone sees the same dance.
    /// </summary>
    internal static class SyncDances
    {
        /// <summary>Dance clips found in <paramref name="folderPath"/> (or auto-located by folder name when
        /// blank). Skips obvious non-dance/system clips. Also returns the folder it used.</summary>
        public static List<AnimationClip> Detect(string folderPath, out string usedFolder)
        {
            usedFolder = folderPath;
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                usedFolder = AutoLocate();

            var clips = new List<AnimationClip>();
            if (string.IsNullOrEmpty(usedFolder) || !AssetDatabase.IsValidFolder(usedFolder)) return clips;

            string[] skip = { "idle", "afk", "locomotion", "reset", "tpose", "t-pose", "writedefault", "base" };
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { usedFolder }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                var n = clip.name.ToLowerInvariant();
                if (System.Array.Exists(skip, w => n.Contains(w))) continue;
                clips.Add(clip);
            }
            return clips.OrderBy(c => c.name).ToList();
        }

        private static string AutoLocate()
        {
            // A folder whose path mentions "sync dance" (any version).
            foreach (var guid in AssetDatabase.FindAssets("t:Folder"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var lower = p.ToLowerInvariant();
                if (lower.Contains("sync dance") || lower.Contains("syncdance") || lower.Contains("sync_dance"))
                    return p;
            }
            return null;
        }

        /// <summary>Build the dance dropdown + exclusive layer (Off + each dance) into every target controller,
        /// matching the controller's WriteDefaults so it doesn't fight locomotion. Returns an error or null.</summary>
        public static string Build(GameObject avatar, CckAvatar cvr, AnimatorController[] controllers,
                                   List<AnimationClip> dances, string folder, string menuName = "Dances", string param = "Dances")
        {
            if (cvr == null) return "No CVRAvatar — run Step 1 first.";
            if (controllers == null || controllers.Length == 0) return "No writable controller — run Step 2 first.";
            if (dances == null || dances.Count == 0) return "No dance clips found to build from.";

            // Auto-match an audio clip to each dance by name, and (if found) bake a clip that plays the dance
            // and its sound together.
            var audioMap = AudioByName(folder);
            int withAudio = 0;

            // Option 0 = Off (empty), then one per dance.
            var clips = new AnimationClip[dances.Count + 1];
            clips[0] = null;
            for (int i = 0; i < dances.Count; i++)
            {
                var dance = dances[i];
                if (audioMap.TryGetValue(Norm(dance.name), out var audio) && audio != null)
                {
                    clips[i + 1] = EmoteSlots.MakeAudioClip(avatar, "Dance " + dance.name, dance, audio);
                    withAudio++;
                }
                else clips[i + 1] = dance;
            }
            var names = new[] { "Off" }.Concat(dances.Select(d => d.name)).ToArray();

            foreach (var c in controllers)
            {
                if (c.parameters.Any(p => p.name == param)) continue; // already built here
                // Int param so it matches the CVR dropdown (an Int control) — a Float would show as a slider
                // and the dropdown's integer selection wouldn't drive it. States stay WriteDefaults-off so the
                // empty "Off" option contributes nothing and locomotion shows through (no motorbike).
                AnimatorUtil.AddModesLayer(c, "CVRFury Dances", param, clips, 0.1f, defaultIndex: 0, useInt: true);
                EditorUtility.SetDirty(c);
            }

            if (!cvr.AddDropdown(menuName, param, names, 0, false))
                return "Built the dance layer, but couldn't add the menu dropdown (AAS write failed).";

            AssetDatabase.SaveAssets();
            cvr.Persist();
            return $"Built a synced \"{menuName}\" dropdown with {dances.Count} dance(s) (plus Off)" +
                   (withAudio > 0 ? $", {withAudio} with auto-matched audio" : " (no matching audio found by name)") +
                   ". Everyone sees the same selection in CVR. Test in Play mode or upload.";
        }

        /// <summary>AudioClips in the dances folder (recursive), keyed by normalised name, so each dance can
        /// find the sound that shares its name. Falls back to the whole project if no folder is given.</summary>
        private static Dictionary<string, AudioClip> AudioByName(string folder)
        {
            var map = new Dictionary<string, AudioClip>();
            var guids = !string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder)
                ? AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
                : AssetDatabase.FindAssets("t:AudioClip");
            foreach (var g in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g));
                if (a == null) continue;
                var key = Norm(a.name);
                if (!map.ContainsKey(key)) map[key] = a;
            }
            return map;
        }

        private static string Norm(string s) =>
            string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
