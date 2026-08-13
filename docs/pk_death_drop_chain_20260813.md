# PK / 善恶值 / 死亡掉落 —— 逐字节链路（2026-08-13）

镜像 `D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`。
反汇编 capstone（`staging/_cs_disasm.py` / `_cs_field.py` / `_cs_find.py`），
VMT 归属用本次新写的 `staging/_pk_vmtwhere.py` / `_pk_slotdump.py`（只读）。
所有结论都给 VA + 字节 + 反汇编；凡是没给的一律标 BLOCKED。

同一分支上另有一份并行产出 `docs/m_ident_pk_death_drop_20260813.md`（PKD-01..07 与
PKD-10）。两份是互补的：那一份写死亡爆装的抽签序列与谋杀惩罚链，这一份写可攻击性
三层结构、善恶值规则表与落地归属。重叠的七条我做过独立字节复核，结论一致。

---

## 0. 三个入口的身份（先把层次钉死，否则后面全乱）

| 层 | native | 说明 |
|---|---|---|
| 外层通用门 | `sub_767498` | **非虚**，全镜像 `E8` 直调 **169** 处。九道硬门 → 调 `[self.vmt+0x20]` |
| 虚槽 `+0x20` | TCreature/THumanKind/TAnimal/TAIMon/TMonster/TSuperGuard = `sub_7671F0`；**TPlayer/TGdMsgGMAgent = `sub_6C13C4`**；THeroAct = `sub_686AB8`；TGuardUnit/TArcherGuard = `sub_6846B0`；TFieldHero = `sub_60931C`；TShadowHero = `sub_71B790` | 关系判定（主人族 / 攻击模式族） |
| 保护判定 | `sub_6C175C` | 只被 `sub_6C13C4` @0x6C172A 调用，= C# `IsProtectTarget` |
| 责任玩家解析 | `[vmt+0xB4]`：TCreature=`sub_769910`（递归取 `[+0x38C]`）、**TPlayer=`sub_6C185C` 是裸 `C3`**（eax 未改 → 返回自身） | C# 无此递归解析器，一律用 `m_Master` 近似 |

C# 的对应：`IsProperTarget` ≙ `sub_767498`，`IsAttackTarget` ≙ `[vmt+0x20]`，
`IsProtectTarget` ≙ `sub_6C175C`。**C# 把外层的九道门丢了**（本次已补，PKD-08）。

---

## 1. 可攻击性判定链

### 1.1 外层九道门 —— `sub_767498`（eax=self=edi，edx=target=esi）

```
7674A1  85 F6                    test esi,esi
7674A3  74 4E                    je   0x7674F3        ; target = nil          -> FALSE
7674A5  8B C6 / E8 FC B8 00 00   call sub_772DA8      ; = `8A 40 74 C3` = mov al,[eax+0x74]
7674AC  84 C0 / 75 43            test al,al / jne     ; target.m_boDeath      -> FALSE
7674B0  80 7E 73 00 / 75 3D      cmp byte [esi+0x73],0 / jne
                                                      ; target.m_boGhost      -> FALSE
7674B6  3B FE / 74 39            cmp edi,esi / je     ; self = target         -> FALSE
7674BA  80 BE E0 02 00 00 00     cmp byte [esi+0x2E0],0 / 75 30 jne
                                                      ; target.m_boAdminMode  -> FALSE
7674C3  80 BE E5 02 00 00 00     cmp byte [esi+0x2E5],0 / 75 27 jne
                                                      ; target.m_boStoneMode  -> FALSE
7674CC  8B 86 28 01 00 00        mov eax,[esi+0x128]
7674D2  3B 87 28 01 00 00        cmp eax,[edi+0x128] / 75 19 jne
                                                      ; 不同地图              -> FALSE
7674DA  B2 34 / E8 …             mov dl,0x34 / call sub_772960 / 75 0C jne
                                                      ; target 有状态 52      -> FALSE
7674E7  8A 86 78 01 00 00        mov al,[esi+0x178]
7674ED  04 10                    add al,0x10          ; == sub al,0xF0
7674EF  2C 02                    sub al,2
7674F1  73 04                    jae  0x7674F7        ; CF=0 -> 继续
7674F3  33 C0                    xor eax,eax          ; race ∈ {240,241}      -> FALSE
7674F7  FF 51 20                 call [self.vmt+0x20]
```

`add al,0x10 / sub al,2 / jae` 是 Delphi `x in [240,241]` 的惯用式，**只拒 240 与 241**，
不是「>= 240」。全镜像扫 `78 01 00 00 F0` / `…F1` 两式各 0 命中，说明这两个种族只能
来自 Monster.DB 的 Race 列。

