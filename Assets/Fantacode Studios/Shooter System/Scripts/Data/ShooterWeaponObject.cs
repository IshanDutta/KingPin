using FS_Core;
using FS_Util;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Pool;

namespace FS_ShooterSystem
{
    public class ShooterWeaponObject : EquippableItemObject
    {
        [Header("Aim & IK Reference")]

        [Tooltip("Controller for aiming the weapon.")]
        public ShootingAimController aimController;

        public Transform supportHandIkTarget;
        [HideInInspector] public Transform supportHandIkSetupHelperTarget;
        public Transform holdHandIkTarget;
        [HideInInspector] public Transform holdHandIkSetupHelperTarget;

        [Tooltip("Reference transform for aiming.")]
        public Transform aimReference;

        [Space(10)]
        [Tooltip("Reference transform for spawn ammo.")]
        public Transform ammoSpawnPoint;

        [HideInInspector]
        public AvatarIKGoal IkTarget;


        [Header("Visual Effects")]

        [Tooltip("Flash effect for the weapon.")]
        public Behaviour flash;

        [Tooltip("List of particle systems for visual effects.")]
        public List<ParticleSystem> vfx = new List<ParticleSystem>();

        [Space(10)]

        [Header("Scope Settings")]

        [Tooltip("Enable scope functionality.")]
        public bool hasScope = false;

        [Tooltip("The scopeTarget representing the scope camera.")]
        [ShowIf("hasScope", true)]
        public Transform scopeTarget;

        [Tooltip("If true, uses a UI Canvas Image as the visual for the scope view")]
        [ShowIf("hasScope", true)]
        public bool useCanvasImageForScope;

        [ShowIf(LogicOperator.And, "hasScope", true, "useCanvasImageForScope", true)]
        [Tooltip("Canvas containing the scope UI graphic to be displayed when aiming.")]
        public GameObject canvasObject;

        [Tooltip("Field of view for the scope.")]
        [ShowIf("hasScope", true)]
        public float scopeFov = 20;

        [field: Space(10)]

        [field: Header("Ammo")]

        [Tooltip("Current ammo count in the weapon.")]
        [field: SerializeField] public int CurrentAmmoCount { get; set; } = 30;

        [Tooltip("Total ammo count available.")]
        [field: SerializeField] public int TotalAmmoCount { get; set; } = 100;

        public bool dontBreakAimingWhileReload { get; set; } = false;

        [Space(10)]
        [Tooltip("Event triggered when the weapon is fired.")]
        public UnityEvent OnFire;

        [Tooltip("Action triggered when the weapon is fired.")]
        public Action OnFireAction;
        public Action OnSetAmmo;


        public AudioSource AudioSource { get; set; }

        [Tooltip("Indicates if the weapon has already been used.")]
        public bool AlreadyUsed { get; set; } = false;

        [Tooltip("Currently loaded ammo in the weapon.")]
        [field:SerializeField] public ShooterAmmoObject CurrentLoadedAmmo { get; set; }

        [Tooltip("Indicates if the weapon has ammo.")]
        public bool HasAmmo => TotalAmmoCount > 0 || ParentShooterFighter.hasInfiniteAmmo;

        public ShooterFighter ParentShooterFighter { get; set; }
        public Action OnEquip;
        public Action OnUnEquip;


        private void Start()
        {
            AudioSource = GetComponent<AudioSource>();

            if(AudioSource == null)
                AudioSource = gameObject.AddComponent<AudioSource>();
            if (bulletPool == null)
            {
                CreatePooledBullet();
            }

            supportHandIkTarget.hideFlags = HideFlags.HideInHierarchy;
            holdHandIkTarget.hideFlags = HideFlags.HideInHierarchy;
            supportHandIkSetupHelperTarget.hideFlags = HideFlags.HideInHierarchy;
            holdHandIkSetupHelperTarget.hideFlags = HideFlags.HideInHierarchy;
        }

        public override void OnEnable()
        {
            if(bulletPool == null)
            {
                CreatePooledBullet();
            }
            base.OnEnable();
        }

        public void SpawnBullet(Vector3 direction, float ammoForceMultiplier = 1)
        {
            if(ParentShooterFighter?.CurrentWeapon == null)
            {
                return;
            }

            if (CurrentLoadedAmmo == null)
                CurrentLoadedAmmo = GetBullet();
            CurrentLoadedAmmo.ParentShooterFighter = ParentShooterFighter;
            CurrentLoadedAmmo.Initialize(CurrentLoadedAmmo.transform.position, direction, ParentShooterFighter.CurrentWeapon.GetAttributeValue<float>("Projectile Speed") * ammoForceMultiplier, ReturnBullet, ParentShooterFighter.BulletHitPoint, ParentShooterFighter.TargetHitData);
            
            CurrentAmmoCount--;
        }

