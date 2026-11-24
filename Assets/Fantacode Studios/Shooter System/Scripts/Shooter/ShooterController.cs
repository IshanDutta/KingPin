using FS_CombatCore;
using FS_Core;
using FS_CoverSystem;
using FS_Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FS_LocomotionSystem;

namespace FS_Core
{
    public static partial class AnimatorParameters
    {
        public static int IsAiming = Animator.StringToHash("IsAiming");
    }
    public static partial class AnimationNames
    {
        public static string ShootingAim = "Shooting Aim";
    }
}

namespace FS_ShooterSystem
{
    public class ShooterController : EquippableSystemBase, IAimProvider
    {
        // The player's aim point is recomputed every frame from a camera raycast
        // (UpdateAimPoint), so hit data is always fresh — the fighter never needs
        // to recompute it at fire time.
        public bool ProducesContinuousHitData => true;

        public bool enableAimAssist = false;
        [ShowIf("enableAimAssist", true)]
        [Range(0, 1)]
        [SerializeField] private float aimAssistStrength = 0.2f; // 0 = none, 1 = full snap
        [ShowIf("enableAimAssist", true)]
        [SerializeField] private float aimAssistRadius = 0.4f; // 0 = none, 1 = full snap
        [ShowIf("enableAimAssist", true)]
        [SerializeField] private float lockOnMaxAngle = 45f; // 0 = none, 1 = full snap

        [SerializeField] bool equipOnAim = false;
        [ShowIf("equipOnAim", true)]
        [SerializeField] ShooterWeapon weaponToEquipOnAim;
        [ShowIf("equipOnAim", true)]
        [SerializeField] bool unequipOnAimStop = true;


        [Header("Component References")]
        private PlayerController playerController;
        private LocomotionCore locomotion;
        private ICharacter playerCharacter;
        private EnvironmentScanner environmentScanner;
        private ShooterInputManger shooterInputManger;
        private CombatInputManger combatInputManger;
        private ItemEquipper itemEquipper;
        private CharacterController characterController;
        private CoverController coverController;
        private FighterCore fighterCore;
        private Camera cam;
        private Animator animator;

        [Header("Aiming State")]
        //private Vector3 aimPoint;
        private bool ammoIsOver;
        private float defaultFieldOfView;
        private Coroutine aimViewCoroutine;
        private bool wasAimingLastFrame;
        private bool isZoomed;
        private bool previousCameraShakeStatus = false;
        //private bool isFiringWithoutAim;
        private GameObject playerCamera;

        private bool isCharging;
        private float forceMultiplier = 0.1f;
        private bool isInHipFire;

        private List<Collider> colliders = new List<Collider>();

        public ShooterFighter Shooter { get; private set; }

        public override List<Type> EquippableItems => new() { typeof(ShooterWeapon) };
        public override SystemState State => SystemState.Shooter;

        public bool CurrentWeaponObjectHasScope =>
            Shooter.CurrentShooterWeaponObject.hasScope &&
            Shooter.CurrentShooterWeaponObject.scopeTarget != null;

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeComponents();

        }

        private void Start()
        {
            cam = playerController.cameraGameObject.GetComponent<Camera>();
            defaultFieldOfView = cam.fieldOfView;
            playerCamera = playerController.cameraGameObject;
            SubscribeToEvents();
        }

        private void LateUpdate()
        {
            //if (!Shooter.stuckOnIkDebug)
            //    HandleAimStop();
            if (Shooter.IkSetupMode && Shooter.IsShooterWeaponEquipped)
                UpdateAimPoint(cam);
        }

        private void HandleAimStop()
        {
            if (Time.timeScale == 0) return;
            // Force stop aiming if conditions change mid-aim to prevent stuck aiming states
            // This handles cases where systems are disabled or input conflicts occur
            if (Shooter.IsAiming &&
                (!wasAimingLastFrame || playerCharacter.PreventAllSystems) &&
                Shooter.IsShooterWeaponEquipped)
            {
                Shooter.StopAiming();
                wasAimingLastFrame = true;
                return;
            }
            wasAimingLastFrame = false;
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            locomotion = GetComponent<LocomotionCore>();
            playerCharacter = GetComponent<ICharacter>();
            playerController = GetComponent<PlayerController>();
            shooterInputManger = GetComponent<ShooterInputManger>();
            characterController = GetComponent<CharacterController>();
            fighterCore = GetComponent<FighterCore>();
            environmentScanner = GetComponent<EnvironmentScanner>();
            itemEquipper = GetComponent<ItemEquipper>();
            Shooter = GetComponent<ShooterFighter>();
            animator = GetComponent<Animator>();
            combatInputManger = GetComponent<CombatInputManger>();
            coverController = GetComponent<CoverController>();

            // Register as the aim source so the fighter knows hit data is refreshed
            // per frame (player) rather than needing recompute at fire time (AI).
            Shooter.SetAimProvider(this);
        }

