// PasGroupSetVCompatCheck —— 战神 `GroupSetV` 脚本 API 的运行期契约.
//
// 注册点 0x7318AF  BA 30 08 6E 00  mov edx,0x6E0830   ; handler
//                  B9 98 2A 73 00  mov ecx,0x732A98   ; name blob "GroupSetV"
// 声明文本 0x72DA45 "function GroupSetV(const nTaskNo, nFieldNo, nValue: integer): Boolean;"
//
// handler sub_6E0830 -> TGroup sub_727754 -> 每个成员 SetV sub_6DF288 -> upsert sub_6E4140.
// 逐字节读出来的四条契约,本文件逐条钉住:
//
//   ① 无队 = 什么都不写 + 返回 False
//        6E0835  33 DB                 xor ebx,ebx           ; result := False
//        6E0837  8B B0 80 0A 00 00     mov esi,[eax+0xA80]   ; caller 的 TGroup
//        6E083D  85 F6 / 6E083F 74 0D  test esi,esi / je     ; 直接跳到出口
//      —— 原生没有"退化成对自己 SetV"这条路。C# 曾有一个 solo fallback,是 INVENTED。
//
//   ② 有队 = 恒返回 True(与实际写了几个人无关)
//        727765  C6 45 F3 01           mov byte [ebp-0xD],1  ; 预置 True
//        7277A9  8A 45 F3              mov al,[ebp-0xD]      ; 中途从不清零
//
//   ③ ghost 成员跳过
//        72777E  8B 40 10              mov eax,[eax+0x10]    ; slot -> player
//        72778C  85 C0 / 72778E 74 15  test eax,eax / je     ; 空槽跳过
//        727790  80 78 73 00 / 75 0F   cmp [eax+0x73],0/jne  ; ghost 跳过
//
//   ④ 写 0 是原样存 0,不是删除
//        7277A0  E8 E3 7A FB FF        call 0x6DF288 (SetV)
//        6DF2CC  89 45 FC              mov [ebp-4],eax       ; [ebp-8]=Key [ebp-4]=Value
//        6DF2DA  E8 61 4E 00 00        call 0x6E4140         ; 8 字节 {Key,Value} 升序 upsert
//      sub_6E4140 全函数对「值」只有存储、没有任何 test/cmp:命中键改值
//      (6E41C2 mov [eax+ebx*8+4],edx)、未命中插入 (6E422A/6E4231、6E425A/6E4260)、
//      空数组首插 (6E4182/6E4187)。三条路径一个零值分支都没有。
//      —— 本工具此前断言「写 0 会移除该键」,那正是 §4.19(QST-30) 记录的 C# 自造擦除,
//         不是原生契约。断言已按字节改正,不是放宽。

using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

var leader = NewPlayer("leader");
var member = NewPlayer("member");
var outsider = NewPlayer("outsider");
leader.m_GroupOwner = leader;
leader.m_GroupMembers.Add(leader);
leader.m_GroupMembers.Add(member);
member.m_GroupOwner = leader;

var bridge = new PasApiBridge { CurrentPlayer = member };

Assert(bridge.CallPlayerMethod("GroupSetV", Values(22, 3, 50)),
    "GroupSetV player-method route was not dispatched");
AssertValue(leader, 22003, 50, "player-method leader");
AssertValue(member, 22003, 50, "player-method member");
AssertMissing(outsider, 22003, "player-method outsider");

// ④ 零值:原样落成 {22003, 0},键必须还在。
Assert(bridge.CallPlayerFunc("GroupSetV", Values(22, 3, 0), out var functionResult),
    "GroupSetV player-function route was not dispatched");
Assert(functionResult.AsBool(), "GroupSetV player-function returned False");
AssertValue(leader, 22003, 0, "player-function leader zero write (6E4140 has no value test)");
AssertValue(member, 22003, 0, "player-function member zero write (6E4140 has no value test)");
AssertMissing(outsider, 22003, "player-function outsider");

