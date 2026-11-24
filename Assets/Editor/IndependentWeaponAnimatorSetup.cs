#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Put this file in an Editor folder. Creates a NEW controller so the original
// Fantacode controller and its other users are not modified.
public class IndependentWeaponAnimatorSetup : EditorWindow
{
    const string LayerName = "Independent Weapon";
    Animator targetAnimator;
    AnimationClip aimClip;
    AnimationClip shootClip;
    AnimationClip reloadClip;

    [MenuItem("Tools/FS System/Independent Weapon Animator Setup")]
    static void Open()
    {
        var window = GetWindow<IndependentWeaponAnimatorSetup>("Weapon Animator Setup");
        if (Selection.activeGameObject != null)
            window.targetAnimator = Selection.activeGameObject.GetComponentInChildren<Animator>(true);
        window.minSize = new Vector2(390, 310);
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("Creates a copy of your Animator Controller with a masked upper-body weapon layer. Original controller stays unchanged. Use compatible Humanoid clips.", MessageType.Info);
        targetAnimator = (Animator)EditorGUILayout.ObjectField("Shooter Animator", targetAnimator, typeof(Animator), true);
        aimClip = (AnimationClip)EditorGUILayout.ObjectField("Aim / Rifle Hold", aimClip, typeof(AnimationClip), false);
        shootClip = (AnimationClip)EditorGUILayout.ObjectField("Shoot / Recoil", shootClip, typeof(AnimationClip), false);
        reloadClip = (AnimationClip)EditorGUILayout.ObjectField("Reload", reloadClip, typeof(AnimationClip), false);
        if (reloadClip != null)
            EditorGUILayout.LabelField("Reload Seconds (set on combat AI)", reloadClip.length.ToString("F2"));
        EditorGUILayout.HelpBox("Copies of the weapon clips have their Animation Events removed, so the independent shooting controller remains the only source of shots and reload timing. The aiming clip should be a static or looping pose.", MessageType.None);
        if (targetAnimator != null && !(targetAnimator.runtimeAnimatorController is AnimatorController))
            EditorGUILayout.HelpBox("Select an Animator with a normal Animator Controller (.controller). Animator Override Controllers are not supported by this setup tool.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(targetAnimator == null || aimClip == null || shootClip == null || reloadClip == null || !(targetAnimator.runtimeAnimatorController is AnimatorController)))
        {
            if (GUILayout.Button("Create and Assign Weapon Controller", GUILayout.Height(32))) CreateController();
        }
    }

    void CreateController()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Weapon Animator", "Exit Play Mode before creating the controller.", "OK");
            return;
        }
        if (targetAnimator.avatar == null || !targetAnimator.avatar.isValid || !targetAnimator.avatar.isHuman ||
            !aimClip.isHumanMotion || !shootClip.isHumanMotion || !reloadClip.isHumanMotion)
        {
            EditorUtility.DisplayDialog("Weapon Animator", "The Animator and all three weapon clips must use compatible Humanoid animation. Generic rigs require a custom transform mask.", "OK");
            return;
        }
        var original = targetAnimator.runtimeAnimatorController as AnimatorController;
        if (original == null) return;
        foreach (var layer in original.layers)
            if (layer.name == LayerName)
            {
                EditorUtility.DisplayDialog("Weapon Animator", "This controller already has an Independent Weapon layer. Use its existing states or select the original controller to generate a new copy.", "OK");
                return;
            }
        string source = AssetDatabase.GetAssetPath(original);
        string path = EditorUtility.SaveFilePanelInProject("Save a new weapon Animator Controller", original.name + " Weapon", "controller", "Choose a location for the new controller.");
        if (string.IsNullOrEmpty(path)) return;
        // Always use a fresh path, even if the user selected an existing file.
        path = AssetDatabase.GenerateUniqueAssetPath(path);
        if (string.IsNullOrEmpty(source) || !AssetDatabase.CopyAsset(source, path))
        {
            Debug.LogError("[Weapon Animator] Could not copy the source controller.", targetAnimator);
            return;
        }
        try
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) throw new System.InvalidOperationException("Copied controller could not be loaded.");
            var mask = new AvatarMask { name = "Independent Upper Body Mask" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            AssetDatabase.AddObjectToAsset(mask, controller);

            controller.AddLayer(LayerName);
            var layers = controller.layers;
            int index = layers.Length - 1;
            var layer = layers[index];
            layer.avatarMask = mask;
            layer.defaultWeight = 0f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layers[index] = layer;
            controller.layers = layers;
            var machine = layer.stateMachine;
            var aim = machine.AddState("Aim", new Vector3(250, 60, 0));
            var shoot = machine.AddState("Shoot", new Vector3(500, 60, 0));
            var reload = machine.AddState("Reload", new Vector3(500, 180, 0));
            aim.motion = CopyWithoutEvents(aimClip, controller, "Independent Aim Clip");
            shoot.motion = CopyWithoutEvents(shootClip, controller, "Independent Shoot Clip");
            reload.motion = CopyWithoutEvents(reloadClip, controller, "Independent Reload Clip");
            aim.writeDefaultValues = false;
            shoot.writeDefaultValues = false;
            reload.writeDefaultValues = false;
            machine.defaultState = aim;
            AddReturn(shoot, aim);
            AddReturn(reload, aim);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Undo.RecordObject(targetAnimator, "Assign Independent Weapon Controller");
            targetAnimator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(targetAnimator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(targetAnimator);
            EditorGUIUtility.PingObject(controller);
            Debug.Log("[Weapon Animator] Created " + path + ". Set Reload Seconds on CombatAIController to " + reloadClip.length.ToString("F2") + " if you want the timer to match the clip.", targetAnimator);
        }
        catch (System.Exception ex)
        {
            // Only delete the newly generated asset, never the original controller.
            AssetDatabase.DeleteAsset(path);
            Debug.LogError("[Weapon Animator] Setup failed: " + ex, targetAnimator);
        }
    }

    static AnimationClip CopyWithoutEvents(AnimationClip source, AnimatorController controller, string clipName)
    {
        var copy = Object.Instantiate(source);
        copy.name = clipName;
        AnimationUtility.SetAnimationEvents(copy, new AnimationEvent[0]);
        AssetDatabase.AddObjectToAsset(copy, controller);
        return copy;
    }

    static void AddReturn(AnimatorState from, AnimatorState to)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.hasFixedDuration = true;
        transition.duration = 0.06f;
    }
}
#endif
