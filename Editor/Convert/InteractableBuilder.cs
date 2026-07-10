using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Rebuilds simple Udon interactions as ready-made <c>CVRInteractable</c>s: a use-button that toggles
    /// GameObjects becomes an interactable with a set-active operation over the SAME target objects that were
    /// read out of the Udon behaviour's public variables.
    ///
    /// The CVRInteractable action graph (action list → trigger enum → operation list → operation type +
    /// targets) is built by SHAPE, not by hard-coded member names: lists are found by their element-type
    /// name, enums by fuzzy member matching, targets by field type. That keeps it working across CCK
    /// revisions whose exact field names drift. When a shape can't be found the component is still added and
    /// the failure message includes the real member layout of the CCK types, so the wiring can be tightened
    /// for that CCK version from a single report.
    /// </summary>
    internal static class InteractableBuilder
    {
        private const string InteractableType = "ABI.CCK.Components.CVRInteractable";

        public static bool Available => Reflect.FindType(InteractableType) != null;

        /// <summary>Adds a CVRInteractable that toggles <paramref name="targets"/> on use. Returns true when
        /// the action graph was fully wired; false (with a reason) when only the component could be placed.</summary>
        public static bool AddToggle(GameObject on, List<GameObject> targets, out string detail)
        {
            detail = "";
            var t = Reflect.FindType(InteractableType);
            if (t == null) { detail = "CCK CVRInteractable type not loaded."; return false; }

            var comp = on.GetComponent(t);
            if (comp == null) comp = Undo.AddComponent(on, t); // '??' breaks on Unity's fake-null

            try
            {
                // actions list: first field on CVRInteractable that's a List<T> with "Action" in T's name.
                var actionsField = ListField(t, elem => elem.Name.Contains("Action"));
                if (actionsField == null) { detail = "no action-list field: " + DumpMembers(t); return false; }
                var actionType = actionsField.FieldType.GetGenericArguments()[0];
                var actions = EnsureList(comp, actionsField);

                var action = Activator.CreateInstance(actionType);
                if (!SetEnumFuzzy(action, new[] { "oninteractdown", "interactdown", "oninteract", "interact", "onuse" }))
                { detail = "no trigger enum on " + actionType.Name + ": " + DumpMembers(actionType); return false; }

                // operations list on the action; the operation carries the op-type enum + the targets.
                var opsField = ListField(actionType, elem => elem.Name.Contains("Operation"));
                if (opsField == null) { detail = "no operation-list field: " + DumpMembers(actionType); return false; }
                var opType = opsField.FieldType.GetGenericArguments()[0];
                var ops = EnsureList(action, opsField);

                var op = Activator.CreateInstance(opType);
                if (!SetEnumFuzzy(op, new[] { "setgameobjectactive", "gameobjectactive", "togglegameobject",
                                              "toggleactive", "setactive", "toggle", "enable" }))
                { detail = "no set-active op on " + opType.Name + ": " + DumpMembers(opType); return false; }

                if (!SetTargets(op, targets))
                { detail = "no target slot on " + opType.Name + ": " + DumpMembers(opType); return false; }

                ops.Add(op);
                actions.Add(action);
                EditorUtility.SetDirty(comp);
                detail = $"toggles {string.Join(", ", targets.Select(g => g.name))}";
                return true;
            }
            catch (Exception ex)
            {
                detail = "wiring failed: " + ex.Message;
                return false;
            }
        }

        // --- shape-based plumbing ------------------------------------------------------------------

        private static FieldInfo ListField(Type owner, Func<Type, bool> elementMatch) =>
            owner.GetFields(BindingFlags.Public | BindingFlags.Instance)
                 .FirstOrDefault(f => f.FieldType.IsGenericType &&
                                      f.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                                      elementMatch(f.FieldType.GetGenericArguments()[0]));

        private static IList EnsureList(object owner, FieldInfo listField)
        {
            var list = listField.GetValue(owner) as IList;
            if (list == null)
            {
                list = (IList)Activator.CreateInstance(listField.FieldType);
                listField.SetValue(owner, list);
            }
            return list;
        }

        /// <summary>Finds the first enum field on the object with a member fuzzily matching any candidate
        /// (case/underscore-insensitive contains) and sets it. Candidates are ordered most→least specific.</summary>
        private static bool SetEnumFuzzy(object obj, string[] candidates)
        {
            foreach (var f in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.FieldType.IsEnum) continue;
                var names = Enum.GetNames(f.FieldType);
                foreach (var want in candidates)
                {
                    var hit = names.FirstOrDefault(n => n.ToLowerInvariant().Replace("_", "").Contains(want));
                    if (hit == null) continue;
                    f.SetValue(obj, Enum.Parse(f.FieldType, hit));
                    return true;
                }
            }
            return false;
        }

        private static bool SetTargets(object op, List<GameObject> targets)
        {
            var t = op.GetType();
            var listGo = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                          .FirstOrDefault(f => f.FieldType == typeof(List<GameObject>));
            if (listGo != null) { listGo.SetValue(op, new List<GameObject>(targets)); return true; }

            var arrGo = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                         .FirstOrDefault(f => f.FieldType == typeof(GameObject[]));
            if (arrGo != null) { arrGo.SetValue(op, targets.ToArray()); return true; }

            var single = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                          .FirstOrDefault(f => f.FieldType == typeof(GameObject));
            if (single != null) { single.SetValue(op, targets[0]); return true; }
            return false;
        }

        /// <summary>One-line member layout of a CCK type — lands in the report when a shape is missing, so a
        /// single user paste is enough to tighten the wiring for that CCK version.</summary>
        private static string DumpMembers(Type t) =>
            "[" + t.Name + ": " + string.Join(", ",
                t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                 .Select(f => $"{Short(f.FieldType)} {f.Name}")) + "]";

        private static string Short(Type t) =>
            t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(Short))}>" : t.Name;
    }
}
