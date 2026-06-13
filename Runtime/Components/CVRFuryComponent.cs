using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Base class for every CVRFury feature. A feature is a plain MonoBehaviour you
    /// drop onto your avatar (or any child of it). At build/upload time the matching
    /// <c>FeatureBuilder</c> in the editor assembly reads this configuration and bakes
    /// it into the avatar's ChilloutVR Advanced Avatar Settings / animators.
    ///
    /// CVRFury components are <b>editor-only intent</b>: they carry no runtime logic and
    /// are stripped from the avatar before it is uploaded, exactly like VRCFury.
    /// </summary>
    public abstract class CVRFuryComponent : MonoBehaviour, IEditorOnly
    {
        /// <summary>Bumped whenever a component's serialized layout changes, so the
        /// editor can run migrations on older assets.</summary>
        public const int CurrentSchemaVersion = 1;

        [HideInInspector] public int schemaVersion = CurrentSchemaVersion;

        /// <summary>Human-readable name shown in the inspector header.</summary>
        public abstract string FeatureTitle { get; }

        /// <summary>
        /// Lower numbers build first. Most features are 0; structural features that
        /// reshape the hierarchy (Armature Link, Object State deletions) run earlier
        /// so later features see the final hierarchy.
        /// </summary>
        public virtual int BuildPriority => 0;
    }

    /// <summary>
    /// Informational marker: components implementing this are editor-only intent and must
    /// never ship inside an uploaded bundle. CVRFury removes every
    /// <see cref="CVRFuryComponent"/> from the build instance during the bake (see
    /// <c>CVRFuryBuilder.StripComponents</c>), so nothing here reaches the upload.
    /// </summary>
    public interface IEditorOnly { }
}
