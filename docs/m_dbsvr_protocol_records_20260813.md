# GameSvr ↔ DBServer — 协议与记录布局逐字节对账（第二轮）

日期：2026-08-13　工作树 `D:\loym2\.claude\wt2\m-dbsvr`（分支 `w/m-dbsvr`）
**未执行任何编译命令。**

本轮接续 `docs/m_dbsvr_impl_20260813.md`。上一轮把消息码空间与人物记录的**外层几何**做完了；
本轮补的是它没做的三块，并**推翻了它的两条结论**：

1. 两侧消息码全集的交叉对账（含出站方向，用**与形状无关**的扫描重做）；
2. 人物存档记录的**字段级**布局（101 个持久化字段，逐个给 VA）；
3. 物品记录的尾部零校验范围与逐字段归属，用 1363 条真实物品实测。

## 0. 证据源与基址

| # | 源 | 基址 |
|---|---|---|
| N1 | `staging/_dbsvr_reunpack_work/dbserver_CODE_live.bin` | **`VA = 0x401000 + 偏移`** |
| N2 | `staging/_reunpack_work/flat_image.bin`（M2Server） | **`VA = 0x400000 + 偏移`** |
| N3 | `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin` | **`VA = 0x10000000 + 偏移`** |
| N4 | `staging/golden_saves_gtwl/`（30 条真实 `mir3.user_data`，原版 Delphi DBServer 写的） | — |

**下文每个 VA 都注明属于哪个二进制。** 复现脚本 `staging/re/`（`a*` 记录布局，`b*` 消息码，
`c*` 补丁验证），本工作树内、已 gitignore。

基址无需重新论证：`staging/dbsvr_re/q01_verify.py` 的三条判据本轮全部复现——我独立重算的
四张跳表（§1.1）全部落在转储内且解出自洽 Delphi，`0x400000` 下则全部越界。

---

## 1. 消息码全集对账

### 1.1 入站（DBServer 认什么）—— 我自己从跳表字节重算，与上一轮逐项一致

四张表直接读 dword，不依赖上一轮的转录：

| 表 | VA（DBServer） | 判定指令 | 覆盖 | 实臂 |
|---|---|---|---|---|
| type-1 比较链 | `0x598A30` | `0x598A36 3D 68 01 00 00 cmp eax,0x168` 起 6 级 | `0x0045 0x0150..0x0159 0x0168` | 12 |
| type-1 表 A | `0x598ADA` | `0x598ACA 83 F8 0C cmp eax,0xC / ja` | `0x015B..0x0167` | 9 |
| type-1 表 B | `0x598B23` | `0x598B12 83 F8 34 cmp eax,0x34 / ja` | `0x016A..0x019E` | 19 |
| DBTool（role 9） | `0x598909` | `0x5988EE add eax,-0x100 / cmp eax,4` | `0x0100..0x0104` | 5 |
| type-2 表 a | `0x5998B1` | `0x59989E add eax,-0x3C / cmp eax,6` | `0x003C..0x0042` | 7 |
| type-2 表 b | `0x5998E2` | `0x5998CD add eax,-0x180 / cmp eax,0x11` | `0x0180..0x0191` | 6 |
| type-2 直比 | — | `0x599891 3D 77 01 00 00 cmp eax,0x177 / je 0x599AEE` | `0x0177` | 1 |
| type-3 | — | `0x599DEC 66 2D 88 01 sub ax,0x188 / 75 3C jne` | `0x0188` | 1 |

type-1 default 是 `0x599502`，type-2 default 是 **`0x599C7D`**（上一轮写 `0x599C7C`，差一字节，
不影响结论但转录有误）。

**四态结果（入站）**：

| 边界 | native 活命令 | C# 有常量 | MISSING | INVENTED |
|---|---|---|---|---|
| type-1（GameServer 角色） | 40 | **40** | 0 | 0 |
| type-1（DBTool 角色） | 5 | **5** | 0 | 0 |
| type-2 | 14 | **14** | 0 | 0 |
| type-3 | 1 | **1** | 0 | 0 |

C# 侧 `IsSilentNoOpCommand`（`NativeType2Protocol.cs:28-33`）声明的 12 个静默项与我从
`0x5998E2` 算出的「在表内但指向 default」12 项**完全一致**。`FAITHFUL`。

### 1.2 出站（DBServer 发什么）—— 【推翻上一轮】

上一轮的出站普查只找**函数体内**含 `mov dword ptr [reg], 0x33AABB77` 的构造器。
这漏掉了**在栈上拼帧**的那一类。四个被判「本构建从不发」的码，实际都有完整的帧序言：

```
; 全部为 DBServer VA
0x5CD3F2  c7 85 7c ff ff ff 77 bb aa 33   mov dword [ebp-0x84], 0x33AABB77
0x5CD3FC  66 c7 45 80 01 00               mov word  [ebp-0x80], 1        ; Type = 1
0x5CD402  c7 45 84 48 00 00 00            mov dword [ebp-0x7C], 0x48     ; DataLength
0x5CD409  66 c7 45 88 57 00               mov word  [ebp-0x78], 0x57     ; ← 0x0057

0x5CF784 / 0x5CF78B / 0x5CF791 / 0x5CF798  66 c7 45 98 78 00   ; ← 0x0078
0x5CEBF2 / 0x5CEBF9 / 0x5CEBFF / 0x5CEC06  66 c7 45 a8 79 00   ; ← 0x0079
0x5CEC88 / 0x5CEC8F / 0x5CEC95 / 0x5CEC9C  66 c7 45 a8 7a 00   ; ← 0x007A
```

