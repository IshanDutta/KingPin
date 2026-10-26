using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    public class StealthSoundHandler : MonoBehaviour
    {
        [Tooltip("Maps each sound type to the priority its stimulus is given. Sound types not listed here default to Low.")]
        [SerializeField]
        List<SoundPriorityMapping> soundPriorities = new List<SoundPriorityMapping>()
        {
            new SoundPriorityMapping() { soundType = GameSoundType.Gunshot, priority = StimulusPriority.High },
            new SoundPriorityMapping() { soundType = GameSoundType.Footstep, priority = StimulusPriority.Low },
            new SoundPriorityMapping() { soundType = GameSoundType.ThrowImpact, priority = StimulusPriority.Low },
            new SoundPriorityMapping() { soundType = GameSoundType.Explosion, priority = StimulusPriority.High },
        };

        private void OnEnable()
        {
            GameSoundEvents.OnSoundEmitted += HandleSound;
        }

        private void OnDisable()
        {
            GameSoundEvents.OnSoundEmitted -= HandleSound;
        }

        Collider[] results = new Collider[128];
        void HandleSound(GameSoundEvent evt)
        {
            var stimulus = new Stimulus()
            {
                position = evt.position,
                priority = MapPriority(evt.type),
                modality = StimulusModality.Audio,
                soundType = evt.type,
                source = evt.source
            };

            int count = Physics.OverlapSphereNonAlloc(evt.position, evt.radius, results, StealthSettings.i.HearingLayer, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                var aiHearing = results[i].GetComponentInParent<AIHearing>();
                if (aiHearing == null)
                    continue;

                aiHearing.Hear(stimulus, evt.radius, evt.baseLoudness);
            }
        }

        private StimulusPriority MapPriority(GameSoundType type)
        {
            foreach (var mapping in soundPriorities)
            {
                if (mapping.soundType == type)
                    return mapping.priority;
            }

            return StimulusPriority.Low;
        }
    }

    [System.Serializable]
    public class SoundPriorityMapping
    {
        [Tooltip("Sound type this mapping applies to.")]
        public GameSoundType soundType;
        [Tooltip("Priority given to stimuli created from sounds of this type.")]
        public StimulusPriority priority;
    }
}
