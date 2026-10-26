using UnityEngine;

namespace FS_Core
{
    /// <summary>
    /// Holding state the state machine sits in while an external system (parkour, climb, …) owns the
    /// character via FocusSystem/IsPaused. It does nothing while active; the work happens on
    /// <see cref="Exit"/>, when control is handed back to the regular states.
    /// </summary>
    public class PausedState : State<AIController>
    {
        public override void Exit()
        {
            // The paused action may have driven the body across an off-mesh link (e.g. parkour up a ledge)
            // So call navAgent.Warp() before calling the SetDestination
            var navAgent = Owner.NavAgent;
            if (navAgent != null && navAgent.isActiveAndEnabled)
                navAgent.Warp(navAgent.transform.position);
        }
    }
}
