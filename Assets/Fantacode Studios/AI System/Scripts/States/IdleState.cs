using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_Core
{
    public class IdleState : State<AIController>
    {
        public bool ReturnToInitialPos { get; set; } = false;
        public bool ReturnToInitialFacing { get; set; } = false;

        Vector3? initialPos = null;
        Vector3? initialForward = null;
        bool returningToPos;

        AIController ai;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            returningToPos = false;

            if (initialPos == null)
            {
                initialPos = ai.transform.position;
                initialForward = ai.transform.forward;
            }

            if (ReturnToInitialPos)
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
            if (ReturnToInitialFacing && initialForward.HasValue)
                ai.FacingDirOverride = initialForward.Value;
        }
    }
}
