# gap/ys-provable · 眼神可证项清扫报告

- 日期：2026-08-14
- 分支：`gap/ys-provable`（基线 `ef674c3a`）
- 矩阵复跑：`python tools/ys_gui_matrix.py --repo <worktree> --out %TEMP%\m2`

## 汇总

| 指标 | 清扫前 | 清扫后 |
|---|---:|---:|
| LABEL_ONLY | 105 | **25** |
| IMPLEMENTED | 257 | **337** |

| 类别 | 数 | 说明 |
|---|---:|---|
| **新接线（PARAM）** | **2** | `神兽_数量` / `召唤骷髅_数量` → `YanshenPangu1Patches` + `MagicManager` |
| **EQUIVALENT_BY_ABSENCE** | **53** | `YanshenProvableRegistry.cs` — 零第四路径消费者 |
| **PLUGIN_SIDE_ONLY 闭合** | **26** | 同上 — 仅插件侧消费 |
| **BLOCKED（仍 LABEL_ONLY）** | **25** | 22×NATIVE_GAP tramp1/tramp2/长载荷 + 3×PARAM 父键 BLOCKED |

---

## 新接线（2）

| 键 | 宿主 VA | C# |
|---|---|---|
| `神兽_数量` | `0x76EE99` imm8 | `YanshenPangu1Patches.ShenShouSlaveCount` ← `MagicManager` |
| `召唤骷髅_数量` | `0x76EE1F` imm8 | `YanshenPangu1Patches.KuLouSlaveCount` ← `MagicManager` |

---

## BLOCKED 清单（25，仍 LABEL_ONLY）

### tramp1 / 桩体未回放（20）

| 键 | 卡点 VA | 分类 |
|---|---|---|
| `AddLimLF函数修改` | `0x6DE8E3` | tramp1 |
| `IncActivePoint函数修改` | `0x6F91BA` | tramp1 |
| `give极品` | `0x6C89AE` | tramp1 |
| `中毒飘血` | `0x767E10` (+`[ebx+0x1BB]` 门) | tramp1 |
| `复活戒指改cd` | `0x73C47A` `0x73C4F2` `0x743751` | tramp1 |
| `复活戒指概率` | `0x74373A` | tramp1 |
| `攻击反伤` | `0x767BB4` | tramp1 |
| `攻沙脚本控制` | `0x65C6B6` `0x65C76D` `0x65C785` | tramp1 + 宿主 UserCastle 未命名 |
| `永久属性` | `0x73D9CF..0x73DA3A` (12×) | tramp1 + 模板 `0x100D120A` |
| `永久攻速` | `0x73D9A0` | tramp1 |
| `特殊属性` | `0x6E41BD` `0x73D951` | tramp1 |
| `禁止装备自动绑定` | `0x784351` | tramp1 |
| `移动速度` | `0x73D983` | tramp1 |
| `英雄攻速移速` | `0x73DA43` | tramp1 |
| `英雄施法速度` | `0x68DD60` | tramp1 |
| `装备吸血` | `0x76E2A3` | tramp1 |

### tramp2 / 长载荷 / Themida（5）

| 键 | 卡点 | 分类 |
|---|---|---|
| `脚本控制头发外显` | `0x740F85` | tramp2 |
| `邮件防刷` | `0x6E7810` | tramp2 |
| `随身仓库` | `0x6E087C` 等 | 长载荷 45B |
| `盘古高级属性` | `0x6F9AB0` 等 | 长载荷 43B |
| `无极真气` | `0x74587C` | 长载荷 / 文案域 |
| `获取玩家对象函数` | `0x646F40` `0x647D24` | 长载荷 72+84B |

### PARAM 父键 BLOCKED（3）

| 键 | 父键 | 原因 |
|---|---|---|
| `施毒术_公式值` | `施毒术` | 宿主 `0x76E599` 31B 整段替换未回放 |
| `无极真气_A值` | `无极真气` | 同上 |
| `无极真气_时间` | `无极真气` | 同上 |

---

## F3 S 变量（27 缺口 — 本轮无新接线）

仍 MISSING / BLOCKED，原因未变（见 `docs/ys_svars_three_path_census_20260813.md` §4–6）：

| 子类 | 数 | 卡点 |
|---|---:|---|
| 刀刀切割族 `S(1,9..11,50..53,62,63)` + 门 `S(1,65)` | 9 | 伪随机 `[atk+0x18]/[+0x470]` C# 无模型 |
| 永久属性 `S(1,13..23,31..41)` | 22 | 模板 `0x100D120A` 未回收；`S(1,12/30)` Themida 模板首站 |
| 技能变址 `S(7/8/9,magicid)` | 若干 | 需 Recalc / 伤害链逐 skill 落点（部分经 `YanshenSkillPatches` 固定 index 已覆盖已知 magicId） |
| `S(1,1)` 禁言 6/7/8 | 1 | SetS detour `0x100CEB47` → Themida 零页 |
| `S(1,123/124)` | 2 | 三条原生路径无读点 |

**F3 静态可证接线已归零**（相对路径 1/2 已命名且非 VM 的坐标）；余下 27 条全部锁在 tramp 模板 / Themida / 伪随机模型 / 父键长载荷。

---

## 文件

- `GameSvr/Plugins/YanshenProvableRegistry.cs` — 53 EQUIV + 26 PLUGIN_SIDE
- `GameSvr/Plugins/YanshenPangu1Patches.cs` — 2 PARAM 接线
- `tools/_gen_provable_registry.py` — 可复跑生成器
