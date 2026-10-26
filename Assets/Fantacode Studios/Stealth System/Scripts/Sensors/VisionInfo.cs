using UnityEngine;

namespace FS_StealthSystem {

    public enum VisionZone { None, Primary, Peripheral }

    public class VisionInfo
    {
        public VisionZone Zone { get; set; }
        public VisualStimulus Stimulus { get; set; }
        public bool IsHidden { get; set; }
    }
}
