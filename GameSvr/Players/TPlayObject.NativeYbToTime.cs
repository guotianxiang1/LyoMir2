using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeYbToTimeBalancePrefix = "当前卡内剩余: ";
        internal const string NativeYbToTimeAutoConvertDenied =
            "元宝无法自动转换为时间";
        internal const string NativeYbToTimeSuccessPrefix =
            "元宝转时间成功，当前元宝数 ";
        internal const string NativeYbToTimeFailureDialog =
            "\\ \\<返回/@main>";

        /// <summary>
        /// 元宝转游戏时间 NPC — native 0x0063846C.
        /// action==1 (0x6384B6): show card balance via 0x6D340C + SysMsg prefix.
        /// action!=1 entry: SysMsg "元宝无法自动转换为 time" when not action 1.
        /// Result callbacks (m_NPC required, 0x638510): nativeResult -4/-3/-2 map to
        /// sub_63DFAC dialog arms at 0x638584/0x638574/0x638564; else failure 0x638594.
        /// </summary>
        internal void ApplyNativeYbToTimeNpc(NormNpc npc, int action,
            int nativeResultCode, int currentYuanbao)
        {
            if (npc == null) return;
            m_NPC = npc;

            if (action == 1)
            {
                SendNativeCapitalInfo();
                var balance = m_nNativeYbRemainingSeconds;
                SysMsg(NativeYbToTimeBalancePrefix + balance, MsgColor.Red,
                    MsgType.Hint);
                return;
            }

            if (nativeResultCode == 0)
            {
                SysMsg(NativeYbToTimeAutoConvertDenied, MsgColor.Red,
                    MsgType.Hint);
                return;
            }

            var dialog = nativeResultCode switch
            {
                -4 => NativeYbToTimeSuccessPrefix + currentYuanbao +
                      NativeYbToTimeFailureDialog,
                -3 => NativeYbToTimeAutoConvertDenied + NativeYbToTimeFailureDialog,
                -2 => NativeYbToTimeFailureDialog,
                _ => NativeYbToTimeFailureDialog
            };
            SendNativeYbNpcDialog(npc, dialog);
        }
    }
}
