using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using FS_Core;
using FS_CombatCore;

namespace FS_ShooterSystem
{
    [CustomEditor(typeof(ShooterWeapon))]
    public class ShooterWeaponEditor : EquippableItemEditor
    {
        private ShooterWeapon shooterWeaponData;

        private SerializedProperty ammoData;
        private SerializedProperty range;
        private SerializedProperty soundRange;
        private SerializedProperty useReloadAnimationDuration;
        private SerializedProperty reloadTime;
        private SerializedProperty fireAudioClip;
        private SerializedProperty triggerAudioClip;
        private SerializedProperty loadAmmoAudioClip;
        private SerializedProperty autoReload;
        private SerializedProperty preventManualReload;
        private SerializedProperty playLoadAnimationPerShot;
        private SerializedProperty ammoEnableTime;
        private SerializedProperty reloadAudioClip;
        private SerializedProperty isBurst;
        private SerializedProperty fireType;
        private SerializedProperty burstFireBulletCount;
        private SerializedProperty burstSpreadingAngleRange;
        private SerializedProperty weaponRecoilInfo;
        private SerializedProperty bulletSpreadRadius;
        private SerializedProperty enableRecoil;
        private SerializedProperty hipFire;
        private SerializedProperty canAimWithoutAmmo;
        private SerializedProperty allowJumpingWhileAiming;
        private SerializedProperty zoomWhileAim;
        private SerializedProperty aimCameraSettings;
        private SerializedProperty aimClip;
        private SerializedProperty reloadClip;
        private SerializedProperty reloadAnimationMask;
        private SerializedProperty loadAmmoClip;
        private SerializedProperty shootClip;

        private SerializedProperty UseHandIkForIdle;
        //private SerializedProperty needIkForAim;
        private SerializedProperty aimAnimationMask;
        private SerializedProperty shootAnimationMask;
        private SerializedProperty chargeAnimationMask;

        public SerializedProperty aimIKData;
        public SerializedProperty idleIKData;
        public SerializedProperty iKReferences;

        private SerializedProperty upperBodyIK;
        private SerializedProperty supportHandIK;
        private SerializedProperty holdHandIK;

        private SerializedProperty aimedLocomotionClipSet2D;
        public SerializedProperty isChargedWeapon;
        public SerializedProperty chargeClip;
        public SerializedProperty chargeForce;
        public SerializedProperty playHitReaction;
        public SerializedProperty reactionTag;
        public SerializedProperty reactionData;

        private SerializedProperty overrideDodge;
        private SerializedProperty dodgeData;
        private SerializedProperty overrideRoll;
        private SerializedProperty rollData;

        private SerializedProperty weaponDamageType;

        private bool showWeaponSettings = false;
        private bool showFireSettings = false;
        private bool showRecoilSettings = false;
        private bool showAnimationSettings = false;
        private bool reloadSettings = false;
        private bool audioSettings = false;
        private bool ikSettings = false;
        private bool defaultIKSettings = false;
        private bool showNewAimingSettings = false;
        private bool reactionSettings = false;
        private bool showDamage = false;
        private bool showDodge = false;
        private bool showRoll = false;


        public override bool ShowDualItemProperty => true;

