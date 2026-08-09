using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MakeIndexOccupancyAudit;

internal static class OccupancyScanner
{
    private const int SampleLimit = 200;
    private const uint ItemNumberMaximum = 0xFFFFFFF6;
    internal const string SchemaV1MigrationBlocked =
        "schemaVersion 1 is inspection-only and cannot authorize production migration";

    internal static AuditReport Scan(string manifestPath, AuditManifest manifest)
    {
        ValidateManifest(manifest);
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var baseDirectory = Path.GetDirectoryName(fullManifestPath)
                            ?? Environment.CurrentDirectory;
        var manifestBefore = Snapshot(fullManifestPath);
        var occurrences = new List<Occurrence>();
        var sourceReports = new List<SourceReport>();

        foreach (var source in manifest.Sources)
            sourceReports.Add(ScanSource(baseDirectory, source, occurrences));

        var primary = occurrences.Where(x => x.Role == "primary").ToArray();
        var references = occurrences.Where(x => x.Role == "reference").ToArray();
        var nonzeroPrimaryGroups = primary.Where(x => x.NumericMakeIndex != 0)
            .GroupBy(x => x.NumericMakeIndex)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var duplicates = nonzeroPrimaryGroups.Where(pair => pair.Value.Length > 1)
            .OrderBy(pair => pair.Key)
            .Select(pair => new DuplicateGroup
            {
                MakeIndex = pair.Key.ToString(CultureInfo.InvariantCulture),
                MakeIndexHex = Hex(pair.Key),
                Occurrences = pair.Value
            }).ToArray();
        var primaryIds = nonzeroPrimaryGroups.Keys.ToHashSet();
        var dangling = references.Where(x => x.NumericMakeIndex == 0
                                             || !primaryIds.Contains(x.NumericMakeIndex))
            .ToArray();
        var highWater = primary.Length == 0 ? 0u : primary.Max(x => x.NumericMakeIndex);

        var counterReports = manifest.Counters
            .Select(counter => ScanCounter(baseDirectory, counter, highWater))
            .ToArray();
        var summary = BuildSummary(primary, references, duplicates, dangling, highWater);
        var failures = BuildGateFailures(manifest, sourceReports, counterReports,
            summary);
        var manifestAfter = Snapshot(fullManifestPath);
        var manifestAudit = Compare(manifestBefore, manifestAfter);
        if (!manifestAudit.Unchanged)
            failures.Add("manifest changed while it was being scanned");

        return new AuditReport
        {
            ManifestPath = fullManifestPath,
            ManifestFile = manifestAudit,
            GeneratedUtc = DateTime.UtcNow,
            MigrationGatePassed = failures.Count == 0,
            GateFailures = failures.ToArray(),
            Summary = summary,
            Sources = sourceReports.ToArray(),
            Counters = counterReports,
            DuplicatePrimaryIds = duplicates,
            ZeroPrimarySamples = primary.Where(x => x.NumericMakeIndex == 0)
                .Take(SampleLimit).ToArray(),
            NegativeIntPrimarySamples = primary.Where(x => x.NumericMakeIndex > int.MaxValue)
                .Take(SampleLimit).ToArray(),
            DanglingReferenceSamples = dangling.Take(SampleLimit).ToArray()
        };
    }

