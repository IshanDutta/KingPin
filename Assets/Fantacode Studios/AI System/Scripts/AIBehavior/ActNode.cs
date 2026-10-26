using FS_Util;
using UnityEngine;

namespace FS_Core
{
    [System.Serializable]
    [AINodeModule("Core", "Act")]
    public class ActNode : AIBehaviorNode
    {
        [SerializeField] AnimGraphClipInfo actionClip;
        [SerializeField] bool useDirectAudio;
        [SerializeField, ShowIf("useDirectAudio", true)] DialogueAudio directAudio;
        [SerializeField, ShowIf("useDirectAudio", false)] DialogueBarkSet dialogueBarkSet;
        [SerializeField] bool waitForDialogue = true;

        ActState actState;

        public override void Enter(AIController ai)
        {
            var context = new ActStateContext
            {
                ActionClip = actionClip,
                DirectAudio = useDirectAudio ? directAudio : null,
                DialogueBarkSet = useDirectAudio ? null : dialogueBarkSet,
                WaitForDialogue = waitForDialogue,
            };

            actState = new ActState();
            actState.SetContext(context);
            actState.Enter(ai);
        }

        public override bool Execute()
        {
            actState.Execute();
            return actState.IsCompleted;
        }

        public override void Exit()
        {
            actState.Exit();
        }
    }
}
