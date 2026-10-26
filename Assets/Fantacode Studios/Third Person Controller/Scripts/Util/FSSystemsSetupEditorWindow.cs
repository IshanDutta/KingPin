
#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;

using FS_Util;

// FIX: Explicitly resolve the AnimatorController ambiguity.
using AnimatorController = UnityEditor.Animations.AnimatorController;

namespace FS_Core
{
    public class FSSystemsSetupEditorWindow : EditorWindow
    {
        // Required by FSSystemsSetup.cs
        public const string inputsystem = "inputsystem";

        private const string CharactersFolder = "Assets/Characters";

        private const string SourceAnimatorResource =
            "AnimatorControllers/Third Person Controller";

        private const string PlayerPrefabResource =
            "Locomotion Controller";

        private const string AIPrefabResource =
            "AI Controller";

        private const string DefaultCharacterResource =
            "Default Character";

        public static FSSystemsSetup setupScript;

        public static CharacterType characterType =
            CharacterType.Player;

        public static GameObject characterModel;

        private Vector2 scrollPosition;

        private string playerAnimatorName = "Combined Controller";
        private string aiAnimatorName = "Combined Controller AI";

        private bool isSettingUp;

        private static FSSystemsSetupEditorWindow window;

        private string AnimatorName =>
            characterType == CharacterType.Player
                ? playerAnimatorName
                : aiAnimatorName;

        // =====================================================
        // WINDOW
        // =====================================================

        [MenuItem("Tools/FS System/ FS Window", false, 0)]
        public static void ShowWindow()
        {
            window = GetWindow<FSSystemsSetupEditorWindow>("FS Systems");
            window.minSize = new Vector2(560f, 550f);

            CreateSetupScript();
            setupScript.FindSystem();

            if (characterType == CharacterType.Player)
            {
                setupScript.EnableSystems(
                    FSSystemsSetup.ThirdPersonControllerSystemSetup.systemName);
            }

            window.Show();
        }

        public static void ChangeCharacterType(CharacterType newType)
        {
            characterType = newType;

            CreateSetupScript();
            setupScript.FindSystem();

            if (characterType == CharacterType.Player)
            {
                setupScript.EnableSystems(
                    FSSystemsSetup.ThirdPersonControllerSystemSetup.systemName);
            }
            else
            {
                setupScript.EnableSystems();
            }

            if (window != null)
                window.Repaint();
        }

        public static void SetTab(int tab)
        {
            if (window != null)
                window.Repaint();
        }

        public static void SetWindowHeight(float height)
        {
            if (window == null)
                return;

            window.minSize = new Vector2(
                560f,
                Mathf.Max(550f, height));
        }

        public static void RequestInstallPopup()
        {
            EditorApplication.delayCall += ShowWindow;
        }

        public static void ResetSetupPopup()
        {
            ShowWindow();
        }

        private void OnEnable()
        {
            if (characterModel == null)
            {
                characterModel =
                    Resources.Load<GameObject>(DefaultCharacterResource);
            }

            CreateSetupScript();
        }

        private void OnDisable()
        {
            if (setupScript != null)
            {
                DestroyImmediate(setupScript.gameObject);
                setupScript = null;
            }
        }

        // =====================================================
        // GUI
        // =====================================================

        private void OnGUI()
        {
            GUILayout.Space(12);

            EditorGUILayout.LabelField(
                "Fantacode Studios",
                EditorStyles.boldLabel);

            GUILayout.Space(8);

            EditorGUILayout.LabelField(
                "Character Setup",
                EditorStyles.largeLabel);

            EditorGUILayout.HelpBox(
                "Create a Fantacode character and save it " +
                "as a prefab in Assets/Characters.",
                MessageType.Info);

            GUILayout.Space(10);

            CreateSetupScript();

            if (setupScript == null)
            {
                EditorGUILayout.HelpBox(
                    "Unable to initialize FSSystemsSetup.",
                    MessageType.Error);
                return;
            }

            DrawCharacterType();

            GUILayout.Space(8);

            DrawCharacterModel();

            GUILayout.Space(8);

            DrawAnimatorName();

            GUILayout.Space(12);

            DrawSystems();

            GUILayout.Space(12);

            DrawButtons();
        }

