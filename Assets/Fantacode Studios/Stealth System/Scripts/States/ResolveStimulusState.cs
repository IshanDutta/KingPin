using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    public class ResolveStimulusState : State<AIController>
    {
        StealthAIController stealthAI;

        public override void OnEnter(AIController owner)
        {
            if (stealthAI == null)
                stealthAI = owner.GetComponent<StealthAIController>();

            StealthAIManager.Instance.ResolveStimulus(stealthAI.ActiveStimulus);
            owner.Blackboard.Set<bool>(StealthBlackboardKeys.Investigate, false);

            IsCompleted = true;
        }
    }
}
