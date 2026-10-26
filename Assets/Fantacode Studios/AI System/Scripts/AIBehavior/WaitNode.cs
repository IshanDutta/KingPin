using FS_Util;
using UnityEngine;

namespace FS_Core
{
    [System.Serializable]
    [AINodeModule("Core", "Wait")]
    public class WaitNode : AIBehaviorNode
    {
        [SerializeField] bool waitForever;
        [SerializeField, ShowIf("waitForever", false)] float duration = 1f;

        float timer;

        public override void Enter(AIController ai)
        {
            timer = 0f;
        }

        public override bool Execute()
        {
            if (waitForever) return false;

            timer += Time.deltaTime;
            return timer >= duration;
        }

        public override void Exit() { }
    }
}
