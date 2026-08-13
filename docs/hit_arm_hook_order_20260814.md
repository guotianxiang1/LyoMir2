# HIT-ARM — HIT 族双臂的钩子次序、can-act 门、`0x10B` 豁免与 `0x768CFB` 归因

- 工作树 / 分支：`D:\loym2\.claude\wt3\hitarm` / `w/hitarm`，基线 `288028e3` = master
- 底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`（file off = VA − 0x400000）
- 工具：`tools/m2_disasm.py`（既有）+ 本轮新增 `tools/hitarm_xref.py` / `hitarm_dword.py` /
  `hitarm_state_census.py` / `hitarm_str.py`
- 构建：`dotnet build GameSvr/GameSvr.csproj` → **0 错 15 警**（15 条全部为基线既有，无新增）

## 0. 裁决摘要

| 命题 | 裁决 | 依据 |
|---|---|---|
| HIT 族有两条臂、钩子次序不同 | **真** | CASE1 `0x6D9ED3` < can-act `0x6D9EDF`；CASE2 `0x6D9F7D` > can-act `0x6D9F6C` |
| 两臂差异只有钩子次序一处 | **假，共三处** | 另有骑乘闸失败去向（静默 vs 0x276）与 `sub_6EC078` 选择子（`[msg+4]` vs `[msg+8]`） |
| CASE2 = 「CM_3037 / 3027」两个 ident | **假，只有 3027** | `0x6D9F4B` 的 rel32/jcc 全扫恰好 1 个来源 `0x6D850D` |
| can-act 门本端未建模 | **假，早已建模** | `TBaseObject.IsNativeCanActBlocked`(MOVE-14) + `TPlayObject` override(MOVE-15)，只是 HIT 路径从未调用 |
| CASE1 只覆盖 8 个 ident | **假，11 个** | 漏了 `0x6D851A`(3002)、`0x6D85F5`(3028)、`0x6D8610`(**3035**) |
| `0x768CFB` 未归因 | **已归因** | `sub_768CEC` = 按名 `SpaceMove` 包装器；用字面量 `"D5071~0"/11/13` 与本端钉死 |
| `0x10B` 豁免语义 | **已定性** | 唯一能触发它的入口是 CM_SPELL(3017)，`edx = word[msg+0x0A]` = 魔法号；即 magic 267 施法不破隐身 |

一句话：HIT 族的次序分歧成立，但 **can-act 门早就在本端**，缺的只是「在 HIT 路径上查询它」；
真正需要拆 case 的是 `sub_6BCE2C` 与 can-act 门的先后，以及骑乘闸失败时发不发 `0x276`。

---

## 1. HIT 族派发：逐 ident 的 CASE 归属

派发器是 `sub_6D7D68` 内的平衡比较树，每个 ident 恰好落一个叶子。相关节点全文：

```
0x6D8502  3D D3 0B 00 00     cmp eax,0xBD3          ; 3027
0x6D8507  0F 8F C9 00 00 00  jg  0x6D85D6
0x6D850D  0F 84 38 1A 00 00  je  0x6D9F4B           ; ★ CASE2 唯一入口
0x6D8513  3D BA 0B 00 00     cmp eax,0xBBA          ; 3002
0x6D8518  7F 63              jg  0x6D857D           ; -> 跳表分支
0x6D851A  0F 84 8F 19 00 00  je  0x6D9EAF           ; ★ CASE1 (3002)

0x6D857D  05 3E F4 FF FF     add eax,-0xBC2         ; 基 ident 3010
0x6D8582  83 F8 10           cmp eax,0x10
0x6D8585  0F 87 A1 36 00 00  ja  0x6DBC2C
0x6D858B  FF 24 85 92 85 6D 00  jmp [eax*4+0x6D8592]

