using FS_Core;
using FS_LocomotionSystem;
using FS_Util;
using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Events;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Scripting.APIUpdating;

namespace FS_CombatCore
{
    public enum AICombatType { Melee, Ranged }

    [RequireComponent(typeof(AIController))]
    [FoldoutGroup("Detection Settings", false, "detectionRayOriginOffset", "detectionRayTargetOffset", "detectionRayTargetInCoverOffset")]
    [MovedFrom("EnemyController")]
    public class CombatAIController : MonoBehaviour
    {
        [field: Tooltip("The maximum distance at which the AI can alert nearby allies or detect threats.")]
        [field: SerializeField] public float AlertRange { get; private set; } = 20f;

        [field: Tooltip("Movement speed when the AI is in combat mode.")]
        [field: SerializeField] public float CombatModeSpeed { get; private set; } = 2f;

        [Tooltip("The combat type of this AI (Melee or Ranged).")]
        [SerializeField] AICombatType aiCombatType;
        [ShowIf("aiCombatType", 0)]
        public CombatWeapon backupRangedWeapon;
        [ShowIf("aiCombatType", 0)]
        public float backupRangedWeaponPreferredRange = 20f;

        [ShowIf("aiCombatType", 1)]
        public CombatWeapon backupMeleeWeapon;
        [ShowIf("aiCombatType", 1)]
        public float backupMeleeWeaponPreferredRange = 2;

        [Tooltip("If true, the AI will not be able to move out of a certain range while in combat. Useful to create enemies that defend a certain area.")]
        [SerializeField] bool hasMovementRange = false;

        [Tooltip("The maximum distance the AI can move from its initial position while in combat. Only used if 'hasMovementRange' is true.")]
        [ShowIf("hasMovementRange", true)]
        [SerializeField] float movementRange = 15f;

        [Tooltip("If true, the AI will drop its weapon upon death.")]
        [SerializeField] bool dropWeaponOnDeath = false;

        [Tooltip("Vertical offset for the origin of detection rays (used for vision checks).")]
        [SerializeField] float detectionRayOriginOffset = 1.3f;

        [Tooltip("Vertical offset for the target's head position when performing detection checks.")]
        [SerializeField] float detectionRayTargetOffset = 1.5f;

        [Tooltip("Vertical offset for the target's head position when the target is in cover.")]
        [SerializeField] float detectionRayTargetInCoverOffset = 1f;

        [Tooltip("Interval (in seconds) at which the AI recalculates the distance to its target.")]
        [SerializeField] float distanceCalculationInterval = 0.1f;

        [Tooltip("When enabled, distance to target is calculated along the NavMesh path instead of straight-line.")]
        [SerializeField] bool useNavMeshDistance = false;

        [Tooltip("Extra distance beyond preferred range before IsTargetInPreferredRange resets to false.")]
        [SerializeField] float preferredRangeBuffer = 3f;

        [Header("Last Seen Position")]
        [Tooltip("When enabled, combat states move toward the last seen target position instead of real-time position when LOS is lost. Must be turned on when using Stealth System")]
        [SerializeField] bool useLastSeenPosition = false;

        [Tooltip("Vertical offset added to the last seen position when aiming at it. The position is recorded at feet level, so this raises the aim toward the torso.")]
        [SerializeField] float lastSeenAimHeightOffset = 1f;


        [Header("Monster Priority On Startup (no trigger required)")]
        [Tooltip("Enable on soldiers that should use independent monster-first shooting from scene startup. Does not require CombatAIManager.")]
        [SerializeField] bool monsterPriorityOnStartup = false;
        [SerializeField] GameObject startupMonster;
        [SerializeField] string startupMonsterTag = "Monster";
        [SerializeField] LayerMask startupMonsterLayers;
        [SerializeField] bool startupFallbackToPlayer = true;
        [Header("Absolute Monster Priority")]
        [SerializeField, Min(0f)] float monsterPresenceRadius = 10000f;
        [SerializeField, Min(0.1f)] float frozenMonsterAimHeight = 1.4f;
        Transform presentMonster;
        static int monsterSceneScanFrame = -1;
        static float nextMonsterSceneScan = -1f;
        static MonoBehaviour[] sceneBosses = new MonoBehaviour[0];
        static readonly Dictionary<string, GameObject[]> sceneMonsterTags = new Dictionary<string, GameObject[]>();
        static Collider[] sceneMonsterColliders;

        // Shared across soldiers. Never run a huge-radius overlap per soldier.
        static void RefreshMonsterSceneCache(bool freshForPlayerShot)
        {
            if (monsterSceneScanFrame == Time.frameCount) return;
            if (!freshForPlayerShot && Time.frameCount > monsterSceneScanFrame && Time.unscaledTime < nextMonsterSceneScan) return;
            monsterSceneScanFrame = Time.frameCount;
            nextMonsterSceneScan = Time.unscaledTime + 0.25f;
            sceneBosses = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(b => b != null && b.GetType().Name == "BossEnemyAI").ToArray();
            sceneMonsterTags.Clear();
            sceneMonsterColliders = null;
        }

        bool IsLivingMonster(Transform actor)
        {
            if (actor == null || !actor.gameObject.activeInHierarchy || actor == transform ||
                actor.IsChildOf(transform) || transform.IsChildOf(actor)) return false;
            var fighter = actor.GetComponentInParent<FighterCore>();
            if (fighter != null && (fighter.IsDead || (fighter.Damagable != null && fighter.Damagable.CurrentHealth <= 0))) return false;
            var damageable = actor.GetComponentInParent<Damagable>();
            return damageable == null || damageable.CurrentHealth > 0;
        }

        Transform FindPresentMonster(bool freshForPlayerShot = false)
        {
            float best = monsterPresenceRadius * monsterPresenceRadius;
            Transform nearest = null;
            void Consider(Transform actor)
            {
                if (!IsLivingMonster(actor)) return;
                float d = (actor.position - transform.position).sqrMagnitude;
                if (d > best) return;
                nearest = actor; best = d;
            }
            Consider(priorityMonsterRoot);
            Consider(startupMonster != null ? startupMonster.transform : null);
            Consider(presentMonster);
            if (nearest == null)
            {
                RefreshMonsterSceneCache(freshForPlayerShot);
                foreach (var boss in sceneBosses)
                    if (boss != null) Consider(boss.transform);
            }
            // A configured tag/layer can identify custom monsters without BossEnemyAI.
            // Only use the more expensive fallback when no known boss is present.
            if (nearest == null)
            {
                string tag = authoredMonsterPriority ? priorityMonsterTag : startupMonsterTag;
                if (!string.IsNullOrEmpty(tag))
                {
                    if (!sceneMonsterTags.TryGetValue(tag, out var tagged))
                    {
                        try { tagged = GameObject.FindGameObjectsWithTag(tag); }
                        catch (UnityException) { tagged = new GameObject[0]; }
                        sceneMonsterTags[tag] = tagged;
                    }
                    foreach (var actor in tagged) if (actor != null) Consider(actor.transform);
                }
                int layers = authoredMonsterPriority ? priorityMonsterLayers : startupMonsterLayers.value;
                if (nearest == null && layers != 0)
                {
                    if (sceneMonsterColliders == null)
                        sceneMonsterColliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
                    foreach (var c in sceneMonsterColliders)
                        if (c != null && c.enabled && (layers & (1 << c.gameObject.layer)) != 0)
                            Consider(ResolvePriorityMonster(c.transform));
                }
            }
            return nearest;
        }

        void RefreshMonsterPresence(bool freshForPlayerShot = false)
        {
            Transform found = FindPresentMonster(freshForPlayerShot);
            if (ReferenceEquals(found, presentMonster)) return;
            presentMonster = found;
            LayerShootingTarget = null;
            lastLayerSeenTime = float.NegativeInfinity;
            coverHoldUntil = -1f;
            ClearDefensiveThreat();
            StopLayerPursuit();
            ReleaseIndependentFireAnimation();
            nextShootingScan = 0f;
        }

        bool IsAllowedByMonsterPresence(Collider target)
        {
            if (presentMonster == null) return true;
            Transform actor = target != null ? ResolvePriorityMonster(target.transform) : null;
            return IsLivingMonster(actor) && (actor.position - transform.position).sqrMagnitude <= monsterPresenceRadius * monsterPresenceRadius;
        }


        [Header("Layer Only Shooting (independent of Fantacode combat)")]
        [SerializeField] bool layerOnlyShooting = false;
        [SerializeField] LayerMask shootingTargetLayers;
        [SerializeField] LayerMask shootingObstructionLayers = ~0;
        [SerializeField, Min(0.1f)] float shootingRange = 35f;
        [SerializeField, Range(1f, 360f)] float shootingFieldOfView = 120f;
        [SerializeField, Min(0.02f)] float shootingScanInterval = 0.1f;
        [SerializeField, Min(0.01f)] float secondsBetweenShots = 0.2f;
        [SerializeField] Transform shootingEyes;
        [SerializeField] Transform shootingMuzzle;
        [Tooltip("Optional independent gun/aim pivot, local +Z points forward.")]
        [SerializeField] Transform shootingAimPivot;
        [SerializeField] bool turnBodyToTarget = true;
        [SerializeField, Min(0f)] float shootingTurnSpeed = 540f;
        [SerializeField, Range(0f, 180f)] float shootingAimTolerance = 12f;
        [Tooltip("Stops the original AIController from competing with this independent mode. Turn off only if your movement AI does not require Fighter.Target.")]
        [SerializeField] bool stopOriginalAIController = true;
        [Tooltip("Optional cosmetic projectile prefab. Root must have a non-kinematic Rigidbody; +Z is forward. No damage is applied by this controller.")]
        [SerializeField] GameObject shootingProjectilePrefab;
        [Tooltip("Use a non-physical shot tracer instead of a projectile or impact prefab when firing at BossEnemyAI. Avoids collider/rigidbody interference with boss movement and vision.")]
        [SerializeField] bool nonPhysicalBossShots = true;
        [SerializeField, Min(0.1f)] float shootingProjectileSpeed = 60f;
        [SerializeField, Min(0.1f)] float shootingProjectileLifetime = 3f;
        [SerializeField] ParticleSystem shootingMuzzleFlash;
        [SerializeField] AudioSource shootingAudio;
        [SerializeField] AudioClip shootingSound;
        [Tooltip("Optional Animator trigger parameter. Leave blank if unused.")]
        [SerializeField] string shootingAnimationTrigger = "";
        [Tooltip("Optional impact VFX spawned at the hitscan hit point, only when no projectile prefab is assigned.")]
        [SerializeField] GameObject shootingImpactPrefab;
        [SerializeField, Min(0.1f)] float shootingImpactLifetime = 2f;
        [SerializeField] UnityEvent onLayerShot = new UnityEvent();
        [SerializeField] bool debugLayerShots = false;

        [Header("Independent Pursuit and Feedback")]
        [SerializeField] bool pursueLayerTargets = true;
        [SerializeField, Min(0.1f)] float pursuitSpeed = 3.5f;
        [SerializeField, Min(0.1f)] float preferredShootingDistance = 10f;
        [SerializeField, Min(0.1f)] float maximumFireDistance = 25f;
        [SerializeField, Min(0f)] float pursueLastSeenSeconds = 3f;
        [SerializeField, Min(0.1f)] float pursuitRepathInterval = 0.2f;
        [SerializeField] bool disableRootMotionForPursuit = true;
        [Tooltip("Optional existing Animator float parameter. Leave empty to look for common movement parameters.")]
        [SerializeField] string pursuitSpeedParameter = "";
        [Tooltip("Optional existing Animator bool parameter for movement.")]
        [SerializeField] string pursuitMovingParameter = "";
        [Tooltip("Optional full Animator state path, e.g. Base Layer.Run. Used only if supplied.")]
        [SerializeField] string pursuitMoveState = "";
        [SerializeField] string pursuitIdleState = "";
        [Header("Fantacode Locomotion Blend Tree")]
        [Tooltip("Use the existing Locomotion blend tree when its Fantacode parameters are present.")]
        [SerializeField] bool useFantacodeBlendTree = true;
        [Tooltip("Check the locomotionType blend tree thresholds. Usually 0 is free locomotion and 1 is directional strafing.")]
        [SerializeField] float fantacodeStrafeType = 1f;
        [Tooltip("Value of locomotionType for ordinary forward movement.")]
        [SerializeField] float fantacodeFreeType = 0f;
        [Tooltip("Signed moveAmount/strafeAmount at full navigation speed. Match the run threshold in your blend tree.")]
        [SerializeField, Min(0.1f)] float fantacodeRunBlend = 1f;
        [SerializeField, Min(0f)] float fantacodeBlendDamping = 0.12f;
        [Header("Independent Shooting Animation")]
        [Tooltip("Optional Animator override for independent mode. Use the Animator that contains the gun animations.")]
        [SerializeField] Animator shootingAnimatorOverride;
        [Tooltip("Print the actual runtime Animator, weapon layer weight, state and clips while fighting.")]
        [SerializeField] bool debugWeaponAnimation = true;
        [Tooltip("Let the independent weapon Animator own the arms while aiming, firing or reloading. Prevents ShooterFighter's LateUpdate idle hand IK from overriding the authored weapon pose.")]
        [SerializeField] bool suppressNativeWeaponIK = true;

        [Header("Independent Weapon Target Tracking")]
        [Tooltip("Keep authored aim/recoil clips, but use the equipped weapon's aim reference to track the visible target.")]
        [SerializeField] bool trackWeaponTarget = true;
        [Tooltip("Use the equipped Fantacode weapon's ammo spawn point instead of the manually assigned muzzle when available.")]
        [SerializeField] bool useEquippedWeaponMuzzle = true;
        [Tooltip("Wait until the real weapon's aim reference faces the target before firing. Requires a registered weapon reference.")]
        [SerializeField] bool requireWeaponAlignment = true;
        [SerializeField, Range(1f, 45f)] float weaponAlignmentTolerance = 15f;
        [Tooltip("Maximum additional upper-body correction after the native aim solver, in degrees. Zero disables the extra correction.")]
        [SerializeField, Range(0f, 85f)] float maximumWeaponAimCorrection = 45f;
        public float MaximumWeaponAimCorrection => maximumWeaponAimCorrection;
        public bool UsesIndependentShooting => layerOnlyShooting && layerShootingInitialized && isActiveAndEnabled;
        Transform equippedWeaponAimReference, equippedWeaponMuzzle;
        int equippedWeaponReferenceFrame = -100;
        bool hasVisibleWeaponAim;
        Vector3 visibleWeaponAimPoint;
        bool HasLiveWeaponReference => equippedWeaponAimReference != null &&
            equippedWeaponAimReference.gameObject.activeInHierarchy && Time.frameCount - equippedWeaponReferenceFrame <= 2;

        // Called by ShooterFighter after equip. No reference to the Shooter package
        // is needed here, so CombatCore does not acquire a new assembly dependency.
        public void RegisterIndependentWeaponReference(Animator sourceAnimator, Transform aimReference, Transform muzzle)
        {
            if (!UsesIndependentShooting || sourceAnimator != Animator) return;
            equippedWeaponAimReference = aimReference;
            equippedWeaponMuzzle = muzzle;
            equippedWeaponReferenceFrame = Time.frameCount;
        }

        public bool TryGetIndependentWeaponAim(out Vector3 point)
        {
            point = visibleWeaponAimPoint;
            return trackWeaponTarget && OwnsIndependentWeaponPose && hasVisibleWeaponAim &&
                !IsReloading && (independentAiming || fireAnimationActive) && (LayerShootingTarget != null || bossAttackHoldActive);
        }

