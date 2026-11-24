using FS_CombatCore;
using FS_Core;
using FS_ShooterSystem;
using FS_Util;
using UnityEngine;

namespace FS_Shooter
{
    // Moves toward the target position (last seen position when sight is lost) to regain visual
    // contact. All shooting is handled by ShootState — this state completes once visual is regained
    // and a clear firing line exists, or when the position is reached without finding the target.
    public class FlankState : State<AIController>
    {
        private AIController ai;
        private CombatAIController combatAI;

        private float flankSpeed = 2f;
        private bool stopAimingOnPathDeviation = true;
        private ShooterFighter shooter;
        private ShooterAIController shooterAI;
        private Vector3 approachDir;

        // Sweep destination cache — GetNearestReachablePosition runs a synchronous path
        // calculation, so it only reruns when the target position actually moves
        private Vector3? sweepSourcePos;
        private Vector3 sweepDest;

        // Aiming snaps the body toward the aim point, which stalls the walk when the path heads
        // elsewhere — only aim while the path direction stays within this angle of the aim direction
        private const float maxAimPathAngle = 20f;

        public override void OnEnter(AIController owner)
        {
            ai = owner;

            if (Context is FlankStateContext ctx)
            {
                flankSpeed = ctx.FlankSpeed;
                stopAimingOnPathDeviation = ctx.StopAimingOnPathDeviation;
            }

            if (combatAI == null)
                combatAI = ai.GetComponent<CombatAIController>();

            if (shooter == null)
                shooter = combatAI.GetComponent<ShooterFighter>();

            if (shooterAI == null)
                shooterAI = combatAI.GetComponent<ShooterAIController>();

            // Approach direction for the overshoot, captured once
            Vector3 toTarget = combatAI.GetTargetPosition() - ai.transform.position;
            toTarget.y = 0f;
            approachDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : ai.transform.forward;

            sweepSourcePos = null;
            combatAI.NavAgent.speed = flankSpeed;
        }

        public override void Execute()
        {
            if (combatAI.Fighter.Target == null || combatAI.Fighter.Target.IsDead)
            {
                combatAI.Fighter.Target = null;
                IsCompleted = true;
                return;
            }

            // If target is visible and if we have a clear shot, the complete the Flank state
            // Visibility can ClearShot check uses different rays (from eye vs gun)
            if (combatAI.IsTargetVisible && shooterAI.HasClearShot())
            {
                IsCompleted = true;
                return;
            }

            // Push past the target position along the approach direction: the last seen position is where the target slipped 
            // out of view past an occluder (doorway edge, corner), so the hiding pocket is just BEYOND it. Stopping exactly 
            // on the position leaves the AI blind to a target pressed against the far side of that occluder.
            var targetPos = combatAI.GetTargetPosition();
            if (sweepSourcePos == null || targetPos != sweepSourcePos.Value)
            {
                sweepSourcePos = targetPos;
                Vector3 overshoot = targetPos + approachDir * AIManager.Instance.StimulusOvershootDistance;
                sweepDest = NavMeshUtil.GetNearestReachablePosition(ai.transform.position, overshoot);
            }
            var dest = sweepDest;
            combatAI.NavAgent.SetDestination(dest);

            // The stimulus reach check must measure against where this flank is actually going
            ai.Blackboard.Set(AIBlackboardKeys.StimulusCheckPosition, dest);

            // Reached the last seen position without finding the target - Complete the Flank state
            // In this case, Stealth System will automatically downgrade Combat to Alerted Investigation Situation based 
            // on its own distance check to the lastseen pos, which will also exit the RangedCombat state entirely
            if (AIUtil.HasReachedDestination(combatAI.NavAgent))
            {
                IsCompleted = true;
                return;
            }

            // Aim (which also snaps the body toward the aim point) only while the path's next
            // corner roughly agrees with the aim direction. Otherwise the navemesh won't be able to move the AI
            Vector3 pathDir = combatAI.NavAgent.steeringTarget - ai.transform.position;
            pathDir.y = 0f;
            Vector3 aimDir = dest - ai.transform.position;
            aimDir.y = 0f;
            bool pathAllowsAiming = !stopAimingOnPathDeviation || pathDir.sqrMagnitude < 0.01f || Vector3.Angle(pathDir, aimDir) <= maxAimPathAngle;

            if (!combatAI.UseLastSeenPosition || pathAllowsAiming && shooterAI.CanSeeTargetPosition())
            {
                if (!shooter.IsAiming)
                    shooter.StartAiming();

                // Last seen position is recorded at feet level; raise it toward the torso so the AI aims at the body.
                Vector3? aimOverride = combatAI.UseLastSeenPosition
                    ? dest + Vector3.up * combatAI.LastSeenAimHeightOffset
                    : null;
                shooter.UpdateAimingTargets(aimPositionOverride: aimOverride);
            }
            else if (shooter.IsAiming)
            {
                shooter.StopAiming();
            }
        }

        public override void Exit()
        {
            ai.Blackboard.Set<bool>(ShooterBlackboardKeys.FlankRequested, false);
            ai.Blackboard.Remove(AIBlackboardKeys.StimulusCheckPosition);
            shooter.StopAiming();
        }
    }

    public class FlankStateContext
    {
        public float FlankSpeed = 2f;
        public bool StopAimingOnPathDeviation = true;
    }
}
