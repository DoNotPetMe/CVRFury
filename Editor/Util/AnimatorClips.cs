using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Enumerates every <see cref="AnimationClip"/> referenced by a controller, walking
    /// state machines, sub-state-machines and blend trees.</summary>
    internal static class AnimatorClips
    {
        public static HashSet<AnimationClip> GetAll(AnimatorController c)
        {
            var set = new HashSet<AnimationClip>();
            if (c == null) return set;
            foreach (var layer in c.layers)
                CollectStateMachine(layer.stateMachine, set);
            return set;
        }

        private static void CollectStateMachine(AnimatorStateMachine sm, HashSet<AnimationClip> set)
        {
            if (sm == null) return;
            foreach (var cs in sm.states)
                CollectMotion(cs.state.motion, set);
            foreach (var child in sm.stateMachines)
                CollectStateMachine(child.stateMachine, set);
        }

        private static void CollectMotion(Motion m, HashSet<AnimationClip> set)
        {
            switch (m)
            {
                case AnimationClip clip:
                    set.Add(clip);
                    break;
                case BlendTree tree:
                    foreach (var ch in tree.children)
                        CollectMotion(ch.motion, set);
                    break;
            }
        }
    }
}