        private void DrawCharacterType()
        {
            CharacterType[] availableTypes =
                Enum.GetValues(typeof(CharacterType))
                    .Cast<CharacterType>()
                    .Where(t => t != CharacterType.None)
                    .ToArray();

            string[] names =
                availableTypes.Select(t => t.ToString()).ToArray();

            int selectedIndex =
                Array.IndexOf(availableTypes, characterType);

            if (selectedIndex < 0)
                selectedIndex = 0;

            EditorGUI.BeginChangeCheck();

            selectedIndex = EditorGUILayout.Popup(
                "Character Type",
                selectedIndex,
                names);

            if (EditorGUI.EndChangeCheck())
            {
                ChangeCharacterType(availableTypes[selectedIndex]);
            }
        }

        private void DrawCharacterModel()
        {
            characterModel =
                (GameObject)EditorGUILayout.ObjectField(
                    "Character Model",
                    characterModel,
                    typeof(GameObject),
                    true);

            if (characterModel == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a humanoid character model.",
                    MessageType.Warning);
                return;
            }

            Animator animator = characterModel.GetComponent<Animator>();

            if (animator == null)
            {
                EditorGUILayout.HelpBox(
                    "The character model needs an Animator component.",
                    MessageType.Error);
                return;
            }

            if (animator.avatar == null || !animator.avatar.isValid)
            {
                EditorGUILayout.HelpBox(
                    "The character needs a valid Avatar.",
                    MessageType.Error);
                return;
            }

