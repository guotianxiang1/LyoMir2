using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;

const string NativeName = "ABCDEFGHIJKLMNO";
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var managerContent = Encoding.ASCII.GetBytes(
    NativeName + " 74565\r\n" +
    "NegativeManager -1\r\n");
var butchContent = Encoding.ASCII.GetBytes(
    NativeName + " 8 255\r\n" +
    NativeName + " 7 1\r\n" +
    "InvalidMonster 9 199\r\n");

var catalog = CreateCatalog(NativeName);
var definition = catalog.Definitions.Single();
var tables = NativeType2MonsterManagerTables.FromContents(
    managerContent, butchContent);
var fields = tables.Resolve(definition);

Equal(74565, fields.ManagerLookupValue,
    "MonBasePk keeps the parsed Int32 value");
Equal((ushort)0x2345, fields.ManagerId,
    "definition/actor manager field consumes the low word");
Equal((byte)8, fields.Classification,
    "ButchType first matching classification");
Equal((byte)255, fields.ClassificationValue,
    "ButchType third column");

var missing = tables.Resolve(Encoding.ASCII.GetBytes("MissingMonster"));
Equal(0, missing.ManagerLookupValue, "missing manager defaults to zero");
var negative = tables.Resolve(Encoding.ASCII.GetBytes("NegativeManager"));
Equal(0, negative.ManagerLookupValue, "negative manager defaults to zero");
var invalidButch = tables.Resolve(
    Encoding.ASCII.GetBytes("InvalidMonster"));
Equal((byte)0, invalidButch.Classification,
    "ButchType classifications above eight are cleared");
Equal((byte)0, invalidButch.ClassificationValue,
    "invalid ButchType third column is cleared with classification");

var projection = NativeType2MonsterActorProjection.Create(
    definition, tables);
Equal("ABCDEFGHIJKLMN", projection.ActorName,
    "actor name is capped at fourteen raw bytes");
SequenceEqual(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMN"),
    projection.CopyActorNameBytes(), "actor raw name bytes");
Equal((ushort)0x1234, projection.Speed, "speed remains a word");
Equal((ushort)0xFEDC, projection.Hit, "hit remains a word");
Equal(2, projection.SuperForceMask, "definition +0x5C projection");
Equal(25, projection.SuperForceReductionPercent,
    "definition +0x60 projection");
Equal(0x55667788, projection.JobFastness,
    "definition +0x64 write-only projection");
Equal(0x11223344, definition.ForceValue,
    "source +0x44 remains definition-only +0x50 data");
Check(typeof(NativeType2MonsterActorProjection).GetProperty(
          "ForceValue", BindingFlags.Instance | BindingFlags.Public) == null,
    "definition ForceValue leaked into actor projection");
Check(typeof(TBaseObject).GetField("m_nNativeMonsterForceValue") == null,
    "definition ForceValue leaked into TBaseObject");

PrepareRuntimeConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
var directActor = new Monster();
directActor.ApplyNativeType2MonsterProjection(projection);
Equal((ushort)0x1234, directActor.m_wSpeedPoint,
    "projection writes actual word speed");
Equal((ushort)0xFEDC, directActor.m_btHitPoint,
    "projection writes actual word hit");
Equal(74565, directActor.m_nNativeMonsterManagerLookupValue,
    "actor preserves manager lookup Int32");
Equal((ushort)0x2345, directActor.m_wNativeMonsterManagerId,
    "actor manager field is the low word");
Equal(0x55667788, directActor.m_nNativeMonsterJobFastness,
    "actor stores +0x64 without adding behavior");

Equal(75, NativeType2MonsterActorProjection.ApplySuperForceReduction(
        100, 2, 25, 1),
    "matching attacker job reduction");
Equal(100, NativeType2MonsterActorProjection.ApplySuperForceReduction(
        100, 2, 25, 0),
    "non-matching attacker job");
Equal(100, NativeType2MonsterActorProjection.ApplySuperForceReduction(
        100, 2, 25, 4),
    "attacker jobs above the native range");
Equal(1497949673,
    NativeType2MonsterActorProjection.ApplySuperForceReduction(
        1500000000, 2, 3, 1),
    "native IMUL/CDQ uses the wrapped Int32 product");

VerifyPublicationAndDamage(catalog, managerContent, butchContent);

Console.WriteLine(
    "PASS NativeType2MonsterResidualProjectionCheck " +
    "name=15/14 manager=int-to-word butch=3-column " +
    "speed-hit=word force=definition-only reduction=attacker-job " +
    "job-fastness=write-only");

