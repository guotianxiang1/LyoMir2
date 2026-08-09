using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using DBSvr.Core;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
SetDefinitions();

var bridge = new PasApiBridge
{
    CurrentPlayer = NewPlayer(),
    CurrentNpc = new NormNpc()
};
// ---------------------------------------------------------------------------
// Section 0: the record offsets themselves, asserted against the 战神 SAVE/LOAD
// disassembly rather than against whatever the C# constants happen to say.
//
// This section exists because the previous revision of this audit hardcoded
// 0x238/0x240/0x248 -- the SAME wrong values the production code had -- so it
// compared C# against itself and passed while every slot was shifted +8 onto
// the next field. Offsets are now derived from one anchor (the 20-byte block
// base) plus the object-side displacements, so a future drift in either the
// code or this file cannot agree its way to green.
//
//   0x6B149B  mov [esi+0x230], eax   <- obj+0x1844 (flags)      => rec 0x230
//   0x6B14A2  lea edi,[esi+0x234]    <- block dest              => rec 0x234
//   0x6B14A8  lea esi,[ebx+0x1848]   <- block src  (obj)
//   0x6B14AE  movsd x5               <- 20 bytes: rec 0x234..0x247
// esi inside sub_6B0FF0 is the pre-biased base (0x6B100C lea esi,[eax+8]), so
// [esi+N] is record offset N with no further adjustment.
const int nativeBlockRecordBase = 0x234;   // 0x6B14A2 / LOAD 0x6B07B3
const int nativeBlockObjectBase = 0x1848;  // 0x6B14A8 / LOAD 0x6B07B9
const int nativeBlockLength = 20;          // 5x movsd @0x6B14AE / 0x6B07BF
const int nativeFlagsRecordOffset = 0x230; // 0x6B149B / LOAD 0x6B079E
const int nativeFlagsObjectOffset = 0x1844;// 0x6B1495 / LOAD 0x6B07A7
const int nativeJob0ObjectOffset = 0x184C; // 0x6466D7 inc byte
const int nativeJob1ObjectOffset = 0x184D; // 0x6466E2 inc byte
const int nativeJob2ObjectOffset = 0x184E; // 0x6466ED inc byte
const int nativeJob3ObjectOffset = 0x1854; // 0x6466F8 inc dword

int RecFromObj(int objOffset) =>
    nativeBlockRecordBase + (objOffset - nativeBlockObjectBase);

Equal(nativeFlagsRecordOffset, FieldOffset("NativeSubmitBallQuestFlagsOffset"),
    "flags slot == rec 0x230 (0x6B149B), NOT the 0x238 job-0 byte");
Equal(RecFromObj(nativeJob0ObjectOffset),
    FieldOffset("NativeSubmitBallQuestJob012Offset"),
    "job0 slot == rec 0x234+(0x184C-0x1848) == 0x238 (0x6466D7)");
Equal(RecFromObj(nativeJob3ObjectOffset),
    FieldOffset("NativeSubmitBallQuestJob3Offset"),
    "job3 slot == rec 0x234+(0x1854-0x1848) == 0x240 (0x6466F8)");

// Job 1/2 must be the job-0 byte plus the object-side stride, i.e. contiguous
// bytes -- this is what IncrementNativeBallQuestJobReward's `+ m_btJob` assumes.
Equal(RecFromObj(nativeJob1ObjectOffset),
    FieldOffset("NativeSubmitBallQuestJob012Offset") + 1,
    "job1 byte is contiguous with job0 (0x184D == 0x184C+1)");
Equal(RecFromObj(nativeJob2ObjectOffset),
    FieldOffset("NativeSubmitBallQuestJob012Offset") + 2,
    "job2 byte is contiguous with job0 (0x184E == 0x184C+2)");

