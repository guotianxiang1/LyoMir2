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

## 4. 10 项 BLOCKED 定案

上一轮的 B1..B10 里，属本边界的 6 项（B1..B6）加 B9，**7 项定案、1 项部分定案、2 项仍 BLOCKED**。

| # | 上一轮 | 本轮 | 依据 |
|---|---|---|---|
| B1 | `0x0163/0x0166/0x0167` 是否存在 | **CLOSED** | 三者在 DBServer type-1 跳表里各有实臂 |
| B2 | `0x0177/0x0180/0x0187/0x0188/0x00C9` 走哪条链路 | **CLOSED** | 全在同一条 DBServer 入站链路；前三个是 **type-2**，`0x0188` 是 **type-3**，`0x00C9` 是它的应答 |
| B3 | 宗派 `0x0170/0x0071` | **CLOSED** | 原生 DBServer 命令，**不是眼神插件** |
| B4 | type-1 头 `+0x35` 第三 ShortString 槽 | **CLOSED** | 至少 12 个 DBServer 构造器写它，4 个分发器臂读它 |
| B5 | `0x003C/0x003D` 的出处 | **CLOSED** | `0x003D` 是角色注册，`0x003C` 是人数上报 |
| B6 | 36 个应答体逐字段布局 | **部分 CLOSED** | 40 个构造器的 type / DataLength / 字符串槽已全表化（§6）；少数变长体的字段语义仍缺 |
| B9 | `TSelectGroupInfo` 哪些字段由 DB 填 | 仍 `BLOCKED` | 属 5600 链路，本轮未覆盖 |
| B10 | DBServer 实现不可得 | **CLOSED**（前提被推翻） | §1 |

### 4.1 B1 — `0x0163 / 0x0166 / 0x0167` 是真命令

三个臂都只做"从 0x48 头里取 ShortString → 调 worker"：

```
; 0x0163  arm 0x598FFF -> worker sub_59AE6C -> 应答 0x0059
00599008  83 c2 35   add edx,0x35   ; 第三槽
00599020  83 c2 25   add edx,0x25   ; 角色名
00599038  83 c2 10   add edx,0x10   ; 账号
0059904A  e8 1d1e0000 call 0x59AE6C

; 0x0166  arm 0x5990E0 -> worker sub_59B258 -> 应答 0x005E
005990E9  83 c2 35   add edx,0x35
00599101  83 c2 25   add edx,0x25
00599113  e8 40210000 call 0x59B258

; 0x0167  arm 0x59911D -> worker sub_59C184 -> 应答 0x0070
00599126  83 c2 25   add edx,0x25
00599137  e8 48300000 call 0x59C184
```

上一轮因为 `sub_6C53B8` 的 4 个动态 `ecx` 调用点解不出，**正确地**没有判 INVENTED。
现在从接收侧确认：三者都是原生命令。**M2Server 侧那 4 个动态点仍未展开**，
但已不影响判定 —— 收方认，C# 声明就不是发明。

### 4.2 B5 — `0x003D` 是连接角色注册，`0x003C` 是人数上报

`0x003D` 臂 `0x59994A`：

```
0059994D  83 78 08 00        cmp dword[payload+8], 0
00599951  0f 8e 55030000     jle exit                 ; <= 0 直接忽略
0059995A  8a 40 08           mov al, byte[payload+8]  ; 取低字节
00599960  88 82 a0400000     mov byte[conn+0x40A0], al  ; 写入连接角色
00599969  80 b8 a0400000 09  cmp byte[conn+0x40A0], 9
0059997B  ba e89c5900        mov edx, 0x599CE8        ; 'DB工具已连接'
00599987  68 009d5900        push 0x599D00            ; 'GameServer '
005999A1  68 149d5900        push 0x599D14            ; ' 已连接.'
005999C7  80 b8 a1400000 00  cmp byte[conn+0x40A1], 0 ; 只首次生效
005999D7  c6 80 a1400000 01  mov byte[conn+0x40A1], 1
005999E1  80 b8 a0400000 09  cmp byte[conn+0x40A0], 9
005999E8  0f 84 be020000     je exit                  ; DBTool 到此为止
005999EE  e8 e2070000        call 0x59A1D8            ; GameServer: 先发 0x006E
005999F6..00599A1B           两次链表推送（§3.4）
```

⇒ **`payload+0x08`（i32，须 > 0）= 对端角色号**。`9` = DBTool，其余值 = GameServer 序号，
日志拼成 `'GameServer ' + IntToStr(role) + ' 已连接.'`。
`conn+0x40A1` 是"已注册"闩，保证静态记录只推一次。

`0x003C` 臂 `0x59992A`：

```
0059992A  a1 909b5d00    mov eax,[0x5D9B90] ; mov eax,[eax]
00599934  8b 4a 08       mov ecx, dword[payload+8]     ; i32
0059993A  8a 92 a0400000 mov dl, byte[conn+0x40A0]     ; 角色
00599940  e8 23790300    call 0x5D1268
```

