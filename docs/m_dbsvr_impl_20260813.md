# GameSvr ↔ DBServer 边界 — DBServer 侧逐字节还原与四态对账

日期：2026-08-13
工作树：`D:\loym2\.claude\wt2\m-dbsvr`（分支 `w/m-dbsvr`）
**未执行任何编译命令。**

本报告接续 `staging/m_logindb_impl_20260813.md`（该报告只有 M2Server 侧字节，DBServer 侧全部标
BLOCKED）。本轮的差别是：**DBServer 侧的可分析代码一直存在**，10 项 BLOCKED 里有 9 项就此定案。

---

## 0. 证据源

| # | 证据源 | 说明 |
|---|---|---|
| N1 | `staging/_dbsvr_reunpack_work/dbserver_CODE_live.bin`（1,916,928 B，熵 6.433） | DBServer CODE 节的**运行期转储**，可逐字节反汇编 |
| N2 | `staging/_dbsvr_reunpack_work/DBServer_repaired_20260803.exe`（14,708,736 B） | 用于读节表、定基址 |
| N3 | `staging/_reunpack_work/flat_image.bin` | M2Server 平坦镜像（对端，上一轮已用） |
| N4 | `staging/_dbsvr_reunpack_work/DBService_original.ini.txt` | 生产拓扑（端口） |
| N5 | C# 工作树 | 对账对象 |

中间产物：`staging/dbsvr_re/`（`p*` 为上一轮，`q*` 为本轮复核）。

---

## 1. 基址确定（必须自证，用错基址会让所有交叉引用返回 0 命中）

`q01_verify.py`，三条互相独立的判据：

### T1 — PE 节表（决定性）

`DBServer_repaired_20260803.exe` 的 `IMAGE_OPTIONAL_HEADER.ImageBase = 0x00400000`，
`SectionAlignment = 0x1000`，节表：

```
CODE     VA=0x00401000 VSZ=0x001D34C8 RAW=0x00000000 RSZ=0x00000000
DATA     VA=0x005D5000 VSZ=0x00005DB4 RAW=0x00000000 RSZ=0x00000000
BSS      VA=0x005DB000 VSZ=0x00005AE5 RAW=0x00000000 RSZ=0x00000000
.vmp0    VA=0x005E6000 VSZ=0x0057C78B RAW=0x00000000 RSZ=0x00000000
.vmp1    VA=0x00B63000 VSZ=0x006A2D40 RAW=0x00000400 RSZ=0x006A2E00
```

`CODE.VSZ = 0x1D34C8`，向上对齐到 `0x1000` 得 `0x1D4000`；转储长度**正好** `0x1D4000`。
⇒ 转储 = CODE 节的完整虚拟映像，**基址 = `0x00401000`**。

> 顺带定案：`CODE/DATA/BSS` 的 `SizeOfRawData` 全为 0、唯一载荷在 `.vmp1` ——
> 这正是上一轮判 BLOCKED 的依据。**该判断对磁盘那份成立，对运行期转储不成立。**
> 节名是 `.vmp0/.vmp1`（VMProtect），与眼神的 `.tvmp`（Themida）不是同一个壳，两者不要混谈。

### T2 — 绝对跳表落点（证伪性最强）

全转储枚举 `jmp dword ptr [imm32 + reg*4]`（`FF 24 8D`），取每张表前 8 项，
要求表项是转储内的合法 VA：

| 候选基址 | 表项落在转储内 |
|---|---|
| `0x00400000` | 0 / 24 （0.0%） |
| **`0x00401000`** | **22 / 24 （91.7%）** |
| `0x00402000` | 0 / 24 （0.0%） |
| `0x10000000` | 0 / 24 （0.0%） |

只有 `0x401000` 非零，且相邻候选立刻掉到 0 —— 这是硬判据，不是打分。

### T3 — 反证：按该基址取三个分发器臂，必须解出合理的 Delphi 代码

```
0x00598BF7  mov eax,[ebp-0x10] ; mov ax,word[eax+2] ; push eax ; mov eax,[ebp-0x10]
0x00599AEE  mov eax,[0x5D9EBC] ; mov eax,[eax] ; mov edx,[ebp-0xC] ; call 0x5AD298
0x00599DF2  lea eax,[ebp-0x14] ; mov edx,[ebp-0xC] ; add edx,0x29 ; call 0x404E5C
```

全部落在指令边界且语义自洽（`0x5D9EBC` 在 DATA 段 `0x5D5000..0x5DB000` 内，
`0x404E5C` 是 Delphi 的 `LStrFromPCharLen` 族）。

