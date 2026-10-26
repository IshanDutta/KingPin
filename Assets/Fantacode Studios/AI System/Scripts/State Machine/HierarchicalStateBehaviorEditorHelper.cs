#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FS_Core
{
    public static class HierarchicalStateBehaviorEditorHelper
    {
        public static void DrawBehaviorSection(HierarchicalStateAsset asset)
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);

            var current = asset.BehaviorAsset;
            if (current == null)
            {
                if (GUILayout.Button("Add Behavior"))
                    ShowAddBehaviorMenu(asset);
            }
            else
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(current.DisplayName, EditorStyles.miniBoldLabel);
                    var editor = UnityEditor.Editor.CreateEditor(current);
                    editor.OnInspectorGUI();
                    GUILayout.Space(4);
                    if (GUILayout.Button("Remove Behavior"))
                        RemoveBehavior(asset, current);
                }
            }
        }

        static void ShowAddBehaviorMenu(HierarchicalStateAsset asset)
        {
            var menu = new GenericMenu();
            var types = TypeCache.GetTypesDerivedFrom<HierarchicalStateBehaviorAsset>();
            foreach (var t in types)
            {
                if (t.IsAbstract) continue;
                menu.AddItem(new GUIContent(t.Name), false, () => AddBehavior(asset, t));
            }
            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new GUIContent("No behavior types found"));
            menu.ShowAsContext();
        }

        static void AddBehavior(HierarchicalStateAsset asset, System.Type behaviorType)
        {
            var newBehavior = (HierarchicalStateBehaviorAsset)ScriptableObject.CreateInstance(behaviorType);
            newBehavior.name = behaviorType.Name;
            newBehavior.hideFlags = HideFlags.HideInHierarchy;

            // Add as sub-asset of the parent main asset (the StateMachineAsset).
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                Object.DestroyImmediate(newBehavior);
                Debug.LogError("HierarchicalStateAsset is not saved on disk.");
                return;
            }
            var parent = AssetDatabase.LoadMainAssetAtPath(path);
            AssetDatabase.AddObjectToAsset(newBehavior, parent);

            Undo.RegisterCreatedObjectUndo(newBehavior, "Add State Behavior");
            Undo.RecordObject(asset, "Assign State Behavior");
            asset.SetBehaviorAsset(newBehavior);
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(parent);
            AssetDatabase.SaveAssets();
        }

        static void RemoveBehavior(HierarchicalStateAsset asset, HierarchicalStateBehaviorAsset behavior)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            var parent = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);

            Undo.RecordObject(asset, "Remove State Behavior");
            asset.SetBehaviorAsset(null);

            Undo.DestroyObjectImmediate(behavior);

            EditorUtility.SetDirty(asset);
            if (parent != null) EditorUtility.SetDirty(parent);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