⇒ `payload+0x08` 是一个 i32，连同角色号交给 `sub_5D1268`（在线人数统计对象）。

**这条同时解释了 §2.1 的角色分流**：GameServer 与 DBTool 用**同一个监听端口、同一套帧协议**，
靠 `0x003D` 自报身份区分。所以 `0x0177/0x0180/0x0187/0x0188` 并不在另一条 5100 链路上
（上一轮 B2 的猜测），它们就在这条链路上。

### 4.3 B3 — 宗派 `0x0170 / 0x0071` 是原生 DBServer 功能

上一轮因为"M2Server 核心分发器不认 `0x0071`"而倾向 INVENTED，只是碍于眼神壳没敢落判。
**幸好没落判。**

```
; type-1 arm 0x599206
00599213  e8 04330000   call 0x59C51C
; worker sub_59C51C
0059C537  a1 b0ac5d00   mov eax,[0x5DACB0] ; mov eax,[eax]
0059C544  e8 277bffff   call 0x594070            ; 宗派处理，返回 1 / 2
0059C552  83 7d e8 01   cmp dword[ebp-0x18], 1
0059C563  e8 cc05f0ff   call 0x49CB34            ; 分支 1
0059C56A  83 7d e8 02   cmp dword[ebp-0x18], 2
0059C57E  e8 cd1e0000   call 0x59E450            ; 分支 2：向全体 GameServer 扇出
```

`sub_594070` 是 **`0x0071` 的构造器**，四个写入点
`0x59442E / 0x594514 / 0x5945E4 / 0x594716`（`p21_t2scan` 第 30-34 行），
帧 type 1，DataLength 有 **`0x9C`** 与 **`0x78`** 两种（体长 `0x54` / `0x30`），
三个 ShortString 槽 `+0x10/20`、`+0x25/15`、`+0x35/15` 都用。

**判定：`NativeZongpaiProtocol` 的 `0x0170/0x0071` = `FAITHFUL`。**
M2Server 分发器不认 `0x0071` 是原生自身的不对称（与 §3.6 同类），
**不构成删除 C# 常量的理由**。B3 关闭。

### 4.4 B2 — `0x0177 / 0x0180 / 0x0187` 是 type-2，`0x0188` 是 type-3

上一轮把这四个都当成 type-1 找不到，于是猜"属 GameGate↔DBServer(5100) 或 DBTool 链路"。
**帧 type 猜错了**：在 type-1 跳表里它们确实落 default（§2.2 的 38 项里就有
`0x177 0x180 0x187 0x188`），因为它们根本不是 type-1。

| 命令 | 帧 type | 臂 | 体约束 / 语义 | 应答 |
|---|---|---|---|---|
| `0x0177` | **2** | `0x599AEE` | `sub_5AD298` 返回 true 才继续；取 `body+0x14`、`body+0x18` 两个 i32 | `0x013A`（`sub_59C8E8`） |
| `0x0180` | **2** | `0x599B43` | `edx = payload+0x08`（i32）→ `sub_5CAC88` | 无 |
| `0x0187` | **2** | `0x599BA1` | `payload+0x04` 三分支：`0`→单 IP 上限设置（回显 `'单IP最大在线人数已被设置为'`）；`1`→排队系统开关（`'排队系统开启'`/`'排队系统关闭'`）；其它→忽略 | 无（回文本行） |
| `0x0188` | **3** | `0x599DF2` | 见 §5 | **`0x00C9`** |

字节：`0x599891 3D 77 01 00 00 cmp eax,0x177` / `0x599898 0F 84 50 02 00 00 je 0x599AEE`；
`0x5998DB FF 24 85 E2 98 59 00 jmp [eax*4+0x5998E2]` 的第 1 / 8 项分别是
`0x599B43`(0x180) / `0x599BA1`(0x187)。

### 4.5 B4 — type-1 头 `+0x35` 第三槽是实字段

上一轮只在 hero-save 路径看到一次写入，语义存疑。DBServer 侧两端都在用：

- **读**（分发器臂）：`0x598C30`(0x0151)、`0x598C97`(0x0152)、`0x599008`(0x0163)、`0x5990E9`(0x0166)
  —— 全部 `add edx,0x35` 后 `call 0x404E5C`（取 ShortString）。
- **写**（应答构造器，§6 表）：12 个构造器写 `+0x35/cap 15`，
  含 `0x0053 0x0055 0x0056 0x0059 0x005A 0x005E 0x005F 0x0064 0x0071 0x0138`。

⇒ `+0x35` 是与 `+0x10`(账号,cap 20) / `+0x25`(角色名,cap 15) 并列的**第三个 ShortString 槽，
cap 15**，双向使用。C# 若不建模，这些应答的第三个字符串会丢。

---

## 5. type-3 全链路（`0x0188` → `0x00C9`）—— 逐字段

type-3 只有这一条链路，这里给出完整布局。

### 5.1 请求 `0x0188`

臂 `0x599DF2`：

