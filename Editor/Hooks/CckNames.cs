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
        /// <summary>CVRFury package version, surfaced in the conversion/build log so you can confirm at
        /// a glance which build actually ran (a stale recompile is the usual cause of "same issue").
        /// Keep in sync with package.json.</summary>
        public const string CvrFuryVersion = "0.9.20";

        // --- Build pipeline (editor) ---
        // Static UnityEvent&lt;GameObject&gt; fields that the CCK fires immediately before it
        // bundles an avatar / prop for upload. These are CVRFury's equivalent of VRChat's
        // IVRCSDKPreprocessAvatarCallback.
        public const string BuildUtilityType = "ABI.CCK.Scripts.Editor.CCK_BuildUtility";
        public const string PreAvatarBundleEvent = "PreAvatarBundleEvent";
        public const string PrePropBundleEvent = "PrePropBundleEvent";

        // --- Avatar component (runtime) ---
        // NB: CVRAvatar has NO baseController field. The animator the avatar runs lives on the
        // Advanced Avatar Settings container (avatarSettings.baseController / .animator).
        public const string AvatarType = "ABI.CCK.Components.CVRAvatar";
        public const string Avatar_OverridesField = "overrides";          // AnimatorOverrideController
        public const string Avatar_AdvancedSettings = "avatarSettings";    // CVRAdvancedAvatarSettings
        public const string Avatar_UsesAdvancedSettings = "avatarUsesAdvancedSettings"; // bool

        // --- Advanced Avatar Settings container ---
        public const string AdvancedSettingsType = "ABI.CCK.Scripts.CVRAdvancedAvatarSettings";
        public const string AdvancedSettings_List = "settings";            // List&lt;CVRAdvancedSettingsEntry&gt;
        public const string AdvancedSettings_BaseController = "baseController"; // RuntimeAnimatorController (clean base)
        public const string AdvancedSettings_Animator = "animator";        // generated AnimatorController
        public const string AdvancedSettings_Overrides = "overrides";      // generated AnimatorOverrideController
        // CRITICAL: the CCK inspector treats an avatarSettings whose `initialized` flag is false as
        // "never set up" and SILENTLY REPLACES it with a fresh, empty container the first time the
        // CVRAvatar inspector draws (CCK_CVRAvatarEditorAdvSettings.InitializeSettingsListIfNeeded →
        // CreateAvatarSettings). If CVRFury doesn't set this, every entry it added is wiped the instant
        // the avatar is selected — which is exactly why toggles vanished and the list showed empty.
        public const string AdvancedSettings_Initialized = "initialized";  // bool — must be set true
        public const string Entry_Setting = "setting";                     // property → per-type settings object
        public const string Setting_SetupAnimator = "SetupAnimator";       // generates this entry's layer/param/states

        // --- A single AAS entry ---
        // The entry holds a SettingsType discriminator plus a *typed* settings object on a
        // per-type field (toggleSettings / sliderSettings / dropDownSettings / …). There is NO
        // single "setting" field, no "isLocal", and no "usedType" on the entry itself — usedType
        // lives on the typed settings object below.
        public const string SettingsEntryType = "ABI.CCK.Scripts.CVRAdvancedSettingsEntry";
        public const string Entry_Name = "name";            // display name
        public const string Entry_MachineName = "machineName"; // animator/synced parameter name
        public const string Entry_Type = "type";            // SettingsType enum

        // Per-type settings object fields on the entry.
        public const string Entry_ToggleSettings = "toggleSettings";
        public const string Entry_SliderSettings = "sliderSettings";
        public const string Entry_DropdownSettings = "dropDownSettings";

        // SettingsType enum (nested in CVRAdvancedSettingsEntry). Resolved from the field itself
        // via Reflect.SetEnumFieldByName, so the full enum type name is informational only.
        public const string SettingsTypeEnum = "ABI.CCK.Scripts.CVRAdvancedSettingsEntry+SettingsType";
        public const string SettingsType_Toggle = "Toggle";
        public const string SettingsType_Dropdown = "Dropdown";
        public const string SettingsType_Slider = "Slider";
        public const string SettingsType_Color = "Color";
        public const string SettingsType_Joystick2D = "Joystick2D";
        public const string SettingsType_Joystick3D = "Joystick3D";
        public const string SettingsType_InputSingle = "InputSingle";
        public const string SettingsType_InputVector2 = "InputVector2";
        public const string SettingsType_InputVector3 = "InputVector3";

        // --- Typed settings classes ---
        public const string SettingToggleType = "ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectToggle";
        public const string SettingSliderType = "ABI.CCK.Scripts.CVRAdvancesAvatarSettingSlider";
        public const string SettingDropdownType = "ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectDropdown";

        public const string Setting_DefaultBool = "defaultValue";   // toggle (bool)
        public const string Setting_DefaultFloat = "defaultValue";  // slider (float)
        public const string Setting_DefaultInt = "defaultValue";    // dropdown (int)
        public const string Setting_UseAnimationClip = "useAnimationClip";

        // Animation clips a toggle / slider plays. ChilloutVR's AAS generator builds the working
        // animator layer from THESE at build time — without them a toggle does nothing in-game.
        public const string Toggle_AnimationClip = "animationClip";        // on
        public const string Toggle_OffAnimationClip = "offAnimationClip";  // off
        public const string Slider_MinAnimationClip = "minAnimationClip";  // value 0
        public const string Slider_MaxAnimationClip = "maxAnimationClip";  // value 1

        // How the synced parameter is encoded. ParameterType { Float, Int, Bool }. This is the
        // synced-bit cost driver: a toggle left as Float costs many bits; as Bool it costs ~1.
        // Resolved from the field itself via Reflect.SetEnumFieldByName.
        public const string Setting_UsedType = "usedType";
        public const string ParameterType_Bool = "Bool";
        public const string ParameterType_Int = "Int";
        public const string ParameterType_Float = "Float";

        // GameObject targets a toggle / dropdown option drives directly (CVR-native, no animator).
        public const string Setting_GameObjectTargets = "gameObjectTargets";
        public const string TargetEntryType = "ABI.CCK.Scripts.CVRAdvancedSettingsTargetEntryGameObject";
        public const string Target_TreePath = "treePath";
        public const string Target_OnState = "onState";
        public const string Target_GameObject = "gameObject";

        // Dropdown options. Each option is a CVRAdvancedSettingsDropDownEntry with a display name.
        public const string Setting_DropdownOptions = "options";
        public const string DropdownOptionType = "ABI.CCK.Scripts.CVRAdvancedSettingsDropDownEntry";
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
