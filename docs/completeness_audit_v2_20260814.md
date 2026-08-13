# 战神引擎 M2Server → C# 1:1 复刻 · 完成度全量重算（v2）

- 日期：2026-08-14　任务代号：**recount**
- 工作树：`D:\loym2\.claude\wt3\recount`　分支：`w/recount`　基/HEAD：**`4a59a361`**（master）
- 上一版：`docs/completeness_audit_20260814.md`（基 `ed39ef63`，产出 01:12）
- 自上一版以来 master 新增 **98 个提交**（`ed39ef63..4a59a361`）

> **审计窗口说明**：本轮开工时 master 在 `69f049b6`，审计过程中并行车道把 master 推进到
> `4a59a361`（+19 提交）。已 rebase 并对**受影响的三项**（SPWN-56 / MOVE-11 / SGRP 簇）
> 重新裁决，见 §3.2 与 §8。其余条目的判定基于 `69f049b6`，在 `69f049b6..4a59a361`
> 区间内未被触及。
>
> **定稿时 master 已到 `c1bb5696`（再 +14 提交）**，其中 3 处明确触及本报告结论
> （DROP-33 / STATE-19 / DURA-39），**已在 §8.1 点名，未纳入重算**。本报告是
> `4a59a361` 的时点快照，不是「此刻」的真值。
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase 0x400000，file_off = VA − 0x400000）
- 工具：`tools/recount_dis.py`（capstone 5.0.7 / x86-32，本轮新建）、`tools/recount_sample.py`（抽样器）
- 铁律：**只读审计，不改任何业务代码**。每条判定都附 C# 位置 + 原生 VA。凡未亲自核实者一律标 UNKNOWN/仍未闭合，**不充数**。
- 原始核验轨迹（逐条反汇编输出）见 `docs/_recount_notes.md`。

---

## 0. 执行摘要

| 指标 | 分子/分母 | 值 |
|---|---|---|
| 契约总数 | — | **754** |
| FAITHFUL（沿用，本轮抽查 30 条未发现虚报） | 651 | |
| FIXED（累计已闭合） | 42 | |
| **严格完成度** | **693 / 754** | **91.9%** |
| C 类（不可证 / DBSvr 运行期 / 有据 fail-closed） | 54 | |
| **广义可交付度** | **747 / 754** | **99.1%** |
| **真实行为缺口** | **7 / 754** | **0.93%** |
| 保守下界（把 40 条非 TIER1 证据强度的 FAITHFUL 全部剔出） | 653 / 754 | 86.6% |

**一句话**：真实可证的行为缺口从上一版的 23 条降到 **7 条**，严格完成度 89.4% → **91.9%**。
但**广义 99.1% 这个数字不要单独引用** —— 它把 54 条「不可证/依赖外部系统」全算成可交付，
是上一版定下的宽口径。诚实区间见 §6。

**本轮最重要的发现不是数字，而是一处错接**：原生 `Obj+0x3FE` 在 C# 里被**两个字段**重复建模
（`m_boInSafeArea` 与 `m_boThroughOccupancyCache`），且 0xB05 变迁广播接在了**错误的谓词**上。
详见 §3 的 MOVE-73/74。这是上一版没有识别出来的问题，评级应高于原报告给它的 "在建"。

---

## 1. 账本基线校验

`staging/equivalence_ledger_20260810.tsv` = 755 行 − 1 表头 = **754 条数据行，754 个唯一 id**。✓

实测前缀分布：

| 前缀 | 数 | 前缀 | 数 | 前缀 | 数 |
|---|--:|---|--:|---|--:|
| MOVE | 98 | DURA | 45 | MFLG | 31 |
| MINE | 61 | ECON | 40 | GILD | 29 |
| TRADE | 61 | DROP | 38 | TCFP | 28 |
| SPWN | 60 | POIS | 38 | PRICE | 25 |
| STATE | 52 | QST | 32 | CGLD | 24 |
| CRAFT | 47 | SGRP | 45 | | |

合计 = 754 ✓

> **订正上一版 §2.1**：旧报告写「MINE 57、SPWN 63」，实测为 **MINE 61、SPWN 60**；且旧报告
> 列出的前缀数之和是 753 而不是 754。分母 754 本身正确，只是前缀明细笔误。

---

## 2. 29 分片逐片统计（按当前 master `69f049b6`）

