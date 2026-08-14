# 眼神2(第2页) + 扩展页 · gap/ys-yspage2ext 实现报告

- 日期：2026-08-14
- 分支：`gap/ys-yspage2ext`　基线：`ddf7fd1a`
- 复跑：`python tools/ys_page2ext_census.py`；`dotnet run --project AuditTools/YanshenPage2ExtCensusCheck`
- 底本：眼神 2.0.8 plain/delayed 转储；M2 `flat_image.bin` @0x400000

## 汇总

| 类别 | 数 | 说明 |
|---|---:|---|
| **DONE** | **1** | `火墙不吸血`（连带 `攻击吸血` 魔法吸血段，同 `sub_100795C0` 块） |
| **EQUIVALENT-BY-ABSENCE** | **30** | 原版零运行时读点，或仅 GUI 状态标签生产者 |
| **BLOCKED** | **6** | 语义/补丁有，宿主或插件入口不可证 |

已 IMPLEMENTED / SCRIPT_ONLY 不在本任务重做的 9 键：`全局循环函数`、`循环时间_值`、`攻击吸血`(脚本+本次引擎段)、`眼神特殊函数`、`自定义伤害_plus`、`高级回收`、`super攻击触发`、`毫秒级cd记录`、`大背包`。

---

## 逐键（37 真缺口键）

### DONE

| 键 | VA | C# |
|---|---|---|
| 火墙不吸血 | `0x1007AAEB` `cmp [cfg+0x1A4],0x1F4`；`0x1007AAF7` `cmp magicId,0x16` | `YanshenPage2ExtBehaviors.cs:ApplyMagicDamageVamp`；`TBaseObject.NativeMagicDamage.cs` 切割后调用 |

### EQUIVALENT-BY-ABSENCE（30）

| 键 | VA 证据 |
|---|---|
| 伤害触发脚本_plus | 仅 `0x10091EE0` gui_label |
| 新怪物爆率 | cfg+`0x93C`；`cmp …,0x1F4` 零命中 |
| 怪物爆率A/B/K_值 | cfg+`0x92C/0x930/0x934`；零命中 |
| 道士合击系数 + 数值1..5 | cfg+`0x850/0xC00..0xC60`；零命中（≠ 盘古3 战士/法道合击表） |
| 英雄魔法攻击触发 | 仅 `0x10092070` gui_label |
| 高级魔法攻击触发 | 仅 `0x10092020` gui_label |
| 多元伤害 | cfg+`0x114`；零命中 |
| 雷电术自定义伤害 + 系数A/B | cfg+`0x6AC/0x6B0/0x6B4`；零命中 |
| 扩展 17 键（星耀/护身/投保/格挡/召唤/修装等） | 各 1 处 `0x1009Exxx` gui_label，无 logic 臂 |

### BLOCKED（6）

| 键 | VA | 卡点 |
|---|---|---|
| 英雄野蛮 | `0x10067D92` | 启用后 `jmp 0x10BB915A`（Themida 远端区） |
| 英雄物理攻击触发 | `0x10068035` `@MyHeroAttack` | `sub_10067C90` rel32 调用者 0 |
| 高级物理攻击触发 | `0x10067F16` `@MyAttack` | 同上 |
| 千分比经验倍数 | `0x1006A99D` | `sub_1006A920` rel32 调用者 0 |
| 麻痹中不被麻痹a | `0x1009029D` | 含 GetS logic 但函数 0 静态引用 |
| 获取玩家对象函数 | 宿主 `0x646F40` `0x647D24` … | 72/84 B 长载荷未逐帧回放（矩阵 MISSING） |

---

## 反汇编复核

```
1007AAC9  cmp [cfg+0x124],0x1F4    ; 攻击吸血
1007AAEB  cmp [cfg+0x1A4],0x1F4    ; 火墙不吸血
1007AAF7  cmp [ebp+0x1C],0x16      ; magicId 22
1007AAFB  je  0x1007ABB8           ; 跳过 IncHealthSpell
10068035  cmp [cfg+0x52C],0x1F4    ; 英雄物理攻击触发（sub_10067C90，0 调用者）
10067D92  cmp [cfg+0x128],0x1F4    ; 英雄野蛮 → Themida
1006A99D  cmp [cfg+0x540],0x1F4    ; 千分比经验倍数（sub_1006A920，0 调用者）
```
