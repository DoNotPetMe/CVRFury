using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Detection + conversion scaffolding for VRChat penetration systems (SPS / DPS / TPS) → ChilloutVR.
    ///
    /// IMPORTANT: ChilloutVR has no native SPS/DPS deformation system. The shader-driven mesh deformation
    /// (plug bending toward a socket along depth) is VRChat-shader + VRChat-contacts specific and does not
    /// port. What DOES port is the contact/marker layer: a plug tip becomes a <c>CVRPointer</c> and a
    /// socket becomes a <c>CVRAdvancedAvatarSettingsTrigger</c> that fires on a matching pointer — CVR's
    /// native building blocks for this kind of interaction.
    ///
    /// This class currently provides non-destructive DETECTION only: it finds the markers VRChat avatars
    /// use so the window can list candidate plugs/sockets. The CVR-side write is gated on a design decision
    /// (which CVR target system) and added in a later step.
    /// </summary>
    internal static class SpsConverter
    {
        // VRCFury haptic components have moved names across versions; try the known ones by reflection.
        private static readonly string[] PlugTypeNames =
        {
            "VF.Model.VRCFuryHapticPlug", "VRCFuryHapticPlug", "VF.Model.VRCFurySpsPlug", "VRCFurySpsPlug",
        };
        private static readonly string[] SocketTypeNames =
        {
            "VF.Model.VRCFuryHapticSocket", "VRCFuryHapticSocket", "VF.Model.VRCFurySpsSocket", "VRCFurySpsSocket",
        };

        public struct Found
        {
            public Transform transform;
            public string kind;   // "Plug" or "Socket"
            public string source; // which system it was detected from
        }

        public static List<Found> Detect(GameObject avatar)
        {
            var found = new List<Found>();
            if (avatar == null) return found;

            // 1) VRCFury SPS plug/socket components (most modern avatars).
            foreach (var tn in PlugTypeNames) AddComponents(avatar, tn, "Plug", "VRCFury SPS", found);
            foreach (var tn in SocketTypeNames) AddComponents(avatar, tn, "Socket", "VRCFury SPS", found);

            // 2) VRChat Contacts (TPS / SPS use VRCContactReceiver/Sender with TPS_*/SPS_* collision tags).
            DetectContacts(avatar, found);

            // 3) DPS (Raliv): orifices/penetrators are marked by small Lights with characteristic ranges,
            //    and by GameObject names. Heuristic, but a useful hint.
            DetectDpsLights(avatar, found);

            // 4) Name-based fallback so nothing obvious is missed.
            DetectByName(avatar, found);

            // De-duplicate by transform+kind.
            return found
                .GroupBy(f => (f.transform, f.kind))
                .Select(g => g.First())
                .ToList();
        }

        public static string DetectReport(GameObject avatar)
        {
            var list = Detect(avatar);
            if (list.Count == 0)
                return "No SPS / DPS / TPS markers found. If this avatar has them, point me at the plug/socket " +
                       "transforms manually below.";

            var plugs = list.Where(f => f.kind == "Plug").ToList();
            var sockets = list.Where(f => f.kind == "Socket").ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Found {plugs.Count} plug(s) and {sockets.Count} socket(s):");
            foreach (var f in list.Take(40))
                sb.AppendLine($"  • {f.kind} — {Path(avatar.transform, f.transform)}   ({f.source})");
            if (list.Count > 40) sb.AppendLine("  …");
            sb.AppendLine("\nNote: CVR has no native SPS deformation — only the contact/marker layer converts. " +
                          "Choose the CVR target (pointer/trigger) to enable conversion.");
            return sb.ToString();
        }

        private static void AddComponents(GameObject avatar, string typeName, string kind, string source, List<Found> found)
        {
            var t = Reflect.FindType(typeName);
            if (t == null) return;
            foreach (var c in avatar.GetComponentsInChildren(t, true))
            {
                var comp = c as Component;
                if (comp != null) found.Add(new Found { transform = comp.transform, kind = kind, source = source });
            }
        }

        private static void DetectContacts(GameObject avatar, List<Found> found)
        {
            // VRCContactReceiver/Sender carry a list of string collision tags. SPS/TPS use tags such as
            // TPS_Pen_Penetrating / SPS_Pen* (plug) and TPS_Orf_Root / SPS_Socket* (socket).
            foreach (var typeName in new[]
            {
                "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver",
                "VRC.SDK3.Dynamics.Contact.Components.VRCContactSender",
            })
            {
                var t = Reflect.FindType(typeName);
                if (t == null) continue;
                foreach (var c in avatar.GetComponentsInChildren(t, true))
                {
                    var comp = c as Component;
                    if (comp == null) continue;
                    var tags = Reflect.GetField(comp, "collisionTags") as System.Collections.IEnumerable;
                    string joined = tags == null ? "" : string.Join(",", tags.Cast<object>().Select(o => o?.ToString() ?? "")).ToLowerInvariant();
                    if (joined.Contains("pen")) found.Add(new Found { transform = comp.transform, kind = "Plug", source = "VRChat Contacts" });
                    else if (joined.Contains("orf") || joined.Contains("socket")) found.Add(new Found { transform = comp.transform, kind = "Socket", source = "VRChat Contacts" });
                }
            }
        }

        private static void DetectDpsLights(GameObject avatar, List<Found> found)
        {
            foreach (var light in avatar.GetComponentsInChildren<Light>(true))
            {
                // Raliv DPS encodes orifice/penetrator state in light intensity (≈0.0–0.45) on tiny ranges.
                if (light.type != LightType.Point) continue;
                if (light.range > 0.6f) continue;
                float i = light.intensity;
                bool dps = Mathf.Approximately(i, 0f) || (i > 0.3f && i < 0.5f) || Mathf.Approximately(i, 0.41f);
                if (!dps) continue;
                // Penetrator lights usually sit at the tip; orifices have a pair of marker lights.
                found.Add(new Found { transform = light.transform.parent ? light.transform.parent : light.transform,
                                      kind = "Socket", source = "DPS light" });
            }
        }

        private static void DetectByName(GameObject avatar, List<Found> found)
        {
            foreach (var tr in avatar.GetComponentsInChildren<Transform>(true))
            {
                var n = tr.name.ToLowerInvariant();
                if (n.Contains("penetrator") || n.Contains("dildo") || (n.Contains("sps") && n.Contains("plug")))
                    found.Add(new Found { transform = tr, kind = "Plug", source = "name" });
                else if (n.Contains("orifice") || n.Contains("socket") || n.Contains("hole"))
                    found.Add(new Found { transform = tr, kind = "Socket", source = "name" });
            }
        }

        private static string Path(Transform root, Transform t) =>
            t == null ? "(null)" : UnityEditor.AnimationUtility.CalculateTransformPath(t, root);
    }
}
