using FS_CombatCore;
using FS_Core;
using System;
using System.Collections;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Random = UnityEngine.Random;
using FS_Util;
using FS_LocomotionSystem;

namespace FS_ShooterSystem
{
    public class ShooterFighter : MonoBehaviour
    {
        public ShooterWeapon CurrentWeapon
        {
            get
            {
                return itemEquipper?.EquippedItem is ShooterWeapon shooterWeaponData ? shooterWeaponData : null;
            }
        }
        public FighterCore Fighter { get; private set; }

        // Body-posture angles/weights, hand reference axes and IK tuning now live
        // on AimRigProfile (per-weapon, with a built-in default fallback). See
        // ResolveProfile / ActiveAimProfile below.
        private AimRigProfile defaultAimProfile;

        // Only the custom independent AI can take over the authored weapon pose.
        // Player shooters and ordinary Fantacode AI retain the original IK pipeline.
        private CombatAIController independentCombatAI;
        private bool independentPoseWasOwned;
        private bool independentIdleLoopReleased;
        private float nextIndependentPoseSearch;
        private float nextIndependentPoseLog;
        private bool independentPoseLinkLogged;
        private float nextWeaponTrackingLog;
        private bool IndependentPoseOwned => independentCombatAI != null &&
            independentCombatAI.OwnsIndependentWeaponPose && !IkSetupMode;

        private void ResolveIndependentCombatAI()
        {
            if (independentCombatAI != null) return;
            independentCombatAI = GetComponent<CombatAIController>();
            if (independentCombatAI == null) independentCombatAI = GetComponentInParent<CombatAIController>();
            if (independentCombatAI == null)
            {
                // Only accept a child controller that drives this same Animator.
                foreach (var candidate in GetComponentsInChildren<CombatAIController>(true))
                    if (candidate.Animator != null && candidate.Animator == Animator)
                    { independentCombatAI = candidate; break; }
            }
        }

        /// <summary>
        /// Returns the tuning profile for the given weapon, or a shared runtime
        /// default (values matching the previous hardcoded behavior) when the
        /// weapon has none assigned.
        /// </summary>
        private AimRigProfile ResolveProfile(ShooterWeapon weapon)
        {
            if (weapon != null && weapon.aimProfile != null) return weapon.aimProfile;
            if (defaultAimProfile == null)
                defaultAimProfile = ScriptableObject.CreateInstance<AimRigProfile>();
            return defaultAimProfile;
        }

        /// <summary>Tuning profile for the currently equipped weapon (falls back to a default when none is assigned).</summary>
        public AimRigProfile ActiveAimProfile => ResolveProfile(CurrentWeapon);

        /// <summary>
        /// Resolves the arm IK data (hand offsets + elbow poles) for the current
        /// weapon and character, matching the per-character override lookup used for
        /// the hand IK targets.
        /// </summary>
        public ArmIkData ResolveIkData(bool aiming) => existingArmIKReference != null
            ? (aiming ? existingArmIKReference.aimIKData : existingArmIKReference.idleIKData)
            : (aiming ? CurrentWeapon.aimIKData : CurrentWeapon.idleIKData);

        /// <summary>
        /// Resolves a New-aiming hand grip (position/rotation/elbow offset) for the
        /// current weapon and character — the per-character override when one matches,
        /// otherwise the weapon default. <paramref name="support"/> selects the support
        /// (off) hand vs the hold (main) hand; <paramref name="aiming"/> selects the aim
        /// grip vs the idle grip. Mirrors <see cref="ResolveIkData"/>.
        /// </summary>
        public HandIKData ResolveHandIKData(bool support, bool aiming)
        {
            if (CurrentWeapon == null) return null;
            var s = support ? CurrentWeapon.supportHandIK : CurrentWeapon.holdHandIK;
            var reference = support ? existingSupportRef : existingHoldRef;
            return reference != null
                ? (aiming ? reference.aimData : reference.idleData)
                : (aiming ? s.aimData : s.idleData);
        }

        /// <summary>Character root — elbow pole offsets are authored/interpreted in this space.</summary>
        public Transform CharacterRoot => transform;

        /// <summary>Lower-arm (elbow) bone of the weapon-holding arm, or null if unavailable.</summary>
        public Transform HoldLowerArm => (Animator != null && CurrentWeapon != null)
            ? Animator.GetBoneTransform(CurrentWeapon.holderBone == HumanBodyBones.RightHand ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm)
            : null;

        /// <summary>Lower-arm (elbow) bone of the support arm, or null if unavailable.</summary>
        public Transform SupportLowerArm => (Animator != null && CurrentWeapon != null)
            ? Animator.GetBoneTransform(CurrentWeapon.holderBone == HumanBodyBones.RightHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm)
            : null;

        [Space(10)]
        [Tooltip("Which aiming implementation runs each frame. Legacy = the original procedural spine + arm/hand IK aiming. New = the new aiming architecture (in development).")]
        [SerializeField] private AimingMode aimingMode = AimingMode.New;

        /// <summary>Which aiming implementation drives aim posture and weapon IK (see <see cref="AimingMode"/>).</summary>
        public AimingMode AimingMode => aimingMode;

        /// <summary>True when the original procedural aiming system should run (the only mode implemented today).</summary>
        public bool IsLegacyAiming => aimingMode == AimingMode.Legacy;
        // public bool IsLegacyAiming => false;

        [Space(10)]
        [Tooltip("If true, the fighter has unlimited ammo for the equipped weapons.")]
        public bool hasInfiniteAmmo = false;
        public bool canRoll = true;
        public bool canDodge = true;


        #region Variables

        private Transform spine;
        private Transform chest;
        private Transform upperChest;

        private ArmIKReference existingArmIKReference;

        public Animator Animator { get; private set; }
        private AnimGraph animGraph;
        private ItemEquipper itemEquipper;
        private LocomotionCore locomotionCore;

        public CharacterID characterID { get; private set; }

        private bool waitForNextFire;

        // Procedural aim collaborators (see AimPostureSolver / AimIKWeightController / IAimProvider).
        private AimPostureSolver postureSolver;
        private readonly AimIKWeightController aimWeightController = new AimIKWeightController();
        private IAimProvider aimProvider;

        // New aiming architecture — upper-body (3-bone) rotation. Only ticked while
        // AimingMode.New is selected (see UpdateNewAim). Tuning is weapon-specific
        // (ShooterWeapon.upperBodyIK) rather than from the legacy AimRigProfile.
        private UpperBodyAimSolver upperBodyAimSolver;

        // New aiming architecture — eased hand-IK blend weights (0..1), one per hand.
        // Kept separate from the legacy aimWeightController so New/legacy never share state.
        private float supportHandIKWeight;
        private float holdHandIKWeight;

        // Legacy aiming — eased weight for the idle grips, so they release smoothly
        // when an authored action animation takes the arms over (see IsPlayingActionAnimation).
        private float idleHandIKWeight = 1f;

        // Cached per-character grip overrides for the current weapon/character (null =
        // use the weapon default). Resolved on equip, mirroring existingArmIKReference.
        private HandIKReference existingSupportRef;
        private HandIKReference existingHoldRef;

        public Action OnStartAim;
        public Action OnStopAim;
        public Action OnFire;
        public Action OnStartReload;
        public Action OnHitDamagable;
        public Action OnTargetLocked;   // Target is currently under the aim
        public Action OnTargetCleared;  // Target is no longer under the aim

        bool enableIk = true;
        bool aimingAnimationPlaying;
        bool isAimedLocomotionOverrideActive;

        Transform rightHand;
        Transform leftHand;

        #endregion

        #region Properties

        public bool IsShooterWeaponEquipped => itemEquipper?.EquippedItem is ShooterWeapon;
        public bool IsInScopeView { get; set; }
        public bool IsWeaponEquipped => IsShooterWeaponEquipped;

        public ShooterWeaponObject CurrentWeaponRight => itemEquipper?.EquippedItemRight as ShooterWeaponObject;
        public ShooterWeaponObject CurrentWeaponLeft => itemEquipper?.EquippedItemLeft as ShooterWeaponObject;
        private ShootingAimController rightHandAimController => CurrentWeaponRight?.aimController;
        private ShootingAimController leftHandAimController => CurrentWeaponLeft?.aimController;

        public ShooterWeaponObject CurrentShooterWeaponObject => itemEquipper.EquippedItemObject is ShooterWeaponObject ? itemEquipper.EquippedItemObject as ShooterWeaponObject : null;

        public bool IsShooting => Fighter.Action == FighterAction.Shooting;
        public bool IsLoadingAmmo { get; set; }
        public bool IsAiming { get; private set; }
        public bool IsReloading => Fighter.Action == FighterAction.Reloading;
        public bool IsChangingWeapon => itemEquipper.IsChangingItem;

