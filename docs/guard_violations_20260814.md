# guardfix —— 两道反臆造闸门 + 三个 YB 关联工具的逐条裁决

- 分支：`w/guardfix`（自 `master` 69f049b6 拉出，未触碰 master）
- 底本：`staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`，`file_off = VA − 0x400000`
- 结论摘要：**5 个工具全部转绿；无一条依靠弱化断言取得。**
  4 个 SM_YB 常量裁定为**有据（非臆造）**；ProvenanceGuard 违规裁定为**误报**。
  `SystemModule/Grobal2.cs` **无需任何改动**。

| 工具 | 原判 | 根因分类 | 处置 |
|---|---|---|---|
| WireIdentPinCheck | FAIL ×4 | 工具已知下界局限（header limit 1）真实命中 | 补录发送点 VA |
| ProvenanceGuardCheck | FAIL ×1 | 闸门取窗差一行的误报 | 改闸门为块级取窗 + 自证 |
| NativeYbConsignmentQueryCheck | FAIL | 陈旧/写反的断言（代码是对的） | 按字节反转断言 + 修错误注释 |
| NativeYbDealPurchaseStateMachineCheck | FAIL | 陈旧断言：文本计数 ≠ 其自述的 runtime 引用 | 剥注释/字面量后计数，并收紧为文件名集合 |
| YbDbCancelDealProtocolCompatCheck | FAIL | 陈旧断言：把镜像转录的名表当成暴露面 | 按"注册处理器/引用编解码器"重新定义暴露 |

---

## (1) WireIdentPinCheck —— 4 条疑似臆造协议常量

### 报错原文

```
WireIdentPinCheck FAIL: Grobal2.SM_YB_CONSIGN_INBOX = 3001 is a new SM_ constant with no
send-slot site in the image and no production traffic. Add the emitting VA to the wire
table, or name it RM_ if it is an internal tag.
WireIdentPinCheck FAIL: Grobal2.SM_YB_CONSIGN_OUTBOX = 3002 （同上）
WireIdentPinCheck FAIL: Grobal2.SM_YB_DEAL_BUY_HISTORY = 3005 （同上）
WireIdentPinCheck FAIL: Grobal2.SM_YB_DEAL_SELL_HISTORY = 3006 （同上）
WireIdentPinCheck FAIL: 4 violation(s)
```

### 裁决：**有据，非臆造（NOT INVENTED）**

四个值在原生镜像里都有真实发送点，只是发送点的形态是工具的静态扫描**结构上够不到**的那一类。

### 字节证据

**第一步 —— 为什么 `mov dx,imm16` 扫描是 0 命中。**
全镜像穷举 `66 BA/B9/B8/BB` + `{3001,3002,3005,3006}`（即 dx/cx/ax/bx 的 16 位立即数装载），
**四个值合计 0 命中**。这解释了闸门为什么报警，但不足以判臆造 —— 闸门自己的头注释就写明
静态集合是 LOWER BOUND。

**第二步 —— ident 是以 32 位立即数写进栈局部的。**
共享发射器 `sub_6E80CC(Self=eax, rec=edx, selector=ecx, count=[ebp+8])`，
选择子经一条减法阶梯翻译成 ident 并停在 `[ebp-0x10]`：

```
006E80D5  894df4               mov [ebp-0xc], ecx          ; 选择子入栈
006E80DE  8b45f4               mov eax, [ebp-0xc]
006E80E1  2d7a040000           sub eax, 0x47A
006E80E6  7410                 je  0x6E80F8
006E80E8  48                   dec eax
006E80E9  741e                 je  0x6E8109
006E80EB  83e805               sub eax, 5
006E80EE  742a                 je  0x6E811A
006E80F0  48                   dec eax
006E80F1  7430                 je  0x6E8123
006E80F3  e9f0010000           jmp 0x6E82E8                ; 未知选择子：不回包

006E80F8  c745f0b90b0000       mov dword [ebp-0x10], 0xBB9  ; = 3001
006E8109  c745f0ba0b0000       mov dword [ebp-0x10], 0xBBA  ; = 3002
006E811A  c745f0bd0b0000       mov dword [ebp-0x10], 0xBBD  ; = 3005
006E8123  c745f0be0b0000       mov dword [ebp-0x10], 0xBBE  ; = 3006
```

**第三步 —— 四条臂汇合到同一个发送槽。**
行序列化循环收尾处：

