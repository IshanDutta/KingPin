using FS_Core;
using System.Collections;
using UnityEngine;

namespace FS_CombatCore
{
    [RequireComponent(typeof(ActionSystemBase))]
    public class ReactionHandler : MonoBehaviour
    {
        FighterCore fighter;
        ActionSystemBase actionSystem;
        int activeHitCount;
        bool isInRagdoll;

        void Awake()
        {
            fighter = GetComponent<FighterCore>();
            actionSystem = GetComponent<ActionSystemBase>();
        }

        void OnEnable()
        {
            if (fighter != null)
            {
                fighter.OnGotHit += OnHit;
                fighter.OnGotHitCompleted += OnHitCompleted;
                fighter.OnRagdollImpactStarted += OnRagdollImpactStarted;
            }
        }

        void OnDisable()
        {
            if (fighter != null)
            {
                fighter.OnGotHit -= OnHit;
                fighter.OnGotHitCompleted -= OnHitCompleted;
                fighter.OnRagdollImpactStarted -= OnRagdollImpactStarted;
            }

            if (activeHitCount > 0)
            {
                activeHitCount = 0;
                actionSystem?.ReleaseFocus();
            }
        }

        /// <summary>
        /// Fires when FighterCore starts a ragdoll-impact sequence (explosion, etc.).
        /// Acquires focus the same way a non-additive hit does, and holds it until
        /// the get-up completes (IsKnockedDown returns to false).
        /// </summary>
        void OnRagdollImpactStarted()
        {
            activeHitCount++;
            if (activeHitCount == 1 && actionSystem != null){
                actionSystem.RequestFocus();
                isInRagdoll = true;
            }

            // Mirror the knockdown-release path used by OnHitCompleted: once the
            // get-up animation starts we wait for IsKnockedDown to flip false, then
            // decrement and release focus if no other hits are pending.
            fighter.OnGettingUp += OnImpactGettingUpStarted;
        }

        void OnImpactGettingUpStarted()
        {
            fighter.OnGettingUp -= OnImpactGettingUpStarted;
            StartCoroutine(WaitForImpactGetUpComplete());
        }

        IEnumerator WaitForImpactGetUpComplete()
        {
            yield return new WaitUntil(() => !fighter.IsKnockedDown);

            if (activeHitCount > 0)
                activeHitCount--;

            if (activeHitCount == 0){
                actionSystem?.ReleaseFocus();
                isInRagdoll = false;
            }
        }

        void OnHit(FighterCore attacker, Vector3 hitPoint, float hittingTime, bool isBlocked, HitType hitType)
        {
            // Melee hits are handled by the melee combat system's own focus logic.
            if (hitType == HitType.Melee) return;

            // Hits that won't start a reaction (suppressed while knocked down,
            // getting up, etc.) never fire OnGotHitCompleted — counting them would
            // hold focus forever.
            if (!fighter.LastHitWillPlayReaction) return;

            // Additive reactions (light flinch) play on top without blocking systems.
            if (fighter.CurrReaction == null || fighter.CurrReaction.isAdditiveAnimation) return;

            activeHitCount++;

            // Focus on the first qualifying hit.
            if (activeHitCount == 1 && actionSystem != null)
                actionSystem.RequestFocus();
        }

        void OnHitCompleted()
        {
            if (activeHitCount <= 0) return;

            activeHitCount--;

            // Don't unfocus if more hits are still being processed.
            if (activeHitCount > 0) return;

            // Don't unfocus during knockdown — wait for get-up to complete.
            if (fighter.IsKnockedDown)
            {
                WaitForGetUp();
                return;
            }

            if(!isInRagdoll)
                actionSystem?.ReleaseFocus();
        }

        void WaitForGetUp()
        {
            // Subscribe once to detect when knockdown ends.
            fighter.OnGettingUp += OnGettingUpStarted;
        }

        void OnGettingUpStarted()
        {
            fighter.OnGettingUp -= OnGettingUpStarted;

            // The get-up animation plays via ActionExecutor. Wait for it to finish
            // by checking IsKnockedDown — it becomes false after the get-up completes.
            StartCoroutine(WaitForGetUpComplete());
        }

        IEnumerator WaitForGetUpComplete()
        {
            yield return new WaitUntil(() => !fighter.IsKnockedDown);
            actionSystem?.ReleaseFocus();
        }
    }
}