**定案：`VA = 0x401000 + 转储内偏移`。** 本报告全部地址按此基址。

---

## 2. 分发器还原（本轮的地基）

DBServer 的入站分发按帧头 `+0x04` 的 type 分成三个函数：

| type | 函数 | 未知命令的日志串 |
|---|---|---|
| 1 | `sub_59889C` | `'UnKnown Tools Cmd of TYPE_A:'` / `'UnKnown Cmd of TYPE_A:'` |
| 2 | `sub_599860` | `'UnKnown Cmd of TYPE_B:'` |
| 3 | `sub_599DB4` | `'UnKnown Cmd of TYPE_C:'` |

`q03_disp.py` 按字节走完比较链与跳表，与上一轮 `p24_reqrep.txt` 的臂表**逐项比对，0 处不一致**。

### 2.1 【新发现】连接角色字节 `[conn+0x40A0]` —— type-1 有两套命令空间

```
005988E1  80 B8 A0 40 00 00 09   cmp byte ptr [eax+0x40A0], 9
005988E8  0F 85 42 01 00 00      jne 0x598A30            ; != 9 -> GameServer 分支
```

全转储只有 **两个** 角色常量被比较（`q02_roles.py`，66 条引用 `+0x40A0` 的指令）：
`0`（`0x598564` 建连时初始化）与 `9`（9 处 `cmp`）。

- **role 9 = DBTool**：type-1 走 `0x5988EE add eax,-0x100 / cmp eax,4 / jmp [eax*4+0x598909]`，
  命令空间 **`0x0100..0x0104`**（5 项，全部有实臂）。
- **role ≠ 9 = GameServer**：type-1 走 `0x598A30` 的比较链 + 两张跳表。

角色由 **type-2 命令 `0x003D`** 设置（见 §3.5）。

### 2.2 TYPE-1 命令全集（role ≠ 9，GameServer 链路）

结构：

```
00598A30  movzx eax, word[msg]                    ; 命令码
00598A36  cmp eax,0x168 ; jg 0x598B0E ; je 0x598DCB
00598A47  cmp eax,0x15A ; jg 0x598AC5 ; je 0x598EA1
00598A54  cmp eax,0x154 ; jg 0x598A9E ; je 0x598D80
00598A61  cmp eax,0x151 ; jg 0x598A87 ; je 0x598C1B
00598A6E  sub eax,0x45  ; je 0x59937C             ; 0x0045
00598A77  sub eax,0x10B ; je 0x598BF7             ; 0x0150
00598ACA  cmp eax,0x0C  ; ja default ; jmp [eax*4+0x598ADA]   ; 表A 0x15B..0x167（13 项）
00598B13  cmp eax,0x34  ; ja default ; jmp [eax*4+0x598B23]   ; 表B 0x16A..0x19E（53 项）
```

**40 个活命令**：
`0045 0150 0151 0152 0153 0154 0155 0156 0157 0159 015A 015B 0160 0161 0162 0163 0164 0165
0166 0167 0168 016A 016B 016C 0170 0172 0173 0174 0176 0181 0182 0183 0192 0193 0194 019A
019B 019C 019D 019E`

`0x0155` 的臂就是函数出口标号 `0x59953D`（`00598A9E 2D 55 01 00 00 sub eax,0x155 / 0F 84 94 0A 00 00 je 0x59953D`）
—— **受理但无动作**，不是 default，不会打日志。

在表内但指向 default `0x599502` 的 38 个：
`015C..015F 016D 016E 016F 0171 0175 0177..017F 0180 0184..0191 0195..0199`。

### 2.3 TYPE-2 命令全集

```
0059988E  movzx eax, word[msg]
00599891  cmp eax,0x177 ; jg 0x5998CD ; je 0x599AEE
0059989E  add eax,-0x3C  ; cmp eax,6    ; ja default ; jmp [eax*4+0x5998B1]  ; 0x3C..0x42
005998CD  add eax,-0x180 ; cmp eax,0x11 ; ja default ; jmp [eax*4+0x5998E2]  ; 0x180..0x191
```

**14 个活命令**：`003C 003D 003E 003F 0040 0041 0042 0177 0180 0184 0185 0186 0187 0191`。
在表内但落 default 的 12 个：`0181 0182 0183 0188..0190`。

### 2.4 TYPE-3 命令全集

```
00599DE9  66 8B 00        mov ax, word[msg]
00599DEC  66 2D 88 01     sub ax, 0x188
00599DF0  75 3C           jne 0x599E2E        ; 唯一出口：打 'UnKnown Cmd of TYPE_C:'
```

**type-3 只有一个命令：`0x0188`**（臂 `0x599DF2`）。