// The flags word is OUTSIDE the copied block: native writes it with its own
// dword store at 0x6B149B, before the movsd run. If the flags offset ever lands
// inside 0x234..0x247 it is aliasing a job counter.
True(nativeFlagsRecordOffset + sizeof(uint) <= nativeBlockRecordBase,
    "flags dword (rec 0x230) sits below the block base (rec 0x234), no overlap");
Equal(nativeBlockObjectBase - nativeFlagsObjectOffset, 4,
    "obj+0x1844 flags dword abuts obj+0x1848 block base");

// Every modelled slot must fit inside the 20 bytes the movsd run actually copies.
var nativeBlockEndExclusive = nativeBlockRecordBase + nativeBlockLength; // 0x248
True(FieldOffset("NativeSubmitBallQuestJob3Offset") + sizeof(int)
        <= nativeBlockEndExclusive,
    "job3 dword ends at or before rec 0x248, i.e. inside the copied block");
True(FieldOffset("NativeSubmitBallQuestJob012Offset") + 3
        <= nativeBlockEndExclusive,
    "job0/1/2 bytes lie inside the copied block");
Equal(nativeBlockEndExclusive,
    FieldOffset("NativeSubmitBallQuestBlockEndExclusive"),
    "block end constant == rec 0x248 (0x234 + 20)");

// Guard the specific regression: the old values are the NEXT field's offset.
NotEqual(0x238, FieldOffset("NativeSubmitBallQuestFlagsOffset"),
    "flags must not be 0x238 -- that is the job-0 counter byte");
NotEqual(0x240, FieldOffset("NativeSubmitBallQuestJob012Offset"),
    "job0 must not be 0x240 -- that is the job-3 dword");
NotEqual(0x248, FieldOffset("NativeSubmitBallQuestJob3Offset"),
    "job3 must not be 0x248 -- that is past the end of the block");

var flagsOffset = FieldOffset("NativeSubmitBallQuestFlagsOffset");
var job012Offset = FieldOffset("NativeSubmitBallQuestJob012Offset");
var job3Offset = FieldOffset("NativeSubmitBallQuestJob3Offset");

var player = NewPlayer();
BinaryPrimitives.WriteInt32LittleEndian(
    player.m_NativeHumanData.AsSpan(flagsOffset, 4), 0xA0);
AddRequiredItems(player, missingIndex: 0, addDuplicate: true);
var result = Call(bridge, player);
Equal(1, result, "success result");
Equal(1, player.m_ItemList.Count, "unique six-item consumption");
Equal(107, player.m_ItemList[0].MakeIndex, "duplicate survivor");
Equal(0xA1, ReadInt32(player, flagsOffset), "completion flag preserves other bits");
Equal(1, player.m_NativeHumanData[job012Offset], "warrior reward byte");
Equal(0, player.m_NativeHumanData[job012Offset + 1], "wizard reward byte");
Equal(0, player.m_NativeHumanData[job012Offset + 2], "taoist reward byte");
Equal(0, ReadInt32(player, job3Offset), "job3 reward dword");

// Cross-slot isolation: setting the flag must not disturb the job counters, and
// the job-3 write must not land on any byte the block copy does not own. Both
// were violated by the +8 shift.
Equal(0, player.m_NativeHumanData[nativeBlockEndExclusive],
    "the byte just past the block (rec 0x248) is untouched by the flag OR");
var deleteMessages = player.m_MsgList
    .Where(message => message.wIdent == Grobal2.RM_SENDDELITEMLIST).ToArray();
Equal(1, deleteMessages.Length, "batched delete message count");
var deleteMessage = deleteMessages[0];
Equal(Grobal2.RM_SENDDELITEMLIST, deleteMessage.wIdent,
    "delete message ident");