`sub_772DA8` 的 4 个字节 `8A 40 74 C3` 是 `m_boDeath` 取值器 —— 顺带确证
**`+0x73` = m_boGhost、`+0x74` = m_boDeath**（两份旧 discovery 文档把这两个写反过）。

**C# 判定：`MISSING` → 本次已补**（`TBaseObject.NativeProperTargetGate.cs`，
`IsProperTarget` 首句调用）。原先这九道门摊在约 50 个调用点上各写一部分，多数只写
`!m_boDeath && !m_boGhost`；跨地图 / 石化 / 管理员模式 / 状态 52 在没写全的路径上可被攻击。

**残留 `DIVERGENT`（未改，风险高）**：`GuardUnit.IsProperTarget`（`GuardUnit.cs:70`）
覆写了**外层**且不调 base，所以卫士完全绕过九道门。原生卫士覆写的是**内层** `[vmt+0x20]`
= `sub_6846B0`，外层照走。改法是把 `GuardUnit` 的覆写移到 `IsAttackTarget`，属卫士 AI 重构，
不在本次范围。

### 1.2 内层：怪物/宠物族 `sub_7671F0`（TCreature `[vmt+0x20]`）

```
7671FD  cmp byte [edi+0x178],0x32 / 0F 86 jbe 0x767476   ; self.race <= 50 -> bl:=1 直落尾巴
76720A  call [self.vmt+0xB4] -> [ebp-4] = 责任玩家
767217  je 0x7673D1                                       ; 无责任玩家 -> 另一支
--- 有主人支 ---
767221/76722C/767237  target ∈ {P.m_LastHiter(+0x354), P.m_ExpHitter(+0x34C), P.m_TargetCret(+0x344)} -> bl:=1
767244  self.m_Master(+0x38C) 非空且 target ∈ 它的同三项                                  -> bl:=1
76726D  target.m_TargetCret == self.m_Master                                              -> bl:=1
767284  target.m_TargetCret.m_Master == self.m_Master 且 target.race != 0                 -> bl:=1
7672A3  self == target.m_TargetCret 且 target.race **> 0x32**（0x7672B2 `76 jbe` 跳过）   -> bl:=1
7672B6  两边都有主人时：self.m_Master.m_LastHiter == target.m_Master 或 == 其 m_TargetCret -> bl:=1
7672EC  bl 仍为 0 时：P.[+0xBB0]（英雄）非空且 target == 该英雄的 m_TargetCret            -> bl:=1
76730B  target.m_Master == self.m_Master                                                  -> ebx:=0
76731B  target.race >= 0x32 且 != 0x36 且 target.[+0x480]                                 -> ebx:=0
767334  P.[+0x4C7]（主人休息位）                                                          -> ebx:=0
767342  target.race ∈ {0, 0x36} 且 sub_7684DC(**target**, **self.x**, **self.y**, 10)     -> ebx:=0
76736F  target == P.[+0xBB0]（主人的英雄）                                                -> ebx:=0
76737C  target == self.m_Master                                                           -> ebx:=0
767386  self.[+0x3A8] != -1 时的阵营梯（见下）
--- 尾巴 ---
767424  self.map.[+0x5C]=SAFE 且 self.map.[+0x8C] != 3                                    -> ebx:=0
76743B  self.map SAFE 且 self.map.[+0x90]=MONATTACK 且 self is TAnimal                    -> bl:=1
767469  self.[+0x2EB]                                                                     -> bl:=1
767476  （race<=50 的入口）bl:=1
767478  target.[+0x2E0] 或 target.[+0x2E5]                                                -> ebx:=0
```

对 C# `TBaseObject.IsAttackTarget`（`TBaseObject.cs:5171`）逐条：

