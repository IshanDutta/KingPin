using FS_Core;
using FS_Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    public class MoveState : State<AIController>
    {
        private AIController aiController;

        NavMeshAgent navAgent;

        MoveStateContext moveContext;
        Vector3 lastSetDestination;
        float stuckTimer;
        const float StuckThreshold = 1f;

        public override void OnEnter(AIController owner)
        {
            aiController = owner;
            moveContext = Context as MoveStateContext;

            navAgent = aiController.NavAgent;

            // Set agent speed to the serialized chaseSpeed
            navAgent.speed = moveContext.MoveSpeed;
            navAgent.stoppingDistance = moveContext.StoppingDistance;
            lastSetDestination = Vector3.positiveInfinity;
            stuckTimer = 0f;
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }

        public override void Execute()
        {
            if (!navAgent.enabled) return;

            // dynamically update the chase speed
            if (navAgent.speed != moveContext.MoveSpeed)
                navAgent.speed = moveContext.MoveSpeed;

            var targetPos = GetTargetPos();

            if (targetPos == null) return;

            // Only update destination when it has moved significantly
            if ((targetPos.Value - lastSetDestination).sqrMagnitude > 0.1f)
            {
                navAgent.SetDestination(targetPos.Value);
                lastSetDestination = targetPos.Value;
            }

            // Detect stuck agents (not moving, path invalid or blocked)
            bool isStuck = navAgent.velocity.sqrMagnitude < 0.01f
                && navAgent.remainingDistance > navAgent.stoppingDistance;

            if (isStuck)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer >= StuckThreshold)
                    navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            }
            else
            {
                stuckTimer = 0f;
                navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            }

            // Turn in place toward the target position while standing within stopping distance
            if (moveContext.FaceTargetPosition)
            {
                bool isNearTarget = !navAgent.hasPath || navAgent.remainingDistance <= navAgent.stoppingDistance;
                var dirToTarget = targetPos.Value - aiController.transform.position;
                dirToTarget.y = 0;

                aiController.FacingDirOverride = (isNearTarget && dirToTarget.sqrMagnitude > 0.01f)
                    ? dirToTarget.normalized : (Vector3?)null;
            }

            // If we get within preferredRange, so MoveState is complete now
            if (AIUtil.HasReachedDestination(navAgent))
            {
                aiController.NavAgent.ResetPath();
                IsCompleted = true;
            }
        }

        public override void Exit()
        {
            if (navAgent.enabled && navAgent.isOnNavMesh)
                navAgent.ResetPath();
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

            if (moveContext.FaceTargetPosition)
                aiController.FacingDirOverride = null;
        }

        public Vector3? GetTargetPos()
        {
            if (!moveContext.TakeTargetPositionFromBlackboard)
                return moveContext.TargetPosition;

            if (aiController.Blackboard.TryGet<Vector3>(moveContext.TargetPositionBlackboardKey, out Vector3 targetPos))
                return targetPos;

            // If the blackboard key is not found, then complete the MoveState
            IsCompleted = true;
            return null;
        }

        public void ChangeMoveSpeed(float moveSpeed)
        {
            moveContext.MoveSpeed = moveSpeed;
        }
    }

    public class MoveStateContext
    {
        public Vector3 TargetPosition { get; set; }
        public float StoppingDistance { get; set; } = 1f;
        public float MoveSpeed { get; set; } = 4f;

        public bool TakeTargetPositionFromBlackboard { get; set; } = false;
        public string TargetPositionBlackboardKey { get; set; } = "TargetPosition";

        public bool FaceTargetPosition { get; set; } = false;
    }
}