            if (!animator.isHuman)
            {
                EditorGUILayout.HelpBox(
                    "Set the character rig to Humanoid.",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.HelpBox(
                "Character model is valid.",
                MessageType.Info);
        }

        private void DrawAnimatorName()
        {
            if (characterType == CharacterType.Player)
            {
                playerAnimatorName =
                    EditorGUILayout.TextField(
                        "Animator Name",
                        playerAnimatorName);
            }
            else
            {
                aiAnimatorName =
                    EditorGUILayout.TextField(
                        "Animator Name",
                        aiAnimatorName);
            }
        }

        private void DrawSystems()
        {
            EditorGUILayout.LabelField(
                "Installed Systems",
                EditorStyles.boldLabel);

            if (GUILayout.Button("Refresh Systems"))
            {
                setupScript.FindSystem();
            }

            scrollPosition =
                EditorGUILayout.BeginScrollView(
                    scrollPosition,
                    GUILayout.Height(220));

            foreach (var item in setupScript.CurrentFSSystemsForSetup)
            {
                FSSystemInfo system = item.Value;

                if (system.characterType != characterType)
                    continue;

                EditorGUI.BeginChangeCheck();

                bool selected =
                    EditorGUILayout.ToggleLeft(
                        system.displayName,
                        system.selected);

                if (EditorGUI.EndChangeCheck())
                {
                    system.selected = selected;
                    system.OnSelectionChanged?.Invoke(selected);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset", GUILayout.Height(35)))
            {
                HandleReset();
                setupScript.FindSystem();

                if (characterType == CharacterType.Player)
                {
                    setupScript.EnableSystems(
                        FSSystemsSetup.ThirdPersonControllerSystemSetup.systemName);
                }
                else
                {
                    setupScript.EnableSystems();
                }
            }

            GUI.enabled = !isSettingUp;

            if (GUILayout.Button("Setup", GUILayout.Height(35)))
            {
                EditorApplication.delayCall += ExecuteSetup;
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            EditorGUILayout.LabelField(
                "Output: " + CharactersFolder);
        }

        // =====================================================
        // SETUP EXECUTION
        // =====================================================

        private void ExecuteSetup()
        {
            if (isSettingUp)
                return;

            isSettingUp = true;

            try
            {
                CreateSetupScript();

                if (!ValidateCharacter())
                    return;

                HandleReset();
                HandleSetup();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                EditorUtility.DisplayDialog(
                    "Fantacode Setup Failed",
                    exception.Message,
                    "OK");
            }
            finally
            {
                isSettingUp = false;
                Repaint();
            }
        }

        private bool ValidateCharacter()
        {
            if (setupScript == null)
                return false;

            if (characterModel == null)
            {
                EditorUtility.DisplayDialog(
                    "Missing Character",
                    "Please assign a character model.",
                    "OK");
                return false;
            }

            Animator animator = characterModel.GetComponent<Animator>();

            if (animator == null)
            {
                EditorUtility.DisplayDialog(
                    "Missing Animator",
                    "The character model needs an Animator.",
                    "OK");
                return false;
            }

            if (animator.avatar == null ||
                !animator.avatar.isValid ||
                !animator.isHuman)
            {
                EditorUtility.DisplayDialog(
                    "Invalid Humanoid",
                    "The character needs a valid Humanoid Avatar.",
                    "OK");
                return false;
            }

            if (characterType != CharacterType.Player &&
                characterType != CharacterType.AI)
            {
                EditorUtility.DisplayDialog(
                    "Invalid Character Type",
                    "Select Player or AI.",
                    "OK");
                return false;
            }

            return true;
        }

        // =====================================================
        // MAIN CHARACTER SETUP
        // =====================================================

        private void HandleSetup()
        {
            EnsureCharactersFolder();

            AnimatorController controller =
                CreateCombinedAnimatorController();

            AnimatorMergerUtility merger =
                new AnimatorMergerUtility(controller, controller);

            GameObject characterObject = null;
            GameObject prefabRoot = null;

            if (characterType == CharacterType.Player)
            {
                SetupPlayerCharacter(
                    controller,
                    merger,
                    out characterObject,
                    out prefabRoot);
            }
            else if (characterType == CharacterType.AI)
            {
                SetupAICharacter(
                    controller,
                    merger,
                    out characterObject,
                    out prefabRoot);
            }

            if (characterObject == null || prefabRoot == null)
            {
                throw new InvalidOperationException(
                    "Fantacode failed to generate a character.");
            }

            string prefabName =
                characterType == CharacterType.Player
                    ? "FS Player"
                    : "FS AI";

            string prefabPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    CharactersFolder + "/" + prefabName + ".prefab");

            bool success;

            GameObject prefab =
                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    prefabRoot,
                    prefabPath,
                    InteractionMode.UserAction,
                    out success);

            if (!success || prefab == null)
            {
                throw new InvalidOperationException(
                    "Failed to create prefab: " + prefabPath);
            }

            AssetDatabase.SaveAssets();

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            SceneView sceneView = SceneView.lastActiveSceneView;

            if (sceneView != null)
            {
                sceneView.Focus();
                sceneView.LookAt(characterObject.transform.position);
            }

            Debug.Log(
                "[Fantacode] Character prefab created: " + prefabPath);

            EditorUtility.DisplayDialog(
                "Character Created",
                "Successfully created:\n" + prefabPath,
                "OK");
        }

        // =====================================================
        // ASSET PATH FIX
        // =====================================================

        private static void EnsureCharactersFolder()
        {
            if (AssetDatabase.IsValidFolder(CharactersFolder))
                return;

            AssetDatabase.CreateFolder("Assets", "Characters");

            if (!AssetDatabase.IsValidFolder(CharactersFolder))
            {
                throw new IOException(
                    "Could not create Assets/Characters.");
            }
        }

        private AnimatorController CreateCombinedAnimatorController()
        {
            AnimatorController source =
                Resources.Load<AnimatorController>(
                    SourceAnimatorResource);

            string sourcePath = source != null
                ? AssetDatabase.GetAssetPath(source)
                : string.Empty;

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                string[] guids =
                    AssetDatabase.FindAssets(
                        "Third Person Controller t:AnimatorController");

                foreach (string guid in guids)
                {
                    string candidate =
                        AssetDatabase.GUIDToAssetPath(guid);

                    AnimatorController found =
                        AssetDatabase.LoadAssetAtPath<AnimatorController>(
                            candidate);

                    if (found == null ||
                        found.name != "Third Person Controller")
                    {
                        continue;
                    }

                    sourcePath = candidate;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new FileNotFoundException(
                    "Cannot find Fantacode's " +
                    "'Third Person Controller' Animator Controller.");
            }

            string safeName = SanitizeFilename(AnimatorName);

            string destinationPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    CharactersFolder + "/" +
                    safeName + ".controller");

            if (!AssetDatabase.CopyAsset(
                sourcePath,
                destinationPath))
            {
                throw new IOException(
                    "Failed to copy Animator Controller.\n" +
                    "Source: " + sourcePath + "\n" +
                    "Destination: " + destinationPath);
            }

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    destinationPath);

            if (controller == null)
            {
                throw new IOException(
                    "Copied Animator Controller could not be loaded.");
            }

            Debug.Log(
                "[Fantacode] Animator created: " + destinationPath);

            return controller;
        }

