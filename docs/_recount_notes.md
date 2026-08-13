# recount 工作草稿（非最终报告）

基线：master `69f049b6`；旧审计 `docs/completeness_audit_20260814.md`（基 `ed39ef63`）。
自旧审计以来 master 新增 **79** 个提交。

## 账本校验
- `staging/equivalence_ledger_20260810.tsv` = 755 行 − 1 表头 = **754 条**，754 个**唯一** id。✓
- 前缀分布实测：CGLD 24 / CRAFT 47 / DROP 38 / DURA 45 / ECON 40 / GILD 29 / MFLG 31 /
  MINE 61 / MOVE 98 / POIS 38 / PRICE 25 / QST 32 / SGRP 45 / SPWN 60 / STATE 52 /
  TCFP 28 / TRADE 61 = 754 ✓
- **订正旧报告 §2.1**：旧报告写「MINE 57、SPWN 63」，实测为 **MINE 61、SPWN 60**；且旧报告
  列出的前缀数之和为 753 而非 754。分母 754 本身正确，仅前缀明细笔误。

## 23 条旧缺口逐条复核（进行中）

### MOVE-79 —— 已闭合 (FAITHFUL)
- 提交 `b7b8bbd1`。C#：`GameSvr/Players/TPlayObject.NativeTianDiHeYi.cs`。
- 独立反汇编 sub_7274B4 全体逐条对上：
  0x7274D8 `mov eax,[eax+0x3c]`+test/je 0x727570；循环 0x727567 `cmp esi,0xB`；
  0x7274EB `[grp+esi*4+0x48]`→`[+0x10]`；0x7274F2 test/je 跳过空；0x7274F9 `cmp ebx,[eax+0x3c]`/je 跳过队长；
  0x727501 `lea edx,[ebx+0x106]` 取名；0x72750C `cmp byte [ebx+0xba4],0`/je 拒绝；
  0x72751B `cmp byte [eax+0x67],0`/jne 拒绝；0x727527 `cmp byte [eax+5],0`/jne 拒绝；
  0x727533 `call 0x6bf458`；拒绝臂 0x72753A push 0x7275a4/name/0x7275b4 三串拼接 → cx=0x38ff → vmt+0xD4。
- 裁决：**CLOSED / FAITHFUL**。

### MOVE-82 —— 契约被证伪，C# 原本即正确 (FAITHFUL, 负向)
- 提交 `b7b8bbd1`（只报不改）。
- 账本契约称 str@0x785864「在这里你无法使用」由 **NORANDOMMOVE** 在 0x7856F0 发出。
  独立反汇编**推翻**该归属：
  - sub_7855F8 派发 0x785686 `mov eax,[edi+0x1c]`/`mov al,[eax+0x15]`/dec-链：case1→0x7856A9，case2→0x78570A。
  - case2（NORANDOMMOVE 臂）：0x78570A `mov eax,[esi+0x128]` / 0x785710 `cmp byte [eax+0x68],0`
    / 0x785714 `jne 0x785742` → 0x785742 `xor ebx,ebx` / `jmp 0x78582c`。**全程静默，不发任何串**。
  - 真正发 0x7856F0 的是 case1：0x7856A9 `mov eax,[esi+0x128]` / 0x7856AF `cmp byte [eax+0x70],0`
    / 0x7856B3 `jne 0x7856f0`。**+0x70 = FOXMAP，不是 +0x68 = NORANDOMMOVE**。
  - str@0x785864 实测 refcount=-1 len=16「在这里你无法使用」 ✓（串本身存在，归属错）。
- C# `EatUseItems` case2 在 `boNORANDOMMOVE` 时静默 no-op = 原生 case2 行为。
- 裁决：**CLOSED（契约本身有误；C# 行为与原生一致）**。附带账本订正建议。

### MOVE-85 —— 已闭合 (FAITHFUL)
- 提交 `8298f9ef`（移植）+ `3ab0059a`（接线）。C#：`TPlayObject.NativeMapEntryStatus.cs`。
- 原生 0x6B6C45 `cmp byte [eax+0xb0],0` / 0x6B6C4C `jne 0x6b6c64` / `or esi,8` /
  cx=0xFCFF / edx=0x6B6D10 / vmt+0xD4 —— 逐字节对上。