        bool IsWeaponFacingTarget(Vector3 point)
        {
            if (!trackWeaponTarget || !requireWeaponAlignment || !HasLiveWeaponReference || !OwnsIndependentWeaponPose)
                return true;
            Vector3 direction = point - equippedWeaponAimReference.position;
            return direction.sqrMagnitude < 0.0001f ||
                Vector3.Angle(equippedWeaponAimReference.forward, direction) <= Mathf.Max(1f, weaponAlignmentTolerance);
        }

        // Use the generated layer, or a resolved upper-body fire layer if the user renamed it.
        int IndependentPoseLayer => generatedWeaponLayer > 0 ? generatedWeaponLayer : resolvedFireLayer;
        public bool IndependentPoseRequested => layerOnlyShooting && layerShootingInitialized &&
            (independentAiming || fireAnimationActive || IsReloading);
        public bool OwnsIndependentWeaponPose => suppressNativeWeaponIK && isActiveAndEnabled &&
            IndependentPoseRequested && CanAnimateIndependentFire &&
            IndependentPoseLayer > 0 && IndependentPoseLayer < Animator.layerCount &&
            (generatedWeaponLayer > 0 || Animator.GetLayerWeight(IndependentPoseLayer) > 0.001f) &&
            (Damagable == null || Damagable.CurrentHealth > 0);
        public string IndependentPoseStatus =>
            $"Suppress={suppressNativeWeaponIK}, IndependentMode={layerOnlyShooting}, Initialized={layerShootingInitialized}, " +
            $"Enabled={isActiveAndEnabled}, Animator={(Animator != null ? Animator.name : "missing")}, " +
            $"AnimatorReady={CanAnimateIndependentFire}, Layer={IndependentPoseLayer}, " +
            $"Aim={independentAiming}, Fire={fireAnimationActive}, Reload={IsReloading}, Owns={OwnsIndependentWeaponPose}";
        float nextPoseDiagnostic;
        bool poseDiagnosticWasActive;
        void LogIndependentPoseOwnership()
        {
            if (!IndependentPoseRequested) { poseDiagnosticWasActive = false; return; }
            if (poseDiagnosticWasActive && Time.unscaledTime < nextPoseDiagnostic) return;
            poseDiagnosticWasActive = true;
            nextPoseDiagnostic = Time.unscaledTime + 3f;
            if (!debugWeaponAnimation && OwnsIndependentWeaponPose) return;
            var shooters = GetComponentsInChildren<MonoBehaviour>(true)
                .Concat(GetComponentsInParent<MonoBehaviour>(true))
                .Where(c => c != null && c.GetType().Name == "ShooterFighter").Distinct();
            string receivers = string.Join(", ", shooters.Select(c => c.gameObject.name + ": enabled=" + c.isActiveAndEnabled));
            string message = "[Weapon Pose Check] " + name + ": " + IndependentPoseStatus +
                "; ShooterFighter=" + (string.IsNullOrEmpty(receivers) ? "NOT FOUND" : receivers) +
                $"; Tracking={trackWeaponTarget}, AimReference={(HasLiveWeaponReference ? equippedWeaponAimReference.name : "NOT REGISTERED")}";
            if (!OwnsIndependentWeaponPose || string.IsNullOrEmpty(receivers)) Debug.LogWarning(message, this);
            else Debug.Log(message, this);
        }
        [Tooltip("Exact Animator state name or full state path. If blank, look for common shooting state names.")]
        [SerializeField] string independentFireState = "";
        [Tooltip("-1 = automatically find a matching state, preferring upper-body layers. Set explicitly to match your Animator.")]
        [SerializeField, Min(-1)] int independentFireLayer = -1;
        [Tooltip("Keep locomotion from replacing a base-layer shot for at least this long. Upper-body shots do not interrupt locomotion.")]
        [SerializeField, Min(0.05f)] float fireAnimationHoldSeconds = 0.25f;
        [SerializeField, Min(0f)] float fireAnimationBlend = 0.04f;
        [Tooltip("Restart recoil on every shot. Disable for long or looping automatic-fire clips.")]
        [SerializeField] bool restartFireAnimationEachShot = true;
        [Tooltip("Optional existing bool parameter for aiming. Cleared when line of sight is lost or during reload.")]
        [SerializeField] string shootingAimBool = "";
        [SerializeField] bool showShotTracer = true;
        [SerializeField] Material shotTracerMaterial;
        [SerializeField, Min(0.01f)] float shotTracerLifetime = 0.07f;
        [SerializeField, Min(0.001f)] float shotTracerWidth = 0.035f;

        [Header("Defensive Shooter - Retreat and Cover")]
        [Tooltip("Retreat from a nearby monster; advance again when it moves beyond the combat distance.")]
        [SerializeField] bool retreatFromMonster = true;
        [Tooltip("Also retreat from other layer targets (including the player).")]
        [SerializeField] bool retreatFromOtherTargets = false;
        [SerializeField, Min(0.5f)] float retreatStartDistance = 8f;
        [SerializeField, Min(0.5f)] float retreatDesiredDistance = 12f;
        [SerializeField, Min(0.1f)] float retreatSpeed = 4.5f;
        [SerializeField, Min(1f)] float retreatSearchRadius = 12f;
        [Tooltip("Solid walls / cover. Everything is a useful initial setting; narrow this to your wall and cover layers if needed.")]
        [SerializeField] LayerMask retreatCoverLayers = ~0;
        [SerializeField, Min(0.1f)] float retreatCoverClearance = 1f;
        [SerializeField, Min(0.2f)] float retreatReplanInterval = 0.8f;
        [SerializeField, Min(0f)] float retreatThreatMemory = 7f;
        [SerializeField, Min(0.1f)] float retreatArrivalDistance = 0.7f;
        [Tooltip("Optional existing backpedal/strafe animation state, e.g. Base Layer.Backward. Leave empty to use Pursuit Move State.")]
        [SerializeField] string retreatMoveState = "";

        [Header("Advance / Hold / Reload")]
        [Tooltip("Advance again when the monster is farther than this. Always at least 2m beyond Retreat Desired Distance.")]
        [SerializeField, Min(1f)] float advanceWhenBeyond = 18f;
        [SerializeField, Min(0f)] float holdCoverBeforeReengage = 1.5f;
        [Tooltip("Allow a soldier to move toward an explicitly assigned monster even outside its vision range. Shots still require line of sight.")]
        [SerializeField] bool pursueAssignedMonsterWhenUnseen = true;
        [SerializeField, Min(1)] int magazineSize = 12;
        [SerializeField, Min(0.1f)] float reloadSeconds = 2.2f;
        [Tooltip("Optional Animator trigger for reload. The independent timer works even without an animation.")]
        [SerializeField] string reloadAnimationTrigger = "";
        [Tooltip("Optional reload state, preferably on an upper-body layer.")]
        [SerializeField] string reloadAnimationState = "";
        [SerializeField, Min(0)] int reloadAnimationLayer = 0;
        [SerializeField] AudioClip reloadSound;
        [SerializeField] UnityEvent onReloadStarted = new UnityEvent();
        [SerializeField] UnityEvent onReloadFinished = new UnityEvent();
        public int RoundsRemaining { get; private set; }
        public bool IsReloading { get; private set; }
        float reloadEndsAt;
        bool holdReloadBaseAnimation;
        bool reloadLayerWeightOwned;
        float savedReloadLayerWeight;
        bool advancingRoute;
        Vector3 advanceGoal, advanceRouteEnd, advanceProgressPosition;
        float advanceProgressAt, advancePendingAt = -1f;
        int advanceFailures;
        readonly List<FailedRetreatPoint> failedAdvancePoints = new List<FailedRetreatPoint>();
        float coverHoldUntil = -1f;
        float nextAdvancePlan;
        bool advanceIsDetour;
        bool advancingToThreat;

        [Header("Retreat Anti-Stuck Recovery")]
        [SerializeField, Min(0.3f)] float retreatStuckSeconds = 1.6f;
        [SerializeField, Min(0.05f)] float retreatMinimumProgress = 0.25f;
        [SerializeField, Min(0.2f)] float retreatPathPendingTimeout = 1.5f;
        [SerializeField, Min(1f)] float retreatFailedPointMemory = 12f;
        [SerializeField, Min(0.2f)] float retreatFailedPointRadius = 1.5f;
        [SerializeField, Min(1f)] float retreatMaximumSearchRadius = 35f;
        [SerializeField, Min(0.1f)] float retreatEmergencyRetryInterval = 0.35f;
        [Tooltip("Recover an agent that has drifted slightly off its NavMesh. No long-distance teleporting.")]
        [SerializeField] bool recoverOffNavMesh = true;
        [SerializeField, Min(0.1f)] float retreatNavMeshRecoveryRadius = 0.8f;

        struct FailedRetreatPoint
        {
            public Vector3 position;
            public float expires;
        }
        readonly List<FailedRetreatPoint> failedRetreatPoints = new List<FailedRetreatPoint>();
        float retreatBestRemaining = float.PositiveInfinity;
        float retreatPendingSince = -1f;
        float retreatPhysicalProgressAt;
        float nextAgentRecovery;
        int retreatFailures;
        bool retreatInProgress;
        float retreatPlanDistance;
        int retreatAvoidancePriority = -1;

        Transform defensiveThreat;
        Vector3 defensiveThreatPosition, defensiveThreatAim, retreatDestination;
        Vector3 retreatPlanThreatPosition;
        float defensiveThreatSeenAt = float.NegativeInfinity;
        float nextRetreatPlan;
        float retreatProgressAt;
        Vector3 retreatProgressPosition;
        bool defensiveMoving, retreatHasDestination, retreatDestinationIsCover;
        bool pursuitWasDefensive;

        float nextPursuitRepath;
        float lastLayerSeenTime = float.NegativeInfinity;
        Vector3 lastLayerSeenPosition;
        bool pursuitWasMoving;
        bool pursuitAnimationInitialized;
        string resolvedSpeedParameter, resolvedMovingParameter;
        string resolvedFireTrigger, resolvedAimBool;
        string fsMove, fsStrafe, fsLocomotion, fsRotation;
        bool fsBlendActive, independentAiming;
        int fsLocomotionHash;
        int generatedWeaponLayer = -1;
        RuntimeAnimatorController cachedWeaponController;
        float nextWeaponAnimationDebug;
        float nextWeaponAnimatorSearch;
        const string GeneratedWeaponLayerName = "Independent Weapon";
        int resolvedFireLayer = -1, resolvedFireHash;
        float fireAnimationUntil = float.NegativeInfinity;
        bool fireAnimationActive, fireLayerWeightOwned;
        float savedFireLayerWeight;
        Material generatedTracerMaterial;
        float nextLayerStatusLog;
        bool independentPursuitOwnsAgent;

        public Collider LayerShootingTarget { get; private set; }
        public string LayerShootingStatus { get; private set; }
        public Action<RaycastHit> OnLayerHitscanHit;
        float nextShootingScan;
        float nextLayerShot;
        bool layerShootingInitialized;
        bool standardCombatRegistered;
        Transform authoredLayerTargetRoot;
        int authoredLayerTargetMask;

        // Persistent authored monster priority (survives the one-shot trigger).
        bool authoredMonsterPriority;
        Transform priorityMonsterRoot;
        Transform priorityPlayerRoot;
        string priorityMonsterTag;
        int priorityMonsterLayers;
        bool priorityFallbackToPlayer;

        public bool SetAuthoredMonsterPriority(GameObject monster, GameObject player,
            string monsterTag, LayerMask monsterLayers, bool fallbackToPlayer)
        {
            if (!gameObject.activeInHierarchy ||
                (Damagable != null && Damagable.CurrentHealth <= 0)) return false;

            ConfigureAuthoredMonsterPriority(monster, player, monsterTag, monsterLayers, fallbackToPlayer);
            if (standardCombatRegistered && CombatAIManager.i != null)
                CombatAIManager.i.RemoveCombatAI(this);
            standardCombatRegistered = false;
            stopOriginalAIController = true;
            enabled = true;
            InitializeLayerShooting();
            if (debugLayerShots)
                Debug.Log("[Layer Shooting] Monster priority active.", this);
            return true;
        }

        // Called during the authored trigger's Awake, before any Start methods.
        // Safe for inactive enemies and does not depend on cached Awake references.
        public void PrepareAuthoredMonsterPriority(GameObject monster, GameObject player,
            string monsterTag, LayerMask monsterLayers, bool fallbackToPlayer)
        {
            ConfigureAuthoredMonsterPriority(monster, player, monsterTag, monsterLayers, fallbackToPlayer);
            // Independent shooting owns targeting and movement; prevent the native AI
            // from attempting manager-dependent initialization before our Start.
            var nativeAI = GetComponent<AIController>();
            if (nativeAI != null) nativeAI.enabled = false;
            enabled = true;
        }

        private void ConfigureAuthoredMonsterPriority(GameObject monster, GameObject player,
            string monsterTag, LayerMask monsterLayers, bool fallbackToPlayer)
        {
            priorityMonsterRoot = monster != null ? monster.transform : null;
            priorityPlayerRoot = player != null ? player.transform : null;
            priorityMonsterTag = monsterTag;
            priorityMonsterLayers = monsterLayers.value;
            priorityFallbackToPlayer = fallbackToPlayer;
            authoredMonsterPriority = true;
            authoredLayerTargetRoot = null;
            authoredLayerTargetMask = 0;
            LayerShootingTarget = null;

            // Both targets use the same independent aiming and firing path. This
            // allows immediate switching even when the monster has no FighterCore.
            layerOnlyShooting = true;
            stopOriginalAIController = true;
        }

        Transform ResolvePriorityMonster(Transform candidate)
        {
            if (candidate == null) return null;
            if (priorityMonsterRoot != null &&
                (candidate == priorityMonsterRoot || candidate.IsChildOf(priorityMonsterRoot)))
                return priorityMonsterRoot;

            if (startupMonster != null && (candidate == startupMonster.transform || candidate.IsChildOf(startupMonster.transform)))
                return startupMonster.transform;
            string tag = authoredMonsterPriority ? priorityMonsterTag : startupMonsterTag;
            int layers = authoredMonsterPriority ? priorityMonsterLayers : startupMonsterLayers.value;
            Transform matched = null;
            for (Transform t = candidate; t != null; t = t.parent)
            {
                if (t.GetComponent("BossEnemyAI") != null) return t;
                if (!string.IsNullOrEmpty(tag) && t.tag == tag) return t;
                if ((layers & (1 << t.gameObject.layer)) != 0) matched = t;
            }
            return matched;
        }

        Transform ResolvePriorityPlayer(Transform candidate)
        {
            if (!priorityFallbackToPlayer || candidate == null) return null;
            if (priorityPlayerRoot != null &&
                (candidate == priorityPlayerRoot || candidate.IsChildOf(priorityPlayerRoot)))
                return priorityPlayerRoot;
            for (Transform t = candidate; t != null; t = t.parent)
                if (t.CompareTag("Player")) return t;
            return null;
        }

        Transform ResolvePriorityActor(Collider candidate)
        {
            if (candidate == null) return null;
            Transform monster = ResolvePriorityMonster(candidate.transform);
            return monster != null ? monster : ResolvePriorityPlayer(candidate.transform);
        }

        bool SameLayerActor(Collider a, Collider b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            if (!authoredMonsterPriority && presentMonster == null) return false;
            Transform actor = ResolvePriorityActor(a);
            return actor != null && actor == ResolvePriorityActor(b);
        }


