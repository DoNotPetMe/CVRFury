namespace CVRFury.Builder
{
    /// <summary>
    /// Every CCK type and member name CVRFury touches by reflection lives here, and
    /// nowhere else. The CCK ships inside <c>Assembly-CSharp</c> with no asmdef, so a
    /// distributable package cannot reference it at compile time — reflection is the only
    /// robust option (this is exactly how lilToon and NDMF integrate with ChilloutVR).
    ///
    /// If a future CCK release renames a member, update the single constant here; CVRFury
    /// surfaces a clear diagnostic and skips the affected feature rather than corrupting
    /// an upload.
    ///
    /// Authoritative reference for these names/behaviours: the official ChilloutVR CCK docs
    /// at https://docs.chilloutvr.net/cck/ (the CVRAvatar component + Advanced Avatar Settings
    /// pages). When the docs and a real CCK install disagree, the install wins — verify with
    /// Tools ▸ CVRFury ▸ Diagnose CCK Integration, which reads the actual loaded assemblies.
    /// </summary>
    internal static class CckNames
    {
        // --- Build pipeline (editor) ---
        // Static UnityEvent&lt;GameObject&gt; fields that the CCK fires immediately before it
        // bundles an avatar / prop for upload. These are CVRFury's equivalent of VRChat's
        // IVRCSDKPreprocessAvatarCallback.
        public const string BuildUtilityType = "ABI.CCK.Scripts.Editor.CCK_BuildUtility";
        public const string PreAvatarBundleEvent = "PreAvatarBundleEvent";
        public const string PrePropBundleEvent = "PrePropBundleEvent";

        // --- Avatar component (runtime) ---
        public const string AvatarType = "ABI.CCK.Components.CVRAvatar";
        public const string Avatar_OverridesField = "overrides";          // AnimatorOverrideController
        public const string Avatar_BaseControllerField = "baseController"; // RuntimeAnimatorController (some CCK versions)
        public const string Avatar_AdvancedSettings = "avatarSettings";    // CVRAdvancedAvatarSettings
        public const string Avatar_UsesAdvancedSettings = "avatarUsesAdvancedSettings"; // bool

        // --- Advanced Avatar Settings container ---
        public const string AdvancedSettingsType = "ABI.CCK.Scripts.CVRAdvancedAvatarSettings";
        public const string AdvancedSettings_List = "settings";            // List&lt;CVRAdvancedSettingsEntry&gt;
        public const string AdvancedSettings_BaseController = "baseController";

        // --- A single AAS entry ---
        public const string SettingsEntryType = "ABI.CCK.Scripts.CVRAdvancedSettingsEntry";
        public const string Entry_Name = "name";            // display name
        public const string Entry_MachineName = "machineName"; // animator/synced parameter name
        public const string Entry_Type = "type";            // SettingsType enum
        public const string Entry_Setting = "setting";       // typed CVRAdvancesAvatarSettingBase
        public const string Entry_UsedType = "usedType";
        public const string Entry_IsLocal = "isLocal";

        // SettingsType enum (nested in CVRAdvancedSettingsEntry on most CCK versions).
        public const string SettingsTypeEnum = "ABI.CCK.Scripts.CVRAdvancedSettingsEntry+SettingsType";
        public const string SettingsType_GameObjectToggle = "GameObjectToggle";
        public const string SettingsType_GameObjectDropdown = "GameObjectDropdown";
        public const string SettingsType_Slider = "Slider";
        public const string SettingsType_InputSingle = "InputSingle";
        public const string SettingsType_InputVector2 = "InputVector2";
        public const string SettingsType_InputVector3 = "InputVector3";
        public const string SettingsType_Joystick2D = "Joystick2D";
        public const string SettingsType_Joystick3D = "Joystick3D";
        public const string SettingsType_MaterialColor = "MaterialColor";

        // --- Typed settings classes ---
        public const string SettingToggleType = "ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectToggle";
        public const string SettingSliderType = "ABI.CCK.Scripts.CVRAdvancesAvatarSettingSlider";
        public const string SettingDropdownType = "ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectDropdown";

        public const string Setting_DefaultBool = "defaultValue";   // toggle (bool)
        public const string Setting_DefaultFloat = "defaultValue";  // slider (float)
        public const string Setting_DefaultInt = "defaultValue";    // dropdown (int)
        public const string Setting_UseAnimationClip = "useAnimationClip";

        // Dropdown options. Each option is a CVRAdvancedSettingsDropDownOption with a display name.
        public const string Setting_DropdownOptions = "options";
        public const string DropdownOptionType = "ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectDropdownOption";
        public const string DropdownOption_Name = "name";

        // --- Built-in gesture parameters driven by the platform ---
        // ChilloutVR feeds hand gestures into the animator through these float parameters,
        // mirroring VRChat's GestureLeft / GestureRight convention (0 = neutral … 7 = thumbs up).
        public const string GestureLeftParam = "GestureLeft";
        public const string GestureRightParam = "GestureRight";

        // --- Viseme / blink / eye / spatial fields on CVRAvatar ---
        public const string Avatar_ViewPosition = "viewPosition";   // Vector3
        public const string Avatar_VoicePosition = "voicePosition"; // Vector3
        public const string Avatar_FaceMesh = "bodyMesh";           // SkinnedMeshRenderer used for visemes/blink
        public const string Avatar_UseBlinkBlendshapes = "useBlinkBlendshapes"; // bool
        public const string Avatar_UseVisemeLipsync = "useVisemeLipsync";       // bool
        public const string Avatar_UseEyeMovement = "useEyeMovement";           // bool
    }
}
