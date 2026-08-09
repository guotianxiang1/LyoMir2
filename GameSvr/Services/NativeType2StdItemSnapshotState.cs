using System.Buffers.Binary;
using System.Text;

namespace GameSvr.Services
{
    public enum NativeType2StdItemSnapshotResult
    {
        Ignored,
        SequenceRejected,
        RecordAppended,
        RecordAppendedWithExtensionError,
        StreamCompleted,
        SequenceRejectedAndCompleted,
        RecordAppendedAndCompleted,
        RecordAppendedWithExtensionErrorAndCompleted
    }

    /// <summary>
    /// Isolated native Type2 104 (0x0068) standard-item stream. The original
    /// receiver validates body+0 against the live native TList.Count, so the
    /// startup baseline is deliberately supplied by the caller rather than
    /// guessed as zero or one. This state is not a GoodItem projection.
    /// </summary>
    public sealed class NativeType2StdItemSnapshotState
    {
        public const ushort Command = 0x0068;
        public const int HeaderSize = 12;
        public const int BodySize = 0x134;
        public const int PacketSize = HeaderSize + BodySize;

        // Verified from the original GS1 startup process: the native StdItem
        // TList already contains a 140-byte, otherwise-zero index-0 entry.
        // Its only populated ShortString is 04 BD F0 B1 D2 ("金币"). It is
        // local startup state and therefore remains outside the wire snapshot.
        public const int VerifiedOriginalStartupListCount = 1;

        private readonly List<NativeType2StdItemRawRecord> _records = new();
        private readonly IReadOnlyList<NativeType2StdItemRawRecord> _view;
        private readonly Dictionary<string, NativeType2StdItemRawRecord>
            _latestByName = new(StringComparer.Ordinal);
        private readonly int _initialNativeListCount;
        private Action<NativeType2StdItemSnapshotState> _completionCallback;

        /// <summary>
        /// Creates the state for the verified original GS1 startup baseline.
        /// Callers that model another native list lifetime must still provide
        /// their own explicit initial count.
        /// </summary>
        public static NativeType2StdItemSnapshotState
            CreateForVerifiedOriginalStartup() =>
            new(VerifiedOriginalStartupListCount);

        public NativeType2StdItemSnapshotState(int initialNativeListCount)
        {
            if (initialNativeListCount < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(initialNativeListCount));

            _initialNativeListCount = initialNativeListCount;
            _view = _records.AsReadOnly();
        }

        public bool Completed { get; private set; }
        public int InitialNativeListCount => _initialNativeListCount;
        public int ExpectedWireIndex => _initialNativeListCount + _records.Count;
        public IReadOnlyList<NativeType2StdItemRawRecord> Records => _view;

        /// <summary>
        /// Models the native terminal callback timing. It intentionally does
        /// not load NewQKBag files because no verified C# QKBag runtime model
        /// exists yet.
        /// </summary>
        public void SetCompletionCallback(
            Action<NativeType2StdItemSnapshotState> callback)
        {
            _completionCallback = callback;
        }

        public bool TryGetLatestByNameBytes(ReadOnlySpan<byte> nameBytes,
            out NativeType2StdItemRawRecord record)
        {
            if (nameBytes.Length > NativeType2StdItemRawRecord.NameCapacity)
            {
                record = null;
                return false;
            }

            return _latestByName.TryGetValue(
                NativeType2StdItemRawRecord.ToNameKey(nameBytes), out record);
        }

        public NativeType2StdItemSnapshotResult Consume(
            ReadOnlySpan<byte> payload)
        {
            if (payload.Length < HeaderSize
                || BinaryPrimitives.ReadUInt16LittleEndian(payload) != Command
                || Completed)
                return NativeType2StdItemSnapshotResult.Ignored;

            var result = NativeType2StdItemSnapshotResult.Ignored;
            var body = payload.Slice(HeaderSize);
            if (body.Length >= BodySize)
            {
                var wireIndex = BinaryPrimitives.ReadUInt16LittleEndian(body);
                if (wireIndex != ExpectedWireIndex)
                {
                    result = NativeType2StdItemSnapshotResult.SequenceRejected;
                }
                else
                {
                    // A numeric conversion exception intentionally escapes.
                    // Native sub_7512B4 cannot append the item in that case.
                    var record = new NativeType2StdItemRawRecord(body);
                    _records.Add(record);
                    _latestByName[record.NameKey] = record;
                    result = record.ItemExtAbilParsed
                        ? NativeType2StdItemSnapshotResult.RecordAppended
                        : NativeType2StdItemSnapshotResult
                            .RecordAppendedWithExtensionError;
                }
            }

            if (BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)) != 1)
                return result;