四处的 `[ebp-x] +0 / +4 / +8 / +0xC` 与堆版的 `frame+0 / +4 / +8 / payload+0` 一一对应，
帧型都是 **1**，`DataLength` 都是 **`0x48`**。

> 上一轮「M2Server 认、本 DBServer 构建从不发（6 个）：`0x0057 0x005B 0x005C 0x0078 0x0079 0x007A`，
> 应从 MISSING 降级为原版接收侧遗留」——**六个里四个是错的**。真正不发的只有
> `0x005B` / `0x005C`（多编码扫描：word 立即数存储 0 处，指令立即数只命中
> `0x485945 mov [eax+0x14],0x5b` 一类与帧无关的点）。
>
> 这正是 REPLICATION_RULES §4.20 那条：**按形状正查会漏掉换了寻址形式的发送点。**

补扫 `66 C7 /r imm16`（word 立即数存储）后，DBServer 出站码增补 **`0x0069 0x006F 0x0072
0x0074 0x00C8 0x00CA`**，全部有 VA：

| 码 | 写入点（DBServer VA） | C# |
|---|---|---|
| `0x0069` | `0x5CBB41 66 c7 00 69 00` + `0x5C8FA2` | `NativeType2InitializationProtocol.SecondaryEndCommand` |
| `0x006F` | `0x599A39 66 c7 00 6f 00`（在 `0x003F` 臂 `0x599A36` 内） | `NativeType2Protocol.RelayResponseCommand` |
| `0x0074` | `0x5C6135 66 c7 45 f0 74 00` | `SecondaryBeginCommand` |
| `0x00C8` | `0x5A53F2 66 c7 00 c8 00` + `0x5C8EAA` | `PrimaryEndCommand` |
| `0x00CA` | `0x5CA346 66 c7 00 ca 00` | `NativeType2StdItemsImportService.NotificationCommand` |
| `0x0072` | `0x59D03E 66 c7 45 e8 72 00` → `call 0x59CE94` | **无** |

**⇒ 上一轮把 `0x0069 / 0x006F / 0x0074 / 0x00C8 / 0x00CA` 归入「C# 有、原生没有」的怀疑名单
是错的，五个全部 `FAITHFUL`。** 这与该轮自己总结的教训（认不认在接收侧、发不发在发送侧）同源，
只是它的发送侧扫描不完备。

### 1.3 被怀疑「C# 自己发明」的码，逐个翻案

| 码 | C# 位置 | 判定 | 决定性字节 |
|---|---|---|---|
| `0x0069` | `NativeType2InitializationProtocol` | `FAITHFUL` | DBServer `0x5CBB41` |
| `0x006F` | `NativeType2Protocol.RelayResponseCommand` | `FAITHFUL` | DBServer `0x599A39` |
| `0x0074` | `NativeType2InitializationProtocol` | `FAITHFUL` | DBServer `0x5C6135` |
| `0x00C8` | `NativeType2InitializationProtocol` | `FAITHFUL` | DBServer `0x5A53F2` |
| `0x00CA` | `NativeType2StdItemsImportService` | `FAITHFUL` | DBServer `0x5CA346` |
| `0x0057` | `NativeRenameCharProtocol.ForwardCommand` | `FAITHFUL` | DBServer `0x5CD409` |
| `0x0FB0` | `NativeRenameCharProtocol.Request/Response` | `FAITHFUL` | DBServer `0x5CD4A5` / `0x5CD63A` |
| `0x07DF` | `NativeGateReportProtocol.Type1LoginGateCommand` | `FAITHFUL` | DBServer `0x5CFE64 66 ba df 07 mov dx,0x7DF` |
| `0x07E0` | `NativeGateReportProtocol.Type2LoginGateCommand` | `FAITHFUL` | DBServer `0x5CFFA9 mov dx,0x7E0` |
| `0x1F42` | `NativeGlobalRelayProtocol.DirectSendCommand` | `FAITHFUL` | DBServer `0x5A3359 66 c7 45 8c 42 1f` |
| `0x274D` | `NativeGlobalRelayProtocol.RegistrationQueueCommand` | `FAITHFUL` | DBServer `0x5A3440` / `0x5A49CA mov dx,0x274D` |
| `0x2750` | `NativeGlobalRelayProtocol.QueryQueueCommand` | `FAITHFUL` | DBServer `0x5A3481` / `0x5A4A50 mov dx,0x2750` |
| `0x1191` | `YbDbGlobalShopItemProtocol.CompletionGateCommand` | `FAITHFUL` | **M2Server** `0x63A023 66 ba 91 11 mov dx,0x1191`（DBServer 侧 0 命中，是 GameSvr 发出的） |

后六个都是 `mov dx, imm16` —— **只按 `mov word ptr [mem], imm` 扫会全部漏掉**，
再次印证 §4.20。判 `INVENTED` 前必须多形式扫描。

### 1.4 真正的 MISSING：DBServer 会发、C# DBSvr 不会发（10 个）

搜索范围 `DBSvr/` + `SystemModule/` + `GameSvr/` 全部 `.cs`，十六进制与十进制两种写法，
限定在含 `Command` / `Cmd` / `case` 的行——十个码**零命中**：