| # | native | C# | 判定 |
|---|---|---|---|
| 入口 | `jbe` → race **<= 50** 走恒真尾巴 | `if (m_btRaceServer >= Grobal2.RC_ANIMAL)` → race **>= 50** 走主人梯 | `DIVERGENT`（race 恰为 50 时相反）。未改：TCreature 构造器默认写 50（`0x764E5F`），改动面覆盖全部动物 |
| 命中 1/2 | `[ebp-4]` 责任玩家 vs `self.m_Master` 两套 | 都用 `m_Master` | `DIVERGENT`（多层宠物链才有差） |
| 0x7672A3 | `target.race > 50` | `>= RC_ANIMAL` | `DIVERGENT`（差 race==50 一点） |
| 0x7672EC | 主人英雄的仇恨目标也算可打 | 无 | `MISSING` |
| 0x76731B | `[+0x480]` 前面还有 `race>=50 && race!=54` 两个条件 | `if (BaseObject.m_boHolySeize)` 无条件 | `DIVERGENT`（C# 更严，方向安全） |
| 0x767334 | `P.[+0x4C7]` 主人休息位 | `m_Master.m_boSlaveRelax` | `FAITHFUL`（位置也对，在英雄门之前） |
| 0x767342 | 种族集含 **0x36 英雄**；且传的是 **self 的坐标** 去测 target 的安全区 | 只判 `RC_PLAYOBJECT`，且用 `BaseObject.InSafeZone()`（target 自己的坐标） | `DIVERGENT`（未改，安全区语义整体另有 BLOCKED） |
| 0x76736F | target == 主人的英雄 → 不打 | 无 | `MISSING` → **本次已补（PKD-09）** |
| 0x76737C | target == 自己的主人 → 不打 | 无 | `MISSING` → **本次已补（PKD-09）** |
| 0x767386 | `[+0x3A8]` 阵营梯 | 无 | `BLOCKED`（`[+0x3A8]` 身份未定，见 §5） |
| 0x767424 | SAFE 地图且 sky != 3 → 不可攻击 | 无 | `MISSING`（自建：C# 的 SAFE 语义走 `InSafeZone()`，与原生这条**地图旗标直判**不是一回事） |

### 1.3 内层：玩家族 `sub_6C13C4`（TPlayer `[vmt+0x20]`）

```
6C13EA  call [target.vmt+0xF0]           -> TRUE 则直接 FALSE
6C13F8  target is [0x67F750] 类          -> ebx := not sub_683204(...)  提前返回
6C1425  edi := target.[vmt+0xB4]()        ; 目标的责任玩家
6C1432  edi == nil:
6C1439    target.race < 0x32             -> FALSE
6C1446    self.[+0x3A8] != -1 且 target.[+0x3A8] != -1:相等 -> FALSE，否则 TRUE
          否则 TRUE
6C147C  edi != nil:
6C1482    同上阵营梯（对 edi）
6C14A5    self == edi:  ebx := (self.[+0xAED] == 0)      ; 只有 HAM_ALL(0) 能打自己的宠物
6C14B8    ebx := 1；al := self.[+0xAED] = MyAttackMode
6C14C0    if self.[+0x1829] == 3 then al := 1
6C14D0    cmp eax,7 / ja 0x6C171F / jmp [eax*4 + 0x6C14E0]     ; 8 条臂
             [0]=0x6C171F 保持 TRUE     [1]=0x6C1500 ebx:=0
             [2]=0x6C1507 名字比对 sub_6B7B8C 后 `xor bl,1`
             [3]=0x6C1529 组队/师徒 sub_706B78
             [4]=0x6C15B2 行会（含 0xAE8/0xA80 与行会名 sub_40591C 比对）
             [5]=0x6C158E [+0xAE8] setne
             [6]=0x6C1719 ebx:=0        [7]=0x6C171D ebx:=1
6C1702  （PK 攻击模式臂）ebx := (target.MyPKpoint >= [[0x7D5FAC]])   ; setge
6C171F  ebx 为真则 ebx := sub_6C175C(self, edi, target)   ; = IsProtectTarget
```

C# 把这一整套放在 `TBaseObject.IsAttackTarget` 的 `else` 支里（`m_btAttatckMode` switch），
结构对得上；`M2Share.g_Config.boNonPKServer → IsAttackTarget_sub_4C88E4()`（恒 true）
这一支在原生对应 `sub_4C88E4`，未复核，标 `BLOCKED`。
`[+0x1829] == 3 → 攻击模式强制为 1` 这条 C# **没有**，`MISSING`（`[+0x1829]` 身份未定）。

### 1.4 保护判定 `sub_6C175C`（= `IsProtectTarget`，eax=self, edx=责任玩家, ecx=原目标）

