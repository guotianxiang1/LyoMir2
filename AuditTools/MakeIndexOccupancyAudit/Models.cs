using System.Text.Json.Serialization;

namespace MakeIndexOccupancyAudit;

internal sealed class AuditManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string[] RequiredCoverage { get; set; } = Array.Empty<string>();
    public SourceManifest[] Sources { get; set; } = Array.Empty<SourceManifest>();
    public CounterManifest[] Counters { get; set; } = Array.Empty<CounterManifest>();
}

internal sealed class SourceManifest
{
    public string Name { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public string Format { get; set; } = "items-jsonl";
    public string Path { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? ExpectedSha256 { get; set; }
}

internal sealed class CounterManifest
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool Authoritative { get; set; }
    public string? ExpectedSha256 { get; set; }
}

internal sealed class AuditReport
{
    public int SchemaVersion { get; init; } = 1;
    public string ManifestPath { get; init; } = string.Empty;
    public InputFileAudit? ManifestFile { get; set; }
    public DateTime GeneratedUtc { get; init; }
    public bool MigrationGatePassed { get; set; }
    public string[] GateFailures { get; set; } = Array.Empty<string>();
    public AuditSummary Summary { get; set; } = new();
    public SourceReport[] Sources { get; set; } = Array.Empty<SourceReport>();
    public CounterReport[] Counters { get; set; } = Array.Empty<CounterReport>();
    public DuplicateGroup[] DuplicatePrimaryIds { get; set; } = Array.Empty<DuplicateGroup>();
    public Occurrence[] ZeroPrimarySamples { get; set; } = Array.Empty<Occurrence>();
    public Occurrence[] NegativeIntPrimarySamples { get; set; } = Array.Empty<Occurrence>();
    public Occurrence[] DanglingReferenceSamples { get; set; } = Array.Empty<Occurrence>();
}

internal sealed class AuditSummary
{
    public long PrimaryItems { get; set; }
    public long ReferenceItems { get; set; }
    public string PrimaryUnsignedHighWater { get; set; } = "0";
    public string PrimaryUnsignedHighWaterHex { get; set; } = "0x00000000";
    public int DuplicatePrimaryIds { get; set; }
    public long DuplicatePrimaryOccurrences { get; set; }
    public long ZeroPrimaryIds { get; set; }
    public long NegativeIntPrimaryIds { get; set; }
    public long DanglingReferences { get; set; }
    public Dictionary<string, long> PrimaryRanges { get; set; } = new();
    public Dictionary<string, long> PrimaryModulo3 { get; set; } = new();
}

internal sealed class SourceReport
{
    public string Name { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public bool Required { get; set; }
    public int FilesMatched { get; set; }
    public long RowsRead { get; set; }
    public long ItemsFound { get; set; }
    public long EmptyItemSlots { get; set; }
    public InputFileAudit[] Files { get; set; } = Array.Empty<InputFileAudit>();
    public string[] Errors { get; set; } = Array.Empty<string>();
}

internal sealed class CounterReport
{
    public string Name { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool Authoritative { get; set; }
    public bool Loaded { get; set; }
    public string Value { get; set; } = "0";
    public string ValueHex { get; set; } = "0x00000000";
    public bool BelowPrimaryHighWater { get; set; }
    public InputFileAudit? File { get; set; }
    public string? Error { get; set; }
}

internal sealed class InputFileAudit
{
    public string Path { get; set; } = string.Empty;
    public long LengthBefore { get; set; }
    public long LengthAfter { get; set; }
    public DateTime LastWriteUtcBefore { get; set; }
    public DateTime LastWriteUtcAfter { get; set; }
    public string Sha256Before { get; set; } = string.Empty;
    public string Sha256After { get; set; } = string.Empty;
    public bool Unchanged { get; set; }
}

internal sealed class Occurrence
{
    public string Source { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string MakeIndex { get; set; } = "0";
    public string MakeIndexHex { get; set; } = "0x00000000";
    public int SignedIntPattern { get; set; }
    public int? ItemIndex { get; set; }

    [JsonIgnore]
    public uint NumericMakeIndex { get; set; }
}

internal sealed class DuplicateGroup
{
    public string MakeIndex { get; set; } = "0";
    public string MakeIndexHex { get; set; } = "0x00000000";
    public Occurrence[] Occurrences { get; set; } = Array.Empty<Occurrence>();
}
