using FS_Core;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    public class ChaseState : State<AIController>
    {
        AIController ai;
        ChaseStateContext chaseContext;
        NavMeshAgent navAgent;
        Transform target;

        float keepDistance;
        float runDistance;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            navAgent = ai.NavAgent;
            chaseContext = Context as ChaseStateContext;
            
            ai.Blackboard.TryGet(chaseContext.TargetKey.Name, out target);
            keepDistance = chaseContext.KeepDistance;
            runDistance = chaseContext.RunDistance;

            // Ensure the agent is on a Navmesh before calling SetDestination
            navAgent.Warp(navAgent.transform.position);
            navAgent.stoppingDistance = keepDistance;
            navAgent.speed = ai.WalkSpeed;
            if (IsAgentReady())
                navAgent.SetDestination(target.position);
            if (chaseContext.FaceTarget) UpdateFacing();
        }

        public override void Execute()
        {
            if (target == null) return;

            if (chaseContext.FaceTarget) UpdateFacing();
            
            UpdateMovement();
        }

        public override void Exit()
        {
            if (chaseContext.FaceTarget) ai.FacingDirOverride = null;
            if (IsAgentReady())
                navAgent.ResetPath();
            navAgent.stoppingDistance = 0f;
        }

        void UpdateMovement()
        {
            // Skip path commands while the agent is detached from the NavMesh
            // (e.g., the swimming AI is mid-dive). Calling SetDestination /
            // ResetPath in that window throws.
            if (!IsAgentReady()) return;

            float dist = Vector3.Distance(ai.transform.position, target.position);

            if (dist <= keepDistance)
            {
                navAgent.ResetPath();
            }
            else
            {
                navAgent.SetDestination(target.position);
                navAgent.speed = dist >= runDistance ? ai.RunSpeed : ai.WalkSpeed;
            }
        }

        bool IsAgentReady() => navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh;

        void UpdateFacing()
        {
            var toPlayer = target.position - ai.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
            {
                ai.FacingDirOverride = toPlayer.normalized;
            }
        }
    }

    public class ChaseStateContext
    {
        public float KeepDistance;
        public float RunDistance;
        public BlackboardKey TargetKey;
        public bool FaceTarget = true;
    }
}
