
using System;
using System.Collections;
using UnityEngine;
using FS_Core;
using FS_LocomotionSystem;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
    [Tooltip("Press R once to dodge in the held movement direction.")]
    public KeyCode dodgeKey = KeyCode.R;

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
    public override float Priority => 10f;

    public bool IsDodging { get; private set; }
    public int DodgeSequence { get; private set; }

    public float LastDodgeStartTime { get; private set; }
        = float.NegativeInfinity;

    public event Action OnDodgeStarted;
    public event Action OnDodgeFinished;

    private PlayerController player;
    private LocomotionController locomotion;
    private LocomotionCore core;
    private Animator animator;

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
        locomotion = GetComponent<LocomotionController>();
        core = GetComponent<LocomotionCore>();
        animator = GetComponent<Animator>();
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

        Log("Dodge system initialized. Press R to dodge.");
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
            // Supports the configurable dodge key
            // when using Unity's New Input System.
            UnityEngine.InputSystem.Key inputKey =
                (UnityEngine.InputSystem.Key)(int)dodgeKey;

            // KeyCode and Input System Key enums do not
            // share numeric values, so explicitly map R.
            if (dodgeKey == KeyCode.R)
            {
                pressed |=
                    Keyboard.current.rKey.wasPressedThisFrame;
            }
            else
            {
                // Support additional letter keys.
                string keyName = dodgeKey.ToString().ToLowerInvariant();

                if (Enum.TryParse(
                    keyName,
                    true,
                    out UnityEngine.InputSystem.Key parsedKey))
                {
                    var keyControl =
                        Keyboard.current[parsedKey];

                    if (keyControl != null)
                        pressed |= keyControl.wasPressedThisFrame;
                }
            }
        }
#endif

        return pressed;
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
