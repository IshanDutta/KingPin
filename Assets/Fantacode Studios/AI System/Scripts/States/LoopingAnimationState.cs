using FS_Core;
using UnityEngine;

namespace FS_Core
{
    public class StartLoopingAnimationState : State<AIController>
    {
        AIController ai;
        StartLoopingAnimationContext ctx;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            ctx = Context as StartLoopingAnimationContext;

            var clip = ResolveClip();
            if (clip?.clip != null)
            {
                ai.AnimGraph.PlayLoopingAnimation(clip.clip, clip, ctx.Mask,
                    transitionIn: ctx.TransitionIn, transitionOut: ctx.TransitionOut);
            }

            IsCompleted = true;
        }

        AnimGraphClipInfo ResolveClip()
        {
            if (ctx.TakeClipFromBlackboard
                && ai.Blackboard.TryGet<AnimGraphClipInfo>(ctx.ClipBlackboardKey, out var bbClip)
                && bbClip?.clip != null)
            {
                return bbClip;
            }
            return ctx.Clip;
        }
    }

    public class StartLoopingAnimationContext
    {
        public AnimGraphClipInfo Clip;
        public Mask Mask;
        public float TransitionIn;
        public float TransitionOut;
        public bool TakeClipFromBlackboard;
        public string ClipBlackboardKey;
    }

    public class StopLoopingAnimationState : State<AIController>
    {
        public override void OnEnter(AIController owner)
        {
            var ctx = Context as StopLoopingAnimationContext;
            owner.AnimGraph.StopLoopingAnimations(ctx != null && ctx.RemoveAll);
            IsCompleted = true;
        }
    }

    public class StopLoopingAnimationContext
    {
        public bool RemoveAll;
    }
}
