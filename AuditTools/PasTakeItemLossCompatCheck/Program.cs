// PasTakeItemLossCompatCheck —— PAS 脚本 take/takeexpand 物品损失闸.
//
// Guards the three item-loss invariants of the 战神 script item-consumption path. Each
// assertion below names the exact native address it encodes, so a regression is traceable
// to the disassembly rather than to an opinion:
//
//   (1) ALL-OR-NOTHING. `Take` = sub_6DFA40 -> sub_6DF7E8 PRE-COUNTS before mutating:
//       0x6DF854 `call sub_7447CC` / 0x6DF85F `cmp eax,[ebp-8]` / 0x6DF862 `jl 0x6DF9F3`
//       jumps to the epilogue with the result byte [ebp-9] still 0 and ZERO items removed.
//       Only after the count passes does 0x6DF86D set the byte and the removal loop begin
//       @0x6DF871. A short `take` must therefore leave the bag byte-identical.
//
//   (2) STACKS COUNT AND CONSUME BY Dura, and DECREMENT rather than delete-whole.
//       count   : 0x744852 `cmp byte [ebx+0x14],7` / 0x744858 `movzx eax,word [ebx+0x26]`
//                 / 0x74485C `add [ebp-0xc],eax`, vs 0x744861 `inc [ebp-0xc]` for non-stacks.
//       consume : 0x6DF8E0 `cmp byte [ebx+0x14],7`; a partial take does 0x6DF96A
//                 `add [ebp-0x14],edi` + 0x6DF96D `sub word [ebx+0x26],di` (the item STAYS)
//                 + 0x6DF984 `call [vtbl+0x260]`; a fully drained stack falls to 0x6DF93F /
//                 0x6DF94D sub_424B30 / 0x6DF95B [vtbl+0x268] / 0x6DF963 sub_404690.
//       The `[item+0x14] == 7` tag is the RUNTIME pile class, written only by the pile ctor
//       sub_7880F0 @0x788118 (the base ctor sub_783788 writes 0 @0x7837AE) — i.e.
//       NativeItemFactory.IsPileItem, NOT `StdMode == 7`.
//
//   (3) `TakeExpand`'s 3rd argument is a TRI-STATE LOCK FILTER over sub_784710
//       (`mov ax, word [eax+0x34] / ret` = the btValue[10..11] bind word), NOT
//       "include equipment": 0 = accept all (0x6DF8B2 `je`), 1 = locked-only
//       (0x6DF8C4 `ja`), 2 = unlocked-only (0x6DF8DA `jne` skip), anything else skips every
//       slot (0x6DF8CA fall-through). sub_6DF7E8 walks [player+0x508] (m_ItemList) ONLY and
//       never reads m_UseItems, so worn gear is never removed on this path.
//
// Also pinned: `DelAllThisItem` = sub_7409D4 -> sub_740A00 is its OWN unconditional
// descending sweep (no count, no filter, no Dura math) and must NOT inherit Take's
// all-or-nothing gate; and `AddTaskToUIList` = sub_6E12E4 sends packet mode 2
// (0x6E1304 `push 2`), identical to UpdateTaskDetail sub_6E131C (0x6E133C).

using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();

// Index 1 = a plain single item (runtime kind byte 0 via the base ctor sub_783788).
// Index 2 = a pile/stack (StdMode >= 150 -> NativeItemFactory pile class -> [+0x14] == 7).
const string Single = "审计单品";
const string Stack = "审计堆叠";
SetDefinitions(
    new GoodItem { Name = Single, StdMode = 0, Shape = 0, DuraMax = 100 },
    new GoodItem { Name = Stack, StdMode = 151, Shape = 0, DuraMax = 5000 });

var bridge = new PasApiBridge();
var takeCore = Method("TakeItemsCore");
var countBag = Method("CountBagItem");
var takeItems = Method("TakeItems");
var takeItemsEx = Method("TakeItemsEx");
var delAll = Method("DelAllThisItem");

// The pile predicate must be the runtime kind byte, not StdMode == 7.
var isPileItem = typeof(M2Share).Assembly
    .GetType("GameSvr.NativeItemFactory", throwOnError: true)!
    .GetMethod("IsPileItem", BindingFlags.Static | BindingFlags.NonPublic);
