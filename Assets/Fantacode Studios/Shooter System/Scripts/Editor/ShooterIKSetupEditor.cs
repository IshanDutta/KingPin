using FS_Core;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Codice.Client.Common.Connection.AskCredentialsToUser;
// Unity 6.5 (6000.5) deprecated the int-based hierarchy callback in favor of an EntityId-based
// one. The callback parameter changes from int to EntityId accordingly.
#if UNITY_6000_5_OR_NEWER
using HierarchyItemId = UnityEngine.EntityId;
#else
using HierarchyItemId = System.Int32;
#endif

namespace FS_ShooterSystem
{
    public class ShooterIKSetupEditor : EditorWindow
    {
        static ShooterIKSetupEditor window;
        public static ShooterFighter Fighter { get; private set; }

        /// <summary>True only while THIS (legacy) tool is actively in setup — used for mutual exclusion with the New Hand IK setup.</summary>
        public static bool IsSetupActive { get; private set; }
        static Transform SupportHandTarget => Fighter.CurrentShooterWeaponObject.supportHandIkTarget;
        static Transform HoldHandTarget => Fighter.CurrentShooterWeaponObject.holdHandIkTarget;

        static Color green = new Color(0.2f, 0.8f, 0.3f);
        static Color blue = new Color(.2f, 0.3f, 0.5f);
        static Color red = new Color(0.9f, 0.1f, 0.1f);
        static bool saveAsDefault = true;

        public static void SetFighter(ShooterFighter shooterFighter)
        {
            if (!ReferenceEquals(Fighter, shooterFighter))
            {
                if (shooterFighter != null)
                {
                    Fighter = shooterFighter;
                    if (Fighter != null)
                    {
                        EditorGUIUtility.PingObject(Fighter.gameObject);
                    }
                    InitTargets();
                }
            }
        }

        [MenuItem("Tools/FS System/Shooter/IK Setup")]
        public static void ShowWindow()
        {
            window = GetWindow<ShooterIKSetupEditor>("Ik");
            window.minSize = new Vector2(360, 404); // better default min size
            window.maxSize = new Vector2(360, 404); // better default min size
            InitTargets();
        }

        private void OnGUI()
        {
            if (Fighter == null)
            {
                if (Selection.activeGameObject != null)
                    SetFighter(Selection.activeGameObject.GetComponent<ShooterFighter>());

                if (Fighter == null)
                {
                    EditorGUILayout.HelpBox("Please select a GameObject with ShooterFighter component.", MessageType.Warning);
                    return;
                }
            }
            StopIfModeChanged();
            DrawSetupControls();
            if (IsSetupActive)
            {
                if (Fighter.CurrentWeapon != null)
                {
                    DrawIdleAimIkButtons();

                    GUILayout.Space(10);
                    DrawIKSaveSection();
                    GUILayout.Space(6);
                    EditorGUILayout.HelpBox(
                        "Scene setup (amber = Hold, cyan = Support):\n" +
                        "• Click a sphere to activate it — hands get move + rotate handles, elbows get a move handle.\n" +
                        "• A dotted line links each bone to its target.\n" +
                        "• The arm chain and muzzle→aim line show context.\n" +
                        "Edits are live; Save Aim/Idle IK to persist.",
                        MessageType.Info);
                    GUILayout.Space(10);
                    ShowEquippedWeaponDetails();
                }
            }
        }

