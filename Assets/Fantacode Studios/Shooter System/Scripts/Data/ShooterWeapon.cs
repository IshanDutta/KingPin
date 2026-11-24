using FS_CombatCore;
using FS_Core;
using FS_LocomotionSystem;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FS_ShooterSystem
{
    [CustomIcon(FolderPath.DataIcons + "Shooter Weapon Icon.png")]
    [Icon(FolderPath.DataIcons + "Shooter Weapon Icon.png")]
    [CreateAssetMenu(menuName = "Shooter System/Create Weapon")]
    public class ShooterWeapon : CombatWeapon
    {
        [Tooltip("The data for the ammo used by this weapon.")]
        public ShooterAmmo ammoData;

        [Tooltip("The maximum effective range of the weapon.")]
        public float range = 100f;

        [Tooltip("Use the length of the reload animation as the reload time.")]
        public bool useReloadAnimationDuration = true;

        [Tooltip("The time required to reload the weapon (in seconds).")]
        public float reloadTime = 2f;

        [Tooltip("If true, the weapon play a ammo load animation after each shot (e.g., bolt-action sniper).")]
        public bool playLoadAnimationPerShot = false;

        [Tooltip("Normalized time (0 to 1) in the reload animation when the ammo (e.g., arrow) becomes available or visible.")]
        [Range(0,1)]
        public float ammoEnableTime = 0f;

        [Tooltip("The radius in which enemies can hear this weapon when fired.")]
        public float soundRange = 50f;

        [Tooltip("Audio clip for the firing sound.")]
        public AudioClip fireAudioClip;

        [Tooltip("Audio clip for the trigger sound when out of ammo.")]
        public AudioClip triggerAudioClip;

        [Tooltip("Automatically reload when out of ammo.")]
        public bool autoReload;

        [Tooltip("If true, the player cannot manually reload this weapon. Useful for weapons that reload automatically or use single-use ammo.")]
        public bool preventManualReload = false;

        [Tooltip("Audio clip for the reload sound.")]
        public AudioClip reloadAudioClip;

        [Tooltip("Audio clip for load ammo.")]
        public AudioClip loadAmmoAudioClip;

        [Tooltip("Indicates if the weapon fires in burst mode.")]
        public bool isBurst;

        [Tooltip("The type of fire mode (Single or Auto).")]
        public FireType fireType;

        [Tooltip("The number of bullets fired in a single burst.")]
        public int burstFireBulletCount = 3;

        [Tooltip("The range of bullet spread angles during firing, defined as minimum and maximum values.")]
        public Vector2 burstSpreadingAngleRange = new Vector2(2.5f, 5f);

        [Tooltip("Information about the weapon's recoil.")]
        public RecoilInfo weaponRecoilInfo;

        [Tooltip("The radius of bullet spread.")]
        public float bulletSpreadRadius = 0.1f;

        [Tooltip("Enable recoil effect.")]
        public bool enableRecoil = true;

        [Tooltip("Allow firing without aiming.")]
        public bool hipFire = false;

        [Tooltip("Allow aiming without ammo.")]
        public bool canAimWithoutAmmo = true;

        [Tooltip("Allow jumping while the character is in aiming.")]
        public bool allowJumpingWhileAiming = true;

        [Tooltip("Enable zoom while aiming.")]
        public bool zoomWhileAim = true;

        [Tooltip("Settings for the camera when aiming the weapon.")]
        public CameraSettings aimCameraSettings = new CameraSettings() { distance = 1f, framingOffset = new Vector3(.4f, 1.5f, 0), followSmoothTime = 0f, sensitivity = .2f };

        // Animations
        [Tooltip("Animation clip for aiming.")]
        public AnimGraphClipInfo aimClip;

        [Tooltip("Animation clip for reloading.")]
        public AnimGraphClipInfo reloadClip;
        [Tooltip("Specifies which body parts the reload animation will affect")]
        public Mask reloadAnimationMask = Mask.Arm;

        [Tooltip("Animation clip for reloading.")]
        public AnimGraphClipInfo loadAmmoClip;

        [Tooltip("Animation clip for shooting.")]
        public AnimGraphClipInfo shootClip;

        [Tooltip("Should the target play a reaction animation hit by this weapon.")]
        public bool playReactionOnTarget = true;

        [Tooltip("Tag used to identify the reaction for this weapon like light, medium, heavy, etc.")]
        public string targetReactionTag;

        [Tooltip("Specifies which body parts the aiming animation will affect")]
        public Mask aimAnimationMask = Mask.Arm;
        public Mask shootAnimationMask = Mask.Arm;


        [Tooltip("Optional 2D locomotion clip set used while aiming. Overrides base locomotion when aiming and grounded.")]
        public LocomotionClipSet2D aimedLocomotionClipSet2D;

        [Tooltip("Enable pressure based shot strength (hold to draw/release, e.g. bow & arrow).")]
        public bool isChargedWeapon;
        [Tooltip("Animation clip played while the shot is being charged/drawn.")]
        public AnimGraphClipInfo chargeClip;
        public Mask chargeAnimationMask = Mask.RightArm;
        public float chargeForce = 1;


        [Tooltip("Enable IK adjustments during the weapon's idle state for proper hand positioning")]
        public bool UseHandIkForIdle = true;

        [Tooltip("Procedural aiming & weapon-IK tuning (body posture weights, arm-IK weights, hand axes, clamp angles). If left empty, built-in defaults are used.")]
        public AimRigProfile aimProfile;

        [Tooltip("Upper-body (3-bone: spine, chest, upper chest) aim rotation settings. Only used when the ShooterFighter's Aiming Mode is set to New.")]
        public UpperBodyIKSettings upperBodyIK = new UpperBodyIKSettings();

        [Tooltip("Support-hand (off hand) IK settings for New aiming mode. Keeps the support hand on the weapon. Only used when the ShooterFighter's Aiming Mode is set to New.")]
        public HandIKSettings supportHandIK = new HandIKSettings();

        [Tooltip("Hold-hand (main / weapon hand) IK settings for New aiming mode. Fine-tunes the weapon hand's grip. Off by default (opt-in). Only used when the ShooterFighter's Aiming Mode is set to New.")]
        public HandIKSettings holdHandIK = new HandIKSettings { enableAim = false };

        public ArmIkData idleIKData;
        public ArmIkData aimIKData;

        public List<ArmIKReference> iKReferences = new List<ArmIKReference>();

        public override void SetCategory()
        {
            category = Resources.Load<ItemCategory>("Category/Shooter Weapon");
            aimProfile = Resources.Load<AimRigProfile>("Aim Rig Profile");
        }
        public override HitType HitType => HitType.Ranged;
    }

    [System.Serializable]
    public class ArmIKReference
    {
        [Tooltip("List of character IDs that this IK configuration applies to.")]
        public List<CharacterReference> characterReferences = new List<CharacterReference>();

        [Tooltip("IK data used when the character is in an idle state.")]
        public ArmIkData idleIKData;

        [Tooltip("IK data used when the character is aiming.")]
        public ArmIkData aimIKData;
    }

    /// <summary>
    /// Weapon-specific tuning for the New aiming architecture's upper-body (3-bone)
    /// rotation. Drives <see cref="UpperBodyAimSolver"/>; only used while the
    /// <see cref="ShooterFighter"/>'s <see cref="AimingMode"/> is set to New.
    /// </summary>
    [System.Serializable]
    public class UpperBodyIKSettings
    {
        [Header("Upper Body IK Weights")]
        [Tooltip("Fraction of the aim rotation applied at the spine bone (0 = none, 1 = full).")]
        [Range(0f, 1f)] public float spineWeight = 0.3f;

        [Tooltip("Fraction of the aim rotation applied at the chest bone (0 = none, 1 = full).")]
        [Range(0f, 1f)] public float chestWeight = 0.3f;

        [Tooltip("Fraction of the aim rotation applied at the upper-chest bone (0 = none, 1 = full).")]
        [Range(0f, 1f)] public float upperChestWeight = 0.4f;

        [Header("Rotation Offset")]
        [Tooltip("Additional constant twist (in degrees) around the upper body's vertical axis while aiming, layered on top of the automatic aim alignment. Use for a stylistic torso twist/bladed stance; 0 keeps the weapon pointed straight at the target.")]
        public float yRotationOffset = 0f;

        [Header("Smoothing")]
        [Tooltip("How quickly the upper body eases toward the aim rotation (higher = snappier). Also controls how fast the posture blends in/out when aiming starts and stops.")]
        public float rotationSmoothSpeed = 10f;
    }

    /// <summary>
    /// New-aiming hand grip: where a hand sits on the weapon and where its elbow
    /// points. Used for both the support (off) hand and the hold (main) hand. Separate
    /// per weapon and (via <see cref="HandIKReference"/>) per character, mirroring the
    /// legacy <see cref="ArmIkData"/> model.
    /// </summary>
    [System.Serializable]
    public class HandIKData
    {
        [Tooltip("Local position offset (weapon space) applied to the hand relative to its animated pose — nudges the hand onto the weapon grip.")]
        public Vector3 positionOffset;

        [Tooltip("Local rotation offset (in degrees) applied to the hand relative to its animated pose.")]
        public Vector3 rotationOffset;

        [Tooltip("Elbow pole offset (character space) for the arm. Nudges where the elbow points; zero keeps the animation's natural elbow direction.")]
        public Vector3 elbowOffset;
    }

    /// <summary>
    /// Per-character override of a New-aiming hand grip — applied when the aiming
    /// character matches one of the listed character IDs / avatars. Mirrors
    /// <see cref="ArmIKReference"/>.
    /// </summary>
    [System.Serializable]
    public class HandIKReference
    {
        [Tooltip("Characters this grip applies to (matched by character ID or avatar).")]
        public List<CharacterReference> characterReferences = new List<CharacterReference>();

        [Tooltip("Hand grip + elbow used while aiming, for the referenced characters.")]
        public HandIKData aimData = new HandIKData();

        [Tooltip("Hand grip + elbow used while idle (not aiming), for the referenced characters.")]
        public HandIKData idleData = new HandIKData();
    }

    /// <summary>
    /// Weapon-specific tuning for a New-aiming hand IK — a two-bone IK that keeps one
    /// hand (support or hold) on the weapon. Only used while the
    /// <see cref="ShooterFighter"/>'s <see cref="AimingMode"/> is New. Carries separate
    /// aim and idle grips (default + per-character overrides), independent of the legacy
    /// <see cref="ArmIkData"/> used by the old aiming path.
    /// </summary>
    [System.Serializable]
    public class HandIKSettings
    {
        [Header("Aiming")]
        [Tooltip("Enable this hand's IK while aiming. Turn off for one-handed weapons (e.g. pistols) whose off hand does not grip the weapon.")]
        public bool enableAim = true;

        [Header("Idle")]
        [Tooltip("Enable this hand's IK while idle (weapon equipped, not aiming). Uses the separate Idle grip below.")]
        public bool enableIdle = false;

        [Header("Blend")]
        [Tooltip("Target blend weight for this hand's IK (0 = off / animation only, 1 = fully IK-driven). Applies to both aim and idle.")]
        [Range(0f, 1f)] public float weight = 1f;

        [Tooltip("How quickly the IK weight blends in/out as it engages and disengages.")]
        public float weightBlendSpeed = 10f;

        [Header("Default Grips (per-character overrides in References)")]
        [Tooltip("Default hand grip + elbow used while aiming, when no per-character override matches.")]
        public HandIKData aimData = new HandIKData();

        [Tooltip("Default hand grip + elbow used while idle, when no per-character override matches.")]
        public HandIKData idleData = new HandIKData();

        [Tooltip("Per-character grip overrides (aim + idle). Author these with the Support Hand IK Setup window.")]
        public List<HandIKReference> references = new List<HandIKReference>();
    }

    [System.Serializable]
    public class ArmIkData
    {
        [Header("Main Hand (Holding Weapon)")]
        [Tooltip("Local position offset for the main (holding) hand relative to its IK target.")]
        public Vector3 holdHandPosition;

        [Tooltip("Local rotation offset for the main (holding) hand in degrees.")]
        public Vector3 holdHandRotation;

        [Space(10)]
        [Header("Support Hand")]
        [Tooltip("Local position offset for the support hand relative to its IK target.")]
        public Vector3 supportHandPosition;

        [Tooltip("Local rotation offset for the support hand in degrees.")]
        public Vector3 supportHandRotation;

        [Space(10)]
        [Header("Elbow Poles")]
        [Tooltip("Elbow pole offset for the main (holding) arm, in character space. Nudges where the elbow points; zero keeps the animation's natural elbow direction.")]
        public Vector3 holdElbowPosition;

        [Tooltip("Elbow pole offset for the support arm, in character space. Nudges where the elbow points; zero keeps the animation's natural elbow direction.")]
        public Vector3 supportElbowPosition;
    }

    public enum FireType { Single, Auto }
}
