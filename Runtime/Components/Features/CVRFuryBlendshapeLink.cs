using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Mirrors blendshape values from a source mesh onto one or more target meshes that
    /// share blendshape names. Commonly used so clothing/accessory meshes follow the
    /// avatar's body shape keys (chest size, etc.) — either baked once at build, or kept
    /// live by generating animator drivers.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Blendshape Link")]
    public class CVRFuryBlendshapeLink : CVRFuryComponent
    {
        public override string FeatureTitle => "Blendshape Link";

        // Run after toggles/modes/sliders so "keep live" can mirror their generated curves.
        public override int BuildPriority => 50;

        [Tooltip("Mesh whose blendshape values are the source of truth (usually the body).")]
        public SkinnedMeshRenderer sourceMesh;

        [Tooltip("Meshes that should copy matching blendshapes from the source.")]
        public List<SkinnedMeshRenderer> targetMeshes = new List<SkinnedMeshRenderer>();

        [Tooltip("Blendshape names to skip when linking.")]
        public List<string> excludeBlendshapes = new List<string>();

        [Tooltip("If true, also generate animator drivers so the link stays live for any " +
                 "blendshape that is itself animated (e.g. by a toggle). If false, values " +
                 "are copied once at build time.")]
        public bool keepLive = true;
    }
}