        private void SubscribeToEvents()
        {
            // Subscribe to ShooterInputManger aim events
            if (shooterInputManger != null)
            {
                shooterInputManger.OnAimPressed += OnAimPressed;
                shooterInputManger.OnAimReleased += OnAimReleased;
            }

            Shooter.OnRecoil += StartRecoil;
            Shooter.OnStartAim += OnStartAim;
            Shooter.OnStopAim += OnStopAim;
            fighterCore.OnDeath += OnDead;
            Shooter.OnShooterWeaponEquipAction += OnWeaponEquipped;
            Shooter.OnShooterWeaponUnEquipAction += OnWeaponUnequipped;
            Shooter.OnItemBecameUnused += itemEquipper.OnItemBecameUnusable;

            playerController.OnCameraTypeChanged += (newCameraType) =>
            {
                if (Shooter.IsWeaponEquipped)
                {
                    var speed = newCameraType == FSCameraType.FirstPerson ? ShooterSettings.instance.aimRunSpeed : ShooterSettings.instance.aimWalkSpeed;
                    locomotion.SetMaxSpeed(speed);

                    // Keep walk-only restriction consistent with camera mode
                    // when aiming: first-person allows sprint, third-person
                    // ADS walks only.
                    if (Shooter.IsAiming)
                    {
                        if (newCameraType == FSCameraType.FirstPerson)
                            locomotion.DisableWalkOnly();
                        else
                            locomotion.EnableWalkOnly();
                    }
                }
            };

            fighterCore.OnGotHit += (FighterCore attacker, Vector3 hitPoint, float hittingTime, bool isBlockedHit, HitType hitType) =>
            {
                // A non-additive hit reaction staggers the player out of the aim pose, so stop
                // aiming. Additive flinches play on top and must NOT interrupt aiming.
                // CurrReaction is set in TakeHit before this fires, and only when the hit will
                // actually play a reaction (LastHitWillPlayReaction).
                if (Shooter.IsAiming && fighterCore.LastHitWillPlayReaction
                    && fighterCore.CurrReaction != null && !fighterCore.CurrReaction.isAdditiveAnimation)
                {
                    Shooter.StopAiming();
                }

                // Suppressed hits (LastHitWillPlayReaction false) never fire OnGotHitCompleted, so focusing for them would never be undone.
                if (hitType == HitType.Ranged && itemEquipper.EquippedItem is ShooterWeapon && !fighterCore.CanDoActions && fighterCore.LastHitWillPlayReaction)
                {
                    playerCharacter.FocusSystem(this);
                }
            };
            fighterCore.OnGotHitCompleted += () =>
            {
                if (playerController.CurrentSystemState == SystemState.Shooter && fighterCore.Action != FighterAction.Dodging)
                {
                    playerCharacter.UnFocusSystem(this);
                }
            };


            itemEquipper.OnItemBecameRestricted += (item) => { if (Shooter.IsShooterWeaponEquipped) ShooterUI.i.crosshairController.HandleCrosshairVisiblity(showEntireCrosshair: false); };
            itemEquipper.OnItemBecameUnrestricted += (item) => { if (Shooter.IsShooterWeaponEquipped) ShooterUI.i.crosshairController.HandleCrosshairVisiblity(true); };

            fighterCore.OnStartAction += StartDodge;
            fighterCore.OnEndAction += EndSystem;

        }

        private void OnAimPressed()
        {
            if (playerCharacter.PreventAllSystems) return;

            if (equipOnAim && weaponToEquipOnAim != null)
            {
                itemEquipper.EquipItem(weaponToEquipOnAim);
            }
        }

        private void OnAimReleased()
        {
            if (equipOnAim && weaponToEquipOnAim != null && unequipOnAimStop && itemEquipper.EquippedItem == weaponToEquipOnAim)
            {
                StartCoroutine(WaitAndUnEquip());
            }
        }

        IEnumerator WaitAndUnEquip()
        {
            yield return new WaitUntil(() => !itemEquipper.IsChangingItem);
            itemEquipper.UnEquipItem(weaponToEquipOnAim);
        }

        #endregion

        #region Main Update Loop

