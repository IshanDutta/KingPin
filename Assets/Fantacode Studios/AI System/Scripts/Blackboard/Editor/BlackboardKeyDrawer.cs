using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    [CustomPropertyDrawer(typeof(BlackboardKey))]
    public class BlackBoardKeyDrawer : PropertyDrawer
    {
        static BlackBoardkeys parameterContainer;
        public static BlackBoardkeys ParameterContainer {
            get {
                if (parameterContainer == null)
                {
                    parameterContainer = Resources.Load("Data/BlackboardKeyContainer") as BlackBoardkeys;
                }
                return parameterContainer;
            }
        }

        const float AddButtonWidth = 24f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var blackBoardValueTypeIndex = property.FindPropertyRelative("type").enumValueIndex;
            var blackBoardValueType = (BlackboardValueType)blackBoardValueTypeIndex;
            var paramIDProp = property.FindPropertyRelative("id");

            var popupRect = new Rect(position.x, position.y, position.width - AddButtonWidth - 2f, position.height);
            var buttonRect = new Rect(position.xMax - AddButtonWidth, position.y, AddButtonWidth, position.height);

            var paramNameProp = property.FindPropertyRelative("name");
            paramNameProp.stringValue = DrawParameterPopup(popupRect, paramIDProp, blackBoardValueType, label.text);

            if (GUI.Button(buttonRect, new GUIContent("+", "Add new Blackboard Key")))
                BlackboardKeyEditorWindow.Open();
        }

        public string DrawParameterPopup(Rect rect, SerializedProperty paramIDProp, BlackboardValueType parameterType, string labelText = "Key")
        {
            var container = ParameterContainer;

            if (container == null)
            {
                EditorGUI.HelpBox(rect, "No BlackboardKeyContainer found.", MessageType.Warning);
                return "";
            }

            var parameters = container.Keys
                                      .Where(p => p.Type == parameterType)
                                      .ToList();

            if (parameters.Count == 0)
            {
                EditorGUI.HelpBox(rect, "No Parameter Available", MessageType.Info);
                return "";
            }

            string[] names = parameters.Select(p => p.Name).ToArray();

            int currentIndex = parameters.FindIndex(p => p.Id == paramIDProp.stringValue);

            // If missing (deleted param), show first but DON'T overwrite until user changes
            if (currentIndex < 0)
                currentIndex = 0;

            EditorGUI.BeginChangeCheck();

            int newIndex = EditorGUI.Popup(rect, labelText, currentIndex, names);

            if (EditorGUI.EndChangeCheck())
            {
                paramIDProp.serializedObject.Update();
                paramIDProp.stringValue = parameters[newIndex].Id;

                paramIDProp.serializedObject.ApplyModifiedProperties();
                return newIndex >= 0 ? parameters[newIndex].Name : "";
            }
            return newIndex >= 0 ? parameters[currentIndex].Name : "";
        }
    }
}
