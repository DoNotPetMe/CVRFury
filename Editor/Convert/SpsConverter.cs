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

        // --- SPS → DPS auto-bake -------------------------------------------------------------------
        // Raliv DPS marks an orifice with point LIGHTS that the penetrator's shader reads to bend the mesh.
        // The canonical encoding (VERIFY against a known-working orifice and tell me if these differ — they
        // must be exact or the shader ignores the orifice):
        //   • Two point lights, Range = 0.5, Intensity = 0, at the orifice opening.
        //   • The vector from the first light to the second (placed slightly along the orifice's forward)
        //     gives the orifice orientation/depth direction.
        // These render in CVR, so a baked orifice deforms a DPS-shader penetrator there just like in VRChat.
        public const float DpsOrificeRange = 0.5f;
        public const float DpsOrificeIntensity = 0f;
        public const float DpsNormalOffset = 0.01f; // metres the direction light sits ahead of the entrance

        /// <summary>Create a Raliv-DPS orifice marker (the light rig) at <paramref name="target"/> so a
        /// DPS-shader penetrator deforms toward it in CVR. Experimental: the light codes are the documented
        /// canonical values and may need calibration against a working orifice.</summary>
        public static GameObject GenerateDpsOrifice(Transform target, string name = "DPS Orifice (CVRFury)")
        {
            if (target == null) return null;
            var root = new GameObject(name);
            UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Bake DPS orifice");
            root.transform.SetParent(target, worldPositionStays: false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            MakeMarkerLight(root.transform, "DPS_Light", Vector3.zero);
            MakeMarkerLight(root.transform, "DPS_Light_Normal", new Vector3(0f, 0f, DpsNormalOffset));
            return root;
        }

        private static void MakeMarkerLight(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = DpsOrificeRange;
            l.intensity = DpsOrificeIntensity;
            l.color = Color.black;
            l.renderMode = LightRenderMode.ForceVertex;
            l.shadows = LightShadows.None;
        }

        /// <summary>Bake DPS orifice lights for sockets that don't already have a DPS light rig nearby.
        /// Returns a human-readable report.</summary>
        public static string AutoBake(GameObject avatar)
        {
            if (avatar == null) return "Select your avatar first.";
            var sockets = Detect(avatar).Where(f => f.kind == "Socket").ToList();
            if (sockets.Count == 0)
                return "No sockets detected to bake. Use 'Clone DPS orifice' with a working template instead, " +
                       "or pick a socket transform and bake it directly.";

            int baked = 0;
            foreach (var s in sockets)
            {
                if (s.transform == null) continue;
                // Skip if this socket already has DPS marker lights under it.
                bool hasLights = s.transform.GetComponentsInChildren<Light>(true)
                    .Any(l => l.type == LightType.Point && Mathf.Abs(l.range - DpsOrificeRange) < 0.01f);
                if (hasLights) continue;
                GenerateDpsOrifice(s.transform);
                baked++;
            }

            return $"Baked {baked} DPS orifice light-rig(s) onto detected socket(s) " +
                   $"(Range={DpsOrificeRange}, Intensity={DpsOrificeIntensity}, normal offset={DpsNormalOffset}m).\n" +
                   "EXPERIMENTAL: these are the canonical Raliv DPS values — verify in CVR against a known-good " +
                   "orifice. Nudge each rig so it faces outward, and give the penetrator a DPS-capable shader. " +
                   "If deformation doesn't trigger, tell me the light Range/Intensity from a working orifice and " +
                   "I'll calibrate the encoding.";
        }

        /// <summary>
        /// Transplant a known-working DPS orifice light-rig onto a new socket location. DPS deformation is
        /// driven entirely by marker point-lights read by the penetrator's shader, and those render in CVR
        /// exactly as in VRChat — so copying a rig that already works is the most reliable way to give an
        /// SPS-only avatar real, working deformation without hand-encoding Raliv's light intensity/range
        /// codes (which must be exact or the shader won't detect the orifice).
        /// </summary>
        public static string CloneOrifice(Transform template, Transform target)
        {
            if (template == null || target == null)
                return "Assign BOTH a working DPS orifice (template — from an avatar where DPS already works " +
                       "in CVR) and a target socket location on this avatar.";

            var copy = Object.Instantiate(template.gameObject);
            copy.name = template.name + " (CVR DPS)";
            copy.transform.SetParent(target, worldPositionStays: false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            UnityEditor.Undo.RegisterCreatedObjectUndo(copy, "Clone DPS orifice");
            UnityEditor.Selection.activeGameObject = copy;

            int lights = copy.GetComponentsInChildren<Light>(true).Length;
            return $"Cloned DPS orifice '{template.name}' onto '{target.name}' ({lights} marker light(s) copied). " +
                   "It sits at the target's origin — nudge its position/rotation so the opening faces outward, " +
                   "then test in CVR with a DPS-shader penetrator. Because the marker lights are copied from a " +
                   "working rig, the deformation should behave just like the source.";
        }

    }
}