- 四串长度实测全中：0x6B6D10 len38、0x6B6D40 len48、0x6B6D7C len45、0x6B6DB4 len27。
- 接线：`Message.cs:2168`（RM_LOGON 进图，对应原生 caller 0x6B954D）与 `:2378`
  （RM_NATIVE_CHANGEMAP 换图，对应 0x6B96C2）。
- 裁决：**CLOSED / FAITHFUL**。

### MOVE-10 —— 已闭合 (FAITHFUL)
- 提交 `b5a45030`（谓词）+ `9294fdb3`（接线）。
- 原生：walk 0x6D9BD0 `mov dl,0x34` / 0x6D9BD5 `call 0x772960` / test al /
  0x6D9BDC `jne 0x6dbc2c`（静默 sink）；run 0x6D9CEC/0x6D9CF1/0x6D9CF8 同构。逐条对上。
- C#：`Message.cs:1487`（CM_WALK）与 `:1580`（CM_RUN）均 `if (IsNativeMoveBlockedByPassengerState()) break;`
  —— 静默 break，不发 0x275/0x276，与原生「整臂丢弃」一致。
- 裁决：**CLOSED / FAITHFUL**。

### MOVE-11 —— 已闭合 (FAITHFUL)
- 提交 `a9cc64f2`。C#：`TPlayObject.NativeMoveActionCancel.cs`，接线在 `TPlayObject.Attack.cs:758`(run)/`:900`(walk)。
- 原生 0x6D9BEC `call 0x6bce2c`（edx = word[msg+4] = Ident）在 gate3 `call [edx+0xBC]`(0x6D9BF6) 之前；
  run 侧 0x6D9D08 同构，gate3 在 0x6D9D12。逐条对上（即「可被后续拒绝也已 flush」）。
- 裁决：**CLOSED / FAITHFUL**。

### MOVE-90 —— 已闭合 (FAITHFUL)
- 提交 `597075b9`（消费者）+ `b82c7142`（接线）。C#：`TPlayObject.NativeNoMagicMap.cs`。
- 接线 `Message.cs:1829` `if (!NativeNoMagicMapForbidsSpell() && ClientSpellXY(...))`，
  短路后 dwDelayTime 保持 0 → 走 else 的 `RM_MOVEFAIL + SM_ACT_FAIL`，
  对应原生 0x6DA17A 静默 0x276 应答。
- 已记录偏差（C# 自述）：原生 0x6DA0A7 用 vmt+0x40(sub_6E6700) 分 block A/C，
  只有 block C 带 NOMAGIC 闸；C# 对所有玩家统一施闸 = 常规玩家 1:1，异常状态下更严格（fail-closed）。
- 裁决：**CLOSED / FAITHFUL（带一条 fail-closed 方向的记录性偏差）**。

### MOVE-89 —— 已闭合（fail-closed，原生死代码，已证）
- 提交 `4f342eab`(移植) → `597075b9`(定性) → `b84eba38`(REVERT 接线)。
- C#: `TPlayObject.NativeTriggerBombMap.cs`，唯一方法 `NativeTriggerBombMapConsumerIsReachable()` 恒 false、无调用者。
- 独立复核其负命题：
  - dword `0x781304`(TTimerBomb classptr) 全镜像**恰好 2 次命中**：`0x007812B8`(自身 vmtSelfPtr)、`0x00781370`(自身 RTTI 自引用)。从不作为立即数/classref 被加载。
  - `sub_7896FC` 唯一调用者 = `0x7896BD`(在 `sub_789694` 内)；`sub_789694` 只经 TTimerBomb VMT+0x18 可达。
  - 类名 shortstring `0x0a "TTimerBomb"` @0x781355 实测在位。
