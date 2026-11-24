#if UNITY_EDITOR
using FS_Core;
using UnityEditor;
using UnityEngine;
namespace FS_ShooterSystem
{

    //[CustomEditor(typeof(ShooterFighter))]
    public class ShooterFighterEditor : Editor
    {
        //ShooterFighter shooterFighter;

        //private void OnEnable()
        //{
        //    shooterFighter = (ShooterFighter)target; 
        //}

        //public override void OnInspectorGUI()
        //{
        //    serializedObject.Update();

        //    // Draw original inspector
        //    base.OnInspectorGUI();

        //    EditorGUILayout.Space(10);

        //    // IK Setup Section
        //    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        //    EditorGUILayout.LabelField("IK", EditorStyles.boldLabel);
        //    if (shooterFighter.CurrentWeapon != null)
        //    {
        //        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        //        GUI.backgroundColor = shooterFighter.IsAiming && shooterFighter.ikSetupMode ? new Color(0.5f, 1f, 0.5f) : Color.white;
        //        if (GUILayout.Button("Aim", GUILayout.Height(30)))
        //        {
        //            if (!shooterFighter.IsAiming)
        //            {
        //                //shooterFighter.stuckOnIdleIkDebug = false;
        //                shooterFighter.ikSetupMode = true;
        //                shooterFighter.StartAiming();
        //                // Get ICharacter interface and prevent other systems
        //                var character = shooterFighter.GetComponent<ICharacter>();
        //                if (character != null)
        //                    character.PreventAllSystems = true;
        //            }
        //            else
        //            {
        //                shooterFighter.ikSetupMode = false;
        //                shooterFighter.StopAiming();
        //                // Re-enable other systems
        //                var character = shooterFighter.GetComponent<ICharacter>();
        //                if (character != null)
        //                    character.PreventAllSystems = false;
        //            }
        //        }
        //        GUI.backgroundColor = shooterFighter.stuckOnIdleIkDebug ? new Color(0.5f, 1f, 0.5f) : Color.white;
        //        if (GUILayout.Button("Idle", GUILayout.Height(30)))
        //        {
        //            shooterFighter.stuckOnIdleIkDebug = !shooterFighter.stuckOnIdleIkDebug;
        //            if (shooterFighter.stuckOnIdleIkDebug)
        //            {
        //                if (shooterFighter.IsAiming)
        //                    shooterFighter.StopAiming();
        //                var character = shooterFighter.GetComponent<ICharacter>();
        //                if (character != null)
        //                    character.PreventAllSystems = true;
        //                shooterFighter.ikSetupMode = true;
        //            }
        //            else
        //            {
        //                shooterFighter.ikSetupMode = false;
        //                var character = shooterFighter.GetComponent<ICharacter>();
        //                if (character != null)
        //                    character.PreventAllSystems = false;
        //            }
        //        }


        //        EditorGUILayout.EndHorizontal();
        //        GUI.backgroundColor = Color.white;
        //    }
        //    else
        //    {
        //        if (Application.isPlaying)
        //            EditorGUILayout.HelpBox("No weapon equipped. Please equip a weapon first.", MessageType.Warning);
        //        else
        //            EditorGUILayout.HelpBox("Enter Play Mode and equip a weapon to set up IK datas.", MessageType.Info);
        //    }

        //    EditorGUILayout.EndVertical();

        //    serializedObject.ApplyModifiedProperties();
        //}
    }
}
#endif