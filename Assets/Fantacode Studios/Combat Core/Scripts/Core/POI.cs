using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_CombatCore
{
    public class POI
    {
        public Vector3 Position { get; set; }
        public int Priority { get; set; }
        public bool ShouldAlert { get; set; }
        public POIType Type { get; set; }
    }

    public enum POIType { Visual, Audio }
}
