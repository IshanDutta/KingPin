using FS_CombatCore;
using FS_Core;
using FS_Util;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_ShooterSystem
{
    /// <summary>
    /// Throwable projectile with combat capabilities — hit damage and/or timed explosion.
    /// Extends <see cref="ThrowableItemObject"/> (Core) with all damage and explosion logic.
    /// Place this on the prefab instead of <see cref="ThrowableItemObject"/> when the throwable deals damage.
    /// </summary>
    public class ThrowableWeaponObject : ThrowableItemObject
    {
        // ── Public state ──────────────────────────────────────────────────────

        public ThrowableWeapon WeaponData => itemData as ThrowableWeapon;
        public bool FuseStarted { get; private set; }

        // ── Private ───────────────────────────────────────────────────────────

        Coroutine _fuseCoroutine;

        // ── Aim lifecycle (called by ThrowableHandler via virtual hooks) ──────

        public override void OnAimStarted()
        {
            if (WeaponData == null) return;
            if (WeaponData.damageType == ThrowableDamageType.ExplosionImpact
                && WeaponData.explosionTrigger == ExplosionTrigger.FuseOnAim)
            {
                StartFuse();
            }
        }

        public override void OnAimCancelled()
        {
            StopFuse();
        }

        // ── Fuse management ──────────────────────────────────────────────────

        void StartFuse()
        {
            if (FuseStarted) return;
            FuseStarted = true;
            _fuseCoroutine = StartCoroutine(ExplosionCountdown());
            RaiseFuseStarted();
        }

        void StopFuse()
        {
            if (!FuseStarted) return;
            if (_fuseCoroutine != null)
            {
                StopCoroutine(_fuseCoroutine);
                _fuseCoroutine = null;
            }
            FuseStarted = false;
            RaiseFuseStopped();
        }

        IEnumerator ExplosionCountdown()
        {
            yield return new WaitForSeconds(WeaponData.explosionDelay);

            if (!IsThrown)
            {
                RequestForceDrop();
                yield return null;
            }

            Explode();
        }

        // ── Hit processing overrides ─────────────────────────────────────────

        protected override bool OnBeforeHitProcessed(Collision collision)
        {
            if (WeaponData == null) return false;

            // ExplosionImpact + OnCollision: explode immediately on hit.
            if (WeaponData.damageType == ThrowableDamageType.ExplosionImpact
                && WeaponData.explosionTrigger == ExplosionTrigger.OnCollision)
            {
                PlayHitEffect(collision);
                Explode();
                return true; // skip default hit processing
            }

            return false;
        }

        protected override void OnAfterHitProcessed(Collision collision)
        {
            ApplyHitDamage(collision);
        }

        // ── Hit damage ───────────────────────────────────────────────────────

        void ApplyHitDamage(Collision collision)
        {
            if (WeaponData == null) return;
            if (WeaponData.damageType != ThrowableDamageType.HitImpact) return;
            if (WeaponData.damage <= 0f) return;

            var targetObj = collision.gameObject;
            var fighter = targetObj.GetComponent<FighterCore>()
                       ?? targetObj.GetComponentInParent<FighterCore>();

            if (fighter != null)
            {
                // Only damage fighters that the thrower considers a target.
                var sourceFighter = Source != null ? Source.GetComponent<FighterCore>() : null;
                if (sourceFighter != null && !sourceFighter.IsTarget(fighter.gameObject))
                    return;

                var damagable = targetObj.GetComponent<Damagable>()
                             ?? targetObj.GetComponentInParent<Damagable>();

                fighter.TakeHit(new CombatHitData
                {
                    Damage = WeaponData.damage,
                    DamageType = WeaponData.damageTypeMask,
                    HitType = HitType.Melee,
                    HitPoint = collision.contacts[0].point,
                    Damagable = damagable,
                    PlayReaction = WeaponData.playReactionOnTarget,
                    ReactionTag = WeaponData.targetReactionTag,
                    WaitTimeTillNextReaction = 0.5f
                }, attacker: sourceFighter);
            }
            else
            {
                var damagable = targetObj.GetComponent<Damagable>()
                             ?? targetObj.GetComponentInParent<Damagable>();

                damagable?.TakeDamage(WeaponData.damage, WeaponData.damageTypeMask);
            }
        }

        // ── Explosion ────────────────────────────────────────────────────────

        void Explode()
        {
            Vector3 pos = transform.position;

            if (WeaponData.explosionEffect != null)
                Instantiate(WeaponData.explosionEffect, pos, Quaternion.identity);

            if (WeaponData.explosionSound != null)
                FSAudioUtil.PlaySfxNew(WeaponData.explosionSound, pos);

            float radius = WeaponData.explosionRadius;
            var explosion = new ImpactData
            {
                position = pos,
                force = WeaponData.explosionForce,
                radius = radius
            };

            // Include triggers — combat AI body capsules are flipped to
            // isTrigger=true by CombatAIController, so an Ignore query can
            // miss them entirely when the ragdoll bones aren't active. The
            // damage loop below filters non-character hits via Damagable.
            var colliders = Physics.OverlapSphere(pos, radius, WeaponData.explosionTargetLayer, QueryTriggerInteraction.Collide).Where(c => !IsObstructed(pos, GetColliderHitPosition(c), c)).ToList();
            var damagedTargets = new HashSet<Damagable>();
            foreach (var col in colliders)
            {
                var damagable = col.GetComponent<Damagable>() ?? col.GetComponentInParent<Damagable>();
                if (damagable == null) continue;
                if (!damagedTargets.Add(damagable)) continue;

                var fighter = col.GetComponent<FighterCore>() ?? col.GetComponentInParent<FighterCore>();
                var sourceFighter = Source != null ? Source.GetComponent<FighterCore>() : null;

                // Skip allies unless explosion affects all layers.
                if (fighter != null && sourceFighter != null
                    && !sourceFighter.IsTarget(fighter.gameObject)
                    && !ShooterSettings.instance.explosionAffectAllLayers)
                    continue;

                float dist = Vector3.Distance(pos, col.ClosestPoint(pos));
                float t = Mathf.Clamp01(dist / Mathf.Max(radius, 0.001f));
                float finalDamage = Mathf.Lerp(WeaponData.damage, WeaponData.explosionMinDamage, t);

                damagable.TakeImpactDamage(finalDamage, explosion, WeaponData.damageTypeMask);
            }

            GameSoundEvents.Emit(new GameSoundEvent
            {
                position = pos,
                radius = radius * 2f,
                type = GameSoundType.Explosion,
                baseLoudness = 1f,
                source = gameObject
            });

            Destroy(gameObject);
        }

        bool IsObstructed(Vector3 explosionPos, Vector3 hitAffectingPos, Collider target)
        {
            Vector3 direction = (hitAffectingPos - explosionPos).normalized;
            float distance = Vector3.Distance(explosionPos, hitAffectingPos);
            int layerMask = ~(1 << target.gameObject.layer) & ~ShooterSettings.instance.hitBoneMask;

            // The character's root Damagable defines "self" — any collider sharing it
            // (capsule, ragdoll bones, hit-zones, equipment) must not be treated as an
            // obstruction or the explosion can drop on its own victim's pieces.
            var targetRoot = target.GetComponentInParent<Damagable>();

            // Use RaycastAll + filter so weapons / same-character colliders don't
            // mask real obstructions behind them.
            var hits = Physics.RaycastAll(explosionPos + Vector3.up * Mathf.Epsilon, direction, distance, layerMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var raycastHit in hits)
            {
                if (raycastHit.collider.gameObject == target.gameObject) continue;
                if (targetRoot != null && raycastHit.collider.GetComponentInParent<Damagable>() == targetRoot) continue;
                if (raycastHit.collider.GetComponentInParent<EquippableItemObject>() != null) continue;

                return true;
            }
            return false;
        }

        Vector3 GetColliderHitPosition(Collider collider)
        {
            Animator animator = collider.GetComponentInParent<Animator>();
            return (animator != null && animator.isHuman)
                ? animator.GetBoneTransform(HumanBodyBones.UpperChest).position
                : collider.transform.position;
        }
    }
}
