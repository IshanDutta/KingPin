using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BTConditionDecorator : BTDecoratorNode
    {
        readonly IBlackboardCondition condition;
        readonly Blackboard blackboard;
        public BTConditionDecorator(IBlackboardCondition condition, IBTNode childNode, Blackboard blackboard) : base(childNode)
        {
            this.condition = condition;
            this.blackboard = blackboard;
        }

        public override BTExecutionStatus Tick()
        {
            if (!condition.Evaluate(blackboard))
            {
                IsRunning = false;
                return BTExecutionStatus.FAILURE;
            } 
            IsRunning = true;
            return childNode.Tick();
        }

    }
}
