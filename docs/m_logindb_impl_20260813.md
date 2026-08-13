# LoginGate↔LoginSvr 与 GameSvr↔DBSvr 两条协议边界 — 逐字节还原与对账

日期：2026-08-13
工作树：`D:\loym2\.claude\wt2\m-logindb`（分支 `w/m-logindb`）
**未执行任何编译命令。**

## 0. 证据源与其可信度

| # | 证据源 | 覆盖的边界 | 可信度 |
|---|---|---|---|
| E1 | `D:/loym2/staging/_reunpack_work/flat_image.bin`（M2Server 平坦镜像，ImageBase `0x400000`，CODE `0x401000..0x7A10D0`） | GameSvr↔DBSvr 的 **GameSvr 侧全部**（发送构造 + 接收分发） | 最高（字节级） |
| E2 | **`D:/loym2/战神引擎LG源代码/LoginGate/Source/*.pas`**（战神引擎 LoginGate 的 Delphi 源码，2024-08-05） | LoginGate↔LoginSvr(DBServer) 的 **LoginGate 侧全部**，及 Client↔LoginGate | 最高（源码） |
| E3 | 生产部署 `D:/光头卧龙`（`DBService.ini` / `LoginGate.ini` / `M2Auth.ini`） | 拓扑、端口、方向 | 高 |
| E4 | C# 侧源码（工作树） | 对账对象 | — |

**无法获得的证据（决定了本报告的 BLOCKED 边界）**：

- `D:/光头卧龙/mud2.0/DBServer/DBServer.exe`（6,961,664 B，2019-05-20）**是 VMProtect 加壳**：节表
  `CODE/DATA/BSS/.idata/.tls/.rdata` 的 `SizeOfRawData` 全为 0，唯一载荷是
  `.vmp1`（`RAW=0x400`，`RSZ=0x6A2E00`，熵 **7.761**），入口点 `0xDD3D0A` 落在 `.vmp0/.vmp1` 区间。
- `D:/光头卧龙/mud2.0/GateServer/logingate/LoginGate.exe`（6,269,952 B）同样加壳：
  `.vmp1 RAW=0x400 RSZ=0x5FA000` 熵 **7.887**，入口 `0xA9D290`。
- 因此 **DBServer 侧的实现无法静态验证**。凡是"只有 DBServer 才知道"的语义（例如
  `TSelectGroupInfo` 里哪些字段由 DB 填、type-1 应答体的逐字段布局）一律标 BLOCKED。

> **E2 的版本落差必须登记**：源码树构建出的 `LoginGate.exe` 是 2024-08-05 的 7,331,826 B 未加壳版；
> 生产跑的是 2019-05-20 的加壳版。二者不是同一构建。源码里 `Build.inc` 还
> `{$I ..\..\Common\CommonBuild.inc}`，**该文件不在树内**，所以 `LoginCenterAuth` /
> `LoginCenterAuth_Spec` / `ResSocket` / `NEW_PROTECT` 这几个开关的取值是推断的
> （依据：生产存在 `M2Auth.dll` + `M2Auth.ini`，而 `CONFIGNAME_LOGINCENTERAUTH = 'M2Auth.ini'`
> 只在 `{$IFDEF LoginCenterAuth}` 分支里使用 ⇒ `LoginCenterAuth` 已定义。置信度高，但不是字节证据）。

---

## 1. 判定计数

| 边界 | FAITHFUL | DIVERGENT | MISSING | INVENTED | BLOCKED |
|---|---|---|---|---|---|
| GameSvr↔DBSvr（帧/命令/记录布局） | 12 | 2 | 3 | 5 | 6 |
| LoginGate↔LoginSvr | 9 | 7 | 4 | 2 | 4 |
| 合计 | 21 | 9 | 7 | 7 | 10 |

**「自洽但不兼容」抓到 4 处**，全部在 LoginGate↔LoginSvr 边界（§4）。
**「RM 内部标签当成线上 ident」在这两条边界上抓到 0 处**（§5），但发现了一个同族的反例
（`IM_SELECT_SERVER_REQ = 10000`，C# 正确地没有把它上线）。

---

## 2. GameSvr ↔ DBSvr（端口 6000）

### 2.1 帧格式（12 字节头）— `FAITHFUL`

**发送侧字节证据**（通用 type-1 构造器 `sub_71315C`）：

```
0071318A  C70077BBAA33   mov dword ptr [eax], 0x33AABB77     ; +0x00 Sign
00713190  66C740040100   mov word  ptr [eax+4], 1            ; +0x04 Type(u16)
00713196  8B5508         mov edx, dword ptr [ebp+8]
00713199  83C248         add edx, 0x48
0071319C  895008         mov dword ptr [eax+8], edx          ; +0x08 DataLength(i32) = 0x48 + bodyLen
007131A2  8D530C         lea edx, [ebx+0x0C]                 ; payload 起点 = +0x0C
```

`+0x06..0x07` 从未被写入（缓冲区来自 `AllocMem`/`FillChar 0`），也从未被读取 ⇒ **reserved**。

**接收侧字节证据**（`sub_713408` 的流解析循环）：

```
00713467  813F77BBAA33   cmp dword ptr [edi], 0x33AABB77
0071346D  7556           jne 0x7134C5                        ; 不匹配 -> 逐字节滑动
0071346F  8D460C         lea eax, [esi+0x0C]                 ; 头长 12
00713475  8B4708         mov eax, dword ptr [edi+8]          ; 长度取自 +0x08 的 dword
00713478  0345F8         add eax, dword ptr [ebp-8]
0071347B  3B4344         cmp eax, dword ptr [ebx+0x44]
0071347E  7F54           jg  0x7134D4                        ; 不完整 -> 等待
00713487  66837F0402     cmp word ptr [edi+4], 2             ; Type
007134BE  83C00C         add eax, 0x0C
007134CF  83F80C         cmp eax, 0x0C                       ; 剩余 >= 12 才继续
```

累积缓冲上限：`00713422 81FA00000200 cmp edx,0x20000`（超出即截断并记日志）。

**C#**：`SystemModule/Packet/LegacyDbServerFrameCodec.cs:11-37`
`FrameMagic=0x33AABB77 / Type u16@4 / Reserved u16@6 / len i32@8 / HeaderSize=12` — 逐字段一致。
判定 **`FAITHFUL`**。

> `DIVERGENT（边界，影响≈0）`：`LegacyDbServerFrameCodec.DefaultMaximumFrameLength = 0x1FFFF`
> 把整帧上限卡在 `0x1FFFF`；原生没有单帧上限，只有 `0x20000` 的累积缓冲上限，因此原生能解析
> 总长恰好 `0x20000` 的帧。差 1 字节。实际最大业务帧是存人物的 `0xF0FC`，够不到边界。

### 2.2 三种 type 与最小载荷 — `FAITHFUL`

分发在 `sub_713DDC`：

```
00713E68  668B4308  mov ax, word ptr [ebx+8]     ; 队列节点里的 Type
00713E6C  66FFC8    dec ax ; je 0x713E80         ; Type 1
00713E71  66FFC8    dec ax ; je 0x713EC0         ; Type 2
00713E76  66FFC8    dec ax ; je 0x713EF7         ; Type 3
00713E80  837B0448  cmp dword ptr [ebx+4], 0x48 ; jl skip   -> Type1 头 0x48
00713EC0  837B040C  cmp dword ptr [ebx+4], 0x0C ; jl skip   -> Type2 头 0x0C
00713EF7  837B0440  cmp dword ptr [ebx+4], 0x40 ; jl skip   -> Type3 头 0x40
```

**C#**：`GameSvr/Services/DBService.cs:650-656` 的 `1 => 0x48, 2 => 0x0C, 3 => 0x40` 完全一致。
**`FAITHFUL`**。

**Type 3 在 M2Server 侧是空操作**，这一点 C# 也对：

```
00713A98  55 8BEC         push ebp ; mov ebp,esp
00713A9B  837D0800        cmp dword ptr [ebp+8], 0
00713A9F  7C05            jl  0x713AA6
00713AA1  8BC2 668B00     mov eax,edx ; mov ax, word ptr [eax]   ; 读一个 word 后丢弃
00713AA6  5D C20400       pop ebp ; ret 4
```

`DBService.cs:673-676` 的注释与行为一致。**`FAITHFUL`**。

### 2.3 Type-1 请求/应答的 0x48 字节定长头 — 逐字段表 `FAITHFUL`

证据来自通用包装 `sub_6C53B8` 与直接构造点 `sub_6B6580`（存人物）：