Assert(isPileItem != null, "NativeItemFactory.IsPileItem is missing");
bool IsPile(GoodItem item) => (bool)isPileItem.Invoke(null, new object[] { item })!;

Assert(IsPile(Def(Stack)),
    "StdMode 151 must resolve as a pile item ([item+0x14]==7 via sub_7880F0 @0x788118)");
Assert(!IsPile(Def(Single)),
    "StdMode 0 must NOT be a pile item (base ctor sub_783788 writes [+0x14]=0 @0x7837AE)");
Assert(!IsPile(new GoodItem { Name = "x", StdMode = 7 }),
    "StdMode 7 is TCharm, not a stack — the pile tag must not be keyed off StdMode == 7");

// =====================================================================================
// (1) SHORT `take` MUST MUTATE NOTHING — sub_6DF7E8 @0x6DF862 `jl` -> epilogue.
//     Worked example from the brief: player holds 3, script asks for 5.
// =====================================================================================
{
    var player = NewPlayer();
    player.m_ItemList.Add(NewItem(1, 11));
    player.m_ItemList.Add(NewItem(1, 12));
    player.m_ItemList.Add(NewItem(1, 13));
    bridge.CurrentPlayer = player;
    var before = Snapshot(player);

    Assert(!Take(Single, 5),
        "short take must return False (result byte [ebp-9] stays 0 at 0x6DF862)");
    AssertUnchanged(before, player,
        "ITEM LOSS: short take mutated the bag — native removes NOTHING (0x6DF862 jl 0x6DF9F3)");
    Equal(3, player.m_ItemList.Count, "short take must leave all 3 items");

    // The exactly-sufficient and surplus cases must still succeed and consume exactly.
    Assert(Take(Single, 3), "exact-count take must succeed (0x6DF85F cmp / not jl)");
    Equal(0, player.m_ItemList.Count, "exact-count take must consume all 3");
}
{
    var player = NewPlayer();
    for (var i = 0; i < 5; i++) player.m_ItemList.Add(NewItem(1, 20 + i));
    bridge.CurrentPlayer = player;
    Assert(Take(Single, 2), "surplus take must succeed");
    Equal(3, player.m_ItemList.Count,
        "surplus take must consume exactly the requested count (0x6DF9D9 jge break)");
}

// A short take across a MIXED bag (stack + singles) must also be all-or-nothing: the
// pre-count at 0x6DF854 uses the same Dura-aware core, so a 2-Dura stack + 1 single = 3.
{
    var player = NewPlayer();
    player.m_ItemList.Add(NewStack(2, 31, 2));
    player.m_ItemList.Add(NewItem(1, 32));
    bridge.CurrentPlayer = player;
    var before = Snapshot(player);
    Equal(3, Count(Stack, 0) + Count(Single, 0), "mixed-bag baseline count");
    Assert(!Take(Stack, 3),
        "a short stack take must fail (bag holds Dura 2, request 3)");
    AssertUnchanged(before, player,
        "ITEM LOSS: short stack take mutated the bag (0x6DF862)");
}