            Completed = true;
            var callback = _completionCallback;
            try
            {
                callback?.Invoke(this);
            }
            finally
            {
                _completionCallback = null;
            }

            return result switch
            {
                NativeType2StdItemSnapshotResult.SequenceRejected =>
                    NativeType2StdItemSnapshotResult.SequenceRejectedAndCompleted,
                NativeType2StdItemSnapshotResult.RecordAppended =>
                    NativeType2StdItemSnapshotResult.RecordAppendedAndCompleted,
                NativeType2StdItemSnapshotResult.RecordAppendedWithExtensionError =>
                    NativeType2StdItemSnapshotResult
                        .RecordAppendedWithExtensionErrorAndCompleted,
                _ => NativeType2StdItemSnapshotResult.StreamCompleted
            };
        }

        public void Reset()
        {
            _records.Clear();
            _latestByName.Clear();
            _completionCallback = null;
            Completed = false;
        }
    }

    /// <summary>
    /// Raised for the native StrToInt path. Unlike an unknown property name,
    /// this is not converted to an ItemExtAbil parse failure by original M2.
    /// </summary>
    public sealed class NativeType2StdItemNumericException : FormatException
    {
        public NativeType2StdItemNumericException(ReadOnlySpan<byte> value)
            : base("invalid native ItemExtAbil numeric literal")
        {
            ValueBytes = value.ToArray();
        }

        public byte[] ValueBytes { get; }
    }

    /// <summary>
    /// Raw 0x134-byte wire body plus the six native GoodItem extension slots.
    /// The slots are target fields at native offsets 0x60..0x77, not a C#
    /// GoodItem model.
    /// </summary>
    public sealed class NativeType2StdItemRawRecord
    {
        internal const int NameCapacity = 15;
        private const int ItemExtAbilOffset = 0x5C;
        private const int ItemExtAbilCapacity = 200;
        private const int ExtensionSlotCount = 6;
        private const int ExtensionSlotSize = 4;

        private static readonly Encoding Gbk = CreateGbk();
        private static readonly Dictionary<string, ushort> PrimaryCodes =
            BuildPrimaryCodes();
        private static readonly Dictionary<string, ushort> SecondaryCodes =
            BuildSecondaryCodes();

        private readonly byte[] _wireBody;
        private readonly byte[] _nameShortString;
        private readonly byte[] _extensionSlots = new byte[
            ExtensionSlotCount * ExtensionSlotSize];

        internal NativeType2StdItemRawRecord(ReadOnlySpan<byte> body)
        {
            _wireBody = body.Slice(0, NativeType2StdItemSnapshotState.BodySize)
                .ToArray();
            _nameShortString = CopyNativeName(_wireBody);
            NameKey = ToNameKey(_nameShortString.AsSpan(1,
                _nameShortString[0]));
            ItemExtAbilParsed = TryParseItemExtAbil();
        }

        public ushort WireIndex =>
            BinaryPrimitives.ReadUInt16LittleEndian(_wireBody);

        public ReadOnlyMemory<byte> NativeNameShortString => _nameShortString;
        public ReadOnlyMemory<byte> ExtensionSlots => _extensionSlots;
        public bool ItemExtAbilParsed { get; }

        internal string NameKey { get; }

        public byte[] CopyWireBody() => (byte[])_wireBody.Clone();
        public byte[] CopyExtensionSlots() => (byte[])_extensionSlots.Clone();

        public byte[] CopyItemExtAbilBytes()
        {
            var length = Math.Min((int)_wireBody[ItemExtAbilOffset],
                ItemExtAbilCapacity);
            return _wireBody.AsSpan(ItemExtAbilOffset + 1, length).ToArray();
        }

        private bool TryParseItemExtAbil()
        {
            var declaredLength = _wireBody[ItemExtAbilOffset];
            if (declaredLength > ItemExtAbilCapacity)
                return false;

            ReadOnlySpan<byte> remaining = _wireBody.AsSpan(
                ItemExtAbilOffset + 1, declaredLength);
            var slot = 0;
            while (!remaining.IsEmpty)
            {
                if (slot >= ExtensionSlotCount)
                    return false;

                SplitFirst(remaining, (byte)'|', out var segment,
                    out remaining);
                SplitFirst(segment, (byte)':', out var rawName,
                    out var rawValue);
                var name = TrimNativeWhitespace(rawName);
                var nameKey = ToNameKey(name);
                var slotOffset = slot * ExtensionSlotSize;

                if (PrimaryCodes.TryGetValue(nameKey, out var primaryCode))
                {
                    var value = ParseNativeInt32(rawValue);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        _extensionSlots.AsSpan(slotOffset, 2), primaryCode);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        _extensionSlots.AsSpan(slotOffset + 2, 2),
                        unchecked((ushort)value));
                }
                else if (SecondaryCodes.TryGetValue(nameKey,
                             out var secondaryCode)
                         && secondaryCode > 0)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        _extensionSlots.AsSpan(slotOffset, 2), 0x00FE);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        _extensionSlots.AsSpan(slotOffset + 2, 2),
                        secondaryCode);
                }
                else
                {
                    return false;
                }

                slot++;
            }

            return true;
        }

        private static int ParseNativeInt32(ReadOnlySpan<byte> value)
        {
            // Delphi's System.ValLong, reached by StrToInt, skips leading
            // ASCII spaces only and does not treat a trailing space as valid.
            var cursor = 0;
            while (cursor < value.Length && value[cursor] == (byte)' ')
                cursor++;
            if (cursor == value.Length)
                throw new NativeType2StdItemNumericException(value);

            var negative = false;
            if (value[cursor] == (byte)'-' || value[cursor] == (byte)'+')
            {
                negative = value[cursor] == (byte)'-';
                cursor++;
            }
            if (cursor == value.Length)
                throw new NativeType2StdItemNumericException(value);

            var radix = 10;
            if (value[cursor] is (byte)'$' or (byte)'x' or (byte)'X')
            {
                radix = 16;
                cursor++;
            }
            else if (cursor + 1 < value.Length
                     && value[cursor] == (byte)'0'
                     && value[cursor + 1] is (byte)'x' or (byte)'X')
            {
                radix = 16;
                cursor += 2;
            }

            if (cursor == value.Length)
                throw new NativeType2StdItemNumericException(value);

            ulong parsed = 0;
            var limit = negative ? 0x80000000UL : 0x7FFFFFFFUL;
            for (; cursor < value.Length; cursor++)
            {
                var digit = GetDigit(value[cursor]);
                if (digit < 0 || digit >= radix
                    || parsed > (limit - (uint)digit) / (uint)radix)
                    throw new NativeType2StdItemNumericException(value);
                parsed = parsed * (uint)radix + (uint)digit;
            }

            if (!negative)
                return (int)parsed;
            return parsed == 0x80000000UL ? int.MinValue : -(int)parsed;
        }

        private static int GetDigit(byte value)
        {
            if (value is >= (byte)'0' and <= (byte)'9')
                return value - (byte)'0';
            if (value is >= (byte)'a' and <= (byte)'f')
                return value - (byte)'a' + 10;
            if (value is >= (byte)'A' and <= (byte)'F')
                return value - (byte)'A' + 10;
            return -1;
        }

        private static void SplitFirst(ReadOnlySpan<byte> source,
            byte separator, out ReadOnlySpan<byte> head,
            out ReadOnlySpan<byte> tail)
        {
            var index = source.IndexOf(separator);
            if (index < 0)
            {
                head = source;
                tail = ReadOnlySpan<byte>.Empty;
                return;
            }

            head = source.Slice(0, index);
            tail = source.Slice(index + 1);
        }

        private static ReadOnlySpan<byte> TrimNativeWhitespace(
            ReadOnlySpan<byte> value)
        {
            var start = 0;
            var end = value.Length;
            while (start < end && value[start] <= 0x20) start++;
            while (end > start && value[end - 1] <= 0x20) end--;
            return value.Slice(start, end - start);
        }

        private static byte[] CopyNativeName(ReadOnlySpan<byte> body)
        {
            var length = Math.Min((int)body[0x04], NameCapacity);
            var value = new byte[NameCapacity + 1];
            value[0] = unchecked((byte)length);
            body.Slice(0x05, length).CopyTo(value.AsSpan(1));
            return value;
        }

        internal static string ToNameKey(ReadOnlySpan<byte> value) =>
            Convert.ToBase64String(value.ToArray());

        private static Encoding CreateGbk()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }

        private static Dictionary<string, ushort> BuildPrimaryCodes()
        {
            var names = new[]
            {
                "攻击下限", "攻击上限", "魔法下限", "魔法上限", "道术下限", "道术上限", "防御下限", "防御上限",
                "魔御下限", "魔御上限", "体力值", "魔法值", "准确", "敏捷", "魔法躲避", "幸运",
                "诅咒", "攻击速度", "目标爆率", "防爆", "攻击吸血", "内力恢复速率", "内力恢复速度", "内功伤害",
                "内功减免", "内伤等级", "暴击等级", "负重", "合击威力", "麻痹抗性", "神圣", "药品魔法值回复",
                "药品体力值回复", "内力值上限", "强身等级", "聚魔等级", "主属性", "中毒恢复", "狂暴等级", "神圣攻击下限",
                "神圣攻击上限", "神圣魔法下限", "神圣魔法上限", "神圣道术下限", "神圣道术上限", "体力值百分比", "魔法值百分比", "神圣主属性下限",
                "神圣主属性上限", "神圣幸运", "装备主属性", "护体神盾强化", "击破", "麻痹强化", "龙神之怒", "乾坤借力",
                "怒之火雨增强", "怒之剑术增强", "怒之火符增强", "内功吸收", "连击威力增强", "合击等级", "扭转乾坤", "伤害百分比吸收",
                "魔血值", "冰冻抗性", "钢筋铁骨", "灭世", "强化重生", "觉醒", "法术伤害增强", "护身神技",
                "至尊护身神技", "白日门乾坤", "合击伤害抗性", "火墙伤害抗性", "近战伤害抗性", "金钟罩身", "龙神护体", "合击伤害减少",
                "准确百分比", "敏捷百分比", "麻痹时间增加", "龙神技能CD减少", "龙神之怒CD减少", "防麻时间增加", "扭转乾坤CD减少", "魔意麻痹神技",
                "道意麻痹神技", "伤害增加", "合击伤害减免", "合击威力增加", "裂石", "凝冰", "连击伤害抗性", "药品魔血值回复",
                "真龙护体", "致命几率", "致命伤害增加", "防致命几率", "致命伤害减少", "魔法伤害抗性", "道术伤害抗性", "龙神技能抗性",
                "十步一杀伤害增加", "天雷乱舞每秒伤害增加", "怒噬回天回血增加", "嗜血杀戮伤害增加", "复仇火焰伤害增加", "毁灭神符伤害增加", "刺术下限", "刺术上限",
                "神圣刺术下限", "神圣刺术上限", "血祭刃扇伤害增加", "升龙破", "神龙附体", "怒之暴击术增强", "召唤神龙护卫", "金元护体护盾时间",
                "金元护体护盾次数", "木元护体血量提升", "木元护体血量回复", "木元护体时间", "水元持续时间", "召唤水元伤害", "水元幸运", "水元断筋概率",
                "火元持续时间", "召唤火元伤害", "火元幸运", "火元祸乱概率", "火元祸乱伤害", "火元祸乱时间", "召唤土元伤害", "土元持续时间",
                "主属性百分比", "神圣主属性下限百分比", "神圣主属性上限百分比", "魔血值百分比", "伤害百分比减免", "神圣防御", "重击", "唯我独尊CD减少",
                "金攻击元素", "金防御元素", "木攻击元素", "木防御元素", "水攻击元素", "水防御元素", "火攻击元素", "火防御元素",
                "土攻击元素", "土防御元素", "低级坐骑装备属性增加", "中级坐骑装备属性增加", "高级坐骑装备属性增加", "魔法命中"
            };
            if (names.Length != 158)
                throw new InvalidOperationException("native primary attribute table drift");

            var result = new Dictionary<string, ushort>(StringComparer.Ordinal);
            for (var i = 0; i < names.Length; i++)
                result.Add(ToNameKey(Gbk.GetBytes(names[i])), unchecked((ushort)(i + 1)));
            result.Add(ToNameKey(Gbk.GetBytes("灵媒")), 0x00FF);
            return result;
        }

        private static Dictionary<string, ushort> BuildSecondaryCodes()
        {
            var names = new[]
            {
                "八卦护身神技", "战意麻痹神技", "重生神技", "探测神技",
                "传送神技", "麻痹神技", "魔道麻痹神技"
            };
            var result = new Dictionary<string, ushort>(StringComparer.Ordinal);
            for (var i = 0; i < names.Length; i++)
                result.Add(ToNameKey(Gbk.GetBytes(names[i])), unchecked((ushort)i));
            return result;
        }
    }
}
