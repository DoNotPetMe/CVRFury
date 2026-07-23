using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// A placement marker for the nipple-bump generator: position it on the clothing where the poke should
    /// be, and the pink gizmo sphere shows the exact area (its radius) that will be pushed outward. Purely an
    /// authoring aid — it carries no runtime behaviour and is deleted once the bump is baked.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Nipple Marker")]
    public class CVRFuryNippleMarker : MonoBehaviour
    {
        [Tooltip("Radius (metres) of the area pushed outward — the pink sphere.")]
        public float radius = 0.03f;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = new Color(1f, 0.35f, 0.6f, 0.25f);
            Gizmos.DrawSphere(transform.position, radius * 0.15f); // the peak
        }
#endif
    }
}
