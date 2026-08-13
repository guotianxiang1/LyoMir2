# ID3035 — ident 3035 是攻击动作，不是骑乘跑

- 工作树 / 分支：`D:\loym2\.claude\wt3\id3035` / `w/id3035`，基线 `e222a2cb` = master
  （任务书写的 `cd8d7cf2` 已被 master 吸收，`git merge-base --is-ancestor` 为真）
- 底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`（file off = VA − 0x400000）
- 新增工具：`tools/id3035_dispatch_map.py`（派发树全量仿真器，精确 ZF/CF/SF/OF）
- 构建：`dotnet build GameSvr/GameSvr.csproj -o <独立临时目录>` → **0 错 15 警**（与基线同，无新增）

## 0. 裁决摘要

| 命题 | 裁决 | 关键证据 |
|---|---|---|
| 3035 派进 HIT CASE1 `0x6D9EAF` | **确认**，与前作一致 | `0x6D8610 0F 84 99 18 00 00 je 0x6D9EAF`；全树仿真复算 |
| `sub_6EC078` 窗口 3002..3035 | **确认** | `0x6EC15D add eax,-0xBBA` / `0x6EC162 cmp eax,0x21` |
| 索引 33 → arm 9 → 动作码 `0x3F9` | **确认** | `0x6EC178[33] = 0x09`；`0x6EC19A[9] = 0x6EC29C`；`66 B9 F9 03` |
| 原生骑乘跑是哪个 ident | **`CM_RUN3 = 4108`** | `0x6D9D99 B2 33 / call 0x772960`，未骑乘直接发 `0x276` |
| 本端 `CM_HORSERUN = 3035` 来源 | **上游 LyoMir2 原样继承** | baseline `d5d00744`；`staging/upstream-LyoMir2/.../Grobal2.cs:607` |
| 该常量名有原生依据吗 | **无。名字 INVENTED，值本身不是** | 上游 Delphi 客户端头自注 `//------------未知消息码` |
| 动作码 1017 是什么 | **一次真攻击**，射程 2 格 | `sub_772388` 唯一调用者是 1017 臂；`0x7723A5 push 2` |
| 1017 与 1018(`CM_CRSHIT`) 的关系 | **同族、相邻码，但 1018 是空壳** | `sub_771BB8 = 55 8B EC 33 C0 5D C2 04 00` |
| 本端现状 | **路由级失真，且是一个可利用的加速漏洞** | `ClientHorseRunXY → HorseRunTo` 无任何骑乘态门 |

一句话：前作 §6.1 的两条读数**全部复核通过**，而且比它说的更严重 ——
本端不只是"把攻击当成了跑步"，而是把它接到了一条**连坐骑都不检查的三格位移**上。

---

## 1. 独立复核（一）：3035 的派发归属

### 1.1 为什么要重做而不是照抄

派发器 `sub_6D7D68` 的 case 树混了三种节点形态：跳表、`cmp`+有符号 `jg`、
以及**累减链 + 无符号 `jb` 区间测试**。手工阅读极易读错 —— 同一棵树上
`docs/eqv_shard20_20260814.md:49` 就得出过完全相反的结论：

> 附注：`CM_HORSERUN=3035` 跳表 idx25 越界（=垃圾 `0x18B484`，非 handler）
> → **3035 非本引擎原生 opcode**

那条附注把 3035 硬套进 `0x6D8592` 那张 base=3010 的 17 项跳表（3035−3010 = 25 越界），
但 3035 根本不走跳表，它走的是 `0x6D85F0` 起的累减链。**该附注是错的，本报告推翻它。**

所以本轮不手读，改为**按真实字节整树仿真**：`tools/id3035_dispatch_map.py` 从
树根单步执行到控制流离开"树指令词汇表"为止，落点即 handler。

### 1.2 树根：`eax` 确实是 Ident

```
0x6D803D  8B C7              mov  eax,edi              ; edi = movzx si, si=[ebp+8]
...
0x6D805C  8B 45 CC           mov  eax,[ebp-0x34]       ; TDefaultMessage
0x6D805F  0F B7 40 04        movzx eax,word [eax+4]    ; ★ eax = Ident
0x6D8063  3D D6 0C 00 00     cmp  eax,0xCD6            ; 3286
0x6D8068  0F 8F 26 06 00 00  jg   0x6D8694
0x6D806E  0F 84 E9 25 00 00  je   0x6DA65D
0x6D8074  3D 5C 04 00 00     cmp  eax,0x45C            ; 1116
0x6D8079  0F 8F 5F 02 00 00  jg   0x6D82DE
```

字节：`0x6D805C: 8B45CC0FB74004`，`0x6D8074: 3D5C0400000F8F5F020000`。

> 注：`0x6D82DE` 才是指令首。线性反汇编从 `0x6D82DD` 起会错位成
> `00 3D 4D 05 00 00 add byte [0x54D],bh` —— 那个 `00` 是前一张跳表末项的高字节。
> 前作报告写的 `0x6D82DD` 是同一处的错位读法，结论不受影响。

### 1.3 3035 的完整路径（仿真实录）

