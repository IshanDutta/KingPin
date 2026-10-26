namespace FS_Core
{
    public class BlackboardStateMachineState : State<AIController>, IChildStateMachineOwner<AIController>
    {
        string blackboardKey;
        StateMachineAsset fallback;
        StateMachineAsset lastBuiltAsset;
        StateMachine<AIController> childStateMachine;

        public StateMachine<AIController> ChildStateMachine => childStateMachine;

        public BlackboardStateMachineState(string blackboardKey, StateMachineAsset fallback)
        {
            this.blackboardKey = blackboardKey;
            this.fallback = fallback;
        }

        public override void OnEnter(AIController owner)
        {
            Owner = owner;
            IsCompleted = false;

            StateMachineAsset smAsset = null;
            owner.Blackboard.TryGet(blackboardKey, out smAsset);
            if (smAsset == null) smAsset = fallback;

            if (smAsset == null)
            {
                IsCompleted = true;
                return;
            }

            if (smAsset != lastBuiltAsset)
            {
                childStateMachine = smAsset.Build(owner);
                lastBuiltAsset = smAsset;
            }

            childStateMachine.EnterInitialState();
        }

        public override void Execute()
        {
            if (childStateMachine == null) return;
            childStateMachine.Execute();

            if (childStateMachine.CurrentState != null
                && childStateMachine.CurrentState.IsCompleted
                && !childStateMachine.HasValidTransition())
                IsCompleted = true;
        }

        public override void Exit()
        {
            childStateMachine?.CurrentState?.Exit();
        }
    }
}
