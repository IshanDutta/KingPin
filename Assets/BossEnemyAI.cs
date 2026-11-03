
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
    [SerializeField] private string idleState = "idle";
    [SerializeField] private string runState = "run";
    [SerializeField] private string attackState = "Attack";
    [SerializeField] private string detectedState = "Detected";
    [SerializeField] private string suspiciousState = "Suspicious";
    [SerializeField] private float animationBlend = 0.12f;
    [SerializeField] private float suspiciousFallbackDuration = 1.5f;
    [SerializeField] private float movementThreshold = 0.08f;

    [Header("Player")]
    [SerializeField] private Transform player;

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
    [SerializeField] private float movementCheckInterval = 0.05f;
    [SerializeField] private float movementInvestigationInterval = 0.5f;
    [SerializeField] private float minimumNewSoundDistance = 1.5f;

    [Header("Investigation")]
    [SerializeField] private float investigateSpeed = 4.5f;
    [SerializeField] private float investigateStoppingDistance = 1.1f;
    [SerializeField] private float investigateTimeout = 12f;
    [SerializeField] private float investigateLookDuration = 3f;
    [SerializeField] private float investigateLookTurnSpeed = 110f;
    [SerializeField] private float soundPositionSampleRadius = 5f;

    [Header("Detection and Chase")]
    [SerializeField] private float detectedDuration = 1.5f;
    [SerializeField] private float turnSpeed = 720f;
    [SerializeField] private float chaseSpeed = 5f;
    [SerializeField] private float chaseStoppingDistance = 1.5f;
    [SerializeField] private float chasePathRefreshInterval = 0.15f;
    [SerializeField] private float losePlayerAfter = 4f;

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
    [SerializeField] private float searchPointTimeout = 7f;
    [SerializeField] private float searchPauseDuration = 1.2f;
    [SerializeField] private float searchArrivalDistance = 1.1f;
    [SerializeField] private float minimumPointSeparation = 2.5f;
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
    [SerializeField, Min(0f)] private float cornerLookSeconds = 0.15f;
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
    [SerializeField, Min(0f)] private float investigationScanAngle = 75f;
    [SerializeField, Min(1f)] private float investigationScanSpeed = 135f;
    [SerializeField, Min(0f)] private float investigationScanHold = 0.45f;
    [SerializeField, Min(1)] private int minimumSearchStops = 5;
    [SerializeField, Min(0f)] private float nearbyCoverSearchRadius = 15f;
    [SerializeField, Min(0)] private int maximumCoverTargetsPerInvestigation = 30;
    [SerializeField, Min(1f)] private float investigationCoverTimeout = 18f;
    [SerializeField, Min(0f)] private float coverSearchPause = 1.5f;
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
    [SerializeField] private float coverDamage = 25f;
    [SerializeField] private float coverSearchInterval = 1f;
    [SerializeField] private float coverPathRefreshInterval = 0.25f;
    [SerializeField] private float coverAttackRotationOffset = 0f;

    [Header("Wandering (no vents)")]
    [SerializeField, Min(2f)] private float wanderRadius = 18f;
    [SerializeField, Min(0.5f)] private float wanderSpeed = 2.7f;
    [SerializeField] private Vector2 wanderPauseSeconds = new Vector2(1f, 3f);
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
        Melee
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
    private float investigationLookStartTime;
    private float detectionEndTime;
    private float lastPlayerSeenTime = float.NegativeInfinity;
    private float searchStartTime;
    private float searchPointStartTime;
    private float searchPauseEndTime;
    private float visionSuppressedUntil;
    private float hearingSuppressedUntil;

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

        if (Time.time > lastAudibleContactTime + hearingAwarenessHoldSeconds)
        {
            audibleAwareness = Mathf.MoveTowards(
                audibleAwareness, 0f,
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
    [SerializeField] private float coverHitFallbackSeconds = 1.4f;
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
        (mode == BossMode.Detected || mode == BossMode.Chase) &&
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
        idleHash = Animator.StringToHash(idleState);
        runHash = Animator.StringToHash(runState);
        // Cover attacks always use the exact Animator state named "Attack".
        // This also avoids an old serialized Inspector value of "attack".
        attackHash = Animator.StringToHash("Attack");
        detectedHash = Animator.StringToHash(detectedState);
        suspiciousHash = Animator.StringToHash(suspiciousState);

        originalAgentSpeed = agent.speed;
        originalStoppingDistance = agent.stoppingDistance;

        agent.updateRotation = false;

        previousPosition = transform.position;
        wanderOrigin = transform.position;
        nextCoverDecision = Time.time + Random.Range(coverDecisionSeconds.x, coverDecisionSeconds.y);
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
        if (!AgentReady())
            return;

        agent.isStopped = false;
        agent.speed = originalAgentSpeed;
        agent.stoppingDistance = originalStoppingDistance;
        agent.updateRotation = false;

        hasLockedCoverRotation = false;
    }

    private void Update()
    {
        if (!initialized || ai.Damagable.IsDead)
            return;

        if (ChargeBusy)
        {
            // Keep checking vision during the charge.
            // Do not run normal AI or overwrite Dash/Stunned.
            if (chargeAttack.IsCharging)
                CheckVision();

            previousPosition = transform.position;
            measuredVelocity = Vector3.zero;
            UpdateUnifiedAwareness();
            return;
        }

        if (pendingPostChargeSearch)
        {
            pendingPostChargeSearch = false;

            StartSearch(lastKnownPlayerPosition);

            Log(
                "BOSS RECOVERED - SEARCHING LAST SEEN POSITION"
            );
        }

        if (mode != BossMode.Chase && chaseObstacleCover != null)
            ClearChaseCover();

        UpdateMeasuredVelocity();
        CheckVision();
        CheckPlayerMovement();

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
        if (!initialized ||
            ai.Damagable.IsDead ||
            ChargeBusy)
        {
            return;
        }

        // Keep the UI driven by the same suspicion value as the boss.
        // Some third-party AI components also update AwarenessSystem in Update.
        // LateUpdate makes our intended visual detection value authoritative
        // for this frame without interrupting the investigation itself.
        UpdateUnifiedAwareness();

        if ((mode == BossMode.Cover || mode == BossMode.Chase) &&
            attackingCover &&
            hasLockedCoverRotation)
        {
            transform.rotation = lockedCoverRotation;
        }

        if (mode == BossMode.Detected &&
            trackedPlayer != null)
        {
            FacePosition(trackedPlayer.position, turnSpeed);
        }
    }

    // =====================================================
    // CHARGE RECOVERY
    // =====================================================

    public void ForgetPlayerAfterCharge()
    {
        // lastKnownPlayerPosition has been updated
        // by CheckVision during the charge.

        if (ai != null && ai.Target != null)
            ai.TargetHandler.TrySetTarget(null);

        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;

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

        pendingPostChargeSearch = true;
        currentAnimationHash = 0;

        Log(
            "BOSS LOST PLAYER AFTER CHARGE - STUNNED"
        );
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
        if (Time.time < hearingSuppressedUntil)
            return;

        if (player == null)
        {
            FindPlayerAutomatically();

            if (player == null)
                return;
        }

        if (!ResolvePlayerLocomotion())
            return;

        if (Time.time < nextMovementCheck)
            return;

        nextMovementCheck =
            Time.time +
            Mathf.Max(0.02f, movementCheckInterval);

        Vector3 velocity =
            playerLocomotion.CurrentVelocity;

        velocity.y = 0f;

        float speed = velocity.magnitude;
        MoveType moveType = playerLocomotion.moveType;

        float hearingRange;
        string movementName;

        if (playerLocomotion.IsCrouching)
        {
            hearingRange = crouchingHearingRange;
            movementName = "CROUCHING";
        }
        else if (moveType == MoveType.Sprint)
        {
            hearingRange = sprintHearingRange;
            movementName = "SPRINTING";
        }
        else if (moveType == MoveType.Run)
        {
            hearingRange = runningHearingRange;
            movementName = "RUNNING";
        }
        else
        {
            hearingRange = walkingHearingRange;
            movementName = "WALKING";
        }

        Vector3 heardPosition =
            playerLocomotion.transform.position;

        float distance = Vector3.Distance(
            transform.position,
            heardPosition
        );

        if (logMovementChecks)
        {
            Debug.Log(
                "[BOSS HEARING] " +
                movementName +
                " | Speed=" + speed.ToString("F2") +
                " | Distance=" + distance.ToString("F1") +
                " | Range=" + hearingRange.ToString("F1") +
                " | Mode=" + mode,
                this
            );
        }

        if (speed < minimumMovementSpeed ||
            distance > hearingRange)
        {
            return;
        }

        // Register EVERY audible player movement, even while already
        // investigating, chasing, or attacking. Hearing is UI feedback,
        // not permission to overwrite the current navigation goal.
        lastAudibleContactTime = Time.time;
        audibleAwareness = Mathf.Max(audibleAwareness, hearingAwarenessLevel);
        UpdateUnifiedAwareness();

        if (mode == BossMode.Search && investigationCover != null)
        {
            EndInvestigationCoverAttack();
        }

        if ((mode != BossMode.Cover && mode != BossMode.Search) ||
            (mode == BossMode.Cover && Time.time < nextInvestigationAllowed))
            return;

        if (Time.time < nextMovementInvestigation)
            return;

        nextMovementInvestigation =
            Time.time + movementInvestigationInterval;

        Log(
            "BOSS HEARD " +
            movementName +
            " AT " +
            heardPosition
        );

        BeginMovementInvestigation(heardPosition);
    }

    // Live sensory evidence suppresses destructive blind searching.
    // Sight uses the same fresh-contact timestamp as the chase system.
    // Sound is sampled directly so search can react before the next hearing tick.
    private bool HasLiveSensoryClue()
    {
        if (Time.time - lastPlayerSeenTime <= 0.3f)
            return true;
        if (Time.time < hearingSuppressedUntil || !ResolvePlayerLocomotion())
            return false;
        Vector3 velocity = playerLocomotion.CurrentVelocity;
        velocity.y = 0f;
        if (velocity.magnitude < minimumMovementSpeed)
            return false;
        float range = playerLocomotion.IsCrouching ? crouchingHearingRange :
            playerLocomotion.moveType == MoveType.Sprint ? sprintHearingRange :
            playerLocomotion.moveType == MoveType.Run ? runningHearingRange :
            walkingHearingRange;
        return Vector3.Distance(transform.position, playerLocomotion.transform.position) <= range;
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
        CancelSearch();
        investigationCover = null;
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;
        investigatingVisualClue = true;
        lostPlayerCoverSearch = false;
        partialVisualInvestigationStarted = true;
        committedInvestigationPoint = position;
        soundLocation = position;
        lastKnownPlayerPosition = position;
        investigationCoverTargets = 0;
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

    private void BeginMovementInvestigation(Vector3 position)
    {
        if (mode != BossMode.Cover && mode != BossMode.Search)
            return;

        if (mode == BossMode.Cover)
            InterruptCoverAttack();

        CancelSearch();
        investigatingVisualClue = false;
        lostPlayerCoverSearch = false;
        committedInvestigationPoint = position;
        soundLocation = position;
        investigationCover = null;
        investigationCoverTargets = 0;
        inspectedCovers.Clear();
        mode = BossMode.InvestigateTravel;
        investigationStartTime = Time.time;
        investigationFailedPaths = 0;
        investigationApproachPosition = transform.position;
        investigationPathRequested = false;
        nextInvestigationProgressCheck = Time.time + investigationProgressInterval;
        investigationProgressPosition = transform.position;
        currentAnimationHash = 0;
        nextInvestigationAllowed = Time.time + searchRestartCooldown;
        TrySetInvestigationPath();
        Log("BOSS INVESTIGATING LAST SEEN POSITION: " + position);
    }

    private bool TrySetInvestigationPath()
    {
        if (!AgentReady()) return false;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(committedInvestigationPoint, out hit,
                soundPositionSampleRadius, NavMesh.AllAreas)) return false;
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path) ||
            path.status != NavMeshPathStatus.PathComplete) return false;
        committedInvestigationPoint = hit.position;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = Mathf.Min(investigateStoppingDistance, investigationArrivalTolerance);
        agent.isStopped = false;
        investigationPathRequested = agent.SetPath(path);
        return investigationPathRequested;
    }


    // =====================================================
    // SUSPICIOUS
    // =====================================================

    private void UpdateSuspicious()
    {
        StopAgent();

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
        committedInvestigationPoint = soundLocation;
        nextInvestigationProgressCheck = Time.time + investigationProgressInterval;
        investigationProgressPosition = transform.position;
        TrySetInvestigationPath();
    }

    private void UpdateInvestigationTravel()
    {
        if (!AgentReady()) return;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = Mathf.Min(investigateStoppingDistance, investigationArrivalTolerance);

        Vector3 delta = committedInvestigationPoint - transform.position;
        delta.y = 0f;
        bool arrived = !agent.pathPending &&
            (delta.magnitude <= investigationArrivalTolerance ||
             (agent.hasPath && agent.remainingDistance <= investigationArrivalTolerance));
        if (arrived)
        {
            StopAgent();
            mode = BossMode.InvestigateLook;
            investigationLookStartTime = Time.time;
            BeginInvestigationScan();
            Log("BOSS ARRIVED - SCANNING LAST SEEN POSITION");
            return;
        }

        if (!investigationPathRequested ||
            (!agent.pathPending && (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete)))
        {
            if (!TrySetInvestigationPath()) investigationFailedPaths++;
        }
        else
        {
            agent.isStopped = false;
            FaceMovementDirection();
        }

        if (Time.time >= nextInvestigationProgressCheck)
        {
            float moved = Vector3.Distance(transform.position, investigationProgressPosition);
            if (moved < investigationMinimumMovement && delta.magnitude > investigationArrivalTolerance)
            {
                investigationFailedPaths++;
                TrySetInvestigationPath();
            }
            else if (moved >= investigationMinimumMovement)
                investigationFailedPaths = 0;
            investigationProgressPosition = transform.position;
            nextInvestigationProgressCheck = Time.time + investigationProgressInterval;
        }

        // Only abandon if the NavMesh is genuinely impossible, never just
        // because the monster is partway along a valid path.
        if (investigationFailedPaths >= investigationMaxFailedPaths ||
            Time.time - investigationStartTime > investigationMaximumTravelSeconds)
        {
            Log("INVESTIGATION PATH BLOCKED - SEARCHING AROUND DESTINATION");
            StartSearch(committedInvestigationPoint);
        }
    }

    private void BeginInvestigationScan()
    {
        scanOrigin = transform.rotation;
        scanIndex = 0;
        scanHolding = false;
        scanHoldUntil = 0f;
    }

    private void UpdateInvestigationScan()
    {
        // Center, left, center, right, center, repeat.
        float[] steps = { 0f, -1f, 0f, 1f, 0f };
        Quaternion target = scanOrigin * Quaternion.Euler(
            0f, steps[scanIndex % steps.Length] * investigationScanAngle, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            target, investigationScanSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, target) > 2f)
        {
            scanHolding = false;
            return;
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
    }

    private void UpdateInvestigationLook()
    {
        StopAgent();
        UpdateInvestigationScan();
        if (Time.time - investigationLookStartTime >= Mathf.Min(investigateLookDuration, 0.5f))
            StartSearch(committedInvestigationPoint);
    }


    // =====================================================
    // VISION
    // =====================================================

    private void CheckVision()
    {
        if (Time.time < visionSuppressedUntil)
            return;

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
            lastKnownPlayerPosition = seenTarget.position;
            lastPlayerSeenTime = Time.time;

            // Charging keeps vision updates but owns its movement and animation.
            if (ChargeBusy)
                return;

            // Fully detected combat is handled by the chase state.
            if (mode == BossMode.Chase ||
                mode == BossMode.Detected ||
                mode == BossMode.Melee)
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
            if (suspicion >= investigateSuspicionThreshold &&
                (mode == BossMode.Cover || mode == BossMode.Search) &&
                Time.time >= nextInvestigationAllowed)
            {
                partialVisualInvestigationStarted = true;
                PrioritizeVisualClue(seenTarget.position);
            }

            return;
        }

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

        if (mode != BossMode.Chase &&
            mode != BossMode.Detected &&
            mode != BossMode.Melee)
        {
            // Investigation is a committed AI task, NOT a minimum UI value.
            // When the player breaks line of sight (including HidingArea),
            // the meter always decays to zero, even during investigation.
            suspicion = Mathf.MoveTowards(
                suspicion, 0f,
                Time.deltaTime / Mathf.Max(0.01f, suspicionFadeSeconds));

            UpdateUnifiedAwareness();
        }
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
        detectionEndTime = Time.time + detectedDuration;

        UpdateUnifiedAwareness();

        if (ai.Target != target)
            ai.TargetHandler.TrySetTarget(target);

        StopAgent();
        FacePosition(target.position, turnSpeed);
        ForceAnimation(detectedHash);

        Log("BOSS SEES PLAYER - DETECTED ANIMATION");
    }

    private void UpdateDetection()
    {
        StopAgent();

        if (trackedPlayer != null)
            FacePosition(trackedPlayer.position, turnSpeed);

        if (Time.time < detectionEndTime)
            return;

        if (trackedPlayer == null ||
            Time.time - lastPlayerSeenTime > losePlayerAfter)
        {
            LosePlayer();
            return;
        }

        mode = BossMode.Chase;
        nextChasePath = 0f;

        if (AgentReady())
            agent.isStopped = false;

        currentAnimationHash = 0;

        Log("BOSS CHASING PLAYER");
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

        // Cover search is permitted after a lost visual clue, never just a sound.
        lostPlayerCoverSearch = lostPlayerCoverSearch || investigatingVisualClue || hasPartialVisualContact;
        soundLocation = center;
        nextInvestigationAllowed = Time.time + searchRestartCooldown;
        searchStartTime = Time.time;
        searchDeadline = Time.time + Mathf.Max(searchDuration, 12f);
        completedSearchStops = 0;
        wallChecksCompleted = 0;
        searchRepathAttempts = 0;
        BuildWallCheckPoints(center);
        perimeterChecksPlanned = wallCheckPoints.Count;
        // Give the boss time to physically check corners before its search expires.
        searchDeadline += wallCheckExtraTime;
        investigationCover = null;
        nextInvestigationCoverTime = Time.time;
        mode = BossMode.Search;
        currentAnimationHash = 0;

        StopAgent();

        Log("BOSS SEARCHING AROUND: " + center);
    }

    private void UpdateSearching()
    {
        if (!AgentReady()) return;

        // Stop smashing cover as soon as sight or a usable sound clue returns.
        if (investigationCover != null && HasLiveSensoryClue())
        {
            EndInvestigationCoverAttack();
            return;
        }

        // A selected cover is a committed investigation target. Never switch
        // to a random cover while searching. Wait for destruction or timeout.
        if (investigationCover != null)
        {
            bool expired = Time.time - investigationCoverStartTime > investigationCoverTimeout;
            bool destroyed = investigationCover.IsDestroyed ||
                             !investigationCover.isActiveAndEnabled ||
                             !investigationCover.gameObject.activeInHierarchy;
            if (!expired && !destroyed)
            {
                currentCover = investigationCover;
                UpdateCoverHunting();
                return;
            }
            EndInvestigationCoverAttack();
            return;
        }

        // Unity destroyed-object references compare equal to null.
        // If cover was destroyed by TakeDamage, complete that investigation.
        if (investigationCoverTargets > 0 && currentCover == null && attackingCover)
        {
            EndInvestigationCoverAttack();
            return;
        }

        if (Time.time >= searchDeadline && (!lostPlayerCoverSearch || investigationCoverTargets >= maximumCoverTargetsPerInvestigation) && completedSearchStops >= minimumSearchStops &&
            wallCheckPoints.TrueForAll(p => visitedPoints.Exists(v => Vector3.Distance(v, p) < minimumPointSeparation)))
        {
            FinishSearch();
            return;
        }
        if (Time.time >= searchDeadline + searchDuration &&
            wallChecksCompleted >= perimeterChecksPlanned)
        {
            FinishSearch();
            return;
        }

        if (searchPaused)
        {
            StopAgent();
            UpdateInvestigationScan();
            if (Time.time >= searchPauseEndTime)
            {
                searchPaused = false;
                hasSearchPoint = false;
            }
            return;
        }

        if (!hasSearchPoint)
        {
            if (lostPlayerCoverSearch && !HasLiveSensoryClue() &&
                Time.time >= nextInvestigationCoverTime &&
                (wallChecksCompleted >= perimeterChecksPlanned || perimeterChecksPlanned == 0) &&
                investigationCoverTargets < maximumCoverTargetsPerInvestigation &&
                TrySelectNearbyInvestigationCover())
            {
                investigationCoverTargets++;
                searchDeadline = Mathf.Max(searchDeadline, Time.time + investigationCoverTimeout + 3f);
                UpdateCoverHunting();
                return;
            }

            if (!ChooseSearchPoint())
            {
                FinishSearch();
                return;
            }
            hasSearchPoint = true;
            searchPointStartTime = Time.time;
            movementProgressCheckTime = Time.time;
            movementProgressPosition = transform.position;
            searchRepathAttempts = 0;
            agent.isStopped = false;
            agent.speed = investigateSpeed;
            agent.stoppingDistance = searchArrivalDistance;
            if (!agent.SetDestination(searchPoint))
            {
                visitedPoints.Add(searchPoint);
                hasSearchPoint = false;
            }
            return;
        }

        agent.isStopped = false;
        agent.speed = investigateSpeed;
        agent.stoppingDistance = searchArrivalDistance;
        FaceMovementDirection();

        if (!agent.pathPending &&
            (agent.pathStatus != NavMeshPathStatus.PathComplete ||
             (!agent.hasPath && Vector3.Distance(transform.position, searchPoint) > searchArrivalDistance + 0.25f)))
        {
            MarkSearchPointVisited();
            hasSearchPoint = false;
            StopAgent();
            return;
        }
        if (Time.time - movementProgressCheckTime >= stuckCheckSeconds)
        {
            float moved = Vector3.Distance(transform.position, movementProgressPosition);
            movementProgressPosition = transform.position;
            movementProgressCheckTime = Time.time;
            if (moved < stuckMinimumProgress &&
                Vector3.Distance(transform.position, searchPoint) > searchArrivalDistance + 0.25f)
            {
                // A stalled agent should repath, not stop, spin, and abandon
                // the corner at the first failed progress check.
                searchRepathAttempts++;
                if (searchRepathAttempts <= searchMaxRepaths)
                {
                    agent.ResetPath();
                    agent.isStopped = false;
                    NavMeshPath retry = new NavMeshPath();
                    if (agent.CalculatePath(searchPoint, retry) &&
                        retry.status == NavMeshPathStatus.PathComplete)
                        agent.SetPath(retry);
                }
                else
                {
                    MarkSearchPointVisited();
                    hasSearchPoint = false;
                    searchRepathAttempts = 0;
                    StopAgent();
                }
                return;
            }
            if (moved >= stuckMinimumProgress) searchRepathAttempts = 0;
        }
        if (!agent.pathPending &&
            Vector3.Distance(transform.position, searchPoint) <= searchArrivalDistance + 0.25f)
        {
            MarkSearchPointVisited();
            completedSearchStops++;
            StopAgent();
            searchPaused = true;
            // Only a brief glance at corners. Do not stand motionless for
            // several seconds before moving around the next side of a wall.
            bool checkedWall = wallCheckPoints.Exists(p =>
                Vector3.Distance(p, searchPoint) < minimumPointSeparation);
            searchPauseEndTime = Time.time + (checkedWall ? cornerLookSeconds : searchPauseDuration);
            BeginInvestigationScan();
        }
        else if (Time.time - searchPointStartTime > searchPointTimeout)
        {
            MarkSearchPointVisited();
            hasSearchPoint = false;
            StopAgent();
        }
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
        investigationCover = null;
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;
        hasSearchPoint = false;
        nextCoverPath = 0f;
        nextInvestigationCoverTime = Time.time + coverSearchPause;
        StopAgent();
        searchPaused = true;
        searchPauseEndTime = Time.time + coverSearchPause;
        BeginInvestigationScan();
        currentAnimationHash = 0;
        Log("BOSS FINISHED INSPECTING COVER - CONTINUING SEARCH");
    }

    private bool TrySelectNearbyInvestigationCover()
    {
        Cover best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Cover candidate in FindObjectsOfType<Cover>())
        {
            if (candidate == null || candidate.IsDestroyed ||
                !candidate.isActiveAndEnabled || inspectedCovers.Contains(candidate)) continue;
            Collider collider = candidate.GetComponentInChildren<Collider>();
            Vector3 point = collider != null ? collider.ClosestPoint(transform.position) : candidate.transform.position;
            if (Vector3.Distance(candidate.transform.position, soundLocation) > nearbyCoverSearchRadius) continue;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(point, out hit, 3f, NavMesh.AllAreas)) continue;
            NavMeshPath path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            // Prefer clues near the last sighting, without selecting cover
            // that requires an excessively long detour from the monster.
            float travelDistance = Vector3.Distance(transform.position, hit.position);
            float clueDistance = Vector3.Distance(candidate.transform.position, lastKnownPlayerPosition);
            float distance = travelDistance * 0.35f + clueDistance * 0.65f;
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        if (best == null) return false;
        investigationCover = best;
        investigationCoverStartTime = Time.time;
        currentCover = best;
        inspectedCovers.Add(best);
        hasSearchPoint = false;
        nextCoverPath = 0f;
        Log("BOSS SEARCHING BEHIND / DESTROYING COVER: " + best.name);
        return true;
    }


    // Collect reachable viewpoints around nearby obstacle corners. The boss does
    // not know where the player went; it checks plausible blind angles.
    // Build a genuine perimeter route around the wall nearest the last sighting.
    // Waypoints are ordered clockwise/counterclockwise, not ranked independently;
    // this prevents the boss zigzagging between opposite sides of the wall.
    private void BuildWallCheckPoints(Vector3 center)
    {
        wallCheckPoints.Clear();
        if (!AgentReady()) return;

        Collider targetWall = null;
        float best = float.PositiveInfinity;
        Collider[] nearby = Physics.OverlapSphere(center, perimeterSearchRadius,
            searchObstructionLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider c in nearby)
        {
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (trackedPlayer != null && c.transform.IsChildOf(trackedPlayer.transform)) continue;
            if (c.GetComponentInParent<Cover>() != null) continue;
            Bounds b = c.bounds;
            // Ignore floors, small props, and very large enclosing geometry.
            if (b.size.y < 1.1f || Mathf.Max(b.size.x, b.size.z) < 1.2f ||
                Mathf.Max(b.size.x, b.size.z) > perimeterSearchRadius * 2.2f) continue;
            Vector3 nearest = b.ClosestPoint(center);
            float d = Vector3.Distance(center, nearest);
            if (d < best) { best = d; targetWall = c; }
        }

        if (targetWall != null)
        {
            Bounds b = targetWall.bounds;
            float clearance = Mathf.Max(perimeterClearance, agent.radius + 0.45f);
            float x0 = b.min.x - clearance, x1 = b.max.x + clearance;
            float z0 = b.min.z - clearance, z1 = b.max.z + clearance;
            float y = center.y;
            // Eight ordered points: four corners and four side midpoints.
            Vector3[] loop = {
                new Vector3(x0,y,z0), new Vector3((x0+x1)*0.5f,y,z0),
                new Vector3(x1,y,z0), new Vector3(x1,y,(z0+z1)*0.5f),
                new Vector3(x1,y,z1), new Vector3((x0+x1)*0.5f,y,z1),
                new Vector3(x0,y,z1), new Vector3(x0,y,(z0+z1)*0.5f)
            };
            int start = -1;
            float nearestDistance = float.PositiveInfinity;
            Vector3[] valid = new Vector3[8];
            bool[] usable = new bool[8];
            for (int i = 0; i < loop.Length; i++)
            {
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(loop[i], out hit, 1.5f, agent.areaMask)) continue;
                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                valid[i] = hit.position;
                usable[i] = true;
                float distance = Vector3.Distance(transform.position, hit.position);
                if (distance < nearestDistance) { nearestDistance = distance; start = i; }
            }
            if (start >= 0)
            {
                // Pick the direction that reaches the next valid waypoint with
                // the shorter path. Then commit to that direction for the loop.
                int direction = 1;
                int cw = (start + 1) % 8, ccw = (start + 7) % 8;
                if (usable[cw] && usable[ccw])
                {
                    NavMeshPath a = new NavMeshPath(), bPath = new NavMeshPath();
                    agent.CalculatePath(valid[cw], a);
                    agent.CalculatePath(valid[ccw], bPath);
                    if (PathDistance(bPath) < PathDistance(a)) direction = -1;
                }
                for (int step = 0; step < 8; step++)
                {
                    int i = (start + direction * step + 16) % 8;
                    if (!usable[i]) continue;
                    Vector3 point = valid[i];
                    if (wallCheckPoints.Exists(p => Vector3.Distance(p, point) < 0.8f)) continue;
                    wallCheckPoints.Add(point);
                }
            }
        }

        // If no suitable wall was found, fall back to a navigable loop around
        // the clue. Never pretend a straight-line point is a reachable flank.
        if (wallCheckPoints.Count == 0)
        {
            float radius = Mathf.Max(3f, wallFlankOffset + 1f);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidate, out hit, 1f, agent.areaMask)) continue;
                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                wallCheckPoints.Add(hit.position);
            }
        }
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
                Random.Range(0f, 0.5f) -
                Vector3.Distance(
                    hit.position,
                    soundLocation) * 0.1f;

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
        mode = BossMode.Cover;
        investigatingVisualClue = false;
        lostPlayerCoverSearch = false;
        suspicion = 0f;
        // A completed search clears visual suspicion; fresh sound can
        // still keep the same Fantacode meter visible.
        UpdateUnifiedAwareness();
        hasPartialVisualContact = false;
        partialVisualInvestigationStarted = false;
        lastPartialVisualContactTime = float.NegativeInfinity;
        coverHuntActive = false;
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
        if (!AgentReady()) return;
        // Ordinary wandering never destroys cover. Cover destruction is
        // reserved for searching after losing visual contact with the player.
        coverHuntActive = false;
        currentCover = null;
        attackingCover = false;
        hasLockedCoverRotation = false;
        if (Time.time < wanderPauseUntil)
        {
            StopAgent();
            return;
        }
        if (!hasWanderDestination || (!agent.pathPending &&
            (!agent.hasPath || agent.remainingDistance <= 1.2f)))
        {
            if (hasWanderDestination)
            {
                hasWanderDestination = false;
                wanderPauseUntil = Time.time + Random.Range(wanderPauseSeconds.x, wanderPauseSeconds.y);
                StopAgent();
                return;
            }
            for (int i = 0; i < 20; i++)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(wanderOrigin + new Vector3(offset.x, 0f, offset.y),
                    out hit, 3f, NavMesh.AllAreas)) continue;
                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                wanderDestination = hit.position;
                hasWanderDestination = true;
                break;
            }
        }
        if (hasWanderDestination)
        {
            agent.isStopped = false;
            agent.speed = wanderSpeed;
            agent.stoppingDistance = 0.7f;
            if (!agent.hasPath && !agent.pathPending)
                agent.SetDestination(wanderDestination);
            FaceMovementDirection();
        }
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
        if (mode != BossMode.Search || !lostPlayerCoverSearch || HasLiveSensoryClue() || !AgentReady())
            return;

        if (currentCover == null ||
            !currentCover.isActiveAndEnabled ||
            !currentCover.gameObject.activeInHierarchy ||
            currentCover.IsDestroyed)
        {
            currentCover = null;
            attackingCover = false;
            hasLockedCoverRotation = false;

            // Investigation never falls back to unrelated random cover.
            if (mode == BossMode.Search)
            {
                EndInvestigationCoverAttack();
                return;
            }
            if (mode == BossMode.Chase)
            {
                ClearChaseCover();
                nextChasePath = 0f;
                return;
            }

            if (Time.time >= nextCoverSearch)
            {
                FindNearestCover();
                nextCoverSearch =
                    Time.time + coverSearchInterval;
            }
        }

        if (currentCover == null)
        {
            attackingCover = false;
            hasLockedCoverRotation = false;

            if (agent.hasPath)
                agent.ResetPath();

            return;
        }

        Collider coverCollider =
            currentCover.GetComponentInChildren<Collider>();

        Vector3 navigationPoint =
            coverCollider != null
                ? coverCollider.ClosestPoint(transform.position)
                : currentCover.transform.position;

        Vector3 offset =
            navigationPoint - transform.position;

        offset.y = 0f;

        if (offset.magnitude <= attackDistance)
        {
            if (!attackingCover)
            {
                attackingCover = true;
                nextCoverFallbackHit = Time.time + Mathf.Max(0.2f, coverHitFallbackSeconds);

                StopAgent();
                LockCoverAttackRotation();
                ForceAnimation(attackHash);

                Log(
                    "BOSS ATTACKING COVER: " +
                    currentCover.name
                );
            }

            StopAgent();

            if (hasLockedCoverRotation)
                transform.rotation = lockedCoverRotation;

            if (Time.time >= nextCoverFallbackHit)
                DamageCover();

            return;
        }

        attackingCover = false;
        hasLockedCoverRotation = false;

        agent.isStopped = false;
        agent.speed = originalAgentSpeed;
        agent.stoppingDistance = 0f;

        if (Time.time >= nextCoverPath)
        {
            NavMeshHit reachable;
            if (NavMesh.SamplePosition(navigationPoint, out reachable, 3f, NavMesh.AllAreas))
                agent.SetDestination(reachable.position);
            else
            {
                if (mode == BossMode.Search)
                    EndInvestigationCoverAttack();
                else if (mode == BossMode.Chase)
                {
                    ClearChaseCover();
                    chaseCoverRetryUntil = Time.time + chaseCoverRetryDelay;
                }
                else
                    InterruptCoverAttack();
                return;
            }
            nextCoverPath =
                Time.time + coverPathRefreshInterval;
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

        transform.rotation = lockedCoverRotation;
    }

    // =====================================================
    // COVER DAMAGE / ANIMATION EVENT
    // =====================================================

    public void DamageCover()
    {
        if (mode != BossMode.Search || !lostPlayerCoverSearch || HasLiveSensoryClue() ||
            !attackingCover ||
            currentCover == null ||
            currentCover.IsDestroyed)
        {
            return;
        }

        nextCoverFallbackHit = Time.time + Mathf.Max(0.2f, coverHitFallbackSeconds);
        currentCover.TakeDamage(coverDamage);

        if (currentCover != null && !currentCover.IsDestroyed)
        {
            currentAnimationHash = 0;
            ForceAnimation(attackHash);
        }

        if (currentCover != null && currentCover.IsDestroyed)
        {
            currentCover = null;
            attackingCover = false;
            hasLockedCoverRotation = false;
            nextCoverSearch = 0f;
        }
    }

    private void InterruptCoverAttack()
    {
        bool wasAttacking = attackingCover;

        attackingCover = false;
        hasLockedCoverRotation = false;
        currentCover = null;
        coverHuntActive = false;
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
        visitedPoints.Clear();
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
        if (bossAnimator == null ||
            ChargeBusy ||
            mode == BossMode.Melee ||
            mode == BossMode.Detected ||
            mode == BossMode.Suspicious)
        {
            return;
        }

        if ((mode == BossMode.Cover || mode == BossMode.Search || mode == BossMode.Chase) && attackingCover)
        {
            PlayAnimation(attackHash);
            return;
        }

        float speed = measuredVelocity.magnitude;

        if (AgentReady())
        {
            Vector3 velocity = agent.velocity;
            velocity.y = 0f;

            speed = Mathf.Max(speed, velocity.magnitude);
        }

        PlayAnimation(
            speed > movementThreshold
                ? runHash
                : idleHash
        );
    }

    private void PlayAnimation(int hash)
    {
        if (currentAnimationHash != hash)
            ForceAnimation(hash);
    }

    private void ForceAnimation(int hash)
    {
        if (bossAnimator == null)
            return;

        bossAnimator.CrossFadeInFixedTime(
            hash,
            animationBlend,
            0
        );

        currentAnimationHash = hash;
    }

    private bool AgentReady()
    {
        return agent != null &&
               agent.isActiveAndEnabled &&
               agent.isOnNavMesh;
    }

    private void StopAgent()
    {
        if (!AgentReady())
            return;

        agent.isStopped = true;

        if (agent.hasPath)
            agent.ResetPath();
    }

    private void FaceMovementDirection()
    {
        if (!AgentReady())
            return;

        Vector3 direction = agent.desiredVelocity;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        FacePosition(
            transform.position + direction,
            turnSpeed
        );
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

        transform.rotation =
            Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                speed * Time.deltaTime
            );
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
                searchRadius
            );
        }

        if (mode == BossMode.Search && hasSearchPoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(searchPoint, 0.25f);
        }
    }
}
