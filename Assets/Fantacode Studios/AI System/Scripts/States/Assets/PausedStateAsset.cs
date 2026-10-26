using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Paused")]
    public class PausedStateAsset : StateAsset
    {
        [SerializeField] new string name = "Paused";

        public override State<AIController> Build(AIController owner) => new PausedState();

        public override string NodeTitle => name;
    }
}
