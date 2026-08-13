# DURA-37..44 深层逆向 · 装备耐久扩展写者(item +0x26 = Dura)普查与移植裁定

- 日期：2026-08-14
- 底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`，平映射（file_off = VA − 0x400000），大小 `0x10D8000` = 17,661,952。
- 工具：capstone 5.0.7 x86-32（`Cs(CS_ARCH_X86, CS_MODE_32)`，`detail=True`）。扫描/上下文/调用方脚本置于 `%TEMP%`（`dura_scan_x26.py` / `dura_ctx.py`）。
- 工作树：`D:/loym2/.claude/wt2/dura-deep`，分支 `w/dura-deep`（自 `master` fd0555e8 切出）。
- 前置：`docs/eqv_shard09_20260814.md`（DURA-36..45，8 条 BLOCKED）。本报告把 eqv-09 的 8 条大缺口全部**取证解闭**为精确身份 + 证据化裁定。
- **代码改动：0 处（仅文档提交）。** 结论：DURA-37..44 的全部 ~20 个 `+0x26` 写者，要么**已在 master 忠实移植（含耐久写）**，要么**合理 fail-closed（对应功能"效果本体"未移植，孤立补耐久写=臆造，违铁律）**。无一条属"功能已移植但漏耐久写"的外科缺口，故不改 GameSvr、不触发 build。

---

## 0. 基础事实（capstone 亲验，供全表复核）

| 事实 | 证据 |
|---|---|
| **item+0x26 = Dura(word)，item+0x28 = DuraMax(word)** | SetDura 设置器 `0x784584`：`mov cx,[eax+0x28]`(DuraMax) / `cmp dx,cx` / `jbe` / `mov [eax+0x26],cx`(封顶) 或 `mov [eax+0x26],dx`；GetDura `0x7845A0 mov ax,[eax+0x26];ret`；GetDuraMax `0x7845A8`。 |
| **GetUseItems(container, slot) = `0x75EC20`** | 入参 `eax=[player+0x4C0]`(装备容器)、`dl=槽号`。凡 `mov dl,N; mov eax,[..+0x4C0]; call 0x75EC20` 即取第 N 号装备槽物品。 |
| **`0x404828` = Delphi `is`（类继承判定）** | `mov eax,[item]; call 0x4048C8`（沿 VMT 父链 `mov ecx,[ecx-0x24]` 比对）。故写者前的 `mov edx,[全局]; call 0x404828` 是"物品 IS-A 某类"。 |
| 特殊类全局（类 VMT） | `[0x75E3F8]=TBujuk`(护身符)、`[0x75E7C4]=TDragonHeart`(龙之心)、`[0x75E4E8]=TPoisons`(毒药)、`[0x75E628]=TVessel`(泉水罐/器皿)。类名经 VMT−0x2C 的 ShortString 解出。 |
| 物品工厂类映射（C# 侧交叉印证，`Magic.cs:103-108`） | StdMode 25 + Shape 5 → **TBujuk**；StdMode 25 + Shape {1,2} → **TPoisons**；StdMode 25 + Shape 8 → **TVessel**。 |

全镜像 `word ptr [reg+0x26]` **写**指令普查：**165 命中**（含 mov/sub/dec/add/…）。其中绝大多数 base 并不指向物品结构（+0x26 在别的结构里是别的字段），真正的"物品耐久写"即本表 DURA-37..44 的 ~20 个。

---

## 1. ~20 个写者逐个身份 / 语义 / 槽 / 算术 / 触发 / C# 裁定

约定：**槽**= 作用目标（装备槽号/背包物品/使用中的物品/英雄槽）；**算术**= 对 Dura(item+0x26) 的运算。

### 1.1 已在 master 忠实移植（含耐久写，已逐条读证）——7 条

| VA | native 函数 / 类 | 槽 / 目标 | 算术 | 触发 | C# 落点（已含耐久写） |
|---|---|---|---|---|---|
| `0x73E9A4` | sub_73E93C（TBujuk 通用护身符门·测+耗合体） | 玩家 **slot9**(U_BUJUK) | `Dura −= nCount×100`；`nCount×100 ≥ Dura` 则销毁（`>=`） | 技能护身符消耗（DURA-10/12/13） | `Spells/Magic.cs:168 UseAmulet`（`dura=nCount*100; if(dura<Dura) Dura-=dura; else 销毁`）+ `CheckAmulet` |
| `0x73EB0A` | sub_73EA20（TBujuk 第二例程·raw） | slot9 **优先**，否则背包 | `Dura −= nCount`(raw，无×100)；`Dura<nCount` 换背包/销毁 | SKILL_62(神兵/天使) 等 raw 消耗（DURA-11） | `Players/TPlayObject.NativeAmuletConsume.cs:127`（`item.Dura -= (ushort)nCount`） |
| `0x6E9D0B` | sub_6E9BAC（TFixedCoordStone setter） | 背包中该定位石 | `Dura −= 1`；归零删物 | CM 3420(0xD5C) 记录定点 | `Players/TPlayObject.NativeFixedCoordStone.cs:197`（`item.Dura--`）；消费端 `:392` 同样 `item.Dura--` |
| `0x6D5F4E` | sub_6D5E50（TVessel 合并·eqv-09 误标"修理"） | 玩家 **slot9** 器皿(泉水罐) | `Dura += 100`/每合并栈，末尾 `min(Dura,DuraMax)` 封顶 | CM_1017(0x3F9) 背包同型栈合并入 slot9 | `Players/TPlayObject.NativeItemMerge.cs:115`（`target.Dura += 100`）+ `:122` 封顶 |
| `0x6DF96D` | sub_6DF7E8（Take/TakeExpand 消费体） | 背包"栈"物品(item+0x14==7) | 请求量 `need<Dura`：`Dura −= need`(留物)；否则耗尽删物 | 脚本 `Take`/`TakeExpand`(NPC 收取，全局串"NPC收取") | `ScriptSystem/PasEngine/PasApiBridge.cs:8906-8931`（`item.Dura = item.Dura - need`；耗尽 `RemoveAt+Dispose`） |
| `0x740C8D` | sub_740B04（取物工具·9 个调用点） | 背包栈物品 | 同上（`Dura −= di` / 耗尽删物） | 内部广用"按名取 N"（含 `0x6E506D` 等） | 同 Take 家族（PasApiBridge，`:8982` 姊妹删物臂） |
| `0x740DE4` | sub_740D5C（取物工具·又一实例） | 背包栈物品 | 同上（`Dura −= dx` / 耗尽删物） | "NPC收取"变体（格式串 `0x740F60`） | 同 Take 家族 |

> 注：`0x73E850`/`0x73E88C`（DoDamageWeapon slot1 武器归零/回写）不属 DURA-44 群，已 FAITHFUL（`TBaseObject.DoDamageWeapon`）。

### 1.2 合理 FAIL-CLOSED（对应"效果本体"未移植 / 运行期不可解析）——其余条目

| VA | native 函数 / 类 | 槽 / 目标 | 算术 | 触发 | 为何 fail-closed（缺口） |
|---|---|---|---|---|---|
| `0x6A0B11` `0x6A0BB4` `0x6A0C64` | sub_6A09F4（火云石/技能石复制管理器） | 3 个材料物品 | 每样 `Dura −= 1000`(=1 显示点) | CM 1061(0x425) 技能石复制 | **运行期 manager 表** `[[0x7D5F20]]` 静态解析为 0；C# `Players/TPlayObject.SkillStone.cs` 显式 fail-closed（`ReachesRuntimeManager` 段 `FailClosed=true`）。补写=臆造复制成败与背包变更。 |
| `0x788511` | sub_788418（**TNewHappyCake**·欢乐蛋糕/烟花） | 使用中的该物品 | `Dura −= 1`；广播"在[图]X,Y 施放…请大家前往观看"、"剩余:%d" | 物品使用（use-VMT+0x18，dd@0x7822D8） | 类 **不在** `TryUseItemEffect` switch（`Operate.cs:1096-1152`），落 `default:false`。C# 现有"传情烟花"(`NativeFireworkText`, CM_YANHUA_TEXT)是**另一物品**(整物删除)。孤立补 Dura−1 无广播效果=半移植。 |
| `0x788DAD` | sub_788CA8（**TTaoFaLingAddExpItem**·讨伐令加经验） | 使用中的该物品 | `Dura −= 1`；写 player+0xBC4/+0xBC8(经验加成+计时) | 物品使用（use-VMT+0x18，dd@0x781EB0） | 类不在 switch。且 `NativeWinExp.cs:222-224` 证：+0xBC4/+0xBC8 **整镜像仅本函数引用，经验链根本不读**——效果本体在原生即 inert。补写=补一对死字段。 |
| `0x78B1FE` | sub_78B1D8（**TLevelBuffItem**·等级 buff 物品） | 使用中的该物品 | `Dura −= 1`（门：`sub_746888(player)>0`、std+0x48） | 物品使用（use-VMT+0x18，dd@0x782204） | 类不在 switch，落 default。效果本体(`sub_746888` 语义)未移植；孤立补写=半移植。 |
| `0x68686E` | sub_6867B8（TDragonHeart 消耗·调用方 sub_6E2968） | **英雄** slot9(经 [player+0xBB0]→hero，`mov dl,9;call 0x75EC20`) TDragonHeart | `Dura −= ecx`(变量)；门 `Dura≥amount` | 英雄龙之心消耗（VMT/派发 dd@0x731A7B） | 该英雄龙之心消耗链的**触发/效果**未在 C# 落地（`Operate.cs:896` 仅提及 TDragonHeart 作为 hero slot9 refill 概念，非本消耗路径）。 |
| `0x68F4CA` | sub_68F450（TDragonHeart 累加器·前置门 sub_68F3F0） | slot9 TDragonHeart | `Dura −= di`(钳到 Dura)，累加 player+0x6CE 趋向上限 `[0x7D7194]` | 龙之心"每日/每session 累加"消耗 | 同族，触发/上限累加逻辑未移植。 |
| `0x68C9DE` | sub_68C8F0（TPoisons 消耗·调用方 sub_688650） | slot9 **或** 背包 TPoisons | `Dura −= 100`(0x64)；门 `Dura≥100` + std+0x15 匹配 | 消耗护身符(sub_73EA20,100)+毒药 的双消耗技能（sub_688650 调用方 `0x623B02`/`0x6D104B`） | sub_688650 这一"双消耗"技能路径未在 C# 落地。 |
| `0x6CF2B2` | sub_6CF1FC（群体效果消耗品·dispatcher sub_786ED8） | 使用中的该物品 | `Dura −= 1000`(=1 显示点)；对周围对象设 +0x482=7 并重算 | 物品使用，`std+0x15==6` 分支（dd@0x7800DC） | 群体 buff 效果本体未移植；`std+0x15==7` 另分支同族。 |

> 命名勘误（承 eqv-09）：`0x6D5F4E` 的 `add +0x64` **不是"修理"**，是 CM_1017 的 **TVessel 同型栈合并**（每合并一栈折入 +100 到 slot9 器皿），已在 `NativeItemMerge` 忠实落地。

---

## 2. 关键交叉结论：装备槽 0/2/3/7/8/10/12/15 的"受击掉耐久"——native 中不存在

这是用户点名的核心疑点（eqv-09 DURA-39："slot 0/2/3/7/8/10/12/15 掉耐久点未证"）。本次给出**决定性取证**：

1. **`+0x26` 写者全普查（165 命中）中，无任何写者位于受击路径、按槽循环扣减装备耐久。** 唯二的"战斗 `+0x26` 掉耐久点"是：slot1 武器（攻击时 `DoDamageWeapon`/sub_73E804）、slot9 护身符（特殊消耗 sub_73E93C/sub_73EA20）。
2. **native `StruckDamage` = sub_73F9FC 全函数反汇编（0x73F9FC–0x73FBC5）无 16 槽耐久循环**：其体为 免疫态(0x34/0x37 归零) → nDam=Random(10)+5 → 放大态(0x35 ×1.3 / 0x1E ×1.25|1.2) → 格挡 proc → `call sub_73FBE8`(仅 RecalcAbilitys 包装) → 护盾 +0x3FC → `[+0x1AC]` 落伤。**无逐槽 `sub word[slot+0x26]`**。
3. **SetDura 设置器(`0x784584`)的 8 个调用方**（0x63BBB5/0x63BBCF/0x6A3533/0x6A3564/0x6B860F/0x6F31C7/0x6F31F8/0x763886）**均不在受击路径**，排除"经 setter 间接掉甲"。

**结论**：本引擎中，衣服/项链/头盔/戒指/手镯/腰带/勋章/坐骑等装备槽（0/2/3/7/8/10/12/15，含 slot0 衣服）**受击不掉耐久**。

> ⚠️ **分歧候选（越界，只报不改）**：C# `TBaseObject.StruckDamage`（`Actors/TBaseObject.cs:5983-6041`，标注 "DURA-16"）当前**遍历全部 16 槽**、各 1/8 概率 `Dura −= nDam` 且归零销毁。此循环在本底本 sub_73F9FC 内**无对应**，疑为 GameOfMir C# 祖本遗留的多余行为（1:1 分歧）。因属 DURA-16 已提交域、且删除行为具破坏性、需其所有者按证据复核，本报告仅**标注建议 DURA-16 复检**，不在本任务改动。（唯一保留可能：若存在 `lea eax,[item+0x26]` 后 `mov [eax],..` 的地址计算式写入可绕过 disp8 扫描——但受击路径附近未见此形，Delphi 亦罕用。）

---

## 3. 覆盖率与方法

- **契约条目**：DURA-37..44 全部逐个反汇编解闭（eqv-09 之 8 条 BLOCKED → 精确身份 + 裁定）。
- **写者普查**：`word[reg+0x26]` 写指令 165 命中全量枚举；对 DURA-44 群 ~14 + DURA-37/42/43 全部亲验函数体、prologue、调用方（`E8/E9 rel32`、`push imm32`、dword 表项三法）。
- **C# 侧读证**：`Magic.cs`(UseAmulet/CheckAmulet)、`NativeAmuletConsume.cs`、`NativeFixedCoordStone.cs`、`NativeItemMerge.cs`、`PasApiBridge.cs`(Take 家族)、`SkillStone.cs`、`NativeWinExp.cs`、`Operate.cs`(TryUseItemEffect 派发)、`TBaseObject.cs`(DoDamageWeapon/StruckDamage)、`Grobal2.cs`(U_* 槽常量) 全部读证。
- **未执行编译**：无 C# 改动，未跑 dotnet build（无必要）。

---

## 4. 移植裁定汇总

| 裁定 | 条数 | 明细 |
|---|---:|---|
| 已忠实移植（含耐久写，本次读证确认） | 7 | 0x73E9A4 / 0x73EB0A / 0x6E9D0B / 0x6D5F4E / 0x6DF96D / 0x740C8D / 0x740DE4 |
| 合理 FAIL-CLOSED（运行期不可解析） | 1 组(3) | 0x6A0B11 / 0x6A0BB4 / 0x6A0C64（sub_6A09F4 火云石复制） |
| 合理 FAIL-CLOSED（效果本体未移植，孤立补写=臆造） | 7 | 0x788511 / 0x788DAD / 0x78B1FE / 0x68686E / 0x68F4CA / 0x68C9DE / 0x6CF2B2 |

**映射到 `m_UseItems[slot].Dura` 的装备槽写者**只有 slot9（0x73E9A4/0x73EB0A），已全移植；其余写者作用于**背包物品/使用中物品/英雄槽**，不落 `m_UseItems[slot]`，分别由 Take/merge/fixed-coord 覆盖或按上表 fail-closed。**故本任务无新增应移植的装备槽耐久写。**

刻意不做（做即违铁律）：
1. 为 3 个 use-VMT 物品类（TNewHappyCake/TTaoFaLingAddExpItem/TLevelBuffItem）单独补 `Dura−1` —— 缺其效果本体（广播/经验/buff），=半移植且经验字段原生即 inert。
2. 为 TDragonHeart/TPoisons/群体效果补写 —— 其技能触发链未在 C# 落地，补写=为不存在的功能造耐久扣减。
3. 触碰 C# StruckDamage 的 16 槽循环 —— 属 DURA-16 域，需其证据复核（本报告已把 native 无此循环的取证列全，供后续裁决）。

---

## 5. 分支 / 提交

- 分支：`w/dura-deep`（自 `master` fd0555e8 切出）
- 提交：本报告 `docs/dura_deep_writers_20260814.md`（**无代码改动，仅文档**）