```
6C1769  C6 45 FF 01              result := TRUE
6C176D  80 BB 29 18 00 00 03     cmp byte [self+0x1829],3 / 74 32 je -> FALSE
6C1776  6A 0A / call sub_7684DC  InSafeZone(self,  self.x,  self.y,  10) -> FALSE
6C178F  6A 0A / call sub_7684DC  InSafeZone(target,target.x,target.y,10) -> FALSE
6C17B1  80 BB C4 04 00 00 00     cmp byte [self+0x4C4],0 / 75 72 jne 0x6C182C  ; 跳过等级梯
6C17BA  eax := [[0x7D5FAC]] (200)
6C17C1  3B 83 60 01 00 00 / 7F 2A  cmp eax,[self+0x160] / jg 0x6C17F3   ; self 非红 -> 第二梯
6C17C9  66 83 BB 78 02 .. 14 / 76  cmp word [self+0x278],20 / jbe -> 第二梯
6C17D3  cmp word [target+0x278],20 / 77 ja -> 第二梯
6C17DD  cmp [target+0x160],threshold / 7D jge -> 第二梯
6C17ED  result := FALSE          ; 红名 & Lv>20 打 非红 & Lv<=20  => 受保护
6C17F3  cmp word [self+0x278],20 / 77 ja 0x6C182C
6C17FD  cmp threshold,[self+0x160] / 7E jle 0x6C182C
6C180C  cmp [target+0x160],threshold / 7C jl 0x6C182C
6C181C  cmp word [target+0x278],20 / 76 jbe 0x6C182C
6C1826  result := FALSE          ; 非红 & Lv<=20 打 红名 & Lv>20  => 受保护
6C182C  now := GetTickCount()
6C1833  now - self.[+0x378]   < 0xBB8 (3000) -> FALSE
6C1841  now - target.[+0x378] < 0xBB8        -> FALSE
```

C# `TBaseObject.Base.cs:1374 IsProtectTarget` 的差异：

| 项 | native | C# | 判定 |
|---|---|---|---|
| `[self+0x1829]==3` 直接 FALSE | 有 | 无 | `MISSING`（`[+0x1829]` 身份未定，`BLOCKED`） |
| 安全区两测 | `sub_7684DC(...,10)` 对 self 与**原目标** | `InSafeZone() \|\| BaseObject.InSafeZone()` | `FAITHFUL`（`InSafeZone(Envir,x,y)` 已按 `sub_7684DC` 复刻，range=`nSafeZoneSize`=10） |
| 免战门 | 读 **self** 的 `[+0x4C4]`（`sub_6B6B78` 是它的 setter，改动时刷名字颜色 → 就是 `m_boInFreePKArea`） | 读 **target** 的 `m_boInFreePKArea` | `DIVERGENT`（未改：`[+0x714]` 那个派生布尔还没定性，贸然翻边会改整条免战语义） |
| 等级门数量 | **一套**，阈值都是立即数 `0x14 = 20` | **两套**（`boPKLevelProtect`/`nPKProtectLevel` + `nRedPKProtectLevel`），并多了 `m_boPKFlag` 项 | `DIVERGENT`（第一套 `boPKLevelProtect` 在 `sub_6C175C` 无对应） |
| 3 秒门 | 在 `[+0x4C4]` 门**之外**（`jne` 的目标就是它），恒执行 | 嵌在 `if (!BaseObject.m_boInFreePKArea)` **之内** | `DIVERGENT`（免战区里 C# 丢掉 3 秒切图保护） |
| `[+0x378]` | 3 个写入点都在切图/传送路径（`sub_764E20` 构造器、`sub_6BCEE8`、`sub_6BD294`、`sub_768D78`） | `m_dwMapMoveTick` | `FAITHFUL`（映射合理） |

---

## 2. 善恶值 / PK 值规则表

