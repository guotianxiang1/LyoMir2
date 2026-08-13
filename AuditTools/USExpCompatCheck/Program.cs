using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
PrepareRuntime();

var bridge = new PasApiBridge();
var nativeArgs = Args(2, 7, 20);
bridge.CurrentPlayer = NewOwner(10);
Assert(bridge.CallPlayerFunc("AddUSExp", nativeArgs, out var noPlayer) &&
       noPlayer.AsInt() == -1, "missing player/hero return");
Assert(!bridge.CallPlayerFunc("AddUSExp", Args(1, 2), out var badArity) &&
       badArity.Type == PasValueType.Nil, "non-native arity");
Assert(!bridge.CallPlayerMethod("AddUSExp", nativeArgs),
    "AddUSExp was exposed as a procedure");

var owner = NewOwner(10);
var hero = NewHero(owner);
owner.m_HeroObject = hero;
bridge.CurrentPlayer = owner;

Equal(-2, Call(bridge, 2, 7, 20), "missing union magic");
var magic = AddUnionMagic(hero, trainLevel: 10, maxLevel: 3);
Equal(-3, Call(bridge, 2, 7, 20), "missing union item");
var item = EquipUnionItem(hero, dura: 6, duraMax: 100);
hero.m_Abil.Level = 20;
Equal(-5, Call(bridge, 2, 7, 20), "insufficient union power");
item.Dura = 100;
hero.m_Abil.Level = 9;
Equal(-6, Call(bridge, 2, 7, 20), "hero level requirement");
Equal(100, item.Dura, "rejected training changed Dura");
Equal(10, owner.m_nLingFu, "rejected training changed LingFu");

hero.m_Abil.Level = 20;
ResetObserved(owner, hero);
Equal(1, Call(bridge, 2, 7, 20), "basic success");
Equal(20, magic.nTranPoint, "basic skill experience");
Equal(93, item.Dura, "basic union power");
Equal(8, owner.m_nLingFu, "basic LingFu debit");
Equal(2, owner.m_nUsedLingFu, "basic used LingFu");
Assert(owner.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_LINGFU_CHANGED) == 1,
    "capital refresh count");
Equal(2, hero.m_MsgList.Count, "hero notification count");
var magicMessage = hero.m_MsgList.Single(message =>
    message.wIdent == Grobal2.RM_MAGIC_LVEXP);
Equal(50, magicMessage.nParam1, "magic notification id");
Equal(0, magicMessage.nParam2, "magic notification level");
Equal(20, magicMessage.nParam3, "magic notification current train");
Equal(300, BitConverter.ToInt32((byte[])magicMessage.Payload, 0),
    "magic notification required train body");
var duraMessage = hero.m_MsgList.Single(message =>
    message.wIdent == Grobal2.RM_DURACHANGE);
Equal(Grobal2.U_BUJUK, duraMessage.wParam, "Dura slot");
Equal(93, duraMessage.nParam1, "Dura current");
Equal(100, duraMessage.nParam2, "Dura maximum");
Assert(M2Share.LogStringList.Cast<string>().Single().Contains(
        "\t灵符\t30004\t2\t", StringComparison.Ordinal),
    "native reason-30004 debit log");

owner.m_nLingFu = 10;
owner.m_nUsedLingFu = 0;
magic.btLevel = 0;
magic.nTranPoint = 250;
item.Dura = 100;
ResetObserved(owner, hero);
Equal(1, Call(bridge, 1, 1, 700), "multi-level success");
Equal(2, magic.btLevel, "multi-level result level");
Equal(150, magic.nTranPoint, "multi-level remainder");
Assert(hero.m_MsgList.Select(message => message.wIdent).SequenceEqual(
        new[]
        {
            Grobal2.RM_MAGIC_LVEXP, Grobal2.RM_ABILITY,
            Grobal2.RM_MAGIC_LVEXP, Grobal2.RM_DURACHANGE
        }),
    "multi-level native notification order");
var magicMessages = hero.m_MsgList.Where(message =>
    message.wIdent == Grobal2.RM_MAGIC_LVEXP).ToArray();
Equal(2, magicMessages.Length, "multi-level magic notification count");
magicMessage = magicMessages[1];
Equal(700, BitConverter.ToInt32((byte[])magicMessage.Payload, 0),
    "multi-level next threshold");

owner.m_nLingFu = 10;
magic.btLevel = 1;
magic.nTranPoint = 0;
item.Dura = 100;
hero.m_Abil.Level = 19;
ResetObserved(owner, hero);
Equal(-6, Call(bridge, 1, 1, 10), "current-level actor requirement");
Equal(0, magic.nTranPoint, "actor-level rejection changed experience");
Equal(100, item.Dura, "actor-level rejection changed Dura");
Equal(10, owner.m_nLingFu, "actor-level rejection changed LingFu");