| # | 契约段 | 条数 | FAITHFUL | FIXED | 仍未闭合 | C 类 |
|---|---|--:|--:|--:|--:|--:|
| 01 | ECON-01..26 | 26 | 25 | 1 | 0 | 0 |
| 02 | ECON-27..40 + PRICE-01..12 | 26 | 22 | 1 | 0 | 3 |
| 03 | PRICE-13..25 + TRADE-01..13 | 26 | 23 | 1 | 0 | 2 |
| 04 | TRADE-14..39 | 26 | 26 | 0 | 0 | 0 |
| 05 | TRADE-40..61 + CRAFT-01..04 | 26 | 22 | 1 | 0 | 3 |
| 06 | CRAFT-05..30 | 26 | 25 | 1 | 0 | 0 |
| 07 | CRAFT-31..47 + DURA-01..09 | 26 | 25 | 0 | 0 | 1 |
| 08 | DURA-10..35 | 26 | 24 | 1 | 0 | 1 |
| 09 | DURA-36..45 + POIS-01..16 | 26 | 16 | 1 | 0 | 9 |
| 10 | POIS-17..38 + STATE-01..04 | 26 | 20 | 1 | 0 | 5 |
| 11 | STATE-05..30 | 26 | 25 | 1 | 0 | 0 |
| 12 | STATE-31..52 + MINE-01..04 | 26 | 23 | 0 | 0 | 3 |
| 13 | MINE-05..30 | 26 | 26 | 0 | 0 | 0 |
| 14 | MINE-31..56 | 26 | 24 | 1 | 0 | 1 |
| 15 | MINE-57..61 + GILD-01..21 | 26 | 23 | 1 | 0 | 2 |
| 16 | GILD-22..29 + TCFP-01..18 | 26 | 25 | 0 | 0 | 1 |
| 17 | TCFP-19..28 + MFLG-01..16 | 26 | 26 | 0 | 0 | 0 |
| 18 | DROP-01..11 + MFLG-17..31 | 26 | 25 | 1 | 0 | 0 |
| 19 | DROP-12..37 | 26 | 21 | 3 | 0 | 2 |
| 20 | DROP-38 + MOVE-01..25 | 26 | 22 | 4 | 0 | 0 |
| 21 | MOVE-26..51 | 26 | 23 | 2 | **1** | 0 |
| 22 | MOVE-52..77 | 26 | 19 | 4 | **2** | 1 |
| 23 | MOVE-78..98 + SPWN-01..05 | 26 | 15 | 7 | 0 | 4 |
| 24 | SPWN-06..31 | 26 | 14 | 7 | 0 | 5 |
| 25 | SPWN-32..57 | 26 | 21 | 0 | **1** | 4 |
| 26 | SPWN-58..60 + SGRP-01..23 | 26 | 26 | 0 | 0 | 0 |
| 27 | SGRP-24..45 + CGLD-01..04 | 26 | 18 | 2 | **3** | 3 |
| 28 | CGLD-05..24 + QST-01..06 | 26 | 25 | 1 | 0 | 0 |
| 29 | QST-07..32 | 26 | 22 | 0 | 0 | 4 |
| | **合计** | **754** | **651** | **42** | **7** | **54** |

每片行内和均 = 26，总和 = 754 ✓（脚本校验，无失衡片）

---

## 3. 23 条旧缺口 · 逐条现状裁决

### 3.1 已闭合（16 条）

