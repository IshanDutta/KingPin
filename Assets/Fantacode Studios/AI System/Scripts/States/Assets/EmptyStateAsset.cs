using FS_Core;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Empty")]
    public class EmptyStateAsset : StateAsset
    {
        [SerializeField] new string name = "Empty";

        public override State<AIController> Build(AIController owner)
        {
            return new EmptyState() { Name = name };
        }

        public override string NodeTitle => name;
    }
}
