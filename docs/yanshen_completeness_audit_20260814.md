# 眼神(Yanshen)插件子系统 · 完成度审计报告

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\ys-audit`　分支：`w/ys-audit`　审计基线：**`186ef170`**
  （`armor-agg: 落地 self+0x2DC 百分比物理减伤总量为真实聚合字段(解锁 POIS-39)`，创建工作树时的 `master`）
- 仓库真实根：`D:\loym2\LyoMir2-master`（`D:\loym2` 本身不是 git 仓库，`.git.broken-20260810` 是废弃目录）
- 底本：
  - M2Server 平坦镜像 `staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`（17,661,952 B）
  - 眼神 2.0.8 脱壳转储 `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin`，基址 `0x10000000`（45,821,952 B）
  - 生产配置 `D:/光头卧龙/mud2.0/Mir200/Gs1/config.json`（GBK，10,279 B，**380 键**）
  - 生产 `MyJson/recycle.json`（11,514 B，md5 `AF897884085B82D818352303F897F85F` —— 本轮当场复核）
- 工具：python 3.11 + capstone 5.0.7；`dotnet 10.0.302`
- 纪律：**只读审计，未改任何 `.cs`**。本次提交只含本文档。所有百分比按契约条数计，逐条可回溯到原生地址或 `文件:行`。**不虚报，不按代码体量推断。**

---

## 0. 执行摘要（结论先行）

### 0.1 为什么要单独做这一份

等价账本 `staging/equivalence_ledger_20260810.tsv` 共 **755 行 = 1 表头 + 754 条契约**，本轮当场做了前缀普查：

```
MOVE 98  MINE 61  TRADE 61  SPWN 60  STATE 52  CRAFT 47  DURA 45  SGRP 45
ECON 40  POIS 38  DROP 38   QST 32   MFLG 31   GILD 29   TCFP 28  PRICE 25  CGLD 24
```

合计 `98+61+61+60+52+47+45+45+40+38+38+32+31+29+28+25+24 = 754` ✓，**这 17 个前缀里没有任何一个属于眼神**（`YS-` / `YANSHEN-` 契约号 0 条；全表对 `YS-|YANSHEN|眼神` 的 3 处子串命中分别落在 `TCFP-02` / `TCFP-28` / `MOVE-32` 的描述列里，与眼神无关）。
因此 29 分片核验与 **89.4%** 完成度（`docs/completeness_audit_20260814.md`）**完全没有覆盖眼神子系统**。本文补的就是这块家底。

### 0.2 结论

| 指标 | 值 | 口径 |
|---|---:|---|
| 条目总数（分母） | **660** | 6 个互不重叠面之和，见 §1.1 |
| FAITHFUL | **298** | 有原生字节/地址佐证 **且** C# 有活消费者 |
| PARTIAL | **34** | 有部分行为，但未达 1:1（含"只入注册表未发射"） |
| MISSING | **213** | 原生有行为，C# 无任何消费者 |
| FAIL-CLOSED | **2** | C# 明确拒绝执行而非假装成功 |
| UNPROVEN | **113** | 见 §0.3 二分 |
| **严格有据完成度** | **298 / 660 = 45.2%** | `FAITHFUL / 总数` |
| **广义可交付度** | **413 / 660 = 62.6%** | `(FAITHFUL + FAIL-CLOSED + UNPROVEN) / 总数` |
| **真实可证行为缺口** | **247 / 660 = 37.4%** | `(MISSING + PARTIAL) / 总数` |

**一句话**：眼神子系统按同一把尺子量，**严格完成度 45.2%**，远低于主引擎的 89.4%。差距不是"没干活"（`git log --grep=anshen` 在审计基线上 **72** 个提交、全部 ref 上 73 个；C# 落地约 850 KB），而是**大量功能停在"键被解析、访问器存在、但没有任何游戏代码去读它"这一层**——380 个配置键里 167 个属于此类，其中 **71 个在生产 config 里是开着的**。

### 0.3 UNPROVEN 必须二分，否则会误读

| 子类 | 数 | 含义 |
|---|---:|---|
| `UNPROVEN-IMPL` | **98** | C# **已实现并接线**，但实现依据是随包 Pascal 声明/官方文档，**在本仓找不到原生 VA 佐证**。可能对、可能错，未证。 |
| `UNPROVEN-BLOCKED` | **15** | 原生侧静态不可判（Themida 虚拟化零页 / 模板未回收 / 无 stock 基线），C# 已正确不臆造。 |

只有后者可以像主报告那样"算作 C# 无过错"。**前者 98 条是本子系统最大的证据债**。

### 0.4 生产部署口径（另一把尺子，别和上面混用）

配置键面按生产 `config.json` 实际取值分层：

| 分层 | 键数 | 行为等价性 |
|---|---:|---|
| 生产关闭（值 = 0） | 165 | 原生不打补丁、C# 无行为 ⇒ **行为等价** |
| 生产开启且有引擎行为 | 135 | 有行为 |
| 生产开启但只有脚本门（SCRIPT_ONLY） | 9 | 脚本调得到，引擎侧无补丁行为 |
| **生产开启且完全无行为** | **71** | **真实可观测缺口** |

⇒ 配置键面的**生产口径等价率 = (165 + 135) / 380 = 300 / 380 = 78.9%**（把 9 个 SCRIPT_ONLY 从保守出发算作无行为）。
这个数字只对"光头卧龙"这一份部署成立，换一份 config 就会变；**不能拿它当子系统完成度**。

### 0.5 一条 INVENTED

矩阵生成器同时做反向检测（C# 查了、但生产 config **和** 45 MB 转储字符串里都没有的键），本轮命中 1 条：

- `道士合击系数_数值` —— `GameSvr/Plugins/YanshenFixedReplicaPanels.cs:484`

它不在 380 键分母里（因为它不是原生键），登记于此以示不隐瞒。生产实际存在的是带序号的
`道士合击系数_数值1..5`（5 键，均 LABEL_ONLY、生产 0），这条无序号的多半是面板文案笔误。

---

## 1. 原生面划定与条目清单（分母怎么来的）

### 1.1 六个互不重叠的面

| # | 面 | 条目定义 | 数 | 可复现来源 |
|---|---|---|---:|---|
| F1 | 配置键 → 宿主行为 | 生产 `config.json` 的每一个键 | **380** | `python tools/ys_gui_matrix.py`（本轮重跑） |
| F2 | 脚本 API 名 | 2.08 随包脚本真实存在的函数名 | **125** | `PasApiBridge.Yanshen.cs:11-13` 口径（AllFuc.pas 104 + NpcFuc.pas 1 + 官方例子 20） |
| F3 | S 变量坐标组 | 三条读写路径普查出的坐标行 | **34** | `docs/ys_svars_three_path_census_20260813.md` §4.1（33 行）+ §5（`S(1,123/124)`） |
| F4 | 回收子系统语义契约 | 判定链/结算/缺陷逐条 | **28** | `docs/ys_recycle_impl_20260813.md` §6（15 + 6 + 2 + 1 + 1 + 3） |
| F5 | 协议 / 结构编解码断言 | `Yanshen207ProtocolCheck` 的具名检查 | **21** | 该工具可直接 `dotnet run` |
| F6 | `!!!!` 命令隧道入口 | 数字 40 + 分隔符 16 + 中文 11 + 给予格式 5 | **72** | `YanshenCommands.cs:11-14` 自述 + 本轮解析 |
| | **合计** | | **660** | |

**为什么不把"触发派发 21 开关 / 25 站点"和"随机极品 96 键"单列**：它们**本身就是 380 个配置键中的成员**（触发族 21 键、盘古4 页 96 键）。单列就是重复计数。本报告改为把它们当作**证据**去修正 F1 的判定——见 §2.2 的 12 条降级。

**为什么 F6 与 F2 不算重复**：F6 的条目是**插件自己的 `!!!!` 解析器入口**（opcode → 段数校验 → 开关门 → 短包返回值），F2 的条目是 **PAS 引擎的函数名**。两者是两个独立的原生调用面，只是下游落到同一批 `YanshenApi` 方法。

### 1.2 本轮亲手复核的原生锚点（capstone，`flat_image.bin` / base `0x400000`）

不是抄文档，是当场反汇编。全部逐字节吻合：

```
006E42CC  69C2E8030000   imul eax, edx, 0x3E8      ; S/V 银行扁平键 = group*1000+index
006E42D2  03C1           add  eax, ecx
006DF1CF  8B9304080000   mov  edx,[ebx+0x804]      ; GetS 读玩家 S 银行指针
006DF26D  8D9304080000   lea  edx,[ebx+0x804]      ; SetS

