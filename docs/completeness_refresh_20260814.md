# 战神引擎 M2Server → C# 1:1 复刻 · 完成度刷新报告（两条战线）

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\audit3`　分支：`w/audit3`　审计 HEAD：**`075d11ec`**
  （`fix(yanshen): 中文隧道两条键名解错，另有 5 个自造命令名无原生对应`，建树时的 `master`）
- 仓库真实根：`D:\loym2\LyoMir2-master`（`D:\loym2` 本身不是 git 仓库，`.git.broken-20260810` 是废弃目录）
- 刷新窗口：`ed39ef63..075d11ec` = **92 个提交**（`ed39ef63` 是主报告 `docs/completeness_audit_20260814.md` 的 HEAD）
- A/B 基线工作树：`D:\loym2\.claude\wt2\abbase`（detached @ `ed39ef63`），用于区分「既有失败」与「本轮新引入」
- 底本：`staging/_reunpack_work/flat_image.bin`（base `0x400000`）、
  `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin`（base `0x10000000`）、
  `staging/yanshen208_strparam_runtime_dump_delayed_20260719/…`（base `0x57C40000`，含 Themida 远端区）
- 工具：`dotnet 10.0.302`、python 3.11.9 + capstone 5.0.7
- **纪律：本轮只读审计，未改任何 `.cs`；本次提交只含本文档与三份复现脚本。所有数字逐条可回溯到提交哈希 / 原生 VA / 工具输出。不虚报。**

---

## 0. 执行摘要（结论先行）

| 战线 | 分母 | 旧数（旧报告） | **新数（严格）** | **新数（含 EQUIVALENT-BY-ABSENCE）** | 真实可证行为缺口 |
|---|---:|---|---:|---:|---:|
| **主引擎（等价账本）** | 754 | 89.4%（674/754） | **92.0%（694/754）** | **92.3%（696/754）** | **3 条 = 0.4%** |
| **Yanshen 插件** | 660 | 57.0%（376/660，区间 57.0~59.7%） | **63.8%（421/660）** | **66.8%（441/660）** | **172 条 = 26.1%** |

`AuditTools` 全量（428 个工程）：**368 PASS / 59 FAIL / 1 工具源码编译不过**。
59 个 FAIL 里 **56 条在 `ed39ef63` 基线上同样失败**（既有），**3 条是本窗口新引入的「回归网漂移」**（全部是断言表/正则未随实现更新，**无一条是行为回归**）。

**一句话**：主引擎的"真实 C# 行为与原版有别"缺口已从 23 条压到 **3 条**（MOVE-73 刷新时机 / MOVE-74 穿透态广播 / SPWN-56 视野摘链谓词），距 100% 的阻碍已从"移植工作量"整体转为"证据边界"（43 条 UNPROVEN + 12 条有意 fail-closed）。Yanshen 一侧涨了 6.8 个点，主要来自盘古3 整页落地、战斗五项公式层反演、协议断言复绿；但它离百分百仍差 **143 条 MISSING**，其中最大的单一瓶颈是 **Themida 远端调用链**（`0x10400000..0x11400000` 那 16 MB 在「已重定位」转储里全零）。

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

### 1.2 状态迁移表（`ed39ef63` → `075d11ec`）

| 状态 | 旧（`ed39ef63`） | 迁出 | 迁入 | **新（`075d11ec`）** |
|---|---:|---:|---:|---:|
| FAITHFUL | 651 | 0 | +5 | **656** |
| FIXED | 23 | 0 | +15 | **38** |
| EQUIVALENT-BY-ABSENCE | — | — | +2 | **2** |
| MISSING | 12 | −12 | +1 | **1** |
| DIVERGENT | 8 | −7 | +1 | **2** |
| FAIL-CLOSED | 10 | −1 | +3 | **12** |
| BLOCKED / UNPROVEN | 47 | −4 | 0 | **43** |
| NOTHROUGH 在建 | 3 | −3 | 0 | **0** |
| **合计** | **754** | | | **754** ✓ |

校验：`656 + 38 + 2 + 1 + 2 + 12 + 43 = 754` ✓

```
严格完成度   = (656 + 38) / 754 = 694 / 754 = 92.0%      （旧 89.4%，+2.6pt）
含等价完成度 = (656 + 38 + 2) / 754 = 696 / 754 = 92.3%
广义可交付度 = (656+38+2+12+43) / 754 = 751 / 754 = 99.6%
真实行为缺口 = (1 MISSING + 2 DIVERGENT) / 754 = 3 / 754 = 0.4%   （旧 23 条 / 3.0%）
```

### 1.3 本窗口状态迁移明细（25 条，逐条附提交哈希与代码落点）

> 迁移守恒校验：`FIXED +15`（§1.3.1~1.3.4）+ `FAITHFUL +5`（§1.3.5）+ `EQUIVALENT-BY-ABSENCE +2`（§1.3.6）
> + `FAIL-CLOSED +3`（§1.3.7）= **25 条**；其中 `MOVE-57` 由 FAIL-CLOSED 迁出，故 FAIL-CLOSED 净 `+2`。

#### 1.3.1 MISSING → FIXED（8 条）

| 契约 | 提交 | 代码落点（本轮实测） | 摘要 |
|---|---|---|---|
| MOVE-10 | `b5a45030` + `9294fdb3` | `TPlayObject.Message.cs:1487 / :1580` | state 0x34（双人坐骑乘客态）静默移动闸；只挂 CM_WALK/CM_RUN，不挂 TURN/SITDOWN（跳表 `0x6D8592` 证实只有 2 臂带闸） |
| MOVE-11 | `a9cc64f2` | `TPlayObject.Attack.cs:767 / :905` | walk/run 补 `sub_6BCE2C` 三连取消挂起通道；分片"依赖未建模外观子系统"被证伪（`0x20` 来自相邻的 `sub_6BCE54`） |
| MOVE-39 | `9ae059ee` | `TBaseObject.cs:1334` + `TPlayObject.NativeWalkPartnerFollow.cs` | 人形 mover 尾 `0x741350 call sub_6BBEE4`；分片"英雄跟随"被证伪，实为 `[+0x3C0]` 双人坐骑同伴 |
| MOVE-79 | `b7b8bbd1` | `TPlayObject.NativeTianDiHeYi.cs` + `Command/Commands/TianDiHeYiCommand.cs` | 天地合一全链（命令体 `sub_6C7B28` + 执行 `sub_7274B4` + 切换 idx23/24） |
| MOVE-85 | `8298f9ef` + `3ab0059a` | `TPlayObject.NativeMapEntryStatus.cs` + `Message.cs` ×3 | 进图状态通告 `sub_6B6BEC`（超负重 + 三档巅峰战神状态） |
| MOVE-90 | `597075b9` + `b82c7142` | `TPlayObject.NativeNoMagicMap.cs` + `Message.cs` | NOMAGIC 唯一 reader `0x6DA12B`，置位则静默拒绝（只发 `0x276`） |
| SPWN-13 | `be5126b0` + `39ebe3b9` + `a5a34bcb` | `UsrEngn.cs:3401-3409` | `[gen+0x28]` 尸体存留秒数 → `word[obj+0x38]`；消费端改读 `word[obj+0x38]`，撤销 SPAWN-32 的 `dwZenTime/dwMakeGhostTime` 门 |
| SPWN-14 | `be5126b0` + `39ebe3b9` | `UsrEngn.cs:3407-3409` | `[gen+0x40]` BOSS 生成播报（`wIdent=0x64 / wParam=0x38FF`） |

#### 1.3.2 DIVERGENT → FIXED（3 条）

| 契约 | 提交 | 摘要 |
|---|---|---|
| DROP-33 | `9584a44f` / `20690091` | 怪物 own-table 腿散落半径改原生硬编码 3（`0x71FDCF` / `0x71FE46 mov ecx,3`）；`DropItemRage` 三编码全镜像 0 命中 |
| SGRP-26 | `31d80e5a` | 217/218 = 师徒（`sub_657CF0` / `sub_657AC0`），旧 C# 是好友空桩 |
| SGRP-31 | `31d80e5a` | 241 = 信用卡全清（`sub_655A18` 无条件 `ResetOnlineAll`），移除 C# 自造的行会战分支 |

#### 1.3.3 FAIL-CLOSED / BLOCKED → FIXED（3 条）

| 契约 | 提交 | 摘要 |
|---|---|---|
| MOVE-57 | `4843a4ab` | 登录放置 jitter 耗尽后补当前图 `GetRandomXY`，失败才回城 |
| ECON-17 | `e2dd82a2` | 分片 01 两条封锁理由逐字节推翻：`+0x675 = m_btPermission`（setter `0x6B1E80`），守卫函数 `sub_644244` 就是 `ClientBuyItem` 的孪生臂；补齐 property-9 商人的 GM 专用寄存子系统（`Merchant.NativeStorageNpc.cs`） |
| DURA-11 | `b72c0604` + `42e43669` | 第二护身符消耗例程 `sub_73EA20` 移植 + SKILL_62 接线（`TPlayObject.NativeAmuletConsume.cs` / `HeroObject.NativeAmuletConsume.cs`） |

#### 1.3.4 NOTHROUGH 在建 → FIXED（1 条）

| 契约 | 提交 | 摘要 |
|---|---|---|
| MOVE-75 | `aeadf8f5`（并入前序）+ `35c9a6ed` | `sub_772EB8` 无条件穿透授予（`m_boObMode || InBodyState(0x3C)`）已落 `HasNativeCellPassThroughGrant()`，且优先于 NOTHROUGH；`35c9a6ed` 把 run mover 的 `boIgnoreOccupancy` 也接到缓存 `Obj+0x3FE`，与 walk 对齐 |

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
| MOVE-31 | MISSING | `de97da32` | `sub_765D64` 的三个条件（`[+0x106]` 名字空 / `[+0x128]` Envir 空 / `Envir[+0x44]` 地图未就绪）在托管模型下**全部不可达**（全仓 `m_PEnvir = null` 只有字段初始化器；`m_sCharName` 无清空点）⇒ 探针恒真 ⇒ 摘链臂在原生是活的、在 C# 照搬会变死代码并让最热的格子扫描只增不减。**可观测行为已等价** |
| MOVE-89 | MISSING | `4f342eab` + `597075b9` + `b84eba38` | `TTimerBomb` 的 classptr `0x781304` 全镜像只出现在自身 vmtSelfPtr / RTTI，不在物品工厂跳表 `0x74D07B`（只按 `byte[StdItem+0x15]` 派发 0..10），无 FindClass 引用 ⇒ **原生从不实例化**，消费者是死代码。C# 已 1:1 移植方法体并**刻意不接线**，与原生同处不可达态 |

#### 1.3.7 → FAIL-CLOSED（3 条，证据充分但不可安全移植）

| 契约 | 旧判 | 提交 | 依据 |
|---|---|---|---|
| SGRP-25（202） | DIVERGENT | `2aa89895` | 原生是反作弊惩罚（`sub_653ED0`，`'SD000'@0x65402C`）。三重阻碍：惩罚时长来自帧第三 dword（C# 跨服是文本协议，无载体）、到期字段 `[+0x180c]` / 日期基址 `[+0x780]` 全仓无对应成员、唯一 live 发送方是 `UsrEngn.cs:1568`（禁改）的登出广播。保留登出接收 + 全量字节证据注释 |
| SGRP-44（207） | DIVERGENT | `2aa89895` | 原生是全局 37-bit 位图 swap（`[0x7D7038]` + 逐位回调 `sub_658110`）。掩码来自帧第三 dword、无 C# 全局模型；C# 现有两条语义（信用卡 switchWord / 重载行会）**各自都有 live 发送方**，移除会破坏在用功能 |
| SGRP-30（247） | MISSING | `2aa89895` | 原生 `sub_65805C` 是真实 handler（`len==0xD` 门 + 三 dword → `0x699310` 日志/DB）。C# 新增**显式 no-op case**（而非落默认 error sink），与"原生是真实 handler、不打印 Ident 未知"一致；二进制 body 在文本协议里无载体，且全仓无 247 发送方（SGRP-41） |

### 1.4 仍未闭合 —— 三类清单（58 条 = A 3 + B 0 + C 55）

#### A 类 —— 真实可证行为缺口（3 条，**这是距 100% 唯一真正欠的移植工作量**）

| 契约 | 状态 | 证据 | 现状 / 阻碍 |
|---|---|---|---|
| **MOVE-74** | MISSING | 穿透缓存每次跃迁经 `vmt+0x250` 发 `0xB05`(2821)：TRUE `push 6/1/0/0`，FALSE `push 6/0/0/0` | `TPlayObject.NativePassThrough.cs:38/41/148` 明写"SM_2821 变化广播暂不复刻"。**纯发包，无状态依赖，是三条里最容易补的一条** |
| **MOVE-73** | DIVERGENT | 判定是玩家 tick `sub_6B2D38` 内的缓存（`0x6B308E` 重算 / `0x6B3096` 比旧值 / `0x6B30A3` 变了才回写） | C# 改在"移动使用点"刷新（`NativeRefreshThroughOccupancyCache`，walk + 3 个 run mover 入口各一次）。判定值一致，**刷新时机不同**；忠实化要求能在 `Run()` 里挂 tick，而 `TPlayObject.Message.cs` 禁改 |
| **SPWN-56** | DIVERGENT | `0x77A2EB call 0x765D64`；失败臂摘链 + 记异常（格式串 `0x77A81C` = `TEnvironment.DoPlayerSearchViewRange Curr^.POject.CName = 空`） | 根因比原判定更深：**原生谓词依赖 Delphi 对象拆解后三个槽失效这一信号，托管模型里不存在**。照搬 ⇒ 谓词恒真 ⇒ 摘链臂变死代码 ⇒ 孤儿格子项唯一 GC 消失，而这是全服最热路径。C# 现用 60s 老化摘链（活体由 `VerifyMapTime` 每 30s 刷新 `dwAddTime`，不误伤）。残余差异：回收最多迟 60 秒 + 不发那条异常日志 |

> **B 类（大子系统重写）本轮清零。** 旧报告 B 类 20 条已全部落定：MirrorMessage 跨服 5 条（2 修 3 有据 fail-closed）、随机传送/天地合一族 5 条（3 修 1 上修 FAITHFUL 1 等价）、移动闸 2 条（全修）、NOTHROUGH 3 条（1 修 1 分歧 1 缺失）、SPWN-56 1 条（维持）、DROP-33 1 条（修）、STATE-19 1 条（上修）、SPWN-13/14 2 条（修）。

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
| UNPROVEN 负命题 / 证据缺口（C.3 原 33 条 −ECON-17 −DURA-39 −DURA-44 +DURA-11 已出） | 32 | `ECON-38/39/40`、`PRICE-24/25`、`CRAFT-46`、`POIS-32/33/34`、`STATE-50`、`MINE-56/57/58`、`GILD-29`、`DROP-30/37`、`MOVE-94/95/96/97`、`SPWN-16/22/30/31`、`SPWN-45/47/55/57`、`QST-27/28/31/32` |

### 1.5 账本外的额外加固（不计入 754 分母，登记以免遗漏）

| 项 | 提交 | 摘要 |
|---|---|---|
| `POIS-39` | `a4159547` + `186ef170` | 物理落伤 `sub_73F8E0`（VMT+0x1AC）；`self+0x2DC` 百分比物理减伤聚合字段落地后解锁 |
| `GATE-01/02/03` + 连接点门 | `be2173ad` / `f157d901` / `3d7ee975` / `80c18a75` | 77BBAA33 传输帧 16 字节头；MapInfo 连接点补目标格 `attribute==0` 校验 + 无效连接点告警 |
| `CompareLStr` | `52acf458` | 改纯 ASCII UpCase 对齐 `0x4034D4` |
| `MOVE-41` | `1f95ba6e` | CM_RUN(3013) mover 尾 `0x767683` 补双人坐骑同伴跟随（与 MOVE-39 同门同例程） |
| `MFLG` 残口 | `7aab71fe` + `b5936ebd` | 配置解析器补 8 个真地图旗标 token（含 `UserNoKill byte[+0x71]` + `word[+0x74]`）；`@TempSetMapParam` parser A 同批扩展；`pickup` 改前缀比较 + Delphi `Trim` 空串语义 |
| `SKILL-62` | `64a988bd` | 护身符消耗改召唤前无条件执行 + 落地 `0x76EEF4` 圣兽召唤原语；冷却戳唯一写点改 `HolyMonster.Die` |
| `HERO-MAGIC-1/2/3` | `e14c6eb2` / `f3b0d605` / `2c253e5c` | 英雄侧护身符例程 + 逐技能分派器 `sub_68DD88` |
| `MakeGhost` | `4520d53d` | 移除 `m_boCanReAlive` 分叉（战神 + 两份 Delphi 三方皆无 ⇒ INVENTED），按 `sub_768060` 无条件置 `m_boGhost` |
| 挖肉三守卫 | `2f322c8f` | `sub_71ED80` 补目标校验（`cx=2` 硬门）/ 修正皮革边界（`>=0` 才 return）/ 补 `m_boNoItem` 门 |
| `DURA-16` / `U_DRESS` | `1cc0c42e` / `ac102d67` | 受击掉甲：证明原生确有 16 槽间接写者（fail-closed 保留循环）+ 删除 slot0 冗余双滚 |
| `SPWN-04` | `39ebe3b9` | 删除刷怪 Phase-B 门里自造的 `&& !boVentureServer`（全镜像多编码 0 命中） |

---

## 2. Yanshen：660 条口径重算

### 2.1 口径定义（**这是本轮最重要的口径变更，请先读**）

`ys-page2` 车道（`docs/ys_b1_yanshen2_page1_20260814.md`）证明：`眼神2(第1页)` 的 29 条 `LABEL_ONLY` 里
**20 条在原版 2.0.8 里本身就没有任何行为**——整个 45 MB 镜像（**含 delayed 转储里那 16 MB Themida 远端区**）
对这些配置字段只有三处触碰：JSON 加载器 `sub_100D6220` 写、JSON 序列化器 `sub_10004140` 读回写、GUI 提交函数读勾选框，
**没有第四处读点**。C# 侧同样只有访问器、无消费者。

⇒ **这 20 条不是缺口，是已经 1:1；为它们写实现就是臆造。** 本报告为此新增一个桶：

```
EQUIVALENT-BY-ABSENCE  =  原版可证无行为  ∧  C# 同样无行为   ⇒ 已 1:1
```

两个口径（**两个数都报，不要混用**）：

```
严格完成度   = FAITHFUL / 660
含等价完成度 = (FAITHFUL + EQUIVALENT-BY-ABSENCE) / 660
```

该判定不是"自我宽待"：`AuditTools/YanshenPage1CensusCheck`（提交 `edc062fa`，本轮实测 PASS）已把它钉成回归网，
并带**反臆造闸门**——这 20 个键对应的 30 个 `YanshenApi` 访问器一旦被任何引擎/脚本 `.cs` 点名，检查立刻变红。
自检项（引擎侧可点到的 `YanshenApi` 成员数须 ≥ 20，当前 301）确保扫描面没塌。

### 2.2 分母与基线的两处校正

1. **`tools/ys_key_reachability.py` 修正的漏报必须纳入。** 旧矩阵 `tools/ys_gui_matrix.py` 的
   `accessor_consumers()` 只给**键名字面量持有者**播种，于是**中继方法**（自身不含键名、但被行为文件调用的
   `YanshenApi` 成员）永远进不了活性图。新工具把播种改成「被行为文件点名的任意 `YanshenApi` 成员」再沿调用图传播。
   本轮在 `075d11ec` 上重跑：

   ```
   全库          {'IMPLEMENTED': 222, 'SCRIPT_ONLY': 19, 'LABEL_ONLY': 138, 'MISSING': 1}   n=380
   盘古3          IMPLEMENTED 33 / SCRIPT_ONLY 1 / LABEL_ONLY 5                              n=39
   眼神2(第1页)   IMPLEMENTED  0 / SCRIPT_ONLY 5 / LABEL_ONLY 29                             n=34
   眼神2(第2页)   IMPLEMENTED  6 / SCRIPT_ONLY 2 / LABEL_ONLY 18                             n=26
   ```

   对照：`ys-page2` 在 `38c5f107` 上跑的是 `IMPLEMENTED 219 / LABEL_ONLY 141`；差的 **+3** 正是
   `装备提升人物爆率` + `_A值` + `_B值` 三键在 `6bcdab25` 完成 `MonGetRandomItems` 段2 分母接线。
   盘古3 也因此从报告里的 30/39 走到 **33/39**。

2. **触发注册表 `Wired` 实测。** `YanshenTriggerDispatch.cs` 21 条记录：**`Wired=true` 12 / `Wired=false` 9**
   （旧审计是 8 / 13）。新接通的 4 条 = `死亡触发` `回城按钮触发` `被击杀触发` `捡物触发`
   （`d72cc932` / `f06e19fb` / `bd77fcaa`）。仍未接：`英雄穿戴触发` `新穿戴触发` `上线触发` `心灵启示触发`
   `复活触发脚本` `攻击触发` `魔法攻击触发` `盘古魔法攻击触发` `刀刀切割`。
   其中 8 条的行为文件**只有注册表本身**（纯静态数据，运行期不发射）⇒ 从 IMPLEMENTED 降 `PARTIAL`；
   `刀刀切割` 另有真实落点，沿用旧审计判 FAITHFUL 并把 `@Cutting`(`0x767BAE→0x767BB4`) 未接单列为残口。

### 2.3 六个面的刷新判定

| 面 | 条目 | FAITHFUL | PARTIAL | **EQV-BY-ABSENCE** | MISSING | FAIL-CLOSED | UNPROVEN |
|---|---:|---:|---:|---:|---:|---:|---:|
| F1 配置键 → 宿主行为 | 380 | **214** | 27 | **20** | 99 | 14 | 6 |
| F2 脚本 API 名 | 125 | **88** | 0 | 0 | 17 | 2 | 18 |
| F3 S 变量坐标组 | 34 | **3** | 1 | 0 | 27 | 0 | 3 |
| F4 回收语义契约 | 28 | **24** | 1 | 0 | 0 | 0 | 3 |
| F5 协议 / 结构断言 | 21 | **21** | 0 | 0 | 0 | 0 | 0 |
| F6 `!!!!` 命令隧道 | 72 | **71** | 0 | 0 | 0 | 1 | 0 |
| **合计** | **660** | **421** | **29** | **20** | **143** | **17** | **30** |

校验：`421 + 29 + 20 + 143 + 17 + 30 = 660` ✓

```
严格完成度      = 421 / 660 = 63.8%          （旧 57.0%，+6.8pt）
含等价完成度    = 441 / 660 = 66.8%
广义可交付度    = (421+20+17+30) / 660 = 488 / 660 = 73.9%
真实可证行为缺口 = (143 + 29) / 660 = 172 / 660 = 26.1%   （旧 37.4%）

