# 审计工具分诊 B（atriageB） — 2026-08-14

分支 `w/atriageB`（自 master `69f049b6`）。队列 21 个 FAIL/NOEXE 工具，逐个定性。

跑法：`tools/run_audittools.ps1`（本轮新增，见 §3）。终态两连跑稳定：

```
PASS = 20   SKIP = 1（NativeHonorDbCheck，需活库）   FAIL = 0
```

四分法计数（按工具主因归类）：**(a) 陈旧测试 12 / (b) 真回归 1 / (c) 环境·harness 8 / (d) 真臆造 0**。
另有 **1 条 (d) 臆造物**（`MagicTowerArcherMonster`）与 **1 条真回归**（宝石掉落必崩）
是在别的工具下面挖出来的，见 §2。

---

## 1. 逐工具

### 1.1 M2NativeDbServiceLoopbackCheck — (c) fixture — 已修复

报错：`System.InvalidOperationException: UserEngine native monster MonItems attachment`
（Program.cs:1123 ← VerifyMonsterProductionPublication:757）

根因：MonItems 行只有在物品名能在标准物品表里查到时才保留。战神 `sub_6799E0`：

```
679BB4  E8 1B 27 0D 00        call 0x74C2D4          ; 按名字查 StdItem
679BB9  89 45 D4              mov [ebp-0x2C],eax
679BC1  83 7D D4 00 / 74 16   cmp [ebp-0x2C],0 / je 0x679BDD   ; nil -> 不分配记录
679BDD  83 7D F8 00 / 74 6B   cmp [ebp-8],0 / je 0x679C4E      ; 未分配 -> 整行跳过
```

`LocalDB.LoadMonitems` 里的 `ResolvesToStdItemName` 就是这一门（baseline 就有，不是新改动）。
fixture 用的是全新 `UserEngine`，标准物品表是空的，所以它写的
`"1/1  LoopbackDrop  2"` 这一行被**合法**丢弃。

处置：fixture 里补 `StdItemList.Add(new GoodItem { Name = "LoopbackDrop" })`。断言未动。

### 1.2 Magic167AuditCheck — (a) — 已修复

报错：`FAIL: constant NativeSkill167CellEventType was not found (0x7198E4 push 0x1D)`

根因：`ConstantIs` 的正则只认字面量右值；`c7600445` 把常量改成了
`private const int NativeSkill167CellEventType = Grobal2.ET_PRISON;`。

字节：`0x7198E4: 6A 1D  push 0x1d`，而 `Grobal2.ET_PRISON = 0x1D`。代码是对的。

处置：`ConstantIs` 支持 `Grobal2.<NAME>` 右值，顺符号追进 `SystemModule/Grobal2.cs`
再比数值 —— 断言强度不变（仍然是"这个常量的数值必须等于 0x1D"）。

### 1.3 MarryClusterCompatCheck — (a) ×2 — 已修复

报错 1：`Other-GS divorce dispatcher is missing`
根因：正则 `case Grobal2\.ISM_DIVORCE:\s*MsgGetDivorce\(...\)` 不容忍中间的注释，
而 `MirrorMessage.cs:150-156` 在 case 标签和调用之间放了 7 行 0x6579D8 的证据注释。
处置：改成 `(?:\s|//[^\r\n]*)*` —— 只放行空白与 `//` 注释，任何真语句仍然判失败。

报错 2（修完 1 才暴露）：`Other-GS divorce receiver adds non-native handling: GetValidStr`
根因：受理体切片 `Slice(mirrorMessage, "private void MsgGetDivorce",
"private void MsgGetReloadMakeItemList")` 的终点早已不是下一个方法 ——
中间插进了 `MsgGetMentorStudentLeft` / `MsgGetMentorExpel` 等，它们合法地用了
`HUtil32.GetValidStr3`，被反向锁误伤。
处置：终点改成真正的下一个方法 `MsgGetMentorStudentLeft`（切片变**更**窄）。

### 1.4 MovementReliveCheck — (a) — 已修复（附字节）

