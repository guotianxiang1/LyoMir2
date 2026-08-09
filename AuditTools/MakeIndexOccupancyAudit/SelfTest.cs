using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MakeIndexOccupancyAudit;

internal static class SelfTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal static int Run()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "occupancy-scenarios.json");
        if (!File.Exists(fixturePath))
            throw new FileNotFoundException("Self-test fixture not found", fixturePath);

        var fixture = JsonSerializer.Deserialize<SelfTestFixture>(
                          File.ReadAllText(fixturePath), JsonOptions)
                      ?? throw new InvalidDataException("Self-test fixture is empty");
        if (fixture.SchemaVersion != 1 || fixture.Scenarios.Length == 0)
            throw new InvalidDataException("Self-test fixture schema or scenarios are invalid");

        var root = Path.Combine(Path.GetTempPath(),
            "make-index-occupancy-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var scenario in fixture.Scenarios)
                RunScenario(root, scenario);
            RunNativeItemFileTests(root);
            RunFailClosedGateTests(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Console.WriteLine(
            $"PASS scenarios={fixture.Scenarios.Length} unsigned-high-water=uint32 " +
            "duplicate-zero-negative=covered native-item-file=covered " +
            "authoritative-dat=compared empty-coverage-zero-primary=fail-closed " +
            "schema-v1-migration=fail-closed inputs=unchanged");
        return 0;
    }

    private static void RunFailClosedGateTests(string root)
    {
        const string testName = "fail-closed-gates";
        var directory = Path.Combine(root, testName);
        Directory.CreateDirectory(directory);
        var primaryPath = Path.Combine(directory, "primary.bin");
        var emptyPrimaryPath = Path.Combine(directory, "empty-primary.bin");
        var counterPath = Path.Combine(directory, "ItemNumber.Dat");
        var emptyCoverageManifestPath = Path.Combine(directory,
            "empty-coverage-manifest.json");
        var zeroPrimaryManifestPath = Path.Combine(directory,
            "zero-primary-manifest.json");

        File.WriteAllBytes(primaryPath, CreateNativeItemFile((1003u, (ushort)1)));
        File.WriteAllBytes(emptyPrimaryPath,
            CreateNativeItemFile((77u, (ushort)0)));
        var counterBytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(counterBytes, 1003u);
        File.WriteAllBytes(counterPath, counterBytes);

        var emptyCoverageManifest = new AuditManifest
        {
            RequiredCoverage = Array.Empty<string>(),
            Sources = new[]
            {
                new SourceManifest
                {
                    Name = "primary",
                    Coverage = "primary-items",
                    Role = "primary",
                    Format = "native-item-file",
                    Path = Path.GetFileName(primaryPath),
                    Required = true
                }
            },
            Counters = new[]
            {
                new CounterManifest
                {
                    Name = "authoritative-dat",
                    Path = Path.GetFileName(counterPath),
                    Required = true,
                    Authoritative = true
                }
            }
        };
        WriteManifest(emptyCoverageManifestPath, emptyCoverageManifest);
        var emptyCoverageReport = OccupancyScanner.Scan(emptyCoverageManifestPath,
            emptyCoverageManifest);
        Assert(!emptyCoverageReport.MigrationGatePassed,
            testName + ": empty required coverage passed");
        EqualSequence(new[]
            {
                OccupancyScanner.SchemaV1MigrationBlocked,
                "required coverage list is empty"
            },
            emptyCoverageReport.GateFailures,
            testName + ": empty coverage gate failures");

        var zeroPrimaryManifest = new AuditManifest
        {
            RequiredCoverage = new[] { "primary-items" },
            Sources = new[]
            {
                new SourceManifest
                {
                    Name = "primary",
                    Coverage = "primary-items",
                    Role = "primary",
                    Format = "native-item-file",
                    Path = Path.GetFileName(emptyPrimaryPath),
                    Required = true
                }
            },
            Counters = emptyCoverageManifest.Counters
        };
        WriteManifest(zeroPrimaryManifestPath, zeroPrimaryManifest);
        var zeroPrimaryReport = OccupancyScanner.Scan(zeroPrimaryManifestPath,
            zeroPrimaryManifest);
        Assert(!zeroPrimaryReport.MigrationGatePassed,
            testName + ": zero primary items passed");
        Equal(0L, zeroPrimaryReport.Summary.PrimaryItems,
            testName + ": zero primary count");
        EqualSequence(new[]
            {
                OccupancyScanner.SchemaV1MigrationBlocked,
                "no primary items were loaded"
            },
            zeroPrimaryReport.GateFailures,
            testName + ": zero primary gate failures");
    }

    private static void RunNativeItemFileTests(string root)
    {
        const string testName = "native-item-file";
        var directory = Path.Combine(root, testName);
        var npcSaveDirectory = Path.Combine(directory, "NpcSave");
        Directory.CreateDirectory(npcSaveDirectory);

        var explicitPath = Path.Combine(directory, "offline-container.bin");
        var npcSaveAPath = Path.Combine(npcSaveDirectory, "merchant-a.Sav");
        var npcSaveBPath = Path.Combine(npcSaveDirectory, "merchant-b.Sav");
        var badPath = Path.Combine(directory, "bad-length.Sav");
        var counterPath = Path.Combine(directory, "ItemNumber.Dat");
        var manifestPath = Path.Combine(directory, "manifest.json");
        var badManifestPath = Path.Combine(directory, "bad-manifest.json");

        File.WriteAllBytes(explicitPath,
            CreateNativeItemFile((1003u, (ushort)1), (77u, (ushort)0)));
        File.WriteAllBytes(npcSaveAPath,
            CreateNativeItemFile((0x80000001u, (ushort)2)));
        File.WriteAllBytes(npcSaveBPath,
            CreateNativeItemFile((uint.MaxValue, (ushort)3)));
        File.WriteAllBytes(badPath, new byte[NativeBlobReaders.ItemSize + 1]);
        var counterBytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(counterBytes, uint.MaxValue);
        File.WriteAllBytes(counterPath, counterBytes);

        var manifest = new AuditManifest
        {
            RequiredCoverage = new[] { "offline-container", "merchant-saves" },
            Sources = new[]
            {
                new SourceManifest
                {
                    Name = "offline-explicit",
                    Coverage = "offline-container",
                    Role = "primary",
                    Format = "native-item-file",
                    Path = Path.GetFileName(explicitPath),
                    Required = true
                },
                new SourceManifest
                {
                    Name = "merchant-glob",
                    Coverage = "merchant-saves",
                    Role = "primary",
                    Format = "native-item-file",
                    Path = Path.Combine("NpcSave", "*.Sav"),
                    Required = true
                }
            },
            Counters = new[]
            {
                new CounterManifest
                {
                    Name = "authoritative-original-dat",
                    Path = Path.GetFileName(counterPath),
                    Required = true,
                    Authoritative = true
                }
            }
        };
        WriteManifest(manifestPath, manifest);
        var immutableInputs = new[]
            {
                explicitPath, npcSaveAPath, npcSaveBPath, counterPath, manifestPath
            }
            .ToDictionary(path => path, File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

        var report = OccupancyScanner.Scan(manifestPath, manifest);
        Assert(!report.MigrationGatePassed,
            testName + ": schema-v1 inspection authorized migration");
        EqualSequence(new[] { OccupancyScanner.SchemaV1MigrationBlocked },
            report.GateFailures,
            testName + ": schema-v1 migration failure");
        Equal(3L, report.Summary.PrimaryItems, testName + ": primary count");
        Equal("4294967295", report.Summary.PrimaryUnsignedHighWater,
            testName + ": unsigned high-water decimal");
        Equal("0xFFFFFFFF", report.Summary.PrimaryUnsignedHighWaterHex,
            testName + ": unsigned high-water hex");
        Equal(2L, report.Summary.NegativeIntPrimaryIds,
            testName + ": negative Int32 bit-pattern count");

        var explicitSource = report.Sources.Single(source =>
            source.Name == "offline-explicit");
        Equal(1, explicitSource.FilesMatched, testName + ": explicit file count");
        Equal(2L, explicitSource.RowsRead, testName + ": explicit record count");
        Equal(1L, explicitSource.ItemsFound, testName + ": explicit item count");
        Equal(1L, explicitSource.EmptyItemSlots, testName + ": empty slot count");
        var globSource = report.Sources.Single(source => source.Name == "merchant-glob");
        Equal(2, globSource.FilesMatched, testName + ": glob file count");
        Equal(2L, globSource.RowsRead, testName + ": glob record count");
        Equal(2L, globSource.ItemsFound, testName + ": glob item count");
        Assert(report.Sources.All(source => source.Errors.Length == 0
                                            && source.Files.All(file => file.Unchanged)),
            testName + ": valid source audit failed");
        Assert(report.ManifestFile is { Unchanged: true },
            testName + ": manifest read-only audit failed");
        Assert(report.Counters.Single().File is { Unchanged: true },
            testName + ": counter read-only audit failed");
        foreach (var input in immutableInputs)
            Assert(File.ReadAllBytes(input.Key).SequenceEqual(input.Value),
                $"{testName}: scanner changed {Path.GetFileName(input.Key)}");

        var badManifest = new AuditManifest
        {
            RequiredCoverage = new[] { "bad-native-items" },
            Sources = new[]
            {
                new SourceManifest
                {
                    Name = "bad-length",
                    Coverage = "bad-native-items",
                    Role = "primary",
                    Format = "native-item-file",
                    Path = Path.GetFileName(badPath),
                    Required = true
                }
            },
            Counters = manifest.Counters
        };
        WriteManifest(badManifestPath, badManifest);
        var badBytes = File.ReadAllBytes(badPath);
        var badReport = OccupancyScanner.Scan(badManifestPath, badManifest);
        Assert(!badReport.MigrationGatePassed,
            testName + ": non-divisible file passed the migration gate");
        var badSource = badReport.Sources.Single();
        Equal(0L, badSource.RowsRead,
            testName + ": invalid file was partially scanned");
        Assert(badSource.Errors.Single().Contains(
                $"length {NativeBlobReaders.ItemSize + 1} is not divisible by " +
                NativeBlobReaders.ItemSize, StringComparison.Ordinal),
            testName + ": invalid length error is missing");
        Assert(badSource.Files is [{ Unchanged: true }],
            testName + ": invalid source read-only audit failed");
        Assert(File.ReadAllBytes(badPath).SequenceEqual(badBytes),
            testName + ": invalid source was changed");
        Assert(badReport.ManifestFile is { Unchanged: true },
            testName + ": invalid manifest read-only audit failed");
    }

    private static void RunScenario(string root, SelfTestScenario scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.Name))
            throw new InvalidDataException("Self-test scenario name is missing");

        var directory = Path.Combine(root, scenario.Name);
        Directory.CreateDirectory(directory);
        var primaryPath = Path.Combine(directory, "primary.jsonl");
        var referencePath = Path.Combine(directory, "references.jsonl");
        var counterPath = Path.Combine(directory, "ItemNumber.Dat");
        var manifestPath = Path.Combine(directory, "manifest.json");

        WriteJsonLines(primaryPath, scenario.Primary);
        WriteJsonLines(referencePath, scenario.References);
        var counterBytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(counterBytes,
            ParseUInt32(scenario.AuthoritativeDatValue));
        File.WriteAllBytes(counterPath, counterBytes);

        var manifest = new AuditManifest
        {
            RequiredCoverage = new[] { "primary-items", "reference-items" },
            Sources = new[]
            {
                new SourceManifest
                {
                    Name = "primary",
                    Coverage = "primary-items",
                    Role = "primary",
                    Format = "items-jsonl",
                    Path = Path.GetFileName(primaryPath),
                    Required = true
                },
                new SourceManifest
                {
                    Name = "references",
                    Coverage = "reference-items",
                    Role = "reference",
                    Format = "items-jsonl",
                    Path = Path.GetFileName(referencePath),
                    Required = true
                }
            },
            Counters = new[]
            {
                new CounterManifest
                {
                    Name = "authoritative-original-dat",
                    Path = Path.GetFileName(counterPath),
                    Required = true,
                    Authoritative = true
                }
            }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));

        var immutableInputs = new[] { primaryPath, referencePath, counterPath, manifestPath }
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var report = OccupancyScanner.Scan(manifestPath, manifest);

        CheckScenario(scenario, report);
        foreach (var input in immutableInputs)
            Assert(File.ReadAllBytes(input.Key).SequenceEqual(input.Value),
                $"{scenario.Name}: scanner changed {Path.GetFileName(input.Key)}");
        Assert(report.ManifestFile is { Unchanged: true },
            $"{scenario.Name}: manifest read-only audit failed");
        Assert(report.Sources.All(source => source.Files.Length == 1
                                            && source.Files[0].Unchanged),
            $"{scenario.Name}: source read-only audit failed");
        Assert(report.Counters.Single().File is { Unchanged: true },
            $"{scenario.Name}: counter read-only audit failed");
    }

    private static void CheckScenario(SelfTestScenario scenario, AuditReport report)
    {
        var expected = scenario.Expected;
        var summary = report.Summary;
        Equal(expected.GatePassed, report.MigrationGatePassed,
            scenario.Name + ": migration gate");
        Equal(expected.PrimaryItems, summary.PrimaryItems,
            scenario.Name + ": primary count");
        Equal(expected.ReferenceItems, summary.ReferenceItems,
            scenario.Name + ": reference count");
        Equal(expected.HighWater, summary.PrimaryUnsignedHighWater,
            scenario.Name + ": unsigned high-water decimal");
        Equal(expected.HighWaterHex, summary.PrimaryUnsignedHighWaterHex,
            scenario.Name + ": unsigned high-water hex");
        Equal(expected.DuplicatePrimaryIds, summary.DuplicatePrimaryIds,
            scenario.Name + ": duplicate group count");
        Equal(expected.DuplicatePrimaryOccurrences, summary.DuplicatePrimaryOccurrences,
            scenario.Name + ": duplicate occurrence count");
        Equal(expected.ZeroPrimaryIds, summary.ZeroPrimaryIds,
            scenario.Name + ": zero primary count");
        Equal(expected.NegativeIntPrimaryIds, summary.NegativeIntPrimaryIds,
            scenario.Name + ": negative Int32 bit-pattern count");
        Equal(expected.DanglingReferences, summary.DanglingReferences,
            scenario.Name + ": dangling reference count");
        EqualDictionary(expected.PrimaryRanges, summary.PrimaryRanges,
            scenario.Name + ": primary ranges");
        EqualDictionary(expected.PrimaryModulo3, summary.PrimaryModulo3,
            scenario.Name + ": modulo-3 distribution");

        var primarySource = report.Sources.Single(source => source.Name == "primary");
        Equal(scenario.Primary.LongLength, primarySource.RowsRead,
            scenario.Name + ": primary rows read");
        Equal(scenario.Primary.LongLength, primarySource.ItemsFound,
            scenario.Name + ": primary items found");
        var referenceSource = report.Sources.Single(source => source.Name == "references");
        Equal(scenario.References.LongLength, referenceSource.RowsRead,
            scenario.Name + ": reference rows read");
        Equal(scenario.References.LongLength, referenceSource.ItemsFound,
            scenario.Name + ": reference items found");

        var counter = report.Counters.Single();
        Assert(counter.Loaded && counter.Authoritative && counter.Required,
            $"{scenario.Name}: authoritative Dat was not loaded");
        Equal(expected.CounterValue, counter.Value,
            scenario.Name + ": authoritative Dat decimal");
        Equal(expected.CounterValueHex, counter.ValueHex,
            scenario.Name + ": authoritative Dat hex");
        Equal(expected.CounterBelowHighWater, counter.BelowPrimaryHighWater,
            scenario.Name + ": authoritative Dat comparison");

        EqualSequence(expected.DuplicateMakeIndexHex,
            report.DuplicatePrimaryIds.Select(group => group.MakeIndexHex),
            scenario.Name + ": duplicate IDs");
        EqualSequence(expected.NegativeSignedIntPatterns.OrderBy(value => value),
            report.NegativeIntPrimarySamples.Select(item => item.SignedIntPattern)
                .OrderBy(value => value),
            scenario.Name + ": signed Int32 patterns");
        Equal(expected.ZeroPrimaryIds, report.ZeroPrimarySamples.LongLength,
            scenario.Name + ": zero samples");
        Equal(expected.DanglingReferences, report.DanglingReferenceSamples.LongLength,
            scenario.Name + ": dangling samples");
        EqualSequence(expected.GateFailures, report.GateFailures,
            scenario.Name + ": gate failures");
    }

    private static void WriteJsonLines(string path, JsonElement[] rows)
    {
        var text = string.Join("\n", rows.Select(row => JsonSerializer.Serialize(row)));
        if (rows.Length != 0) text += '\n';
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static byte[] CreateNativeItemFile(
        params (uint MakeIndex, ushort ItemIndex)[] records)
    {
        var data = new byte[checked(records.Length * NativeBlobReaders.ItemSize)];
        for (var index = 0; index < records.Length; index++)
        {
            var offset = index * NativeBlobReaders.ItemSize;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)),
                records[index].MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(offset + sizeof(uint), sizeof(ushort)),
                records[index].ItemIndex);
        }
        return data;
    }

    private static void WriteManifest(string path, AuditManifest manifest)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));
    }

    private static uint ParseUInt32(string value)
    {
        var text = value.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(text.AsSpan(2), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture)
            : uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static void EqualDictionary(IReadOnlyDictionary<string, long> expected,
        IReadOnlyDictionary<string, long> actual, string label)
    {
        Assert(expected.Count == actual.Count
               && expected.All(pair => actual.TryGetValue(pair.Key, out var value)
                                       && value == pair.Value), label);
    }

    private static void EqualSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual,
        string label)
    {
        Assert(expected.SequenceEqual(actual), label);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"{label}: expected {expected}, actual {actual}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SelfTestFixture
    {
        public int SchemaVersion { get; set; }
        public SelfTestScenario[] Scenarios { get; set; } = Array.Empty<SelfTestScenario>();
    }

    private sealed class SelfTestScenario
    {
        public string Name { get; set; } = string.Empty;
        public string AuthoritativeDatValue { get; set; } = "0";
        public JsonElement[] Primary { get; set; } = Array.Empty<JsonElement>();
        public JsonElement[] References { get; set; } = Array.Empty<JsonElement>();
        public SelfTestExpected Expected { get; set; } = new();
    }

    private sealed class SelfTestExpected
    {
        public bool GatePassed { get; set; }
        public long PrimaryItems { get; set; }
        public long ReferenceItems { get; set; }
        public string HighWater { get; set; } = "0";
        public string HighWaterHex { get; set; } = "0x00000000";
        public int DuplicatePrimaryIds { get; set; }
        public long DuplicatePrimaryOccurrences { get; set; }
        public long ZeroPrimaryIds { get; set; }
        public long NegativeIntPrimaryIds { get; set; }
        public long DanglingReferences { get; set; }
        public string CounterValue { get; set; } = "0";
        public string CounterValueHex { get; set; } = "0x00000000";
        public bool CounterBelowHighWater { get; set; }
        public Dictionary<string, long> PrimaryRanges { get; set; } = new();
        public Dictionary<string, long> PrimaryModulo3 { get; set; } = new();
        public string[] DuplicateMakeIndexHex { get; set; } = Array.Empty<string>();
        public int[] NegativeSignedIntPatterns { get; set; } = Array.Empty<int>();
        public string[] GateFailures { get; set; } = Array.Empty<string>();
    }
}
