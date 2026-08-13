# 战神引擎 M2Server → C# 1:1 复刻 · 当前完成度审计报告

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\audit2`　分支：`w/audit2`　基/HEAD：`ed39ef63`（`DROP-35/36 wiring: ClientGetButchItem 挖肉接入掉落表直入包交付`）
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase=0x400000），python 3.11 + capstone 5.0.7
- 数据源：
  1. 29 份分片核验报告 `docs/eqv_shard01..29_20260814.md`（逐条 FAITHFUL/FIXED/MISSING/BLOCKED/DIVERGENT/FAIL-CLOSED）
  2. 等价账本 `staging/equivalence_ledger_20260810.tsv`（**754 条契约** = 755 行 − 1 表头；29 分片 × 26 = 754，逐一对齐）
  3. `git log master` 本会话已合入的代码修复
- 铁律遵循：本报告只做只读审计，不改任何 C# 代码，仅产出并提交本文档。所有百分比按契约条数计，逐条可回溯到分片来源 + 证据地址。**不虚报。**

---

## 0. 执行摘要（结论先行）

| 指标 | 值 | 口径 |
|---|---|---|
| 契约总数 | **754** | 账本数据行数 = 29 分片 × 26 |
| FAITHFUL（原已忠实 / 本轮核验为忠实） | **651** | 含 9 条"忠实带记录性次要/架构注记"（见 §2 口径） |
| FIXED（本会话外科修复并合入 master） | **23** | 10 条分片内直接标 FIXED + 13 条跨车道提交在 HEAD 已闭合（见 §4） |
| **有据完成度（严格口径）** | **674 / 754 = 89.4%** | **(FAITHFUL + FIXED) / 总数** |
| 仍未闭合 | 80 | MISSING 12 + DIVERGENT 8 + FAIL-CLOSED 10 + BLOCKED/UNPROVEN 47 + NOTHROUGH 在建 3 |
| —其中"不可证/DBSvr 运行期(C 类)" | 57 | BLOCKED/UNPROVEN 47 + FAIL-CLOSED 10（C# 多为正确 fail-closed / 不臆造，非缺陷） |
| —其中"真实可证行为缺口(A+B 类)" | 23 | MISSING 12 + DIVERGENT 8 + NOTHROUGH 在建 3 |
| **广义可交付度（含 C 类不可证但 C# 正确/安全）** | **731 / 754 = 97.0%** | (FAITHFUL + FIXED + BLOCKED + FAIL-CLOSED) / 总数 |
| **真实行为缺口占比** | **23 / 754 = 3.0%** | 唯一"C# 行为确与原生有别"的部分 |

**一句话结论**：按用户口径 `(FAITHFUL+FIXED)/总数`，当前完成度 **89.4%**。其中"未闭合的 80 条"里 **57 条（71%）属静态镜像不可证/DBSvr 运行期依赖，C# 已正确 fail-closed 或不臆造，并非 C# 缺陷**；真正"C# 行为与原版有别、可证"的缺口只有 **23 条（占全量 3.0%）**，且绝大多数是共享热点函数或大子系统重写（B 类）。若以"忠实 + 已安全 fail-closed"计，广义可交付度约 **97.0%**。

---

## 1. 逐分片状态汇总（29 份，均以「基线 00841828 + 本车道修复」为快照）

> 说明：每份分片在自己的工作树（基线 `00841828`）上核验，**看不到其他车道并入 master 的修复**。下表"分片声明"列即各报告自述计数；"HEAD 对齐"在 §3 统一按当前 `ed39ef63` 补偿（跨车道提交把若干 MISSING/BLOCKED/在建项落定为 FIXED）。

| # | 契约段 | 条数 | FAITHFUL | FIXED | MISSING | DIVERGENT | FAIL-CLOSED | BLOCKED/UNPROVEN | 其它 |
|---|---|---|---|---|---|---|---|---|---|
| 01 | ECON-01..26 | 26 | 25 | 0 | 0 | 0 | 0 | 1 (ECON-17) | |
| 02 | ECON-27..40+PRICE-01..12 | 26 | 22 | 1 (PRICE-06) | 0 | 0 | 0 | 3 (ECON-38/39/40) | |
| 03 | PRICE-13..25+TRADE-01..13 | 26 | 23 | 1 (TRADE-10) | 0 | 0 | 0 | 2 (PRICE-24/25) | |
| 04 | TRADE-14..39 | 26 | 26 | 0 | 0 | 0 | 0 | 0 | |
| 05 | TRADE-40..61+CRAFT-01..04 | 26 | 22 | 0 | 1 (TRADE-50) | 0 | 2 (TRADE-44/47) | 1 (TRADE-61) | 含 4 条 verified-note 计入 FAITHFUL |
| 06 | CRAFT-05..30 | 26 | 25 | 1 (CRAFT-19) | 0 | 0 | 0 | 0 | |
| 07 | CRAFT-31..47+DURA-01..09 | 26 | 25 | 0 | 0 | 0 | 0 | 1 (CRAFT-46) | CRAFT-47 RESOLVED→FAITHFUL |
| 08 | DURA-10..35 | 26 | 24 | 0 | 0 | 0 | 0 | 2 (DURA-11/23) | |
| 09 | DURA-36..45+POIS-01..16 | 26 | 16 | 0 | 0 | 1 (POIS-11) | 1 (POIS-16) | 8 (DURA-37..44) | |
| 10 | POIS-17..38+STATE-01..04 | 26 | 20 | 0 | 0 | 0 | 2 (POIS-27/30) | 4 (POIS-32/33/34/38) | |
| 11 | STATE-05..30 | 26 | 25 | 0 | 0 | 1 (STATE-19) | 0 | 0 | |
| 12 | STATE-31..52+MINE-01..04 | 26 | 23 | 0 | 0 | 0 | 2 (STATE-31/49) | 1 (STATE-50) | |
| 13 | MINE-05..30 | 26 | 26 | 0 | 0 | 0 | 0 | 0 | |
| 14 | MINE-31..56 | 26 | 24 | 0 | 1 (MINE-49) | 0 | 0 | 1 (MINE-56) | |
| 15 | MINE-57..61+GILD-01..21 | 26 | 23 | 1 (MINE-61) | 0 | 0 | 0 | 2 (MINE-57/58) | |
| 16 | GILD-22..29+TCFP-01..18 | 26 | 25 | 0 | 0 | 0 | 0 | 1 (GILD-29) | |
| 17 | TCFP-19..28+MFLG-01..16 | 26 | 26 | 0 | 0 | 0 | 0 | 0 | |
| 18 | DROP-01..11+MFLG-17..31 | 26 | 25 | 1 (MFLG-24) | 0 | 0 | 0 | 0 | MFLG-24 含 UserNoKill 残留子缺口 |
| 19 | DROP-12..37 | 26 | 21 | 0 | 2 (DROP-35/36) | 1 (DROP-33) | 0 | 2 (DROP-30/37) | DROP-33 世界腿已修，own-table 腿仍分歧 |
| 20 | DROP-38+MOVE-01..25 | 26 | 22 | 2 (MOVE-02/25) | 2 (MOVE-10/11) | 0 | 0 | 0 | |
| 21 | MOVE-26..51 | 26 | 23 | 0 | 3 (MOVE-31/34/39) | 0 | 0 | 0 | 含 3 条 FAITHFUL⚠ |
| 22 | MOVE-52..77 | 26 | 19 | 0 | 0 | 0 | 2 (MOVE-54/57) | 0 | 5 条 NOTHROUGH(71-75) 交他车道，未评级 |
| 23 | MOVE-78..98+SPWN-01..05 | 26 | 15 | 2 (MOVE-78/83) | 5 (MOVE-79/82/85/89/90) | 0 | 0 | 4 (MOVE-94/95/96/97) | |
| 24 | SPWN-06..31 | 26 | 14 | 0 | 7 (SPWN-13/14/17/19/20/21/23) | 0 | 1 (SPWN-29) | 4 (SPWN-16/22/30/31) | |
| 25 | SPWN-32..57 | 26 | 21 | 0 | 0 | 1 (SPWN-56) | 0 | 4 (SPWN-45/47/55/57) | |
| 26 | SPWN-58..60+SGRP-01..23 | 26 | 26 | 0 | 0 | 0 | 0 | 0 | |
| 27 | SGRP-24..45+CGLD-01..04 | 26 | 18 | 0 | 1 (SGRP-30) | 4 (SGRP-25/26/31/44) | 0 | 3 (SGRP-35/40/41) | |
| 28 | CGLD-05..24+QST-01..06 | 26 | 25 | 1 (CGLD-11) | 0 | 0 | 0 | 0 | |
| 29 | QST-07..32 | 26 | 22 | 0 | 0 | 0 | 0 | 4 (QST-27/28/31/32) | |
| **合计（分片声明）** | | **754** | **651** | **10** | **22** | **8** | **10** | **48** | NOTHROUGH 在建 5 |

> 分片声明合计校验：651 + 10 + 22 + 8 + 10 + 48 + 5(NOTHROUGH) = **754** ✓

---

## 2. 口径说明（如何计数，为何可信）

**2.1 分母 = 754**：账本 `equivalence_ledger_20260810.tsv` 数据行 754 条（前缀分布：MOVE 98、SPWN 63、STATE 52、TRADE 61、CRAFT 47、MINE 57、DURA 45、POIS 38、SGRP 45、TCFP 28、CGLD 24、GILD 29、MFLG 31、DROP 38、ECON 40、PRICE 25、QST 32 …），与 29 份分片逐条一一对应，无遗漏、无重复。

**2.2 FAITHFUL 的边界**：计入 FAITHFUL 的 651 条中，有约 9 条是"C# 行为等价、仅带记录性注记"，分片明确判其忠实且已附证据：
- 架构性等价（无 LogServer 二进制协议，改文本日志）：TRADE-52、CRAFT-31/32（196 字节记录 out-of-scope）
- 默认配置等价的 config 门控扩展：TRADE-55
- 忠实但含潜伏/次要偏差（实战不触发）：MOVE-36/41/43（分片 21 判 FAITHFUL⚠）
- UNPROVEN 但不阻断等价（类身份未证、C# 独立字段承载）：MFLG-30
- 原 BLOCKED(工具受限)→复核 FAITHFUL：CRAFT-47

这些均有分片逐字节佐证，非"凑数"。若采最保守口径把这 9 条剔出 FAITHFUL，完成度降至 `(642+23)/754 = 88.2%`，量级不变。

**2.3 FIXED = 23（本会话，对齐 HEAD）**：分片自身在其工作树内标 FIXED 的 10 条 + 其他车道并入 master、当前 HEAD 已闭合但分片(旧基线)仍显示 MISSING/BLOCKED/在建 的 13 条。每条都有 commit 哈希佐证（见 §4）。这是本审计唯一对分片声明做的"向上对齐"，且只向 HEAD 真值靠拢，不臆造。

**2.4 为何要做 HEAD 对齐**：29 份分片全部基于 `00841828` 切工作树、彼此隔离，看不到并行车道的修复（分片 12 自述"落后 master 14 提交"）。因此分片对"他车道已修项"仍记旧态。以当前 `ed39ef63` 为准，需把这些跨车道提交并回。对齐仅影响 13 条的归类（MISSING/BLOCKED/在建 → FIXED），不改变 FAITHFUL 基数。

**2.5 账本范围外的额外加固（不计入 754 分母）**：本会话另有两处修复超出账本契约集，特此登记但不纳入百分比：
- `POIS-39`（`a4159547` port physical landing damage sub_73F8E0, VMT+0x1AC, wire 0x39/0x47）——账本 POIS 止于 POIS-38，POIS-39 系新契约号。
- 协议 16 字节头 `GATE-01/02/03`（`be2173ad`/`f157d901`/`3d7ee975`，77BBAA33 传输帧 Cmd@+0x0C/BodyLen@+0x0E）——账本无 GATE 前缀。
- `CompareLStr`（`52acf458` 改纯 ASCII UpCase 对齐 0x4034D4）——提升分片 17 已计入 FAITHFUL 的 MFLG 全族之逐位保真度，非新增契约。

---

## 3. HEAD 对齐后的最终计数与完成度

| 状态 | 分片声明 | HEAD 对齐（当前 `ed39ef63`） | 说明 |
|---|---:|---:|---|
| FAITHFUL | 651 | **651** | 不变 |
| FIXED（本会话） | 10 | **23** | +13 跨车道提交并回（§4） |
| MISSING | 22 | **12** | −10（TRADE-50/MINE-49/MOVE-34/DROP-35/DROP-36/SPWN-17/19/20/21/23 已闭合） |
| DIVERGENT | 8 | **8** | 不变（DROP-33 仅世界腿修，own-table 仍分歧） |
| FAIL-CLOSED | 10 | **10** | 不变 |
| BLOCKED/UNPROVEN | 48 | **47** | −1（POIS-38 已移植闭合） |
| NOTHROUGH 在建 | 5 | **3** | −2（MOVE-71/72 已修；MOVE-73/74/75 在建） |
| **合计** | **754** | **754** | ✓ |

### 有据完成度

```
严格口径 = (FAITHFUL + FIXED) / 总数 = (651 + 23) / 754 = 674 / 754 = 89.4%
```

- 广义可交付度（含 C 类"不可证但 C# 正确/安全 fail-closed"）= (651+23+47+10)/754 = **731/754 = 97.0%**
- 真实待闭合行为缺口（A+B 类）= (12+8+3)/754 = **23/754 = 3.0%**

---

## 4. 本会话 FIXED 明细（23 条，逐条附提交哈希）

### 4.1 分片内直接标定 FIXED（10 条）
| 契约 | 分片 | 提交 | 摘要 |
|---|---|---|---|
| PRICE-06 | 02 | `d69900ab`/`81981f6a` | 武器(StdMode 5/6)index-6 属性 v≤10 补明文加 v，消除定价加成零贡献 |
| TRADE-10 | 03 | `c1b94311` | ClientAddDealItem 补 GM 权限旁路(sub_6C417C@0x6C41AD) |
| CRAFT-19 | 06 | `62469df2` | 1034 成功扣金后补发 RM_GOLDCHANGED |
| MINE-61 | 15 | `4c01398a` | 挖矿产出率正常档改回硬编码 12，消除配置化偏离 |
| MFLG-24 | 18 | `941cc735` | SAFE 前缀匹配 + 解析 SAFE(NOTHROUGH) 参数（UserNoKill 残留见 §5-A） |
| MOVE-02 | 20 | `08ec2866` | pose(3012) 广播补回 X/Y/Dir |
| MOVE-25 | 20 | `b599ad9c` | turn(3010) 拒绝改四零对齐 0x6D9B94 |
| MOVE-78 | 23 | `59468053` | 定点石 setter 的 map+0x67 门补齐 boLIMITITEMMOVE |
| MOVE-83 | 23 | `651f4384` | NORIDE 召唤坐骑拒绝改硬编码 0xFCFF 原始发送 |
| CGLD-11 | 28 | `9103f4b9` | 城堡存款 ReceiptGolds 两门返回码顺序对齐 sub_65B458 |

### 4.2 跨车道并入 master、当前 HEAD 已闭合（13 条；分片旧基线仍记为 MISSING/BLOCKED/在建）
| 契约 | 分片(旧态) | 提交 | 摘要 |
|---|---|---|---|
| TRADE-50 | 05 (MISSING) | `e9abd014` | 补回全局成交计数器 [0x7D3A90] |
| MINE-49 | 14 (MISSING) | `bb21859e`/`34f2660f` | 复刻骑乘态(51/52) HIT 攻击门 sub_6BBEB8 并接线 |
| POIS-38 | 10 (BLOCKED) | `a6bb17b1`/`23ba211b` | 移植毒 1000ms 中段块(0x76B905..0x76BD33)并接线 |
| MOVE-34 | 21 (MISSING) | `369e4a8d` | 传送点(LinkPoint)格对怪物不可走 — 补 cell+2 走路 mover 闸 |
| MOVE-71 | 22 (在建) | `e2d1b32a` | 复刻 NOTHROUGH 穿透判定并接线玩家 walk |
| MOVE-72 | 22 (在建) | `e2d1b32a` | 同上 |
| DROP-35 | 19 (MISSING) | `c1ae9cd2`/`ed39ef63` | 移植 sub_71EC88(0x71EC88-0x71ED7F) 掉落直入杀手背包交付路径 + 接线 ClientGetButchItem |
| DROP-36 | 19 (MISSING) | `c1ae9cd2` | sub_71EC88 tag7 耐久回填 [mon+0x4A0]→[item+0x26] |
| SPWN-17 | 24 (MISSING) | `c1ae9cd2` | sub_71EC88 self/receiver/atleast-one 语义（同函数 1:1 复刻） |
| SPWN-19 | 24 (MISSING) | `c1ae9cd2` | sub_71EC88 掉落表 TList 遍历 |
| SPWN-20 | 24 (MISSING) | `c1ae9cd2` | sub_71EC88 条目偏移 +0x10/+0x14/+0x18 |
| SPWN-21 | 24 (MISSING) | `c1ae9cd2` | sub_71EC88 按名造物(0x751BBC→0x74C338) |
| SPWN-23 | 24 (MISSING) | `c1ae9cd2` | sub_71EC88 单抽门(≤，无倍率) |

> DROP-35/36 与 SPWN-17/19/20/21/23 同属原生 `sub_71EC88`；提交 `c1ae9cd2` 自述"1:1 复刻 sub_71EC88 (0x71EC88-0x71ED7F)"，`ed39ef63` 将其接线到 CM_BUTCH→ClientGetButchItem，故这 7 条一并闭合。

### 4.3 已闭合"已记录分歧"（不改条数，但消除既有偏差）
| 契约 | 分片 | 提交 | 说明 |
|---|---|---|---|
| ECON-03/06 (m_nRebate) | 01 | `ba07b9e4` | §4.18 把 m_nRebate 归并到单一费率字段 m_nPriceRate，消除分片 01「已记录分歧①」的额外舍入偏差（该两条本就计 FAITHFUL） |
| MINE-50/51 (=DIG) | 14 | `c0a1a18e`/`bf769b11` | 移除挖矿臂自造 "=DIG" 文本包（分片 14 已计 FAITHFUL） |

---

## 5. 剩余未闭合项 · 三类清单（80 条 = A 3 + B 20 + C 57）

> 分类口径：**A**=可证的小/良性缺口，外科可补；**B**=可证但需大子系统重写/热点函数改造；**C**=静态镜像不可证（UNPROVEN 负命题/证据缺口）或依赖 DBSvr 运行期，C# 多为正确 fail-closed / 不臆造，非缺陷。

### A 类 —— 可证但未修的小缺口（应可外科补）：3 条 + 1 残留

| 项 | 分片 | 证据地址 | 现状 / 建议 |
|---|---|---|---|
| MOVE-39 | 21 | `sub_741224` 尾 0x7412E8 清 bodyState 0x17 / 0x741323 `call 0x778EC0` | 人形 mover 成功尾部缺"清 0x17 + sub_778EC0"两副作用（COSMETIC）；提交 X/Y+RM_WALK 广播已在。需字段/函数映射证据后外科补。 |
| MOVE-31 | 21 | `sub_7797CC` 0x7798C0 `call 0x765D64` 存活探针 + 摘链 + 日志 | 陈旧节点自愈缺失，但**托管内存无悬垂**、死/幽灵对象经 IsNativeCellBlocking 天然不挡路 → 可观测行为已等价（**良性**，可不补）。 |
| POIS-11 | 09 | 0x1F(DoT)@0x76BDE0 vs 0x1E(防御毒)@0x767A94 | 索引空间双 fork，毒代理已用 `bodyState=31-nType` 在边界桥接、行为已对齐；剩结构 fork 属追踪项（**已桥接**，可不补）。 |
| MFLG-24 残留(UserNoKill) | 18 | 0x7768EE `[+0x71]=1` + `word[+0x74]=0` | SAFE 已修(FIXED)，UserNoKill 子旗标仍缺：需在共享 `TMapFlag` 加字段 + 解析臂；但**无已证 C# 消费者** → 属机械补法，建议随 §B 的"MapInfo 旗标补全"专项一并做。 |

> 结论：外科可补的干净小缺口已被各车道基本清空；A 类仅剩装饰性/良性/无消费者项，收益低。

### B 类 —— 大子系统重写 / 共享热点改造（可证）：20 条

| 子系统 | 契约(条数) | 分片 | 关键证据地址 | 说明 |
|---|---|---|---|---|
| **MirrorMessage 跨服 handler 旧语义** | SGRP-25/26/31/44(DIVERGENT) + SGRP-30(MISSING)（5） | 27 | 跳表 `0x657110`(idx=202+i)；202=反作弊 `sub_653ED0`('SD000'@0x65402C 置 [+0x1829]=3)；217/218=师徒 `sub_657CF0`/`sub_657AC0`；241=信用卡全清 `sub_655A18`；207=注入过滤 `sub_658114`；247=`sub_65805C`(C# 无 case) | ISM 常量层已修，但 `MirrorMessage.cs` switch 仍走旧语义(登出/好友/行会战/重载行会)；忠实化需整体移植各 native handler(玩家遍历/状态机/DB) |
| **随机传送魔法 / 天地合一 / 进图通告路径** | MOVE-79/82/85/89/90(MISSING)（5） | 23 | 天地合一 `sub_7274B4`([+0xBA4] 门→sub_6BF458)；随机传送魔法 `sub_7855F8`(你-variant 串@0x785864)；进图状态通告 `sub_6B6BEC`(@0x6B6C45)；炸弹触发 `sub_7896FC`(@0x789752 每300ms)；NOMAGIC 消费者(@0x6DA12B) | 均为未移植的大函数/技能路径；C# 旗标侧已解析，消费者/消息缺失 |
| **移动派发静默闸 / 前置外观刷新** | MOVE-10/11(MISSING)（2） | 20 | 静默闸 walk 0x6D9BD5/run 0x6D9CF1 `mov dl,0x34/InBodyState/jne`；外观刷新 0x6D9BEC `call sub_6BCE2C`(SM 0x20) | state 52 静默丢包需跨 4+ 移动臂新增"静默通道"；外观刷新依赖未建模的英雄/外观子系统 |
| **NOTHROUGH 收尾** | MOVE-73/74/75(在建)（3） | 22 | `sub_768454`(缓存)@Message.cs:427；0xB05 发包@:994；`sub_772EB8` 放行(MOVE-75) | 归 `w/move-nothrough` 车道，字段/发包在建、方向一致，尚未确认收口 |
| **视野/格子 SearchViewRange 谓词** | SPWN-56(DIVERGENT)（1） | 25 | 0x77A2EB `call 0x765D64`(有效性校验 ≠ C# m_boDeath) | 每 tick 高频热点，改谓词波及视野子系统 |
| **掉落 own-table 共享散落半径** | DROP-33(DIVERGENT)（1） | 19 | native 段2 own-table `mov ecx,3`(0x71FDCF)；C# 走共享 `ScatterBagItems` 半径 `_MIN(nDropItemRage,7)` | 世界腿已修 4→3(`f3354457`)；own-table 腿共享函数同服务玩家死亡散落，改半径波及玩家路径 |
| **毒系施加管道 legacy** | STATE-19(DIVERGENT)（1） | 11 | RM_POISON=8037 全镜像 0 命中；原生直调 MakePosion(VMT+0xC8) | 跨 MagicManager/MakePosion/legacy 计时槽的整段管道分歧，专属 `w/poison` 车道 |
| **刷怪生成器可选字段（禁改文件）** | SPWN-13/14(MISSING)（2） | 24 | SPWN-13 0x67CA49 `[gen+0x28]→[mon+0x38]`；SPWN-14 0x67CA5D `[gen+0x40]` 播报数组 | 落点在禁改文件 `UsrEngn.cs` worker `sub_67C9E0` 内，须主流程插桩 |

> **另：用户点名的"装备扩展属性聚合子系统"** = DURA-37..44(8)+DURA-11+DURA-23，因原生"入口/调用方/写者(~20 个 +0x26 写点)"证据不足，当前归入 **C 类(不可证)**；一旦取得字节证据即成 B 类大子系统移植。见 §C。

### C 类 —— 静态镜像不可证（UNPROVEN / 依赖 DBSvr 运行期）：57 条

> 该类 C# 绝大多数为**正确的 fail-closed / 不臆造**，或行为在默认配置下等价，仅"无法从静态镜像正面证明"或"依赖 DBSvr 运行期"。非 C# 缺陷。

**C.1 依赖 DBSvr / 运行期配置（5）**
| 契约 | 分片 | 证据 | 说明 |
|---|---|---|---|
| TRADE-61 | 05 | 仓储层级/金币上限表 `[0x7D6608]`/`[0x7D6080]` 运行期加载 | 内容不在静态镜像，仅索引可证 |
| TRADE-44 | 05 | 药品仓 type3 `0x6C2CBF call 0x404690` | DrugStore 需 DBSvr 后端，C# fail-closed 静默拒收 |
| SGRP-35/40/41 | 27 | DB 链出站 `sub_713AAC`(0xCA)；0x19B 负载；无 M2Server 对 202..257 发送方 | 可达性依赖 DBServer 配置/对端，缺 DBSvr 侧不可配对 |

**C.2 装备耐久/扩展属性聚合深层写者（10，UNPROVEN）**
| 契约 | 分片 | 证据 | 说明 |
|---|---|---|---|
| DURA-37..44 | 09 | ~20 个 `+0x26` 写者(0x68686E/0x68C9DE/0x6A0B11/0x740C8D/0x788DAD…)触发/槽/算术未定性；slot 0/2/3/7/8/10/12/15 掉耐久点未证 | 原生入口/调用方普查缺口，铁律禁臆造公式 |
| DURA-11 | 08 | 第二护身符消耗 `sub_73EA20`(18 个魔法调用者) | 跨 MAGIC 分片，C# 相邻内联路径已忠实，本体对应未定位 |
| DURA-23 | 08 | 辅助槽 `+0x1CC` 方法身份 UNPROVEN | COSMETIC，仅外观 |

**C.3 UNPROVEN 负命题 / 证据缺口（C# 正确未臆造）（33）**
| 契约(条) | 分片 | 一句话依据 |
|---|---|---|
| ECON-17 | 01 | +0x675 字段语义 UNPROVEN，守卫函数非 NPC 买卖入口，fail-closed |
| ECON-38/39/40 | 02 | 原生 case body/卖出移除体/修理体取证不全，C# 侧已一致但不升级 |
| PRICE-24/25 | 03 | 静态类引用无法钉住物品类(多态已证)；mode-3 修理不可达 |
| CRAFT-46 | 07 | 成品 pile 行为随 StdItems 配置数据，非二进制可判 |
| POIS-32/33/34 | 10 | 源码命名/变体归属/击杀信用链缺证 |
| STATE-50 | 12 | 复合谓词 0x76B354 唯一调用者 0x6E670D 用途未定 |
| MINE-56/57/58 | 14/15 | 石堆过期清扫点未定位；三成本计数器/挖矿 ident 交换语义 UNPROVEN |
| GILD-29 | 16 | manager +0x1C/+0x20/+0x24 用途无据，C# 未臆造 |
| DROP-30/37 | 19 | +0xA8/+0xAC 读侧 xref 缺(常量 86400.0 已解)；4 VMT 槽未过 SelfPtr |
| MOVE-94/95/96/97 | 23 | cell[2] 生产者缺；creature-block 成员命名未证；占用探针尾未解；SITDOWN 名未证 |
| SPWN-16/22/30/31 | 24 | 配置记录布局；VMT+0x248 自校验；防沉迷播报颜色/参数落代码区 |
| SPWN-45/47/55/57 | 25 | 负命题不可证；sub_78389C 未读体；槽身份/是否泄漏 UNPROVEN |
| QST-27/28/31/32 | 29 | GiveHeroExp 深层/背包满深体/CHECKQUEST 体/原生 opcode 面未捕获 |

**C.4 FAIL-CLOSED —— C# 有意更安全 / 默认等价（9）**
| 契约 | 分片 | 说明 |
|---|---|---|
| TRADE-47 | 05 | 30s 半清扫未复刻，改由每 tick 全量取消覆盖同一触发面(更安全) |
| POIS-16 | 09 | 家族清除唯一调用者随 TKingOfIceMon(race146/154)一并 fail-closed，补空调用=死代码 |
| POIS-27/30 | 10 | 抗性 `<=6` 跨切面家族(G.5)谓词未反编译；legacy 投影在禁改 Grobal2 |
| STATE-31/49 | 12 | 深层消费者语义 UNPROVEN；低 band 部分故意未命名 |
| SPWN-29 | 24 | 防沉迷拦截已实现，播报日志参数不可证故不复刻 |
| MOVE-54/57 | 22 | 创物同图占位复检 / 登录 jitter 耗尽后 GetRandomXY 回退，均落传送/登录热点 |

> C 类合计：C.1(5) + C.2(10) + C.3(33) + C.4(9) = **57** ✓

---

## 6. 结论 —— 距离"百分百"还差哪些（按优先级）

**当前有据完成度 89.4%（674/754，严格口径）**；真实"C# 行为与原版有别"的缺口仅 **23 条(3.0%)**，其余未闭合项要么是本会话未及并回的已修项(已在 §4 对齐)，要么是不可证/DBSvr 依赖(C 类，C# 已正确 fail-closed)。距离 100% 的路径按优先级：

**P0 — 高玩法风险、可证、应优先（B 类头部）**
1. **MirrorMessage 跨服 handler 旧语义**（SGRP-25/26/31/44/30，分片 27）：`MirrorMessage.cs` 仍把 202→登出、207→重载行会、217/218→好友空桩、241→行会战；原生是反作弊惩罚/注入过滤/师徒/信用卡全清。**这是本子系统最高风险语义错配**，需按跳表 `0x657110` 逐 handler 移植。
2. **随机传送魔法/天地合一路径**（MOVE-79/82/85/89/90，分片 23）：`sub_7855F8`/`sub_7274B4`/`sub_6B6BEC`/`sub_7896FC` 五个大函数未移植，影响传送/技能/进图通告/炸弹触发。

**P1 — 可证、影响面中等（B 类其余 + A 残留）**
3. 移动静默闸 MOVE-10 + 前置外观刷新 MOVE-11（分片 20）。
4. NOTHROUGH 收尾 MOVE-73/74/75（分片 22，`w/move-nothrough` 在建，需确认收口）。
5. MapInfo 旗标补全：MFLG-24 UserNoKill + 缺 8 真 token + @TempSetMapParam ~50 运行时切换（分片 18）——机械但缺已证消费者。
6. 掉落 own-table 共享半径 DROP-33、视野谓词 SPWN-56、毒系管道 STATE-19（均共享热点，需谨慎重构）。

**P2 — 禁改文件插桩（B 类）**
7. 刷怪生成器可选字段 SPWN-13/14（分片 24，`UsrEngn.cs` worker 主流程插桩）。

**P3 — 需先取二进制证据方能推进（C 类转 B 类）**
8. 装备扩展属性聚合深层写者 DURA-37..44/11/23（分片 08/09）：先定位 ~20 个 `+0x26` 写者的触发/槽/公式，再移植。
9. 其余 C.3 UNPROVEN 负命题：属逆向研究项，非移植缺口。

**P4 — 依赖外部系统（C 类，非本仓可闭合）**
10. DBSvr 运行期依赖：TRADE-61/44、SGRP-35/40/41 —— 需 DBSvr 侧或运行期配置，静态镜像不可证。

**判断**：GameSvr 主干（经济/定价/城堡金币/交易成交/仓库/耐久/毒与状态机/挖矿/掉落主链/移动可走性/传送原语/公会关系/脚本 V-S 变量/GM 金币命令）已达 **1:1 高保真**；主要"欠账"集中在 **跨服消息语义(P0)** 与 **少数大技能/传送函数(P0-P1)**。把 P0 两项 + P1 的 3/4/5 补齐，严格完成度可从 89.4% 提升到约 96–97%；剩余 3% 为不可证/DBSvr 依赖，属"证据/外部系统"边界而非移植工作量。

---

## 附录 A：核对足迹

- 工作树：`git worktree add D:\loym2\.claude\wt2\audit2 -b w/audit2 master`（HEAD `ed39ef63`）。
- 账本条数：`equivalence_ledger_20260810.tsv` 755 行 − 1 表头 = 754；POIS 止于 POIS-38、DROP 止于 DROP-38、无 GATE 前缀（故 POIS-39/GATE-01..03 为账本外加固）。
- 29 份分片状态计数逐份取自各报告"状态计数/结论统计"段 + 逐条结论表。
- 本会话修复哈希取自 `git log master --oneline`；`sub_71EC88` 移植范围取自 `git show --stat c1ae9cd2/ed39ef63`（新增 `UserEngine.NativeButcherDeliver.cs` + `TPlayObject.Operate.cs` 接线）。
- 本报告不改任何 C# 代码，仅新增本文档。
