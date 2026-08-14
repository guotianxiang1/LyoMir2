# ECON-17 专项：`obj+0x675` 权限门定性与闭合

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\econ17`　分支：`w/econ17`
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase = 0x400000，17,661,952 B）
- 上游：`docs/eqv_shard01_20260814.md` 将 ECON-17 判为 **BLOCKED**
- **结论：BLOCKED 解除 → 判 `MISSING`，本轮已外科补齐（GameSvr 构建 0 错）**

---

## 0. 摘要

分片 01 对 ECON-17 的两条封锁理由**均已被逐字节推翻**：

| shard01 理由 | 本轮结论 |
|---|---|
| (a) `obj+0x675` 字段语义 UNPROVEN | **已证**：`+0x675 = m_btPermission`，setter 在 `0x6B1E80` 直接落 `GetHumPermission`(`sub_65583C`) 的返回值 |
| (b) 守卫函数「非已核实的 NPC 买/卖入口」 | **恰恰就是**：守卫函数 `sub_644244` 是 `ClientBuyItem` 的**孪生变体**，两者由瘦分派器 `sub_63EDE8` 按 NPC 属性位二选一；`0x63EB34` 入口无此门，只是因为它是分派器的**另一条臂** |

另外，shard01 把 `0x644457 xor eax,eax` 读作「返回 false = 拒绝」，实为 Delphi SEH 拆帧序列
`xor eax,eax / pop edx / pop ecx / pop ecx / mov fs:[eax],edx` 的第一条。`sub_644244` 是
**procedure**（`ret 4`，无返回值），权限不足时**静默返回、不发送任何消息**。

---

## 1. 函数定位更正：`0x6441ED` 不是函数头

`0x6441ED` 落在**前一个函数**的字符串清理尾巴里：

```
006441ED  8d 45 d4              lea eax,[ebp-0x2c]
006441F0  ba 02 00 00 00        mov edx,2
006441F5  e8 2a 13 dc ff        call 0x405524          ; @LStrArrayClr
006441FA  8d 45 f0              lea eax,[ebp-0x10]
006441FD  e8 fe 12 dc ff        call 0x405500          ; @LStrClr
00644202  c3                    ret
00644203  e9 30 0c dc ff        jmp 0x404E38           ; @HandleFinally
0064420A  8b c3 / 5f 5e 5b ...  mov eax,ebx / pop edi,esi,ebx
00644212  c3                    ret                    ; <== 前一函数(sub_644044)结束
```

`0x644213` 之后是常量池，正是 NpcSave 文件名的三段 Delphi 长串：

| VA | refcount/len | 内容 | 引用点 |
|---|---|---|---|
| `0x64421C` | `ff ff ff ff 08 00 00 00` | `NpcSave\` | `0x644074` |
| `0x644230` | `ff ff ff ff 01 00 00 00` | `-` | `0x6440A6` |
| `0x64423C` | `ff ff ff ff 04 00 00 00` | `.Sav` | `0x6440BC` |

> 这三个串属于 **`sub_644044`**（存盘函数），**不是** `sub_644244` 的证据。

含 `0x644274` 的函数真正的头在 **`0x644244`**：

```
00644244  55                    push ebp
00644245  8b ec                 mov ebp,esp
00644247  81 c4 e0 fe ff ff     add esp,-0x120
0064424D  53 56 57              push ebx / esi / edi
0064425B  89 4d f8              mov [ebp-8],ecx        ; 参2 = sItemName (AnsiString)
0064425E  89 55 fc              mov [ebp-4],edx        ; 参1 = TPlayObject
00644261  8b f0                 mov esi,eax            ; Self = TMerchant
00644266  68 78 44 64 00        push 0x644478          ; SEH
00644271  8b 45 fc              mov eax,[ebp-4]
00644274  80 b8 75 06 00 00 03  cmp byte [eax+0x675],3
0064427B  0f 86 d6 01 00 00     jbe 0x644457           ; <=3 -> 静默返回
```

签名：`procedure TMerchant.<X>(PlayObject: TPlayObject; const sItemName: string; nMakeIndex: Integer)`（`ret 4`）。

---

## 2. 身份认定：`sub_644244` = `ClientBuyItem` 的 property-9 孪生变体

### 2.1 调用链（全镜像 `E8 rel32` 扫描，唯一解）

```
sub_6BAD98  (CM_USERBUYITEM 处理器)
   ├ 0x6BADBB cmp byte [ebx+0x73],0    ; jne -> 退出
   ├ 0x6BADC1 cmp byte [ebx+0x461],0   ; jne -> 退出
   ├ 0x6BADD3 call 0x649A58 / 0x64A844 ; 由 [[0x7D6784]] 查 NPC
   ├ 0x6BADF2 cmp esi,[ebx+0xCD8]      ; 必须等于「当前对话的 NPC」
   ├ 0x6BAE02 call 0x7743E0 (cx=0xF)   ; 15 格距离门
   └ 0x6BAE21 call 0x63EDE8 ────────────┐
                                        │
