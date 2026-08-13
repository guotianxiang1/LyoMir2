# 四条移动残口收口报告 — MOVE-10 / MOVE-11 / MOVE-31 / MOVE-39

- 来源：`docs/eqv_shard20_20260814.md`（MOVE-10/11）、`docs/eqv_shard21_20260814.md`（MOVE-31/39）
- 工作树 / 分支：`D:\loym2\.claude\wt2\move-misc` / `w/move-misc`（基线 `02a76791` = master）
- 底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`（file off = VA − 0x400000）
- 反汇编：`tools/m2_disasm.py`（capstone 5.0.7 / x86-32），辅以 `%TEMP%` 的 xref / VMT / 跳表扫描脚本
- 构建：`dotnet build GameSvr` → **0 错 14 警**（14 条警告全部为基线既有）

| 条目 | 原分片判定 | 本轮判定 | 处置 |
|---|---|---|---|
| MOVE-10 | MISSING（可达性未证，"跨 4+ 臂触热点"） | **真缺口，已定性且已证可达；作用域实为 2 臂** | 落谓词，接线点上报（禁改 `TPlayObject.Message.cs`） |
| MOVE-11 | MISSING（"依赖未建模的英雄/外观刷新子系统"） | **误判：三个被调子系统本端全已建模** | **已修**（`ClientWalkXY` / `ClientRunXY` 各接一行） |
| MOVE-31 | 良性，仅报 | **判断成立**（并补上了原分片没有的不可达性证明） | 维持不改 |
| MOVE-39 | MISSING/PARTIAL（"英雄跟随"，COSMETIC） | **误判：不是英雄跟随，是双人坐骑同伴跟随；已有现成移植体** | **已修**（虚钩子 + 复用 run3 分支的移植体） |

---

## MOVE-10 — state 0x34 静默丢弃闸

### 1. 原生字节

Ident 跳表 `0x6D8592`（基 ident 3010）实测：

| idx | ident | handler |
|---|---|---|
| 0 | 3010 turn | `0x6D9B65` |
| 1 | 3011 walk | `0x6D9BD0` |
| 2 | 3012 pose | `0x6D9C7D` |
| 3 | 3013 run | `0x6D9CE4` |

四个移动 handler 中**只有 walk 与 run** 带这道闸：

```
walk 0x6D9BD0  B2 34              mov  dl,0x34
     0x6D9BD5  E8 86 8D 09 00     call 0x772960      ; InBodyState
     0x6D9BDC  0F 85 4A 20 00 00  jne  0x6DBC2C
run  0x6D9CEC  B2 34              mov  dl,0x34
     0x6D9CF1  E8 6A 8C 09 00     call 0x772960
     0x6D9CF8  0F 85 2E 1F 00 00  jne  0x6DBC2C
