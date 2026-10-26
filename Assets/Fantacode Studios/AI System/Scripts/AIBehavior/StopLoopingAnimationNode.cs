using UnityEngine;

namespace FS_Core
{

    [AINodeModule("Core", "Stop Looping Animation")]
    public class StopLoopingAnimationNode : AIBehaviorNode
    {
        AIController ai;

        public override void Enter(AIController ai)
        {
            this.ai = ai;
        }

        public override bool Execute()
        {
            ai.AnimGraph.StopLoopingAnimations(false);
            return true;
        }

        public override void Exit() { }
    }
}
