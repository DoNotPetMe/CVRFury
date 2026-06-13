using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// The VRChat → ChilloutVR conversion window. Pick the avatar, choose how aggressive to be, and
    /// convert. Easy steps are on by default; the harder, approximate steps are opt-in.
    /// </summary>
    public class ConverterWindow : EditorWindow
    {
        private readonly ConversionOptions _options = new ConversionOptions();
        private Vector2 _scroll;

        [MenuItem("Tools/CVRFury/VRChat → ChilloutVR Converter", false, 0)]
        public static void Open()
        {
            var w = GetWindow<ConverterWindow>("VRChat → CVR");
            w.minSize = new Vector2(380, 460);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("VRChat → ChilloutVR Converter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Converts a VRChat avatar to a ChilloutVR-ready one.\n\n" +
                "Both SDKs must be present and compiling in THIS project at once: import the VRChat " +
                "Avatars SDK alongside the CCK (or add the CCK to your VRChat project). CVRFury reads " +
                "the VRChat components and writes the CVR equivalents, then you remove the VRChat SDK " +
                "and upload.\n\n" +
                "Gotcha: the CCK and VRChat SDK target different Unity versions — if they don't both " +
                "compile cleanly, their types won't load and conversion can't run.",
                MessageType.Info);

            var sdkPresent = Reflect.FindType(VrcNames.AvatarDescriptorType) != null;
            var dbPresent = Reflect.FindType(VrcNames.DynamicBoneType) != null;
            EditorGUILayout.LabelField("VRChat SDK:", sdkPresent ? "Found" : "NOT found");
            EditorGUILayout.LabelField("DynamicBone:", dbPresent ? "Found" : "NOT found (needed for PhysBones)");

            var target = Selection.activeGameObject;
            EditorGUILayout.ObjectField("Avatar", target, typeof(GameObject), true);

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Always safe", EditorStyles.boldLabel);
            _options.avatarBasics = EditorGUILayout.ToggleLeft(
                "Avatar basics (viewpoint, visemes, blink, eyes)", _options.avatarBasics);
            _options.stripVrcAndBroken = EditorGUILayout.ToggleLeft(
                "Strip VRChat + broken components when done", _options.stripVrcAndBroken);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Automatic (approximate)", EditorStyles.boldLabel);
                if (GUILayout.Button("Enable all", GUILayout.Width(80))) _options.EnableAllAutomatic();
            }
            _options.physBones = EditorGUILayout.ToggleLeft("PhysBones → DynamicBones", _options.physBones);
            _options.physBoneColliders = EditorGUILayout.ToggleLeft("PhysBone colliders → DynamicBone colliders", _options.physBoneColliders);
            _options.expressions = EditorGUILayout.ToggleLeft("Expression menu + parameters → Advanced Avatar Settings", _options.expressions);
            _options.mergePlayableLayers = EditorGUILayout.ToggleLeft("Merge playable layers (FX/Gesture/Action) into the CVR animator", _options.mergePlayableLayers);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Work on a COPY of your avatar — conversion edits the object in place and deletes the " +
                "VRChat components. Generated controllers are saved under Assets/CVRFury Converted/.",
                MessageType.Warning);

            using (new EditorGUI.DisabledScope(target == null || !sdkPresent))
            {
                if (GUILayout.Button("Convert", GUILayout.Height(32)))
                    DoConvert(target);
            }

            if (!sdkPresent)
                EditorGUILayout.HelpBox("Import the VRChat Avatars SDK to enable conversion.", MessageType.Error);
        }

        private void DoConvert(GameObject target)
        {
            if (target == null) return;
            if (!EditorUtility.DisplayDialog("CVRFury",
                    $"Convert '{target.name}' to ChilloutVR? This edits the object in place. " +
                    "Make sure it's a copy you're happy to modify.",
                    "Convert", "Cancel"))
                return;

            Undo.RegisterFullObjectHierarchyUndo(target, "CVRFury Convert VRChat Avatar");
            var log = VRChatConverter.Convert(target, _options);
            EditorUtility.SetDirty(target);

            EditorUtility.DisplayDialog("CVRFury",
                log.HasErrors
                    ? "Conversion finished with errors — see the CVRFury Build Log window."
                    : "Conversion finished. Review the avatar and the CVRFury Build Log window.",
                "OK");
        }
    }
}
