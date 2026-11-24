using FS_Core;
using UnityEngine;

namespace FS_Shooter
{
    [AIStateModule("Shooter", "Flank")]
    public class FlankStateAsset : StateAsset
    {
        [SerializeField] string _name = "Flank";

        [Header("Flank Settings")]
        [Tooltip("Speed at which the AI flanks the player")]
        [SerializeField] private float flankSpeed = 2f;

        [Tooltip("Stop aiming while the path direction deviates too far from the aim direction, so aiming doesn't stall the walk")]
        [SerializeField] private bool stopAimingOnPathDeviation = false;

        public override State<AIController> Build(AIController owner)
        {
            var flankState = new FlankState();
            var flankContext = new FlankStateContext()
            {
                FlankSpeed = flankSpeed,
                StopAimingOnPathDeviation = stopAimingOnPathDeviation,
            };
            flankState.SetContext(flankContext);

            return flankState;
        }

        public override string NodeTitle => _name;
    }
}