// =====================================================================================
// (2) STACKS: counted by Dura (0x744858/0x74485C) and DECREMENTED (0x6DF96D), not deleted.
//     Worked example from the brief: one 5000 stack, script asks for 100 -> 4900 remain.
// =====================================================================================
{
    var player = NewPlayer();
    var stack = NewStack(2, 41, 5000);
    player.m_ItemList.Add(stack);
    bridge.CurrentPlayer = player;

    Equal(5000, Count(Stack, 0),
        "a stack must count as its Dura (0x744858 movzx / 0x74485C add), not 1");
    Assert(Take(Stack, 100), "take 100 from a 5000 stack must succeed");
    Equal(1, player.m_ItemList.Count,
        "ITEM LOSS: the stack entry was deleted — native decrements and keeps it (0x6DF96D)");
    Assert(ReferenceEquals(player.m_ItemList[0], stack),
        "the surviving entry must be the same physical stack object");
    Equal(4900, stack.Dura,
        "ITEM LOSS: stack Dura must fall by exactly the requested amount (0x6DF96D sub word[+0x26],di)");
    Equal(4900, Count(Stack, 0), "post-take count must reflect the decremented Dura");

    // A partial take must announce the new durability, not a deletion (0x6DF984 [vtbl+0x260]).
    // SendDefMessage builds the packet into m_DefMsg before writing it to the socket, so the
    // last-built default message is the observable here.
    Equal(Grobal2.SM_BAGITEMDURACHG, player.m_DefMsg.Ident,
        "a partial stack take must send SM_BAGITEMDURACHG (native [vtbl+0x260] @0x6DF984)");
    Equal(4900, player.m_DefMsg.Param,
        "the durability refresh must carry the DECREMENTED Dura, not a deletion");
}
{
    // Exactly draining a stack DOES delete the whole entry (0x6DF93F..0x6DF963).
    var player = NewPlayer();
    player.m_ItemList.Add(NewStack(2, 42, 40));
    bridge.CurrentPlayer = player;
    Assert(Take(Stack, 40), "draining a stack exactly must succeed");
    Equal(0, player.m_ItemList.Count,
        "a fully drained stack must be removed (0x6DF94D sub_424B30)");
}
{
    // Multi-stack: drain the first, decrement the second — never over-consume.
    var player = NewPlayer();
    var a = NewStack(2, 43, 30);
    var b = NewStack(2, 44, 30);
    player.m_ItemList.Add(a);
    player.m_ItemList.Add(b);
    bridge.CurrentPlayer = player;
    Equal(60, Count(Stack, 0), "two 30-stacks must count 60");
    Assert(Take(Stack, 45), "spanning take across two stacks must succeed");
    Equal(15, Count(Stack, 0),
        "spanning take must consume exactly 45 of 60 (0x6DF93F add / 0x6DF96A add)");
    Equal(1, player.m_ItemList.Count, "exactly one stack entry must survive");
}
{
    // A short take spanning stacks must still mutate nothing.
    var player = NewPlayer();
    player.m_ItemList.Add(NewStack(2, 45, 30));
    player.m_ItemList.Add(NewStack(2, 46, 30));
    bridge.CurrentPlayer = player;
    var before = Snapshot(player);
    Assert(!Take(Stack, 61), "a take exceeding the total stack Dura must fail");
    AssertUnchanged(before, player,
        "ITEM LOSS: short spanning take mutated the stacks (0x6DF862)");
}

