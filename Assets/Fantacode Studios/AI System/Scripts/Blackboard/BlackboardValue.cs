using System;
using FS_Util;
using UnityEngine;

namespace FS_Core
{
    [Serializable]
    public abstract class BlackboardValue<T>
    {
        [SerializeField] protected T defaultValue;
        [SerializeField] bool useBlackboard;
        [ShowIf("useBlackboard", true)]
        [SerializeField] BlackboardKey blackboardKey;

        protected abstract BlackboardValueType ValueType { get; }

        public BlackboardValue()
        {
            blackboardKey = new BlackboardKey(ValueType);
        }

        public BlackboardValue(T defaultValue) : this()
        {
            this.defaultValue = defaultValue;
        }

        public T GetValue(Blackboard blackboard)
        {
            if (useBlackboard && blackboard != null
                && blackboard.TryGet<T>(blackboardKey.Name, out T value))
                return value;
            return defaultValue;
        }
    }

    [Serializable]
    public class BlackboardFloat : BlackboardValue<float>
    {
        protected override BlackboardValueType ValueType => BlackboardValueType.Float;
        public BlackboardFloat() { }
        public BlackboardFloat(float defaultValue) : base(defaultValue) { }
    }

    [Serializable]
    public class BlackboardBool : BlackboardValue<bool>
    {
        protected override BlackboardValueType ValueType => BlackboardValueType.Bool;
        public BlackboardBool() { }
        public BlackboardBool(bool defaultValue) : base(defaultValue) { }
    }

    [Serializable]
    public class BlackboardInt : BlackboardValue<int>
    {
        protected override BlackboardValueType ValueType => BlackboardValueType.Int;
        public BlackboardInt() { }
        public BlackboardInt(int defaultValue) : base(defaultValue) { }
    }

    [Serializable]
    public class BlackboardString : BlackboardValue<string>
    {
        protected override BlackboardValueType ValueType => BlackboardValueType.String;
        public BlackboardString() { }
        public BlackboardString(string defaultValue) : base(defaultValue) { }
    }

    [Serializable]
    public class BlackboardVector3 : BlackboardValue<Vector3>
    {
        protected override BlackboardValueType ValueType => BlackboardValueType.Vector3;
        public BlackboardVector3() { }
        public BlackboardVector3(Vector3 defaultValue) : base(defaultValue) { }
    }

    [Serializable]
    public class BlackboardTransform : BlackboardValue<Transform>
    {
        protected override BlackboardValueType ValueType => BlackboardValueType.Transform;
        public BlackboardTransform() { }
    }
}