0x6D85D6  3D 6C 0C 00 00     cmp eax,0xC6C          ; 3180
0x6D85DB  7F 65              jg  0x6D8642
0x6D85DD  0F 84 22 1E 00 00  je  0x6DA405
0x6D85E3  3D EB 0B 00 00     cmp eax,0xBEB          ; 3051
0x6D85E8  7F 31              jg  0x6D861B
0x6D85EA  0F 84 CE 1C 00 00  je  0x6DA2BE
0x6D85F0  2D D4 0B 00 00     sub eax,0xBD4          ; 累减链起点 3028
0x6D85F5  0F 84 B4 18 00 00  je  0x6D9EAF           ; ★ CASE1 (3028)
0x6D85FB  83 E8 02           sub eax,2              ; 3030
0x6D85FE  0F 84 AD 1B 00 00  je  0x6DA1B1
0x6D8604  83 E8 02           sub eax,2              ; 3032
0x6D8607  0F 84 C8 1B 00 00  je  0x6DA1D5
0x6D860D  83 E8 03           sub eax,3              ; 3035
0x6D8610  0F 84 99 18 00 00  je  0x6D9EAF           ; ★ CASE1 (3035)
0x6D8616  E9 11 36 00 00     jmp 0x6DBC2C
```

跳表 `0x6D8592` 17 项实测（`tools/hitarm_dword.py 0x6D8592:17`）：

| idx | ident | handler | | idx | ident | handler |
|---|---|---|---|---|---|---|
| 0 | 3010 | `0x6D9B65` | | 9 | 3019 | **`0x6D9EAF`** |
| 1 | 3011 | `0x6D9BD0` | | 10..13 | 3020..3023 | `0x6DBC2C` |
| 2 | 3012 | `0x6D9C7D` | | 14 | 3024 | **`0x6D9EAF`** |
| 3 | 3013 | `0x6D9CE4` | | 15 | 3025 | **`0x6D9EAF`** |
| 4..6 | 3014..3016 | **`0x6D9EAF`** | | 16 | 3026 | **`0x6D9EAF`** |
| 7 | 3017 | `0x6DA04A` | | | | |
| 8 | 3018 | **`0x6D9EAF`** | | | | |

### 1.1 归属表

| ident | 常量 | CASE | 证据 VA | 本端 case 现状 |
|---|---|---|---|---|
| 3002 | `CM_SWORD_HIT` | **1** | `0x6D851A` | 在 HIT case 内 |
| 3014 | `CM_HIT` | **1** | 跳表 `0x6D85A2` | 在 |
| 3015 | `CM_HEAVYHIT` | **1** | `0x6D85A6` | 在 |
| 3016 | `CM_BIGHIT` | **1** | `0x6D85AA` | 在 |
| 3018 | `CM_POWERHIT` | **1** | `0x6D85B2` | 在 |
| 3019 | `CM_LONGHIT` | **1** | `0x6D85B6` | 在 |
| 3024 | `CM_WIDEHIT` | **1** | `0x6D85CA` | 在 |
| 3025 | `CM_FIREHIT` | **1** | `0x6D85CE` | 在 |
| 3026 | `CM_CRSHIT` | **1** | `0x6D85D2` | 在 |
| 3028 | `CM_TWINHIT` | **1** | `0x6D85F5` | 在 |
| **3035** | `CM_HORSERUN` | **1** | `0x6D8610` | **不在**（本端另有 `ClientHorseRunXY` 臂，见 §6.1） |
| 3027 | `CM_3037` | **2** | `0x6D850D` | 在 |

`0x6D9F4B` 的全镜像 jcc/rel32 扫描恰好 1 个来源；且其前一条指令
`0x6D9F46 E9 E1 1C 00 00 jmp 0x6DBC2C` 正好在 `0x6D9F4B` 处收尾，**无落空进入**。
故 **CASE2 只有 3027 一个 ident**（前作记的「CM_3037/3027」是同一个东西的两种叫法）。

## 2. 两条臂的完整反汇编与三处差异

### CASE1 `0x6D9EAF`

```
0x6D9EAF  33 D2              xor  edx,edx           ; Ident = 0
0x6D9EB1  8B 45 FC           mov  eax,[ebp-4]
0x6D9EB4  E8 8F 8E 01 00     call 0x6F2D48          ; ① 揭示钩子
0x6D9EB9  8B 45 FC           mov  eax,[ebp-4]
0x6D9EBC  E8 F7 1F FE FF     call 0x6BBEB8          ; ② 骑乘闸
0x6D9EC1  84 C0              test al,al
0x6D9EC3  0F 85 63 1D 00 00  jne  0x6DBC2C          ;   命中 → 静默
0x6D9EC9  8B 45 CC           mov  eax,[ebp-0x34]
0x6D9ECC  0F B7 50 04        movzx edx,word [msg+4] ;   死参
0x6D9ED0  8B 45 FC           mov  eax,[ebp-4]
0x6D9ED3  E8 54 2F FE FF     call 0x6BCE2C          ; ③ 取消挂起通道  ←★ 门之前
0x6D9ED8  B2 01              mov  dl,1
0x6D9EDA  8B 45 FC           mov  eax,[ebp-4]
0x6D9EDD  8B 08              mov  ecx,[eax]
0x6D9EDF  FF 51 40           call [ecx+0x40]        ; ④ can-act 门
0x6D9EE2  84 C0              test al,al
0x6D9EE4  74 29              je   0x6D9F0F          ;   拒绝 → 0x276
0x6D9EE6  0F B7 40 06        movzx eax,word [msg+6] / push eax
0x6D9EF1  8A 40 0A / 24 07   mov al,[msg+0x0A] / and al,7 / push eax
0x6D9EFA  8B 08              mov  ecx,[msg]         ; Recog
0x6D9EFF  66 8B 50 04        mov  dx,word [msg+4]   ;   ←★ 选择子 = Ident
0x6D9F06  E8 6D 21 01 00     call 0x6EC078
0x6D9F0B  84 C0 / 75 1E      test al,al / jne 0x6D9F2D
0x6D9F0F  push0 ×4 / xor ecx,ecx / 66 BA 76 02 mov dx,0x276 / call [vmt+0x250] / jmp 0x6DBC2C
0x6D9F2D  push0 ×4 / xor ecx,ecx / 66 BA 75 02 mov dx,0x275 / call [vmt+0x250] / jmp 0x6DBC2C
```

### CASE2 `0x6D9F4B`

```
0x6D9F4B  33 D2              xor  edx,edx           ; Ident = 0
0x6D9F4D  8B 45 FC           mov  eax,[ebp-4]
0x6D9F50  E8 F3 8D 01 00     call 0x6F2D48          ; ① 揭示钩子（同）
0x6D9F55  8B 45 FC           mov  eax,[ebp-4]
0x6D9F58  E8 5B 1F FE FF     call 0x6BBEB8          ; ② 骑乘闸
0x6D9F5D  84 C0              test al,al
0x6D9F5F  0F 85 82 00 00 00  jne  0x6D9FE7          ;   命中 → 0x276  ←★ 不静默
0x6D9F65  B2 01              mov  dl,1
0x6D9F67  8B 45 FC / 8B 08   mov eax,[ebp-4] / mov ecx,[eax]
0x6D9F6C  FF 51 40           call [ecx+0x40]        ; ③ can-act 门  ←★ 提前
0x6D9F6F  84 C0              test al,al
0x6D9F71  74 74              je   0x6D9FE7          ;   拒绝 → 0x276
0x6D9F73  8B 45 CC           mov  eax,[ebp-0x34]
0x6D9F76  0F B7 50 04        movzx edx,word [msg+4] ;   死参
0x6D9F7A  8B 45 FC           mov  eax,[ebp-4]
0x6D9F7D  E8 AA 2E FE FF     call 0x6BCE2C          ; ④ 取消挂起通道  ←★ 门之后
0x6D9F82  0F B7 40 06        movzx eax,word [msg+6] / push eax
0x6D9F8D  8A 40 0A / 24 07   mov al,[msg+0x0A] / and al,7 / push eax
0x6D9F93  8B 08              mov  ecx,[msg]
0x6D9F9B  66 8B 50 08        mov  dx,word [msg+8]   ;   ←★ 选择子 = Tag
0x6D9FA2  E8 D1 20 01 00     call 0x6EC078
0x6D9FA7  84 C0 / 75 1E      test al,al / jne 0x6D9FC9
0x6D9FAB  ... 0x276 ...      / 0x6D9FC9 ... 0x275 ...
0x6D9FE7  push0 ×4 / xor ecx,ecx / 66 BA 76 02 mov dx,0x276 / call [vmt+0x250] / jmp 0x6DBC2C
```

### 三处差异

| # | 项 | CASE1 | CASE2 | 可观测性 |
|---|---|---|---|---|
| 1 | `sub_6BCE2C` 与 can-act 门 | 门**之前** `0x6D9ED3` | 门**之后** `0x6D9F7D` | 被门拒绝时：CASE1 已取消通道（发 `0x4D0/0x4D2/0xD57`），CASE2 未取消 |
| 2 | 骑乘闸失败去向 | `0x6D9EC3 jne 0x6DBC2C` 静默 | `0x6D9F5F jne 0x6D9FE7` 发 `0x276` | 客户端收不收到纠正包 |
| 3 | `sub_6EC078` 选择子 | `0x6D9EFF` `[msg+4]`=Ident | `0x6D9F9B` `[msg+8]`=Tag | 已由本端 `nParam3` 映射覆盖 |

`0x6DBC2C` 确认是静默出口（SEH 拆栈 + `jmp 0x6DBD0E` 函数尾），**不发任何包**：

```
0x6DBC2C  33 C0 / 5A / 59 / 59 / 64 89 10 / E9 D5 00 00 00  jmp 0x6DBD0E
```

## 3. can-act 门定性 —— 它是什么，本端有没有

`call [ecx+0x40]`，`dl = 1`。TPlayer VMT `0x6AC8C8`+0x40 = **`0x6E6700`**；
THumanKind VMT `0x73BC34`+0x40 = **`0x76B354`**（即基类实现本身）。

```
0x6E6700  55 / 8B EC / 53 / 56
0x6E6705  8B DA              mov  ebx,edx           ; 保存 callerArg
0x6E670D  E8 42 4C 08 00     call 0x76B354          ; 继承版
0x6E6712  84 C0 / 74 09      test al,al / je 0x6E671F   ; 基类否 → false
0x6E6716  83 BE 74 05 00 00 00  cmp dword [esi+0x574],0
0x6E671D  74 04              je   0x6E6723          ; 施法锁为 0 → true
0x6E671F  33 C0              xor  eax,eax           ; false
0x6E6723  B0 01              mov  al,1              ; true
```

```
0x76B35F  E8 44 7A 00 00     call 0x772DA8          ; = `8A 40 74 / C3` 即 byte[Self+0x74] m_boDeath
0x76B366  75 41              jne  0x76B3A9          ; 死 → false
0x76B368  B2 1D / E8 ...     state 0x1D (29)  → jne false
0x76B375  B2 01 / E8 ...     state 0x01 (1)   → jne false
0x76B382  B2 1A / E8 ...     state 0x1A (26)  → jne false
0x76B38F  B2 18 / E8 ...     state 0x18 (24)
0x76B398  84 D8              test al,bl             ; ★ 只在 callerArg≠0 时生效
0x76B39A  75 0D              jne  0x76B3A9
0x76B39C  B2 3E / E8 ...     state 0x3E (62)  → je  true
0x76B3A9  33 C0 (false) / 0x76B3AD B0 01 (true)
```

**定性：它不是冷却、不是僵直计时器，而是一条纯状态阶梯** —— 死亡 + 五个 bodyState
（29 / 1 / 26 / 24 / 62，其中 24 只在调用方传非零时生效）+ TPlayObject 追加的
强制位移施法锁 `[Self+0x574]`。返回 **true = 可以行动**。

**本端已有等价物，无需新建**：

- `TBaseObject.IsNativeCanActBlocked(int callerArg)`（`TBaseObject.cs:613`，MOVE-14）
  = `sub_76B354` 逐项，极性相反（返回 true = 被拦）
- `TPlayObject.IsNativeCanActBlocked` override（`TPlayObject.NativeCanAct.cs:8`，MOVE-15）
  = `sub_6E6700`，追加 `m_nNativeForcedMoveRemaining != 0`

HIT 臂传 `dl = 1`，故对应 `IsNativeCanActBlocked(1)`（state 0x18 生效）。
**缺的只是「在 HIT 路径上查询它」** —— 本端 `ClientHitXY` 与 HIT case 都没调过。

## 4. 骑乘闸 `sub_6BBEB8`（复核既有结论，无改动）

```
0x6BBEBE  B2 33 / E8 99 6A 0B 00  mov dl,0x33 / call 0x772960 → jne true
0x6BBECB  B2 34 / E8 8C 6A 0B 00  mov dl,0x34 / call 0x772960 → jne true
0x6BBED8  33 C0                    false
```
= `HasState(51) || HasState(52)`，与本端 `IsNativeHitBlockedByMountState()` 一致（MINE-49）。

## 5. `sub_6F2D48` —— 带 `0x10B` 豁免的组合揭示钩子

### 5.1 全文（35 字节）

```
0x6F2D48  55 / 8B EC / 53 / 56
0x6F2D4D  8B F2              mov  esi,edx           ; Ident
0x6F2D4F  8B D8              mov  ebx,eax           ; Self
0x6F2D51  8B C3              mov  eax,ebx
0x6F2D53  E8 CC 00 08 00     call 0x772E24          ; ★ 无条件
0x6F2D58  81 FE 0B 01 00 00  cmp  esi,0x10B
0x6F2D5E  74 07              je   0x6F2D67          ; ★ 267 豁免
0x6F2D60  8B C3              mov  eax,ebx
0x6F2D62  E8 59 15 08 00     call 0x7742C0
0x6F2D67  5E / 5B / 5D / C3
```

即 `sub_772E24` 永远跑；只有 `sub_7742C0`（隐身 0x40）被 `0x10B` 豁免。

### 5.2 `sub_772E24` = `sub_7742C0` 的孪生体（隐藏态 0x3C）

```
0x772E3A  B2 3C / E8 1D FB FF FF   mov dl,0x3C / call 0x772960   ; InBodyState(0x3C)
0x772E43  84 C0 / 74 4D            未置位 → 整体空操作
0x772E47  B2 3C / E8 80 86 FF FF   mov dl,0x3C / call 0x76B4D0   ; 清位
0x772E52  E8 61 00 00 00           call 0x772EB8                 ; ★ 比 0x7742C0 多的一步
0x772E57  84 C0 / 75 39            仍被授予穿透 → 不重显
0x772E5B  mov eax,[Self+0x12C] / push   ; nParam1 = X
0x772E62  mov eax,[Self+0x130] / push   ; nParam2 = Y
0x772E69  6A 00                          ; nParam3
0x772E72  FF 91 90 00 00 00  call [vmt+0x90]   ; GetShowName
0x772E7C  6A 01                          ; boFlag = 含自己
0x772E80  8A 8B 54 01 00 00  mov cl,[Self+0x154]  ; wParam = 朝向
0x772E86  66 BA 11 27        mov dx,0x2711        ; RM_TURN
0x772E8E  FF 93 D8 00 00 00  call [vmt+0xD8]
```

- `0x772E4B` 的 `0x76B4D0` 是两指令桩：`55 8B EC / E8 E8 7C 00 00 call 0x7731C0 / 5D C3`，
  即 MOVE-11 已逐项对齐过的 `RemoveTimedAbilityInternal`。
- `0x772E52` 的 `sub_772EB8` = `byte[Self+0x2E2](m_boObMode) || InBodyState(0x3C)`，
  本端逐字节已在位：`TBaseObject.HasNativeCellPassThroughGrant()`（MOVE-33）。
- `0x772E5B..0x772E8E` 与 `sub_7742C0` 的 `0x7742EC..0x77431F` **逐指令同形**，
  故 C# 用同一句 `SendRefMsg(RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, 0, GetShowName())`。

**state 0x3C = 「隐藏」**：唯一原生置位点 `sub_772DD0` 先经 `[vmt+0xE0]` 广播 ident `0x1E`
再挂定时节点（`0x772DF2..0x772E18`），且持有者免于占格（`0x765DC2` 查 `sub_772EB8`）。
`sub_772DD0` 的 rel32 全扫只有 1 个调用者 `0x78B37D`（`0x78B376 mov edx,0x1E`，
前置 `0x78B36D call 0x404828` 的类型判定），属引擎 API 导出，本端未建模。
**因此本端目前没有任何路径置 0x3C，`BreakNativeHideOnAction()` 是带守卫的空操作** ——
但它在逐字节忠实的路径上，一旦补上置位端即自动生效。

### 5.3 `0x10B` 豁免的语义

`sub_6F2D48` 的 rel32 全扫恰好 3 个调用点：

| VA | 归属 | 传入 edx |
|---|---|---|
| `0x6D9EB4` | HIT CASE1 | `0x6D9EAF 33 D2` 字面 0 |
| `0x6D9F50` | HIT CASE2 | `0x6D9F4B 33 D2` 字面 0 |
| `0x6DA054` | **CM_SPELL(3017)** `0x6DA04A` | `0x6DA04D 0F B7 50 0A movzx edx,word[msg+0x0A]` |

**两条 HIT 臂传字面 0，豁免在 HIT 路径上永远不触发。** 唯一能触发它的是 CM_SPELL，
而 `[msg+0x0A]` = Series = **客户端要施放的魔法号**。

`0x10B` = 267。本端已建模：`TBaseObject.NativeSkill267.cs`
（外层臂 `0x6BCAB2 call 0x774054`，冷却键**同一个字面量 `0x10B`**），
效果是给自己挂 bodyState `0x46` 15 秒（`0x3A98`），若已学 magic `0x104` 再挂 `0x41`。
`0x46` 的全镜像普查：读点 `0x771169` / `0x7714BA` / `0x7716D0`，清点
`0x771197` / `0x7714E2` / `0x771735`，全部落在伤害结算段 `0x7711xx..0x7717xx`
（例：`0x771169 B2 46 / E8 F0 17 00 00 call 0x772960` → 命中后 `0x771181 mov dx,0x108`
经 `[vmt+0xE8]`，未消耗则 `0x771197 B2 46 / E8 22 20 00 00 call 0x7731C0` 清位），
唯一置位点 `0x7740E5` 就在 267 的激活体里。

**语义：267 是一次性的自我增益，是唯一一个「隐身状态下可以施放而不暴露」的法术。**
注意它**不**豁免隐藏态 0x3C —— `sub_772E24` 在比较之前就跑完了。

## 6. `0x768CFB` 归因（任务三）

`sub_768CEC(eax = Self, edx = sMapName, ecx = nX, 栈 3 参, ret 0xC)`：

```
0x768CF2  89 4D FC           mov  [ebp-4],ecx        ; nX
0x768CF5  8B F2              mov  esi,edx            ; sMapName
0x768CF7  8B D8              mov  ebx,eax            ; Self
0x768CFB  E8 C0 B5 00 00     call 0x7742C0           ; ★ 入口第一件事：破隐身
0x768D00  A1 0C 66 7D 00     mov  eax,[0x7D660C] / mov eax,[eax]
0x768D09  E8 C2 D5 F2 FF     call 0x6962D0           ; 按名查地图
0x768D0E  85 C0 / 0F 95 C2 / 80 F2 01 / 22 55 08     ; (未找到) && byte[ebp+8]
0x768D24  E8 FF D4 F2 FF     call 0x696228           ; 第二次查表
0x768D2B  74 1D              je   0x768D4A
0x768D42  FF 93 C0 01 00 00  call [vmt+0x1C0]        ; SpaceMove(envir, X, Y, ...)
0x768D5D  E8 22 28 E9 FF     call 0x5FB584           ; 查不到 → 跨服转发
```

**结论：`sub_768CEC` = `TBaseObject.SpaceMove` 的「按地图名」重载**，
`[vmt+0x1C0]`（TPlayer = `0x6BD294`）是「按 envir」重载。本端结构完全一致：
`TBaseObject.SpaceMove(string, ...)` → `SpaceMove(FindMap(sMap), ...)`。

对应关系用字面量钉死 —— `0x64D203` 处：

```
0x64D1F8  6A 0D              push 0xD                ; nY = 13
0x64D1FA  6A 00 / 6A 00      push 0 / push 0
0x64D1FE  B9 0B 00 00 00     mov  ecx,0xB            ; nX = 11
0x64D203  BA 2C D2 64 00     mov  edx,0x64D22C       ; AnsiString(len=7) = "D5071~0"
0x64D208  8B C3 / 0x64D20A   call 0x768CEC
```
即 `SpaceMove("D5071~0", 11, 13)` —— 与本端 `TPlayObject.NativeMagicTower.cs:172` 的
`SpaceMove("D5071~0", 11, 13, 0)` 逐参一致。脚本臂也在内（`0x743BB9`，
从记录取 `[+0x10]`=图名 / `[+4]`=X / `[+8]`=Y，`0x743BA4 6A 01` 开第二次查表）。
`sub_768CEC` 的 rel32 全扫共 8 处：`0x64D20A / 0x64D2BA / 0x65777F / 0x6B2FF6 /
0x6CED48 / 0x6CEDD2 / 0x6CEF99 / 0x743BB9`。

**处置：已接线**（`GameSvr/Actors/TBaseObject.cs` 的 `SpaceMove(string,...)` 加一句
`BreakNativeStealthOnAction();`）。按 envir 的重载不加 —— `sub_7742C0` 的 rel32
全扫只有 4 处，这一族里只有 `0x768CFB` 一个。

### 6.1 顺带查出的既有偏差（未处置，仅报告）

**ident 3035 在原生走 HIT CASE1**，而本端 `Grobal2.CM_HORSERUN = 3035` 被路由到
`ClientHorseRunXY`（`TPlayObject.Message.cs:1539`）。旁证不止派发树一处：
`sub_6EC078` 自己的窗口就是 3002..3035（`0x6EC15D add eax,-0xBBA` /
`0x6EC162 cmp eax,0x21` = 33），索引表 `0x6EC178[33] = 0x09`
（位于 `0x6EC199`），选中 `0x6EC1BE` 槽 = `0x6EC29C`：

```
0x6EC29C  33 C0 / 8A 45 08 / 50   push byte[ebp+8]        ; 方向
0x6EC2A2  66 B9 F9 03             mov  cx,0x3F9           ; 动作码 1017
0x6EC2A6  33 D2 / 8B C3
0x6EC2AA  E8 F9 44 08 00          call 0x7707A8
```
与 `CM_CRSHIT(3026)` 的 `0x6EC2B1 mov cx,0x3FA`（1018）同族 —— 3035 在原生是一个
**攻击动作**，不是跑步。本轮不动它：改这条要连带 `UsrEngn` 的表头映射、
`ClientHorseRunXY` 的存废与 `sub_7707A8` 动作码 1017 的整臂取证，超出本任务契约。
**标记为待专项。**

## 7. 本轮落地（已提交，均未接线到 `TPlayObject.Message.cs`）

| 提交 | 文件 | 内容 |
|---|---|---|
| `bd278837` | `tools/hitarm_{xref,dword,state_census}.py` | 取证工具 |
| `bfd215e9` | `GameSvr/Actors/TBaseObject.NativeHideBreak.cs`（新） | `BreakNativeHideOnAction()` = `sub_772E24`；`NotifyNativeActionReveal(int)` = `sub_6F2D48` |
| `bfd215e9` | `GameSvr/Players/TPlayObject.NativeHitArmGates.cs`（新） | `RunNativeHitArmGates(int)` = 两臂前置闸阶梯，按 ident 分叉 |
| `4029ef38` | `GameSvr/Actors/TBaseObject.cs` | `SpaceMove(string,...)` 加 `0x768CFB` 的破隐身（**已接线**） |
| `4029ef38` | `tools/hitarm_str.py` | 字面量取证 |

`RunNativeHitArmGates` 的三值返回：

```csharp
internal const int NativeHitGateProceed = 0;   // 走 ClientHitXY
internal const int NativeHitGateConsume = 1;   // 0x6D9EC3 jne 0x6DBC2C，静默
internal const int NativeHitGateRefuse  = 2;   // 0x6D9EE4 / 0x6D9F5F / 0x6D9F71 → 0x276
```

`Refuse` 不自行发包：原生两条拒绝边与 `sub_6EC078` 失败**共用同一个 `0x276` 块**
（CASE1 的 `0x6D9EE4` 与 `0x6D9F0D` 同落 `0x6D9F0F`），所以调用方必须把 `Refuse`
导进它已有的「`ClientHitXY` 返回 false」分支。`dwDelayTime` 在整个 switch 内保持
`TPlayObject.Message.cs:934` 的初值 0（与 MOVE-90 给 CM_SPELL 用的短路同理），
故短路后自然落进 `dwDelayTime == 0` 那一支。

`SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、
`GameSvr/UsrSystem/UsrEngn.cs` 全程未触碰。

