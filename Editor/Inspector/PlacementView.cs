using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// "See the body under the clothing" — for placing things (nipple bumps, piercings, SPS sockets, touch
    /// zones) that sit on the body but are hidden by opaque garments. Toggles the SELECTED renderers'
    /// visibility in the editor only (renderer.enabled, not activeSelf — so toggles/animations aren't
    /// disturbed), remembering what it hid so one click brings it all back. Purely an authoring aid; changes
    /// nothing that ships.
    /// </summary>
    internal static class PlacementView
    {
        // Renderers we hid, so Show restores exactly those (and only those).
        private static readonly HashSet<Renderer> _hidden = new HashSet<Renderer>();

        [MenuItem("Tools/CVRFury/Placement ▸ Hide Selected (see body under clothing)", false, 30)]
        private static void Hide()
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled)
                    {
                        Undo.RecordObject(r, "Hide for placement");
                        r.enabled = false;
                        _hidden.Add(r);
                        n++;
                    }
            if (n == 0) EditorUtility.DisplayDialog("CVRFury",
                "Select the clothing object(s) in the Hierarchy first, then run this to see the body under them.", "OK");
            else SceneView.RepaintAll();
        }

        [MenuItem("Tools/CVRFury/Placement ▸ Show All Again", false, 31)]
        private static void ShowAll()
        {
            foreach (var r in _hidden)
                if (r != null) { Undo.RecordObject(r, "Show after placement"); r.enabled = true; }
            _hidden.Clear();
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/CVRFury/Placement ▸ Show All Again", true)]
        private static bool ShowAllValidate() => _hidden.Count > 0;
    }
}
