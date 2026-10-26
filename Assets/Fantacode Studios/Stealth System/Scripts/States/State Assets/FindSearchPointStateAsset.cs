using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{

    [AIStateModule("Core", "Find Search Point")]
    public class FindSearchPointStateAsset : StateAsset
    {
        [Tooltip("Display name of this node in the state machine editor.")]
        [SerializeField] new string name = "FindSearchPoint";
        public override string NodeTitle => name;

        [Tooltip("Radius in meters around the AI within which a hidden search point is picked.")]
        [SerializeField] float searchRadius = 10f;

        public override State<AIController> Build(AIController owner)
        {
            var findSearchPointState = new FindSearchPointState();
            findSearchPointState.SearchRadius = searchRadius;

            return findSearchPointState;
        }
    }
}
