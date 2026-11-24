using System.Collections.Generic;
using UnityEngine;

namespace FS_CombatCore
{
    // Used to detect nearyby enemies.
    // Just vision is not enough here, once in combat, the AI should have an idea
    // of the targets they're fighting even if they are behind and not in LOS
    public class CombatTargetSensor : MonoBehaviour
    {
        [SerializeField] float detectionRadius = 6f;

        FighterCore fighter;

        private void Awake()
        {
            fighter = GetComponent<FighterCore>();
        }

        public List<FighterCore> GetTargetsInRange()
        {
            List<FighterCore> targetsInRange = new();

            var colliders = Physics.OverlapSphere(transform.position, detectionRadius, 
                fighter.targetLayer);
            foreach (var collider in colliders)
            {
                var target = collider.GetComponent<FighterCore>();
                if (target != null && target.gameObject.activeSelf && !target.IsDead)
                    targetsInRange.Add(target);
            }

            return targetsInRange;
        }
    }
}