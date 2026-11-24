using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FS_CombatCore
{

    //[CustomEditor(typeof(CombatWeapon))]
    public class CombatWeaponEditor : EquippableItemEditor
    {
        // New properties
        SerializedProperty reactionData;
        SerializedProperty canBlock;
        SerializedProperty blocking;
        SerializedProperty blockedDamage;
        SerializedProperty blockReactionData;
        SerializedProperty overrideMoveSpeed;
        SerializedProperty combatMoveSpeed;
        SerializedProperty locomotionClipSet2D;
        SerializedProperty overrideDodge;
        SerializedProperty dodgeData;
        SerializedProperty overrideRoll;
        SerializedProperty rollData;

        // Foldout states
        private bool showReactionSettings = true;
        private bool showBlockingSettings = true;
        private bool showMovementSettings = true;
        private bool showDodgeSettings = true;

        public override void OnEnable()
        {
            // New properties
            reactionData = serializedObject.FindProperty("reactionData");
            canBlock = serializedObject.FindProperty("canBlock");
            blocking = serializedObject.FindProperty("blocking");
            blockedDamage = serializedObject.FindProperty("blockedDamage");
            blockReactionData = serializedObject.FindProperty("blockReactionData");
            overrideMoveSpeed = serializedObject.FindProperty("overrideMoveSpeed");
            combatMoveSpeed = serializedObject.FindProperty("combatMoveSpeed");
            locomotionClipSet2D = serializedObject.FindProperty("locomotionClipSet2D");
            overrideDodge = serializedObject.FindProperty("overrideDodge");
            dodgeData = serializedObject.FindProperty("dodgeData");
            overrideRoll = serializedObject.FindProperty("overrideRoll");
            rollData = serializedObject.FindProperty("rollData");

            base.OnEnable();
        }
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Combat Weapon Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Reaction Settings
            DrawFoldout(ref showReactionSettings, "Reaction Settings", () =>
            {
                EditorGUILayout.PropertyField(reactionData);
            });

            // Blocking Settings
            DrawFoldout(ref showBlockingSettings, "Blocking Settings", () =>
            {
                EditorGUILayout.PropertyField(canBlock);
                if (canBlock.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(blocking);
                    EditorGUILayout.PropertyField(blockedDamage);
                    EditorGUILayout.PropertyField(blockReactionData);
                    EditorGUI.indentLevel--;
                }
            });

            // Movement Settings
            DrawFoldout(ref showMovementSettings, "Movement Settings", () =>
            {
                EditorGUILayout.PropertyField(overrideMoveSpeed);
                if (overrideMoveSpeed.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(combatMoveSpeed);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.PropertyField(locomotionClipSet2D);
            });

            // Dodge and Roll Settings
            DrawFoldout(ref showDodgeSettings, "Dodge & Roll Settings", () =>
            {
                EditorGUILayout.PropertyField(overrideDodge);
                if (overrideDodge.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(dodgeData);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);

                EditorGUILayout.PropertyField(overrideRoll);
                if (overrideRoll.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(rollData);
                    EditorGUI.indentLevel--;
                }
            });

            serializedObject.ApplyModifiedProperties();
        }
        private new void DrawFoldout(ref bool toggle, string label, System.Action drawer)
        {
            //EditorGUILayout.EndFoldoutHeaderGroup();
            toggle = EditorGUILayout.Foldout(toggle, label, true);
            if (toggle)
            {
                EditorGUI.indentLevel++;
                drawer();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            //EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}