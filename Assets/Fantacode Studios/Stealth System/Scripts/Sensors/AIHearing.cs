using FS_Util;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace FS_StealthSystem
{
    public class AIHearing : MonoBehaviour
    {
        [Tooltip("Measure how far a sound travelled along the NavMesh path instead of in a straight line, so sounds from behind walls must travel further to be heard.")]
        [SerializeField] bool useNavMeshDistance = false;
        [Tooltip("Fade a sound's strength with distance and drop it entirely below the hearing threshold; when off, anything inside the sound's radius is heard at full strength.")]
        [SerializeField] bool useDistanceAttenuation = false;

        [ShowIf("useDistanceAttenuation", true)]
        [Tooltip("Attenuated loudness (0-1) a sound must exceed to be heard; raise it so distant or quiet sounds go unnoticed.")]
        [SerializeField] float minHearingThreshold = 0.2f;

        StealthAIController stealthAI;
        private void Awake()
        {
            stealthAI = GetComponent<StealthAIController>();
        }

        public void Hear(Stimulus audioStimulus, float radius, float baseLoudness=1f)
        {
            if (!enabled) return;

            float distance;
            if (useNavMeshDistance)
                distance = NavMeshUtil.GetNavMeshDistance(transform.position, audioStimulus.position);
            else
                distance = Vector3.Distance(transform.position, audioStimulus.position);

            if (useDistanceAttenuation)
            {
                float distanceAtten = 1f - Mathf.Pow(distance / radius, 2f);
                if (distanceAtten <= 0f)
                    return;

                float strength = baseLoudness * distanceAtten;
                if (strength <= minHearingThreshold)
                    return;
            }

            stealthAI.HandleAudio(audioStimulus);
        }

    }
}
