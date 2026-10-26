using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    public enum StimulusPriority { Low, High, Critical }

    public enum StimulusModality { Visual, Audio, Damage, Touch }

    public class Stimulus
    {
        public StimulusModality modality;
        public StimulusPriority priority;

        public float importance;    // normalized between zero and one

        public Vector3 position;
        public GameObject source;

        public GameSoundType soundType;

        public bool resolved = false;

        public int TypePriority => (int)priority;
    }
}