上界（若剩余 18 条 UNPROVEN-IMPL 将来全部证真）：
  严格   439 / 660 = 66.5%
  含等价 459 / 660 = 69.5%
⇒ 真值区间：严格 63.8%~66.5%；含等价 66.8%~69.5%。区间宽度仍是 2.7pt。
```

#### F1 的构成怎么来的（可逐条复跑）

| 步骤 | 数 |
|---|---:|
| `ys_key_reachability` IMPLEMENTED | 222 |
| − 8 个「注册表唯一行为」的未接触发键 → PARTIAL | **FAITHFUL 214** |
| SCRIPT_ONLY 19 + 上面 8 | **PARTIAL 27** |
| LABEL_ONLY 138 中，`眼神2(第1页)` 的 20 条（原版无行为） | **EQV-BY-ABSENCE 20** |
| LABEL_ONLY 中，`眼神2(第1页)` 的 9 条（语义已解、挂载点在 Themida 远端区） | FAIL-CLOSED 9 |
| LABEL_ONLY 中，`盘古3` 剩余 5 条（`装备吸血` / `无极真气`×3 / `施毒术_公式值`） | FAIL-CLOSED 5 |
| LABEL_ONLY 中仍不可判：`随身仓库` `盘古高级属性` `邮件防刷` `禁止发言不提示` `英雄野蛮` | UNPROVEN 5 |
| 状态 MISSING 的 `获取玩家对象函数`（72B/84B 长载荷未逐帧回放） | UNPROVEN 1 |
| 余下 LABEL_ONLY | **MISSING 99** |
| 合计 | **380** ✓ |

> 旧审计 §2.3 的 9 条 UNPROVEN 现在只剩 6 条：`屏蔽排行榜` 已定名并落地（`3fbca823`，`sub_6CBA88` = CM **1060** 处理函数）→ FAITHFUL；
> `技能等级突破` 归入 `眼神2(第1页)` 的 20 条等价；`无极真气` 已解（`0x74587C` 是提示串 `，持续 6 秒` 的 AnsiString 长度域，纯文案，数值补丁点不存在）→ FAIL-CLOSED。

#### 各面的窗口内变化

| 面 | 变化 | 提交 |
|---|---|---|
| **F1** | 盘古3 从 `IMPLEMENTED 4/39` → **33/39**：战士合击/法道合击 12 键（含拆开两张 f64 系数表 `0x7D33FC` / `0x7D3278`，DC/CC→战士表、MC/SC→法道表，无一调用点跨界）、中毒时间上限 2 键、脚本控制人物爆率 1 键、屏蔽排行榜 1 键、装备提升人物爆率 3 键 | `8967f8ba` `dea54ad6` `ffd9c3f7` `34565196` `6bcdab25` |
| **F1** | `眼神2(第1页)` 29 条从 MISSING 重判为 **20 等价 + 9 C 类阻塞** | `0e303775` `edc062fa` `4d221c80` |
| **F2** | `ys_myjn_plus` 从 FAITHFUL 降 FAIL-CLOSED：`!!!!plus伤害` 在 ys208/ys207 两版**五编码全 0 命中**，原生无解析器；C# 现走「命令未登记」抛出（宿主回落到真 `GetBagItemCount` 这一层未复刻，登记待办） | `075d11ec` |
| **F3** | `S(1,1..150)` 登录播种（`0x100CE4EA`：`S(1,1..8)` 负值→−1、`S(1,9..150)` 负值→0、`S(1,49)`→1314）落地并接线 `PasScriptHost.cs:998` | `285add26` `b676be88` |
| **F3** | 激光两槽：`S(1,82)`（TrainSkill 的 `Random` 实参）已接，`S(1,81)` fail-closed；`AuditTools/YanshenLaserSlotsCheck` 钉住契约 | `b676be88` `1c82e86e` |
| **F5** | 装备槽 StdMode 冲突裁决完毕（腰带 54/64、鞋 52/62、宝石 53/63 判**无据**），`Yanshen207ProtocolCheck` 复绿 ⇒ 21/21 | `60e00f21` |
| **F6** | 两条中文隧道键名解错修正（`定义伤害`→`自定义伤害`、`英雄极品`→`英雄读取极品`，序列化器 `cmp [esi+off],0x1F4` + 紧邻 `push <键名VA>` 75 条整表 + 2.0.7 独立复算三重印证）；5 个自造别名（`plus伤害` `攻击伤害` `hq取sj间` `zd回收` `给予元素`）删除 | `075d11ec` |
| **F6** | 爱心分割隧道 38 臂全部无门 ⇒ 删掉 16 个无据的 caret 开关 | `6b992507` |

### 2.4 分母之外、但决定"跑得对不对"的一层：战斗五项公式层

`docs/yanshen_evidence_20260814.md` §5.6 曾明写"五项的内部数值语义没有反演到位"。本窗口把这层做掉了
（`docs/yanshen_combat_formula_20260814.md`）：

| 指标 | 值 |
|---|---:|
| 五项实现体反演到 `ret` | **5 / 5**（含 Themida 搬迁段——关键在于**用 delayed 那份转储**，已重定位那份在 `0x10EB82A1` / `0x113763CF` 等处全零） |
| 逐条判定行 | 52（施毒 9 + 麻痹 8 + 吸血 5 + 切割 12 + 自定义伤害 18） |
| 其中原本就对 | 5 |
| 非 FAITHFUL 的数值语义差异 | **47** |
| **本轮按字节修正** | **38** |
| 仍 fail-closed / 部分 | 9 判定行 → 7 条目（§8 的 F-1..F-7）+ 1 条结构性不可达 |

原生根本不是 C# 写的 `max(0,DC-AC) + baseHp*(magicLv+1)/10 + cuttingV`，而是
**`攻高 − Random(攻高−攻低−命中) − 防高`**，随后串三级宿主管线
（`sub_767BA8` 致命一击 → `VMT+0x1B0 DamageHealth` → `SendDelayMsg(10101)`），且 `cuttingV` 在**魔法护盾之后**才加。
四条上一轮记录的偏差也全部闭合：麻痹自加门去掉（`dd029ee7`）、门拓扑收拢成原生的"一族一门"（`19419d3c`）、
`-888/-777` 哨兵补齐（`3dabe8cf`）、`ys_DingShen` 按原生恒短路成 `-888`（`f487d64e`）。

> **口径提醒（沿用上一轮的分界，不要混用）**：660 条口径量的是"有原生佐证 + 有活消费者"。
> 上面这 52 条判定行属于**更深一层**（数值语义已逐指令反演），**不进 660 的分子分母**。
> 换言之：63.8% 说的是"接上了且有据"，公式层的 38 处修正提高的是同一批已计 FAITHFUL 条目的**保真度**。

### 2.5 生产部署口径（另一把尺子，仍然别和上面混用）

按生产 `D:/光头卧龙/mud2.0/Mir200/Gs1/config.json` 实测值分层（本轮重算）：

| 分层 | 旧 | **新** |
|---|---:|---:|
| 生产关闭（值 = 0）⇒ 原生不打补丁、C# 无行为 ⇒ 行为等价 | 165 | **165** |
| 生产开启且有引擎行为 | 135 | **159** |
| 生产开启但只有脚本门（SCRIPT_ONLY） | 9 | **9** |
| 生产开启且完全无行为 | 71 | **47** |

```
生产口径等价率 = (165 + 159) / 380 = 324 / 380 = 85.3%     （旧 78.9%）
若把其中 2 个「原版本身也无行为」的开启键并入 ⇒ 326 / 380 = 85.8%
```

这个数字**只对"光头卧龙"这一份部署成立**，换一份 config 就会变，不能当子系统完成度。

---

## 3. `AuditTools` 全量 PASS / FAIL 全景

### 3.1 运行方法（本环境的三个坑，写下来免得下一位重踩）

1. `dotnet run --project X -- <arg>` 在本环境**不转发 argv**。需要传参的工具必须**直接执行 exe**。
2. 工具的产物落在**三个不同位置**，必须递归找：
   - 无 `ProjectReference` 的静态分析工具 → `AuditTools/<N>/bin/Debug/net8.0*/`
   - 引用 `GameSvr`/`DBSvr` 且带 `<OutputPath>` 的 → **`D:\loym2\.claude\wt2\Build\AuditTools\<N>\[<tfm>\]`**（**跨工作树共享，会互相覆盖**）
   - `GameSvr` 自身 → `D:\loym2\.claude\wt2\Build\Mir200`（即用户点名的 `HeroLifecycleCheck` 有效构建目录）
   - 另有一枚例外：`ActiveOutgoingProtocolCheck` → `D:\loym2\.claude\wt2\tmp\active_producer2\...`
3. **工作目录决定成败**：9 个工具在"exe 所在目录"下跑会因 `FindRepositoryRoot()` 失败而假性 FAIL，
   换成仓库根就全绿。本报告的最终数字一律以**仓库根为工作目录**。

构建：`AuditTools/_buildall.proj`（MSBuild 任务批量 `Restore` + `Build`，`-m:8`），428 个工程约 42 秒。

### 3.2 全景

| 指标 | 值 |
|---|---:|
| 工程总数 | **428**（基线 `ed39ef63` 是 424，本窗口新增 4 个：`NativeCorpseGhostTimingCheck` / `YanshenEquipDropBoostCheck` / `YanshenLaserSlotsCheck` / `YanshenPage1CensusCheck`，**4 个全 PASS**） |
| **PASS** | **368** |
| **FAIL** | **59** |
| 工具源码编译不过 | **1**（`NativeDropRngSequenceCheck`：`Program.cs(106,20)/(109,9) CS8422 静态本地函数不能引用 this` + `(109,9) CS1503`。**基线同样不过**） |

### 3.3 59 个 FAIL 的 A/B 归因（基线工作树 `abbase` @ `ed39ef63`，同一批 exe 同一批断言）

| 归因 | 条数 | 说明 |
|---|---:|---|
| **基线同样失败（既有）** | **56** | 与本窗口 92 个提交无关 |
| **本窗口新引入** | **3** | **全部是回归网漂移（断言表/正则未随实现更新），无一条是行为回归** |

#### 3 条新失败逐条定性

| 工具 | 肇因提交 | 断言 | 定性 |
|---|---|---|---|
| `ChgMonItemPercentStaticCheck` | `6bcdab25`（眼神装备爆率三键接线） | `Program.cs:51` 的正则 `Random\(MonItem\.MaxPoint\b[^)]*\)\s*<=\s*MonItem\.SelPoint` | `UsrEngn.cs:2506-2510` 现在是 `Random(YanshenEquipDropBoost.Denominator(MonItem.MaxPoint * penalty, killer)) <= MonItem.SelPoint`，正则不再匹配。**行为改动本身有字节证据**（`0x100B9F9E → 0x71FD37` 46 dword 桩体，`+0x2A4` = CC 下限），但**这道守卫现在红着 ⇒ 原生掷点形状已无人守护**，必须更新期望正则 |
| `MarryClusterCompatCheck` | `2aa89895`（SGRP OthGs 第二批注记） | `Program.cs:399` 的正则要求 `case Grobal2.ISM_DIVORCE:` 与 `MsgGetDivorce(serverNum, Body); break;` **紧邻** | 该提交在两者之间插了 8 行证据注释。**行为一字未改**（`MsgGetDivorce` 仍在原位），纯脆性正则 |
| `ProvenanceGuardCheck` | `4520d53d`（MakeGhost 移除 INVENTED 分叉） | `TBaseObject.Base.cs:1545` 引用了 `staging/ref-MirServer-Delphi/EM2Engine/ObjBase.pas:18605` 却**未在同一行**标注战神 EA | 该提交正文其实给足了战神 EA（`sub_768060` / `0x7680E9` / `0x7680EF`），只是落在别的行。守卫是**逐行**扫描的 ⇒ 需要给那行补 EA 或调整守卫粒度 |

#### 56 条既有失败的性质分层

| 类别 | 条数 | 示例 |
|---|---:|---|
| 需外部依赖 / 实参（非契约失败） | 4 | `NativeHonorDbCheck`（自述 SKIP，需 MySQL）、`WeaponUpgradeRoundTripCheck`（需 MySQL）、`PasScriptAudit`（需 4 个实参）、`NativeProperTargetGateCheck`（`M2Share` 静态构造要读一个不存在的配置文件） |
| harness 未初始化 / 时序 | 2 | `NativeLevelExpTableCheck`（`TBaseObject..ctor` 空引用，`M2Share.ObjectManager` 未初始化）、`CSharpGateM2IntegrationCheck`（`OperationCanceledException`，通道时序） |
| **真实契约断言失败（基线既有）** | **50** | 见下 |

50 条真实既有失败里，有 3 条已在提交正文中被明确记录为"基线同样失败"，可直接排除误读：
- `HeroUnionStateCheck` —— `CheckMapSkillFlags` 的 `non-test permission4` 断言（`ys_b1_pangu3` §4 已记）
- `NativeTempSetMapParamPickupCheck` —— 权限文案断言（`b5936ebd` 提交正文已记）
- `NativeDropControlRuntimeCheck` —— `native fixed scatter range: expected=4, actual=3`（世界腿 4→3 的修复 `f3354457` **早于** `ed39ef63`，是工具期望表陈旧）

其余 47 条散落在 DbGate/Dispatcher/Hero/Quest/Yb/Pas 等子系统，**全部先于本审计窗口存在**，
明细见 `_toolruns_root/_summary.csv`（当前）与 `_toolruns_ab/_summary.csv`（基线）。

### 3.4 Yanshen 专用工具：20 / 20 全绿

旧 Yanshen 审计记的是「17 个工具 13 PASS / 4 FAIL，其中 3 个已不再守护它们本该守护的契约」。本轮实测：

```
Yanshen207ProtocolCheck        PASS      YanshenItemConfigCheck         PASS
Yanshen208ApiSurfaceCheck      PASS      YanshenLaserSlotsCheck         PASS  (新)
YanshenApiAccessCheck          PASS      YanshenMonsterAttrCheck        PASS  (曾 harness 腐化)
YanshenCdCompatCheck           PASS      YanshenMsgTransportCheck       PASS
YanshenConfigRuntimeCheck      PASS      YanshenMyJsonConfigCheck       PASS
YanshenDpiIsolationCheck       PASS      YanshenPage1CensusCheck        PASS  (新，含反臆造闸门)
YanshenEquipDropBoostCheck     PASS (新) YanshenPaintDiagnostic         PASS
YanshenHalfMoonCompatCheck     PASS      YanshenRecycleConfigCheck      PASS
                                   (曾编译不过)