// =====================================================================================
// (3) `takeexpand` MUST NEVER TOUCH EQUIPPED ITEMS, and its 3rd arg is a lock filter.
// =====================================================================================
{
    var player = NewPlayer();
    var worn = NewItem(1, 51);
    player.m_UseItems[Grobal2.U_WEAPON] = worn;
    player.m_ItemList.Add(NewItem(1, 52));
    bridge.CurrentPlayer = player;

    // THE EXACT OLD-BUG SHAPE FIRST: bag is SHORT and the 3rd arg is non-zero. Under the
    // old `iParam != 0 -> bIncludeEqp` reading this stripped the worn copy to make up the
    // shortfall. Natively the 3rd arg is a lock filter and m_UseItems is never read.
    // Equipment is checked BEFORE any return-value assertion so that an equipment strip is
    // reported as the item-loss it is, never masked by a boolean mismatch.
    var tookShort = TakeEx(Single, 2, 1);
    AssertWorn(player, worn,
        "ITEM LOSS: a SHORT takeexpand with a non-zero 3rd arg stripped WORN gear "
        + "— that is the old bIncludeEqp bug (sub_6DF7E8 walks [player+0x508] only)");
    Assert(!tookShort, "a short takeexpand must fail rather than raid equipment");
    Equal(1, player.m_ItemList.Count, "a short takeexpand must leave the bag intact");

    // Ask for 2 while the bag holds 1 and an identical item is worn, filter 0.
    var took0 = TakeEx(Single, 2, 0);
    AssertWorn(player, worn,
        "ITEM LOSS: takeexpand filter 0 stripped WORN gear — sub_6DF7E8 walks [player+0x508] only");
    Assert(!took0, "takeexpand must not satisfy a shortfall from equipment");
    Equal(1, player.m_ItemList.Count,
        "the failed takeexpand must also leave the bag intact (0x6DF862)");

    // filter 1 (what the legacy `iParam != 0` mapped to "include equipment") selects
    // LOCKED-only, so an unlocked bag item is skipped and nothing is consumed.
    var took1 = TakeEx(Single, 1, 1);
    AssertWorn(player, worn,
        "ITEM LOSS: takeexpand filter 1 stripped WORN gear (the old bIncludeEqp sweep)");
    Assert(!took1, "filter 1 (locked-only, 0x6DF8C4 ja) must reject an unlocked bag item");
    Equal(1, player.m_ItemList.Count, "filter 1 rejection must not mutate the bag");

    // Every remaining filter value, including out-of-range ones, must leave gear alone.
    foreach (var filter in new[] { 2, 3, -1 })
    {
        TakeEx(Single, 2, filter);
        AssertWorn(player, worn,
            $"ITEM LOSS: takeexpand filter {filter} stripped WORN gear");
    }
}
{
    // The tri-state, exercised on the bind word btValue[10..11] (sub_784710 = word[+0x34]).
    var player = NewPlayer();
    var unlocked = NewItem(1, 61);
    var locked = NewItem(1, 62);
    locked.btValue[10] = 1;                     // word[item+0x34] = 1
    player.m_ItemList.Add(unlocked);
    player.m_ItemList.Add(locked);
    bridge.CurrentPlayer = player;

    Equal(2, Count(Single, 0), "filter 0 must count every slot (0x6DF8B2 je / 0x74482C je)");
    Equal(1, Count(Single, 1), "filter 1 must count only locked slots (0x744836 ja)");
    Equal(1, Count(Single, 2), "filter 2 must count only unlocked slots (0x744850 jne)");
    Equal(0, Count(Single, 3),
        "an unrecognised filter must skip every slot (0x744844 / 0x6DF8CA fall-through)");

    Assert(!TakeEx(Single, 2, 1), "filter 1 has only 1 locked slot -> short -> no mutation");
    Equal(2, player.m_ItemList.Count, "ITEM LOSS: rejected filter-1 take mutated the bag");

    Assert(TakeEx(Single, 1, 2), "filter 2 must consume the unlocked slot");
    Equal(1, player.m_ItemList.Count, "filter 2 must remove exactly one slot");
    Assert(ReferenceEquals(player.m_ItemList[0], locked),
        "filter 2 must have spared the LOCKED item (0x6DF8DA jne skip)");

    Assert(TakeEx(Single, 1, 1), "filter 1 must now consume the locked slot");
    Equal(0, player.m_ItemList.Count, "filter 1 must remove the locked slot");
}

// An unknown item name is a clean no-op with no scan (0x6DF842 `cmp [ebp-0x10],0 / jle`).
{
    var player = NewPlayer();
    player.m_ItemList.Add(NewItem(1, 71));
    bridge.CurrentPlayer = player;
    var before = Snapshot(player);
    Assert(!Take("不存在的物品", 1), "an unknown item name must return False");
    AssertUnchanged(before, player, "an unknown item name must not mutate the bag");
    Equal(0, Count("不存在的物品", 0), "an unknown item name must count 0");
}

// `count <= 0` returns True with no work (0x6DF815 `cmp [ebp-8],0 / jne` -> 0x6DF81B).
{
    var player = NewPlayer();
    player.m_ItemList.Add(NewItem(1, 72));
    bridge.CurrentPlayer = player;
    var before = Snapshot(player);
    Assert(Take(Single, 0), "take with count 0 must return True (0x6DF81B mov byte[ebp-9],1)");
    Assert(Take(Single, -3), "take with a negative count must return True");
    AssertUnchanged(before, player, "a non-positive count must not mutate the bag");
}

