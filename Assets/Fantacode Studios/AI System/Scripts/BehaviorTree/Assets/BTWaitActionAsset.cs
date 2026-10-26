using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "BT/Actions/Wait")]
    public class BTWaitActionAsset : BTNodeAsset
    {
        [SerializeField] float waitTime;

        public override IBTNode Build(BTContext context)
        {
            return new BTWaitAction(waitTime);
        }
        public override string NodeTitle => "Wait";
    }
}


