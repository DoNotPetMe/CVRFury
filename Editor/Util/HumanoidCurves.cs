using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>
    /// Distinguishes animation curves that pose the humanoid rig (muscles, root motion, IK goals) from
    /// every other <c>Animator</c>-typed curve — most importantly VRCFury's "Animated Animator Parameter"
    /// (AAP) clips, which drive a float <em>parameter</em> through a curve whose binding type is also
    /// <see cref="Animator"/>. Binding type alone cannot tell the two apart, so we match the property
    /// name against Unity's fixed humanoid muscle list plus the root/motion/IK goal names.
    ///
    /// CVR drives locomotion, hand gestures, emotes and visemes/blink natively, so a merged VRChat layer
    /// that poses the body fights those systems and freezes the avatar (the "motorcycle pose"). Such
    /// layers are dropped on merge; AAP/object/blendshape toggle layers must NOT be, or the toggles die.
    /// </summary>
    internal static class HumanoidCurves
    {
        private static HashSet<string> _muscles;

        private static HashSet<string> Muscles
        {
            get
            {
                if (_muscles != null) return _muscles;
                _muscles = new HashSet<string>(HumanTrait.MuscleName);
                // Root motion + IK goal curves are also Animator-typed and pose/move the body.
                foreach (var p in new[] { "RootT", "RootQ", "MotionT", "MotionQ",
                                          "LeftFootT", "LeftFootQ", "RightFootT", "RightFootQ",
                                          "LeftHandT", "LeftHandQ", "RightHandT", "RightHandQ" })
                    foreach (var c in new[] { ".x", ".y", ".z", ".w" })
                        _muscles.Add(p + c);
                return _muscles;
            }
        }

        /// <summary>True if the clip animates a humanoid muscle, root-motion or IK-goal curve — i.e. it
        /// poses the rig. AAP parameter curves (same <c>Animator</c> binding type) return false.</summary>
        public static bool PosesHumanoid(AnimationClip clip)
        {
            if (clip == null) return false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Animator)) continue;
                if (string.IsNullOrEmpty(b.path) && Muscles.Contains(b.propertyName)) return true;
            }
            return false;
        }
    }
}