// `DelAllThisItem` = sub_7409D4: its own unconditional sweep, NOT a Take loop. It must
// remove every matching slot (stack entries whole) and must never inherit the all-or-nothing
// pre-count, which would make it a no-op on a bag it is supposed to clear.
{
    var player = NewPlayer();
    player.m_ItemList.Add(NewItem(1, 81));
    player.m_ItemList.Add(NewStack(2, 82, 500));
    player.m_ItemList.Add(NewItem(1, 83));
    var keep = NewStack(2, 84, 7);
    bridge.CurrentPlayer = player;
    Equal(2, (int)delAll.Invoke(bridge, new object[] { Single })!,
        "DelAllThisItem must report every removed slot (0x740ACC inc [ebp-0xc])");
    Equal(1, player.m_ItemList.Count, "DelAllThisItem must remove all matching slots");
    Equal(500, player.m_ItemList[0].Dura,
        "DelAllThisItem must not touch a non-matching stack's Dura");
    Equal(1, (int)delAll.Invoke(bridge, new object[] { Stack })!,
        "DelAllThisItem must delete a stack entry WHOLE (no Dura math @sub_740A00)");
    Equal(0, player.m_ItemList.Count, "DelAllThisItem must clear every matching entry");
    Equal(0, (int)delAll.Invoke(bridge, new object[] { Single })!,
        "DelAllThisItem on an empty bag must report 0");
    Equal(7, keep.Dura, "control object must be untouched");
}

// =====================================================================================
// Script variable sentinels: GetV/GetS miss = -1, and arg <= 0 is rejected.
// =====================================================================================
{
    var player = NewPlayer();
    bridge.CurrentPlayer = player;
    Equal(-1, bridge.GetPlayerVar('V', 7, 3).AsInt(),
        "a V miss must be -1 (0x6DF1F1 mov [ebp-4],0xFFFFFFFF), never 0");
    Equal(-1, bridge.GetPlayerVar('S', 7, 3).AsInt(),
        "an S miss must be -1 (0x6DF1BB or esi,0xFFFFFFFF)");

    bridge.SetPlayerVar('V', 7, 3, PasValue.FromInt(42));
    Equal(42, bridge.GetPlayerVar('V', 7, 3).AsInt(), "a stored V value must read back");
    Equal(7003, 7 * 1000 + 3);
    Assert(player.m_ScriptVVars.ContainsKey(7003),
        "the flat key must stay arg1*1000+arg2 (sub_6E42CC imul edx,0x3E8 / add ecx)");

    // arg <= 0 rejects: 0x6DF1BE/0x6DF1C2 (GetS), 0x6DF21B/0x6DF21F (GetV),
    // 0x6DF251/0x6DF255 (SetS), 0x6DF2B3/0x6DF2B7 (SetV).
    foreach (var (g, i, label) in new[]
             {
                 (-1, 5, "negative group"), (5, -1, "negative index"),
                 (-1, -1, "both negative"), (5, 0, "zero index on a keyed group")
             })
    {
        Equal(-1, bridge.GetPlayerVar('V', g, i).AsInt(), $"GetV must reject a {label}");
        Equal(-1, bridge.GetPlayerVar('S', g, i).AsInt(), $"GetS must reject a {label}");
        var vBefore = player.m_ScriptVVars.Count;
        var sBefore = player.m_ScriptSVars.Count;
        bridge.SetPlayerVar('V', g, i, PasValue.FromInt(9));
        bridge.SetPlayerVar('S', g, i, PasValue.FromInt(9));
        Equal(vBefore, player.m_ScriptVVars.Count, $"SetV must not write on a {label}");
        Equal(sBefore, player.m_ScriptSVars.Count, $"SetS must not write on a {label}");
    }

    // Group 0 keeps the native fast-path bound 1..100 (0x6DF20A sub edx,0x64 / jae).
    Equal(-1, bridge.GetPlayerVar('V', 0, 0).AsInt(), "group 0 index 0 is out of 1..100");
    Equal(-1, bridge.GetPlayerVar('V', 0, 101).AsInt(), "group 0 index 101 is out of 1..100");
    bridge.SetPlayerVar('V', 0, 101, PasValue.FromInt(9));
    Assert(!player.m_ScriptVVars.ContainsKey(101),
        "group 0 index 101 must not write (0x6DF2A3 jae -> keyed path -> group 0 jle)");
    bridge.SetPlayerVar('V', 0, 100, PasValue.FromInt(9));
    Equal(9, bridge.GetPlayerVar('V', 0, 100).AsInt(),
        "group 0 index 100 is the last in-range slot (0x6DF20F mov eax,[ebx+eax*4+0x808])");
    bridge.SetPlayerVar('V', 0, 1, PasValue.FromInt(8));
    Equal(8, bridge.GetPlayerVar('V', 0, 1).AsInt(), "group 0 index 1 is the first slot");
}

