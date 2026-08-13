using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length == 0)
{
    // The .pas corpus is a runtime artifact that lives outside the repository,
    // so an argument-less invocation has nothing to audit.
    Console.WriteLine("SKIP: PasScriptAudit needs a script corpus. "
        + "Usage: PasScriptAudit <GameSvr build dir> <Envir dir> <source root> <report.json> [parse-baseline.json]");
    return 0;
}

if (args.Length is not 4 and not 5)
{
    Console.Error.WriteLine("Usage: PasScriptAudit <GameSvr build dir> <Envir dir> <source root> <report.json> [parse-baseline.json]");
    return 2;
}

var gameSvrDirectory = Path.GetFullPath(args[0]);
var envirDirectory = Path.GetFullPath(args[1]);
var sourceRoot = Path.GetFullPath(args[2]);
var reportPath = Path.GetFullPath(args[3]);
var parseBaselinePath = args.Length == 5 ? Path.GetFullPath(args[4]) : null;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(gameSvrDirectory, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameSvrDirectory, "GameSvr.dll"));
var lexerType = RequiredType("GameSvr.PasEngine.PasLexer");
var parserType = RequiredType("GameSvr.PasEngine.PasParser");
var programType = RequiredType("GameSvr.PasEngine.PasProgram");
var apiType = RequiredType("GameSvr.PasEngine.PasApiBridge");
var interpreterType = RequiredType("GameSvr.PasEngine.PasInterpreter");
var readerType = RequiredType("GameSvr.PasEngine.PasScriptTextReader");
var includeResolverType = RequiredType("GameSvr.PasEngine.PasIncludeResolver");

