using FS_CombatCore;
using FS_Core;
using FS_Util;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class ShooterAmmoObject : MonoBehaviour
    {
        public ShooterAmmo Ammo { get; set; }
        [Tooltip("The trail effect for the bullet.")]
        public GameObject trailObject;
        [Tooltip("The transform where the ammo will attach upon hitting a target (e.g., arrows sticking into surfaces).")]
        public Transform hitTransform;

        private Vector3 velocity;
        private Vector3 previousPosition;
        private float elapsedTime;
        private Action<ShooterAmmoObject> returnToPoolCallback;

        public bool ReadyToPerform { get; set; } = false;
        public bool IsHit { get; set; } = false;

        [HideInInspector] public AudioSource fireAudioSource;
        [HideInInspector] public AudioSource hitAudioSource;

        [Tooltip("Assign a Rigidbody if the ammo requires physics-based collision.")]
        public Rigidbody rigidBody;

        [Tooltip("Assign a Collider if the ammo should interact with other objects on collision.")]
        public new Collider collider;

        public ShooterFighter ParentShooterFighter { get; set; }

        private void Start()
        {
            if (rigidBody == null)
                rigidBody = GetComponent<Rigidbody>();
            if (collider == null)
                collider = GetComponent<Collider>();
        }

        private void OnEnable()
        {
            SetAudioSource();
            HandlePhysics(false);
        }

        public void HandlePhysics(bool enable = true)
        {
            if (rigidBody != null)
                rigidBody.isKinematic = !enable;
            if (collider != null)
                collider.enabled = enable;
        }

        public void SetAudioSource()
        {
            if (fireAudioSource == null)
            {
                var go = transform.parent != null ? transform.parent.gameObject : gameObject;

                fireAudioSource = go.AddComponent<AudioSource>();
                fireAudioSource.playOnAwake = false;
                hitAudioSource = go.AddComponent<AudioSource>();
                hitAudioSource.playOnAwake = false;
            }
        }

        public void Initialize(Vector3 startPosition, Vector3 direction, float speed, Action<ShooterAmmoObject> returnCallback, Vector3 hitPos = new Vector3(), RaycastHit hit = new RaycastHit())
        {
            ReadyToPerform = true;
            HandlePhysics(false);
            //transform.forward = direction;
            transform.position = startPosition;
            velocity = direction * speed;
            elapsedTime = 0f;
            gameObject.SetActive(true);
            ClearTrailEffect();
            returnToPoolCallback = returnCallback;
            hitParentTransform = null;
            IsHit = false;
            var distToHitPoint = Vector3.Distance(startPosition, hit.transform != null ? hit.point : hitPos);

            // The precomputed `hit` comes from the shooter's aim cast (eye/camera origin).
            // The muzzle (`startPosition`) is often different — and worse, when the
            // shooter is up against a wall the gun barrel can be *inside* the wall's
            // collider. A raycast originating inside a collider silently skips that
            // collider, so casting from `startPosition` would report "clear" through
            // the wall. Cast from the shooter's body (chest height) instead — that
            // point is reliably outside the wall the muzzle is clipping into.
            bool muzzleBlocked = false;
            if (hit.transform != null && ParentShooterFighter != null)
            {
                Vector3 bodyOrigin = ParentShooterFighter.transform.position + Vector3.up * 1.2f;
                Vector3 toHit = hit.point - bodyOrigin;
                float bodyDist = toHit.magnitude;
                if (bodyDist > 0.05f)
                {
                    int castMask = ~ShooterSettings.instance.hitIgnoreMask;
                    var blockingHits = Physics.RaycastAll(bodyOrigin, toHit / bodyDist, bodyDist - 0.05f, castMask, QueryTriggerInteraction.Ignore);
                    foreach (var bh in blockingHits)
                    {
                        if (bh.collider.transform == hit.transform) continue;
                        if (bh.collider.transform.IsChildOf(hit.transform)) continue;
                        if (hit.transform.IsChildOf(bh.collider.transform)) continue;
                        if (IsOwnCharacter(bh.collider.gameObject)) continue;
                        muzzleBlocked = true;
                        break;
                    }
                }
            }

            if (!muzzleBlocked && (Ammo.enableDirectHit || distToHitPoint <= 5f) && hit.transform != null)
            {
                // Use the aim raycast's actual surface point, not the muzzle-target
                // position. Overwriting hit.point with hitPos puts impact decals and
                // damage application at the bullet's intended landing point — which
                // doesn't lie on the hit collider's surface and produces floating
                // impact effects + wrong knockback direction.
                HandleHit(hit, previousPosition);
                // Must be set BEFORE ReturnToPool: the release path (DisableBullet) resets the
                // pooled instance's flags, so a destroy-on-hit ammo must not touch them again
                // afterwards. Setting IsHit after the release stamped the flag onto the already
                // pooled instance, and EnableBullet doesn't clear flags on the way out — so the
                // next SetAmmo nocked an arrow that was born with IsHit == true, which blocked
                // BowHelper's hand-sync guard for the whole draw.
                IsHit = true;
                if (Ammo.destroyImmediateAfterHit)
                    ReturnToPool();
                else
                {
                    transform.position = hitTransform != null ? hit.point - ((hitTransform.position - transform.position).normalized * Vector3.Distance(transform.position, hitTransform.position)) : hit.point;
                    // Store relative position and rotation
                    hitParentTransform = hit.transform;
                    localHitPosition = hitParentTransform.InverseTransformPoint(transform.position);
                    localHitRotation = Quaternion.Inverse(hitParentTransform.rotation) * transform.rotation;
                    transform.forward = velocity.normalized;
                    trailObject.SetActive(false);
                }
            }
            else if (hitTransform != null)
            {
                // Use the distance to the hit object to place the position in front of the character
                transform.position = transform.position + direction.normalized * 1;
                previousPosition = hitTransform.position;
            }
            else
            {
                transform.position = transform.position + direction.normalized * 1;
                // Default to the starting position if no hit occurred
                previousPosition = transform.position;
            }
        }

        void ClearTrailEffect()
        {
            if (trailObject != null)
            {
                trailObject.SetActive(true);
                var trail = trailObject.GetComponent<TrailRenderer>();
                if (trail != null)
                    trail.Clear();
            }
        }

        private Transform hitParentTransform;
        private Vector3 localHitPosition;
        private Quaternion localHitRotation;

        private void Update()
        {
            if (!ReadyToPerform || Ammo == null) return;

            elapsedTime += Time.deltaTime;

            if (!IsHit)
            {
                Vector3 newPosition = transform.position + velocity * Time.deltaTime;

                // Cast direction is the bullet's actual velocity direction. The
                // previous formulation derived velDir from (hitTransform.position +
                // velocity*dt - previousPosition), which couples the cast direction
                // to the tip-child transform's position. A frame where the parent
                // transform rotates (e.g. transform.forward is interpolated toward a
                // stale velDir while the bullet has already turned via gravity) can
                // make that delta point backward, sending the SphereCast in the
                // opposite direction of travel and the bullet flies past the target
                // unhit. Use velocity directly so the cast always tracks real motion.
                var velDir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : transform.forward;

                float castDistance = velocity.magnitude * Time.deltaTime;
                int castMask = ~ShooterSettings.instance.hitIgnoreMask;

                // Shift the cast origin slightly backward along the velocity direction.
                // Physics.Raycast ignores colliders that already overlap the cast's
                // origin, so if a fast bullet ticks into (or starts a frame inside) a
                // target's collider, the cast silently passes through it. The player's
                // aim logic handles the equivalent self-overlap by advancing past it
                // and re-casting (ShooterController.UpdateAimPoint:452-478). Here we
                // pre-shift the origin so the ray enters any overlapped collider
                // from outside instead of starting inside it.
                const float backOffset = 0.1f;
                Vector3 castOrigin = previousPosition - velDir * backOffset;
                float effectiveDistance = castDistance + backOffset;

                var hitFound = Physics.Raycast(castOrigin, velDir, out RaycastHit hit, effectiveDistance, castMask, QueryTriggerInteraction.Ignore);

                // Mirror ShooterController.UpdateAimPoint's self-hit handling
                // (lines 452-478): if the cast first lands on the shooter itself,
                // advance the origin past that hit and re-cast within the same
                // frame so the actual target downrange is detected — instead of
                // deferring to next tick, which can let a fast bullet skip past
                // its real target and look like it's flying the wrong way.
                if (hitFound && IsOwnCharacter(hit.collider.gameObject))
                {
                    Vector3 newOrigin = hit.point + velDir * 0.01f;
                    float remainingRange = effectiveDistance - hit.distance;
                    if (remainingRange > 0f)
                    {
                        bool secondHitFound = Physics.Raycast(newOrigin, velDir, out RaycastHit secondHit, remainingRange, castMask, QueryTriggerInteraction.Ignore);
                        if (secondHitFound && !IsOwnCharacter(secondHit.collider.gameObject))
                        {
                            hit = secondHit;
                        }
                        else
                        {
                            hitFound = false;
                        }
                    }
                    else
                    {
                        hitFound = false;
                    }
                }

                if (hitFound)
                {
                    HandleHit(hit, previousPosition);
                    if (Ammo.destroyImmediateAfterHit)
                        ReturnToPool();
                    else
                    {
                        transform.position = hitTransform != null ? hit.point - (velDir * Vector3.Distance(transform.position, hitTransform.position)) : hit.point;
                        hitParentTransform = hit.transform;
                        localHitPosition = hitParentTransform.InverseTransformPoint(transform.position);
                        localHitRotation = Quaternion.Inverse(hitParentTransform.rotation) * transform.rotation;
                        transform.forward = velDir;
                        trailObject.SetActive(false);
                        IsHit = true;
                    }
                }
                else
                {
                    transform.position = newPosition;
                    // transform.forward = Vector3.MoveTowards(transform.forward, velDir, Time.deltaTime * .5f);
                    transform.forward = velDir;
                }

                previousPosition = hitTransform != null ? hitTransform.position : transform.position;
                velocity.y -= Ammo.gravity * Time.deltaTime;
            }

            if (IsHit && hitParentTransform != null)
            {
                transform.position = hitParentTransform.TransformPoint(localHitPosition);
                transform.rotation = hitParentTransform.rotation * localHitRotation;
            }

            if (elapsedTime > Ammo.maxLifetime)
            {
                ReturnToPool();
            }
        }

        // Mirrors ShooterController.IsOwnCharacter — covers the case where the
        // hit object is the shooter itself, a child bone, or a parent root.
        bool IsOwnCharacter(GameObject hitObject)
        {
            if (hitObject == null || ParentShooterFighter == null) return false;
            Transform shooter = ParentShooterFighter.transform;
            Transform t = hitObject.transform;
            if (t == shooter) return true;
            if (t.IsChildOf(shooter)) return true;
            if (shooter.IsChildOf(t)) return true;
            return false;
        }


        void HandleHit(RaycastHit hit, Vector3 origin)
        {
            if (Ammo.isExplosive)
            {
                TriggerExplosion(hit.point);
            }
            else
            {
                // Skip the generic wall decal/hit effect for character hits — the
                // alive-state body capsule is wider than the visible mesh, so the
                // decal would land on the invisible capsule shell and read as
                // floating "in air". Blood effect is spawned separately by
                // ApplyDirectDamage for characters.
                bool isCharacterHit = hit.collider.GetComponent<FighterCore>() != null
                                   || hit.collider.GetComponentInParent<FighterCore>() != null
                                   || hit.collider.GetComponent<Damagable>() != null
                                   || hit.collider.GetComponentInParent<Damagable>() != null;

                if (!isCharacterHit)
                {
                    var hitEffect = Ammo.GetHitEffect(hit.collider.gameObject.layer);
                    if (hitEffect != null)
                    {
                        SpawnHitEffect(hit, hitEffect);
                        PlayHitSound(hitEffect.hitAudioClip);
                    }
                }
                ApplyDirectDamage(hit.point, hit);
            }
        }

        /// <summary>
        /// Fires the explosion (effect, audio, damage, sound event) at the given world position.
        /// Bypasses the raycast-travel loop so stationary sources (e.g. ExplosiveBarrel) can
        /// detonate in place without Initialize/movement.
        /// </summary>
        public void TriggerExplosion(Vector3 explosionPos)
        {
            if (Ammo == null || !Ammo.isExplosive) return;

            SpawnExplosionEffect(explosionPos);
            ParentShooterFighter?.AmmoExploded(this);
            PlayHitSound(Ammo.explotionAudio);
            ApplyExplosionDamage(explosionPos);

            if (Ammo.destroyImmediateAfterHit)
                ReturnToPool();
        }

        // ── Direct hit damage (bullet) ───────────────────────────────────────

        void ApplyDirectDamage(Vector3 hitPos, RaycastHit hit)
        {
            var fighter = hit.collider.GetComponent<FighterCore>()
                       ?? hit.collider.GetComponentInParent<FighterCore>();

            Damagable damageHandler = null;
            var boneDamageHandler = hit.collider.GetComponent<ShooterBoneDamageHandler>();
            if (boneDamageHandler != null)
                damageHandler = boneDamageHandler.ParentDamagable
                             ?? hit.collider.GetComponentInParent<Damagable>();
            else
                damageHandler = hit.collider.GetComponent<Damagable>()
                             ?? hit.collider.GetComponentInParent<Damagable>();

            var currWeapon = ParentShooterFighter.CurrentWeapon;
            if (damageHandler != null && damageHandler.CanTakeHit)
            {
                float damage = DamageCalculator.CalculateDamage(currWeapon.GetAttributeValue<float>("Damage"), Ammo.GetAttributeValue<float>("Damage"), damageHandler);
                if (boneDamageHandler != null)
                    damage *= boneDamageHandler.damageMultiplier;

                if (fighter != null && ParentShooterFighter.Fighter.IsTarget(fighter.gameObject))
                {
                    var bloodEffect = fighter.overrideBloodEffects ? fighter.bloodEffectPrefab : Ammo.bloodEffect;
                    SpawnHitEffect(hit, bloodEffect);

                    fighter.TakeHit(new CombatHitData()
                    {
                        Damage = damage,
                        DamageType = currWeapon?.GetDamageType() ?? 0,
                        HitType = currWeapon.HitType,
                        Damagable = damageHandler,
                        PlayReaction = currWeapon.playReactionOnTarget,
                        ReactionTag = currWeapon.targetReactionTag,
                        WaitTimeTillNextReaction = 0.8f,
                        IsUnBlockableHit = true,
                    }, attacker: ParentShooterFighter.Fighter);
                }
                else if (fighter == null)
                {
                    damageHandler.TakeDamage(damage, currWeapon?.GetDamageType() ?? 0);
                }

                ParentShooterFighter.OnHitDamagable?.Invoke();
            }

            Rigidbody rb = hit.collider.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForceAtPosition(velocity * Time.deltaTime, hitPos, ForceMode.Impulse);
            }
        }

        // ── Explosion damage (bazooka, RPG, etc.) ────────────────────────────
        // Uses TakeImpactDamage — same event-driven approach as ThrowableWeaponObject.
        // FighterCore receives the ImpactData via Damagable.OnImpactDamage and
        // handles ragdoll knockdown automatically when useRagdollForExplosion is enabled.

        void ApplyExplosionDamage(Vector3 explosionPos)
        {
            var ignoreLayerMask = Ammo.overrideHitIgnoreMask ? Ammo.hitIgnoreMask : ShooterSettings.instance.explosionIgnoreMask;

            // Include triggers — combat AI body capsules are flipped to
            // isTrigger=true by CombatAIController, so an Ignore query can
            // miss them entirely when the ragdoll bones aren't active. The
            // damage loop below filters non-character hits via Damagable.
            Collider[] colliders = Physics.OverlapSphere(explosionPos, Ammo.explosiveRadius, ~ignoreLayerMask, QueryTriggerInteraction.Collide)
                .Where(c => !IsObstructed(explosionPos, GetColliderHitPosition(c), c))
                .ToArray();

            var explosion = new ImpactData
            {
                position = explosionPos,
                force    = Ammo.explosionForce,
                radius   = Ammo.explosiveRadius
            };

            float maxDamage = Ammo.GetAttributeValue<float>("Damage");
            int damageType = ParentShooterFighter?.CurrentWeapon?.GetDamageType() ?? 0;
            var damagedTargets = new HashSet<Damagable>();

            foreach (var col in colliders)
            {
                var damagable = col.GetComponent<Damagable>() ?? col.GetComponentInParent<Damagable>();
                if (damagable == null) continue;
                if (!damagedTargets.Add(damagable)) continue;

                // Skip allies unless explosion affects all layers.
                var fighter = col.GetComponent<FighterCore>() ?? col.GetComponentInParent<FighterCore>();
                if (fighter != null && ParentShooterFighter != null
                    && !ParentShooterFighter.Fighter.IsTarget(fighter.gameObject)
                    && !ShooterSettings.instance.explosionAffectAllLayers)
                    continue;

                // Distance-based damage falloff: max at centre, min at edge.
                float dist = Vector3.Distance(explosionPos, col.ClosestPoint(explosionPos));
                float t = Mathf.Clamp01(dist / Mathf.Max(Ammo.explosiveRadius, 0.001f));
                float finalDamage = Mathf.Lerp(maxDamage, Ammo.minDamage, t);

                damagable.TakeImpactDamage(finalDamage, explosion, damageType);
            }

            // Apply explosion force to all rigidbodies (props, debris, etc.)
            foreach (var col in colliders)
            {
                Rigidbody rb = col.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 forceDir = (col.transform.position - explosionPos).normalized;
                    float distance = Mathf.Max(Vector3.Distance(explosionPos, col.transform.position), 1f);
                    float forceAmount = Ammo.explosionForce / distance;
                    rb.AddForceAtPosition(forceDir * forceAmount, explosionPos, ForceMode.Impulse);
                }
            }

            GameSoundEvents.Emit(new GameSoundEvent
            {
                position = explosionPos,
                radius   = Ammo.explosionSoundRange,
                type     = GameSoundType.Explosion,
                baseLoudness = 1f,
                source   = gameObject
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        Vector3 GetColliderHitPosition(Collider collider)
        {
            Animator animator = collider.GetComponentInParent<Animator>();
            return (animator != null && animator.isHuman)
                ? animator.GetBoneTransform(HumanBodyBones.UpperChest).position
                : collider.transform.position;
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

        void PlayHitSound(AudioClip audioClip)
        {
            if (audioClip != null)
            {
                hitAudioSource.Stop();
                hitAudioSource.clip = audioClip;
                hitAudioSource.Play();
            }
        }

        // When the bullet starts (or ticks) inside a collider, Physics queries can
        // return RaycastHit.point = Vector3.zero (and normal = Vector3.zero) because
        // there's no real entry surface. Spawning the blood/hit effect at zero
        // teleports it to the world origin. Resolve a sensible fallback position
        // and forward axis from the bullet's current position and the hit collider.
        private (Vector3 position, Quaternion rotation) ResolveHitPose(RaycastHit hit)
        {
            Vector3 pos = hit.point;
            if (pos.sqrMagnitude < 0.0001f)
            {
                pos = hit.collider != null
                    ? hit.collider.ClosestPoint(transform.position)
                    : transform.position;
                if (pos.sqrMagnitude < 0.0001f)
                    pos = transform.position;
            }

            Vector3 normal = hit.normal;
            if (normal.sqrMagnitude < 0.0001f)
            {
                normal = velocity.sqrMagnitude > 0.0001f ? -velocity.normalized : -transform.forward;
                if (normal.sqrMagnitude < 0.0001f)
                    normal = Vector3.up;
            }

            return (pos, Quaternion.LookRotation(normal));
        }

        private void SpawnHitEffect(RaycastHit hit, HitEffectData hitEffectData)
        {
            var (pos, rot) = ResolveHitPose(hit);

            if (hitEffectData.hitMarker != null)
            {
                GameObject effect = Instantiate(hitEffectData.hitMarker, pos, rot);
                if (hit.collider != null)
                    effect.transform.parent = hit.collider.transform;
                Destroy(effect, ShooterSettings.instance.hitEffectLifetime);
            }
            if (hitEffectData.hitEffect != null)
            {
                GameObject effect = Instantiate(hitEffectData.hitEffect, pos, rot);
                Destroy(effect, ShooterSettings.instance.hitEffectLifetime);
            }
        }

        private void SpawnHitEffect(RaycastHit hit, GameObject hitEffect)
        {
            if (hitEffect == null) return;

            var (pos, rot) = ResolveHitPose(hit);
            GameObject effect = Instantiate(hitEffect, pos, rot);
            Destroy(effect, ShooterSettings.instance.hitEffectLifetime);
        }

        void SpawnExplosionEffect() => SpawnExplosionEffect(transform.position);

        void SpawnExplosionEffect(Vector3 pos)
        {
            if (Ammo.explotionPrefab != null)
            {
                var vfx = Instantiate(Ammo.explotionPrefab, pos, Quaternion.identity);
                Destroy(vfx, Ammo.explotionLifeTime);
            }
        }

        public void ReturnToPool()
        {
            if(!gameObject.activeSelf) return;

            gameObject.SetActive(false);
            returnToPoolCallback?.Invoke(this);
        }
    }
}