        public override void OnEnable()
        {

            shooterWeaponData = (ShooterWeapon)target;

            ammoData = serializedObject.FindProperty("ammoData");
            range = serializedObject.FindProperty("range");
            soundRange = serializedObject.FindProperty("soundRange");
            useReloadAnimationDuration = serializedObject.FindProperty("useReloadAnimationDuration");
            reloadTime = serializedObject.FindProperty("reloadTime");
            fireAudioClip = serializedObject.FindProperty("fireAudioClip");
            loadAmmoAudioClip = serializedObject.FindProperty("loadAmmoAudioClip");
            triggerAudioClip = serializedObject.FindProperty("triggerAudioClip");
            autoReload = serializedObject.FindProperty("autoReload");
            preventManualReload = serializedObject.FindProperty("preventManualReload");
            playLoadAnimationPerShot = serializedObject.FindProperty("playLoadAnimationPerShot");
            ammoEnableTime = serializedObject.FindProperty("ammoEnableTime");
            reloadAudioClip = serializedObject.FindProperty("reloadAudioClip");
            isBurst = serializedObject.FindProperty("isBurst");
            fireType = serializedObject.FindProperty("fireType");
            burstFireBulletCount = serializedObject.FindProperty("burstFireBulletCount");
            burstSpreadingAngleRange = serializedObject.FindProperty("burstSpreadingAngleRange");
            weaponRecoilInfo = serializedObject.FindProperty("weaponRecoilInfo");
            bulletSpreadRadius = serializedObject.FindProperty("bulletSpreadRadius");
            enableRecoil = serializedObject.FindProperty("enableRecoil");
            hipFire = serializedObject.FindProperty("hipFire");
            canAimWithoutAmmo = serializedObject.FindProperty("canAimWithoutAmmo");
            allowJumpingWhileAiming = serializedObject.FindProperty("allowJumpingWhileAiming");
            zoomWhileAim = serializedObject.FindProperty("zoomWhileAim");
            aimCameraSettings = serializedObject.FindProperty("aimCameraSettings");
            aimClip = serializedObject.FindProperty("aimClip");
            reloadClip = serializedObject.FindProperty("reloadClip");
            reloadAnimationMask = serializedObject.FindProperty("reloadAnimationMask");
            loadAmmoClip = serializedObject.FindProperty("loadAmmoClip");
            shootClip = serializedObject.FindProperty("shootClip");
            UseHandIkForIdle = serializedObject.FindProperty("UseHandIkForIdle");
            //needIkForAim = serializedObject.FindProperty("needIkForAim");
            aimAnimationMask = serializedObject.FindProperty("aimAnimationMask");
            shootAnimationMask = serializedObject.FindProperty("shootAnimationMask");
            chargeAnimationMask = serializedObject.FindProperty("chargeAnimationMask");

            idleIKData = serializedObject.FindProperty("idleIKData");
            aimIKData = serializedObject.FindProperty("aimIKData");
            iKReferences = serializedObject.FindProperty("iKReferences");

            upperBodyIK = serializedObject.FindProperty("upperBodyIK");
            supportHandIK = serializedObject.FindProperty("supportHandIK");
            holdHandIK = serializedObject.FindProperty("holdHandIK");

            aimedLocomotionClipSet2D = serializedObject.FindProperty("aimedLocomotionClipSet2D");
            isChargedWeapon = serializedObject.FindProperty("isChargedWeapon");
            chargeClip = serializedObject.FindProperty("chargeClip");
            chargeForce = serializedObject.FindProperty("chargeForce");

            playHitReaction = serializedObject.FindProperty("playReactionOnTarget");
            reactionTag = serializedObject.FindProperty("targetReactionTag");
            reactionData = serializedObject.FindProperty("reactionData");


            overrideDodge = serializedObject.FindProperty("overrideDodge");
            dodgeData = serializedObject.FindProperty("dodgeData");
            overrideRoll = serializedObject.FindProperty("overrideRoll");
            rollData = serializedObject.FindProperty("rollData");

            weaponDamageType = serializedObject.FindProperty("weaponDamageType");

            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            // General Weapon Settings
            DrawFoldout(ref showWeaponSettings, "Weapon Settings", () =>
            {
                EditorGUILayout.PropertyField(ammoData);
                EditorGUILayout.PropertyField(range);
                EditorGUILayout.PropertyField(soundRange);
                EditorGUILayout.PropertyField(isChargedWeapon);
                EditorGUILayout.PropertyField(aimCameraSettings);
            });

            // Fire & Spread
            DrawFoldout(ref showFireSettings, "Firing Settings", () =>
            {
                EditorGUILayout.PropertyField(fireType);
                EditorGUILayout.PropertyField(isBurst);
                if (isBurst.boolValue)
                {
                    EditorGUILayout.PropertyField(burstFireBulletCount);
                    EditorGUILayout.PropertyField(burstSpreadingAngleRange);
                }
                EditorGUILayout.PropertyField(bulletSpreadRadius);
                EditorGUILayout.PropertyField(hipFire);
                EditorGUILayout.PropertyField(zoomWhileAim);
                EditorGUILayout.PropertyField(canAimWithoutAmmo);
                EditorGUILayout.PropertyField(allowJumpingWhileAiming);
            });

            // Recoil
            DrawFoldout(ref showRecoilSettings, "Recoil Settings", () =>
            {
                EditorGUILayout.PropertyField(enableRecoil);
                if (enableRecoil.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(weaponRecoilInfo);
                    EditorGUI.indentLevel--;
                }
            });

            // Ammo / Reload
            DrawFoldout(ref reloadSettings, "Reload", () =>
            {
                EditorGUILayout.PropertyField(useReloadAnimationDuration);
                if (!useReloadAnimationDuration.boolValue)
                    EditorGUILayout.PropertyField(reloadTime);
                EditorGUILayout.PropertyField(ammoEnableTime);
                EditorGUILayout.PropertyField(autoReload);
                EditorGUILayout.PropertyField(preventManualReload);

                EditorGUILayout.PropertyField(playLoadAnimationPerShot);
            });
            DrawFoldout(ref audioSettings, "Audio", () =>
            {
                EditorGUILayout.PropertyField(fireAudioClip);
                EditorGUILayout.PropertyField(triggerAudioClip);
                EditorGUILayout.PropertyField(reloadAudioClip);
                if (playLoadAnimationPerShot.boolValue)
                    EditorGUILayout.PropertyField(loadAmmoAudioClip);
            });

            // Animation
            DrawFoldout(ref showAnimationSettings, "Animation Settings", () =>
            {
                EditorGUILayout.PropertyField(aimedLocomotionClipSet2D, new GUIContent("Aimed Locomotion"));
                EditorGUILayout.PropertyField(aimClip);

                if (shooterWeaponData.aimClip.clip != null)
                    EditorGUILayout.PropertyField(aimAnimationMask);

                EditorGUILayout.PropertyField(reloadClip);
                if (shooterWeaponData.reloadClip.clip != null)
                    EditorGUILayout.PropertyField(reloadAnimationMask);
                if (playLoadAnimationPerShot.boolValue)
                    EditorGUILayout.PropertyField(loadAmmoClip);
                EditorGUILayout.PropertyField(shootClip);
                if (shooterWeaponData.shootClip.clip != null)
                    EditorGUILayout.PropertyField(shootAnimationMask);
                if (isChargedWeapon.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(chargeClip);
                    if(shooterWeaponData.chargeClip.clip != null)
                        EditorGUILayout.PropertyField(chargeAnimationMask);
                    EditorGUILayout.PropertyField(chargeForce);
                    EditorGUI.indentLevel--;
                }
            });

            // Reaction
            DrawFoldout(ref reactionSettings, "Reaction Settings", () =>
            {
                EditorGUILayout.PropertyField(reactionData);
                EditorGUILayout.PropertyField(playHitReaction);
                if (playHitReaction.boolValue)
                    EditorGUILayout.PropertyField(reactionTag);
            });

            // IK
            DrawFoldout(ref ikSettings, "IK Settings(Legacy)", () =>
            {

                EditorGUILayout.PropertyField(UseHandIkForIdle);
                DrawFoldout(ref defaultIKSettings, "Default IK Settings", () =>
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    //if (needIkForIdle.boolValue)
                    {
                        //shooterWeaponData.idleIKData.weaponHoldingByRightHand = shooterWeaponData.holderBone == HumanBodyBones.RightHand || shooterWeaponData.isDualItem;
                        //shooterWeaponData.idleIKData.weaponHoldingByLeftHand = shooterWeaponData.holderBone == HumanBodyBones.LeftHand || shooterWeaponData.isDualItem;
                        EditorGUILayout.PropertyField(idleIKData, true);
                    }
                    //if (needIkForAim.boolValue)
                    {
                        //shooterWeaponData.aimIKData.weaponHoldingByLeftHand = shooterWeaponData.holderBone == HumanBodyBones.LeftHand || shooterWeaponData.isDualItem;
                        //shooterWeaponData.aimIKData.weaponHoldingByRightHand = shooterWeaponData.holderBone == HumanBodyBones.RightHand || shooterWeaponData.isDualItem;
                        EditorGUILayout.PropertyField(aimIKData, true);
                    }
                    EditorGUILayout.EndVertical();
                }, false);

                EditorGUILayout.BeginVertical(GUI.skin.box);
                //foreach (var ikRef in shooterWeaponData.iKReferences)
                //{
                //    ikRef.idleIKData.weaponHoldingByRightHand = shooterWeaponData.holderBone == HumanBodyBones.RightHand || shooterWeaponData.isDualItem;
                //    ikRef.idleIKData.weaponHoldingByLeftHand = shooterWeaponData.holderBone == HumanBodyBones.LeftHand || shooterWeaponData.isDualItem;

                //    ikRef.aimIKData.weaponHoldingByRightHand = shooterWeaponData.holderBone == HumanBodyBones.RightHand || shooterWeaponData.isDualItem;
                //    ikRef.aimIKData.weaponHoldingByLeftHand = shooterWeaponData.holderBone == HumanBodyBones.LeftHand || shooterWeaponData.isDualItem;
                //}
                EditorGUILayout.PropertyField(iKReferences, true);
                EditorGUILayout.EndVertical();
            });