- 结论：TTimerBomb 永不被实例化 ⇒ boTRIGGERBOMB 被解析但运行期从不被读 ⇒ **原生无可观测效果**。
  C# 不接线是**正确**的；接线反而会造成分歧。裁决：**CLOSED（已证负命题）**。

### MOVE-31 —— 已闭合（良性，补上了不可达性证明）
- `de97da32` 只报不改，但补了原分片没有的证明。独立复核：全仓 `m_PEnvir = null` 仅 2 处 ——
  `Maps/MapPoint.cs:78/92`（属另一个类 TPointManager）与 `Actors/TBaseObject.cs:322`（字段初始化器）。
  对象入格后 `m_PEnvir` 永不置空 ⇒ 原生探针 `sub_765D64` 的三项失效信号在托管端全不可达 ⇒ 摘链臂恒不触发。
- 裁决：**CLOSED（可观测行为等价）**。回归触发条件：若日后引入 `m_PEnvir = null` 清理，本条立即重新变为真缺口。

### MOVE-39 —— **仍未完全闭合（PARTIAL）**
- `9ae059ee` 只闭合了契约的**最后一个子句**（双人坐骑同伴跟随 sub_6BBEE4）。
- 独立复核原生尾部 `sub_741224`：0x7412D5 提交 X / 0x7412DB 提交 Y / **0x7412E8 `mov dl,0x17` + 0x7412EC `call 0x76B4D0`（清定时状态 0x17）** /
  0x74130D `mov dx,0x2712` + 0x741315 `call [edi+0xD8]`（先广播）/ **0x741323 `call 0x778EC0`（后落格）** / 0x741328 `mov dl,0x33` …
- C# 现状：
  - 同伴跟随 ✓（`TBaseObject.NativeWalkMoverTail.cs` + `TPlayObject.NativeWalkPartnerFollow.cs`，接线 `TBaseObject.cs:1334`）。
  - **清定时状态 0x17 ✗** —— `RemoveNativeMovementTimedState(23)` 只出现在 `CompleteNativeRun3Move`(4108 腿) 与两条坐骑腿，
    walk 腿 `WalkTo`/`Walk` 全无此调用（grep 实测 4 处命中，无一在 walk 腿）。
  - **广播/落格次序相反** —— 原生「先广播 0x2712 后 sub_778EC0」，C# `Walk()` 把两者合并且次序颠倒。
- 本车道自己的收口报告 `docs/move_misc_residual_20260814.md` §5 明确写「本轮不动」这两件事。
- 裁决：**PARTIAL（1/3 子句闭合）**。不得记为已完成。

### SPWN-13 —— 已闭合 (FAITHFUL)
- `be5126b0`(移植) + `39ebe3b9`(接线) + `a5a34bcb`(消费端收口)。
- 独立反汇编逐字节对上：`0x67CA49 83 7B 28 00 cmp dword [ebx+0x28],0` / `0x67CA4D 74 0B je 0x67CA5A` /
  `0x67CA52 66 8B 53 28 mov dx,word [ebx+0x28]` / `0x67CA56 66 89 50 38 mov word [eax+0x38],dx`。
  **门是 dword、落地是 word** —— C# `ApplyNativeMonGenCorpseSeconds` 正是这么写的（`nCorpseSeconds==0` 判空 + `(short)` 截断）。
- 接线：`UsrEngn.cs:3406`。消费端 `TBaseObject.Base.cs:126` 按 `0x766682 movsx word[obj+0x38]` 读。
- 裁决：**CLOSED / FAITHFUL**。

### SPWN-14 —— 已闭合 (FAITHFUL)
- 独立反汇编：`0x67CA5D mov eax,[eax+0x40]` / `0x67CA60 call 0x406A88`(_DynArrayLength) /
  `0x67CA65 test eax,eax` / `0x67CA67 jle 0x67CA92`（长度 <=0 不播报）；播报臂
  `0x67CA7C push 1` / `0x67CA85 mov cx,0x38FF` / `0x67CA89 mov dx,0x64` / `0x67CA8D call 0x5F6F9C`。
