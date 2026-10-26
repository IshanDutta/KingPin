using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    public class DetectionMeterUI : MonoBehaviour
    {
        [Tooltip("Fill image scaled horizontally to show the current awareness (0-1).")]
        [SerializeField] Image detectionFill;
        [Tooltip("Fill color while the AI is still detecting the player.")]
        [SerializeField] Color defaultColor = Color.white;
        [Tooltip("Fill color once the AI is fully aware.")]
        [SerializeField] Color detectedColor = Color.red;

        public AwarenessSystem AwarenessSystem { get; set; }

        private RectTransform _rectTransform;
        public RectTransform RectTransform {
            get {
                if (_rectTransform == null)
                    _rectTransform = GetComponent<RectTransform>();

                return _rectTransform;
            }
        }

        private void Update()
        {
            if (gameObject.activeSelf && AwarenessSystem != null)
            {
                float awareness = AwarenessSystem.CurrentAwareness;
                detectionFill.transform.localScale = new Vector3(awareness, 1, 1);

                if (AwarenessSystem.IsAware)
                    detectionFill.color = detectedColor;
                else
                    detectionFill.color = defaultColor;
            } 
        }
    }
}
