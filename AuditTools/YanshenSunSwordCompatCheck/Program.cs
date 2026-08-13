using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    VerifyProtocolConstants();
    VerifyPhysicalAttackBody();
    VerifyPowerFormula();
    VerifySkillTrainingNotificationDebounce();
    VerifySourceContracts();

    Console.WriteLine(
        "PASS YanshenSunSwordCompatCheck protocol=3002/10023->2/2819/10612->1230 " +
        "body=12-byte-le formula=native-double-trunc+yanshen-fallback+cap255 " +
        "state=mp-twice+ready-timeout+15s-cooldown+forgery-reject+consume-once " +
        "geometry=four-cell-axis-penetration damage=physical-ac+500ms " +
        "training=3s-debounce+switch-flush+levelup-replace " +
        "broadcast=client-3002-only-10612->1230-once");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"YanshenSunSwordCompatCheck FAIL: {exception}");
    return 1;
}

static void VerifyProtocolConstants()
{
    Equal(3002, Grobal2.CM_SWORD_HIT, "CM_SWORD_HIT");
    Equal(10023, ReadProtocolConstant("RM_SWORD_HIT"), "RM_SWORD_HIT");
    Equal(2, Grobal2.SM_SWORD_HIT, "SM_SWORD_HIT");
    Equal(2819, Grobal2.SM_SWORDHIT_ON, "SM_SWORDHIT_ON");
    Equal(1230, Grobal2.SM_PHYSICAL_ATT, "SM_PHYSICAL_ATT");
    Equal(10612, Grobal2.RM_PHYSICAL_ATT, "RM_PHYSICAL_ATT");
}

static void VerifyPhysicalAttackBody()
{
    const int level = 3;
    const int direction = 6;
    const int x = 321;
    const int y = 654;
    var body = InvokeBody(level, direction, x, y);

    Equal(12, body.Length, "TClientPhyAttRec wire size");
    var expected = new[] { 1015, level, 0, direction, x, y };
    for (var index = 0; index < expected.Length; index++)
    {
        Equal(expected[index],
            BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(index * 2, 2)),
            $"TClientPhyAttRec Int16 field {index}");
    }
}

