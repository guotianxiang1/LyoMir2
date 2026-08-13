# MOVE-74 / MOVE-73 / MOVE-39 —— `Obj+0x3FE` 穿透缓存的归属裁定与落地

> 分支 `w/move74`，基于 master `58cb0ac4`。底本 `staging\_reunpack_work\flat_image.bin`
> （ImageBase `0x400000`）。取证脚本 `tools/move74_off3fe_census.py`、
> `tools/move74_callxref.py`、`tools/move74_vmtowner.py`。

## 0. 结论速览

| 问题 | 裁定 | 依据 |
|---|---|---|
| `Obj+0x3FE` 是 InSafeArea 还是穿透缓存？ | **穿透缓存**。28 个访问点无一与名字颜色/PK/安全区显示相关 | §3 普查表 |
| `sub_768454` 是什么？ | 「本对象此刻能否穿过占格者」，**唯一调用者** `0x6B308E` | §1 / §2 |
| `0xB05` 由什么驱动？ | `sub_768454` 的**变迁**，无条件每 tick 求值，**无任何行会条件** | §4 |
| C# 两个字段重复建模？ | 是。`m_boThroughOccupancyCache` 正确，`m_boInSafeArea` 错 | §5 |
| recount 的四条结论 | **四条全部独立复核成立**，无一需要推翻 | §1–§4 |

recount 唯一措辞需要收紧的地方：它说「这不是 InSafeArea，是穿透判定」。
更准确的说法是 —— `sub_768454` 的**主体确实是安全区测试**（`sub_7684DC` 里
`boSAFE` / SafeZoneList 多边形 / RedHome / 起点表全在），但它**不是** C# 那个
`InSafeArea()`，而且它的**用途**（唯一写入 `+0x3FE`，`+0x3FE` 只喂
`boIgnoreOccupancy`）是穿透。判定内容像安全区、语义槽位是穿透，两者都要说全，
否则下一个人会以为可以拿 `InSafeArea()` 顶替。

---

## 1. `sub_768454` 逐字节还原

```
00768454  55                       push ebp
00768455  8B EC                    mov  ebp,esp
00768457  53                       push ebx
00768458  8B D8                    mov  ebx,eax          ; ebx = Self
0076845A  8B C3                    mov  eax,ebx
0076845C  E8 57 AA 00 00           call 0x772EB8         ; 无条件穿透授予
00768461  84 C0                    test al,al
00768463  75 33                    jne  0x768498         ; -> TRUE
00768465  8B 83 28 01 00 00        mov  eax,[ebx+0x128]  ; Envir
0076846B  80 B8 84 00 00 00 00     cmp  byte [eax+0x84],0 ; NOTHROUGH
00768472  75 1F                    jne  0x768493         ; 置位 -> FALSE
00768474  A1 70 69 7D 00           mov  eax,[0x7D6970]
00768479  8B 00                    mov  eax,[eax]        ; ThroughRange
0076847B  50                       push eax              ; arg4
0076847C  8B 8B 30 01 00 00        mov  ecx,[ebx+0x130]  ; Y
00768482  8B 93 2C 01 00 00        mov  edx,[ebx+0x12C]  ; X
00768488  8B C3                    mov  eax,ebx
0076848A  E8 4D 00 00 00           call 0x7684DC
0076848F  84 C0                    test al,al
00768491  75 05                    jne  0x768498         ; -> TRUE
00768493  33 C0                    xor  eax,eax          ; FALSE
00768495  5B / 5D / C3             pop ebx / pop ebp / ret
00768498  B0 01                    mov  al,1             ; TRUE
0076849A  5B / 5D / C3
```

等价伪码：

```
bool sub_768454(Self):
    if sub_772EB8(Self):                 return TRUE     // m_boObMode ‖ 体状态 0x3C
    if Envir[+0x84] != 0:                return FALSE    // NOTHROUGH
    return sub_7684DC(Self, X=[+0x12C], Y=[+0x130], nRange=*[0x7D6970])
```

