using FS_Core;
using UnityEngine;

namespace FS_Shooter
{

    [AIStateModule("Shooter", "Shoot")]
    public class ShootStateAsset : StateAsset
    {
        [SerializeField] string _name = "Shoot";

        public override State<AIController> Build(AIController owner)
        {
            return new ShootState();
        }

        public override string NodeTitle => _name;
    }
}
