using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Wait")]
    public class WaitStateAsset : StateAsset
    {
        [SerializeField] bool waitForever;
        [SerializeField, ShowIf("waitForever", false)] float duration = 5f;

        public override State<AIController> Build(AIController owner)
        {
            return new WaitState(duration, waitForever);
        }

        public override string NodeTitle => waitForever ? "Wait (Forever)" : $"Wait ({duration}s)";
    }
}