var readAllText = readerType.GetMethod("ReadAllText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(readerType.FullName, "ReadAllText");
var setDefines = lexerType.GetMethod("SetDefines", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new MissingMethodException(lexerType.FullName, "SetDefines");
var parse = parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new MissingMethodException(parserType.FullName, "Parse");
var resolveInclude = includeResolverType.GetMethod("Resolve",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(includeResolverType.FullName, "Resolve");

RunIncludeResolverRegression(resolveInclude);

var bridgeSourcePath = Path.Combine(sourceRoot, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
var yanshenSourcePath = Path.Combine(sourceRoot, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.Yanshen.cs");
var bridgeSource = File.ReadAllText(bridgeSourcePath);
var yanshenSource = File.Exists(yanshenSourcePath) ? File.ReadAllText(yanshenSourcePath) : string.Empty;
var api = ReadApiSurface(bridgeSource, yanshenSource);
RunPlayerMemberSurfaceRegression(lexerType, parserType, setDefines, parse, api);
var placeholderSurface = ReadPlaceholderSurface(bridgeSource, yanshenSource);
RunSyntheticStateSurfaceRegression();
RunUnsupportedOverloadSurfaceRegression();
RunCrossSourceSurfaceMergeRegression();
RunGlobalSurfaceRegression();
RunRuntimeDispatchOrderRegression(sourceRoot);
Console.WriteLine($"API standalone={api.Standalone.Count} player-func={api.PlayerFunctions.Count} player-method={api.PlayerMethods.Count} player-read={api.PlayerReadableProperties.Count} player-write={api.PlayerWritableProperties.Count} npc-func={api.NpcFunctions.Count} npc-method={api.NpcMethods.Count} db-method={api.DbMethods.Count}");

var files = Directory.GetFiles(envirDirectory, "*.pas", SearchOption.AllDirectories)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();
var strictUtf8 = new UTF8Encoding(false, true);
var strictGbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
var encodingAudits = files.Select(file => InspectEncoding(file, envirDirectory, strictUtf8, strictGbk)).ToArray();
var scriptDefines = ReadCompilerDefines(envirDirectory, readAllText);
var parseFailures = new List<ParseFailure>();
var skippedArtifacts = new List<SkippedArtifact>();
var parsed = new List<ParsedScript>();
var includeClosures = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
HashSet<string>? builtinFunctions = null;
HashSet<string>? builtinProcedures = null;

foreach (var file in files)
{
    var includeClosure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    includeClosures[Path.GetFullPath(file)] = includeClosure;
    try
    {
        var source = (string)readAllText.Invoke(null, new object[] { file })!;
        var relative = Relative(file, envirDirectory);
        var skippedReason = ClassifyNonSourceArtifact(file, source);
        if (skippedReason != null)
        {
            skippedArtifacts.Add(new SkippedArtifact(relative, skippedReason));
            continue;
        }
        var expanded = PreprocessForAudit(source, Path.GetDirectoryName(file)!, envirDirectory,
            readAllText, resolveInclude, scriptDefines,
            includeClosure);
        var lexer = Activator.CreateInstance(lexerType, expanded)!;
        setDefines.Invoke(lexer, new object[] { scriptDefines });
        var parser = Activator.CreateInstance(parserType, lexer, Path.GetDirectoryName(file) ?? string.Empty)!;
        var program = parse.Invoke(parser, null)!;

        if (builtinFunctions == null || builtinProcedures == null)
        {
            var bridge = Activator.CreateInstance(apiType)!;
            var interpreter = interpreterType.GetConstructor(new[] { programType, apiType })!
                .Invoke(new[] { program, bridge });
            builtinFunctions = ReadStringSet(interpreter, "_builtinFuncs");
            builtinProcedures = ReadStringSet(interpreter, "_builtinProcs");
        }

        parsed.Add(InspectProgram(file, envirDirectory, program, builtinFunctions, builtinProcedures, api, placeholderSurface));
    }
    catch (Exception ex)
    {
        parseFailures.Add(new ParseFailure(Relative(file, envirDirectory), FlattenException(ex)));
    }
}

var sourceClassifications = ClassifyScriptSources(files, envirDirectory, sourceRoot,
    includeClosures, readAllText);
RunScriptSourceClassificationRegression();
RunNpcScriptRegistrationRegression();
var sourceClassificationByFile = sourceClassifications.ToDictionary(item => item.File,
    StringComparer.OrdinalIgnoreCase);
var runtimeParsed = parsed.Where(script =>
        sourceClassificationByFile.TryGetValue(script.File, out var classification) &&
        classification.Role == "runtime-entry")
    .ToArray();
var unresolved = runtimeParsed.SelectMany(script => script.Unresolved)
    .GroupBy(item => item.Kind + "\0" + item.Name, StringComparer.OrdinalIgnoreCase)
    .Select(group => new SymbolSummary(group.First().Kind, group.First().Name, group.Sum(item => item.Count),
        group.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray()))
    .OrderByDescending(item => item.Count).ThenBy(item => item.Kind).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
    .ToArray();
var placeholders = runtimeParsed.SelectMany(script => script.Placeholders)
    .GroupBy(item => item.Kind + "\0" + item.Name, StringComparer.OrdinalIgnoreCase)
    .Select(group => new SymbolSummary(group.First().Kind, group.First().Name, group.Sum(item => item.Count),
        group.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray()))
    .OrderByDescending(item => item.Count).ThenBy(item => item.Kind).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
    .ToArray();
var unsupported = SummarizeUnsupported(runtimeParsed);
var orphanParsed = parsed.Where(script =>
        sourceClassificationByFile.TryGetValue(script.File, out var classification) &&
        classification.Role == "unregistered-orphan")
    .ToArray();
var unsupportedOrphans = SummarizeUnsupported(orphanParsed);
var syntheticState = runtimeParsed.SelectMany(script => script.SyntheticState)
    .GroupBy(item => item.Kind + "\0" + item.Name, StringComparer.OrdinalIgnoreCase)
    .Select(group => new SymbolSummary(group.First().Kind, group.First().Name, group.Sum(item => item.Count),
        group.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray()))
    .OrderByDescending(item => item.Count).ThenBy(item => item.Kind).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
    .ToArray();
var conditionalDispatch = runtimeParsed.SelectMany(script => script.ConditionalDispatch)
    .GroupBy(item => item.Kind + "\0" + item.Name + "\0" +
        (item.FirstLiteralInteger?.ToString() ?? "<dynamic>"),
        StringComparer.OrdinalIgnoreCase)
    .Select(group => new ConditionalDispatchSummary(group.First().Kind,
        group.First().Name, group.First().FirstLiteralInteger,
        group.Sum(item => item.Count),
        group.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path).ToArray()))
    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
    .ThenBy(item => item.FirstLiteralInteger.HasValue ? 0 : 1)
    .ThenBy(item => item.FirstLiteralInteger)
    .ToArray();
var runtimeFiles = runtimeParsed.Select(script => script.File)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var orphanFiles = orphanParsed.Select(script => script.File)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
if (placeholders.Concat(unsupported).Concat(syntheticState)
    .SelectMany(summary => summary.Files)
    .Any(file => !runtimeFiles.Contains(file)))
    throw new InvalidOperationException(
        "Used API summaries contain a non-runtime script source");
if (unsupportedOrphans.SelectMany(summary => summary.Files)
    .Any(file => !orphanFiles.Contains(file) || runtimeFiles.Contains(file)))
    throw new InvalidOperationException(
        "Orphan API summary contains a runtime or non-orphan script source");
Console.WriteLine("runtime-use-summary-regression=PASS");
var parsedByFile = parsed.ToDictionary(item => item.File, StringComparer.OrdinalIgnoreCase);
var failuresByFile = parseFailures.ToDictionary(item => item.File, StringComparer.OrdinalIgnoreCase);
var skippedByFile = skippedArtifacts.ToDictionary(item => item.File, StringComparer.OrdinalIgnoreCase);
var fileMatrix = encodingAudits.Select(item =>
{
    parsedByFile.TryGetValue(item.File, out var parsedScript);
    failuresByFile.TryGetValue(item.File, out var failure);
    skippedByFile.TryGetValue(item.File, out var skipped);
    sourceClassificationByFile.TryGetValue(item.File, out var classification);
    return new FileAuditResult(item.File, item.ByteLength, item.Sha256, item.EncodingKind,
        item.ReaderPath, item.StrictUtf8, item.StrictGbk, parsedScript != null,
        parsedScript?.CallCount ?? 0, failure?.Error, skipped?.Reason,
        classification?.Role ?? "runtime-entry");
}).ToArray();
var encodingSummary = fileMatrix.GroupBy(item => item.EncodingKind, StringComparer.OrdinalIgnoreCase)
    .Select(group => new EncodingSummary(group.Key, group.Count(), group.Count(item => item.Parsed)))
    .OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
    .ToArray();

var report = new AuditReport(
    envirDirectory,
    files.Length,
    parsed.Count,
    parseFailures.Count,
    skippedArtifacts.Count,
    parsed.Sum(item => item.CallCount),
    parseFailures,
    skippedArtifacts,
    unresolved,
    placeholders,
    unsupported,
    unsupportedOrphans,
    syntheticState,
    conditionalDispatch,
    placeholderSurface.SourceFindings,
    placeholderSurface.UnsupportedSourceFindings,
    placeholderSurface.SyntheticStateSourceFindings,
    sourceClassifications,
    encodingSummary,
    fileMatrix);

Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PAS files={report.TotalFiles} parsed={report.ParsedFiles} failed={report.ParseFailureCount} skipped={report.SkippedArtifactCount} calls={report.CallCount} unresolved={report.UnresolvedSymbols.Length} placeholders-used={report.UsedPlaceholders.Length} unsupported-used={report.UsedUnsupportedApis.Length} unsupported-orphans={report.UsedUnsupportedOrphans.Length} synthetic-state-used={report.UsedSyntheticStateApis.Length}");
Console.WriteLine("encoding=" + string.Join(" ", report.EncodingSummary.Select(item =>
    $"{item.Kind}:{item.Files}/{item.Parsed}")));
Console.WriteLine("source-role=" + string.Join(" ", sourceClassifications
    .GroupBy(item => item.Role, StringComparer.OrdinalIgnoreCase)
    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
    .Select(group => $"{group.Key}:{group.Count()}")));
Console.WriteLine($"report={reportPath}");
if (parseBaselinePath == null)
    return report.ParseFailureCount == 0 ? 0 : 1;

var parseBaseline = JsonSerializer.Deserialize<ParseBaseline>(File.ReadAllText(parseBaselinePath))
    ?? throw new InvalidDataException($"Invalid parse baseline: {parseBaselinePath}");
var baselineMatches = report.TotalFiles == parseBaseline.TotalFiles &&
    report.ParsedFiles == parseBaseline.ParsedFiles &&
    report.ParseFailureCount == parseBaseline.ParseFailureCount &&
    report.ParseFailures.Select(FailureKey).SequenceEqual(parseBaseline.ParseFailures.Select(FailureKey));
Console.WriteLine($"parse-baseline={(baselineMatches ? "PASS" : "FAIL")} path={parseBaselinePath}");
return baselineMatches ? 0 : 1;

Type RequiredType(string name) => assembly.GetType(name, throwOnError: true)!;

static FileEncodingAudit InspectEncoding(string file, string root, Encoding strictUtf8, Encoding strictGbk)
{
    var bytes = File.ReadAllBytes(file);
    var hasUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    var ascii = bytes.All(value => value < 0x80);
    var isStrictUtf8 = CanDecode(strictUtf8, bytes);
    var isStrictGbk = CanDecode(strictGbk, bytes);
    var encodingKind = hasUtf8Bom ? "utf8-bom" :
        ascii ? "ascii" :
        isStrictUtf8 && isStrictGbk ? "utf8-gbk-ambiguous" :
        isStrictUtf8 ? "utf8" :
        isStrictGbk ? "gbk" : "invalid";
    var readerPath = hasUtf8Bom ? "utf8-bom" : isStrictUtf8 ? "utf8" : "gbk-fallback";
    return new FileEncodingAudit(Relative(file, root), bytes.LongLength,
        Convert.ToHexString(SHA256.HashData(bytes)), encodingKind, readerPath,
        isStrictUtf8, isStrictGbk);
}

static bool CanDecode(Encoding encoding, byte[] bytes)
{
    try
    {
        encoding.GetString(bytes);
        return true;
    }
    catch (DecoderFallbackException)
    {
        return false;
    }
}

static string FailureKey(ParseFailure failure) => failure.File + "\0" + failure.Error;

static HashSet<string> ReadStringSet(object instance, string fieldName)
{
    var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);
    return new HashSet<string>(((IEnumerable)field.GetValue(instance)!).Cast<string>(), StringComparer.OrdinalIgnoreCase);
}

static string FlattenException(Exception exception)
{
    var messages = new List<string>();
    for (var current = exception; current != null; current = current.InnerException!)
        if (messages.Count == 0 || !string.Equals(messages[^1], current.Message, StringComparison.Ordinal))
            messages.Add(current.Message);
    return string.Join(" -> ", messages);
}

static SymbolSummary[] SummarizeUnsupported(IEnumerable<ParsedScript> scripts) =>
    scripts.SelectMany(script => script.Unsupported)
        .GroupBy(item => item.Kind + "\0" + item.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => new SymbolSummary(group.First().Kind, group.First().Name,
            group.Sum(item => item.Count),
            group.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path).ToArray()))
        .OrderByDescending(item => item.Count).ThenBy(item => item.Kind)
        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

static string? ClassifyNonSourceArtifact(string file, string source)
{
    var bytes = File.ReadAllBytes(file);
    var controlBytes = bytes.Count(value => value < 0x20 && value is not 0x09 and not 0x0A and not 0x0D);
    if (controlBytes >= 8 && controlBytes * 20 >= bytes.Length)
        return "opaque-binary: high control-byte density; not Pascal source text";

    var lines = source.Replace("\r\n", "\n").Replace('\r', '\n')
        .Split('\n')
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .ToArray();
    if (lines.Length > 1 && lines[0].TrimStart().StartsWith("#", StringComparison.Ordinal) &&
        lines.Any(line => Regex.IsMatch(line, "^\\s*\\d+\\s+")) &&
        lines.All(line => line.TrimStart().StartsWith("#", StringComparison.Ordinal) ||
                          Regex.IsMatch(line, "^\\s*\\d+\\s+")))
        return "tabular-data: hash-comment header followed by numeric rows";

    return null;
}

static SourceClassification[] ClassifyScriptSources(string[] files, string envirDirectory,
    string sourceRoot, Dictionary<string, HashSet<string>> includeClosures, MethodInfo readAllText)
{
    var includedFiles = new HashSet<string>(includeClosures.Values.SelectMany(paths => paths)
        .Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
    var referenceCandidates = files.Where(file =>
            includedFiles.Contains(Path.GetFullPath(file)) || IsExplicitCopyArtifact(file))
        .ToArray();
    var literalReferences = FindLiteralRuntimeReferences(referenceCandidates,
        envirDirectory, sourceRoot, readAllText);
    var registeredNpcScripts = FindRegisteredNpcScriptFiles(envirDirectory,
        path => (string)readAllText.Invoke(null, new object[] { path })!);
    return ClassifyScriptSourcesCore(files, envirDirectory, includeClosures, literalReferences,
        registeredNpcScripts);
}

static SourceClassification[] ClassifyScriptSourcesCore(IReadOnlyList<string> files, string root,
    IReadOnlyDictionary<string, HashSet<string>> includeClosures,
    IReadOnlyDictionary<string, string[]> literalReferences,
    IReadOnlySet<string> registeredNpcScripts)
{
    var fullFiles = files.Select(Path.GetFullPath).ToArray();
    var knownFiles = new HashSet<string>(fullFiles, StringComparer.OrdinalIgnoreCase);
    var registeredFiles = new HashSet<string>(registeredNpcScripts.Select(Path.GetFullPath)
        .Where(knownFiles.Contains), StringComparer.OrdinalIgnoreCase);
    var includedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in fullFiles)
    {
        if (!includeClosures.TryGetValue(entry, out var closure)) continue;
        foreach (var included in closure.Select(Path.GetFullPath))
        {
            if (!knownFiles.Contains(included) || included.Equals(entry, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!includedBy.TryGetValue(included, out var includers))
            {
                includers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                includedBy[included] = includers;
            }
            includers.Add(entry);
        }
    }

    var roots = fullFiles.Where(file => !includedBy.ContainsKey(file)).ToArray();
    var unreachableRoots = new HashSet<string>(roots.Where(file =>
        IsExplicitCopyArtifact(file) &&
        !registeredFiles.Contains(file) &&
        (!literalReferences.TryGetValue(file, out var references) || references.Length == 0)),
        StringComparer.OrdinalIgnoreCase);
    var orphanRoots = new HashSet<string>(roots.Where(file =>
        IsPsNpcScriptFile(file, root) &&
        !registeredFiles.Contains(file) &&
        (!literalReferences.TryGetValue(file, out var references) || references.Length == 0)),
        StringComparer.OrdinalIgnoreCase);
    var runtimeRoots = new HashSet<string>(roots.Where(file =>
            !unreachableRoots.Contains(file) && !orphanRoots.Contains(file)),
        StringComparer.OrdinalIgnoreCase);
    runtimeRoots.UnionWith(registeredFiles);
    runtimeRoots.UnionWith(literalReferences.Where(pair => pair.Value.Length > 0)
        .Select(pair => Path.GetFullPath(pair.Key)));

    return fullFiles.Select(file =>
    {
        var references = literalReferences.TryGetValue(file, out var foundReferences)
            ? foundReferences
            : Array.Empty<string>();
        string role;
        string[] entryFiles;
        if (includedBy.TryGetValue(file, out var includers) && references.Length == 0 &&
            !registeredFiles.Contains(file))
        {
            role = "include-fragment";
            entryFiles = includers.Where(runtimeRoots.Contains)
                .Select(path => Relative(path, root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        else if (orphanRoots.Contains(file))
        {
            role = "unregistered-orphan";
            entryFiles = Array.Empty<string>();
        }
        else if (unreachableRoots.Contains(file))
        {
            role = "unreachable-copy";
            entryFiles = Array.Empty<string>();
        }
        else
        {
            role = "runtime-entry";
            entryFiles = includedBy.TryGetValue(file, out includers)
                ? includers.Where(runtimeRoots.Contains)
                    .Append(file)
                    .Select(path => Relative(path, root))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : new[] { Relative(file, root) };
        }

        return new SourceClassification(Relative(file, root), role, entryFiles, references);
    }).ToArray();
}

static HashSet<string> FindRegisteredNpcScriptFiles(string envirDirectory,
    Func<string, string> readAllText)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var fileName in new[] { "PsNpcScript.txt", "PsNpcScriptEx.txt" })
    {
        var path = Path.Combine(envirDirectory, fileName);
        if (!File.Exists(path)) continue;
        var source = readAllText(path);
        foreach (var line in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var parts = SplitNpcScriptRegistrationLine(line);
            if (parts.Length != 9) continue;
            var scriptName = CleanNpcScriptRegistrationToken(parts[0]);
            var mapName = CleanNpcScriptRegistrationToken(parts[1]);
            var resolved = ResolveRegisteredNpcScriptPath(envirDirectory, scriptName, mapName);
            if (resolved != null) result.Add(Path.GetFullPath(resolved));
        }
    }
    return result;
}

static string[] SplitNpcScriptRegistrationLine(string line)
{
    line = (line ?? string.Empty).Trim();
    if (line.Length == 0 || line.StartsWith(';')) return Array.Empty<string>();
    var comment = line.IndexOf("//", StringComparison.Ordinal);
    if (comment >= 0) line = line.Substring(0, comment).Trim();
    if (line.Length == 0) return Array.Empty<string>();

    var parts = new List<string>();
    var token = new StringBuilder();
    var quote = '\0';
    foreach (var character in line)
    {
        if (quote != '\0')
        {
            token.Append(character);
            if (character == quote) quote = '\0';
            continue;
        }
        if (character is '"' or '\'')
        {
            quote = character;
            token.Append(character);
            continue;
        }
        if (char.IsWhiteSpace(character))
        {
            if (token.Length > 0)
            {
                parts.Add(token.ToString());
                token.Clear();
            }
            continue;
        }
        token.Append(character);
    }
    if (token.Length > 0) parts.Add(token.ToString());
    return parts.ToArray();
}

static string CleanNpcScriptRegistrationToken(string value)
{
    value = (value ?? string.Empty).Trim();
    if (value.Length >= 2 &&
        (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
        value = value.Substring(1, value.Length - 2).Trim();
    return value;
}

static string? ResolveRegisteredNpcScriptPath(string envirDirectory, string scriptName,
    string mapName)
{
    scriptName = CleanNpcScriptRegistrationToken(scriptName);
    mapName = CleanNpcScriptRegistrationToken(mapName);
    if (string.IsNullOrWhiteSpace(scriptName)) return null;

    var names = string.IsNullOrWhiteSpace(mapName)
        ? new[] { scriptName }
        : new[] { scriptName + "-" + mapName, scriptName };
    foreach (var name in names)
    {
        if (Path.IsPathRooted(name) &&
            Path.GetExtension(name).Equals(".pas", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(name))
            return name;

        var hasExtension = !string.IsNullOrEmpty(Path.GetExtension(name));
        foreach (var directory in new[]
                 {
                     Path.Combine(envirDirectory, "PsNpcscripts"),
                     Path.Combine(envirDirectory, "CommonScripts"),
                     envirDirectory
                 })
        {
            var path = Path.Combine(directory, name);
            if (hasExtension &&
                Path.GetExtension(path).Equals(".pas", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
                return path;
            if (!hasExtension && File.Exists(path + ".pas")) return path + ".pas";
        }
    }
    return null;
}

static bool IsPsNpcScriptFile(string file, string root) =>
    Relative(file, root).StartsWith("PsNpcscripts/", StringComparison.OrdinalIgnoreCase);

static Dictionary<string, string[]> FindLiteralRuntimeReferences(string[] candidates,
    string envirDirectory, string sourceRoot, MethodInfo readAllText)
{
    var references = candidates.ToDictionary(Path.GetFullPath,
        _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        StringComparer.OrdinalIgnoreCase);
    if (references.Count == 0)
        return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    var configExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".ini", ".cfg", ".conf", ".csv", ".lst", ".dat", ".json" };
    var gameSvrSource = Path.Combine(sourceRoot, "GameSvr");
    var csharpFiles = Directory.Exists(gameSvrSource)
        ? Directory.GetFiles(gameSvrSource, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactPath(path))
        : Array.Empty<string>();
    var referenceFiles = Directory.GetFiles(envirDirectory, "*", SearchOption.AllDirectories)
        .Where(path => !path.EndsWith(".pas", StringComparison.OrdinalIgnoreCase) &&
                       configExtensions.Contains(Path.GetExtension(path)))
        .Concat(csharpFiles)
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    foreach (var referenceFile in referenceFiles)
    {
        string source;
        try
        {
            source = referenceFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? File.ReadAllText(referenceFile)
                : (string)readAllText.Invoke(null, new object[] { referenceFile })!;
        }
        catch
        {
            continue;
        }

        foreach (var candidate in references.Keys)
        {
            if (referenceFile.Equals(candidate, StringComparison.OrdinalIgnoreCase)) continue;
            var scriptName = Path.GetFileNameWithoutExtension(candidate);
            var found = referenceFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? ContainsCSharpRuntimeReference(source, scriptName)
                : ContainsConfigScriptReference(source, scriptName);
            if (!found) continue;
            references[candidate].Add(ReferenceName(referenceFile, envirDirectory, sourceRoot));
        }
    }

    return references.ToDictionary(pair => pair.Key,
        pair => pair.Value.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
        StringComparer.OrdinalIgnoreCase);
}

static bool ContainsCSharpRuntimeReference(string source, string scriptName)
{
    return Regex.IsMatch(source,
        $@"\b[A-Za-z_][A-Za-z0-9_]*(?:ScriptFile|ScriptLabel)[A-Za-z0-9_]*\s*\(\s*" +
        $@"[\""'](?:[^\""'\r\n]*[\\/])?{Regex.Escape(scriptName)}(?:\.pas)?[\""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

static bool ContainsQuotedScriptReference(string source, string scriptName)
{
    return Regex.IsMatch(source,
        $@"[\""'](?:[^\""'\r\n]*[\\/])?{Regex.Escape(scriptName)}(?:\.pas)?[\""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

static bool ContainsConfigScriptReference(string source, string scriptName)
{
    if (ContainsQuotedScriptReference(source, scriptName)) return true;
    return Regex.IsMatch(source,
        $@"(?<![^\s,;=]){Regex.Escape(scriptName)}(?:\.pas)?(?![^\s,;=])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

static string ReferenceName(string path, string envirDirectory, string sourceRoot)
{
    var fullPath = Path.GetFullPath(path);
    var envirRoot = Path.GetFullPath(envirDirectory).TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
    if (fullPath.StartsWith(envirRoot, StringComparison.OrdinalIgnoreCase))
        return "envir:" + Relative(fullPath, envirDirectory);
    return "source:" + Relative(fullPath, sourceRoot);
}

static bool IsBuildArtifactPath(string path)
{
    var segments = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);
    return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                   segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}

static bool IsExplicitCopyArtifact(string path)
{
    var name = Path.GetFileNameWithoutExtension(path);
    return Regex.IsMatch(name, @"(?:\s+-\s*|\s+)副本(?:\s*\(\d+\)|\s+\d+)?$",
        RegexOptions.CultureInvariant);
}

static void RunScriptSourceClassificationRegression()
{
    var root = Path.Combine(Path.GetTempPath(), "loym2-pas-source-role");
    var entry = Path.Combine(root, "entry.pas");
    var fragment = Path.Combine(root, "fragment.pas");
    var nested = Path.Combine(root, "nested.pas");
    var copy = Path.Combine(root, "shop - 副本.pas");
    var configuredCopy = Path.Combine(root, "configured - 副本.pas");
    var legitimateDungeon = Path.Combine(root, "活动副本.pas");
    var files = new[] { entry, fragment, nested, copy, configuredCopy, legitimateDungeon };
    var closures = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        [entry] = new(StringComparer.OrdinalIgnoreCase) { fragment, nested },
        [fragment] = new(StringComparer.OrdinalIgnoreCase) { nested },
        [nested] = new(StringComparer.OrdinalIgnoreCase),
        [copy] = new(StringComparer.OrdinalIgnoreCase),
        [configuredCopy] = new(StringComparer.OrdinalIgnoreCase),
        [legitimateDungeon] = new(StringComparer.OrdinalIgnoreCase)
    };
    var references = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [configuredCopy] = new[] { "envir:PsNpcScript.txt" }
    };
    var actual = ClassifyScriptSourcesCore(files, root, closures, references,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase))
        .ToDictionary(item => item.File, StringComparer.OrdinalIgnoreCase);

    AssertRole("entry.pas", "runtime-entry");
    AssertRole("fragment.pas", "include-fragment");
    AssertRole("nested.pas", "include-fragment");
    AssertRole("shop - 副本.pas", "unreachable-copy");
    AssertRole("configured - 副本.pas", "runtime-entry");
    AssertRole("活动副本.pas", "runtime-entry");
    if (!actual["nested.pas"].EntryFiles.SequenceEqual(new[] { "entry.pas" }))
        throw new InvalidOperationException("Script source classification reverse include closure failed");
    if (!ContainsCSharpRuntimeReference("host.FindScriptFile(\"onLogin\")", "onLogin") ||
        ContainsCSharpRuntimeReference("var core = \"core\";", "core") ||
        !ContainsConfigScriptReference("shop - 副本.pas", "shop - 副本"))
        throw new InvalidOperationException("Script runtime reference classification failed");
    Console.WriteLine("script-source-classification-regression=PASS");

    void AssertRole(string file, string expected)
    {
        if (!actual.TryGetValue(file, out var classification) || classification.Role != expected)
            throw new InvalidOperationException(
                $"Script source classification failed: {file} expected={expected}");
    }
}

static void RunNpcScriptRegistrationRegression()
{
    var root = Path.Combine(Path.GetTempPath(),
        "loym2-pas-npc-registration-" + Guid.NewGuid().ToString("N"));
    var npcDirectory = Path.Combine(root, "PsNpcscripts");
    Directory.CreateDirectory(npcDirectory);
    try
    {
        var entry = Path.Combine(root, "entry.pas");
        var fragment = Path.Combine(npcDirectory, "fragment.pas");
        var generic = Path.Combine(npcDirectory, "shop.pas");
        var mapSpecific = Path.Combine(npcDirectory, "shop-G001.pas");
        var fallback = Path.Combine(npcDirectory, "fallback.pas");
        var exOnly = Path.Combine(npcDirectory, "ex-only.pas");
        var invalid = Path.Combine(npcDirectory, "eight-fields.pas");
        var orphan = Path.Combine(npcDirectory, "orphan.pas");
        var files = new[]
        {
            entry, fragment, generic, mapSpecific, fallback, exOnly, invalid, orphan
        };
        foreach (var file in files) File.WriteAllText(file, "begin end.");
        File.WriteAllText(Path.Combine(root, "PsNpcScript.txt"), """
            shop G001 1 2 "Shop Keeper" 0 1 0 0 // map-specific wins
            fallback G002 1 2 Fallback 0 1 0 0
            eight-fields G003 1 2 Invalid 0 1 0
            """);
        File.WriteAllText(Path.Combine(root, "PsNpcScriptEx.txt"),
            "ex-only G004 1 2 ExOnly 0 1 0 0");

        var registered = FindRegisteredNpcScriptFiles(root, File.ReadAllText);
        var closures = files.ToDictionary(Path.GetFullPath,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        closures[entry].Add(fragment);
        var actual = ClassifyScriptSourcesCore(files, root, closures,
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase), registered)
            .ToDictionary(item => item.File, StringComparer.OrdinalIgnoreCase);

        AssertRole(entry, "runtime-entry");
        AssertRole(fragment, "include-fragment");
        AssertRole(mapSpecific, "runtime-entry");
        AssertRole(fallback, "runtime-entry");
        AssertRole(exOnly, "runtime-entry");
        AssertRole(generic, "unregistered-orphan");
        AssertRole(invalid, "unregistered-orphan");
        AssertRole(orphan, "unregistered-orphan");
        var parsedFixture = new[]
        {
            WithUnsupported(mapSpecific, "RuntimeClosed", 1),
            WithUnsupported(generic, "ChgMonItemPercent", 6)
        };
        var runtimeUnsupported = SummarizeUnsupported(parsedFixture.Where(script =>
            actual[script.File].Role == "runtime-entry"));
        var orphanUnsupported = SummarizeUnsupported(parsedFixture.Where(script =>
            actual[script.File].Role == "unregistered-orphan"));
        var orphanChgMonItemPercent = orphanUnsupported.SingleOrDefault(item =>
            item.Name.Equals("ChgMonItemPercent", StringComparison.OrdinalIgnoreCase));
        if (registered.Contains(generic) || !registered.Contains(mapSpecific) ||
            !registered.Contains(fallback) || !registered.Contains(exOnly) ||
            registered.Contains(invalid) ||
            runtimeUnsupported.Any(item => item.Name.Equals("ChgMonItemPercent",
                StringComparison.OrdinalIgnoreCase)) ||
            orphanChgMonItemPercent?.Count != 6)
            throw new InvalidOperationException(
                "PsNpcScript registration parsing or map precedence regression failed");
        Console.WriteLine(
            "npc-script-registration-regression=PASS fields=9 map=exact-first orphan=isolated");

        void AssertRole(string file, string expected)
        {
            var relative = Relative(file, root);
            if (!actual.TryGetValue(relative, out var classification) ||
                classification.Role != expected)
                throw new InvalidOperationException(
                    $"NPC script registration role failed: {relative} expected={expected}");
        }

        ParsedScript WithUnsupported(string file, string name, int count)
        {
            var relative = Relative(file, root);
            return new ParsedScript(relative, count, Array.Empty<SymbolUse>(),
                Array.Empty<SymbolUse>(),
                new[] { new SymbolUse(relative, "global-call", name, count) },
                Array.Empty<SymbolUse>(), Array.Empty<ConditionalDispatchUse>());
        }
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void RunIncludeResolverRegression(MethodInfo resolveInclude)
{
    var root = Path.Combine(Path.GetTempPath(), "loym2-pas-include-" + Guid.NewGuid().ToString("N"));
    var current = Path.Combine(root, "PsMapQuest");
    var common = Path.Combine(root, "CommonScripts");
    var npc = Path.Combine(root, "PsNpcscripts");
    Directory.CreateDirectory(current);
    Directory.CreateDirectory(common);
    Directory.CreateDirectory(npc);
    try
    {
        var localPath = Path.Combine(current, "shared.inc");
        var commonPath = Path.Combine(common, "shared.inc");
        var rootPath = Path.Combine(root, "root.inc");
        var npcPath = Path.Combine(npc, "npc.inc");
        File.WriteAllText(localPath, "local");
        File.WriteAllText(commonPath, "common");
        File.WriteAllText(rootPath, "root");
        File.WriteAllText(npcPath, "npc");

        AssertResolved(localPath, "shared.inc");
        File.Delete(localPath);
        AssertResolved(commonPath, "shared.inc");
        AssertResolved(rootPath, "root.inc");
        AssertResolved(npcPath, "npc.inc");
        AssertResolved(null, "missing.inc");

        var binaryPath = Path.Combine(root, "encrypted.pas");
        var binaryBytes = Enumerable.Repeat((byte)0x01, 32)
            .Concat(Enumerable.Repeat((byte)0x41, 32)).ToArray();
        File.WriteAllBytes(binaryPath, binaryBytes);
        AssertClassification("opaque-binary", binaryPath, new string('A', 64));

        var tablePath = Path.Combine(root, "table.pas");
        const string tableSource = "# id name\n1 title\n2 title2\n";
        File.WriteAllText(tablePath, tableSource);
        AssertClassification("tabular-data", tablePath, tableSource);

        var scriptPath = Path.Combine(root, "script.pas");
        const string scriptSource = "procedure Test; begin end;";
        File.WriteAllText(scriptPath, scriptSource);
        AssertClassification(null, scriptPath, scriptSource);
        Console.WriteLine("include-resolver-regression=PASS");
    }
    finally
    {
        Directory.Delete(root, true);
    }

    void AssertResolved(string? expected, string includeName)
    {
        var actual = (string?)resolveInclude.Invoke(null, new object[] { includeName, current, root });
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Include resolver regression: {includeName} expected={expected ?? "<missing>"} actual={actual ?? "<missing>"}");
    }

    static void AssertClassification(string? expectedPrefix, string path, string source)
    {
        var actual = ClassifyNonSourceArtifact(path, source);
        var matches = expectedPrefix == null ? actual == null :
            actual?.StartsWith(expectedPrefix, StringComparison.Ordinal) == true;
        if (!matches)
            throw new InvalidOperationException(
                $"PAS artifact classification regression: {Path.GetFileName(path)} expected={expectedPrefix ?? "<source>"} actual={actual ?? "<source>"}");
    }
}

static void RunPlayerMemberSurfaceRegression(Type lexerType, Type parserType,
    MethodInfo setDefines, MethodInfo parse, ApiSurface api)
{
    const string source = """
        program PlayerMemberSurface;
        begin
          This_Player.DynRoomIdx := This_Player.DynRoomIdx;
          This_Player.CallOutParam := This_Player.CallOutParam;
        end.
        """;
    var lexer = Activator.CreateInstance(lexerType, source)!;
    setDefines.Invoke(lexer, new object[] { new HashSet<string>(StringComparer.OrdinalIgnoreCase) });
    var parser = Activator.CreateInstance(parserType, lexer, string.Empty)!;
    var program = parse.Invoke(parser, null)!;
    var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Result", "This_Player"
    };
    var calls = new List<CallUse>();
    Walk(program, false, calls, declared,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase), api);

    AssertUse("player-member-read", "DynRoomIdx", true);
    AssertUse("player-member-write", "DynRoomIdx", false);
    AssertUse("player-member-read", "CallOutParam", true);
    AssertUse("player-member-write", "CallOutParam", true);
    Console.WriteLine("player-member-surface-regression=PASS");

    void AssertUse(string kind, string name, bool resolved)
    {
        var matches = calls.Where(call => call.Kind == kind &&
            call.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Resolved != resolved)
            throw new InvalidOperationException(
                $"Player member surface regression: {kind} {name} expected resolved={resolved}; " +
                $"matches={string.Join(",", matches.Select(call => call.Resolved))}; " +
                $"calls={string.Join(" | ", calls.Select(call => $"{call.Kind}:{call.Name}:{call.Resolved}"))}");
    }
}

static void RunSyntheticStateSurfaceRegression()
{
    const string source = """
        public bool CallPlayerFunc(string name, List<PasValue> args, out PasValue result)
        {
            switch (name)
            {
                case "shadowedbalance": result = GetPlayerVar('V', 10, 1); break;
                default: return false;
            }
            return true;
        }

        public bool GetPlayerProperty(string name, out PasValue result)
        {
            switch (name)
            {
                case "shadowedbalance": result = PasValue.FromInt(7); break;
                case "propertybalance": result = GetPlayerVar('V', 10, 2); break;
                case "getv": result = GetPlayerVar('V', 1, 1); break;
                default: return false;
            }
            return true;
        }
        """;
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var findings = new List<SourceFinding>();
    var unsupportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var unsupportedFindings = new List<SourceFinding>();
    var supportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var syntheticKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var syntheticFindings = new List<SourceFinding>();
    ReadPlaceholderSource(source, "regression.cs", names, findings,
        unsupportedKeys, unsupportedFindings, supportedKeys, syntheticKeys, syntheticFindings);
    unsupportedKeys.ExceptWith(supportedKeys);
    var api = ReadApiSurface(source, string.Empty);
    var objectTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var shadowed = ResolveMember("This_Player", "shadowedbalance", false, false, objectTypes, api);
    var propertyOnly = ResolveMember("This_Player", "propertybalance", false, false, objectTypes, api);
    if (!syntheticKeys.Contains("player-func\0shadowedbalance") ||
        syntheticKeys.Contains("player-member-read\0shadowedbalance") ||
        !syntheticKeys.Contains("player-member-read\0propertybalance") ||
        syntheticKeys.Any(key => key.EndsWith("\0getv", StringComparison.OrdinalIgnoreCase)) ||
        syntheticFindings.Count != 2 ||
        shadowed is not { Kind: "player-member-read", Resolved: true, Surface: "player-func" } ||
        !IsSyntheticStateCall(shadowed, syntheticKeys, api) ||
        propertyOnly is not { Kind: "player-member-read", Resolved: true, Surface: "player-member-read" } ||
        !IsSyntheticStateCall(propertyOnly, syntheticKeys, api))
        throw new InvalidOperationException("Synthetic player state surface regression failed");
    Console.WriteLine("synthetic-state-surface-regression=PASS");
}

static void RunUnsupportedOverloadSurfaceRegression()
{
    const string source = """
        public bool CallPlayerFunc(string name, List<PasValue> args, out PasValue result)
        {
            switch (name)
            {
                case "procedureonly":
                    return RejectUnsupportedNativeApi(out result);
                case "functiononly":
                    if (args.Count != 1)
                        return RejectUnsupportedNativeApi(out result);
                    result = PasValue.FromInt(1);
                    return true;
                case "getsigninactprizer":
                    if (args.Count != 2)
                        return RejectUnsupportedNativeApi(out result);
                    if (IsYanshenSignInTunnelCall(args))
                        return TryCallYanshenSignInTunnel(args, out result);
                    result = PasValue.FromString("native");
                    return true;
                case "closedfunc":
                    return RejectUnsupportedNativeApi(out result);
                case "propertyfallback":
                    return RejectUnsupportedNativeApi(out result);
                default: return false;
            }
            return true;
        }

        public bool CallPlayerMethod(string name, List<PasValue> args)
        {
            switch (name)
            {
                case "procedureonly":
                    return true;
                case "functiononly":
                    return RejectUnsupportedNativeApi();
                case "getsigninactprizer":
                    return RejectUnsupportedNativeApi();
                case "closedmethod":
                    return RejectUnsupportedNativeApi();
                case "propertyfallback":
                    return RejectUnsupportedNativeApi();
                default: return false;
            }
        }

        public bool GetPlayerProperty(string name, out PasValue result)
        {
            switch (name)
            {
                case "propertyfallback": result = PasValue.FromInt(1); return true;
                default: result = PasValue.Nil; return false;
            }
        }

        public bool CallNpcFunc(string name, List<PasValue> args, out PasValue result)
        {
            switch (name)
            {
                case "npcmethodfallback":
                case "npcpropertyfallback":
                case "npcclosed":
                    return RejectUnsupportedNativeApi(out result);
                default: return false;
            }
        }

        public bool CallNpcMethod(string name, List<PasValue> args, out PasValue result)
        {
            switch (name)
            {
                case "npcmethodfallback": result = PasValue.Nil; return true;
                case "npcpropertyfallback": return RejectUnsupportedNativeApi(out result);
                default: result = PasValue.Nil; return false;
            }
        }

        public bool GetNpcProperty(string name, out PasValue result)
        {
            switch (name)
            {
                case "npcpropertyfallback": result = PasValue.FromInt(1); return true;
                default: result = PasValue.Nil; return false;
            }
        }
        """;
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var findings = new List<SourceFinding>();
    var unsupportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var unsupportedFindings = new List<SourceFinding>();
    var supportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var syntheticKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var syntheticFindings = new List<SourceFinding>();
    ReadPlaceholderSource(source, "regression.cs", names, findings,
        unsupportedKeys, unsupportedFindings, supportedKeys, syntheticKeys, syntheticFindings);
    unsupportedKeys.ExceptWith(supportedKeys);
    var api = ReadApiSurface(source, string.Empty);
    var objectTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var procedureCall = ResolveMember("This_Player", "procedureonly", true, true,
        objectTypes, api);
    var procedureAsFunction = ResolveMember("This_Player", "procedureonly", true, false,
        objectTypes, api);
    var functionCall = ResolveMember("This_Player", "functiononly", true, false,
        objectTypes, api);
    var functionAsProcedure = ResolveMember("This_Player", "functiononly", true, true,
        objectTypes, api);
    var signInFunction = ResolveMember("This_Player", "getsigninactprizer", true, false,
        objectTypes, api);
    var signInStatement = ResolveMember("This_Player", "getsigninactprizer", true, true,
        objectTypes, api);
    var closedFunction = ResolveMember("This_Player", "closedfunc", true, false,
        objectTypes, api);
    var closedMethod = ResolveMember("This_Player", "closedmethod", true, true,
        objectTypes, api);
    var bareFallback = ResolveMember("This_Player", "procedureonly", false, false,
        objectTypes, api);
    var propertyFallback = ResolveMember("This_Player", "propertyfallback", false, false,
        objectTypes, api);
    var npcMethodFallback = ResolveMember("This_Npc", "npcmethodfallback", false, false,
        objectTypes, api);
    var npcPropertyFallback = ResolveMember("This_Npc", "npcpropertyfallback", false, false,
        objectTypes, api);
    var npcClosed = ResolveMember("This_Npc", "npcclosed", false, false,
        objectTypes, api);
    if (IsRejectedCall(procedureCall, unsupportedKeys, api) ||
        IsRejectedCall(procedureAsFunction, unsupportedKeys, api) ||
        IsRejectedCall(functionCall, unsupportedKeys, api) ||
        IsRejectedCall(functionAsProcedure, unsupportedKeys, api) ||
        IsRejectedCall(signInFunction, unsupportedKeys, api) ||
        IsRejectedCall(signInStatement, unsupportedKeys, api) ||
        unsupportedKeys.Contains("player-func\0functiononly") ||
        !unsupportedKeys.Contains("player-method\0functiononly") ||
        unsupportedKeys.Contains("player-func\0getsigninactprizer") ||
        !unsupportedKeys.Contains("player-method\0getsigninactprizer") ||
        IsRejectedCall(bareFallback, unsupportedKeys, api) ||
        IsRejectedCall(propertyFallback, unsupportedKeys, api) ||
        IsRejectedCall(npcMethodFallback, unsupportedKeys, api) ||
        IsRejectedCall(npcPropertyFallback, unsupportedKeys, api) ||
        !IsRejectedCall(npcClosed, unsupportedKeys, api) ||
        !IsRejectedCall(closedFunction, unsupportedKeys, api) ||
        !IsRejectedCall(closedMethod, unsupportedKeys, api))
        throw new InvalidOperationException("Unsupported overload surface regression failed");
    Console.WriteLine("unsupported-overload-surface-regression=PASS function=available method=reject fallback=function-first");
}

static void RunCrossSourceSurfaceMergeRegression()
{
    const string supportedSource = """
        public bool CallPlayerFunc(string name, List<PasValue> args, out PasValue result)
        {
            switch (name)
            {
                case "crosssource":
                    result = PasValue.FromInt(1);
                    return true;
                default: return false;
            }
        }
        """;
    const string rejectedSource = """
        public bool CallPlayerFunc(string name, List<PasValue> args, out PasValue result)
        {
            switch (name)
            {
                case "crosssource":
                case "closedsource":
                    return RejectUnsupportedNativeApi(out result);
                default: return false;
            }
        }
        """;

    AssertOrder(supportedSource, rejectedSource);
    AssertOrder(rejectedSource, supportedSource);
    Console.WriteLine("cross-source-surface-merge-regression=PASS order=both");

    static void AssertOrder(params string[] sources)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<SourceFinding>();
        var unsupportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsupportedFindings = new List<SourceFinding>();
        var supportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var syntheticKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var syntheticFindings = new List<SourceFinding>();
        for (var index = 0; index < sources.Length; index++)
            ReadPlaceholderSource(sources[index], $"source-{index}.cs", names, findings,
                unsupportedKeys, unsupportedFindings, supportedKeys,
                syntheticKeys, syntheticFindings);
        unsupportedKeys.ExceptWith(supportedKeys);
        if (unsupportedKeys.Contains("player-func\0crosssource") ||
            !unsupportedKeys.Contains("player-func\0closedsource"))
            throw new InvalidOperationException(
                "Cross-source supported/unsupported surface merge is order-dependent");
    }
}

static void RunGlobalSurfaceRegression()
{
    var builtins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "builtin" };
    var procedures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "procedure" };
    var standalone = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "standalone" };
    var playerFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "playeronly" };
    var supported = BuildSupportedGlobalSurface(builtins, procedures, standalone,
        Array.Empty<string>());
    if (!supported.SetEquals(new[] { "builtin", "procedure", "standalone" }) ||
        supported.Overlaps(playerFunctions))
        throw new InvalidOperationException("PAS global surface admitted a player-only function");
    Console.WriteLine("global-surface-regression=PASS");
}

static void RunRuntimeDispatchOrderRegression(string sourceRoot)
{
    var source = File.ReadAllText(Path.Combine(sourceRoot, "GameSvr",
        "ScriptSystem", "PasEngine", "PasInterpreter.cs"));
    foreach (var expected in new[]
             {
                 "_api.CallPlayerFunc(method, args, out result) || _api.CallPlayerMethod(method, args)",
                 "_api.CallNpcFunc(method, args, out result) || _api.CallNpcMethod(method, args, out result)"
             })
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "PAS runtime function-to-method fallback order changed: " + expected);
    }
    Console.WriteLine("runtime-dispatch-order-regression=PASS");
}

static string PreprocessForAudit(string source, string baseDirectory, string envirDirectory,
    MethodInfo readAllText, MethodInfo resolveInclude, HashSet<string> scriptDefines,
    HashSet<string> visited)
{
    var output = new StringBuilder(source.Length);
    var stack = new List<AuditPreprocessorFrame>();
    foreach (var line in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
    {
        var trimmed = line.Trim();
        var closeAt = trimmed.StartsWith("{$", StringComparison.Ordinal) ? trimmed.IndexOf('}') : -1;
        var trailing = closeAt >= 0 ? trimmed.Substring(closeAt + 1).TrimStart() : string.Empty;
        if (closeAt < 2 || trailing.Length > 0 && !trailing.StartsWith("//", StringComparison.Ordinal))
        {
            if (stack.Count == 0 || stack[^1].Active) output.AppendLine(line);
            continue;
        }

        var directive = trimmed.Substring(2, closeAt - 2).Trim();
        if (directive.StartsWith("IFDEF", StringComparison.OrdinalIgnoreCase))
        {
            stack.Add(new AuditPreprocessorFrame(stack.Count == 0 || stack[^1].Active,
                scriptDefines.Contains(directive.Substring(5).Trim())));
            continue;
        }
        if (directive.StartsWith("IFNDEF", StringComparison.OrdinalIgnoreCase))
        {
            stack.Add(new AuditPreprocessorFrame(stack.Count == 0 || stack[^1].Active,
                !scriptDefines.Contains(directive.Substring(6).Trim())));
            continue;
        }
        if (directive.Equals("ELSE", StringComparison.OrdinalIgnoreCase) ||
            directive.StartsWith("ELSE ", StringComparison.OrdinalIgnoreCase))
        {
            if (stack.Count > 0 && !stack[^1].ElseSeen)
            {
                stack[^1].ElseSeen = true;
                stack[^1].ConditionActive = !stack[^1].ConditionActive;
            }
            continue;
        }
        if (directive.StartsWith("ENDIF", StringComparison.OrdinalIgnoreCase))
        {
            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            continue;
        }
        if (stack.Count > 0 && !stack[^1].Active) continue;
        if (!directive.StartsWith("I ", StringComparison.OrdinalIgnoreCase) &&
            !directive.StartsWith("INCLUDE ", StringComparison.OrdinalIgnoreCase)) continue;

        var includeName = directive.Substring(directive.IndexOf(' ') + 1).Trim().Trim('\'', '"')
            .Replace('/', Path.DirectorySeparatorChar);
        var includePath = (string?)resolveInclude.Invoke(null,
            new object[] { includeName, baseDirectory, envirDirectory });
        if (includePath == null)
            throw new FileNotFoundException($"Include not found: {includeName} (from {baseDirectory})");
        if (!visited.Add(includePath)) continue;
        var includeSource = (string)readAllText.Invoke(null, new object[] { includePath })!;
        output.Append(PreprocessForAudit(includeSource, Path.GetDirectoryName(includePath)!, envirDirectory,
            readAllText, resolveInclude, scriptDefines, visited));
    }
    return output.ToString();
}

static HashSet<string> ReadCompilerDefines(string envirDirectory, MethodInfo readAllText)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VER150" };
    var path = Path.Combine(envirDirectory, "CommonScripts", "Compiler.inc");
    if (!File.Exists(path)) return result;

    var source = (string)readAllText.Invoke(null, new object[] { path })!;
    foreach (var rawLine in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) ||
            line.StartsWith(";", StringComparison.Ordinal))
            continue;

        var tokenLength = 0;
        while (tokenLength < line.Length &&
               (char.IsLetterOrDigit(line[tokenLength]) || line[tokenLength] == '_'))
            tokenLength++;
        if (tokenLength == 0 || char.IsDigit(line[0])) continue;

        var remainder = line.Substring(tokenLength).TrimStart();
        if (remainder.Length == 0 || remainder.StartsWith("//", StringComparison.Ordinal))
            result.Add(line.Substring(0, tokenLength));
    }
    return result;
}

