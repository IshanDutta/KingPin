
using System;
using System.Collections;
using UnityEngine;
using FS_Core;
using FS_LocomotionSystem;
using FS_ParkourSystem;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(LocomotionController))]
[RequireComponent(typeof(LocomotionCore))]
public class PlayerDodgeRoll : SystemBase
{
    [Header("Directional Roll Animator States")]
    [Tooltip("Exact Animator state name on Base Layer.")]
    public string forwardRoll = "RollForward";
    public string backwardRoll = "RollBackward";
    public string leftRoll = "RollLeft";
    public string rightRoll = "RollRight";

    [Header("Input")]
    [Tooltip("Press Space once to dodge when no parkour action has priority.")]
    public KeyCode dodgeKey = KeyCode.Space;

    [Header("Dodge")]
    public bool requireGrounded = true;

    [Min(0f)]
    public float dodgeCooldown = 0.25f;

    [Range(0f, 0.3f)]
    public float transitionDuration = 0.08f;

    [Min(0.1f)]
    public float maximumRollDuration = 2f;

    [Header("Root Motion")]
    public bool useRootMovement = true;
    public bool useRootRotation = false;

    [Header("Debug")]
    public bool debugLogs = true;

    public override SystemState State => SystemState.Action;
    public override float Priority => 1f;

    public bool IsDodging { get; private set; }
    public int DodgeSequence { get; private set; }

    public float LastDodgeStartTime { get; private set; }
        = float.NegativeInfinity;

    public event Action OnDodgeStarted;
    public event Action OnDodgeFinished;

    private PlayerController player;
    private ICharacter character;
    private LocomotionController locomotion;
    private LocomotionCore core;
    private Animator animator;
    private ParkourController parkour;
    private EnvironmentScanner scanner;
    private ClimbController climb;

    private bool registered;
    private bool ownsFocus;

    private float nextAllowedDodgeTime;

    private Coroutine dodgeRoutine;
    private Coroutine actionRoutine;

    private CharacterAction currentDodgeAction;

    // =====================================================
    // INITIALIZATION
    // =====================================================

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        character = GetComponent<ICharacter>();
        locomotion = GetComponent<LocomotionController>();
        core = GetComponent<LocomotionCore>();
        animator = GetComponent<Animator>();
        parkour = GetComponent<ParkourController>();
        scanner = GetComponent<EnvironmentScanner>();
        climb = GetComponent<ClimbController>();
    }

    private void Start()
    {
        if (player == null ||
            locomotion == null ||
            core == null ||
            animator == null)
        {
            Debug.LogError(
                "[PlayerDodgeRoll] Required components are missing.",
                this
            );

            enabled = false;
            return;
        }

        if (core.ActionExecutor == null)
        {
            Debug.LogError(
                "[PlayerDodgeRoll] ActionExecutor is missing.",
                this
            );

            enabled = false;
            return;
        }

        RegisterDodgeSystem();

        Log("Dodge system initialized. Press Space to dodge when vault/predictive jump is unavailable.");
    }

    private void RegisterDodgeSystem()
    {
        player.Register(this, true);

        if (player.CoreSystems == null)
        {
            player.CoreSystems =
                new System.Collections.Generic.List<SystemBase>();
        }

        if (!player.CoreSystems.Contains(this))
            player.CoreSystems.Add(this);

        player.CoreSystems.Sort(
            (a, b) => b.Priority.CompareTo(a.Priority)
        );

        registered = true;

        Log(
            "Registered in CoreSystems: " +
            player.CoreSystems.Contains(this)
        );
    }

    // =====================================================
    // INPUT
    // =====================================================

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;

        if (IsDodging)
            return;

        if (!WasDodgePressedThisFrame())
            return;

        Log("DODGE KEY DETECTED: " + dodgeKey);

        if (Time.time < nextAllowedDodgeTime)
        {
            Log("Dodge on cooldown.");
            return;
        }

        if (ParkourHasPriority())
        {
            Log("Space reserved for vault/predictive jump/climb.");
            return;
        }

        TryStartDodge();
    }

    private bool WasDodgePressedThisFrame()
    {
        bool pressed = false;
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(dodgeKey);
#endif
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (dodgeKey == KeyCode.Space)
                pressed |= Keyboard.current.spaceKey.wasPressedThisFrame;
            else if (dodgeKey == KeyCode.R)
                pressed |= Keyboard.current.rKey.wasPressedThisFrame;
        }
