# 0x33AABB77 (77 BB AA 33) 内部帧逆向对照表

> 分支 `w/gateframe` · 工作树 `D:\loym2\.claude\wt2\gateframe`
> 底本 M2Server `staging\_reunpack_work\flat_image.bin` (ImageBase 0x400000)
> 底本 GameGate `staging\_gg_reunpack_work\dump_gg2025\flat_image.bin` (ImageBase 0x400000)
> 反汇编: capstone 5.0.7 / Python 3.11 · 脚本见 `_re_scratch/`
> 铁律: 一比一等价复刻, 所有结论均有二进制地址/字节支撑; 证据不足处 fail-closed。

---

## 1. 结论摘要 (先看这里)

1. **GameGate ↔ M2Server 转发帧 = 16 字节传输头 (0x10)**, 逐字段:
   `+0x00 Magic(4) · +0x04 ConnID(4) · +0x08 SeqID(4) · +0x0C Cmd(2) · +0x0E BodyLen(2) · +0x10 Payload`
   总长 = `0x10 + BodyLen`。**控制/ACK 与数据帧共用同一 16 字节头**, 唯一区别是 BodyLen 与 Cmd。
2. 原 C# `InternalPacket77.HEADER_SIZE=24` 且 `FrameLen@+0x0C` 系**虚构布局**, 已纠正为 16 字节头 (Cmd@+0x0C, BodyLen@+0x0E)。
3. 任务线索里"**28 字节数据帧 / payload 起于 +0x1C**"是**误读**: 数据帧构造器确实用 `edx/ecx=0x1C` 做 memset, 但那是 **16 字节头 + 12 字节内层子头** 的暂存区 (`0x1C=28`)。传输头仍是 16, `BodyLen(+0x0E)=12+文本长=总长-0x10`, 文本追加在 `+0x1C`。内层 12 字节子头 (Recog/Ident/Param/Tag/Series) 属 **Payload**, 由 `LegacyGateType18` 建模, 不是传输头字段。
4. magic `0x33AABB77` 在 M2 内被**多条链路复用**, 头长各异 (见 §4)。GameGate 链是 16 字节; DBServer(6000) 链是 12 字节 (已由 `LegacyDbServerFrameCodec` 建模); 另有 20 字节跨服线缆帧与若干"内存命令结构标记"。**只有 16 字节那族才是本任务的 GameGate↔M2 转发帧。**
5. 已修复的 C# 缺陷: 两个流解析器 (`InternalPacket77FrameParser` / `GameGateServerFrameParser`) 的通用分支原把 `word[+0x0C]` 当帧长 (实为 Cmd), 改为 `0x10 + word[+0x0E]`; 回归断言 `ProtocolRegressionCheck` 由旧 24/28 布局改为 16/20。`SystemModule` 与 `GameGate-CS` 均 `dotnet build` 0 错误。

---

## 2. GameGate ↔ M2Server 权威帧格式 (16 字节头)

### 2.1 头字段逐偏移证据 (构造器 0x637AC1, M2)

主缓冲写入器 `sub_637A??`(对象缓冲 `[+0x180]`, 写指针 `[+0x184]`):

```
0x637AC1  c7 00 77 bb aa 33     mov [eax], 0x33AABB77      ; +0x00 Magic
0x637AC7  66 89 78 0c           mov [eax+0x0C], di          ; +0x0C Cmd   (word)
0x637ACE  89 50 04              mov [eax+0x04], edx         ; +0x04 ConnID(=[ebp-8])
0x637AD4  89 50 08              mov [eax+0x08], edx         ; +0x08 SeqID (=[ebp+0x10])
0x637AD7  66 89 58 0e           mov [eax+0x0E], bx          ; +0x0E BodyLen(word)=ebx
0x637ADE  83 80 84 01 00 00 10  add [eax+0x184], 0x10       ; 头前进 16 字节
0x637AFD  8b 45 0c ...          mov eax,[ebp+0x0C]          ; body 源
0x637B02  e8 59 b7 dc ff        call memcpy                 ; body memcpy 到 +0x10
0x637B0A  01 98 84 01 00 00     add [eax+0x184], ebx        ; 再前进 BodyLen
```

| 偏移 | 大小 | 字段 | 写入指令 (地址) |
|---|---|---|---|
| +0x00 | 4 | Magic = 0x33AABB77 | `0x637AC1 mov[eax],77BBAA33` |
| +0x04 | 4 | ConnID (nSocket/连接句柄) | `0x637ACE mov[eax+4],edx` |
| +0x08 | 4 | SeqID (会话/上下文) | `0x637AD4 mov[eax+8],edx` |
| +0x0C | 2 | Cmd (ident/命令码, switch 判别) | `0x637AC7 mov[eax+0xC],di` |
| +0x0E | 2 | BodyLen (= 总长 − 0x10) | `0x637AD7 mov[eax+0xE],bx` |
| +0x10.. | BodyLen | Payload (body) | `0x637B02 memcpy` |

