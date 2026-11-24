#if UNITY_EDITOR
using FS_CombatCore;
using FS_ShooterSystem;
using FS_Core;
using FS_CoverSystem;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FS_Core
{
    public partial class FSSystemsSetup
    {
        static FSSystemInfo ShooterSystemSetup = new FSSystemInfo
        (
            characterType: CharacterType.Player,
            selected: false,
            systemName: "Shooter System",
            displayName: "Shooter",
            prefabName: "Shooter Controller",
            welcomeEditorShowKey: "ShooterSystem_WelcomeWindow_Opened",
            uiPrefabNames: new[] { "Shooter UI", "Combat Mobile Controller", CoverMobileControllerPrefab },
            OnInstallation: () => SetCoverLayerToCoverPrefab(),

            extraSetupActionPlayer: (GameObject playerObject, GameObject playerParentObjectPrefab, GameObject createdPlayerParentObject) =>
                {
                    SetShooterSettings(playerParentObjectPrefab, createdPlayerParentObject);
                    SetShooterUI(createdPlayerParentObject);
                    //SetupVisionSensorShooter(playerObject, playerParentObjectPrefab);
                    ShooterInputManagersSetup(playerObject, createdPlayerParentObject);
                }
            );

        static FSSystemInfo ShooterSystemAISetup = new FSSystemInfo
        (
            characterType: CharacterType.AI,
            selected: false,
            systemName: "Shooter System",
            displayName: "Shooter AI",
            prefabName: "Shooter AI Controller",
            welcomeEditorShowKey: "ShooterSystem_WelcomeWindow_Opened",

            extraSetupActionAI: (GameObject aiObject, GameObject prefabObject) =>
            {
                //SetupVisionSensorShooter(aiObject, prefabObject);
                SetShooterTargetLayer(aiObject, "Player");
            }
        );


        static string ShooterSystemWelcomeEditorKey => ShooterSystemSetup.welcomeEditorShowKey;


        [InitializeOnLoadMethod]
        public static void LoadShooterSystem()
        {
            NotifyNewInstall(ShooterSystemWelcomeEditorKey);
        }

        static void SetShooterSettings(GameObject playerParentObjectPrefab, GameObject createdPlayerParentObject)
        {
            FSSystemsSetupEditorWindow.CopyComponents(playerParentObjectPrefab, createdPlayerParentObject);
            SetShooterTargetLayer(createdPlayerParentObject.GetComponentInChildren<PlayerController>().gameObject, "Enemy");
        }

        static void SetShooterUI(GameObject createdPlayerParentObject)
        {
            var shooterUI = Resources.Load<GameObject>("Shooter UI Canvas");
            if (shooterUI != null)
            {
                var targetUI = Instantiate(shooterUI.gameObject, createdPlayerParentObject.transform);
                targetUI.name = "Shooter UI Canvas";
                var shooterCrosshairController = targetUI.GetComponentInChildren<ShooterCrosshairController>();
                shooterCrosshairController.shooterController = createdPlayerParentObject.GetComponentInChildren<ShooterController>();
            }
        }

        static void SetupVisionSensorShooter(GameObject characterObject, GameObject prefabObject)
        {
            var visionSensor = characterObject.GetComponentInChildren<SimpleVision>();

            if (visionSensor != null) return;

            var visionSensorObj = Resources.Load<GameObject>("Vision Sensor");

            visionSensorObj.transform.SetParent(characterObject.transform);

            if ((visionSensorObj.gameObject.layer != LayerMask.NameToLayer("VisionSensor")))
                visionSensorObj.gameObject.layer = LayerMask.NameToLayer("VisionSensor");
        }

        static void SetShooterTargetLayer(GameObject characterObject, string layerName)
        {
            var fighterCore = characterObject.GetComponent<FighterCore>();
            fighterCore.targetLayer = 1 << LayerMask.NameToLayer(layerName);
        }

        static void ShooterInputManagersSetup(GameObject playerObject, GameObject createdPlayerParentObject)
        {
            var ui = createdPlayerParentObject.transform.Find("Game UI/Shooter UI");
            var shooterUI = ui.GetComponent<ShooterUI>();
            var shooterInputManager = playerObject.GetComponent<ShooterInputManger>();
            if (shooterInputManager != null && shooterUI != null)
                shooterInputManager.SetShooterInputUI(shooterUI);

            var mc = createdPlayerParentObject.transform.Find("Game UI/Combat Mobile Controller");
            var combatInputUI = mc.GetComponent<CombatUI>();
            var combatInputManager = playerObject.GetComponent<CombatInputManger>();
            if (combatInputManager != null && combatInputUI != null)
                combatInputManager.SetCombatInputUI(combatInputUI);

            SetupCoverInputManager(playerObject, createdPlayerParentObject);
        }
    }
}
#endif