YanshenHeroCastCommand28Check  PASS      YanshenSunSwordCompatCheck     PASS
                                         YanshenTriggerDispatchCheck    PASS  (曾期望表漂移)
                                         YanshenWarriorSkillCompatCheck PASS
```

**旧审计的 P0「先把审计链修好」已完成。** 反过来，主引擎侧新出现了 3 处同类漂移（§3.3）——
这类问题会周期性复发，建议把 `_buildall.proj` + 本报告的运行脚本固化成 CI 的一步。

---

## 4. 距离"百分百"还差什么 —— 按优先级排序

排序原则：**能直接搬走百分点的排前面；只能搬"保真度"或需要先拿证据的排后面。**

### P0 — 先修回归网（3 条，代价以小时计，不修则后续所有改动无护栏）

1. `ChgMonItemPercentStaticCheck` 期望正则更新以容纳 `YanshenEquipDropBoost.Denominator(...)` 包裹
   —— **这条最急**：它守的是原生掷点形状（`Random(MaxPoint 派生分母) <= SelPoint`），现在红着等于没守。
2. `MarryClusterCompatCheck` 的 `ISM_DIVORCE` 正则改成允许中间夹注释。
3. `ProvenanceGuardCheck`：给 `TBaseObject.Base.cs:1545` 那行补战神 EA，或把守卫粒度从"逐行"改成"逐注释块"。
   顺带：`NativeDropControlRuntimeCheck` 的 `fixed scatter range` 期望值 4 应按 DROP-33 证据改 3。
4. `NativeDropRngSequenceCheck` 的 `CS8422/CS1503` 编译错误（基线既有，该契约当前**零守护**）。

### P1 — 主引擎最后 3 条真实缺口（能把 92.0% 推到 ~92.4%，并让 A 类归零）

5. **MOVE-74**（穿透态 `0xB05`/2821 变化广播）。纯发包，`vmt+0x250`，TRUE `push 6/1/0/0` / FALSE `push 6/0/0/0`，
   缓存字段与刷新点都已在位。**性价比最高的一条。**
6. **MOVE-73**（判定改回 tick 缓存）。需要能在 `Run()`（= `sub_6B2D38`）里挂刷新，
   而 `TPlayObject.Message.cs` 是禁改文件 —— **这是一个流程闸门问题，不是技术难题**，请主代理放行插桩点。
7. **SPWN-56**（视野摘链谓词）。前置条件是先补齐 Delphi 的对象拆解语义（`Free`/`MakeGhost` 时清 `m_sCharName`
   与 `m_PEnvir`），牵动全树对象生命周期 + 全服最热路径，**风险最高、收益最低（0.13pt），建议最后做或永久记为有界偏差**。

### P2 — Yanshen 能直接搬点数的两块（每块 ~1.4~4pt）

8. **A 类外科接线的残余 9 条触发点**（`英雄穿戴触发` `新穿戴触发` `上线触发` `心灵启示触发` `复活触发脚本`
   `攻击触发` `魔法攻击触发` `盘古魔法攻击触发` + `@Cutting`）。每条都有挂载 VA / 覆盖长度 / 槽号 / 参数个数，
   `YanshenTriggerDispatch.Registry` 已把原生事实存成静态数据，接线就是"在宿主同位点调一次 `DispatchPlain`"。
   **两个陷阱**：①`心灵启示触发`(`0x6EDC2B`) 是**顶掉型**，不重放被覆盖字节，接错方向会让原生动作双跑；
   ②`武器绿毒` 与 `物功带毒` 打**同一个** `0x76E2BC`，必须做成互斥的一套毒源。
   预计 +8~9 条 ⇒ 严格约 **+1.4pt**。
9. **B3 S 变量消费层 27 条 MISSING**（永久属性 22 槽 `S(1,13..23)`/`S(1,31..41)`、切割族 `S(1,9..11,50..53,62,63)`、
   技能变址 `S(7/8/9, magicid)`）。前置 `S(1,1..150)` 播种**已经通了**（`PasScriptHost.cs:998`），
   所以这一族接上就是活的。预计 +27 条 ⇒ 严格约 **+4.1pt**。

### P3 — Yanshen 剩余 MISSING 的大头（99 条 F1 + 17 条 F2）

10. `盘古1`（29 LABEL_ONLY）、`眼神2(第2页)`（18）、`配置1`（15）、`盘古2`（11）、`技能相关`（9）、
    `群毒`（6）、`配置2`（7）——**都要先做 §2.1 那套"原版到底有没有行为"的普查**，
    因为 `眼神2(第1页)` 的教训是：**29 条里有 20 条根本不该写代码**。
    `tools/ys_page1_census.py` + `docs/ys_patch_label_atlas.tsv`（107 特性 / 407 站点）已经把方法与全插件补丁面备好，
    对别的页可以直接复用。**先普查再动手，能避免把"等价"做成"臆造"。**
11. B2 高站点数补丁未接：`屏蔽属性提升提示` 31 站点、`免毒符` 12、`永久属性` 12、`屏蔽元宝增减信息` 8
    —— 载荷字节完整，缺的是 C# 落点映射。
12. F2 的 17 条未登记脚本 API + 18 条 `UNPROVEN-IMPL`（官方例子名，三份底本零命中）。

### P4 — 需要先取证据才能推进（不是移植工作量，是逆向工作量）

13. **Themida 远端调用链 —— 这是两条战线共同的、也是唯一的"硬"瓶颈。**
    症状：`0x10400000..0x11400000` 这 16 MB 在**已重定位**那份 2.0.8 转储里 `gap_zero_ratio = 1.0`（全零），
    只有 **delayed 那份**（PE 基址 `0x57C40000`，绝对操作数需 `−0x47C40000` 还原）才有内容
    （实测非零 `16,506,511 / 16,777,216`）。它当前卡住的东西：
    - **9 条 `眼神2(第1页)` fail-closed 键**：伤害后处理流水线 `sub_100795C0` 在两份转储里
      **rel32 调用者只有 1 处**——`0x10F2D759`，就落在远端混淆区；`.rdata` trampoline 模板里
      指向插件 `.text` 的 7 处 `E8/E9` 无一指向它。**5 条切割键 + `英雄千分比免伤` 的语义已经逐字节备齐
      （`magicId → S(1,116..120)`、tag/值槽偏移已由 `YanshenPage1CensusCheck` 钉住），只差一个挂载点。**
    - `主号高级暴击` / `高级英雄倍功暴击`：另加载荷不可判（`[0x1031C250]`/`[0x1031C254]` 两份转储皆为 0，
      `pushal/popal` + 返回值走 `ecx`，目标运行期现搭）。
    - `英雄野蛮`（两个宿主函数指针在虚拟化前半段赋值）、`S(1,1)` 禁言模式 6/7/8（SetS detour `0x100CEB40`）、
      `Ys_HuiShou` 的脚本注册点、操作码 1（`ys_myysjn`）的实现体（臂 `0x1007670A` → `call 0x10DF3A91`）。
    **解法二选一**：(a) 对远端区做去混淆 / 反虚拟化，把 `0x10F2D759` 的调用链上溯到某个 trampoline；
    (b) **活体调试：在 `0x100795C0` 下断点读返回地址链**（这条更快）。任一成功，一次性解锁 6+ 条。
14. **缺一份"插件未加载"的 M2Server 转储**：磁盘 `M2Server.exe` 被 VMProtect 加壳（`CODE` 节 `SizeOfRawData = 0`），
    没有 stock 基线 ⇒ 一切 `orig`/`stock` 断言不可判，96 个随机极品槽里 24 个双向冲突无从裁决。
15. 主引擎 C 类 43 条 UNPROVEN + 12 条 fail-closed：`DURA-37/38/40/41/42/43`（入口/可达性/装卸 opcode/超重后果）、
    `SGRP-35/40/41` 与 `TRADE-61/44`（**依赖 DBSvr 侧或运行期配置，非本仓可闭合**）、其余 32 条负命题。

### 一句话优先级

> **P0 修 4 个守卫（小时级）→ P1 补 MOVE-74（半天）+ 放行 MOVE-73 插桩点 → P2 接 Yanshen 9 个触发点 + S 变量消费层（能把 Yanshen 从 63.8% 推到 ~69%）→ P3 逐页做"原版有无行为"普查再动手 → P4 抓一份能看到 Themida 远端调用链的转储（这是唯一的硬瓶颈）。**
> **SPWN-56 与 DBSvr 依赖项建议永久记为有界偏差 / 外部边界，不要为 0.4% 去动全服最热路径。**

---

## 附录 A：复现足迹

```
工作树      git worktree add D:\loym2\.claude\wt2\audit3 -b w/audit3 master      HEAD 075d11ec
A/B 基线    git worktree add --detach D:\loym2\.claude\wt2\abbase ed39ef63
python      C:\Users\Administrator\AppData\Local\Programs\Python\Python311\python.exe   (3.11.9 + capstone 5.0.7)
            注意：PATH 上的 python.exe 是 Windows Store 别名，直接调会「拒绝访问」，必须走绝对路径或 py -3.11
