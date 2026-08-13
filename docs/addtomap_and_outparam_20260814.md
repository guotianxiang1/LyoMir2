# addtomap —— `AddToMap` 去重扫描 / 存活秒数入参 / `TList` 出参 / 三条 UNPROVEN 收口　2026-08-14

- 工作树：`D:\loym2\.claude\wt3\addtomap`　分支：`w/addtomap`　基线：`9ac74f9e`（master）
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`，file_off = VA − 0x400000）
- 前作：`docs/view_searchrange_predicate_20260814.md`（view56）、`docs/view_predicate_remaining_20260814.md`（view56b）
- 构建：`dotnet build GameSvr/GameSvr.csproj` → **0 错误 / 15 警告**（与基线逐条相同）
- 铁律：所有判定附 VA + 字节 + 反汇编；无原生字节证据一律 fail-closed
- 禁改文件本轮 diff 为空：`SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、
  `GameSvr/UsrSystem/UsrEngn.cs`

> **一句话**：`AddToMap` 的整条节点循环（去重续期 + 清道夫）已补回并接上谓词；存活秒数
> 入参已补，全镜像普查证明所有 C# 已建模的调用点原生都 `push 0`，唯一传变量的站点
> （`0x612B65`，TArenaRoom 阵型刷怪）在 C# 无落点、标 BLOCKED。`TList` 出参**裁决为不补**
> —— 它就是 `m_VisibleHumanList`，消费端早已落在 `TBaseObject.SendRefMsg`，而不是任务书
> 指的 `TBaseObject.SearchViewRange`；view56b 的对应体认定被本轮推翻。四条 UNPROVEN
> 收口三条（4414/4415 落点已定位并接线、`+0x2E9` 身份已证、`[0x7D6754]` 身份已证），
> `+0x2E3` 只证到「布尔隐身位 + 极性」，C# 字段名仍 UNPROVEN。

---

## 0. 提交

| SHA | 说明 |
|---|---|
| `9fae28eb` | `AddToMap`：补回 `sub_7776EC` 的去重/续期扫描 + 清道夫谓词 + 存活秒数入参 |
| `069242aa` | 4414 / 4415 就近查询：接上族 B「整段放弃」变体的有效性谓词 |
| `808b9e22` | `TList` 出参裁决 + 订正 `ViewRange.cs` 对应体注释（含一处后被撤回的并联） |
| `4fbc6684` | 撤回 `SendRefMsg` 重建循环的并联；两个夹具改为「有名对象 + 有名地图」 |
| （本文件） | 报告 |

---

## 1. `sub_7776EC` = `TEnvironment.AddToMap` 完整控制流

### 1.1 身份与签名

- **虚方法槽**：`TEnvironment` VMT 基址 `0x77477C`（vmtSelfPtr `[0x774730] = 0x77477C`），
  `[0x7747A4] = 0x007776EC` ⇒ **VMT+0x28**。另一处 `[0x612C98] = 0x007776EC` 是
  `TArenaRoom`（VMT `0x612C70`，vmtParent `[0x612C4C] = 0x774730` → `TEnvironment`）继承来的同一槽。
- **签名**：`0x777BC0 C2 08 00 ret 8` ⇒ 两个栈参。
  `AddToMap(Self=EAX, nX=EDX, nY=ECX, AObject=[ebp+0xC], nSeconds=[ebp+8]): TObject`
  （`0x77770C 8B 5D 08 mov ebx,[ebp+8]`、`0x777722 83 7D 0C 00 cmp [ebp+0xC],0`）。
- **原生没有独立的 btType 入参**：节点类型直接取自对象自己的类型字节
  `0x777A7F 8B 45 0C / 0x777A82 8A 40 04 mov al,[AObject+4] / 0x777A88 88 02 mov [node],al`。
  同一个 `+4` 在 `TDynEnvir.AddToMap`（`0x5FD65C`）里被拿来判 actor：
  `0x5FD69C 80 7E 04 01 cmp byte [esi+4],1` 之后 `0x5FD6AF 8D 96 06 01 00 00 lea edx,[esi+0x106]`
  取的正是 `CName`（`sub_765D64` 的同一个槽）⇒ **`AObject` 就是 actor 本体，`+4` 是它自带的
  格子类型标签**。C# 把它提成了显式 `btType` 参数，本轮沿用，未改。
- **格子解析** `sub_7776A8(Self=EAX, X=EDX, Y=ECX, [esp+4]=@cellPtr)`：只做越界检查
  （`0x7776BD 3B 50 3C cmp edx,[eax+0x3C]` / `0x7776C6 3B 48 40 cmp ecx,[eax+0x40]`），
  `0x7776CB 0F AF 50 40 imul edx,[eax+0x40]` / `0x7776D1 8D 14 49 lea edx,[ecx+ecx*2]` /
  `0x7776D7 8D 04 90 lea eax,[eax+edx*4]` ⇒ cell 记录 12 字节，`+8` 是链表头。
  **它不看格子属性** —— 这一点决定了节点循环排在属性闸之前。

### 1.2 十步控制流（逐段贴字节）

