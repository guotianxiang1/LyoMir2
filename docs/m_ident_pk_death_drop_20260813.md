# PK / 善恶值 / 死亡惩罚 / 爆装 / 复活 —— 逐字节对账（2026-08-13）

镜像 `D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`。
反汇编脚本 `staging/_pkd/m2dis.py`（capstone 5.0.7），本轮所有结论均自行重读字节，
未直接采信 `staging/discovery_pkdeath_20260803.md` / `staging/deathpk_fix_20260804.md`。

---

## 一、善恶值（PKPoint）规则表

字段：`MyPKpoint = [player+0x160]`（int，RTTI 名 `MyPKpoint`）。`PKLevel() = PK / 100`
（有符号 `idiv 0x64`，`0x73F4D1` / `0x6CCB21` / `0x6CCB48`）。

| 项 | 原生 VA + 字节 | 数值 / 方向 |
|---|---|---|
| 全局阈值 | `[0x7D5FAC] -> 0x7DCF00`，该处 dword = `C8 00 00 00` | **200** |
| 杀人加点 | `[0x7D5AE8] -> 0x7DCF04` = `64 00 00 00`；`0x6C10FE E8 B9 E3 07 00 call sub_73F4BC` | **+100** |
| 增点 `IncPkPoint` | `sub_73F4BC`：`0x73F4D5 add [ecx+0x160],esi`；`0x73F4E9 cmp eax,ebx / je`；`0x73F4ED 83 F8 02 cmp eax,2 / 7F jg` 跳过 | 新等级 `<= 2` 才发颜色刷新 |
| 减点 `DecPKPoint` | `sub_6CCB0C`：`0x6CCB25 sub [ecx+0x160],esi`；`0x6CCB2B cmp …,0 / 7D jge` 下限 0；`0x6CCB4E 4B dec ebx / 0x6CCB4F 83 EB 02 sub ebx,2 / 0x6CCB52 73 jae` 跳过 | 旧等级 ∈ **{1,2}** 才刷新（`dec` 不动 CF，旧等级 0 → `0xFFFFFFFF` 不小于 2，**不刷新**） |
| 衰减周期 | `0x6B3705 2B 90 34 07 00 00 sub edx,[self+0x734]`；`0x6B370B 81 FA C0 D4 01 00 cmp edx,0x1D4C0`；`0x6B3711 76 25 jbe` 跳过 | 严格 **> 120000 ms**（2 分钟） |
| 衰减量 | `0x6B3722 cmp [self+0x160],0 / 7E jle` 跳过；`0x6B372B BA 01 00 00 00 mov edx,1` | PK **> 0** 时每次 **-1** |
| 衰减时间戳 | `0x6B3719 mov [self+0x734],now` 在 PK>0 判定**之前** | 即使 PK 为 0 也刷新时间戳 |
| 卫士击杀重置 | `0x6C089E cmp byte [LastHiter+0x178],0x70`（种族 112）；`0x6C08AA 81 B8 60 01 00 00 C8 00 00 00 cmp …,0xC8 / 7C jl`；`0x6C08B9 C7 80 60 01 00 00 64 00 00 00` | PK **>= 200** → **赋值** 100（不是减法），且是**裸字段写**，不发 10046 |
| 名字颜色下发 | `sub_767548`：`0x767558 66 BA 3E 27 mov dx,0x273E`（10046），5 个 `6A 00`，`ecx=0`，`0x76755E FF 93 D8 00 00 00 call [vmt+0xD8]` | 走 `+0xD8` 入队广播槽，不是 `+0x250` 单播 |

### 红名/灰名阈值：**原生自己就不一致，不许统一**

| 用途 | VA | 指令 | 判据 |
|---|---|---|---|
| **装备**爆率红名 | `0x73FCB0` | `3B 86 60 01 00 00 cmp eax,[esi+0x160]` / `0x73FCB6 7D 09 jge` | **PK > 200**（严格） |
| **背包**爆率红名 | `0x7400B8` | `3B 86 60 01 00 00 cmp eax,[esi+0x160]` / `0x7400BE 0F 9E 45 FF setle` | **PK >= 200** |
| 谋杀惩罚入口（受害者） | `0x6C0863` | `3B 02 cmp eax,[edx]` / `7F 2A jg` 跳过 | 受害者 **PK <= 200** 才惩罚凶手 |
| `sub_6C0FE4` 内重复门 | `0x6C100D` | `3B 86 60 01 00 00` / `0x6C1013 0F 8C jl` 返回 | 同上 |
| 卫士重置 | `0x6C08B4` | `7C jl` | PK **>= 200** |
| 武器解锁抽签门 | `0x6C114F` | `83 BE 60 01 00 00 64 cmp [esi+0x160],0x64` / `7D jge` 跳过 | 受害者 PK **< 100** |

两个 worker 差**一点**（200 这一点上装备算非红、背包算红），这不是笔误而是原生现状。

---

## 二、PK 判定门清单（谁能打谁）

### 门 0：`sub_767498` —— 通用前置梯（全镜像 **169** 个 `E8` 直调，非虚函数）

