namespace FS_Core
{
    public class DummyState : State<AIController>
    {
        AIController aiController;

        public override void OnEnter(AIController owner)
        {
            aiController = owner;
            //StartCoroutine(AsyncUtil.RunAfterDelay(0.3f, () => IsCompleted = true));
        }

        public override void Execute()
        {
            IsCompleted = true;
        }
    }
}
