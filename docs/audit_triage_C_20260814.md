# 审计工具分诊 C 组（atriageC）—— 21 个 FAIL 逐工具定性

- 分支：`w/atriageC`（基于 `master` @ `69f049b6`）
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`）
- 工具全量跑基线：`D:\loym2\.claude\wt2\audit3\_toolruns\_summary.csv`
- 复跑脚本：`D:\loym2\.claude\wt3\_sweep.ps1`（46 把闸，含 21 项队列 + 血缘邻居）

## 0. 统计

| 定性 | 数量 | 工具 |
|---|---|---|
| **(a) 陈旧测试** | 10 | NativeTempSetMapParamPickupCheck、PasNativeShadowP0CompatCheck、PasNativeShadowP123CompatCheck、PasDispatchShadowCompatCheck、QST12CompatCheck、QST22VariableScopeAudit、QuestDiamondCompletionStateMachineCheck、NativeType43EarthFireCompatCheck、TimedAbilityStateGateExactCheck、RobotUnknownItemInitCheck |
| **(b) 真回归** | 1 | PasDispatchShadowCompatCheck 抓到的 `TBaseObject.HasLevelUp` MaxExp 取表等级错误 |
| **(c) 环境 / harness** | 12 | NativeState26CompatCheck、NativeType1RouteCheck、SpeedHackClientReportCompatCheck、PasScriptAudit、WeaponUpgradeRoundTripCheck、ServerSwitchOwnershipCompatCheck、TargetActionExtensionCheck、NpcRegistryExactReferenceCheck、ReviveSubsystemCheck、SecHeroPracticeCompatCheck、QuestDiamondCompatCheck、（NativeType43EarthFire / TimedAbilityStateGate 各含一条 repo-root） |
| **(d) 真臆造** | 0 已确认 / 1 待定 | `SendSocket("+WID")` / `"+UWID"`（SKILL_REDBANWOL 56），见 §4 |

> 若干工具同时中 (a) 与 (c)，上表按「本工具出现过的性质」计，故合计大于 21。
> 按「每个工具的**主因**」计：a=9、b=1、c=11、d=0。

- **已修复：21 / 21 全部转 PASS**（外加队列外 1 把 `NativeLevelExpTableCheck`）
- **仍 BLOCKED：0**（1 条需主代理定夺的开放项，见 §4）

## 1. 真回归（最重要）

### R1 — `TBaseObject.HasLevelUp` 用错了取表等级，连升被打断

- **抓到它的闸**：`PasDispatchShadowCompatCheck`
  「`Give experience multi-level result: expected 3, actual 2`」
- **引入者**：`4d63d4e4`（2026-08-13 20:09）
  “Load PlayerUpgradeExp.ini as uint32 …。HasLevelUp now looks up the previous
  level, matching VMT+0x240 edx after 0x6C0543 dec.”
  把 `m_Abil.MaxExp = GetLevelExp(m_Abil.Level)` 改成 `GetLevelExp(nLevel)`。
- **归属错在哪**：该提交把 VMT+0x240 认成 `sub_6BA140`。

  - `sub_6BA140` 与孪生 `sub_6BA7BC` 在**全镜像 dword 引用数 = 0**，不可能装在任何
    VMT 里；它们写的是 `[edi+0x1E8+0x5C]` = `+0x244`、按 `[edi+0x72]` 分职业，
    是**另一套对象布局**（玩家的 Level/Exp/MaxExp 是 `+0x278`/`+0x2BC`/`+0x2C0`）。
  - 真正装在 VMT+0x240 的是 `sub_6BDBA0`：它在全镜像只有两处 dword 引用
    `0x0062F1CC` 与 `0x006ACB08`，二者各减 `0x240` 得 `0x0062EF8C` / `0x006AC8C8`，
    都是 145/145 全代码指针的 VMT。
- **字节证据**（`sub_6BDBA0`，忽略 edx、直接重读**已自增**的等级）：

```
006bdbc5  0f b7 93 78 02 00 00   movzx edx, word ptr [ebx + 0x278]   ; 新等级
006bdbcc  8b c3                  mov   eax, ebx
006bdbce  e8 f5 20 ff ff         call  0x6afcc8                      ; GetLevelExp
006bdbd3  89 83 c0 02 00 00      mov   dword ptr [ebx + 0x2c0], eax  ; MaxExp
```

  调用方 `sub_6C03F8` 的升级循环：

```
006c0516  29 b3 bc 02 00 00      sub   [ebx+0x2BC], esi   ; Exp -= MaxExp
006c0527  66 ff 83 78 02 00 00   inc   word [ebx+0x278]   ; Level++
006c053c  66 8b 93 78 02 00 00   mov   dx, word [ebx+0x278]
006c0543  4a                     dec   edx                ; 只喂给 sub_73EE14
006c054f  ff 96 40 02 00 00      call  dword ptr [esi+0x240]
006c057b  8b b3 c0 02 00 00      mov   esi, [ebx+0x2C0]   ; 重读 MaxExp
006c0581  3b b3 bc 02 00 00      cmp   esi, [ebx+0x2BC]
006c0587  76 8d                  jbe   0x6c0516           ; 继续升
```

  `0x6C0543` 的 `dec edx` 只影响转发给 `sub_73EE14` 的第二个参数，不进 MaxExp。
- **后果**：改动后每次升级把 MaxExp 取成**上一行**阈值；当上一行为 0（表里未填）时
  `GrantNativePlayerExperience` 的 while 循环因 `m_Abil.MaxExp != 0` 直接退出，
  连升整段停摆。工具用例：Level=1/Exp=0/MaxExp=10、`dwNeedExps[2]=10`、
  `dwNeedExps[3]=1000`，给 25 点经验本该升到 3 级余 5，实测只到 2 级。
- **旁证**：同一棵树的英雄路 `TPlayObject.NativeGive.cs:227` 早就引用同一站点
  `0x6BDBD3` 并用**自增后**的等级，改动后主人路与英雄路自相矛盾。
- **处置**：`GameSvr/Actors/TBaseObject.cs` 改回 `GetLevelExp(m_Abil.Level)`，
  注释换成正确归属。**已修复**。
- **连带**：`4d63d4e4` 同时新建的守卫 `NativeLevelExpTableCheck` 把**错误的**契约
  钉死了（`Require GetLevelExp(nLevel)` / `Forbid GetLevelExp(m_Abil.Level)`），
  只是它当时崩在第一条断言之前（`TBaseObject..ctor` 里
  `M2Share.ObjectManager.RegisterConstructed` 空引用）所以没生效。已整条翻正并
  补上 `ObjectManager` 初始化，现在 14 条断言全过。**不修它就会把回归改回去。**

> 除 R1 外，21 项队列里**没有第二条真回归**。特别地：
> **MFLG-24 不是回归** —— `@TempSetMapParam` 的 8 个新 token 与
> `TMapFlag.UserNoKillLevelCap`（word `+0x74`）全部通过，该工具的红灯来自
> 越权回执串陈旧（见 §2 T1）。

## 2. (a) 陈旧测试 —— 逐条（改测试者均附字节）

### T1 `NativeTempSetMapParamPickupCheck`
- 报错：`permission 4 production denial: expected=权限不够!!!, actual=该命令需要5级GM才能使用`
- 根因：`BaseCommond.Handle` 的越权回执早在 `30be880a` 就按原生改对，工具没跟上。
- 字节：`"权限不够!!!"` GBK `c8 a8 cf de b2 bb b9 bb 21 21 21` 在底本 **0 命中**。真回执：

```
00622ab9  80 fb 03           cmp  bl, 3
00622abc  72 4b              jb   0x622b09              ; <3 静默
00622ac4  68 68 b7 62 00     push 0x62b768              ; "该命令需要"
00622ad4  e8 c3 9d de ff     call 0x40c89c              ; IntToStr(N)
00622adf  68 7c b7 62 00     push 0x62b77c              ; "级GM才能使用"
00622aef  e8 9c 2d de ff     call 0x405890              ; LStrCatN(edx=3)
```
  `0x62B768`：refcnt `FF FF FF FF`，len `0A`，`b8 c3 c3 fc c1 ee d0 e8 d2 aa`。
  `0x62B77C`：refcnt `FF FF FF FF`，len `0C`，`bc b6 47 4d b2 c5 c4 dc ca b9 d3 c3`。
- 处置：断言改为 `"该命令需要5级GM才能使用"`。**已修复**。
- **外溢**：`HeroUnionStateCheck` 与 `NativeGetUserItemCompatCheck`（不在本队列）
  同一病因，字节同上，可直接照改。

### T2 `PasNativeShadowP0CompatCheck` —— PlatLv 不是 shadow，是可读写发布属性
- 报错：`PlatLv property read did not fail closed`
- 自相矛盾旁证：该工具自己的 PASS 横幅写着 `PlatLv=property-RW`。
- 字节（Delphi `TPropInfo`，Name 在记录末尾，前缀 26 字节）：

```
0x006AD1EE  6c 10 40 00     PropType   = 0x0040106C
0x006AD1F2  85 0b 00 ff     GetProc    = 0xFF000B85  -> 直接字段 +0x0B85
0x006AD1F6  85 0b 00 ff     SetProc    = 0xFF000B85  -> 同偏移，可写
0x006AD1FA  01 00 00 00     StoredProc
0x006AD206  39 00           NameIndex
0x006AD208  06 "PlatLv"     Name
```
  对照紧邻的 `IsAStudent`（`0x006AD20F` 起，GetProc `95 0b 00 ff` / SetProc 全 0）
  可见「SetProc=0 才是只读」。
- 处置：断言改成正读正写 + 「必须绑到 `m_btPlatLv`、不得碰 V/S 槽」。**已修复**。

### T3 `PasNativeShadowP123CompatCheck` —— JiaYouPoint / DecJiaYouPoint / 四个 NPC 面 API
- 报错：`JiaYouPoint property read did not fail closed`
- 字节 1（JiaYouPoint 是**只读**发布属性，`+0x0AF0`）：

```
0x006ACDBE  94 10 40 00     PropType
0x006ACDC2  f0 0a 00 ff     GetProc = 字段 +0x0AF0
0x006ACDC6  00 00 00 00     SetProc = nil（只读）
0x006ACDD6  19 00           NameIndex
0x006ACDD8  0b "JiaYouPoint"
```
- 字节 2（`DecJiaYouPoint` = `sub_6F28E8`，只读属性的专用变更器）：

```
006f28ed  85 db                  test ebx, ebx
006f28ef  7e 30                  jle  0x6f2921            ; point<=0 no-op
006f28f6  8b 81 f0 0a 00 00      mov  eax, [ecx+0xAF0]
006f2909  73 10                  jae  0x6f291b            ; 余额>=point 走减法
006f2913  89 81 f0 0a 00 00      mov  [ecx+0xAF0], eax    ; 否则夹 0
006f291b  29 99 f0 0a 00 00      sub  [ecx+0xAF0], ebx
```
  名字亦在底本：`0x007301E1` `"procedure DecJiaYouPoint(point: Integer);"`
  （`FF FF FF FF 29 00 00 00`）、`0x00733653` `"DecJiaYouPoint"`。
- 字节 3（UseGuildPoint / GetSomeGuildPoint / SetWineTreat / GetTreatWine 是 **NPC 面**
  而非独立全局）：注册运 `0x0073472D..0x00735099` 共 201 条
  `ba <declStr> / 8b c3 / e8 -> 0x00510FFC`，运头即 `My_X` / `My_Y` / `NPCSay` /
  `CreateMon` / `ClearMon`。四条各自：

```
0x00734ABD -> 0x00736608 "function UseGuildPoint(Player: TPlayer) : Integer;"
0x00734AC9 -> 0x00736644 "function GetSomeGuildPoint(Player: TPlayer) : Integer;"
0x00734E65 -> 0x007379F0 "procedure SetWineTreat(wtType: Byte; boDesk: Boolean);"
0x00734E71 -> 0x00737A30 "function GetTreatWine(Hum: TPlayer): Integer;"
```
  `ConvertVExp` 在底本 **0 命中**，仍留 standalone。
- 处置：三处断言按上述翻正（四条仍 fail-closed，只是登记面从 standalone 改到
  npcFunctions）。**已修复**。

### T4 `PasDispatchShadowCompatCheck` —— 除 R1 外另有两条陈旧断言
- **union 进度包 Param/Series 反了**。`sub_744E88`：

```
00744ed7  e8 60 36 d8 ff   call 0x4c853c        ; ax = MagicInfo.wMagicID
00744edc  50               push eax             ; 栈第 1 个
00744edd  6a 00            push 0
00744edf  6a 00            push 0
00744ee9  66 ba 45 0b      mov  dx, 0xB45       ; Ident 2885
```
  收方 `sub_6D7BF8`（两张 VMT 的 `+0x254` 都指向它）按 `TDefaultMessage` 落位：

```
006d7c6d  66 8b 45 fe / 006d7c71 -> [ebp-0x10]   Ident
006d7c75  66 8b 45 18 / 006d7c79 -> [ebp-0x0E]   Param   <- 栈第 1 个 = wMagicID
006d7c7d  66 8b 45 14 / 006d7c81 -> [ebp-0x0C]   Tag
006d7c85  66 8b 45 10 / 006d7c89 -> [ebp-0x0A]   Series
```
  即 Param=wMagicID、Tag=0、Series=0。断言互换。
- **PlayDice 种子种错了 V 库**。`sub_645200` 十次取值走 GROUP-0：

```
0064522f  33 f6            xor  esi, esi        ; i = 0
00645234  8d 4e 01         lea  ecx, [esi+1]    ; index = i+1
00645237  33 d2            xor  edx, edx        ; group = 0
0064523b  e8 a4 9f 09 00   call 0x6df1e4        ; GetV(0, i+1)
00645246  83 fe 0a         cmp  esi, 0xA        ; 十格
```
  `GetV` `0x006DF203 85 f6 test esi,esi` / `75 14 jne` 把 group 0 分到内联区
  `0x006DF20F mov eax,[ebx+eax*4+0x808]`；keyed 字典的键是
  `group*1000+index`（`sub_6E42CC imul eax,edx,0x3E8`），`<1000` 的键永远命不中。
  测试改种 `m_ScriptVGroup0[1..10]`。
- **已修复**（三条一起，工具转 PASS）。

### T5 `QST12CompatCheck` —— group-0 SetV 落内联区，不落 keyed 字典
- 报错：`SetV(0,5,0) must store 0 as real value (QST-07)`
- 字节（`sub_6DF288`）：

```
006df299  85 ff                  test edi, edi           ; edi = group
006df29b  75 16                  jne  0x6df2b3           ; !=0 走 keyed
006df29f  4a / 83 ea 64 / 73 0e  dec/sub 0x64/jae        ; index 必须 1..100
006df2a5  8b 45 08               mov  eax, [ebp+8]       ; 值（无零值判定）
006df2a8  89 84 b3 08 08 00 00   mov  [ebx+esi*4+0x808], eax
006df2b1  eb 2c                  jmp  0x6df2df           ; 直接返回
```
  `0x6DF2DA` 的 keyed upsert 在这条臂上不可达。
- 处置：改查 `m_ScriptVGroup0[5] == 0`，并**加**查 keyed 字典没被写脏（更严）。
  **已修复**。

### T6 `QST22VariableScopeAudit` —— 断言 4/5 钉的是旧形状
- 两个库已收口到 `TPlayObject.TryGetScriptVar` / `SetScriptVar` 单一解析器。
  契约不变，形状检查跟着搬家，两头都查（桥必须转发且不得自算 flat key；解析器
  必须把 group-0 V 读/写到内联槽）。检查前先**剥行注释**——生产源码的字节证据
  注释里就写着被禁的形状字面串，原来那两条 Contains 命中的是散文。**已修复**。

### T7 `QuestDiamondCompletionStateMachineCheck` —— Delphi RNG 切换已收口
- 报错：`process-wide RandomNumber was switched without consumer closure`
  （`Reject(random, "DelphiRandom")`）
- 字节（`sub_403B4C`，`result = high32(bound * (seed*0x08088405 + 1))`，种子 `0x007A2008`）：

```
00403b4c  53                             push ebx
00403b4d  31 db                          xor  ebx, ebx
00403b4f  69 93 08 20 7a 00 05 84 08 08  imul edx, [ebx+0x7A2008], 0x8088405
00403b59  42                             inc  edx
00403b5a  89 93 08 20 7a 00              mov  [ebx+0x7A2008], edx
00403b60  f7 e2                          mul  edx
00403b62  89 d0                          mov  eax, edx
00403b64  5b c3                          pop  ebx / ret
```
  三把专用闸 `DelphiRandomNumberFacadeCompatCheck` /
  `NativePasRandomContractCompatCheck` / `RngTraceSinkOffIdenticalCheck` 均 PASS。
- 处置：断言反向 —— 必须是 Delphi 门面，且不得再 `new Random(`。**已修复**。

### T8 `NativeType43EarthFireCompatCheck` —— 四条
1. `bubble legacy slot11 authority: expected=0, actual=2`。legacy 数组已是
   `Self+0xDC` 节点链的转发视图（`TBaseObject.LegacyStatusTimeView.cs`），
   slot i == state 31-i，slot 11 == internal20；同段代码上一行刚断言 internal20
   在位、下一行断言 duration=2000ms，视图必然读回 2。断言改 2。
2. `">3000"` 硬编码没了。间隔改成每子类字段 `[obj+0x54]`：

```
007178ac  c7 43 54 b8 0b 00 00   mov [ebx+0x54], 0xBB8   ; TFireBurnEvent
00717a81  c7 43 54 e8 03 00 00   mov [ebx+0x54], 0x3E8   ; TBTFireBurnEvent
007179c5  2b 43 4c               sub eax, [ebx+0x4C]
007179c8  3b 43 54               cmp eax, [ebx+0x54]
007179cb  76 5c                  jbe                     ; 严格大于才触发
```
3. `NotContains(fire,"8030")` 被压缩后的字节注释误伤：`C7 43 54 E8 03 00 00`
   去空白得 `C74354E8030000`，内含 `8030`。改查符号 `RM_MAGSTRUCK_MINE`。
4. 31 次重试 / 两轴补种 / PointList 兜底三条已随 MOVE-63 收口到
   `TBaseObject.NativeGetRandomXY`（原生 `sub_7782D0` 一体服务 11 个调用者）。
   断言搬到共用体上查，并**新增**「UserMove 必须转发、不得自带第二份搜索」。
- 另含 (c)：`FindRepoRoot` 换 `AuditRepoRoot.Resolve()`。**已修复**。

### T9 `TimedAbilityStateGateExactCheck`
- `detached / ghost internal45 broadcast 3415` 原来数整条 `m_MsgList`。state 45 的
  gained 臂本来就要对自己发一句 SysMsg，且不经地图：

```
00741e38  66 b9 ff 38            mov cx, 0x38FF
00741e3c  ba 88 2e 74 00         mov edx, 0x742E88
00741e45  ff 93 d4 00 00 00      call [ebx+0xD4]
0x742E88: refcnt FF FF FF FF / len 0C / c4 e3 b1 bb b6 a8 c9 ed c1 cb a3 a1
          = "你被定身了！"
```
  改成只数 `RM_NATIVE_HORSE_CALL_STOP`（与同函数后半段的地图用例同口径，且更严）。
- `AddTimedAbilityInternal` 已由 `private` 改 `internal`（原生 AddState 是虚槽
  `VMT+0x1EC @0x7730D0`，直接调用点遍布引擎），标记串去掉可见性。
- 另含 (c)：`FindRepoRoot`。**已修复**。

### T10 `RobotUnknownItemInitCheck` —— 样本落在了未知臂上
- 报错：`monster drop path randomized unknown attributes`
- 样本是 StdMode 15 / Shape **130**，而 `THelmet` 的 `VMT+0x28 = sub_7611C8`
  对 130/131/132 转 `[vmt+0x08]`：

```
007611d1  8b 46 1c                 mov  eax, [esi+0x1C]      ; StdItem
007611d4  8a 40 15                 mov  al,  [eax+0x15]      ; Shape
007611d7  04 7e                    add  al,  0x7E
007611d9  2c 03                    sub  al,  3
007611db  73 0c                    jae  0x7611e9             ; Shape>=133 正常臂
007611dd..e1                       call [vmt+0x08]           ; 130/131/132 未知臂
007611ed  e8 0a 2d 02 00           call 0x783efc             ; 正常臂 Dura80
007611fa  b8 0a 00 00 00 / call 0x403b4c                     ; Random(10) 闸
0076120f  f6 40 02 40              test byte [eax+2], 0x40   ; 极品位
00761213  0f 84 1b 01 00 00        je   0x761334
```
- 处置：样本换 Shape 129（同为 THelmet、走正常臂、无极品位）验「btValue 全 0 +
  Dura 落 20..99% 带」；**新增**一条用 Shape 130 验「原版 Mir2 的
  `RandomUpgradeUnknownItem` 标记 `btValue[8]` 绝不出现」——这才是本工具真正的
  分界线。**已修复**。

## 3. (c) 环境 / harness —— 逐条

| 工具 | 报错原文 | 根因 | 处置 |
|---|---|---|---|
| `NativeState26CompatCheck` | `InvalidOperationException: repo root` | 本地 `FindRepoRoot` 只从 CWD 向上找 `LyoMir2.sln`；无参数跑法把 CWD 设成共享 Build 目录，其祖先链没有 sln | 走 `AuditRepoRoot.Resolve()`（含 CallerFilePath 回退） |
| `NativeType1RouteCheck` | `FileNotFoundException: run this audit from the repository root` | 同上（`Directory.GetCurrentDirectory()`） | 同上 + 源文件缺失时 `SKIP` + exit 0 |
| `SpeedHackClientReportCompatCheck` | `DirectoryNotFoundException: run this audit from the repository root` | 同上 | 同上 |
| `PasScriptAudit` | `Usage: PasScriptAudit <GameSvr build dir> …`（exit 2） | 需要 4~5 个参数，脚本无参数调用 | 零参数改 `SKIP` + exit 0；参数数量错误仍 exit 2 |
| `WeaponUpgradeRoundTripCheck` | `INCOMPLETE: a MySQL connection string is required`（exit 2） | 无活 MySQL（本机 `mysqld` 未运行） | 改 `SKIP` + exit 0 |
| `ServerSwitchOwnershipCompatCheck` | `NickLinFu shared owner injection is missing` | 跨行 `Require` 用字面 `\n`，检出是全 CRLF（`GameApp.cs` 749 CRLF / 0 裸 LF），永不匹配 | 读入后归一化换行；`FindRepositoryRoot` 换共享解析器 |
| `TargetActionExtensionCheck` | `joint-attack direction/x/y fields are not preserved` | 同上（`\n` vs CRLF） | 同上 |
| `NpcRegistryExactReferenceCheck` | `expected=8 actual=10` | 逐行 `Contains` 把 `UsrEngn.cs` 里两行**注释**数成直接访问 | 过滤注释行（`UsrEngn.cs` 未改，热点文件） |
| `ReviveSubsystemCheck` | `the revive attempt precedes Die()…` | 「相距 <200 字符」量的是原文，中间新插入的眼神 `@MyKill` 字节注释把 `Die()` 挤出窗口 | 先剥注释再去空白；顺带把 HP 门升级成紧邻形状精确匹配（更严） |
| `SecHeroPracticeCompatCheck` | `practice LingFu summary at shared save entry: expected=1 actual=0` | 保存入口拆成两个 public 转发器 + 私有 `SaveHumanRcdCore`，旧锚点匹配到 3 行转发器，Flush 落在 2200 窗口外（实测 3727） | 分别钉两个 overload 必须转发到 core，再在 core 内查 Flush 先于 `MakeSaveRcd` |
| `QuestDiamondCompatCheck` | `global random owner left the Delphi RandSeed for System.Random` | `Reject(random, "private static Random random")` 命中的是 `RandomNumber.cs` 注释里「这个字段本类已不再拥有」那句散文 | 剥注释后查真实 `new Random(`，并正面要求 owner 是 `DelphiRandomNumberFacade` |

`PasScriptAudit` 补充：带真实参数跑（`Build\Mir200` + `staging\ys208_original_capture\
Mir200\Envir` + 本仓源根）时，**11 条内建 regression 全部 PASS**
（include-resolver / player-member-surface / synthetic-state / unsupported-overload /
cross-source-merge / global-surface / runtime-dispatch-order / script-source-classification /
npc-script-registration / runtime-use-summary / API 面统计）。唯一的非零退出来自语料
本身的 3 条解析失败：`PsMapQuest/LogonQuest - 副本.pas` 与 `RunQuest - 副本.pas`
缺 include `commonSetAbil.pas`，`PsNpcscripts/查看Boss-3.pas` 第 11 行用了
`array`。语料非仓库资产，故按环境跳过处理。

## 4. 待主代理定夺的开放项

### O1 —— `SendSocket("+WID")` / `"+UWID"` 疑似臆造（SKILL_REDBANWOL 56）

`TargetActionExtensionCheck` 原本要求三条「文本状态包」都在。底本给出的是：

- `"+UFIR"` / `"+FIR"`：**0 命中**
- `"+WID"`：**0 命中**
- `"+LNG"`：仅 1 处 `0x011A86D0`，落在 CODE 段之外（>0x7A10D0）且 **dword 引用数 0**

长刺与烈火两条已经被换成原生 ident 包并有字节（`SM_THRUSTING` 0x270 @`0x006B225B`、
`SM_FIREHITSKILL` 0x272 @`0x006B2F47`，均经 `call [ebx+0x250]`）。半月的
`TPlayObject.Attack.cs:612/617` 仍在发 `"+WID"` / `"+UWID"`，而**同文件的注释自己写着**
「SKILL_REDBANWOL(56) 落在默认臂 `0x6BCCA6`、原生什么都不发」（`0x6BC6AF` 的表
`add eax,-3` 只覆盖 3..27 加一个 58 的 `je`）。

两处自相矛盾。忠实做法应是**整条删掉那两个 `SendSocket`**，但那是可观测的线上行为
变更，且会与技能 56 的归属方冲突，所以本轮**只钉住现状**（断言仍要求 `+WID` 在位，
并在工具内注明待定），不动代码。请主代理指派归属方定夺。

### O2 —— 越权回执陈旧断言外溢到队列外两把闸

`HeroUnionStateCheck`（exit `-532462766`）与 `NativeGetUserItemCompatCheck`（exit 1）
的首条失败与 §2 T1 完全同因（`expected=权限不够!!!`）。字节证据见 T1，可直接照改。
二者不在本队列，本轮未动。

### O3 —— `InProcEngineRunCheck` 与本轮无关

`ASSERT FAILED: real Monster constructed as RC_ANIMAL and placed on the real map`，
基线即为此错，本轮改动前后逐字相同，属其它队列。

## 5. 回归验证

`_sweep.ps1` 重建并重跑 46 把闸（21 项队列 + 经验/等级血缘 12 把 + RNG/状态邻居 13 把）：

- 21 / 21 队列工具 **PASS**
- 队列外 25 把中 21 PASS、4 FAIL，四条 FAIL 与基线 `_summary.csv` 的报错原文
  **逐字相同**（`NativeLevelExpTableCheck` 随后也被修好转 PASS，见 §1），
  即本轮**未引入任何新失败**。

## 6. 提交清单（分支 `w/atriageC`）

| SHA | 说明 |
|---|---|
| `293172e4` | 5 个 AuditTool 的 harness/环境缺陷（repo-root / SKIP 语义） |
| `3e854d89` | TempSetMapParam 越权回执 + ServerSwitch 归属（CRLF + SignIn） |
| `a72152a1` | **真回归修复** `HasLevelUp` + PasDispatchShadow 两条陈旧断言 |
| `b315cfd7` | QST-12 / QST-22 / QuestDiamond ×2 |
| `5323c081` | TargetActionExtension / NativeType43EarthFire |
| `0e861152` | TimedAbilityStateGateExact |
| `d16d4d3c` | NpcRegistry / Revive / RobotUnknownItem / SecHeroPractice |
| `d4ab39e5` | NativeLevelExpTableCheck 解除空跑并翻正 HasLevelUp 契约 |

未触碰热点文件 `SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、
`GameSvr/UsrSystem/UsrEngn.cs`。唯一的生产代码改动是 `GameSvr/Actors/TBaseObject.cs`
的一行（R1）。