`eax=self(edi)`, `edx=target(esi)`：

| VA | 字节 | 门 |
|---|---|---|
| `0x7674A1` | `85 F6 / 74 4E` | target = nil |
| `0x7674A7` | `E8 FC B8 00 00 call sub_772DA8`（`8A 40 74 C3` = 取 `[+0x74]`） | target `m_boDeath` |
| `0x7674B0` | `80 7E 73 00 / 75 3D` | target `m_boGhost`（`+0x73`） |
| `0x7674B6` | `3B FE / 74 39` | self == target |
| `0x7674BA` | `80 BE E0 02 00 00 00 / 75 30` | target `m_boAdminMode` |
| `0x7674C3` | `80 BE E5 02 00 00 00 / 75 27` | target `m_boStoneMode` |
| `0x7674CC` | `8B 86 28 01 00 00 / 3B 87 28 01 00 00 / 75 19` | 不同地图 |
| `0x7674DA` | `B2 34 / call sub_772960 / 75 0C` | target 状态 **52** |
| `0x7674E7` | `8A 86 78 01 00 00 / 04 10 add al,0x10 / 2C 02 sub al,2 / 73 04 jae` | 种族 ∈ {240,241} |
| `0x7674FD` | `FF 51 20 call [vmt+0x20]` | 交给 `IsAttackTarget` |

### 门 1：`sub_6C13C4` = `TPlayer.IsAttackTarget`（`[vmt+0x20]`，0 个 `E8` 直调 = 虚）

`eax=esi=self`, `edx=[ebp-4]=target`：

```
6C13EA  call [target.vmt+0xF0]            -> TRUE 则整体 FALSE
6C13FB  target is [0x67F750]?             -> 是则 bl = NOT sub_683204(target, self.guild)
6C142A  edi = [target.vmt+0xB4]           ; 责任玩家
        edi == nil:
6C1439    cmp byte [target+0x178],0x32 / jb  -> 种族 < 50 直接 FALSE
6C1446    阵营 [self+0x3A8] / [target+0x3A8]：任一 == -1 -> TRUE；相同 -> FALSE；不同 -> TRUE
        edi != nil:
6C147C    阵营同上，但拿 [edi+0x3A8] 比
6C14A5    self == edi -> bl = ([self+0xAED] == 0)      ; 只有 ALL 模式能打自己的宠物
6C14B8    bl = 1；al = [self+0xAED]（攻击模式）
6C14C0    cmp byte [self+0x1829],3 / jne -> al = 1     ; 作弊惩罚 3 级强制「和平」
6C14D0    cmp eax,7 / ja 0x6C171F
6C14D9    jmp [eax*4 + 0x6C14E0]
```

跳表 `0x6C14E0`（原始字节 `1F 17 6C 00 | 00 15 6C 00 | 07 15 6C 00 | 29 15 6C 00 | B2 15 6C 00 | 8E 15 6C 00 | 19 17 6C 00 | 1D 17 6C 00`）：

| idx | 目标 | 语义 | 关键字节 |
|---|---|---|---|
| 0 | `0x6C171F` | **全体**：bl 保持 1 | — |
| 1 | `0x6C1500` | **和平**：`33 DB` FALSE | — |
| 2 | `0x6C1507` | **编组**：`sub_6B7B8C(self, [target+0x106])` 取反 | `0x6C1521 80 F3 01 xor bl,1` |
| 3 | `0x6C1529` | **行会**：同会 FALSE；`[self+0x714]` 非 0 则止；`sub_706B78` 联盟 FALSE | `0x6C154C 33 DB`, `0x6C1587 33 DB` |
| 4 | `0x6C15B2` | **仇敌**：夫妻名 → 战队 `[+0xAE8]` → 组 `[+0xA80]` → 行会 全部排除，再走红名判据 | `0x6C1710 0F 9D C3 setge bl`（target PK >= 200） |
| 5 | `0x6C158E` | **战队**：`[self+0xAE8]` 为空则 TRUE；否则 `0F 95 C3 setne bl` | `0x6C15AA` |
| 6 | `0x6C1719` | `33 DB` FALSE | — |
| 7 | `0x6C171D` | `B3 01` TRUE | — |

模式 4（仇敌）的例外放行门，按顺序：
`0x6C164E sub_6FA2E8(target)` → TRUE；
`0x6C165E [[0x7D6214]+0x29]` 攻城战开启 且 `0x6C1685 sub_659FD4(castleMgr, self.Envir, x, y)` → TRUE；
`0x6C1695 cmp byte [target+0x4B9],0 / jne` → TRUE（target 是 PK 标记者）；
`0x6C16A2 [self+0x714] == 0` 且双方都有行会 且 `sub_706B3C` 为假 → FALSE；
兜底 `0x6C1702 setge` target PK >= 200。

**`0x6C1723 8B 4D FC / 8B D7 / 8B C6 / E8 2D 00 00 00 call sub_6C175C`** —— 原生把
`IsProtectTarget` 调用**放在 IsAttackTarget 内部**，C# 放在 `IsProperTarget` 里。