`sub_7684DC(Self, nX=edx, nY=ecx, nRange=[ebp+8])`，`ret 4`：

```
007684F6  8A 58 5C                 mov  bl,[Envir+0x5C]      ; boSAFE（整图安全）
007684F9  84 DB / 75 0F            SAFE 非 0 -> 跳过下一步，bl 即结果
00768505  E8 96 FF FF FF           call 0x7684A0             ; SafeZoneList 多边形
0076850C  84 DB / 75 62            bl 非 0 -> TRUE
00768510  85 FF / 7E 5E            nRange <= 0 -> 返回 bl(FALSE)，后两臂被整段跳过
0076851D  BA 88 85 76 00           edx = 字面量 @0x768588（RedHome 图名）
00768522  E8 F5 D3 C9 FF           call 0x40591C             ; 与 Envir[+0x44] 比串
00768527  75 24                    不等 -> 跳到起点表臂
0076852C  2D 4D 03 00 00           sub eax,0x34D (845)  -> cdq/xor/sub = abs
00768536  3B F8 / 7C 13            nRange < |X-845| -> 跳过
0076853D  2D A2 02 00 00           sub eax,0x2A2 (674)  -> abs
00768547  3B F8 / 7C 02            nRange < |Y-674| -> 跳过
0076854B  B3 01                    bl = 1  (RedHome 命中)
0076854D  84 DB / 75 21            bl 非 0 -> TRUE
00768567  E8 D8 E8 F2 FF           call 0x696E48             ; 起点表半径 nRange 扫描
```

两个列表**确实是两个不同的列表**，不是同一个：
`sub_7684A0 -> sub_696D7C` 在 `0x696DAE` 读 `[UserEngine+0x3C]`；
`sub_696E48` 在 `0x696E71` 读 `[UserEngine+0x38]`。C# 现有的
`M2Share.SafeZoneList` / `M2Share.StartPointList` 二分是站得住的，**不要合并**。

C# 侧 `NativeComputeThroughOccupancy()` + `NativeSafeZoneThroughTest()`
（`TPlayObject.NativePassThrough.cs`）与上表逐条对上，本轮**未发现需要改的地方**。

### 1.1 与 `sub_76858C` 的区别（别再混）

`sub_76858C` 是**另一个**函数，11 个调用者，形状近似但少两件事、多一件事：

```
00768598  8A 40 5C                 mov al,[Envir+0x5C]      ; boSAFE
007685AD  E8 EE FE FF FF           call 0x7684A0            ; 多边形
007685BE  6A 0C                    push 0xC                 ; nRange 恒为 12（硬编码）
007685D7  E8 6C E8 F2 FF           call 0x696E48
```

即：**没有** `sub_772EB8` 授予臂、**没有** NOTHROUGH 否决臂、**没有** RedHome 臂，
半径写死 12 而不是 `*[0x7D6970]`。C# `TBaseObject.InSafeArea()` 用的是半径 **60**
的起点表扫描，两边都不等于 `sub_768454`。（半径 60→12 的订正属 MFLG-17，
已在旁支 `fix/mine-21-tier2-halfspeed` 的 `962a0afb` 上，本分支不动它以免撞车。）

---

## 2. tick 站点 `0x6B308E`：确认「无条件求值」

```
006B2FFB  E8 40 53 D5 FF           call 0x408340            ; GetTickCount -> [ebp-8]
006B3003  80 B8 11 07 00 00 00     cmp byte [eax+0x711],0
006B300D  74 2D                    je  0x6B303C
...
006B303C  8B 45 FC                 mov eax,[ebp-4]
006B303F  80 B8 11 07 00 00 00     cmp byte [eax+0x711],0
006B3046  74 43                    je  0x6B308B      <-- 跳【到】求值点
006B304E  2B 90 1C 07 00 00        sub edx,[eax+0x71C]
006B3054  81 FA 00 53 07 00        cmp edx,0x75300   ; 480000ms
006B305A  76 2F                    jbe 0x6B308B      <-- 也跳【到】求值点
006B305C..006B3086                 挂机播报 + call 0x765E68
006B308B  8B 45 FC                 mov eax,[ebp-4]   <-- 直落 + 两条跳转的共同落点
006B308E  E8 C1 53 0B 00           call 0x768454
```

