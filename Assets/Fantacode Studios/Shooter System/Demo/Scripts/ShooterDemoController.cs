using FS_CombatCore;
using System.Collections.Generic;
using UnityEngine;
namespace FS_ShooterSystem
{
    public class ShooterDemoController : MonoBehaviour
    {
        [SerializeField] List<GameObject> coverObjects = new List<GameObject>();
        private void Start()
        {
            ShooterSettings.instance.hitIgnoreMask = LayerMask.GetMask("Player", "Enemy");

            foreach (GameObject cover in coverObjects)
            {
                cover.layer = LayerMask.NameToLayer("Cover");
            }
        }
    }
}