| 契约 | 旧态 | 闭合提交 | 本轮独立复核依据 |
|---|---|---|---|
| MOVE-79 | MISSING | `b7b8bbd1` | `sub_7274B4` 全体逐条对上：0x7274D8 owner/test-je、0x727567 `cmp esi,0xB` 循环、0x7274EB `[grp+esi*4+0x48]→[+0x10]`、0x7274F9 跳过队长、0x72750C `[ebx+0xBA4]`、0x72751B `[Envir+0x67]`、0x727527 `[Envir+5]`、0x727533 `call 0x6BF458`、拒绝臂三串拼接 + cx=0x38FF + vmt+0xD4。C# `TPlayObject.NativeTianDiHeYi.cs` 一一对应 |
| MOVE-82 | MISSING | `b7b8bbd1`（只报不改） | **契约本身被证伪**。case2(NORANDOMMOVE)臂 0x785710 `cmp byte [eax+0x68],0` / 0x785714 `jne 0x785742` → `xor ebx,ebx; jmp` **全程静默**。发 str@0x785864 的是 case1 的 0x7856AF `cmp byte [eax+0x70],0`（**FOXMAP**）。C# `EatUseItems` case2 静默 no-op 本就正确 |
| MOVE-85 | MISSING | `8298f9ef` + `3ab0059a` | 0x6B6C45 `cmp byte [eax+0xB0],0` / jne / cx=0xFCFF / edx=0x6B6D10 / vmt+0xD4 逐字节对上；四串长度实测 38/48/45/27 全中；接线 `Message.cs:2168`(进图 ↔ 0x6B954D)、`:2378`(换图 ↔ 0x6B96C2) |
| MOVE-89 | MISSING | `4f342eab`→`597075b9`→`b84eba38`(REVERT 接线) | **已证原生死代码**：dword `0x781304`(TTimerBomb classptr) 全镜像**恰好 2 次**命中，皆为自引用(0x7812B8 vmtSelfPtr / 0x781370 RTTI)；`sub_7896FC` 唯一调用者 0x7896BD。TTimerBomb 永不实例化 ⇒ boTRIGGERBOMB 运行期从不被读。**C# 不接线才是对的** |
| MOVE-90 | MISSING | `597075b9` + `b82c7142` | 接线 `Message.cs:1829`，短路后 dwDelayTime=0 → `RM_MOVEFAIL + SM_ACT_FAIL`，对应原生 0x6DA17A 静默 0x276。带一条 fail-closed 方向的记录性偏差（block A/C 未分叉，对常规玩家 1:1） |
| MOVE-10 | MISSING | `b5a45030` + `9294fdb3` | walk 0x6D9BD0 `mov dl,0x34`/0x6D9BD5 `call 0x772960`/0x6D9BDC `jne 0x6DBC2C`；run 0x6D9CEC/0x6D9CF1/0x6D9CF8 同构。C# `Message.cs:1487`/`:1580` 静默 `break`，不发 0x275/0x276 |
| MOVE-11 | MISSING | `a9cc64f2`（+ 审计窗口内 `0a2419ca`/`288028e3`） | 0x6D9BEC `call 0x6BCE2C`(edx=Ident) 在 gate3(0x6D9BF6) 之前；run 侧 0x6D9D08/0x6D9D12 同构。C# `NativeMoveActionCancel.cs`，接线 `Attack.cs:758`/`:900`。**契约本体（sub_6BCE2C）在 `69f049b6` 即已闭合**；窗口内另修了一处**相邻**遗漏：run 臂在 0x34 门之前还有 0x6D9CE7 `call 0x7742C0`（隐身揭示钩子），walk 臂没有（我的反汇编独立印证：walk 0x6D9BD0 直接 `mov dl,0x34`）。同时更正了「sub_6BCE2C 发 SM 0x20 外观刷新」的错误归因 |
| MOVE-31 | MISSING | `de97da32`（只报不改） | 良性判断成立且补了证明：全仓 `m_PEnvir = null` 仅 2 处（`MapPoint.cs` 属另一个类；`TBaseObject.cs:322` 是字段初始化器）⇒ 原生探针三项失效信号在托管端全不可达 ⇒ 摘链臂恒不触发，可观测行为等价 |
| MOVE-75 | NOTHROUGH 在建 | 既有 + MOVE-33 补项 | `sub_768454` 首句 0x76845C `call 0x772EB8` → 真则 TRUE；C# `NativeComputeThroughOccupancy` 首句即 `HasNativeCellPassThroughGrant()`（`m_boObMode(+0x2E2) ‖ 体状态 0x3C`），`TBaseObject.cs:655` 有取证 |
| SPWN-13 | MISSING | `be5126b0`+`39ebe3b9`+`a5a34bcb` | 0x67CA49 `83 7B 28 00`(dword 门) / 0x67CA52 `66 8B 53 28`(word 取) / 0x67CA56 `66 89 50 38`(word 存)。**门是 dword、落地是 word** —— C# `ApplyNativeMonGenCorpseSeconds` 正是如此。接线 `UsrEngn.cs:3406` |
| SPWN-14 | MISSING | `be5126b0` + `39ebe3b9` | 0x67CA60 `call 0x406A88`(Length，非 0x406A90 的 High) / 0x67CA67 `jle` / 播报臂 push 1 / cx=0x38FF / dx=0x64 / call 0x5F6F9C。C# `UserEngine.NativeMonGenAnnounce.cs`，接线 `UsrEngn.cs:3411` |
| DROP-33 | DIVERGENT | `9584a44f` + 既有 | 四条腿半径全对：独占链 0x71FC02/0x71FC79 `b9 05`→C# `NativeExclusiveChainDropRange=5`；own-table 0x71FDCF/0x71FE46 `b9 03`→`NativeMonsterOwnTableScatterRange=3`；世界腿 0x71FF3D `b9 03`→`ScatterRange=3`；金币 0x768ADC `6a 03`→`GetDropPosition(...,3,...)` |
| POIS-11 | DIVERGENT | `02a76791` | 「结构 fork」根因已消除：`m_wStatusTimeArr` 现为 `LegacyStatusTimeView` 纯转发门面（`TBaseObject.cs:114-118` 明确无存储），读写均落唯一权威 Self+0xDC。31−nType 桥接由绿毒 applier `0x76E5C9 push 0x1F`、红毒 `0x76E673 push 0x1E` 双点坐实 |
| STATE-19 | DIVERGENT | `7ee2a42d`（仅证据） | 两条事实独立复核：①`0x1F65` 作 16 位立即数全镜像 **0 命中**（对照 `0x285A` 9 命中，扫描器可信）；②但「原生直调 MakePosion」**是错的** —— 绿毒 0x76E5D5 `push 0x3E8`/0x76E5DA `mov cx,0x283C`/0x76E5E1 `call 0x766060`，**原生同样走 1000ms 延迟内部消息**。8037 与 10300 都是内部编号、永不上线 |
| SGRP-26 | DIVERGENT | `31d80e5a` | 桩体 0x6572A4→`call 0x657CF0`、0x6572B4→`call 0x657AC0` 实测对上。C# `ISM_MENTOR_STUDENT_1/2` → `MsgGetMentorStudentLeft`/`MsgGetMentorExpel` → `TPlayObject.NativeMirrorMentor.cs` |
| SGRP-31 | DIVERGENT | `31d80e5a` | 桩体 0x65735C `mov eax,[0x7D6D50]/[eax]/call 0x655A18` —— **无 body 入参、无条件**。C# `MirrorMessage.cs:116-124` 已改为无条件 `ResetOnlineAll()`，旧的「非空 body 走行会战」C# 扩展已删，`ISM_GUILDWAR=241` 标 `[Obsolete]` |

