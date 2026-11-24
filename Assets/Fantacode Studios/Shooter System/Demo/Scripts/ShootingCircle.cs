using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace FS_ShooterSystem
{
    public class ShootingCircle : MonoBehaviour
    {
        [Header("Movement Settings")]
        [Tooltip("Enable ping-pong movement between two points.")]
        public bool usePingPong = false;

        [Tooltip("Start and end points for movement.")]
        public Transform pointA;
        public Transform pointB;
        public Transform objectToMove;

        [Tooltip("Movement speed between points.")]
        public float moveSpeed = .3f;

        public Damagable damagable;

        private void Start()
        {
            damagable = GetComponentInChildren<Damagable>();
            damagable.OnDead += Dead;
        }

        void Update()
        {
            if (usePingPong && pointA && pointB)
            {
                float t = Mathf.PingPong(Time.time * moveSpeed, 1f);
                objectToMove.transform.position = Vector3.Lerp(pointA.position, pointB.position, t);
            }
        }

        void Dead()
        {
            StartCoroutine(RotateObjectX(-90f));
            Invoke("ResetObject", 3);
        }

        void ResetObject()
        {
            StartCoroutine(RotateObjectX(0f));
        }

        IEnumerator RotateObjectX(float targetAngle)
        {
            float duration = 0.5f;
            float time = 0f;

            Quaternion startRotation = objectToMove.localRotation;
            Quaternion targetRotation = Quaternion.Euler(targetAngle, objectToMove.localEulerAngles.y, objectToMove.localEulerAngles.z);

            while (time < duration)
            {
                time += Time.deltaTime;
                float t = time / duration;
                objectToMove.localRotation = Quaternion.Lerp(startRotation, targetRotation, t);
                yield return null;
            }

            objectToMove.localRotation = targetRotation;
        }

    }
}