### 2.2 控制 / ACK 帧 (BodyLen=0, 恒 16 字节)

构造器 `sub_5F61C5` (任务线索直接命中):

```
0x5F61C5  c7 45 e0 77 bb aa 33  mov [ebp-0x20], 0x33AABB77  ; +0x00 Magic
0x5F61CC  ...                   mov [ebp-0x1C], eax         ; +0x04 ConnID
0x5F61D6  ...                   mov [ebp-0x18], eax         ; +0x08 SeqID
0x5F61D9  66 c7 45 ec 0b 00     mov word [ebp-0x14], 0x0B   ; +0x0C Cmd = 0x0B
0x5F61DF  66 c7 45 ee 00 00     mov word [ebp-0x12], 0      ; +0x0E BodyLen = 0
0x5F61EA  b9 10 00 00 00        mov ecx, 0x10               ; 发送长度 = 16
0x5F61F2  e8 ..                 call send(0x4C93F8)
```

另一控制构造器 `sub_5F65A4`: `add esp,-0x10` (16 字节栈帧) → `+0x0C=dx`(Cmd 变量) → `+0x0E=0` → `mov ecx,0x10` → send。**恒 16 字节, BodyLen=0**。

> 控制/ACK 帧**无独立 14 字节格式**: 解析器最小需 16 字节才能读到 `+0x0E`。C# 旧代码里的 `ACK_FRAME_LEN=14` 是错误产物, 已改为 16。已观测控制 Cmd: `0x0B`, ACK `0x0C` (C# `GateServer`/`SendCompactAck`)。

### 2.3 数据帧 (16 字节头 + 内层子头 + 文本)

数据帧构造器 `sub_5F6FD0` (Cmd=0x12) —— 任务线索 "ecx/edx=0x1C + memcpy + payload@+0x1C" 的真身:

```
0x5F6FAD  bb 0c 00 00 00        mov ebx, 0x0C              ; body 基长 = 12 (内层子头)
0x5F6FBE  03 5d 10              add ebx, [ebp+0x10]        ; ebx = 12 + 文本长
0x5F6FC6  ba 1c 00 00 00        mov edx, 0x1C              ; 暂存区 = 28 = 16头+12内层子头
0x5F6FCB  e8 ..                 call memset(local,0,0x1C)
0x5F6FD0  c7 45 e0 77 bb aa 33  mov [ebp-0x20], 0x33AABB77 ; +0x00 Magic
0x5F6FD7  66 c7 45 ec 12 00     mov word [ebp-0x14], 0x12  ; +0x0C Cmd = 0x12 (=18)
0x5F6FDD  66 89 5d ee           mov word [ebp-0x12], bx    ; +0x0E BodyLen = 12+文本长
0x5F6FE1  66 89 7d f4           mov word [ebp-0x0C], di    ; body+0x04 (内层子头字段)
0x5F6FE9  66 89 45 f6           mov word [ebp-0x0A], ax    ; body+0x06 (内层子头字段)
                                ; 之后逐 socket 调 sub_5F6A68, 追加文本到 +0x1C
```

**关键**: `BodyLen(+0x0E) = 12 + 文本长 = 总长 − 0x10`, 即内层 12 字节子头**计入 body**。`+0x1C` 只是"16头+12子头"之后文本的落点, **不是** 28 字节传输头的边界。

同族数据帧构造器 (均 16 字节头, `0x1C` 暂存, `+0x0E=总长-0x10`):

| 地址 | Cmd(+0x0C) | 说明 |
|---|---|---|
| `0x5F6CD4` | 0x13 | 组播: body = [N×uint16 目标表] + 12字节子头 + 文本, `+0x0E=word[ebp-0x10]-0x10` |
| `0x5F6FD0` | 0x12 | 广播(逐用户), 内层子头 + 文本 |
| `0x5F7052` | 0x12 | 同上 (strlen 变体) |
| `0x5F70CF` | 0x18 | `+0x0C=0x18`, 0x1C 暂存 |
| `0x5F764A` | 0x12 | 内层子头取自 [edi] |
| `0x6D7C44` | 0x0E | ConnID=[ebx+0x464], SeqID=[ebx+0x468] |
| `0x6D7CFE` | 0x0E | 同上变体 |
| `0x6DCEB6` | 0x0E | 内层子头取自 [edi] |

### 2.4 解析 / 接收证据 (M2)