### 门 2：`sub_6C175C` = `IsProtectTarget`（`eax=self(ebx)`, `edx=edi=责任玩家`, `ecx=esi=原始目标`）

```
6C176D  80 BB 29 18 00 00 03 cmp byte [self+0x1829],3 / 74 je   -> 受保护(FALSE)
6C1776  6A 0A push 0xA / call sub_7684DC(self, x, y, 10)        -> self 在安全区 -> FALSE
6C178F  6A 0A push 0xA / call sub_7684DC(esi, …, 10)            -> 目标在安全区 -> FALSE
6C17B1  80 BB C4 04 00 00 00 cmp byte [self+0x4C4],0 / 75 72 jne -> 跳过等级保护梯
        梯一（红名欺负新手）:
6C17C7    jg  阈值 > self.PK        -> 下一梯
6C17D1    jbe self.Level <= 20      -> 下一梯
6C17DB    ja  target.Level > 20     -> 下一梯
6C17EB    jge target.PK >= 阈值     -> 下一梯
6C17ED    受保护(FALSE)
        梯二（新手打红名）:
6C17FB    ja  self.Level > 20       -> 跳过
6C180A    jle 阈值 <= self.PK       -> 跳过
6C181A    jl  target.PK < 阈值      -> 跳过
6C1824    jbe target.Level <= 20    -> 跳过
6C1826    受保护(FALSE)
6C182C  now - [self+0x378] < 0xBB8(3000) -> FALSE
6C1841  now - [esi+0x378] < 0xBB8        -> FALSE
```

两条等级梯的门槛都是**立即数 `0x14` = 20**（`0x6C17C9` / `0x6C17D3` / `0x6C17F3` / `0x6C181C`
全是 `66 83 B? 78 02 00 00 14`），而且**只有一个**等级常量，不是两个。

### 地图旗标

| 旗标 | 解析 VA | 偏移 | 消费 |
|---|---|---|---|
| `SAFE` | `sub_774D98` | `[flag+0x5C]` | `sub_7684DC`/`sub_76858C` 第一腿 |
| `FIGHT` | `sub_774D98` | `[flag+0x5D]` | 爆装梯 `0x7413F6`；幸运/荣耀 `0x6C07F4`；谋杀门 `0x6C083F` |
| `FIGHT3` | `sub_774D98` | `[flag+0x5E]` | `0x7413FC` / `0x6C07FA` / `0x6C084E` |
| `FREEPK` | `sub_774D98` | `[flag+0x5F]` | **只有一处**：谋杀门 `0x6C0830 80 78 5F 00 / 75 5B` |
| `ONLYDROPSPEC` | `0x775AC7` | `[flag+0x76]` | 爆装梯 `0x741417` / `0x74143E` |
| `LIMITBAGITEMDROP` | `0x775AFB` | `[flag+0x77]` | 爆装梯 `0x741426` / `0x74144E` |
| `OLDSKY/NEWSKY/MULSKY` | `0x774FCE/0x775003/0x775033` | `[flag+0x8C]` 三态 | 爆装梯 `0x741435` |
| **`NOC2C`** | `0x7756E7 B9 05 / BA A4 5E 77 00`，`0x7756F7 4F dec edi / 0F 94 C0 sete al / 88 83 82 00 00 00` | `[flag+0x82]` | **全镜像唯一消费点** `0x6F0A3F`，在 `sub_6F0A24` 里，与 PK 无关 |
| **`GuildPK`** | **第二套解析器** `0x776969 B9 07 / BA F0 6E 77 00`（长串 `0x776EF0` 长度前缀 7） | 不写 flag 字节，走 `0x7769A7 call sub_77D078` | 见下 |

---

## 三、死亡处理链完整顺序

`TPlayer.Die = sub_6C07A0`（`[vmt+0x84]`，`TPlayer VMT@0x6AC8C8`）：

```
6C07BF  B2 33 mov dl,0x33 / call sub_772960     ; 状态 51 活跃?
6C07CD    -> call sub_6EE458                    ; 是则先跑这个
6C07D8  E8 8B 0B 08 00 call sub_741368          ; ← THumanKind.Die：**整条爆装链在这里面**
6C07EE  if (!FIGHT[+0x5D] && !FIGHT3[+0x5E]):
6C0808     call sub_6D2928(edx=1)               ; 荣耀 -1
6C0815     call sub_7698BC(edx=0x1F4=+500)      ; 幸运 +1（500 原生单位 /500 截断）
6C081A  ebx = [victim+0x354] (LastHiter)
6C0823  if ebx == nil -> 0x6C0891
6C0830  if FREEPK / 6C083F FIGHT / 6C084E FIGHT3 -> 0x6C0891
6C0863  if victim.PK > 200 -> 0x6C0891
6C086B  killer = [ebx.vmt+0xB4]()               ; 责任玩家
6C0873  if killer == nil -> skip
6C0875  if killer.m_boGhost([+0x73]) -> skip
6C087B  if killer == victim -> skip              ; 自杀
6C0883  victim.LastHiter = killer
6C088C  call sub_6C0FE4(killer, victim)          ; ← 谋杀惩罚
6C0891  if LastHiter.race == 0x70(112) && victim.PK >= 200: victim.PK := 100
6C0926/6C0942  call sub_768BE0(dx=0x13=19)       ; 19 号日志，无凶手时占位符 '#####'
6C0993  if LastHiter.race == 0: call sub_6C0A28  ; PK 击杀播报
```

