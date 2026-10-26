using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace FS_Core
{
    public enum FloatComparison
    {
        Equal,
        Greater,
        Less,
        GreaterOrEqual,
        LessOrEqual,
    }

    public class BlackboardFloatCondition : IBlackboardCondition
    {
        readonly string key;
        readonly float compareValue;
        readonly FloatComparison comparison;

        public BlackboardFloatCondition(string key, float compareValue, FloatComparison comparison)
        {
            this.key = key;
            this.compareValue = compareValue;
            this.comparison = comparison;
        }

        public bool Evaluate(Blackboard blackboard)
        {
            if (!blackboard.TryGet<float>(key, out float value))
                return false;

            return comparison switch
            {
                FloatComparison.Equal => value == compareValue,
                FloatComparison.Greater => value > compareValue,
                FloatComparison.Less => value < compareValue,
                FloatComparison.GreaterOrEqual => value >= compareValue,
                FloatComparison.LessOrEqual => value <= compareValue,
                _ => false
            };
        }
    }
}
