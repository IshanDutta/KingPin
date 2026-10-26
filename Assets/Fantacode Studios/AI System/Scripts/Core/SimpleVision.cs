using FS_StealthSystem;
using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FS_Util;

namespace FS_Core
{
    public class SimpleVision : MonoBehaviour
    {
        [SerializeField] LayerMask targetLayer;
        
        [Tooltip("Field of view angle (in degrees) for AI perception. Determines how wide the AI can see targets.")]
        [SerializeField] public float fov = 180f;
        [SerializeField] float eyeHeight = 1.8f;

        public void SetTargetLayer(LayerMask layer)
        {
            targetLayer = layer;
        }

        public List<Transform> TargetsInRange { get; private set; } = new List<Transform>();

        AIController aiController;
        Damagable damagable;
        private void Awake()
        {
            aiController = GetComponentInParent<AIController>();

            if (aiController != null)
            {
                damagable = aiController.GetComponent<Damagable>();

                gameObject.layer = LayerMask.NameToLayer("VisionSensor");

                // disable vision sensor on death
                damagable.OnDead += () => gameObject.SetActive(false);
            }
        }

        private void Start()
        {
            
        }

        private void FixedUpdate()
        {
            Vector3 eyePosition = transform.position + Vector3.up * eyeHeight;

            var targetsInVision = new List<Transform>();
            foreach (var target in TargetsInRange)
            {
                var vecToTarget = target.transform.position - aiController.transform.position;
                float angle = Vector3.Angle(aiController.transform.forward, vecToTarget);

                if (angle < fov/2 && AIUtil.HasLineOfSight(target, eyePosition))
                {
                    targetsInVision.Add(target);
                }
            }

            aiController.SetTargetsInVision(targetsInVision);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (LayersUtil.LayerMaskContainsLayer(targetLayer, other.gameObject.layer))
            {
                TargetsInRange.Add(other.transform);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (LayersUtil.LayerMaskContainsLayer(targetLayer, other.gameObject.layer))
            {
                TargetsInRange.Remove(other.transform);
            }
        }
    }
}
