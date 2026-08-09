using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace GameSvr.Services
{
    public enum NativeType2StdItemRuntimeDecodeResult
    {
        Ignored,
        PayloadTooShort,
        Decoded
    }

    public enum NativeType2StdItemAppendStatus
    {
        Ignored,
        PayloadTooShort,
        SequenceRejected,
        Appended,
        AppendedWithExtensionError
    }

    public enum NativeType2StdItemNeedIdentifyStatus
    {
        ResolverUnavailable,
        NotMatched,
        Resolved
    }

    public enum NativeType2StdItemScriptBindingStatus
    {
        BinderUnavailable,
        FileNotFound,
        Bound,
        CompileFailed
    }

    public enum NativeType2StdItemCorrelationStatus
    {
        NotRequested,
        ResolverUnavailable,
        TargetNotFound,
        InsufficientPermission,
        PromptEligible
    }

    public interface INativeType2StdItemNeedIdentifyResolver
    {
        bool TryResolve(ReadOnlyMemory<byte> nativeNameBytes,
            out byte needIdentify);
    }

    public interface INativeType2StdItemScriptBinder
    {
        NativeType2StdItemScriptBindingResult Bind(
            NativeType2StdItemDefinition definition,
            ReadOnlyMemory<byte> relativePathBytes);
    }

    /// <summary>
    /// Resolves Param1 as an opaque managed correlation token. It is never
    /// interpreted as the original 32-bit actor pointer.
    /// </summary>
    public interface INativeType2StdItemCorrelationResolver
    {
        bool TryResolvePermission(int correlation,
            out byte permissionLevel);
    }

    public sealed class NativeType2StdItemScriptBindingResult
    {
        private NativeType2StdItemScriptBindingResult(
            NativeType2StdItemScriptBindingStatus status,
            string attemptedPath, string errorText, object binding)
        {
            Status = status;
            AttemptedPath = attemptedPath ?? string.Empty;
            ErrorText = errorText ?? string.Empty;
            Binding = binding;
        }

        public NativeType2StdItemScriptBindingStatus Status { get; }
        public string AttemptedPath { get; }
        public string ErrorText { get; }
        public object Binding { get; }

        public static NativeType2StdItemScriptBindingResult FileNotFound(
            string attemptedPath) => new(
            NativeType2StdItemScriptBindingStatus.FileNotFound,
            attemptedPath, string.Empty, null);

        public static NativeType2StdItemScriptBindingResult Bound(
            string attemptedPath, object binding) => new(
            NativeType2StdItemScriptBindingStatus.Bound,
            attemptedPath, string.Empty, binding);

        public static NativeType2StdItemScriptBindingResult CompileFailed(
            string attemptedPath, string errorText) => new(
            NativeType2StdItemScriptBindingStatus.CompileFailed,
            attemptedPath, errorText, null);

        internal static NativeType2StdItemScriptBindingResult Unavailable() =>
            new(NativeType2StdItemScriptBindingStatus.BinderUnavailable,
                string.Empty, string.Empty, null);
    }

    public sealed class NativeType2StdItemExtensionSlot
    {
        internal NativeType2StdItemExtensionSlot(ushort ident, ushort value)
        {
            Ident = ident;
            Value = value;
        }

        public ushort Ident { get; }
        public ushort Value { get; }
    }

    /// <summary>
    /// Immutable representation of the complete native 0x134-byte 00CA body.
    /// Fields that the current GoodItem cannot hold remain available here.
    /// </summary>
    public sealed class NativeType2StdItemDefinition
    {
        public const int NameCapacity = 15;
        public const int ItemExtAbilCapacity = 200;

        private static readonly Encoding Gbk = CreateGbk();
        private static readonly byte[] ScriptPrefix =
            Encoding.ASCII.GetBytes("PsItemScript\\");
        private static readonly byte[] ScriptSuffix =
            Encoding.ASCII.GetBytes(".pas");

        private readonly byte[] _wireBody;
        private readonly byte[] _nameBytes;
        private readonly byte[] _itemExtAbilBytes;
        private readonly byte[] _scriptRelativePathBytes;

        internal NativeType2StdItemDefinition(ReadOnlySpan<byte> body)
        {
            _wireBody = body.Slice(0,
                NativeType2StdItemRuntimeProtocol.BodySize).ToArray();

            var nameLength = Math.Min((int)_wireBody[0x04], NameCapacity);
            _nameBytes = _wireBody.AsSpan(0x05, nameLength).ToArray();
            Name = Gbk.GetString(_nameBytes);

            var extensionLength = Math.Min((int)_wireBody[0x5C],
                ItemExtAbilCapacity);
            _itemExtAbilBytes = _wireBody.AsSpan(
                0x5D, extensionLength).ToArray();

            _scriptRelativePathBytes = new byte[
                ScriptPrefix.Length + _nameBytes.Length +
                ScriptSuffix.Length];
            ScriptPrefix.CopyTo(_scriptRelativePathBytes, 0);
            _nameBytes.CopyTo(_scriptRelativePathBytes,
                ScriptPrefix.Length);
            ScriptSuffix.CopyTo(_scriptRelativePathBytes,
                ScriptPrefix.Length + _nameBytes.Length);
            ScriptRelativePath = Gbk.GetString(_scriptRelativePathBytes);
        }

        public ushort WireIndex => ReadUInt16(0x00);
        public ushort Reserved02 => ReadUInt16(0x02);
        public string Name { get; }
        public byte StdMode => _wireBody[0x14];
        public byte Shape => _wireBody[0x15];
        public byte Need => _wireBody[0x16];
        public byte Source => _wireBody[0x17];
        public ushort Looks => ReadUInt16(0x18);
        public ushort Weight => ReadUInt16(0x1A);
        public ushort DuraMax => ReadUInt16(0x1C);
        public ushort AniCount => ReadUInt16(0x1E);
        public ushort NeedConf => ReadUInt16(0x20);
        public ushort NeedLevel => ReadUInt16(0x22);
        public ushort Ac => ReadUInt16(0x24);
        public ushort MaxAc => ReadUInt16(0x26);
        public ushort Mac => ReadUInt16(0x28);
        public ushort MaxMac => ReadUInt16(0x2A);
        public ushort Dc => ReadUInt16(0x2C);
        public ushort MaxDc => ReadUInt16(0x2E);
        public ushort Mc => ReadUInt16(0x30);
        public ushort MaxMc => ReadUInt16(0x32);
        public ushort Sc => ReadUInt16(0x34);
        public ushort MaxSc => ReadUInt16(0x36);
        public ushort Cc => ReadUInt16(0x38);
        public ushort MaxCc => ReadUInt16(0x3A);
        public int Price => ReadInt32(0x3C);
        public byte OutLookByte => _wireBody[0x40];
        public byte AntiqueLevel => _wireBody[0x41];
        public ushort ItemScore => ReadUInt16(0x42);
        public ushort SuitEquipType => ReadUInt16(0x44);
        public ushort BaseEffectId => ReadUInt16(0x46);
        public ushort WordParam1 => ReadUInt16(0x48);
        public ushort WordParam2 => ReadUInt16(0x4A);
        public int IntParam1 => ReadInt32(0x4C);
        public int IntParam2 => ReadInt32(0x50);
        public int IntParam3 => ReadInt32(0x54);
        public ushort MaxSteelLevel => ReadUInt16(0x58);
        public ushort MaxVeinsLevel => ReadUInt16(0x5A);
        public ushort OutLookWord => ReadUInt16(0x126);
        public byte NeedJob => _wireBody[0x128];
        public int ItemLevel => ReadInt32(0x12C);
        public ushort ItemConf => ReadUInt16(0x130);
        public string ScriptRelativePath { get; }

        public byte[] CopyWireBody() => (byte[])_wireBody.Clone();
        public byte[] CopyNameBytes() => (byte[])_nameBytes.Clone();
        public byte[] CopyItemExtAbilBytes() =>
            (byte[])_itemExtAbilBytes.Clone();
        public byte[] CopyScriptRelativePathBytes() =>
            (byte[])_scriptRelativePathBytes.Clone();

        internal NativeType2StdItemExtensionParseResult ParseExtensions()
        {
            var raw = new NativeType2StdItemRawRecord(_wireBody);
            var slotBytes = raw.CopyExtensionSlots();
            var slots = new NativeType2StdItemExtensionSlot[6];
            for (var index = 0; index < slots.Length; index++)
            {
                var offset = index * 4;
                slots[index] = new NativeType2StdItemExtensionSlot(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        slotBytes.AsSpan(offset, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        slotBytes.AsSpan(offset + 2, 2)));
            }
            return new NativeType2StdItemExtensionParseResult(
                raw.ItemExtAbilParsed, slots);
        }

        internal static string DecodeGbk(ReadOnlySpan<byte> value) =>
            Gbk.GetString(value);

        private ushort ReadUInt16(int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(
                _wireBody.AsSpan(offset, 2));

        private int ReadInt32(int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(
                _wireBody.AsSpan(offset, 4));

        private static Encoding CreateGbk()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936);
        }
    }

    internal sealed class NativeType2StdItemExtensionParseResult
    {
        public NativeType2StdItemExtensionParseResult(bool parsed,
            NativeType2StdItemExtensionSlot[] slots)
        {
            Parsed = parsed;
            Slots = slots;
        }

        public bool Parsed { get; }
        public NativeType2StdItemExtensionSlot[] Slots { get; }
    }

    public sealed class NativeType2StdItemRuntimeNotification
    {
        internal NativeType2StdItemRuntimeNotification(ushort word2,
            int correlation, int param2,
            NativeType2StdItemDefinition definition)
        {
            Word2 = word2;
            Correlation = correlation;
            Param2 = param2;
            Definition = definition;
        }

        public ushort Word2 { get; }
        public int Correlation { get; }
        public int Param2 { get; }
        public NativeType2StdItemDefinition Definition { get; }
    }

    public static class NativeType2StdItemRuntimeDecoder
    {
        public static NativeType2StdItemRuntimeDecodeResult TryDecode(
            ReadOnlySpan<byte> payload,
            out NativeType2StdItemRuntimeNotification notification)
        {
            notification = null;
            if (payload.Length < NativeType2StdItemRuntimeProtocol.HeaderSize)
                return NativeType2StdItemRuntimeDecodeResult.Ignored;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) !=
                NativeType2StdItemRuntimeProtocol.Command)
                return NativeType2StdItemRuntimeDecodeResult.Ignored;
            if (payload.Length < NativeType2StdItemRuntimeProtocol.PacketSize)
                return NativeType2StdItemRuntimeDecodeResult.PayloadTooShort;

            notification = new NativeType2StdItemRuntimeNotification(
                BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.Slice(2, 2)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(8, 4)),
                new NativeType2StdItemDefinition(payload.Slice(
                    NativeType2StdItemRuntimeProtocol.HeaderSize,
                    NativeType2StdItemRuntimeProtocol.BodySize)));
            return NativeType2StdItemRuntimeDecodeResult.Decoded;
        }
    }

    public sealed class NativeType2StdItemRuntimeEntry
    {
        private NativeType2StdItemScriptBindingResult _scriptBinding;

        internal NativeType2StdItemRuntimeEntry(int catalogIndex,
            NativeType2StdItemDefinition definition,
            NativeType2StdItemNeedIdentifyStatus needIdentifyStatus,
            byte needIdentify,
            NativeType2StdItemExtensionParseResult extensionParse)
        {
            CatalogIndex = catalogIndex;
            Definition = definition;
            NeedIdentifyStatus = needIdentifyStatus;
            NeedIdentify = needIdentify;
            ItemExtAbilParsed = extensionParse.Parsed;
            ExtensionSlots = Array.AsReadOnly(extensionParse.Slots);
            _scriptBinding =
                NativeType2StdItemScriptBindingResult.Unavailable();
        }

        public int CatalogIndex { get; }
        public NativeType2StdItemDefinition Definition { get; }
        public NativeType2StdItemNeedIdentifyStatus NeedIdentifyStatus { get; }
        public byte NeedIdentify { get; }
        public bool ItemExtAbilParsed { get; }
        public IReadOnlyList<NativeType2StdItemExtensionSlot>
            ExtensionSlots { get; }
        public NativeType2StdItemScriptBindingResult ScriptBinding =>
            Volatile.Read(ref _scriptBinding);

        internal void SetScriptBinding(
            NativeType2StdItemScriptBindingResult result) =>
            Volatile.Write(ref _scriptBinding, result);
    }

    public sealed class NativeType2StdItemRuntimeCatalog
    {
        private readonly object _sync = new();
        private readonly List<NativeType2StdItemRuntimeEntry> _entries = new();
        private readonly Dictionary<string, int> _firstIndexByName =
            new(StringComparer.OrdinalIgnoreCase);

        public int Count
        {
            get { lock (_sync) return _entries.Count; }
        }

        public IReadOnlyList<NativeType2StdItemRuntimeEntry> Entries
        {
            get
            {
                lock (_sync)
                    return Array.AsReadOnly(_entries.ToArray());
            }
        }

        public bool TryGetFirstByName(string name,
            out NativeType2StdItemRuntimeEntry entry)
        {
            entry = null;
            if (name == null) return false;
            lock (_sync)
            {
                if (!_firstIndexByName.TryGetValue(name, out var index))
                    return false;
                entry = _entries[index];
                return true;
            }
        }

        public bool TryGetFirstByNameBytes(ReadOnlySpan<byte> nameBytes,
            out NativeType2StdItemRuntimeEntry entry) =>
            TryGetFirstByName(
                NativeType2StdItemDefinition.DecodeGbk(nameBytes), out entry);

        internal bool TryAppend(NativeType2StdItemDefinition definition,
            NativeType2StdItemNeedIdentifyStatus needIdentifyStatus,
            byte needIdentify,
            NativeType2StdItemExtensionParseResult extensionParse,
            out NativeType2StdItemRuntimeEntry entry,
            out int expectedIndex)
        {
            lock (_sync)
            {
                expectedIndex = _entries.Count;
                if (definition.WireIndex != expectedIndex)
                {
                    entry = null;
                    return false;
                }

                entry = new NativeType2StdItemRuntimeEntry(expectedIndex,
                    definition, needIdentifyStatus, needIdentify,
                    extensionParse);
                _entries.Add(entry);
                if (!_firstIndexByName.ContainsKey(definition.Name))
                    _firstIndexByName.Add(definition.Name, expectedIndex);
                return true;
            }
        }
    }

    public sealed class NativeType2StdItemCorrelationDecision
    {
        internal NativeType2StdItemCorrelationDecision(
            NativeType2StdItemCorrelationStatus status,
            int correlation, byte permissionLevel, string prompt)
        {
            Status = status;
            Correlation = correlation;
            PermissionLevel = permissionLevel;
            Prompt = prompt;
        }

        public NativeType2StdItemCorrelationStatus Status { get; }
        public int Correlation { get; }
        public byte PermissionLevel { get; }
        public string Prompt { get; }
    }

    public sealed class NativeType2StdItemAppendResult
    {
        internal NativeType2StdItemAppendResult(
            NativeType2StdItemAppendStatus status,
            NativeType2StdItemRuntimeNotification notification,
            NativeType2StdItemRuntimeEntry entry, int expectedIndex,
            IReadOnlyList<string> logs,
            NativeType2StdItemCorrelationDecision correlationDecision)
        {
            Status = status;
            Notification = notification;
            Entry = entry;
            ExpectedIndex = expectedIndex;
            Logs = logs;
            CorrelationDecision = correlationDecision;
        }

        public NativeType2StdItemAppendStatus Status { get; }
        public NativeType2StdItemRuntimeNotification Notification { get; }
        public NativeType2StdItemRuntimeEntry Entry { get; }
        public int ExpectedIndex { get; }
        public IReadOnlyList<string> Logs { get; }
        public NativeType2StdItemCorrelationDecision CorrelationDecision
            { get; }
    }

    public static class NativeType2StdItemRuntimeProtocol
    {
        public const ushort Command = 0x00CA;
        public const int HeaderSize = 12;
        public const int BodySize = 0x134;
        public const int PacketSize = HeaderSize + BodySize;
        public const byte MinimumPromptPermission = 4;

        public const string RuntimeAddPrefix = "运行期添加道具:";
        public const string RuntimeSuccessPrefix = "运行期成功添加道具:";
        public const string SequenceError =
            "[Error]: 致命错误: StdItem.DB 数据出错";
        public const string ExtensionErrorPrefix =
            "[error]: 错误的道具属性：";
        public const string ScriptFatalPrefix = "[ERROR]: 致命错误 ";
        public const string ScriptErrorPrefix = "物品脚本错误:";
    }

    /// <summary>
    /// Isolated 00CA append transaction. It never writes UserEngine.StdItemList
    /// or sends a player message; those effects are returned as decisions for
    /// the later reviewed routing phase.
    /// </summary>
    public sealed class NativeType2StdItemAppendTransaction
    {
        private static readonly ReadOnlyCollection<string> EmptyLogs =
            Array.AsReadOnly(Array.Empty<string>());

        private readonly NativeType2StdItemRuntimeCatalog _catalog;
        private readonly INativeType2StdItemNeedIdentifyResolver
            _needIdentifyResolver;
        private readonly INativeType2StdItemScriptBinder _scriptBinder;
        private readonly INativeType2StdItemCorrelationResolver
            _correlationResolver;

        public NativeType2StdItemAppendTransaction(
            NativeType2StdItemRuntimeCatalog catalog,
            INativeType2StdItemNeedIdentifyResolver needIdentifyResolver = null,
            INativeType2StdItemScriptBinder scriptBinder = null,
            INativeType2StdItemCorrelationResolver correlationResolver = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(
                nameof(catalog));
            _needIdentifyResolver = needIdentifyResolver;
            _scriptBinder = scriptBinder;
            _correlationResolver = correlationResolver;
        }

        public NativeType2StdItemAppendResult Apply(
            ReadOnlySpan<byte> payload)
        {
            var decoded = NativeType2StdItemRuntimeDecoder.TryDecode(
                payload, out var notification);
            if (decoded != NativeType2StdItemRuntimeDecodeResult.Decoded)
            {
                return new NativeType2StdItemAppendResult(
                    decoded == NativeType2StdItemRuntimeDecodeResult
                        .PayloadTooShort
                        ? NativeType2StdItemAppendStatus.PayloadTooShort
                        : NativeType2StdItemAppendStatus.Ignored,
                    null, null, _catalog.Count, EmptyLogs,
                    CorrelationNotRequested());
            }

            var definition = notification.Definition;
            var logs = new List<string>();
            var expectedIndex = _catalog.Count;
            NativeType2StdItemRuntimeEntry entry = null;
            NativeType2StdItemAppendStatus status;

            if (definition.WireIndex != expectedIndex)
            {
                status = NativeType2StdItemAppendStatus.SequenceRejected;
                logs.Add(NativeType2StdItemRuntimeProtocol.SequenceError);
            }
            else
            {
                ResolveNeedIdentify(definition,
                    out var needIdentifyStatus, out var needIdentify);

                // Parsing occurs only after idx==Count, matching sub_7512B4.
                // Numeric conversion failures intentionally escape and leave
                // the isolated catalog unchanged.
                var extensionParse = definition.ParseExtensions();
                if (!_catalog.TryAppend(definition, needIdentifyStatus,
                        needIdentify, extensionParse, out entry,
                        out expectedIndex))
                {
                    status = NativeType2StdItemAppendStatus.SequenceRejected;
                    logs.Add(NativeType2StdItemRuntimeProtocol.SequenceError);
                }
                else
                {
                    status = extensionParse.Parsed
                        ? NativeType2StdItemAppendStatus.Appended
                        : NativeType2StdItemAppendStatus
                            .AppendedWithExtensionError;
                    if (!extensionParse.Parsed)
                    {
                        logs.Add(
                            NativeType2StdItemRuntimeProtocol
                                .ExtensionErrorPrefix +
                            definition.Name + ": " +
                            NativeType2StdItemDefinition.DecodeGbk(
                                definition.CopyItemExtAbilBytes()));
                    }

                    BindScript(definition, entry, logs);
                }
            }

            logs.Add(NativeType2StdItemRuntimeProtocol.RuntimeAddPrefix +
                     definition.Name);
            var correlationDecision = ResolveCorrelation(notification);
            return new NativeType2StdItemAppendResult(status, notification,
                entry, expectedIndex, Array.AsReadOnly(logs.ToArray()),
                correlationDecision);
        }

        private void ResolveNeedIdentify(
            NativeType2StdItemDefinition definition,
            out NativeType2StdItemNeedIdentifyStatus status,
            out byte needIdentify)
        {
            needIdentify = 0;
            if (_needIdentifyResolver == null)
            {
                status = NativeType2StdItemNeedIdentifyStatus
                    .ResolverUnavailable;
                return;
            }

            status = _needIdentifyResolver.TryResolve(
                definition.CopyNameBytes(), out needIdentify)
                ? NativeType2StdItemNeedIdentifyStatus.Resolved
                : NativeType2StdItemNeedIdentifyStatus.NotMatched;
            if (status == NativeType2StdItemNeedIdentifyStatus.NotMatched)
                needIdentify = 0;
        }

        private void BindScript(NativeType2StdItemDefinition definition,
            NativeType2StdItemRuntimeEntry entry, List<string> logs)
        {
            if (_scriptBinder == null) return;

            NativeType2StdItemScriptBindingResult result;
            try
            {
                result = _scriptBinder.Bind(definition,
                    definition.CopyScriptRelativePathBytes())
                    ?? throw new InvalidOperationException(
                        "native item script binder returned null");
            }
            catch (Exception exception)
            {
                result = NativeType2StdItemScriptBindingResult.CompileFailed(
                    definition.ScriptRelativePath, exception.Message);
            }

            entry.SetScriptBinding(result);
            if (result.Status ==
                NativeType2StdItemScriptBindingStatus.CompileFailed)
            {
                logs.Add(
                    NativeType2StdItemRuntimeProtocol.ScriptFatalPrefix +
                    result.AttemptedPath +
                    NativeType2StdItemRuntimeProtocol.ScriptErrorPrefix +
                    result.ErrorText);
            }
        }

        private NativeType2StdItemCorrelationDecision ResolveCorrelation(
            NativeType2StdItemRuntimeNotification notification)
        {
            var correlation = notification.Correlation;
            if (correlation == 0) return CorrelationNotRequested();
            if (_correlationResolver == null)
            {
                return new NativeType2StdItemCorrelationDecision(
                    NativeType2StdItemCorrelationStatus.ResolverUnavailable,
                    correlation, 0, string.Empty);
            }
            if (!_correlationResolver.TryResolvePermission(correlation,
                    out var permission))
            {
                return new NativeType2StdItemCorrelationDecision(
                    NativeType2StdItemCorrelationStatus.TargetNotFound,
                    correlation, 0, string.Empty);
            }
            if (permission <
                NativeType2StdItemRuntimeProtocol.MinimumPromptPermission)
            {
                return new NativeType2StdItemCorrelationDecision(
                    NativeType2StdItemCorrelationStatus
                        .InsufficientPermission,
                    correlation, permission, string.Empty);
            }

            return new NativeType2StdItemCorrelationDecision(
                NativeType2StdItemCorrelationStatus.PromptEligible,
                correlation, permission,
                NativeType2StdItemRuntimeProtocol.RuntimeSuccessPrefix +
                notification.Definition.Name);
        }

        private static NativeType2StdItemCorrelationDecision
            CorrelationNotRequested() => new(
            NativeType2StdItemCorrelationStatus.NotRequested,
            0, 0, string.Empty);
    }
}
