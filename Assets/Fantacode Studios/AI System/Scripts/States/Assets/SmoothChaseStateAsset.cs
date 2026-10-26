using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Smooth Chase")]
    public class SmoothChaseStateAsset : StateAsset
    {
        [SerializeField] string _name = "Smooth Chase";

        [SerializeField] BlackboardKey targetKey = new BlackboardKey(BlackboardValueType.Transform);

        [SerializeField] float stoppingDistance = 1f;
        [SerializeField] float minSpeed = 2f;
        [SerializeField] float maxSpeed = 3.5f;

        public override State<AIController> Build(AIController owner)
        {
            var context = new SmoothChaseStateContext()
            {
                TargetKey = targetKey,
                StoppingDistance = stoppingDistance,
                MinSpeed = minSpeed,
                MaxSpeed = maxSpeed,
            };
            var state = new SmoothChaseState();
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => _name;
    }
}
