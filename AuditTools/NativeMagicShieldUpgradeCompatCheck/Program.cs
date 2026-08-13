using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();

try
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();

    VerifyEffectiveLevelAndLevelMutation();
    VerifyMaterialSelection();
    VerifyHeroProgressQueueAndPacket();
    VerifyProductionTransactions();
    VerifyLabelMatrix();
    VerifySourceContracts();

    Console.WriteLine(
        "PASS NativeMagicShieldUpgradeCompatCheck ABI=explicit-player " +
        "self+hero gates=job/40/level3 materials=last-wine-btValue1+first3-books " +
        "transaction=4-delete+weight level=clamp4+preserve-points " +
        "progress=3s+switch-flush+required-minus1 " +
        "runtime=self-success+missing-atomic+hero-success labels=8");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeMagicShieldUpgradeCompatCheck FAIL: {exception}");
    return 1;
}

static void VerifyEffectiveLevelAndLevelMutation()
{
    var wrapped = Magic(31, 250, 20, 12345);
    SetField(wrapped, "NativeLevelBonus", (byte)10);
    Equal(4, InvokeStatic<int>("GetNativeMagicShieldEffectiveLevel",
        wrapped), "effective level wraps byte before train cap");

    var capped = Magic(31, 3, 3, 0x12345678);
    SetField(capped, "NativeLevelBonus", (byte)5);
    Equal(3, InvokeStatic<int>("GetNativeMagicShieldEffectiveLevel",
        capped), "effective level train cap");
    InvokeStatic<object>("SetNativeMagicShieldLevel", capped);
    Equal((byte)3, capped.btLevel, "target level clamps to train cap");
    Equal(0x12345678, capped.nTranPoint,
        "current training points must be preserved");

    var levelFour = Magic(31, 3, 4, 7654321);
    InvokeStatic<object>("SetNativeMagicShieldLevel", levelFour);
    Equal((byte)4, levelFour.btLevel, "normal target level four");
    Equal(7654321, levelFour.nTranPoint,
        "level four does not overwrite current training points");
}

static void VerifyMaterialSelection()
{
    var lowQuality = Item(1, quality: 6, otherValue: 9);
    var book1 = Item(2);
    var firstWine = Item(3, quality: 7);
    var book2 = Item(4);
    var book3 = Item(5);
    var lastWine = Item(6, quality: 8);
    var book4 = Item(7);
    var items = new List<TUserItem>
    {
        lowQuality, book1, firstWine, book2, book3, lastWine, book4
    };
    var names = new Dictionary<TUserItem, string>
    {
        [lowQuality] = "高粱酒",
        [firstWine] = "高粱酒",
        [lastWine] = "高粱酒",
        [book1] = "白日门魔法盾",
        [book2] = "白日门魔法盾",
        [book3] = "白日门魔法盾",
        [book4] = "白日门魔法盾"
    };
    Func<TUserItem, string> resolver = item => names[item];

    object[] arguments = { items, resolver, null };
    Assert((bool)Method("TrySelectNativeMagicShieldMaterials",
            BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, arguments), "complete materials accepted");
    var selected = (TUserItem[])arguments[2];
    Equal(4, selected.Length, "four materials selected");
    Same(lastWine, selected[0], "last qualifying wine wins");
    Same(book1, selected[1], "first book selected");
    Same(book2, selected[2], "second book selected");
    Same(book3, selected[3], "third book selected");

    arguments = new object[]
    {
        new List<TUserItem> { lowQuality, book1, book2, book3 },
        resolver,
        null
    };
    Assert(!(bool)Method("TrySelectNativeMagicShieldMaterials",
            BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, arguments),
        "btValue[3] must not substitute for wine btValue[1]");
}