---

## 8. 接线需求（须主代理落地，两处，均在 `GameSvr/Players/TPlayObject.Message.cs`）

> 下面两块**都已在本工作树里临时施加、`dotnet build GameSvr/GameSvr.csproj` 实测
> 0 错 15 警（与基线同），随后 `git checkout --` 原样撤回**，故 `TPlayObject.Message.cs`
> 在本分支上零改动。主代理照抄即可，不会遇到编译问题。

### 8.1 HIT 族 —— 拆 case 的完整方案

**不需要把 case 标签拆开。** 三处差异里有两处（钩子次序、骑乘闸去向）已经收进
`RunNativeHitArmGates`，第三处（选择子）本来就已由 `nParam3` 三元表达式覆盖。
`case` 标签列表原样保留，只替换其后的 **1701–1716 行**（从 `// MINE-49:` 注释开始，
到 `if (ClientHitXY(` 那一整个条件表达式的收尾行为止）。

**被替换的原文**（现 1701–1716）：

```csharp
                    // MINE-49: 骑乘态攻击门。原生 HIT 派发器 sub_6D9EAF 在 ClientHitXY(0x6EC078)
                    // 之前先调 sub_6BBEB8：0x6D9EBC call 0x6BBEB8 / 0x6D9EC3 jne 0x6DBC2C
                    // == HasState(51)||HasState(52) → 放弃整个 HIT case、不发任何包（静默消费）。
                    // 覆盖本 arm 全部 ident（CASE1 3002/3014/3015/3016/3018/3019/3024/3025/3026/3028
                    // 命中 jne 0x6DBC2C 静默；CASE2 CM_3037 原生改跳 0x6D9FE7 先发 0x276 更正包——
                    // 因该更正包精确载荷未取证，按 fail-closed 统一对齐 0x6DBC2C 静默放弃，
                    // 记为有界偏差：CM_3037 骑乘态下少发一个 SM_ACT_FAIL 更正包）。
                    if (IsNativeHitBlockedByMountState())
                    {
                        break;
                    }
                    if (ClientHitXY(
                            ProcessMsg.wIdent == Grobal2.CM_3037
                                ? ProcessMsg.nParam3
                                : ProcessMsg.wIdent,
                            ProcessMsg.nParam1, ProcessMsg.nParam2, (byte)(ProcessMsg.wParam & 7), ProcessMsg.boLateDelivery, ref dwDelayTime))
```