            // New Aiming (upper-body rotation + support-hand IK). Active only when the
            // character's ShooterFighter has Aiming Mode set to New.
            DrawFoldout(ref showNewAimingSettings, "New Aiming Settings", () =>
            {
                EditorGUILayout.HelpBox(
                    "Used only when the character's ShooterFighter has Aiming Mode set to New.\n\n" +
                    "Support Hand (off) and Hold Hand (main) IK each have separate Aim and Idle grips (defaults + per-character " +
                    "References), independent of the legacy Aim IK Data. Enable Idle to keep a hand on the weapon while not aiming. " +
                    "Set the grips visually with Tools > FS System > Shooter > Hand IK Setup (New Aiming) — pick Arm + Pose, default " +
                    "or per-character — or edit the offsets below directly.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(upperBodyIK, true);
                EditorGUILayout.PropertyField(supportHandIK, true);
                EditorGUILayout.PropertyField(holdHandIK, true);
            });

            // Damage
            DrawFoldout(ref showDamage, "Damage Settings", () =>
            {
                EditorGUILayout.PropertyField(weaponDamageType);
            });

            // Dodge
            DrawFoldout(ref showDodge, "Dodge Settings", () =>
            {
                EditorGUILayout.PropertyField(overrideDodge);
                if (overrideDodge.boolValue)
                    EditorGUILayout.PropertyField(dodgeData);
            });

            // Roll
            DrawFoldout(ref showRoll, "Roll Settings", () =>
            {
                EditorGUILayout.PropertyField(overrideRoll);
                if (overrideRoll.boolValue)
                    EditorGUILayout.PropertyField(rollData);
            });

            serializedObject.ApplyModifiedProperties();
        }