static ParsedScript InspectProgram(string file, string root, object program,
    HashSet<string> builtinFunctions, HashSet<string> builtinProcedures,
    ApiSurface api, PlaceholderSurface placeholders)
{
    var relative = Relative(file, root);
    var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Result", "This_Player", "This_Npc", "This_DB", "This_Item", "This_Animal",
        "ExceptionType", "ExceptionParam"
    };
    var objectTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var functionReturnTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var declaredProcedures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in ReadEnumerable(program, "Consts")) declared.Add(ReadString(item, "Name"));
    foreach (var item in ReadEnumerable(program, "GlobalVars")) AddDeclaration(item, declared, objectTypes);
    foreach (var proc in ReadEnumerable(program, "Procedures"))
    {
        var procName = ReadString(proc, "Name");
        declared.Add(procName);
        declaredProcedures.Add(procName);
        var returnType = ReadString(proc, "ReturnType");
        if (!string.IsNullOrWhiteSpace(returnType)) functionReturnTypes[procName] = returnType;
        foreach (var item in ReadEnumerable(proc, "Parameters")) AddDeclaration(item, declared, objectTypes);
        foreach (var item in ReadEnumerable(proc, "LocalVars")) AddDeclaration(item, declared, objectTypes);
    }

    var supportedGlobal = BuildSupportedGlobalSurface(builtinFunctions, builtinProcedures,
        api.Standalone, declaredProcedures);

    var calls = new List<CallUse>();
    Walk(program, false, calls, declared, objectTypes, functionReturnTypes, supportedGlobal, api);
    var unresolved = calls.Where(call => !call.Resolved)
        .GroupBy(call => call.Kind + "\0" + call.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => new SymbolUse(relative, group.First().Kind, group.First().Name, group.Count()))
        .ToArray();
    var usedPlaceholders = calls.Where(call => call.Resolved && placeholders.Names.Contains(call.Name))
        .GroupBy(call => call.Kind + "\0" + call.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => new SymbolUse(relative, group.First().Kind, group.First().Name, group.Count()))
        .ToArray();
    var usedUnsupported = calls.Where(call => call.Resolved &&
            IsRejectedCall(call, placeholders.UnsupportedKeys, api))
        .GroupBy(call => call.Kind + "\0" + call.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => new SymbolUse(relative, group.First().Kind, group.First().Name, group.Count()))
        .ToArray();
    var usedSyntheticState = calls.Where(call => call.Resolved &&
            IsSyntheticStateCall(call, placeholders.SyntheticStateKeys, api))
        .GroupBy(call => call.Kind + "\0" + call.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => new SymbolUse(relative, group.First().Kind, group.First().Name, group.Count()))
        .ToArray();
    var conditionalDispatch = calls.Where(call => call.Resolved &&
            call.Name.Equals("AddPlayerAbil", StringComparison.OrdinalIgnoreCase) ||
            call.Resolved && call.Name.Equals("AddHeroAbil", StringComparison.OrdinalIgnoreCase))
        .GroupBy(call => call.Kind + "\0" + call.Name + "\0" +
            (call.FirstLiteralInteger?.ToString() ?? "<dynamic>"),
            StringComparer.OrdinalIgnoreCase)
        .Select(group => new ConditionalDispatchUse(relative, group.First().Kind,
            group.First().Name, group.First().FirstLiteralInteger, group.Count()))
        .ToArray();
    return new ParsedScript(relative, calls.Count, unresolved, usedPlaceholders, usedUnsupported,
        usedSyntheticState, conditionalDispatch);
}

