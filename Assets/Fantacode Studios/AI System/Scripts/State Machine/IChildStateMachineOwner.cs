namespace FS_Core
{
    public interface IChildStateMachineOwner<T>
    {
        StateMachine<T> ChildStateMachine { get; }
    }
}