| 偏移 | 宽度 | 语义 | 原生字节证据 |
|---|---|---|---|
| `0x00` | u16 | **Cmd** | `006C53C7 66894DB8 mov word[ebp-0x48],cx`；`006B6618 66C7460C5001 mov word[esi+0x0C],0x150` |
| `0x02` | u16 | 子码/结果码 | `006C53CB 668B450C / 006C53CF 668945BA`；应答侧 `00654517 0FB75002 movzx edx,word[eax+2]` |
| `0x04` | i32 | 参数1 | `006C53D3 8B4508 / 006C53D6 8945BC`；存人物处写 0（`006B6626 33C0 / 006B6628 894610`） |
| `0x08` | i32 | 参数2 | `006B662B 8B4508 / 006B662E 894614`；应答侧 `00654511 8B4804 mov ecx,[eax+4]`（相对 rec 起点为 +4，即本表 0x04）|
| `0x0C` | i32 | 参数3 | `006B6631 8B450C / 006B6634 894618` |
| `0x10` | ShortString[20]（21 B） | **sAccount** | `006B6637 8D461C lea eax,[esi+0x1C]` + `006B6640 B114 mov cl,0x14` → `sub_4039E4`；应答侧 `006544EB 83C210 add edx,0x10` |
| `0x25` | ShortString[15]（16 B） | **sCharName** | `006B6647 8D4631` + `006B6650 B10F mov cl,0x0F`；应答侧 `006544FD 83C225 add edx,0x25` |
| `0x35` | ShortString[15]（16 B） | 第三字符串槽 | `00654A72 8D4641 lea eax,[esi+0x41]` + `00654A75 B10F`；存人物处置空 `006B6657 C6464100` |
| `0x45..0x47` | — | 对齐填充 | `0x45 → 0x48` |

**C#**：`GameSvr/Services/NativeHumanDbCodec.cs:24/28/29`
`MessageSize = 0x48`、`AccountOffset = 0x10`（cap 20）、`CharacterOffset = 0x25`（cap 15）。**`FAITHFUL`**。

`MISSING（低）`：`0x35` 的第三个 ShortString 槽 C# 侧没有任何建模。原生在 hero-save（`0x654A72`）
里会填它。C# `NativeHeroDbFrameCodec` 是否覆盖到该槽未逐字节核对 → 见 BLOCKED B4。

### 2.4 人物存档记录布局（cmd `0x0150` / `0x0050`）— `FAITHFUL`（本次重点复核项）

存人物请求的整帧几何，逐条对上：

```
006B65E3  8D87FCF00000   lea eax, [edi+0xF0FC]      ; 总分配 = 0xF0FC + ScriptData 长度
006B65E9  E8B2C9D4FF     call 0x402FA0              ; AllocMem
006B65FE  E829D5D4FF     call 0x403B2C              ; FillChar 0
006B6603  C70677BBAA33   mov dword[esi], 0x33AABB77
006B6609  66C746040100   mov word[esi+4], 1                       ; Type 1
006B660F  8D87F0F00000   lea eax, [edi+0xF0F0]
006B6615  894608         mov [esi+8], eax                         ; DataLength = 0xF0F0 + extra
006B6618  66C7460C5001   mov word[esi+0x0C], 0x150                ; Cmd = 0x0150
006B665B  8D5654         lea edx, [esi+0x54]                      ; 记录写入点 = 帧 +0x54 = payload +0x48
006B6663  E888A9FFFF     call 0x6B0FF0                            ; SAVE
```

`sub_6B0FF0` 的锚点：

```
006B1009  8B45FC         mov eax, dword ptr [ebp-4]   ; arg2 = 帧+0x54
006B100C  8D7008         lea esi, [eax+8]             ; 记录数据区 = arg2 + 8
```

`sub_6AFD7C`（LOAD）同样锚定 `arg2+8`：`006AFDBF 83C008 add eax,8`。

于是 payload 几何：

| payload 偏移 | 长度 | 内容 |
|---|---|---|
| `0x0000` | `0x48` | type-1 定长头（§2.3） |
| `0x0048` | `0x08` | 记录前缀（原生**全零**：缓冲区 `FillChar 0` 后无人写入） |
| `0x0050` | `0xEEF8` | **人物记录本体** |
| `0xEF48` | `0x01A8` | 尾部会话/包装块 |
| `0xF0F0` | 可变 | ScriptData |

校验：`0x48 + 0x08 + 0xEEF8 + 0x1A8 = 0xF0F0` ✔ 与 `[esi+8] = 0xF0F0 + extra` 一致；
`0x0C + 0xF0F0 = 0xF0FC` ✔ 与 `lea ecx,[edi+0xF0FC]` 一致。

**C#**：`GameSvr/Services/NativeHumanDbCodec.cs:22-34`

```csharp
public const ushort LoadCommand = 0x0050;      // ✔ 原生应答表 case 0x050 -> 0x6542B5
public const ushort SaveCommand = 0x0150;      // ✔ 0x6B6618
public const int MessageSize          = 0x48;   // ✔
public const int HumanInfoPrefixSize  = 0x08;   // ✔
public const int HumanInfoSize        = 0xF0A8; // = 8 + 0xEEF8 + 0x1A8 ✔
public const int SessionSuffixSize    = 0x01A8; // ✔
public const int ScriptDataOffset     = 0x48 + 0xF0A8 = 0xF0F0; // ✔
```

`DBSvr/Core/NativeHumanDataCodec.cs:14` `DataRecordSize = 0xEEF8` ✔。

**逐字段记录布局**：`staging/human_record_field_map_20260807.md` 已把 `sub_6AFD7C`/`sub_6B0FF0`
的每一条 LOAD/SAVE 指令与 C# 偏移做过对照，结论是 **Class C（偏移/宽度错）= 0**。
本次抽样复核了 4 个决定性锚点，全部成立：

- `sCharName @0x0000` cap15：`006AFDD2` LOAD `cl=0x0E` / `006B1017` SAVE `cl=0x0F`
  （**LOAD cap=14、SAVE cap=15，原生本身不对称**；C# 统一用 15，与 SAVE 侧一致 —— 这是正确的选择，
  因为写盘侧决定持久化字节）。
- `sCurMap @0x0010` cap15：`006AFDE7` / `006B1029`（`006AFDE2 83C210 add edx,0x10`）。
- 记录数据区锚点 `arg2+8`：`006AFDBF` / `006B100C`（上引）。
- 记录尺寸 `0xEEF8`：由 `0xF0F0 - 0x48 - 8 - 0x1A8` 反算，并与 C# 常量一致。

判定 **`FAITHFUL`**。**记录布局本次没有发现偏差**——这是好消息，意味着换 DBSvr 不需要数据迁移。

> `DataSizeMarker = 0xEF00` 不是线上字段，是 MySQL blob 信封（`0xEEF8 + 8`）。
> 线上帧里那 8 字节前缀原生是全零，C# `HumanInfoPrefix` 原样搬运，语义等价。

### 2.5 Type-1 请求命令码全集（M2Server → DBServer）

发送路径是收敛的：全部 4 个构造器（`sub_713094` / `sub_7130E8` / `sub_71315C` / `sub_713554`）
最终都走 `sub_713CBC`，而 `sub_713CBC` 只有这 5 个调用点
（`0x7130DB`、`0x71314B`、`0x7131D3`、`0x713583`、`0x713591`），因此下表是**闭合的**。

| Cmd | 写入点（VA） | C# 常量 | 判定 |
|---|---|---|---|
| `0x0045` | `0x651DDB` | — | `NOT_FOUND`（C# 无对应） |
| `0x0150` | `0x6B6618` | `NativeHumanDbCodec.SaveCommand` | `FAITHFUL` |
| `0x0151` | `0x6C546F` | — | `NOT_FOUND` |
| `0x0152` | `0x6D223C`,`0x641A24`,`0x6BDED5`,`0x6C533B`,`0x6C604C` | `NativeMasterRelationFrameCodec.RequestCommand` | `FAITHFUL` |
| `0x0153` | `0x6C2403` | — | `NOT_FOUND` |
| `0x0154` | `0x6C269B` | — | `NOT_FOUND` |
| `0x0157` | `0x6C207C`,`0x6DD4D7` | — | `NOT_FOUND` |
| `0x0158` | `0x6D2659` | — | `NOT_FOUND` |
| `0x0159` | `0x6BD6CC` | — | `NOT_FOUND` |
| `0x015A` | `0x63BC50` | — | `NOT_FOUND` |
| `0x015B` | `0x63BE4E` | — | `NOT_FOUND` |
| `0x0160` | `0x6CC94A` | `NativeHeroDbFrameCodec.LoadCommand` | `FAITHFUL` |
| `0x0161` | `0x654A21`,`0x68886D` | `.SaveCommand` | `FAITHFUL` |
| `0x0162` | `0x6C9C82`,`0x6C9CCD` | `.CreateCommand` | `FAITHFUL` |
| `0x0164` | `0x62E14F`,`0x648464` | `.RenameCommand` | `FAITHFUL` |
| `0x0165` | `0x6DA376` | `.ConsignedListCommand` | `FAITHFUL` |
| `0x016B` | `0x6E55D7` | `NativeAccountStorageClient.LoadCommand` | `FAITHFUL` |
| `0x016C` | `0x6E5823` | `.SaveCommand` | `FAITHFUL` |
| `0x0172` | `0x656832` | — | `NOT_FOUND` |
| `0x0173` | `0x61A020`,`0x6E73A4` | — | `NOT_FOUND` |
| `0x0192` | `0x6F0155` | — | `NOT_FOUND` |
| `0x0193` | `0x6F028F` | — | `NOT_FOUND` |
| `0x0194` | `0x6CC9AE` | `NativeHeroDbFrameCodec.DetachCommand` | `FAITHFUL` |
| `0x019A` | `0x6B7C3D` | — | `NOT_FOUND` |
| `0x019B` | `0x656DF3`,`0x656F09`,`0x657009` | — | `NOT_FOUND` |
| `0x019E` | `0x6561A6` | — | `NOT_FOUND` |