// =====================================================================================
// Source-level pins: the fixes must not be silently reverted.
// =====================================================================================
var root = FindRepositoryRoot();
var bridgeSource = ReadSource(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasApiBridge.cs");
var hostSource = ReadSource(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasScriptHost.cs");
var taskSource = ReadSource(root, "GameSvr", "Players", "PlayerTask.cs");
var gotoSource = ReadSource(root, "GameSvr", "Npcs", "NormNpc.GotoLable.cs");

Require(bridgeSource, "if (CountBagItem(itemName, filter) < count) return false;",
    "the all-or-nothing pre-count gate (0x6DF854..0x6DF862) was removed");
Require(bridgeSource, "item.Dura = (ushort)(item.Dura - need);",
    "the stack decrement (0x6DF96D sub word[+0x26],di) was removed");
Reject(bridgeSource, "TakeItemsEx(itemName, count, iParam != 0)",
    "takeexpand's tri-state lock filter was collapsed back to a bool");
Reject(bridgeSource, "while (TakeItems(args[0].AsString(), 1)) { }",
    "delallthisitem was reverted to an O(n^2) Take loop");
Require(bridgeSource, "private const int NativeScriptVarMiss = -1;",
    "the -1 V/S miss sentinel (0x6DF1BB / 0x6DF1F1 / 0x6E427A) was removed");
Require(bridgeSource, "if (!NativeScriptVarArgsAccepted(type, group, index)) return false;",
    "the SetV/SetS arg<=0 reject (0x6DF2B3 / 0x6DF251) was removed");
// AddTaskToUIList must send mode 2 (0x6E1304 `push 2`), the same mode as UpdateTaskDetail
// (sub_6E131C @0x6E133C). Slice the method body so an identical line in a NEIGHBOURING
// task API cannot satisfy the pin.
var addTaskBody = Slice(taskSource,
    "public void AddTaskToUIList(int taskId, int showUiFlag)",
    "public void UpdateTaskDetail(");
Assert(addTaskBody.Length > 0, "AddTaskToUIList could not be located");
Require(addTaskBody, "SendTaskPacket(task, showUiFlag, 2);",
    "AddTaskToUIList's packet mode 2 (0x6E1304 push 2) was reverted");
Reject(addTaskBody, "SendTaskPacket(task, showUiFlag, 1);",
    "AddTaskToUIList sends mode 1 again — the native client never receives that from this API");
// The other three modes stay pinned to their own native pushes.
Require(Slice(taskSource, "public void UpdateTaskProgress(", "public void DeleteTaskFromUIList("),
    "SendTaskPacket(task, showUiFlag, 3);",
    "UpdateTaskProgress must stay mode 3 (sub_6E1354 @0x6E1374 push 3)");
Require(Slice(taskSource, "public void DeleteTaskFromUIList(", "private void SendAllTaskDetails("),
    "SendTaskPacket(task, showUiFlag, 100);",
    "DeleteTaskFromUIList must stay mode 100 (sub_6E138C @0x6E138F push 0x64)");
Reject(hostSource, "ready.Sort(", "the native descending-index fire order (0x6B3A8A) was re-sorted");
Require(hostSource, "EvictNpcInteractionBindings(objectId);",
    "the per-relog NPC-binding eviction was removed");
Reject(gotoSource, "playObject.m_NPC = null;",
    "m_NPC is pre-nulled again (native has 3 writes to +0xCD8 and no clears)");

// The takeexpand consume path must never reference the equipment array.
var takeRegion = Slice(bridgeSource,
    "/// <summary>Native filter values for the 3rd argument",
    "// =====================================================================\r\n        // INI FILE OPERATIONS");
if (takeRegion.Length == 0)
    takeRegion = Slice(bridgeSource, "private const int NativeTakeFilterAll",
        "private int DelAllThisItem");
Assert(takeRegion.Length > 0, "the take helper region could not be located");
Reject(takeRegion, "m_UseItems",
    "the take/count helpers reference equipment — sub_6DF7E8 walks [player+0x508] only");
Reject(takeRegion, "RecalcAbilitys",
    "the take helpers recompute equipment stats, which native never does here");

Console.WriteLine(
    "PASS PasTakeItemLoss short-take=no-mutation stack=count-by-Dura+decrement " +
    "takeexpand=tri-state-lock+never-equipment delallthisitem=own-sweep " +
    "V/S=-1-miss+arg<=0-reject task-add=mode2 order=descending-index");
return;

// ------------------------------------------------------------------------------------

bool Take(string name, int count) =>
    (bool)takeItems.Invoke(bridge, new object[] { name, count })!;

bool TakeEx(string name, int count, int filter) =>
    (bool)takeItemsEx.Invoke(bridge, new object[] { name, count, filter })!;

int Count(string name, int filter) =>
    (int)countBag.Invoke(bridge, new object[] { name, filter })!;

MethodInfo Method(string name)
{
    var method = typeof(PasApiBridge).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, $"PasApiBridge.{name} is missing");
    return method;
}

static GoodItem Def(string name) => M2Share.UserEngine.GetStdItem(name);

static TPlayObject NewPlayer() => new()
{
    m_boOffLineFlag = true,
    m_sCharName = "audit-take",
    m_sMapName = "audit-map",
    m_nCurrX = 10,
    m_nCurrY = 20
};

static TUserItem NewItem(ushort index, int makeIndex) => new()
{
    MakeIndex = makeIndex,
    wIndex = index,
    Dura = 100,
    DuraMax = 100,
    btValue = new byte[14]
};

static TUserItem NewStack(ushort index, int makeIndex, ushort dura) => new()
{
    MakeIndex = makeIndex,
    wIndex = index,
    Dura = dura,
    DuraMax = 5000,
    btValue = new byte[14]
};

// (MakeIndex, wIndex, Dura) per slot — enough to prove "nothing changed".
static (int, int, int)[] Snapshot(TPlayObject player) => player.m_ItemList
    .Select(item => (item.MakeIndex, (int)item.wIndex, (int)item.Dura))
    .ToArray();

static void AssertUnchanged((int, int, int)[] before, TPlayObject player,
    string message)
{
    var after = Snapshot(player);
    Assert(before.Length == after.Length && before.SequenceEqual(after),
        message + $" (before={Describe(before)} after={Describe(after)})");
}

static string Describe((int, int, int)[] slots) =>
    "[" + string.Join(" ", slots.Select(s => $"{s.Item1}/{s.Item2}:{s.Item3}")) + "]";

static void AssertWorn(TPlayObject player, TUserItem worn, string message)
{
    var slot = player.m_UseItems[Grobal2.U_WEAPON];
    Assert(slot != null && ReferenceEquals(slot, worn) && worn.wIndex != 0,
        message + $" (slot={(slot == null ? "null" : "present")} wIndex={worn.wIndex})");
}

static void SetDefinitions(params GoodItem[] definitions)
{
    M2Share.UserEngine.StdItemList.Clear();
    foreach (var definition in definitions)
        M2Share.UserEngine.StdItemList.Add(definition);
}

static string ReadSource(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);

static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), message);

static string Slice(string source, string start, string end)
{
    var from = source.IndexOf(start, StringComparison.Ordinal);
    if (from < 0) return string.Empty;
    var to = source.IndexOf(end, from, StringComparison.Ordinal);
    return to < 0 ? source[from..] : source[from..to];
}

static void Equal(int expected, int actual, string message = null)
{
    Assert(expected == actual,
        (message ?? "value mismatch") + $": expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    foreach (var origin in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(origin);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("repository root was not found");
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
