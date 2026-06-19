using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CVRFury.Components;

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
                return "No plugs or sockets found.\n" +
                       "Next: in Step 2, pick the spot where you want an orifice and add lights there.";

            var plugs = list.Where(f => f.kind == "Plug").ToList();
            var sockets = list.Where(f => f.kind == "Socket").ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Found {plugs.Count} plug(s) and {sockets.Count} socket(s):");
            foreach (var f in list.Take(40))
                sb.AppendLine($"  • {f.kind} — {Path(avatar.transform, f.transform)}");
            if (list.Count > 40) sb.AppendLine("  …");
            sb.Append(sockets.Count > 0
                ? "\nNext: Step 2 — add DPS orifice lights to the socket(s)."
                : "\nNext: Step 2 — pick where the orifice goes and add lights there.");
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
        public static GameObject GenerateDpsOrifice(Transform target, string name = "DPS Orifice (CVRFury)",
                                                    bool addToggle = true)
        {
            if (target == null) return null;
            var root = new GameObject(name);
            UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Bake DPS orifice");
            root.transform.SetParent(target, worldPositionStays: false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            MakeMarkerLight(root.transform, "DPS_Light", Vector3.zero);
            MakeMarkerLight(root.transform, "DPS_Light_Normal", new Vector3(0f, 0f, DpsNormalOffset));
            AssignOrificeIcon(root);                 // distinct scene/hierarchy icon (not the plain light icon)
            if (addToggle) AddDefaultOffToggle(root); // menu toggle, OFF by default
            return root;
        }

        /// <summary>Make the orifice start disabled and add a CVRFury menu toggle (default OFF) that enables
        /// it. So every DPS location is opt-in: no deformation until the wearer turns it on in the menu.</summary>
        private static void AddDefaultOffToggle(GameObject orifice)
        {
            var host = orifice.transform.root.gameObject; // keep the toggle on an ACTIVE object so it bakes
            var toggle = UnityEditor.Undo.AddComponent<CVRFuryToggle>(host);
            toggle.menuPath = orifice.name;
            toggle.defaultOn = false;
            toggle.saved = true;
            toggle.state.actions.Add(new FuryAction
            {
                type = FuryAction.ActionType.ObjectToggle,
                targetObject = orifice,
                targetState = true,
            });
            orifice.SetActive(false); // OFF by default
        }

        // A distinct icon so DPS orifices are obvious in the scene/hierarchy (the lights alone just show
        // Unity's generic light icon). Stored per-object in the scene; no runtime component is added.
        private static Texture2D _orificeIcon;
        private static void AssignOrificeIcon(GameObject go)
        {
            if (_orificeIcon == null) _orificeIcon = BuildOrificeIcon();
            UnityEditor.EditorGUIUtility.SetIconForObject(go, _orificeIcon);
        }

        private static Texture2D BuildOrificeIcon()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var ring = new Color(0.93f, 0.23f, 0.66f, 1f);   // magenta ring
            float c = (size - 1) / 2f, rOut = 14f, rIn = 8f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = (d <= rOut && d >= rIn) ? 1f : 0f; // a ring shape
                    tex.SetPixel(x, y, new Color(ring.r, ring.g, ring.b, a));
                }
            tex.Apply();
            return tex;
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
        public static string AutoBake(GameObject avatar, bool addToggle = true)
        {
            if (avatar == null) return "Select your avatar first.";
            var sockets = Detect(avatar).Where(f => f.kind == "Socket").ToList();
            if (sockets.Count == 0)
                return "No sockets found to add lights to.\n" +
                       "Next: use the \"Or one spot\" field above to pick where the orifice goes, then \"Add to this spot\".";

            int baked = 0, skipped = 0;
            foreach (var s in sockets)
            {
                if (s.transform == null) continue;
                // Skip if this socket already has DPS marker lights under it.
                bool hasLights = s.transform.GetComponentsInChildren<Light>(true)
                    .Any(l => l.type == LightType.Point && Mathf.Abs(l.range - DpsOrificeRange) < 0.01f);
                if (hasLights) { skipped++; continue; }
                GenerateDpsOrifice(s.transform, addToggle: addToggle);
                baked++;
            }

            if (baked == 0)
                return $"All {skipped} socket(s) already have DPS lights — nothing to add.\n" +
                       "Next: Step 3 — pick the plug mesh and click \"Enable deformation\".";
            var toggleNote = addToggle
                ? " Each is OFF by default with its own menu toggle, so no deformation until the wearer turns it on."
                : "";
            return $"Done — added DPS orifice lights to {baked} socket(s)" +
                   (skipped > 0 ? $" ({skipped} already had them)" : "") + "." + toggleNote + "\n" +
                   "Next: Step 3 — pick the plug mesh and click \"Enable deformation\".";
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
                return "Pick both a working orifice and a Step 2 spot first.";

            var copy = Object.Instantiate(template.gameObject);
            copy.name = template.name + " (CVR DPS)";
            copy.transform.SetParent(target, worldPositionStays: false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            UnityEditor.Undo.RegisterCreatedObjectUndo(copy, "Clone DPS orifice");
            UnityEditor.Selection.activeGameObject = copy;

            int lights = copy.GetComponentsInChildren<Light>(true).Length;
            return $"Done — copied the orifice onto '{target.name}' ({lights} light(s)).\n" +
                   "Next: rotate it so the opening faces outward, then Step 3 — switch the plug's shader.";
        }

        /// <summary>Best guess at the penetrator mesh object, for the Step 3 auto-fill. Prefers a detected
        /// plug that actually has a renderer; falls back to the renderer nearest a detected plug.</summary>
        public static Transform FindPlug(GameObject avatar)
        {
            if (avatar == null) return null;
            var plugs = Detect(avatar).Where(f => f.kind == "Plug" && f.transform != null).ToList();

            // A detected plug that already carries a mesh is the ideal answer.
            foreach (var p in plugs)
                if (p.transform.GetComponentInChildren<Renderer>(true) != null)
                    return p.transform;

            // Otherwise, a child/parent renderer near the first detected plug.
            if (plugs.Count > 0)
            {
                var t = plugs[0].transform;
                var r = t.GetComponentInChildren<Renderer>(true) ?? t.GetComponentInParent<Renderer>();
                if (r != null) return r.transform;
                return t;
            }
            return null;
        }

        // --- Step 3: turn on light-based deformation on the plug's material -------------------------
        // We don't hardcode shader property names (they differ per shader/version). Instead we read the
        // material's ACTUAL shader properties and switch on the ones that are clearly a penetration /
        // DPS deform enable. Safe and reversible; reports exactly what changed (or why it couldn't).
        public static string SetupPlugShader(Transform plug)
        {
            if (plug == null) return "Pick the plug (penetrator) mesh first.";
            var renderers = plug.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return $"No mesh renderer found under '{plug.name}'. Pick the object that has the plug's mesh.";

            var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null)
                                .Distinct().ToList();
            if (mats.Count == 0) return $"'{plug.name}' has no materials to set up.";

            int enabledProps = 0, lockedMats = 0;
            var shaders = new System.Collections.Generic.HashSet<string>();
            var details = new System.Text.StringBuilder();

            foreach (var m in mats)
            {
                shaders.Add(m.shader.name);
                if (m.shader.name.StartsWith("Hidden/Locked") || m.shader.name.StartsWith("Locked/"))
                {
                    lockedMats++;
                    continue; // locked/optimised Poiyomi material — properties are baked, can't toggle
                }

                var enables = FindDeformEnableProps(m.shader);
                if (enables.Count == 0) continue;
                UnityEditor.Undo.RecordObject(m, "Enable plug deformation");
                foreach (var p in enables)
                {
                    m.SetFloat(p, 1f);
                    enabledProps++;
                    details.AppendLine($"  • {m.name}: set {p} = 1");
                }
                UnityEditor.EditorUtility.SetDirty(m);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Plug '{plug.name}': {mats.Count} material(s), shader(s): {string.Join(", ", shaders)}.");
            if (enabledProps > 0)
            {
                sb.AppendLine($"Done — enabled deformation on {enabledProps} property(ies):");
                sb.Append(details);
                sb.Append("Next: test in CVR — the plug should bend toward the orifice lights.");
            }
            else if (lockedMats > 0)
            {
                sb.Append($"{lockedMats} material(s) are LOCKED/optimised (Poiyomi). Unlock them first " +
                          "(material header → Unlock), run this again, then re-lock before upload.");
            }
            else
            {
                sb.Append($"Couldn't auto-enable: no penetration-deform toggle exists on {string.Join(", ", shaders)}. " +
                          "Either it's not a DPS/SPS shader, or (Poiyomi Pro) the \"Penetration Deformation\" " +
                          "module isn't added to this material yet.\n" +
                          $"Next: send me this exact shader name — \"{string.Join("\", \"", shaders)}\" — and I'll wire it up.");
            }
            return sb.ToString();
        }

        /// <summary>Names of float/toggle properties on the shader that look like a penetration-deform
        /// ENABLE switch (e.g. "_EnablePenetrationDeformation", "_DPS_Penetrator_Enabled").</summary>
        private static System.Collections.Generic.List<string> FindDeformEnableProps(Shader shader)
        {
            var result = new System.Collections.Generic.List<string>();
            int count = UnityEditor.ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                var t = UnityEditor.ShaderUtil.GetPropertyType(shader, i);
                if (t != UnityEditor.ShaderUtil.ShaderPropertyType.Float &&
                    t != UnityEditor.ShaderUtil.ShaderPropertyType.Range) continue;
                var name = UnityEditor.ShaderUtil.GetPropertyName(shader, i);
                var n = name.ToLowerInvariant();
                bool deform = n.Contains("penetr") || n.Contains("dps") || n.Contains("orifice");
                bool enable = n.Contains("enable") || n.Contains("toggle") || n.EndsWith("_en") || n.Contains("active");
                if (deform && enable) result.Add(name);
            }
            return result;
        }

    }
}