**单帧解析器 `sub_5F666A`**:
```
0x5F666A  81 38 77 bb aa 33     cmp [eax], 0x33AABB77
0x5F6679  66 81 78 0e 00 30     cmp word [eax+0x0E], 0x3000  ; BodyLen 上限 0x3000
0x5F669F  66 8b 70 0e           mov si, word [eax+0x0E]      ; si = BodyLen
0x5F66A9  83 c0 10              add eax, 0x10                 ; total = 偏移 + BodyLen + 0x10
0x5F66BE  66 8b 78 0c           mov di, word [eax+0x0C]       ; Cmd = word[+0x0C]  (switch 判别)
```

**中继解析器 `sub_63A66C`** (GameGate 面向侧, 帧流循环):
```
0x63A66C  81 3f 77 bb aa 33     cmp [edi], 0x33AABB77
0x63A674  8d 46 10              lea eax, [esi+0x10]           ; total = pos + 0x10 + ...
0x63A677  0f b7 57 0e           movzx edx, word [edi+0x0E]    ; + BodyLen
0x63A68E  e8 ..                 call sub_63B258               ; 处理单帧
0x63A697  83 c0 10              add eax, 0x10                 ; 步进 = 0x10 + BodyLen
0x63A6A8  83 f8 10              cmp eax, 0x10                 ; 循环下限 16 字节
```

**接收器 `sub_63B258`** (三重确认头长):
```
0x63B26A  b8 1c 00 00 00        mov eax, 0x1C                 ; 分配 28 字节节点
0x63B28E  a5 a5 a5 a5           movsd ×4                      ; 拷 16 字节头 → 节点[0..0x0F]
0x63B29F  66 89 46 14           mov [esi+0x14], ax            ; 节点+0x14 = BodyLen
0x63B2AC  89 46 10              mov [esi+0x10], eax            ; 节点+0x10 = body 指针
0x63B2B6  8d 47 10              lea eax, [edi+0x10]            ; body 源 = frame+0x10
0x63B2B9  e8 ..                 call memcpy(len=BodyLen)       ; body 拷自 +0x10
```

### 2.5 GameGate 底本反向佐证

GG `dump_gg2025` 内 5 处 `77 BB AA 33` **全部落在数据段** (协议/配置表, 与 `44 FF 44 FF` 客户端 magic 相邻), GG 不内联构造而作透明中继。表项紧邻 `10 00 00 00`(=0x10=16), 佐证头长 16:
```
0x560A41: 01 00 00 00 | 10 00 00 00 | 77 bb aa 33 | 00 00 00 00 ...
0x561F02: 10 00 00 00 | 77 bb aa 33 | 01 00 00 00 ...
```

---

## 3. 帧型总表 (一图流)

| 帧型 | 头长 | Magic | 判别 | 长度语义 | 证据 (M2 地址) | C# 建模 |
|---|---|---|---|---|---|---|
| **GameGate 控制/ACK** | **16** | +0x00 | Cmd@+0x0C (0x0B/0x0C…) | 恒 16, BodyLen@+0x0E=0 | 构造 `0x5F61C5`/`0x5F65A4` | `InternalPacket77.Ack` |
| **GameGate 数据** | **16** | +0x00 | Cmd@+0x0C (0x0E/0x12/0x13/0x18…) | 0x10+BodyLen; 内层12子头+文本属body | 构造 `0x637AC1`/`0x5F6FD0`; 解析 `0x5F666A`/`0x63A66C`; 接收 `0x63B258` | `InternalPacket77` + `LegacyGateType18` |
| DBServer(6000) | 12 | +0x00 | Type@+0x04 (word) | 0x0C+dword[+0x08] | 构造 `0x71318A`/`0x6BEF55`; 解析 `0x713467` | `LegacyDbServerFrameCodec` |
| 跨服线缆 | 20 | +0x00 | word@+0x04 | 0x14+dword[+0x10] | 构造 `0x69CC59`; 解析 `0x69CB62` | (未建模, 非本任务) |
| 内存命令结构标记 | — | +0x00 | kind@+0x04 | 非线缆 (含指针/大块 alloc) | `0x654A0C`/`0x688858`/`0x6B6603`/`0x79D40D`/`0x79E7B0` | (N/A, magic 作对象标记) |

---

## 4. M2 全部 26 处 magic 引用分类 (逐条)

> 地址为 mov/cmp 指令首字节 (scan 报告的立即数偏移相应 −2/−3)。

