using System.Collections;
using System.Collections.Generic;
using FS_InteractionSystem;
using UnityEngine;

namespace FS_StealthSystem
{
    public class StealthSettings : MonoBehaviour
    {
        [Tooltip("Layers containing VisualStimulus colliders that AI vision and touch volumes can detect.")]
        [SerializeField] LayerMask visualStimulusLayer;
        [Tooltip("Layers containing AI hearing colliders that emitted sounds are broadcast to.")]
        [SerializeField] LayerMask hearingLayer;

        [Header("References")]
        [Tooltip("Prefab instantiated on each AI to build its vision volumes.")]
        [SerializeField] VisionVolume visionVolumePrefab;
        [Tooltip("Prefab instantiated on each AI to build its touch-detection volume.")]
        [SerializeField] TouchVolume touchVolumePrefab;
        [Tooltip("Visual stimulus prefab attached to a dead AI's hips so other AIs can spot the body.")]
        [SerializeField] VisualStimulus deadBodyPrefab;
        [Tooltip("Hotspot prefab attached to a dead AI's hips that lets the player pick up and carry the body.")]
        [SerializeField] CarryHotspot carryHotspot;

        public static StealthSettings i { get; private set; }
        private void Awake()
        {
            i = this;
        }

        public LayerMask VisualStimulusLayer => visualStimulusLayer;
        public LayerMask HearingLayer => hearingLayer;
        public VisionVolume VisionVolumePrefab => visionVolumePrefab;
        public TouchVolume TouchVolumePrefab => touchVolumePrefab;
        public VisualStimulus DeadBodyPrefab => deadBodyPrefab;
        public CarryHotspot CarryHotspot => carryHotspot;
    }
}