- C# `UserEngine.NativeMonGenAnnounce.cs`：Ident=100、Param=0x38FF、FilterUserIndex=1，走 `BroadcastLegacyType18`。
  接线 `UsrEngn.cs:3411`，位置正确（字段搬运之后、挂 CertList 之前）。
- 裁决：**CLOSED / FAITHFUL**。

### DROP-33 —— 已闭合 (FAITHFUL，四条腿全对)
- `9584a44f`(own-table 4→3) + 既有世界腿修复。独立复核四个半径立即数：
  - 独占链 `0x71FC02`/`0x71FC79` = `b9 05 00 00 00` (mov ecx,5) → C# `NativeExclusiveChainDropRange = 5`
  - own-table `0x71FDCF`/`0x71FE46` = `b9 03 00 00 00` → C# `NativeMonsterOwnTableScatterRange = 3`
  - 世界腿 `0x71FF3D` = `b9 03 00 00 00` → C# `NativeDropControlRuntime.ScatterRange = 3`
  - 金币 `0x768ADC 6a 03 push 3` → C# `TBaseObject.cs:1500 GetDropPosition(...,3,...)`
- 裁决：**CLOSED / FAITHFUL**。
- 附带瑕疵（非行为）：`TBaseObject.cs:1490` 的局部量 `DropWide = _MIN(nDropItemRage,7)` 在 `DropGoldDown` 内已成死变量（下文用的是硬编码 3）。仅代码卫生。

### MOVE-75 —— 已闭合 (FAITHFUL)
- 独立反汇编 `sub_772EB8` 的消费面：`TBaseObject.cs:639` 记 `0x765DC2 call 0x772EB8`，
  C# 已把 `m_boObMode(+0x2E2) || 体状态 0x3C` 两项合取落地（MOVE-33 补的第六项）。
  `NativePassThrough.NativeComputeThroughOccupancy` 首句即 `HasNativeCellPassThroughGrant()`，
  对应 `sub_768454` 的 `0x76845C call 0x772EB8 / 0x768463 jne → TRUE`。
- 裁决：**CLOSED / FAITHFUL**。

### MOVE-73 —— **仍未闭合（DIVERGENT）**
- 独立反汇编 `sub_768454` 全体：
  `0x76845C call 0x772EB8` → 真则 TRUE；否则 `0x76846B cmp byte [Envir+0x84],0` 非零则 FALSE；
  再 `0x76848A call 0x7684DC`（安全区子判定，带全局 `[0x7D6970]` 与 X/Y）→ 真则 TRUE，否则 FALSE。
  **这不是 InSafeArea，是穿透判定。**
- 原生 tick 站点（`sub_6B2D38` 内）实测：`0x6B308E call 0x768454` / `0x6B3096 cmp al,[edx+0x3FE]` /
  `0x6B309C je 0x6B30E1`（未变则既不写也不发包）/ `0x6B30A3 mov [ecx+0x3FE],dl`（**仅变化时回写**）。
  且上游 `0x6B303F cmp byte [eax+0x711],0 / je 0x6B308B` 是**跳到** 0x6B308B，即
  `call 0x768454` **无条件每 tick 执行**，原生此处**没有任何行会门**。
- C# 现状：`TPlayObject.NativePassThrough.cs` 有字段 `m_boThroughOccupancyCache`，但
  `NativeRefreshThroughOccupancyCache()` 在**移动使用点**调用且**无条件覆写**（无「仅变化时回写」比较），
  不在 tick 头刷新。文件自述这是「已记录的有界偏差」。
- 裁决：**仍 DIVERGENT**（判定值一致，刷新时机与写入条件不一致）。

### MOVE-74 —— **仍未闭合（DIVERGENT，且发现错接）**
- 原生 0xB05 变迁广播实测：TRUE 臂 `0x6B30AD push 6 / push 1 / push 0 / push 0 / xor ecx,ecx / mov dx,0xB05 / call [vmt+0x250]`；
  FALSE 臂 `0x6B30C8` 同形但 `push 0`。**驱动它的是 `sub_768454` 穿透判定的变迁。**