共享包装 `sub_6C53B8` 有 13 个调用点，其中 4 个（`0x645721`、`0x6B23BF`、`0x6BF5F5`、`0x6BF5D2`）
的 `ecx` 静态回溯解不出 → **BLOCKED B1**。`0x0163` / `0x0166` / `0x0167` 可能藏在这 4 个里，
**因此不判 INVENTED**。

C# 里声明但本次未在 M2Server 找到发送点的请求码：`0x0163`、`0x0166`、`0x0167`（→ B1）、
`0x0170`（Zongpai，→ §2.7）、`0x0177`、`0x0180`、`0x0187`、`0x0188`（这四个应属
GameGate↔DBServer(5100) / DBTool 链路，不在 6000 上，→ B2）。

### 2.6 Type-1 应答命令码全集（DBServer → M2Server）

分发器 `sub_654140`：

```
0065417C  0FB700         movzx eax, word ptr [eax]      ; Cmd
0065417F  83F861         cmp eax, 0x61 ; jg 0x65420D ; je 0x6544D1
00654188+ 83C0BA         add eax, -0x46 ; cmp eax,0x1A ; ja 0x654656(default)
0065419A  FF2485A1416500 jmp dword ptr [eax*4 + 0x6541A1]
```

跳表 `0x6541A1`（27 项，0x46..0x60）与 `0x654281`（13 项，0x131..0x13D）逐条读出：

**认识（非 default）**：`0x46 0x47 0x50 0x51 0x52 0x53 0x54 0x55 0x56 0x57 0x58 0x5A 0x5B 0x5C
0x5D 0x5E 0x5F 0x60 0x61 0x62 0x63 0x70 0x78 0x79 0x7A 0x12D 0x12E 0x12F 0x131 0x132 0x138
0x139 0x13A 0x13B 0x13C 0x13D`（共 36 个）。

**表内但落 default `0x654656`**：`0x48..0x4F`、`0x59`、`0x133..0x137`。

比较链（`0x61` 以上）字节：
`0065420D 3D2F010000 cmp eax,0x12F` / `00654225 83E862 sub eax,0x62` /
`0065422E 48 dec eax`(0x63) / `00654235 83E80D sub eax,0x0D`(0x70) /
`0065423E 83E808 sub eax,8`(0x78) / `0065421F je`(0x79) /
`0065424C 83E87A sub eax,0x7A`(0x7A) / `00654255 2DB3000000`(0x12D) / `00654260 48`(0x12E)。

**对账**：

| C# 常量 | 值 | 原生 | 判定 |
|---|---|---|---|
| `NativeHumanDbCodec.LoadCommand` | `0x0050` | `-> 0x6542B5` | `FAITHFUL` |
| `NativeHeroDbFrameCodec.LoadResponseCommand` | `0x0051` | `-> 0x6542D7` | `FAITHFUL` |
| `NativeForceDisconnectClient.ResponseCommand` | `0x0052` | `-> 0x654386` | `FAITHFUL` |
| `NativeHeroDbFrameCodec.CreateResponseCommand` | `0x0053` | `-> 0x654395` | `FAITHFUL` |
| `.DeleteResponseCommand` | `0x0059` | **表项 = `0x654656`(default)** | `INVENTED`（常量存在但 C# 也不派发，实害 0，建议删常量或注明） |
| `.RenameResponseCommand` | `0x005A` | `-> 0x65447B` | `FAITHFUL` |
| `.ConsignedListResponseCommand` | `0x005D` | `-> 0x654466` | `FAITHFUL` |
| `.RestoreConsignedResponseCommand` | `0x005E` | `-> 0x65448A` | `FAITHFUL` |
| `.BuildThreeSlotResponseCommand` | `0x0070` | `-> 0x654499` | `FAITHFUL` |
| `NativeType1YbTransactionAck.BagInjectionResponseCommand` | `0x0060` | `-> 0x6544BD` | `FAITHFUL` |
| `.AwardPlayerResponseCommand` | `0x0061` | `-> 0x6544D1` | `FAITHFUL` |
| `NativeAccountStorageClient.LoadResponseCommand` | `0x0062` | `-> 0x6544E5` | `FAITHFUL` |
| `.SaveResponseCommand` | `0x0063` | `-> 0x654527` | `FAITHFUL` |
| `NativeZongpaiProtocol.ResponseCommand` | `0x0071` | **不在集合内，落 default** | 见 §2.7 |
| `NativeUserAdmissionControl.ResponseCommand` | `0x0132` | `-> 0x654407` | `FAITHFUL` |
| `NativeType2SessionExtProtocol.ResponseCommand` | `0x013A` | `-> 0x654609` | `FAITHFUL` |
| `NativeType1PersistenceCompletion.HeroSaveCommand` | `0x013C` | `-> 0x654646` | `FAITHFUL` |
| `.PlayStateCommand` | `0x013D` | `-> 0x65461B` | `FAITHFUL` |
| `NativeType3Protocol.QueryCharactersResponseCommand` | `0x00C9` | 不在集合内（且属 type3，M2Server 不处理 type3） | `INVENTED`（对 6000 链路而言）/ 见 B2 |

