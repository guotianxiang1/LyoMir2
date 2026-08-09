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
                equipment)
        {
            _owner = owner;
            Equipment = equipment;
        }

        public NativeType2FieldHeroDefinition Definition =>
            _owner.Definition;
        public long Generation => _owner.Generation;
        public IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>
            Equipment { get; }
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
        private int _effectiveJob;

        internal NativeType2FieldHeroRuntimeDefinition(object publicationOwner,
            long generation, NativeType2FieldHeroDefinition definition,
            NativeType2FieldHeroRuntimeEquipmentBinding[] equipment)
        {
            _publicationOwner = publicationOwner;
            Generation = generation;
            Definition = definition;
            _equipment = Array.AsReadOnly(equipment);
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

        internal NativeType2FieldHeroMaterialization MaterializeEquipment(
            Action<string> missingEquipmentLogger)
        {
            if (missingEquipmentLogger == null)
                throw new ArgumentNullException(
                    nameof(missingEquipmentLogger));

            for (var index = 0; index < _equipment.Count; index++)
            {
                var binding = _equipment[index];
                if (!binding.IsMissing) continue;
                missingEquipmentLogger(
                    NativeType2FieldHeroRuntimeCatalogAdapter
                        .MissingEquipmentLogPrefix
                    + binding.Definition.Name
                    + NativeType2FieldHeroRuntimeCatalogAdapter
                        .MissingEquipmentLogSuffix);
            }

            return new NativeType2FieldHeroMaterialization(this, _equipment);
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

        public NativeType2FieldHeroMaterialization MaterializeEquipment(
            Action<string> missingEquipmentLogger) =>
            _runtime.MaterializeEquipment(missingEquipmentLogger);
    }

    /// <summary>
    /// Mutable runtime adapter over immutable Type2 FieldHero definitions.
    /// Normal production is one-shot Publish. Replace is an explicit audit and
    /// reload boundary: the old generation is retained because live actors may
    /// still borrow its manager-owned equipment/drop-list entries.
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
                IReadOnlyList<GoodItem> items)
            {
                Ready = true;
                Generation = generation;

                var standardItems = BuildStandardItemBindings(
                    itemDefinitions, items);
                var entries = new NativeType2FieldHeroRuntimeDefinition[
                    definitions.Count];
                for (var index = 0; index < entries.Length; index++)
                {
                    var definition = definitions[index];
                    entries[index] = new NativeType2FieldHeroRuntimeDefinition(
                        this, generation, definition,
                        BuildEquipmentBindings(definition, standardItems));
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
                for (var index = 0; index < Entries.Count; index++)
                {
                    var entry = Entries[index];
                    if (entry.Definition.NameBytesEqual(nameBytes))
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
        private readonly List<Publication> _retiredPublications = new();
        private Publication _publication = Publication.Empty;
        private long _nextGeneration;

        public bool Ready => Volatile.Read(ref _publication).Ready;
        public int Count => Volatile.Read(ref _publication).Entries.Count;
        public long Generation => Volatile.Read(ref _publication).Generation;

        public int RetiredPublicationCount
        {
            get
            {
                lock (_publishLock) return _retiredPublications.Count;
            }
        }

        public void Publish(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems)
        {
            lock (_publishLock)
            {
                if (Volatile.Read(ref _publication).Ready)
                {
                    throw new InvalidOperationException(
                        "Native FieldHero runtime adapter is already " +
                        "published.");
                }
                PublishCore(definitionCatalog, standardItems, false);
            }
        }

        public void Replace(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems)
        {
            lock (_publishLock)
            {
                if (!Volatile.Read(ref _publication).Ready)
                {
                    throw new InvalidOperationException(
                        "Native FieldHero runtime adapter is not published.");
                }
                PublishCore(definitionCatalog, standardItems, true);
            }
        }

        public bool TryResolveForSpawn(string name, byte? fameJob,
            out NativeType2FieldHeroSpawnSelection selection)
        {
            if (name == null)
            {
                selection = null;
                return false;
            }
            return TryResolveForSpawnBytes(HUtil32.GbkEncoding.GetBytes(name),
                fameJob, out selection);
        }

        public bool TryResolveForSpawnBytes(ReadOnlySpan<byte> nameBytes,
            byte? fameJob,
            out NativeType2FieldHeroSpawnSelection selection)
        {
            selection = null;
            if (nameBytes.Length is 0 or >
                NativeType2FieldHeroDefinition.NameCapacity)
                return false;

            var publication = Volatile.Read(ref _publication);
            var runtime = publication.FindByNameBytes(nameBytes);
            if (runtime == null) return false;

            var effectiveJob = runtime.CaptureEffectiveJob(fameJob);
            selection = new NativeType2FieldHeroSpawnSelection(runtime,
                effectiveJob);
            return true;
        }

        private void PublishCore(
            NativeType2FieldHeroStaticCatalog definitionCatalog,
            NativeType2StdItemStaticCatalog standardItems, bool retireCurrent)
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
                itemDefinitions, items);

            var current = Volatile.Read(ref _publication);
            if (retireCurrent) _retiredPublications.Add(current);
            Interlocked.Exchange(ref _publication, next);
            _nextGeneration = generation;
        }
    }
}