### 3.2 仍未闭合（7 条）

| 契约 | 旧态 | 现状 | 卡点（本轮实测） |
|---|---|---|---|
| **MOVE-39** | MISSING | **PARTIAL（3 子句闭合 1）** | `9ae059ee` 只闭合了「双人坐骑同伴跟随 sub_6BBEE4」。**清定时状态 0x17 仍缺**：原生 0x7412E8 `mov dl,0x17`/0x7412EC `call 0x76B4D0`，而 C# `RemoveNativeMovementTimedState(23)` 全仓 4 处命中**无一在 walk 腿**（只在 `CompleteNativeRun3Move` 与两条坐骑腿）。**广播/落格次序仍相反**：原生先 0x741315 广播 0x2712、后 0x741323 `call 0x778EC0`；C# `Walk()` 合并且倒序。本车道报告 `move_misc_residual_20260814.md` §5 自述「本轮不动」 |
| **MOVE-73** | NOTHROUGH 在建 | **DIVERGENT** | 原生 tick 站点 0x6B308E `call 0x768454` / 0x6B3096 `cmp al,[edx+0x3FE]` / 0x6B309C `je`（未变则**既不写也不发包**）/ 0x6B30A3 `mov [ecx+0x3FE],dl`（**仅变化时回写**）。且上游 0x6B303F 的 `je 0x6B308B` 是**跳到**它，即**每 tick 无条件求值**。C# `NativeRefreshThroughOccupancyCache()` 在**移动使用点**调用且**无条件覆写**，无「仅变化时」比较、不在 tick 头刷新 |
| **MOVE-74** | NOTHROUGH 在建 | **DIVERGENT（且是错接，非单纯缺失）** | 原生 0xB05 变迁广播由 **`sub_768454` 穿透判定**的变迁驱动（TRUE 臂 0x6B30AD `push 6/push 1/…/mov dx,0xB05/call [vmt+0x250]`，FALSE 臂 0x6B30C8 `push 0`）。C# `Message.cs:418-437` 确实发了 `SM_COMMON_INFORMATION(=2821=0xB05)`，但 ①谓词用的是 **`InSafeArea()`**、缓存字段是 `m_boInSafeArea`；②整块被 **`m_MyGuild != null && GuildWarList.Count > 0`** 包住，**原生此处没有任何行会条件**；③同一原生字段 `Obj+0x3FE` 被 C# **两个字段**重复建模，语义互斥。反汇编 `sub_768454` 全体证明它是穿透判定：`call 0x772EB8` → 真则 TRUE；否则 `cmp byte [Envir+0x84],0` 非零则 FALSE；再 `call 0x7684DC`。**不是 InSafeArea** |
| **SGRP-25** | DIVERGENT | **DIVERGENT（已降级为有据 fail-closed）** | C# `MirrorMessage.cs:44-69` 仍调 `MsgGetUserLogout`。常量已标 `[Obsolete]` 但行为未改。三条卡点可信：跨服为文本协议，无第三 dword 载体（桩体 0x657208 确有 `mov ecx,[ebp+0x10]`）；到期字段 `[+0x180c]` 全仓无对应成员；全仓唯一 live 202 发送方是 `UsrEngn.cs:1568` 的登出广播 |
| **SGRP-44** | DIVERGENT | **DIVERGENT（已降级为有据 fail-closed）** | `Grobal2.cs:1806/1808` 的 `ISM_RELOADGUILD = ISM_SERVERSWITCH = 207` **冲突仍在**（两者已标 `[Obsolete]`，另加了 `ISM_SINGLEQUOTE_SCAN=207`）。`MirrorMessage.cs:88-112` 仍是「数字 body→信用卡 / 非数字→重载行会」两套 C# 旧语义；原生 `sub_658114` 的 37 位全局位图 swap 未移植（掩码来自帧第三 dword，无文本载体） |
| **SGRP-30** | MISSING | **PARTIAL** | 新增 `ISM_IDENT_247` 与 `MirrorMessage.cs:211-224` 的**显式空 case**（避免落 error sink 打印，那与原生不符）。但 handler 本体（`sub_65805C` 门 `len==0xD`、读三 dword、0x65808A `call 0x699310` 写日志/DB）**未移植**。派发面已在，本体仍缺 |
| **SPWN-56** | DIVERGENT | **PARTIAL（审计窗口内被推进）** | `e1b031a3` + `17ae7f39`（在 `69f049b6..4a59a361` 内落地）已把 `sub_765D64` 三项合取谓词移植为 `TBaseObject.NativeCellObjectValidity.cs` 的 `IsNativeStaleCellActor`，并接到三处 SearchViewRange 副本（`TPlayObject.Base.cs:1795`、`TBaseObject.ViewRange.cs:200/:307`、`RobotPlayObject.Base.cs:456`）。**但仍非 1:1**：C# 是 `age >= 60s ‖ !valid` 的**并联**，那条 60 秒 `dwAddTime` 规则是移植期自造的、原生没有的**额外**摘链条件（代码注释自述「两者不等价，故并联而非替换」）。原生只在 `!valid` 时摘链 |

**小结：23 → 16 闭合 / 7 仍开。** 其中 2 条（MOVE-73/74）比旧报告的「在建」更严重，因为发现了错接。

