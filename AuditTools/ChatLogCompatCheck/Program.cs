using System.Globalization;
using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;

// The very first M2Share reference runs its static ctor, which loads
// !Setup.txt / String.ini / Command.conf plus ..\Share\PlayerUpgradeExp.ini and throws
// when they are absent — this audit had none of them, so it died before assertion one.
PrepareRuntimeConfig();

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    var originalConfigPath = M2Share.sConfigPath;
    var originalChatLog = M2Share.g_ChatLoggingList;
    var originalCulture = CultureInfo.CurrentCulture;
    var root = Path.Combine(Path.GetTempPath(),
        "loym2-chat-log-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        VerifyFormattedNewFile(root);
        VerifyExistingFileMerge(root);
        VerifyEmptyFile(root);
        VerifyWriteFailure(root);
        VerifySourceContracts();
    }
    finally
    {
        M2Share.sConfigPath = originalConfigPath;
        M2Share.g_ChatLoggingList = originalChatLog;
        CultureInfo.CurrentCulture = originalCulture;
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the primary assertion.
        }
    }

    Console.WriteLine(
        "PASS ChatLogCompatCheck path=GS1/ChatLog.txt encoding=CP936-no-bom " +
        "lines=CRLF-final merge=session-before-existing overwrite=direct " +
        "memory=not-cleared format=current-culture " +
        "shutdown=gate-item-chat-stop/failure-propagates");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ChatLogCompatCheck FAIL: {exception}");
    return 1;
}

static void VerifyFormattedNewFile(string root)
{
    var directory = Directory.CreateDirectory(Path.Combine(root, "formatted")).FullName;
    M2Share.sConfigPath = directory;
    M2Share.g_ChatLoggingList = new List<string>();
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");

    var before = DateTime.Now.ToString("G", CultureInfo.CurrentCulture);
    InvokeAppendChatLog("张三", "你好");
    var after = DateTime.Now.ToString("G", CultureInfo.CurrentCulture);

    Equal(1, M2Share.g_ChatLoggingList.Count, "formatted in-memory line count");
    var line = M2Share.g_ChatLoggingList[0];
    var expectedBefore = $"[{before}] 张三: 你好";
    var expectedAfter = $"[{after}] 张三: 你好";
    Assert(line == expectedBefore || line == expectedAfter,
        $"current-culture line mismatch: '{line}'");
    Assert(M2Share.SaveChatLog(), "new chat log save returned false");

    var fileName = Path.Combine(directory, "ChatLog.txt");
    Assert(File.Exists(fileName), "ChatLog.txt was not written under sConfigPath");
    var expectedBytes = HUtil32.GbkEncoding.GetBytes(line + "\r\n");
    EqualBytes(expectedBytes, File.ReadAllBytes(fileName), "formatted GBK file");
    Equal(0, HUtil32.GbkEncoding.GetPreamble().Length, "CP936 preamble length");
}

static void VerifyExistingFileMerge(string root)
{
    var directory = Directory.CreateDirectory(Path.Combine(root, "merge")).FullName;
    var fileName = Path.Combine(directory, "ChatLog.txt");
    File.WriteAllBytes(fileName,
        HUtil32.GbkEncoding.GetBytes("旧一\r\nold2"));

    var sessionLines = new List<string> { "new1", "中文new2" };
    M2Share.sConfigPath = directory;
    M2Share.g_ChatLoggingList = sessionLines;
    Assert(M2Share.SaveChatLog(), "merged chat log save returned false");

    var expectedLines = new[] { "new1", "中文new2", "旧一", "old2" };
    EqualSequence(expectedLines, M2Share.g_ChatLoggingList,
        "session-before-existing in-memory order");
    Assert(ReferenceEquals(sessionLines, M2Share.g_ChatLoggingList),
        "SaveChatLog replaced the in-memory list");

    var expectedText = string.Join("\r\n", expectedLines) + "\r\n";
    EqualBytes(HUtil32.GbkEncoding.GetBytes(expectedText),
        File.ReadAllBytes(fileName), "session-before-existing overwrite bytes");
}

static void VerifyEmptyFile(string root)
{
    var directory = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
    M2Share.sConfigPath = directory;
    M2Share.g_ChatLoggingList = new List<string>();
    Assert(M2Share.SaveChatLog(), "empty chat log save returned false");
    Equal(0L, new FileInfo(Path.Combine(directory, "ChatLog.txt")).Length,
        "empty ChatLog.txt length");
}

static void VerifyWriteFailure(string root)
{
    var blockedPath = Path.Combine(root, "blocked-path");
    File.WriteAllText(blockedPath, "not a directory", Encoding.ASCII);
    M2Share.sConfigPath = blockedPath;
    M2Share.g_ChatLoggingList = new List<string> { "unsaved" };

    Exception failure = null;
    try
    {
        M2Share.SaveChatLog();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        failure = exception;
    }

    Assert(failure != null, "SaveChatLog swallowed a write-path failure");
    EqualSequence(new[] { "unsaved" }, M2Share.g_ChatLoggingList,
        "failed save changed the session list");
}

