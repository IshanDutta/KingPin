using FS_CombatCore;
using FS_Core;
using FS_ShooterSystem;
using UnityEngine;

namespace FS_Shooter
{
    public class ShootState : State<AIController>
    {
        private float stateStartTime;
        private float stateDuration;
        private float visionMismatchSince;

        // Not reset on enter, so the fire-interval pacing carries across shoot/cover cycles.
        private float nextFireTime;

        // Draw state for charged weapons (bows). Held across frames while the AI pulls the string.
        private bool isCharging;
        private float chargeReleaseTime;
        private float chargeForceMultiplier;

        // Matches the player's post-shot reset in ShooterController.FireWeapon — a small non-zero
        // floor so a shot released the instant the draw starts still leaves the bow.
        private const float ChargeBaseForce = 0.1f;

        private AIController aiController;
        private CombatAIController combatAI;

        ShooterFighter shooter;
        FighterCore fighterCore;

        ShooterAIController shooterAI;

        public override void OnEnter(AIController owner)
        {
            aiController = owner;

            if (combatAI == null)
                combatAI = aiController.GetComponent<CombatAIController>();

            if (shooterAI == null)
                shooterAI = aiController.GetComponent<ShooterAIController>();

            if (shooter == null)
                shooter = combatAI.GetComponent<ShooterFighter>();
            if(fighterCore == null)
                fighterCore = combatAI.Fighter;

            // Decide how long we stay in Shoot mode
            stateStartTime = Time.time;
            stateDuration = Random.Range(shooterAI.MinShootTime, shooterAI.MaxShootTime);
            visionMismatchSince = -1f;
            CancelCharge();

            // Prevent AI from moving if movement range is less than 1
            if (combatAI.GetMovementRange() < 1f)
            {
                combatAI.NavAgent.autoTraverseOffMeshLink = false;
            }
        }

        public override void Execute()
        {
            if (combatAI.Fighter.Target == null || combatAI.Fighter.Target.IsDead)
            {
                combatAI.Fighter.Target = null;
                IsCompleted = true;
                return;
            }

            // If out of ammo, then complete the shooting state
            if (shooter.CurrentWeapon == null || 
                (shooter.CurrentShooterWeaponObject.TotalAmmoCount <= 0 && !shooter.CurrentShooterWeaponObject.HasAmmo))
            {
                shooter.StopAiming();
                IsCompleted = true;
                return;
            }

            if (shooter.IsReloading)
            {
                stateStartTime += Time.deltaTime / 2;
            }

            // Start aiming
            if (!shooter.IsAiming)
                shooter.StartAiming();

            Vector3 targetPos = combatAI.GetTargetPosition();

            bool visible = combatAI.IsTargetVisible;
            bool clearShot = shooterAI.HasClearShot();

            // Last seen position is recorded at feet level; raise it toward the torso so the AI aims at the body.
            // Otherwise aim at the point HasClearShot validated (chest, or the head when only the head is exposed).
            Vector3? aimOverride = combatAI.UseLastSeenPosition && !visible
                ? targetPos + Vector3.up * combatAI.LastSeenAimHeightOffset
                : shooterAI.ClearShotAimPoint;
            shooter.UpdateAimingTargets(shooterAI.AimError, aimPositionOverride: aimOverride);

            bool canSeeTargetPos = shooterAI.CanSeeTargetPosition();

            // Hold position only while it offers something useful:
            //   • Visible + clear shot: fight from here.
            //   • Neither visible nor hittable, but the target position is visible: suppress it from here.
            // Otherwise advance toward the target position after a grace period:
            //   • Vision and firing line disagree (they are different rays — eye vs gun — so either
            //     can be blocked while the other is clear): step closer until both agree.
            //   • Nothing visible at all (e.g. a wall right in front): advance until some part of
            //     the target position comes into view.
            bool positionUseful = visible == clearShot && (visible || canSeeTargetPos);
            if (!positionUseful)
            {
                if (visionMismatchSince < 0f)
                    visionMismatchSince = Time.time;

                if (Time.time - visionMismatchSince >= shooterAI.ClearShotRepositionDelay)
                {
                    // Walk, don't run — matches the flank speed so the advance reads as a
                    // deliberate step-up while still aiming, not a charge.
                    combatAI.NavAgent.speed = shooterAI.AdvanceSpeed;
                    combatAI.NavAgent.SetDestination(targetPos);
                }
                else
                    combatAI.NavAgent.SetDestination(combatAI.transform.position);
            }
            else
            {
                visionMismatchSince = -1f;
                combatAI.NavAgent.SetDestination(combatAI.transform.position);
            }

            // If our shoot-duration has elapsed → complete the shooting state. If no cover point is available, keep shooting 
            // instead of completing, so the AI doesn't drop its aim only to bounce straight back from the cover state.
            if (shooterAI.UseDuration && Time.time - stateStartTime >= stateDuration
                && shooterAI.IsCoverAvailable)
            {
                // Don't complete the state if we're currenly shooting or reloading, wait for it to complete
                if (shooter.IsShooting || shooter.IsReloading)
                    return;

                IsCompleted = true;
                return;
            }

            // Reload when the magazine is empty, ammo remains, and we're free to act.
            if (shooter.CurrentShooterWeaponObject.CurrentAmmoCount == 0 && shooter.CurrentShooterWeaponObject.HasAmmo 
                && !shooter.IsShooting && !fighterCore.IsBusy)
            {
                shooter.Reload();
            }

            // Fire only at something the AI can justify: a target it sees, a firing line that actually lands, or a 
            // position it can see well enough to suppress. An AI with none of these (e.g. the target is behind the wall right 
            // in front of it) keeps aiming but holds fire — the advance above walks it to a position worth shooting from.
            // ShooterAIManager still requests a Flank once sight has been lost long enough.
            if (visible || clearShot || canSeeTargetPos)
            {
                shooter.StartAiming();

                if (shooterAI.HasFireInterval && Time.time < nextFireTime)
                {
                    // Pacing between shots is dead time — don't stand there at full draw for it.
                    CancelCharge();
                    return;
                }

                if (IsChargedWeapon)
                    HandleChargedShot();
                else
                    FireShot();
            }
            else
            {
                // Nothing worth shooting at any more; relax the string instead of holding it.
                CancelCharge();
            }
        }

