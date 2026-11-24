using FS_CombatCore;
using FS_Core;
using UnityEngine;

namespace FS_ShooterSystem
{

    public class ShooterBoneDamageHandler : MonoBehaviour
    {
        public float damageMultiplier = 1;
        Damagable parentDamagable;

        private void Awake()
        {
            var fighterCore = GetComponentInParent<FighterCore>();

            if (fighterCore != null)
            {
                // Damagable may live on the FighterCore's GameObject, or somewhere else in the
                // character hierarchy. Try both so bone hits always route to the right handler.
                parentDamagable = fighterCore.GetComponent<Damagable>()
                               ?? fighterCore.GetComponentInChildren<Damagable>(true)
                               ?? GetComponentInParent<Damagable>();
                gameObject.layer = LayerMask.NameToLayer("RagdollBone");
            }
        }

        public Damagable ParentDamagable => parentDamagable;
    }
}