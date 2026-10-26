#if UNITY_EDITOR
using UnityEditor;

namespace FS_Core
{
    [CustomEditor(typeof(HierarchicalStateAsset))]
    public class HierarchicalStateAssetInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            HierarchicalStateBehaviorEditorHelper.DrawBehaviorSection((HierarchicalStateAsset)target);
            StateMachineEditorWindow.DrawStateTransitionOrderList((HierarchicalStateAsset)target);
        }
    }
}
#endif