```
00599DF8  83 c2 29   add edx, 0x29    ; payload+0x29 ShortString -> arg
00599E0A  83 c2 08   add edx, 0x08    ; payload+0x08 ShortString -> arg
00599E1A  8a 90 a0400000  mov dl, byte[conn+0x40A0]   ; 角色号一并传下
00599E27  e8 84060100     call 0x5AA4B0
```

### 5.2 应答 `0x00C9`（`sub_5AA4B0`）

```
005AA4C7  8b 40 14     mov eax,[acctMgr+0x14]      ; 账号表
005AA4CD  e8 964900 00 call 0x5AEE68               ; 按账号名查
005AA4DE  66 8b 40 08  mov ax, word[acct+8]        ; 该账号的角色数
005AA4F2  6b c0 3c     imul eax, eax, 0x3C         ; 每角色 0x3C 字节
005AA4F5  83 c0 4c     add eax, 0x4C               ; + 0x4C（= 0x0C 帧头 + 0x40 载荷头）
005AA4F8  e8 4b8ae5ff  call 0x402F48               ; AllocMem
005AA50F  e8 d491e5ff  call 0x4036E8               ; 全零
...
005AA61F  c7 00 77bbaa33  mov dword[frame], 0x33AABB77
005AA628  66 c7 40 04 03 00  mov word[frame+4], 3          ; Type = 3
005AA632  6b c0 3c        imul eax, count, 0x3C
005AA635  83 c0 40        add eax, 0x40
005AA63B  89 42 08        mov [frame+8], eax                ; DataLength = 0x40 + n*0x3C
005AA64A  66 c7 00 c9 00  mov word[payload], 0xC9           ; Cmd
005AA656  89 42 04        mov [payload+4], count            ; i32 实际条数
005AA675  83 c0 08 / b1 20  payload+0x08  ShortString cap 32
005AA69B  83 c0 29 / b1 14  payload+0x29  ShortString cap 20
```

**载荷头 = `0x40` 字节**，与 M2Server 侧 type-3 最小体长
`00713EF7 837B0440 cmp dword[ebx+4],0x40` **完全吻合** —— 这是两端独立的交叉验证。

| 偏移 | 宽度 | 内容 |
|---|---|---|
| `0x00` | u16 | Cmd `0x00C9` |
| `0x02` | u16 | 未写入（保留） |
| `0x04` | i32 | 角色条数 |
| `0x08` | ShortString cap **32**（33 B） | 请求回显（路由串） |
| `0x29` | ShortString cap **20**（21 B） | 请求回显（账号） |
| `0x3E..0x3F` | — | 对齐到 `0x40` |
| `0x40 + i*0x3C` | `0x3C` | 第 i 个角色条目 |

角色条目（`0x3C` 字节，缓冲区已整体清零，未写字节即 0）：

| 偏移 | 宽度 | 来源 | 字节 |
|---|---|---|---|
| `0x00` | i32 | `[chr+0x74]` | `005AA55A` / `005AA562` |
| `0x04` | i32 | `[chr+0x70]` | `005AA567` / `005AA56D` |
| `0x08` | ShortString cap 15 | `[chr+0x26]` 角色名 | `005AA573 add eax,8` + `005AA57C b1 0f` |
| `0x18` | u16 | `[chr+0x3E]` 等级 | `005AA589` / `005AA58D` |
| `0x1A` | u8 | `[chr+0x3A]` 性别 | `005AA5E7` / `005AA5EA` |
| `0x1B` | ShortString cap 4（5 B） | 职业名，按 `[chr+0x39]` 选表 | `005AA5AA lea edi,[eax+0x1B]` + `movsd;movsb` |
| `0x1F..0x3B` | — | 恒零 |

职业串表（GBK，Delphi ShortString，长度字节 `04`）：

```
005AA6D4  04 d5 bd ca bf   '战士'    ; [chr+0x39] == 0
005AA6DC  04 b7 a8 ca a6   '法师'    ; == 1
005AA6E4  04 b5 c0 ca bf   '道士'    ; == 2
005AA6EC  04 b4 cc bf cd   '刺客'    ; == 3
005AA5A5  eb 3a  jmp                 ; 其它 -> 不写，保持全零
```

跳过条件：`005AA549 80 78 37 01 cmp byte[chr+0x37],1 / je` —— `chr+0x37 == 1` 的角色不计入。

### 5.3 对账 `DBSvr/Core/NativeType3Protocol.cs`

