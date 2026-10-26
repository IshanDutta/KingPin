using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BlackboardAndCondition : IBlackboardCondition
    {
        private readonly IBlackboardCondition[] conditions;

        public BlackboardAndCondition(params IBlackboardCondition[] conditions)
        {
            //if (conditions == null || conditions.Length == 0)
            //    throw new ArgumentException("AndCondition requires at least one condition");

            this.conditions = conditions;
        }

        public bool Evaluate(Blackboard blackboard)
        {
            foreach (var condition in conditions)
            {
                if (!condition.Evaluate(blackboard))
                    return false;
            }

            return true;
        }
    }

}
