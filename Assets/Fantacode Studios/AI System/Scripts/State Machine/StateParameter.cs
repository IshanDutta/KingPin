using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    [System.Serializable]
    public abstract class StateParameter
    {
        public bool takeFromBlackboard;
        public string blackboardKey;
    }

    [System.Serializable]
    public class Vector3Parameter : StateParameter
    {
        public Vector3 value;
    }

    [System.Serializable]
    public class FloatParameter : StateParameter
    {
        public float value;
    }

    [System.Serializable]
    public class BoolParameter : StateParameter
    {
        public bool value;
    }
}
