using System.Collections.ObjectModel;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// One manager-owned equipment/drop-list entry. Actors borrow these
    /// bindings through a materialization handle; they never dispose them.
    /// </summary>
    public sealed class NativeType2FieldHeroRuntimeEquipmentBinding
    {
        internal NativeType2FieldHeroRuntimeEquipmentBinding(
            NativeType2FieldHeroEquipmentDefinition definition,
            GoodItem item)
        {
            Definition = definition;
            Item = item;
        }

        public NativeType2FieldHeroEquipmentDefinition Definition { get; }
        public GoodItem Item { get; }
        public bool IsEmpty => Definition.IsEmpty;
        public bool IsResolved => Item != null;
        public bool IsMissing => !IsEmpty && Item == null;
    }

    /// <summary>
    /// Keeps the publication generation alive while an actor borrows its
    /// manager-owned equipment/drop-list projection.
    /// </summary>
    public sealed class NativeType2FieldHeroMaterialization
    {
        private readonly NativeType2FieldHeroRuntimeDefinition _owner;

        internal NativeType2FieldHeroMaterialization(
            NativeType2FieldHeroRuntimeDefinition owner,
            ReadOnlyCollection<NativeType2FieldHeroRuntimeEquipmentBinding>
                equipment,
            ReadOnlyCollection<NativeFieldHeroRuntimeDropBinding> dropItems)
        {
            _owner = owner;
            Equipment = equipment;
            DropItems = dropItems;
        }

        public NativeType2FieldHeroDefinition Definition =>
            _owner.Definition;
        public long Generation => _owner.Generation;
        public IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>
            Equipment { get; }
        public IReadOnlyList<NativeFieldHeroRuntimeDropBinding> DropItems
            { get; }
    }

    /// <summary>
    /// Runtime handle for one immutable wire definition. Only the effective
    /// selector is mutable, matching native template+0x10 fame overrides.
    /// </summary>
    public sealed class NativeType2FieldHeroRuntimeDefinition
    {
        private readonly object _publicationOwner;
        private readonly ReadOnlyCollection<
            NativeType2FieldHeroRuntimeEquipmentBinding> _equipment;
        private readonly ReadOnlyCollection<NativeFieldHeroRuntimeDropBinding>
            _dropItems;
        private int _effectiveJob;

        internal NativeType2FieldHeroRuntimeDefinition(object publicationOwner,
            long generation, NativeType2FieldHeroDefinition definition,
            NativeType2FieldHeroRuntimeEquipmentBinding[] equipment,
            NativeFieldHeroRuntimeDropBinding[] dropItems)
        {
            _publicationOwner = publicationOwner;
            Generation = generation;
            Definition = definition;
            _equipment = Array.AsReadOnly(equipment);
            _dropItems = Array.AsReadOnly(dropItems);
            _effectiveJob = definition.Job;
        }

        public long Generation { get; }
        public NativeType2FieldHeroDefinition Definition { get; }
        public byte EffectiveJob => unchecked((byte)Volatile.Read(
            ref _effectiveJob));

        internal byte CaptureEffectiveJob(byte? fameJob)
        {
            if (!fameJob.HasValue) return EffectiveJob;
            Interlocked.Exchange(ref _effectiveJob, fameJob.Value);
            return fameJob.Value;
        }

        // Native equipment lookup/logging belongs to sub_60B154's slot loop.
        // Materialization only captures the immutable publication generation.
        internal NativeType2FieldHeroMaterialization MaterializeEquipment()
        {
            return new NativeType2FieldHeroMaterialization(this, _equipment,
                _dropItems);
        }
    }

    /// <summary>
    /// Captured selector plus a strong runtime handle. Constructor or map
    /// publication failure must not roll back the selector sidecar.
    /// </summary>
    public sealed class NativeType2FieldHeroSpawnSelection
    {
        private readonly NativeType2FieldHeroRuntimeDefinition _runtime;

        internal NativeType2FieldHeroSpawnSelection(
            NativeType2FieldHeroRuntimeDefinition runtime, byte effectiveJob)
        {
            _runtime = runtime;
            EffectiveJob = effectiveJob;
        }

        public NativeType2FieldHeroDefinition Definition =>
            _runtime.Definition;
        public long Generation => _runtime.Generation;
        public byte EffectiveJob { get; }

        public NativeType2FieldHeroMaterialization MaterializeEquipment() =>
            _runtime.MaterializeEquipment();
    }

    /// <summary>
    /// Side-effect-free template lookup result. Native sub_604E3C performs
    /// placement before its fame lookup and persistent template+0x10 write;
    /// callers must therefore capture the spawn selection only after placement
    /// succeeds.
    /// </summary>
    public sealed class NativeType2FieldHeroTemplateResolution
    {
        private readonly NativeType2FieldHeroRuntimeDefinition _runtime;

        internal NativeType2FieldHeroTemplateResolution(
            NativeType2FieldHeroRuntimeDefinition runtime)
        {
            _runtime = runtime;
        }

        public NativeType2FieldHeroDefinition Definition =>
            _runtime.Definition;
        public long Generation => _runtime.Generation;
        public byte CurrentEffectiveJob => _runtime.EffectiveJob;

        public NativeType2FieldHeroSpawnSelection
            CaptureSelectionAfterPlacement(byte? fameJob)
        {
            var effectiveJob = _runtime.CaptureEffectiveJob(fameJob);
            return new NativeType2FieldHeroSpawnSelection(_runtime,
                effectiveJob);
        }
    }

    /// <summary>
    /// Mutable runtime adapter over immutable Type2 FieldHero definitions.
    /// Normal production is one-shot Publish. Replace is an explicit audit and
    /// reload boundary. In-flight selections and actor materializations retain
    /// their own publication generation through their runtime owner.
    /// </summary>
    public sealed class NativeType2FieldHeroRuntimeCatalogAdapter
    {
        public const string MissingEquipmentLogPrefix =
            " [Error]: TFieldHero.FillDBData: ";
        public const string MissingEquipmentLogSuffix =
            "\u4E0D\u5B58\u5728\uFF01";

        private sealed class StandardItemBinding
        {
            public StandardItemBinding(byte[] nameBytes, GoodItem item)
            {
                NameBytes = nameBytes;
                Item = item;
            }

            public byte[] NameBytes { get; }
            public GoodItem Item { get; }
        }

        private sealed class DropStandardItemBinding
        {
            public DropStandardItemBinding(byte[] lookupNameBytes,
                GoodItem item)
            {
                LookupNameBytes = lookupNameBytes;
                Item = item;
            }

            public byte[] LookupNameBytes { get; }
            public GoodItem Item { get; }
        }

        private sealed class Publication
        {
            public static readonly Publication Empty = new();

            private Publication()
            {
                Ready = false;
                Entries = Array.AsReadOnly(
                    Array.Empty<NativeType2FieldHeroRuntimeDefinition>());
            }

            public Publication(long generation,
                IReadOnlyList<NativeType2FieldHeroDefinition> definitions,
                IReadOnlyList<NativeType2StdItemDefinition> itemDefinitions,
                IReadOnlyList<GoodItem> items,
                INativeFieldHeroMonItemsSource monItemsSource)
            {
                Ready = true;
                Generation = generation;

                var standardItems = BuildStandardItemBindings(
                    itemDefinitions, items);
                var dropStandardItems = BuildDropStandardItemBindings(
                    itemDefinitions, items);
                var entries = new NativeType2FieldHeroRuntimeDefinition[
                    definitions.Count];
                for (var index = 0; index < entries.Length; index++)
                {
                    var definition = definitions[index];
                    entries[index] = new NativeType2FieldHeroRuntimeDefinition(
                        this, generation, definition,
                        BuildEquipmentBindings(definition, standardItems),
                        BuildDropBindings(definition, monItemsSource,
                            dropStandardItems));
                }
                Entries = Array.AsReadOnly(entries);
            }

            public bool Ready { get; }
            public long Generation { get; }
            public ReadOnlyCollection<
                NativeType2FieldHeroRuntimeDefinition> Entries { get; }

            public NativeType2FieldHeroRuntimeDefinition FindByNameBytes(
                ReadOnlySpan<byte> nameBytes)
            {
                var lookupName = NativeFieldHeroFactoryPreflight
                    .CanonicalizeLookupName(nameBytes);
                // sub_49F128 inserts at the hash-bucket head, so a duplicate
                // key loaded later is the first one returned by sub_49F2E4.
                for (var index = Entries.Count - 1; index >= 0; index--)
                {
                    var entry = Entries[index];
                    if (entry.Definition.LookupNameBytesEqual(lookupName))
                        return entry;
                }
                return null;
            }

            private static StandardItemBinding[] BuildStandardItemBindings(
                IReadOnlyList<NativeType2StdItemDefinition> definitions,
                IReadOnlyList<GoodItem> items)
            {
                var bindings = new StandardItemBinding[definitions.Count];
                for (var index = 0; index < definitions.Count; index++)
                {
                    var definition = definitions[index];
                    var wireIndex = definition.WireIndex;
                    if (wireIndex >= items.Count
                        || items[wireIndex] == null
                        || items[wireIndex].NativeWireIndex != wireIndex)
                    {
                        throw new InvalidDataException(
                            "Native standard-item publication is internally " +
                            "inconsistent.");
                    }
                    bindings[index] = new StandardItemBinding(
                        definition.CopyNameBytes(), items[wireIndex]);
                }
                return bindings;
            }

            private static DropStandardItemBinding[]
                BuildDropStandardItemBindings(
                    IReadOnlyList<NativeType2StdItemDefinition> definitions,
                    IReadOnlyList<GoodItem> items)
            {
                var bindings = new List<DropStandardItemBinding>();
                if (items.Count > 0 && items[0] != null
                    && items[0].NativeWireIndex == 0)
                {
                    bindings.Add(new DropStandardItemBinding(
                        NativeFieldHeroFactoryPreflight
                            .CanonicalizeLookupName(
                                HUtil32.GbkEncoding.GetBytes(items[0].Name)),
                        items[0]));
                }

                for (var index = 0; index < definitions.Count; index++)
                {
                    var definition = definitions[index];
                    var wireIndex = definition.WireIndex;
                    if (wireIndex >= items.Count
                        || items[wireIndex] == null
                        || items[wireIndex].NativeWireIndex != wireIndex)
                    {
                        throw new InvalidDataException(
                            "Native standard-item publication is internally " +
                            "inconsistent.");
                    }
                    bindings.Add(new DropStandardItemBinding(
                        NativeFieldHeroFactoryPreflight
                            .CanonicalizeLookupName(
                                definition.CopyNameBytes()),
                        items[wireIndex]));
                }
                return bindings.ToArray();
            }

            private static NativeFieldHeroRuntimeDropBinding[]
                BuildDropBindings(
                    NativeType2FieldHeroDefinition definition,
                    INativeFieldHeroMonItemsSource monItemsSource,
                    IReadOnlyList<DropStandardItemBinding> standardItems)
            {
                var lines = monItemsSource.LoadLines(
                    definition.CopyNameBytes());
                if (lines == null)
                {
                    throw new InvalidDataException(
                        "Native FieldHero MonItems source returned null.");
                }

                return NativeFieldHeroMonItemsParser.Parse(lines, name =>
                {
                    var lookupName = NativeFieldHeroFactoryPreflight
                        .CanonicalizeLookupName(
                            HUtil32.GbkEncoding.GetBytes(name));
                    // The native hash inserts each later definition at the
                    // bucket head, so duplicate normalized names resolve to
                    // the last loaded standard item.
                    for (var index = standardItems.Count - 1;
                         index >= 0; index--)
                    {
                        var candidate = standardItems[index];
                        if (lookupName.AsSpan().SequenceEqual(
                                candidate.LookupNameBytes))
                            return candidate.Item;
                    }
                    return null;
                });
            }

            private static
                NativeType2FieldHeroRuntimeEquipmentBinding[]
                BuildEquipmentBindings(
                    NativeType2FieldHeroDefinition definition,
                    IReadOnlyList<StandardItemBinding> standardItems)
            {
                var bindings = new
                    NativeType2FieldHeroRuntimeEquipmentBinding[
                        definition.Equipment.Count];
                for (var slot = 0; slot < bindings.Length; slot++)
                {
                    var equipment = definition.Equipment[slot];
                    GoodItem item = null;
                    if (!equipment.IsEmpty)
                    {
                        for (var itemIndex = 0;
                             itemIndex < standardItems.Count;
                             itemIndex++)
                        {
                            var candidate = standardItems[itemIndex];
                            if (!equipment.NameBytesEqual(candidate.NameBytes))
                                continue;
                            item = candidate.Item;
                            break;
                        }
                    }
                    bindings[slot] =
                        new NativeType2FieldHeroRuntimeEquipmentBinding(
                            equipment, item);
                }
                return bindings;
            }
        }

        private readonly object _publishLock = new();
        private Publication _publication = Publication.Empty;
        private long _nextGeneration;

        public bool Ready => Volatile.Read(ref _publication).Ready;
        public int Count => Volatile.Read(ref _publication).Entries.Count;
        public long Generation => Volatile.Read(ref _publication).Generation;

        public void Publish(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems)
        {
            Publish(definitionCatalog, standardItems,
                NativeFieldHeroEmptyMonItemsSource.Instance);
        }

        public void Publish(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems,
            INativeFieldHeroMonItemsSource monItemsSource)
        {
            if (monItemsSource == null)
                throw new ArgumentNullException(nameof(monItemsSource));
            lock (_publishLock)
            {
                if (Volatile.Read(ref _publication).Ready)
                {
                    throw new InvalidOperationException(
                        "Native FieldHero runtime adapter is already " +
                        "published.");
                }
                PublishCore(definitionCatalog, standardItems,
                    monItemsSource);
            }
        }

        public void Replace(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems)
        {
            Replace(definitionCatalog, standardItems,
                NativeFieldHeroEmptyMonItemsSource.Instance);
        }

        public void Replace(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems,
            INativeFieldHeroMonItemsSource monItemsSource)
        {
            if (monItemsSource == null)
                throw new ArgumentNullException(nameof(monItemsSource));
            lock (_publishLock)
            {
                if (!Volatile.Read(ref _publication).Ready)
                {
                    throw new InvalidOperationException(
                        "Native FieldHero runtime adapter is not published.");
                }
                PublishCore(definitionCatalog, standardItems,
                    monItemsSource);
            }
        }

        public bool TryResolveTemplate(string name,
            out NativeType2FieldHeroTemplateResolution resolution)
        {
            if (name == null)
            {
                resolution = null;
                return false;
            }
            return TryResolveTemplateBytes(HUtil32.GbkEncoding.GetBytes(name),
                out resolution);
        }

        public bool TryResolveTemplateBytes(ReadOnlySpan<byte> nameBytes,
            out NativeType2FieldHeroTemplateResolution resolution)
        {
            resolution = null;
            if (nameBytes.Length is 0 or >
                NativeType2FieldHeroDefinition.NameCapacity)
                return false;

            var publication = Volatile.Read(ref _publication);
            var runtime = publication.FindByNameBytes(nameBytes);
            if (runtime == null) return false;

            resolution = new NativeType2FieldHeroTemplateResolution(runtime);
            return true;
        }

        private void PublishCore(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems,
            INativeFieldHeroMonItemsSource monItemsSource)
        {
            if (definitionCatalog == null)
                throw new ArgumentNullException(nameof(definitionCatalog));
            if (standardItems == null)
                throw new ArgumentNullException(nameof(standardItems));
            if (!definitionCatalog.Ready)
            {
                throw new InvalidOperationException(
                    "Native FieldHero definition catalog is not published.");
            }
            if (!standardItems.Ready)
            {
                throw new InvalidOperationException(
                    "Native standard-item catalog is not published.");
            }

            // Both source catalogs are one-shot immutable publications. Capture
            // each exposed collection once before constructing the new epoch.
            var definitions = definitionCatalog.Definitions;
            var itemDefinitions = standardItems.Definitions;
            var items = standardItems.Items;
            var generation = checked(_nextGeneration + 1);
            var next = new Publication(generation, definitions,
                itemDefinitions, items, monItemsSource);

            Interlocked.Exchange(ref _publication, next);
            _nextGeneration = generation;
        }
    }
}
