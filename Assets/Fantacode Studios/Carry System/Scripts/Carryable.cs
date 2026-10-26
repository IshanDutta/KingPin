using FS_Core;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace FS_InteractionSystem
{
    /// <summary>
    /// Runtime component on any object that can be picked up and carried.
    /// All configuration (animations, socket, physics) is stored in a <see cref="CarryableData"/>
    /// ScriptableObject so data is shared across prefab variants and so
    /// <see cref="ConcealmentHotspot"/> can filter by type via asset-reference comparison.
    /// </summary>
    public class Carryable : MonoBehaviour
    {
        [Tooltip("Defines the type and carry configuration for this object. " +
                 "Objects sharing the same asset are considered the same carryable type " +
                 "and can be filtered at a ConcealmentSpot.")]
        public CarryableData data;

        // ── Convenience accessors ──────────────────────────────────────────────────
        // Forward to 'data' so CarryHandler and CarryHotspot need no changes.

        public CarrySocketType carrySocket => data.carrySocket;
        public float carrySocketMoveDuration => data.carrySocketMoveDuration;

        /// <summary>Seat the object at <see cref="carrySocketPosOffset"/> once attached.</summary>
        public bool applyPositionOffset => data.applyPositionOffset;

        /// <summary>
        /// Force <see cref="carrySocketRotOffset"/> once attached; false leaves the rotation the
        /// pickup animation produced untouched.
        /// </summary>
        public bool applyRotationOffset => data.applyRotationOffset;

        /// <summary>
        /// True when at least one socket offset is applied on attach. With both off the object is
        /// left exactly where the pickup animation puts it, so there is nothing to place or tune.
        /// </summary>
        public bool AppliesAnyOffset => data.applyPositionOffset || data.applyRotationOffset;

        // Character-pose carries use a SEPARATE offset per pose (face-up / face-down), because the two
        // pose clips rest on the socket at different body points, so one offset can't serve both.
        // Non-character objects use the single offset.
        public Vector3 carrySocketPosOffset =>
            useCharacterPosePickup
                ? (IsFaceUp ? data.carrySocketPosOffsetFaceUp : data.carrySocketPosOffsetFaceDown)
                : data.carrySocketPosOffset;

        public Vector3 carrySocketRotOffset =>
            useCharacterPosePickup
                ? (IsFaceUp ? data.carrySocketRotOffsetFaceUp : data.carrySocketRotOffsetFaceDown)
                : data.carrySocketRotOffset;

        /// <summary>
        /// Writes the carry socket offset for the pose currently being carried (face-up / face-down),
        /// or the single offset for non-character objects. Used by the editor tuning handle.
        /// </summary>
        public void SetCarrySocketOffset(Vector3 pos, Vector3 rot)
        {
            if (!useCharacterPosePickup)       { data.carrySocketPosOffset = pos;        data.carrySocketRotOffset = rot; }
            else if (IsFaceUp)                 { data.carrySocketPosOffsetFaceUp = pos;  data.carrySocketRotOffsetFaceUp = rot; }
            else                               { data.carrySocketPosOffsetFaceDown = pos; data.carrySocketRotOffsetFaceDown = rot; }
        }

        /// <summary>Human-readable name of the pose whose offset is currently in use.</summary>
        public string CurrentPoseName => !useCharacterPosePickup ? "Default" : (IsFaceUp ? "Face Up" : "Face Down");
        public AnimGraphClipInfo carryIdleClip => data.carryIdleClip;
        public Mask carryIdleClipMask => data.carryIdleClipMask;
        public AnimGraphClipInfo dropClip => data.dropClip;
        public AnimGraphClipInfo throwClip => data.throwClip;
        public AnimGraphClipInfo objectDropClip => data.objectDropClip;
        public AnimGraphClipInfo carryingClip => data.carryingClip;
        public bool useCharacterPosePickup => data.useCharacterPosePickup;
        public AnimGraphClipInfo carryingClipFaceUp => data.carryingClipFaceUp;
        public AnimGraphClipInfo carryingClipFaceDown => data.carryingClipFaceDown;
        public AnimGraphClipInfo targetClipFaceUp => data.targetClipFaceUp;
        public AnimGraphClipInfo targetClipFaceDown => data.targetClipFaceDown;
        public AnimGraphClipInfo targetClip => data.targetClip;
        public float carryStartNormalizedTime => data.carryStartNormalizedTime;
        public bool isThrowable => data.isThrowable;
        public bool canBeDumped => data.canBeDumped;
        public AnimGraphClipInfo objectDumpClip => data.objectDumpClip;
        public AnimGraphClipInfo playerDumpClip => data.playerDumpClip;
        public float dropStartNormalizedTime => data.dropStartNormalizedTime;
        public float throwStartNormalizedTime => data.throwStartNormalizedTime;
        public float throwForce => data.throwForce;
        public float throwHeight => data.throwHeight;

        // ── Pose snapshot ─────────────────────────────────────────────────────
        // Evaluated once at the start of carrying, then reused by animations,
        // offsets, and any other pose-dependent logic.

        bool poseSnapshotTaken;
        bool snapshotFaceUp;

        /// <summary>
        /// Snapshots the current face-up/down pose. Call once before the ragdoll
        /// state changes (before rigidbody goes kinematic or blend-back starts).
        /// All subsequent calls to GetPickupClip, GetTargetClip, carrySocketPosOffset,
        /// and carrySocketRotOffset use this snapshot.
        /// </summary>
        public void SnapshotPose()
        {
            if (!useCharacterPosePickup)
            {
                snapshotFaceUp = false;
                poseSnapshotTaken = true;
                return;
            }

            var anim = GetComponentInParent<Animator>();
            if (anim == null || !anim.isHuman) { poseSnapshotTaken = true; return; }
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) { poseSnapshotTaken = true; return; }

            var rb = hips.GetComponent<Rigidbody>();
            Vector3 forward = rb != null && !rb.isKinematic
                ? rb.rotation * Vector3.forward
                : hips.forward;

            snapshotFaceUp = Vector3.Dot(forward, Vector3.up) > 0f;
            poseSnapshotTaken = true;
        }

        void ClearPoseSnapshot()
        {
            poseSnapshotTaken = false;
        }

        /// <summary>
        /// Returns the snapshotted value if available, otherwise evaluates live.
        /// </summary>
        public bool IsFaceUp
        {
            get
            {
                if (poseSnapshotTaken) return snapshotFaceUp;

                if (!useCharacterPosePickup) return false;
                var anim = GetComponentInParent<Animator>();
                if (anim == null || !anim.isHuman) return false;
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                if (hips == null) return false;

                var rb = hips.GetComponent<Rigidbody>();
                Vector3 forward = rb != null && !rb.isKinematic
                    ? rb.rotation * Vector3.forward
                    : hips.forward;

                return Vector3.Dot(forward, Vector3.up) > 0f;
            }
        }

        // ── Pose-based getters (all use the snapshot) ────────────────────────

        public AnimGraphClipInfo GetPickupClip()
        {
            if (!useCharacterPosePickup) return carryingClip;
            var poseClip = IsFaceUp ? carryingClipFaceUp : carryingClipFaceDown;
            return poseClip.NotNull() ? poseClip : carryingClip;
        }

        public AnimGraphClipInfo GetTargetClip()
        {
            if (!useCharacterPosePickup) return targetClip;
            var poseClip = IsFaceUp ? targetClipFaceUp : targetClipFaceDown;
            return poseClip.NotNull() ? poseClip : targetClip;
        }

        /// <summary>Stand point for the pose currently in use (see <see cref="CarryStandPoint"/>).</summary>
        public CarryStandPoint GetStandPoint()
        {
            if (!useCharacterPosePickup) return data.standPoint;
            return IsFaceUp ? data.standPointFaceUp : data.standPointFaceDown;
        }

        // ── Events ─────────────────────────────────────────────────────────────────

        /// <summary>Fired when a carrier picks this object up.</summary>
        public Action OnStartCarrying;

        /// <summary>Fired when the object is released. Parameters: force direction, whether force was applied.</summary>
        public Action<Vector3, bool> OnEndCarrying;
        public Action OnDumpedIntoSpot;

        [Space(5)]
        public UnityEvent OnStartCarryingEvent;
        public UnityEvent OnEndCarryingEvent;

        // ── Runtime state ──────────────────────────────────────────────────────────

        /// <summary>True while this object is attached to a carrier.</summary>
        public bool IsCarrying { get; private set; }

        [Tooltip("Rigidbody on this object. Auto-assigned from the same GameObject if left empty.")]
        public new Rigidbody rigidbody;

        Transform defaultParent;
        ActiveRagdollSystem activeRagdollSystem;

        // ── Unity Messages ─────────────────────────────────────────────────────────

        private void Awake()
        {
            defaultParent = transform.parent;
            if (rigidbody == null)
                rigidbody = GetComponent<Rigidbody>();
            activeRagdollSystem = GetComponent<ActiveRagdollSystem>();
        }

        private void OnEnable()
        {
            OnStartCarrying += StartCarry;
            OnEndCarrying += EndCarry;
        }

        private void OnDisable()
        {
            OnStartCarrying -= StartCarry;
            OnEndCarrying -= EndCarry;
        }

        // ── Internal carry/release ─────────────────────────────────────────────────

        void StartCarry()
        {
            OnStartCarryingEvent?.Invoke();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = true;
            }
            IsCarrying = true;
        }

        void EndCarry(Vector3 forceDir, bool addForce)
        {
            ClearPoseSnapshot();
            OnEndCarryingEvent?.Invoke();
            IsCarrying = false;   // the SmoothCarryToSocket loop watches this and stops

            if (rigidbody != null)
                rigidbody.isKinematic = false;

            if (!addForce || forceDir == Vector3.zero) return;

            // Compose the throw velocity from independent horizontal and vertical components.
            // forceDir is the normalized horizontal throw direction from the carrier.
            // throwForce  = horizontal speed  (m/s)
            // throwHeight = upward speed      (m/s)
            // VelocityChange applies the same velocity regardless of mass, giving
            // consistent, tunable arcs for both rigidbodies and ragdoll bodies.
            Vector3 throwVelocity = forceDir.normalized * throwForce + Vector3.up * throwHeight;

            if (rigidbody != null)
                rigidbody.AddForce(throwVelocity, ForceMode.VelocityChange);
            else if (activeRagdollSystem != null)
                activeRagdollSystem.ApplyForce(HumanBodyBones.Hips, throwVelocity, ForceMode.VelocityChange);
        }

        // ── Transform helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Parents this object to <paramref name="parent"/>. The socket-offset placement and its
        /// smoothing are driven by <c>CarryHotspot.Interact</c> (SmoothCarryToSocket), not here.
        /// </summary>
        public void SetParent(Transform parent)
        {
            transform.SetParent(parent);
        }

        /// <summary>
        /// Immediately snaps the object to its carry socket (no smoothing) using the current pose's
        /// socket offset. Used by the editor tuning handle so offset edits preview live while carrying.
        /// Each offset is only written when its apply flag is enabled.
        /// </summary>
        public void ApplyCarryPlacement()
        {
            if (transform.parent == null) return;
            if (applyPositionOffset)
                transform.localPosition = carrySocketPosOffset;
            if (applyRotationOffset)
                transform.localRotation = Quaternion.Euler(carrySocketRotOffset);
        }

        /// <summary>Returns this object to its original parent in the hierarchy.</summary>
        public void Resetparent()
        {
            transform.SetParent(defaultParent, true);
        }
    }
}