| 项 | 原生 | C# | 判定 |
|---|---|---|---|
| 请求码 | `0x0188` | `QueryCharactersCommand = 0x0188` (:11) | `FAITHFUL` |
| 应答码 | `0x00C9` | `QueryCharactersResponseCommand = 0x00C9` (:12) | `FAITHFUL` |
| 载荷头 | `0x40` | `HeaderSize = 0x40` (:13) | `FAITHFUL` |
| 条目 | `0x3C` | `CharacterEntrySize = 0x3C` (:14) | `FAITHFUL` |
| 槽 1 | `+0x08` cap 32 | `RouteOffset = 0x08 / RouteCapacity = 32` (:16-17) | `FAITHFUL` |
| 槽 2 | `+0x29` cap 20 | `PtidOffset = 0x29 / PtidCapacity = 20` (:18-19) | `FAITHFUL` |
| 条数 | i32 @`+0x04` | `WriteUInt32LittleEndian(payload[4..8], (ushort)Count)` (:99) | `FAITHFUL` |
| 条目 `+0x00/+0x04` | `[chr+0x74]` / `[chr+0x70]` | UserId 高 32 位 @0、低 32 位 @4 (:116-119) | `FAITHFUL` |
| 条目 `+0x08` | cap 15 | `WriteShortStringBytes(entry, 0x08, 15, …)` (:120) | `FAITHFUL` |
| 条目 `+0x18` | u16 | Level (:124) | `FAITHFUL` |
| 条目 `+0x1A` | u8 | Sex (:126) | `FAITHFUL` |
| 条目 `+0x1B` | cap 4，4 个职业名 | `WriteShortStringBytes(entry, 0x1B, 4, GetJobText(Job))` (:127) | `FAITHFUL` |
| 职业名 | 战士/法师/道士/刺客，其它留空 | `0=>"战士" 1=>"法师" 2=>"道士" 3=>"刺客" _=>""` (:187-194) | `FAITHFUL`（含 default 分支） |

**扇出规则**也对得上。原生 `sub_59E450`：

```
0059E49F  80 7d fb 00     cmp byte[senderRole], 0
0059E4A3  75 1e           jne 0x59E4C3
0059E4A8  80 b8 a0400000 09  cmp byte[peer+0x40A0], 9
0059E4AF  74 32           je skip                  ; role 0 -> 发给一切非 DBTool
0059E4C1  eb 20           jmp continue             ; 继续循环（全量广播）
0059E4C6  8a 80 a0400000  mov al, byte[peer+0x40A0]
0059E4CC  3a 45 fb        cmp al, byte[senderRole]
0059E4CF  75 12           jne skip                 ; role != 0 -> 只发同角色号
0059E4E1  eb 08           jmp 0x59E4EB             ; 命中即 break（只发第一个）
```

C# `NativeType3Protocol.cs:135-153`：

```csharp
senderGroup == 0 ? peerGroup != 9 : peerGroup == senderGroup
...
if (senderGroup != 0) break;
```

**逐条一致，连"非零角色只发第一个匹配者就 break"这个细节都对。`FAITHFUL`。**

---

## 6. 40 个出站构造器全表（B6）

判据：函数体内含 `mov dword ptr [reg], 0x33AABB77`。对每个构造器读出帧 type、
DataLength 写法、载荷命令字、以及写入的 ShortString 槽（`add eax,off` + `mov cl,cap`）。
脚本 `q12_replies.py`。

| 构造器 | type | DataLength | 命令 | ShortString 槽 |
|---|---|---|---|---|
| `sub_594070` | 1 | `0x9C` / `0x78` | `0x0071` | `+0x10/20 +0x25/15 +0x35/15` |
| `sub_5982D0` | 1 | `0x48` | `0x0056` | `+0x10/20 +0x25/15 +0x35/15` |
| `sub_5984A8` | 1 | `0x48` | `0x0058` | `+0x10/20` |
| `sub_598584` | 1 | `0x48` | `0x0052` | `+0x10/20` |
| `sub_598618` | 1 | `0x48` | `0x0046` `0x0047` | `+0x25/15` |
| `sub_5986CC` | 1 | 变长 `0xF0F0+extra` | `0x0050` | `+0x10/20 +0x25/15` |
| `sub_599680` | 1 | `0x48` | `0x0051` | `+0x10/20 +0x25/15` |
| `sub_599EA8` | 1 | `0x48` | `0x0138` | `+0x10/20 +0x25/15 +0x35/15` |
| `sub_599FBC` | 1 | 变长 `0x48+body` | `0x0055` | `+0x10/20 +0x25/15 +0x35/15` |
| `sub_59A0FC` | 1 | `0x48` | `0x0054` | `+0x10/20 +0x25/15` |
| `sub_59AD4C` | 1 | `0x48` | `0x0053` | `+0x25/15 +0x35/15` |
| `sub_59AE6C` | 1 | `0x48` | `0x0059` | `+0x10/20 +0x25/15 +0x35/15` |
| `sub_59AF6C` | 1 | `0x48` | `0x005F` | `+0x10/20 +0x25/15 +0x35/15` |
| `sub_59B17C` | 1 | 变长 | `0x005D` | `+0x25/15` |
| `sub_59B258` | 1 | `0x48` | `0x005E` | `+0x25/15 +0x35/15` |
| `sub_59B338` | 1 | `0x48` | `0x005A` | `+0x25/15 +0x35/15` |
| `sub_59B470` | 1 | `0x48` | `0x0060` | `+0x10/20 +0x25/15` |
| `sub_59B558` | 1 | `0x48` | `0x0064` | `+0x10/20 +0x25/15 +0x35/15` |
| `sub_59B730` | 1 | `0x48` | `0x0064` | `+0x25/15` |
| `sub_59B800` | 1 | `0x48` | `0x0064` | `+0x10/20 +0x25/15` |
| `sub_59B9F0` | 1 | `0x48` | `0x0064` | `+0x10/20 +0x25/15` |
| `sub_59BBEC` | 1 | `0x48` | `0x0064` | `+0x25/15` |
| `sub_59BCB8` | 1 | `0x48` | `0x0061` | `+0x10/20 +0x25/15` |
| `sub_59BE08` | 1 | `0x48` | `0x0136` | `+0x25/15` |
| `sub_59BEC0` | 1 | `0xA07C` / `0x48` | `0x0137` | `+0x10/20` |
| `sub_59C184` | 1 | `0x48` | `0x0070` | `+0x10/20 +0x25/15` |
| `sub_59C594` | 1 | 变长 | `0x0062` | — |
| `sub_59C6AC` | 1 | `0x48` | `0x0063` | `+0x10/20 +0x25/15` |
| `sub_59C7F4` | 1 | `0x48` | `0x012F` | `+0x25/15` |
| `sub_59C8E8` | 1 | `0x48` | `0x013A` | — |
| `sub_59C970` | 1 | `0x48` | `0x013D` | — |
| `sub_59CA94` | 1 | `0xF0F0` | `0x012E` | — |
| `sub_59CB6C` | 1 | 变长 | `0x0132` | `+0x25/15` |
| `sub_59CC48` | 1 | `0x48` | `0x0131` | `+0x10/20 +0x25/15` |
| `sub_59CD4C` | 1 | 变长 | `0x0139` | `+0x18/20 +0x54/15` |
| `sub_59E1CC` | 1 | `0x48` | `0x012D` | `+0x10/20 +0x25/15` |
| `sub_59E298` | **2** | 变长 | `0x0130` | — |
| `sub_59E338` | 1 | `0x48` | `0x013B` | — |
| `sub_59E3C4` | 1 | `0x48` | `0x013C` | — |
| `sub_5AA4B0` | **3** | `0x40+n*0x3C` | `0x00C9` | `+0x08/32 +0x29/20`（条目内 `+0x08/15`） |

