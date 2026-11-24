
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FS_Core;
using FS_StealthSystem;
using FS_LocomotionSystem;

[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(AIController))]
[RequireComponent(typeof(StealthAIController))]
[RequireComponent(typeof(AwarenessSystem))]
[RequireComponent(typeof(NavMeshAgent))]
public class BossEnemyAI : MonoBehaviour
{
    [Header("Animation States")]
    [SerializeField] private string idleState = "Idle";
    [SerializeField] private string runState = "run";
    [SerializeField] private string attackState = "Attack";
    [SerializeField] private string detectedState = "Detected";
    [SerializeField] private string suspiciousState = "Suspicious";
    [SerializeField] private float animationBlend = 0.12f;
    [SerializeField] private float suspiciousFallbackDuration = 1.5f;
    [SerializeField] private float movementThreshold = 0.08f;

    [Header("Player")]
    [SerializeField] private Transform player;

    [Header("Enemy Priority / Gunfire")]
    [SerializeField] private LayerMask enemyTargetLayers;
    [SerializeField, Min(0.1f)] private float playerPriorityDistance = 3f;
    [SerializeField, Min(0.1f)] private float enemyWalkSpeed = 2.7f;
    [SerializeField] private string enemyWalkState = "Walk";
    // Live soldier pursuit always uses zero NavMesh stopping distance.
    // Keep a separate arrival tolerance only for an unseen last-known clue.
    [UnityEngine.Serialization.FormerlySerializedAs("enemyStoppingDistance")]
    [SerializeField, Min(0f)] private float enemyClueArrivalDistance = 1.8f;
    private const float EnemyContactTolerance = 0.05f;
    [Header("Enemy Attack State")]
    [SerializeField] private string enemyAttackAnimationState = "Jump Attack";
    [SerializeField, Min(0.1f)] private float enemyAttackDistance = 2.5f;
    [SerializeField, Min(0.1f)] private float enemyAttackMaxHeightDifference = 1.5f;
    [Tooltip("Reach the soldier at this zero-based frame of the Jump Attack clip. Uses the clip's imported sample rate and actual Animator playback time.")]
    [SerializeField, Min(1)] private int enemyAttackArrivalFrame = 44;
    [Tooltip("Horizontal landing offset in meters relative to the soldier: X = right, Z = forward. Y is ignored to preserve the boss height.")]
    [SerializeField] private Vector3 enemyAttackPositionOffset = Vector3.zero;
    [Header("Enemy Attack Impact")]
    [SerializeField] private bool ragdollEnemyOnImpact = true;
    [Tooltip("Zero-based Jump Attack frame at which the ragdoll and visible dismemberment begin; independent of landing frame.")]
    [SerializeField, Min(0)] private int enemyAttackImpactFrame = 36;
    [Tooltip("Start preparing hidden asset cuts this many meters before attack range. Only one pursuit target is prewarmed per attack cycle.")]
    [SerializeField, Min(0f)] private float enemyAttackPrewarmLeadDistance = 20f;
    [Tooltip("Abort a stalled attack if its Animator makes no progress for this many unscaled seconds.")]
    [SerializeField, Min(0.25f)] private float enemyAttackAnimationStallSeconds = 1.5f;
    private float enemyAttackLastProgressAt;
    private float enemyAttackLastNormalizedTime;
    private float enemyAttackRetryAt;
    private Transform enemyAttackPrewarmTarget;
    [Tooltip("Horizontal outward launch speed in meters per second for each detached part.")]
    [SerializeField, Min(0f)] private float enemyFragmentSeparationVelocity = 4.5f;
    [SerializeField, Min(0f)] private float enemyFragmentUpwardVelocity = 2.2f;
    [Tooltip("Random angular velocity change in radians per second.")]
    [SerializeField, Min(0f)] private float enemyFragmentSpin = 5f;
    [Tooltip("Corpse physics layer. Default 0 = Default. Must collide with the floor and be excluded from enemy scans. Legacy value 2 is migrated at impact.")]
    [SerializeField, Range(0, 31)] private int enemyCorpseLayer = 0;
    private bool enemyAttackLandingLogged;
    private bool enemyAttackImpactTriggered;
    private bool enemyAttackImpactSucceeded;
    private Transform enemyAttackTarget;
    private Vector3 enemyAttackAnchor;
    private Vector3 enemyAttackStartPosition;
    private Vector3 enemyAttackEndPosition;
    private int enemyAttackStateHash;
    private AnimationClip enemyAttackMotionClip;
    private bool enemyAttackMotionActive;
    private bool enemyAttackStateEntered;
    private float enemyAttackMotionProgress;
    private readonly List<AnimatorClipInfo> enemyAttackClipBuffer = new List<AnimatorClipInfo>();
    public bool IsInEnemyAttackState => mode == BossMode.EnemyAttack && enemyAttackTarget != null;
    public Transform EnemyAttackTarget => enemyAttackTarget;
    [Header("Enemy Detection Memory")]
    [SerializeField, Min(0.05f)] private float enemyScanInterval = 0.2f;
    [SerializeField, Min(0.1f)] private float enemyMemorySeconds = 7f;
    [SerializeField, Min(0.1f)] private float gunfireMemorySeconds = 10f;
    [Header("Enemy Pursuit Anti-Stuck")]
    [SerializeField, Min(0.5f)] private float enemyStuckSeconds = 2f;
    [SerializeField, Min(0.05f)] private float enemyMinimumProgress = 0.25f;
    [SerializeField, Min(0.2f)] private float enemyPathPendingTimeout = 1.5f;
    [SerializeField, Min(0.5f)] private float enemyRecoverySearchRadius = 12f;
    [SerializeField, Min(1f)] private float enemyFailedPointMemory = 10f;
    [SerializeField, Min(0.1f)] private float enemyNavMeshRecoveryRadius = 1.5f;
    [SerializeField] private bool debugEnemyRecovery = true;
    private Vector3 enemyRouteEnd, enemyProgressPosition;
    private float enemyProgressAt, enemyPendingSince = -1f, nextEnemyNavRecovery;
    private bool enemyRouteIsDetour;
    private int enemyRouteFailures;
    private int enemyOriginalAvoidance = -1;
    private readonly List<Vector3> enemyFailedPoints = new List<Vector3>();
    private readonly List<float> enemyFailedExpires = new List<float>();
    private float patrolProgressAt = -1f;
    private Vector3 patrolProgressPosition;
    private float patrolPendingAt = -1f;
    private Transform visibleEnemy, rememberedEnemy, firingEnemy;
    private Vector3 rememberedEnemyPosition, firingEnemyPosition;
    private float enemySeenAt = float.NegativeInfinity;
    private float enemyShotAt = float.NegativeInfinity;
    private bool gunfireClueCommitted;
    [Tooltip("After a gunfire clue has been used, do not immediately rediscover it from another bullet.")]
    [SerializeField, Min(0.1f)] private float gunfireRediscoveryCooldown = 3f;
    [SerializeField, Min(0.1f)] private float enemyClueLookSeconds = 0.8f;
    [SerializeField] private bool debugGunfireDiscovery = false;
    private float nextGunfireDiscoveryAt = float.NegativeInfinity;
    private float nextIgnoredGunfireLog;
    private float enemyClueLookUntil = -1f;
    private float nextEnemyScan, nextEnemyPath;
    private Vector3 lastEnemyGoal;
    private Transform lastEnemyPathTarget;
    private bool hasEnemyGoal;
    private bool enemyOwnsMovement, enemyAiWasEnabled, enemyStealthWasEnabled;
    private StealthAIController enemyStealthController;
    private bool walkAfterCancelledDetection;
    private Vector3 detectedAnchor;
    private bool hasDetectedAnchor;
    private bool detectionLostSight;
    private bool playerRedetectionPending;
    private bool playerDetectionReadyForCharge;
    private int detectedStartedFrame;
    private float detectedLastProgressAt;
    private float detectedLastNormalized;
    private Vector3 detectedAnimatorLocalPosition;
    private Quaternion detectedFacing;
    public Transform PlayerCollisionRoot => player;
    public bool ConsumeCompletedPlayerDetection()
    {
        bool ready = playerDetectionReadyForCharge;
        playerDetectionReadyForCharge = false;
        return ready;
    }
    // Read evaluated animation progress, not an unrelated windup timer.
    public static bool TryGetDetectionProgress(Animator animator, int hash, out float progress)
    {
        progress = 0f;
        if (animator == null || !animator.isActiveAndEnabled || !animator.HasState(0, hash)) return false;
        var current = animator.GetCurrentAnimatorStateInfo(0);
        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            // During a replay, the incoming state is the new animation instance.
            if (next.shortNameHash == hash || next.fullPathHash == hash)
            { progress = next.normalizedTime; return true; }
        }
        if (current.shortNameHash == hash || current.fullPathHash == hash)
        { progress = current.normalizedTime; return true; }
        return false;
    }
    private int enemyWalkHash;
    private Transform priorityEnemy;
    private Transform lastReportedPriorityEnemy;
    private Vector3 priorityEnemyPosition;
    public bool EnemyTakesPriority => priorityEnemy != null;

    [Header("Vision")]
    [SerializeField] private float visionDistance = 25f;
    [SerializeField, Range(1f, 360f)]
    private float visionAngle = 140f;
    [SerializeField] private float eyeHeight = 1.7f;
    [SerializeField] private float targetHeight = 1f;
    [SerializeField] private LayerMask visionBlockingLayers = ~0;

    [Header("Hearing")]
    [SerializeField] private float sprintHearingRange = 100f;
    [SerializeField] private float runningHearingRange = 100f;
    [SerializeField] private float walkingHearingRange = 25f;
    [SerializeField] private float crouchingHearingRange = 8f;
    [SerializeField] private float minimumMovementSpeed = 0.05f;
    [Tooltip("Crouching is silent by default. Enable if crouched footsteps should still be audible at close range.")]
    [SerializeField] private bool hearCrouchedFootsteps = false;
    [Header("Reliable Footstep Detection")]
    [Tooltip("Minimum horizontal speed for footsteps. Checks both Fantacode velocity and actual transform movement.")]
    [SerializeField, Min(0.02f)] private float minimumAudibleFootstepSpeed = 0.12f;
    [Tooltip("Optional: walls attenuate footstep hearing. Off by default to restore the normal hearing ranges.")]
    [SerializeField] private bool muffleFootstepsThroughWalls = false;
    [Tooltip("Enable to see once-per-second hearing diagnostics, including why a sound was rejected.")]
    [SerializeField] private bool debugFootstepHearing = false;
    private bool hasPreviousFootstepPosition;
    private Vector3 previousFootstepPosition;
    private float previousFootstepSampleTime;
    private float nextFootstepDebugTime;
    [Header("Realistic Sound Occlusion")]
    [Tooltip("Which solid obstacles muffle movement sounds. Match your wall/cover layers.")]
    [SerializeField] private LayerMask hearingOcclusionLayers = ~0;
    [Tooltip("A wall reduces the effective hearing range to this fraction. A nearby loud sound can still be heard.")]
    [SerializeField, Range(0.01f, 1f)] private float obstructedHearingMultiplier = 0.75f;
    [SerializeField] private float movementCheckInterval = 0.05f;
    [SerializeField] private float movementInvestigationInterval = 0.5f;
    [SerializeField] private float minimumNewSoundDistance = 1.5f;

    [Header("Investigation")]
    [SerializeField] private float investigateSpeed = 4.5f;
    [SerializeField] private float investigateStoppingDistance = 1.1f;
    [SerializeField] private float investigateTimeout = 12f;
    [SerializeField] private float investigateLookDuration = 3.2f;
    [SerializeField] private float investigateLookTurnSpeed = 110f;
    [SerializeField] private float soundPositionSampleRadius = 5f;

    [Header("Detection and Chase")]
    [SerializeField] private float detectedDuration = 1.5f;
    [SerializeField] private float turnSpeed = 220f;
    [SerializeField] private float chaseSpeed = 5f;
    [SerializeField] private float chaseStoppingDistance = 1.5f;
    [SerializeField] private float chasePathRefreshInterval = 0.15f;
    [SerializeField] private float losePlayerAfter = 4f;

    [Header("Post-Stun Suspicion / Investigation")]
    [Tooltip("After a charge stun, face the dodge location and play Suspicious before returning to investigate.")]
    [SerializeField, Min(0.1f)] private float postStunSuspicionSeconds = 2.1f;
    [SerializeField, Min(15f)] private float postStunTurnSpeed = 200f;

    [Header("Post-Charge Recovery")]
    [SerializeField, Min(0f)]
    private float postChargeVisionCooldown = 2.5f;

    [SerializeField]
    private bool suppressHearingAfterCharge = true;

    [Header("Searching")]
    [SerializeField] private float searchDuration = 18f;
    [SerializeField] private float searchRadius = 9f;
    [SerializeField] private float maximumSearchRadius = 22f;
    [SerializeField] private int searchCandidates = 25;
    [SerializeField] private float searchPointTimeout = 16f;
    [SerializeField] private float searchPauseDuration = 0.4f;
    [SerializeField] private float searchArrivalDistance = 1.1f;
    [SerializeField] private float minimumPointSeparation = 3.0f;
    [SerializeField] private float blindSpotPriority = 2.5f;
    [SerializeField] private LayerMask searchObstructionLayers = ~0;

    [Header("Wall And Corner Investigation")]
    [SerializeField, Min(1)] private int minimumWallChecksBeforeBreakingCover = 4;
    [SerializeField, Min(1f)] private float wallInspectionRadius = 7f;
    [SerializeField, Min(0.5f)] private float wallFlankOffset = 2.5f;
    [SerializeField, Min(0f)] private float wallFlankPriority = 6f;
    [SerializeField, Min(0.5f)] private float wallCheckPauseSeconds = 2.5f;
    [SerializeField, Min(0f)] private float wallCheckExtraTime = 18f;
    private readonly List<Vector3> wallCheckPoints = new List<Vector3>();
    private int wallChecksCompleted;
    private int perimeterChecksPlanned;
    [SerializeField, Min(0.2f)] private float perimeterClearance = 1.0f;
    [SerializeField, Min(0.5f)] private float perimeterSearchRadius = 10f;
    [SerializeField, Min(0f)] private float cornerLookSeconds = 0.0f;
    private int searchRepathAttempts;
    private Vector3 investigationApproachPosition;
    [SerializeField, Min(0.1f)] private float wallScanPause = 0.3f;
    [SerializeField, Min(1)] private int searchMaxRepaths = 3;

    [Header("Intensive Investigation")]
    [SerializeField, Min(0.5f)] private float investigationArrivalTolerance = 1.2f;
    [SerializeField, Min(0.1f)] private float investigationProgressInterval = 2.5f;
    [SerializeField, Min(0.01f)] private float investigationMinimumMovement = 0.25f;
    [SerializeField, Min(1)] private int investigationMaxFailedPaths = 5;
    [SerializeField, Min(1f)] private float investigationMaximumTravelSeconds = 35f;
    [SerializeField, Min(0f)] private float investigationScanAngle = 55f;
    [SerializeField, Min(1f)] private float investigationScanSpeed = 95f;
    [SerializeField, Min(0f)] private float investigationScanHold = 0.18f;
    [SerializeField, Min(1)] private int minimumSearchStops = 5;
    [SerializeField, Min(0f)] private float nearbyCoverSearchRadius = 25f;
    [Header("Persistent Cover Clearing")]
    [Tooltip("The boss looks for all living Cover objects at least this far from the clue, even if the older radius was saved smaller on a prefab.")]
    [SerializeField, Min(1f)] private float minimumCoverClearRadius = 32f;
    [Tooltip("Cover is considered reachable for an attack when within Attack Distance plus this tolerance. This matches the NavMesh attack-position check.")]
    [SerializeField, Min(0f)] private float coverHitReachTolerance = 0.35f;
    [Tooltip("If still physically moving toward cover this won't reset its route. Only stagnant approach paths are retried.")]
    [SerializeField, Min(0.5f)] private float coverStuckCheckSeconds = 2.5f;
    [SerializeField, Min(0.05f)] private float coverMinimumProgress = 0.2f;
    [SerializeField, Range(1, 8)] private int coverStuckRetries = 4;
    [Tooltip("Allow extra time to clear remaining cover instead of exiting at the ordinary search timer.")]
    [SerializeField, Min(10f)] private float coverClearExtraTime = 140f;
    [SerializeField] private bool debugCoverSweep = false;
    private float coverProgressCheckAt;
    private Vector3 coverProgressPosition;
    private int coverStallAttempts;
    private int coversDestroyedThisSearch;
    private int finalCoverRechecks;

    [Tooltip("Give each unreachable or hard-to-approach cover more than one chance before skipping it.")]
    [SerializeField, Range(1, 10)] private int maximumCoverApproachAttempts = 6;
    [SerializeField, Min(0)] private int maximumCoverTargetsPerInvestigation = 60;
    [SerializeField, Min(1f)] private float investigationCoverTimeout = 32f;
    [SerializeField, Min(0f)] private float coverSearchPause = 0.04f;
    [Header("Stable Rotation & Deliberate Scanning")]
    [SerializeField, Range(60f, 360f)] private float movementTurnSpeed = 180f;
    [SerializeField, Min(0.1f)] private float minimumSteeringLookAhead = 0.85f;
    [SerializeField, Min(0.1f)] private float steeringDirectionSmoothness = 6f;
    [SerializeField, Range(0.1f, 12f)] private float rotationDeadZone = 2.5f;
    [SerializeField, Min(1f)] private float distanceBetweenFullScans = 4.0f;
    [SerializeField, Min(0f)] private float minimumSecondsBetweenScans = 3f;
    [SerializeField, Min(0.25f)] private float deliberateScanTimeout = 3.0f;
    [SerializeField, Min(0f)] private float coverAttackApproachMargin = 0.25f;
    private Vector3 smoothedMovementHeading;
    private Quaternion stableBodyRotation;
    private Quaternion requestedBodyRotation;
    private float requestedTurnSpeed;
    private bool hasRequestedBodyRotation;
    private bool stableBodyRotationInitialized;
    private Vector3 lastFullScanPosition;
    private float nextFullScanAllowed;
    private bool searchPauseHasScan;
    private Vector3 coverApproachPoint;
    private bool coverApproachValid;

    [Header("Ruthless Investigation / Cover Clearing")]
    [Tooltip("After checking the wall, systematically destroy every reachable Cover within range.")]
    [SerializeField] private bool clearAllNearbyCover = true;
    [SerializeField, Min(0f)] private float initialCoverSearchGrace = 0.7f;
    [SerializeField, Min(0f)] private float chainedCoverDelay = 0.03f;
    [SerializeField, Min(0.2f)] private float aggressiveCoverHitInterval = 0.55f;
    [Tooltip("Multiplies hit damage during lost-contact cover-clearing. Works with existing serialized Cover Damage.")]
    [SerializeField, Min(1f)] private float coverSearchDamageMultiplier = 1.5f;
    [SerializeField, Min(0.1f)] private float meaningfulHearingSpeed = 0.75f; // Legacy tuning; footstep hearing uses minimumAudibleFootstepSpeed.
    [SerializeField, Min(0.2f)] private float soundRetargetDistance = 12f;
    [SerializeField, Min(0.2f)] private float soundRetargetCooldown = 7f;
    [Tooltip("After clearing cover around a clue, investigate nearby corners briefly, then return to patrol.")]
    [SerializeField, Min(0f)] private float postCoverSweepLookSeconds = 8f;
    [Tooltip("The boss occasionally looks around while patrolling, without stopping every few steps.")]
    [SerializeField, Min(2f)] private float patrolScanInterval = 12f;
    [SerializeField, Min(0.2f)] private float patrolScanDuration = 1.5f;
    private bool patrolScanning;
    private float patrolScanEndTime;
    private float nextPatrolScanTime;
    private bool coverSweepCompleted;
    private float coverSweepFinishedAt;
    [SerializeField] private bool continuousWandering = true;
    [SerializeField, Min(0.05f)] private float movementAnimationGrace = 0.18f;
    private bool clearingNearbyCover;
    private float lastSoundRetargetTime = float.NegativeInfinity;
    private float lastActuallyMovingTime = float.NegativeInfinity;
    private float searchPointAllowedTime;

    [Header("Aggressive Cover Searching")]
    [SerializeField, Min(0)] private int wallChecksBeforeFirstCoverAttack = 1;
    [SerializeField, Min(1)] private int wallChecksBetweenCoverAttacks = 1;
    [SerializeField, Min(0f)] private float minimumCoverSearchDuration = 80f;
    // Search follows a strict sequence: finish the wall circuit BEFORE destroying cover.
    private enum InvestigationSearchPhase { Perimeter, CoverSweep, AreaSweep, FinalScan }
    private InvestigationSearchPhase investigationSearchPhase;

    [Header("Extended Area Search After Wall / Cover")]
    [Tooltip("The boss keeps searching at least this long while reachable viewpoints remain. A genuinely blocked NavMesh can end the search earlier.")]
    [SerializeField, Min(15f)] private float minimumInvestigationSearchSeconds = 85f;
    [Tooltip("Maximum total time for this one clue, excluding time spent reacting to new evidence.")]
    [SerializeField, Min(25f)] private float maximumInvestigationSearchSeconds = 190f;
    [Tooltip("Travel to this many wider viewpoints PER area pass; the boss searches more than one pass.")]
    [SerializeField, Range(6, 24)] private int extendedAreaViewpoints = 14;
    [SerializeField, Min(4f)] private float extendedAreaSearchRadius = 19f;
    [SerializeField, Range(1, 5)] private int minimumAreaSearchPasses = 2;
    [SerializeField, Range(1, 30)] private int minimumExtendedViewpointsVisited = 12;
    [SerializeField, Range(1, 6)] private int minimumCoverSweepPasses = 3;
    [Tooltip("Brief inspection at every fourth extended search point, without prolonged idle pauses.")]
    [SerializeField, Range(0f, 1.5f)] private float extendedWaypointLookSeconds = 0.65f;
    [SerializeField, Range(0.5f, 3f)] private float extendedWaypointArrival = 0.95f;
    [SerializeField, Range(4f, 30f)] private float extendedWaypointTimeout = 18f;
    [SerializeField, Range(2, 6)] private int maximumExtendedAreaPasses = 5;

    private readonly List<Vector3> extendedSearchPoints = new List<Vector3>();
    private readonly HashSet<Cover> deferredCoverThisPass = new HashSet<Cover>();
    private int extendedWaypointIndex;
    private int extendedPassesFinished;
    private int extendedStopsVisited;
    private int coverSweepPassesFinished;
    private bool extendedHasDestination;
    private bool extendedLooking;
    private float extendedLookUntil;
    private float extendedDestinationDeadline;
    private float extendedProgressAt;
    private Vector3 extendedProgressPosition;
    private int extendedRetries;
    private float coverTargetDeadline;
    private int perimeterWaypointIndex;
    private bool perimeterHasDestination;
    private int perimeterDestinationFailures;
    private float perimeterWaypointDeadline;
    private float perimeterProgressCheckAt;
    private Vector3 perimeterProgressPosition;
    private float finalScanDeadline;
    private bool originalAutoBraking;
    private Collider identifiedWall;

    [Header("Ordered Perimeter Investigation")]
    [Tooltip("Inspect the wall perimeter before destroying cover. Disable only to debug NavMesh route generation.")]
    [SerializeField] private bool circleWallBeforeCover = true;
    [SerializeField, Range(0.35f, 1.2f)] private float perimeterWaypointArrival = 0.75f;
    [SerializeField, Range(0.5f, 6f)] private float perimeterStuckCheck = 2.5f;
    [SerializeField, Min(0.05f)] private float perimeterMinimumProgress = 0.25f;
    [SerializeField, Range(1, 5)] private int perimeterMaximumRepaths = 3;
    [SerializeField, Min(0.2f)] private float finalInvestigationScanSeconds = 3.5f;
    [SerializeField, Min(1f)] private float retargetOnNewSoundDistance = 4.5f;
    private int nextCoverWallCheckMilestone;
    private float nextInvestigationCoverTime;

    [Header("Chase Obstacle Destruction")]
    [SerializeField] private bool destroyCoverDuringChase = true;
    [SerializeField, Min(0.2f)] private float chaseObstacleCheckInterval = 0.2f;
    [SerializeField, Min(0.5f)] private float chaseObstacleCheckDistance = 4f;
    [SerializeField, Min(0.1f)] private float chaseObstacleProbeRadius = 0.45f;
    [SerializeField, Min(0.5f)] private float chaseCoverAttackTimeout = 12f;
    [SerializeField, Min(0f)] private float chaseCoverRetryDelay = 1.25f;
    private float nextChaseObstacleCheck;
    private float chaseCoverStartTime;
    private float chaseCoverRetryUntil;
    private Cover chaseObstacleCover;
    private bool lostPlayerCoverSearch;

    [Header("Cover Hunting")]
    [SerializeField] private float attackDistance = 2f;
    [SerializeField] private float coverDamage = 35f;
    [SerializeField] private float coverSearchInterval = 1f;
    [SerializeField] private float coverPathRefreshInterval = 0.9f;
    [SerializeField] private float coverAttackRotationOffset = 0f;

    [Header("Wandering (no vents)")]
    [SerializeField, Min(2f)] private float wanderRadius = 18f;
    [SerializeField, Min(0.5f)] private float wanderSpeed = 2.7f;
    [SerializeField] private Vector2 wanderPauseSeconds = new Vector2(0.1f, 0.4f);
    [SerializeField] private Vector2 coverDecisionSeconds = new Vector2(30f, 60f);
    [SerializeField, Range(0f, 1f)] private float coverDestructionChance = 0.25f;
    [SerializeField, Min(0f)] private float randomCoverMaxDistance = 12f;

    [Header("Progressive visual suspicion")]
    [SerializeField, Min(0.01f)] private float suspicionBuildSeconds = 2.5f;
    [SerializeField, Min(0.01f)] private float suspicionFadeSeconds = 8f;
    [SerializeField, Range(0f, 1f)] private float investigateSuspicionThreshold = 0.22f;

    [Header("Melee Attack")]
    [SerializeField] private string meleeState = "MeleeAttack";
    [SerializeField, Min(0.1f)] private float meleeRange = 2.5f;
    [SerializeField, Min(0.1f)] private float meleeDuration = 1.1f;
    [SerializeField, Min(0f)] private float meleeCooldown = 2f;

    [Header("Debug")]
    [SerializeField] private bool logEvents = true;
    [SerializeField] private bool logMovementChecks = false;
    [SerializeField] private bool showGizmos = true;

    private enum BossMode
    {
        Cover,
        Suspicious,
        InvestigateTravel,
        InvestigateLook,
        Search,
        Detected,
        Chase,
        Melee,
        EnemyWalk,
        EnemyAttack
    }

    private BossMode mode = BossMode.Cover;

    private BossChargeAttack chargeAttack;
    private AIController ai;
    private AwarenessSystem awareness;
    private NavMeshAgent agent;
    private Animator bossAnimator;
    private LocomotionCore playerLocomotion;
    private Transform trackedPlayer;
    private VisualStimulus[] playerStimuli;
    private Cover currentCover;

    private Vector3 soundLocation;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 searchPoint;
    private Vector3 previousPosition;
    private Vector3 measuredVelocity;

    private readonly List<Vector3> visitedPoints =
        new List<Vector3>();

    private float originalAgentSpeed;
    private float originalStoppingDistance;
    private float nextVisionScan;
    private float nextCoverSearch;
    private float nextCoverPath;
    private float nextChasePath;
    private float nextMovementCheck;
    private float nextMovementInvestigation;
    private float suspicionStartTime;
    private float investigationStartTime;
    [Header("Sound Investigation - Unstuck Navigation")]
    [Tooltip("Spacing between attempts to calculate a sound investigation path.")]
    [SerializeField, Min(0.25f)] private float soundPathRetryInterval = 0.8f;
    [Tooltip("How long the boss can remain stationary on a valid path before changing approach.")]
    [SerializeField, Min(0.75f)] private float soundStuckCheckInterval = 2.0f;
    [SerializeField, Min(0.05f)] private float soundMinimumPathProgress = 0.22f;
    [SerializeField, Range(6, 24)] private int soundApproachDirections = 8;
    [SerializeField, Min(1f)] private float soundApproachMaxRadius = 5f;
    [Tooltip("A moving player must make a substantially different sound before the boss changes its current path.")]
    [SerializeField, Min(3f)] private float soundTravelRetargetDistance = 11f;
    [SerializeField, Min(1f)] private float soundTravelRetargetCooldown = 5f;

    [Header("Nearby Sound Priority")]
    [Tooltip("Within this distance, new footsteps quickly redirect an investigation.")]
    [SerializeField, Min(1f)] private float highPrioritySoundRadius = 6f;
    [Tooltip("By this distance, sound retargeting uses the original cautious long-range rules.")]
    [SerializeField, Min(2f)] private float lowPrioritySoundRadius = 30f;
    [Tooltip("How far the NEW sound must move before a nearby sound investigation retargets.")]
    [SerializeField, Min(0.3f)] private float nearbySoundMinimumMove = 1.25f;
    [Tooltip("Minimum time between nearby sound retargets; prevents path resets every frame.")]
    [SerializeField, Min(0.2f)] private float nearbySoundRetargetCooldown = 0.8f;
    [Tooltip("Only close new sound can override an old visual clue, after vision has been gone this long.")]
    [SerializeField, Min(0f)] private float nearbySoundOverridesOldSightAfter = 0.35f;
    [SerializeField] private bool debugSoundNavigation = false;

    // The actual noise/visual clue and the WALKABLE NavMesh goal are not the same.
    // Never overwrite the clue with a NavMesh.SamplePosition result.
    private Vector3 investigationCluePosition;
    private Vector3 failedSoundApproachPoint;
    private bool hasFailedSoundApproach;
    private float nextSoundPathAttempt;
    private float lastSoundTravelRetarget = float.NegativeInfinity;
    private Vector3 committedInvestigationPoint;
    private Vector3 investigationProgressPosition;
    private float nextInvestigationProgressCheck;
    private int investigationFailedPaths;
    private bool investigationPathRequested;
    private Quaternion scanOrigin;
    private int scanIndex;
    private float scanHoldUntil;
    private bool scanHolding;
    private int completedSearchStops;
    private float searchDeadline;
    private Cover investigationCover;
    private float investigationCoverStartTime;
    private int investigationCoverTargets;
    private readonly HashSet<Cover> inspectedCovers = new HashSet<Cover>();
    private readonly Dictionary<Cover, int> failedCoverApproaches = new Dictionary<Cover, int>();
    private float investigationLookStartTime;
    private float lastPlayerSeenTime = float.NegativeInfinity;
    private float searchStartTime;
    private float searchPointStartTime;
    private float searchPauseEndTime;
    private float visionSuppressedUntil;
    private float hearingSuppressedUntil;

    private bool pendingPostChargeSuspicion;
    private bool postChargeSuspicionActive;
    private Vector3 postChargeInvestigationPoint;
    private Vector3 postChargeLookPoint;
    private bool pendingPostChargeSearch;
    private bool suspicionAnimationEntered;
    private bool attackingCover;
    private bool hasLockedCoverRotation;
    private bool hasSearchPoint;
    private bool searchPaused;
    private bool initialized;
    private bool warnedMissingLocomotion;

    private Quaternion lockedCoverRotation;

    private int meleeHash;
    private float meleeEndTime;
    private float nextMeleeTime;
    private float suspicion;

    [Header("Reliable Fantacode Visual Indicator")]
    [Tooltip("Immediate visible awareness on EACH new sighting, including after hiding.")]
    [SerializeField, Range(0.02f, 0.9f)] private float freshSightingAwareness = 0.26f;
    private bool hadVisualContactLastScan;
    private int lastAwarenessFadeFrame = -1;

    [Header("Fantacode Awareness UI - Unified Senses")]
    [SerializeField, Range(0f, 0.99f)] private float hearingAwarenessLevel = 0.42f;
    [SerializeField, Min(0.1f)] private float hearingAwarenessHoldSeconds = 1.1f;
    [SerializeField, Min(0.1f)] private float hearingAwarenessFadeSeconds = 2.5f;
    private float audibleAwareness;
    private float lastAudibleContactTime = float.NegativeInfinity;

    // AwarenessSystem is the single display output for both senses.
    // Hearing alone never reaches 1.0 and therefore never causes full detection.
    private void UpdateUnifiedAwareness()
    {
        if (awareness == null) return;

        // This method is called from vision, hearing and LateUpdate. Fade ONCE
        // per rendered frame so the same noise is not decayed three times.
        if (lastAwarenessFadeFrame != Time.frameCount)
        {
            lastAwarenessFadeFrame = Time.frameCount;
            if (Time.time > lastAudibleContactTime + hearingAwarenessHoldSeconds)
                audibleAwareness = Mathf.MoveTowards(audibleAwareness, 0f,
                    Time.deltaTime / Mathf.Max(0.1f, hearingAwarenessFadeSeconds));
        }
        awareness.SetAwareness(Mathf.Clamp01(Mathf.Max(suspicion, audibleAwareness)));
    }
    [Header("Partial Visual Detection")]
    [SerializeField, Min(0f)] private float partialSightLossGrace = 0.2f;
    private bool hasPartialVisualContact;
    private bool partialVisualInvestigationStarted;
    private float lastPartialVisualContactTime = float.NegativeInfinity;
    private Vector3 partialVisualLastSeenPosition;
    [Header("Investigation Anti-Stuck Recovery")]
    [Tooltip("If a NavMesh path remains pending this long, cancel it and recover the current investigation waypoint.")]
    [SerializeField, Min(0.5f)] private float investigationPathPendingTimeout = 3f;
    [Tooltip("Only used if the agent is disabled, off the NavMesh or stopped updating its physical position after a charge.")]
    [SerializeField, Min(0.5f)] private float investigationNavMeshResyncRadius = 2f;
    [SerializeField, Min(0.25f)] private float investigationNavMeshRetryInterval = 1.25f;
    [Tooltip("How long the boss may work on one perimeter point even if a repeatedly renewed path claims to be valid.")]
    [SerializeField, Min(8f)] private float perimeterAbsoluteTimeout = 27f;
    private float pendingInvestigationPathSince = -1f;
    private float nextInvestigationNavMeshResync;
    private float perimeterPointStartedAt;
    private Cover failedCoverApproachOwner;
    private Vector3 failedCoverApproachPoint;
    private bool hasFailedCoverApproach;
    [Header("Navigation Stability")]
    [SerializeField, Min(0.1f)] private float stuckCheckSeconds = 2.5f;
    [SerializeField, Min(0f)] private float stuckMinimumProgress = 0.3f;
    [SerializeField, Min(0f)] private float searchRestartCooldown = 3f;
    private float nextInvestigationAllowed;
    private float movementProgressCheckTime;
    private Vector3 movementProgressPosition;
    private float nextCoverDecision;
    private float wanderPauseUntil;
    private Vector3 wanderDestination;
    private bool hasWanderDestination;
    private bool coverHuntActive;
    [Header("Patrol Cover Destruction")]
    [Tooltip("When a patrol check succeeds, randomly choose from the nearest few reachable Cover objects; walls need a Cover component.")]
    [SerializeField, Range(1, 6)] private int patrolNearestCoverCandidates = 3;
    [Tooltip("The boss must finish this patrol attack or abandon it if stuck.")]
    [SerializeField, Min(3f)] private float patrolCoverAttackTimeout = 24f;
    private bool patrolCoverHuntActive;
    private Vector3 patrolCoverApproachPoint;
    private float patrolCoverAttackDeadline;
    private float patrolCoverProgressCheckAt;
    private Vector3 patrolCoverProgressPosition;
    private int patrolCoverFailedApproaches;
    [SerializeField] private float coverHitFallbackSeconds = 0.55f;
    private float nextCoverFallbackHit;
    private Vector3 wanderOrigin;

    private int idleHash;
    private int runHash;
    private int attackHash;
    private int detectedHash;
    private int suspiciousHash;
    private int currentAnimationHash;

    private bool ChargeBusy =>
        chargeAttack != null && chargeAttack.IsBusy;

    // Visual awareness remains available during charging.
    // Hearing alone never makes this true.
    public bool HasVisualCombatTarget =>
        !EnemyTakesPriority &&
        mode == BossMode.Chase &&
        trackedPlayer != null &&
        (
            (chargeAttack != null && chargeAttack.IsCharging) ||
            (
                Time.time - lastPlayerSeenTime <= 0.2f &&
                Time.time >= visionSuppressedUntil &&
                !ChargeBusy
            )
        );

    public Transform VisualCombatTarget =>
        HasVisualCombatTarget ? trackedPlayer : null;

    private void Awake()
    {
        ai = GetComponent<AIController>();
        awareness = GetComponent<AwarenessSystem>();
        agent = GetComponent<NavMeshAgent>();
        chargeAttack = GetComponent<BossChargeAttack>();
        enemyStealthController = GetComponent<StealthAIController>();
        enemyWalkHash = Animator.StringToHash(enemyWalkState);

        Animator parentAnimator = GetComponent<Animator>();

        foreach (Animator animator in
            GetComponentsInChildren<Animator>(true))
        {
            if (animator != parentAnimator)
            {
                bossAnimator = animator;
                break;
            }
        }

        if (bossAnimator == null)
        {
            Debug.LogError(
                "BossEnemyAI: No child Animator found.",
                this
            );

            enabled = false;
            return;
        }

        if (parentAnimator != null)
            parentAnimator.enabled = false;

        bossAnimator.enabled = true;
        bossAnimator.applyRootMotion = false;

        meleeHash = Animator.StringToHash(meleeState);
        // Force the exact state name: existing scene components may retain
        // an older serialized lowercase "idle" value in the Inspector.
        idleHash = Animator.StringToHash("Idle");
        if (!bossAnimator.HasState(0, idleHash))
            Debug.LogWarning("BossEnemyAI: Animator layer 0 needs a state named exactly 'Idle'.", this);
        runHash = Animator.StringToHash(runState);
        if (!bossAnimator.HasState(0, enemyWalkHash) && !bossAnimator.HasState(0, Animator.StringToHash("walk")))
            Debug.LogWarning("BossEnemyAI: Assign Enemy Walk State to your actual walking state; using existing run state as fallback.", this);
        // Cover attacks always use the exact Animator state named "Attack".
        // This also avoids an old serialized Inspector value of "attack".
        attackHash = Animator.StringToHash("Attack");
        detectedHash = Animator.StringToHash(detectedState);
        suspiciousHash = Animator.StringToHash(suspiciousState);

        originalAutoBraking = agent.autoBraking;
        originalAgentSpeed = agent.speed;
        originalStoppingDistance = agent.stoppingDistance;

        agent.updateRotation = false;
        stableBodyRotation = transform.rotation;
        stableBodyRotationInitialized = true;
        lastFullScanPosition = transform.position - Vector3.forward * 100f;

        previousPosition = transform.position;
        wanderOrigin = transform.position;
        nextCoverDecision = Time.time + Random.Range(coverDecisionSeconds.x, coverDecisionSeconds.y);
        nextPatrolScanTime = Time.time + patrolScanInterval;
        initialized = true;
    }

    private void Start()
    {
        if (!initialized)
            return;

        RefreshPlayers();

        if (player == null)
            FindPlayerAutomatically();

        ResolvePlayerLocomotion();
        ForceAnimation(idleHash);

        Log(
            "BOSS AI READY | Player: " +
            (player != null ? player.name : "NOT FOUND")
        );
    }

    private void OnDisable()
    {
        ReleaseEnemyMovement();
        hasDetectedAnchor = false;
        if (!AgentReady())
            return;

        agent.isStopped = false;
        agent.autoBraking = originalAutoBraking;
        agent.speed = originalAgentSpeed;
        agent.stoppingDistance = originalStoppingDistance;
        agent.updateRotation = false;

        hasLockedCoverRotation = false;
    }

    private void Update()
    {
        if (!initialized || ai == null || (ai.Damagable != null && ai.Damagable.IsDead))
        {
            ReleaseEnemyAttackHold();
            return;
        }

        // A committed attack is not a pursuit: no target scans, cover searches,
        // navigation recovery, player switches, or gunfire can restart movement.
        if (mode == BossMode.EnemyAttack)
        {
            if ((IsEnemyAlive(enemyAttackTarget) || (enemyAttackImpactSucceeded && enemyAttackTarget != null)) && !ChargeBusy)
            {
                HoldEnemyAttackPosition();
                UpdateUnifiedAwareness();
                return;
            }
            ReleaseEnemyAttackHold();
            mode = BossMode.EnemyWalk;
            nextEnemyScan = 0f;
            ResetEnemyRouteRecovery();
        }
        UpdateEnemyPriority();
        if (ChargeBusy)
        {
            // Keep checking vision during the charge.
            // Do not run normal AI or overwrite Dash/Stunned.
            if (chargeAttack.IsCharging || chargeAttack.IsPositioning ||
                chargeAttack.IsWindingUp)
                CheckVision();

            previousPosition = transform.position;
            measuredVelocity = Vector3.zero;
            UpdateUnifiedAwareness();
            return;
        }

        // A full detection is a committed player sequence, not a soldier scan.
        if (playerDetectionReadyForCharge && mode == BossMode.Chase)
        {
            CheckVision();
            if (!CanContinuePlayerWindup())
            {
                BeginPlayerRedetectionSearch();
                return;
            }
            if (chargeAttack != null && chargeAttack.isActiveAndEnabled && AgentReady())
            {
                StopAgent();
                UpdateUnifiedAwareness();
                return;
            }
            playerDetectionReadyForCharge = false;
        }
        // Exactly one navigation / scan / cover-facing request per frame.
        hasRequestedBodyRotation = false;

        if (EnemyTakesPriority)
        {
            UpdateMeasuredVelocity();
            if (mode != BossMode.EnemyWalk) EnterEnemyWalk();
            UpdateEnemyWalk();
            UpdateUnifiedAwareness();
            return;
        }
        if (mode == BossMode.EnemyWalk)
        {
            bool closePlayer = TryGetVisiblePlayer(out Transform closeTarget) &&
                Vector3.Distance(transform.position, closeTarget.position) <= playerPriorityDistance;
            ResumeWalkingAfterDetection();
            if (closePlayer) DetectPlayer(closeTarget);
        }

        if (pendingPostChargeSuspicion)
        {
            pendingPostChargeSuspicion = false;
            EnterPostChargeSuspicion();
        }
        else if (pendingPostChargeSearch)
        {
            pendingPostChargeSearch = false;

            StartSearch(lastKnownPlayerPosition);

            Log(
                "BOSS RECOVERED - SEARCHING LAST SEEN POSITION"
            );
        }

        if (mode != BossMode.Chase && chaseObstacleCover != null)
            ClearChaseCover();

        // Repair navigation state left invalid by a charge, and recover a
        // stalled asynchronous path before this frame's investigation update.
        if (mode == BossMode.InvestigateTravel || mode == BossMode.Search)
            CheckInvestigationNavigationHealth();

        UpdateMeasuredVelocity();
        CheckVision();
        CheckPlayerMovement();
        if (AgentReady() && (mode != BossMode.Search ||
            investigationSearchPhase != InvestigationSearchPhase.Perimeter))
            agent.autoBraking = originalAutoBraking;

        switch (mode)
        {
            case BossMode.Cover:
                UpdateWandering();
                break;

            case BossMode.Suspicious:
                UpdateSuspicious();
                break;

            case BossMode.InvestigateTravel:
                UpdateInvestigationTravel();
                break;

            case BossMode.InvestigateLook:
                UpdateInvestigationLook();
                break;

            case BossMode.Search:
                UpdateSearching();
                break;

            case BossMode.Detected:
                UpdateDetection();
                break;

            case BossMode.Chase:
                UpdateChasing();
                break;
            case BossMode.Melee:
                UpdateMelee();
                break;
        }

        UpdateMovementAnimation();
    }

    private void LateUpdate()
    {
        if (!initialized || ai == null || (ai.Damagable != null && ai.Damagable.IsDead)) return;
        if (IsInEnemyAttackState && !ChargeBusy)
        {
            // Read the evaluated animation here, rather than integrate deltaTime.
            // Update already locks navigation at the previous horizontal anchor.
            UpdateEnemyAttackMotion();
            if (!IsInEnemyAttackState) return; // A failed animation may have released the hold.
            if (enemyAttackStateEntered && EnemyImpactAnimationFinished())
            {
                EndEnemyAttackState();
                return;
            }
            HoldEnemyAttackPosition();
            UpdateUnifiedAwareness();
            return;
        }
        if (ChargeBusy)
        {
            // BossChargeAttack owns yaw for Detected -> Dash -> Stunned.
            stableBodyRotation = transform.rotation;
            stableBodyRotationInitialized = true;
            return;
        }

        // Our script is the only system allowed to rotate this NavMeshAgent.
        // Never allow automatic agent rotation to fight search scans / cover facing.
        if (AgentReady())
        {
            agent.updateRotation = false;
            if (mode == BossMode.EnemyWalk && enemyOwnsMovement)
                agent.stoppingDistance = 0f;
        }

        // Keep the UI driven by the same suspicion value as the boss.
        // Some third-party AI components also update AwarenessSystem in Update.
        // LateUpdate makes our intended visual detection value authoritative
        // for this frame without interrupting the investigation itself.
        UpdateUnifiedAwareness();

        if ((mode == BossMode.Search || patrolCoverHuntActive) &&
            attackingCover && hasLockedCoverRotation)
            RequestBodyRotation(lockedCoverRotation, movementTurnSpeed);

        if (mode == BossMode.Detected)
        {
            HoldDetectedPosition();
            // Preserve yaw as well as the translation for the authored clip.
            stableBodyRotation = detectedFacing;
            stableBodyRotationInitialized = true;
            hasRequestedBodyRotation = false;
        }

        // Final single writer of the boss's body yaw. Using the previous
        // authored rotation (rather than another component's transient yaw)
        // prevents third-party AI and NavMesh steering from causing spin.
        if (!stableBodyRotationInitialized)
        {
            stableBodyRotation = transform.rotation;
            stableBodyRotationInitialized = true;
        }
        if (hasRequestedBodyRotation)
            stableBodyRotation = Quaternion.RotateTowards(
                stableBodyRotation, requestedBodyRotation,
                Mathf.Max(1f, requestedTurnSpeed) * Time.deltaTime);
        transform.rotation = stableBodyRotation;
    }

    // =====================================================
    // CHARGE RECOVERY
    // =====================================================

    // Called as soon as a charge enters Stunned. Its location is SNAPSHOTTED
    // by BossChargeAttack (at the moment of dodge, not after the dash travels
    // away). We queue the actual reaction for after stun & NavMesh recovery.
    public void SchedulePostChargeSuspicion(Vector3 lastKnownPoint, Vector3 lookPoint)
    {
        if (ai != null && ai.Target != null)
            ai.TargetHandler.TrySetTarget(null);

        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;
        hadVisualContactLastScan = false;
        hasPartialVisualContact = false;
        partialVisualInvestigationStarted = false;
        investigatingVisualClue = false;
        lostPlayerCoverSearch = false;
        hasPreviousFootstepPosition = false;
        attackingCover = false;
        hasLockedCoverRotation = false;
        currentCover = null;
        investigationCover = null;
        patrolCoverHuntActive = false;
        coverHuntActive = false;
        hasWanderDestination = false;
        patrolScanning = false;
        CancelSearch();

        // Don't immediately reacquire/recharge while recovering or while
        // turning to investigate. The marker goes to partial suspicion.
        suspicion = Mathf.Max(freshSightingAwareness, 0.35f);
        audibleAwareness = 0f;
        lastAudibleContactTime = float.NegativeInfinity;
        UpdateUnifiedAwareness();

        postChargeInvestigationPoint = lastKnownPoint;
        postChargeLookPoint = lookPoint;
        lastKnownPlayerPosition = lastKnownPoint;
        soundLocation = lastKnownPoint;
        pendingPostChargeSearch = false;
        pendingPostChargeSuspicion = true;
        postChargeSuspicionActive = false;
        float remaining = chargeAttack != null ?
            chargeAttack.RecoveryTimeRemaining : 0f;
        visionSuppressedUntil = Mathf.Max(visionSuppressedUntil,
            Time.time + remaining + postChargeVisionCooldown);
        if (suppressHearingAfterCharge)
            hearingSuppressedUntil = Mathf.Max(hearingSuppressedUntil,
                Time.time + remaining + postChargeVisionCooldown);
        currentAnimationHash = 0;
        Log("BOSS STUNNED - WILL FACE DODGE POSITION AND INVESTIGATE: " + lastKnownPoint);
    }

    private void EnterPostChargeSuspicion()
    {
        if (mode == BossMode.Cover) InterruptCoverAttack();
        CancelSearch();
        investigationCluePosition = postChargeInvestigationPoint;
        soundLocation = postChargeInvestigationPoint;
        lastKnownPlayerPosition = postChargeInvestigationPoint;
        committedInvestigationPoint = postChargeInvestigationPoint;
        mode = BossMode.Suspicious;
        postChargeSuspicionActive = true;
        suspicionStartTime = Time.time;
        suspicionAnimationEntered = false;
        // LateUpdate uses authored stable yaw; BossChargeAttack controlled
        // the previous frame, so its final stun orientation is the baseline.
        stableBodyRotation = transform.rotation;
        stableBodyRotationInitialized = true;
        StopAgent();
        currentAnimationHash = 0;
        ForceAnimation(suspiciousHash);
        FacePosition(postChargeLookPoint, postStunTurnSpeed);
        Log("POST-STUN SUSPICIOUS - TURNING BACK TO LAST KNOWN POSITION");
    }

    public void ForgetPlayerAfterCharge()
    {
        // lastKnownPlayerPosition has been updated
        // by CheckVision during the charge.

        if (ai != null && ai.Target != null)
            ai.TargetHandler.TrySetTarget(null);

        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;
        hadVisualContactLastScan = false;

        attackingCover = false;
        hasLockedCoverRotation = false;
        currentCover = null;

        CancelSearch();

        float remaining =
            chargeAttack != null
                ? chargeAttack.RecoveryTimeRemaining
                : 0f;

        visionSuppressedUntil = Mathf.Max(
            visionSuppressedUntil,
            Time.time + remaining + postChargeVisionCooldown
        );

        if (suppressHearingAfterCharge)
            hearingSuppressedUntil = visionSuppressedUntil;
        hasPreviousFootstepPosition = false;

        pendingPostChargeSearch = true;
        currentAnimationHash = 0;

        Log(
            "BOSS LOST PLAYER AFTER CHARGE - STUNNED"
        );
    }

    // Heavy-cover impact ends the combat encounter instead of starting another
    // last-seen investigation. Called by BossChargeAttack once stun begins.
    public void ReturnToPatrolAfterCharge()
    {
        if (ai != null && ai.Target != null)
            ai.TargetHandler.TrySetTarget(null);

        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;
        hadVisualContactLastScan = false;
        hasPartialVisualContact = false;
        partialVisualInvestigationStarted = false;
        lastPartialVisualContactTime = float.NegativeInfinity;
        investigatingVisualClue = false;
        lostPlayerCoverSearch = false;
        suspicion = 0f;
        audibleAwareness = 0f;
        lastAudibleContactTime = float.NegativeInfinity;
        if (awareness != null) awareness.SetAwareness(0f);
        hasPreviousFootstepPosition = false;
        pendingPostChargeSearch = false;
        patrolScanning = false;
        attackingCover = false;
        hasLockedCoverRotation = false;
        currentCover = null;
        investigationCover = null;
        patrolCoverHuntActive = false;
        coverHuntActive = false;
        hasWanderDestination = false;
        CancelSearch();
        mode = BossMode.Cover;
        currentAnimationHash = 0;

        // Not allowed to instantly rediscover the player during stun.
        float remaining = chargeAttack != null
            ? chargeAttack.RecoveryTimeRemaining : 0f;
        visionSuppressedUntil = Mathf.Max(visionSuppressedUntil,
            Time.time + remaining + postChargeVisionCooldown);
        hearingSuppressedUntil = Mathf.Max(hearingSuppressedUntil,
            Time.time + remaining + postChargeVisionCooldown);
        nextCoverDecision = Time.time + NextPatrolCoverInterval();
        Log("BOSS STUNNED BY IMPACT - TARGET FORGOTTEN; PATROL AFTER RECOVERY");
    }

    // =====================================================
    // PLAYER REFERENCES
    // =====================================================

    private void RefreshPlayers()
    {
        playerStimuli =
            FindObjectsOfType<VisualStimulus>();
    }

    private void FindPlayerAutomatically()
    {
        if (playerStimuli == null)
            RefreshPlayers();

        foreach (VisualStimulus stimulus in playerStimuli)
        {
            if (stimulus == null ||
                stimulus.Type != VisualStimulusType.Player)
            {
                continue;
            }

            LocomotionCore loco =
                stimulus.GetComponentInParent<LocomotionCore>();

            if (loco == null)
            {
                loco =
                    stimulus.GetComponentInChildren<LocomotionCore>(
                        true
                    );
            }

            player = loco != null
                ? loco.transform
                : stimulus.transform.root;

            return;
        }

        if (logEvents)
        {
            Debug.LogWarning(
                "BossEnemyAI: Player not found. " +
                "Assign Player in Inspector.",
                this
            );
        }
    }

    private bool ResolvePlayerLocomotion()
    {
        if (playerLocomotion != null)
            return true;

        if (player == null)
            return false;

        playerLocomotion =
            player.GetComponent<LocomotionCore>();

        if (playerLocomotion == null)
        {
            playerLocomotion =
                player.GetComponentInChildren<LocomotionCore>(
                    true
                );
        }

        if (playerLocomotion == null)
        {
            playerLocomotion =
                player.GetComponentInParent<LocomotionCore>();
        }

        if (playerLocomotion == null)
        {
            if (!warnedMissingLocomotion)
            {
                Debug.LogWarning(
                    "BossEnemyAI: Cannot find player LocomotionCore.",
                    this
                );

                warnedMissingLocomotion = true;
            }

            return false;
        }

        warnedMissingLocomotion = false;

        Log(
            "BOSS CONNECTED TO PLAYER LOCOMOTION: " +
            playerLocomotion.name
        );

        return true;
    }

    // =====================================================
    // HEARING
    // =====================================================

    private void CheckPlayerMovement()
    {
        if (Time.time < hearingSuppressedUntil || ChargeBusy)
            return;

        if (player == null)
        {
            FindPlayerAutomatically();
            if (player == null) return;
        }
        if (!ResolvePlayerLocomotion() || Time.time < nextMovementCheck)
            return;
        nextMovementCheck = Time.time + Mathf.Max(0.02f, movementCheckInterval);

        // IMPORTANT: CurrentVelocity can be close to zero after root-motion
        // rolls, animation hand-offs and some Fantacode locomotion transitions.
        // Measure actual character displacement too, instead of making the
        // hearing system completely deaf when that property is temporarily zero.
        Vector3 heardPosition = playerLocomotion.transform.position;
        Vector3 controllerVelocity = playerLocomotion.CurrentVelocity;
        controllerVelocity.y = 0f;
        float controllerSpeed = controllerVelocity.magnitude;

        float displacementSpeed = 0f;
        if (hasPreviousFootstepPosition)
        {
            float dt = Time.time - previousFootstepSampleTime;
            if (dt > 0.001f)
            {
                Vector3 delta = heardPosition - previousFootstepPosition;
                delta.y = 0f;
                displacementSpeed = delta.magnitude / dt;
            }
        }
        previousFootstepPosition = heardPosition;
        previousFootstepSampleTime = Time.time;
        hasPreviousFootstepPosition = true;
        float speed = Mathf.Max(controllerSpeed, displacementSpeed);

        bool crouching = playerLocomotion.IsCrouching;
        MoveType moveType = playerLocomotion.moveType;
        float hearingRange = crouching ? crouchingHearingRange :
            moveType == MoveType.Sprint ? sprintHearingRange :
            moveType == MoveType.Run ? runningHearingRange : walkingHearingRange;

        float threshold = Mathf.Max(0.02f, minimumAudibleFootstepSpeed);
        bool moving = speed >= threshold;
        bool audible = moving && (!crouching || hearCrouchedFootsteps) &&
                       IsSoundAudible(heardPosition, hearingRange);

        if (debugFootstepHearing && Time.time >= nextFootstepDebugTime)
        {
            nextFootstepDebugTime = Time.time + 1f;
            Debug.Log("[BOSS HEARING] " + (audible ? "HEARD" : "NOT HEARD") +
                " | movement=" + moveType + " crouch=" + crouching +
                " | coreSpeed=" + controllerSpeed.ToString("F2") +
                " displacementSpeed=" + displacementSpeed.ToString("F2") +
                " | range=" + hearingRange.ToString("F1") +
                " distance=" + Vector3.Distance(transform.position, heardPosition).ToString("F1") +
                " | muffleWalls=" + muffleFootstepsThroughWalls +
                " | mode=" + mode, this);
        }
        if (!audible) return;

        // Hearing contributes to the same Fantacode indicator, even while the
        // monster is already searching or clearing cover.
        lastAudibleContactTime = Time.time;
        audibleAwareness = Mathf.Max(audibleAwareness, hearingAwarenessLevel);
        UpdateUnifiedAwareness();

        if (logMovementChecks)
            Debug.Log("[BOSS HEARD] " + moveType + " at " + heardPosition +
                      " | state=" + mode, this);

        // An actual sighting and charge/combat outrank any audio sample.
        if (playerRedetectionPending || hadVisualContactLastScan || mode == BossMode.Chase ||
            mode == BossMode.Detected || mode == BossMode.Melee)
            return;

        if (mode != BossMode.Cover && mode != BossMode.Search &&
            mode != BossMode.InvestigateTravel && mode != BossMode.InvestigateLook)
            return;

        float distanceFromBoss = FlatDistance(transform.position, heardPosition);
        float requiredChange, cooldown;
        GetSoundRetargetRules(distanceFromBoss, mode, out requiredChange, out cooldown);
        bool closeEnoughToOverrideOldSight =
            distanceFromBoss <= highPrioritySoundRadius &&
            Time.time - lastPlayerSeenTime >= nearbySoundOverridesOldSightAfter;

        // Current sight remains absolute priority. A STALE last-seen position,
        // however, should yield to nearby footsteps that reveal a new location.
        if (investigatingVisualClue &&
            (mode == BossMode.InvestigateTravel || mode == BossMode.InvestigateLook) &&
            !closeEnoughToOverrideOldSight)
            return;

        if (mode != BossMode.Cover)
        {
            // Use the REAL sound/clue position, not the nearest NavMesh point.
            Vector3 oldClue = mode == BossMode.Search
                ? soundLocation : investigationCluePosition;
            float relocated = FlatDistance(heardPosition, oldClue);

            // Near sounds can trigger at roughly 1.25m / 0.8s. More distant
            // sounds retain progressively stronger hysteresis, so the agent
            // doesn't drop its path for every footstep.
            if (relocated < requiredChange ||
                Time.time - lastSoundRetargetTime < cooldown)
                return;
        }
        else if (Time.time < nextMovementInvestigation)
            return;

        if (BeginMovementInvestigation(heardPosition))
        {
            nextMovementInvestigation = Time.time +
                Mathf.Max(0.25f, movementInvestigationInterval);
            lastSoundRetargetTime = Time.time;
            if (debugSoundNavigation)
                Debug.Log("[BOSS SOUND PRIORITY] distance=" +
                    distanceFromBoss.ToString("F1") + " | minChange=" +
                    requiredChange.ToString("F2") + " | cooldown=" +
                    cooldown.ToString("F2") + " | clue=" + heardPosition, this);
            Log("BOSS HEARD NEW FOOTSTEPS - INVESTIGATING: " + heardPosition);
        }
    }

    // Smooth proximity scaling, not a single fixed 11m relocation rule:
    // near sounds are urgent; distant ones only redirect for major changes.
    private void GetSoundRetargetRules(float distanceFromBoss, BossMode currentMode,
        out float minimumRelocation, out float cooldown)
    {
        float farRadius = Mathf.Max(highPrioritySoundRadius + 0.1f,
            lowPrioritySoundRadius);
        float t = Mathf.InverseLerp(highPrioritySoundRadius, farRadius,
            distanceFromBoss);

        float farMinMove = currentMode == BossMode.InvestigateTravel
            ? Mathf.Max(soundTravelRetargetDistance, retargetOnNewSoundDistance)
            : Mathf.Max(5f, retargetOnNewSoundDistance);
        float farCooldown = currentMode == BossMode.InvestigateTravel
            ? soundTravelRetargetCooldown : 3.5f;

        minimumRelocation = Mathf.Lerp(nearbySoundMinimumMove,
            farMinMove, t);
        cooldown = Mathf.Lerp(nearbySoundRetargetCooldown,
            farCooldown, t);

        // An active cover/perimeter search should not restart for tiny steps,
        // even nearby. It still reacts much faster than distant sounds.
        if (currentMode == BossMode.Search)
        {
            minimumRelocation = Mathf.Max(minimumRelocation,
                nearbySoundMinimumMove * 1.5f);
            cooldown = Mathf.Max(cooldown,
                nearbySoundRetargetCooldown * 1.4f);
        }
    }

    private bool IsSoundAudible(Vector3 soundPosition, float normalRange)
    {
        float distance = Vector3.Distance(transform.position, soundPosition);
        if (distance > normalRange) return false;

        // Default: preserve hearing ranges even through walls. A blanket
        // ~0 layer mask previously marked practically all sounds as occluded.
        if (!muffleFootstepsThroughWalls) return true;

        Vector3 ear = transform.position + Vector3.up * eyeHeight;
        Vector3 source = soundPosition + Vector3.up * targetHeight;
        Transform ignoredPlayer = playerLocomotion != null
            ? playerLocomotion.transform : player;
        bool blocked = IsBlocked(ear, source, hearingOcclusionLayers, ignoredPlayer);
        if (!blocked) return true;
        return distance <= normalRange * Mathf.Clamp(obstructedHearingMultiplier, 0.25f, 1f);
    }

    // A visual clue always supersedes a sound investigation. The destination
    // is captured once so continuous sightings do not reset the NavMesh path.
    private bool investigatingVisualClue;

    private void PrioritizeVisualClue(Vector3 position)
    {
        if (mode == BossMode.Detected || mode == BossMode.Chase ||
            mode == BossMode.Melee || ChargeBusy)
            return;

        if (investigatingVisualClue &&
            (mode == BossMode.InvestigateTravel || mode == BossMode.InvestigateLook))
            return;

        if (mode == BossMode.Cover)
            InterruptCoverAttack();

        // Stop any ongoing sound search / cover attack before changing goals.
        patrolScanning = false;
        CancelSearch();
        investigationCover = null;
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;
        investigatingVisualClue = true;
        lostPlayerCoverSearch = false;
        partialVisualInvestigationStarted = true;
        investigationCluePosition = position;
        committedInvestigationPoint = position;
        soundLocation = position;
        hasFailedSoundApproach = false;
        nextSoundPathAttempt = 0f;
        lastKnownPlayerPosition = position;
        investigationCoverTargets = 0;
        nextCoverWallCheckMilestone = wallChecksBeforeFirstCoverAttack;
        inspectedCovers.Clear();
        investigationStartTime = Time.time;
        investigationFailedPaths = 0;
        investigationApproachPosition = transform.position;
        investigationPathRequested = false;
        nextInvestigationProgressCheck = Time.time + investigationProgressInterval;
        investigationProgressPosition = transform.position;
        nextInvestigationAllowed = Time.time + searchRestartCooldown;
        mode = BossMode.InvestigateTravel;
        currentAnimationHash = 0;
        TrySetInvestigationPath();
        Log("BOSS VISUAL CONTACT - OVERRIDING SOUND INVESTIGATION: " + position);
    }

    private bool BeginMovementInvestigation(Vector3 position)
    {
        if (mode != BossMode.Cover && mode != BossMode.Search &&
            mode != BossMode.InvestigateTravel && mode != BossMode.InvestigateLook)
            return false;
        float soundDistance = FlatDistance(transform.position, position);
        if (investigatingVisualClue &&
            (mode == BossMode.InvestigateTravel || mode == BossMode.InvestigateLook) &&
            (soundDistance > highPrioritySoundRadius ||
             Time.time - lastPlayerSeenTime < nearbySoundOverridesOldSightAfter))
            return false;

        // Retarget while STILL MOVING. Do not call CancelSearch / StopAgent:
        // those were causing the boss to restart its running animation and
        // hesitate every time the walking player emitted another sound.
        if (mode == BossMode.InvestigateTravel)
        {
            float minimumMove, cooldown;
            GetSoundRetargetRules(soundDistance, mode, out minimumMove, out cooldown);
            if (Time.time - lastSoundTravelRetarget < cooldown ||
                FlatDistance(position, investigationCluePosition) < minimumMove)
                return false;

            Vector3 priorClue = investigationCluePosition;
            Vector3 priorGoal = committedInvestigationPoint;
            investigationCluePosition = position;
            hasFailedSoundApproach = false;
            nextSoundPathAttempt = 0f;
            if (TrySetInvestigationPath())
            {
                investigatingVisualClue = false; // Nearby fresh audio beats stale sight.
                soundLocation = position;
                lastSoundTravelRetarget = Time.time;
                investigationStartTime = Time.time;
                investigationFailedPaths = 0;
                investigationProgressPosition = transform.position;
                nextInvestigationProgressCheck = Time.time + soundStuckCheckInterval;
                Log("BOSS FOLLOWING NEW SOUND: " + position);
                return true;
            }

            // Unreachable new clue: preserve current valid route and keep
            // working toward the previous position instead of freezing.
            investigationCluePosition = priorClue;
            committedInvestigationPoint = priorGoal;
            investigationPathRequested = AgentReady() && agent.hasPath;
            return false;
        }

        if (mode == BossMode.Cover)
            InterruptCoverAttack();

        patrolScanning = false;
        CancelSearch();
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;
        coverApproachValid = false;
        investigatingVisualClue = false;
        lostPlayerCoverSearch = false;
        investigationCluePosition = position;
        committedInvestigationPoint = position;
        soundLocation = position;
        hasFailedSoundApproach = false;
        nextSoundPathAttempt = 0f;
        investigationCover = null;
        investigationCoverTargets = 0;
        nextCoverWallCheckMilestone = wallChecksBeforeFirstCoverAttack;
        inspectedCovers.Clear();
        mode = BossMode.InvestigateTravel;
        investigationStartTime = Time.time;
        lastSoundTravelRetarget = Time.time;
        investigationFailedPaths = 0;
        investigationApproachPosition = transform.position;
        investigationPathRequested = false;
        nextInvestigationProgressCheck = Time.time + soundStuckCheckInterval;
        investigationProgressPosition = transform.position;
        currentAnimationHash = 0;
        nextInvestigationAllowed = Time.time + searchRestartCooldown;
        if (!TrySetInvestigationPath())
            Log("SOUND LOCATION NOT DIRECTLY REACHABLE - TRYING ALTERNATE APPROACH");
        Log("BOSS INVESTIGATING SOUND: " + position);
        return true;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // A heard location may be inside a wall, on inaccessible geometry, or
    // on a NavMesh island the boss cannot reach. Sample approachable positions
    // around the REAL sound source, validate complete paths, and commit once.
    private bool TrySetInvestigationPath()
    {
        if (!AgentReady() || Time.time < nextSoundPathAttempt) return false;
        nextSoundPathAttempt = Time.time + Mathf.Max(0.25f, soundPathRetryInterval);

        Vector3 bestPosition = Vector3.zero;
        NavMeshPath bestPath = null;
        float bestScore = float.PositiveInfinity;

        ConsiderSoundApproach(investigationCluePosition, 1.6f,
            ref bestPosition, ref bestPath, ref bestScore);

        // Most sounds already lie on a walkable point. Avoid an expensive
        // ring search on those frames; sample the ring only when the center is
        // unreachable or when recovering from an actual stall.
        if (bestPath == null || hasFailedSoundApproach)
        {
            int directions = Mathf.Max(6, soundApproachDirections);
            int ringCount = 3;
            for (int ring = 1; ring <= ringCount; ring++)
            {
                float radius = soundApproachMaxRadius * ring / ringCount;
                for (int i = 0; i < directions; i++)
                {
                    float angle = (i + (ring % 2) * 0.5f) * Mathf.PI * 2f / directions;
                    Vector3 sample = investigationCluePosition + new Vector3(
                        Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    ConsiderSoundApproach(sample, 0.85f,
                        ref bestPosition, ref bestPath, ref bestScore);
                }
            }
        }

        if (bestPath == null)
        {
            if (debugSoundNavigation)
                Debug.LogWarning("[BOSS SOUND PATH] No complete NavMesh path around " +
                    investigationCluePosition, this);
            return false;
        }

        agent.speed = investigateSpeed;
        agent.stoppingDistance = Mathf.Min(investigateStoppingDistance,
            investigationArrivalTolerance * 0.75f);
        agent.isStopped = false;
        if (!agent.SetPath(bestPath)) return false;

        committedInvestigationPoint = bestPosition;
        investigationPathRequested = true;
        if (debugSoundNavigation)
            Debug.Log("[BOSS SOUND PATH] source=" + investigationCluePosition +
                " | approach=" + committedInvestigationPoint +
                " | score=" + bestScore.ToString("F1"), this);
        return true;
    }

    private void ConsiderSoundApproach(Vector3 candidate, float snapDistance,
        ref Vector3 bestPosition, ref NavMeshPath bestPath, ref float bestScore)
    {
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(candidate, out hit, snapDistance, agent.areaMask))
            return;

        if (hasFailedSoundApproach &&
            FlatDistance(hit.position, failedSoundApproachPoint) < 1.1f)
            return;

        NavMeshPath trial = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, trial) ||
            trial.status != NavMeshPathStatus.PathComplete)
            return;

        float pathLength = PathDistance(trial);
        // Unity can return a one-corner path when the candidate is already
        // beneath the boss. Treat it as a valid arrival, not an invalid path.
        if (float.IsInfinity(pathLength) &&
            FlatDistance(hit.position, transform.position) <= investigationArrivalTolerance)
            pathLength = 0f;
        if (float.IsInfinity(pathLength) || float.IsNaN(pathLength))
            return;

        float distanceToClue = FlatDistance(hit.position, investigationCluePosition);
        float score = distanceToClue * 4.0f + pathLength * 0.22f;
        // Avoid choosing our own position as the destination when the sound
        // came from the other side of a solid wall.
        if (FlatDistance(hit.position, transform.position) < 0.7f &&
            distanceToClue > investigationArrivalTolerance)
            score += 30f;

        if (score >= bestScore) return;
        bestScore = score;
        bestPosition = hit.position;
        bestPath = trial;
    }

    // =====================================================
    // SUSPICIOUS
    // =====================================================

    private void UpdateSuspicious()
    {
        StopAgent();
        if (postChargeSuspicionActive)
            FacePosition(postChargeLookPoint, postStunTurnSpeed);

        if (AgentReady())
            agent.velocity = Vector3.zero;

        float elapsed =
            Time.time - suspicionStartTime;

        bool completed = false;

        if (bossAnimator.HasState(0, suspiciousHash))
        {
            AnimatorStateInfo current =
                bossAnimator.GetCurrentAnimatorStateInfo(0);

            AnimatorStateInfo next =
                bossAnimator.GetNextAnimatorStateInfo(0);

            bool inCurrent =
                current.shortNameHash == suspiciousHash;

            bool inNext =
                bossAnimator.IsInTransition(0) &&
                next.shortNameHash == suspiciousHash;

            if (inCurrent || inNext)
                suspicionAnimationEntered = true;

            if (suspicionAnimationEntered &&
                inCurrent &&
                !bossAnimator.IsInTransition(0) &&
                current.normalizedTime >= 1f)
            {
                completed = true;
            }

            float timeout = Mathf.Max(
                suspiciousFallbackDuration,
                GetSuspiciousClipDuration() +
                animationBlend + 0.2f
            );

            if (elapsed >= timeout)
                completed = true;
        }
        else
        {
            completed =
                elapsed >= suspiciousFallbackDuration;
        }

        if (postChargeSuspicionActive)
        {
            // Ensure we really turn toward the dodge clue, even if the
            // Suspicious animation happens to finish before the turn does.
            Vector3 toClue = postChargeLookPoint - transform.position;
            toClue.y = 0f;
            bool facingClue = toClue.sqrMagnitude < 0.05f ||
                Vector3.Angle(transform.forward, toClue) <= 9f;
            bool timeElapsed = elapsed >= postStunSuspicionSeconds;
            if (timeElapsed && (facingClue ||
                elapsed >= postStunSuspicionSeconds + 1.5f))
            {
                postChargeSuspicionActive = false;
                BeginInvestigationTravel();
            }
            return;
        }

        if (completed)
            BeginInvestigationTravel();
    }

    private float GetSuspiciousClipDuration()
    {
        if (bossAnimator.runtimeAnimatorController == null)
            return suspiciousFallbackDuration;

        foreach (AnimationClip clip in
            bossAnimator.runtimeAnimatorController.animationClips)
        {
            if (clip != null &&
                clip.name == suspiciousState)
            {
                return clip.length;
            }
        }

        return suspiciousFallbackDuration;
    }

    private void BeginInvestigationTravel()
    {
        // Kept for compatibility with the Suspicious state, but the visual
        // investigation enters travel immediately instead of freezing in place.
        mode = BossMode.InvestigateTravel;
        investigationStartTime = Time.time;
        investigationFailedPaths = 0;
        investigationPathRequested = false;
        investigationCluePosition = soundLocation;
        committedInvestigationPoint = soundLocation;
        nextSoundPathAttempt = 0f;
        hasFailedSoundApproach = false;
        nextInvestigationProgressCheck = Time.time + soundStuckCheckInterval;
        investigationProgressPosition = transform.position;
        TrySetInvestigationPath();
    }

    private void UpdateInvestigationTravel()
    {
        if (!AgentReady()) return;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = Mathf.Min(investigateStoppingDistance,
            investigationArrivalTolerance * 0.75f);

        // Arrival is checked against the WALKABLE goal, not against a player
        // position that might be on the other side of a collider.
        float goalDistance = FlatDistance(transform.position, committedInvestigationPoint);
        bool arrived = investigationPathRequested && !agent.pathPending &&
            (goalDistance <= investigationArrivalTolerance ||
             (agent.hasPath && agent.remainingDistance <= agent.stoppingDistance + 0.22f));

        if (arrived)
        {
            StopAgent();
            mode = BossMode.InvestigateLook;
            investigationLookStartTime = Time.time;
            BeginInvestigationScan(investigationCluePosition);
            lastFullScanPosition = transform.position;
            nextFullScanAllowed = Time.time + minimumSecondsBetweenScans;
            Log("BOSS REACHED HEARD / SEEN CLUE AREA - LOOKING AROUND");
            return;
        }

        // Do not spam SetPath while a path request is pending or just failed.
        // An invalid path gets a bounded, cooldown-controlled recovery.
        if (!agent.pathPending && (!investigationPathRequested ||
            !agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete))
        {
            if (Time.time >= nextSoundPathAttempt)
            {
                if (!TrySetInvestigationPath())
                    investigationFailedPaths++;
            }
        }
        else if (investigationPathRequested)
        {
            agent.isStopped = false;
            // If the agent itself is not attempting to advance, turning the
            // mesh toward it only produces the appearance of walking in place.
            if (agent.desiredVelocity.sqrMagnitude > 0.03f)
                FaceMovementDirection();
        }

        if (Time.time >= nextInvestigationProgressCheck)
        {
            float moved = FlatDistance(transform.position, investigationProgressPosition);
            bool shouldBeMoving = investigationPathRequested && !agent.pathPending &&
                agent.hasPath && agent.remainingDistance >
                agent.stoppingDistance + investigationArrivalTolerance;

            if (shouldBeMoving && moved < soundMinimumPathProgress)
            {
                investigationFailedPaths++;
                failedSoundApproachPoint = committedInvestigationPoint;
                hasFailedSoundApproach = true;
                if (debugSoundNavigation)
                    Debug.LogWarning("[BOSS SOUND PATH] Agent stalled. " +
                        "Retry " + investigationFailedPaths + "/" +
                        investigationMaxFailedPaths + " at " + committedInvestigationPoint, this);
                agent.ResetPath();
                investigationPathRequested = false;
                nextSoundPathAttempt = Time.time + Mathf.Min(0.5f, soundPathRetryInterval);
            }
            else if (moved >= soundMinimumPathProgress)
            {
                investigationFailedPaths = 0;
                hasFailedSoundApproach = false;
            }

            investigationProgressPosition = transform.position;
            nextInvestigationProgressCheck = Time.time + soundStuckCheckInterval;
        }

        // If no real progress is possible, NEVER remain permanently in the
        // travelling/run state. Investigate the physical area from reachable
        // nearby viewpoints, then continue into the extended cover sweep.
        if (investigationFailedPaths >= investigationMaxFailedPaths ||
            Time.time - investigationStartTime > investigationMaximumTravelSeconds)
        {
            Log("SOUND INVESTIGATION PATH STALLED - SEARCHING AROUND SOURCE");
            StopAgent();
            StartSearch(investigationCluePosition);
        }
    }

    private void BeginInvestigationScan()
    {
        BeginInvestigationScan(soundLocation);
    }

    private void BeginInvestigationScan(Vector3 focus)
    {
        Vector3 look = focus - transform.position;
        look.y = 0f;
        scanOrigin = look.sqrMagnitude > 1.0f
            ? Quaternion.LookRotation(look.normalized)
            : transform.rotation;
        scanIndex = 0;
        scanHolding = false;
        scanHoldUntil = 0f;
    }

    private bool UpdateInvestigationScan()
    {
        // Look toward the evidence, sweep one side, sweep the other, return.
        // Not a full spin and not a new random turn each frame.
        if (scanIndex >= 4) return true;
        // No frantic 150-degree whiplash: body turns are limited even when
        // an old prefab has a much larger serialized scan angle.
        float sweep = Mathf.Min(38f, investigationScanAngle);
        float angle = scanIndex == 1 ? -sweep :
                      scanIndex == 2 ? sweep : 0f;
        Quaternion target = scanOrigin * Quaternion.Euler(0f, angle, 0f);
        RequestBodyRotation(target, investigationScanSpeed);
        if (Quaternion.Angle(stableBodyRotation, target) > rotationDeadZone)
        {
            scanHolding = false;
            return false;
        }
        if (!scanHolding)
        {
            scanHolding = true;
            scanHoldUntil = Time.time + investigationScanHold;
        }
        else if (Time.time >= scanHoldUntil)
        {
            scanIndex++;
            scanHolding = false;
        }
        return scanIndex >= 4;
    }

    private void UpdateInvestigationLook()
    {
        StopAgent();
        bool finishedScan = UpdateInvestigationScan();
        if (finishedScan || Time.time - investigationLookStartTime >= investigateLookDuration)
            StartSearch(investigationCluePosition);
    }

    // =====================================================
    // VISION
    // =====================================================

    private bool TryGetVisiblePlayer(out Transform target)
    {
        target = null;
        if (Time.time < visionSuppressedUntil || playerStimuli == null) return false;
        float best = float.PositiveInfinity;
        foreach (VisualStimulus stimulus in playerStimuli)
        {
            if (stimulus == null || !stimulus.gameObject.activeInHierarchy ||
                stimulus.Type != VisualStimulusType.Player || stimulus.IsConcealed ||
                HidingArea.IsHidden(stimulus) || !CanSeePlayer(stimulus.transform)) continue;
            float distance = Vector3.Distance(transform.position, stimulus.transform.position);
            if (distance >= best) continue;
            best = distance;
            target = stimulus.transform;
        }
        return target != null;
    }

    // Called by the charge component BEFORE Dash. This is a live LOS check,
    // not HasVisualCombatTarget, which intentionally excludes busy states.
    public bool CanContinuePlayerWindup()
    {
        if (trackedPlayer == null || Time.time < visionSuppressedUntil || playerStimuli == null) return false;
        foreach (var stimulus in playerStimuli)
        {
            if (stimulus == null || !stimulus.gameObject.activeInHierarchy ||
                stimulus.Type != VisualStimulusType.Player || stimulus.IsConcealed || HidingArea.IsHidden(stimulus)) continue;
            var seen = stimulus.transform;
            if (seen != trackedPlayer && !seen.IsChildOf(trackedPlayer) && !trackedPlayer.IsChildOf(seen)) continue;
            if (CanSeePlayer(seen)) return true;
        }
        return false;
    }

    private int EnemyMask => enemyTargetLayers.value != 0 ? enemyTargetLayers.value : LayerMask.GetMask("Enemy");

    private bool IsEnemyAlive(Transform candidate)
    {
        if (candidate == null || !candidate.gameObject.activeInHierarchy ||
            candidate == transform || candidate.IsChildOf(transform) || transform.IsChildOf(candidate)) return false;
        if (player != null && (candidate == player || candidate.IsChildOf(player) || player.IsChildOf(candidate))) return false;
        if (BossSoldierRagdollImpact.IsImpacted(candidate)) return false;
        var fighter = candidate.GetComponent<FS_CombatCore.FighterCore>();
        if (fighter != null && fighter.IsDead) return false;
        AIController other = candidate.GetComponent<AIController>();
        return other == null || other.Damagable == null || !other.Damagable.IsDead;
    }

    private Transform ResolveEnemy(Collider collider)
    {
        if (collider == null) return null;
        AIController controller = collider.GetComponentInParent<AIController>();
        Transform actor = controller != null ? controller.transform : null;
        if (actor != null && ((EnemyMask & (1 << actor.gameObject.layer)) != 0 ||
            (EnemyMask & (1 << collider.gameObject.layer)) != 0)) return actor;
        // Also support non-Fantacode enemies on an explicitly selected layer.
        for (Transform t = collider.transform; t != null; t = t.parent)
            if ((EnemyMask & (1 << t.gameObject.layer)) != 0) return t;
        return null;
    }

    private bool CanSeeEnemy(Transform actor, Vector3 point)
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 delta = point - eye;
        if (delta.magnitude > visionDistance) return false;
        Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
        if (flat.sqrMagnitude > 0.001f && Vector3.Angle(transform.forward, flat) > visionAngle * 0.5f) return false;
        return !IsBlocked(eye, point, visionBlockingLayers, actor);
    }

    private void ScanEnemies()
    {
        visibleEnemy = null;
        float best = float.PositiveInfinity;
        if (EnemyMask == 0) return;
        foreach (Collider col in Physics.OverlapSphere(transform.position, visionDistance, ~0, QueryTriggerInteraction.Collide))
        {
            Transform actor = ResolveEnemy(col);
            if (!IsEnemyAlive(actor) || !CanSeeEnemy(actor, col.bounds.center)) continue;
            float distance = Vector3.Distance(transform.position, actor.position);
            // Strict nearest visible enemy. Only keep the previous target on an exact tie.
            if (distance > best || (distance == best && actor != rememberedEnemy)) continue;
            best = distance;
            visibleEnemy = actor;
        }
        if (visibleEnemy != null)
        {
            rememberedEnemy = visibleEnemy;
            rememberedEnemyPosition = visibleEnemy.position;
            enemySeenAt = Time.time;
        }
    }

    private bool HasRememberedEnemy()
    {
        return IsEnemyAlive(rememberedEnemy) &&
            Time.time - enemySeenAt <= enemyMemorySeconds;
    }

    private bool HasGunfireClue()
    {
        return IsEnemyAlive(firingEnemy) &&
            Time.time - enemyShotAt <= gunfireMemorySeconds;
    }

    private void ClearGunfireClue()
    {
        // Only start the cooldown when an actual clue was consumed or expired.
        // UpdateEnemyPriority calls this even when there is no clue.
        if (gunfireClueCommitted || !float.IsNegativeInfinity(enemyShotAt))
            nextGunfireDiscoveryAt = Time.time + Mathf.Max(0.1f, gunfireRediscoveryCooldown);
        firingEnemy = null;
        enemyShotAt = float.NegativeInfinity;
        gunfireClueCommitted = false;
    }

    // Gunfire is discovery only. It must never steal a known enemy target.
    // Retain the nearest pending gunfire clue if several shots arrive before
    // the next boss Update. Once selected, that clue cannot be replaced by shots.
    public void NotifyShotAt(Transform shooter)
    {
        if (!isActiveAndEnabled || !IsEnemyAlive(shooter)) return;
        AIController sourceAI = shooter.GetComponentInParent<AIController>();
        if (sourceAI != null) shooter = sourceAI.transform;
        if (!IsEnemyAlive(shooter)) return;
        // A bullet is information, never an animation or movement command.
        // Do not interrupt an existing pursuit, the player charge, or its recovery.
        if (IsInEnemyAttackState || HasRememberedEnemy() || IsEnemyAlive(priorityEnemy) ||
            (mode == BossMode.EnemyWalk && gunfireClueCommitted) ||
            ChargeBusy || pendingPostChargeSuspicion || pendingPostChargeSearch ||
            Time.time < nextGunfireDiscoveryAt)
        {
            if (debugGunfireDiscovery && Time.time >= nextIgnoredGunfireLog)
            {
                nextIgnoredGunfireLog = Time.time + 1f;
                Log("GUNFIRE IGNORED - existing target, recovery or discovery cooldown; movement unchanged");
            }
            return;
        }

        if (HasGunfireClue())
        {
            // The boss is already pursuing a gunfire clue. Do not let another
            // soldier (or repeated shots) change or indefinitely refresh it.
            if (gunfireClueCommitted) return;
            float currentDistance = (firingEnemyPosition - transform.position).sqrMagnitude;
            float newDistance = (shooter.position - transform.position).sqrMagnitude;
            if (newDistance >= currentDistance) return;
        }

        gunfireClueCommitted = false;
        firingEnemy = shooter;
        firingEnemyPosition = shooter.position;
        enemyShotAt = Time.time;
        nextEnemyScan = 0f;
        audibleAwareness = Mathf.Max(audibleAwareness, hearingAwarenessLevel);
        lastAudibleContactTime = Time.time;
        Log("BOSS DISCOVERED ENEMY BY GUNFIRE: " + shooter.name);
    }

    private void UpdateEnemyPriority()
    {
        if (mode == BossMode.Detected || playerRedetectionPending || playerDetectionReadyForCharge ||
            (chargeAttack != null && (chargeAttack.IsWindingUp || chargeAttack.IsPositioning)))
        {
            priorityEnemy = null;
            return;
        }
        if (Time.time >= nextEnemyScan)
        {
            nextEnemyScan = Time.time + Mathf.Max(0.05f, enemyScanInterval);
            ScanEnemies();
        }

        priorityEnemy = null;
        // Sight wins over gunfire. ScanEnemies has already selected the closest
        // visible actor without any shooting bonus or distance bias.
        // When none is visible, keep the last seen enemy for the memory window.
        if (HasRememberedEnemy())
        {
            priorityEnemy = rememberedEnemy;
            priorityEnemyPosition = rememberedEnemyPosition;
            ClearGunfireClue();
        }
        else if (HasGunfireClue())
        {
            // Walk to the last reported firing position, not an unseen moving
            // target's live position. Normal vision can then acquire an enemy.
            gunfireClueCommitted = true;
            priorityEnemy = firingEnemy;
            priorityEnemyPosition = firingEnemyPosition;
        }
        else
        {
            ClearGunfireClue();
        }

        // Preserve the existing very-close, visible player exception.
        if (priorityEnemy != null && TryGetVisiblePlayer(out Transform closePlayer) &&
            Vector3.Distance(transform.position, closePlayer.position) <= playerPriorityDistance)
            priorityEnemy = null;

        if (priorityEnemy != lastReportedPriorityEnemy)
        {
            lastReportedPriorityEnemy = priorityEnemy;
            if (priorityEnemy != null)
                Log("BOSS ENEMY TARGET: " + priorityEnemy.name +
                    " | distance=" + Vector3.Distance(transform.position, priorityEnemyPosition).ToString("F1") +
                    " | source=" + (priorityEnemy == rememberedEnemy ? "vision/memory" : "gunfire discovery"));
        }
    }

    private void EnterEnemyWalk()
    {
        InterruptCoverAttack();
        CancelSearch();
        investigationCover = null;
        chaseObstacleCover = null;
        pendingPostChargeSuspicion = pendingPostChargeSearch = false;
        postChargeSuspicionActive = false;
        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;
        hadVisualContactLastScan = hasPartialVisualContact = partialVisualInvestigationStarted = false;
        hasDetectedAnchor = false;
        suspicion = 0f;
        if (ai.Target != null) ai.TargetHandler.TrySetTarget(null);
        if (!enemyOwnsMovement)
        {
            enemyAiWasEnabled = ai.enabled;
            enemyStealthWasEnabled = enemyStealthController != null && enemyStealthController.enabled;
            if (enemyStealthController != null) enemyStealthController.enabled = false;
            ai.enabled = false;
            enemyOwnsMovement = true;
        }
        mode = BossMode.EnemyWalk;
        if (agent != null) agent.stoppingDistance = 0f;
        enemyClueLookUntil = -1f;
        ResetEnemyRouteRecovery();
        nextEnemyPath = 0f;
        hasEnemyGoal = false;
        currentAnimationHash = 0;
        Log("BOSS PRIORITIZING ENEMY - WALK ONLY: " + priorityEnemy.name);
    }

    private void ReleaseEnemyMovement()
    {
        ReleaseEnemyAttackHold();
        if (!enemyOwnsMovement) return;
        enemyOwnsMovement = false;
        if (agent != null) agent.stoppingDistance = originalStoppingDistance;
        enemyClueLookUntil = -1f;
        ResetEnemyRouteRecovery();
        if (ai != null) ai.enabled = enemyAiWasEnabled;
        if (enemyStealthController != null) enemyStealthController.enabled = enemyStealthWasEnabled;
    }

    private void ResetEnemyRouteRecovery()
    {
        hasEnemyGoal = false;
        enemyRouteIsDetour = false;
        enemyRouteFailures = 0;
        enemyPendingSince = -1f;
        nextEnemyPath = 0f;
        enemyFailedPoints.Clear();
        enemyFailedExpires.Clear();
        if (agent != null && enemyOriginalAvoidance >= 0)
            agent.avoidancePriority = enemyOriginalAvoidance;
        enemyOriginalAvoidance = -1;
    }

    private bool RecoverEnemyNavMesh()
    {
        if (AgentReady()) return true;
        if (agent == null || !gameObject.activeInHierarchy || Time.time < nextEnemyNavRecovery) return false;
        nextEnemyNavRecovery = Time.time + 0.75f;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(transform.position, out hit, enemyNavMeshRecoveryRadius, filter)) return false;
        Vector3 offset = Vector3.up * Mathf.Max(0.3f, agent.radius);
        if (IsBlocked(transform.position + offset, hit.position + offset, visionBlockingLayers)) return false;
        if (!agent.enabled) agent.enabled = true;
        if (!agent.Warp(hit.position) || !AgentReady()) return false;
        agent.updatePosition = true;
        agent.updateRotation = false;
        ResetEnemyRouteRecovery();
        if (debugEnemyRecovery) Log("ENEMY PURSUIT - restored nearby NavMesh");
        return true;
    }

    private void FailEnemyRoute(string reason)
    {
        if (hasEnemyGoal)
        {
            enemyFailedPoints.Add(enemyRouteEnd);
            enemyFailedExpires.Add(Time.time + Mathf.Max(1f, enemyFailedPointMemory));
            if (enemyFailedPoints.Count > 32) { enemyFailedPoints.RemoveAt(0); enemyFailedExpires.RemoveAt(0); }
        }
        hasEnemyGoal = false;
        enemyPendingSince = -1f;
        nextEnemyPath = 0f;
        enemyRouteFailures = Mathf.Min(12, enemyRouteFailures + 1);
        if (AgentReady())
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.isStopped = true;
            if (enemyOriginalAvoidance < 0) enemyOriginalAvoidance = agent.avoidancePriority;
            // Give the large boss priority over soldier avoidance, varying on retry.
            agent.avoidancePriority = Mathf.Clamp(5 + enemyRouteFailures % 8, 0, 99);
        }
        if (debugEnemyRecovery) Log("ENEMY PURSUIT RECOVERY #" + enemyRouteFailures + ": " + reason);
    }

    private void ConsiderEnemyRoute(Vector3 raw, Vector3 target, bool detour,
        ref float best, ref NavMeshPath bestPath, ref Vector3 bestPoint, ref bool bestDetour)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(raw, out hit, 1.3f, filter)) return;
        if (Vector3.Distance(transform.position, hit.position) < (detour ? 0.65f : 0.025f)) return;
        for (int i = 0; i < enemyFailedPoints.Count; i++)
            if (enemyFailedExpires[i] > Time.time && FlatDistance(hit.position, enemyFailedPoints[i]) < 1.2f) return;
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return;
        float length = PathDistance(path);
        if (float.IsInfinity(length) || float.IsNaN(length)) return;
        // Favor routes that close distance while permitting a short sideways escape.
        float score = FlatDistance(hit.position, target) * 2f + length * 0.35f + (detour ? 0.6f : 0f);
        if (score >= best) return;
        best = score; bestPath = path; bestPoint = hit.position; bestDetour = detour;
    }

    private bool PlanEnemyRoute(Vector3 target)
    {
        for (int i = enemyFailedPoints.Count - 1; i >= 0; i--)
            if (enemyFailedExpires[i] <= Time.time) { enemyFailedPoints.RemoveAt(i); enemyFailedExpires.RemoveAt(i); }
        NavMeshPath bestPath = null;
        Vector3 bestPoint = Vector3.zero;
        bool detour = false;
        float best = float.PositiveInfinity;
        ConsiderEnemyRoute(target, target, false, ref best, ref bestPath, ref bestPoint, ref detour);
        if (bestPath == null || enemyRouteFailures > 0)
        {
            float maxRadius = Mathf.Min(enemyRecoverySearchRadius, 3f + enemyRouteFailures * 2f);
            for (int ring = 1; ring <= 3; ring++)
                for (int i = 0; i < 12; i++)
                {
                    float angle = (i + ring * 0.5f) * Mathf.PI / 6f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Max(1.5f, maxRadius * ring / 3f);
                    ConsiderEnemyRoute(target + offset, target, true, ref best, ref bestPath, ref bestPoint, ref detour);
                    if (enemyRouteFailures > 0)
                        ConsiderEnemyRoute(transform.position + offset, target, true, ref best, ref bestPath, ref bestPoint, ref detour);
                }
        }
        if (bestPath == null) return false;
        if (!agent.SetPath(bestPath)) return false;
        if (!hasEnemyGoal)
        {
            enemyProgressAt = Time.time;
            enemyProgressPosition = transform.position;
        }
        hasEnemyGoal = true;
        enemyRouteEnd = bestPoint;
        enemyRouteIsDetour = detour;
        lastEnemyGoal = target;
        agent.isStopped = false;
        return true;
    }

    private bool TryEnterEnemyAttack()
    {
        // Close contact is checked live, not against the last vision scan or its cone.
        if (!IsEnemyAlive(priorityEnemy) || ChargeBusy || Time.unscaledTime < enemyAttackRetryAt) return false;
        Vector3 point = priorityEnemy.position;
        if (FlatDistance(transform.position, point) > Mathf.Max(0.1f, enemyAttackDistance) ||
            Mathf.Abs(transform.position.y - point.y) > enemyAttackMaxHeightDifference) return false;
        // Two independent straight lines allow close contact even if the tall boss's
        // eye ray is occluded. Both blocked means there is still an obstacle.
        if (IsBlocked(transform.position + Vector3.up * eyeHeight,
                point + Vector3.up * targetHeight, visionBlockingLayers, priorityEnemy) &&
            IsBlocked(transform.position + Vector3.up * 0.65f,
                point + Vector3.up * 0.65f, visionBlockingLayers, priorityEnemy)) return false;
        if (bossAnimator == null || !bossAnimator.isActiveAndEnabled ||
            !bossAnimator.HasState(0, Animator.StringToHash(enemyAttackAnimationState)))
        {
            enemyAttackRetryAt = Time.unscaledTime + 1f;
            Debug.LogWarning("BossEnemyAI: Cannot enter Jump Attack; Animator or state is missing. Pursuit stays active.", this);
            return false;
        }

        enemyAttackTarget = priorityEnemy;
        enemyAttackAnchor = transform.position;
        enemyAttackStartPosition = enemyAttackAnchor;
        // Snapshot the soldier's horizontal orientation at attack entry.
        // Offsets are in meters, independent of the soldier's transform scale.
        Vector3 targetForward = Vector3.ProjectOnPlane(enemyAttackTarget.forward, Vector3.up);
        if (targetForward.sqrMagnitude < 0.0001f)
            targetForward = Vector3.ProjectOnPlane(enemyAttackTarget.up, Vector3.up);
        if (targetForward.sqrMagnitude < 0.0001f)
            targetForward = Vector3.forward;
        targetForward.Normalize();
        Vector3 targetRight = Vector3.Cross(Vector3.up, targetForward);
        Vector3 landingPoint = point + targetRight * enemyAttackPositionOffset.x
            + targetForward * enemyAttackPositionOffset.z;
        enemyAttackEndPosition = new Vector3(landingPoint.x, enemyAttackAnchor.y, landingPoint.z);
        enemyAttackMotionClip = null;
        enemyAttackMotionActive = false;
        enemyAttackStateEntered = false;
        enemyAttackMotionProgress = 0f;
        enemyAttackLandingLogged = false;
        enemyAttackImpactTriggered = false;
        enemyAttackImpactSucceeded = false;
        mode = BossMode.EnemyAttack;
        ResetEnemyRouteRecovery();
        // Message avoids a reverse assembly dependency from Fantacode to this boss.
        // Only the selected soldier receives the hold, never every nearby soldier.
        enemyAttackTarget.BroadcastMessage("BeginBossEnemyAttackHold", transform, SendMessageOptions.DontRequireReceiver);
        HoldEnemyAttackPosition();
        enemyAttackStateHash = Animator.StringToHash(enemyAttackAnimationState);
        // Do not gate the attack on the asynchronous mesh queue. Start the
        // animation immediately; the impact will commit only a complete batch.
        Vector3 facing = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(facing);
            stableBodyRotation = transform.rotation;
        }
        StartPreparedEnemyAttackAnimation();
        if (ragdollEnemyOnImpact)
        {
            var preparation = enemyAttackTarget.GetComponent<BossSoldierRagdollImpact>();
            if (preparation == null) preparation = enemyAttackTarget.gameObject.AddComponent<BossSoldierRagdollImpact>();
            preparation.PrepareForAttack();
        }
        Log("ENEMY ATTACK STATE - BOTH STOPPED: " + enemyAttackTarget.name);
        return true;
    }

    private void StartPreparedEnemyAttackAnimation()
    {
        bossAnimator.CrossFadeInFixedTime(enemyAttackStateHash, animationBlend, 0, 0f);
        currentAnimationHash = enemyAttackStateHash;
        enemyAttackLastProgressAt = Time.unscaledTime;
        enemyAttackLastNormalizedTime = -1f;
        enemyAttackMotionActive = true;
    }

    private bool IsEnemyAttackAnimation(AnimatorStateInfo state)
    {
        return state.shortNameHash == enemyAttackStateHash ||
               state.fullPathHash == enemyAttackStateHash;
    }

    private void UpdateEnemyAttackMotion()
    {
        if (enemyAttackTarget == null) return;
        if (bossAnimator == null || !bossAnimator.isActiveAndEnabled)
        {
            AbortStalledEnemyAttack("Animator unavailable");
            return;
        }
        if (Time.unscaledTime - enemyAttackLastProgressAt > Mathf.Max(0.25f, enemyAttackAnimationStallSeconds))
        {
            AbortStalledEnemyAttack("Jump Attack Animator stopped progressing");
            return;
        }
        // Continue observing animation progress even after landing so a frozen
        // final animation cannot indefinitely hold the boss.
        // During the entry crossfade, the destination state is already playing.
        // Prefer its playback time so the blend does not add a timing delay.
        bool transitioning = bossAnimator.IsInTransition(0);
        AnimatorStateInfo state = bossAnimator.GetCurrentAnimatorStateInfo(0);
        bool useNext = false;
        if (transitioning)
        {
            AnimatorStateInfo next = bossAnimator.GetNextAnimatorStateInfo(0);
            if (IsEnemyAttackAnimation(next))
            {
                state = next;
                useNext = true;
            }
        }
        if (!IsEnemyAttackAnimation(state))
        {
            if (enemyAttackStateEntered)
            {
                if (enemyAttackImpactSucceeded) EndEnemyAttackState();
                else AbortStalledEnemyAttack("Jump Attack exited before impact");
            }
            return;
        }
        enemyAttackStateEntered = true;
        if (state.normalizedTime > enemyAttackLastNormalizedTime + 0.0001f)
        {
            enemyAttackLastNormalizedTime = state.normalizedTime;
            enemyAttackLastProgressAt = Time.unscaledTime;
        }
        if (!enemyAttackMotionActive) return;

        if (enemyAttackMotionClip == null)
        {
            enemyAttackClipBuffer.Clear();
            if (useNext) bossAnimator.GetNextAnimatorClipInfo(0, enemyAttackClipBuffer);
            else bossAnimator.GetCurrentAnimatorClipInfo(0, enemyAttackClipBuffer);
            float bestWeight = -1f;
            foreach (AnimatorClipInfo info in enemyAttackClipBuffer)
            {
                if (info.clip != null && info.weight > bestWeight)
                {
                    bestWeight = info.weight;
                    enemyAttackMotionClip = info.clip;
                }
            }
            if (enemyAttackMotionClip == null) return;
            float lastFrame = enemyAttackMotionClip.length * enemyAttackMotionClip.frameRate;
            if (lastFrame + 0.01f < enemyAttackArrivalFrame)
                Debug.LogWarning("BossEnemyAI: Jump Attack clip has fewer than " + enemyAttackArrivalFrame +
                    " frames. Reaching the target at the end of the available clip instead.", this);
        }

        // The animation's source clip time is normalizedTime * clip.length.
        // This automatically follows Animator speed, state speed and speed multipliers.
        float availableFrames = enemyAttackMotionClip.length * enemyAttackMotionClip.frameRate;
        if (availableFrames <= 0f) return;
        float arrivalFrame = Mathf.Min(Mathf.Max(1, enemyAttackArrivalFrame), availableFrames);
        float playedFrames = Mathf.Max(0f, state.normalizedTime) * availableFrames;
        float progress = Mathf.Clamp01(playedFrames / arrivalFrame);
        enemyAttackMotionProgress = Mathf.Max(enemyAttackMotionProgress, progress);
        enemyAttackAnchor = Vector3.Lerp(enemyAttackStartPosition, enemyAttackEndPosition, enemyAttackMotionProgress);
        // Deliberately never interpolate toward the soldier's height.
        enemyAttackAnchor.y = enemyAttackStartPosition.y;
        // Position first, then trigger impact at its OWN animation frame.
        // Keep the original landing snapshot even after the soldier becomes a ragdoll.
        HoldEnemyAttackPosition();
        float impactFrame = Mathf.Min(Mathf.Max(0, enemyAttackImpactFrame), availableFrames);
        if (ragdollEnemyOnImpact && !enemyAttackImpactTriggered && playedFrames + 0.0001f >= impactFrame)
            TriggerEnemyAttackImpact(playedFrames);
        if (!IsInEnemyAttackState) return;

        if (enemyAttackMotionProgress >= 1f && !enemyAttackLandingLogged)
        {
            enemyAttackLandingLogged = true;
            enemyAttackAnchor = enemyAttackEndPosition;
            Log("ENEMY JUMP ATTACK - reached target XZ at arrival frame " + arrivalFrame);
        }
        if (enemyAttackMotionProgress >= 1f && (!ragdollEnemyOnImpact || enemyAttackImpactTriggered ||
            availableFrames + 0.01f < impactFrame))
        {
            enemyAttackMotionActive = false;
            if (ragdollEnemyOnImpact && !enemyAttackImpactTriggered)
                Debug.LogWarning("BossEnemyAI: no impact because the clip does not contain impact frame " + impactFrame, this);
        }
    }

    private void AbortStalledEnemyAttack(string reason)
    {
        Debug.LogWarning("BossEnemyAI: Released failed enemy attack: " + reason, this);
        enemyAttackRetryAt = Time.unscaledTime + 0.35f;
        EndEnemyAttackState();
        if (bossAnimator != null && bossAnimator.isActiveAndEnabled) ForceAnimation(ResolveWalkHash());
    }

    private void TriggerEnemyAttackImpact(float playedFrames)
    {
        if (enemyAttackImpactTriggered || enemyAttackTarget == null) return;
        enemyAttackImpactTriggered = true;
        var impact = enemyAttackTarget.GetComponent<BossSoldierRagdollImpact>();
        if (impact == null) impact = enemyAttackTarget.gameObject.AddComponent<BossSoldierRagdollImpact>();
        try
        {
            enemyAttackImpactSucceeded = impact.Execute(enemyAttackStartPosition, enemyFragmentSeparationVelocity,
                enemyCorpseLayer, transform, enemyFragmentUpwardVelocity, enemyFragmentSpin);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex, this);
            AbortStalledEnemyAttack("ragdoll impact threw an exception");
            return;
        }
        if (enemyAttackImpactSucceeded)
        {
            if (rememberedEnemy == enemyAttackTarget) rememberedEnemy = null;
            if (visibleEnemy == enemyAttackTarget) visibleEnemy = null;
            if (firingEnemy == enemyAttackTarget) ClearGunfireClue();
            Log("ENEMY JUMP ATTACK - impact requested frame " + enemyAttackImpactFrame +
                " | evaluated clip frame=" + playedFrames.ToString("F2") + " | landing frame=" + enemyAttackArrivalFrame);
        }
    }

    private bool EnemyImpactAnimationFinished()
    {
        if (bossAnimator == null || !bossAnimator.isActiveAndEnabled) return true;
        var state = bossAnimator.GetCurrentAnimatorStateInfo(0);
        if (bossAnimator.IsInTransition(0))
        {
            var next = bossAnimator.GetNextAnimatorStateInfo(0);
            if (IsEnemyAttackAnimation(next)) return next.normalizedTime >= 1f;
        }
        return !IsEnemyAttackAnimation(state) || state.normalizedTime >= 1f;
    }

    private void HoldEnemyAttackPosition()
    {
        if (enemyAttackTarget == null) return;
        priorityEnemy = enemyAttackTarget;
        hasRequestedBodyRotation = false;
        measuredVelocity = Vector3.zero;
        previousPosition = enemyAttackAnchor;
        if (AgentReady())
        {
            agent.stoppingDistance = 0f;
            agent.isStopped = true;
            if (agent.hasPath || agent.pathPending) agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.nextPosition = enemyAttackAnchor;
        }
        if (bossAnimator != null && bossAnimator.applyRootMotion) bossAnimator.applyRootMotion = false;
        transform.position = enemyAttackAnchor;
        // Leave the attack animation alone while both characters are held.
    }

    private void ReleaseEnemyAttackHold()
    {
        Transform target = enemyAttackTarget;
        enemyAttackTarget = null;
        enemyAttackPrewarmTarget = null;
        enemyAttackImpactTriggered = false;
        enemyAttackImpactSucceeded = false;
        enemyAttackMotionActive = false;
        enemyAttackMotionClip = null;
        enemyAttackClipBuffer.Clear();
        if (mode != BossMode.EnemyAttack && target == null) return;
        mode = BossMode.EnemyWalk;
        if (target != null)
            target.BroadcastMessage("EndBossEnemyAttackHold", transform, SendMessageOptions.DontRequireReceiver);
        if (AgentReady())
        {
            agent.nextPosition = transform.position;
            agent.updatePosition = true;
        }
    }

    // An attack animation event can call this to end the hold.
    // A living target still in range can trigger a new attack on a later update.
    public void EndEnemyAttackState()
    {
        ReleaseEnemyAttackHold();
        ResetEnemyRouteRecovery();
        nextEnemyScan = 0f;
    }

    private void UpdateEnemyWalk()
    {
        if (agent != null) agent.stoppingDistance = 0f;
        if (TryEnterEnemyAttack()) return;
        // The asset can take several frames to prepare all hidden cuts. Give
        // the closest visible target a head start without killing or moving it.
        if (ragdollEnemyOnImpact && enemyAttackPrewarmLeadDistance > 0f &&
            enemyAttackPrewarmTarget != priorityEnemy && IsEnemyAlive(priorityEnemy) && visibleEnemy == priorityEnemy &&
            Mathf.Abs(transform.position.y - priorityEnemy.position.y) <= enemyAttackMaxHeightDifference &&
            FlatDistance(transform.position, priorityEnemy.position) <= enemyAttackDistance + enemyAttackPrewarmLeadDistance)
        {
            enemyAttackPrewarmTarget = priorityEnemy;
            var preparation = priorityEnemy.GetComponent<BossSoldierRagdollImpact>();
            if (preparation == null) preparation = priorityEnemy.gameObject.AddComponent<BossSoldierRagdollImpact>();
            preparation.PrepareForAttack();
        }
        if (!RecoverEnemyNavMesh()) return;
        if (lastEnemyPathTarget != priorityEnemy)
        {
            lastEnemyPathTarget = priorityEnemy;
            enemyClueLookUntil = -1f;
            ResetEnemyRouteRecovery();
            // Keep the old path until the replacement route is calculated.
            // A closest-enemy switch must not first zero the agent velocity.
        }
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.autoBraking = true;
        agent.speed = Mathf.Max(0.1f, enemyWalkSpeed);
        if (bossAnimator != null) bossAnimator.applyRootMotion = false;
        bool targetActuallyVisible = visibleEnemy == priorityEnemy;
        float arrivalTolerance = targetActuallyVisible ? EnemyContactTolerance : enemyClueArrivalDistance + 0.15f;
        bool near = FlatDistance(transform.position, priorityEnemyPosition) <= arrivalTolerance &&
            Mathf.Abs(transform.position.y - priorityEnemyPosition.y) < Mathf.Max(1f, agent.height);
        bool unobstructed = !IsBlocked(transform.position + Vector3.up * eyeHeight,
            priorityEnemyPosition + Vector3.up * targetHeight, visionBlockingLayers, priorityEnemy);
        if (targetActuallyVisible || !near || !unobstructed)
            enemyClueLookUntil = -1f;
        if (near && unobstructed && targetActuallyVisible)
        {
            // An animation failure has a bounded retry, not a permanent arrival idle.
            FacePosition(priorityEnemyPosition, turnSpeed);
            nextEnemyScan = 0f;
            nextEnemyPath = 0f;
        }
        // Reaching a last-known location is not a new detection. Give vision
        // one short chance to reacquire before returning to patrol, and then
        // suppress immediate rediscovery from continuous gunfire.
        if (near && !targetActuallyVisible && unobstructed)
        {
            if (enemyClueLookUntil < 0f)
            {
                enemyClueLookUntil = Time.time + Mathf.Max(0.1f, enemyClueLookSeconds);
                nextEnemyScan = 0f;
                StopAgent();
                if (agent.hasPath || agent.pathPending) agent.ResetPath();
                ResetEnemyRouteRecovery();
            }
            if (Time.time < enemyClueLookUntil)
            {
                FacePosition(priorityEnemyPosition, turnSpeed);
                PlayAnimation(idleHash);
                return;
            }
            enemyClueLookUntil = -1f;
            if (rememberedEnemy == priorityEnemy) enemySeenAt = float.NegativeInfinity;
            if (firingEnemy == priorityEnemy) ClearGunfireClue();
            nextGunfireDiscoveryAt = Time.time + Mathf.Max(0.1f, gunfireRediscoveryCooldown);
            nextEnemyScan = 0f;
            priorityEnemy = null;
            ResumeWalkingAfterDetection();
            return;
        }
        if (hasEnemyGoal)
        {
            if (FlatDistance(transform.position, enemyProgressPosition) >= enemyMinimumProgress)
            {
                enemyProgressPosition = transform.position;
                enemyProgressAt = Time.time;
            }
            if (agent.pathPending)
            {
                if (enemyPendingSince < 0f) enemyPendingSince = Time.time;
                if (Time.time - enemyPendingSince >= enemyPathPendingTimeout) FailEnemyRoute("path pending timeout");
            }
            else
            {
                enemyPendingSince = -1f;
                bool reached = FlatDistance(transform.position, enemyRouteEnd) <= (enemyRouteIsDetour ? 0.35f : EnemyContactTolerance) &&
                    Mathf.Abs(transform.position.y - enemyRouteEnd.y) <= Mathf.Max(1f, Mathf.Abs(agent.baseOffset) + 0.5f);
                if (reached) FailEnemyRoute("route ended before target was reached");
                else if (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete ||
                    Time.time - enemyProgressAt >= enemyStuckSeconds)
                    FailEnemyRoute("path lost or boss stopped moving");
            }
        }
        // Replanning a moving target must not reset the progress watchdog.
        if (Time.time >= nextEnemyPath && (!hasEnemyGoal || (!agent.pathPending && !enemyRouteIsDetour &&
            FlatDistance(lastEnemyGoal, priorityEnemyPosition) > (targetActuallyVisible ? 0.15f : 1.25f))))
        {
            nextEnemyPath = Time.time + Mathf.Max(0.2f, chasePathRefreshInterval);
            if (!PlanEnemyRoute(priorityEnemyPosition) && !hasEnemyGoal)
            {
                enemyRouteFailures = Mathf.Min(12, enemyRouteFailures + 1);
                nextEnemyPath = Time.time + 0.45f;
                if (debugEnemyRecovery && (enemyRouteFailures == 1 || enemyRouteFailures == 4))
                    Log("ENEMY PURSUIT - no complete route; expanding search");
            }
        }
        agent.stoppingDistance = 0f;
        agent.isStopped = !hasEnemyGoal;
        if (hasEnemyGoal)
        {
            FaceMovementDirection();
            bool moving = agent.velocity.sqrMagnitude > 0.04f || measuredVelocity.sqrMagnitude > 0.04f;
            bool startingRoute = Time.time - enemyProgressAt < 0.25f && !agent.isStopped && agent.hasPath;
            PlayAnimation(moving || startingRoute ? ResolveWalkHash() : idleHash);
        }
        else { FacePosition(priorityEnemyPosition, turnSpeed); PlayAnimation(idleHash); }
    }

    private int ResolveWalkHash()
    {
        if (bossAnimator.HasState(0, enemyWalkHash)) return enemyWalkHash;
        int lower = Animator.StringToHash("walk");
        return bossAnimator.HasState(0, lower) ? lower : runHash;
    }

    // Cancel only the anticipation phase. No stun, Dash, or forced search.
    public void ResumeWalkingAfterDetection()
    {
        playerDetectionReadyForCharge = false;
        playerRedetectionPending = false;
        ReleaseEnemyMovement();
        if (ai != null && ai.Target != null) ai.TargetHandler.TrySetTarget(null);
        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;
        hadVisualContactLastScan = hasPartialVisualContact = partialVisualInvestigationStarted = false;
        suspicion = 0f;
        investigatingVisualClue = postChargeSuspicionActive = lostPlayerCoverSearch = false;
        pendingPostChargeSuspicion = pendingPostChargeSearch = false;
        hasDetectedAnchor = false;
        InterruptCoverAttack();
        CancelSearch();
        patrolScanning = false;
        hasWanderDestination = false;
        nextCoverDecision = Time.time + NextPatrolCoverInterval();
        mode = BossMode.Cover;
        walkAfterCancelledDetection = true;
        if (AgentReady())
        {
            agent.updatePosition = true;
            agent.updateRotation = false;
            agent.isStopped = false;
            agent.speed = wanderSpeed;
        }
        UpdateUnifiedAwareness();
        if (EnemyTakesPriority)
        {
            EnterEnemyWalk();
            UpdateEnemyWalk();
        }
        else
        {
            UpdateWandering();
            ForceAnimation(AgentReady() && agent.hasPath ? ResolveWalkHash() : idleHash);
        }
        Log("BOSS CANCELLED DETECTION - WALKING");
    }

    public void ReleaseDetectedPositionForCharge()
    {
        if (!hasDetectedAnchor) return;
        hasDetectedAnchor = false;
        if (AgentReady())
        {
            agent.nextPosition = transform.position;
            agent.updatePosition = true;
        }
    }

    private void HoldDetectedPosition()
    {
        if (!hasDetectedAnchor) { detectedAnchor = transform.position; hasDetectedAnchor = true; }
        if (AgentReady())
        {
            agent.isStopped = true;
            if (agent.hasPath || agent.pathPending) agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.updatePosition = false;
            agent.nextPosition = detectedAnchor;
        }
        if (bossAnimator != null)
        {
            bossAnimator.applyRootMotion = false;
            if (bossAnimator.transform != transform) bossAnimator.transform.localPosition = detectedAnimatorLocalPosition;
        }
        transform.position = detectedAnchor;
        transform.rotation = detectedFacing;
    }

    private void CheckVision()
    {
        if (Time.time < visionSuppressedUntil)
        {
            hadVisualContactLastScan = false;
            return;
        }

        if (Time.time >= nextVisionScan)
        {
            RefreshPlayers();
            nextVisionScan = Time.time + 1f;

            if (player == null)
            {
                FindPlayerAutomatically();
                ResolvePlayerLocomotion();
            }
        }

        bool visible = false;
        Transform seenTarget = null;

        if (playerStimuli != null)
        {
            foreach (VisualStimulus stimulus in playerStimuli)
            {
                if (stimulus == null ||
                    !stimulus.gameObject.activeInHierarchy ||
                    stimulus.Type != VisualStimulusType.Player ||
                    stimulus.IsConcealed ||
                    HidingArea.IsHidden(stimulus) ||
                    !CanSeePlayer(stimulus.transform))
                    continue;

                visible = true;
                seenTarget = stimulus.transform;
                break;
            }
        }

        if (visible)
        {
            bool firstFrameOfNewSighting = !hadVisualContactLastScan;
            hadVisualContactLastScan = true;
            lastKnownPlayerPosition = seenTarget.position;
            lastPlayerSeenTime = Time.time;

            // Crucial: the Fantacode marker needs a fresh nonzero value EVERY
            // time the player reappears, including if it previously faded to 0.
            if (firstFrameOfNewSighting)
            {
                // Re-arm the UI's zero -> positive transition when no hearing
                // awareness is active. This does not force full detection.
                if (suspicion <= 0.001f && audibleAwareness <= 0.001f && awareness != null)
                    awareness.SetAwareness(0f);
                suspicion = Mathf.Max(suspicion, freshSightingAwareness);
                Log("BOSS SAW PLAYER - FANTACODE INDICATOR REACTIVATED");
            }

            // Combat continues independently of UI fade when out of sight.
            if (mode == BossMode.Chase || mode == BossMode.Detected || mode == BossMode.Melee)
                suspicion = 1f;
            UpdateUnifiedAwareness();

            // Charging owns its movement and animation, never the UI meter.
            if (ChargeBusy)
                return;

            if (playerRedetectionPending && mode != BossMode.Detected)
            {
                DetectPlayer(seenTarget);
                return;
            }
            if (mode == BossMode.Chase || mode == BossMode.Detected || mode == BossMode.Melee)
                return;

            hasPartialVisualContact = true;
            partialVisualLastSeenPosition = seenTarget.position;
            lastPartialVisualContactTime = Time.time;

            // Continue building suspicion even during an investigation.
            // Never move the committed investigation destination every frame.

            Vector3 direction = seenTarget.position - transform.position;
            direction.y = 0f;
            float angle = Vector3.Angle(transform.forward, direction);
            float centerFactor = Mathf.Lerp(0.4f, 1.5f,
                1f - Mathf.Clamp01(angle / Mathf.Max(1f, visionAngle * 0.5f)));
            float distanceFactor = Mathf.Lerp(1.5f, 0.65f,
                Mathf.Clamp01(direction.magnitude / Mathf.Max(1f, visionDistance)));

            suspicion = Mathf.Clamp01(suspicion +
                Time.deltaTime / Mathf.Max(0.01f, suspicionBuildSeconds) *
                centerFactor * distanceFactor);

            UpdateUnifiedAwareness();

            if (suspicion >= 1f)
            {
                hasPartialVisualContact = false;
                partialVisualInvestigationStarted = false;
                DetectPlayer(seenTarget);
                return;
            }

            // Visual contact outranks hearing and any sound investigation.
            // Retarget once to the visual clue; do not keep resetting the path.
            if (suspicion >= investigateSuspicionThreshold &&
                !investigatingVisualClue &&
                (mode == BossMode.InvestigateTravel ||
                 mode == BossMode.InvestigateLook || mode == BossMode.Search))
            {
                PrioritizeVisualClue(seenTarget.position);
                return;
            }

            // Suspicious UI means the boss physically investigates immediately.
            // Once committed, the destination stays fixed until arrival.
            if (firstFrameOfNewSighting &&
                (mode == BossMode.Cover || mode == BossMode.Search))
            {
                partialVisualInvestigationStarted = true;
                PrioritizeVisualClue(seenTarget.position);
            }

            return;
        }

        // A new sighting after this loss must wake the Fantacode indicator.
        hadVisualContactLastScan = false;

        // Brief occlusion does not cause investigation to restart repeatedly.
        if (hasPartialVisualContact &&
            Time.time - lastPartialVisualContactTime >= partialSightLossGrace)
        {
            hasPartialVisualContact = false;

            if (!partialVisualInvestigationStarted &&
                suspicion >= investigateSuspicionThreshold &&
                mode != BossMode.Chase &&
                mode != BossMode.Detected &&
                mode != BossMode.Melee &&
                !ChargeBusy)
            {
                partialVisualInvestigationStarted = true;
                soundLocation = partialVisualLastSeenPosition;
                lastKnownPlayerPosition = partialVisualLastSeenPosition;

                // Preserve an ongoing investigation. Only begin a new one
                // when idle or searching, without fighting an active path.
                if (mode == BossMode.Cover || mode == BossMode.Search)
                    BeginMovementInvestigation(partialVisualLastSeenPosition);

                Log("BOSS LOST PARTIAL VISUAL CONTACT - INVESTIGATING LAST SEEN POSITION");
            }
        }

        // Fade the UI whenever vision is lost, EVEN if the boss is still
        // pursuing a last-known combat target. This is only the visual meter;
        // the chase/search state is independent and is not cancelled.
        suspicion = Mathf.MoveTowards(suspicion, 0f,
            Time.deltaTime / Mathf.Max(0.01f, suspicionFadeSeconds));
        UpdateUnifiedAwareness();
    }

    private bool CanSeePlayer(Transform target)
    {
        Vector3 eye =
            transform.position +
            Vector3.up * eyeHeight;

        Vector3 targetPoint =
            target.position +
            Vector3.up * targetHeight;

        Vector3 direction =
            targetPoint - eye;

        if (direction.magnitude > visionDistance)
            return false;

        Vector3 flatDirection =
            Vector3.ProjectOnPlane(
                direction,
                Vector3.up
            );

        if (flatDirection.sqrMagnitude > 0.001f &&
            Vector3.Angle(
                transform.forward,
                flatDirection) >
            visionAngle * 0.5f)
        {
            return false;
        }

        return !IsBlocked(
            eye,
            targetPoint,
            visionBlockingLayers,
            target
        );
    }

    private bool IsBlocked(
        Vector3 from,
        Vector3 to,
        LayerMask layers,
        Transform ignoredTarget = null)
    {
        Vector3 direction = to - from;

        if (direction.sqrMagnitude < 0.0001f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            from,
            direction.normalized,
            direction.magnitude,
            layers,
            QueryTriggerInteraction.Ignore
        );

        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform =
                hit.collider.transform;

            if (hitTransform == transform ||
                hitTransform.IsChildOf(transform))
            {
                continue;
            }

            // Corpses are transparent to gameplay queries in EVERY AI state.
            if (BossSoldierRagdollImpact.IsCorpseCollider(hit.collider)) continue;

            // Treat every collider belonging to the player as the target,
            // not as a wall. A sibling collider otherwise blocks vision of a
            // VisualStimulus on the same character after reappearing.
            if (player != null &&
                (hitTransform == player || hitTransform.IsChildOf(player)))
                continue;

            if (ignoredTarget != null &&
                (hitTransform == ignoredTarget ||
                 hitTransform.IsChildOf(ignoredTarget) ||
                 ignoredTarget.IsChildOf(hitTransform)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    // =====================================================
    // DETECTION / CHASE
    // =====================================================

    private void DetectPlayer(Transform target)
    {
        if (target == null ||
            Time.time < visionSuppressedUntil ||
            ChargeBusy)
        {
            return;
        }

        investigatingVisualClue = false;
        postChargeSuspicionActive = false;
        pendingPostChargeSuspicion = false;
        lostPlayerCoverSearch = false;
        suspicion = 1f;
        hasPartialVisualContact = false;
        partialVisualInvestigationStarted = false;
        trackedPlayer = target;
        lastKnownPlayerPosition = target.position;
        lastPlayerSeenTime = Time.time;

        if (mode == BossMode.Detected ||
            mode == BossMode.Chase)
        {
            return;
        }

        if (mode == BossMode.Cover)
            InterruptCoverAttack();

        CancelSearch();
        investigationCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;

        mode = BossMode.Detected;
        playerRedetectionPending = false;
        playerDetectionReadyForCharge = false;
        detectionLostSight = false;
        priorityEnemy = null;
        walkAfterCancelledDetection = false;
        detectedStartedFrame = Time.frameCount;
        detectedLastProgressAt = Time.unscaledTime;
        detectedLastNormalized = -1f;
        var facing = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(facing);
        detectedFacing = transform.rotation;
        stableBodyRotation = detectedFacing;
        stableBodyRotationInitialized = true;
        hasRequestedBodyRotation = false;
        detectedAnchor = transform.position;
        hasDetectedAnchor = true;
        if (bossAnimator != null) detectedAnimatorLocalPosition = bossAnimator.transform.localPosition;

        UpdateUnifiedAwareness();

        if (ai.Target != target)
            ai.TargetHandler.TrySetTarget(target);

        StopAgent();
        HoldDetectedPosition();
        ForceAnimation(detectedHash);

        Log("BOSS SEES PLAYER - DETECTED ANIMATION");
    }

    private void UpdateDetection()
    {
        HoldDetectedPosition();
        bool visible = CanContinuePlayerWindup();
        if (!visible) detectionLostSight = true;
        // Do not mistake the outgoing previous instance for a completed replay.
        if (Time.frameCount == detectedStartedFrame) return;
        if (Time.timeScale <= 0f) { detectedLastProgressAt = Time.unscaledTime; return; }
        bool statePresent = TryGetDetectionProgress(bossAnimator, detectedHash, out float progress);
        if (statePresent && progress > detectedLastNormalized + 0.0001f)
        {
            detectedLastNormalized = progress;
            detectedLastProgressAt = Time.unscaledTime;
        }
        if (!statePresent || progress < 0.999f)
        {
            if (Time.unscaledTime - detectedLastProgressAt > Mathf.Max(2f, detectedDuration))
            {
                Debug.LogWarning("[Boss Detection] Detected missing, interrupted, or stalled. Check Animator transitions and speed; retrying sight instead of charging.", this);
                BeginPlayerRedetectionSearch();
            }
            return;
        }
        if (detectionLostSight || !visible)
        {
            Log("DETECTED FINISHED - LOS WAS LOST; REACQUIRING BEFORE CHARGE");
            BeginPlayerRedetectionSearch();
            return;
        }
        mode = BossMode.Chase;
        playerDetectionReadyForCharge = true;
        ReleaseDetectedPositionForCharge();
        nextChasePath = 0f;
        // Keep the agent stopped until the charge script takes over this frame.
        StopAgent();
        currentAnimationHash = 0;
        Log("DETECTED FINISHED - PLAYER VISIBLE, CHARGE AUTHORIZED");
    }

    // Called only after a full anticipation clip has played (or a broken
    // animator was detected). Investigate the last actually seen location.
    public void BeginPlayerRedetectionSearch()
    {
        playerDetectionReadyForCharge = false;
        playerRedetectionPending = true;
        priorityEnemy = null;
        ReleaseDetectedPositionForCharge();
        if (ai != null && ai.Target != null) ai.TargetHandler.TrySetTarget(null);
        trackedPlayer = null;
        hasPartialVisualContact = partialVisualInvestigationStarted = false;
        pendingPostChargeSuspicion = pendingPostChargeSearch = false;
        if (AgentReady())
        {
            agent.updatePosition = true;
            agent.updateRotation = false;
            agent.isStopped = false;
        }
        mode = BossMode.Search;
        if (!BeginMovementInvestigation(lastKnownPlayerPosition)) StartSearch(lastKnownPlayerPosition);
        investigatingVisualClue = true;
        if (TryGetVisiblePlayer(out Transform seen)) DetectPlayer(seen);
        else Log("BOSS MOVING TO LAST SEEN PLAYER POSITION - REDETECTION REQUIRED");
    }

    private void UpdateChasing()
    {
        if (trackedPlayer == null)
        {
            StartSearch(lastKnownPlayerPosition);
            return;
        }

        if (Time.time - lastPlayerSeenTime > losePlayerAfter)
        {
            ClearChaseCover();
            LosePlayer();
            return;
        }

        // Never attack cover while chasing a visible target.
        if (chaseObstacleCover != null) ClearChaseCover();

        float distanceToPlayer = Vector3.Distance(transform.position, trackedPlayer.position);
        if (distanceToPlayer <= meleeRange &&
            Time.time - lastPlayerSeenTime <= 0.25f &&
            Time.time >= nextMeleeTime)
        {
            BeginMelee();
            return;
        }
        if (!AgentReady())
            return;

        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.stoppingDistance = Mathf.Min(chaseStoppingDistance, meleeRange * 0.4f);

        if (Time.time >= nextChasePath)
        {
            agent.SetDestination(trackedPlayer.position);
            nextChasePath =
                Time.time + chasePathRefreshInterval;
        }

        FaceMovementDirection();
    }

    private void ClearChaseCover()
    {
        chaseObstacleCover = null;
        if (mode == BossMode.Chase)
        {
            currentCover = null;
            attackingCover = false;
            hasLockedCoverRotation = false;
            currentAnimationHash = 0;
        }
    }

    private void LosePlayer()
    {
        if (ai.Target != null)
            ai.TargetHandler.TrySetTarget(null);

        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;

        lostPlayerCoverSearch = true;

        // Losing full detection starts a search, but visual certainty
        // now decays rather than remaining permanently full.
        suspicion = Mathf.Min(suspicion, 0.8f);
        UpdateUnifiedAwareness();
        StartSearch(lastKnownPlayerPosition);

        Log(
            "BOSS LOST SIGHT - SEARCHING LAST SEEN POSITION"
        );
    }

    // =====================================================
    // SEARCH
    // =====================================================

    private void StartSearch(Vector3 center)
    {
        if (mode == BossMode.Cover)
            InterruptCoverAttack();

        CancelSearch();
        inspectedCovers.Clear();
        failedCoverApproaches.Clear();
        investigationCoverTargets = 0;
        lostPlayerCoverSearch = true;
        soundLocation = center; // snapshot clue, never track player magically
        searchStartTime = Time.time;
        searchDeadline = Time.time + Mathf.Max(searchDuration, 12f) + wallCheckExtraTime;
        nextInvestigationAllowed = Time.time + searchRestartCooldown;
        completedSearchStops = 0;
        wallChecksCompleted = 0;
        clearingNearbyCover = false;
        coverSweepCompleted = false;
        searchPaused = false;
        searchPauseHasScan = false;
        hasSearchPoint = false;
        attackingCover = false;
        hasLockedCoverRotation = false;
        currentCover = null;
        investigationCover = null;
        coverApproachValid = false;
        nextInvestigationCoverTime = Time.time;
        perimeterWaypointIndex = 0;
        perimeterDestinationFailures = 0;
        perimeterHasDestination = false;
        perimeterPointStartedAt = Time.time;
        extendedSearchPoints.Clear();
        deferredCoverThisPass.Clear();
        extendedWaypointIndex = 0;
        extendedPassesFinished = 0;
        extendedStopsVisited = 0;
        coverSweepPassesFinished = 0;
        coversDestroyedThisSearch = 0;
        finalCoverRechecks = 0;
        coverStallAttempts = 0;
        extendedHasDestination = false;
        extendedLooking = false;
        extendedRetries = 0;

        BuildWallCheckPoints(center);
        perimeterChecksPlanned = wallCheckPoints.Count;
        investigationSearchPhase = circleWallBeforeCover && perimeterChecksPlanned > 0
            ? InvestigationSearchPhase.Perimeter
            : InvestigationSearchPhase.CoverSweep;

        mode = BossMode.Search;
        currentAnimationHash = 0;
        StopAgent();
        Log("BOSS SEARCH: " + center + " | ordered wall points=" +
            perimeterChecksPlanned + " | stage=" + investigationSearchPhase);
    }

    private void UpdateSearching()
    {
        if (!AgentReady()) return;

        // Vision is processed at the start of Update and can supersede search.
        // A useful, newly located sound is handled by CheckPlayerMovement.
        if (hadVisualContactLastScan) return;

        switch (investigationSearchPhase)
        {
            case InvestigationSearchPhase.Perimeter:
                UpdateOrderedPerimeter();
                break;
            case InvestigationSearchPhase.CoverSweep:
                UpdateCommittedCoverSweep();
                break;
            case InvestigationSearchPhase.AreaSweep:
                UpdateExtendedAreaSweep();
                break;
            case InvestigationSearchPhase.FinalScan:
                StopAgent();
                if (UpdateInvestigationScan() || Time.time >= finalScanDeadline)
                    FinishSearch();
                break;
        }
    }

    // Follow the perimeter in its chosen clockwise or anticlockwise order.
    // A waypoint is not a reason to idle or randomly select another direction.
    private void UpdateOrderedPerimeter()
    {
        if (perimeterWaypointIndex >= wallCheckPoints.Count)
        {
            BeginCommittedCoverSweep();
            return;
        }

        Vector3 target = wallCheckPoints[perimeterWaypointIndex];
        Vector3 remaining = target - transform.position;
        remaining.y = 0f;
        float arrival = Mathf.Max(0.35f, perimeterWaypointArrival);
        if (perimeterPointStartedAt <= 0f) perimeterPointStartedAt = Time.time;
        if (Time.time - perimeterPointStartedAt >
            Mathf.Max(8f, perimeterAbsoluteTimeout))
        {
            Log("PERIMETER corner exceeded time budget - moving to next waypoint");
            AdvancePerimeterWaypoint();
            return;
        }

        if (!perimeterHasDestination)
        {
            if (remaining.magnitude <= arrival || TrySetPerimeterDestination(target))
            {
                if (remaining.magnitude > arrival) return;
            }
            else
            {
                Log("PERIMETER point unreachable: " + perimeterWaypointIndex);
                AdvancePerimeterWaypoint();
                return;
            }
        }

        if (!agent.pathPending &&
            (remaining.magnitude <= arrival ||
             (perimeterHasDestination && agent.hasPath &&
              FlatDistance(agent.destination, target) < 0.9f &&
              agent.remainingDistance <= arrival)))
        {
            AdvancePerimeterWaypoint();
            return;
        }

        if (!agent.pathPending &&
            (agent.pathStatus != NavMeshPathStatus.PathComplete || !agent.hasPath ||
             Time.time >= perimeterWaypointDeadline))
        {
            RetryPerimeterWaypoint();
            return;
        }

        if (!agent.pathPending && Time.time >= perimeterProgressCheckAt)
        {
            Vector3 diff = transform.position - perimeterProgressPosition;
            diff.y = 0f;
            perimeterProgressPosition = transform.position;
            perimeterProgressCheckAt = Time.time + perimeterStuckCheck;
            if (diff.magnitude < perimeterMinimumProgress)
            {
                RetryPerimeterWaypoint();
                return;
            }
            perimeterDestinationFailures = 0;
        }

        agent.isStopped = false;
        agent.autoBraking = false;
        FaceMovementDirection();
    }

    private bool TrySetPerimeterDestination(Vector3 destination)
    {
        if (!AgentReady()) return false;
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(destination, path) ||
            path.status != NavMeshPathStatus.PathComplete) return false;

        agent.autoBraking = false;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = Mathf.Max(0.2f, perimeterWaypointArrival * 0.75f);
        agent.isStopped = false;
        if (!agent.SetPath(path)) return false;

        float pathLength = PathDistance(path);
        if (float.IsInfinity(pathLength))
            pathLength = Vector3.Distance(transform.position, destination);
        perimeterWaypointDeadline = Time.time + Mathf.Max(7f,
            pathLength / Mathf.Max(0.5f, investigateSpeed) * 2.8f + 4f);
        perimeterProgressPosition = transform.position;
        perimeterProgressCheckAt = Time.time + perimeterStuckCheck;
        perimeterHasDestination = true;
        return true;
    }

    private void RetryPerimeterWaypoint()
    {
        if (perimeterWaypointIndex >= wallCheckPoints.Count) return;
        perimeterDestinationFailures++;
        if (perimeterDestinationFailures <= perimeterMaximumRepaths)
        {
            Vector3 nearby;
            Vector3 current = wallCheckPoints[perimeterWaypointIndex];
            if (TryFindAlternateInvestigationWaypoint(current, out nearby) &&
                TrySetPerimeterDestination(nearby))
            {
                wallCheckPoints[perimeterWaypointIndex] = nearby;
                Log("PERIMETER recovering around blocked corner " + perimeterWaypointIndex);
                return;
            }
            if (perimeterDestinationFailures == 1 &&
                TrySetPerimeterDestination(current)) return;
        }
        Log("PERIMETER skipped stalled point " + perimeterWaypointIndex);
        AdvancePerimeterWaypoint();
    }

    private void AdvancePerimeterWaypoint()
    {
        if (perimeterWaypointIndex < wallCheckPoints.Count)
        {
            Vector3 finished = wallCheckPoints[perimeterWaypointIndex];
            visitedPoints.Add(finished);
            wallChecksCompleted++;
            completedSearchStops++;
        }

        perimeterWaypointIndex++;
        perimeterDestinationFailures = 0;
        perimeterHasDestination = false;
        perimeterPointStartedAt = Time.time;

        if (perimeterWaypointIndex >= wallCheckPoints.Count)
        {
            BeginCommittedCoverSweep();
            return;
        }

        // Next destination is set directly; no StopAgent, no multi-second pause.
        TrySetPerimeterDestination(wallCheckPoints[perimeterWaypointIndex]);
    }

    private void BeginCommittedCoverSweep()
    {
        investigationSearchPhase = InvestigationSearchPhase.CoverSweep;
        clearingNearbyCover = true;
        agent.autoBraking = originalAutoBraking;
        StopAgent();
        perimeterHasDestination = false;
        nextInvestigationCoverTime = Time.time;
        deferredCoverThisPass.Clear(); // An alternate approach can work after circling the area.
        Log("SEARCH COVER PASS " + (coverSweepPassesFinished + 1) +
            " - attempting all nearby reachable Cover");
        if (debugCoverSweep) CountUnclearedCoverInRadius();
    }

    private void UpdateCommittedCoverSweep()
    {
        // Once the perimeter is done, target every reachable Cover near the
        // ORIGINAL clue. Do not waste time on random search points.
        if (investigationCover != null)
        {
            if (Time.time >= coverTargetDeadline ||
                investigationCover.IsDestroyed ||
                !investigationCover.isActiveAndEnabled ||
                !investigationCover.gameObject.activeInHierarchy)
            {
                EndInvestigationCoverAttack();
                return;
            }
            currentCover = investigationCover;
            UpdateCoverHunting(); // CrossFade -> "Attack"; repeated DamageCover()
            return;
        }

        if (attackingCover && currentCover == null)
        {
            EndInvestigationCoverAttack();
            return;
        }
        if (Time.time < nextInvestigationCoverTime) return;

        if ((clearAllNearbyCover || investigationCoverTargets < maximumCoverTargetsPerInvestigation) &&
            TrySelectNearbyInvestigationCover())
        {
            investigationCoverTargets++;
            UpdateCoverHunting();
            return;
        }

        // No attackable target FROM THIS VIEWPOINT. Do not mistake this for a
        // completed search: move to a new side of the room, then try again.
        clearingNearbyCover = false;
        coverSweepPassesFinished++;
        BeginExtendedAreaSweep();
    }

    // Leaving one hiding position is not enough. Search the wider area on
    // walkable NavMesh viewpoints, then recheck Cover from those new angles.
    private void BeginExtendedAreaSweep()
    {
        bool enoughTime = Time.time - searchStartTime >= minimumInvestigationSearchSeconds;
        bool enoughStops = extendedStopsVisited >= minimumExtendedViewpointsVisited;
        bool enoughPasses = extendedPassesFinished >= minimumAreaSearchPasses;
        bool enoughCoverSweeps = coverSweepPassesFinished >= minimumCoverSweepPasses;
        bool timeLimit = Time.time - searchStartTime >= maximumInvestigationSearchSeconds;

        // Do not use a time limit as a reason to abandon still-standing cover.
        // A HARD upper bound prevents an impossible NavMesh/cover setup from
        // keeping this single investigation alive forever.
        bool hardCoverLimit = Time.time - searchStartTime >=
            maximumInvestigationSearchSeconds + coverClearExtraTime;
        int remaining = CountUnclearedCoverInRadius();
        if (remaining > 0 && !hardCoverLimit && finalCoverRechecks < 3 &&
            (timeLimit || (enoughTime && enoughStops && enoughPasses && enoughCoverSweeps)))
        {
            finalCoverRechecks++;
            // Another deliberate pass after checking new angles. This bypasses
            // the ordinary search duration but does not repeat an attack on
            // something already destroyed or proven unreachable.
            BeginCommittedCoverSweep();
            return;
        }
        if (timeLimit || hardCoverLimit ||
            (enoughTime && enoughStops && enoughPasses && enoughCoverSweeps && remaining == 0))
        {
            BeginFinalInvestigationScan();
            return;
        }

        if (extendedPassesFinished >= maximumExtendedAreaPasses)
        {
            // One more cover pass from the present side of the room, but
            // don't restart forever if the remainder is inaccessible.
            if (remaining > 0 && !hardCoverLimit && finalCoverRechecks < 3)
            {
                finalCoverRechecks++;
                BeginCommittedCoverSweep();
            }
            else
                BeginFinalInvestigationScan();
            return;
        }

        BuildExtendedSearchPoints();
        if (extendedSearchPoints.Count == 0)
        {
            // If no new viewpoints exist, still give the boss another chance
            // to approach other cover; do not blindly end after one break.
            if (coverSweepPassesFinished < minimumCoverSweepPasses &&
                finalCoverRechecks < 3)
            {
                finalCoverRechecks++;
                BeginCommittedCoverSweep();
            }
            else
                BeginFinalInvestigationScan();
            return;
        }

        investigationSearchPhase = InvestigationSearchPhase.AreaSweep;
        extendedWaypointIndex = 0;
        extendedHasDestination = false;
        extendedLooking = false;
        extendedRetries = 0;
        agent.autoBraking = false;
        Log("WIDER SEARCH PASS " + (extendedPassesFinished + 1) +
            " with " + extendedSearchPoints.Count + " viewpoints");
    }

    private void BuildExtendedSearchPoints()
    {
        extendedSearchPoints.Clear();
        if (!AgentReady()) return;
        int perRing = Mathf.Max(3, Mathf.CeilToInt(extendedAreaViewpoints / 2f));
        float radius = Mathf.Max(4f, extendedAreaSearchRadius);
        float offset = (extendedPassesFinished % 2 == 0 ? 0.25f : 0.75f) / perRing;
        for (int ring = 0; ring < 2; ring++)
        {
            float ringRadius = radius * (ring == 0 ? 0.48f : 0.95f);
            for (int i = 0; i < perRing; i++)
            {
                // Outer ring reverses direction to avoid zigzagging across
                // the search area every time a viewpoint changes.
                int idx = ring == 0 ? i : perRing - 1 - i;
                float angle = 2f * Mathf.PI * ((float)idx / perRing + offset);
                Vector3 guess = soundLocation + new Vector3(
                    Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(guess, out hit, 2.5f, agent.areaMask))
                    continue;
                Vector3 snapped = hit.position - guess;
                snapped.y = 0f;
                if (snapped.magnitude > 2.6f) continue;
                if (Vector3.Distance(hit.position, transform.position) < 1.1f)
                    continue;
                if (extendedSearchPoints.Exists(p =>
                    Vector3.Distance(p, hit.position) < 2.3f)) continue;
                // Do not repeat an identical viewpoint we just cleared.
                if (visitedPoints.Exists(p =>
                    Vector3.Distance(p, hit.position) < 1.5f)) continue;
                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                extendedSearchPoints.Add(hit.position);
            }
        }

        // Small maps or narrow hallways may not admit circular samples.
        // Try viewpoints closer to the clue before abandoning the search.
        if (extendedSearchPoints.Count < 2)
        {
            for (int i = 0; i < 12; i++)
            {
                float theta = i * Mathf.PI * 2f / 12f;
                Vector3 guess = soundLocation + new Vector3(
                    Mathf.Cos(theta), 0f, Mathf.Sin(theta)) * 4.5f;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(guess, out hit, 1.5f, agent.areaMask)) continue;
                if (Vector3.Distance(hit.position, transform.position) < 1.0f ||
                    extendedSearchPoints.Exists(p => Vector3.Distance(p, hit.position) < 2f))
                    continue;
                NavMeshPath path = new NavMeshPath();
                if (agent.CalculatePath(hit.position, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                    extendedSearchPoints.Add(hit.position);
            }
        }
    }

    private bool TrySetExtendedDestination(Vector3 target)
    {
        if (!AgentReady()) return false;
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(target, path) ||
            path.status != NavMeshPathStatus.PathComplete) return false;
        agent.isStopped = false;
        agent.autoBraking = false;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = Mathf.Min(0.6f, extendedWaypointArrival * 0.65f);
        if (!agent.SetPath(path)) return false;
        float distance = PathDistance(path);
        if (float.IsInfinity(distance))
            distance = Vector3.Distance(transform.position, target);
        extendedDestinationDeadline = Time.time + Mathf.Max(extendedWaypointTimeout,
            distance / Mathf.Max(0.5f, investigateSpeed) * 2.5f + 5f);
        extendedProgressAt = Time.time + 2.5f;
        extendedProgressPosition = transform.position;
        extendedHasDestination = true;
        return true;
    }

    private void UpdateExtendedAreaSweep()
    {
        if (!AgentReady()) return;
        if (extendedLooking)
        {
            StopAgent();
            UpdateInvestigationScan();
            if (Time.time >= extendedLookUntil)
            {
                extendedLooking = false;
                extendedHasDestination = false;
            }
            return;
        }
        if (extendedWaypointIndex >= extendedSearchPoints.Count)
        {
            ExtendedAreaPassComplete();
            return;
        }
        Vector3 target = extendedSearchPoints[extendedWaypointIndex];
        Vector3 delta = target - transform.position;
        delta.y = 0f;
        if (!extendedHasDestination)
        {
            if (delta.magnitude > extendedWaypointArrival &&
                !TrySetExtendedDestination(target))
            {
                AdvanceExtendedWaypoint(false);
                return;
            }
            if (delta.magnitude > extendedWaypointArrival) return;
        }
        if (!agent.pathPending &&
            (delta.magnitude <= extendedWaypointArrival ||
             (extendedHasDestination && agent.hasPath &&
              FlatDistance(agent.destination, target) < 1f &&
              agent.remainingDistance <= extendedWaypointArrival)))
        {
            AdvanceExtendedWaypoint(true);
            return;
        }
        if (!agent.pathPending &&
            (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete ||
             Time.time >= extendedDestinationDeadline))
        {
            RetryExtendedWaypoint();
            return;
        }
        if (!agent.pathPending && Time.time >= extendedProgressAt)
        {
            Vector3 diff = transform.position - extendedProgressPosition;
            diff.y = 0f;
            extendedProgressAt = Time.time + 2.5f;
            extendedProgressPosition = transform.position;
            if (diff.magnitude < 0.3f)
            {
                RetryExtendedWaypoint();
                return;
            }
            extendedRetries = 0;
        }
        agent.isStopped = false;
        FaceMovementDirection();
    }

    private void RetryExtendedWaypoint()
    {
        if (extendedWaypointIndex >= extendedSearchPoints.Count) return;
        extendedRetries++;
        if (extendedRetries <= 2)
        {
            Vector3 original = extendedSearchPoints[extendedWaypointIndex];
            Vector3 alternate;
            if (TryFindAlternateInvestigationWaypoint(original, out alternate) &&
                TrySetExtendedDestination(alternate))
            {
                extendedSearchPoints[extendedWaypointIndex] = alternate;
                return;
            }
            if (extendedRetries == 1 && TrySetExtendedDestination(original)) return;
        }
        AdvanceExtendedWaypoint(false);
    }

    private void AdvanceExtendedWaypoint(bool reached)
    {
        if (reached && extendedWaypointIndex < extendedSearchPoints.Count)
        {
            Vector3 reachedPosition = extendedSearchPoints[extendedWaypointIndex];
            extendedStopsVisited++;
            visitedPoints.Add(reachedPosition);
        }
        extendedWaypointIndex++;
        extendedHasDestination = false;
        extendedRetries = 0;
        if (extendedWaypointIndex >= extendedSearchPoints.Count)
        {
            ExtendedAreaPassComplete();
            return;
        }
        if (reached && extendedWaypointIndex % 4 == 0 &&
            extendedWaypointLookSeconds > 0.02f)
        {
            extendedLooking = true;
            extendedLookUntil = Time.time + extendedWaypointLookSeconds;
            BeginInvestigationScan(soundLocation);
            StopAgent();
            return;
        }
        // Set the next path immediately; no unnecessary pause at each corner.
        TrySetExtendedDestination(extendedSearchPoints[extendedWaypointIndex]);
    }

    private void ExtendedAreaPassComplete()
    {
        extendedPassesFinished++;
        extendedHasDestination = false;
        extendedLooking = false;
        if (AgentReady()) agent.autoBraking = originalAutoBraking;
        // Fresh angle, fresh attempt at cover. Covers skipped on one side can
        // now be reached from the other side of a wall.
        BeginCommittedCoverSweep();
    }

    private void BeginFinalInvestigationScan()
    {
        // Even after completing area passes, try to finish nearby accessible
        // cover rather than giving up just because the clock ran out.
        if (clearAllNearbyCover && finalCoverRechecks < 3 &&
            Time.time - searchStartTime < maximumInvestigationSearchSeconds + coverClearExtraTime &&
            CountUnclearedCoverInRadius() > 0)
        {
            finalCoverRechecks++;
            BeginCommittedCoverSweep();
            return;
        }
        investigationSearchPhase = InvestigationSearchPhase.FinalScan;
        finalScanDeadline = Time.time + Mathf.Max(3f, finalInvestigationScanSeconds);
        BeginInvestigationScan(soundLocation);
        StopAgent();
        Log("SEARCH AREA AND COVER PASSES DONE - FINAL VISUAL SWEEP");
    }

    private void MarkSearchPointVisited()
    {
        if (!visitedPoints.Exists(p => Vector3.Distance(p, searchPoint) < 0.8f))
            visitedPoints.Add(searchPoint);
        if (wallCheckPoints.Exists(p => Vector3.Distance(p, searchPoint) < 0.8f))
            wallChecksCompleted++;
    }

    private void EndInvestigationCoverAttack()
    {
        // A timed-out cover was inspected and should not trap the search in
        // a retry loop. When interrupted by new sound/sight, do not mark it:
        // the boss should be free to return to it after losing the clue.
        if (investigationCover != null && !hadVisualContactLastScan)
        {
            if (investigationCover.IsDestroyed ||
                !investigationCover.isActiveAndEnabled ||
                !investigationCover.gameObject.activeInHierarchy)
            {
                if (inspectedCovers.Add(investigationCover))
                    coversDestroyedThisSearch++;
            }
            else
            {
                // Do not retry the same inaccessible object every frame.
                // Approach it again on a later cover pass, from another side.
                deferredCoverThisPass.Add(investigationCover);
                int attempts;
                failedCoverApproaches.TryGetValue(investigationCover, out attempts);
                attempts++;
                failedCoverApproaches[investigationCover] = attempts;
                if (attempts >= Mathf.Max(6, maximumCoverApproachAttempts))
                {
                    inspectedCovers.Add(investigationCover);
                    Log("COVER EXHAUSTED ALL APPROACH ATTEMPTS: " + investigationCover.name);
                }
            }
        }
        investigationCover = null;
        failedCoverApproachOwner = null;
        hasFailedCoverApproach = false;
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;
        coverApproachValid = false;
        hasSearchPoint = false;
        nextCoverPath = 0f;
        nextInvestigationCoverTime = Time.time + chainedCoverDelay;
        if (AgentReady()) agent.isStopped = true;
        searchPaused = false;
        searchPauseHasScan = false;
        currentAnimationHash = 0;
        Log("BOSS FINISHED COVER - CONTINUING NEARBY COVER SWEEP");
    }

    // Find a walkable point on ANY side of a cover collider. Sampling several
    // offsets around its actual surface works better than sampling its center
    // (which is often inside a wall / outside the NavMesh).
    private bool TryGetCoverApproach(Cover cover, out Vector3 bestPoint,
        out float bestPathDistance)
    {
        bestPoint = Vector3.zero;
        bestPathDistance = float.PositiveInfinity;
        if (!AgentReady() || cover == null) return false;

        Collider collider = cover.GetComponentInChildren<Collider>();
        Vector3 center = collider != null ? collider.bounds.center : cover.transform.position;
        Vector3 nearest = collider != null ? collider.ClosestPoint(transform.position) : center;
        Vector3 toSurface = nearest - transform.position;
        toSurface.y = 0f;
        if (toSurface.magnitude <= attackDistance + coverHitReachTolerance)
        {
            bestPoint = transform.position;
            bestPathDistance = 0f;
            return true;
        }

        const int samples = 24;
        float[] offsets = {
            Mathf.Max(agent.radius + 0.1f, 0.35f),
            Mathf.Max(0.55f, attackDistance * 0.65f),
            Mathf.Max(0.65f, attackDistance * 0.9f)
        };
        Vector3 towardBoss = transform.position - center;
        towardBoss.y = 0f;
        float initialAngle = towardBoss.sqrMagnitude > 0.01f
            ? Mathf.Atan2(towardBoss.z, towardBoss.x) : 0f;
        for (int i = 0; i < samples; i++)
        {
            // Nearest side, alternating clockwise and anticlockwise.
            int step = i == 0 ? 0 : ((i + 1) / 2) * (i % 2 == 1 ? 1 : -1);
            float angle = initialAngle + (Mathf.PI * 2f * step) / samples;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 far = center + radial * (collider != null
                ? Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.z) + attackDistance + 2f
                : attackDistance + 2f);
            Vector3 surface = collider != null ? collider.ClosestPoint(far) : center;
            for (int j = 0; j < offsets.Length; j++)
            {
                Vector3 test = surface + radial * offsets[j];
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(test, out hit, 1.15f, agent.areaMask)) continue;
                if (hasFailedCoverApproach && failedCoverApproachOwner == cover &&
                    FlatDistance(hit.position, failedCoverApproachPoint) < 1.55f)
                    continue;
                Vector3 contact = collider != null ? collider.ClosestPoint(hit.position) : center;
                Vector3 gap = contact - hit.position;
                gap.y = 0f;
                // The destination must let us ACTUALLY reach attack range.
                if (gap.magnitude > attackDistance + coverHitReachTolerance - 0.05f) continue;
                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                float length = PathDistance(path);
                if (float.IsInfinity(length))
                    length = Vector3.Distance(transform.position, hit.position);
                if (length < bestPathDistance)
                {
                    bestPathDistance = length;
                    bestPoint = hit.position;
                    // A nearby valid attack position is sufficient. Avoid
                    // dozens of redundant NavMesh path calculations per cover.
                    if (bestPathDistance <= 2.5f) return true;
                }
            }
        }
        return !float.IsInfinity(bestPathDistance);
    }

    // This inventory is independent of "cover targets attempted". Breaking
    // one crate must never be treated as clearing the whole investigation area.
    private int CountUnclearedCoverInRadius()
    {
        // Searches call this at phase transitions, not every frame.
        int count = 0;
        float radius = Mathf.Max(nearbyCoverSearchRadius, minimumCoverClearRadius);
        foreach (Cover candidate in FindObjectsOfType<Cover>())
        {
            if (candidate == null || candidate.IsDestroyed ||
                !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy ||
                inspectedCovers.Contains(candidate)) continue;
            Collider collider = candidate.GetComponentInChildren<Collider>();
            Vector3 point = collider != null
                ? collider.ClosestPoint(soundLocation) : candidate.transform.position;
            point.y = soundLocation.y;
            if ((point - soundLocation).sqrMagnitude <= radius * radius)
                count++;
        }
        if (debugCoverSweep)
            Debug.Log("[BOSS COVER] still standing and not exhausted: " + count +
                " | destroyed=" + coversDestroyedThisSearch +
                " | attempted=" + investigationCoverTargets, this);
        return count;
    }

    // Select closest suspicious cover first, then break everything reachable
    // within the extended search area. Expensive NavMesh checks are run only
    // on candidates in priority order (rather than on every cover every tick).
    private bool TrySelectNearbyInvestigationCover()
    {
        Cover[] all = FindObjectsOfType<Cover>();
        var candidates = new List<Cover>();
        float mainRadius = Mathf.Max(nearbyCoverSearchRadius, minimumCoverClearRadius);
        foreach (Cover candidate in all)
        {
            if (candidate == null || candidate.IsDestroyed ||
                !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy ||
                inspectedCovers.Contains(candidate) ||
                deferredCoverThisPass.Contains(candidate)) continue;
            Collider collider = candidate.GetComponentInChildren<Collider>();
            Vector3 contactToClue = collider != null
                ? collider.ClosestPoint(soundLocation) : candidate.transform.position;
            Vector3 fromClue = contactToClue - soundLocation;
            fromClue.y = 0f;
            if (fromClue.magnitude > mainRadius) continue;
            candidates.Add(candidate);
        }

        candidates.Sort((a, b) => CoverSuspicionScore(a).CompareTo(CoverSuspicionScore(b)));
        foreach (Cover candidate in candidates)
        {
            Vector3 approach;
            float pathLength;
            if (!TryGetCoverApproach(candidate, out approach, out pathLength))
            {
                // This side cannot be reached now. Revisit after moving to
                // another area viewpoint; only give up after several attempts.
                deferredCoverThisPass.Add(candidate);
                int failed;
                failedCoverApproaches.TryGetValue(candidate, out failed);
                failed++;
                failedCoverApproaches[candidate] = failed;
                if (failed >= Mathf.Max(6, maximumCoverApproachAttempts))
                    inspectedCovers.Add(candidate);
                if (logEvents) Log("COVER REQUIRES ALTERNATE APPROACH: " + candidate.name);
                continue;
            }
            investigationCover = candidate;
            failedCoverApproachOwner = null;
            hasFailedCoverApproach = false;
            investigationCoverStartTime = Time.time;
            coverProgressCheckAt = Time.time + coverStuckCheckSeconds;
            coverProgressPosition = transform.position;
            coverStallAttempts = 0;
            // Include time needed to get around the obstacle before the attack.
            coverTargetDeadline = Time.time + Mathf.Max(
                Mathf.Max(25f, investigationCoverTimeout),
                pathLength / Mathf.Max(0.5f, investigateSpeed) * 2.2f + 14f);
            currentCover = candidate;
            coverApproachPoint = approach;
            coverApproachValid = true;
            hasSearchPoint = false;
            nextCoverPath = 0f;
            Log("BOSS BREAKING COVER: " + candidate.name);
            return true;
        }
        return false;
    }

    private float CoverSuspicionScore(Cover cover)
    {
        if (cover == null) return float.PositiveInfinity;
        Collider collider = cover.GetComponentInChildren<Collider>();
        Vector3 clue = collider != null
            ? collider.ClosestPoint(soundLocation) : cover.transform.position;
        Vector3 body = collider != null
            ? collider.ClosestPoint(transform.position) : cover.transform.position;
        Vector3 fromClue = clue - soundLocation;
        Vector3 fromBoss = body - transform.position;
        fromClue.y = fromBoss.y = 0f;
        int retries;
        failedCoverApproaches.TryGetValue(cover, out retries);
        return fromClue.magnitude * 0.55f + fromBoss.magnitude * 0.45f + retries * 5f;
    }

    // Surface-sampled perimeter: unlike AABB corners, this follows the actual
    // collider silhouette, including rotated, concave and L-shaped geometry.
    // Every candidate must be on the NavMesh and reachable by a complete path.
    [SerializeField, Range(16, 96)] private int perimeterSurfaceSamples = 48;
    [SerializeField, Min(0.25f)] private float perimeterSampleSpacing = 3.25f;
    [SerializeField, Min(0.25f)] private float perimeterMaxSampleDistance = 2.0f;

    private void BuildWallCheckPoints(Vector3 center)
    {
        wallCheckPoints.Clear();
        identifiedWall = null;
        if (!AgentReady()) return;

        float lowestScore = float.PositiveInfinity;
        foreach (Collider col in Physics.OverlapSphere(center, perimeterSearchRadius,
                     searchObstructionLayers, QueryTriggerInteraction.Ignore))
        {
            if (col == null || col.isTrigger || col.transform.IsChildOf(transform) ||
                (player != null && (col.transform == player || col.transform.IsChildOf(player))) ||
                (trackedPlayer != null && col.transform.IsChildOf(trackedPlayer)))
                continue;
            Bounds b = col.bounds;
            if (b.size.y < 1.1f || Mathf.Max(b.size.x, b.size.z) < 1.2f)
                continue;
            // Prefer a permanent wall, but still permit a large piece of
            // destructible Cover to define the search perimeter.
            bool destructible = col.GetComponentInParent<Cover>() != null;
            float score = Vector3.Distance(center, col.ClosestPoint(center)) +
                (destructible ? 2.5f : 0f);
            if (score < lowestScore)
            {
                lowestScore = score;
                identifiedWall = col;
            }
        }

        var radialSamples = new List<Vector3>();
        float spacing = Mathf.Clamp(perimeterSampleSpacing, 1.2f, 2.75f);
        if (identifiedWall != null)
        {
            Bounds bounds = identifiedWall.bounds;
            Vector3 pivot = bounds.center;
            float clearance = Mathf.Max(perimeterClearance, agent.radius + 0.35f);
            float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude +
                clearance + Mathf.Max(3f, wallFlankOffset);
            int count = Mathf.Clamp(perimeterSurfaceSamples, 24, 96);
            // This loop is in angular order; after a starting point is chosen
            // we will retain that ordering rather than sort by distance.
            for (int i = 0; i < count; i++)
            {
                float angle = 2f * Mathf.PI * i / count;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 outside = pivot + outward * radius;
                RaycastHit hit;
                if (!identifiedWall.Raycast(new Ray(outside, -outward), out hit,
                    radius * 2.1f)) continue;
                Vector3 normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                if (normal.sqrMagnitude < 0.01f) normal = outward;
                normal.Normalize();
                Vector3 candidate = hit.point + normal * clearance;
                candidate.y = center.y;
                NavMeshHit nav;
                if (!NavMesh.SamplePosition(candidate, out nav,
                    Mathf.Max(1.4f, perimeterMaxSampleDistance), agent.areaMask))
                    continue;
                Vector3 horizontal = nav.position - candidate;
                horizontal.y = 0f;
                if (horizontal.magnitude > Mathf.Max(1.4f, perimeterMaxSampleDistance))
                    continue;
                if (radialSamples.Exists(p => Vector3.Distance(p, nav.position) < spacing))
                    continue;
                NavMeshPath toBoss = new NavMeshPath();
                if (!agent.CalculatePath(nav.position, toBoss) ||
                    toBoss.status != NavMeshPathStatus.PathComplete) continue;
                radialSamples.Add(nav.position);
            }
        }

        if (radialSamples.Count < 3)
        {
            // No suitable wall collider: walk an ordered ring of nearby
            // reachable viewpoints around the actual heard/seen clue.
            radialSamples.Clear();
            float ringRadius = Mathf.Max(3f, wallFlankOffset + 1.3f);
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * 2f / 16f;
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f,
                    Mathf.Sin(angle)) * ringRadius;
                NavMeshHit nav;
                if (!NavMesh.SamplePosition(p, out nav, 1.35f, agent.areaMask)) continue;
                if (radialSamples.Exists(v => Vector3.Distance(v, nav.position) < spacing * 0.75f))
                    continue;
                NavMeshPath path = new NavMeshPath();
                if (agent.CalculatePath(nav.position, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                    radialSamples.Add(nav.position);
            }
        }
        if (radialSamples.Count == 0) return;

        int start = 0;
        float bestPath = float.PositiveInfinity;
        for (int i = 0; i < radialSamples.Count; i++)
        {
            NavMeshPath navPath = new NavMeshPath();
            if (!agent.CalculatePath(radialSamples[i], navPath) ||
                navPath.status != NavMeshPathStatus.PathComplete) continue;
            float length = PathDistance(navPath);
            if (float.IsInfinity(length))
                length = Vector3.Distance(transform.position, radialSamples[i]);
            if (length < bestPath) { bestPath = length; start = i; }
        }

        int direction = 1;
        if (radialSamples.Count > 2)
        {
            int clockwise = (start + 1) % radialSamples.Count;
            int counterClockwise = (start - 1 + radialSamples.Count) % radialSamples.Count;
            float forwardCost = PointToPointPathDistance(radialSamples[start], radialSamples[clockwise]);
            float reverseCost = PointToPointPathDistance(radialSamples[start], radialSamples[counterClockwise]);
            if (reverseCost < forwardCost) direction = -1;
        }
        for (int i = 0; i < radialSamples.Count; i++)
        {
            int index = (start + direction * i + radialSamples.Count * (i + 1)) % radialSamples.Count;
            Vector3 point = radialSamples[index];
            if (wallCheckPoints.Count > 0 &&
                Vector3.Distance(wallCheckPoints[wallCheckPoints.Count - 1], point) < spacing * 0.6f)
                continue;
            if (wallCheckPoints.Count > 0 &&
                float.IsInfinity(PointToPointPathDistance(wallCheckPoints[wallCheckPoints.Count - 1], point)))
                continue;
            wallCheckPoints.Add(point);
        }
        // Close the circuit only if returning to its first waypoint is
        // naturally navigable, not if the path would be a major detour.
        if (wallCheckPoints.Count >= 4)
        {
            Vector3 last = wallCheckPoints[wallCheckPoints.Count - 1];
            Vector3 first = wallCheckPoints[0];
            float direct = Vector3.Distance(last, first);
            float route = PointToPointPathDistance(last, first);
            if (route <= Mathf.Max(6f, direct * 2.8f) && direct > spacing * 0.8f)
                wallCheckPoints.Add(first);
        }
    }

    private float PointToPointPathDistance(Vector3 from, Vector3 to)
    {
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, to, agent.areaMask, path) ||
            path.status != NavMeshPathStatus.PathComplete)
            return float.PositiveInfinity;
        float length = PathDistance(path);
        return float.IsInfinity(length) ? Vector3.Distance(from, to) : length;
    }

    private static float PathDistance(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2) return float.PositiveInfinity;
        float total = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            total += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return total;
    }

    private bool ChooseSearchPoint()
    {
        // Traverse perimeter sequentially; choosing the highest-scoring flank
        // each frame caused backtracking and getting stuck at the wall edge.
        foreach (Vector3 waypoint in wallCheckPoints)
        {
            if (visitedPoints.Exists(p => Vector3.Distance(p, waypoint) < 0.8f)) continue;
            NavMeshPath path = new NavMeshPath();
            if (!agent.CalculatePath(waypoint, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                visitedPoints.Add(waypoint);
                wallChecksCompleted++;
                continue;
            }
            searchPoint = waypoint;
            return true;
        }

        float progress = Mathf.Clamp01(
            (Time.time - searchStartTime) /
            Mathf.Max(1f, searchDuration)
        );

        float radius = Mathf.Lerp(
            searchRadius,
            maximumSearchRadius,
            progress
        );

        float bestScore = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < searchCandidates; i++)
        {
            Vector2 random =
                Random.insideUnitCircle * radius;

            Vector3 candidate =
                soundLocation +
                new Vector3(random.x, 0f, random.y);

            NavMeshHit hit;

            if (!NavMesh.SamplePosition(
                candidate,
                out hit,
                2f,
                NavMesh.AllAreas))
            {
                continue;
            }

            bool alreadyVisited = false;

            foreach (Vector3 visited in visitedPoints)
            {
                if (Vector3.Distance(
                    visited,
                    hit.position) < minimumPointSeparation)
                {
                    alreadyVisited = true;
                    break;
                }
            }

            if (alreadyVisited)
                continue;

            NavMeshPath path = new NavMeshPath();

            if (!agent.CalculatePath(hit.position, path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            Vector3 eye =
                transform.position + Vector3.up * eyeHeight;

            float score =
                Random.Range(0f, 0.15f) -
                Vector3.Distance(hit.position, soundLocation) * 0.08f -
                PathDistance(path) * 0.18f;

            if (IsBlocked(
                eye,
                hit.position + Vector3.up * targetHeight,
                searchObstructionLayers))
            {
                score += blindSpotPriority;
            }

            if (score > bestScore)
            {
                bestScore = score;
                searchPoint = hit.position;
                found = true;
            }
        }

        return found;
    }

    private void FinishSearch()
    {
        playerRedetectionPending = false;
        Log("BOSS SEARCH COMPLETE | elapsed=" +
            (Time.time - searchStartTime).ToString("F1") +
            "s | cover passes=" + coverSweepPassesFinished +
            " | area passes=" + extendedPassesFinished +
            " | viewpoints reached=" + extendedStopsVisited +
            " | cover targets approached=" + investigationCoverTargets +
            " | covers destroyed=" + coversDestroyedThisSearch +
            " | unbroken / not-exhausted cover=" + CountUnclearedCoverInRadius());
        mode = BossMode.Cover;
        investigatingVisualClue = false;
        lostPlayerCoverSearch = false;
        suspicion = 0f;
        // A completed search clears visual suspicion; fresh sound can
        // still keep the same Fantacode meter visible.
        UpdateUnifiedAwareness();
        hasPartialVisualContact = false;
        hadVisualContactLastScan = false;
        partialVisualInvestigationStarted = false;
        lastPartialVisualContactTime = float.NegativeInfinity;
        coverHuntActive = false;
        patrolScanning = false;
        nextPatrolScanTime = Time.time + patrolScanInterval;
        hasWanderDestination = false;
        trackedPlayer = null;

        CancelSearch();

        if (AgentReady())
        {
            agent.ResetPath();
            agent.isStopped = false;
            agent.speed = originalAgentSpeed;
            agent.stoppingDistance =
                originalStoppingDistance;
        }

        nextCoverSearch = 0f;
        nextInvestigationAllowed = Time.time + searchRestartCooldown;
        nextCoverDecision = Time.time + Random.Range(coverDecisionSeconds.x, coverDecisionSeconds.y);

        Log(
            "BOSS FINISHED SEARCH - RETURNING TO COVER"
        );
    }

    // =====================================================
    // WANDERING + OCCASIONAL COVER HUNTING
    // =====================================================

    private void UpdateWandering()
    {
        if (!RecoverEnemyNavMesh()) return;
        if (!agent.updatePosition)
        {
            agent.nextPosition = transform.position;
            agent.updatePosition = true;
        }

        // A patrol cover attack is a committed task, not a regular wander step.
        // Sight/hearing can interrupt it at any frame via the existing modes.
        if (patrolCoverHuntActive)
        {
            UpdatePatrolCoverAttack();
            return;
        }

        // Use the previously unused 30-60 second / 25% patrol decision fields.
        if (Time.time >= nextCoverDecision)
        {
            nextCoverDecision = Time.time + NextPatrolCoverInterval();
            if (UnityEngine.Random.value < coverDestructionChance &&
                TryBeginPatrolCoverAttack())
            {
                UpdatePatrolCoverAttack();
                return;
            }
        }
        coverHuntActive = false;
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;

        if (patrolScanning)
        {
            StopAgent();
            if (UpdateInvestigationScan() || Time.time >= patrolScanEndTime)
            {
                patrolScanning = false;
                hasWanderDestination = false;
            }
            return;
        }

        if (!continuousWandering && Time.time < wanderPauseUntil)
        {
            StopAgent();
            return;
        }

        if (hasWanderDestination)
        {
            if (patrolProgressAt < 0f || FlatDistance(transform.position, patrolProgressPosition) >= enemyMinimumProgress)
            {
                patrolProgressAt = Time.time;
                patrolProgressPosition = transform.position;
            }
            if (agent.pathPending)
            {
                if (patrolPendingAt < 0f) patrolPendingAt = Time.time;
            }
            else patrolPendingAt = -1f;
            bool pendingTimeout = patrolPendingAt >= 0f && Time.time - patrolPendingAt >= enemyPathPendingTimeout;
            bool stalled = !agent.pathPending && FlatDistance(transform.position, wanderDestination) > 1.2f &&
                (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete || Time.time - patrolProgressAt >= enemyStuckSeconds);
            if (pendingTimeout || stalled)
            {
                agent.ResetPath();
                hasWanderDestination = false;
                patrolProgressAt = patrolPendingAt = -1f;
                if (debugEnemyRecovery) Log("PATROL RECOVERY - selecting another waypoint");
            }
        }
        else patrolProgressAt = patrolPendingAt = -1f;

        bool arrived = hasWanderDestination && !agent.pathPending &&
            ((agent.hasPath && agent.remainingDistance <= 1.2f) ||
             Vector3.Distance(transform.position, wanderDestination) <= 1.2f);

        if (arrived && Time.time >= nextPatrolScanTime)
        {
            patrolScanning = true;
            patrolScanEndTime = Time.time + patrolScanDuration;
            nextPatrolScanTime = Time.time + patrolScanInterval;
            BeginInvestigationScan(transform.position + transform.forward * 3f);
            StopAgent();
            return;
        }

        if (arrived || !hasWanderDestination ||
            (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid))
        {
            hasWanderDestination = false;
            if (!continuousWandering && arrived)
            {
                wanderPauseUntil = Time.time + Random.Range(wanderPauseSeconds.x, wanderPauseSeconds.y);
                StopAgent();
                return;
            }

            for (int i = 0; i < 20; i++)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                // Patrol continues around its present region, rather than
                // permanently returning to the original spawn coordinates.
                Vector3 center = Vector3.Lerp(wanderOrigin, transform.position, 0.65f);
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(center + new Vector3(offset.x, 0f, offset.y),
                    out hit, 3f, agent.areaMask)) continue;
                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                if (Vector3.Distance(transform.position, hit.position) < 3f) continue;
                if (!agent.SetPath(path)) continue;
                wanderDestination = hit.position;
                hasWanderDestination = true;
                patrolProgressAt = Time.time;
                patrolProgressPosition = transform.position;
                patrolPendingAt = -1f;
                agent.isStopped = false;
                agent.speed = wanderSpeed;
                agent.stoppingDistance = 0.7f;
                break;
            }
        }

        if (hasWanderDestination)
        {
            agent.isStopped = false;
            agent.speed = wanderSpeed;
            agent.stoppingDistance = 0.7f;
            FaceMovementDirection();
        }
        else StopAgent();
    }

    private float NextPatrolCoverInterval()
    {
        float min = Mathf.Max(1f, Mathf.Min(coverDecisionSeconds.x, coverDecisionSeconds.y));
        float max = Mathf.Max(min, Mathf.Max(coverDecisionSeconds.x, coverDecisionSeconds.y));
        return UnityEngine.Random.Range(min, max);
    }

    // On a successful patrol decision, choose ONE reachable target from the
    // nearest few Cover objects. Never select a distant random object just
    // because it is within the large investigation sweep radius.
    private bool TryBeginPatrolCoverAttack()
    {
        if (!AgentReady() || ChargeBusy || hadVisualContactLastScan) return false;
        List<Cover> closest = new List<Cover>();
        List<float> distances = new List<float>();
        foreach (Cover cover in FindObjectsOfType<Cover>())
        {
            if (cover == null || cover.IsDestroyed ||
                !cover.isActiveAndEnabled || !cover.gameObject.activeInHierarchy)
                continue;

            Collider part = cover.GetComponentInChildren<Collider>();
            Vector3 surface = part != null
                ? part.ClosestPoint(transform.position) : cover.transform.position;
            float distance = FlatDistance(transform.position, surface);
            if (distance > randomCoverMaxDistance) continue;

            int insert = 0;
            while (insert < distances.Count && distances[insert] <= distance)
                insert++;
            closest.Insert(insert, cover);
            distances.Insert(insert, distance);
            if (closest.Count > Mathf.Max(1, patrolNearestCoverCandidates))
            {
                closest.RemoveAt(closest.Count - 1);
                distances.RemoveAt(distances.Count - 1);
            }
        }
        // Randomize among the nearest, but only pick reachable attack points.
        while (closest.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, closest.Count);
            Cover target = closest[index];
            closest.RemoveAt(index);
            Vector3 approach;
            float routeLength;
            if (target == null ||
                !TryGetCoverApproach(target, out approach, out routeLength))
                continue;

            currentCover = target;
            patrolCoverApproachPoint = approach;
            patrolCoverAttackDeadline = Time.time + patrolCoverAttackTimeout;
            patrolCoverProgressCheckAt = Time.time + coverStuckCheckSeconds;
            patrolCoverProgressPosition = transform.position;
            patrolCoverFailedApproaches = 0;
            patrolCoverHuntActive = true;
            attackingCover = false;
            hasLockedCoverRotation = false;
            currentAnimationHash = 0;
            if (agent.hasPath) agent.ResetPath();
            agent.isStopped = false;
            agent.speed = investigateSpeed;
            agent.stoppingDistance = 0.1f;
            agent.SetDestination(patrolCoverApproachPoint);
            Log("PATROL COVER ATTACK SELECTED: " + target.name);
            return true;
        }
        return false;
    }

    private void UpdatePatrolCoverAttack()
    {
        if (!AgentReady() || currentCover == null || currentCover.IsDestroyed ||
            !currentCover.isActiveAndEnabled ||
            !currentCover.gameObject.activeInHierarchy ||
            Time.time >= patrolCoverAttackDeadline)
        {
            FinishPatrolCoverAttack();
            return;
        }

        Collider part = currentCover.GetComponentInChildren<Collider>();
        Vector3 contact = part != null
            ? part.ClosestPoint(transform.position) : currentCover.transform.position;
        if (FlatDistance(transform.position, contact) <=
            attackDistance + coverHitReachTolerance)
        {
            StopAgent();
            if (!attackingCover)
            {
                attackingCover = true;
                LockCoverAttackRotation();
                nextCoverFallbackHit = Time.time + Mathf.Max(0.2f,
                    Mathf.Min(coverHitFallbackSeconds, aggressiveCoverHitInterval));
                ForceAnimation(attackHash);
                Log("PATROL ATTACKING COVER: " + currentCover.name);
            }
            if (Time.time >= nextCoverFallbackHit)
                DamageCover();
            return;
        }

        attackingCover = false;
        hasLockedCoverRotation = false;
        agent.isStopped = false;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = 0.1f;
        if (!agent.pathPending &&
            (!agent.hasPath ||
             Vector3.Distance(agent.destination, patrolCoverApproachPoint) > 0.5f))
            agent.SetDestination(patrolCoverApproachPoint);
        FaceMovementDirection();

        if (Time.time >= patrolCoverProgressCheckAt)
        {
            if (FlatDistance(transform.position, patrolCoverProgressPosition) <
                coverMinimumProgress)
            {
                patrolCoverFailedApproaches++;
                Vector3 alternative;
                float length;
                if (TryGetCoverApproach(currentCover, out alternative, out length))
                {
                    patrolCoverApproachPoint = alternative;
                    agent.ResetPath();
                    agent.SetDestination(alternative);
                }
            }
            else patrolCoverFailedApproaches = 0;
            patrolCoverProgressPosition = transform.position;
            patrolCoverProgressCheckAt = Time.time + coverStuckCheckSeconds;
            if (patrolCoverFailedApproaches >= coverStuckRetries)
                FinishPatrolCoverAttack();
        }
    }

    private void FinishPatrolCoverAttack()
    {
        // Always leave a fresh 30-60 second interval between patrol decisions.
        nextCoverDecision = Time.time + NextPatrolCoverInterval();
        patrolCoverHuntActive = false;
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;
        hasWanderDestination = false;
        currentAnimationHash = 0;
        if (AgentReady())
        {
            agent.ResetPath();
            agent.isStopped = false;
        }
        Log("PATROL COVER CHECK COMPLETE - RESUMING WANDER");
    }

    private void BeginMelee()
    {
        if (ChargeBusy || trackedPlayer == null) return;
        if (!bossAnimator.HasState(0, meleeHash))
        {
            Debug.LogError("BossEnemyAI: Missing Animator state MeleeAttack", this);
            nextMeleeTime = Time.time + meleeCooldown;
            return;
        }
        mode = BossMode.Melee;
        meleeEndTime = Time.time + meleeDuration;
        nextMeleeTime = meleeEndTime + meleeCooldown;
        StopAgent();
        FacePosition(trackedPlayer.position, turnSpeed);
        ForceAnimation(meleeHash);
    }

    private void UpdateMelee()
    {
        StopAgent();
        if (trackedPlayer != null) FacePosition(trackedPlayer.position, turnSpeed);
        if (Time.time < meleeEndTime) return;
        mode = BossMode.Chase;
        currentAnimationHash = 0;
        if (Time.time - lastPlayerSeenTime > losePlayerAfter) LosePlayer();
    }

    // =====================================================
    // COVER HUNTING
    // =====================================================

    private void UpdateCoverHunting()
    {
        if (mode != BossMode.Search || !lostPlayerCoverSearch ||
            hadVisualContactLastScan || !AgentReady()) return;

        if (currentCover == null || currentCover.IsDestroyed ||
            !currentCover.isActiveAndEnabled || !currentCover.gameObject.activeInHierarchy)
        {
            EndInvestigationCoverAttack();
            return;
        }

        Collider coverCollider = currentCover.GetComponentInChildren<Collider>();
        Vector3 surface = coverCollider != null
            ? coverCollider.ClosestPoint(transform.position)
            : currentCover.transform.position;
        Vector3 toSurface = surface - transform.position;
        toSurface.y = 0f;

        if (toSurface.magnitude <= attackDistance + coverHitReachTolerance)
        {
            if (!attackingCover)
            {
                attackingCover = true;
                nextCoverFallbackHit = Time.time + Mathf.Max(0.2f,
            Mathf.Min(coverHitFallbackSeconds, aggressiveCoverHitInterval));
                StopAgent();
                LockCoverAttackRotation();
                ForceAnimation(attackHash);
                Log("BOSS ATTACKING COVER: " + currentCover.name);
            }
            else
            {
                StopAgent();
                // LateUpdate owns cover-facing rotation so there is only
                // one rotation command per frame.
            }
            if (Time.time >= nextCoverFallbackHit) DamageCover();
            return;
        }

        attackingCover = false;
        hasLockedCoverRotation = false;
        if (!coverApproachValid)
        {
            float distance;
            if (!TryGetCoverApproach(currentCover, out coverApproachPoint, out distance))
            {
                EndInvestigationCoverAttack();
                return;
            }
            coverApproachValid = true;
        }

        // Keep the SAME reachable destination until arrival. Recomputing the
        // collider's ClosestPoint every frame caused severe zigzags and stalls.
        agent.isStopped = false;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = 0.12f;
        if (Time.time >= nextCoverPath && !agent.pathPending &&
            (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete ||
             Vector3.Distance(agent.destination, coverApproachPoint) > 0.6f))
        {
            if (!agent.SetDestination(coverApproachPoint))
            {
                RegisterFailedInvestigationCoverApproach();
                return;
            }
            nextCoverPath = Time.time + coverPathRefreshInterval;
        }
        if (!agent.pathPending && agent.hasPath &&
            agent.remainingDistance <= 0.3f &&
            FlatDistance(transform.position, coverApproachPoint) <= 0.5f &&
            toSurface.magnitude > attackDistance + coverHitReachTolerance)
        {
            // NavMesh endpoint ended just outside attack range. Recalculate
            // from a different side instead of walking in place.
            RegisterFailedInvestigationCoverApproach();
            return;
        }
        if (Time.time >= coverProgressCheckAt)
        {
            Vector3 travelled = transform.position - coverProgressPosition;
            travelled.y = 0f;
            if (travelled.magnitude < coverMinimumProgress &&
                toSurface.magnitude > attackDistance + coverHitReachTolerance)
            {
                RegisterFailedInvestigationCoverApproach();
                return;
            }
            // Preserve the number of failed approaches on this cover even
            // when one detour briefly moves; reset only for a new target.
            coverProgressPosition = transform.position;
            coverProgressCheckAt = Time.time + coverStuckCheckSeconds;
        }
        if (coverStallAttempts >= Mathf.Max(1, coverStuckRetries))
        {
            EndInvestigationCoverAttack();
            return;
        }
        FaceMovementDirection();
    }

    private void FindNearestCover()
    {
        float nearestDistance = float.PositiveInfinity;
        Cover nearest = null;

        foreach (Cover cover in FindObjectsOfType<Cover>())
        {
            if (cover == null ||
                !cover.isActiveAndEnabled ||
                !cover.gameObject.activeInHierarchy ||
                cover.IsDestroyed)
            {
                continue;
            }

            Collider coverCollider =
                cover.GetComponentInChildren<Collider>();

            Vector3 point =
                coverCollider != null
                    ? coverCollider.ClosestPoint(transform.position)
                    : cover.transform.position;

            float distance =
                Vector3.Distance(transform.position, point);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = cover;
            }
        }

        currentCover = nearest;
    }

    private void LockCoverAttackRotation()
    {
        hasLockedCoverRotation = false;

        if (currentCover == null)
            return;

        Collider coverCollider =
            currentCover.GetComponentInChildren<Collider>();

        Vector3 facingPoint =
            coverCollider != null
                ? coverCollider.bounds.center
                : currentCover.transform.position;

        Vector3 direction =
            facingPoint - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            lockedCoverRotation = transform.rotation;
        }
        else
        {
            lockedCoverRotation =
                Quaternion.LookRotation(direction.normalized) *
                Quaternion.Euler(
                    0f,
                    coverAttackRotationOffset,
                    0f
                );
        }

        hasLockedCoverRotation = true;

        if (AgentReady())
        {
            agent.updateRotation = false;
            StopAgent();
            agent.velocity = Vector3.zero;
        }

        // Don't snap instantly toward cover; rotate smoothly during the attack.
    }

    // =====================================================
    // COVER DAMAGE / ANIMATION EVENT
    // =====================================================

    public void DamageCover()
    {
        bool isSearchAttack = mode == BossMode.Search && lostPlayerCoverSearch;
        bool isPatrolAttack = mode == BossMode.Cover && patrolCoverHuntActive;
        if ((!isSearchAttack && !isPatrolAttack) || hadVisualContactLastScan ||
            !attackingCover ||
            currentCover == null ||
            currentCover.IsDestroyed)
        {
            return;
        }

        nextCoverFallbackHit = Time.time + Mathf.Max(0.2f,
            Mathf.Min(coverHitFallbackSeconds, aggressiveCoverHitInterval));
        Cover struckCover = currentCover;
        struckCover.TakeDamage(coverDamage * (isSearchAttack && clearAllNearbyCover
            ? coverSearchDamageMultiplier : 1f));

        if (currentCover != null && !currentCover.IsDestroyed)
        {
            // Re-trigger an attack swing only if the attack animation has
            // actually played far enough. No per-frame animation restarts.
            AnimatorStateInfo attackInfo = bossAnimator.GetCurrentAnimatorStateInfo(0);
            if (attackInfo.shortNameHash != attackHash || attackInfo.normalizedTime >= 0.92f)
                ForceAnimation(attackHash);
        }

        if (currentCover == null || currentCover.IsDestroyed)
        {
            if (isPatrolAttack)
            {
                FinishPatrolCoverAttack();
                return;
            }
            // TakeDamage can destroy its GameObject immediately, making
            // investigationCover compare as null. Keep the pre-hit reference
            // so we can record a destroyed target before proceeding.
            if (!object.ReferenceEquals(struckCover, null) &&
                inspectedCovers.Add(struckCover))
                coversDestroyedThisSearch++;
            EndInvestigationCoverAttack();
        }
    }

    private void InterruptCoverAttack()
    {
        bool wasAttacking = attackingCover;

        attackingCover = false;
        hasLockedCoverRotation = false;
        currentCover = null;
        coverHuntActive = false;
        patrolCoverHuntActive = false;
        hasWanderDestination = false;
        nextCoverPath = 0f;
        nextCoverSearch = 0f;

        if (AgentReady())
        {
            agent.isStopped = true;

            if (agent.hasPath)
                agent.ResetPath();

            agent.velocity = Vector3.zero;
        }

        currentAnimationHash = 0;

        if (wasAttacking)
            Log("BOSS INTERRUPTED COVER ATTACK!");
    }

    // =====================================================
    // MOVEMENT / ANIMATION
    // =====================================================

    private void CancelSearch()
    {
        hasSearchPoint = false;
        searchPaused = false;
        searchPauseHasScan = false;
        coverApproachValid = false;
        smoothedMovementHeading = Vector3.zero;
        visitedPoints.Clear();
        hasFailedCoverApproach = false;
        failedCoverApproachOwner = null;
        pendingInvestigationPathSince = -1f;
        perimeterPointStartedAt = 0f;
        perimeterHasDestination = false;
        perimeterWaypointIndex = 0;
        perimeterDestinationFailures = 0;
        extendedHasDestination = false;
        extendedLooking = false;
        extendedSearchPoints.Clear();
        deferredCoverThisPass.Clear();
        if (AgentReady()) agent.autoBraking = originalAutoBraking;
    }

    private void UpdateMeasuredVelocity()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        measuredVelocity =
            (transform.position - previousPosition) / dt;

        measuredVelocity.y = 0f;
        previousPosition = transform.position;
    }

    private void UpdateMovementAnimation()
    {
        if (bossAnimator == null || ChargeBusy || playerDetectionReadyForCharge ||
            mode == BossMode.Melee || mode == BossMode.Detected ||
            mode == BossMode.Suspicious || mode == BossMode.EnemyAttack) return;

        if (attackingCover &&
            (mode == BossMode.Search || (mode == BossMode.Cover && patrolCoverHuntActive)))
        {
            PlayAnimation(attackHash);
            return;
        }
        if (mode == BossMode.InvestigateLook || searchPaused ||
            !AgentReady() || agent.isStopped)
        {
            PlayAnimation(idleHash); // stationary always CrossFade to Idle
            return;
        }

        float actualSpeed = measuredVelocity.magnitude;
        float navSpeed = agent.velocity.magnitude;
        bool moving = actualSpeed >= Mathf.Max(0.18f, movementThreshold);
        // When the sound route is blocked, an agent can report simulated
        // velocity even while the transform is stationary. That used to make
        // the boss visibly run in place. Require actual position change after
        // a short initial navigation handoff.
        if (mode != BossMode.InvestigateTravel && navSpeed >= 0.3f)
            moving = true;
        if (moving) lastActuallyMovingTime = Time.time;
        else if (agent.hasPath && !agent.pathPending &&
                 Time.time - lastActuallyMovingTime < movementAnimationGrace)
            moving = true;

        PlayAnimation(moving ? (mode == BossMode.Cover && walkAfterCancelledDetection ? ResolveWalkHash() : runHash) : idleHash);
    }

    private void PlayAnimation(int hash)
    {
        if (currentAnimationHash != hash)
            ForceAnimation(hash);
    }

    private void ForceAnimation(int hash)
    {
        if (bossAnimator == null || !bossAnimator.HasState(0, hash))
            return;

        bossAnimator.CrossFadeInFixedTime(
            hash,
            animationBlend,
            0
        );

        currentAnimationHash = hash;
    }

    // Keep investigation moving even after a failed NavMesh handoff or a
    // path request that never finishes. Only affects investigation, not chase,
    // charging, stunned animation, deliberate corner scans or melee.
    private void CheckInvestigationNavigationHealth()
    {
        if (agent == null || ChargeBusy) return;

        if ((!agent.enabled || !agent.isOnNavMesh || !agent.updatePosition) &&
            Time.time >= nextInvestigationNavMeshResync)
        {
            nextInvestigationNavMeshResync = Time.time +
                Mathf.Max(0.25f, investigationNavMeshRetryInterval);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit,
                Mathf.Max(0.5f, investigationNavMeshResyncRadius), NavMesh.AllAreas))
            {
                if (!agent.enabled) agent.enabled = true;
                if (agent.isActiveAndEnabled && agent.Warp(hit.position))
                {
                    agent.updatePosition = true;
                    agent.updateRotation = false;
                    agent.isStopped = false;
                    pendingInvestigationPathSince = -1f;
                    investigationPathRequested = false;
                    nextSoundPathAttempt = 0f;
                    perimeterHasDestination = false;
                    extendedHasDestination = false;
                    coverApproachValid = false;
                    nextCoverPath = 0f;
                    if (debugSoundNavigation)
                        Log("INVESTIGATION NAVMESH RESTORED: " + hit.position);
                }
            }
        }

        if (!AgentReady())
        {
            pendingInvestigationPathSince = -1f;
            return;
        }

        bool needsMovingPath = mode == BossMode.InvestigateTravel ||
            (mode == BossMode.Search &&
                (investigationSearchPhase == InvestigationSearchPhase.Perimeter ||
                 (investigationSearchPhase == InvestigationSearchPhase.AreaSweep &&
                  !extendedLooking && extendedHasDestination) ||
                 (investigationSearchPhase == InvestigationSearchPhase.CoverSweep &&
                  investigationCover != null && !attackingCover && coverApproachValid)));
        if (!needsMovingPath || !agent.pathPending)
        {
            pendingInvestigationPathSince = -1f;
            return;
        }
        if (pendingInvestigationPathSince < 0f)
        {
            pendingInvestigationPathSince = Time.time;
            return;
        }
        if (Time.time - pendingInvestigationPathSince < investigationPathPendingTimeout)
            return;

        // Cancel the hung request, then use this phase's normal bounded
        // recovery. Never keep waiting for pathPending to become false.
        pendingInvestigationPathSince = -1f;
        agent.ResetPath();
        if (mode == BossMode.InvestigateTravel)
        {
            investigationFailedPaths++;
            investigationPathRequested = false;
            failedSoundApproachPoint = committedInvestigationPoint;
            hasFailedSoundApproach = true;
            nextSoundPathAttempt = 0f;
        }
        else if (investigationSearchPhase == InvestigationSearchPhase.Perimeter &&
            perimeterWaypointIndex < wallCheckPoints.Count)
            RetryPerimeterWaypoint();
        else if (investigationSearchPhase == InvestigationSearchPhase.AreaSweep &&
            extendedWaypointIndex < extendedSearchPoints.Count)
            RetryExtendedWaypoint();
        else if (investigationSearchPhase == InvestigationSearchPhase.CoverSweep)
            RegisterFailedInvestigationCoverApproach();

        if (debugSoundNavigation || debugCoverSweep)
            Log("INVESTIGATION NAVMESH PATH TIMED OUT - selecting a new approach");
    }

    // Candidate must be close to the original corner but have a COMPLETE
    // walkable route. Using a nearby alternate avoids retrying exactly the
    // same blocked surface or unreachable NavMesh corner forever.
    private bool TryFindAlternateInvestigationWaypoint(Vector3 original,
        out Vector3 alternate)
    {
        alternate = Vector3.zero;
        if (!AgentReady()) return false;
        float bestScore = float.PositiveInfinity;
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = 1.4f + ring * 1.1f;
            for (int i = 0; i < 12; i++)
            {
                float angle = (i + ring * 0.5f) * Mathf.PI * 2f / 12f;
                Vector3 candidate = original + new Vector3(
                    Mathf.Cos(angle) * radius, 0f,
                    Mathf.Sin(angle) * radius);
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidate, out hit, 0.8f, agent.areaMask))
                    continue;
                if (FlatDistance(hit.position, original) < 0.85f ||
                    (FlatDistance(hit.position, transform.position) < 0.9f &&
                     FlatDistance(transform.position, original) > 2f))
                    continue;
                NavMeshPath route = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, route) ||
                    route.status != NavMeshPathStatus.PathComplete)
                    continue;
                float distance = PathDistance(route);
                if (float.IsNaN(distance) || float.IsInfinity(distance)) continue;
                float score = FlatDistance(hit.position, original) * 2f +
                    distance * 0.15f;
                if (score < bestScore)
                {
                    bestScore = score;
                    alternate = hit.position;
                }
            }
        }
        return !float.IsInfinity(bestScore);
    }

    private void RegisterFailedInvestigationCoverApproach()
    {
        if (investigationCover == null || !AgentReady()) return;
        failedCoverApproachOwner = investigationCover;
        failedCoverApproachPoint = coverApproachPoint;
        hasFailedCoverApproach = true;
        coverStallAttempts++;
        coverApproachValid = false;
        nextCoverPath = 0f;
        coverProgressPosition = transform.position;
        coverProgressCheckAt = Time.time + Mathf.Max(0.5f, coverStuckCheckSeconds);
        agent.ResetPath();
        if (debugCoverSweep)
            Log("COVER APPROACH UNREACHABLE - trying another side: " +
                investigationCover.name + " (" + coverStallAttempts + ")");
        if (coverStallAttempts >= Mathf.Max(1, coverStuckRetries))
            EndInvestigationCoverAttack();
    }

    private bool AgentReady()
    {
        return agent != null &&
               agent.isActiveAndEnabled &&
               agent.isOnNavMesh;
    }

    private void StopAgent()
    {
        if (!AgentReady()) return;
        agent.isStopped = true;
        smoothedMovementHeading = Vector3.zero;
        hasRequestedBodyRotation = false;
        // Deliberately do NOT ResetPath here: resetting every time search
        // scans or attacks causes stuttering / momentary path loss.
    }

    private void FaceMovementDirection()
    {
        if (!AgentReady() || agent.isStopped || agent.pathPending || !agent.hasPath)
            return;
        if (agent.remainingDistance <= agent.stoppingDistance + 0.35f)
            return;

        // SteeringTarget follows the next NavMesh corner. It is much more
        // stable than agent.desiredVelocity (which oscillates with avoidance).
        Vector3 direction = agent.steeringTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < minimumSteeringLookAhead * minimumSteeringLookAhead)
            return;
        direction.Normalize();
        if (smoothedMovementHeading.sqrMagnitude < 0.5f)
            smoothedMovementHeading = direction;
        else
            smoothedMovementHeading = Vector3.Slerp(smoothedMovementHeading,
                direction, 1f - Mathf.Exp(-steeringDirectionSmoothness * Time.deltaTime));

        if (smoothedMovementHeading.sqrMagnitude < 0.4f) return;
        Quaternion target = Quaternion.LookRotation(smoothedMovementHeading);
        if (Quaternion.Angle(transform.rotation, target) <= rotationDeadZone) return;
        RequestBodyRotation(target, movementTurnSpeed);
    }

    private void RequestBodyRotation(Quaternion target, float degreesPerSecond)
    {
        requestedBodyRotation = target;
        requestedTurnSpeed = degreesPerSecond;
        hasRequestedBodyRotation = true;
    }

    private void FacePosition(Vector3 position, float speed)
    {
        Vector3 direction =
            position - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(direction.normalized);

        RequestBodyRotation(targetRotation, Mathf.Min(speed, 320f));
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log(message, this);
    }

    // =====================================================
    // DEBUG GIZMOS
    // =====================================================

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(
            transform.position,
            sprintHearingRange
        );

        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(
            transform.position,
            runningHearingRange
        );

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            transform.position,
            walkingHearingRange
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            transform.position,
            crouchingHearingRange
        );

        if (!Application.isPlaying)
            return;

        // Scene view: blue points are the ordered wall circuit, the yellow
        // point is the next target. This reveals skipped/unreachable corners.
        if (mode == BossMode.Search && wallCheckPoints.Count > 0)
        {
            Gizmos.color = Color.blue;
            for (int i = 0; i < wallCheckPoints.Count; i++)
            {
                Gizmos.DrawWireSphere(wallCheckPoints[i], 0.22f);
                if (i + 1 < wallCheckPoints.Count)
                    Gizmos.DrawLine(wallCheckPoints[i], wallCheckPoints[i + 1]);
            }
            if (investigationSearchPhase == InvestigationSearchPhase.Perimeter &&
                perimeterWaypointIndex < wallCheckPoints.Count)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(wallCheckPoints[perimeterWaypointIndex], 0.3f);
            }
        }

        if (mode == BossMode.InvestigateTravel)
        {
            // Yellow: NavMesh-reachable target. Magenta: actual heard point.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(committedInvestigationPoint, 0.35f);
            Gizmos.DrawLine(investigationCluePosition, committedInvestigationPoint);
        }

        if (mode == BossMode.Suspicious ||
            mode == BossMode.InvestigateTravel ||
            mode == BossMode.InvestigateLook ||
            mode == BossMode.Search)
        {
            Gizmos.color = Color.magenta;

            Gizmos.DrawSphere(soundLocation, 0.35f);
            Gizmos.DrawLine(
                transform.position,
                soundLocation
            );

            Gizmos.DrawWireSphere(
                soundLocation,
                nearbyCoverSearchRadius
            );
        }

        if (mode == BossMode.Search && extendedSearchPoints.Count > 0 &&
            investigationSearchPhase == InvestigationSearchPhase.AreaSweep)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < extendedSearchPoints.Count; i++)
            {
                Gizmos.DrawWireSphere(extendedSearchPoints[i], 0.19f);
                if (i + 1 < extendedSearchPoints.Count)
                    Gizmos.DrawLine(extendedSearchPoints[i], extendedSearchPoints[i + 1]);
            }
            if (extendedWaypointIndex < extendedSearchPoints.Count)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(extendedSearchPoints[extendedWaypointIndex], 0.28f);
            }
        }

        if (mode == BossMode.Search && hasSearchPoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(searchPoint, 0.25f);
        }
        if (mode == BossMode.Search && investigationCover != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, coverApproachPoint);
            Gizmos.DrawWireSphere(coverApproachPoint, 0.35f);
        }
    }
}