#endif
        return pressed;
    }

    // Same-frame eligibility checks based on ParkourController's own
    // HandleParkourAction and HandlePredictiveJumpAndClimb conditions.
    // These do not trigger parkour; they only reserve Space for it.
    private bool ParkourHasPriority()
    {
        if (parkour == null || !parkour.enabled)
            return false;

        if (parkour.ControlledByParkour)
            return true;

        if (scanner == null || !locomotion.IsGrounded)
            return false;

        var hit = scanner.ObstacleCheck();
        if (hit.forwardHitFound && hit.heightHitFound && hit.hasSpace &&
            Vector3.Angle(Vector3.up, hit.forwardHit.normal) > 45f &&
            Vector3.Angle(Vector3.up, hit.heightHit.normal) <= 45f)
        {
            foreach (var action in parkour.parkourActions)
            {
                if (action != null && action.CheckIfPossible(hit, transform))
                    return true;
            }
        }

        if (parkour.enablePredictiveJump ||
            (climb != null && climb.enableClimbing))
        {
            // This intentionally errs on the side of preserving parkour.
            // The controller performs more detailed ledge/distance checks.
            var jump = scanner.FindPointToJump(
                character != null ? character.MoveDir : transform.forward,
                parkour.MaxJumpDistance,
                parkour.MaxJumpHeight);

            if (jump != null)
            {
                if (parkour.enablePredictiveJump && jump.jumpPointFound)
                    return true;
                if (climb != null && climb.enableClimbing && jump.isClimbable)
                    return true;
            }
        }

        return false;
    }

    private bool IsKeyHeld(KeyCode key)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(key);
#else
        return false;
#endif
    }

    private bool IsForwardHeld()
    {
        bool held =
            IsKeyHeld(KeyCode.W) ||
            IsKeyHeld(KeyCode.UpArrow);

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            held |= Keyboard.current.wKey.isPressed;
            held |= Keyboard.current.upArrowKey.isPressed;
        }
#endif

        return held;
    }

    private bool IsBackwardHeld()
    {
        bool held =
            IsKeyHeld(KeyCode.S) ||
            IsKeyHeld(KeyCode.DownArrow);

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            held |= Keyboard.current.sKey.isPressed;
            held |= Keyboard.current.downArrowKey.isPressed;
        }
#endif

        return held;
    }

    private bool IsLeftHeld()
    {
        bool held =
            IsKeyHeld(KeyCode.A) ||
            IsKeyHeld(KeyCode.LeftArrow);

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            held |= Keyboard.current.aKey.isPressed;
            held |= Keyboard.current.leftArrowKey.isPressed;
        }
#endif

        return held;
    }

    private bool IsRightHeld()
    {
        bool held =
            IsKeyHeld(KeyCode.D) ||
            IsKeyHeld(KeyCode.RightArrow);

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            held |= Keyboard.current.dKey.isPressed;
            held |= Keyboard.current.rightArrowKey.isPressed;
        }
