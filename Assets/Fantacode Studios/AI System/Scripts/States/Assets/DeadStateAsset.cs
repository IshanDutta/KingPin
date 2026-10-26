namespace FS_Core
{
    [AIStateModule("Combat Core", "Dead")]
    public class DeadStateAsset : StateAsset
    {
        public override State<AIController> Build(AIController owner)
        {
            return new DeadState();
        }

        public override string NodeTitle => "Dead";
    }
}
