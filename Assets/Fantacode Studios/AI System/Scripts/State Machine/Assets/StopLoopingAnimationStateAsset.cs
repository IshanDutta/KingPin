using UnityEngine;

namespace FS_Core
{

    [AIStateModule("Core", "Stop Looping Animation")]
    public class StopLoopingAnimationStateAsset : StateAsset
    {
        [SerializeField] new string name = "Stop Looping Animation";

        public override State<AIController> Build(AIController owner)
        {
            var state = new StopLoopingAnimationState();
            var context = new StopLoopingAnimationContext
            {
                RemoveAll = false,
            };
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => name;
    }
}