报错：`SetNoKillMapLv GM permission contract changed`（工具要 `"等级", 10)]`，
代码是 `"等级", 5)]`）

字节：战神 GM 命令表记录 stride `0x120`，布局 = 名 ShortString[23] @+0x00、
id dword @+0x18、**权限 dword @+0x1C**、帮助串 ShortString @+0x20。

```
007CD254  0E 53 65 74 4E 6F 4B 69 6C 6C 4D 61 70 4C 76   len=14 "SetNoKillMapLv"
007CD26C  88 01 00 00                                     id   = 392
007CD270  05 00 00 00                                     权限 = 5
```

+0x1C 是权限位这一点用对照组坐实（每条都与自己的 `[GameCommand]` 第四个实参吻合）：

| 命令 | 记录 VA | +0x1C | C# 属性 |
|---|---|---|---|
| AttackMode | 0x7B6274 | 0 | 0 |
| Rest | 0x7B6394 | 0 | 0 |
| addGuildMem | 0x7B9AB4 | 3 | 3 |
| AddHeroExp | 0x7C46D4 | 4 | 4 |
| GetUserItem | 0x7BC8D4 | 4 | 4 |
| **SetNoKillMapLv** | **0x7CD254** | **5** | **5** |

处置：改测试 10 → 5，PASS 串 `gm=permission10` → `gm=permission5`。

### 1.5 NativeAccountStorageCompatCheck — (c) — 已修复（harness）

