using FS_Core;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Turn To Face")]
    public class TurnToFaceStateAsset : StateAsset
    {
        [SerializeField] string displayName = "Turn To Face";
        [SerializeField] BlackboardKey positionKey = new BlackboardKey(BlackboardValueType.Vector3);
        [SerializeField] float angleTolerance = 10f;

        [Tooltip("Minimum angle to the target required to trigger turning. Below this, the state completes immediately.")]
        [SerializeField] float triggerThreshold = 0f;

        public override State<AIController> Build(AIController owner)
        {
            var state = new TurnToFaceState(positionKey.Name, angleTolerance, triggerThreshold);
            state.Name = displayName;
            return state;
        }

        public override string NodeTitle => displayName;
    }
}