**替换为**：

```csharp
                    // HIT-ARM: 原生 HIT 族是**两条**臂，前置闸的次序按 ident 分叉，
                    // 整条阶梯移进 RunNativeHitArmGates（TPlayObject.NativeHitArmGates.cs）：
                    //   CASE1 0x6D9EAF（3002/3014/3015/3016/3018/3019/3024/3025/3026/3028）
                    //     0x6D9EB4 call 0x6F2D48 揭示钩子 → 0x6D9EBC 骑乘闸(命中静默)
                    //     → 0x6D9ED3 call 0x6BCE2C 取消通道 → 0x6D9EDF can-act 闸
                    //   CASE2 0x6D9F4B（3027 = CM_3037，jcc 全扫证明它是唯一入口）
                    //     0x6D9F50 揭示钩子 → 0x6D9F58 骑乘闸(命中发 0x276)
                    //     → 0x6D9F6C can-act 闸 → 0x6D9F7D 取消通道
                    // 即 sub_6BCE2C 在 CASE1 排在 can-act 之前、CASE2 之后，单点插入
                    // 无法同时忠实，故由 helper 按 ident 分叉。
                    // MINE-49 的骑乘闸并入该阶梯；CM_3037 骑乘态下原生走 0x6D9FE7 的
                    // 0x276，现按 Refuse 落到下方 dwDelayTime==0 分支，旧注释登记的
                    // 「CM_3037 少发一个 SM_ACT_FAIL 更正包」有界偏差随之消除。
                    // can-act 闸 0x6D9EDF/0x6D9F6C = `B2 01 mov dl,1` + `FF 51 40
                    // call [ecx+0x40]` = TPlayer VMT 0x6AC8C8+0x40 = 0x6E6700；本端
                    // IsNativeCanActBlocked(1) 早已在位（MOVE-14/15），只是从未在
                    // HIT 路径上被查询过。
                    // Refuse 不自行发包：原生两条拒绝边与 sub_6EC078 失败共用同一个
                    // 0x276 块（0x6D9EE4 与 0x6D9F0D 同落 0x6D9F0F），故复用下方
                    // 「ClientHitXY 返回 false」的分支；dwDelayTime 在本 switch 内
                    // 保持 :934 的初值 0（与 MOVE-90 的 CM_SPELL 短路同理）。
                    int nHitGate = RunNativeHitArmGates(ProcessMsg.wIdent);
                    if (nHitGate == NativeHitGateConsume)
                    {
                        break;
                    }
                    if (nHitGate == NativeHitGateProceed && ClientHitXY(
                            ProcessMsg.wIdent == Grobal2.CM_3037
                                ? ProcessMsg.nParam3
                                : ProcessMsg.wIdent,
                            ProcessMsg.nParam1, ProcessMsg.nParam2, (byte)(ProcessMsg.wParam & 7), ProcessMsg.boLateDelivery, ref dwDelayTime))