`THumanKind.Die = sub_741368`（`E8` 直调者恰好 2 个：`0x6C07D8` 与 `0x687125`，即玩家与英雄）：

```
74138A  call sub_76631C                          ; TCreature.Die：0x766323 C6 43 74 01 置 m_boDeath
7413A2  call [vmt+0x214]
7413AB  if LastHiter != nil && !LastHiter.m_boGhost:
7413C2     call sub_73FBF8(victim, LastHiter, cl=1)
7413D8     if LastHiter is THumanKind([0x73BBE8]):
7413E8        call sub_73FBF8(LastHiter, victim, cl=0)
7413F6  ── 爆装策略梯（见下）──
```

**顺序上的硬事实**：爆装（`sub_741368`）在**荣耀/幸运/PK 惩罚之前**跑完并返回，
所以爆装读到的 `MyPKpoint` 是**死亡前的旧值**——谋杀惩罚 `sub_6C0FE4` 对凶手加点，
不影响本次爆装。经验惩罚（`m_dwPKDieLostExp` / `m_nPKDieLostLevel` 那一段）在 C# 里
排在爆装之前，原生对应位置在 `sub_6C0FE4` 之后，属于**已知顺序差异**（见第六节 BLOCKED）。

---

## 四、爆装规则与抽签序列（重点）

### 4.1 策略梯 `sub_741368 @0x7413F6-0x741496`

```
if (FIGHT || FIGHT3 || InSafeZone(sub_76858C))     -> A 仲裁
else                                               -> B 爆
A: if ONLYDROPSPEC        -> B
   else if !LIMITBAGITEMDROP -> C（[vmt+0x21C] 空桩，什么也不掉）
   else                   -> B
B: if OLDSKY/NEWSKY/MULSKY([+0x8C] != 0) -> [vmt+0x21C] 空桩
   else if ONLYDROPSPEC   -> sub_740300 独占
   else if LIMITBAGITEMDROP -> sub_748D48 独占
   else                   -> sub_73FC70(装备) 然后 sub_740078(背包)
```

`[vmt+0x21C]`：`THumanKind/THeroAct` = `sub_741620`（`55 8B EC 5D C2 08 00` 空）；
`TPlayer/TGdMsgGMAgent` = `sub_6EB8CC`（转发两个栈参后 `call sub_741620`）。净效果都是不掉。

### 4.2 装备 worker `sub_73FC70`（容器 `[self+0x4C0]`）

分母 K：
```
73FCA9  eax = [[0x7D5FAC]] = 200
73FCB0  cmp eax,[esi+0x160] / 73FCB6 jge -> 非红
73FCB8  红名: K = 0x15 = 21
73FCC1  非红: K = [esi+0x18C] + 0x5A(90)
73FCEF  若 LastHiter is THumanKind: K -= byte [LastHiter+0x579]
73FD0B  K < 0 -> K = 0
```

**逐格循环 `ebx = 0..15`（`0x73FF70 83 FB 10 cmp ebx,0x10`）**：

| 步 | VA | 动作 | 是否抽签 |
|---|---|---|---|
| 1 | `0x73FD33` | `sub_75EC20(container, ebx)` 取物 | — |
| 2 | `0x73FD3C` | 空格 → 下一格 | **不抽** |
| 3 | `0x73FD49` | `test byte [[item+0x1C]+2],8` | 置位 → 走「死亡爆出消失」销毁支，`0x73FD8C sub_75F27C` 清格，件数 +1，**不抽** |
| 4 | **`0x73FD99`** | **`call sub_403B4C`（`Random(K)`）** | ✅ **每格一次** |
| 5 | `0x73FD9E` | `test eax,eax / je` → 0 通过；非 0 时只有 `[item+0xFC] != 0` 才继续 | — |
| 6 | `0x73FDB6` | `cmp byte [self+0x178],0 / jne` → 非玩家（英雄）直接走落地支 | — |
| 7 | `0x73FDC7` | `sub_617A38([[0x7D6534]], self, cl=4)` 实名认证 | — |
| 8 | `0x73FDD7` | 已认证 且 `[item+0xD8] == 0`（非赠品）→ 落地支；否则销毁支 | — |
| 9a | 销毁支 `0x73FDE0` | 要求 `[std+2] & 0x10` **置位**，否则跳过本格 | — |
| 9b | `0x73FDF7` | `sub_78389C(item, 5, cl=[self+0x4B7])` 非 0 → 跳过 | — |
| 9c | `0x73FEBD/0x73FEC2` | 日志 `dx=0x5E`，`sub_404690` = `TObject.Free`（**不落地**） | — |
| 10a | 落地支 `0x73FED1` | 要求 `[std+2] & 0x10` **清零**，否则跳过 | — |
| 10b | `0x73FEFE` | `sub_7688A0(self, item, ecx=2, "死亡爆出-"+凶手名, …, 末参 1)` | — |
| 10c | `0x73FF11` | `sub_75F3E8(container, ebx, 0)` 清格 | — |
| 10d | **`0x73FF69`** | `83 7D F4 02 cmp [ebp-0xC],2` / `7F 0A jg` → **件数 > 2 时中断整个循环** | — |
| 11 | `0x73FF96` | 件数 > 0 时发 `cx=0x27A4`（10148）带 MakeIndex 数组 | — |

