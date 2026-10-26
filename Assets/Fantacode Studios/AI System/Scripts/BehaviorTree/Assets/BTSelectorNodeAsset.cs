using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "BT/Selector")]
    public class BTSelectorNodeAsset : BTNodeAsset
    {
        [SerializeField] private List<BTNodeAsset> children = new List<BTNodeAsset>();
        public override IBTNode Build(BTContext context)
        {
            var iBTNode = new BTSelectorNode(children.Select(c => c.Build(context)).ToArray());
            Node = iBTNode;
            return iBTNode;
        }

        public override List<BTNodeAsset> Children => children;

        #region Editor
        public override bool HasMultipleChildren
        {
            get => true;
            set { }
        }
        public override string NodeTitle => "Selector";
        public override void SetChildren(BTNodeAsset children)
        {
            base.SetChildren(children);
            this.children.Add(children);
        }
        public override void RemoveChild(BTNodeAsset children, bool clear = false)
        {
            if (clear)
            {
                this.children.Clear();
            }
            else
            {
                this.children.Remove(children);
            }
        }
        public override Color NodeColor => new Color(0.2f, 0.6f, 0.3f);
        #endregion
    }
}