static HashSet<string> BuildSupportedGlobalSurface(IEnumerable<string> builtinFunctions,
    IEnumerable<string> builtinProcedures, IEnumerable<string> standaloneFunctions,
    IEnumerable<string> declaredProcedures)
{
    var result = new HashSet<string>(builtinFunctions, StringComparer.OrdinalIgnoreCase);
    result.UnionWith(builtinProcedures);
    result.UnionWith(standaloneFunctions);
    result.UnionWith(declaredProcedures);
    return result;
}

static bool IsRejectedCall(CallUse call, HashSet<string> unsupportedKeys,
    ApiSurface api)
{
    if (call.Kind == "player-call")
        return IsUnavailable("player-func", api.PlayerFunctions.Contains(call.Name)) &&
               IsUnavailable("player-method", api.PlayerMethods.Contains(call.Name));
    if (call.Kind == "player-member-read")
        return IsUnavailable("player-func", api.PlayerFunctions.Contains(call.Name)) &&
               IsUnavailable("player-method", api.PlayerMethods.Contains(call.Name)) &&
               IsUnavailable("player-member-read", api.PlayerReadableProperties.Contains(call.Name));
    if (call.Kind == "npc-call")
        return IsUnavailable("npc-func", api.NpcFunctions.Contains(call.Name)) &&
               IsUnavailable("npc-method", api.NpcMethods.Contains(call.Name));
    if (call.Kind == "npc-member")
        return IsUnavailable("npc-func", api.NpcFunctions.Contains(call.Name)) &&
               IsUnavailable("npc-method", api.NpcMethods.Contains(call.Name)) &&
               IsUnavailable("npc-member", api.NpcProperties.Contains(call.Name));
    return unsupportedKeys.Contains(call.Surface + "\0" + call.Name);

    bool IsUnavailable(string surface, bool exists) => !exists ||
        unsupportedKeys.Contains(surface + "\0" + call.Name);
}

