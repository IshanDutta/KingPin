namespace FS_Core
{
    public class BlackboardIsSetCondition : IBlackboardCondition
    {
        readonly string key;
        readonly bool expectedIsSet;

        public BlackboardIsSetCondition(string key, bool expectedIsSet)
        {
            this.key = key;
            this.expectedIsSet = expectedIsSet;
        }

        public bool Evaluate(Blackboard blackboard)
        {
            return blackboard.Has(key) == expectedIsSet;
        }
    }
}