| 事项 | native | 值 / 方向 | C# |
|---|---|---|---|
| 字段 | RTTI `006AD2A4 name='MyPKpoint' GetProc=FF000160` | `Self+0x160`，int，**有符号** | `m_nPkPoint` `FAITHFUL` |
| PKLevel | `sub_76866B` `mov ecx,0x64 / cdq / idiv ecx` | 有符号除 100 | `m_nPkPoint / 100` `FAITHFUL` |
| 杀人加点 | `sub_6C0FE4` @0x6C10F4 `mov edx,[[0x7D5AE8]]` → `IncPkPoint sub_73F4BC` | 配置值 | `nKillHumanAddPKPoint` `FAITHFUL` |
| 加点入口门 | `sub_6C0FE4` @0x6C1006-0x6C1013 `cmp [[0x7D5FAC]],[victim+0x160] / jl 退出` | **受害者 PK > 200 则凶手什么都不承担** | 见 §4 已修 |
| Die 侧的门 | `sub_6C07A0` @0x6C0830/0x6C083F/0x6C084E/0x6C0863 | FREEPK / FIGHT / FIGHT3 / `victimPK <= 200` | 见 §4 已修 |
| 凶手解析 | 0x6C086B `call [vmt+0xB4]`；nil / **m_boGhost(+0x73)** / **自杀** 三道否决 | — | 见 §4 已修 |
| 幸运扣减 | 0x6C1019 `mov edx,0xFFFFFE0C(-500)` → `sub_7698BC`，`/500.0` 后 `sub_403580`（**截断**，不是四舍五入）→ −1 级 | **排在行会战/攻城/免战/正当防卫全部判定之前，无条件** | 见 §4 已修 |
| 幸运钳位 | `sub_7698BC` 0x7698E3 `cmp eax,5`、0x7698F4 `cmp eax,-0xA` | `[+5, −10]` | `FAITHFUL` |
| 正当防卫 | 0x6C10D6 `cmp byte [victim+0x4B9],0 / jne` | `m_boPKFlag`（写 1 的点 `0x6D7909`） | `IsGoodKilling` `FAITHFUL` |
| 受害者等级保护 | 0x6C10E3 `movzx eax,word [victim+0x278]` / `cmp eax,[[0x7D6B8C]]` / `jl` → 走「受防卫法保护」支 | 低于配置等级不加 PK，改给荣耀 | `MISSING` |
| 武器解锁 | 0x6C114F `cmp [victim+0x160],0x64 / jge 退出`，0x6C1158 `Random(5)==0` → `sub_73D194` | 受害者 PK < 100 且 1/5 | `FAITHFUL`（C# `PKLevel() < 1` ≡ `< 100`） |
| **衰减** | `sub_6B2D38` 0x6B3705 `sub edx,[self+0x734]` / 0x6B370B `cmp edx,0x1D4C0` / 0x6B3711 **`jbe` 跳过**；0x6B3719 **无条件**刷新时间戳；0x6B3722 `cmp [+0x160],0 / jle` 才 `DecPKPoint(1)` | 按**时间**：每 120000 ms 减 1，严格大于；PK<=0 时时间戳仍然推进 | `TBaseObject.Base.cs:515-522` **`FAITHFUL`**（`dwDecPkPointTime=120000`、`nDecPkPointCount=1`） |
| Dec 后刷色条件 | `sub_6CCB0C` 0x6CCB4E `dec ebx / sub ebx,2 / jae 跳过` = Delphi `x in [1..2]` | oldLevel ∈ {1,2} 才发 10046 | C# `(nC>0 && nC<=2)` **`FAITHFUL`**（旧任务书要求删 `nC>0` 是错的，会多发包） |
| Inc 后刷色条件 | `sub_73F4BC` 0x73F4ED `cmp eax,2 / jg 跳过` | newLevel <= 2 才发 | `FAITHFUL` |
| **名字颜色** | `sub_76865C`：`bl := [self+0x155]`；`eax := [self+0x160] / 100`；`cmp eax,1 / jne` → `bl := 0xFB`；`cmp eax,2 / jl 保持` → `bl := 0xF9` | **PKLevel == 1 → 0xFB（灰/黄）；PKLevel >= 2 → 0xF9（红）**；比较方向：等于 1 用 `jne`，红名用 **`jl` 跳过**即 `>= 2` | `GetNamecolor()` `FAITHFUL`（默认值 0xFB/0xF9 与立即数一致） |
| 卫士击杀重置 | `sub_6C07A0` 0x6C089E `cmp byte [killer+0x178],0x70`、0x6C08AA `cmp [self+0x160],0xC8`、0x6C08B9 `mov …,0x64` | 弓箭手守卫杀死且 PK>=200 → **赋值** 100（不是减法），且用**立即数 200** 而非 `[[0x7D5FAC]]` | 早前已补，`FAITHFUL` |
| 持久化 | — | — | **BLOCKED**：本次未查存档 codec 里 `MyPKpoint` 的读/写/编码三条路径（§4.19 要求三条一起看） |

**阈值只有一个来源**：`off_7D5FAC → 0x7DCF00`，初值 200，C# `g_Config.nPKPunishPoint = 200`。
注意原生自己不一致：卫士重置那处写死 `0xC8`，其余读全局。

---

## 3. 死亡掉落的完整概率链

### 3.1 策略梯 `sub_741368`（THumanKind.Die，VMT `+0x84`；`E8` 调用者恰好 2 个：
`0x6C07D8` TPlayer.Die、`0x687125` THeroAct.Die ⇒ **只对玩家和英雄生效**）

```
7413F6  FIGHT [Envir+0x5D]        -> A
7413FC  FIGHT3[Envir+0x5E]        -> A
741405  call sub_76858C InSafeZone / 74140C je 0x74142C   ; 不在安全区 -> B(掉)
A 0x74140E: ONLYDROPSPEC[+0x76] -> B ; 否则 LIMITBAGITEMDROP[+0x77] 为 0 -> C(空) ; 否则 -> B
B 0x74142C: OLDSKY[+0x8C] -> C
            ONLYDROPSPEC     -> sub_740300  (独占)
            LIMITBAGITEMDROP -> sub_748D48  (独占)
            否则             -> sub_73FC70(装备) ; sub_740078(背包)
C: [vmt+0x21C]；THumanKind/THeroAct = sub_741620 = `55 8B EC 5D C2 08 00` 空桩，
   TPlayer/TGdMsgGMAgent = sub_6EB8CC 尾调它 —— 净效果都是什么也不掉
```
之后 0x7414DB `[self+0x37C] := 0`，0x741514 `[vmt+0xD8]` 发 `dx=0x2725`(10021)。
**整条链没有金币。玩家死亡原生不掉金币** —— C# `boDieDropGold → ScatterGolds(null)`
在原生无对应，默认 `false` 所以线上不触发，标 `INVENTED(config-gated)`，建议屏蔽而非删除。