        private static string SanitizeFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return "Combined Controller";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                filename = filename.Replace(invalid, '_');

            filename = filename
                .Replace('/', '_')
                .Replace('\\', '_');

            return filename.Trim();
        }

        // =====================================================
        // PLAYER SETUP
        // =====================================================

        private void SetupPlayerCharacter(
            AnimatorController controller,
            AnimatorMergerUtility merger,
            out GameObject characterObject,
            out GameObject prefabRoot)
        {
            GameObject source =
                Resources.Load<GameObject>(PlayerPrefabResource);

            if (source == null)
            {
                throw new FileNotFoundException(
                    "Fantacode Locomotion Controller prefab was not found.");
            }

            GameObject instance = Instantiate(source);
            instance.name = "FS Player";

            prefabRoot = instance;

            PlayerController playerController =
                instance.GetComponentInChildren<PlayerController>(true);

            if (playerController == null)
            {
                throw new InvalidOperationException(
                    "PlayerController is missing from the prefab.");
            }

            GameObject player = playerController.gameObject;
            characterObject = player;

            CameraController camera =
                instance.GetComponentInChildren<CameraController>(true);

            if (camera == null)
                throw new InvalidOperationException("CameraController is missing.");

            camera.followTarget = player.transform;

            camera.firstPersonCamera.defaultSettings.overridedFollowTarget =
                player.transform;

            camera.thirdPersonCamera.defaultSettings.overridedFollowTarget =
                player.transform;

            GameObject model =
                Instantiate(
                    characterModel,
                    Vector3.zero,
                    Quaternion.identity);

            model.transform.SetParent(player.transform, false);

            Animator modelAnimator = model.GetComponent<Animator>();

            Transform head =
                modelAnimator.GetBoneTransform(HumanBodyBones.Head);

            if (head != null)
            {
                GameObject headTarget =
                    new GameObject("Head Follow Target");

                headTarget.transform.SetParent(head, false);

                headTarget.transform.localPosition =
                    new Vector3(0f, 0.18f, 0.08f);

                camera.firstPersonCamera.defaultSettings.overridedFollowTarget =
                    headTarget.transform;
            }

            camera.thirdPersonCamera.defaultSettings.overridedFollowTarget =
                player.transform;

            AddFootTrigger(modelAnimator);
            SetItemHandler(modelAnimator);

            int playerLayer = LayerMask.NameToLayer("Player");

            if (playerLayer >= 0)
                player.layer = playerLayer;

            if (player.GetComponent<CharacterID>() == null)
                player.AddComponent<CharacterID>();

            Animator rootAnimator = player.GetComponent<Animator>();

            if (rootAnimator == null)
            {
                throw new InvalidOperationException(
                    "Player root Animator is missing.");
            }

            rootAnimator.avatar = modelAnimator.avatar;
            rootAnimator.runtimeAnimatorController = controller;

            foreach (var item in setupScript.CurrentFSSystemsForSetup)
            {
                if (!item.Value.selected)
                    continue;

                var systemPrefab =
                    setupScript.CopyComponentsAndAnimControllerFromPrefab(
                        item.Value.prefabName,
                        merger,
                        player);

                if (item.Value.characterType == CharacterType.Player &&
                    item.Value.uiPrefabNames != null)
                {
                    FS_GameUI gameUI =
                        instance.GetComponentInChildren<FS_GameUI>(true);

                    if (gameUI != null)
                    {
                        foreach (string uiName in item.Value.uiPrefabNames)
                        {
                            if (string.IsNullOrEmpty(uiName))
                                continue;

                            GameObject uiPrefab =
                                Resources.Load<GameObject>(uiName);

                            if (uiPrefab == null)
                                continue;

                            if (gameUI.transform.Find(uiPrefab.name) != null)
                                continue;

                            GameObject uiInstance =
                                PrefabUtility.InstantiatePrefab(
                                    uiPrefab,
                                    gameUI.transform) as GameObject;

                            if (uiInstance != null)
                                uiInstance.name = uiPrefab.name;
                        }
                    }
                }

                item.Value.extraSetupActionPlayer?.Invoke(
                    player,
                    systemPrefab,
                    instance);
            }

            FS_GameUI finalGameUI =
                instance.GetComponentInChildren<FS_GameUI>(true);

            if (finalGameUI != null)
            {
                Transform mobileController =
                    finalGameUI.transform.Find(
                        "Locomotion Mobile Controller");

                if (mobileController != null)
                    mobileController.SetAsFirstSibling();
            }

            playerController.managedScripts =
                player.GetComponents<SystemBase>()
                    .OrderByDescending(system => system.Priority)
                    .ToList();
        }