        public bool CanAim { get; set; } = true;

        /// <summary>
        /// True while AnimGraph is driving an authored one-shot action clip (an AI's look-around or investigate, a vault, ...). 
        /// The idle grips are authored against the idle/locomotion pose, so leaving them on through such a clip pins the hands 
        /// to the weapon while the animation takes the arms somewhere else entirely. Aiming is unaffected: the aim clip loops 
        /// and the shoot clip is additive, neither of which counts as an action animation, and reload/load-ammo already 
        /// suppress arm IK through their own flags.
        /// </summary>
        public bool IsPlayingActionAnimation() => animGraph != null && animGraph.IsPlayingActionAnimation;

        public bool IsAimPointObstructed { get; set; }
        public Vector3 TargetAimPoint { get; set; }
        public Vector3 BulletHitPoint { get; set; }
        public RaycastHit TargetHitData;
        public bool IsTransitioningToAim { get; private set; }

        /// <summary>The current aim result bundled into a single value (aim point, bullet point, obstruction, hit).</summary>
        public AimTarget CurrentAimTarget => new AimTarget
        {
            aimPoint = TargetAimPoint,
            bulletPoint = BulletHitPoint,
            obstructed = IsAimPointObstructed,
            hit = TargetHitData
        };


        // Debug varibles
        bool _ikSetupMode = false;
        bool _previousIkSetupMode = false;
        public bool IkSetupMode
        {
            get => _ikSetupMode;
            set
            {
                _ikSetupMode = value;

                if (_previousIkSetupMode != _ikSetupMode)
                {
                    if (CurrentShooterWeaponObject != null)
                    {
                        //if (_ikSetupMode)
                        //{
                        //    CurrentShooterWeaponObject.supportHandIkTarget.SetParent(null, true);
                        //    CurrentShooterWeaponObject.holdHandIkTarget.SetParent(null, true);
                        //}
                        //else
                        //{
                        //    if (CurrentShooterWeaponObject != null)
                        //    {
                        //        CurrentShooterWeaponObject.supportHandIkTarget.SetParent(CurrentShooterWeaponObject.transform, true);
                        //        CurrentShooterWeaponObject.holdHandIkTarget.SetParent(CurrentShooterWeaponObject.transform, true);
                        //    }
                        //}
                    }
                    _previousIkSetupMode = _ikSetupMode;
                }
            }
        }

        #endregion

        #region Events
        //public Action OnDead;
        public Action OnEndAction;

        public Action<EquippableItem> OnShooterWeaponEquipAction;
        public Action OnShooterWeaponUnEquipAction;

        public Action OnRecoil;

        public Action<EquippableItem> OnItemBecameUnused;
        #endregion

        // Whether hit data must be recomputed fresh at fire time. True when no aim
        // provider produces continuous per-frame hit data — the AI case, where
        // nothing resets the persistent TargetHitData struct between shots, so
        // stale data from a previous shot would otherwise be reused. The player's
        // ShooterController registers a continuous provider, making this false.
        bool RefreshHitDataOnFire => aimProvider == null || !aimProvider.ProducesContinuousHitData;

        /// <summary>Registers the aim source (player camera raycast vs. AI target-relative).</summary>
        public void SetAimProvider(IAimProvider provider) => aimProvider = provider;

        private void Awake()
        {
            Fighter = GetComponent<FighterCore>();
            Animator = GetComponent<Animator>();
            ResolveIndependentCombatAI();

            animGraph = GetComponent<AnimGraph>();
            itemEquipper = GetComponent<ItemEquipper>();
            characterID = GetComponent<CharacterID>();
            locomotionCore = GetComponent<LocomotionCore>();

            leftHand = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            spine = Animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = Animator.GetBoneTransform(HumanBodyBones.UpperChest);

            postureSolver = new AimPostureSolver(transform, spine, chest, upperChest);
            upperBodyAimSolver = new UpperBodyAimSolver(transform, spine, chest, upperChest);

            itemEquipper.OnEquip += EquipItem;
            itemEquipper.OnEquipComplete += (itemData) =>
            {
                if (itemData is ShooterWeapon)
                {
                    OnShooterWeaponEquipAction?.Invoke(itemData);
                    enableIk = true;
                    independentIdleLoopReleased = false;

                    Fighter.ResetActionToNone(FighterAction.SwitchingWeapon);

                    if (CurrentShooterWeaponObject.CurrentAmmoCount == 0 && CurrentWeapon.autoReload)
                        Reload();
                }
            };

            itemEquipper.OnUnEquip += UnEquipItem;
            itemEquipper.OnBeforeItemDisable += (wd) =>
            {
                if (wd is ShooterWeapon)
                {
                    DisableCurrentWeapon();
                    enableIk = true;
                    Fighter.ResetActionToNone(FighterAction.SwitchingWeapon);
                }
            };
            IkSetupMode = false;

            Fighter.OnDeath += () =>
            {
                StopAiming();
                enabled = false;
            };

        }

        private void OnDestroy()
        {
            // Clean up the runtime default profile created for weapons without one.
            if (defaultAimProfile != null)
                Destroy(defaultAimProfile);
        }

        // Explosion damage and throw-completion are handled by ThrowableController.

        #region Aiming
        public void StartAiming()
        {
            if (IndependentPoseOwned) return;
            // Wait for any turning animation (idle turn / turn-back) to finish before starting to
            // aim: the turn's rotation correction owns the body's rotation until the clip ends, and
            // starting the aim mid-turn (aiming instantly faces the body toward the target every
            // frame) makes the two fight over the facing and glitches the aim pose. Callers — the AI
            // aim states and the player's per-frame aim input — invoke this repeatedly, so aiming
            // begins on the first frame after the turn completes.
            if (locomotionCore != null && locomotionCore.IsPlayingTurnAnimation)
                return;

            // Wait for any one-shot action animation to finish before aiming. When the
            // AI spots the target mid-investigate, the state machine's Exit() cancels
            // that animation — but a cancel only fast-forwards the clip to its
            // transition-out, so it keeps playing for the fade. Starting the aim loop
            // during that fade splices the aim animation on top of a clip that is
            // still being torn down, which corrupts the pose. Callers (AI states, the
            // player's per-frame input) invoke StartAiming every frame, so aiming
            // begins on the first frame after the cancellation completes.
            if (animGraph != null && animGraph.IsPlayingActionAnimation)
                return;

            if (IsShooterWeaponEquipped)
            {
                if (!CurrentWeapon.canAimWithoutAmmo && (CurrentShooterWeaponObject.CurrentAmmoCount == 0 && !hasInfiniteAmmo))
                {
                    if (IsAiming && !IsShooting)
                        StopAiming();
                    return;
                }
            }

            if (!IsAiming && CanAim && !Fighter.IsBusy && !IsLoadingAmmo)
            {
                OnStartAim?.Invoke();
                {
                    var clip = CurrentWeapon.aimClip;
                    var mask = CurrentWeapon.aimAnimationMask;
                    animGraph.PlayLoopingAnimation(null, clip, mask: mask);
                    aimingAnimationPlaying = clip.clip != null;
                }
                Animator.SetBool(AnimatorParameters.IsAiming, true);
                IsAiming = true;
                IsTransitioningToAim = true;

                if (Animator.GetBool(AnimatorParameters.IsGrounded))
                    ApplyAimedLocomotionOverride();

                StartCoroutine(AsyncUtil.RunAfterDelay(0.2f, () =>
                {
                    if (IsAiming)
                    {
                        IsTransitioningToAim = false;
                    }
                }));
            }
            else if (!CanAim && IsAiming)
                StopAiming();
        }
        public void StopAiming()
        {
            if (IsAiming)
            {
                ClearAimedLocomotionOverride();
                IsAiming = false;
                OnStopAim?.Invoke();
                Animator.SetBool(AnimatorParameters.IsAiming, false);

                if (chargingAnimationPlayed || (CurrentWeapon != null && CurrentWeapon.isChargedWeapon))
                {
                    animGraph.StopLoopingAnimations(true);
                    chargingAnimationPlayed = false;
                    itemEquipper.PlayIdleAnimation();
                    aimingAnimationPlaying = false;
                }
                else
                {
                    StopAimingAnimation();
                }

            }
        }


        public void StopAimingAnimation()
        {
            if (aimingAnimationPlaying)
                animGraph.StopLoopingAnimations(false);
            aimingAnimationPlaying = false;
        }

        void ApplyAimedLocomotionOverride()
        {
            if (isAimedLocomotionOverrideActive) return;
            if (!IsShooterWeaponEquipped || CurrentWeapon.aimedLocomotionClipSet2D == null) return;

            animGraph.OverrideLocomotion2D(CurrentWeapon.aimedLocomotionClipSet2D);
            isAimedLocomotionOverrideActive = true;
        }

