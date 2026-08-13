using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckPlayerProductionLifecycle();
CheckHeroLifecycle();
CheckSharedShieldCarrierAndConsumers();
CheckAdmissionGateAndExitLifetime();
CheckClientWire();
CheckSourceContracts();

Console.WriteLine(
    "PASS timed-type27 PAS=player+hero+Word-coercion+low-byte-alias " +
    "internal=59 runtime-state-only recalc=none zero-second=next-500ms-expiry " +
    "3555=add-body10+remove-body0 hero=no-direct-3555 " +
    "shield=independent-ushort-carrier physical+magic-consumers " +
    "persistence=none state52=no-node");
return;

void CheckPlayerProductionLifecycle()
{
    var player = NewPlayer("type27-player");
    Array.Fill(player.m_NativeHumanData, (byte)0x5A);
    var persistentBefore = player.m_NativeHumanData.ToArray();
    var bridge = new PasApiBridge { CurrentPlayer = player };

    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(283, 65_535, 0)), "player low-byte alias dispatch");
    Assert(player.HasTimedAbility(27), "player type27 node");
    Assert(player.HasNativeActiveState(59), "player internal state59");
    Equal(0, GetTimedNodeField<byte>(player, "Flag"),
        "player native node flag");
    Equal(59, GetTimedNodeField<byte>(player, "InternalType"),
        "player native node internal type");
    Equal(65_535, player.GetTimedAbilityValue(27), "player Word value");
    Equal(0, player.GetTimedAbilityRemainingMilliseconds(27),
        "player zero-second duration");
    Equal(1, player.TimedStateCount, "player add 3555 hook count");
    Equal(59, player.LastInternalType, "player add internal type");
    Equal(0, player.LastRemaining, "player add remaining");
    Equal(65_535, player.LastValue, "player add value");
    Assert(!player.LastRemoved, "player add marked removal");
    Equal(1, CountMessages(player, Grobal2.RM_CHARSTATUSCHANGED),
        "player add status message");

    player.ConsumePendingRecalc();
    Equal(0, player.RecalcCount, "type27 add requested ability recalc");

    int lastTick = GetTimedNodeField<int>(player, "LastTick");
    SetBaseField(player, "m_TimedAbilityProcessTick", lastTick);
    player.ProcessTimedAbilities(unchecked(lastTick + 499));
    Assert(player.HasTimedAbility(27),
        "zero-second node expired before the 500ms scan");
    player.ProcessTimedAbilities(unchecked(lastTick + 500));
    Assert(!player.HasTimedAbility(27), "zero-second node did not expire");
    Assert(!player.HasNativeActiveState(59),
        "zero-second expiry retained state59");
    Equal(2, player.TimedStateCount, "player removal 3555 hook count");
    Assert(player.LastRemoved, "player expiry did not mark removal");
    Equal(2, CountMessages(player, Grobal2.RM_CHARSTATUSCHANGED),
        "player expiry status message");
    player.ConsumePendingRecalc();
    Equal(0, player.RecalcCount, "type27 expiry requested ability recalc");
    Assert(persistentBefore.SequenceEqual(player.m_NativeHumanData),
        "type27 changed persistent human data");
}

void CheckHeroLifecycle()
{
    var owner = NewPlayer("type27-owner");
    var hero = new Type27ProbeHero
    {
        m_Master = owner,
        m_sCharName = "type27-hero"
    };
    owner.m_HeroObject = hero;
    owner.m_DefMsg = null;
    var bridge = new PasApiBridge { CurrentPlayer = owner };

    Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(27, 7, 1)),
        "hero type27 dispatch");
    Assert(hero.HasTimedAbility(27) && hero.HasNativeActiveState(59),
        "hero type27/internal59 state");
    Assert(!owner.HasTimedAbility(27) && !owner.HasNativeActiveState(59),
        "hero type27 leaked to owner");
    Assert(owner.m_DefMsg == null, "hero sent direct 3555 to owner");
    var heroTimedHook = typeof(HeroObject).GetMethod(
        "SendTimedAbilityClientState",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(heroTimedHook?.DeclaringType == typeof(TBaseObject),
        "hero overrides the player-only 3555 hook");

    hero.ConsumePendingRecalc();
    Equal(0, hero.RecalcCount, "hero type27 add requested recalc");
    Assert(hero.RemoveTimedAbility(27), "hero type27 removal");
    hero.ConsumePendingRecalc();
    Equal(0, hero.RecalcCount, "hero type27 removal requested recalc");
}

