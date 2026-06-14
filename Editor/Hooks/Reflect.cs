using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Tiny, defensive reflection helpers used by the CCK integration layer. Everything
    /// returns gracefully (null / false) and logs a single clear warning rather than
    /// throwing, so a renamed CCK member degrades to "feature skipped" instead of a
    /// failed upload.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private const BindingFlags AllStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>Find a type by full name across every loaded assembly. Caches nothing
        /// (called rarely, at build time) but is cheap enough for that.</summary>
        public static Type FindType(string fullName)
        {
            // Fast path: already-qualified or in mscorlib/UnityEngine.
            var direct = Type.GetType(fullName);
            if (direct != null) return direct;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
        }

        public static object GetStaticField(Type type, string field)
        {
            if (type == null) return null;
            var f = type.GetField(field, AllStatic);
            if (f == null)
            {
                Warn($"static field '{field}' on '{type.FullName}'");
                return null;
            }
            return f.GetValue(null);
        }

        public static object GetField(object instance, string field)
        {
            if (instance == null) return null;
            var f = instance.GetType().GetField(field, AllInstance);
            if (f == null)
            {
                Warn($"field '{field}' on '{instance.GetType().FullName}'");
                return null;
            }
            return f.GetValue(instance);
        }

        /// <summary>Read a property by name (e.g. the CCK entry's <c>setting</c> get-only property
        /// that resolves the per-type settings object).</summary>
        public static object GetProperty(object instance, string prop)
        {
            if (instance == null) return null;
            var p = instance.GetType().GetProperty(prop, AllInstance);
            if (p == null) { Warn($"property '{prop}' on '{instance.GetType().FullName}'"); return null; }
            try { return p.GetValue(instance); }
            catch (Exception e) { Warn($"getting property '{prop}': {e.Message}"); return null; }
        }

        /// <summary>Invoke an instance method, supporting a leading <c>ref</c> parameter (read back
        /// from the args array after the call). Returns true on success.</summary>
        public static bool InvokeMethod(object instance, string method, object[] args)
        {
            if (instance == null) return false;
            var m = instance.GetType().GetMethod(method, AllInstance);
            if (m == null) { Warn($"method '{method}' on '{instance.GetType().FullName}'"); return false; }
            try { m.Invoke(instance, args); return true; }
            catch (Exception e)
            {
                Warn($"invoking '{method}' on '{instance.GetType().FullName}': {(e.InnerException ?? e).Message}");
                return false;
            }
        }

        public static bool SetField(object instance, string field, object value)
        {
            if (instance == null) return false;
            var f = instance.GetType().GetField(field, AllInstance);
            if (f == null)
            {
                Warn($"field '{field}' on '{instance.GetType().FullName}'");
                return false;
            }
            try { f.SetValue(instance, value); return true; }
            catch (Exception e)
            {
                Warn($"setting '{field}' on '{instance.GetType().FullName}': {e.Message}");
                return false;
            }
        }

        /// <summary>Set an enum field by member name, resolving the enum type from the field itself
        /// (so we don't need the enum's full type name). Used for CCK SettingsType / ParameterType.</summary>
        public static bool SetEnumFieldByName(object instance, string field, string member)
        {
            if (instance == null) return false;
            var f = instance.GetType().GetField(field, AllInstance);
            if (f == null) { Warn($"field '{field}' on '{instance.GetType().FullName}'"); return false; }
            try
            {
                f.SetValue(instance, Enum.Parse(f.FieldType, member));
                return true;
            }
            catch
            {
                Warn($"enum member '{member}' for field '{field}' on '{instance.GetType().FullName}'");
                return false;
            }
        }

        /// <summary>Construct an instance of a CCK type via its parameterless constructor.</summary>
        public static object New(Type type)
        {
            if (type == null) return null;
            try { return Activator.CreateInstance(type); }
            catch (Exception e)
            {
                Warn($"constructing '{type.FullName}': {e.Message}");
                return null;
            }
        }

        /// <summary>Parse an enum member by name on a (possibly nested) enum type.</summary>
        public static object EnumValue(Type enumType, string memberName)
        {
            if (enumType == null || !enumType.IsEnum) return null;
            try { return Enum.Parse(enumType, memberName); }
            catch
            {
                Warn($"enum member '{memberName}' on '{enumType?.FullName}'");
                return null;
            }
        }

        /// <summary>Add a strongly-typed delegate to a UnityEvent stored as a boxed object.</summary>
        public static bool AddUnityEventListener(object unityEvent, Delegate action)
        {
            if (unityEvent == null) return false;
            var add = unityEvent.GetType().GetMethods(AllInstance)
                .FirstOrDefault(m => m.Name == "AddListener" && m.GetParameters().Length == 1);
            if (add == null)
            {
                Warn($"AddListener on '{unityEvent.GetType().FullName}'");
                return false;
            }
            try { add.Invoke(unityEvent, new object[] { action }); return true; }
            catch (Exception e)
            {
                Warn($"AddListener invoke: {e.Message}");
                return false;
            }
        }

        /// <summary>Treat a value as a non-generic <see cref="IList"/> (List&lt;T&gt; implements it).</summary>
        public static IList AsList(object value) => value as IList;

        private static readonly System.Collections.Generic.HashSet<string> _warned =
            new System.Collections.Generic.HashSet<string>();

        private static void Warn(string what)
        {
            // De-duplicate: a single missing member would otherwise log once per element
            // (hundreds of times for a big avatar). Report each unique problem once.
            if (!_warned.Add(what)) return;
            Debug.LogWarning($"[CVRFury] CCK reflection: could not resolve {what}. " +
                             "Your CCK version may differ from the one CVRFury expects — run " +
                             "Tools ▸ CVRFury ▸ Diagnose CCK Integration and update Editor/Hooks/CckNames.cs.");
        }
    }
}
