using FS_Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_ShooterSystem
{

    [CustomIcon(FolderPath.DataIcons + "Ammo Icon.png")]
    [Icon(FolderPath.DataIcons + "Ammo Icon.png")]
    [CreateAssetMenu(menuName = "Shooter System/Create Ammo")]
    public class ShooterAmmo : Item
    {
        [Tooltip("Prefab of the projectile or bullet to be instantiated when fired.")]
        public GameObject ammo;

        [Tooltip("List of visual and audio effects triggered upon hitting different surfaces.")]
        public List<HitEffectData> hitEffects = new List<HitEffectData>();

        [Tooltip("Gravity applied to the projectile, affecting its trajectory.")]
        public float gravity = 9.8f;

        [Tooltip("Maximum lifespan of the projectile before it gets destroyed (in seconds).")]
        public float maxLifetime = 5f;

        [Tooltip("Enable to override the default layers the projectile should ignore upon collision.")]
        public bool overrideHitIgnoreMask = false;

        [Tooltip("Layers that the projectile should ignore when checking for collisions.")]
        public LayerMask hitIgnoreMask;

        [Tooltip("Determines if the projectile causes an explosion upon impact (e.g. bazooka).")]
        public bool isExplosive = false;

        [Tooltip("Radius of the explosion effect caused by the projectile (in units).")]
        public float explosiveRadius = 3f;

        [Tooltip("The radius in which enemies can hear this ammo's explosion.")]
        public float explosionSoundRange = 50f;

        [Tooltip("Minimum damage dealt by the explosion at the edge of its radius.")]
        public float minDamage = 3f;

        [Tooltip("Force applied to nearby objects and ragdoll characters when the projectile explodes.")]
        public float explosionForce = 500f;

        [Tooltip("Layer mask filtering which targets can be damaged by the explosion.")]
        public LayerMask explosionTargetLayer = ~0;

        [Tooltip("If enabled, the projectile directly spawns at the hit point without raycasting along its path.")]
        public bool enableDirectHit = false;

        [Tooltip("Prefab instantiated at the explosion point to visualize the explosion effect.")]
        public GameObject explotionPrefab;

        [Tooltip("Audio clip played when the projectile explodes.")]
        public AudioClip explotionAudio;

        [Tooltip("Duration the explosion effect remains in the scene before being destroyed (in seconds).")]
        public float explotionLifeTime;

        [Tooltip("Blood Prefab to instantiate when this ammo hits a fighter.")]
        public GameObject bloodEffect;

        [Tooltip("If enabled, the ammo object will be destroyed immediately upon impact.")]
        public bool destroyImmediateAfterHit = true;

        public HitEffectData GetHitEffect(int hitLayer)
        {
            return hitEffects.FirstOrDefault(h => (h.layer.value & (1 << hitLayer)) != 0);
        }

        public override void SetCategory()
        {
            category = Resources.Load<ItemCategory>("Category/Ammo");
        }
    }
}