static void VerifyHeroProgressQueueAndPacket()
{
    var hero = new HeroObject { m_boGhost = false };
    var shield = Magic(31, 4, 4, unchecked((int)0x89ABCDEF));
    InvokeStatic<object>("QueueNativeHeroMagicShieldSnapshot", hero, shield);

    Equal(1, hero.m_MsgList.Count, "first hero snapshot queued");
    var first = hero.m_MsgList[0];
    Assert(first.boLateDelivery, "first hero snapshot delayed");
    Equal(Grobal2.RM_MAGIC_LVEXP, first.wIdent,
        "hero snapshot ident");
    Equal(31, first.nParam1, "hero queued magic ID");
    Equal(4, first.nParam2, "hero queued effective level");
    Equal(unchecked((int)0x89ABCDEF), first.nParam3,
        "hero queued current training points");
    Equal(-1, BitConverter.ToInt32((byte[])first.Payload),
        "required training is -1 body, not current points");

    InvokeStatic<object>("QueueNativeHeroMagicShieldSnapshot", hero, shield);
    Equal(1, hero.m_MsgList.Count,
        "same magic replaces and resets pending snapshot");
    Assert(hero.m_MsgList[0].boLateDelivery,
        "same magic remains delayed");

    var other = Magic(32, 4, 4, 77);
    InvokeStatic<object>("QueueNativeHeroMagicShieldSnapshot", hero, other);
    Equal(2, hero.m_MsgList.Count,
        "different magic flushes old and queues new");
    var flushed = hero.m_MsgList.Single(message =>
        message.nParam1 == 31);
    var pending = hero.m_MsgList.Single(message =>
        message.nParam1 == 32);
    Assert(!flushed.boLateDelivery && flushed.dwDeliveryTime == 0,
        "different old magic becomes immediate");
    Assert(pending.boLateDelivery && pending.dwDeliveryTime != 0,
        "different new magic remains delayed");

    var packet = (ClientPacket)(typeof(HeroObject).GetMethod(
        "BuildHeroRuntimePacket",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "HeroObject.BuildHeroRuntimePacket"))
        .Invoke(null, new object[]
        {
            ToProcess(pending), 0, 0
        });
    Equal((ushort)Grobal2.SM_HERO_MAGIC_LVEXP, packet.Ident,
        "hero progress client ident");
    Equal(32, packet.Recog, "hero progress client magic ID");
    Equal((ushort)4, packet.Param,
        "hero progress client effective level");
    Equal((ushort)77, packet.Tag,
        "hero progress client current points low word");
    Equal((ushort)0, packet.Series,
        "hero progress client current points high word");
}

static void VerifyProductionTransactions()
{
    SetMagicShieldDefinitions();
    VerifySelfProductionSuccess();
    VerifyMissingMaterialProductionRejection();
    VerifyHeroProductionSuccess();
}

static void VerifySelfProductionSuccess()
{
    ResetRuntimeObservations();
    var player = NewMagicShieldPlayer("self-success");
    var magic = Magic(31, 3, 4, 0x12345678);
    player.m_MagicList.Add(magic);
    AddMagicShieldMaterials(player, 100);

    Equal("Success", InvokeUpgrade(player, false),
        "self production outcome");
    Equal(0, player.m_ItemList.Count,
        "self production consumed four materials");
    AssertDeleteAndWeightMessages(player, "self production");
    Equal((byte)4, magic.btLevel, "self production skill level");
    Equal(0x12345678, magic.nTranPoint,
        "self production current training points");
    Same(magic, ReadField<TUserMagic>(player,
        "m_NativeMagicTrainingPending"),
        "self production pending magic pointer");
    Equal(4, M2Share.LogStringList.Count,
        "self production item log count");
    Assert(M2Share.LogStringList.Cast<string>().All(log =>
            log.EndsWith("\t学习四级盾", StringComparison.Ordinal)),
        "self production log label");
}