```
$ python tools/id3035_dispatch_map.py 3035
=== ident 3035 (0xBDB) -> handler 0x6D9EAF ===
0x6D805F  0FB74004        movzx eax, word ptr [eax + 4]   ; eax=3035
0x6D8063  3DD60C0000      cmp eax, 0xcd6                  ; 3035 < 3286, jg 不取
0x6D8074  3D5C040000      cmp eax, 0x45c                  ; 3035 > 1116
0x6D8079  0F8F5F020000    jg 0x6d82de                     ; ★ 取
0x6D82DE  3D4D050000      cmp eax, 0x54d                  ; 3035 > 1357
0x6D82E3  0F8F19020000    jg 0x6d8502                     ; ★ 取
0x6D8502  3DD30B0000      cmp eax, 0xbd3                  ; 3035 > 3027
0x6D8507  0F8FC9000000    jg 0x6d85d6                     ; ★ 取
0x6D85D6  3D6C0C0000      cmp eax, 0xc6c                  ; 3035 < 3180
0x6D85DB  7F65            jg 0x6d8642                     ; 不取
0x6D85DD  0F84221E0000    je 0x6da405                     ; 不取
0x6D85E3  3DEB0B0000      cmp eax, 0xbeb                  ; 3035 < 3051
0x6D85E8  7F31            jg 0x6d861b                     ; 不取
0x6D85EA  0F84CE1C0000    je 0x6da2be                     ; 不取
0x6D85F0  2DD40B0000      sub eax, 0xbd4                  ; eax = 3035-3028 = 7
0x6D85F5  0F84B4180000    je 0x6d9eaf                     ; 不取（3028 走这条）
0x6D85FB  83E802          sub eax, 2                      ; eax = 5   （3030）
0x6D85FE  0F84AD1B0000    je 0x6da1b1                     ; 不取
0x6D8604  83E802          sub eax, 2                      ; eax = 3   （3032）
0x6D8607  0F84C81B0000    je 0x6da1d5                     ; 不取
0x6D860D  83E803          sub eax, 3                      ; eax = 0   （3035）
0x6D8610  0F8499180000    je 0x6d9eaf                     ; ★★ 取 —— HIT CASE1
```

累减链原字节 `0x6D85F0`：

```
2D D4 0B 00 00  0F 84 B4 18 00 00  83 E8 02  0F 84 AD 1B 00 00
83 E8 02  0F 84 C8 1B 00 00  83 E8 03  0F 84 99 18 00 00  E9 11 36 00 00
```

**裁决：3035 → `0x6D9EAF`（HIT CASE1）。与前作完全一致。**

### 1.4 全量交叉校验

对 ident 0..0x3FFF 逐个仿真，命中 311 个 ident / 300 个 handler。`0x6D9EAF` 的完整
ident 集合：

```
0x6D9EAF  3002 3014 3015 3016 3018 3019 3024 3025 3026 3028 3035
0x6D9F4B  3027
```

恰好 11 + 1，与前作 §1.1 归属表逐项吻合。跳表 `0x6D8592` 17 项也重新 dump 过，
与前作表格逐槽相同。

> 仿真器最初把 `jg`/`ja`/`jb` 都压成一个 "greater" 位。这在主派发树上碰巧无害
> （复算证明 311/300 一字不差），但在 §4.3 的技能槽 switch 上会把
> `sub eax,4` + `jb` 的四值区间误判成"≠0"，虚报出 175 个 magic id。
> 已改为完整 ZF/CF/SF/OF 模型后重跑，两处结论都在下面用的是精确版。

## 2. 独立复核（二）：`sub_6EC078` 的窗口与 arm 9

```
0x6EC15A  0F B7 C7              movzx eax,di              ; di = 选择子（CASE1 = Ident）
0x6EC15D  05 46 F4 FF FF        add   eax,0xFFFFF446      ; = -0xBBA = -3002
0x6EC162  83 F8 21              cmp   eax,0x21            ; 33
0x6EC165  0F 87 5B 01 00 00     ja    0x6EC2C6            ; 越界 → 默认臂
0x6EC16B  8A 80 78 C1 6E 00     mov   al,byte [eax+0x6EC178]   ; 字节索引表
0x6EC171  FF 24 85 9A C1 6E 00  jmp   dword [eax*4+0x6EC19A]   ; dword 跳表
```

字节 `0x6EC15A: 0FB7C70546F4FFFF83F8210F875B0100008A8078C16E00FF24859AC16E`。

**窗口 = 3002..3035，上界正好落在 3035。**

字节索引表 `0x6EC178`（34 项）原文：

```
01 00 00 00 00 00 00 00 00 00 00 00 02 03 04 00 05 06 00 00
00 00 07 08 0A 00 0B 00 00 00 00 00 00 09
                                       ^^ idx 33 = ident 3035 → arm 9
```

dword 跳表 `0x6EC19A`（12 槽）：

