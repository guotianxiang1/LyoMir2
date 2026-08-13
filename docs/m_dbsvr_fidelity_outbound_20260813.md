# 存档往返保真度缺陷、十个 MISSING 出站码、BLOCKED 推进（第三轮）

日期：2026-08-13　工作树 `D:\loym2\.claude\wt2\m-dbsvr`（分支 `w/m-dbsvr`）
接续 `docs/m_dbsvr_protocol_records_20260813.md`（第二轮）与 `docs/m_dbsvr_impl_20260813.md`（第一轮）。

**编译只在两次持锁批次内跑，全程只有一个 `dotnet` 进程。**

## 0. 证据源与基址

| # | 源 | 基址 |
|---|---|---|
| N1 | `staging/_dbsvr_reunpack_work/dbserver_CODE_live.bin` | **`VA = 0x401000 + 偏移`** |
| N2 | `staging/_reunpack_work/flat_image.bin`（M2Server） | `VA = 0x400000 + 偏移` |
| N3 | `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin` | `VA = 0x10000000 + 偏移` |
| N4 | `staging/golden_saves_gtwl/` 30 条真实 `mir3.user_data` | 只用 `DECOMPRESSED.md` 口径 |

复现脚本 `staging/re2/`（本工作树内，已 gitignore）。**下文每个 VA 都注明属于哪个二进制。**

---

## 1. 任务一：往返保真度缺陷 —— 完整根因与修复

### 1.1 缺陷面貌比报告的大得多

`GoldenCodecFidelityCheck` 只打印每条记录的**首个**差异，用户看到的那行是 30 条里的一条：

```
user_data_idx9.bin: round-trip lost fidelity — 19 byte(s) differ, first at 0xFBC
```

实际是 **30 条里 28 条失败**，`idx1` / `idx5` 恰好是仅有的两条没有出处戳的角色。
我把 C# 的 `Unpack`/`Pack` 逐行改写成 Python 在全部 30 条上重放（`staging/re2/d02_sim.py`），
逐字节对齐，idx9 得到的正是 **19**，与审计工具报的数字一致 —— 这是模型可信的判据。

全语料差异只落在**两个**物品记录偏移上：

| 物品记录偏移 | 全语料差异字节数 | 性质 |
|---|---|---|
| `0x54` | **931** | 解码端整个字段没读，编码端凭猜重算 |
| `0x28..0x2A` | **3**（`idx23` BAG[4] 一条） | 读到 NUL 就停，写回却先清 12 字节 |

`0xFBC = EquippedItemBase 0xF68 + 槽 0 + 物品偏移 0x54`。

### 1.2 缺陷一：`item[0x54]`（`SourceKindOffset`）—— 解码丢了，编码猜错

`YanshenNativeItemLayout.Unpack` 用 `record[0x54]` 做分支判据，**但从不把它存到 DTO 上**；
`PackOrigin` 在 drop 分支里重算：

```csharp
var self = !string.IsNullOrEmpty(item.killerName)
           && string.Equals(item.killerName, item.pname, StringComparison.Ordinal);
destination[SourceKindOffset] = self ? SourceKindSelf : SourceKindMonster;
```

**这个判据对「自己获得」这一类恒为假。** 语料交叉表（1363 条真实物品，`staging/re2/d06_corr.py`）：

| `rec[0x54]` | `[0x55]` | 地图`0x20` | 来源名`0x30` | 角色名`0x44` | 条数 |
|---|---|---|---|---|---|
| 0 | 0x00 | – | – | – | 131（从未戳过） |
| 0 | 0xFF | 有 | 有 | 有 | **301**（怪物掉落） |
| 1 | 0xFF | 无 | 有 | 无 | **931**（自己获得） |

自己获得那 931 条**角色名槽是空的**（原生就这么设计），于是 `killerName == pname` 永远不成立，
931 条全部 `1 → 0`。再核对语义：`k=1` 的 `0x30` 槽在 891/931 条里**等于记录自己的角色名**
（其余 40 条是交易/邮件来的，戳的是原主人的名字）；`k=0` 的 `0x44` 槽等于记录自己的角色名。
所以 C# 把 0/1/2 叫 Monster/Self/Custom 在**语义上是对的**，错的是「用名字比较去反推」。

**这个字节不可重算，只能携带。** 眼神 `.text` 全量 capstone 扫描（1 字节访问、位移 0x74/0x75，
`staging/re2/d07_sweep74.py`，21 个命中里剔掉数据区误码后）只剩：

