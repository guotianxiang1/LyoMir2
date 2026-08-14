# 眼神 gap/ys-pangu3 · 盘古3 页闭合报告

- 日期：2026-08-14
- 工作树：`D:\loym2\_gapwork\ys-pangu3`　分支：`gap/ys-pangu3`
- 底本：M2 `flat_image.bin` @0x400000；眼神 2.0.8 转储 @0x10000000
- 矩阵：`python tools/ys_gui_matrix.py`（本轮修复中继可达性播种）

## 收口

| 状态 | 数 |
|---|---:|
| DONE (IMPLEMENTED) | 33 |
| SCRIPT_ONLY | 1 (`施毒术`) |
| BLOCKED (LABEL_ONLY) | 5 |
| **合计** | **39** |

---

## DONE（33 + 1 脚本）

| 键 | 宿主 VA | C# file:行 | commit |
|---|---|---|---|
| `中毒时间上限` | `0x76E5CE` `0x76E675` | `YanshenPoisonTimeCap.cs:61` | `872ce375` |
| `中毒时间上限_秒` | （同上桩体立即数） | `YanshenPoisonTimeCap.cs:63` | `872ce375` |
| `修改召唤神兽` | `0x76EE98` `0x76EEAF` | `MagicManager.cs:2027` | （基线） |
| `人物等级1_值` | （同上 detour 阈值） | `YanshenApi.cs:5150` → `MagicManager.cs:2027` | （基线） |
| `人物等级2_值` | 同上 | `YanshenApi.cs:5151` | （基线） |
| `人物等级3_值` | 同上 | `YanshenApi.cs:5152` | （基线） |
| `怪物名字1_值` | `0x76EEAF` mov edx | `YanshenApi.cs:5150` | （基线） |
| `怪物名字2_值` | 同上 | `YanshenApi.cs:5151` | （基线） |
| `怪物名字3_值` | 同上 | `YanshenApi.cs:5152` | （基线） |
| `怪物数量1_值` | `0x76EE98` push | `YanshenApi.cs:5150` | （基线） |
| `怪物数量2_值` | 同上 | `YanshenApi.cs:5151` | （基线） |
| `怪物数量3_值` | 同上 | `YanshenApi.cs:5152` | （基线） |
| `战士合击` | `0x7D33FC..0x7D341C` | `YanshenComboTables.cs:102` | `e007ee77` |
| `战士合击_数值1..5` | 同上 5 槽 f64 | `YanshenComboTables.cs:102` / `YanshenApi.cs:5024` | `e007ee77` |
| `法道合击` | `0x7D3278..0x7D3298` | `YanshenComboTables.cs:103` | `e007ee77` |
| `法道合击_数值1..5` | 同上 5 槽 f64 | `YanshenComboTables.cs:103` / `YanshenApi.cs:5024` | `e007ee77` |
| `屏蔽排行榜` | `0x6CBA88` (CM 1060) | `YanshenHideRank.cs:38` / `TPlayObject.NativeQuestOrder.cs:20` | `3fbca823` |
| `脚本控制人物爆率` | `0x6DF2CC` | `YanshenScriptDropRate.cs:49` / `TPlayObject.Base.cs:333` | `872ce375` |
| `装备提升人物爆率` | `0x71FD37` | `YanshenEquipDropBoost.cs:105` / `UsrEngn.cs:2571` | `6bcdab25` |
| `装备提升人物爆率_A值` | 桩体 `+0x013` | `YanshenEquipDropBoost.cs:117` | `34565196` |
| `装备提升人物爆率_B值` | 桩体 `+0x023` | `YanshenEquipDropBoost.cs:117` | `34565196` |
| `人物爆率调整` / `最大装备数量` / `红名K值` / `非红名K值` | （各键既有落点） | `TBaseObject.Base.cs` 等 | （基线） |
| `施毒术` | 脚本 API | `PasApiBridge.Yanshen.cs` | SCRIPT_ONLY |

---

## BLOCKED（5）

| 键 | 宿主 VA | 原因 | C# 标注 |
|---|---|---|---|
| `施毒术_公式值` | `0x76E599` (31B 整段) | 原生改 `[vmt+0xCC]` 链的 nParam3；C# 走 `HUtil32.Round` 基座，非 1:1 | `YanshenApi.cs:4516` |
| `无极真气` | `0x74587C` | g11 仅 memcpy 提示串「，持续 N 秒」，不改数值 | `YanshenApi.cs:4393` |
| `无极真气_A值` | （无数值补丁点） | 同上 | `YanshenApi.cs:4397` |
| `无极真气_时间` | `0x745880` 文本 | 同上，消费者 `sub_7457D7` 只 SysMsg | `YanshenApi.cs:4398` |
| `装备吸血` | `0x76E2A3` | trampoline 完整但源字段 `[+0x184]` 来自未建模的 Recalc 聚合块 | `YanshenApi.cs:4511` |

---

## 本轮变更

1. `tools/ys_gui_matrix.py`：`accessor_consumers` 改为对 **全部** `YanshenApi` 成员从引擎/脚本文件播种（修复 `TryGetModifyShenShou` 中继漏报 10 键）。
2. `YanshenApi.cs`：5 条 BLOCKED 键附 VA 证据注释。
3. 矩阵再生成：`盘古3` 33 IMPLEMENTED / 1 SCRIPT_ONLY / 5 LABEL_ONLY。

详细反汇编与桩体字节级证据见 `docs/ys_b1_pangu3_20260814.md`。