**装备一次死亡最多落地 3 件**；销毁支不受这个上限约束但同样占用件数计数（`0x73FD74` / `0x73FE4E`）。

### 4.3 背包 worker `sub_740078`（容器 `[self+0x508]`，**倒序** `Count-1 downto 0`）

```
7400B8  cmp [[0x7D5FAC]], [esi+0x160] / 7400BE 0F 9E setle  ; redName = (PK >= 200)
```

| 步 | VA | 动作 | 是否抽签 |
|---|---|---|---|
| 1 | `0x7400E2` | `sub_424D4C(list, edi)` 取物 | — |
| 2 | `0x7400E9` | `cmp byte [item+0xFC],0 / jne 0x740140` 必爆类 | **不抽**，且跳过第 4-6 步 |
| 3 | `0x7400F2` | `cmp byte [ebp-1],0 / jne 0x74010A` 红名 | **不抽** |
| 4 | **`0x7400FD`** | **`call sub_403B4C`，`eax = 3`（`0x7400F8 B8 03 00 00 00`，硬编码）** | ✅ |
| 5 | `0x740102` | `test eax,eax / jne 0x74025C` 非 0 → 不掉 | — |
| 6a | `0x74010D` | `test byte [std+2],0x10 / jne` → 不掉 | — |
| 6b | `0x74011A` | `test byte [std+3],2 / jne` → 不掉 | — |
| 6c | `0x740126`+`0x740131` | `sub_784720`（`[std+3]&0x40`）且 `sub_784710`（`word [item+0x34]`）`== 1` → 不掉 | — |
| 7 | `0x740147` | `cmp byte [self+0x178],0 / jne 0x740225` 非玩家 → 落地 | — |
| 8 | `0x740158` | `sub_617A38(mgr, self, cl=4)` 认证 | — |
| 9 | `0x740168` | 已认证 且 `[item+0xD8]==0` → 落地；否则销毁 | — |
| 10a | 销毁 `0x74017B` | `sub_78389C(item,5,…)` 非 0 → 整件保留 | — |
| 10b | `0x74019D` | `sub_424B30` 从背包摘除；`0x740217 sub_768BE0(dx=0x5E)`；`0x74021E sub_404690` Free | — |
| 11a | 落地 `0x740236` | `sub_7688A0(self, item, ecx=2, 空串, …, 末参 1)` | — |
| 11b | `0x740254` | `sub_424B30` 摘除 | — |
| 12 | `0x74028E` | 件数 > 0 时发 `cx=0x27A4`（10148） | — |

**背包没有件数上限**（`[ebp-0xD8]` 数组 200 字节 = 50 项，背包上限 48 格，不会溢出）。

### 4.4 一次普通死亡的完整抽签序列

按 `sub_741368 @0x74145E/0x741466` 的调用顺序：

1. **装备**：`for slot = 0..15`，对每个「非空且 `[std+2]&8` 未置位」的格子调用一次
   `Random(K)`；一旦落地件数 > 2 立即中断，后续格子**不再抽**。
2. **背包**：`for i = Count-1 downto 0`，对每个「`[item+0xFC] == 0` 且非红名」的物品
   调用一次 `Random(3)`。红名与必爆类**不消耗抽签**。

`sub_403B4C` = `@RandInt`：`seed = seed*0x8088405 + 1`（`0x403B4F imul edx,[0x7A2008],0x8088405`），
`result = HIGH32(seed * range)`（`0x403B60 F7 E2 mul edx`）。**落点搜索 `sub_768688` 全程无 Random**
（确定性螺旋，`0x7686A0` 起始 `[ebp-0x14]=0x3E7`，`0x76876C cmp …,8 / jge` 回落原点），
所以掉落坐标不参与抽签序列。

### 4.5 绑定物 / 不可爆物判定

`sub_78389C(item, edx=模式, cl=旗标)`：
```
7838AE  sub_784710 -> word [item+0x34] != 0        -> 1（拒绝）
7838BB  test byte [[item+0x1C]+3],8                -> 1
7838C3  sub_784720 -> [std+3] & 0x40               -> 1
7838E2  jmp [edi*4 + 0x7838E9]                     ; 表项 [0]=0x783979 [1]=0x783901
                                                   ;      [2]=0x783911 [3]=0x78392A
                                                   ;      [4]=0x783940 [5]=0x78396B
模式 5（爆装/丢弃）@0x783940:
  [item+0xFC] != 0 || [std+3]&2 || [std+3]&4 || [std+2]&0x80 -> 5（拒绝）
```

