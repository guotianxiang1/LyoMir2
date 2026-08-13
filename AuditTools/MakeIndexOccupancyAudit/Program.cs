using System.Text.Json;

namespace MakeIndexOccupancyAudit;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        try
        {
            // This is a scanner over a caller-supplied manifest, so the sweep harness -- which
            // runs every tool bare and then retries with the repository root -- can only ever
            // exercise the self-test. Printing usage and exiting 2 in both those cases reported
            // a failure without evaluating a single occupancy rule. Anything that is not an
            // explicit scan now runs the self-test instead.
            var explicitScan = args.Length > 0
                && args[0].Equals("scan", StringComparison.OrdinalIgnoreCase);
            if (!explicitScan)
            {
                if (args.Length > 1)
                {
                    Console.Error.WriteLine(
                        "Usage: MakeIndexOccupancyAudit scan <manifest.json> <report.json>\n" +
                        "       MakeIndexOccupancyAudit --self-test");
                    return 2;
                }
                return SelfTest.Run();
            }

            if (args.Length != 3)
            {
                Console.Error.WriteLine(
                    "Usage: MakeIndexOccupancyAudit scan <manifest.json> <report.json>");
                return 2;
            }

            var manifestPath = Path.GetFullPath(args[1]);
            var reportPath = Path.GetFullPath(args[2]);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Manifest not found", manifestPath);
            if (string.Equals(manifestPath, reportPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Report path must not overwrite the manifest");

            var manifest = JsonSerializer.Deserialize<AuditManifest>(
                               File.ReadAllText(manifestPath), JsonOptions)
                           ?? throw new InvalidDataException("Manifest is empty");
            var report = OccupancyScanner.Scan(manifestPath, manifest);

            var reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));

            Console.WriteLine(
                $"MakeIndexOccupancyAudit {(report.MigrationGatePassed ? "PASS" : "FAIL")} " +
                $"primary={report.Summary.PrimaryItems} references={report.Summary.ReferenceItems} " +
                $"highWater={report.Summary.PrimaryUnsignedHighWaterHex} " +
                $"duplicates={report.Summary.DuplicatePrimaryIds} zero={report.Summary.ZeroPrimaryIds} " +
                $"negative={report.Summary.NegativeIntPrimaryIds} dangling={report.Summary.DanglingReferences}");
            Console.WriteLine(reportPath);
            return report.MigrationGatePassed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MakeIndexOccupancyAudit ERROR: " + ex.Message);
            return 2;
        }
    }
}
