extern alias dbsvr;
// 原生 S / V 变量银行的内部布局 —— 战神等价性审计。
//
// 钉住三件事，任何一件被改动都会让眼神英雄/法术族的 trampoline 静默取到错误的槽：
//
//   ① 键合成       sub_6E42CC  0x6E42CC  69 C2 E8 03 00 00  imul eax, edx, 0x3E8
//                              0x6E42D2  03 C1              add  eax, ecx
//                  Delphi 寄存器约定 eax=Self / edx=group / ecx=index
//                  ⇒ Key = group*1000 + index。
//                  group/index 的角色由眼神 SetS 包装器 0x10065F40 的三个常量现场
//                  钉死：0x100697F8 push 0x6E + 0x100697FA mov edx,1 -> SetS(1,110)；
//                  0x1006983D push 1 + 0x1006983F mov edx,6 -> SetS(6,1)；
//                  0x1009010E push 2 + 0x10090107 mov edx,6 -> SetS(6,2)。
//
//   ② 条目步长 8   TypeInfo [0x78D908] -> 0x78D90C，kind 0x11 = tkDynArray，
//                  名 "TScriptTagArr"，elSize = 8，Key 在 +0、Value 在 +4
//                  (0x6E42A2  3B 3C C6     cmp edi,[esi+eax*8]
//                   0x6E42A7  8B 44 C6 04  mov eax,[esi+eax*8+4])。
//                  长度是 Delphi 动态数组自带的 [ptr-4]（sub_406A88
//                  0x406A8C  8B 40 FC  mov eax,[eax-4]）—— 银行没有自定义头部，
//                  第 0 个条目就在 [ptr+0]。
//
//   ③ 升序约束     upsert sub_6E4140 二分后有序插入
//                  (0x6E41D3  7E 05  jle -> lo=mid+1，0x6E4216/0x6E4247 两个 Move
//                   分别把尾部右移一格)。存档 codec 两侧都不重排：
//                   编码 0x6E4DE7 mov eax,[eax+0x804] / 0x6E4DEF call 0x403260 (Move)
//                   解码 0x6E457C add eax,0x804      / 0x6E45A8 call 0x403260 (Move)
//                  ⇒ 落盘字节序 == 内存序 == Key 升序。
//
// 由 ①②③ 得出可双向换算的裸偏移映射（眼神桩体直读 [player+0x804] 时用的就是它）：
//     S(1,i).Key   在 bank + (i-1)*8
//     S(1,i).Value 在 bank + (i-1)*8 + 4
// 之所以成立，是因为眼神在 0x100CE4EA 起把 S(1,1..150) 灌满
// (0x100CE50D cmp esi,0x96 / jg 收尾)，而 SetS 对 value 无任何检查
// (0x6DF251/0x6DF255 只拒 group<=0 / index<=0)，所以「写 -1」同样建键
// ⇒ 键 1001..1150 连续占据槽位 0..149。
//
// 下面 BareOffsetProbes 里的 13 行不是推导出来的，是从眼神桩体里逐条读出的
// 内嵌期望键常量。它们同时验证 ①②③ —— 若键公式或步长被改动，13 行会立刻失配。
using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

const int NativeEntryStride = 8;          // TScriptTagArr elSize (RTTI @0x78D90C)
const int NativeKeyFieldOffset = 0;       // 0x6E42A2 cmp edi,[esi+eax*8]
const int NativeValueFieldOffset = 4;     // 0x6E42A7 mov eax,[esi+eax*8+4]
const int NativeGroupMultiplier = 1000;   // 0x6E42CC imul edx,0x3E8
const int NativeLowestPossibleKey = 1001; // group>=1 && index>=1
const int NativeSeedCount = 150;          // 0x100CE50D cmp esi,0x96
const int NativeSeedMarkerIndex = 49;     // 0x100CE517 cmp esi,0x31
const int NativeSeedMarkerValue = 1314;   // 0x100CE51C push 0x522
const int NativeSeedNegativeBelow = 9;    // 0x100CE541 cmp esi,9 / jge

