
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FS_Core;

[DefaultExecutionOrder(10001)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BossEnemyAI))]
[RequireComponent(typeof(NavMeshAgent))]
public class BossChargeAttack : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Animator bossAnimator;

    [Header("Animation")]
    [SerializeField] private string detectedAnimation = "Detected";
    [SerializeField, Min(0f)] private float chargeWindupDuration = 0.8f;
    [SerializeField, Min(0f)] private float detectedCrossfade = 0.12f;
    [SerializeField] private bool facePlayerDuringWindup = true;
    [SerializeField] private string dashAnimation = "Dash";
    [SerializeField] private string stunnedAnimation = "Stunned";
    [SerializeField, Min(0f)] private float dashCrossfade = 0.1f;
    [SerializeField, Min(0f)] private float stunnedCrossfade = 0.1f;
    [SerializeField] private bool holdStunnedUntilRecoveryEnds = true;

    // No distance gate or cooldown: full visual detection always triggers a charge.

    [Header("Pre-Charge Wall Navigation")]
    [Tooltip("When a wall or durable cover blocks the charge lane, walk a NavMesh path around it BEFORE beginning Detected/Dash.")]
    [SerializeField] private bool navigateAroundWallsBeforeCharging = true;
    [SerializeField, Min(0.5f)] private float preChargeApproachSpeed = 5.5f;
    [SerializeField, Min(0.1f)] private float preChargeApproachTimeout = 18f;
    [SerializeField, Min(0.1f)] private float preChargePathRefreshInterval = 0.15f;
    [SerializeField, Min(0.05f)] private float preChargeCorridorRadius = 0.42f;
    [SerializeField] private string approachAnimation = "run";

    [Header("Charge Movement")]
    [SerializeField, Min(0f)] private float chargeSpeed = 15f;
    [Tooltip("Optional fail-safe limit (0 = charge continuously until an impact/stun).")]
    [SerializeField, Min(0f)] private float maximumChargeSeconds = 0f;
    [Tooltip("How long to continue straight ahead after a dodge before entering Stunned.")]
    [SerializeField, Min(0.1f)] private float postDodgeStraightDuration = 1.5f;
    [SerializeField, Min(0f)] private float chargeTurnSpeed = 400f;
    [Tooltip("How often to update the pursuit destination while NavMesh controls the charge.")]
    [SerializeField, Min(0.03f)] private float chargePathRefreshInterval = 0.1f;
    [SerializeField, Min(0.2f)] private float playerNavMeshSampleRadius = 2f;
    [SerializeField, Min(1f)] private float chargeAcceleration = 100f;

    [Header("Charge Navigation / Anti-Stuck")]
    [Tooltip("Do not throw away a working route just because the player moved a few centimeters.")]
    [SerializeField, Min(0.25f)] private float chargeRetargetDistance = 1.25f;
    [Tooltip("Elapsed time with almost no real movement before attempting another route.")]
    [SerializeField, Min(0.4f)] private float chargeStuckCheckSeconds = 1.4f;
    [SerializeField, Min(0.05f)] private float chargeMinimumProgress = 0.28f;
    [SerializeField, Range(1, 10)] private int chargeMaximumRouteRecoveries = 5;
    [SerializeField, Min(1.5f)] private float chargeSideRouteRadius = 5f;
    [SerializeField, Min(0.5f)] private float chargeSideRouteHoldSeconds = 2.4f;
    [SerializeField] private bool logChargeNavigationRecovery = true;
    [Tooltip("Proactive cover impact probe, in addition to the distance moved this frame.")]
    [SerializeField, Min(0.1f)] private float coverLookAhead = 1.25f;
    [Header("Charge Cover Impact Rules")]
    [Tooltip("EVERY Cover physically hit by the charge is destroyed in one impact. If its CURRENT health before destruction is ABOVE this value, the boss is also stunned.")]
    [SerializeField, Min(0.01f)] private float coverImpactStunThreshold = 75f;

    [Header("Recovery")]
    [Tooltip("Exact recovery time in seconds. Animation length is ignored.")]
    [SerializeField, Min(0f)] private float recoveryDuration = 1.5f;
    [SerializeField, Min(0f)] private float navMeshRecoverySampleRadius = 3f;

    [Header("Collision")]
    [SerializeField, Min(0.05f)] private float collisionRadius = 0.55f;
    [SerializeField] private float collisionHeight = 1f;
    [SerializeField, Min(0f)] private float coverDamage = 25f;

    [Tooltip("Include cover and walls. Exclude ground and debris.")]
    [SerializeField] private LayerMask obstacleLayers = 0;

    [SerializeField] private bool ignoreCoverFragments = true;
    [SerializeField] private string fragmentNamePrefix = "Cover_Fragment";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool logMovement = false;

    public bool IsPositioning { get; private set; }
    public bool IsWindingUp { get; private set; }
    public bool IsCharging { get; private set; }
    public bool IsRecovering { get; private set; }

    // Legacy name: means steering is locked, NOT sight lost.
    public bool HasLostTracking { get; private set; }

    public bool IsBusy => IsPositioning || IsWindingUp || IsCharging || IsRecovering;

    public float RecoveryTimeRemaining =>
        IsRecovering
            ? Mathf.Max(0f, recoveryEndTime - Time.time)
            : 0f;

    private BossEnemyAI boss;
    private AIController ai;
    private NavMeshAgent agent;
    private PlayerDodgeRoll playerDodge;

    private bool aiWasEnabled;
    private bool agentWasEnabled;
    private float agentOriginalSpeed;
    private float agentOriginalAcceleration;
    private float agentOriginalStoppingDistance;
    private bool agentOriginalAutoBraking;
    private float nextChargePathRefresh;
    private Vector3 priorChargePosition;
    // Watch the transform, not agent.desiredVelocity: desiredVelocity can be
    // nonzero while the boss is completely motionless against an obstacle.
    private float lastChargeMotionAt;
    private Vector3 lastChargeMotionPosition;
    private Vector3 chargeCommittedDestination;
    private Vector3 chargePlayerPositionAtLastPlan;
    private bool hasChargeCommittedDestination;
    private int chargeRouteRecoveries;
    private bool chargeUsingSideRoute;
    private Vector3 chargeSideRoutePoint;
    private float chargeSideRouteStartedAt;
    private Vector3 lastFailedSideRoutePoint;
    private bool hasFailedSideRoute;
    private Vector3 chargeLastRecoveryPosition;

    private Vector3 preChargeCommittedDestination;
    private Vector3 preChargePlayerPositionAtLastPlan;
    private bool hasPreChargeCommittedDestination;
    private float lastPreChargeMotionAt;
    private Vector3 lastPreChargeMotionPosition;
    private int preChargeRecoveryAttempts;
    private bool preChargeUsingSideRoute;
    private Vector3 preChargeSideRoutePoint;
    private float preChargeSideRouteStartedAt;
    // Save existing cover obstacle state while charging. All Cover objects
    // become pass-through targets on the NavMesh, irrespective of health.
    private struct StoredCoverObstacle
    {
        public NavMeshObstacle obstacle;
        public bool wasEnabled;
        public bool wasCarving;

        public StoredCoverObstacle(NavMeshObstacle value)
        {
            obstacle = value;
            wasEnabled = value.enabled;
            wasCarving = value.carving;
        }
    }

    private readonly List<StoredCoverObstacle> originalCoverObstacles =
        new List<StoredCoverObstacle>();
    private readonly HashSet<NavMeshObstacle> storedCoverObstacleSet =
        new HashSet<NavMeshObstacle>();
    private int coverColliderLayers;
    private bool agentWasStopped;
    private bool agentWasUpdatingPosition;
    private bool agentWasUpdatingRotation;
    private bool savedControllerState;
    private bool animatorRootMotionWasEnabled;
    private bool savedAnimatorState;

    private float preChargePositioningStartedAt;
    private float nextPreChargePathRefresh;
    private Vector3 dodgeLastKnownPosition;
    private bool hasDodgeLastKnownPosition;
    private float chargeStartTime;
    private float postDodgeStartedAt;
    private float windupEndTime;
    private float recoveryEndTime;

    private Vector3 chargeDirection;
    private Vector3 chargeStartPosition;

    private int dodgeSequenceAtChargeStart;
    private int stunnedHash;
    private bool stunnedEntered;

    private readonly HashSet<Cover> hitCovers =
        new HashSet<Cover>();

    private readonly HashSet<Transform> destroyedCoverRoots =
        new HashSet<Transform>();

    private readonly HashSet<Collider> ignoredDebrisColliders =
        new HashSet<Collider>();

    private void Awake()
    {
        boss = GetComponent<BossEnemyAI>();
        ai = GetComponent<AIController>();
        agent = GetComponent<NavMeshAgent>();

        if (bossAnimator == null)
        {
            Animator root = GetComponent<Animator>();

            foreach (Animator candidate in
                GetComponentsInChildren<Animator>(true))
            {
                if (candidate != root)
                {
                    bossAnimator = candidate;
                    break;
                }
            }

            if (bossAnimator == null)
                bossAnimator = root;
        }

        stunnedHash = Animator.StringToHash(stunnedAnimation);
    }

    private void Start()
    {
        ResolvePlayer();

        if (bossAnimator == null)
        {
            Debug.LogError(
                "[BossChargeAttack] Missing boss Animator.",
                this
            );
            enabled = false;
        }
    }

    private void Update()
    {
        if (IsPositioning)
        {
            UpdatePreChargePositioning();
            return;
        }

        if (IsWindingUp)
        {
            UpdateWindup();
            return;
        }

        if (IsCharging)
        {
            UpdateCharge();
            return;
        }

        if (IsRecovering)
        {
            UpdateRecovery();
            return;
        }

        // Full visual detection is the ONLY trigger; never reject on range.
        // During stun IsRecovering handles the frame before this code runs.
        if (boss == null || !boss.HasVisualCombatTarget || !ResolvePlayer())
            return;

        BeginWindup();
    }

    private bool ResolvePlayer()
    {
        if (boss != null && boss.VisualCombatTarget != null)
            player = boss.VisualCombatTarget;

        if (player == null)
            return false;

        playerDodge = player.GetComponent<PlayerDodgeRoll>();

        if (playerDodge == null)
            playerDodge =
                player.GetComponentInParent<PlayerDodgeRoll>();

        if (playerDodge == null)
            playerDodge =
                player.GetComponentInChildren<PlayerDodgeRoll>(true);

        if (playerDodge == null)
            playerDodge =
                player.root.GetComponentInChildren<PlayerDodgeRoll>(true);

        return true;
    }

    private void BeginWindup()
    {
        if (IsBusy || player == null || agent == null ||
            !agent.enabled || !agent.isOnNavMesh)
            return;

        Vector3 towardPlayer = player.position - transform.position;
        towardPlayer.y = 0f;
        if (towardPlayer.sqrMagnitude < 0.0001f)
            return;

        // Prepare dynamic cover obstacles before judging the lane. A carved
        // obstacle may force a real detour even if the target is straight ahead.
        if (!savedControllerState)
        {
            SaveAndPauseControllers();
            PrepareCoverNavMeshObstacles();
        }

        if (navigateAroundWallsBeforeCharging && !HasClearPreChargeLane())
        {
            BeginPreChargePositioning();
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
        transform.rotation = Quaternion.LookRotation(towardPlayer.normalized);
        IsWindingUp = true;
        windupEndTime = Time.time + chargeWindupDuration;
        PlayState(detectedAnimation, detectedCrossfade);
        Log("CHARGE WINDUP - NavMesh pursuit next");
    }

    // Pre-charge navigation is NOT the dash. The normal agent follows a
    // walkable route around obstacles; Detected and Dash begin once there is
    // a clear, walkable corridor. This prevents immediately stunning against
    // the wall right in front of the boss.
    private void BeginPreChargePositioning()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            AbortPreChargePositioning("Agent unavailable");
            return;
        }
        IsPositioning = true;
        preChargePositioningStartedAt = Time.time;
        nextPreChargePathRefresh = 0f;
        hasPreChargeCommittedDestination = false;
        lastPreChargeMotionAt = Time.time;
        lastPreChargeMotionPosition = transform.position;
        preChargeRecoveryAttempts = 0;
        preChargeUsingSideRoute = false;
        agent.speed = preChargeApproachSpeed;
        agent.acceleration = Mathf.Max(20f, chargeAcceleration);
        agent.stoppingDistance = 0.3f;
        agent.autoBraking = false;
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.isStopped = false;
        RefreshPreChargePath();
        if (bossAnimator != null && bossAnimator.HasState(0, Animator.StringToHash(approachAnimation)))
            PlayState(approachAnimation, 0.12f);
        Log("WALL BLOCKING CHARGE - NAVMESH APPROACH BEFORE WINDUP");
    }

    private void UpdatePreChargePositioning()
    {
        if (player == null || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            AbortPreChargePositioning("Lost target or agent");
            return;
        }

        if (HasClearPreChargeLane())
        {
            // The boss has actually flanked the obstruction. Do not enter
            // Dash while a wall still lies between the boss and player.
            IsPositioning = false;
            agent.isStopped = true;
            if (agent.hasPath) agent.ResetPath();
            Log("CHARGE LANE CLEAR - STARTING DETECTED WINDUP");
            BeginWindup();
            return;
        }

        if (Time.time - preChargePositioningStartedAt >= preChargeApproachTimeout)
        {
            AbortPreChargePositioning("Could not find a navigable charge lane");
            return;
        }

        agent.isStopped = false;
        agent.speed = preChargeApproachSpeed;
        agent.updatePosition = true;
        if (preChargeUsingSideRoute)
        {
            bool finished = !agent.pathPending &&
                FlatChargeDistance(transform.position, preChargeSideRoutePoint) < 0.95f;
            bool expired = Time.time - preChargeSideRouteStartedAt >=
                chargeSideRouteHoldSeconds;
            if (finished || expired)
            {
                preChargeUsingSideRoute = false;
                hasPreChargeCommittedDestination = false;
                nextPreChargePathRefresh = 0f;
            }
        }
        if (!preChargeUsingSideRoute)
            RefreshPreChargePath();

        TrackPlayer();
        if (FlatChargeDistance(transform.position, lastPreChargeMotionPosition) >=
            chargeMinimumProgress)
        {
            lastPreChargeMotionPosition = transform.position;
            lastPreChargeMotionAt = Time.time;
        }
        else if (Time.time - lastPreChargeMotionAt >= chargeStuckCheckSeconds)
        {
            preChargeRecoveryAttempts++;
            lastPreChargeMotionAt = Time.time;
            if (preChargeRecoveryAttempts > chargeMaximumRouteRecoveries)
            {
                AbortPreChargePositioning("Repeatedly blocked before charge");
                return;
            }
            Vector3 sidePoint;
            NavMeshPath sidePath;
            if (TryFindSideRoute(player.position, preChargeRecoveryAttempts,
                out sidePoint, out sidePath))
            {
                agent.ResetPath();
                if (agent.SetPath(sidePath))
                {
                    preChargeUsingSideRoute = true;
                    preChargeSideRoutePoint = sidePoint;
                    preChargeSideRouteStartedAt = Time.time;
                    NavLog("PRE-CHARGE SIDE ROUTE " + preChargeRecoveryAttempts +
                        " -> " + sidePoint);
                    return;
                }
            }
            hasPreChargeCommittedDestination = false;
            nextPreChargePathRefresh = 0f;
            NavLog("Pre-charge path stalled; forcing new route");
        }
    }

    private void RefreshPreChargePath()
    {
        if (Time.time < nextPreChargePathRefresh || player == null ||
            agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        nextPreChargePathRefresh = Time.time + preChargePathRefreshInterval;

        bool validExistingPath = agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete);
        if (hasPreChargeCommittedDestination && validExistingPath &&
            FlatChargeDistance(preChargePlayerPositionAtLastPlan, player.position) <
                chargeRetargetDistance) return;
        Vector3 desired;
        NavMeshPath path;
        if (!TryFindReachablePlayerPoint(out desired, out path)) return;
        // The path may be pending; don't replace it with an identical goal.
        validExistingPath = agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete);
        if (hasPreChargeCommittedDestination && validExistingPath &&
            FlatChargeDistance(preChargeCommittedDestination, desired) <
                chargeRetargetDistance) return;
        if (agent.SetPath(path))
        {
            preChargeCommittedDestination = desired;
            preChargePlayerPositionAtLastPlan = player.position;
            hasPreChargeCommittedDestination = true;
        }
    }

    private bool HasClearPreChargeLane()
    {
        if (player == null || agent == null || !agent.enabled || !agent.isOnNavMesh)
            return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.04f) return true;

        // Cover of ANY health is smashable. Only solid geometry without
        // Cover blocks the charge lane. Ignore self and player colliders.
        int mask = obstacleLayers.value == 0
            ? ~(1 << 2) : (obstacleLayers.value | coverColliderLayers) & ~(1 << 2);
        float radius = Mathf.Max(0.05f, preChargeCorridorRadius);
        float lowY = radius + 0.1f;
        float highY = Mathf.Max(lowY + 0.2f, collisionHeight * 1.6f);
        Vector3 bottom = transform.position + Vector3.up * lowY;
        Vector3 top = transform.position + Vector3.up * highY;
        float castLength = Mathf.Max(0f, toPlayer.magnitude - radius - 0.4f);
        // Check overlaps as well as forward casts. Unity's capsule cast
        // can miss a wall that starts already intersecting the body capsule.
        Collider[] nearColliders = Physics.OverlapCapsule(bottom, top, radius,
            mask, QueryTriggerInteraction.Ignore);
        foreach (Collider collider in nearColliders)
        {
            if (IsPreChargeBlockingCollider(collider)) return false;
        }
        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius,
            toPlayer.normalized, castLength, mask, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (IsPreChargeBlockingCollider(hit.collider)) return false;
        }

        // Geometric line of sight alone is not sufficient. Require the
        // direct corridor to be walkable on the NavMesh as well (includes
        // carved obstacles once their NavMesh update has been applied).
        NavMeshHit navTarget;
        if (!NavMesh.SamplePosition(player.position, out navTarget,
            Mathf.Max(playerNavMeshSampleRadius, 3f), agent.areaMask)) return false;
        NavMeshHit navBlock;
        return !NavMesh.Raycast(agent.nextPosition, navTarget.position,
            out navBlock, agent.areaMask);
    }

    private bool IsPreChargeBlockingCollider(Collider collider)
    {
        if (collider == null) return false;
        Transform t = collider.transform;
        if (t == transform || t.IsChildOf(transform)) return false;
        if (player != null && (t == player || t.IsChildOf(player) ||
            t == player.root || t.IsChildOf(player.root))) return false;
        if (ignoreCoverFragments && IsCoverFragment(collider)) return false;
        // Cover is ALWAYS destructible while charging. It cannot prevent
        // the pre-charge lane check based on its remaining health.
        // Non-Cover geometry must still be navigated around.
        Cover cover = collider.GetComponentInParent<Cover>();
        if (cover != null) return false;
        return true;
    }

    private void AbortPreChargePositioning(string reason)
    {
        IsPositioning = false;
        Vector3 clue = player != null ? player.position : transform.position;
        RestoreControllers();
        if (boss != null)
            boss.SchedulePostChargeSuspicion(clue, clue);
        Log("COULD NOT REACH CLEAR CHARGE LANE - INVESTIGATING INSTEAD: " + reason);
    }

    private void UpdateWindup()
    {
        if (facePlayerDuringWindup && player != null)
        {
            Vector3 direction = player.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, Quaternion.LookRotation(direction.normalized),
                    chargeTurnSpeed * Time.deltaTime);
        }

        if (Time.time >= windupEndTime)
        {
            IsWindingUp = false;
            if (navigateAroundWallsBeforeCharging && !HasClearPreChargeLane())
            {
                BeginPreChargePositioning();
                return;
            }
            BeginCharge();
        }
    }

    private void BeginCharge()
    {
        if (IsBusy || player == null)
        {
            // Never leave AI disabled after an interrupted wind-up.
            RestoreControllers();
            return;
        }

        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f ||
            agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            RestoreControllers();
            return;
        }

        chargeDirection = direction.normalized;
        chargeStartPosition = transform.position;
        priorChargePosition = transform.position;
        lastChargeMotionAt = Time.time;
        lastChargeMotionPosition = transform.position;
        chargeStartTime = Time.time;
        nextChargePathRefresh = 0f;
        hasChargeCommittedDestination = false;
        chargeRouteRecoveries = 0;
        chargeUsingSideRoute = false;
        hasFailedSideRoute = false;
        chargeLastRecoveryPosition = transform.position;
        dodgeSequenceAtChargeStart = playerDodge != null
            ? playerDodge.DodgeSequence : -1;
        HasLostTracking = false;
        hasDodgeLastKnownPosition = false;
        hitCovers.Clear();
        destroyedCoverRoots.Clear();
        ignoredDebrisColliders.Clear();

        // NavMeshAgent owns position until the player dodges.
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.speed = chargeSpeed;
        agent.acceleration = Mathf.Max(chargeAcceleration, chargeSpeed * 3f);
        agent.stoppingDistance = 0f;
        agent.autoBraking = false;
        agent.isStopped = false;
        agent.ResetPath();

        IsCharging = true;
        PlayState(dashAnimation, dashCrossfade);
        RefreshChargeDestination();
        Log("CHARGE STARTED - NAVMESH TRACKING | speed=" + chargeSpeed);
    }

    private void SaveAndPauseControllers()
    {
        aiWasEnabled = ai != null && ai.enabled;
        agentWasEnabled = agent != null && agent.enabled;
        agentWasStopped = agentWasEnabled && agent.isOnNavMesh && agent.isStopped;
        agentWasUpdatingPosition = agentWasEnabled && agent.updatePosition;
        agentWasUpdatingRotation = agentWasEnabled && agent.updateRotation;
        if (agentWasEnabled)
        {
            agentOriginalSpeed = agent.speed;
            agentOriginalAcceleration = agent.acceleration;
            agentOriginalStoppingDistance = agent.stoppingDistance;
            agentOriginalAutoBraking = agent.autoBraking;
        }
        savedControllerState = true;

        if (ai != null) ai.enabled = false;
        if (agentWasEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
            // Keep position syncing available to NavMesh when the charge starts.
            agent.updatePosition = true;
            agent.updateRotation = false;
        }

        if (bossAnimator != null)
        {
            animatorRootMotionWasEnabled = bossAnimator.applyRootMotion;
            savedAnimatorState = true;
            bossAnimator.applyRootMotion = false;
        }
    }

    private void UpdateCharge()
    {
        // Normal charges have no fixed duration. The boss continues pursuing
        // until a physical impact causes a stun, or an optional safety cap is set.
        if (maximumChargeSeconds > 0f &&
            Time.time - chargeStartTime >= maximumChargeSeconds)
        {
            StopCharge("Optional charge safety timeout", true);
            return;
        }

        CheckPlayerDodge();
        if (!IsCharging) return;

        if (HasLostTracking)
        {
            // A dodge commits to a short straight-line charge and then a
            // stun. Pre-dodge pursuit itself has no fixed duration.
            if (Time.time - postDodgeStartedAt >= postDodgeStraightDuration)
            {
                StopCharge("Post-dodge straight charge completed", true);
                return;
            }
            // NavMesh has been disabled; no more steering or player tracking.
            Vector3 displacement = chargeDirection * chargeSpeed * Time.deltaTime;
            if (displacement.sqrMagnitude < 0.000001f) return;

            if (CheckChargeCollisions(displacement, true)) return;

            transform.position += displacement;
            transform.rotation = Quaternion.LookRotation(chargeDirection);
            priorChargePosition = transform.position;
        }
        else
        {
            // Agent steers along a walkable route until the player dodges.
            UpdateNavMeshCharge();
        }

        if (logMovement)
            Log("CHARGE MOVED | " + transform.position +
                " | Mode=" + (HasLostTracking ? "STRAIGHT" : "NAVMESH"));
    }

    private void CheckPlayerDodge()
    {
        if (HasLostTracking || playerDodge == null ||
            playerDodge.DodgeSequence <= dodgeSequenceAtChargeStart)
            return;

        // Lock the facing direction at the instant of the dodge.
        // Do not swerve toward the player's new position or snap to a
        // different avoidance velocity when switching navigation modes.
        Vector3 heading = transform.forward;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.04f && agent != null && agent.enabled)
        {
            heading = agent.velocity;
            heading.y = 0f;
        }
        if (heading.sqrMagnitude > 0.0001f)
            chargeDirection = heading.normalized;

        // Freeze the exact location where the dodge occurred; after the
        // charge/stun, investigate THIS point, not the player's newer position.
        dodgeLastKnownPosition = player != null ? player.position :
            transform.position + chargeDirection * 3f;
        hasDodgeLastKnownPosition = true;
        HasLostTracking = true;
        chargeUsingSideRoute = false;
        postDodgeStartedAt = Time.time;
        if (agent != null && agent.enabled)
        {
            // The player has dodged: release NavMesh control completely.
            agent.enabled = false;
        }
        // No NavMesh constraints remain during straight-line charging.
        // Durable cover is now smashable, regardless of its remaining health.
        DisableCoverObstaclesForStraightCharge();
        transform.rotation = Quaternion.LookRotation(chargeDirection);
        priorChargePosition = transform.position;
        Log("PLAYER DODGED - NAVMESH DISABLED, STRAIGHT-LINE CHARGE LOCKED");
    }

    private void TrackPlayer()
    {
        // Kept as a small helper for consistency with older code paths.
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        Vector3 direction = agent.steeringTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = agent.velocity;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        Quaternion heading = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, heading, chargeTurnSpeed * Time.deltaTime);
        chargeDirection = transform.forward;
    }

    // Every Cover object is charge-breakable, including 100+ HP cover.
    // During an active charge, disable existing NavMeshObstacle components
    // on Cover so the agent can plan a route THROUGH it, not around it.
    // The original state is restored at recovery if that cover survives.
    private void PrepareCoverNavMeshObstacles()
    {
        RestoreCoverNavMeshObstacles();
        int coverCount = 0;

        foreach (Cover cover in FindObjectsOfType<Cover>())
        {
            if (cover == null || cover.IsDestroyed || !cover.isActiveAndEnabled)
                continue;

            coverCount++;
            foreach (Collider part in cover.GetComponentsInChildren<Collider>(true))
            {
                if (part != null && part.enabled && !part.isTrigger)
                    coverColliderLayers |= 1 << part.gameObject.layer;
            }

            foreach (NavMeshObstacle obstacle in cover.GetComponentsInChildren<NavMeshObstacle>(true))
            {
                if (obstacle == null) continue;
                StoreExistingCoverObstacle(obstacle);
                obstacle.enabled = false;
            }
        }

        Log("ALL COVER BREAKABLE DURING CHARGE | objects=" + coverCount +
            " | stun only above " + coverImpactStunThreshold.ToString("F1") + " HP");
    }

    private void StoreExistingCoverObstacle(NavMeshObstacle obstacle)
    {
        if (obstacle != null && storedCoverObstacleSet.Add(obstacle))
            originalCoverObstacles.Add(new StoredCoverObstacle(obstacle));
    }

    private void DisableCoverObstaclesForStraightCharge()
    {
        foreach (StoredCoverObstacle item in originalCoverObstacles)
            if (item.obstacle != null) item.obstacle.enabled = false;
    }

    private void RestoreCoverNavMeshObstacles()
    {
        foreach (StoredCoverObstacle item in originalCoverObstacles)
        {
            if (item.obstacle == null) continue; // Cover may have fractured.
            item.obstacle.enabled = false;
            item.obstacle.carving = item.wasCarving;
            item.obstacle.enabled = item.wasEnabled;
        }
        originalCoverObstacles.Clear();
        storedCoverObstacleSet.Clear();

        coverColliderLayers = 0;
    }

    // A route is planned only when the player has moved meaningfully, the
    // current route becomes invalid, or we explicitly request a recovery.
    // Calling SetDestination every 0.1 seconds with a near-identical point
    // repeatedly restarts NavMesh planning and can produce in-place dashing.
    private void RefreshChargeDestination(bool force = false)
    {
        if (player == null || agent == null || !agent.enabled ||
            !agent.isOnNavMesh ||
            (!force && Time.time < nextChargePathRefresh)) return;
        nextChargePathRefresh = Time.time + chargePathRefreshInterval;

        bool pathUsable = agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete);
        if (!force && hasChargeCommittedDestination && pathUsable &&
            FlatChargeDistance(chargePlayerPositionAtLastPlan, player.position) <
                chargeRetargetDistance)
            return;
        Vector3 desired;
        NavMeshPath route;
        if (!TryFindReachablePlayerPoint(out desired, out route))
        {
            if (force) NavLog("No complete path toward the player's area");
            return;
        }
        pathUsable = agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete);
        if (!force && hasChargeCommittedDestination && pathUsable &&
            FlatChargeDistance(chargeCommittedDestination, desired) <
                chargeRetargetDistance)
            return;

        // SetPath uses the already validated route. Don't replace movement
        // with a partial route or clear it because a new path could not be found.
        if (agent.SetPath(route))
        {
            chargeCommittedDestination = desired;
            chargePlayerPositionAtLastPlan = player.position;
            hasChargeCommittedDestination = true;
            if (force) NavLog("NavMesh route refreshed -> " + desired);
        }
    }

    // NavMesh.SamplePosition finds nearby *walkable* geometry, not necessarily
    // geometry reachable from the boss. Check PathComplete before committing.
    // If the player stands against a carved wall, sample nearby approaches.
    private bool TryFindReachablePlayerPoint(out Vector3 destination,
        out NavMeshPath route)
    {
        destination = Vector3.zero;
        route = null;
        if (player == null || agent == null || !agent.enabled ||
            !agent.isOnNavMesh) return false;

        float bestScore = float.PositiveInfinity;
        Vector3 target = player.position;
        for (int ring = 0; ring <= 3; ring++)
        {
            float radius = ring == 0 ? 0f : ring * 1.4f;
            int samples = ring == 0 ? 1 : 12;
            for (int i = 0; i < samples; i++)
            {
                float angle = i * Mathf.PI * 2f / samples;
                Vector3 test = target + new Vector3(
                    Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                NavMeshHit hit;
                float snap = ring == 0 ? playerNavMeshSampleRadius : 0.95f;
                if (!NavMesh.SamplePosition(test, out hit,
                    Mathf.Max(0.2f, snap), agent.areaMask)) continue;
                NavMeshPath candidate = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, candidate) ||
                    candidate.status != NavMeshPathStatus.PathComplete) continue;
                float score = FlatChargeDistance(hit.position, target) * 5f +
                    CalculatePathLength(candidate) * 0.25f;
                if (score >= bestScore) continue;
                destination = hit.position;
                route = candidate;
                bestScore = score;
            }
            // A complete route to the closest player sample is usually ideal.
            // The rings are a fallback only when that sample is unreachable.
            if (route != null && ring == 0) return true;
        }
        return route != null;
    }

    private static float CalculatePathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null) return float.PositiveInfinity;
        float distance = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            distance += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return distance;
    }

    // After an observed stall, try an *ordered, alternative* reachable point
    // to one side of the boss. Keep that path long enough to get around the
    // obstruction; do not immediately retarget to the player and undo it.
    private bool TryFindSideRoute(Vector3 goal, int recoveryNumber,
        out Vector3 destination, out NavMeshPath route)
    {
        destination = Vector3.zero;
        route = null;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return false;

        Vector3 forward = goal - transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.04f) forward = transform.forward;
        forward.Normalize();
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        float best = float.PositiveInfinity;
        int preferredSide = recoveryNumber % 2 == 0 ? -1 : 1;
        for (int step = 0; step < 3; step++)
        {
            float radius = chargeSideRouteRadius * (0.6f + step * 0.45f);
            for (int i = 0; i < 8; i++)
            {
                int side = i % 2 == 0 ? preferredSide : -preferredSide;
                float forwardPart = i < 4 ? 0.65f : -0.30f;
                Vector3 offset = (right * side + forward * forwardPart).normalized * radius;
                Vector3 candidatePos = transform.position + offset;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidatePos, out hit, 0.8f,
                    agent.areaMask)) continue;
                float displacement = FlatChargeDistance(hit.position, transform.position);
                if (displacement < 1.5f) continue;
                if (hasFailedSideRoute &&
                    FlatChargeDistance(hit.position, lastFailedSideRoutePoint) < 1.5f)
                    continue;
                NavMeshPath candidate = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, candidate) ||
                    candidate.status != NavMeshPathStatus.PathComplete) continue;
                float cost = CalculatePathLength(candidate) +
                    FlatChargeDistance(hit.position, goal) * 0.15f + step * 0.5f;
                if (cost >= best) continue;
                best = cost;
                destination = hit.position;
                route = candidate;
            }
        }
        return route != null;
    }

    private void UpdateNavMeshCharge()
    {
        if (agent == null || !agent.enabled)
        {
            StopCharge("NavMesh charge lost agent - recovering");
            return;
        }
        if (!agent.isOnNavMesh)
        {
            // Runtime carving can sometimes invalidate the agent directly
            // under its feet. Reattach only within a small local distance.
            NavMeshHit nearGround;
            if (!NavMesh.SamplePosition(transform.position, out nearGround, 1.0f,
                NavMesh.AllAreas) || !agent.Warp(nearGround.position) ||
                !agent.isOnNavMesh)
            {
                StopCharge("Agent could not return to nearby NavMesh", true);
                return;
            }
            hasChargeCommittedDestination = false;
            chargeUsingSideRoute = false;
            nextChargePathRefresh = 0f;
            lastChargeMotionAt = Time.time;
            lastChargeMotionPosition = transform.position;
            NavLog("Reattached charge agent to nearby walkable NavMesh");
        }

        agent.speed = chargeSpeed;
        agent.updatePosition = true;
        if (agent.isStopped) agent.isStopped = false;

        if (chargeUsingSideRoute)
        {
            bool reached = !agent.pathPending &&
                FlatChargeDistance(transform.position, chargeSideRoutePoint) <= 0.85f;
            bool expired = Time.time - chargeSideRouteStartedAt >=
                chargeSideRouteHoldSeconds;
            if (reached || expired)
            {
                if (expired && !reached)
                {
                    lastFailedSideRoutePoint = chargeSideRoutePoint;
                    hasFailedSideRoute = true;
                }
                chargeUsingSideRoute = false;
                hasChargeCommittedDestination = false;
                nextChargePathRefresh = 0f;
            }
        }
        if (!chargeUsingSideRoute)
            RefreshChargeDestination();
        TrackPlayer();

        // Test physical body displacement plus a short prediction along the
        // NavMesh route. A distant heavy cover should not cause a stun.
        Vector3 actualStep = transform.position - priorChargePosition;
        actualStep.y = 0f;
        if (actualStep.sqrMagnitude > 0.0001f)
        {
            if (ProbeChargeHits(priorChargePosition, actualStep.normalized,
                    actualStep.magnitude + collisionRadius, false)) return;
        }
        else
        {
            // If the agent is pressed against Cover, its real displacement
            // may be zero. Still register overlap as a collision rather than
            // playing Dash endlessly without destroying the object.
            if (ProbeChargeHits(transform.position, transform.forward,
                    0.02f, false)) return;
        }
        priorChargePosition = transform.position;

        Vector3 ahead = agent.desiredVelocity;
        ahead.y = 0f;
        if (ahead.sqrMagnitude < 0.01f)
            ahead = agent.steeringTarget - transform.position;
        ahead.y = 0f;
        if (ahead.sqrMagnitude > 0.01f)
        {
            if (ProbeChargeHits(transform.position, ahead.normalized,
                coverLookAhead + chargeSpeed * Time.deltaTime, false, false)) return;
        }

        // A player in range has been reached; this is not a navigation stall.
        if (player != null && FlatChargeDistance(transform.position, player.position) <=
            collisionRadius + 0.7f)
        {
            StopCharge("Reached player", true);
            return;
        }

        if (FlatChargeDistance(transform.position, lastChargeMotionPosition) >=
            chargeMinimumProgress)
        {
            lastChargeMotionPosition = transform.position;
            lastChargeMotionAt = Time.time;
            // Reset failed attempts only after the monster has truly moved
            // away from the failed area, not after a tiny avoidance shuffle.
            if (FlatChargeDistance(transform.position, chargeLastRecoveryPosition) >
                Mathf.Max(3f, chargeSideRouteRadius))
                chargeRouteRecoveries = 0;
        }
        else if (Time.time - lastChargeMotionAt >= chargeStuckCheckSeconds)
        {
            // The Dash animation can show running even if the agent can't
            // move. Try new walkable side routes instead of doing that forever.
            lastChargeMotionAt = Time.time;
            NavLog("Stall: speed=" + agent.velocity.magnitude.ToString("F2") +
                ", desired=" + agent.desiredVelocity.magnitude.ToString("F2") +
                ", pending=" + agent.pathPending +
                ", path=" + agent.pathStatus +
                ", hasPath=" + agent.hasPath +
                ", onNavMesh=" + agent.isOnNavMesh);
            chargeRouteRecoveries++;
            chargeLastRecoveryPosition = transform.position;
            if (chargeRouteRecoveries > chargeMaximumRouteRecoveries)
            {
                StopCharge("No navigable route after recovery attempts", true);
                return;
            }

            Vector3 detour;
            NavMeshPath detourPath;
            if (TryFindSideRoute(player != null ? player.position :
                transform.position + transform.forward * 6f,
                chargeRouteRecoveries, out detour, out detourPath))
            {
                if (agent.SetPath(detourPath))
                {
                    chargeUsingSideRoute = true;
                    chargeSideRoutePoint = detour;
                    chargeSideRouteStartedAt = Time.time;
                    hasChargeCommittedDestination = false;
                    NavLog("CHARGE STALLED: trying side route " +
                        chargeRouteRecoveries + " -> " + detour);
                    return;
                }
            }

            chargeUsingSideRoute = false;
            hasChargeCommittedDestination = false;
            nextChargePathRefresh = 0f;
            RefreshChargeDestination(true);
            NavLog("CHARGE STALLED: retrying a fresh complete NavMesh path " +
                chargeRouteRecoveries);
        }
    }

    private void NavLog(string detail)
    {
        if (logChargeNavigationRecovery) Debug.Log(
            "[BossChargeAttack][Navigation] " + detail, this);
    }

    // A capsule matches the boss's body much better than a tiny center ray.
    // Before a dodge it follows NavMesh, then it travels straight. Contact
    // with cover over coverImpactStunThreshold stuns in EITHER phase,
    // but the cover MUST be destroyed before applying that stun.
    private static float FlatChargeDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private bool ProbeChargeHits(Vector3 origin, Vector3 direction,
        float distance, bool stopForSolidCollision, bool allowHeavyImpact = true)
    {
        if (distance <= 0f || direction.sqrMagnitude < 0.0001f)
            return false;

        int mask = obstacleLayers.value == 0
            ? ~(1 << 2)  // Unconfigured mask: hit everything except Ignore Raycast.
            : (obstacleLayers.value | coverColliderLayers) & ~(1 << 2);
        float bottomHeight = collisionRadius + 0.08f;
        float topHeight = Mathf.Max(bottomHeight + 0.15f, collisionHeight * 1.6f);
        Vector3 bottom = origin + Vector3.up * bottomHeight;
        Vector3 top = origin + Vector3.up * topHeight;

        // Overlap check covers cases where the boss begins inside a collider.
        Collider[] overlap = Physics.OverlapCapsule(bottom, top, collisionRadius,
            mask, QueryTriggerInteraction.Ignore);
        foreach (Collider hit in overlap)
        {
            // When inside a wall during straight travel, we still stop; when
            // on NavMesh we only care about destructible cover here.
            if (ProcessHit(hit, stopForSolidCollision, allowHeavyImpact)) return true;
        }

        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top,
            collisionRadius, direction, distance, mask,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
            if (ProcessHit(hit.collider, stopForSolidCollision, allowHeavyImpact)) return true;
        return false;
    }

    private bool CheckChargeCollisions(Vector3 displacement, bool stopForSolidCollision)
    {
        if (displacement.sqrMagnitude < 0.0000001f)
            return false;
        return ProbeChargeHits(transform.position, displacement.normalized,
            displacement.magnitude + collisionRadius, stopForSolidCollision);
    }

    private bool ProcessHit(Collider collider, bool stopForSolidCollision,
        bool allowHeavyImpact)
    {
        if (collider == null) return false;
        Transform target = collider.transform;
        if (target == transform || target.IsChildOf(transform)) return false;
        if (ignoredDebrisColliders.Contains(collider)) return false;
        if (ignoreCoverFragments && IsCoverFragment(collider))
        {
            ignoredDebrisColliders.Add(collider);
            return false;
        }

        foreach (Transform root in destroyedCoverRoots)
        {
            if (root != null && (target == root || target.IsChildOf(root)))
                return false;
        }

        Cover cover = collider.GetComponentInParent<Cover>();
        if (cover != null)
        {
            if (cover.IsDestroyed || hitCovers.Contains(cover)) return false;

            Transform coverRoot = cover.transform;
            string coverName = cover.name;
            // The long look-ahead is NOT physical contact. Only destroy
            // cover detected by a real body overlap / short movement sweep.
            if (!allowHeavyImpact) return false;

            float healthAtImpact = cover.CurrentHealth;
            bool stunsBoss = healthAtImpact > coverImpactStunThreshold;
            hitCovers.Add(cover);

            // UNCONDITIONAL one-hit destruction. Apply damage FIRST, then
            // decide whether the original health warrants a stun.
            cover.TakeDamage(Mathf.Max(coverDamage, healthAtImpact));
            if (cover.IsDestroyed)
            {
                destroyedCoverRoots.Add(coverRoot);
                Log("CHARGE DESTROYED COVER (" + healthAtImpact.ToString("F1") +
                    " HP): " + coverName);
            }
            else
            {
                Debug.LogWarning("[BossChargeAttack] Cover did not fracture after " +
                    "a lethal charge impact: " + coverName, this);
            }

            if (stunsBoss)
            {
                StopCharge("HEAVY COVER DESTROYED (" +
                    healthAtImpact.ToString("F1") + " HP): " + coverName, true);
                return true;
            }
            return false;
        }

        if (!stopForSolidCollision) return false; // Let NavMesh route around walls.

        if (player != null)
        {
            Transform playerRoot = player.root;
            if (target == player || target.IsChildOf(player) ||
                target == playerRoot || target.IsChildOf(playerRoot))
            {
                StopCharge("Collided with player", true);
                return true;
            }
        }

        StopCharge("Solid obstacle collision: " + collider.name, true);
        return true;
    }

    private bool IsCoverFragment(Collider collider)
    {
        if (collider == null ||
            string.IsNullOrEmpty(fragmentNamePrefix))
        {
            return false;
        }

        for (Transform t = collider.transform;
             t != null;
             t = t.parent)
        {
            if (t.name.StartsWith(
                fragmentNamePrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // returnToPatrol is used for heavy-cover impacts and other safety stops.
    // It prevents the existing default post-charge SEARCH transition.
    private void StopCharge(string reason, bool returnToPatrol = true)
    {
        if (!IsCharging) return;

        IsCharging = false;
        IsRecovering = true;
        stunnedEntered = false;
        recoveryEndTime = Time.time + recoveryDuration;

        if (ai != null) ai.enabled = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;

        stunnedHash = Animator.StringToHash(stunnedAnimation);
        PlayState(stunnedAnimation, stunnedCrossfade);

        if (boss != null)
        {
            // Old code sent EVERY stun straight to patrol. Preserve the
            // location at the instant of the dodge, then let BossEnemyAI
            // play Suspicious and investigate it after the stun recovers.
            Vector3 clue = hasDodgeLastKnownPosition
                ? dodgeLastKnownPosition
                : (player != null ? player.position : transform.position);
            boss.SchedulePostChargeSuspicion(clue, clue);
        }

        float traveled = Vector3.Distance(chargeStartPosition, transform.position);
        Log("CHARGE STOPPED | " + reason + " | Traveled=" +
            traveled.ToString("F2") + "m");
        Log("STUNNED STARTED | recovery=" + recoveryDuration.ToString("F2") + "s");
    }

    private void UpdateRecovery()
    {
        // If an Animator transition overrides Stunned,
        // restore it without extending the recovery timer.
        if (holdStunnedUntilRecoveryEnds &&
            Time.time < recoveryEndTime &&
            bossAnimator != null &&
            bossAnimator.enabled &&
            bossAnimator.HasState(0, stunnedHash))
        {
            AnimatorStateInfo current =
                bossAnimator.GetCurrentAnimatorStateInfo(0);

            AnimatorStateInfo next =
                bossAnimator.GetNextAnimatorStateInfo(0);

            bool isStunned =
                current.shortNameHash == stunnedHash ||
                (bossAnimator.IsInTransition(0) &&
                 next.shortNameHash == stunnedHash);

            if (isStunned)
            {
                stunnedEntered = true;
            }
            else
            {
                // Re-enter even if the state was interrupted
                // before the first transition completed.
                bossAnimator.CrossFadeInFixedTime(
                    stunnedHash,
                    0.05f,
                    0
                );

                if (stunnedEntered)
                    Log("STUNNED OVERRIDDEN - RESTORING STATE");
            }
        }

        if (Time.time >= recoveryEndTime)
            FinishRecovery();
    }

    private void FinishRecovery()
    {
        RestoreControllers();

        IsRecovering = false;

        Log("CHARGE RECOVERY COMPLETE - AI CONTROL RESTORED");
    }

    private void RestoreControllers()
    {
        if (!savedControllerState) return;

        bool ready = false;
        if (agent != null && agentWasEnabled)
        {
            // Straight-line charging may leave the baked NavMesh. Find a valid
            // recovery point BEFORE re-enabling the agent to avoid error spam.
            NavMeshHit navHit;
            bool found = NavMesh.SamplePosition(transform.position, out navHit,
                navMeshRecoverySampleRadius, NavMesh.AllAreas);
            if (!found) found = NavMesh.SamplePosition(transform.position, out navHit,
                Mathf.Max(6f, navMeshRecoverySampleRadius * 2f), NavMesh.AllAreas);
            if (!found) found = NavMesh.SamplePosition(transform.position, out navHit,
                Mathf.Max(12f, navMeshRecoverySampleRadius * 4f), NavMesh.AllAreas);
            // A straight post-dodge dash can end outside the NavMesh entirely.
            // Walk the traveled segment BACKWARD to find the closest valid
            // recovery point. Without this, the boss can stay permanently
            // disabled and never perform its post-stun suspicion/investigation.
            if (!found)
            {
                Vector3 end = transform.position;
                float total = Vector3.Distance(end, chargeStartPosition);
                for (float d = 1.5f; d <= total + 1.5f; d += 1.5f)
                {
                    Vector3 sample = Vector3.Lerp(end, chargeStartPosition,
                        Mathf.Clamp01(d / Mathf.Max(total, 0.01f)));
                    if (NavMesh.SamplePosition(sample, out navHit, 1.75f,
                            NavMesh.AllAreas))
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (found)
            {
                if (!agent.enabled) transform.position = navHit.position;
                agent.enabled = true;
                ready = agent.Warp(navHit.position) && agent.isOnNavMesh;
                agent.updatePosition = agentWasUpdatingPosition;
                agent.updateRotation = agentWasUpdatingRotation;
                agent.speed = agentOriginalSpeed;
                agent.acceleration = agentOriginalAcceleration;
                agent.stoppingDistance = agentOriginalStoppingDistance;
                agent.autoBraking = agentOriginalAutoBraking;
                if (ready)
                {
                    agent.ResetPath();
                    agent.isStopped = agentWasStopped;
                }
            }
            else
            {
                if (agent.enabled) agent.enabled = false;
                Debug.LogWarning("[BossChargeAttack] No nearby NavMesh after charge. " +
                    "Agent could not be restored at this position.", this);
            }
        }

        RestoreCoverNavMeshObstacles();

        if (savedAnimatorState && bossAnimator != null)
            bossAnimator.applyRootMotion = animatorRootMotionWasEnabled;
        savedAnimatorState = false;

        if (ai != null)
        {
            // Re-enable AI when navigation is valid, rather than leaving
            // the controller fighting a disabled or off-mesh NavMeshAgent.
            ai.enabled = aiWasEnabled && ready;
            if (!ready && aiWasEnabled)
                Debug.LogWarning("[BossChargeAttack] AI navigation could not recover. " +
                    "Make sure the charge ends near walkable NavMesh.", this);
        }
        savedControllerState = false;
    }

    private void PlayState(string state, float crossfade)
    {
        if (bossAnimator == null || !bossAnimator.enabled)
            return;

        int hash = Animator.StringToHash(state);

        if (!bossAnimator.HasState(0, hash))
        {
            Debug.LogError(
                "[BossChargeAttack] Animator state missing on layer 0: '" +
                state +
                "'. Check the child Animator and state name.",
                this
            );

            return;
        }

        bossAnimator.CrossFadeInFixedTime(
            hash,
            crossfade,
            0
        );
    }

    private void OnDisable()
    {
        RestoreControllers();

        IsPositioning = false;
        IsWindingUp = false;
        IsCharging = false;
        IsRecovering = false;
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log(
                "[BossChargeAttack] " + message,
                this
            );
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position + Vector3.up * collisionHeight,
            collisionRadius
        );
    }
}
