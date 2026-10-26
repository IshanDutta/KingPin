#if UNITY_EDITOR
using FS_StealthSystem;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    public partial class FSSystemsSetup
    {
        static FSSystemInfo StealthSystemSetup = new FSSystemInfo
        (
            characterType: CharacterType.Player,
            selected: false,
            systemName: "Stealth System",
            displayName: "Stealth",
            prefabName: "Stealth Controller",
            welcomeEditorShowKey: "StealthSystem_WelcomeWindow_Opened",
            uiPrefabNames: new[] { CoverMobileControllerPrefab },
            OnInstallation: () => SetCoverLayerToCoverPrefab(),
            extraSetupActionPlayer: (GameObject playerObject, GameObject playerParentObjectPrefab, GameObject createdPlayerParentObject) =>
            {
                FSSystemsSetupEditorWindow.CopyComponents(playerParentObjectPrefab, createdPlayerParentObject);
                SetupAwarenessUIManager(createdPlayerParentObject);
                SetupCoverInputManager(playerObject, createdPlayerParentObject);
            }
        );

        static FSSystemInfo StealthSystemAISetup = new FSSystemInfo
        (
            characterType: CharacterType.AI,
            selected: false,
            systemName: "Stealth System",
            displayName: "Stealth AI",
            prefabName: "Stealth AI Controller",
            welcomeEditorShowKey: "StealthSystem_WelcomeWindow_Opened",
            extraSetupActionAI: (GameObject aiObject, GameObject prefabObject) =>
            {
                RemoveSimpleVisionSensor(aiObject);
            }
        );

        // Stealth perception is driven by AIVision; the base AI's SimpleVision sensor
        // would double-report targets, so remove its GameObject.
        static void RemoveSimpleVisionSensor(GameObject aiObject)
        {
            var simpleVision = aiObject.GetComponentInChildren<SimpleVision>(true);
            if (simpleVision != null)
                Object.DestroyImmediate(simpleVision.gameObject);
        }

        static string StealthSystemWelcomeEditorKey => StealthSystemSetup.welcomeEditorShowKey;

        static void SetupAwarenessUIManager(GameObject createdPlayerParentObject)
        {
            var awarenessUIManager = createdPlayerParentObject.GetComponentInChildren<AwarenessUIManager>();
            if (awarenessUIManager == null) return;

            var serializedObj = new SerializedObject(awarenessUIManager);

            var cameraProp = serializedObj.FindProperty("playerCamera");
            cameraProp.objectReferenceValue = createdPlayerParentObject.GetComponentInChildren<CameraController>().GetComponent<Camera>();

            var canvasProp = serializedObj.FindProperty("screenCanvas");
            var canvas = createdPlayerParentObject.GetComponentInChildren<FS_GameUI>();
            if (canvas != null)
                canvasProp.objectReferenceValue = canvas.GetComponent<RectTransform>();

            serializedObj.ApplyModifiedPropertiesWithoutUndo();
        }

        [InitializeOnLoadMethod]
        public static void LoadStealthSystem()
        {
            NotifyNewInstall(StealthSystemWelcomeEditorKey);
        }
    }
}
#endif