// (桩体访问 VA, 裸偏移, 桩体内嵌的期望键, 该键的语义)
// 全部来自 D:/loym2/staging/_sbank/q12_final.py，13 命中 / 0 失配。
var bareOffsetProbes = new (uint Va, int Offset, int ExpectedKey, string Note)[]
{
    (0x1007AF32, 0x180, 1049, "S(1,49)  init marker key  81 FB 19 04 00 00"),
    (0x1007AF4E, 0x398, 1116, "S(1,116) 冰咆哮切割        81 FB 5C 04 00 00"),
    (0x1007AF98, 0x180, 1049, "S(1,49)  init marker key"),
    (0x1007AFB4, 0x3A0, 1117, "S(1,117) 火墙切割          81 FB 5D 04 00 00"),
    (0x1007AFFD, 0x180, 1049, "S(1,49)  init marker key"),
    (0x1007B019, 0x3A8, 1118, "S(1,118) 烈火切割          81 FB 5E 04 00 00"),
    (0x1007B063, 0x180, 1049, "S(1,49)  init marker key"),
    (0x1007B07F, 0x3B0, 1119, "S(1,119) 雷电术切割        81 FB 5F 04 00 00"),
    (0x1007B0C6, 0x180, 1049, "S(1,49)  init marker key"),
    (0x1007B0E2, 0x3B8, 1120, "S(1,120) 灵魂火符切割      81 FB 60 04 00 00"),
    (0x100DBA78, 0x2E8, 1094, "S(1,94)  施毒术毒范围      81 FF 46 04 00 00"),
    (0x100DBB23, 0x2F8, 1096, "S(1,96)  施毒术每次掉血    3D 48 04 00 00"),
    (0x100DBB3E, 0x328, 1102, "S(1,102) 施毒术中毒时间    3D 4E 04 00 00"),
};

// 眼神 SetS 包装器 0x10065F40 的三个常量现场 —— 用来钉 group/index 不被互换。
var wrapperCallSites = new (uint Va, int Group, int Index, int ExpectedKey)[]
{
    (0x100697FF, 1, 110, 1110),
    (0x10069844, 6, 1, 6001),
    (0x10090110, 6, 2, 6002),
};

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

var merge = ResolveMergeKeyValues();

CheckKeySynthesis();
CheckWrapperCallSitesPinArgumentOrder();
CheckNonPositiveArgumentsAreRejected();
CheckBareOffsetRoundTripAgainstPluginConstants();
CheckSeededBankPlacesEveryProbeOnItsSlot();
CheckPayloadStrideAndAscendingOrder();
CheckSubThousandKeysNeverReachThePayload();
CheckZeroIsStoredAsZero();

Console.WriteLine(
    $"PASS S-bank layout: key=group*{NativeGroupMultiplier}+index stride={NativeEntryStride} " +
    $"ascending seed=S(1,1..{NativeSeedCount}) probes={bareOffsetProbes.Length}/13 matched");
return;

// ---------------------------------------------------------------------------

// ① Key = group*1000 + index (sub_6E42CC).
void CheckKeySynthesis()
{
    foreach (var (_, offset, expectedKey, note) in bareOffsetProbes)
    {
        var slot = offset / NativeEntryStride;
        var index = slot + 1;
        var player = NewPlayer("key-synthesis");
        player.SetScriptVar('S', 1, index, 0x5A5A);
        Assert(player.m_ScriptSVars.TryGetValue(expectedKey, out var stored),
            $"SetS(1,{index}) did not file under key {expectedKey} ({note}); " +
            "sub_6E42CC is imul edx,0x3E8 / add ecx");
        Assert(stored == 0x5A5A, $"SetS(1,{index}) stored {stored}, expected 0x5A5A");
        Assert(player.m_ScriptSVars.Count == 1,
            $"SetS(1,{index}) wrote {player.m_ScriptSVars.Count} keys, expected exactly 1");
    }
}

