# 战神引擎 M2Server → C# 1:1 复刻 · 完成度刷新报告（两条战线）

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\audit3`　分支：`w/audit3`
- **审计 HEAD：`4a59a361`**（`docs(SGRP): full ProcessOthGsMsg dispatch dump and per-ident verdicts`）
  —— 建树时 `master` 是 `075d11ec`，审计过程中并行车道又推进了 21 个提交，本分支已 `git rebase master` 校准到 `4a59a361`，全部数字按此 HEAD 重测
- 仓库真实根：`D:\loym2\LyoMir2-master`（`D:\loym2` 本身不是 git 仓库，`.git.broken-20260810` 是废弃目录）
- 刷新窗口：`ed39ef63..4a59a361` = **113 个提交**（`ed39ef63` 是主报告 `docs/completeness_audit_20260814.md` 的 HEAD）
- A/B 基线工作树：`D:\loym2\.claude\wt2\abbase`（detached @ `ed39ef63`）、`D:\loym2\.claude\wt2\hb1`（二分用）
- 底本：`staging/_reunpack_work/flat_image.bin`（base `0x400000`）、
  `staging/yanshen208_strparam_runtime_dump_20260719/…`（base `0x10000000`）、
  `staging/yanshen208_strparam_runtime_dump_delayed_20260719/…`（base `0x57C40000`，**唯一含 Themida 远端区的一份**）
- 工具：`dotnet 10.0.302`、python 3.11.9 + capstone 5.0.7
- **纪律：本轮只读审计，未改任何 `.cs`（`git show --stat HEAD` 无 `.cs`）；提交只含本文档与四份复现脚本。所有数字逐条可回溯到提交哈希 / 原生 VA / 工具输出。不虚报。**

---

## 0. 执行摘要（结论先行）

| 战线 | 分母 | 旧数（旧报告） | **新数（严格）** | **新数（含 EQUIVALENT-BY-ABSENCE）** | 真实可证行为缺口 |
|---|---:|---|---:|---:|---:|
| **主引擎（等价账本）** | 754 | 89.4%（674/754） | **92.2%（695/754）** | **92.4%（697/754）** | **2 条 = 0.3%** |
| **Yanshen 插件** | 660 | 57.0%（376/660，区间 57.0~59.7%） | **63.3%（418/660）** | **66.4%（438/660）**〔已逐键验证的 20 条〕<br>**72.9%（481/660）**〔全库补丁图谱判定的 63 条〕 | **26.1%（严格）/ 19.5%（含图谱等价）** |

`AuditTools` 全量（428 个工程）：**366 PASS / 61 FAIL / 1 工具源码编译不过**。
61 个 FAIL 的三段 A/B 归因：**56 条在 `ed39ef63` 基线上同样失败**；**3 条在 `ed39ef63..075d11ec` 引入**；
**2 条在 `075d11ec..4a59a361` 引入，已二分定位到 `17ae7f39`**。**5 条新引入的失败无一是行为回归，全部是回归网自身的问题。**

**一句话**：主引擎"真实 C# 行为与原版有别"的缺口已从 23 条压到 **2 条**（MOVE-74 穿透态广播未发、MOVE-73 缓存刷新时机），
距 100% 的阻碍整体从"移植工作量"转成了"证据边界"（43 条 UNPROVEN + 12 条有意 fail-closed）。
Yanshen 涨了 6.3 个点，并且第一次有了**二进制侧的第二根轴**（全库补丁图谱），
它把"到底有没有东西可移植"这个问题从猜测变成了可判定——**代价是必须同时报三个数，因为三根尺子量的不是同一件事**。
两条战线共同的、也是唯一的硬瓶颈是 **Themida 远端调用链**。

---

## 1. 主引擎：754 条账本刷新

### 1.1 口径定义（与旧报告严格可比，仅新增一个桶）

| 桶 | 含义 |
|---|---|
| `FAITHFUL` | 原生字节佐证下 C# 行为等价，未动代码 |
| `FIXED` | 本轮或前序会话按字节外科修复并已合入 `master` |
| `EQUIVALENT-BY-ABSENCE` | **本报告新增**：原生该行为**可证不可达**（死代码 / 谓词恒假），C# 同样不可达 ⇒ 本就 1:1，写代码反而是臆造 |
| `MISSING` | 原生有行为、C# 无 |
| `DIVERGENT` | 两边都有行为但不一致 |
| `FAIL-CLOSED` | C# 有意拒绝执行 / 保留自洽语义，证据充分但不可安全移植 |
| `BLOCKED / UNPROVEN` | 静态镜像不可判或依赖 DBSvr 运行期，C# 未臆造 |

- **严格完成度** = `(FAITHFUL + FIXED) / 754`
- **含等价完成度** = `(FAITHFUL + FIXED + EQUIVALENT-BY-ABSENCE) / 754`

### 1.2 状态迁移表（`ed39ef63` → `4a59a361`）

| 状态 | 旧（`ed39ef63`） | 迁出 | 迁入 | **新（`4a59a361`）** |
|---|---:|---:|---:|---:|
| FAITHFUL | 651 | 0 | +5 | **656** |
| FIXED | 23 | 0 | +16 | **39** |
| EQUIVALENT-BY-ABSENCE | — | — | +2 | **2** |
| MISSING | 12 | −12 | +1 | **1** |
| DIVERGENT | 8 | −8 | +1 | **1** |
| FAIL-CLOSED | 10 | −1 | +3 | **12** |
| BLOCKED / UNPROVEN | 47 | −4 | 0 | **43** |
| NOTHROUGH 在建 | 3 | −3 | 0 | **0** |
| **合计** | **754** | | | **754** ✓ |

校验：`656 + 39 + 2 + 1 + 1 + 12 + 43 = 754` ✓

```
严格完成度   = (656 + 39) / 754 = 695 / 754 = 92.2%      （旧 89.4%，+2.8pt）
含等价完成度 = (656 + 39 + 2) / 754 = 697 / 754 = 92.4%
广义可交付度 = (656+39+2+12+43) / 754 = 752 / 754 = 99.7%
真实行为缺口 = (1 MISSING + 1 DIVERGENT) / 754 = 2 / 754 = 0.3%   （旧 23 条 / 3.0%）
```

### 1.3 本窗口状态迁移明细（26 条，逐条附提交哈希与代码落点）

> 迁移守恒：`FIXED +16`（§1.3.1~1.3.4）+ `FAITHFUL +5`（§1.3.5）+ `EQUIVALENT-BY-ABSENCE +2`（§1.3.6）
> + `FAIL-CLOSED +3`（§1.3.7）= **26 条**；其中 `MOVE-57` 由 FAIL-CLOSED 迁出，故 FAIL-CLOSED 净 `+2`。

#### 1.3.1 MISSING → FIXED（8 条）

| 契约 | 提交 | 代码落点（本轮实测） | 摘要 |
|---|---|---|---|
| MOVE-10 | `b5a45030` + `9294fdb3` | `TPlayObject.Message.cs:1487 / :1580` | state 0x34（双人坐骑乘客态）静默移动闸；只挂 CM_WALK/CM_RUN，不挂 TURN/SITDOWN（跳表 `0x6D8592` 证实只有 2 臂带闸） |
| MOVE-11 | `a9cc64f2` → `0a2419ca` / `288028e3` | `TPlayObject.Attack.cs:767 / :905`、`TBaseObject.NativeStealthBreak.cs` | walk/run 补 `sub_6BCE2C` 三连取消挂起通道；分片"依赖未建模外观子系统"被两轮证伪（`0x20` 来自相邻的 `sub_6BCE54`，真正的外观腿是 `sub_7742C0` 隐身揭示，已于 `288028e3` 接线到 CM_RUN 臂） |
| MOVE-39 | `9ae059ee` | `TBaseObject.cs:1334` + `TPlayObject.NativeWalkPartnerFollow.cs` | 人形 mover 尾 `0x741350 call sub_6BBEE4`；分片"英雄跟随"被证伪，实为 `[+0x3C0]` 双人坐骑同伴 |
| MOVE-79 | `b7b8bbd1` | `TPlayObject.NativeTianDiHeYi.cs` + `Command/Commands/TianDiHeYiCommand.cs` | 天地合一全链（命令体 `sub_6C7B28` + 执行 `sub_7274B4` + 切换 idx23/24） |
| MOVE-85 | `8298f9ef` + `3ab0059a` | `TPlayObject.NativeMapEntryStatus.cs` + `Message.cs` ×3 | 进图状态通告 `sub_6B6BEC`（超负重 + 三档巅峰战神状态） |
| MOVE-90 | `597075b9` + `b82c7142` | `TPlayObject.NativeNoMagicMap.cs` + `Message.cs` | NOMAGIC 唯一 reader `0x6DA12B`，置位则静默拒绝（只发 `0x276`） |
| SPWN-13 | `be5126b0` + `39ebe3b9` + `a5a34bcb` | `UsrEngn.cs:3401-3409` | `[gen+0x28]` 尸体存留秒数 → `word[obj+0x38]`；消费端改读 `word[obj+0x38]`，撤销 SPAWN-32 的 `dwZenTime/dwMakeGhostTime` 门 |
| SPWN-14 | `be5126b0` + `39ebe3b9` | `UsrEngn.cs:3407-3409` | `[gen+0x40]` BOSS 生成播报（`wIdent=0x64 / wParam=0x38FF`） |

#### 1.3.2 DIVERGENT → FIXED（4 条）

| 契约 | 提交 | 摘要 |
|---|---|---|
| DROP-33 | `9584a44f` / `20690091` | 怪物 own-table 腿散落半径改原生硬编码 3（`0x71FDCF` / `0x71FE46 mov ecx,3`）；`DropItemRage` 三编码全镜像 0 命中 |
| SGRP-26 | `31d80e5a` | 217/218 = 师徒（`sub_657CF0` / `sub_657AC0`），旧 C# 是好友空桩 |
| SGRP-31 | `31d80e5a` | 241 = 信用卡全清（`sub_655A18` 无条件 `ResetOnlineAll`），移除 C# 自造的行会战分支 |
| **SPWN-56** | `e1b031a3` + `17ae7f39` + `c777db2a` | 移植 `sub_765D64` 三项合取有效性谓词（`Length(CName)!=0 && PEnvir!=nil && PEnvir.MapName!=''`），**并联（OR）**进四处 `SearchViewRange` 的摘链条件而非替换 60s 老化规则。OR 是单调的，不会驱逐"有名字、有地图"的活体，因此保住了托管侧唯一的孤儿格子 GC。`drop_view_residual §2` 的"照搬即净回归"结论只对"替换"成立，已标记 superseded。**⚠ 该提交带来两条回归网失败，见 §3.3.3** |

#### 1.3.3 FAIL-CLOSED / BLOCKED → FIXED（3 条）

| 契约 | 提交 | 摘要 |
|---|---|---|
| MOVE-57 | `4843a4ab` | 登录放置 jitter 耗尽后补当前图 `GetRandomXY`，失败才回城 |
| ECON-17 | `e2dd82a2` | 分片 01 两条封锁理由逐字节推翻：`+0x675 = m_btPermission`（setter `0x6B1E80`），守卫函数 `sub_644244` 就是 `ClientBuyItem` 的孪生臂；补齐 property-9 商人的 GM 专用寄存子系统（`Merchant.NativeStorageNpc.cs`） |
| DURA-11 | `b72c0604` + `42e43669` | 第二护身符消耗例程 `sub_73EA20` 移植 + SKILL_62 接线（`TPlayObject.NativeAmuletConsume.cs` / `HeroObject.NativeAmuletConsume.cs`） |

#### 1.3.4 NOTHROUGH 在建 → FIXED（1 条）

| 契约 | 提交 | 摘要 |
|---|---|---|
| MOVE-75 | `aeadf8f5`（前序并入）+ `35c9a6ed` | `sub_772EB8` 无条件穿透授予（`m_boObMode \|\| InBodyState(0x3C)`）落 `HasNativeCellPassThroughGrant()`，且优先于 NOTHROUGH；`35c9a6ed` 把 run mover 的 `boIgnoreOccupancy` 也接到缓存 `Obj+0x3FE`，与 walk 对齐 |

#### 1.3.5 → FAITHFUL（5 条，复核上修，无代码改动或仅移除自造物）

| 契约 | 旧判 | 提交 | 依据 |
|---|---|---|---|
| POIS-11 | DIVERGENT | `02a76791` | 分片注记"结构 fork 仍存"已过时——并行存储 `ushort[12]` 已删，`LegacyStatusTimeView` 是无存储门面；`31 − nType` 映射经 VMT+0xC8 全镜像 63 站点普查逐点吻合 |
| STATE-19 | DIVERGENT | `7ee2a42d` | 两条前提均不成立：原生施毒同样走 1000ms 延迟自消息（`10300`），C# `RM_POISON=8037` 与之是同一条**永不上线**的内部消息；legacy 计时槽已删 |
| MOVE-82 | MISSING | `b7b8bbd1` | 分片"NORANDOMMOVE 发『在这里你无法使用』"被证伪：`sub_7855F8` 的 NORANDOMMOVE 分支 `@0x785742` 是静默 no-op，串 `0x785864` 实由 FOXMAP/NODRUG 触发；C# 本就静默 |
| DURA-39 | UNPROVEN | `14e40f55` + `1cc0c42e` | "穿戴装备是否掉耐久"由 UNPROVEN 变为已证**掉**：`sub_73F9FC → sub_73FBE8 → sub_75EBC0`（0..15 槽循环）→ `sub_75EA40`（`Random(8)` 门 / `sub word[item+0x26],ax` / 归零销毁）。C# 16 槽循环有据；`ac102d67` 另删掉 slot0 冗余双滚 |
| DURA-44 | UNPROVEN | `14e40f55` | ~20 个 `+0x26` 写者全部逐个反汇编定身份：7 已忠实移植、10 合理 fail-closed、3 运行期不可解析；无一条属"功能已移植但漏耐久写" |

#### 1.3.6 → EQUIVALENT-BY-ABSENCE（2 条）

| 契约 | 旧判 | 提交 | 依据 |
|---|---|---|---|
| MOVE-31 | MISSING | `de97da32` | `sub_765D64` 的三个条件在托管模型下**全部不可达**（全仓 `m_PEnvir = null` 只有字段初始化器；`m_sCharName` 无清空点）⇒ 探针恒真 ⇒ 可观测行为已等价。**注意**：`17ae7f39` 之后这条的前提部分改变了——见 §3.3.3 |
| MOVE-89 | MISSING | `4f342eab` + `597075b9` + `b84eba38` | `TTimerBomb` 的 classptr `0x781304` 全镜像只出现在自身 vmtSelfPtr / RTTI，不在物品工厂跳表 `0x74D07B`（只按 `byte[StdItem+0x15]` 派发 0..10），无 FindClass 引用 ⇒ **原生从不实例化**。C# 已 1:1 移植方法体并**刻意不接线**，与原生同处不可达态 |

#### 1.3.7 → FAIL-CLOSED（3 条，证据充分但不可安全移植）

| 契约 | 旧判 | 提交 | 依据 |
|---|---|---|---|
| SGRP-25（202） | DIVERGENT | `2aa89895` | 原生是反作弊惩罚（`sub_653ED0`，`'SD000'@0x65402C`）。惩罚时长来自帧第三 dword（C# 跨服是文本协议，无载体）、到期字段 `[+0x180c]`/日期基址 `[+0x780]` 全仓无对应成员、唯一 live 发送方是 `UsrEngn.cs:1568`（禁改）的登出广播 |
| SGRP-44（207） | DIVERGENT | `2aa89895` | 原生是全局 40-bit 位图 swap（`[0x7D7038]` + 逐位回调 `sub_658110`）。掩码来自帧第三 dword；C# 现有两条语义（信用卡 switchWord / 重载行会）**各自都有 live 发送方**，移除会破坏在用功能 |
| SGRP-30（247） | MISSING | `2aa89895` | 原生 `sub_65805C` 是真实 handler（`len==0xD` 门 + 三 dword → `0x699310` 日志/DB）。C# 新增**显式 no-op case**（而非落默认 error sink），与"原生是真实 handler、不打印 Ident 未知"一致；二进制 body 在文本协议里无载体，且全仓无 247 发送方（SGRP-41） |

### 1.4 仍未闭合 —— 三类清单（57 条 = A 2 + B 0 + C 55）

#### A 类 —— 真实可证行为缺口（2 条，**这是距 100% 唯一真正欠的移植工作量**）

| 契约 | 状态 | 证据 | 现状 / 阻碍 |
|---|---|---|---|
| **MOVE-74** | MISSING | 穿透缓存每次跃迁经 `vmt+0x250` 发 `0xB05`(2821)：TRUE `push 6/1/0/0`，FALSE `push 6/0/0/0` | `TPlayObject.NativePassThrough.cs:38/41/148` 明写"SM_2821 变化广播暂不复刻"。**纯发包，无状态依赖，是两条里最容易补的一条** |
| **MOVE-73** | DIVERGENT | 判定是玩家 tick `sub_6B2D38` 内的缓存（`0x6B308E` 重算 / `0x6B3096` 比旧值 / `0x6B30A3` 变了才回写） | C# 改在"移动使用点"刷新（`NativeRefreshThroughOccupancyCache`，walk + 3 个 run mover 入口各一次）。判定值一致，**刷新时机不同**；忠实化要求能在 `Run()` 里挂 tick，而 `TPlayObject.Message.cs` 禁改 —— **这是流程闸门问题，不是技术难题** |

> **B 类（大子系统重写）本轮清零。** 旧报告 B 类 20 条已全部落定：MirrorMessage 跨服 5 条（2 修 3 有据 fail-closed）、
> 随机传送/天地合一族 5 条（3 修 1 上修 FAITHFUL 1 等价）、移动闸 2 条（全修）、NOTHROUGH 3 条（1 修 1 分歧 1 缺失）、
> SPWN-56 1 条（**已修**）、DROP-33 1 条（修）、STATE-19 1 条（上修）、SPWN-13/14 2 条（修）。

#### C 类 —— 不可证 / 有意 fail-closed（55 条 = FAIL-CLOSED 12 + BLOCKED/UNPROVEN 43）

> §1.3.6 的 2 条 `EQUIVALENT-BY-ABSENCE` 已证等价，**不计入未闭合**。

**C.1 FAIL-CLOSED（12）** ── C# 有意更安全或保留自洽语义，均带字节证据注释

`TRADE-44`（DrugStore 需 DBSvr 后端）、`TRADE-47`（30s 半清扫改每 tick 全量取消，更安全）、
`POIS-16`（家族清除随 TKingOfIceMon 一并 fail-closed）、`POIS-27` / `POIS-30`（抗性 `<=6` 跨切面谓词未反编译 / legacy 投影在禁改 `Grobal2`）、
`STATE-31` / `STATE-49`（深层消费者语义 UNPROVEN / 低 band 故意未命名）、`SPWN-29`（防沉迷播报参数不可证）、
`MOVE-54`（创物同图占位复检 —— `fd0555e8` 复核为**落位中性、仅广播差**）、
`SGRP-25` / `SGRP-44` / `SGRP-30`（见 §1.3.7）

**C.2 BLOCKED / UNPROVEN（43）**

| 组 | 条数 | 一句话瓶颈 |
|---|---:|---|
| DBSvr 运行期依赖（`TRADE-61`、`SGRP-35/40/41`） | 4 | 仓储层级表 `[0x7D6608]` / 金币上限 `[0x7D6080]` 运行期加载；OthGs 202..257 无 M2Server 发送方，缺 DBSvr 侧不可配对 |
| 装备耐久扩展写者残余（`DURA-37/38/40/41/42/43`、`DURA-23`） | 7 | 写者身份与算术已证（`14e40f55`），但入口 / 可达性 / base 寄存器 / 装卸网络 opcode / 超重后果仍未捕获 |
| UNPROVEN 负命题 / 证据缺口 | 32 | `ECON-38/39/40`、`PRICE-24/25`、`CRAFT-46`、`POIS-32/33/34`、`STATE-50`、`MINE-56/57/58`、`GILD-29`、`DROP-30/37`、`MOVE-94/95/96/97`、`SPWN-16/22/30/31`、`SPWN-45/47/55/57`、`QST-27/28/31/32` |

### 1.5 ⚠ 分母风险：账本对跨服 OthGs 的覆盖不全（不改数字，但必须记）

`4a59a361` 的 `docs/mirror_crossserver_20260814.md` 把 `ProcessOthGsMsg`（**订正：`0x657110` 是函数体首字节，不是跳表**；
真跳表在函数内 `0x657160` 索引表 + `0x657198` 地址表，27 REAL / 29 SINK）全 27 个 handler 逐条 dump 完，
查出 **8 个此前没人看过的 ident 存在分歧**：

| ident | 原生真实语义 | C# 现状 | 报告判定 |
|---|---|---|---|
| 203 | 私聊转发，第三 dword 低字 = 发信人等级 | Tag 恒 0 | DIVERGENT |
| 209 | `sub_621B14(名, 第三dword)` | `ExecCmd("Shutup", null)` 占位 | DIVERGENT |
| 210 | `sub_621CE4(名)` 解除禁言 | 空 | MISSING |
| 212 | `sub_65B6E0(名串)` —— **带参** | `CastleManager.Initialize()` 无参 | DIVERGENT |
| 214 | 按第三 dword 三路 switch 写全局 `[[0x7D6010]]` | 空 | BLOCKED |
| 224 | **师徒声望奖励** `[师父+0x4F0] += 第三dword` | `MsgGetMarketOpen(true)` 空 | BLOCKED |
| 227 | 给指定玩家发文本通知 | `LocalDB.LoadMakeItem()` | DIVERGENT |
| 228 | **师徒充值奖励** 第三dword>=1000 → `sub_6C03F8` | 行会成员召回 | DIVERGENT |

**本轮已核实：等价账本里没有任何一条契约覆盖这 8 个 ident**（逐 ident 全表串匹配，只有 `203` 命中 `CGLD-20`，
但那是 GM 命令索引 203、不是 OthGs ident，属假阳性；`212` 命中 `SGRP-10`，而 SGRP-10 断言的只是"212 有真实函数体"，
mirror2 证实成立，故 SGRP-10 维持 FAITHFUL）。

⇒ **这是账本的覆盖缺口，不是 754 条的计数错误**，所以 92.2% 不需要下调。但它说明一件事：
**分母本身不完整**。同期 `4a59a361` 一轮就新修了 4 个 ident（219/221/226/240，其中 226/240 此前连 `case` 都没有、
直接落 error sink 打印 `[Error]: ProcessOthGsMsg Ident=226`）。建议给 SGRP 段补 8~12 条新契约号后重算。

### 1.6 账本外的额外加固（不计入 754 分母，登记以免遗漏）

| 项 | 提交 | 摘要 |
|---|---|---|
| `POIS-39` | `a4159547` + `186ef170` | 物理落伤 `sub_73F8E0`（VMT+0x1AC）；`self+0x2DC` 百分比物理减伤聚合字段落地后解锁 |
| `GATE-01/02/03` + 连接点门 | `be2173ad` / `f157d901` / `3d7ee975` / `80c18a75` | 77BBAA33 传输帧 16 字节头；MapInfo 连接点补目标格 `attribute==0` 校验 + 无效连接点告警 |
| `CompareLStr` | `52acf458` | 改纯 ASCII UpCase 对齐 `0x4034D4` |
| `MOVE-41` | `1f95ba6e` | CM_RUN(3013) mover 尾 `0x767683` 补双人坐骑同伴跟随 |
| `MFLG` 残口 | `7aab71fe` + `b5936ebd` | 配置解析器补 8 个真地图旗标 token（含 `UserNoKill byte[+0x71]` + `word[+0x74]`）；`@TempSetMapParam` parser A 同批扩展；`pickup` 改前缀比较 + Delphi `Trim` 空串语义 |
| `SKILL-62` | `64a988bd` | 护身符消耗改召唤前无条件执行 + 落地 `0x76EEF4` 圣兽召唤原语；冷却戳唯一写点改 `HolyMonster.Die` |
| `HERO-MAGIC-1/2/3` | `e14c6eb2` / `f3b0d605` / `2c253e5c` | 英雄侧护身符例程 + 逐技能分派器 `sub_68DD88` |
| `MakeGhost` | `4520d53d` | 移除 `m_boCanReAlive` 分叉（战神 + 两份 Delphi 三方皆无 ⇒ INVENTED） |
| 挖肉三守卫 | `2f322c8f` | `sub_71ED80` 补目标校验（`cx=2` 硬门）/ 修正皮革边界（`>=0` 才 return）/ 补 `m_boNoItem` 门 |
| `DURA-16` / `U_DRESS` | `1cc0c42e` / `ac102d67` | 受击掉甲：证明原生确有 16 槽间接写者（fail-closed 保留循环）+ 删除 slot0 冗余双滚 |
| `SPWN-04` | `39ebe3b9` | 删除刷怪 Phase-B 门里自造的 `&& !boVentureServer` |
| OthGs 219/221/226/240 | `c7f1200a` / `391a250b` / `3e1f86bb` | 见 §1.5，账本无对应契约号 |

---

## 2. Yanshen：660 条口径重算

### 2.1 三把尺子，三个数（**这是本轮最重要的口径变更，请先读**）

本窗口出现了一根全新的、**二进制侧**的轴，它第一次让"原版到底有没有东西可移植"变成可判定的问题。
`ys_gui_matrix` / `ys_key_reachability` 只看本仓 C# 源码——一个 `LABEL_ONLY` 既可能是真缺口，
也可能这个开关**本来就不做事**。补上的两根二进制信号是：

- **原生补丁**：键是否出现在重建后的补丁站点图谱里（407 个安装点 → 107 个特性，
  `docs/ys_patch_sites_atlas.tsv`），并入 `extreme-map` 96 个（apply 臂用 `mov [绝对地址],eax` 直写，无安装点调用）
  与 `g11` 12 个（立即数宽度改写）。三者并集 = 214 个键有补丁目标。
- **插件读取**：加载器把布尔开关编码成 `rand()%1000+1000`（开）/ 小模数（关），
  故任何消费者都是 `cmp dword [reg+OFF],0x1F4`；全库单遍扫描按 `OFF` 归桶，
  `OFF → 键名` 由序列化器 `sub_10004140` 给出，排除序列化器与加载器自身区间。
  **"无消费者"只对 delayed 转储断言**（另一份的 16 MB Themida 区全零）。

于是本报告必须同时给三个数：

```
口径①  严格          = FAITHFUL / 660
口径②  含已验证等价   = (FAITHFUL + 20) / 660     ← 20 = 眼神2第1页逐键验证、且有回归网守住
口径③  含图谱等价     = (FAITHFUL + 63) / 660     ← 63 = 全库图谱判定 EQUIVALENT_BY_ABSENCE ∧ C# LABEL_ONLY
```

**为什么口径②不直接用 63**：`ys_b1_yanshen2_page1` 那 20 条是**逐键**做过
"任意 `mov`/`cmp` 读该位移"的额外普查（因为数值型键不走 `cmp …,0x1F4`），
并由 `AuditTools/YanshenPage1CensusCheck`（含**反臆造闸门**：这 20 个键的 30 个 `YanshenApi` 访问器
一旦被任何引擎/脚本 `.cs` 点名，检查立刻变红）钉死。全库版只跑了布尔判据，
**已知对非布尔键有假阴性**——证据就在本轮的交叉表里：16 个被判 `EQUIVALENT_BY_ABSENCE` 的键
在 C# 侧是 IMPLEMENTED（见 §2.3）。所以口径③是**上界情景**，不是结论。

两法一致性检验（很硬的一条）：**20 条是 63 条的严格子集，无一例外**；
`眼神2(第1页)` 那 9 条 fail-closed 键在图谱里恰好全部是 `PLUGIN_SIDE_ONLY`（插件读、不打补丁），
与"消费者在插件自己的 `sub_100795C0` 里"逐条吻合；图谱作者自述在第 1 页 34 个键上与逐键扫描 **0 处不一致**。

### 2.2 分母基线的两处校正

1. **`tools/ys_key_reachability.py` 修正的漏报必须纳入。** 旧矩阵 `ys_gui_matrix.py` 的
   `accessor_consumers()` 只给**键名字面量持有者**播种，于是**中继方法**（自身不含键名、
   但被行为文件调用的 `YanshenApi` 成员）永远进不了活性图。新工具改成
   「被行为文件点名的任意 `YanshenApi` 成员」再沿调用图传播。本轮在 `4a59a361` 上重跑：

   ```
   全库          {'IMPLEMENTED': 222, 'SCRIPT_ONLY': 19, 'LABEL_ONLY': 138, 'MISSING': 1}   n=380
   盘古3          IMPLEMENTED 33 / SCRIPT_ONLY 1 / LABEL_ONLY 5                              n=39
   眼神2(第1页)   IMPLEMENTED  0 / SCRIPT_ONLY 5 / LABEL_ONLY 29                             n=34
   眼神2(第2页)   IMPLEMENTED  6 / SCRIPT_ONLY 2 / LABEL_ONLY 18                             n=26
   ```

   对照：`ys-page2` 在 `38c5f107` 上是 `IMPLEMENTED 219 / LABEL_ONLY 141`；差的 **+3** 正是
   `装备提升人物爆率` + `_A值` + `_B值` 三键在 `6bcdab25` 完成 `MonGetRandomItems` 段2 分母接线。
   盘古3 从报告里的 30/39 走到 **33/39**。
   > 注意 `docs/ys_patch_completeness.tsv` 的 `matrix_state` 列是 **8-13 的旧矩阵快照**
   > （`LABEL_ONLY 184 / IMPLEMENTED 173`），因此它的 `NATIVE_GAP=61` 是**用旧 C# 轴算的**，
   > 本报告一律改用新轴重新交叉，不直接引用 61。

2. **触发注册表 `Wired` 实测。** `YanshenTriggerDispatch.cs` 21 条记录：**`Wired=true` 12 / `false` 9**
   （旧审计 8 / 13）。新接通 4 条 = `死亡触发` `回城按钮触发` `被击杀触发` `捡物触发`
   （`d72cc932` / `f06e19fb` / `bd77fcaa`）。仍未接 9 条，其中 8 条的行为文件**只有注册表本身**
   （纯静态数据，运行期不发射）⇒ 从 IMPLEMENTED 降 `PARTIAL`；`刀刀切割` 另有真实落点，
   沿用旧审计判 FAITHFUL 并把 `@Cutting`(`0x767BAE→0x767BB4`) 未接单列为残口。

### 2.3 两轴交叉表（`4a59a361` 实测，380 键）

行 = C# 侧 `ys_key_reachability`；列 = 二进制侧 `ys_patch_completeness` 判定。

| C# ＼ 二进制 | NATIVE_OK | NATIVE_GAP | PARAM_OF_PATCHED | PLUGIN_SIDE_ONLY | EQV_BY_ABSENCE | 合计 |
|---|---:|---:|---:|---:|---:|---:|
| IMPLEMENTED | 153 | 18 | 30 | 5 | **16** | 222 |
| SCRIPT_ONLY | 0 | 9 | 0 | 7 | 3 | 19 |
| LABEL_ONLY | 0 | 33 | 7 | 35 | **63** | 138 |
| MISSING | 0 | 1 | 0 | 0 | 0 | 1 |
| **合计** | **153** | **61** | **37** | **47** | **82** | **380** |

三处必须读出来的信息：

1. **`NATIVE_GAP ∧ IMPLEMENTED = 18`** —— 图谱说"插件改了 M2Server、本仓没落点"，
   但新 C# 轴说已实现。逐条看就明白了：`战士合击` `法道合击` `中毒时间上限` `脚本控制人物爆率`
   `屏蔽排行榜` `装备提升人物爆率` 等，全是**本窗口刚落地的盘古3 那批**。
   ⇒ 图谱的 `NATIVE_GAP=61` 用的是旧 C# 轴，**真实缺口是 33+9+1 = 43，不是 61**。
2. **`EQV_BY_ABSENCE ∧ IMPLEMENTED = 16`** —— C# 实现了插件既不打补丁、也不读取的东西。
   其中 12 条有独立佐证（`修改召唤神兽` 一族 10 条由页面对象构造函数
   `[edi+0x66C..0x68C] = 42/45/48/神兽/白虎/月灵/2/2/2` 佐证，见 `ys_b1_pangu3 §1`；
   `全局循环函数`/`循环时间_值` 由节拍器 `0x1008C7C0` + 周期 `+0x938` 佐证），
   说明布尔判据对**非布尔键有假阴性**。剩下 **4 条**（`最大装备数量` `红名K值` `非红名K值` `随机极品`）
   图谱作者已明确标为"需人工确认是否属于原生 M2Server 行为而非插件行为"，本报告单列为**待裁决**，
   **从 FAITHFUL 中扣除**（宁可低报）。
3. **`NATIVE_OK ∧ (LABEL_ONLY | MISSING) = 0`** —— 没有"插件改了、C# 只有 DTO"的漏网，
   两轴在这个方向上完全一致。

### 2.4 六个面的刷新判定

| 面 | 条目 | FAITHFUL | PARTIAL | **EQV-BY-ABSENCE** | MISSING | FAIL-CLOSED | UNPROVEN | 待裁决 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| F1 配置键 → 宿主行为 | 380 | **210** | 27 | **20** | 99 | 14 | 6 | 4 |
| F2 脚本 API 名 | 125 | **89** | 0 | 0 | 17 | 1 | 18 | 0 |
| F3 S 变量坐标组 | 34 | **3** | 1 | 0 | 27 | 0 | 3 | 0 |
| F4 回收语义契约 | 28 | **24** | 1 | 0 | 0 | 0 | 3 | 0 |
| F5 协议 / 结构断言 | 21 | **21** | 0 | 0 | 0 | 0 | 0 | 0 |
| F6 `!!!!` 命令隧道 | 72 | **71** | 0 | 0 | 0 | 1 | 0 | 0 |
| **合计** | **660** | **418** | **29** | **20** | **143** | **16** | **30** | **4** |

校验：`418 + 29 + 20 + 143 + 16 + 30 + 4 = 660` ✓

```
口径① 严格          = 418 / 660 = 63.3%          （旧 57.0%，+6.3pt）
口径② 含已验证等价   = 438 / 660 = 66.4%
口径③ 含图谱等价     = 481 / 660 = 72.9%
广义可交付度         = (418+20+16+30) / 660 = 484 / 660 = 73.3%
真实可证行为缺口      = (143+29) / 660 = 172 / 660 = 26.1%（严格）
                     = (100+29) / 660 = 129 / 660 = 19.5%（含图谱等价，MISSING 143−43）