```
006E82CE  668b55f0             mov dx, word [ebp-0x10]
006E82D2  8b45fc               mov eax, [ebp-4]
006E82D5  8b18                 mov ebx, [eax]
006E82D7  ff9354020000         call dword [ebx+0x254]      ; ← 发送槽（Buf/Len 尾）
```

`FF 93 54 02 00 00` 正是闸门 `SINK_DISPS` 枚举的 `[obj+0x254]` 形态（rm=3 → ebx）。
所以这**不是**"没有发送点"，而是"发送点的 DX 来自内存操作数"。

**第四步 —— 可达性：回解 `sub_6E80CC` 的四个直接调用方。**
`CM 分派表 0x6D8315`（base ident 1200）→ thunk → 管理器 `[[0x7D6ABC]]` 方法 → 发射器：

| CM | thunk | 管理器方法 | 选择子装载 | 调用点 | ident |
|---|---|---|---|---|---|
| 1252 | 0x6E7E3C | 0x632A14 | `0x632B0E B9 7A 04 00 00 mov ecx,0x47A` | 0x632B17 | 3001 |
| 1253 | 0x6E7E90 | 0x632E7C | `0x632F86 B9 7B 04 00 00 mov ecx,0x47B` | 0x632F8F | 3002 |
| 1256 | 0x6E83AC | 0x632BEC | `0x632CF3 B9 80 04 00 00 mov ecx,0x480` | 0x632CFC | 3005 |
| 1257 | 0x6E8400 | 0x632D34 | `0x632E3E B9 81 04 00 00 mov ecx,0x481` | 0x632E47 | 3006 |

四条调用点各自落在对应管理器方法的地址区间内，CM↔SM 配对与 `Grobal2.cs` 现有写法**逐条吻合**。

**第五步 —— 独立佐证。**
仓内已有的 `NativeYbConsignmentQueryCheck` 早就把同样的字节钉死了
（`Pin(0x006E80F8, "C745F0B90B0000", "0x47A -> SM 0xBB9 (3001)")` 等四条），且该 Pin 一直 PASS。
两条互不相关的取证路径给出同一结论。

### 闸门为什么漏掉

`staging/_sm1_work/s08_final.py` 的两趟算法在此处双双失效：

1. **pass 1（后向锚定解码）**：`track()` 在 `0x6E82CE` 看到 `mov dx, word [ebp-0x10]`，
   操作数是 MEM 而非 IMM，返回 `("DYN", ...)`，该 sink 落入 `dyn_sinks`。
2. **pass 2（前向符号模拟）**：`simulate()` 其实会把 `stack[-0x10]` 记成 IMM，
   但调用方只接受 `sym[0] == 'ARG'`，IMM 结果被直接丢弃；且模拟是**线性反汇编、无路径敏感**，
   四条臂会顺序覆写同一个栈槽，走到 sink 时只剩最后一条（0xBBE），本来也只能捞回 1/4。

这正是工具头注释 "TWO KNOWN LIMITS → 1. …the static set is a LOWER BOUND" 的真实命中。

### 处置

按闸门自述的补救路径（"Add the emitting VA to the wire table"）执行，落在
`AuditTools/WireIdentPinCheck/Program.cs`：

- 新增 `WireTables.WireStackLocal = { 3001, 3002, 3005, 3006 }`，**单列一张表**而不是塞进
  生成的 `Wire` 数组 —— `Wire` 由 `s08_final.py` 生成，`ExpectedWireCount` 仍为 503，
  重新生成不会冲掉手工成果。
- `WireSet` 改为 `Wire.Concat(WireStackLocal)`。
- `CheckEmbeddedTables` 增加 `ExpectedWireStackLocalCount = 4` 与四条 mustHave 锚点。
- `CheckPinnedIdents` 增加四条 pin，把 CM→选择子→ident 的完整链路冻结在断言里。
- 头注释的 limit 1 补上栈局部形态。

结果：`PASS WireIdentPinCheck wire=503 stack-local=4 traffic=198 sm=598 rm-space-collisions=0 baseline=94`

### 需主代理在 Grobal2.cs 执行的改动

**无。** 四个常量的值与其上方的注释（1428–1443 行）经字节复核**全部正确**，
包括 "0x6E80DE-0x6E8129 translates it into these before the vtbl+0x254 send" 这句 ——
唯一可挑剔的是它没写发送点 VA 是 `0x6E82D7`，但这不构成事实错误。

