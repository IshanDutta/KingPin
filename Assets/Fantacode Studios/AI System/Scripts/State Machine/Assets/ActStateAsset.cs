using FS_Util;
using UnityEngine;

namespace FS_Core
{

    [AIStateModule("Core", "Act")]
    public class ActStateAsset : StateAsset
    {
        [SerializeField] new string name = "Act";

        [SerializeField] AnimGraphClipInfo actionClip;
        [SerializeField] DialogueBarkSet dialogueBarkSet;
        [SerializeField] bool waitForDialogue = true;

        [Header("Animation Override")]
        [SerializeField] bool takeActionClipFromBlackboard;

        [ShowIf("takeActionClipFromBlackboard", true)]
        [SerializeField] BlackboardKey actionClipBlackboardKey = new BlackboardKey(BlackboardValueType.AnimGraphClipInfo);

        public override State<AIController> Build(AIController owner)
        {
            var actState = new ActState();
            var context = new ActStateContext
            {
                ActionClip = actionClip,
                DialogueBarkSet = dialogueBarkSet,
                WaitForDialogue = waitForDialogue,
                TakeActionClipFromBlackboard = takeActionClipFromBlackboard,
                ActionClipBlackboardKey = actionClipBlackboardKey.Name,
            };
            actState.SetContext(context);

            return actState;
        }

        public override string NodeTitle => name;
    }
}