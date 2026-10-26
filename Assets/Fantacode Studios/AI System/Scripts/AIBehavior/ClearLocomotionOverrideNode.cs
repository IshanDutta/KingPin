using UnityEngine;

namespace FS_Core
{
    [AINodeModule("Core", "Clear Locomotion Override")]
    public class ClearLocomotionOverrideNode : AIBehaviorNode
    {
        [SerializeField] float transitionDuration = 0.25f;
        [SerializeField] bool resetTurningAnimations = true;

        AIController ai;

        public override void Enter(AIController ai)
        {
            this.ai = ai;
        }

        public override bool Execute()
        {
            ai.AnimGraph.ClearLocomotionOverride(transitionDuration);

            if (resetTurningAnimations)
                ai.LocomotionCore.ResetTurningAnimations();

            return true;
        }

        public override void Exit() { }
    }
}