| arm | 槽 VA | 目标 | 动作码 | ident |
|---|---|---|---|---|
| 0 | `0x6EC19A` | `0x6EC2C6` | （无动作） | 3003..3013 / 3017 / 3020..3023 / 3027 / 3029..3034 |
| 1 | `0x6EC19E` | `0x6EC1CA` | `0x3F7` 1015 | 3002 `CM_SWORD_HIT` |
| 2 | `0x6EC1A2` | `0x6EC1E2` | `0x400` 1024 若 `[+0x72]==3`，否则 `0x3E8` 1000 | 3014 `CM_HIT` |
| 3 | `0x6EC1A6` | `0x6EC218` | `0x3E9` 1001 | 3015 `CM_HEAVYHIT` |
| 4 | `0x6EC1AA` | `0x6EC230` | `0x3EA` 1002 | 3016 `CM_BIGHIT` |
| 5 | `0x6EC1AE` | `0x6EC248` | `0x3EB` 1003 | 3018 `CM_POWERHIT` |
| 6 | `0x6EC1B2` | `0x6EC25D` | `0x3EC` 1004 | 3019 `CM_LONGHIT` |
| 7 | `0x6EC1B6` | `0x6EC272` | `0x3ED` 1005 | 3024 `CM_WIDEHIT` |
| 8 | `0x6EC1BA` | `0x6EC287` | `0x3EF` 1007 | 3025 `CM_FIREHIT` |
| **9** | **`0x6EC1BE`** | **`0x6EC29C`** | **`0x3F9` 1017** | **3035** |
| 10 | `0x6EC1C2` | `0x6EC2B1` | `0x3FA` 1018 | 3026 `CM_CRSHIT` |
| 11 | `0x6EC1C6` | `0x6EC2D7` | （直接落公共尾） | 3028 `CM_TWINHIT` |

arm 9 与 arm 10 只差一个立即数：

```
0x6EC29C  33 C0 / 8A 45 08 / 50   ; push 方向
0x6EC2A2  66 B9 F9 03             ; mov cx,0x3F9 = 1017
0x6EC2A6  33 D2 / 8B C3
0x6EC2AA  E8 F9 44 08 00          ; call 0x7707A8
—— 字节 33C08A45085066B9F90333D28BC3E8F9440800

0x6EC2B1  33C08A45085066B9FA0333D28BC3E8E4440800   ← 同形，仅 F9→FA
```

**裁决：arm 9 = `0x6EC29C`，动作码 `0x3F9` = 1017。与前作完全一致。**

## 3. 反向求证：原生骑乘跑 = `CM_RUN3` = 4108

### 3.1 移动族全表（仿真产出，非推断）

| ident | 本端常量 | handler | 性质 |
|---|---|---|---|
| 3010 | `CM_TURN` | `0x6D9B65` | 转身 |
| 3011 | `CM_WALK` | `0x6D9BD0` | 走 |
| 3012 | `CM_SITDOWN` | `0x6D9C7D` | 坐 |
| 3013 | `CM_RUN` | `0x6D9CE4` | 跑（2 格，`sub_76756C`，广播 ident 0x0D） |
| 4105 | — | `0x6DA005` | |
| 4106 | `CM_SHANGMA_OK` | `0x6DA030` | 上马确认 |
| 4107 | `CM_XIAMA` | `0x6DA03D` | 下马 |
| **4108** | **`CM_RUN3`** | **`0x6D9D99`** | **骑乘跑（3 格，`sub_767694`，广播 ident 0xD58）** |
| 4109 | `CM_YAOQING_SHANGMA` | `0x6D9E33` | 邀请上马（同样先查 state 0x33） |
| 4110 | `CM_INVITE_HORSE` | `0x6D9E96` | |
| 4111 | `CM_RIDER_DOWN` | `0x6D9E77` | 乘客下马（查 state 0x34） |

**这张表里没有 3035。** 3035 落在 `0x6D9EAF`，与十个 HIT ident 同址。

### 3.2 `0x6D9D99` 开篇就是骑乘态硬门

```
0x6D9D99  B2 33              mov  dl,0x33            ; bodyState 51 = 已骑乘
0x6D9D9B  8B 45 FC           mov  eax,[ebp-4]
0x6D9D9E  E8 BD 8B 09 00     call 0x772960           ; InBodyState
0x6D9DA3  84 C0              test al,al
0x6D9DA5  74 35              je   0x6D9DDC           ; ★ 未骑乘 → 直接去更正块
0x6D9DA7  ...                call [edx+0xBC]         ; 第二道门
0x6D9DB4  74 26              je   0x6D9DDC
0x6D9DB6  B2 01 / FF 51 40   call [ecx+0x40]         ; can-act 门
0x6D9DC2  74 18              je   0x6D9DDC
0x6D9DC4  0F B7 48 06        movzx ecx,word [msg+6]
0x6D9DD3  E8 FC 22 FE FF     call 0x6BC0D4           ; ★ 3 格 mover
0x6D9DDA  75 39              jne  0x6D9E15           ; 成功 → 0x275
0x6D9DDC  ... push CurrX / CurrY / 朝向 / 0
0x6D9E01  66 BA 76 02        mov  dx,0x276           ; 失败 → SM_ACT_FAIL 更正
```

字节 `0x6D9D99: B2338B45FCE8BD8B090084C0`。

**这就是正面答案：原生的"骑乘跑"是 ident 4108 / `CM_RUN3`，worker 是
`sub_6BC0D4` → `sub_767694`，前置条件是 bodyState `0x33`。**

### 3.3 本端已经建对了

`GameSvr/Players/TPlayObject.NativeRun3Horse.cs` 的 `ClientNativeRun3` 逐项对齐
`0x6D9D99`（首条即 `HasNativeActiveState(51)`），`TPlayObject.Message.cs:1652`
的 `case Grobal2.CM_RUN3:` 接的就是它。

