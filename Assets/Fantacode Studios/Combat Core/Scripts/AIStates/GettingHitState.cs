using FS_Core;

namespace FS_CombatCore
{
    public class GettingHitState : State<AIController>
    {
        AIController ai;
        CombatAIController combatAI;

        public override void OnEnter(AIController owner)
        {
            ai = owner;

            if (combatAI == null)
                combatAI = ai.GetComponent<CombatAIController>();

            combatAI.Fighter.OnHitComplete += HitComplete;
        }

        public override void Exit()
        {
            combatAI.Fighter.OnHitComplete -= HitComplete;
            ai.Blackboard.Set(CombatCoreBlackboardKeys.IsGettingHit, false);
        }

        void HitComplete()
        {
            IsCompleted = true;
        }
    }
}
