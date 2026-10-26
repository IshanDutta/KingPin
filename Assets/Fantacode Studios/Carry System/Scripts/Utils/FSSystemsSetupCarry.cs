#if UNITY_EDITOR
using FS_InteractionSystem;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    public partial class FSSystemsSetup
    {
        public static FSSystemInfo CarrySystemSetup = new FSSystemInfo
        (
            characterType: CharacterType.Player,
            selected: false,
            systemName: "Carry System",
            displayName: "Carry & Occupant",
            prefabName: "Carry Controller",
            welcomeEditorShowKey: "CarrySystem_WelcomeWindow_Opened",

            extraSetupActionPlayer: (GameObject playerObject, GameObject systemPrefabObj, GameObject createdPlayerParentObject) =>
            {
                SetCarrySockets(playerObject);
            }
        );

        static string CarrySystemWelcomeEditorKey => CarrySystemSetup.welcomeEditorShowKey;


        [InitializeOnLoadMethod]
        public static void LoadCarrySystem()
        {
            NotifyNewInstall(CarrySystemWelcomeEditorKey);
        }

        /// <summary>
        /// Creates the attach points a carried object is parented to and assigns them on the
        /// CarryHandler. They live on the character model's bones, so they can only be built once
        /// the model exists — the Carry Controller prefab ships with the socket list empty.
        /// </summary>
        static void SetCarrySockets(GameObject playerObject)
        {
            var carryHandler = playerObject.GetComponent<CarryHandler>();
            if (carryHandler == null) return;

            var modelAnimator = GetCarryModelAnimator(playerObject);
            if (modelAnimator == null || !modelAnimator.isHuman) return;

            var hand = modelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            var rightArm = modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (hand == null || rightArm == null) return;

            var sockets = new SerializedObject(carryHandler).FindProperty("sockets");
            sockets.arraySize = 2;
            AssignCarrySocket(sockets.GetArrayElementAtIndex(0), CarrySocketType.RightHand,
                              CreateCarrySocket("Carry Socket - RIght Hand", hand));
            AssignCarrySocket(sockets.GetArrayElementAtIndex(1), CarrySocketType.RightArm,
                              CreateCarrySocket("Carry Socket - Right Arm", rightArm));
            sockets.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        // The player object carries its own Animator (controller + avatar); the humanoid bones we
        // need belong to the model instantiated underneath it.
        static Animator GetCarryModelAnimator(GameObject playerObject)
        {
            foreach (var animator in playerObject.GetComponentsInChildren<Animator>(true))
                if (animator.gameObject != playerObject) return animator;
            return null;
        }

        static Transform CreateCarrySocket(string name, Transform bone)
        {
            var existing = bone.Find(name);
            if (existing != null) return existing;

            var socket = new GameObject(name).transform;
            socket.SetParent(bone, false);
            return socket;
        }

        static void AssignCarrySocket(SerializedProperty element, CarrySocketType type, Transform socket)
        {
            element.FindPropertyRelative("type").enumValueIndex = (int)type;
            element.FindPropertyRelative("transform").objectReferenceValue = socket;
        }
    }
}
#endif