---

## 4. FAIL-CLOSED(10) 与 BLOCKED/UNPROVEN(47) 复核

对全部 57 条逐一检索 `git log ed39ef63..69f049b6`，只有 6 条有新工作。

### 4.1 已闭合（3 条，C 类 → FIXED）

| 契约 | 旧态 | 提交 | 本轮复核 |
|---|---|---|---|
| **ECON-17** | BLOCKED | `e2dd82a2` | 两条阻塞理由都被推翻并经我复核：① 0x644244 **确是函数头**（`push ebp / mov ebp,esp / add esp,-0x120`），门 0x644274 `cmp byte [eax+0x675],3` / `jbe 0x644457` 在它体内，不属 `sub_6441ED`；② **+0x675 = 权限**已坐实 —— 0x6B1E7B `call 0x65583C`(GetHumPermission) / 0x6B1E80 `mov byte [esi+0x675],al`。字段语义不再 UNPROVEN |
| **DURA-11** | BLOCKED | `b72c0604` + `42e43669` | 0x73EB0A `sub word [eax+0x26],dx`（**原始量、不 ×100**）/ 0x73EB15 `cmp ax,0x64` / 0x73EB19 `jb` 门槛 100 —— 与契约逐条对上。C# `NativeConsumeBujukCharm`（`TPlayObject.NativeAmuletConsume.cs` + `HeroObject.NativeAmuletConsume.cs`），接线 `MagicManager.cs:2186`、`HeroObject.cs:846`、`HeroObject.NativeDoSpell.cs:257` |
| **MOVE-57** | FAIL-CLOSED | `4843a4ab` | C# `UsrEngn.cs:704-719`：jitter 循环后先 `NativeGetRandomXY(Envir, …)`，**只有它也失败才**回退主城 —— 对上原生 0x6B9C6F `push 1/push 1` + 0x6B9C81 `call 0x7782D0` + 0x6B9C88 `jne 0x6B9D05`。旧 C# 直接回城会把本可就地落点的登录强行传回主城。**残留**：契约点名的两条控制台日志串未逐字复刻（C# 用 `sChangeServerFail2`/`sErrorEnvirIsNil`），非游戏可观测面 |

### 4.2 复核后维持 C 类（3 条有新证据，结论不变）

| 契约 | 提交 | 结论 |
|---|---|---|
| MOVE-54 | `9017403f` + `fd0555e8` | 复核为「落位中性，仅广播差」，维持 fail-closed 只报不改 |
| DURA-37..44 | `14e40f55` | **仅取证报告，无移植**。8 条仍 BLOCKED |
| SGRP-41 | `2aa89895` | 反汇编核验 + 注记，无行为改动，仍 BLOCKED |

### 4.3 其余 51 条

无任何新提交触及，**沿用旧判定**。本轮未逐条重新反汇编，故按铁律**不上调**。

> **C 类净变化：57 − 3 = 54。**

---

## 5. FAITHFUL 反向抽查（30 条）

**方法**：`tools/recount_sample.py`，固定种子 20260814，从 754 条中剔除上一版判定为
非 FAITHFUL 的 103 个 id 后随机抽 30。剩余池恰为 **651**，与上一版 FAITHFUL 计数吻合（自洽性校验通过）。
对每条回到底本核对 EVIDENCE 列点名的字节/地址。

**抽中的 30 条**：CGLD-18、CRAFT-07、CRAFT-25、CRAFT-47、DROP-31、DURA-22、ECON-18、ECON-36、
GILD-21、MFLG-11、MOVE-29、MOVE-87、MOVE-93、POIS-13、POIS-31、PRICE-05、PRICE-17、QST-01、
QST-04、SGRP-27、SGRP-43、SPWN-01、SPWN-08、SPWN-10、SPWN-25、STATE-11、STATE-43、TCFP-15、
TRADE-03、TRADE-53。

### 5.1 结果：**30 / 30 通过，未发现虚报**

逐条实测（节选最能证伪的几条）：

| 契约 | 契约主张 | 实测 |
|---|---|---|
| STATE-43 | 位图 `01 20 00 F5 00 20 20 00 08 86 C7 00 40 00 00 00` | 0x772664 dump **逐字节完全一致** |
| PRICE-17 | TEquipItem 槽 +0x20 raw `70 3D 78 00` | 0x75CAE8 = `70 3d 78 00` **精确命中** |
| MFLG-11 | MINE2 零命中；MINE 两命中于 0x775D74 / 0x776CA4 | MINE2 **0 命中**；MINE **2 命中，地址完全一致** |
| GILD-21 | `0x70633A C6 47 28 01` | 实测 `c6 47 28 01` ✓ |
| ECON-18 | 负数门 + 余额门 + 单次减 | 0x6C7D69 `test edx,edx`/0x6C7D6B `jl`；0x6C7D6D `cmp edx,[eax+0x15c]`/0x6C7D73 `jg`；0x6C7D75 `sub` ✓。C# `TPlayObject.cs:1463` 两门俱在 |
| MOVE-29 | kind 1/2/4 写点 0x764E45 / 0x7837AA / 0x77C285 | 分别 `c6 46 04 01` / `c6 43 04 02` / `c6 47 04 04` ✓ |
| POIS-13 | level 只与 4 比，恰两档 | 0x767AA5 `call 0x773BEC` / 0x767AAA `cmp eax,4` ✓ |
| PRICE-05 | 有界 8 项跳表 | 0x783DF2 `lea edx,[ebx+0x20]` / 0x783DF7 `cmp ecx,7` / `ja` / 0x783DFC `jmp [ecx*4+0x783E03]` ✓ |
| POIS-31 | 0x76B354 是真序言；0x76B360 会失步 | 序言 ✓；0x76B360 = `44 7a 00 00` = `inc esp / jp` **确实失步** ✓ |
| TCFP-15 | `cmp eax,0x30` + `setle`（闭区间 48） | ✓ 逐字节 |
| TRADE-03 | 互相面对门 | 0x6C3F49 `cmp ebx,eax`/`jne` ✓；C# `Operate.cs:1655` `TargetPlayObject.GetPoseCreate() == this` ✓ |