`0x6B2FFB`..`0x6B30E1` 之间**没有任何时间闸**（全函数扫 `0x3E8` 立即数，
本区间 0 命中；最近的时间闸是 `0x6B3149` 的 3000ms，在本块**之后**），
也**没有任何** `m_MyGuild` / 行会战判断。recount 这两条结论成立。

变迁段：

```
006B3093  8B 55 FC                 mov edx,[ebp-4]
006B3096  3A 82 FE 03 00 00        cmp al,[edx+0x3FE]     ; 与旧缓存比
006B309C  74 43                    je  0x6B30E1           ; 未变 -> 既不写也不发包
006B309E  8B 4D FC                 mov ecx,[ebp-4]
006B30A1  8B D0                    mov edx,eax
006B30A3  88 91 FE 03 00 00        mov [ecx+0x3FE],dl     ; 唯一写点
006B30A9  84 D2 / 74 1B            test dl,dl / je 0x6B30C8
006B30AD  6A 06 / 6A 01 / 6A 00 / 6A 00
006B30B5  33 C9 / 66 BA 05 0B      xor ecx,ecx / mov dx,0xB05
006B30C0  FF 93 50 02 00 00        call [ebx+0x250]
006B30C6  EB 19                    jmp 0x6B30E1
006B30C8  6A 06 / 6A 00 / 6A 00 / 6A 00
006B30D2  33 C9 / 66 BA 05 0B
006B30DB  FF 93 50 02 00 00        call [ebx+0x250]
```

---

## 3. `Obj+0x3FE` 全部读写点普查（28 个，穷举）

扫法：全镜像搜 `FE 03 00 00`，对每个命中回退 2..10 字节逐一 capstone 解码，保留
「指令长度覆盖该 disp 且内存操作数 disp 恰为 0x3FE」的解。**宁可多报不可漏报**。

