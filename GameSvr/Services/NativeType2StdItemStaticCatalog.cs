using System.Collections.ObjectModel;

namespace GameSvr.Services
{
    /// <summary>
    /// A complete, off-list projection of the native startup Type2 0x0068
    /// stream. Script failures are retained beside the item because original
    /// M2 appends the item before compiling its optional item script.
    /// </summary>
    public sealed class NativeType2StdItemStaticBuildResult
    {
        internal NativeType2StdItemStaticBuildResult(GoodItem[] items,
            NativeType2StdItemDefinition[] definitions,
            NativeType2StdItemScriptBindingResult[] scriptBindings,
            string[] logs)
        {
            Items = Array.AsReadOnly(items);
            Definitions = Array.AsReadOnly(definitions);
            ScriptBindings = Array.AsReadOnly(scriptBindings);
            Logs = Array.AsReadOnly(logs);
        }

        public ReadOnlyCollection<GoodItem> Items { get; }
        public ReadOnlyCollection<NativeType2StdItemDefinition> Definitions
            { get; }
        public ReadOnlyCollection<NativeType2StdItemScriptBindingResult>
            ScriptBindings { get; }
        public ReadOnlyCollection<string> Logs { get; }

        public IList<GoodItem> CreateGoodItemList() =>
            new List<GoodItem>(Items);
    }

    public static class NativeType2StdItemStaticBuilder
    {
        public static NativeType2StdItemStaticBuildResult BuildGoodItemList(
            NativeType2StdItemSnapshotState snapshot,
            IReadOnlyList<GoodItem> seed,
            INativeType2StdItemNeedIdentifyResolver needIdentifyResolver = null,
            INativeType2StdItemScriptBinder scriptBinder = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (seed == null)
                throw new ArgumentNullException(nameof(seed));
            if (!snapshot.Completed)
                throw new InvalidDataException(
                    "Native standard-item snapshot is not complete.");
            if (seed.Count != snapshot.InitialNativeListCount)
            {
                throw new InvalidDataException(
                    "Native standard-item seed count does not match the " +
                    "receiver startup baseline.");
            }

            var items = new GoodItem[seed.Count + snapshot.Records.Count];
            for (var index = 0; index < seed.Count; index++)
            {
                items[index] = seed[index] ?? throw new InvalidDataException(
                    "Native standard-item seed contains a null entry.");
            }

            var definitions = new NativeType2StdItemDefinition[
                snapshot.Records.Count];
            var bindings = new NativeType2StdItemScriptBindingResult[
                snapshot.Records.Count];
            var logs = new List<string>();

            for (var recordIndex = 0;
                 recordIndex < snapshot.Records.Count;
                 recordIndex++)
            {
                var definition = new NativeType2StdItemDefinition(
                    snapshot.Records[recordIndex].CopyWireBody());
                var expectedIndex = seed.Count + recordIndex;
                if (definition.WireIndex != expectedIndex)
                {
                    throw new InvalidDataException(
                        "Native standard-item snapshot index sequence is " +
                        "not contiguous.");
                }

                var needIdentify = ResolveNeedIdentify(definition,
                    needIdentifyResolver);
                var extensions = definition.ParseExtensions();
                var item = NativeType2StdItemGoodItemMapper.Map(definition,
                    needIdentify, extensions);

                definitions[recordIndex] = definition;
                items[expectedIndex] = item;

                if (!extensions.Parsed)
                {
                    logs.Add(NativeType2StdItemRuntimeProtocol
                                 .ExtensionErrorPrefix
                             + definition.Name + ": "
                             + NativeType2StdItemDefinition.DecodeGbk(
                                 definition.CopyItemExtAbilBytes()));
                }

                var binding = BindScript(definition, scriptBinder, logs);
                bindings[recordIndex] = binding;
                if (binding.Status ==
                    NativeType2StdItemScriptBindingStatus.Bound)
                    item.NativeItemScriptPath = binding.AttemptedPath;
            }

            return new NativeType2StdItemStaticBuildResult(items,
                definitions, bindings, logs.ToArray());
        }

        private static byte ResolveNeedIdentify(
            NativeType2StdItemDefinition definition,
            INativeType2StdItemNeedIdentifyResolver resolver)
        {
            if (resolver == null) return 0;
            return resolver.TryResolve(definition.CopyNameBytes(),
                out var needIdentify) ? needIdentify : (byte)0;
        }