owner.m_nLingFu = 10;
magic.btLevel = 0;
magic.nTranPoint = 0;
item.Dura = 100;
hero.m_boFastTrain = true;
ResetObserved(owner, hero);
Equal(1, Call(bridge, 1, 0, 10), "fast training success");
Equal(30, magic.nTranPoint, "fast training multiplier");
hero.m_boFastTrain = false;

owner.m_nLingFu = 10;
magic.btLevel = 2;
magic.nTranPoint = 690;
item.Dura = 100;
hero.m_Abil.Level = 30;
ResetObserved(owner, hero);
Equal(1, Call(bridge, 1, -5, 10), "negative-power success");
Equal(3, magic.btLevel, "negative-power level changed");
Equal(0, magic.nTranPoint, "negative-power skill experience");
Equal(105, item.Dura, "native signed power subtraction");

owner.m_nLingFu = 10;
magic.btLevel = 3;
magic.nTranPoint = 0;
item.Dura = 100;
hero.m_Abil.Level = 30;
ResetObserved(owner, hero);
Equal(-6, Call(bridge, 1, 1, 10), "max-level actor requirement");
Equal(0, magic.nTranPoint, "max-level rejection changed experience");
Equal(100, item.Dura, "max-level rejection changed Dura");
Equal(10, owner.m_nLingFu, "max-level rejection changed LingFu");

owner.m_nLingFu = 10;
magic.btLevel = 0;
magic.nTranPoint = 0;
item.Dura = 100;
hero.m_Abil.Level = 30;
ResetObserved(owner, hero);
Equal(1, Call(bridge, 1, 1, -1), "negative experience native unsigned loop");
Equal(3, magic.btLevel, "negative experience result level");
Equal(-1501, magic.nTranPoint, "negative experience remainder");
magicMessage = hero.m_MsgList.Last(message =>
    message.wIdent == Grobal2.RM_MAGIC_LVEXP);
Equal(-1, BitConverter.ToInt32((byte[])magicMessage.Payload, 0),
    "negative experience next threshold");

owner.m_nLingFu = 0;
var snapshot = (magic.btLevel, magic.nTranPoint, item.Dura);
Equal(-4, Call(bridge, 1, 1, 1), "insufficient LingFu");
Equal(snapshot, (magic.btLevel, magic.nTranPoint, item.Dura),
    "LingFu rejection side effects");

Console.WriteLine(
    "PASS USExp returns=-1..-6/1 TrainLevel/MaxTrain fast=x3 notifications=916/919 debit=30004");

static int Call(PasApiBridge bridge, params int[] values)
{
    Assert(bridge.CallPlayerFunc("AddUSExp", Args(values), out var result),
        "AddUSExp dispatch");
    return result.AsInt();
}

static List<PasValue> Args(params int[] values)
{
    return values.Select(PasValue.FromInt).ToList();
}

static TPlayObject NewOwner(int lingFu)
{
    return new TPlayObject
    {
        m_nLingFu = lingFu,
        m_sMapName = "usexp-map",
        m_sCharName = "usexp-player",
        m_nCurrX = 12,
        m_nCurrY = 34
    };
}

static HeroObject NewHero(TPlayObject owner)
{
    return new HeroObject
    {
        m_Master = owner,
        MasterName = owner.m_sCharName
    };
}

static TUserMagic AddUnionMagic(HeroObject hero, byte trainLevel,
    byte maxLevel)
{
    var magic = new TUserMagic
    {
        wMagIdx = 50,
        MagicInfo = new TMagic
        {
            wMagicID = 50,
            sMagicName = "合击",
            btTrainLv = maxLevel,
            TrainLevel = new byte[] { trainLevel, 20, 30, 30 },
            MaxTrain = new[] { 300, 500, 700, 700 }
        }
    };
    hero.m_HeroMagicList.Add(magic);
    var owner = (TPlayObject)hero.m_Master;
    var wasOffline = owner.m_boOffLineFlag;
    owner.m_boOffLineFlag = true;
    try
    {
        hero.SendHeroLogon();
    }
    finally
    {
        owner.m_boOffLineFlag = wasOffline;
    }
    ResetObserved(owner, hero);
    return magic;
}

static TUserItem EquipUnionItem(HeroObject hero, ushort dura,
    ushort duraMax)
{
    var item = new TUserItem { wIndex = 1, Dura = dura, DuraMax = duraMax };
    hero.m_UseItems[Grobal2.U_BUJUK] = item;
    return item;
}

static void ResetObserved(TPlayObject owner, HeroObject hero)
{
    owner.m_MsgList.Clear();
    hero.m_MsgList.Clear();
    M2Share.LogStringList.Clear();
}

static void PrepareRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.LogMsgCriticalSection ??= new object();
    M2Share.ProcessMsgCriticalSection ??= new object();
    M2Share.LogStringList ??= new ArrayList();
    M2Share.CreditCardService = NativeCreditCardService.Disabled;
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "火龙之心",
        StdMode = 25,
        Shape = 7,
        DuraMax = 100
    });
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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}
