using UnityEngine;

namespace FS_Core
{
    public class AIManager : MonoBehaviour
    {
        public static AIManager Instance { get; private set; }

        /// <summary>
        /// Last known position of the target (e.g. player), shared across all AI.
        /// Updated by any AI that currently sees the target.
        /// </summary>
        public Vector3 LastKnownTargetPos { get; set; }

        [Tooltip("How far past a stimulus position an AI sweeps when checking it out. A position where a target was lost (or attacked from) sits at the edge of an occluder, so the hiding pocket is just beyond it — stopping exactly on the position leaves that pocket unchecked. Shared by every system that verifies a position (shooter flank, stealth hit response).")]
        [SerializeField] float stimulusOvershootDistance = 2f;
        public float StimulusOvershootDistance => stimulusOvershootDistance;

        void Awake()
        {
            Instance = this;
        }
    }
}