```

本轮新增的四份脚本（随本提交入库，均只读）：

| 文件 | 用途 |
|---|---|
| `AuditTools/_buildall.proj` | MSBuild 批量 `Restore`+`Build` 全部 428 个工程（`dotnet build AuditTools\_buildall.proj -m:8`） |
| `tools/audit_run_all.ps1` | 全量跑工具（三处产物目录递归定位 + 仓库根为工作目录 + 超时 + 退出码 + 落盘 CSV） |
| `tools/audit_run_ab.ps1` | 在基线产物目录上重跑失败集，做 A/B 归因（含 `-p:OutputPath` 防共享目录互相覆盖的说明） |
| `tools/ys_reach_bypage.py` | `ys_key_reachability` 的按页分解版，另导出 380 键逐键 `页/状态/生产值` 的 TSV |

产物（未入库，留在工作树内供复核）：
`_toolruns/`（首轮，cwd=exe 目录）、`_toolruns_root/`（**权威**，cwd=仓库根）、`_toolruns_ab/`（基线 `ed39ef63`）、
`_ys_reach_bypage.txt` + `_ys_reach_rows.tsv`（380 键逐键 页/状态/生产值）、`_buildall.log` / `_ab_build.log`。

关键复现命令：

```powershell
# Yanshen 380 键逐键可达性（修正了旧矩阵「只给键名字面量持有者播种」的漏报）
python tools\ys_key_reachability.py D:\loym2\.claude\wt2\audit3
#   -> {'IMPLEMENTED': 222, 'SCRIPT_ONLY': 19, 'LABEL_ONLY': 138, 'MISSING': 1}