`MISSING`：原生认识但 C# 完全不处理的应答码 —— `0x46 0x47 0x54 0x55 0x56 0x57 0x58 0x5B 0x5C
0x5F 0x78 0x79 0x7A 0x12D 0x12E 0x12F 0x131 0x138 0x139 0x13B`（20 个）。
DBServer 发这些包时 C# GameSvr 静默丢弃。

### 2.7 `0x0170` / `0x0071`（宗派）— `BLOCKED`，倾向 `INVENTED`

- 请求 `0x0170`：不在 §2.5 的闭合发送集合里。
- 应答 `0x0071`：`0x71 > 0x61` 走比较链，`0x12F/0x79/0x62/0x63/0x70/0x78` 全不等 → `jmp 0x654656`（default）。
  M2Server **核心分发器不认识 0x0071**。

但 **宗派大概率是眼神插件功能**，而 `yanshen2.0.x.dll` 是 Themida 壳、静态不可分析
（`REPLICATION_RULES.md` §5.1 已定案不再啃壳）。插件加载进 M2Server 后能否自建 socket 或
挂钩分发器，我无法证伪。**故不落 `INVENTED`，标 `BLOCKED B3`**。

### 2.8 Type-2 命令码全集

原生有两个 type-2 处理器，**触发条件不同**（这一点必须区分，否则会漏掉一半命令）：

- **即时**处理器 `sub_7138D4`：仅当 `word[recv+0x5A] != 0`（一次性静态初始化回调在位）时，
  由 socket 回调直接调用（`00713485 66837B5A00 cmp word[ebx+0x5A],0` / `00713487 66837F0402 cmp word[edi+4],2`）。
- **延迟**处理器 `sub_712EC8`：正常运行期由队列 drain 调用（`00713EF0 E8D3EFFFFF call 0x712EC8`）。

**即时表**（`0071390C jmp [edx*4+0x713913]`，`00713900 83C29B add edx,-0x65` / `cmp edx,9`）：

| Cmd | 处理器 | 最小体长 | C# |
|---|---|---|---|
| `0x65` | `0x71393B` → `0x4C8324` | `cmp eax,0x3C`（60） | `NativeType2MagicSnapshotState.HumanMagicCommand` ✔ |
| `0x66` | `0x71396D` → `0x4C8208` | 60 | `.HeroMagicCommand` ✔ |
| `0x67` | `0x71399F` → `0x67CAD0` | `cmp eax,0x5C; jl`（92） | `NativeType2MonsterSnapshotState.Command` ✔ |
| `0x68` | `0x7139E4` → `0x7512B4` | `cmp eax,0x134; jl`（308） | `NativeType2StdItemSnapshotState.Command` ✔ |
| `0x69`,`0x6A`,`0x6B`,`0x6D` | **default `0x713A72`** | — | — |
| `0x6C` | `0x713A1D` → `0x60593C` | `cmp eax,0x13C; jne`（316） | `NativeType2FieldHeroSnapshotState.Command` ✔ |
| `0x6E` | `0x713A5D` → `0x5F734C` | — | `NativeType2EndpointSlotState.Command` ✔ |

**延迟链**（`00712EF6 0FB713 movzx edx,word[ebx]`）：

```
00712EF9  83FA74      cmp edx, 0x74 ; jg 0x712F14 ; je 0x712F76     -> 0x74
00712F00  83EA69      sub edx, 0x69 ; je 0x712F2A                   -> 0x69
00712F05  83EA06      sub edx, 6    ; je 0x712F3F                   -> 0x6F
00712F0A  83EA03      sub edx, 3    ; je 0x712F60                   -> 0x72
00712F14  81EACA000000 sub edx,0xCA ; je 0x712F82                   -> 0xCA
00712F1C  83EA66      sub edx, 0x66 ; je 0x71302B                   -> 0x130
```

| Cmd | 判定 |
|---|---|
| `0x69` | `NativeType2SecondaryRankingState.RecordCommand` **`FAITHFUL`** |
| `0x6F` | 原生 `0x712F3F → 0x657110`；C# 无 → **`MISSING`** |
| `0x72` | 原生 `0x712F60 → 0x6562E0`；C# 无 → **`MISSING`** |
| `0x74` | `.ClearCommand` **`FAITHFUL`**（`0x712F76 xor edx,edx; call 0x7135A0`） |
| `0xCA` | `NativeType2StdItemRuntimeAppend.Command` **`FAITHFUL`**（与 `0x68` 共用 `0x7512B4`，同样 `cmp esi,0x134`） |
| `0x130` | 原生 `0x71302B`（`cmp esi,0x105; jl`，→ `0x61A00C`）；C# 无 → **`MISSING`** |

**`INVENTED`（4 项，全镜像双分发器 0 命中）**：
`DBSvr/Core/NativeType2StaticRecordBuilder.cs:90/92/93/94` 的
`AntiqueItemsCommand = 0x0073`、`SuperForceCommand = 0x0075`、`SuperSkillCommand = 0x0076`、
`ForceMagicCommand = 0x006D`。

- `0x6D`：即时表索引 `0x6D-0x65 = 8` → 表项 `0x713A72` = **default**；延迟链不匹配。
- `0x73`：即时 `0x73-0x65 = 0x0E > 9` → `ja default`；延迟链 `0x73 < 0x74` 且三个 `sub` 都不为 0 → default。
- `0x75`/`0x76`：`> 0x74` → `sub 0xCA` / `sub 0x66` 都不为 0 → default。

C# **GameSvr 侧也没有任何消费者**（全 GameSvr 扫 `0x0073/0x0075/0x0076/0x006D` 0 命中）。
即：C# DBSvr 会往线上发 4 类原版 M2Server 直接丢弃、C# GameSvr 也不读的记录。
**按规矩应移除或屏蔽**（`NativeType2StaticRecordBuilder.cs:108/110/111/112/268/325/343/363`、
`NativeType2StaticLoader.cs:68/72/74/76/103`）。

### 2.9 Type-2 控制帧（M2Server → DBServer）

`sub_713094`（无体）与 `sub_7130E8`（带体）构造，载荷头 12 字节：

```
007130B3  C70077BBAA33     mov dword[eax], magic
007130B9  66C740040200     mov word[eax+4], 2                 ; Type 2
007130BF  C740080C000000   mov dword[eax+8], 0x0C             ; DataLength = 12
007130C6  6689780C         mov word[eax+0x0C], di             ; payload+0x00 = Cmd
007130CA  0FB74DFE         movzx ecx, word ptr [ebp-2]
007130CE  894810           mov [eax+0x10], ecx                ; payload+0x04 (i32)
007130D4  894814           mov [eax+0x14], ecx                ; payload+0x08 (i32)
```

即 type-2 载荷头 = `{u16 Cmd@0; u16 pad@2; i32 P1@4; i32 P2@8}`，体从 `+0x0C` 起
（与接收侧 `00712EF6 movzx edx,word[ebx]` / `00713EC9 83E80C sub eax,0x0C` 一致）。

**C#** `DBService.cs:764-780` `SendControlFrameDirect`：12 字节载荷、`u16 cmd@0`、`bytes 2..3 = 0`、
`i32 @4`、`i32 @8`，type=2。**`FAITHFUL`**（原生 `+0x02` 未初始化，C# 确定性写 0 —— 更安全且
DBServer 不读，可接受）。

心跳 `0x003C` / 注册 `0x003D` 的**编号**本次未能在镜像里定位到写入点（构造器的 `edx` 来自调用方，
`0x625D28`/`0x6279EB`/`0x627A2D`/`0x628AD7`/`0x628C6D`/`0x650EA0` 六个调用点的立即数未逐个展开）
→ **BLOCKED B5**。C# 注释声称来自"原版 GS1 动态观察"，是运行期证据，可信度高于我这次的静态覆盖。

另有一条 type-2 请求走 `sub_713554` 直发：

```
006BEF54  C745C477BBAA33   mov dword[ebp-0x3C], 0x33AABB77
006BEF5B  66C745C80200     mov word [ebp-0x38], 2         ; Type 2
006BEF5F  C745CC20000000   mov dword[ebp-0x34], 0x20      ; DataLength = 0x20
006BEF69  66C745D04100     mov word [ebp-0x30], 0x41      ; Cmd = 0x0041
```

**`0x0041`** 与 C# `NativeUserAdmissionControl.DenyIpCommand = 0x0041` 对上，但 C# 把它归到
type-1；原生这里是 **type 2**，载荷 0x20 字节、`ShortString[15] @payload+0x0C`（`006BEF87 8D45DC`
+ `006BEF8A B10F`）、`i32 @payload+0x1C`。**`DIVERGENT`（帧 type 与体布局）** —— 需要主控确认
C# 该命令的实际发送 type。

---

## 3. LoginGate ↔ LoginSvr（DBServer），端口 5600

### 3.1 拓扑（生产实证）

`LoginGate.ini`：`LoginGateListen=7000`（客户端）、`DBServerListen=5600`；
`uDBListen.pas:201` `Port := Confini.ReadInteger('Setup','DBServerListen',5600)` + `Open`
⇒ **LoginGate 是 5600 的监听方，DBServer 主动连入**。
`DBService.ini` `[LoginGate] IP=127.0.0.1 / Port=5600` 与之吻合。
`[DBServerIP] IPAddressN` 是白名单（`uDBListen.pas:119-125 CheckDBServerIPAddress`）。

C# `LoginGate/Core/NativeDbServerService.cs:70` 监听 `_config.DBServerListen`，
`IsAllowedBackend` 校验 `DbServerAddresses`。**`FAITHFUL`**。

### 3.2 16 字节帧头 `TServerMessage` — `FAITHFUL`

```pascal
// uTypes.pas:124-131
TServerMessage = record
  Sign: Cardinal;          // +0x00  $33AABB77
  rSocketHandle: integer;  // +0x04
  Ident: integer;          // +0x08
  Cmd: Word;               // +0x0C
  DataLength: Word;        // +0x0E
end;
```

收：`uDBListen.pas:350-355` `while FReciveBufferLen - iOffise >= Sizeof(TServerMessage)` /
`if Sign = SEGMENTATION_SIGN` / `PackageLen := Sizeof(TServerMessage) + DataLength`。

C# `SystemModule/Packet/YbDbLegacy77Codec.cs:56-61`：
`magic@0 / QueryId i32@4 / Param i32@8 / Ident u16@12 / Len u16@14`，`HeaderSize=16`。
字段名不同（`QueryId`↔`rSocketHandle`、`Param`↔`Ident`、`Ident`↔`Cmd`），**字节布局完全一致**。
**`FAITHFUL`**。

> 同一个魔数 `0x33AABB77` 在 6000 链路是 12 字节头、在 5600 链路是 16 字节头。
> 这不是 bug，是原版就这样（`uTypes.pas` 的 `TServerMessage` vs M2Server `sub_713408` 的解析）。
> C# 用两个独立 codec 区分，是对的。

超长保护：`uDBListen.pas:341` `if Count + FReciveBufferLen > MAX_RECEIVE_LENGTH` 且
`MAX_RECEIVE_LENGTH = MAX_IOCP_BUF_SIZE`。C# `MaximumFrameLength = 0x8000`。
`MAX_IOCP_BUF_SIZE` 的取值在 `IocpSocket.pas`，未核 → 小 BLOCKED，影响低。

### 3.3 命令码全集（`uTypes.pas:72-97`）

| 值 | 名 | 方向 | C# 实现 | 判定 |
|---|---|---|---|---|
| 1000 | `GDM_PING` | LG→DB | `NativeRegistrationAckIdent` | `FAITHFUL` |
| 1001 | `GDM_SELECT_SERVER` | LG→DB | `NativeProbeRequestIdent` | **`DIVERGENT`（语义被改写，见 §4.1）** |
| 1002 | `GDM_PIG_MESSAGE` | LG→DB | 无 | `MISSING` |
| 1003 | `GDM_SDK_AUTH_RESPONSE_OK` | LG→DB | `NativeAuthResponseIdent` | 部分（见 §3.6） |
| 1004 | `GDM_SDK_AUTH_RESPONSE_FAIL` | LG→DB | LG 侧有，**DB 侧不处理** | **`MISSING`（DBSvr）** |
| 1005 | `GDM_SDK_BUS_FIRST_REALNAME` | LG→DB | 无（`REAL_NAME_VERSION` 未定义） | 不适用 |
| 1006/1007 | `GDM_SMS_AUTH_RESPONSE_OK/FAIL` | LG→DB | 无 | `MISSING`（SDOBASE 未定义，实害低） |
| 1008/1009 | `GDM_QUERY_ACTIVATE_OK/FAIL` | LG→DB | 无 | `MISSING`（同上） |
| 2000 | `DGM_PING` | DB→LG | `NativeRegistrationIdent` | `DIVERGENT`（长度，见 §3.4） |
| 2001 | `DGM_SELECT_SERVER` | DB→LG | `NativeProbeResponseIdent` | **`DIVERGENT`（见 §4.1）** |
| 2002/2003 | `DGM_SDOA_OPEN/CLOSE` | DB→LG | `NativeType2Enabled/DisabledIdent` | `DIVERGENT`（见 §3.5） |
| 2011..2017 | `DGM_DirectStaticAuth`/`DirectDynAuth`/`DirectECardAuth`/`DirectSDOAAuth`/`CHECK_TRADE`/`SEND_SMSCODE`/`QUERY_ACTIVATE` | DB→LG | 无 | `MISSING`（本部署应不触发） |
| 2018 | `DGM_DirectLoginCenterAuth` | DB→LG | `NativeAuthRequestIdent` | `FAITHFUL`（体） |

**C# 侧 0 个 INVENTED 的 ident**（`0x07D2/0x07D3` 就是 2002/2003，只是命名成了 "Type2Control"，
与 SDOA 无关的命名会误导人，但值是对的）。

### 3.4 `DGM_PING` (2000) 载荷 — `DIVERGENT`

```pascal
// uTypes.pas:165-177
TPingMsg    = record GroupName: array[0..15] of Char; HumCounts: TGS_Human_Count; end;      // 40
TPingMsgNew = record GroupName: array[0..15]; HumCounts; QueueCount: Word; UnUse: array[0..5] of Integer end; // 68
```

```pascal
// uDBListen.pas:366-372
DGM_PING:
  if DataLength = sizeof(TPingMsg) then ProcPingMsg(..., 0)
  else if DataLength = sizeof(TPingMsgNew) then ProcPingMsg(..., PPingMsgNew(...).QueueCount);
