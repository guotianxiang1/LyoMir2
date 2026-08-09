using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // Save-record offsets, derived from the 战神 SAVE routine sub_6B0FF0. Inside SAVE,
        // esi is the PRE-BIASED record base (0x6B100C  lea esi,[eax+8], where
        // eax = [ebp-4] = the raw record pointer), so a [esi+N] store lands at record
        // offset N directly. (Contrast the marriage block at 0x6B1688, which reloads the
        // RAW pointer into eax first and therefore maps lea [eax+0x658] -> rec 0x650.)
        //
        //   0x6B1495  mov eax,[ebx+0x1844]      ; obj: completion flags
        //   0x6B149B  mov [esi+0x230], eax      ; -> rec 0x230
        //   0x6B14A2  lea edi,[esi+0x234]       ; dest rec 0x234
        //   0x6B14A8  lea esi,[ebx+0x1848]      ; src obj+0x1848
        //   0x6B14AE  movsd x5                  ; 20 bytes -> rec 0x234..0x247
        // LOAD mirrors it exactly at 0x6B079E/0x6B07A7 and 0x6B07B3/0x6B07B9.
        //
        // Object-side writers in sub_6465B8 map through rec = 0x234 + (objOff - 0x1848):
        //   0x6466B7  or  dword [eax+0x1844], 1 ; flags bit 0   -> rec 0x230
        //   0x6466D7  inc byte  [eax+0x184C]    ; job 0         -> rec 0x238
        //   0x6466E2  inc byte  [eax+0x184D]    ; job 1         -> rec 0x239
        //   0x6466ED  inc byte  [eax+0x184E]    ; job 2         -> rec 0x23A
        //   0x6466F8  inc dword [eax+0x1854]    ; job 3         -> rec 0x240
        //
        // These constants were previously 0x238/0x240/0x248 -- every one shifted +8 onto
        // the NEXT field. That made the completion check read the job-0/1/2 byte triple,
        // made SubmitNativeBallQuest OR the completion bit into the job-0 counter, and put
        // the job-3 write at rec 0x248, past the end of the 20-byte block (0x234..0x247).
        internal const int NativeSubmitBallQuestFlagsOffset = 0x230;
        internal const int NativeSubmitBallQuestJob012Offset = 0x238;
        internal const int NativeSubmitBallQuestJob3Offset = 0x240;
        internal const uint NativeSubmitBallQuestCompletedMask = 1;

        // rec 0x234..0x247 inclusive: the 20-byte block copied by the 5x movsd at 0x6B14AE.
        // Job 3 (rec 0x240, 4 bytes) is the last field that fits; a record shorter than
        // this cannot hold the family at all.
        internal const int NativeSubmitBallQuestBlockEndExclusive = 0x248;

        internal static readonly string[] NativeSubmitBallQuestRequiredItems =
        {
            "红色夜明珠",
            "橙色夜明珠",
            "黄色夜明珠",
            "绿色夜明珠",
            "蓝色夜明珠",
            "庄主令牌"
        };

        internal int SubmitNativeBallQuest()
        {
            if (IsNativeBallQuestCompleted()) return -1;

            var selected = new TUserItem[NativeSubmitBallQuestRequiredItems.Length];
            if (M2Share.UserEngine == null) return -2;

            for (var itemIndex = 0; itemIndex < m_ItemList.Count; itemIndex++)
            {
                var item = m_ItemList[itemIndex];
                if (item == null || item.wIndex == 0) continue;

                var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                for (var requiredIndex = 0;
                     requiredIndex < NativeSubmitBallQuestRequiredItems.Length;
                     requiredIndex++)
                {
                    if (selected[requiredIndex] != null
                        || !string.Equals(itemName,
                            NativeSubmitBallQuestRequiredItems[requiredIndex],
                            StringComparison.Ordinal))
                        continue;
                    selected[requiredIndex] = item;
                    break;
                }
            }

            for (var i = 0; i < selected.Length; i++)
                if (selected[i] == null || !m_ItemList.Contains(selected[i]))
                    return -2;

            var record = EnsureNativeBallQuestRecord();
            if (record == null) return -2;

            var deletedItems = new List<TDeleteItem>(selected.Length);
            for (var i = 0; i < selected.Length; i++)
            {
                var item = selected[i];
                deletedItems.Add(new TDeleteItem
                {
                    sItemName = NativeSubmitBallQuestRequiredItems[i],
                    MakeIndex = item.MakeIndex
                });
                m_ItemList.Remove(item);
                Dispose(item);
            }

            var flags = BinaryPrimitives.ReadUInt32LittleEndian(
                record.AsSpan(NativeSubmitBallQuestFlagsOffset, sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                record.AsSpan(NativeSubmitBallQuestFlagsOffset, sizeof(uint)),
                flags | NativeSubmitBallQuestCompletedMask);
            IncrementNativeBallQuestJobReward(record);

            SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                deletedItems.Count, 0, 0,
                string.Empty, deletedItems);
            HasLevelUp(m_Abil.Level);
            return 1;
        }

        private bool IsNativeBallQuestCompleted()
        {
            if (m_NativeHumanData == null
                || m_NativeHumanData.Length <
                NativeSubmitBallQuestFlagsOffset + sizeof(uint))
                return false;
            return (BinaryPrimitives.ReadUInt32LittleEndian(
                        m_NativeHumanData.AsSpan(
                            NativeSubmitBallQuestFlagsOffset, sizeof(uint)))
                    & NativeSubmitBallQuestCompletedMask) != 0;
        }

        private byte[] EnsureNativeBallQuestRecord()
        {
            if (m_NativeHumanData == null)
                m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
            // Job 3 at rec 0x240 is the last 4 bytes of the 0x234..0x247 block, so this is
            // equivalently NativeSubmitBallQuestBlockEndExclusive (0x248).
            return m_NativeHumanData.Length >=
                   NativeSubmitBallQuestJob3Offset + sizeof(int)
                ? m_NativeHumanData
                : null;
        }

        private void IncrementNativeBallQuestJobReward(byte[] record)
        {
            switch (m_btJob)
            {
                case 0:
                case 1:
                case 2:
                    var offset = NativeSubmitBallQuestJob012Offset + m_btJob;
                    record[offset] = unchecked((byte)(record[offset] + 1));
                    break;
                case 3:
                    var value = BinaryPrimitives.ReadInt32LittleEndian(
                        record.AsSpan(NativeSubmitBallQuestJob3Offset, sizeof(int)));
                    BinaryPrimitives.WriteInt32LittleEndian(
                        record.AsSpan(NativeSubmitBallQuestJob3Offset, sizeof(int)),
                        unchecked(value + 1));
                    break;
            }
        }
    }
}
