using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "BT/Actions/Print")]
    public class BTPrintActionAsset : BTNodeAsset
    {
        [SerializeField] string message;

        public override IBTNode Build(BTContext context)
        {
            return new BTPrintAction(message);
        }

        public override string NodeTitle => "Print";
    }
}
