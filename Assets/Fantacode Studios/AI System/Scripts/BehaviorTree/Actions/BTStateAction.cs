namespace FS_Core
{
    public class BTStateAction : IBTNode
    {
        State<AIController> state;
        HierarchicalState<AIController> parentState;
        AIController owner;

        public BTStateAction(State<AIController> state, HierarchicalState<AIController> parentState,
            AIController owner)
        {
            this.state = state;
            this.owner = owner;
            this.parentState = parentState;
        }

        public BTExecutionStatus Tick()
        {
            var stateMachine = parentState.ChildStateMachine;

            if (stateMachine.CurrentState != state)
                stateMachine.ChangeState(state);

            stateMachine.CanExecute = () => true;

            if (state.IsCompleted)
            {
                IsRunning = false;
                return BTExecutionStatus.SUCCESS;
            }


            return BTExecutionStatus.RUNNING;
        }
        public bool IsRunning { get; set; }

    }
}
