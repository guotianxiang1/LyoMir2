using System.Buffers.Binary;
using SystemModule;

namespace GameSvr.Services
{
    // ================================================================================================
    // Stall (摆摊) CM wire codec — the BYTE-EXACT field extraction + the 4418 browse-response builder,
    // reversed in staging/stall_cm_wire_formats_20260802.md. This is the SINGLE SOURCE OF TRUTH for
    // "which message field carries which stall wire field", so the live handlers (TPlayObject.NativeStall)
    // and AuditTools/NativeStallWireIntegrationCheck drive the EXACT SAME code.
    //
    // Verified wire -> TProcessMessage mapping (all stall ops hit the ProcessUserMessage DEFAULT case
    // `SendMsg(obj, Ident, Series, Recog, Param, Tag, sMsg, payload)`; ClientPacket header = Recog i32@0 /
    // Ident u16@4 / Param u16@6 / Tag u16@8 / Series u16@0xA):
    //   Recog  (dword@0)  -> msg.nParam1     (full 32 bits)
    //   Param  (word@6)   -> msg.nParam2
    //   Tag    (word@8)   -> msg.nParam3
    //   Series (word@0xA) -> msg.wParam
    //   encoded body      -> msg.Payload  (6-bit ENCODED; DECODE before reading binary fields)
    //
    // Owner identity on the wire is ALWAYS the target's 64-bit CharID = body[0](lo dword) | body[4](hi dword),
    // read as one little-endian int64 (the same as NativeCorpsWireCodec.TryReadId). NEVER a name string, and
    // NEVER the lossy GBK sMsg — reading sMsg corrupts any id byte >= 0x80.
    // ================================================================================================
    public static class NativeStallWireCodec
    {
        /// <summary>Minimum decoded body for the CharID-carrying ops (4418 Query / 4426 Buy / 4467 Message).</summary>
        public const int OwnerCharIdSize = 8;

        /// <summary>Minimum decoded body for 4421 AddItem (the single uprice dword lives in the body).</summary>
        public const int AddItemBodySize = 4;

        // ---- 4418 browse RESPONSE (sub_61DA00) layout, byte-exact from spec §3b ----
        public const int QueryHeaderSize = 88;      // 0x58 header-only length for an empty stall
        public const int QueryItemRecordSize = 16;  // one {ClientItemID,uprice,moneytype,count} scalar record
        private const int Name1MaxChars = 14;       // ShortString[15] content @0x08 (1 len byte + 14)
        private const int Name2MaxChars = 30;       // ShortString[33] content @0x17 (1 len byte + up to 30 +pad)

        // Header field offsets (spec §3b segment ①).
        private const int OffOwnerLo = 0x00;
        private const int OffOwnerHi = 0x04;
        private const int OffName1 = 0x08;
        private const int OffName2 = 0x17;
        private const int OffDbIdx = 0x38;
        private const int OffStatus = 0x3C;
        private const int OffTimeLevel = 0x40;
        private const int OffRemainSecs = 0x44;
        private const int OffCreateDate = 0x48;
        private const int OffOwnerOnline = 0x50;
        private const int OffItemCount = 0x54;

        // ============================ Body decode (shared 6-bit) ============================

        /// <summary>
        /// Decode the 6-bit-ENCODED CM body (<c>msg.Payload</c>) to raw bytes — mirrors the native CM dispatch
        /// (<c>sub_6D7D68</c>) and <c>DecodeNativeSocialBody</c>: strip one trailing 0 (as
        /// <c>DecodeClientMessageBody</c> does) then <c>Misc.Decode6BitBufDirect</c> with NO GBK, so binary
        /// int64 CharID / int32 ids stay byte-exact. Returns empty when there is no payload.
        /// </summary>
        public static byte[] DecodeBody(object payload)
        {
            if (payload is not byte[] raw || raw.Length == 0)
                return Array.Empty<byte>();
            var textLength = raw.Length;
            if (raw[textLength - 1] == 0) textLength--;       // mirror DecodeClientMessageBody trailing-0 strip
            if (textLength == 0) return Array.Empty<byte>();
            return Misc.Decode6BitBufDirect(raw, textLength); // raw 6-bit decode, NO GBK
        }

        // ============================ Per-op REQUEST decoders ============================