| # | VA | 字节 | 指令 | 去向 |
|---|---|---|---|---|
| 1 | `0x6661AB` | `80 BB FE 03 00 00 00` | `cmp byte [ebx+0x3FE],0` | **闸**：非 0 → 跳过「同格 ≥2 人则置 `[+0x4D9]`」 |
| 2 | `0x673692` | `8A 8E FE 03 00 00` | `mov cl,[esi+0x3FE]` | `call [vmt+0x30]` WalkTo 第三参 |
| 3 | `0x6736A1` | `80 BE FE 03 00 00 00` | `cmp byte [esi+0x3FE],0` | **闸**：非 0 → 跳过挤人（`0x778858` GetXYObjCount ≥2） |
| 4 | `0x6737A1` | `8A 8B FE 03 00 00` | `mov cl,[ebx+0x3FE]` | WalkTo |
| 5 | `0x673943` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | `push` → `call 0x777EF8` CanWalkEx |
| 6 | `0x675261` | `8A 8B FE 03 00 00` | `mov cl,[ebx+0x3FE]` | WalkTo |
| 7 | `0x682B04` | `8A 8E FE 03 00 00` | `mov cl,[esi+0x3FE]` | WalkTo |
| 8 | `0x68BBCF` | `8A 86 FE 03 00 00` | `mov al,[esi+0x3FE]` | CanWalkEx |
| 9 | `0x68BC17` | `8A 86 FE 03 00 00` | `mov al,[esi+0x3FE]` | CanWalkEx |
| 10 | `0x68BEEF` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | CanWalkEx |
| 11 | `0x68C710` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | CanWalkEx |
| 12 | `0x6B3096` | `3A 82 FE 03 00 00` | `cmp al,[edx+0x3FE]` | **tick 比较** |
| 13 | `0x6B30A3` | `88 91 FE 03 00 00` | `mov [ecx+0x3FE],dl` | **全镜像唯一写点** |
| 14 | `0x6B3206` | `80 BA FE 03 00 00 00` | `cmp byte [edx+0x3FE],0` | **闸**：非 0 → 跳过整段 CharPushed |
| 15 | `0x6BBD0C` | `8A 8B FE 03 00 00` | `mov cl,[ebx+0x3FE]` | WalkTo（CM_WALK 处理器 `sub_6BBCE0`） |
| 16 | `0x6EECE0` | `8A 8B FE 03 00 00` | `mov cl,[ebx+0x3FE]` | WalkTo |
| 17 | `0x71ACD3` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | CanWalkEx |
| 18 | `0x71AEAD` | `8A 8B FE 03 00 00` | `mov cl,[ebx+0x3FE]` | WalkTo |
| 19 | `0x71B0E4` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | CanWalkEx |
| 20 | `0x71B12C` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | CanWalkEx |
| 21 | `0x71DE77` | `8A 8B FE 03 00 00` | `mov cl,[ebx+0x3FE]` | WalkTo |
| 22 | `0x71DECC` | `8A 8B FE 03 00 00` | `mov cl,[ebx+0x3FE]` | WalkTo |
| 23 | `0x71E903` | `8A 8E FE 03 00 00` | `mov cl,[esi+0x3FE]` | WalkTo |
| 24 | `0x7675BA` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | CanWalkEx（2 格 run 探测） |
| 25 | `0x767601` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | `call 0x7797CC` MoveToMovingObject |
| 26 | `0x7676E2` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | CanWalkEx（3 格 run 探测） |
| 27 | `0x76772B` | `8A 83 FE 03 00 00` | `mov al,[ebx+0x3FE]` | `call 0x7797CC` |
| 28 | `0x767EC8` | `8A 86 FE 03 00 00` | `mov al,[esi+0x3FE]` | CanWalkEx |

分布：WalkTo 第三参 **10**、CanWalkEx 第四参 **11**、MoveToMovingObject 第六参 **2**、
挤人闸 **3**、tick 读写 **2**。

**裁定**：`Obj+0x3FE` = **穿透缓存**（`boIgnoreOccupancy`）。27 个读点中
**零个**与名字颜色、PK 状态、安全区提示或任何显示层相关。C# 的
`m_boThroughOccupancyCache` 是对的名字；`m_boInSafeArea` 是错的名字，
`Grobal2.cs:1044` 把 2821 注为 "Safe zone entry/exit notification" 也是同一误解。

生产者侧同样唯一：`sub_768454` 全镜像**只有 1 个引用**（`E8/E9` rel32 全偏移穷举
＋ 绝对 dword 全偏移穷举，双路都只命中 `0x6B308E`）。所以「一个生产者、一个写点、
27 个只读消费点」这条链是闭合的。

---

## 4. `0xB05` 的完整触发条件

| 项 | 值 | 字节 |
|---|---|---|
| 谁 | 玩家自己（`sub_6B2D38` 的 Self，即 `TPlayer.Run`） | `0x6B308B mov eax,[ebp-4]` |
| 何时 | **每次 `Run()` 都求值**；仅当 `sub_768454` 的返回值与 `[+0x3FE]` **不同**时才发 | `0x6B3096 cmp` / `0x6B309C je` |
| 发给谁 | `vmt+0x250` = `SendDefMessage`，**只发自己**（不是 SendRefMsg 广播） | `0x6B30C0 / 0x6B30DB call [ebx+0x250]` |
| 载荷 | `wIdent=0xB05(2821)`，`nRecog=0`，`nParam=6`，`nTag=1`(可穿)/`0`(不可穿)，`nSeries=0`，`sMsg` 空 | TRUE 臂 `6A 06 / 6A 01 / 6A 00 / 6A 00 / 33 C9 / 66 BA 05 0B`；FALSE 臂 `6A 06 / 6A 00 / 6A 00 / 6A 00 / 33 C9 / 66 BA 05 0B` |
| 前置条件 | **无**。无时间闸、无行会闸、无地图闸 | §2 |

