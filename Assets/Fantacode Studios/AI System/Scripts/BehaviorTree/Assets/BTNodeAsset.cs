using FS_Core;
using System.Collections.Generic;
using UnityEngine;
namespace FS_Core
{
    public abstract class BTNodeAsset : ScriptableObject
    {
        public IBTNode Node { get; protected set; }
        public abstract IBTNode Build(BTContext context);


        #region Editor
        [SerializeField] private BTNodeAsset parent;
        public BTNodeAsset Parent
        {
            get { return parent; }
            set { parent = value; }
        }

        [SerializeField] private Vector2 nodePosition;
        public Vector2 NodePosition
        {
            get { return nodePosition; }
            set { nodePosition = value; }
        }
        public virtual Vector2 NodeSize { get; } = new Vector2(100, 50);
        public void SetNodePosition(Vector2 newPos)
        {
            nodePosition = newPos;
        }
        public Rect NodeRect => new Rect(NodePosition, NodeSize);
        public virtual void SetChildren(BTNodeAsset children) { }
        public virtual void DrawInspector() { }
        public virtual bool CanCreateChildNodes { get; } = true;
        public abstract string NodeTitle { get; }
        public virtual List<BTNodeAsset> Children { get; } = new List<BTNodeAsset>();
        public virtual void RemoveChild(BTNodeAsset children, bool clearList = false) { }
        public virtual Color NodeColor { get; } = new Color(0.0f, 0.5f, 1f);
        public virtual bool HasMultipleChildren { get; set; } = false;
        #endregion
    }
}