```
;--- 0 入口 / 空对象闸 ------------------------------------------------------
77770C  8B 5D 08              ebx := nSeconds
77771F  89 45 F0              [ebp-0x10] := 0                 ; Result := nil
777722  83 7D 0C 00 / 75 18   AObject = nil ?
777728  A1 CC 5E 7D 00 ...    -> 记 "[Exception]: TEnvironment.AddToMap Obj = "（0x777BCC）
77773B  E9 55 04 00 00        jmp 0x777B95                    ; 返回 nil

;--- 1 存活秒数 -> dwAddTime（[ebp-0x28]）----------------------------------
777740  C6 45 DF 00           bo1E := False
777744  85 DB / 7E 25         nSeconds <= 0 -> 0x77776D
777748  E8 F3 0B C9 FF        call 0x408340                   ; GetTickCount
77774D  69 D3 E8 03 00 00     imul edx,ebx,0x3E8              ; sec * 1000
777753  81 EA C0 27 09 00     sub  edx,0x927C0                ; − 600000
777759  03 C2 / 8B D8         ebx := now + sec*1000 − 600000
77775D  85 DB / 7E 05         有符号 <= 0 ?
777761  89 5D D8              [ebp-0x28] := ebx
777766  33 C0 / 89 45 D8      否则 [ebp-0x28] := 0
77776D  E8 CE 0B C9 FF        call 0x408340                   ; sec <= 0 分支
777772  89 45 D8              [ebp-0x28] := now

;--- 2 解析格子 -------------------------------------------------------------
777790  E8 13 FF FF FF        call 0x7776A8                   ; -> [ebp-0x14] = cell
777795  84 C0 / 0F 84 95 03.. je  0x777B32                    ; 越界 -> 返回 nil
77779D  8B 45 EC / 8B 40 08   Curr := cell^.head              ; [ebp-0x18]
7777A6  33 C0 / 89 45 E4      prev := nil                     ; [ebp-0x1C]
7777B9  83 7D E8 00 / 0F 84.. 空链 -> 0x777904

;--- 3 节点循环（去重续期 + 清道夫）---------------------------------------
7777C3  8B 45 E8 / 8B 40 0C   next := Curr^.Next              ; [ebp-0x20]
7777CC  33 DB                 bl := 0                         ; 「本轮已摘链」
7777CE  8B 45 E8 / 80 38 01   cmp byte [Curr],1               ; 战神 OS_MOVINGOBJECT
7777D4  0F 85 E7 00 00 00     jne 0x7778C1                    ; 非 actor -> 直接去重判定
7777DA  8B 45 E8 / 8B 70 04   esi := Curr^.POject
7777E0  85 F6 / 0F 84 02 01.. je  0x7778EA                    ; POject = nil -> 只跳过，不摘链
7777E8  8B C6
7777EA  E8 75 E5 FE FF        call 0x765D64                   ; ★ 有效性谓词
7777EF  84 C0 / 0F 85 A1 00.. jne 0x777898                    ; 有效 -> 去重判定
; 无效臂（族 A 摘链，与 CanWalk/GetMovObjCount/CreatureMoveTo 逐字节同形）
7777F7  83 7D E4 00 / 74 0B   prev = nil ?
7777FD  8B 45 E4 / 8B 55 E0 / 89 50 0C   prev^.Next := next
777808  8B 45 EC / 8B 55 E0 / 89 50 08   cell^.head := next
777811  B3 01                 bl := 1
777813  68 00 7C 77 00        push "[Exception]: TEnvironment.AddToMap Pt.POject.CName = 空 Pt = "
777891  E8 DE 66 02 00        call 0x79DF74                   ; 记异常
777896  EB 52                 jmp 0x7778EA                    ; continue，不是 break
; 去重续期臂（actor）
777898  8B 45 E8 / 8B 40 04   eax := Curr^.POject
77789E  3B 45 0C              cmp eax,[ebp+0xC]               ; 正是要加的这个对象？
7778A1  75 47                 jne 0x7778EA
7778A3  8B 45 E8 / 8B 55 D8
7778A9  89 50 08              mov [Curr+8],edx                ; 只刷 dwAddTime
7778AC-7778B9                 拆两层 SEH
7778BC  E9 D4 02 00 00        jmp 0x777B95                    ; 直接返回，不插入
; 去重续期臂（非 actor）—— 同一件事
7778C1  8B 45 E8 / 8B 40 04 / 3B 45 0C / 75 1E
7778D2  89 50 08              mov [Curr+8],edx
7778E5  E9 AB 02 00 00        jmp 0x777B95
; 尾部推进
7778EA  84 DB / 75 06         摘过链 -> prev 不前进
7778EE  8B 45 E8 / 89 45 E4   prev := Curr
7778F4  8B 45 E0 / 89 45 E8   Curr := next
7778FA  83 7D E8 00 / 0F 85 BF FE FF FF   非空 -> 回 0x7777C3

;--- 4 格子属性闸（C# 的 MapCellInfo.Valid 对应物，但多一条豁免）------------
777967  8B 45 EC / 80 38 00   cmp byte [cell],0               ; 属性 = 0（可走）？
77796D  74 16                 je  0x777985
77796F  8B 45 0C / 8B 15 D0 6E 71 00
777978  E8 AB CE C8 FF        call 0x404828                   ; @IsClass(AObject, [0x716ED0])
77797D  84 C0 / 0F 84 AD 01.. je  0x777B32                    ; 不是该类 -> 返回 nil
```
`[0x716ED0]` 是 VMT `0x716F1C` 的 vmtSelfPtr（`0x716F1C − 0x4C = 0x716ED0`），
classname 在 `[0x716F1C − 44]` ⇒ **`TFireworksEvent`**。`0x404828` 是 Delphi `@IsClass`
（`0x404836 call 0x4048C8` 沿 `[ecx-0x24]`= vmtParent 逐级比较）。
**即：非可走格只允许 `TFireworksEvent` 登记。**