        // A one-way switch to independent shooting for an authored target without FighterCore.
        // Standard combat remains unchanged for ordinary FighterCore targets.
        public bool TrySetAuthoredTarget(GameObject targetObject)
        {
            if (targetObject == null || !gameObject.activeInHierarchy) return false;
            if (Damagable != null && Damagable.CurrentHealth <= 0) return false;

            FighterCore targetFighter = targetObject.GetComponentInParent<FighterCore>();
            if (targetFighter == null)
                targetFighter = targetObject.GetComponentInChildren<FighterCore>();

            bool nativeTarget = targetFighter != null && targetFighter.Damagable != null;
            if (nativeTarget && targetFighter.IsDead) return false;

            if (!layerOnlyShooting && nativeTarget)
            {
                if (!isActiveAndEnabled || !standardCombatRegistered || calcTimer == null ||
                    CombatAIManager.i == null || Fighter == null || AIController == null ||
                    !AIController.isActiveAndEnabled)
                {
                    Debug.LogWarning("[Authored AI] Standard combat is not initialized. Check CombatAIManager and AIController.", this);
                    return false;
                }
                if (targetFighter == Fighter) return false;
                AIController.Blackboard.Set(CombatCoreBlackboardKeys.TriggerAuthoredBehavior, true);
                Fighter.Target = targetFighter;
                return true;
            }

            Transform root = targetFighter != null ? targetFighter.transform : targetObject.transform;
            if (root == transform || root.IsChildOf(transform) || transform.IsChildOf(root)) return false;
            int mask = 0;
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                mask |= 1 << c.gameObject.layer;
            if (mask == 0)
            {
                Debug.LogWarning("[Authored AI] Target has no colliders: " + root.name, root);
                return false;
            }

            // Stop standard combat before starting the independent shooter. Do not install
            // health, damage or FighterCore components on the target.
            layerOnlyShooting = true;
            if (standardCombatRegistered && CombatAIManager.i != null)
                CombatAIManager.i.RemoveCombatAI(this);
            standardCombatRegistered = false;
            authoredMonsterPriority = false;
            authoredLayerTargetRoot = root;
            authoredLayerTargetMask = mask;
            LayerShootingTarget = null;
            stopOriginalAIController = true;
            enabled = true;
            InitializeLayerShooting();
            if (debugLayerShots)
                Debug.Log("[Authored AI] Independent target: " + root.name, this);
            return true;
        }

        public bool UseLastSeenPosition => useLastSeenPosition;

        public float LastSeenAimHeightOffset => lastSeenAimHeightOffset;

        /// <summary>
        /// Single source of truth for target visibility — reads from vision sensors, never does its own LOS check.
        /// Stealth path: AIVision → HasVisualOnTarget blackboard key. Non-stealth path: SimpleVision → TargetsInVision.
        /// </summary>
        public bool IsTargetVisible
        {
            get
            {
                if (layerOnlyShooting)
                    return CanSeeLayerTarget(LayerShootingTarget, out _);
                if (Fighter == null || Fighter.Target == null) return false;
                if (AIController.Blackboard.TryGet<bool>(AIBlackboardKeys.HasVisualOnTarget, out bool hasVisual))
                    return hasVisual;
                return AIController.TargetsInVision.Contains(Fighter.Target.transform);
            }
        }
        public bool HasMovementRange => hasMovementRange;
        public float GetMovementRange() => hasMovementRange ? movementRange : float.MaxValue;
        public Vector3 InitialPosition { get; private set; }

        public bool DropWeaponOnDeath => dropWeaponOnDeath;

        public float CombatMovementTimer { get; set; } = 0f;

        public AIController AIController { get; private set; }
        public NavMeshAgent NavAgent { get; private set; }
        public CharacterController CharacterController { get; private set; }
        public Animator Animator { get; private set; }
        public FighterCore Fighter { get; private set; }
        public LocomotionCore Locomotion { get; private set; }
        public Damagable Damagable { get; private set; }
        public BoneMapper BoneMapper { get; private set; }
        public ItemEquipper ItemEquipper { get; private set; }

        public Action OnSelectedAsTarget;
        public Action OnRemovedAsTarget;

        FSTimer calcTimer;

        public AICombatType AICombatType
        {
            get => aiCombatType;
            set { aiCombatType = value; }
        }

        private void Awake()
        {
            AIController = GetComponent<AIController>();
            Fighter = GetComponent<FighterCore>();
            Locomotion = GetComponent<LocomotionCore>();

            NavAgent = GetComponent<NavMeshAgent>();
            CharacterController = GetComponent<CharacterController>();
            Animator = GetComponent<Animator>();
            //Animator.fireEvents = false;
            Damagable = GetComponent<Damagable>();
            BoneMapper = GetComponent<BoneMapper>();
            ItemEquipper = GetComponent<ItemEquipper>();

            // This per-enemy setting works even if no trigger references this soldier.
            // Do not overwrite priority already configured by an authored trigger's Awake.
            if (monsterPriorityOnStartup && !authoredMonsterPriority)
            {
                PrepareAuthoredMonsterPriority(startupMonster, null,
                    startupMonsterTag, startupMonsterLayers, startupFallbackToPlayer);
            }

            if (layerOnlyShooting) return;

            if (CharacterController != null)
                CharacterController.excludeLayers = LayerMask.GetMask("Hitbox");

            var bodyCapsule = GetComponent<CapsuleCollider>();
            if (bodyCapsule != null)
                bodyCapsule.isTrigger = true;
        }

        private void Start()
        {
            InitialPosition = transform.position;

            if (layerOnlyShooting)
            {
                InitializeLayerShooting();
                if (authoredMonsterPriority)
                    Debug.Log($"[CombatAIController] {name}: Monster priority active at startup (independent shooting).", this);
                return;
            }

            if (AIController == null || Fighter == null || Damagable == null ||
                NavAgent == null || CombatAIManager.i == null)
            {
                Debug.LogError($"[CombatAIController] Standard combat initialization failed on {name}. " +
                    $"AIController={AIController != null}, Fighter={Fighter != null}, " +
                    $"Damagable={Damagable != null}, NavAgent={NavAgent != null}, " +
                    $"CombatAIManager={CombatAIManager.i != null}. " +
                    "For native combat restore the scene manager. For independent monster-first shooting, enable Monster Priority On Startup on this enemy.", this);
                enabled = false;
                return;
            }

            AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsGettingHit, false);
            AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsCombatTypeMelee, aiCombatType == AICombatType.Melee);

            CombatAIManager.i.RegisterCombatAI(this);
            standardCombatRegistered = true;

            Damagable.OnDead += OnDeath;

            Fighter.OnTargetSet += (FighterCore target) =>
            {
                if (!layerOnlyShooting) AlertNearbyAllies();
            };

