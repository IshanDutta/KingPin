using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    public enum FleePositionCalculationType { BestPosition, Simple }

    public class FleeState : State<AIController>
    {
        AIController ai;
        NavMeshAgent navAgent;
        FleeStateContext ctx;

        static readonly float[] angles =
        {
            0f,
            30f, -30f,
            60f, -60f,
            90f, -90f,
            135f, -135f,
            180f
        };

        Transform threat;
        bool destinationSet;
        float nextRepathTime;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            ctx = Context as FleeStateContext;
            navAgent = ai.NavAgent;

            ai.UseRunSpeed();
            destinationSet = false;
            threat = ai.Target;

            if (navAgent != null)
                navAgent.isStopped = false;

            nextRepathTime = Time.time;
        }

        public override void Execute()
        {
            if (IsCompleted)
                return;

            if (threat == null)
            {
                IsCompleted = true;
                return;
            }

            float distanceToThreat = Vector3.Distance(ai.transform.position, threat.position);

            if (distanceToThreat >= ctx.SafeDistance)
            {
                IsCompleted = true;
                return;
            }

            if (!destinationSet || Time.time >= nextRepathTime)
            {
                SetFleeDestination();
                nextRepathTime = Time.time + ctx.RepathInterval;
            }
        }

        public override void Exit()
        {
            destinationSet = false;

            if (navAgent != null)
                navAgent.ResetPath();
        }

        void SetFleeDestination()
        {
            if (navAgent == null || threat == null)
                return;

            Vector3 fleePos;

            if (ctx.CalculationType == FleePositionCalculationType.BestPosition)
                fleePos = FindBestFleePosition();
            else
                fleePos = FindSimpleFleePosition();

            navAgent.SetDestination(fleePos);
            destinationSet = true;
        }

        Vector3 FindBestFleePosition()
        {
            var aiPos = ai.transform.position;
            var threatPos = threat.position;

            Vector3 baseDir = (aiPos - threatPos).normalized;
            float bestScore = float.MinValue;
            Vector3 bestPos = aiPos;

            foreach (float angle in angles)
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * baseDir;
                Vector3 testPos = aiPos + dir * ctx.FleeDistance;

                if (!NavMesh.SamplePosition(testPos, out NavMeshHit hit, ctx.FleeDistance, NavMesh.AllAreas))
                    continue;

                float score = Vector3.Distance(hit.position, threatPos);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = hit.position;
                }
            }

            return bestPos;
        }

        Vector3 FindSimpleFleePosition()
        {
            Vector3 fleeDir = (ai.transform.position - threat.position).normalized;
            Vector3 desiredPos = ai.transform.position + fleeDir * ctx.FleeDistance;

            if (NavMesh.SamplePosition(desiredPos, out NavMeshHit hit, ctx.FleeDistance, NavMesh.AllAreas))
                return hit.position;

            return desiredPos;
        }
    }

    public class FleeStateContext
    {
        public float FleeDistance { get; set; } = 12f;
        public float SafeDistance { get; set; } = 18f;
        public float RepathInterval { get; set; } = 2f;
        public FleePositionCalculationType CalculationType { get; set; }
    }
}
