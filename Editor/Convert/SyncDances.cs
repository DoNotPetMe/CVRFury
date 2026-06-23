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

            // Build the dances as states INSIDE the base locomotion layer (layer 0), driven by the Dances
            // int param and returning to locomotion when 0 — exactly how CVR handles emotes. A separate
            // full-body override layer fights locomotion and motorbikes even when off; states inside the
            // locomotion layer don't, because when Dances=0 the layer simply stays in Standard Locomotion.
            int added = 0;
            foreach (var c in controllers.Where(c => c != null).Distinct())
            {
                CleanOldDances(c);                       // remove any previous dance layer/states (re-run safe)
                AnimatorUtil.EnsureIntParam(c, param, 0);
                if (c.layers.Length == 0) continue;
                var sm = c.layers[0].stateMachine;
                var home = sm.defaultState;              // Standard Locomotion / locomotion default
                bool wd = home != null && home.writeDefaultValues; // match the locomotion layer's WD
                for (int i = 0; i < dances.Count; i++)
                {
                    var clip = clips[i + 1];
                    var st = sm.AddState("CVRFury Dance " + dances[i].name,
                        new Vector3(360, 80 + i * 60, 0));
                    st.motion = clip;
                    st.writeDefaultValues = wd;

                    var toDance = sm.AddAnyStateTransition(st);
                    toDance.hasExitTime = false; toDance.hasFixedDuration = true; toDance.duration = 0.1f;
                    toDance.canTransitionToSelf = false;
                    toDance.AddCondition(AnimatorConditionMode.Equals, i + 1, param);

                    if (home != null)
                    {
                        var back = st.AddTransition(home);  // back to locomotion when the wheel is set to Off
                        back.hasExitTime = false; back.hasFixedDuration = true; back.duration = 0.1f;
                        back.AddCondition(AnimatorConditionMode.Equals, 0, param);
                    }
                }
                EditorUtility.SetDirty(c);
                added++;
            }
            if (added == 0) return "No controller layer to add dances to — run Step 2 (Build & attach) first.";

            // Refresh the menu dropdown (remove a previous one with the same machine name first).
            RemoveAasEntry(cvr, param);
            if (!cvr.AddDropdown(menuName, param, names, 0, false))
                return "Added the dance states, but couldn't add the menu dropdown (AAS write failed).";

            AssetDatabase.SaveAssets();
            cvr.Persist();
            return $"Built a synced \"{menuName}\" dropdown with {dances.Count} dance(s) (plus Off)" +
                   (withAudio > 0 ? $", {withAudio} with auto-matched audio" : " (no matching audio found by name)") +
                   ". They live in the locomotion layer and return to standing when set to Off — no motorbike. " +
                   "Test in Play mode or upload.";
        }

        /// <summary>Remove any previous CVRFury dance setup (old override layer + in-layer dance states) so
        /// re-running is clean.</summary>
        private static void CleanOldDances(AnimatorController c)
        {
            AnimatorUtil.RemoveLayers(c, "CVRFury Dances"); // old separate-override-layer approach
            if (c.layers.Length == 0) return;
            var sm = c.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions.ToList())
                if (t.destinationState != null && t.destinationState.name.StartsWith("CVRFury Dance "))
                    sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states.ToList())
                if (s.state != null && s.state.name.StartsWith("CVRFury Dance "))
                    sm.RemoveState(s.state);
        }

        private static void RemoveAasEntry(CckAvatar cvr, string machine)
        {
            var list = cvr.SettingsList;
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
                if (CckAvatar.EntryMachineName(list[i]) == machine) list.RemoveAt(i);
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
