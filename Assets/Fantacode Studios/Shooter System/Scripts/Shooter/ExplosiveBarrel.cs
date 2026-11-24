using FS_Core;
using UnityEngine;
using UnityEngine.Events;
namespace FS_ShooterSystem
{
    public class ExplosiveBarrel : MonoBehaviour
    {
        [SerializeField] ShooterAmmo explosiveAmmo;
        [SerializeField] bool destroyAfterExplode = true;
        [SerializeField] UnityEvent onExplode;


        Damagable damagable;
        bool hasExploded;

        private void Awake()
        {
            damagable = GetComponent<Damagable>();
        }

        private void Start()
        {
            damagable.OnDead += Explode;
        }

        void Explode()
        {
            // Guard against re-entry: the explosion's own AoE damage hits this barrel's
            // Damagable, which fires OnDead again → infinite loop without this flag.
            if (hasExploded) return;
            hasExploded = true;

            // Unsubscribe too so any further damage can't retrigger.
            if (damagable != null)
                damagable.OnDead -= Explode;

            var obj = Instantiate(explosiveAmmo.ammo, transform.position, Quaternion.identity);
            var ammoObj = obj.GetComponentInChildren<ShooterAmmoObject>();
            ammoObj.Ammo = explosiveAmmo;
            ammoObj.TriggerExplosion(transform.position);

            // The ammo prefab isn't pooled for barrels, so clean it up ourselves.
            Destroy(obj);

            onExplode?.Invoke();

            if (destroyAfterExplode)
                Destroy(this.gameObject);
        }
    }
}
