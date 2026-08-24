using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SuperMerchant", "超级商人(万能购买)", "物品类型 库存类型 数量", 5)]
    public class SuperMerchantCommand : BaseCommond
    {
        private const ushort NativeColorWord = 0xFFDB;
        private const string UsageMessage =
            "命令格式：SuperMerchant 物品类型（1疗伤2雪霜） 库存类型(1最小库存2最大库存3当前库存) 数量";
        private const string SuccessMessage = "超级商人库存信息修改成功！";
        private const string FailureMessage = "超级商人库存信息修改失败！";

        [DefaultCommand]
        public void SuperMerchant(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            if (!TryParse(@Params, 0, out var goodsType) ||
                !TryParse(@Params, 1, out var storageType) ||
                !TryParse(@Params, 2, out var amount) ||
                goodsType <= 0 || storageType <= 0 || amount <= 0)
            {
                SendNativeSysMsg(PlayObject, UsageMessage);
                return;
            }

            // The native dispatcher checks the global manager pointer and returns
            // silently when the subsystem is unavailable.
            var manager = M2Share.SuperMerchantManager;
            if (manager == null)
                return;

            SendNativeSysMsg(PlayObject,
                manager.TrySetStock(goodsType, storageType, amount)
                    ? SuccessMessage
                    : FailureMessage);
        }

        private static bool TryParse(string[] parameters, int index, out int value)
        {
            value = -1;
            return parameters != null && index < parameters.Length &&
                   PasEngine.PasApiBridge.TryParseNativeDelphiInteger(
                       parameters[index] ?? string.Empty, out value);
        }

        private static void SendNativeSysMsg(TPlayObject player, string message)
        {
            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                NativeColorWord & 0xFF, NativeColorWord >> 8, 0, message);
        }
    }
}
