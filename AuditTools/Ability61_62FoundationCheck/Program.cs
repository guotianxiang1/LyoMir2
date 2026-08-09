using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

VerifyPlayerNativeItemSources();
VerifyStaticSourcesIgnoreNativeRecord();
VerifyExtensionTerminationRules();
VerifyNativeType74Foundation();
VerifyHeroUsesSharedRecalculation();
VerifyTimedCoreWritesAndWraps();
VerifyPhase2AdmissionBoundary();

Console.WriteLine(
    "PASS ability61/62 foundation equipment=stditem-static+word-wrap " +
    "extensions=stditem-six-slot+raw-pairs-ignored+invalid-break " +
    "actors=player+hero timed-core=type61-strength+type62-resistance " +
    "admission=type61+type62-open");
return;

static void VerifyPlayerNativeItemSources()
{
    ResetItems();
    AddSpecialStdItem(5, 61, 65535, 0);
    AddSpecialStdItem(6, 63, 2, 0);
    AddSpecialStdItem(29, 1, 0, 3);
    AddSpecialStdItem(5, 60, 1000, 0);

    var player = NewPlayer("native-item-player");
    player.m_UseItems[0] = Item(1, Record((0x48, 123)), 60);
    player.m_UseItems[1] = Item(2, Record((0x48, 456)), 63);
    player.m_UseItems[2] = Item(3, Record((0x15, 9), (0x4C, 789)));
    player.m_UseItems[3] = Item(4, Record((0x48, 1000)), 61);
    player.RecalcAbilitys();

    Equal(2, player.m_wEffectStrength,
        "special native strength sources and UInt16 wrap");
    Equal(0, player.m_wEffectResistance,
        "special native strength sources changed resistance");
}

static void VerifyStaticSourcesIgnoreNativeRecord()
{
    ResetItems();
    AddSpecialStdItem(5, 61, 7, 0);
    AddSpecialStdItem(29, 1, 0, 9);
    var player = NewPlayer("record-length-player");
    player.m_UseItems[0] = Item(1, new byte[207]);
    player.m_UseItems[1] = Item(2, null);
    player.RecalcAbilitys();
    Equal(16, player.m_wEffectStrength,
        "static strength depended on embedded native record");
    Equal(0, player.m_wEffectResistance, "missing native record was parsed");
}

static void VerifyExtensionTerminationRules()
{
    ResetItems();
    AddStdItem(0, 0, (1, 999), (254, 777), (30, 65535),
        (54, 65535), (30, 2), (54, 2));
    AddStdItem(0, 0, (159, 1), (30, 500));
    AddStdItem(0, 0, (30, 5), (0, 0), (54, 500));
    AddStdItem(0, 0);
    var player = NewPlayer("extension-player");

    var valid = Record();
    player.m_UseItems[0] = Item(1, valid);

    var invalidHigh = Record();
    player.m_UseItems[1] = Item(2, invalidHigh);

    var invalidZero = Record();
    player.m_UseItems[2] = Item(3, invalidZero);

    var rawIgnored = Record();
    Pair(rawIgnored, 0, 30, 500);
    Pair(rawIgnored, 1, 54, 500);
    player.m_UseItems[3] = Item(4, rawIgnored);

    player.RecalcAbilitys();
    Equal(6, player.m_wEffectResistance,
        "ID30 accumulation, ID254 continuation, or invalid-ID break");
    Equal(1, player.m_wEffectStrength,
        "ID54 UInt16 wrap or zero-ID termination");
}

static void VerifyNativeType74Foundation()
{
    ResetItems();
    AddStdItem(0, 0, (158, 65535));
    AddStdItem(0, 0, (158, 2));
    var player = NewPlayer("type74-player");
    player.m_UseItems[0] = Item(1, Record());
    player.m_UseItems[1] = Item(2, Record());
    player.RecalcAbilitys();

    var field = typeof(TBaseObject).GetField("m_wNativeType74MagicHit",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("m_wNativeType74MagicHit");
    Equal(1, (ushort)(field.GetValue(player) ?? ushort.MaxValue),
        "ID158 magic-hit extension UInt16 wrap");

    var packetBuilder = typeof(TBaseObject).GetMethod(
        "BuildNativeAbilityPacket",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildNativeAbilityPacket");
    var packet = (byte[])(packetBuilder.Invoke(player, null)
        ?? throw new InvalidOperationException("native ability packet"));
    Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0x10, 2)),
        "type74 native ability packet slot");
}

static void VerifyHeroUsesSharedRecalculation()
{
    ResetItems();
    AddSpecialStdItem(6, 62, 7, 0, (30, 11), (54, 13));
    var hero = new HeroObject { m_sCharName = "native-item-hero" };
    var record = Record((0x48, 7));
    hero.m_UseItems[0] = Item(1, record, 60);
    hero.RecalcAbilitys();
    Equal(11, hero.m_wEffectResistance, "hero extension resistance");
    Equal(20, hero.m_wEffectStrength, "hero special and extension strength");
}

