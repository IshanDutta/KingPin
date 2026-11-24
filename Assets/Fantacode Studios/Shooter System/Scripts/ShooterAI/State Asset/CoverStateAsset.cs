using FS_Core;
using FS_ShooterSystem;
using UnityEngine;

namespace FS_Shooter
{
    [AIStateModule("Shooter", "Cover")]
    public class CoverStateAsset : StateAsset
    {
        [SerializeField] string _name = "Cover";

        public override State<AIController> Build(AIController owner)
        {
            return new CoverState();
        }

        public override string NodeTitle => _name;
    }
}
