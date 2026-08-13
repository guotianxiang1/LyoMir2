#nullable enable
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
///
/// Every BCL name below is written as global::System.* on purpose. This file is
/// compiled into ~400 top-level-statement programs, and a top-level local or
/// local function named Environment / Path / File is enough to make the simple
/// name unresolvable here (CS8801). DynRoomRuntimeTransactionCheck has a local
/// function called Environment and did exactly that.
///
/// For the same portability reason there is no Span usage: two projects target
/// net48, where string[].AsSpan does not exist without System.Memory.
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
            var commandLine = global::System.Environment.GetCommandLineArgs();
            if (commandLine.Length > 1)
            {
                var copy = new string[commandLine.Length - 1];
                global::System.Array.Copy(commandLine, 1, copy, 0, copy.Length);
                args = copy;
            }
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
                resolved = ExistingDirectory(
                    global::System.Environment.GetEnvironmentVariable(name));
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
                             : global::System.IO.Path.GetDirectoryName(
                                 global::System.IO.Path.GetFullPath(callerFile)),
                         global::System.Environment.CurrentDirectory,
                         global::System.AppContext.BaseDirectory
                     })
            {
                resolved = WalkForRepo(start);
                if (resolved != null)
                    break;
            }
        }

        if (resolved == null && IsRepoRoot(HardcodedFallback))
            resolved = global::System.IO.Path.GetFullPath(HardcodedFallback);

        if (resolved == null)
            throw new global::System.IO.DirectoryNotFoundException(
                "repository root not found; pass it as argv[0] or set M2_REPO_ROOT");

        Trace(resolved);
        return resolved;
    }

    static void Trace(string resolved)
    {
        if (global::System.Environment.GetEnvironmentVariable("M2_AUDIT_REPO_TRACE") == "1")
            global::System.Console.Error.WriteLine("AUDIT_REPO_ROOT=" + resolved);
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
        if (string.IsNullOrWhiteSpace(path) || !global::System.IO.Directory.Exists(path))
            return null;
        return global::System.IO.Path.GetFullPath(path);
    }

    static bool IsRepoRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !global::System.IO.Directory.Exists(path))
            return false;
        var git = global::System.IO.Path.Combine(path, ".git");
        return global::System.IO.File.Exists(
                   global::System.IO.Path.Combine(path, "LyoMir2.sln"))
               || global::System.IO.Directory.Exists(git)
               || global::System.IO.File.Exists(git);
    }

    static string? WalkForRepo(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;
        global::System.IO.DirectoryInfo? current;
        try
        {
            current = new global::System.IO.DirectoryInfo(start);
        }
        catch (global::System.Exception)
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
