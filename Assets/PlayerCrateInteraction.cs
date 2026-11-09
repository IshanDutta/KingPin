
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
public class PlayerCrateInteraction : SystemBase
{
    [Header("Detection (Only While Crouching)")]
    public float detectionDistance = 2.5f;
    public float rayOriginHeight = 0.75f;
    public LayerMask crateLayers = ~0;
    public bool drawDebugRay = true;

    [Header("Interaction Input")]
    public KeyCode interactionKey = KeyCode.E;

    [Header("Positioning")]
    [Tooltip("Z controls clearance from the crate.")]
    public Vector3 crateLocalOffset =
        new Vector3(0f, 0f, -0.7f);

    [Min(0f)] public float positionDuration = 0.3f;
    [Min(0f)] public float rotationDuration = 0.25f;
    public bool snapFacingToNearest90 = true;
    public bool preservePlayerHeight = true;

    [Header("Animation")]
    public string openCrateAnimation = "OpenCrate";

    [Range(0f, 0.5f)]
    public float animationTransition = 0.12f;

    [Min(0.1f)]
    public float maximumAnimationDuration = 3f;

    public bool animateAfterAlignment = true;

    [Header("Return To Crouch")]
    public bool returnToCrouch = true;
    public string locomotionAnimation = "Locomotion";

    [Range(0f, 0.5f)]
    public float returnTransition = 0.15f;

    [Header("Crate Parent Opening")]
    [Min(1)] public int openingFrame = 26;
    public float openParentLocalX = 116.96f;

    [Min(0f)]
    public float parentRotationDuration = 0.45f;

    [Min(1f)]
    public float fallbackAnimationFPS = 30f;

    [Header("Automatic Crate Closing")]
    [Min(0f)]
    public float secondsBeforeCrateCloses = 2f;

    [Min(0f)]
    public float crateClosingDuration = 0.45f;

    [Header("Tunnel Crate Interaction (Press E)")]
    public bool enableTunnelCrateInteraction = true;

    [Tooltip("Magnitude of the opening X angle. Direction is selected automatically from the crate parent local Y rotation.")]
    public float tunnelOpenParentLocalX = 116.96f;

    [Min(0.1f)]
    public float tunnelCrateDetectionDistance = 3f;

    public Transform tunnelInteractionView;
    public bool showTunnelCrateDebugLogs = true;

    [Header("Tunnel First-Person Camera")]
    [Range(0, 31)]
    public int tunnelLayer = 22;

    public string tunnelLayerName = "Tunnel";
    public bool returnToPreviousCameraOnExit = true;
    public bool showTunnelDebugLogs = true;
    public CameraController tunnelCameraController;

    public override SystemState State => SystemState.Action;
    public override float Priority => 5f;

    public bool IsInteracting { get; private set; }
    public Transform CurrentCrate { get; private set; }

    public event Action<Transform> OnCrateOpened;

    private PlayerController player;
    private LocomotionController locomotion;
    private LocomotionCore core;
    private Animator animator;

    private Coroutine interactionRoutine;
    private Coroutine crateClosingRoutine;
    private Coroutine tunnelCrateRoutine;
    private Coroutine cameraRefreshRoutine;

    private bool tunnelCrateBusy;
    private bool registered;

    private readonly HashSet<Collider> tunnelOverlaps =
        new HashSet<Collider>();

    private FSCameraType cameraModeBeforeTunnel =
        FSCameraType.ThirdPerson;

    private bool hasSavedCameraMode;

    private static readonly FieldInfo CameraModeField =
        typeof(CameraController).GetField(
            "CameraType",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

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
            animator == null ||
            core.ActionExecutor == null)
        {
            Debug.LogError(
                "[PlayerCrateInteraction] Missing required component.",
                this
            );

            enabled = false;
            return;
        }

        player.Register(this, true);

        if (player.CoreSystems == null)
            player.CoreSystems = new List<SystemBase>();