static bool IsSyntheticStateCall(CallUse call, HashSet<string> syntheticKeys,
    ApiSurface api)
{
    if (call.Kind == "player-call")
        return FirstExistingSurfaceIsSynthetic(
            ("player-func", api.PlayerFunctions.Contains(call.Name)),
            ("player-method", api.PlayerMethods.Contains(call.Name)));
    if (call.Kind == "player-member-read")
        return FirstExistingSurfaceIsSynthetic(
            ("player-func", api.PlayerFunctions.Contains(call.Name)),
            ("player-method", api.PlayerMethods.Contains(call.Name)),
            ("player-member-read", api.PlayerReadableProperties.Contains(call.Name)));
    if (call.Kind == "npc-call")
        return FirstExistingSurfaceIsSynthetic(
            ("npc-func", api.NpcFunctions.Contains(call.Name)),
            ("npc-method", api.NpcMethods.Contains(call.Name)));
    if (call.Kind == "npc-member")
        return FirstExistingSurfaceIsSynthetic(
            ("npc-func", api.NpcFunctions.Contains(call.Name)),
            ("npc-method", api.NpcMethods.Contains(call.Name)),
            ("npc-member", api.NpcProperties.Contains(call.Name)));
    return syntheticKeys.Contains(call.Surface + "\0" + call.Name);

    bool FirstExistingSurfaceIsSynthetic(params (string Surface, bool Exists)[] surfaces)
    {
        foreach (var surface in surfaces)
        {
            if (surface.Exists)
                return syntheticKeys.Contains(surface.Surface + "\0" + call.Name);
        }
        return false;
    }
}

