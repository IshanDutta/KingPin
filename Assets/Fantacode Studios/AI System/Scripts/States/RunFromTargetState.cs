using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    public class RunFromTargetState : State<AIController>
    {
        AIController ai;
        RunFromTargetStateContext ctx;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            ctx = Context as RunFromTargetStateContext;
            ai.NavAgent.speed = ctx.MoveSpeed;
        }

        public override void Execute()
        {
            if (ai.Target == null) return;

            Vector3 dir = ai.transform.position - ai.Target.position;
            if (dir.magnitude > ctx.TriggerDistance) return;

            dir.Normalize();
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            Vector3 forward = ai.Target.position + dir * ctx.FleeDistance;

            if (TrySample(forward, out Vector3 result)
                && DistToTarget(result) >= ctx.MinSafeDistance)
            {
                ai.NavAgent.SetDestination(result);
                return;
            }

            if (TrySample(forward + right * ctx.LateralOffset, out result)
                && DistToTarget(result) >= ctx.MinSafeDistance)
            {
                ai.NavAgent.SetDestination(result);
                return;
            }

            if (TrySample(forward - right * ctx.LateralOffset, out result))
            {
                ai.NavAgent.SetDestination(result);
            }
        }

        public override void Exit()
        {
            ai?.NavAgent.ResetPath();
        }

        bool TrySample(Vector3 pos, out Vector3 result)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, ctx.FleeDistance, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
            result = pos;
            return false;
        }

        float DistToTarget(Vector3 pos)
        {
            return (pos - ai.Target.position).magnitude;
        }
    }

    public class RunFromTargetStateContext
    {
        public float TriggerDistance { get; set; } = 10f;
        public float FleeDistance { get; set; } = 8f;
        public float MinSafeDistance { get; set; } = 6f;
        public float LateralOffset { get; set; } = 3f;
        public float MoveSpeed { get; set; } = 3.5f;
    }
}