bridge.CurrentPlayer = leader;
Assert(bridge.CallStandaloneFunction("GroupSetV", Values(26, 4, 1),
        out var standaloneResult),
    "GroupSetV standalone route was not dispatched");
Assert(standaloneResult.AsBool(), "GroupSetV standalone returned False");
AssertValue(leader, 26004, 1, "standalone leader");
AssertValue(member, 26004, 1, "standalone member");
AssertMissing(outsider, 26004, "standalone outsider");

// ③ ghost 成员必须被跳过(727790 cmp byte [eax+0x73],0 / jne)。
member.m_boGhost = true;
Assert(bridge.CallPlayerFunc("GroupSetV", Values(27, 5, 8), out var ghostResult),
    "GroupSetV player-function route was not dispatched for the ghost case");
Assert(ghostResult.AsBool(),
    "a group with a ghost member must still answer True (727765 preset is never cleared)");
AssertValue(leader, 27005, 8, "ghost-gate leader still written");
AssertMissing(member, 27005,
    "ghost member was written; native skips it at 727790 cmp byte [eax+0x73],0 / jne");
member.m_boGhost = false;

// ① 无队:一个字节都不许写,并且必须返回 False。
var solo = NewPlayer("solo");
bridge.CurrentPlayer = solo;
Assert(bridge.CallPlayerFunc("GroupSetV", Values(10, 2, 7), out var soloResult),
    "solo GroupSetV player-function route was not dispatched");
Assert(!soloResult.AsBool(),
    "an ungrouped caller must answer False (6E0835 xor ebx,ebx / 6E083F je 0x6E084E)");
AssertMissing(solo, 10002,
    "an ungrouped caller wrote a V variable; native sub_6E0830 returns at 6E083F "
    + "without ever reaching the per-member SetV — there is no solo fallback");

const string interpreterSource = """
    program GroupSetVProbe;
    procedure GroupWrite;
    begin
      GroupSetV(31, 6, 77);
    end;
    procedure GroupClear;
    begin
      GroupSetV(31, 6, 0);
    end;
    procedure SoloWrite;
    begin
      GroupSetV(32, 7, 9);
    end;
    begin
    end.
    """;
var interpreterProgram = new PasParser(new PasLexer(interpreterSource)).Parse();
var interpreter = new PasInterpreter(interpreterProgram, bridge);

bridge.CurrentPlayer = member;
interpreter.ExecuteProcedure("GroupWrite");
AssertValue(leader, 31006, 77, "interpreter group leader");
AssertValue(member, 31006, 77, "interpreter group member");
AssertMissing(outsider, 31006, "interpreter group outsider");
interpreter.ExecuteProcedure("GroupClear");
AssertValue(leader, 31006, 0, "interpreter group leader zero write");
AssertValue(member, 31006, 0, "interpreter group member zero write");

bridge.CurrentPlayer = solo;
interpreter.ExecuteProcedure("SoloWrite");
AssertMissing(solo, 32007, "interpreter solo caller wrote without a group");
AssertMissing(outsider, 32007, "interpreter solo outsider");

Console.WriteLine("PASS GroupSetV routes=method/function/bridge-standalone/interpreter "
    + "group=leader+member zero=stored-not-removed ghost=skipped ungrouped=false-no-write");
return;

static TPlayObject NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_sMapName = "audit-map",
    m_nCurrX = 12,
    m_nCurrY = 34
};

static List<PasValue> Values(params int[] values) => values
    .Select(PasValue.FromInt)
    .ToList();

static void AssertValue(TPlayObject player, int key, int expected, string operation)
{
    Assert(player.m_ScriptVVars.TryGetValue(key, out var actual),
        operation + " did not write the V variable");
    Assert(actual == expected,
        $"{operation} wrote {actual}, expected {expected}");
}

static void AssertMissing(TPlayObject player, int key, string operation) =>
    Assert(!player.m_ScriptVVars.ContainsKey(key),
        operation + " unexpectedly changed the V variable");

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