# 触发注册表接通数
#   -> Wired=true 12 / Wired=false 9   (GameSvr/Plugins/YanshenTriggerDispatch.cs)

# AuditTools 全量
dotnet build AuditTools\_buildall.proj -m:8
powershell -File tools\audit_run_all.ps1        #  368 PASS / 59 FAIL / 1 NOEXE
powershell -File tools\audit_run_ab.ps1         #  59 FAIL 中 56 条基线同样失败
```

## 附录 B：本报告未做的事（边界声明）

1. **未逐条重跑 29 份分片的原始核验。** 本报告以旧主报告的 HEAD 对齐结果（651/23/…）为起点，
   只对**窗口内 92 个提交触碰到的契约**做逐条复核（22 条闭合 + 3 条残留 + DURA/SGRP 组重判），
   其余 651 条 FAITHFUL 未重新反汇编。依据是：A/B 工具对照显示本窗口**没有引入任何行为回归**（3 条新失败全是断言漂移）。
2. **未验证 Yanshen 143 条 MISSING 的"原版是否真有行为"。** 只有 `眼神2(第1页)` 做过这项普查（20/29 判等价）。
   其余页很可能也含相当比例的 EQUIVALENT-BY-ABSENCE ⇒ **当前 63.8% 大概率是低估**，但在普查做完之前不上调（不虚报）。
3. **未跑需要 MySQL / 实参的 3 个工具**（`NativeHonorDbCheck` / `WeaponUpgradeRoundTripCheck` / `PasScriptAudit`），
   它们在 §3.3 计入"需外部依赖"而非契约失败。
4. **未改任何 `.cs`。** `git diff master --stat` 只有本文档与三份脚本。
