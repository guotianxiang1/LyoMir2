# MOVE-11 — 移动臂的前置刷新钩子（复核 + 收口）

- 工作树 / 分支：`D:\loym2\.claude\wt3\move11` / `w/move11`，基线 `69f049b6` = master
- 底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`（file off = VA − 0x400000）
- 反汇编：`tools/m2_disasm.py`（capstone x86-32），辅以 `%TEMP%` 的 rel32 xref / VMT / 跳表 / 字节模式扫描脚本
- 构建：`dotnet build GameSvr/GameSvr.csproj` → **0 错 15 警**（15 条全部为基线既有）

## 0. 裁决摘要

| 待判命题 | 裁决 | 依据 |
|---|---|---|
| 「`0x6D9BEC call sub_6BCE2C` 发外观刷新消息 SM 0x20」 | **假。逐字节证伪** | `sub_6BCE2C` 全函数 39 字节单 ret，无 `push 0x20`；它发的是 `0x4D0`/`0x4D2`/`0xD57` |
| 「依赖未建模的英雄/外观子系统」（旧 BLOCKED 理由） | **不成立** | 该描述抄的是**相邻函数** `sub_6BCE54` 的函数体，而那个 0x20 是**载荷字节数**不是 ident |
| MOVE-11 本体（walk/run 前置 `sub_6BCE2C`） | **已在 master 落地且忠实** | commit `a9cc64f2`，本轮逐条复核通过 |
| 移动臂是否**另有**未复现的前置刷新 | **有，且本轮已落地** | `0x6D9CE7 call sub_7742C0` —— CM_RUN(3013) handler 的**首条指令对** |

一句话：任务描述里的「SM 0x20 外观刷新」是个**误传**，但它指向的方位是对的 ——
CM_RUN 的移动臂**确实**有一个此前从未复现的前置刷新钩子，只不过它是 `sub_7742C0`
（隐身态揭示 + `RM_TURN` 重显），不是 `sub_6BCE2C`。

---

## 1. `sub_6BCE2C` 完整反汇编（39 字节，全文）

```
0x6BCE2C  55                 push ebp
0x6BCE2D  8BEC               mov  ebp,esp
0x6BCE2F  53                 push ebx
0x6BCE30  56                 push esi
0x6BCE31  8BF2               mov  esi,edx          ; 保存第 2 参（wIdent）
0x6BCE33  8BD8               mov  ebx,eax          ; Self
0x6BCE35  8BC3               mov  eax,ebx
0x6BCE37  E8EC120300         call 0x6EE128
0x6BCE3C  8BC3               mov  eax,ebx
0x6BCE3E  E88D270300         call 0x6EF5D0
0x6BCE43  8BD6               mov  edx,esi
0x6BCE45  8BC3               mov  eax,ebx
0x6BCE47  8B08               mov  ecx,[eax]
0x6BCE49  FF91D8010000       call [ecx+0x1D8]
0x6BCE4F  5E                 pop  esi
0x6BCE50  5B                 pop  ebx
0x6BCE51  5D                 pop  ebp
0x6BCE52  C3                 ret
0x6BCE53  90                 nop                   ; ← 函数边界
```

**全文无 `push 0x20`、无 `[+0x168]`、无 `vmt+0x1C8`、无 `vmt+0x70`。**

三条被调的完整语义（各自也全文反汇编过）：

| 被调 | 关键字节 | 行为 | 发出的 ident |
|---|---|---|---|
| `sub_6EE128` | `0x6EE12F mov si,word[edx+0xA24]`；`0x6EE139 jbe` 早退 | 清 `dword[+0xA20]` / `word[+0xA24]` / `word[+0xA26]` | `0x6EE164 mov dx,0x4D0` (1232)，经 `[vmt+0xE0]` |
| `sub_6EF5D0` | `0x6EF5D7 mov byte[edx+0x18E1],0`（**无条件，先于一切判断**）；`0x6EF5EA jbe` 早退 | `word[+0xA4C]!=0` 时清 `[+0xA28]..[+0xA4C]` 共 0x26 字节 | `0x6EF62E mov dx,0x4D2` (1234) |
| `[vmt+0x1D8]` | TPlayer VMT `0x6AC8C8`+0x1D8 = **`0x6EE2AC`**；`0x6EE2B2 cmp byte[edx+0x1914],0` 早退 | 清 `byte[+0x1914]`/`dword[+0x1918]`/`word[+0x191C]` | `0x6EE2DF mov dx,0xD57` (3415) |

`THumanKind` VMT `0x73BC34` 同槽 = `0x772A98`，实测就是一条 `C3 ret`（空实现）。

**第 2 参是死参**（复证）：`sub_6EE128` 开头 `0x6EE12D mov edx,eax`、`sub_6EF5D0` 开头
`0x6EF5D5 mov edx,eax`、`sub_6EE2AC` 开头 `0x6EE2B0 mov edx,eax` —— 三个被调都在读之前
就把 edx 覆盖了。本端 `CancelNativeActionChannels()` 不带参是忠实的。

## 2. 「SM 0x20」是从哪儿来的 —— 相邻函数 `sub_6BCE54`

`0x6BCE54` 是紧接着的下一个函数（`ret 0xC`，三个栈参）。它的函数体**逐项**对应旧结论
里那段描述：

```
0x6BCE65  lea eax,[ebp-0x24]
0x6BCE6A  mov edx,0x20
0x6BCE6F  call 0x403B2C            ; FillChar(rec, 0x20, 0) —— 记录是 32 字节
0x6BCE7E  call [vmt+0x1C8]         ; -> rec[0x00]
0x6BCE88  lea esi,[ebx+0x168]
0x6BCE91  A5 A5 A5 A5              ; movsd x4 —— 从 [+0x168] 拷 16 字节 -> rec[0x04..0x13]
0x6BCEA2  call [vmt+0x70]          ; -> rec[0x14..0x1D]
0x6BCEB9  push word[ebp+0x10]      ; p4
0x6BCEBE  push word[ebp+0x0C]      ; p5
0x6BCEC3  push word[ebp+0x08]      ; p6
0x6BCEC8  push lea[ebp-0x24]       ; p7 = 记录指针
0x6BCECC  6A20  push 0x20          ; p8  ←←← 就是这个 0x20
0x6BCED0  mov  dx,word[ebp-2]      ; wIdent 走的是 dx，与 0x20 无关
0x6BCED8  call [vmt+0x254]
```

`0x20` 是**载荷字节数**，不是 SM ident。接收方 `[vmt+0x254]` = TPlayer `0x6D7BF8` 拿它当长度用：

```
0x6D7C0A  mov  esi,[ebp+8]         ; = 0x20（最后一个栈参）
0x6D7C24  mov  edi,0xC             ; 包头 12 字节
0x6D7C29  cmp  dword[ebp+0xC],0    ; 记录指针非空
0x6D7C33  add  edi,esi             ; 总长 = 0xC + 0x20 = 0x2C
0x6D7C44  mov  dword[ebp-0x24],0x33AABB77   ; 包头魔数
0x6D7C5D  mov  word[ebp-0x18],0x0E
0x6D7C63  mov  word[ebp-0x16],di            ; ← 长度字段
```

对照组：同族的 `[vmt+0x250]` = `0x6D7CB0` 结构完全一样，只是
`0x6D7CE7 call 0x4057D0`(Length) + `inc eax` —— 那是**字符串**载荷版本，这个是**裸缓冲区**版本。

**并且 `sub_6BCE54` 根本不在移动路径上**：rel32 全扫其调用点恰好 14 处，
`0x6B4529 / 0x6B4557 / 0x6B4585 / 0x6B4813 / 0x6B490B / 0x6B492E / 0x6B49F4 / 0x6B4A36 /
0x6B56B8 / 0x6B59FF / 0x6B5B93 / 0x6B5C91 / 0x6B602C / 0x6EED12`，一个都不在 `0x6D9xxx` 派发里。

**结论：旧 BLOCKED 理由「依赖未建模的英雄/外观子系统」建立在一次张冠李戴上，不成立。**

本轮已把仓内三处复述该错误的注释/诊断串改正（见 §6）。

## 3. `0x6D9BEC` 的精确位置（复核通过）

跳表 `0x6D8592`（`0x6D857D add eax,-0xBC2 / cmp eax,0x10 / jmp [eax*4+0x6D8592]`，基 ident 3010）实测 17 项：

| ident | handler | | ident | handler |
|---|---|---|---|---|
| 3010 turn | `0x6D9B65` | | 3017 spell | `0x6DA04A` |
| 3011 walk | `0x6D9BD0` | | 3018/3019 | `0x6D9EAF` |
| 3012 pose | `0x6D9C7D` | | 3020..3023 | `0x6DBC2C`（空出口）|
| 3013 run | `0x6D9CE4` | | 3024..3026 | `0x6D9EAF` |
| 3014..3016 | `0x6D9EAF` | | | |

walk 臂全文（`0x6D9BD0`..`0x6D9C78`）：

```
0x6D9BD0  mov dl,0x34 / call 0x772960 / jne 0x6DBC2C    ; gate1 乘客态，静默（MOVE-10）
0x6D9BE2  movzx edx,word[pkt+4]                          ; 死参
0x6D9BEC  call 0x6BCE2C                                  ; ← 本条
0x6D9BF6  call [edx+0xBC]  / je 0x6D9C26                 ; gate3
0x6D9C07  xor edx,edx / call [ecx+0x40] / je 0x6D9C26    ; gate4（walk 传 dl=0）
0x6D9C1D  call 0x6BBCD8                                  ; 走路原语
0x6D9C4B  mov dx,0x276 / call [vmt+0x250]                ; 失败：X/Y/Dir 纠正
0x6D9C69  mov dx,0x275 / call [vmt+0x250]                ; 成功：四零
```

run 臂同构，钩子在 `0x6D9D08`，gate3 `0x6D9D12`、gate4 `0x6D9D23`（`mov dl,1`）、原语 `0x6D9D39`。

即：**无闸、每次都发，且在 gate3/gate4/原语之前** —— 被 `0x276` 拒绝的移动也已经取消了通道。
本端 `ClientWalkXY`(`TPlayObject.Attack.cs:905`) / `ClientRunXY`(`:767`) 的落点与此一致。

## 4. `sub_6BCE2C` 调用点普查（8 处，逐条交叉验证）

| VA | 归属 | 本端状态 |
|---|---|---|
| `0x6D98DF` | **CM_HERO_POWERUP(1108)**，两道 0x33/0x34 门之后 | 已接（`Message.cs` 三连） |
| `0x6D9BEC` | CM_WALK(3011) | 已接（`Attack.cs:905`） |
| `0x6D9D08` | CM_RUN(3013) | 已接（`Attack.cs:767`） |
| `0x6D9ED3` | HIT 族共享臂 `0x6D9EAF`（3014/3015/3016/3018/3019/3024/3025/3026），在 can-act 之**前** | **未接**（见 §7） |
| `0x6D9F7D` | CASE2（CM_3037/3027），在 can-act 之**后** | **未接**（见 §7） |
| `0x6DA017` | **CM 4105**（非 CM_SPELL） | fail-closed，不动 |
| `0x6EC635` | `sub_6EC5D8`，唯一入口 CM 3344 | fail-closed，不动 |
| `0x6EE201` | `sub_6EE174` 召唤坐骑，由 CM 4105 进入 | fail-closed，不动 |

**更正两处旧标注**（本轮实证）：

- `0x6DA017` **不是** CM_SPELL(3017)。跳表 idx 7 = 3017 → `0x6DA04A`，那条臂是
  `0x6DA054 call 0x6F2D48` + `0x6DA059 mov dl,0x33`，通篇不调 `0x6BCE2C`。
  `0x6DA005` 不在跳表里，属 CM 4105 leaf（`0x6DA008 call 0x7742C0` / `0x6DA017 call 0x6BCE2C` /
  `0x6DA026 call 0x6EE174` / `0x6DA02B jmp 0x6DBC2C`）。
- `0x6D98DF` 是 CM_HERO_POWERUP(1108)，不是「双人坐骑相关臂」。

---

## 5. 本轮真正的缺口：`sub_7742C0` —— CM_RUN 臂的前置揭示钩子

### 5.1 发现

run 臂的入口并不是 `mov dl,0x34`，而是：

```
0x6D9CE4  8B45FC        mov  eax,[ebp-4]
0x6D9CE7  E8D4A50900    call 0x7742C0      ; ←←← 比 MOVE-10 的 0x34 闸还早
0x6D9CEC  B234          mov  dl,0x34
```

walk 臂（`0x6D9BD0`）**没有**这一步。此前所有移动族报告都从 `0x6D9CEC` 开始读 run 臂，
把 `0x6D9CE4`/`0x6D9CE7` 漏掉了。

### 5.2 `sub_7742C0` 全文语义

```
0x7742D6  B2 40                mov  dl,0x40
0x7742DA  E881E6FFFF           call 0x772960        ; InBodyState(0x40)
0x7742DF  84C0 / 7442          test al,al / je 0x774325      ; 未隐身 -> 整体空操作
0x7742E3  B2 40                mov  dl,0x40
0x7742E7  E8D4EEFFFF           call 0x7731C0        ; 清 0x40 + 摘定时节点
0x7742EC  8B832C010000 / 50    push [Self+0x12C]    ; nParam1 = X
0x7742F3  8B8330010000 / 50    push [Self+0x130]    ; nParam2 = Y
0x7742FA  6A00                 push 0               ; nParam3
0x774303  FF9190000000         call [vmt+0x90]      ; GetShowName -> sMsg
0x77430C  50                   push eax             ; sMsg
0x77430D  6A01                 push 1               ; boFlag = 含自己
0x774311  8A8B54010000         mov  cl,[Self+0x154] ; wParam = 朝向
0x774317  66BA1127             mov  dx,0x2711       ; RM_TURN (10001)
0x77431F  FF93D8000000         call [vmt+0xD8]      ; SendRefMsg
```

`[vmt+0x90]` = TPlayer `0x6C5A30`（开头 `0x6C5A5B mov dl,0x33 / call 0x772960` —— 坐骑感知的
显示名），THumanKind `0x768640`，THeroAct `0x6922E0`。本端 `GetShowName()` 正是 virtual +
`TPlayObject` override，虚派发形状一致。

`boFlag` 的含义由 `[vmt+0xE0]` = `0x6DC0C0` 的同一参数位钉死：

```
0x6DC22F  mov eax,[ebp-0xC]   ; 当前观察者
0x6DC232  cmp eax,[ebp-4]     ; 与 Self 比
0x6DC235  0F95C0  setne al    ; al = (观察者 != 自己)
0x6DC238  0A4508  or  al,byte[ebp+8]   ; | boFlag
0x6DC23B  je  skip
```

即 `boFlag=1` = **不跳过自己**。本端 `SendRefMsg` 是按格扫描视野、自己就在自己的格里，
天然含自己 —— 与 `push 1` 相符。

### 5.3 state 0x40 是什么（全镜像取证）

`mov dl,0x40` + 紧跟 rel32 call 的站点全扫，代码段内 6 处：

| VA | callee | 角色 |
|---|---|---|
| `0x686AD2` | `0x772960` | 读 |
| `0x6F2F02` | `0x772960` | 读 |
| `0x774291` | `0x772960` | 读（`sub_774288`） |
| `0x7742D6` | `0x772960` | 读（本条） |
| `0x7742E3` | `0x7731C0` | **唯一的清位点** |
| — | — | `mov dl,0x40` 无置位点；`mov dx,0x40` 0 命中 |

`sub_774288(eax=被看者, edx=观察者)`：

```
0x774291  mov dl,0x40 / call 0x772960 / je -> 0
0x77429E  ecx=[观察者+0x130] / edx=[观察者+0x12C]
0x7742AC  call 0x76B4A4          ; Chebyshev 距离
0x7742B1  cmp eax,2 / ja -> 1
```

它是两个广播槽的逐观察者排除项（`0x6DC247` 在 `[vmt+0xE0]`、`0x6DC6F1` 在 `[vmt+0xD8]`），
所以 **state 0x40 = 隐身（2 格外看不见）**。

置位端在本端已建模：`TBaseObject.NativeSkill261.cs`（隐身术 magic 261）
`AddTimedAbilityInternal(0x40, 1, (lv+1)*5*1000, 0)`，读端 `IsNativeStealthedFrom` 就是
`sub_774288` 的移植体。**唯独没有任何路径提前清它** —— 全仓 `NativeSkill261State` 只有
声明 / 一个 Add / 一个读，三处。

### 5.4 净行为偏差

原生：隐身玩家一跑，state 0x40 当场掉，全屏重发 `RM_TURN`（朝向 + X/Y + 显示名）把他画回来。
改前的 C#：隐身玩家可以在隐身全程（5..25 秒）**边跑边隐形**，只能等计时到期。
可达且可观测。

**走路不揭示、跑步才揭示** —— 这是原生的既有形状（walk handler `0x6D9BD0` 不调 `sub_7742C0`），
不是遗漏，不要顺手给 CM_WALK 也接上。

### 5.5 `sub_7742C0` 调用点普查（rel32 全扫，恰好 4 处）

| VA | 归属 | 处置 |
|---|---|---|
| `0x6D9CE7` | **CM_RUN(3013) handler 首指令对** | **本条已落地** |
| `0x6DA008` | CM 4105 worker 1 | 整臂 fail-closed，不动 |
| `0x6F2D62` | `sub_6F2D48(Self, Ident)` 内：`0x6F2D53 call 0x772E24` 后 `0x6F2D58 cmp esi,0x10B / je` 豁免。`sub_6F2D48` 的 rel32 调用点恰好 3 处：`0x6D9EB4`(HIT CASE1，`edx=0` 故豁免不触发)、`0x6D9F50`(CASE2，同)、`0x6DA054`(CM_SPELL 3017，`edx=word[pkt+0xA]`) | 非移动臂，只报 |
| `0x768CFB` | `sub_768CEC`，非 CM leaf | 只报 |

注意 CM_RUN3(4108, `0x6D9D99`) 与 CM_HORSERUN **不在**此表里 —— 骑乘跑不揭示隐身。

### 5.6 清位原语的对齐证明

`sub_7731C0(eax=Self, dl=state)` 与本端 `RemoveTimedAbilityInternal(byte)` 逐项同构：

| 原生 | C# |
|---|---|
| `0x7731D8 call 0x772960` / `je` 返回 false | `if (!HasNativeActiveState(t)) return false;` |
| `0x7731E6 mov eax,[esi+0xDC]` 取表头 | `var node = m_TimedAbilityHead;` |
| `0x7731F2 call 0x7729A8`（btr） | `ClearNativeActiveState(t);` |
| `0x7731FE cmp bl,byte[eax+1]` 按类型匹配 | `node.InternalType == internalType` |
| `0x773203` 有前驱 → `0x773211 [prev+0xE]=[node+0xE]`；无 → `0x77321B [esi+0xDC]=[node+0xE]` | `previous == null ? m_TimedAbilityHead = node.Next : previous.Next = node.Next` |
| `0x773227 call [vmt+0x5C]` = **`0x741578`** | `SendTimedAbilityState(node, true)` + `OnNativeTimedStateLost(...)` |
| `0x77322C call 0x764E10` 释放节点 | GC |
| `0x773231 mov byte[ebp-1],1` | `return true;` |

`[vmt+0x5C] = 0x741578` 正是 `TBaseObject.TimedAbility.cs:327` 注释里已登记的
「TPlayObject 状态消亡虚方法覆写」，所以这条链条不是新近似，而是已在位的移植体。
`OnNativeTimedStateLost` 无 `case 64`，走静默默认臂 —— 与该状态自然到期时**完全同路**。

### 5.7 落地

新增 `GameSvr/Actors/TBaseObject.NativeStealthBreak.cs`：

```csharp
internal bool BreakNativeStealthOnAction()
{
    if (!HasNativeActiveState(NativeSkill261State)) return false;   // 0x7742D6..0x7742E1
    RemoveTimedAbilityInternal(NativeSkill261State);                 // 0x7742E3 / 0x7742E7
    SendRefMsg(Grobal2.RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, 0,
        GetShowName());                                              // 0x7742EC..0x77431F
    return true;
}
```

放在 `TBaseObject` 而非 `TPlayObject`：原生 `sub_7742C0` 位于基础单元（`0x77xxxx`）、
只吃 `eax=Self`、内部全走虚槽（`+0x90` / `+0xD8`），且另有 HIT/spell 侧的调用点将来要复用。
`RemoveTimedAbilityInternal` 与 `NativeSkill261State` 都是 `TBaseObject` 的 private，
partial 同类可见，无需放宽任何可见性。

**未接线** —— 落点在禁改的 `TPlayObject.Message.cs`。见 §8。

---

## 6. 顺带更正的错误归因（纯注释/诊断串，无行为改动）

| 文件 | 原文 | 现文 |
|---|---|---|
| `GameSvr/Services/NativeCmQ3FailClosed.cs:110` | `0x6BCE2C(发 SM 0x20)`；`0x7742C0(刷新 [+0x12C/0x130/0x154/0x388/0x178/0x270/0x272])` | 按实测改为「取消挂起通道三连 0x4D0/0x4D2/0xD57」与「隐身态 0x40 揭示」；`[+0x388]/[+0x178]/[+0x270]/[+0x272]` 是广播槽 `0x6DC590` 的内部字段，不是 `sub_7742C0` 读的 |
| `GameSvr/Players/TPlayObject.HeroNotify.cs:205-224` | 同上 + 「vmt+0x1D8 sends SM 0x20 from the 16-byte appearance block [obj+0x168] plus vmt+0x1C8 / vmt+0x70」 | 改为 `0x6EE2AC` 的真实行为；那段描述实为 `sub_6BCE54` |
| `GameSvr/Players/TPlayObject.NativeCmProtocol_Q3.cs:329-333` | 「0x6BCE2C … answers SM 0x20 off [+0x1C8]/[+0x1D8]/[+0xC28]」 | 同上 |
| `GameSvr/Players/TPlayObject.NativeMoveActionCancel.cs:5-7` | `0x6D98DF(双人坐骑相关臂)`、`0x6DA017(CM_SPELL 3017)` | 改为 `CM_HERO_POWERUP 1108` / `CM 4105`，并附跳表反证 |

`NativeCmQ3FailClosed` 里 **CM 4105 的 fail-closed 判定本身不变**：worker 1（`0x7742C0`）与
worker 2（`0x6BCE2C`）现在都已建模，但 worker 3（`0x6EE174` 召唤坐骑，读
`[+0x4C0]` / `[+0xA24]==0x72` / `[+0x1914]`）仍未建模，而那才是这条命令的正事 ——
只发两个刷新、既不给坐骑也不给三条拒绝提示，不是原版的整臂行为。只改了理由文本。

已全仓确认：`AuditTools` 无任何断言依赖这些字符串（`rg "状态刷新三连|SM 0x20|Q3FailClosed" AuditTools` 0 命中）。

## 7. 明确未做（fail-closed，留给专项）

1. **HIT 族 `0x6D9ED3` / `0x6D9F7D` 的 `sub_6BCE2C`**。CASE1（`0x6D9EAF`，8 个 ident）把钩子放在
   can-act 门 `0x6D9EDF mov dl,1 / call [vmt+0x40]` **之前**，CASE2（`0x6D9F4B`，CM_3037/3027）
   放在 `0x6D9F6C call [ecx+0x40]` **之后**。C# 把两者并进同一个 `case` 块，单点插入无法同时忠实
   两种次序；且本端该 `case` 尚未建模那道 can-act 门。要修须先取证 `[vmt+0x40]` 再按 ident 分叉。
2. **HIT/spell 侧的 `sub_7742C0`**（经 `0x6F2D62`）。`sub_6F2D48` 还带 `0x6F2D53 call 0x772E24`
   与 `cmp esi,0x10B` 豁免，是一条独立的组合钩子，不在移动臂契约内。
3. **`0x768CFB`** 未归因。
4. `[vmt+0xE0]` / `[vmt+0xD8]` 的完整形参表（本端 `SendRefMsg` 少一个 `boFlag` 位）是全仓既有的
   近似，非本条引入 —— 本条用到的那个站点恰好 `boFlag=1`，与 C# 的天然行为相符，不受影响。

## 8. 接线需求（须主代理落地）

**文件**：`GameSvr/Players/TPlayObject.Message.cs`

**位置**：`case Grobal2.CM_RUN:`（当前 :1577）**紧跟 case 标签、在 MOVE-10 那道
`if (IsNativeMoveBlockedByPassengerState())` 之前**，加一行：

```csharp
case Grobal2.CM_RUN:
    // MOVE-11 / 0x6D9CE7：run handler 首指令对就是 call sub_7742C0，
    // 排在 0x6D9CEC 的 state 0x34 乘客闸之前 —— 乘客被静默丢弃也已经揭示。
    BreakNativeStealthOnAction();
    if (IsNativeMoveBlockedByPassengerState())   // MOVE-10 / 0x6D9CEC
    {
        break;
    }
    ...
```

次序不能反：原生 `0x6D9CE7` < `0x6D9CEC`，且 `sub_7742C0` 在乘客态被拒时**照样跑过**。

**不要**加到 `CM_WALK`(3011) / `CM_TURN`(3010) / `CM_SITDOWN`(3012) / `CM_HORSERUN` /
CM_RUN3(4108) —— rel32 全扫证明 `sub_7742C0` 在移动族里只有 `0x6D9CE7` 这一个调用点。

## 9. 改动清单

| 提交 | 内容 | 类型 |
|---|---|---|
| `6f22b99e` | `GameSvr/Actors/TBaseObject.NativeStealthBreak.cs`（新） | 移植（未接线） |
| `b7895f0b` | `NativeCmQ3FailClosed.cs` / `TPlayObject.HeroNotify.cs` / `TPlayObject.NativeCmProtocol_Q3.cs` / `TPlayObject.NativeMoveActionCancel.cs` | 注释更正，零行为 |

`SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、`GameSvr/UsrSystem/UsrEngn.cs`
全程未触碰。
