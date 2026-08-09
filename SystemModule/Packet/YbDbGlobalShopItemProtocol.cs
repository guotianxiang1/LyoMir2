using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant decoder and pure cache decisions for native YBDB response 1101.
    /// No matching request Ident is proven, so this type performs no transport,
    /// cache, player, account, item, database, log, or gate mutation.
    /// </summary>
    public static class YbDbGlobalShopItemProtocol
    {
        public const ushort ResponseIdent = 1101;
        public const int CategoryMinimum = 0;
        public const int CategoryMaximum = 7;
        public const int EndMarkerQueryId = 10000;
        public const ushort CompletionGateCommand = 0x1191;
        public const int RecordSize = 180;
        public const int RecordsPerBatch = 10;
        public const int BatchPayloadSize = RecordSize * RecordsPerBatch;

        public const int NameOffset = 0;
        public const int NameCapacity = 15;
        public const int TypeNameOffset = 16;
        public const int TypeNameCapacity = 15;
        public const int LooksOffset = 32;
        public const int CategoryOffset = 34;
        public const int PriceOffset = 36;
        public const int CurrentPriceOffset = 38;
        public const int TypeOffset = 40;
        public const int CountOffset = 42;
        public const int EffectCountOffset = 44;
        public const int EffectOffsetOffset = 48;
        public const int DescriptionOffset = 52;
        public const int DescriptionCapacity = 127;

        private static readonly Encoding Gbk;
        private static readonly HashSet<string> SpecialItemNames = new(
            StringComparer.Ordinal)
        {
            "气血石(小)",
            "气血石(中)",
            "气血石(大)",
            "幻魔石(小)",
            "幻魔石(中)",
            "幻魔石(大)",
            "比奇传送石",
            "魔血石(大)",
            "双倍秘籍",
            "双倍宝典",
            "双倍卷轴",
            "修复神水"
        };

        static YbDbGlobalShopItemProtocol()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936);
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "global-shop item response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"global-shop item response Ident must be {ResponseIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (frame.QueryId == EndMarkerQueryId)
            {
                // The native handler ignores BodyLength and Param on this branch.
                response = new Response(ResponseKind.EndMarker, frame.QueryId,
                    frame.Param, payload.Length, Array.Empty<ItemRecord>());
                return true;
            }

            if (frame.QueryId < CategoryMinimum
                || frame.QueryId > CategoryMaximum)
            {
                error = "global-shop item category QueryId must be 0..7 or 10000";
                return false;
            }
            if (payload.Length != BatchPayloadSize)
            {
                error = $"global-shop item batch payload must be " +
                        $"{BatchPayloadSize} bytes";
                return false;
            }

            var records = new List<ItemRecord>(RecordsPerBatch);
            for (var index = 0; index < RecordsPerBatch; index++)
            {
                var raw = payload.AsSpan(index * RecordSize, RecordSize);
                if (raw[NameOffset] == 0) break;
                records.Add(DecodeDeclaredRecord(raw));
            }

            response = new Response(ResponseKind.CategoryBatch, frame.QueryId,
                frame.Param, payload.Length, records.ToArray());
            return true;
        }

        public static RecordDecision EvaluateRecord(ItemRecord record,
            int category, int capacity, int currentCount,
            bool standardItemFound, ushort canonicalLooks)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (category < CategoryMinimum || category > CategoryMaximum)
                throw new ArgumentOutOfRangeException(nameof(category));

            var patched = (byte[])record.Raw.Clone();
            var resolvedLooks = standardItemFound
                ? canonicalLooks
                : unchecked((ushort)record.EffectOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(
                patched.AsSpan(LooksOffset, sizeof(ushort)), resolvedLooks);

            // The native signed comparison is capacity <= current list count.
            if (capacity <= currentCount)
            {
                Array.Clear(patched, 0, patched.Length);
                return new RecordDecision(false, !standardItemFound, true,
                    resolvedLooks, patched, null);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(
                patched.AsSpan(CategoryOffset, sizeof(ushort)),
                unchecked((ushort)category));
            var companion = new CompanionRecord(record.Name,
                IsNativeSpecialItemName(record.Name), record.CurrentPrice);
            return new RecordDecision(true, !standardItemFound, false,
                resolvedLooks, patched, companion);
        }

        public static CompletionDecision EvaluateCompletion(Response response)
        {
            if (response == null || response.Kind != ResponseKind.EndMarker)
                return CompletionDecision.Ignore;
            return new CompletionDecision(true, CompletionGateCommand);
        }

        public static bool IsNativeSpecialItemName(string itemName) =>
            itemName != null && SpecialItemNames.Contains(itemName);

        internal static ItemRecord DecodeDeclaredRecord(ReadOnlySpan<byte> record)
        {
            var raw = record.ToArray();
            return new ItemRecord(raw,
                ReadDeclaredShortString(record, NameOffset, NameCapacity),
                ReadDeclaredShortString(record, TypeNameOffset, TypeNameCapacity),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(LooksOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(CategoryOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(PriceOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(CurrentPriceOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(TypeOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(CountOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(EffectCountOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(EffectOffsetOffset, sizeof(uint))),
                ReadDeclaredShortString(record, DescriptionOffset,
                    DescriptionCapacity));
        }

        private static string ReadDeclaredShortString(ReadOnlySpan<byte> data,
            int offset, int capacity)
        {
            // Frames are trusted native records. Clamp malformed length bytes so
            // dormant inspection never adds a rejection gate absent in Delphi.
            var length = Math.Min(data[offset], capacity);
            return Gbk.GetString(data.Slice(offset + 1, length));
        }

        public enum ResponseKind
        {
            CategoryBatch,
            EndMarker
        }

        public sealed class Response
        {
            internal Response(ResponseKind kind, int queryId,
                int ignoredHeaderParam, int ignoredPayloadLength,
                ItemRecord[] records)
            {
                Kind = kind;
                QueryId = queryId;
                IgnoredHeaderParam = ignoredHeaderParam;
                IgnoredPayloadLength = ignoredPayloadLength;
                Records = records ?? Array.Empty<ItemRecord>();
            }

            public ResponseKind Kind { get; }
            public int QueryId { get; }
            public int IgnoredHeaderParam { get; }
            public int IgnoredPayloadLength { get; }
            public ItemRecord[] Records { get; }
            public bool UsesRoleOrSessionRouting => false;
            public bool CreatesPendingRequest => false;
        }

        public sealed class ItemRecord
        {
            internal ItemRecord(byte[] raw, string name, string typeName,
                ushort looks, ushort category, ushort price,
                ushort currentPrice, ushort type, ushort count,
                ushort effectCount, uint effectOffset, string description)
            {
                Raw = raw;
                Name = name;
                TypeName = typeName;
                Looks = looks;
                Category = category;
                Price = price;
                CurrentPrice = currentPrice;
                Type = type;
                Count = count;
                EffectCount = effectCount;
                EffectOffset = effectOffset;
                Description = description;
            }

            public byte[] Raw { get; }
            public string Name { get; }
            public string TypeName { get; }
            public ushort Looks { get; }
            public ushort Category { get; }
            public ushort Price { get; }
            public ushort CurrentPrice { get; }
            public ushort Type { get; }
            public ushort Count { get; }
            public ushort EffectCount { get; }
            public uint EffectOffset { get; }
            public string Description { get; }
        }

        public sealed class CompanionRecord
        {
            internal CompanionRecord(string name, bool specialItem,
                uint currentPrice)
            {
                Name = name;
                SpecialItem = specialItem;
                CurrentPrice = currentPrice;
            }

            public string Name { get; }
            public bool SpecialItem { get; }
            public uint CurrentPrice { get; }
        }

        public sealed class RecordDecision
        {
            internal RecordDecision(bool append, bool logMissingStandardItem,
                bool logCapacityOverflow, ushort resolvedLooks,
                byte[] patchedRecord, CompanionRecord companion)
            {
                Append = append;
                LogMissingStandardItem = logMissingStandardItem;
                LogCapacityOverflow = logCapacityOverflow;
                ResolvedLooks = resolvedLooks;
                PatchedRecord = patchedRecord;
                Companion = companion;
            }

            public bool Append { get; }
            public bool LogMissingStandardItem { get; }
            public bool LogCapacityOverflow { get; }
            public ushort ResolvedLooks { get; }
            public byte[] PatchedRecord { get; }
            public CompanionRecord Companion { get; }
            public bool MutatesRuntime => false;
            public bool SendsClientMessageOrAck => false;
            public bool MutatesPlayerAccountInventoryOrDatabase => false;
            public bool WritesBusinessGameLog => false;
        }

        public sealed class CompletionDecision
        {
            internal static CompletionDecision Ignore { get; } = new(false, 0);

            internal CompletionDecision(bool emitGateBroadcast,
                ushort gateCommand)
            {
                EmitGateBroadcast = emitGateBroadcast;
                GateCommand = gateCommand;
            }

            public bool EmitGateBroadcast { get; }
            public ushort GateCommand { get; }
            public bool SendsClient812Or815 => false;
            public bool SendsPurchase816 => false;
            public bool SendsAck => false;
            public bool MutatesDatabaseAccountOrInventory => false;
            public bool WritesBusinessGameLog => false;
        }
    }
}