void CheckSharedShieldCarrierAndConsumers()
{
    var actor = NewPlayer("type27-shared-state");
    actor.m_btJob = M2Share.jWarr;
    actor.m_WAbil.AC = HUtil32.MakeLong(0, 0);
    actor.m_WAbil.DC = HUtil32.MakeLong(0, 40);
    SetShieldCharges(actor, 2);
    actor.SetNativeActiveState(59);

    actor.AddTimedAbility(27, 65_535, 0);
    Equal(2, ShieldCharges(actor), "type27 add changed shield charges");
    int lastTick = GetTimedNodeField<int>(actor, "LastTick");
    SetBaseField(actor, "m_TimedAbilityProcessTick", lastTick);
    actor.ProcessTimedAbilities(unchecked(lastTick + 500));
    Equal(2, ShieldCharges(actor), "type27 expiry changed shield charges");
    Assert(!actor.HasNativeActiveState(59),
        "type27 expiry did not clear the shared active bit");

    actor.SetNativeActiveState(59);
    // MOVED to the native stage: sub_73F9FC @0x73FB5F consumes the charge
    // inside StruckDamage (stage 11/19), NOT in the armour getter
    // sub_767958, which ends at the bubble tail-call @0x7679A9 and never
    // reads word [+0x3FC]. So GetHitStruckDamage must leave 150 alone and
    // leave the charge count untouched.
    Equal(150, actor.GetHitStruckDamage(null, 150),
        "armour getter must not consume the charge shield");
    Equal(2, ShieldCharges(actor),
        "armour getter consumed a charge (native sub_767958 never does)");
    // Now the real consumer. HiWord(DC)=40 -> trunc(40*2.5)=100 (native
    // @0x73FB75 fild/fmul [0x73FBE4]=2.5/call sub_403580 = truncating fistp).
    // 150-100 = 50 lands on HP.
    actor.m_WAbil.HP = 500;
    actor.m_WAbil.MaxHP = 500;
    actor.StruckDamage(150);
    Equal(450, actor.m_WAbil.HP,
        "StruckDamage charge shield reduction reached HP");
    Equal(1, ShieldCharges(actor), "physical shield charge");
    Equal(50, ApplyMagicShield(actor, 150), "magic shield consumer");
    Equal(0, ShieldCharges(actor), "magic shield charge");
    Assert(!actor.HasNativeActiveState(59),
        "shield exhaustion retained state59");
}

void CheckAdmissionGateAndExitLifetime()
{
    var blocked = NewPlayer("type27-state52");
    blocked.SetNativeActiveState(52);
    var bridge = new PasApiBridge { CurrentPlayer = blocked };
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(27, 1, 60)),
        "state52 call was not handled");
    Assert(!blocked.HasTimedAbility(27) &&
           !blocked.HasNativeActiveState(59),
        "state52 admitted type27");

    var exiting = NewPlayer("type27-exit");
    exiting.AddTimedAbility(27, 1, 60);
    Assert(exiting.HasTimedAbility(27), "exit setup node");
    exiting.ClearTimedStateOnExit();
    Assert(!exiting.HasTimedAbility(27) &&
           !exiting.HasNativeActiveState(59),
        "exit retained runtime type27 state");
    Assert(!NewPlayer("type27-fresh").HasTimedAbility(27),
        "fresh actor inherited type27 state");
}

