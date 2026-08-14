# 盘古3 页 · 34 键接线报告

- 日期：2026-08-14
- 工作树：`D:\loym2\_gapwork\ys-pangu3`　分支：`gap/ys-pangu3`
- 底本：M2Server `flat_image.bin`（base `0x400000`）；眼神 DLL `yanshen2_0_8_dll.memory.bin`（base `0x10000000`）
- 反汇编：`python tools/recount_dis.py dis <VA> <n>`
- 审计基线：`yanshen_completeness_audit_20260814.md` §2.4「盘古3 34/39 无行为」

## 收口

| 口径 | 数 |
|---|---:|
| 审计 MISSING（LABEL_ONLY） | 34 |
| **DONE**（含 10 条矩阵漏报、19 条本轮接线） | **29** |
| **BLOCKED**（fail-closed，保持 LABEL_ONLY） | **5** |
| 基线 IMPLEMENTED（未计入 34） | 4 |
| SCRIPT_ONLY（`施毒术`，PAS 脚本侧） | 1 |
| 页合计 | 39 |

`ys_key_reachability.py` 复跑：**IMPLEMENTED 33 / SCRIPT_ONLY 1 / LABEL_ONLY 5**（n=39）。

---

## 逐键 DONE / BLOCKED

### DONE — 补丁/宿主 VA 接线（19 键）

| 键 | 宿主 VA / 形态 | C# 落点 |
|---|---|---|
| 战士合击 | `0x7D33FC..0x7D341C` f64×5（读点 `0x68FF6D fmul [eax*8+0x7D33FC]`） | `YanshenComboTables.cs:88-103` + `HeroObject.cs:1471` |
| 战士合击_数值1..5 | 同上（插件 `0x100B89D5` 起直写 .data） | `YanshenApi.cs:5000-5004` → `WarriorComboMultiplier` |
| 法道合击 | `0x7D3278..0x7D3298` f64×5（读点 `0x68EF1D fmul [eax*8+0x7D3278]`） | `YanshenComboTables.cs:91-103` + `HeroObject.cs:1472` |
| 法道合击_数值1..5 | 同上（插件 `0x100B8DF9` 起） | `YanshenApi.cs:5006-5010` → `WizTaoComboMultiplier` |
| 中毒时间上限 | trampoline 起点 `0x76E5CE`（绿毒 `push ecx…`） | `YanshenPoisonTimeCap.cs:55-65` |
| 中毒时间上限_秒 | 桩体立即数 V（`cmp ecx,V` / `cmp eax,V`） | `MagicManager.cs:331-337,1472-1477` 调 `Cap()` |
| 脚本控制人物爆率 | `0x6DF2CC` SetV 桩 + 裸写消 `0x73D578`/`0x73DAC5` | `YanshenScriptDropRate.cs` + `TPlayObject.Base.cs:332` |
| 屏蔽排行榜 | `0x6CBA88 push ebp` → 插件写 `C3` | `YanshenHideRank.cs:43-47` + `TPlayObject.NativeQuestOrder.cs:20` |
| 装备提升人物爆率 | trampoline `0x71FD37 mov eax,[eax+0x14]` | `YanshenEquipDropBoost.cs:105-119` |
| 装备提升人物爆率_A值 | 桩体 `+0x013 mov ecx,A` | 同上 `BoostDropRateA()` |
| 装备提升人物爆率_B值 | 桩体 `+0x023 mov ecx,B` | 同上 `BoostDropRateB()` + `UsrEngn.cs:2570-2572` |

**反汇编取证（抽样）**

```
0x76E5CE  51 8B D3 52 50          ; 绿毒 SendDelayMsg 前（trampoline 覆盖点）
0x76E675  8B 45 F8 50 53          ; 红毒时长取指（trampoline 覆盖点）
0x68FF6D  fmul qword [eax*8+0x7D33FC]   ; 战士合击系数
0x68EF1D  fmul qword [eax*8+0x7D3278]   ; 法道合击系数
0x71FD37  8B 40 14 / F7 6D D4     ; 掉落 Random 分母（trampoline 覆盖点）
0x6CBA88  55                      ; sub_6CBA88 序言（CM 1060 排行榜）
0x73D578  C6 86 79 05 00 00 00    ; 裸写清零 +0x579（补丁消掉）
0x73DAC5  89 86 8C 01 00 00       ; 裸写 +0x18C（补丁消掉）
```

