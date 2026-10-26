using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Wander")]
    public class WanderStateAsset : StateAsset
    {
        [SerializeField] string _name = "Wander";
        [SerializeField] float wanderRadius = 30f;
        [SerializeField] float moveSpeed = 2f;
        [SerializeField] float arrivalThreshold = 0.5f;

        public override State<AIController> Build(AIController owner)
        {
            var context = new WanderStateContext
            {
                WanderRadius = wanderRadius,
                MoveSpeed = moveSpeed,
                ArrivalThreshold = arrivalThreshold,
            };
            var state = new WanderState();
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => _name;
    }
}
