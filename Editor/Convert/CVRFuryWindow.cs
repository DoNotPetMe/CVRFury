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

        // Step 3 (physbones)
        private bool _pbColliders = true;

        private bool _s1 = true, _s2 = true, _s3, _s4;

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
            Step1Parameters();
            Step2Clips();
            Step3PhysBones();
            Step4Magica();

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
                _onSuffix = EditorGUILayout.TextField("ON  clip name ends with", _onSuffix);
                _offSuffix = EditorGUILayout.TextField("OFF clip name ends with", _offSuffix);
                _buildController = EditorGUILayout.ToggleLeft(
                    "Build & attach a controller (creates parameters → clears the red ❗)", _buildController);
                using (new EditorGUI.DisabledScope(!_buildController))
                using (new EditorGUI.IndentLevelScope())
                    _controller = (AnimatorController)EditorGUILayout.ObjectField(
                        "Controller (optional)", _controller, typeof(AnimatorController), false);

                using (new EditorGUI.DisabledScope(_avatar == null || _clipFolder == null))
                    if (GUILayout.Button("Link clips" + (_buildController ? " & build controller" : "")))
                    {
                        var folderPath = AssetDatabase.GetAssetPath(_clipFolder);
                        RunAndRefresh(() => ToggleClipLinker.LinkClips(
                            _avatar, folderPath, _onSuffix, _offSuffix, _buildController, _controller));
                    }
            }
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
                        ? "Converts VRCPhysBone/Collider to DynamicBone/Collider (approximate — tune stiffness/" +
                          "elasticity after). Colliders convert first so bones can reference them."
                        : "DynamicBone isn't in this project. Import it (the CCK bundles it) to enable this.",
                    dbPresent ? MessageType.None : MessageType.Warning);
                _pbColliders = EditorGUILayout.ToggleLeft("Also convert PhysBone colliders", _pbColliders);
                using (new EditorGUI.DisabledScope(_avatar == null || !dbPresent))
                    if (GUILayout.Button("Convert PhysBones"))
                        RunAndRefresh(ConvertPhysBones);
            }
        }

        private void Step4Magica()
        {
            _s4 = Foldout(_s4, "4 — Magica Cloth");
            if (!_s4) return;
            using (new EditorGUI.IndentLevelScope())
            {
                var magica = Reflect.FindType("MagicaCloth2.MagicaCloth") ?? Reflect.FindType("MagicaCloth.MagicaCloth");
                if (magica == null)
                {
                    EditorGUILayout.HelpBox("Magica Cloth is not installed in this project — this step is skipped. " +
                                            "Install Magica Cloth 2 to enable it.", MessageType.None);
                    return;
                }
                EditorGUILayout.HelpBox(
                    "Magica Cloth 2 detected. PhysBone → Magica conversion with options is the next feature being " +
                    "built. For now, use step 3 (DynamicBones) for chains. This section will gain Magica options shortly.",
                    MessageType.Info);
            }
        }

        private string ConvertPhysBones()
        {
            var assets = AssetSaver.CreatePersistent(_avatar.name);
            var opts = new ConversionOptions { physBones = true, physBoneColliders = _pbColliders };
            var ctx = new ConversionContext(_avatar, opts, assets);
            new PhysBoneConverter().Run(ctx);
            EditorUtility.SetDirty(_avatar);
            var lines = ctx.Log.Entries.Select(e => e.Message);
            return string.Join("\n", lines);
        }

        // --- helpers ---
        private void RunAndRefresh(System.Func<string> action)
        {
            string result;
            try { result = action(); }
            catch (System.Exception ex) { result = "Error: " + ex.Message; Debug.LogException(ex); }
            _log = result ?? "";
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