static void VerifyTimedCoreWritesAndWraps()
{
    ResetItems();
    AddStdItem(0, 0, (30, 65534), (54, 65533));
    var player = NewPlayer("timed-core-player");
    var record = Record();
    player.m_UseItems[0] = Item(1, record);
    InjectTimedNodes(player, (61, 5), (62, 4));
    player.RecalcAbilitys();
    Equal(2, player.m_wEffectStrength, "type61 timed strength UInt16 wrap");
    Equal(2, player.m_wEffectResistance, "type62 timed resistance UInt16 wrap");

    var hero = new HeroObject();
    InjectTimedNodes(hero, (61, 65535), (62, 65535));
    hero.RecalcAbilitys();
    Equal(65535, hero.m_wEffectStrength, "hero type61 timed strength");
    Equal(65535, hero.m_wEffectResistance, "hero type62 timed resistance");
}

static void VerifyPhase2AdmissionBoundary()
{
    var actor = NewPlayer("phase2-admission-player");
    actor.AddTimedAbility(61, 10, 60);
    actor.AddTimedAbility(62, 10, 60);
    Assert(actor.HasTimedAbility(61), "type61 admission remained closed");
    Assert(actor.HasNativeActiveState(93),
        "type61 admission did not create internal state93");
    Assert(actor.HasTimedAbility(62), "type62 admission remained closed");
    Assert(actor.HasNativeActiveState(94),
        "type62 admission did not create internal state94");
}

static void InjectTimedNodes(TBaseObject actor, params (int Type, int Value)[] nodes)
{
    var nodeType = typeof(TBaseObject).GetNestedType("TimedAbilityNode",
        BindingFlags.NonPublic) ?? throw new MissingMemberException("TimedAbilityNode");
    var headField = typeof(TBaseObject).GetField("m_TimedAbilityHead",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("m_TimedAbilityHead");
    var internalTypeField = nodeType.GetField("InternalType")
        ?? throw new MissingFieldException("InternalType");
    var valueField = nodeType.GetField("Value")
        ?? throw new MissingFieldException("Value");
    var remainingField = nodeType.GetField("RemainingMilliseconds")
        ?? throw new MissingFieldException("RemainingMilliseconds");
    var nextField = nodeType.GetField("Next")
        ?? throw new MissingFieldException("Next");

    object head = null;
    foreach (var (type, value) in nodes.Reverse())
    {
        var node = Activator.CreateInstance(nodeType, nonPublic: true)
            ?? throw new InvalidOperationException("timed node allocation failed");
        internalTypeField.SetValue(node, (byte)(type + 32));
        valueField.SetValue(node, value);
        remainingField.SetValue(node, -1);
        nextField.SetValue(node, head);
        head = node;
    }
    headField.SetValue(actor, head);
}

static TPlayObject NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static TUserItem Item(ushort index, byte[] record,
    byte nativeEffectSelector = 0)
{
    var item = new TUserItem
    {
        wIndex = index,
        Dura = 100,
        DuraMax = 100,
        NativeRecord = record
    };
    item.btValue[7] = nativeEffectSelector;
    return item;
}

static byte[] Record(params (int Offset, ushort Value)[] writes)
{
    var record = new byte[208];
    foreach (var (offset, value) in writes)
    {
        if (offset == 0x15)
            record[offset] = (byte)value;
        else
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset, 2), value);
    }
    return record;
}

static void Pair(byte[] record, int pair, ushort id, ushort value)
{
    var offset = 0x60 + pair * 4;
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset, 2), id);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset + 2, 2), value);
}

static void ResetItems()
{
    M2Share.UserEngine.StdItemList.Clear();
}

static void AddStdItem(byte stdMode, byte aniCount,
    params (ushort Id, ushort Value)[] properties)
{
    var item = new GoodItem
    {
        Name = $"effect-{stdMode}-{aniCount}",
        StdMode = stdMode,
        AniCount = aniCount,
        ItemType = GoodType.ITEM_ETC
    };
    for (var index = 0; index < properties.Length; index++)
    {
        item.NativeItemExtAbilIdents[index] = properties[index].Id;
        item.NativeItemExtAbilValues[index] = properties[index].Value;
    }
    M2Share.UserEngine.StdItemList.Add(item);
}

static void AddSpecialStdItem(byte stdMode, byte shape, ushort wordParam1,
    int intParam1, params (ushort Id, ushort Value)[] properties)
{
    AddStdItem(stdMode, 0, properties);
    var item = M2Share.UserEngine.StdItemList[^1];
    item.Shape = shape;
    item.WordParam1 = wordParam1;
    item.IntParam1 = intParam1;
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
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
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
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
