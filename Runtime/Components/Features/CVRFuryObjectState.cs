using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Forces objects into a given state at build time. Useful for shipping an avatar
    /// with editor-only helper objects disabled, deleting placeholder geometry, or
    /// guaranteeing a clothing item starts active regardless of how the scene was saved.
    /// Applied before animator-driven features so toggles see the corrected hierarchy.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Object State")]
    public class CVRFuryObjectState : CVRFuryComponent
    {
        public override string FeatureTitle => "Object State";

        // Structural changes should happen before toggles bind to objects.
        public override int BuildPriority => -10;

        public enum Action
        {
            Activate,
            Deactivate,
            Delete,
        }

        [Serializable]
        public class Entry
        {
            public GameObject target;
            public Action action = Action.Deactivate;
        }

        public List<Entry> entries = new List<Entry>();
    }
}