Equal(0, deleteMessage.wParam, "delete message wParam");
Equal(6, deleteMessage.nParam1, "delete message item count");
Equal(0, deleteMessage.nParam2, "delete message nParam2");
Equal(0, deleteMessage.nParam3, "delete message nParam3");
var deleteItems = deleteMessage.Payload as IList<TDeleteItem>;
Assert(deleteItems != null, "delete message payload type");
Equal(24, deleteItems.Count * sizeof(int), "delete message payload bytes");
SequenceEqual(new[] { 101, 102, 103, 104, 105, 106 },
    deleteItems.Select(item => item.MakeIndex).ToArray(),
    "native fixed delete order");
Assert(ReferenceEquals(player, deleteMessage.BaseObject),
    "delete message BaseObject");

var buildDeleteBody = typeof(TPlayObject).GetMethod(
    "BuildDelItemListBody", BindingFlags.Static | BindingFlags.NonPublic)!;
var deleteBody = (byte[])buildDeleteBody.Invoke(null,
    new object[] { deleteItems })!;
Equal(25, deleteBody.Length, "delete message BufferLen");
SequenceEqual(new[] { 101, 102, 103, 104, 105, 106 },
    Enumerable.Range(0, 6).Select(index =>
        BinaryPrimitives.ReadInt32LittleEndian(
            deleteBody.AsSpan(index * sizeof(int), sizeof(int)))).ToArray(),
    "delete message wire ids");
Equal(0, deleteBody[24], "delete message trailing NUL");

typeof(TPlayObject).GetMethod(
    "SendDelItemList", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(player, new object[] { deleteItems, 6 });
Equal(Grobal2.SM_DELITEMS, player.m_DefMsg.Ident,
    "delete wire ident");
Equal(6, player.m_DefMsg.Recog, "delete wire Recog count");

var levelMessages = player.m_MsgList
    .Where(message => message.wIdent == Grobal2.RM_LEVELUP).ToArray();
Equal(1, levelMessages.Length, "HasLevelUp refresh message count");
Assert(player.m_MsgList.IndexOf(deleteMessage)
       < player.m_MsgList.IndexOf(levelMessages[0]),
    "delete message precedes HasLevelUp refresh");

var repeatCount = player.m_ItemList.Count;
Equal(-1, Call(bridge, player), "repeat result");
Equal(repeatCount, player.m_ItemList.Count, "repeat side effects");
Equal(1, player.m_MsgList.Count(message =>
    message.wIdent == Grobal2.RM_SENDDELITEMLIST), "repeat delete message");

for (ushort missingIndex = 1; missingIndex <= 6; missingIndex++)
{
    var missing = NewPlayer();
    AddRequiredItems(missing, missingIndex, addDuplicate: true);
    var before = missing.m_ItemList.Select(item => item.MakeIndex).ToArray();
    Equal(-2, Call(bridge, missing), $"missing slot {missingIndex} result");
    SequenceEqual(before,
        missing.m_ItemList.Select(item => item.MakeIndex).ToArray(),
        $"missing slot {missingIndex} atomicity");
    Equal(0, missing.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_SENDDELITEMLIST),
        $"missing slot {missingIndex} delete message");
    Equal(0, ReadInt32(missing, flagsOffset),
        $"missing slot {missingIndex} completion flag");
}

for (byte job = 1; job <= 2; job++)
{
    var jobPlayer = NewPlayer();
    jobPlayer.m_btJob = job;
    AddRequiredItems(jobPlayer, missingIndex: 0, addDuplicate: false);
    Equal(1, Call(bridge, jobPlayer), $"job {job} result");
    Equal(1, jobPlayer.m_NativeHumanData[job012Offset + job],
        $"job {job} reward byte");
}

var job3 = NewPlayer();
job3.m_btJob = 3;
job3.m_Abil.Level = 35;
AddRequiredItems(job3, missingIndex: 0, addDuplicate: false);
Equal(1, Call(bridge, job3), "job3 success result");
Equal(0, job3.m_ItemList.Count, "job3 six-item consumption");
Equal(1, ReadInt32(job3, flagsOffset), "job3 completion flag");
Equal(1, ReadInt32(job3, job3Offset), "job3 reward dword");
Equal(1, job3.m_MsgList.Count(message =>
    message.wIdent == Grobal2.RM_SENDDELITEMLIST),
    "job3 delete message");