```
; 眼神 2.0.8 转储，基址 0x10000000
0x100586A4  88 50 75   mov byte [eax+0x75], dl     ; dl = 0xFF
0x100586A9  88 50 74   mov byte [eax+0x74], dl     ; dl = 2      ← 只写 2
0x1005A4EA  88 50 75   mov byte [eax+0x75], dl     ; dl = 0xFF
0x1005A4EF  88 50 74   mov byte [eax+0x74], dl     ; dl = 2      ← 只写 2
0x10082868  8a 50 75   mov dl, byte [eax+0x75]     ; 唯一的读，读的是 0x75
0x100828E1  88 50 75   mov byte [eax+0x75], dl     ; 掉落戳，dl = 0xFF
```

掉落戳 `0x10082868..0x100828EA` 写的是 `+0x40/+0x44/+0x48/+0x4C`（地图，16 字节盲拷）、
`+0x50/+0x54/+0x5C/+0x60`（来源名，来自 `[esi+0x106]`）、`+0x64/+0x68/+0x6C/+0x70`
（角色名，来自 `[ebx+0x106]`）、`+0x75 = 0xFF`、`+0x3C`（时间戳），**唯独不碰 `+0x74`**
—— 这正好解释了 301 条怪物掉落为什么全是 0：工厂的零值从没被覆盖。
**`+0x74` 在整个眼神 `.text` 里连一个读点都没有。**

> **`rec[0x54] == 1` 的写入点未找到，登记为新 BLOCKED（B6）。** 唯一写字面量 1 的
> `0x1006FD7F c6 40 74 01` 已排除：它的 `this` 是脚本隧道的解析器对象，不是物品 ——
> 该函数按 24 字节步长算参数个数、不足 2 个就返回 `0xFFFFFC88`（-888，§5.1.0.7 的
> 「字段不足」哨兵），唯一调用者 `0x100779D9` 落在集成函数派发器里。**不猜，只携带。**

顺带一条独立佐证：`GameSvr/Plugins/BigBag/YanshenBigBagRecord.cs` 建模的是**同一份 208 字节**，
它把 `SourceKind` 当原始字节读进来（:355）再原样写回去（:418）—— 同一个仓库里两套模型，
大背包那套是对的。

### 1.3 缺陷二：`item[0x28..0x2A]` —— 12 字节地图名是个「先清后写」

```csharp
// TryWriteGbkFixed
destination.Clear();          // 清掉 0x20..0x2B 全部 12 字节
bytes.CopyTo(destination);    // 只写回 ReadGbkZ 截到第一个 NUL 的前缀
```

`idx23` BAG[4]（记录偏移 `0x2F36`）的 `0x20..0x2F` 实际是：

```
C4 A7 C1 FA B9 C8 00 00 | C8 20 30 00 | 10 6F F7 04
魔    龙    谷    NUL     ← 之后是堆里的相邻内容，不是文本
```

`ReadGbkZ` 只拿到「魔龙谷」6 字节，写回时把 `0x28/0x29/0x2A` 的 `C8 20 30` 抹成 0。
那三个字节是**记录的真实内容**：原生 `0x1008287F..0x10082891` 从
`[[esi+0x128]+0x48]` **盲拷 16 字节**，源缓冲越过字符串结尾就带进了堆里的相邻字节
（`0x003020C8`、`0x046FF710` 一看就是指针），而 `rep movsd` 把它们一路带下去。

**顺带订正一条注释错误**：`MapTitleSize = 12` 旁边写着「leave 0x2C untouched
(no .text store of that dword)」。**有 store**：脚本戳 `0x1005863E` 和掉落戳 `0x10082891`
都写 `item+0x4C` = 记录 `0x2C`，而且 1363 条语料里 **257 条**的 `0x2C..0x2F` 非零。

### 1.4 修复：原生从不重算这个块，C# 也不许重算

两个缺陷是**同一个根因**：把「解析出来的字符串投影」当权威，反过来重写原始字节。
原生的做法是——出处块在获得物品的那一刻**戳一次**，此后 208 字节整体 `rep movsd` 搬运
（M2Server LOAD `0x74DB3A/0x74DB3D/0x74DB42`，SAVE `0x6B170F/0x6B1712/0x6B1717`），
**从来不从字符串反推**。

