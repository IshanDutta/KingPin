using FS_Core;
using UnityEditor;
using UnityEngine;

namespace FS_ShooterSystem
{
    [CustomEditor(typeof(ThrowableWeapon))]
    public class ThrowableWeaponEditor : ThrowableItemEditor
    {
        SerializedProperty damageType;
        SerializedProperty damage;
        SerializedProperty damageTypeMask;
        SerializedProperty playReactionOnTarget;
        SerializedProperty targetReactionTag;
        SerializedProperty reactionData;
        SerializedProperty explosionTrigger;
        SerializedProperty explosionMinDamage;
        SerializedProperty explosionDelay;
        SerializedProperty explosionRadius;
        SerializedProperty explosionForce;
        SerializedProperty explosionTargetLayer;
        SerializedProperty explosionEffect;
        SerializedProperty explosionSound;

        static bool _damageFoldout = true;

        public override void OnEnable()
        {
            base.OnEnable();

            damageType = serializedObject.FindProperty("damageType");
            damage = serializedObject.FindProperty("damage");
            damageTypeMask = serializedObject.FindProperty("damageTypeMask");
            playReactionOnTarget = serializedObject.FindProperty("playReactionOnTarget");
            targetReactionTag = serializedObject.FindProperty("targetReactionTag");
            reactionData = serializedObject.FindProperty("reactionData");
            explosionTrigger = serializedObject.FindProperty("explosionTrigger");
            explosionMinDamage = serializedObject.FindProperty("explosionMinDamage");
            explosionDelay = serializedObject.FindProperty("explosionDelay");
            explosionRadius = serializedObject.FindProperty("explosionRadius");
            explosionForce = serializedObject.FindProperty("explosionForce");
            explosionTargetLayer = serializedObject.FindProperty("explosionTargetLayer");
            explosionEffect = serializedObject.FindProperty("explosionEffect");
            explosionSound = serializedObject.FindProperty("explosionSound");
        }

        protected override void DrawThrowableInspector()
        {
            DrawFoldout(ref _damageFoldout, "Damage", () =>
            {
                EditorGUILayout.PropertyField(damageType, new GUIContent("Type"));
                EditorGUILayout.PropertyField(damage);
                EditorGUILayout.PropertyField(damageTypeMask, new GUIContent("Damage Type Mask"));

                if (damageType.enumValueIndex == (int)ThrowableDamageType.HitImpact)
                {
                    EditorGUILayout.PropertyField(playReactionOnTarget, new GUIContent("Play Reaction"));
                    if (playReactionOnTarget.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(targetReactionTag, new GUIContent("Reaction Tag"));
                        EditorGUILayout.PropertyField(reactionData, new GUIContent("Reaction Data"));
                        EditorGUI.indentLevel--;
                    }
                }

                if (damageType.enumValueIndex == (int)ThrowableDamageType.ExplosionImpact)
                {
                    EditorGUILayout.PropertyField(explosionTrigger, new GUIContent("Trigger"));
                    EditorGUILayout.PropertyField(explosionMinDamage, new GUIContent("Min Damage (Edge)"));

                    if (explosionTrigger.enumValueIndex == (int)ExplosionTrigger.FuseOnAim)
                        EditorGUILayout.PropertyField(explosionDelay, new GUIContent("Fuse Time"));

                    EditorGUILayout.PropertyField(explosionRadius, new GUIContent("Radius"));
                    EditorGUILayout.PropertyField(explosionForce, new GUIContent("Force"));
                    EditorGUILayout.PropertyField(explosionTargetLayer, new GUIContent("Target Layer"));
                    EditorGUILayout.PropertyField(explosionEffect, new GUIContent("Effect Prefab"));
                    EditorGUILayout.PropertyField(explosionSound, new GUIContent("Sound"));
                }
            });

            base.DrawThrowableInspector();
        }
    }
}