- C# 现状 `TPlayObject.Message.cs:418-437`：确实发了 `SendDefMessage(SM_COMMON_INFORMATION=2821=0xB05, 0, 6, x?1:0, 0, "")`，
  但：
  1. **谓词错**：用的是 `InSafeArea()`、缓存字段是 `m_boInSafeArea`；行内注释把 `sub_768454` 写成「InSafeArea」、
     把 `[+0x3FE]` 写成 `m_boInSafeArea`。二进制证明该谓词是穿透判定。
     `Grobal2.cs:1044` 甚至把 2821 注释为 "Safe zone entry/exit notification"，同一误解。
  2. **多了一道原生没有的门**：整块被 `if (m_MyGuild != null) { if (m_MyGuild.GuildWarList.Count > 0) {` 包住；
     原生 0x6B308B..0x6B30E1 无任何行会条件。
  3. 同一原生字段 `Obj+0x3FE` 在 C# 里被**两个不同字段**建模（`m_boInSafeArea` 与 `m_boThroughOccupancyCache`），
     语义互相冲突，必有一错。
- 裁决：**仍 DIVERGENT，且属「错接」而非单纯缺失**。优先级应高于原报告给的评级。

### SGRP-26 (217/218 师徒) —— 已闭合 (FAITHFUL)
- `31d80e5a`。独立复核跳表桩体：`0x6572A4 mov ecx,[ebp+8] / mov edx,[ebp+0xc] / call 0x657CF0`；
  `0x6572B4 ... call 0x657AC0`。与契约一致。
- C#: `Grobal2.ISM_MENTOR_STUDENT_1=217` / `ISM_MENTOR_STUDENT_2=218`；
  `MirrorMessage.cs:159-166` → `MsgGetMentorStudentLeft` / `MsgGetMentorExpel`
  → `TPlayObject.NativeMirrorMentor.cs` 的 `NativeMirrorStudentLeftMaster` / `NativeMirrorMasterExpelStudent`。
- 裁决：**CLOSED / FAITHFUL**。

### SGRP-31 (241 信用卡全清) —— 已闭合 (FAITHFUL)
- `31d80e5a`。独立复核桩体 `0x65735C mov eax,[0x7D6D50] / mov eax,[eax] / call 0x655A18`
  —— **无 body 入参、无条件**。
- C#: `MirrorMessage.cs:116-124` 无条件 `ResetOnlineAll()`；旧的「非空 body 走行会战」C# 扩展已删除；
  `ISM_GUILDWAR=241` 已标 `[Obsolete]`。
- 裁决：**CLOSED / FAITHFUL**。

### SGRP-25 (202 反作弊惩罚) —— **仍未闭合（DIVERGENT，已降级为有据 fail-closed）**
- 独立复核桩体 `0x657208 mov edx,[ebp+8] / push edx / mov ecx,[ebp+0x10] / mov edx,[ebp+0xc] / call 0x658384`
  —— 确有第三参 `[ebp+0x10]`（惩罚时长）。
- C# `MirrorMessage.cs:44-69` **仍调 `MsgGetUserLogout`**。常量 `ISM_USERLOGOUT=202` 已标 `[Obsolete]`，
  但 case 与行为未改。
- 已记录的三条卡点（可信）：跨服为文本协议无第三 dword 载体；`[+0x180c]` 到期字段全仓无对应成员；
  全仓唯一 live 202 发送方是 `UsrEngn.cs:1568` 的登出广播。
- 裁决：**仍 DIVERGENT**（契约「202 是反作弊惩罚而非登出」未满足）。但从「未察觉的错」升级为「已取证的有据 fail-closed」。

### SGRP-44 (207 常量冲突) —— **仍未闭合（DIVERGENT，已降级为有据 fail-closed）**
- C# `Grobal2.cs:1806/1808`：`ISM_RELOADGUILD = 207` 与 `ISM_SERVERSWITCH = 207` **冲突仍在**（两者均已标 `[Obsolete]`，
  另新增 `ISM_SINGLEQUOTE_SCAN = 207`）。`MirrorMessage.cs:88-112` 的 case 仍是数字 body→信用卡 switchWord、
  非数字→重载行会两套 C# 旧语义；原生 `sub_658114` 的 37 位全局位图 swap 未移植。
