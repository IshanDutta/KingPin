using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace FS_ShooterSystem
{

    public class BlendTreeEditor : EditorWindow
    {
        private AnimatorController animatorController;

        // Animation Clip Fields
        private AnimationClip aimClip;
        private AnimationClip idleClip;
        private AnimationClip reloadClip;
        private AnimationClip equipClip;
        private AnimationClip unEquipClip;
        private AnimationClip shootClip;

        // Single Threshold Value for all animations
        private float threshold = 0f;

        //[MenuItem("Tools/FS System/Blend Tree Editor")]
        private static void ShowWindow()
        {
            var window = GetWindow<BlendTreeEditor>();
            window.titleContent = new GUIContent("Blend Tree Editor");
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Blend Tree Editor", EditorStyles.boldLabel);

            // Select Animator Controller
            animatorController = (AnimatorController)EditorGUILayout.ObjectField("Animator Controller", animatorController, typeof(AnimatorController), false);

            if (animatorController == null)
                return;

            // Display animation clip fields for each animation (Aim, Idle, Reload, Equip, UnEquip, Shoot)
            aimClip = (AnimationClip)EditorGUILayout.ObjectField("Aim Clip", aimClip, typeof(AnimationClip), false);
            idleClip = (AnimationClip)EditorGUILayout.ObjectField("Idle Clip", idleClip, typeof(AnimationClip), false);
            reloadClip = (AnimationClip)EditorGUILayout.ObjectField("Reload Clip", reloadClip, typeof(AnimationClip), false);
            equipClip = (AnimationClip)EditorGUILayout.ObjectField("Equip Clip", equipClip, typeof(AnimationClip), false);
            unEquipClip = (AnimationClip)EditorGUILayout.ObjectField("Unequip Clip", unEquipClip, typeof(AnimationClip), false);
            shootClip = (AnimationClip)EditorGUILayout.ObjectField("Shoot Clip", shootClip, typeof(AnimationClip), false);

            // Single Threshold field for all animations
            threshold = EditorGUILayout.FloatField("Threshold", threshold);

            if (GUILayout.Button("Assign Animations to Blend Trees"))
            {
                if (animatorController != null)
                {
                    // Assign animations to the corresponding blend trees with the specified threshold
                    AssignAnimationsToBlendTree("Upper Body", aimClip, idleClip, reloadClip, equipClip, unEquipClip);
                    AssignAnimationsToBlendTree("Shooting", shootClip);
                }
            }
        }

        private void AssignAnimationsToBlendTree(string layerName, params AnimationClip[] clips)
        {
            // Find the layer by name
            var layer = FindLayerByName(animatorController, layerName);
            if (layer == null)
            {
                Debug.LogWarning("Layer not found: " + layerName);
                return;
            }

            // Access the state machine in the selected layer
            var stateMachine = layer.stateMachine;

            // Find the blend trees in the state machine
            var blendTrees = GetBlendTreesFromStateMachine(stateMachine);

            // Assign animation clips to corresponding blend trees
            foreach (var blendTree in blendTrees)
            {
                switch (blendTree.name)
                {
                    case "Aim":
                        AddOrOverrideAnimationInBlendTree(blendTree, aimClip, threshold);
                        break;
                    case "Idle":
                        AddOrOverrideAnimationInBlendTree(blendTree, idleClip, threshold);
                        break;
                    case "Reload":
                        AddOrOverrideAnimationInBlendTree(blendTree, reloadClip, threshold);
                        break;
                    case "Equip":
                        AddOrOverrideAnimationInBlendTree(blendTree, equipClip, threshold);
                        break;
                    case "UnEquip":
                        AddOrOverrideAnimationInBlendTree(blendTree, unEquipClip, threshold);
                        break;
                    case "Shoot":
                        AddOrOverrideAnimationInBlendTree(blendTree, shootClip, threshold);
                        break;
                    default:
                        Debug.LogWarning("Blend Tree not handled: " + blendTree.name);
                        break;
                }
            }
        }

        private void AddOrOverrideAnimationInBlendTree(BlendTree blendTree, AnimationClip animationClip, float blendValue)
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
                AddAnimationToBlendTree(blendTree, animationClip, blendValue);
            }
        }

        private System.Collections.Generic.List<BlendTree> GetBlendTreesFromStateMachine(AnimatorStateMachine stateMachine)
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

        private void AddAnimationToBlendTree(BlendTree blendTree, AnimationClip animationClip, float blendValue)
        {
            Undo.RecordObject(blendTree, "Add Animation to Blend Tree");
            blendTree.AddChild(animationClip, blendValue);
            EditorUtility.SetDirty(blendTree);
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
    }
}