0076C88B  8BC6 5F 5E 5B  mov eax,esi / pop edi/esi/ebx   ; 新倍攻和暴击 挂载 5B → resume 0x76C890
0076C816  83BB8400000000 cmp dword [ebx+0x84],0          ; 英雄倍攻和暴击 挂载 7B → resume 0x76C81D
006EDC5E  E819120800     call 0x76EE7C                   ; 召唤神兽触发（顶掉这条 call）
006EDB44  E8B3120800     call 0x76EDFC                   ; 召唤骷髅触发（顶掉）
006EC111  66837E2600     cmp word [esi+0x26],0           ; 挖矿触发
006C09B5  5F 5E 5B       pop edi/esi/ebx                 ; 死亡触发
0076631C  55 8BEC 53     push ebp/mov ebp,esp/push ebx   ; BB死亡触发（改写序言）
0071F467  5E 5B 59       pop esi/ebx/ecx                 ; BB杀怪触发（收尾）

007608EF  B80A000000     mov eax,0xA                     ; 武器最随机性_极品 出厂 10
0076090E  BA14000000     mov edx,0x14                    ; 武器点数几率_攻击 出厂 20
00760913  B806000000     mov eax,6                       ; 武器最高点数_攻击 出厂 6
004C707C  55 8BEC …      sub_4C707C 伯努利计数器
006D791C  55 8BEC 53 …   IncGold；0x6D7926 mov ebx,[eax+0x15C]（金币）/ 0x6D792C add ebx,edx
0077A3FD  81FAC0270900   cmp edx,0x927C0                 ; 地面物品消失时间原生 600s
006E7C38  55 8BEC 53 56  sub_6E7C38 序言（关闭摆摊 补成 C3）
006C5BCB  7449           je 0x6C5C16                     ; 行会显示 被 NOP 的第一条 je
```

复现脚本：`%TEMP%\ys_native_verify.py`（只读）。

> **`0x76C816` 的 7 字节覆盖恰好是那条 `cmp dword [ebx+0x84],0`**，与 `YanshenTriggerDispatch.cs` 注册表里
> `HostTargets = 0x0076C816 / HostResumes = 0x0076C81D` 严丝合缝；`0x76C88B` 的 5 字节覆盖恰好是
> `mov eax,esi` + 三个 `pop`，也解释了 `ys_trigger_dispatch §…"改写后的 esi 经重放的 mov eax,esi 成为返回值"`。
> 两条注册表记录**独立于文档被本轮验证为真**。

---

## 2. 逐面判定

### 2.1 F1 配置键面（380）—— 本轮重跑，与 8-13 快照有差

`python tools/ys_gui_matrix.py --repo <worktree> --out %TEMP%\ys_matrix_now` 在基线 `186ef170` 上输出：

| 状态 | `docs/ys_gui_matrix.tsv`（8-13） | 本轮（`186ef170`） | 差 |
|---|---:|---:|---|
| IMPLEMENTED | 173 | **184** | +11 |
| SCRIPT_ONLY | 22 | **20** | −2 |
| LABEL_ONLY | 184 | **175** | −9 |
| MISSING | 1 | **1** | 0 |
| INVENTED（分母外） | 1 | **1** | 0 |
| 生产开启无行为（不含 SCRIPT_ONLY） | 80 | **71** | −9 |

11 条状态迁移（全部向好，逐条可回溯到提交）：

| 键 | 旧 → 新 | 落点 |
|---|---|---|
| `全局循环函数` | SCRIPT_ONLY → IMPLEMENTED | `YanshenRecycleDriver.cs` |
| `循环时间_值` | LABEL_ONLY → IMPLEMENTED | `YanshenRecycleDriver.cs:127` |
| `高级回收` | SCRIPT_ONLY → IMPLEMENTED | `TPlayObject.Message.cs:346` |
| `盘古冰咆哮的范围` / `_范围值` | LABEL_ONLY → IMPLEMENTED | `YanshenSkillPatches.cs:90-92` |
| `盘古地狱雷光范围` / `_范围值` | LABEL_ONLY → IMPLEMENTED | `YanshenSkillPatches.cs:87-89` |
| `盘古流星火雨范围` / `_范围值` | LABEL_ONLY → IMPLEMENTED | `YanshenSkillPatches.cs:93-95` |
| `盘古爆裂火焰范围` / `_范围值` | LABEL_ONLY → IMPLEMENTED | `YanshenSkillPatches.cs:84-86` |

### 2.2 **对矩阵的一处修正：12 个触发键被高估**

矩阵把"键在某个非 GUI 的 `.cs` 里出现"当作有行为。`YanshenTriggerDispatch.cs` 里 21 条注册表记录**全部**含键名字符串，于是 21 个触发键**一律**被判 IMPLEMENTED。但注册表自带 `Wired` 字段，本轮统计：

```
注册表条目 21   Wired=true 8   Wired=false 13
```

| Wired | 键 |
|---|---|
| **true（8）** | 召唤神兽触发、召唤骷髅触发、BB杀怪触发、BB死亡触发、死亡触发、挖矿触发、盘古穿戴触发、新倍攻和暴击 |
| **false（13）** | 英雄穿戴触发、新穿戴触发、上线触发、回城按钮触发、心灵启示触发、复活触发脚本、被击杀触发、捡物触发、攻击触发、魔法攻击触发、盘古魔法攻击触发、刀刀切割、英雄倍攻和暴击 |

13 条里有 12 条的 `behavior_files` **只有** `YanshenTriggerDispatch.cs` 一个文件——即注册表是它们**唯一**的"行为"，而注册表是纯静态数据，运行期一次都不会发射。
第 13 条 `刀刀切割` 另有 `TPlayObject.NativeSocialSlots.cs` 等真实落点，只是它的 `@Cutting` 回调（宿主 `0x767BAE → 0x767BB4`）没接，故保留 IMPLEMENTED 并单列为 A 类小缺口。

**⇒ 12 键 IMPLEMENTED 降级为 PARTIAL。**（这 12 个键生产值都是 0，故不影响 §0.4 的生产口径数字。）

### 2.3 F1 里的 UNPROVEN（9 键）

出自 `ys_gui_impl2 §5` 的 BLOCKED 分类，本轮逐条复核仍未解：

| 键 | 状态 | 生产 | 卡在哪 |
|---|---|---|---|
| `获取玩家对象函数` | MISSING | 1 | 72 B / 84 B 长载荷未逐帧回放（`0x646F40` `0x647D24`） |
| `随身仓库` | LABEL_ONLY | 1 | 45 B 整函数替换载荷未回放（`0x6E087C`） |
| `盘古高级属性` | LABEL_ONLY | 1 | 43 B 载荷未回放（`0x6F9AB0`） |
| `无极真气` | LABEL_ONLY | 1 | 只有目标 VA `0x74587C`，未反汇编写入的是时间还是系数 |
| `邮件防刷` | LABEL_ONLY | 1 | 启用臂是 `0x10032FD0` 组装的蹦床，桩体运行时汇编 |
| `禁止发言不提示` | LABEL_ONLY | 1 | 3 站点里 `0x6C94A9` 所在函数未定名，只接 2/3 不算 1:1 |
| `技能等级突破` | LABEL_ONLY | 1 | 图谱 121 补丁面**无**该键的 memcpy/trampoline 站点，实现位置未解 |
| `屏蔽排行榜` | LABEL_ONLY | 0 | `0x6CBA88 55→C3`，C# 落点未定名（跳转表分发器） |
| `英雄野蛮` | LABEL_ONLY | 0 | 两个宿主函数指针在 Themida 虚拟化前半段赋值，落全零区，**永久不可判** |

### 2.4 F1 判定小结

| 判定 | 数 | 说明 |
|---|---:|---|
| FAITHFUL | **172** | 184 − 12（§2.2 降级） |
| PARTIAL | **32** | SCRIPT_ONLY 20 + 触发注册表未发射 12 |
| UNPROVEN | **9** | §2.3 |
| MISSING | **167** | LABEL_ONLY 175 − 8 转 UNPROVEN |
| 合计 | **380** | ✓ |

MISSING 的重灾区（按页；`IMPLEMENTED` 列是矩阵原始值，**未扣 §2.2 的 12 条降级**，因为那 12 条散在盘古1/2 与眼神页）：

| 页 | 键数 | IMPLEMENTED | LABEL_ONLY |
|---|---:|---:|---:|
| 盘古4（随机极品） | 98 | **97** | 0 |
| 配置2 | 31 | 17 | 14 |
| 盘古2 | 48 | 28 | 17 |
| 盘古1 | 51 | 20 | 30 |
| 配置1 | 33 | 13 | 15 |
| **盘古3** | **39** | **4** | **34** |
| **眼神2(第1页)** | **34** | **0** | **29** |
| 眼神2(第2页) | 26 | 5 | 18 |
| 扩展/技能相关 | 9 | 0 | 9 |
| 扩展/物品相关 | 4 | 0 | 3 |
| 扩展/脚本相关 | 5 | 0 | 4 |
| 扩展/角色相关 | 2 | 0 | 2 |

`盘古4` 一页 97/98 落地，是 `ys_gui_extreme_20260813.md` 把 96 个立即数键对上宿主 VA 的成果——它一页就贡献了当前 IMPLEMENTED 的 **53%**。反过来，`盘古3`（34/39 无行为）与 `眼神2(第1页)`（29/34 无行为）是两个未开垦的整页。

### 2.5 F2 脚本 API 面（125）

口径见 `PasApiBridge.Yanshen.cs:11-13`：AllFuc.pas 声明 104 + NpcFuc.pas 1 + 官方《AllFuc 使用例子》给出的 20 个插件原生注册名 = **125**，其中 **108** 登记在 `YanshenApiNames`。

本轮机器核对（`%TEMP%\ys_api_census.py`）：

```
registered catalog names : 108
dispatch-switch arms     : 108
registered but NOT dispatched : 0
dispatched but not registered : 0
```

⇒ **登记面与派发面严格闭合，无悬空、无私货。** 这是本子系统做得最干净的一处。

再做**证据密度**核对（`%TEMP%\ys_api_evidence.py`）：把每个名字沿 `api.Xxx(` 解析到 `YanshenApi` 成员，检查该成员前后 ±70 行内是否出现原生 VA（`0x100xxxxx` 插件 / `0x4x-0x7x` 宿主）：

```
arms whose impl cites a native VA : 40
arms with no VA anywhere near     : 68
YanshenApi.cs 内原生 VA 记号总数   : 526
```

68 个无字节佐证的名字里包括 `ys_shidu`(施毒)、`ys_mymabi`(麻痹)、`ys_xixue`(吸血)、`ys_cutting`(切割)、
`ys_myjn_plus2/super/undead/delay/effect`(自定义伤害五变体)、`ys_jitui/jitui2/tuitui/tuitui2`(推拉四式)、
`ys_sqldbinsert/sqldbselect/senddbmsg`(DB 三件套)、`ysgetg/yssetg/ysgetstr/yssetstr`(全局变量四件套) ——
**都是战斗与数据主干**。它们能跑、有门控、有测试，但"跑得对不对"没有原生字节背书。

| 判定 | 数 | 依据 |
|---|---:|---|
| FAITHFUL | **39** | 40 个有 VA 佐证 − 1 个 fail-closed |
| FAIL-CLOSED | **1** | `ys_settimerbyname` → `YanshenApi.SetLoopTimer` (`YanshenApi.cs:3385-3391`) 直接 `throw new YanshenApiUnavailableException`：具名定时器注册**未实现**，不假装成功 |
| UNPROVEN-IMPL | **68** | 已实现已接线，无原生 VA |
| MISSING | **17** | 125 − 108，声明存在但未登记 |
| 合计 | **125** | ✓ |

> `SetLoopTimer` 与 §2.1 的 `全局循环函数` 不矛盾：`YanshenRecycleDriver` 实现的是**单例的 `MyTimer` 全局节拍**（原生 `0x1008C7C0`，周期读 `0x1008C7E7` ← 配置单例 `+0x938`），而 `Ys_SetTimerByName` 是**任意具名定时器注册**，是另一件事，仍未实现。

### 2.6 F3 S 变量坐标面（34）

**本轮复核（`ddf7fd1a` + capstone 亲验 `0x100CE4EA`）**：C# 侧 `TryGetScriptVar('S', …)` 已扩至
`YanshenSkillPatches` / `YanshenTriggerDispatch` / `YanshenTriggerDispatch.Wave2` /
`YanshenPage1PostDamage` / `YanshenLaserSlots` / `TPlayObject.YanshenSVarSeed`。

已接线坐标（逐字节对账）：

```
登录灌种 S(1,1..150)+哨兵 S(1,49)     → TPlayObject.YanshenSVarSeed.cs:70-99（0x100CE4EA）
倍攻暴击 S(1,64/67/68)               → YanshenTriggerDispatch.cs:773-779（0x100D3BC4）
英雄倍攻 S(1,44/45/46)               → YanshenTriggerDispatch.Wave2.cs:66-78（0x100D49B4）
技能主/范/除/系数 S(1,74..92) 子集   → YanshenSkillPatches.cs（0x100D8A3D 等）
激光槽② S(1,82)                       → YanshenLaserSlots+MagicManager.cs:423（0x76EA14）
激光槽① S(1,81)                       → YanshenLaserSlots 读取器已钉住、**故意不接**
                                       （0x76FEA7 arg0 写 struct[0x2C] 但 type=2 伤害链不读 ——
                                       YanshenLaserSlotsCheck + MagicManager.cs:424-431）
野蛮 S(1,95)                           → YanshenSkillPatches.cs:197（0x100DB11C）
五法术切割 S(1,116..120)               → YanshenPage1PostDamage.cs:54-65（0x1007AF46 cmp 0x522）
```

对照 `ys_svars` §4.1 的 33 行 + §5 的 `S(1,123)/S(1,124)`：

| 判定 | 数 | 条目 |
|---|---:|---|
| FAITHFUL | **4** | `S(1,49)+登录灌种`（`0x100CE4EA`）；`S(1,64/67/68)`；`S(1,74..92)`（81 死参 fail-closed、82 已接线）；`S(1,95)` |
| PARTIAL | **0** | — |
| UNPROVEN-BLOCKED | **3** | `S(1,1)` 禁言模式 6/7/8（SetS detour `0x100CEB40` 跳进全零 16 MB）；`S(1,12)/S(1,30)`（模板 `0x100D120A` 未回收）；`S(1,123)/S(1,124)`（C# `PasApiBridge.Yanshen.cs:477-478` 有消费者，三条原生路径都没找到读点） |
| MISSING | **27** | 其余全部（含 `S(1,9..11,50..53,62,63)` 刀刀切割族、`S(1,13..23,31..41)` 永久属性 22 槽、`S(7/8/9,magicid)` 变址等） |
| 合计 | **34** | ✓ |

27 条 MISSING 里最要紧的三块（播种已落地，不再阻塞下游门控）：

1. **刀刀切割族** `S(1,9..11,50..53,62,63)` + 派发门 `S(1,65)==100`：原生 `0x100CF36E` 用裸银行偏移 + 对象字段合成伪随机（`+0x18/+0x470`），C# 无对应模型，仍 fail-closed。
2. **永久属性 22 槽**（`S(1,13..23)` 主号 / `S(1,31..41)` 英雄经 `[hero+0x68C]`，宿主 `0x73D9CF..0x73DA3A` 12 个 memcpy 站点）——模板 `0x100D120A` 未回收，C# 无消费者。
3. **技能变址** `S(7/8/9, magicid)` 与 §4.1 其余 R 行——路径 1/2 已证实读点，C# 未接。

### 2.7 F4 回收子系统（28）—— 本轮最大的好消息

`ys_recycle_impl_20260813.md` §6 当时的账是 `FAITHFUL 15 / DIVERGENT 6 / 原生缺陷 2 / INVENTED 1 / MISSING 1 / BLOCKED 3`。本轮逐条复核 `186ef170`，**六条 DIVERGENT 全部回正、INVENTED 已删、MISSING 已驱动、两条原生缺陷已照抄**：

| 编号 | 8-13 状态 | 现状 | 证据 |
|---|---|---|---|
| D1 结算/删除次序 | C# 先付后删 + 回滚 | **回正**：先删后结算，删除段后不允许 `return` | `YanshenApi.cs:1979-1992` + 方法头逐字节注释 |
| D2 货币落账粒度 | C# 每件即时 | **回正**：四路只累加，循环后 `SettleRecycleTotals` 一次 | `YanshenApi.cs:1727, 1844-1870` |
| D3 产出为正的判据 | C# 判缩放后 | **回正**：判缩放前、未乘件数的五个单价 | `YanshenApi.cs:1975-1977` |
| D4 元宝整件拒收 | C# `if (yuanbao>0) return false` | **回正**：`CreditRecycleYuanbao` 入队，丢弃结果 | `YanshenApi.cs:1851, 1895-1899` |
| D5 未知字段 throw | 整份配置作废 | **回正**：`default: break;` 忽略 | `PluginManager.cs:844` |
| D6 总开关子键缺失 throw | 快照 null | **回正**：`HasMasterSwitch` 保持 false，门失效 | `PluginManager.cs:790-796` |
| N1 经验单价跨件泄漏 | C# 未复刻 | **已照抄**：`totals.ExpUnitCarry` | `YanshenApi.cs:1796-1809, 1944-1954` |
| N2 返回 999 死代码 | — | **已照抄**：恒返回 1 | `YanshenApi.cs:1681, 1728` |
| I1 `RecycleBagModelResolved` | C# 自造 fail-closed 门 | **已删**，并留了"勿重新加回"的证据段（七个键在 `0x1006B020..0x1006CF80` 引用数 = 0） | `YanshenApi.cs:1692-1700` |
| M1 `全局循环函数` 无驱动 | 回收永不触发 | **已驱动**：`YanshenRecycleDriver.Tick` 挂在 `TPlayObject.Message.cs:346` | 节拍器 `0x1008C7C0`、周期 `+0x938`、下限 500 ms |

| 判定 | 数 |
|---|---:|
| FAITHFUL | **24** |
| PARTIAL | **1**（`Enabled("高级回收")` 这道原生入口没有的额外门仍在，`YanshenApi.cs:1706`；生产值 = 1 故零差异，作者已写明保留理由） |
| UNPROVEN-BLOCKED | **3**（`Ys_HuiShou` 的脚本注册点：`sub_1006CF10` 在 45 MB 转储里 0 个 rel32 调用者、0 个 dword 引用；`player VMT+0x268`；`0x6F8730` 元宝 / `0x6C87B4` 经验的精确契约） |
| 合计 | **28** |

**⇒ 回收子系统实际完成度 24/28 = 85.7%，是眼神里唯一达到主引擎水准的部分。**
代价也要写明：M1 一接通，生产 `recycle.json` 的 **313 条物品规则的删除闸门就真的开了**，包括 §5.2 那条"`V(10,10)` 技能书档默认是开的 ⇒ 24 本技能书按每本 100 金币回收"。这是原生真实行为，不是缺陷。

### 2.8 F5 协议 / 结构编解码（21）

`dotnet run --project AuditTools/Yanshen207ProtocolCheck` 的 21 个具名检查：**21 PASS / 0 FAIL**（本轮亲跑 `ddf7fd1a`）。

PASS 覆盖：`!!!!` 三种隧道（数字 / `^` / 中文）、五种给予格式、动态 `TClientItem` 线格式、13..15 号装备槽、
`SM_DEALREMOTEADDITEM` 原生头、`CM_DEALDELITEM` 整单撤销、`SM_STORAGE_OK`、物品生产包逐字节、
`SM_SAVEITEMLIST` 容量头、物品来源字段 55..108、英雄动态 `TClientEquip`、英雄能力头与空表、
`SM_SENDUSERSTATE` 16 动态槽、13..15 槽参与属性、命令 16 / 命令 22 语义、绑定传播、
**白猪 16 槽装备规则**（A8 裁决见下）。

**A8 裁决（2026-08-14，capstone 亲验）**：腰带/鞋子/宝石各只收一个 StdMode（27/28/7），扩展号拒收。
工具 `Program.cs:901-947` 与 `M2Share.CheckUserItems`（DURA-16/17）已对齐，不再要求 `{54,52,62,53,63,64}`。

原生证据（`flat_image.bin` @0x400000）：

- StdMode 派发表 `0x74C374`：27→TBelt / 28→TBoots / 7→TCharm；51-54/62-64→TBasePileItem/TAnimalMascot（VMT 无 +0x60 谓词）。
- 谓词体：`0x762D30 cmp dl,0xA`（腰带 slot10）、`0x7630CC cmp dl,0xB`（鞋子 slot11）、`0x763390 cmp dl,0xC`（宝石 slot12）。
- 眼神插件转储对以上 20 个 VA 做 dword 引用普查 = **0 命中**（插件不扩展装备资格）。

| 判定 | 数 |
|---|---:|
| FAITHFUL | 21 |
| MISSING | 0 |
| 合计 | 21 |

### 2.9 F6 `!!!!` 命令隧道（72）

`YanshenCommands.cs:10-14` 自述面：40 数字 ID（2.07 未用 6）、15 分隔符 + 2.08 新增 `^38^` = 16、7 中文名（11 条别名臂）、5 物品给予格式。本轮解析实测：`numeric=40 caret=16 chinese=11`，全部有派发臂，无悬空。

证据密度（`%TEMP%\ys_cmd_evidence.py`，判据 = 臂内或其 `YanshenApi` 成员附近有原生 VA）：

| 段 | 条目 | 有佐证 | 无佐证 |
|---|---:|---:|---:|
| 数字 ID | 40 | 21 | 19（`1 2 4 8 9 10 11 12 13 16 21 22 26 27 29 35 38 39 40`） |
| `^N^` | 16 | 10 | 6（`1 2 3 29 31 38`） |
| 中文名 | 11 | 4 | 7 |
| 给予格式 | 5 | — | — |

再叠加审计工具覆盖：`Yanshen207ProtocolCheck` 的 `command 16 direct MSG semantics`、
`command 22 ranged MSG semantics`、`five Give format behavior` 三条 PASS，把 ID 16 / 22 与 5 种给予格式补进有佐证一侧。

| 判定 | 数 |
|---|---:|
| FAITHFUL | **41** |
| FAIL-CLOSED | **1**（ID 25 → `SetLoopTimer`，同 §2.5） |
| UNPROVEN-IMPL | **30** |
| 合计 | **72** |

---

## 3. 合计表

| 面 | 条目 | FAITHFUL | PARTIAL | MISSING | FAIL-CLOSED | UNPROVEN |
|---|---:|---:|---:|---:|---:|---:|
| F1 配置键 → 宿主行为 | 380 | 172 | 32 | 167 | 0 | 9 |
| F2 脚本 API 名 | 125 | 39 | 0 | 17 | 1 | 68 |
| F3 S 变量坐标组 | 34 | 4 | 0 | 27 | 0 | 3 |
| F4 回收语义契约 | 28 | 24 | 1 | 0 | 0 | 3 |
| F5 协议 / 结构断言 | 21 | 21 | 0 | 0 | 0 | 0 |
| F6 `!!!!` 命令隧道 | 72 | 41 | 0 | 0 | 1 | 30 |
| **合计** | **660** | **300** | **33** | **212** | **2** | **113** |

校验：`300 + 33 + 212 + 2 + 113 = 660` ✓

- 严格有据完成度 `300 / 660 = 45.5%`
- 广义可交付度 `(300 + 2 + 113) / 660 = 415 / 660 = 62.9%`
- 真实可证行为缺口 `(212 + 33) / 660 = 245 / 660 = 37.1%`
- UNPROVEN 二分：`UNPROVEN-IMPL 98`（F2 68 + F6 30）/ `UNPROVEN-BLOCKED 15`（F1 9 + F3 3 + F4 3）

> **最保守口径**：若把 98 条 `UNPROVEN-IMPL` 全部当作"未证即未成"，广义可交付度降到 `(300+2+15)/660 = 48.0%`；
> 若反过来把它们全部当作"已实现即算数"，严格完成度升到 `(300+98)/660 = 60.3%`。
> **真值在 45.5% ~ 60.3% 之间**，收敛它的唯一办法是给那 98 条补原生 VA。

---

## 4. 17 个专用审计工具：PASS / FAIL 汇总

全部 `dotnet run`（基线 `ddf7fd1a`）。**14 PASS / 3 FAIL**。

| # | 工具 | 结果 | 关键输出 / 失败原因 |
|---|---|---|---|
| 1 | `Yanshen207ProtocolCheck` | **PASS** | 21 检查全过；A8 白猪 16 槽 StdMode 27/28/7 与 DURA-16/17 对齐 |
| 2 | `Yanshen208ApiSurfaceCheck` | PASS | `interpreter=no-player item=outlook+object+bind role=speed+npc-reject equipment=slot15 hero=live-weight save=slot15` |
| 3 | `YanshenApiAccessCheck` | PASS | `plugin=running+initys switches=missing+off+on calls=…+alias+player+main+wrapper init=rollback+concurrent` |
| 4 | `YanshenCdCompatCheck` | PASS | |
| 5 | `YanshenConfigRuntimeCheck` | PASS¹ | `keys=379 tabs=4/6/3/5 size=1016x680 encoding=GBK hotApply=yes malformedReload=preserved` |
| 6 | `YanshenDpiIsolationCheck` | PASS | 子窗口 `Unaware/96dpi` 与宿主 `SystemAware/192dpi` 隔离成立 |
| 7 | `YanshenHalfMoonCompatCheck` | **FAIL** | **工具自身编译不过**：`Program.cs(90,15) CS0103 nativeFallbackAboveCap` + `CS8422 静态本地函数不能引用 this` |
| 8 | `YanshenHeroCastCommand28Check` | PASS | `handler=magic-positive+clamp255 state=ordinal-player-key+one-shot-clear lifecycle=pending-through-hero-absence` |
| 9 | `YanshenItemConfigCheck` | PASS¹ | `sample=real GBK=roundtrip unknown=preserved save=atomic infiniteBag=items` |
| 10 | `YanshenMonsterAttrCheck` | **FAIL** | **harness 腐化**：`TBaseObject.cs:907 M2Share.ObjectManager.RegisterConstructed` 空引用——工具没初始化 `M2Share.ObjectManager`，与眼神实现无关 |
| 11 | `YanshenMsgTransportCheck` | PASS | `equipment slots 13..15 survive the original 12-byte MSG` |
| 12 | `YanshenMyJsonConfigCheck` | PASS | `kinds=6 routing=isolated GBK=strict lastValid=preserved deepClone=yes save=atomic threadSafe=yes` |
| 13 | `YanshenPaintDiagnostic` | PASS¹ | `classicButtonRedPixels=0 paintExceptions=0` |
| 14 | `YanshenRecycleConfigCheck` | PASS | `unknownField=ignored rootKeys=物品种类+回收类型 autoRecycle=1/-999 production=313items/2dangling nonMatch=neverDeleted` |
| 15 | `YanshenSunSwordCompatCheck` | PASS | `protocol=3002/10023->2/2819/10612->1230 formula=native-double-trunc+cap255 state=mp-twice+15s-cooldown+forgery-reject` |
| 16 | `YanshenTriggerDispatchCheck` | **FAIL** | 1 条契约不成立：`新倍攻和暴击 Wired 应为 False`。工具 `Program.cs:40` 的期望表把它写死为 `false`，而注册表（提交 `e9c55fe3`）已改为 `true`。**期望表与实现漂移** |
| 17 | `YanshenWarriorSkillCompatCheck` | PASS | `gbk=strings formula=enabled fallback=uninitialized+disabled+invalid-stab-b caps=thrusting+fire` |

¹ 这三个工具接受位置参数，**必须裸跑 `dotnet run --project <proj>`**；给 `dotnet run` 加 `-v q --nologo` 会把 `--nologo` 透传成 `args[0]`，工具会去打开一个叫 `--nologo` 的路径而假性失败。首轮我踩了这个坑，重跑后三个全绿。

**四个 FAIL 的性质**：

| 工具 | 性质 | 归类 |
|---|---|---|
| `Yanshen207ProtocolCheck` | 真实契约冲突，需裁决 | A（1 条） |
| `YanshenTriggerDispatchCheck` | 期望表未随实现更新 | A（1 条） |
| `YanshenHalfMoonCompatCheck` | 工具源码编译错误，**半月弯刀契约当前完全无人守护** | A（1 条，但风险不小） |
| `YanshenMonsterAttrCheck` | harness 缺 `M2Share.ObjectManager` 初始化 | A（1 条） |

⇒ **17 个工具里有 3 个已经不再守护它们本该守护的契约**（7 / 10 / 16）。这是审计链本身的回归，比任何单个功能缺口都更值得先修。

---

## 5. 缺口三分类

### A 类 —— 可证的小缺口，可外科补（28 条）

证据已完整到"照着写就行"的程度，每条都有原生挂载点、覆盖长度、语义。

| 组 | 条数 | 内容与原生锚点 |
|---|---:|---|
| A1 触发接线 | 12 | `英雄穿戴触发`(`0x75F08C→0x75F093`, `0x75EA31→0x75EA37`)、`新穿戴触发`(`0x75F085`, `0x75EA37`)、`上线触发 @initys`(`0x6548BD`)、`回城按钮触发`(`0x6DBB80`)、`心灵启示触发`(`0x6EDC2B`，**顶掉型**)、`复活触发脚本`(`0x73C484`)、`被击杀触发`(`0x766624`)、`捡物触发`(`0x6B770C`)、`攻击触发`(`0x76E35D`)、`魔法攻击触发`(`0x76DE84`)、`盘古魔法攻击触发`(`0x76E1AF`,`0x76DEC0`)、`英雄倍攻和暴击`(`0x76C816→0x76C81D`，本轮已亲验字节) |
| A2 刀刀切割回调 | 1 | `@Cutting` `0x767BAE → 0x767BB4`，槽 0x48，参数 0 |
| A3 带毒五键 | 5 | `半月带毒 0x7720FB` / `武器绿毒`+`物功带毒` **同址 `0x76E2BC`（必须互斥，不能做成两个毒源）** / `雷电带毒 0x76EB1D` / `法师群毒 0x76E1A9`；RNG 顺序已定：先 `Random(1)` 判 `[+0x18CC]&2` 上绿毒，再 `Random(5)` 判 `&4` 上红毒 |
| A4 野蛮麻痹 | 1 | `0x6BC9E2`，技能 27 冲撞成功后 `call [vmt+0xC8](edx=0x1A, ecx=3)` |
| A5 噬魂沼泽绿毒修复 | 1 | `0x691E2E`，无条件 `call 0x766060` |
| A6 S 变量播种 | 1 | `0x100CE4EA` 起 150 轮：`S(1,1..8)` 负值→`-1`、`S(1,9..150)` 负值→`0`、`S(1,49)`→`1314` |
| A7 激光两槽 | 2 | `S(1,81)` 写 `0x76FE44` 的 arg0 低 8 位（**不是线跨度**，跨度在更早的 `0x76E9FD 6A 08`）；`S(1,82)` 是 `TrainSkill` 的 `Random` 实参，开启且 `S≤0` 时落到 `Random(1)+1`，**不是还原 `Random(3)+1`** |
| A8 装备槽 StdMode 裁决 | 1 | §2.8：腰带 54/64、鞋子 52/62、宝石 53/63 —— 判定"白猪扩展"还是"DURA-16/17 的原生 fail-closed"为准 |
| A9 审计工具修复 | 4 | `YanshenHalfMoonCompatCheck` 编译错误；`YanshenTriggerDispatchCheck` 期望表；`YanshenMonsterAttrCheck` harness；`Yanshen207ProtocolCheck` 待 A8 裁决 |

### B 类 —— 大子系统 / 热点（主体是 213 MISSING + 98 UNPROVEN-IMPL）

| 组 | 规模 | 说明 |
|---|---|---|
| B1 两个整页无行为 | `盘古3` 34/39、`眼神2(第1页)` 29/34 | 前者含 `战士合击`/`法道合击`（`0x7D3298`/`0x7D341C` 浮点表）、`装备吸血 0x76E2A3`、`装备提升人物爆率 0x71FD37`（分母改成 `MaxPoint×倍率×A÷(B+[player+0x2A4])`，**缺 `+0x2A4` 累计爆率加成字段**）；后者含 `主号高级暴击`、`技能等级突破`、各类固定增伤 / 切割 |
| B2 高站点数补丁未接 | `屏蔽属性提升提示` 31 站点、`免毒符` 12 站点、`永久属性` 12 站点、`屏蔽元宝增减信息` 8 站点 | 载荷字节完整，缺的是 C# 落点映射 |
| B3 S 变量消费层 | 28 组坐标 | 永久属性 22 槽、切割族 9 槽、技能变址 `S(7/8/9, magicid)`、包装器路径 20 余个常量读点 |
| B4 脚本 API 证据债 | 68 UNPROVEN-IMPL + 17 未登记 | 施毒 / 麻痹 / 吸血 / 切割 / 自定义伤害五变体 / 推拉四式 / DB 三件套 / 全局变量四件套 |
| B5 命令隧道证据债 | 30 UNPROVEN-IMPL | 数字 19 条、`^N^` 6 条、中文 7 条 |
| B6 `穿人穿怪` | 1 键，需新建引擎设施 | 补丁字节 100% 完整（`0x768454 55 8B EC → B0 01 C3` 谓词恒真；`0x6B30A3` 10 字节强制缓存位 = 1），但 C# 既无 `[player+0x3FE]` 缓存位、也从不发 `SM_COMMON_INFORMATION(2821)` |
| B7 具名定时器 | 1 | `Ys_SetTimerByName` 需要一整套回调派发层，不是接一行 |

### C 类 —— 不可证 / 依赖运行期（15 UNPROVEN-BLOCKED + 底层障碍）

| # | 障碍 | 影响面 | 解法 |
|---|---|---|---|
| C1 | **Themida 逐函数虚拟化**：275 个入口 / 255 个函数跳进 `0x10400000..0x11400000`，该 16 MB 在本转储 `gap_zero_ratio = 1.0`（**100% 全零**） | `S(1,1)` 禁言模式 6/7/8 的 SetS detour `0x100CEB40`；`Ys_HuiShou` 与节拍器 `0x1008C7C0` 的派发者；`英雄野蛮` 的两个函数指针 | **重抓一份带 VM 段的转储** |
| C2 | **8 个 trampoline 模板未回收** | `S(1,12)/S(1,30)`（`0x100D120A` 永久属性首站，count=184）、`火墙设置时间上限`(`0x100B39B3`)、`中毒时间上限`×2、`装备提升人物爆率`（已由 §5 补齐）、`新穿戴触发`/`英雄穿戴触发`/`装备来源` | 补 `rep movsd` / `movaps` 回放对这几个站点的支持 |
| C3 | **没有未打补丁的 stock 基线**：磁盘 `M2Server.exe` 被 VMProtect 加壳（`CODE` 节 `SizeOfRawData = 0`），唯一可读的是活进程转储；插件"复位表"与转储在 96 个随机极品槽里 **24 个冲突且双向** | 一切 `orig`/`stock` 断言 | 抓一份"插件未加载"的 M2Server 转储 |
| C4 | **长载荷补丁未逐帧回放** | `随身仓库` 45 B、`获取玩家对象函数` 72 + 84 B、`设置玩家称号函数_支持80字符` 69 B、`盘古高级属性` 43 B、`施毒术` 31 B | 已有雏形 `_ysgui2/g10_diff.payload`，按帧补齐 |
| C5 | **宿主函数未定名** | `0x6C94A9`（`禁止发言不提示` 第三站）、`0x6CBA88`（`屏蔽排行榜`） | 各 10 分钟的活 |
| C6 | **契约细节未解完** | `player VMT+0x268`（删物下发）、`0x6F8730` 元宝 / `0x6C87B4` 经验的完整参数契约、`S(1,123)/S(1,124)` 的原生读点、带毒 `0x766060` 的 `ident 0x283C` 出网形态 | 逐项定点反演 |

---

## 6. 结论：眼神距"百分百"还差什么（按优先级）

**P0 · 先把审计链修好（4 条，A9 + A8）。**
17 个工具里 3 个已经不再守护契约（`YanshenHalfMoonCompatCheck` 根本编译不过，半月弯刀的钳位/回退契约当前**零守护**；`YanshenTriggerDispatchCheck` 的期望表已被实现甩开；`YanshenMonsterAttrCheck` 卡在 harness）。在这三个绿之前，任何后续改动都没有回归网。同时裁决 §2.8 的装备槽 StdMode 冲突——两边都拿了字节证据，必须有人拍板，否则 `Yanshen207ProtocolCheck` 永远红着。

**P1 · A 类 23 条外科接线（A1..A7）。**
12 个触发点 + `@Cutting` + 带毒五键 + 野蛮麻痹 + 噬魂绿毒 + `S(1,81/82)` + `S(1,1..150)` 播种 = `12+1+5+1+1+2+1 = 23`。
每条都有挂载 VA、覆盖长度、槽号、参数个数，`YanshenTriggerDispatch.Registry` 已经把 12 条触发的全部原生事实存成静态数据，接线就是"在宿主同位点调一次 `DispatchPlain`/`DispatchWithParams`"。
**两个陷阱写在前面**：①`心灵启示触发` 是**顶掉型**（不重放被覆盖字节），接错方向会让原生动作双跑；②`武器绿毒` 与 `物功带毒` 打**同一个** `0x76E2BC`，必须做成互斥的一套毒源，不能是两个。

**P2 · `S(1,1..150)` 播种是很多东西的前置。**
它单独看只是 A 类一条，但原生切割函数 `0x1007AF24` 先核对 `S(1,49)==1314` 才往下读 `bank+0x398..0x3B8`；不播种，B3 里一整族的切割/永久属性消费者接了也不会生效。**应当先于 B3 做。**

**P3 · 补 98 条 UNPROVEN-IMPL 的原生 VA。**
这是把完成度从"45.2% ~ 60.0% 区间"收敛成一个确定数字的唯一办法，也是整份报告里最大的不确定源。
优先级内部排序：先补战斗主干（`ys_shidu` / `ys_mymabi` / `ys_xixue` / `ys_cutting` / `ys_myjn_*` 五变体 / `ys_jitui|tuitui` 四式），因为它们直接决定伤害数值；DB 三件套与全局变量四件套可以后置。

**P4 · B1 两个整页（`盘古3` 34 键、`眼神2(第1页)` 29 键）。**
这是剩余 MISSING 的最大单块。`盘古3` 的性价比更高：`战士合击`/`法道合击` 的浮点系数表在 `0x7D3298`/`0x7D341C`，`装备吸血 0x76E2A3`、`装备提升人物爆率 0x71FD37` 的 trampoline 已在 `ys_gui_extreme §5` 完整解出（只差 `[player+0x2A4]` 这个字段的来源）。

**P5 · C 类：重抓一份带 VM 段的转储 + 一份"插件未加载"的 M2Server 转储。**
这两件事一次性解掉 C1 / C3，顺带给 C2 的 8 个模板和 96 个极品槽提供判据。在此之前，`S(1,1)` 禁言语义、`英雄野蛮`、`Ys_HuiShou` 注册点都**永久不可判**，C# 保持 fail-closed 是正确的，不要凑数。

**最后一句提醒（不是技术项，是风险项）**：`M1` 已经在 `186ef170` 上接通了。也就是说，当前 master 上**回收的删除闸门是开着的**——生产 `recycle.json` 的 313 条规则、11 个类型全部生效，其中 `V(10,10)` 技能书档因为不在运营脚本 `_HuiS` 的归零名单里，默认值 `-1 ≠ 关闭值 0` ⇒ **24 本技能书会被以每本 100 金币回收**。这是原生的真实行为（`ys_recycle_impl §5.2` 有逐字节论证），不是缺陷；但上线前必须让运营知道这一条。
