using FS_Core;
using FS_LocomotionSystem;
using FS_Util;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_InteractionSystem
{
    public enum CarrySocketType
    {
        RightHand,
        RightArm,
        LeftHand,
        LeftArm
    }

    [System.Serializable]
    public struct Socket
    {
        public CarrySocketType type;
        public Transform transform;
    }

    /// <summary>
    /// Manages picking up, holding, and dropping <see cref="Carryable"/> objects.
    /// Requires <see cref="ActionExecutor"/>, <see cref="AnimGraph"/>, and <see cref="LocomotionCore"/>
    /// on the same GameObject.
    /// </summary>
    public class CarryHandler : MonoBehaviour, IFinisherBlocker
    {
        [SerializeField] Socket[] sockets;

        Carryable carriedObject;
        AnimGraph animGraph;
        ActionExecutor actionExecutor;
        ItemEquipper itemEquipper;
        LocomotionCore locomotionCore;
        InteractionExecutor interactionExecutor;
        InteractionInputManager interactionInputManager;
        Dictionary<CarrySocketType, Transform> socketMap;
        Coroutine _playCarryIdleCoroutine;

        /// <summary>
        /// The carried object's own Animator, captured when it was switched off. Held rather than
        /// re-queried so the object gets back the exact component that was disabled, and so no
        /// release path pays for a GetComponent.
        /// </summary>
        Animator carriedObjectAnimator;

        /// <summary>
        /// True from the moment a drop or throw begins until the object has actually left. The two
        /// halves of the choreography (the carrier's clip and the object's own) do not end together,
        /// so "the carrier's animation finished" is not the end of the carry — without this, the gap
        /// between them accepted a second drop on an object already on its way out.
        /// </summary>
        bool isReleasing;

        /// <summary>The object's own executor for the drop in flight, when it plays a half of its own.</summary>
        ActionExecutor releasingObjectExecutor;

        /// <summary>True while either half of the drop choreography is still playing.</summary>
        bool IsDropAnimating() =>
            actionExecutor.IsBusy || (releasingObjectExecutor != null && releasingObjectExecutor.IsBusy);

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>True while an object is being carried.</summary>
        public bool IsCarrying => carriedObject != null;

        /// <summary>The object currently being carried, or null.</summary>
        public Carryable CarriedObject => carriedObject;

        /// <summary>
        /// No finishers with your hands full — an assassination can't play while a body is on
        /// the shoulder. Polled by the finisher executor, so the prompt hides as well.
        /// </summary>
        public bool BlockFinisher => IsCarrying;

        /// <summary>Returns the attach transform for the given socket type, or null if not configured.</summary>
        public Transform GetSocket(CarrySocketType type)
        {
            if (socketMap != null)
                return socketMap.TryGetValue(type, out var t) ? t : null;

            // Edit-mode path (socketMap is built in Awake): scan the serialized array directly.
            if (sockets != null)
                foreach (var socket in sockets)
                    if (socket.type == type) return socket.transform;
            return null;
        }

        // ── Unity Messages ─────────────────────────────────────────────────────────

        private void Awake()
        {
            actionExecutor = GetComponent<ActionExecutor>();
            animGraph = GetComponent<AnimGraph>();
            locomotionCore = GetComponent<LocomotionCore>();
            itemEquipper = GetComponent<ItemEquipper>();
            interactionExecutor = GetComponent<InteractionExecutor>();
            interactionInputManager = GetComponent<InteractionInputManager>();

            socketMap = new Dictionary<CarrySocketType, Transform>();
            foreach (var socket in sockets)
                socketMap[socket.type] = socket.transform;
        }

        private void OnEnable()
        {
            if (interactionExecutor != null)
                interactionExecutor.OnInteractionCancelled += CancelCarry;
        }

        private void OnDisable()
        {
            if (interactionExecutor != null)
                interactionExecutor.OnInteractionCancelled -= CancelCarry;

            // Last line of defence. A carrier torn down mid-carry (destroyed, pooled, scene
            // unloaded) never reaches a release path, and the object it was holding would keep an
            // Animator that nothing is left alive to switch back on.
            RestoreObjectAnimator();
        }

        private void Update()
        {
            if (carriedObject == null) return;

            // A drop in flight blocks a second one for the whole of it, the gap between the carrier's
            // clip and the object's own included. It clears itself once nothing is animating any more
            // and the object is STILL in hand: that drop was interrupted before it could hand anything
            // over, and the character has to be able to try again rather than be stuck holding it.
            if (isReleasing && !IsDropAnimating())
                isReleasing = false;

            if (isReleasing || actionExecutor.IsBusy) return;

            // Drop / throw only once the pickup has fully SETTLED. The action executor going idle is
            // not that moment: the pickup animation finishes while CarryHotspot is still easing the
            // object from its attach pose onto the carry socket (SmoothCarryToSocket, the tail of the
            // hotspot's Interact routine). Releasing during that ease throws the object from a
            // half-blended pose, and the ease keeps writing localPosition/localRotation on an object
            // that is no longer in hand.
            //
            // InteractionExecutor.IsBusy stays true for the whole pickup routine, the ease included,
            // and every end path clears it — completion, Cancel, Stop — so this cannot latch and
            // leave the player unable to put the object down.
            if (interactionExecutor != null && interactionExecutor.IsBusy) return;

            if (carriedObject != null)
            {
                if (interactionInputManager.DropButtonDown())
                {
                    StartCoroutine(Drop(carriedObject.dropClip, carriedObject.objectDropClip, fallTime: carriedObject.dropStartNormalizedTime));
                }
                else if (carriedObject.isThrowable && interactionInputManager.ThrowButtonDown())
                {
                    StartCoroutine(Drop(carriedObject.throwClip, fallTime: carriedObject.throwStartNormalizedTime, forceDir: transform.forward));
                }
            }
        }
        // ── Carry / Drop ───────────────────────────────────────────────────────────

        /// <summary>
        /// Attaches <paramref name="objectToCarry"/> to its configured socket on this character
        /// and reduces movement speed to a carry pace.
        /// </summary>
        bool _defaultPreventVerticalJump;
        MoveType _defaultMoveType;

        /// <summary>
        /// Sets (or clears) the object this character is carrying, and owns BOTH ends of that
        /// object's own Animator: off while it is carried — it is posed by the carrier's animation,
        /// and its own controller fighting that is what put bodies into their idle pose in someone's
        /// arms — and back on the moment it is let go.
        ///
        /// The re-enable belongs here rather than in each release path. Every way a carry can end
        /// passes through this setter, whereas the paths that each did it themselves had already
        /// missed one: the carrier's drop clip completing before the object's own did cleared the
        /// carried object from here, so the release that the object's clip then asked for found
        /// nothing to release and quietly bailed — animator off, rigidbody kinematic, carrier still
        /// walk-locked. Idempotent, so the pickup calling it twice (once to stage the object, once
        /// from <see cref="Carry"/>) costs nothing.
        /// </summary>
        public void SetCarryObject(Carryable objectToCarry) 
        {
            if (carriedObject != objectToCarry)
                RestoreObjectAnimator();

            carriedObject = objectToCarry;
            isReleasing = false;
            releasingObjectExecutor = null;

            if (carriedObject == null) return;

            carriedObjectAnimator = carriedObject.GetComponent<Animator>();
            if (carriedObjectAnimator != null)
                carriedObjectAnimator.enabled = false;
        }

        /// <summary>
        /// Hands the carried object its own Animator back and forgets it. Safe to call at any time
        /// and however often — nothing happens unless an Animator is actually being held off.
        /// </summary>
        void RestoreObjectAnimator()
        {
            if (carriedObjectAnimator == null) return;

            carriedObjectAnimator.enabled = true;
            carriedObjectAnimator = null;
        }

        // Only puts the weapon away. Keeping it away is not this method's business: item switching
        // is blocked for the whole carry by InteractionExecutor, off the carry state it already
        // tracks, so there is nothing to set here and nothing to remember to clear later.
        public IEnumerator UnEquipCurrentItem()
        {
            yield return itemEquipper.UnEquipItemAsync();
        }
        public void Carry(Carryable objectToCarry)
        {
            // Refuse a Carry() arriving after the carry interaction was cancelled. The attach is
            // triggered by an animation-time callback on the pickup clip, and that clip can outlive
            // the interaction: a hit lands mid-pickup, Cancel() tears the routine and state down —
            // but the clip keeps playing inside the AnimGraph (the hit reaction has already replaced
            // it as the ActionExecutor's current action, so CancelCurrent never touches it) and its
            // callback then fires into a world where the interaction is gone. Without this gate that
            // late call re-attaches the object and re-applies the carry locks with NOTHING tracking
            // them: Cancel() finds isInInteraction/activeHotspot/IsCarrying all clear and returns,
            // so the character is left walk-locked, focus-locked and un-cancellable — the stuck
            // "carrying with no carry animation" state. StartCarrying() runs at the top of every
            // pickup, so a live pickup always passes this check.
            if (interactionExecutor != null && !interactionExecutor.IsCarrying)
            {
                Debug.LogWarning("[CarryHandler] Carry() after the interaction was cancelled — ignored.", this);
                return;
            }

            objectToCarry.OnStartCarrying?.Invoke();
            SetCarryObject(objectToCarry);
            //carriedObject = objectToCarry;
            objectToCarry.SetParent(GetSocket(objectToCarry.carrySocket));
            EnterCarryLocomotion();
        }

        /// <summary>
        /// Applies the locomotion restrictions that stay active while an object is carried
        /// (walk-only, no crouch, no vertical jump) and locks locomotion. Paired with
        /// <see cref="ExitCarryLocomotion"/>, which every carry-end path must call so the
        /// restrictions are never left applied.
        /// </summary>
        void EnterCarryLocomotion()
        {
            // Snapshot the move type before EnableWalkOnly forces it to Walk — see ExitCarryLocomotion.
            _defaultMoveType = locomotionCore.moveType;

            locomotionCore.EnableWalkOnly();
            locomotionCore.DisableCrouching();

            _defaultPreventVerticalJump = locomotionCore.PreventVerticalJump;
            locomotionCore.PreventVerticalJump = true;
            locomotionCore.EnableLocomotionLock();
        }

        /// <summary>
        /// Reverts everything <see cref="EnterCarryLocomotion"/> applied. Called from every
        /// carry-end path (<see cref="Release"/> and <see cref="ForceRelease"/>) so dropping,
        /// throwing, dumping into a concealment spot, and cancelling all restore locomotion
        /// identically.
        /// </summary>
        void ExitCarryLocomotion()
        {
            locomotionCore.DisableWalkOnly();

            // DisableWalkOnly only lifts the RESTRICTION; it leaves the move type itself wherever
            // EnableWalkOnly forced it, which is Walk. Without putting it back the character keeps
            // moving at carry pace long after the object is gone. The restore has to come after
            // DisableWalkOnly, since SetMoveType clamps back to Walk while the restriction is up.
            locomotionCore.SetMoveType(_defaultMoveType);

            locomotionCore.EnableCrouching();
            locomotionCore.PreventVerticalJump = _defaultPreventVerticalJump;
            locomotionCore.DisableLocomotionLock();
        }


        /// <summary>
        /// Puts the carried object down — a drop or a throw, depending on the clips and the force
        /// passed in. Each side can play its own half of the choreography, and the two halves do not
        /// end together, so exactly ONE of them owns the release:
        ///
        ///   • the object plays a clip → its clip owns the release; the carrier's clip only hands the
        ///                               object over at <paramref name="fallTime"/>
        ///   • no object clip          → the carrier's clip owns it, releasing at fallTime
        ///   • no clips at all         → released on the spot
        ///
        /// The carrier's clip used to clear the carried object when it completed, and that is where
        /// this went wrong: a carrier clip shorter than the object's landed FIRST, and the release
        /// the object's clip then asked for found nothing left to release and bailed out — object
        /// stuck with its Animator off and its rigidbody kinematic, carrier still walk-locked with
        /// its hands flagged full. Clearing it is <see cref="Release"/>'s job alone;
        /// <see cref="isReleasing"/> holds off a second drop in the meantime.
        /// </summary>
        IEnumerator Drop(AnimGraphClipInfo clip, AnimGraphClipInfo objectDropClip = null, float fallTime = 0, Vector3 forceDir = default)
        {
            if (carriedObject == null || isReleasing) yield break;

            isReleasing = true;

            // An object-side clip is only worth hanging the release on when the object has something
            // to play it with. Authored on an object with no ActionExecutor of its own it silently
            // never ran, and — since the release waited on its completion — the object never left the
            // character's hands at all. Resolved once, up front, and everything below branches on it.
            var objectExecutor = objectDropClip != null && objectDropClip.NotNull()
                ? carriedObject.GetComponent<ActionExecutor>()
                : null;

            // A carried object is posed by the CARRIER, so its own Animator is off; it cannot play
            // its half of the drop until it has that back. This is the order the dump path already
            // uses (ForceRelease, then the object's clip) — the drop path executed the clip on a
            // disabled Animator, so an authored object drop animation simply never appeared.
            if (objectExecutor != null)
                RestoreObjectAnimator();

            releasingObjectExecutor = objectExecutor;

            // Temporary workaround: A strange bug occurs during the throw animation due to parent-child changes.
            // During this transition, the carried object's rotation becomes incorrect for one frame.
            // To prevent transform issues, reset the parent and reassign it after one frame.
            carriedObject.Resetparent();
            bool released = false;
            StartCoroutine(AsyncUtil.RunAfterFrames(1, () => { if (!released) carriedObject?.SetParent(GetSocket(carriedObject.carrySocket)); }));

            if (clip.NotNull())
            {
                var dropAction = new CharacterAction(clip);
                dropAction.animationParameters = new AnimParameters()
                {
                    onAnimationUpdate = (normalizedTime, _) =>
                    {
                        if (normalizedTime < fallTime || released) return;
                        released = true;

                        // The hand-off. With a clip of its own the object falls under that clip's
                        // control and is released when it ends; without one, this IS the release.
                        if (objectExecutor != null)
                            carriedObject?.Resetparent();
                        else
                            StartCoroutine(Release(forceDir));
                    }
                };
                dropAction.ignoreCollision = true;
                actionExecutor.Execute(dropAction);
            }

            if (objectExecutor != null)
            {
                var objectAction = new CharacterAction(objectDropClip);
                objectAction.animationParameters = new AnimParameters()
                {
                    transitionBack = false,
                    OnComplete = () => StartCoroutine(Release(forceDir))
                };
                objectAction.ignoreCollision = true;
                objectExecutor.Execute(objectAction);
            }
            else if (clip.IsNull())
            {
                // No animation on either side — release immediately.
                StartCoroutine(Release(forceDir));
            }
            // EndCarrying is handled inside Release; don't call it here too.
        }

        public void PlayCarryIdleClip(Carryable carryObject, float playAfterDelay = .75f)
        {
            _playCarryIdleCoroutine = StartCoroutine(AsyncUtil.RunAfterDelay(playAfterDelay, () =>
                animGraph.PlayLoopingAnimation(null, carryObject.carryIdleClip, carryObject.carryIdleClipMask)));
        }

        /// <summary>
        /// Takes the carry pose off outright — kills a carry idle that is scheduled but not yet
        /// playing, and removes the looping animation from the graph.
        ///
        /// removeAll is true on purpose. The gentler transition-back path bails out without doing
        /// anything whenever something else has taken over the graph since the loop started, and
        /// that is precisely the situation being cleaned up here: a hit reaction is normally what
        /// caused the cancel in the first place. It would leave the carry idle connected and the
        /// upper body stuck holding an object that is no longer there. Removing outright does not
        /// depend on who currently owns the graph.
        /// </summary>
        void CancelCarryIdleClip()
        {
            if (_playCarryIdleCoroutine != null)
            {
                StopCoroutine(_playCarryIdleCoroutine);
                _playCarryIdleCoroutine = null;
            }
            animGraph.StopLoopingAnimations(true);
        }

        public void StopCarryIdleClip(float playAfterDelay = .25f)
        {
            StartCoroutine(AsyncUtil.RunAfterDelay(playAfterDelay, () =>
                animGraph.StopLoopingAnimations(false)));
        }

        /// <summary>
        /// Silently detaches the carried object with no animation and no throw force.
        /// Used by <see cref="ConcealmentHandler"/> when the object is transferred directly
        /// into a concealment spot instead of being dropped normally.
        /// </summary>
        public void ForceRelease()
        {
            if (carriedObject == null) return;

            var co = carriedObject;
            // Clears first so any in-flight Release coroutine exits early — and hands the object its
            // own Animator back on the way out, which the dump clip played next depends on.
            SetCarryObject(null);
            co.OnEndCarrying?.Invoke(Vector3.zero, false);
            co.Resetparent();
            CancelCarryIdleClip();
            ExitCarryLocomotion();
            interactionExecutor.EndCarrying();
        }

        public void Release()
        {
            StartCoroutine(Release(Vector3.zero));
        }

        /// <summary>
        /// Ends the carry because something took the character over — a full-body hit reaction,
        /// a ragdoll, a death — rather than the player choosing to put the object down. All of
        /// those arrive the same way: FighterCore cancels the interaction, and this runs off
        /// <see cref="InteractionExecutor.OnInteractionCancelled"/>.
        ///
        /// The difference from a normal release is timing. A player-driven drop plays a drop
        /// animation, so the carry pose is left up for a moment longer to blend out of. A cancel has
        /// no such animation: the character is already back under their own control, so the pose has
        /// to come off on the spot. It also has to kill a carry idle that has been SCHEDULED but not
        /// yet started (<see cref="PlayCarryIdleClip"/> starts it on a delay) — otherwise a hit taken
        /// just after pickup starts the loop after the release, and nothing is left to stop it.
        /// </summary>
        void CancelCarry()
        {
            // Nothing to undo unless this character is actually mid-carry. Both flags are checked:
            // the executor raises its own at the START of the pickup (CarryHotspot), before the object
            // is ever attached, so a hit inside that window has a carrying character with an empty
            // pair of hands.
            if (carriedObject == null && !interactionExecutor.IsCarrying) return;

            // Put the character back under their own control HERE, synchronously, rather than leaving
            // it to the tail of the release coroutine. Release only reaches those resets after it has
            // waited on a ragdoll blend (carrying a body), and it never reaches them at all when there
            // is nothing in hand to release — either way the character was left flagged as carrying,
            // walk-locked and unable to switch items, with the carry pose already gone.
            //
            // Every one of these is an idempotent setter, so the release coroutine running them again
            // a moment later changes nothing.
            CancelCarryIdleClip();
            ExitCarryLocomotion();
            interactionExecutor.EndCarrying();

            // The object itself still goes out through the normal release, which detaches it, hands it
            // back its physics and — for a body — blends it into a ragdoll on the way down.
            if (carriedObject != null)
                StartCoroutine(Release(Vector3.zero, immediate: true));
        }

        IEnumerator Release(Vector3 forceDir, bool immediate = false)
        {
            // Guard: ForceRelease may have already cleaned up (e.g. hit mid-drop-animation).
            if (carriedObject == null) yield break;

            if (immediate) CancelCarryIdleClip();
            else StopCarryIdleClip();

            var activeRagdollSystem = carriedObject.GetComponent<ActiveRagdollSystem>();
            var co = carriedObject;
            // Cleared before any await so ForceRelease won't race in. This is also what switches the
            // object's own Animator back on — and it has to happen before the ragdoll blend below,
            // which samples the animated pose it blends out of.
            SetCarryObject(null);
            if (activeRagdollSystem != null)
            {
                yield return activeRagdollSystem.BlendToRagdollAsync();
            }
            co.OnEndCarrying?.Invoke(forceDir, forceDir != default(Vector3));
            co.Resetparent();
            ExitCarryLocomotion();
            interactionExecutor.EndCarrying();
        }
    }
}
