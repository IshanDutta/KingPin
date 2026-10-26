using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    [CustomEditor(typeof(AIBehavior))]
    public class AIBehaviorEditor : Editor
    {
        SerializedProperty nodesProp;
        SerializedProperty blackboardKeyProp;
        SerializedProperty interruptibleProp;

        void OnEnable()
        {
            nodesProp = serializedObject.FindProperty("nodes");
            blackboardKeyProp = serializedObject.FindProperty("blackboardKey");
            interruptibleProp = serializedObject.FindProperty("interruptible");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("AI Behavior", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(blackboardKeyProp);
            EditorGUILayout.PropertyField(interruptibleProp);
            EditorGUILayout.Space(4);

            for (int i = 0; i < nodesProp.arraySize; i++)
            {
                DrawNode(i);
            }

            EditorGUILayout.Space(4);
            DrawAddNodeButton();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawNode(int index)
        {
            var nodeProp = nodesProp.GetArrayElementAtIndex(index);
            var node = nodeProp.managedReferenceValue as AIBehaviorNode;
            if (node == null) return;

            string nodeTypeName = node.GetType().Name;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.indentLevel++;
            nodeProp.isExpanded = EditorGUILayout.Foldout(nodeProp.isExpanded, $"{index + 1}. {nodeTypeName}", true, EditorStyles.foldout);
            EditorGUI.indentLevel--;

            if (GUILayout.Button("\u25B2", GUILayout.Width(25)) && index > 0)
            {
                nodesProp.MoveArrayElement(index, index - 1);
                serializedObject.ApplyModifiedProperties();
                return;
            }
            if (GUILayout.Button("\u25BC", GUILayout.Width(25)) && index < nodesProp.arraySize - 1)
            {
                nodesProp.MoveArrayElement(index, index + 1);
                serializedObject.ApplyModifiedProperties();
                return;
            }
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                nodesProp.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (nodeProp.isExpanded)
            {
                EditorGUI.indentLevel++;
                DrawNodeFields(nodeProp);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        void DrawNodeFields(SerializedProperty nodeProp)
        {
            var iterator = nodeProp.Copy();
            int parentDepth = iterator.depth;
            bool enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                if (iterator.depth <= parentDepth) break;
                EditorGUILayout.PropertyField(iterator, true);
                enterChildren = false;
            }
        }

        void DrawAddNodeButton()
        {
            if (!GUILayout.Button("Add Node", GUILayout.Height(25))) return;

            var nodeOptions = TypeCache.GetTypesWithAttribute<AINodeModuleAttribute>()
                .Where(t => t.IsSubclassOf(typeof(AIBehaviorNode)) && !t.IsAbstract)
                .Select(t =>
                {
                    var attr = t.GetCustomAttribute<AINodeModuleAttribute>();
                    return (type: t, module: attr.ModuleName, name: attr.DisplayName);
                })
                .OrderBy(e => e.module).ThenBy(e => e.name)
                .ToList();

            var menu = new GenericMenu();
            foreach (var (type, module, name) in nodeOptions)
            {
                var path = string.IsNullOrEmpty(module) ? name : $"{module}/{name}";
                var capturedType = type;
                menu.AddItem(new GUIContent(path), false, () =>
                {
                    var node = Activator.CreateInstance(capturedType) as AIBehaviorNode;
                    if (node != null) AddNode(node);
                });
            }
            menu.ShowAsContext();
        }

        void AddNode(AIBehaviorNode node)
        {
            serializedObject.Update();
            int index = nodesProp.arraySize;
            nodesProp.InsertArrayElementAtIndex(index);
            nodesProp.GetArrayElementAtIndex(index).managedReferenceValue = node;
            serializedObject.ApplyModifiedProperties();
        }
    }
}