void CheckClientWire()
{
    var added = BuildTimedAbilityPacket(59, 0, 65_535, false);
    Equal(3555, added.Header.Ident, "add packet ident");
    Equal(0, added.Header.Recog, "add packet recog");
    Equal(59, added.Header.Param, "add packet param");
    Equal(10, added.Body.Length, "add packet body length");
    Equal(59, added.Body[0], "add packet body type");
    Equal(0, added.Body[1], "add packet padding");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
        added.Body.AsSpan(2, 4)), "add packet body remaining");
    Equal(65_535, BinaryPrimitives.ReadInt32LittleEndian(
        added.Body.AsSpan(6, 4)), "add packet body value");

    var removed = BuildTimedAbilityPacket(59, -500, 65_535, true);
    Equal(3555, removed.Header.Ident, "remove packet ident");
    Equal(0, removed.Header.Recog, "remove packet recog");
    Equal(59, removed.Header.Param, "remove packet param");
    Equal(0, removed.Body.Length, "remove packet body length");
}

void CheckSourceContracts()
{
    string root = FindRepositoryRoot();
    string timed = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.TimedAbility.cs"));
    string shield = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeSkill153Shield.cs"));
    string physical = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.cs"));
    string magic = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeMagicDamage.cs"));

    Require(timed, @"\b17\s+or\s+27\s+or\s+43\b",
        "type27 admission is missing");
    CheckInternal59RequestsNoRecalc();
    Reject(timed, @"case\s+27\s*:",
        "type27 incorrectly mutates working ability fields");
    Require(shield, @"m_wNativeSkill153ShieldCharges",
        "shared shield charge carrier");
    Reject(shield, @"AddTimedAbility\s*\(",
        "shield producer reused the type27 timed node");
    Require(physical, @"ConsumeNativeSkill153ShieldCharge\(nDamage\)",
        "physical received-damage consumer");
    // sub_73F9FC @0x73FB5F consumes word [+0x3FC] INSIDE StruckDamage, after
    // the durability worker (@0x73FB5A). sub_767958/sub_7679B8 both end at
    // `call sub_76FFE8` + `ret 4` (@0x7679AE / @0x767A0E) and never touch
    // +0x3FC, so the armour getters must NOT consume a charge.
    Reject(physical, @"ConsumeNativeSkill153ShieldCharge\(result\)",
        "charge shield consumed in the armour getter, not StruckDamage "
        + "(native site is sub_73F9FC @0x73FB5F)");
    Require(magic, @"ApplyNativeSkill153ShieldToMagicDamage\(damage\)",
        "magic received-damage consumer");

    var persistedSources = Directory.GetFiles(
            Path.Combine(root, "GameSvr", "DataStores"), "*.cs",
            SearchOption.AllDirectories)
        .Select(File.ReadAllText);
    Reject(string.Join(Environment.NewLine, persistedSources),
        @"m_TimedAbilityHead",
        "timed list entered persistence codecs");
}

// The recalc decision used to be an exclusion list, and this audit grepped for
// its `internalType != NativeSkill153ShieldState` literal. The STATE-41 rewrite
// replaced the list with the native bitmap at 0x77326C, so the literal is gone
// and the text expectation went stale. Assert the decision instead of its
// spelling, straight off the leaf at 0x773254:
//   0x773254  80 C2 F8              add dl,0xF8   ; index = internalType - 8
//   0x773257  80 FA 67              cmp dl,0x67
//   0x77325A  77 0A                 ja  0x773266  ; out of range -> false
//   0x77325F  0F A3 15 6C 32 77 00  bt  [0x77326C],edx
// 44 and 76 are in the native set and 59 is not. Including 44 and 76 is what
// gives this teeth: both go false if the -8 bias is ever dropped again, whereas
// 59 reads false under either indexing and cannot detect the regression alone.
static void CheckInternal59RequestsNoRecalc()
{
    var decide = typeof(TBaseObject).GetMethod("RequiresTimedAbilityRecalc",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(decide != null,
        "RequiresTimedAbilityRecalc (sub_773254) reflection target missing");

    bool Requires(byte internalType) =>
        (bool)decide.Invoke(null, new object[] { internalType })!;

    Assert(!Requires(59), "internal59 incorrectly requests ability recalc");
    Assert(Requires(44),
        "internal44 lost its recalc: bit index must be internalType - 8 "
        + "(0x773254 add dl,0xF8)");
    Assert(Requires(76),
        "internal76 lost its recalc: bit index must be internalType - 8");
    Assert(!Requires(7),
        "internal7 is below the biased domain and must be refused "
        + "(0x77325A ja, unsigned)");
}

static Type27ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_boObMode = true,
    m_PEnvir = new Envirnoment(),
    m_sCharName = name,
    m_NativeHumanData = new byte[0xEEF8]
};

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static int CountMessages(TBaseObject actor, int ident) =>
    actor.m_MsgList.Count(message => message.wIdent == ident);

