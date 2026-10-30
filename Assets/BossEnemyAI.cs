
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
    [SerializeField] private string attackState = "attack";
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

    [Header("Cover Hunting")]
    [SerializeField] private float attackDistance = 2f;
    [SerializeField] private float coverDamage = 25f;
    [SerializeField] private float coverSearchInterval = 1f;
    [SerializeField] private float coverPathRefreshInterval = 0.25f;
    [SerializeField] private float coverAttackRotationOffset = 0f;

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
        Chase
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

        idleHash = Animator.StringToHash(idleState);
        runHash = Animator.StringToHash(runState);
        attackHash = Animator.StringToHash(attackState);
        detectedHash = Animator.StringToHash(detectedState);
        suspiciousHash = Animator.StringToHash(suspiciousState);

        originalAgentSpeed = agent.speed;
        originalStoppingDistance = agent.stoppingDistance;

        agent.updateRotation = false;

        previousPosition = transform.position;
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

        UpdateMeasuredVelocity();
        CheckVision();
        CheckPlayerMovement();

        switch (mode)
        {
            case BossMode.Cover:
                UpdateCoverHunting();
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

        if (mode == BossMode.Cover &&
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

        if (mode == BossMode.Detected ||
            mode == BossMode.Chase ||
            mode == BossMode.Suspicious ||
            mode == BossMode.InvestigateTravel ||
            mode == BossMode.InvestigateLook)
        {
            return;
        }

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

    private void BeginMovementInvestigation(Vector3 position)
    {
        if (mode == BossMode.Detected ||
            mode == BossMode.Chase ||
            mode == BossMode.Suspicious ||
            mode == BossMode.InvestigateTravel ||
            mode == BossMode.InvestigateLook)
        {
            return;
        }

        if (mode == BossMode.Cover)
            InterruptCoverAttack();

        CancelSearch();

        soundLocation = position;
        mode = BossMode.Suspicious;
        suspicionStartTime = Time.time;
        suspicionAnimationEntered = false;

        StopAgent();

        if (AgentReady())
            agent.velocity = Vector3.zero;

        if (bossAnimator.HasState(0, suspiciousHash))
        {
            ForceAnimation(suspiciousHash);
        }
        else
        {
            Debug.LogWarning(
                "BossEnemyAI: Missing Suspicious Animator state.",
                this
            );
        }

        Log(
            "BOSS SUSPICIOUS - WAITING BEFORE INVESTIGATION"
        );
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
        mode = BossMode.InvestigateTravel;
        investigationStartTime = Time.time;
        currentAnimationHash = 0;

        if (!AgentReady())
        {
            Debug.LogWarning(
                "BossEnemyAI: NavMeshAgent unavailable for investigation.",
                this
            );

            return;
        }

        NavMeshHit hit;

        if (!NavMesh.SamplePosition(
            soundLocation,
            out hit,
            soundPositionSampleRadius,
            NavMesh.AllAreas))
        {
            StartSearch(soundLocation);
            return;
        }

        agent.isStopped = false;
        agent.speed = investigateSpeed;
        agent.stoppingDistance =
            investigateStoppingDistance;

        bool pathSet =
            agent.SetDestination(hit.position);

        Log(
            "BOSS INVESTIGATING SOUND: " +
            hit.position +
            " | Path: " + pathSet
        );
    }

    private void UpdateInvestigationTravel()
    {
        if (!AgentReady())
            return;

        if (Time.time - investigationStartTime >
            investigateTimeout)
        {
            StartSearch(soundLocation);
            return;
        }

        agent.isStopped = false;
        agent.speed = investigateSpeed;
        agent.stoppingDistance =
            investigateStoppingDistance;

        NavMeshHit hit;

        if (!NavMesh.SamplePosition(
            soundLocation,
            out hit,
            soundPositionSampleRadius,
            NavMesh.AllAreas))
        {
            StartSearch(soundLocation);
            return;
        }

        agent.SetDestination(hit.position);
        FaceMovementDirection();

        if (!agent.pathPending &&
            Vector3.Distance(
                transform.position,
                hit.position) <=
            investigateStoppingDistance + 0.25f)
        {
            StopAgent();

            mode = BossMode.InvestigateLook;
            investigationLookStartTime = Time.time;

            Log(
                "BOSS REACHED LAST HEARD POSITION"
            );
        }
    }

    private void UpdateInvestigationLook()
    {
        StopAgent();

        transform.Rotate(
            Vector3.up,
            investigateLookTurnSpeed * Time.deltaTime,
            Space.World
        );

        if (Time.time - investigationLookStartTime >=
            investigateLookDuration)
        {
            StartSearch(soundLocation);
        }
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

        if (playerStimuli == null)
            return;

        foreach (VisualStimulus stimulus in playerStimuli)
        {
            if (stimulus == null ||
                !stimulus.gameObject.activeInHierarchy ||
                stimulus.Type != VisualStimulusType.Player)
            {
                continue;
            }

            if (stimulus.IsConcealed ||
                HidingArea.IsHidden(stimulus))
            {
                continue;
            }

            if (!CanSeePlayer(stimulus.transform))
                continue;

            lastKnownPlayerPosition =
                stimulus.transform.position;

            lastPlayerSeenTime = Time.time;

            // During a charge, vision may update,
            // but must not trigger new animation/state changes.
            if (mode != BossMode.Detected &&
                mode != BossMode.Chase &&
                !ChargeBusy)
            {
                DetectPlayer(stimulus.transform);
            }

            break;
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

        mode = BossMode.Detected;
        detectionEndTime = Time.time + detectedDuration;

        awareness.SetAwareness(1f);

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
            LosePlayer();
            return;
        }

        if (!AgentReady())
            return;

        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.stoppingDistance = chaseStoppingDistance;

        if (Time.time >= nextChasePath)
        {
            agent.SetDestination(trackedPlayer.position);
            nextChasePath =
                Time.time + chasePathRefreshInterval;
        }

        FaceMovementDirection();
    }

    private void LosePlayer()
    {
        if (ai.Target != null)
            ai.TargetHandler.TrySetTarget(null);

        trackedPlayer = null;
        lastPlayerSeenTime = float.NegativeInfinity;

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

        soundLocation = center;
        searchStartTime = Time.time;
        mode = BossMode.Search;
        currentAnimationHash = 0;

        StopAgent();

        Log("BOSS SEARCHING AROUND: " + center);
    }

    private void UpdateSearching()
    {
        if (!AgentReady())
            return;

        if (Time.time - searchStartTime > searchDuration)
        {
            FinishSearch();
            return;
        }

        if (searchPaused)
        {
            StopAgent();

            transform.Rotate(
                Vector3.up,
                investigateLookTurnSpeed * Time.deltaTime,
                Space.World
            );

            if (Time.time >= searchPauseEndTime)
            {
                searchPaused = false;
                hasSearchPoint = false;
            }

            return;
        }

        if (!hasSearchPoint)
        {
            if (!ChooseSearchPoint())
            {
                FinishSearch();
                return;
            }

            hasSearchPoint = true;
            searchPointStartTime = Time.time;

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
        FaceMovementDirection();

        if (!agent.pathPending &&
            Vector3.Distance(
                transform.position,
                searchPoint) <=
            searchArrivalDistance + 0.25f)
        {
            visitedPoints.Add(searchPoint);
            StopAgent();

            searchPaused = true;
            searchPauseEndTime =
                Time.time + searchPauseDuration;
        }
        else if (Time.time - searchPointStartTime >
                 searchPointTimeout)
        {
            visitedPoints.Add(searchPoint);
            hasSearchPoint = false;
            StopAgent();
        }
    }

    private bool ChooseSearchPoint()
    {
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

        Log(
            "BOSS FINISHED SEARCH - RETURNING TO COVER"
        );
    }

    // =====================================================
    // COVER HUNTING
    // =====================================================

    private void UpdateCoverHunting()
    {
        if (mode != BossMode.Cover || !AgentReady())
            return;

        if (currentCover == null ||
            !currentCover.isActiveAndEnabled ||
            !currentCover.gameObject.activeInHierarchy ||
            currentCover.IsDestroyed)
        {
            currentCover = null;
            attackingCover = false;
            hasLockedCoverRotation = false;

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

            return;
        }

        attackingCover = false;
        hasLockedCoverRotation = false;

        agent.isStopped = false;
        agent.speed = originalAgentSpeed;
        agent.stoppingDistance = 0f;

        if (Time.time >= nextCoverPath)
        {
            agent.SetDestination(navigationPoint);
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
        if (mode != BossMode.Cover ||
            !attackingCover ||
            currentCover == null ||
            currentCover.IsDestroyed)
        {
            return;
        }

        currentCover.TakeDamage(coverDamage);

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
            mode == BossMode.Detected ||
            mode == BossMode.Suspicious)
        {
            return;
        }

        if (mode == BossMode.Cover && attackingCover)
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
