using FS_CombatCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class SetShooterLayersForDemo : MonoBehaviour
    {
        [SerializeField] FighterCore player;
        [SerializeField] List<FighterCore> enemies;
        [SerializeField] List<GameObject> covers;

        private void Start()
        {
            player.gameObject.layer = LayerMask.NameToLayer("Player");
            player.targetLayer = LayerMask.GetMask("Enemy");

            foreach (var enemy in enemies)
            {
                enemy.gameObject.layer = LayerMask.NameToLayer("Enemy");
                enemy.targetLayer = LayerMask.GetMask("Player");
            }
            foreach (var cover in covers)
            {
                cover.layer = LayerMask.NameToLayer("Cover");
            }

            ShooterSettings.instance.hitIgnoreMask = LayerMask.GetMask("Player", "Enemy");
        }
    }
}
