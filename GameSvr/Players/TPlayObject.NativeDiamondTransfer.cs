using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeDiamondTransferOfflineMessage =
            "对方不在线或不在有效范围内";
        internal const string NativeDiamondTransferInvalidAmountMessage =
            "请输入 0-500000之间的数字";
        internal const string NativeDiamondTransferSuccessPrefix =
            "当前金刚石数目为: ";

        /// <summary>
        /// 玩家间金刚石数量转账 — native 0x006C686C.
        /// Validates amount &gt;= 0 (0x6C6898 jl); resolves target (0x652784);
        /// online: [target+0xBF0]=amount, AddGameDataLog dx=0x20 (0x768BE0),
        /// SysMsg 0xFFDB to self, sub_6B99E4 refresh on target.
        /// </summary>
        internal bool TryNativeDiamondAmountTransfer(NormNpc npc,
            string targetName, int amount)
        {
            if (amount < 0)
            {
                SysMsg(NativeDiamondTransferInvalidAmountMessage, MsgColor.Red,
                    MsgType.Hint);
                return false;
            }

            if (amount > 500_000)
            {
                SysMsg(NativeDiamondTransferInvalidAmountMessage, MsgColor.Red,
                    MsgType.Hint);
                return false;
            }

            var target = M2Share.UserEngine?.GetPlayObject(targetName);
            if (target == null || target.m_boGhost)
            {
                SysMsg(NativeDiamondTransferOfflineMessage, MsgColor.Red,
                    MsgType.Hint);
                return false;
            }

            target.m_nNativeDiamondTransferPending = amount;

            M2Share.AddGameDataLog(string.Join('\t', 0x20, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, targetName, amount, 1,
                "金刚石转账"));

            SysMsg(NativeDiamondTransferSuccessPrefix + amount, MsgColor.Red,
                MsgType.Hint);
            target.RefreshNativeLingFu();

            if (npc != null) m_NPC = npc;
            return true;
        }

        internal int m_nNativeDiamondTransferPending;
    }
}
