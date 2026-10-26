#if UNITY_EDITOR
using UnityEditor;

namespace FS_Core
{
    [CustomEditor(typeof(StateAsset), true)]
    [CanEditMultipleObjects]
    public class StateAssetInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length == 1)
                StateMachineEditorWindow.DrawStateTransitionOrderList((StateAsset)target);
        }
    }
}
#endif
