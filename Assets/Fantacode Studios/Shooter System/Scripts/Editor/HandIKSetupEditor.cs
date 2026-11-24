using FS_Core;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS_ShooterSystem
{
    /// <summary>
    /// Scene/setup window for the NEW aiming architecture's hand IK.
    ///
    /// Edits the grip + elbow for either hand — Support (off) or Hold (main / weapon)
    /// — for the equipped character and weapon, separately for the Aim and Idle poses.
    /// Editing a character auto-creates and stores that character's override
    /// (<see cref="HandIKReference"/>) and enables that pose at runtime. The green
    /// "Sync to Default" toggle promotes the character grip to the default and mirrors
    /// future edits & resets; off, they touch only the current character.
    /// </summary>
    public class HandIKSetupEditor : EditorWindow
    {
        /// <summary>True while this window is open.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True only while THIS tool is actively in setup — the legacy IK setup suppresses its gizmos then so they don't overlap (but works normally when this window is just open).</summary>
        public static bool IsSetupActive { get; private set; }

        public static ShooterFighter Fighter { get; private set; }

        // The open window, so a scene-gizmo click can repaint the Edit Target toolbar.
        static HandIKSetupEditor instance;

        // When ON, every edit/reset is mirrored into the weapon default in real time.
        static bool syncToDefault;

        // Pose being authored: false = Aim, true = Idle.
        static bool editIdle;

        // Arm being authored: false = Support (off) hand, true = Hold (main) hand.
        static bool editHold;

        // Scene handle target: 0 = hand, 1 = elbow.
        static int activeElement;
        static readonly string[] elementTabs = { "Hand", "Elbow" };
        static readonly string[] poseTabs = { "Aim", "Idle" };
        static readonly string[] armTabs = { "Support", "Hold (Main)" };

        // Live hand-handle pose. Synced from the IK target every frame except while a
        // handle is being dragged (so the IK-driven target can't feed its motion back).
        static Vector3 handPos;
        static Quaternion handRot;

        // Offset reference frame (the weapon the target hangs off) + the bone-tracking
        // helper's local pose. Kept fresh while idle, frozen during a drag so the hold
        // hand — which carries the weapon — can't feed its IK motion into the offset.
        static Matrix4x4 frozenWeaponToWorld = Matrix4x4.identity;
        static Quaternion frozenWeaponRot = Quaternion.identity;
        static Vector3 frozenHelperLocalPos;
        static Vector3 frozenHelperLocalEuler;

        // Elbow handle world pose + its frozen base (the elbow bone, which the IK moves
        // as the pole offset changes) — same freeze-during-drag anti-feedback as the hand.
        static Vector3 elbowPolePos;
        static Vector3 frozenElbowBase;

        // Palette — one consistent set of colours across window + scene.
        static readonly Color activeGreen = new Color(0.24f, 0.78f, 0.42f);
        static readonly Color stopRed     = new Color(0.86f, 0.32f, 0.30f);
        static readonly Color mutedBtn    = new Color(0.42f, 0.44f, 0.48f);
        static readonly Color handGreen   = new Color(0.30f, 0.90f, 0.40f);   // hand gizmo
        static readonly Color elbowBlue   = new Color(0.30f, 0.55f, 1.00f);   // elbow gizmo
        static readonly Color armLine     = new Color(0.70f, 0.70f, 0.70f, 0.40f);

        [MenuItem("Tools/FS System/Shooter/Hand IK Setup (New Aiming)")]
        public static void ShowWindow()
        {
            var win = GetWindow<HandIKSetupEditor>("Hand IK");
            win.minSize = new Vector2(330, 330);
        }

        public static void SetFighter(ShooterFighter shooterFighter)
        {
            if (!ReferenceEquals(Fighter, shooterFighter) && shooterFighter != null)
            {
                Fighter = shooterFighter;
                EditorGUIUtility.PingObject(Fighter.gameObject);
            }
        }

        void OnEnable()
        {
            IsOpen = true;
            instance = this;
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChangedGlobal;
            SceneView.RepaintAll();
        }

        void OnDisable()
        {
            IsOpen = false;
            IsSetupActive = false;
            if (instance == this) instance = null;
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChangedGlobal;
            Tools.hidden = false;

            if (Fighter != null)
            {
                Fighter.IkSetupMode = false;
                Fighter.StopAiming();
                Fighter = null;
            }
            SceneView.RepaintAll();
        }

        void OnSelectionChangedGlobal()
        {
            if (Selection.activeGameObject != null)
                SetFighter(Selection.activeGameObject.GetComponent<ShooterFighter>());
            Repaint();
        }

        // ---------------------------------------------------------------- Window UI

        void OnGUI()
        {
            DrawTitle();

            if (Fighter == null)
            {
                if (Selection.activeGameObject != null)
                    SetFighter(Selection.activeGameObject.GetComponent<ShooterFighter>());

                if (Fighter == null)
                {
                    EditorGUILayout.HelpBox("Select a character with a ShooterFighter component to begin.", MessageType.Info);
                    return;
                }
            }

            StopIfModeChanged();
            DrawWarnings();
            DrawSetupButton();

            if (!IsSetupActive) return;

            if (Fighter.CurrentWeapon == null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(Application.isPlaying
                    ? "No weapon equipped. Equip a weapon to edit its grip."
                    : "Enter Play Mode and equip a weapon to edit its grip.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawArmTabs();
                EditorGUILayout.Space(4);
                DrawPoseTabs();
                EditorGUILayout.Space(4);
                DrawElementTabs();
                EditorGUILayout.LabelField($"Move axes: {Tools.pivotRotation}  ·  toggle Global/Local in the scene toolbar.", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawUpperBody();
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSyncToggle();
            }

            EditorGUILayout.Space(4);
            DrawResetSection();
        }

        static void DrawTitle()
        {
            EditorGUILayout.Space(4);
            var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            EditorGUILayout.LabelField("Hand IK  ·  New Aiming", style);
            DrawSeparator();
        }

        void DrawWarnings()
        {
            if (Fighter.CurrentWeapon != null && Fighter.characterID == null)
                EditorGUILayout.HelpBox("No CharacterID on this character — edits fall back to the weapon Default (per-character overrides can't be matched at runtime).", MessageType.Warning);
        }

        void DrawSetupButton()
        {
            EditorGUILayout.Space(4);

            if (!IsSetupActive)
            {
                // Only usable while the character is in New aiming mode.
                if (Fighter.AimingMode != AimingMode.New)
                {
                    EditorGUILayout.HelpBox("Set the ShooterFighter's Aiming Mode to New to use Hand IK setup.", MessageType.Warning);
                    DrawDisabledStart();
                    return;
                }
                // Mutually exclusive with the legacy IK setup (they share IkSetupMode).
                if (ShooterIKSetupEditor.IsSetupActive)
                {
                    EditorGUILayout.HelpBox("Legacy IK Setup is running. Stop it before starting Hand IK setup.", MessageType.Warning);
                    DrawDisabledStart();
                    return;
                }
            }

            using (new BgColor(IsSetupActive ? stopRed : activeGreen))
            {
                if (GUILayout.Button(IsSetupActive ? "Stop Setup" : "Start Setup", GUILayout.Height(32)))
                {
                    if (IsSetupActive) StopSetup();
                    else StartSetup();
                }
            }
        }

        static void DrawDisabledStart()
        {
            using (new EditorGUI.DisabledScope(true))
            using (new BgColor(mutedBtn))
                GUILayout.Button("Start Setup", GUILayout.Height(32));
        }

        static void StartSetup()
        {
            IsSetupActive = true;
            if (Fighter != null) Fighter.IkSetupMode = true;
            ApplyPose();
        }

        static void StopSetup()
        {
            IsSetupActive = false;
            if (Fighter != null)
            {
                Fighter.IkSetupMode = false;
                Fighter.StopAiming();
            }
            Tools.hidden = false;
            Persist();
            RepaintWindow();
        }

        // Auto-stop if the character's aiming mode changed away from New while running.
        static void StopIfModeChanged()
        {
            if (IsSetupActive && (Fighter == null || Fighter.AimingMode != AimingMode.New))
                StopSetup();
        }

        void DrawArmTabs()
        {
            EditorGUILayout.LabelField("Arm", EditorStyles.miniBoldLabel);
            int cur = editHold ? 1 : 0;
            int next = GUILayout.Toolbar(cur, armTabs, GUILayout.Height(26));
            if (next != cur)
            {
                editHold = next == 1;
                SceneView.RepaintAll();
            }
        }

        void DrawPoseTabs()
        {
            EditorGUILayout.LabelField("Pose", EditorStyles.miniBoldLabel);
            int cur = editIdle ? 1 : 0;
            int next = GUILayout.Toolbar(cur, poseTabs, GUILayout.Height(26));
            if (next != cur)
            {
                editIdle = next == 1;
                ApplyPose();
                SceneView.RepaintAll();
            }
        }

        void DrawElementTabs()
        {
            EditorGUILayout.LabelField("Edit Target", EditorStyles.miniBoldLabel);
            int next = GUILayout.Toolbar(activeElement, elementTabs, GUILayout.Height(26));
            if (next != activeElement)
            {
                activeElement = next;
                SceneView.RepaintAll();
            }
        }

        // Live edit of the weapon's upper-body aim rotation (3-bone weights + Y twist).
        // Per-weapon (not per-character); only affects the aim pose.
        void DrawUpperBody()
        {
            EditorGUILayout.LabelField("Upper Body", EditorStyles.miniBoldLabel);

            var ub = Fighter.CurrentWeapon.upperBodyIK;
            EditorGUI.BeginChangeCheck();

            float spine = EditorGUILayout.Slider(
                new GUIContent("Spine Weight", "Fraction of the aim rotation applied at the spine bone."),
                ub.spineWeight, 0f, 1f);
            float chest = EditorGUILayout.Slider(
                new GUIContent("Chest Weight", "Fraction of the aim rotation applied at the chest bone."),
                ub.chestWeight, 0f, 1f);
            float upperChest = EditorGUILayout.Slider(
                new GUIContent("Upper Chest Weight", "Fraction of the aim rotation applied at the upper-chest bone."),
                ub.upperChestWeight, 0f, 1f);
            float y = EditorGUILayout.Slider(
                new GUIContent("Y Rotation Offset", "Constant torso twist (degrees) applied to the upper body while aiming."),
                ub.yRotationOffset, -90f, 90f);
            float smooth = EditorGUILayout.Slider(
                new GUIContent("Rotation Smooth", "How quickly the upper body eases toward the aim rotation (higher = snappier). Also controls how fast the posture blends in/out."),
                ub.rotationSmoothSpeed, 0f, 30f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Fighter.CurrentWeapon, "Upper body IK");
                ub.spineWeight = spine;
                ub.chestWeight = chest;
                ub.upperChestWeight = upperChest;
                ub.yRotationOffset = y;
                ub.rotationSmoothSpeed = smooth;
                EditorUtility.SetDirty(Fighter.CurrentWeapon);
            }

            EditorGUILayout.LabelField($"Weights sum ≈ {(ub.spineWeight + ub.chestWeight + ub.upperChestWeight):0.00}  ·  ~1 = weapon fully on target.", EditorStyles.miniLabel);
            if (editIdle)
                EditorGUILayout.LabelField("Applies while aiming — switch Pose to Aim to preview.", EditorStyles.miniLabel);
        }

        void DrawSyncToggle()
        {
            using (new BgColor(syncToDefault ? activeGreen : mutedBtn))
            {
                if (GUILayout.Button(syncToDefault ? "Sync to Default:  ON" : "Sync to Default:  OFF", GUILayout.Height(24)))
                {
                    syncToDefault = !syncToDefault;
                    // Turning sync on immediately promotes this character's grip to the default.
                    if (syncToDefault)
                        SyncCharacterToDefault();
                }
            }
            EditorGUILayout.LabelField(
                syncToDefault ? "Copies this character to the default now, and mirrors future edits & resets to the default."
                              : "Edits & resets apply to this character only.",
                EditorStyles.miniLabel);
        }

        void DrawResetSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(syncToDefault ? "Reset  (character + default)" : "Reset  (character)", EditorStyles.miniBoldLabel);

                using (new BgColor(mutedBtn))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Hand Position", GUILayout.Height(22)))
                            ResetPart("Reset hand position", d => d.positionOffset = Vector3.zero);
                        if (GUILayout.Button("Hand Rotation", GUILayout.Height(22)))
                            ResetPart("Reset hand rotation", d => d.rotationOffset = Vector3.zero);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Elbow Position", GUILayout.Height(22)))
                            ResetPart("Reset elbow position", d => d.elbowOffset = Vector3.zero);
                        if (GUILayout.Button("Reset All", GUILayout.Height(22)))
                            ResetPart("Reset all", d => { d.positionOffset = Vector3.zero; d.rotationOffset = Vector3.zero; d.elbowOffset = Vector3.zero; });
                    }
                }
            }
        }

        // Match the character's actual pose to the tab being authored.
        static void ApplyPose()
        {
            if (Fighter == null) return;
            if (editIdle) { if (Fighter.IsAiming) Fighter.StopAiming(); }
            else { if (!Fighter.IsAiming) Fighter.StartAiming(); }
        }

        // ---------------------------------------------------------------- Editing

        // Settings for the arm currently being authored.
        static HandIKSettings CurrentSettings() =>
            editHold ? Fighter.CurrentWeapon.holdHandIK : Fighter.CurrentWeapon.supportHandIK;

        // Auto-creates (if needed) and returns the current character's grip data for the given pose.
        static HandIKData EnsureCharacterData(bool aim)
        {
            var settings = CurrentSettings();

            var reference = ResolveReference();
            if (reference != null) return aim ? reference.aimData : reference.idleData;

            // Without a CharacterID we can't match an override at runtime — edit the default.
            if (Fighter.characterID == null)
                return aim ? settings.aimData : settings.idleData;

            // Seed the new override from the current defaults so nothing jumps.
            reference = new HandIKReference
            {
                characterReferences = new List<CharacterReference>
                {
                    new CharacterReference { characterId = Fighter.characterID.id, avatar = Fighter.Animator.avatar }
                },
                aimData = Clone(settings.aimData),
                idleData = Clone(settings.idleData)
            };
            settings.references.Add(reference);
            Fighter.UpdateExistingHandIKReferences();
            return aim ? reference.aimData : reference.idleData;
        }

        static HandIKData Clone(HandIKData d) => new HandIKData
        {
            positionOffset = d.positionOffset,
            rotationOffset = d.rotationOffset,
            elbowOffset = d.elbowOffset
        };

        // Applies 'mutate' to the current pose's character data, and to the weapon
        // default too when Sync to Default is on. Shared by editing and resetting.
        static void MutatePose(System.Action<HandIKData> mutate)
        {
            var settings = CurrentSettings();
            bool aim = !editIdle;
            mutate(EnsureCharacterData(aim));
            if (syncToDefault)
                mutate(aim ? settings.aimData : settings.idleData);
            EditorUtility.SetDirty(Fighter.CurrentWeapon);
        }

        static void ApplyEdit(System.Action<HandIKData> mutate)
        {
            Undo.RecordObject(Fighter.CurrentWeapon, "Hand IK edited");
            MutatePose(mutate);

            // Authoring a pose's grip enables it at runtime, so it keeps working once
            // setup stops. (Setup force-previews the IK; this flag gates the real game.)
            var s = CurrentSettings();
            if (editIdle) s.enableIdle = true; else s.enableAim = true;
            EditorUtility.SetDirty(Fighter.CurrentWeapon);
        }

        void ResetPart(string undo, System.Action<HandIKData> zero)
        {
            Undo.RecordObject(Fighter.CurrentWeapon, undo);
            MutatePose(zero);
            Persist();
        }

        // Copies this character's current-pose grip onto the weapon default. Called
        // when Sync to Default is switched on.
        void SyncCharacterToDefault()
        {
            var weapon = Fighter.CurrentWeapon;
            if (weapon == null) return;

            var settings = CurrentSettings();
            bool aim = !editIdle;
            var charData = Fighter.ResolveHandIKData(!editHold, aim);   // override if any, else the default itself
            var def = aim ? settings.aimData : settings.idleData;
            if (charData == null || ReferenceEquals(charData, def)) return; // nothing distinct to copy

            Undo.RecordObject(weapon, "Sync character to default");
            def.positionOffset = charData.positionOffset;
            def.rotationOffset = charData.rotationOffset;
            def.elbowOffset = charData.elbowOffset;

            EditorUtility.SetDirty(weapon);
            Persist();
        }

        static void Persist()
        {
            if (Fighter != null && Fighter.CurrentWeapon != null)
                EditorUtility.SetDirty(Fighter.CurrentWeapon);
            AssetDatabase.SaveAssets();
        }

        // Per-character override for the equipped character on the current arm (null = default).
        static HandIKReference ResolveReference()
        {
            if (Fighter == null || Fighter.CurrentWeapon == null || Fighter.characterID == null) return null;
            return CurrentSettings().references.Find(
                r => r.characterReferences != null && r.characterReferences.Count > 0 &&
                     r.characterReferences.FirstOrDefault(c => c.characterId == Fighter.characterID.id || c.avatar == Fighter.Animator.avatar) != null);
        }

        // ---------------------------------------------------------------- Scene GUI

        static bool SetupActive() =>
            IsSetupActive
            && Application.isPlaying && Fighter != null && Fighter.CurrentWeapon != null
            && Fighter.AimingMode == AimingMode.New
            && !Fighter.CurrentWeapon.isDualItem
            && Fighter.CurrentShooterWeaponObject != null
            && Fighter.CurrentShooterWeaponObject.aimController != null
            && Fighter.IkSetupMode;

        static void OnSceneGUI(SceneView sceneView)
        {
            StopIfModeChanged();

            if (!SetupActive())
            {
                Tools.hidden = false;
                return;
            }

            Tools.hidden = true; // hide Unity's gizmo so our handles are unambiguous

            // Both arms' gizmos are always drawn. The active target (current Arm + Edit
            // Target) shows a manipulation handle; the other three show clickable spheres
            // that switch to them (updating the Arm and Edit Target toolbars).
            DrawArmContext(false); DrawArmContext(true);
            DrawHandHandle(false); DrawHandHandle(true);
            DrawElbowHandle(false); DrawElbowHandle(true);
        }

        // Resolves an arm bone for the given arm (hold = holder side, support = opposite).
        static Transform ArmBone(bool hold, HumanBodyBones rightVariant, HumanBodyBones leftVariant)
        {
            if (Fighter == null || Fighter.Animator == null || Fighter.CurrentWeapon == null) return null;
            bool holdRight = Fighter.CurrentWeapon.holderBone == HumanBodyBones.RightHand;
            bool useRight = hold ? holdRight : !holdRight;
            return Fighter.Animator.GetBoneTransform(useRight ? rightVariant : leftVariant);
        }

        // A thin arm line for context — no joint spheres.
        static void DrawArmContext(bool hold)
        {
            Transform upper = ArmBone(hold, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperArm);
            Transform lower = ArmBone(hold, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerArm);
            Transform hand = ArmBone(hold, HumanBodyBones.RightHand, HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) return;

            Handles.color = armLine;
            Handles.DrawAAPolyLine(3f, upper.position, lower.position, hand.position);
        }

        static void DrawHandHandle(bool hold)
        {
            var wobj = Fighter.CurrentShooterWeaponObject;
            Transform ikTarget = hold ? wobj.holdHandIkTarget : wobj.supportHandIkTarget;
            Transform helper = hold ? wobj.holdHandIkSetupHelperTarget : wobj.supportHandIkSetupHelperTarget;
            Transform sourceBone = ArmBone(hold, HumanBodyBones.RightHand, HumanBodyBones.LeftHand);
            if (ikTarget == null || helper == null || sourceBone == null || ikTarget.parent == null) return;

            // Not the active target → clickable green sphere that selects this hand.
            if (!(editHold == hold && activeElement == 0))
            {
                if (ClickableSphere(ikTarget.position, handGreen))
                {
                    editHold = hold;
                    activeElement = 0;
                    RepaintWindow();
                }
                return;
            }

            // Active → move/rotate handle. While not dragging, follow the live grip and
            // keep the offset reference frame fresh; while dragging, freeze both so
            // applying the offset can't move the frame it was measured in (hold-hand
            // weapon-carry feedback → jitter).
            if (GUIUtility.hotControl == 0)
            {
                handPos = ikTarget.position;
                handRot = ikTarget.rotation;
                frozenWeaponToWorld = ikTarget.parent.localToWorldMatrix;
                frozenWeaponRot = ikTarget.parent.rotation;
                frozenHelperLocalPos = helper.localPosition;
                frozenHelperLocalEuler = helper.localEulerAngles;
            }

            Handles.color = handGreen;
            Handles.DrawDottedLine(sourceBone.position, handPos, 3f);

            EditorGUI.BeginChangeCheck();
            handPos = Handles.PositionHandle(handPos, PosHandleRotation(handRot));
            handRot = Handles.RotationHandle(handRot, handPos);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 posOffset = frozenWeaponToWorld.inverse.MultiplyPoint3x4(handPos) - frozenHelperLocalPos;
                Vector3 rotOffset = (Quaternion.Inverse(frozenWeaponRot) * handRot).eulerAngles - frozenHelperLocalEuler;
                ApplyEdit(d => { d.positionOffset = posOffset; d.rotationOffset = rotOffset; });
            }
        }

        static void DrawElbowHandle(bool hold)
        {
            Transform lowerArm = hold ? Fighter.HoldLowerArm : Fighter.SupportLowerArm;
            Transform root = Fighter.CharacterRoot;
            if (lowerArm == null || root == null) return;

            HandIKData data = Fighter.ResolveHandIKData(!hold, !editIdle);
            if (data == null) return;

            Vector3 livePole = lowerArm.position + root.TransformVector(data.elbowOffset);

            // Not the active target → clickable blue sphere that selects this elbow.
            if (!(editHold == hold && activeElement == 1))
            {
                if (ClickableSphere(livePole, elbowBlue))
                {
                    editHold = hold;
                    activeElement = 1;
                    RepaintWindow();
                }
                return;
            }

            // Active → move handle. While not dragging, follow the live pole and keep the
            // elbow base fresh; while dragging, freeze both so applying the offset (which
            // repositions the elbow bone via IK) can't move the reference → jitter.
            if (GUIUtility.hotControl == 0)
            {
                elbowPolePos = livePole;
                frozenElbowBase = lowerArm.position;
            }

            Handles.color = elbowBlue;
            Handles.DrawDottedLine(frozenElbowBase, elbowPolePos, 3f);

            EditorGUI.BeginChangeCheck();
            elbowPolePos = Handles.PositionHandle(elbowPolePos, PosHandleRotation(root.rotation));
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newOffset = root.InverseTransformVector(elbowPolePos - frozenElbowBase);
                ApplyEdit(d => d.elbowOffset = newOffset);
            }
        }

        // ---------------------------------------------------------------- Helpers

        // Orients a move handle by the scene toolbar's Pivot Rotation toggle:
        // Global = world axes, Local = the passed local rotation.
        static Quaternion PosHandleRotation(Quaternion local) =>
            Tools.pivotRotation == PivotRotation.Global ? Quaternion.identity : local;

        // A clickable coloured sphere; returns true the frame it's clicked.
        static bool ClickableSphere(Vector3 point, Color color)
        {
            Handles.color = color;
            float size = HandleUtility.GetHandleSize(point) * 0.1f;
            return Handles.Button(point, Quaternion.identity, size, size, Handles.SphereHandleCap);
        }

        // Repaints the window so the Edit Target toolbar follows a scene-gizmo selection.
        static void RepaintWindow()
        {
            if (instance != null) instance.Repaint();
        }

        static void DrawSeparator()
        {
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
        }

        // Scoped GUI.backgroundColor override, restored on dispose.
        readonly struct BgColor : System.IDisposable
        {
            readonly Color prev;
            public BgColor(Color c) { prev = GUI.backgroundColor; GUI.backgroundColor = c; }
            public void Dispose() { GUI.backgroundColor = prev; }
        }
    }
}