**所以本端并不缺骑乘跑，它多了一条冒名顶替的第二骑乘跑。**

## 4. 动作码 `0x3F9`(1017) 整臂取证

### 4.1 `sub_7707A8` 的动作窗口

```
0x770803  0F B7 45 FA           movzx eax,word [ebp-6]    ; cx = 动作码
0x770807  05 18 FC FF FF        add   eax,0xFFFFFC18      ; = -0x3E8 = -1000
0x77080C  83 F8 21              cmp   eax,0x21            ; 33
0x77080F  0F 87 AF 04 00 00     ja    0x770CC4
0x770815  FF 24 85 1C 08 77 00  jmp   dword [eax*4+0x77081C]
```

窗口 = 1000..1033。`0x77081C[17] = 0x770ABF`（1017），`[18] = 0x77092A`（1018）。

在此之前有一条对所有动作码都跑的副作用：

```
0x7707E0  8A 45 08              mov  al,[ebp+8]           ; 方向
0x7707E3  88 86 54 01 00 00     mov  [esi+0x154],al       ; ★ m_btDirection
```

### 4.2 1017 与 1018 逐字节对比

```
1017 @0x770ABF                          1018 @0x77092A（CM_CRSHIT）
8B 86 C4 00 00 00  mov eax,[esi+0xC4]   8B 86 A4 00 00 00  mov eax,[esi+0xA4]   ←① 技能槽不同
89 45 F0           mov [ebp-0x10],eax   89 45 F0
66 8B 55 FA        mov dx,[ebp-6]       66 8B 55 FA
52                 push edx             52
8B 55 F0 / 52      push [ebp-0x10]      8B 55 F0 / 52
8B 8E 90 02 00 00  mov ecx,[esi+0x290]  8B 8E 90 02 00 00
8B 86 8C 02 00 00  mov eax,[esi+0x28C]  8B 86 8C 02 00 00
2B C8 / 8B D0 / 8B C6 / 8B 18           2B C8 / 8B D0 / 8B C6 / 8B 18
FF 93 CC 00 00 00  call [vmt+0xCC]      FF 93 CC 00 00 00  call [vmt+0xCC]
89 45 F4           mov [ebp-0xC],eax    89 45 F4
                                        66 8B 45 08 / 50   push word[ebp+8]     ←② 1018 多传方向
8B 55 FC           mov edx,[ebp-4]      8B 4D F4 / 8B 55 FC
8B C6              mov eax,esi          8B C6
E8 90 18 00 00     call 0x772388        E8 4D 12 00 00     call 0x771BB8        ←③ worker 不同
```

**三处差异，其中第③处是关键：**

```
0x771BB8  55 8B EC 33 C0 5D C2 04 00
          push ebp / mov ebp,esp / xor eax,eax / pop ebp / ret 4
```

**1018 的 worker 是个返回 0 的空壳** —— 这与本端 `TPlayObject.Attack.cs` 里
`case Grobal2.CM_CRSHIT:` 只写 `m_btDirection = nDir;` 的既有注释完全吻合。

而 1017 的 `sub_772388` 是**真的**：`E8` rel32 全镜像扫描证明它**只有一个调用者**
`0x770AF3`，即 1017 臂本身。

### 4.3 `sub_772388` 全文语义

```
0x772390  8B F2              mov  esi,edx              ; 目标（可为 nil）
0x772392  8B D8              mov  ebx,eax              ; Self
0x772394  C6 45 FF 00        mov  byte [ebp-1],0       ; result = 0
0x772398  85 F6 / 75 49      test esi,esi / jne 0x7723E5   ; 已给目标 → 跳过搜索
  0x77239E  8A 83 54 01 00 00  mov  al,[ebx+0x154]     ; 朝向
  0x7723A4  50                 push eax
  0x7723A5  6A 02              push 2                  ; ★ 距离 = 2 格
  0x7723A7  lea/push [ebp-8] / lea/push [ebp-0xC]      ; 出参 X / Y
  0x7723AF  mov ecx,[ebx+0x130] / mov edx,[ebx+0x12C]  ; CurrY / CurrX
  0x7723BB  mov eax,[ebx+0x128]                        ; Envir
  0x7723C1  E8 22 68 00 00     call 0x778BE8           ; GetNextPosition
  0x7723C6  84 C0 / 74 1B      失败 → 无目标
  0x7723CA  push 1 / push 0 ×3
  0x7723D8  mov eax,[ebx+0x128]
  0x7723DE  E8 C5 60 00 00     call 0x7784A8           ; 取该格上的对象
  0x7723E3  8B F0              mov  esi,eax
0x7723E5  8B D6 / 8B C3 / 8B 08
0x7723EB  FF 51 4C           call [vmt+0x4C]           ; ★ 伤害结算，返回 eax
0x7723EE  85 C0 / 7C 60      jl  0x772452              ; < 0 → 什么都不做（含不练级）
0x7723F2  C6 45 FF 01        result = 1
0x7723F6  85 C0 / 7E 21      jle 0x77241B              ; == 0 → 跳过施加伤害
0x7723FA  C6 45 FF 02        result = 2
0x7723FE  68 F9 03 00 00     push 0x3F9                ; ★ 动作码回传
0x772403  50                 push eax                  ; 伤害值
0x772404  A0 5C 24 77 00     mov  al,byte [0x77245C]   ; 该字节实测 = 0x00
0x772409  50 / 6A 01         push eax / push 1
0x77240C  8B 8B C4 00 00 00  mov  ecx,[ebx+0xC4]       ; 技能记录
0x772412  8B D6 / 8B C3
0x772416  E8 4D BE FF FF     call 0x76E268             ; 施加伤害（全镜像 24 个调用者，
                                                       ;  全在 0x7710xx..0x7724xx 结算带）
0x77241B  85 F6 / 74 19      test esi,esi
  0x77241F  push 0 ×5 / 6A 46  push 0x46
  0x77242B  66 B9 40 27        mov  cx,0x2740          ; ident 10048
  0x77242F  8B D3 / 8B C6      edx=Self / eax=target
  0x772433  E8 28 3C FF FF     call 0x766060           ; target.SendMsg(...)
0x772438  B8 03 00 00 00 / E8 0A 17 C9 FF   Random(3)
0x772442  8B C8 / 41         ecx = rand+1              ; 1..3
0x772445  8B 93 C4 00 00 00  mov  edx,[ebx+0xC4]
0x77244F  FF 53 3C           call [vmt+0x3C]           ; TrainSkill(magic, 1..3)
0x772452  8A 45 FF           return byte [ebp-1]       ; 0 / 1 / 2
```

