using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SuperMerchant", "超级商人(万能购买)", "", 5)]
    public class SuperMerchantCommand : BaseCommond
    {
        [DefaultCommand]
        public void SuperMerchant(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "SuperMerchant",
                "原版超级药商库存字段尚未移植，未修改库存。");
        }
    }
}