sub_63EDE8  (瘦分派器, EDX/ECX 原样透传) │
   0063EDF2  f6 86 55 04 00 00 02  test byte [esi+0x455],2
   0063EDF9  74 0a                 je   0x63EE05
   0063EDFB  57 / 8b c6 / e8 41 54 00 00   call 0x644244   ; ← 置位臂
   0063EE05  57 / 8b c6 / e8 27 fd ff ff   call 0x63EB34   ; ← 清零臂 = ClientBuyItem
   0063EE10  c2 04 00              ret 4
```

- `calls_to(0x644244)` → **仅** `0x63EDFE`；`dword_refs(0x644244)` → **0 个**（不在任何 VMT 槽）。
- `calls_to(0x63EDE8)` → **仅** `0x6BAE21`。
- `calls_to(0x63EB34)` → **仅** `0x63EE08`（即分派器另一臂）。

`sub_63EB34` 就是 shard01 已核实的 `ClientBuyItem`：`0x63EC47 call 0x63F380`(GetUserItemPrice) +
`0x63EC54 call 0x640208`(GetUserPrice)，与 ECON-01 完全吻合。**所以 shard01 说的「0x63EB34 入口无 +0x675 门」是对的，
但它不是唯一入口——真正的协议入口是分派器。**

### 2.2 两臂逐句同构对照（`sub_63EB34` vs `sub_644244`）

| 环节 | `sub_63EB34`（常规买） | `sub_644244`（property-9） |
|---|---|---|
| 入口权限门 | 无 | `0x644274 cmp [+0x675],3 / jbe` **静默返回** |
| 遍历 | `[+0x56C]` 组表 → 组内 `[grp+0x10]` | 同（`0x644288`/`0x6442CD`） |
| 组名匹配 | `0x63EBEB GetItemName(item0)` → `0x63EBF5 @LStrCmp` | 同（`0x6442EE`/`0x6442F9`） |
| 负重前置门 | `0x63EBD9 call 0x73C950` 失败→ n=2 | **无** |
| 静态货判据 | `0x63EC2A sub al,5/jb; sub al,0x1a/je; sub al,0xb/je` | 同（`0x644334`，逐字节相同） |
| MakeIndex 比对 | `0x63EC39 cmp [ebx+0x20],[ebp+8]` | 同（`0x644343`） |
| **定价** | `GetUserItemPrice`→`GetUserPrice` | **无** |
| **余额判定** | `0x63EC62 call 0x6D7960` 失败→ n=3 | **无** |
| 入包 | `0x63EC7A call [vmt+0x248]`(cl=1, push 0) | 同（`0x644357`） |
| **扣款** | `0x63EC8E call 0x6C7D64` DecGold | **无** |
| 出表 | `0x63EC9B TList.Delete(j)` | 同（`0x64436E`） |
| 数据日志 | `0x63ECD7 call 0x769934`（动作 9） | 同（`0x6443A9`） |
| **城堡税** | `0x63ECF2 call 0x65B31C` | **无** |
| 空组回收 | `0x63ED03 cmp [grp+8],0` **再判一次** 后释放 | `0x6443B4` **无**这一步，直接释放 |
| **脏标记** | **无** | `0x6443DF mov byte [esi+0x5D0],1` |
| 成功回包 | `0x63ED7F cx=0x2795` + `0x63ED91 WeightChanged` | 同（`0x644423`/`0x644434`） |
| 失败回包 | `0x63EDA6 cx=0x2796`，携 n | 同（`0x644449`） |

> `0x2795 = 10133 = RM_BUYITEM_SUCCESS`，`0x2796 = 10134 = RM_BUYITEM_FAIL`（`SystemModule/Grobal2.cs:1554-1555`）。
> 六个 push 的形状与 `sub_63EB34` 尾部**逐字节一致**，故 C# 侧沿用 `ClientBuyItem` 既有的 `SendMsg` 渲染。

**⇒ `sub_644244` = 「property-9 商人的免费取货」**：不计价、不扣钱、不抽税，代价是入口的 GM 门。

### 2.3 卖出侧完全对称

```
sub_63F35C  (卖分派器)
   0063F362  f6 86 55 04 00 00 02  test byte [esi+0x455],2
   0063F369  74 0a                 je 0x63F375
   0063F36D  e8 16 51 00 00        call 0x644488     ; property-9 臂
   0063F377  e8 84 fe ff ff        call 0x63F200     ; ClientSellItem
