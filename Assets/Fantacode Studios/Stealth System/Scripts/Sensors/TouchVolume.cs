using FS_Util;
using UnityEngine;

namespace FS_StealthSystem
{
    public class TouchVolume : MonoBehaviour
    {
        AITouch touchSensor;

        private void Awake()
        {
            gameObject.layer = LayerMask.NameToLayer("VisionSensor");
        }

        public void Init(AITouch touchSensor) => this.touchSensor = touchSensor;

        // Stay instead of Enter so contact that starts while the AI is busy or the player
        // is not yet suspicious still registers once that changes mid-contact
        private void OnTriggerStay(Collider other)
        {
            if (!LayersUtil.LayerMaskContainsLayer(StealthSettings.i.VisualStimulusLayer, other.gameObject.layer))
                return;

            if (other.TryGetComponent<VisualStimulus>(out VisualStimulus stimulus) && stimulus.Type == VisualStimulusType.Player)
                touchSensor.Touch(stimulus);
        }
    }
}