        public override void HandleUpdate()
        {

            //if (fighterCore.Action == FighterAction.Dodging)
            //    fighterCore.ApplyAnimationGravity(locomotion.CheckIsGrounded(), IsInFocus);
            if (Shooter.IsWeaponEquipped)
            {
                //UpdateCameraAlignment();
                // Calculate where the weapon is pointing based on camera center and weapon position
                if (Shooter.IsShooterWeaponEquipped)
                    UpdateAimPoint(cam);
                
            }
            // Skip update if the shooter system is in setup mode to prevent unnecessary processing
            if (Shooter.IkSetupMode) return;

            // Skip update if the shooter system is currently in focus (e.g., during dodge or roll actions)
            // to prevent interference from other shooter-related processes.
            if (ShouldSkipUpdate() || IsInFocus)
            {
                bool shouldAim = !Shooter.IsShooterWeaponEquipped || ShouldStartAiming();
                if(!shouldAim)
                    Shooter.StopAiming();

                return;
            }



            if (Shooter.IsShooterWeaponEquipped)
            {
                if (Shooter.CurrentWeapon.autoReload && Shooter.CurrentShooterWeaponObject.CurrentAmmoCount == 0 && Shooter.CurrentShooterWeaponObject.HasAmmo && !Shooter.IsShooting && !fighterCore.IsBusy)
                {
                    Shooter.Reload();
                }
                HandleShooterWeaponLogic();
            }

            HandleGroundedAimingLogic();

            if (playerController.CurrentSystemState == SystemState.Locomotion)
            {

                if (Shooter.canDodge && fighterCore.CanDodge && combatInputManger.Dodge && fighterCore.Action != FighterAction.Dodging)
                {
                    if (locomotion.CheckIsGrounded())
                    {
                        StartCoroutine(fighterCore.Dodge(playerCharacter.MoveDir));
                        if (Shooter.IsAiming)
                        {
                            Shooter.StopAiming();
                        }
                        return;
                    }
                }

                if (Shooter.canRoll && fighterCore.CanRoll && combatInputManger.Roll && fighterCore.Action != FighterAction.Dodging)
                {
                    if (locomotion.CheckIsGrounded())
                    {
                        StartCoroutine(fighterCore.Roll(playerCharacter.MoveDir));
                        if (Shooter.IsAiming)
                        {
                            Shooter.StopAiming();
                        }
                        return;
                    }
                }
            }
        }

        private bool ShouldSkipUpdate()
        {
            // Skip if changing weapons to prevent input conflicts during transitions
            // Skip if fighter is busy, but allow processing during hit reactions for responsive combat
            return Shooter.IsChangingWeapon ||
                   (Shooter.Fighter.IsBusy && Shooter.Fighter.Action != FighterAction.TakingHit && Shooter.Fighter.Action != FighterAction.Shooting && fighterCore.CanDoActions);//|| playerController.CurrentSystemState != SystemState.Locomotion;
        }

        private void HandleShooterWeaponLogic()
        {
            // Process all shooting-related inputs and state changes
            HandleAimingShootingReloading();

            // Update UI elements with current weapon information
            ShooterUI.i.UpdateShooterUI(Shooter.CurrentShooterWeaponObject);
        }

        //private void UpdateCameraAlignment()
        //{
        //    // Only enable camera-forward alignment when aiming to maintain natural movement when not aiming
        //    if (!playerController.AlignTargetWithCameraForward)
        //        playerController.AlignTargetWithCameraForward = Shooter.IsAiming;
        //}

        private void HandleGroundedAimingLogic()
        {
            bool isGrounded = locomotion.CheckIsGrounded();

            // Disable zoom when airborne to prevent awkward mid-air scoping
            if (!isGrounded && Shooter.IsAiming && isZoomed)
            {
                if (Shooter.CurrentWeapon.allowJumpingWhileAiming)
                    HandleAim(false);
                else
                    Shooter.StopAiming();
            }
            // Re-enable zoom when landing while still aiming
            else if (isGrounded && Shooter.IsAiming && !isZoomed)
                HandleAim(true);
        }

        #endregion

        #region System Management

        public override void ExitSystem()
        {
            Shooter.ExitFromShooterSystem();
        }

        void ApplyAimAssist(Vector3 aimPoint, float aimAssistRadius, float maxRange)
        {
            var currentLockTarget = FindClosestEnemyAlongSpherecast(
                 cam,
                 sphereRadius: aimAssistRadius,
                 maxRange: maxRange,
                 enemyMask: fighterCore.targetLayer
             );
            if (currentLockTarget != null)
            {
                Vector3 ideal = currentLockTarget.bounds.center;
                if (currentLockTarget is CharacterController)
                {
                    var myCapsuleCollider = (CharacterController)currentLockTarget;
                    ideal = myCapsuleCollider.transform.position
                     + myCapsuleCollider.transform.up * (myCapsuleCollider.height - (myCapsuleCollider.radius));
                }

                //Vector3 ideal = currentLockTarget.ClosestPoint(aimPoint);
                //ideal = (ideal - aimPoint).normalized * 0.1f + ideal;
                playerController.CameraLookAtPoint(Vector3.Lerp(aimPoint, ideal, aimAssistStrength));
            }
        }
        private Collider FindClosestEnemyAlongSpherecast(
           Camera cam,
           float sphereRadius,
           float maxRange,
           LayerMask enemyMask
       )
        {
            // 1) Build center‑screen ray
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Ray ray = cam.ScreenPointToRay(center);

            // 2) Sphere‑cast all hits
            RaycastHit[] hits = Physics.SphereCastAll(
                ray,
                sphereRadius,
                maxRange,
                enemyMask,
                QueryTriggerInteraction.Ignore
            );

            Collider bestTarget = null;
            float bestDistance = float.MaxValue;

            // 3) For each hit, compute perpendicular distance from enemy pos to ray
            foreach (var hit in hits)
            {
                Collider t = hit.collider;
                Vector3 p = t.transform.position + Vector3.up * 1f;

                // Project (p - orig) onto ray.direction
                Vector3 toPoint = p - ray.origin;
                Vector3 alongRay = Vector3.Project(toPoint, ray.direction);
                Vector3 closestPt = ray.origin + alongRay;

                // Perpendicular distance
                float perpDist = Vector3.Distance(p, closestPt);

                // Tie‑break by along‑ray distance if you want:
                // float forwardDist = alongRay.magnitude;
                Vector3 toEnemy = p - cam.transform.position;
                float angle = Vector3.Angle(toEnemy, cam.transform.forward);
                if (angle > lockOnMaxAngle) continue;

                if (perpDist < bestDistance)
                {
                    bestDistance = perpDist;
                    bestTarget = t;
                }
            }

            return bestTarget;
        }

