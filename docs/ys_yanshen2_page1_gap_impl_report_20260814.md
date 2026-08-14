# 眼神2(第1页) · gap/ys-yanshen2p1 实现子代理报告

- 日期：2026-08-14
- 分支：`gap/ys-yanshen2p1`
- 底本：眼神 2.0.8 delayed 转储 `staging/yanshen208_strparam_runtime_dump_delayed_20260719/yanshen2_0_8_dll.memory.bin`（基址 `0x10000000`）
- 复跑：`python tools/ys_page1_census.py`；`dotnet run --project AuditTools/YanshenPage1CensusCheck` → **PASS**
- 纪律：20 惰性键不得写引擎消费者（反臆造闸门）；9 键缺宿主挂载点不得猜时机

## 汇总

| 类别 | 数 | 说明 |
|---|---:|---|
| **DONE**（EQUIVALENT-BY-ABSENCE） | **20** | 原版 45 MB 镜像（含 Themida 远端区）零消费者；C# 仅 `YanshenApi` 访问器，已 1:1 |
| **BLOCKED**（C1） | **9** | 插件侧消费者 VA 已逐字节解出；唯一静态调用链 `0x10F2D759 call 0x100795C0` / `0x1123B15E call 0x10068470` 落在 Themida 混淆区，宿主入口不可证 |

---

## 逐键（29 无行为键）

### A. DONE — 原版无行为，C# 不写引擎代码（20）

| 键 | VA 证据 | C# | 备注 |
|---|---|---|---|
| 技能触发脚本 | cfg+`0x508`；全库 `cmp [reg+0x508],0x1F4` 零命中（`0x100D269F` 为触发注册表步长 `0x88` 误认） | `YanshenApi.cs:4690` `IsSkillTrigger` | 序列化器 `sub_10004140` |
| 英雄自动开盾 | cfg+`0x668`；零消费者 | `YanshenApi.cs:4620` `IsHeroAutoShield` | |
| 装备转生穿戴判定a | cfg+`0x66C`；零消费者 | `YanshenApi.cs:4761` `IsRebirthWear` | |
| 诱惑之光触发脚本a | cfg+`0x670`；零消费者 | `YanshenApi.cs:4699` `IsLureTrigger` | |
| 烈火固定增伤 | cfg+`0x678`；零消费者 | `YanshenApi.cs:4493` `IsFireFixDmg` | |
| 冰咆哮固定增伤 | cfg+`0x67C`；零消费者 | `YanshenApi.cs:4480` `IsIceStormFixDmg` | |
| 火墙固定增伤 | cfg+`0x680`；零消费者 | `YanshenApi.cs:4471` `IsFireWallFixDmg` | |
| 火符固定增伤 | cfg+`0x684`；零消费者 | `YanshenApi.cs:4495` `IsAmuletFixDmg` | |
| 技能等级突破 | cfg+`0x69C`；零消费者；407 补丁站点无此键 | `YanshenApi.cs:5044` `IsLevelBreak` | |
| 宝宝自动叛变 | cfg+`0x6A0`；零消费者（`0x100D2B41 cmp [ebx+0x6A0],0` 为触发注册表第 N 条使能位） | `YanshenApi.cs:4616` `IsPetAutoRebel` | |
| 新呼唤宝宝 | cfg+`0x6A4`；零消费者 | `YanshenApi.cs:4609` `IsNewCallPet` | |
| 技能等级突破_最大值 | cfg+`0x6A8`；任意读位移零命中 | `YanshenApi.cs:5045` `IsLevelBreakMax` | 数值型，非 `0x1F4` 判据 |
| 嗜血术范围 | cfg+`0x808`；零消费者 | `YanshenApi.cs:4397` `IsBloodRange` | |
| 主号施法速度 | cfg+`0x83C`；零消费者 | `YanshenApi.cs:5047` `IsMainCastSpeed` | |
| 装备多职业 | cfg+`0x844`；零消费者 | `YanshenApi.cs:4760` `IsMultiJob` | |
| 角色多阵营 | cfg+`0x848`；零消费者 | `YanshenApi.cs:4949` `IsMultiFaction` | |
| 战队职业限制 | cfg+`0x84C`；零消费者 | `YanshenApi.cs:5051` `IsGamePartnerLimit` | |
| 穿戴触发_plus | cfg+`0x860`；零消费者（`0x100922C9` 为 GUI 页面对象勾选框生产者） | `YanshenApi.cs:4696` `IsWearPlusTrigger` | |
| 切换暴击报文 | cfg+`0x864`；零消费者 | `YanshenApi.cs:4554` `IsSwitchCritMsg` | |
| 主号全局法速 | cfg+`0x8CC`；零消费者 | `YanshenApi.cs:5046` `IsMainGlobalSpeed` | 数值型 |