### 5.2 抽查中发现的问题（**不是虚报，但要记录**）

1. **账本 EVIDENCE 的地址常有几字节偏移**（3 例：CGLD-18 写 `0x6C6BCE mov eax,[ebp-8]`，实为
   0x6C6BCB，0x6C6BCE 是紧随的 `call 0x40C89C`；CRAFT-07 写 `0x74DA1E StrToIntDef`，实为
   0x74DA21，0x74DA1E 是 `mov eax,[ebp-0x18]`；SGRP-43 写 `0x658560 castleord select`，
   0x658560 实为**代码** `mov eax,0x658730`，串在 0x65873E）。**每次实质结论都站得住**，
   属账本记录精度问题，不影响判定。

2. **TRADE-53 的账本证据实质有误 —— 但项目已自行发现**。账本称 `0x783984 读 item[+0xF4]/[+0xF8]`；
   我实测 0x783984 = `33 C0`(xor eax,eax) / `C3`(ret)，是**恒返回 0 的桩**，真谓词在**相邻另一函数
   0x783988**。分片 05 与分片 17（TCFP-28 行）**各自独立**得出同一结论并据此判 C# 无条件发日志为忠实。
   这是一次成功的自查，不是虚报。

3. **DROP-31 的 "BLOCKED" 已过期**。账本称「`0x403B4C` 函数体本轮未 dump，Random 上界包含性不可判」。
   我本轮 dump 了：`imul edx,[0x7A2008],0x8088405 / inc edx / mov [0x7A2008],edx / mul edx / mov eax,edx`
   —— 取 64 位积的**高 32 位**，即 **上界排他 [0, Range)**。C# `DelphiRandom.Random`
   （`product = (uint)range * nextSeed; return (int)(uint)(product >> 32)`）**与之逐位相同**。
   该条本就该是 TIER1 FAITHFUL，不是 BLOCKED。

4. **计数口径问题（重要）**：抽中的 30 条里有 **4 条**（CRAFT-47、DROP-31、MOVE-93、QST-04）
   在账本里的 `strength` 是 BLOCKED/UNPROVEN，却被上一版计进了 FAITHFUL。其中 **CRAFT-47 的契约文本是
   「不存在关于 crafting 的 C# 结论：只读普查两次都没返回可用输出」** —— 这是一条关于**普查过程**的
   陈述，不含任何行为主张，把它计作「忠实」是类别错误（没有可忠实的对象）。
   全池统计：651 条中 **TIER1 611 / UNPROVEN 22 / INFERRED 12 / BLOCKED 6**，
   即 **40 条 FAITHFUL 建立在非 TIER1 证据上**。这是 §6 保守下界的来源。

### 5.3 抽查的统计效力（诚实说明）

30 / 651 = 4.6% 抽样率，0 失败。按「三法则」，95% 置信上界为 3/30 = **10%** —— 也就是说
**最多可能有约 65 条虚报仍未被这次抽查发现**。本节只能说「未发现虚报」，
**不能说「不存在虚报」**。若要把上界压到 3%，需抽约 100 条。

---

## 6. 诚实的百分比区间与口径

三种口径，都给分子分母：

| 口径 | 算式 | 值 | 说明 |
|---|---|---|---|
| **严格完成度** | (651 + 42) / 754 | **91.9%** | (FAITHFUL + FIXED)/总数。与上一版同口径，可直接对比：**89.4% → 91.9%** |
| **保守下界** | (611 + 42) / 754 | **86.6%** | 把 40 条非 TIER1 证据强度的 FAITHFUL 全部剔出 |
| **广义可交付度** | (651 + 42 + 54) / 754 | **99.1%** | 把 54 条 C 类全算成可交付。**这个数字不宜单独引用** |
| **真实行为缺口** | 7 / 754 | **0.93%** | 唯一「C# 行为确与原生有别且可证」的部分 |

**推荐表述**：**严格完成度 91.9%，诚实区间 86.6% – 91.9%**。
区间下界来自「40 条 FAITHFUL 的证据强度不是 TIER1」，上界来自「按上一版同口径直接对比」。
广义 99.1% 只在「把不可证/依赖 DBSvr 的都算作已交付」这一宽口径下成立，附带 §5.3 的抽样效力限制。

