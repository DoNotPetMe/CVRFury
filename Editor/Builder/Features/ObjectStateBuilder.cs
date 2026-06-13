using CVRFury.Components;
using UnityEngine;

namespace CVRFury.Builder
{
    internal sealed class ObjectStateBuilder : FeatureBuilder<CVRFuryObjectState>
    {
        protected override void Build(BuildContext ctx, CVRFuryObjectState f)
        {
            foreach (var e in f.entries)
            {
                if (e?.target == null) continue;
                switch (e.action)
                {
                    case CVRFuryObjectState.Action.Activate:
                        e.target.SetActive(true);
                        break;
                    case CVRFuryObjectState.Action.Deactivate:
                        e.target.SetActive(false);
                        break;
                    case CVRFuryObjectState.Action.Delete:
                        Object.DestroyImmediate(e.target, true);
                        break;
                }
            }
        }
    }
}
