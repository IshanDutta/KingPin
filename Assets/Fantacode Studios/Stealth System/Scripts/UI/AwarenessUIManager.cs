using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FS_StealthSystem
{
    public class AwarenessUIManager : MonoBehaviour
    {
        [Tooltip("Detection meter prefab instantiated for each registered AI.")]
        [SerializeField] DetectionMeterUI detectionMeterUIPrefab;
        [Tooltip("Camera used to map AI directions to on-screen meter positions.")]
        [SerializeField] Camera playerCamera;
        [Tooltip("Canvas rect the detection meters are parented under.")]
        [SerializeField] RectTransform screenCanvas;
        [Tooltip("Seconds the meter stays visible after the AI becomes fully aware.")]
        [SerializeField] float hideDelayAfterDetected = 1.5f;

        public static AwarenessUIManager i { get; private set; }
        private void Awake()
        {
            i = this;
        }


        Dictionary<AwarenessSystem, DetectionMeterUI> detectionMeters = new Dictionary<AwarenessSystem, DetectionMeterUI>();
        Dictionary<AwarenessSystem, float> detectedTimers = new Dictionary<AwarenessSystem, float>();

        public void RegisterCharacter(AwarenessSystem awarenessSystem)
        {
            if (!detectionMeters.ContainsKey(awarenessSystem))
            {
                var meterUI = Instantiate(detectionMeterUIPrefab, screenCanvas);
                detectionMeters.Add(awarenessSystem, meterUI);
                meterUI.gameObject.SetActive(false);
                meterUI.AwarenessSystem = awarenessSystem;
            }
        }

        private void Update()
        {
            foreach (var kvp in detectionMeters)
            {
                var awarenessSystem = kvp.Key;
                var meterUI = kvp.Value;

                if (awarenessSystem.IsAware)
                {
                    // Start or continue the hide timer once fully detected
                    if (!detectedTimers.ContainsKey(awarenessSystem))
                        detectedTimers[awarenessSystem] = 0f;

                    detectedTimers[awarenessSystem] += Time.deltaTime;

                    if (detectedTimers[awarenessSystem] >= hideDelayAfterDetected)
                    {
                        if (meterUI.gameObject.activeSelf)
                            meterUI.gameObject.SetActive(false);
                        continue;
                    }
                }
                else
                {
                    detectedTimers.Remove(awarenessSystem);
                }

                // Don't reactivate the meter during combat once it's been hidden
                if (!meterUI.gameObject.activeSelf
                    && StealthAIManager.Instance.HighestSituationType == SituationType.Combat)
                    continue;

                if (awarenessSystem.CurrentAwareness > 0)
                {
                    if (!meterUI.gameObject.activeSelf)
                        meterUI.gameObject.SetActive(true);

                    UpdateMeterPosition(awarenessSystem, meterUI);
                }
                else
                {
                    if (meterUI.gameObject.activeSelf)
                        meterUI.gameObject.SetActive(false);
                }
            }
        }

        void UpdateMeterPosition(AwarenessSystem awarenessSystem, DetectionMeterUI meter)
        {
            Vector3 toEnemy = (awarenessSystem.transform.position - playerCamera.transform.position).normalized;

            // Transform to local space relative to camera
            Vector3 localDir = playerCamera.transform.InverseTransformDirection(toEnemy);

            // Position on screen (normalized direction mapped to screen size)
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Vector2 dir2D = new Vector2(localDir.x, localDir.z).normalized; // horizontal plane

            // Offset from screen center
            float maxOffset = Mathf.Min(Screen.width, Screen.height) / 2f * 0.8f; // keep inside screen
            Vector2 screenPos = screenCenter + dir2D * maxOffset;

            meter.RectTransform.position = screenPos;

            // Rotation: point arrow toward enemy
            float angle = Mathf.Atan2(dir2D.y, dir2D.x) * Mathf.Rad2Deg;
            meter.RectTransform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        }
    }
}
