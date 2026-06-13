using System;
using CVRFury.Components;

namespace CVRFury.Builder
{
    /// <summary>Bakes one kind of <see cref="CVRFuryComponent"/> into the avatar.</summary>
    internal interface IFeatureBuilder
    {
        Type ComponentType { get; }
        void Apply(BuildContext ctx, CVRFuryComponent component);
    }

    /// <summary>Strongly-typed base. Subclass per feature; the registry auto-discovers it.</summary>
    internal abstract class FeatureBuilder<T> : IFeatureBuilder where T : CVRFuryComponent
    {
        public Type ComponentType => typeof(T);

        public void Apply(BuildContext ctx, CVRFuryComponent component) => Build(ctx, (T)component);

        protected abstract void Build(BuildContext ctx, T feature);
    }
}