| 码 | DBServer 构造器 / 写入点 | 帧型 | 后果 |
|---|---|---|---|
| `0x0046` | `0x598676 66 c7 00 46 00`（`sub_598618`） | 1 | C# DBSvr 换上后这条应答消失 |
| `0x0047` | `0x598680 66 c7 00 47 00`（同一函数的另一臂） | 1 | 同上 |
| `0x0058` | `0x598500 66 c7 00 58 00`（`sub_5984A8`） | 1 | 同上 |
| `0x0072` | `0x59D03E` → `call 0x59CE94` | 未追到出网跳 | 见 BLOCKED B3 |
| `0x0078` | `0x5CF798`，`DataLength=0x48` | 1 | 同上 |
| `0x0079` | `0x5CEC06`，`DataLength=0x48` | 1 | 同上 |
| `0x007A` | `0x5CEC9C`，`DataLength=0x48` | 1 | 同上 |
| `0x012D` | `0x59E22F 66 c7 00 2d 01`（`sub_59E1CC`） | 1 | 同上 |
| `0x0130` | `0x59E30B 66 c7 00 30 01`（`sub_59E298`） | **2** | 同上 |
| `0x013B` | `0x59E38D 66 c7 00 3b 01`（`sub_59E338`） | 1 | 同上 |

**这十个不影响「用原版 DBServer + C# GameSvr」这一侧**（GameSvr 是收方，收不到就当没有），
但会在**换成 C# DBSvr** 时让原版 GameSvr 少收十类应答，破坏 §1.4 的可回退性。
`0x013B` 尤其扎眼：紧邻的 `0x013C`（`HeroSaveNotificationCommand`）已经实现了，只差它。

### 1.5 帧格式（两侧一致，逐字节）

```
+0x00  dword  0x33AABB77                 幻数
+0x04  word   Type                       1 / 2 / 3
+0x06  word   （未写入，构造器只写 +4 的 word）
+0x08  dword  DataLength                 = 载荷字节数，不含 12 字节头
+0x0C  ...    载荷
```
证据（DBServer）：`0x59A2E4 c7 00 77 bb aa 33` / `0x59A2ED 66 c7 40 04 02 00` /
`0x59A2F9 89 42 08` / `0x59A305 8d 50 0c lea edx,[frame+0xC]`。
栈版同构，见 §1.2。

**type-1 载荷头 `0x48` 字节**，三个 ShortString 槽：

| 偏移 | 容量 | 用途 | 读证据（DBServer） |
|---|---|---|---|
| `+0x10` | 20（21 B） | 账号 | `0x599038 83 c2 10` + `call 0x404E5C` |
| `+0x25` | 15（16 B） | 角色名 | `0x599020 83 c2 25` |
| `+0x35` | 15（16 B） | 第三槽 | `0x599008 83 c2 35` |

字符串一律 **Delphi ShortString**：首字节长度，其后 GBK 字节，**容量固定、不含结尾 0**。
`0x404E5C` 是 `LStrFromPCharLen` 族，`0x4039E4` 是定长 ShortString 赋值（`cl` = 容量上限，
**截断而不报错**，且**不清空槽尾**）。

**type-3 载荷头 `0x40` 字节**，条目 `0x3C`——两侧独立互证：DBServer
`0x5AA4F5 83 c0 4c add eax,0x4C`（=0x0C 帧头 + 0x40 载荷头）对上 M2Server
`0x713EF7 83 7b 04 40 cmp dword[ebx+4],0x40`。

---

## 2. 人物存档记录（HumanData）字段级布局

### 2.1 基准点

```
LOAD  sub_6AFD7C (M2Server)  0x6AFDBC  8b 45 f8   mov eax,[ebp-8]      ; arg = blob 首址
                             0x6AFDBF  83 c0 08   add eax, 8
                             0x6AFDC2  89 45 d8   mov [ebp-0x28], eax  ; 记录体基址
SAVE  sub_6B0FF0 (M2Server)  0x6B1009  8b 45 fc   mov eax,[ebp-4]
                             0x6B100C  8d 70 08   lea esi,[eax+8]      ; 记录体基址
                             0x6B0FFF  8b d8      mov ebx, eax         ; 玩家对象
```

**`arg+8` ≡ C# 的 `raw[0]`**，即 `DataRecordSize = 0xEEF8`。DBServer 侧
`0x598810 rep movsd (ecx=0x3BC0)` 把 MySQL blob 原样搬进 `frame+0x54`，**无任何变换**，
所以落库格式 ≡ 线上格式 ≡ C# 的 `raw`。三方闭合。

golden 语料实测：30/30 条 zlib 解压后**恒为 61176 = `0xEEF8`**，头里的 size marker
恒为 61184 = `0xEF00`，blob 长度 30/30 是 256 的整数倍。C# 的 `TryUnwrap` 对
`marker == 0xEF00 而 raw 只有 0xEEF8` 的特判是**必需的**，不是历史包袱。

### 2.2 整体几何（全部由字节算出）

| 区 | 记录体偏移 | 大小 | 决定性字节（M2Server） |
|---|---|---|---|
| 标量区 | `0x0000..0x06CF` | — | §2.3 |
| 社交块 | `0x0650..0x06CF` | 128 | LOAD `0x6B096C lea esi,[eax+0x658]` / `0x6B0978 mov ecx,0x20` / `0x6B097D rep movsd`；SAVE `0x6B1688`/`0x6B1699` |
| 魔法 55×40 | `0x06D0..0x0F67` | 2200 | SAVE `0x6B16B1 lea edx,[edi+edi*4]` / `0x6B16B7 lea edx,[ecx+edx*8+0x6d8]` / `0x6B16C4 cmp edi,0x37` |
| 身上装备 16×208 | `0x0F68..0x1C67` | 3328 | SAVE `0x6B16CC add eax,0xf70`；`sub_75EEF0` `0x75EF1C add [ebp-8],0xd0` / `0x75EF24 cmp ebx,0x10` |
| **保留空洞** | `0x1C68..0x2BF5` | **3982** | 无任何指令触及；30/30 语料全零 |
| 背包 48×208 | `0x2BF6..0x52F5` | 9984 | SAVE `0x6B1701 imul edx,edi,0x1a` / `0x6B1708 lea edi,[ecx+edx*8+0x1c8e]` / `0x6B1712 mov ecx,0x34` / `0x6B171B cmp edi,0x30` |
| 仓库 192×208 | `0x52F6..0xEEF5` | 39936 | SAVE `0x6B1723 lea edx,[eax+0x438e]` / `0x6B1729 mov ecx,0xc0` |
| 尾隙 | `0xEEF6..0xEEF7` | 2 | — |

