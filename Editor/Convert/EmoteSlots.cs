using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// ChilloutVR's emote wheel (a separate in-game menu from Advanced Avatar Settings) works by setting an
    /// <c>Emote</c> integer animator parameter to a slot number; the avatar controller has states reached by
    /// a transition condition <c>Emote Equals N</c> whose motion is that slot's animation. This finds those
    /// real emote slots (so we know we're targeting the wheel, not a toggle) and lets each slot's clip — and
    /// optional audio — be swapped for something custom.
    /// </summary>
    internal static class EmoteSlots
    {
        public struct Slot
        {
            public int index;            // the Emote parameter value (slot number)
            public AnimationClip clip;    // current animation in that slot
            public string location;       // "Layer/State" for display
        }

        /// <summary>The emote slots found in a controller (states gated by `Emote Equals N`).</summary>
        public static List<Slot> Detect(AnimatorController c)
        {
            var map = MapStates(c);
            return map.Select(kv => new Slot
            {
                index = kv.Key,
                clip = kv.Value.state.motion as AnimationClip,
                location = $"{kv.Value.layer}/{kv.Value.state.name}",
            }).OrderBy(s => s.index).ToList();
        }

        /// <summary>Replace the clip in emote slot <paramref name="index"/>. Returns true if the slot existed.</summary>
        public static bool SetClip(AnimatorController c, int index, AnimationClip clip)
        {
            var map = MapStates(c);
            if (!map.TryGetValue(index, out var hit)) return false;
            Undo.RecordObject(hit.state, "Set emote clip");
            hit.state.motion = clip;
            EditorUtility.SetDirty(c);
            return true;
        }

        // index -> (state, layerName). First match per index wins.
        private static Dictionary<int, (AnimatorState state, string layer)> MapStates(AnimatorController c)
        {
            var map = new Dictionary<int, (AnimatorState, string)>();
            if (c == null) return map;
            foreach (var layer in c.layers)
                Scan(layer.stateMachine, layer.name, map);
            return map;
        }

        private static void Scan(AnimatorStateMachine sm, string layer, Dictionary<int, (AnimatorState, string)> map)
        {
            if (sm == null) return;
            foreach (var t in sm.anyStateTransitions) Consider(t, layer, map);
            foreach (var cs in sm.states)
                foreach (var t in cs.state.transitions) Consider(t, layer, map);
            foreach (var sub in sm.stateMachines) Scan(sub.stateMachine, layer, map);
        }

        private static void Consider(AnimatorStateTransition t, string layer, Dictionary<int, (AnimatorState, string)> map)
        {
            if (t == null || t.destinationState == null) return;
            foreach (var cond in t.conditions)
                if (cond.parameter == "Emote" && cond.mode == AnimatorConditionMode.Equals)
                {
                    int idx = Mathf.RoundToInt(cond.threshold);
                    if (idx > 0 && !map.ContainsKey(idx)) map[idx] = (t.destinationState, layer);
                }
        }

        /// <summary>Give an emote slot a sound: an AudioSource on a child object that the slot's clip switches
        /// on (so it plays for the emote's duration). Builds a combined clip = the chosen animation + an
        /// "activate the audio object" curve, and points the slot at it. Returns an error, or null on success.</summary>
        public static string SetClipWithAudio(GameObject avatar, AnimatorController[] controllers, int index,
                                              AnimationClip clip, AudioClip audio)
        {
            if (clip == null) return $"Emote {index}: pick an animation before adding audio.";

            // 1) An always-on (play-on-awake) AudioSource on a child that starts disabled.
            var goName = $"CVRFury Emote {index} Audio";
            var existing = avatar.transform.Find(goName);
            GameObject audioGo = existing != null ? existing.gameObject : new GameObject(goName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(audioGo, "Add emote audio");
                audioGo.transform.SetParent(avatar.transform, false);
            }
            var src = audioGo.GetComponent<AudioSource>() ?? Undo.AddComponent<AudioSource>(audioGo);
            src.clip = audio;
            src.playOnAwake = true;
            src.spatialBlend = 1f; // 3D, so others hear it positionally
            audioGo.SetActive(false);

            // 2) A combined clip: the animation, plus a curve that activates the audio object.
            var combined = Object.Instantiate(clip);
            combined.name = clip.name + " +audio";
            float len = Mathf.Max(combined.length, 0.05f);
            var binding = EditorCurveBinding.FloatCurve(goName, typeof(GameObject), "m_IsActive");
            AnimationUtility.SetEditorCurve(combined, binding, AnimationCurve.Constant(0f, len, 1f));

            var dir = "Assets/CVRFury Emotes";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "CVRFury Emotes");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{combined.name}.anim");
            AssetDatabase.CreateAsset(combined, path);

            bool any = false;
            foreach (var c in controllers) if (c != null && SetClip(c, index, combined)) any = true;
            return any ? null : $"Emote slot {index} not found in the controller.";
        }
    }
}
