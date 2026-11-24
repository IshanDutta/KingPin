using FS_Core;

namespace FS_CombatCore
{
    [AIStateModule("Combat Core", "Knocked Down")]
    public class KnockedDownStateAsset : StateAsset
    {
        public override State<AIController> Build(AIController owner)
        {
            return new KnockedDownState();
        }

        public override string NodeTitle => "Knocked Down";
    }
}
