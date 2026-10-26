using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "BT/New BehaviorTree")]
    public class BehaviorTree : BTNodeAsset
    {
        [SerializeField] BTNodeAsset rootNode;

        public override IBTNode Build(BTContext context)
        {
            return rootNode.Build(context);
        }

        public override string NodeTitle => "Root";
        public override List<BTNodeAsset> Children
        {
            get
            {
                if (rootNode == null)
                    return null;

                return new List<BTNodeAsset> { rootNode };
            }
        }


        public override void SetChildren(BTNodeAsset children)
        {
            base.SetChildren(children);
            rootNode = children;
        }

    }
}