vmt+0x250 的形参映射由同函数内两个对照点坐实：`0x6B2EED`（`dx=0xD6`、`ecx=0`、
四个 `push 0`）与 `0x6B2F50`（`dx=0x272`、`ecx=1`、四个 `push 0`）——
即 `SendDefMessage(wIdent=dx, nRecog=ecx, nParam, nTag, nSeries, sMsg)`，
栈参按 Delphi 寄存器约定自左向右压。C# 现有调用
`SendDefMessage(SM_COMMON_INFORMATION, 0, 6, x?1:0, 0, string.Empty)` 形状正确，
**错的只是驱动它的谓词和外面那两道门**。

---

## 5. 两个 C# 字段的归属裁定

| C# 符号 | 对应原生槽 | 裁定 |
|---|---|---|
| `m_boThroughOccupancyCache`（`TPlayObject.NativePassThrough.cs`） | `Obj+0x3FE` | **正确**，保留为唯一权威 |
| `m_boInSafeArea`（`TPlayObject.Base.cs:46`） | 无。它是 stock-Mir2 遗留名，被 `Message.cs` 用来存 `InSafeArea()`（半径 60 起点表）的上一次值 | **错误建模**，应随接线一并删除 |
| `TBaseObject.InSafeArea()`（`TBaseObject.cs:3544`） | 近似 `sub_76858C`，但半径 60 vs 原生 12 | **保留**（另有正当消费者 `TBaseObject.cs:1546`；半径订正属 MFLG-17，在旁支上，本分支不动） |

`m_boInSafeArea` **不是「另有正当来源」**：本轮把它唯一的非-Message 消费者
（`NativeRun3Horse.cs:253`，把它当 `MoveToMovingObject` 的 `boFlag`）改回
`m_boThroughOccupancyCache` 之后，它在全仓只剩 `Message.cs:423/425` 这一处
自读自写，以及 `Base.cs:46/839` 的声明与复位。接线落地后这四行可整体删除。

> 本分支**故意没有**把 `m_boInSafeArea` 做成转发属性指向
> `m_boThroughOccupancyCache`。理由：接线未落地前，`Message.cs` 那段会把
> `InSafeArea()`（错谓词）+ 行会战门的结果写进权威槽，比现状更糟。保持两个
> 独立存储直到接线落地，是唯一在**任意应用顺序下都安全**的形状。

---

## 6. 本分支已落地的改动

| 提交 | 内容 |
|---|---|
| `2f6c28da` | 三个取证脚本 |
| `7bb5e98a` | **MOVE-74**：新增 `TPlayObject.NativeThroughOccupancyTick.cs` 的 `NativeTickThroughOccupancyTransition()`（逐条复刻 `0x6B308B..0x6B30E1`）；修正 `NativeRun3FallbackWalk` 的 `boFlag` 源 |
| `880e6169` | **MOVE-73**：删除 4 处 mover 入口的重算，缓存改为 tick 单写点 |
| `34eedaa9` | **MOVE-39**：补齐四条 mover 腿的「清定时状态 0x17」 |

`dotnet build GameSvr/GameSvr.csproj` = **0 错误 / 15 警告**（与基线一致）。
审计工具用 `-o` 隔离目录重建后运行，三条失败项
（`TargetActionExtensionCheck` / `TimedAbilityStateGateExactCheck` /
`ExactEnvironmentMoveCheck`）在 master `58cb0ac4` 的独立 worktree 上**逐字复现**，
属既存失败，非本轮引入；`MovementCollisionCheck` / `NativeMoveGateCheck` /
`NativeTimedAbilityListCheck` 三条 PASS。

---

## 7. 需主代理执行的接线（`TPlayObject.Message.cs` 属禁改文件）

三处编辑必须**一起**落地。落地前本分支的穿透缓存无写点，会恒为 `false`
（fail-closed：安全区不可穿人）。