### B. BLOCKED — 语义已解，宿主挂载点不可证（9）

| 键 | 消费者 VA | 卡点 | C#（仅访问器） |
|---|---|---|---|
| 冰咆哮切割 | `0x1007AF12` `cmp [cfg+0x688],0x1F4`；切割臂 `0x1007AF0C` magicId=33 → `S(1,116)` | `sub_100795C0` 唯一 rel32 调用 `0x10F2D759`（Themida 远端区）；M2 零补丁标签 | `YanshenApi.cs:4479` |
| 火墙切割 | `0x1007AF78`；magicId=22 → `S(1,117)` | 同上 | `YanshenApi.cs:4470` |
| 烈火切割 | `0x1007AFDD`；magicId=1007 → `S(1,118)` | 同上 | `YanshenApi.cs:4492` |
| 雷电术切割 | `0x1007B043`；magicId=11 → `S(1,119)` | 同上 | `YanshenApi.cs:4487` |
| 火符切割 | `0x1007B0A6`；magicId=13 → `S(1,120)` | 同上 | `YanshenApi.cs:4494` |
| 英雄千分比免伤 | `0x1007A8A7` `cmp [cfg+0x108],0x1F4`；`0x1007A8FE` `GetS(hero,1,58)`；千分比截断减伤 | 同上流水线；且 C# 英雄对象尚无独立 S 银行（见 `YanshenApi.cs:4663-4669` 注释） | `YanshenApi.cs:4671` |
| 主号高级暴击 | `0x10079FB1`；`call [0x1031C250]` 运行期桩（转储为 0） | 载荷不可判 + 挂载点不可证 | `YanshenApi.cs:5048` |
| 高级英雄倍功暴击 | `0x1007A014`；`call [0x1031C254]` 运行期桩（转储为 0） | 同上 | `YanshenApi.cs:4622` |
| 主号分身术a | `0x1006953F` `cmp [cfg+0x6DC],0x1F4`；子配置 `cfg+0x1860` 字符串字典 | `sub_10068470` 唯一调用 `0x1123B15E`（Themida）；子键未展开 | `YanshenApi.cs:5049` |

#### 切割/免伤流水线（blocked 但语义备齐）

```
0x10F2D759  e8 62 be 14 ff     call 0x100795C0
0x10F2D75E  83 c4 18           add esp, 0x18          ; 6×4 cdecl 参
sub_100795C0(defender=[ebp+8], attacker=[ebp+0xC], damage=[ebp+0x10] in/out,
             defenderClass=[ebp+0x14], attackerClass=[ebp+0x18], magicId=[ebp+0x1C])
```

前置 `S(1,49)==1314` 灌种已由 `TPlayObject.YanshenSeedLoginSVars()`（`TPlayObject.YanshenSVarSeed.cs:70`，原生 `0x100CE4EA`）接在 `PasScriptHost.cs:998`。

#### 解锁路径（C1）

1. Themida 远端区去混淆，从 `0x10F2D759` 上溯到 M2Server trampoline；或
2. 活体调试：`0x100795C0` 断点 → 返回地址链。

---

## 反汇编复核（本轮亲验）

| VA | 指令摘要 |
|---|---|
| `0x1007AF12` | `cmp dword [ecx+0x688], 0x1F4`（冰咆哮切割门控） |
| `0x1007AF24` | `mov esi,[edx+0x804]`（攻击方 S 银行） |
| `0x1007AF46` | `cmp ebx, 0x522`（S(1,49)==1314 哨兵） |
| `0x1007AF67` | `add [ebp+0x10], ebx`（damage += 槽值） |
| `0x1007A8A7` | `cmp [edx+0x108], 0x1F4`（英雄千分比免伤门控） |
| `0x10079FB1` | `cmp [edx+0x518], 0x1F4`（主号高级暴击门控） |
| `0x1006953F` | `cmp [ecx+0x6DC], 0x1F4`（主号分身术a 门控） |
| `0x10F2D759` | `call 0x100795C0`（delayed 转储唯一静态引用） |

---

## 未改动项（刻意）

- **无**新增 `GameSvr` 引擎消费者（20 键反臆造；9 键 fail-closed）
- **无** `dotnet build`
- 本页 **0** 条 M2Server 补丁（407 安装点 / 107 特性标签，本页键命中 0）
