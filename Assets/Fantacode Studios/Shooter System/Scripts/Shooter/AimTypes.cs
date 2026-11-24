using UnityEngine;

namespace FS_ShooterSystem
{
    /// <summary>
    /// Selects which aiming implementation drives the character's aim posture and
    /// weapon IK each frame.
    /// <list type="bullet">
    /// <item><see cref="Legacy"/> — the original procedural spine-chain posture +
    /// arm/hand IK system (<see cref="AimPostureSolver"/> / ShootingAimController).</item>
    /// <item><see cref="New"/> — the replacement aiming architecture, built up
    /// incrementally. While it is in development the per-frame procedural aiming
    /// is skipped so the two systems never fight over the same bones.</item>
    /// </list>
    /// </summary>
    public enum AimingMode
    {
        Legacy,
        New
    }

    /// <summary>High-level aiming/IK state, the single source of truth that drives IK weights.</summary>
    public enum AimState
    {
        Idle,
        Aiming,
        ScopeView,
        Reloading,
        ChangingWeapon,
        Dead
    }

    /// <summary>
    /// The output of an aim source: where the character aims, where bullets converge,
    /// and what (if anything) that line of fire hit. Groups the loose aim fields that
    /// were previously scattered as individual members on ShooterFighter.
    /// </summary>
    public struct AimTarget
    {
        public Vector3 aimPoint;     // Where the body/arms orient (TargetAimPoint).
        public Vector3 bulletPoint;  // Where bullets converge (BulletHitPoint).
        public bool obstructed;      // Line of fire blocked right in front of the muzzle.
        public RaycastHit hit;       // The resolved hit along the line of fire, if any.
    }

    /// <summary>
    /// Abstraction over how aim is sourced. The player (camera raycast) and AI
    /// (target-relative) implement/register different providers so ShooterFighter
    /// no longer needs to special-case who is driving it.
    /// </summary>
    public interface IAimProvider
    {
        /// <summary>
        /// True when the provider recomputes hit data every frame (e.g. the player's
        /// per-frame camera raycast). When false, hit data is stale between shots and
        /// must be recomputed at fire time (the AI case).
        /// </summary>
        bool ProducesContinuousHitData { get; }
    }

    /// <summary>
    /// Owns the master aim-IK blend weight. Replaces the scattered on/off gating
    /// (the abrupt <c>enableIk = false</c> flips, per-frame boolean AND chains) with
    /// a single eased 0..1 weight derived from the current <see cref="AimState"/>.
    /// Consumers multiply their IK contribution by <see cref="AimWeight"/> so aim
    /// blends in and out smoothly instead of snapping.
    /// </summary>
    public class AimIKWeightController
    {
        /// <summary>Eased master weight for procedural aim posture + arm IK, 0..1.</summary>
        public float AimWeight { get; private set; }

        public void Tick(AimState state, bool canAim, bool keepAimingWhileReload, float blendSpeed, float dt)
        {
            float target;
            switch (state)
            {
                case AimState.Aiming:
                case AimState.ScopeView:
                    target = canAim ? 1f : 0f;
                    break;
                case AimState.Reloading:
                    target = (canAim && keepAimingWhileReload) ? 1f : 0f;
                    break;
                default: // Idle, ChangingWeapon, Dead
                    target = 0f;
                    break;
            }

            AimWeight = Mathf.MoveTowards(AimWeight, target, blendSpeed * dt);
        }

        /// <summary>Force the weight (e.g. on teardown) without easing.</summary>
        public void Reset(float value = 0f) => AimWeight = Mathf.Clamp01(value);
    }
}
