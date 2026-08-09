using System.Buffers.Binary;
using System.Collections.ObjectModel;
using SystemModule;

namespace GameSvr.Services
{
    public sealed class NativeType2StdItemRuntimePublishResult
    {
        internal NativeType2StdItemRuntimePublishResult(
            NativeType2StdItemAppendStatus status,
            NativeType2StdItemRuntimeNotification notification,
            GoodItem item, int expectedIndex, bool itemExtAbilParsed,
            NativeType2StdItemScriptBindingResult scriptBinding,
            IReadOnlyList<string> logs,
            NativeType2StdItemCorrelationDecision correlationDecision)
        {
            Status = status;
            Notification = notification;
            Item = item;
            ExpectedIndex = expectedIndex;
            ItemExtAbilParsed = itemExtAbilParsed;
            ScriptBinding = scriptBinding;
            Logs = logs;
            CorrelationDecision = correlationDecision;
        }

        public NativeType2StdItemAppendStatus Status { get; }
        public NativeType2StdItemRuntimeNotification Notification { get; }
        public GoodItem Item { get; }
        public int ExpectedIndex { get; }
        public bool ItemExtAbilParsed { get; }
        public NativeType2StdItemScriptBindingResult ScriptBinding { get; }
        public IReadOnlyList<string> Logs { get; }
        public NativeType2StdItemCorrelationDecision CorrelationDecision
            { get; }
    }

    public static class NativeType2StdItemGoodItemMapper
    {
        public const ushort DrugSpellPropertyId = 32;
        public const ushort DrugHealthPropertyId = 33;
        public const ushort DrugJobPropertyId = 96;

        internal static GoodItem Map(NativeType2StdItemDefinition definition,
            byte needIdentify,
            NativeType2StdItemExtensionParseResult extensionParse)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (extensionParse == null)
                throw new ArgumentNullException(nameof(extensionParse));

            var item = new GoodItem
            {
                NativeWireIndex = definition.WireIndex,
                NativeReserved02 = definition.Reserved02,
                Name = definition.Name,
                StdMode = definition.StdMode,
                Shape = definition.Shape,
                Need = definition.Need,
                Source = definition.Source,
                Looks = definition.Looks,
                Weight = definition.Weight,
                DuraMax = definition.DuraMax,
                AniCount = definition.AniCount,
                Reserved = definition.NeedConf,
                NeedLevel = definition.NeedLevel,
                Ac = definition.Ac,
                Ac2 = definition.MaxAc,
                Mac = definition.Mac,
                Mac2 = definition.MaxMac,
                Dc = definition.Dc,
                Dc2 = definition.MaxDc,
                Mc = definition.Mc,
                Mc2 = definition.MaxMc,
                Sc = definition.Sc,
                Sc2 = definition.MaxSc,
                Cc = definition.Cc,
                Cc2 = definition.MaxCc,
                Price = definition.Price,
                Outlook = definition.OutLookByte,
                AntiqueLevel = definition.AntiqueLevel,
                ItemScore = definition.ItemScore,
                SuitEquipType = definition.SuitEquipType,
                BaseEffectId = definition.BaseEffectId,
                WordParam1 = definition.WordParam1,
                WordParam2 = definition.WordParam2,
                IntParam1 = definition.IntParam1,
                IntParam2 = definition.IntParam2,
                IntParam3 = definition.IntParam3,
                MaxSteelLevel = definition.MaxSteelLevel,
                MaxVeinsLevel = definition.MaxVeinsLevel,
                OutLookWord = definition.OutLookWord,
                NeedJob = definition.NeedJob,
                ItemLevel = definition.ItemLevel,
                ItemConf = definition.ItemConf,
                NeedIdentify = needIdentify,
                ItemType = Classify(definition.StdMode),
                NativeItemExtAbilParsed = extensionParse.Parsed,
                NativeStdItemWireBody = definition.CopyWireBody()
            };

            for (var index = 0; index < extensionParse.Slots.Length; index++)
            {
                var slot = extensionParse.Slots[index];
                item.NativeItemExtAbilIdents[index] = slot.Ident;
                item.NativeItemExtAbilValues[index] = slot.Value;
                switch (slot.Ident)
                {
                    case DrugHealthPropertyId:
                        item.NativeDrugHealthBonus = unchecked((ushort)(
                            item.NativeDrugHealthBonus + slot.Value));
                        break;
                    case DrugSpellPropertyId:
                        item.NativeDrugSpellBonus = unchecked((ushort)(
                            item.NativeDrugSpellBonus + slot.Value));
                        break;
                    case DrugJobPropertyId:
                        item.NativeDrugJobBonus = unchecked((ushort)(
                            item.NativeDrugJobBonus + slot.Value));
                        break;
                }
            }

