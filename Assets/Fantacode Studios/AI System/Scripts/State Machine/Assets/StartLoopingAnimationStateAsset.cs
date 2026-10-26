using FS_Util;
using UnityEngine;

namespace FS_Core
{

    [AIStateModule("Core", "Start Looping Animation")]
    public class StartLoopingAnimationStateAsset : StateAsset
    {
        [SerializeField] new string name = "Start Looping Animation";

        [SerializeField] AnimGraphClipInfo clip;
        [SerializeField] Mask mask = Mask.Null;
        [SerializeField] float transitionIn = 0.2f;
        [SerializeField] float transitionOut = 0.2f;

        [Header("Clip Override")]
        [SerializeField] bool takeClipFromBlackboard;

        [ShowIf("takeClipFromBlackboard", true)]
        [SerializeField] BlackboardKey clipBlackboardKey = new BlackboardKey(BlackboardValueType.AnimGraphClipInfo);

        public override State<AIController> Build(AIController owner)
        {
            var state = new StartLoopingAnimationState();
            var context = new StartLoopingAnimationContext
            {
                Clip = clip,
                Mask = mask,
                TransitionIn = transitionIn,
                TransitionOut = transitionOut,
                TakeClipFromBlackboard = takeClipFromBlackboard,
                ClipBlackboardKey = clipBlackboardKey.Name,
            };
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => name;
    }
}