static T GetTimedNodeField<T>(TBaseObject actor, string name)
{
    var head = typeof(TBaseObject).GetField("m_TimedAbilityHead",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingMemberException("m_TimedAbilityHead");
    return (T)(head.GetType().GetField(name)?.GetValue(head)
        ?? throw new MissingFieldException(name));
}

static void SetBaseField(TBaseObject actor, string name, object value)
{
    var field = typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    field.SetValue(actor, value);
}

static ushort ShieldCharges(TBaseObject actor) =>
    (ushort)(typeof(TBaseObject).GetField(
        "m_wNativeSkill153ShieldCharges",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingFieldException("m_wNativeSkill153ShieldCharges"));

static void SetShieldCharges(TBaseObject actor, ushort value)
{
    var field = typeof(TBaseObject).GetField(
        "m_wNativeSkill153ShieldCharges",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("m_wNativeSkill153ShieldCharges");
    field.SetValue(actor, value);
}

static int ApplyMagicShield(TBaseObject actor, int damage)
{
    var method = typeof(TBaseObject).GetMethod(
        "ApplyNativeSkill153ShieldToMagicDamage",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "ApplyNativeSkill153ShieldToMagicDamage");
    return (int)(method.Invoke(actor, new object[] { damage })
        ?? throw new InvalidOperationException("magic shield result"));
}

static (ClientPacket Header, byte[] Body) BuildTimedAbilityPacket(byte type,
    int remaining, int value, bool removed)
{
    var method = typeof(TBaseObject).GetMethod(
        "BuildTimedAbilityClientState",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildTimedAbilityClientState");
    var tuple = method.Invoke(null,
            new object[] { type, remaining, value, removed })
        ?? throw new InvalidOperationException("3555 builder returned null");
    var tupleType = tuple.GetType();
    var header = (ClientPacket)(tupleType.GetField("Item1")?.GetValue(tuple)
        ?? throw new MissingFieldException(tupleType.FullName, "Item1"));
    var body = (byte[])(tupleType.GetField("Item2")?.GetValue(tuple)
        ?? throw new MissingFieldException(tupleType.FullName, "Item2"));
    return (header, body);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root was not found");
}

static void Require(string source, string pattern, string message) =>
    Assert(Regex.IsMatch(source, pattern, RegexOptions.Singleline), message);

static void Reject(string source, string pattern, string message) =>
    Assert(!Regex.IsMatch(source, pattern, RegexOptions.Singleline), message);

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
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
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Type27ProbePlayer : TPlayObject
{
    public int RecalcCount { get; private set; }
    public int TimedStateCount { get; private set; }
    public byte LastInternalType { get; private set; }
    public int LastRemaining { get; private set; }
    public int LastValue { get; private set; }
    public bool LastRemoved { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();
    public void ClearTimedStateOnExit() => ClearTimedAbilitiesOnExit();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }

    protected override void SendTimedAbilityClientState(byte internalType,
        int remainingMilliseconds, int value, bool removed)
    {
        TimedStateCount++;
        LastInternalType = internalType;
        LastRemaining = remainingMilliseconds;
        LastValue = value;
        LastRemoved = removed;
    }
}

sealed class Type27ProbeHero : HeroObject
{
    public int RecalcCount { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }
}
