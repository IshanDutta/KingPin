using FS_CombatCore;
using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_ShooterSystem
{
    /// <summary>
    /// Throwable item with combat capabilities — damage on impact or timed explosion.
    /// Extends <see cref="ThrowableItem"/> (Core) with all damage/explosion configuration.
    /// </summary>
    [CreateAssetMenu(menuName = "Shooter System/Throwable Weapon")]
    public class ThrowableWeapon : ThrowableItem
    {
        // ── Damage ────────────────────────────────────────────────────────────

        [Tooltip("HitImpact = damage on direct collision (stone). ExplosionImpact = area damage (grenade).")]
        public ThrowableDamageType damageType;

        [Tooltip("Damage dealt to targets (HitImpact: single target; ExplosionImpact: max at centre).")]
        public float damage = 20f;

        [Tooltip("Damage type bitmask matched against target's allowed damage types.")]
        [DamageTypeDropdown]
        public int damageTypeMask = ~0;

        // ── Hit Reaction ──────────────────────────────────────────────────────

        [Tooltip("Should the target play a hit reaction animation when struck by this throwable.")]
        public bool playReactionOnTarget = true;

        [Tooltip("Tag used to select the reaction animation (e.g. light, medium, heavy).")]
        public string targetReactionTag;

        [Tooltip("Weapon-specific reaction data. Falls back to the target's default reactions if null.")]
        public ReactionsData reactionData;

        // ── Explosion ─────────────────────────────────────────────────────────

        [Tooltip("FuseOnAim = countdown starts when aiming (frag grenade). OnCollision = explodes on hit.")]
        public ExplosionTrigger explosionTrigger;

        [Tooltip("Minimum damage at the edge of the explosion radius.")]
        public float explosionMinDamage = 5f;

        [Tooltip("Seconds before explosion fires (FuseOnAim only).")]
        public float explosionDelay = 3f;

        [Tooltip("Radius of the explosion area.")]
        public float explosionRadius = 5f;

        [Tooltip("Force applied to ragdoll characters in the explosion radius.")]
        public float explosionForce = 500f;

        [Tooltip("Layer mask filtering which targets can be damaged by the explosion.")]
        public LayerMask explosionTargetLayer = ~0;

        [Tooltip("VFX prefab spawned when the explosion fires.")]
        public GameObject explosionEffect;

        [Tooltip("Sound played when the explosion fires.")]
        public AudioClip explosionSound;

        /// <summary>Fuse HUD countdown length — the explosion delay for this weapon.</summary>
        public override float FuseDuration => explosionDelay;
    }

    public enum ThrowableDamageType
    {
        HitImpact,
        ExplosionImpact
    }

    public enum ExplosionTrigger
    {
        FuseOnAim,
        OnCollision
    }
}
