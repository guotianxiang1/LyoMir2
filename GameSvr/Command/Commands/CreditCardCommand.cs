using GameSvr.CommandSystem;
using SystemModule;
using System.Globalization;

namespace GameSvr
{
    /// <summary>
    /// 原版灵符使用及清理控制命令。
    /// Usage: @CreditCard open|close|ClearMonLingfu|ClearAll
    /// </summary>
    [GameCommand("CreditCard", "管理灵符使用及清理", "open|close|ClearMonLingfu|ClearAll", 4)]
    public class CreditCardCommand : BaseCommond
    {
        [DefaultCommand]
        public void CreditCard(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length != 1)
            {
                return;
            }

            var service = M2Share.CreditCardService ?? NativeCreditCardService.Disabled;
            var argument = @Params[0];
            if (argument.Equals("open", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                var enabled = argument.Equals("open", StringComparison.OrdinalIgnoreCase);
                if (!service.TrySetEnabled(enabled, out var switchWord))
                    return;

                M2Share.UserEngine.SendServerGroupMsg(Grobal2.ISM_CS_SERVERSWITCH, 0,
                    switchWord.ToString(CultureInfo.InvariantCulture));
                PlayObject.SysMsg(enabled ? " 扩展灵符  : 开" : " 扩展灵符  : 关",
                    MsgColor.Red, MsgType.Hint);
                service.TryPersistSwitches();
                return;
            }

            if (argument.Equals("ClearMonLingfu", StringComparison.OrdinalIgnoreCase))
            {
                if (service.MonthlyLimitedEnabled)
                {
                    PlayObject.SysMsg("需要先关闭每月限时灵符的应用",
                        MsgColor.Red, MsgType.Hint);
                    return;
                }
                if (!service.TryClearMonthly())
                    return;

                PlayObject.SysMsg("清除每月限时灵符数据成功",
                    MsgColor.Red, MsgType.Hint);
                M2Share.UserEngine.SendServerGroupMsg(
                    Grobal2.ISM_CREDITCARD_CLEARMONTHLY, 0, string.Empty);
                service.ResetOnlineMonthly();
                return;
            }

            if (!argument.Equals("ClearAll", StringComparison.OrdinalIgnoreCase))
                return;
            if (service.Enabled)
            {
                PlayObject.SysMsg("需要先关闭扩展灵符的应用",
                    MsgColor.Red, MsgType.Hint);
                return;
            }
            if (!service.TryArchiveAll())
                return;

            PlayObject.SysMsg("清除CreditCard表数据成功",
                MsgColor.Red, MsgType.Hint);
            M2Share.UserEngine.SendServerGroupMsg(
                Grobal2.ISM_CREDITCARD_CLEARALL, 0, string.Empty);
            service.ResetOnlineAll();
        }
    }
}