static void VerifyMissingMaterialProductionRejection()
{
    ResetRuntimeObservations();
    var player = NewMagicShieldPlayer("missing-material");
    var magic = Magic(31, 3, 4, 87654321);
    player.m_MagicList.Add(magic);
    var wine = RuntimeItem(201, 1, quality: 7);
    var book1 = RuntimeItem(202, 2);
    var book2 = RuntimeItem(203, 2);
    player.m_ItemList.Add(wine);
    player.m_ItemList.Add(book1);
    player.m_ItemList.Add(book2);
    var before = player.m_ItemList.ToArray();
    player.m_WAbil.Weight = 321;

    Equal("Item", InvokeUpgrade(player, false),
        "missing material outcome");
    Assert(player.m_ItemList.SequenceEqual(before),
        "missing material changed bag identity/order");
    Equal((byte)3, magic.btLevel,
        "missing material changed skill level");
    Equal(87654321, magic.nTranPoint,
        "missing material changed current training points");
    Equal(321, player.m_WAbil.Weight,
        "missing material refreshed weight");
    Equal(0, player.m_MsgList.Count,
        "missing material emitted internal messages");
    Equal(0, M2Share.LogStringList.Count,
        "missing material emitted logs");
    Assert(ReadField<TUserMagic>(player,
               "m_NativeMagicTrainingPending") == null,
        "missing material changed pending magic pointer");
}

static void VerifyHeroProductionSuccess()
{
    ResetRuntimeObservations();
    var master = NewMagicShieldPlayer("hero-success");
    master.m_btJob = 0;
    var hero = new HeroObject
    {
        m_boGhost = false,
        m_btJob = 1,
        m_Master = master
    };
    hero.m_Abil.Level = 40;
    master.m_HeroObject = hero;
    var magic = Magic(31, 3, 4, unchecked((int)0xCAFEBABE));
    hero.m_MagicList.Add(magic);
    AddMagicShieldMaterials(master, 300);

    Equal("Success", InvokeUpgrade(master, true),
        "hero production outcome");
    Equal(0, master.m_ItemList.Count,
        "hero production consumed master materials");
    AssertDeleteAndWeightMessages(master, "hero production");
    Equal((byte)4, magic.btLevel, "hero production skill level");
    Equal(unchecked((int)0xCAFEBABE), magic.nTranPoint,
        "hero production current training points");
    Assert(ReadField<TUserMagic>(master,
               "m_NativeMagicTrainingPending") == null,
        "hero production changed master pending magic pointer");

    var progress = hero.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_MAGIC_LVEXP);
    Assert(progress.boLateDelivery && progress.dwDeliveryTime != 0,
        "hero production progress is delayed");
    Equal(31, progress.nParam1,
        "hero production queued magic ID");
    Equal(4, progress.nParam2,
        "hero production queued effective level");
    Equal(unchecked((int)0xCAFEBABE), progress.nParam3,
        "hero production queued current training points");
    Equal(-1, BitConverter.ToInt32((byte[])progress.Payload),
        "hero production required training body");
    Equal(4, M2Share.LogStringList.Count,
        "hero production item log count");
    Assert(M2Share.LogStringList.Cast<string>().All(log =>
            log.EndsWith("\t学习白日门四级盾", StringComparison.Ordinal)),
        "hero production log label");
}

static void AssertDeleteAndWeightMessages(TPlayObject player,
    string scenario)
{
    var deletion = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SENDDELITEMLIST);
    Equal(4, deletion.nParam1, scenario + " delete count");
    Equal(0, deletion.wParam, scenario + " delete wParam");
    Equal(0, deletion.nParam2, scenario + " delete nParam2");
    Equal(0, deletion.nParam3, scenario + " delete nParam3");
    Assert(ReferenceEquals(player, deletion.BaseObject),
        scenario + " delete BaseObject");
    var deletedItems = deletion.Payload as IList<TDeleteItem>;
    Assert(deletedItems != null, scenario + " delete payload type");
    Equal(4, deletedItems.Count, scenario + " delete payload count");
    Equal("高粱酒", deletedItems[0].sItemName,
        scenario + " wine deletion order");
    Assert(deletedItems.Skip(1).All(item =>
            item.sItemName == "白日门魔法盾"),
        scenario + " book deletion order");
    Assert(deletedItems.All(item => item.ClientItemID != 0),
        scenario + " client item IDs");
    Equal(1, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_WEIGHTCHANGED),
        scenario + " weight message count");
    Equal(0, player.m_WAbil.Weight,
        scenario + " recomputed bag weight");
}

