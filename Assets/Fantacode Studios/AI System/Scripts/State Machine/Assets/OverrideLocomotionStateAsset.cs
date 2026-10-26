using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Override Locomotion")]
    public class OverrideLocomotionStateAsset : StateAsset
    {
        [SerializeField] new string name = "Override Locomotion";

        [SerializeField] BlackboardKey idleClipKey = new BlackboardKey(BlackboardValueType.AnimGraphClipInfo);
        [SerializeField] BlackboardKey walkClipKey = new BlackboardKey(BlackboardValueType.AnimGraphClipInfo);
        [SerializeField] BlackboardKey runClipKey = new BlackboardKey(BlackboardValueType.AnimGraphClipInfo);

        [SerializeField] float transitionDuration = 0.25f;
        [SerializeField] bool disableTurningAnimations = true;

        public override State<AIController> Build(AIController owner)
        {
            var state = new OverrideLocomotionState();
            var context = new OverrideLocomotionContext
            {
                TransitionDuration = transitionDuration,
                DisableTurningAnimations = disableTurningAnimations,
                IdleClipKey = idleClipKey.Name,
                WalkClipKey = walkClipKey.Name,
                RunClipKey = runClipKey.Name,
            };
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => name;
    }
}