Equal(1, job3.m_MsgList.Count(message =>
    message.wIdent == Grobal2.RM_LEVELUP),
    "job3 HasLevelUp message");
Equal(390, job3.m_Abil.MaxHP, "job3 level MaxHP");
Equal(390, job3.m_Abil.MaxMP, "job3 level MaxMP");
Equal(458, job3.m_Abil.MaxWeight, "job3 level MaxWeight");
Equal(76, job3.m_Abil.MaxWearWeight, "job3 level MaxWearWeight");
Equal(106, job3.m_Abil.MaxHandWeight, "job3 level MaxHandWeight");
Equal(0, HUtil32.LoWord(job3.m_Abil.AC), "job3 level AC low");
Equal(5, HUtil32.HiWord(job3.m_Abil.AC), "job3 level AC high");
Equal(0, HUtil32.LoWord(job3.m_Abil.MAC), "job3 level MAC low");
Equal(5, HUtil32.HiWord(job3.m_Abil.MAC), "job3 level MAC high");
var job3Working = typeof(TBaseObject).GetField(
    "m_NativeCoreWorkingAbility",
    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(job3)!;
Equal(15, ReadWorking(job3Working, "HitPoint"), "job3 native Hit");
Equal(23, ReadWorking(job3Working, "SpeedPoint"), "job3 native Speed");
Equal(6, ReadWorking(job3Working, "CCLow"), "job3 level CC low");
Equal(8, ReadWorking(job3Working, "CCHigh"),
    "job3 level and quest CC high");
Equal(15, job3.m_btHitPoint, "job3 projected Hit");
Equal(23, job3.m_wSpeedPoint, "job3 projected Speed");

job3.RecalcAbilitys();
job3Working = typeof(TBaseObject).GetField(
    "m_NativeCoreWorkingAbility",
    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(job3)!;
Equal(15, ReadWorking(job3Working, "HitPoint"),
    "job3 repeat native Hit");
Equal(23, ReadWorking(job3Working, "SpeedPoint"),
    "job3 repeat native Speed");
Equal(8, ReadWorking(job3Working, "CCHigh"),
    "job3 repeat CC non-accumulation");
Equal(15, job3.m_btHitPoint, "job3 repeat projected Hit");
Equal(23, job3.m_wSpeedPoint, "job3 repeat projected Speed");

var cappedJob3 = NewPlayer();
cappedJob3.m_btJob = 3;
cappedJob3.m_Abil.Level = ushort.MaxValue;
cappedJob3.RecalcLevelAbilitys();
Equal(0xFFDC, cappedJob3.m_Abil.MaxWeight, "job3 MaxWeight cap");
Equal(0xFFDC, cappedJob3.m_Abil.MaxWearWeight,
    "job3 MaxWearWeight cap");
Equal(0xFFDC, cappedJob3.m_Abil.MaxHandWeight,
    "job3 MaxHandWeight cap");

var signed = NewPlayer();
signed.m_NativeHumanData[job012Offset] = 0xFF;
signed.m_NativeHumanData[job012Offset + 1] = 0x80;
signed.m_NativeHumanData[job012Offset + 2] = 0x7F;
BinaryPrimitives.WriteInt32LittleEndian(
    signed.m_NativeHumanData.AsSpan(job3Offset, 4), 123456);
InvokeRecalc(signed);
var working = typeof(TBaseObject).GetField(
    "m_NativeCoreWorkingAbility",
    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(signed)!;
Equal(-1, ReadWorking(working, "DCHigh"), "signed warrior bonus");
Equal(-128, ReadWorking(working, "MCHigh"), "signed wizard bonus");
Equal(127, ReadWorking(working, "SCHigh"), "signed taoist bonus");
Equal(123456, ReadWorking(working, "CCHigh"), "job3 dword bonus");

Assert(!bridge.CallNpcMethod("SubmitBallQuest",
        new List<PasValue> { PasValue.FromObject(NewPlayer()) }, out _),
    "SubmitBallQuest retained a procedure shadow");
Assert(!bridge.CallNpcFunc("SubmitBallQuest", new List<PasValue>(), out _),
    "SubmitBallQuest accepted a missing player argument");

Console.WriteLine(
    "PASS NativeSubmitBallQuest six/name/order atomic=-2 repeat=-1 success=1 " +
    "delete=10148->709/recog6/body25 " +
    $"offsets=flags0x{flagsOffset:X3}/job012_0x{job012Offset:X3}/job3_0x{job3Offset:X3} " +
    "(derived from 0x6B149B/0x6B14A2/0x6466D7/0x6466F8, block 0x234+20) " +
    "refresh=HasLevelUp recalc=sbyte job3=level/native/cc/cap");
return;

static int Call(PasApiBridge bridge, TPlayObject player)
{
    Assert(bridge.CallNpcFunc("SubmitBallQuest",
        new List<PasValue> { PasValue.FromObject(player) }, out var result),
        "SubmitBallQuest function dispatch");
    return result.AsInt();
}

static TPlayObject NewPlayer() => new()
{
    m_boOffLineFlag = true,
    m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize]
};

static void AddRequiredItems(TPlayObject player, ushort missingIndex,
    bool addDuplicate)
{
    var order = new ushort[] { 5, 2, 6, 1, 4, 3 };
    foreach (var index in order)
    {
        if (index == missingIndex) continue;
        player.m_ItemList.Add(NewItem(100 + index, index));
    }
    if (addDuplicate)
    {
        var duplicateIndex = missingIndex == 1 ? (ushort)2 : (ushort)1;
        player.m_ItemList.Add(NewItem(107, duplicateIndex));
    }
}

static TUserItem NewItem(int makeIndex, ushort index) => new()
{
    MakeIndex = makeIndex,
    wIndex = index,
    Dura = 1,
    DuraMax = 1,
    btValue = new byte[14]
};

static void SetDefinitions()
{
    var names = new[]
    {
        "红色夜明珠", "橙色夜明珠", "黄色夜明珠",
        "绿色夜明珠", "蓝色夜明珠", "庄主令牌"
    };
    M2Share.UserEngine.StdItemList.Clear();
    foreach (var name in names)
        M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = name });
}

