using FS_Core;
using UnityEngine;

namespace FS_CombatCore
{
    [AIStateModule("Combat Core", "Getting Hit")]
    public class GettingHitStateAsset : StateAsset
    {
        [SerializeField] string _name = "Getting Hit";

        public override State<AIController> Build(AIController owner)
        {
            return new GettingHitState();
        }

        public override string NodeTitle => _name;
    }
}