static void Walk(object? node, bool statementPosition, List<CallUse> calls,
    HashSet<string> declared, Dictionary<string, string> objectTypes,
    Dictionary<string, string> functionReturnTypes, HashSet<string> supportedGlobal, ApiSurface api)
{
    if (node == null) return;
    var type = node.GetType();
    var typeName = type.Name;
    if (typeName == "PasAssignStmt")
    {
        var target = ReadObject(node, "Target");
        if (target?.GetType().Name == "PasMemberAccessExpr")
        {
            calls.Add(ResolveMember(ResolveObjectName(target, objectTypes, functionReturnTypes),
                ReadString(target, "MemberName"), false, false, objectTypes, api, true));
            Walk(ReadObject(target, "Target"), false, calls, declared, objectTypes,
                functionReturnTypes, supportedGlobal, api);
        }
        else
        {
            Walk(target, false, calls, declared, objectTypes, functionReturnTypes, supportedGlobal, api);
        }
        Walk(ReadObject(node, "Value"), false, calls, declared, objectTypes,
            functionReturnTypes, supportedGlobal, api);
        return;
    }
    switch (typeName)
    {
        case "PasCallStmt":
        {
            var name = ReadString(node, "Name");
            var isMethod = ReadBool(node, "IsMethod");
            var objectName = ReadString(node, "ObjectName");
            var call = isMethod ? ResolveMember(objectName, name, true, true, objectTypes, api) :
                new CallUse("global-call", name, supportedGlobal.Contains(name), "global");
            calls.Add(call with { FirstLiteralInteger = ReadFirstLiteralInteger(node) });
            break;
        }
        case "PasMethodCallExpr":
            calls.Add(ResolveMember(ResolveObjectName(node, objectTypes, functionReturnTypes),
                ReadString(node, "MethodName"), true, statementPosition, objectTypes, api) with
                { FirstLiteralInteger = ReadFirstLiteralInteger(node) });
            break;
        case "PasMemberAccessExpr":
            calls.Add(ResolveMember(ResolveObjectName(node, objectTypes, functionReturnTypes),
                ReadString(node, "MemberName"), statementPosition, statementPosition,
                objectTypes, api));
            break;
        case "PasIdentifierExpr":
        {
            var name = ReadString(node, "Name");
            if (statementPosition || !declared.Contains(name))
                calls.Add(new CallUse(statementPosition ? "global-bare-statement" : "global-bare-expression",
                    name, supportedGlobal.Contains(name), "global"));
            break;
        }
    }

    foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
    {
        if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
        var value = property.GetValue(node);
        if (value == null || value is string) continue;
        var childStatement = property.Name is "ThenBlock" or "ElseBlock" or "Body";
        if (value is IEnumerable enumerable)
        {
            var elementStatement = property.Name == "Statements";
            foreach (var item in enumerable) Walk(item, elementStatement, calls, declared, objectTypes,
                functionReturnTypes, supportedGlobal, api);
        }
        else if (property.PropertyType.FullName?.StartsWith("GameSvr.PasEngine.Pas", StringComparison.Ordinal) == true)
        {
            Walk(value, childStatement, calls, declared, objectTypes, functionReturnTypes, supportedGlobal, api);
        }
    }
}