        private static NativeType2StdItemScriptBindingResult BindScript(
            NativeType2StdItemDefinition definition,
            INativeType2StdItemScriptBinder binder, List<string> logs)
        {
            if (binder == null)
                return NativeType2StdItemScriptBindingResult.Unavailable();

            NativeType2StdItemScriptBindingResult result;
            try
            {
                result = binder.Bind(definition,
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
    }

    internal sealed class NativeType2StdItemProductionNeedIdentifyResolver :
        INativeType2StdItemNeedIdentifyResolver
    {
        public bool TryResolve(ReadOnlyMemory<byte> nativeNameBytes,
            out byte needIdentify)
        {
            var name = NativeType2StdItemDefinition.DecodeGbk(
                nativeNameBytes.Span);
            needIdentify = M2Share.GetGameLogItemNameList(name);
            return needIdentify != 0;
        }
    }

    internal sealed class NativeType2StdItemProductionScriptBinder :
        INativeType2StdItemScriptBinder
    {
        private readonly PasEngine.PasScriptHost _host;

        public NativeType2StdItemProductionScriptBinder(
            PasEngine.PasScriptHost host) =>
            _host = host ?? throw new ArgumentNullException(nameof(host));

        public NativeType2StdItemScriptBindingResult Bind(
            NativeType2StdItemDefinition definition,
            ReadOnlyMemory<byte> relativePathBytes)
        {
            var path = _host.FindItemScriptFile(definition.Name);
            if (path == null)
            {
                return NativeType2StdItemScriptBindingResult.FileNotFound(
                    definition.ScriptRelativePath);
            }
            if (!_host.TryPreloadItemScript(definition.Name,
                    out var loadedPath, out var error))
            {
                return NativeType2StdItemScriptBindingResult.CompileFailed(
                    path, error);
            }
            return NativeType2StdItemScriptBindingResult.Bound(
                loadedPath, loadedPath);
        }
    }

    /// <summary>
    /// Atomically publishes the complete startup standard-item table. The
    /// verified original process owns a 140-byte zeroed index-0 entry whose
    /// only populated ShortString is GBK 04 BD F0 B1 D2 ("金币").
    /// </summary>
    public sealed class NativeType2StdItemStaticCatalog
    {
        public const int VerifiedGoldSentinelNativeSize = 140;
        public const int VerifiedGoldSentinelNameOffset = 4;

        private static readonly byte[] GoldSentinelNameShortString =
            { 0x04, 0xBD, 0xF0, 0xB1, 0xD2 };
        private static readonly byte[] GoldSentinelNativeImage =
            CreateGoldSentinelNativeImage();

        private sealed class Publication
        {
            public static readonly Publication Empty = new(null);

            public Publication(NativeType2StdItemStaticBuildResult result) =>
                Result = result;

            public NativeType2StdItemStaticBuildResult Result { get; }
        }

        private readonly object _publishLock = new();
        private Publication _publication = Publication.Empty;

        public bool Ready => Volatile.Read(ref _publication).Result != null;

        public int Count =>
            Volatile.Read(ref _publication).Result?.Items.Count ?? 0;

        public IReadOnlyList<GoodItem> Items
        {
            get
            {
                var result = Volatile.Read(ref _publication).Result;
                return result == null
                    ? Array.Empty<GoodItem>() : result.Items;
            }
        }

        public IReadOnlyList<NativeType2StdItemDefinition> Definitions
        {
            get
            {
                var result = Volatile.Read(ref _publication).Result;
                return result == null
                    ? Array.Empty<NativeType2StdItemDefinition>()
                    : result.Definitions;
            }
        }

        public IReadOnlyList<string> Logs
        {
            get
            {
                var result = Volatile.Read(ref _publication).Result;
                return result == null
                    ? Array.Empty<string>() : result.Logs;
            }
        }

        public void Publish(NativeType2StdItemSnapshotState snapshot,
            INativeType2StdItemNeedIdentifyResolver needIdentifyResolver = null,
            INativeType2StdItemScriptBinder scriptBinder = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.InitialNativeListCount !=
                NativeType2StdItemSnapshotState
                    .VerifiedOriginalStartupListCount)
            {
                throw new InvalidDataException(
                    "Native standard-item snapshot does not use the " +
                    "verified original startup baseline.");
            }

            lock (_publishLock)
            {
                if (Ready)
                    throw new InvalidOperationException(
                        "Native standard-item catalog is already published.");

                var seed = new[] { CreateVerifiedGoldSentinel() };
                var result = NativeType2StdItemStaticBuilder
                    .BuildGoodItemList(snapshot, seed,
                        needIdentifyResolver, scriptBinder);
                Interlocked.Exchange(ref _publication,
                    new Publication(result));
            }
        }

        public IList<GoodItem> CreateGoodItemList()
        {
            var result = Volatile.Read(ref _publication).Result;
            if (result == null)
                throw new InvalidOperationException(
                    "Native standard-item catalog is not published.");
            return result.CreateGoodItemList();
        }

        public GoodItem FindByName(string name)
        {
            if (name == null) return null;
            var items = Volatile.Read(ref _publication).Result?.Items;
            if (items == null) return null;
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item != null && string.Equals(item.Name, name,
                        StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        public static GoodItem CreateVerifiedGoldSentinel()
        {
            var length = GoldSentinelNameShortString[0];
            return new GoodItem
            {
                NativeWireIndex = 0,
                Name = NativeType2StdItemDefinition.DecodeGbk(
                    GoldSentinelNameShortString.AsSpan(1, length))
            };
        }

        public static byte[] CopyVerifiedGoldSentinelNameShortString() =>
            (byte[])GoldSentinelNameShortString.Clone();

        public static byte[] CopyVerifiedGoldSentinelNativeImage() =>
            (byte[])GoldSentinelNativeImage.Clone();

        private static byte[] CreateGoldSentinelNativeImage()
        {
            var image = new byte[VerifiedGoldSentinelNativeSize];
            GoldSentinelNameShortString.CopyTo(image,
                VerifiedGoldSentinelNameOffset);
            return image;
        }
    }
}
