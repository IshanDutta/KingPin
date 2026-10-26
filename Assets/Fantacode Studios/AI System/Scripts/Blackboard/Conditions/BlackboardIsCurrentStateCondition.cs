using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BlackboardIsCurrentStateCondition<T> : IBlackboardCondition
    {
        private readonly StateMachine<T> stateMachine;
        private readonly State<T> state;

        public BlackboardIsCurrentStateCondition(StateMachine<T> stateMachine, State<T> state)
        {
            this.stateMachine = stateMachine;
            this.state = state;
        }

        public bool Evaluate(Blackboard blackboard)
        {
            return stateMachine.CurrentState == state;
        }
    }
}