static void VerifyPowerFormula()
{
    var root = Path.Combine(Path.GetTempPath(),
        "loym2-yanshen-sunsword-" + Guid.NewGuid().ToString("N"));
    try
    {
        var envir = Directory.CreateDirectory(
            Path.Combine(root, "Mir200", "Envir")).FullName;
        var runtime = Directory.CreateDirectory(
            Path.Combine(root, "Mir200", "GS1")).FullName;
        var configPath = Path.Combine(runtime, "config.json");
        File.WriteAllText(configPath,
            "{\r\n" +
            "  \"逐日剑法\": 1,\r\n" +
            "  \"逐日剑法_A值\": \"7\",\r\n" +
            "  \"逐日剑法_B值\": \"6\"\r\n" +
            "}\r\n", Encoding.GetEncoding(936));

        var manager = new PluginManager(envir, runtime);
        manager.RegisterBuiltinPlugins();
        Assert(manager.LoadPlugin("YanshenCompat"),
            "YanshenCompat did not enter Running state");
        var plugin = manager.GetPlugin("YanshenCompat")
            ?? throw new InvalidOperationException("YanshenCompat was not registered");
        var api = new YanshenApi(null, null, manager);

        Assert(!plugin.IsInitialized && !api.IsSunSword(),
            "uninitialized plugin reported sun-sword enabled");
        Equal(16, InvokePower(11, 1, api),
            "native multiplier 15/10 must truncate to 16");
        Equal(21, InvokePower(10, 99, api),
            "native effective level multiplier must cap at level 3");
        EqualSequence(new[] { 16, 14, 12, 11 },
            Enumerable.Range(1, 4)
                .Select(distance => InvokeDistancePower(16, distance)).ToArray(),
            "native distance decay must truncate independently after scaling");

        plugin.IsInitialized = true;
        Assert(api.IsSunSword() && api.SunSwordA() == 7 && api.SunSwordB() == 6,
            "GBK sun-sword configuration did not reach YanshenApi");
        Equal(150, InvokePower(100, 2, api),
            "enabled Yanshen A=7 B=6 effective-level=2 formula");

        // The plugin caps A at 255 before the write (0x100B46F6 cmp eax,0xFF +
        // cmovg) and then splices `04 07 add al,7` over host 0x0076B14C, so
        // A + effective level is 8-bit: 254 + 2 wraps to 0, it does not stop at
        // 255. The dialog's "(A+Level)不可超过255" is the width of [ebx+0x92].
        manager.SetNativeConfigValue("逐日剑法_A值", "254");
        manager.SetNativeConfigValue("逐日剑法_B值", "100");
        Equal(0, InvokePower(100, 2, api),
            "Yanshen A+effective-level multiplier must wrap at 256 (0x0076B14C add al)");
        // B is the imm32 of host 0x00771DA3 `B9 0A 00 00 00`, consumed by the
        // idiv at 0x00771DA9, so the division truncates instead of rounding.
        // 100 * (2+7) / 7 = 128.57: truncation gives 128, Round gives 129.
        manager.SetNativeConfigValue("逐日剑法_A值", "7");
        manager.SetNativeConfigValue("逐日剑法_B值", "7");
        Equal(128, InvokePower(100, 2, api),
            "Yanshen B must divide with idiv truncation (0x00771DA9)");

        manager.SetNativeConfigValue("逐日剑法", 0L);
        Assert(!api.IsSunSword(), "disabled sun-sword switch remained enabled");
        Equal(180, InvokePower(100, 2, api),
            "disabled Yanshen switch must retain native formula");

        manager.SetNativeConfigValue("逐日剑法", 1L);
        manager.SetNativeConfigValue("逐日剑法_A值", "7");
        foreach (var invalidB in new[] { "0", "-1" })
        {
            manager.SetNativeConfigValue("逐日剑法_B值", invalidB);
            Assert(api.SunSwordB() <= 0,
                "invalid sun-sword B value did not reach YanshenApi");
            Equal(180, InvokePower(100, 2, api),
                "non-positive Yanshen B must retain native formula: " + invalidB);
        }
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static void VerifySkillTrainingNotificationDebounce()
{
    M2Share.ProcessMsgCriticalSection ??= new object();
    var actor = (TBaseObject)System.Runtime.CompilerServices.RuntimeHelpers
        .GetUninitializedObject(typeof(TBaseObject));
    actor.m_MsgList = new List<SendMessage>();
    var update = typeof(TBaseObject).GetMethod("SendUpdateDelayMsg",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: new[]
        {
            typeof(TBaseObject), typeof(short), typeof(short), typeof(int),
            typeof(int), typeof(int), typeof(string), typeof(int)
        },
        modifiers: null)
        ?? throw new MissingMethodException(typeof(TBaseObject).FullName,
            "SendUpdateDelayMsg");

    void Queue(int magicId, int level, int trainPoint, int delay) =>
        update.Invoke(actor, new object[]
        {
            actor, (short)Grobal2.RM_MAGIC_LVEXP, (short)0, magicId,
            level, trainPoint, string.Empty, delay
        });

    Queue(58, 1, 10, 3000);
    Queue(58, 1, 13, 3000);
    var messages = actor.m_MsgList
        .Where(message => message.wIdent == Grobal2.RM_MAGIC_LVEXP).ToArray();
    Equal(1, messages.Length, "same-skill debounce message count");
    Equal(13, messages[0].nParam3, "same-skill debounce latest train point");
    Assert(messages[0].boLateDelivery,
        "same-skill debounce did not retain a pending delayed message");

    Queue(42, 2, 21, 3000);
    messages = actor.m_MsgList
        .Where(message => message.wIdent == Grobal2.RM_MAGIC_LVEXP).ToArray();
    Equal(2, messages.Length, "skill-switch message count");
    var flushed = messages.Single(message => message.nParam1 == 58);
    Equal(0, flushed.dwDeliveryTime, "switched skill immediate delivery time");
    Assert(!flushed.boLateDelivery,
        "switched skill notification remained delayed");
    var pending = messages.Single(message => message.nParam1 == 42);
    Assert(pending.boLateDelivery,
        "new skill notification did not start a debounce window");

    Queue(42, 3, 0, 800);
    messages = actor.m_MsgList
        .Where(message => message.wIdent == Grobal2.RM_MAGIC_LVEXP).ToArray();
    Equal(2, messages.Length, "level-up replacement message count");
    pending = messages.Single(message => message.nParam1 == 42);
    Equal(3, pending.nParam2, "level-up replacement level");
    Equal(0, pending.nParam3, "level-up replacement train point");
    Assert(pending.boLateDelivery,
        "level-up replacement did not retain its 800ms delay");
}

static void VerifySourceContracts()
{
    var root = FindRepositoryRoot();
    var actorAttack = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.Attack.cs"));
    var actorState = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.cs"));
    var playerAttack = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Attack.cs"));
    var playerMessage = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    var magicManager = File.ReadAllText(Path.Combine(root, "GameSvr", "Spells",
        "MagicManager.cs"));

    Require(actorState, "public bool m_boSunSwordReady = false;",
        "sun-sword ready state is missing");
    Require(actorState, "public int m_dwLatestSunSwordTick = 0;",
        "sun-sword ready timestamp is missing");
    RequireMatches(magicManager,
        @"case\s+SpellsDef\.SKILL_58\s*:", 1,
        "skill 58 is not classified as a warrior skill");

    var hit = ExtractMethodBody(playerAttack, "ClientHitXY");
    var forgeryGate = RequiredIndex(hit, "wIdent == Grobal2.CM_SWORD_HIT",
        "CM_SWORD_HIT forgery gate");
    var releaseCall = RequiredIndex(hit, "ReleaseSunSword(nDir)",
        "CM_SWORD_HIT release call");
    Assert(forgeryGate < releaseCall,
        "CM_SWORD_HIT reaches release before its ready/skill gate");
    RequireMatches(hit,
        @"wIdent\s*==\s*Grobal2\.CM_SWORD_HIT[\s\S]{0,180}?" +
        @"!m_boSunSwordReady\s*\|\|\s*" +
        @"m_MagicArr\[SpellsDef\.SKILL_58\]\s*==\s*null[\s\S]{0,100}?return",
        1, "forged CM_SWORD_HIT is not rejected fail-closed");
    RequireMatches(hit,
        @"case\s+Grobal2\.CM_SWORD_HIT\s*:[\s\S]{0,160}?ReleaseSunSword\(nDir\)",
        1, "CM_SWORD_HIT does not dispatch to sun-sword release");

    var spell = ExtractMethodBody(playerAttack, "ClientSpellXY");
    var charge = ExtractCaseBlock(spell, "SpellsDef.SKILL_58");
    RequireMatches(charge,
        @"!m_boSunSwordReady\s*&&[\s\S]{0,120}?" +
        @"unchecked\s*\(\s*now\s*-\s*m_dwLatestSunSwordTick\s*\)\s*>=\s*15\s*\*\s*1000",
        1, "skill 58 charge does not enforce ready-state and wrap-safe 15s cooldown");
    Require(charge, "GetSpellPoint(UserMagic)",
        "skill 58 charge does not calculate its first MP cost");
    Require(charge, "m_WAbil.MP >= nSpellPoint",
        "skill 58 does not reject insufficient MP");
    Require(charge, "DamageSpell(nSpellPoint)",
        "skill 58 does not deduct MP");
    Require(charge, "m_boSunSwordReady = true;",
        "skill 58 does not enter ready state");
    Require(charge, "Grobal2.SM_SWORDHIT_ON",
        "skill 58 does not send the 2819 ready acknowledgement");
    Assert(RequiredIndex(charge, "DamageSpell(nSpellPoint)", "skill 58 MP deduction") <
           RequiredIndex(charge, "m_boSunSwordReady = true;", "skill 58 ready assignment"),
        "skill 58 enters ready state before MP deduction");

    var run = ExtractMethodBody(playerMessage, "Run");
    RequireMatches(run,
        @"m_boSunSwordReady\s*&&[\s\S]{0,120}?" +
        @"unchecked\s*\(\s*HUtil32\.GetTickCount\(\)\s*-\s*" +
        @"m_dwLatestSunSwordTick\s*\)\s*>\s*10\s*\*\s*1000",
        1, "Run does not expire sun-sword ready state after 10 seconds");
    var timeout = ExtractControlBlock(run, "if (m_boSunSwordReady", "sun-sword timeout");
    Require(timeout, "m_boSunSwordReady = false;",
        "sun-sword timeout does not clear ready state");
    RequireMatches(timeout,
        @"MakeDefaultMsg\(\s*Grobal2\.SM_SWORDHIT_ON\s*,\s*1\s*,",
        1, "sun-sword timeout does not send the native close acknowledgement");

    var release = ExtractMethodBody(actorAttack, "ReleaseSunSword");
    RequireMatches(release, @"GetSpellPoint\s*\(\s*magic\s*\)", 1,
        "sun-sword release does not calculate its second MP cost");
    RequireMatches(release, @"DamageSpell\s*\(\s*spellPoint\s*\)", 1,
        "sun-sword release does not deduct its second MP cost");
    RequireMatches(release, @"m_boSunSwordReady\s*=\s*false\s*;", 1,
        "sun-sword ready state is not consumed exactly once");
    Assert(RequiredIndex(release, "m_boSunSwordReady = false;",
               "sun-sword ready consumption") <
           RequiredIndex(release, "for (", "sun-sword four-cell loop"),
        "sun-sword ready state is consumed after target processing");
    RequireMatches(release, @"GetAttackPower\s*\(", 1,
        "sun-sword must roll physical DC exactly once per release");
    Require(release, ".NativeLevelBonus",
        "sun-sword effective level omits the native per-skill level bonus");
    Require(release, ".MagicInfo.btTrainLv",
        "sun-sword effective level is not capped by the trained level");
    RequireMatches(release,
        @"unchecked\s*\(\s*\(byte\)\s*\([^)]*\.btLevel\s*\+\s*" +
        @"[^)]*\.NativeLevelBonus\s*\)\s*\)", 1,
        "sun-sword effective level does not retain byte-wrap semantics");
    var scaleCall = RequiredIndex(release, "CalculateSunSwordAttackPower(",
        "sun-sword native/plugin scale call");
    var loopStart = RequiredIndex(release, "for (", "sun-sword four-cell loop");
    var decayCall = RequiredIndex(release, "CalculateSunSwordDistancePower(",
        "sun-sword distance decay call");
    Assert(scaleCall < loopStart && loopStart < decayCall,
        "sun-sword does not scale once before applying per-distance truncation");
    RequireMatches(release,
        @"for\s*\([^;]*distance\s*=\s*1\s*;\s*distance\s*<=\s*4\s*;",
        1, "sun-sword does not traverse exactly distances 1 through 4");
    RequireMatches(release,
        @"GetNextPosition\([^;]*(?:direction|nDir)[^;]*distance[^;]*\)", 1,
        "sun-sword cells are not projected on the requested direction");
    RequireMatches(release,
        @"GetMovingObject\([^;]*,\s*true\s*\)", 1,
        "sun-sword does not select the moving object on each exact axis cell");
    Reject(release, "GetMapBaseObjects",
        "sun-sword incorrectly expands an axis cell into a multi-target area");
    Require(release, "IsProperTarget", "sun-sword does not filter hostile targets");
    var positiveHitCheck = Regex.IsMatch(release,
        @"M2Share\.RandomNumber\.Random\([^)]*\.m_wSpeedPoint\)\s*<\s*m_btHitPoint",
        RegexOptions.CultureInvariant);
    var negativeHitCheck = Regex.IsMatch(release,
        @"M2Share\.RandomNumber\.Random\([^)]*\.m_wSpeedPoint\)\s*>=\s*m_btHitPoint" +
        @"[\s\S]{0,80}?continue\s*;", RegexOptions.CultureInvariant);
    Assert(positiveHitCheck || negativeHitCheck,
        "sun-sword omits the original per-target hit check");

    Require(release, ".GetHitStruckDamage(this,",
        "sun-sword does not apply physical AC");
    Reject(release, "GetMagStruckDamage",
        "sun-sword incorrectly applies magical defense");
    Require(release, ".StruckDamage(",
        "sun-sword does not apply the post-AC damage");
    RequireMatches(release,
        @"SendDelayMsg\([^;]*Grobal2\.RM_STRUCK[^;]*,\s*500\s*\)", 1,
        "sun-sword struck message does not retain the original 500ms delay");
    Reject(release, "Grobal2.RM_SWORD_HIT",
        "client CM_SWORD_HIT path incorrectly adds the server-driven RM_SWORD_HIT action");
    RequireMatches(release, @"Grobal2\.RM_PHYSICAL_ATT", 1,
        "sun-sword does not broadcast RM_PHYSICAL_ATT=10612 exactly once");
    RequireMatches(release, @"BuildSunSwordPhysicalAttackBody\s*\(", 1,
        "sun-sword does not build its 12-byte physical-attack body exactly once");
    var distanceLoop = ExtractControlBlock(release, "for (var distance",
        "sun-sword four-cell loop");
    Reject(distanceLoop, "RM_PHYSICAL_ATT",
        "sun-sword broadcasts RM_PHYSICAL_ATT once per cell instead of once per release");
    Reject(distanceLoop, "BuildSunSwordPhysicalAttackBody",
        "sun-sword rebuilds its physical-attack body inside the four-cell loop");

    RequireMatches(release,
        @"target\.m_btRaceServer\s*>=\s*Grobal2\.RC_ANIMAL[\s\S]{0,100}?" +
        @"canTrain\s*=\s*true",
        1, "sun-sword training is not armed by a race>=50 target");
    RequireMatches(release,
        @"canTrain\s*&&\s*magic\.btLevel\s*<\s*3\s*&&[\s\S]{0,120}?" +
        @"magic\.MagicInfo\.TrainLevel\[magic\.btLevel\]\s*<=\s*m_Abil\.Level",
        1, "sun-sword training omits the native level and train-level gates");
    RequireMatches(release,
        @"TrainSkill\(magic\s*,\s*M2Share\.RandomNumber\.Random\(3\)\s*\+\s*1\)",
        1, "sun-sword training does not use native Random(3)+1 progress");
    RequireMatches(release,
        @"SendUpdateDelayMsg\(\s*this\s*,\s*Grobal2\.RM_MAGIC_LVEXP\s*," +
        @"[\s\S]{0,180}?magic\.nTranPoint\s*,\s*\x22\x22\s*,\s*3000\s*\)",
        1, "sun-sword training does not debounce same-skill progress for 3 seconds");

    var updateDelay = ExtractMethodBody(actorState, "SendUpdateDelayMsg");
    RequireMatches(updateDelay,
        @"wIdent\s*==\s*Grobal2\.RM_MAGIC_LVEXP\s*&&\s*" +
        @"SendMessage\.boLateDelivery[\s\S]{0,160}?" +
        @"SendMessage\.dwDeliveryTime\s*=\s*0\s*;[\s\S]{0,80}?" +
        @"SendMessage\.boLateDelivery\s*=\s*false\s*;",
        1, "switching skills does not immediately flush the previous pending progress");
    var levelUp = ExtractMethodBody(actorState, "CheckMagicLevelup");
    RequireMatches(levelUp,
        @"SendUpdateDelayMsg\(\s*this\s*,\s*Grobal2\.RM_MAGIC_LVEXP\s*," +
        @"[\s\S]{0,180}?UserMagic\.nTranPoint\s*,\s*\x22\x22\s*,\s*800\s*\)",
        1, "skill level-up does not replace pending progress with the latest snapshot");

    RequireMatches(playerMessage,
        @"case\s+Grobal2\.CM_SWORD_HIT\s*:", 1,
        "CM_SWORD_HIT=3002 is absent from the client dispatcher");
    var nativeLabelPattern = @"case\s+Grobal2\.RM_SWORD_HIT\s*:";
    var nativeLabels = Regex.Matches(playerMessage, nativeLabelPattern,
        RegexOptions.CultureInvariant).Count;
    var groupedNativeMapping = Regex.IsMatch(playerMessage,
        nativeLabelPattern + @"\s*subCode\s*=\s*Grobal2\.SM_SWORD_HIT\s*;",
        RegexOptions.CultureInvariant);
    var directNativeMapping = Regex.IsMatch(playerMessage,
        nativeLabelPattern + @"[\s\S]{0,500}?" +
        @"MakeDefaultMsg\(Grobal2\.SM_SWORD_HIT",
        RegexOptions.CultureInvariant);
    Assert((groupedNativeMapping && nativeLabels >= 2) || directNativeMapping,
        "RM_SWORD_HIT=10023 does not dispatch to SM_SWORD_HIT=2");
    var compatActionName = playerMessage.Contains(
        "case Grobal2.RM_PHYSICAL_ATT:", StringComparison.Ordinal)
        ? "Grobal2.RM_PHYSICAL_ATT"
        : "Grobal2.RM_NATIVE_UNION_EFFECT";
    var compatAction = ExtractCaseBlock(playerMessage, compatActionName);
    Require(compatAction, "Grobal2.SM_PHYSICAL_ATT",
        "RM_PHYSICAL_ATT does not dispatch to SM_PHYSICAL_ATT=1230");
    Require(compatAction, "GetQueuedPayloadBytes(ProcessMsg)",
        "SM_PHYSICAL_ATT dispatcher drops the 12-byte body");
}

static int InvokePower(int power, int effectiveLevel, YanshenApi api) =>
    (int)InvokeStatic("CalculateSunSwordAttackPower",
        new[] { typeof(int), typeof(int), typeof(YanshenApi) },
        power, effectiveLevel, api);

static int InvokeDistancePower(int scaledPower, int distance) =>
    (int)InvokeStatic("CalculateSunSwordDistancePower",
        new[] { typeof(int), typeof(int) }, scaledPower, distance);

static byte[] InvokeBody(int skillLevel, int direction, int x, int y) =>
    (byte[])InvokeStatic("BuildSunSwordPhysicalAttackBody",
        new[] { typeof(int), typeof(int), typeof(int), typeof(int) },
        skillLevel, direction, x, y);

static object InvokeStatic(string name, Type[] parameterTypes, params object[] arguments)
{
    var method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, types: parameterTypes, modifiers: null)
        ?? throw new MissingMethodException(typeof(TBaseObject).FullName, name);
    try
    {
        return method.Invoke(null, arguments)
            ?? throw new InvalidOperationException(name + " returned null");
    }
    catch (TargetInvocationException exception) when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }
}