        private void UpdateAimPoint(Camera cam)
        {
            if (cam == null) return;

            Vector2 screenCenter = new(Screen.width / 2f, Screen.height / 2f);
            Ray ray = cam.ScreenPointToRay(screenCenter);
            ray.origin += ray.direction * 0.01f; // Avoid ray starting exactly at camera (precision glitching)

            float range = Shooter.CurrentWeapon.range;
            Vector3 calculatedAimPoint = ray.origin + ray.direction * range;


            bool hitSomething = Physics.Raycast(
                ray,
                out RaycastHit bulletHit,
                range,
                ~ShooterSettings.instance.hitIgnoreMask,
                QueryTriggerInteraction.Ignore
            );


                // Check if we hit our own character and ignore it
            if (hitSomething && IsOwnCharacter(bulletHit.collider.gameObject))
            {
                Vector3 newOrigin = bulletHit.point + ray.direction * 0.01f;
                float remainingRange = range - bulletHit.distance;

                bool hitSomethingElse = Physics.Raycast(
                    newOrigin,
                    ray.direction,
                    out RaycastHit secondHit,
                    remainingRange,
                    ~ShooterSettings.instance.hitIgnoreMask,
                    QueryTriggerInteraction.Ignore
                );

                if (hitSomethingElse && !IsOwnCharacter(secondHit.collider.gameObject))
                {
                    bulletHit = secondHit;
                    hitSomething = true;
                }
                else
                {
                    hitSomething = false;
                }
            }

            Vector3 bulletHitPoint = hitSomething ? bulletHit.point : calculatedAimPoint;

            if (Shooter.IsAiming && !Shooter.IsReloading)
            {
                var ammoPos = Shooter.CurrentShooterWeaponObject.ammoSpawnPoint;
                var weaponStart = ammoPos.position - ammoPos.forward.normalized * 1f;
                var weaponDirection = (bulletHitPoint - weaponStart).normalized;

                // Check if weapon is hitting something right in front of it
                bool gunHitObstacle = Physics.Raycast(
                    weaponStart,
                    weaponDirection,
                    out RaycastHit closeHit,
                    1.5f,
                    environmentScanner.ObstacleLayer,
                    QueryTriggerInteraction.Ignore
                );

                if (gunHitObstacle && IsOwnCharacter(closeHit.collider.gameObject))
                {
                    gunHitObstacle = false;
                }

                if (gunHitObstacle)
                    Shooter.BulletHitPoint = closeHit.point;

                bool isBehind = gunHitObstacle && IsObjectBehind(bulletHitPoint, closeHit.point);

                if (!gunHitObstacle || isBehind || Vector3.Distance(bulletHitPoint, closeHit.point) < 0.01f)
                {
                    Shooter.BulletHitPoint = bulletHitPoint;
                    closeHit = bulletHit;
                    Shooter.IsAimPointObstructed = false;
                }
                else
                {
                    Shooter.IsAimPointObstructed = true;
                }

                Shooter.TargetHitData = closeHit;
            }
            else
            {
                Shooter.BulletHitPoint = bulletHitPoint;
                Shooter.IsAimPointObstructed = false;
                Shooter.TargetHitData = bulletHit.transform != null ? bulletHit : default;
            }

            Shooter.SetAimPoint(calculatedAimPoint, false, true);

            bool isHitOnTarget =
                !IsOwnCharacter(Shooter.TargetHitData.transform?.gameObject) &&
                IsInLayerMask(
                    Shooter.TargetHitData.transform?.gameObject,
                    fighterCore.targetLayer | LayerMask.GetMask("RagdollBone")
                );

            if (enableAimAssist && Shooter.IsAiming && !isHitOnTarget)
                ApplyAimAssist(calculatedAimPoint, aimAssistRadius, range);

            if (Shooter.TargetHitData.transform != null)
            {
                Shooter.TargetHitData.point = Shooter.BulletHitPoint;
            }

            if (isHitOnTarget)
                Shooter.OnTargetLocked?.Invoke();
            else
                Shooter.OnTargetCleared?.Invoke();

            //Debug.DrawRay(Shooter.BulletHitPoint, Vector3.up * 0.5f, Color.green);
        }