```
;--- 5 金币合并扫描（C# 已有，形状不同，未改）-------------------------------
777993  8B 45 0C / 80 78 04 02   cmp byte [AObject+4],2       ; 战神 OS_ITEMOBJECT
77799F  E8 44 C9 00 00           call 0x7842E8                ; = cmp byte [eax+0x14],1 / sete al
7779A8  8B 45 EC / 8B 40 08      Curr := cell^.head（重新取）
7779BA  80 38 02                 cmp byte [node],2
7779CB  E8 18 C9 00 00           call 0x7842E8                ; 对已有节点同样判
7779D9  E8 12 D4 00 00           call 0x784DF0(eax=已有, edx=新) -> al
7779DE  88 45 DF                 bo1E := al
7779E7  89 5D F0                 Result := 已有节点的 POject

;--- 6 插入 -----------------------------------------------------------------
777A6C  80 7D DF 00 / 75 5D      bo1E -> 跳过插入
777A72  B8 10 00 00 00 / E8 24 B5 C8 FF   GetMem(16)          ; 节点 16 字节
777A7F-777A88                    node.CellType := byte [AObject+4]
777A8A-777A90                    node.POject   := AObject
777A93  8B 45 E8 / 8B 55 D8 / 89 50 08    node.dwAddTime := [ebp-0x28]
777A9C-777AAE                    头插：node.Next := cell.head；cell.head := node
777AB1  8B 45 0C / 8A 40 04 / FE C8 / 75 0E   byte [AObject+4] = 1（actor）？
777ABB  8B 4D E8 / 8B 55 0C / 8B 45 FC / 8B 18
777AC6  FF 53 04                 call [Self.VMT+4]            ; TDynEnvir.AddObject 0x5FD534
777AC9  8B 45 0C / 89 45 F0      Result := AObject

;--- 7 返回 -----------------------------------------------------------------
777BB7  8B 45 F0                 eax := Result
777BC0  C2 08 00                 ret 8
```

**三条返回值语义**（`[ebp-0x10]` 只在两处被赋值）：
| 路径 | 返回 |
|---|---|
| 正常插入（`0x777ACC`） | `AObject` |
| 金币合并（`0x7779E7`） | 被合并到的那个已有 item |
| **去重命中**（`0x7778BC` / `0x7778E5`） | **nil** |
| 越界 / 属性闸拒绝（`0x777B32`） | nil |

### 1.3 C# 侧落地（`GameSvr/Maps/Envirnoment.cs`）

新增两个私有方法 + 一个 5 参重载：

- `GetNativeAddToMapStamp(int nAliveSeconds)` —— §1.2 第 1 步逐条移植（两处 `jle` 都按有符号）。
- `ScanNativeAddToMapChain(MapCellinfo, object, int)` —— §1.2 第 3 步。命中返回 `true`，
  调用方立即 `return null`。O(链长)、零分配（无 LINQ / 无闭包 / 无装箱，
  `ReferenceEquals` 对全部载荷类型 `MapItem` / `TDoorInfo` / `Event` / `TBaseObject`
  都是引用比较，四者都是 class）。
- 插入路径的 `dwAddTime` 改用同一个 `[ebp-0x28]` 值（`0x777A96` / `0x777A99`），
  不再单独调一次 `GetTickCount`。

**两处刻意的取舍，都写在代码注释里：**

1. **摘链后不调 `ReleaseCellObjectList`**。族 A 的另外三处（`CanWalk` / `GetMovObjCount` /
   `MoveToMovingObjectCore`）摘空后会 `Release` + `break`，那是因为它们后面不再往这张表写。
   `AddToMap` 后面还要插入：`MapCellinfo` 是 struct，`Release` 把
   `MapCellObjectLists[index]` 置 null 之后，`MapCellInfo.ObjList` 仍指着那张已被
   `Clear` 的表，插入就会落进一张无人引用的表 —— 对象静默不落格。原生这里只改链指针、
   从不释放格子，所以「不 Release」既忠实又安全。
2. **节点循环放在 `MapCellInfo.Valid` 闸之前**，与原生同序（`sub_7776A8` 不看属性，
   属性闸在 `0x777967`）。非可走格上原生同样会跑这条循环；命中去重时两边都返回 nil，
   只是时间戳会被刷新 —— 严格更忠实，且不改任何调用方可见的返回值。

### 1.4 本函数还剩的两条分歧（**登记，未改**）

| # | 原生 | C# | 说明 |
|---|---|---|---|
| D-1 | `0x777967` 属性非 0 时允许 `AObject is TFireworksEvent` 登记 | `if (mapCell && MapCellInfo.Valid)` 一刀切 | C# 未建模 `TFireworksEvent`，补它要先定该类的落点 |
| D-2 | 无「同格 item 数 ≥ 5 就拒绝」的闸 | `if (!bo1E && MapCellInfo.Count >= 5) { result = null; bo1E = true; }` | C# 多出来的；本底本 `sub_7776EC` 全函数无此比较。可能来自另一版源码，需单独取证再动 |

---

## 2. 存活秒数入参：逐调用点取证表

### 2.1 参数语义（为什么是「秒」）

`dwAddTime := now + sec*1000 − 600000`（`0x77774D` / `0x777753` / `0x777759`）。
`0x927C0 = 600000` 正是地面物的过期阈值 —— `sub_77A178` 类型 2 臂：

```
77A3ED  8B 40 08              eax := node^.dwAddTime
77A3F0  3B 45 08 / 73 45      dwAddTime >= now -> 不过期
77A3FA  2B 50 08              edx := now − dwAddTime
77A3FD  81 FA C0 27 09 00     cmp edx,0x927C0
77A403  72 35                 jb 0x77A43A                     ; 未满 10 分钟 -> 正常处理
77A422  E8 69 A2 C8 FF        call 0x404690                   ; 过期：Free 对象
77A42E  E8 9D 8B C8 FF        call 0x402FD0（edx=0x10）        ;       Free 16 字节节点
```

