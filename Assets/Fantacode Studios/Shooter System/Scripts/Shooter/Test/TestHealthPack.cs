using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class TestHealthPack : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            var damagable = other.GetComponent<Damagable>();

            if (damagable != null)
            {
                damagable?.UpdateHealth(25);
                Destroy(gameObject);
            }
        }
    }
}
