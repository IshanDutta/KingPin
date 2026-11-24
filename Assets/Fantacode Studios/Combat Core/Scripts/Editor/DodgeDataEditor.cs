using System.Collections;
using System.Collections.Generic;
using UnityEngine;


using UnityEditor;

#if UNITY_EDITOR
namespace FS_CombatCore
{


    [CustomPropertyDrawer(typeof(DodgeData))]
    public class DodgeDataEditor : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty clip = property.FindPropertyRelative("clipInfo");
            SerializedProperty defaultDirection = property.FindPropertyRelative("defaultDirection");
            SerializedProperty useDifferentClipsForDirections = property.FindPropertyRelative("useDifferentClipsForDirections");

            SerializedProperty frontClip = property.FindPropertyRelative("frontClipInfo");
            SerializedProperty backClip = property.FindPropertyRelative("backClipInfo");
            SerializedProperty leftClip = property.FindPropertyRelative("leftClipInfo");
            SerializedProperty rightClip = property.FindPropertyRelative("rightClipInfo");

            if (!useDifferentClipsForDirections.boolValue)
                EditorGUILayout.PropertyField(clip);

            EditorGUILayout.PropertyField(defaultDirection);
            EditorGUILayout.PropertyField(useDifferentClipsForDirections);
            if (useDifferentClipsForDirections.boolValue)
            {
                EditorGUILayout.PropertyField(frontClip);
                EditorGUILayout.PropertyField(backClip);
                EditorGUILayout.PropertyField(leftClip);
                EditorGUILayout.PropertyField(rightClip);
            }
        }
    }
}

#endif