### 6.1 与 M2Server 接收集合对账

M2Server `sub_654140` 认识 36 个码。两侧取差集：

**DBServer 发、M2Server 落 default（7 个）** ——
`0x0059`、`0x0064`、`0x0071`、`0x00C9`、`0x0130`、`0x0136`、`0x0137`。

- `0x0064` 有 5 个构造器，全部是 **DBTool 分支**（type-1 `0x0100..0x0104`）的应答，
  不上 GameServer 链路，M2Server 不认识**属正常**。
- `0x00C9` 是 type-3，M2Server 的 type-3 处理器 `sub_713A98` 本就读一个 word 后丢弃。
- `0x0130` 是 **type-2**，M2Server 由延迟链 `0x71302B` 处理 —— 不该拿 type-1 跳表去衡量。
  上一轮把它算进 type-1 缺口是分类错误。
- `0x0059`、`0x0071`、`0x0136`、`0x0137` 是真正的原生不对称（同 §3.6）。

**M2Server 认、本 DBServer 构建从不发（6 个）** ——
`0x0057 0x005B 0x005C 0x0078 0x0079 0x007A`。
上一轮把它们记成 "C# MISSING"；实际本构建的 DBServer 根本不产生这些码，
**C# 不实现它们不产生互通风险**，应从 MISSING 降级为"原版接收侧遗留"。

### 6.2 由此产生的第二处判定更正：`0x0059` 不是 INVENTED

上一轮 I5 判 `NativeHeroDbFrameCodec.DeleteResponseCommand = 0x0059` 为 `INVENTED`，
理由是 M2Server 跳表第 `0x59-0x46 = 0x13` 项落 default。理由成立，结论同样只覆盖接收侧。

`sub_59AE6C` 是 `0x0059` 的构造器，而它正是 type-1 `0x0163` 的 worker（§4.1）。
**DBServer 确实会发 `0x0059`。判定改为 `FAITHFUL`，I5 撤销。**

---

## 7. 人物存档记录布局 —— DBServer 侧逐字节复核

这是"换 DBSvr 要不要数据迁移"的唯一决定性证据，因为**持久化格式只有 DBServer 知道**。

### 7.1 载入路径 `sub_5986CC`（应答 `0x0050`）