`0xF68 + 16×208 = 0x1C68`，而背包基址是 `0xF70 + 0x1C8E = 0x2BFE`（blob 坐标）
= 记录体 `0x2BF6`。**中间 3982 字节是真实存在的保留区**，`0x1C8E / 208` 不是整数，
所以它不是「装备槽位更多」，就是留白。C# 不建模、整块克隆携带——正确。

**C# 的 8 个尺寸常量与几何逐个相等**：`MagicBase 0x6D0`、`MagicCount 55`、
`MagicRecordSize 40`、`EquippedItemBase 0xF68`、`BagItemBase 0x2BF6`、`BagItemCount 48`、
`StorageBase 0x52F6`、`StorageItemCount 192`、`ItemRecordSize 208`。全部 `FAITHFUL`。

### 2.3 SAVE 持久化的全部标量字段（101 条，`sub_6B0FF0`，esi=记录体 ebx=对象）

只列 C# 已建模的与有争议的；完整 101 行见 `staging/re/a04_fields.txt`。

| 记录偏移 | 宽 | 对象偏移 | SAVE VA | C# | 判定 |
|---|---|---|---|---|---|
| `0x0000` | SS15 | `+0x106` | `0x6B1019` cl=**0x0F** | `sCharName` | 见 §4.5 |
| `0x0010` | SS15 | `+0x115` | `0x6B1029` cl=0x0F | `sCurMap` | `FAITHFUL` |
| `0x0020` | SS20 | `+0xAF4` | `0x6B1081` cl=**0x14** | `sAccount` | `FAITHFUL`（语料 30/30 长度=20） |
| `0x0036/0x0038` | 2/2 | `+0x12C/+0x130` | `0x6B1067/0x6B1072` | `wCurX/wCurY` | `FAITHFUL` |
| `0x003A` | 1 | `+0x154` | `0x6B108C` | `btDir` | `FAITHFUL` |
| `0x003C` | 2 | `+0x278` | `0x6B1096` | `Abil.Level` | `FAITHFUL` |
| `0x003E/0x003F/0x0040` | 1×3 | `+0x70/+0x71/+0x72` | `0x6B109D/0x6B10A3/0x6B10A9` | `btHair/btSex/btJob` | `FAITHFUL`（`0x3B` 无任何读写，旧版本门是错的，已删对了） |
| `0x0044` | 4 | `+0x15C` | `0x6B10B2` | `nGold` | `FAITHFUL` |
| `0x0048/0x004C/0x0050` | 4×3 | `+0x2AC/+0x2B4/+0x2BC` | `0x6B10BB/0x6B10C4/0x6B10CD` | `HP/MP/Exp` | `FAITHFUL`（原生无任何校验，C# 去掉非负守卫是对的） |
| `0x00B4` | SS15 | `+0x134` | `0x6B1144` cl=0x0F | `sHomeMap` | `FAITHFUL` |
| `0x00C4/0x00C6` | 2/2 | `+0x144/+0x148` | `0x6B1150/0x6B115E` | `wHomeX/wHomeY` | `FAITHFUL` |
| `0x00D8..0x00DC` | 1×5 | `+0xB8F/+0xB90/+0xB91/+0xB94/+0xB95` | `0x6B11E6..0x6B1216` | `AllowMarry/AllowMaster/Master/Married/Student` | `FAITHFUL` |
| `0x00DE` | 1 | **`+0xBA5`** | `0x6B123A` | `btAllowGroup` | `FAITHFUL`（`0x00D7 ↔ +0xBA4` 是天地合一，2026-08-07 那次搬到 `0xD7` 确实是错的，回退正确） |
| `0x00DF/0x00E0` | 1/1 | `+0xB96/+0xB97` | `0x6B1222/0x6B122E` | `btStudentOrder/btStudentCount` | `FAITHFUL` |
| `0x00EC` | 4 | `+0x4F0` | `0x6B1252` | `nShengWan` | `FAITHFUL` |
| `0x00F0/0x00F4` | 4/4 | `+0xBD8/+0xBDC` | `0x6B128E/0x6B129A` | `nLingFu/nUsedLingFu` | `FAITHFUL` |
| `0x00F8` | 4 | `sub_714334(+0x1824)` | `0x6B14FF` | 无（克隆携带） | SAVE-only，LOAD 0 引用 ✔ |
| **`0x0180..0x019F`** | 4×8 | `+0xD74..+0xD93` | `0x6B13C2..0x6B13D7` 循环 `3c 08 cmp al,8` | `ExchangeBookPersonalRareCounters[8]` | **`FAITHFUL`**（LOAD 对称循环 `0x6B05C9..0x6B05E4`） |
| `0x01CC` | 4 | `+0x70C` | `0x6B14EE` | `nNickLinFu` | `FAITHFUL`；**LOAD 0 引用**，确为 SAVE-only |
| `0x01D8/0x01D9` | 1/1 | `+0x181D/+0x182C` | `0x6B13F7/0x6B1403` | `btGoldActNextLevel/btFirstUsedGiftStage` | `FAITHFUL` |
| `0x01E0/0x01E4` | 4+4 | `+0x4D0/+0x4D4` | `0x6B141D/0x6B1429` | `NativeHeroIntimacy`（double） | `FAITHFUL`；LOAD **有**读（`0x6B0659/0x6B0665`），语料解出 0.0/-1.0/-2.0/-3.0 干净 double |
| `0x04C8..0x04DF` | 24 | `+0x1868` | `0x6B15A8` → `sub_78FD08` | `NativeHeroExperienceAccumulator` | `FAITHFUL`，长度 24 由 `0x78FD15 b0 04 mov al,4` 循环算出：`word[4]@+0` + `dword[4]@+8` |
| `0x04E0..0x04EF` | 16 | `+0x1880` | `0x6B15BA/0x6B15C0` + 4× `a5 movsd` | `ForceLv/ForceExp/FightPoints/sfLevel` | `FAITHFUL` |
| `0x04F0/0x04F1/0x04F2` | 1/1/2 | `+0x1892/+0x1893/+0x1890` | `0x6B15D1/0x6B15DD/0x6B15EA` | `btSecHeroPracticeRewardMode / CostTier / wLevel` | `FAITHFUL` |
| **`0x050E`** | 2 | `[+0x6D0]+8` | `0x6B112F` | `StorageSpaceCount` | **`DIVERGENT` → 已修**，见 §3.1 |
| `0x0534` | 2 | `+0x18A4` | `0x6B12CD` | `AccountScopedWordOffset` | 载入时被 DBServer 覆写（`0x598824`，DBServer VA），C# 仍无实现 → `MISSING`（不阻塞迁移） |
| `0x0608` | 4 | `+0xAE4` | `0x6B1667` | `nActivePoint` | `FAITHFUL` |
| `0x0650..0x06CF` | 128 | `+0xC48` | `0x6B1699 rep movsd ecx=0x20` | `NativeSocialBlob` | `FAITHFUL`（整块携带，不解析槽——语料 30/30 的 `0x670` 槽里有外来 `':'/'$'` 串溢出到 `0x680`，按 ShortString 解会 100% 崩） |

