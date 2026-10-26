namespace FS_Core
{
    public class ExecuteBehaviorState : State<AIController>
    {
        string blackboardKey;
        AIBehavior behavior;
        int currentNodeIndex;

        public int CurrentNodeIndex => currentNodeIndex;

        public ExecuteBehaviorState(string blackboardKey)
        {
            this.blackboardKey = blackboardKey;
        }

        public override void OnEnter(AIController owner)
        {
            currentNodeIndex = 0;

            owner.Blackboard.TryGet(blackboardKey, out behavior);
            owner.Blackboard.Set(AIBlackboardKeys.CanInterruptBehavior,
                behavior == null || behavior.Interruptible);

            if (behavior == null || behavior.NodeCount == 0)
            {
                IsCompleted = true;
                return;
            }

            behavior.GetNode(0).Enter(owner);
        }

        public override void Execute()
        {
            if (behavior == null) return;

            var node = behavior.GetNode(currentNodeIndex);

            if (node.Execute())
            {
                node.Exit();
                currentNodeIndex++;

                if (currentNodeIndex >= behavior.NodeCount)
                {
                    IsCompleted = true;
                    return;
                }

                behavior.GetNode(currentNodeIndex).Enter(Owner);
            }
        }

        public override void Exit()
        {
            if (behavior != null && currentNodeIndex < behavior.NodeCount && !IsCompleted)
            {
                behavior.GetNode(currentNodeIndex).Exit();
            }
        }
    }
}
