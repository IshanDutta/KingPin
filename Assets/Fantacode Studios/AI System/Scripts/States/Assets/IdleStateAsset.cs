using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    [AIStateModule("Core", "Idle")]
    public class IdleStateAsset : StateAsset
    {
        [SerializeField] new string name = "Idle";
        [SerializeField] bool returnToInitialPosition = true;
        [SerializeField] bool returnToInitialFacing = true;

        public override State<AIController> Build(AIController owner)
        {
            var idleState = new IdleState();
            idleState.ReturnToInitialPos = returnToInitialPosition;
            idleState.ReturnToInitialFacing = returnToInitialFacing;
            return idleState;
        }

        public override string NodeTitle => name;
    }
}