// group 与 index 不可互换 —— 三个眼神常量现场。
void CheckWrapperCallSitesPinArgumentOrder()
{
    foreach (var (va, group, index, expectedKey) in wrapperCallSites)
    {
        var player = NewPlayer("wrapper-site");
        player.SetScriptVar('S', group, index, 7);
        Assert(player.m_ScriptSVars.ContainsKey(expectedKey),
            $"plugin site {va:X8} SetS({group},{index}) must land on key {expectedKey}; " +
            "swapping group/index would yield " +
            $"{index * NativeGroupMultiplier + group}");
    }
}

// GetS 0x6DF1BE/0x6DF1C2、SetS 0x6DF251/0x6DF255：任一参数 <= 0 直接拒绝。
// 原生把「守卫」和「存储」放在同一个函数里，C# 拆成了 PasApiBridge（守卫）+
// TPlayObject.SetScriptVar（存储原语）。所以这条契约必须在 API 层验，
// 在存储原语上验会验出一个它本来就不负责的东西。
void CheckNonPositiveArgumentsAreRejected()
{
    foreach (var (group, index) in new[] { (0, 5), (1, 0), (-1, 5), (1, -3), (0, 0) })
    {
        var player = NewPlayer("reject");
        var bridge = new PasApiBridge { CurrentPlayer = player };
        var wrote = bridge.SetPlayerVar('S', group, index, PasValue.FromInt(99));
        Assert(!wrote,
            $"SetS({group},{index}) returned True; native returns al=0 at " +
            "0x6DF24F and never reaches sub_6E4140 (0x6DF251/0x6DF255)");
        Assert(player.m_ScriptSVars.Count == 0,
            $"SetS({group},{index}) wrote a key; native rejects before any store");
        Assert(bridge.GetPlayerVar('S', group, index).AsInt() == -1,
            $"GetS({group},{index}) did not return the -1 seed (0x6DF1BB or esi,-1)");
    }

    // 未设置过的合法坐标同样是 -1，不是 0（sub_6E4270 0x6E427A 的种子）。
    var fresh = NewPlayer("miss");
    Assert(new PasApiBridge { CurrentPlayer = fresh }
            .GetPlayerVar('S', 1, 116).AsInt() == -1,
        "an unset S coordinate must read back -1, not 0 (0x6E427A)");
}

// ② + 换算公式：裸偏移 <-> S 坐标必须与桩体内嵌常量一致，双向。
void CheckBareOffsetRoundTripAgainstPluginConstants()
{
    foreach (var (va, offset, expectedKey, note) in bareOffsetProbes)
    {
        Assert(offset % NativeEntryStride == NativeKeyFieldOffset,
            $"{va:X8}: probe offset {offset:X} is not a Key field of an " +
            $"{NativeEntryStride}-byte record");
        var slot = offset / NativeEntryStride;

        // 正向：偏移 -> 键
        var derivedKey = NativeLowestPossibleKey + slot;
        Assert(derivedKey == expectedKey,
            $"{va:X8}: bank+{offset:X} -> slot {slot} -> key {derivedKey}, but the stub " +
            $"compares against {expectedKey} ({note})");

        // 反向：键 -> 偏移
        var index = expectedKey % NativeGroupMultiplier;
        var group = expectedKey / NativeGroupMultiplier;
        Assert(group == 1,
            $"{va:X8}: key {expectedKey} decodes to group {group}; every raw-bank stub " +
            "reads group 1");
        var keyOffset = (index - 1) * NativeEntryStride + NativeKeyFieldOffset;
        var valueOffset = (index - 1) * NativeEntryStride + NativeValueFieldOffset;
        Assert(keyOffset == offset,
            $"S(1,{index}).Key should sit at bank+{keyOffset:X} but the stub reads " +
            $"bank+{offset:X}");
        Assert(valueOffset == offset + NativeValueFieldOffset,
            $"S(1,{index}).Value should sit at bank+{valueOffset:X}");
    }
}

