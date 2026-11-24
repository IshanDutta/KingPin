using FS_Core;
using UnityEngine;

namespace FS_ShooterSystem
{
    /// <summary>
    /// Central tuning for the procedural aiming + weapon IK system. Consolidates
    /// the body-posture weights, arm-IK weights, hand reference axes and clamp
    /// angles that were previously hardcoded across <see cref="ShooterFighter"/>
    /// and <see cref="ShootingAimController"/>.
    ///
    /// A weapon without an assigned profile falls back to a runtime default whose
    /// values match the previous hardcoded behavior, so this is drop-in safe.
    /// </summary>
    [CreateAssetMenu(menuName = "Shooter System/Create Aim Rig Profile")]
    public class AimRigProfile : ScriptableObject
    {
        [Header("Body Posture")]
        [Tooltip("Max horizontal (yaw) angle the upper body will rotate toward the aim point before snapping back to neutral.")]
        public float maxHorizontalAngle = 70f;

        [Tooltip("Max vertical (pitch) angle the upper body will tilt toward the aim point before snapping back to neutral.")]
        public float maxVerticalAngle = 70f;

        [Tooltip("Smoothing speed used when easing the spine chain toward the aim direction.")]
        public float bodyRotationSpeed = 10f;

        [Tooltip("Fraction of the aim angle applied at the spine bone. Default 0 matches the legacy upperChest-only posture; raise for more upper-body lean, but re-tune the hand IK grip/elbow afterwards.")]
        [Range(0f, 1f)] public float spineWeight = 0f;

        [Tooltip("Fraction of the aim angle applied at the chest bone. Default 0 matches the legacy upperChest-only posture; raise for more upper-body lean, but re-tune the hand IK grip/elbow afterwards.")]
        [Range(0f, 1f)] public float chestWeight = 0f;

        [Tooltip("Fraction of the aim angle applied at the upper-chest bone.")]
        [Range(0f, 1f)] public float upperChestWeight = 0.4f;

        [Header("Arm IK")]
        [Tooltip("Blend weight for the upper-arm bone during aim alignment.")]
        [Range(0f, 1f)] public float upperArmWeight = 1f;

        [Tooltip("Blend weight for the lower-arm (forearm) bone during aim alignment.")]
        [Range(0f, 1f)] public float lowerArmWeight = 1f;

        [Tooltip("Blend weight for the hand bone during aim alignment.")]
        [Range(0f, 1f)] public float handWeight = 1f;

        [Tooltip("Speed at which arm bones ease toward their aligned aim pose.")]
        public float alignSmoothSpeed = 100f;

        [Tooltip("Speed at which the master aim IK weight blends in/out (aim<->idle, reload, weapon swap). Higher = snappier.")]
        public float ikBlendSpeed = 6f;

        [Tooltip("Minimum distance from the aim origin to the aim point. Closer points are pushed out along the aim direction to keep the arm IK stable at point-blank range.")]
        public float minIKAimDistance = 3f;

        [Header("Hand Reference Axes")]
        [Tooltip("Which local axis of the right hand bone points 'forward' along the weapon.")]
        public DirectionAxis rightHandForward = DirectionAxis.PositiveY;

        [Tooltip("Which local axis of the right hand bone points 'up'.")]
        public DirectionAxis rightHandUp = DirectionAxis.PositiveZ;

        [Tooltip("Which local axis of the left hand bone points 'forward' along the weapon.")]
        public DirectionAxis leftHandForward = DirectionAxis.PositiveY;

        [Tooltip("Which local axis of the left hand bone points 'up'.")]
        public DirectionAxis leftHandUp = DirectionAxis.PositiveZ;

        [Header("Aim Clamp")]
        [Tooltip("Max angle between character forward and the aim direction. The aim point is clamped within this cone (horizontal).")]
        public float maxAimAngleFromForward = 75f;

        [Header("Head Look")]
        [Tooltip("Target weight for the head look-at while aiming.")]
        [Range(0f, 1f)] public float headLookWeight = 0.3f;

        [Tooltip("Blend speed for easing the head look-at weight in and out.")]
        public float headLookBlendSpeed = 0.1f;
    }
}