static TPlayObject NewMagicShieldPlayer(string name)
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_boGhost = false,
        m_btJob = 1,
        m_sMapName = "audit-map",
        m_nCurrX = 12,
        m_nCurrY = 34,
        m_sCharName = name
    };
    player.m_Abil.Level = 40;
    return player;
}

static void AddMagicShieldMaterials(TPlayObject player, int makeIndexBase)
{
    player.m_ItemList.Add(RuntimeItem(makeIndexBase + 1, 2));
    player.m_ItemList.Add(RuntimeItem(makeIndexBase + 2, 1, quality: 7));
    player.m_ItemList.Add(RuntimeItem(makeIndexBase + 3, 2));
    player.m_ItemList.Add(RuntimeItem(makeIndexBase + 4, 2));
}

static TUserItem RuntimeItem(int makeIndex, ushort itemIndex,
    byte quality = 0)
{
    var item = new TUserItem
    {
        MakeIndex = makeIndex,
        wIndex = itemIndex,
        Dura = 1,
        DuraMax = 1,
        btValue = new byte[14]
    };
    item.btValue[1] = quality;
    return item;
}

static void SetMagicShieldDefinitions()
{
    M2Share.UserEngine.StdItemList.Clear();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "高粱酒",
        Weight = 2
    });
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "白日门魔法盾",
        Weight = 3
    });
}

static void ResetRuntimeObservations()
{
    M2Share.LogStringList.Clear();
}

