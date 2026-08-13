#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;

/// <summary>
/// Shared repository-root locator for AuditTools.
///
/// Priority:
///   1. args[0] when it names an existing directory (or args[1] / M2_REPO_ROOT
///      when the first argument is already a fixture path)
///   2. M2_REPO_ROOT, then LYOMIR_REPO_ROOT
///   3. Walk up from the call-site source file, the working directory, and the
///      executable directory until a folder containing <c>.git</c> or
///      <c>LyoMir2.sln</c> is found
///   4. The historical hardcoded main worktree, last
///
/// A sibling folder literally named "LyoMir2-master" is never probed during the
/// walk: from D:\loym2 that always lands on the main worktree, which is often
/// sitting on an unrelated branch.
/// </summary>
internal static class AuditRepoRoot
{
    public const string HardcodedFallback = @"D:\loym2\LyoMir2-master";

    public static string Resolve(
        string[]? args = null,
        bool firstArgIsRepoRoot = true,
        [CallerFilePath] string callerFile = "")
    {
        if (args == null)
        {
            var commandLine = Environment.GetCommandLineArgs();
            if (commandLine.Length > 1)
                args = commandLine.AsSpan(1).ToArray();
        }

        string? resolved = null;
        if (firstArgIsRepoRoot)
            resolved = ExistingDirectory(Nth(args, 0));
        else
        {
            var second = ExistingDirectory(Nth(args, 1));
            if (second != null && IsRepoRoot(second))
                resolved = second;
        }

        if (resolved == null)
        {
            foreach (var name in new[] { "M2_REPO_ROOT", "LYOMIR_REPO_ROOT" })
            {
                resolved = ExistingDirectory(Environment.GetEnvironmentVariable(name));
                if (resolved != null)
                    break;
            }
        }

        if (resolved == null)
        {
            foreach (var start in new[]
                     {
                         string.IsNullOrEmpty(callerFile)
                             ? null
                             : Path.GetDirectoryName(Path.GetFullPath(callerFile)),
                         Environment.CurrentDirectory,
                         AppContext.BaseDirectory
                     })
            {
                resolved = WalkForRepo(start);
                if (resolved != null)
                    break;
            }
        }

        if (resolved == null && IsRepoRoot(HardcodedFallback))
            resolved = Path.GetFullPath(HardcodedFallback);

        if (resolved == null)
            throw new DirectoryNotFoundException(
                "repository root not found; pass it as argv[0] or set M2_REPO_ROOT");

        Trace(resolved);
        return resolved;
    }

    static void Trace(string resolved)
    {
        if (Environment.GetEnvironmentVariable("M2_AUDIT_REPO_TRACE") == "1")
            Console.Error.WriteLine("AUDIT_REPO_ROOT=" + resolved);
    }

    static string? Nth(string[]? args, int index)
    {
        if (args == null || args.Length <= index)
            return null;
        var value = args[index];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    static string? ExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;
        return Path.GetFullPath(path);
    }

    static bool IsRepoRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        var git = Path.Combine(path, ".git");
        return File.Exists(Path.Combine(path, "LyoMir2.sln"))
               || Directory.Exists(git)
               || File.Exists(git);
    }

    static string? WalkForRepo(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;
        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(start);
        }
        catch (Exception)
        {
            return null;
        }

        while (current != null)
        {
            if (IsRepoRoot(current.FullName))
                return current.FullName;
            current = current.Parent;
        }

        return null;
    }
}
