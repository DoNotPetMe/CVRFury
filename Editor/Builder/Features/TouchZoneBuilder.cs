using CVRFury.Components;

namespace CVRFury.Builder
{
    /// <summary>
    /// Touch zones are consumed when their reaction is created (the zone becomes the CCK trigger and the
    /// component is removed). One still present at bake was placed but never turned into a reaction — say
    /// so instead of the generic "no builder" warning; the component itself is stripped like all intent.
    /// </summary>
    internal sealed class TouchZoneBuilder : FeatureBuilder<CVRFuryTouchZone>
    {
        protected override void Build(BuildContext ctx, CVRFuryTouchZone f)
        {
            ctx.Log.Info($"Touch zone on '{f.gameObject.name}' was never turned into a reaction — removed " +
                         "from the upload. Create the reaction in CVRFury ▸ Touch reactions to use it.");
        }
    }
}
