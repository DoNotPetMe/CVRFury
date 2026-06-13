using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CVRFury.Components;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Maps each <see cref="CVRFuryComponent"/> type to the builder that bakes it. Builders are
    /// discovered by reflection, so adding a new feature is just: add the component + a
    /// <c>FeatureBuilder&lt;T&gt;</c> subclass. No central list to edit.
    /// </summary>
    internal static class FeatureBuilderRegistry
    {
        private static Dictionary<Type, IFeatureBuilder> _byComponent;

        public static IFeatureBuilder For(CVRFuryComponent component)
        {
            EnsureBuilt();
            return _byComponent.TryGetValue(component.GetType(), out var b) ? b : null;
        }

        private static void EnsureBuilt()
        {
            if (_byComponent != null) return;
            _byComponent = new Dictionary<Type, IFeatureBuilder>();

            var builderTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => !t.IsAbstract && typeof(IFeatureBuilder).IsAssignableFrom(t));

            foreach (var t in builderTypes)
            {
                if (!(Activator.CreateInstance(t) is IFeatureBuilder instance)) continue;
                if (_byComponent.ContainsKey(instance.ComponentType))
                {
                    Debug.LogWarning($"[CVRFury] Two builders claim {instance.ComponentType.Name}; " +
                                     $"keeping the first.");
                    continue;
                }
                _byComponent[instance.ComponentType] = instance;
            }
        }
    }
}