        // Helper method to check if a GameObject belongs to this character
        private bool IsOwnCharacter(GameObject hitObject)
        {
            if (hitObject == null) return false;
            Transform shooterTransform = this.transform; // or however you reference the shooter's root transform
            Transform hitTransform = hitObject.transform;

            // Check if hit object is the shooter itself
            if (hitTransform == shooterTransform)
                return true;

            // Check if hit object is a child/descendant of shooter
            if (hitTransform.IsChildOf(shooterTransform))
                return true;

            // Check if shooter is a child/descendant of hit object (less common but possible)
            if (shooterTransform.IsChildOf(hitTransform))
                return true;

            return false;
        }
        public bool IsObjectBehind(Vector3 referenceTransform, Vector3 targetTransform)
        {
            Vector3 cameraPos = playerCamera.transform.position;
            Vector3 referencePos = referenceTransform;
            Vector3 targetPos = targetTransform;

            // Calculate distances from camera
            float refDistance = Vector3.Distance(cameraPos, referencePos);
            float targetDistance = Vector3.Distance(cameraPos, targetPos);

            // Target must be further from camera than reference
            if (targetDistance <= refDistance)
                return false;

            // Check if objects are roughly aligned from camera's perspective
            Vector3 cameraToRef = (referencePos - cameraPos).normalized;
            Vector3 cameraToTarget = (targetPos - cameraPos).normalized;

            // Calculate dot product to check alignment
            float alignment = Vector3.Dot(cameraToRef, cameraToTarget);

            // Objects are aligned if dot product is above threshold
            return alignment >= 0.5f;
        }
        bool IsInLayerMask(GameObject go, LayerMask mask)
        {
            if (go == null) return false;
            // Shift 1 by the GameObject's layer index, then AND against mask
            return (mask.value & (1 << go.layer)) != 0;
        }
        private void HandleAimingShootingReloading()
        {
            // Process aiming input and state changes
            HandleAimingInput();

            // Handle reload requests
            HandleReloadingInput();

            // Process firing mechanics
            HandleFiringInput();

            // Update ammo status for UI and game logic
            ammoIsOver = Shooter.CurrentShooterWeaponObject.CurrentAmmoCount == 0;
        }

        private void HandleAimingInput()
        {
            // Determine if we should be aiming based on various input conditions
            bool shouldAim = ShouldStartAiming();

            // Check if currently in hip fire mode (firing without explicit aim button)
            isInHipFire = DetermineHipFireState();

            if (shouldAim)
            {
                // Process aiming state transitions and scope handling
                HandleAimingState();
            }
            else
            {
                // Stop aiming if conditions no longer met
                Shooter.StopAiming();
            }


            //if (!shooterInputManger.Aim)
            //{
            //    Shooter.OnStopAim?.Invoke();
            //    animator.SetBool(AnimatorParameters.IsAiming, false);
            //    Shooter.StopAimingAnimation();
            //}

            // Handle camera effects during hip fire transitions
            if (!Shooter.IsInScopeView)
                HandleHipFireEffects();

          
        }

        private bool ShouldStartAiming()
        {
            bool isGrounded = locomotion.CheckIsGrounded();
            // Aim if: explicit aim button pressed OR hip fire enabled with fire button OR in first person
            // First person always aims to provide better accuracy control
            return (shooterInputManger.Aim ||
                   (Shooter.CurrentWeapon.hipFire && shooterInputManger.Fire) ||
                   playerController.CameraType == FSCameraType.FirstPerson) && (isGrounded || Shooter.CurrentWeapon.allowJumpingWhileAiming);
        }

        private bool DetermineHipFireState()
        {
            // Hip fire is when firing without holding the aim button
            // This affects camera behavior and weapon positioning
            return shooterInputManger.Fire && !shooterInputManger.Aim;
        }

        private void HandleAimingState()
        {
            if (!fighterCore.IsBusy)
            {
                // Transition from idle to aiming state
                Shooter.StartAiming();
            }
            if (ShouldEnterScopeView())
            {
                // Enter detailed scope view with zoom and positioning
                ApplyScopeView(true);
            }
            else if (ShouldExitScopeView())
            {
                // Exit scope but maintain basic aim state
                ApplyScopeView(false);
                ApplyAimCameraSettings();
                ResetWeaponTransforms();
            }

            // Track aiming state for frame-to-frame comparison
            wasAimingLastFrame = Shooter.IsAiming;
        }

