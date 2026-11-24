#if UNITY_EDITOR

using UnityEditor;

namespace FS_CombatCore
{
    [CustomEditor(typeof(TriggerAuthoredAIBehavior))]
    [CanEditMultipleObjects]
    public class TriggerAuthoredAIBehaviorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
}

#endif