`0x2740`(10048) 的接收臂 `0x767073` 只做一件事：

```
0x767073  6A 20 / 6A 00 ×4 / 33 C9 / 66 BA 05 29  mov dx,0x2905
0x767087  FF 93 D8 00 00 00                       call [vmt+0xD8]   ; SendRefMsg 10501
```

即"被打中"的视觉广播。同一套 `push 0x46 / mov cx,0x2740 / call 0x766060` 惯用法
在 `0x76A2A3` 还有一处，形状一模一样。

### 4.4 `[Self+0xC4]` 是哪个技能

动作臂的技能槽是一整排 `[Self+0x9C .. +0xD4]`，每 4 字节一个：

| 槽 | 动作码 | 对应 ident |
|---|---|---|
| `+0x9C` | 1000 / 1001 | 3014 / 3015 |
| `+0xA0` | 1003 | 3018 |
| `+0xA4` | 1018 | 3026 `CM_CRSHIT` |
| `+0xA8` | 1004 | 3019 |
| `+0xAC` | 1005 | 3024 |
| `+0xB0` | 1007 / 1014 | 3025 |
| `+0xB8` | 1015 | 3002 |
| `+0xBC` | 1011 / 1012 | — |
| **`+0xC4`** | **1017** | **3035** |
| `+0xD0` | 1019 | — |
| `+0xD4` | 1021 | — |

`89 8B/86 C4 00 00 00` 形态的写点全镜像共 9 处，落在技能刷新器里的只有一处：

```
0x76B16D  8B 45 F0           mov  eax,[ebp-0x10]       ; TUserMagic*
0x76B170  89 83 C4 00 00 00  mov  [ebx+0xC4],eax
```

它所在的 switch 以 `0x76AE6B call 0x4C853C` / `movzx eax,ax`（= magic id）为选择子。
对该 switch 做同样的精确仿真：

```
arm 0x76B12C : magic id [58]            -> [+0xB8]  (CM_SWORD_HIT)
arm 0x76B16D : magic ids [65,66,67,68]  -> [+0xC4]  ★ 动作 1017
arm 0x76B191 : magic id [69]
```

命中路径是 `0x76AF2B add eax,-7` / `0x76AF2E sub eax,4` / `0x76AF31 jb 0x76B16D`
—— **无符号借位区间测试**，正是最初粗糙 flag 模型误判成 175 个 id 的那一处。

**结论：3035 的攻击语义 = 「以 magic 65/66/67/68 中已学的那个为技能记录、
射程 2 格、走 `[vmt+0x4C]=0x744388` 专用伤害公式的一次单体攻击，
命中后回传动作码 0x3F9 并按 Random(3)+1 练级」。**

它与 `CM_CRSHIT`(1018) 是**相邻动作码的同族兄弟**，但 1018 在本引擎被阉成空壳，
1017 是活的。

## 5. `CM_HORSERUN = 3035` 的来源追溯

### 5.1 本仓

```
$ git log --reverse --format="%H %ad %s" --date=short -S CM_HORSERUN -- SystemModule/Grobal2.cs
d5d00744  2026-08-10  Baseline: 战神 M2Server 1:1 C# rewrite tree
```

**首次出现即 baseline，不是本项目任何一次移植加进来的。**

### 5.2 上游

`staging/upstream-LyoMir2/SystemModule/Grobal2.cs:607` = `public const int CM_HORSERUN = 3035;`，
且上游 `TPlayObject.Message.cs:1011` 就是 `case Grobal2.CM_HORSERUN: if (ClientHorseRunXY(...))`。
**常量、路由、`ClientHorseRunXY` 三件套整体来自上游 LyoMir2。**

有意思的是，上游 `UsrEngn.cs:1404` 把 `CM_HORSERUN` 和 HIT 族**放在同一个
case 标签组**里走同一条表头路径 —— 上游自己就自相矛盾。