        bool IsChargedWeapon => shooterAI.UseChargedShots
                             && shooter.CurrentWeapon != null
                             && shooter.CurrentWeapon.isChargedWeapon;

        /// <summary>
        /// Draws the weapon, holds it for a randomised beat, then looses. Force builds over the
        /// draw exactly as it does for the player (ShooterController.HandleChargingMechanic), so a
        /// half-drawn AI shot carries the same reduced velocity a half-drawn player shot would.
        /// </summary>
        void HandleChargedShot()
        {
            // Nocking the next arrow owns the draw hand — don't start a draw against the reload.
            if (shooter.IsReloading)
            {
                CancelCharge();
                return;
            }

            if (!isCharging)
            {
                // Wait for the weapon to actually be ready rather than opening a draw that can't
                // be released — HandleCharging is refused between shots, and a charge left hanging
                // would keep the bow at full stretch until the state exits.
                if (!shooter.IsReadyToFire)
                    return;

                shooter.HandleCharging();
                isCharging = true;
                chargeForceMultiplier = ChargeBaseForce;
                chargeReleaseTime = Time.time + Random.Range(shooterAI.MinChargeTime, shooterAI.MaxChargeTime);
            }

            chargeForceMultiplier += Time.deltaTime * shooter.CurrentWeapon.chargeForce;

            if (Time.time < chargeReleaseTime)
                return;

            FireShot(Mathf.Clamp01(chargeForceMultiplier));
        }

        void FireShot(float forceMultiplier = 1f)
        {
            shooter.Shoot(forceMultiplier);

            // Shoot() refuses while the weapon is busy. Only clear the draw and re-arm the fire
            // interval once the shot has actually gone out, otherwise the AI keeps holding and
            // retries — which is what an archer waiting for its moment should look like.
            if (!shooter.IsShooting)
                return;

            CancelCharge();

            if (shooterAI.HasFireInterval)
                nextFireTime = Time.time + Random.Range(shooterAI.MinFireInterval, shooterAI.MaxFireInterval);
        }

        void CancelCharge()
        {
            if (!isCharging) return;

            isCharging = false;
            chargeForceMultiplier = ChargeBaseForce;
            shooter.StopCharging();
        }

        public override void Exit()
        {
            CancelCharge();
            shooter.StopAiming();
        }
    }
}