所以 `PackOrigin` 开头加一道 `OriginUnchanged` 闸：destination 解出来的六个字段
若与 DTO 当前值完全一致，就一个字节都不写（clone 本来就是对的）。
另外把 drop 分支里那行重算 `SourceKindOffset` 的赋值删掉 —— 原生掉落戳不写 `+0x74`，
所以新物品保持工厂的 0，正是原生行为。`hasDesc` 分支里的 `= SourceKindCustom` **保留**，
它逐字对应 `0x100586A9 mov [eax+0x74], 2`。

**验证（第二次持锁批次，`staging/re2/verify.log`）：**

```
GoldenCodecFidelityCheck PASS decoded=30 roundTripByteExact=30
    sex/job/level=user_index-30/30/30 names=GBK-stable hair@0x3E={1}
    0x3B=zero-in-all source=live-DBServer-written-records
GoldenSaveFrameCheck     PASS frames=30 inflated=61176
NativeHumanDbCodecCheck  PASS (6/6)
M2NativeDbGoldenFrameCheck PASS
```

修复前 934 个差异字节（931 + 3），修复后 **0**。提交 `fd40b10d`。

---

## 2. 任务二：十个 MISSING 出站码的载荷布局

### 2.0 先定案：`0x0072` 真出网，而且是 **Type 2**

第二轮停在「`0x59D03E` 填了个 12 字节缓冲，`call 0x59CE94` 之后没追到 `0x33AABB77`」。
追到了 —— `sub_59CE94` 就是帧构造器兼广播器（全部 DBServer VA）：

```
0x59CEF1  c7 00 77 bb aa 33         mov dword [eax], 0x33AABB77
0x59CEFA  66 c7 40 04 02 00         mov word  [eax+4], 2          ← Type 2，不是 1
0x59CF00  0f b7 45 10               movzx eax, word [ebp+0x10]    ; payloadLen
0x59CF04  83 c0 0c                  add eax, 0xC
0x59CF0A  89 42 08                  mov dword [edx+8], eax        ← DataLength = 0x0C + n
0x59CF13  8b 0a / 89 48 0c          frame+0x0C..+0x17 ← 那 12 字节头
0x59CF32  8d 50 18                  lea edx, [eax+0x18]           ; 尾巴接在 frame+0x18
0x59CF38  e8 93 62 e6 ff            call 0x4031D0                 ; Move
0x59CFBC  e8 03 f4 ff ff            call 0x59C3C4                 ; 逐连接下发
```

12 字节头由 `sub_59D020` 填：`0x59D039` `FillChar(buf,0xC,0)` → `0x59D03E` `word 0x0072`
→ `0x59D044` `word 0`，其余 8 字节保持 0。**B3 结案。**

### 2.1 两条对第二轮报告的订正

1. 第二轮把 `0x0078/0x0079/0x007A` 各列了**四个 VA**
   （`0x5CF784 / 0x5CF78B / 0x5CF791 / 0x5CF798`）。逐字节复核后，那是**同一个帧序言的四条
   连续指令**（幻数 / 帧型 / 长度 / ident），每个码只有**一个**站点；
   `66 c7 45 98 78 00`、`66 c7 45 a8 79 00`、`66 c7 45 a8 7a 00` 全镜像各命中 **1** 次。
2. 「帧型都是 1，`DataLength` 都是 `0x48`」只对八个定长码成立。
   **`0x0072` 与 `0x0130` 是 Type 2、变长。**

### 2.2 共用 type-1 载荷（0x48 字节，与 `NativeAuxiliaryType1Protocol` 同构）

```
body+0x00  word   Ident
body+0x02  word   次选择子
body+0x04  dword  标量
body+0x08  dword  } 只有 0x013B 用
body+0x0C  dword  }
body+0x10  ShortString[20]  账号    （赋值助手 0x4035D8，cl=0x14）
body+0x25  ShortString[15]  角色名  （cl=0x0F）
body+0x35  ShortString[15]  第三槽
```

**分配器是 AllocMem 不是 GetMem**：`0x40ADCC` → `0x40ADD8 call 0x402F48`（GetMem）
→ `0x40ADE8 call 0x4036E8`（FillChar，ecx=0）。所以构造器没写到的字节在线上是**硬零**，
不是堆垃圾。唯一例外是 `0x0079/0x007A`，它们的帧是 `sub_5CEBC0` 的栈局部 `[ebp-0x64]`
且没有 FillChar，未写区是**栈残留** —— 不可复现且无人消费，C# 一律补零。

