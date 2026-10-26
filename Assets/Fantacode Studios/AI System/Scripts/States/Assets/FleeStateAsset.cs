using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Flee")]
    public class FleeStateAsset : StateAsset
    {
        [SerializeField] string _name = "Flee";

        [Tooltip("How far the NPC tries to move in a single flee decision")]
        [SerializeField] float fleeDistance = 12f;

        [Tooltip("Distance from the threat at which the NPC stops fleeing")]
        [SerializeField] float safeDistance = 18f;

        [Tooltip("Time interval at which the AI will recalculate the flee position")]
        [SerializeField] float repathInterval = 2f;

        [Tooltip(
            "Controls how the flee destination is chosen.\n\n" +
            "Best Position: Samples multiple directions to find a better escape route.\n" +
            "Simple: Moves directly away from the threat."
        )]
        [SerializeField] FleePositionCalculationType calculationType;

        public override State<AIController> Build(AIController owner)
        {
            var context = new FleeStateContext
            {
                FleeDistance = fleeDistance,
                SafeDistance = safeDistance,
                RepathInterval = repathInterval,
                CalculationType = calculationType,
            };
            var state = new FleeState();
            state.SetContext(context);
            return state;
        }

        public override string NodeTitle => _name;
    }
}
