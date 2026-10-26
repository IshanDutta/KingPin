using FS_Core;
using UnityEngine;

namespace FS_Core
{
    [System.Serializable]
    [AINodeModule("Core", "Move")]
    public class MoveNode : AIBehaviorNode
    {
        [SerializeField] Transform targetTransform;
        [SerializeField] float moveSpeed = 4f;
        [SerializeField] float stoppingDistance = 1f;

        MoveState moveState;
        MoveStateContext moveContext;

        public override void Enter(AIController ai)
        {
            moveContext = new MoveStateContext()
            {
                TargetPosition = GetTargetPosition(),
                TakeTargetPositionFromBlackboard = false,
                StoppingDistance = stoppingDistance,
                MoveSpeed = moveSpeed,
            };

            moveState = new MoveState();
            moveState.SetContext(moveContext);
            moveState.Enter(ai);
        }

        public override bool Execute()
        {
            if (targetTransform != null)
                moveContext.TargetPosition = GetTargetPosition();

            moveState.Execute();
            return moveState.IsCompleted;
        }

        public override void Exit()
        {
            moveState.Exit();
        }

        Vector3 GetTargetPosition()
        {
            return targetTransform.position;
        }
    }
}