C# **未建模但被克隆携带**的原生字段（不构成互通风险，登记备查）：
`0x0100 0x0160 0x0164 0x0168 0x01B8 0x01BC 0x01C8 0x01DA 0x01E8 0x01EC 0x01F0 0x01F4
0x01F8 0x01F9 0x01FA 0x01FC 0x01FE 0x0230 0x0278..0x02A3 0x04C4 0x04C6 0x04F8 0x0508
0x050A 0x050C 0x0532 0x0537 0x057C 0x0580 0x058C 0x0598 0x05A0 0x05A4 0x05BC 0x05BE
0x05D2 0x05D4 0x05E8 0x060C 0x0610 0x0614 0x061C 0x0624 0x0648 0x00D0..0x00D7 0x00E1
0x00E4 0x00E8 0x016D 0x016E 0x0172 0x0174 0x0178 0x05AC(SS15)`。

其中 `0x0278..0x029F` 是 10 组 `(u16,u16)`、`0x02A0` 是条数：
SAVE `0x6B1610 lea eax,[esi+0x278] / mov edx,0x28 / call 0x403B2C`（先清 40 字节），
再 `0x6B1631` 循环 `mov dx,[ebx+edi*4+0x62c]` → `[esi+edi*4+0x274]`。语料 30/30 条数为 0。

### 2.4 ScriptData

节语法（C# 与原生逐字节一致）：`dword 0xABCDEFAA | word 长度 | byte 类型 | 载荷`，
外层 4 字节总长。解码器 `sub_6E448C`：`0x6E44EE cmp dword[eax],0xABCDEFAA`、
`0x6E4510 83 f8 08 cmp eax,8 / 0x6E4513 ja 0x6E4856`，9 项跳表 `0x6E4520`。

**type 0 = S 银行、type 1 = V 银行 —— 复核确认 C# 是对的**（历史上接反过）：

```
type0 臂 0x6E4544 : 0x6E457C 05 04 08 00 00      add eax, 0x804
                    0x6E459D 8b 90 04 08 00 00   mov edx,[eax+0x804]
type1 臂 0x6E45F7 : 0x6E462E 05 08 08 00 00      add eax, 0x808
                    0x6E464F 8b 90 08 08 00 00   mov edx,[eax+0x808]
GetS 0x6DF1CF 8b 93 04 08 00 00   SetS 0x6DF26D 8d 93 04 08 00 00   → +0x804 = S
GetV 0x6DF225 8b 93 08 08 00 00   SetV 0x6DF2CF 8d 93 08 08 00 00   → +0x808 = V
```
编码器同向：`0x6E4DDD c6 40 06 00`（type 0）配 `0x6E4DE7 mov eax,[eax+0x804]`；
`0x6E4E0F c6 40 06 01`（type 1）配 `0x6E4E19 mov eax,[eax+0x808]`。

**落盘数组必须按 key 全局升序 —— 确证，因为读侧是二分查找**：

