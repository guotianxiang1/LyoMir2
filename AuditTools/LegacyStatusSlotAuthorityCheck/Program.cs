// STATE-53: the 12 legacy status slots must be a view on the native state list,
// not a second store.
//
// Native keeps exactly one timed-state store. AddState @0x7730D0 allocates an
// 18-byte record (0x764E00 `mov eax,0x12 / call 0x402FA0`) and prepends it to
// the list at Self+0xDC:
//
//   +0x00 flag         0x77310F  88 10                 mov [eax], dl
//   +0x01 internalType 0x773164  88 58 01              mov [eax+1], bl
//   +0x02 remaining ms 0x77315E  89 50 02              mov [eax+2], edx
//   +0x06 lastTick     0x7731B3  89 42 06              mov [edx+6], eax
//   +0x0A value        0x77316A  89 78 0A              mov [eax+0xA], edi
//   +0x0E next         0x773176  89 50 0E              mov [eax+0xE], edx
//                      0x77317C  89 86 DC 00 00 00     mov [esi+0xDC], eax
//
// plus a 112-bit presence bitset at Self+0x168 (bts 0x77299B / btr 0x7729B9 /
// bt 0x772968). Expiry is a single 500 ms sweep, 0x772FD0:
//
//   0x772FE4  2B 83 E0 00 00 00     sub eax, [ebx+0xE0]
//   0x772FEA  3D F4 01 00 00        cmp eax, 0x1F4
//   0x772FEF  0F 82 AF 00 00 00     jb 0x7730A4          ; unsigned: <500 skips
//
// and the per-record step @0x7730AC works in raw tick deltas:
//
//   0x7730B1  83 78 02 FF           cmp dword [eax+2], -1   ; permanent sentinel
//   0x7730B9  2B 50 06              sub edx, [eax+6]        ; now - lastTick
//   0x7730BC  29 50 02              sub [eax+2], edx
//   0x7730C2  83 78 02 00 / 7F 02   jg alive                ; SIGNED
//
// There is no seconds array anywhere in it. C# had one - ushort[12] with its own
// once-per-second countdown - which is the REPLICATION_RULES 4.18 case named in
// the rules file. This audit pins the collapse.
//
// Slot i is native state 31 - i. Proofs:
//  1. GetCharStatus used to project slot i onto wire bit 31 - i
//     (`0x80000000 >> i`), and native ships the raw bitset where state s is
//     bit s (0x7729D4 `lea edx,[eax+0x168]`, RefMsg 0x291 = 657).
//  2. MakePosion forwards nType as state 31 - nType into AddState, mirroring
//     native MakePoison @0x76B3C8 which passes the caller's id straight through.
//  3. The state-lost message dispatch @0x742692
//     (`add eax,-0xE / cmp eax,0x5C / ja / jmp [eax*4+0x7426A9]`) sends state 22
//     to arm 0x74296D -> 0x7433D8 "防御力回复正常". STATE_DEFENCEUP is slot 9 and
//     31 - 9 = 22. Same for state 21 / slot 10 -> 0x7433BC "抗魔法力回复正常".

using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

var passed = 0;
var failed = 0;

// slot -> native state, from the three proofs above.
var slotMap = new (int Slot, byte State, string Name)[]
{
    (Grobal2.POISON_DECHEALTH,      31, "POISON_DECHEALTH"),
    (Grobal2.POISON_DAMAGEARMOR,    30, "POISON_DAMAGEARMOR"),
    (Grobal2.POISON_LOCKSPELL,      29, "POISON_LOCKSPELL"),
    (Grobal2.STATE_LOCKRUN,         28, "STATE_LOCKRUN"),
    (Grobal2.POISON_DONTMOVE,       27, "POISON_DONTMOVE"),
    (Grobal2.POISON_STONE,          26, "POISON_STONE"),
    (6,                             25, "slot6"),
    (7,                             24, "slot7"),
    (Grobal2.STATE_TRANSPARENT,     23, "STATE_TRANSPARENT"),
    (Grobal2.STATE_DEFENCEUP,       22, "STATE_DEFENCEUP"),
    (Grobal2.STATE_MAGDEFENCEUP,    21, "STATE_MAGDEFENCEUP"),
    (Grobal2.STATE_BUBBLEDEFENCEUP, 20, "STATE_BUBBLEDEFENCEUP")
};

