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

        public void Run(ConversionContext ctx)
        {
            if (ctx.Options.mergePlayableLayers) MergeLayers(ctx);
            if (ctx.Options.expressions)
            {
                BuildParamSyncMap(ctx);
                ConvertMenu(ctx);
            }
        }

        /// <summary>Read VRCExpressionParameters so we know which parameters are network-synced.
        /// Anything not synced in VRChat becomes a local (zero-bit) setting in CVR.</summary>
        private static void BuildParamSyncMap(ConversionContext ctx)
        {
            var ep = Reflect.GetField(ctx.VrcDescriptor, VrcNames.Desc_ExpressionParameters);
            var list = Reflect.AsList(ep == null ? null : Reflect.GetField(ep, VrcNames.ExprParams_List));
            if (list == null)
            {
                ctx.Log.Warning("No VRCExpressionParameters found; assuming all menu parameters are synced. " +
                                "Enable 'Make all parameters local' if you hit the synced-bit limit.");
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

        private static void MergeLayers(ConversionContext ctx)
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

                ControllerMerger.Merge(ctx.GetOrCreateController(), controller, ctx.Assets, "", ctx.Log);
                merged++;
                ctx.Log.Info($"Merged '{typeName}' playable layer.");
            }
            ctx.Log.Info($"Merged {merged} playable-layer controller(s) into the CVR animator.");
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

            // Ground-truth readback: report how each parameter is actually encoded. If toggles are
            // Float here, the usedType fix didn't take effect (almost always a stale recompile).
            var summary = ctx.Cvr.SummarizeSyncCost();
            if (summary.Contains("WARNING")) ctx.Log.Warning(summary); else ctx.Log.Info(summary);

            if (_syncedCount > 0 && !ctx.Options.forceLocalParameters)
                ctx.Log.Warning($"{_syncedCount} synced parameter(s) created. ChilloutVR caps synced bits " +
                                "(3200). Toggles are encoded as Bool (~1 bit) and dropdowns as Int, so this " +
                                "should fit comfortably. If the CCK still reports 'over the Synced Bit Limit', " +
                                "check the encoding readback above — Float toggles mean a stale CVRFury build.");
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
            if (string.IsNullOrEmpty(param) || !ctx.AddedParams.Add(param)) return false;
            var local = IsLocal(ctx, param);
            ctx.Cvr.AddToggle(name, param, false, local);
            if (local) _localCount++; else _syncedCount++;
            return true;
        }

        private bool AddSlider(ConversionContext ctx, string name, string param)
        {
            if (string.IsNullOrEmpty(param) || !ctx.AddedParams.Add(param)) return false;
            var local = IsLocal(ctx, param);
            ctx.Cvr.AddSlider(name, param, 0f, local);
            if (local) _localCount++; else _syncedCount++;
            return true;
        }

        /// <summary>A parameter is local if forced, or if VRChat marked it not network-synced.</summary>
        private static bool IsLocal(ConversionContext ctx, string param)
        {
            if (ctx.Options.forceLocalParameters) return true;
            return ctx.ParamSynced.TryGetValue(param, out var synced) && !synced;
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