        void ClearAimedLocomotionOverride()
        {
            if (!isAimedLocomotionOverrideActive) return;

            animGraph.ClearLocomotionOverride();
            isAimedLocomotionOverrideActive = false;
        }

        /// <summary>
        /// Sets the aiming target point with optional smooth blending on the XZ and Y axes.
        /// Clamps the horizontal aim direction within a specified angle from the character's forward direction.
        /// Use blending for smoother player transitions, but disable it (especially for enemies) when immediate snapping is needed.
        /// </summary>
        /// <param name="aimPoint">The desired world-space target point to aim at.</param>
        /// <param name="blendXZ">Whether to smoothly blend the X and Z components of the aim point.</param>
        /// <param name="blendY">Whether to smoothly blend the Y component of the aim point.</param>
        public void SetAimPoint(Vector3 aimPoint, bool blendXZ = false, bool blendY = false)
        {
            if (IsAiming)
            {
                Vector3 blendedTarget = TargetAimPoint;
                // Compute dynamic blend speed based on distance
                float dist = Vector3.Distance(TargetAimPoint, aimPoint);

                //float minSpeed = 5;
                //float maxSpeed = 1000;

                float speed = 1000;
                // Blend XZ
                if (blendXZ)
                {
                    Vector2 currentXZ = new Vector2(TargetAimPoint.x, TargetAimPoint.z);
                    Vector2 targetXZ = new Vector2(aimPoint.x, aimPoint.z);
                    Vector2 blendedXZ = Vector2.MoveTowards(currentXZ, targetXZ, Time.deltaTime * speed);

                    blendedTarget.x = blendedXZ.x;
                    blendedTarget.z = blendedXZ.y;
                }
                else
                {
                    blendedTarget.x = aimPoint.x;
                    blendedTarget.z = aimPoint.z;
                }

                // Blend Y
                if (blendY)
                {
                    blendedTarget.y = Mathf.MoveTowards(TargetAimPoint.y, aimPoint.y, Time.deltaTime * speed);
                }
                else
                {
                    blendedTarget.y = aimPoint.y;
                }

                // Clamp aim direction angle relative to player's forward
                Transform character = Animator.GetBoneTransform(HumanBodyBones.Hips);
                Vector3 toTarget = blendedTarget - character.position;
                Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z).normalized;

                float maxAngle = ActiveAimProfile.maxAimAngleFromForward;
                float angle = Vector3.Angle(character.forward, flatToTarget);

                if (angle > maxAngle)
                {
                    // Clamp direction to max angle
                    Vector3 clampedDirection = Vector3.RotateTowards(character.forward, flatToTarget, Mathf.Deg2Rad * maxAngle, float.MaxValue);

                    // Maintain horizontal distance
                    float flatDistance = new Vector2(toTarget.x, toTarget.z).magnitude;
                    Vector3 clampedPosition = character.position + clampedDirection * flatDistance;

                    blendedTarget.x = clampedPosition.x;
                    blendedTarget.z = clampedPosition.z;
                }

                TargetAimPoint = blendedTarget;
            }
            else
            {
                TargetAimPoint = aimPoint;
            }
        }

        public void UpdateAimingTargets(float randomFactor = 0.5f, Vector3? aimPositionOverride = null)
        {
            if ((Fighter.Target == null && !aimPositionOverride.HasValue) || IsReloading)
            {
                Vector3 forwardPoint = transform.position + transform.forward * 10f;
                TargetAimPoint = forwardPoint;
                BulletHitPoint = forwardPoint;
                return;
            }

            // Aim at the target's chest when available. Matches gun-height, so
            // the firing direction stays roughly horizontal at any range.
            Vector3 targetCenter = aimPositionOverride.HasValue
                ? aimPositionOverride.Value
                : GetTargetAimCenter(Fighter.Target);

            // Rotate the body horizontally toward the target (no vertical tilt). Skipped while a
            // turning animation plays — its rotation correction owns the body until the clip ends
            // (see the matching guard in StartAiming); resuming the snap a few frames later is
            // invisible, while fighting the turn for the rotation visibly glitches the pose.
            Vector3 horizontalLook = targetCenter - transform.position;
            horizontalLook.y = 0f;
            bool turning = locomotionCore != null && locomotionCore.IsPlayingTurnAnimation;
            if (horizontalLook.sqrMagnitude > 0.0001f && !turning)
            {
                transform.rotation = Quaternion.LookRotation(horizontalLook);

                // Keep locomotion's targetRotation in sync with the aim facing.
                locomotionCore?.ResetTargetRotation();
            }

            // IK aim point. The bone alignment computes direction as
            // (TargetAimPoint - aimHelper.position); at point-blank that
            // vector shrinks to near-zero and LookRotation collapses, which
            // visually breaks the arm IK during firing. Extend the aim point
            // along the chest→target direction (gun-height origin, so the
            // push is roughly horizontal) when the target is closer than the
            // minimum IK aim distance. Using the chest as origin keeps the
            // extension nearly level — the old feet-origin extension added a
            // huge Y push at close range and pitched the gun upward.
            float MinIKAimDistance = ActiveAimProfile.minIKAimDistance;
            Transform aimOriginBone = upperChest != null ? upperChest
                                   : chest != null ? chest
                                   : spine;
            Vector3 aimOrigin = aimOriginBone != null
                ? aimOriginBone.position
                : transform.position + Vector3.up * 1.4f;

            Vector3 chestToTarget = targetCenter - aimOrigin;
            float chestToTargetDist = chestToTarget.magnitude;
            if (chestToTargetDist < MinIKAimDistance && chestToTargetDist > 0.0001f)
            {
                Vector3 extendDir = chestToTarget / chestToTargetDist;
                TargetAimPoint = aimOrigin + extendDir * MinIKAimDistance;
            }
            else
            {
                TargetAimPoint = targetCenter;
            }

            // Bullet hit point: target chest + distance-scaled noise.
            float distToTarget = Vector3.Distance(targetCenter, transform.position);
            float scaledFactor = Mathf.Min(randomFactor, distToTarget * 0.15f);
            BulletHitPoint = targetCenter + Random.insideUnitSphere * scaledFactor;

            Debug.DrawRay(TargetAimPoint, Vector3.up, Color.blue);
            Debug.DrawRay(BulletHitPoint, Vector3.up, Color.red);
        }


        #endregion

        #region Shooting
        [HideInInspector]
        public bool chargingAnimationPlayed;
        public void HandleCharging()
        {
            if (!IsShooterWeaponEquipped || waitForNextFire || !IsAiming || !CanAim || IsTransitioningToAim)
                return;
            chargingAnimationPlayed = CurrentWeapon.chargeClip.clip != null;
            animGraph.PlayLoopingAnimation(null, CurrentWeapon.chargeClip, mask: CurrentWeapon.chargeAnimationMask);
        }

        /// <summary>
        /// Drops a charge that was started but never released, without firing. The player always
        /// resolves a draw (fire-up shoots, aim-out goes through StopAiming), but an AI can lose
        /// its shot mid-draw — this lets it relax the bow instead of holding the loop forever.
        /// </summary>
        public void StopCharging()
        {
            if (!chargingAnimationPlayed) return;

            animGraph.StopLoopingAnimations(false);
            chargingAnimationPlayed = false;
        }

        /// <summary>
        /// Whether <see cref="Shoot"/> would go through right now. Mirrors its guard so a caller
        /// pacing itself around the shot (an AI drawing a charged weapon) can wait for the weapon
        /// to actually be ready instead of starting a draw it can't release.
        /// </summary>
        public bool IsReadyToFire => IsShooterWeaponEquipped && !waitForNextFire && IsAiming
                                  && CanAim && !IsTransitioningToAim && !IsReloading;

        public void Shoot(float ammoForceMultiplier = 1)
        {
            if (!IsShooterWeaponEquipped || waitForNextFire || !IsAiming || !CanAim || IsTransitioningToAim || IsReloading) return;


            if (CurrentWeapon.isDualItem)
            {
                StartCoroutine(HandleShoot(CurrentWeaponRight, ammoForceMultiplier));
                StartCoroutine(HandleShoot(CurrentWeaponLeft, ammoForceMultiplier));
            }
            else
            {
                StartCoroutine(HandleShoot(CurrentShooterWeaponObject, ammoForceMultiplier));
            }
        }
        IEnumerator HandleShoot(ShooterWeaponObject weapon, float ammoForceMultiplier = 1)
        {
            if (IsTransitioningToAim)
            {
                if (CurrentWeapon.hipFire)
                {
                    yield return new WaitUntil(() => !IsTransitioningToAim);
                }
                else
                    yield break;
            }
            if (weapon.CurrentAmmoCount <= 0)
            {
                if (!weapon.HasAmmo)
                {
                    var triggerAudio = weapon.AudioSource;
                    triggerAudio.clip = CurrentWeapon.triggerAudioClip;
                    triggerAudio.Play();
                }
                yield break;
            }

            StartCoroutine(ExecuteShooting(weapon, ammoForceMultiplier));
        }

