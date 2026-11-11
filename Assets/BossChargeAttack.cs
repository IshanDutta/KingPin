
using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using FS_Core;
using FS_StealthSystem;

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
    [SerializeField] private string playerKillIdleAnimation = "Idle";
    [SerializeField, Min(0f)] private float playerKillIdleCrossfade = 0.15f;
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
    [SerializeField, Min(0f)] private float chargeSpeed = 22f;
    [Header("Charge Speed Ramp")]
    [Tooltip("Speed at the start of Dash, before accelerating toward Charge Speed.")]
    [SerializeField, Min(0f)] private float chargeStartingSpeed = 6f;
    [Tooltip("How many meters/second of speed the boss gains each second while charging.")]
    [SerializeField, Min(0f)] private float chargeSpeedGainPerSecond = 5f;
    [Tooltip("Current dash speed. Ramps up while charging and carries through dodge/run-through.")]
    [SerializeField] private float currentChargeSpeed;
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

    [Header("Missing Path / Frozen Charge Recovery")]
    [Tooltip("If Unity drops a valid charge path, attempt to restore its destination this often instead of waiting for the full stall timeout.")]
    [SerializeField, Min(0.1f)] private float missingChargePathRetryInterval = 0.35f;
    [Tooltip("After a real path disappears for this long, follow the last VALID NavMesh corners manually instead of repeatedly picking left/right detours.")]
    [SerializeField, Min(0.15f)] private float missingChargePathManualDelay = 0.55f;
    [Tooltip("NavMesh-checked movement speed when Unity's agent refuses to advance along a previously calculated complete path.")]
    [SerializeField, Range(0.25f, 1.5f)] private float manualChargeSpeedMultiplier = 0.9f;
    [Tooltip("How closely the fallback must approach a route corner before continuing to the next one.")]
    [SerializeField, Range(0.2f, 1.25f)] private float manualChargeCornerArrival = 0.6f;

    [Header("Long-Distance Charge / Approach Stability")]
    [Tooltip("Past this distance, keep an existing complete NavMesh path longer instead of constantly replanning as the player moves.")]
    [SerializeField, Min(8f)] private float farNavigationDistance = 18f;
    [Tooltip("Minimum time between complete path replans while the target is far away.")]
    [SerializeField, Min(0.2f)] private float farPathRefreshInterval = 0.7f;
    [Tooltip("How far the player must move before a distant complete path is replaced.")]
    [SerializeField, Min(1f)] private float farRetargetDistance = 5f;
    [Tooltip("Limit distant steering rotation so a NavMesh corner cannot whip the model left and right.")]
    [SerializeField, Range(45f, 360f)] private float farNavigationTurnSpeed = 160f;
    [SerializeField, Min(0.25f)] private float farHeadingSmoothness = 4f;
    [Tooltip("Require a wall bypass to remain clear briefly before switching from approach to charge windup.")]
    [SerializeField, Min(0f)] private float clearChargeLaneConfirmSeconds = 0.25f;
    [SerializeField] private bool logChargeNavigationRecovery = true;
    [Tooltip("Proactive cover impact probe, in addition to the distance moved this frame.")]
    [SerializeField, Min(0.1f)] private float coverLookAhead = 1.25f;
    [Header("Charge Cover Impact Rules")]
    [Tooltip("EVERY Cover physically hit by the charge is destroyed in one impact. If its CURRENT health before destruction is ABOVE this value, the boss is also stunned.")]
    [SerializeField, Min(0.01f)] private float coverImpactStunThreshold = 75f;

    [Header("Lethal Player Impact")]
    [SerializeField] private bool killPlayerOnChargeContact = true;
    [Tooltip("Additional launch momentum distributed across the WHOLE ragdoll (never only the hips, which can destabilize the joints).")]
    [SerializeField, Min(0f)] private float playerImpactForce = 75f;
    [Tooltip("Horizontal launch speed shared by the entire ragdoll (m/s).")]
    [SerializeField, Min(0f)] private float playerLaunchForwardSpeed = 18f;
    [Tooltip("Upward launch velocity given to EVERY ragdoll bone on impact (m/s).")]
    [SerializeField, Min(0f)] private float playerLaunchUpwardSpeed = 8f;
    [Tooltip("Whole-body tumble rate (radians/s). Safely capped to prevent joint instability.")]
    [SerializeField, Min(0f)] private float playerLaunchTumble = 4.5f;
    [SerializeField, Min(0.1f)] private float playerImpactDetectionRadius = 0.85f;
    [Header("Post-Kill Run Through")]
    [Tooltip("Distance the boss keeps dashing STRAIGHT past the player after killing them, before entering Idle (meters).")]
    [SerializeField, Min(0f)] private float playerKillRunThroughDistance = 6f;
    [Tooltip("Follow-through speed as a multiplier of Charge Speed. 1 = full charge speed.")]
    [SerializeField, Min(0.1f)] private float playerKillRunThroughSpeedMultiplier = 1f;
    [Tooltip("Optional: assign BoneMapper on the player's child model. If empty, it is found automatically on death.")]
    [SerializeField] private BoneMapper playerBoneMapper;
    [Tooltip("Rigidbody, Collider and CharacterJoint components are created only when the boss kills the player. No ragdoll physics is needed while alive.")]
    [SerializeField] private bool buildRagdollOnDeath = true;
    [Tooltip("Disable other player colliders on death so they do not block the new ragdoll.")]
    [SerializeField] private bool disableNonRagdollCollidersOnDeath = true;
    [Tooltip("Ignore collisions between the new ragdoll and the boss, preventing explosive overlap on impact.")]
    [SerializeField] private bool ignoreBossCollidersOnDeath = true;

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
    private StealthAIController stealthAI;
    private NavMeshAgent agent;
    private PlayerDodgeRoll playerDodge;

    private bool aiWasEnabled;
    private bool stealthAIWasEnabled;
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
    private float chargeSideRouteDeadline;
    private Vector3 lastFailedSideRoutePoint;
    private bool hasFailedSideRoute;
    private Vector3 chargeLastRecoveryPosition;
    // Keep the complete path corners when NavMeshAgent.SetPath succeeds.
    // Runtime obstacle/carving updates can silently clear the agent's path;
    // the stored corners let us keep moving on the walkable NavMesh instead.
    private readonly List<Vector3> chargeCachedPathCorners = new List<Vector3>();
    private int chargeCachedCornerIndex;
    private bool chargeManualNavigationActive;
    private float chargeManualNavigationDeadline;
    private float chargeNoPathSince = -1f;
    private float nextChargeMissingPathRetry;
    private float lastChargeRouteAssignedAt = -100f;
    private int chargeManualBlockedCount;
    private Vector3 chargeCachedRouteDestination;

    private Vector3 preChargeCommittedDestination;
    private Vector3 preChargePlayerPositionAtLastPlan;
    private bool hasPreChargeCommittedDestination;
    private float lastPreChargeMotionAt;
    private Vector3 lastPreChargeMotionPosition;
    private int preChargeRecoveryAttempts;
    private bool preChargeUsingSideRoute;
    private Vector3 preChargeSideRoutePoint;
    private float preChargeSideRouteStartedAt;
    private float preChargeSideRouteDeadline;
    private float clearChargeLaneSince = -1f;
    private Vector3 smoothedChargeSteeringHeading;
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
    private bool playerKilledByCharge;
    private bool postKillRunThroughActive;
    private Vector3 postKillRunThroughDirection;
    private float postKillRunThroughRemaining;

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
        stealthAI = GetComponent<StealthAIController>();
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
        // After a lethal hit, keep playing Dash and physically travel THROUGH
        // the player's ragdoll. Only crossfade to Idle once this is finished.
        if (playerKilledByCharge)
        {
            if (postKillRunThroughActive) UpdatePostKillRunThrough();
            return;
        }

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
        clearChargeLaneSince = -1f;
        smoothedChargeSteeringHeading = Vector3.zero;
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
            // At long distances NavMesh/collider clearance can alternate at
            // a corner. Require a brief, uninterrupted clear lane before
            // changing state; otherwise Approach <-> Windup can oscillate.
            if (clearChargeLaneSince < 0f)
                clearChargeLaneSince = Time.time;
            if (Time.time - clearChargeLaneSince >= clearChargeLaneConfirmSeconds)
            {
                IsPositioning = false;
                agent.isStopped = true;
                if (agent.hasPath) agent.ResetPath();
                smoothedChargeSteeringHeading = Vector3.zero;
                Log("CHARGE LANE CLEAR - STARTING DETECTED WINDUP");
                BeginWindup();
                return;
            }
        }
        else
        {
            clearChargeLaneSince = -1f;
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
            // The route may go around a large wall. A fixed 2.4-second
            // expiry abandoned that detour halfway through and made the boss
            // repeatedly veer left/right instead of finishing the path.
            bool expired = Time.time >= preChargeSideRouteDeadline;
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
                    float detourLength = CalculatePathLength(sidePath);
                    float detourSeconds = float.IsInfinity(detourLength)
                        ? chargeSideRouteHoldSeconds
                        : detourLength / Mathf.Max(0.5f, preChargeApproachSpeed) * 2f + 1f;
                    preChargeSideRouteDeadline = Time.time + Mathf.Clamp(
                        Mathf.Max(chargeSideRouteHoldSeconds, detourSeconds),
                        chargeSideRouteHoldSeconds,
                        Mathf.Max(chargeSideRouteHoldSeconds, preChargeApproachTimeout));
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
        nextPreChargePathRefresh = Time.time + EffectivePathRefreshInterval(preChargePathRefreshInterval);

        // Keep a complete route committed while the player is far away.
        // Replanning every 0.15s was making the agent alternate between
        // different NavMesh corners, producing the long-range zigzag.
        bool validExistingPath = agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete);
        if (agent.pathPending && hasPreChargeCommittedDestination) return;
        float retargetThreshold = EffectiveRetargetDistance();
        if (hasPreChargeCommittedDestination && validExistingPath &&
            FlatChargeDistance(preChargePlayerPositionAtLastPlan, player.position) <
                retargetThreshold) return;
        Vector3 desired;
        NavMeshPath path;
        if (!TryFindReachablePlayerPoint(out desired, out path)) return;
        // The path may be pending; don't replace it with an identical goal.
        validExistingPath = agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete);
        if (hasPreChargeCommittedDestination && validExistingPath &&
            FlatChargeDistance(preChargeCommittedDestination, desired) <
                retargetThreshold) return;
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
        currentChargeSpeed = Mathf.Clamp(chargeStartingSpeed, 0f, chargeSpeed);
        nextChargePathRefresh = 0f;
        hasChargeCommittedDestination = false;
        chargeRouteRecoveries = 0;
        chargeUsingSideRoute = false;
        hasFailedSideRoute = false;
        chargeLastRecoveryPosition = transform.position;
        chargeCachedPathCorners.Clear();
        chargeCachedCornerIndex = 0;
        chargeManualNavigationActive = false;
        chargeNoPathSince = -1f;
        nextChargeMissingPathRetry = 0f;
        lastChargeRouteAssignedAt = -100f;
        chargeManualBlockedCount = 0;
        smoothedChargeSteeringHeading = Vector3.zero;
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
        agent.speed = currentChargeSpeed;
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
        stealthAIWasEnabled = stealthAI != null && stealthAI.enabled;
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

        // Fantacode's StealthAIController can also write movement/path state.
        // Disable it while this component owns the charge NavMeshAgent;
        // otherwise it can clear a successfully assigned route every frame.
        if (stealthAI != null) stealthAI.enabled = false;
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

        // Accelerate smoothly from starting speed to the configured top speed.
        // This applies to NavMesh pursuit, manual path recovery, and post-dodge motion.
        currentChargeSpeed = Mathf.MoveTowards(currentChargeSpeed,
            Mathf.Max(0f, chargeSpeed),
            Mathf.Max(0f, chargeSpeedGainPerSecond) * Time.deltaTime);
        CheckPlayerDodge();
        if (!IsCharging) return;
        if (CheckLethalPlayerContact()) return;

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
            Vector3 displacement = chargeDirection * currentChargeSpeed * Time.deltaTime;
            if (displacement.sqrMagnitude < 0.000001f) return;

            if (CheckChargeCollisions(displacement, true)) return;

            transform.position += displacement;
            if (CheckLethalPlayerContact()) return;
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
        chargeManualNavigationActive = false;
        chargeNoPathSince = -1f;
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

    // Far away, prefer a committed route and rotate toward its travel direction
    // smoothly. Close to the player we recover the original fast steering and
    // frequent retargeting so impacts/dodges remain responsive.
    private float FarNavigationWeight()
    {
        if (player == null) return 0f;
        float distance = FlatChargeDistance(transform.position, player.position);
        return Mathf.InverseLerp(6f, Mathf.Max(8f, farNavigationDistance), distance);
    }

    private float EffectivePathRefreshInterval(float nearInterval)
    {
        return Mathf.Lerp(nearInterval,
            Mathf.Max(nearInterval, farPathRefreshInterval), FarNavigationWeight());
    }

    private float EffectiveRetargetDistance()
    {
        float nearDistance = Mathf.Max(0.25f, chargeRetargetDistance);
        return Mathf.Lerp(nearDistance,
            Mathf.Max(nearDistance, farRetargetDistance), FarNavigationWeight());
    }

    private void TrackPlayer()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh ||
            agent.isStopped || agent.pathPending || !agent.hasPath) return;

        // SteeringTarget is the next NavMesh corner; near a corner it can snap
        // behind the character, so use the agent's actual velocity there.
        Vector3 cornerDirection = agent.steeringTarget - transform.position;
        cornerDirection.y = 0f;
        Vector3 travelDirection = agent.velocity;
        travelDirection.y = 0f;
        Vector3 desiredDirection = cornerDirection;
        float closeCornerDistance = Mathf.Max(0.9f, agent.radius + 0.65f);
        if (cornerDirection.sqrMagnitude < closeCornerDistance * closeCornerDistance ||
            (travelDirection.sqrMagnitude > 0.16f &&
             Vector3.Dot(cornerDirection.normalized, travelDirection.normalized) < -0.35f))
            desiredDirection = travelDirection;
        if (desiredDirection.sqrMagnitude < 0.01f)
            desiredDirection = agent.desiredVelocity;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude < 0.01f) return;
        desiredDirection.Normalize();

        float far = FarNavigationWeight();
        if (smoothedChargeSteeringHeading.sqrMagnitude < 0.2f)
            smoothedChargeSteeringHeading = desiredDirection;
        else
        {
            float smoothing = Mathf.Lerp(14f, farHeadingSmoothness, far);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.25f, smoothing) * Time.deltaTime);
            smoothedChargeSteeringHeading = Vector3.Slerp(
                smoothedChargeSteeringHeading, desiredDirection, blend);
        }
        // Only yaw should change. Avoid a vertical tilt if the steering
        // direction ever reverses by 180 degrees at a NavMesh corner.
        smoothedChargeSteeringHeading.y = 0f;
        if (smoothedChargeSteeringHeading.sqrMagnitude < 0.01f)
            smoothedChargeSteeringHeading = desiredDirection;
        else
            smoothedChargeSteeringHeading.Normalize();
        float turnSpeed = Mathf.Lerp(chargeTurnSpeed,
            Mathf.Min(chargeTurnSpeed, farNavigationTurnSpeed), far);
        Quaternion heading = Quaternion.LookRotation(smoothedChargeSteeringHeading);
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            heading, Mathf.Max(1f, turnSpeed) * Time.deltaTime);
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
        nextChargePathRefresh = Time.time + EffectivePathRefreshInterval(chargePathRefreshInterval);

        bool pathUsable = agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete);
        if (!force && hasChargeCommittedDestination && agent.pathPending) return;
        float retargetThreshold = EffectiveRetargetDistance();
        if (!force && hasChargeCommittedDestination && pathUsable &&
            FlatChargeDistance(chargePlayerPositionAtLastPlan, player.position) <
                retargetThreshold)
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
                retargetThreshold)
            return;

        // SetPath uses the already validated route. Don't replace movement
        // with a partial route or clear it because a new path could not be found.
        if (agent.SetPath(route))
        {
            chargeCommittedDestination = desired;
            chargePlayerPositionAtLastPlan = player.position;
            hasChargeCommittedDestination = true;
            lastChargeRouteAssignedAt = Time.time;
            CacheChargeNavMeshRoute(route, desired);
            if (force) NavLog("NavMesh route refreshed -> " + desired);
        }
        else if (force)
        {
            // SetPath sometimes rejects a previously COMPLETE calculated route
            // while a NavMeshObstacle is updating. A cached complete route is
            // still useful for recovery; do not replace it with a random detour.
            CacheChargeNavMeshRoute(route, desired);
            NavLog("SetPath rejected calculated complete path; saved corners for fallback");
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
                    candidate.status != NavMeshPathStatus.PathComplete ||
                    candidate.corners == null || candidate.corners.Length == 0) continue;
                // A one-corner "complete" path is only meaningful if the boss
                // is already at that destination. Otherwise Unity would accept
                // it, immediately clear hasPath, and leave the charge frozen.
                if (candidate.corners.Length < 2 &&
                    FlatChargeDistance(transform.position, hit.position) > 0.8f)
                    continue;
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

        agent.speed = currentChargeSpeed;
        agent.updatePosition = true;
        if (agent.isStopped) agent.isStopped = false;

        if (chargeUsingSideRoute)
        {
            bool reached = !agent.pathPending &&
                FlatChargeDistance(transform.position, chargeSideRoutePoint) <= 0.85f;
            // Let a valid side route finish; short fixed timers could
            // cancel the path before a distant corner was reached.
            bool expired = Time.time >= chargeSideRouteDeadline;
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
        if (!chargeUsingSideRoute && !chargeManualNavigationActive)
            RefreshChargeDestination();
        bool manuallyMovedThisFrame = MaintainChargeNavigation();
        if (!manuallyMovedThisFrame && !chargeManualNavigationActive)
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
        if (CheckLethalPlayerContact()) return;

        Vector3 ahead = agent.desiredVelocity;
        ahead.y = 0f;
        if (ahead.sqrMagnitude < 0.01f)
            ahead = agent.steeringTarget - transform.position;
        ahead.y = 0f;
        if (ahead.sqrMagnitude > 0.01f)
        {
            if (ProbeChargeHits(transform.position, ahead.normalized,
                coverLookAhead + currentChargeSpeed * Time.deltaTime, false, false)) return;
        }

        // A player in range has been reached; this is not a navigation stall.
        if (player != null && FlatChargeDistance(transform.position, player.position) <=
            collisionRadius + 0.7f)
        {
            KillPlayerOnImpact();
            return;
        }

        if (manuallyMovedThisFrame ||
            FlatChargeDistance(transform.position, lastChargeMotionPosition) >=
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
        else if (Time.time - lastChargeMotionAt >= chargeStuckCheckSeconds &&
                 (!agent.pathPending ||
                  Time.time - lastChargeMotionAt >=
                      Mathf.Max(4f, chargeStuckCheckSeconds * 3f)))
        {
            // A stationary agent WITH a valid path may be trapped by avoidance
            // or a runtime carving change. Follow that same route before
            // choosing a completely different side every 1.4 seconds.
            if (!chargeManualNavigationActive && TryStartManualChargeNavigation(true))
            {
                lastChargeMotionAt = Time.time;
                NavLog("Stall with a calculated path - manually traversing its NavMesh corners");
                return;
            }
            if (chargeManualNavigationActive)
                return;
            // A briefly pending path is normal. If it remains pending for
            // several seconds without moving, it needs the same recovery as
            // a stalled complete route rather than waiting forever.
            if (agent.pathPending) agent.ResetPath();
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
                    float detourLength = CalculatePathLength(detourPath);
                    float detourSeconds = float.IsInfinity(detourLength)
                        ? chargeSideRouteHoldSeconds
                        : detourLength / Mathf.Max(0.5f, chargeSpeed) * 2f + 1f;
                    chargeSideRouteDeadline = Time.time + Mathf.Clamp(
                        Mathf.Max(chargeSideRouteHoldSeconds, detourSeconds),
                        chargeSideRouteHoldSeconds,
                        Mathf.Max(chargeSideRouteHoldSeconds, 12f));
                    hasChargeCommittedDestination = false;
                    CacheChargeNavMeshRoute(detourPath, detour);
                    lastChargeRouteAssignedAt = Time.time;
                    chargeNoPathSince = -1f;
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

    // Store ONLY complete, reachable path corners. If Unity silently clears
    // agent.hasPath (often after mass runtime cover-obstacle changes), these
    // are the last reliable waypoints toward the actual player or a detour.
    private void CacheChargeNavMeshRoute(NavMeshPath route, Vector3 destination)
    {
        if (route == null || route.status != NavMeshPathStatus.PathComplete ||
            route.corners == null || route.corners.Length < 2) return;
        chargeCachedPathCorners.Clear();
        chargeCachedPathCorners.AddRange(route.corners);
        chargeCachedRouteDestination = destination;
        chargeCachedCornerIndex = 1;
        chargeManualNavigationActive = false;
        chargeManualBlockedCount = 0;
    }

    // Automatic path following is preferred. This recovers from the exact
    // console state "PathComplete, hasPath=False, desiredVelocity=0" without
    // endlessly alternating side routes or leaving the boss running in place.
    private bool MaintainChargeNavigation()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || HasLostTracking)
            return false;

        bool realPath = agent.hasPath &&
            agent.pathStatus == NavMeshPathStatus.PathComplete;
        if (realPath && agent.desiredVelocity.sqrMagnitude > 0.04f)
        {
            chargeNoPathSince = -1f;
            if (chargeManualNavigationActive)
            {
                chargeManualNavigationActive = false;
                NavLog("Agent resumed NavMesh path movement - leaving manual fallback");
            }
            return false;
        }

        if (!realPath && !agent.pathPending)
        {
            if (chargeNoPathSince < 0f) chargeNoPathSince = Time.time;
            // SetPath can report success one frame before the native agent
            // exposes hasPath. Don't throw away that fresh route immediately.
            if (Time.time - lastChargeRouteAssignedAt < 0.2f)
                return false;
            if (Time.time >= nextChargeMissingPathRetry)
            {
                nextChargeMissingPathRetry = Time.time +
                    Mathf.Max(0.1f, missingChargePathRetryInterval);

                // Prefer a direct COMPLETE route to the player. Side routes
                // are a last resort and must not replace a viable main route.
                if (!chargeUsingSideRoute && !chargeManualNavigationActive)
                {
                    hasChargeCommittedDestination = false;
                    nextChargePathRefresh = 0f;
                    RefreshChargeDestination(true);
                    if (!agent.hasPath && !agent.pathPending &&
                        hasChargeCommittedDestination)
                    {
                        if (agent.SetDestination(chargeCommittedDestination))
                            lastChargeRouteAssignedAt = Time.time;
                    }
                }
                else if (chargeUsingSideRoute && !chargeManualNavigationActive)
                    agent.SetDestination(chargeSideRoutePoint);
            }
        }
        else if (agent.pathPending)
        {
            // A fresh asynchronous path must have time to complete.
            if (chargeNoPathSince < 0f) chargeNoPathSince = Time.time;
            if (Time.time - chargeNoPathSince < Mathf.Max(1f,
                missingChargePathManualDelay * 2f)) return false;
        }

        if (!chargeManualNavigationActive && chargeNoPathSince >= 0f &&
            Time.time - chargeNoPathSince >= missingChargePathManualDelay)
            TryStartManualChargeNavigation(false);

        if (!chargeManualNavigationActive) return false;
        if (Time.time >= chargeManualNavigationDeadline)
        {
            StopManualChargeNavigation("stored NavMesh route timed out");
            return false;
        }
        return MoveAlongCachedChargeNavMeshRoute();
    }

    private bool TryStartManualChargeNavigation(bool forceStallRecovery)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh ||
            chargeCachedPathCorners.Count < 2 || HasLostTracking)
            return false;
        if (!forceStallRecovery && chargeManualBlockedCount >= 2)
            return false;

        // If an agent is stuck with a valid complete path, capture its newest
        // NavMesh corners first; avoid following an obsolete cached route.
        if (forceStallRecovery && agent.hasPath &&
            agent.pathStatus == NavMeshPathStatus.PathComplete)
        {
            NavMeshPath current = agent.path;
            if (current != null && current.corners != null &&
                current.corners.Length >= 2)
                CacheChargeNavMeshRoute(current, agent.destination);
        }

        chargeCachedCornerIndex = Mathf.Clamp(chargeCachedCornerIndex,
            1, chargeCachedPathCorners.Count - 1);
        // Ignore passed corners (e.g. the cached path starts behind the boss).
        while (chargeCachedCornerIndex < chargeCachedPathCorners.Count - 1 &&
               FlatChargeDistance(agent.nextPosition,
                   chargeCachedPathCorners[chargeCachedCornerIndex]) <=
               manualChargeCornerArrival)
            chargeCachedCornerIndex++;

        chargeManualNavigationActive = true;
        float remainingRouteDistance = 0f;
        Vector3 last = agent.nextPosition;
        for (int i = chargeCachedCornerIndex; i < chargeCachedPathCorners.Count; i++)
        {
            remainingRouteDistance += FlatChargeDistance(last, chargeCachedPathCorners[i]);
            last = chargeCachedPathCorners[i];
        }
        float fallbackSpeed = Mathf.Max(0.5f,
            currentChargeSpeed * manualChargeSpeedMultiplier);
        chargeManualNavigationDeadline = Time.time + Mathf.Clamp(
            remainingRouteDistance / fallbackSpeed * 3f + 3f, 4f, 24f);
        NavLog("Agent has no usable movement; following " +
            (chargeCachedPathCorners.Count - chargeCachedCornerIndex) +
            " saved walkable corners toward " + chargeCachedRouteDestination);
        return true;
    }

    private bool MoveAlongCachedChargeNavMeshRoute()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh ||
            chargeCachedPathCorners.Count < 2) return false;

        // Never blindly move through non-cover walls: every manual step is
        // checked against NavMesh polygons. Dodge still switches to the
        // existing unconstrained straight-line charge immediately.
        for (int i = 0; i < 3; i++)
        {
            if (chargeCachedCornerIndex >= chargeCachedPathCorners.Count)
            {
                StopManualChargeNavigation("reached stored NavMesh destination");
                if (chargeUsingSideRoute) chargeUsingSideRoute = false;
                hasChargeCommittedDestination = false;
                nextChargePathRefresh = 0f;
                return false;
            }
            if (FlatChargeDistance(agent.nextPosition,
                chargeCachedPathCorners[chargeCachedCornerIndex]) >
                manualChargeCornerArrival) break;
            chargeCachedCornerIndex++;
        }
        if (chargeCachedCornerIndex >= chargeCachedPathCorners.Count)
        {
            StopManualChargeNavigation("reached stored NavMesh destination");
            if (chargeUsingSideRoute) chargeUsingSideRoute = false;
            hasChargeCommittedDestination = false;
            nextChargePathRefresh = 0f;
            return false;
        }

        Vector3 toward = chargeCachedPathCorners[chargeCachedCornerIndex] -
            agent.nextPosition;
        toward.y = 0f;
        if (toward.sqrMagnitude < 0.0001f) return false;
        float allowedStep = Mathf.Min(toward.magnitude,
            Mathf.Max(0.5f, currentChargeSpeed * manualChargeSpeedMultiplier) *
            Time.deltaTime);
        if (allowedStep < 0.0001f) return false;
        Vector3 step = toward.normalized * allowedStep;
        NavMeshHit blocked;
        if (NavMesh.Raycast(agent.nextPosition, agent.nextPosition + step,
            out blocked, agent.areaMask))
        {
            // The baked mesh or dynamic carving changed beneath a cached
            // segment; don't push into solid walls or keep spinning in place.
            chargeManualBlockedCount++;
            StopManualChargeNavigation("cached segment blocked by current NavMesh");
            hasChargeCommittedDestination = false;
            nextChargePathRefresh = 0f;
            nextChargeMissingPathRetry = Time.time;
            return false;
        }

        agent.isStopped = false;
        Vector3 oldPosition = agent.nextPosition;
        agent.Move(step);
        if (FlatChargeDistance(oldPosition, agent.nextPosition) < 0.005f)
        {
            // NavMeshAgent.Move itself was suppressed (possibly by an
            // avoidance conflict). Don't claim movement occurred.
            chargeManualBlockedCount++;
            StopManualChargeNavigation("agent.Move refused NavMesh-safe movement");
            return false;
        }
        // Rotation is cosmetic; do not change the saved path or turn away
        // toward the player until a new real NavMesh path is available.
        float far = FarNavigationWeight();
        float turn = Mathf.Lerp(chargeTurnSpeed,
            Mathf.Min(chargeTurnSpeed, farNavigationTurnSpeed), far);
        Quaternion heading = Quaternion.LookRotation(step.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            heading, Mathf.Max(1f, turn) * Time.deltaTime);
        chargeDirection = transform.forward;
        return true;
    }

    private void StopManualChargeNavigation(string reason)
    {
        if (!chargeManualNavigationActive) return;
        chargeManualNavigationActive = false;
        chargeNoPathSince = -1f;
        NavLog("Manual NavMesh recovery: " + reason);
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

        // Player impact must work during BOTH NavMesh and straight charges.
        // A long look-ahead probe is NOT physical contact, so it cannot kill.
        if (player != null)
        {
            Transform playerRoot = player.root;
            if (target == player || target.IsChildOf(player) ||
                target == playerRoot || target.IsChildOf(playerRoot))
            {
                if (!allowHeavyImpact) return false;
                KillPlayerOnImpact();
                return true;
            }
        }

        if (!stopForSolidCollision) return false; // Let NavMesh route around walls.

        StopCharge("Solid obstacle collision: " + collider.name, true);
        return true;
    }

    private bool CheckLethalPlayerContact()
    {
        if (!IsCharging || !killPlayerOnChargeContact || player == null) return false;
        var targetController = player.GetComponentInParent<CharacterController>();
        Vector3 targetPosition = targetController != null
            ? targetController.bounds.center : player.position + Vector3.up;
        Vector3 bossPosition = transform.position + Vector3.up;
        Vector3 delta = targetPosition - bossPosition;
        delta.y *= 0.4f;
        float radius = playerImpactDetectionRadius +
            (targetController != null ? targetController.radius : 0.35f);
        if (delta.sqrMagnitude > radius * radius) return false;
        KillPlayerOnImpact();
        return true;
    }

    private void KillPlayerOnImpact()
    {
        if (!IsCharging || player == null) return;
        if (!killPlayerOnChargeContact)
        {
            StopCharge("Reached player", true);
            return;
        }

        var damageable = player.GetComponentInParent<Damagable>();
        if (damageable == null) damageable = player.GetComponentInChildren<Damagable>(true);
        if (damageable == null) damageable = player.root.GetComponentInChildren<Damagable>(true);
        if (damageable == null)
        {
            Debug.LogWarning("[BossChargeAttack] Player has no FS_Core.Damagable; cannot apply lethal damage.", this);
            StopCharge("Player contact without Damagable", true);
            return;
        }

        // Prepare the physics skeleton BEFORE Fantacode fires its death events.
        // Release and launch on the next fixed step after the joints are built.
        Vector3 impulseDirection = chargeDirection.sqrMagnitude > 0.01f
            ? chargeDirection.normalized : transform.forward;

        BoneMapper mapper = ResolvePlayerBoneMapper(damageable);
        if (buildRagdollOnDeath)
        {
            if (mapper != null)
            {
                if (!BuildAndActivateDeathRagdoll(mapper, damageable, impulseDirection))
                    Debug.LogWarning("[BossChargeAttack] BoneMapper was found, but the death ragdoll could not be built. Check Hips/Head and mapped limb bones.", mapper);
            }
            else
            {
                Debug.LogWarning("[BossChargeAttack] No BoneMapper found on the player or its child model. Add BoneMapper to the child model (with its Animator) to build ragdoll physics on death.", this);
            }
        }

        if (!damageable.IsDead)
        {
            // We launch the physics ragdoll ourselves. TakeImpactDamage would ALSO
            // broadcast OnImpactDamage, potentially triggering a second knockback/
            // ragdoll reaction that fights these joints and causes violent jitter.
            damageable.TakeDamage(Mathf.Max(1f, damageable.CurrentHealth + 1f));
        }

        if (mapper != null && !mapper.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[BossChargeAttack] A Damagable OnDead event disabled the player's model. Keep the model active if the ragdoll corpse should remain visible.", mapper);
        }

        BeginPostKillRunThrough();
    }

    private void BeginPostKillRunThrough()
    {
        if (playerKilledByCharge) return;
        playerKilledByCharge = true;
        postKillRunThroughActive = true;
        IsPositioning = false;
        IsWindingUp = false;
        IsRecovering = false;
        HasLostTracking = false;
        // Keep IsCharging TRUE until the follow-through finishes, so Dash
        // stays in control and the normal recovery/stun path cannot run.

        // Commit to the direction the boss was already traveling. Never
        // retarget the dead player's hips or steer back toward their ragdoll.
        postKillRunThroughDirection = transform.forward;
        postKillRunThroughDirection.y = 0f;
        if (postKillRunThroughDirection.sqrMagnitude < 0.001f)
        {
            postKillRunThroughDirection = chargeDirection;
            postKillRunThroughDirection.y = 0f;
        }
        if (postKillRunThroughDirection.sqrMagnitude < 0.001f)
            postKillRunThroughDirection = Vector3.forward;
        postKillRunThroughDirection.Normalize();
        postKillRunThroughRemaining = Mathf.Max(0f, playerKillRunThroughDistance);

        // AI and NavMesh must not slow the boss down as the player's physics
        // body falls in front of it. Continue straight by transform motion.
        if (ai != null) ai.enabled = false;
        if (boss != null) boss.enabled = false;
        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            agent.enabled = false;
        }
        if (bossAnimator != null)
        {
            bossAnimator.applyRootMotion = false;
            // Do not crossfade to Idle on impact: let Dash continue.
        }

        Log("PLAYER KILLED - DASHING THROUGH BODY | remaining=" +
            postKillRunThroughRemaining.ToString("F1") + "m");
        if (postKillRunThroughRemaining <= 0.001f)
            StopAfterPlayerKill();
    }

    private void UpdatePostKillRunThrough()
    {
        if (!postKillRunThroughActive) return;

        // No player hit tests, dodge handling, or NavMesh steering here.
        // The boss passes through the newly spawned ragdoll without stopping.
        float speed = Mathf.Max(0f, currentChargeSpeed * playerKillRunThroughSpeedMultiplier);
        float distance = Mathf.Min(postKillRunThroughRemaining,
            speed * Time.deltaTime);
        if (distance > 0f)
        {
            transform.position += postKillRunThroughDirection * distance;
            transform.rotation = Quaternion.LookRotation(postKillRunThroughDirection);
            postKillRunThroughRemaining -= distance;
        }
        if (postKillRunThroughRemaining <= 0.001f || speed <= 0.001f)
            StopAfterPlayerKill();
    }

    private void StopAfterPlayerKill()
    {
        if (!playerKilledByCharge) return;
        postKillRunThroughActive = false;
        postKillRunThroughRemaining = 0f;
        IsCharging = false;
        IsPositioning = false;
        IsWindingUp = false;
        IsRecovering = false;
        HasLostTracking = false;

        // Never enter Stunned or post-charge suspicion on a successful kill.
        // Prevent both AI systems from overriding the Idle crossfade.
        if (ai != null) ai.enabled = false;
        if (boss != null) boss.enabled = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }

        RestoreCoverNavMeshObstacles();
        if (bossAnimator != null)
        {
            bossAnimator.applyRootMotion = false;
            PlayState(playerKillIdleAnimation, playerKillIdleCrossfade);
        }
        // Do not restore controllers: the boss remains idle until reset/reloaded.
        savedControllerState = false;
        savedAnimatorState = false;
        Log("PLAYER KILLED - RUN THROUGH COMPLETE - BOSS IDLE");
    }

    // BoneMapper belongs on the player's child model in many Fantacode prefabs.
    // No Rigidbody/Collider/Joint is required on the model before death.
    private BoneMapper ResolvePlayerBoneMapper(Damagable damageable)
    {
        if (playerBoneMapper != null && playerBoneMapper.gameObject.activeInHierarchy)
            return playerBoneMapper;

        BoneMapper mapper = null;
        if (damageable != null)
        {
            mapper = damageable.GetComponent<BoneMapper>();
            if (mapper == null)
                mapper = damageable.GetComponentInChildren<BoneMapper>(true);
        }
        if (mapper == null && player != null)
        {
            mapper = player.GetComponent<BoneMapper>();
            if (mapper == null) mapper = player.GetComponentInChildren<BoneMapper>(true);
            if (mapper == null) mapper = player.root.GetComponentInChildren<BoneMapper>(true);
        }
        if (mapper != null) playerBoneMapper = mapper;
        return mapper;
    }

    private struct DeathRagdollBone
    {
        public Transform bone;
        public Transform end;
        public float mass;
        public float radius;
        public bool sphere;

        public DeathRagdollBone(Transform bone, Transform end, float mass,
            float radius, bool sphere = false)
        {
            this.bone = bone;
            this.end = end;
            this.mass = mass;
            this.radius = radius;
            this.sphere = sphere;
        }
    }

    // Build a physical skeleton on impact. Components are NEVER added in Awake,
    // Start or Update, so normal locomotion is unaffected while alive.
    // The new joints are fully assembled before any rigidbody becomes dynamic.
    private bool BuildAndActivateDeathRagdoll(BoneMapper mapper,
        Damagable damageable, Vector3 direction)
    {
        Transform hips = mapper.GetBone(BoneType.Hips);
        Transform head = mapper.GetBone(BoneType.Head);
        if (hips == null || head == null)
        {
            Debug.LogWarning("[BossChargeAttack] BoneMapper must resolve Hips and Head for death ragdoll.", mapper);
            return false;
        }

        Animator modelAnimator = mapper.GetComponent<Animator>();
        bool humanoid = modelAnimator != null && modelAnimator.isHuman;
        Transform torso = humanoid
            ? FirstHumanoidBone(modelAnimator, HumanBodyBones.Chest,
                HumanBodyBones.UpperChest, HumanBodyBones.Spine)
            : InferTorsoBone(hips, head);
        if (torso == null || torso == hips || torso == head)
            torso = InferTorsoBone(hips, head);

        Transform leftElbow = mapper.GetBone(BoneType.LeftElbow);
        Transform rightElbow = mapper.GetBone(BoneType.RightElbow);
        Transform leftKnee = mapper.GetBone(BoneType.LeftKnee);
        Transform rightKnee = mapper.GetBone(BoneType.RightKnee);
        Transform leftHand = mapper.GetBone(BoneType.LeftHand);
        Transform rightHand = mapper.GetBone(BoneType.RightHand);
        Transform leftFoot = mapper.GetBone(BoneType.LeftFoot);
        Transform rightFoot = mapper.GetBone(BoneType.RightFoot);

        Transform leftUpperArm = humanoid
            ? modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm)
            : (leftElbow != null ? leftElbow.parent : null);
        Transform rightUpperArm = humanoid
            ? modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm)
            : (rightElbow != null ? rightElbow.parent : null);
        Transform leftUpperLeg = humanoid
            ? modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg)
            : (leftKnee != null ? leftKnee.parent : null);
        Transform rightUpperLeg = humanoid
            ? modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg)
            : (rightKnee != null ? rightKnee.parent : null);

        float size = Mathf.Clamp(Vector3.Distance(hips.position, head.position) / 0.85f,
            0.55f, 2.0f);
        var specs = new List<DeathRagdollBone>(16);
        AddDeathBone(specs, hips, torso, 10f, 0.16f * size);
        AddDeathBone(specs, torso, head, 13f, 0.18f * size);
        AddDeathBone(specs, head, null, 4f, 0.115f * size, true);
        AddDeathBone(specs, leftUpperArm, leftElbow, 2.5f, 0.07f * size);
        AddDeathBone(specs, leftElbow, leftHand, 1.5f, 0.055f * size);
        AddDeathBone(specs, leftHand, null, 0.5f, 0.055f * size, true);
        AddDeathBone(specs, rightUpperArm, rightElbow, 2.5f, 0.07f * size);
        AddDeathBone(specs, rightElbow, rightHand, 1.5f, 0.055f * size);
        AddDeathBone(specs, rightHand, null, 0.5f, 0.055f * size, true);
        AddDeathBone(specs, leftUpperLeg, leftKnee, 6f, 0.11f * size);
        AddDeathBone(specs, leftKnee, leftFoot, 3.5f, 0.085f * size);
        AddDeathBone(specs, leftFoot, null, 1f, 0.075f * size, true);
        AddDeathBone(specs, rightUpperLeg, rightKnee, 6f, 0.11f * size);
        AddDeathBone(specs, rightKnee, rightFoot, 3.5f, 0.085f * size);
        AddDeathBone(specs, rightFoot, null, 1f, 0.075f * size, true);

        var valid = new List<DeathRagdollBone>(specs.Count);
        var seen = new HashSet<Transform>();
        foreach (DeathRagdollBone spec in specs)
        {
            if (spec.bone == null || !seen.Add(spec.bone)) continue;
            if (spec.bone != hips && !spec.bone.IsChildOf(hips)) continue;
            valid.Add(spec);
        }
        if (valid.Count < 3)
        {
            Debug.LogWarning("[BossChargeAttack] BoneMapper resolved too few bones under Hips. Check the Avatar/bone mappings.", mapper);
            return false;
        }

        // The ActiveRagdollSystem is for prebuilt rigs; do not let its LateUpdate
        // overwrite this on-demand physics skeleton.
        Transform characterRoot = damageable != null ? damageable.transform : mapper.transform.root;
        CharacterController parentController = damageable != null
            ? damageable.GetComponentInParent<CharacterController>() : null;
        if (parentController != null) characterRoot = parentController.transform;

        foreach (ActiveRagdollSystem active in
            characterRoot.GetComponentsInChildren<ActiveRagdollSystem>(true))
            active.enabled = false;
        foreach (CharacterController controller in
            characterRoot.GetComponentsInChildren<CharacterController>(true))
            controller.enabled = false;
        foreach (PlayerController controller in
            characterRoot.GetComponentsInChildren<PlayerController>(true))
            controller.enabled = false;
        foreach (FS_LocomotionSystem.LocomotionController controller in
            characterRoot.GetComponentsInChildren<FS_LocomotionSystem.LocomotionController>(true))
            controller.enabled = false;
        foreach (PlayerDodgeRoll dodge in
            characterRoot.GetComponentsInChildren<PlayerDodgeRoll>(true))
            dodge.enabled = false;
        // Animation on ANY child of the player can fight a jointed ragdoll.
        foreach (Animator animator in characterRoot.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
        foreach (Animation animation in characterRoot.GetComponentsInChildren<Animation>(true))
            animation.enabled = false;

        var bodies = new Dictionary<Transform, Rigidbody>(valid.Count);
        var boneColliders = new List<Collider>(valid.Count);
        var boneColliderSet = new HashSet<Collider>();
        var ragdollBodySet = new HashSet<Rigidbody>();

        // Pass 1: create dedicated PHYSICS colliders. Never turn an existing
        // hitbox/trigger collider into a solid collider: hitboxes can be huge
        // and their overlap with the world causes ragdoll explosions.
        foreach (DeathRagdollBone spec in valid)
        {
            Rigidbody rb = spec.bone.GetComponent<Rigidbody>();
            if (rb == null) rb = spec.bone.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = true;
            rb.mass = spec.mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.solverIterations = 18;
            rb.solverVelocityIterations = 12;
            rb.maxAngularVelocity = 12f;
            rb.maxDepenetrationVelocity = 4f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 1.5f;
            bodies.Add(spec.bone, rb);
            ragdollBodySet.Add(rb);

            Collider collider = CreateDeathRagdollCollider(spec);
            collider.isTrigger = false;
            collider.enabled = true;
            collider.excludeLayers = 0;
            boneColliders.Add(collider);
            boneColliderSet.Add(collider);
        }

        // Pass 2: connect the joints while every body is still kinematic.
        foreach (DeathRagdollBone spec in valid)
        {
            if (spec.bone == hips) continue;
            Transform parent = spec.bone.parent;
            while (parent != null && !bodies.ContainsKey(parent))
                parent = parent.parent;
            if (parent == null) continue;

            CharacterJoint joint = spec.bone.GetComponent<CharacterJoint>();
            if (joint == null) joint = spec.bone.gameObject.AddComponent<CharacterJoint>();
            // Joint is not a Behaviour and has no enabled property.
            // Remove competing joints rather than trying to disable them.
            foreach (Joint other in spec.bone.GetComponents<Joint>())
            {
                if (other != joint) Destroy(other);
            }
            joint.connectedBody = bodies[parent];
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = parent.InverseTransformPoint(spec.bone.position);

            // Align the twist axis with the actual limb, instead of using the
            // same arbitrary local X axis for every arm, leg and spine bone.
            Vector3 worldAxis = spec.end != null
                ? spec.end.position - spec.bone.position
                : spec.bone.position - parent.position;
            if (worldAxis.sqrMagnitude < 0.0001f)
                worldAxis = spec.bone.up;
            Vector3 axis = spec.bone.InverseTransformDirection(worldAxis.normalized);
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.up;
            axis.Normalize();
            Vector3 swing = Vector3.Cross(axis, Vector3.forward);
            if (swing.sqrMagnitude < 0.0001f)
                swing = Vector3.Cross(axis, Vector3.up);
            joint.axis = axis;
            joint.swingAxis = swing.normalized;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;

            bool isKneeOrElbow = spec.bone == leftKnee || spec.bone == rightKnee ||
                spec.bone == leftElbow || spec.bone == rightElbow;
            float twist = isKneeOrElbow ? 20f : 40f;
            float swingLimit = isKneeOrElbow ? 40f : 60f;
            joint.lowTwistLimit = new SoftJointLimit { limit = -twist };
            joint.highTwistLimit = new SoftJointLimit { limit = twist };
            joint.swing1Limit = new SoftJointLimit { limit = swingLimit };
            joint.swing2Limit = new SoftJointLimit { limit = swingLimit };
        }

        // Freeze any OTHER rigidbodies on the player root/model. An active root
        // rigidbody can move the parent Transform while the bones simulate.
        foreach (Rigidbody other in characterRoot.GetComponentsInChildren<Rigidbody>(true))
        {
            if (other == null || ragdollBodySet.Contains(other)) continue;
            if (!other.isKinematic)
            {
                other.linearVelocity = Vector3.zero;
                other.angularVelocity = Vector3.zero;
            }
            other.isKinematic = true;
        }

        if (disableNonRagdollCollidersOnDeath)
        {
            foreach (Collider collider in characterRoot.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && !boneColliderSet.Contains(collider))
                    collider.enabled = false;
            }
        }

        // All bone-vs-bone and bone-vs-boss contacts are suppressed, including
        // adjacent bones, so the corpse cannot explode from initial overlaps.
        Collider[] bossColliders = ignoreBossCollidersOnDeath
            ? GetComponentsInChildren<Collider>(true) : null;
        for (int i = 0; i < boneColliders.Count; i++)
        {
            Collider a = boneColliders[i];
            for (int j = i + 1; j < boneColliders.Count; j++)
                Physics.IgnoreCollision(a, boneColliders[j], true);
            if (bossColliders == null) continue;
            foreach (Collider b in bossColliders)
                if (b != null && b != a) Physics.IgnoreCollision(a, b, true);
        }

        // A fixed-step delay gives Unity a chance to register the new joints
        // before launching. A single giant impulse on Hips would wrench the
        // pelvis away from the other bodies and destabilize the entire rig.
        StartCoroutine(ReleaseAndLaunchDeathRagdoll(bodies, hips, direction, mapper));
        Log("PREPARED STABLE DEATH RAGDOLL USING BONEMAPPER | bones=" + bodies.Count);
        return true;
    }

    private static Collider CreateDeathRagdollCollider(DeathRagdollBone spec)
    {
        Vector3 scale = spec.bone.lossyScale;
        float sx = Mathf.Max(0.001f, Mathf.Abs(scale.x));
        float sy = Mathf.Max(0.001f, Mathf.Abs(scale.y));
        float sz = Mathf.Max(0.001f, Mathf.Abs(scale.z));

        if (spec.sphere || spec.end == null)
        {
            SphereCollider sphere = spec.bone.gameObject.AddComponent<SphereCollider>();
            sphere.center = Vector3.zero;
            sphere.radius = spec.radius / Mathf.Max(sx, Mathf.Max(sy, sz));
            return sphere;
        }

        Vector3 localEnd = spec.bone.InverseTransformPoint(spec.end.position);
        Vector3 abs = new Vector3(Mathf.Abs(localEnd.x),
            Mathf.Abs(localEnd.y), Mathf.Abs(localEnd.z));
        int axis = abs.x > abs.y && abs.x > abs.z ? 0 : (abs.z > abs.y ? 2 : 1);
        float axisScale = axis == 0 ? sx : (axis == 1 ? sy : sz);
        float perpendicularScale = axis == 0 ? Mathf.Max(sy, sz) :
            (axis == 1 ? Mathf.Max(sx, sz) : Mathf.Max(sx, sy));
        float length = Vector3.Distance(spec.bone.position, spec.end.position);
        float worldRadius = Mathf.Min(spec.radius, Mathf.Max(0.018f, length * 0.23f));
        float localRadius = worldRadius / perpendicularScale;
        float localLength = length / axisScale;

        CapsuleCollider capsule = spec.bone.gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = axis;
        capsule.center = localEnd * 0.5f;
        capsule.radius = localRadius;
        // Keep the capsule INSIDE the bone segment. Oversized capsules clip
        // into floors and neighboring geometry when the ragdoll first spawns.
        capsule.height = Mathf.Max(localRadius * 2.05f,
            localLength * 0.88f);
        return capsule;
    }

    private IEnumerator ReleaseAndLaunchDeathRagdoll(
        Dictionary<Transform, Rigidbody> bodies, Transform hips,
        Vector3 direction, BoneMapper mapper)
    {
        yield return new WaitForFixedUpdate();
        if (mapper == null || !mapper.gameObject.activeInHierarchy) yield break;

        // A death callback might have re-enabled animation after we prepared
        // the rig. Keep the physics skeleton fully in charge of bone transforms.
        foreach (Animator animator in mapper.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
        foreach (ActiveRagdollSystem active in
            mapper.GetComponentsInChildren<ActiveRagdollSystem>(true))
            active.enabled = false;
        Physics.SyncTransforms();
        Vector3 push = direction;
        push.y = 0f;
        if (push.sqrMagnitude < 0.001f)
        {
            push = transform.forward;
            push.y = 0f;
        }
        if (push.sqrMagnitude < 0.001f) push = Vector3.forward;
        push.Normalize();

        float totalMass = 0f;
        foreach (Rigidbody rb in bodies.Values)
            if (rb != null) totalMass += rb.mass;

        // Keep the strong flying launch, but distribute extra impact momentum
        // across the entire body. Do not kick only the hips.
        float bonusSpeed = Mathf.Max(0f, playerImpactForce) /
            Mathf.Max(25f, totalMass * 0.5f);
        Vector3 launchVelocity = push * (playerLaunchForwardSpeed + bonusSpeed) +
            Vector3.up * playerLaunchUpwardSpeed;

        // Apply one coherent angular velocity to the entire body, with the
        // corresponding tangential velocity at each bone's center of mass.
        // This makes the ragdoll tumble as a whole instead of twisting apart.
        Vector3 tumbleAxis = Vector3.Cross(Vector3.up, push);
        Vector3 omega = tumbleAxis.sqrMagnitude > 0.0001f
            ? tumbleAxis.normalized * Mathf.Min(playerLaunchTumble, 2.5f)
            : Vector3.zero;
        Rigidbody hipsBody;
        Vector3 pivot = bodies.TryGetValue(hips, out hipsBody) && hipsBody != null
            ? hipsBody.worldCenterOfMass : hips.position;

        foreach (Rigidbody rb in bodies.Values)
        {
            if (rb == null) continue;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = launchVelocity +
                Vector3.Cross(omega, rb.worldCenterOfMass - pivot);
            rb.angularVelocity = omega;
            rb.WakeUp();
        }
        Log("STABLE DEATH RAGDOLL LAUNCHED | speed=" +
            launchVelocity.magnitude.ToString("F1") + "m/s | bones=" + bodies.Count);
    }

    private static void AddDeathBone(List<DeathRagdollBone> list, Transform bone,
        Transform end, float mass, float radius, bool sphere = false)
    {
        if (bone != null)
            list.Add(new DeathRagdollBone(bone, end, mass, radius, sphere));
    }

    private static Transform FirstHumanoidBone(Animator animator,
        HumanBodyBones first, HumanBodyBones second, HumanBodyBones third)
    {
        Transform t = animator.GetBoneTransform(first);
        if (t != null) return t;
        t = animator.GetBoneTransform(second);
        return t != null ? t : animator.GetBoneTransform(third);
    }

    private static Transform InferTorsoBone(Transform hips, Transform head)
    {
        if (hips == null || head == null) return null;
        var chain = new List<Transform>();
        for (Transform t = head.parent; t != null && t != hips; t = t.parent)
            chain.Add(t);
        return chain.Count > 0 ? chain[chain.Count / 2] : null;
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

        if (stealthAI != null)
            stealthAI.enabled = stealthAIWasEnabled && ready;
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
        if (!playerKilledByCharge) RestoreControllers();

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
