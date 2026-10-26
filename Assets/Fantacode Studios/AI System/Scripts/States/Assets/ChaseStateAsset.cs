using FS_Core;
using UnityEngine;

namespace FS_StealthSystem
{
    [AIStateModule("Core", "Chase")]
    public class ChaseStateAsset : StateAsset
    {
        [SerializeField] string _name = "Chase";

        [Tooltip("Distance at which the AI stops chasing and holds position while.")]
        [SerializeField] float keepDistance = 3f;

        [Tooltip("Distance beyond which the AI runs instead of walks to close the gap with the target.")]
        [SerializeField] float runDistance = 6f;

        [Tooltip("If true, the AI rotates to face the target while chasing.")]
        [SerializeField] bool faceTarget = true;

        [SerializeField] BlackboardKey targetKey = new BlackboardKey(BlackboardValueType.Transform);

        public override State<AIController> Build(AIController owner)
        {
            var state = new ChaseState();
            state.SetContext(new ChaseStateContext
            {
                TargetKey = targetKey,
                KeepDistance = keepDistance,
                RunDistance = runDistance,
                FaceTarget = faceTarget
            });
            return state;
        }

        public override string NodeTitle => _name;
    }
}