```
`calls_to(0x63F35C)` → **仅** `0x6B92EA`（CM_USERSELLITEM 处理器，遍历 `[ebx+0x508]` 背包、比对 `[esi+0x18]`）。

`sub_644488`（`function ... : Boolean`）：
```
006444A8  c6 45 ff 00           mov byte [ebp-1],0        ; Result := False
006444AC  80 be 75 06 00 00 03  cmp byte [esi+0x675],3
006444B3  76 6b                 jbe 0x644520              ; -> cx=0x2794 六参全 0
006444C6  66 b9 93 27           mov cx,0x2793             ; RM_USERSELLITEM_OK(10131)，携 [esi+0x15C]=m_nGold
006444D7  e8 40 a5 ff ff        call 0x63EA1C             ; AddItemToGoodsList（无前置门）
00644507  e8 d4 46 12 00        call 0x768BE0  dx=0x0A    ; 数据日志动作 10
0064450C  c6 45 ff 01           mov byte [ebp-1],1        ; Result := True
00644510  c6 83 d0 05 00 00 01  mov byte [ebx+0x5d0],1    ; 脏标记
00644519  e8 c6 89 0f 00        call 0x73CEE4             ; WeightChanged
```
同样**无定价、无 IncGold、无城堡税**。

---

## 3. `[+0x455] & 2` 的语义：property-9，且 C# 早已建模

原生 NPC 有一个 **16 位属性集**位于 `[npc+0x454..0x455]`（`0x640724` 读 `[+0x454],0x40`，其余 28 处读 `[+0x455]` 的 2/4/0x10/0x20/0x80）。
`[+0x455] & 2` = 第二字节 bit 1 = **元素 9**。

C# 侧 `GameSvr/Npcs/NormNpc.cs:78-88` 已把它建成 `ushort m_wNativePasProperties`：
```csharp
public void AddNativePasProperty(int propertyIndex)
{
    if ((uint)propertyIndex >= 16) return;
    m_wNativePasProperties |= (ushort)(1 << propertyIndex);
}
```
`HasNativePasProperty(9)` 与原生 `test byte [+0x455],2` **同一位**。

property-9 的语义由商人 Run 循环坐实——它是**持久化货表**：
```
0063E73A  f6 83 55 04 00 00 02  test byte [ebx+0x455],2
0063E741  74 47                 je  0x63E78A
0063E743  80 bb d0 05 00 00 00  cmp byte [ebx+0x5d0],0     ; 脏标记
0063E74A  74 3e                 je  0x63E78A
0063E74C  2b 83 80 05 00 00     sub eax,[ebx+0x580]
0063E754  3d 60 ea 00 00        cmp eax,0xEA60             ; 60,000 ms
0063E759  72 2f                 jb  0x63E78A
0063E75B  c6 83 d0 05 00 00 00  mov byte [ebx+0x5d0],0
0063E76A  e8 d5 58 00 00        call 0x644044              ; 写 NpcSave\<脚本名>-<地图名>.Sav
```
↔ C# `Merchant.SaveNativeGoodsIfDue`（`Merchant.cs:282`，`HasNativePasProperty(9)` + `< 60000` + `_nativeGoodsDirty`）与
`GetNativeGoodsFilePath`（`Merchant.cs:215`）。**映射已存在且正确。**

`+0x5D0` 脏标记的**全部 4 个置位点**：`0x643A35`(`sub_64392C`) / `0x643D2D`(`sub_643B20`) /
`0x6443DF`(`sub_644244`) / `0x644510`(`sub_644488`)。
`sub_63EB34`、`sub_63F200` **均不置位**（见 §6 记录分歧②）。

---

## 4. `+0x675 = m_btPermission` 的独立坐实

**setter（唯一写入语义源）**：
```
006B1E63  8d 45 e4 / 8d 96 06 01 00 00  lea eax,[ebp-0x1c] / lea edx,[esi+0x106]   ; 角色名
006B1E6C  e8 03 39 d5 ff               call 0x405774        ; ShortString -> AnsiString
006B1E74  a1 50 6d 7d 00 / 8b 00       mov eax,[[0x7D6D50]]
006B1E7B  e8 bc 39 fa ff               call 0x65583C        ; GetHumPermission(sName)
006B1E80  88 86 75 06 00 00            mov byte [esi+0x675],al
```
**两个硬写点**：`0x62FAB3 mov byte [edi+0x675],5`（紧邻 `mov word [edi+0x134],0x3001`）、
`0x6BB3FD mov byte [ebx+0x675],5`（GM 指令路径，前后有 `cmp 4/jb`、`cmp 2/jb`）。

**全镜像 34 个 `+0x675` 访问点**的阈值只有 `2 / 3 / 4` 三档，与 C# `TBaseObject.m_btPermission`（byte，默认 0）一致。

### 4.1 同族「property-9 = GM 专用」的四道门

| 原生函数 | 门 | 字节 | 身份 |
|---|---|---|---|
| `sub_64392C` | `0x64396A` | `cmp [edi+0x675],3 / jbe` | **StorageAllBagItems**（PAS NPC 函数，`0x62E77E` 调用；`0x64397A` 紧跟 property-9 门） |
| `sub_643B20` | `0x643B71` | `cmp [eax+0x675],3 / jbe` | 寄存清单查询（PAS NPC 函数，`0x62E833` 调用，遍历 `[+0x56C]` 生成文本） |
| `sub_644244` | `0x644274` | `cmp [eax+0x675],3 / jbe` | **本 ECON-17 守卫**：property-9 取货 |
| `sub_644488` | `0x6444AC` | `cmp [esi+0x675],3 / jbe` | property-9 寄存 |

四者同门同界 ⇒ **property-9 商人是一整套 GM 专用（`m_btPermission >= 4`）的 NPC 物品寄存子系统**，
普通玩家在原版上完全无法触发其中任何一个操作。这也解释了「为什么免费发货是安全的」。

---

## 5. C# 等价性判定与本轮修复

### 5.1 判定

**ECON-17：`BLOCKED` → `MISSING`（本轮已修复）**

| 原生构件 | 修复前 C# | 处置 |
|---|---|---|
| `sub_63EDE8` 买分派器 | **缺失**（`TPlayObject.Operate.cs:280` 直呼 `ClientBuyItem`） | 新增 `ClientBuyItemDispatch` 并改接 |
| `sub_644244` property-9 取货 | **完全未移植** | 新增 `NativeStorageTakeItem` |
| `sub_63F35C` 卖分派器 | **缺失**（`TPlayObject.Operate.cs:246` 直呼 `ClientSellItem`） | 新增 `ClientSellItemDispatch` 并改接 |
| `sub_644488` property-9 寄存 | **完全未移植** | 新增 `NativeStorageStoreItem` |
| `sub_64392C` 的 `m_btPermission > 3` 门 | **漏门**（只有 `m_boReadyRun` + property-9） | `Merchant.cs:317` 补门 |

修复前的实际偏差（双向）：在 property-9 商人上，C# 允许**任意玩家**按 `GetUserPrice` 付费买走
（并卖出换钱）**别人寄存的物品**；原版则对非 GM **完全静默拒绝**。这既是保真缺口，也是资产安全缺口。

### 5.2 改动清单

| 文件 | 改动 |
|---|---|
| `GameSvr/Npcs/Merchant.NativeStorageNpc.cs`（新增 partial） | 两个分派器 + 两个变体，全部带字节注释 |
| `GameSvr/Npcs/Merchant.cs:11` | `class Merchant` → `partial class Merchant` |
| `GameSvr/Npcs/Merchant.cs:317` | `StorageAllBagItems` 补 `sender.m_btPermission <= 3` 门（置于 property-9 门之前，与 `0x64396A→0x64397A` 同序） |
| `GameSvr/Players/TPlayObject.Operate.cs:246` | `ClientSellItem` → `ClientSellItemDispatch` |
| `GameSvr/Players/TPlayObject.Operate.cs:280` | `ClientBuyItem` → `ClientBuyItemDispatch` |
| `AuditTools/StorageAllBagItemsStaticCheck/Program.cs` | 测试玩家补 `m_btPermission = 4`；新增权限门用例；源文本必含项加 `sender.m_btPermission <= 3` |

未触碰：`Grobal2.cs` / `TPlayObject.Message.cs` / `UsrEngn.cs`。

### 5.3 验证

- `dotnet build GameSvr` → **0 错误**（17 个警告全部为既有，新文件 0 警告）
- `dotnet run --project AuditTools\StorageAllBagItemsStaticCheck` → `PASS tests=10`

---

## 6. `m_btPermission` 阈值族一致性核查（任务项 3）

### 6.1 已确证同界

| 场景 | 原生 | C# | 结论 |
|---|---|---|---|
| 交易 GM 旁路 | `0x6C41AD` / `0x6C41BC` `cmp 4 / jae` | trade-sec TRADE-09/10 已核 | 同界 `>= 4` |
| 入包盖印门 | `0x6B73A3 cmp 3 / ja`（`>3` 跳过盖印） | `TBaseObject.cs:2453` `m_btPermission <= NativeItemAcquisitionStamp.MaxStampedGmLevel(=3)` | 同界 |
| property-9 取货 | `0x644274 cmp 3 / jbe` | 本轮新增 `<= 3 → return` | 同界（新） |
| property-9 寄存 | `0x6444AC cmp 3 / jbe` | 本轮新增 | 同界（新） |
| StorageAllBagItems | `0x64396A cmp 3 / jbe` | 本轮补门 | 同界（新） |

> 注意 `>3`(`ja`) 与 `>=4`(`jae` on 4) 在无符号 byte 上恒等，两种写法在原生并存，不构成分歧。

### 6.2 C# 其余消费点（原生对应待专项，本轮不改）

| C# 位置 | C# 界 | 备注 |
|---|---|---|
| `Command/BaseCommond.cs:70` | `== 4` | 原生 `0x6BB35B cmp 4/jb`、`0x712FE7 cmp 4/jb` 为候选，未逐一绑定 |
| `Command/Commands/UserMoveXYCommand.cs:28` | `>= 2` | 原生存在 `cmp 2` 档（`0x6BB44C jb`、`0x6BE6BD jne`、`0x6CE422 jb`），形态相符 |
| `Command/Commands/SearchHumanCommand.cs:25` | `< 3` | 原生 `0x657631 cmp 3/jb` 形态相符（该点在 `[[0x7D6D50]]` 按名查人后发 SysMsg 0xFFDB） |
| `Npcs/Merchant.cs:868` | `< 4` | 候选同 `BaseCommond` |
| `Npcs/CastleOfficial.cs:23` | `>= 3` | **未找到可绑定的原生 `+0x675` 读点**；`0x6F9040 cmp 3/ja` 属 TPlayObject 方法（读 `[+0x757]`/`[+0x760]`），非城堡官员 |
| `Actors/TBaseObject.cs:6668+`（约 16 处） | `> 9` && `boGMRunAll` | **全镜像 34 个 `+0x675` 读点中不存在阈值 9**。该判据在原生此字段上无对应；需专项确认是否另有来源（如缓存布尔或 config） |

> 上面两条标 **未绑定/无对应** 的项属于本任务边界之外的独立疑点，按铁律**不臆造、不修改**，如实记录待专项。

---

## 7. 顺带记录的分歧（本轮不动手）

**① `MarkNativeGoodsDirty()` 越界置位**
`ClientBuyItem`(`Merchant.cs:1953`) 与 `ClientSellItem`(`Merchant.cs:2161`) 都调了它，
但原生 `sub_63EB34` / `sub_63F200` **不写 `[+0x5D0]`**（脏标记仅有 §3 列出的 4 个置位点）。
接入分派器后 property-9 商人不再走这两个函数，非 property-9 商人的脏标记又被
`SaveNativeGoodsIfDue`/`FlushNativeGoods` 的 `HasNativePasProperty(9)` 门吞掉 ⇒ **已退化为惰性写**，
不可观测。删除会牵动既有审计文本约束，故保留并记录。

**② 数据日志的 `NeedIdentify` 门是 C# 追加的**
`Merchant.cs:1948` / `:2163` 用 `if (StdItem.NeedIdentify == 1)` 包住 `AddGameDataLog`。
原生 `0x63ECD7`(`sub_769934`) 与 `0x63F304`(`sub_768BE0`) **均无门**；`sub_768BE0`
（`0x768BE0`-`0x768C47`）逐字节确认是纯格式化+转发（`[ebx+0x12C]`/`[+0x130]`=x/y、`[+0x106]`=角色名、
`[+0x115]`=地图名 → `0x79D3D8`），内部也没有过滤。本轮新增的两个变体按原生**不加门**。

**③ `ClientBuyItem` 尾部 `WeightChanged` 缺失**
原生 `0x63ED8E-0x63ED91` 在成功回包后调 `0x73CEE4`；C# `ClientBuyItem` 尾部没有。
本轮新增的 `NativeStorageTakeItem` 按原生 `0x644434` **有**这一步。既有函数不在本任务范围。

**④ `ClientBuyItem` 的内外层退出语义**
原生 `0x63ED48`→`0x63ED50`：内层跑完即 `jmp` 函数尾，不回外层找下一组；C# 依赖 `bo29` 标志，
名字不匹配时会继续找。因组名唯一，实际不可观测。新增的 `NativeStorageTakeItem` 按原生
（`0x6443E6`/`0x6443EF`/`0x6443FB`）写成匹配后必然 `break` 外层。

**⑤ CM_USERBUYITEM 的前置门形态差异**
原生 `sub_6BAD98` 的门是 `[player+0x73]==0`、`[player+0x461]==0`、
`merchant == [player+0xCD8]`（当前对话 NPC）、`call 0x7743E0(cx=15)`；
C# `ClientUserBuyItem` 用的是 `m_boDealing` + `merchant.m_boBuy` + `m_PEnvir` + `|dx|>15||\|dy|>15`。
两者不同构（原生此处**没有** `m_boBuy` 测试）。属独立议题，未改。

---

## 8. 复现命令

```powershell
$py = "C:\Users\Administrator\AppData\Local\Programs\Python\Python311\python.exe"
# 反汇编（脚本见 %TEMP%\e17tool.py，flat 偏移 = VA - 0x400000）
& $py $env:TEMP\e17tool.py range 0x644244 0x644490    # sub_644244 全函数
& $py $env:TEMP\e17tool.py range 0x63EDE8 0x63EE14    # 买分派器
& $py $env:TEMP\e17tool.py range 0x63F35C 0x63F380    # 卖分派器
& $py $env:TEMP\e17tool.py range 0x644488 0x644568    # sub_644488
& $py $env:TEMP\e17tool.py calls 0x644244             # -> 仅 0x63EDFE
& $py $env:TEMP\e17tool.py calls 0x63EDE8             # -> 仅 0x6BAE21
& $py $env:TEMP\e17tool.py dref  0x644244             # -> 空（不在任何 VMT）
```

```powershell
cd D:\loym2\.claude\wt2\econ17
dotnet build GameSvr
dotnet run --project AuditTools\StorageAllBagItemsStaticCheck
```