报错：`FAIL native operation source contract: run this audit from the repository root`
根因：sweep 把 CWD 设成 exe 自己的 bin 目录，而 bin 在 checkout 之外
（共享 OutputPath 是 `..\..\..\Build\AuditTools\<name>\`），源码扫描找不到仓库根。
处置：harness 改用仓库根做 CWD（§3）。工具本身一行没动，直接 PASS。

### 1.6 NativeDropControlRuntimeCheck — (a) — 已修复（附字节）

报错：`native fixed scatter range: expected=4, actual=3`

字节：`sub_71FA20` 段 3 的散落调用

```
71FF32  6A 01 / 6A 00 / 6A 00 / 68 34 01 72 00
71FF3D  B9 03 00 00 00        mov ecx,3        ; DropItemDown 的 ItemRange
71FF42  8B 55 D8 / 8B 45 F4   mov edx,item / mov eax,self
71FF48  E8 53 89 04 00        call 0x7688A0
```

ECX 是第三个寄存器参数，被 `0x7688B4 8B D9 mov ebx,ecx` 收下。`DROP-33`(f3354457)
按此把 `ScatterRange` 4→3 改对了，测试停在旧值。

处置：改测试 4 → 3，PASS 串同步。

### 1.7 NativeDropRngSequenceCheck — (b) — 已修复 ★ 见 §2

报错：NOEXE（编译不过）。修完编译又暴露出 GameSvr 里一个必崩的真缺陷。

### 1.8 NativeFixedCoordStoneCheck — (a) — 已修复（附字节）

报错：`replay X (0x6B23F0 movzx [esi+0x1908]): expected=845, actual=0`

根因：SM 3420 走两跳，字段在第二跳被**重排**过。入队（0x6B23EC-0x6B2412）只决定
X/Y 落在记录的哪个槽；下线前的 RM 处理器才决定线序：

```
6B6036  66 8B 43 02   mov ax,[rec+2]      -> Param  = wParam  = 0
6B603C  8B 43 08      mov eax,[rec+8]     -> Tag    = nParam2 = X
6B6040  66 8B 43 0C   mov ax,[rec+0xC]    -> Series = nParam3 = Y
6B6051  66 BA 5C 0D   mov dx,0xD5C        -> ident 3420
```

代码 `SendDefMessage(SM_FIXEDCOORD, 0, 0, X, Y, map)`（签名
`(wIdent, nRecog, nParam, nTag, nSeries, sMsg)`）是对的；测试停在 `Param=X, Tag=Y`。

处置：改测试为 `Param=0 / Tag=X / Series=Y`，顺手补上原来没有的 Series 断言。

### 1.9 NativeFloorItemOwnerExpiryCheck — (a) — 已修复

报错：`GameSvr/Players/TPlayObject.Base.cs: 0x7839C1 cmp byte [owner+0x73],0 => 归属作废判据是 m_boGhost`

根因：扫描窗口是"从 `MapItem.CanPickUpTick` 起 1600 字符"。该段后来插进了 20 行
0x783988 的证据注释（`TPlayObject.Base.cs:1852-1871`），`.m_boGhost`（1874/1881 行）
被挤出窗口。代码本身完全正确（RobotPlay 那份缩进浅，同样的窗口够得到，所以只红一半）。

处置：窗口终点改成结构边界（下一个 `CellType.OS_EVENTOBJECT`），
反向锁 `!window.Contains(".m_boDeath")` 仍然有效。

### 1.10 NativeGetUserItemCompatCheck — (a) ×2 — 已修复（附字节）

报错 1：`permission 3 result: expected=权限不够!!!, actual=该命令需要4级GM才能使用`

字节：整个镜像里 `"权限不够"` **0 命中**。真正的拒绝串是两段拼的：

```
0x62B768  len=10  B8 C3 C3 FC C1 EE D0 E8 D2 AA           "该命令需要"
0x62B77C  len=12  BC B6 47 4D B2 C5 C4 DC CA B9 D3 C3     "级GM才能使用"
```

`BaseCommond.cs:47` 已按 `"该命令需要" + N + "级GM才能使用"` 拼，
`M2Share.g_sGameCommandPermissionTooLow` 是个幽灵串。→ 改测试。

报错 2：`invalid gate frame length 5`
根因：工具把 `word[header+0x0C]` 当帧长读。16 字节传输头里 +0x0C 是 **Cmd**：

```
637AC1  C7 00 77 BB AA 33     mov [eax],0x33AABB77   ; magic  @+0x00
637AC7  66 89 78 0C           mov [eax+0xC],di       ; Cmd    @+0x0C
637ACE  89 50 04              mov [eax+4],edx        ; ConnID @+0x04
637AD4  89 50 08              mov [eax+8],edx        ; SeqID  @+0x08
637AD7  66 89 58 0E           mov [eax+0xE],bx       ; BodyLen@+0x0E
637ADE  83 80 84 01 00 00 10  add [eax+0x184],0x10   ; 头长 16
```

总长 = `0x10 + word[+0x0E]`。读出来的 5 是 Cmd。→ 改测试。

### 1.11 NativeGildExitViceWiringCompatCheck — (c) fixture — 已修复

报错：`4583 exit success code: expected=0, actual=38`

根因：4583 的第一道门是安全区探针（`NativeGildExitTransaction.Evaluate`:
`if (!c.CanLeave) return NotAllowed(38)`），`HandleNativeGildExit` 传的是
`InSafeZone()`。fixture 造的 `TPlayObject` 根本没有 `m_PEnvir`，
`InSafeZone()` 恒 false → 永远只能拿到 38，后面的成功阶梯从来没被执行过。

处置：给 fixture 一张 `Flag.boSAFE = true` 的地图（生产里离会的人站在城里）。
三道分区拒绝本来就由 `ExitZoneGatesRejectViaService` 直接钉 `service.ApplyGildExit`，
覆盖没丢。

> ⚠ 顺带发现的保真度偏差（未改，交主代理）：`TPlayObject.NativeGuildRelationTailProtocol.cs:508`
> 把这道门标注成 `sub_76858C`，但 `MFLG-17`(c4e08029) 已经证明 C# 的 `InSafeZone()`
> 移植的是 `sub_7684DC`（range 取 `nSafeZoneSize`，且带 RedHome 臂），
> 而 `sub_76858C` 是 0 参兄弟、range 硬编码 12（`0x7685BE 6A 0C`）、无 RedHome 臂。
> 两者在 `nSafeZoneSize != 12` 或站在 RedHome 附近时结果不同。

### 1.12 NativeHonorDbCheck — (c) — 仍 SKIP（设计如此）

报错：`SKIP NativeHonorDbCheck: no MySQL connection string given.`（exit 2）

这个工具**已经**是优雅跳过：exit 2 + 明确 SKIP 输出 + 一句"本次运行不能证明任何事"。
它刻意不用 exit 0，避免在汇总里冒充绿灯 —— 这个判断是对的，不该改成 0。

处置：修 **harness** 而不是工具。新的 `tools/run_audittools.ps1` 把
"exit 2 且输出里announce 了 SKIP/INCOMPLETE" 归为 `SKIP`，与 `PASS` 分列。
要真跑就传连接串：`-ToolArgs @{ NativeHonorDbCheck = @('server=...;uid=...;pwd=...') }`。

### 1.13 NativeItemUseCheck — (a) — 已修复

报错：`unimplemented native special class was consumed`

根因：这条"不可实现的特殊类不得被消耗"的探针用的是 StdMode 1 / Shape 1，
而工厂把它映射到 `TDoubleExpProp`，那个类早已按 `sub_786390`
（VMT 0x77F288 槽 +0x18）移植落地（`TPlayObject.Operate.cs:1136`），使用后当然消耗。

处置：探针改用 Shape 20（`THappyCake`）—— 工厂映射到它，但 item-use switch 没有对应臂，
落 `default: return false`，验的仍是同一条契约。
（曾想再补一条"TDoubleExpProp 必须被消耗"的正向断言，但 `wIndex` 是
`StdItemList` 的 1-based 下标，中途插一个 std item 会把后面所有用例的下标顶掉，
遂放弃，未留半成品。）

### 1.14 NativeLevelExpTableCheck — (c) — 已修复（**不是**构造函数缺陷）

报错：`System.NullReferenceException at GameSvr.TBaseObject..ctor() (TBaseObject.cs:907)`
（退出码 `-1073741819`）

**先查了是不是线上 bug —— 不是。** `TBaseObject.cs:907` 是
`M2Share.ObjectManager.RegisterConstructed(this);`，无空检查。但生产里
`GameApp.cs:564 M2Share.ObjectManager = new ObjectManager();` 排在
`UserEngine`(584)、`MapManager`(570)、`PasEngine.LoadNpcScriptMap()`(608) **之前**，
而这些才是第一批造 `TBaseObject` 的地方；`LoadConfig()`(503) 之前不造对象。
所以生产路径上这个字段恒非空，构造函数无缺陷。
（`ObjectManager == null` 的防御性判空散见于 PasScriptHost / NativeDynamicRoom*，
是重载/脚本路径的保护，与构造期无关。）

处置：harness 里补 `M2Share.ObjectManager ??= new ObjectManager();`
（与 `YanshenMonsterAttrCheck` 同解），另把 `FindRepoRoot` 从只走
`AppContext.BaseDirectory` 改成也走工作目录（共享 Build 目录在 checkout 之外）。

### 1.15 NativeLogonStateSyncCheck — (a) — 已修复

报错：`BuildNativeTimedAbilitySnapshot missing`

根因：`3c43b685` 把 w/m-sm-c 与 master 各自实现的两份 SM 3554 构造合并成一份，
留下 master 的 `BuildTimedAbilityListState`（同 ident 0xDE2、同样遍历 `[self+0xDC]`、
同样 10 字节记录，且已被 `NativeTimedAbilityListCheck` 钉住）。工具还在反射旧名。

处置：源码断言与反射双双改名，其余 3554 帧结构断言原样保留。

### 1.16 NativeMagicMidStatesCompatCheck — (c) — 已修复（harness）

报错：`System.IO.DirectoryNotFoundException: GameSvr/GameSvr.csproj`（FindRepositoryRoot）
同 §1.5：CWD 在 checkout 之外。harness 改仓库根后直接 PASS，工具未动。

### 1.17 NativeMagicTowerEngageArcherCheck — (a) + (d) — 工具已修复，(d) 交主代理

报错：`Race99 did not use MagicTowerArcherMonster`

根因：那条 switch 臂是**死代码**。`UsrEngn.AddBaseObject` 的分发顺序是
`2839 TryCreateRaceA(...)` → `2840 if (Cert == null) TryCreateRaceBase` →
`2841 ... TryCreateRaceHigh` → `2843 if (Cert == null) switch {...}`。
race 99 在第一步就被 `SkyArcher` 认领，`UsrEngn.cs:2950` 的
`case TPlayObject.NativeMagicTowerArcherRace:` 永远走不到。

字节（race 99 = TSkyArcher）：工厂 `sub_679F8C` 的 `jt[28] = 0x67A63F`
取 classref `0x67F21C` 调 ctor `0x681958`：

```
681971  E8 B2 BE 09 00              call 0x71D828            ; TAnimal.Create
681976  C6 86 78 01 00 00 63        mov byte [esi+0x178],0x63 ; race = 99
68197D  C7 46 78 07 00 00 00        mov dword [esi+0x78],7    ; view range = 7
681984  C6 86 AC 03 00 00 01        mov byte [esi+0x3AC],1
6819B0..6819C5                      IsAttackTarget: race>=0x32 且 !=0x63
```

处置：工具改钉 `SkyArcher`。原来那条 `Assert(actor.m_boWantRefMsg, "Race99 +0x3AC")`
换成 `IsAttackTarget` 三点（0x6819BA `jb` race<50 / 0x6819B8 race>=50 /
0x6819BE `jne` race==99 排除）—— 因为 `+0x3AC` 到底是不是 `m_boWantRefMsg`
**没有字节证据**，`SkyArcher.cs:35` 与 `ShadowHero.cs:34` 都已把它登记为不可证；
换成一条同样由字节支撑的断言，强度不降。

> 🔴 **交主代理（(d) 臆造物）**：`GameSvr/Monsters/Monster/MagicTowerArcherMonster.cs`
> 通篇只有 `m_nViewRange = 7; m_boWantRefMsg = true;`，零字节注释，是 TSkyArcher 的
> 无据重复实现，且已不可达。删它需要同时删 `UsrEngn.cs:2950-2952` 那条 case ——
> UsrEngn.cs 是禁改热点文件，本代理未动。

### 1.18 NativeProperTargetGateCheck — (c) — 已修复（**只在干净树上第一次跑会绿**）

报错：`TypeInitializationException ... 配置文件[...\bin\Debug\Share\PlayerUpgradeExp.ini]不存在或配置文件内容为空`

根因（这条最阴）：它的 `PrepareConfig()` 只写 `!Setup.txt / String.ini / Command.conf`，
不写 M2Share 静态构造还要的 `..\Share\PlayerUpgradeExp.ini`。而 `IniFile.Load()`
在文件**不存在**时会 `Directory.CreateDirectory` + `File.Create(...).Close()` 然后
直接 return（IniFile.cs:203-206，**不抛**）；文件**存在但为空**时才走到
`IniFile.cs:281 ConfigCount <= 0 -> throw`。
所以：干净树上第一次跑 PASS（顺手留下两个 0 字节 ini），从第二次起永远 FAIL。
我第一轮跑到的那个 PASS 就是这么来的 —— 差点误判成 (c) 已解决。

处置：`PrepareConfig()` 按其他 in-process 审计的通用写法补上
`Share/PlayerUpgradeExp.ini` 与 `Share/ServerData.ini`。已验证连跑两次都绿。

### 1.19 NativeSkill153ShieldCompatCheck — (c) — 已修复（harness）

同 §1.5 / §1.16。harness 改仓库根后 PASS，工具未动。

### 1.20 NativeSocialProtocolRouterCheck — (a) — 已修复

报错：`base fallback must follow a failed social route`

根因：`Operate` 的 default 臂把 28 个 CM 路由串成了一条 `&&` 链
（`TPlayObject.Message.cs:3121-3151`），社交路由不再是 `if (!X)` 本体，而是
`&& !TryHandleNativeSocialProtocol(ProcessMsg)` 中的一项。

处置：改为断言"它以 `!` 取反出现在守卫里，且 `result = base.Operate(ProcessMsg);`
排在其后"，另加一条"default 臂必须是单一 `if (!` 守卫"补回结构约束。
（只改了工具；`TPlayObject.Message.cs` 是禁改热点文件，未动。）

### 1.21 NativeSpellApplyCompatCheck — (a) — 已修复（附字节）

报错：`AoE range slot is 1 and arg0 is true (@0x76F270 / @0x76F27A)`

根因：`QueueNativeAreaBlast` 的半径槽被眼神技能范围 trampoline 包了一层
（`byte range = YanshenSkillPatches.RangeByte(PlayObject, magicId, 1);`），
旧字面量 `nTargetX, nTargetY, 1, true, 0,` 不再成立。

字节（`sub_76F21C` 段）：

```
76F26B  68 58 02 00 00   push 0x258   ; 600 ms 延迟
76F270  6A 01            push 1       ; range
76F272  6A 03            push 3       ; dispatchCategory
76F27A  6A 01            push 1       ; arg0 = true
```

`RangeByte` 在开关关闭时原样返回 `nativeDefault`，所以只要 nativeDefault 是 1
就与原生一致。

处置：拆成两条 —— `RangeByte(PlayObject, magicId, 1)`（钉住 ys 默认值 = 原生 1）
与 `nTargetX, nTargetY, range, true, 0,`（钉住原样传递）。比原断言更强。

---

## 2. 发现的真回归（最重要）

### R1 🔴 宝石掉落必崩：`NativeJewelStoneTable.Apply` 越界写

引入者：`d78d42c4 Run helmet/ring Shape 130 +0x08 and write the jewel 9-byte row on drop.`

改动前的代码：

```csharp
public const int NativeRecordSize = 208;
public const int ItemPlus100RecordOffset = 0xE0; // item+0x100
...
if (item.NativeRecord == null || item.NativeRecord.Length != NativeRecordSize)
    item.NativeRecord = new byte[NativeRecordSize];          // 长度恒为 208
Buffer.BlockCopy(rec, 0, item.NativeRecord, 0x16, 6);
item.NativeRecord[ItemPlus100RecordOffset] = rec[6];          // 索引 224 >= 208 -> 必抛
```

`0xE0 = 224`，数组长度 208 —— **每一次**宝石掉落（`StdMode 79` 且
`WordParam1 ∈ 1..4`）都抛 `IndexOutOfRangeException`。
调用链是活的：`UsrEngn.MonGetRandomItems → NativeItemPlus28.ApplyOnDrop →
ApplyJewelStone → NativeJewelStoneTable.Apply`。

字节根据 —— 持久化的物品记录只有 `item+0x20 .. item+0xEF` 共 208 字节：

```
LOAD sub_74DAE4:  74DB3A  8D 7B 20         lea edi,[ebx+0x20]
                  74DB3D  B9 34 00 00 00   mov ecx,0x34      ; 52 dword = 208 B
                  74DB42  F3 A5            rep movsd
SAVE           :  6B170F  8D 70 20         lea esi,[eax+0x20]
                  6B1712  B9 34 00 00 00   mov ecx,0x34
                  6B1717  F3 A5            rep movsd
```

而写入点在记录窗口之外 16 字节：

```
sub_78C5EC:  78C643  88 86 00 01 00 00   mov byte [esi+0x100],al
             78C64C  88 86 01 01 00 00   mov byte [esi+0x101],al
             78C655  88 86 02 01 00 00   mov byte [esi+0x102],al
```

所以 `NativeRecord[0xE0]` 既越界、语义也错：这三个字节是**运行期**镶嵌属性，
本来就不该进持久化镜像（`TPlayObject.Inlay.cs:40` 与 `TBaseObject.Base.cs:1191`
都已独立记录过 `item+0x100..0x102` 是运行期字段，后者还引了消费者
`sub_78BCBC: mov eax,0x64 / call Random / cmp eax,[ebx+0x100] / setl al`）。

修复：`TUserItem` 上加三个 `[ProtoIgnore]` 运行期字节
（`NativeItemPlus100/101/102`，跟既有的 `NativeGiftItem` / `NativeMapDropAllowed`
同一套路，不入任何序列化），拷贝构造同步；`Apply` 写它们而不是 `NativeRecord`。
`NativeDropRngSequenceCheck` 的三条断言随之改指新字段（附上述字节）。

### R2 🟠 `NativeDropRngSequenceCheck` 自 `d73aa2e2` 起就编译不过（NOEXE）

`d73aa2e2 Clear the four AuditTools BUILD-ERRORs the merges introduced` 把三张掉落表
从 `static readonly int[]` 改成本地函数，改了第 35-41 行的调用点，**漏了第 109 行**
把 `HelmetUnknown08` 当方法组传给 `Concat(params int[][])`
（CS1503 + CS8422）。修：补 `()`。

修完编译后又发现它连 `PrepareRuntimeConfig` 都没有（M2Share 静态构造直接炸），
以及第 130 行的 `Random(MonItem.MaxPoint * penalty)` 字面量已被眼神装备爆率
trampoline 包掉。三处都修完后，它才第一次真正跑起来 —— 也正是它跑起来才炸出 R1。

### R3 🔴（(d) 臆造物，未修，交主代理）`MagicTowerArcherMonster`

见 §1.17。无字节依据、与 `SkyArcher` 重复、已不可达。删除需要动
`GameSvr/UsrSystem/UsrEngn.cs:2950-2952`（禁改热点文件）。

### R4 🟡 `NativeProperTargetGateCheck` 的"一次性绿灯"

见 §1.18。这不是产品缺陷，但它制造的正是任务里最忌讳的那种假绿：
在干净 CI 上第一次跑永远 PASS，本地重跑必红。已修。

---

## 3. harness 改动：`tools/run_audittools.ps1`（新增，已入库）

旧 sweep 脚本（`D:\loym2\.claude\wt2\audit3\_run_audittools.ps1`，未入库）有两处
系统性制造红灯的问题：

1. **工作目录**：用 exe 自己的 bin 目录当 CWD。共享 Debug OutputPath 是
   `..\..\..\Build\AuditTools\<name>\`，在 checkout 之外，所有源码扫描型审计
   从那里往上走永远找不到 `GameSvr/GameSvr.csproj`，一条断言都没跑就抛异常。
   **本队列 21 个里有 4 个（§1.5 / §1.16 / §1.19 与部分 §1.14）纯靠改 CWD 就绿了。**
2. **退出码归类**：把 exit 2 一律算 FAIL。本树用 exit 2 表示 INCOMPLETE/SKIP
   （`NativeHonorDbCheck` 要活库、`MovementReliveCheck` 要明文客户端根目录），
   算成 FAIL 会把真失败埋掉。

新脚本：CWD = 仓库根；exit 0 = PASS，exit 2 且输出 announce 了 `SKIP`/`INCOMPLETE`
= SKIP（**独立于 PASS 计数，绝不冒充绿灯**），其余 = FAIL；支持
`-ToolArgs @{ 工具名 = @('参数') }` 给需要实参的工具传参（`dotnet run -- <arg>`
在本环境不转发 argv，脚本直接执行 exe）。

---

## 4. 仍 BLOCKED / 交主代理

| 项 | 原因 |
|---|---|
| 删除 `MagicTowerArcherMonster` 及 `UsrEngn.cs:2950-2952` 的 case | 需动禁改热点文件 `GameSvr/UsrSystem/UsrEngn.cs` |
| `NativeHonorDbCheck` 真跑 | 需活 MySQL 连接串；已由 harness 记为 SKIP，不是 FAIL |
| 4583 离会安全区门用错兄弟函数 | `HandleNativeGildExit` 调 `InSafeZone()`(=`sub_7684DC`, range=nSafeZoneSize, 带 RedHome 臂)，而该 handler 的原生门是 `sub_76858C`(0 参, range 硬编码 12 @0x7685BE `6A 0C`, 无 RedHome 臂)。需要单独移植 `sub_76858C`，超出本轮范围 |

## 5. 改动过的文件

产品代码（2）：`GameSvr/Items/NativeItemPlus28.cs`、`SystemModule/Packet/TUserItem.cs` — 只为修 R1。
审计工具（15）+ 新 harness（1）：见各 commit。**禁改热点文件一个没动。**
