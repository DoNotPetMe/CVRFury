using System;
using System.Collections;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// A reflection-backed facade over <c>ABI.CCK.Components.CVRAvatar</c> and its Advanced
    /// Avatar Settings (AAS). All CCK-specific knowledge lives behind this class so feature
    /// builders can speak in plain terms ("add a synced toggle named X").
    /// </summary>
    internal sealed class CckAvatar
    {
        public readonly Component Component;   // the CVRAvatar MonoBehaviour
        public readonly GameObject Root;

        private CckAvatar(Component component)
        {
            Component = component;
            Root = component.gameObject;
        }

        /// <summary>Find the CVRAvatar on a build root, or null if the CCK isn't present /
        /// this object isn't a CVR avatar.</summary>
        public static CckAvatar FindOn(GameObject root)
        {
            var type = Reflect.FindType(CckNames.AvatarType);
            if (type == null) return null;
            var comp = root.GetComponent(type);
            return comp != null ? new CckAvatar(comp) : null;
        }

        // ------------------------------------------------------------------ animators

        public AnimatorOverrideController Overrides
        {
            get => Reflect.GetField(Component, CckNames.Avatar_OverridesField) as AnimatorOverrideController;
            set => Reflect.SetField(Component, CckNames.Avatar_OverridesField, value);
        }

        /// <summary>The AAS container object (CVRAdvancedAvatarSettings), or null.</summary>
        public object AdvancedSettings => Reflect.GetField(Component, CckNames.Avatar_AdvancedSettings);

        public void EnableAdvancedSettings() =>
            Reflect.SetField(Component, CckNames.Avatar_UsesAdvancedSettings, true);

        /// <summary>The AnimatorController the AAS uses as its base — the place CVRFury adds
        /// its generated layers and parameters. May be null if not configured yet.</summary>
        public AnimatorController BaseController
        {
            get
            {
                var aas = AdvancedSettings;
                if (aas != null)
                {
                    var c = Reflect.GetField(aas, CckNames.AdvancedSettings_BaseController) as AnimatorController;
                    if (c != null) return c;
                }
                return Reflect.GetField(Component, CckNames.Avatar_BaseControllerField) as AnimatorController;
            }
            set
            {
                var aas = AdvancedSettings;
                if (aas != null) Reflect.SetField(aas, CckNames.AdvancedSettings_BaseController, value);
                Reflect.SetField(Component, CckNames.Avatar_BaseControllerField, value);
            }
        }

        // ------------------------------------------------------------------ AAS entries

        /// <summary>The live <c>List&lt;CVRAdvancedSettingsEntry&gt;</c>, as a non-generic IList.</summary>
        public IList SettingsList
        {
            get
            {
                var aas = AdvancedSettings;
                if (aas == null) return null;
                return Reflect.AsList(Reflect.GetField(aas, CckNames.AdvancedSettings_List));
            }
        }

        /// <summary>Register a synced on/off toggle that appears in the in-game Advanced
        /// Settings menu and drives an animator parameter of the same machine name.</summary>
        public bool AddToggle(string displayName, string machineName, bool defaultOn, bool isLocal)
        {
            return AddEntry(
                displayName, machineName, isLocal,
                CckNames.SettingsType_GameObjectToggle, CckNames.SettingToggleType,
                setting => Reflect.SetField(setting, CckNames.Setting_DefaultBool, defaultOn));
        }

        /// <summary>Register a synced 0..1 slider (radial) menu control.</summary>
        public bool AddSlider(string displayName, string machineName, float defaultValue, bool isLocal)
        {
            return AddEntry(
                displayName, machineName, isLocal,
                CckNames.SettingsType_Slider, CckNames.SettingSliderType,
                setting => Reflect.SetField(setting, CckNames.Setting_DefaultFloat, defaultValue));
        }

        /// <summary>Register a synced dropdown (exclusive multi-option) menu control.</summary>
        public bool AddDropdown(string displayName, string machineName, string[] optionNames,
                                int defaultIndex, bool isLocal)
        {
            return AddEntry(
                displayName, machineName, isLocal,
                CckNames.SettingsType_GameObjectDropdown, CckNames.SettingDropdownType,
                setting =>
                {
                    Reflect.SetField(setting, CckNames.Setting_DefaultInt, defaultIndex);

                    var optionType = Reflect.FindType(CckNames.DropdownOptionType);
                    var list = Reflect.AsList(Reflect.GetField(setting, CckNames.Setting_DropdownOptions));
                    if (optionType != null && list != null)
                    {
                        foreach (var name in optionNames)
                        {
                            var opt = Reflect.New(optionType);
                            if (opt == null) continue;
                            Reflect.SetField(opt, CckNames.DropdownOption_Name, name);
                            list.Add(opt);
                        }
                    }
                });
        }

        // ------------------------------------------------------------------ spatial / face

        public void SetViewPosition(Vector3 v) => Reflect.SetField(Component, CckNames.Avatar_ViewPosition, v);
        public void SetVoicePosition(Vector3 v) => Reflect.SetField(Component, CckNames.Avatar_VoicePosition, v);
        public void SetFaceMesh(SkinnedMeshRenderer m) => Reflect.SetField(Component, CckNames.Avatar_FaceMesh, m);
        public void SetUseBlink(bool b) => Reflect.SetField(Component, CckNames.Avatar_UseBlinkBlendshapes, b);
        public void SetUseVisemes(bool b) => Reflect.SetField(Component, CckNames.Avatar_UseVisemeLipsync, b);
        public void SetUseEyeMovement(bool b) => Reflect.SetField(Component, CckNames.Avatar_UseEyeMovement, b);

        private bool AddEntry(string displayName, string machineName, bool isLocal,
                              string settingsTypeEnumMember, string settingClassName,
                              Action<object> configureSetting)
        {
            var list = SettingsList;
            if (list == null)
            {
                Debug.LogWarning($"[CVRFury] Could not access the AAS list; skipped menu entry '{displayName}'.");
                return false;
            }

            var entryType = Reflect.FindType(CckNames.SettingsEntryType);
            var entry = Reflect.New(entryType);
            if (entry == null) return false;

            Reflect.SetField(entry, CckNames.Entry_Name, displayName);
            Reflect.SetField(entry, CckNames.Entry_MachineName, machineName);
            Reflect.SetField(entry, CckNames.Entry_IsLocal, isLocal);

            var enumType = Reflect.FindType(CckNames.SettingsTypeEnum);
            var enumVal = Reflect.EnumValue(enumType, settingsTypeEnumMember);
            if (enumVal != null)
            {
                Reflect.SetField(entry, CckNames.Entry_Type, enumVal);
                Reflect.SetField(entry, CckNames.Entry_UsedType, enumVal);
            }

            var setting = Reflect.New(Reflect.FindType(settingClassName));
            if (setting != null)
            {
                configureSetting?.Invoke(setting);
                Reflect.SetField(entry, CckNames.Entry_Setting, setting);
            }

            list.Add(entry);
            return true;
        }
    }
}
