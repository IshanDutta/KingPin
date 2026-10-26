using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BlackboardOrCondition : IBlackboardCondition
    {
        private readonly IBlackboardCondition[] conditions;

        public BlackboardOrCondition(params IBlackboardCondition[] conditions)
        {
            //if (conditions == null || conditions.Length == 0)
            //    throw new ArgumentException("OrCondition requires at least one condition");

            this.conditions = conditions;
        }

        public bool Evaluate(Blackboard blackboard)
        {
            foreach (var condition in conditions)
            {
                if (condition.Evaluate(blackboard))
                    return true;
            }

            return false;
        }
    }

}
