# F2 脚本 API 面 · wave-2 验证报告（非推拉四式 / 非 wave-1 战斗主干）

- 日期：2026-08-14
- 工作树：`D:\loym2\_gapwork\ys-f2rest`　分支：`gap/ys-f2rest`　基线：`ddf7fd1a`
- 底本：眼神 2.0.8 转储 `yanshen2_0_8_dll.memory.bin` @ `0x10000000`；M2 `flat_image.bin` @ `0x400000`
- 工具：`tools/ys_f2rest_verify.py`（handler 门控/元数扫描）、`tools/ys_b4_api_census.py --verify`
- 纪律：wave-1 已处理项（施毒/麻痹/吸血/切割/自定义伤害五变体/推拉四式）**本轮不重做**

---

## 0. 一句话

本轮对 **52** 条剩余 UNPROVEN-IMPL 逐条核对跳表 handler VA；**45 CONFIRMED**、**4 DIVERGENT 已修**、**7 STILL-UNPROVEN**；MISSING **17** 条 **0 补登记**（16 EQUIVALENT-BY-ABSENCE + 1 BLOCKED）。

---

## 1. 裁决口径

| 裁决 | 含义 |
|---|---|
| CONFIRMED | handler/臂 VA 在转储坐实；元数门或 cfg 门与 AllFuc 一致；C# 门控/返回值契约对齐 |
| DIVERGENT→修正 | 原生字节与 C# 不一致，本轮已改 |
| STILL-UNPROVEN | 三份底本 + AllFuc 均无独立隧道/注册点，不臆造 |
| MISSING·BLOCKED | 原生不可安全登记（`NPC_CreatMons`） |
| MISSING·EQUIVALENT | 未登记才是 1:1（AllFuc 薄包装 / 纯 Pascal / 宿主 API） |

---

## 2. wave-1 跳过（14）

`ys_shidu` `ys_shidu_effect` `ys_mymabi` `ys_xixue` `ys_cutting` `ys_myjn_plus2` `ys_myjn_super` `ys_myjn_undead` `ys_myjn_delay` `ys_myjn_effect` `ys_jitui` `ys_jitui2` `ys_tuitui` `ys_tuitui2`

---

## 3. DIVERGENT 修正（4）

| API | 原生证据 | 旧 C# | 修正 |
|---|---|---|---|
| `Ys_Attact` / `DirectAttack` | 中文 `!!!!定义伤害` 门 cfg2+**0x510** → 键「**自定义伤害**」（`0x1005EDA3`；内联 `0x1005EDDC`） | `Enabled("刀刀切割")` | → `Enabled("自定义伤害")` |
| `Ys_AddHp` / `Ys_AddMp` | 数字 11 handler `0x10071920`；臂 `0x10076D4E` 共用门 cfg2+0x11C | 无门 | → `TunnelGate()` |
| `Ys_GiveExp` | 数字 29 handler `0x10075090` | 无门 | → `TunnelGate()` |
| `Ys_MySkillExp` / `SetSkillExp` | 数字 10 handler `0x10071710` | 无门 | → `TunnelGate()` |

---

## 4. CONFIRMED 表（45）

handler VA 均来自 `docs/ys_b4_api_census.tsv` + 本轮 capstone 复核。

### 4.1 DB 三件套 + 全局变量四件套

| API | 隧道 | handler / 内联 VA | 门 | C# |
|---|---|---|---|---|
| `ys_SqlDbInsert` | caret ^1^ | `0x10058ED0`；段数 `<3` jb（`0x10058F20`） | caret 臂经 `高级回收` 等 caret 门（非 cfg2+0x11C） | `SqlDbInsert` + `Enabled("眼神特殊函数")` ✓ |
| `ys_SqlDbSelect` | libmysql | 选择器 `0x10087DC0` 比 `"libmysql"`@`0x102C0324`（`0x10087DD8`） | 同 GetSignInActPrizer 支 | `SqlDbSelect` ✓ |
| `ys_SendDBMsg` | caret ^3^ | `0x10059160`；段数 `<2` jb（`0x100591B3`） | caret 门 | `SendDbMsg` ✓ |
| `ysgetg` `yssetg` `ysgetstr` `yssetstr` | — | **无隧道**（官方例子名，转储 0 命中） | — | C# 内存字典；**登记面存在、原生不可达** → 见 §5 |

### 4.2 宠物 / 物品 / buff / 数值