---

## (2) ProvenanceGuardCheck —— 1 条违规

### 报错原文

```
GameSvr\Actors\TBaseObject.Base.cs:1545 引用 ref 源 'ObjBase.pas' 但未标注来源非战神,
也未给出战神 EA(sub_XXXXXX/0xXXXXXX)。
      行内容: ///        <c>staging/ref-MirServer-Delphi/EM2Engine/ObjBase.pas:18605</c>）。
```

### 复核结论：**主代理的初判正确 —— 确为误报，且根因就是窗口过窄，差一行。**

### 根因（精确到行）

闸门取窗是 `lo = i-6, hi = i+6`（0-based）。违规行 1545 → `i = 1544` → 窗口 = **1-based 1539..1551**。

而 `nativeEvidence` 正则是 `sub_[0-9A-Fa-f]{6}|0x00[0-9A-Fa-f]{5,6}|@0x[0-9A-Fa-f]{6}`，
窗口内那些 `0x67C354 / 0x67C381 / 0x67C38E` 都是 6 位十六进制、**不带 `0x00` 前缀**，不匹配；
唯一能匹配的 `sub_67C150` 在**第 1538 行** —— 比窗口下界早整整一行。

对照证据：**同一个文档块里的第 1544 行**（`ref-MIR2/.../ObjBase.pas:20510`）窗口是 1538..1550，
刚好含住 1538 行的 `sub_67C150`，所以它被判为 `nativeBacked` 而没报。
**同块、相邻两行、一报一放** —— 这本身就是窗口人为切断语义单元的铁证。

而 `MakeGhost` 的 `///` 块（1518–1555）里战神 EA 密集：`sub_768060`(1519)、`0x768138`(1520)、
`0x76807B..0x7680E4`(1521)、`0x7680E9/0x7680ED/0x7680EF/0x7680F3/0x7680F8/0x7680FE`(1523–1528)、
`sub_7681B4`(1529)、`0x7681D7/0x7681EE`(1529–1530)、`0x768060..0x76812E`(1532)。
两条 Delphi 引用被明确写成"三条独立旁证"的第 (3) 条，主证据本来就是战神字节。

### 处置：修闸门，不修注释

闸门**自己的头注释**（第 19–20 行）写的判据就是
"凡代码注释引用 ref 源文件名时，必须在**同一注释块内**显式标注来源非战神"。
±6 行邻域只是对"同一注释块"的近似。所以改成真的按注释块取窗，是**落实原判据**而非放宽。

实现（`AuditTools/ProvenanceGuardCheck/Program.cs`）：

- `CommentBlockRange` 取以该行为中心、上下连续且**同类**的注释行；
  代码行、空行、以及 `///` ↔ `//` 切换都会截断，块不会越过一段代码够到隔壁注释里的地址。
- `ContextBlock` = 注释块 **∪ 原 ±6 行邻域**。取并集而非替换：邻域是旧口径的下限，
  保留它才能保证"凡旧实现抓得到的违规现在依然抓得到"，且引用写在代码行上的情形不会新报。
- 新增 PASS 字段 `blockExtended` —— 只因放宽才判合规的条数，把唯一被放宽的口子显式计量。

### 防止改松到失去意义（4 个自检 + 2 个端到端反例，均已实测）

每次运行前强制跑 `SelfTest`，任一失灵即拒绝扫描：

1. 同块内 12 行开外的 `sub_XXXXXX` 应算数（并断言该反例本身是旧邻域抓不到的，否则证明不了任何事）；
2. **块内既无 EA 也无标注 → 必须仍判违规**；
3. **EA 在隔着代码行的另一个注释块里 → 不得算数**；
4. 引用写在代码行（字符串字面量）上时，仍按 ±6 邻域取证（旧口径不退化）。

端到端反例（临时构造 `GameSvr/Bad.cs` 与 `GameSvr/NeighbourBlock.cs`，验完删除）：
两条**都仍然 FAIL**，报错正确。

破坏性验证：把 `ContextBlock` 手工改成"整文件取窗"后，自检第 3 条立即拦下并拒绝扫描。

### 全树影响面

`PASS files=996 refCitations=24 annotated=18 nativeBacked=6 blockExtended=1 unprovenanced=0`

**`blockExtended=1`** —— 996 个文件、24 条 ref 引用中，只有目标那一条因块级取窗改判。改动是外科级的。

