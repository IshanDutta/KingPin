using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Execute Behavior")]
    public class ExecuteBehaviorStateAsset : StateAsset
    {
        [SerializeField] string displayName = "Execute Behavior";
        [SerializeField] BlackboardKey behaviorKey = new BlackboardKey(BlackboardValueType.AIBehavior);

        public string BehaviorKeyName => behaviorKey.Name;

        public override State<AIController> Build(AIController owner)
        {
            var state = new ExecuteBehaviorState(behaviorKey.Name);
            state.Name = displayName;
            return state;
        }

        public override string NodeTitle => displayName;
        public override Color NodeColor => new Color(0.3f, 0.5f, 0.4f, 1f);
        public override bool CanCreateMultiple => true;
    }
}
