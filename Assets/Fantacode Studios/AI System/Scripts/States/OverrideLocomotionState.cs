using FS_Core;
using System;
using UnityEngine;

namespace FS_Core
{
    public class OverrideLocomotionState : State<AIController>
    {
        public override void OnEnter(AIController owner)
        {
            var ctx = Context as OverrideLocomotionContext;

            AnimationClip idle, walk, run;
            idle = walk = run = null;

            var bb = owner.Blackboard;
            if (!String.IsNullOrEmpty(ctx.IdleClipKey) && bb.TryGet<AnimGraphClipInfo>(ctx.IdleClipKey, out var idleInfo) && idleInfo?.clip != null)
                idle = idleInfo.clip;
            if (!String.IsNullOrEmpty(ctx.WalkClipKey) && bb.TryGet<AnimGraphClipInfo>(ctx.WalkClipKey, out var walkInfo) && walkInfo?.clip != null)
                walk = walkInfo.clip;
            if (!String.IsNullOrEmpty(ctx.RunClipKey) && bb.TryGet<AnimGraphClipInfo>(ctx.RunClipKey, out var runInfo) && runInfo?.clip != null)
                run = runInfo.clip;

            if (idle == null && walk == null && run == null) return;

            owner.AnimGraph.OverrideLocomotion(idle, walk, run, ctx.TransitionDuration);

            if (ctx.DisableTurningAnimations)
                owner.LocomotionCore.DisableTurningAnimations();

            IsCompleted = true;
        }
    }

    public class OverrideLocomotionContext
    {
        public float TransitionDuration;
        public bool DisableTurningAnimations;
        public bool TakeClipsFromBlackboard;
        public string IdleClipKey;
        public string WalkClipKey;
        public string RunClipKey;
    }

    public class ClearLocomotionOverrideState : State<AIController>
    {
        public override void OnEnter(AIController owner)
        {
            var ctx = Context as ClearLocomotionOverrideContext;

            owner.AnimGraph.ClearLocomotionOverride(ctx.TransitionDuration);

            if (ctx.ResetTurningAnimations)
                owner.LocomotionCore.ResetTurningAnimations();

            IsCompleted = true;
        }
    }

    public class ClearLocomotionOverrideContext
    {
        public float TransitionDuration;
        public bool ResetTurningAnimations;
    }
}