`NativeReserved02` = `[std+2]` 的 ushort，所以 `[std+3]&2 == 0x0200`、`&4 == 0x0400`、
`[std+2]&0x80 == 0x0080`、`[std+3]&0x40 == 0x4000`、`[std+3]&8 == 0x0800`。

---

## 五、复活

C# 已有 `GameSvr/Actors/NativeRevivePolicy.cs` + `TBaseObject.NativeRevive.cs`
（`sub_7436F8` 的移植），本轮只做抽查复核，未改动：

| 项 | 原生 VA + 字节 | 值 |
|---|---|---|
| `NoRelive` 门 | `0x743726 80 78 72 00 / 0F 85 8C 02 00 00` | `[flag+0x72]` → 直接返回 FALSE |
| `NOEQUIPRELIVE` | `0x743730 80 78 7F 00 / 0F 85 D1 01 00 00` | 跳过两条道具路径，仍走尾部 |
| 复活戒指 CD | `0x743756 81 FA 60 EA 00 00 cmp edx,0xEA60` | **硬编码 60000 ms**，不是配置 |
| 戒指路径 | `0x743775 [esi+0x2B0] -> [esi+0x2AC]` | HP = MaxHP |
| 附带回蓝 | `0x743781 80 BE B9 01 00 00 00` | `[+0x1B9]` 置位才 MP = MaxMP |
| 复活提示 | `0x7437A3 BA F0 39 74 00` | `'靠戒指的力量，您复活了。'` |
| 第二路径 | `0x7437CB call sub_746084`，`0x7437D8 B2 30` 状态 **48** 在冷却中则只发 CD 文本 | — |
| 分级 CD | `sub_74609C`：1→150 / 2→120 / 3→90 / 4→60 / 默认 300 秒 | `[+0x1DD]` |
| 复活后无敌 | `0x74390F 6A 01 / 0x743911 66 B9 02 00 / 0x743915 B2 37 / 0x74391B call [edi+0x1A8]` | 状态 **55**，值 **2** |
| `AUTORELIVE` | `0x743925 cmp byte [eax+0x7E],0`，然后 `call [Envir.vmt+0x10]`，`0x74393E cmp [esi+0x2AC],0 / setg bl` | — |
| `RELIVEBACK` | `0x74394C cmp byte [eax+0x7D],0`；`0x743958 cmp byte [esi+0x178],0`（必须是玩家）；`0x743961 B8 05 00 00 00 / call sub_403B4C` | **`Random(5) + 坐标 - 2`**，x/y 各抽一次 |

**复活链上有两次 `Random(5)`**（`RELIVEBACK` 的 x 与 y 抖动），发生在死亡链之外的重生 tick 里。

---

## 六、判定汇总

### DIVERGENT（本轮已修，共 8 条）

| ID | C# 位置 | 原生 | 修法 |
|---|---|---|---|
| PKD-01 | `TPlayObject.Message.cs:2990` `PKLevel() > 2` | `0x73FCB6 jge` = PK **> 200** | 改 `m_nPkPoint > nPKPunishPoint` |
| PKD-02 | 同上 `if (deathDropPatched) { … break; }` | `0x73FF69/0x73FF6D` 无条件上限 | 上限恒生效，眼神只改 imm8 |
| PKD-03 | 同上 第二个循环首句就 `Random(nRate)` | `0x73FD3C je` 在 `0x73FD99 call` 之前 | 空格/已清格不抽签 |
| PKD-04 | `TPlayObject.Base.cs:2173` `ShouldDestroy` 在抽签前 | `0x7400FD Random(3)` 在 `0x740140` 分流前 | 抽签与三道门前置 |
| PKD-05 | `TBaseObject.Base.cs:1014` 缺 FREEPK；`PKLevel() < 2` | `0x6C0830`；`0x6C0863 jg` = PK **<= 200** | 补 `!boFREEPK`，改 `<=` |
| PKD-06 | 同上 缺幽灵/自杀守卫 | `0x6C0875` / `0x6C087B` | 补两道 |
| PKD-07 | 同上 `AddBodyLuck(-1)` 嵌在最内层 | `0x6C1019` 在 guildwarkill 之前 | 上提 |
| PKD-10 | 同上 `tStr = "####"` | `0x6C09FC` 长度前缀 5 | 改 `"#####"` |

> 编号说明：`w/m-ident` 这条分支上有**另一个代理并行提交**（`e2c43355` PKD-08/09
> = `sub_767498` 前置梯 + 宠物不打主人/主人英雄；`87bf17f4` PKD-11 = 地面掉落归属作废
> 判据；`ffb2a9bd` docs/pk_death_drop_chain_20260813.md），与本报告是同一份任务的两次
> 独立执行。撞号已协调好：**PKD-08/09 归对方，PKD-10 归本报告（死亡日志占位符），
> PKD-11 归对方（地面掉落归属）**。两边改动落在不同函数上，已复核无覆盖冲突。

### MISSING

