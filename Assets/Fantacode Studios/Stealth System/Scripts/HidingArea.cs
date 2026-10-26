using System.Collections.Generic;
using FS_Core;
using FS_LocomotionSystem;
using UnityEngine;

namespace FS_StealthSystem
{
    public class HidingArea : MonoBehaviour
    {
        [Tooltip("If true, the player must also be crouching inside the area to count as hidden.")]
        [SerializeField] bool requiresCrouch = true;

        static readonly HashSet<HidingArea> activeAreas = new();

        VisualStimulus trackedStimulus;
        LocomotionController cachedLocomotion;

        void OnEnable() => activeAreas.Add(this);
        void OnDisable() => activeAreas.Remove(this);

        void OnTriggerEnter(Collider other)
        {
            var stimulus = other.GetComponent<VisualStimulus>();
            if (stimulus == null || stimulus.Type != VisualStimulusType.Player) return;

            trackedStimulus = stimulus;
            cachedLocomotion = stimulus.GetComponent<LocomotionController>();
        }

        void OnTriggerExit(Collider other)
        {
            var stimulus = other.GetComponent<VisualStimulus>();
            if (stimulus == null || stimulus != trackedStimulus) return;

            trackedStimulus = null;
            cachedLocomotion = null;
        }

        bool ContainsAndHidden(VisualStimulus stimulus)
        {
            if (trackedStimulus != stimulus) return false;
            if (!requiresCrouch) return true;
            return cachedLocomotion != null && cachedLocomotion.IsCrouching;
        }

        public static bool IsHidden(VisualStimulus stimulus)
        {
            foreach (var area in activeAreas)
            {
                if (area.ContainsAndHidden(stimulus))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the stimulus is inside any hiding area, regardless of crouch state.
        /// Used by AI to know the player entered a hiding spot (even if not yet fully hidden).
        /// </summary>
        public static bool IsInsideAnyArea(VisualStimulus stimulus)
        {
            foreach (var area in activeAreas)
            {
                if (area.trackedStimulus == stimulus)
                    return true;
            }
            return false;
        }
    }
}
