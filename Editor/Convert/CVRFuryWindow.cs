using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// The single CVRFury window: a guided, top-to-bottom workflow that replaces the scattered menu
    /// items. Pick the avatar once, then run each numbered step in order. Every step reports into the
    /// shared log at the bottom. Nothing here is destructive to your controller unless you ask it to
    /// build one.
    /// </summary>
    public class CVRFuryWindow : EditorWindow
    {
        private GameObject _avatar;
        private Vector2 _scroll;
        private string _log = "";

        // Step 2 (clips + controller)
        private DefaultAsset _clipFolder;
        private string _onSuffix = "toggled";
        private string _offSuffix = "default";
        private string _renameOn = "1", _renameOff = "0"; // last-resort bulk renamer endings
        private bool _buildController = true;
        private AnimatorController _controller;

        // Step 2 — optional smart-match review
        private bool _reviewMatches;
        private List<ToggleClipLinker.Assignment> _reviewRows;
        private string _rowSearch = "";
        private int _rowFilter; // 0 All · 1 Found · 2 Guessed · 3 None · 4 Changed
        private Vector2 _rowScroll;

        // PhysBones
        private bool _pbColliders = true;
        private float _pbDamping = 0.1f, _pbElasticity = 1f, _pbStiffness = 1f, _pbRadiusScale = 1f, _pbGravityScale = 1f;
        private bool _pbRemoveOriginal = true;

        // Magica
        private int _magicaType; // 0 = BoneCloth, 1 = BoneSpring
        private bool _magicaRemoveOriginal = true;

        // Emotes / poses
        private DefaultAsset _emoteFolder;

        // Strip
        private bool _removeFinalIK = true;

        private bool _s0, _s1 = true, _s2 = true, _se, _s3, _s4, _s5, _sSps, _sResize, _sCredits;
        private string _resizeStatus = "";

        // Unified slider rows (size / length / hue / emission) — one per control you want.
        private enum SliderKind { Size, Length, Hue, Emission }
        private sealed class SliderRow
        {
            public SliderKind kind = SliderKind.Size;
            public Transform target;     // Size / Length
            public Renderer renderer;    // Hue / Emission
            public string property = ""; // Hue / Emission shader property
            public int axis = 2;         // Length (X/Y/Z)
            public float min = 0.5f, max = 2f;
        }
        private readonly System.Collections.Generic.List<SliderRow> _scaleRows = new System.Collections.Generic.List<SliderRow>();
        private double _bounceA = -100, _bounceB = -100; // last click time per name (bounce decays from it)

        // SPS / DPS (experimental)
        private Transform _spsPlug;
        private Transform _spsSocket;
        private Transform _spsTemplate;
        private string _spsStatus = "";   // inline result of the last SPS action
        private bool _spsCloneOpen;        // "I already have a working DPS avatar" sub-section
        private bool _spsAddToggle = true; // wrap each baked orifice in a default-off menu toggle

        [MenuItem("Tools/CVRFury/CVRFury", false, 0)]
        public static void Open()
        {
            var w = GetWindow<CVRFuryWindow>("CVRFury");
            w.minSize = new Vector2(460, 560);
            w.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"CVRFury  v{CckNames.CvrFuryVersion}", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "VRChat → ChilloutVR, one step at a time. Pick your avatar, then work down the steps. " +
                "Both the VRChat SDK and the CCK must be present and compiling in this project.",
                MessageType.Info);

            _avatar = (GameObject)EditorGUILayout.ObjectField(
                "Avatar", _avatar != null ? _avatar : Selection.activeGameObject, typeof(GameObject), true);

            EditorGUILayout.Space();
            Step0Basics();
            Step1Parameters();
            Step2Clips();
            StepEmotes();
            Step3PhysBones();
            Step4Magica();
            StepSps();
            StepResize();
            Step5Strip();

            if (!string.IsNullOrEmpty(_log))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_log, GUILayout.MinHeight(120));
            }

            StepCredits();

            EditorGUILayout.EndScrollView();
        }

        private void Step1Parameters()
        {
            _s1 = Foldout(_s1, "1 — Parameters (from VRChat menu)");
            if (!_s1) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox(
                    "Creates a CCK Advanced Avatar Setting for every VRChat menu control (Machine Name = the " +
                    "VRChat parameter). Non-destructive and leaves your controller alone. Re-running is safe.",
                    MessageType.None);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Link parameters from VRChat menu"))
                        RunAndRefresh(() => AasParameterLinker.LinkParameters(_avatar));
            }
        }

        private void Step2Clips()
        {
            _s2 = Foldout(_s2, "2 — Toggle clips + build controller");
            if (!_s2) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox(
                    "Matches on/off animation clips from a folder to your toggles by name, and (optionally) " +
                    "builds & attaches a controller so the parameters exist and toggles animate. Leave the " +
                    "Controller empty to copy CVR's stock AvatarAnimator (keeps locomotion). Do NOT drop your " +
                    "VRChat FX controller here — its hundreds of params blow the synced-bit budget.",
                    MessageType.None);
                _clipFolder = (DefaultAsset)EditorGUILayout.ObjectField("Animations Folder", _clipFolder, typeof(DefaultAsset), false);
                _onSuffix = EditorGUILayout.TextField(new GUIContent("ON  clip name ends with",
                    "One or more comma-separated words, e.g. \"toggled, on, enabled\". Used to recognise " +
                    "the ON clip when a creator named some clips differently from the rest."), _onSuffix);
                _offSuffix = EditorGUILayout.TextField(new GUIContent("OFF clip name ends with",
                    "One or more comma-separated words, e.g. \"default, off, disabled\"."), _offSuffix);
                EditorGUILayout.LabelField(" ", "Tip: list several comma-separated words if clips aren't all named the same.", EditorStyles.miniLabel);
                _buildController = EditorGUILayout.ToggleLeft(
                    "Build & attach a controller (creates parameters → clears the red ❗)", _buildController);
                using (new EditorGUI.DisabledScope(!_buildController))
                using (new EditorGUI.IndentLevelScope())
                    _controller = (AnimatorController)EditorGUILayout.ObjectField(
                        "Controller (optional)", _controller, typeof(AnimatorController), false);

                _reviewMatches = EditorGUILayout.ToggleLeft(new GUIContent(
                    "Review & fix clip matches before building (smart-guess unmatched)",
                    "Preview what clip the tool pairs to each toggle. Toggles it couldn't match exactly get a " +
                    "best-guess clip you can confirm, swap with the ⊙ picker, or drag-and-drop. Your picks are " +
                    "remembered per-avatar."), _reviewMatches);

                if (!_reviewMatches)
                {
                    using (new EditorGUI.DisabledScope(_avatar == null || _clipFolder == null))
                        if (GUILayout.Button("Link clips" + (_buildController ? " & build controller" : "")))
                        {
                            var folderPath = AssetDatabase.GetAssetPath(_clipFolder);
                            RunAndRefresh(() => ToggleClipLinker.LinkClips(
                                _avatar, folderPath, _onSuffix, _offSuffix, _buildController, _controller));
                        }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_avatar == null || _clipFolder == null))
                        if (GUILayout.Button("Preview / refresh matches"))
                            _reviewRows = ToggleClipLinker.Preview(
                                _avatar, AssetDatabase.GetAssetPath(_clipFolder), _onSuffix, _offSuffix);
                    if (_reviewRows != null) DrawReviewList();
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("Motorbike pose / no movement after editing the avatar (e.g. visemes) " +
                    "since you built the controller? Click this to re-point the avatar at a controller that has " +
                    "CVR locomotion. It also runs automatically at upload.", MessageType.None);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Fix motorbike pose (re-assert locomotion)"))
                        RunAndRefresh(() =>
                        {
                            var log = new BuildLog();
                            ControllerGuard.ReassertLocomotion(_avatar, log);
                            var msgs = log.Entries.Select(e => e.Message).ToList();
                            return msgs.Count > 0 ? string.Join("\n", msgs)
                                : "Locomotion controller is OK (has CVR movement) — nothing to fix.";
                        });

                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("LAST RESORT: if the clip names are all over the place and matching keeps " +
                    "failing, this scans every clip in the folder above, guesses on/off from words at the end of " +
                    "each name (show/hide, enable/disable…), and renames them to end with your markers below. It " +
                    "edits the actual asset files (can break references), can mis-guess, and may take a while — " +
                    "back up first.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _renameOn = EditorGUILayout.TextField("ON ending", _renameOn);
                    _renameOff = EditorGUILayout.TextField("OFF ending", _renameOff);
                }
                using (new EditorGUI.DisabledScope(_clipFolder == null))
                    if (GUILayout.Button("Bulk-rename clip endings (last resort)"))
                    {
                        if (EditorUtility.DisplayDialog("CVRFury — bulk rename",
                            "This renames the animation asset files in the folder. It can break references and " +
                            "can guess wrong. Back up first.\n\nProceed?", "Rename", "Cancel"))
                            RunAndRefresh(() => AnimationRenamer.RenameEndings(
                                AssetDatabase.GetAssetPath(_clipFolder), _renameOn, _renameOff));
                    }
            }
        }

        private void DrawReviewList()
        {
            int matched = 0, guessed = 0, none = 0, changed = 0, nativeCount = 0;
            foreach (var r in _reviewRows)
            {
                if (r.native) nativeCount++; else if (r.state == 0) matched++; else if (r.state == 1) guessed++; else none++;
                if (r.changed) changed++;
            }
            EditorGUILayout.LabelField($"{matched} matched · {guessed} guessed · {none} no clip · {nativeCount} object · {changed} changed", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("✔ exact match   ? best guess (check it!)   ✘ none found   ● object toggle (CVR " +
                                    "toggles it directly — no clip needed). Use the ⊙ picker or drag a clip into a box " +
                                    "to change it. ON = shown/enabled, OFF = hidden.", MessageType.None);

            // Quick filter buttons. "Found" includes native object toggles (state 0).
            _rowFilter = GUILayout.Toolbar(_rowFilter, new[]
            {
                $"All ({_reviewRows.Count})", $"Found ({matched + nativeCount})", $"Guessed ({guessed})",
                $"None ({none})", $"Changed ({changed})"
            });
            _rowSearch = EditorGUILayout.TextField("Search", _rowSearch);
            string q = (_rowSearch ?? "").Trim().ToLowerInvariant();

            int shown = 0;
            _rowScroll = EditorGUILayout.BeginScrollView(_rowScroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(340));
            for (int i = 0; i < _reviewRows.Count; i++)
            {
                var row = _reviewRows[i];
                // Category filter.
                if (_rowFilter == 1 && row.state != 0) continue;
                if (_rowFilter == 2 && row.state != 1) continue;
                if (_rowFilter == 3 && row.state != 2) continue;
                if (_rowFilter == 4 && !row.changed) continue;

                var label = string.IsNullOrEmpty(row.display) ? row.machine : row.display;
                if (q.Length > 0 && !(label ?? "").ToLowerInvariant().Contains(q) &&
                    !(row.machine ?? "").ToLowerInvariant().Contains(q)) continue;
                shown++;

                string mark = row.native ? "●" : row.state == 0 ? "✔" : row.state == 1 ? "?" : "✘";
                string tag = row.native ? "   (object toggle — no clip needed)" : row.isSlider ? "   (slider)" : "";
                EditorGUILayout.LabelField($"{mark} {label}{tag}", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    var newOn = (AnimationClip)EditorGUILayout.ObjectField(row.isSlider ? "Max (1)" : "ON", row.on, typeof(AnimationClip), false);
                    var newOff = (AnimationClip)EditorGUILayout.ObjectField(row.isSlider ? "Min (0)" : "OFF", row.off, typeof(AnimationClip), false);
                    if (newOn != row.on || newOff != row.off)
                    {
                        row.on = newOn; row.off = newOff;
                        row.state = (newOn != null || newOff != null) ? 0 : 2; // a manual pick counts as resolved
                        row.changed = true;
                        _reviewRows[i] = row;
                    }
                }
            }
            if (shown == 0)
                EditorGUILayout.LabelField("Nothing matches this filter/search.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(_avatar == null))
                if (GUILayout.Button("Apply matches" + (_buildController ? " & build controller" : "")))
                    RunAndRefresh(() => ToggleClipLinker.ApplyAndBuild(_avatar, _reviewRows, _buildController, _controller));
        }

        private void Step3PhysBones()
        {
            _s3 = Foldout(_s3, "3 — PhysBones → DynamicBones");
            if (!_s3) return;
            using (new EditorGUI.IndentLevelScope())
            {
                var dbPresent = Reflect.FindType(VrcNames.DynamicBoneType) != null;
                EditorGUILayout.HelpBox(
                    dbPresent
                        ? "Converts VRCPhysBone/Collider to DynamicBone/Collider. The physics models differ, so " +
                          "tune the sliders below; defaults are a reasonable starting point. Colliders convert first."
                        : "DynamicBone isn't in this project. Import it (the CCK bundles it) to enable this.",
                    dbPresent ? MessageType.None : MessageType.Warning);
                _pbColliders = EditorGUILayout.ToggleLeft("Also convert PhysBone colliders", _pbColliders);
                _pbDamping = EditorGUILayout.Slider("Damping", _pbDamping, 0f, 1f);
                _pbElasticity = EditorGUILayout.Slider("Elasticity ×", _pbElasticity, 0f, 3f);
                _pbStiffness = EditorGUILayout.Slider("Stiffness ×", _pbStiffness, 0f, 3f);
                _pbRadiusScale = EditorGUILayout.Slider("Radius ×", _pbRadiusScale, 0.1f, 3f);
                _pbGravityScale = EditorGUILayout.Slider("Gravity ×", _pbGravityScale, 0f, 3f);
                _pbRemoveOriginal = EditorGUILayout.ToggleLeft("Remove the VRChat PhysBones after converting", _pbRemoveOriginal);
                using (new EditorGUI.DisabledScope(_avatar == null || !dbPresent))
                    if (GUILayout.Button("Convert PhysBones"))
                        RunAndRefresh(ConvertPhysBones);
            }
        }

        private const float BounceDuration = 1.6f; // seconds a name bounces after being clicked

        private void StepCredits()
        {
            EditorGUILayout.Space(6);
            _sCredits = Foldout(_sCredits, "♥ Credits");
            if (!_sCredits) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Made by  (click a name to make it bounce)", EditorStyles.centeredGreyMiniLabel);

                var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                var row = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
                double now = EditorApplication.timeSinceStartup;
                float half = row.width * 0.5f;
                bool animating = false;

                if (DrawBouncyName(new Rect(row.x, row.y, half, row.height), "DoNotPetMe", now, _bounceA, style, ref animating))
                    _bounceA = now;
                if (DrawBouncyName(new Rect(row.x + half, row.y, half, row.height), "--Stardust--", now, _bounceB, style, ref animating))
                    _bounceB = now;

                EditorGUILayout.LabelField("--Stardust-- in CVR", EditorStyles.centeredGreyMiniLabel);

                if (animating) Repaint(); // only redraw while a name is mid-bounce; idle otherwise
            }
        }

        /// <summary>Draws a name that sits still until clicked, then bounces with a decaying amplitude and
        /// settles on its own. Returns true the frame it's clicked; sets <paramref name="animating"/> while
        /// it's still moving.</summary>
        private bool DrawBouncyName(Rect area, string label, double now, double clickTime, GUIStyle style, ref bool animating)
        {
            float elapsed = (float)(now - clickTime);
            float amp = 0f;
            if (elapsed >= 0f && elapsed < BounceDuration)
            {
                float decay = 1f - elapsed / BounceDuration;            // ease out to rest
                amp = Mathf.Abs(Mathf.Sin(elapsed * 14f)) * 11f * decay; // a few quick hops, shrinking
                animating = true;
            }
            var r = new Rect(area.x, area.y + area.height - 22f - amp, area.width, 22f);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            return GUI.Button(r, label, style);
        }

        private static readonly string[] AxisNames = { "X", "Y", "Z" };
        private static readonly string[] KindNames = { "Size", "Length", "Hue", "Emission" };

        private void StepResize()
        {
            _sResize = Foldout(_sResize, "Sliders (size, length, hue, emission)");
            if (!_sResize) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Add in-game menu sliders for anything: resize a part (Size/Length), " +
                    "or shift a material's Hue / Emission. Each row makes one slider. Works natively in CVR.",
                    EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ Add slider")) _scaleRows.Add(new SliderRow());
                    using (new EditorGUI.DisabledScope(_avatar == null))
                        if (GUILayout.Button("+ Whole-avatar size", GUILayout.Width(150)))
                            _scaleRows.Add(new SliderRow { kind = SliderKind.Size, target = _avatar.transform });
                }

                SliderRow remove = null;
                foreach (var row in _scaleRows)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            row.kind = (SliderKind)EditorGUILayout.Popup((int)row.kind, KindNames);
                            if (GUILayout.Button("✕", GUILayout.Width(24))) remove = row;
                        }

                        if (row.kind == SliderKind.Size || row.kind == SliderKind.Length)
                            row.target = (Transform)EditorGUILayout.ObjectField("Part", row.target, typeof(Transform), true);
                        else
                        {
                            row.renderer = (Renderer)EditorGUILayout.ObjectField("Mesh", row.renderer, typeof(Renderer), true);
                            if (string.IsNullOrEmpty(row.property))
                                row.property = row.kind == SliderKind.Hue ? "_MainHueShift" : "_EmissionStrength";
                            row.property = EditorGUILayout.TextField(new GUIContent("Shader property",
                                "The material float property to drive. Defaults are Poiyomi's; check your shader " +
                                "if the slider does nothing."), row.property);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (row.kind == SliderKind.Length)
                                row.axis = EditorGUILayout.Popup(row.axis, AxisNames, GUILayout.Width(40));
                            row.min = EditorGUILayout.FloatField("min", row.min, GUILayout.Width(110));
                            row.max = EditorGUILayout.FloatField("max", row.max, GUILayout.Width(110));
                        }
                    }
                }
                if (remove != null) _scaleRows.Remove(remove);

                using (new EditorGUI.DisabledScope(_avatar == null || _scaleRows.Count == 0))
                    if (GUILayout.Button("Create sliders"))
                    {
                        try { _resizeStatus = CreateSliders(); }
                        catch (System.Exception ex) { _resizeStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                if (!string.IsNullOrEmpty(_resizeStatus))
                    EditorGUILayout.HelpBox(_resizeStatus,
                        _resizeStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private string CreateSliders()
        {
            int made = 0;
            var errors = new System.Collections.Generic.List<string>();
            foreach (var row in _scaleRows)
            {
                string err = null;
                switch (row.kind)
                {
                    case SliderKind.Size:
                    {
                        var part = row.target == (_avatar ? _avatar.transform : null) ? "Avatar" : row.target?.name;
                        err = AvatarSizeSlider.AddSlider(_avatar, row.target, Vector3.one, row.min, row.max, part + " Size");
                        break;
                    }
                    case SliderKind.Length:
                    {
                        var axisVec = new Vector3(row.axis == 0 ? 1 : 0, row.axis == 1 ? 1 : 0, row.axis == 2 ? 1 : 0);
                        err = AvatarSizeSlider.AddSlider(_avatar, row.target, axisVec, row.min, row.max,
                            (row.target ? row.target.name : "?") + " Length");
                        break;
                    }
                    case SliderKind.Hue:
                        err = AvatarSizeSlider.AddMaterialSlider(_avatar, row.renderer, row.property, row.min, row.max,
                            (row.renderer ? row.renderer.name : "?") + " Hue");
                        break;
                    case SliderKind.Emission:
                        err = AvatarSizeSlider.AddMaterialSlider(_avatar, row.renderer, row.property, row.min, row.max,
                            (row.renderer ? row.renderer.name : "?") + " Emission");
                        break;
                }
                if (err != null) errors.Add(err); else made++;
            }
            var msg = $"Created {made} slider(s). They appear in the Advanced Settings menu and apply at runtime in CVR.";
            if (errors.Count > 0) msg += "\nSkipped: " + string.Join("; ", errors);
            return msg;
        }

        private void StepSps()
        {
            _sSps = Foldout(_sSps, "SPS / DPS (NSFW penetration) — experimental");
            if (!_sSps) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField(
                    "SPS can't deform in CVR; DPS can (it's light-based). Convert SPS → DPS in 3 steps.",
                    EditorStyles.wordWrappedMiniLabel);

                // Step 1 — Detect
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Step 1 — Find the parts", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Detect plugs & sockets"))
                        RunSps(() => SpsConverter.DetectReport(_avatar));

                // Step 2 — Bake orifice lights
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Step 2 — Add the DPS orifice lights", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Creates the marker lights the plug bends toward. Handles multiple " +
                    "spots — male + female, several orifices — in one pass.", EditorStyles.wordWrappedMiniLabel);
                _spsAddToggle = EditorGUILayout.ToggleLeft(new GUIContent(
                    "Add a menu toggle, OFF by default (recommended)",
                    "Each orifice starts disabled and gets its own in-game toggle, so nothing deforms until the " +
                    "wearer turns it on."), _spsAddToggle);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Add to every socket found"))
                        RunSps(() => SpsConverter.AutoBake(_avatar, _spsAddToggle));
                EditorGUILayout.LabelField("Or place them by hand: drag a spot (bone/empty) and click — repeat " +
                    "for as many orifices as you want.", EditorStyles.wordWrappedMiniLabel);
                _spsSocket = (Transform)EditorGUILayout.ObjectField(new GUIContent("Or one spot",
                    "Pick the transform where you want a single orifice; the DPS lights are placed here."),
                    _spsSocket, typeof(Transform), true);
                using (new EditorGUI.DisabledScope(_spsSocket == null))
                    if (GUILayout.Button("Add to this spot"))
                        RunSps(() =>
                        {
                            SpsConverter.GenerateDpsOrifice(_spsSocket, addToggle: _spsAddToggle);
                            return $"Done — added DPS orifice lights at '{_spsSocket.name}'" +
                                   (_spsAddToggle ? " (OFF by default, with a menu toggle)" : "") + ".\n" +
                                   "Next: rotate it so the opening faces outward, then Step 3 — enable the plug's deformation.";
                        });

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Made a mess? Undo just the orifices CVRFury baked (not your own DPS):",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Remove CVRFury-baked orifices"))
                        RunSps(() => SpsConverter.RemoveBaked(_avatar));

                // Step 3 — Turn on deformation on the plug
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Step 3 — Turn on the plug's deformation", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Drop the penetrator's mesh object here (the one with its Mesh " +
                    "Renderer + material) — or auto-fill it from Step 1.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.HelpBox("Use a shader with DPS / light-based deform (Poiyomi works well). This " +
                    "enables that — not SPS, which needs VRChat contacts and stays inert in CVR.", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _spsPlug = (Transform)EditorGUILayout.ObjectField(new GUIContent("Plug mesh",
                        "The object holding the penetrator's mesh/material."), _spsPlug, typeof(Transform), true);
                    using (new EditorGUI.DisabledScope(_avatar == null))
                        if (GUILayout.Button("Auto-fill", GUILayout.Width(70)))
                            RunSps(() =>
                            {
                                var plug = SpsConverter.FindPlug(_avatar);
                                _spsPlug = plug;
                                return plug != null
                                    ? $"Filled plug mesh: '{plug.name}'. Now click \"Enable deformation\"."
                                    : "Couldn't find a plug automatically — drop the penetrator mesh object in by hand.";
                            });
                }
                using (new EditorGUI.DisabledScope(_spsPlug == null))
                    if (GUILayout.Button("Enable deformation on this plug"))
                        RunSps(() => SpsConverter.SetupPlugShader(_spsPlug));
                EditorGUILayout.LabelField("If the shader has no such toggle, the status tells you what to do.",
                    EditorStyles.wordWrappedMiniLabel);

                // Inline status of the last action + what to do next.
                if (!string.IsNullOrEmpty(_spsStatus))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(_spsStatus,
                        _spsStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
                }

                // Optional path for people who already have a working DPS avatar.
                EditorGUILayout.Space(6);
                _spsCloneOpen = EditorGUILayout.ToggleLeft(
                    "I already have an avatar where DPS works — copy its orifice instead", _spsCloneOpen);
                if (_spsCloneOpen)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        _spsTemplate = (Transform)EditorGUILayout.ObjectField("Working orifice", _spsTemplate, typeof(Transform), true);
                        EditorGUILayout.LabelField("Copied onto the Step 2 spot. Most reliable — exact known-good lights.",
                            EditorStyles.wordWrappedMiniLabel);
                        using (new EditorGUI.DisabledScope(_spsTemplate == null || _spsSocket == null))
                            if (GUILayout.Button("Copy orifice → chosen spot"))
                                RunSps(() => SpsConverter.CloneOrifice(_spsTemplate, _spsSocket));
                    }
            }
        }

        private void Step4Magica()
        {
            _s4 = Foldout(_s4, "4 — Magica Cloth");
            if (!_s4) return;
            using (new EditorGUI.IndentLevelScope())
            {
                if (MagicaType() == null)
                {
                    EditorGUILayout.HelpBox("Magica Cloth 2 is not installed — this step is skipped. Install it to enable.",
                                            MessageType.None);
                    return;
                }
                EditorGUILayout.HelpBox(
                    "Magica Cloth 2 detected. Adds a MagicaCloth component to each PhysBone root, sets the cloth type " +
                    "and assigns the root bone. After running, open each MagicaCloth and press its Build button (or " +
                    "enter Play) and tune — Magica's solver is configured at build time.",
                    MessageType.Info);
                _magicaType = EditorGUILayout.Popup("Cloth type", _magicaType, new[] { "BoneCloth", "BoneSpring" });
                _magicaRemoveOriginal = EditorGUILayout.ToggleLeft("Remove the VRChat PhysBones after converting", _magicaRemoveOriginal);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Convert PhysBones → Magica Cloth"))
                        RunAndRefresh(ConvertMagica);
            }
        }

        private string ConvertPhysBones()
        {
            var assets = AssetSaver.CreatePersistent(_avatar.name);
            var opts = new ConversionOptions
            {
                physBones = true, physBoneColliders = _pbColliders,
                pbDamping = _pbDamping, pbElasticityScale = _pbElasticity, pbStiffnessScale = _pbStiffness,
                pbRadiusScale = _pbRadiusScale, pbGravityScale = _pbGravityScale,
                removeOriginalPhysBones = _pbRemoveOriginal,
            };
            var ctx = new ConversionContext(_avatar, opts, assets);
            new PhysBoneConverter().Run(ctx);
            EditorUtility.SetDirty(_avatar);
            return string.Join("\n", ctx.Log.Entries.Select(e => e.Message));
        }

        private static System.Type MagicaType() =>
            Reflect.FindType("MagicaCloth2.MagicaCloth") ?? Reflect.FindType("MagicaCloth.MagicaCloth");

        private string ConvertMagica()
        {
            var magicaType = MagicaType();
            if (magicaType == null) return "Magica Cloth 2 is not installed.";
            var pbType = Reflect.FindType(VrcNames.PhysBoneType);
            if (pbType == null) return "VRChat SDK / PhysBones not found in this project.";

            var clothTypeName = _magicaType == 1 ? "BoneSpring" : "BoneCloth";
            int made = 0;
            foreach (var pb in _avatar.GetComponentsInChildren(pbType, true))
            {
                var go = ((Component)pb).gameObject;
                var rootT = Reflect.GetField(pb, VrcNames.PB_Root) as Transform ?? go.transform;
                var comp = go.GetComponent(magicaType) ?? go.AddComponent(magicaType);
                var sd = Reflect.GetProperty(comp, "SerializeData") ?? Reflect.GetField(comp, "serializeData");
                if (sd != null)
                {
                    Reflect.SetEnumFieldByName(sd, "clothType", clothTypeName);
                    var roots = Reflect.AsList(Reflect.GetField(sd, "rootBones"));
                    if (roots != null && !roots.Contains(rootT)) roots.Add(rootT);
                }
                made++;
            }

            int removed = 0;
            if (_magicaRemoveOriginal)
                foreach (var typeName in new[] { VrcNames.PhysBoneType, VrcNames.PhysBoneColliderType })
                {
                    var t = Reflect.FindType(typeName);
                    if (t == null) continue;
                    foreach (var c in _avatar.GetComponentsInChildren(t, true)) { DestroyImmediate(c); removed++; }
                }

            EditorUtility.SetDirty(_avatar);
            return $"Added/updated {made} MagicaCloth component(s) (type {clothTypeName}); assigned each PhysBone root." +
                   (removed > 0 ? $" Removed {removed} VRChat PhysBone/Collider(s)." : "") +
                   "\nNext: open each MagicaCloth and press Build (or enter Play), then tune the cloth.";
        }

        private void Step0Basics()
        {
            _s0 = Foldout(_s0, "0 — Avatar basics (viewpoint / visemes / blink / eyes)");
            if (!_s0) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox("Copies the VRChat viewpoint, visemes, blink and eye-look settings onto the CVRAvatar.",
                                        MessageType.None);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Apply avatar basics"))
                        RunAndRefresh(() => RunConverter(new AvatarBasicsConverter(), new ConversionOptions { avatarBasics = true }, true));
            }
        }

        private void Step5Strip()
        {
            _s5 = Foldout(_s5, "5 — Strip VRChat + broken components (do last)");
            if (!_s5) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox("Removes leftover VRChat components and missing scripts once you're happy. " +
                                        "Do this last — afterwards you can remove the VRChat SDK.", MessageType.Warning);
                _removeFinalIK = EditorGUILayout.ToggleLeft("Also remove FinalIK / VRIK (CVR has its own IK)", _removeFinalIK);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Strip VRChat + broken components"))
                        RunAndRefresh(() => RunConverter(new FinalCleanupConverter(),
                            new ConversionOptions { stripVrcAndBroken = true, removeFinalIK = _removeFinalIK }, true));
            }
        }

        private string RunConverter(IConverter step, ConversionOptions opts, bool needsDescriptor)
        {
            var assets = AssetSaver.CreatePersistent(_avatar.name);
            var ctx = new ConversionContext(_avatar, opts, assets);
            if (needsDescriptor)
            {
                var descType = Reflect.FindType(VrcNames.AvatarDescriptorType);
                if (descType != null) ctx.VrcDescriptor = _avatar.GetComponent(descType);
            }
            ctx.Cvr = CckAvatar.EnsureOn(_avatar);
            step.Run(ctx);
            EditorUtility.SetDirty(_avatar);
            return string.Join("\n", ctx.Log.Entries.Select(e => e.Message));
        }

        private void StepEmotes()
        {
            _se = Foldout(_se, "Emotes / Poses — toggle full-body animations (sit, dance, …)");
            if (!_se) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox(
                    "Adds a menu toggle for each animation clip in a folder. While the toggle is ON the clip plays " +
                    "(sit / dance / pose); when OFF the avatar returns to normal CVR movement — the layer only poses " +
                    "the body while toggled on, so it doesn't break locomotion. Point this at a folder of full-body " +
                    "clips (e.g. GoGoLoco's emotes, or any sit/dance animations).\n\n" +
                    "Run step 2 first so a controller exists; emotes are added onto it.",
                    MessageType.Info);
                _emoteFolder = (DefaultAsset)EditorGUILayout.ObjectField("Emote/Pose clips folder", _emoteFolder, typeof(DefaultAsset), false);
                using (new EditorGUI.DisabledScope(_avatar == null || _emoteFolder == null))
                    if (GUILayout.Button("Add emote toggles"))
                        RunAndRefresh(AddEmotes);

                EditorGUILayout.LabelField("Posing wrong / motorbike? Remove all emotes to get a clean avatar, " +
                    "then tell me if it still poses badly.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Remove emote toggles"))
                        RunAndRefresh(RemoveEmotes);
            }
        }

        private string RemoveEmotes()
        {
            var cvr = CckAvatar.FindOn(_avatar);
            if (cvr == null) return "No CVRAvatar found.";
            var controller = Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Animator) as AnimatorController;

            int layers = AnimatorUtil.RemoveLayers(controller, "CVRFury Emote:");
            int parms = AnimatorUtil.RemoveParameters(controller, "Emote/");

            int entries = 0;
            var list = cvr.SettingsList;
            if (list != null)
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var m = CckAvatar.EntryMachineName(list[i]);
                    if (!string.IsNullOrEmpty(m) && m.StartsWith("Emote/")) { list.RemoveAt(i); entries++; }
                }

            if (controller != null) { EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); }
            cvr.Persist();
            if (layers + parms + entries == 0)
                return "No CVRFury emotes found to remove.";
            return $"Removed all CVRFury emotes ({entries} menu entr(ies), {layers} animator layer(s), {parms} " +
                   "parameter(s)). If the avatar STILL motorbikes now, the cause is the base controller, not the " +
                   "emotes — use \"Fix motorbike pose\", or re-run Step 2 to rebuild locomotion.";
        }

        private string AddEmotes()
        {
            var folderPath = AssetDatabase.GetAssetPath(_emoteFolder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return "Pick a folder of emote/pose animation clips.";

            var cvr = CckAvatar.FindOn(_avatar);
            if (cvr == null) return "No CVRAvatar — run steps 1–2 first.";
            var controller = Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Animator) as AnimatorController;
            if (controller == null)
                return "No AAS controller attached yet — run step 2 (Build & attach a controller) first, then add emotes.";

            // CVR regenerates the AAS animator at upload by COPYING the base controller and rebuilding a
            // layer for every entry whose parameter the base does NOT already declare. So an emote layer that
            // lives only in the generated animator gets wiped/rebuilt (CVR's version motorbikes) on some
            // uploads but not others — the "sometimes it looks like Unity, sometimes not" bug. The cure is to
            // put the emote parameter+layer in the BASE controller too: then regeneration copies our correct
            // layer and SKIPS rebuilding it. We write to both so it's right whether or not CVR regenerates.
            var baseController = Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_BaseController) as AnimatorController;
            bool baseUsable = IsWritablePerAvatarController(baseController) && baseController != controller;
            var targets = new List<AnimatorController> { controller };
            if (baseUsable) targets.Add(baseController);

            var existingMachines = new HashSet<string>();
            foreach (var e in cvr.SettingsList)
            {
                var m = CckAvatar.EntryMachineName(e);
                if (!string.IsNullOrEmpty(m)) existingMachines.Add(m);
            }

            int added = 0;
            var skipped = new List<string>();
            // Match the controller's existing WriteDefaults convention. A mismatch is what makes an emote
            // look right while moving but wrong (default/bind pose bleeding through) when idle.
            bool wd = AnimatorUtil.DetectWriteDefaults(controller);
            // Repair any emote layers already built with the wrong WriteDefaults from an earlier run.
            int repaired = 0;
            foreach (var t in targets) repaired += AnimatorUtil.SetWriteDefaultsForLayers(t, "CVRFury Emote:", wd);
            // GoGoLoco (and similar) ship locomotion/system clips in the same folder as their emotes —
            // idle, AFK, stand, walk/run, jump, crouch, prone, fly, swim, sit/stand transitions. Added as
            // always-present override layers these pose the body at rest (the "motorbike" look), so skip
            // anything that looks like a locomotion/system clip and only build layers for real poses/dances.
            string[] systemWords = { "idle", "afk", "locomotion", "movement", "stand", "walk", "run",
                                     "jog", "sprint", "jump", "fall", "crouch", "prone", "fly", "flight",
                                     "swim", "tracking", "calibrat", "tpose", "t-pose", "reset", "base" };
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                var name = clip.name;
                var lower = name.ToLowerInvariant();
                if (System.Array.Exists(systemWords, w => lower.Contains(w))) { skipped.Add(name); continue; }
                var machine = "Emote/" + name;
                if (!existingMachines.Contains(machine)) { cvr.AddToggle(name, machine, false, false); existingMachines.Add(machine); }
                var emoteEntry = cvr.SettingsList.Cast<object>().FirstOrDefault(e => CckAvatar.EntryMachineName(e) == machine);
                // If the base carries the layer, CVR skips SetupAnimator for this entry, so its clips are
                // ignored — leave them off to avoid CVR ever building a competing full-body layer. If we
                // couldn't use the base, fall back to the entry carrying the clip (old behaviour).
                if (emoteEntry != null && !baseUsable) cvr.SetToggleClips(emoteEntry, clip, null);

                bool builtAny = false;
                foreach (var t in targets)
                    if (!t.parameters.Any(p => p.name == machine))
                    {
                        // Off state empty (lets CVR locomotion show), On state = the emote clip. Override
                        // layer, Bool-driven — poses the body only while toggled on.
                        AnimatorUtil.AddBoolToggleLayer(t, "CVRFury Emote: " + name, machine, null, clip,
                                                        defaultOn: false, mask: null, writeDefaults: wd);
                        builtAny = true;
                    }
                if (builtAny) added++;
            }

            foreach (var t in targets) EditorUtility.SetDirty(t);
            AssetDatabase.SaveAssets();
            cvr.Persist();
            var msg = $"Added {added} emote toggle(s) (WriteDefaults {(wd ? "on" : "off")}, matching this " +
                   "controller). Each plays its clip while toggled ON and returns to normal movement when OFF. " +
                   "Toggle one at a time (two emotes on at once will fight).";
            msg += baseUsable
                ? "\nWritten into the base controller too, so it survives CVR's upload regeneration — the upload " +
                  "should now match what you see in Unity, every time."
                : "\nNote: couldn't find a per-avatar base controller to harden against CVR's upload " +
                  "regeneration — re-run Step 2 (Build & attach a controller) so emotes upload consistently.";
            if (repaired > 0)
                msg += $"\nFixed WriteDefaults on {repaired} existing emote state(s) — that's the cause of an " +
                       "emote looking wrong when idle but snapping right when you move.";
            if (skipped.Count > 0)
                msg += $"\nSkipped {skipped.Count} locomotion/system clip(s) that would pose the body at rest " +
                       $"(the motorbike pose): {string.Join(", ", skipped.Take(20))}{(skipped.Count > 20 ? " …" : "")}." +
                       "\nIf one of these is actually a pose you want, rename it without the locomotion word and re-run.";
            return msg;
        }

        // --- helpers ---

        /// <summary>True if the controller is a project asset we may safely add layers to — i.e. a per-avatar
        /// controller, not ChilloutVR's shared stock AvatarAnimator (which lives under a CCK/Packages folder
        /// and must never be edited, since every avatar shares it).</summary>
        private static bool IsWritablePerAvatarController(AnimatorController c)
        {
            if (c == null) return false;
            var path = AssetDatabase.GetAssetPath(c);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) return false;
            var lower = path.ToLowerInvariant();
            return !lower.Contains("/cck/") && !lower.Contains("cvr.cck") && !lower.Contains("abi.cck");
        }

        private string RunAndRefresh(System.Func<string> action)
        {
            string result;
            try { result = action(); }
            catch (System.Exception ex) { result = "Error: " + ex.Message; Debug.LogException(ex); }
            _log = result ?? "";
            // Mirror this run's output into the persistent "Show Last Build Log" view so the menu
            // item is never empty after a workflow step.
            BuildLogWindow.PublishText(_log, _log.StartsWith("Error:"));
            // Rebuild the CCK inspector against the now-populated list (deselect/reselect).
            var target = _avatar;
            Selection.activeObject = null;
            EditorApplication.delayCall += () => { if (target != null) Selection.activeObject = target; Repaint(); };
            return _log;
        }

        /// <summary>Runs an SPS/DPS action and shows its result ONLY in the section's inline status box —
        /// it deliberately does not touch the shared Log box at the bottom of the window.</summary>
        private void RunSps(System.Func<string> action)
        {
            try { _spsStatus = action() ?? ""; }
            catch (System.Exception ex) { _spsStatus = "Error: " + ex.Message; Debug.LogException(ex); }
            Repaint();
        }

        private static bool Foldout(bool state, string label)
        {
            EditorGUILayout.Space(2);
            return EditorGUILayout.Foldout(state, label, true, EditorStyles.foldoutHeader);
        }
    }
}