| 项 | 原生证据 | 玩家可见后果 |
|---|---|---|
| **`GuildPK` 地图旗标整套** | 第二套解析器 `0x776969 B9 07 / BA F0 6E 77 00`，长串 `0x776EF0` len=7 `'GuildPK'`；载荷经 `0x77699D sub_4C6964` 按 `0x776F00`/`0x776F0C` 一对分隔符切出后交 `0x7769A7 sub_77D078`；该函数自带两条报错串 `0x77D2BD '[Error]: GuildPk字符串解析错误'`、`0x77D535 '[Error]: GuildPk 时间字符串解析错误'` | C# 全库 `guildpk` **0 命中**。运营 `mapinfo.txt` 里写的 `GUILDPK{...}` 段在 C# 上完全无效 |
| 装备 worker 的真实分母 21 / (`[+0x18C]`+90) | `0x73FCB8` / `0x73FCC7` | 仍用 15/30，见 BLOCKED |
| `[item+0xFC]` 必爆类 | `0x7400E9` / `0x73FDA2` / `0x783940` | 该类物品在 C# 会被抽签挡掉 |
| 荣耀（`[+0x4D0]/[+0x4D8]/[+0x4DC]`） | `sub_6D2928` / `sub_6D29A4` / `sub_6D27C0` | 死亡不扣荣耀、PK 不加荣耀 |
| 受害者等级保护 | `0x6C10E3 movzx eax,word [esi+0x278] / cmp eax,[[0x7D6B8C]] / 7C jl` | **默认无影响**：`[0x7D6B8C] -> 0x7DCEFC` 处的 dword 实测 **0**，等级永远不小于 0 |

### INVENTED（**已全部删除**，2026-08-13 第二轮）

> 裁决结果是删。`REPLICATION_RULES.md` §3.1 已明确：「原版没有、但删掉玩家会当场吃亏的
> 保护性代码属于 INVENTED，要删」。下表六项加上后来发现的基类那一半，逐项零命中取证后删除，
> 每项一个提交。完整字节证据与扫描输出见 **`docs/pk_death_drop_invented_removals_20260813.md`**。

| 项 | C# 位置 | 原生 | 处置 |
|---|---|---|---|
| `boPKLevelProtect` / `nPKProtectLevel` 那整段「新人保护」 | `TBaseObject.Base.cs` `IsProtectTarget` | `sub_6C175C` 只有两条梯，门槛是同一个立即数 `0x14`，没有第二套配置 | 删，`45bdd75f` (PKD-12) |
| `m_boAngryRing` / `m_boNoDropItem` / `Flag.boNODROPITEM` 早退 | `TPlayObject.Base.cs` `ScatterBagItems` | `sub_740078` 开头无任何早退，第一条条件跳转是 `0x7400D4` | 删，`2adfed89` (PKD-13) |
| `m_boAngryRing` / `m_boNoDropUseItem` 早退 | `TPlayObject.Message.cs` `DropUseItems` | `sub_73FC70` 开头无任何早退，第一条条件跳转是 `0x73FCB6` | 删，`91be1220` (PKD-14) |
| 同上，**基类那一半**（英雄与怪物走这条） | `TBaseObject.Base.cs` `DropUseItems` | 同上 | 删，`9f81902b` (PKD-14b) |
| `InDisableTakeOffList` | `TPlayObject.Message.cs` `DropUseItems` | 装备 worker 循环体 `0x73FD29..0x73FF73` 无任何按物品编号查表 | 删，`3f1455f6` (PKD-15) |
| `m_LastHiter.race == RC_NPC` 也算 PK | `TBaseObject.Base.cs` 谋杀门 | `0x6C081A..0x6C0891` 八条比较里没有种族比较；`cmp byte [reg+0x178],0x0A` 全镜像 6 处无一在死亡链内 | 删，`66ca8fda` (PKD-16) |
| `boVentureServer` 门 | `TBaseObject.Base.cs` 谋杀门 | 只有三个地图旗标字节 + 一个阈值，无第四道全局门 | 删（仅此调用点），`06c12f7b` (PKD-17) |

### BLOCKED