| API | handler VA | 关键字节 |
|---|---|---|
| `ys_BBflowme` | `0x1006F0E0` | 段 `<2` → `-888` `0x1006F141` |
| `ys_SetPetV` | `0x100735B0` | 段 `<0xE`；`[player+0x4FC]` 宠表 |
| `Ys_GiveBBSkill` | `0x10075170` | 段不足 → `or eax,-1` |
| `Ys_GiveBB_SX` | `0x10075600` | 同上 |
| `Ys_KillBBbyName` | `0x1005C810` | caret 32 |
| `ys_CheckWupinIsBind` | `0x10073440` | 段 `<3` → `-1`；命中返回 Bind 字节 |
| `ys_WupinMakeIndex` | `0x100863B0` | lucker2 op1 |
| `ys_WupinGetData` | `0x10086860` | lucker2 op2 flag=0 |
| `ys_WupinGetData2Take` | `0x10086860` | op3 flag=1 |
| `ys_GetDataByClientItemID` | `0x10086E60` | lucker2 op4 |
| `Ys_GetItemDBData` | `0x1005D9F0` | caret 38 |
| `Ys_GetItemid` | `0x1005AC10` | caret 13 |
| `Ys_GetClientItemIDByItemid` | `0x1005AD40` | caret 20 |
| `Ys_GetItemJp` / `Ys_SetItemJp` | `0x1005D290` / `0x1005D4E0` | caret 35/36 |
| `ys_SetYs` / `ys_GetYs` | `0x10072CD0` / `0x10072F90` | 数字 17/18 |
| `Ys_GivePis` / `Ys_GetPis` | `0x1005E7CE` / `0x1005EAE3` | 中文隧道；门 cfg2+**0x664**「自定义元素」 |
| `ys_AddShuxing*` / `ys_SubShuxing` | `0x10071F10` | 数字 14 |
| `ys_GetA` / `ys_SetA` | `0x1006F8E0` | 数字 40 |
| `ys_Getshuxing` | `0x1005C4E0` | caret 31 |
| `ys_GetMember*` | `0x1006F630` | 数字 38 |
| `Ys_RepairInBag` | `0x1005C330` | caret 30 |
| `ys_Change_ly` | `0x1005A1D0` | caret 10 |
| `Ys_UpDataBody` | `0x1005C220` | caret 29；段 `<0xF` |
| `ys_Magic_huoqiang` | `0x1006F2C0` | 数字 37 |
| `Ys_SetHeroCSkill` | `0x10074EE0` | 数字 28 |
| `ys_DecExp` | `0x1006F790` | 数字 39 |
| `ys_CheckMapMonByName` | `0x10073210` | 数字 20 |
| `ys_DoEffect` | `0x1006FDE0` | 数字 12 |
| `ys_pick` | `0x1006D3D0` | 数字 19 |
| `ys_PlayerOut` | `0x1006FD00` | 数字 41 |
| `Ys_GetOther` | `0x10075B70` | 数字 32（prior commit） |
| `ys_HeroJp` | `0x1005EFDB` | 中文英雄极品（prior commit） |
| `ys_giveduar` | `0x10072650` | 数字 15 |
| `Ys_NpcGiveItemYs` | `0x10073B40` | 数字 24 |
| `Ys_GiveBind` | `0x10076060` | 数字 33 |
| `ys_Test_ground` | `0x100728A0` | 数字 16 |
| `ys_Ground_Other` | `0x10072A30` | 数字 22 |

---

## 5. STILL-UNPROVEN（7 条独立计数 + 11 文档名仅登记）

**无原生隧道的官方例子名（18）**：`ysattact`…`ysyeman` 等 —— 转储 0 命中；C# 登记为扩展调用面，**债务不成立**，保持 STILL-UNPROVEN。

**AllFuc 复合体（3）**：`ys_CDGetTimes` `ys_MakeSlaveEx` `ys_SendMsg` —— 证据在被调者，不单列 handler。

**具名定时器（1）**：`Ys_SetTimerByName` handler `0x10073E20` 已定位，C# 仍 fail-closed（与原生节拍器 `0x1008C7C0` 非同一设施）—— **STILL-UNPROVEN 行为层**。

**Give 五元素 `Ys_GiveItem`（1）**：Give 载荷非 `#ys` 形态，普查正则漏扫；宿主 Give 挂钩有证据，C# `GiveItem5El` 已接 —— 隧道链 CONFIRMED、字段级仍待逐字节。

---

## 6. MISSING 17 · 补登记

| 名字 | 判定 | 原因 |
|---|---|---|
| `NPC_CreatMons` | **BLOCKED** | Themida 生成路径；C# fail-closed 正确 |
| 其余 16 | **EQUIVALENT-BY-ABSENCE** | AllFuc 薄包装 / 纯 Pascal / `GetItemNameOnBody` / PlayerNotice；隧道已在 C# 通，**不补登记** |

未改 `YanshenApiNames`（与 `ys_b4_scriptapi` §4.1 一致：跨车道共用表，隔离需主代理裁决）。

---

## 7. 复现

```bat
python tools/ys_b4_api_census.py D:\loym2\_gapwork\ys-f2rest --verify
python tools/ys_f2rest_verify.py
```