Console.WriteLine("[STATE-53] legacy status slots are a view on the native list");
Console.WriteLine(new string('=', 70));

// ---- 1. the slot table is exactly 31 - i, all 12 of them -------------------
foreach (var (slot, state, name) in slotMap)
{
    Check($"slot {slot} ({name}) is native state {state}",
        31 - slot == state);
}

// ---- 2. writing a slot creates the native state, and only that state -------
foreach (var (slot, state, name) in slotMap)
{
    var actor = NewActor();
    actor.m_wStatusTimeArr[slot] = 7;

    Check($"{name}: write lit native state {state}",
        actor.HasNativeActiveState(state));
    Check($"{name}: exactly one node exists",
        CountNodes(actor) == 1);
    Check($"{name}: the node carries state {state}",
        NodeTypes(actor).Contains(state));
    Check($"{name}: read-back is the value written",
        actor.m_wStatusTimeArr[slot] == 7);

    // No other slot may light up: a second store would show as cross-talk.
    var bleed = slotMap.Where(e => e.Slot != slot)
        .Where(e => actor.m_wStatusTimeArr[e.Slot] != 0).ToList();
    Check($"{name}: no other slot reports time",
        bleed.Count == 0);
}

// ---- 3. clearing the native bit clears the slot ----------------------------
// FindState @0x773BB1 tests the bitset first (`call 0x772960 / test al,al / je`)
// and returns nil, so a record with a cleared bit is invisible. Before the
// collapse the slot kept its own countdown and reported time regardless.
foreach (var (slot, state, name) in slotMap)
{
    var actor = NewActor();
    actor.m_wStatusTimeArr[slot] = 5;
    actor.ClearNativeActiveState(state);
    Check($"{name}: slot reads 0 once the state bit is cleared",
        actor.m_wStatusTimeArr[slot] == 0);
}

// ---- 4. sub-second remainders must not read as expired ---------------------
// The ~60 action gates are written `slot == 0`. If the seconds projection
// truncated, a node with 500 ms left would unblock them while still alive.
{
    var actor = NewActor();
    actor.m_wStatusTimeArr[Grobal2.POISON_STONE] = 1;
    var node = FirstNode(actor);
    SetNodeRemaining(node, 1);
    Check("1 ms remaining still reads as 1 second, not 0",
        actor.m_wStatusTimeArr[Grobal2.POISON_STONE] == 1);
    Check("1 ms remaining keeps the state active",
        actor.HasNativeActiveState(26));

    SetNodeRemaining(node, 1000);
    Check("exactly 1000 ms reads as 1 second",
        actor.m_wStatusTimeArr[Grobal2.POISON_STONE] == 1);
    SetNodeRemaining(node, 1001);
    Check("1001 ms reads as 2 seconds",
        actor.m_wStatusTimeArr[Grobal2.POISON_STONE] == 2);
}

// ---- 5. the permanent sentinel round-trips ---------------------------------
// Native marks permanence with remaining == -1 (0x7730B1 `cmp dword [eax+2],-1
// / je`, which skips the whole decrement). The legacy loop had no such marker
// and instead skipped anything >= 60000, which is why the two writers that
// meant "never expire" spelled it 6 * 10 * 1000.
{
    var actor = NewActor();
    actor.m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 6 * 10 * 1000;
    var node = FirstNode(actor);
    Check("60000 s maps to the native -1 permanent sentinel",
        GetNodeRemaining(node) == -1);
    Check("a permanent node reads back as 60000",
        actor.m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] == 60000);
}

