using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Blackboard State Machine")]
    public class BlackboardStateMachineStateAsset : StateAsset
    {
        [SerializeField] string displayName = "Blackboard SM";
        [SerializeField] BlackboardKey stateMachineKey = new BlackboardKey(BlackboardValueType.Object);
        [SerializeField] StateMachineAsset fallbackStateMachineAsset;

        public override State<AIController> Build(AIController owner)
        {
            var state = new BlackboardStateMachineState(stateMachineKey.Name, fallbackStateMachineAsset);
            state.Name = displayName;
            return state;
        }

        public override string NodeTitle => displayName;
        public override Color NodeColor => new Color(0.4f, 0.3f, 0.6f, 1f);
        public override bool CanCreateMultiple => true;
    }
}
