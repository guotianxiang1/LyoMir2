using System.Buffers.Binary;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936);

M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "普通药", StdMode = 0 });
M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "叠加药", StdMode = 7 });

var raw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
WriteShortString(raw, NativeHeroDbFrameCodec.MasterNameOffset, 15, "主人甲");
WriteShortString(raw, NativeHeroDbFrameCodec.HeroNameOffset, 15, "英雄乙");
raw[NativeHeroDbFrameCodec.RaceOffset] = Grobal2.RC_HEROOBJECT;
raw[NativeHeroDbFrameCodec.JobOffset] = M2Share.jWarr;
raw[NativeHeroDbFrameCodec.HeroTypeOffset] = 1;
raw[0x150] = 0xA7;
BinaryPrimitives.WriteUInt16LittleEndian(
    raw.AsSpan(NativeHeroDbFrameCodec.LevelOffset, 2), 10);
WriteBagItem(raw, 0, 1001, 1, 10);
WriteBagItem(raw, 1, 1002, 1, 10);
WriteBagItem(raw, 2, 1003, 1, 10);
WriteBagItem(raw, 3, 2001, 2, 5);

Assert(NativeHeroDbFrameCodec.TryCreateRecord(raw, out var record, out var error), error);
var dynamicData = new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>());
var hero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(hero, record, dynamicData, out error), error);
var owner = new TPlayObject
{
    m_sCharName = "主人甲",
    m_HeroObject = hero,
    m_btNativeHeroState = 1
};

var api = new PasApiBridge();
using var context = api.PushContext(owner, null);

owner.m_btNativeHeroState = 0;
Assert(api.CallPlayerFunc("GetHeroBagItemCount",
    new List<PasValue> { PasValue.FromString("普通药") }, out var missingResult),
    "missing-hero GetHeroBagItemCount was not dispatched");
Equal(-1, missingResult.AsInt(), "missing native hero state result");
owner.m_btNativeHeroState = 1;

Assert(api.CallPlayerFunc("GetHeroBagItemCount",
    new List<PasValue> { PasValue.FromString("普通药") }, out var countResult),
    "GetHeroBagItemCount was not dispatched");
Equal(3, countResult.AsInt(), "normal item count");
Assert(api.CallPlayerFunc("GetHeroBagItemCount",
    new List<PasValue> { PasValue.FromString("叠加药") }, out countResult),
    "pile GetHeroBagItemCount was not dispatched");
Equal(5, countResult.AsInt(), "StdMode=7 Dura count");

Assert(api.CallPlayerFunc("TakeFromHeroBag",
    new List<PasValue> { PasValue.FromString("普通药"), PasValue.FromInt(2) },
    out var takeResult), "TakeFromHeroBag was not dispatched");
Assert(takeResult.AsBool(), "native TakeFromHeroBag rejected available items");
Equal(1, hero.GetNativeBagItemCount("普通药"), "bag count after native take");
Assert(!hero.TryTakeNativeBagItems("普通药", 2, out _),
    "insufficient take was accepted");
Equal(1, hero.GetNativeBagItemCount("普通药"), "failed take mutated the bag");

// TakeFromHeroBagEx / TakeHeroBagExItem do not exist in 战神: both names are 0-hit in the
// M2 baseline, while the two real hero-bag APIs are present twice each (declaration +
// runtime name binding) —
//   0x72D439 'function GetHeroBagItemCount(const ...' / 0x732894 'GetHeroBagItemCount'
//   0x72D481 'function TakeFromHeroBag(const ItemName: string; ItemCount: Byte): Boolean'
//   0x7328B0 'TakeFromHeroBag'
// so the Ex form must stay fail-closed and must not touch the bag.
Assert(!api.CallPlayerMethod("TakeFromHeroBagEx",
    new List<PasValue> { PasValue.FromString("普通药"), PasValue.FromInt(1) }),
    "TakeFromHeroBagEx is 0-hit in the native surface and must stay fail-closed");
