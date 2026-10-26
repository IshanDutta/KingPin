namespace FS_Core
{
    public abstract class HierarchicalStateBehavior<T>
    {
        public virtual void OnStateEnter(T owner, HierarchicalState<T> state) { }
        public virtual void OnStateExecute(T owner, HierarchicalState<T> state) { }
        public virtual void OnStateExit(T owner, HierarchicalState<T> state) { }
    }
}
