#if UNITY_EDITOR
using System;

namespace FS_Core
{
    public interface IAIStateMachinePatch
    {
        void Apply(PatchContext ctx);
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AIStateMachinePatchAttribute : Attribute
    {
        public string SystemName { get; }
        public int Order { get; }

        public AIStateMachinePatchAttribute(string systemName, int order = 100)
        {
            SystemName = systemName;
            Order = order;
        }
    }
}
#endif
