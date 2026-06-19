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

        private bool _s0, _s1 = true, _s2 = true, _se, _s3, _s4, _s5, _sSps;

        // SPS / DPS (experimental)
        private Transform _spsPlug;
        private Transform _spsSocket;

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
            Step5Strip();

            if (!string.IsNullOrEmpty(_log))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_log, GUILayout.MinHeight(120));
            }

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

        private void StepSps()
        {
            _sSps = Foldout(_sSps, "SPS / DPS (NSFW penetration) — experimental");
            if (!_sSps) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox(
                    "Detects VRChat penetration markers (VRCFury SPS, VRChat Contacts/TPS, Raliv DPS). " +
                    "ChilloutVR has NO native SPS deformation — the shader bending doesn't port. What converts " +
                    "is the contact layer: a plug tip → CVRPointer, a socket → CVRAdvancedAvatarSettingsTrigger. " +
                    "Conversion is gated on a design choice (see chat); detection + manual location picking work now.",
                    MessageType.Warning);

                using (new EditorGUI.DisabledScope(_avatar == null))
                    if (GUILayout.Button("Detect SPS / DPS markers"))
                        RunAndRefresh(() => SpsConverter.DetectReport(_avatar));

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Choose locations (VRCFury-style)", EditorStyles.miniBoldLabel);
                _spsPlug = (Transform)EditorGUILayout.ObjectField(new GUIContent("Plug (penetrator) tip",
                    "The transform at the tip of the plug — becomes a CVRPointer when conversion is enabled."),
                    _spsPlug, typeof(Transform), true);
                _spsSocket = (Transform)EditorGUILayout.ObjectField(new GUIContent("Socket (orifice)",
                    "The orifice transform — becomes a CVRAdvancedAvatarSettingsTrigger when conversion is enabled."),
                    _spsSocket, typeof(Transform), true);
                EditorGUILayout.HelpBox("Conversion button appears once we lock the CVR target system.", MessageType.None);
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
            }
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

            var existingParams = new HashSet<string>(controller.parameters.Select(p => p.name));
            var existingMachines = new HashSet<string>();
            foreach (var e in cvr.SettingsList)
            {
                var m = CckAvatar.EntryMachineName(e);
                if (!string.IsNullOrEmpty(m)) existingMachines.Add(m);
            }

            int added = 0;
            var skipped = new List<string>();
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
                // Keep the AAS entry consistent with the working clothing-toggle path: the entry carries the
                // clip (on = pose, off = none) so the CCK sees a proper animation toggle, not an empty one.
                var emoteEntry = cvr.SettingsList.Cast<object>().FirstOrDefault(e => CckAvatar.EntryMachineName(e) == machine);
                if (emoteEntry != null) cvr.SetToggleClips(emoteEntry, clip, null);
                if (!existingParams.Contains(machine))
                {
                    // Off state empty (lets CVR locomotion show), On state = the emote clip. Override layer,
                    // Bool-driven — so it only poses while toggled on. (Bypasses the clothing motorbike guard
                    // on purpose: posing the body is exactly what an emote should do, but only when on.)
                    AnimatorUtil.AddBoolToggleLayer(controller, "CVRFury Emote: " + name, machine, null, clip, false);
                    existingParams.Add(machine);
                    added++;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            cvr.Persist();
            var msg = $"Added {added} emote toggle(s). Each plays its clip while toggled ON and returns to normal " +
                   "movement when OFF. Toggle one at a time (two emotes on at once will fight).";
            if (skipped.Count > 0)
                msg += $"\nSkipped {skipped.Count} locomotion/system clip(s) that would pose the body at rest " +
                       $"(the motorbike pose): {string.Join(", ", skipped.Take(20))}{(skipped.Count > 20 ? " …" : "")}." +
                       "\nIf one of these is actually a pose you want, rename it without the locomotion word and re-run.";
            return msg;
        }

        // --- helpers ---
        private void RunAndRefresh(System.Func<string> action)
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
        }

        private static bool Foldout(bool state, string label)
        {
            EditorGUILayout.Space(2);
            return EditorGUILayout.Foldout(state, label, true, EditorStyles.foldoutHeader);
        }
    }
}
