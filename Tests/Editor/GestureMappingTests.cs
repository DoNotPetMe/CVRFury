using CVRFury.Builder;
using CVRFury.Components;
using NUnit.Framework;

namespace CVRFury.Tests
{
    /// <summary>
    /// The Gesture feature must key on ChilloutVR's gesture indices (GestureLeftIdx /
    /// GestureRightIdx, −1 … 6 per docs.chilloutvr.net), NOT VRChat's 0–7 order the enum
    /// members are declared in. A wrong mapping fires gestures on the wrong hand pose —
    /// or never (VRChat's ThumbsUp=7 doesn't exist in CVR's range).
    /// </summary>
    public class GestureMappingTests
    {
        [Test]
        public void MapsEveryGestureToItsCvrIndex()
        {
            Assert.AreEqual(0, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.Neutral));
            Assert.AreEqual(1, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.Fist));
            Assert.AreEqual(-1, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.HandOpen));
            Assert.AreEqual(4, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.FingerPoint));
            Assert.AreEqual(5, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.Victory));
            Assert.AreEqual(6, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.RockAndRoll));
            Assert.AreEqual(3, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.HandGun));
            Assert.AreEqual(2, GestureBuilder.ToCvrGestureIndex(CVRFuryGesture.GestureType.ThumbsUp));
        }

        [Test]
        public void AllIndicesAreWithinCvrRange()
        {
            foreach (CVRFuryGesture.GestureType g in
                     System.Enum.GetValues(typeof(CVRFuryGesture.GestureType)))
            {
                var idx = GestureBuilder.ToCvrGestureIndex(g);
                Assert.GreaterOrEqual(idx, -1, $"{g} maps below CVR's range");
                Assert.LessOrEqual(idx, 6, $"{g} maps above CVR's range");
            }
        }
    }
}
