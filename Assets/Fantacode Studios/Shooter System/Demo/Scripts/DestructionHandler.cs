using FS_Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
namespace FS_ShooterSystem
{
    public class DestructionHandler : MonoBehaviour
    {
        public Damagable damagable;
        public List<Rigidbody> rigidbodies = new List<Rigidbody>();
        public UnityEvent OnDestrucion;

        private new Collider collider;
        private void Awake()
        {
            if(damagable == null)
            {
                damagable = GetComponent<Damagable>();
            }
            collider = GetComponent<Collider>();

        }
        private void Start()
        {
            if(damagable != null)
            {
                damagable.OnDead += HandleDestruction;
            }
        }
        private void HandleDestruction()
        {
            if (collider != null)
            {
                collider.enabled = false; // Disable the collider
            }
            OnDestrucion?.Invoke();
            foreach (var rb in rigidbodies)
            {
                if (rb != null)
                {
                    rb.isKinematic = false; // Enable physics
                    rb.useGravity = true; // Enable Gravity
                    rb.AddExplosionForce(10f, transform.position, 5f); // Add explosion force
                }
            }
        }

    }
}