// ③ 连续性：灌种之后，每个桩体的裸偏移必须真的落在它自己的槽上。
// 这是整条链路的承重墙 —— 键公式、步长、升序任何一条被改，这里立刻塌。
void CheckSeededBankPlacesEveryProbeOnItsSlot()
{
    var player = NewPlayer("seeded");
    SeedLikePlugin(player);

    var payload = merge(null, player.m_ScriptSVars);
    Assert(payload.Length == NativeSeedCount * NativeEntryStride,
        $"seeded S bank serialised to {payload.Length} bytes, expected " +
        $"{NativeSeedCount * NativeEntryStride}");

    foreach (var (va, offset, expectedKey, note) in bareOffsetProbes)
    {
        Assert(offset + NativeEntryStride <= payload.Length,
            $"{va:X8}: bank+{offset:X} is past the end of the seeded bank");
        var keyAtOffset = BinaryPrimitives.ReadInt32LittleEndian(
            payload.AsSpan(offset + NativeKeyFieldOffset, 4));
        Assert(keyAtOffset == expectedKey,
            $"{va:X8}: the seeded bank holds key {keyAtOffset} at bank+{offset:X}, " +
            $"but the stub requires {expectedKey} ({note}). The trampoline would bail " +
            "and the feature would silently do nothing.");
    }

    // 标记槽是每个桩体的第二道守卫：bank+0x180 = 1049, bank+0x184 = 1314。
    var markerKey = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0x180, 4));
    var markerValue = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0x184, 4));
    Assert(markerKey == NativeGroupMultiplier + NativeSeedMarkerIndex,
        $"bank+0x180 holds key {markerKey}, expected " +
        $"{NativeGroupMultiplier + NativeSeedMarkerIndex}");
    Assert(markerValue == NativeSeedMarkerValue,
        $"bank+0x184 holds {markerValue}, expected the init marker " +
        $"{NativeSeedMarkerValue} (0x100CE51C push 0x522)");

    // 灌种规则：i<9 -> -1，i>=9 且 i!=49 -> 0。
    for (var i = 1; i <= NativeSeedCount; i++)
    {
        var slotOffset = (i - 1) * NativeEntryStride;
        var key = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(slotOffset, 4));
        Assert(key == NativeGroupMultiplier + i,
            $"slot {i - 1} holds key {key}, expected {NativeGroupMultiplier + i}; " +
            "the seeded key range must be contiguous 1001..1150");
        var value = BinaryPrimitives.ReadInt32LittleEndian(
            payload.AsSpan(slotOffset + NativeValueFieldOffset, 4));
        var expected = i == NativeSeedMarkerIndex ? NativeSeedMarkerValue
            : i < NativeSeedNegativeBelow ? -1 : 0;
        Assert(value == expected,
            $"S(1,{i}) seeded to {value}, expected {expected} " +
            "(0x100CE546 push -1 / 0x100CE554 push 0)");
    }
}

// ② + ③ 在序列化层面的表现：8 字节定长、Key 在前、严格升序。
void CheckPayloadStrideAndAscendingOrder()
{
    var player = NewPlayer("ordering");
    // 故意乱序写入，落盘必须是升序 —— 原生靠 sub_6E4140 维持，C# 靠 SortedDictionary。
    foreach (var (group, index, value) in new[]
             {
                 (6, 2, 62), (1, 150, 11150), (2, 1, 21), (1, 1, 11),
                 (1, 49, NativeSeedMarkerValue), (9, 9, 99),
             })
        player.SetScriptVar('S', group, index, value);

    var payload = merge(null, player.m_ScriptSVars);
    Assert(payload.Length % NativeEntryStride == 0,
        $"payload is {payload.Length} bytes, not a multiple of {NativeEntryStride}; " +
        "native rejects that outright at 0x6E4561 idiv / 0x6E4564 test edx,edx / jne");

    var previous = int.MinValue;
    for (var offset = 0; offset < payload.Length; offset += NativeEntryStride)
    {
        var key = BinaryPrimitives.ReadInt32LittleEndian(
            payload.AsSpan(offset + NativeKeyFieldOffset, 4));
        Assert(key > previous,
            $"payload key order broke at bank+{offset:X} ({previous} then {key}); " +
            "sub_6E4270's binary search requires strictly ascending keys");
        previous = key;
    }

    var firstKey = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
    var firstValue = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4));
    Assert(firstKey == 1001 && firstValue == 11,
        $"slot 0 is ({firstKey},{firstValue}), expected (1001,11) — Key at +0, Value at +4");
}