            return item;
        }

        private static GoodType Classify(byte stdMode) => stdMode switch
        {
            0 or 55 or 58 => GoodType.ITEM_LEECHDOM,
            5 or 6 => GoodType.ITEM_WEAPON,
            10 or 11 => GoodType.ITEM_ARMOR,
            15 or 19 or 20 or 21 or 22 or 23 or 24 or 26 or 30 or 51 or 52
                or 53 or 54 or 62 or 63 or 64 => GoodType.ITEM_ACCESSORY,
            _ => GoodType.ITEM_ETC
        };
    }

    public sealed class NativeType2StdItemRuntimePublisher
    {
        private static readonly ReadOnlyCollection<string> EmptyLogs =
            Array.AsReadOnly(Array.Empty<string>());

        private readonly object _syncRoot;
        private readonly IList<GoodItem> _items;
        private readonly INativeType2StdItemNeedIdentifyResolver
            _needIdentifyResolver;
        private readonly INativeType2StdItemScriptBinder _scriptBinder;
        private readonly INativeType2StdItemCorrelationResolver
            _correlationResolver;

        public NativeType2StdItemRuntimePublisher(IList<GoodItem> items,
            object syncRoot = null,
            INativeType2StdItemNeedIdentifyResolver needIdentifyResolver = null,
            INativeType2StdItemScriptBinder scriptBinder = null,
            INativeType2StdItemCorrelationResolver correlationResolver = null)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _syncRoot = syncRoot ?? new object();
            _needIdentifyResolver = needIdentifyResolver;
            _scriptBinder = scriptBinder;
            _correlationResolver = correlationResolver;
        }

        public int Count
        {
            get { lock (_syncRoot) return _items.Count; }
        }

        public bool TryGetFirstByName(string name, out GoodItem item)
        {
            item = null;
            if (name == null) return false;
            lock (_syncRoot)
            {
                for (var index = 0; index < _items.Count; index++)
                {
                    var candidate = _items[index];
                    if (candidate != null && string.Equals(candidate.Name,
                            name, StringComparison.OrdinalIgnoreCase))
                    {
                        item = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        public NativeType2StdItemRuntimePublishResult Apply(
            ReadOnlySpan<byte> payload)
        {
            var decoded = NativeType2StdItemRuntimeDecoder.TryDecode(payload,
                out var notification);
            if (decoded != NativeType2StdItemRuntimeDecodeResult.Decoded)
            {
                return new NativeType2StdItemRuntimePublishResult(
                    decoded == NativeType2StdItemRuntimeDecodeResult
                        .PayloadTooShort
                        ? NativeType2StdItemAppendStatus.PayloadTooShort
                        : NativeType2StdItemAppendStatus.Ignored,
                    null, null, Count, false,
                    NativeType2StdItemScriptBindingResult.Unavailable(),
                    EmptyLogs, CorrelationNotRequested());
            }

            lock (_syncRoot)
            {
                var definition = notification.Definition;
                var expectedIndex = _items.Count;
                var logs = new List<string>();
                GoodItem item = null;
                var parsed = false;
                var scriptBinding =
                    NativeType2StdItemScriptBindingResult.Unavailable();
                NativeType2StdItemAppendStatus status;

                if (definition.WireIndex != expectedIndex)
                {
                    status = NativeType2StdItemAppendStatus.SequenceRejected;
                    logs.Add(NativeType2StdItemRuntimeProtocol.SequenceError);
                }
                else
                {
                    var needIdentify = ResolveNeedIdentify(definition);

                    // Delphi StrToInt failures escape before the native list add.
                    var extensions = definition.ParseExtensions();
                    parsed = extensions.Parsed;
                    item = NativeType2StdItemGoodItemMapper.Map(definition,
                        needIdentify, extensions);
                    _items.Add(item);

                    status = parsed
                        ? NativeType2StdItemAppendStatus.Appended
                        : NativeType2StdItemAppendStatus
                            .AppendedWithExtensionError;
                    if (!parsed)
                    {
                        logs.Add(NativeType2StdItemRuntimeProtocol
                                     .ExtensionErrorPrefix
                                 + definition.Name + ": "
                                 + NativeType2StdItemDefinition.DecodeGbk(
                                     definition.CopyItemExtAbilBytes()));
                    }

                    scriptBinding = BindScript(definition, logs);
                    if (scriptBinding.Status ==
                        NativeType2StdItemScriptBindingStatus.Bound)
                        item.NativeItemScriptPath =
                            scriptBinding.AttemptedPath;
                }

                logs.Add(NativeType2StdItemRuntimeProtocol.RuntimeAddPrefix
                         + definition.Name);
                return new NativeType2StdItemRuntimePublishResult(status,
                    notification, item, expectedIndex, parsed, scriptBinding,
                    Array.AsReadOnly(logs.ToArray()),
                    ResolveCorrelation(notification));
            }
        }

        private byte ResolveNeedIdentify(
            NativeType2StdItemDefinition definition)
        {
            if (_needIdentifyResolver == null) return 0;
            return _needIdentifyResolver.TryResolve(
                definition.CopyNameBytes(), out var needIdentify)
                ? needIdentify
                : (byte)0;
        }

        private NativeType2StdItemScriptBindingResult BindScript(
            NativeType2StdItemDefinition definition, List<string> logs)
        {
            if (_scriptBinder == null)
                return NativeType2StdItemScriptBindingResult.Unavailable();

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

            if (result.Status ==
                NativeType2StdItemScriptBindingStatus.CompileFailed)
            {
                logs.Add(NativeType2StdItemRuntimeProtocol.ScriptFatalPrefix
                         + result.AttemptedPath
                         + NativeType2StdItemRuntimeProtocol.ScriptErrorPrefix
                         + result.ErrorText);
            }
            return result;
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
                NativeType2StdItemRuntimeProtocol.RuntimeSuccessPrefix
                + notification.Definition.Name);
        }

        private static NativeType2StdItemCorrelationDecision
            CorrelationNotRequested() => new(
            NativeType2StdItemCorrelationStatus.NotRequested,
            0, 0, string.Empty);
    }

    public static class NativeType2StdItemRuntimeProduction
    {
        private static readonly object PublisherLock = new();
        private static IList<GoodItem> _boundItems;
        private static NativeType2StdItemRuntimePublisher _publisher;

        public static bool TryConsume(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < sizeof(ushort)
                || BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != NativeType2StdItemRuntimeProtocol.Command)
                return false;

            var userEngine = M2Share.UserEngine;
            if (userEngine?.StdItemList == null)
            {
                M2Share.ErrorMessage(
                    "[RunDB] 00CA到达时StdItemList尚未初始化");
                return true;
            }

            NativeType2StdItemRuntimePublisher publisher;
            lock (PublisherLock)
            {
                if (_publisher == null
                    || !ReferenceEquals(_boundItems,
                        userEngine.StdItemList))
                {
                    _boundItems = userEngine.StdItemList;
                    _publisher = new NativeType2StdItemRuntimePublisher(
                        _boundItems,
                        M2Share.ProcessHumanCriticalSection,
                        new NativeType2StdItemProductionNeedIdentifyResolver(),
                        M2Share.PasEngine == null
                            ? null
                            : new NativeType2StdItemProductionScriptBinder(
                                M2Share.PasEngine),
                        new RuntimeCorrelationResolver());
                }
                publisher = _publisher;
            }

            var result = publisher.Apply(payload);

            for (var index = 0; index < result.Logs.Count; index++)
                M2Share.MainOutMessage(result.Logs[index]);

            var decision = result.CorrelationDecision;
            if (decision.Status ==
                NativeType2StdItemCorrelationStatus.PromptEligible
                && M2Share.ObjectManager?.Get(decision.Correlation)
                    is TPlayObject player
                && player.m_btPermission >=
                    NativeType2StdItemRuntimeProtocol.MinimumPromptPermission)
            {
                player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                    0xFF, 0xFC, 0, decision.Prompt);
            }
            return true;
        }

        private sealed class RuntimeCorrelationResolver :
            INativeType2StdItemCorrelationResolver
        {
            public bool TryResolvePermission(int correlation,
                out byte permissionLevel)
            {
                if (M2Share.ObjectManager?.Get(correlation)
                    is TPlayObject player)
                {
                    permissionLevel = player.m_btPermission;
                    return true;
                }
                permissionLevel = 0;
                return false;
            }
        }
    }
}