### DONE — 矩阵漏报、行为已存在（10 键，EQUIVALENT_BY_ABSENCE）

| 键 | 证据 | C# 落点 |
|---|---|---|
| 修改召唤神兽 | 插件 detour `0x76EE98/0x76EEAF`；页面对象 ctor 预置 | `MagicManager.cs:2027-2031` `TryGetModifyShenShou` |
| 人物等级1..3_值 | ctor `[edi+0x66C..0x674]=42/45/48` | `YanshenApi.cs:5149-5152` |
| 怪物名字1..3_值 | ctor `神兽/白虎/月灵` | 同上 |
| 怪物数量1..3_值 | ctor `2/2/2` | 同上 |

### BLOCKED — fail-closed（5 键）

| 键 | 宿主 VA | 原因 |
|---|---|---|
| 装备吸血 | `0x76E2A3`（trampoline 完整：`cmp [ebx+0x184]` → `call 0x769DB4 IncHealthSpell`） | 源字段 `[obj+0x184]` 写点仅在 `RecalcAbilitys` 装备聚合块（`0x73DE9D`），C# 无对应聚合；shape 136/137/138 扫不到 ⇒ 接上会恒 0 |
| 无极真气 | `0x74587C` | 非数值：AnsiString len=11 + `，持续 6 秒` 文案 memcpy；唯一消费者 `sub_7457D7` 只拼 SysMsg |
| 无极真气_A值 | （无数值补丁点） | 同上，`_A值` 在图谱无 apply 臂 |
| 无极真气_时间 | `0x745880` 文本内「6」 | 同上，只改提示串秒数 |
| 施毒术_公式值 | `0x76E599` 31B 整段替换（`idiv V` 公式） | 31B 桩体已逐字节回收，但 C# `nParam3` 走 `Round(btLevel/3*nPower/…)` 不同基座；需毒系车道补 `[vmt+0xCC]` 链后再落 |

**BLOCKED 反汇编取证**

```
0x76E2A3  8B D3 8B C6 8B 38       ; 装备吸血 trampoline 覆盖点（回放段）
0x74587C  0B 00 00 00             ; len=11（AnsiString 长度域，非立即数）
0x76E599  8B C6 E8 CC A3 D5 FF    ; 施毒术原逻辑入口（31B 替换点）
          3C 04 75 07 B8 08 00 00 00  ; effLevel==4 ? 8 : (level&FF)+1
```

---

## 改动文件

| 文件 | 行/说明 |
|---|---|
| `GameSvr/Plugins/YanshenComboTables.cs` | 1-106（新） |
| `GameSvr/Plugins/YanshenPoisonTimeCap.cs` | 1-66（新） |
| `GameSvr/Plugins/YanshenScriptDropRate.cs` | 全文件（新） |
| `GameSvr/Plugins/YanshenHideRank.cs` | 1-49（新） |
| `GameSvr/Plugins/YanshenEquipDropBoost.cs` | 1-121（新） |
| `GameSvr/UsrSystem/UsrEngn.cs` | 2570-2572 |
| `GameSvr/Spells/MagicManager.cs` | 331-337, 1472-1477, 2027-2031 |
| `GameSvr/Actors/HeroObject.cs` | 59, 1471-1472 |
| `GameSvr/Players/TPlayObject.NativeQuestOrder.cs` | 19-20 |
| `GameSvr/Players/TPlayObject.Base.cs` | 332-333 |
| `GameSvr/Actors/TBaseObject.NativeDeathDropDenominator.cs` | 172 |
| `GameSvr/Plugins/YanshenApi.cs` | 4535-5010, 5141-5160 等访问器/缺省 |

主要提交：`8967f8ba`（合击12）· `dea54ad6`（中毒+脚本爆率）· `3fbca823`（屏蔽排行榜）· `34565196`/`6bcdab25`（装备爆率三键）。

---

## 一句话

**本页接线 29 键 / BLOCKED 5 键**（34 审计 MISSING 全部结案；页内另 4 键基线已实现、`施毒术` 为 SCRIPT_ONLY）。