```

`0x6DBC2C` 是 Operate 的公共出口：不发 `0x275`(SM_ACT_GOOD)、不发 `0x276`(SM_ACT_FAIL)、不广播、不记 tick。移动路径其余所有拒绝分支都要经 `[vmt+0x250]` 发 `0x276`（`0x6D9C4B` / `0x6D9D67`），所以这确实是唯一的静默通道。

**修正原分片**：turn(3010) 入口是 `0x6D9B65 xor edx,edx / call [ecx+0x40]`，pose(3012) 是 `0x6D9C7D mov dl,1 / call [ecx+0x40]`，两者都没有 0x34 闸。原分片"跨 4+ 臂"的估计偏大，实为 **2 臂**。

### 2. state 0x34 是什么（原分片未定性）

全镜像扫 `mov dl,0x34` 后紧跟调用的站点共 45 处代码内命中，其中：

- **置位唯一点** `0x6EE8AF mov dl,0x34` / `0x6EE8B3 call 0x772974`（`bts [esi+0x168],edx`）
- **清位点** `0x6EEBC2` / `0x6EEBC6 call 0x7729A8`（`btr`）
- 其余全是 `call 0x772960` 读点

置位点所在上下文决定了语义：

```
0x6EE89B  call 0x6C3140
0x6EE8A0  mov  [esi+0x3C0], ebx        ; 写双人坐骑同伴指针
0x6EE8A9  mov  [esi+0x3C4], al         ; 坐骑类型
0x6EE8AF  mov  dl,0x34
0x6EE8B3  call 0x772974                ; ← 置位 0x34
0x6EE8BE  ... call [edi+0x14]          ; 状态变更广播
0x6EE8DC  call 0x6BBEE4                ; 把自己搬到 ebx(驾驶者)的格
```

即 **0x34 = 双人坐骑的乘客态**，0x33 = 驾驶者 / 单人坐骑态。交叉印证：

- `sub_6BBE84`（组队门，`0x6BBE8A`..`0x6BBEB6`）= `(0x33 && [+0x3C0]) || 0x34`
- `sub_6BBEB8`（HIT 门，`0x6BBEBE`..）= `0x33 || 0x34`
- `sub_6E9BAC`（定点石 setter）前置门 `0x6DAE01` 读 0x34

本端早已把它落为 `NativeHorseBlockedState = 52`（`TPlayObject.NativeRun3Horse.cs:8`），并在组队门 / HIT 门 / 定点石门三处用同一常量。

原分片排除的两个替身也复证不成立：`POISON_STONE=5 → bodyState 0x1A(26)`；`m_boCanWalk/m_boCanRun` 是 `boLockWalkAction/boLockRunAction` 登录锁（`TPlayObject.Base.cs:1465`）。

### 3. 可达性（原分片的阻塞点，本轮已证）

`CM_INVITE_HORSE` → `ClientNativeHorseInviteResponse`（`TPlayObject.Message.cs:1646`）→ `TPlayObject.NativeHorsePair.cs:117 SetNativeActiveState(NativeHorseBlockedState)`，随后 `MoveToNativeHorseDriver(driver)` 把乘客钉在驾驶者格上。**state 52 是真实可达的玩家态，不是死码。**

### 4. 净行为偏差

`ClientWalkXY`/`ClientRunXY` 均未测 52 → 乘客能自己走跑离开驾驶者的格子，并收到 `0x275` 成功应答。原生：原地不动 + 一个字节都不回。

### 5. 处置：落谓词，不接线（fail-closed）

忠实的"静默"必须落在派发层。`ClientWalkXY` 返回 false 会让 `TPlayObject.Message.cs:1491` 的 else 臂发 `SendMoveActionFail()`（= `0x276`），返回 true 则发 `0x275` —— **两条都不是静默**，硬塞进 `ClientWalkXY` 就是"天真修复反而偏离原版"。而 `TPlayObject.Message.cs` 本轮禁改。

新增 `GameSvr/Players/TPlayObject.NativeMoveMountGate.cs`：

```csharp
internal bool IsNativeMoveBlockedByPassengerState()
{
    return HasNativeActiveState(NativeHorseBlockedState);
}
```

形状与既有先例 MINE-49 完全一致（`TPlayObject.NativeHitMountGate.cs` 的 `IsNativeHitBlockedByMountState` + `Message.cs:1686 if (谓词) break;`）。注意本闸**只测 52 不测 51**：原生 walk/run 用的是裸 `InBodyState(0x34)`，不是 HIT 那个 `51||52` 的 `sub_6BBEB8`。

**接线点（上报主代理）**：`GameSvr/Players/TPlayObject.Message.cs`

- `case Grobal2.CM_WALK:`（:1480）开头
- `case Grobal2.CM_RUN:`（:1567）开头

各加一行 `if (IsNativeMoveBlockedByPassengerState()) break;`，**不要**加到 `CM_TURN` / `CM_SITDOWN`。

---

## MOVE-11 — gate2 前置钩子 sub_6BCE2C（已修）

### 1. 原分片的判断错在哪

原分片写"`sub_6BCE2C` 发 SM 0x20 外观刷新，依赖未建模的英雄/外观刷新子系统"。反汇编不支持这个描述：`0x20` 来自**相邻的另一个函数** `sub_6BCE54`（`0x6BCECC push 0x20`），与 `sub_6BCE2C` 无关。`sub_6BCE2C` 自身 `0x6BCE2C..0x6BCE52` 单 ret，体内只有三条调用。

### 2. 实际语义

```
0x6BCE37  call 0x6EE128
0x6BCE3E  call 0x6EF5D0
0x6BCE47  call [vmt+0x1D8]
```

| 被调 | 行为 | 对应 C# |
|---|---|---|
| `sub_6EE128` | `word[+0xA24]!=0` → 清 `[+0xA20]`/`[+0xA24]`/`[+0xA26]`，发 ident `0x4D0`(1232)，nParam1=旧 magicId | `CancelNativeChannelMagic()` |
| `sub_6EF5D0` | `byte[+0x18E1]=0`（**无条件**，先于任何判断）；`word[+0xA4C]!=0` → 清 `[+0xA28..+0xA4C]` 共 0x26 字节，发 ident `0x4D2`(1234) | `CancelNativeLocationChannelMagic()` |
| `[vmt+0x1D8]` | TPlayer VMT `0x6AC8C8+0x1D8 = 0x6EE2AC`：`byte[+0x1914]!=0` → 清 `[+0x1914]`/`[+0x1918]`/`[+0x191C]`，发 ident `0xD57`(3415) | `CancelNativeType51PendingForTimedAbility()` |

VMT 定位法：全镜像找值为 `0x741224`（人形 mover）的 dword，减 0x30 得 VMT 基址；TPlayer = `0x6AC8C8`（`+0x250=0x6D7CB0`、`+0xD8=0x6DC590` 与已登记槽一致），THumanKind = `0x73BC34`（同槽 `0x772A98` 是 `ret` 空实现）。

第三项与 `TBaseObject.TimedAbility.cs:117-124` 已有的注释完全对上（那里已经把 `TPlayObject VMT 0x6AC8C8+0x1D8 = 0x6EE2AC` 认成 `CancelNativeType51PendingForTimedAbility`）。

**结论：三个子系统本端全已建模**（`TPlayObject.NativeTimedAbility.cs`），而且已经在 `CM_HERO_POWERUP` 臂按同一顺序连用（`TPlayObject.Message.cs:3011-3013`，对应 `sub_6BCE2C` 的第 8 个调用点 `0x6EE201`）。原分片的"未建模"前提不成立。

### 3. 调用点普查

`sub_6BCE2C` 的直接 xref 共 8 处：`0x6D98DF`（坐骑相关臂）、`0x6D9BEC`（walk 3011）、`0x6D9D08`（run 3013）、`0x6D9ED3`（HIT 族 CASE1）、`0x6D9F7D`（CM_3037 CASE2）、`0x6DA017`（CM_SPELL 3017）、`0x6EC635`、`0x6EE201`（召唤坐骑）。本条只补 walk/run 两臂。

### 4. 位置与净行为

原生位置在 gate3（`0x6D9BF6 call [edx+0xBC]`）、gate4（`0x6D9C07 call [ecx+0x40]`）与走路原语（`0x6D9C1D sub_6BBCD8`）**之前**——被 `0x276` 拒绝的走路也已经取消了通道。run 同构（`0x6D9D12` / `0x6D9D23` / `0x6D9D39`）。

对没有任何挂起的普通玩家，三连的净效果只有 `m_boNativeLocationChannelActive = false` 一条，与原生 `0x6EF5D7 mov byte [edx+0x18E1],0` 的无条件清零逐字对上；其余两条各自有 `magicId==0` / `!pending` 的早退。**没有引入任何新包。**

### 5. 改动

- 新增 `GameSvr/Players/TPlayObject.NativeMoveActionCancel.cs`：`CancelNativeActionChannels()` 归拢三连 + 字节证据。
- `GameSvr/Players/TPlayObject.Attack.cs`：`ClientWalkXY` / `ClientRunXY` 各接一行。

放在 `m_boCanWalk` / `m_boCanRun` 早退**之后**：那两个是 C# 独有的登录锁，原生此路径无对应物；放在其前等于在一个 C# 自创的吞包窗口里凭空造取消。原生序列中排在钩子之前的只有 0x34 闸（MOVE-10，落在派发层），与本处相对次序不冲突。

---

## MOVE-31 — MoveToMovingObject 陈旧节点自愈（复核：原判断成立）

### 1. 原生字节

```
0x7798BE  mov  eax,esi                 ; esi = node[+4] = 格内对象
0x7798C0  call 0x765D64                ; 存活探针
0x7798C5  test al,al
0x7798C7  jne  0x779998                ; 存活 -> 走 VMT+0 阻挡判定
; 不存活：
0x7798CD..0x7798E7   摘链（改前驱 [+0xC] 或表头 cell[8]），bl=1 抑制推进
0x7798E9..0x779980   拼诊断串
0x779991  call 0x79DF74                ; 记日志
0x779996  jmp  0x7799AC                ; 继续扫描（不挡路）
```

`sub_765D64` 三项合取：

```
0x765D6D  cmp byte [esi+0x106],0    ; 名字 ShortString 长度字节 != 0
0x765D76  cmp dword [esi+0x128],0   ; Envir != 0
0x765D85  cmp dword [eax+0x44],0    ; Envir[+0x44] != 0（地图就绪；同一字节在
                                    ;   0x77980D 是 MoveToMovingObject 的整调用前置）
