namespace FS_Core
{
    [System.Serializable]
    public abstract class AIBehaviorNode
    {
        public abstract void Enter(AIController ai);
        public abstract bool Execute();
        public abstract void Exit();
    }
}
