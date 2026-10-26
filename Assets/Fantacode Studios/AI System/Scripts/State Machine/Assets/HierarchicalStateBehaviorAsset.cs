using UnityEngine;

namespace FS_Core
{
    public abstract class HierarchicalStateBehaviorAsset : ScriptableObject
    {
        public abstract HierarchicalStateBehavior<AIController> Build(AIController owner);
        public virtual string DisplayName => GetType().Name;
    }
}