        private bool ShouldEnterScopeView()
        {

            bool isGrounded = locomotion.CheckIsGrounded();
            // Enter scope view if: currently aiming, not already scoped, scope button pressed,
            // weapon has scope capability, and not in hip fire mode
            return Shooter.IsAiming &&
                   !Shooter.IsInScopeView &&
                   shooterInputManger.Scope &&
                   CurrentWeaponObjectHasScope && isGrounded;
        }

        private bool ShouldExitScopeView()
        {
            // Exit scope view if: currently scoped, scope button released,
            // weapon has scope, and not in hip fire mode
            return Shooter.IsInScopeView &&
                   !shooterInputManger.Scope &&
                   CurrentWeaponObjectHasScope;
        }

        private void HandleHipFireEffects()
        {
            // Apply camera effects when starting hip fire (for crosshair zoom, etc.)
            if (shooterInputManger.AimDown && Shooter.IsAiming && shooterInputManger.Fire)
            {
                ApplyAimCameraSettings();
            }
            // Reset effects when stopping hip fire
            else if (shooterInputManger.AimUp && Shooter.IsAiming && shooterInputManger.Fire)
            {
                HandleAim(false);
            }
        }

        private void HandleReloadingInput()
        {
            if (shooterInputManger.Reload && !Shooter.CurrentWeapon.preventManualReload)
                Shooter.Reload();
        }

        private void HandleFiringInput()
        {
            // Don't process firing if out of ammo
            if (ammoIsOver) return;

            // Determine firing modes based on weapon type and input
            bool canAutoFire = shooterInputManger.Fire && Shooter.CurrentWeapon.fireType == FireType.Auto;
            bool canSingleFire = shooterInputManger.FireDown && Shooter.CurrentWeapon.fireType == FireType.Single;
            bool isChargingWeapon = Shooter.CurrentWeapon.isChargedWeapon;  // For bows and chargeable weapons

            // Handle charging mechanics (bows, charged weapons)
            if (Shooter.IsAiming && shooterInputManger.Fire && isChargingWeapon)
            {
                HandleChargingMechanic();
            }
            // Handle normal firing or release of charged weapon
            else if (canAutoFire || canSingleFire || (shooterInputManger.FireUp && isChargingWeapon))
            {
                FireWeapon();
            }
        }

        private void HandleChargingMechanic()
        {
            // Start charging animation if not already charging
            if (!isCharging)
            {
                Shooter.HandleCharging();
                isCharging = Shooter.chargingAnimationPlayed;
            }

            // Gradually increase draw force while holding fire button
            // This creates variable damage/velocity based on draw time
            forceMultiplier += Time.deltaTime * Shooter.CurrentWeapon.chargeForce;
        }

        private void FireWeapon()
        {
            // Use accumulated charge force for charged weapons, normal force for others
            float force = Shooter.CurrentWeapon.isChargedWeapon
                ? Mathf.Clamp01(forceMultiplier)  // Clamp to 0-1 range for consistency
                : 1f;  // Full force for non-charged weapons

            Shooter.Shoot(force);

            // Reset charged state after firing
            forceMultiplier = 0.1f;  // Small base value to prevent zero force
            if (Shooter.CurrentWeapon.isChargedWeapon)
                isCharging = false;
        }

        #endregion

        #region Camera and Aim Handling

        private void HandleAim(bool zoom = true)
        {
            HandleWeaponAim(zoom);
            // Always apply movement restrictions when aiming
            HandleMovementWhileAiming(zoom);
        }

        private void HandleWeaponAim(bool zoom)
        {
            if (!Shooter.IsShooterWeaponEquipped) return;

            // Check if weapon should use zoom effects
            bool shouldZoom = Shooter.CurrentWeapon.zoomWhileAim ||
                             shooterInputManger.Scope ||
                             Shooter.IsInScopeView;

            // Apply zoom effects if enabled and not in hip fire mode
            if (shouldZoom && !isInHipFire)
            {
                if (ShooterUI.i.crosshairController.showCrosshairOnlyWhenAiming)
                    ShooterUI.i.crosshairController.HandleCrosshairVisiblity(showEntireCrosshair: zoom);
                if ((shooterInputManger.Scope || Shooter.IsInScopeView) && CurrentWeaponObjectHasScope)
                {
                    // Use scope-specific settings for enhanced zoom
                    ApplyScopeView(zoom);
                }
                else if (zoom)
                {
                    // Use standard aiming camera settings
                    ApplyAimCameraSettings();
                }
            }
        }