            Fighter.OnHitComplete += () =>
            {
                if (!layerOnlyShooting)
                    AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsGettingHit, false);
            };

            Fighter.OnGotHit += (FighterCore attacker, Vector3 hitPoint, float hittingTime, bool isBlockedHit, HitType hitType) =>
            {
                if (layerOnlyShooting) return;
                AIController.Blackboard.Set(CombatCoreBlackboardKeys.WasHurt, true);   // Some NPC should only go to Flee behavior if they were hurt by the player

                // A hit from the player proves their position without needing line of sight.
                // Published for the Stealth System (which consumes the key); AI-vs-AI hits don't count
                if (attacker != null && CombatAIManager.i != null && attacker == CombatAIManager.i.Player)
                    AIController.Blackboard.Set(AIBlackboardKeys.HitByPlayerPosition, attacker.transform.position);

                if (Fighter.Damagable.CurrentHealth > 0)
                {
                    if (Fighter.LastHitWillPlayReaction && Fighter.CurrReaction != null && !Fighter.CurrReaction.isAdditiveAnimation)
                        AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsGettingHit, true);
                }

            };


            // Fighter.OnDeath += () => 
            // { 
            //     // if (Fighter.Damagable.IsDead)
            //     //     AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsDead, true);

            //     // AIController.Blackboard.Set(CombatCoreBlackboardKeys.Health, Fighter.Damagable.CurrentHealth);
            // };

            Fighter.OnWeaponEquipAction += (CombatWeapon weaponData) =>
            {
                if (weaponData.OverrideMoveSpeed)
                {
                    CombatModeSpeed = weaponData.CombatMoveSpeed;
                }
            };

            Fighter.OnResetFighter += () =>
            {
                CharacterController.enabled = true;
                colliders.ForEach(c => c.enabled = true);
                Fighter.Locomotion.ResetAllMovementBlock();
            };
            Fighter.OnDeath += Death;

            NavAgent.stoppingDistance = 0f;

            calcTimer = new FSTimer(distanceCalculationInterval, repeat: true);
            calcTimer.OnCompleted += OnCalcTimerTick;
            calcTimer.Start();
        }


        List<Collider> colliders = new List<Collider>();
        public void Death()
        {
            //if (CharacterController != null)
            //    CharacterController.enabled = false;

            var capsuleCollider = GetComponent<CapsuleCollider>();
            if (capsuleCollider != null)
                capsuleCollider.enabled = false;
        }

        private Transform bossAttackHoldOwner;
        private Vector3 bossAttackHoldAnchor;
        private bool bossAttackHoldActive;
        public bool IsHeldByBossAttack => bossAttackHoldActive;

        public void BeginBossEnemyAttackHold(Transform boss)
        {
            if (boss == null || !isActiveAndEnabled || !layerOnlyShooting ||
                (Damagable != null && Damagable.CurrentHealth <= 0)) return;
            if (bossAttackHoldActive) return;
            bossAttackHoldOwner = boss;
            bossAttackHoldAnchor = transform.position;
            bossAttackHoldActive = true;
            ClearDefensiveThreat();
            StopLayerPursuit();
            advancingToThreat = false;
            LayerShootingTarget = null;
            hasVisibleWeaponAim = false;
            SetIndependentAim(false);
            ReleaseIndependentFireAnimation();
            RestoreIndependentReloadLayer();
            // Do not refill ammo or complete a reload while locked.
            IsReloading = false;
            holdReloadBaseAnimation = false;
            pursuitAnimationInitialized = false;
            HoldForBossAttack();
            ApplyGeneratedWeaponWeight();
            SetLayerStatus("Boss attack state - stationary, aiming at boss, firing stopped");
            Debug.Log("[Boss Attack] Soldier stopped: " + name, this);
        }

        public void EndBossEnemyAttackHold(Transform boss)
        {
            if (bossAttackHoldActive && bossAttackHoldOwner == boss)
                ReleaseBossAttackHold();
        }

        private void ReleaseBossAttackHold()
        {
            if (!bossAttackHoldActive) return;
            bossAttackHoldActive = false;
            bossAttackHoldOwner = null;
            LayerShootingTarget = null;
            hasVisibleWeaponAim = false;
            SetIndependentAim(false);
            if (CanMoveLayerAgent())
            {
                NavAgent.nextPosition = transform.position;
                NavAgent.updatePosition = true;
            }
            nextShootingScan = 0f;
            nextLayerShot = Time.time;
            pursuitAnimationInitialized = false;
        }

        private bool CheckBossAttackHold()
        {
            if (!bossAttackHoldActive) return false;
            if (bossAttackHoldOwner == null || !bossAttackHoldOwner.gameObject.activeInHierarchy ||
                (Damagable != null && Damagable.CurrentHealth <= 0))
            {
                ReleaseBossAttackHold();
                return false;
            }
            HoldForBossAttack();
            return true;
        }

        private void HoldForBossAttack()
        {
            if (CanMoveLayerAgent())
            {
                NavAgent.isStopped = true;
                if (NavAgent.hasPath || NavAgent.pathPending) NavAgent.ResetPath();
                NavAgent.velocity = Vector3.zero;
                NavAgent.updatePosition = false;
                NavAgent.updateRotation = false;
                NavAgent.nextPosition = bossAttackHoldAnchor;
            }
            if (Animator != null && Animator.applyRootMotion) Animator.applyRootMotion = false;
            transform.position = bossAttackHoldAnchor;
            if (bossAttackHoldOwner != null)
            {
                // The attacker owns the frozen target even if another monster is nearer.
                // Keep the hips planted, but turn in place and track the moving boss.
                Vector3 point = bossAttackHoldOwner.position + Vector3.up * frozenMonsterAimHeight;
                Vector3 flat = bossAttackHoldOwner.position - bossAttackHoldAnchor;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
                visibleWeaponAimPoint = point;
                hasVisibleWeaponAim = true;
                SetIndependentAim(true);
                if (shootingAimPivot != null && !(trackWeaponTarget && HasLiveWeaponReference))
                {
                    Vector3 direction = point - shootingAimPivot.position;
                    if (direction.sqrMagnitude > 0.0001f)
                        shootingAimPivot.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
                // A previous recoil/reload may still be playing on the weapon layer.
                int layer = generatedWeaponLayer;
                if (CanAnimateIndependentFire && layer > 0 && layer < Animator.layerCount)
                {
                    int aim = Animator.StringToHash(GeneratedWeaponLayerName + ".Aim");
                    if (Animator.HasState(layer, aim) &&
                        Animator.GetCurrentAnimatorStateInfo(layer).fullPathHash != aim &&
                        !(Animator.IsInTransition(layer) && Animator.GetNextAnimatorStateInfo(layer).fullPathHash == aim))
                        Animator.CrossFadeInFixedTime(aim, 0.04f, layer);
                }
            }
            // Stop the blend-tree immediately instead of damping from a run.
            if (CanAnimateIndependentFire && fsBlendActive)
            {
                Animator.SetFloat(fsMove, 0f);
                Animator.SetFloat(fsStrafe, 0f);
                Animator.SetFloat(fsLocomotion, fantacodeStrafeType);
                if (!string.IsNullOrEmpty(fsRotation)) Animator.SetFloat(fsRotation, 0f);
                if (fsLocomotionHash != 0 && Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != fsLocomotionHash &&
                    !(Animator.IsInTransition(0) && Animator.GetNextAnimatorStateInfo(0).fullPathHash == fsLocomotionHash))
                    Animator.CrossFadeInFixedTime(fsLocomotionHash, 0.12f, 0);
            }
            else UpdatePursuitAnimation();
        }

        private void Update()
        {
            if (Time.deltaTime == 0) return;

            if (layerOnlyShooting)
            {
                if (layerShootingInitialized)
                {
                    RefreshIndependentAnimatorBinding();
                    if (CheckBossAttackHold())
                    {
                        ApplyGeneratedWeaponWeight();
                        return;
                    }
                    UpdateLayerShooting();
                    ApplyGeneratedWeaponWeight(); // Before Unity evaluates animation.
                    LogIndependentPoseOwnership();
                }
                return;
            }

            if (calcTimer == null || AIController == null || Fighter == null) return;

            AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsKnockedDown, Fighter.IsKnockedDown);

            if (Fighter.Target == null)
                FindTargetFromVision();

            UpdateTargetTracking();

            if (Fighter.IsBusy) return;

            calcTimer.Update(Time.deltaTime);
        }

        // Layer-only mode deliberately never assigns Fighter.Target or calls Damagable.
        void InitializeLayerShooting()
        {
            Animator = ResolveIndependentAnimator();
            if (stopOriginalAIController && AIController != null)
            {
                AIController.enabled = false;
                if (NavAgent != null && NavAgent.isActiveAndEnabled)
                {
                    NavAgent.updatePosition = true;
                    NavAgent.updateRotation = false;
                    NavAgent.speed = Mathf.Max(0.1f, pursuitSpeed);
                    independentPursuitOwnsAgent = true;
                    if (NavAgent.isOnNavMesh)
                    {
                        NavAgent.isStopped = true;
                        NavAgent.ResetPath();
                    }
                }
                if (Animator != null && disableRootMotionForPursuit)
                    Animator.applyRootMotion = false;
            }

            if (shootingTargetLayers.value == 0 && authoredLayerTargetRoot == null && !authoredMonsterPriority)
                Debug.LogWarning("[Layer Shooting] Assign Shooting Target Layers in the Inspector.", this);
            if (shootingMuzzle == null)
                Debug.LogWarning("[Layer Shooting] No muzzle assigned; using eye position.", this);
            if (shootingProjectilePrefab != null)
            {
                Rigidbody body = shootingProjectilePrefab.GetComponent<Rigidbody>();
                if (body == null || body.isKinematic)
                {
                    Debug.LogError("[Layer Shooting] Projectile prefab needs a non-kinematic Rigidbody on its root. " +
                        "Falling back to hitscan.", this);
                    shootingProjectilePrefab = null;
                }
            }

            layerShootingInitialized = true;
            nextShootingScan = 0f;
            nextLayerShot = 0f;
            RoundsRemaining = Mathf.Max(1, magazineSize);
            IsReloading = false;
            holdReloadBaseAnimation = false;
            advancingRoute = false;
            advanceFailures = 0;
            failedAdvancePoints.Clear();
            nextAdvancePlan = 0f;
            coverHoldUntil = -1f;
            advancingToThreat = false;
            nextPursuitRepath = 0f;
            lastLayerSeenTime = float.NegativeInfinity;
            ClearDefensiveThreat();
            CachePursuitAnimatorParameters();
            CacheIndependentFireAnimation();
            Debug.Log("[Weapon Pose Init] " + name + ": " + IndependentPoseStatus, this);
            if (pursueLayerTargets && !stopOriginalAIController)
                Debug.LogWarning("[Layer Shooting] Independent pursuit requires Stop Original AI Controller.", this);
            if (pursueLayerTargets && !CanMoveLayerAgent())
                Debug.LogWarning("[Layer Shooting] Pursuit needs an enabled NavMeshAgent on a baked NavMesh.", this);
        }

        Vector3 LayerEyePosition => shootingEyes != null ? shootingEyes.position :
            transform.position + Vector3.up * detectionRayOriginOffset;

        Vector3 LayerMuzzlePosition => useEquippedWeaponMuzzle && equippedWeaponMuzzle != null &&
            equippedWeaponMuzzle.gameObject.activeInHierarchy && Time.frameCount - equippedWeaponReferenceFrame <= 2
            ? equippedWeaponMuzzle.position : shootingMuzzle != null ? shootingMuzzle.position : LayerEyePosition;

        bool IsOwnCollider(Collider other)
        {
            return other != null && (other.transform == transform || other.transform.IsChildOf(transform));
        }

        bool IsLayerTarget(Collider other)
        {
            if (other == null || !other.enabled || !other.gameObject.activeInHierarchy || IsOwnCollider(other))
                return false;
            if (!IsAllowedByMonsterPresence(other)) return false;
            if (authoredMonsterPriority || presentMonster != null)
            {
                Transform actor = ResolvePriorityActor(other);
                if (actor == null || actor == transform || actor.IsChildOf(transform) || transform.IsChildOf(actor))
                    return false;
                FighterCore fighter = actor.GetComponentInParent<FighterCore>();
                return fighter == null || fighter.Damagable == null || !fighter.IsDead;
            }
            if (authoredLayerTargetRoot != null)
                return other.transform == authoredLayerTargetRoot || other.transform.IsChildOf(authoredLayerTargetRoot);
            // If an explicitly authored target was destroyed, do not silently shoot something else.
            if (authoredLayerTargetMask != 0) return false;
            return (shootingTargetLayers.value & (1 << other.gameObject.layer)) != 0;
        }

        // Include target layers even if the obstruction mask excludes them.
        // Ignore our own colliders and unrelated triggers; target triggers are valid hits.
        bool TryLayerRay(Vector3 origin, Vector3 point, out RaycastHit nearest)
        {
            nearest = default;
            Vector3 delta = point - origin;
            if (delta.sqrMagnitude < 0.000001f) return false;
            // Priority actors may have child colliders on arbitrary layers.
            int mask = (authoredMonsterPriority || presentMonster != null) ? ~0 :
                shootingObstructionLayers.value | shootingTargetLayers.value | authoredLayerTargetMask;
            RaycastHit[] hits = Physics.RaycastAll(origin, delta.normalized,
                delta.magnitude + 0.05f, mask, QueryTriggerInteraction.Collide);
            float best = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || IsOwnCollider(hit.collider)) continue;
                bool isTarget = IsLayerTarget(hit.collider);
                if (hit.collider.isTrigger && !isTarget) continue;
                if ((authoredMonsterPriority || presentMonster != null) && !isTarget &&
                    (shootingObstructionLayers.value & (1 << hit.collider.gameObject.layer)) == 0) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                nearest = hit;
            }
            return nearest.collider != null;
        }

        bool CanSeeLayerTarget(Collider target, out Vector3 aimPoint)
        {
            aimPoint = default;
            if (!IsLayerTarget(target)) return false;
            aimPoint = target.bounds.center;
            Vector3 direction = aimPoint - LayerEyePosition;
            if (direction.sqrMagnitude < 0.000001f || direction.sqrMagnitude > shootingRange * shootingRange)
                return false;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (shootingFieldOfView < 360f && flatDirection.sqrMagnitude > 0.0001f &&
                Vector3.Angle(forward, flatDirection) > shootingFieldOfView * 0.5f)
                return false;
            return TryLayerRay(LayerEyePosition, aimPoint, out RaycastHit hit) && SameLayerActor(hit.collider, target);
        }

        Collider FindLayerShootingTarget()
        {
            Collider nearest = null;
            float best = float.PositiveInfinity;
            Vector3 origin = LayerEyePosition;
            if (authoredMonsterPriority || presentMonster != null)
            {
                Collider monster = null, player = null;
                float monsterDistance = float.PositiveInfinity, playerDistance = float.PositiveInfinity;
                foreach (Collider candidate in Physics.OverlapSphere(origin, shootingRange, ~0, QueryTriggerInteraction.Collide))
                {
                    if (!CanSeeLayerTarget(candidate, out Vector3 point)) continue;
                    float distance = (point - origin).sqrMagnitude;
                    if (ResolvePriorityMonster(candidate.transform) != null)
                    {
                        if (distance < monsterDistance) { monster = candidate; monsterDistance = distance; }
                    }
                    else if (distance < playerDistance)
                    {
                        player = candidate; playerDistance = distance;
                    }
                }
                return monster != null ? monster : (presentMonster != null ? null : player);
            }
            Collider[] candidates = authoredLayerTargetRoot != null
                ? authoredLayerTargetRoot.GetComponentsInChildren<Collider>()
                : Physics.OverlapSphere(origin, shootingRange, shootingTargetLayers, QueryTriggerInteraction.Collide);
            foreach (Collider candidate in candidates)
            {
                if (!CanSeeLayerTarget(candidate, out Vector3 point)) continue;
                float distance = (point - origin).sqrMagnitude;
                if (distance >= best) continue;
                nearest = candidate;
                best = distance;
            }
            return nearest;
        }

        bool CanMoveLayerAgent()
        {
            return independentPursuitOwnsAgent && NavAgent != null &&
                NavAgent.isActiveAndEnabled && NavAgent.isOnNavMesh;
        }

        void StopLayerPursuit()
        {
            defensiveMoving = false;
            if (advancingRoute) ClearAdvanceRoute(true);
            if (!CanMoveLayerAgent()) return;
            if (!NavAgent.isStopped) NavAgent.isStopped = true;
            if (NavAgent.hasPath || NavAgent.pathPending) NavAgent.ResetPath();
            nextPursuitRepath = 0f;
        }

        void ClearAdvanceRoute(bool clearFailures)
        {
            advancingRoute = false;
            advancePendingAt = -1f;
            nextAdvancePlan = 0f;
            if (clearFailures) { advanceFailures = 0; failedAdvancePoints.Clear(); }
        }

        void FailAdvanceRoute(string reason)
        {
            if (advancingRoute)
                failedAdvancePoints.Add(new FailedRetreatPoint
                {
                    position = advanceRouteEnd,
                    expires = Time.time + Mathf.Max(2f, retreatFailedPointMemory)
                });
            if (failedAdvancePoints.Count > 32) failedAdvancePoints.RemoveAt(0);
            ClearAdvanceRoute(false);
            advanceFailures = Mathf.Min(10, advanceFailures + 1);
            if (CanMoveLayerAgent())
            {
                NavAgent.ResetPath();
                NavAgent.isStopped = true;
            }
            if (debugLayerShots) Debug.Log($"[Layer Shooting] {name}: advance recovery #{advanceFailures}: {reason}", this);
        }

        void ConsiderAdvancePoint(Vector3 raw, Vector3 destination, float stop, bool detour,
            ref float best, ref NavMeshPath bestPath, ref Vector3 bestEnd, ref bool bestDetour)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = NavAgent.agentTypeID, areaMask = NavAgent.areaMask };
            if (!NavMesh.SamplePosition(raw, out NavMeshHit sample, 1.25f, filter)) return;
            if (Vector3.Distance(transform.position, sample.position) < (detour ? 0.65f : Mathf.Max(0.65f, stop + 0.2f))) return;
            if (failedAdvancePoints.Exists(f => f.expires > Time.time && RetreatFlatDistance(f.position, sample.position) < retreatFailedPointRadius)) return;
            var path = new NavMeshPath();
            if (!NavAgent.CalculatePath(sample.position, path) || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2) return;
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            float score = RetreatFlatDistance(sample.position, destination) * 2f + length * 0.35f + (detour ? 1f : 0f);
            if (score >= best) return;
            best = score; bestPath = path; bestEnd = sample.position; bestDetour = detour;
        }

        bool MoveLayerAgent(Vector3 destination, float stoppingDistance)
        {
            if (!pursueLayerTargets || !TryRecoverRetreatAgent() || !CanMoveLayerAgent()) return false;
            float stop = Mathf.Max(0.15f, stoppingDistance);
            NavAgent.updatePosition = true;
            NavAgent.updateRotation = false;
            NavAgent.autoBraking = true;
            NavAgent.speed = Mathf.Max(0.1f, pursuitSpeed);
            defensiveMoving = false;
            if (advancingRoute)
            {
                if (RetreatFlatDistance(transform.position, advanceProgressPosition) >= retreatMinimumProgress)
                {
                    advanceProgressPosition = transform.position;
                    advanceProgressAt = Time.time;
                }
                if (NavAgent.pathPending)
                {
                    if (advancePendingAt < 0f) advancePendingAt = Time.time;
                    if (Time.time - advancePendingAt >= retreatPathPendingTimeout) FailAdvanceRoute("path pending timeout");
                }
                else
                {
                    advancePendingAt = -1f;
                    bool reached = RetreatFlatDistance(transform.position, advanceRouteEnd) <= NavAgent.stoppingDistance + 0.35f &&
                        Mathf.Abs(transform.position.y - advanceRouteEnd.y) <= Mathf.Max(1f, Mathf.Abs(NavAgent.baseOffset) + 0.5f);
                    if (reached)
                    {
                        if (advanceIsDetour && RetreatFlatDistance(transform.position, destination) > stop + 0.5f)
                            FailAdvanceRoute("alternate waypoint reached; seeking next route");
                        else { ClearAdvanceRoute(true); return false; }
                    }
                    else if (!NavAgent.hasPath || NavAgent.pathStatus != NavMeshPathStatus.PathComplete ||
                        Time.time - advanceProgressAt >= retreatStuckSeconds)
                        FailAdvanceRoute("path lost or no physical progress");
                }
                if (advancingRoute && (NavAgent.pathPending || advanceIsDetour ||
                    Time.time < nextAdvancePlan || RetreatFlatDistance(advanceGoal, destination) < 1.5f))
                {
                    NavAgent.isStopped = false;
                    return true;
                }
            }
            if (Time.time < nextAdvancePlan) return advancingRoute;
            nextAdvancePlan = Time.time + Mathf.Max(0.15f, advanceFailures > 0 ? retreatEmergencyRetryInterval : pursuitRepathInterval);
            failedAdvancePoints.RemoveAll(f => f.expires <= Time.time);
            NavMeshPath bestPath = null;
            Vector3 bestEnd = Vector3.zero;
            bool bestDetour = false;
            float best = float.PositiveInfinity;
            ConsiderAdvancePoint(destination, destination, stop, false, ref best, ref bestPath, ref bestEnd, ref bestDetour);
            if (bestPath == null || advanceFailures > 0)
            {
                for (int ring = 1; ring <= 3; ring++)
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = (i + ring * 0.5f) * Mathf.PI / 6f;
                        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * 2f);
                        ConsiderAdvancePoint(destination + offset, destination, stop, true, ref best, ref bestPath, ref bestEnd, ref bestDetour);
                        if (advanceFailures > 0)
                            ConsiderAdvancePoint(transform.position + offset, destination, stop, true, ref best, ref bestPath, ref bestEnd, ref bestDetour);
                    }
            }
            if (bestPath == null)
            {
                if (advancingRoute && NavAgent.hasPath && NavAgent.pathStatus == NavMeshPathStatus.PathComplete) return true;
                NavAgent.isStopped = true;
                advanceFailures = Mathf.Min(10, advanceFailures + 1);
                return false;
            }
            NavAgent.stoppingDistance = bestDetour ? 0.2f : stop;
            if (!NavAgent.SetPath(bestPath)) { FailAdvanceRoute("SetPath failed"); return false; }
            if (!advancingRoute)
            {
                advanceProgressAt = Time.time;
                advanceProgressPosition = transform.position;
            }
            advancingRoute = true;
            advanceGoal = destination;
            advanceRouteEnd = bestEnd;
            advanceIsDetour = bestDetour;
            NavAgent.isStopped = false;
            return true;
        }

        void LeaveRetreatForAdvance()
        {
            if (!retreatHasDestination && !retreatInProgress && !defensiveMoving) return;
            retreatHasDestination = false;
            retreatDestinationIsCover = false;
            defensiveMoving = false;
            ResetRetreatRecovery();
            if (CanMoveLayerAgent()) NavAgent.ResetPath();
            ClearAdvanceRoute(true);
        }

        void BeginIndependentReload()
        {
            if (IsReloading) return;
            IsReloading = true;
            ReleaseIndependentFireAnimation();
            SetIndependentAim(false);
            reloadEndsAt = Time.time + Mathf.Max(0.1f, reloadSeconds);
            holdReloadBaseAnimation = false;
            if (Animator != null && Animator.isActiveAndEnabled && Animator.runtimeAnimatorController != null)
            {
                if (!string.IsNullOrEmpty(reloadAnimationTrigger))
                    foreach (var parameter in Animator.parameters)
                        if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == reloadAnimationTrigger)
                        { Animator.SetTrigger(reloadAnimationTrigger); holdReloadBaseAnimation = reloadAnimationLayer == 0; break; }
                if (!string.IsNullOrEmpty(reloadAnimationState) && reloadAnimationLayer < Animator.layerCount &&
                    Animator.HasState(reloadAnimationLayer, Animator.StringToHash(reloadAnimationState)))
                {
                    if (reloadAnimationLayer > 0)
                    {
                        savedReloadLayerWeight = Animator.GetLayerWeight(reloadAnimationLayer);
                        reloadLayerWeightOwned = true;
                        Animator.SetLayerWeight(reloadAnimationLayer, 1f);
                    }
                    Animator.CrossFadeInFixedTime(reloadAnimationState, 0.1f, reloadAnimationLayer);
                    holdReloadBaseAnimation = reloadAnimationLayer == 0;
                }
            }
            if (shootingAudio != null && reloadSound != null) shootingAudio.PlayOneShot(reloadSound);
            if (debugLayerShots) Debug.Log($"[Layer Shooting] {name}: Reloading ({reloadSeconds:F1}s)", this);
            onReloadStarted?.Invoke();
        }

        void RestoreIndependentReloadLayer()
        {
            if (reloadLayerWeightOwned && Animator != null && reloadAnimationLayer > 0 && reloadAnimationLayer < Animator.layerCount)
                Animator.SetLayerWeight(reloadAnimationLayer, savedReloadLayerWeight);
            reloadLayerWeightOwned = false;
        }

        void UpdateIndependentReload()
        {
            if (IsReloading && Time.time >= reloadEndsAt)
            {
                RoundsRemaining = Mathf.Max(1, magazineSize);
                IsReloading = false;
                RestoreIndependentReloadLayer();
                holdReloadBaseAnimation = false;
                pursuitAnimationInitialized = false;
                nextLayerShot = Time.time;
                if (debugLayerShots) Debug.Log($"[Layer Shooting] {name}: Reload complete ({RoundsRemaining} rounds)", this);
                onReloadFinished?.Invoke();
            }
            else if (!IsReloading && RoundsRemaining <= 0 && Time.time >= nextLayerShot) BeginIndependentReload();
        }

        float TacticalAdvanceDistance => Mathf.Max(retreatStartDistance + 2f, retreatDesiredDistance + 2f, advanceWhenBeyond);
        float TacticalHoldDistance => Mathf.Clamp(retreatDesiredDistance, retreatStartDistance + 0.5f, TacticalAdvanceDistance - 0.5f);

        Transform KnownMonsterForPursuit()
        {
            if (!pursueAssignedMonsterWhenUnseen) return null;
            Transform candidate = presentMonster != null ? presentMonster : priorityMonsterRoot != null ? priorityMonsterRoot : defensiveThreat;
            if (candidate == null || !candidate.gameObject.activeInHierarchy) return null;
            var fighter = candidate.GetComponentInParent<FighterCore>();
            return fighter != null && fighter.Damagable != null && fighter.IsDead ? null : candidate;
        }

        bool ShouldRetreatFrom(Collider target)
        {
            if (target == null) return false;
            if (retreatFromMonster && (ResolvePriorityMonster(target.transform) != null ||
                target.GetComponentsInParent<MonoBehaviour>(true).Any(b => b != null && b.GetType().Name == "BossEnemyAI")))
                return true;
            return retreatFromOtherTargets;
        }

        void RememberDefensiveThreat(Collider target, Vector3 aimPoint)
        {
            Transform actor = ResolvePriorityMonster(target.transform);
            if (actor == null) actor = authoredLayerTargetRoot;
            if (actor == null)
            {
                // Use the boss component if present without an assembly dependency.
                var bossComponent = target.GetComponentsInParent<MonoBehaviour>(true)
                    .FirstOrDefault(b => b != null && b.GetType().Name == "BossEnemyAI");
                actor = bossComponent != null ? bossComponent.transform : ResolvePriorityActor(target);
            }
            if (actor == null) actor = target.attachedRigidbody != null ? target.attachedRigidbody.transform : target.transform;
            if (defensiveThreat != actor)
            {
                ResetRetreatRecovery();
                retreatHasDestination = false;
                nextRetreatPlan = 0f;
            }
            defensiveThreat = actor;
            defensiveThreatPosition = actor.position;
            defensiveThreatAim = aimPoint;
            defensiveThreatSeenAt = Time.time;
        }

        bool HasDefensiveThreat()
        {
            return defensiveThreat != null && defensiveThreat.gameObject.activeInHierarchy &&
                (Time.time - defensiveThreatSeenAt <= retreatThreatMemory ||
                 (retreatHasDestination && Time.time - defensiveThreatSeenAt <= retreatThreatMemory + 12f));
        }

        void ClearDefensiveThreat()
        {
            defensiveThreat = null;
            defensiveThreatSeenAt = float.NegativeInfinity;
            retreatHasDestination = false;
            retreatDestinationIsCover = false;
            defensiveMoving = false;
            nextRetreatPlan = 0f;
            ResetRetreatRecovery();
        }

        void ResetRetreatRecovery()
        {
            failedRetreatPoints.Clear();
            retreatFailures = 0;
            retreatInProgress = false;
            retreatBestRemaining = float.PositiveInfinity;
            retreatPendingSince = -1f;
            if (NavAgent != null && retreatAvoidancePriority >= 0)
                NavAgent.avoidancePriority = retreatAvoidancePriority;
            retreatAvoidancePriority = -1;
        }

        void RememberFailedRetreatPoint(Vector3 point)
        {
            failedRetreatPoints.RemoveAll(p => p.expires <= Time.time);
            failedRetreatPoints.Add(new FailedRetreatPoint
            {
                position = point,
                expires = Time.time + Mathf.Max(1f, retreatFailedPointMemory)
            });
            if (failedRetreatPoints.Count > 32) failedRetreatPoints.RemoveAt(0);
        }

        bool IsFailedRetreatPoint(Vector3 point)
        {
            foreach (var failure in failedRetreatPoints)
                if (failure.expires > Time.time && RetreatFlatDistance(point, failure.position) < retreatFailedPointRadius)
                    return true;
            return false;
        }

        bool TryRecoverRetreatAgent()
        {
            if (!independentPursuitOwnsAgent || NavAgent == null || !NavAgent.isActiveAndEnabled) return false;
            if (NavAgent.isOnNavMesh) return true;
            if (!recoverOffNavMesh || Time.time < nextAgentRecovery) return false;
            nextAgentRecovery = Time.time + 0.75f;
            var filter = new NavMeshQueryFilter { agentTypeID = NavAgent.agentTypeID, areaMask = NavAgent.areaMask };
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit sample,
                Mathf.Max(0.1f, retreatNavMeshRecoveryRadius), filter)) return false;
            // A small resync only, and never across a solid wall.
            Vector3 offset = Vector3.up * Mathf.Max(0.25f, NavAgent.radius);
            Vector3 delta = sample.position - transform.position;
            if (delta.sqrMagnitude > 0.0001f)
                foreach (RaycastHit hit in Physics.RaycastAll(transform.position + offset, delta.normalized,
                    delta.magnitude, retreatCoverLayers, QueryTriggerInteraction.Ignore))
                    if (IsRetreatCoverCollider(hit.collider)) return false;
            if (!NavAgent.Warp(sample.position) || !NavAgent.isOnNavMesh) return false;
            retreatHasDestination = false;
            nextRetreatPlan = 0f;
            if (debugLayerShots) Debug.Log($"[Layer Shooting] {name}: recovered nearby NavMesh", this);
            return true;
        }

        void FailRetreatRoute(string reason)
        {
            if (retreatHasDestination) RememberFailedRetreatPoint(retreatDestination);
            retreatHasDestination = false;
            retreatFailures = Mathf.Min(12, retreatFailures + 1);
            retreatPendingSince = -1f;
            retreatBestRemaining = float.PositiveInfinity;
            nextRetreatPlan = 0f;
            StopLayerPursuit();
            // Break repeated local-avoidance deadlocks between soldiers. Restore the
            // original priority once the retreat ends or the threat changes.
            if (CanMoveLayerAgent())
            {
                if (retreatAvoidancePriority < 0) retreatAvoidancePriority = NavAgent.avoidancePriority;
                NavAgent.avoidancePriority = Mathf.Clamp(15 + (int)(Math.Abs((long)GetInstanceID() + 17L * retreatFailures) % 65L), 0, 99);
            }
            if (debugLayerShots) Debug.Log($"[Layer Shooting] {name}: retreat recovery #{retreatFailures}: {reason}", this);
        }

        static float RetreatFlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        bool IsRetreatCoverCollider(Collider c)
        {
            if (c == null || c.isTrigger || IsOwnCollider(c) || IsLayerTarget(c)) return false;
            if (defensiveThreat != null && (c.transform == defensiveThreat || c.transform.IsChildOf(defensiveThreat))) return false;
            if (c.GetComponentInParent<NavMeshAgent>() != null || c.GetComponentInParent<FighterCore>() != null) return false;
            return (retreatCoverLayers.value & (1 << c.gameObject.layer)) != 0;
        }

        bool IsShelteredFromThreat(Vector3 position)
        {
            Vector3 eye = position + Vector3.up * Mathf.Max(0.7f, LayerEyePosition.y - transform.position.y);
            Vector3 delta = defensiveThreatAim - eye;
            if (delta.sqrMagnitude < 0.01f) return false;
            // Ignore actors and triggers. Only real solid cover counts.
            foreach (RaycastHit hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude,
                retreatCoverLayers, QueryTriggerInteraction.Ignore))
                if (IsRetreatCoverCollider(hit.collider)) return true;
            return false;
        }

        bool RetreatRouteIsSafe(NavMeshPath path, float currentDistance, int fallbackLevel,
            out float length, out float closestDistance)
        {
            length = 0f;
            closestDistance = float.PositiveInfinity;
            Vector3[] corners = path.corners;
            if (corners.Length < 2) return false;
            // Normal routes move away. Emergency routes may briefly approach the
            // threat to get around a wall, but never deliberately enter its immediate reach.
            float minimum = fallbackLevel == 0 ? Mathf.Max(0f, Mathf.Min(currentDistance - 0.65f, retreatStartDistance * 0.7f)) :
                Mathf.Max(0f, Mathf.Min(currentDistance - 0.3f, Mathf.Max(2f, NavAgent.radius * 3f)));
            Vector3 threat = defensiveThreatPosition;
            threat.y = 0f;
            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
                Vector3 a = corners[i - 1], b = corners[i];
                a.y = b.y = 0f;
                Vector3 segment = b - a;
                float t = segment.sqrMagnitude > 0.001f ? Mathf.Clamp01(Vector3.Dot(threat - a, segment) / segment.sqrMagnitude) : 0f;
                closestDistance = Mathf.Min(closestDistance, Vector3.Distance(a + segment * t, threat));
                if (closestDistance < minimum) return false;
            }
            return true;
        }

        void ConsiderRetreatPoint(Vector3 raw, float currentDistance, int fallbackLevel, ref float bestScore,
            ref NavMeshPath bestPath, ref Vector3 bestPoint, ref bool bestCover)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = NavAgent.agentTypeID, areaMask = NavAgent.areaMask };
            if (!NavMesh.SamplePosition(raw, out NavMeshHit sample, 1.5f, filter)) return;
            if (RetreatFlatDistance(transform.position, sample.position) < Mathf.Max(0.8f, retreatArrivalDistance + 0.25f)) return;
            if (IsFailedRetreatPoint(sample.position)) return;
            float distance = RetreatFlatDistance(sample.position, defensiveThreatPosition);
            bool covered = IsShelteredFromThreat(sample.position);
            // A short lateral move or a slightly closer sheltered point can free a
            // trapped soldier when no strictly outward route exists.
            float minimumGain = fallbackLevel == 0 ? 0.65f : fallbackLevel == 1 ? 0.1f : (covered ? -1.25f : -0.15f);
            if (distance < currentDistance + minimumGain) return;
            var path = new NavMeshPath();
            if (!NavAgent.CalculatePath(sample.position, path) || path.status != NavMeshPathStatus.PathComplete ||
                !RetreatRouteIsSafe(path, currentDistance, fallbackLevel, out float length, out float closest)) return;
            float desired = Mathf.Max(retreatStartDistance + 1f, retreatDesiredDistance);
            float score = Mathf.Min(distance, desired + 2f) * 1.3f - length * 0.45f;
            if (covered && distance >= Mathf.Min(desired, retreatStartDistance * 0.65f)) score += 12f;
            if (distance < retreatStartDistance) score -= (retreatStartDistance - distance) * 2f;
            score -= Mathf.Max(0f, currentDistance - closest) * 0.7f;
            if (score <= bestScore) return;
            bestScore = score;
            bestPath = path;
            bestPoint = sample.position;
            bestCover = covered;
        }

        bool PlanDefensiveRetreat(float currentDistance)
        {
            float score = float.NegativeInfinity;
            NavMeshPath bestPath = null;
            Vector3 point = transform.position;
            bool covered = false;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - defensiveThreatPosition, Vector3.up);
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            away.Normalize();
            float baseRadius = Mathf.Max(2f, retreatSearchRadius);
            float radius = Mathf.Min(Mathf.Max(baseRadius, retreatMaximumSearchRadius), baseRadius * (1f + 0.4f * retreatFailures));
            // First use the strict defensive policy, then widen to all directions and
            // accept short lateral escape routes if no outward route exists.
            for (int level = 0; level < 3 && bestPath == null; level++)
            {
                int maxAngle = level == 0 ? 90 : 180;
                float searchRadius = level == 0 ? radius : Mathf.Min(Mathf.Max(baseRadius, retreatMaximumSearchRadius), radius * 1.5f);
                // Short samples get out of corners; longer samples get around large walls.
                float[] rings = { 1.5f, 3f, searchRadius * 0.5f, searchRadius };
                foreach (float ring in rings)
                    for (int angle = -maxAngle; angle < maxAngle + (maxAngle == 90 ? 1 : 0); angle += 30)
                        ConsiderRetreatPoint(transform.position + Quaternion.Euler(0f, angle, 0f) * away * ring,
                            currentDistance, level, ref score, ref bestPath, ref point, ref covered);
                if (level != 0) continue;
                foreach (Collider c in Physics.OverlapSphere(transform.position, radius, retreatCoverLayers, QueryTriggerInteraction.Ignore)
                    .Where(IsRetreatCoverCollider).OrderBy(c => (c.ClosestPoint(transform.position) - transform.position).sqrMagnitude).Take(16))
                {
                    Bounds b = c.bounds;
                    if (b.max.y < transform.position.y + 0.65f || b.size.y < 0.55f) continue;
                    Vector3 fromThreat = Vector3.ProjectOnPlane(b.center - defensiveThreatPosition, Vector3.up);
                    if (fromThreat.sqrMagnitude < 0.01f) continue;
                    fromThreat.Normalize();
                    float extent = Mathf.Abs(fromThreat.x) * b.extents.x + Mathf.Abs(fromThreat.z) * b.extents.z;
                    Vector3 behind = b.center + fromThreat * (extent + Mathf.Max(retreatCoverClearance, NavAgent.radius));
                    behind.y = transform.position.y;
                    if (RetreatFlatDistance(behind, transform.position) > radius + 2f) continue;
                    Vector3 side = Vector3.Cross(Vector3.up, fromThreat);
                    for (int i = -1; i <= 1; i++)
                        ConsiderRetreatPoint(behind + side * i * 1.25f, currentDistance, level, ref score, ref bestPath, ref point, ref covered);
                }
            }
            if (bestPath == null) return false;
            NavAgent.updatePosition = true;
            NavAgent.updateRotation = false;
            NavAgent.speed = Mathf.Max(0.1f, retreatSpeed);
            NavAgent.autoBraking = true;
            NavAgent.stoppingDistance = Mathf.Max(0.1f, retreatArrivalDistance * 0.5f);
            NavAgent.isStopped = false;
            if (!NavAgent.SetPath(bestPath)) { RememberFailedRetreatPoint(point); return false; }
            retreatDestination = point;
            retreatPlanThreatPosition = defensiveThreatPosition;
            retreatPlanDistance = RetreatFlatDistance(point, defensiveThreatPosition);
            retreatDestinationIsCover = covered;
            retreatHasDestination = true;
            retreatInProgress = true;
            retreatProgressAt = Time.time;
            retreatPhysicalProgressAt = Time.time;
            retreatProgressPosition = transform.position;
            retreatBestRemaining = float.PositiveInfinity;
            retreatPendingSince = -1f;
            if (debugLayerShots)
                Debug.Log($"[Layer Shooting] {name}: {(covered ? "Retreating to cover" : "Retreating away from monster")} at {point}", this);
            return true;
        }

        void UpdateDefensiveMovement(bool threatVisible)
        {
            if (!pursueLayerTargets || !TryRecoverRetreatAgent() || !CanMoveLayerAgent())
            {
                StopLayerPursuit();
                SetLayerStatus("Defensive movement needs an enabled, owned NavMeshAgent on a baked NavMesh");
                return;
            }
            if (advancingRoute)
            {
                ClearAdvanceRoute(true);
                NavAgent.ResetPath();
            }
            advancingToThreat = false;
            float distance = RetreatFlatDistance(transform.position, defensiveThreatPosition);
            bool arrived = retreatHasDestination && Vector3.Distance(transform.position, retreatDestination) <= Mathf.Max(retreatArrivalDistance, 0.2f);
            if (retreatHasDestination && !NavAgent.pathPending && NavAgent.hasPath &&
                NavAgent.remainingDistance <= retreatArrivalDistance &&
                Vector3.Distance(transform.position, retreatDestination) <= retreatArrivalDistance + Mathf.Abs(NavAgent.baseOffset) + 0.25f)
                arrived = true;
            if (retreatHasDestination && !arrived)
            {
                if (NavAgent.pathPending)
                {
                    if (retreatPendingSince < 0f) retreatPendingSince = Time.time;
                    if (Time.time - retreatPendingSince >= retreatPathPendingTimeout)
                        FailRetreatRoute("path calculation timed out");
                }
                else
                {
                    retreatPendingSince = -1f;
                    if (!NavAgent.hasPath || NavAgent.pathStatus != NavMeshPathStatus.PathComplete)
                        FailRetreatRoute("path lost or incomplete");
                    else
                    {
                        float remaining = NavAgent.remainingDistance;
                        if (!float.IsInfinity(remaining) && !float.IsNaN(remaining) &&
                            remaining < retreatBestRemaining - retreatMinimumProgress)
                        {
                            retreatBestRemaining = remaining;
                            retreatProgressAt = Time.time;
                        }
                        if (Vector3.Distance(transform.position, retreatProgressPosition) >= retreatMinimumProgress)
                        {
                            retreatProgressPosition = transform.position;
                            retreatPhysicalProgressAt = Time.time;
                        }
                        if (Time.time - retreatProgressAt >= retreatStuckSeconds ||
                            Time.time - retreatPhysicalProgressAt >= retreatStuckSeconds)
                            FailRetreatRoute("no physical or path progress along committed route");
                    }
                }
                if (retreatHasDestination)
                {
                    // Do not discard a valid short escape simply because it begins
                    // inside the danger radius, or replan every time the boss moves.
                    bool threatMoved = RetreatFlatDistance(retreatPlanThreatPosition, defensiveThreatPosition) > 2.5f;
                    float destinationDistance = RetreatFlatDistance(retreatDestination, defensiveThreatPosition);
                    bool compromised = threatMoved && destinationDistance < retreatPlanDistance - 2.5f &&
                        destinationDistance < distance - 0.75f;
                    if (compromised && Time.time >= nextRetreatPlan)
                        FailRetreatRoute("boss cut off escape route");
                    else
                    {
                        NavAgent.updatePosition = true;
                        NavAgent.updateRotation = false;
                        NavAgent.speed = Mathf.Max(0.1f, retreatSpeed);
                        NavAgent.isStopped = false;
                        defensiveMoving = true;
                        SetLayerStatus(retreatDestinationIsCover ? "Falling back to cover" : "Backpedaling from monster");
                        return;
                    }
                }
            }
            if (arrived)
            {
                retreatHasDestination = false;
                retreatFailures = 0;
                nextRetreatPlan = 0f;
                if (NavAgent != null && retreatAvoidancePriority >= 0)
                    NavAgent.avoidancePriority = retreatAvoidancePriority;
                retreatAvoidancePriority = -1;
            }
            // Hysteresis: once retreating, keep escaping until reaching desired
            // separation or useful cover, not just crossing the initial threshold.
            bool sheltered = IsShelteredFromThreat(transform.position);
            bool safe = distance >= retreatStartDistance && (sheltered || !retreatInProgress ||
                distance >= Mathf.Max(retreatStartDistance, retreatDesiredDistance));
            if (safe)
            {
                retreatHasDestination = false;
                ResetRetreatRecovery();
                StopLayerPursuit();
                SetLayerStatus(sheltered ? "Holding cover" : "Holding defensive distance");
                return;
            }
            retreatInProgress = true;
            if (Time.time < nextRetreatPlan) return;
            nextRetreatPlan = Time.time + Mathf.Max(0.2f, retreatFailures > 0 ? retreatEmergencyRetryInterval : retreatReplanInterval);
            retreatHasDestination = false;
            defensiveMoving = PlanDefensiveRetreat(distance);
            if (!defensiveMoving)
            {
                retreatFailures = Mathf.Min(12, retreatFailures + 1);
                StopLayerPursuit();
                SetLayerStatus("No reachable escape yet; expanding search and retrying");
                return;
            }
            SetLayerStatus(retreatDestinationIsCover ? "Retreating to cover" : "No cover found; retreating to open space");
        }

        Vector3 LayerGroundTarget(Collider target)
        {
            Transform actor = authoredMonsterPriority ? ResolvePriorityActor(target) : authoredLayerTargetRoot;
            Vector3 p = actor != null ? actor.position : target.bounds.center;
            // Keep the target's horizontal coordinates, but use the bottom of its collider
            // as the initial height for NavMesh projection.
            p.y = target.bounds.min.y;
            return p;
        }

        void SetLayerStatus(string status)
        {
            LayerShootingStatus = status;
            if (debugLayerShots && Time.time >= nextLayerStatusLog)
            {
                nextLayerStatusLog = Time.time + 1f;
                Debug.Log($"[Layer Shooting] {name}: {status}", this);
            }
        }

        string FindAnimatorParameter(string configured, AnimatorControllerParameterType type, params string[] candidates)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return null;
            var parameters = Animator.parameters;
            if (!string.IsNullOrEmpty(configured))
            {
                foreach (var parameter in parameters)
                    if (parameter.type == type && parameter.name == configured) return parameter.name;
                Debug.LogWarning($"[Layer Shooting] Animator parameter '{configured}' not found or has wrong type.", this);
                return null;
            }
            foreach (string candidate in candidates)
                foreach (var parameter in parameters)
                    if (parameter.type == type && string.Equals(parameter.name, candidate, StringComparison.OrdinalIgnoreCase))
                        return parameter.name;
            return null;
        }

        void CachePursuitAnimatorParameters()
        {
            resolvedSpeedParameter = FindAnimatorParameter(pursuitSpeedParameter, AnimatorControllerParameterType.Float,
                "moveAmount", "moveSpeed", "speed", "inputMagnitude", "movementSpeed");
            resolvedMovingParameter = FindAnimatorParameter(pursuitMovingParameter, AnimatorControllerParameterType.Bool,
                "isMoving", "moving");
            fsMove = FindAnimatorParameter(null, AnimatorControllerParameterType.Float, "moveAmount");
            fsStrafe = FindAnimatorParameter(null, AnimatorControllerParameterType.Float, "strafeAmount");
            fsLocomotion = FindAnimatorParameter(null, AnimatorControllerParameterType.Float, "locomotionType");
            fsRotation = FindAnimatorParameter(null, AnimatorControllerParameterType.Float, "rotation");
            fsBlendActive = useFantacodeBlendTree && fsMove != null && fsStrafe != null && fsLocomotion != null;
            fsLocomotionHash = 0;
            if (fsBlendActive && Animator.layerCount > 0)
            {
                int hash = Animator.StringToHash(Animator.GetLayerName(0) + ".Locomotion");
                if (Animator.HasState(0, hash)) fsLocomotionHash = hash;
            }
            if (fsBlendActive && debugLayerShots)
                Debug.Log("[Layer Shooting] Fantacode directional locomotion blend tree detected.", this);
            pursuitAnimationInitialized = false;
        }

        bool CanAnimateIndependentFire => Animator != null && Animator.isActiveAndEnabled &&
            Animator.runtimeAnimatorController != null;

        void SetIndependentAim(bool aiming)
        {
            independentAiming = aiming;
            if (CanAnimateIndependentFire && !string.IsNullOrEmpty(resolvedAimBool))
                Animator.SetBool(resolvedAimBool, aiming);
        }

        bool TryResolveFireState(int layer, string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            string layerName = Animator.GetLayerName(layer);
            string[] names = { state, layerName + "." + state };
            foreach (string candidate in names)
            {
                int hash = Animator.StringToHash(candidate);
                if (!Animator.HasState(layer, hash)) continue;
                resolvedFireLayer = layer;
                resolvedFireHash = hash;
                return true;
            }
#if UNITY_EDITOR
            // Find nested state machines in the actual controller, including override controllers.
            RuntimeAnimatorController runtime = Animator.runtimeAnimatorController;
            while (runtime is AnimatorOverrideController overrides)
                runtime = overrides.runtimeAnimatorController;
            var controller = runtime as UnityEditor.Animations.AnimatorController;
            if (controller != null && layer < controller.layers.Length)
            {
                string path = FindNestedFireState(controller.layers[layer].stateMachine, layerName, state);
                if (!string.IsNullOrEmpty(path))
                {
                    int hash = Animator.StringToHash(path);
                    if (Animator.HasState(layer, hash))
                    {
                        resolvedFireLayer = layer;
                        resolvedFireHash = hash;
                        return true;
                    }
                }
            }
#endif
            return false;
        }