### 2.3 十个码逐个

| 码 | 帧型 | 构造器（DBServer VA） | body 写入 | 下发范围 |
|---|---|---|---|---|
| `0x0046` | 1 | `sub_598618` @`0x598676` | `+0x04` = 零扩展的 byte 参数（`0x5986AD movzx` / `0x5986B3`）；`+0x25` SS15 角色名 | 请求方单播 `0x5986C1 call 0x59C3C4` |
| `0x0047` | 1 | 同函数 @`0x598680` | 同上 | 同上 |
| `0x0058` | 1 | `sub_5984A8` @`0x598500` | `+0x04` = ecx；`+0x10` SS20 账号 | 请求方单播 |
| `0x0072` | **2** | `sub_59D020`→`sub_59CE94` | 12 字节头 + n 字节尾，`DataLength = 0x0C+n` | 广播（角色≠9） |
| `0x0078` | 1 | `sub_5CF514` 臂 @`0x5CF798` | `+0x02` = `word[msg+4]`；`+0x04` = `dword[msg+8]`；`+0x10` 账号(`[self+0x24]`)；`+0x25` 角色名(`[self+0x44]`) | 单个 GameServer：`0x5CF810 call 0x59E450`，`dl = [self+0x19]` |
| `0x0079` | 1 | `sub_5CEBC0` @`0x5CEC06` | `+0x04` = `dword[src+0xC]`；`+0x02` = `word[src+0x10]`；账号/角色名同上 | 同上 |
| `0x007A` | 1 | 同函数 @`0x5CEC9C` | **只有** `+0x04`，没有 `+0x02` 的 store | 同上 |
| `0x012D` | 1 | `sub_59E1CC` @`0x59E22F` | `+0x02` = dx（**在 `0x59E228` 先写，晚于它才写 ident**）；`+0x10` 账号；`+0x25` 角色名 | 广播，`0x59E28C call 0x59E450` `dl=0` |
| `0x0130` | **2** | `sub_59E298` @`0x59E30B` | 12 字节头（`+2/+4/+8` 显式清零）+ 从**记录指针本身**起 `dword[记录]` 字节 | 广播 |
| `0x013B` | 1 | `sub_59E338` @`0x59E38D` | `+0x08` = edx、`+0x0C` = ecx，**无任何字符串槽** | 广播 |

选择子门：`0x0078` 的臂由 `0x5CF76A dec ax / 0x5CF76B sub ax,2 / 0x5CF76F jae` 守，
即只在 `word[msg+4] ∈ {1,2}` 时发；`0x0079/0x007A` 由 `0x5CEBDF dec ax / je` 与
`0x5CEBE4 dec ax / je` 分别对应选择子 1 与 2。

`sub_59E450(eax=mgr, dl=过滤, ecx=buf, [esp+4]=len)`：`dl == 0` 时遍历 `[mgr+0x50]` 全表、
跳过 `[conn+0x40A0] == 9`（DBTool）后逐个 `0x59C3C4`；`dl != 0` 时只发给角色字节相等的那一个。

### 2.4 实现范围与诚实说明

新增 `DBSvr/Core/NativeOutboundNotificationProtocol.cs`（十个帧构造器）与
`AuditTools/NativeOutboundNotificationLayoutCheck`（钉住全部字节，含「未写区必须为零」断言）。
审计 **PASS**。

**但这十个码在运行期仍然是 `MISSING`（§「死代码算 MISSING」）。** 触发条件没接：
它们全都**不是应答**，从 type-1/type-2 请求派发器一个都到不了；每个生产者都挂在
`sub_5D25EC` 那条内部事件队列上（在 `[self+0x4C]` 临界区里从 `[self+0x44]` 出队，
`0x5D269C movzx eax, word [node+8]` 取选择子），而 C# DBSvr 还没有这条队列。
本轮交付的是**线序**，不是**时机**。

---

## 3. 任务三：BLOCKED 推进

### 3.1 B1 `acct+0x1C` —— **解决**。它就是记录 `0x534` 自己的写穿缓存