```

### 2. 复核结论：原判断成立，且可以给出比原分片更硬的理由

原分片说的是"托管内存下不存在悬垂对象 + 死/幽灵经 `IsNativeCellBlocking` 天然不挡路"。前半句对但不够，因为探针查的**不是**死/幽灵（那是 `sub_765D94` 的活），而是三项"对象已失效"信号。逐项查本端可达性：

| 原生条件 | C# 对应 | 可达性 |
|---|---|---|
| `[+0x106]==0` 名字为空 | `m_sCharName` | Delphi 里这一条实质是"被释放内存的探测"；托管端对象在入格前必已命名 |
| `[+0x128]==0` Envir 为空 | `m_PEnvir` | **grep 全仓：`m_PEnvir = null` 只出现在 `TBaseObject.cs:322` 的字段初始化器上**（`MapPoint.cs` 的同名字段属另一个类 `TPointManager`）。对象一旦入格，`m_PEnvir` 永不被置空 |
| `Envir[+0x44]==0` 地图未就绪 | 地图对象 | 本端运行期不拆图 |

三项在本端全部不可达 ⇒ 探针恒返回"存活" ⇒ 扫描必然落到 `IsNativeCellBlocking`（= `sub_765D94`），这正是本端现在做的事。**可观测行为等价**；缺的摘链是对裸指针链表的卫生动作，缺的 `0x79DF74` 是日志，均无游戏可观测面。

### 3. 处置与留痕

维持不改。**唯一的失效条件**：若日后有人在对象清理路径上引入 `m_PEnvir = null`，第二项立刻变为可达，届时必须补探针（否则一个 Envir 为空的对象会永久堵死一格）。此处记录以便回归。

---

## MOVE-39 — 人形 mover 尾部 sub_6BBEE4（已修）

### 1. 原分片的判断错在哪

原分片记成"英雄跟随，C# 大概率由英雄自身 AI 完成"。反汇编不支持：`0x74134A mov eax,[ebx+0x3C0]` —— `sub_6BBEE4` 的**接收者是 `[+0x3C0]` 指向的那个对象**，而 `[+0x3C0]` 是双人坐骑同伴指针，不是英雄。本端 `TPlayObject.NativeGroupProtocol.cs:541-547` 已经对该字段做过取证（9 个写点全在坐骑簇，读点按 actor 解引用取 `+0x106` 名字），并落为 `m_NativeHorsePartner`。

`sub_6BBEE4` 的直接 xref 恰好 4 处，全在坐骑簇：

| VA | 所属 | 本端状态 |
|---|---|---|
| `0x6EE8DC` | 接受双人坐骑，乘客搬到驾驶者格 | 已有 `MoveToNativeHorseDriver` |
| `0x741350` | **人形 walk mover `sub_741224` 尾（本条）** | 改前缺失 |
| `0x767683` | CM_RUN(3013) mover `sub_76756C` 尾 | 仍缺（见"相邻观察"） |
| `0x7677B4` | CM_RUN3(4108) mover `sub_767694` 尾 | 已有 `SyncNativeHorsePartnerAfterRun3` |

### 2. 两个 mover 尾部对照

```
              人形 sub_741224              怪物 sub_71F0F4
