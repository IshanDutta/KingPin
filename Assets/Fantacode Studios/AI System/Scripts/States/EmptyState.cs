using FS_Core;

namespace FS_Core
{
    // Note: Name is set by EmptyStateAsset.Build so the state shows up correctly
    // in debug logs and editor diagnostics.
    public class EmptyState : State<AIController>
    {
        public override void OnEnter(AIController owner)
        {
        }

        public override void Execute()
        {
            IsCompleted = true;
        }

        public override void Exit()
        {
        }
    }
}
