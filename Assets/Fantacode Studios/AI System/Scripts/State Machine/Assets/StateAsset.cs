using FS_Util;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public abstract class StateAsset : ScriptableObject
    {
        public abstract State<AIController> Build(AIController owner);

        public static Dictionary<State<AIController>, StateAsset> stateRuntimeRef = new Dictionary<State<AIController>, StateAsset>();


        [HideInInspector][SerializeField] private Vector2 nodePosition;
        public Vector2 NodePosition
        {
            get { return nodePosition; }
            set { nodePosition = value; }
        }
        public virtual Vector2 NodeSize => new Vector2(150, 35);
        public void SetNodePosition(Vector2 newPos)
        {
            nodePosition = newPos;
        }
        public Rect NodeRect => new Rect(NodePosition, NodeSize);
        public virtual void DrawInspector()
        {
#if UNITY_EDITOR
            InspectorDrawer.DrawSerializedFields(this);
#endif
        }
        public virtual string NodeTitle => "State";
        public virtual Color NodeColor => new Color(0.2863f, 0.2980f, 0.3098f, 1f);
        public virtual bool CanDelete => true;
        public virtual bool CanCreateMultiple => true;
        public virtual bool CanSetAsDefault => true;
        public virtual bool CanSetTranstionEnd => true;
        public virtual bool CanSetTranstionStart => true;
    }
}