**与上一版的差异来源**（23 → 7）：

| 来源 | 条数 |
|---|--:|
| 真正被后续提交移植并接线闭合 | 9（MOVE-79/85/90/10/11、SPWN-13/14、DROP-33、SGRP-26/31 中的移植项） |
| 契约或旧判定本身有误，C# 本就正确 | 4（MOVE-82 归属错、STATE-19 前提错、POIS-11 根因已消、MOVE-89 原生死代码） |
| 补上不可达性/等价性证明后判为良性 | 1（MOVE-31） |
| 归类修正（MOVE-75 从"在建"落定为已闭合） | 1 |
| 另有 3 条从 C 类升为 FIXED（ECON-17 / DURA-11 / MOVE-57） | +3 |

> 注意：这里有 **4 条是「契约错了」而不是「代码改好了」**。这类闭合含金量与真正移植不同，
> 但它们确实消除了 C# 与原生的行为差异，计入完成度是正当的。

---

## 7. 仍未闭合项 · 按可做性三档排序

### 甲档 —— 可证且可做（应优先）：5 条

| 序 | 契约 | 卡点 | 建议 |
|---|---|---|---|
| **1** | **MOVE-74** | **错接**：0xB05 变迁广播挂在 `InSafeArea()` 上而非 `sub_768454` 穿透判定，且多一道原生没有的 `GuildWarList.Count > 0` 门；`Obj+0x3FE` 被 `m_boInSafeArea` 与 `m_boThroughOccupancyCache` 两个字段重复建模 | 先合并两字段为一个（以穿透判定为准），再把广播移到该字段的变迁点、去掉行会门。`Grobal2.cs:1044` 对 2821 的注释「Safe zone entry/exit notification」也要一并更正。**证据齐全，无未知量** |
| **2** | **MOVE-73** | 缓存刷新时机与写入条件与原生不同（用点刷新 + 无条件覆写 vs tick 刷新 + 仅变化时回写） | 与 1 一并做：把刷新放进玩家 tick，加「仅变化时回写」比较。`Message.cs` 已不再是禁改文件（MOVE-10/85/90 都已在其中接线），原先的阻塞理由已消失 |
| **3** | **MOVE-39** | 缺「清定时状态 0x17」；广播/落格次序与原生相反 | 清 0x17 属两个 mover 共有，应落在 `WalkTo` 而非人形钩子。前置：确认 internal type 23 在 `RemoveTimedAbilityInternal` 下的清除语义与 `sub_76B4D0 → sub_7731C0` 的节点摘除逐条对齐（`0x7731C0` 不只是 btr，还要按 `[node+1]` 匹配摘链） |
| **4** | **SPWN-56**〔窗口内已推进〕 | 原生谓词已移植，但仍与自造的 60 秒 `dwAddTime` 规则**并联** | 只差「删掉 60 秒那一半」。删之前须确认：托管侧 OS_MOVINGOBJECT 孤儿格子项是否还有别的 GC 路径 —— 这正是当初保留 60 秒规则的理由。若无，需另补一条与原生等价的清理，而不是直接留着非原生条件 |
| **5** | **SGRP-30** | 派发面已在（显式空 case），handler 本体未移植 | 需要三 dword 二进制帧载体。若跨服协议能带二进制体，`sub_65805C`(门 `len==0xD`) + `0x699310`(IntToStr 格式化 + 读 `[0x7D5C40]` 写日志) 的移植量不大 |

### 乙档 —— 可证但工程量大 / 需先改传输层：3 条

| 序 | 契约 | 卡点 |
|---|---|---|
| 6 | **SGRP-25**（202 反作弊惩罚） | 需要三件事同时到位：①跨服文本协议增加第三 dword（惩罚时长）载体；②补 `[+0x180c]` 到期字段与 `[+0x780]` 日期基址的 C# 映射；③补一个反作弊形态的 202 发送方（当前唯一 live 发送方是登出广播）。缺任一条都只能维持 fail-closed |
| 7 | **SGRP-44**（207 常量冲突 + 位图 swap） | 需要：①解开 `ISM_RELOADGUILD` / `ISM_SERVERSWITCH` 双 207 冲突（两者都有 live 发送方，直接改会破坏在用功能）；②37 位全局位图对象 `[0x7D7038]` 与逐位回调 `sub_658110`/`0x794F30` 在 C# 无模型；③掩码是 32 位二进制，文本协议不可表示 |
| 8 | **DURA-37..44**（8 条，C 类） | `14e40f55` 已产出取证报告但未移植。约 20 个 `+0x26` 写点的触发/槽/算术仍未定性。取得字节证据后即可从 C 类转为可移植项 |

### 丙档 —— 不可证 / 需运行期 / 有意偏离：维持现状

| 契约 | 理由 |
|---|---|
| MOVE-54 | 复核为落位中性、仅广播差，fail-closed |
| TRADE-61 / TRADE-44 / SGRP-35/40/41 | 依赖 DBSvr 运行期或对端配置，静态镜像不可证 |
| 其余 C.3 类 UNPROVEN 负命题（约 40 条） | 属逆向研究项而非移植缺口。**其中 DROP-31 本轮已解，应从该类移出**（见 §5.2-3） |

