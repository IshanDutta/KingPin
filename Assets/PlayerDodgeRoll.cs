
using System;
using System.Collections;
using System.Reflection;
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
    [Tooltip("Double-tap Space to dodge in the held movement direction.")]
    public KeyCode dodgeKey = KeyCode.Space;

    [Min(0.1f)]
    [Tooltip("Maximum time between Space presses for a dodge.")]
    public float doubleTapWindow = 0.3f;

    [Header("Vault Priority")]
    [Tooltip("When Space is shared with Fantacode parkour, let vault / climb / predictive jump win over a dodge.")]
    public bool giveParkourPriority = true;

    [Min(0.5f)]
    [Tooltip("If an obstacle is this close in front of the character, allow the parkour FixedUpdate to process Space before rolling. Normal dodges in open space remain instant.")]
    public float nearVaultCheckDistance = 2.5f;

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

    [Header("Return To Movement")]
    [Tooltip("After the roll, return to the previous moving Animator state if movement is still held. The normal locomotion controller remains in charge of speed and direction.")]
    public bool restoreMovementAfterDodge = true;

    [Tooltip("Optional exact Animator state name to resume when moving (e.g. a locomotion blend tree). Leave empty to restore the state captured before the roll.")]
    public string movementStateOverride = "";

    [Range(0f, 0.3f)]
    public float movementResumeBlend = 0.1f;

    [Header("Roll Pose Recovery")]
    [Tooltip("Exact Base Layer idle state used when movement input is released during a roll.")]
    public string idleStateOverride = "Idle";

    [Min(0.25f)]
    [Tooltip("For this long after a roll, retry leaving its pose if Fantacode's action executor finishes late.")]
    public float rollPoseRecoverySeconds = 0.9f;

    [Min(0.05f)]
    [Tooltip("Minimum time between emergency Animator crossfade retries.")]
    public float rollPoseRetryInterval = 0.16f;

    [Range(1, 10)]
    [Tooltip("Frames to help the Animator recover from the ActionExecutor after the dodge. Normal locomotion remains in control afterwards.")]
    public int movementRecoveryFrames = 8;

    [Header("Sprint Momentum Preservation")]
    [Tooltip("Keep the speed you had before the dodge when you continue holding a movement direction.")]
    public bool preservePreDodgeMomentum = true;

    [Min(0f)]
    [Tooltip("Ignore almost-stationary pre-dodge movement.")]
    public float minimumMomentumSpeed = 0.35f;

    [Range(1, 8)]
    [Tooltip("How many frames to repair velocity if the action executor zeroes it during a roll.")]
    public int momentumRecoveryFrames = 4;

    [Range(0.1f, 1f)]
    [Tooltip("Only restore pre-dodge speed if the controller's current speed fell below this fraction.")]
    public float momentumRestorationThreshold = 0.85f;

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
    private CharacterController unityCharacterController;
    private ParkourController parkour;
    private EnvironmentScanner scanner;
    private ClimbController climb;

    private bool registered;
    private bool ownsFocus;

    private float nextAllowedDodgeTime;
    private float lastSpacePressTime = float.NegativeInfinity;

    private Coroutine dodgeRoutine;
    private Coroutine actionRoutine;
    private Coroutine resumeMovementRoutine;
    private Coroutine pendingParkourDecision;

    private CharacterAction currentDodgeAction;
    private int previousAnimatorStateHash;
    private bool previousStateWasIdle;
    private float previousMoveAmount = 1f;
    private float previousSpeedAmount = 1f;
    private bool warnedNoMovementState;
    private float previousAnimationNormalizedTime;
    private float previousAnimationLength = 1f;

    // Keep the true running velocity, not just the Animator's sprint blend.
    private Vector3 momentumBeforeDodge;
    private bool momentumWasMoving;
    private bool warnedMomentumAccess;

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
        unityCharacterController = GetComponent<CharacterController>();
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

        Log("Dodge initialized. Double-tap Space for a directional dodge; vaulting and jumping retain priority.");
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
        if (Time.timeScale <= 0f || IsDodging)
            return;

        if (!WasDodgePressedThisFrame())
            return;

        float now = Time.unscaledTime;
        bool doubleTap = now - lastSpacePressTime <= doubleTapWindow;
        lastSpacePressTime = now;

        // The first Space press belongs entirely to Fantacode's normal
        // vault / jump input. Only the second press requests a dodge.
        if (!doubleTap)
        {
            Log("First Space press; waiting for second press.");
            return;
        }

        // Consume this pair. A third press begins a new pair.
        lastSpacePressTime = float.NegativeInfinity;

        if (Time.time < nextAllowedDodgeTime)
            return;

        // Never steal focus from a vault, predictive jump or climb.
        if (giveParkourPriority && parkour != null && parkour.isActiveAndEnabled)
        {
            if (ParkourHasPriority() || NearPotentialVaultObstacle())
            {
                Log("Double Space: parkour takes priority.");
                return;
            }
        }

        // TryStartDodge preserves the grounded, focus and action checks,
        // and chooses the directional roll from currently held WASD keys.
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

        // Match ParkourController's grounded condition (ICharacter.IsGrounded).
        if (scanner == null || character == null || !character.IsGrounded)
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

    // Used only for a Space press close to something that might be vaultable.
    // The regular dodge never waits in open space.
    private bool NearPotentialVaultObstacle()
    {
        if (scanner == null || character == null || !character.IsGrounded)
            return false;

        var hit = scanner.ObstacleCheck();
        if (hit.forwardHitFound)
        {
            Vector3 toHit = hit.forwardHit.point - transform.position;
            toHit.y = 0f;
            if (toHit.magnitude <= nearVaultCheckDistance)
                return true;
        }

        // An approach can be just outside the scanner's activation threshold
        // during Update but enter range by the next physics tick. This is a
        // conservative *arbitration* probe: it never starts parkour itself.
        Vector3 direction = character.MoveDir;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            direction = transform.forward;
        if (direction.sqrMagnitude < 0.01f)
            return false;

        RaycastHit wall;
        Vector3 origin = transform.position + Vector3.up * 0.7f;
        if (!Physics.SphereCast(origin, 0.18f, direction.normalized,
                out wall, nearVaultCheckDistance, scanner.ObstacleLayer,
                QueryTriggerInteraction.Ignore))
            return false;

        return wall.collider != null &&
               !wall.collider.transform.IsChildOf(transform) &&
               Vector3.Angle(Vector3.up, wall.normal) > 45f;
    }

    // Physics gets the first opportunity to start a vault or climb.
    // If no parkour action is possible after that check, roll as a fallback.
    private IEnumerator WaitForParkourBeforeRolling()
    {
        Log("Near an obstacle: giving vault first chance on this Space press.");
        yield return new WaitForFixedUpdate();

        pendingParkourDecision = null;
        if (!isActiveAndEnabled || player == null || player.IsDead || IsDodging)
            yield break;

        if (parkour != null && parkour.isActiveAndEnabled &&
            (parkour.ControlledByParkour || player.FocusedScript == parkour))
        {
            Log("Parkour took focus; no roll.");
            yield break;
        }

        if (ParkourHasPriority())
        {
            Log("A vault is still eligible; no roll.");
            yield break;
        }

        if (Time.time < nextAllowedDodgeTime)
            yield break;

        // The physics check found no usable parkour action. A normal dodge
        // still happens with the original single press (no second press).
        TryStartDodge();
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

    // A held movement input, rather than the old velocity, decides whether
    // the player should resume moving. MoveDir also supports controllers
    // whose bindings aren't W/A/S/D.
    private bool WantsToMove()
    {
        if (IsForwardHeld() || IsBackwardHeld() ||
            IsLeftHeld() || IsRightHeld())
            return true;

        return character != null && character.MoveDir.sqrMagnitude > 0.01f;
    }

    private bool IsSprintHeld()
    {
        bool held = IsKeyHeld(KeyCode.LeftShift) || IsKeyHeld(KeyCode.RightShift);
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            held |= Keyboard.current.leftShiftKey.isPressed ||
                    Keyboard.current.rightShiftKey.isPressed;
        }
#endif
        return held || (core != null && core.moveType == MoveType.Sprint);
    }

    private bool HasFloatParameter(string name)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Float &&
                string.Equals(parameters[i].name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private float GetFloatParameter(string name, float fallback)
    {
        if (!HasFloatParameter(name))
            return fallback;

        AnimatorControllerParameter[] parameters = animator.parameters;
        foreach (AnimatorControllerParameter param in parameters)
        {
            if (param.type == AnimatorControllerParameterType.Float &&
                string.Equals(param.name, name, StringComparison.OrdinalIgnoreCase))
                return animator.GetFloat(param.nameHash);
        }
        return fallback;
    }

    private void RestoreFloatParameterIfLow(string name, float value)
    {
        if (Mathf.Abs(value) < 0.001f || animator == null)
            return;

        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.type != AnimatorControllerParameterType.Float ||
                !string.Equals(param.name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            // Do not overwrite a correct value from Fantacode. If its
            // post-action reset left this at zero, or sprint is still held
            // but its speed float is below the sprint threshold, repair it.
            float current = animator.GetFloat(param.nameHash);
            bool speedParameter =
                string.Equals(name, "Speed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "normalizedSpeed", StringComparison.OrdinalIgnoreCase);
            if (Mathf.Abs(current) < 0.05f ||
                (speedParameter && IsSprintHeld() && current < value - 0.05f))
                animator.SetFloat(param.nameHash, value);
            return;
        }
    }

    private void CapturePreviousAnimation()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (next.fullPathHash != 0 && animator.HasState(0, next.fullPathHash))
                state = next;
        }

        previousAnimatorStateHash = state.fullPathHash;
        previousAnimationNormalizedTime = Mathf.Repeat(state.normalizedTime, 1f);
        previousAnimationLength = Mathf.Max(0.001f, state.length);
        previousStateWasIdle = state.IsName("Idle") || state.IsName("idle") ||
                               state.IsName("Base Layer.Idle") ||
                               state.IsName("Base Layer.idle");

        previousMoveAmount = Mathf.Max(1f, GetFloatParameter("moveAmount", 1f));
        previousSpeedAmount = Mathf.Max(1f, GetFloatParameter("normalizedSpeed",
                                         GetFloatParameter("Speed", 1f)));
    }

    private int ResolveResumeAnimatorState()
    {
        if (!string.IsNullOrEmpty(movementStateOverride))
        {
            int state = FindAnimatorState(movementStateOverride);
            if (state != 0)
                return state;

            Log("Movement state override not found: " + movementStateOverride);
        }

        // Prefer the real pre-dodge blend tree or movement state.
        if (!previousStateWasIdle && previousAnimatorStateHash != 0 &&
            animator.HasState(0, previousAnimatorStateHash))
            return previousAnimatorStateHash;

        // Some controllers transition to Idle immediately after an action.
        // Find the real locomotion/run state when one exists.
        string[] candidates = IsSprintHeld()
            ? new[] { "Locomotion", "Movement", "FreeMovement", "Sprint", "Run", "Running", "WalkRun" }
            : new[] { "Locomotion", "Movement", "FreeMovement", "Run", "Running", "WalkRun" };
        for (int i = 0; i < candidates.Length; i++)
        {
            int hash = FindAnimatorState(candidates[i]);
            if (hash != 0)
                return hash;
        }

        // An Idle-named blend tree can still handle movement parameters.
        if (previousAnimatorStateHash != 0 && animator.HasState(0, previousAnimatorStateHash))
            return previousAnimatorStateHash;

        return 0;
    }

    // An interrupted or completed roll can remain on its last animation
    // frame if no transition to an idle/locomotion state is requested.
    private bool IsDodgeAnimatorState(AnimatorStateInfo state)
    {
        int hash = state.shortNameHash;
        return hash == Animator.StringToHash(forwardRoll) ||
               hash == Animator.StringToHash(backwardRoll) ||
               hash == Animator.StringToHash(leftRoll) ||
               hash == Animator.StringToHash(rightRoll);
    }

    private int FindAnimatorState(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return 0;

        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            return hash;

        // Some controller layouts require the full Base Layer path.
        if (!stateName.StartsWith("Base Layer.", StringComparison.Ordinal))
        {
            hash = Animator.StringToHash("Base Layer." + stateName);
            if (animator.HasState(0, hash))
                return hash;
        }

        return 0;
    }

    private int ResolveIdleAnimatorState()
    {
        int preferred = FindAnimatorState(idleStateOverride);
        if (preferred != 0)
            return preferred;

        string[] candidates = { "Idle", "idle", "Locomotion", "Movement", "FreeMovement" };
        for (int i = 0; i < candidates.Length; i++)
        {
            int candidate = FindAnimatorState(candidates[i]);
            if (candidate != 0)
                return candidate;
        }

        if (previousStateWasIdle && previousAnimatorStateHash != 0 &&
            animator.HasState(0, previousAnimatorStateHash))
            return previousAnimatorStateHash;

        return 0;
    }

    // =====================================================
    // MOMENTUM SAVE / RECOVERY
    // =====================================================

    private static Vector3 Horizontal(Vector3 velocity)
    {
        velocity.y = 0f;
        return velocity;
    }

    private void CapturePreDodgeMomentum()
    {
        momentumBeforeDodge = Horizontal(core.CurrentVelocity);

        // CharacterController.velocity is a useful fallback on controllers
        // where CurrentVelocity was not updated yet on the input frame.
        if (unityCharacterController != null)
        {
            Vector3 controllerVelocity = Horizontal(unityCharacterController.velocity);
            if (controllerVelocity.sqrMagnitude > momentumBeforeDodge.sqrMagnitude)
                momentumBeforeDodge = controllerVelocity;
        }

        momentumWasMoving = momentumBeforeDodge.magnitude >= minimumMomentumSpeed;
        if (momentumWasMoving)
            Log("Saved pre-dodge horizontal speed: " + momentumBeforeDodge.magnitude.ToString("F2"));
    }

    // Some Fantacode releases expose CurrentVelocity as a read-only property
    // backed by an internal field. We use reflection ONLY as an optional
    // recovery when ActionExecutor has cleared that field. This avoids
    // depending on an unverified public setter or changing FS package files.
    private bool TryRestoreCoreVelocity(Vector3 desired)
    {
        if (core == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance |
                                   BindingFlags.Public |
                                   BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly;
        Type type = core.GetType();

        while (type != null)
        {
            try
            {
                PropertyInfo property = type.GetProperty("CurrentVelocity", flags);
                if (property != null && property.PropertyType == typeof(Vector3))
                {
                    MethodInfo setter = property.GetSetMethod(true);
                    if (setter != null)
                    {
                        setter.Invoke(core, new object[] { desired });
                        return true;
                    }
                }

                string[] fieldNames = {
                    "currentVelocity", "_currentVelocity",
                    "m_CurrentVelocity", "<CurrentVelocity>k__BackingField"
                };

                foreach (string name in fieldNames)
                {
                    FieldInfo field = type.GetField(name, flags);
                    if (field != null && field.FieldType == typeof(Vector3) &&
                        !field.IsInitOnly)
                    {
                        field.SetValue(core, desired);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Momentum recovery field not writable: " + ex.Message);
            }
            type = type.BaseType;
        }

        return false;
    }

    private void RecoverMomentumIfNeeded()
    {
        if (!preservePreDodgeMomentum || !momentumWasMoving ||
            !WantsToMove() || core == null)
            return;

        Vector3 current = core.CurrentVelocity;
        Vector3 planarCurrent = Horizontal(current);
        float previousSpeed = momentumBeforeDodge.magnitude;
        if (planarCurrent.magnitude >= previousSpeed * momentumRestorationThreshold)
            return;

        Vector3 direction = momentumBeforeDodge.normalized;
        if (character != null)
        {
            Vector3 desiredDirection = Horizontal(character.MoveDir);
            if (desiredDirection.sqrMagnitude > 0.01f)
                direction = desiredDirection.normalized;
        }

        // Preserve gravity/vertical velocity while restoring horizontal speed.
        Vector3 recovered = direction * previousSpeed + Vector3.up * current.y;
        if (TryRestoreCoreVelocity(recovered))
        {
            Log("Restored running momentum: " + previousSpeed.ToString("F2"));
        }
        else if (!warnedMomentumAccess)
        {
            warnedMomentumAccess = true;
            Debug.LogWarning(
                "[PlayerDodgeRoll] This Fantacode version does not expose a writable " +
                "LocomotionCore velocity field. Removed the explicit ResetCurrentVelocity " +
                "call, but if speed still resets internally, the LocomotionCore movement " +
                "setter/implementation is needed to restore it precisely.", this);
        }
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

        // Save the previous locomotion state BEFORE FocusSystem or the dodge
        // ActionExecutor changes the Animator.
        if (resumeMovementRoutine != null)
        {
            StopCoroutine(resumeMovementRoutine);
            resumeMovementRoutine = null;
        }

        CapturePreviousAnimation();
        CapturePreDodgeMomentum();
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

        // DO NOT call ResetCurrentVelocity/ResetTargetRotation here.
        // That used to erase the acceleration built up while sprinting.
        // ActionExecutor handles the roll's root motion on its own.

        currentDodgeAction = new CharacterAction
        {
            animName = animationName,

            usesRootMotion = true,
            usesRootMovement = useRootMovement,
            usesRootRotation = useRootRotation,

            usesGravity = true,
            // Keep the locomotion blend parameter intact during the roll.
            // Otherwise the executor forces it to zero and the player can
            // remain visually Idle after a roll while Shift+Up is held.
            resetMoveAmount = false,
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

            if (core != null && core.ActionExecutor != null)
                core.ActionExecutor.Cancel(action);

            // The executor was cancelled, so do not leave our wrapper
            // coroutine running and making a late completion callback.
            if (actionRoutine != null)
            {
                StopCoroutine(actionRoutine);
                actionRoutine = null;
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

    private void FinishDodge(bool allowMovementResume = true)
    {
        if (!IsDodging && !ownsFocus)
            return;

        bool wasDodging = IsDodging;
        IsDodging = false;

        currentDodgeAction = null;
        nextAllowedDodgeTime = Time.time + dodgeCooldown;

        // The dodge has ended. Give control back to Fantacode locomotion
        // BEFORE trying to resume running. Do not zero out the movement
        // velocity here: that is what caused the idle snap after a roll.
        if (ownsFocus && locomotion != null && player != null &&
            player.FocusedScript == this)
        {
            locomotion.UnFocusSystem(this);
        }

        ownsFocus = false;
        dodgeRoutine = null;
        actionRoutine = null;

        // Seed speed immediately after releasing focus; the coroutine below
        // also catches an ActionExecutor reset in the next few frames.
        if (allowMovementResume && wasDodging)
            RecoverMomentumIfNeeded();

        if (wasDodging)
            OnDodgeFinished?.Invoke();

        // Let locomotion process one frame of the player's current input.
        // Only help the Animator if it hasn't returned to movement by itself.
        if (allowMovementResume && wasDodging && isActiveAndEnabled)
        {
            resumeMovementRoutine = StartCoroutine(ResumePreviousMovement());
        }

        Log("Dodge complete. Returning control to locomotion.");
    }

    private IEnumerator ResumePreviousMovement()
    {
        // Let ActionExecutor and UnFocusSystem finish their frame first.
        yield return null;

        // Do NOT require held movement to exit the roll. Releasing the keys
        // used to leave the Animator frozen in the final dodge pose.
        float endTime = Time.time + Mathf.Max(0.25f, rollPoseRecoverySeconds);
        float nextRecoveryAttempt = Time.time;
        int frame = 0;
        bool warnedIdleState = false;

        while (Time.time <= endTime)
        {
            if (IsDodging || player == null || player.IsDead ||
                locomotion == null || core == null || animator == null ||
                !animator.isActiveAndEnabled)
                break;

            // A vault, climb, or another action has taken over. Never force
            // our animation recovery on top of somebody else's action.
            if (player.FocusedScript != null && player.FocusedScript != this)
                break;

            if (locomotion.PreventAllSystems)
            {
                yield return null;
                frame++;
                continue;
            }

            bool moving = WantsToMove();
            if (moving && frame < momentumRecoveryFrames)
                RecoverMomentumIfNeeded();

            if (moving && restoreMovementAfterDodge)
            {
                bool sprinting = IsSprintHeld();
                RestoreFloatParameterIfLow("moveAmount", previousMoveAmount);
                RestoreFloatParameterIfLow("normalizedSpeed", sprinting
                    ? Mathf.Max(1.5f, previousSpeedAmount) : previousSpeedAmount);
                RestoreFloatParameterIfLow("Speed", sprinting
                    ? Mathf.Max(1.5f, previousSpeedAmount) : previousSpeedAmount);

                if (IsForwardHeld()) RestoreFloatParameterIfLow("vertical", 1f);
                if (IsBackwardHeld()) RestoreFloatParameterIfLow("vertical", -1f);
                if (IsRightHeld()) RestoreFloatParameterIfLow("horizontal", 1f);
                if (IsLeftHeld()) RestoreFloatParameterIfLow("horizontal", -1f);
            }
            else if (!moving && HasFloatParameter("moveAmount"))
            {
                // An action can preserve a non-zero blend value despite the
                // player releasing input. Idle should remain genuinely idle.
                animator.SetFloat("moveAmount", 0f);
            }

            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            bool transitioning = animator.IsInTransition(0);
            AnimatorStateInfo next = transitioning
                ? animator.GetNextAnimatorStateInfo(0) : current;
            bool stillInRoll = IsDodgeAnimatorState(current) ||
                (transitioning && IsDodgeAnimatorState(next));

            int targetHash = moving
                ? ResolveResumeAnimatorState()
                : ResolveIdleAnimatorState();

            if (targetHash == 0)
            {
                if (!moving && !warnedIdleState)
                {
                    warnedIdleState = true;
                    Debug.LogWarning(
                        "[PlayerDodgeRoll] No Idle state found on Animator layer 0. " +
                        "Set Idle State Override to your exact idle/locomotion state.", this);
                }
                if (moving && !warnedNoMovementState)
                {
                    warnedNoMovementState = true;
                    Debug.LogWarning(
                        "[PlayerDodgeRoll] No movement state found on Animator layer 0. " +
                        "Set Movement State Override to your locomotion state.", this);
                }
            }

            bool targetIsCurrent = targetHash != 0 &&
                (current.fullPathHash == targetHash || current.shortNameHash == targetHash);
            bool targetIsNext = targetHash != 0 && transitioning &&
                (next.fullPathHash == targetHash || next.shortNameHash == targetHash);

            // If the executor left Animator speed at 0, no crossfade will
            // advance even if the correct target is selected.
            if (animator.speed <= 0.001f && Time.timeScale > 0f)
            {
                animator.speed = 1f;
                Log("Recovered Animator speed from a frozen roll.");
            }

            // Only force an animation change if we are actually stranded in
            // a roll (or in Idle while movement is held). Otherwise let
            // Fantacode's normal locomotion transitions work undisturbed.
            bool strandedInIdleWhileMoving = moving && !transitioning &&
                (current.IsName("Idle") || current.IsName("idle") ||
                 current.IsName("Base Layer.Idle") || current.IsName("Base Layer.idle"));
            // Leaving the roll pose is mandatory, even if optional movement
            // restoration is disabled in the Inspector.
            bool requiresRecovery = (stillInRoll ||
                (restoreMovementAfterDodge && strandedInIdleWhileMoving)) &&
                !targetIsCurrent && !targetIsNext;

            if (targetHash != 0 && requiresRecovery &&
                Time.time >= nextRecoveryAttempt)
            {
                // No time offset here: fixed-time offsets based on an old
                // state can sometimes land in the wrong frame of a blend tree.
                animator.CrossFadeInFixedTime(targetHash,
                    movementResumeBlend, 0, 0f);
                nextRecoveryAttempt = Time.time + Mathf.Max(0.05f, rollPoseRetryInterval);
                Log(moving ? "Leaving roll pose for locomotion." : "Leaving roll pose for Idle.");
            }

            // Keep checking briefly in case the ActionExecutor overwrites the
            // transition one last time, but exit once locomotion owns the state.
            if (!stillInRoll && !strandedInIdleWhileMoving && !transitioning &&
                frame >= 2)
                break;

            frame++;
            yield return null;
        }

        resumeMovementRoutine = null;
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
        lastSpacePressTime = float.NegativeInfinity;
        if (pendingParkourDecision != null)
        {
            StopCoroutine(pendingParkourDecision);
            pendingParkourDecision = null;
        }

        if (resumeMovementRoutine != null)
        {
            StopCoroutine(resumeMovementRoutine);
            resumeMovementRoutine = null;
        }

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

        FinishDodge(false);
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
