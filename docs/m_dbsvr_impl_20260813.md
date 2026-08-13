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

（后续章节随分析推进补入）