#if UNITY_EDITOR
        static string FindNestedFireState(UnityEditor.Animations.AnimatorStateMachine machine, string prefix, string requested)
        {
            foreach (var child in machine.states)
            {
                string path = prefix + "." + child.state.name;
                if (string.Equals(child.state.name, requested, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, requested, StringComparison.OrdinalIgnoreCase)) return path;
            }
            foreach (var child in machine.stateMachines)
            {
                string match = FindNestedFireState(child.stateMachine, prefix + "." + child.stateMachine.name, requested);
                if (match != null) return match;
            }
            return null;
        }
#endif

        Animator ResolveIndependentAnimator()
        {
            if (shootingAnimatorOverride != null) return shootingAnimatorOverride;
            var animators = GetComponentsInChildren<Animator>(true);
            // Prefer the Animator actually carrying the generated weapon layer.
            var weapon = animators.FirstOrDefault(a => a.isActiveAndEnabled &&
                a.runtimeAnimatorController != null && a.GetLayerIndex(GeneratedWeaponLayerName) > 0);
            if (weapon != null) return weapon;
            if (Animator != null && Animator.isActiveAndEnabled && Animator.runtimeAnimatorController != null)
                return Animator;
            return animators.FirstOrDefault(a => a.isActiveAndEnabled && a.runtimeAnimatorController != null);
        }

        void RefreshIndependentAnimatorBinding()
        {
            // Fantacode or an equip script may replace the runtime controller after Start.
            // Rebuild cached layer/state/parameter references when that happens.
            var desired = Animator;
            if (shootingAnimatorOverride != null) desired = shootingAnimatorOverride;
            else if ((!CanAnimateIndependentFire || Animator.GetLayerIndex(GeneratedWeaponLayerName) <= 0) &&
                Time.unscaledTime >= nextWeaponAnimatorSearch)
            {
                nextWeaponAnimatorSearch = Time.unscaledTime + 0.5f;
                desired = ResolveIndependentAnimator();
            }
            var controller = desired != null ? desired.runtimeAnimatorController : null;
            if (desired == Animator && controller == cachedWeaponController) return;
            ReleaseIndependentFireAnimation();
            RestoreIndependentReloadLayer();
            Animator = desired;
            if (Animator != null && stopOriginalAIController && disableRootMotionForPursuit)
                Animator.applyRootMotion = false;
            CachePursuitAnimatorParameters();
            CacheIndependentFireAnimation();
            if (IsReloading && CanAnimateIndependentFire && generatedWeaponLayer > 0 &&
                Animator.HasState(generatedWeaponLayer, Animator.StringToHash(GeneratedWeaponLayerName + ".Reload")))
            {
                Animator.SetLayerWeight(generatedWeaponLayer, 1f);
                float elapsed = Mathf.Max(0f, reloadSeconds - (reloadEndsAt - Time.time));
                Animator.CrossFadeInFixedTime(Animator.StringToHash(GeneratedWeaponLayerName + ".Reload"),
                    0.05f, generatedWeaponLayer, elapsed);
            }
        }

        void ApplyGeneratedWeaponWeight()
        {
            if (!CanAnimateIndependentFire || generatedWeaponLayer <= 0 || generatedWeaponLayer >= Animator.layerCount) return;
            bool alive = Damagable == null || Damagable.CurrentHealth > 0;
            bool active = alive && (independentAiming || fireAnimationActive ||
                (IsReloading && reloadAnimationLayer == generatedWeaponLayer));
            Animator.SetLayerWeight(generatedWeaponLayer, active ? 1f : 0f);
        }

        void LogWeaponAnimation()
        {
            if (!debugWeaponAnimation || !CanAnimateIndependentFire ||
                generatedWeaponLayer <= 0 || generatedWeaponLayer >= Animator.layerCount ||
                !(independentAiming || fireAnimationActive || IsReloading) || Time.time < nextWeaponAnimationDebug) return;
            nextWeaponAnimationDebug = Time.time + 1f;
            var state = Animator.GetCurrentAnimatorStateInfo(generatedWeaponLayer);
            var clips = Animator.GetCurrentAnimatorClipInfo(generatedWeaponLayer);
            string clipNames = string.Join(", ", clips.Select(c => c.clip != null ? c.clip.name : "None"));
            string stateName = state.IsName(GeneratedWeaponLayerName + ".Aim") ? "Aim" :
                state.IsName(GeneratedWeaponLayerName + ".Shoot") ? "Shoot" :
                state.IsName(GeneratedWeaponLayerName + ".Reload") ? "Reload" : state.fullPathHash.ToString();
            Debug.Log($"[Weapon Animation] {name}: Animator={Animator.gameObject.name}, " +
                $"Controller={Animator.runtimeAnimatorController.name}, Layer={generatedWeaponLayer}, " +
                $"Weight={Animator.GetLayerWeight(generatedWeaponLayer):F2}, State={stateName}, " +
                $"Time={state.normalizedTime:F2}, Clips={clipNames}, AnimatorSpeed={Animator.speed:F2}, " +
                $"InTransition={Animator.IsInTransition(generatedWeaponLayer)}, Aiming={independentAiming}, Reloading={IsReloading}", Animator);
        }

        void CacheIndependentFireAnimation()
        {
            ReleaseIndependentFireAnimation();
            resolvedFireLayer = -1;
            resolvedFireHash = 0;
            resolvedFireTrigger = resolvedAimBool = null;
            generatedWeaponLayer = -1;
            cachedWeaponController = Animator != null ? Animator.runtimeAnimatorController : null;
            if (!CanAnimateIndependentFire)
            {
                Debug.LogWarning("[Layer Shooting] No active Animator with a controller. Assign Shooting Animator Override.", this);
                return;
            }
            resolvedAimBool = FindAnimatorParameter(shootingAimBool, AnimatorControllerParameterType.Bool,
                "IsAiming", "isAiming");
            generatedWeaponLayer = Animator.GetLayerIndex(GeneratedWeaponLayerName);
            if (debugWeaponAnimation)
            {
                Debug.Log($"[Weapon Animation] {name}: using Animator '{Animator.gameObject.name}', controller '{cachedWeaponController.name}', weapon layer index {generatedWeaponLayer}.", Animator);
                if (generatedWeaponLayer <= 0)
                    Debug.LogWarning("[Weapon Animation] Independent Weapon layer is missing on the actual runtime Animator. Assign the generated controller to the correct Animator and set Shooting Animator Override if needed. If the controller changed after Start, check the equip/animation override script.", Animator);
            }
            // The optional editor tool creates these exact states. Prefer them even when
            // old serialized settings still contain an invalid generic name/layer.
            if (generatedWeaponLayer > 0)
            {
                string firePath = GeneratedWeaponLayerName + ".Shoot";
                string reloadPath = GeneratedWeaponLayerName + ".Reload";
                if (Animator.HasState(generatedWeaponLayer, Animator.StringToHash(firePath)))
                {
                    independentFireLayer = generatedWeaponLayer;
                    independentFireState = firePath;
                    shootingAnimationTrigger = "";
                }
                if (Animator.HasState(generatedWeaponLayer, Animator.StringToHash(reloadPath)))
                {
                    reloadAnimationLayer = generatedWeaponLayer;
                    reloadAnimationState = reloadPath;
                    reloadAnimationTrigger = "";
                }
            }
            resolvedFireTrigger = FindAnimatorParameter(shootingAnimationTrigger, AnimatorControllerParameterType.Trigger,
                "Shoot", "Fire", "Shooting", "FireWeapon", "ShootWeapon");
            // Search upper-body layers first; a full state path can disambiguate duplicate names.
            string[] candidates = !string.IsNullOrWhiteSpace(independentFireState)
                ? new[] { independentFireState.Trim() }
                : new[] { "Shoot", "Shooting", "Fire", "Firing", "Rifle Shoot", "Rifle Fire", "Shoot Rifle", "FireWeapon", "ShootWeapon", "GunShoot", "GunFire" };
            int first = independentFireLayer >= 0 ? independentFireLayer : Animator.layerCount - 1;
            int last = independentFireLayer >= 0 ? independentFireLayer : 0;
            for (int layer = first; layer >= last && resolvedFireHash == 0; layer--)
            {
                if (layer >= Animator.layerCount) continue;
                foreach (string candidate in candidates)
                    if (TryResolveFireState(layer, candidate)) break;
            }
            if (resolvedFireHash == 0 && !string.IsNullOrWhiteSpace(independentFireState))
                Debug.LogWarning($"[Layer Shooting] Fire state '{independentFireState}' not found on the selected Animator layer(s). Check its exact path and layer.", this);
            if (resolvedFireHash == 0 && resolvedFireTrigger == null)
                Debug.LogWarning("[Layer Shooting] No shooting state/trigger in this Animator. IsAiming alone cannot play recoil. Add a weapon animation layer or assign a valid existing state. Shots will still fire.", this);
            else if (debugLayerShots)
                Debug.Log($"[Layer Shooting] {name}: fire animation layer={resolvedFireLayer}, state hash={resolvedFireHash}, trigger={resolvedFireTrigger ?? "none"}", this);
            // Trigger-driven controllers need an explicit layer if they do not use base layer.
            if (resolvedFireLayer < 0 && resolvedFireTrigger != null)
                resolvedFireLayer = independentFireLayer >= 0 && independentFireLayer < Animator.layerCount ? independentFireLayer : 0;
        }

        void PlayIndependentFireAnimation()
        {
            if (!CanAnimateIndependentFire || IsReloading || resolvedFireLayer < 0 ||
                resolvedFireLayer >= Animator.layerCount) return;
            if (resolvedFireHash == 0 && resolvedFireTrigger == null) return;
            if (!fireAnimationActive && resolvedFireLayer > 0)
            {
                savedFireLayerWeight = Animator.GetLayerWeight(resolvedFireLayer);
                fireLayerWeightOwned = true;
            }
            if (resolvedFireLayer > 0) Animator.SetLayerWeight(resolvedFireLayer, 1f);
            fireAnimationActive = true;
            fireAnimationUntil = Time.time + Mathf.Max(fireAnimationHoldSeconds, secondsBetweenShots + 0.03f);
            // Explicit trigger is used for authored transitions if no explicit state is selected.
            if (resolvedFireTrigger != null && (resolvedFireHash == 0 ||
                (!string.IsNullOrWhiteSpace(shootingAnimationTrigger) && string.IsNullOrWhiteSpace(independentFireState))))
            {
                Animator.SetTrigger(resolvedFireTrigger);
                return;
            }
            var state = Animator.GetCurrentAnimatorStateInfo(resolvedFireLayer);
            bool inState = state.fullPathHash == resolvedFireHash || state.shortNameHash == resolvedFireHash;
            bool entering = false;
            if (Animator.IsInTransition(resolvedFireLayer))
            {
                var next = Animator.GetNextAnimatorStateInfo(resolvedFireLayer);
                entering = next.fullPathHash == resolvedFireHash || next.shortNameHash == resolvedFireHash;
            }
            if (!entering && (restartFireAnimationEachShot || !inState || (!state.loop && state.normalizedTime >= 1f)))
                Animator.CrossFadeInFixedTime(resolvedFireHash, fireAnimationBlend, resolvedFireLayer, 0f);
        }

        void ReleaseIndependentFireAnimation()
        {
            if (fireLayerWeightOwned && Animator != null && resolvedFireLayer > 0 && resolvedFireLayer < Animator.layerCount)
                Animator.SetLayerWeight(resolvedFireLayer, savedFireLayerWeight);
            if (fireAnimationActive)
            {
                if (resolvedFireLayer == 0) pursuitAnimationInitialized = false;
                if (CanAnimateIndependentFire && !string.IsNullOrEmpty(resolvedFireTrigger))
                    Animator.ResetTrigger(resolvedFireTrigger);
            }
            fireAnimationActive = false;
            fireLayerWeightOwned = false;
            fireAnimationUntil = float.NegativeInfinity;
        }

        void UpdatePursuitAnimation()
        {
            if (Animator == null || !Animator.isActiveAndEnabled || Animator.runtimeAnimatorController == null) return;
            // Apply the directional blend once per frame in LateUpdate. Never replace
            // a Fantacode blend tree with a nonexistent generic Idle/Run state.
            if (fsBlendActive) return;
            float velocity = CanMoveLayerAgent() ? NavAgent.velocity.magnitude : 0f;
            bool moving = velocity > 0.1f;
            if (!string.IsNullOrEmpty(resolvedSpeedParameter))
                Animator.SetFloat(resolvedSpeedParameter, Mathf.Clamp01(velocity / Mathf.Max(0.1f, defensiveMoving ? retreatSpeed : pursuitSpeed)), 0.12f, Time.deltaTime);
            if (!string.IsNullOrEmpty(resolvedMovingParameter)) Animator.SetBool(resolvedMovingParameter, moving);
            if (IsReloading && holdReloadBaseAnimation) return;
            if (fireAnimationActive && resolvedFireLayer == 0) return;
            if (!pursuitAnimationInitialized || moving != pursuitWasMoving || defensiveMoving != pursuitWasDefensive)
            {
                string state = moving ? (defensiveMoving && !string.IsNullOrEmpty(retreatMoveState) ? retreatMoveState : pursuitMoveState) : pursuitIdleState;
                if (!string.IsNullOrEmpty(state) && Animator.HasState(0, Animator.StringToHash(state)))
                    Animator.CrossFadeInFixedTime(state, 0.12f, 0);
                pursuitWasMoving = moving;
                pursuitWasDefensive = defensiveMoving;
                pursuitAnimationInitialized = true;
            }
        }

        private void LateUpdate()
        {
            if (CheckBossAttackHold())
            {
                ApplyGeneratedWeaponWeight();
                return;
            }
            if (!layerOnlyShooting || !layerShootingInitialized || !CanAnimateIndependentFire ||
                (Damagable != null && Damagable.CurrentHealth <= 0)) return;
            if (!string.IsNullOrEmpty(resolvedAimBool)) Animator.SetBool(resolvedAimBool, independentAiming);

            // Reassert after other Update/animation callbacks. The early Update call
            // also supplies the correct weight before the current animation evaluation.
            ApplyGeneratedWeaponWeight();
            LogWeaponAnimation();
            if (!fsBlendActive) return;
            Vector3 world = CanMoveLayerAgent() ? NavAgent.velocity : Vector3.zero;
            world.y = 0f;
            Vector3 local = transform.InverseTransformDirection(world);
            float speed = Mathf.Max(0.1f, defensiveMoving ? retreatSpeed : pursuitSpeed);
            float scale = Mathf.Max(0.1f, fantacodeRunBlend);
            // Strafe while facing a threat; negative local Z selects Walk Back / Run Back.
            bool strafe = independentAiming || defensiveMoving || LayerShootingTarget != null ||
                local.z < -0.1f || Mathf.Abs(local.x) > Mathf.Abs(local.z) * 0.5f;
            float forward = strafe ? Mathf.Clamp(local.z / speed, -1f, 1f) : Mathf.Clamp01(world.magnitude / speed);
            float side = strafe ? Mathf.Clamp(local.x / speed, -1f, 1f) : 0f;
            Animator.SetFloat(fsLocomotion, strafe ? fantacodeStrafeType : fantacodeFreeType);
            Animator.SetFloat(fsMove, forward * scale, fantacodeBlendDamping, Time.deltaTime);
            Animator.SetFloat(fsStrafe, side * scale, fantacodeBlendDamping, Time.deltaTime);
            if (!string.IsNullOrEmpty(fsRotation)) Animator.SetFloat(fsRotation, 0f);
            if ((IsReloading && holdReloadBaseAnimation) || (fireAnimationActive && resolvedFireLayer == 0)) return;
            if (fsLocomotionHash != 0)
            {
                var state = Animator.GetCurrentAnimatorStateInfo(0);
                bool entering = Animator.IsInTransition(0) && Animator.GetNextAnimatorStateInfo(0).fullPathHash == fsLocomotionHash;
                if (state.fullPathHash != fsLocomotionHash && !entering)
                    Animator.CrossFadeInFixedTime(fsLocomotionHash, 0.12f, 0);
            }
        }

        void UpdateLayerShooting()
        {
            if (fireAnimationActive && Time.time >= fireAnimationUntil)
                ReleaseIndependentFireAnimation();
            // Clear aim evidence at the start of each update. Hidden targets are not tracked.
            hasVisibleWeaponAim = false;
            SetIndependentAim(false);
            if (Damagable != null && Damagable.CurrentHealth <= 0)
            {
                LayerShootingTarget = null;
                ReleaseIndependentFireAnimation();
                RestoreIndependentReloadLayer();
                if (CanAnimateIndependentFire && generatedWeaponLayer > 0 && generatedWeaponLayer < Animator.layerCount)
                    Animator.SetLayerWeight(generatedWeaponLayer, 0f);
                StopLayerPursuit();
                return;
            }

            RefreshMonsterPresence();
            UpdateIndependentReload();
            if (Time.time >= nextShootingScan)
            {
                nextShootingScan = Time.time + Mathf.Max(0.02f, shootingScanInterval);
                LayerShootingTarget = FindLayerShootingTarget();
            }

            if (!CanSeeLayerTarget(LayerShootingTarget, out Vector3 aimPoint))
            {
                LayerShootingTarget = null;
                // Complete a committed escape before re-engaging. Hidden cover is a
                // brief pause, not an indefinite defensive hold.
                if (HasDefensiveThreat() && retreatHasDestination &&
                    RetreatFlatDistance(transform.position, retreatDestination) > retreatArrivalDistance)
                {
                    UpdateDefensiveMovement(false);
                    Vector3 towardThreat = Vector3.ProjectOnPlane(defensiveThreatAim - transform.position, Vector3.up);
                    if (turnBodyToTarget && towardThreat.sqrMagnitude > 0.001f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(towardThreat), shootingTurnSpeed * Time.deltaTime);
                    UpdatePursuitAnimation();
                    return;
                }
                if (HasDefensiveThreat() && coverHoldUntil < 0f)
                    coverHoldUntil = Time.time + Mathf.Max(0f, holdCoverBeforeReengage);
                if (Time.time < coverHoldUntil)
                {
                    LeaveRetreatForAdvance();
                    StopLayerPursuit();
                    SetLayerStatus(IsReloading ? "Reloading in cover" : "Briefly holding cover");
                    UpdatePursuitAnimation();
                    return;
                }
                LeaveRetreatForAdvance();
                Transform known = KnownMonsterForPursuit();
                bool hasClue = known != null || Time.time - lastLayerSeenTime <= Mathf.Max(pursueLastSeenSeconds, retreatThreatMemory);
                Vector3 goal = known != null ? known.position : lastLayerSeenPosition;
                if (hasClue && Vector3.Distance(transform.position, goal) > 1f && MoveLayerAgent(goal, 0.5f))
                {
                    SetLayerStatus(known != null ? "Re-engaging monster" : "Moving to last visible target position");
                    Vector3 movement = NavAgent.desiredVelocity;
                    movement.y = 0f;
                    if (turnBodyToTarget && movement.sqrMagnitude > 0.001f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(movement), shootingTurnSpeed * Time.deltaTime);
                }
                else
                {
                    if (!advancingRoute) StopLayerPursuit();
                    SetLayerStatus("Searching for a clear route or visible target");
                }
                UpdatePursuitAnimation();
                return;
            }

            visibleWeaponAimPoint = aimPoint;
            hasVisibleWeaponAim = true;
            coverHoldUntil = -1f;
            lastLayerSeenPosition = LayerGroundTarget(LayerShootingTarget);
            lastLayerSeenTime = Time.time;
            bool currentTargetIsThreat = ShouldRetreatFrom(LayerShootingTarget);
            if (currentTargetIsThreat)
                RememberDefensiveThreat(LayerShootingTarget, aimPoint);
            else if (!HasDefensiveThreat() && defensiveThreatSeenAt != float.NegativeInfinity)
            {
                ClearDefensiveThreat();
                StopLayerPursuit();
            }
            Vector3 flat = aimPoint - transform.position;
            flat.y = 0f;
            if (turnBodyToTarget && flat.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(flat, Vector3.up), shootingTurnSpeed * Time.deltaTime);

            // Never rotate a gun separately from the animated hands when the
            // native weapon reference can be aligned through the upper body.
            if (shootingAimPivot != null && !(trackWeaponTarget && HasLiveWeaponReference))
            {
                Vector3 aim = aimPoint - shootingAimPivot.position;
                if (aim.sqrMagnitude > 0.0001f)
                    shootingAimPivot.rotation = Quaternion.RotateTowards(shootingAimPivot.rotation,
                        Quaternion.LookRotation(aim.normalized, Vector3.up), shootingTurnSpeed * Time.deltaTime);
            }

            Vector3 muzzle = LayerMuzzlePosition;
            Vector3 shotDirection = aimPoint - muzzle;
            bool clearShot = TryLayerRay(muzzle, aimPoint, out RaycastHit hit) &&
                SameLayerActor(hit.collider, LayerShootingTarget);
            float fireDistance = Mathf.Min(shootingRange, Mathf.Max(0.1f, maximumFireDistance));
            SetIndependentAim(!IsReloading && RoundsRemaining > 0 && shotDirection.magnitude <= fireDistance);
            float preferred = Mathf.Clamp(preferredShootingDistance, 0.1f, fireDistance);
            bool approach = flat.magnitude > preferred + 0.5f;
            // If the eyes see the target but the muzzle is blocked, close the distance.
            bool reposition = !clearShot && flat.magnitude > 2f;
            if (currentTargetIsThreat && HasDefensiveThreat())
            {
                float distance = RetreatFlatDistance(transform.position, defensiveThreatPosition);
                bool danger = distance < retreatStartDistance;
                bool shouldAdvance = distance > TacticalAdvanceDistance ||
                    (shotDirection.magnitude > fireDistance && distance > retreatStartDistance + 1f) ||
                    (advancingToThreat && distance > TacticalHoldDistance + 0.5f) ||
                    (reposition && distance > Mathf.Max(retreatStartDistance + 1f, 2f));
                // Stop retreating once the boss is far enough; do not remain in
                // defensive mode forever. Reloading soldiers still move to safety.
                if (danger || (retreatHasDestination && distance < TacticalAdvanceDistance))
                {
                    UpdateDefensiveMovement(true);
                }
                else if (shouldAdvance)
                {
                    LeaveRetreatForAdvance();
                    advancingToThreat = true;
                    if (MoveLayerAgent(lastLayerSeenPosition, reposition ? Mathf.Max(1f, retreatStartDistance + 0.5f) : Mathf.Max(retreatStartDistance + 0.5f, Mathf.Min(TacticalHoldDistance, fireDistance - 1f))))
                        SetLayerStatus(reposition ? "Seeking firing angle" : "Advancing toward monster");
                }
                else
                {
                    LeaveRetreatForAdvance();
                    advancingToThreat = false;
                    StopLayerPursuit();
                    SetLayerStatus(IsReloading ? "Reloading at combat distance" : "Holding firing distance");
                }
            }
            else if (pursueLayerTargets && (approach || reposition))
            {
                LeaveRetreatForAdvance();
                if (MoveLayerAgent(lastLayerSeenPosition, reposition ? 1.5f : preferred))
                    SetLayerStatus(reposition ? "Approaching for a clear muzzle shot" : "Pursuing target");
                else if (!CanMoveLayerAgent()) SetLayerStatus("Cannot pursue: NavMeshAgent not on NavMesh or not owned");
            }
            else { LeaveRetreatForAdvance(); StopLayerPursuit(); }

            UpdatePursuitAnimation();

            if (shotDirection.sqrMagnitude < 0.000001f || shotDirection.magnitude > fireDistance)
            {
                SetLayerStatus("Target visible but outside firing distance");
                return;
            }
            if (turnBodyToTarget && flat.sqrMagnitude > 0.0001f &&
                Vector3.Angle(transform.forward, flat) > Mathf.Max(1f, shootingAimTolerance))
            {
                SetLayerStatus("Turning toward target");
                return;
            }
            if (shootingAimPivot != null && !(trackWeaponTarget && HasLiveWeaponReference) &&
                Vector3.Angle(shootingAimPivot.forward, shotDirection) > Mathf.Max(1f, shootingAimTolerance))
            {
                SetLayerStatus("Waiting for aim pivot alignment");
                return;
            }
            if (!clearShot)
            {
                SetLayerStatus("Muzzle ray blocked by " + (hit.collider != null ? hit.collider.name : "no target hit"));
                return;
            }
            SetIndependentAim(!IsReloading && RoundsRemaining > 0);
            if (IsReloading || RoundsRemaining <= 0 || Time.time < nextLayerShot) return;
            if (!IsWeaponFacingTarget(aimPoint))
            {
                SetLayerStatus("Waiting for actual weapon alignment");
                return;
            }

            // Recheck scene presence immediately before a non-monster shot. This
            // catches a newly spawned boss even between normal target scans.
            if (ResolvePriorityMonster(LayerShootingTarget.transform) == null)
            {
                RefreshMonsterPresence(true);
                if (presentMonster != null || LayerShootingTarget == null) return;
            }
            if (!IsAllowedByMonsterPresence(hit.collider)) return;
            nextLayerShot = Time.time + Mathf.Max(0.01f, secondsBetweenShots);
            SetLayerStatus("Firing at " + hit.collider.name);
            RoundsRemaining--;
            FireLayerShot(muzzle, shotDirection.normalized, hit);
            // Let the last shot's recoil appear before the reload animation starts.

        }

        void DrawLayerShotTracer(Vector3 origin, Vector3 end, bool forceVisible = false)
        {
            if (!showShotTracer && !forceVisible) return;
            Material material = shotTracerMaterial;
            if (material == null)
            {
                if (generatedTracerMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
                    if (shader == null || !shader.isSupported) return;
                    generatedTracerMaterial = new Material(shader);
                    if (generatedTracerMaterial.HasProperty("_BaseColor"))
                        generatedTracerMaterial.SetColor("_BaseColor", Color.yellow);
                    if (generatedTracerMaterial.HasProperty("_Color"))
                        generatedTracerMaterial.SetColor("_Color", Color.yellow);
                }
                material = generatedTracerMaterial;
            }
            var tracer = new GameObject("Layer Shot Tracer");
            var line = tracer.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, origin);
            line.SetPosition(1, end);
            line.startWidth = shotTracerWidth;
            line.endWidth = shotTracerWidth * 0.5f;
            line.startColor = Color.yellow;
            line.endColor = Color.yellow;
            Destroy(tracer, Mathf.Max(0.01f, shotTracerLifetime));
        }

        private void OnDestroy()
        {
            if (generatedTracerMaterial != null) Destroy(generatedTracerMaterial);
        }

        bool IsCustomBossHit(Collider hitCollider)
        {
            // No direct reference to the custom boss class: Fantacode may be in a separate assembly.
            for (Transform t = hitCollider != null ? hitCollider.transform : null; t != null; t = t.parent)
                if (t.GetComponent("BossEnemyAI") != null) return true;
            return false;
        }

        void FireLayerShot(Vector3 origin, Vector3 direction, RaycastHit hit)
        {
            bool cosmeticBossShot = nonPhysicalBossShots && IsCustomBossHit(hit.collider);
            // Discovery is handled by the boss. No native hit or stagger is requested here.
            if (hit.collider != null)
                hit.collider.SendMessageUpwards("NotifyShotAt", transform, SendMessageOptions.DontRequireReceiver);
            if (shootingProjectilePrefab == null || cosmeticBossShot)
                DrawLayerShotTracer(origin, hit.point, cosmeticBossShot && shootingProjectilePrefab != null);
            if (shootingProjectilePrefab != null && !cosmeticBossShot)
            {
                GameObject bullet = Instantiate(shootingProjectilePrefab, origin, Quaternion.LookRotation(direction));
                Rigidbody body = bullet.GetComponent<Rigidbody>();
                // Avoid immediately colliding with the shooter's own body or weapon.
                foreach (Collider bulletCollider in bullet.GetComponentsInChildren<Collider>())
                    foreach (Collider ownCollider in GetComponentsInChildren<Collider>())
                        if (bulletCollider != ownCollider)
                            Physics.IgnoreCollision(bulletCollider, ownCollider);
                body.linearVelocity = direction * shootingProjectileSpeed;
                Destroy(bullet, shootingProjectileLifetime);
            }
            else
            {
                // Hitscan already hit the selected collider. No health/damage API involved.
                if (shootingImpactPrefab != null && !cosmeticBossShot)
                {
                    GameObject impact = Instantiate(shootingImpactPrefab, hit.point,
                        Quaternion.LookRotation(hit.normal));
                    Destroy(impact, shootingImpactLifetime);
                }
                // Preserve the existing hitscan event contract. Do not invent a hitscan
                // damage event for shots that originally used cosmetic projectiles.
                if (shootingProjectilePrefab == null) OnLayerHitscanHit?.Invoke(hit);
            }

            if (shootingMuzzleFlash != null) shootingMuzzleFlash.Play(true);
            if (shootingAudio != null && shootingSound != null) shootingAudio.PlayOneShot(shootingSound);
            PlayIndependentFireAnimation();
            onLayerShot?.Invoke();
            if (debugLayerShots)
            {
                Debug.DrawLine(origin, hit.point, Color.red, 0.25f);
                Debug.Log($"[Layer Shooting] {name} fired at {hit.collider.name}", this);
            }
        }

        void UpdateTargetTracking()
        {
            if (Fighter.Target == null || !useLastSeenPosition) return;

            // Stealth AI handles this via StealthAIController — skip
            if (AIController.Blackboard.Has(AIBlackboardKeys.HasVisualOnTarget)) return;

            if (AIController.TargetsInVision.Contains(Fighter.Target.transform))
                AIController.LastSeenTargetPos = Fighter.Target.transform.position;
        }

        void FindTargetFromVision()
        {
            foreach (var target in AIController.TargetsInVision)
            {
                if (AIController.TargetHandler.TrySetTarget(target))
                    return;
            }
        }

        public void AlertNearbyAllies()
        {
            var colliders = Physics.OverlapBox(transform.position, new Vector3(AlertRange / 2f, 5f, AlertRange / 2f),
                Quaternion.identity, 1 << gameObject.layer);

            foreach (var collider in colliders)
            {
                if (collider.gameObject == gameObject) continue;

                var nearbyAlly = collider.GetComponent<CombatAIController>();
                if (nearbyAlly != null && !nearbyAlly.layerOnlyShooting && nearbyAlly.Fighter != null && nearbyAlly.Fighter.Target == null && AIUtil.HasLineOfSight(nearbyAlly.transform, transform.position + Vector3.up * 1.7f))
                {
                    StartCoroutine(AsyncUtil.RunAfterDelay(0.5f, () => nearbyAlly.Fighter.Target = Fighter.Target));
                }
            }
        }

        public Vector3 GetTargetPosition()
        {
            if (layerOnlyShooting)
                return LayerShootingTarget != null ? LayerShootingTarget.bounds.center : transform.position;
            if (Fighter == null || Fighter.Target == null)
                return transform.position;

            if (useLastSeenPosition
                && AIController.Blackboard.TryGet<bool>(AIBlackboardKeys.HasVisualOnTarget, out bool hasVisual)
                && !hasVisual)
            {
                return AIController.LastSeenTargetPos;
            }

            return Fighter.Target.transform.position;
        }

        public bool IsOutsideMovementRange()
        {
            if (hasMovementRange)
            {
                var vecToInitialPos = transform.position - InitialPosition;
                vecToInitialPos.y = 0; // Ignore height difference
                return vecToInitialPos.magnitude > movementRange;
            }

            return false;
        }

        public float DistanceToTarget { get; private set; }

        void OnCalcTimerTick()
        {
            if (Fighter.Target == null) return;

            CheckIfTargetIsUnreachable();
            CalculateDistanceToTarget();
        }

        void CheckIfTargetIsUnreachable()
        {
            if (aiCombatType == AICombatType.Melee && backupRangedWeapon != null)
            {
                if (Fighter.Targetable.IsUnreachableByGround)
                {
                    // switch to ranged
                    if (Fighter.CurrentWeapon != backupRangedWeapon)
                    {
                        // ItemEquipper.EquipItem(backupRangedWeapon, true);
                        // Fighter.UpdatePreferredFightingRange(backupRangedWeaponPreferredRange);
                        AIController.Blackboard.Set(AIBlackboardKeys.IsTargetUnreachableByGround, true);
                    }
                }
                else
                {
                    // switch back to melee
                    if (Fighter.CurrentWeapon != Fighter.defaultWeapon)
                    {
                        // ItemEquipper.EquipItem(Fighter.defaultWeapon, true);
                        // Fighter.ResetPreferredFigtingRangeToDefault();
                        AIController.Blackboard.Set(AIBlackboardKeys.IsTargetUnreachableByGround, false);
                    }
                }
            }
        }

        void CalculateDistanceToTarget()
        {
            DistanceToTarget = useNavMeshDistance
                ? NavMeshUtil.GetNavMeshDistance(transform.position, Fighter.Target.transform.position)
                : Vector3.Distance(transform.position, Fighter.Target.transform.position);

            AIController.Blackboard.Set<float>(CombatCoreBlackboardKeys.DistanceToTarget, DistanceToTarget);

            bool wasInRange = AIController.Blackboard.TryGet(CombatCoreBlackboardKeys.IsTargetInPreferredRange, out bool prev) && prev;
            bool inRange = wasInRange
                ? DistanceToTarget <= Fighter.PreferredFightingRange + preferredRangeBuffer
                : DistanceToTarget <= Fighter.PreferredFightingRange;
            AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsTargetInPreferredRange, inRange);

            AIController.Blackboard.Set(CombatCoreBlackboardKeys.IsTargetReachable, DistanceToTarget - Fighter.PreferredFightingRange < GetMovementRange());
        }

        public static List<CombatAITag> combatAITags = new();
        public CombatAITag tagCost { get; set; } = new CombatAITag() { Tag = "", maxCost = 1 };

        public CombatAITag GetTagCost()
        {
            foreach (var combatTag in combatAITags)
            {
                if (combatTag.Tag == tagCost.Tag)
                {
                    return combatTag;
                }
            }
            return combatAITags.LastOrDefault(t => t.Tag == ""); // Return -1 if tag is not found
        }

        public Vector3 GetDetectionRayOrigin()
        {
            return transform.position + Vector3.up * detectionRayOriginOffset;
        }

        public Vector3 GetDetectionRayTarget(FighterCore target = null)
        {
            if (layerOnlyShooting && target == null) return GetTargetPosition();
            if (target == null && Fighter == null) return transform.position;
            if (target == null)
                target = Fighter.Target;

            if (target == null) return transform.position;
            return target.transform.position + Vector3.up * (target.animator.GetBool(AnimatorParameters.coverMode) ? detectionRayTargetInCoverOffset : detectionRayTargetOffset);
        }

        public void OnDeath()
        {
            if (layerOnlyShooting) return;
            if (standardCombatRegistered && CombatAIManager.i != null)
            {
                CombatAIManager.i.RemoveCombatAI(this);
                standardCombatRegistered = false;
            }

            if (DropWeaponOnDeath)
            {
                // Drop every weapon the AI carries — equipped or placed under a body holder — as a
                // proper, re-equippable object so the corpse isn't left holding them.
                ItemEquipper.DropAllItems();
            }
        }

        public void RequestAttack()
        {
            if (layerOnlyShooting || AIController == null) return;
            AIController.Blackboard.Set(CombatCoreBlackboardKeys.AttackRequested, true);
        }

        private void OnDisable()
        {
            ReleaseBossAttackHold();
            if (layerShootingInitialized)
            {
                ReleaseIndependentFireAnimation();
                RestoreIndependentReloadLayer();
                SetIndependentAim(false);
                if (CanAnimateIndependentFire && generatedWeaponLayer > 0 && generatedWeaponLayer < Animator.layerCount)
                    Animator.SetLayerWeight(generatedWeaponLayer, 0f);
                StopLayerPursuit();
                ClearDefensiveThreat();
            }
            LayerShootingTarget = null;
            if (standardCombatRegistered && CombatAIManager.i != null)
            {
                CombatAIManager.i.RemoveCombatAI(this);
                standardCombatRegistered = false;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Handles.color = Color.blue;
            if (hasMovementRange)
                Handles.DrawWireDisc(transform.position, transform.up, movementRange);

            Handles.color = Color.red;
            //Handles.DrawWireDisc(transform.position, transform.up, AlertRange);

            if (useLastSeenPosition && Application.isPlaying && AIController != null && AIController.LastSeenTargetPos != Vector3.zero)
            {
                //var pos = AIController.LastSeenTargetPos;
                //Gizmos.color = Color.yellow;
                //Gizmos.DrawWireSphere(pos + Vector3.up * 0.25f, 0.5f);
                //Gizmos.DrawLine(transform.position + Vector3.up, pos + Vector3.up * 0.25f);
                //Handles.color = Color.yellow;
                //Handles.Label(pos + Vector3.up * 1f, "Last Seen Pos");
            }
        }
#endif
    }

    [Serializable]
    public class CombatAITag
    {
        public string Tag;
        public float maxCost = 2;
        public float currentCost { get; set; } = 0;
    }
}