### 3.2 装备 worker `sub_73FC70`（容器 `[self+0x4C0]`）

```
73FCA9  A1 AC 5F 7D 00     mov eax,[0x7D5FAC] / 8B 00 mov eax,[eax]   ; 200
73FCB0  3B 86 60 01 00 00  cmp eax,[esi+0x160]
73FCB6  7D 09              jge 0x73FCC1        ; 阈值 >= PK -> 非红名
73FCB8  C7 45 F8 15 00 00 00  denom := 0x15 = 21          ; 红名（严格 PK > 200）
73FCC1  8B 86 8C 01 00 00  mov eax,[esi+0x18C] / 83 C0 5A add eax,0x5A
                              denom := [+0x18C] + 90       ; 非红名
73FCEF  LastHiter is THumanKind([0x73BBE8]) -> denom -= [LastHiter+0x579]
73FD0B  denom < 0 -> denom := 0
73FD29  ebx := 0；循环 ebx = 0..15（0x73FF70 `cmp ebx,0x10`）
73FD33    item := sub_75EC20([+0x4C0], ebx)
73FD3C    item = nil -> 下一格（**不抽签**）
73FD49    StdItem[+2] & 8  -> 直接移除（sub_75F27C），cnt++，**不抽签、不检查上限**
73FD96    eax := denom
73FD99    E8 AE 3D CC FF  call sub_403B4C      ; ← 唯一一次抽签，Random(denom)
73FD9E    结果 != 0 且 item.[+0xFC] == 0 -> 下一格
73FDAF    self.race != 0 -> 落地支 0x73FECE
          --- 玩家销毁支 ---
73FDBC    sub_617A38([[0x7D6534]], self, cl=4) 为真 且 item.[+0xD8] == 0 -> 落地支
73FDE0    StdItem[+2] & 0x10 为 0 -> 下一格
73FDF7    sub_78389C(item, 5, self.[+0x4B7]) != 0 -> 下一格
73FEBD    日志 dx=0x5E(94)；73FEC4 sub_404690 = Free（销毁）；cnt++
          --- 落地支 0x73FECE ---
73FED1    StdItem[+2] & 0x10 非 0 -> 下一格
73FEFE    sub_7688A0(self, item, range=2, 名字=LastHiter名, 0, 0, 1)
73FF11    sub_75F3E8([+0x4C0], ebx, 0) 清格
73FF66    cnt++
73FF69    83 7D F4 02  cmp [ebp-0xC],2 / 7F 0A jg  -> **跳出循环**
73FF79  cnt>0 -> 发包 cx=0x27A4 (10148)
73FFA3  [ebp-1] -> [vmt+0x1CC]（重算属性）
```

要点：**每格至多一次抽签，且只有「非空 + 无 `Reserved02&0x0008`」的格子才抽**；
**落地件数上限恒为 3**（`cmp …,2 / jg`），眼神只改那个 imm8。

### 3.3 背包 worker `sub_740078`（容器 `[self+0x508]`）

```
7400B1  eax := [[0x7D5FAC]]
7400B8  3B 86 60 01 00 00  cmp eax,[esi+0x160]
7400BE  0F 9E 45 FF        setle [ebp-1]        ; 红名 = 阈值 <= PK，即 PK >= 200
                                                ; ★ 与装备 worker 的严格 > 200 **不同**，
                                                ;   这是原生自身的不一致，不许统一
7400C7  edi := [+0x508].Count - 1；**自后向前**遍历
7400E9  item.[+0xFC] != 0 -> 跳到 0x740140（不抽签、不过三道门）
7400F2  红名 -> 跳过抽签
7400F8  B8 03 00 00 00     mov eax,3
7400FD  E8 4A 3A CC FF     call sub_403B4C      ; Random(3)，**分母硬编码 3**
740102  != 0 -> 本件不掉
74010A  StdItem[+2] & 0x10 -> 本件不动
740117  StdItem[+3] & 0x02 -> 本件不动          ; = Reserved02 & 0x0200
740124  sub_784720(item) 且 sub_784710(item)==1 -> 本件不动   ; 绑定
740140  分流：self.race != 0 -> 落地 0x740225
        玩家：sub_617A38(...,4) 且 item.[+0xD8]==0 -> 落地
              sub_78389C(item,5,self.[+0x4B7]) != 0 -> 本件不动
              → 从背包移除、日志 dx=0x5E(94)、sub_404690 销毁
740225  push 1 / push 0 / push 0 / **push 0（归属人 = nil）** / ecx=2 / call sub_7688A0
740266  cnt>0 -> 发包 cx=0x27A4 (10148)
```