提交 X        0x7412D5                     0x71F203
提交 Y        0x7412DB                     0x71F209
清状态 0x17   0x7412E8 call 0x76B4D0       0x71F21C call 0x76B4D0
广播 RM_WALK  0x74130D dx=0x2712 [vmt+0xD8]  —（怪物走 0x71F211 dx=0x0B call 0x765154）
落格处理      0x74131B call 0x778EC0       0x71F231 call 0x778EC0
同伴跟随      0x741328..0x741350           —（无）
```

人形独有段：

```
0x741328  mov  dl,0x33
0x74132C  call 0x772960          ; InBodyState(0x33)
0x741333  je   0x741355
0x741335  cmp  dword [ebx+0x3C0],0
0x74133C  je   0x741355
0x74133E  mov  al,[ebx+0x154]    ; 自己的朝向
0x741345  mov  ecx,[ebp-0xC]     ; 新 Y
0x741348  mov  edx,esi           ; 新 X
0x74134A  mov  eax,[ebx+0x3C0]   ; 接收者 = 同伴
0x741350  call 0x6BBEE4
```

`sub_6BBEE4(partner, newX, newY, dir)` 自身：`0x6BBEF3 [partner+0x154]=dir` → `0x6BBF12 call sub_779CD8`（与 `sub_7797CC` 同形参：`eax=Envir, dx=oldX, cx=oldY, push newX/newY/self/boFlag=1`，实测 `0x779CF5 imul esi,[eax+0x40]` 与 MOVE-27 的列主序寻址一致）→ 成功才 `0x6BBF1B/0x6BBF21` 提交同伴 X/Y、`0x6BBF27` 清状态 0x17、`0x6BBF38 call sub_778EC0`、`0x6BBF3D call sub_6E37C4`；**全程不广播 RM_WALK**（客户端把乘客画在坐骑上）。

### 3. 净行为与可达性

驾驶者（state 0x33 + `m_NativeHorsePartner != null`）走一步：原生把乘客一起拖过去；改前的 C# 让乘客留在原地，双人坐骑当场脱钩。走路 handler 没有 0x33 闸（只有 0x34，MOVE-10），驾驶者本来就能走 —— 可达且可观测。

### 4. 改动

`0x741328..0x741350` 与 4108 mover 的 `0x767789..0x7677B4` 逐字节同构（同样的 `InBodyState(0x33) && [+0x3C0]!=0` 门 + 同一个 `sub_6BBEE4`），所以复用本端已落地的移植体 `SyncNativeHorsePartnerAfterRun3()`，不重写第二份。

- 新增 `GameSvr/Actors/TBaseObject.NativeWalkMoverTail.cs`：`protected virtual void OnNativeHumanWalkMoverCommitted()`，基类空实现 = 怪物 mover 分支。
- 新增 `GameSvr/Players/TPlayObject.NativeWalkPartnerFollow.cs`：覆写 → 调 `SyncNativeHorsePartnerAfterRun3()`。作用域天然精确：`m_NativeHorsePartner` 只在 `TPlayObject` 上，`HeroObject : AnimalObject` 拿基类空实现。
- `GameSvr/Actors/TBaseObject.cs`：`WalkTo` 的 `if (Walk(Grobal2.RM_WALK))` 成功臂内、`result = true` 之前调用。位置对齐原生（在广播与 `sub_778EC0` 之后）；`Walk()` 返回 false 时本端要回滚，故只挂成功臂。

### 5. 本条未做的两件事（原分片列在同一条下）

- **清定时状态 0x17**：`0x7412E8`（人形）与 `0x71F21C`（怪物）都有，属**两个 mover 共有**，应落在 `WalkTo` 而非人形钩子里。本端 `WalkTo` 无此调用，但 4108 分支的 `CompleteNativeRun3Move` 有 `RemoveNativeMovementTimedState(23)`。要补须先确认 internal type 23 在 `RemoveTimedAbilityInternal` 下的清除语义与 `sub_76B4D0 → sub_7731C0` 的节点摘除逐条对齐（`0x7731C0` 不只是 btr，还要按 `[node+1]` 匹配摘链），证据未取足，**本轮不动**。
- **`sub_778EC0`**：本端 `Walk()`（`TBaseObject.cs:4553`）已经把落格扫描（GATEOBJECT / EVENTOBJECT）与 RM_WALK 广播合并实现，等价物在位，只是次序与原生相反（原生先广播后落格）。不属本条缺口。

---

## 相邻观察（非本轮四条，仅报）

1. **CM_RUN(3013) 也缺同伴跟随**。原生 `sub_76756C` 尾 `0x76765B mov dl,0x33` / `0x767668 cmp [ebx+0x3C0],0` / `0x767683 call 0x6BBEE4`，与 MOVE-39 同构。本端 `TPlayObject.RunTo`（`TPlayObject.cs:1062-1073`）只做 `Walk(RM_RUN)`，无同伴同步。修法与 MOVE-39 完全一样（一行），但 3013 走的是 `RunTo` 而非 `WalkTo`，不在 MOVE-39 的契约边界内，留给专项。
2. **`sub_6BCE2C` 还有 3 处调用点未核**：`0x6D98DF`、`0x6EC635`、`0x6DA017`(CM_SPELL 3017)。本轮只处理 walk/run 两臂；HIT 族（`0x6D9ED3`/`0x6D9F7D`）与 spell 是否已接同一钩子未逐条核对。
3. **`sub_779CD8` vs `sub_7797CC`**：`sub_6BBEE4` 用的是前者，实测无地形/占用判定（纯摘链+头插），本端 `SyncNativeHorsePartnerAfterRun3` 用 `MoveToMovingObject(..., boFlag: true)` 近似。该近似是 master 既有选择（4108 分支），本轮沿用未改；若要精确须单独落一个"无判定重定位"原语。

---

## 改动清单

| 提交 | 条目 | 文件 | 类型 |
|---|---|---|---|
| `7b2e493b` | MOVE-11 | `GameSvr/Players/TPlayObject.NativeMoveActionCancel.cs`（新） | 移植 |
| `7b2e493b` | MOVE-11 | `GameSvr/Players/TPlayObject.Attack.cs` | 接线 ×2 |
| `6ee4ad8f` | MOVE-39 | `GameSvr/Actors/TBaseObject.NativeWalkMoverTail.cs`（新） | 虚钩子 |
| `6ee4ad8f` | MOVE-39 | `GameSvr/Players/TPlayObject.NativeWalkPartnerFollow.cs`（新） | 覆写 |
| `6ee4ad8f` | MOVE-39 | `GameSvr/Actors/TBaseObject.cs` | 接线 ×1 |
| `d890de54` | MOVE-10 | `GameSvr/Players/TPlayObject.NativeMoveMountGate.cs`（新） | 谓词（未接线） |

`SystemModule/Grobal2.cs` / `GameSvr/Players/TPlayObject.Message.cs` / `GameSvr/UsrSystem/UsrEngn.cs` 全程未触碰。

## 接线点（须主代理落地）

`GameSvr/Players/TPlayObject.Message.cs`，两处，各一行：

```csharp
case Grobal2.CM_WALK:                       // :1480
    if (IsNativeMoveBlockedByPassengerState()) break;   // MOVE-10 / 0x6D9BD0
    if (ClientWalkXY(...))

case Grobal2.CM_RUN:                        // :1567
    if (IsNativeMoveBlockedByPassengerState()) break;   // MOVE-10 / 0x6D9CEC
```

不要加到 `CM_TURN`(3010) / `CM_SITDOWN`(3012)：跳表证实那两个 handler 没有 0x34 闸。
