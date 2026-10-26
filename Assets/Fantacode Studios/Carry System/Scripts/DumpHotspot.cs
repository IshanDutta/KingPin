using FS_Core;
using FS_Util;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_InteractionSystem
{
    public class DumpHotspot : InteractionHotspot
    {
        // ── What this spot accepts ───────────────────────────────────────────────────

        [Header("Dump")]
        [Tooltip("Maximum number of objects this spot can store. -1 means unlimited.")]
        public int dumpCapacity = 1;

        [Tooltip("Which CarryableData types are accepted. Leave empty to accept any carryable object.")]
        public List<CarryableData> allowedTypes = new List<CarryableData>();

        // ── This spot's own animation ────────────────────────────────────────────────

        [Tooltip("Animate this spot opening as the object is dumped in — a box lid lifting, a car " +
                 "boot or dumpster opening.")]
        public bool playOpenAnimationOnDump;

        [Tooltip("The AnimGraph on this spot that plays the open animation.")]
        [ShowIf("playOpenAnimationOnDump", true)]
        public AnimGraph dumpOpenTarget;

        [Tooltip("Open animation played on this spot when a dump occurs (e.g. the lid lifting).")]
        [ShowIf("playOpenAnimationOnDump", true)]
        public AnimGraphClipInfo dumpOpenClip;

        // ── Contents ─────────────────────────────────────────────────────────────────

        [Tooltip("Objects currently stored in this spot. Read-only at runtime.")]
        [ReadOnly]
        [SerializeField] List<Carryable> storedObjects = new List<Carryable>();

        /// <summary>Raised once an object has been taken in, with the object that was dumped.</summary>
        public event Action<Carryable> OnObjectDumped;

        public IReadOnlyList<Carryable> StoredObjects => storedObjects;

        public int StoredCount => storedObjects.Count;

        public bool IsFull => dumpCapacity >= 0 && StoredCount >= dumpCapacity;

        // ── Unity lifecycle ──────────────────────────────────────────────────────────

        protected override void Awake()
        {
            base.Awake();

            // A dump spot closes by filling up, not by being used once — IsFull is the only thing
            // that ends it. The base default would switch a capacity-3 bin off after the first body.
            disableHotspotAfterCompletion = false;
            disableObjectAfterCompletion = false;
        }

        // Runs only when the component is first added (and on an explicit Reset), so a fresh dump
        // spot prompts with the right verb and an authored label is never overwritten.
        void Reset()
        {
            interactionLabel = "Stash";
        }

        public bool CanDump(Carryable obj)
        {
            // Null-checked first: callers ask this straight off a CarryHandler, whose CarriedObject
            // is null whenever nothing is being carried.
            if (obj == null || IsFull || !obj.canBeDumped) return false;

            return allowedTypes.Count == 0 || allowedTypes.Contains(obj.data);
        }

        public override bool CanInteract() => base.CanInteract() && !IsFull;


        public override bool CanInteractWhileCarrying(InteractionExecutor executor) =>
            CanDump(CarriedBy(executor));

        static Carryable CarriedBy(InteractionExecutor executor)
        {
            if (executor == null) return null;

            var carry = executor.GetComponent<CarryHandler>();
            return carry != null && carry.IsCarrying ? carry.CarriedObject : null;
        }

        // ── The dump ─────────────────────────────────────────────────────────────────

        public override IEnumerator Interact(InteractionExecutor interactionExecutor)
        {
            yield return base.Interact(interactionExecutor);

            if (interactionExecutor == null) yield break;

            var carryHandler = interactionExecutor.GetComponent<CarryHandler>();
            var carried = carryHandler != null ? carryHandler.CarriedObject : null;

            // Re-checked at the moment of the act, not assumed from the gate that offered it: the
            // walk to the interaction point takes time, and the spot can fill up during it.
            if (!CanDump(carried)) yield break;

            yield return DumpCarried(interactionExecutor, carryHandler, carried);
        }
        protected virtual IEnumerator DumpCarried(InteractionExecutor exec, CarryHandler carryHandler,
                                                  Carryable carried)
        {
            var actionExecutor = exec.GetComponent<ActionExecutor>();
            var damagable = exec.GetComponent<Damagable>();

            // Hits are ignored for the duration: cancelling a transfer midway is what leaves an object
            // owned by nobody. Restored in the finally, so however this ends the character is never
            // left invulnerable.
            SetCanTakeHit(damagable, false);
            try
            {
                carryHandler.ForceRelease();

                // This spot's own opening, through its AnimGraph. Never drives timing.
                PlayDumpOpening();

                // The object's own animation, if it can play one. Fire-and-forget — and a body settles
                // into a ragdoll as it lands, rather than holding its last animated pose.
                if (carried.objectDumpClip.NotNull())
                {
                    carried.GetComponent<ActionExecutor>()?.Execute(new CharacterAction(carried.objectDumpClip)
                    {
                        animationParameters = new AnimParameters()
                        {
                            transitionBack = false,
                            OnComplete = () =>
                            {
                                var ragdoll = carried.GetComponent<ActiveRagdollSystem>();
                                if (ragdoll != null) ragdoll.BlendToRagdoll();
                            }
                        },
                        ignoreCollision = true,
                        requiresLedgeBlocking = false
                    });
                }

                // The carrier's animation — per object type, from its CarryableData. Awaited: the
                // object changes hands when this finishes.
                if (carried.playerDumpClip.NotNull() && actionExecutor != null)
                {
                    yield return actionExecutor.ExecuteAsync(new CharacterAction(carried.playerDumpClip)
                    {
                        animationParameters = new AnimParameters(),
                        ignoreCollision = true,
                        requiresLedgeBlocking = false
                    });
                }

                StoreObject(carried);

                carried.OnDumpedIntoSpot?.Invoke();
                exec.EndCarrying();
            }
            finally
            {
                SetCanTakeHit(damagable, true);
            }
        }

        /// <summary>
        /// Takes <paramref name="obj"/> into this spot: records it and parents it here so it travels
        /// with the spot. The transfer is the last step of the dump — this only accepts what it is
        /// handed, so a scripted stash can call it directly.
        /// </summary>
        public void StoreObject(Carryable obj)
        {
            if (obj == null) return;

            storedObjects.Add(obj);
            obj.transform.SetParent(transform);

            OnObjectDumped?.Invoke(obj);
        }

        /// <summary>
        /// Fires this spot's own "opening" animation (a lid, a boot, a door) through its AnimGraph.
        /// Never drives the interaction's timing — the character animation does.
        /// </summary>
        public void PlayDumpOpening()
        {
            if (!playOpenAnimationOnDump || dumpOpenTarget == null || !dumpOpenClip.NotNull()) return;

            dumpOpenTarget.Crossfade(dumpOpenClip.clip, dumpOpenClip, transitionBack: false);
        }

        static void SetCanTakeHit(Damagable damagable, bool value)
        {
            if (damagable != null) damagable.CanTakeHit = value;
        }
    }
}
