namespace FS_Core
{
    public class BlackboardTriggerCondition : IBlackboardCondition, IPostTransitionCondition
    {
        readonly string key;

        public BlackboardTriggerCondition(string key)
        {
            this.key = key;
        }

        public bool Evaluate(Blackboard blackboard)
        {
            return blackboard.TryGet<bool>(key, out bool value) && value;
        }

        public void OnTransitioned(Blackboard blackboard)
        {
            blackboard.Set(key, false);
        }
    }
}