**GameGate 16 字节族 (14 处):**
- `0x5F61C5` 构造 · 控制帧 Cmd=0x0B · `ecx=0x10`
- `0x5F65AA` 构造 · 控制帧 Cmd=可变 · `ecx=0x10`
- `0x5F666A` **解析** · `total=pos+0x10+word[+0x0E]`, Cmd@+0x0C, BodyLen≤0x3000
- `0x5F6CD4` 构造 · 数据 Cmd=0x13 (组播)
- `0x5F6FD0` 构造 · 数据 Cmd=0x12
- `0x5F7052` 构造 · 数据 Cmd=0x12
- `0x5F70CF` 构造 · 数据 Cmd=0x18
- `0x5F764A` 构造 · 数据 Cmd=0x12
- `0x637AC1` 构造 · 主缓冲写入器 (advance 0x10, body@+0x10)
- `0x637B66` 构造 · 同函数 flush 分支
- `0x63A66C` **中继解析** · `total=pos+0x10+word[+0x0E]` → `sub_63B258`
- `0x6D7C44` 构造 · 数据 Cmd=0x0E
- `0x6D7CFE` 构造 · 数据 Cmd=0x0E
- `0x6DCEB6` 构造 · 数据 Cmd=0x0E

**DBServer 12 字节族 (5 处):** magic·Type@+0x04(word)·Reserved@+0x06·Len@+0x08(dword)·payload@+0x0C
- `0x6BEF55` 构造 · Type=2, Len=0x20
- `0x7130B3` 构造 · Type=2, Len=0x0C
- `0x71310F` 构造 · Type=2
- `0x71318A` 构造 · Type=1 (0x48 字节子结构, body@+0x54)
- `0x713467` **解析** · `total=pos+0x0C+dword[+0x08]`, 校验 word[+0x04]==2

**跨服 20 字节线缆族 (2 处):** magic·word@+0x04·word@+0x06·dword@+0x08·dword@+0x0C·Len@+0x10(dword)·payload@+0x14
- `0x69CC59` 构造 · `+0x10=esi=payload 长`, 帧 = esi+0x14
- `0x69CB62` **解析** · `total=0x14+dword[+0x10]`, 循环下限 0x14

**内存命令结构标记 (5 处, 非线缆帧, magic 作对象类型标记):**
- `0x654A0C` kind@+4=1, cmd@+0xC=0x161, `+0x08=指针`, alloc 0x4A28
- `0x68885A` kind@+4=1, cmd@+0xC=0x161, `+0x08=指针`, alloc 0x4A28
- `0x6B6603` kind@+4=1, cmd@+0xC=0x150, `+0x08=指针`, alloc 0xF0FC
- `0x79D40D` type@+4(byte)=1, len@+6(word)=0xBC
- `0x79E7B0` type@+4(byte)=6, len@+6(word)

## 5. GameGate 全部 5 处 magic 引用

均在**数据段** (协议/配置表), 非代码内联构造: `0x560A49`, `0x560C33`, `0x561F06`, `0x561FAB`, `0x5620D3`。表项与长度 `0x10` 及客户端 magic `0x44FF44FF` 并列 —— GG 为透明中继, 帧构造权威在 M2。

---

## 6. C# 修复清单 (本分支)

| 提交 | 文件 | 修复 |
|---|---|---|
| GATE-01 | `SystemModule/Packet/InternalPacket77.cs` | 已确认 16 字节头 (Cmd@+0x0C, BodyLen@+0x0E); 保留 (前序改动+本轮复核定稿) |
| GATE-01 | `SystemModule/Packet/InternalPacket77FrameParser.cs` | 通用分支帧长 `word[+0x0C]` → `0x10+word[+0x0E]` |
| GATE-01 | `SystemModule/Packet/GameGateServerFrameParser.cs` | 同上 (通用 InternalPacket77 分支) |
| GATE-02 | `ProtocolRegressionCheck/Program.cs` | 帧断言由 24/28 旧布局 → 16/20 真值 |

- `GateService.cs` 送帧路径经 `InternalPacket77.ToBytes()` 已产出正确 16 字节帧 (其 `-24` 是**内部队列 PacketHeader** 大小, 非线缆头; `Field16/Field20` 是被 `ToBytes` 忽略的死写)。未改动以降低风险。
- `LegacyGateType18` (16 头 + 12 客户端子头) 与本次 §2.3 逐字节一致, 无需改。
- 构建: `dotnet build SystemModule` / `GameGate-CS` / `ProtocolRegressionCheck` 均 **0 错误 0 警告**。

## 7. fail-closed 缺口 (证据不足, 未强行实现)

1. **跨服 20 字节帧 (`0x69CB62`/`0x69CC59`)** 的 word@+0x04/+0x06、dword@+0x08/+0x0C 字段语义与所属端口/链路名, 证据不足以命名, **未建 C# codec** (亦非 GameGate↔M2 范畴)。
2. **内存命令结构标记 (§4 末 5 处)** 含指针与大块 alloc, 判定为**本地对象类型标记**而非线缆帧; 未纳入任何帧 codec。若后续发现其被序列化上线, 需另行取证。
3. GameGate 数据帧 Cmd 全集 (已观测 0x0E/0x12/0x13/0x18 及控制 0x0B/0x0C) 未穷举 switch 全表; 传输头结构不受影响。