即「倒填时间戳」= 让节点一出生就只剩 `sec` 秒寿命。**C# 完全没有这条 600000 ms 过期规则**
（全仓 `dwAddTime` 的消费只有 60 秒 actor 时限四处），所以本参数在 C# 目前是惰性的
——但签名与算式已按原生落地，等 600000 规则补上时立即生效。

### 2.2 调用点普查方法

全镜像扫 `FF /2 disp8 == 0x28`（`call dword ptr [reg+0x28]`），再用 Delphi 虚调用序言
过滤（要求紧邻前一条是 `8B /r mod=00` 且目的寄存器 = call 的基址寄存器）：167 命中；
再要求尾部 9 条指令内有 ≥2 个 `push`：60 命中；逐个看上下文剔除同槽异类
（`0x55xxxx` 一族是脚本插件的 `call [eax+0x54]` 邻居，`0x5BCB25` 是 3 个栈参，
`0x766BEE` / `0x5EF635` / `0x5139CB` / `0x5B1AA8` 无栈参）。

### 2.3 取证表

| # | 原生调用点 | 秒数入参（字节） | 所在上下文 | C# 落点 | 结论 |
|---|---|---|---|---|---|
| 1 | `0x5FD693`（唯一直接 `E8`） | 转发调用方的 `[ebp+8]`（`0x5FD68C 8B 4D 08 / 0x5FD68F 51`） | `TDynEnvir.AddToMap`（`0x5FD65C`）的 `inherited` | C# 把 `TDynEnvir.AddObject` 折进 `NativeDynEnvirAddObjectTrigger` | 透传，无独立取值 |
| 2 | `0x64F7CC` | `0x64F7BC 6A 00` | 收件人 `[esi+0x128]`，`push edi` = 对象 | — | **0** |
| 3 | `0x68F012` | `0x68EFFC 6A 00` | `[[ebx]+0x128]` | — | **0** |
| 4 | `0x6BD285` | `0x6BD275 6A 00` | `[ebx+0x128]`，`push ebx` | — | **0** |
| 5 | `0x6BD568` | `0x6BD552 6A 00` | 同上 | — | **0** |
| 6 | `0x6BD622` | `0x6BD613 6A 00` | 同上 | — | **0** |
| 7 | `0x717388` | `0x71737B 6A 00` | `[esi+0x1C]`（事件对象的 Envir） | `MapScriptEvt` / `Event` 一族 | **0** |
| 8 | `0x7173AD` | `0x7173A1 6A 00` | 同上 | 同上 | **0** |
| 9 | `0x7174AB` | `0x7174A0 6A 00` | `[eax+0x3C]/[eax+0x40]` 取 X/Y | 同上 | **0** |
| 10 | `0x717B2A` | `0x717B20 6A 00` | `[ebx+0x38]` = Envir，`+0x3C/+0x40` = X/Y | 同上 | **0** |
| 11 | `0x719BE7` | `0x719BDD 6A 00` | 同上（`TStallEvent` 一族） | 同上 | **0** |
| 12 | `0x71F0E2` | `0x71F0D2 6A 00` | `[ebx+0x128]` | — | **0** |
| 13 | `0x765134` | `0x76511E 6A 00` | `CanWalk`(`0x765114`) 成功后落格 | `TBaseObject.AddToMap()` 一族 | **0** |
| 14 | `0x765C0A` | `0x765BF4 6A 00` | `[esi+0x128]`，`push esi` | 同上 | **0** |
| 15 | `0x768934` | `0x768924 6A 00` | `[esi+0x128]` | 掉落/物品一族 | **0** |
| 16 | `0x768B1F` | `0x768B0F 6A 00` | `[ebx+0x128]`，`push esi` | 同上 | **0** |
| 17 | `0x768EC7` | `0x768EB1 6A 00` | `[ebx+0x128]` | 同上 | **0** |
| 18 | `0x768F41` | `0x768F2F 6A 00` | `[ebx+0x128]` | 同上 | **0** |
| 19 | **`0x612B65`** | **`0x612B4F 8B 43 18 mov eax,[ebx+0x18]` / `0x612B52 50 push eax`** | `sub_61268C` 阵型刷怪循环：`0x612B17 call 0x777EF8`(CanWalk) → `0x612B2A call 0x74DE54`（按名造怪） → `0x612B42 call 0x78389C` → AddToMap → `0x612B82 call [obj.VMT+0x2C]` | **无** | **BLOCKED**（见 §2.4） |

⇒ **除 #19 外，全部原生站点都传 0**，`0` 走 `0x77776D` 分支、`dwAddTime := GetTickCount()`
—— 与改前 C# 逐位相同。故不带秒数的 4 参重载**就是**「原生 `push 0`」这一档，不是随手糊的默认值；
它的存在同时是硬约束：`GameSvr/UsrSystem/UsrEngn.cs:3274 / :5008` 两个调用点在禁改文件里。

### 2.4 `0x612B65` 为什么 BLOCKED

- 所在函数 `sub_61268C`，**唯一调用点** `0x61208E`（在 `sub_611948` 里）。
- `sub_61268C` 是按方向 switch（`0x612709 FF 24 8D 10 27 61 00 jmp [ecx*4+0x612710]`，8 个分支）
  在 `[ebx+0x24]` 那张点位表上铺开刷怪的「阵型刷怪」；`[ebx+0x18]` 是每只怪的存活秒数，
  `[ebx+0x1C]` 是 Envir，`[ebx+0x28]` 是怪名，`[ebx+0x20]` bit1 写进 `[obj+0xFC]`。