static string InvokeUpgrade(TPlayObject player, bool heroUpgrade)
{
    object outcome = Method("UpgradeNativeMagicShield",
            BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(player, new object[] { heroUpgrade });
    return outcome?.ToString() ?? string.Empty;
}

static void VerifyLabelMatrix()
{
    Type outcomeType = typeof(TPlayObject).Assembly.GetType(
        "GameSvr.NativeMagicShieldUpgradeOutcome", true);
    MethodInfo labels = typeof(PasApiBridge).GetMethod(
        "GetNativeMagicShieldLabel",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "PasApiBridge.GetNativeMagicShieldLabel");

    Equal("@MagicShield_job", Label("Job", false), "self job label");
    Equal("@MagicShield_Level", Label("Level", true), "hero level label");
    Equal("@MagicShield_mglevel", Label("MagicLevel", false),
        "magic level label");
    Equal("@MagicShield_finish1", Label("Finished", false),
        "self finished label");
    Equal("@MagicShield_finish2", Label("Finished", true),
        "hero finished label");
    Equal("@MagicShield_Item", Label("Item", true), "item label");
    Equal("@MagicShield_OK1", Label("Success", false),
        "self success label");
    Equal("@MagicShield_OK2", Label("Success", true),
        "hero success label");
    return;

    string Label(string name, bool hero)
    {
        object outcome = Enum.Parse(outcomeType, name);
        return (string)labels.Invoke(null, new[] { outcome, (object)hero });
    }
}

static void VerifySourceContracts()
{
    string root = FindRepositoryRoot();
    string playerSource = Compact(File.ReadAllText(Path.Combine(root,
        "GameSvr", "Players", "TPlayObject.NativeMagicShieldUpgrade.cs")));
    string bridgeSource = Compact(File.ReadAllText(Path.Combine(root,
        "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.NativeMagicShieldUpgrade.cs")));

    Require(bridgeSource, "args?.Count!=1", "strict one-argument ABI");
    Require(bridgeSource, "args[0].ObjValisnotTPlayObjectplayer",
        "explicit player object ABI");
    Reject(bridgeSource, "CurrentPlayer.UpgradeNativeMagicShield",
        "ambient player dispatch");

    Require(playerSource, "item.btValue[1]>=7",
        "wine quality runtime +0x2B mapping");
    Require(playerSource,
        "materials=new[]{wine,books[0],books[1],books[2]}",
        "wine then three books deletion order");
    Require(playerSource, "deletedItems.Count,0,0,string.Empty,deletedItems",
        "grouped four-item deletion count");
    Require(playerSource, "WeightChanged();SetNativeMagicShieldLevel(magic);",
        "weight before skill mutation");
    Require(playerSource, "QueueNativeMagicTrainingSnapshot(magic",
        "self native pending magic pointer");
    Require(playerSource, "QueueNativeHeroMagicShieldSnapshot(heroOwner,magic)",
        "hero native pending magic adapter");
    Reject(playerSource, "magic.nTranPoint=-1",
        "required cache must not overwrite current training points");
    Reject(playerSource, "RecalcAbilitys()",
        "original upgrade does not recalculate full ability");
    Reject(playerSource, "SendAddMagic(",
        "original upgrade uses progress snapshot, not add-magic");

    int remove = playerSource.IndexOf("m_ItemList.Remove(item)",
        StringComparison.Ordinal);
    int log = playerSource.IndexOf("M2Share.AddGameDataLog",
        StringComparison.Ordinal);
    int dispose = playerSource.IndexOf("Dispose(item)",
        StringComparison.Ordinal);
    int grouped = playerSource.IndexOf(
        "SendMsg(this,Grobal2.RM_SENDDELITEMLIST",
        StringComparison.Ordinal);
    Assert(remove >= 0 && remove < log && log < dispose &&
           dispose < grouped,
        "native transaction order remove/log/dispose/grouped-message");
}

static TProcessMessage ToProcess(SendMessage message) => new()
{
    wIdent = message.wIdent,
    wParam = message.wParam,
    nParam1 = message.nParam1,
    nParam2 = message.nParam2,
    nParam3 = message.nParam3,
    BaseObject = message.BaseObject?.ObjectId ?? 0,
    boLateDelivery = message.boLateDelivery,
    Payload = message.Payload
};

static TUserItem Item(ushort index, byte quality = 0,
    byte otherValue = 0)
{
    var item = new TUserItem
    {
        wIndex = index,
        MakeIndex = index,
        btValue = new byte[14]
    };
    item.btValue[1] = quality;
    item.btValue[3] = otherValue;
    return item;
}

static TUserMagic Magic(ushort magicId, int level, byte trainLevel,
    int currentPoints)
{
    return new TUserMagic
    {
        btLevel = unchecked((byte)level),
        wMagIdx = magicId,
        nTranPoint = currentPoints,
        MagicInfo = new TMagic
        {
            wMagicID = magicId,
            btTrainLv = trainLevel,
            MaxTrain = new[] { 100, 200, 300, 400 }
        }
    };
}

static T InvokeStatic<T>(string name, params object[] arguments)
{
    object value = Method(name,
        BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, arguments);
    return value == null ? default : (T)value;
}

static MethodInfo Method(string name, BindingFlags flags)
{
    return typeof(TPlayObject).GetMethod(name, flags)
           ?? throw new MissingMethodException(
               $"TPlayObject.{name}");
}

static void SetField<T>(object target, string name, T value)
{
    var field = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    field.SetValue(target, value);
}

static T ReadField<T>(object target, string name)
{
    var field = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    return (T)field.GetValue(target);
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static string Compact(string value) =>
    string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

static void Require(string source, string token, string label)
{
    Assert(source.Contains(token, StringComparison.Ordinal), label);
}

static void Reject(string source, string token, string label)
{
    Assert(!source.Contains(token, StringComparison.Ordinal), label);
}

static void Same(object expected, object actual, string label)
{
    if (!ReferenceEquals(expected, actual))
        throw new InvalidOperationException(label);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static void PrepareRuntimeConfig()
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "!Setup.txt"),
        "[Server]\r\n");
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "String.ini"),
        "[String]\r\n");
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Command.conf"),
        "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
}