static int ReadProtocolConstant(string name)
{
    var field = typeof(Grobal2).GetField(name,
        BindingFlags.Static | BindingFlags.Public)
        ?? throw new MissingFieldException(typeof(Grobal2).FullName, name);
    return (int)(field.GetRawConstantValue()
        ?? throw new InvalidOperationException(name + " has no constant value"));
}

static string ExtractMethodBody(string source, string methodName)
{
    var declaration = Regex.Match(source,
        @"(?:public|private|protected|internal)\s+" +
        @"(?:(?:static|virtual|override|sealed|async|new)\s+)*" +
        @"[A-Za-z_][A-Za-z0-9_<>,\[\]?\.]*\s+" +
        Regex.Escape(methodName) + @"\s*\(",
        RegexOptions.CultureInvariant);
    Assert(declaration.Success, "source method was not found: " + methodName);
    var open = source.IndexOf('{', declaration.Index + declaration.Length);
    Assert(open >= 0, "source method has no body: " + methodName);
    return ExtractBraceBlock(source, open, methodName);
}

static string ExtractCaseBlock(string switchBody, string caseValue)
{
    var startMatch = Regex.Match(switchBody,
        @"case\s+" + Regex.Escape(caseValue) + @"\s*:",
        RegexOptions.CultureInvariant);
    Assert(startMatch.Success, "source case was not found: " + caseValue);
    var next = Regex.Match(switchBody.Substring(startMatch.Index + startMatch.Length),
        @"\n\s*(?:case\s+[^:]+:|default\s*:)", RegexOptions.CultureInvariant);
    var end = next.Success
        ? startMatch.Index + startMatch.Length + next.Index
        : switchBody.Length;
    return switchBody.Substring(startMatch.Index, end - startMatch.Index);
}

static string ExtractControlBlock(string source, string marker, string description)
{
    var markerIndex = RequiredIndex(source, marker, description);
    var open = source.IndexOf('{', markerIndex);
    Assert(open >= 0, description + " opening brace is missing");
    return ExtractBraceBlock(source, open, description);
}

static string ExtractBraceBlock(string source, int open, string description)
{
    var depth = 0;
    for (var index = open; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        else if (source[index] == '}' && --depth == 0)
            return source.Substring(open, index - open + 1);
    }
    throw new InvalidOperationException(description + " source body is incomplete");
}

static int RequiredIndex(string source, string value, string description)
{
    var index = source.IndexOf(value, StringComparison.Ordinal);
    if (index < 0) throw new InvalidOperationException(description + " is missing");
    return index;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj was not found");
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

static void RequireMatches(string source, string pattern, int expected, string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant).Count;
    if (actual != expected)
        throw new InvalidOperationException(
            $"{message}: expected {expected} match(es), actual {actual}");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void EqualSequence(int[] expected, int[] actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{message}: expected=[{string.Join(',', expected)}], " +
            $"actual=[{string.Join(',', actual)}]");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