---

## 3. 【定案·推翻前论】静态记录码 `0x73/0x75/0x76/0x6D` 不是 INVENTED

上一轮把这 4 个码判成 `INVENTED（I1–I4）`并建议移除，依据是「原版 M2Server 的两个 type-2
分发器都不认」。**该依据成立，但结论错了** —— 认不认在**接收侧**，发不发在**发送侧**，
而发送侧就是 DBServer。

### 3.1 四个码各自是什么

`p21_t2scan` 的 `mov word ptr [mem], imm16` 扫描 + `p22_static` 的函数内字符串：

| 记录码 | 生产函数 | MySQL 语句（函数内字面量） |
|---|---|---|
| `0x006D` | `sub_5C3AE4` | `select High_Priority * from forcemagic order by ForceId` |
| `0x0073` | `sub_5C7148` | `select High_Priority * from AntiqueItems;` |
| `0x0075` | `sub_5C78B4` | `Select High_Priority * from SuperForce order by level;` |
| `0x0076` | `sub_5C8038` | `Select High_Priority * from SuperSkill;` |

对照已确认在线的同族：`0x65 humanmagic` / `0x66 heromagic` / `0x67 monster` /
`0x68 stditems` / `0x6C fieldhero`。

### 3.2 九个生产者是同一个函数的九次连续调用

`sub_5C9094`（"载入全部静态表"）：

```
005C90F3  e8 34 b9 ff ff   call 0x5C4A2C   ; 0x65 humanmagic
005C90FF  e8 98 b2 ff ff   call 0x5C439C   ; 0x66 heromagic
005C910B  e8 64 c8 ff ff   call 0x5C5974   ; 0x67 monster
005C9117  e8 c8 d5 ff ff   call 0x5C66E4   ; 0x68 stditems
005C9123  e8 20 e0 ff ff   call 0x5C7148   ; 0x73 AntiqueItems
005C912F  e8 6c 9e ff ff   call 0x5C2FA0   ; 0x6C fieldhero
005C913B  e8 74 e7 ff ff   call 0x5C78B4   ; 0x75 SuperForce
005C9147  e8 ec ee ff ff   call 0x5C8038   ; 0x76 SuperSkill
005C9153  e8 8c a9 ff ff   call 0x5C3AE4   ; 0x6D forcemagic
```

每个生产者**都只有这一个调用点**（`p22_static` 的 xref 表：9 个函数各 1 个 xref，全部来自
`sub_5C9094`）。争议的 4 个与无争议的 5 个是同一批、同一层、同一顺序。

### 3.3 九个生产者把记录挂进的是**同一条**广播链表

节点形状（以 `0x65` 为例，`0x6D` 在 `0x5C3BA1` 处逐字节相同）：

```
005C4AC4  b8 0c 000000    mov eax, 0xC
005C4AC9  e8 7a e4 e3 ff  call 0x402F48        ; AllocMem(12) -> node
005C4AD6  89 10           mov dword[node], edx ; node.Next = nil
005C4ADB  e8 68 e4 e3 ff  call 0x402F48        ; AllocMem(recLen) -> buf
005C4AE3  89 42 04        mov [node+4], eax    ; node.Buffer
005C4AEC  89 50 08        mov [node+8], edx    ; node.Length
005C4AFA  e8 e9 eb e3 ff  call 0x4036E8        ; FillChar(buf, len, 0)
005C4B0B  66 c7 00 65 00  mov word[buf], 0x65  ; 记录码写在 buf+0x00
005C4B19  83 c0 0c        add eax, 0xC         ; 记录体从 buf+0x0C 起
```

挂链helper `sub_5BA300(eax=self, edx=node)`：

```
005BA30F  83 78 18 00     cmp dword[self+0x18], 0    ; tail
005BA31E  89 10           mov [tail], node           ; tail.Next = node
005BA323  83 78 14 00     cmp dword[self+0x14], 0    ; head
005BA32F  89 42 14        mov [self+0x14], node      ; head = node
005BA338  89 42 18        mov [self+0x18], node      ; tail = node
```

⇒ **`self+0x14` = 链表头、`self+0x18` = 链表尾**。
`0x5BA300` 出现在 **全部九个** 生产者的调用集合里（`p22_static` 第 4/8/12/16/24/28/35/42/46 行）。

### 3.4 这条链表就是 GameServer 注册时被推送的那条

type-2 `0x003D` 的臂（§4.2）在首次注册时：