- 单元归属：同段的 `TArenaRoom`（VMT `0x612C70`，`TEnvironment` 派生）。
- **C# 侧全仓无对应体**：`Arena/Leitai` 只出现在 GM 命令表
  （`NativeGmMoveLeitaiCommands.cs`）、`ReloadLeitaiBlockCommand.cs`、
  `Envirnoment.MapQuestTriggers.cs`，没有任何「按阵型批量刷怪 + 每只带存活秒数」的实现。
- ⇒ 无法「按原生传值」，标 **BLOCKED**；等 TArenaRoom 那条账被建模时一并接。

---

## 3. `SearchViewRange` 的 `TList` 出参 —— 裁决：**不补**

### 3.1 出参是什么

`sub_77A990(Self=EAX, nX=EDX, nY=ECX, searcher=[ebp+8], list=[ebp+0xC])`
（虚方法 **VMT+0x1C**：`[0x774798] = 0x77B330`，thunk `0x77B33C E8 4F F6 FF FF call 0x77A990`，
`0x77B343 C2 08 00 ret 8`）。收人臂：

```
77ABB5  80 BE 78 01 00 00 00  cmp byte [esi+0x178],0    ; m_btRaceServer = RC_PLAYOBJECT(0)?
77ABBC  75 10                 jne 0x77ABCE              ; 非玩家 -> 不收
77ABBE  80 7E 73 00 / 75 0A   cmp byte [esi+0x73],0     ; 幽灵 -> 不收
77ABC4  8B D6 / 8B 45 0C
77ABC9  E8 EA 9E CA FF        call 0x424AB8             ; ★ TList.Add(list, actor)
```
（`+0x178` = `m_btRaceServer` 已由 `Envirnoment.cs` 里 MOVE-34 那条注释坐实：
「`cmp byte [Cert+0x178],0` … 写 0x32」，而 `Grobal2.RC_PLAYOBJECT = 0`、`RC_ANIMAL = 50 = 0x32`。）

### 3.2 三个调用点全部传 `[self+0x380]`

| 调用点 | 所在函数 | 传入的 TList |
|---|---|---|
| `0x76528A` | `sub_7651EC` | `0x76526E 8B 83 80 03 00 00 / 0x765274 50` |
| `0x765451` | `sub_76533C` = `TCreature.SendRefMsg` | `0x765429 8B 80 80 03 00 00 / 0x76542F 50` |
| `0x76589D` | `sub_765790` = `TCreature.SendRefBuff` | `0x765875 8B 80 80 03 00 00 / 0x76587B 50` |

三处一律**先 Clear 再传**（`mov eax,[self+0x380] / mov edx,[eax] / FF 52 08 call [edx+8]`）。
**没有第四个调用点。** ⇒ 出参 = 搜索者自己的 `[self+0x380]` = `m_VisibleHumanList`。

### 3.3 消费端在 C# 早就存在 —— 在 `SendRefMsg`，不在 `SearchViewRange`

`TBaseObject.SendRefMsg`（`GameSvr/Actors/TBaseObject.cs`）里那段
`m_VisibleHumanList.Clear()` + 按 `nSendRefMsgRange` 扫格 + `m_VisibleHumanList.Add(BaseObject)`
的重建循环，**就是 `sub_77A990` 的内联体**。三条硬证据：

1. **半径同源**。`sub_77A990` 的扫描窗取自全局 `[[0x7D6754]]`
   （`0x77A9DD` / `0x77A9E7` / `0x77A9F2` / `0x77A9FF`），而该全局 = INI `[Setup] GlobalSeeZone`（见 §4.3），
   缺省 12 —— 正是 `g_Config.nSendRefMsgRange`。
   `TBaseObject.SearchViewRange` 用的是**每对象** `m_nViewRange`，不是这个全局。
2. **出参同源**。§3.2。`TBaseObject.SearchViewRange` 全程不碰 `m_VisibleHumanList`，
   它填的是 `m_VisibleActors`。
3. **臂数同源**。`sub_77A990` 节点循环只有 CellType 1 一条臂
   （`0x77AAEE 80 38 01` / `0x77AAF1 0F 85 1C 01 00 00`）。

⇒ **在 `TBaseObject.SearchViewRange` 上加一个 `TList` 出参，就会造出一个没人用的出参**
（任务书明令禁止）。裁决：**不补**。

### 3.4 对 view56b §A-5 的订正

view56b 判「`sub_77A990` 的 C# 对应体就是 `TBaseObject.SearchViewRange`」，依据是「臂数只有一条」。
臂数确实对得上，但那只是必要条件；半径来源与出参归属两条把它推翻了。正确的图景是
**一个原生函数在移植期被劈成了两半**：

| `sub_77A990` 的半边 | C# 落点 |
|---|---|
| `0x77ABC9 call 0x424AB8` 往 `[self+0x380]` 收人 | `TBaseObject.SendRefMsg` 的重建循环 |
| `0x77AC01 call [searcher.VMT+0x1BC]` 可见性刷新 | `TBaseObject.SearchViewRange`（非玩家版） |

两处因此**共用同一条节点循环**，所以摘链谓词（`0x77AB07`）在两处都成立。
本轮已把这段结论写进 `TBaseObject.ViewRange.cs` 的注释，订正原来的对应体说法。

`sub_77A178`（VMT+0x18，玩家版）交叉验证：全镜像只有一个真调用点 `0x6B6839`
（`0x6B6821 push esi` / `0x6B681D mov eax,[self+0x78]` seeZone / `0x6B6813 [self+0x130]`），
在一个 `TPlayObject` 方法里 ⇒ 对应 `TPlayObject.SearchViewRange`。

### 3.5 由此暴露、但**本轮未接**的一条（交主代理）