---

## (3) NativeYbConsignmentQueryCheck

### 报错原文

```
Unhandled exception. System.InvalidOperationException: FAIL: wrapped tick is rejected
   at g__Assert  (Program.cs:377)
   at g__CheckThrottlePredicate (Program.cs:269)
```

### 根因分类：**(a) 陈旧测试 —— 断言把原生语义写反了，生产代码是对的**

### 字节证据

三条节流规则的现场：

```
CM 1252（MoreThanTenMs）
  00632A63  2b5620         sub edx, [esi+0x20]
  00632A66  83fa0a         cmp edx, 0xA
  00632A69  0f86b4000000   jbe 0x632B23        ← 跳到 epilogue
  00632A6F  894620         mov [esi+0x20], eax  ← 写回在分支之后
  ...
  00632B17  e8b0550b00     call 0x6E80CC        ← 发射器
  00632B1E  e8ad04ddff     call 0x402FD0
  00632B23  33c0           xor eax, eax         ← jbe 落点，在发射器之后

CM 1257（MoreThanTwoMs）
  00632D83  2b5624         sub edx, [esi+0x24]
  00632D86  83fa02         cmp edx, 2
  00632D89  0f86c4000000   jbe 0x632E53
  00632D8F  894624         mov [esi+0x24], eax

CM 1256（DifferentTick，无 cmp，直接读 sub 的 ZF）
  00632C3B  2b5624         sub edx, [esi+0x24]
  00632C3E  0f84c4000000   je  0x632D08
  00632C44  894624         mov [esi+0x24], eax
```

两个事实同时成立：

1. **`jbe` 是"拒绝"臂** —— 落点 `0x632B23` 在发射调用 `0x632B17` **之后**，也在写回 `0x632A6F` 之后。
2. **`jbe` 是无符号比较** —— tick 倒退时 `sub` 得到 `0xFFFFFFFF`，它 **> 0x0A**，分支不跳，请求**放行**。

所以原生对"回绕 tick"的行为是：**三条规则全部放行**。原生**没有任何 wrap 守卫**。

生产代码 `NativeYbConsignmentQuery.ThrottleAllows` 写的是 `(uint)elapsed > 10u`，**完全正确**。
错的是：

- 测试第 269–270 行断言 `!ThrottleAllows(tenMs, -1)`（"wrapped tick is rejected"）；
- `ThrottleAllows` 上方的 XML 注释也写着"a tick that went backwards (wrap) also fails" ——
  **注释与它下面自己的代码互相矛盾**。

### 处置

- 断言按字节反转，并**追加**一条 `int.MinValue` 断言，把整个负半平面钉死。
  断言数量不减反增，强度不降。
- 修正 `GameSvr/Services/NativeYbConsignmentQuery.cs` 里说反了的 XML 注释，
  补上 `jbe` 是拒绝臂的地址依据，并写明"原生没有 wrap 守卫，不要加"。

结果：`PASS idents=4 asserts=239`

---

## (4) NativeYbDealPurchaseStateMachineCheck

### 报错原文

```
Unhandled exception. System.InvalidOperationException:
  dormant state machine runtime reference count: expected 1, actual 6
   at g__Equal (Program.cs:316)
   at g__TestDormantBoundary (Program.cs:200)
```

### 根因分类：**(a) 陈旧测试 —— 计数机制（整文件文本匹配）从未匹配它自述的意图（runtime reference）**

### 证据

多出的 5 处**全部是文档，零代码引用**：

| 文件 | 行 | 形态 |
|---|---|---|
| `GameSvr/Services/NativeYbConsignmentQuery.cs` | 41 | `///` 文档注释 |
| `GameSvr/Services/NativeCmQ1FailClosed.cs` | 26 | `///` 文档注释 |
| `GameSvr/Services/NativeCmQ1FailClosed.cs` | 159, 162 | `Add(...)` 的中文描述**字符串字面量** |
| `GameSvr/Players/TPlayObject.NativeYbConsignment.cs` | 39 | `///` 文档注释 |
| `GameSvr/Players/TPlayObject.MallCm.cs` | 20, 227 | `//` 与 `///` 注释 |
| `GameSvr/Players/TPlayObject.NativeCmProtocol_Q1.cs` | 30, 403, 419 | `///` 与 `//` 注释 |