        // =====================================================
        // AI SETUP
        // =====================================================

        private void SetupAICharacter(
            AnimatorController controller,
            AnimatorMergerUtility merger,
            out GameObject characterObject,
            out GameObject prefabRoot)
        {
            GameObject source =
                Resources.Load<GameObject>(AIPrefabResource);

            if (source == null)
            {
                throw new FileNotFoundException(
                    "Fantacode AI Controller prefab was not found.");
            }

            GameObject aiObject =
                Instantiate(
                    source,
                    Vector3.zero,
                    Quaternion.identity);

            aiObject.name = "FS AI";

            characterObject = aiObject;
            prefabRoot = aiObject;

            GameObject model =
                Instantiate(
                    characterModel,
                    Vector3.zero,
                    Quaternion.identity);

            model.transform.SetParent(aiObject.transform, false);

            Animator modelAnimator = model.GetComponent<Animator>();

            AddFootTrigger(modelAnimator);
            SetItemHandler(modelAnimator);

            int enemyLayer = LayerMask.NameToLayer("Enemy");

            if (enemyLayer >= 0)
                aiObject.layer = enemyLayer;

            if (aiObject.GetComponent<CharacterID>() == null)
                aiObject.AddComponent<CharacterID>();

            Animator rootAnimator = aiObject.GetComponent<Animator>();

            if (rootAnimator == null)
            {
                throw new InvalidOperationException(
                    "AI Controller root Animator is missing.");
            }

            rootAnimator.avatar = modelAnimator.avatar;
            rootAnimator.runtimeAnimatorController = controller;

            foreach (var item in setupScript.CurrentFSSystemsForSetup)
            {
                if (!item.Value.selected)
                    continue;

                var systemPrefab =
                    setupScript.CopyComponentsAndAnimControllerFromPrefab(
                        item.Value.prefabName,
                        merger,
                        aiObject);

                item.Value.extraSetupActionAI?.Invoke(
                    aiObject,
                    systemPrefab);
            }

            List<string> selectedAISystemNames =
                setupScript.CurrentFSSystemsForSetup
                    .Where(item =>
                        item.Value.selected &&
                        item.Value.characterType == CharacterType.AI)
                    .Select(item => item.Value.systemName)
                    .ToList();

            RunAICharacterSetupHandlers(
                aiObject,
                selectedAISystemNames);
        }

        // =====================================================
        // AI SETUP HANDLERS
        // =====================================================

