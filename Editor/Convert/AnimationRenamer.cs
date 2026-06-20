using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// LAST-RESORT fallback for when a pack's clip names are too inconsistent for the on/off matcher. It
    /// scans every animation clip in a folder, classifies each as an ON or OFF clip by recognising common
    /// words at the END of the name (show/hide, enable/disable, on/off, …), then renames the clip so it ends
    /// with the user's chosen markers (e.g. "1" for on, "0" for off). Renaming asset files is destructive and
    /// can break references, so this is opt-in and confirmed.
    /// </summary>
    internal static class AnimationRenamer
    {
        private static readonly string[] OnWords =
            { "toggled", "enabled", "enable", "shown", "show", "visible", "equip", "equipped", "true", "active", "on" };
        private static readonly string[] OffWords =
            { "disabled", "disable", "hidden", "hide", "default", "none", "unequip", "false", "inactive", "off" };

        public static string RenameEndings(string folderPath, string onEnding, string offEnding)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return "Pick a valid folder of animation clips first.";
            if (string.IsNullOrEmpty(onEnding) || string.IsNullOrEmpty(offEnding))
                return "Set both the ON and OFF endings.";

            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
            int renamed = 0;
            var skipped = new List<string>();
            var taken = new HashSet<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null) continue;
                    var name = clip.name;

                    string baseName = null, ending = null;
                    if (TryStripEndWord(name, OnWords, out var bOn)) { baseName = bOn; ending = onEnding; }
                    else if (TryStripEndWord(name, OffWords, out var bOff)) { baseName = bOff; ending = offEnding; }

                    if (baseName == null) { skipped.Add(name); continue; }

                    var target = baseName + ending;
                    var unique = target;
                    int n = 2;
                    while (!taken.Add(unique)) unique = target + " " + n++;
                    if (unique == name) { continue; }

                    var err = AssetDatabase.RenameAsset(path, unique);
                    if (string.IsNullOrEmpty(err)) renamed++;
                    else skipped.Add($"{name} ({err})");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            var msg = $"Renamed {renamed} clip(s) to end with \"{onEnding}\"/\"{offEnding}\".";
            if (skipped.Count > 0)
                msg += $"\nLeft {skipped.Count} alone (couldn't tell on/off, or name clash): " +
                       string.Join(", ", skipped.GetRange(0, System.Math.Min(skipped.Count, 25))) +
                       (skipped.Count > 25 ? " …" : "");
            msg += "\nNow set Step 2's ON/OFF suffix fields to your chosen endings and link clips.";
            return msg;
        }

        /// <summary>If <paramref name="name"/> ends with one of <paramref name="words"/> as a separate token
        /// (preceded by a separator, or the whole name), return the part before it. Requiring a separator
        /// avoids false hits like "Dragon" ending in "on".</summary>
        private static bool TryStripEndWord(string name, string[] words, out string baseName)
        {
            var lower = name.ToLowerInvariant();
            foreach (var w in words)
            {
                if (!lower.EndsWith(w)) continue;
                int cut = name.Length - w.Length;
                if (cut == 0) { baseName = ""; return true; } // whole name is the word
                char before = name[cut - 1];
                if (before == ' ' || before == '_' || before == '-' || before == '.')
                {
                    baseName = name.Substring(0, cut); // keep the separator so pairs share a base
                    return true;
                }
            }
            baseName = null;
            return false;
        }
    }
}
