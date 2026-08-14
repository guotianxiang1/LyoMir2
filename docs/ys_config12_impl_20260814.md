# 配置1 / 配置2 页 · 逐键闭合报告

- 日期：2026-08-14
- 分支：`gap/ys-config`
- 纪律：仅列有 capstone 字节佐证且已接 C# 消费者的键为 DONE；其余 BLOCKED。

## 汇总

| 页 | 键数 | DONE | BLOCKED |
|---|---:|---:|---:|
| 配置1 | 33 | 0 | 33 |
| 配置2 | 31 | 4 | 27 |
| **合计** | **64** | **4** | **60** |

另：配置2 中 **升级技能不提示** 在基线已 DONE（`TrainingMagicCommand.cs:77`），不计入本轮新增。

---

## 配置1（33）

| 状态 | 键 | VA / 依据 | C# |
|---|---|---|---|
| BLOCKED | 全屏拾取 | SCRIPT_ONLY 0x6B795C 等 | — |
| BLOCKED | 刀刀切割 | 基线 IMPLEMENTED | — |
| BLOCKED | 永久属性 | 12×memcpy 0x73D9CF..0x73DA3A，S(1,13..23) 消费者未接 | — |
| BLOCKED | 特殊属性 | 0x6E41BD / 0x73D951 | — |
| BLOCKED | 复活触发脚本 | 基线 IMPLEMENTED | — |
| BLOCKED | 被击杀触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 移动速度 | 0x73D983 tramp 未逐帧还原 | — |
| BLOCKED | 攻击反伤 | 0x767BB4 | — |
| BLOCKED | 捡物触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 复活戒指改cd | 0x73C47A 等 3 站 | — |
| BLOCKED | 攻击触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 魔法攻击触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 新穿戴触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 复活戒指概率 | 0x74373A | — |
| BLOCKED | 禁止装备自动绑定 | 0x784351 | — |
| BLOCKED | 新倍攻和暴击 | 基线 IMPLEMENTED | — |
| BLOCKED | give极品 | 0x6C89AE 长载荷 | — |
| BLOCKED | 麻痹概率 | SCRIPT_ONLY 0x76E2D2 | — |
| BLOCKED | AddLimLF函数修改 | 0x6DE8E3 | — |
| BLOCKED | IncActivePoint函数修改 | 0x6F91BA | — |
| BLOCKED | 英雄穿戴触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 英雄攻速移速 | 0x73DA43 | — |
| BLOCKED | BB杀怪触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 临时大背包 | 无补丁 | — |
| BLOCKED | 英雄倍攻和暴击 | 基线 IMPLEMENTED | — |
| BLOCKED | BB死亡触发 | 基线 IMPLEMENTED | — |
| BLOCKED | 特殊宝宝 | SCRIPT_ONLY | — |
| BLOCKED | 英雄施法速度 | 0x68DD60 | — |
| BLOCKED | 读取英雄装备 | SCRIPT_ONLY | — |
| BLOCKED | 装备来源 | SCRIPT_ONLY | — |
| BLOCKED | 千分比免伤 | 无补丁 | — |
| BLOCKED | 永久攻速 | 0x73D9A0 | — |
| BLOCKED | 上线触发 | 基线 IMPLEMENTED | — |

---

## 配置2（31）

| 状态 | 键 | VA / 依据 | C# |
|---|---|---|---|
| BLOCKED | 地狱雷光系数/范围/可换主属性 等 14 键 | 基线 IMPLEMENTED（YanshenSkillPatches） | — |
| **DONE** | **免毒符** | 12×memcpy 0x6ED945..0x6EDE1D | `Magic.cs:109` `NativeAmuletConsume.cs:96` `MagicManager.cs:355` |
| BLOCKED | 激光命中概率 | 0x76EA14，S(1,82) 已接 laser 但与键无关 | — |
| BLOCKED | 嗜血术倍数 / 野蛮等级 | 基线 IMPLEMENTED | — |
| **DONE** | **禁止发言不提示** | 0x6BB5CD / 0x6BB625 / 0x6C94A9 | `TPlayObject.Chat.cs:145,156,318` |
| BLOCKED | 中毒飘血 | tramp/memcpy 0x767E10，+[0x1BB] 门未还原 | — |
| **DONE** | **删除技能不提示** | 0x6C7797 jmp apply 0x100DB4A4 | `DelUserSkillCommand.cs:41` |
| BLOCKED | 升级技能不提示 | 0x73F5EE（基线 DONE） | `TrainingMagicCommand.cs:77` |
| BLOCKED | 群毒 / 群毒值 | 无宿主补丁，仅配置字段 | — |
| BLOCKED | 绿毒_A/B/最低、红毒_A/B、双毒时间_最低 | 群毒参数，无独立消费者 | — |
| BLOCKED | 魔法盾修正 | 无补丁站点 | — |
| BLOCKED | 冰咆哮/火球/雷电… | 基线 IMPLEMENTED | — |

---

## 本轮新增 DONE 明细

1. **免毒符** — `PatchToggleOn` + CheckAmulet/UseAmulet/NativeConsumeBujukCharm 免消耗 + 施毒术无符绿毒臂。
2. **禁止发言不提示** — 三处 SysMsg 门控。
3. **删除技能不提示** — DelUserSkill + DeleteSelfMagic，成功提示门控。
4. **DelSelfSkill** — sub_73F690 等价（无 SysMsg，补注册表缺口）。
