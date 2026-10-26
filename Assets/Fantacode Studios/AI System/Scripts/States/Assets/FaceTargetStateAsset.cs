using FS_Core;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Face Target")]
    public class FaceTargetStateAsset : StateAsset
    {
        [SerializeField] string displayName = "Face Target";
        [SerializeField] BlackboardKey targetKey = new BlackboardKey(BlackboardValueType.Transform);

        public override State<AIController> Build(AIController owner)
        {
            var state = new FaceTargetState(targetKey.Name);
            state.Name = displayName;
            return state;
        }

        public override string NodeTitle => displayName;
    }
}
