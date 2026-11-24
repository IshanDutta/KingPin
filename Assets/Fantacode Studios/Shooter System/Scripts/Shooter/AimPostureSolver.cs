using UnityEngine;

namespace FS_ShooterSystem
{
    /// <summary>
    /// Procedurally rotates the spine chain (spine → chest → upperChest) toward an
    /// aim point. Each bone applies a configurable fraction of the aim angle on top
    /// of the animated pose captured fresh every frame, so the offsets layer over
    /// the underlying locomotion/aim animation instead of fighting or accumulating.
    ///
    /// Extracted from ShooterFighter. Unlike the previous single-bone (upperChest
    /// only) implementation, all three bones participate — distributing the bend
    /// across the chain reads far more naturally than over-rotating one bone. The
    /// distribution is driven by <see cref="AimRigProfile"/> weights, and a master
    /// weight lets callers blend the whole posture in and out smoothly.
    /// </summary>
    public class AimPostureSolver
    {
        readonly Transform root;
        readonly Transform spine;
        readonly Transform chest;
        readonly Transform upperChest;

        // Persistent smoothed aim OFFSET per bone (not the pose). Kept separate from
        // the animated pose so the animation's own motion is never filtered.
        Quaternion spineOffset = Quaternion.identity;
        Quaternion chestOffset = Quaternion.identity;
        Quaternion upperChestOffset = Quaternion.identity;

        public AimPostureSolver(Transform root, Transform spine, Transform chest, Transform upperChest)
        {
            this.root = root;
            this.spine = spine;
            this.chest = chest;
            this.upperChest = upperChest;
        }

        /// <summary>
        /// Rotate the spine chain toward <paramref name="targetPoint"/>.
        /// </summary>
        /// <param name="weight">Master 0..1 blend. 0 relaxes to the animated pose.</param>
        public void Solve(Vector3 targetPoint, AimRigProfile profile, float weight = 1f)
        {
            // Needs at least the spine (used as the aim origin) and the root for
            // its right/rotation reference.
            if (spine == null || root == null)
            {
                Apply(0f, 0f, profile, weight);
                return;
            }

            Vector3 directionToTarget = targetPoint - spine.position;
            Vector3 horizontalDirection = directionToTarget;
            horizontalDirection.y = 0f;

            if (horizontalDirection.magnitude <= 0.001f)
            {
                Apply(0f, 0f, profile, weight);
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(horizontalDirection, Vector3.up);
            float verticalAngle = Vector3.SignedAngle(
                horizontalDirection.normalized,
                directionToTarget.normalized,
                root.right);

            Quaternion worldToLocal = Quaternion.Inverse(root.rotation);
            float localYRotation = (worldToLocal * targetRotation).eulerAngles.y;
            if (localYRotation > 180f) localYRotation -= 360f;

            // Always clamp-and-hold at the cone edge. Resetting to neutral (0) when
            // the aim leaves the cone caused the upper body to snap back to center
            // as the aim swept past maxHorizontal/VerticalAngle; holding at the
            // clamped max keeps the lean continuous while sweeping the aim.
            verticalAngle = Mathf.Clamp(verticalAngle, -profile.maxVerticalAngle, profile.maxVerticalAngle);
            localYRotation = Mathf.Clamp(localYRotation, -profile.maxHorizontalAngle, profile.maxHorizontalAngle);
            Apply(verticalAngle, localYRotation, profile, weight);
        }

        /// <summary>Ease the spine chain back to the animated (neutral) pose.</summary>
        public void Relax(AimRigProfile profile) => Apply(0f, 0f, profile, 1f);

        void Apply(float pitch, float yaw, AimRigProfile profile, float masterWeight)
        {
            float smooth = Mathf.Clamp01(profile.bodyRotationSpeed * Time.deltaTime);
            masterWeight = Mathf.Clamp01(masterWeight);

            ApplyBone(spine, ref spineOffset, pitch, yaw, profile.spineWeight * masterWeight, smooth);
            ApplyBone(chest, ref chestOffset, pitch, yaw, profile.chestWeight * masterWeight, smooth);
            ApplyBone(upperChest, ref upperChestOffset, pitch, yaw, profile.upperChestWeight * masterWeight, smooth);
        }

        static void ApplyBone(Transform bone, ref Quaternion currentOffset, float pitch, float yaw, float weight, float smooth)
        {
            if (bone == null) return;

            // Smooth ONLY the aim offset — never the animation. The offset is eased
            // toward its target, then layered on top of the bone's fresh animated
            // localRotation. Because the animation's own motion (e.g. strafe chest
            // sway) is applied unfiltered, the torso no longer beats/pops once per
            // step; only the aim response is damped.
            Quaternion targetOffset = Quaternion.Euler(pitch * weight, yaw * weight, 0f);
            currentOffset = Quaternion.Slerp(currentOffset, targetOffset, smooth);

            bone.localRotation = bone.localRotation * currentOffset;
        }
    }
}