### 5.3 上游的上游

上游继承自 LOMCN 系 Delphi 客户端头。`staging/ref-MirServer-Delphi/MirClient/Common/Grobal2z.pas:1043`：

```
  CM_HORSERUN     = 3035;     //------------未知消息码
  CM_CRSHIT       = 3036;     //------------未知消息码
  CM_3037         = 3037;
  CM_TWINHIT      = 3038;
```

注释原字节 `CE B4 D6 AA CF FB CF A2 C2 EB`，GBK 解码 = **「未知消息码」**。

这个四行块在本引擎里已被证伪三行：本仓 `Grobal2.cs:903-909` 早就用字节证明
`CM_CRSHIT/CM_3037/CM_TWINHIT` 是 **3026/3027/3028** 而非 3036/3037/3038。
同一块里的 `CM_HORSERUN = 3035` 是最后一行没被复核的。

另外服务端侧 `staging/ref-MirServer-Delphi/Common/Grobal2.pas:492` 写的是
`CM_HORSERUN = 3009;` —— **参考树内部两个值互相打架**，本身就说明这个名字不可信。

### 5.4 裁决

| 对象 | 裁决 |
|---|---|
| 数值 3035 | **不是 INVENTED**。它是本引擎真实可达的 ident，有专属动作码和专属 worker。 |
| 名字 `CM_HORSERUN` | **INVENTED**（上游 Delphi 头自注"未知消息码"的占位名） |
| 路由 `→ ClientHorseRunXY` | **INVENTED，且是本轮要修的东西** |
| `ClientHorseRunXY` / `HorseRunTo` 本体 | **INVENTED**（原生无对应 worker；真正的 3 格 mover 是 4108 的 `sub_767694`） |

> 按仓库既有惯例（`CM_3037 = 3027` 保留上游**名字**、纠正上游**数值**），
> 这里数值本来就对，**建议只修路由、不改名**，另加注释说明名字来历。
> 但删不删 `CM_HORSERUN` 按任务约定留给主代理裁决，见 §7。

## 6. 影响面：本端现状比"路由错了"更严重

`TPlayObject.Message.cs:1539` → `ClientHorseRunXY` → `TPlayObject.cs:1096 HorseRunTo`。
把 `HorseRunTo` 的门列全：

```
HasTimedAbility(13)                       ; 唯一的状态门
switch (btDir) → CanWalkEx ×3 → CommitRunMove(±3, ±3)
```

**没有 `HasNativeActiveState(51)`，没有任何骑乘态检查。**
`ClientHorseRunXY` 自己的门也只有 `m_boCanRun` / `IsNativeCanActBlockedByForcedMove()` /
死亡麻痹 / 测速节流。

对照原生 4108 的 `0x6D9DA5 je 0x6D9DDC`：**未骑乘就直接进更正块。**

结论：**今天任何客户端只要发 ident 3035，就能在没有坐骑的情况下拿到一次三格位移**，
而原生对 3035 的回应是「转个身，然后做一次 2 格攻击」——**位移量 0**。
这是一条无原生依据的加速通道。

各站点影响清单：

| 站点 | 现状 | 迁移后 | 风险 |
|---|---|---|---|
| `TPlayObject.Message.cs:1539` | 独立 `ClientHorseRunXY` 臂 | 并入 HIT case 标签组 | **修掉三格位移漏洞**；3035 改为按攻击节流（`m_dwAttackTick`）而非按跑步节流（`m_dwMoveTick`） |
| `UsrEngn.cs:2680` | 已在 HIT 表头组 | **不动** | 无。表头映射本来就对（Recog→X / Param→Y / Series&7→dir，锚 `0x6D9EAF`） |
| `UsrEngn.cs:2739` | 不含 `CM_HORSERUN` | **不动** | 该 switch（`m_dwRunTick -= 100`）在本仓无任何 VA 标注，属上游遗留，无字节依据，fail-closed |
| `TPlayObject.Attack.cs` | 无 3035 臂 | **已加**（本分支） | 当前惰性；Message.cs 改后生效 |
| `TPlayObject.Attack.cs:267` `ClientHorseRunXY` | 被 Message.cs 调用 | 变死代码 | 私有方法未被调用不产生警告，实测 15 警不变 |
| `TBaseObject.cs:3894` `SendActionMsg` | 含 `CM_HORSERUN` | **不动** | 3035 确是动作消息，留在动作冲刷表里方向正确 |
| `GameGate-CS/SpeedDetector.cs:168` | 归类 `ActionType.RUN` | **建议改 ATTACK，但本轮不改** | 见下 |
| `GameGate-CS/GateServer.cs:111` | `IsActionCoordinateIdent` 含之 | **不动** | 正确：3035 就是动作坐标族 |
| `AuditTools/NativeCastLockMovementGateCheck` | `:68` 断言 3035、`:183` 断言 3035 走移动门 | **必须同步改** | 迁移后 `:183` 会红 |

关于 `SpeedDetector`：原生 GameGate 底本
（`staging/_gg_reunpack_work/dump_gg2025/flat_image.bin`）里对 3035 / 3013 / 3011 /
3014 / 4108 的 `3D`/`2D`/`05` imm32 与 `66 3D` imm16 比较形态**全部 0 命中**，
即该分类器在网关侧**没有原生字节依据**。把它从 RUN 改成 ATTACK 语义上更对，
但那是拿一个无依据换另一个无依据，**按铁律 fail-closed，本轮不改**，
只登记为「与服务端路由必须同步翻转」的联动项。