// ---- 6. expiry is driven by the 500 ms sweep, not a 1 s countdown ----------
{
    var actor = NewActor();
    actor.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] = 2;

    const int start = 0x10000;
    SetField(actor, "m_TimedAbilityProcessTick", start);
    SetNodeField(FirstNode(actor), "LastTick", start);

    // Below the 0x1F4 interval nothing may move: `cmp eax,0x1F4 / jb`.
    actor.ProcessTimedAbilities(start + 499);
    Check("sweep below 500 ms does not decrement",
        GetNodeRemaining(FirstNode(actor)) == 2000);

    // At exactly 500 the sweep runs; jb is unsigned <, so 500 is not below.
    actor.ProcessTimedAbilities(start + 500);
    Check("sweep at exactly 500 ms runs",
        GetNodeRemaining(FirstNode(actor)) == 1500);
    Check("1500 ms still reads as 2 seconds",
        actor.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] == 2);

    // 0x7730C2 `cmp dword [eax+2],0 / jg alive` is a SIGNED test, so hitting
    // exactly 0 expires the record.
    actor.ProcessTimedAbilities(start + 2000);
    Check("remaining 0 expires the state",
        !actor.HasNativeActiveState(31));
    Check("expired slot reads 0",
        actor.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] == 0);
    Check("expired node is off the list",
        CountNodes(actor) == 0);
}

// ---- 7. save / load / live agree (REPLICATION_RULES 4.19) ------------------
{
    var actor = NewActor();
    actor.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] = 11;
    actor.m_wStatusTimeArr[Grobal2.STATE_DEFENCEUP] = 22;

    var saved = actor.m_wStatusTimeArr.ToArray();
    Check("save projection is 12 slots", saved.Length == 12);
    Check("save projection carries slot 0", saved[Grobal2.POISON_DECHEALTH] == 11);
    Check("save projection carries slot 9", saved[Grobal2.STATE_DEFENCEUP] == 22);

    var restored = NewActor();
    restored.m_wStatusTimeArr.CopyFrom(saved);
    Check("load restores slot 0", restored.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] == 11);
    Check("load restores slot 9", restored.m_wStatusTimeArr[Grobal2.STATE_DEFENCEUP] == 22);
    Check("load lit native state 31", restored.HasNativeActiveState(31));
    Check("load lit native state 22", restored.HasNativeActiveState(22));
    Check("load built exactly two nodes", CountNodes(restored) == 2);
    Check("load is idempotent through another save",
        restored.m_wStatusTimeArr.ToArray()
            .SequenceEqual(saved));
}

// ---- 8. the second store is really gone ------------------------------------
{
    var fields = typeof(TBaseObject)
        .GetFields(BindingFlags.Instance | BindingFlags.Public |
                   BindingFlags.NonPublic)
        .Select(f => f.Name).ToHashSet();
    Check("no ushort[] m_wStatusTimeArr field remains",
        !fields.Contains("m_wStatusTimeArr"));
    Check("no m_dwStatusArrTick companion remains",
        !fields.Contains("m_dwStatusArrTick"));
}

Console.WriteLine(new string('=', 70));
Console.WriteLine($"Result: {passed} passed, {failed} failed");
if (failed == 0) Console.WriteLine("AUDIT_PASS");
return failed == 0 ? 0 : 1;

void Check(string name, bool ok)
{
    if (ok) { Console.WriteLine("[PASS] " + name); passed++; }
    else { Console.WriteLine("[FAIL] " + name); failed++; }
}

static TBaseObject NewActor() => new TBaseObject();

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
    var directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

static void SetField(TBaseObject actor, string name, object value) =>
    typeof(TBaseObject)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        .SetValue(actor, value);

static void SetNodeField(object node, string name, object value) =>
    node.GetType().GetField(name).SetValue(node, value);

static object FirstNode(TBaseObject actor) =>
    typeof(TBaseObject)
        .GetField("m_TimedAbilityHead", BindingFlags.Instance | BindingFlags.NonPublic)
        .GetValue(actor);

static int CountNodes(TBaseObject actor)
{
    var node = FirstNode(actor);
    var next = node?.GetType().GetField("Next");
    var n = 0;
    while (node != null) { n++; node = next.GetValue(node); }
    return n;
}

static List<byte> NodeTypes(TBaseObject actor)
{
    var types = new List<byte>();
    var node = FirstNode(actor);
    if (node == null) return types;
    var t = node.GetType();
    var typeField = t.GetField("InternalType");
    var nextField = t.GetField("Next");
    while (node != null)
    {
        types.Add((byte)typeField.GetValue(node));
        node = nextField.GetValue(node);
    }
    return types;
}

static int GetNodeRemaining(object node) =>
    (int)node.GetType().GetField("RemainingMilliseconds").GetValue(node);

static void SetNodeRemaining(object node, int ms) =>
    node.GetType().GetField("RemainingMilliseconds").SetValue(node, ms);