上界（剩余 18 条 UNPROVEN-IMPL 全部证真）：口径① 66.1% / 口径③ 75.6%
```

#### F1 的构成怎么来的（可逐条复跑）

| 步骤 | 数 |
|---|---:|
| `ys_key_reachability` IMPLEMENTED | 222 |
| − 8 个「注册表唯一行为」的未接触发键 → PARTIAL | −8 |
| − 4 个「C# 实现了插件既不打补丁也不读取的东西」→ 待裁决 | −4 |
| | **FAITHFUL 210** |
| SCRIPT_ONLY 19 + 上面 8 | **PARTIAL 27** |
| LABEL_ONLY 138 中，`眼神2(第1页)` 的 20 条（逐键验证 + 回归网守住） | **EQV-BY-ABSENCE 20** |
| LABEL_ONLY 中，`眼神2(第1页)` 的 9 条（语义已解、挂载点在 Themida 远端区；图谱侧全部 `PLUGIN_SIDE_ONLY`） | FAIL-CLOSED 9 |
| LABEL_ONLY 中，`盘古3` 剩余 5 条（`装备吸血` / `无极真气`×3 / `施毒术_公式值`） | FAIL-CLOSED 5 |
| LABEL_ONLY 中仍不可判：`随身仓库` `盘古高级属性` `邮件防刷` `禁止发言不提示` `英雄野蛮` | UNPROVEN 5 |
| 状态 MISSING 的 `获取玩家对象函数`（4 个 apply 站点 177 字节，2.0.8 新增特性，是缺口第 2 名） | UNPROVEN 1 |
| 余下 LABEL_ONLY | **MISSING 99** |
| 合计 | **380** ✓ |

> 旧审计 §2.3 的 9 条 UNPROVEN 现在只剩 6 条：`屏蔽排行榜` 已定名并落地
> （`3fbca823`，`sub_6CBA88` = CM **1060** 处理函数）；`技能等级突破` 归入验证过的 20 条等价；
> `无极真气` 已解（`0x74587C` 是提示串 `，持续 6 秒` 的 AnsiString 长度域，纯文案，数值补丁点不存在）⇒ FAIL-CLOSED。

#### 各面的窗口内变化

| 面 | 变化 | 提交 |
|---|---|---|
| **F1** | 盘古3 从 `IMPLEMENTED 4/39` → **33/39**：战士合击/法道合击 12 键（含拆开两张 f64 系数表 `0x7D33FC` / `0x7D3278`，DC/CC→战士表、MC/SC→法道表，15 个调用点无一跨界）、中毒时间上限 2 键、脚本控制人物爆率 1 键、屏蔽排行榜 1 键、装备提升人物爆率 3 键 | `8967f8ba` `dea54ad6` `ffd9c3f7` `34565196` `6bcdab25` |
| **F1** | `眼神2(第1页)` 29 条从 MISSING 重判为 **20 等价 + 9 C 类阻塞** | `0e303775` `edc062fa` `4d221c80` |
| **F1** | 全库补丁图谱重建：407 站点 → 107 特性，站点级底账（arm / 目标 VA / 字节数），与 `g09.json` 共有的 306 站点上标签/VA/字节数 **0 冲突** | `62cb250c` `759f6220` `e0698edc` `11e41c25` |
| **F2** | `ys_myjn_plus`：`!!!!plus伤害` 在 ys208/ys207 两版**五编码全 0 命中**，原生无解析器 ⇒ 串原样落到宿主真 `GetBagItemCount`。C# 先改成抛异常（`075d11ec`），随后 `9db7d5c3` 补上宿主回落，**与原生一致** ⇒ 维持 FAITHFUL | `075d11ec` + `9db7d5c3` |
| **F3** | `S(1,1..150)` 登录播种（`0x100CE4EA`：`S(1,1..8)` 负值→−1、`S(1,9..150)` 负值→0、`S(1,49)`→1314）落地并接线 `PasScriptHost.cs:998` | `285add26` `b676be88` |
| **F3** | 激光两槽：`S(1,82)`（TrainSkill 的 `Random` 实参）已接，`S(1,81)` fail-closed；`YanshenLaserSlotsCheck` 钉住契约 | `b676be88` `1c82e86e` |
| **F5** | 装备槽 StdMode 冲突裁决完毕（腰带 54/64、鞋 52/62、宝石 53/63 判**无据**），`Yanshen207ProtocolCheck` 复绿 ⇒ 21/21 | `60e00f21` |
| **F6** | 两条中文隧道键名解错修正（`定义伤害`→`自定义伤害`、`英雄极品`→`英雄读取极品`；序列化器 75 条整表首尾相接无缺口 + 原版 config 实测 + 2.0.7 独立复算，三重印证）；5 个自造别名删除 | `075d11ec` |
| **F6** | 爱心分割隧道 38 臂全部无门 ⇒ 删掉 16 个无据的 caret 开关 | `6b992507` |

### 2.5 分母之外、但决定"跑得对不对"的一层：战斗五项公式层

`docs/yanshen_evidence_20260814.md` §5.6 曾明写"五项的内部数值语义没有反演到位"。本窗口做掉了
（`docs/yanshen_combat_formula_20260814.md`）：

| 指标 | 值 |
|---|---:|
| 五项实现体反演到 `ret` | **5 / 5**（含 Themida 搬迁段——关键在于**用 delayed 那份转储**，已重定位那份在 `0x10EB82A1` / `0x113763CF` 处全零） |
| 逐条判定行 | 52（施毒 9 + 麻痹 8 + 吸血 5 + 切割 12 + 自定义伤害 18） |
| 其中原本就对 | 5 |
| 非 FAITHFUL 的数值语义差异 | **47** |
| **本轮按字节修正** | **38** |
| 仍 fail-closed / 部分 | 9 判定行 → 7 条目（F-1..F-7）+ 1 条结构性不可达 |

原生根本不是 C# 写的 `max(0,DC-AC) + baseHp*(magicLv+1)/10 + cuttingV`，而是
**`攻高 − Random(攻高−攻低−命中) − 防高`**，随后串三级宿主管线
（`sub_767BA8` 致命一击 → `VMT+0x1B0 DamageHealth` → `SendDelayMsg(10101)`），且 `cuttingV` 在**魔法护盾之后**才加。
上一轮记录的四条偏差也全部闭合：麻痹自加门去掉（`dd029ee7`）、门拓扑收拢成原生的"一族一门"（`19419d3c`）、
`-888/-777` 哨兵补齐（`3dabe8cf`）、`ys_DingShen` 按原生恒短路成 `-888`（`f487d64e`）。

> **口径提醒（沿用上一轮的分界，不要混用）**：660 条口径量的是"有原生佐证 + 有活消费者"。
> 上面这 52 条判定行属于**更深一层**（数值语义已逐指令反演），**不进 660 的分子分母**。
> 63.3% 说的是"接上了且有据"；公式层那 38 处修正提高的是同一批已计 FAITHFUL 条目的**保真度**。

### 2.6 生产部署口径（另一把尺子，仍然别和上面混用）

按生产 `D:/光头卧龙/mud2.0/Mir200/Gs1/config.json` 实测值分层（本轮重算）：

| 分层 | 旧 | **新** |
|---|---:|---:|
| 生产关闭（值 = 0）⇒ 原生不打补丁、C# 无行为 ⇒ 行为等价 | 165 | **165** |
| 生产开启且有引擎行为 | 135 | **159** |
| 生产开启但只有脚本门（SCRIPT_ONLY） | 9 | **9** |
| 生产开启且完全无行为 | 71 | **47** |

```
生产口径等价率 = (165 + 159) / 380 = 324 / 380 = 85.3%     （旧 78.9%）
```

这个数字**只对"光头卧龙"这一份部署成立**，换一份 config 就会变，不能当子系统完成度。
图谱侧的独立说法可交叉印证：61 个（旧轴）缺口里 31 个在生产配置里是开着的。

---

## 3. `AuditTools` 全量 PASS / FAIL 全景

### 3.1 运行方法（本环境的四个坑，写下来免得下一位重踩）

1. `dotnet run --project X -- <arg>` **不转发 argv**。需要传参的工具必须**直接执行 exe**。
2. 产物落在**四个不同位置**，必须递归找：
   - 无 `ProjectReference` 的静态分析工具 → `AuditTools/<N>/bin/Debug/net8.0*/`
   - 引用 `GameSvr`/`DBSvr` 且带 `<OutputPath>` 的 → **`D:\loym2\.claude\wt2\Build\AuditTools\<N>\[<tfm>\]`**（**跨工作树共享，会互相覆盖**）
   - `GameSvr` 自身 → `D:\loym2\.claude\wt2\Build\Mir200`（即用户点名的 `HeroLifecycleCheck` 有效构建目录）
   - 例外一枚：`ActiveOutgoingProtocolCheck` → `D:\loym2\.claude\wt2\tmp\active_producer2\...`
3. **工作目录决定成败**：9 个工具在"exe 所在目录"下跑会因 `FindRepositoryRoot()` 失败而假性 FAIL，
   换成仓库根就全绿。本报告的最终数字一律以**仓库根为工作目录**。
4. **`-m:8` 会 OOM**。428 个工程并行 8 路时 `csc.exe` 报"页面文件太小"，
   而且**失败的项目里包含 `GameSvr` 本身**——若不察觉，后续所有 linked 工具跑的都是上一轮的旧 dll。
   改 `-m:2` 后只剩已知的那 1 个真实编译错误。

构建：`AuditTools/_buildall.proj`（MSBuild 任务批量 `Restore` + `Build`）。

### 3.2 全景（`4a59a361`）

| 指标 | 值 |
|---|---:|
| 工程总数 | **428**（`ed39ef63` 时 424；窗口内新增 4 个：`NativeCorpseGhostTimingCheck` / `YanshenEquipDropBoostCheck` / `YanshenLaserSlotsCheck` / `YanshenPage1CensusCheck`，**4 个全 PASS**） |
| **PASS** | **366** |
| **FAIL** | **61** |
| 工具源码编译不过 | **1**（`NativeDropRngSequenceCheck`：`Program.cs(106,20)/(109,9) CS8422 静态本地函数不能引用 this` + `(109,9) CS1503`。**`ed39ef63` 同样不过**） |

### 3.3 61 个 FAIL 的三段 A/B 归因

| 段 | 条数 | 归因 |
|---|---:|---|
| 在 `ed39ef63` 基线上同样失败 | **56** | 既有，与刷新窗口无关 |
| `ed39ef63..075d11ec` 引入 | **3** | 回归网漂移（§3.3.2） |
| `075d11ec..4a59a361` 引入 | **2** | 已二分定位到 `17ae7f39`（§3.3.3） |

A/B 方法：`git worktree add --detach abbase ed39ef63`，把 `_buildall.proj` 拷进去，
用 **`-p:OutputPath=<隔离目录>`** 构建（这一步是关键：不加就会写进跨工作树共享的
`Build\AuditTools\<name>\` 把被审的二进制覆盖掉），再用同一批断言在基线工作树上重跑失败集。

#### 3.3.2 `ed39ef63..075d11ec` 引入的 3 条（全部是回归网漂移，无行为回归）

| 工具 | 肇因提交 | 断言 | 定性 |
|---|---|---|---|
| `ChgMonItemPercentStaticCheck` | `6bcdab25`（眼神装备爆率三键接线） | `Program.cs:51` 正则 `Random\(MonItem\.MaxPoint\b[^)]*\)\s*<=\s*MonItem\.SelPoint` | `UsrEngn.cs:2506-2510` 现在是 `Random(YanshenEquipDropBoost.Denominator(MonItem.MaxPoint * penalty, killer)) <= MonItem.SelPoint`，正则不再匹配。**行为改动本身有字节证据**（`0x100B9F9E → 0x71FD37` 46 dword 桩体，`+0x2A4` = CC 下限），但**这道守卫现在红着 ⇒ 原生掷点形状已无人守护** |
| `MarryClusterCompatCheck` | `2aa89895`（SGRP OthGs 第二批注记） | `Program.cs:399` 要求 `case Grobal2.ISM_DIVORCE:` 与 `MsgGetDivorce(serverNum, Body); break;` **紧邻** | 该提交在两者之间插了 8 行证据注释。**行为一字未改**，纯脆性正则 |
| `ProvenanceGuardCheck` | `4520d53d`（MakeGhost 移除 INVENTED 分叉） | `TBaseObject.Base.cs:1545` 引用 `staging/ref-MirServer-Delphi/EM2Engine/ObjBase.pas:18605` 却**未在同一行**标注战神 EA | 该提交正文其实给足了战神 EA（`sub_768060` / `0x7680E9` / `0x7680EF`），只是落在别的行；守卫是**逐行**扫描的 |

#### 3.3.3 `075d11ec..4a59a361` 引入的 2 条 —— 二分定位到 `17ae7f39`（**本报告最值得注意的一条**）

```
NativeHorsePairProtocolCheck    3418 pair packet count: expected=2, actual=1
NativeRun3HorseProtocolCheck    4108 partner shared cell registration: expected=2, actual=0
```

二分足迹（独立工作树 `hb1`，`-p:OutputPath` 隔离，逐提交构建 + 运行这两个工具）：

| 提交 | 两工具 |
|---|---|
| `4b6ee650`（SPWN-56 之前） | **PASS / PASS** |
| `e1b031a3`（谓词落地 + 接 `TBaseObject.ViewRange.cs` 两处） | **PASS / PASS** |
| `17ae7f39`（把谓词扩展到 `TPlayObject.SearchViewRange` 与 `RobotPlayObject.SearchViewRange`） | **FAIL / FAIL** |

根因已定位到具体一行：谓词第三项要求 `envir.sMapName` 非空
（`TBaseObject.NativeCellObjectValidity.cs:63`，对应原生 `0x765D85 cmp dword [eax+0x44],0`），
而两个 harness 都是 `var map = new Envirnoment();`（`Envirnoment.cs:20` 的 `sMapName` 默认 `string.Empty`），
于是**每一个格内 actor 都被判"失效"并在首次视野扫描时摘链**，同伴自然不在共享格上、3418 只发出 1 个包。

**性质判定（不夸大也不缩小）**：

- **生产路径大概率不受影响** —— 正式地图经 `Maps.cs:408 AddMapInfo(sMapName, …)` 建立，
  而 `Maps.cs:77` 明确拒绝空名；`sMapName` 为空的只有裸 `new Envirnoment()`
  与 `NativeDynamicRoomEnvironmentFactory`（取 `definition.RoomName`）。
- **但这是一条真实的隐式前置条件**：`Envirnoment.sMapName` 的类型层面不保证非空，
  谓词却把它当成"对象有效"的必要条件。`drop_view_residual §2` 当初的顾虑
  （"原生谓词依赖的对象拆解信号在托管模型里不存在"）被 `c777db2a` 判为 superseded，
  从"OR 是单调的"这一点看没错，但**单调性只保证不驱逐"有名字且地图名非空"的 actor**——
  它没有保证托管侧每个合法占格 actor 都满足这两条。
- **无论如何，两条此前绿着的协议守卫现在红着，必须先恢复。**

⇒ 建议：给两个 harness 补 `sMapName`（一行），**同时**在 `Envirnoment` 侧确认
（或断言）任何进入格子链的 actor 其 `m_PEnvir.sMapName` 非空；两者缺一都只是把红灯关掉而不是解决问题。

#### 3.3.4 56 条既有失败的性质分层

| 类别 | 条数 | 示例 |
|---|---:|---|
| 需外部依赖 / 实参（非契约失败） | 4 | `NativeHonorDbCheck`（自述 SKIP，需 MySQL）、`WeaponUpgradeRoundTripCheck`（需 MySQL）、`PasScriptAudit`（需 4 个实参）、`NativeProperTargetGateCheck`（`M2Share` 静态构造要读一个不存在的配置文件） |
| harness 未初始化 / 时序 | 2 | `NativeLevelExpTableCheck`（`TBaseObject..ctor` 空引用，`M2Share.ObjectManager` 未初始化）、`CSharpGateM2IntegrationCheck`（`OperationCanceledException`） |
| **真实契约断言失败（基线既有）** | **50** | 见下 |

50 条里有 3 条已在提交正文中被明确记录为"基线同样失败"，可直接排除误读：
`HeroUnionStateCheck`（`CheckMapSkillFlags` 的 `non-test permission4`）、
`NativeTempSetMapParamPickupCheck`（权限文案）、
`NativeDropControlRuntimeCheck`（`native fixed scatter range: expected=4, actual=3` —— 世界腿 4→3 的修复 `f3354457` **早于** `ed39ef63`，是工具期望表陈旧）。
其余 47 条散落在 DbGate/Dispatcher/Hero/Quest/Yb/Pas 等子系统，全部先于本审计窗口存在。
明细：`_toolruns2/_summary.csv`（当前）、`_toolruns_root/_summary.csv`（`075d11ec`）、`_toolruns_ab/_summary.csv`（`ed39ef63`）。

### 3.4 Yanshen 专用工具：20 / 20 全绿

旧 Yanshen 审计记的是「17 个工具 13 PASS / 4 FAIL，其中 3 个已经不再守护它们本该守护的契约」。本轮实测全绿：

```
Yanshen207ProtocolCheck  Yanshen208ApiSurfaceCheck  YanshenApiAccessCheck    YanshenCdCompatCheck
YanshenConfigRuntimeCheck YanshenDpiIsolationCheck  YanshenEquipDropBoostCheck(新)
YanshenHalfMoonCompatCheck(曾编译不过)  YanshenHeroCastCommand28Check  YanshenItemConfigCheck
YanshenLaserSlotsCheck(新)  YanshenMonsterAttrCheck(曾 harness 腐化)  YanshenMsgTransportCheck
YanshenMyJsonConfigCheck  YanshenPage1CensusCheck(新，含反臆造闸门)  YanshenPaintDiagnostic
YanshenRecycleConfigCheck  YanshenSunSwordCompatCheck  YanshenTriggerDispatchCheck(曾期望表漂移)
YanshenWarriorSkillCompatCheck
```

**旧审计的 P0「先把审计链修好」已完成。** 但主引擎侧在同一段时间里新出现了 5 处漂移（§3.3.2 + §3.3.3）——
这类问题会周期性复发，建议把 `_buildall.proj` + 本报告的运行脚本固化成 CI 的一步。

---

## 4. 距离"百分百"还差什么 —— 按优先级排序

排序原则：**能直接搬走百分点的排前面；只能搬"保真度"或需要先拿证据的排后面。**

### P0 — 先修回归网（6 条，代价以小时计；不修则后续所有改动没有护栏）

1. **`17ae7f39` 引入的两条**（`NativeHorsePairProtocolCheck` / `NativeRun3HorseProtocolCheck`）。
   **这是唯一一条既红了守卫、又指向一个真实隐式前置条件的**，排第一。修法见 §3.3.3。
2. `ChgMonItemPercentStaticCheck` 期望正则更新以容纳 `YanshenEquipDropBoost.Denominator(...)` 包裹
   —— 它守的是原生掷点形状（`Random(MaxPoint 派生分母) <= SelPoint`），现在红着等于没守。
3. `MarryClusterCompatCheck` 的 `ISM_DIVORCE` 正则改成允许中间夹注释。
4. `ProvenanceGuardCheck`：给 `TBaseObject.Base.cs:1545` 那行补战神 EA，或把守卫粒度从"逐行"改成"逐注释块"。
5. `NativeDropControlRuntimeCheck` 的 `fixed scatter range` 期望值 4 应按 DROP-33 证据改 3。
6. `NativeDropRngSequenceCheck` 的 `CS8422/CS1503` 编译错误（基线既有，该契约当前**零守护**）。

### P1 — 主引擎最后 2 条真实缺口（能把 92.2% 推到 ~92.5%，并让 A 类归零）

7. **MOVE-74**（穿透态 `0xB05`/2821 变化广播）。纯发包，`vmt+0x250`，TRUE `push 6/1/0/0` / FALSE `push 6/0/0/0`，
   缓存字段与刷新点都已在位。**性价比最高的一条。**
8. **MOVE-73**（判定改回 tick 缓存）。需要能在 `Run()`（= `sub_6B2D38`）里挂刷新，
   而 `TPlayObject.Message.cs` 是禁改文件 —— **流程闸门问题，不是技术难题**，请主代理放行插桩点。

### P1.5 — 补账本对跨服 OthGs 的覆盖（不涨百分比，但会让 92.2% 更诚实）

9. 给 §1.5 的 8 个 ident（203/209/210/212/214/224/227/228）补契约号后重算 SGRP 段。
   其中 **224 / 228 是师徒声望与充值奖励**，被 C# 当成了"开市场"与"行会成员召回"，**玩法语义完全错位**，
   风险等级不低于旧报告里的 P0。

### P2 — Yanshen 能直接搬点数的两块（每块 ~1.4~4pt）

10. **A 类外科接线的残余 9 条触发点**（`英雄穿戴触发` `新穿戴触发` `上线触发` `心灵启示触发`
    `复活触发脚本` `攻击触发` `魔法攻击触发` `盘古魔法攻击触发` + `@Cutting`）。每条都有挂载 VA /
    覆盖长度 / 槽号 / 参数个数，`YanshenTriggerDispatch.Registry` 已把原生事实存成静态数据。
    **两个陷阱**：①`心灵启示触发`(`0x6EDC2B`) 是**顶掉型**，不重放被覆盖字节，接错方向会让原生动作双跑；
    ②`武器绿毒` 与 `物功带毒` 打**同一个** `0x76E2BC`，必须做成互斥的一套毒源。
    预计 +8~9 条 ⇒ 严格约 **+1.4pt**。
11. **B3 S 变量消费层 27 条 MISSING**（永久属性 22 槽 `S(1,13..23)`/`S(1,31..41)`、
    切割族 `S(1,9..11,50..53,62,63)`、技能变址 `S(7/8/9, magicid)`）。
    前置 `S(1,1..150)` 播种**已经通了**（`PasScriptHost.cs:998`），所以这一族接上就是活的。
    预计 +27 条 ⇒ 严格约 **+4.1pt**。

### P3 — Yanshen 剩余 MISSING 的大头（99 条 F1 + 17 条 F2）

12. **先用两轴交叉把 99 条 MISSING 拆开，再动手。** 按 §2.3，其中 43 条落在
    `EQUIVALENT_BY_ABSENCE ∧ LABEL_ONLY`（口径③的那 63 减去已验证的 20），
    **很可能根本不该写代码**。做法照抄 `ys_b1_yanshen2_page1` §2：
    先跑 `tools/ys_page1_census.py` 的"任意 `mov`/`cmp` 读该位移"补充普查（布尔判据对数值键有假阴性），
    确认零读点后判等价并加反臆造闸门。**先普查再动手，能避免把"等价"做成"臆造"。**
    真正要移植的是 `NATIVE_GAP ∧ LABEL_ONLY` 那 33 条 + `SCRIPT_ONLY` 9 条 + `获取玩家对象函数` 1 条 = **43 条**。
13. `PLUGIN_SIDE_ONLY ∧ LABEL_ONLY` 35 条：插件自己读、不改 M2Server，
    其中 9 条已知卡在 `sub_100795C0`（见 P4），其余 26 条需要逐条定位插件内消费者。
14. B2 高站点数补丁未接：`永久属性` 12 站点、`屏蔽属性提升提示` 31、`免毒符` 12、`屏蔽元宝增减信息` 8
    —— 载荷字节完整（`ys_patch_sites_atlas.tsv` 现在连 trampoline apply 载荷也有了），缺的是 C# 落点映射。
15. F2 的 17 条未登记脚本 API + 18 条 `UNPROVEN-IMPL`（官方例子名，三份底本零命中）。

### P4 — 需要先取证据才能推进（不是移植工作量，是逆向工作量）

16. **Themida 远端调用链 —— 两条战线共同的、也是唯一的硬瓶颈。**
    症状：`0x10400000..0x11400000` 这 16 MB 在**已重定位**那份 2.0.8 转储里全零，
    只有 **delayed 那份**（PE 基址 `0x57C40000`，绝对操作数需 `−0x47C40000` 还原）才有内容
    （实测非零 `16,506,511 / 16,777,216`）。它当前卡住的东西：
    - **9 条 `眼神2(第1页)` fail-closed 键**：伤害后处理流水线 `sub_100795C0` 在两份转储里
      **rel32 调用者只有 1 处**——`0x10F2D759`，就落在远端混淆区；`.rdata` trampoline 模板里
      指向插件 `.text` 的 7 处 `E8/E9` 无一指向它。**5 条切割键 + `英雄千分比免伤` 的语义已逐字节备齐
      （`magicId → S(1,116..120)`、tag/值槽偏移已由 `YanshenPage1CensusCheck` 钉住），只差一个挂载点。**
    - `主号高级暴击` / `高级英雄倍功暴击`：另加载荷不可判（`[0x1031C250]`/`[0x1031C254]` 两份转储皆为 0，
      `pushal/popal` + 返回值走 `ecx`，目标运行期现搭）。
    - `英雄野蛮`、`S(1,1)` 禁言模式 6/7/8（SetS detour `0x100CEB40`）、`Ys_HuiShou` 的脚本注册点、
      操作码 1（`ys_myysjn`）的实现体（臂 `0x1007670A` → `call 0x10DF3A91`）。
    **解法二选一**：(a) 对远端区做去混淆 / 反虚拟化，把 `0x10F2D759` 的调用链上溯到某个 trampoline；
    (b) **活体调试：在 `0x100795C0` 下断点读返回地址链**（更快）。任一成功，一次性解锁 6+ 条。
17. **缺一份"插件未加载"的 M2Server 转储**：磁盘 `M2Server.exe` 被 VMProtect 加壳（`CODE` 节 `SizeOfRawData = 0`），
    没有 stock 基线 ⇒ 一切 `orig`/`stock` 断言不可判，96 个随机极品槽里 24 个双向冲突无从裁决。
18. 主引擎 C 类 43 条 UNPROVEN + 12 条 fail-closed：`DURA-37/38/40/41/42/43`（入口/可达性/装卸 opcode/超重后果）、
    `SGRP-35/40/41` 与 `TRADE-61/44`（**依赖 DBSvr 侧或运行期配置，非本仓可闭合**）、其余 32 条负命题。

### 一句话优先级

> **P0 修 6 个守卫（先修 `17ae7f39` 那两条，它指向一个真实的隐式前置条件）→
> P1 补 MOVE-74（半天）+ 放行 MOVE-73 插桩点 → P1.5 给跨服 OthGs 补 8 条契约（224/228 是玩法语义错位）→
> P2 接 Yanshen 9 个触发点 + S 变量消费层（能把 Yanshen 严格口径从 63.3% 推到 ~69%）→
> P3 用两轴交叉先普查再动手（99 条 MISSING 里可能有 43 条根本不该写）→
> P4 抓一份能看到 Themida 远端调用链的转储（唯一的硬瓶颈）。**
> **DBSvr 依赖项建议永久记为外部边界，不要硬凑。**

---

## 附录 A：复现足迹

```
工作树      git worktree add D:\loym2\.claude\wt2\audit3 -b w/audit3 master
            （建树时 master=075d11ec；成文时已 git rebase master 到 4a59a361）
