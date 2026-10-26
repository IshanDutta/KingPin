using System;
using UnityEngine;

namespace FS_Core
{
    [Serializable]
    [AINodeModule("Core", "Set Blackboard Value")]
    public class SetBlackboardValueNode : AIBehaviorNode
    {
        [SerializeField] AIController targetAI;
        [SerializeField] BlackboardKey key = new BlackboardKey(BlackboardValueType.Bool);
        [SerializeField] bool value;

        AIController self;

        public override void Enter(AIController ai) { self = ai; }

        public override bool Execute()
        {
            var ai = targetAI != null ? targetAI : self;
            ai.Blackboard.Set(key.Name, value);
            return true;
        }

        public override void Exit() { }
    }
}