`SendRefMsg` 重建循环是 `sub_77A990` 的内联体 ⇒ 它那条摘链
（`(now − dwAddTime) >= 60s`）应当**并联**上 `IsNativeStaleCellActor`，
对应 `0x77AB07 call 0x765D64` / `0x77AB0C 84 C0 / 0F 85 A3 00 00 00 jne 0x77ABB5` /
`0x77AB14`–`0x77AB23` 摘链 / `0x77AB2E B3 01`。

这条**本轮接过又撤回**（见 §5）。撤回理由不是证据不足，而是影响面：
`HeroUnionStateCheck` 的失败点会从 1740 行提前到 721 行，因为它的观察者夹具是
**无名 `TPlayObject` + 无图名地图**，谓词判其失效后整条广播收不到。
这属于「夹具与生产现实的落差」（生产入图路径一律先命名、地图一律有图名），
不是谓词判错，但要一起改夹具，超出本任务范围。**建议主代理连同 §5 的两个 master 既有红一并处理。**

view56b §4.3 的「探针在消费侧不在重建侧」应据此修正：`sub_7651EC` 自己确实不调 `sub_765D64`，
但它委派的 `VMT+0x1C`（= `sub_77A990`）在 `0x77AB07` 调了 —— 重建侧同样有探针。

---

## 4. 四条 UNPROVEN 的收口

### 4.1 `0x6F4499` / `0x6F4882` —— **已定位并接线**

**函数身份**（view56b 记为「AI 选靶」，实为客户端就近查询）：
两者都是 `sub_6D7D68` 大 switch 的消息处理器。跳表基址 `0x6D8867`
（`0x6D8860 FF 24 85 67 88 6D 00 jmp dword ptr [eax*4+0x6D8867]`），
索引式 `0x6D8852 05 C5 EE FF FF add eax,-0x113B` / `0x6D8857 83 F8 33 cmp eax,0x33`：

| 表项 VA | 值 | idx | ident | 常量 | 处理器 |
|---|---|---|---|---|---|
| `0x6D8873` | `0x6DB19A` | 3 | `0x113E` = 4414 | `CM_QUERY_NEARBYPLAYER` | `0x6DB1A8 E8 E3 95 01 00 call 0x6F4790` |
| `0x6D8877` | `0x6DB1B2` | 4 | `0x113F` = 4415 | `CM_QUERY_NEARBYGROUP` | `0x6DB1B5 E8 0E 92 01 00 call 0x6F43C8` |

**C# 落点**：`GameSvr/Players/TPlayObject.NativeGroupProtocol.cs`
→ `HandleNativeNearbyPlayerQuery`（4414）/ `HandleNativeNearbyGroupQuery`（4415）
（该文件里的记录布局注释本来就已经引了 `sub_6F4790` / `sub_6F43C8` 的尾部，只是没人把
两者与这两条谓词对上）。

**接线**（族 B「整段放弃」变体：`je` 而非 `jne`，无日志、无删表）：

`sub_6F4790`（4414，遍历请求体里的 16 字节名字记录）：
```
6F4824  8D 55 E0 / 8B 45 FC / 03 C6 / B9 10 00 00 00 / E8 2A EA D0 FF   取 16 字节 ShortString
6F483C  E8 33 0F D1 FF        call 0x405774            ; @LStrFromString
6F4844  A1 50 6D 7D 00        eax := [[0x7D6D50]]      ; UserEngine 单例
6F484B  E8 34 DF F5 FF        call 0x652784            ; GetPlayObject(name)
6F4854  0F 84 09 01 00 00     je  0x6F4963             ; nil -> 放弃候选
6F485A  80 7B 73 00 / 0F 85.. 幽灵 -> 放弃
6F4866  E8 4D E6 07 00        call 0x772EB8 / jne 放弃
6F4873  80 BB E3 02 00 00 00  cmp byte [ebx+0x2E3],0 / jne 放弃
6F4882  E8 DD 14 07 00        call 0x765D64            ; ★
6F4889  0F 84 D4 00 00 00     je  0x6F4963             ; 无效 -> 放弃候选
6F4893  E8 F0 F9 07 00        call 0x774288            ; 潜行/隐身
6F48A0  8B 87 28 01 00 00 / 3B 83 28 01 00 00 / 74 15  ; 同图才收
```
`sub_6F43C8`（4415，遍历 `[self+0x380]` —— 与 C# 这条循环同源）：
```
6F442F  8B 80 80 03 00 00     eax := [self+0x380]
6F4435  8B 70 08              esi := Count
6F4455  E8 F2 08 D3 FF        call 0x424D4C            ; TList.Get(idx)
6F445E  0F 84 BC 01 00 00     je  0x6F4620             ; nil -> 放弃
6F4464  80 BB 78 01 00 00 00  cmp byte [ebx+0x178],0 / jne 放弃   ; 非玩家
6F4471  80 7B 73 00 / 0F 85.. 幽灵 -> 放弃
6F447D  E8 36 EA 07 00        call 0x772EB8 / jne 放弃
6F448A  80 BB E3 02 00 00 00  cmp byte [ebx+0x2E3],0 / jne 放弃
6F4499  E8 C6 18 07 00        call 0x765D64            ; ★
6F44A0  0F 84 7A 01 00 00     je  0x6F4620             ; 无效 -> 放弃候选
6F44AB  E8 D8 FD 07 00        call 0x774288            ; 潜行/隐身
```
C# 侧各加一条 `!IsNativeCellObjectValid(...)` 到既有的短路链里。两条链上全是纯谓词、
无副作用，位置先后不影响可观察行为。