A/B 基线    git worktree add --detach D:\loym2\.claude\wt2\abbase ed39ef63
二分        git worktree add --detach D:\loym2\.claude\wt2\hb1 <commit>
python      C:\Users\Administrator\AppData\Local\Programs\Python\Python311\python.exe   (3.11.9 + capstone 5.0.7)
            注意：PATH 上的 python.exe 是 Windows Store 别名，直接调会「拒绝访问」，必须走绝对路径或 py -3.11
```

本轮新增的四份脚本（随本提交入库，均只读）：

| 文件 | 用途 |
|---|---|
| `AuditTools/_buildall.proj` | MSBuild 批量 `Restore`+`Build` 全部 428 个工程（**用 `-m:2`，`-m:8` 会 OOM 且会静默漏掉 GameSvr**） |
| `tools/audit_run_all.ps1` | 全量跑工具（四处产物目录递归定位 + 仓库根为工作目录 + 超时 + 退出码 + 落盘 CSV） |
| `tools/audit_run_ab.ps1` | 在基线产物目录上重跑失败集做 A/B 归因（含 `-p:OutputPath` 防共享目录互相覆盖的说明） |
| `tools/ys_reach_bypage.py` | `ys_key_reachability` 的按页分解版，另导出 380 键逐键 `页/状态/生产值` 的 TSV |

产物（未入库，留在工作树内供复核）：
`_toolruns2/`（**权威，`4a59a361`**）、`_toolruns_root/`（`075d11ec`）、`_toolruns_ab/`（基线 `ed39ef63`）、
`_ys_reach_bypage.txt` + `_ys_reach_rows.tsv`（380 键逐键 页/状态/生产值）、`_buildall*.log`。

关键复现命令：

```powershell
# Yanshen 380 键逐键可达性（修正了旧矩阵「只给键名字面量持有者播种」的漏报）
python tools\ys_key_reachability.py D:\loym2\.claude\wt2\audit3
#   -> {'IMPLEMENTED': 222, 'SCRIPT_ONLY': 19, 'LABEL_ONLY': 138, 'MISSING': 1}