        private void DrawSetupControls()
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("Setup Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            Color originalColor = GUI.backgroundColor;

            if (!IsSetupActive)
            {
                // Only usable while the character is in Legacy aiming mode.
                if (Fighter.AimingMode != AimingMode.Legacy)
                {
                    EditorGUILayout.HelpBox("Set the ShooterFighter's Aiming Mode to Legacy to use the legacy IK setup.", MessageType.Warning);
                    DrawDisabledStart();
                    EditorGUILayout.EndVertical();
                    return;
                }
                // Mutually exclusive with the New Hand IK setup (they share IkSetupMode).
                if (HandIKSetupEditor.IsSetupActive)
                {
                    EditorGUILayout.HelpBox("Hand IK Setup (New Aiming) is running. Stop it before starting Legacy IK setup.", MessageType.Warning);
                    DrawDisabledStart();
                    EditorGUILayout.EndVertical();
                    return;
                }
            }

            // Main setup toggle
            GUI.backgroundColor = IsSetupActive ? red : green;
            if (GUILayout.Button(IsSetupActive ? "Stop Setup" : "Start Setup", GUILayout.Height(30)))
            {
                if (IsSetupActive) StopSetup();
                else StartSetup();
            }
            GUI.backgroundColor = originalColor;

            if (IsSetupActive && Fighter.CurrentWeapon == null)
            {
                if (Application.isPlaying)
                    EditorGUILayout.HelpBox("No weapon equipped. Please equip a weapon first.", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("Enter Play Mode and equip a weapon to set up IK.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        static void DrawDisabledStart()
        {
            GUI.enabled = false;
            GUILayout.Button("Start Setup", GUILayout.Height(30));
            GUI.enabled = true;
        }

        static void StartSetup()
        {
            IsSetupActive = true;
            activeHandle = null;
            if (Fighter != null)
            {
                Fighter.IkSetupMode = true;
                if (Fighter.IsAiming) Fighter.StopAiming();
            }
        }

        static void StopSetup()
        {
            IsSetupActive = false;
            activeHandle = null;
            if (Fighter != null)
            {
                Fighter.IkSetupMode = false;
                if (Fighter.IsAiming) Fighter.StopAiming();
            }
            Tools.hidden = false; // restore Unity's transform gizmo
            if (window != null) window.Repaint();
        }

        // Auto-stop if the character's aiming mode changed away from Legacy while running.
        static void StopIfModeChanged()
        {
            if (IsSetupActive && (Fighter == null || Fighter.AimingMode != AimingMode.Legacy))
                StopSetup();
        }

        private void DrawIdleAimIkButtons()
        {
            if (IsSetupActive)
            {
                float buttonWidth = (position.width / 2f) - 10f;
                Color originalColor = GUI.backgroundColor;
                GUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                // IK Setup
                GUI.backgroundColor = Fighter.IsAiming ? green : Color.white;
                if (GUILayout.Button("Aim", GUILayout.Width(buttonWidth), GUILayout.Height(28)))
                {
                    Selection.activeObject = null;
                    activeHandle = null; // aim/idle use different IK data — drop the active handle
                    Fighter.StartAiming();
                }


                // Weapon Setup
                GUI.backgroundColor = !Fighter.IsAiming ? green : Color.white;
                if (GUILayout.Button("Idle", GUILayout.Width(buttonWidth), GUILayout.Height(28)))
                {
                    activeHandle = null;
                    if (Fighter.IsAiming)
                        Fighter.StopAiming();
                }

                GUI.backgroundColor = originalColor;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawIKSaveSection()
        {
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical("box");

            saveAsDefault = EditorGUILayout.Toggle("Save as Default", saveAsDefault);


            GUI.backgroundColor = blue;
            EditorGUILayout.BeginHorizontal("box");

            if (GUILayout.Button("Save Aim IK", GUILayout.Height(25)))
                SaveIKData(false);
            EditorGUILayout.Space(10);
            if (GUILayout.Button("Clear", GUILayout.Height(25)))
                ClearData(true);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal("box");
            GUI.backgroundColor = blue;
            if (GUILayout.Button("Save Idle IK", GUILayout.Height(25)))
                SaveIKData(true);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Clear", GUILayout.Height(25)))
                ClearData(false);

            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void ShowEquippedWeaponDetails()
        {
            GUILayout.Space(5);

            if (Fighter.CurrentWeapon != null)
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.LabelField("Current Weapon", Fighter.CurrentWeapon.name);
                EditorGUILayout.LabelField("Holder Bone", Fighter.CurrentWeapon.holderBone.ToString());
                EditorGUILayout.LabelField("Idle IK (Support Hand)", Fighter.CurrentWeapon.UseHandIkForIdle.ToString());

                EditorGUILayout.EndVertical();
            }
        }

        void SaveIKData(bool saveIdleData = true)
        {
            Undo.RecordObject(Fighter.CurrentWeapon, "Ik data modified");
            if (saveIdleData)
            {
                if (!saveAsDefault)
                {
                    var existingArmIKReference = Fighter.CurrentWeapon.iKReferences.Find(
                    r => r.characterReferences != null &&
                    r.characterReferences.Count > 0 &&
                    r.characterReferences.FirstOrDefault(c => c.characterId == Fighter.characterID.id || c.avatar == Fighter.Animator.avatar) != null);

                    if (existingArmIKReference != null)
                    {
                        existingArmIKReference.idleIKData.supportHandPosition = SupportHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localPosition;
                        existingArmIKReference.idleIKData.supportHandRotation = SupportHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localEulerAngles;
                        existingArmIKReference.idleIKData.holdHandPosition = HoldHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localPosition;
                        existingArmIKReference.idleIKData.holdHandRotation = HoldHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localEulerAngles;
                    }
                    else
                    {
                        var newIkReference = new ArmIKReference()
                        {
                            characterReferences = new List<CharacterReference>()
                            {
                                new CharacterReference
                                {
                                    characterId = Fighter.characterID.id,
                                    avatar = Fighter.Animator.avatar
                                }
                            },
                            idleIKData = new ArmIkData()
                            {
                                supportHandPosition = SupportHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localPosition,
                                supportHandRotation = SupportHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localEulerAngles,
                                holdHandPosition = HoldHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localPosition,
                                holdHandRotation = HoldHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localEulerAngles,
                                holdElbowPosition = Fighter.CurrentWeapon.idleIKData.holdElbowPosition,
                                supportElbowPosition = Fighter.CurrentWeapon.idleIKData.supportElbowPosition
                            }
                        };

                        Fighter.CurrentWeapon.iKReferences.Add(newIkReference);
                    }
                }
                else
                {
                    Fighter.CurrentWeapon.idleIKData.supportHandPosition = SupportHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localPosition;
                    Fighter.CurrentWeapon.idleIKData.supportHandRotation = SupportHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localEulerAngles;
                    Fighter.CurrentWeapon.idleIKData.holdHandPosition = HoldHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localPosition;
                    Fighter.CurrentWeapon.idleIKData.holdHandRotation = HoldHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localEulerAngles;
                }
                Debug.Log($"Idle IK data saved for {Fighter.CurrentWeapon.name}.");
            }
            else
            {
                if (!saveAsDefault)
                {
                    var existingArmIKReference = Fighter.CurrentWeapon.iKReferences.Find(
                    r => r.characterReferences != null &&
                    r.characterReferences.Count > 0 &&
                    r.characterReferences.FirstOrDefault(c => c.characterId == Fighter.characterID.id || c.avatar == Fighter.Animator.avatar) != null);

                    if (existingArmIKReference != null)
                    {
                        existingArmIKReference.aimIKData.supportHandPosition = SupportHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localPosition;
                        existingArmIKReference.aimIKData.supportHandRotation = SupportHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localEulerAngles;
                        existingArmIKReference.aimIKData.holdHandPosition = HoldHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localPosition;
                        existingArmIKReference.aimIKData.holdHandRotation = HoldHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localEulerAngles;
                    }
                    else
                    {
                        var newIkReference = new ArmIKReference()
                        {
                            characterReferences = new List<CharacterReference>()
                            {
                                new CharacterReference
                                {
                                    characterId = Fighter.characterID.id,
                                    avatar = Fighter.Animator.avatar
                                }
                            },
                            aimIKData = new ArmIkData()
                            {
                                supportHandPosition = SupportHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localPosition,
                                supportHandRotation = SupportHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localEulerAngles,
                                holdHandPosition = HoldHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localPosition,
                                holdHandRotation = HoldHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localEulerAngles,
                                holdElbowPosition = Fighter.CurrentWeapon.aimIKData.holdElbowPosition,
                                supportElbowPosition = Fighter.CurrentWeapon.aimIKData.supportElbowPosition
                            }
                        };

                        Fighter.CurrentWeapon.iKReferences.Add(newIkReference);
                    }
                }
                else
                {
                    Fighter.CurrentWeapon.aimIKData.supportHandPosition = SupportHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localPosition;
                    Fighter.CurrentWeapon.aimIKData.supportHandRotation = SupportHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.supportHandIkSetupHelperTarget.localEulerAngles;
                    Fighter.CurrentWeapon.aimIKData.holdHandPosition = HoldHandTarget.localPosition - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localPosition;
                    Fighter.CurrentWeapon.aimIKData.holdHandRotation = HoldHandTarget.localEulerAngles - Fighter.CurrentShooterWeaponObject.holdHandIkSetupHelperTarget.localEulerAngles;
                }
                Debug.Log($"Aim IK data saved for {Fighter.CurrentWeapon.name}.");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.SetDirty(Fighter.CurrentWeapon);
            Fighter.UpdateExistingIkReference();
        }

        void ClearData(bool isAimData)
        {
            Undo.RecordObject(Fighter.CurrentWeapon, "Ik data modified");
            var existingArmIKReference = Fighter.CurrentWeapon.iKReferences.Find(
                     r => r.characterReferences != null &&
                     r.characterReferences.Count > 0 &&
                     r.characterReferences.FirstOrDefault(c => c.characterId == Fighter.characterID.id || c.avatar == Fighter.Animator.avatar) != null);

            if (existingArmIKReference != null)
            {
                if(isAimData)
                    existingArmIKReference.aimIKData = new ArmIkData();
                else
                    existingArmIKReference.idleIKData = new ArmIkData();

                var dataType = isAimData ? "Aim" : "Idle";
                Debug.Log($"{Fighter.CurrentWeapon} {dataType} IK data cleared.");
            }
            else
            {
                bool confirm = EditorUtility.DisplayDialog(
                               "No Data Found",
                               "No Aim IK data was found for this character on the current weapon.\n\n" +
                               "Do you want to clear the default data? Only clear it if the default data is not used by other weapons.",
                               "OK",
                               "Cancel"
                           );

                if (confirm)
                {
                    if (isAimData)
                        Fighter.CurrentWeapon.aimIKData = new ArmIkData();
                    else
                        Fighter.CurrentWeapon.idleIKData = new ArmIkData();

                    var dataType = isAimData ? "Aim" : "Idle";
                    Debug.Log($"{Fighter.CurrentWeapon} default {dataType} IK data has been cleared.");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.SetDirty(Fighter.CurrentWeapon);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            InitTargets();

            Selection.selectionChanged += () =>
            {
                if (Selection.activeGameObject != null)
                    SetFighter(Selection.activeGameObject.GetComponent<ShooterFighter>());
                if (window != null)
                {
                    window.Repaint();
                }
            };
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Tools.hidden = false; // always restore Unity's transform gizmo
            activeHandle = null;
            IsSetupActive = false;

            if (Fighter != null)
            {
                Fighter.IkSetupMode = false;
                Fighter.StopAiming();
                Fighter = null;
            }
        }

        static void InitTargets()
        {
#if UNITY_6000_5_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#endif
        }

        #region Gizmos

        // Per-arm colours, shared across every scene visual so hold vs support is
        // always read the same way.
        static readonly Color holdColor = new Color(1f, 0.75f, 0.1f);     // amber
        static readonly Color supportColor = new Color(0.1f, 0.8f, 1f);   // cyan

        // Which IK point is currently being edited. Only the active point shows a
        // move/rotate handle; every other point shows a clickable sphere. Exactly
        // one handle is live at a time, so the hold-hand handle no longer chases the
        // IK result (the weapon is parented to the hold hand, so an always-on handle
        // fed its own movement back into itself).
        static string activeHandle;      // null = none. e.g. "hold-hand", "support-elbow"
        static Vector3 activePos;        // world pose the active hand handle holds (frozen, not re-read from the moving target)
        static Quaternion activeRot;

        static void OnSceneGUI(SceneView sceneView)
        {
            StopIfModeChanged();

            // The New-aiming Hand IK setup owns the scene handles only while it's
            // actively in setup — don't draw the legacy gizmos on top of it then.
            // (When that window is merely open, legacy setup works normally.)
            if (HandIKSetupEditor.IsSetupActive)
            {
                Tools.hidden = false;
                return;
            }

            bool active = Application.isPlaying && Fighter != null && Fighter.CurrentWeapon != null
                          && !Fighter.CurrentWeapon.isDualItem && Fighter.CurrentShooterWeaponObject.aimController != null
                          && IsSetupActive;

            if (!active)
            {
                Tools.hidden = false; // restore Unity's transform gizmo when not setting up
                return;
            }

            // Hide Unity's default transform gizmo so our combined move+rotate
            // handles are unambiguous.
            Tools.hidden = true;

            // Context visualization: arm chains + line of fire.
            DrawArmChain(false); // hold (main) arm
            DrawArmChain(true);  // support arm
            DrawAimVisualization();

            // Hand grips: always-on move + rotate handles that write offsets live
            // into the IK data (same model as the elbow handles — no selection).
            DrawHandTarget(false); // hold
            DrawHandTarget(true);  // support

            // Elbow poles.
            DrawElbowPoleHandle(false); // hold (main) arm
            DrawElbowPoleHandle(true);  // support arm
        }

        // Resolves an arm bone for the hold or support side based on which hand holds the weapon.
        static Transform Bone(bool support, HumanBodyBones rightVariant, HumanBodyBones leftVariant)
        {
            if (Fighter == null || Fighter.Animator == null || Fighter.CurrentWeapon == null) return null;
            bool holdRight = Fighter.CurrentWeapon.holderBone == HumanBodyBones.RightHand;
            bool useRight = support ? !holdRight : holdRight;
            return Fighter.Animator.GetBoneTransform(useRight ? rightVariant : leftVariant);
        }

        // Draws the shoulder -> elbow -> hand chain for one arm.
        static void DrawArmChain(bool support)
        {
            Transform upper = Bone(support, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperArm);
            Transform lower = Bone(support, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerArm);
            Transform hand = Bone(support, HumanBodyBones.RightHand, HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) return;

            Handles.color = support ? supportColor : holdColor;
            Handles.DrawAAPolyLine(4f, upper.position, lower.position, hand.position);

            const float s = 0.01f;
            Handles.SphereHandleCap(0, upper.position, Quaternion.identity, s, EventType.Repaint);
            Handles.SphereHandleCap(0, lower.position, Quaternion.identity, s, EventType.Repaint);
            Handles.SphereHandleCap(0, hand.position, Quaternion.identity, s, EventType.Repaint);
        }

        // Draws the line of fire (muzzle -> aim point) plus muzzle/aim markers.
        static void DrawAimVisualization()
        {
            var wobj = Fighter.CurrentShooterWeaponObject;
            if (wobj == null || wobj.ammoSpawnPoint == null) return;

            Vector3 muzzle = wobj.ammoSpawnPoint.position;
            Vector3 aim = Fighter.TargetAimPoint;

            Handles.color = new Color(1f, 0.25f, 0.2f, 0.9f);
            Handles.DrawAAPolyLine(3f, muzzle, aim);
            Handles.SphereHandleCap(0, muzzle, Quaternion.identity, 0.012f, EventType.Repaint);
            Handles.Label(muzzle, "  Muzzle");

            Handles.color = new Color(1f, 0.25f, 0.2f, 0.5f);
            Handles.SphereHandleCap(0, aim, Quaternion.identity, 0.02f, EventType.Repaint);
            Handles.Label(aim, "  Aim Point");
        }

        // Draws a small clickable sphere at a point. Returns true when clicked.
        static bool ClickableSphere(Vector3 point)
        {
            float size = HandleUtility.GetHandleSize(point) * 0.09f;
            return Handles.Button(point, Quaternion.identity, size, size, Handles.SphereHandleCap);
        }

        // Hand grip: a dotted line from the wrist bone to the grip, a clickable
        // sphere, and — only when that sphere is the active handle — move + rotate
        // gizmos. While active the handle holds a FROZEN world pose (activePos/Rot)
        // rather than re-reading the target every frame, so the hold hand (whose
        // target rides the weapon it drives) no longer feeds its own motion back and
        // jitters.
        static void DrawHandTarget(bool support)
        {
            var wobj = Fighter.CurrentShooterWeaponObject;
            Transform ikTarget = support ? wobj.supportHandIkTarget : wobj.holdHandIkTarget;
            Transform helper = support ? wobj.supportHandIkSetupHelperTarget : wobj.holdHandIkSetupHelperTarget;
            Transform sourceBone = Bone(support, HumanBodyBones.RightHand, HumanBodyBones.LeftHand);
            if (ikTarget == null || helper == null || sourceBone == null || ikTarget.parent == null) return;

            ArmIkData data = Fighter.ResolveIkData(Fighter.IsAiming);
            if (data == null) return;

            string key = support ? "support-hand" : "hold-hand";
            bool isActive = activeHandle == key;

            Handles.color = support ? supportColor : holdColor;
            Vector3 point = isActive ? activePos : ikTarget.position;
            Handles.DrawDottedLine(sourceBone.position, point, 3f);
            Handles.Label(point + Vector3.up * 0.03f, support ? "  Support Hand" : "  Hold Hand");

            if (isActive)
            {
                EditorGUI.BeginChangeCheck();
                activePos = Handles.PositionHandle(activePos, activeRot);
                activeRot = Handles.RotationHandle(activeRot, activePos);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(Fighter.CurrentWeapon, "Hand IK edited");

                    // Convert the frozen world pose into the weapon-local offset the
                    // runtime applies (relative to the bone-tracking helper), without
                    // touching the live target transform.
                    Transform wf = ikTarget.parent;
                    Vector3 posOffset = wf.InverseTransformPoint(activePos) - helper.localPosition;
                    Vector3 rotOffset = (Quaternion.Inverse(wf.rotation) * activeRot).eulerAngles - helper.localEulerAngles;

                    if (support) { data.supportHandPosition = posOffset; data.supportHandRotation = rotOffset; }
                    else { data.holdHandPosition = posOffset; data.holdHandRotation = rotOffset; }

                    EditorUtility.SetDirty(Fighter.CurrentWeapon);
                }
            }
            else if (ClickableSphere(ikTarget.position))
            {
                activeHandle = key;
                activePos = ikTarget.position;
                activeRot = ikTarget.rotation;
            }
        }

        // Elbow pole: a dotted line from the elbow bone to the pole, a clickable
        // sphere, and — only when active — a move gizmo. The pole is bone-relative
        // (root-space offset), which is already stable, so it isn't frozen.
        static void DrawElbowPoleHandle(bool support)
        {
            Transform lowerArm = support ? Fighter.SupportLowerArm : Fighter.HoldLowerArm;
            Transform root = Fighter.CharacterRoot;
            if (lowerArm == null || root == null) return;

            ArmIkData data = Fighter.ResolveIkData(Fighter.IsAiming);
            if (data == null) return;

            string key = support ? "support-elbow" : "hold-elbow";
            bool isActive = activeHandle == key;

            Vector3 offset = support ? data.supportElbowPosition : data.holdElbowPosition;
            Vector3 polePoint = lowerArm.position + root.TransformVector(offset);

            Handles.color = support ? supportColor : holdColor;
            Handles.DrawDottedLine(lowerArm.position, polePoint, 3f);
            Handles.Label(polePoint, support ? "  Support Elbow" : "  Hold Elbow");

            if (isActive)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(polePoint, root.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(Fighter.CurrentWeapon, "Elbow pole moved");
                    Vector3 newOffset = root.InverseTransformVector(newPos - lowerArm.position);
                    if (support) data.supportElbowPosition = newOffset;
                    else data.holdElbowPosition = newOffset;
                    EditorUtility.SetDirty(Fighter.CurrentWeapon);
                }
            }
            else if (ClickableSphere(polePoint))
            {
                activeHandle = key;
            }
        }

        private void OnSelectionChange()
        {
            SceneView.RepaintAll();
        }

        static void OnHierarchyGUI(HierarchyItemId instanceID, Rect selectionRect)
        {
            if (Fighter == null) return;
            // Unity 6.5 (6000.5) deprecated EditorUtility.InstanceIDToObject(int) in favor of
            // EditorUtility.EntityIdToObject(EntityId); instanceID is an EntityId on 6.5+ and an int otherwise.
#if UNITY_6000_5_OR_NEWER
            GameObject obj = EditorUtility.EntityIdToObject(instanceID) as GameObject;
#else
            GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
            if (obj == null) return;

            if (obj == Fighter.gameObject)
            {
                Rect rect = new Rect(selectionRect.xMax - 60, selectionRect.y, 60, selectionRect.height);
                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = Color.green;
                GUI.Label(rect, "IK Setup", style);
            }
        }
        #endregion
    }
}