客户端侧：本次改动**不改变任何发出去的包的 ident 或字段布局**
（`UsrEngn` 表头映射一字未动），只改变服务端对 3035 的处理与节流通道。
原生客户端若确实会发 3035，收到的将从「SM_ACT_GOOD + 三格位移广播」
变成「SM_ACT_GOOD/0x276 + 转身」——后者才是原生行为。

## 7. 迁移方案（热点文件由主代理落地）

> 下面三段「被替换原文」都已与 master `3af087ee`（本报告写成时的 master 头，
> 已越过本分支基线 `e222a2cb`）逐字符比对通过，可直接搜索替换。
> 行号按 `e222a2cb` 给出，master 上可能有位移，**以文本为准、不要按行号切**。

### 7.1 `SystemModule/Grobal2.cs`（**建议只加注释，不改值不改名**）

**被替换原文**（现 902 行）：

```csharp
        public const int CM_HORSERUN = 3035;
```

**替换为**：

```csharp
        // ID3035: the value is right, the name is not. 3035 is a real, reachable
        // ident, but the dispatcher sends it to HIT CASE1 with the other ten hit
        // opcodes — 0x6D8610 `0F 84 99 18 00 00 je 0x6D9EAF`, reached through the
        // running chain 0x6D85F0 `sub eax,0xBD4` / 0x6D85FB `sub eax,2` /
        // 0x6D8604 `sub eax,2` / 0x6D860D `sub eax,3`. sub_6EC078's window closes
        // exactly on it (0x6EC15D `add eax,-0xBBA` / 0x6EC162 `cmp eax,0x21`) and
        // byte table 0x6EC178[33] = 0x09 picks slot 0x6EC19A[9] = 0x6EC29C
        // `66 B9 F9 03 mov cx,0x3F9`, i.e. action code 1017 — one below
        // CM_CRSHIT's 1018 and, unlike 1018 whose worker 0x771BB8 is the stub
        // `55 8B EC 33 C0 5D C2 04 00`, backed by a real one at 0x772388.
        // The mount run is CM_RUN3 (4108), whose handler 0x6D9D99 opens with
        // `B2 33 mov dl,0x33` / `call 0x772960` and refuses with 0x276 when the
        // rider is not mounted.
        // The name comes from the upstream Delphi client header
        // (MirClient/Common/Grobal2z.pas:1043), which annotates it
        // `//------------未知消息码` — a placeholder. It is kept because the
        // repository already keeps upstream names for byte-corrected values
        // (CM_3037 = 3027 is the same situation).
        public const int CM_HORSERUN = 3035;