```

1717 行起（`{ m_dwActionTick = ...` 一直到 1771 的 `break;`）**一字不动**。

> `int nHitGate` 声明在 case 段内，作用域是整个 switch。全文件无同名局部，编译已验。
> 若嫌风格不一致，可改为在 `TPlayObject.Message.cs:935` 的 `int nMsgCount;` 旁加
> `int nHitGate;`，此处去掉 `int`。两种写法等价。

**次序不能动**：`RunNativeHitArmGates` 必须整体排在 `ClientHitXY` 之前，
且 `NativeHitGateConsume` 的 `break` 必须在 `ClientHitXY` 之前 —— 原生
`0x6D9EC3` 静默出口在 `0x6D9F06` 之前。

### 8.2 CM_SPELL(3017) —— 带 `0x10B` 豁免的揭示钩子

**位置**：`case Grobal2.CM_SPELL:`（现 1826），**紧跟 case 标签、在 MOVE-90 那段注释与
`if (!NativeNoMagicMapForbidsSpell()` 之前**，插入：

```csharp
                case Grobal2.CM_SPELL:
                    // HIT-ARM: 原生 3017 臂 0x6DA04A 的第一件事是
                    //   0x6DA04D  0F B7 50 0A  movzx edx,word [msg+0x0A]   ; Series
                    //   0x6DA054  E8 EF 8C 01 00  call 0x6F2D48
                    // 即带 0x10B 豁免的揭示钩子，排在 0x6DA059 的 state 0x33 骑乘闸
                    // 之前。[msg+0x0A] 是施法魔法号，UsrEngn 的 default 臂把 Series
                    // 放进 SendMsg 的第 3 参 wParam（同一个值下面 ClientSpellXY 当
                    // nKey 收），所以这里传 ProcessMsg.wParam。
                    // 唯有 magic 267(0x10B) 不破隐身 0x40；隐藏态 0x3C 照破
                    // （0x6F2D53 排在 0x6F2D58 的比较之前）。
                    NotifyNativeActionReveal(ProcessMsg.wParam);
                    // MOVE-90: NOMAGIC 地图禁施法门。原生 CM_SPELL 派发器 sub_6D7D68 在调
                    ...（以下原文不动）
```

**不要**给 CM_WALK / CM_TURN / CM_SITDOWN / CM_HORSERUN / CM_RUN3 加任何一个 ——
`sub_6F2D48` 的 rel32 全扫恰好 3 处，`sub_7742C0` 恰好 4 处，都不含这些臂。

## 9. 明确未做 / 仍 UNPROVEN

1. **ident 3035 的臂别冲突**（§6.1）。派发证据已足，但改动要连带 `UsrEngn` 表头映射、
   `ClientHorseRunXY` 的存废与 `sub_7707A8` 动作码 `0x3F9` 的整臂取证 —— 待专项。
2. **CM_SPELL 的 can-act 门是反向的**：`0x6DA0A4 call [vmt+0x40]` / `0x6DA0A9 jne 0x6DA122`
   —— 可以行动时跳去 NOMAGIC 检查，**不能**行动时反而落到 `0x6DA0AB`
   `mov dx,word[msg+0x0A]` / `call 0x7725FC`，命中则照样 `0x6BC510` 施法。
   即存在一张「僵直中仍可施放」的法术白名单 `sub_7725FC`。本轮只报，不动。
3. **`0x276` 载荷的精确形状**。原生 `0x6D9F0F` 是 `[vmt+0x250]` 带 `Recog=0` 与四个零；
   本端失败分支发的是 `RM_MOVEFAIL` + `SM_ACT_FAIL(wIdent, 0,0,0)`。这是全仓既有的
   近似，本轮沿用未改（新增的 can-act 拒绝走同一支，与原生「两条拒绝边共用一个块」
   的形状一致）。
4. **state 0x3C 没有置位端**。`BreakNativeHideOnAction()` 目前是带守卫的空操作，
   直到 `sub_772DD0` / 其调用者 `sub_78B35C` 被建模才会生效（§5.2）。
5. `[vmt+0xE0]` / `[vmt+0xD8]` 的 `boFlag` 形参本端 `SendRefMsg` 仍未建模 —— 全仓既有近似，
   本轮用到的两个站点（`0x77430D` 与 `0x772E7C`）都是 `push 1`，与 C# 的天然行为相符。