```

C# `LoginGateWireProtocol.TryParseNativeRegistration` 经 `TryRequireNativeFrame(frame, 2000, 40)`
**只接受 40**；收到 68 会 `throw new InvalidDataException` → `ProcessFrameAsync` 抛出 →
`HandleConnectionAsync` 捕获后 **关闭连接**。

原版 DBServer 若使用新格式（带排队人数），C# LoginGate 会把它踢下线并反复重连。
判定 **`DIVERGENT`**。最小修复：接受 40 或 68，68 时额外读 `QueueCount = u16@40`。

`TGS_Human_Count` = `array[0..5] of Integer`，`[0]` 是锻造人数 —— C#
`NativeLoginGateProtocol.cs:25-29` 的注释与偏移（`HumanCountsOffset=16`，6 槽）**正确**。
`NativeLoginGateRegistration.OnlineCount` 求和槽 1..5 且跳过负值 —— 与
`uFormMain.pas` 的用法一致（未逐行核 `uFormMain.pas`，标注为高置信推断）。

### 3.5 `DGM_SDOA_OPEN/CLOSE` (2002/2003) — `DIVERGENT`

```pascal
// uDBListen.pas:373-374
DGM_SDOA_OPEN:  G_SDOA_Open := True;
DGM_SDOA_CLOSE: G_SDOA_Open := False;
```

两点差异：

1. **原生不检查 `DataLength`**；C# `TryParseNativeType2Control` 要求 `Payload.Length == 0`，
   否则抛异常 → 断连。
2. **原生写的是全局 `G_SDOA_Open`**（`uMainThread.pas:44`）；C# 存在**每连接**的
   `LoginGateBackendState.SetType2Enabled`。多 DBServer 时行为不同。

而且 C# **解析了这个开关却从不使用它**：`TryCreateSelectServerJumpFrame` 把
`payload[20]`（`TSelectServerMsg.BoSDOA`）硬写 0，而原生是
`BoSDOA := G_SDOAAuth_Enabled and G_SDOA_Open`（`uMainThread.pas:208`）。
`G_USE_SDOAAUTH` 在 `uServerInfo.pas:234` 被硬编码为 `False`，所以 **原生实际也恒为 False**
—— 结果等价，但 C# 是"碰巧对"，不是"照抄对"。登记。

### 3.6 认证族记录布局

`TSDKAuthHead`（Delphi 默认对齐，枚举 7 值占 1 字节）：

| 偏移 | 宽 | 字段 |
|---|---|---|
| 0 | 1 | `wAuthType`（`atLoginAuth=0` … `atLoginCenterAuth=6`） |
| 1 | 1 | `GateIdx` |
| 2 | 2 | `wSocketHandle` |
| 4 | 2 | `DynAuthIdent` |
| 6 | 2 | （对齐填充） |
| 8 | 4 | `nResult` |
| — | **12** | 合计 |

`TLoginCenterAuthInfo` = Head(12) + `szAuthID[52]@12` + `szPwd[32]@64` + `szClientIP[16]@96`
+ `szMacAddr[20]@112` + `areaId:Word@132` + `groupId:Word@134` = **136**。
C# `NativeAuthRequestPayloadSize = 136` 与全部字段偏移 **`FAITHFUL`**。

`TStatusAuthResult`（`{$IFDEF LoginCenterAuth}`）= Head(12) + `szPTID[21]@12` + `szGameID[21]@33`
+ `szDigitID[21]@54` + `UIDSet(2)@75` + 4 个 Byte@77..80 + `szPhone[21]@81` + `szSecPwd[21]@102`
= 123 → 记录对齐 4 → **124**。C# `NativeAuthResponseFullPayloadSize = 124` 且
`TryDecodeAuthResponse` 读 12/33/54/75/77/78/79/80/81/102 —— **偏移全部 `FAITHFUL`**。
`TECardAuthResult` = 20 ✔（`NativeAuthResponseMediumPayloadSize`）。

**失败帧**：`uSDKAuth.pas:1624`
`SendToDBServer(wHandle, GDM_SDK_AUTH_RESPONSE_FAIL, PChar(@Pres^.Head), sizeof(TSDKAuthHead))`
—— LoginCenter 路径 **恒为 12 字节、无文本尾**。
（带文本尾的只有 SDPT 路径 `uSDKAuth.pas:1127-1138`，`errMsgLen := StrLen+1`，
且 `if not (errMsgLen in [2..100]) then errMsgLen := 0` ⇒ 上限 `12+100 = 112`。
C# `NativeAuthFailureMaximumPayloadSize=112` / `MaximumTextBytes=99` **`FAITHFUL`**。）

**成功帧头**：`AuthResult.Head` 由 `PopAuthHead` 从请求原样取回（`uSDKAuth.pas:788 P^ := nNode^.CliHead`），
再 `AuthResult.Head.nResult := nResult`；`LC_AUTH_SUCCESS = 0`（`uSDKAuth.pas:38`），
`LoginCenterNotifyLoginResult` 只在 `nResult in [LC_AUTH_SUCCESS]` 时发 1003。
`P^.Head.wAuthType := atLoginCenterAuth`（=6）由 `DirectLoginCenterAuth` 在请求侧写入
（`uSDKAuth.pas:1476/1495`）。

对账 C# `LoginGateWireProtocol.WriteNativeAuthCommon` + 调用点：

| 字节 | 原生 | C# | 判定 |
|---|---|---|---|
| `[0] wAuthType` | 成功=6 | `TryCreateNativeAuthResponse124(6, …)` = 6 | `FAITHFUL` |
| `[0] wAuthType` | 失败也=6（Head 回显） | `TryCreateNativeAuthFailure(0, …)` = **0** | **`DIVERGENT`** |
| `[1] GateIdx` | 回显请求头 | **硬编码 1** | **`DIVERGENT`** |
| `[2..5]` | `wSocketHandle`+`DynAuthIdent` | 当作一个 i32 "QueryId" 原样回写 | 字节等价，`FAITHFUL`（命名误导） |
| `[6..7]` | 回显（填充） | 清 0 | 等价 |
| `[8..11] nResult` | 成功 0 / 失败 `-1/-3/-4/-5` | **恒 0** | 成功 `FAITHFUL`，**失败 `DIVERGENT`** |
| 失败帧长 | **12** | `12 + len("authentication failed") + 1 = 34` | **`DIVERGENT`** |

**玩家可见后果**：认证失败时 DBServer 收到 `nResult = 0`。按 `uSDKAuth.pas:1598` 的映射表，
0 对应 `'对不起，发生连接错误，请稍后登陆'`，而不是真实原因（账号不存在 / 密码错误 / 超时）。
如果 DBServer 还校验 `DataLength = sizeof(TSDKAuthHead)`，34 字节的失败帧会被**整包丢弃**，
客户端卡在登录中直到超时。

**最小修复**：给 `TryCreateNativeAuthFailure` 增加 `nResult` 参数并写入 `payload[8..11]`；
LoginCenter 失败路径改为 `status=6`、`nResult=-1`、**不带文本尾**（长度恒 12）；
`GateIdx` 改为回显 `request.RawPayload[1]`。

### 3.7 Client ↔ LoginGate（顺带核对，属相邻边界）

`TClientMessage`（`uTypes.pas:115-122`）= `Sign $FF44FF44@0 / userType:Byte@4 / Cmd:Byte@5 /
DataLength:Word@6 / DataIndex:Cardinal@8`，12 字节。
C# `LoginGateWireProtocol.TryDecodeClientFrame` 逐字段一致 **`FAITHFUL`**。

- `LM_DYN_ENCRYPT_CODE = 23 = 0x17` ✔ = C# `ClientDataCommand`；
  `LM_GET_ENCRYPT = 24 = 0x18` ✔ = C# `ClientConnectCommand`。
- `MAX_RECEIVE_LENGTH = 256`，`if PackageLen >= MAX_RECEIVE_LENGTH then` 丢弃
  ⇒ 入站 `DataLength` 上限 `256-12-1 = 243 = 0xF3`。C# `ClientInboundMaximumPayloadSize = 0xF3`
  **`FAITHFUL`**（这条掐得很准，值得表扬）。
- **`DIVERGENT`**：C# `TryParseConnectRequest` 要求 `Flag == 2`。原生 `userType` 只是
  `if userType = 2 then FIsMobile := True`（`uGateListen.pas:511`），**任何 userType 都接受**
  `LM_GET_ENCRYPT`。C# 会拒绝 PC 客户端（userType 0）。用户已声明部署是手游，实害为 0，登记。
- **`FAITHFUL`**：`SM_SERVER_LIST = 4001`，载荷 = `TDefaultMessage(12) + N × TClientGroupInfo(40)`
  （`uServerInfo.pas:288`），`Param := iGroupCount`。C# `ServerGroupInfoSize = 40`、
  `ServerListPayloadSize = 52`、`Param = count` 全对。
  `TClientGroupInfo.GroupDesc` 虽然是 `array[0..23]`，但原生只写 15 字节
  （`uServerInfo.pas:303 StrPLCopy(Pc.GroupDesc, arGroups[j].sDesc, 15)`），
  C# 注释里那条 "StrPLCopy(GroupDesc,sDesc,15)" 的断言 **经核对是对的**，
  写 16 字节槽 + 其余留零与原生逐字节相同。
- **`MISSING`**：`ServerListBuf` 发送前会被改写
  `Recog := ResGateAddr; Tag := ResGatePort`（`uGateListen.pas:290-294`）；C# 恒写 0。
  仅当 `{$IFDEF ResSocket}` 且资源网关在线才非 0，本部署大概率不启用 → 实害低。
- **`MISSING`**：客户端版本闸（`PMsg.Recog` → `IsAllowVersion`）、PK 警告
  （`SendPKWarning` / `CM_MERCHANT_QUERY(1110)` 回执 / `ID_PK_WARNING = 4030`）、
  `SM_HACKER_CHARACTOR = 499`、`SM_Collect_ServerInfo = 22968` —— C# `ClientSelectionService`
  一个都没有。原版客户端点"选服"后会先收到 PK 警告框；C# 直接跳服。
- **`DIVERGENT`（更严格）**：`TryReadExactGbkCString` 要求 NUL 必须是载荷最后一字节；
  原生 `FSelServerName := StrPas(@Buf[12])` 只读到第一个 NUL，允许尾部填充。

---

## 4. 专章：「两端都是 C#、自洽但与原版不互通」

### 4.1 【最高】`GDM_SELECT_SERVER (1001)` / `DGM_SELECT_SERVER (2001)` 被改造成"随机挑战握手"

**原版语义**（源码，不是推断）：

```pascal
// uGateListen.pas:203-217  客户端选服 -> 填 TSelectGroupInfo
FillChar(SelGroupInfo, sizeof(TSelectGroupInfo), 0);
wRes := G_MirServerInfo.SelectServer(FAreaIdx, FSelServerName, @SelGroupInfo, wHandle);
  //   uServerInfo.pas:145-147: P^.wAreaID := AreaIdx; P^.bGroupNo := bGroupIdx;
  //                            StrPLCopy(P^.szPostfix, sSuffix, 7);