**红名语义 = 跳过抽签，即每件都掉**，与 C# `boDieRedScatterBagAll && PKLevel() >= 2` 等价。

### 3.4 落地与归属 `sub_7688A0` → `[item.vmt+0x2C]` = `sub_7839E8`

```
7688F8  [ebp+0x14]==0 时才跑 sub_78389C(item,5,[ebp+0x10]) 过滤
76891E  sub_768688 求落点（range = ecx）
768934  [Envir.vmt+0x28] 加入地图格
76894C  [item.vmt+0x2C](x, y, self, [ebp+0xC])
783A40  call GetTickCount / 783A45 mov [item+8],eax        ; 落地 tick
783A48  mov byte [item+0xC],1
783A4C  [item+0xF0] := x ; [item+0xF2] := y
783A62  89 B3 F4 00 00 00  mov [item+0xF4],esi              ; ← 归属人 = 第二个栈参
```
**两个 worker 传的第二个栈参都是 `0`**（`0x73FEDF 6A 00`、`0x740229 6A 00`）
⇒ **玩家死亡爆出的东西原生没有归属人，落地即公共**。传给 `sub_7688A0` 的
`LastHiter` 名字只进日志（0x768998-0x7689B8）。

过期：`sub_783988`（唯一调用点 `0x77A476`，地图格 tick）
```
78399D  81 FA C0 D4 01 00  cmp edx,0x1D4C0      ; now - [item+8] vs 120000
7839A3  76 12              jbe                  ; <= 保留；**严格大于**才清
7839A5/7839AD              [item+0xF4] := 0 ; [item+0xF8] := 0
7839C1  80 7A 73 00        cmp byte [owner+0x73],0   ; **m_boGhost**
7839C7                     清 [item+0xF4]（[item+0xF8] 同段）
```
⇒ **归属最长 120 秒；归属人变成幽灵后立刻放开。两个归属槽。**

### 3.5 概率链一览（每次抽签的 VA 与参数）

| 顺序 | VA | 调用 | 参数 | 何时抽 |
|---|---|---|---|---|
| 装备-每格 | `0x73FD99` | `sub_403B4C` | `denom` = 红名 21 / 非红 `[+0x18C]+90`，再减 `[LastHiter+0x579]`，下钳 0 | 仅「非空 + 无 `Reserved02&8`」的装备格 |
| 背包-每件 | `0x7400FD` | `sub_403B4C` | **3**（硬编码） | 仅「非红名 + `[+0xFC]==0`」的物品 |
| 武器解锁 | `0x6C115D` | `sub_403B4C` | **5** | 谋杀结算里，受害者 PK < 100 时 |

`Random(0)` 在 Delphi 返回 0 ⇒ `denom` 被钳到 0 时装备**必掉**。这是真实可达路径
（`[LastHiter+0x579]` 最大 0xA，`[+0x18C]` 可为 0 ⇒ 非红最小 80；红名 21−10=11，仍 >0），
目前不可达，记录备查。

---

## 4. 本次改了什么

| 编号 | 文件 | 改动 | 证据 |
|---|---|---|---|
| PKD-08 | `GameSvr/Actors/TBaseObject.NativeProperTargetGate.cs`（新）+ `TBaseObject.cs` | `IsProperTarget` 首句补 `sub_767498` 的九道门 | 0x7674A1-0x7674F1 |
| PKD-09 | `GameSvr/Actors/TBaseObject.cs` | 宠物不打「主人的英雄」与「主人本人」 | 0x76736F-0x767384 |
| PKD-11 | `GameSvr/Players/TPlayObject.Base.cs`、`GameSvr/RobotPlay/RobotPlayObject.Base.cs` | 地面物归属作废判据 `m_boDeath` → `m_boGhost` | `sub_783988` 0x7839C1 / 0x7839D9 |
| 审计 | `AuditTools/NativeProperTargetGateCheck/`（新） | A 段行为驱动 11 条（含阳性对照与 239/242 反向锁），B 段源码契约 6 条 | 每条带 EA |
| 审计 | `AuditTools/NativeFloorItemOwnerExpiryCheck/`（新） | 120000 / 严格大于 / `m_boGhost` 主断言 + `m_boDeath` 反向锁 | 每条带 EA |