```
005999EE  e8 e2 07 00 00  call 0x59A1D8      ; 先发 cmd 0x6E
005999FA  68 b8 a2 59 00  push 0x59A2B8      ; 逐记录回调
00599A06  e8 51 f4 02 00  call 0x5C8E5C      ; 遍历 [self+0x14]
00599A1B  e8 14 f5 02 00  call 0x5C8F34      ; 第二条链表
```

`sub_5C8E5C` 正是从 `[self+0x14]` 起遍历：

```
005C8E6F  8b 40 14        mov eax, dword[eax+0x14]     ; head
005C8E7E  8b 48 08        mov ecx, dword[node+8]       ; len
005C8E84  8b 50 04        mov edx, dword[node+4]       ; buf
005C8E8A  ff 55 08        call dword[ebp+8]            ; callback(conn, buf, len)
005C8E90  8b 00           mov eax, dword[node]         ; node = node.Next
```

回调 `sub_59A2B8` 就是发帧：

```
0059A2CA  83 c0 0c        add eax, 0xC                 ; total = len + 12
0059A2D3  e8 f4 0a e7 ff  call 0x40ADCC                ; AllocMem
0059A2E4  c7 00 77bbaa33  mov dword[frame], 0x33AABB77
0059A2ED  66 c7 40 04 02 00 mov word[frame+4], 2       ; Type = 2
0059A2F9  89 42 08        mov [frame+8], eax           ; DataLength = len（不含 12 字节头）
0059A305  8d 50 0c        lea edx, [frame+0xC]         ; 记录整体拷到 frame+0x0C
0059A30E  e8 bd 8e e6 ff  call 0x4031D0                ; Move
0059A31E  e8 a1 20 00 00  call 0x59C3C4                ; 入发送队列
```

**闭合**：九条记录（含争议的 4 条）→ 同一条 `[self+0x14]` 链表 → `0x003D` 注册时逐条
→ `0x33AABB77 | type=2 | len | 记录体` 发给 GameServer。

### 3.5 判定与处理

| 项 | 上一轮判定 | 本轮判定 | 依据 |
|---|---|---|---|
| `0x0073 AntiqueItemsCommand` | `INVENTED`，建议移除 | **`FAITHFUL`** | `sub_5C7148`，`0x5C9123` 唯一调用点，挂 `[self+0x14]` |
| `0x0075 SuperForceCommand` | `INVENTED`，建议移除 | **`FAITHFUL`** | `sub_5C78B4`，`0x5C913B` |
| `0x0076 SuperSkillCommand` | `INVENTED`，建议移除 | **`FAITHFUL`** | `sub_5C8038`，`0x5C9147` |
| `0x006D ForceMagicCommand` | `INVENTED`，建议移除 | **`FAITHFUL`** | `sub_5C3AE4`，`0x5C9153` |

**处理：一个都不许删。** 上一轮的 P3「删除零风险」是错的 —— 删掉会让 C# DBSvr 相对原版
DBServer 少发 4 类记录，**恰好破坏「一个组件一个组件替换」的可回退性**：C# DBSvr 配原版
GameSvr 时静默少发，配 C# GameSvr 时也少发。

**顺带核对推送顺序**（链表是尾插+头遍历，故上线顺序 = `sub_5C9094` 的调用顺序）：

```
原生 : 0x65 0x66 0x67 0x68 0x73 0x6C 0x75 0x76 0x6D
C#   : 0x65 0x66 0x67 0x68 0x73 0x6C 0x75 0x76 0x6D
```

C# `DBSvr/Core/NativeType2StaticLoader.cs:58-78` 的 `Tables[]` 数组顺序**逐项一致**，
包括 `AntiqueItems` 插在 `stditems` 与 `fieldhero` 之间这个反直觉的位置。**`FAITHFUL`**。

### 3.6 那为什么原版 M2Server 不认这 4 个？

因为**这是原版自身的不对称，不是 C# 的缺陷**。M2Server 的即时表
`0071390C jmp [edx*4+0x713913]` 只覆盖 `0x65..0x6E`（`00713900 add edx,-0x65` / `cmp edx,9`），
`0x6D` 落表内 default、`0x73/0x75/0x76` 直接 `ja default`；延迟链也不匹配。

⇒ 原版部署里 DBServer 发、M2Server 丢。C# GameSvr 同样丢弃即为 **`FAITHFUL`**，
**GameSvr 侧不需要任何改动**，但**不许因此去删 DBSvr 侧的发送**。
（这两侧属于不同组件、不同版本节奏；DBServer 这个构建支持 战神 的
SuperForce / SuperSkill / AntiqueItems / ForceMagic 四张表，而手上的 M2Server 构建没有接。）

---

（后续章节随分析推进补入）
