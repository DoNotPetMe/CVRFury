using UnityEngine;

namespace CVRFury.Components
{
    /// <summary>
    /// Plays an animation while a hand holds a specific gesture (fist, open hand, point, …),
    /// reading ChilloutVR's platform-driven gesture parameters. The classic use is hand-pose props
    /// or facial expressions triggered by making a fist or a peace sign.
    /// </summary>
    [AddComponentMenu("CVRFury/CVRFury Gesture")]
    public class CVRFuryGesture : CVRFuryComponent
    {
        public override string FeatureTitle => "Gesture";

        public enum Hand { Left, Right }

        /// <summary>
        /// Declared in VRChat's familiar order for readability. The serialized value is the enum
        /// member, and the builder maps it to ChilloutVR's real gesture index (−1 … 6, where
        /// Open Hand = −1 and Fist = 1) at build time — see <c>GestureBuilder.ToCvrGestureIndex</c>.
        /// </summary>
        public enum GestureType
        {
            Neutral = 0,
            Fist = 1,
            HandOpen = 2,
            FingerPoint = 3,
            Victory = 4,
            RockAndRoll = 5,
            HandGun = 6,
            ThumbsUp = 7,
        }

        public Hand hand = Hand.Right;
        public GestureType gesture = GestureType.Fist;

        [Tooltip("Blend time when entering/leaving the gesture, in seconds.")]
        public float transitionSeconds = 0.1f;

        [Tooltip("What the avatar does while the gesture is held.")]
        public FuryState state = new FuryState();
    }
}
