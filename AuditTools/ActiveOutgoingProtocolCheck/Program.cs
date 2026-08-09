var root = FindRepositoryRoot();
var messagePath = Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Message.cs");
var messageSource = File.ReadAllText(messagePath);

var changeMapBlock = CaseBlock(messageSource, "RM_CHANGEMAP", "RM_BUTCH", 0);
Contains(changeMapBlock,
    "SendDefMessage(Grobal2.SM_CHANGEMAP, ObjectId, m_nCurrX, m_nCurrY, DayBright(), ProcessMsg.sMsg);",
    "RM_CHANGEMAP packet");
Ordered(changeMapBlock, "SM_CHANGEMAP", "RefUserState();",
    "RM_CHANGEMAP user-state order");
Ordered(changeMapBlock, "RefUserState();", "SendMapDescription();",
    "RM_CHANGEMAP map-description order");
NotContains(changeMapBlock, "SendServerConfig",
    "RM_CHANGEMAP extra SM_SERVERCONFIG");

var hearCase = messageSource.IndexOf("case Grobal2.RM_HEAR:",
    StringComparison.Ordinal);
var chatSwitch = messageSource.IndexOf("switch (ProcessMsg.wIdent)", hearCase,
    StringComparison.Ordinal);
Require(chatSwitch >= 0, "chat dispatcher switch");
var guildBlock = CaseBlock(messageSource, "RM_GUILDMESSAGE",
    "RM_MERCHANTSAY", chatSwitch);
Contains(guildBlock,
    "Grobal2.MakeDefaultMsg(Grobal2.SM_GUILDMESSAGE, ProcessMsg.BaseObject, 0xFFD4, 0, 1);",
    "RM_GUILDMESSAGE target header");
NotContains(guildBlock, "ProcessMsg.nParam1",
    "RM_GUILDMESSAGE configurable foreground color");
NotContains(guildBlock, "ProcessMsg.nParam2",
    "RM_GUILDMESSAGE configurable background color");
NotContains(guildBlock, "g_Config",
    "RM_GUILDMESSAGE runtime color configuration");

var playerSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.cs"));
NotContains(playerSource, "SendServerConfig(",
    "orphan SM_SERVERCONFIG sender");

Console.WriteLine(
    "ActiveOutgoingProtocolCheck PASS changemap-extra=0 guild-param=0xFFD4");
return 0;

static string CaseBlock(string source, string startCase, string endCase,
    int startIndex)
{
    var start = source.IndexOf("case Grobal2." + startCase + ":", startIndex,
        StringComparison.Ordinal);
    var end = source.IndexOf("case Grobal2." + endCase + ":", start,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start, startCase + " source block");
    return source[start..end];
}

static void Ordered(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Require(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static void Contains(string source, string value, string label) =>
    Require(source.Contains(value, StringComparison.Ordinal), label);

static void NotContains(string source, string value, string label) =>
    Require(!source.Contains(value, StringComparison.Ordinal), label);

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static string FindRepositoryRoot()
{
    foreach (var startPath in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(startPath);
             directory != null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "GameSvr"))
                && Directory.Exists(Path.Combine(directory.FullName, "SystemModule")))
                return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException("repository root not found");
}
