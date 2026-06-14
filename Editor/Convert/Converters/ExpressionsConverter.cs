using System.Collections;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Brings VRChat's expression system across: merges the playable-layer animator controllers
    /// (FX/Gesture/Action/…) into the CVR animator, and turns the VRCExpressionsMenu controls into
    /// ChilloutVR Advanced Avatar Settings (toggles, sliders, dropdowns) so they appear in the
    /// in-game menu. VRChat's GestureLeft/GestureRight parameter names carry over unchanged.
    /// </summary>
    internal sealed class ExpressionsConverter : IConverter
    {
        public string Title => "Expressions (menu + parameters + playable layers)";
        public int Order => 30;
        public bool ShouldRun(ConversionContext ctx) => ctx.Options.expressions || ctx.Options.mergePlayableLayers;

        private int _syncedCount;
        private int _localCount;

        /// <summary>VRChat parameter names actually exposed by a menu control. Only these (when also
        /// VRChat-synced) stay network-synced in CVR; every other parameter is localised. A synced
        /// VRChat parameter with no menu control has nothing driving it in CVR (it was driven by
        /// contacts/OSC/parameter-drivers that don't convert) — syncing it just wastes bits, and CVR
        /// floats cost 64 bits each.</summary>
        private readonly HashSet<string> _menuParams = new HashSet<string>();

        public void Run(ConversionContext ctx)
        {
            // The sync map + menu-parameter set must be known *before* merging, so the merge can
            // localise (#-prefix) non-synced parameters. ChilloutVR syncs every animator parameter
            // except #-prefixed ones; merging VRChat's FX controller (full of local smoothing/driver
            // floats, plus synced-but-unused params) is what blows the 3200 synced-bit budget.
            BuildParamSyncMap(ctx);
            if (ctx.Options.expressions) CollectMenuParams(ctx);
            if (ctx.Options.mergePlayableLayers) MergeLayers(ctx);
            if (ctx.Options.expressions) ConvertMenu(ctx);

            // Final pass: ChilloutVR's synced-bit cost is driven by each animator parameter's TYPE
            // (a float ≈ 64 bits, a bool ≈ 1). Retype float parameters that are really just on/off
            // toggles down to Bool — the native, zero-latency CVR equivalent of VRCFury's parameter
            // compressor. Genuinely-continuous floats (radials/blend trees) are reported, not touched.
            if (ctx.Controller != null)
                SyncBitOptimizer.Run(ctx.Controller, n => n[0] != '#' && !CoreParams.Contains(n), ctx.Log);
        }

        /// <summary>Pre-walk the menu to learn which parameters are reachable from a control, so the
        /// merge can keep only those synced. Does not create AAS entries (that is ConvertMenu's job).</summary>
        private void CollectMenuParams(ConversionContext ctx)
        {
            var menu = Reflect.GetField(ctx.VrcDescriptor, VrcNames.Desc_ExpressionsMenu);
            if (menu == null) return;
            CollectFrom(menu, new HashSet<object>());
        }

        private void CollectFrom(object menu, HashSet<object> visited)
        {
            if (menu == null || !visited.Add(menu)) return;
            var controls = Reflect.AsList(Reflect.GetField(menu, VrcNames.Menu_Controls));
            if (controls == null) return;
            foreach (var control in controls)
            {
                var main = ParamName(control);
                if (!string.IsNullOrEmpty(main)) _menuParams.Add(main);
                for (var i = 0; i < 4; i++)
                {
                    var sub = SubParamName(control, i);
                    if (!string.IsNullOrEmpty(sub)) _menuParams.Add(sub);
                }
                var type = Reflect.GetField(control, VrcNames.Control_Type)?.ToString() ?? "";
                if (type == "SubMenu")
                    CollectFrom(Reflect.GetField(control, VrcNames.Control_SubMenu), visited);
            }
        }

        /// <summary>ChilloutVR core/locomotion parameters the platform drives by name. Never localise
        /// or sync these — CVR recognises them as core (they don't count toward the synced budget),
        /// and #-prefixing them would stop the game writing to them. (Normally these are already in
        /// the seeded base controller and kept as-is; this guards the case where seeding failed.)</summary>
        private static readonly HashSet<string> CoreParams = new HashSet<string>
        {
            "GestureLeft", "GestureRight", "Grounded", "Travelling", "Sitting", "Crouching",
            "Prone", "Flying", "Emote", "MovementX", "MovementY",
        };

        /// <summary>
        /// The parameter name ChilloutVR should actually use. CVR core params keep their name; synced
        /// VRChat parameters keep their name (CVR syncs them); everything else is prefixed with
        /// <c>#</c> so CVR treats it as a local, zero-synced-bit parameter. Honours the "make all
        /// parameters local" option.
        /// </summary>
        private string FinalName(ConversionContext ctx, string vrcName)
        {
            if (string.IsNullOrEmpty(vrcName) || vrcName[0] == '#' || CoreParams.Contains(vrcName))
                return vrcName;
            // Keep synced only if VRChat synced it AND a menu control actually exposes it. A synced
            // parameter with no menu control has nothing to drive it in CVR, so it would just burn
            // synced bits (64 each for floats) for no benefit.
            var synced = !ctx.Options.forceLocalParameters &&
                         _menuParams.Contains(vrcName) &&
                         ctx.ParamSynced.TryGetValue(vrcName, out var s) && s;
            return synced ? vrcName : "#" + vrcName;
        }

        /// <summary>Read VRCExpressionParameters so we know which parameters are network-synced.
        /// Anything not synced in VRChat becomes a local (zero-bit) setting in CVR.</summary>
        private static void BuildParamSyncMap(ConversionContext ctx)
        {
            var ep = Reflect.GetField(ctx.VrcDescriptor, VrcNames.Desc_ExpressionParameters);
            var list = Reflect.AsList(ep == null ? null : Reflect.GetField(ep, VrcNames.ExprParams_List));
            if (list == null)
            {
                ctx.Log.Warning("No VRCExpressionParameters found, so CVRFury can't tell which parameters " +
                                "VRChat network-synced. To stay under the synced-bit limit it will localise " +
                                "(#-prefix) ALL converted parameters — your toggles will work locally but " +
                                "won't be visible to others. If you need specific parameters synced, set them " +
                                "manually in the CVRAvatar inspector (remove the leading #).");
                return;
            }
            foreach (var p in list)
            {
                if (p == null) continue;
                var name = Reflect.GetField(p, VrcNames.ExprParam_Name) as string;
                if (string.IsNullOrEmpty(name)) continue;
                var synced = !(Reflect.GetField(p, VrcNames.ExprParam_NetworkSynced) is bool b) || b;
                ctx.ParamSynced[name] = synced;
            }
            ctx.Log.Info($"Read {ctx.ParamSynced.Count} VRChat expression parameter(s).");
        }

        private void MergeLayers(ConversionContext ctx)
        {
            var layers = Reflect.GetField(ctx.VrcDescriptor, VrcNames.Desc_BaseAnimationLayers) as IEnumerable;
            if (layers == null) return;

            var merged = 0;
            foreach (var layer in layers)
            {
                var controller = Reflect.GetField(layer, VrcNames.Layer_Controller) as AnimatorController;
                if (controller == null) continue;

                var typeName = Reflect.GetField(layer, VrcNames.Layer_Type)?.ToString() ?? "?";

                // Only the FX layer is safe to merge. Base/Additive/Action/Sitting/etc. carry
                // full-body animation that fights ChilloutVR's own locomotion and pins the avatar
                // in a pose (the classic "motorcycle pose"). Gesture is opt-in (it can still clash
                // with CVR's hand gestures).
                var include = typeName == "FX" || (ctx.Options.mergeGestureLayer && typeName == "Gesture");
                if (!include)
                {
                    ctx.Log.Info($"Skipped '{typeName}' playable layer (only FX is merged to avoid " +
                                 "full-body pose conflicts with CVR locomotion).");
                    continue;
                }

                ControllerMerger.Merge(ctx.GetOrCreateController(), controller, ctx.Assets, "", ctx.Log,
                                       renameParameter: n => FinalName(ctx, n));
                merged++;
                ctx.Log.Info($"Merged '{typeName}' playable layer.");
            }
            ctx.Log.Info($"Merged {merged} playable-layer controller(s) into the CVR animator. " +
                         $"Kept {_menuParams.Count} menu parameter(s) eligible for sync; all other " +
                         "parameters (FX-internal locals and synced-but-unused) were localised " +
                         "(#-prefixed) so they cost zero synced bits.");
        }

        private void ConvertMenu(ConversionContext ctx)
        {
            var menu = Reflect.GetField(ctx.VrcDescriptor, VrcNames.Desc_ExpressionsMenu);
            if (menu == null)
            {
                ctx.Log.Warning("No VRCExpressionsMenu found on the descriptor (field '" +
                                VrcNames.Desc_ExpressionsMenu + "' was null or unresolved). " +
                                "If the avatar definitely has a menu, the field name may differ for your " +
                                "SDK version — update Editor/Convert/VrcNames.cs.");
                return;
            }

            var topControls = Reflect.AsList(Reflect.GetField(menu, VrcNames.Menu_Controls));
            if (topControls == null)
                ctx.Log.Warning("Found the menu but its '" + VrcNames.Menu_Controls +
                                "' list was unresolved — check VrcNames for your SDK version.");
            else
                ctx.Log.Info($"Expression menu top level has {topControls.Count} control(s).");

            var added = 0;
            WalkMenu(ctx, menu, new HashSet<object>(), ref added);
            ctx.Log.Info($"Added {added} Advanced Avatar Setting(s): {_syncedCount} synced, {_localCount} local. " +
                         "(If 0 but the avatar has menu controls, the control field/enum names in VrcNames " +
                         "may need updating for your SDK version.)");

            // Ground-truth readback: report how each parameter is actually encoded.
            ctx.Log.Info(ctx.Cvr.SummarizeSyncCost());

            ctx.Log.Info($"Menu parameters: {_syncedCount} synced, {_localCount} local (#-prefixed, zero " +
                         "synced bits). Non-synced VRChat parameters and merged FX-internal parameters are " +
                         "localised automatically; enable 'Make all parameters local' to force everything local.");
        }

        private void WalkMenu(ConversionContext ctx, object menu, HashSet<object> visited, ref int added)
        {
            if (menu == null || !visited.Add(menu)) return; // guard against submenu cycles

            var controls = Reflect.AsList(Reflect.GetField(menu, VrcNames.Menu_Controls));
            if (controls == null) return;

            foreach (var control in controls)
            {
                var name = Reflect.GetField(control, VrcNames.Control_Name) as string ?? "Control";
                var type = Reflect.GetField(control, VrcNames.Control_Type)?.ToString() ?? "";

                switch (type)
                {
                    case "Toggle":
                    case "Button":
                        if (AddToggle(ctx, name, ParamName(control))) added++;
                        break;
                    case "RadialPuppet":
                        if (AddSlider(ctx, name, SubParamName(control, 0))) added++;
                        break;
                    case "TwoAxisPuppet":
                    case "FourAxisPuppet":
                        // CVR has joystick settings, but our reflection wrapper exposes sliders; map
                        // each axis to a slider as an approximation.
                        for (var i = 0; i < 2; i++)
                            if (AddSlider(ctx, $"{name} {i + 1}", SubParamName(control, i))) added++;
                        ctx.Log.Warning($"'{name}' is a puppet; converted to slider(s) (approximate).");
                        break;
                    case "SubMenu":
                        WalkMenu(ctx, Reflect.GetField(control, VrcNames.Control_SubMenu), visited, ref added);
                        break;
                }
            }
        }

        private bool AddToggle(ConversionContext ctx, string name, string param)
        {
            if (string.IsNullOrEmpty(param)) return false;
            // The AAS entry must drive the *final* (possibly #-localised) controller parameter name,
            // so the toggle keeps working after the merge renames non-synced params to local.
            var machine = FinalName(ctx, param);
            if (!ctx.AddedParams.Add(machine)) return false;
            var local = machine[0] == '#';
            ctx.Cvr.AddToggle(name, machine, false, local);
            if (local) _localCount++; else _syncedCount++;
            return true;
        }

        private bool AddSlider(ConversionContext ctx, string name, string param)
        {
            if (string.IsNullOrEmpty(param)) return false;
            var machine = FinalName(ctx, param);
            if (!ctx.AddedParams.Add(machine)) return false;
            var local = machine[0] == '#';
            ctx.Cvr.AddSlider(name, machine, 0f, local);
            if (local) _localCount++; else _syncedCount++;
            return true;
        }

        private static string ParamName(object control)
        {
            var p = Reflect.GetField(control, VrcNames.Control_Parameter);
            return p == null ? null : Reflect.GetField(p, VrcNames.Control_ParameterName) as string;
        }

        private static string SubParamName(object control, int index)
        {
            var subs = Reflect.AsList(Reflect.GetField(control, VrcNames.Control_SubParameters));
            if (subs == null || index >= subs.Count) return null;
            var p = subs[index];
            return p == null ? null : Reflect.GetField(p, VrcNames.Control_ParameterName) as string;
        }
    }
}