        private void ApplyScopeView(bool zoom)
        {
            // Create camera settings for scope view with close positioning to scope
            var camSettings = CreateScopeCameraSettings();
            playerController.SetCustomCameraState?.Invoke(camSettings);
            Shooter.IsInScopeView = zoom;
            if (Shooter.CurrentShooterWeaponObject.useCanvasImageForScope)
                Shooter.CurrentShooterWeaponObject.canvasObject.SetActive(zoom);

            ShooterUI.i.SetCrosshairActive(!zoom);

            // Set target FOV based on zoom state
            float targetFOV = zoom ? Shooter.CurrentShooterWeaponObject.scopeFov : defaultFieldOfView;
            StartSmoothZoom(targetFOV);
        }

        private CameraSettings CreateScopeCameraSettings()
        {
            // Configure camera for scope view: zero distance, follow scope target, use scope sensitivity
            return new CameraSettings()
            {
                overridedFollowTarget = Shooter.CurrentShooterWeaponObject.scopeTarget.transform,
                distance = 0,  // Camera positioned at scope
                framingOffset = Vector3.zero,
                followSmoothTime = 0,  // Instant follow for responsive scope
                minDistanceFromTarget = 0,
                sensitivity = Shooter.CurrentWeapon.aimCameraSettings.sensitivity,
                minVerticalAngle = Shooter.CurrentWeapon.aimCameraSettings.minVerticalAngle,
                maxVerticalAngle = Shooter.CurrentWeapon.aimCameraSettings.maxVerticalAngle
            };
        }

        private void StartSmoothZoom(float targetFOV)
        {
            if (aimViewCoroutine != null)
                StopCoroutine(aimViewCoroutine);

            aimViewCoroutine = StartCoroutine(SmoothZoom(targetFOV));
        }

        private void ApplyAimCameraSettings()
        {
            bool isGrounded = locomotion.CheckIsGrounded();

            if (!isGrounded) return;

            // Use different camera settings based on camera type
            var camSettings = playerController.CameraType == FSCameraType.ThirdPerson
                ? Shooter.CurrentWeapon.aimCameraSettings  // Use weapon-specific third person settings
                : CreateFirstPersonCameraSettings();       // Use generic first person settings

            playerController.SetCustomCameraState?.Invoke(camSettings);
        }

        private CameraSettings CreateFirstPersonCameraSettings()
        {
            return new CameraSettings()
            {
                distance = 0,
                framingOffset = Vector3.zero,
                sensitivity = Shooter.CurrentWeapon.aimCameraSettings.sensitivity,
                minVerticalAngle = -45,
                maxVerticalAngle = 45
            };
        }

        private void HandleMovementWhileAiming(bool zoom)
        {
            if (zoom)
            {
                ApplyAimingMovementRestrictions();
            }
            else
            {
                ResetAimState();
            }
            isZoomed = zoom;
        }

        private void ApplyAimingMovementRestrictions()
        {
            float moveSpeed = GetAimingMoveSpeed();
            // In first-person the player keeps full sprint/run mobility — the
            // camera is the aim reference and ADS-walk would feel sluggish.
            // Walk-only restriction still applies to third-person ADS.
            if (playerController.CameraType != FSCameraType.FirstPerson)
                locomotion.EnableWalkOnly();
            locomotion.DisableTurningAnimations();
            locomotion.PreventRotation = true;
        }

        private float GetAimingMoveSpeed()
        {
            var isFirstPerson = playerController.CameraType == FSCameraType.FirstPerson;
            var baseSpeed = isFirstPerson ? ShooterSettings.instance.aimRunSpeed : ShooterSettings.instance.aimWalkSpeed;

            if (Shooter.IsShooterWeaponEquipped && Shooter.CurrentWeapon.OverrideMoveSpeed)
            {
                return isFirstPerson ? ShooterSettings.instance.aimRunSpeed : Shooter.CurrentWeapon.CombatMoveSpeed;
            }

            return baseSpeed;
        }

        private void ResetAimState()
        {
            Shooter.IsInScopeView = false;
            locomotion.DisableWalkOnly();
            locomotion.ResetTurningAnimations();
            locomotion.PreventRotation = false;
            StartCoroutine(WaitToChangeCamera());
            ResetWeaponTransforms();
            isZoomed = false;
        }

        private void ResetWeaponTransforms()
        {
            ResetWeaponTransform(Shooter.CurrentWeaponRight);
            ResetWeaponTransform(Shooter.CurrentWeaponLeft);
        }

        private void ResetWeaponTransform(ShooterWeaponObject weapon)
        {
            if (weapon != null)
            {
                weapon.transform.localPosition = weapon.defaultLocalPos;
                weapon.transform.localRotation = weapon.defaultLocalRot;
            }
        }

        #endregion


        #region Event Handlers

        private void OnStartAim()
        {
            HandleAim();
            HandleCoverSystemTransition();
            if (Shooter.IsShooterWeaponEquipped)
                locomotion.PreventVerticalJump = !Shooter.CurrentWeapon.allowJumpingWhileAiming;
            playerController.AlignTargetWithCameraForward = true;
        }

