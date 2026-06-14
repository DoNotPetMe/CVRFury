using System;
using System.Collections;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
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

        /// <summary>Find the CVRAvatar, adding one if the object doesn't have it yet. Returns null
        /// only if the CCK isn't installed. Used by the VRChat converter.</summary>
        public static CckAvatar EnsureOn(GameObject root)
        {
            var existing = FindOn(root);
            if (existing != null) return existing;

            var type = Reflect.FindType(CckNames.AvatarType);
            if (type == null) return null;
            var comp = root.AddComponent(type);
            return comp != null ? new CckAvatar(comp) : null;
        }

        /// <summary>Ensure Advanced Avatar Settings are enabled and have a live container + list, so
        /// converters can append entries even on a freshly-added CVRAvatar.</summary>
        public void EnsureAdvancedSettingsContainer()
        {
            EnableAdvancedSettings();

            var aas = AdvancedSettings;
            if (aas == null)
            {
                aas = Reflect.New(Reflect.FindType(CckNames.AdvancedSettingsType));
                if (aas != null) Reflect.SetField(Component, CckNames.Avatar_AdvancedSettings, aas);
            }
            if (aas == null) return;

            if (Reflect.GetField(aas, CckNames.AdvancedSettings_List) == null)
            {
                var entryType = Reflect.FindType(CckNames.SettingsEntryType);
                if (entryType == null) return;
                var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(entryType);
                var newList = Reflect.New(listType);
                if (newList != null) Reflect.SetField(aas, CckNames.AdvancedSettings_List, newList);
            }
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
                return aas != null
                    ? Reflect.GetField(aas, CckNames.AdvancedSettings_BaseController) as AnimatorController
                    : null;
            }
            set
            {
                // CVRAvatar has no baseController; the controller lives on the AAS container only.
                EnsureAdvancedSettingsContainer();
                var aas = AdvancedSettings;
                if (aas != null) Reflect.SetField(aas, CckNames.AdvancedSettings_BaseController, value);
            }
        }

        /// <summary>Force Unity to serialize the changes CVRFury made to the CVRAvatar by reflection.
        /// The AAS list, animators and overrides are mutated directly on the live component (not through
        /// a SerializedObject), so Unity only persists them — to the scene file and to the upload clone —
        /// once the object is marked dirty. Without this the entries can read back correctly in-memory
        /// yet show as an empty list after a domain reload, and toggles upload as if unconfigured.</summary>
        public void Persist()
        {
            EditorUtility.SetDirty(Component);
            if (PrefabUtility.IsPartOfPrefabInstance(Component))
                PrefabUtility.RecordPrefabInstancePropertyModifications(Component);
            if (!Application.isPlaying && Root.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(Root.scene);
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

        /// <summary>Register an on/off toggle that appears in the in-game Advanced Settings menu
        /// and drives an animator parameter of the same machine name. Encoded as a <c>Bool</c>
        /// parameter so it costs ~1 synced bit (a Float toggle is what blows the 3200-bit budget).
        /// When <paramref name="onClip"/> is supplied, ChilloutVR's AAS generator builds the working
        /// animator layer from it (and <paramref name="offClip"/>) at build time — without a clip the
        /// generated toggle does nothing in-game.</summary>
        public bool AddToggle(string displayName, string machineName, bool defaultOn, bool isLocal,
                              AnimationClip onClip = null, AnimationClip offClip = null)
        {
            return AddEntry(
                displayName, machineName,
                CckNames.SettingsType_Toggle, CckNames.Entry_ToggleSettings, CckNames.SettingToggleType,
                setting =>
                {
                    Reflect.SetField(setting, CckNames.Setting_DefaultBool, defaultOn);
                    Reflect.SetEnumFieldByName(setting, CckNames.Setting_UsedType, CckNames.ParameterType_Bool);
                    if (onClip != null)
                    {
                        Reflect.SetField(setting, CckNames.Setting_UseAnimationClip, true);
                        Reflect.SetField(setting, CckNames.Toggle_AnimationClip, onClip);
                        if (offClip != null) Reflect.SetField(setting, CckNames.Toggle_OffAnimationClip, offClip);
                    }
                });
        }

        /// <summary>Register a 0..1 slider (radial) menu control. Sliders are inherently continuous,
        /// so the parameter is encoded as a <c>Float</c>. When min/max clips are supplied, CVR's AAS
        /// generator builds the blend layer from them.</summary>
        public bool AddSlider(string displayName, string machineName, float defaultValue, bool isLocal,
                              AnimationClip minClip = null, AnimationClip maxClip = null)
        {
            return AddEntry(
                displayName, machineName,
                CckNames.SettingsType_Slider, CckNames.Entry_SliderSettings, CckNames.SettingSliderType,
                setting =>
                {
                    Reflect.SetField(setting, CckNames.Setting_DefaultFloat, defaultValue);
                    Reflect.SetEnumFieldByName(setting, CckNames.Setting_UsedType, CckNames.ParameterType_Float);
                    if (minClip != null && maxClip != null)
                    {
                        Reflect.SetField(setting, CckNames.Setting_UseAnimationClip, true);
                        Reflect.SetField(setting, CckNames.Slider_MinAnimationClip, minClip);
                        Reflect.SetField(setting, CckNames.Slider_MaxAnimationClip, maxClip);
                    }
                });
        }

        /// <summary>Register a dropdown (exclusive multi-option) menu control. Encoded as an
        /// <c>Int</c> parameter (cheap: a handful of bits regardless of option count).</summary>
        public bool AddDropdown(string displayName, string machineName, string[] optionNames,
                                int defaultIndex, bool isLocal)
        {
            return AddEntry(
                displayName, machineName,
                CckNames.SettingsType_Dropdown, CckNames.Entry_DropdownSettings, CckNames.SettingDropdownType,
                setting =>
                {
                    Reflect.SetField(setting, CckNames.Setting_DefaultInt, defaultIndex);
                    Reflect.SetEnumFieldByName(setting, CckNames.Setting_UsedType, CckNames.ParameterType_Int);

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

        /// <summary>
        /// Read back every AAS entry and report how each synced parameter is encoded (Bool/Int/Float),
        /// with an estimated synced-bit total. This is the ground truth for the "over the Synced Bit
        /// Limit" problem: if toggles show up as Float here, the usedType write didn't take effect
        /// (most likely the package didn't recompile) — a fleet of Float toggles is what blows the cap.
        /// </summary>
        public string SummarizeSyncCost()
        {
            var list = SettingsList;
            if (list == null) return "AAS list unavailable — cannot summarise sync cost.";

            int boolN = 0, intN = 0, floatN = 0, unknownN = 0, localN = 0, estBits = 0;

            foreach (var entry in list)
            {
                if (entry == null) continue;

                // ChilloutVR does not sync parameters whose machine name starts with '#'.
                var machine = Reflect.GetField(entry, CckNames.Entry_MachineName) as string;
                if (!string.IsNullOrEmpty(machine) && machine[0] == '#') { localN++; continue; }

                var setting =
                    Reflect.GetField(entry, CckNames.Entry_ToggleSettings) ??
                    Reflect.GetField(entry, CckNames.Entry_SliderSettings) ??
                    Reflect.GetField(entry, CckNames.Entry_DropdownSettings);

                var used = setting == null ? null : Reflect.GetField(setting, CckNames.Setting_UsedType)?.ToString();
                switch (used)
                {
                    case CckNames.ParameterType_Bool:  boolN++;  estBits += 1;  break;
                    case CckNames.ParameterType_Int:   intN++;   estBits += 8;  break;
                    case CckNames.ParameterType_Float: floatN++; estBits += 32; break;
                    default:                           unknownN++; estBits += 32; break; // unset defaults to Float
                }
            }

            return $"AAS readback: {boolN} Bool, {intN} Int, {floatN} Float synced" +
                   (unknownN > 0 ? $", {unknownN} UNSET(→Float)" : "") +
                   $", {localN} local(#) — est. ~{estBits} synced bits from AAS (CVR cap 3200). " +
                   "Note: CVR's own counter also sums non-#-prefixed parameters in the base controller, " +
                   "so localising (#) the merged FX parameters is what actually clears the limit.";
        }

        // ------------------------------------------------------------------ spatial / face

        public void SetViewPosition(Vector3 v) => Reflect.SetField(Component, CckNames.Avatar_ViewPosition, v);
        public void SetVoicePosition(Vector3 v) => Reflect.SetField(Component, CckNames.Avatar_VoicePosition, v);
        public void SetFaceMesh(SkinnedMeshRenderer m) => Reflect.SetField(Component, CckNames.Avatar_FaceMesh, m);
        public void SetUseBlink(bool b) => Reflect.SetField(Component, CckNames.Avatar_UseBlinkBlendshapes, b);
        public void SetUseVisemes(bool b) => Reflect.SetField(Component, CckNames.Avatar_UseVisemeLipsync, b);
        public void SetUseEyeMovement(bool b) => Reflect.SetField(Component, CckNames.Avatar_UseEyeMovement, b);

        private bool AddEntry(string displayName, string machineName,
                              string settingsTypeEnumMember, string settingFieldName, string settingClassName,
                              Action<object> configureSetting)
        {
            EnsureAdvancedSettingsContainer();

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
            Reflect.SetEnumFieldByName(entry, CckNames.Entry_Type, settingsTypeEnumMember);

            // Build the typed settings object and attach it to its per-type field on the entry
            // (toggleSettings / sliderSettings / dropDownSettings). The setting carries usedType,
            // which determines synced-bit cost.
            var setting = Reflect.New(Reflect.FindType(settingClassName));
            if (setting != null)
            {
                configureSetting?.Invoke(setting);
                Reflect.SetField(entry, settingFieldName, setting);
            }

            list.Add(entry);
            return true;
        }
    }
}
