using FS_Core;
using FS_Util;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Move")]
    public class MoveStateAsset : StateAsset
    {
        [SerializeField] new string name = "Move";

        [SerializeField] bool takeTargetPosFromBlackboard;

        [ShowIf("takeTargetPosFromBlackboard", false)]
        [SerializeField] Vector3 targetPosition;

        [ShowIf("takeTargetPosFromBlackboard", true)]
        [SerializeField] BlackboardKey blackboardKey = new BlackboardKey(BlackboardValueType.Vector3);

        [SerializeField] float stoppingDistance = 1f;

        [SerializeField] float moveSpeed = 4f;

        [Tooltip("Turn in place to keep facing the target position while standing within stopping distance")]
        [SerializeField] bool faceTargetPosition = false;

        public override State<AIController> Build(AIController owner)
        {
            var moveContext = new MoveStateContext()
            {
                TargetPosition = targetPosition,
                TakeTargetPositionFromBlackboard = takeTargetPosFromBlackboard,
                TargetPositionBlackboardKey = blackboardKey.Name,
                StoppingDistance = stoppingDistance,
                MoveSpeed = moveSpeed,
                FaceTargetPosition = faceTargetPosition,
            };
            var moveState = new MoveState();
            moveState.SetContext(moveContext);
            return moveState;
        }

        public override string NodeTitle => name;
    }
}
