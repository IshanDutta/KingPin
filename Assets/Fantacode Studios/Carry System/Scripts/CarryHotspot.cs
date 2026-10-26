using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_InteractionSystem
{
    public class CarryHotspot : InteractionHotspot
    {
        public Carryable objectToCarry;

        [Tooltip("Use the CarryableData's per-pose stand points (face-up / face-down aware) instead " +
                 "of the manual interactionPoints list. Enable a pose's stand point on the " +
                 "CarryableData, then position it by dragging the magenta scene-view handle. Poses " +
                 "without an enabled stand point fall back to the interaction points.")]
        public bool usePoseStandPoints = true;

        [Tooltip("Log the resolved pose + stand point when the pickup starts, and the attach-moment " +
                 "alignment residual — how far the body is from its tuned socket pose when it gets " +
                 "parented (the distance SmoothCarryToSocket then covers). Small residual = the stand " +
                 "point put the carrier exactly where the animation pair needed them.")]
        public bool debugStandPoint = false;

        // Reused for the resolved stand point so the per-frame approach queries don't allocate.
        readonly InteractionPoint resolvedStandPoint = new InteractionPoint(Vector3.zero, 0);

        protected override void Start()
        {
            base.Start();
            if (objectToCarry == null) return;
            objectToCarry.OnDumpedIntoSpot += () => enabled = false;
        }

        public override IEnumerator Interact(InteractionExecutor interactionExecutor)
        {
            if (objectToCarry == null) yield break;
            yield return base.Interact(interactionExecutor);

            // Snapshot the pose NOW — before anything changes the ragdoll state.
            // All subsequent calls (GetPickupClip, GetTargetClip, carrySocketOffset)
            // use this snapshot so they all agree on the same pose.
            objectToCarry.SnapshotPose();

            var actionExecutor = interactionExecutor.GetComponent<ActionExecutor>();

            var carryHandler = interactionExecutor.GetComponent<CarryHandler>();
            if (carryHandler == null) yield break;

            // Claimed only once there is a CarryHandler to see the carry through. Raising it above
            // the bail-out left a character with no handler flagged as carrying for good, with
            // nothing that could ever call EndCarrying — and a latched flag now keeps every hotspot
            // hidden by the carry filter AND makes Cancel do its full teardown on every call.
            interactionExecutor.StartCarrying();

            if (debugStandPoint)
            {
                bool haveStand = TryGetStandPoint(out Vector3 stand, out Quaternion standRot);
                Debug.Log($"[CarryHotspot] {objectToCarry.CurrentPoseName}: stand point " +
                          (haveStand ? $"{stand:F3} yaw {standRot.eulerAngles.y:F1}°" : "DISABLED (fallback to interaction points)") +
                          $" | player at {interactionExecutor.transform.position:F3} yaw {interactionExecutor.transform.eulerAngles.y:F1}°" +
                          $" | IsFaceUp={objectToCarry.IsFaceUp}", this);
            }

            carryHandler.SetCarryObject(objectToCarry);

            // Since the idle clip is looping, queue it before the pickup animation completes.
            carryHandler.PlayCarryIdleClip(objectToCarry);

            // Select the correct pickup clip based on character pose (face-up vs face-down).
            var pickupClip = objectToCarry.GetPickupClip();

            yield return carryHandler.UnEquipCurrentItem();

            if (pickupClip.IsNull())
            {
                // No pickup animation — attach immediately, then ease to the socket.
                carryHandler.Carry(objectToCarry);
                yield return SmoothCarryToSocket(objectToCarry);
                yield break;
            }

            bool carried = false;
            var interactAction = new CharacterAction(pickupClip) { ignoreCollision = true};
            interactAction.animationParameters = new AnimParameters()
            {
                onAnimationUpdate = (nor, t) =>
                {
                    if (nor >= objectToCarry.carryStartNormalizedTime && !carried)
                    {
                        if (debugStandPoint) LogAttachResidual(carryHandler);
                        carryHandler.Carry(objectToCarry);
                        carried = true;
                    }
                }
            };

            // var aiInteractionParticipant = objectToCarry.GetComponent<AIInteractionParticipant>();

            // if (aiInteractionParticipant != null)
            //     aiInteractionParticipant.BeginParticipation();

            // Select the correct target clip based on character pose.
            var resolvedTargetClip = objectToCarry.GetTargetClip();

            if (resolvedTargetClip.NotNull())
            {
                actionExecutor.Execute(interactAction);

                var targetAction = new CharacterAction(resolvedTargetClip);
                // transitionIn = 0 so the clip's root-rotation offset lands fully on the first frame; the
                // ActiveRagdollSystem's root-rotation ease (smoothRootRotation, below) captures it there
                // and eases it in — rather than reading a half-applied crossfade value.
                targetAction.animationParameters = new AnimParameters() { transitionBack = false, transitionIn = 0f };
                targetAction.ignoreCollision = true;
                var activeRagdollSystem = objectToCarry.GetComponent<ActiveRagdollSystem>();
                if (activeRagdollSystem != null)
                {
                    // Disable root motion until AlignAnimatorToRagdoll has committed the
                    // correct position and the animator has run one suppressed frame.
                    // BlendToAnimatorAsync fires onAligned at exactly that moment. smoothRootRotation eases
                    // the clip's root-rotation OFFSET in over the blend instead of snapping it — the
                    // "hips snap, then move toward the target rotation" glitch at the start of carry.
                    targetAction.usesRootMovement = false;
                    yield return activeRagdollSystem.BlendToAnimatorAsync(targetAction,
                        onAligned: () => targetAction.usesRootMovement = true,
                        smoothRootRotation: true);
                }
                else
                    yield return objectToCarry.GetComponent<ActionExecutor>().ExecuteAsync(targetAction);
            }
            else
            {
                yield return actionExecutor.ExecuteAsync(interactAction);
            }

            // Ease the carried object from its attach pose to the carry socket offset — softens the pop
            // at the moment it locks onto the socket. Done here (from Interact) after the pickup has
            // settled, so the pose animation no longer overwrites the glide.
            yield return SmoothCarryToSocket(objectToCarry);
        }

        /// <summary>
        /// Eases the carried object from its current (attach) pose to its carry socket offset over
        /// <c>carrySocketMoveDuration</c>, softening the pop when the body locks onto the socket. The
        /// offset accessors are pose-specific (face-up / face-down) for character carries.
        ///
        /// In <see cref="CarryPlacementMode.PositionOnly"/> the rotation is never written, so the object
        /// keeps the orientation its pickup animation produced and simply rides the socket from there.
        /// Leaving localRotation alone (rather than easing toward the current value) is what makes this
        /// a true no-op on rotation — a slerp whose target is read once at the start would still fight
        /// the animation for the remainder of the ease.
        /// </summary>
        IEnumerator SmoothCarryToSocket(Carryable obj)
        {
            // Nothing to ease toward when neither offset is applied — the animation's own pose stands.
            if (obj == null || !obj.AppliesAnyOffset) yield break;

            // Wait until the object is actually attached to the socket. Carry() — which sets IsCarrying
            // and parents the object — fires at carryStartNormalizedTime, which can come AFTER
            // BlendToAnimatorAsync finishes. So reaching here does NOT guarantee the attach happened;
            // easing before it (wrong parent / not carrying) is the intermittent "offset not applied"
            // failure. Wait for the attach first, then ease.
            float timeout = 3f;
            while (!obj.IsCarrying && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (!obj.IsCarrying || obj.transform.parent == null) yield break;

            bool applyPosition = obj.applyPositionOffset;
            bool applyRotation = obj.applyRotationOffset;

            Vector3 startPos = obj.transform.localPosition;
            Quaternion startRot = obj.transform.localRotation;
            Vector3 targetPos = obj.carrySocketPosOffset;
            Quaternion targetRot = Quaternion.Euler(obj.carrySocketRotOffset);
            float duration = obj.carrySocketMoveDuration;

            float time = 0f;
            while (time < duration && obj.IsCarrying)
            {
                time += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, time / duration);
                if (applyPosition)
                    obj.transform.localPosition = Vector3.Lerp(startPos, targetPos, k);
                if (applyRotation)
                    obj.transform.localRotation = Quaternion.Slerp(startRot, targetRot, k);
                yield return null;
            }

            if (obj.IsCarrying)
            {
                if (applyPosition)
                    obj.transform.localPosition = targetPos;
                if (applyRotation)
                    obj.transform.localRotation = targetRot;
            }
        }

        public override bool CanInteract()
        {
            return base.CanInteract() && !objectToCarry.IsCarrying;
        }

        public override (Vector3, Quaternion) GetBestInteractionPoint(Vector3 interactorPos, List<InteractionPoint> points)
        {
            if (usePoseStandPoints && TryGetStandPoint(out Vector3 pos, out Quaternion rot))
                return (pos, rot);
            return base.GetBestInteractionPoint(interactorPos, points);
        }

        /// <summary>
        /// Resolves the stand point for the object's current pose (face-up / face-down) and maps it
        /// through the live body-canonical frame via the base hotspot helpers — so the point follows
        /// the body wherever and however it lies, with the same frame and ground snap as manual
        /// interaction points. False when the resolved pose has no enabled stand point (caller falls
        /// back to the interaction points).
        /// </summary>
        public bool TryGetStandPoint(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = Quaternion.identity;

            if (objectToCarry == null || objectToCarry.data == null) return false;
            var stand = objectToCarry.GetStandPoint();
            if (stand == null || !stand.enabled) return false;

            resolvedStandPoint.positionOffset = stand.positionOffset;
            resolvedStandPoint.facingDirection = stand.facingDirection;

            // Edit mode has no FSSettings singleton, which GetInteractionPosition's ground snap
            // needs — use the raw (unsnapped) frame instead. Same frame, minus the raycast, so
            // the scene-view handle still round-trips through WorldToOffsetSpace.
            if (FSSettings.i == null)
            {
                pos = GetInteractionPositionRaw(resolvedStandPoint);
                rot = GetInteractionRotation(resolvedStandPoint);
                return true;
            }

            pos = GetInteractionPosition(resolvedStandPoint);
            rot = GetInteractionRotation(resolvedStandPoint);
            return true;
        }

        /// <summary>
        /// Logs how far the body is from its tuned socket pose at the attach moment — the residual
        /// SmoothCarryToSocket has to cover. Near-zero means the stand point put the carrier exactly
        /// where the animation pair needed them; a large value points at the stand point (or the
        /// pose data) being wrong for this pose.
        /// </summary>
        void LogAttachResidual(CarryHandler carryHandler)
        {
            var socket = carryHandler.GetSocket(objectToCarry.carrySocket);
            if (socket == null) return;
            Vector3 expectedPos = socket.TransformPoint(objectToCarry.carrySocketPosOffset);
            Quaternion expectedRot = socket.rotation * Quaternion.Euler(objectToCarry.carrySocketRotOffset);
            Debug.Log($"[CarryHotspot] {objectToCarry.CurrentPoseName}: attach residual " +
                      $"{Vector3.Distance(expectedPos, objectToCarry.transform.position):F3} m / " +
                      $"{Quaternion.Angle(expectedRot, objectToCarry.transform.rotation):F1}°", this);
        }

    }
}