正则穷举 `NativeYbDealPurchaseStateMachine\s*\.` / `using .*NativeYbDealPurchaseStateMachine` /
`new NativeYbDealPurchaseStateMachine` → **No matches found**。
即：无成员访问、无 using、无实例化。**休眠不变式从未被破坏。**

时间线佐证：工具随基线 `d5d00744` 引入；这些交叉引用注释来自其后的 CM Quarter 1 移植
`d479f644`（"Port CM Quarter 1 (idents 1054..1260) as evidence-backed fail-closed handlers"）。
计数从 1 涨到 6 全部来自文档增长。

一个"把准确的交叉引用变成违规"的闸门，只能靠**删掉正确的文档**来满足 —— 与它的目的正相反。

### 处置

- 新增 `StripCommentsAndLiterals`（剥 `//`、`///`、`/* */`、`"..."`、`@"..."`、`'.'`，
  其余字节原样保留），计数前先剥。
- 断言**收紧**而非放宽：从裸计数 `Equal(1, count)` 改为钉死**文件名集合**
  `Equal("NativeYbDealPurchaseStateMachine.cs", ...)`，违规时直接报出是谁接的线。
- 新增 `AssertCodeReferenceScannerWorks`：3 条必须存活的真引用形态（成员调用 / using static /
  类声明）+ 5 条必须消失的非代码形态（`//`、`///` 的 `<see cref>`、`/* */`、中文描述字面量、逐字字符串）。

端到端反例：临时注入 `GameSvr/ZZTempWireUp.cs`（真实调用 `BeginValidatedPurchase`），
闸门立即 FAIL 并点名 `ZZTempWireUp.cs`；删除后恢复 PASS。

---

## (5) YbDbCancelDealProtocolCompatCheck

### 报错原文

```
Unhandled exception. System.InvalidOperationException:
  117 PAS distinction and runtime fail-closed: admin command exposes dormant 124 at runtime
 ---> admin command exposes dormant 124 at runtime
   at g__Reject (Program.cs:291)
   at g__CheckRuntimeFailClosed (Program.cs:197)
```

### 根因分类：**(a) 陈旧测试 —— 把"从镜像转录的名表数据"误判成"运行期暴露面"**

### 证据

`GameSvr/Command` 下 `CancelYBDeal` 只有一处：
`GameSvr/Command/NativeGmCommandRegistry.cs:83`
`[96] = "CancelYBDeal", // perm 4 IMPL @00624FF8`

该文件头写明 `// Auto-generated from flat_image.bin GM table 0x007B4654 stride 0x120, 430 slots.`

**转录属实（字节核验）**：镜像中存在 ShortString `0C 'CancelYBDeal'` @ `0x7BBB54`，
且它与前后两条在表里正好各差一个 0x120 步长，索引差与注册表列出的索引差逐条吻合：

| 索引 | 槽地址 | 镜像 ShortString | 注册表 |
|---|---|---|---|
| 95 | 0x7BBA34 | len=12 `DelSelfSkill` | `[95] = "DelSelfSkill"` |
| **96** | **0x7BBB54** | **len=12 `CancelYBDeal`** | **`[96] = "CancelYBDeal"`** |
| 97 | 0x7BBC74 | len=10 `ChgmanKind` | `[97] = "ChgmanKind"` |

（锚点 index 88 @ `0x7BB254` = `GM前撞`。低索引区因注册表字典有空洞（33→51），
锚点仿射外推会漂移，故此处只主张已实测的**局部连续三条**，不主张整表。）

case body 亦属实：
```
00624FF8  8b55cc   mov edx,[ebp-0x34]
00624FFB  8b45f8   mov eax,[ebp-8]
00624FFE  e819230b00  call 0x6D731C
00625003  e944660000  jmp 0x62B64C
```

**该名表够不到任何运行期路径**：唯一消费点是
`CommandManager.ApplyNativeFormGmCommandIni`（`CommandManager.cs:148`），
它把 `FormGMCommand.ini` 的索引映射到**已注册**命令上以便改名；
`OriginalCommandMaps.TryGetValue(defaultName, out var cmd)` 取不到就 `continue`。
全仓 `*.cs` 穷举确认：**没有任何以 `CancelYBDeal` 注册的命令处理器。**
（`GameSvr/Services/NativeGmCurrencyCommands.cs` 里的同名条目是
`CoreBodyDeferred = true` 的描述符元数据，且由 `NativeGmCurrencyCommandsCheck` 单独看守，该工具 PASS。）