```
读 sub_6E4270 : 0x6E4295 mov eax,ecx / sub eax,edx / sar eax,1 / add eax,edx   ; mid
                0x6E42A2 3b 3c c6      cmp edi,[esi+eax*8]
                0x6E42B3 7e 05         jle  → 收缩上界，否则收缩下界
写 sub_6E4140 : 0x6E4199 / 0x6E41AA  同样的 (lo+hi)/2，未命中时
                0x6E4220 call 0x403260 (Move) 把尾部整体后移一格再插入
```
C# `MergeKeyValues` 用 `SortedDictionary` 输出升序 → `FAITHFUL`。

**key ≥ 1001 的过滤也是对的**：key 由 `sub_6E42CC`（`imul eax,edx,0x3e8 / add eax,ecx`）
算出 = `group*1000 + index`，全镜像只有 4 个调用者（`0x6DF1C8 GetS`、`0x6DF1FE GetV`、
`0x6DF25F SetS`、`0x6DF2C1 SetV`），而四者都要求 `group > 0 且 index > 0`
（`0x6DF1BE test ecx,ecx / jle`、`0x6DF1C2 test edx,edx / jle`；SetS 同形
`0x6DF251 / 0x6DF255`）。group 0 走**内联表** `[obj + index*4 + 0x808]`
（`0x6DF20F` 读 / `0x6DF2A8` 写，`index` 限 1..100 由 `0x6DF209 dec edx / sub edx,0x64 / jae` 界定），
**不进 ScriptData**。⇒ 落盘 key 恒 ≥ 1001，C# 的 `if (pair.Key < 1001) continue;` `FAITHFUL`。

两处 `DIVERGENT` 见 §3.2 / §3.3。

---

## 3. 已修正的 C# 不一致

### 3.1 `StorageSpaceCount` 载入阈值反了（`DIVERGENT` → 已修）

原生（M2Server）：

```
构造 0x6AD8E5  89 b7 d0 06 00 00     mov [edi+0x6d0], esi      ; 仓库容器
     0x6AD8EB  c7 46 08 30 00 00 00  mov dword [esi+8], 0x30   ; 默认 48
载入 0x6B0CBC  66 8b 80 16 05 00 00  mov ax,[eax+0x516]        ; = 记录体 0x50E
     0x6B0CC3  66 83 f8 30           cmp ax, 0x30
     0x6B0CC7  76 0f                 jbe 0x6B0CD8              ; <= 48 → 保留默认 48
     0x6B0CD5  89 42 08              mov [container+8], eax    ; > 48 才采用，无上钳
```

C# 旧代码 `DBSvr/Core/NativeHumanDataCodec.cs:321`：
`storedSpaceCount < 24 ? 48 : Math.Min(storedSpaceCount, 192)`。

- 存值 **24..48** → 原生给 48，C# 给 24..48。**玩家可见：仓库从 48 格缩到最少 24 格。**
- 存值 **> 192** → 原生原样采用，C# 钳到 192。

已改为原生规则 `> 48 ? 值 : 48`，并去掉上钳（全部消费端
`UsrEngn.cs:3879`、`TPlayObject.Operate.cs:2243`、`PileItems.cs:246` 本来就各自
`Math.Clamp` 到 `MAX_STORAGE_ITEM_COUNT`，不会越界）。
语料回归：30/30 条存值均为 192 → 新旧同为 192，**无回归**。

### 3.2 空 V/S 节被写进 ScriptData（`DIVERGENT` → 已修）

原生编码器**只在长度非零时才写节**：

```
0x6E4DCC  85 f6 / 7e 2e   test esi,esi / jle 0x6E4DFE   ; 跳过 type 0
0x6E4DFE  85 ff / 7e 2e   test edi,edi / jle 0x6E4E30   ; 跳过 type 1
（type 2/6/7/8 同形：0x6E4E30 / 0x6E4E62 / 0x6E4E94 / 0x6E4EC6）
```

C# `TryBuildScript` 无条件补 type 0 与 type 1，空银行会写出 7 字节空节头。
而原生**解码器**把零长节当错误分支处理（`0x6E4553 66 83 78 04 00 cmp word[sec+4],0` /
`0x6E4558 76 58 jbe 0x6E45B2` → 格式化日志 `0x6E468E call 0x40DCC0` + `0x79DF74`），
所以原版 M2Server 读到 C# 写的记录会**每次登录为每个空银行刷一行假告警**。
已改为：合并后为空则不写、并移除已存在的空 type-0/1 节。

### 3.3 V/S 节长度不是 8 的倍数时 C# 拒绝整条记录（`DIVERGENT` → 已修）

原生只记一行日志然后**跳过该节继续载入**：
`0x6E4561 f7 7d ec idiv [ebp-0x14]`（=8）/ `0x6E4564 85 d2 / 75 4a jne 0x6E45B2`，
而 `0x6E45B2` 的尾巴是 `0x6E45F2 e9 bd 02 00 00 jmp 0x6E48B4` —— 回到节遍历。
C# `DecodeKeyValues` 返回 false 会让 `TryDecode` 整条失败，**角色直接登不上**。
已改为跳过畸形节。这与「发型门」「物品尾部零校验」是同一类事故：
**C# 在原生宽容的地方 fail-closed，代价是玩家进不去游戏。**

### 3.4 物品尾部零校验覆盖了 ys1..ys17（`DIVERGENT` → 已修）

`LegacyUserItem208Codec.UnownedSpanStart = 0x56`，守卫 `0x56..0xCF`（跳过 `0xB8`）。
但 `SystemModule/Packet/YanshenNativeItemLayout.cs` 把 **ys1..ys17 映射在 `0x58..0x6B`**，
而且 `NativeHumanDataCodec.DecodeItem` 正是通过 `YanshenNativeItemLayout.Unpack` 从这些
字节读回来的。**同一个仓库里两个文件对 `0x58..0x6B` 的归属互相矛盾。**

