using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BTDeferredStateAction : IBTNode
    {
        State<AIController> state;
        StateMachine<AIController> stateMachine;
        AIController aIController;

        public BTDeferredStateAction(State<AIController> state, AIController aIController)
        {
            this.state = state;
            this.aIController = aIController;
            stateMachine = null;
        }

        public BTExecutionStatus Tick()
        {
            if (stateMachine == null)
                stateMachine = aIController.StateMachineAsset.GetActiveStateMachine(aIController);

            if (stateMachine.CurrentState != state)
                stateMachine.ChangeState(state);

            if (state.IsCompleted)
            {
                stateMachine = null;    // reset active state machine for next time
                IsRunning = false;
                return BTExecutionStatus.SUCCESS;
            }
            IsRunning = true;
            return BTExecutionStatus.RUNNING;
        }
        public bool IsRunning { get; set; }
    }

}
