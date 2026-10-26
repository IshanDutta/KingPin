namespace FS_Core
{
    public interface IPostTransitionCondition
    {
        void OnTransitioned(Blackboard blackboard);
    }
}