```
005986F0  68 00ef0000     push 0xEF00                  ; 向 MySQL 要 0xEF00 字节
005986FD  8d 8d d010ffff  lea ecx, [ebp-0xEF30]        ; 栈上接收缓冲
0059870D  e8 c2ec0000     call 0x5A73D4                ; 读 blob
00598712  89 45 ec        mov [ebp-0x14], eax          ; 实际长度
00598715  81 7d ec 00ef0000  cmp dword[ebp-0x14], 0xEF00
0059871C  0f 85 71010000  jne 0x598893                 ; 长度不等 -> 整帧不发（fail-closed）
0059872C  b9 00ef0000     mov ecx, 0xEF00
00598734  e8 3f220000     call 0x59A978                ; 就地规整（写 +0x50/+0x51/+0x52 等）
00598758  8b 45 e4        mov eax, [ebp-0x1C]          ; ScriptData 长度
0059875B  05 fcf00000     add eax, 0xF0FC              ; 总分配
00598766  e8 6126e7ff     call 0x40ADCC
0059877D  c7 00 77bbaa33  mov dword[frame], 0x33AABB77
00598786  66 c7 40 04 0100  mov word[frame+4], 1
0059878F  05 f0f00000     add eax, 0xF0F0
00598797  89 42 08        mov [frame+8], eax           ; DataLength = 0xF0F0 + extra
005987A6  66 c7 00 5000   mov word[payload], 0x50
005987C7  83 c0 10 / b1 14  payload+0x10 cap 20        ; 账号
005987ED  83 c0 25 / b1 0f  payload+0x25 cap 15        ; 角色名
005987FA  83 c0 54        add eax, 0x54                ; rec = frame+0x54 = payload+0x48
00598803  8d b5 d010ffff  lea esi, [ebp-0xEF30]
0059880B  b9 c03b0000     mov ecx, 0x3BC0
00598810  f3 a5           rep movsd                    ; 0x3BC0*4 = 0xEF00 -> rec+0x0000
00598824  66 89 82 3c050000  mov word[rec+0x53C], ax   ; 唯一的就地覆写（见 7.3）
00598833  8d ba 00ef0000  lea edi, [rec+0xEF00]
00598839  b9 28000000     mov ecx, 0x28
0059883E  f3 a5           rep movsd                    ; 0x28*4 = 0xA0   -> rec+0xEF00
0059884E  8d ba a0ef0000  lea edi, [rec+0xEFA0]
00598854  b9 42000000     mov ecx, 0x42
00598859  f3 a5           rep movsd                    ; 0x42*4 = 0x108  -> rec+0xEFA0
0059886A  8d 90 fcf00000  lea edx, [frame+0xF0FC]
00598876  e8 55a9e6ff     call 0x4031D0                ; ScriptData -> frame+0xF0FC
```

### 7.2 几何自洽（全部由字节算出，无一处推断）

```
blob        0xEF00                       (0x3BC0 dwords)
tail 块A    0xEF00 .. 0xEF9F  = 0x00A0   (0x28 dwords)   连续 ✔
tail 块B    0xEFA0 .. 0xF0A7  = 0x0108   (0x42 dwords)   连续 ✔
tail 合计                       0x01A8   = 0xA0 + 0x108
记录定长部分                     0xF0A8   = 0xEF00 + 0x1A8
payload                          0xF0F0   = 0x48 + 0xF0A8    ✔ 与 0x59878F 一致
frame                            0xF0FC   = 0x0C + 0xF0F0    ✔ 与 0x59875B 一致
前缀                             8        = 0xEF00 - 0xEEF8
```

**关键结论：`rep movsd` 是逐字节搬运，MySQL blob 与线上记录之间没有任何变换。**
落库格式 ≡ 线上格式。加上 `0x598715` 的 `cmp … 0xEF00 / jne` 硬闸，
DBServer 只接受**恰好** `0xEF00` 字节的 blob。

### 7.3 唯一的例外：`rec+0x53C`

```
00598812  a1 bc9e5d00     mov eax,[0x5D9EBC] ; mov eax,[eax]   ; 账号管理器
00598819  8b 55 f8        mov edx,[ebp-8]                      ; 账号名
0059881C  e8 e74d0100     call 0x5AD608
00598824  66 89 82 3c050000  mov word[rec+0x53C], ax
```

`sub_5AD608`：`[mgr+0x14]` 里按账号名查（`sub_5AEE68`），命中取 `word[acct+0x1C]`，
未命中写 `0`（`005AD638 66 c7 45 f6 0000`）。

⇒ **记录偏移 `0x53C`（记录本体内 `0x53C - 8 = 0x534`）在载入时被 DBServer 用
内存态账号对象的 `+0x1C` 覆盖，blob 里的原值被丢弃。**

`acct+0x1C` 的写入点全镜像只有两处：`0x426436` 与 `0x5AD676`。语义 **UNPROVEN**，不猜。

**C# 现状**：全仓 `0x53C` / `0x534` **0 命中**（`DBSvr` 与 `GameSvr` 都没有）。
判定 **`MISSING`（记录布局）**：C# DBSvr 回放 blob 原值，原版会覆盖。
影响面取决于 `acct+0x1C` 的语义 —— 在拿到语义前**不得凭空实现**，
但必须登记，否则换 DBSvr 时这个字段会出现两版不一致。

### 7.4 与 C# 常量对账