// 键 < 1001 只可能来自 group 0，而 group 0 的 V 槽在 +0x80C..+0x99B，从不落盘。
void CheckSubThousandKeysNeverReachThePayload()
{
    var store = new Dictionary<int, int> { [7] = 7, [100] = 100, [1000] = 1000, [1001] = 1 };
    var payload = merge(null, store);
    Assert(payload.Length == NativeEntryStride,
        $"payload kept {payload.Length / NativeEntryStride} entries, expected only key 1001; " +
        "decoder sub_6E448C only ever touches +0x804/+0x808, never the inline group-0 region");
    var key = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
    Assert(key == NativeLowestPossibleKey, $"surviving key is {key}, expected 1001");
}

// sub_6E4140 的四个 Value 存储点都没有零值测试 (0x6E4187/0x6E41C2/0x6E4231/0x6E4260)。
void CheckZeroIsStoredAsZero()
{
    var player = NewPlayer("zero");
    player.SetScriptVar('S', 1, 9, 0);
    Assert(player.TryGetScriptVar('S', 1, 9, out var read) && read == 0,
        "S(1,9)=0 did not read back as an explicit 0");
    var payload = merge(null, player.m_ScriptSVars);
    Assert(payload.Length == NativeEntryStride,
        "a zero-valued key was dropped from the payload; native has no zero-value " +
        "special case anywhere in sub_6E4140");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)) == 0,
        "the persisted value is not 0");
}

// ---------------------------------------------------------------------------

// 复刻 0x100CE4EA 的一次性灌种，用来构造出桩体所假设的那张银行。
void SeedLikePlugin(TPlayObject player)
{
    for (var i = 1; i <= NativeSeedCount; i++)
    {
        if (i == NativeSeedMarkerIndex)
        {
            player.SetScriptVar('S', 1, i, NativeSeedMarkerValue);
            continue;
        }
        // 0x100CE53B test eax,eax / 0x100CE53D jns -> 只在当前值为负（含 miss=-1）时写。
        var current = player.TryGetScriptVar('S', 1, i, out var v) ? v : -1;
        if (current >= 0) continue;
        player.SetScriptVar('S', 1, i, i < NativeSeedNegativeBelow ? -1 : 0);
    }
}

static TPlayObject NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_sMapName = "audit-map",
    m_nCurrX = 12,
    m_nCurrY = 34
};

// 反射解析私有的 MergeKeyValues。必须显式断言拿到了方法 —— 一个返回 null 的
// GetMethod 加上 ?.Invoke 会让整个工具编译通过却空跑（REPLICATION_RULES §4.17）。
static Func<byte[], Dictionary<int, int>, byte[]> ResolveMergeKeyValues()
{
    var codec = typeof(dbsvr::DBSvr.Core.NativeHumanDataCodec);
    var method = codec.GetMethod("MergeKeyValues",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
        binder: null,
        types: new[] { typeof(byte[]), typeof(Dictionary<int, int>) },
        modifiers: null);
    Assert(method != null,
        "NativeHumanDataCodec.MergeKeyValues(byte[], Dictionary<int,int>) not found — " +
        "this audit cannot verify the on-disk S bank layout without it");
    Assert(method.ReturnType == typeof(byte[]),
        $"MergeKeyValues returns {method.ReturnType}, expected byte[]");
    return (original, current) =>
        (byte[])method.Invoke(null, new object[] { original, current });
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
