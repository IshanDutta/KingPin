using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    [AIStateModule("Stealth", "Resolve Stimulus")]
    public class ResolveStimulusStateAsset : StateAsset
    {
        public override State<AIController> Build(AIController owner)
        {
            return new ResolveStimulusState();
        }
    }

}
