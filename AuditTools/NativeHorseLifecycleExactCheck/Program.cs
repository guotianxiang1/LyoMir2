using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
var horseSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeHorseDismount.cs"));
var actorSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
    "TBaseObject.cs"));
var messageSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Message.cs"));

CheckDeathParity(horseSource);
CheckSpaceMoveParity(horseSource, actorSource);
CheckExitAndState52Clear(horseSource, messageSource);

Console.WriteLine(
    "NativeHorseLifecycleExactCheck PASS death=state51-only " +
    "space-move=paired-51/state52 cross-server=pre-remove " +
    "gate=unchanged exit=state52-bit-clear+status/3555");
return 0;

static void CheckDeathParity(string horseSource)
{
    var die = MethodBlock(horseSource, "public override void Die()");
    Contains(die, "HasNativeActiveState(NativeHorseMountedState)",
        "driver death state51 gate");
    Ordered(die, "ClientNativeHorseDismount();", "base.Die();",
        "driver teardown before base death");
    NotContains(die, "NativeHorseBlockedState",
        "passenger death state52 teardown");
    NotContains(die, "CleanupNativeHorseOnExit",
        "death must not use full exit cleanup");
    Equal(1, Count(die, "ClientNativeHorseDismount();"),
        "driver death dismount count");
}

static void CheckSpaceMoveParity(string horseSource, string actorSource)
{
    var cleanup = MethodBlock(horseSource,
        "internal void CleanupNativeHorseBeforeSpaceMove()");
    var compact = Compact(cleanup);
    Contains(compact,
        "varmounted=HasNativeActiveState(NativeHorseMountedState);",
        "space-move mounted snapshot");
    Contains(compact,
        "if((!mounted||m_NativeHorsePartner==null)&&" +
        "!HasNativeActiveState(NativeHorseBlockedState))",
        "native paired-driver/state52 entry gate");
    Ordered(cleanup, "if (mounted)", "ClientNativeHorseDismount();",
        "state51 cleanup order");
    Ordered(cleanup, "ClientNativeHorseDismount();",
        "if (HasNativeActiveState(NativeHorseBlockedState))",
        "state51 before state52 cleanup");
    Ordered(cleanup, "if (HasNativeActiveState(NativeHorseBlockedState))",
        "NativeHorseRiderDownCore();", "state52 cleanup order");
    NotContains(cleanup, "CleanupNativeHorseOnExit",
        "space move must preserve unpaired state51");

    var directMove = MethodBlock(actorSource,
        "internal bool TrySpaceMoveToEnvironment(");
    Ordered(directMove, "if (oldEnvironment == null) return false;",
        "playObject.CleanupNativeHorseBeforeSpaceMove();",
        "direct move cleanup after environment precondition");
    Ordered(directMove, "playObject.CleanupNativeHorseBeforeSpaceMove();",
        "oldEnvironment.DeleteFromMap", "direct move cleanup before removal");
    Equal(1, Count(directMove,
        "CleanupNativeHorseBeforeSpaceMove();"),
        "direct move cleanup count");

    var crossMove = MethodBlock(actorSource,
        "private bool TryBeginCrossServerTransfer(");
    Ordered(crossMove, "var sourceY = m_nCurrY;",
        "playObject.CleanupNativeHorseBeforeSpaceMove();",
        "cross-server cleanup after preconditions");
    Ordered(crossMove, "playObject.CleanupNativeHorseBeforeSpaceMove();",
        "sourceEnvironment.DeleteFromMap",
        "cross-server cleanup before removal");
    Equal(1, Count(crossMove,
        "CleanupNativeHorseBeforeSpaceMove();"),
        "cross-server cleanup count");

    var gateMove = MethodBlock(actorSource,
        "private bool EnterAnotherMap(");
    NotContains(gateMove, "CleanupNativeHorse",
        "ordinary gate must keep native no-hook behavior");
}

static void CheckExitAndState52Clear(string horseSource,
    string messageSource)
{
    var disappear = MethodBlock(messageSource,
        "public override void Disappear()");
    Ordered(disappear, "CleanupNativeHorseOnExit();", "DisappearA();",
        "exit cleanup before disappearance");

    var removeState = MethodBlock(horseSource,
        "protected void RemoveNativeHorseTimedState(");
    Contains(removeState, "ClearNativeActiveState(internalType);",
        "horse state bit clear");
    NotContains(removeState, "RemoveTimedAbilityInternal",
        "state52 must not use generic timed nodes");
    Equal(1, Count(removeState, "ClearNativeActiveState(internalType);"),
        "horse state bit clear count");
    Ordered(removeState, "ClearNativeActiveState(internalType);",
        "SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED",
        "state bit clear before status broadcast");
    Contains(removeState, "GetBodyStateBuffer()",
        "status broadcast body state");
    Ordered(removeState, "SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED",
        "SendSocket(Grobal2.MakeDefaultMsg(3555, 0, internalType, 0, 0));",
        "status broadcast before header-only 3555");
    Equal(1, Count(removeState, "MakeDefaultMsg(3555"),
        "state removal 3555 count");

    var riderDown = MethodBlock(horseSource,
        "private void NativeHorseRiderDownCore()");
    Ordered(riderDown,
        "RemoveNativeHorseTimedState(NativeHorseBlockedState);",
        "m_NativeHorsePartner = null;",
        "state52 bit clear before partner clear");
}

static string MethodBlock(string source, string anchor)
{
    var start = source.IndexOf(anchor, StringComparison.Ordinal);
    Require(start >= 0, "method anchor: " + anchor);
    return BraceBlock(source, start, anchor);
}

static string BraceBlock(string source, int start, string label)
{
    var open = source.IndexOf('{', start);
    Require(open >= 0, "opening brace: " + label);
    var depth = 0;
    for (var index = open; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        else if (source[index] == '}' && --depth == 0)
            return source[start..(index + 1)];
    }
    throw new InvalidOperationException("closing brace: " + label);
}

static string Compact(string value) =>
    Regex.Replace(value, @"\s+", string.Empty);

static int Count(string source, string value)
{
    var count = 0;
    for (var index = 0;;)
    {
        index = source.IndexOf(value, index, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        index += value.Length;
    }
}

static void Contains(string source, string value, string label) =>
    Require(source.Contains(value, StringComparison.Ordinal), label);

static void NotContains(string source, string value, string label) =>
    Require(!source.Contains(value, StringComparison.Ordinal), label);

static void Ordered(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Require(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}