### 编辑 A —— 插入 tick 刷新点

原生次序是「刷新 → 挤人块」，故插在 `m_dwCheckDupObjTick` 块**之前**。
把 `TPlayObject.Message.cs:374` 这一行：

```csharp
                if ((HUtil32.GetTickCount() - m_dwCheckDupObjTick) > 3000)
```

替换为：

```csharp
                // MOVE-73/74 —— 原生 sub_6B2D38 在 0x6B308B..0x6B30E1 无条件重算穿透
                // 判定、仅变化时回写 Obj[+0x3FE] 并发 0xB05。位置在挤人块(0x6B3149)
                // 之前、且不在任何时间闸内，故这里也放在 3000ms 块之前、每 Run 一次。
                NativeTickThroughOccupancyTransition();
                if ((HUtil32.GetTickCount() - m_dwCheckDupObjTick) > 3000)
```

### 编辑 B —— 删除行会战块

把 `TPlayObject.Message.cs:418-439` 整段：

```csharp
                    if (m_MyGuild != null)
                    {
                        if (m_MyGuild.GuildWarList.Count > 0)
                        {
                            var boInSafeArea = InSafeArea();
                            if (boInSafeArea != m_boInSafeArea)
                            {
                                m_boInSafeArea = boInSafeArea;
                                RefNameColor();
                                // 0x6B308B  8B 45 FC / E8 C1 53 0B 00  call 0x768454 (InSafeArea)
                                // 0x6B3096  3A 82 FE 03 00 00  cmp al,[edx+0x3FE]   ; m_boInSafeArea
                                // 0x6B309C  74 43              je  0x6B30E1         ; unchanged -> no packet
                                // 0x6B30A3  88 91 FE 03 00 00  mov [ecx+0x3FE],dl
                                // 0x6B30A9  84 D2 / 74 1B      test dl,dl / je 0x6B30C8
                                //   true  0x6B30AD 6A 06 / 6A 01 / 6A 00 / 6A 00 / 33 C9 / 66 BA 05 0B
                                //   false 0x6B30C8 6A 06 / 6A 00 / 6A 00 / 6A 00 / 33 C9 / 66 BA 05 0B
                                // i.e. Recog=0, Param=6, Tag=(1|0), Series=0, no string.
                                SendDefMessage(Grobal2.SM_COMMON_INFORMATION,
                                    0, 6, boInSafeArea ? 1 : 0, 0, string.Empty);
                            }
                        }
                    }
```

替换为（整段删除，只留注释存档）：

```csharp
                    // MOVE-74 —— 这里原先用 InSafeArea() 驱动 0xB05，并且整块被
                    // m_MyGuild.GuildWarList.Count > 0 包住。两者都不是原生：原生
                    // 0x6B308B..0x6B30E1 由 sub_768454(穿透判定)驱动、无任何行会条件、
                    // 也不调 RefNameColor()，且不在 1000ms 闸内。已移到本方法上方的
                    // NativeTickThroughOccupancyTransition()（编辑 A）。
```

### 编辑 C —— 删除 `m_boInSafeArea`（编辑 B 之后它已无引用）

`TPlayObject.Base.cs:46` 删除：

```csharp
        public bool m_boInSafeArea = false;
```

`TPlayObject.Base.cs:839` 删除：

```csharp
            m_boInSafeArea = false;
```

### 编辑 D（可选，非本轮三条契约范围）—— 更正常量注释

`SystemModule/Grobal2.cs:1044`：

```csharp
        public const int SM_COMMON_INFORMATION = 2821; // Safe zone entry/exit notification
```

→

```csharp
        public const int SM_COMMON_INFORMATION = 2821; // Obj+0x3FE 穿透判定变迁通知（0x6B30AD/0x6B30C8）
```

注意 `AuditTools/TargetActionExtensionCheck/Program.cs:56` 断言的是
`"public const int SM_COMMON_INFORMATION = 2821;"` 这个前缀字面量，分号后的注释
不在断言内，改注释不会打红。

