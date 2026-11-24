using FS_Core;
using UnityEngine;

namespace FS_CombatCore
{
    public class KnockedDownState : State<AIController>
    {
        AIController ai;
        CombatAIController combatAI;

        public override void OnEnter(AIController owner)
        {
            ai = owner;

            if (combatAI == null)
                combatAI = owner.GetComponent<CombatAIController>();

            if (combatAI.NavAgent.isActiveAndEnabled)
            {
                combatAI.NavAgent.ResetPath();
                combatAI.NavAgent.isStopped = true;
            }
        }

        public override void Execute()
        {
            if (!combatAI.Fighter.IsKnockedDown)
            {
                ai.Blackboard.Set(CombatCoreBlackboardKeys.IsKnockedDown, false);

                if (combatAI.NavAgent.isActiveAndEnabled)
                    combatAI.NavAgent.isStopped = false;

                IsCompleted = true;
            }
        }
    }
}