static int? ReadFirstLiteralInteger(object callNode)
{
    var first = ReadEnumerable(callNode, "Arguments").FirstOrDefault();
    return ReadLiteralInteger(first);
}

static int? ReadLiteralInteger(object? node)
{
    if (node == null) return null;
    if (node.GetType().Name == "PasUnaryOpExpr" &&
        ReadString(node, "Op") == "-")
    {
        var operand = ReadLiteralInteger(ReadObject(node, "Operand"));
        return operand.HasValue ? -operand.Value : null;
    }
    if (node.GetType().Name != "PasLiteralExpr") return null;
    var value = ReadObject(node, "Value");
    if (value == null || ReadString(value, "Type") != "Integer") return null;
    var intValue = value.GetType().GetProperty("IntVal")?.GetValue(value);
    return intValue is int result ? result : null;
}

static string ResolveObjectName(object node, Dictionary<string, string> objectTypes,
    Dictionary<string, string> functionReturnTypes)
{
    var objectName = ReadString(node, "ObjectName");
    if (!string.IsNullOrWhiteSpace(objectName)) return objectName;
    var target = ReadObject(node, "Target");
    return InferObjectName(target, objectTypes, functionReturnTypes);
}

static string InferObjectName(object? node, Dictionary<string, string> objectTypes,
    Dictionary<string, string> functionReturnTypes)
{
    if (node == null) return "<expression>";
    switch (node.GetType().Name)
    {
        case "PasIdentifierExpr":
            return ReadString(node, "Name");
        case "PasCallStmt":
        {
            var name = ReadString(node, "Name");
            if (ReturnsPlayer(name, functionReturnTypes)) return "This_Player";
            return ReadBool(node, "IsMethod") && ReturnsPlayerMember(name) ? "This_Player" : "<expression>";
        }
        case "PasMethodCallExpr":
            return ReturnsPlayerMember(ReadString(node, "MethodName")) ? "This_Player" : "<expression>";
        default:
            return "<expression>";
    }
}

static bool ReturnsPlayer(string name, Dictionary<string, string> functionReturnTypes) =>
    functionReturnTypes.TryGetValue(name, out var typeName) &&
    typeName.Equals("TPlayer", StringComparison.OrdinalIgnoreCase);

