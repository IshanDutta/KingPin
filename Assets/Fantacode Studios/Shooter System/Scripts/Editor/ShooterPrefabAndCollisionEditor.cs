using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Type = System.Type;
using System.Linq;
using FS_Core;

namespace FS_ShooterSystem
{
    public class ShooterPrefabAndCollisionEditor : EditorWindow
    {
        private enum TabType { Weapon, Ammo, RagdollAndCollision }
        private TabType selectedTab;

        private string prefabPath;
        private string savePathKey = "FS_WeaponEditor_SavePath";
        private Vector2 scrollPosition;

        // UI Style constants
        private const float SECTION_SPACING = 15f;
        private const float ITEM_SPACING = 8f;
        private const float BUTTON_HEIGHT = 32f;
        private const float LARGE_BUTTON_HEIGHT = 40f;

        [MenuItem("Tools/FS System/Shooter/Shooter Prefab and Collision Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<ShooterPrefabAndCollisionEditor>("Shooter System Setup");
            window.minSize = new Vector2(450, 500);
        }

        private void OnEnable()
        {
            prefabPath = EditorPrefs.GetString(savePathKey, "Assets");
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(prefabPath))
                EditorPrefs.SetString(savePathKey, prefabPath);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            DrawToolbar();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.Space(5);

            switch (selectedTab)
            {
                case TabType.Weapon:
                    DrawWeaponTab(); break;
                case TabType.Ammo:
                    DrawAmmoTab(); break;
                case TabType.RagdollAndCollision:
                    DrawRagdollTab(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            selectedTab = (TabType)GUILayout.Toolbar((int)selectedTab, Enum.GetNames(typeof(TabType)), EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSectionHeader(string title, string description = "")
        {
            EditorGUILayout.Space(SECTION_SPACING);

            // Draw section background
            Rect headerRect = EditorGUILayout.GetControlRect(false, 35);
            EditorGUI.DrawRect(headerRect, EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f, 0.5f) : new Color(0.8f, 0.8f, 0.8f, 0.5f));

            // Draw title
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 14;
            headerStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;

            Rect titleRect = new Rect(headerRect.x + 10, headerRect.y + 2, headerRect.width - 20, 18);
            EditorGUI.LabelField(titleRect, title, headerStyle);

            // Draw description if provided
            if (!string.IsNullOrEmpty(description))
            {
                GUIStyle descStyle = new GUIStyle(EditorStyles.miniLabel);
                descStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.4f, 0.4f, 0.4f);
                Rect descRect = new Rect(headerRect.x + 10, headerRect.y + 20, headerRect.width - 20, 12);
                EditorGUI.LabelField(descRect, description, descStyle);
            }

            EditorGUILayout.Space(ITEM_SPACING);
        }

        private void DrawSectionBox(Action content)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Space(5);
                content?.Invoke();
                EditorGUILayout.Space(5);
            }
        }

        #region Weapon Fields
        private GameObject weaponModel;
        private string weaponName = "New Weapon";
        #endregion

        #region Ammo Fields
        private GameObject ammoModel;
        private string ammoName = "New Ammo";
        private bool hasTrailEffect;
        private GameObject trailEffectPrefab;
        private bool addRigidbody;
        #endregion

        #region Draw Weapon UI
        private void DrawWeaponTab()
        {
            DrawSectionHeader("Weapon Configuration", "Set up weapon prefabs with all required components");

            DrawSectionBox(() =>
            {
                EditorGUILayout.LabelField("Basic Settings", EditorStyles.boldLabel);
                EditorGUILayout.Space(ITEM_SPACING);

                weaponModel = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Weapon Model", "The 3D model that will be used for the weapon"),
                    weaponModel, typeof(GameObject), true);

                EditorGUILayout.Space(3);
                weaponName = EditorGUILayout.TextField(
                    new GUIContent("Weapon Name", "Name for the weapon prefab"),
                    weaponName);
            });

            DrawSavePathSection();

            EditorGUILayout.Space(SECTION_SPACING);

