using FS_Core;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class ShooterSettings : MonoBehaviour
    {
        public static ShooterSettings instance;

        [Tooltip("The movement speed while aiming and walking.")]
        public float aimWalkSpeed = 2.5f;

        [Tooltip("The movement speed while aiming and running.")]
        public float aimRunSpeed = 4.5f;

        [Tooltip("Time in seconds before the hit effect disappears.")]
        public float hitEffectLifetime = 2f;

        [Tooltip("Layer mask used to detect valid hit bones on characters.")]
        [HideInInspector]
        public LayerMask hitBoneMask;

        [Tooltip("Layer mask used to ignore certain objects when raycasting hits.")]
        public LayerMask hitIgnoreMask;

        [Tooltip("Layer mask used to ignore certain objects from explosion range detection.")]
        public LayerMask explosionIgnoreMask;

        [Tooltip("If enabled, explosion damage will affect all layers regardless of the fighter's target layers.")]
        public bool explosionAffectAllLayers = true;



        PlayerController player;

        private void Awake()
        {
            hitBoneMask = LayerMask.GetMask("RagdollBone");
            hitIgnoreMask += 1 << LayerMask.NameToLayer("OcuppantObject");


            instance = this;
            player = GetComponentInChildren<PlayerController>();

            IgnorePlayerAndHitBoneCollisions();
        }

        void IgnorePlayerAndHitBoneCollisions()
        {
            int playerLayer = player.gameObject.layer;
            Physics.IgnoreLayerCollision(LayerMask.NameToLayer("Player"), LayerMask.NameToLayer("RagdollBone"), true);
            Physics.IgnoreLayerCollision(LayerMask.NameToLayer("Enemy"), LayerMask.NameToLayer("RagdollBone"), true);
            //Physics.IgnoreLayerCollision(LayerMask.NameToLayer("Player"), LayerMask.NameToLayer("Enemy"), true);

            //for (int i = 0; i < 32; i++) // Unity supports 32 layers
            //{
            //    if ((hitBoneMask.value & (1 << i)) != 0)
            //    {
            //        Physics.IgnoreLayerCollision(playerLayer, i, true);
            //    }
            //}
        }
    }

}