static void VerifyPublicationAndDamage(
    NativeType2MonsterRuntimeCatalog catalog,
    byte[] managerContent, byte[] butchContent)
{
    var temporaryRoot = Path.Combine(Path.GetTempPath(),
        "loym2-monster-residual-" + Guid.NewGuid().ToString("N"));
    var previousConfigPath = M2Share.sConfigPath;
    var previousRootPath = M2Share.sRootPath;
    var previousBaseDirectory = M2Share.g_Config.sBaseDir;
    var previousEnvirDirectory = M2Share.g_Config.sEnvirDir;
    var previousLocalDb = M2Share.LocalDB;
    var previousUserEngine = M2Share.UserEngine;

    try
    {
        var configDirectory = Path.Combine(temporaryRoot,
            "Share", "Config");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(temporaryRoot,
            "Envir", "MonItems"));
        File.WriteAllBytes(Path.Combine(configDirectory,
            "MonBasePk.txt"), managerContent);
        File.WriteAllBytes(Path.Combine(configDirectory,
            "ButchType.txt"), butchContent);

        M2Share.sConfigPath = temporaryRoot;
        M2Share.sRootPath = temporaryRoot;
        M2Share.g_Config.sBaseDir = "Share";
        M2Share.g_Config.sEnvirDir = "Envir";
        M2Share.LocalDB = new LocalDB();
        var engine = new UserEngine();
        M2Share.UserEngine = engine;

        Check(engine.TryPublishNativeMonsterDefinitions(
                catalog, out var error),
            "native publication failed: " + error);

        var initialize = typeof(UserEngine).GetMethod("MonInitialize",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Check(initialize != null, "UserEngine.MonInitialize is missing");
        var actor = new Monster();
        initialize.Invoke(engine, new object[] { actor, NativeName });

        SequenceEqual(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMN"),
            actor.m_NativeMonsterActorNameBytes,
            "published actor raw name truncation");
        Equal("ABCDEFGHIJKLMN", actor.m_sCharName,
            "published actor display name truncation");
        Equal(74565, actor.m_nNativeMonsterManagerLookupValue,
            "published manager Int32 lookup");
        Equal((ushort)0x2345, actor.m_wNativeMonsterManagerId,
            "published manager actor word");
        Equal((byte)8, actor.m_btNativeMonsterClassification,
            "published ButchType classification");
        Equal(255, actor.m_nNativeMonsterClassificationValue,
            "published ButchType third column");
        Equal((ushort)0x1234, actor.m_wSpeedPoint,
            "MonInitialize word speed");
        Equal((ushort)0xFEDC, actor.m_btHitPoint,
            "MonInitialize word hit");

        actor.RecalcAbilitys();
        Equal((ushort)0x1234, actor.m_wSpeedPoint,
            "ability recalculation preserves native word speed");
        Equal((ushort)0xFEDC, actor.m_btHitPoint,
            "ability recalculation preserves native word hit");

        actor.m_WAbil.CopyFrom(actor.m_Abil);
        actor.m_WAbil.HP = 1000;
        actor.m_WAbil.MaxHP = 1000;
        var matchingAttacker = new Monster { m_btJob = 1 };
        var nonMatchingAttacker = new Monster { m_btJob = 0 };
        var unsupportedAttacker = new Monster { m_btJob = 4 };

        // MOVED to the native stage. sub_767CBC is called from
        // TCreature.StruckDamage = sub_767A18 @0x767AE4 (`mov cl,[edi+0x72]`
        // = the ATTACKER job byte; edi = the ecx argument), positioned after
        // the 1.3/1.25/1.2 amplify tier and before the land call @0x767B1D.
        // The armour getters sub_767958 / sub_7679B8 both end at
        // `call sub_76FFE8` + `ret 4` and never call sub_767CBC — so
        // GetHitStruckDamage/GetMagStruckDamage must pass damage through.
        actor.SetLastHiter(nonMatchingAttacker);
        var physicalDamage = actor.GetHitStruckDamage(
            matchingAttacker, 100);
        Equal((ushort)100, physicalDamage,
            "armour getter must not apply the super-force reduction");
        actor.StruckDamage(physicalDamage, matchingAttacker);
        Equal(925, actor.m_WAbil.HP,
            "StruckDamage super-force reduction reaches HP");

        // Native reads the attacker from its ecx ARGUMENT, never from
        // m_LastHiter (+0x354): 19 of the 23 native +0xA8 callers never call
        // SetLastHiter at all, and sub_766A70 calls SetLastHiter @0x766BC1
        // AFTER StruckDamage @0x766BA6. So a stale/mismatched m_LastHiter must
        // have no effect on the reduction.
        actor.m_WAbil.HP = 1000;
        actor.SetLastHiter(matchingAttacker);
        actor.StruckDamage(100, nonMatchingAttacker);
        Equal(900, actor.m_WAbil.HP,
            "stale matching last hitter must not reduce damage");

        actor.m_WAbil.HP = 1000;
        actor.SetLastHiter(nonMatchingAttacker);
        actor.StruckDamage(100, matchingAttacker);
        Equal(925, actor.m_WAbil.HP,
            "damage is reduced exactly once, from the direct attacker");

        actor.m_WAbil.HP = 1000;
        var magicDamage = actor.GetMagStruckDamage(
            matchingAttacker, 100);
        Equal(100, magicDamage,
            "magic armour getter must not apply the super-force reduction");
        actor.StruckDamage(magicDamage, matchingAttacker);
        Equal(925, actor.m_WAbil.HP,
            "magic path super-force reduction reaches HP");

        // @0x767AD9 `test edi,edi; je 0x767B13` — a NIL attacker skips the
        // whole stage. Native passes `xor ecx,ecx` at 0x73F3AD / 0x73F43E,
        // so this is a faithful state, not a fallback.
        actor.m_WAbil.HP = 1000;
        actor.SetLastHiter(matchingAttacker);
        actor.StruckDamage(100, null);
        Equal(900, actor.m_WAbil.HP,
            "nil attacker must skip the reduction (0x767AD9 test edi,edi)");
        actor.m_WAbil.HP = 1000;
        actor.StruckDamage(100);
        Equal(900, actor.m_WAbil.HP,
            "the legacy no-attacker overload is the nil-attacker shape");

        // @0x767CDE `sub dl,4; jae` — job >= 4 passes through.
        actor.m_WAbil.HP = 1000;
        actor.StruckDamage(100, unsupportedAttacker);
        Equal(900, actor.m_WAbil.HP,
            "attacker jobs at or above four are skipped");
    }
    finally
    {
        M2Share.sConfigPath = previousConfigPath;
        M2Share.sRootPath = previousRootPath;
        M2Share.g_Config.sBaseDir = previousBaseDirectory;
        M2Share.g_Config.sEnvirDir = previousEnvirDirectory;
        M2Share.LocalDB = previousLocalDb;
        M2Share.UserEngine = previousUserEngine;

        var fullRoot = Path.GetFullPath(temporaryRoot);
        var fullTemp = Path.GetFullPath(Path.GetTempPath());
        if (fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }
}

static NativeType2MonsterRuntimeCatalog CreateCatalog(string name)
{
    var body = new byte[NativeType2MonsterSnapshotState.NativeRecordSize];
    var nameBytes = Encoding.ASCII.GetBytes(name);
    body[0x04] = (byte)nameBytes.Length;
    nameBytes.CopyTo(body, 0x05);
    body[0x14] = 49;
    WriteInt32(body, 0x20, 1000);
    WriteInt32(body, 0x24, 600);
    WriteUInt16(body, 0x34, 0x1234);
    WriteUInt16(body, 0x36, 0xFEDC);
    WriteInt32(body, 0x44, 0x11223344);
    WriteInt32(body, 0x50, 2);
    WriteInt32(body, 0x54, 25);
    WriteInt32(body, 0x58, 0x55667788);

    var packet = new byte[NativeType2MonsterSnapshotState.HeaderSize
                          + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet,
        NativeType2MonsterSnapshotState.Command);
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), 1);
    body.CopyTo(packet, NativeType2MonsterSnapshotState.HeaderSize);

    var snapshot = new NativeType2MonsterSnapshotState();
    snapshot.Consume(packet);
    var catalog = new NativeType2MonsterRuntimeCatalog();
    catalog.Publish(snapshot);
    return catalog;
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory,
            "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void WriteUInt16(byte[] target, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(
        target.AsSpan(offset, sizeof(ushort)), value);

static void WriteInt32(byte[] target, int offset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(
        target.AsSpan(offset, sizeof(int)), value);

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
    }
}

static void SequenceEqual(byte[] expected, byte[] actual,
    string description)
{
    if (!expected.AsSpan().SequenceEqual(actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {Convert.ToHexString(expected)}, " +
            $"actual {Convert.ToHexString(actual)}");
    }
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}
