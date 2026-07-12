using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// A placeable touch area — the "Custom" option for touch reactions. Drop it where the touch should
    /// count (parent it to a bone so it follows, e.g. the head for a nose zone), move it with the normal
    /// transform tools, and set <see cref="size"/> for the box dimensions; the magenta gizmo box in the
    /// Scene view IS the trigger area, so what you see is exactly what will fire.
    ///
    /// This is authoring intent only: creating the reaction converts it into the real CCK trigger and
    /// removes this component. A zone that's still here at upload is unused and gets stripped like every
    /// CVRFury component.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Touch Zone")]
    public class CVRFuryTouchZone : CVRFuryComponent
    {
        public override string FeatureTitle => "Touch Zone";

        [Tooltip("Box dimensions of the touch area, in local space (metres). The magenta gizmo shows it.")]
        public Vector3 size = new Vector3(0.05f, 0.05f, 0.05f);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.25f);
            Gizmos.DrawCube(Vector3.zero, size);
        }
#endif
    }
}
