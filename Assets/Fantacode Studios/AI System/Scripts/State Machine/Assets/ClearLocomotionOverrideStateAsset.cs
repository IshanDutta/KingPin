using UnityEngine;

namespace FS_Core
{

    [AIStateModule("Core", "Clear Locomotion Override")]
    public class ClearLocomotionOverrideStateAsset : StateAsset
    {
        [SerializeField] new string name = "Clear Locomotion Override";

        [SerializeField] float transitionDuration = 0.25f;
        [SerializeField] bool resetTurningAnimations = true;

        public override State<AIController> Build(AIController owner)
        {
            var state = new ClearLocomotionOverrideState();
            var context = new ClearLocomotionOverrideContext
            {
                TransitionDuration = transitionDuration,
                ResetTurningAnimations = resetTurningAnimations,
            };
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => name;
    }
}
