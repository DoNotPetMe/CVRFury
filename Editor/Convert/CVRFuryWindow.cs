using System.Collections.Generic;
using System.Linq;
using CVRFury.Components;
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
        private readonly List<DefaultAsset> _extraClipFolders = new List<DefaultAsset>(); // also-scan folders (siblings)
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

        private bool _sPre = true, _s0, _s1 = true, _s2 = true, _se, _sEmoteWheel, _sDances, _sLoco, _sPresets, _sReveal, _s3, _s4, _s5, _sSps, _sResize, _sCredits;
        private bool _catConvert = true, _catAnim, _catFeatures; // top-level category groups

        // Reveal invisible clothing (material-animated visibility)
        private Renderer _revealTarget;
        private string _revealStatus = "";

        // Rework: find-anything search + Menu Wizard
        private string _search = "";
        private bool _sWizard = true;
        private List<MenuWizard.Row> _wizardRows;
        private Vector2 _wizardScroll;
        private string _wizardStatus = "";

        // Prefab Converter (VRCFury → CVRFury)
        private bool _sPrefabConv;
        private bool _prefabRemoveAfter = true;
        private string _prefabStatus = "";

        // Clothing setup (manual drag-in)
        private bool _sClothing;
        private SkinnedMeshRenderer _clothingBody;
        private string _clothingFolder = "Clothing";
        private readonly List<AvatarFeaturePack.ClothingItem> _clothingItems =
            new List<AvatarFeaturePack.ClothingItem> { new AvatarFeaturePack.ClothingItem() };
        private string _clothingStatus = "";

        // Touch reactions
        private bool _sTouch;
        private SkinnedMeshRenderer _touchFace;
        private sealed class TouchShapeRow { public int shape; public float value = 100f; }
        private readonly List<TouchShapeRow> _touchShapes = new List<TouchShapeRow> { new TouchShapeRow() };
        private int _touchZone;
        private string _touchCustomName = "Nose";
        private CVRFuryTouchZone _touchCustomZone;
        private bool _touchOthers = true;
        private int _touchStyle;              // 0 Instant · 1 Build-up
        private float _touchBuildSeconds = 6f;
        private AudioClip _touchSound;
        private bool _touchParticles;
        private string _touchStatus = "";

        // Breathing
        private bool _sBreathing;
        private SkinnedMeshRenderer _breathMesh;
        private int _breathShapeIndex;
        private float _breathCycle = 4f;
        private float _breathIntensity = 40f;
        private string _breathStatus = "";

        private string _preflight = "";
        private bool _preflightOk;
        private System.Collections.Generic.List<PreflightCheck.Result> _preflightResults;

        // Convert & Verify options (advanced control over the one-click pipeline).
        private bool _showConvertOpts;
        private readonly ConversionOptions _convertOpts = new ConversionOptions
        {
            avatarBasics = true, expressions = true, physBones = true, physBoneColliders = true,
            removeOriginalPhysBones = true, stripVrcAndBroken = true, mergePlayableLayers = false,
        };

        // Emote wheel (CVR emote menu) slot overrides
        private sealed class EmoteSlotRow { public int index; public string current; public AnimationClip newClip; public AudioClip audio; }
        private readonly List<EmoteSlotRow> _emoteSlots = new List<EmoteSlotRow>();
        private string _emoteWheelStatus = "";

        // Sync Dances → CVR dance menu
        private DefaultAsset _dancesFolder;
        private List<AnimationClip> _dances = new List<AnimationClip>();
        private string _dancesFolderUsed = "";
        private string _dancesStatus = "";
        private string _resizeStatus = "";

        // Crouch/Prone locomotion styles
        private sealed class LocoStyleRow { public string name = ""; public Motion motion; public int trigger; }
        private readonly List<LocoStyleRow> _locoRows = new List<LocoStyleRow>();
        private string _locoStatus = "";

        // Outfit presets (exclusive dropdown)
        private sealed class PresetRow { public string name = "Preset"; public readonly List<GameObject> objectsOn = new List<GameObject> { null }; }
        private readonly List<PresetRow> _presetRows = new List<PresetRow>();
        private string _presetStatus = "";

        // Unified slider rows (size / length / hue / emission) — one per control you want.
        private enum SliderKind { Size, Length, Hue, Emission, Blendshape }
        private sealed class SliderRow
        {
            public SliderKind kind = SliderKind.Size;
            public string label = "";    // optional menu name; blank = auto from first target
            public readonly List<Transform> targets = new List<Transform> { null };  // Size / Length (≥1, equal scaling)
            public readonly List<Renderer> renderers = new List<Renderer> { null };  // Hue / Emission / Blendshape (mesh)
            public string property = ""; // Hue / Emission shader property
            public int axis = 2;         // Length (X/Y/Z)
            public int blendIndex;       // Blendshape dropdown selection
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
            // Subtle dark backdrop instead of the flat editor gray.
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.145f, 0.135f, 0.16f));

            DrawBanner();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Keep content to a readable column instead of stretching across a wide monitor.
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(ContentWidth));

            var picked = (GameObject)EditorGUILayout.ObjectField(
                "Avatar", _avatar != null ? _avatar : Selection.activeGameObject, typeof(GameObject), true);
            if (picked != _avatar) _avatar = ResolveAvatarRoot(picked); // snap to the avatar root, not a child

            if (_avatar == null)
            {
                ThemedBox("👋 Drop your avatar above to begin.\n" +
                          "New here? Converting a VRChat avatar? Open “Convert” and press “Convert & Verify”. " +
                          "Building CVR features from scratch? Just add the pieces under each section.",
                          MessageType.Info);
                EditorGUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndScrollView();
                return;
            }

            // Find-anything search: type a word ("slider", "hue", "clothing", "wizard"…) and only matching
            // sections show, from EVERY category, forced open. The cure for "too many tabs".
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("🔍", GUILayout.Width(18));
                _search = EditorGUILayout.TextField(_search ?? "");
                if (GUILayout.Button("✕", GUILayout.Width(22))) { _search = ""; GUI.FocusControl(null); }
            }
            if (string.IsNullOrEmpty(_search))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("🪄 Convert", EditorStyles.miniButtonLeft))
                    { _catConvert = true; _catAnim = _catFeatures = false; }
                    if (GUILayout.Button("🎛 Features", EditorStyles.miniButtonMid))
                    { _catFeatures = true; _catConvert = _catAnim = false; }
                    if (GUILayout.Button("💃 Emotes", EditorStyles.miniButtonMid))
                    { _catAnim = true; _catConvert = _catFeatures = false; }
                    if (GUILayout.Button("⬍ Collapse all", EditorStyles.miniButtonRight))
                    { _catConvert = _catAnim = _catFeatures = false; }
                }
            else
                ThemedBox($"Showing every section matching “{_search}” (all categories searched).", MessageType.None);

            EditorGUILayout.Space(2);
            StepPreflight();

            _catConvert = Category("Convert  ·  VRChat → ChilloutVR", _catConvert);
            if (_catConvert)
                using (new EditorGUI.IndentLevelScope())
                {
                    ThemedBox("One-click: brings over visemes/eyes, the expression menu, and PhysBones, strips " +
                        "VRChat leftovers, and pre-flights the result — using CVR's own locomotion (no motorbike). " +
                        "Work on a COPY: it edits the avatar in place (undoable). The manual steps below do the " +
                        "same, one piece at a time.", MessageType.None);
                    _showConvertOpts = EditorGUILayout.Foldout(_showConvertOpts, "Options (advanced)", true);
                    if (_showConvertOpts)
                        using (new EditorGUI.IndentLevelScope())
                        {
                            _convertOpts.avatarBasics = EditorGUILayout.ToggleLeft(new GUIContent("Avatar basics",
                                "Viewpoint, visemes, blink, eye look → CVRAvatar."), _convertOpts.avatarBasics);
                            _convertOpts.expressions = EditorGUILayout.ToggleLeft(new GUIContent("Expression menu → AAS",
                                "Bring the VRChat menu (toggles/radials) into CVR's Advanced Settings."), _convertOpts.expressions);
                            _convertOpts.physBones = EditorGUILayout.ToggleLeft(new GUIContent("PhysBones → DynamicBones",
                                "Convert bouncing bones (approximate; tune after)."), _convertOpts.physBones);
                            _convertOpts.physBoneColliders = EditorGUILayout.ToggleLeft("… include colliders", _convertOpts.physBoneColliders);
                            _convertOpts.removeOriginalPhysBones = EditorGUILayout.ToggleLeft("… remove the VRChat PhysBones after", _convertOpts.removeOriginalPhysBones);
                            _convertOpts.stripVrcAndBroken = EditorGUILayout.ToggleLeft(new GUIContent("Strip VRChat + broken at the end",
                                "Remove leftover VRChat components and missing scripts."), _convertOpts.stripVrcAndBroken);
                            _convertOpts.forceLocalParameters = EditorGUILayout.ToggleLeft(new GUIContent("Force all params local (0 sync bits)",
                                "Makes toggles work even on a heavy avatar, but others won't see them. For testing."), _convertOpts.forceLocalParameters);
                            _convertOpts.mergePlayableLayers = EditorGUILayout.ToggleLeft(new GUIContent("Merge playable layers (NOT recommended)",
                                "Merges VRChat FX/Action layers — this is what drags in GoGo Loco and causes the motorbike pose. Leave OFF."), _convertOpts.mergePlayableLayers);
                        }
                    using (new EditorGUI.DisabledScope(_avatar == null))
                        if (GUILayout.Button("✨ Convert & Verify  (recommended)", GUILayout.Height(28)))
                            if (EditorUtility.DisplayDialog("CVRFury — Convert & Verify",
                                $"Convert '{_avatar.name}' from VRChat to ChilloutVR now?\n\nA COPY named " +
                                $"\"{_avatar.name} (CVR)\" is created and converted; your original is kept " +
                                "untouched (disabled) next to it. The VRChat Avatars SDK must be imported so " +
                                "the source data can be read.", "Convert", "Cancel"))
                                RunAndRefresh(RunConvertAndVerify);
                    EditorGUILayout.Space(4);
                    StepMenuWizard();
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("… or run the steps manually:", EditorStyles.miniBoldLabel);
                    Step0Basics();
                    Step1Parameters();
                    Step2Clips();
                    Step3PhysBones();
                    Step4Magica();
                    Step5Strip();
                    StepPrefabConv();
                }

            _catAnim = Category("Emotes, Dances & Poses", _catAnim);
            if (_catAnim)
                using (new EditorGUI.IndentLevelScope())
                {
                    StepEmotes();
                    StepEmoteWheel();
                    StepDances();
                    StepLocoStyles();
                }

            _catFeatures = Category("Avatar features  ·  sliders & NSFW", _catFeatures);
            if (_catFeatures)
                using (new EditorGUI.IndentLevelScope())
                {
                    StepClothing();
                    StepResize();
                    StepPresets();
                    StepTouch();
                    StepBreathing();
                    StepReveal();
                    StepSps();
                }

            if (!string.IsNullOrEmpty(_log))
            {
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)))
                        EditorGUIUtility.systemCopyBuffer = _log;
                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
                        _log = "";
                }
                EditorGUILayout.TextArea(_log, GUILayout.MinHeight(90));
            }

            StepCredits();

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        // Max readable column width; the window can be wider but content won't stretch past this.
        private const float ContentWidth = 600f;

        /// <summary>Snap a picked object to the avatar ROOT (the highest ancestor carrying an Animator), so
        /// dropping a child mesh/bone still targets the whole avatar instead of a sub-object.</summary>
        private static GameObject ResolveAvatarRoot(GameObject go)
        {
            if (go == null) return null;
            var best = go;
            for (var t = go.transform; t != null; t = t.parent)
                if (t.GetComponent<Animator>() != null) best = t.gameObject;
            return best;
        }

        private string RunConvertAndVerify()
        {
            if (_avatar == null) return "Select your avatar first.";

            // Safety first: convert a COPY and keep the original untouched (disabled beside it). If anything
            // goes wrong, the original is one click away — no undo archaeology needed.
            var source = _avatar;
            var copy = Instantiate(source);
            copy.name = source.name + " (CVR)";
            copy.transform.SetParent(source.transform.parent, false);
            copy.transform.localPosition = source.transform.localPosition;
            Undo.RegisterCreatedObjectUndo(copy, "CVRFury Convert");
            source.SetActive(false);
            _avatar = copy;
            Selection.activeGameObject = copy;

            var log = VRChatConverter.Convert(copy, _convertOpts);
            var lines = log.Entries.Select(e => (e.Level == BuildLog.Level.Error ? "✗ " : e.Level == BuildLog.Level.Warning ? "! " : "• ") + e.Message);
            var pre = PreflightCheck.Report(_avatar, out _);
            return "Convert:\n" + string.Join("\n", lines) + "\n\n" + pre;
        }

        // --- pretty headers ----------------------------------------------------------------------
        private static readonly Color BrandDark = new Color(0.16f, 0.09f, 0.20f);
        private static readonly Color BrandHeader = new Color(0.22f, 0.13f, 0.28f);
        private static readonly Color BrandText = new Color(0.86f, 0.74f, 0.95f);

        private void DrawBanner()
        {
            var rect = new Rect(0, 0, position.width, 44);
            EditorGUI.DrawRect(rect, BrandDark);
            EditorGUI.DrawRect(new Rect(0, 43, position.width, 2), new Color(0.45f, 0.30f, 0.55f)); // accent underline
            var title = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = BrandText }, fontSize = 16,
                alignment = TextAnchor.LowerLeft, padding = new RectOffset(12, 0, 0, 2),
            };
            GUI.Label(new Rect(0, 2, position.width, 24), "✦ CVRFury", title);
            var sub = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.62f, 0.54f, 0.70f) },
                alignment = TextAnchor.UpperLeft, padding = new RectOffset(14, 0, 0, 0),
            };
            GUI.Label(new Rect(0, 24, position.width, 18), "VRChat → ChilloutVR, made easy", sub);
            var ver = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.65f, 0.55f, 0.72f) },
                alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 12, 0, 0),
            };
            GUI.Label(rect, $"v{CckNames.CvrFuryVersion}", ver);
            GUILayout.Space(48);
        }

        /// <summary>Themed replacement for EditorGUILayout.HelpBox — same signature, but tinted to match the
        /// window instead of Unity's flat grey (warnings/errors keep semantic amber/red).</summary>
        private static void ThemedBox(string text, MessageType type)
        {
            Color bg, fg, accent;
            switch (type)
            {
                case MessageType.Error:
                    bg = new Color(0.30f, 0.13f, 0.13f); fg = new Color(1f, 0.82f, 0.82f); accent = new Color(0.85f, 0.35f, 0.35f); break;
                case MessageType.Warning:
                    bg = new Color(0.28f, 0.23f, 0.09f); fg = new Color(1f, 0.93f, 0.72f); accent = new Color(0.88f, 0.74f, 0.32f); break;
                default: // None / Info → brand tint
                    bg = new Color(0.195f, 0.165f, 0.235f); fg = new Color(0.82f, 0.80f, 0.88f); accent = new Color(0.52f, 0.37f, 0.62f); break;
            }
            var style = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, richText = true, fontSize = 11,
                normal = { textColor = fg }, padding = new RectOffset(10, 8, 6, 6),
            };
            var content = new GUIContent(text);
            var rect = GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, bg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            GUI.Label(rect, content, style);
            EditorGUILayout.Space(2);
        }

        /// <summary>A coloured, clickable category header. Returns the (possibly toggled) open state.</summary>
        private bool Category(string title, bool open)
        {
            // Search mode: categories stop gating — matching sections from anywhere show directly.
            if (!string.IsNullOrEmpty(_search)) return true;
            EditorGUILayout.Space(5);
            var rect = EditorGUILayout.GetControlRect(false, 24);
            EditorGUI.DrawRect(rect, BrandHeader);
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = BrandText }, fontSize = 12,
                alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 0, 0, 0),
            };
            GUI.Label(rect, $"{(open ? "▾" : "▸")}  {title}", style);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                open = !open;
                Event.current.Use();
                Repaint();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return open;
        }


        private void Step1Parameters()
        {
            _s1 = Foldout(_s1, "1 — Parameters (from VRChat menu)");
            if (!_s1) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox(
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
                ThemedBox(
                    "Matches on/off animation clips from a folder to your toggles by name, and (optionally) " +
                    "builds & attaches a controller so the parameters exist and toggles animate. Leave the " +
                    "Controller empty to copy CVR's stock AvatarAnimator (keeps locomotion). Do NOT drop your " +
                    "VRChat FX controller here — its hundreds of params blow the synced-bit budget.",
                    MessageType.None);
                _clipFolder = (DefaultAsset)EditorGUILayout.ObjectField("Animations Folder", _clipFolder, typeof(DefaultAsset), false);
                EditorGUILayout.LabelField("Subfolders are scanned automatically. Add more folders only if " +
                    "clips live in separate (non-nested) folders.", EditorStyles.wordWrappedMiniLabel);
                DrawExtraFolders(_extraClipFolders);
                _onSuffix = EditorGUILayout.TextField(new GUIContent("ON  clip name ends with",
                    "One or more comma-separated words, e.g. \"toggled, on, enabled\". LEAVE BLANK if the ON " +
                    "animation is named exactly after the toggle (e.g. \"Tail\" on / \"Tail off\" off)."), _onSuffix);
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
                            var folders = ClipFolderPaths();
                            RunAndRefresh(() => ToggleClipLinker.LinkClips(
                                _avatar, folders, _onSuffix, _offSuffix, _buildController, _controller));
                        }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_avatar == null || _clipFolder == null))
                        if (GUILayout.Button("Preview / refresh matches"))
                            _reviewRows = ToggleClipLinker.Preview(
                                _avatar, ClipFolderPaths(), _onSuffix, _offSuffix);
                    if (_reviewRows != null) DrawReviewList();
                }

                EditorGUILayout.Space(2);
                ThemedBox("Motorbike pose / no movement after editing the avatar (e.g. visemes) " +
                    "since you built the controller? Click this to re-point the avatar at a controller that has " +
                    "CVR locomotion. It also runs automatically at upload.", MessageType.None);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Fix motorbike pose (re-assert locomotion)"))
                        RunAndRefresh(() =>
                        {
                            var log = new BuildLog();
                            ControllerGuard.ReassertLocomotion(_avatar, log);
                            // Force CVRFury full-body layers (emotes/dances) to WriteDefaults-off so their
                            // "Off" state passes through to locomotion instead of writing the bind/motorbike pose.
                            int fixedWd = 0;
                            foreach (var c in EmoteControllers(out _))
                            {
                                fixedWd += AnimatorUtil.SetWriteDefaultsForLayers(c, "CVRFury Emote:", false);
                                fixedWd += AnimatorUtil.SetWriteDefaultsForLayers(c, "CVRFury Dances", false);
                                EditorUtility.SetDirty(c);
                            }
                            AssetDatabase.SaveAssets();
                            var msgs = log.Entries.Select(e => e.Message).ToList();
                            if (fixedWd > 0) msgs.Add($"Set {fixedWd} emote/dance state(s) to WriteDefaults-off " +
                                "(stops the Off pose overriding locomotion).");
                            // Diagnostic: any UNMASKED, weight>0 layer (other than the base locomotion) can pose
                            // the body. If it still motorbikes, the culprit is usually one of these — paste this.
                            EmoteControllers(out var dc);
                            if (dc != null)
                                msgs.Add("Layers: " + string.Join(", ", dc.layers.Select(l =>
                                    $"{l.name}[w{l.defaultWeight:0.#}{(l.avatarMask ? ",masked" : "")}]")));
                            return msgs.Count > 0 ? string.Join("\n", msgs)
                                : "Locomotion controller is OK (has CVR movement) and no emote/dance layers needed fixing.";
                        });

                EditorGUILayout.Space(2);
                ThemedBox("Still motorbiking (e.g. the avatar shipped with GoGo Loco / VRChat " +
                    "locomotion)? This force-replaces the controller with CVR's own native locomotion so the " +
                    "avatar stands. It's a clean base WITHOUT your toggles — then re-run \"Link clips & build\" " +
                    "above (empty Controller) to rebuild toggles on it.", MessageType.None);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Reset to CVR native locomotion"))
                        if (EditorUtility.DisplayDialog("CVRFury — Reset locomotion",
                            "Replace this avatar's controller with CVR's native locomotion?\n\nThe avatar will " +
                            "stand correctly, but this is a clean controller WITHOUT your toggles — you'll " +
                            "re-run “Link clips & build” to rebuild them.", "Reset", "Cancel"))
                            RunAndRefresh(ResetToCvrLocomotion);

                EditorGUILayout.Space(4);
                ThemedBox("LAST RESORT: if the clip names are all over the place and matching keeps " +
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
                                ClipFolderPaths(), _renameOn, _renameOff));
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
            ThemedBox("✔ exact match   ? best guess (check it!)   ✘ none found   ● object toggle (CVR " +
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
                ThemedBox(
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
        private static readonly string[] KindNames = { "Size", "Length", "Hue", "Emission", "Blendshape" };

        private void StepPreflight()
        {
            _sPre = Foldout(_sPre, "✈ Pre-flight check — is this avatar upload-ready?");
            if (!_sPre) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("One look before uploading: locomotion, missing scripts, shaders, " +
                    "synced-bit budget.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Run pre-flight check"))
                    {
                        try { _preflightResults = PreflightCheck.Run(_avatar); _preflight = PreflightCheck.Report(_avatar, out _preflightOk); }
                        catch (System.Exception ex) { _preflight = "Error: " + ex.Message; _preflightOk = false; _preflightResults = null; Debug.LogException(ex); }
                        Repaint();
                    }
                if (!string.IsNullOrEmpty(_preflight))
                    ThemedBox(_preflight, _preflightOk ? MessageType.Info : MessageType.Error);

                // One-click fixes for the failures we know how to repair.
                if (_preflightResults != null)
                    foreach (var res in _preflightResults)
                    {
                        if (res.ok) continue;
                        if (res.label == "Locomotion" &&
                            GUILayout.Button("Fix locomotion → reset to CVR native"))
                            RunAndRefresh(ResetToCvrLocomotion);
                        else if (res.label == "Missing scripts" &&
                            GUILayout.Button("Fix → clean missing scripts"))
                            RunAndRefresh(() =>
                            {
                                int n = MissingScriptCleaner.RemoveInHierarchy(_avatar);
                                return $"Removed {n} broken/missing-script component(s). Re-run pre-flight to confirm.";
                            });
                    }
            }
        }

        private void StepLocoStyles()
        {
            _sLoco = Foldout(_sLoco, "Crouch / Prone styles (dropdown)");
            if (!_sLoco) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("An in-game dropdown of custom crouch/prone animations (works with packs like " +
                    "CCK BaseAnimatorPatch — drop a clip OR a blend-tree asset per style). \"Default\" keeps " +
                    "CVR's normal locomotion; a style only plays while you're actually crouched/prone.",
                    MessageType.None);
                if (GUILayout.Button("+ Add style", GUILayout.Width(100))) _locoRows.Add(new LocoStyleRow());
                LocoStyleRow removeLoco = null;
                foreach (var row in _locoRows)
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            row.name = EditorGUILayout.TextField("Style name", row.name);
                            if (GUILayout.Button("✕", GUILayout.Width(24))) removeLoco = row;
                        }
                        row.motion = (Motion)EditorGUILayout.ObjectField(new GUIContent("Motion",
                            "An AnimationClip or a blend-tree asset (e.g. Kemono_CrouchingLoco)."),
                            row.motion, typeof(Motion), false);
                        if (row.motion != null && string.IsNullOrEmpty(row.name)) row.name = row.motion.name;
                        row.trigger = EditorGUILayout.Popup("Plays while", row.trigger, new[] { "Crouching", "Prone" });
                    }
                if (removeLoco != null) _locoRows.Remove(removeLoco);

                using (new EditorGUI.DisabledScope(_avatar == null || _locoRows.Count == 0))
                    if (GUILayout.Button("Build style dropdown"))
                    {
                        try
                        {
                            var styles = _locoRows.Select(r => new LocoStyles.Style
                            {
                                name = string.IsNullOrEmpty(r.name) ? "Style" : r.name,
                                motion = r.motion,
                                trigger = r.trigger == 1 ? LocoStyles.Trigger.Prone : LocoStyles.Trigger.Crouch,
                            }).ToList();
                            _locoStatus = LocoStyles.Build(_avatar, CckAvatar.FindOn(_avatar), EmoteControllers(out _), styles);
                        }
                        catch (System.Exception ex) { _locoStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                if (!string.IsNullOrEmpty(_locoStatus))
                    ThemedBox(_locoStatus, _locoStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepPresets()
        {
            _sPresets = Foldout(_sPresets, "Outfit presets (dropdown — picking one un-equips the rest)");
            if (!_sPresets) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("One in-game dropdown that swaps whole outfits: list what's ON in each preset; " +
                    "everything mentioned in any preset is turned off unless that preset includes it. " +
                    "Creates a CVRFury Modes component on the avatar (edit it there later).", MessageType.None);
                if (GUILayout.Button("+ Add preset", GUILayout.Width(110))) _presetRows.Add(new PresetRow());
                PresetRow removePreset = null;
                foreach (var row in _presetRows)
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            row.name = EditorGUILayout.TextField("Preset name", row.name);
                            if (GUILayout.Button("✕", GUILayout.Width(24))) removePreset = row;
                        }
                        for (int i = 0; i < row.objectsOn.Count; i++)
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                row.objectsOn[i] = (GameObject)EditorGUILayout.ObjectField(i == 0 ? "On in this preset" : " ",
                                    row.objectsOn[i], typeof(GameObject), true);
                                if (GUILayout.Button("✕", GUILayout.Width(24)) && row.objectsOn.Count > 1)
                                { row.objectsOn.RemoveAt(i); break; }
                            }
                        if (GUILayout.Button("+ object", EditorStyles.miniButton, GUILayout.Width(80)))
                            row.objectsOn.Add(null);
                    }
                if (removePreset != null) _presetRows.Remove(removePreset);

                using (new EditorGUI.DisabledScope(_avatar == null || _presetRows.Count < 2))
                    if (GUILayout.Button("Create preset dropdown"))
                    {
                        try { _presetStatus = CreatePresets(); }
                        catch (System.Exception ex) { _presetStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                if (!string.IsNullOrEmpty(_presetStatus))
                    ThemedBox(_presetStatus, _presetStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private string CreatePresets()
        {
            var union = _presetRows.SelectMany(r => r.objectsOn).Where(o => o != null).Distinct().ToList();
            if (union.Count == 0) return "Add at least one object to a preset.";

            const string label = "Presets";
            foreach (var existing in _avatar.GetComponents<CVRFuryModes>())
                if (existing.menuPath == label) DestroyImmediate(existing);

            var modes = Undo.AddComponent<CVRFuryModes>(_avatar);
            modes.menuPath = label;
            modes.saved = true;
            modes.modes = _presetRows.Select(r => new CVRFuryModes.Mode
            {
                name = string.IsNullOrEmpty(r.name) ? "Preset" : r.name,
                state = new FuryState
                {
                    actions = union.Select(o => new FuryAction
                    {
                        type = FuryAction.ActionType.ObjectToggle,
                        targetObject = o,
                        targetState = r.objectsOn.Contains(o),
                    }).ToList(),
                },
            }).ToList();
            EditorUtility.SetDirty(_avatar);
            return $"Created a \"{label}\" dropdown with {modes.modes.Count} preset(s) over {union.Count} object(s). " +
                   "Picking a preset turns its items on and everything else in the list off. Bakes at upload.";
        }

        private void StepMenuWizard()
        {
            _sWizard = Foldout(_sWizard, "🪄 Menu Wizard — convert the VRChat menu from the FX graph");
            if (!_sWizard) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("No clip folders, no name guessing: the FX animator graph stores exactly which " +
                    "parameter plays which clip, and the wizard reads it directly. Every row shows WHERE its " +
                    "clips came from (layer / states / blend tree) so a wrong pick is visible BEFORE you apply. " +
                    "Toggles that only switch objects on/off become CVR-NATIVE toggles — no clips at all. " +
                    "Run this on the avatar BEFORE stripping VRChat components.", MessageType.None);

                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("🪄 Preview from FX graph", GUILayout.Height(26)))
                    {
                        try { _wizardRows = MenuWizard.Preview(_avatar, out _wizardStatus); }
                        catch (System.Exception ex) { _wizardStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }

                if (_wizardRows != null && _wizardRows.Count > 0)
                {
                    _wizardScroll = EditorGUILayout.BeginScrollView(_wizardScroll, GUILayout.MinHeight(150), GUILayout.MaxHeight(320));
                    foreach (var row in _wizardRows)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            row.include = EditorGUILayout.Toggle(row.include, GUILayout.Width(18));
                            var tag = row.options != null ? $"  [dropdown — {row.options.Count} options]" : row.isSlider ? "  [slider]" : "";
                            EditorGUILayout.LabelField($"{row.display}  ({row.param}){tag}", EditorStyles.boldLabel);
                        }
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.LabelField(row.provenance, EditorStyles.miniLabel);
                            if (row.options == null)
                            {
                                row.on = (AnimationClip)EditorGUILayout.ObjectField(row.isSlider ? "Max (1)" : "ON",
                                    row.on, typeof(AnimationClip), false);
                                row.off = (AnimationClip)EditorGUILayout.ObjectField(row.isSlider ? "Min (0)" : "OFF",
                                    row.off, typeof(AnimationClip), false);
                            }
                        }
                    }
                    EditorGUILayout.EndScrollView();

                    using (new EditorGUI.DisabledScope(_avatar == null || !_wizardRows.Any(r => r.include)))
                        if (GUILayout.Button($"Apply {_wizardRows.Count(r => r.include)} menu entr(ies)"))
                        {
                            try
                            {
                                _wizardStatus = MenuWizard.Apply(_avatar, _wizardRows);
                                // Immediately verify the full causal chain — dead entries are named NOW,
                                // not after an upload.
                                _wizardStatus += "\n\n" + MenuVerifier.Verify(_avatar);
                                _log = _wizardStatus; _wizardRows = null;
                            }
                            catch (System.Exception ex) { _wizardStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                            Repaint();
                        }
                }

                using (new EditorGUILayout.HorizontalScope())
                using (new EditorGUI.DisabledScope(_avatar == null))
                {
                    if (GUILayout.Button("🔬 Verify menu (no upload needed)"))
                    {
                        try { _wizardStatus = MenuVerifier.Verify(_avatar); _log = _wizardStatus; }
                        catch (System.Exception ex) { _wizardStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                    if (GUILayout.Button(new GUIContent("🔓 Unlock Poiyomi",
                        "Locked Poiyomi shaders ignore animation on non-'Animated' properties, so dissolve/" +
                        "hue/emission toggles silently do nothing in CVR. This unlocks them so they animate.")))
                    {
                        try { _wizardStatus = PoiyomiTools.UnlockAll(_avatar); _log = _wizardStatus; }
                        catch (System.Exception ex) { _wizardStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                }

                if (!string.IsNullOrEmpty(_wizardStatus))
                    ThemedBox(_wizardStatus, _wizardStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepPrefabConv()
        {
            _sPrefabConv = Foldout(_sPrefabConv, "🧰 Prefab Converter (VRCFury prefabs → CVRFury)");
            if (!_sPrefabConv) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("For toys, clothing and avatar additions built on VRCFury: import the .unitypackage, " +
                    "put the prefab on your avatar as its instructions say, then convert here. Every VRCFury " +
                    "feature is read and recreated as CVRFury components — toggles (objects, blendshapes, " +
                    "material swaps, scale, shader properties), Full Controllers (animators + menu parameters), " +
                    "and Armature Links (how it attaches to your skeleton). What can't convert is listed with " +
                    "the reason, never dropped silently. VRCFury itself must be imported so the data is readable.",
                    MessageType.None);

                _prefabRemoveAfter = EditorGUILayout.ToggleLeft(new GUIContent(
                    "Remove the VRCFury components after converting",
                    "Recommended — they'd trigger VRChat's build hooks in Play mode and get stripped at upload anyway."),
                    _prefabRemoveAfter);

                using (new EditorGUI.DisabledScope(_avatar == null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Scan (read-only)"))
                    {
                        try { _prefabStatus = PrefabConverter.Scan(_avatar); _log = _prefabStatus; }
                        catch (System.Exception ex) { _prefabStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                    if (GUILayout.Button("Convert VRCFury → CVRFury"))
                    {
                        try { _prefabStatus = PrefabConverter.Convert(_avatar, _prefabRemoveAfter); _log = _prefabStatus; }
                        catch (System.Exception ex) { _prefabStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                }
                if (!string.IsNullOrEmpty(_prefabStatus))
                    ThemedBox(_prefabStatus, _prefabStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepClothing()
        {
            _sClothing = Foldout(_sClothing, "🧥 Clothing setup — toggles + body-shape links + clipping fixes");
            if (!_sClothing) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("Drag your clothing items in and configure each one: a menu toggle, a Blendshape " +
                    "Link so the clothes FOLLOW body sliders (bust/hips/weight deform the outfit instead of " +
                    "clipping through it), and clipping-fix body shapes that apply only while the item is worn " +
                    "(e.g. Shrink_Torso while the coat is on). Everything is created in one click.",
                    MessageType.None);

                _clothingBody = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(new GUIContent("Body mesh",
                    "The body — the source for shape links and the target for clipping-fix shapes."),
                    _clothingBody, typeof(SkinnedMeshRenderer), true);
                _clothingFolder = EditorGUILayout.TextField(new GUIContent("Menu folder",
                    "Toggles land under this submenu (blank = top level)."), _clothingFolder);

                var bodyShapes = BlendshapeNames(_clothingBody);
                AvatarFeaturePack.ClothingItem removeItem = null;
                foreach (var item in _clothingItems)
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var newGo = (GameObject)EditorGUILayout.ObjectField(item.go, typeof(GameObject), true);
                            if (newGo != item.go)
                            {
                                item.go = newGo;
                                if (newGo != null && string.IsNullOrEmpty(item.label))
                                { item.label = AvatarFeaturePack.Prettify(newGo.name); item.defaultOn = newGo.activeSelf; }
                            }
                            if (GUILayout.Button("✕", GUILayout.Width(24)) && _clothingItems.Count > 1) removeItem = item;
                        }
                        if (item.go == null) continue;

                        item.label = EditorGUILayout.TextField("Menu name", item.label);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            item.defaultOn = EditorGUILayout.ToggleLeft("on by default", item.defaultOn, GUILayout.Width(110));
                            item.linkBodyShapes = EditorGUILayout.ToggleLeft(new GUIContent("follow body sliders",
                                "Adds a Blendshape Link: the clothing mesh mirrors the body's shape keys."),
                                item.linkBodyShapes, GUILayout.Width(150));
                        }

                        if (bodyShapes.Length > 0)
                        {
                            AvatarFeaturePack.ClipFix removeFix = null;
                            foreach (var fix in item.fixes)
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    fix.shapeIndex = EditorGUILayout.Popup(
                                        Mathf.Clamp(fix.shapeIndex, 0, bodyShapes.Length - 1), bodyShapes);
                                    fix.value = EditorGUILayout.Slider(fix.value, 0f, 100f, GUILayout.Width(150));
                                    if (GUILayout.Button("✕", GUILayout.Width(24))) removeFix = fix;
                                }
                            if (removeFix != null) item.fixes.Remove(removeFix);
                            if (GUILayout.Button(new GUIContent("+ clipping fix (body shape while worn)",
                                "A body blendshape applied only while this item is ON — the coat/bra clipping fix."),
                                EditorStyles.miniButton, GUILayout.Width(240)))
                                item.fixes.Add(new AvatarFeaturePack.ClipFix());
                        }
                        else
                            EditorGUILayout.LabelField(" ", "Assign the Body mesh above to add clipping fixes.",
                                EditorStyles.miniLabel);
                    }
                if (removeItem != null) _clothingItems.Remove(removeItem);
                if (GUILayout.Button("+ item", EditorStyles.miniButton, GUILayout.Width(70)))
                    _clothingItems.Add(new AvatarFeaturePack.ClothingItem());

                using (new EditorGUI.DisabledScope(_avatar == null || _clothingItems.All(i => i.go == null)))
                    if (GUILayout.Button("Set up clothing"))
                    {
                        try
                        {
                            _clothingStatus = AvatarFeaturePack.CreateClothing(_avatar, _clothingBody, _clothingItems, _clothingFolder);
                            _clothingItems.Clear();
                            _clothingItems.Add(new AvatarFeaturePack.ClothingItem());
                        }
                        catch (System.Exception ex) { _clothingStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                if (!string.IsNullOrEmpty(_clothingStatus))
                    ThemedBox(_clothingStatus, _clothingStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepTouch()
        {
            _sTouch = Foldout(_sTouch, "🫦 Touch reactions (body part → blendshape / sound / particles)");
            if (!_sTouch) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("Touch a body part → face reactions. Stack as MANY blendshapes as you want on one " +
                    "touch, each with its own strength. Zones: presets, or CUSTOM — place a box exactly where " +
                    "the touch should count (just the nose, not the whole head), sized and positioned visually " +
                    "with the magenta gizmo. Instant, or BUILD-UP (grows the longer the touch lasts). Optional: " +
                    "sound at the touched spot, heart particles.", MessageType.None);

                _touchFace = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Face mesh", _touchFace,
                    typeof(SkinnedMeshRenderer), true);

                // Any number of blendshapes fire together on the one touch.
                var touchShapes = BlendshapeNames(_touchFace);
                if (touchShapes.Length > 0)
                {
                    TouchShapeRow removeShape = null;
                    foreach (var row in _touchShapes)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            row.shape = EditorGUILayout.Popup(Mathf.Clamp(row.shape, 0, touchShapes.Length - 1), touchShapes);
                            row.value = EditorGUILayout.Slider(row.value, 0f, 100f, GUILayout.Width(150));
                            if (GUILayout.Button("✕", GUILayout.Width(24)) && _touchShapes.Count > 1) removeShape = row;
                        }
                    if (removeShape != null) _touchShapes.Remove(removeShape);
                    if (GUILayout.Button("+ blendshape", EditorStyles.miniButton, GUILayout.Width(100)))
                        _touchShapes.Add(new TouchShapeRow());
                }

                // Preset bone zones + "Custom": a box the user places and sizes with the scene gizmo.
                var zoneLabels = ReactionPack.TouchZones.Select(z => z.label).Append("Custom (place a box)").ToArray();
                _touchZone = EditorGUILayout.Popup("Touch zone", Mathf.Clamp(_touchZone, 0, zoneLabels.Length - 1), zoneLabels);
                bool custom = _touchZone == zoneLabels.Length - 1;
                if (custom)
                {
                    _touchCustomName = EditorGUILayout.TextField(new GUIContent("Zone name",
                        "Names the parameter and menu entry — e.g. \"Nose\"."), _touchCustomName);
                    _touchCustomZone = (CVRFuryTouchZone)EditorGUILayout.ObjectField(new GUIContent("Zone box",
                        "The placed touch area. Move it with the transform tools; edit Size on the component. " +
                        "The magenta box in the Scene view IS the trigger area."),
                        _touchCustomZone, typeof(CVRFuryTouchZone), true);
                    using (new EditorGUI.DisabledScope(_avatar == null))
                        if (_touchCustomZone == null && GUILayout.Button("Place zone box (starts at the head — drag it onto the spot)"))
                        {
                            var anim = _avatar.GetComponentInChildren<Animator>();
                            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                            var go = new GameObject($"CVRFury Touch Zone ({_touchCustomName})");
                            Undo.RegisterCreatedObjectUndo(go, "Touch zone");
                            go.transform.SetParent(head != null ? head : _avatar.transform, false);
                            go.transform.localPosition = new Vector3(0f, 0f, 0.1f);
                            _touchCustomZone = go.AddComponent<CVRFuryTouchZone>();
                            Selection.activeGameObject = go;
                            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                            _touchStatus = "Zone placed (magenta box in the Scene view = the exact trigger area). " +
                                           "Drag it over the spot, set Size on its component, then add the reaction.";
                        }
                    if (_touchCustomZone != null)
                        ThemedBox("Zone box parented to '" +
                                  (_touchCustomZone.transform.parent != null ? _touchCustomZone.transform.parent.name : "scene root") +
                                  "' — it follows that bone. What the magenta gizmo covers is exactly what will trigger.",
                                  MessageType.None);
                }

                _touchStyle = GUILayout.Toolbar(_touchStyle, new[] { "Instant", "Build-up" });
                if (_touchStyle == 1)
                    _touchBuildSeconds = EditorGUILayout.Slider(new GUIContent("Build time (s)",
                        "How long continuous touch takes to reach the full reaction."), _touchBuildSeconds, 1f, 20f);

                _touchSound = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("Sound (optional)",
                    "Played positionally at the touched spot each time the touch starts."),
                    _touchSound, typeof(AudioClip), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _touchParticles = EditorGUILayout.ToggleLeft("💕 heart particles", _touchParticles, GUILayout.Width(140));
                    _touchOthers = EditorGUILayout.ToggleLeft("others can trigger it", _touchOthers);
                }

                using (new EditorGUI.DisabledScope(_avatar == null || _touchFace == null || touchShapes.Length == 0 ||
                                                   (custom && _touchCustomZone == null)))
                    if (GUILayout.Button("Add touch reaction"))
                    {
                        try
                        {
                            var shapes = _touchShapes
                                .Select(r => new ReactionPack.ShapeReaction
                                { shape = touchShapes[Mathf.Clamp(r.shape, 0, touchShapes.Length - 1)], value = r.value })
                                .ToList();
                            if (custom)
                            {
                                _touchStatus = ReactionPack.CreateTouchReaction(_avatar, _touchFace, shapes,
                                    HumanBodyBones.Head, string.IsNullOrEmpty(_touchCustomName) ? "Custom" : _touchCustomName,
                                    _touchCustomZone, _touchOthers,
                                    _touchStyle == 1 ? ReactionPack.Style.BuildUp : ReactionPack.Style.Instant,
                                    _touchBuildSeconds, _touchSound, _touchParticles);
                                _touchCustomZone = null; // consumed — it became the trigger
                            }
                            else
                            {
                                var zone = ReactionPack.TouchZones[Mathf.Clamp(_touchZone, 0, ReactionPack.TouchZones.Length - 1)];
                                _touchStatus = ReactionPack.CreateTouchReaction(_avatar, _touchFace, shapes,
                                    zone.bone, zone.label, null, _touchOthers,
                                    _touchStyle == 1 ? ReactionPack.Style.BuildUp : ReactionPack.Style.Instant,
                                    _touchBuildSeconds, _touchSound, _touchParticles);
                            }
                        }
                        catch (System.Exception ex) { _touchStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                if (!string.IsNullOrEmpty(_touchStatus))
                    ThemedBox(_touchStatus, _touchStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepBreathing()
        {
            _sBreathing = Foldout(_sBreathing, "🫁 Breathing (always-on idle life)");
            if (!_sBreathing) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("A generated looping layer cycles a chest/breath blendshape forever — the subtle " +
                    "idle motion that makes an avatar feel alive. No toggle or slider can produce this; it " +
                    "needs a looping animation, which is built and merged for you.", MessageType.None);

                _breathMesh = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(new GUIContent("Mesh",
                    "The mesh with the breath shape — usually the Body (look for Breathe / Chest / Sternum shapes)."),
                    _breathMesh, typeof(SkinnedMeshRenderer), true);
                var breathShapes = BlendshapeNames(_breathMesh);
                if (breathShapes.Length > 0)
                    _breathShapeIndex = EditorGUILayout.Popup("Breath blendshape",
                        Mathf.Clamp(_breathShapeIndex, 0, breathShapes.Length - 1), breathShapes);
                _breathCycle = EditorGUILayout.Slider(new GUIContent("Breath cycle (s)",
                    "Seconds per full inhale + exhale. ~4s reads calm; ~2s reads worked up."), _breathCycle, 1f, 10f);
                _breathIntensity = EditorGUILayout.Slider(new GUIContent("Intensity",
                    "How far the shape moves at full inhale. Subtle (20–50) sells it best."), _breathIntensity, 5f, 100f);

                using (new EditorGUI.DisabledScope(_avatar == null || _breathMesh == null || breathShapes.Length == 0))
                    if (GUILayout.Button("Add breathing"))
                    {
                        try
                        {
                            _breathStatus = ReactionPack.CreateBreathing(_avatar, _breathMesh,
                                breathShapes[_breathShapeIndex], _breathCycle, _breathIntensity);
                        }
                        catch (System.Exception ex) { _breathStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                if (!string.IsNullOrEmpty(_breathStatus))
                    ThemedBox(_breathStatus, _breathStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepReveal()
        {
            _sReveal = Foldout(_sReveal, "👁 Reveal invisible clothing (enabled but not rendering)");
            if (!_sReveal) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("For items whose GameObject is ON but the mesh doesn't render (the selection outline " +
                    "shows, the surface doesn't): creators often toggle clothing via an animated MATERIAL " +
                    "property (Poiyomi dissolve/alpha) instead of the object — with no animator running in the " +
                    "editor, the material sits at its 'hidden' default. Diagnose reads the animator clips to " +
                    "find which property the toggle drives; \"Make visible\" bakes the shown value into the " +
                    "material (unlocking locked Poiyomi first when possible).", MessageType.None);

                _revealTarget = (Renderer)EditorGUILayout.ObjectField(new GUIContent("Invisible mesh",
                    "The renderer that should be visible but isn't — drop the object from the Hierarchy."),
                    _revealTarget, typeof(Renderer), true);

                using (new EditorGUI.DisabledScope(_avatar == null || _revealTarget == null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Diagnose"))
                    {
                        try { _revealStatus = MeshRevealer.Diagnose(_avatar, _revealTarget); _log = _revealStatus; }
                        catch (System.Exception ex) { _revealStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                    if (GUILayout.Button("Make visible (bake shown state)"))
                    {
                        try { _revealStatus = MeshRevealer.Reveal(_avatar, _revealTarget); _log = _revealStatus; }
                        catch (System.Exception ex) { _revealStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                }
                if (!string.IsNullOrEmpty(_revealStatus))
                    ThemedBox(_revealStatus, _revealStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepResize()
        {
            _sResize = Foldout(_sResize, "Sliders (size, length, hue, emission, blendshape)");
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
                        {
                            var r = new SliderRow { kind = SliderKind.Size, label = "Avatar Size" };
                            r.targets[0] = _avatar.transform;
                            _scaleRows.Add(r);
                        }
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

                        row.label = EditorGUILayout.TextField(new GUIContent("Menu name",
                            "Optional. Blank = named after the first target (e.g. \"Boobs\" for a left+right pair)."),
                            row.label);

                        if (row.kind == SliderKind.Size || row.kind == SliderKind.Length)
                            DrawTargetList(row.targets, "Part", "Add part — list several for equal scaling (L+R)");
                        else if (row.kind == SliderKind.Blendshape)
                        {
                            DrawRendererList(row.renderers, "Mesh");
                            var shapes = BlendshapeNames(row.renderers.FirstOrDefault() as SkinnedMeshRenderer);
                            if (shapes.Length == 0)
                                EditorGUILayout.LabelField(" ", "Drop a Skinned Mesh Renderer with blendshapes.", EditorStyles.miniLabel);
                            else
                                row.blendIndex = EditorGUILayout.Popup("Blendshape", Mathf.Clamp(row.blendIndex, 0, shapes.Length - 1), shapes);
                            if (row.min == 0.5f && row.max == 2f) { row.min = 0f; row.max = 100f; } // sensible blendshape range
                        }
                        else
                        {
                            DrawRendererList(row.renderers, "Mesh");
                            if (string.IsNullOrEmpty(row.property))
                                row.property = row.kind == SliderKind.Hue ? "_MainHueShift" : "_EmissionStrength";
                            row.property = EditorGUILayout.TextField(new GUIContent("Shader property",
                                "The material float property to drive. Defaults are Poiyomi's; check your shader " +
                                "if the slider does nothing."), row.property);
                            // Hue/emission are 0..1 properties — the scale defaults (0.5–2) are out of range
                            // and were a major reason hue sliders "did nothing" or behaved weirdly.
                            if (row.min == 0.5f && row.max == 2f) { row.min = 0f; row.max = 1f; }
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (row.kind == SliderKind.Length)
                            {
                                GUILayout.Label("axis", GUILayout.Width(30));
                                row.axis = EditorGUILayout.Popup(row.axis, AxisNames, GUILayout.Width(45));
                                GUILayout.Space(8);
                            }
                            GUILayout.Label("min", GUILayout.Width(28));
                            row.min = EditorGUILayout.FloatField(row.min, GUILayout.Width(60));
                            GUILayout.Space(8);
                            GUILayout.Label("max", GUILayout.Width(28));
                            row.max = EditorGUILayout.FloatField(row.max, GUILayout.Width(60));
                            GUILayout.FlexibleSpace();
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
                    ThemedBox(_resizeStatus,
                        _resizeStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private static void DrawTargetList(List<Transform> list, string label, string addTip)
        {
            for (int i = 0; i < list.Count; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    list[i] = (Transform)EditorGUILayout.ObjectField(i == 0 ? label : " ", list[i], typeof(Transform), true);
                    if (GUILayout.Button("✕", GUILayout.Width(24)) && list.Count > 1) { list.RemoveAt(i); break; }
                }
            if (GUILayout.Button(new GUIContent("+ target", addTip), EditorStyles.miniButton, GUILayout.Width(80)))
                list.Add(null);
        }

        private static void DrawRendererList(List<Renderer> list, string label)
        {
            for (int i = 0; i < list.Count; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    list[i] = (Renderer)EditorGUILayout.ObjectField(i == 0 ? label : " ", list[i], typeof(Renderer), true);
                    if (GUILayout.Button("✕", GUILayout.Width(24)) && list.Count > 1) { list.RemoveAt(i); break; }
                }
            if (GUILayout.Button(new GUIContent("+ mesh", "Add another mesh for equal scaling"), EditorStyles.miniButton, GUILayout.Width(80)))
                list.Add(null);
        }

        private string CreateSliders()
        {
            int made = 0;
            var errors = new List<string>();
            foreach (var row in _scaleRows)
            {
                string err = null;
                var transforms = row.targets.Where(t => t != null).ToList();
                var renderers = row.renderers.Where(r => r != null).ToList();
                switch (row.kind)
                {
                    case SliderKind.Size:
                    {
                        var name = SliderLabel(row.label, transforms.Count > 0 ? transforms[0].name : null,
                            transforms.Count > 0 && transforms[0] == (_avatar ? _avatar.transform : null) ? "Avatar Size" : "Size");
                        err = AvatarSizeSlider.AddSlider(_avatar, transforms, Vector3.one, row.min, row.max, name);
                        break;
                    }
                    case SliderKind.Length:
                    {
                        var axisVec = new Vector3(row.axis == 0 ? 1 : 0, row.axis == 1 ? 1 : 0, row.axis == 2 ? 1 : 0);
                        var name = SliderLabel(row.label, transforms.Count > 0 ? transforms[0].name : null, "Length");
                        err = AvatarSizeSlider.AddSlider(_avatar, transforms, axisVec, row.min, row.max, name);
                        break;
                    }
                    case SliderKind.Hue:
                        err = AvatarSizeSlider.AddMaterialSlider(_avatar, renderers, row.property, row.min, row.max,
                            SliderLabel(row.label, renderers.Count > 0 ? renderers[0].name : null, "Hue"));
                        break;
                    case SliderKind.Emission:
                        err = AvatarSizeSlider.AddMaterialSlider(_avatar, renderers, row.property, row.min, row.max,
                            SliderLabel(row.label, renderers.Count > 0 ? renderers[0].name : null, "Emission"));
                        break;
                    case SliderKind.Blendshape:
                    {
                        var smrs = row.renderers.OfType<SkinnedMeshRenderer>().Where(s => s != null).ToList();
                        var shapes = BlendshapeNames(smrs.FirstOrDefault());
                        var shape = shapes.Length > 0 ? shapes[Mathf.Clamp(row.blendIndex, 0, shapes.Length - 1)] : null;
                        err = AvatarSizeSlider.AddBlendshapeSlider(_avatar, smrs, shape, row.min, row.max,
                            SliderLabel(row.label, shape, "Slider"));
                        break;
                    }
                }
                if (err != null) errors.Add(err); else made++;
            }
            var msg = $"Created {made} slider(s). They appear in the Advanced Settings menu and apply at runtime in CVR.";
            if (errors.Count > 0) msg += "\nSkipped: " + string.Join("; ", errors);
            return msg;
        }

        /// <summary>Blendshape names on a mesh (empty array if none), for the Blendshape slider dropdown.</summary>
        private static string[] BlendshapeNames(SkinnedMeshRenderer smr)
        {
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null) return System.Array.Empty<string>();
            var names = new string[mesh.blendShapeCount];
            for (int i = 0; i < names.Length; i++) names[i] = mesh.GetBlendShapeName(i);
            return names;
        }

        /// <summary>Menu label: the user's custom name if set, else "{first target} {kind}".</summary>
        private static string SliderLabel(string custom, string firstName, string kindWord)
        {
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
            return (string.IsNullOrEmpty(firstName) ? "?" : firstName) + " " + kindWord;
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
                ThemedBox("Use a shader with DPS / light-based deform (Poiyomi works well). This " +
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
                    ThemedBox(_spsStatus,
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
                    ThemedBox("Magica Cloth 2 is not installed — this step is skipped. Install it to enable.",
                                            MessageType.None);
                    return;
                }
                ThemedBox(
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
                var comp = go.GetComponent(magicaType);
                if (comp == null) comp = go.AddComponent(magicaType); // '??' breaks on Unity's fake-null
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
                ThemedBox("Copies the VRChat viewpoint, visemes, blink and eye-look settings onto the CVRAvatar.",
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
                ThemedBox("Removes leftover VRChat components and missing scripts once you're happy. " +
                                        "Do this last — afterwards you can remove the VRChat SDK.", MessageType.Warning);
                _removeFinalIK = EditorGUILayout.ToggleLeft("Also remove FinalIK / VRIK (CVR has its own IK)", _removeFinalIK);
                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Strip VRChat + broken components"))
                        if (EditorUtility.DisplayDialog("CVRFury — Strip components",
                            $"Permanently remove VRChat + broken components from '{_avatar.name}'?\n\nDo this only " +
                            "once you're happy with the conversion. It edits the avatar in place (undoable).",
                            "Strip", "Cancel"))
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
                ThemedBox(
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

        private void StepDances()
        {
            _sDances = Foldout(_sDances, "Sync Dances → CVR dance menu");
            if (!_sDances) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("Turns a Sync-Dances-style pack already in your project into a CVR " +
                    "dance menu: a synced dropdown (Off + each dance). Leave the folder blank to auto-find it " +
                    "by name. Uses only the dance clips — none of the VRChat setup.", MessageType.Info);
                _dancesFolder = (DefaultAsset)EditorGUILayout.ObjectField("Dances folder (optional)", _dancesFolder, typeof(DefaultAsset), false);

                if (GUILayout.Button("Find dances"))
                {
                    try
                    {
                        var path = _dancesFolder != null ? AssetDatabase.GetAssetPath(_dancesFolder) : null;
                        _dances = SyncDances.Detect(path, out var used);
                        _dancesFolderUsed = used;
                        _dancesStatus = _dances.Count == 0
                            ? "No dance clips found. Point the folder field at the pack's animations folder and try again."
                            : $"Found {_dances.Count} dance(s) in '{used}': " +
                              string.Join(", ", _dances.Take(12).Select(d => d.name)) + (_dances.Count > 12 ? " …" : "");
                    }
                    catch (System.Exception ex) { _dancesStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                    Repaint();
                }

                using (new EditorGUI.DisabledScope(_avatar == null || _dances.Count == 0))
                    if (GUILayout.Button($"Build dance menu ({_dances.Count})"))
                    {
                        try
                        {
                            var controllers = EmoteControllers(out _);
                            _dancesStatus = SyncDances.Build(_avatar, CckAvatar.FindOn(_avatar), controllers, _dances, _dancesFolderUsed);
                        }
                        catch (System.Exception ex) { _dancesStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }

                if (!string.IsNullOrEmpty(_dancesStatus))
                    ThemedBox(_dancesStatus,
                        _dancesStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        private void StepEmoteWheel()
        {
            _sEmoteWheel = Foldout(_sEmoteWheel, "Emote wheel (CVR emote menu — separate from toggles)");
            if (!_sEmoteWheel) return;
            using (new EditorGUI.IndentLevelScope())
            {
                ThemedBox("Replaces the clips in ChilloutVR's built-in EMOTE WHEEL (the emote " +
                    "radial, not the Advanced Settings toggles). Detect first — it reads the avatar's animator " +
                    "for the real emote slots (states driven by the Emote parameter), so you see exactly what " +
                    "you're changing.", MessageType.Info);

                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Detect emote slots"))
                    {
                        try
                        {
                            EmoteControllers(out var c);
                            var slots = c != null ? EmoteSlots.Detect(c) : new List<EmoteSlots.Slot>();
                            _emoteSlots.Clear();
                            foreach (var s in slots)
                                _emoteSlots.Add(new EmoteSlotRow { index = s.index, current = s.clip ? s.clip.name : "(empty)" });
                            _emoteWheelStatus = slots.Count == 0
                                ? "No emote slots found — this controller has no Emote-parameter states. Run " +
                                  "Step 2 (Build & attach) so it carries CVR's emote layer, then detect again."
                                : $"Found {slots.Count} emote slot(s): " +
                                  string.Join(", ", slots.Select(s => $"#{s.index} ({s.location})")) +
                                  ". Assign replacements below, then Apply.";
                        }
                        catch (System.Exception ex) { _emoteWheelStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }

                foreach (var row in _emoteSlots)
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField($"Emote slot {row.index}", $"now: {row.current}");
                        row.newClip = (AnimationClip)EditorGUILayout.ObjectField("Replace with", row.newClip, typeof(AnimationClip), false);
                        row.audio = (AudioClip)EditorGUILayout.ObjectField("Audio (optional)", row.audio, typeof(AudioClip), false);
                    }

                if (_emoteSlots.Count > 0)
                    using (new EditorGUI.DisabledScope(_avatar == null))
                        if (GUILayout.Button("Apply emote changes"))
                        {
                            try { _emoteWheelStatus = ApplyEmoteWheel(); }
                            catch (System.Exception ex) { _emoteWheelStatus = "Error: " + ex.Message; Debug.LogException(ex); }
                            Repaint();
                        }

                if (!string.IsNullOrEmpty(_emoteWheelStatus))
                    ThemedBox(_emoteWheelStatus,
                        _emoteWheelStatus.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }
        }

        /// <summary>Controllers to write emote slots into: the live AAS animator plus the writable per-avatar
        /// base controller (so changes survive CVR's upload regeneration). <paramref name="detect"/> is the
        /// one to read slots from.</summary>
        private AnimatorController[] EmoteControllers(out AnimatorController detect)
        {
            var list = new List<AnimatorController>();
            var cvr = CckAvatar.FindOn(_avatar);
            var anim = cvr != null ? Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Animator) as AnimatorController : null;
            if (anim == null)
            {
                var a = _avatar != null ? _avatar.GetComponentInChildren<Animator>() : null;
                anim = a != null ? a.runtimeAnimatorController as AnimatorController : null;
            }
            if (anim != null) list.Add(anim);
            var baseC = cvr != null ? Reflect.GetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_BaseController) as AnimatorController : null;
            if (baseC != null && baseC != anim && IsWritablePerAvatarController(baseC)) list.Add(baseC);
            detect = anim ?? baseC;
            return list.ToArray();
        }

        private string ApplyEmoteWheel()
        {
            var controllers = EmoteControllers(out _);
            if (controllers.Length == 0) return "No writable controller — run Step 2 (Build & attach) first.";

            int changed = 0;
            var errors = new List<string>();
            foreach (var row in _emoteSlots)
            {
                if (row.newClip == null) continue;
                if (row.audio != null)
                {
                    var err = EmoteSlots.SetClipWithAudio(_avatar, controllers, row.index, row.newClip, row.audio);
                    if (err != null) errors.Add(err); else changed++;
                }
                else
                {
                    bool any = false;
                    foreach (var c in controllers) if (EmoteSlots.SetClip(c, row.index, row.newClip)) any = true;
                    if (any) changed++; else errors.Add($"Slot {row.index} not found.");
                }
            }
            foreach (var c in controllers) EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            CckAvatar.FindOn(_avatar)?.Persist();

            if (changed == 0 && errors.Count == 0) return "Nothing to apply — assign a replacement clip to a slot first.";
            var msg = $"Updated {changed} emote slot(s) in the emote wheel.";
            if (errors.Count > 0) msg += "\nSkipped: " + string.Join("; ", errors);
            return msg;
        }

        private string ResetToCvrLocomotion()
        {
            var cvr = CckAvatar.FindOn(_avatar);
            if (cvr == null) return "No CVRAvatar found on the selected avatar.";
            var stock = ConversionContext.FindCvrStockAnimator();
            if (stock == null)
                return "Couldn't find CVR's stock AvatarAnimator. Make sure the ChilloutVR CCK is imported " +
                       "(it ships AvatarAnimator.controller under the CCK's Animations folder).";

            const string dir = "Assets/CVRFury Generated";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "CVRFury Generated");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{_avatar.name} CVR Locomotion.controller");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(stock), path))
                return "Failed to copy CVR's stock AvatarAnimator.";
            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (copy == null) return "Copied CVR's animator but couldn't load it back.";

            Reflect.SetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_BaseController, copy);
            Reflect.SetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Animator, copy);
            Reflect.SetField(cvr.AdvancedSettings, CckNames.AdvancedSettings_Initialized, true);
            var anim = _avatar.GetComponentInChildren<Animator>();
            if (anim != null) anim.runtimeAnimatorController = copy;
            cvr.Persist();

            return "Reset to CVR's native locomotion — the avatar will now STAND in ChilloutVR (no more " +
                   "motorbike). This is a clean controller WITHOUT your toggles/dances. Next: run \"Link clips " +
                   "& build\" above with the Controller field EMPTY to rebuild toggles on this base, then re-add " +
                   "Sync Dances / emotes if you use them.";
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
            // Full-body override layers must run WriteDefaults OFF: their empty "Off" state then contributes
            // nothing, so locomotion shows through. WD ON makes the Off state write the default/bind pose over
            // locomotion — the motorbike. (This is the opposite of clothing toggles, which are mask-protected.)
            const bool wd = false;
            // Repair any emote layers built with the wrong WriteDefaults from an earlier run.
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

        /// <summary>All folders to scan for clips: the primary Animations Folder plus any extras. Each is
        /// scanned recursively by FindAssets, so nested subfolders come along automatically.</summary>
        private string[] ClipFolderPaths()
        {
            var paths = new List<string>();
            if (_clipFolder != null) paths.Add(AssetDatabase.GetAssetPath(_clipFolder));
            foreach (var f in _extraClipFolders)
                if (f != null) paths.Add(AssetDatabase.GetAssetPath(f));
            return paths.ToArray();
        }

        /// <summary>A small editable list of extra folder slots (for clips spread across separate folders).</summary>
        private static void DrawExtraFolders(List<DefaultAsset> list)
        {
            int removeAt = -1;
            for (int i = 0; i < list.Count; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    list[i] = (DefaultAsset)EditorGUILayout.ObjectField("+ also scan", list[i], typeof(DefaultAsset), false);
                    if (GUILayout.Button("✕", GUILayout.Width(24))) removeAt = i;
                }
            if (removeAt >= 0) list.RemoveAt(removeAt);
            if (GUILayout.Button("+ add folder", EditorStyles.miniButton, GUILayout.Width(100))) list.Add(null);
        }

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

        /// <summary>Themed sub-section foldout — a thin tinted bar with an accent edge, consistent with the
        /// category headers (just quieter), instead of Unity's clashing grey foldout box.</summary>
        private bool Foldout(bool state, string label)
        {
            // Search mode: only matching sections render, and they render OPEN.
            if (!string.IsNullOrEmpty(_search))
            {
                if (label.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
                state = true;
            }
            EditorGUILayout.Space(2);
            var rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.DrawRect(rect, new Color(0.185f, 0.165f, 0.215f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), new Color(0.45f, 0.32f, 0.55f));
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.80f, 0.78f, 0.87f) }, fontSize = 11,
                alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 0, 0, 0),
            };
            GUI.Label(rect, $"{(state ? "▾" : "▸")}  {label}", style);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                state = !state;
                Event.current.Use();
                Repaint();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return state;
        }
    }
}
