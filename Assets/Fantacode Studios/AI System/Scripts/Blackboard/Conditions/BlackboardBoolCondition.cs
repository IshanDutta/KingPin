using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BlackboardBoolCondition : IBlackboardCondition
    {
        readonly string key;
        readonly bool expected;

        public BlackboardBoolCondition(string key, bool expected)
        {
            this.key = key;
            this.expected = expected;
        }

        public bool Evaluate(Blackboard blackboard)
        {
            return blackboard.TryGet<bool>(key, out bool value) && value == expected;
        }
    }
}