iEnCodeIdx := FEnCodeIdx;  wSocketHandle := SocketHandle;
G_MainExecute.FromClientMsg(wHandle, IM_SELECT_SERVER_REQ, @SelGroupInfo, 28);

// uMainThread.pas:278-285   分配会话号后转发给 DBServer
inc(FSessionID); if FSessionID < 1000 then FSessionID := 1000;
PSelectGroupInfo(ImBuf)^.ciSessionID := FSessionID;      // 或 xor $A5A5A5A5（秒卡区）
G_DBListen.RequestSelectServer(ImHandle, PSelectGroupInfo(ImBuf));

// uDBListen.pas:231
DBServer.SendToDBServer(GDM_SELECT_SERVER, PChar(P), sizeof(TSelectGroupInfo));

// uMainThread.pas:186-212   DBServer 回 2001 -> 组 TSelectServerMsg 下发客户端
if ciSessionID = 0 then DMsg.Series := bErrorType          // 2:满员 3:维护中
else begin DMsg.Recog := ciSessionID; DMsg.Param := wGatePort;
           DMsg.Series := Word(ciGateIP shr 16); DMsg.Tag := Word(ciGateIP);
           AreaID := wAreaID; GroupID := bGroupNo; StrLCopy(szSuffix, szPostfix, 7); end;
```

**C# 干了什么**（`LoginGate/Core/NativeDbServerService.cs:287-303`）：

```csharp
private async Task SendProbeAsync(...)   // 在 DBServer 一注册完就发，与客户端无关
{
    var payload = new byte[28];
    RandomNumberGenerator.Fill(payload.AsSpan(0, 10));    // ciSessionID+iEnCodeIdx+wSocketHandle
    RandomNumberGenerator.Fill(payload.AsSpan(20, 8));    // szPostfix
    ...
    connection.SetProbe(payload);                          // 记下随机挑战
    TryCreateNativeProbeRequest(payload, out var probe, ...);   // ident 1001
}
// :249  收到 2001 时
if (!connection.MatchesProbe(route.RawPayload))
    throw new InvalidDataException("Native77 probe challenge mismatch");   // -> 断连
