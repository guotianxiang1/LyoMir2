using System;
using System.Buffers.Binary;
using DBSvr.Core;
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
        internal const int NativeCheatPenaltyRecordOffset = 0x1E8;
        internal const int NativeCheatPenaltyHardTier = 3;
        internal const int NativeCheatPenaltyExpiryGraceDays = 7;
        internal const int NativeCheatPenaltyMinimumLevel = 35;

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

            var matches = new List<TPlayObject>();
            foreach (var player in engine.PlayObjects)
            {
                if (player == null || player.m_boGhost)
                    continue;
                if (!string.Equals(player.m_sUserID, accountName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                matches.Add(player);
            }
            if (matches.Count == 0)
                return;

            Envirnoment target = null;
            var logQuantity = 2;
            if (penaltyDays > 0)
                target = FindNativeCheatPenaltyMap(out logQuantity);

            foreach (var player in matches)
                player.ApplyNativeAntiCheatPenalty(accountName, penaltyDays,
                    target, logQuantity);
        }

        private static Envirnoment FindNativeCheatPenaltyMap(out int logQuantity)
        {
            logQuantity = 1;
            var manager = M2Share.MapManager;
            if (manager == null)
                return null;

            foreach (var environment in manager.Maps)
            {
                if (environment?.Flag != null && environment.Flag.boBLACKROOM)
                {
                    logQuantity = 2;
                    return environment;
                }
            }
            return manager.FindMapByNativeName("SD000");
        }

        private void ApplyNativeAntiCheatPenalty(string accountName,
            int penaltyDays, Envirnoment target, int logQuantity)
        {
            if (penaltyDays > 0)
            {
                m_btNativeCheatPenaltyTier = NativeCheatPenaltyHardTier;
                var daysOnline = GetNativeTruncDaysOnline();
                m_nNativeCheatPenaltyExpiryDay = unchecked(daysOnline
                    + NativeCheatPenaltyExpiryGraceDays - penaltyDays);
                if (target != null)
                {
                    var x = unchecked((short)M2Share.RandomNumber.Random(
                        target.wWidth));
                    var y = unchecked((short)M2Share.RandomNumber.Random(
                        target.wHeight));
                    TrySpaceMoveToEnvironment(target, x, y, showMode: 1,
                        coordinatesAlreadyResolved: false);
                }
                M2Share.AddNativeGameDataLog(this, 0x1D,
                    "设置外挂惩罚", penaltyDays, logQuantity, accountName);
            }
            else
            {
                m_btNativeCheatPenaltyTier = 0;
                m_nNativeCheatPenaltyExpiryDay = 0;
                M2Share.AddNativeGameDataLog(this, 0x1D,
                    "清除外挂惩罚", 1, 2, accountName);
            }
        }

        /// <summary>
        /// sub_6D43C4 @0x6D43C4: Trunc(Now - [+0x780])。
        /// </summary>
        internal int GetNativeTruncDaysOnline()
        {
            var localNow = DateTime.Now.ToOADate();
            var dbNow = NativeDbClockNow(localNow);
            return unchecked((int)NativeTruncateDay64(dbNow));
        }

        /// <summary>
        /// sub_403580: x87 truncation to a signed 64-bit integer. An invalid
        /// conversion produces the x87 integer-indefinite value.
        /// </summary>
        internal static long NativeTruncateDay64(double value)
        {
            const double LongUpperExclusive = 9223372036854775808.0d;
            if (double.IsNaN(value) || value < long.MinValue
                || value >= LongUpperExclusive)
                return long.MinValue;

            return (long)Math.Truncate(value);
        }

        /// <summary>
        /// LOAD 0x6B0674..0x6B0795: rec+0x1E8 -> obj+0x180C, then derive
        /// tier 3 from the stored day, the loaded WORD level and DB clock.
        /// </summary>
        internal bool RestoreNativeAntiCheatPenalty(double dbClockNow)
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length != NativeHumanDataCodec.DataRecordSize)
            {
                m_nNativeCheatPenaltyExpiryDay = 0;
                return false;
            }

            m_nNativeCheatPenaltyExpiryDay =
                BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(
                    NativeCheatPenaltyRecordOffset, sizeof(int)));

            var elapsedDays = unchecked(NativeTruncateDay64(dbClockNow)
                - (long)m_nNativeCheatPenaltyExpiryDay);
            if (m_nNativeCheatPenaltyExpiryDay > 0
                && m_Abil.Level >= NativeCheatPenaltyMinimumLevel
                && elapsedDays < NativeCheatPenaltyExpiryGraceDays)
            {
                m_btNativeCheatPenaltyTier = NativeCheatPenaltyHardTier;
                return true;
            }

            // Native clears only obj+0x180C on this path. The freshly created
            // player already carries tier 0; do not invent another tier write.
            m_nNativeCheatPenaltyExpiryDay = 0;
            return true;
        }

        /// <summary>
        /// SAVE 0x6B142F..0x6B1435: obj+0x180C -> rec+0x1E8 verbatim.
        /// </summary>
        internal bool PersistNativeAntiCheatPenalty()
        {
            var raw = m_NativeHumanData;
            if (raw == null)
            {
                raw = new byte[NativeHumanDataCodec.DataRecordSize];
                m_NativeHumanData = raw;
            }

            // The shared encoder only preserves a record with the exact native
            // size. Writing an active value into any other buffer would report
            // success and then silently lose it when the encoder replaces it.
            if (raw.Length != NativeHumanDataCodec.DataRecordSize)
                return m_nNativeCheatPenaltyExpiryDay == 0;

            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(
                NativeCheatPenaltyRecordOffset, sizeof(int)),
                m_nNativeCheatPenaltyExpiryDay);
            return true;
        }
    }
}