    private static SourceReport ScanSource(string baseDirectory, SourceManifest source,
        List<Occurrence> occurrences)
    {
        var report = new SourceReport
        {
            Name = source.Name,
            Coverage = source.Coverage,
            Role = NormalizeRole(source.Role),
            Format = source.Format.Trim().ToLowerInvariant(),
            Required = source.Required
        };
        var errors = new List<string>();
        var audits = new List<InputFileAudit>();
        string[] files;
        try
        {
            files = ResolveFiles(baseDirectory, source.Path);
        }
        catch (Exception ex)
        {
            report.Errors = new[] { ex.Message };
            return report;
        }
        report.FilesMatched = files.Length;
        if (files.Length == 0 && source.Required)
            errors.Add($"required input did not match any files: {source.Path}");
        if (!string.IsNullOrWhiteSpace(source.ExpectedSha256) && files.Length != 1)
            errors.Add("expectedSha256 requires exactly one matched file");

        foreach (var file in files)
        {
            FileSnapshot? before = null;
            try
            {
                before = Snapshot(file);
                if (!string.IsNullOrWhiteSpace(source.ExpectedSha256)
                    && !before.Sha256.Equals(NormalizeHash(source.ExpectedSha256),
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{file}: SHA256 mismatch");
                }
                if (report.Format == "native-item-file")
                    ScanNativeItemFile(file, report, occurrences);
                else
                    ScanJsonLines(file, report, occurrences, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
            finally
            {
                if (before != null)
                {
                    try
                    {
                        var audit = Compare(before, Snapshot(file));
                        audits.Add(audit);
                        if (!audit.Unchanged)
                            errors.Add($"{file}: input changed while scanning");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{file}: post-scan snapshot failed: {ex.Message}");
                    }
                }
            }
        }

        report.Files = audits.ToArray();
        report.Errors = errors.ToArray();
        return report;
    }

    private static void ScanNativeItemFile(string file, SourceReport report,
        List<Occurrence> occurrences)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length % NativeBlobReaders.ItemSize != 0)
            throw new InvalidDataException(
                $"native item file length {stream.Length} is not divisible by " +
                NativeBlobReaders.ItemSize);

        var record = new byte[NativeBlobReaders.ItemSize];
        var recordCount = stream.Length / NativeBlobReaders.ItemSize;
        var owner = Path.GetFileName(file);
        for (long index = 0; index < recordCount; index++)
        {
            stream.ReadExactly(record);
            report.RowsRead++;
            ScanItemRecord(report, occurrences, owner, $"record[{index}]", record);
        }
    }

    private static void ScanJsonLines(string file, SourceReport report,
        List<Occurrence> occurrences, List<string> errors)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            report.RowsRead++;
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("line must be a JSON object");
                ScanRow(document.RootElement, report, occurrences, file, lineNumber);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException
                                           or FormatException or OverflowException)
            {
                errors.Add($"{file}:{lineNumber}: {ex.Message}");
            }
        }
    }

    private static void ScanRow(JsonElement row, SourceReport report,
        List<Occurrence> occurrences, string file, int lineNumber)
    {
        var owner = ReadString(row, "owner") ?? $"{Path.GetFileName(file)}:{lineNumber}";
        switch (report.Format)
        {
            case "items-jsonl":
            {
                var value = RequiredProperty(row, "makeIndex");
                var makeIndex = ParseMakeIndex(value);
                var location = ReadString(row, "location") ?? $"row[{lineNumber}]";
                var itemIndex = ReadOptionalInt(row, "itemIndex");
                AddOccurrence(report, occurrences, owner, location, makeIndex, itemIndex);
                break;
            }
            case "native-human-jsonl":
            {
                var data = NativeBlobReaders.DecodeHuman(ReadBinary(row));
                ScanArea(report, occurrences, owner, data, 0x0F68, 16, "equipment");
                ScanArea(report, occurrences, owner, data, 0x2BF6, 48, "bag");
                ScanArea(report, occurrences, owner, data, 0x52F6, 192, "storage");
                break;
            }
            case "native-hero-jsonl":
            {
                var data = NativeBlobReaders.DecodeHero(ReadBinary(row));
                const int recordSize = 0x49D4;
                for (var slot = 0; slot < data.Length / recordSize; slot++)
                {
                    var record = data.AsSpan(slot * recordSize, recordSize);
                    var prefix = data.Length == recordSize ? string.Empty : $"hero[{slot}].";
                    ScanArea(report, occurrences, owner, record, 0x016C, 16,
                        prefix + "equipment");
                    ScanArea(report, occurrences, owner, record, 0x191A, 40,
                        prefix + "bag");
                }
                break;
            }
            case "native-storage-jsonl":
            {
                var data = NativeBlobReaders.DecodeStorage(ReadBinary(row));
                var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2));
                ScanArea(report, occurrences, owner, data, 4, count, "account-storage");
                break;
            }
            case "native-item-jsonl":
            {
                var data = ReadBinary(row);
                if (data.Length != NativeBlobReaders.ItemSize)
                    throw new InvalidDataException(
                        $"native item record length {data.Length} != {NativeBlobReaders.ItemSize}");
                var location = ReadString(row, "location") ?? $"record[{lineNumber}]";
                ScanItemRecord(report, occurrences, owner, location, data);
                break;
            }
            default:
                throw new InvalidDataException($"unsupported source format '{report.Format}'");
        }
    }

    private static void ScanArea(SourceReport report, List<Occurrence> occurrences,
        string owner, ReadOnlySpan<byte> data, int offset, int count, string name)
    {
        var requiredLength = checked(offset + count * NativeBlobReaders.ItemSize);
        if (requiredLength > data.Length)
            throw new InvalidDataException(
                $"{name} area ends at 0x{requiredLength:X}, beyond data length 0x{data.Length:X}");
        for (var i = 0; i < count; i++)
        {
            var record = data.Slice(offset + i * NativeBlobReaders.ItemSize,
                NativeBlobReaders.ItemSize);
            ScanItemRecord(report, occurrences, owner, $"{name}[{i}]", record);
        }
    }

    private static void ScanItemRecord(SourceReport report, List<Occurrence> occurrences,
        string owner, string location, ReadOnlySpan<byte> record)
    {
        var itemIndex = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4, 2));
        if (itemIndex == 0)
        {
            report.EmptyItemSlots++;
            return;
        }
        var makeIndex = BinaryPrimitives.ReadUInt32LittleEndian(record);
        AddOccurrence(report, occurrences, owner, location, makeIndex, itemIndex);
    }

    private static void AddOccurrence(SourceReport report, List<Occurrence> occurrences,
        string owner, string location, uint makeIndex, int? itemIndex)
    {
        occurrences.Add(new Occurrence
        {
            Source = report.Name,
            Coverage = report.Coverage,
            Role = report.Role,
            Owner = owner,
            Location = location,
            MakeIndex = makeIndex.ToString(CultureInfo.InvariantCulture),
            MakeIndexHex = Hex(makeIndex),
            SignedIntPattern = unchecked((int)makeIndex),
            ItemIndex = itemIndex,
            NumericMakeIndex = makeIndex
        });
        report.ItemsFound++;
    }

    private static CounterReport ScanCounter(string baseDirectory, CounterManifest counter,
        uint highWater)
    {
        var report = new CounterReport
        {
            Name = counter.Name,
            Required = counter.Required,
            Authoritative = counter.Authoritative
        };
        var path = ResolveSingleFile(baseDirectory, counter.Path);
        if (path == null)
        {
            if (counter.Required) report.Error = $"required counter not found: {counter.Path}";
            return report;
        }

        FileSnapshot? before = null;
        try
        {
            before = Snapshot(path);
            if (!string.IsNullOrWhiteSpace(counter.ExpectedSha256)
                && !before.Sha256.Equals(NormalizeHash(counter.ExpectedSha256),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA256 mismatch");
            byte[] data;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length != 4)
                    throw new InvalidDataException($"counter length {stream.Length} != 4");
                data = new byte[4];
                stream.ReadExactly(data);
            }
            var value = BinaryPrimitives.ReadUInt32LittleEndian(data);
            report.Loaded = true;
            report.Value = value.ToString(CultureInfo.InvariantCulture);
            report.ValueHex = Hex(value);
            report.BelowPrimaryHighWater = value < highWater;
        }
        catch (Exception ex)
        {
            report.Error = ex.Message;
        }
        finally
        {
            if (before != null)
            {
                try
                {
                    report.File = Compare(before, Snapshot(path));
                    if (!report.File.Unchanged)
                        report.Error = Append(report.Error, "input changed while scanning");
                }
                catch (Exception ex)
                {
                    report.Error = Append(report.Error, "post-scan snapshot failed: " + ex.Message);
                }
            }
        }
        return report;
    }

    private static AuditSummary BuildSummary(Occurrence[] primary, Occurrence[] references,
        DuplicateGroup[] duplicates, Occurrence[] dangling, uint highWater)
    {
        var ranges = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["zero"] = 0,
            ["original-generator-00000001-FFFFFFF6"] = 0,
            ["above-reset-threshold-FFFFFFF7-FFFFFFFF"] = 0
        };
        var modulo = new Dictionary<string, long>(StringComparer.Ordinal)
            { ["0"] = 0, ["1"] = 0, ["2"] = 0 };
        foreach (var item in primary)
        {
            var value = item.NumericMakeIndex;
            if (value == 0) ranges["zero"]++;
            else if (value <= ItemNumberMaximum)
                ranges["original-generator-00000001-FFFFFFF6"]++;
            else
                ranges["above-reset-threshold-FFFFFFF7-FFFFFFFF"]++;
            if (value != 0) modulo[(value % 3).ToString(CultureInfo.InvariantCulture)]++;
        }

        return new AuditSummary
        {
            PrimaryItems = primary.LongLength,
            ReferenceItems = references.LongLength,
            PrimaryUnsignedHighWater = highWater.ToString(CultureInfo.InvariantCulture),
            PrimaryUnsignedHighWaterHex = Hex(highWater),
            DuplicatePrimaryIds = duplicates.Length,
            DuplicatePrimaryOccurrences = duplicates.Sum(group => (long)group.Occurrences.Length),
            ZeroPrimaryIds = primary.LongCount(x => x.NumericMakeIndex == 0),
            NegativeIntPrimaryIds = primary.LongCount(x => x.NumericMakeIndex > int.MaxValue),
            DanglingReferences = dangling.LongLength,
            PrimaryRanges = ranges,
            PrimaryModulo3 = modulo
        };
    }

    private static List<string> BuildGateFailures(AuditManifest manifest,
        List<SourceReport> sources, CounterReport[] counters, AuditSummary summary)
    {
        var failures = new List<string> { SchemaV1MigrationBlocked };
        foreach (var source in sources)
            foreach (var error in source.Errors)
                failures.Add($"source {source.Name}: {error}");
        if (manifest.RequiredCoverage.Length == 0)
            failures.Add("required coverage list is empty");

        var completedCoverage = sources.Where(source => source.FilesMatched > 0
                                                        && source.Errors.Length == 0)
            .Select(source => source.Coverage)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var required in manifest.RequiredCoverage)
            if (!completedCoverage.Contains(required))
                failures.Add($"required coverage '{required}' is missing or invalid");

        foreach (var counter in counters)
        {
            if (!string.IsNullOrEmpty(counter.Error))
                failures.Add($"counter {counter.Name}: {counter.Error}");
            if (counter.Authoritative && counter.Loaded && counter.BelowPrimaryHighWater)
                failures.Add($"authoritative counter {counter.Name} is below primary unsigned high-water");
        }
        if (!counters.Any(counter => counter.Authoritative && counter.Loaded
                                     && string.IsNullOrEmpty(counter.Error)))
            failures.Add("no valid authoritative ItemNumber counter was loaded");
        if (summary.PrimaryItems == 0)
            failures.Add("no primary items were loaded");
        if (summary.DuplicatePrimaryIds != 0)
            failures.Add($"{summary.DuplicatePrimaryIds} duplicate nonzero primary MakeIndex values");
        if (summary.ZeroPrimaryIds != 0)
            failures.Add($"{summary.ZeroPrimaryIds} active primary items have MakeIndex zero");
        if (summary.DanglingReferences != 0)
            failures.Add($"{summary.DanglingReferences} references do not match a nonzero primary item");
        return failures;
    }

    private static void ValidateManifest(AuditManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"unsupported manifest schemaVersion {manifest.SchemaVersion}");
        manifest.RequiredCoverage ??= Array.Empty<string>();
        manifest.Sources ??= Array.Empty<SourceManifest>();
        manifest.Counters ??= Array.Empty<CounterManifest>();
        if (manifest.Sources.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != manifest.Sources.Length)
            throw new InvalidDataException("source names must be unique");
        if (manifest.Counters.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != manifest.Counters.Length)
            throw new InvalidDataException("counter names must be unique");
        foreach (var source in manifest.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(source.Path))
                throw new InvalidDataException("every source requires name and path");
            _ = NormalizeRole(source.Role);
        }
        foreach (var counter in manifest.Counters)
            if (string.IsNullOrWhiteSpace(counter.Name) || string.IsNullOrWhiteSpace(counter.Path))
                throw new InvalidDataException("every counter requires name and path");
    }

    private static string NormalizeRole(string role)
    {
        var normalized = (role ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "primary" or "reference"
            ? normalized
            : throw new InvalidDataException($"unsupported source role '{role}'");
    }

    private static string[] ResolveFiles(string baseDirectory, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return Array.Empty<string>();
        var combined = Path.IsPathFullyQualified(configuredPath)
            ? configuredPath
            : Path.Combine(baseDirectory, configuredPath);
        if (!combined.Contains('*') && !combined.Contains('?'))
        {
            var full = Path.GetFullPath(combined);
            return File.Exists(full) ? new[] { full } : Array.Empty<string>();
        }

        var directoryPart = Path.GetDirectoryName(combined) ?? baseDirectory;
        var pattern = Path.GetFileName(combined);
        if (directoryPart.Contains('*') || directoryPart.Contains('?'))
            throw new InvalidDataException("wildcards are supported only in the final path segment");
        var directory = Path.GetFullPath(directoryPart);
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
    }

    private static string? ResolveSingleFile(string baseDirectory, string configuredPath)
    {
        var files = ResolveFiles(baseDirectory, configuredPath);
        if (files.Length > 1)
            throw new InvalidDataException($"counter path matched {files.Length} files; exactly one is required");
        return files.Length == 1 ? files[0] : null;
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
        => TryGetProperty(element, name, out var value)
            ? value
            : throw new InvalidDataException($"missing '{name}'");

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"'{name}' must be a string");
        return value.GetString();
    }

    private static int? ReadOptionalInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"'{name}' must be an Int32");
        return result;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static uint ParseMakeIndex(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => throw new InvalidDataException("makeIndex must be a number or string")
        };
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(text.AsSpan(2), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture);
        if (text.StartsWith('-'))
            return unchecked((uint)int.Parse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture));
        return uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static byte[] ReadBinary(JsonElement row)
    {
        if (TryGetProperty(row, "dataHex", out var hex))
        {
            if (hex.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("dataHex must be a string");
            var text = (hex.GetString() ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (text.Length % 2 != 0) throw new InvalidDataException("dataHex has odd length");
            return Convert.FromHexString(text);
        }
        if (TryGetProperty(row, "dataBase64", out var base64))
        {
            if (base64.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("dataBase64 must be a string");
            return Convert.FromBase64String(base64.GetString() ?? string.Empty);
        }
        throw new InvalidDataException("missing dataHex or dataBase64");
    }

    private static FileSnapshot Snapshot(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("Input disappeared", path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        info.Refresh();
        return new FileSnapshot(Path.GetFullPath(path), info.Length, info.LastWriteTimeUtc, hash);
    }

    private static InputFileAudit Compare(FileSnapshot before, FileSnapshot after)
        => new()
        {
            Path = before.Path,
            LengthBefore = before.Length,
            LengthAfter = after.Length,
            LastWriteUtcBefore = before.LastWriteUtc,
            LastWriteUtcAfter = after.LastWriteUtc,
            Sha256Before = before.Sha256,
            Sha256After = after.Sha256,
            Unchanged = before.Length == after.Length
                        && before.LastWriteUtc == after.LastWriteUtc
                        && before.Sha256 == after.Sha256
        };

    private static string NormalizeHash(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal).Trim();

    private static string Append(string? current, string addition)
        => string.IsNullOrEmpty(current) ? addition : current + "; " + addition;

    private static string Hex(uint value) => $"0x{value:X8}";

    private sealed record FileSnapshot(string Path, long Length,
        DateTime LastWriteUtc, string Sha256);
}
