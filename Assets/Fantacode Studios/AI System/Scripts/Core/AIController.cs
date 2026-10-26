using FS_LocomotionSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FS_Core
{
    [RequireComponent(typeof(TargetHandler))]
    [RequireComponent(typeof(Damagable))]
    public class AIController : MonoBehaviour, ITargetable
    {
        [SerializeField] StateMachineAsset stateMachineAsset;
        public StateMachineAsset StateMachineAsset => stateMachineAsset;

        [SerializeField] bool showStateChain = true;

        HierarchicalState<AIController> behaviorState;

        [field: Tooltip("Movement speed when the AI is walking (non-combat).")]
        [field: SerializeField] public float WalkSpeed { get; private set; } = 2f;

        [field: Tooltip("Movement speed when the AI is running (non-combat).")]
        [field: SerializeField] public float RunSpeed { get; private set; } = 4.5f;

        object focusedSystem;
    

        public List<Transform> TargetsInVision { get; private set; } = new();

        public Blackboard Blackboard { get; private set; } = new Blackboard();

        public TargetHandler TargetHandler { get; private set; }

        public Transform Target => TargetHandler.Target;

        public StateMachine<AIController> StateMachine { get; private set; }

        public NavMeshAgent NavAgent { get; private set; }
        public CharacterController CharacterController { get; private set; }
        public Animator Animator { get; private set; }
        public AudioSource AudioSource { get; private set; }
        public AnimGraph AnimGraph { get; private set; }
        public Damagable Damagable { get; private set; }
        public ActionExecutor ActionExecutor { get; private set; }
        LocomotionCore locomotionCore;
        public LocomotionCore LocomotionCore => locomotionCore;
        IMotionDriver motionDriver;
        ItemEquipper itemEquipper;   // may be null for unarmed AI
        ActiveRagdollSystem activeRagdoll;   // may be null for AI without a ragdoll setup

        // When set, overrides the NavAgent-derived facing direction passed to HandleLocomotion
        public Vector3? FacingDirOverride { get; set; }

        // Last position the target was seen at — delegates to AIManager shared state
        public Vector3 LastSeenTargetPos
        {
            get => AIManager.Instance.LastKnownTargetPos;
            set => AIManager.Instance.LastKnownTargetPos = value;
        }

        // only required for player characters right now
        public bool IsUnreachableByGround => false;

        // Counted, not a flag. Several systems suspend this AI for overlapping stretches — an
        // interaction, a melee pull-out, a vehicle seat — and with a plain bool whichever finished
        // FIRST handed control back to everyone, including the holder still using it.
        int stateMachinePauseCount;
        public bool IsStateMachinePaused => stateMachinePauseCount > 0;

        // The system whose focus is holding a state-machine pause, so UnFocusSystem only resumes
        // what its own focus took. Tracked per-owner rather than as a flag because focus is
        // re-taken repeatedly within a single session (see FocusSystem).
        object focusPauseOwner;

        private void Awake()
        {
            Animator = GetComponent<Animator>();
            AudioSource = GetComponent<AudioSource>();
            AnimGraph = GetComponent<AnimGraph>();
            NavAgent = GetComponent<NavMeshAgent>();
            CharacterController = GetComponent<CharacterController>();
            Damagable = GetComponent<Damagable>();
            ActionExecutor = GetComponent<ActionExecutor>();
            locomotionCore = GetComponent<LocomotionCore>();
            motionDriver = GetComponent<IMotionDriver>();
            TargetHandler = GetComponent<TargetHandler>();
            itemEquipper = GetComponent<ItemEquipper>();
            activeRagdoll = GetComponent<ActiveRagdollSystem>();
        }

        private void Start()
        {
            Damagable.OnDead += OnDead;

            Blackboard.Set(AIBlackboardKeys.IsDead, false);
            Blackboard.Set(AIBlackboardKeys.Health, Damagable.CurrentHealth);



            Blackboard.Set(AIBlackboardKeys.IsRagdolling, false);
            if (activeRagdoll != null)
            {
                activeRagdoll.OnRagdollStart += OnRagdollStart;
                activeRagdoll.OnSettled += OnRagdollSettled;
            }

            if (Target != null)
                Blackboard.Set(AIBlackboardKeys.TargetPosition, Target.position);

            if (TargetHandler.FollowTarget != null)
                Blackboard.Set(AIBlackboardKeys.FollowTarget, TargetHandler.FollowTarget);

            StateMachine = stateMachineAsset.Build(this);
            StateMachine.EnterInitialState();

            // we want to update the rotation manually for finer control
            NavAgent.updateRotation = false;

            Damagable.OnHealthUpdated += () => Blackboard.Set(AIBlackboardKeys.Health, Damagable.CurrentHealth);
        }

        private void OnDead()
        {
            if (NavAgent.isActiveAndEnabled)
            {
                NavAgent.ResetPath();
                NavAgent.isStopped = true;
            }
            Blackboard.Set(AIBlackboardKeys.IsDead, true);
        }

        // Ragdoll begins → flag it on the blackboard so a dedicated ragdoll state can take over
        // (and combat states can stop aiming/shooting). Cleared once the ragdoll settles.
        private void OnRagdollStart() 
        { 
            if (!Damagable.IsDead)
                Blackboard.Set(AIBlackboardKeys.IsRagdolling, true);
        }

        private void OnRagdollSettled() => Blackboard.Set(AIBlackboardKeys.IsRagdolling, false);

        private void OnDestroy()
        {
            if (activeRagdoll != null)
            {
                activeRagdoll.OnRagdollStart -= OnRagdollStart;
                activeRagdoll.OnSettled -= OnRagdollSettled;
            }
        }

        private void Update()
        {
            if (Time.deltaTime == 0) return;

            Blackboard.Set<bool>(AIBlackboardKeys.IsTargetSet, Target != null);
            if (Target != null)
            {
                bool targetLost = Blackboard.TryGet<bool>(AIBlackboardKeys.HasVisualOnTarget, out bool hasVisual) && !hasVisual;
                Blackboard.Set(AIBlackboardKeys.TargetPosition, targetLost ? LastSeenTargetPos : Target.position);
            }

            // While the state machine is paused, another system has taken exclusive control of this AI
            // (e.g. an Interaction driving it through Move/Act nodes, or a focused system). Bail out of
            // BOTH the state machine AND the NavAgent-driven locomotion below — otherwise this would
            // overwrite the controller's HandleLocomotion / animation every frame and the AI would
            // never move or act.
            if (IsStateMachinePaused) return;

            StateMachine.Execute();

            if (Damagable.CurrentHealth <= 0) return;

            // Skip locomotion while a hit reaction (or other action) is playing 
            // Keep targetRotation tracking transform.rotation so locomotion doesn't snap back to a stale facing once the action ends.
            if (ActionExecutor.IsBusy)
            {
                locomotionCore.ResetTargetRotation();
                return;
            }

            // The body is being moved by something other than this agent — a vehicle seat the
            // character is parented into, a ragdoll, anything that switches the agent off. Only the
            // steering stops: the state machine above has already run, so an AI riding in a car still
            // thinks, still evaluates its transitions, and can still die or be pulled into combat.
            //
            // Reading desiredVelocity off a disabled agent is also an error in its own right — Unity
            // logs one per frame for it.
            if (!NavAgent.enabled || !NavAgent.isOnNavMesh) return;

            var desiredMoveDir = NavAgent.desiredVelocity.normalized;
            var desiredFacingDir = FacingDirOverride ?? desiredMoveDir;
            desiredFacingDir.y = 0;
            var moveAmount = NavAgent.speed > 0 ? NavAgent.desiredVelocity.magnitude / NavAgent.speed : 0f;

            locomotionCore.HandleLocomotion(desiredMoveDir, moveAmount, desiredFacingDir,
                false, false);
        }

        public void FocusSystem(object system, bool blockStateMachine=false)
        {
            // Focus is single-owner, so a system taking it over revokes the previous owner's —
            // and with it any pause that owner was holding. Released here rather than left for
            // the old owner's UnFocusSystem, which no-ops now that it no longer holds focus.
            if (focusPauseOwner != null && focusPauseOwner != system)
                ReleaseFocusPause();

            focusedSystem = system;

            // Freeze the state machine in place rather than transitioning to a holding state,
            // so the active state resumes where it left off instead of re-entering on unfocus.
            //
            // At most ONE hold per focus session: a chained action re-focuses once per link in
            // the chain (a climb into a back jump into a shimmy) but only unfocuses once, at the
            // end of the whole chain. Pausing per call would leave counted holds outstanding that
            // nothing ever releases, and the AI would stay frozen after the action finished.
            if (blockStateMachine && focusPauseOwner == null)
            {
                focusPauseOwner = system;
                PauseStateMachine();
            }
        }

        public void UnFocusSystem(object system)
        {
            if (focusedSystem != system) return;

            focusedSystem = null;
            if (focusPauseOwner == system)
                ReleaseFocusPause();
        }

        void ReleaseFocusPause()
        {
            focusPauseOwner = null;
            ResumeStateMachine();
        }

        /// <summary>True when <paramref name="system"/> currently holds focus. Lets a running action
        /// detect that another system has taken control (e.g. swimming) and bail out.</summary>
        public bool IsSystemFocused(object system) => focusedSystem == system;

        #region Free hands for actions

        // Weapon temporarily unequipped for a hands-requiring action (climb, vault, interaction, …),
        // remembered AI can re-equip it once the action ends.
        EquippableItem itemToRestore;
        bool handsFreed;

        /// <summary>
        /// True while a <see cref="FreeHands"/> unequip is still settling. Action systems should defer
        /// starting their animation until this is false (mirrors the player's
        /// LocomotionController.WaitToStartSystem). Always false for unarmed AI.
        /// </summary>
        public bool WaitToStartSystem { get; private set; }

        /// <summary>
        /// Holster the currently held weapon for a hands-requiring action. Items flagged
        /// <c>unEquipDuringActions</c> are fully unequipped; others just have their idle animation
        /// stopped. Safe to call when unarmed (no-op) and idempotent within an action — pair with
        /// <see cref="RestoreHands"/> on completion. ShooterFighter's own ItemEquipper event hooks
        /// handle weapon-specific cleanup (IK/aim helpers), so no Shooter dependency is needed here.
        /// </summary>
        public void FreeHands()
        {
            if (handsFreed || itemEquipper == null) return;
            handsFreed = true;
            StartCoroutine(FreeHandsRoutine());
        }

        IEnumerator FreeHandsRoutine()
        {
            WaitToStartSystem = true;
            var equippedItem = itemEquipper.EquippedItem;
            itemEquipper.PreventItemSwitching = true;

            if (equippedItem == null && !itemEquipper.IsChangingItem)
            {
                itemEquipper.InterruptItemSwitching = false;
                WaitToStartSystem = false;
                yield break;
            }

            // If an item switch is mid-flight, let it settle before we unequip the result.
            if (itemEquipper.IsChangingItem)
            {
                itemEquipper.InterruptItemSwitching = true;
                while (itemEquipper.IsChangingItem)
                    yield return null;
                equippedItem = itemEquipper.EquippedItem;
            }

            // Remember the weapon so combat AI can pull it back out once the action completes.
            itemToRestore = equippedItem;

            if (equippedItem != null && equippedItem.unEquipDuringActions)
            {
                itemEquipper.PreventItemSwitching = false;
                itemEquipper.UnEquipItem(false);
                itemEquipper.PreventItemSwitching = true;
            }
            else if (equippedItem != null)
                itemEquipper.StopIdleAnimation();

            itemEquipper.InterruptItemSwitching = false;
            WaitToStartSystem = false;
        }

        /// <summary>
        /// Counterpart to <see cref="FreeHands"/>, called when the action ends. A weapon that was only
        /// idle-stopped resumes its idle animation; a fully-unequipped (holstered) weapon is re-equipped
        /// only when the AI is in combat (has a target) — idle/patrolling AI leaves it holstered.
        /// No-op if hands were never freed.
        /// </summary>
        public void RestoreHands()
        {
            if (!handsFreed) return;
            handsFreed = false;
            if (itemEquipper == null) return;

            itemEquipper.PreventItemSwitching = false;

            var item = itemToRestore;
            itemToRestore = null;

            if (item != null && item.unEquipDuringActions)
            {
                if (Target != null)
                    itemEquipper.EquipItem(item, false);
            }
            else
            {
                // Item was only idle-stopped, not removed — resume its idle animation.
                itemEquipper.ResumeIdleAnimation();
            }
        }

        #endregion

        #region Off-mesh-link jump-down

        [Tooltip("A link is treated as a simple drop when its horizontal distance is below this (m).")]
        float dropXZThreshold = 2f;

        [Tooltip("A link is treated as a downward drop when its vertical delta is below this (m). Negative = downward.")]
        float dropYThreshold = -0.2f;

        [Tooltip("Degrees per second used to align the character toward the jump-down direction.")]
        [SerializeField] float alignTurnSpeed = 720f;

        [Tooltip("Failsafe (seconds): the alignment step bails out after this long even if it has not converged, so a fighting rotation source can never leave the AI stuck.")]
        [SerializeField] float alignTimeout = 1.5f;

        bool isInJumpDown;
        /// <summary>True while a jump-down is in progress. Used to ignore overlapping requests.</summary>
        public bool IsInJumpDown => isInJumpDown;

        /// <summary>Inspection result for the agent's current off-mesh link, viewed as a potential drop-down.</summary>
        public struct JumpDownInfo
        {
            public bool isDropDownLink;   // link type is DropDown or JumpAcross
            public Vector3 startPos;      // NavMesh-snapped link start
            public Vector3 landPos;       // NavMesh-snapped link end
            public Vector3 deltaXZ;       // horizontal vector from current position to landPos
            public float deltaY;          // signed vertical drop (negative = downward)
            public bool isSimpleDrop;     // close horizontally and clearly downward
        }

        /// <summary>Pure inspection of the current off-mesh link. No side effects — safe every frame.</summary>
        public JumpDownInfo InspectCurrentLink()
        {
            var info = new JumpDownInfo();
            var a = NavAgent;
            if (a == null || !a.isOnOffMeshLink)
                return info;

            var link = a.currentOffMeshLinkData;
            info.isDropDownLink = link.linkType == OffMeshLinkType.LinkTypeDropDown
                               || link.linkType == OffMeshLinkType.LinkTypeJumpAcross;

            // Snap both ends to the NavMesh; keep the raw point if sampling fails (preserves prior behaviour).
            Vector3 startPos = link.startPos;
            if (NavMesh.SamplePosition(startPos, out var startHit, 1f, NavMesh.AllAreas))
                startPos = startHit.position;

            Vector3 landPos = link.endPos;
            if (NavMesh.SamplePosition(landPos, out var endHit, 1f, NavMesh.AllAreas))
                landPos = endHit.position;

            info.startPos = startPos;
            info.landPos = landPos;

            var delta = landPos - transform.position;
            info.deltaY = delta.y;
            delta.y = 0f;
            info.deltaXZ = delta;
            info.isSimpleDrop = delta.magnitude < dropXZThreshold && info.deltaY < dropYThreshold;

            return info;
        }

        /// <summary>
        /// The single, shared jump-down sequence used by every AI system: take the guard, align
        /// rotation toward the jump-down direction, play the fall (anim + gravity + descent via
        /// <see cref="LocomotionCore.HandleFalling"/>), complete the off-mesh link, then release the
        /// guard. <paramref name="onComplete"/> carries the per-system tail (e.g. entering the swim
        /// state). Any call made while <see cref="IsInJumpDown"/> is true is ignored.
        /// </summary>
        public IEnumerator JumpDown(JumpDownInfo info, Action onComplete = null)
        {
            if (isInJumpDown)
                yield break;

            isInJumpDown = true;

            // Block other systems and stop the agent from driving while we own the transform.
            FocusSystem(this, true);
            motionDriver.SetMovementBlocked(true);

            yield return AlignToDirection(info.deltaXZ);

            // Re-snap the landing in case anything shifted during alignment.
            Vector3 landPos = info.landPos;
            if (NavMesh.SamplePosition(landPos, out var hit, 1f, NavMesh.AllAreas))
                landPos = hit.position;

            // Play the jump-down animation, apply gravity, and descend.
            if (locomotionCore != null)
                yield return locomotionCore.HandleFalling("Jump Down", landPos);

            // Finish the link, then release the block — the driver re-attaches the agent
            // (Warp at the landing position) when the count reaches zero.
            var a = NavAgent;
            if (a != null && a.enabled && a.isOnOffMeshLink)
                a.CompleteOffMeshLink();
            motionDriver.SetMovementBlocked(false);
            UnFocusSystem(this);

            onComplete?.Invoke();

            isInJumpDown = false;
        }

        IEnumerator AlignToDirection(Vector3 dirXZ)
        {
            dirXZ.y = 0f;
            if (dirXZ.sqrMagnitude < 0.0001f)
                yield break;

            Quaternion targetRot = Quaternion.LookRotation(dirXZ.normalized);
            // Failsafe timeout: even if some other source keeps rewriting the rotation, never spin
            // here forever — bail after alignTimeout and let the fall proceed.
            float t = 0f;
            while (Quaternion.Angle(transform.rotation, targetRot) > 3 && t < alignTimeout)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, alignTurnSpeed * Time.deltaTime);
                t += Time.deltaTime;
                yield return null;
            }
            transform.rotation = targetRot;
        }

        #endregion

        public void UseRunSpeed()
        {
            NavAgent.speed = RunSpeed;
        }

        public void UseWalkSpeed()
        {
            NavAgent.speed = WalkSpeed;
        }

        public event Action OnStatemachinePaused;
        public event Action OnStatemachineResumed;
        /// <summary>
        /// Suspends the state machine and the locomotion under it. Every call must be matched by a
        /// <see cref="ResumeStateMachine"/> — the holds stack, so one system releasing does not free
        /// an AI another system is still driving.
        /// </summary>
        public void PauseStateMachine()
        {
            stateMachinePauseCount++;

            // Only the first hold is a state change; the rest are already-paused.
            if (stateMachinePauseCount == 1)
                OnStatemachinePaused?.Invoke();
        }

        public void ResumeStateMachine()
        {
            // An unmatched release. Ignored rather than allowed to drive the count negative, which
            // would silently swallow the next real pause.
            if (stateMachinePauseCount == 0) return;

            stateMachinePauseCount--;

            if (stateMachinePauseCount == 0)
                OnStatemachineResumed?.Invoke();
        }

        // Where the agent was headed when it was suspended, so the errand survives. Null when it had
        // nowhere to be. See SuspendNavigation.
        Vector3? pausedDestination;

        /// <summary>
        /// Brings the AI to a genuine standstill — for a system that takes the body over WITHOUT
        /// steering it anywhere itself. Pair with <see cref="ResumeNavigation"/>.
        ///
        /// Pausing the state machine is not enough on its own. Update() stops driving locomotion,
        /// but the NavMeshAgent is not part of that loop: it follows whatever path it holds and
        /// moves the transform by itself, every frame, whether or not anything is animating the
        /// body. An AI suspended mid-errand goes on gliding toward the destination its now-frozen
        /// state machine set — an interaction that only rotates an NPC to face the player leaves
        /// that NPC sliding off toward its last patrol waypoint.
        ///
        /// The path is dropped rather than the agent merely stopped, because isStopped does not
        /// hold: any root-motion action played while suspended (the turn a Rotate Character node
        /// performs, for one) takes a motion-driver block on the way in and releases it on the way
        /// out — and that release clears isStopped, handing the stale path back. Nothing resurrects
        /// a path that is gone.
        ///
        /// The destination is kept instead, and re-issued on release. Losing it would strand the AI:
        /// states re-issue a destination only once they see the current one reached, and an agent
        /// with no path never reports arrival, so a patrol would stop where it stood for good.
        ///
        /// The locomotion blend settles too. Nothing writes those parameters while the state machine
        /// is paused, and the animator holds the last values it was given — so an AI caught
        /// mid-stride keeps playing that walk. Stopping the body without this just trades a slide
        /// for a march on the spot.
        ///
        /// <b>Not for systems that drive the agent themselves.</b> The Parkour AI reads
        /// <c>destination</c> and traverses off-mesh links across a whole climb, and dropping the
        /// path cancels the link traversal out from under it. Those systems block the motion driver
        /// instead, which stops the body while leaving the agent's path and link state intact.
        /// </summary>
        public void SuspendNavigation()
        {
            pausedDestination = null;

            if (NavAgent != null && NavAgent.isActiveAndEnabled && NavAgent.isOnNavMesh)
            {
                if (NavAgent.hasPath || NavAgent.pathPending)
                    pausedDestination = NavAgent.destination;

                NavAgent.ResetPath();

                // Dropping the path stops the steering; whatever the agent had already built up
                // still carries the body a little way past the pause without this.
                NavAgent.velocity = Vector3.zero;
            }

            // isActiveAndEnabled: the settle runs as a coroutine, and Unity refuses to start one on
            // a component whose object is on its way out (a despawning AI, a pooled one being put
            // back) — which is exactly when a last pause can still land.
            if (locomotionCore != null && locomotionCore.isActiveAndEnabled)
                locomotionCore.ResetAnimationMotionParams();
        }

        /// <summary>
        /// Puts the AI back on the errand <see cref="SuspendNavigation"/> interrupted. The blend
        /// needs nothing here — Update() resumes writing it the moment the state machine ticks
        /// again, and ramps it up from the idle it was left at.
        /// </summary>
        public void ResumeNavigation()
        {
            var destination = pausedDestination;
            pausedDestination = null;

            if (destination == null) return;
            if (NavAgent == null || !NavAgent.isActiveAndEnabled || !NavAgent.isOnNavMesh) return;

            // Something steered this AI somewhere of its own accord while it was suspended — a Move
            // Character node walking it to a mark, say. That course is the current one; the errand
            // the pause interrupted is stale, and re-issuing it here would pull the AI off a
            // destination another system just set.
            if (NavAgent.hasPath || NavAgent.pathPending) return;

            NavAgent.SetDestination(destination.Value);
        }

        public bool IsStateActive<TState>() where TState : State<AIController>
        {
            var activeStates = StateMachine.GetActiveStateChain();
            foreach (var state in activeStates)
            {
                if (state is TState) return true;
            }

            return false;
        }

        public void Speak(DialogueAudio dialogueAudio)
        {
            if (dialogueAudio == null || dialogueAudio.clip == null || AudioSource == null)
                return;

            AudioSource.clip = dialogueAudio.clip;
            AudioSource.Play();
        }

        public void SetTargetsInVision(List<Transform> targetsInVision)
        {
            TargetsInVision = targetsInVision;
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || ActionExecutor == null)
                return;

            var sb = new StringBuilder();

            sb.Append("Action: ");
            sb.AppendLine(ActionExecutor.CurrentClipName ?? "—");

            if (StateMachine != null)
            {
                var chain = StateMachine.GetActiveStateChain().ToList();
                if (chain.Count > 0)
                {
                    if (showStateChain)
                    {
                        bool first = true;
                        foreach (var state in chain)
                        {
                            if (state == null) continue;
                            if (!first) sb.Append(" > ");
                            sb.Append(!String.IsNullOrEmpty(state.Name) ? state.Name : state.GetType().Name);
                            first = false;
                        }
                    }
                    else
                    {
                        var leaf = chain[chain.Count - 1];
                        sb.Append(!String.IsNullOrEmpty(leaf.Name) ? leaf.Name : leaf.GetType().Name);
                    }
                }
            }

            var labelPos = transform.position + Vector3.up * 2.2f;
            var labelText = sb.ToString();
            var shadowStyle = new GUIStyle
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.black }
            };
            var labelStyle = new GUIStyle
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0f, 0.9f, 0f) }
            };

            // Draw shadow offset in screen space
            var cam = Camera.current;
            if (cam != null)
            {
                Vector3 screenPos = cam.WorldToScreenPoint(labelPos);
                Vector3 shadowScreen = screenPos + new Vector3(1, -1, 0);
                Vector3 shadowWorld = cam.ScreenToWorldPoint(shadowScreen);
                Handles.Label(shadowWorld, labelText, shadowStyle);
            }
            Handles.Label(labelPos, labelText, labelStyle);
#endif
        }
    }

    public struct RootMotionData
    {
        public Vector3 deltaPosition;
        public Quaternion deltaRotation;
    }
}
