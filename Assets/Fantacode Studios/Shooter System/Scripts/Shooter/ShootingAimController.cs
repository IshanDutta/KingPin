using FS_Core;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class ShootingAimController
    {
        private BoneAlignment upperArmAlignment;
        private BoneAlignment lowerArmAlignment;
        public BoneAlignment handAlignment;
        public BoneAlignment aimAlignment;

        private Transform mainShoulderBone;

        // Character root, used to interpret authored elbow pole offsets in character space.
        private Transform root;

        private Transform supportUpperArmBone;
        private Transform supportLowerArmBone;
        private Transform supportHandBone;
        private Transform supportShoulderBone;

        private DirectionAxis handForwardReferenceAxis = DirectionAxis.PositiveY;
        private DirectionAxis handUpReferenceAxis = DirectionAxis.NegativeZ;

        private Vector3 HandForwardVector => handForwardReferenceAxis switch
        {
            DirectionAxis.PositiveX => handAlignment.Bone.right,
            DirectionAxis.NegativeX => -handAlignment.Bone.right,
            DirectionAxis.PositiveY => handAlignment.Bone.up,
            DirectionAxis.NegativeY => -handAlignment.Bone.up,
            DirectionAxis.PositiveZ => handAlignment.Bone.forward,
            DirectionAxis.NegativeZ => -handAlignment.Bone.forward,
            _ => handAlignment.Bone.forward
        };

        private Vector3 HandUpVector => handUpReferenceAxis switch
        {
            DirectionAxis.PositiveX => handAlignment.Bone.right,
            DirectionAxis.NegativeX => -handAlignment.Bone.right,
            DirectionAxis.PositiveY => handAlignment.Bone.up,
            DirectionAxis.NegativeY => -handAlignment.Bone.up,
            DirectionAxis.PositiveZ => handAlignment.Bone.forward,
            DirectionAxis.NegativeZ => -handAlignment.Bone.forward,
            _ => handAlignment.Bone.up
        };

        public ShootingAimController(
            Animator animator,
            DirectionAxis forwardAxis,
            DirectionAxis upAxis,
            HumanBodyBones upperArmBoneType,
            HumanBodyBones lowerArmBoneType,
            HumanBodyBones handBoneType,
            HumanBodyBones shoulderBoneType,
            HumanBodyBones supportHandBoneType,
            HumanBodyBones supportLowerArmBoneType,
            HumanBodyBones supportUpperArmBoneType,
            HumanBodyBones supportShoulderBoneType,
            float upperArmWeight,
            float lowerArmWeight,
            float handWeight,
            Transform aimTransform
            )
        {
            upperArmAlignment = new BoneAlignment(animator.GetBoneTransform(upperArmBoneType), null, upperArmWeight);
            lowerArmAlignment = new BoneAlignment(animator.GetBoneTransform(lowerArmBoneType), upperArmAlignment.Helper, lowerArmWeight);
            handAlignment = new BoneAlignment(animator.GetBoneTransform(handBoneType), lowerArmAlignment.Helper, handWeight);
            aimAlignment = new BoneAlignment(aimTransform, handAlignment.Helper, 1);

            mainShoulderBone = animator.GetBoneTransform(shoulderBoneType);
            root = animator.transform;

            supportHandBone = animator.GetBoneTransform(supportHandBoneType);
            supportLowerArmBone = animator.GetBoneTransform(supportLowerArmBoneType);
            supportUpperArmBone = animator.GetBoneTransform(supportUpperArmBoneType);
            supportShoulderBone = animator.GetBoneTransform(supportShoulderBoneType);

            handForwardReferenceAxis = forwardAxis;
            handUpReferenceAxis = upAxis;

            SaveDefaultAlignment();
        }

        public void DestroyAimHelpers()
        {
            GameObject.Destroy(upperArmAlignment.Helper.gameObject);
            GameObject.Destroy(lowerArmAlignment.Helper.gameObject);
            GameObject.Destroy(handAlignment.Helper.gameObject);
            GameObject.Destroy(aimAlignment.Helper.gameObject);
        }

        public void SaveDefaultAlignment()
        {
            upperArmAlignment.SetDefaultAlignment();
            lowerArmAlignment.SetDefaultAlignment();
            handAlignment.SetDefaultAlignment();
            aimAlignment.SetDefaultAlignment();

            upperArmAlignment.SaveDefaultState();
            lowerArmAlignment.SaveDefaultState();
            handAlignment.SaveDefaultState();
            aimAlignment.SaveDefaultState();
        }

        public void ResetToDefaultAlignment()
        {
            upperArmAlignment.ResetToDefaultState();
            lowerArmAlignment.ResetToDefaultState();
            handAlignment.ResetToDefaultState();
            aimAlignment.ResetToDefaultState();
        }

        public void AlignBones(Vector3 aimTargetPosition, bool isFiring, bool resetToDefault, float smoothSpeed = 100f, Vector3 aimPosOffset = new Vector3(), Vector3 aimRotOffset = new Vector3(), Vector3 rotOffs = new Vector3(), bool applyHandIK = true, float ikWeight = 1f, Vector3 elbowOffset = new Vector3())
        {
            if(applyHandIK)
                ApplyArmIK(aimPosOffset, aimRotOffset, false, ikWeight, elbowOffset);

            if (isFiring) ResetToDefaultAlignment();
            else SaveDefaultAlignment();


            AdjustBoneAlignment(upperArmAlignment, aimTargetPosition, upperArmAlignment.weight, resetToDefault, smoothSpeed, rotOffs);
            AdjustBoneAlignment(lowerArmAlignment, aimTargetPosition, lowerArmAlignment.weight, resetToDefault, smoothSpeed, rotOffs);
            AdjustBoneAlignment(handAlignment, aimTargetPosition, handAlignment.weight, resetToDefault, smoothSpeed, rotOffs);
        }
        private void AdjustBoneAlignment(BoneAlignment boneAlignment, Vector3 aimTargetPosition, float weight, bool resetToDefault, float smoothSpeed = 1f, Vector3 aimRotOffset = new Vector3())
        {
            Vector3 targetDirection = aimTargetPosition - aimAlignment.Helper.position;
            Quaternion targetRotation = Quaternion.LookRotation(targetDirection, aimAlignment.Bone.up);

            Vector3 currentDirectionLocal = boneAlignment.Helper.InverseTransformDirection(aimAlignment.Helper.forward);
            Vector3 targetDirectionLocal = boneAlignment.Helper.InverseTransformDirection(targetRotation * Vector3.forward);

            Quaternion rotationDifference = Quaternion.FromToRotation(currentDirectionLocal, targetDirectionLocal);
            boneAlignment.currentWeight = Mathf.MoveTowards(boneAlignment.currentWeight, resetToDefault ? 0 : weight, smoothSpeed * Time.deltaTime);

            boneAlignment.currentRotation = resetToDefault
                ? Quaternion.Lerp(boneAlignment.Bone.localRotation * rotationDifference, boneAlignment.Bone.localRotation, 1 - boneAlignment.currentWeight)
                : Quaternion.Lerp(boneAlignment.Bone.localRotation, boneAlignment.Bone.localRotation * rotationDifference, boneAlignment.currentWeight);

            boneAlignment.Bone.localRotation = boneAlignment.currentRotation;
            boneAlignment.Helper.localRotation = boneAlignment.currentRotation;
        }

        private void ApplyTwoBoneIK(Vector3 targetPosition, Vector3 targetRotationEuler, Transform upperArm, Transform lowerArm, Transform hand, Transform shoulder, float ikWeight = 1f, Vector3 elbowOffset = new Vector3())
        {
            if (ikWeight <= 0f) return;
            if (upperArm == null || lowerArm == null || hand == null) return;

            Vector3 originalLowerArmPosition = lowerArm.position;
            Vector3 originalHandPosition = hand.position;
            Quaternion originalUpperArmRotation = upperArm.rotation;
            Quaternion originalLowerArmRotation = lowerArm.rotation;
            Quaternion originalHandRotation = hand.rotation;

            float upperArmLength = Vector3.Distance(upperArm.position, lowerArm.position);
            float lowerArmLength = Vector3.Distance(lowerArm.position, hand.position);
            float totalArmLength = upperArmLength + lowerArmLength;

            if (upperArmLength < 0.001f || lowerArmLength < 0.001f) return;

            // Clamp the target within the arm's reach so we don't overstretch.
            Vector3 toTarget = targetPosition - upperArm.position;
            float distanceToTarget = toTarget.magnitude;
            if (distanceToTarget > totalArmLength)
                targetPosition = upperArm.position + toTarget.normalized * totalArmLength;

            Vector3 shoulderToTarget = targetPosition - upperArm.position;
            float targetDistance = Mathf.Clamp(shoulderToTarget.magnitude,
                                               Mathf.Abs(upperArmLength - lowerArmLength) + 0.001f,
                                               totalArmLength - 0.001f);
            Vector3 targetDirection = shoulderToTarget.normalized;

            // Elbow interior angle (law of cosines) required to reach the target.
            float cosAngle = (upperArmLength * upperArmLength + targetDistance * targetDistance - lowerArmLength * lowerArmLength)
                             / (2f * upperArmLength * targetDistance);
            cosAngle = Mathf.Clamp(cosAngle, -1f, 1f);
            float elbowAngle = Mathf.Acos(cosAngle);

            // ELBOW POLE: the point the elbow bends toward. By default this is the
            // animation's own elbow position, so the bend matches the animation and
            // (unlike the old projection of the elbow onto the aim axis) never flips
            // as the aim direction sweeps past the arm. An authored elbowOffset
            // (character space, from the weapon's IK data) nudges that pole point so
            // artists can steer the elbow. Zero offset = pure animation pole.
            Vector3 polePoint = lowerArm.position;
            if (elbowOffset != Vector3.zero && root != null)
                polePoint += root.TransformVector(elbowOffset);

            Vector3 elbowBendDirection = Vector3.ProjectOnPlane(polePoint - upperArm.position, targetDirection);
            if (elbowBendDirection.sqrMagnitude < 0.0001f) elbowBendDirection = Vector3.ProjectOnPlane(lowerArm.position - upperArm.position, targetDirection);
            if (elbowBendDirection.sqrMagnitude < 0.0001f) elbowBendDirection = Vector3.ProjectOnPlane(-upperArm.up, targetDirection);
            if (elbowBendDirection.sqrMagnitude < 0.0001f) elbowBendDirection = Vector3.up;
            elbowBendDirection.Normalize();

            // Solve the elbow position by rotating the aim axis up toward the pole
            // by the interior elbow angle.
            Vector3 elbowRotationAxis = Vector3.Cross(targetDirection, elbowBendDirection).normalized;
            Vector3 elbowDirection = Quaternion.AngleAxis(elbowAngle * Mathf.Rad2Deg, elbowRotationAxis) * targetDirection;
            Vector3 desiredElbowPosition = upperArm.position + elbowDirection * upperArmLength;
            Vector3 clampedTargetPosition = upperArm.position + targetDirection * targetDistance;

            // Aim the upper arm at the solved elbow.
            Vector3 newUpperArmDirection = (desiredElbowPosition - upperArm.position).normalized;
            Quaternion upperArmRotationDelta = Quaternion.FromToRotation((lowerArm.position - upperArm.position).normalized, newUpperArmDirection);
            upperArm.rotation = Quaternion.Slerp(originalUpperArmRotation, upperArmRotationDelta * upperArm.rotation, ikWeight);

            // Move the elbow to the solved position.
            lowerArm.position = Vector3.Lerp(originalLowerArmPosition, desiredElbowPosition, ikWeight);

            // Aim the forearm from the elbow to the target so the hand reaches it
            // exactly — the bend closes the gap, so the hand bone is not stretched.
            Vector3 newLowerArmDirection = (clampedTargetPosition - desiredElbowPosition).normalized;
            Quaternion lowerArmRotationDelta = Quaternion.FromToRotation((hand.position - lowerArm.position).normalized, newLowerArmDirection);
            lowerArm.rotation = Quaternion.Slerp(originalLowerArmRotation, lowerArmRotationDelta * lowerArm.rotation, ikWeight);

            // Place & orient the hand at the grip.
            hand.position = Vector3.Lerp(originalHandPosition, targetPosition, ikWeight);
            hand.rotation = Quaternion.Slerp(originalHandRotation, Quaternion.Euler(targetRotationEuler), ikWeight);
        }


        public void ApplyArmIK(Vector3 targetPosition, Vector3 targetRotationEuler, bool applyToSupportArm = true, float ikWeight = 1f, Vector3 elbowOffset = new Vector3())
        {
            if (applyToSupportArm)
                ApplyTwoBoneIK(targetPosition, targetRotationEuler, supportUpperArmBone, supportLowerArmBone, supportHandBone, supportShoulderBone, ikWeight, elbowOffset);
            else
                ApplyTwoBoneIK(targetPosition, targetRotationEuler, upperArmAlignment.Bone, lowerArmAlignment.Bone, handAlignment.Bone, mainShoulderBone, ikWeight, elbowOffset);
        }
    }

    public class BoneAlignment
    {
        public Transform Bone { get; private set; }
        public Transform Helper { get; private set; }

        private Quaternion defaultLocalRotation;
        private Vector3 defaultLocalPosition;

        public float currentWeight;
        public float weight;
        public Quaternion currentRotation;

        public BoneAlignment(Transform boneTransform, Transform parentTransform, float alignmentWeight = 1f)
        {
            Bone = boneTransform;
            if (boneTransform != null)
            {
                Helper = new GameObject($"{boneTransform.name}_Helper").transform;
                Helper.parent = parentTransform != null ? parentTransform : boneTransform.parent;
            }
            weight = alignmentWeight;
        }

        public void SetDefaultAlignment()
        {
            if (Helper == null) return;
            Helper.SetPositionAndRotation(Bone.position, Bone.rotation);
        }

        public void SaveDefaultState()
        {
            if (Helper == null) return;
            defaultLocalRotation = Helper.localRotation;
            defaultLocalPosition = Helper.localPosition;
        }

        public void ResetToDefaultState()
        {
            if (Helper == null) return;
            Helper.localRotation = defaultLocalRotation;
            Helper.localPosition = defaultLocalPosition;
        }
    }
}