        private void OnStopAim()
        {
            playerController.AlignTargetWithCameraForward = false;
            HandleAim(false);
            HandleCoverSystemReturn();
            isCharging = false;
            locomotion.PreventVerticalJump = false;
        }

        private void HandleCoverSystemTransition()
        {
            if (playerController.CurrentSystemState == SystemState.Cover)
            {
                var coverController = GetComponent<CoverController>();
                coverController.LastCoverPosition = transform.position;
                coverController.LastCoverForward = transform.forward;
                coverController.CustomMode = false;
                coverController.WasInCover = true;
                coverController.StopCover();
            }
        }

        private void HandleCoverSystemReturn()
        {
            HandleAim(false);

            coverController.CustomMode = false;
            bool shouldReturnToCover = coverController.WasInCover &&
                                     //(coverController.LastCoverPosition - transform.position).magnitude < 0.4f &&
                                     coverController.CheckCover(0.5f) != Vector3.zero;

            if (shouldReturnToCover)
            {
                coverController.StartCover();
            }
            coverController.WasInCover = false;
        }

        private void OnDead()
        {
            playerCharacter.PreventAllSystems = true;
            playerCharacter.UnFocusSystem(this);
            playerController.AlignTargetWithCameraForward = false;
            ShooterUI.i.crosshairController.HandleCrosshairVisiblity(showEntireCrosshair: false);
        }

        private void OnWeaponEquipped(EquippableItem weapon)
        {
            if (!ShooterUI.i.crosshairController.showCrosshairOnlyWhenAiming)
                ShooterUI.i.crosshairController.HandleCrosshairVisiblity(showEntireCrosshair: true);
            ShooterUI.i.weaponUI.SetActive(true);
            ShooterUI.i.currentWeaponImage.sprite = weapon.icon;
            previousCameraShakeStatus = playerController.PreventCameraShake;
            playerController.PreventCameraShake = true;
        }

        private void OnWeaponUnequipped()
        {
            ShooterUI.i.weaponUI.SetActive(false);
            ShooterUI.i.crosshairController.HandleCrosshairVisiblity(showEntireCrosshair: false);
            ShooterUI.i.currentWeaponImage.sprite = null;
            playerController.PreventCameraShake = previousCameraShakeStatus;
        }

        private void StartRecoil()
        {
            if (Shooter.CurrentWeapon.enableRecoil)
                playerController.CameraRecoil?.Invoke(Shooter.CurrentWeapon.weaponRecoilInfo);
        }

        #endregion

        #region smooth camera

        private IEnumerator WaitToChangeCamera()
        {
            yield return null;
            playerController.SetCustomCameraState?.Invoke(null);
        }

        private IEnumerator SmoothZoom(float targetFOV)
        {
            while (!Mathf.Approximately(cam.fieldOfView, targetFOV))
            {
                cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, targetFOV, Time.deltaTime * 300);
                yield return null;
            }
            aimViewCoroutine = null;
        }

        #endregion

        #region Utility Methods (Unused but keeping for compatibility)

        private void UpdateCharacterRotation()
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0;
            Quaternion targetRotation = Quaternion.LookRotation(forward);
            transform.rotation = targetRotation;
        }

        private void Death()
        {
            characterController.enabled = false;
            colliders = GetComponentsInChildren<Collider>().Where(c => c.enabled).ToList();
            colliders.ForEach(c => c.enabled = false);
        }

        #endregion

        public bool IsFighterMoving()
        {
            if (characterController == null)
                return false;

            Vector3 horizontalVelocity = characterController.velocity;
            return horizontalVelocity.magnitude > 0.1f;
        }

        float currentLookWeight = 0f;
        private void OnAnimatorIK(int layerIndex)
        {
            var profile = Shooter.ActiveAimProfile;
            if (Shooter.IsShooterWeaponEquipped && !itemEquipper.IsCurrentItemUnusable)
            {
                currentLookWeight = Mathf.MoveTowards(currentLookWeight, profile.headLookWeight, Time.deltaTime * profile.headLookBlendSpeed);
                animator.SetLookAtWeight(currentLookWeight);
                animator.SetLookAtPosition(Shooter.TargetAimPoint);
            }
            else
            {
                currentLookWeight = Mathf.MoveTowards(currentLookWeight, 0f, Time.deltaTime * profile.headLookBlendSpeed);
            }
        }
        void StartDodge()
        {
            if (fighterCore.Action == FighterAction.Dodging && playerController.CurrentEquippedSystem?.State == SystemState.Shooter
                && (playerController.CurrentSystemState == SystemState.Locomotion))
            {
                playerCharacter.FocusSystem(this);
                itemEquipper.PreventItemSwitching = true;
            }
        }

        void EndSystem()
        {
            if (playerController.CurrentSystemState != SystemState.Shooter) return;

            playerCharacter.UnFocusSystem(this);
            itemEquipper.PreventItemSwitching = false;
        }
    }
}