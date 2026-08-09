var root = FindRepositoryRoot();
var messageSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Message.cs"));
var routerSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeSocialProtocol.cs"));

const string socialCall = "TryHandleNativeSocialProtocol(ProcessMsg)";
Require(Count(messageSource, socialCall) == 1,
    "Operate must invoke the social router exactly once");
Require(Count(messageSource, "base.Operate(ProcessMsg)") == 1,
    "Operate must have exactly one base fallback");

var defaultStart = messageSource.LastIndexOf("default:", StringComparison.Ordinal);
Require(defaultStart >= 0, "Operate default branch missing");
var defaultEnd = messageSource.IndexOf("break;", defaultStart,
    StringComparison.Ordinal);
Require(defaultEnd > defaultStart, "Operate default branch terminator missing");
var defaultBranch = messageSource[defaultStart..defaultEnd];
Ordered(defaultBranch, "if (!" + socialCall + ")",
    "result = base.Operate(ProcessMsg);",
    "base fallback must follow a failed social route");
Require(Count(defaultBranch, socialCall) == 1,
    "default branch social router call count");
Require(Count(defaultBranch, "base.Operate(ProcessMsg)") == 1,
    "default branch base fallback count");

var handlers = new[]
{
    "TryHandleNativeGroupProtocol(processMessage)",
    "TryHandleNativeRelationProtocol(processMessage)",
    "TryHandleNativeChannelProtocol(processMessage)",
    "TryHandleNativeCorpsCoreProtocol(processMessage)",
    "TryHandleNativeCorpsAdminProtocol(processMessage)",
    "TryHandleNativeGuildCoreProtocol(processMessage)",
    "TryHandleNativeGuildRelationProtocol(processMessage)",
    "TryHandleNativeGuildTailProtocol(processMessage)"
};

var previous = -1;
foreach (var handler in handlers)
{
    Require(Count(routerSource, handler) == 1,
        handler + " must be invoked exactly once");
    var current = routerSource.IndexOf(handler, StringComparison.Ordinal);
    Require(current > previous, handler + " is out of routing order");
    previous = current;
}

Require(Count(routerSource,
    "private bool TryHandleNativeSocialProtocol(TProcessMessage processMessage)") == 1,
    "social router declaration count");
Require(!routerSource.Contains("return true;", StringComparison.Ordinal),
    "social router must not contain a swallowing true stub");
Require(Count(routerSource, "private bool ") == 1,
    "social router file must not implement protocol-family handlers");

Console.WriteLine(
    "NativeSocialProtocolRouterCheck PASS default-fallback=guarded families=8 ordered-once");

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }

    throw new InvalidOperationException("repository root not found");
}

static int Count(string source, string value)
{
    var count = 0;
    var index = 0;
    while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }
    return count;
}

static void Ordered(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Require(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
