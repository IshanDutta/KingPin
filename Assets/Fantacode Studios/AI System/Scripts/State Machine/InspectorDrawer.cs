#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;


namespace FS_Util
{
    public static class InspectorDrawer
    {
        /// <summary>
        /// Draws all serialized fields of the given target (UnityEngine.Object)
        /// using proper heights, indentation and a dynamic label / field ratio
        /// based on EditorGUIUtility.currentViewWidth.
        /// </summary>
        /// <param name="target">ScriptableObject / MonoBehaviour / any UnityEngine.Object</param>
        /// <param name="labelRatio">Ratio of view width used for label (0..1). Default 0.35 (35%).</param>
        public static void DrawSerializedFields(UnityEngine.Object target, float labelRatio = .35f)
        {
            if (target == null) return;

            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.GetIterator();

            // Save GUI state
            int prevIndent = EditorGUI.indentLevel;
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            bool prevWideMode = EditorGUIUtility.wideMode;

            // Setup nice defaults
            EditorGUIUtility.wideMode = true;
            EditorGUIUtility.labelWidth = Mathf.Clamp(
                EditorGUIUtility.currentViewWidth * Mathf.Clamp01(labelRatio),
                120f,
                200f
            );

            bool enterChildren = true;
            EditorGUILayout.BeginVertical(GUIStyle.none);
            GUILayout.Space(4);

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip the script reference
                if (prop.name == "m_Script")
                    continue;

                if (!ShouldShow(prop, target))
                    continue;

                // Respect nesting depth for indent
                EditorGUI.indentLevel = prop.depth;

                // Proper height calculation so complex types align correctly
                float h = EditorGUI.GetPropertyHeight(prop, true);
                Rect r = EditorGUILayout.GetControlRect(true, h);

                EditorGUI.PropertyField(r, prop, true);
            }

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            // Apply changes and restore GUI state
            so.ApplyModifiedProperties();
            EditorGUI.indentLevel = prevIndent;
            EditorGUIUtility.labelWidth = prevLabelWidth;
            EditorGUIUtility.wideMode = prevWideMode;
        }

        static bool ShouldShow(SerializedProperty property, UnityEngine.Object target)
        {
            FieldInfo backingField = target.GetType()
                .GetField(property.name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (backingField == null)
                return true;

            // Find an attribute whose type name is "ShowIfAttribute"
            var showIfAttr = backingField.GetCustomAttributes(true)
                .FirstOrDefault(a => a.GetType().Name == "ShowIfAttribute");

            if (showIfAttr == null)
                return true;

            var showIf = showIfAttr as ShowIfAttribute;

            return ShowIfDrawer.ShouldShow(property, showIf);
        }
    }
}
#endif