        // 4426 BuyItem (sub_6E7A04 -> sub_61C8E0 -> sub_61E8EC), disasm-proven end to end:
        //   ClientItemID = Recog (dword@0)  -> nParam1
        //   count        = Series (word@0xA) -> wParam
        //   seller CharID = body[0](lo)/body[4](hi) -> decoded int64 LE @0
        // Returns false when the decoded body is shorter than the 8-byte CharID (malformed => caller rejects).
        public static bool TryDecodeBuyRequest(TProcessMessage msg, out long ownerId,
            out int clientItemId, out int count)
        {
            clientItemId = msg?.nParam1 ?? 0;   // Recog
            count = msg?.wParam ?? 0;           // Series (word, 0..65535)
            return TryReadOwnerCharId(DecodeBody(msg?.Payload), out ownerId);
        }

        // 4421 AddItem (sub_6E7CF4 -> sub_61BC7C):
        //   ClientItemID = Recog (dword@0) -> nParam1
        //   count        = Param (word@6)  -> nParam2
        //   moneytype    = Tag   (word@8)  -> nParam3
        //   uprice       = body[0] (dword) -> decoded int32 LE @0
        // Returns false when the decoded body cannot hold the 4-byte uprice (spec §5.2 length guard).
        public static bool TryDecodeAddItemRequest(TProcessMessage msg, out int clientItemId,
            out int uprice, out int moneyType, out int count)
        {
            clientItemId = msg?.nParam1 ?? 0;   // Recog
            count = msg?.nParam2 ?? 0;          // Param
            moneyType = msg?.nParam3 ?? 0;      // Tag
            var body = DecodeBody(msg?.Payload);
            if (body.Length < AddItemBodySize)
            {
                uprice = 0;
                return false;
            }
            uprice = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, 4));
            return true;
        }

        // 4422 DelItem (sub_6E7D4C -> sub_61BECC): ClientItemID = Recog (dword@0) -> nParam1. Owner = self.
        public static int DecodeStallItemClientId(TProcessMessage msg) => msg?.nParam1 ?? 0;

        // 4419 SetTimeLevel (sub_6E7938 -> sub_61D294): duration/time-level = Param (word@6) -> nParam2.
        // (Timestamp = Tag(word@8) -> nParam3; applied by the booth-setup executor's follow-up, not here.)
        public static int DecodeSetTimeLevelDuration(TProcessMessage msg) => msg?.nParam2 ?? 0;

        // 4420 SetName (sub_6E7984 -> sub_61D3E0): name = the WHOLE decoded body as a GBK C-string (max 30).
        public static string DecodeSetStallName(TProcessMessage msg)
            => ReadGbkCString(DecodeBody(msg?.Payload), 0);

        // 4418 QueryStall (sub_6E7B2C -> sub_61BA80): target owner CharID = body[0](lo)/body[4](hi).
        // 0 or == self => own-stall view (resolved by the caller); else browse that owner.
        public static bool TryDecodeQueryOwner(TProcessMessage msg, out long ownerId)
            => TryReadOwnerCharId(DecodeBody(msg?.Payload), out ownerId);

        // 4467 MessageStall (sub_6E7A64 -> sub_61C80C): target owner CharID = body[0]/body[4], text @ body+8.
        // (Codec-level wire decode for completeness/audit; the message-delivery executor is not wired.)
        public static bool TryDecodeMessageStall(TProcessMessage msg, out long ownerId, out string text)
        {
            var body = DecodeBody(msg?.Payload);
            text = ReadGbkCString(body, OwnerCharIdSize);
            return TryReadOwnerCharId(body, out ownerId);
        }

        // 64-bit CharID = body[0](lo dword) | body[4](hi dword) read as one little-endian int64
        // (identical to NativeCorpsWireCodec.TryReadId). Requires >= 8 decoded bytes.
        private static bool TryReadOwnerCharId(byte[] body, out long ownerId)
        {
            ownerId = 0;
            if (body == null || body.Length < OwnerCharIdSize)
                return false;
            ownerId = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(0, OwnerCharIdSize));
            return true;
        }

        // Lenient GBK C-string (up to the first NUL) from a decoded-body offset — for the name/message text
        // slices ONLY (numeric fields never go through GBK). Empty when the offset is past the end.
        private static string ReadGbkCString(byte[] body, int offset)
        {
            if (body == null || offset < 0 || offset >= body.Length)
                return string.Empty;
            var end = Array.IndexOf(body, (byte)0, offset);
            if (end < 0) end = body.Length;
            if (end <= offset) return string.Empty;
            return HUtil32.GetString(body, offset, end - offset);
        }

        // ============================ 4418 browse RESPONSE builder ============================

        /// <summary>
        /// Build the 4418 browse-response PAYLOAD (<c>sub_61DA00</c>, spec §3b): an 88-byte header +
        /// a <c>16 * itemCount</c> item-scalar array (<c>{ClientItemID, uprice, moneytype, count}</c>, the
        /// records BUY echoes back in <c>Recog</c>) + the per-item detail blob. Empty stall => header only
        /// (length 88). The per-item ClientItemID must already be assigned on <see cref="TUserItem.ClientItemID"/>
        /// (native writes back <c>item+0xFC</c>); the caller runs <c>EnsureClientItemId</c> first.
        /// </summary>
        /// <param name="record">The resolved stall (own or browse target).</param>
        /// <param name="remainingSeconds">sub_61F3D8: seconds of paid time left (0 when not running).</param>
        /// <param name="ownerOnline">sub_708C4C(ownerId): 1 when the owner is online.</param>
        /// <param name="serializeItemBlob">
        /// Per-item detail serializer (segment ③ — "the same wire an item uses elsewhere", the live path passes
        /// the item client-record encoder). May return empty; the blob is appended verbatim after the array.
        /// </param>
        public static byte[] BuildQueryResponse(NativeStallRecord record, int remainingSeconds,
            bool ownerOnline, Func<TUserItem, byte[]> serializeItemBlob)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var items = record.Items ?? new List<NativeStallItem>();
            var count = items.Count;

            // -------- segment ① : 88-byte header --------
            var header = new byte[QueryHeaderSize];
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffOwnerLo, 4),
                unchecked((int)(record.OwnerId & 0xFFFFFFFF)));
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffOwnerHi, 4),
                unchecked((int)((record.OwnerId >> 32) & 0xFFFFFFFF)));
            // name#1 (ShortString[15]) / name#2 (ShortString[33]). FLAGGED (pre-flip): the byte LAYOUT is exact,
            // but which record string feeds each (OwnerName<=14 / StallName<=30) is a pre-flip assumption.
            WriteShortString(header, OffName1, Name1MaxChars, record.OwnerName);
            WriteShortString(header, OffName2, Name2MaxChars, record.StallName);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffDbIdx, 4), record.DbIdx);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffStatus, 4), (int)record.Status);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffTimeLevel, 4), record.DuraTime);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffRemainSecs, 4), remainingSeconds);
            BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(OffCreateDate, 8),
                record.CreateDate.ToOADate());
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffOwnerOnline, 4), ownerOnline ? 1 : 0);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(OffItemCount, 4), count);

            using var ms = new MemoryStream(QueryHeaderSize + count * QueryItemRecordSize);
            ms.Write(header, 0, header.Length);

            // -------- segment ② : 16 * itemCount scalar array --------
            var record16 = new byte[QueryItemRecordSize];
            foreach (var si in items)
            {
                var clientItemId = si?.Item?.ClientItemID ?? 0;
                BinaryPrimitives.WriteInt32LittleEndian(record16.AsSpan(0x00, 4), clientItemId);
                BinaryPrimitives.WriteInt32LittleEndian(record16.AsSpan(0x04, 4), si?.UnitPrice ?? 0);
                BinaryPrimitives.WriteInt32LittleEndian(record16.AsSpan(0x08, 4), si?.MoneyType ?? 0);
                BinaryPrimitives.WriteInt32LittleEndian(record16.AsSpan(0x0C, 4), si?.ItemCount ?? 0);
                ms.Write(record16, 0, record16.Length);
            }

            // -------- segment ③ : per-item detail blob --------
            if (serializeItemBlob != null)
            {
                foreach (var si in items)
                {
                    if (si?.Item == null) continue;
                    var blob = serializeItemBlob(si.Item);
                    if (blob != null && blob.Length > 0)
                        ms.Write(blob, 0, blob.Length);
                }
            }

            return ms.ToArray();
        }

        // Delphi ShortString: 1 length byte + up to maxChars GBK content bytes (fixed-offset zero padded).
        private static void WriteShortString(byte[] buf, int offset, int maxChars, string text)
        {
            var bytes = string.IsNullOrEmpty(text) ? Array.Empty<byte>() : HUtil32.GetBytes(text);
            var len = Math.Min(bytes.Length, maxChars);
            buf[offset] = (byte)len;
            if (len > 0)
                Array.Copy(bytes, 0, buf, offset + 1, len);
        }
    }
}
