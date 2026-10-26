#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "BT/Actions/StateAction")]
    public class BTStateActionAsset : BTNodeAsset
    {
        // [SerializeField] AIStates stateid;

        public override IBTNode Build(BTContext context)
        {
            //var aIController = context.Owner as AIController;
            //if (aIController == null)
            //{
            //    Debug.LogError("BTStateActionAsset is designed to work with AIController.");
            //    return null;
            //}

            //var state = aIController.GetStateById(stateid);

            //var iBTNode = new BTDeferredStateAction(state, aIController);
            //Node = iBTNode;
            //return iBTNode;

            return null;
        }

        #region Editor

        public override string NodeTitle => "State";
        public override Color NodeColor => new Color(0.4f, 0.4f, 0.8f);
        public override bool CanCreateChildNodes => false;
#if UNITY_EDITOR
        SerializedProperty _stateid;
        SerializedObject serializedObject;
        float helpBoxHeight;



        public override void DrawInspector()
        {
            base.DrawInspector();

            if (serializedObject == null)
                serializedObject = new SerializedObject(this);

            serializedObject.Update();

            if (_stateid == null)
                _stateid = serializedObject.FindProperty("stateid");

            EditorGUILayout.PropertyField(_stateid, new GUIContent("State"));

            serializedObject.ApplyModifiedProperties();
        }
#endif
        #endregion
    }
}


