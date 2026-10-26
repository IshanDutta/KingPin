using FS_Util;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    public class WanderState : State<AIController>
    {
        AIController ai;
        WanderStateContext ctx;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            ctx = Context as WanderStateContext;
            ai.NavAgent.speed = ctx.MoveSpeed;
            ai.NavAgent.stoppingDistance = ctx.ArrivalThreshold;
            SetRandomDestination();
        }

        public override void Execute()
        {
            if (AIUtil.HasReachedDestination(ai.NavAgent))
                SetRandomDestination();
        }

        public override void Exit()
        {
            ai?.NavAgent.ResetPath();
        }

        void SetRandomDestination()
        {
            Vector3 randomDir = Random.insideUnitSphere * ctx.WanderRadius;
            randomDir += ai.transform.position;
            if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, ctx.WanderRadius, NavMesh.AllAreas))
                ai.NavAgent.SetDestination(hit.position);
        }
    }

    public class WanderStateContext
    {
        public float WanderRadius { get; set; } = 30f;
        public float MoveSpeed { get; set; } = 2f;
        public float ArrivalThreshold { get; set; } = 0.5f;
    }
}
