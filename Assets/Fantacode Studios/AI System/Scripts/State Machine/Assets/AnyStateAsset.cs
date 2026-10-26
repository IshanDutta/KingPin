using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class AnyStateAsset : StateAsset
    {
        public override State<AIController> Build(AIController owner)
        {
            return null;
        }
        public override string NodeTitle => "Any State";
        public override Color NodeColor => new Color(0.3373f, 0.5843f, 0.5216f, 1f);
        public override bool CanDelete => false;
        public override bool CanCreateMultiple => false;
        public override bool CanSetAsDefault => false;
        public override bool CanSetTranstionEnd => false;
    }
}
