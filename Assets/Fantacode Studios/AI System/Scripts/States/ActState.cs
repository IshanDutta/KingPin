using System.Collections;
using UnityEngine;

namespace FS_Core
{
    public class ActState : State<AIController>
    {
        AIController ai;
        ActStateContext actContext;

        CharacterAction currentAction;

        public override void OnEnter(AIController owner)
        {
            ai = owner;

            actContext = Context as ActStateContext;

            ai.StartCoroutine(Act());
        }

        IEnumerator Act()
        {
            // Resolve clip: prefer blackboard override, fall back to direct clip
            var clipToPlay = actContext.ActionClip;
            if (actContext.TakeActionClipFromBlackboard
                && ai.Blackboard.TryGet<AnimGraphClipInfo>(actContext.ActionClipBlackboardKey, out var bbClip)
                && bbClip?.clip != null)
            {
                clipToPlay = bbClip;
            }

            if (clipToPlay?.clip != null)
            {
                currentAction = new CharacterAction()
                {
                    clipInfo = clipToPlay,
                    animationParameters = new AnimParameters()
                    {
                        OnComplete = () => IsCompleted = true
                    }
                };

                ai.ActionExecutor.Execute(currentAction);
            }

            if (actContext.DirectAudio?.clip != null)
            {
                ai.Speak(actContext.DirectAudio);
                if (actContext.WaitForDialogue)
                    yield return new WaitForSeconds(actContext.DirectAudio.clip.length);
            }
            else if (actContext.DialogueBarkSet != null)
            {
                var audio = actContext.DialogueBarkSet.Pick();
                if (audio != null)
                {
                    ai.Speak(audio);
                    if (actContext.WaitForDialogue)
                        yield return new WaitForSeconds(audio.clip.length);
                }
            }

            // If no action needs to be played, mark as completed immediately
            if (clipToPlay?.clip == null)
                IsCompleted = true;

            yield break;
        }

        public override void Exit()
        {
            if (!IsCompleted)
            {
                ai.ActionExecutor.Cancel(currentAction);
            }

            currentAction = null;
        }
    }

    public class ActStateContext
    {
        public AnimGraphClipInfo ActionClip;
        public DialogueBarkSet DialogueBarkSet;
        public DialogueAudio DirectAudio;
        public bool WaitForDialogue;
        public bool TakeActionClipFromBlackboard;
        public string ActionClipBlackboardKey;
    }
}