        if (!player.CoreSystems.Contains(this))
            player.CoreSystems.Add(this);

        player.CoreSystems.Sort(
            (a, b) => b.Priority.CompareTo(a.Priority)
        );

        registered = true;
    }

    private bool IsFirstPersonCameraActive()
    {
        if (TryReadCameraMode(out FSCameraType mode) &&
            mode == FSCameraType.FirstPerson)
            return true;

        return player != null &&
               player.CameraType == FSCameraType.FirstPerson;
    }

    private void Update()
    {
        if (Time.timeScale <= 0f ||
            player == null ||
            locomotion == null)
            return;

        bool tunnelInteraction =
            IsFirstPersonCameraActive() ||
            tunnelOverlaps.Count > 0;

        // FPS interaction has priority.
        // Never position or animate the player here.
        if (tunnelInteraction)
        {
            if (enableTunnelCrateInteraction &&
                !tunnelCrateBusy &&
                !IsInteracting &&
                WasInteractionPressed())
            {
                TryOpenTunnelCrate();
            }

            return;
        }

        if (IsInteracting ||
            !locomotion.IsCrouching ||
            player.IsDead ||
            locomotion.PreventAllSystems)
            return;

        if (player.FocusedScript != null &&
            player.FocusedScript != this)
            return;

        if (!TryFindCrate(out Transform crate))
            return;

        if (WasInteractionPressed())
            StartInteraction(crate);
    }

    private bool WasInteractionPressed()
    {
        bool pressed = false;

#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(interactionKey);
#endif

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null &&
            interactionKey == KeyCode.E)
        {
            pressed |= Keyboard.current.eKey.wasPressedThisFrame;
        }