### 附：本轮顺带发现的非行为瑕疵

- `TBaseObject.cs:1490` 的局部量 `DropWide = _MIN(nDropItemRage, 7)` 在 `DropGoldDown` 内已成
  **死变量**（实际用的是 1500 行的硬编码 3）。仅代码卫生，无行为影响。
- 上一版报告 §2.1 的前缀明细有两处笔误（MINE / SPWN），见 §1。

---

## 8. 审计窗口内 master 的变动（`69f049b6..4a59a361`，19 提交）

本轮开工后并行车道推进了 master。已 rebase 并逐条核对这 19 个提交对本报告结论的影响：

| 提交簇 | 涉及 | 对本报告的影响 |
|---|---|---|
| `e1b031a3` `17ae7f39` `c777db2a` | **SPWN-56** | **改变裁决**：DIVERGENT → PARTIAL。`sub_765D64` 谓词已移植并接三处，但与自造 60 秒规则并联。见 §3.2、§7 甲档-4 |
| `0a2419ca` `54c4bcd5` `2e427b41` `288028e3` | **MOVE-11** | **不改变裁决**（仍 CLOSED）。契约本体 sub_6BCE2C 在 `69f049b6` 即闭合；窗口内补的是相邻的 run 臂 `sub_7742C0` 隐身揭示钩子，并更正了 SM 0x20 的错误归因 |
| `391a250b` `c7f1200a` `3e1f86bb` `4a59a361` | SGRP 214/219/220/221/226/240 | **不影响** SGRP-25/30/44（202/207/247）。已复核 `MirrorMessage.cs` 三处 case 状态未变。这些是账本 754 条之外或已计 FAITHFUL 的 ident |
| 其余 8 提交（眼神图谱/工具/docs） | 眼神插件子系统 | 不在本账本 754 条范围内（眼神有独立审计 `docs/yanshen_completeness_audit_20260814.md`） |

**计数不变**：SPWN-56 从 DIVERGENT 变 PARTIAL，仍在「未闭合」桶内，故 651 / 42 / 7 / 54 不变。

### 8.1 快照之后 master 仍在移动 —— 三条判定需要复检

本报告定稿时 master 已推进到 `c1bb5696`（比快照 `4a59a361` 又多 14 提交）。
我**没有**把这批纳入重算（否则永远追不上），但已识别出其中**明确触及本报告结论**的三处，
在此点名，供下一轮优先复检：

| 新提交 | 触及 | 对本报告的潜在影响 |
|---|---|---|
| `2a31c313` 撤回 ScatterRange 4→3、`832a7b1f` 删两处 nDropItemRage 读取、`a8156a32` 半径全表报告（并「订正 eqv_shard19 的误归属」） | **DROP-33** | 本报告 §3.1 判 DROP-33 CLOSED（四条腿半径全对），依据是 `69f049b6` 的代码。这批提交说**掉落控制的本体是 `sub_720278` 而非段 3**，并撤回了其中一处改动。**§3.1 的 DROP-33 行必须按新基线重验** |
| `8fbf2adc` 移植 MakePosion VMT+0xC8 槽、`f2ba7dc5` 让 legacy MakePosion 走该槽、`9d5ac26c` 让 debuff trap 走 MakePosion 而非 AddState、`345c45f4` 全管道图 | **STATE-19** | 本报告 §3.1 判 STATE-19 CLOSED，依据是 `7ee2a42d` 的「仅证据文档、不改代码」。这批是**真的改了代码**，说明该管道确有分歧未被 `7ee2a42d` 覆盖。**我核实的那两条事实（0x1F65 零命中、原生走 1000ms/0x283C 延迟消息）仍然成立**，但「整条管道忠实」这个更强的结论已被推翻 |
| `47578f71` fix(DURA-39)、`27b28c00` +0x26 写者全普查 | **DURA-37..44** | 本报告 §4.2 把 8 条整体记为「仅取证、无移植」。DURA-39 现已有 fix，普查也已完成。**至少 1 条应从 C 类转出，其余 7 条的卡点描述需按普查结果更新** |

**因此严格完成度 91.9% 是 `4a59a361` 时点的值，且很可能已偏低**（DURA-39 等在其后闭合）。
这三处复检完成前，不要把本报告的分片表当作最新真值。

---

## 9. 核对足迹

- 工作树：`git worktree add D:\loym2\.claude\wt3\recount -b w/recount master`（建时 `69f049b6`，
  完工前 rebase 到 `4a59a361`）。
- 反汇编工具本轮新建于 `tools/recount_dis.py`（dis / bytes / find / dstr / sfind 五个子命令），
  抽样器 `tools/recount_sample.py`（固定种子，可复现）。
- 账本条数经脚本核对：755 行 − 1 表头 = 754，754 个唯一 id，前缀和 = 754。
- 29 分片行内和均 = 26，总和 = 754，经脚本校验无失衡。
- 本报告**不改任何业务代码**，只新增本文件、`docs/_recount_notes.md` 与 `tools/` 下两个只读脚本。
