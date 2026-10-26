using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Run From Target")]
    public class RunFromTargetStateAsset : StateAsset
    {
        [SerializeField] string _name = "Run From Target";

        [Tooltip("Only flee when the target is within this distance")]
        [SerializeField] float triggerDistance = 10f;

        [Tooltip("How far from the target the flee point is placed")]
        [SerializeField] float fleeDistance = 8f;

        [Tooltip("Minimum acceptable distance from target for the chosen point")]
        [SerializeField] float minSafeDistance = 6f;

        [Tooltip("Lateral offset for fallback flee points")]
        [SerializeField] float lateralOffset = 3f;

        [SerializeField] float moveSpeed = 3.5f;

        public override State<AIController> Build(AIController owner)
        {
            var context = new RunFromTargetStateContext
            {
                TriggerDistance = triggerDistance,
                FleeDistance = fleeDistance,
                MinSafeDistance = minSafeDistance,
                LateralOffset = lateralOffset,
                MoveSpeed = moveSpeed,
            };
            var state = new RunFromTargetState();
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => _name;
    }
}