同一工作树里另有 PKD-01..07（装备上限 3、红名判据严格 >200、抽签序列对齐、背包抽签
先于销毁分流、FREEPK 门、幽灵/自杀否决、幸运扣减上提）已由并行落地并提交为
`8d287832`。本报告对这七条做了独立字节复核，**全部与我自己反出来的结果一致**。

---

## 5. BLOCKED

| 项 | 缺什么 |
|---|---|
| 装备掉落分母 21 / `[+0x18C]+90` | `[self+0x18C]`（= `sub_73D500` @0x73DAC5 的 `aggregate(+0x5E)/10`）与 `[LastHiter+0x579]` 都不在已公布 RTTI 里，C# 无对应字段。填 0 等于凭空规定「非红分母 90」 |
| `sub_740300` / `sub_748D48` 两个独占 worker | 前者按 Delphi 类 `TSpecialDropItem`（`[0x783434]`）筛选并按 `[item+0x100]` 百分比抽；后者按每图配额记录 `sub_784568+sub_77C028`。C# 两套都没有，当前 fail-closed 成「不掉」 |
| `[+0x3A8]` 阵营梯 | 字段身份未定（RTTI 无、写入点未查）。涉及 `sub_7671F0` 与 `sub_6C13C4` 两处 |
| `[+0x1829]`（攻击模式强制 / IsProtectTarget 直接放行） | 全镜像只有 `0x6C14C0` 与 `0x6C176D` 两处读，无写入点线索 |
| `[+0x4C4]` / `[+0x714]` 免战双字段 | setter `sub_6B6B78` 会把 `[+0x714]` 算成派生值并刷名字颜色，取反关系没吃透；C# 只有一个 `m_boInFreePKArea`，翻边前必须先定性（§4.18 的双权威问题） |
| 荣耀（`[+0x4D0]/[+0x4D8]/[+0x4DC]`） | 子系统级缺失，且没有持久化路径。`sub_6D2928`（死亡 −1，−99.0 下限）/ `sub_6D29A4`（PK +30）/ `sub_6D27C0`（加，1000 上限）字节谱已在 `staging/deathpk_fix_20260804.md` |
| `MyPKpoint` 的持久化三条路径 | 本次没查存档 codec 的读 / 写 / 编码，不能断言 `FAITHFUL` |
| `sub_617A38(…, cl=4)` / `item.[+0xD8]` / `item.[+0xFC]` | 认证态与「必爆类」两个开关，C# 已有 `NativeItemDropDestroy` 建模但 `[+0xFC]` 仍空缺 |
| `GuardUnit.IsProperTarget` 绕过九道门 | 原生卫士覆写的是内层 `[vmt+0x20]`（`sub_6846B0`）。改法是把 C# 的覆写下移到 `IsAttackTarget`，属卫士 AI 重构 |

---

## 6. 复核中发现的前人错误

1. **`deathpk_fix_20260804.md` 说「地面物归属过期这里 C# 已经 FAITHFUL」是错的** ——
   当前树两处读的都是 `m_boDeath`，原生 `0x7839C1` 读的是 `[owner+0x73]` = `m_boGhost`。已修（PKD-11）。
2. **`discovery_pkdeath_20260803.md` 第 41 行要求删掉 `DecPKPoint` 的 `nC > 0`** ——
   `dec ebx / sub ebx,2 / jae` 是 `x in [1..2]`，`dec` 不改 CF，`dec 0` 得 `0xFFFFFFFF` 不小于 2，
   所以 oldLevel 0 **不**刷色。C# 现状是对的。（`deathpk_fix` 第二遍已推翻，此处再次确认。）
3. **`discovery_pkdeath_20260803.md` 第 11 行把 `sub_403580` 叫「Round」** —— 它是
   `or word [esp+2],0x0F00` 设 RC=向零舍入的**截断**；`sub_403574` 才是银行家舍入。
4. **玩家死亡不掉金币这件事此前没人写过**。`sub_741368` 的三条出口全在 0x741498 汇合，
   之后只有 `[+0x37C] := 0` 和一个 10021 包，`TPlayer.Die` 里也没有金币调用。
   C# 的 `boDieDropGold` 分支在原生无对应（默认 false，线上不触发）。
5. **装备 worker 与背包 worker 的红名判据在原生就不一致**（`jge` 严格 > 200 vs
   `setle` >= 200）。任何「统一红名判据」的重构都会引入偏差。
6. `sub_767498` 的种族拒绝集是 **{240, 241} 两个值**，不是「>= 240」。
   `add al,0x10 / sub al,2 / jae` 读成范围下限会把 242 以上全部误拒。
