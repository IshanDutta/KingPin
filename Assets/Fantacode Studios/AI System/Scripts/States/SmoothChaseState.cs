using FS_Util;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    public class SmoothChaseState : State<AIController>
    {
        private AIController aiController;
        private NavMeshAgent navAgent;
        private Animator animator;
        private Transform target;

        private SmoothChaseStateContext context;
        private Vector3 lastSetDestination;
        private float stuckTimer;
        private const float StuckThreshold = 1f;

        public override void OnEnter(AIController owner)
        {
            aiController = owner;
            context = Context as SmoothChaseStateContext;

            if (animator == null) animator = owner.GetComponent<Animator>();

            navAgent = aiController.NavAgent;

            aiController.Blackboard.TryGet(context.TargetKey.Name, out target);

            navAgent.speed = context.MaxSpeed;
            navAgent.stoppingDistance = context.StoppingDistance;
            lastSetDestination = Vector3.positiveInfinity;
            stuckTimer = 0f;
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }

        public override void Execute()
        {
            if (navAgent == null || !navAgent.enabled || context == null)
                return;

            if (target == null)
            {
                IsCompleted = true;
                return;
            }

            Vector3 targetPos = target.position;

            if ((targetPos - lastSetDestination).sqrMagnitude > 0.1f)
            {
                navAgent.SetDestination(targetPos);
                lastSetDestination = targetPos;
            }

            ApplySmoothStartStop();

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

            if (AIUtil.HasReachedDestination(navAgent))
            {
                navAgent.ResetPath();
                IsCompleted = true;
            }
        }

        public override void Exit()
        {
            if (navAgent != null)
                navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }

        private void ApplySmoothStartStop()
        {
            float distance = navAgent.remainingDistance;
            float stoppingDistance = context.StoppingDistance;

            if (distance > stoppingDistance)
            {
                navAgent.stoppingDistance = Mathf.MoveTowards(navAgent.stoppingDistance, 0f, Time.deltaTime);

                if (distance > stoppingDistance * 2f)
                    navAgent.speed = Mathf.MoveTowards(navAgent.speed, navAgent.speed + distance * Time.deltaTime, Time.deltaTime);
                else
                    navAgent.speed = Mathf.MoveTowards(navAgent.speed, context.MinSpeed, Time.deltaTime * 5f);
            }
            else
            {
                navAgent.stoppingDistance = Mathf.MoveTowards(navAgent.stoppingDistance, stoppingDistance + 1f, Time.deltaTime * 10f);
                navAgent.speed = Mathf.MoveTowards(navAgent.speed, context.MinSpeed, Time.deltaTime * 5f);
            }

            navAgent.speed = Mathf.Clamp(navAgent.speed, context.MinSpeed, context.MaxSpeed);
        }
    }

    public class SmoothChaseStateContext
    {
        public BlackboardKey TargetKey;
        public float StoppingDistance { get; set; } = 1f;
        public float MinSpeed { get; set; } = 2f;
        public float MaxSpeed { get; set; } = 3.5f;
    }
}