---

## 8. 行为差异面（改前 → 改后）

| 面 | 改前 | 改后 | 性质 |
|---|---|---|---|
| 0xB05 收包人群 | 只有「有行会且行会处于战争状态」的玩家 | 所有玩家 | 修复（原生无行会门） |
| 0xB05 触发语义 | `InSafeArea()`（boSAFE ‖ 起点表半径 60）变迁 | `sub_768454`（授予 ‖ (¬NOTHROUGH ∧ 安全区/RedHome/起点表半径 = ThroughRange)）变迁 | 修复 |
| 0xB05 求值频率 | 1000ms 闸内 | 每次 `Run()` | 修复 |
| `RM_CHANGENAMECOLOR` | 每次安全区变迁额外广播一次 | **不再广播** | 修复（原生此处无 `RefNameColor`），但这是**可见行为减少**：若某处依赖它刷新名字颜色，需另找原生依据补 |
| 穿透缓存刷新时机 | 每个 mover 入口重算（同一 Run 内多步移动会逐步跟随位置变化） | 每 Run 一次，mover 只读（同一 Run 内多步移动**共用**该 Run 开头算出的值） | 修复；玩家一步跨出安全区后，本 Run 剩余步数仍按「可穿」走，与原生一致 |
| 未接线时的穿透 | 各 mover 现算 | 恒 `false`（无写点） | **仅存在于「只应用本分支、不应用第 7 节」的中间态** |
| 定时状态 0x17 | 四条 mover 腿都不清 | 四条腿都清 | 修复 |
| `NativeRun3FallbackWalk` 的 `boFlag` | `m_boInSafeArea`（几乎恒 `false`，只有行会战玩家才被赋值） | `m_boThroughOccupancyCache` | 修复 |

---

## 9. MOVE-39 现状

| 子句 | 状态 | 说明 |
|---|---|---|
| ① 提交 X/Y（`0x7412D5`） | 早已闭合 | `TBaseObject.WalkTo` 的 `m_nCurrX/Y = nNX/nNY` |
| ② 清定时状态 0x17（`0x7412EC`） | **本轮闭合** | 见下 |
| ③ 广播 `0x2712`（`0x741315`）在落格 `sub_778EC0`（`0x741323`）**之前** | **仍未闭合（BLOCKED）** | 见 §10 |
| ④ 坐骑同伴跟随（`0x74132C` / `sub_6BBEE4`） | 前轮 `9ae059ee` 已闭合 | `OnNativeHumanWalkMoverCommitted` |

子句 ② 实测四个 mover **全部**有这一步，C# 此前一条都没有：

| mover | `mov dl,0x17` | `call 0x76B4D0` | 相对广播 |
|---|---|---|---|
| 人形 walk `sub_741224` | `0x7412E8 B2 17` | `0x7412EC E8 DF A1 02 00` | 广播**前** |
| 怪物 walk `sub_71F0F4` | `0x71F21C B2 17` | `0x71F220 E8 AB C2 04 00` | 广播**后** |
| 2 格 run `sub_76756C` | `0x767634 B2 17` | `0x767638 E8 93 3E 00 00` | 广播**前** |
| 3 格 run `sub_767694` | `0x76775E B2 17` | `0x767762 E8 69 3D 00 00` | 广播**前** |

`sub_76B4D0` 只是 `sub_7731C0` 的三指令薄壳（`push ebp / mov ebp,esp /
call 0x7731C0`）。`sub_7731C0` 的语义：`InBodyState(id)` 不成立直接返回 false；
成立则遍历 `[Self+0xDC]` 链表，按 `cmp bl,[node+1]` 匹配、改前驱 `[prev+0xE]`
摘链、`call [vmt+0x5C]` 通知丢失、`sub_764E10` 释放节点。这与 C#
`RemoveTimedAbilityInternal` 的形状一致（审计报告点名的前置条件成立）。