**顺带登记（未改）**：两处原生的过滤集是
`{+0x73 幽灵, sub_772EB8, +0x2E3, sub_765D64, sub_774288(潜行), 同图}`，
C# 现有的是 `{IsNativeGroupRestricted（坐骑闸）, 同图}` —— 这两套并不重合。
不属本任务，登记待查。

### 4.2 `+0x2E3` / `+0x2E9` 身份

**`+0x2E9` —— 已取证：「本对象被某个玩家看见过」的一次性闩。**

- **置位方**：只有观察者侧的可见性过滤会写 1 ——
  `0x6E2282 C6 80 E9 02 00 00 01 mov byte [obj+0x2E9],1`
  （在 `[TPlayObject.VMT+0x1BC] = 0x6E21F8` 里，紧跟幽灵闸
  `0x6E2271 80 78 73 00 cmp byte [eax+0x73],0` / `0x6E2275 jne 0x6E2721` 之后）。
  另有 `0x687D97` / `0x6AD897` / `0x76C5A5` 三处也写 1。
- **读方 = 广播总闸**。`sub_76533C`（`TCreature.SendRefMsg`）开头：
  ```
  7653B1  83 B8 80 03 00 00 00  cmp dword [self+0x380],0 / je 0x7653EA
  7653BD  E8 F6 DA 00 00        call 0x772EB8            / jne 0x7653EA
  7653C9  80 B8 E3 02 00 00 00  cmp byte [self+0x2E3],0  / jne 0x7653EA
  7653D5  83 B8 28 01 00 00 00  cmp dword [self+0x128],0 / je 0x7653EA
  7653E1  80 B8 E9 02 00 00 00  cmp byte [self+0x2E9],0
  7653E8  75 0D                 jne 0x7653F7             ; ★ 非零才广播
  7653EA  ...                   -> 直接返回，一条消息都不发
  ```
  `sub_765790`（`SendRefBuff`）在 `0x76582D` 同形；`sub_7651EC` 在
  `0x76523B 80 BB E9 02 00 00 00 cmp byte [ebx+0x2E9],0 / 0x765242 je 0x765334` 同形。
- **清零方**：`0x765536 C6 80 E9 02 00 00 00`（SendRefMsg 尾）、
  `0x765986`（SendRefBuff 尾）、`0x76532D`。
- ⇒ 语义确定：**没被任何玩家看见的对象整条广播直接短路**，是一条广播抑制闩。
  **C# 完全没有建模**（`SendRefMsg` 无此闸），登记。

**`+0x2E3` —— 证到「布尔隐身位 + 极性」，C# 字段名仍 UNPROVEN。**

- 极性（订正 view56b §6.2 的口误）：`0x6E2289 cmp byte [obj+0x2E3],0` / `0x6E2290 74 0D je 0x6E229F`,
  `0x6E229F` 才是继续往下走的正常路径，`0x6E2292 33 C0` + 拆 SEH + `0x6E229A jmp 0x6E27E6`
  是返回 False。**即「零 = 可见，非零 = 不可见」**，与其余五处 `jne <放弃>` 一致。
- 读点共 5 类：`sub_765D94` 死/幽灵族（`0x765DB7`，非零算「不可交互」）、
  广播总闸（`0x7653C9` / `0x765815` / `0x765221`）、可见性过滤（`0x6E2289`）、
  就近查询（`0x6F448A` / `0x6F4873`）、`0x7789C4` / `0x778BB6`。
- 写点约 30 处，其中一对最干净的 getter/setter 式小方法：
  ```
  64F44C  55 8B EC / C6 80 E3 02 00 00 01 / 33 D2 / E8 57 8D 11 00 (0x7681B4) / 5D C3   ; := True
  64F460  55 8B EC / C6 80 E3 02 00 00 00 /        E8 6D 67 11 00 (0x765BDC) / 5D C3   ; := False
  ```
  以及构造期就置 1 的 `0x71C57D`（同时 `0x71C576 mov byte [esi+0x5F0],1`、
  `0x71C589 mov [esi+0x5F4],GetTickCount()`）。
- **相邻槽**：`sub_772EB8 = ([+0x2E2] <> 0) || HasState(0x3C)`
  （`0x772EBE 80 BB E2 02 00 00 00` / `0x772EC7 B2 3C mov dl,0x3C` / `0x772ECB call 0x772960`）。
  广播总闸是 `sub_772EB8(self) || [self+0x2E3]`，与 C# `SendRefMsg` 的
  `m_boObMode || m_boFixedHideMode` 形状一致 ⇒ **`+0x2E2`/state 0x3C ↔ `m_boObMode`，
  `+0x2E3` ↔ `m_boFixedHideMode`** 是最贴的候选。
- 但 `m_boInvisible` 也没有已知的原生槽，二者无法靠现有证据区分 ⇒
  **字段名判定 UNPROVEN，本轮不接线。**

### 4.3 `[0x7D6754]` 解引用出的 int —— **已取证**

`[0x7D6754]` 是一个指针型全局，指向的记录第 0 个 dword 就是那个半径。
配置读取点（`0x794460`–`0x7944D0`）：

```
794480  B9 24 4A 79 00        ecx := 0x794A24
794485  BA 88 47 79 00        edx := 0x794788
79448C  E8 B7 8B CB FF        call 0x44D048          ; ValueExists(Section, Ident)
794491  84 C0 / 74 1D         不存在 -> 0x7944B2
794495  6A 0C                 push 0xC               ; ★ 默认值 12
794497  B9 24 4A 79 00 / BA 88 47 79 00 / 8B C3 / 8B 30
7944A5  FF 56 08              call [Self.VMT+8]      ; ReadInteger
7944A8  8B 15 54 67 7D 00 / 89 02   [[0x7D6754]] := eax
7944B2  A1 54 67 7D 00
7944B7  C7 00 0C 00 00 00     [[0x7D6754]] := 12     ; ★ 缺省
7944BD  6A 0C / ... FF 56 0C  WriteInteger(..., 12)  ; 回写 INI
```
Delphi 长串（`[addr-4]` 长度前缀 + GBK）：
`0x794788` len=5 `'Setup'`、`0x794A24` len=13 `'GlobalSeeZone'`。

