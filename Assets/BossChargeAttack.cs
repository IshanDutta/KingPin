
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

    [Header("Charge Conditions")]
    [SerializeField, Min(0f)] private float minimumChargeDistance = 7f;
    [SerializeField, Min(0f)] private float maximumChargeDistance = 24f;
    [SerializeField, Min(0f)] private float chargeCooldown = 5f;

    [Header("Charge Movement")]
    [SerializeField, Min(0f)] private float chargeSpeed = 15f;
    [SerializeField, Min(0.05f)] private float chargeDuration = 1.5f;
    [SerializeField, Min(0f)] private float chargeTurnSpeed = 400f;
    [Tooltip("How often to update the pursuit destination while NavMesh controls the charge.")]
    [SerializeField, Min(0.03f)] private float chargePathRefreshInterval = 0.1f;
    [SerializeField, Min(0.2f)] private float playerNavMeshSampleRadius = 2f;
    [SerializeField, Min(1f)] private float chargeAcceleration = 100f;
    [Tooltip("Proactive cover impact probe, in addition to the distance moved this frame.")]
    [SerializeField, Min(0.1f)] private float coverLookAhead = 1.25f;
    [Header("Cover Navigation Rules")]
    [Tooltip("Before a dodge: break cover at or below this CURRENT health, and navigate around stronger cover. After a dodge: smash any cover in the straight charge path.")]
    [SerializeField, Min(0.01f)] private float preDodgeCoverBreakHealth = 25f;
    [Tooltip("Temporarily create carving NavMeshObstacles on durable cover if it doesn't already have one. Restored/removed at recovery.")]
    [SerializeField] private bool createTemporaryObstaclesForDurableCover = true;

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

    public bool IsWindingUp { get; private set; }
    public bool IsCharging { get; private set; }
    public bool IsRecovering { get; private set; }

    // Legacy name: means steering is locked, NOT sight lost.
    public bool HasLostTracking { get; private set; }

    public bool IsBusy => IsWindingUp || IsCharging || IsRecovering;

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
    // Preserve the exact scene state: weak cover must not block the NavMesh,
    // while durable cover must remain a walk-around obstacle until dodged.
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
    private readonly List<NavMeshObstacle> temporaryCoverObstacles =
        new List<NavMeshObstacle>();
    private int coverColliderLayers;
    private bool agentWasStopped;
    private bool agentWasUpdatingPosition;
    private bool agentWasUpdatingRotation;
    private bool savedControllerState;
    private bool animatorRootMotionWasEnabled;
    private bool savedAnimatorState;

    private float nextChargeTime;
    private float chargeStartTime;
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

        if (Time.time < nextChargeTime ||
            boss == null ||
            !boss.HasVisualCombatTarget ||
            !ResolvePlayer())
        {
            return;
        }

        float distance = Vector3.Distance(
            transform.position,
            player.position
        );

        if (distance >= minimumChargeDistance &&
            distance <= maximumChargeDistance)
        {
            BeginWindup();
        }
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

        SaveAndPauseControllers();
        PrepareCoverNavMeshObstacles();

        transform.rotation = Quaternion.LookRotation(towardPlayer.normalized);
        IsWindingUp = true;
        windupEndTime = Time.time + chargeWindupDuration;
        PlayState(detectedAnimation, detectedCrossfade);
        Log("CHARGE WINDUP - NavMesh pursuit next");
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
        chargeStartTime = Time.time;
        nextChargeTime = Time.time + chargeCooldown;
        nextChargePathRefresh = 0f;
        dodgeSequenceAtChargeStart = playerDodge != null
            ? playerDodge.DodgeSequence : -1;
        HasLostTracking = false;
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
        if (Time.time - chargeStartTime >= chargeDuration)
        {
            StopCharge("Charge duration finished");
            return;
        }

        CheckPlayerDodge();
        if (!IsCharging) return;

        if (HasLostTracking)
        {
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

        HasLostTracking = true;
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

    // Pre-dodge navigation policy:
    //   <= 25 HP: ignore NavMeshObstacle and smash when contacted.
    //   >  25 HP: keep/construct an obstacle so NavMeshAgent routes around it.
    // The original component state is restored when the charge ends.
    private void PrepareCoverNavMeshObstacles()
    {
        RestoreCoverNavMeshObstacles();
        int durable = 0;
        int fragile = 0;

        foreach (Cover cover in FindObjectsOfType<Cover>())
        {
            if (cover == null || cover.IsDestroyed || !cover.isActiveAndEnabled)
                continue;

            foreach (Collider part in cover.GetComponentsInChildren<Collider>(true))
            {
                if (part != null && part.enabled && !part.isTrigger)
                    coverColliderLayers |= 1 << part.gameObject.layer;
            }

            bool isDurable = cover.CurrentHealth > preDodgeCoverBreakHealth + 0.001f;
            NavMeshObstacle[] existing = cover.GetComponentsInChildren<NavMeshObstacle>(true);

            if (isDurable)
            {
                durable++;
                bool hasUsableObstacle = false;
                foreach (NavMeshObstacle obstacle in existing)
                {
                    if (obstacle == null || !obstacle.gameObject.activeInHierarchy)
                        continue;
                    StoreExistingCoverObstacle(obstacle);
                    // Carving makes the obstacle part of path planning, not
                    // merely an obstacle the moving agent tries to avoid locally.
                    obstacle.carving = true;
                    obstacle.enabled = true;
                    hasUsableObstacle = true;
                }

                if (!hasUsableObstacle && createTemporaryObstaclesForDurableCover)
                    CreateTemporaryDurableCoverObstacle(cover);
            }
            else
            {
                fragile++;
                foreach (NavMeshObstacle obstacle in existing)
                {
                    if (obstacle == null) continue;
                    StoreExistingCoverObstacle(obstacle);
                    // Agents may travel through weak cover and destroy it.
                    obstacle.enabled = false;
                }
            }
        }

        Log("NAVMESH COVER RULES | durable avoid=" + durable +
            " | weak break=" + fragile +
            " | threshold=" + preDodgeCoverBreakHealth.ToString("F1"));
    }

    private void StoreExistingCoverObstacle(NavMeshObstacle obstacle)
    {
        if (obstacle != null && storedCoverObstacleSet.Add(obstacle))
            originalCoverObstacles.Add(new StoredCoverObstacle(obstacle));
    }

    // This supports scenes where the user has REMOVED NavMeshObstacle from
    // cover prefabs: the temporary component only exists for the charge.
    private void CreateTemporaryDurableCoverObstacle(Cover cover)
    {
        Collider[] colliders = cover.GetComponentsInChildren<Collider>(true);
        bool found = false;
        Bounds worldBounds = default(Bounds);
        foreach (Collider part in colliders)
        {
            if (part == null || !part.enabled || part.isTrigger ||
                !part.gameObject.activeInHierarchy) continue;
            if (!found)
            {
                worldBounds = part.bounds;
                found = true;
            }
            else worldBounds.Encapsulate(part.bounds);
        }
        if (!found) return;

        NavMeshObstacle obstacle = cover.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.enabled = false;
        obstacle.shape = NavMeshObstacleShape.Box;

        // Convert all corners of the world bounds to the cover's local frame.
        // This works for child colliders and non-unit cover transforms.
        Bounds localBounds = new Bounds();
        bool first = true;
        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 local = cover.transform.InverseTransformPoint(corner);
                    if (first)
                    {
                        localBounds = new Bounds(local, Vector3.zero);
                        first = false;
                    }
                    else localBounds.Encapsulate(local);
                }
        obstacle.center = localBounds.center;
        Vector3 size = localBounds.size;
        obstacle.size = new Vector3(Mathf.Max(0.2f, size.x),
            Mathf.Max(0.2f, size.y), Mathf.Max(0.2f, size.z));
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        obstacle.enabled = true;
        temporaryCoverObstacles.Add(obstacle);
        Log("TEMPORARY NAVMESH OBSTACLE CREATED FOR DURABLE COVER: " + cover.name);
    }

    private void DisableCoverObstaclesForStraightCharge()
    {
        foreach (StoredCoverObstacle item in originalCoverObstacles)
            if (item.obstacle != null) item.obstacle.enabled = false;
        foreach (NavMeshObstacle obstacle in temporaryCoverObstacles)
            if (obstacle != null) obstacle.enabled = false;
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

        foreach (NavMeshObstacle obstacle in temporaryCoverObstacles)
        {
            if (obstacle == null) continue;
            obstacle.enabled = false;
            Destroy(obstacle);
        }
        temporaryCoverObstacles.Clear();
        coverColliderLayers = 0;
    }

    private void RefreshChargeDestination()
    {
        if (player == null || agent == null || !agent.enabled ||
            !agent.isOnNavMesh || Time.time < nextChargePathRefresh)
            return;
        nextChargePathRefresh = Time.time + chargePathRefreshInterval;

        NavMeshHit point;
        if (!NavMesh.SamplePosition(player.position, out point,
                playerNavMeshSampleRadius, agent.areaMask))
            return;
        if (!agent.SetDestination(point.position) && debugLogs)
            Log("NavMesh could not set chase destination");
    }

    private void UpdateNavMeshCharge()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            // Unexpected off-mesh state: don't silently freeze mid-charge.
            StopCharge("NavMesh charge lost agent");
            return;
        }
        RefreshChargeDestination();
        if (agent.isStopped) agent.isStopped = false;
        TrackPlayer();

        // Test both the displacement already made by the NavMeshAgent and
        // the route just ahead, allowing the boss to smash cover pre-emptively.
        // No stun for ordinary walls in this stage: NavMesh steers around them.
        Vector3 actualStep = transform.position - priorChargePosition;
        actualStep.y = 0f;
        if (actualStep.sqrMagnitude > 0.0001f)
            ProbeChargeHits(priorChargePosition, actualStep.normalized,
                actualStep.magnitude + collisionRadius, false);
        priorChargePosition = transform.position;

        Vector3 ahead = agent.desiredVelocity;
        ahead.y = 0f;
        if (ahead.sqrMagnitude < 0.01f)
            ahead = agent.steeringTarget - transform.position;
        ahead.y = 0f;
        if (ahead.sqrMagnitude > 0.01f)
            ProbeChargeHits(transform.position, ahead.normalized,
                coverLookAhead + chargeSpeed * Time.deltaTime, false);

        // End the charge upon reaching the player; terrain is routed around.
        if (player != null &&
            Vector3.Distance(transform.position, player.position) <=
                collisionRadius + 0.7f)
            StopCharge("Reached player");
    }

    // A capsule matches the boss's body much better than a tiny center ray.
    // In NavMesh mode it destroys only weak Cover; after dodge it smashes
    // every Cover it touches and also stops
    // against permanent solid geometry.
    private bool ProbeChargeHits(Vector3 origin, Vector3 direction,
        float distance, bool stopForSolidCollision)
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
            if (ProcessHit(hit, stopForSolidCollision)) return true;
        }

        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top,
            collisionRadius, direction, distance, mask,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
            if (ProcessHit(hit.collider, stopForSolidCollision)) return true;
        return false;
    }

    private bool CheckChargeCollisions(Vector3 displacement, bool stopForSolidCollision)
    {
        if (displacement.sqrMagnitude < 0.0000001f)
            return false;
        return ProbeChargeHits(transform.position, displacement.normalized,
            displacement.magnitude + collisionRadius, stopForSolidCollision);
    }

    private bool ProcessHit(Collider collider, bool stopForSolidCollision)
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
            // BEFORE DODGE: avoid durable Cover using NavMesh carving;
            // do not damage or mark it as hit. This lets the same piece
            // get smashed later if the player dodges during this charge.
            if (!stopForSolidCollision &&
                cover.CurrentHealth > preDodgeCoverBreakHealth + 0.001f)
            {
                return false;
            }

            hitCovers.Add(cover);
            // Weak cover is destroyed in the NavMesh phase. After a dodge,
            // any cover is destroyed as the boss travels straight ahead.
            cover.TakeDamage(Mathf.Max(coverDamage, cover.CurrentHealth));
            if (cover.IsDestroyed)
            {
                destroyedCoverRoots.Add(coverRoot);
                Log((stopForSolidCollision ? "POST-DODGE SMASHED COVER: " :
                    "NAVMESH SMASHED WEAK COVER: ") + coverName);
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
                StopCharge("Collided with player");
                return true;
            }
        }

        StopCharge("Solid obstacle collision: " + collider.name);
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

    private void StopCharge(string reason)
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

        if (boss != null) boss.ForgetPlayerAfterCharge();

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

        Log(
            "CHARGE RECOVERY COMPLETE - SEARCHING"
        );
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
