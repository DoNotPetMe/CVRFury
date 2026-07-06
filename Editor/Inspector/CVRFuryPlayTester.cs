using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// A CVR-flavoured "Gesture Manager": drive your avatar's Advanced Avatar Settings live in Unity Play
    /// mode, with no upload. CVR toggles/sliders/dropdowns are just animator parameters, so this lists the
    /// avatar's animator parameters (labelled from the AAS entries) and lets you flip/slide them — the
    /// animations play exactly as they will in-game.
    /// </summary>
    internal sealed class CVRFuryPlayTester : EditorWindow
    {
        private GameObject _avatar;
        private Vector2 _scroll;
        private bool _simulateStanding = true;
        private bool _showLayers;
        private int _stance;                       // 0 standing, 1 crouching, 2 prone
        private int _gestureLeft = 1, _gestureRight = 1; // popup index; CVR idx = index − 1

        // CVR's gesture indices (−1 … 6) in popup order, offset by +1.
        private static readonly string[] GestureNames =
        {
            "Open Hand (−1)", "Neutral (0)", "Fist (1)", "Thumbs Up (2)",
            "Gun (3)", "Point (4)", "Peace (5)", "Rock n Roll (6)",
        };

        // ChilloutVR drives these itself; hide them so the list shows only your settings.
        private static readonly HashSet<string> Core = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Crouching", "Prone", "Flying", "Sitting", "Swimming",
            "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight",
            "GestureLeftIdx", "GestureRightIdx",
            "VelocityX", "VelocityY", "VelocityZ", "Toggle", "Emote", "CancelEmote", "IsLocal",
        };

        [MenuItem("Tools/CVRFury/Play Mode Tester", false, 1)]
        public static void Open()
        {
            var w = GetWindow<CVRFuryPlayTester>("CVR Play Tester");
            w.minSize = new Vector2(320, 360);
            w.Show();
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint(); // keep controls in sync with the running animator
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            _avatar = (GameObject)EditorGUILayout.ObjectField("Avatar",
                _avatar != null ? _avatar : Selection.activeGameObject, typeof(GameObject), true);
            EditorGUILayout.HelpBox("Test your CVR toggles, sliders and emotes live in Play mode — like " +
                "Gesture Manager, for ChilloutVR. No upload needed.", MessageType.Info);

            if (!EditorApplication.isPlaying)
            {
                if (GUILayout.Button("Enter Play Mode")) EditorApplication.EnterPlaymode();
                EditorGUILayout.LabelField("Enter Play mode, then flip the controls that appear here.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            if (_avatar == null) { EditorGUILayout.HelpBox("Select your avatar.", MessageType.Warning); return; }
            var anim = _avatar.GetComponentInChildren<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                EditorGUILayout.HelpBox("This avatar has no Animator with a controller — run Step 2 " +
                    "(Build & attach a controller) first.", MessageType.Warning);
                return;
            }

            // Without locomotion input the avatar sits in CVR's no-input/falling pose (the "motorbike").
            // Simulate a grounded, standing avatar so what you see matches in-game idle while testing.
            _simulateStanding = EditorGUILayout.ToggleLeft(new GUIContent("Stand still (simulate grounded)",
                "Drives CVR's locomotion params to a grounded idle so the avatar doesn't motorbike in Play mode."),
                _simulateStanding);
            if (_simulateStanding) DriveStanding(anim);

            var labels = BuildLabelLookup(_avatar);
            var ps = anim.parameters;
            int shown = 0;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var p in ps)
            {
                if (Core.Contains(p.name)) continue;
                shown++;
                var label = labels.TryGetValue(p.name, out var l) ? l : p.name;
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        var b = anim.GetBool(p.name);
                        var nb = EditorGUILayout.ToggleLeft(label, b);
                        if (nb != b) anim.SetBool(p.name, nb);
                        break;
                    case AnimatorControllerParameterType.Float:
                        var f = anim.GetFloat(p.name);
                        var nf = EditorGUILayout.Slider(label, f, 0f, 1f);
                        if (!Mathf.Approximately(nf, f)) anim.SetFloat(p.name, nf);
                        break;
                    case AnimatorControllerParameterType.Int:
                        var i = anim.GetInteger(p.name);
                        var ni = EditorGUILayout.IntField(label, i);
                        if (ni != i) anim.SetInteger(p.name, ni);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (GUILayout.Button(label)) anim.SetTrigger(p.name);
                        break;
                }
            }
            EditorGUILayout.EndScrollView();

            if (shown == 0)
                EditorGUILayout.HelpBox("No adjustable settings found on this avatar's animator.", MessageType.None);
            else if (GUILayout.Button("Reset all to defaults"))
                foreach (var p in ps)
                {
                    if (Core.Contains(p.name)) continue;
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Bool: anim.SetBool(p.name, p.defaultBool); break;
                        case AnimatorControllerParameterType.Float: anim.SetFloat(p.name, p.defaultFloat); break;
                        case AnimatorControllerParameterType.Int: anim.SetInteger(p.name, p.defaultInt); break;
                    }
                }

            // Live layer diagnostics — to find what's posing the body (the motorbike). The culprit is an
            // UNMASKED layer at weight ~1 that isn't the base locomotion (often a merged VRChat Action/FX layer).
            EditorGUILayout.Space();
            _showLayers = EditorGUILayout.Foldout(_showLayers, "Animator layers (motorbike diagnostics)");
            if (_showLayers)
            {
                var ctrl = anim.runtimeAnimatorController as AnimatorController;
                for (int li = 0; li < anim.layerCount; li++)
                {
                    bool masked = ctrl != null && li < ctrl.layers.Length && ctrl.layers[li].avatarMask != null;
                    float w = anim.GetLayerWeight(li);
                    if (li == 0) w = 1f; // base layer is always full weight
                    EditorGUILayout.LabelField($"{li}: {anim.GetLayerName(li)}",
                        $"w={w:0.##}{(masked ? "  (masked)" : "")}");
                }

                // What the BASE layer is actually playing right now, and whether real CVR locomotion exists.
                EditorGUILayout.Space(2);
                var info = anim.GetCurrentAnimatorClipInfo(0);
                var playing = info.Length > 0
                    ? string.Join(", ", info.Select(ci => ci.clip ? ci.clip.name : "?"))
                    : "(nothing — base layer has no clip playing)";
                EditorGUILayout.LabelField("Base layer playing", playing);
                bool hasLoco = ctrl != null && ControllerGuard.HasCvrLocomotion(ctrl);
                EditorGUILayout.LabelField("Real CVR locomotion blendtree", hasLoco ? "yes" : "NO — this is the problem");
                var path = ctrl != null ? AssetDatabase.GetAssetPath(ctrl) : "(none)";
                EditorGUILayout.LabelField("Controller asset", string.IsNullOrEmpty(path) ? "(runtime/none)" : path);
                EditorGUILayout.LabelField("Suspect: an unmasked layer at w=1 that isn't locomotion, or no " +
                    "locomotion blendtree at all.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>Hold CVR's locomotion parameters at "grounded, not moving" in the chosen stance so
        /// the avatar idles (or crouches/prones) instead of dropping into the no-input motorbike pose.</summary>
        private static void DriveStanding(Animator anim, int stance)
        {
            foreach (var p in anim.parameters)
            {
                switch (p.name)
                {
                    case "Grounded": Set(anim, p, 1f); break;
                    case "Upright": Set(anim, p, 1f); break;
                    case "IsLocal": Set(anim, p, 1f); break;
                    case "Emote": Set(anim, p, 0f); break; // not emoting → stay in locomotion
                    case "Crouching": Set(anim, p, stance == 1 ? 1f : 0f); break;
                    case "Prone": Set(anim, p, stance == 2 ? 1f : 0f); break;
                    case "MovementX": case "MovementY":
                    case "VelocityX": case "VelocityY": case "VelocityZ":
                    case "Sitting": case "Flying": case "Swimming":
                        Set(anim, p, 0f); break;
                }
            }
        }

        /// <summary>Drive both gesture parameter styles like the game: the discrete *Idx Ints and the
        /// analog Floats (fist held fully squeezed).</summary>
        private static void DriveGestures(Animator anim, int left, int right)
        {
            foreach (var p in anim.parameters)
            {
                switch (p.name)
                {
                    case "GestureLeftIdx": Set(anim, p, left); break;
                    case "GestureRightIdx": Set(anim, p, right); break;
                    case "GestureLeft": Set(anim, p, left); break;
                    case "GestureRight": Set(anim, p, right); break;
                }
            }
        }

        private static void Set(Animator anim, AnimatorControllerParameter p, float v)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Bool: anim.SetBool(p.name, v > 0.5f); break;
                case AnimatorControllerParameterType.Int: anim.SetInteger(p.name, Mathf.RoundToInt(v)); break;
                case AnimatorControllerParameterType.Float: anim.SetFloat(p.name, v); break;
            }
        }

        /// <summary>Map each synced parameter (machine name) to its friendly AAS display name for labels.</summary>
        private static Dictionary<string, string> BuildLabelLookup(GameObject avatar)
        {
            var map = new Dictionary<string, string>();
            var cvr = CckAvatar.FindOn(avatar);
            var list = cvr?.SettingsList;
            if (list != null)
                foreach (var e in list)
                {
                    var m = CckAvatar.EntryMachineName(e);
                    var n = Reflect.GetField(e, CckNames.Entry_Name) as string;
                    if (!string.IsNullOrEmpty(m) && !string.IsNullOrEmpty(n)) map[m] = n;
                }
            return map;
        }
    }
}