| 量 | 原生（DBServer 字节） | C# | 判定 |
|---|---|---|---|
| type-1 头 | `0x48` | `NativeHumanDbCodec.MessageSize = 0x48` | `FAITHFUL` |
| 记录前缀 | `8`（`0xEF00 - 0xEEF8`） | `HumanInfoPrefixSize = 0x08` | `FAITHFUL` |
| 记录本体 | `0xEEF8` | `NativeHumanDataCodec.DataRecordSize = 0xEEF8` | `FAITHFUL` |
| MySQL blob | `0xEF00`（硬闸） | `NativeHumanDataCodec.DataSizeMarker = 0xEF00` | `FAITHFUL` |
| 会话尾块 | `0x1A8` = `0xA0 + 0x108` | `SessionSuffixSize = 0x01A8` | `FAITHFUL` |
| 记录定长总长 | `0xF0A8` | `HumanInfoSize = 0xF0A8` | `FAITHFUL` |
| payload | `0xF0F0` | `ScriptDataOffset = 0xF0F0` | `FAITHFUL` |
| frame | `0xF0FC` | 由 `0x0C + 0xF0F0` 导出 | `FAITHFUL` |
| 记录锚点 | `frame+0x54` = `payload+0x48` | 一致 | `FAITHFUL` |
| 尾块两段切分 | `0xA0` + `0x108` | `NativeDbServerProtocol.cs:523-524` 注释即此 | `FAITHFUL` |
| `rec+0x53C` 覆写 | 有 | **无** | **`MISSING`** |

### 7.5 定案：换 DBSvr **不需要数据迁移**

前一轮的说法「记录布局没有发现偏差 ⇒ 换 DBSvr 不需要数据迁移」当时**只有 M2Server 侧证据，
不足以支撑**。现在补齐了 DBServer 侧：

1. 持久化 blob 恒 `0xEF00` 字节，DBServer 用 `cmp/jne` 硬校验，长度不符直接不回包；
2. blob → 线上记录是 `rep movsd` **恒等搬运**，无字段重排、无编解码；
3. C# 的 6 个尺寸常量与原生逐个相等（§7.4）。

⇒ **结论成立，且现在有 DBServer 侧字节支撑：老库可以直接被 C# DBSvr 读，反之亦然。**

**唯一附加条件**：`rec+0x53C`（§7.3）是 DBServer 在**载入时**覆写的字段，
不是持久化字段，所以**不影响迁移**，但影响"两版并存时该字段取值不同"。
按 §1.4 登记为记录布局缺口，不作为迁移阻塞项。

---

## 8. 本轮的判定更正汇总

| 项 | 上一轮 | 本轮 | 决定性字节 |
|---|---|---|---|
| I1 `0x0073` | `INVENTED`，建议删 | **`FAITHFUL`** | `sub_5C7148` @ `0x5C9123`，挂 `[self+0x14]` |
| I2 `0x0075` | `INVENTED`，建议删 | **`FAITHFUL`** | `sub_5C78B4` @ `0x5C913B` |
| I3 `0x0076` | `INVENTED`，建议删 | **`FAITHFUL`** | `sub_5C8038` @ `0x5C9147` |
| I4 `0x006D` | `INVENTED`，建议删 | **`FAITHFUL`** | `sub_5C3AE4` @ `0x5C9153` |
| I5 `0x0059` | `INVENTED` | **`FAITHFUL`** | `sub_59AE6C` 是它的构造器 |
| I6 `0x00C9` | `INVENTED`（对 6000 链路） | **`FAITHFUL`** | `0x5AA64A mov word[payload],0xC9`，type 3 |
| B3 `0x0170/0x0071` | 倾向 `INVENTED` | **`FAITHFUL`** | `sub_594070` 四个写入点 |
| D8 `0x0041` 帧 type | `DIVERGENT`（存疑） | **`DIVERGENT`（确证）** | `0x599A9E cmp dword[ebp+8],0x14`，type-2 表第 6 项 |
| M2/M3/M4（`0x6F`/`0x72`/`0x130`） | `MISSING` | `MISSING`（维持） | `0x0130` 应归 type-2 而非 type-1 缺口 |
| MISSING `0x57/0x5B/0x5C/0x78/0x79/0x7A` | `MISSING` | 降级为"原版接收侧遗留" | 本 DBServer 构建 0 个构造器 |

**上一轮建议的 P3「删除 type-2 四个发明码，零风险」是错的。**
若照做，C# DBSvr 会比原版 DBServer 少发 4 类静态表，
并且 `0x0059` / `0x00C9` / `0x0071` 三个常量也会被误删。

## 8bis. 第三处更正：D8「`0x0041` 被 C# 归到 type-1」不成立

上一轮据 `DBSvr/Core/NativeUserAdmissionControl.cs:134` 的**文件名**推断 C# 把 `0x0041`
归入 type-1 族。逐行核对否定该推断：该常量在 `NativeType2AdmissionProtocol` 类里，
分发发生在 `GameSocService.cs:578`，位于 `frame.Type == 1` 与 `frame.Type == 3`
两个分支 **return 之后**的 type-2 分支内。

逐字段与原生 `0x599A9E` 臂对照：

