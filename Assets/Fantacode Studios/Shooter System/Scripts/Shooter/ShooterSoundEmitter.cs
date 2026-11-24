using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_ShooterSystem
{
    using FS_CombatCore;
    using FS_Core;
    using FS_Util;

    /// <summary>
    /// Emits a sound alert to nearby enemies whenever the ShooterFighter fires a weapon.
    /// Attach this component to the same GameObject as ShooterFighter.
    /// </summary>
    [RequireComponent(typeof(ShooterFighter))]
    public class ShootSoundEmitter : MonoBehaviour
    {
        [Tooltip("If enabled, nearby enemies will be direclty alerted when this fighter fires. Disable it if you want the enemies to be only alerted through the stealth system")]
        [SerializeField] bool alertEnemies = true;

        // Optional: Increase if you expect more enemies in range
        private static readonly Collider[] _enemyBuffer = new Collider[128];

        ShooterFighter shooterFighter;
        FighterCore fighterCore;

        void Awake()
        {
            shooterFighter = GetComponent<ShooterFighter>();
            fighterCore = GetComponent<FighterCore>();

            if (shooterFighter != null)
            {
                shooterFighter.OnFire += AlertEnemiesOnFire;
                shooterFighter.OnAmmoExploded += AlertEnemiesOnExplosion;
            }
        }

        void OnDestroy()
        {
            if (shooterFighter != null)
            {
                shooterFighter.OnFire -= AlertEnemiesOnFire;
            }
        }

        /// <summary>
        /// Called when the player fires a weapon. Alerts nearby enemies within the weapon's sound radius.
        /// </summary>
        void AlertEnemiesOnFire()
        {
            var weapon = shooterFighter.CurrentWeapon as ShooterWeapon;
            if (weapon == null) return;

            GameSoundEvents.Emit(new GameSoundEvent
            {
                position = transform.position,
                radius = weapon.soundRange,
                type = GameSoundType.Gunshot
            });

            if (alertEnemies)
                AlertEnemies(weapon.soundRange, transform.position);
        }

        void AlertEnemiesOnExplosion(ShooterAmmoObject ammoObject)
        {
            if (ammoObject == null) return;

            GameSoundEvents.Emit(new GameSoundEvent
            {
                position = transform.position,
                radius = ammoObject.Ammo.explosionSoundRange,
                type = GameSoundType.Gunshot
            });

            if (alertEnemies)
                AlertEnemies(ammoObject.Ammo.explosionSoundRange, ammoObject.transform.position);
        }

        void AlertEnemies(float soundRadius, Vector3 origin)
        {
            if (soundRadius <= 0f) return;

            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                soundRadius,
                _enemyBuffer,
                fighterCore.targetLayer,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < hitCount; i++)
            {
                var hit = _enemyBuffer[i];
                if (hit == null) continue;

                var combatAI = hit.GetComponent<CombatAIController>();
                if (combatAI != null && combatAI.Fighter.Target == null)
                {
                    StartCoroutine(AsyncUtil.RunAfterDelay(0.5f, () => combatAI.Fighter.Target = fighterCore));
                }
            }
        }
    }
}