| | |
|---|---|
| 唯一写入点 | `sub_5AD648` @`0x5AD676` `66 89 50 1c` |
| 唯一调用者 | `0x59C0FA`，在 **SAVE 处理器 `sub_59C060`**（type-1 `0x0150`，派发臂 `0x598BF7`）内 |
| 喂给它的值 | `0x59C0F3 66 8b 89 34 05 00 00  mov cx, word [record+0x534]` |
| 读回 | `sub_5AD608` @`0x5AD62E` `mov ax,[eax+0x1c]`，查不到账号则返回 **0**（`0x5AD638`） |

即：**存档时 `cache[账号].w1C ← record[0x534]`，载入时 `record[0x534] ← cache[账号].w1C`。**
记录才是权威，缓存是派生物。所以 C# 现在「整块携带、不碰 `0x534`」在单 DBServer 下**完全等价**。

**订正第二轮**：它说写入点有两个，`0x426436` 和 `0x5AD676`。**`0x426436` 不是。**
它在 Delphi RTL 助手 `sub_4263C0` 里，遍历动态数组（`mov eax,[eax-4]` / `mov eax,[eax+edx*4]`）
并对一个标志字做掩码（`0x426424 not edx` / `0x426426 and dx,[eax+0x1c]`）——
另一种结构碰巧也有 `+0x1C`，与账号记录无关。

残留一点（不阻塞）：唯一与纯携带有差的情形是「载入时账号不在缓存里」，原生会写 0。
语料里 `record[0x534]` **30/30 全为 0**，所以不可观测；但缓存是否在启动时预载没有证据。

### 3.2 B4 记录 `0x1C68..0x2BF5` 那 3982 字节 —— **实质结案：两侧编解码器零触及**

把 M2Server LOAD `sub_6AFD7C`（`0x6AFD7C..0x6B0D40`）和 SAVE `sub_6B0FF0`
（`0x6B0FF0..0x6B1760`）**整函数**用 capstone 多相位解码，收集**全部**内存位移
（`staging/re2/f03_b4.py`）：

- LOAD 245 个、SAVE 193 个位移 > 0x100。
- 落在空洞区间的只有 **`0x1C8E` 一个**，出现在 `0x6B0CF6` 与 `0x6B1708`，
  而那正是背包基址表达式 `lea edi,[ecx+edx*8+0x1c8e]` —— **`ecx` 不是记录基址**，
  所以它不是对空洞的访问，是背包的步长常数。
- `0x0F00..0x3000` 里实际用到的其余位移，要么是装备区（`0xF40/0xF48/0xF70`），
  要么是 `0x18xx` 一族，而那些的基址寄存器是 **`ebx` = 玩家对象**（`+0x1868` 英雄经验累加器、
  `+0x18A4` 就是 `0x534` 的来源、`+0x1890/0x1892/0x1893` 二英雄修炼），不是记录。
- 语料：30/30 条在 `0x1C68..0x2BF5` 内**没有任何非零字节**。

⇒ 「整块克隆携带」不只是权宜，是**可证正确**。仍不能断言「任何构建都不用」，
但可行动的问题已经关闭，降级为观察项。

### 3.3 B5 六个变长应答体 —— 推进 1/6

`0x0130` 的信封与 12 字节体头已在 §2.3 解出（Type 2、`DataLength = 0x0C + n`、
体头 `word ident, word 0, dword 0, dword 0`、尾巴从记录指针本身起拷 `dword[记录]` 字节，
**那个长度 dword 本身也在传输内容里**）。`0x0055 / 0x005D / 0x0062 / 0x0132 / 0x0139`
本轮未推进，预算给了保真度缺陷与 B1/B4。

---

## 4. 复核中发现的既有错误（本轮增量）

