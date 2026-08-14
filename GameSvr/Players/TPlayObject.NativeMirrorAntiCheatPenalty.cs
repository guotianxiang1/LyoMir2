using System;
using System.Globalization;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // 战神 ident 202 stub 0x657208 -> wrapper sub_658384 @0x658384
        // (len>0 提 body 串) -> core sub_653ED0 @0x653ED0。
        // 第三 dword ecx=[ebp+0x10] = 惩罚天数；body = 账号名（[+0xB33] CompareText）。
        // 到期：Trunc(Now-[+0x780])+7-time -> [+0x180c]（0x653F74/0x653F7F）。

        internal const int NativeCheatPenaltyExpiryOffset = 0x180C;
        internal const int NativeCheatPenaltyHardTier = 3;
        internal const int NativeCheatPenaltyExpiryGraceDays = 7;

        /// <summary>obj+0x180c — 外挂惩罚到期天数（native Trunc 日计数）。</summary>
        public int m_nNativeCheatPenaltyExpiryDay;

        internal static void NativeMirrorAntiCheatPenalty(string accountName,
            int penaltyDays)
        {
            if (string.IsNullOrEmpty(accountName))
                return;

            var engine = M2Share.UserEngine;
            if (engine == null)
                return;

            foreach (var player in engine.PlayObjects)
            {
                if (player == null || player.m_boGhost)
                    continue;
                if (!string.Equals(player.m_sUserID, accountName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                player.ApplyNativeAntiCheatPenalty(penaltyDays);
            }
        }

        private void ApplyNativeAntiCheatPenalty(int penaltyDays)
        {
            if (penaltyDays > 0)
            {
                m_btNativeCheatPenaltyTier = NativeCheatPenaltyHardTier;
                var daysOnline = GetNativeTruncDaysOnline();
                m_nNativeCheatPenaltyExpiryDay = daysOnline
                    + NativeCheatPenaltyExpiryGraceDays - penaltyDays;
                M2Share.MainOutMessage("[反作弊] 设置外挂惩罚 账号="
                    + (m_sUserID ?? string.Empty) + " 天数="
                    + penaltyDays.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                m_btNativeCheatPenaltyTier = 0;
                m_nNativeCheatPenaltyExpiryDay = 0;
                M2Share.MainOutMessage("[反作弊] 清除外挂惩罚 账号="
                    + (m_sUserID ?? string.Empty));
            }
        }

        /// <summary>
        /// sub_6D43C4 @0x6D43C4: Trunc(Now - [+0x780])。
        /// </summary>
        internal int GetNativeTruncDaysOnline()
        {
            var localNow = DateTime.Now.ToOADate();
            var dbNow = NativeDbClockNow(localNow);
            return (int)Math.Truncate(dbNow);
        }
    }
}
