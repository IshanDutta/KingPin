using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_Core
{
    [AINodeModule("Core", "Start Looping Animation")]
    public class StartLoopingAnimationNode : AIBehaviorNode
    {
        [SerializeField] AnimGraphClipInfo clip;
        [SerializeField] Mask mask = Mask.Null;
        [SerializeField] float transitionIn = 0.2f;
        [SerializeField] float transitionOut = 0.2f;

        [Header("Clip Override")]
        [SerializeField] bool takeClipFromBlackboard;

        [ShowIf("takeClipFromBlackboard", true)]
        [SerializeField] BlackboardKey clipBlackboardKey = new BlackboardKey(BlackboardValueType.AnimGraphClipInfo);

        AIController ai;

        public override void Enter(AIController ai)
        {
            this.ai = ai;
        }

        public override bool Execute()
        {
            var clipToPlay = clip;
            if (takeClipFromBlackboard
                && ai.Blackboard.TryGet<AnimGraphClipInfo>(clipBlackboardKey.Name, out var bbClip)
                && bbClip?.clip != null)
            {
                clipToPlay = bbClip;
            }

            if (clipToPlay?.clip != null)
            {
                ai.AnimGraph.PlayLoopingAnimation(clipToPlay.clip, clipToPlay, mask,
                    transitionIn: transitionIn, transitionOut: transitionOut);
            }

            return true;
        }

        public override void Exit() { }
    }
}