        int shootExecutionCount = 0;
        private IEnumerator ExecuteShooting(ShooterWeaponObject weapon, float ammoForceMultiplier = 1)
        {
            ++shootExecutionCount;

            OnFire?.Invoke();
            if (!IsReloading)
                Fighter.SetAction(FighterAction.Shooting);

            if (!CurrentWeapon.playLoadAnimationPerShot)
                StartCoroutine(HandleFireDelay(CurrentWeapon.GetAttributeValue<float>("Fire Frequency")));
            else
                waitForNextFire = true;

            // Detaching the ammo's parent must be delayed by one frame because Unity updates transform parenting at the end of the frame.
            yield return new WaitForEndOfFrame();

            if (weapon.CurrentLoadedAmmo != null)
                weapon.CurrentLoadedAmmo.transform.parent.SetParent(null, true);
            Vector3 shootDirection = (BulletHitPoint - weapon.aimController.handAlignment.Helper.position).normalized;
            ExecuteFire(shootDirection, CurrentWeapon.isBurst, weapon, ammoForceMultiplier);
            //Debug.DrawLine(BulletHitPoint, BulletHitPoint+ shootDirection, Color.red);
            //Debug.Break();
            OnRecoil?.Invoke();

            var currentWeaponTemp = CurrentWeapon;
            if (!IsInScopeView)
            {
                var shootAction = new CharacterAction()
                {
                    clipInfo = CurrentWeapon.shootClip,
                    animationParameters = new AnimParameters()
                    {
                        mask = CurrentWeapon.shootAnimationMask,
                        isAdditiveLayerAnimation = true
                    },
                    usesRootMotion = false,
                    blocksLocomotion = false,
                    resetMoveAmount = false     // Shooting must not stop movement
                };
                yield return animGraph.CrossfadeAsync(CurrentWeapon.shootClip, shootAction.animationParameters);
            }

            if (CurrentWeapon != null && currentWeaponTemp == CurrentWeapon)
            {
                if (chargingAnimationPlayed)
                {
                    animGraph.StopLoopingAnimations(false);
                    chargingAnimationPlayed = false;
                }

                if (CurrentWeapon.playLoadAnimationPerShot && weapon.CurrentAmmoCount > 0)
                    StartCoroutine(LoadAmmoAfterShoot(weapon, CurrentWeapon.loadAmmoClip));

                if (CurrentWeapon.autoReload && weapon.CurrentAmmoCount <= 0 && weapon.HasAmmo)
                {
                    if (IsInScopeView && CurrentWeapon.shootClip.clip != null)
                        yield return new WaitForSeconds(CurrentWeapon.shootClip.clip.length);
                    if (CurrentWeapon.playLoadAnimationPerShot)
                        waitForNextFire = false;
                    Reload();
                }
            }
            if (CurrentWeapon == null)
                waitForNextFire = false;

            --shootExecutionCount;
            if (shootExecutionCount < 0)
            {
                shootExecutionCount = 0;
                Debug.LogError("Shoot Execution Count went below zero");
            }

            if (shootExecutionCount == 0)
                Fighter.ResetActionToNone(FighterAction.Shooting);
        }

        private void ExecuteFire(Vector3 shootDirection, bool isBurst, ShooterWeaponObject weapon, float ammoForceMultiplier = 1)
        {
            if (isBurst)
            {
                for (int i = 0; i < CurrentWeapon.burstFireBulletCount; i++)
                {
                    Vector3 direction = GenerateRandomizedDirection(shootDirection,
                        CurrentWeapon.burstSpreadingAngleRange.x,
                        CurrentWeapon.burstSpreadingAngleRange.y);
                    SpawnBullet(direction, weapon, ammoForceMultiplier);
                }
            }
            else
            {
                ExecuteAmmo(BulletHitPoint, shootDirection, weapon, ammoForceMultiplier);
            }
        }
        private void ExecuteAmmo(Vector3 TargetAimPoint, Vector3 shootDirection, ShooterWeaponObject weapon, float ammoForceMultiplier = 1)
        {
            // Apply bullet spread as a perpendicular offset, scaled by the
            // distance from muzzle to aim point and clamped so close-range
            // shots don't fan out into a huge angular cone.
            Vector3 newTargetPoint = ApplyBulletSpread(TargetAimPoint, weapon.ammoSpawnPoint.position, CurrentWeapon.bulletSpreadRadius);
            Vector3 toTarget = newTargetPoint - weapon.ammoSpawnPoint.position;

            // When the target overlaps the shooter (point-blank), the muzzle can sit
            // at or past the target/aim point. (target - muzzle) is then tiny or
            // points backward. Fall back to shootDirection (gun forward) when the
            // computed direction is degenerate or points behind the gun.
            Vector3 direction;
            if (toTarget.sqrMagnitude < 0.0001f || Vector3.Dot(toTarget, shootDirection) <= 0f)
                direction = shootDirection.sqrMagnitude > 0.0001f ? shootDirection.normalized : weapon.ammoSpawnPoint.forward;
            else
                direction = toTarget.normalized;

            // AI shooters need TargetHitData populated so ShooterAmmo's direct-hit
            // branch (gated on hit.transform != null) can fire at close range,
            // where the bullet's own travel-raycast often starts inside the
            // target's collider (Unity ignores colliders the cast origin overlaps).
            //
            // Casting from AI feet (or muzzle) is unreliable at point-blank:
            //   • feet → ground hits the floor first and stamps the floor as the hit
            //   • muzzle is frequently inside the target's capsule
            // Both produce a TargetHitData that doesn't match the actual target,
            // which then leads Initialize to override hit.point onto a wrong
            // collider (decal/parent desync) and ApplyDirectDamage finds no
            // Damagable on the floor → player takes no damage.
            //
            // Robust approach: raycast directly against Fighter.Target's primary
            // collider from an origin guaranteed to be outside that collider
            // (AI head height, two metres up). Collider.Raycast tests against
            // that single collider only — no floor/wall interference.
            // When the aim provider doesn't refresh hit data every frame (AI),
            // recompute TargetHitData fresh per shot — stale data from a previous
            // shot (or a previous target) would otherwise route the bullet to the
            // wrong collider.
            if (RefreshHitDataOnFire)
                TargetHitData = default;

            if (TargetHitData.transform == null && Fighter.Target != null)
            {
                Collider targetCollider = Fighter.Target.capsuleCollider != null
                    ? (Collider)Fighter.Target.capsuleCollider
                    : Fighter.Target.GetComponentInChildren<Collider>();

                if (targetCollider != null)
                {
                    Vector3 targetCenter = targetCollider.bounds.center;
                    Vector3 castOrigin = transform.position + Vector3.up * 2f;
                    Vector3 castDir = targetCenter - castOrigin;
                    float castDist = castDir.magnitude;
                    if (castDist > 0.0001f)
                    {
                        castDir /= castDist;
                        if (targetCollider.Raycast(new Ray(castOrigin, castDir), out RaycastHit colHit, castDist + 2f))
                        {
                            TargetHitData = colHit;
                        }
                    }
                }
            }

            SpawnBullet(direction, weapon, ammoForceMultiplier);
        }

        Vector3 GetRandomPositionByRadiusInDirection(Vector3 center, Vector3 dir, float radius)
        {
            dir = dir.normalized;
            Vector3 randomPoint = center + dir * radius + Random.insideUnitSphere * radius;
            return randomPoint;
        }

        // Spread tuned for ~10m reference range. We project a random offset
        // onto the plane perpendicular to the firing direction and scale by
        // the actual muzzle-to-aim distance, so the angular spread stays
        // consistent at any range (no huge cone at point-blank).
        const float BulletSpreadReferenceDistance = 10f;