#endif

        return held;
    }

    // =====================================================
    // DIRECTIONAL ANIMATION
    // =====================================================

    private string GetDirectionalAnimation()
    {
        bool forward = IsForwardHeld();
        bool backward = IsBackwardHeld();
        bool left = IsLeftHeld();
        bool right = IsRightHeld();

        if (left && !right)
            return leftRoll;

        if (right && !left)
            return rightRoll;

        if (backward && !forward)
            return backwardRoll;

        return forwardRoll;
    }

    // =====================================================
    // START DODGE
    // =====================================================

    private void TryStartDodge()
    {
        if (IsDodging)
            return;

        Log("Attempting dodge...");

        if (ParkourHasPriority())
        {
            Log("Rejected: parkour has priority.");
            return;
        }

        if (player.IsDead)
        {
            Log("Rejected: player is dead.");
            return;
        }

        if (requireGrounded && !locomotion.IsGrounded)
        {
            Log("Rejected: player is airborne.");
            return;
        }

        if (locomotion.PreventAllSystems)
        {
            Log("Rejected: locomotion is locked.");
            return;
        }

        if (player.FocusedScript != null &&
            player.FocusedScript != this)
        {
            Log(
                "Rejected: focused system is " +
                player.FocusedScript.GetType().Name
            );

            return;
        }

        string animationName = GetDirectionalAnimation();

        Log("Selected animation: " + animationName);

        if (string.IsNullOrEmpty(animationName))
        {
            Log("Rejected: animation name is empty.");
            return;
        }

        int stateHash =
            Animator.StringToHash(animationName);

        if (!animator.HasState(0, stateHash))
        {
            Debug.LogError(
                "[PlayerDodgeRoll] Animator state '" +
                animationName +
                "' does not exist on Base Layer.",
                this
            );

            return;
        }

        locomotion.FocusSystem(this);

        if (player.FocusedScript != this)
        {
            Log(
                "Focus failed. IsInFocus=" +
                IsInFocus +
                ", CoreSystemsContains=" +
                player.CoreSystems.Contains(this)
            );

            if (IsInFocus)
                locomotion.UnFocusSystem(this);

            return;
        }

        ownsFocus = true;
        IsDodging = true;

        // Boss charge integration:
        // Only count successfully started dodges.
        LastDodgeStartTime = Time.time;
        DodgeSequence++;

        OnDodgeStarted?.Invoke();

        Log(
            "FOCUS ACQUIRED! DodgeSequence=" +
            DodgeSequence
        );

        dodgeRoutine = StartCoroutine(
            ExecuteDodge(animationName)
        );
    }

    // =====================================================
    // EXECUTE DODGE
    // =====================================================

    private IEnumerator ExecuteDodge(string animationName)
    {
        Log("Starting dodge animation: " + animationName);

        core.ResetCurrentVelocity();
        core.ResetTargetRotation();

        currentDodgeAction = new CharacterAction
        {
            animName = animationName,

            usesRootMotion = true,
            usesRootMovement = useRootMovement,
            usesRootRotation = useRootRotation,

            usesGravity = true,
            resetMoveAmount = true,
            requiresLedgeBlocking = false,

            useFixedTransition = true,

            animationParameters = new AnimParameters
            {
                transitionIn = transitionDuration
            }
        };

        bool actionFinished = false;

        CharacterAction action = currentDodgeAction;

        actionRoutine = StartCoroutine(
            RunDodgeAction(
                action,
                () => actionFinished = true
            )
        );

        float elapsed = 0f;

        while (!actionFinished &&
               elapsed < maximumRollDuration)
        {
            if (player.FocusedScript != this)
            {
                Log("Dodge interrupted: focus lost.");
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!actionFinished)
        {
            Log("Dodge timed out or was interrupted.");

            if (core != null &&
                core.ActionExecutor != null)
            {
                core.ActionExecutor.Cancel(action);
            }
        }

        FinishDodge();
    }

    private IEnumerator RunDodgeAction(
        CharacterAction action,
        Action onFinished)
    {
        Log("Sending animation to ActionExecutor...");

        yield return core.ActionExecutor.ExecuteAnimatorAction(
            action
        );

        Log("ActionExecutor completed animation.");
        onFinished?.Invoke();
    }

    // =====================================================
    // FINISH DODGE
    // =====================================================

    private void FinishDodge()
    {
        if (!IsDodging && !ownsFocus)
            return;

        bool wasDodging = IsDodging;
        IsDodging = false;

        if (wasDodging)
            OnDodgeFinished?.Invoke();

        currentDodgeAction = null;

        nextAllowedDodgeTime =
            Time.time + dodgeCooldown;

        if (core != null)
        {
            core.ResetCurrentVelocity();
            core.ResetTargetRotation();
        }

        if (ownsFocus &&
            locomotion != null &&
            player != null &&
            player.FocusedScript == this)
        {
            locomotion.UnFocusSystem(this);
        }

        ownsFocus = false;
        dodgeRoutine = null;
        actionRoutine = null;

        Log("Dodge complete.");
    }

    // =====================================================
    // FANTACODE SYSTEM OVERRIDES
    // =====================================================

    public override void HandleUpdate()
    {
        // ActionExecutor controls animation during dodge.
    }

    public override void HandleFixedUpdate()
    {
    }

    // =====================================================
    // CLEANUP
    // =====================================================

    private void OnDisable()
    {
        if (dodgeRoutine != null)
        {
            StopCoroutine(dodgeRoutine);
            dodgeRoutine = null;
        }

        if (actionRoutine != null)
        {
            StopCoroutine(actionRoutine);
            actionRoutine = null;
        }

        if (currentDodgeAction != null &&
            core != null &&
            core.ActionExecutor != null)
        {
            core.ActionExecutor.Cancel(currentDodgeAction);
        }

        FinishDodge();
    }

    private void OnDestroy()
    {
        if (player == null)
            return;

        if (player.CoreSystems != null)
            player.CoreSystems.Remove(this);

        if (registered)
        {
            player.Unregister(this);
            registered = false;
        }
    }

    // =====================================================
    // DEBUG
    // =====================================================

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log(
                "[PlayerDodgeRoll] " + message,
                this
            );
        }
    }
}
