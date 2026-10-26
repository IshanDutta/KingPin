using FS_Core;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class SetBlackboardValueState : State<AIController>
    {
        List<BlackboardInitValue> values;
        AIController ai;

        public SetBlackboardValueState(List<BlackboardInitValue> values)
        {
            this.values = values;
        }

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            foreach (var value in values)
                value.Apply(ai.Blackboard);

            IsCompleted = true;
        }

        public override void Execute()
        {
        }

        public override void Exit()
        {
        }
    }
}