Assert(!api.CallPlayerMethod("TakeHeroBagExItem",
    new List<PasValue> { PasValue.FromString("普通药"), PasValue.FromInt(1) }),
    "TakeHeroBagExItem is 0-hit in the native surface and must stay fail-closed");
Equal(1, hero.GetNativeBagItemCount("普通药"),
    "rejected Ex take must not mutate the hero bag");

Assert(api.CallPlayerMethod("SetHeroLevel",
    new List<PasValue> { PasValue.FromInt(42) }), "SetHeroLevel failed");
Equal((ushort)42, hero.m_Abil.Level, "runtime level");
Equal((ushort)42, hero.HeroLevel, "hero mirror level");
Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(hero, out var saved,
    out _, out error), error);
Equal((ushort)42, saved.Level, "saved native level");
Equal((byte)0xA7, saved.ToArray()[0x150], "unknown fixed byte after mutations");
// Four records went in (三份 普通药 + 一份 叠加药); the only accepted take was
// TakeFromHeroBag(普通药, 2), so 普通药 ×1 and 叠加药 ×1 survive.
Equal(2, saved.ToArray().AsSpan(NativeHeroDbFrameCodec.BagItemsOffset,
        NativeHeroDbFrameCodec.BagItemCount * NativeHeroDbFrameCodec.ItemRecordSize)
    .ToArray().Chunk(NativeHeroDbFrameCodec.ItemRecordSize)
    .Count(item => BinaryPrimitives.ReadUInt16LittleEndian(item.AsSpan(4, 2)) != 0),
    "saved bag active record count");

hero.m_boDeath = true;
Assert(!api.CallPlayerMethod("SetHeroLevel",
        new List<PasValue> { PasValue.FromInt(43) }),
    "dead hero level change did not fail closed");
Equal((ushort)42, hero.m_Abil.Level, "dead hero level changed");

Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(hero, out var beforeTimedState,
    out var beforeTimedDynamic, out error), error);
Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(beforeTimedDynamic,
    out var beforeTimedDynamicBytes, out error), error);

var timedTick = HUtil32.GetTickCount();
hero.ProcessTimedAbilities(timedTick);
Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(0), PasValue.FromInt(10), PasValue.FromInt(30)
        }),
    "dead hero AddHeroAbil was not dispatched");
Assert(hero.HasTimedAbility(0), "AddHeroAbil did not create hero timed state");
Assert(!owner.HasTimedAbility(0), "AddHeroAbil created state on the owner");
Equal(10, hero.GetTimedAbilityValue(0), "initial hero timed value");
Equal(30000, hero.GetTimedAbilityRemainingMilliseconds(0),
    "initial hero timed duration");

Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(0), PasValue.FromInt(5), PasValue.FromInt(60)
        }), "lower-value AddHeroAbil was not dispatched");
Equal(10, hero.GetTimedAbilityValue(0), "lower hero value replaced active value");
Equal(30000, hero.GetTimedAbilityRemainingMilliseconds(0),
    "lower hero value replaced active duration");

Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(0), PasValue.FromInt(10), PasValue.FromInt(60)
        }), "equal-value AddHeroAbil was not dispatched");
Equal(60000, hero.GetTimedAbilityRemainingMilliseconds(0),
    "equal hero value did not extend duration");
Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(0), PasValue.FromInt(11), PasValue.FromInt(5)
        }), "higher-value AddHeroAbil was not dispatched");
Equal(11, hero.GetTimedAbilityValue(0), "higher hero value did not replace value");
Equal(5000, hero.GetTimedAbilityRemainingMilliseconds(0),
    "higher hero value did not replace duration");

Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(1), PasValue.FromInt(1), PasValue.FromInt(65535)
        }), "Word-max AddHeroAbil was not dispatched");
