#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    class AIStateMachineSetupHandler : IAICharacterSetupHandler
    {
        public void OnAICharacterSetup(GameObject aiObject, List<string> selectedSystemNames)
        {
            var aiController = aiObject.GetComponent<AIController>();
            if (aiController == null) return;

            var assetPath = AIStateMachineGenerator.DefaultAssetPathFor(aiObject.name);
            var generated = AIStateMachineGenerator.GenerateForModules(selectedSystemNames, assetPath);
            if (generated == null) return;

            var so = new SerializedObject(aiController);
            so.FindProperty("stateMachineAsset").objectReferenceValue = generated;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(aiController);
        }
    }
}
#endif
