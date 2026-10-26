using System;
using UnityEngine;

namespace FS_Core
{
    [Serializable]
    public class BlackboardInitValue
    {
        [SerializeField] string keyId;
        [SerializeField] string keyName;

        [SerializeField] float floatValue;
        [SerializeField] bool boolValue;
        [SerializeField] int intValue;
        [SerializeField] string stringValue;
        [SerializeField] Vector2 vector2Value;
        [SerializeField] Vector3 vector3Value;
        [SerializeField] Transform transformValue;
        [SerializeField] AnimGraphClipInfo animGraphClipInfoValue;

        public void Apply(Blackboard blackboard)
        {
            if (string.IsNullOrEmpty(keyId)) return;

            var container = Resources.Load<BlackBoardkeys>("Data/BlackboardKeyContainer");
            if (container == null) return;

            var key = container.GetParameterByID(keyId);
            if (key == null) return;

            switch (key.Type)
            {
                case BlackboardValueType.Float:
                    blackboard.Set(key.Name, floatValue);
                    break;
                case BlackboardValueType.Bool:
                    blackboard.Set(key.Name, boolValue);
                    break;
                case BlackboardValueType.Int:
                    blackboard.Set(key.Name, intValue);
                    break;
                case BlackboardValueType.String:
                    blackboard.Set(key.Name, stringValue);
                    break;
                case BlackboardValueType.Vector2:
                    blackboard.Set(key.Name, vector2Value);
                    break;
                case BlackboardValueType.Vector3:
                    blackboard.Set(key.Name, vector3Value);
                    break;
                case BlackboardValueType.Transform:
                    blackboard.Set(key.Name, transformValue);
                    break;
                case BlackboardValueType.AnimGraphClipInfo:
                    blackboard.Set(key.Name, animGraphClipInfoValue);
                    break;
            }
        }
    }
}