⇒ **`[[0x7D6754]]` = INI `[Setup] GlobalSeeZone`，缺省 12。**
C# 已建模：`GameSvr/Configs/GameSvrConfig.cs:359 nSendRefMsgRange`，
`:1219 nSendRefMsgRange = 12`，`TBaseObject.SendRefMsg` 用 `m_nCurrX ± nSendRefMsgRange`
夹取扫描窗 —— 与 `sub_77A990` 的 `0x77A9DD`–`0x77AA09` 逐条对应。**无需改动。**

另两个读点顺带登记：`0x6AD7C7 cmp dword [[0x7D6754]],0xC / jl -> [edi+0x78] := 12`
（每对象视野下限 12），`0x71F9BD cmp eax,[ebx+0x78] / jl -> 跳过`。

---

## 5. 回归验证（跨 worktree 共享 exe 陷阱已规避）

方法：`dotnet build <proj>.csproj -o <独立临时目录>` 后**跑该目录里的 exe**；
异常堆栈的源码路径逐条核对过是对应的树。基线树 `D:\loym2\.claude\wt3\atm_base`
（detached @ `9ac74f9e`）与本树逐个对跑。

- `dotnet build GameSvr/GameSvr.csproj` → **0 错误 / 15 警告**，与基线逐条相同。
- 30 个涉及 `AddToMap` 的 AuditTool，退出码与失败行号 NEW 与 BASE 全部一致或更好：
  - `ExactEnvironmentRemoveEverywhereCheck`：BASE PASS → NEW PASS（夹具已订正，见下）
  - `HeroUnionStateCheck`：NEW 与 BASE 同停在 1740 行（权限文案，与本任务无关）
  - 其余 PASS 的仍 PASS，其余 FAIL 的行号一字不差。

### 5.1 两个夹具订正（都是「夹具不符合生产现实」，不是行为回归）

| 夹具 | 症状 | 订正 |
|---|---|---|
| `ExactEnvironmentRemoveEverywhereCheck` | 构造**同格**重复登记（`AddDuplicate(actor,2,2)` / `(player,1,1)`），战神里不可能出现 | 改成跨格（`3,3` / `2,2`）；`NewActor` 补 `m_sCharName` |
| `HeroUnionStateCheck` | 「同格第二个目标被跳过」用的两个探针都无名、地图 `sMapName` 为空 | `CreateNativeUnionCombatEnvironment` 补 `sMapName`；两个探针工厂补 `m_sCharName` |

### 5.2 master 上**已经**红着的两个（非本轮引入，交主代理）

在基线树 `9ac74f9e` 上复现确认：

| AuditTool | 失败点 | 根因 |
|---|---|---|
| `MovementCollisionCheck` | 25 行 `players did not block walking` | view56b `c392b391` 给 `Envirnoment.CanWalk` 接的族 A 谓词，遇上夹具里**无名**的 `TBaseObject`（`NewObject` 不设 `m_sCharName`）+ 无图名地图，占位者被当悬挂项摘链 |
| `NativeMoveGateCheck` | 89 行 | 同类，`GetNativeMovObjCount` 那处 |

两者与本轮 `AddToMap` 的接线同源：**族 A 谓词一旦接进热路径，所有用「无名对象 / 无图名地图」
搭的 in-proc 夹具都会失真。** 建议主代理定一条统一口径（要么全量给夹具补名字，
要么给谓词加一个「仅在生产配置下生效」的判据），否则每接一处就红一批。

---

## 6. 方法与可复现

- 反汇编：仓内 `tools/m2_disasm.py`（capstone x86-32，`off = VA − 0x400000`）。
- 临时脚本置于 `%TEMP%`，未入库：
  - `atm_dis.py`：**对齐窗口反汇编**（在 `[start−64, start)` 里逐字节试起点，
    取第一个能让指令流正好落在锚点上的起点，解决 Delphi 代码段线性反汇编错位）。
  - `atm_callers.py`：`E8/E9 rel32` 目标匹配普查（不用指针跟踪，不会漏）。
  - `atm_dwref.py`：全镜像 dword 值匹配（找 VMT 槽、跳表项）。
  - `atm_vmt.py`：VA 区间 dword 转储 + `vmtSelfPtr`（`[vmt−0x4C] == vmt`）识别 VMT 基址。
  - `atm_cls.py`：Delphi 类名（`[vmt−44]` → ShortString）+ 长串（`[addr−4]` 长度前缀，GBK）。
  - `atm_vcall.py` / `atm_v28sum.py`：`call [reg+disp]` 虚调用普查 + 序言过滤 + 尾部 push 摘要。
  - `atm_fnstart.py`：函数入口反推（全镜像 `E8` 目标集合 ∩ `[va−0x4000, va]` 取最大者）。
  - `atm_fld.py`：按位移扫字段读写点。
- **函数命名一律不靠猜**：从失败臂的 `push imm32` 取 Delphi 长串，`[addr−4]` 校长度、
  `[addr−8]` refcount = −1 校形状，串里就是作者写的函数名。
- **消息 ident 不靠猜**：从 `jmp [reg*4+base]` 跳表反推索引，再加 `add eax,-imm` 的基数。
- PowerShell 无 heredoc，提交信息一律 `git commit -F <临时文件>`。
