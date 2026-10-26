namespace FS_Core
{
    public class DeadState : State<AIController>
    {
        public override void OnEnter(AIController owner)
        {
            owner.NavAgent.enabled = false;
        }
    }
}