static int ReadInt32(TPlayObject player, int offset) =>
    BinaryPrimitives.ReadInt32LittleEndian(
        player.m_NativeHumanData.AsSpan(offset, 4));

static void InvokeRecalc(TPlayObject player)
{
    typeof(TBaseObject).GetMethod("SeedNativeFixedAbility",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(
        player, new object[] { new TAddAbility() });
}

static int ReadWorking(object value, string name) => (int)value.GetType()
    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(value)!;

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

static void SequenceEqual(int[] expected, int[] actual, string message)
{
    Assert(expected.SequenceEqual(actual), message +
        $": expected [{string.Join(',', expected)}], " +
        $"actual [{string.Join(',', actual)}]");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual) throw new InvalidOperationException(
        $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(
        message + ": expected true");
}

static void NotEqual(int notExpected, int actual, string message)
{
    if (notExpected == actual) throw new InvalidOperationException(
        $"{message}: value must not be {notExpected}, but it is");
}

// Reads the production constant by reflection rather than restating its literal,
// so this audit can never drift into agreeing with a wrong value the way the
// previous revision did.
static int FieldOffset(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"TPlayObject.{name} not found -- the constant was renamed or removed");
    var value = field.GetRawConstantValue()
        ?? throw new InvalidOperationException($"{name} has no constant value");
    return value is uint u ? unchecked((int)u) : Convert.ToInt32(value);
}