        private static void RunAICharacterSetupHandlers(
            GameObject aiObject,
            List<string> selectedSystemNames)
        {
            var handlerTypes =
                TypeCache.GetTypesDerivedFrom<IAICharacterSetupHandler>();

            foreach (Type type in handlerTypes)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                try
                {
                    var handler =
                        (IAICharacterSetupHandler)
                        Activator.CreateInstance(type);

                    handler.OnAICharacterSetup(
                        aiObject,
                        selectedSystemNames);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "AI setup handler failed: " +
                        type.FullName + "\n" +
                        exception);
                }
            }
        }

        // =====================================================
        // FOOT TRIGGERS
        // =====================================================

        private static void AddFootTrigger(Animator animator)
        {
            if (animator == null || !animator.isHuman)
                return;

            GameObject footTriggerPrefab =
                Resources.Load<GameObject>("FootTrigger");

            if (footTriggerPrefab == null)
            {
                Debug.LogWarning(
                    "Fantacode FootTrigger prefab missing.");
                return;
            }

            AddFootTriggerToBone(
                animator,
                HumanBodyBones.RightFoot,
                footTriggerPrefab);

            AddFootTriggerToBone(
                animator,
                HumanBodyBones.LeftFoot,
                footTriggerPrefab);
        }

        private static void AddFootTriggerToBone(
            Animator animator,
            HumanBodyBones bone,
            GameObject prefab)
        {
            Transform foot = animator.GetBoneTransform(bone);

            if (foot == null)
                return;

            GameObject instance =
                PrefabUtility.InstantiatePrefab(
                    prefab,
                    foot) as GameObject;

            if (instance == null)
                return;

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            int layer = LayerMask.NameToLayer("FootTrigger");

            if (layer >= 0)
                instance.layer = layer;
        }

        // =====================================================
        // ITEM HANDLERS
        // =====================================================

        public static void SetItemHandler(Animator animator)
        {
            if (animator == null || !animator.isHuman)
                return;

            AddItemHandler(
                animator,
                HumanBodyBones.RightHand,
                "Right Hand Item Handler");

            AddItemHandler(
                animator,
                HumanBodyBones.LeftHand,
                "Left Hand Item Handler");
        }

        private static void AddItemHandler(
            Animator animator,
            HumanBodyBones bone,
            string handlerName)
        {
            Transform hand = animator.GetBoneTransform(bone);

            if (hand == null)
                return;

            if (hand.Find(handlerName) != null)
                return;

            GameObject handler = new GameObject(handlerName);

            handler.transform.SetParent(hand, false);
            handler.transform.localPosition = Vector3.zero;
            handler.transform.localRotation = Quaternion.identity;

            handler.AddComponent<EquippableItemHolder>();
        }

        // =====================================================
        // COMPONENT COPYING
        // =====================================================

        public static void CopyComponents(
            GameObject sourcePrefab,
            GameObject targetPrefab)
        {
            if (sourcePrefab == null || targetPrefab == null)
                return;

            Component[] components =
                sourcePrefab.GetComponents<Component>();

            foreach (Component sourceComponent in components)
            {
                if (sourceComponent == null ||
                    sourceComponent is Transform)
                    continue;

                Type type = sourceComponent.GetType();

                if (targetPrefab.GetComponent(type) != null)
                    continue;

                Component targetComponent =
                    targetPrefab.AddComponent(type);

                ComponentUtility.CopyComponent(sourceComponent);
                ComponentUtility.PasteComponentValues(targetComponent);
            }
        }

        // =====================================================
        // RESET
        // =====================================================

        private void HandleReset()
        {
            if (setupScript == null)
                return;

            Component[] components =
                setupScript.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component == null ||
                    component is Transform ||
                    component is FSSystemsSetup)
                    continue;

                DestroyImmediate(component);
            }

            Transform root = setupScript.transform;

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(root.GetChild(i).gameObject);
            }
        }

        // =====================================================
        // SETUP OBJECT
        // =====================================================

        private static void CreateSetupScript()
        {
            if (setupScript != null)
                return;

            setupScript = FindAnyObjectByType<FSSystemsSetup>();

            if (setupScript != null)
                return;

            GameObject setupObject = new GameObject("FS System");

            setupScript =
                setupObject.AddComponent<FSSystemsSetup>();

            setupScript.CurrentFSSystemsForSetup =
                new Dictionary<string, FSSystemInfo>();

            setupObject.hideFlags = HideFlags.HideInHierarchy;

            setupScript.FindSystem();
        }
    }
}

#endif
