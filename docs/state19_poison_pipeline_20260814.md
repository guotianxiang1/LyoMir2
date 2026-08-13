# STATE-19 / POIS-19 —— 施毒（定时状态施加）管道全图与 1:1 重建

底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`（ImageBase `0x400000`，flat：file_off = VA − 0x400000）。
分支：`w/pois19`（基于 master `69f049b6`）。工具：Python311 + capstone，`tools/pois19_*.py`（本轮新增）。

> **总判定：DIVERGENT（已部分落地）。**
> 上一轮 `docs/state19_rm_poison_faithful_20260814.md` 判 FAITHFUL 的**结论方向对、覆盖面不够**：
> 它只看了「绿/红毒 applier → 10300 → MakePosion」这一条腿，没有 dump VMT+0xC8 的持有者，
> 因此漏掉了两件事 ——
> 1. **`THumanKind` 覆写了 `MakePosion`（VMT+0xC8 = `0x746604`）**，C# 侧完全没有；
> 2. **`MakePosion` 不是毒专用**，它是全引擎定时状态施加口，24 个直接调用点 + 9 个 10300 发送点，
>    覆盖状态 id 17/20..27/29/31（+ 消息带的 30）。C# 的 `MakePosion` 只是它的一张窄脸。

---

## 1. 复核：`8037` 全镜像 0 命中（**确认**），但结论不是"C# 自造"

自建指令级扫描器 `tools/pois19_immscan.py`：对值的 2/4 字节形态做原始匹配，再回退 1..14 字节反汇编，
**只有 capstone 解出的立即数字段恰好覆盖匹配字节时才计一次命中**，故数据/rel32 巧合不会混入。

| 值 | 原始 word 命中 | 原始 dword 命中 | **立即数加载命中** |
|---|---|---|---|
| `0x1F65` = 8037 (`RM_POISON`) | 157 | 0 | **0** |
| `0x283C` = 10300 | 232 | 0 | **12**（其中 10 条在代码段，2 条为数据噪声） |
| `0x278D` = 10125（对照组 `RM_DURACHANGE`） | 189 | 1 | **28** |

对照组证明扫描器可信。**8037 在原生不存在**——这一条前一轮说对了。
但由此推出的「原生直调 `MakePosion`、C# 整段自造」**不成立**：原生同样走一条服务器内部延迟自消息，
只是编号是 `10300`。8037 与 10300 是同一条内部消息的不同编号，永不上线，数值差本身行为不可见。

真正的分歧不在消息号，在**下游**。

---

## 2. `MakePosion` = VMT+0xC8 —— 语义还原

### 2.1 槽位持有者（`tools/pois19_vmt.py slotof C8`，遍历 1346 个 Delphi VMT）

| 实现 | 持有类 |
|---|---|
| **`0x76B3C8`** `TCreature.MakePosion` | `TCreature`(VMT `0x764608`)、`TAnimal`(`0x71D51C`)、`TMonster`(`0x65E030`) 及全部怪物/NPC/守卫子类，另含 `TFieldHero`(`0x606F1C`)、`TShadowHero`(`0x719F78`) |
| **`0x746604`** `THumanKind.MakePosion` **（覆写）** | `THumanKind`(`0x73BC34`)、`TPlayer`(`0x6AC8C8`)、`THeroAct`(`0x685630`)、`TWarHero`(`0x685968`)、`TTaosHero`(`0x685CA0`)、`TMagHero`(`0x685FD8`)、`TSecWarHero`(`0x5F55A8`)、`TSecTaosHero`(`0x5F58E4`)、`TSecMagHero`(`0x5F5C24`)、`TGdMsgGMAgent`(`0x62EF8C`) |

注意 `TFieldHero` / `TShadowHero` **不在**覆写组，它们用基类体。

### 2.2 ABI

Delphi register 约定，`eax`=Self，`dl`=状态 id，`cx`=秒，`[ebp+8]`=值；`ret 4`，无返回值。
栈参按**声明序从左到右压栈**（已用 `AddState` 的 `push value / push flag` 与 `0x7732B6` 对照证实：
先压的落在**高** `[ebp+N]`）。

### 2.3 `TCreature.MakePosion` @`0x76B3C8` 全文

```
76B3CE  8B F9                  mov  edi, ecx           ; cx = 秒
76B3D0  8B DA                  mov  ebx, edx           ; dl = 状态 id
76B3D8  E8 67 88 00 00         call 0x773C44           ; ImmuneCheck(self,id)
76B3DF  75 44                  jne  0x76B425           ;   -> 静默中止
76B3E1  B2 34                  mov  dl, 0x34
76B3E5  E8 76 75 00 00         call 0x772960           ; HasState(52)
76B3EC  75 37                  jne  0x76B425           ;   -> 静默中止
76B3EE  80 FB 12               cmp  bl, 0x12
76B3F1  75 16                  jne  0x76B409
76B3F3  B2 1A / E8 64 75 00 00 HasState(0x1A)
76B3FE  74 09                  je   0x76B409
76B404  E8 C7 00 00 00         call 0x76B4D0           ; RemoveState(0x1A) -> 0x7731C0
76B409  0F B7 45 08 / 50       push movzx word [ebp+8] ; 值
76B40E  6A 00                  push 0                  ; 新节点 flag
76B410  0F B7 C7               movzx eax, di           ; 秒，零扩展
76B413  69 C8 E8 03 00 00      imul ecx, eax, 0x3E8    ; -> 毫秒
76B41F  FF 93 EC 01 00 00      call [ebx+0x1EC]        ; AddState @0x7730D0
```

两道门都是 `AddState` 自身 VMT+0x1E8 门（`0x772F84`）的**真子集**，本身不额外拒绝；复现它们是因为
它们跑在 `0x12→移除 0x1A` **之前**，而那一条门不覆盖。

### 2.4 `THumanKind.MakePosion` @`0x746604` 覆写（**C# 全缺**）

```
74660B  66 89 4D FE            mov  [ebp-2], cx        ; 暂存秒
746613  B2 34 / E8 44 C3 02 00 HasState(0x34)
74661E  75 2F                  jne  0x74664F           ; -> 静默中止
746620  80 FB 1D               cmp  bl, 0x1D           ; 仅状态 29
746623  75 18                  jne  0x74663D
746625  66 8B BE 80 01 00 00   mov  di, word [esi+0x180]
74662C  B8 64 00 00 00         mov  eax, 100
746631  E8 16 D5 CB FF         call 0x403B4C           ; Random(100)
746636  0F B7 D7               movzx edx, di
746639  3B C2                  cmp  eax, edx
74663B  7C 12                  jl   0x74664F           ; 掷点 < 抗性 -> 中止
74664A  E8 79 4D 02 00         call 0x76B3C8           ; 直调基类体（非二次虚派发）
```

**同一掷点在玩家路径上出现两次**：`THumanKind` 还覆写了 `CanAddState`（VMT+0x1E8 = `0x7465D4`），
其嵌套闭包 `0x74659C`（经 `[ebp+8]` 读父帧：`[frame-1]`=id、`[frame-8]`=Self）做完全相同的判定：

```
7465E6  E8 99 C9 02 00         call 0x772F84           ; 继承门
7465ED  74 0B                  je   0x7465FA           ;   false -> false
7465EF  55 / E8 A7 FF FF FF    call 0x74659C
...
7465A6  80 78 FF 1D            cmp  byte [eax-1], 0x1D
7465B2  66 8B B0 80 01 00 00   mov  si, word [eax+0x180]
7465BE  E8 89 D5 CB FF         call 0x403B4C           ; Random(100)
7465C6  3B C2 / 7D 02          cmp eax,edx / jge 通过
7465CA  B3 01                  mov  bl, 1              ; 否则否决
```

即**经 MakePosion 到达的玩家要过两道独立掷点**，有效抗性 = 1−(1−p)²；
直接走 `AddState` 的路径只过一道。

状态 29 = 麻痹：走/跑/转向/坐下门 `0x76B354` 在 `0x76B368` 就以它拒绝。

### 2.5 下游：`AddState` VMT+0x1EC = `0x7730D0`（全 `TCreature` 族**无覆写**）

节点 18(`0x12`) 字节，`0x764E00` 分配 / `0x764E10` 释放，挂在 `Self+0xDC` 单链：

| 偏移 | 宽度 | 含义 |
|---|---|---|
| `+0x00` | byte | flag（`AddState` 的 `[ebp+8]`；MakePosion 恒传 0） |
| `+0x01` | byte | 状态 id |
| `+0x02` | dword | 剩余毫秒 |
| `+0x06` | dword | `GetTickCount()` 戳（`0x7731AB`/`0x7731B3`） |
| `+0x0A` | dword | 值 |
| `+0x0E` | dword | next |

刷新规则：`0x773117 cmp edi,eax / jle` 值更高则改写值+时长；`0x773140 cmp eax,[ebp-4] / jge` 值相等仅延长。
尾部 `0x77318C push 1 / idiv 0x3E8 / call [VMT+0x14]` 广播秒数，然后戳 tick。

状态位图另有一份：`0x772960 HasState` = `bt dword [Self+0x168], id&0x7F`（128 位，`Self+0x168..0x177`），
`0x772974 SetState` = `bts`，`0x7729A8 ClearState` = `btr`。

### 2.6 与已落地 POIS-38 的衔接

`AddState` 建的就是 POIS-38 那条 1000ms 中段块（`0x76B905..0x76BD33`）消费的链：块头
`0x76B908 sub eax,[esi+0x2C] / cmp eax,0x3E8` 限流后，逐档 `0x772960 HasState(id)` +
`0x773BEC GetStateValue(id)` 取值。即 **MakePosion 是写端、POIS-38 是读端，同一 `Self+0xDC` 链**，无第二权威。

---

## 3. 调用点全图

### 3.1 直接调用（24 处 `call [reg+0xC8]`，`tools/pois19_vcall.py C8`，代码段内共 63 处，其余属 VCL/其它类族）

| VA | 状态 id | 秒 | 值 | 触发条件 | 所属 |
|---|---|---|---|---|---|
| `0x666D4E` | `0x1F` 31 | 30 | 1 | `Random([esi+0x26C]+0x14)==0` | `sub_666BD8` |
| `0x6670C3` | `0x1A` 26 | 5 | 0 | `Random([esi+0x26C]+0x14)==0` | `sub_666FC0` |
| `0x66AE2F` | `0x15` 21 | `Random(6)+5` | 1 | 无 | `TArmLightGuard` VMT+0x198 |
| `0x66B86D` | `0x1B` 27 | 3 | 1 | 伤害 > `10*[ebx+0x2AC]` | `sub_66B828` |
| `0x674A19` | `0x1D` 29 | 5 | 1 | `Random(10)<3` | `TQuickKnifeIceMon` VMT+0x22C |
| `0x674D63` | `0x1D` 29 | 3 | 1 | `IsProperTarget` | `sub_674D38` |
| `0x674F23` | `0x1D` 29 | 5 | 1 | `Random(10)==0` | `sub_674E94` |
| `0x675037` | **变量** `[ebp-9]` | 300 | 1 | 复合 | `sub_674F58` |
| `0x675D82` | `0x1D` 29 | `word[ebx+2]` | `word[ebx+8]` | `!0x772DA8`(`[self+0x74]`) | `TKingOfIceMonBB` VMT+0x2E4 |
| `0x676D98` | `0x1A` 26 | 3 | 0 | `Random(10)==0` | `TKingOfBlackFox` VMT+0x22C |
| `0x677E58` | `0x1F` 31 | 10 | `word[ebp-8]` | 无 | `sub_677CB4` |
| `0x6785F1` | `0x1D` 29 | 3 | 1 | switch 臂 1 | `sub_678514` |
| `0x678617` | `0x1F` 31 | 5 | `max(100,[ebx+0x2AC]>>6)` | switch 臂 2 | `sub_678514` |
| `0x67896B` | `0x1A` 26 | 3 | 0 | `Random(5)==0` | `TPanJunLeader` VMT+0x22C |
| `0x678AFE` | `0x1A` 26 | 3 | 0 | `Random(5)==0` | `TEvilMaster` VMT+0x22C |
| `0x678B6E` | `0x1A` 26 | 3 | 0 | `Random(5)==0` | `TEvilMaster` VMT+0x22C |
| `0x680AD8` | `0x1F` 31 | 60 | 3 | `Random(3)!=0` | `TCentipedeKingMon` VMT+0x200 |
| `0x680AEC` | `0x1A` 26 | 5 | 0 | 上式 else 臂 | 同上 |
| `0x6B37F8` | `0x19` 25 | 600 | 0 | `[eax+0xB75]==0` | `TPlayer` VMT+0x88 |
| `0x6EC594` | `0x1A` 26 | 5 | 0 | `0x7743E0` 为真 | `sub_6EC4FC` |
| `0x717D92` | `0x11` 17 | 10 | 1 | `IsProperTarget` | 陷阱 `sub_717D50` |
| `0x717DA5` | `0x18` 24 | 10 | 1 | 同上 | 同上 |
| `0x7691B4` | `0x16` 22 | `0x4C700C(...)` | 0 | `[ebp+8]==0` | `sub_769100` |
| `0x7691D3` | `0x15` 21 | 同上 | 0 | else | 同上 |
| `0x769C7A` | `0x17` 23 | `word[ebp-4]` | 0 | 无 | `sub_769B9C` |
| `0x769DA3` | `0x14` 20 | `word[ebp-4]` | `edi` | `!HasState(...)` | `sub_769D78` |
| `0x76A3F8` | `0x1A` 26 | 5 | 0 | `Random([edi+0x26C]+5)==0` | `sub_769F90` |
| `0x76A428` | `0x1A` 26 | 3 | 0 | `Random([edi+0x26C]+0xF)==0` | 同上 |
| `0x76DEFB` / `0x76DF3E` | `0x1A` 26 | `word[ebx+0x1A4]+5` / `+3` | 0 | `!0x772598`(等级掷点) | `sub_76DE1C` |
| `0x76E1E8` / `0x76E229` | `0x1A` 26 | `word[esi+0x1A4]+5` / `+3` | 0 | 同上 | `sub_76E0B4` |
| `0x76E2F5` / `0x76E336` | `0x1A` 26 | `word[ebx+0x1A4]+5` / `+3` | 0 | 同上 | `sub_76E268` |
| `0x76E351` | `0x1D` 29 | 2 | 0 | `[ebx+0x1DB]!=0` | 同上 |
| `0x76FA36` | `0x14` 20 | `edi` | `0x4C896C(..)&0xFF` | `[ebp-1]!=0` | `sub_76F9E0` |
| `0x770152` | `0x17` 23 | `word[ebp-4]` | 0 | 无 | `sub_770074` |
| `0x766F7F` / `0x766F9A` | 消息 `wParam` | 消息 `nParam1` | 消息 `nParam3` | 10300 接收臂 | `RunMsg` |

### 3.2 延迟消息 10300 发送点（9 处）

`SendDelayMsg` = `0x766060`，`eax`=Self、`edx`=BaseObject、`cx`=wIdent，栈参按序
`wParam, nParam1, nParam2, nParam3, sMsg, dwDelay`。接收臂 `0x766E9F`（`RunMsg` `0x766A7C`
比较链 `0x766B14 sub eax,0x7B` 落点）读 `byte[esi+2]`=wParam→id、`word[esi+4]`=nParam1→秒、
`word[esi+0xC]`=nParam3→值，然后 `call [ebx+0xC8]`。

| VA | wParam(状态) | nParam1(秒) | nParam3(值) | 延迟 ms |
|---|---|---|---|---|
| `0x6097E8` | `0x1F` 31 | 计算值 | `0x4C896C(..)&0xFF` | 1000 |
| `0x609839` | `0x1E` 30 | 计算值 | 同上 | 1000 |
| `0x66B2F5` | `0x1E` 30 | 5 | 3 | **600** |
| `0x66B945` | `0x18` 24 / `0x1E` 30 | 10 / 20 | 50 / 4 | **600** |
| `0x67438D` | `0x1F` 31 | 30 | 3 | **800** |
| `0x67452D` | `0x1E` 30 | 30 | 3 | **800** |
| `0x677094` | `0x11` 17 | 8 | 1 | 1000 |
| `0x76E5DA` | `0x1F` 31 | 计算值 | `VMT+0xCC` 返回 | 1000 |
| `0x76E68E` | `0x1E` 30 | 计算值 | 同上 | 1000 |

**状态 30（红毒）没有任何直接调用点，只经 10300 到达；状态 26（石化）反之，只有直接调用点，
从不经 10300。**

---

## 4. 与 C# 现状的分歧（逐条）

| # | 分歧 | 证据 | 处置 |
|---|---|---|---|
| D1 | **`THumanKind.MakePosion`(`0x746604`) 覆写整体缺失**：玩家/英雄目标缺状态 29 抗性掷点 | `0x746620`..`0x74663B` | **已落地**（见 §5） |
| D2 | **`THumanKind.CanAddState`(`0x7465D4`→`0x74659C`) 覆写缺失**：第二次独立掷点 | `0x7465A6`..`0x7465CA` | **已落地** |
| D3 | C# `MakePosion` 手搭 `AddTimedAbilityInternal`，**绕过 VMT+0xC8 槽本身**，故 D1/D2 对所有毒源不可达 | 原生无任何毒源绕过该槽 | **已落地**（改为经槽） |
| D4 | `DebuffTrapEvent` 用 `ApplyNativeStateSeconds` 直落 `AddState`，绕过槽 | `0x717D92`/`0x717DA5` 是 `call [target.VMT+0xC8]` | **已落地** |
| D5 | `0x12 → RemoveState(0x1A)` 伴随：原生 MakePosion **无条件**执行；C# 只在 `AddTimedAbilityInternal` 的 `abilityChanged` 臂里执行（那份对应 VMT+0x60 `0x77327C` 的 `0x773316` 臂，本身是忠实的） | `0x76B3EE` vs `0x773316` | **已落地**（两份并存＝原生形态） |
| D6 | 抗麻痹数值 `word[Self+0x180]` 无生产者 | `0x73D57F` 清零 / `0x73DA61` 累加 / `0x743E4E` 快照 | **BLOCKED**（§6） |
| D7 | `Khazard.cs:33/47 MakePosion(POISON_DECHEALTH, 35, 2)` 无原生对应：全镜像**没有** `mov cx,0x23`(35) 的立即数加载，24 个直接调用点里也无 (31,35,2) 组合 | `pois19_immscan 23` → 37 处立即数，无一在 `0xC8` 调用点附近 | **未改**（§6） |
| D8 | `RedMonster.cs:24 MakePosion(POISON_DAMAGEARMOR, 30, 1)`：状态 30 在原生**没有直接调用点**，只经 10300；参数组合 (30,30,1) 亦无对应（最接近的 `0x67452D` 是 30/30/**3** 且带 800ms 延迟） | §3.1 无 `mov dl,0x1E`；§3.2 | **未改**（§6） |
| D9 | `MagicManager.cs:1957` 用 `SendDelayMsg(RM_POISON, POISON_STONE, …, 650)` 把石化做成 650ms 延迟消息；原生 10300 从不带状态 26，且无 650(`0x28A`) 延迟 | §3.2 | **未改**（§6） |
| D10 | C# `MakePosion` 尾部的 `m_btGreenPoisoningPoint = nPoint`（对**所有**毒型都写）与 `SysMsg(sYouPoisoned)`：原生 `0x76B3C8`/`0x746604` 都不写字段、不发消息 | 两函数全文见 §2.3/§2.4 | **未改**（§6） |
| D11 | 原生 24 个直接调用点里，C# 只实现了 5 个（`0x666D4E`/`0x6670C3`/`0x680AD8`/`0x680AEC` 及攻击路径复用）；状态 20/21/22/23/25/27 一个都没有 | §3.1 | **未改**（§6，属各怪物/技能子系统） |

**非分歧（复核后排除）**：
- `RM_POISON=8037` vs `10300` 的编号差 —— 内部消息，不序列化，行为不可见。
- `31 - nType` 折算 —— C# 内部消息 wParam 用毒槽 0/1/5，接收侧折算为状态 31/30/26，净落态与原生一致。
- `AddTimedAbilityInternal` / `CanAddNativeTimedAbility` / `ProcessTimedAbilities` 本体 —— 与
  `0x7730D0` / `0x772F84` / `0x773C44` 逐指令对应，本轮未发现偏差。
- `m_wStatusTimeArr` 第二权威 —— 已在 master 删成纯转发视图，前一轮结论有效。

---

## 5. 已落地（`w/pois19`）

| commit | 内容 |
|---|---|
| `d16401df` | `tools/pois19_immscan.py` / `pois19_vmt.py` / `pois19_vcall.py`（另有后续 `pois19_field.py` / `pois19_owner.py`） |
| `b2ec4a66` | 忠实核心：`TBaseObject.NativeMakePosion.cs`（`0x76B3C8` 基体 + `0x746604` / `0x7465D4` 共享体）、`TPlayObject.NativeMakePosion.cs`、`HeroObject.NativeMakePosion.cs`；`CanAddNativeTimedAbility` 拆出非虚 `CanAddNativeTimedAbilityCreature` 供覆写直调 |
| `0101048e` | 调用点切换 1：legacy `MakePosion` 改经 VMT+0xC8 槽（一次覆盖全部既有毒源） |
| `0df81a61` | 调用点切换 2：`DebuffTrapEvent` 的 `0x11`/`0x18` 改经槽 |

新代码全部在独立文件 + partial class；热点文件 `Grobal2.cs` / `TPlayObject.Message.cs` / `UsrEngn.cs` **未动**。
`TBaseObject.cs` 只改了 `MakePosion` 里那一个调用表达式，`TBaseObject.TimedAbility.cs` 只做了方法体外提。

**审计**：`LegacyStatusSlotAuthorityCheck` 107/107 PASS、`NativeState26CompatCheck` PASS、
`PoisonIndexDivergenceCheck` PASS。`TimedAbilityStateGateExactCheck` 与 `NativeSpellApplyCompatCheck`
失败，但在**未改动的 `69f049b6` 上以完全相同的报错与行号失败**（已用临时 worktree 对照），属既有缺陷，本轮未触及。

---

## 6. BLOCKED / 未切换项与卡点

| 项 | 卡点 |
|---|---|
| **D6 抗麻痹数值 `word[Self+0x180]`** | 生产链未建模：`RecalcAbilitys` 在 `0x73D57F` 清零，`0x73DA5A/0x73DA61`（`mov ax,word[edi+0xAC]` / `add word[esi+0x180],ax`）按已装备来源累加，`0x743E4E/0x743E55` 快照进客户端能力记录 `+0x9C`。源属性（item `+0xAC`，即 type-2 StdItem 属性表里的「麻痹抗性」）本仓无字段。故 `NativeParalysisResistPercent` fail-closed 返回 0，两处掷点当前恒不否决——**结构已忠实、数值待接线**。 |
| **D7 Khazard (35,2)** | 需先确定该 C# 类对应哪个原生 race/VMT 才能取到真参数；`35` 这个秒数在全镜像无立即数加载，说明现值是自造，但**无证据支持任何替代值**，按铁律不臆改。 |
| **D8 RedMonster (30,30,1)** | 同上：状态 30 原生只经 10300 到达，改成消息路径需要知道该怪的原生发送点；未定位前不动。 |
| **D9 石化 650ms 延迟消息** | 需先把 `MagicManager.cs:1957` 那条魔法对到原生技能编号，才能判断原生是直调还是别的消息。跨 MagicManager 子系统。 |
| **D10 `m_btGreenPoisoningPoint` / `sYouPoisoned`** | 两者是 legacy 附加物，原生 MakePosion 确实没有；但 `m_btGreenPoisoningPoint` 有下游读者，直接删会连带改动毒 tick 伤害路径（POIS-38 车道）。需与该车道合并处理。 |
| **D11 未实现的 18 个直接调用点** | 分属 `TArmLightGuard`(21)、`TQuickKnifeIceMon`/`TKingOfIceMonBB`(29)、`TKingOfBlackFox`/`TPanJunLeader`/`TEvilMaster`(26)、`TPlayer` VMT+0x88(25) 等各自未建模的怪物/技能子系统，不属本管道范围。§3.1 已给全 VA、参数与触发条件，可直接照表落地。 |

---

## 7. 接线需求

1. **抗麻痹属性链**（解 D6，使 D1/D2 真正生效）：
   - StdItem 增 `+0xAC` 属性（麻痹抗性）；
   - `RecalcAbilitys` 增清零 + 按装备累加，落到一个玩家 word 字段；
   - `TPlayObject` / `HeroObject` 覆写 `NativeParalysisResistPercent` 返回该字段；
   - 客户端能力记录 `+0x9C` 位同步。
2. **子类覆写位**：若后续移植 `TFieldHero`(`0x606F1C`) / `TShadowHero`(`0x719F78`)，它们**不得**继承
   `HeroObject`——原生这两个类用的是 `TCreature` 的 `0x76B3C8` / `0x772F84`，不是 `THumanKind` 覆写。
3. **新怪物落地约定**：怪物侧施加定时状态一律调 `NativeMakePosion(stateId, seconds, point)`，
   不要再调 `ApplyNativeStateSeconds`（那是绕过 VMT+0xC8 槽的后门，只应留给原生确实直调 `AddState`
   的站点，如 `0x7732C3` / `0x773342` / `0x77335B` / `0x5FABC5`）。
