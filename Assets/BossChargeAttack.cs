
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

    public bool IsCharging { get; private set; }
    public bool IsRecovering { get; private set; }

    // Legacy name: means steering is locked, NOT sight lost.
    public bool HasLostTracking { get; private set; }

    public bool IsBusy => IsCharging || IsRecovering;

    public float RecoveryTimeRemaining =>
        IsRecovering
            ? Mathf.Max(0f, recoveryEndTime - Time.time)
            : 0f;

    private BossEnemyAI boss;
    private AIController ai;
    private NavMeshAgent agent;
    private PlayerDodgeRoll playerDodge;

    private bool aiWasEnabled;
    private bool agentWasStopped;
    private bool agentWasUpdatingPosition;
    private bool agentWasUpdatingRotation;
    private bool savedControllerState;
    private bool animatorRootMotionWasEnabled;
    private bool savedAnimatorState;

    private float nextChargeTime;
    private float chargeStartTime;
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
            BeginCharge();
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

    private void BeginCharge()
    {
        if (IsBusy || player == null)
            return;

        Vector3 direction =
            player.position - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        chargeDirection = direction.normalized;
        chargeStartPosition = transform.position;
        chargeStartTime = Time.time;
        nextChargeTime = Time.time + chargeCooldown;

        dodgeSequenceAtChargeStart =
            playerDodge != null
                ? playerDodge.DodgeSequence
                : -1;

        HasLostTracking = false;

        hitCovers.Clear();
        destroyedCoverRoots.Clear();
        ignoredDebrisColliders.Clear();

        aiWasEnabled = ai != null && ai.enabled;

        agentWasStopped =
            agent != null &&
            agent.enabled &&
            agent.isOnNavMesh &&
            agent.isStopped;

        agentWasUpdatingPosition =
            agent != null && agent.updatePosition;

        agentWasUpdatingRotation =
            agent != null && agent.updateRotation;

        savedControllerState = true;

        if (ai != null)
            ai.enabled = false;

        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        if (bossAnimator != null)
        {
            animatorRootMotionWasEnabled =
                bossAnimator.applyRootMotion;

            savedAnimatorState = true;
            bossAnimator.applyRootMotion = false;
        }

        transform.rotation =
            Quaternion.LookRotation(chargeDirection);

        IsCharging = true;

        PlayState(dashAnimation, dashCrossfade);

        Log("CHARGE STARTED | Speed=" + chargeSpeed);
    }

    private void UpdateCharge()
    {
        if (Time.time - chargeStartTime >= chargeDuration)
        {
            StopCharge("Charge duration finished");
            return;
        }

        CheckPlayerDodge();

        if (!HasLostTracking)
            TrackPlayer();

        Vector3 displacement =
            chargeDirection * chargeSpeed * Time.deltaTime;

        if (displacement.sqrMagnitude < 0.000001f)
            return;

        if (obstacleLayers.value != 0 &&
            CheckChargeCollisions(displacement))
        {
            return;
        }

        transform.position += displacement;

        if (logMovement)
            Log("CHARGE MOVED | " + transform.position);
    }

    private void CheckPlayerDodge()
    {
        if (HasLostTracking ||
            playerDodge == null ||
            playerDodge.DodgeSequence <= dodgeSequenceAtChargeStart)
        {
            return;
        }

        // Dodge only locks the direction of movement.
        // BossEnemyAI still updates visual awareness.
        HasLostTracking = true;

        chargeDirection = transform.forward;
        chargeDirection.y = 0f;

        if (chargeDirection.sqrMagnitude > 0.0001f)
            chargeDirection.Normalize();

        Log(
            "PLAYER DODGED - STEERING LOCKED, SIGHT RETAINED"
        );
    }

    private void TrackPlayer()
    {
        if (player == null)
            return;

        Vector3 direction =
            player.position - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            chargeTurnSpeed * Time.deltaTime
        );

        chargeDirection = transform.forward;
        chargeDirection.y = 0f;
        chargeDirection.Normalize();
    }

    private bool CheckChargeCollisions(Vector3 displacement)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            transform.position + Vector3.up * collisionHeight,
            collisionRadius,
            displacement.normalized,
            displacement.magnitude,
            obstacleLayers,
            QueryTriggerInteraction.Ignore
        );

        Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance)
        );

        foreach (RaycastHit hit in hits)
        {
            if (ProcessHit(hit.collider))
                return true;
        }

        return false;
    }

    private bool ProcessHit(Collider collider)
    {
        if (collider == null)
            return false;

        Transform target = collider.transform;

        if (target == transform ||
            target.IsChildOf(transform))
        {
            return false;
        }

        if (ignoredDebrisColliders.Contains(collider))
            return false;

        if (ignoreCoverFragments && IsCoverFragment(collider))
        {
            ignoredDebrisColliders.Add(collider);
            return false;
        }

        foreach (Transform root in destroyedCoverRoots)
        {
            if (root != null &&
                (target == root || target.IsChildOf(root)))
            {
                return false;
            }
        }

        Cover cover = collider.GetComponentInParent<Cover>();

        if (cover != null)
        {
            if (hitCovers.Contains(cover) || cover.IsDestroyed)
                return false;

            hitCovers.Add(cover);

            Transform coverRoot = cover.transform;
            string coverName = cover.name;

            cover.TakeDamage(coverDamage);

            if (cover.IsDestroyed)
            {
                destroyedCoverRoots.Add(coverRoot);
                Log("DESTROYED COVER: " + coverName);
                return false;
            }

            StopCharge("Durable cover: " + coverName);
            return true;
        }

        if (player != null)
        {
            Transform playerRoot = player.root;

            if (target == player ||
                target.IsChildOf(player) ||
                target == playerRoot ||
                target.IsChildOf(playerRoot))
            {
                StopCharge("Collided with player");
                return true;
            }
        }

        StopCharge("Obstacle collision: " + collider.name);
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
        if (!IsCharging)
            return;

        IsCharging = false;
        IsRecovering = true;
        stunnedEntered = false;

        // EXACT recovery duration.
        // Never use animation clip length here.
        recoveryEndTime = Time.time + recoveryDuration;

        // AI must remain disabled for the full recovery.
        if (ai != null)
            ai.enabled = false;

        if (agent != null &&
            agent.enabled &&
            agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        stunnedHash = Animator.StringToHash(stunnedAnimation);

        // The stun starts immediately when charging stops.
        PlayState(stunnedAnimation, stunnedCrossfade);

        // Lose combat targeting only AFTER the charge ends.
        if (boss != null)
            boss.ForgetPlayerAfterCharge();

        float traveled = Vector3.Distance(
            chargeStartPosition,
            transform.position
        );

        Log(
            "CHARGE STOPPED | " + reason +
            " | Traveled=" + traveled.ToString("F2") + "m"
        );

        Log(
            "STUNNED STARTED | Exact recovery=" +
            recoveryDuration.ToString("F2") + "s"
        );
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
        if (!savedControllerState)
            return;

        bool ready = false;

        if (agent != null && agent.enabled)
        {
            NavMeshHit hit;

            if (NavMesh.SamplePosition(
                transform.position,
                out hit,
                navMeshRecoverySampleRadius,
                NavMesh.AllAreas))
            {
                ready =
                    agent.Warp(hit.position) &&
                    agent.isOnNavMesh;
            }

            agent.updatePosition =
                agentWasUpdatingPosition;

            agent.updateRotation =
                agentWasUpdatingRotation;

            if (ready)
                agent.isStopped = agentWasStopped;
        }

        if (savedAnimatorState && bossAnimator != null)
        {
            bossAnimator.applyRootMotion =
                animatorRootMotionWasEnabled;
        }

        savedAnimatorState = false;

        if (ai != null)
        {
            if (ready)
            {
                ai.enabled = aiWasEnabled;
            }
            else
            {
                Debug.LogWarning(
                    "[BossChargeAttack] NavMesh recovery failed. " +
                    "AI remains disabled.",
                    this
                );
            }
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