| # | 出处 | 原结论 | 实际 | 决定性字节 |
|---|---|---|---|---|
| F1 | `YanshenNativeItemLayout` `PackOrigin` | `item[0x54]` 可由 `killerName == pname` 推出 | 自己获得那类**角色名槽恒空**，判据恒假，931/1232 条被改写 | 眼神 `0x100828E1` 一带不写 `+0x74`；语料交叉表 |
| F2 | 同文件 `MapTitleSize` 旁注 | 「`0x2C` 没有 .text store」 | **有两个**，且 257/1363 条该 dword 非零 | 眼神 `0x1005863E`、`0x10082891` 都写 `item+0x4C` |
| F3 | 第二轮 §1.2 | `0x0078/0x0079/0x007A` 各有四个写入点 | 各只有**一个**；那四个 VA 是同一帧序言的四条指令 | `66 c7 45 98 78 00` 等全镜像各 1 命中 |
| F4 | 第二轮 §1.2 / §1.4 | 「帧型都是 1，`DataLength` 都是 `0x48`」 | `0x0072`、`0x0130` 是 **Type 2 变长** | DBServer `0x59CEFA`、`0x59E2D2` |
| F5 | 第二轮 §6 B1 / codec 注释 | `acct+0x1C` 有两个写入点，语义未证 | 只有一个；语义 = 记录 `0x534` 的写穿缓存 | DBServer `0x5AD676` ← `0x59C0FA` ← `mov cx,[record+0x534]` |
| F6 | 第二轮 §6 B1 | `0x426436` 是写入点之一 | 是 Delphi RTL 的动态数组掩码代码，撞了偏移 | DBServer `0x426424 not edx` / `0x426426 and dx,[eax+0x1c]` |
| F7 | master `8699cae5` | 标题「Aim HumanDbCodec type-1 fixture at the V bank」 | 它把 fixture 的节类型字节从 0 翻成 1，而断言与其正上方的注释仍指 S 库，导致四处 `ScriptS[7]` 全抛 `KeyNotFoundException`；**type 0 就是 S 库** | M2Server 类型 0 臂 `0x6E4544` → `0x6E457C add eax,0x804`；`GetS 0x6DF1CF` 也是 `+0x804` |

F7 补一句归属：把断言写成 `ScriptS[7]` 的是**我上一轮的 `1eed3455`**（19:36），
而 fixture 字节是 `8699cae5`（18:27）先翻的。两边合起来才坏。按字节，**断言是对的、fixture 是错的**，
所以改 fixture 而不是弱化断言（§4.17）。

---

## 5. 判定计数

| 判定 | 数 |
|---|---|
| `DIVERGENT`（本轮已修） | **2**：`item[0x54]` 重算、`item[0x20..0x2B]` 先清后写 |
| `MISSING`（布局已解，触发未接） | **10**：§2.3 十个出站码 |
| 审计工具缺陷（已修） | **1**：`NativeHumanDbCodecCheck` fixture 节类型 |
| `BLOCKED` 解决 | **2**：B1、B3 |
| `BLOCKED` 实质结案 | **1**：B4 |
| `BLOCKED` 部分推进 | **1**：B5（1/6） |
| `BLOCKED` 新增 | **1**：B6 |

### 仍然 BLOCKED

| # | 项 | 缺什么 |
|---|---|---|
| B5 | `0x0055 / 0x005D / 0x0062 / 0x0132 / 0x0139` 的逐字段语义 | 本轮未反；`0x0130` 已解 |
| B6 | 谁把 `1` 写进 `item[0x74]`（1363 条里 931 条是 1） | 眼神 `.text` 全量扫描只有写 `2` 的两处，没有读点；`0x1006FD7F` 已排除（脚本隧道解析器对象）。可能在被 Themida 虚拟化的函数里，或在 M2Server 侧未定位 |
| B7 | `acct+0x1C` 缓存是否在启动时预载 | 决定「账号未缓存时原生写 0」是否可观测。语料 30/30 为 0，当前不可观测 |

### 优先级建议

1. **§1.4（已修）** —— 唯一的数据完整性缺陷，931 个字节存进去和读出来不是同一份。已 PASS。
2. **F7（已修）** —— 一个审计工具在 master 上就是红的，会掩盖后续回归。
3. **§2.3 十个码的触发接线** —— 需要先复刻 `sub_5D25EC` 那条事件队列，是独立的一块工作量，
   建议单独排期；线序已经钉死，接线时不会再走错字节。
4. **B6** —— 在找到写 `1` 的地方之前，任何「C# 主动生成掉落戳」的功能都不要做，
   否则会产出原生不会产生的 `0`。当前 C# 没有这类生产者，风险为 0。

### 另外报一个不是我造成、但 master 现在是红的

`HeroRuntimeCodecCheck` 崩在 `0xE0434352`：
`HeroObject.TrySetNativeLevel`（`GameSvr/Actors/HeroObject.cs:2095`）→ `TBaseObject.SendMsg`
（`TBaseObject.cs:3620`）→ `HUtil32.EnterCriticalSection(null)`，即裸 in-proc fixture 里
消息队列锁没初始化。`git log master..HEAD` 显示我这条分支只比 master 多两个提交，
都不在这条调用链上；且 master 今天刚给这个工具加了 10 行。属英雄域，未动。