        private void DrawFoldout(ref bool toggle, string label, System.Action drawer, bool needIndent = true)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox); // Start HelpBox
            if(needIndent)
                EditorGUI.indentLevel++;
            toggle = EditorGUILayout.Foldout(toggle, label, true);
            if (toggle)
            {
                EditorGUI.indentLevel++;
                drawer();
                EditorGUI.indentLevel--;
            }
            if (needIndent)
                EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical(); // End HelpBox

        }

        static Animations currentClipType = Animations.Equip;

        public override void ChangeAnimationClip(object type = null)
        {
            base.ChangeAnimationClip(type);
            if (type != null)
                currentClipType = (Animations)type;
            switch (currentClipType)
            {
                case Animations.Equip:
                    clip = shooterWeaponData.equipClip.clip;
                    break;
                case Animations.UnEquip:
                    clip = shooterWeaponData.unEquipClip.clip;
                    break;
                default:
                    break;
            }
        }

        public override void HandleAnimationEnumPopup()
        {
            base.HandleAnimationEnumPopup();
            EditorGUI.BeginChangeCheck();
            currentClipType = (Animations)EditorGUILayout.EnumPopup(currentClipType, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                ChangeAnimationClip();
                UpdatePreview();
            }
        }

        #region Animation Setup

        private void AssignAnimationsToBlendTree(AnimatorController animatorController, int weaponID, AnimationClip aim, AnimationClip idle, AnimationClip reload,
            AnimationClip equip, AnimationClip unequip, AnimationClip shoot)
        {
            if (animatorController == null)
            {
                Debug.LogWarning("Animator Controller is not assigned.");
                return;
            }

            // Hand Layer
            // Find the layer by name
            var layer = FindLayerByName(animatorController, AnimatorLayer.armLayer.layerName);
            if (layer == null)
            {
                Debug.LogWarning($"Layer not found: Arm Layer");
                return;
            }

            // Access the state machine in the selected layer
            var stateMachine = layer.stateMachine;

            // Find the blend trees in the state machine
            var blendTrees = GetBlendTreesFromStateMachine(stateMachine);

            // Loop through blend trees and assign animations using weaponID as threshold
            foreach (var blendTree in blendTrees)
            {
                switch (blendTree.name)
                {
                    case "Aim":
                        AddOrOverrideAnimationInBlendTree(blendTree, aim, weaponID);
                        break;
                    case "Idle":
                        AddOrOverrideAnimationInBlendTree(blendTree, idle, weaponID);
                        break;
                    case "System Actions":
                        AddOrOverrideAnimationInBlendTree(blendTree, reload, weaponID);
                        break;
                    case "Equip":
                        AddOrOverrideAnimationInBlendTree(blendTree, equip, weaponID);
                        break;
                    case "UnEquip":
                        AddOrOverrideAnimationInBlendTree(blendTree, unequip, weaponID);
                        break;
                    default:
                        Debug.LogWarning("Blend Tree not handled: " + blendTree.name);
                        break;
                }
            }


            // Shooting layer
            layer = FindLayerByName(animatorController, "Shooting");
            if (layer == null)
            {
                Debug.LogWarning($"Layer not found: Shooting");
                return;
            }
            stateMachine = layer.stateMachine;
            blendTrees = GetBlendTreesFromStateMachine(stateMachine);
            foreach (var blendTree in blendTrees)
            {
                switch (blendTree.name)
                {
                    case "Shoot":
                        AddOrOverrideAnimationInBlendTree(blendTree, shoot, weaponID);
                        break;
                    default:
                        Debug.LogWarning("Blend Tree not handled: " + blendTree.name);
                        break;
                }
            }
        }

        private void AddOrOverrideAnimationInBlendTree(BlendTree blendTree, AnimationClip animationClip, int blendValue)
        {
            Undo.RecordObject(blendTree, "Add or Override Animation in Blend Tree");

            // Check if an animation with the same blend value exists
            var children = blendTree.children;
            bool blendValueExists = false;

            for (int i = 0; i < children.Length; i++)
            {
                if (Mathf.Approximately(children[i].threshold, blendValue))
                {
                    // Override existing animation
                    children[i].motion = animationClip;
                    blendValueExists = true;
                    break;
                }
            }

            

            blendTree.children = children;
            EditorUtility.SetDirty(blendTree);
            if (!blendValueExists)
            {
                blendTree.AddChild(animationClip, blendValue);
            }
        }

        private List<BlendTree> GetBlendTreesFromStateMachine(AnimatorStateMachine stateMachine)
        {
            var blendTrees = new System.Collections.Generic.List<BlendTree>();

            foreach (var state in stateMachine.states)
            {
                if (state.state.motion is BlendTree blendTree)
                {
                    blendTrees.Add(blendTree);
                }
            }

            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                blendTrees.AddRange(GetBlendTreesFromStateMachine(subStateMachine.stateMachine));
            }

            return blendTrees;
        }

        private AnimatorControllerLayer FindLayerByName(AnimatorController controller, string layerName)
        {
            foreach (var layer in controller.layers)
            {
                if (layer.name == layerName)
                    return layer;
            }
            return null;
        }

        #endregion
    }
}