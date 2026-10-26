using FS_Core;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    [CustomPropertyDrawer(typeof(BlackboardInitValue))]
    public class BlackboardInitValueDrawer : PropertyDrawer
    {
        static BlackBoardkeys parameterContainer;
        static BlackBoardkeys ParameterContainer
        {
            get
            {
                if (parameterContainer == null)
                    parameterContainer = Resources.Load<BlackBoardkeys>("Data/BlackboardKeyContainer");
                return parameterContainer;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float valueHeight = lineHeight;

            var keyIdProp = property.FindPropertyRelative("keyId");
            var container = ParameterContainer;
            if (container != null)
            {
                var key = container.GetParameterByID(keyIdProp.stringValue);
                if (key != null && key.Type == BlackboardValueType.AnimGraphClipInfo)
                    valueHeight = EditorGUI.GetPropertyHeight(property.FindPropertyRelative("animGraphClipInfoValue"));
            }

            return lineHeight + spacing + valueHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var container = ParameterContainer;
            if (container == null)
            {
                EditorGUI.HelpBox(position, "No BlackboardKeyContainer found in Resources/Data.", MessageType.Warning);
                EditorGUI.EndProperty();
                return;
            }

            var keys = container.Keys;
            if (keys == null || keys.Count == 0)
            {
                EditorGUI.HelpBox(position, "No Blackboard keys registered.", MessageType.Info);
                EditorGUI.EndProperty();
                return;
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var keyIdProp = property.FindPropertyRelative("keyId");
            var keyNameProp = property.FindPropertyRelative("keyName");

            // Key dropdown
            string[] names = keys.Select(k => k.Name).ToArray();
            int currentIndex = keys.FindIndex(k => k.Id == keyIdProp.stringValue);
            if (currentIndex < 0) currentIndex = 0;

            var keyRect = new Rect(position.x, position.y, position.width, lineHeight);
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(keyRect, "Key", currentIndex, names);
            if (EditorGUI.EndChangeCheck())
            {
                keyIdProp.stringValue = keys[newIndex].Id;
                keyNameProp.stringValue = keys[newIndex].Name;
            }
            else if (string.IsNullOrEmpty(keyNameProp.stringValue) && currentIndex >= 0)
            {
                keyNameProp.stringValue = keys[currentIndex].Name;
            }

            // Value field based on selected key's type
            var selectedKey = keys[newIndex >= 0 ? newIndex : currentIndex];
            var valueRect = new Rect(position.x, position.y + lineHeight + spacing, position.width, lineHeight);

            switch (selectedKey.Type)
            {
                case BlackboardValueType.Float:
                    EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("floatValue"), new GUIContent("Value"));
                    break;
                case BlackboardValueType.Bool:
                    EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("boolValue"), new GUIContent("Value"));
                    break;
                case BlackboardValueType.Int:
                    EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("intValue"), new GUIContent("Value"));
                    break;
                case BlackboardValueType.String:
                    EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("stringValue"), new GUIContent("Value"));
                    break;
                case BlackboardValueType.Vector2:
                    EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("vector2Value"), new GUIContent("Value"));
                    break;
                case BlackboardValueType.Vector3:
                    EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("vector3Value"), new GUIContent("Value"));
                    break;
                case BlackboardValueType.Transform:
                    EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("transformValue"), new GUIContent("Value"));
                    break;
                case BlackboardValueType.AnimGraphClipInfo:
                    var clipInfoProp = property.FindPropertyRelative("animGraphClipInfoValue");
                    var clipInfoRect = new Rect(position.x, position.y + lineHeight + spacing, position.width, EditorGUI.GetPropertyHeight(clipInfoProp));
                    EditorGUI.PropertyField(clipInfoRect, clipInfoProp, GUIContent.none);
                    break;
            }

            EditorGUI.EndProperty();
        }
    }
}