            // Status and Create Button
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (weaponModel == null)
                {
                    EditorGUILayout.HelpBox("Please assign a weapon model to continue.", MessageType.Warning);
                }
                else if (string.IsNullOrEmpty(weaponName))
                {
                    EditorGUILayout.HelpBox("Please enter a weapon name to continue.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("Ready to create weapon prefab!", MessageType.Info);
                }

                EditorGUILayout.Space(ITEM_SPACING);

                GUI.enabled = weaponModel && !string.IsNullOrEmpty(weaponName);
                if (GUILayout.Button("Create Weapon Prefab", GUILayout.Height(LARGE_BUTTON_HEIGHT)))
                    CreateWeaponPrefab();
                GUI.enabled = true;
            }
        }

        private void CreateWeaponPrefab()
        {
            GameObject weapon = new GameObject(weaponName);
            var weaponObj = weapon.AddComponent<ShooterWeaponObject>();
            weapon.AddComponent<BoxCollider>();
            weapon.AddComponent<Rigidbody>().isKinematic = true;

            GameObject model = Instantiate(weaponModel);
            model.transform.SetParent(weapon.transform, false);

            weaponObj.ammoSpawnPoint = CreateReference("Ammo_Spawn", weapon.transform).transform;
            weaponObj.aimReference = CreateReference("Aim Reference", weapon.transform).transform;
            weaponObj.aimReference.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            weaponObj.supportHandIkTarget = CreateReference("SupportHand_IK", weapon.transform, true).transform;
            weaponObj.supportHandIkSetupHelperTarget = CreateReference("SupportHand_IK_SetupHelper", weapon.transform, true).transform;
            weaponObj.holdHandIkTarget = CreateReference("HoldHand_IK", weapon.transform, true).transform;
            weaponObj.holdHandIkSetupHelperTarget = CreateReference("HoldHand_IK_SetupHelper", weapon.transform, true).transform;


            weaponObj.supportHandIkTarget.hideFlags = HideFlags.HideInHierarchy;
            weaponObj.holdHandIkTarget.hideFlags = HideFlags.HideInHierarchy;
            weaponObj.supportHandIkSetupHelperTarget.hideFlags = HideFlags.HideInHierarchy;
            weaponObj.holdHandIkSetupHelperTarget.hideFlags = HideFlags.HideInHierarchy;


            GameObject flash = CreateReference("Flash", weapon.transform);
            var light = flash.AddComponent<Light>();
            SetupDefaultLight(light);
            weaponObj.flash = light;
            var source = weapon.AddComponent<AudioSource>();
            SetupAudio(source);
            weaponObj.AudioSource = source;

            string path = Path.Combine(prefabPath, weaponName + ".prefab");
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var prefab = PrefabUtility.SaveAsPrefabAsset(weapon, path);
            DestroyImmediate(weapon);
            Selection.activeObject = prefab;
            EditorUtility.DisplayDialog("Success", weaponName + " created!", "OK");
        }
        #endregion

        #region Draw Ammo UI
        private void DrawAmmoTab()
        {
            DrawSectionHeader("Ammo Configuration", "Set up ammunition prefabs with optional trail effects");

            DrawSectionBox(() =>
            {
                EditorGUILayout.LabelField("Basic Settings", EditorStyles.boldLabel);
                EditorGUILayout.Space(ITEM_SPACING);

                ammoModel = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Ammo Model", "The 3D model that will be used for the ammunition"),
                    ammoModel, typeof(GameObject), true);

                EditorGUILayout.Space(3);
                ammoName = EditorGUILayout.TextField(
                    new GUIContent("Ammo Name", "Name for the ammo prefab"),
                    ammoName);
            });

