using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "BT/Actions/MoveToPos")]
    public class BTMoveToPosActionAsset : BTNodeAsset
    {
        [SerializeField] Vector3 targetPosition;
        
        public override IBTNode Build(BTContext context)
        {
            var aiController = context.Owner as AIController;
            if (aiController == null)
            {
                Debug.LogError("Owner is not an AIController");
                return null;
            }
            return new BTMoveToPosAction(targetPosition, aiController.GetComponent<NavMeshAgent>());
        }

        public override string NodeTitle => "Move";
    }
}