| 项 | 缺什么 |
|---|---|
| 装备分母 `[self+0x18C]` | 唯一写入点 `sub_73D500 @0x73DAC5`，值 = `movzx word [edi+0x5E] / 10`，其中 `edi = [esi+0x1B0]` 是 0x1B0 字节的装备加成聚合块。RTTI 里没有这个字段名，C# 无对应载体。照抄 90 会把所有非红玩家的装备爆率从 1/30 改成 1/90 |
| 装备分母减项 `[LastHiter+0x579]` | 全镜像 3 处引用，两个写入点都在 `sub_73D500`（`0x73D578` 写 0、`0x73DECF` 写 0xA，受 `[esi+0x1D5]` 门控），身份不明 |
| `[item+0xFC]` | 由 `sub_74DAE4` 赋值的物品类标记，C# 无字段 |
| `sub_740300`（ONLYDROPSPEC）/ `sub_748D48`（LIMITBAGITEMDROP） | 前者按 Delphi 类 `TSpecialDropItem`（`[0x783434]`）过滤并按 `[item+0x100]` 百分比抽签；后者查每地图配额记录。C# 两者都没有，现状 fail-closed 成「不掉」 |
| 装备销毁支不清槽位 | `0x73FEC2 sub_404690` 之后没有 `sub_75F3E8`，`[self+0x4C0]` 的槽位指针仍指向已释放对象。要么原生就有这个悬垂，要么我漏读了一条清槽路径。不敢照抄 |
| `[std+2] & 8`（「死亡必消失」位）在 C# 读的是 `GoodItem.Reserved`（来自 MySQL `NeedConf` 列）而不是 `NativeReserved02`（type-2 记录 +2 的 ushort） | 两条装载路径是否写同一份数据没有证据。改错会让「死亡必消失」类整体失效或整体误触发 |
| 经验/等级惩罚在链上的位置 | C# 把 `m_dwPKDieLostExp`/`m_nPKDieLostLevel` 放在 `boPK` 块里、爆装之前；`sub_6C0FE4` 里没有对应代码，说明它在别处（可能是 `PKDie`/`sub_73FBF8`）。未定位到原生对应点前不动 |
| `sub_6F0A24` 的调用面 | NOC2C 唯一消费者，12 个调用点全在 `0x6F09D0..0x6F11C8` 一族，函数族身份未定；只能确认**与 PK 目标判定无关** |

### FAITHFUL（复核通过，停止扫描）

- PK 衰减周期/量/严格 `>`（`0x6B370B jbe`）与 C# `TBaseObject.Base.cs:515` 完全一致。
- `IncPkPoint` / `DecPKPoint` 的刷新集合（{≤2} 与 {1,2}）、`PKLevel()` 的有符号 `idiv`。
- `RefNameColor` 的 ident 10046（`0x767558 66 BA 3E 27`）。
- `AddBodyLuck` 的 `[+5,-10]` 钳位（`0x7698E3` / `0x7698F4`）。
- 卫士重置用立即数 200 与赋值 100，且**不发**颜色刷新包。
- 爆装策略梯的 FIGHT/FIGHT3/安全区/ONLYDROPSPEC/LIMITBAGITEMDROP/天空三态六条腿。
- 背包红名判据 `PKLevel() >= 2`（对应 `setle`）与硬编码分母 3。
- 复活链的 60000 ms CD、状态 55 值 2、`RELIVEBACK` 的 `Random(5)-2` 抖动。

---

## 七、对既有材料的订正

1. **`GuildPK` 不是「原生不认识的遗留 token」。** `deathpk_fix_20260804.md` 第 6 节据
   「扫 yanshen2.0.8.dll 0 命中」推断 `GUILDPK` 在原版里也是惰性的。那次扫的是**插件**，
   不是 M2Server。M2Server 里有长度前缀 7 的 Delphi 长串 `'GuildPK'`@`0x776EF0`、
   一个专门的解析入口 `0x77696E` 和一个带两条中文报错的载荷解析器 `sub_77D078`。
   把它当惰性 token 处理会漏掉一整套按时间段生效的行会 PK 配置。
2. **`discovery_pkdeath_20260803.md` 第 17 行「受害者等级保护」被高估。**
   门确实存在（`0x6C10F2 jl`），但阈值 `[0x7D6B8C] -> 0x7DCEFC` 处的 dword **实测为 0**，
   默认配置下永不触发。它是 MISSING 但不是活缺陷。
3. **`discovery_pkdeath` 第 15 行漏了 FREEPK。** 谋杀惩罚的地图门是三个（FREEPK/FIGHT/FIGHT3），
   不是两个；`0x6C0830 80 78 5F 00 / 75 5B` 是第一道。
4. **谋杀惩罚门的方向被写反过一次。** `0x6C0863 jg` 表示「受害者 PK **> 200** 才跳过」，
   即门是 `<= 200`。写成 `PKLevel() < 2`（`< 200`）在 PK 恰为 200 时少判一次。
5. **两个爆装 worker 的红名判据不同，之前的材料都按同一条描述。**
   装备 `jge` = 严格 `> 200`，背包 `setle` = `>= 200`。
6. **装备落地件数上限不是眼神加的。** `0x73FF69` 是原生指令，眼神补丁
   （`0x100B9D3A A2 6C FF 73 00`）只改那个 imm8。C# 把整条上限做成「补丁存在才生效」，
   等于无插件时上限消失。
7. **`sub_403B4C` 的语义再确认**：`imul [0x7A2008],0x8088405 / inc / mul edx / mov eax,edx`，
   即 `Random(n)` 一次调用消耗一步 LCG。`0x403B68` 是它的浮点孪生，不要混用。
8. **`NOC2C` 与 PK 无关。** 唯一消费点 `0x6F0A3F` 在 `sub_6F0A24` 里，
   与目标可攻击性、爆装、善恶值都没有关系；把它列进「PK 门」的清单是错的。
   另注：原生解析是 `NOC2C(1)` 才置位（`0x7756F7 dec edi / sete al`），
   C# `Maps.cs:280` 见 token 即置 true，参数为 0 时行为不同。