- 卡点：新掩码来自帧第三 dword（文本协议无载体）；全局 `[0x7D7038]` 位图对象与逐位回调 `sub_658110`/`0x794F30` 无模型。
- 裁决：**仍 DIVERGENT**。

### SGRP-30 (247) —— **仍未闭合（PARTIAL / fail-closed）**
- C# 新增 `ISM_IDENT_247 = 247` 与 `MirrorMessage.cs:211-224` 的**显式空 case**（避免落 error sink 打印
  "[Error] ProcessOthGsMsg Ident=247"，那与原生不符）。
- 但原生 handler 本体（`sub_65805C` 门 `len==0xD`、读三 dword、`0x65808A call 0x699310` 写日志/DB）**未移植**。
- 裁决：**PARTIAL**。较契约「entirely MISSING」有改善（派发面已在），本体仍缺。

### POIS-11 —— 已闭合 (FAITHFUL)
- `02a76791`。原判 DIVERGENT 的根因是「并行 ushort[12] 秒制存储」这个第二权威。
  复核：`TBaseObject.cs:114-118` 明确 `m_wStatusTimeArr` 已无存储；
  `TBaseObject.LegacyStatusTimeView.cs` 是**纯转发门面**（`sealed class` 只持 `_owner`，
  且做成只读属性以便任何再赋值变成编译错误），读写均落唯一权威 Self+0xDC 节点链。
- 索引双 fork 的桥接经独立反汇编坐实：绿毒 applier `0x76E5C9 push 0x1F`、红毒 applier `0x76E673 push 0x1E`；
  C# `POISON_DECHEALTH=0`→31−0=0x1F ✓、`POISON_DAMAGEARMOR=1`→31−1=0x1E ✓。
- 裁决：**CLOSED / FAITHFUL**。

### STATE-19 —— 已闭合 (FAITHFUL；原 DIVERGENT 前提被推翻)
- `7ee2a42d`（仅证据文档）。独立复核两条关键事实：
  1. `8037 = 0x1F65` 作为 16 位立即数全镜像 **0 命中**（`66 ba 65 1f` 与 `66 b9 65 1f` 皆 0）；
     对照 `66 ba 5a 28`(0x285A) **9 命中** ⇒ 扫描器可信。契约的负命题成立。
  2. 但「原生直调 MakePosion」是**错的**：绿毒 applier `0x76E5D5 push 0x3E8`(1000ms) / `0x76E5DA mov cx,0x283C`(10300)
     / `0x76E5E1 call 0x766060`(SendDelayMsg)；红毒 applier `0x76E689/0x76E68E/0x76E696` 同构。
     **原生同样走 1000ms 延迟内部消息**。
- C# `SendDelayMsg(RM_POISON, …, 1000)` → `TBaseObject.Base.cs:2097 case RM_POISON` → MakePosion，
  与原生 10300 接收臂同构。8037 与 10300 都是**服务器内部编号、永不上线**，行为不可见。
- 裁决：**CLOSED / FAITHFUL**。

### SPWN-56 —— **仍未闭合（DIVERGENT，有据 fail-closed）**
- `02a76791` 维持只报不改，但把根因从「谓词写错」更正为「照搬会造成净回归」。
- 原生类型 1 分支用 `sub_765D64`（`[+0x106]!=0 && [+0x128]!=0 && Envir[+0x44]!=0` 三项有效性）做校验；
  C# 用 `m_boDeath`。二者不等价：**已死但仍有效**的对象在 C# 会被摘链、在原生不会。
- 保留 C# 谓词的理由（可信）：托管端三项恒真 ⇒ 照搬会让摘链臂变死代码，而它是 OS_MOVINGOBJECT
  孤儿格子项在托管侧的唯一 GC，且位于每 tick 最热循环。
- 裁决：**仍 DIVERGENT**（这是一处**有意的、已记录的**偏离，不是疏漏）。