        public void SetAmmo()
        {
            CurrentLoadedAmmo = GetBullet();
            // A freshly nocked round is by definition neither in flight nor hit. The flags are
            // normally cleared by the pool's release callback, but a round can reach here with
            // stale state (found in the hierarchy, or mutated after it was released — a
            // destroy-on-hit round used to get IsHit stamped back on after pooling), and a
            // stale IsHit/ReadyToPerform makes BowHelper treat the nocked arrow as spent.
            CurrentLoadedAmmo.ReadyToPerform = false;
            CurrentLoadedAmmo.IsHit = false;
            CurrentLoadedAmmo.transform.parent.transform.parent = ammoSpawnPoint.transform;
            CurrentLoadedAmmo.transform.parent.transform.localPosition = Vector3.zero;
            CurrentLoadedAmmo.transform.parent.transform.localRotation = Quaternion.identity;
            CurrentLoadedAmmo.transform.localPosition = Vector3.zero;
            CurrentLoadedAmmo.transform.localRotation = Quaternion.identity;
            //CurrentLoadedAmmo.Initialize(CurrentLoadedAmmo.transform.position, aimReference.forward, ParentShooterFighter.CurrentWeapon.GetAttributeValue<float>("Projectile Speed"), ReturnBullet);
            if (CurrentLoadedAmmo.trailObject != null)
                CurrentLoadedAmmo.trailObject.SetActive(false);

            OnSetAmmo?.Invoke();
        }

        public void ReloadAmmo(int magazineSize)
        {
            var addedAmmoCount = !ParentShooterFighter.hasInfiniteAmmo?Mathf.Min(magazineSize - CurrentAmmoCount, TotalAmmoCount): magazineSize - CurrentAmmoCount;
            CurrentAmmoCount += addedAmmoCount;
            if (!ParentShooterFighter.hasInfiniteAmmo)
                TotalAmmoCount -= addedAmmoCount;
        }

        #region Ammo Pooling

        private const int DEFAULT_POOL_CAPACITY = 2;
        private const int MAX_POOL_CAPACITY = 20;
        private ObjectPool<ShooterAmmoObject> bulletPool;

        public void CreatePooledBullet()
        {
            bulletPool = new ObjectPool<ShooterAmmoObject>(
                CreateBullet,
                EnableBullet,
                DisableBullet,
                OnDestroyBullet,
                false,
                DEFAULT_POOL_CAPACITY,
                MAX_POOL_CAPACITY
            );
        }

        private ShooterAmmoObject CreateBullet()
        {
            ShooterAmmo ammo = ParentShooterFighter.CurrentWeapon.ammoData;
            GameObject bulletObject = Instantiate(ammo.ammo);
            ShooterAmmoObject ammoObject = bulletObject.GetComponentInChildren<ShooterAmmoObject>();
            ammoObject.Ammo = ammo;
            ammoObject.SetAudioSource();
            ammoObject.gameObject.SetActive(false);
            return ammoObject;
        }

        private void EnableBullet(ShooterAmmoObject ammo)
        {
            // A pooled bullet can be destroyed out from under the pool (scene cleanup, its
            // parent GameObject destroyed elsewhere, weapon swap). Unity's ObjectPool keeps the
            // stale reference and hands it back, so without this null check SetActive throws a
            // MissingReferenceException — which aborts the firing flow and leaves the shooter
            // (especially AI) stuck. The Unity-overloaded == catches destroyed objects.
            if (ammo == null)
                return;
            ammo.gameObject.SetActive(true);
        }

        private void DisableBullet(ShooterAmmoObject ammo)
        {
            if (ammo == null)
                return;
            ammo.gameObject.SetActive(false);
            ammo.ReadyToPerform = false;
            ammo.IsHit = false;
        }

        private void OnDestroyBullet(ShooterAmmoObject ammo)
        {
            if(ammo == null)
                return;
            if (ammo.transform.parent?.gameObject != null)
                Destroy(ammo.transform.parent.gameObject);
        }

        public ShooterAmmoObject GetBullet()
        {
            if (bulletPool == null)
            {
                CreatePooledBullet();
            }
            ShooterAmmoObject ammo = ammoSpawnPoint.GetComponentInChildren<ShooterAmmoObject>();
            if (ammo == null)
                ammo = GetValidPooledBullet();

            if (ParentShooterFighter?.CurrentWeapon != null)
            {
                // Discard bullets that don't match the equipped weapon's ammo (e.g. after a
                // weapon switch). Bounded so a pool that can't produce a matching/live bullet
                // can't spin forever and hang the shooter. ammo can be null only if the pool is
                // exhausted of live objects and a fresh one couldn't be made — bail in that case.
                int safety = 0;
                while (ammo != null && ParentShooterFighter.CurrentWeapon.ammoData != ammo.Ammo && safety++ < MAX_POOL_CAPACITY)
                {
                    OnDestroyBullet(ammo);
                    ammo = GetValidPooledBullet();
                }
            }

            return ammo;
        }

        // Pulls from the pool until a live (non-destroyed) bullet is found. Unity's ObjectPool
        // can hand back objects that were destroyed while pooled; EnableBullet now ignores those,
        // so Get() returns the destroyed (Unity-null) reference instead of throwing — we skip them
        // here and create a fresh bullet if the pool has no live entries left.
        private ShooterAmmoObject GetValidPooledBullet()
        {
            for (int i = 0; i < MAX_POOL_CAPACITY; i++)
            {
                ShooterAmmoObject ammo = bulletPool.Get();
                if (ammo != null)
                    return ammo;
            }
            return CreateBullet();
        }

        public void ReturnBullet(ShooterAmmoObject ammo)
        {
            bulletPool.Release(ammo);
        }
        #endregion

    }
}