| 项 | 原生 | C# | 判定 |
|---|---|---|---|
| 帧 type | 2 | type-2 分支（`GameSocService.cs:555+`） | `FAITHFUL` |
| 体长 | `0x599A9E 83 7D 08 14 cmp dword[ebp+8],0x14` + `jne` | `Suffix.Length != DenyIpBodySize(20)` → false | `FAITHFUL` |
| 体 `+0x00` | ShortString cap 15（`0x599AB4 call 0x404E5C`） | `Suffix[0]`，`length > 15 → false` | `FAITHFUL` |
| 体 `+0x10` | i32（`0x599ABF 8B 48 10 mov ecx,[eax+0x10]`） | `body[16]` 起 4 字节 | `FAITHFUL` |

**D8 撤销。** 原生帧几何 `DataLength = 0x20 = 0x0C 载荷头 + 0x14 体` 与 M2Server 发送侧
`0x6BEF5F mov dword[ebp-0x34],0x20` 也吻合，三方一致。

---

## 9bis. 四态对账

### 9bis.1 type-1 请求空间（GameServer 角色）

自动比对（C# `const ushort` 声明值 vs 原生 40 个活命令）：

| 状态 | 数量 | 明细 |
|---|---|---|
| `FAITHFUL` | **40 / 40** | 原生 40 个活命令 C# 全部有对应处理 |
| `MISSING` | **0** | — |
| `INVENTED` | **0** | — |

初次比对报出 9 个"原生认、C# 无常量"（`0x0160..0x0167`、`0x0194`），
系扫描范围只覆盖 `DBSvr/` 所致：这 9 个的常量在
`SystemModule/Packet/NativeHeroDbFrameCodec.cs:22-30`，
分发走 `GameSocService.cs:536-538` 的区间判断
`command >= LoadCommand(0x0160) && command <= BuildThreeSlotCommand(0x0167)`
加 `:531` 的 `DetachCommand(0x0194)`。**区间连续覆盖 8 个，无缺口。**

初次比对报出的 24 个"C# 有、原生 type-1 表没有"全部可解释，**无一是 INVENTED**：

- `0x0100..0x0104`（5 个）—— 正是 §2.1 发现的 **DBTool 命令空间**。
  C# `NativeDbToolProtocol.cs:42-46` 命名为
  `DeleteCommand / HumanWriteCommand / HumanReadCommand / HeroWriteCommand / HeroReadCommand`。
  与原生 role-9 分支跳表 `0x598909` 的 5 项一一对应 —— **两边独立得出同一结论，互为交叉验证**。
- `0x012E 0x012F 0x0131 0x0132 0x0136 0x0137 0x0138 0x0139 0x013A 0x013C 0x013D`（11 个）
  —— 是**应答**码（DBServer→GameSvr），本就不该出现在 DBServer 的入站表；
  全部能在 §6 的构造器表里找到。
- `0x0177 0x0180 0x0184 0x0185 0x0186 0x0187 0x0191`（7 个）—— **type-2** 请求（§2.3）。
- `0x0188`（1 个）—— **type-3** 请求（§2.4）。

### 9bis.2 type-2 / type-3

| 边界 | `FAITHFUL` | `MISSING` | `INVENTED` | `BLOCKED` |
|---|---|---|---|---|
| type-1 请求（40） | 40 | 0 | 0 | 0 |
| type-1 应答（本构建 37 个构造器码） | 见 §6.1 | 6（原版接收侧遗留，非风险） | 0 | 变长体语义 N3 |
| type-2 请求（14） | 14 | 0 | 0 | 0 |
| type-2 静态记录（9） | 9 | 0 | **0（上一轮误判 4）** | 0 |
| type-3（1 请求 + 1 应答） | 2 | 0 | 0 | 0 |
| 人物记录布局（8 个尺寸量） | 8 | 1（`rec+0x53C`） | 0 | `acct+0x1C` 语义 N1 |

`IsSilentNoOpCommand`（`NativeType2Protocol.cs:28-33`）声明
`0x0181 0x0182 0x0183 0x0188..0x0190` 为静默无操作 ——
与我从跳表 `0x5998E2` 算出的 12 个"在表内但落 default"**完全一致**（§2.3）。`FAITHFUL`。

---

## 9. 仍然 BLOCKED

| # | 项 | 缺什么 |
|---|---|---|
| N1 | `acct+0x1C` 的业务语义（决定 `rec+0x53C`） | 两个写入点 `0x426436` / `0x5AD676` 未展开；不猜 |
| N2 | M2Server 侧 `sub_6C53B8` 的 4 个动态 `ecx` 调用点 | 不再阻塞判定（接收侧已确认），仅影响发送侧覆盖率 |
| N3 | 变长应答体（`0x0055 0x005D 0x0062 0x0132 0x0139 0x0130`）的逐字段语义 | §6 只给出 type/长度/字符串槽 |
| N4 | `0x0177` 的 `body+0x14 / +0x18` 两个 i32 语义 | `sub_5AD298` / `sub_59C8E8` 未展开 |
| N5 | B9 `TSelectGroupInfo` 字段归属 | 属 5600 链路，本轮未覆盖 |