```

而 C# DBSvr（`DBSvr/Core/NativeLoginGateProtocol.cs:131-141`）配合地
**克隆请求载荷、只覆盖 10..19**，把 0..9 和 20..27 原样回显 —— 两端严丝合缝。

**为什么与原版不互通（两个方向都断）**：

1. **C# LoginGate + 原版 DBServer**：原版 DBServer 收到一个会话号是随机数、socket handle 是随机数的
   `GDM_SELECT_SERVER`。它要么忽略（C# 侧永远等不到 2001，路由永不 ready，
   `FindRoute` 恒 null → 所有玩家选服都抛 `"所选服务器组尚无可用 GameGate 路由"`），
   要么回一个 `ciSessionID = 0 + bErrorType` 的错误帧 → 前 10 字节对不上 →
   `MatchesProbe` 失败 → **抛异常关连接**，进入无限重连。
   而且 `szPostfix` 原版是**账号后缀配置**，绝不可能等于 8 个随机字节。
2. **原版 LoginGate + C# DBSvr**：原版只在玩家真的选服时才发 1001，且里面是真实
   `ciSessionID/iEnCodeIdx/wSocketHandle`。C# DBSvr 的 `TryCreateProbeResponse` 恰好会
   正确回填 port/IP/area/group 并回显其余 —— **这一侧居然是对的**。
   但 C# DBSvr 只在收到 1001 时回，而 C# LoginGate 永远不为真实选服发 1001。

3. **最要命的一条**：C# LoginGate 把选服**完全本地化**了
   （`ClientSelectionService.cs:229-236`：`FindRoute` + `TryCreateSelectServerJumpFrame`），
   **DBServer 从头到尾不知道有玩家在登录**，也就不会创建会话。
   原版 GameGate/GameSvr 是靠 DBServer 下发的会话号来放行的 —— 换回原版 DBServer 后
   玩家会被 GameGate 拒绝。

**修复方向**（不是一行改动，必须整体重排）：
删除随机挑战；`ClientSelectionService` 在收到 `CM_SELECT_SERVER` 时构造真实
`TSelectGroupInfo`（ciSessionID 自增≥1000、iEnCodeIdx、wSocketHandle、wAreaID、bGroupNo、
szPostfix=区后缀）→ 发 1001 → 等 2001 → 按 `uMainThread.pas:186-212` 组 `TSelectServerMsg` 下发。
**改动前必须与主控确认**，因为它会同时改变 C#↔C# 两端的行为。

### 4.2 【高】认证失败帧的 `nResult`/长度约定被两端"共同忽略"

C# LoginGate 发 34 字节、`nResult=0` 的 1004；C# DBSvr **根本不处理 1004**
（`LoginSocService.cs` 的 `ProcessNativeLoginGateFrame` 只认 1000/1001/1003，其余 `return`）。
所以两端"自洽"——失败就是超时。换任一端为原版都会暴露：原版 DBServer 会把 `nResult=0`
翻译成"连接错误"，原版 LoginGate 会发 12 字节带真实错误码的帧而 C# DBSvr 直接丢弃。
判定：LoginGate 侧 `DIVERGENT`，DBSvr 侧 `MISSING`。

### 4.3 【中】type-2 静态记录 `0x73/0x75/0x76/0x6D`：C# DBSvr 发、原版 GameSvr 丢、C# GameSvr 也不收

见 §2.8。这是"发明了原版没有的内部约定"的另一种形态 —— **连 C# 自己都没接上**，
属于半成品，但已经在往线上发字节了。

### 4.4 【中】`0x0041` 在原生是 type-2、C# 归到 type-1

见 §2.9 末尾。两端 C# 只要一致就跑得通，但原版 DBServer 按 type 分流，会把它送错处理器。

---

## 5. 专章：「RM 内部队列标签被当成线上 ident」

**在这两条边界上抓到 0 处。** 逐项排查依据：

- **GameSvr↔DBSvr**：这条链路没有 RM 队列参与。DB 命令码是直接写进帧载荷 `+0x00` 的
  （§2.3 的 `mov word[esi+0x0C], imm`），而这些帧都经 `sub_713CBC` 唯一出口发送，
  §2.5 的每一个值都取自真正的发送槽，不存在"内部标签误当线上码"的空间。
  反向的应答码也全部取自 `sub_654140` 的跳表，是接收端真实认识的值。
- **LoginGate↔LoginSvr**：`uTypes.pas:23` 有一个真正的内部标签
  `IM_SELECT_SERVER_REQ = 10000`，它只用于 `TMainExecute.FromClientMsg` 的**进程内队列**
  （`uMainThread.pas:274`），从不上线。C# **没有**任何地方使用 10000 —— 这一条 C# 是对的。
- 顺带核了 `WM_UPDATE_HUMAN_COUNT = $400+200` 等 6 个 `WM_*`：Windows 消息，C# 未误用。

**但发现一类相近的命名陷阱**（不影响互通，会误导后人）：
C# 把 `DGM_SDOA_OPEN/CLOSE (2002/2003)` 命名为 `NativeType2Enabled/DisabledIdent`，
把 `GDM_SELECT_SERVER (1001)` 命名为 `NativeProbeRequestIdent`，
把 `TSDKAuthHead.wSocketHandle+DynAuthIdent` 合称 `QueryId`。
值都对，名字全错，且 §4.1 的缺陷正是被这套命名掩盖的。

---

## 6. `RUNGATECODE` 定案

**结论：不是缺陷，可以结案。`0xAA9AAA9A` 是一个纯进程内的哨兵值，0 字节上线。**

证据链：

1. `SystemModule/Grobal2.cs:714` `RUNGATECODE = 0xAA55AA55 + 0x00450045 = 0xAA9AAA9A`。
   这是 LOMCN Mir2 `TMsgHeader.dwCode` 的写法，本项目从那套代码派生而来。
2. 全镜像字节扫描（本次复核）：`0xAA9AAA9A`、`0xAA55AA55`、`0x55AA55AA` 均 **0 命中**；
   顺带 `0xFF44FF44` 在 M2Server 也是 **0 命中**（合理：那是 Client↔LoginGate 的魔数，
   M2Server 不在那条链路上）。
3. 全 CODE 段枚举 `cmp dword ptr [mem], imm32`（`81 /7`）的大立即数，得到镜像里
   **全部**帧魔数比较点：
   - `0x33AABB77` × 4 —— `0x5F666A`、`0x63A66C`、`0x69CB62`、`0x713467`
     （分别是跨服 16 字节头、跨服 16 字节头、20 字节头、DBServer 12 字节头）
   - `0xABCDEFAA` × 4 —— `0x68B0B3`、`0x68B3DF`、`0x6E44EE`、`0x6E495C`
     （8 字节头：`dword magic@0 / u16 len@4 / u8 cmd@6`，`cmp eax,7`/`cmp eax,8` 的小跳表；
     与本次两条边界无关，**顺手登记**）
   —— **没有任何 `0xAA55AA55` 家族的比较**。
4. C# 里 `RUNGATECODE` 只出现在 `PacketHeader.PacketCode`（20 字节结构）。发送路径
   `GameSvr/GameGate/GateService.cs:298-333` 证明它**被剥掉**：

```csharp
var bodyLen = buffer.Length - 24;              // 跳过 [4B len][20B PacketHeader]
uint connId = BitConverter.ToUInt32(buffer, 8);   // 取 PacketHeader.Socket
ushort cmd  = BitConverter.ToUInt16(buffer, 14);  // 取 PacketHeader.Ident
Buffer.BlockCopy(buffer, 24, payload, 0, bodyLen);
var pkt = new InternalPacket77 { Magic = InternalPacket77.MAGIC, /* 0x33AABB77 */ ... };
```

   接收路径 `GateService.cs:144-152` 反过来：从已解析的 `InternalPacket77` **合成**一个
   `PacketHeader{PacketCode = RUNGATECODE}` 传给 `ExecGateBuffers`，**没有任何地方比较它**。

所以 `RUNGATECODE` 既不写上线也不从线上读，改成任何值都不影响互通。
**建议**：把它重命名为 `INPROC_GATE_SENTINEL` 或直接删除 `PacketCode` 字段，
避免下一个人误以为它是协议常量而"去修"。**登记为 `INVENTED（惰性，实害 0）`，不改。**

---

## 7. `DIVERGENT` / `MISSING` / `INVENTED` 完整清单

### DIVERGENT（9）

| # | 项 | 原生证据 | C# 位置 | 后果 |
|---|---|---|---|---|
| D1 | 1001/2001 选服协议被改成随机挑战握手 | `uGateListen.pas:203-217`、`uMainThread.pas:278-285`、`uDBListen.pas:231`、`uMainThread.pas:186-212` | `LoginGate/Core/NativeDbServerService.cs:287-303,249`；`ClientSelectionService.cs:229-236` | 与原版任一端都无法完成登录（§4.1） |
| D2 | 2000 只接受 40 字节 | `uDBListen.pas:366-372` 接受 40 或 68 | `LoginGate/Core/LoginGateWireProtocol.cs:43,286` | 新版 DBServer 被反复踢下线 |
| D3 | 2002/2003 强制空载荷 + 每连接作用域 | `uDBListen.pas:373-374`（不查长度，写全局 `G_SDOA_Open`） | `LoginGateWireProtocol.cs:447-462`；`NativeDbServerService.cs:270-278` | 带载荷即断连；多 DB 时语义不同 |
| D4 | 1004 失败帧 `wAuthType=0` | `uSDKAuth.pas:1476/1495`（恒 6） | `NativeDbServerService.cs:341` | DBServer 认错认证类型 |
| D5 | 1004 失败帧 `nResult` 恒 0 | `uSDKAuth.pas:38,565,1568`（失败为 -1/-3/-4/-5） | `LoginGateWireProtocol.cs:484-491` | 玩家看到错误的失败原因 |
| D6 | 1004 失败帧带 22 字节文本尾 | `uSDKAuth.pas:1624` 恒 `sizeof(TSDKAuthHead)`=12 | `NativeDbServerService.cs:341-344` | 若 DBServer 校验长度则整包丢弃 |
| D7 | `GateIdx` 硬编码 1 | `uSDKAuth.pas:788,969`（回显请求头） | `LoginGateWireProtocol.cs:487`（`version` 参数） | 多网关时回包路由错 |
| D8 | `0x0041` 帧 type 归属 | `0x6BEF5B 66C745C80200 mov word[ebp-0x38],2`（type 2，len 0x20） | `DBSvr/Core/NativeUserAdmissionControl.cs:134` 归 type-1 族 | 原版按 type 分流会送错处理器 |
| D9 | 客户端 CONNECT 强制 `userType==2` | `uGateListen.pas:507-513`（任何 userType 都受理） | `LoginGateWireProtocol.cs:126-142` | 拒绝 PC 客户端（本部署实害 0） |
| D10 | 单帧上限 `0x1FFFF` vs `0x20000` | `0x713422 81FA00000200` | `LegacyDbServerFrameCodec.cs:13` | 边界差 1，实害≈0 |

（D10 计入"边界类"，与 D1..D9 合计 10 条，表头计数按 9 条实质 + 1 条边界。）

### MISSING（7）

| # | 项 | 原生 | 后果 |
|---|---|---|---|
| M1 | DBSvr 不处理 1004 | `uSDKAuth.pas:1624` 会发 | 原版 LoginGate 的认证失败被吞，玩家卡登录 |
| M2 | type-2 `0x6F` | `0x712F3F → sub_657110` | DBServer 下发的这类记录被 C# 丢弃 |
| M3 | type-2 `0x72` | `0x712F60 → sub_6562E0` | 同上 |
| M4 | type-2 `0x130` | `0x71302B`（`cmp esi,0x105`）`→ sub_61A00C` | 同上 |
| M5 | type-1 应答 20 个码 | §2.6 列表 | 大量 DB 反馈静默丢弃 |
| M6 | 客户端版本闸 / PK 警告 / 4030 / 499 / 22968 | `uGateListen.pas:349-402,416-444`、`uServerInfo.pas:433-496,533-549` | 版本检查与 PK 确认框全无 |
| M7 | 服务器列表包的 `Recog=ResGateAddr` / `Tag=ResGatePort` | `uGateListen.pas:290-294` | 仅 `ResSocket` 启用时有影响 |

### INVENTED（7）

| # | 项 | 0 命中证据 | 建议 |
|---|---|---|---|
| I1 | type-2 `0x0073` `AntiqueItemsCommand` | 即时表 `0x73-0x65=0x0E > 9 → ja default`；延迟链三次 `sub` 皆不为 0 | 移除或屏蔽 |
| I2 | type-2 `0x0075` `SuperForceCommand` | `>0x74`，`sub 0xCA`/`sub 0x66` 皆不为 0 | 同上 |
| I3 | type-2 `0x0076` `SuperSkillCommand` | 同上 | 同上 |
| I4 | type-2 `0x006D` `ForceMagicCommand` | 即时表索引 8 = `0x713A72`（default） | 同上 |
| I5 | type-1 应答 `0x0059` `DeleteResponseCommand` | 跳表 `0x5A-0x46=0x13` 项 = `0x654656`(default) | 删常量或加注释 |
| I6 | type-3 `0x00C9` `QueryCharactersResponseCommand`（对 6000 链路） | `sub_713A98` 读一个 word 后丢弃，type-3 无分发 | 确认它是 5100 链路的码（见 B2） |
| I7 | `Grobal2.RUNGATECODE = 0xAA9AAA9A` | 全镜像 0 命中 + 发送路径证明被剥离 | 惰性，改名即可，**不要动值** |

---

## 8. BLOCKED 清单

| # | 项 | 缺什么证据 | 怎么补 |
|---|---|---|---|
| B1 | `sub_6C53B8` 的 4 个动态 `ecx` 调用点（`0x645721`、`0x6B23BF`、`0x6BF5D2`、`0x6BF5F5`） | 线性回溯解不出 `ecx`；`0x0163/0x0166/0x0167` 可能藏在这里 | 用 IDA 串行跟这 4 个函数的调用方，或运行期在 `sub_71315C` 下断点抓 `[edx+0]` |
| B2 | `0x0177/0x0180/0x0187/0x0188/0x00C9/0x0041` 究竟走哪条链路 | DBServer 侧加壳，GameGate↔DBServer(5100) 的协议无任何可读实现 | 生产抓包 5100 端口；或找 GameGate 的未加壳源码/构建 |
| B3 | 宗派 `0x0170/0x0071` | M2Server 核心分发器不认，但眼神插件 Themida 加壳、静态不可分析 | 生产抓包 6000 端口，看是否真有 `0x0170` 上线 |
| B4 | type-1 头 `+0x35` 第三 ShortString 槽的语义 | 原生只在 hero-save 路径填它（`0x654A72`），语义要看 DBServer 怎么用 | 抓包比对；或等 DBServer 脱壳 |
| B5 | type-2 心跳 `0x003C` / 注册 `0x003D` 的镜像出处 | 6 个 `sub_713094` 调用点的 `edx` 立即数未逐个展开 | 展开 `0x625D28/0x6279EB/0x627A2D/0x628AD7/0x628C6D/0x650EA0`（约 20 分钟） |
| B6 | type-1 各应答体的逐字段布局 | 只核了 `0x0050/0x0051`（后者含 `cmp dword[ebp+8],0x49D4` 的体长门），其余 34 个未展开 | 逐个展开 `sub_654140` 的 36 个处理器 |
| B7 | `MAX_IOCP_BUF_SIZE` 的取值 | `IocpSocket.pas` 未读 | 读 `D:/loym2/战神引擎LG源代码/Library/IocpSocket.pas` |
| B8 | LG 源码的 `CommonBuild.inc` 缺失 | `LoginCenterAuth` 等开关取值靠 `M2Auth.ini/M2Auth.dll` 的存在推断 | 找到 `..\..\Common\CommonBuild.inc` |
| B9 | `TSelectGroupInfo` 中哪些字段由 DB 填 / 哪些回显 | `uTypes.pas` 注释与 `uServerInfo.pas` 的写入互相矛盾（注释说 `wAreaID/bGroupNo` 是 DB→LG，代码里 LG 也写） | 抓 5600 端口一个真实选服往返 |
| B10 | 原版 DBServer / LoginGate 的实现 | 两者均 VMProtect（`.vmp1` 熵 7.761 / 7.887，CODE `RSZ=0`） | 运行期 dump（需要有效授权环境），或找未加壳构建 |

---

## 9. 建议的落地优先级

| 序 | 项 | 理由 |
|---|---|---|
| P0 | **不要动 §4.1**，先与主控/用户确认迁移策略 | 这是唯一会"改了更糟"的项：现在 C#↔C# 是通的，半改会两边都不通。且它与"可回退性"直接冲突——按现状，LoginGate 与 DBServer 必须同时换 |
| P1 | D2（2000 接受 68 字节） | 一处长度判断，严格放宽，不可能破坏现有 C#↔C# 组合 |
| P2 | D3（2002/2003 去掉长度检查） | 同上，严格放宽 |
| P3 | I1–I4（type-2 四个发明码） | 双向 0 消费者，删除零风险，止住无效字节 |
| P4 | D4+D5+D6+D7（1004 失败帧头） | 字节证据确凿（源码级），改动集中在一个方法；但要同时给 DBSvr 补 M1，否则只是换一种失败方式 |
| P5 | M1（DBSvr 处理 1004） | 与 P4 配套 |
| P6 | D8（`0x0041` 的 type 归属） | 需先做 B2/B5 确认 |
| P7 | M6（版本闸 / PK 警告） | 功能缺失而非协议冲突，量大，排后 |
| P8 | B1/B5/B6 的展开 | 提高覆盖度，不阻塞落地 |

---

## 10. 复核中发现的前人结论错误 / 需要更正的说法

1. **`REPLICATION_RULES.md` §1.4 表格里"GameSvr ↔ DBServer：type1 (`0x01xx`) / type2 (`0x00xx`) 命令族"这个概括不准确。**
   `type1`/`type2` 是帧头 `+0x04` 的**帧类型字段**（值 1/2/3），不是命令号的高位。
   type-1 的请求码确实多在 `0x01xx`，但也有 `0x0045`；type-1 的**应答**码在 `0x0046..0x013D`，
   横跨 `0x00xx` 和 `0x01xx`。而 type-2 有自己独立的命令空间（`0x65..0x74`、`0xCA`、`0x130`、`0x3C`、`0x3D`、`0x41`）。
   把 "0x01xx = type1、0x00xx = type2" 当判据会误判。

2. **`staging/dbcommand_wire_path_20260731.md` §2.1 说 "Type1 命令 0x0050 HumData"** —— `0x0050` 是
   **应答**码（`sub_654140` 跳表 `case 0x050 -> 0x6542B5`），请求码是 `0x0150`（`0x6B6618`）。
   C# 的 `NativeHumanDbCodec` 两个都定义对了，是那份文档的表述有歧义。

3. **`LoginGate/Core/LoginGateWireProtocol.cs:530-535` 的注释是对的，值得记一笔。**
   它声称 "uServerInfo.pas: StrPLCopy(GroupName,sName,15), StrPLCopy(GroupDesc,sDesc,15),
   StrPLCopy(szPostfix,sSuffix,7)" —— 逐条核对 `uServerInfo.pas:302,303,147`，
   **三条全部准确**。（本项目里注释准确的情况不多，这条可以直接采信。）

4. **`LoginGate/Core/LoginGateWireProtocol.cs:656-661` 关于 `TPingMsg` 的注释也准确**
   （`array[0..15] of Char` + `TGS_Human_Count` 六槽、`[0]` 是锻造数），
   但同一个文件的 `NativeRegistrationPayloadSize = 40` **漏了 `TPingMsgNew`(68)** 这一支。
   注释对、实现不全 —— 这种组合最容易蒙混过关。

5. **`LoginGate/Core/LoginGateWireProtocol.cs:703-707` 的注释把 2001 说成
   "DGM_SELECT_SERVER (2001) response. Maps one-for-one onto TSelectGroupInfo"** —— 结构映射是对的，
   但同一个文件把 1001 实现成了随机挑战。**注释描述的是原版语义，代码实现的是另一套**，
   两者矛盾却并存了下来。这正是 §4.1 长期没被发现的原因。

6. **`m2_wire_protocol_audit_20260813.md` §7 B6 对 RUNGATECODE 的处理（"仅登记、不视为缺陷"）是对的，
   但结论可以更强**：不只是"从这个镜像里无法验证"，而是**可以证明它根本不上线**（本报告 §6 第 4 点）。
   这一条现在可以从 BLOCKED 移到 CLOSED。

---

## 附录 A：本次中间产物

`D:/loym2/staging/logindb_re/`

| 文件 | 内容 |
|---|---|
| `flatten.py` | PE 展平 + 节表/熵报告（用于判定 DBServer/LoginGate 加壳） |
| `dbserver_flat.bin` / `logingate_flat.bin` | 展平结果（`.vmp1` 之外全空，仅作加壳证据） |
| `scan.py` | 镜像访问与 capstone 反汇编工具 |
| `s1_magic.py` / `s2_ctx.txt` | 26 个 `0x33AABB77` 站点及其上下文 |
| `s3_dbsvr.txt` | DBServer 链路收发函数完整反汇编 |
| `s4_callers.py` | 4 个帧构造器的全部调用点 |
| `s6_extract.py` | 从调用点回溯 type-1 命令立即数 |
| `s8_disp.txt` | `sub_654140` / `sub_712EC8` / `sub_713A98` 反汇编 |
| `s9_tables.py` | 三张跳表逐项解码 |
| `s10_magics.py` | 全 CODE 段大立即数 `cmp`/`mov` 枚举（RUNGATECODE 定案依据） |
| `s11_wrap.py` | `sub_6C53B8` 13 个调用点的 `ecx` 解析 |
