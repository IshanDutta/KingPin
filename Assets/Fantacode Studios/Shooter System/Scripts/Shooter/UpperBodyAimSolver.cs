using UnityEngine;

namespace FS_ShooterSystem
{
    /// <summary>
    /// New aiming architecture — procedural upper-body (3-bone) rotation.
    ///
    /// Rotates the spine → chest → upperChest chain so that the weapon's Aim
    /// Reference (<see cref="ShooterWeaponObject.aimReference"/>) ends up pointing at
    /// the aim target once the rotation is applied. The Aim Reference itself is never
    /// modified — it hangs off the arm/weapon hierarchy, so rotating the upper body
    /// carries it (and the whole weapon) rigidly onto the target.
    ///
    /// The solve is an "aim through a child" delta: each frame it measures where the
    /// Aim Reference currently points (its forward at the animated pose) and computes
    /// the minimal world-space rotation that maps that onto the direction to the
    /// target. That delta is distributed across the three bones by their weights and
    /// layered on top of the fresh animated pose, plus a constant per-weapon Y-axis
    /// (yaw) offset. Because each bone is pre-multiplied by delta^(weight) and the
    /// bends share the same world axis, the Aim Reference's final orientation is
    /// rotated by delta^(sum-of-weights) — so weights summing to 1 bring it fully
    /// onto the target, and lower sums blend partially with the animation.
    ///
    /// This is the New-mode counterpart to the legacy <see cref="AimPostureSolver"/>:
    /// its tuning lives on the weapon (<see cref="UpperBodyIKSettings"/>) rather than
    /// on an AimRigProfile, and it only runs while <see cref="AimingMode.New"/> is
    /// selected (see <see cref="ShooterFighter.UpdateNewAim"/>).
    /// </summary>
    public class UpperBodyAimSolver
    {
        readonly Transform root;
        readonly Transform spine;
        readonly Transform chest;
        readonly Transform upperChest;

        // Persistent smoothed world-space aim delta (the rotation that brings the Aim
        // Reference forward onto the target). Easing this — rather than snapping to
        // the freshly computed delta — smooths aim tracking and blends the whole
        // posture in and out (delta → identity relaxes back to the animated pose).
        Quaternion smoothedDelta = Quaternion.identity;

        public UpperBodyAimSolver(Transform root, Transform spine, Transform chest, Transform upperChest)
        {
            this.root = root;
            this.spine = spine;
            this.chest = chest;
            this.upperChest = upperChest;
        }

        /// <summary>
        /// Rotate the upper body so <paramref name="aimReference"/>'s forward points at
        /// <paramref name="targetPoint"/> after the rotation is applied. The reference
        /// transform is only read (forward + position), never modified.
        /// </summary>
        public void Solve(Transform aimReference, Vector3 targetPoint, UpperBodyIKSettings settings)
        {
            if (settings == null || aimReference == null) return;

            Vector3 currentForward = aimReference.forward;
            Vector3 desiredForward = targetPoint - aimReference.position;

            // Minimal (no-roll) rotation that maps where the weapon currently points
            // onto the direction to the target. Degenerate directions → no aim delta.
            Quaternion targetDelta = Quaternion.identity;
            if (currentForward.sqrMagnitude > 0.0001f && desiredForward.sqrMagnitude > 0.0001f)
                targetDelta = Quaternion.FromToRotation(currentForward.normalized, desiredForward.normalized);

            // Constant per-weapon torso twist around the character's vertical axis,
            // layered on top of the aim alignment.
            if (Mathf.Abs(settings.yRotationOffset) > 0.001f)
            {
                Vector3 upAxis = root != null ? root.up : Vector3.up;
                targetDelta = Quaternion.AngleAxis(settings.yRotationOffset, upAxis) * targetDelta;
            }

            Apply(targetDelta, settings);
        }

        /// <summary>Ease the upper-body chain back to the animated (neutral) pose.</summary>
        public void Relax(UpperBodyIKSettings settings)
        {
            if (settings == null) return;
            Apply(Quaternion.identity, settings);
        }

        void Apply(Quaternion targetDelta, UpperBodyIKSettings settings)
        {
            float smooth = settings.rotationSmoothSpeed * Time.deltaTime * 10;
            smoothedDelta = Quaternion.Slerp(smoothedDelta, targetDelta, smooth);

            // Parent → child order: applying the delta to a parent also rotates the
            // children's world rotations, so each child reads the already-rotated
            // pose before adding its own share.
            ApplyBone(spine, settings.spineWeight);
            ApplyBone(chest, settings.chestWeight);
            ApplyBone(upperChest, settings.upperChestWeight);
        }

        void ApplyBone(Transform bone, float weight)
        {
            if (bone == null || weight <= 0f) return;

            // Pre-multiplying the world delta rotates the bone (and everything below
            // it — arm, weapon, aim reference) about the bone's joint. Slerping from
            // the current rotation by 'weight' applies delta^(weight) in world space,
            // so the chain's contributions compose to delta^(sum-of-weights).
            Quaternion fullyRotated = smoothedDelta * bone.rotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, fullyRotated, weight);
        }
    }
}