```

> 若主代理裁决改名，可加 `public const int CM_3035 = 3035;` 并把
> `CM_HORSERUN` 留成 `= CM_3035` 的别名 —— 这样 §7.3 与 GameGate 侧的
> 既有引用都不会断。**不建议直接删** `CM_HORSERUN`：`GameGate-CS` 两处、
> `TBaseObject.cs:3894`、`AuditTools` 两处都在引用它。

### 7.2 `GameSvr/Players/TPlayObject.Message.cs`（两处，**已在本工作树实测编译通过后原样撤回**）

> 两块改动都临时施加过，`dotnet build GameSvr/GameSvr.csproj -o <临时目录>`
> 实测 **0 错 15 警**（与基线同），随后 `git checkout --` 撤回，
> 故本分支上 `TPlayObject.Message.cs` 零改动。

#### 7.2.1 删除独立的 `CM_HORSERUN` 臂

**被替换原文**（现 1539–1576，整段删除，`case Grobal2.CM_RUN:` 保留）：

```csharp
                case Grobal2.CM_HORSERUN:
                    if (ClientHorseRunXY((short)ProcessMsg.wIdent, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.boLateDelivery, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendMoveActionFail();
                        }
                        else
                        {
                            nMsgCount = GetRunMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxRunMsgCount)
                            {
                                // MOVE-22: Native never disconnects, kicks or logs a fast client.
                                // Simply send correction back to client.
                                SendMoveActionFail();
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                }
                            }
                            else
                            {
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg(format("操作延迟 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                }
                                SendDelayMsg(this, (short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                result = false;
                            }
                        }
                    }
                    break;
                case Grobal2.CM_RUN:
```

**替换为**：

```csharp
                case Grobal2.CM_RUN:
```

> `nMsgCount` 在本 switch 的其它臂仍有使用，删除后不产生 CS0219；实测已验。

#### 7.2.2 把 3035 加进 HIT case 标签组

**被替换原文**（现 1683–1684）：

```csharp
                case Grobal2.CM_SWORD_HIT:
                case Grobal2.CM_3037:
```

**替换为**：

```csharp
                case Grobal2.CM_SWORD_HIT:
                // ID3035: 3035 is the eleventh CASE1 ident, not a mount run. The
                // dispatcher reaches it through the running subtraction chain
                //   0x6D85F0 sub eax,0xBD4 / 0x6D85FB sub eax,2 /
                //   0x6D8604 sub eax,2     / 0x6D860D sub eax,3 /
                //   0x6D8610 0F 84 99 18 00 00  je 0x6D9EAF
                // and sub_6EC078's window closes on it (0x6EC15D `add eax,-0xBBA` /
                // 0x6EC162 `cmp eax,0x21`), where byte table 0x6EC178[33] = 0x09
                // picks slot 0x6EC19A[9] = 0x6EC29C `mov cx,0x3F9` = action 1017,
                // one code below CM_CRSHIT's 0x3FA. The native mount run is
                // CM_RUN3 (4108) at 0x6D9D99, gated on bodyState 0x33.
                // The old ClientHorseRunXY arm had no mount gate at all, so 3035
                // bought an unmounted three-cell HorseRunTo; it is gone.
                case Grobal2.CM_HORSERUN:
                case Grobal2.CM_3037:
```

**下方一字不动**：`RunNativeHitArmGates(ProcessMsg.wIdent)` 对 3035 天然走 CASE1
（`boCase2` 只对 `CM_3037` 为真），`ClientHitXY` 的三元表达式对 3035 也天然传
`ProcessMsg.wIdent` 而非 `nParam3`。

### 7.3 `GameSvr/UsrSystem/UsrEngn.cs`：**无需改动**

`:2680` 的 `case Grobal2.CM_HORSERUN:` 已经和 HIT 族同组，走的正是锚在
`0x6D9EAF` 的表头映射（`Recog`→X、`Param`→Y、`Series & 7`→方向）。
**这条本来就是对的** —— 而且它与 `TPlayObject.Message.cs` 的移动族路由
互相矛盾，是本仓内部独立佐证 3035 属于动作族的一条旁证。

`:2739` 的第二个 switch（`m_dwRunTick -= 100`）**不动**，理由见 §6 表。

### 7.4 已在本分支落地的非热点改动

| 文件 | 改动 |
|---|---|
| `tools/id3035_dispatch_map.py`（新） | 精确 flag 的派发树全量仿真器 |
| `GameSvr/Players/TPlayObject.Attack.cs` | `ClientHitXY` 加 `case Grobal2.CM_HORSERUN:` 臂（仅朝向更新，带完整 VA 取证注释） |
| `GameSvr/Players/TPlayObject.NativeHitArmGates.cs` | 更新 §6.1 遗留的"待专项"注释为本轮结论 |

`ClientHitXY` 的 3035 臂**只做 `m_btDirection = nDir`**，理由：那是
`0x7707E3 mov [esi+0x154],al` 对 1000..1033 全窗口的无条件副作用，有字节依据；
而 1017 的伤害半边挂在 `[vmt+0x4C] = 0x744388`（约 900 字节、自带
`[Self+0xC4]` 空指针短路 `0x7443A3 cmp dword [ebx+0xC4],0 / je 0x7446DD`），
**本轮未转录，按铁律 fail-closed，宁缺毋造**。

### 7.5 `AuditTools/NativeCastLockMovementGateCheck/Program.cs`（随 §7.2 同步）

`:68` 的 `Equal(3035, Grobal2.CM_HORSERUN, "CM_HORSERUN")` 可保留（值没变）。
`:183` 的

```csharp
    Assert(player.Operate(Message(Grobal2.CM_HORSERUN, 5, 3, 0)),
```

在 3035 改走 HIT 路径后语义不再是"移动门"，**必须重写或移除**，
否则该 AuditTool 会判红。本轮未改（属主代理裁决的连带项）。

## 8. 仍 BLOCKED / UNPROVEN

1. **动作 1017 的伤害半边未建模。** `sub_772388` 已逐指令抄录（§4.3），但它依赖
   `[vmt+0x4C] = 0x744388`（TPlayer 与 THumanKind 同槽）尚未转录，
   以及 `0x76E268`（施加伤害，24 个调用者）与 `[vmt+0x3C] = 0x76AD30`（练级）的
   本端对应关系尚未逐项钉死。**建议单开一个专项。**
2. **`[Self+0xC4]` 对应的 magic 65/66/67/68 在本端只有 `SKILL_65..68` 占位常量**，
   四者的技能语义未定性；`0x76B16D` 的写入是"最后一个已学的覆盖前面"，
   若四者可同时习得则存在顺序依赖，未验。
3. **ident `0x2740`(10048) / `0x2905`(10501) 本端无常量名**，
   `0x766060` 的六个栈参形状（`push 0x46` 落在哪一个形参）未逐项确认。
4. **GameGate 侧 `ActionClassifier` 无原生依据**（§6）。原生 GameGate 底本对
   3011/3013/3014/3035/4108 的比较形态 0 命中，无法用字节裁决 RUN vs ATTACK。
5. **`UsrEngn.cs:2739` 的 `m_dwRunTick -= 100` switch 全仓无 VA 标注**，
   其 ident 集合（缺 `CM_SWORD_HIT` / `CM_3037` / `CM_HORSERUN`）是否忠实未验。
6. **ident 4105 / 4110 的语义未查**（`0x6DA005` / `0x6D9E96`），
   本轮只确认它们不是骑乘跑。
7. **`docs/eqv_shard20_20260814.md:49` 的附注已被本报告推翻**，但该文件本轮未改，
   留给主代理决定是否就地订正。