            DrawSectionBox(() =>
            {
                EditorGUILayout.LabelField("Additional Components", EditorStyles.boldLabel);
                EditorGUILayout.Space(ITEM_SPACING);

                hasTrailEffect = EditorGUILayout.Toggle(
                    new GUIContent("Has Trail Effect", "Enable to add a trail effect to the ammunition"),
                    hasTrailEffect);

                if (hasTrailEffect)
                {
                    EditorGUI.indentLevel++;
                    trailEffectPrefab = (GameObject)EditorGUILayout.ObjectField(
                        new GUIContent("Trail Prefab", "Prefab to use for the trail effect"),
                        trailEffectPrefab, typeof(GameObject), true);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(3);
                addRigidbody = EditorGUILayout.Toggle(
                    new GUIContent("Add Rigidbody", "Add a kinematic rigidbody component"),
                    addRigidbody);
            });

            DrawSavePathSection();

            EditorGUILayout.Space(SECTION_SPACING);

            // Status and Create Button
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ammoModel == null)
                {
                    EditorGUILayout.HelpBox("Please assign an ammo model to continue.", MessageType.Warning);
                }
                else if (string.IsNullOrEmpty(ammoName))
                {
                    EditorGUILayout.HelpBox("Please enter an ammo name to continue.", MessageType.Warning);
                }
                else if (hasTrailEffect && trailEffectPrefab == null)
                {
                    EditorGUILayout.HelpBox("Please assign a trail prefab or disable trail effect.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("Ready to create ammo prefab!", MessageType.Info);
                }

                EditorGUILayout.Space(ITEM_SPACING);

                GUI.enabled = ammoModel && !string.IsNullOrEmpty(ammoName) && (!hasTrailEffect || trailEffectPrefab != null);
                if (GUILayout.Button("Create Ammo Prefab", GUILayout.Height(LARGE_BUTTON_HEIGHT)))
                    CreateAmmoPrefab();
                GUI.enabled = true;
            }
        }

        private void CreateAmmoPrefab()
        {
            GameObject ammoRoot = new GameObject(ammoName);
            GameObject ammo = new GameObject("Ammo");
            ammo.transform.SetParent(ammoRoot.transform);
            var shooterAmmo = ammo.AddComponent<ShooterAmmoObject>();
            ammo.AddComponent<SphereCollider>().isTrigger = true;
            if (addRigidbody) ammo.AddComponent<Rigidbody>().isKinematic = true;

            Instantiate(ammoModel, ammo.transform);

            if (hasTrailEffect)
                shooterAmmo.trailObject = Instantiate(trailEffectPrefab, ammo.transform);

            string path = Path.Combine(prefabPath, ammoName + ".prefab");
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var prefab = PrefabUtility.SaveAsPrefabAsset(ammoRoot, path);
            DestroyImmediate(ammoRoot);
            Selection.activeObject = prefab;
            EditorUtility.DisplayDialog("Success", ammoName + " created!", "OK");
        }
        #endregion

        #region Draw Save Path UI
        private void DrawSavePathSection()
        {
            DrawSectionBox(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.TextField(
                        new GUIContent("Save Path", "Directory where the prefab will be saved"),
                        prefabPath);

                    if (GUILayout.Button("Browse", GUILayout.Width(80), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                    {
                        string path = EditorUtility.OpenFolderPanel("Select Folder", prefabPath, "");
                        if (path.StartsWith(Application.dataPath))
                            prefabPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                }
            });
        }
        #endregion

        #region Draw Ragdoll UI
        [Tooltip("Animator that uses a humanoid rig.")]
        private Animator targetAnimator;

        [Tooltip("Enable to apply colliders and damage handler to head bones.")]
        private bool head = true;
        [Tooltip("Enable to apply colliders and damage handler to torso bones.")]
        private bool body = true;
        [Tooltip("Enable to apply colliders and damage handler to arm bones.")]
        private bool arm = true;
        [Tooltip("Enable to apply colliders and damage handler to leg bones.")]
        private bool leg = true;

        [Tooltip("Scaling multiplier for head collider size.")]
        private float headMultiplier = 1f;

        [Tooltip("Scaling multiplier for spine collider size.")]
        private float spineMultiplier = 1f;

        [Tooltip("Scaling multiplier for limb collider size.")]
        private float armMultiplier = 1f;

        [Tooltip("Controls horizontal thickness of hip collider.")]
        private float hipWidth = 1f;

        private void DrawRagdollTab()
        {
            DrawSectionHeader("Collision & Damage Setup", "Configure ragdoll physics and damage detection for humanoid characters");

            // Step 1: Model Selection
            DrawSectionBox(() =>
            {
                EditorGUILayout.LabelField("Select Humanoid Model", EditorStyles.boldLabel);
                EditorGUILayout.Space(ITEM_SPACING);

                targetAnimator = (Animator)EditorGUILayout.ObjectField(
                    new GUIContent("Target Animator", "Assign a humanoid Animator to begin setup"),
                    targetAnimator,
                    typeof(Animator),
                    true
                );

                if (targetAnimator == null)
                {
                    EditorGUILayout.Space(ITEM_SPACING);
                    EditorGUILayout.HelpBox(
                        "No Animator assigned. Please assign a valid Animator component from a humanoid model.",
                        MessageType.Warning
                    );
                }
                else if (!targetAnimator.isHuman)
                {
                    EditorGUILayout.Space(ITEM_SPACING);
                    EditorGUILayout.HelpBox(
                        "Invalid rig type. The assigned Animator must use a Humanoid rig for bone mapping to work correctly.",
                        MessageType.Error
                    );
                }
                else
                {
                    EditorGUILayout.Space(ITEM_SPACING);
                    EditorGUILayout.HelpBox(
                        "Valid humanoid model detected. Ready to proceed with setup.",
                        MessageType.Info
                    );
                }
            });

            if (targetAnimator == null || !targetAnimator.isHuman)
                return;

            // Step 2: Ragdoll Setup
            DrawSectionBox(() =>
            {
                EditorGUILayout.LabelField("Ragdoll Setup", EditorStyles.boldLabel);
                EditorGUILayout.Space(ITEM_SPACING);
                EditorGUILayout.HelpBox(
                    "Click 'Create Ragdoll' to automatically set up physics bones and joint configurations for the assigned humanoid model. This will enable realistic ragdoll physics when the character is dies.",
                    MessageType.Info);

                EditorGUILayout.Space(ITEM_SPACING);

                if (GUILayout.Button("Create Ragdoll", GUILayout.Height(BUTTON_HEIGHT)))
                {
                    Type ragdollType = typeof(Editor).Assembly.GetType("UnityEditor.RagdollBuilder");
                    if (ragdollType == null)
                    {
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            ragdollType = assembly.GetType("UnityEditor.RagdollBuilder");
                            if (ragdollType != null)
                                break;
                        }
                    }
                    var wizard = Activator.CreateInstance(ragdollType);
                    SaveExistingPhysicsComponents(targetAnimator.transform);

                    SetBone(wizard, ragdollType, "pelvis", targetAnimator.GetBoneTransform(HumanBodyBones.Hips));
                    SetBone(wizard, ragdollType, "leftHips", targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
                    SetBone(wizard, ragdollType, "leftKnee", targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg));
                    SetBone(wizard, ragdollType, "leftFoot", targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot));
                    SetBone(wizard, ragdollType, "rightHips", targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg));
                    SetBone(wizard, ragdollType, "rightKnee", targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg));
                    SetBone(wizard, ragdollType, "rightFoot", targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot));
                    SetBone(wizard, ragdollType, "middleSpine", targetAnimator.GetBoneTransform(HumanBodyBones.Chest));
                    SetBone(wizard, ragdollType, "head", targetAnimator.GetBoneTransform(HumanBodyBones.Head));
                    SetBone(wizard, ragdollType, "leftArm", targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
                    SetBone(wizard, ragdollType, "leftElbow", targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm));
                    SetBone(wizard, ragdollType, "rightArm", targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm));
                    SetBone(wizard, ragdollType, "rightElbow", targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm));

                    MethodInfo updateMethod = ragdollType.GetMethod("OnWizardUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                    updateMethod?.Invoke(wizard, null);

                    MethodInfo createMethod = ragdollType?.GetMethod("OnWizardCreate", BindingFlags.Instance | BindingFlags.NonPublic);
                    createMethod?.Invoke(wizard, null);

                    RestoreSavedPhysicsComponents();
                }
            });

            // Step 3: Body Part Selection
            //DrawSectionBox(() =>
            //{
            //    EditorGUILayout.LabelField("Select Body Parts for Damage Detection", EditorStyles.boldLabel);
            //    EditorGUILayout.Space(ITEM_SPACING);

            //    EditorGUILayout.HelpBox(
            //        "Choose which body parts should have damage detection colliders and handlers.",
            //        MessageType.Info
            //    );

            //    EditorGUILayout.Space(ITEM_SPACING);

            //    using (new EditorGUILayout.HorizontalScope())
            //    {
            //        using (new EditorGUILayout.VerticalScope())
            //        {
            //            head = EditorGUILayout.ToggleLeft(
            //                new GUIContent("Head", "Enable collider + handler for head"),
            //                head, GUILayout.Width(100));
            //            body = EditorGUILayout.ToggleLeft(
            //                new GUIContent("Body/Torso", "Enable collider + handler for torso"),
            //                body, GUILayout.Width(100));
            //        }

            //        using (new EditorGUILayout.VerticalScope())
            //        {
            //            arm = EditorGUILayout.ToggleLeft(
            //                new GUIContent("Arms", "Enable collider + handler for arms"),
            //                arm, GUILayout.Width(100));
            //            leg = EditorGUILayout.ToggleLeft(
            //                new GUIContent("Legs", "Enable collider + handler for legs"),
            //                leg, GUILayout.Width(100));
            //        }
            //    }
            //});

            // Step 4: Apply Setup
            DrawSectionBox(() =>
            {
                EditorGUILayout.LabelField("Apply Damage Detection", EditorStyles.boldLabel);
                EditorGUILayout.Space(ITEM_SPACING);

                bool hasSelection = head || body || arm || leg;

                if (!hasSelection)
                {
                    EditorGUILayout.HelpBox(
                        "Please select at least one body part to set up damage detection.",
                        MessageType.Warning
                    );
                }
                else
                {
                    string selectedParts = "";
                    if (head) selectedParts += "Head, ";
                    if (body) selectedParts += "Body, ";
                    if (arm) selectedParts += "Arms, ";
                    if (leg) selectedParts += "Legs, ";
                    selectedParts = selectedParts.TrimEnd(' ', ',');

                    EditorGUILayout.HelpBox(
                        $"Ready to apply damage detection to: {selectedParts}",
                        MessageType.Info
                    );
                }

                EditorGUILayout.Space(ITEM_SPACING);

                GUI.enabled = hasSelection;
                if (GUILayout.Button(new GUIContent("Apply Damage Detection Setup", "This allowing specific body parts (like head, arms, legs) to take different amounts of damage"), GUILayout.Height(LARGE_BUTTON_HEIGHT)))
                {
                    Undo.RegisterFullObjectHierarchyUndo(targetAnimator.gameObject, "Setup Damagable");

                    if (head)
                    {
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.Head)); 
                        SetupHead(targetAnimator);
                    }

                    if (body)
                    {
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.Hips));
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.Chest));
                        SetupTorso(targetAnimator);
                    }

                    if (arm)
                    {
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm));
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm));
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm));
                        SetupArms(targetAnimator);
                    }

                    if (leg)
                    {
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg));
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg));
                        SetDamagable(targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg));
                        SetupLegs(targetAnimator);
                    }

                    EditorUtility.DisplayDialog("Success", "Damage detection setup completed!", "OK");
                }
                GUI.enabled = true;
            });
        }

        /// <summary>
        /// Assigns the ShooterBoneDamageHandler and proper layer to the specified bone transform.
        /// </summary>
        private void SetDamagable(Transform bone)
        {
            if (bone != null)
            {
                if(bone.gameObject.GetComponent<ShooterBoneDamageHandler>() == null)
                    bone.gameObject.AddComponent<ShooterBoneDamageHandler>();
                bone.gameObject.layer = LayerMask.NameToLayer("RagdollBone");
            }
        }

        /// <summary>
        /// Uses reflection to assign bones into Unity's internal Ragdoll Wizard fields.
        /// </summary>
        static void SetBone(object wizard, Type ragdollType, string fieldName, Transform bone)
        {
            FieldInfo field = ragdollType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                field.SetValue(wizard, bone);
            bone.gameObject.layer = LayerMask.NameToLayer("RagdollBone");
        }

        private List<ColliderBackup> savedColliders = new List<ColliderBackup>();
        private List<RigidbodyBackup> savedRigidbodies = new List<RigidbodyBackup>();
        private List<GameObject> duplicatedObjects = new List<GameObject>();



        private void SaveExistingPhysicsComponents(Transform root)
        {
            savedColliders.Clear();
            savedRigidbodies.Clear();

            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                // Skip bones that will be overridden
                if (col == null) continue;
                var colliderData = new ColliderBackup
                {
                    target = col.gameObject,
                    colliderType = col.GetType(),
                    originalCollider = UnityEngine.Object.Instantiate(col)
                };// Clone settings
                savedColliders.Add(colliderData);

                if (!duplicatedObjects.Contains(colliderData.originalCollider.gameObject))
                    duplicatedObjects.Add(colliderData.originalCollider.gameObject);

            }

            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null) continue;

                var rigidBodyData = new RigidbodyBackup
                {
                    target = rb.gameObject,
                    originalRigidbody = UnityEngine.Object.Instantiate(rb) // Clone settings
                };// Clone settings
                savedRigidbodies.Add(rigidBodyData);

                if (!duplicatedObjects.Contains(rigidBodyData.originalRigidbody.gameObject))
                    duplicatedObjects.Add(rigidBodyData.originalRigidbody.gameObject);
            }
        }
        private void RestoreSavedPhysicsComponents()
        {
            foreach (var backup in savedColliders)
            {
                if (backup.target == null) continue;
                // Check if collider was removed or replaced
                if (backup.target.GetComponent(backup.colliderType) == null)
                {
                    var newCol = backup.target.AddComponent(backup.colliderType) as Collider;
                    EditorUtility.CopySerialized(backup.originalCollider, newCol);
                }
            }

            foreach (var backup in savedRigidbodies)
            {
                if (backup.target == null) continue;
                if (backup.target.GetComponent<Rigidbody>() == null)
                {
                    var newRB = backup.target.AddComponent<Rigidbody>();
                    EditorUtility.CopySerialized(backup.originalRigidbody, newRB);
                }
            }
            foreach (var obj in duplicatedObjects)
            {
                DestroyImmediate(obj);
            }
        }


        #region Collider Setup Logic
        private void SetupHead(Animator animator)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);

            if (head != null && neck != null)
            {
                GetOrAddComponent<SphereCollider>(head.gameObject, (_collider) =>
                {
                    var collider = _collider as SphereCollider;
                    float headSize = .2f * headMultiplier;
                    collider.radius = headSize * 0.6f;
                    collider.center = new Vector3(0, headSize * 0.5f, headSize * 0.2f);
                });
            }
        }

        private void SetUpHipsCollider(Animator animator)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var spine = animator.GetBoneTransform(HumanBodyBones.Chest);
            var leg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);

            float height = spine.position.y - leg.position.y;

            GetOrAddComponent<BoxCollider>(hips.gameObject, (_collider) =>
            {
                var collider = _collider as BoxCollider;
                collider.center = hips.InverseTransformPoint(hips.position);
                collider.size = new Vector3(.35f, height, .27f) * hipWidth;
            });
        }

        private void SetUpSpineCollider(Animator animator)
        {
            var spine = animator.GetBoneTransform(HumanBodyBones.Hips);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);

            var center = (spine.position + neck.position) / 2;
            var height = neck.position.y - spine.position.y;

            GetOrAddComponent<BoxCollider>(spine.gameObject, (_collider) =>
            {
                var collider = _collider as BoxCollider;
                collider.center = spine.InverseTransformPoint(center);
                collider.size = new Vector3(0.35f, height, 0.27f) * spineMultiplier;
            });
        }

        private void SetupTorso(Animator animator)
        {
            SetUpHipsCollider(animator);
            SetUpSpineCollider(animator);
        }

        private void SetupArms(Animator animator)
        {
            SetupArmPair(animator, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            SetupArmPair(animator, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        }

        private void SetupLegs(Animator animator)
        {
            SetupLegPair(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            SetupLegPair(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        }

        #endregion

        #region Pair Setup Methods

        private void SetupArmPair(Animator animator, HumanBodyBones shoulderBone, HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones handBone)
        {
            var upper = animator.GetBoneTransform(upperBone);
            var lower = animator.GetBoneTransform(lowerBone);
            var hand = animator.GetBoneTransform(handBone);

            if (upper != null && lower != null)
            {
                GetOrAddComponent<CapsuleCollider>(upper.gameObject, (_collider) =>
                {
                    var collider = _collider as CapsuleCollider;
                    Vector3 mid = (upper.position + lower.position) * 0.5f;
                    float len = Vector3.Distance(upper.position, lower.position);
                    collider.direction = 1;
                    collider.height = len * armMultiplier;
                    collider.radius = len * 0.25f;
                    collider.center = upper.InverseTransformPoint(mid);
                });
            }

            if (lower != null && hand != null)
            {
                GetOrAddComponent<CapsuleCollider>(lower.gameObject, (_collider) =>
                {
                    var collider = _collider as CapsuleCollider;
                    Vector3 dir = (hand.position - lower.position).normalized;
                    float len = Vector3.Distance(lower.position, hand.position + dir * .2f);
                    Vector3 mid = (lower.position + (hand.position + dir * .2f)) * 0.5f;
                    collider.direction = 1;
                    collider.height = len * armMultiplier;
                    collider.radius = len * 0.125f;
                    collider.center = lower.InverseTransformPoint(mid);
                });
            }
        }

        private void SetupLegPair(Animator animator, HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones footBone)
        {
            var upper = animator.GetBoneTransform(upperBone);
            var lower = animator.GetBoneTransform(lowerBone);
            var foot = animator.GetBoneTransform(footBone);

            if (upper != null && lower != null)
            {
                GetOrAddComponent<CapsuleCollider>(upper.gameObject, (_collider) =>
                {
                    var collider = _collider as CapsuleCollider;
                    Vector3 mid = (upper.position + lower.position) * 0.5f;
                    float len = Vector3.Distance(upper.position, lower.position);
                    collider.direction = 1;
                    collider.height = len * armMultiplier;
                    collider.radius = len * 0.18f;
                    collider.center = upper.InverseTransformPoint(mid);
                });
            }

            if (lower != null && foot != null)
            {
                GetOrAddComponent<CapsuleCollider>(lower.gameObject, (_collider) =>
                {
                    var collider = _collider as CapsuleCollider;
                    Vector3 mid = (lower.position + foot.position) * 0.5f;
                    float len = Vector3.Distance(lower.position, foot.position);
                    collider.direction = 1;
                    collider.height = len * armMultiplier;
                    collider.radius = len * 0.15f;
                    collider.center = lower.InverseTransformPoint(mid);
                });
            }
        }

        #endregion

        #region Utility
        /// <summary>
        /// Ensures a collider is present and triggers setup logic.
        /// </summary>
        private T GetOrAddComponent<T>(GameObject obj, Action<Collider> OnCreateNewCollider = null) where T : Collider
        {
            T collider = obj.GetComponent<T>();
            if (collider == null)
            {
                collider = obj.AddComponent<T>();
                OnCreateNewCollider?.Invoke(collider);
            }

            //collider.isTrigger = true;

            if (!obj.TryGetComponent(out ShooterBoneDamageHandler _))
                obj.AddComponent<ShooterBoneDamageHandler>();

            obj.layer = LayerMask.NameToLayer("RagdollBone");

            return collider;
        }
        #endregion
        #endregion

        #region Utility Methods
        private GameObject CreateReference(string name, Transform parent, bool hideHeirarchy = false)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            if (hideHeirarchy)
                obj.hideFlags = HideFlags.HideInHierarchy;
            return obj;
        }

        private void SetupDefaultLight(Light light)
        {
            light.type = LightType.Point;
            light.color = new Color(1f, 0.8f, 0.5f);
            light.intensity = 2;
            light.range = 3;
            light.enabled = false;
        }

        private void SetupAudio(AudioSource src)
        {
            src.playOnAwake = false;
            src.spatialBlend = 1;
            src.minDistance = 1;
            src.maxDistance = 20;
            src.rolloffMode = AudioRolloffMode.Linear;
        }
        #endregion
    }
}