static bool ReturnsPlayerMember(string name) =>
    name.Equals("FindPlayerByName", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("GetSpouse", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("GetMember", StringComparison.OrdinalIgnoreCase);

static CallUse ResolveMember(string objectName, string name, bool callContext,
    bool statementCall,
    Dictionary<string, string> objectTypes, ApiSurface api, bool memberWrite = false)
{
    var isPlayer = objectName.Equals("This_Player", StringComparison.OrdinalIgnoreCase) ||
        objectTypes.TryGetValue(objectName, out var objectType) &&
        objectType.Equals("TPlayer", StringComparison.OrdinalIgnoreCase);
    if (isPlayer)
    {
        var readSurface = api.PlayerFunctions.Contains(name)
            ? "player-func"
            : api.PlayerMethods.Contains(name)
                ? "player-method"
                : "player-member-read";
        var resolved = memberWrite
            ? api.PlayerWritableProperties.Contains(name)
            : callContext
                ? api.PlayerFunctions.Contains(name) || api.PlayerMethods.Contains(name)
                : readSurface != "player-member-read" || api.PlayerReadableProperties.Contains(name);
        return new CallUse(callContext ? "player-call" : memberWrite ? "player-member-write" : "player-member-read",
            name, resolved, callContext
                ? statementCall ? "player-method" : "player-func"
                : memberWrite ? "player-member-write" : readSurface);
    }
    if (objectName.Equals("This_Npc", StringComparison.OrdinalIgnoreCase))
    {
        var readSurface = api.NpcFunctions.Contains(name)
            ? "npc-func"
            : api.NpcMethods.Contains(name)
                ? "npc-method"
                : "npc-member";
        var resolved = api.NpcFunctions.Contains(name) || api.NpcMethods.Contains(name) ||
            (!callContext && api.NpcProperties.Contains(name));
        return new CallUse(callContext ? "npc-call" : "npc-member", name, resolved,
            callContext ? statementCall ? "npc-method" : "npc-func" : readSurface);
    }
    if (objectName.Equals("This_Animal", StringComparison.OrdinalIgnoreCase))
    {
        return new CallUse(callContext ? "animal-call" : "animal-member", name,
            !callContext && api.AnimalProperties.Contains(name),
            callContext ? "animal-method" : "animal-member");
    }
    if (objectName.Equals("This_DB", StringComparison.OrdinalIgnoreCase))
    {
        var resolved = api.DbMethods.Contains(name) || (!callContext && api.DbProperties.Contains(name));
        return new CallUse(callContext ? "db-call" : "db-member", name, resolved,
            callContext ? "db-method" : "db-member");
    }
    if (objectName.Equals("This_Item", StringComparison.OrdinalIgnoreCase) ||
        objectTypes.TryGetValue(objectName, out objectType) &&
        objectType.Equals("TBaseItem", StringComparison.OrdinalIgnoreCase))
    {
        return new CallUse(callContext ? "item-call" : "item-member", name,
            !callContext && api.ItemProperties.Contains(name),
            callContext ? "item-method" : "item-member");
    }
    return new CallUse("unknown-object:" + objectName, name, false, "unknown");
}

static void AddDeclaration(object declaration, HashSet<string> declared,
    Dictionary<string, string> objectTypes)
{
    var name = ReadString(declaration, "Name");
    var typeName = ReadString(declaration, "TypeName");
    declared.Add(name);
    if (!string.IsNullOrEmpty(typeName)) objectTypes[name] = typeName;
}

static ApiSurface ReadApiSurface(string bridge, string yanshen)
{
    var standalone = CasesInMethod(bridge, "CallStandaloneFunction");
    standalone.UnionWith(CasesInMethod(bridge, "TryCallThisPlayerFunc"));
    standalone.UnionWith(CasesInMethod(yanshen, "TryCallYanshenFunc"));
    return new ApiSurface(
        standalone,
        CasesInMethod(bridge, "CallPlayerFunc"),
        CasesInMethod(bridge, "CallPlayerMethod"),
        CasesInMethod(bridge, "GetPlayerProperty"),
        CasesInMethod(bridge, "SetPlayerProperty"),
        CasesInMethod(bridge, "CallNpcFunc"),
        CasesInMethod(bridge, "CallNpcMethod"),
        CasesInMethod(bridge, "GetNpcProperty"),
        CasesInMethod(bridge, "GetAnimalProperty"),
        Union(CasesInMethod(bridge, "GetItemProperty"), CasesInMethod(bridge, "SetItemProperty")),
        CasesInMethod(bridge, "CallDbMethod"),
        CasesInMethod(bridge, "GetDbProperty"));
}

static PlaceholderSurface ReadPlaceholderSurface(string bridge, string yanshen)
{
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var findings = new List<SourceFinding>();
    var unsupportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var unsupportedFindings = new List<SourceFinding>();
    var supportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var syntheticStateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var syntheticStateFindings = new List<SourceFinding>();
    ReadPlaceholderSource(bridge, "PasApiBridge.cs", names, findings,
        unsupportedKeys, unsupportedFindings, supportedKeys,
        syntheticStateKeys, syntheticStateFindings);
    ReadPlaceholderSource(yanshen, "PasApiBridge.Yanshen.cs", names, findings,
        unsupportedKeys, unsupportedFindings, supportedKeys,
        syntheticStateKeys, syntheticStateFindings);
    unsupportedKeys.ExceptWith(supportedKeys);
    return new PlaceholderSurface(names, findings.ToArray(),
        unsupportedKeys, unsupportedFindings.ToArray(), syntheticStateKeys,
        syntheticStateFindings.ToArray());
}

static void ReadPlaceholderSource(string source, string fileName,
    HashSet<string> names, List<SourceFinding> findings,
    HashSet<string> unsupportedKeys, List<SourceFinding> unsupportedFindings,
    HashSet<string> supportedKeys,
    HashSet<string> syntheticStateKeys, List<SourceFinding> syntheticStateFindings)
{
    if (string.IsNullOrEmpty(source)) return;
    var lines = source.Replace("\r\n", "\n").Split('\n');
    var activeCases = new List<string>();
    var dispatchSurface = string.Empty;
    for (var index = 0; index < lines.Length; index++)
    {
        var methodMatch = Regex.Match(lines[index],
            @"^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:bool|PasValue|void|int|string|IDisposable)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(");
        if (methodMatch.Success)
        {
            dispatchSurface = GetDispatchSurface(methodMatch.Groups[1].Value);
            activeCases.Clear();
        }
        var matches = Regex.Matches(lines[index], "case\\s+\"([^\"]+)\"");
        if (matches.Count > 0)
        {
            var previousWasCase = index > 0 && Regex.IsMatch(lines[index - 1],
                "^\\s*(?:case\\s+\"[^\"]+\"\\s*:\\s*)+$");
            if (!previousWasCase) activeCases.Clear();
            activeCases.AddRange(matches.Select(match => match.Groups[1].Value));
            RecordSyntheticStateMapping(lines[index], fileName, index, dispatchSurface,
                activeCases, syntheticStateKeys, syntheticStateFindings);
            continue;
        }
        if (Regex.IsMatch(lines[index], "^\\s*//\\s*=+"))
        {
            activeCases.Clear();
            continue;
        }
        if (Regex.IsMatch(lines[index], "^\\s*default\\s*:")) activeCases.Clear();
        RecordSyntheticStateMapping(lines[index], fileName, index, dispatchSurface,
            activeCases, syntheticStateKeys, syntheticStateFindings);
        if (lines[index].Contains("RejectUnsupportedNativeApi", StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(dispatchSurface))
            {
                foreach (var name in activeCases)
                    unsupportedKeys.Add(dispatchSurface + "\0" + name);
            }
            unsupportedFindings.Add(new SourceFinding(
                fileName, index + 1, activeCases.ToArray(), lines[index].Trim()));
        }
        else if (!string.IsNullOrEmpty(dispatchSurface) &&
                 HasSupportedCaseExit(lines[index]))
        {
            foreach (var name in activeCases)
                supportedKeys.Add(dispatchSurface + "\0" + name);
        }
        if (Regex.IsMatch(lines[index], "TODO|stub|placeholder|not yet implemented|always true", RegexOptions.IgnoreCase))
        {
            foreach (var name in activeCases) names.Add(name);
            findings.Add(new SourceFinding(fileName, index + 1, activeCases.ToArray(), lines[index].Trim()));
        }
    }
}

static bool HasSupportedCaseExit(string line)
{
    if (Regex.IsMatch(line, @"\bbreak\s*;")) return true;
    var returnMatch = Regex.Match(line, @"\breturn\s+(.+?)\s*;");
    return returnMatch.Success &&
        !returnMatch.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase);
}

static void RecordSyntheticStateMapping(string line, string fileName, int index,
    string dispatchSurface, List<string> activeCases, HashSet<string> keys,
    List<SourceFinding> findings)
{
    if (string.IsNullOrEmpty(dispatchSurface) || activeCases.Count == 0 ||
        !Regex.IsMatch(line, @"\b(?:Get|Set)PlayerVar\s*\(")) return;
    var syntheticNames = activeCases.Where(name => !IsExplicitPlayerVariableApi(name)).ToArray();
    if (syntheticNames.Length == 0) return;
    foreach (var name in syntheticNames) keys.Add(dispatchSurface + "\0" + name);
    findings.Add(new SourceFinding(fileName, index + 1, syntheticNames, line.Trim()));
}

static bool IsExplicitPlayerVariableApi(string name) => name.ToLowerInvariant() is
    "getv" or "setv" or "gets" or "sets" or "groupsetv" or "groupsets" or
    "getvex" or "setvex" or "ys_cmptime_min" or "ys_setcd_min";

static string GetDispatchSurface(string methodName)
{
    return methodName switch
    {
        "CallPlayerMethod" => "player-method",
        "CallPlayerFunc" => "player-func",
        "GetPlayerProperty" => "player-member-read",
        "SetPlayerProperty" => "player-member-write",
        "CallNpcMethod" => "npc-method",
        "CallNpcFunc" => "npc-func",
        "GetNpcProperty" => "npc-member",
        "GetAnimalProperty" => "animal-member",
        "GetItemProperty" or "SetItemProperty" => "item-member",
        "CallDbMethod" => "db-method",
        "GetDbProperty" => "db-member",
        "CallStandaloneFunction" or "TryCallThisPlayerFunc" or "TryCallYanshenFunc" => "global",
        _ => string.Empty
    };
}

static HashSet<string> CasesInMethod(string source, string methodName)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrEmpty(source)) return result;
    var signature = Regex.Match(source,
        $@"^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:bool|PasValue|void|int|string)\s+{Regex.Escape(methodName)}\s*\(",
        RegexOptions.Multiline);
    if (!signature.Success) return result;
    var bodyStart = signature.Index + signature.Length;
    var nextMethod = Regex.Match(source.Substring(bodyStart),
        @"^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:bool|PasValue|void|int|string|IDisposable)\s+[A-Za-z_][A-Za-z0-9_]*\s*\(",
        RegexOptions.Multiline);
    var bodyLength = nextMethod.Success ? nextMethod.Index : source.Length - bodyStart;
    foreach (Match match in Regex.Matches(source.Substring(bodyStart, bodyLength), "case\\s+\"([^\"]+)\""))
        result.Add(match.Groups[1].Value);
    return result;
}

static HashSet<string> Union(params HashSet<string>[] sets)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var set in sets) result.UnionWith(set);
    return result;
}

static IEnumerable<object> ReadEnumerable(object instance, string propertyName) =>
    ((IEnumerable)(instance.GetType().GetProperty(propertyName)!.GetValue(instance)!)).Cast<object>();
static string ReadString(object instance, string propertyName) =>
    instance.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString() ?? string.Empty;
static object? ReadObject(object instance, string propertyName) =>
    instance.GetType().GetProperty(propertyName)?.GetValue(instance);
static bool ReadBool(object instance, string propertyName) =>
    (bool)(instance.GetType().GetProperty(propertyName)?.GetValue(instance) ?? false);
static string Relative(string path, string root) => Path.GetRelativePath(root, path).Replace('\\', '/');

sealed record ApiSurface(HashSet<string> Standalone, HashSet<string> PlayerFunctions,
    HashSet<string> PlayerMethods, HashSet<string> PlayerReadableProperties,
    HashSet<string> PlayerWritableProperties,
    HashSet<string> NpcFunctions, HashSet<string> NpcMethods, HashSet<string> NpcProperties,
    HashSet<string> AnimalProperties,
    HashSet<string> ItemProperties,
    HashSet<string> DbMethods, HashSet<string> DbProperties);
sealed record PlaceholderSurface(HashSet<string> Names, SourceFinding[] SourceFindings,
    HashSet<string> UnsupportedKeys, SourceFinding[] UnsupportedSourceFindings,
    HashSet<string> SyntheticStateKeys, SourceFinding[] SyntheticStateSourceFindings);
sealed record CallUse(string Kind, string Name, bool Resolved, string Surface,
    int? FirstLiteralInteger = null);
sealed record SymbolUse(string File, string Kind, string Name, int Count);
sealed record ParsedScript(string File, int CallCount, SymbolUse[] Unresolved,
    SymbolUse[] Placeholders, SymbolUse[] Unsupported, SymbolUse[] SyntheticState,
    ConditionalDispatchUse[] ConditionalDispatch);
sealed record ConditionalDispatchUse(string File, string Kind, string Name,
    int? FirstLiteralInteger, int Count);
sealed record ParseFailure(string File, string Error);
sealed record SkippedArtifact(string File, string Reason);
sealed record SourceClassification(string File, string Role, string[] EntryFiles,
    string[] LiteralReferences);
sealed record ParseBaseline(int TotalFiles, int ParsedFiles, int ParseFailureCount,
    List<ParseFailure> ParseFailures);
sealed record FileEncodingAudit(string File, long ByteLength, string Sha256, string EncodingKind,
    string ReaderPath, bool StrictUtf8, bool StrictGbk);
sealed record FileAuditResult(string File, long ByteLength, string Sha256, string EncodingKind,
    string ReaderPath, bool StrictUtf8, bool StrictGbk, bool Parsed, int CallCount, string? Error,
    string? SkippedReason, string SourceRole);
sealed record EncodingSummary(string Kind, int Files, int Parsed);
sealed record SymbolSummary(string Kind, string Name, int Count, string[] Files);
sealed record ConditionalDispatchSummary(string Kind, string Name,
    int? FirstLiteralInteger, int Count, string[] Files);
sealed record SourceFinding(string File, int Line, string[] ApiNames, string Text);
sealed record AuditReport(string EnvirPath, int TotalFiles, int ParsedFiles, int ParseFailureCount,
    int SkippedArtifactCount, int CallCount, List<ParseFailure> ParseFailures,
    List<SkippedArtifact> SkippedArtifacts, SymbolSummary[] UnresolvedSymbols,
    SymbolSummary[] UsedPlaceholders, SymbolSummary[] UsedUnsupportedApis,
    SymbolSummary[] UsedUnsupportedOrphans,
    SymbolSummary[] UsedSyntheticStateApis,
    ConditionalDispatchSummary[] ConditionalDispatchCalls,
    SourceFinding[] PlaceholderSourceFindings,
    SourceFinding[] UnsupportedSourceFindings, SourceFinding[] SyntheticStateSourceFindings,
    SourceClassification[] SourceClassifications, EncodingSummary[] EncodingSummary,
    FileAuditResult[] Files);

sealed class AuditPreprocessorFrame
{
    public AuditPreprocessorFrame(bool parentActive, bool conditionActive)
    {
        ParentActive = parentActive;
        ConditionActive = conditionActive;
    }

    public bool ParentActive { get; }
    public bool ConditionActive { get; set; }
    public bool ElseSeen { get; set; }
    public bool Active => ParentActive && ConditionActive;
}
