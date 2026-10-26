using FS_Core;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Set Blackboard Value")]
    public class SetBlackboardValueStateAsset : StateAsset
    {
        [SerializeField] List<BlackboardInitValue> values = new List<BlackboardInitValue>();

        public override State<AIController> Build(AIController owner)
        {
            return new SetBlackboardValueState(values);
        }

        public override string NodeTitle => "Set Blackboard Value";
    }
}