        Vector3 ApplyBulletSpread(Vector3 aimPoint, Vector3 muzzlePos, float radius)
        {
            if (radius <= 0f) return aimPoint;

            Vector3 forward = aimPoint - muzzlePos;
            float distance = forward.magnitude;
            if (distance < 0.0001f) return aimPoint;

            forward /= distance;

            float scaled = radius * (distance / BulletSpreadReferenceDistance);
            Vector2 disk = Random.insideUnitCircle * scaled;

            Vector3 right = Vector3.Cross(forward, Vector3.up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.Cross(forward, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, forward);

            return aimPoint + right * disk.x + up * disk.y;
        }

        public static Vector3 GetTargetAimCenter(FighterCore target)
        {
            if (target == null) return Vector3.zero;

            var animator = target.animator;
            if (animator != null && animator.isHuman)
            {
                var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                         ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                         ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                if (chest != null) return chest.position;
            }

            if (target.capsuleCollider != null)
                return target.capsuleCollider.bounds.center;

            return target.transform.position + Vector3.up;
        }

        public Vector3 GenerateRandomizedDirection(Vector3 baseDirection, float minAngle = 0, float maxAngle = 3)
        {
            baseDirection.Normalize();
            float angleVariance = Random.Range(minAngle, maxAngle);
            Quaternion randomRotation = Quaternion.Euler(
                Random.Range(-angleVariance, angleVariance),
                Random.Range(-angleVariance, angleVariance),
                Random.Range(-angleVariance, angleVariance)
            );
            return (randomRotation * baseDirection).normalized;
        }


        private IEnumerator ExecuteEffects(ShooterWeaponObject weapon)
        {
            weapon.OnFire?.Invoke();
            weapon.OnFireAction?.Invoke();

            foreach (var particle in weapon.vfx)
            {
                particle.Emit(1);
            }

            if (weapon.CurrentLoadedAmmo.fireAudioSource != null)
            {
                weapon.CurrentLoadedAmmo.fireAudioSource.clip = CurrentWeapon.fireAudioClip;
                weapon.CurrentLoadedAmmo.fireAudioSource.Play();
            }

            //if (weapon.CurrentLoadedAmmo.trailObject != null)
            //    weapon.CurrentLoadedAmmo.trailObject.SetActive(true);
            if (weapon.flash != null)
            {
                weapon.flash.enabled = true;
                yield return new WaitForSeconds(.1f);
                if (weapon.flash.enabled)
                    weapon.flash.enabled = false;
            }
        }

        private void SpawnBullet(Vector3 direction, ShooterWeaponObject weapon, float ammoForceMultiplier = 1)
        {
            weapon.SpawnBullet(direction, ammoForceMultiplier);
            StartCoroutine(ExecuteEffects(weapon));

            if (weapon.CurrentAmmoCount > 0)
                weapon.SetAmmo();
        }

        private IEnumerator HandleFireDelay(float fireTime)
        {
            waitForNextFire = true;
            yield return new WaitForSeconds(fireTime);
            waitForNextFire = false;
        }

        private IEnumerator LoadAmmoAfterShoot(ShooterWeaponObject weapon, AnimGraphClipInfo clip)
        {
            IsLoadingAmmo = true;
            if (Fighter.Action != FighterAction.Dodging && !IsReloading)
            {
                if (CurrentWeapon.loadAmmoAudioClip != null)
                {
                    weapon.AudioSource.clip = CurrentWeapon.loadAmmoAudioClip;
                    weapon.AudioSource.Play();
                }
                yield return animGraph.CrossfadeAsync(null, clip, mask: Mask.Arm);
            }
            waitForNextFire = IsLoadingAmmo = false;

        }

        #endregion

        #region Reload

        public void Reload()
        {
            if (!IsShooterWeaponEquipped || Fighter.Action != FighterAction.None) return;
            var weapons = CurrentWeapon.isDualItem ? new[] { CurrentWeaponRight, CurrentWeaponLeft } : new[] { CurrentShooterWeaponObject };
            foreach (var w in weapons)
            {
                HandleReload(w);
            }
        }

        private void HandleReload(ShooterWeaponObject weapon)
        {
            if (weapon == null || !weapon.HasAmmo || weapon.CurrentAmmoCount >= CurrentWeapon.GetAttributeValue<int>("Magazine Size") || IsReloading) return;

            StartCoroutine(ReloadWeapon(weapon));
        }

        private IEnumerator ReloadWeapon(ShooterWeaponObject weapon)
        {
            if (CurrentWeapon.reloadClip.clip != null || (!CurrentWeapon.useReloadAnimationDuration && CurrentWeapon.reloadTime > 0))
            {
                OnStartReload?.Invoke();
                Fighter.SetAction(FighterAction.Reloading);
                enableIk = false;
            }
            if (IsInScopeView)
            {
                StopAiming();
            }
            // Play reload sound if available
            if (weapon.AudioSource && CurrentWeapon.reloadAudioClip)
            {
                weapon.AudioSource.clip = CurrentWeapon.reloadAudioClip;
                weapon.AudioSource.Play();
            }
            bool ammoSpawned = false;

            var curWeapon = CurrentWeapon;
            // Play reload animation if available
            if (CurrentWeapon.reloadClip.clip)
            {
                if (CurrentWeapon.useReloadAnimationDuration)
                {
                    yield return animGraph.CrossfadeAsync(null, CurrentWeapon.reloadClip, mask: CurrentWeapon.reloadAnimationMask,
                        onAnimationUpdate: (normalizedTimer, timer) =>
                        {
                            if (!ammoSpawned && normalizedTimer >= CurrentWeapon.ammoEnableTime)
                            {
                                if (IsReloading && CurrentWeapon != null && curWeapon == CurrentWeapon)
                                {
                                    weapon?.SetAmmo();
                                    ammoSpawned = true;
                                }
                            }
                        });
                }
                else
                {
                    animGraph.Crossfade(null, CurrentWeapon.reloadClip, mask: CurrentWeapon.reloadAnimationMask);
                    yield return new WaitForSeconds(CurrentWeapon.reloadTime);
                }
            }
            else if (!CurrentWeapon.useReloadAnimationDuration)
            {
                yield return new WaitForSeconds(CurrentWeapon.reloadTime);
            }

            if (IsReloading && CurrentWeapon != null && curWeapon == CurrentWeapon)    // If reload was not interupted
            {
                if (!ammoSpawned)
                    weapon?.SetAmmo();

                weapon?.ReloadAmmo(CurrentWeapon.GetAttributeValue<int>("Magazine Size"));

                //if (aimingAnimationPlaying)
                //    animGraph.StopLoopingAnimations(false);
            }

            Fighter.ResetActionToNone(FighterAction.Reloading);
            enableIk = true;
        }


        #endregion

        #region Utility Methods

        /// <summary>Called by <see cref="ShooterAmmoObject"/> when shooter-weapon ammo explodes.</summary>
        public Action<ShooterAmmoObject> OnAmmoExploded;
        public void AmmoExploded(ShooterAmmoObject shooterAmmo) => OnAmmoExploded?.Invoke(shooterAmmo);

        public void EvaluateShootingFeasibility()
        {
            float distance = Vector3.Distance(TargetAimPoint, upperChest.position);

            //CanShoot = distance >= minShootingDistance;
        }


        #endregion

        #region Equip Weapon

        public void EquipItem(EquippableItem itemData)
        {
            TargetAimPoint = transform.position;
            if (itemData is ShooterWeapon)
            {
                var weaponData = itemData as ShooterWeapon;

                enableIk = false;

                Fighter.SetAction(FighterAction.SwitchingWeapon);
                Fighter.TriggerOnWeaponEquip(weaponData);

                if (weaponData != null)
                {
                    if (weaponData.isDualItem)
                    {
                        // Right hand weapon
                        SetRightHand(weaponData);

                        // Left hand weapon
                        SetLeftHand(weaponData);


                    }
                    else if (weaponData.holderBone == HumanBodyBones.RightHand)
                    {
                        SetRightHand(weaponData);
                        CurrentWeaponRight.IkTarget = AvatarIKGoal.LeftHand;
                    }
                    else if (weaponData.holderBone == HumanBodyBones.LeftHand)
                    {
                        SetLeftHand(weaponData);
                        CurrentWeaponLeft.IkTarget = AvatarIKGoal.RightHand;
                    }
                }

                UpdateExistingIkReference();
                UpdateExistingHandIKReferences();

                waitForNextFire = false;
            }
        }
        private void SetRightHand(ShooterWeapon weaponData)
        {
            var profile = ResolveProfile(weaponData);
            SetWeapon(CurrentWeaponRight, rightHandAimController, weaponData, profile.rightHandForward, profile.rightHandUp,
                      HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightShoulder,
                      HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftShoulder);
        }

        private void SetLeftHand(ShooterWeapon weaponData)
        {
            var profile = ResolveProfile(weaponData);
            SetWeapon(CurrentWeaponLeft, leftHandAimController, weaponData, profile.leftHandForward, profile.leftHandUp,
                      HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftShoulder,
                      HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightShoulder);
        }

        private void SetWeapon(ShooterWeaponObject currentWeapon, ShootingAimController aimController, ShooterWeapon weaponData, DirectionAxis handForward, DirectionAxis handUp,
                        HumanBodyBones upperArm, HumanBodyBones lowerArm, HumanBodyBones hand, HumanBodyBones shoulder,
                        HumanBodyBones supportUpperArm, HumanBodyBones supportLowerArm, HumanBodyBones supportHand, HumanBodyBones supportShoulder)
        {
            if (aimController != null)
                aimController.DestroyAimHelpers();

            currentWeapon.itemData = weaponData;
            currentWeapon.gameObject.SetActive(false);

            var profile = ResolveProfile(weaponData);
            currentWeapon.aimController = new ShootingAimController(Animator, handForward, handUp, upperArm, lowerArm, hand, shoulder, supportHand, supportLowerArm, supportUpperArm, supportShoulder, profile.upperArmWeight, profile.lowerArmWeight, profile.handWeight, CurrentShooterWeaponObject.aimReference);

            currentWeapon.ParentShooterFighter = this;

            if (!currentWeapon.AlreadyUsed)
            {
                currentWeapon.SetAmmo();
                currentWeapon.ReloadAmmo(weaponData.GetAttributeValue<int>("Magazine Size"));
            }
            else if (currentWeapon.CurrentAmmoCount != 0)
            {
                currentWeapon.SetAmmo();
            }

            currentWeapon.AlreadyUsed = true;

            // Update external aimController reference
            aimController = currentWeapon.aimController;
            currentWeapon.OnEquip?.Invoke();
        }

        public void UpdateExistingIkReference()
        {
            if (characterID == null) return;

            existingArmIKReference = CurrentWeapon.iKReferences.Find(
                    r => r.characterReferences != null &&
                    r.characterReferences.Count > 0 &&
                    r.characterReferences.FirstOrDefault(c => c.characterId == characterID.id || c.avatar == Animator.avatar) != null);
        }

        /// <summary>
        /// Resolves and caches the per-character grip overrides (support + hold) for the
        /// current weapon/character (New aiming). Call after equipping or after the setup
        /// window adds/edits an override. Mirrors <see cref="UpdateExistingIkReference"/>.
        /// </summary>
        public void UpdateExistingHandIKReferences()
        {
            if (characterID == null || CurrentWeapon == null) return;

            existingSupportRef = FindHandIKReference(CurrentWeapon.supportHandIK);
            existingHoldRef = FindHandIKReference(CurrentWeapon.holdHandIK);
        }

        HandIKReference FindHandIKReference(HandIKSettings settings) =>
            settings.references.Find(
                r => r.characterReferences != null &&
                r.characterReferences.Count > 0 &&
                r.characterReferences.FirstOrDefault(c => c.characterId == characterID.id || c.avatar == Animator.avatar) != null);

        #endregion

        #region UnEquip Weapon

        public void UnEquipItem()
        {
            if (CurrentWeapon != null)
            {
                //if (CurrentShooterState != ShooterStates.Idle && CurrentShooterState != ShooterStates.Aim) return;


                if (CurrentWeapon.isDualItem)
                {
                    if (CurrentWeaponLeft != null)
                    {
                        CurrentWeaponLeft.OnUnEquip?.Invoke();
                        CurrentWeaponLeft.ParentShooterFighter = null;
                    }
                    if (CurrentWeaponRight != null)
                    {
                        CurrentWeaponRight.OnUnEquip?.Invoke();
                        CurrentWeaponRight.ParentShooterFighter = null;
                    }
                }
                else
                {
                    if (CurrentShooterWeaponObject != null)
                    {
                        CurrentShooterWeaponObject.OnUnEquip?.Invoke();
                        CurrentShooterWeaponObject.ParentShooterFighter = null;
                    }

                }

                //OnStopAim?.Invoke();
                //animator.SetBool(AnimatorParameters.IsAiming, false);
                StopAiming();
                //if (IsAiming) animGraph.StopLoopingAnimations(true);
                OnShooterWeaponUnEquipAction?.Invoke();
                enableIk = false;

                Fighter.SetAction(FighterAction.SwitchingWeapon);
                Fighter.TriggerOnWeaponEquip(CurrentWeapon);

                existingArmIKReference = null;
                existingSupportRef = null;
                existingHoldRef = null;
            }
        }

        void DisableCurrentWeapon()
        {
            if (CurrentWeapon.isDualItem)
            {
                rightHandAimController?.DestroyAimHelpers();
                CurrentWeaponRight.aimController = null;

                leftHandAimController?.DestroyAimHelpers();
                CurrentWeaponLeft.aimController = null;
            }
            else
            {
                CurrentShooterWeaponObject.aimController?.DestroyAimHelpers();
                CurrentShooterWeaponObject.aimController = null;
            }
        }

        #endregion

        #region IK

        private void LateUpdate()
        {
            if (Fighter == null) return;
            if (independentCombatAI == null && Time.unscaledTime >= nextIndependentPoseSearch)
            {
                nextIndependentPoseSearch = Time.unscaledTime + 1f;
                ResolveIndependentCombatAI();
            }
            if (independentCombatAI != null && independentCombatAI.IndependentPoseRequested && !independentPoseLinkLogged)
            {
                independentPoseLinkLogged = true;
                Debug.Log("[Weapon Pose Receiver] " + name + ": linked to " + independentCombatAI.name + "; " + independentCombatAI.IndependentPoseStatus, this);
            }
            if (Fighter.ActiveRagdoll != null && Fighter.ActiveRagdoll.IsActive) return;

            if (independentCombatAI != null && independentCombatAI.UsesIndependentShooting &&
                independentCombatAI.Animator == Animator)
            {
                var equipped = itemEquipper != null && IsShooterWeaponEquipped ? CurrentShooterWeaponObject : null;
                bool ready = equipped != null && !IsChangingWeapon && !itemEquipper.IsCurrentItemUnusable;
                independentCombatAI.RegisterIndependentWeaponReference(Animator,
                    ready ? (equipped.aimReference != null ? equipped.aimReference : equipped.ammoSpawnPoint) : null,
                    ready ? equipped.ammoSpawnPoint : null);
            }

            if (IndependentPoseOwned)
            {
                if (!independentPoseWasOwned)
                {
                    independentPoseWasOwned = true;
                    independentIdleLoopReleased = false;
                    if (IsAiming) StopAiming();
                    if (chargingAnimationPlayed) StopCharging();
                    supportHandIKWeight = 0f;
                    holdHandIKWeight = 0f;
                    idleHandIKWeight = 0f;
                    Debug.Log("[Weapon Pose] Independent weapon layer owns arms; native procedural IK suspended.", this);
                }
                // Fantacode also plays equipped-item idle clips through AnimGraph,
                // outside the Animator Controller. Release the looping overlay once,
                // after equip/action transitions have finished, not every frame.
                if (!independentIdleLoopReleased && animGraph != null && IsShooterWeaponEquipped &&
                    !IsChangingWeapon && !Fighter.IsBusy && !IsPlayingActionAnimation())
                {
                    animGraph.StopLoopingAnimations(true);
                    independentIdleLoopReleased = true;
                    Debug.Log("[Weapon Pose] Native looping weapon pose release requested.", this);
                }
                // Preserve authored arms and recoil. Only restore target-facing
                // upper-body orientation, not the idle grip IK or native aim loop.
                if (independentCombatAI.TryGetIndependentWeaponAim(out Vector3 aimPoint))
                    TrackIndependentWeaponTarget(aimPoint);
                return;
            }
            if (independentCombatAI != null && independentCombatAI.IndependentPoseRequested &&
                Time.unscaledTime >= nextIndependentPoseLog)
            {
                nextIndependentPoseLog = Time.unscaledTime + 3f;
                Debug.LogWarning("[Weapon Pose Receiver] NOT suppressing IK: " + independentCombatAI.IndependentPoseStatus +
                    "; IK setup mode=" + IkSetupMode, this);
            }
            if (independentPoseWasOwned)
            {
                independentPoseWasOwned = false;
                if (independentIdleLoopReleased && IsShooterWeaponEquipped && !IsChangingWeapon &&
                    !Fighter.IsBusy && !IsPlayingActionAnimation())
                    itemEquipper.PlayIdleAnimation();
                independentIdleLoopReleased = false;
            }

            if (IsShooterWeaponEquipped && !IsChangingWeapon && !itemEquipper.IsCurrentItemUnusable)
            {
                // The legacy procedural aiming (spine posture + arm/hand IK) only
                // runs in Legacy mode. The New aiming architecture is built up in
                // later steps; until then it deliberately drives no IK.
                if (IsLegacyAiming)
                    UpdateAim();
                else
                    UpdateNewAim();
            }
            UpdateAimedLocomotionOverride();
        }

        private void TrackIndependentWeaponTarget(Vector3 point)
        {
            if (Fighter.IsDead || Fighter.IsBusy || !IsShooterWeaponEquipped || IsChangingWeapon ||
                itemEquipper.IsCurrentItemUnusable || IsPlayingActionAnimation()) return;
            var weapon = CurrentShooterWeaponObject;
            if (weapon == null || !weapon.gameObject.activeInHierarchy) return;
            Transform reference = weapon.aimReference != null ? weapon.aimReference : weapon.ammoSpawnPoint;
            if (reference == null) return;
            TargetAimPoint = point;
            BulletHitPoint = point;

            // The supplied Fantacode solver rotates the spine/chest/upper chest,
            // preserving the animated relationship of the arms and gun.
            if (upperBodyAimSolver != null && weapon.aimReference != null)
                upperBodyAimSolver.Solve(reference, point, CurrentWeapon.upperBodyIK);

            // If the weapon profile limits tracking, apply a bounded residual to
            // a shared upper-body ancestor. Never rotate the gun or wrist alone.
            Transform pivot = null;
            if (upperChest != null && reference.IsChildOf(upperChest) &&
                (leftHand == null || leftHand.IsChildOf(upperChest)) && (rightHand == null || rightHand.IsChildOf(upperChest))) pivot = upperChest;
            else if (chest != null && reference.IsChildOf(chest) &&
                (leftHand == null || leftHand.IsChildOf(chest)) && (rightHand == null || rightHand.IsChildOf(chest))) pivot = chest;
            else if (spine != null && reference.IsChildOf(spine) &&
                (leftHand == null || leftHand.IsChildOf(spine)) && (rightHand == null || rightHand.IsChildOf(spine))) pivot = spine;
            float remaining = independentCombatAI.MaximumWeaponAimCorrection;
            for (int i = 0; pivot != null && i < 3 && remaining > 0.1f; i++)
            {
                Vector3 desired = point - reference.position;
                if (desired.sqrMagnitude < 0.0001f) break;
                float error = Vector3.Angle(reference.forward, desired);
                if (error <= 2f) break;
                Quaternion correction = Quaternion.FromToRotation(reference.forward, desired.normalized);
                float step = Mathf.Min(error - 2f, remaining);
                pivot.rotation = Quaternion.RotateTowards(Quaternion.identity, correction, step) * pivot.rotation;
                remaining -= step;
            }
            if (Time.unscaledTime >= nextWeaponTrackingLog)
            {
                nextWeaponTrackingLog = Time.unscaledTime + 3f;
                float error = Vector3.Angle(reference.forward, point - reference.position);
                float muzzleError = weapon.ammoSpawnPoint != null ? Vector3.Angle(weapon.ammoSpawnPoint.forward,
                    point - weapon.ammoSpawnPoint.position) : -1f;
                Debug.Log($"[Weapon Tracking] {name}: reference={reference.name}, correctionBone={(pivot != null ? pivot.name : "none")}, aimError={error:F1} degrees, muzzleError={muzzleError:F1} degrees", this);
            }
        }

        /// <summary>
        /// The aim-orientation source for the New aiming system: the equipped
        /// weapon's Aim Reference transform, whose forward always points toward the
        /// current aim target. For dual wield the right-hand weapon's reference is
        /// used (both point at the same target while aiming).
        /// </summary>
        private Transform ActiveAimReference
        {
            get
            {
                if (CurrentShooterWeaponObject != null) return CurrentShooterWeaponObject.aimReference;
                if (CurrentWeaponRight != null) return CurrentWeaponRight.aimReference;
                if (CurrentWeaponLeft != null) return CurrentWeaponLeft.aimReference;
                return null;
            }
        }

        /// <summary>
        /// Per-frame entry point for the new aiming architecture. Built up
        /// incrementally. Keeping the legacy <see cref="UpdateAim"/> untouched lets
        /// both systems coexist behind <see cref="AimingMode"/> without interfering.
        ///
        /// Current step: rotate the upper-body (3-bone) chain so that the weapon's
        /// Aim Reference — which is left untouched — ends up pointing at the aim
        /// target once the rotation is applied, driven by the equipped weapon's
        /// <see cref="UpperBodyIKSettings"/>. Only the upper body is rotated; the
        /// weapon hierarchy follows it rigidly onto the target. Weight-blended two-bone
        /// IK then keeps the support (off) and hold (main) hands on the weapon.
        /// </summary>
        void UpdateNewAim()
        {
            if (CurrentWeapon == null) return;

            var aimReference = ActiveAimReference;
            if (aimReference == null) return;

            var settings = CurrentWeapon.upperBodyIK;

            // Rotate the upper body to bring the aim reference onto the target only
            // while actively aiming; otherwise ease the spine chain back to the
            // animated pose. Reload keeps aiming only when the weapon opts in
            // (matches legacy UpdateAim).
            bool keepReloadAim = CurrentShooterWeaponObject != null && CurrentShooterWeaponObject.dontBreakAimingWhileReload;
            bool active = IsAiming && CanAim && !Fighter.IsDead && (!IsReloading || keepReloadAim);

            if (active)
                upperBodyAimSolver.Solve(aimReference, TargetAimPoint, settings);
            else
                upperBodyAimSolver.Relax(settings);

            // Applied after the upper-body solve so the hands correct on top of the
            // rotated (aim-following) pose.
            UpdateNewHandIK(active);
        }

        /// <summary>
        /// New aiming hand IK for both hands — support (off) and hold (main). Each hand
        /// blends its own weight and runs a two-bone IK that pins it to the weapon grip
        /// while aiming (aim grip) and, if enabled, while idle (idle grip). Non-dual
        /// weapons only. Grips come from the New-aiming <see cref="HandIKSettings"/>
        /// (independent of the legacy Aim IK Data); the two-bone solve is reused from
        /// <see cref="ShootingAimController"/> via <see cref="ApplyArmIK"/>.
        /// </summary>
        void UpdateNewHandIK(bool aimActive)
        {
            supportHandIKWeight = UpdateNewArmIK(support: true, aimActive, supportHandIKWeight);
            holdHandIKWeight = UpdateNewArmIK(support: false, aimActive, holdHandIKWeight);
        }

        /// <summary>Runs one hand's IK (support or hold) and returns its updated blend weight.</summary>
        float UpdateNewArmIK(bool support, bool aimActive, float weight)
        {
            var s = support ? CurrentWeapon.supportHandIK : CurrentWeapon.holdHandIK;
            bool nonDual = !CurrentWeapon.isDualItem && CurrentShooterWeaponObject != null;

            // In IK setup mode the grip must be visible/editable for whichever pose is
            // being authored, so force the IK on regardless of the enable flags.
            bool setup = IkSetupMode;

            bool idlePose = !IsAiming && !Fighter.IsDead && !IsReloading && !IsPlayingActionAnimation();
            bool wantAim = aimActive && nonDual && (s.enableAim || setup);
            bool wantIdle = idlePose && nonDual && (s.enableIdle || setup);
            bool canApply = wantAim || wantIdle;

            float targetWeight = canApply ? (setup ? 1f : s.weight) : 0f;
            weight = Mathf.MoveTowards(weight, targetWeight, s.weightBlendSpeed * Time.deltaTime);

            if (weight <= 0.001f || CurrentShooterWeaponObject == null) return weight;

            // Aim grip while aiming, idle grip otherwise — per-character override when
            // one matches, else the weapon default.
            var gripData = ResolveHandIKData(support, wantAim);
            if (gripData == null) return weight;

            // Support = off hand / support arm; hold = weapon hand / main arm.
            bool holdRight = CurrentWeapon.holderBone == HumanBodyBones.RightHand;
            Transform source = support ? (holdRight ? leftHand : rightHand) : (holdRight ? rightHand : leftHand);
            var wobj = CurrentShooterWeaponObject;
            Transform target = support ? wobj.supportHandIkTarget : wobj.holdHandIkTarget;
            Transform helper = support ? wobj.supportHandIkSetupHelperTarget : wobj.holdHandIkSetupHelperTarget;

            // Place the IK target from the current hand pose plus the resolved grip offset.
            UpdateHandIkTargetPositionAndRotation(target, helper, source, gripData.positionOffset, gripData.rotationOffset, isHoldHand: !support);

            // Two-bone IK the arm onto the target.
            ApplyArmIK(applyToSupportArm: support, ikWeight: weight, elbowOffset: gripData.elbowOffset);
            return weight;
        }

        void UpdateAimedLocomotionOverride()
        {
            if (!IsAiming) return;

            bool grounded = Animator.GetBool(AnimatorParameters.IsGrounded);
            if (grounded && !isAimedLocomotionOverrideActive)
                ApplyAimedLocomotionOverride();
            else if (!grounded && isAimedLocomotionOverrideActive)
                ClearAimedLocomotionOverride();
        }
        public void UpdateAim()
        {
            if (IndependentPoseOwned) return;
            var profile = ActiveAimProfile;

            // Drive the master aim-IK weight from the current state so procedural
            // posture and arm IK blend in/out smoothly instead of snapping on/off.
            bool keepReloadAim = CurrentShooterWeaponObject != null && CurrentShooterWeaponObject.dontBreakAimingWhileReload;
            aimWeightController.Tick(ComputeAimState(), CanAim, keepReloadAim, profile.ikBlendSpeed, Time.deltaTime);
            float aimW = aimWeightController.AimWeight;

            if (IsAiming && (!IsReloading || CurrentShooterWeaponObject.dontBreakAimingWhileReload) && CanAim)
            {
                postureSolver.Solve(TargetAimPoint, profile, aimW);
                // Force weapon forward if in scope mode without canvas-based scope rendering
                if (IsInScopeView && !CurrentShooterWeaponObject.useCanvasImageForScope)
                {
                    CurrentShooterWeaponObject.transform.forward =
                        (TargetAimPoint - CurrentShooterWeaponObject.transform.position).normalized;
                }
                if (CurrentWeapon.isDualItem)
                {
                    var ikData = ResolveIkData(true);
                    if (!IsShooting)
                    {
                        // Update IK targets for both weapons (left and right)
                        UpdateHandIkTargetPositionAndRotation(CurrentWeaponLeft.holdHandIkTarget, CurrentWeaponLeft.holdHandIkSetupHelperTarget, leftHand, ikData.holdHandPosition, ikData.holdHandRotation, true);
                        UpdateHandIkTargetPositionAndRotation(CurrentWeaponRight.holdHandIkTarget, CurrentWeaponRight.holdHandIkSetupHelperTarget, rightHand, ikData.holdHandPosition, ikData.holdHandRotation, true);
                    }

                    // Align both arms towards the aim point. The two-bone arm IK
                    // runs at full weight — a partially-weighted solve is not a
                    // valid elbow pose. Only body posture eases (see Solve above).
                    leftHandAimController.AlignBones(TargetAimPoint, IsShooting, false, profile.alignSmoothSpeed, elbowOffset: ikData.holdElbowPosition);
                    rightHandAimController.AlignBones(TargetAimPoint, IsShooting, false, profile.alignSmoothSpeed, elbowOffset: ikData.holdElbowPosition);
                }
                else
                {
                    var aimData = ResolveIkData(true);
                    if (!IsShooting && !IsInScopeView)
                    {
                        UpdateHandIkTarget(true);
                    }

                    var holdHandRot = IkSetupMode ? (CurrentShooterWeaponObject.holdHandIkTarget.localEulerAngles - CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localEulerAngles) : aimData.holdHandRotation;
                    var handDefaultRot = CurrentWeapon.holderBone == HumanBodyBones.RightHand ? rightHand.eulerAngles : leftHand.eulerAngles;

                    // Align weapon bones and apply IK
                    CurrentShooterWeaponObject.aimController.AlignBones(
                        TargetAimPoint, IsShooting, false, profile.alignSmoothSpeed,
                        CurrentShooterWeaponObject.holdHandIkTarget.position,
                        CurrentShooterWeaponObject.holdHandIkTarget.eulerAngles,
                        applyHandIK: !IsInScopeView,
                        elbowOffset: aimData.holdElbowPosition
                    );


                    ApplyArmIK(true, elbowOffset: aimData.supportElbowPosition);
                }

            }
            else
            {
                postureSolver.Relax(profile);

                if (!CurrentWeapon.isDualItem)
                {
                    var idleData = ResolveIkData(false);
                    if (!IsShooting)
                    {
                        UpdateHandIkTarget(false);
                    }
                    // Hold-hand IK rebuilds its target from the current wrist transform plus a local offset
                    // every frame; while moving, the wrist orientation swings with the locomotion clip and
                    // the rotated offset can yank the wrist to a new position in a single frame (visible snap
                    // on idle → walk). Only run hold-hand IK while effectively idle.
                    bool isMoving = Animator.GetFloat(AnimatorParameters.moveAmount) > 0.05f;

                    // Release the grips while an authored action animation plays, eased at the profile's IK blend speed 
                    // so the hands come off the weapon over the clip's fade-in instead of snapping the moment it starts.
                    idleHandIKWeight = Mathf.MoveTowards(idleHandIKWeight, IsPlayingActionAnimation() ? 0f : 1f,
                        profile.ikBlendSpeed * Time.deltaTime);

                    if (idleHandIKWeight > 0.001f)
                    {
                        if (CurrentWeapon.UseHandIkForIdle)
                            ApplyArmIK(true, idleHandIKWeight, idleData.supportElbowPosition);
                        if (!isMoving)
                            ApplyArmIK(false, idleHandIKWeight, idleData.holdElbowPosition);
                    }
                }
            }
        }
        public void UpdateHandIkTarget(bool isAiming)
        {
            Transform supportHandSource = (CurrentWeapon.holderBone == HumanBodyBones.RightHand) ? leftHand : rightHand;
            Transform holdHandSource = (CurrentWeapon.holderBone == HumanBodyBones.RightHand) ? rightHand : leftHand;

            var ikData = existingArmIKReference != null
                        ? (isAiming ? existingArmIKReference.aimIKData : existingArmIKReference.idleIKData)
                        : (isAiming ? CurrentWeapon.aimIKData : CurrentWeapon.idleIKData);

            UpdateHandIkTargetPositionAndRotation(CurrentShooterWeaponObject.supportHandIkTarget, CurrentShooterWeaponObject.supportHandIkSetupHelperTarget, supportHandSource, ikData.supportHandPosition, ikData.supportHandRotation);
            UpdateHandIkTargetPositionAndRotation(CurrentShooterWeaponObject.holdHandIkTarget, CurrentShooterWeaponObject.holdHandIkSetupHelperTarget, holdHandSource, ikData.holdHandPosition, ikData.holdHandRotation, true);
        }


        /// <summary>
        /// Updates a hand IK target's position and rotation with given offsets.
        /// </summary>
        private void UpdateHandIkTargetPositionAndRotation(Transform ikTarget, Transform ikSetupTarget, Transform sourceHand, Vector3 positionOffset, Vector3 rotationOffset, bool isHoldHand = false)
        {
            var targetSelected = false;
#if UNITY_EDITOR
            var supportTargetSelected = Equals(Selection.activeObject, CurrentShooterWeaponObject.supportHandIkTarget.gameObject);
            var holdTargetSelected = Equals(Selection.activeObject, CurrentShooterWeaponObject.holdHandIkTarget.gameObject);

            targetSelected = supportTargetSelected || holdTargetSelected;
#endif
            if (targetSelected && IkSetupMode)
            {
                ikSetupTarget.position = sourceHand.position;
                ikSetupTarget.eulerAngles = sourceHand.eulerAngles;

                //if(isHoldHand)
                //    ikTarget.eulerAngles = sourceHand.eulerAngles;
            }
            else
            {
                ikTarget.position = sourceHand.position;
                ikTarget.eulerAngles = sourceHand.eulerAngles;
                ikTarget.localPosition += positionOffset;
                ikTarget.localEulerAngles += rotationOffset;

                ikSetupTarget.position = sourceHand.position;
                ikSetupTarget.eulerAngles = sourceHand.eulerAngles;
            }
        }

        void ApplyArmIK(bool applyToSupportArm = true, float ikWeight = 1f, Vector3 elbowOffset = new Vector3())
        {
            if (IndependentPoseOwned) return;
            if (!enableIk || IsChangingWeapon || itemEquipper.IsCurrentItemUnusable || IsReloading || IsLoadingAmmo || Fighter.IsDead) return;

            Vector3 targetPos = Vector3.zero;
            Vector3 targetRot = Vector3.zero;
            if (applyToSupportArm)
            {
                targetPos = CurrentShooterWeaponObject.supportHandIkTarget.position;
                targetRot = CurrentShooterWeaponObject.supportHandIkTarget.eulerAngles;
            }
            else
            {
                targetPos = CurrentShooterWeaponObject.holdHandIkTarget.position;
                targetRot = CurrentShooterWeaponObject.holdHandIkTarget.eulerAngles;
            }
            CurrentShooterWeaponObject.aimController.ApplyArmIK(targetPos, targetRot, applyToSupportArm, ikWeight, elbowOffset);
        }

        /// <summary>Resolves the high-level aiming/IK state that drives the IK weight blend.</summary>
        AimState ComputeAimState()
        {
            if (Fighter.IsDead) return AimState.Dead;
            if (IsChangingWeapon || itemEquipper.IsCurrentItemUnusable) return AimState.ChangingWeapon;
            if (IsReloading) return AimState.Reloading;
            if (IsInScopeView) return AimState.ScopeView;
            if (IsAiming) return AimState.Aiming;
            return AimState.Idle;
        }

        // Spine-chain aim posture (spine → chest → upperChest) now lives in
        // AimPostureSolver, driven from UpdateAim via postureSolver.Solve/Relax.

        #endregion

        public void ExitFromShooterSystem()
        {
            if (IsAiming)
                StopAiming();

            //Fighter.SetAction(FighterAction.None);
        }

    }
}


