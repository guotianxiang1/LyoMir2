using System.Diagnostics;

namespace GameGate.Forms;

internal enum M2ProcessState
{
    NotConfigured,
    MissingFile,
    NotRunning,
    Running,
    Unknown
}

internal readonly record struct M2ProcessProbeResult(
    M2ProcessState State, string? FullPath, string? Error = null);

internal static class M2ProcessProbe
{
    public static M2ProcessProbeResult Check(string? configuredPath, string configDir)
    {
        string? fullPath = ResolvePath(configuredPath, configDir);
        if (fullPath == null)
            return new(M2ProcessState.NotConfigured, null);
        if (!File.Exists(fullPath))
            return new(M2ProcessState.MissingFile, fullPath);

        string processName = Path.GetFileNameWithoutExtension(fullPath);
        if (string.IsNullOrWhiteSpace(processName))
            return new(M2ProcessState.NotConfigured, fullPath);

        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(processName);
        }
        catch (Exception ex)
        {
            return new(M2ProcessState.Unknown, fullPath, ex.Message);
        }

        bool unknownCandidate = false;
        try
        {
            foreach (var process in candidates)
            {
                try
                {
                    string? candidatePath = process.MainModule?.FileName;
                    if (candidatePath == null)
                    {
                        unknownCandidate = true;
                        continue;
                    }
                    if (Path.GetFullPath(candidatePath).Equals(fullPath,
                            StringComparison.OrdinalIgnoreCase))
                        return new(M2ProcessState.Running, fullPath);
                }
                catch
                {
                    unknownCandidate = true;
                }
            }
        }
        finally
        {
            foreach (var process in candidates) process.Dispose();
        }

        return new(unknownCandidate ? M2ProcessState.Unknown : M2ProcessState.NotRunning,
            fullPath);
    }

    public static string? ResolvePath(string? configuredPath, string configDir)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;
        try
        {
            string value = Environment.ExpandEnvironmentVariables(configuredPath.Trim())
                .Trim('"');
            if (value.Length == 0) return null;
            if (!Path.IsPathFullyQualified(value))
                value = Path.Combine(configDir, value);
            return Path.GetFullPath(value);
        }
        catch
        {
            return null;
        }
    }
}
