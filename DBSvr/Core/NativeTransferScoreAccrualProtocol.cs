using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeTransferScoreAccrualRequest
    {
        /// <summary>ShortString at header+0x35 — the character name.</summary>
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
        /// <summary>Low word of the dword at header+0x04 — score index, valid 1..3.</summary>
        public ushort ScoreIndex { get; init; }
        /// <summary>High word of the dword at header+0x04 — the accrual delta.</summary>
        public ushort Delta { get; init; }
    }

    /// <summary>
    /// Native type1 command 0x0174, reversed byte-for-byte from the 战神 DBServer.
    ///
    /// Handler 0x599274 gates on the <c>TTransferAreaScoreManager</c> singleton
    /// ([0x5D9B00], class name recovered from its ctor 0x594CC0) being non-null,
    /// then calls 0x595CF4 to build a 0x27-byte pending record and 0x595DCC to
    /// file it under a three-part key. The queued record is later turned into SQL
    /// by 0x595FE8:
    ///
    ///   Insert into TransferAreaScore(CharName, Score1, Score2, Score3)
    ///   Values("%s", %d, %d, %d) on duplicate key update
    ///   Score1=Score1+%d, Score2=Score2+%d, Score3=Score3+%d;
    ///
    /// 0x596026 reads the record's <c>word[+0x20]</c> score index, and
    /// <c>dec eax / sub ax,3 / jae</c> accepts only 1..3; the matching slot then
    /// receives <c>word[+0x22]</c> while the other two stay 0 — so exactly one of
    /// the three scores accrues per request, and the accrual is ADDITIVE.
    ///
    /// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3之三.
    /// </summary>
    public static class NativeTransferScoreAccrualProtocol
    {
        public const ushort RequestCommand = 0x0174;

        /// <summary>0x59DDAC: the 0x48-byte type1 header.</summary>
        public const int HeaderSize = 0x48;
        /// <summary>0x595D25 allocates a 0x27-byte pending record.</summary>
        public const int PendingRecordSize = 0x27;
        /// <summary>0x596026-0x59602E accepts score index 1..3 only.</summary>
        public const int MinimumScoreIndex = 1;
        public const int MaximumScoreIndex = 3;

        private const int ScoreIndexOffset = 0x04;
        private const int CharacterNameOffset = 0x35;
        private const int ShortStringCapacity = 0x0F;

        /// <summary>
        /// True when the index selects a real Score column. Out-of-range values
        /// take the <c>jae 0x596047</c> branch, which skips the assignment and
        /// leaves all three deltas at 0 — the row is still upserted, but nothing
        /// accrues.
        /// </summary>
        public static bool IsScoreIndexInRange(int scoreIndex) =>
            scoreIndex >= MinimumScoreIndex && scoreIndex <= MaximumScoreIndex;

        /// <summary>
        /// Spreads the delta across (Score1, Score2, Score3) exactly as
        /// 0x596042 does: only the slot named by the index is non-zero.
        /// </summary>
        public static void SpreadDelta(int scoreIndex, int delta,
            out int score1, out int score2, out int score3)
        {
            score1 = score2 = score3 = 0;
            switch (scoreIndex)
            {
                case 1: score1 = delta; break;
                case 2: score2 = delta; break;
                case 3: score3 = delta; break;
            }
        }

        public static bool TryDecodeRequest(LegacyDbServerFrame frame,
            out NativeTransferScoreAccrualRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native 0174 frame is null";
                return false;
            }
            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length < HeaderSize)
            {
                error = "native 0174 payload is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != RequestCommand)
            {
                error = "native 0174 command mismatch";
                return false;
            }
            if (!TryReadShortString(payload, CharacterNameOffset,
                    ShortStringCapacity, out var characterName, out error))
                return false;

            // Both values ride the SINGLE dword at header+0x04: 0x5992A8 takes its
            // low word into cx (→ record+0x20, the score index) and 0x599288 runs
            // it through 0x4080B0, which is just `shr eax,0x10`, pushing the HIGH
            // word (→ record+0x22, the delta).
            var indexAndDelta = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(ScoreIndexOffset, 4));

            request = new NativeTransferScoreAccrualRequest
            {
                CharacterName = characterName,
                ScoreIndex = unchecked((ushort)indexAndDelta),
                Delta = unchecked((ushort)(indexAndDelta >> 16)),
            };
            return true;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> payload,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = Array.Empty<byte>();
            error = string.Empty;
            if (offset >= payload.Length) return true;
            var length = payload[offset];
            if (length > capacity)
            {
                error = $"native 0174 ShortString at 0x{offset:X2} "
                        + $"exceeds {capacity} bytes";
                return false;
            }
            if (offset + 1 + length > payload.Length)
            {
                error = $"native 0174 ShortString at 0x{offset:X2} "
                        + "runs past the header";
                return false;
            }
            value = payload.Slice(offset + 1, length).ToArray();
            return true;
        }
    }
}
