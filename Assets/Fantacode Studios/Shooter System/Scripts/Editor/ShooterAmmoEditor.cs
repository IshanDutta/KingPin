using FS_Core;
using UnityEditor;

namespace FS_ShooterSystem
{

    [CustomEditor(typeof(ShooterAmmo))]
    public class ShooterAmmoEditor : ItemDataEditor
    {
        static bool ammoSettingsFoldout = false;
        static bool explosionSettingsFoldout = false;

        SerializedProperty ammoProp, hitEffectsProp;
        SerializedProperty gravityProp, maxLifetimeProp, destroyImmediateAfterHit;
        SerializedProperty overrideHitIgnoreMaskProp, hitIgnoreMaskProp;
        SerializedProperty isExplosiveProp, explosiveRadiusProp, minDamageProp, explosionForceProp, bloodEffectProp;
        SerializedProperty enableDirectHitProp;
        SerializedProperty explotionPrefabProp, explotionAudioProp, explotionLifeTimeProp;
        SerializedProperty explosionSoundRangeProp;

        public override void OnEnable()
        {
            base.OnEnable();
            ammoProp = serializedObject.FindProperty("ammo");
            hitEffectsProp = serializedObject.FindProperty("hitEffects");
            gravityProp = serializedObject.FindProperty("gravity");
            maxLifetimeProp = serializedObject.FindProperty("maxLifetime");
            destroyImmediateAfterHit = serializedObject.FindProperty("destroyImmediateAfterHit");

            overrideHitIgnoreMaskProp = serializedObject.FindProperty("overrideHitIgnoreMask");
            hitIgnoreMaskProp = serializedObject.FindProperty("hitIgnoreMask");
            isExplosiveProp = serializedObject.FindProperty("isExplosive");
            explosiveRadiusProp = serializedObject.FindProperty("explosiveRadius");
            minDamageProp = serializedObject.FindProperty("minDamage");
            explosionForceProp = serializedObject.FindProperty("explosionForce");
            enableDirectHitProp = serializedObject.FindProperty("enableDirectHit");
            bloodEffectProp = serializedObject.FindProperty("bloodEffect");

            explotionPrefabProp = serializedObject.FindProperty("explotionPrefab");
            explotionAudioProp = serializedObject.FindProperty("explotionAudio");
            explotionLifeTimeProp = serializedObject.FindProperty("explotionLifeTime");
            explosionSoundRangeProp = serializedObject.FindProperty("explosionSoundRange");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            base.OnInspectorGUI();

            DrawFoldout(ref ammoSettingsFoldout, "Ammo Settings", () =>
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(ammoProp);
                    EditorGUILayout.PropertyField(gravityProp);

                    EditorGUILayout.Space();

                    EditorGUILayout.PropertyField(overrideHitIgnoreMaskProp);
                    if (overrideHitIgnoreMaskProp.boolValue)
                    {
                        EditorGUILayout.PropertyField(hitIgnoreMaskProp);
                    }

                    EditorGUILayout.PropertyField(maxLifetimeProp);
                    EditorGUILayout.PropertyField(destroyImmediateAfterHit);

                    if (!isExplosiveProp.boolValue)
                    {
                        EditorGUILayout.PropertyField(enableDirectHitProp);
                    }
                }
            });

            DrawFoldout(ref explosionSettingsFoldout, "Explosion Settings", () =>
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(isExplosiveProp);
                    if (isExplosiveProp.boolValue)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField("Explosion Properties", EditorStyles.boldLabel);
                            EditorGUILayout.PropertyField(explosiveRadiusProp);
                            EditorGUILayout.PropertyField(minDamageProp);
                            EditorGUILayout.PropertyField(explosionForceProp);
                        }

                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField("Effects", EditorStyles.boldLabel);
                            EditorGUILayout.PropertyField(explotionPrefabProp);
                            EditorGUILayout.PropertyField(explotionAudioProp);
                            EditorGUILayout.PropertyField(explotionLifeTimeProp);
                            EditorGUILayout.PropertyField(explosionSoundRangeProp);
                        }
                    }
                }
            });

            if (!isExplosiveProp.boolValue)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(hitEffectsProp, true);
                    EditorGUILayout.PropertyField(bloodEffectProp);
                    EditorGUI.indentLevel--;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
