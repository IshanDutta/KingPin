using FS_Util;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Patrol")]
    public class PatrolStateAsset : StateAsset
    {
        [SerializeField] string _name = "Patrol";
        [SerializeField] BlackboardKey waypointsKey = new BlackboardKey(BlackboardValueType.Transform);

        [SerializeField] bool takeMoveSpeedFromBlackboard;

        [ShowIf("takeMoveSpeedFromBlackboard", false)]
        [SerializeField] float moveSpeed = 2f;

        [ShowIf("takeMoveSpeedFromBlackboard", true)]
        [SerializeField] BlackboardKey moveSpeedKey = new BlackboardKey(BlackboardValueType.Float);

        [SerializeField] float arrivalThreshold = 0.5f;
        [SerializeField] bool loop = true;

        public override State<AIController> Build(AIController owner)
        {
            var context = new PatrolStateContext
            {
                WaypointsKey = waypointsKey,
                MoveSpeed = moveSpeed,
                TakeMoveSpeedFromBlackboard = takeMoveSpeedFromBlackboard,
                MoveSpeedKey = moveSpeedKey,
                ArrivalThreshold = arrivalThreshold,
                Loop = loop,
            };
            var state = new PatrolState();
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => _name;
    }
}
