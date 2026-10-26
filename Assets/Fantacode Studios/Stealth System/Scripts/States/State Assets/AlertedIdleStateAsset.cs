using FS_Core;
using UnityEngine;

namespace FS_StealthSystem
{
    [AIStateModule("Stealth", "Alerted Idle")]
    public class AlertedIdleStateAsset : StateAsset
    {
        [Tooltip("Display name of this node in the state machine editor.")]
        [SerializeField] string _name = "Alerted Idle";
        [Tooltip("If true, the AI walks back to the position it held before being alerted.")]
        [SerializeField] bool returnToInitialPosition = true;
        [Tooltip("If true, the AI turns back to its original facing direction once in place.")]
        [SerializeField] bool returnToInitialFacing = true;

        public override State<AIController> Build(AIController owner)
        {
            var state = new AlertedIdleState();
            state.SetContext(new AlertedIdleContext
            {
                ReturnToInitialPos = returnToInitialPosition,
                ReturnToInitialFacing = returnToInitialFacing
            });
            return state;
        }

        public override string NodeTitle => _name;
    }
}
