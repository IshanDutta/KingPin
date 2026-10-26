using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_StealthSystem
{
    public class AlertedIdleState : State<AIController>
    {
        AIController ai;
        Vector3? initialPos;
        Vector3? initialForward;
        AlertedIdleContext context;
        bool returningToPos;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            context = Context as AlertedIdleContext;
            returningToPos = false;

            if (initialPos == null)
            {
                initialPos = ai.transform.position;
                initialForward = ai.transform.forward;
            }

            bool returnToPos = context != null && context.ReturnToInitialPos;

            if (returnToPos)
            {
                if (Vector3.Distance(initialPos.Value, ai.transform.position) > 0.5f)
                {
                    ai.UseWalkSpeed();
                    ai.NavAgent.SetDestination(initialPos.Value);
                    returningToPos = true;
                }
                else
                {
                    ApplyInitialFacing();
                }
            }
            else
            {
                ApplyInitialFacing();
            }
        }

        public override void Execute()
        {
            if (returningToPos && AIUtil.HasReachedDestination(ai.NavAgent))
            {
                ai.NavAgent.ResetPath();
                returningToPos = false;
                ApplyInitialFacing();
            }
        }

        public override void Exit()
        {
            ai?.NavAgent.ResetPath();
            ai.FacingDirOverride = null;
        }

        void ApplyInitialFacing()
        {
            if (context != null && context.ReturnToInitialFacing && initialForward.HasValue)
                ai.FacingDirOverride = initialForward.Value;
        }
    }

    public class AlertedIdleContext
    {
        public bool ReturnToInitialPos;
        public bool ReturnToInitialFacing;
    }
}
