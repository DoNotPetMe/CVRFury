using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace CVRFury.Builder
{
    /// <summary>
    /// Discovers the ChilloutVR CCK's avatar/prop "pre-bundle" events at runtime instead of relying
    /// solely on a hard-coded type/field name. CVR has renamed and relocated these between CCK
    /// generations (e.g. the move to <c>CVR.CCK</c> / the ContentUploader), so CVRFury looks for any
    /// static <see cref="UnityEvent{T}"/> that accepts a <see cref="GameObject"/> and classifies it
    /// by name. This keeps the build hook working across versions and powers the diagnostic command.
    /// </summary>
    internal static class CckProbe
    {
        public readonly struct BundleEvent
        {
            public readonly string TypeName;
            public readonly string MemberName;
            public readonly object EventInstance;
            public readonly bool IsAvatar;
            public readonly bool IsProp;

            public BundleEvent(string typeName, string memberName, object instance, bool isAvatar, bool isProp)
            {
                TypeName = typeName;
                MemberName = memberName;
                EventInstance = instance;
                IsAvatar = isAvatar;
                IsProp = isProp;
            }
        }

        private const BindingFlags StaticMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Find the CCK pre-bundle events. Tries the known build-utility type first (fast, exact),
        /// then falls back to a broad scan of CCK-looking assemblies.
        /// </summary>
        public static List<BundleEvent> Discover(bool broadScan = false)
        {
            var results = new List<BundleEvent>();

            var known = Reflect.FindType(CckNames.BuildUtilityType);
            if (known != null)
                ScanType(known, results);

            if (results.Count == 0 || broadScan)
            {
                foreach (var type in EnumerateCandidateTypes())
                {
                    if (known != null && type == known) continue;
                    ScanType(type, results);
                }
            }

            return results;
        }

        private static void ScanType(Type type, List<BundleEvent> results)
        {
            FieldInfo[] fields;
            try { fields = type.GetFields(StaticMembers); }
            catch { return; }

            foreach (var f in fields)
            {
                if (!AcceptsGameObject(f.FieldType)) continue;
                object instance;
                try { instance = f.GetValue(null); }
                catch { continue; }
                results.Add(Classify(type.FullName, f.Name, instance));
            }
        }

        /// <summary>True if the type is a UnityEvent whose AddListener takes a UnityAction&lt;GameObject&gt;.</summary>
        private static bool AcceptsGameObject(Type eventType)
        {
            if (eventType == null || !typeof(UnityEventBase).IsAssignableFrom(eventType)) return false;
            foreach (var m in eventType.GetMethods())
            {
                if (m.Name != "AddListener") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(UnityAction<GameObject>))
                    return true;
            }
            return false;
        }

        private static BundleEvent Classify(string typeName, string memberName, object instance)
        {
            var lower = memberName.ToLowerInvariant();
            var isProp = lower.Contains("prop") || lower.Contains("spawnable");
            // Anything bundle/build-related that isn't a prop is treated as the avatar event.
            var isAvatar = !isProp && (lower.Contains("avatar") || lower.Contains("bundle") || lower.Contains("build"));
            return new BundleEvent(typeName, memberName, instance, isAvatar, isProp);
        }

        private static IEnumerable<Type> EnumerateCandidateTypes()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (IsFrameworkAssembly(name)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    var full = t.FullName ?? "";
                    // Limit to CCK-looking types to keep the scan cheap and avoid false positives.
                    if (full.IndexOf("CCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        full.IndexOf("ABI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        full.IndexOf("ChilloutVR", StringComparison.OrdinalIgnoreCase) >= 0)
                        yield return t;
                }
            }
        }

        private static bool IsFrameworkAssembly(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            return name.StartsWith("System") || name.StartsWith("Unity") || name.StartsWith("UnityEngine") ||
                   name.StartsWith("UnityEditor") || name.StartsWith("mscorlib") || name.StartsWith("netstandard") ||
                   name.StartsWith("Mono.") || name.StartsWith("nunit") || name.StartsWith("Newtonsoft") ||
                   name.StartsWith("com.donotpetme.cvrfury");
        }

        private const BindingFlags AnyMember =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Statically check every CCK type / field / enum member CVRFury writes to during a bake, so
        /// the full reflection contract can be verified without doing an upload. Returns a human
        /// report for the diagnostic command.
        /// </summary>
        public static string ValidateDataModel()
        {
            var sb = new System.Text.StringBuilder();

            void Type(string label, string name)
            {
                var t = Reflect.FindType(name);
                sb.AppendLine($"  [{(t != null ? "OK" : "MISSING")}] {label}  ({name})");
            }

            void Fields(string label, string typeName, params string[] fields)
            {
                var t = Reflect.FindType(typeName);
                if (t == null)
                {
                    sb.AppendLine($"  [MISSING] {label} type not found ({typeName}) — fields unchecked");
                    return;
                }
                sb.AppendLine($"  {label} ({t.Name}) fields:");
                foreach (var f in fields)
                {
                    var ok = t.GetField(f, AnyMember) != null || t.GetProperty(f, AnyMember) != null;
                    sb.AppendLine($"     [{(ok ? "OK" : "MISSING")}] {f}");
                }
            }

            void EnumMembers(string label, string enumTypeName, params string[] members)
            {
                var t = Reflect.FindType(enumTypeName);
                if (t == null || !t.IsEnum)
                {
                    sb.AppendLine($"  [MISSING] {label} enum not found ({enumTypeName})");
                    return;
                }
                var names = new HashSet<string>(Enum.GetNames(t));
                sb.AppendLine($"  {label} ({t.Name}) members:");
                foreach (var m in members)
                    sb.AppendLine($"     [{(names.Contains(m) ? "OK" : "MISSING")}] {m}");
            }

            sb.AppendLine("--- Data model contract ---");
            Fields("CVRAvatar", CckNames.AvatarType,
                CckNames.Avatar_OverridesField, CckNames.Avatar_BaseControllerField,
                CckNames.Avatar_AdvancedSettings, CckNames.Avatar_UsesAdvancedSettings,
                CckNames.Avatar_ViewPosition, CckNames.Avatar_VoicePosition, CckNames.Avatar_FaceMesh,
                CckNames.Avatar_UseBlinkBlendshapes, CckNames.Avatar_UseVisemeLipsync,
                CckNames.Avatar_UseEyeMovement);

            Fields("AdvancedAvatarSettings", CckNames.AdvancedSettingsType,
                CckNames.AdvancedSettings_List, CckNames.AdvancedSettings_BaseController);

            Fields("SettingsEntry", CckNames.SettingsEntryType,
                CckNames.Entry_Name, CckNames.Entry_MachineName, CckNames.Entry_Type,
                CckNames.Entry_Setting, CckNames.Entry_UsedType, CckNames.Entry_IsLocal);

            EnumMembers("SettingsType", CckNames.SettingsTypeEnum,
                CckNames.SettingsType_GameObjectToggle, CckNames.SettingsType_GameObjectDropdown,
                CckNames.SettingsType_Slider, CckNames.SettingsType_MaterialColor);

            Type("Toggle setting class", CckNames.SettingToggleType);
            Type("Slider setting class", CckNames.SettingSliderType);
            Type("Dropdown setting class", CckNames.SettingDropdownType);
            Type("Dropdown option class", CckNames.DropdownOptionType);

            return sb.ToString();
        }
    }
}
