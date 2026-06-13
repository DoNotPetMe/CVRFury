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

        public void Run(ConversionContext ctx)
        {
            if (ctx.Options.mergePlayableLayers) MergeLayers(ctx);
            if (ctx.Options.expressions) ConvertMenu(ctx);
        }

        private static void MergeLayers(ConversionContext ctx)
        {
            var layers = Reflect.GetField(ctx.VrcDescriptor, VrcNames.Desc_BaseAnimationLayers) as IEnumerable;
            if (layers == null) return;

            var merged = 0;
            foreach (var layer in layers)
            {
                if (Reflect.GetField(layer, VrcNames.Layer_Controller) is AnimatorController ac)
                {
                    ControllerMerger.Merge(ctx.GetOrCreateController(), ac, ctx.Assets, "", ctx.Log);
                    merged++;
                }
            }
            ctx.Log.Info($"Merged {merged} playable-layer controller(s) into the CVR animator.");
        }

        private static void ConvertMenu(ConversionContext ctx)
        {
            var menu = Reflect.GetField(ctx.VrcDescriptor, VrcNames.Desc_ExpressionsMenu);
            if (menu == null)
            {
                ctx.Log.Warning("No VRCExpressionsMenu on the descriptor; skipped menu conversion.");
                return;
            }
            var added = 0;
            WalkMenu(ctx, menu, new HashSet<object>(), ref added);
            ctx.Log.Info($"Added {added} Advanced Avatar Setting(s) from the expression menu.");
        }

        private static void WalkMenu(ConversionContext ctx, object menu, HashSet<object> visited, ref int added)
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
                    {
                        var p = ParamName(control);
                        if (!string.IsNullOrEmpty(p)) { ctx.Cvr.AddToggle(name, p, false, false); added++; }
                        break;
                    }
                    case "RadialPuppet":
                    {
                        var p = SubParamName(control, 0);
                        if (!string.IsNullOrEmpty(p)) { ctx.Cvr.AddSlider(name, p, 0f, false); added++; }
                        break;
                    }
                    case "TwoAxisPuppet":
                    case "FourAxisPuppet":
                    {
                        // CVR has joystick settings, but our reflection wrapper exposes sliders; map
                        // each axis to a slider as an approximation.
                        for (var i = 0; i < 2; i++)
                        {
                            var p = SubParamName(control, i);
                            if (!string.IsNullOrEmpty(p)) { ctx.Cvr.AddSlider($"{name} {i + 1}", p, 0f, false); added++; }
                        }
                        ctx.Log.Warning($"'{name}' is a puppet; converted to slider(s) (approximate).");
                        break;
                    }
                    case "SubMenu":
                        WalkMenu(ctx, Reflect.GetField(control, VrcNames.Control_SubMenu), visited, ref added);
                        break;
                }
            }
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
