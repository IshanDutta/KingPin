using UnityEngine;

namespace FS_StealthSystem
{
    public class AITouch : MonoBehaviour
    {
        [Tooltip("Radius of the touch trigger. Keep it hugging the body so stealth takedowns can be initiated from outside it.")]
        [SerializeField] float radius = 0.45f;
        [Tooltip("Height of the touch trigger capsule in meters; should roughly match the character's height.")]
        [SerializeField] float height = 1.8f;

        StealthAIController stealthAI;

        private void Start()
        {
            stealthAI = GetComponent<StealthAIController>();

            var touchVolume = Instantiate(StealthSettings.i.TouchVolumePrefab, transform);
            touchVolume.transform.localPosition = Vector3.zero;
            touchVolume.Init(this);

            var capsule = touchVolume.GetComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = Vector3.up * (height * 0.5f);
        }

        public void Touch(VisualStimulus stimulus)
        {
            if (!enabled) return;

            stealthAI.HandleTouch(stimulus);
        }
    }
}