Equal(65535000, hero.GetTimedAbilityRemainingMilliseconds(1),
    "Word-max hero duration was treated as permanent or overflowed");

Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(43), PasValue.FromInt(1), PasValue.FromInt(30)
        }), "native hero ability type 43 was not dispatched");
Assert(hero.HasTimedAbility(43), "hero type 43 state was not created");
Assert(hero.HasNativeActiveState(75),
    "hero type 43 did not map to internal state 75");
Equal(1, hero.GetTimedAbilityValue(43), "initial hero type 43 value");
Equal(30000, hero.GetTimedAbilityRemainingMilliseconds(43),
    "initial hero type 43 duration");

Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(299), PasValue.FromInt(2), PasValue.FromInt(40)
        }), "low-byte hero ability alias 299 was not dispatched");
Equal(2, hero.GetTimedAbilityValue(43),
    "low-byte hero ability alias 299 did not refresh type 43");
Equal(40000, hero.GetTimedAbilityRemainingMilliseconds(43),
    "low-byte hero ability alias 299 did not replace the type 43 duration");
Assert(hero.RemoveTimedAbility(43), "hero type 43 state was not removable");
Assert(!hero.HasTimedAbility(43) && !hero.HasNativeActiveState(75),
    "hero type 43 removal retained node or state 75");

Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(1), PasValue.FromInt(65535), PasValue.FromInt(0)
        }), "zero-duration AddHeroAbil was not dispatched");
hero.ProcessTimedAbilities(timedTick + 499);
Assert(hero.HasTimedAbility(1), "zero-duration hero state expired before 500ms scan");
hero.ProcessTimedAbilities(timedTick + 500);
Assert(!hero.HasTimedAbility(1), "zero-duration hero state survived the 500ms scan");

Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(hero, out var afterTimedState,
    out var afterTimedDynamic, out error), error);
Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(afterTimedDynamic,
    out var afterTimedDynamicBytes, out error), error);
Assert(beforeTimedState.ToArray().SequenceEqual(afterTimedState.ToArray()),
    "timed hero state leaked into the fixed DB snapshot");
Assert(beforeTimedDynamicBytes.SequenceEqual(afterTimedDynamicBytes),
    "timed hero state leaked into the dynamic DB snapshot");

owner.m_HeroObject = null;
Assert(api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue>
        {
            PasValue.FromInt(2), PasValue.FromInt(7), PasValue.FromInt(10)
        }), "missing-hero AddHeroAbil was not a successful no-op");
Assert(!owner.HasTimedAbility(2), "missing-hero AddHeroAbil changed the owner");
owner.m_HeroObject = hero;
Assert(!api.CallPlayerMethod("AddHeroAbil",
        new List<PasValue> { PasValue.FromInt(0), PasValue.FromInt(1) }),
    "two-argument AddHeroAbil was accepted");

Console.WriteLine(
    "PASS hero-pas-admin count=stdmode7-dura take=atomic+queued ex=fail-closed(0-hit) level=native+queued addabil=timed+transient+runtime-only");

void WriteShortString(byte[] destination, int offset, int maximumLength, string value)
{
    var bytes = gbk.GetBytes(value);
    Assert(bytes.Length <= maximumLength, "test short string is oversized");
    destination.AsSpan(offset, maximumLength + 1).Clear();
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
}

static void WriteBagItem(byte[] destination, int slot, int makeIndex,
    ushort itemIndex, ushort dura)
{
    var item = destination.AsSpan(NativeHeroDbFrameCodec.BagItemsOffset
                                  + slot * NativeHeroDbFrameCodec.ItemRecordSize,
        NativeHeroDbFrameCodec.ItemRecordSize);
    BinaryPrimitives.WriteInt32LittleEndian(item.Slice(0, 4), makeIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(item.Slice(4, 2), itemIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(item.Slice(6, 2), dura);
    BinaryPrimitives.WriteUInt16LittleEndian(item.Slice(8, 2), 100);
}

static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}