# 两轴交叉（C# 可达性 × 二进制补丁图谱）
python tools\ys_reach_bypage.py D:\loym2\.claude\wt2\audit3     # 生成 _ys_reach_rows.tsv
#   再与 docs\ys_patch_completeness.tsv 的 verdict 列做交叉，见 §2.3

# 触发注册表接通数 (GameSvr/Plugins/YanshenTriggerDispatch.cs)
#   -> Wired=true 12 / Wired=false 9

# AuditTools 全量
dotnet build AuditTools\_buildall.proj -m:2
powershell -File tools\audit_run_all.ps1        #  366 PASS / 61 FAIL / 1 NOEXE
powershell -File tools\audit_run_ab.ps1         #  61 FAIL 中 56 条在 ed39ef63 同样失败
```

## 附录 B：本报告未做的事（边界声明）

1. **未逐条重跑 29 份分片的原始核验。** 本报告以旧主报告的 HEAD 对齐结果（651/23/…）为起点，
   只对**窗口内 113 个提交触碰到的契约**做逐条复核（26 条迁移 + 2 条残留 + DURA/SGRP 组重判），
   其余 651 条 FAITHFUL 未重新反汇编。依据是 A/B 工具对照：本窗口引入的 5 条工具失败**无一是行为回归**。
2. **未验证 Yanshen 那 43 条"图谱判等价但未逐键复查"的键。** 只有 `眼神2(第1页)` 的 20 条做过逐键普查
   并有回归网。因此口径③（72.9%）是**上界情景**，口径①②才是可交付的结论。
   反过来说，**当前 63.3% 大概率是低估**，但在普查做完之前不上调（不虚报）。
3. **未跑需要 MySQL / 实参的 3 个工具**（`NativeHonorDbCheck` / `WeaponUpgradeRoundTripCheck` / `PasScriptAudit`）。
4. **未给跨服 OthGs 的 8 个新分歧 ident 补契约号**（§1.5），所以 754 这个分母本身是不完整的。
5. **未改任何 `.cs`。** 本提交只含本文档与四份脚本。
