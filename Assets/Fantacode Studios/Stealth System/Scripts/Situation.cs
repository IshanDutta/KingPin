using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_StealthSystem
{
    public enum SituationType
    {
        NoSituation = 0,
        SuspiciousInvestigation = 1,
        TrespassingRestrictedArea = 2,
        AlertInvestigation = 3,
        Combat = 4
    }

    [System.Serializable]
    public struct SituationConfig
    {
        [Tooltip("Seconds before the situation ends (for investigations, counted after the stimulus is reached). 0 = no timeout.")]
        public float Duration;

        [Tooltip("Minimum seconds between ongoing barks")]
        public float DialogueCooldown;

        [Tooltip("Only the initial bark plays; no periodic ongoing dialogue")]
        public bool DialogueIsOneShot;

        [Tooltip("All barks come from the situation leader instead of random participants")]
        public bool LeaderSpeaksOnly;

        [Tooltip("Max participants that actively move to the stimulus position")]
        public int MaxInvestigators;

        public SituationConfig(float duration, float dialogueCooldown, bool dialogueIsOneShot,
            int maxInvestigators = int.MaxValue, bool leaderSpeaksOnly = false)
        {
            Duration = duration;
            DialogueCooldown = dialogueCooldown;
            DialogueIsOneShot = dialogueIsOneShot;
            LeaderSpeaksOnly = leaderSpeaksOnly;
            MaxInvestigators = maxInvestigators;
        }
    }

    public class Situation
    {
        public SituationType Type;
        public SituationConfig Config;
        public Vector3 Position;
        public Stimulus OriginStimulus;
        public SuspicionProfile SuspicionProfile;
        public StealthAIController Leader;
        public HashSet<StealthAIController> Participants = new();
        public HashSet<StealthAIController> Investigators = new();
        public float CreatedTime;
        public int CreatedFrame;

        // Stimulus position at the last investigator assignment — used to skip
        // re-evaluation until the stimulus has drifted meaningfully (e.g. footsteps
        // redirect the situation every step)
        public Vector3 LastAssignPosition;
        public float LastUpdatedTime;

        // Last time any AI had LOS on the player (combat only).
        public float LastSpottedTime;
        public bool IsActive = true;
        public bool IsEnding = false;

        // Dialogue coordination
        public float LastDialogueTime = -Mathf.Infinity;
        public int WarningCount;


        /// <summary>
        /// True once any investigator has reached the stimulus source position.
        /// Reset when the situation is redirected to a new position or escalated.
        /// </summary>
        public bool StimulusReached;

        public int Priority => (int)Type;
        public bool HasParticipant(StealthAIController ai) => Participants.Contains(ai);
        public bool IsInvestigator(StealthAIController ai) => Investigators.Contains(ai);

        public void AddParticipant(StealthAIController ai)
        {
            Participants.Add(ai);
            LastUpdatedTime = Time.time;
        }

        public void RemoveParticipant(StealthAIController ai)
        {
            Participants.Remove(ai);
            Investigators.Remove(ai);
            if (Leader == ai && Participants.Count > 0)
                Leader = Participants.First();
        }

        public bool IsInvestigationSituation()
        {
            return Type == SituationType.SuspiciousInvestigation || Type == SituationType.AlertInvestigation;
        }
    }
}
