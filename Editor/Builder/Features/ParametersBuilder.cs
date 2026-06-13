using CVRFury.Components;

namespace CVRFury.Builder
{
    internal sealed class ParametersBuilder : FeatureBuilder<CVRFuryParameters>
    {
        protected override void Build(BuildContext ctx, CVRFuryParameters f)
        {
            if (f.parameters == null || f.parameters.Count == 0) return;
            var controller = ctx.GetOrCreateController();

            foreach (var p in f.parameters)
            {
                if (string.IsNullOrEmpty(p.name))
                {
                    ctx.Log.Warning("Parameters: skipped an entry with no name.");
                    continue;
                }

                switch (p.type)
                {
                    case CVRFuryParameters.ParamType.Int:
                        AnimatorUtil.EnsureIntParam(controller, p.name, Mathf_RoundToInt(p.defaultValue));
                        break;
                    case CVRFuryParameters.ParamType.Bool:
                        AnimatorUtil.EnsureBoolParam(controller, p.name, p.defaultValue >= 0.5f);
                        break;
                    default:
                        AnimatorUtil.EnsureFloatParam(controller, p.name, p.defaultValue);
                        break;
                }

                var display = MenuLeaf(p.menuPath, p.name);
                switch (p.menu)
                {
                    case CVRFuryParameters.MenuKind.Toggle:
                        ctx.Avatar.AddToggle(display, p.name, p.defaultValue >= 0.5f, p.localOnly);
                        break;
                    case CVRFuryParameters.MenuKind.Slider:
                    case CVRFuryParameters.MenuKind.Dropdown:
                        // A declared parameter has no option list; expose it as a slider.
                        ctx.Avatar.AddSlider(display, p.name, p.defaultValue, p.localOnly);
                        break;
                    case CVRFuryParameters.MenuKind.None:
                    default:
                        break;
                }
            }
        }

        private static int Mathf_RoundToInt(float v) => UnityEngine.Mathf.RoundToInt(v);

        private static string MenuLeaf(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            var parts = path.Split('/');
            var leaf = parts[parts.Length - 1].Trim();
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
    }
}