眼神转储（基址 `0x10000000`）逐条证实 ys 的归属：

```
0x10075D48  c7 45 ec 78 00 00 00   mov [ebp-0x14],0x78   ; ys5  → 记录 0x58
0x10075D3F  c7 45 ec 79 00 00 00   ; ys4 → 0x59
0x10075D36  c7 45 ec 7a 00 00 00   ; ys3 → 0x5A
0x10075D2A  c7 45 ec 7b 00 00 00   ; ys2 → 0x5B
0x10075CF9  89 70 7c               mov [eax+0x7c],esi    ; ys1  → 记录 0x5C（dword）
0x10075D51/5A/63/6C  0x80/0x81/0x82/0x83   ; ys6..ys9 → 0x60..0x63 …… ys17 → 0x6B
```
（in-memory item `+0x20` ≡ 记录 `0`，由 M2Server `0x74DB3A lea edi,[ebx+0x20]` /
`0x74DB3D mov ecx,0x34` / `0x74DB42 rep movsd` 确定。）

后果：任何**真的带眼神元素值**的物品，`LegacyUserItem208Codec.TryDecode/TryEncode`
与 `NativeStallItemRecordCodec` 都会以 `unmapped native item data at offset 0x58` 拒绝——
和当初那次 90.4% 是同一个失效模式，只是要等到有部署真用上这个功能才爆。
golden 语料看不见它（`0x56..0xCF` 在 1363 条里全零），属**潜伏**。

已在两处守卫里排除 `0x58..0x6B`（新增 `LegacyUserItem208Codec.HasKnownOwner`）。
守卫实际覆盖收敛为 `0x56..0x57` + `0x6C..0xB7` + `0xB9..0xCF`。

---

## 4. 物品记录（208 B）逐字段核对与尾部校验实测

`item+0x20 .. item+0xEF`，208 字节，四处 `rep movsd ecx=0x34` 互证：
LOAD 单件 `0x74DB3A/0x74DB3D/0x74DB42`、SAVE 背包 `0x6B170F/0x6B1712/0x6B1717`、
SAVE 仓库 `0x74A687/0x74A68A/0x74A68F`（`sub_74A648` 内）。

### 4.1 尾部零校验的确切范围（1363 条真实物品实测）

| 检查 | 拒绝数 / 1363 |
|---|---|
| 旧「`0x18..0xCF` 全零」（排除 `0x27`/`0xB8`） | **1232（90.4%）** —— 复现无误，首个违规字节全部是 `0x1C` |
| 当前 `0x56..0xCF`（排除 `0xB8`） | **0** |
| 本次修正后 `0x56..0x57` + `0x6C..0xB7` + `0xB9..0xCF` | **0** |

逐字节占用（1363 条中非零计数，完整表见 `staging/re/a10_corpus.txt`）：

```
0x00..0x09 核心      1358 1360 1363    0 1363 1065 1317 1101 1337 1109
0x0A..0x17 btValue      6    3    1   10    1    0    1    1    0   42  184   0   0   0
0x18..0x1B              0    0    0    0            ← 有主但恒零
0x1C..0x1F 日期      1232 1232 1221 1201
0x20..0x2F 地图名     301 …
0x30..0x37 来源名    1232 1232 1232 1232 1232  921  916  526
0x38..0x3B 空洞         0    0    0    0            ← 眼神自己跳过的洞
0x3C..0x42 来源名续    476  310  288  219  219  148  148
0x43       图码长    1232
0x44..0x51 角色名     301 …
0x53/0x54/0x55        301  931 1232
0x56..0xB7              全 0
0xB8       赠品         22
0xB9..0xCF              全 0
```

**⇒ `UnownedSpanStart = 0x56` 与「排除 `0xB8`」两项都正确**，唯一的缺陷是没排除
`0x58..0x6B`（§3.4）。`0x18..0x1B` 虽然也恒零，但它在眼神出处块 `0x18..0x55` 内，
不守它是对的。

### 4.2 核心字段

| 偏移 | 宽 | 字段 | 证据 |
|---|---|---|---|
| `0x00` | 4 | `MakeIndex` | `0x74DB06 83 3e 00 cmp dword[esi],0 / jbe` → **0 直接拒收** |
| `0x04` | 2 | `wIndex` | `0x74DB0F 0f b7 56 04` → `sub_74C248` 查 StdItem；调用侧 `0x6B0BED / 0x6B0D03 cmp word[eax+4],0 / jbe` → **0 表示空槽** |
| `0x06` | 2 | `Dura` | 核心区，随 208 字节整体搬运 |
| `0x08` | 2 | `DuraMax` | 同上 |
| `0x0A..0x17` | 14 | `btValue[14]` | 同上；**其中 `0x0A..0x0F` 同时被眼神当 jp2/jp1/jp6/jp5/jp4/jp3**（`0x10075C8D` 起 `0x2A..0x2F` 一串） |
| `0x27` | 1 | `UpgradeFlags` | `0x6CA0F3 or byte[esi+0x47],0x80`（不破碎）、`0x6CA10D … ,0x40`（必成功）、`0x6D7A93 mov al,[ebx+0x47] / and al,0x80`；清零 `0x6D7AE5`/`0x6D7B07`。**原生只 OR 高两位或整字节清零，从不重写低六位** —— 低六位在生产里是出处地图名第 4 个 GBK 字的尾字节，C# 的「不许改低六位」守卫是对的 |
| `0xB8` | 1 | `Bind`（实为赠品字节 `item+0xD8`） | 工厂清零 `0x7837EE`；置 1 `0x67D236 / 0x6C8611 / 0x709498 / 0x7094A4`；三条掉落路径读 `0x73CD44 / 0x740161 / 0x73FDD0`。**真正的绑定字是 `word[item+0x34]` = `btValue[10..11]`**（`sub_784710` / `sub_784718`）—— 现有映射保持不动是对的，改动会重新解释存量数据 |