结论：旧检查唯一能被满足的方式是**删掉一条从镜像读出来的事实** —— 方向完全反了。

### 处置

把"暴露"按其标签的字面意思重新定义：

- 逐文件扫描 `GameSvr/Command`：命中 `CancelYBDeal` 的文件若**不是**
  `NativeGmCommandRegistry.cs`，直接 FAIL 并**点名文件**。
- 对注册表文件放行，但**必须**仍带那条精确转录
  （`[96] = "CancelYBDeal", // perm 4 IMPL @00624FF8`），且**不得**长出 `[GameCommand(` 注册。
- **新增**两条旧检查根本没做的拒绝：命令面不得出现 `YbDbCancelDealProtocol`、
  不得出现 `RequestCancelYbDeal`。

净效果是**收紧**：多了两条拒绝、违规会点名，只对一条有字节背书的转录开了口。

端到端反例（两条，均实测命中后删除）：

1. `GameSvr/Command/Commands/ZZTempGm.cs`（`[GameCommand("CancelYBDeal", ...)]`）
   → `admin command exposes dormant 124 at runtime: ZZTempGm.cs`
2. `GameSvr/Command/Commands/ZZTempCodec.cs`（引用 `YbDbCancelDealProtocol`）
   → `command surface references the dormant 124/1124 codec`

---

## 复验（分支 `w/guardfix` 末态）

```
[WireIdentPinCheck]                      exit=0  PASS wire=503 stack-local=4 traffic=198 sm=598 rm-space-collisions=0 baseline=94
[ProvenanceGuardCheck]                   exit=0  PASS files=996 refCitations=24 annotated=18 nativeBacked=6 blockExtended=1 unprovenanced=0
[NativeYbConsignmentQueryCheck]          exit=0  PASS idents=4 asserts=239
[NativeYbDealPurchaseStateMachineCheck]  exit=0  PASS dormant ... production=fail-closed
[YbDbCancelDealProtocolCompatCheck]      exit=0  PASS tests=6 request=124/Q0/P0/65+N response=1124/64B runtime=fail-closed
```

## 取证脚本（随分支入库，可复跑）

| 脚本 | 作用 |
|---|---|
| `tools/guardfix_yb_ident_probe.py` | 全镜像穷举 3001/3002/3005/3006 的立即数形态；反汇编选择子阶梯 |
| `tools/guardfix_yb_sendslot_trace.py` | 追 `[ebp-0x10]` 到 `call [ebx+0x254]` |
| `tools/guardfix_yb_caller_probe.py` | 回解 `sub_6E80CC` 四个调用方的 ECX；打印 CM thunk |
| `tools/guardfix_yb_throttle_probe.py` | 证明 `jbe` 是拒绝臂（落点在发射器之后） |
| `tools/guardfix_gmtable_probe.py` | 定位 `CancelYBDeal` ShortString 与 case body |
| `tools/guardfix_gmtable_verify.py` | 按锚点步长比对注册表与镜像 |

## 仍未决 / 交待事项

1. **`SystemModule/Grobal2.cs` 无需改动**（热点文件未触碰）。四个 SM_YB 常量有据，注释准确。
2. `ProvenanceGuardCheck` 的 `nativeEvidence` 正则只认 `sub_XXXXXX`、`0x00XXXXX(X)`、`@0xXXXXXX`
   三种形态，**不认**代码里最常见的 `0x768060` / `0x7680E9` 这类 6 位裸地址。
   `MakeGhost` 块里那么多战神 EA，最终只靠 `sub_768060` / `sub_7681B4` 匹配上。
   放宽这条正则会显著改变全树判定面，**超出本次授权范围，未动**，建议主代理单独评估。
3. `sub_6E80CC` 还有第 5 个调用方 `0x63C450`，其 ECX 来自 `[ebp+8]`（运行期值，
   `0063C44B 8b4d08 mov ecx,[ebp+8]`）。说明同一发射器可能还产出别的 ident。
   本次只需判定这 4 个，**未展开**；如要补全 SM 3000 段应从这里入手。
4. `staging/_sm1_work/s08_final.py` 本身未改。pass 2 丢弃 IMM 结果、且无路径敏感，
   同类"栈局部转发"的 ident 仍会被漏。本次以 `WireStackLocal` 手工表补位，
   **生成器的系统性缺陷仍在**，建议单独立项。