落点选在共享的 `TBaseObject.WalkTo` 与两条 run 腿的「位置真的变了」臂上，
与原生同为「只有成功提交才清」。怪物腿相对广播的次序与原生相反，但 `RM_WALK`
载荷只有 Dir/X/Y、不含状态位，故无可观测后果 —— 这一点已写进代码注释。

---

## 10. 仍 BLOCKED

### 10.1 MOVE-39 子句 ③：广播/落格次序

原生**四个 mover 一律**「先广播、后 `sub_778EC0`」：

```
人形  0x741315 call [edi+0xD8]   ->  0x741323 call 0x778EC0
怪物  0x71F217 call 0x765154     ->  0x71F231 call 0x778EC0
2 格  0x767645 call 0x765154     ->  0x767656 call 0x778EC0
3 格  0x76776F call 0x765154     ->  0x767780 call 0x778EC0
```

C# 把两步合进 `TBaseObject.Walk(nIdent)`（`TBaseObject.cs:4564`），顺序**相反**：
先跑格子上的 gate/event，再 `SendRefMsg`，且广播被 `if (result &&
!suppressMovementBroadcast)` 两个条件挡住。

不在本轮动它的理由（都是「动了会踩到没有原生依据的东西」）：

1. 原生 `0x741323` 之后紧接 `0x741328 mov dl,0x33` —— `sub_778EC0` 的返回值被
   **丢弃**，人形 mover 恒返回 `[ebp-6]=1`。C# 却用 `Walk()` 的返回值驱动
   **位置回滚**（`TBaseObject.cs:1349-1357`、`RollbackCommittedRunMove`）。
   把次序改对的同时必须回答「回滚该不该存在」，那是另一条契约。
2. `suppressMovementBroadcast` 服务的是跨服传送（`TryBeginCrossServerTransfer`），
   原生同一位置没有对应物。删它需要跨服传输层的独立依据。
3. `Walk()` 有 5 个调用点（`RM_WALK` ×1、`RM_RUN` ×3、`RM_TURN` ×1，含
   `RobotPlayObject`），改内部次序会同时改这 5 条腿。

**建议做法**：新开一条契约，把 `Walk()` 拆成
`SendRefMsg(nIdent, …)` + 既有的 `ProcessNativeMoveActionWithoutBroadcast()`
两步显式调用（`CompleteNativeRun3Move` 已经是这个形状且次序正确，可直接抄），
同时逐条判定回滚与跨服抑制的去留。

### 10.2 `0x6B3206` 的挤人闸（非本轮三条契约，登记备查）

原生在挤人块入口有两道闸，C# 只有第一道：

```
006B31F6  80 BA 13 07 00 00 00     cmp byte [edx+0x713],0   ; C# 的 bo2F0，已有
006B31FD  0F 84 9E 00 00 00        je  0x6B32A1
006B3206  80 BA FE 03 00 00 00     cmp byte [edx+0x3FE],0   ; 穿透缓存，C# 缺
006B320D  0F 85 8E 00 00 00        jne 0x6B32A1             ; 能穿人 -> 整段跳过
006B321F  83 F8 03 / 7C 08         cmp eax,3 / jl
006B3224  81 FB B8 0B 00 00 / 77 0D  cmp ebx,0xBB8 / ja
006B322C  83 F8 02 / 75 70         cmp eax,2 / jne
```

后三行与 `Message.cs:391-392` 的 `tObjCount>=3 && >3000 || tObjCount==2 && >10000`
一一对应，说明这确实是同一段。缺的就是 `[+0x3FE]` 那道闸。接线（编辑 A）落地后
可用一行补齐 —— 把 `Message.cs:391` 的条件外面加 `!m_boThroughOccupancyCache &&`。
本轮不提交，因为它属于另一条（尚未编号的）契约，且落点同样在禁改文件里。

同族的 `0x6661AB` / `0x6736A1` 两道闸位于怪物/宠物侧的对应逻辑，C# 侧是否有
对应实现未在本轮核查。