static void VerifySourceContracts()
{
    var repositoryRoot = FindRepositoryRoot();
    var m2ShareSource = File.ReadAllText(
        Path.Combine(repositoryRoot, "GameSvr", "M2Share.cs"));
    var chatSource = File.ReadAllText(
        Path.Combine(repositoryRoot, "GameSvr", "Players", "TPlayObject.Chat.cs"));
    var appServiceSource = File.ReadAllText(
        Path.Combine(repositoryRoot, "GameSvr", "AppService.cs"));
    var timedServiceSource = File.ReadAllText(
        Path.Combine(repositoryRoot, "GameSvr", "TimedService.cs"));

    var saveBody = ExtractMethodBody(m2ShareSource,
        "public static bool SaveChatLog()");
    Contains(saveBody, "Path.Combine(sConfigPath, \"ChatLog.txt\")",
        "GS1 chat-log path");
    Contains(saveBody, "lock (ChatLogSync)", "save shared lock");
    Contains(saveBody, "detectEncodingFromByteOrderMarks: false",
        "strict CP936 read");
    Contains(saveBody, ".Append(\"\\r\\n\")", "final CRLF writer");
    Contains(saveBody,
        "File.WriteAllText(fileName, content.ToString(), HUtil32.GbkEncoding)",
        "GBK direct overwrite");
    Assert(!saveBody.Contains("AtomicFile", StringComparison.Ordinal),
        "SaveChatLog must not replace through a temporary file");
    Assert(!saveBody.Contains(".Clear(", StringComparison.Ordinal),
        "SaveChatLog clears the in-memory list");
    Assert(!saveBody.Contains("catch", StringComparison.Ordinal),
        "SaveChatLog no longer propagates persistence failures");
    Assert(IndexOf(saveBody, "g_ChatLoggingList.Add(line)") <
           IndexOf(saveBody, "File.WriteAllText"),
        "existing lines are not merged before overwrite");

    var appendBody = ExtractMethodBody(m2ShareSource,
        "internal static void AppendChatLog(string characterName, string message)");
    Contains(appendBody,
        "DateTime.Now.ToString(\"G\", CultureInfo.CurrentCulture)",
        "current-culture DateTimeToStr equivalent");
    Contains(appendBody, "lock (ChatLogSync)", "append shared lock");

    Contains(chatSource, "M2Share.AppendChatLog(m_sCharName, sData);",
        "player chat append route");
    Assert(!chatSource.Contains("s_chatLogLock", StringComparison.Ordinal),
        "player retains a private chat-log lock");
    Assert(!chatSource.Contains("CultureInfo.InvariantCulture", StringComparison.Ordinal),
        "player chat timestamp remains invariant-culture");

    var stopBody = ExtractMethodBody(appServiceSource,
        "public override Task StopAsync(CancellationToken cancellationToken)");
    var gateIndex = IndexOf(stopBody, "M2Share.GateManager?.Stop()");
    var itemIndex = IndexOf(stopBody, "_mirApp.SaveItemNumber()");
    var chatIndex = IndexOf(stopBody, "M2Share.SaveChatLog()");
    var stopIndex = IndexOf(stopBody, "_mirApp.Stop()");
    Assert(gateIndex < itemIndex && itemIndex < chatIndex && chatIndex < stopIndex,
        "shutdown order is not Gate -> ItemNumber -> ChatLog -> Stop");
    var chatTail = stopBody[chatIndex..stopIndex];
    Assert(!chatTail.Contains("catch", StringComparison.Ordinal),
        "chat-log failure must interrupt the remaining shutdown path");
    Assert(!timedServiceSource.Contains("SaveChatLog", StringComparison.Ordinal),
        "TimedService calls the one-shot native chat-log save");
}

static void InvokeAppendChatLog(string characterName, string message)
{
    var method = typeof(M2Share).GetMethod("AppendChatLog",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(M2Share).FullName, "AppendChatLog");
    method.Invoke(null, new object[] { characterName, message });
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static string ExtractMethodBody(string source, string signature)
{
    var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
    if (signatureIndex < 0)
        throw new InvalidOperationException($"Missing source signature: {signature}");
    var openingBrace = source.IndexOf('{', signatureIndex + signature.Length);
    if (openingBrace < 0)
        throw new InvalidOperationException($"Missing opening brace: {signature}");

    var depth = 0;
    for (var index = openingBrace; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        if (source[index] != '}') continue;
        depth--;
        if (depth == 0)
            return source.Substring(openingBrace, index - openingBrace + 1);
    }
    throw new InvalidOperationException($"Missing closing brace: {signature}");
}

static int IndexOf(string value, string expected)
{
    var index = value.IndexOf(expected, StringComparison.Ordinal);
    if (index < 0)
        throw new InvalidOperationException($"Missing source fragment: {expected}");
    return index;
}

static void Contains(string value, string expected, string label) =>
    Assert(value.Contains(expected, StringComparison.Ordinal),
        $"{label}: missing '{expected}'");

static void EqualSequence(IEnumerable<string> expected, IEnumerable<string> actual,
    string label)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();
    Assert(expectedArray.SequenceEqual(actualArray, StringComparer.Ordinal),
        $"{label}: expected [{string.Join(", ", expectedArray)}], " +
        $"actual [{string.Join(", ", actualArray)}]");
}

static void EqualBytes(byte[] expected, byte[] actual, string label)
{
    Assert(expected.AsSpan().SequenceEqual(actual),
        $"{label}: expected {Convert.ToHexString(expected)}, " +
        $"actual {Convert.ToHexString(actual)}");
}

static void Equal<T>(T expected, T actual, string label) where T : IEquatable<T> =>
    Assert(expected.Equals(actual), $"{label}: expected {expected}, actual {actual}");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