---

## 5. 复核中发现的前人结论错误（本轮的主要增量）

| # | 出处 | 原结论 | 实际 | 决定性字节 |
|---|---|---|---|---|
| E1 | `docs/m_dbsvr_impl_20260813.md` §6.1 | 「`0x0057 0x005B 0x005C 0x0078 0x0079 0x007A` 本 DBServer 构建 0 个构造器」 | **四个是错的**，只有 `0x005B/0x005C` 成立 | DBServer `0x5CD409` / `0x5CF798` / `0x5CEC06` / `0x5CEC9C`，各自带完整 `0x33AABB77`+type1+`0x48` 序言 |
| E2 | 同上 §6.1 推论 | 「这 6 个应从 MISSING 降级为原版接收侧遗留，C# 不实现无互通风险」 | 其中 4 个是真 MISSING，换 C# DBSvr 会少发 | 同上 |
| E3 | 同上 §2.3 | type-2 default 标号 `0x599C7C` | 实为 **`0x599C7D`** | 跳表 dword `0x5998E2` |
| E4 | 上一轮 INVENTED 怀疑名单 | `0x0069/0x006F/0x0074/0x00C8/0x00CA` 未定 | 五个全部有 DBServer 写入点 | §1.2 表 |
| E5 | `LegacyUserItem208Codec` 类头 | 「守卫范围已收敛到没有归属的两段」 | `0x58..0x6B` **有归属**（ys1..ys17），仍在守卫内 | 眼神转储 `0x10075CF9` 等，§3.4 |
| E6 | `NativeHumanDataCodec` `StorageSpaceCount` | `< 24 ? 48 : min(v,192)` | 原生是 `> 48 ? v : 48`，无上钳 | M2Server `0x6AD8EB` + `0x6B0CC3/0x6B0CC7` |
| E7 | `staging/golden_saves_gtwl/MANIFEST.md` | 表里 `raw[0x3E]` 列全是随机值（0xC1/0x91/…），并称「hair==1 的 0/30」 | 那张表读的是**未解压**的 blob；`DECOMPRESSED.md` 里解压后 `[0x3E]` 是 `{1: 30}`。**同目录两份文档结论相反，MANIFEST 那份不可用** | 30/30 条 zlib 解压长度恒 `0xEEF8` |

E7 值得单独提一句：`MANIFEST.md` 与 `DECOMPRESSED.md` 就在同一个目录里，一份读压缩数据、
一份读解压数据，谁先看到哪份就会得到相反的结论。引用 golden 语料时**只能用 `DECOMPRESSED` 那套口径**。

---

## 6. 仍然 BLOCKED

| # | 项 | 缺什么 |
|---|---|---|
| B1 | `acct+0x1C` 的业务语义（决定记录 `0x534` 该填什么） | DBServer 两个写入点 `0x426436` / `0x5AD676` 未展开。**不猜。** C# 保持不实现 |
| B2 | 十个 MISSING 出站码（§1.4）的**载荷布局** | 只确认了码、帧型与 `DataLength=0x48`，各自的 `+0x10/+0x25/+0x35` 槽与体字段未逐个反 |
| B3 | `0x0072` 是否真的出网 | `0x59D03E` 填的是 12 字节缓冲，`call 0x59CE94` 之后的链路未追到 `0x33AABB77` 那一跳 |
| B4 | 记录 `0x1C68..0x2BF5`（3982 B 保留区）的用途 | 两侧编解码器都 0 引用，语料全零。整块携带即可，但不能断言「永远无用」 |
| B5 | 变长应答体（`0x0055 0x005D 0x0062 0x0132 0x0139 0x0130`）逐字段语义 | 沿用上一轮 N3，本轮未推进 |

---

## 7. 判定计数

| 判定 | 数 |
|---|---|
| `FAITHFUL` | 消息码 60（入站 40+5+14+1）+ 出站翻案 13 + 记录字段/几何 46 |
| `DIVERGENT`（本轮全部已修） | **4**：§3.1 仓库容量阈值、§3.2 空 V/S 节、§3.3 畸形节拒整条、§3.4 ys 段被守卫 |
| `MISSING` | **11**：十个出站码（§1.4）+ 记录 `0x534` 覆写 |
| `INVENTED` | **0** |
| `BLOCKED` | 5（§6） |

### 优先级建议

1. **§3.3、§3.1（已修）** —— 这两条会让玩家登不上/仓库缩水，是唯二有直接玩家可见后果的。
2. **§3.4（已修）** —— 潜伏型，一旦有部署启用眼神元素值就 100% 拒收，修它成本极低。
3. **§1.4 的十个 MISSING** —— 只在「换成 C# DBSvr」时才暴露，但那正是终局；
   建议按 `0x013B → 0x012D → 0x0130 → 0x0046/0x0047/0x0058 → 0x0078/0x0079/0x007A` 的顺序补，
   前三个的邻居码都已实现，改动面最小。
4. **§3.2（已修）** —— 只影响原版 M2Server 的日志噪音，无数据后果。
5. **B1** —— 在拿到 `acct+0x1C` 语义之前不要动 `0x534`。