#endif

        return pressed;
    }

    private bool TryFindCrate(out Transform crate)
    {
        crate = null;

        Vector3 origin =
            transform.position + Vector3.up * rayOriginHeight;

        Vector3 direction = transform.forward;

        bool found = Physics.Raycast(
            origin,
            direction,
            out RaycastHit hit,
            detectionDistance,
            crateLayers,
            QueryTriggerInteraction.Ignore
        );

        if (drawDebugRay)
        {
            Debug.DrawRay(
                origin,
                direction * detectionDistance,
                found ? Color.yellow : Color.cyan
            );
        }

        if (!found ||
            hit.collider.transform.IsChildOf(transform))
            return false;

        crate = hit.collider.transform;
        return true;
    }

    private void StartInteraction(Transform crate)
    {
        if (string.IsNullOrEmpty(openCrateAnimation) ||
            !animator.HasState(
                0,
                Animator.StringToHash(openCrateAnimation)
            ))
        {
            Debug.LogWarning(
                "[PlayerCrateInteraction] Missing animation state: " +
                openCrateAnimation,
                this
            );

            return;
        }

        // Never enter the animated interaction while inside the tunnel or FPS.
        if (tunnelOverlaps.Count > 0 || IsFirstPersonCameraActive())
            return;

        locomotion.FocusSystem(this);

        if (player.FocusedScript != this)
            return;

        IsInteracting = true;
        CurrentCrate = crate;

        interactionRoutine =
            StartCoroutine(PerformInteraction(crate));
    }

    private IEnumerator PerformInteraction(Transform crate)
    {
        if (crate == null || tunnelOverlaps.Count > 0 || IsFirstPersonCameraActive())
        {
            FinishInteraction();
            yield break;
        }

        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;

        // -------------------------------------------
        // SAME-SIDE POSITIONING AND FACING FIX
        // -------------------------------------------

        Collider crateCollider = crate.GetComponent<Collider>();

        Vector3 crateCenter =
            crateCollider != null
                ? crateCollider.bounds.center
                : crate.position;

        // Direction FROM crate TO player.
        Vector3 crateToPlayer =
            startPosition - crateCenter;

        crateToPlayer.y = 0f;

        Vector3 currentForward =
            startRotation * Vector3.forward;

        currentForward.y = 0f;

        if (currentForward.sqrMagnitude > 0.0001f)
            currentForward.Normalize();
        else
            currentForward = Vector3.forward;

        if (crateToPlayer.sqrMagnitude < 0.0001f)
            crateToPlayer = -currentForward;

        crateToPlayer.Normalize();

        // Find which of the four sides the
        // player is currently standing on.
        Vector3[] cardinalDirections =
        {
            Vector3.forward,
            Vector3.right,
            Vector3.back,
            Vector3.left
        };

        Vector3 playerSide = cardinalDirections[0];
        float bestScore = float.NegativeInfinity;

        foreach (Vector3 candidate in cardinalDirections)
        {
            float score =
                Vector3.Dot(candidate, crateToPlayer);

            if (score > bestScore)
            {
                bestScore = score;
                playerSide = candidate;
            }
        }

        // Face INTO the crate, not away from it.
        Vector3 facingDirection = -playerSide;

        Quaternion targetRotation =
            Quaternion.LookRotation(
                facingDirection,
                Vector3.up
            );

        // Keep the player on their original side.
        float clearance = Mathf.Max(
            0.05f,
            Mathf.Abs(crateLocalOffset.z)
        );

        float halfExtent = 0f;

        if (crateCollider != null)
        {
            Bounds bounds = crateCollider.bounds;

            halfExtent =
                Mathf.Abs(playerSide.x) * bounds.extents.x +
                Mathf.Abs(playerSide.z) * bounds.extents.z;
        }

        Vector3 targetPosition =
            crateCenter +
            playerSide * (halfExtent + clearance);

        if (preservePlayerHeight)
        {
            targetPosition.y = startPosition.y;
        }
        else
        {
            targetPosition.y =
                crate.position.y + crateLocalOffset.y;
        }

        // -------------------------------------------
        // PLAYER ALIGNMENT
        // -------------------------------------------

        if (!animateAfterAlignment)
        {
            animator.CrossFadeInFixedTime(
                openCrateAnimation,
                animationTransition,
                0
            );
        }

        float duration = Mathf.Max(
            positionDuration,
            rotationDuration
        );

        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (player.FocusedScript != this ||
                crate == null || tunnelOverlaps.Count > 0 || IsFirstPersonCameraActive())
            {
                FinishInteraction();
                yield break;
            }

            elapsed += Time.deltaTime;

            float positionT =
                positionDuration <= 0f
                    ? 1f
                    : Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            elapsed / positionDuration
                        )
                    );

            float rotationT =
                rotationDuration <= 0f
                    ? 1f
                    : Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            elapsed / rotationDuration
                        )
                    );

            transform.position = Vector3.Lerp(
                startPosition,
                targetPosition,
                positionT
            );

            transform.rotation = Quaternion.Slerp(
                startRotation,
                targetRotation,
                rotationT
            );

            yield return null;
        }

        transform.position = targetPosition;
        transform.rotation = targetRotation;

        // -------------------------------------------
        // OPEN CRATE ANIMATION
        // -------------------------------------------

        if (animateAfterAlignment)
        {
            animator.CrossFadeInFixedTime(
                openCrateAnimation,
                animationTransition,
                0
            );
        }

        float timer = 0f;
        bool entered = false;
        bool openingTriggered = false;
        bool openingComplete = false;
        float openingElapsed = 0f;

        Transform crateParent = crate.parent;

        Quaternion originalClosedRotation =
            crateParent != null
                ? crateParent.localRotation
                : Quaternion.identity;

        if (crateClosingRoutine != null)
        {
            StopCoroutine(crateClosingRoutine);
            crateClosingRoutine = null;
        }

        Quaternion parentStartRotation =
            originalClosedRotation;

        Quaternion parentTargetRotation =
            Quaternion.identity;

        float triggerNormalized =
            GetOpeningFrameTime() /
            GetOpeningClipLength();

        int hash =
            Animator.StringToHash(openCrateAnimation);

        while (timer < maximumAnimationDuration &&
               player.FocusedScript == this)
        {
            AnimatorStateInfo state =
                animator.GetCurrentAnimatorStateInfo(0);

            bool inAnimation =
                state.shortNameHash == hash ||
                state.fullPathHash == hash ||
                state.IsName(openCrateAnimation);

            if (inAnimation)
            {
                entered = true;
            }
            else if (entered &&
                     !animator.IsInTransition(0))
            {
                break;
            }

            // Open at frame 26.
            if (entered &&
                !openingTriggered &&
                state.normalizedTime >= triggerNormalized)
            {
                openingTriggered = true;

                if (crateParent != null)
                {
                    Vector3 euler =
                        crateParent.localEulerAngles;

                    parentStartRotation =
                        crateParent.localRotation;

                    parentTargetRotation =
                        Quaternion.Euler(
                            GetOpeningXForParent(crateParent, false),
                            euler.y,
                            euler.z
                        );
                }
                else
                {
                    Debug.LogWarning(
                        "[PlayerCrateInteraction] Crate has no parent.",
                        this
                    );
                }
            }

            if (openingTriggered &&
                !openingComplete &&
                crateParent != null)
            {
                openingElapsed += Time.deltaTime;

                float t =
                    parentRotationDuration <= 0f
                        ? 1f
                        : Mathf.SmoothStep(
                            0f,
                            1f,
                            Mathf.Clamp01(
                                openingElapsed /
                                parentRotationDuration
                            )
                        );

                crateParent.localRotation =
                    Quaternion.Slerp(
                        parentStartRotation,
                        parentTargetRotation,
                        t
                    );

                openingComplete = t >= 1f;
            }

            if (entered &&
                state.normalizedTime >= 1f &&
                !animator.IsInTransition(0))
            {
                break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        // Finish rotating the crate if necessary.
        while (openingTriggered &&
               !openingComplete &&
               crateParent != null &&
               player.FocusedScript == this)
        {
            openingElapsed += Time.deltaTime;

            float t =
                parentRotationDuration <= 0f
                    ? 1f
                    : Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            openingElapsed /
                            parentRotationDuration
                        )
                    );

            crateParent.localRotation =
                Quaternion.Slerp(
                    parentStartRotation,
                    parentTargetRotation,
                    t
                );

            openingComplete = t >= 1f;

            yield return null;
        }

        // Automatically close after X seconds.
        if (openingTriggered && crateParent != null)
        {
            crateClosingRoutine =
                StartCoroutine(
                    CloseCrateAfterDelay(
                        crateParent,
                        originalClosedRotation
                    )
                );
        }

        if (crate != null)
            OnCrateOpened?.Invoke(crate);

        FinishInteraction(true);
    }

    private IEnumerator CloseCrateAfterDelay(
        Transform crateParent,
        Quaternion originalClosedRotation)
    {
        if (secondsBeforeCrateCloses > 0f)
        {
            yield return new WaitForSeconds(
                secondsBeforeCrateCloses
            );
        }

        if (crateParent == null)
        {
            crateClosingRoutine = null;
            yield break;
        }

        Quaternion from =
            crateParent.localRotation;

        float elapsed = 0f;

        if (crateClosingDuration > 0f)
        {
            while (elapsed < crateClosingDuration &&
                   crateParent != null)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(
                        elapsed / crateClosingDuration
                    )
                );

                crateParent.localRotation =
                    Quaternion.Slerp(
                        from,
                        originalClosedRotation,
                        t
                    );

                yield return null;
            }
        }

        if (crateParent != null)
        {
            crateParent.localRotation =
                originalClosedRotation;
        }

        crateClosingRoutine = null;
    }

    // -------------------------------------------
    // FIRST-PERSON CRATE INTERACTION
    // -------------------------------------------

    private void TryOpenTunnelCrate()
    {
        Transform view = tunnelInteractionView;

        if (view == null &&
            player != null &&
            player.cameraGameObject != null)
        {
            view = player.cameraGameObject.transform;
        }

        if (view == null && Camera.main != null)
            view = Camera.main.transform;

        if (view == null)
        {
            Debug.LogWarning(
                "[PlayerCrateInteraction] No camera found.",
                this
            );

            return;
        }

        Ray ray = new Ray(
            view.position,
            view.forward
        );

        bool found = Physics.Raycast(
            ray,
            out RaycastHit hit,
            tunnelCrateDetectionDistance,
            crateLayers,
            QueryTriggerInteraction.Ignore
        );

        if (drawDebugRay)
        {
            Debug.DrawRay(
                ray.origin,
                ray.direction *
                tunnelCrateDetectionDistance,
                found ? Color.green : Color.red,
                0.3f
            );
        }

        if (!found ||
            hit.collider.transform.IsChildOf(transform))
        {
            if (showTunnelCrateDebugLogs)
            {
                Debug.Log(
                    "[PlayerCrateInteraction] No crate found in front of camera.",
                    this
                );
            }

            return;
        }

        Transform crateParent =
            hit.collider.transform.parent;

        if (crateParent == null)
        {
            Debug.LogWarning(
                "[PlayerCrateInteraction] Crate collider has no parent.",
                this
            );

            return;
        }

        tunnelCrateRoutine =
            StartCoroutine(
                OpenTunnelCrateAndClose(crateParent)
            );
    }

    // Both tunnel and normal interactions use the SAME opening direction.
    // Local Y near 0 degrees: +116.96 X.
    // Local Y near 90 degrees: -116.96 X.
    // For other Y values, choose the nearest of those two orientations.
    private float GetOpeningXForParent(Transform crateParent, bool insideTunnel)
    {
        float y = crateParent.localEulerAngles.y;
        float distanceToZero = Mathf.Abs(Mathf.DeltaAngle(y, 0f));
        float distanceToNinety = Mathf.Abs(Mathf.DeltaAngle(y, 90f));
        float magnitude = Mathf.Abs(tunnelOpenParentLocalX);
        return distanceToNinety < distanceToZero ? magnitude : -magnitude;
    }

    private IEnumerator OpenTunnelCrateAndClose(
        Transform crateParent)
    {
        tunnelCrateBusy = true;

        Quaternion originalRotation =
            crateParent.localRotation;

        Vector3 angles =
            crateParent.localEulerAngles;

        Quaternion openRotation =
            Quaternion.Euler(
                GetOpeningXForParent(crateParent, true),
                angles.y,
                angles.z
            );

        if (showTunnelCrateDebugLogs)
        {
            Debug.Log(
                "[PlayerCrateInteraction] FPS opening: " +
                crateParent.name,
                this
            );
        }

        float elapsed = 0f;
        float duration =
            Mathf.Max(0f, parentRotationDuration);

        while (elapsed < duration &&
               crateParent != null)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / duration)
            );

            crateParent.localRotation =
                Quaternion.Slerp(
                    originalRotation,
                    openRotation,
                    t
                );

            yield return null;
        }

        if (crateParent == null)
        {
            tunnelCrateBusy = false;
            tunnelCrateRoutine = null;
            yield break;
        }

        crateParent.localRotation = openRotation;

        if (secondsBeforeCrateCloses > 0f)
        {
            yield return new WaitForSeconds(
                secondsBeforeCrateCloses
            );
        }

        if (crateParent == null)
        {
            tunnelCrateBusy = false;
            tunnelCrateRoutine = null;
            yield break;
        }

        Quaternion closingStart =
            crateParent.localRotation;

        elapsed = 0f;
        duration =
            Mathf.Max(0f, crateClosingDuration);

        while (elapsed < duration &&
               crateParent != null)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / duration)
            );

            crateParent.localRotation =
                Quaternion.Slerp(
                    closingStart,
                    originalRotation,
                    t
                );

            yield return null;
        }

        if (crateParent != null)
        {
            crateParent.localRotation =
                originalRotation;
        }

        if (showTunnelCrateDebugLogs)
        {
            Debug.Log(
                "[PlayerCrateInteraction] FPS crate closed.",
                this
            );
        }

        tunnelCrateBusy = false;
        tunnelCrateRoutine = null;
    }

    // -------------------------------------------
    // ANIMATION TIMING
    // -------------------------------------------

    private float GetOpeningClipLength()
    {
        if (animator.runtimeAnimatorController != null)
        {
            foreach (AnimationClip clip in
                     animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null &&
                    clip.name == openCrateAnimation)
                {
                    return Mathf.Max(
                        0.01f,
                        clip.length
                    );
                }
            }
        }

        return Mathf.Max(
            0.01f,
            maximumAnimationDuration
        );
    }

    private float GetOpeningFrameTime()
    {
        if (animator.runtimeAnimatorController != null)
        {
            foreach (AnimationClip clip in
                     animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null &&
                    clip.name == openCrateAnimation)
                {
                    return openingFrame /
                           Mathf.Max(
                               1f,
                               clip.frameRate
                           );
                }
            }
        }

        return openingFrame /
               Mathf.Max(1f, fallbackAnimationFPS);
    }

    private void FinishInteraction(
        bool completed = false)
    {
        if (!IsInteracting)
            return;

        IsInteracting = false;
        CurrentCrate = null;
        interactionRoutine = null;

        if (player != null &&
            locomotion != null &&
            player.FocusedScript == this)
        {
            locomotion.UnFocusSystem(this);
        }

        if (completed &&
            returnToCrouch &&
            core != null)
        {
            core.SetCrouching(true);

            if (animator != null &&
                !string.IsNullOrEmpty(locomotionAnimation))
            {
                int hash =
                    Animator.StringToHash(
                        locomotionAnimation
                    );

                if (animator.HasState(0, hash))
                {
                    animator.CrossFadeInFixedTime(
                        locomotionAnimation,
                        returnTransition,
                        0
                    );
                }
            }
        }
    }

    // -------------------------------------------
    // TUNNEL CAMERA
    // -------------------------------------------

    private bool IsTunnel(Collider other)
    {
        if (other == null ||
            !other.isTrigger)
            return false;

        if (other.gameObject.layer == tunnelLayer)
            return true;

        int namedLayer =
            string.IsNullOrEmpty(tunnelLayerName)
                ? -1
                : LayerMask.NameToLayer(
                    tunnelLayerName
                );

        return namedLayer >= 0 &&
               other.gameObject.layer == namedLayer;
    }

    private CameraController FindTunnelCamera()
    {
        if (tunnelCameraController != null)
            return tunnelCameraController;

        if (player != null &&
            player.cameraGameObject != null)
        {
            tunnelCameraController =
                player.cameraGameObject
                    .GetComponent<CameraController>();
        }

        if (tunnelCameraController == null &&
            Camera.main != null)
        {
            tunnelCameraController =
                Camera.main
                    .GetComponent<CameraController>();
        }

        if (tunnelCameraController == null)
        {
            tunnelCameraController =
                FindObjectOfType<CameraController>();
        }

        return tunnelCameraController;
    }

    private bool TryReadCameraMode(
        out FSCameraType mode)
    {
        mode = FSCameraType.ThirdPerson;

        CameraController camera =
            FindTunnelCamera();

        if (camera == null ||
            CameraModeField == null)
            return false;

        mode = (FSCameraType)
            CameraModeField.GetValue(camera);

        return true;
    }

    private void CancelAnimatedInteractionForTunnel()
    {
        if (!IsInteracting)
            return;

        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        FinishInteraction(false);

        // Restore locomotion only if the crate animation was active.
        if (animator != null && !string.IsNullOrEmpty(locomotionAnimation))
        {
            int hash = Animator.StringToHash(locomotionAnimation);
            if (animator.HasState(0, hash))
                animator.CrossFadeInFixedTime(locomotionAnimation, returnTransition, 0);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsTunnel(other) ||
            !tunnelOverlaps.Add(other))
            return;

        if (tunnelOverlaps.Count == 1)
        {
            // An interaction started just before entering the trigger must not
            // keep moving or animating the player once they are inside.
            CancelAnimatedInteractionForTunnel();

            hasSavedCameraMode =
                TryReadCameraMode(
                    out cameraModeBeforeTunnel
                );

            SetTunnelCameraMode(
                FSCameraType.FirstPerson
            );
        }

        if (showTunnelDebugLogs)
        {
            Debug.Log(
                "[PlayerCrateInteraction] Entered tunnel: " +
                other.name,
                this
            );
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsTunnel(other) ||
            tunnelOverlaps.Contains(other))
            return;

        OnTriggerEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!tunnelOverlaps.Remove(other))
            return;

        if (showTunnelDebugLogs)
        {
            Debug.Log(
                "[PlayerCrateInteraction] Exited tunnel: " +
                other.name,
                this
            );
        }

        if (tunnelOverlaps.Count == 0)
        {
            if (returnToPreviousCameraOnExit)
            {
                SetTunnelCameraMode(
                    hasSavedCameraMode
                        ? cameraModeBeforeTunnel
                        : FSCameraType.ThirdPerson
                );
            }

            hasSavedCameraMode = false;
        }
    }

    private void SetTunnelCameraMode(
        FSCameraType mode)
    {
        CameraController camera =
            FindTunnelCamera();

        if (camera == null ||
            CameraModeField == null)
        {
            Debug.LogError(
                "[PlayerCrateInteraction] CameraController or CameraType field not found.",
                this
            );

            return;
        }

        CameraModeField.SetValue(camera, mode);

        if (player != null)
            player.CameraType = mode;

        if (cameraRefreshRoutine != null)
            StopCoroutine(cameraRefreshRoutine);

        cameraRefreshRoutine =
            StartCoroutine(
                RefreshTunnelCameraSettings(camera)
            );

        if (showTunnelDebugLogs)
        {
            Debug.Log(
                "[PlayerCrateInteraction] Camera mode: " +
                mode,
                this
            );
        }
    }

    private IEnumerator RefreshTunnelCameraSettings(
        CameraController camera)
    {
        yield return new WaitForEndOfFrame();

        if (camera != null)
            camera.SetCustomCameraState(null);

        cameraRefreshRoutine = null;
    }

    public override void HandleUpdate() { }

    public override void HandleFixedUpdate() { }

    private void OnDisable()
    {
        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        FinishInteraction();

        if (tunnelCrateRoutine != null)
        {
            StopCoroutine(tunnelCrateRoutine);
            tunnelCrateRoutine = null;
        }

        tunnelCrateBusy = false;

        if (crateClosingRoutine != null)
        {
            StopCoroutine(crateClosingRoutine);
            crateClosingRoutine = null;
        }

        if (cameraRefreshRoutine != null)
        {
            StopCoroutine(cameraRefreshRoutine);
            cameraRefreshRoutine = null;
        }

        if (tunnelOverlaps.Count > 0 &&
            returnToPreviousCameraOnExit &&
            hasSavedCameraMode)
        {
            CameraController camera =
                FindTunnelCamera();

            if (camera != null &&
                CameraModeField != null)
            {
                CameraModeField.SetValue(
                    camera,
                    cameraModeBeforeTunnel
                );
            }
        }

        tunnelOverlaps.Clear();
        hasSavedCameraMode = false;
    }

    private void OnDestroy()
    {
        if (player == null)
            return;

        if (player.CoreSystems != null)
            player.CoreSystems.Remove(this);

        if (registered)
            player.Unregister(this);
    }
}
