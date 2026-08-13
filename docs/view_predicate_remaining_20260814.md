# SPWN-56 续 —— `sub_765D64` 剩余调用点的接线与裁决　2026-08-14

- 工作树：`D:\loym2\.claude\wt3\view56b`　分支：`w/view56b`　基线：`c777db2a`（master，含前作 view56）
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`，file_off = VA − 0x400000）
- 前作：`docs/view_searchrange_predicate_20260814.md`（`sub_765D64` 完整语义 + 13 个调用点普查）
- 铁律：所有判定附 VA + 字节 + 反汇编；无原生字节证据一律 fail-closed
- 构建：`dotnet build GameSvr/GameSvr.csproj` → **0 错误 / 15 警告**（与基线逐条相同，无新增）

> **一句话**：族 A 六个站点里，四个（`CanWalk`、`GetMovObjCount`、`CreatureMoveTo`
> 以及 `DoSearchTargetList`）本轮全部落地或确认已覆盖；`AddToMap` **不能接**——C# 那个函数
> 根本没有原生的那条扫描循环，接了就是自造。族 B 的三个名字在 C# 里**合流成一个**
> `TBaseObject.SendRefMsg`，已按「只跳过、不删表项」接线，**完全没有碰禁改文件**。
> MOVE-31 裁决：应接，且已接。

---

## 0. 提交

| SHA | 说明 |
|---|---|
| `c392b391` | 族 A：`CanWalk`(0x778030) / `GetMovObjCount`(0x7788F9) / `CreatureMoveTo`(0x7798C0) 三处摘链谓词接线；`IsNativeStaleCellActor` 提升为 `public` |
| `0bd64847` | 族 B：`TBaseObject.SendRefMsg` 的 `m_VisibleHumanList` 消费循环加「只跳过、不删表项」门；顺带订正 `TBaseObject.SearchViewRange` 的原生 VA 引用 |
| （本文件） | 报告 |

**0 触碰禁改文件**：`SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、
`GameSvr/UsrSystem/UsrEngn.cs` 三个文件本轮 diff 为空。

---

## 1. 族 A 六个站点的原生形状：逐字节同形，已核

先把六处的**函数身份**用作者自己写的诊断串坐实（`[addr-4]` 长度前缀 + GBK）：

| 函数入口 | 谓词调用点 | 失败臂拼的串（VA / 内容） |
|---|---|---|
| `sub_7776EC` | `0x7777EA` | `0x777C00` `[Exception]: TEnvironment.AddToMap Pt.POject.CName = 空 Pt = ` |
| `sub_777EF8` | `0x778030` | `0x7781E8` `[Exception]: TEnvironment.CanWalk Pt.POject.CName = 空 Pt = ` |
| `sub_778858` | `0x7788F9` | `0x778A7C` `[Exception]: TEnvironment.GetMovObjCount Pt.POject.CName = 空 Pt = ` |
| `sub_7797CC` | `0x7798C0` | `0x779BB4` `[Exception]: TEnvironment.CreatureMoveTo Curr.POject.CName = 空 Curr = ` |
| `sub_77A178` | `0x77A2EB` | `0x77A81C` `[Exception]: TEnvironment.DoPlayerSearchViewRange Curr^.POject.CName = 空 Curr = ` |
| `sub_77A990` | `0x77AB07` | `0x77AD00` `[Exception]: TEnvironment.DoSearchTargetList Pt.POject.CName = 空 Pt = ` |

函数入口用「全镜像 `E8` 目标集合 ∩ [va−0x4000, va] 取最大者」反推，不用 `55 8B EC` 序言回溯。

### 1.1 摘链臂形状（五处逐条对齐，字节全同）

以 `CanWalk` 为样板，其余四处只有 disp 与跳转距离不同：

```
778014  8B 45 E8 / 80 38 01     eax := node; cmp byte [eax],1     ; CellType = OS_MOVINGOBJECT ?
77801A  0F 85 D1 00 00 00       jne 0x7780F1                      ; 非 actor -> 直接下一节点
778020  8B 45 E8 / 8B 70 04     esi := node^.POject               ; node+4
778026  85 F6 / 0F 84 C3 00..   test esi,esi / je 0x7780F1        ; POject = nil -> 只跳过，不摘链
77802E  8B C6
778030  E8 2F DD FE FF          call 0x765D64                     ; ★ 有效性谓词
778035  84 C0
778037  0F 85 A1 00 00 00       jne 0x7780DE                      ; 有效 -> 正常臂
; ---- 无效臂 ----
77803D  83 7D E4 00 / 74 0B     prev = nil ?
778043  8B 45 E4 / 8B 55 E0 / 89 50 0C    prev^.Next := saved_next
77804C  EB 09
77804E  8B 45 EC / 8B 55 E0 / 89 50 08    cell^.head := saved_next
778057  B3 01                   bl := 1                           ; 抑制 prev 前进
778059  68 E8 81 77 00 ...      拼诊断串 -> call 0x79DF74（记异常）
        ...                     jmp 尾部                          ; ★ continue，不是 break
; ---- 尾部（以 AddToMap 的 0x7778EA 为例，五处同形）----
7778EA  84 DB / 75 06           test bl,bl / jne（摘过链 -> prev 不前进）
7778EE  8B 45 E8 / 89 45 E4     prev := Curr
7778F4  8B 45 E0 / 89 45 E8     Curr := saved_next
7778FA  83 7D E8 00 / 0F 85 ..  非空 -> 回节点循环头
```

五处的 `bl := 1` 都实测为 `B3 01`：`0x777811` / `0x778057` / `0x778920` / `0x7798E7` / `0x77AB2E`；
五处的循环尾部都实测为 `84 DB / 75 06`：`0x7778EA` / `0x7780F1` / `0x7789D9` / `0x7799AC` / `0x77AC13`。

| 站点 | 有效臂 | 摘链段 | `bl := 1` | 诊断串 push |
|---|---|---|---|---|
| `0x7777EA` AddToMap | `jne 0x777898` | `0x7777F7`–`0x777806` | `0x777811` | `0x777813 push 0x777C00` |
| `0x778030` CanWalk | `jne 0x7780DE` | `0x77803D`–`0x77804C` | `0x778057` | `0x778059 push 0x7781E8` |
| `0x7788F9` GetMovObjCount | `jne 0x7789A6` | `0x778906`–`0x778915` | `0x778920` | `0x778922 push 0x778A7C` |
| `0x7798C0` CreatureMoveTo | `jne 0x779998` | `0x7798CD`–`0x7798DC` | `0x7798E7` | `0x7798E9 push 0x779BB4` |
| `0x77AB07` DoSearchTargetList | `jne 0x77ABB5` | `0x77AB14`–`0x77AB23` | `0x77AB2E` | `0x77AB30 push 0x77AD00` |

结论：**族 A 五个站点与已接的 `0x77A2EB` 完全同族、同处置**（摘链 + 抑制 prev 前进 +
异常日志 + continue）。

---

## 2. 逐处判定与接线结果

### A-1 `TEnvironment.AddToMap`（`0x7777EA`，`sub_7776EC`）—— **不接，fail-closed**

原生这条循环**不是**一条单纯的清道夫扫描，它是一条**去重/续期扫描**：

```
777898  8B 45 E8 / 8B 40 04     eax := node^.POject
77789E  3B 45 0C                cmp eax,[ebp+0xC]        ; 是不是正在加的这个对象？
7778A1  75 47                   jne 0x7778EA             ; 不是 -> 下一节点
7778A3  8B 45 E8 / 8B 55 D8     eax := node; edx := 新算出的 dwAddTime
7778A9  89 50 08                mov [node+8],edx         ; 只刷时间戳
7778AC-7778BC                   拆两层 SEH 后 jmp 0x777B95 —— 直接返回，不再插入
```

（非 actor 节点走 `0x7778C1`，做的是同一件事。）

**C# `Envirnoment.AddToMap`（`GameSvr/Maps/Envirnoment.cs:198`）里根本没有这条扫描**：
它只在 `btType == OS_ITEMOBJECT` 且是金币时扫一遍做堆叠合并，`OS_MOVINGOBJECT`
直接 `MapCellInfo.ObjList.Add(OSObject)`。**没有可挂谓词的循环**，硬加一条就等于凭空
造出一段原生语义的搬运，超出「只接谓词」的范围。**故不接。**

顺带两条分歧登记（都不属本条，交主代理判）：

1. **缺去重**：原生同一格里同一对象只会有一个节点（找到就刷时间戳返回）；C# 无条件
   `Add`，重复 `AddToMap` 会在同一格留下多份登记。
2. **缺存活秒数入参**：原生 `AddToMap` 的最后一个栈参是「存活秒数」，
   `0x777748 call 0x408340`（GetTickCount）/ `0x77774D 69 D3 E8 03 00 00 imul edx,ebx,0x3E8`
   / `0x777753 81 EA C0 27 09 00 sub edx,0x927C0`（600000）/ `0x777759 03 C2` ——
   即 `dwAddTime := now + sec*1000 − 600000`，用倒填时间戳的办法给节点定寿命；
   秒数 ≤ 0 时退化为 `dwAddTime := now`（`0x77776D`）。C# 的 `AddToMap` 没有这个参数。

### A-2 `TEnvironment.CanWalk`（`0x778030`，`sub_777EF8`）—— **已接**

- C# 落点：`GameSvr/Maps/Envirnoment.cs` → `Envirnoment.CanWalk(int nX, int nY, bool boFlag)`
- 族别：**族 A（摘链）**
- 循环由 `for` 改为手工下标 `while`，摘链后 `continue` 不自增 —— 对应原生尾部
  `0x7780F1 84 DB test bl,bl / 0x7780F3 75 06 jne 0x7780FB` 的「prev 不前进」。
- `Count` 归零时 `ReleaseCellObjectList(nX, nY)` 后 `break`，沿用本文件既有惯用法
  （`DeleteFromMap`、`MoveToMovingObjectCore` 都是这个形状）。

### A-3 `TEnvironment.GetMovObjCount`（`0x7788F9`，`sub_778858`）—— **已接**

- C# 落点：`Envirnoment.GetNativeMovObjCount(int nX, int nY)`（该方法的 XML 注释本来就
  已把自己标成 `sub_778858`，且已写明「drops entries whose actor fails the liveness probe
  sub_765D64 (logging each one)」—— 注释早就描述了这件事，只是代码没做。本轮补上代码。）
- 族别：**族 A（摘链）**

### A-4 `TEnvironment.CreatureMoveTo`（`0x7798C0`，`sub_7797CC`）—— **已接**，MOVE-31 裁决见 §3

- C# 落点：`Envirnoment.MoveToMovingObjectCore` 的**占位扫描**循环（非 run 路径）。
- 定位依据：原生 `0x779870 cmp byte [ebp+8],0 / 0x779874 jne 0x7799C6` 是 `boFlag`
  短路，跳过的正是这个循环；`0x779881` 取的是**目标格**表头（`[ebp-0x20]`）。
  与 C# `if (!boFlag && mapCell) { ... }` 里那段 `IsNativeCellBlocking()` 扫描一一对应。
  有效臂 `0x779998 mov eax,[node+4] / 8B 10 / FF 12 call [POject.VMT+0]`，返回真则
  `0x7799A6 mov byte [ebp-0xA],0`（bo1A := false）并 `jmp 0x7799C6` 跳出 —— 与 C# 的
  `bo1A = false; break;` 逐条对应。
- 族别：**族 A（摘链）**
- `useRunRules`（`MoveToMovingObjectForRun` / `HasRunBlockingObject`）**没接**：原生只有
  `sub_7797CC` 一个 mover，run 规则是移植期分出来的另一条路径，没有对应的原生站点。

### A-5 `TEnvironment.DoSearchTargetList`（`0x77AB07`，`sub_77A990`）—— **已被 view56 覆盖**，本轮订正 VA 引用

前作把它列为「未接」，实际上**它的 C# 对应体就是 view56 已经接过的
`TBaseObject.SearchViewRange`（基类非玩家版）**，只是注释里引的 VA 写成了玩家版的
`0x77A2EB`。判定依据是节点循环的**臂数**：

| | 节点循环分支 | C# 对应 |
|---|---|---|
| `sub_77A178`（玩家版） | `0x77A2BD` 1 → `0x77A2D6`；`0x77A2C1` 2 → `0x77A3D9`；`0x77A2C9` 3 → `0x77A480`；其它 → `0x77A65A`。**三条臂** | `TPlayObject.SearchViewRange` |
| `sub_77A990`（非玩家版） | `0x77AAEE 80 38 01 cmp byte [node],1` / `0x77AAF1 0F 85 1C 01 00 00 jne 0x77AC13`。**只有 CellType 1 一条臂，没有地面物 / 事件臂** | `TBaseObject.SearchViewRange`（同样只处理 `OS_MOVINGOBJECT`） |

`sub_77A990` 另两条特征也对得上非玩家路径：`0x77A9DD` 起用全局
`[0x7D6754]` 解引用出的搜索半径夹取扫描窗（不是每对象的 `m_nViewRange`），
`0x77ABB5 cmp byte [esi+0x178],0 / jne` 只把**非 creature**（玩家）收进
`[ebp+0xC]` 那个 `TList`，然后 `0x77AC01 call [searcher.VMT+0x1BC]` 走可见性。
两个函数都挂在 `TEnvironment` 的 VMT 相邻槽上（`0x774794 → 0x77B314 → sub_77A178`，
`0x774798 → 0x77B330 → sub_77A990`，两个都是 `push ebp / mov ebp,esp / push 参数 / call / ret n` 的虚方法 thunk）。

**本轮改动仅为注释订正**（把该方法的原生对应体和 VA 写准），代码行为不变。

> 遗留（登记，不属本条）：`sub_77A990` 会把扫到的玩家收进调用方传入的 `TList`
> （`0x77ABC9 call 0x424AB8`），C# 的 `TBaseObject.SearchViewRange` 没有这个出参。

---

## 3. `CreatureMoveTo` / MOVE-31 裁决

**裁决：旧结论的前提已被推翻一半，应当接，本轮已接（族 A 摘链写法）。**

- 旧账本（`docs/move_misc_residual_20260814.md`）MOVE-31 判「可观测等价、维持不改」，
  依据是三项合取在 C# **不可达**。
- 前作已证：`TBaseObject.cs:14 public string m_sCharName;` **无初值 ⇒ 默认 `null`**，
  所以合取只是「**实践上**不可达」（所有已知入图路径都先命名后 `AddToMap`），
  不是「**结构上**不可达」。本轮全仓复核维持这一结论：`m_PEnvir = null` 全仓只有
  `TBaseObject.cs:322` 一处字段初值；`Envirnoment.sMapName` 默认 `string.Empty`
  且 `Envirnoment.cs:94` 会置空。
- 但「实践上不可达」**恰恰就是原生的处境**——前作的全镜像扫描证明原生也从不主动清
  这三个槽（`mov byte [reg+0x106],imm` 0 命中；`[reg+0x128]` 的 25 处写点无一写 0）。
  这条谓词在原生里同样是**平时恒真、只在链表出现悬挂/半构造项时才响**的探针。
- 因此「因为平时不响所以不用接」这个推理，对原生和 C# 是同一句话，不构成不接的理由；
  真正的判据是**响的时候两边行为是否一致**，而现在不一致（原生摘链 + 跳过，C# 拿它当活体
  参与占位判定）。
- 接法与已接的四处同源：谓词为假 ⇒ 摘链 + 跳过。它**不可能**摘掉一个「有名 + 已入图 +
  图名非空」的活体，所以是单调的：只减少分歧，不引入新分歧。

**注意：这里没有「并联」可言。** 主代理交待的并联（`age>=60s || IsNativeStaleCellActor(...)`）
是针对 `SearchViewRange` 那四处——那里存在一条移植期自造的 60 秒时限，替换会砍掉托管侧
孤儿格子项的唯一回收通道。族 A 这三处（`CanWalk` / `GetMovObjCount` / `CreatureMoveTo`）
**原本一条摘链条件都没有**，谓词是**新增**的唯一条件，不存在被替换掉的东西。
四处已接的并联写法本轮**一个字没动**。

---

## 4. 族 B：语义不同（只记日志 + 跳过，**不删表项**）

### 4.1 原生形状（两处逐字节同形，本轮实测复核）

`sub_76533C` = `TCreature.SendRefMsg`：

```
765468  8B D8 / 4B              ebx := Count; ebx--            ; 倒序遍历
76546B  83 FB 00 / 0F 8C ..     ebx < 0 -> 出循环
765474  8B 45 FC / 8B 80 80 03 00 00   eax := self^.[+0x380]   ; TList
76547D  8B D3
76547F  E8 C8 F8 CB FF          call 0x424D4C                  ; TList.Get(idx)
765484  89 45 F4                item := eax
765487  83 7D F4 00 / 0F 84 96 00 00 00   item = nil -> jmp 0x765527   ; 跳过
765491  8B 45 F4
765494  E8 CB 08 00 00          call 0x765D64                  ; ★ 有效性谓词
765499  84 C0 / 75 49           jne 0x7654E6                   ; 有效 -> 幽灵判定
; ---- 无效臂：只记日志 ----
76549D  68 D4 56 76 00          push 0x7656D4  "[Exception]: TCreature.SendRefMsg Obj.CName = 空 Obj = "
7654A2-7654CE                   拼 3 段（名字 + ClassName）
7654DF  E8 90 8A 03 00          call 0x79DF74                  ; 记异常
7654E4  EB 41                   jmp 0x765527                   ; ★ 跳过，表项留着
; ---- 有效臂 ----
7654E6  8B 45 F4 / 80 78 73 00  cmp byte [item+0x73],0         ; 幽灵
7654ED  74 12                   je 0x765501                    ; 非幽灵 -> 发消息
7654EF  8B 45 FC / 8B 80 80 03 00 00 / 8B D3
7654FA  E8 31 F6 CB FF          call 0x424B30                  ; ★ TList.Delete —— 幽灵才删
7654FF  EB 26                   jmp 0x765527
765501-765522                   SendMsg(item, ...)
```

`sub_765790` = `TCreature.SendRefBuff` 在 `0x7658E0` 完全同形：
`0x7658E5 84 C0 / 0x7658E7 75 49 jne 0x765932`；无效臂 `0x7658E9 push 0x765B1C`
（`"[Exception]: TCreature.SendRefBuff Obj.CName = 空 Obj = "`）→ `0x76592B call 0x79DF74`
→ `0x765930 EB 45 jmp 0x765977`（跳过，不删）；有效臂 `0x765935 cmp byte [eax+0x73],0`
→ 幽灵 `0x765946 call 0x424B30`（删）。

`0x424B30` 实测就是 `TList.Delete`：`0x424B72 FF 48 08 dec dword [eax+8]`（FCount−−）
后做 `System.Move` 前移；它内部还调 `0x424B67 call 0x424D4C`（= `TList.Get`，
`0x424D64 3B 42 08 cmp eax,[edx+8]` 是同一套下标越界检查）。

**「无效 → 只记日志跳过」与「幽灵 → 删表项」在同一个函数里相隔 22 字节，处置不同。**
这是「有效性 ≠ 死亡/幽灵」最直接的一条原生佐证，也是族 B 绝不能照抄族 A 摘链的原因。

### 4.2 C# 落点：三个名字合流成一个

全仓核查结果（这一条推翻了任务书里的顾虑）：

| 原生 | C# |
|---|---|
| `TCreature.SendRefMsg`（`0x765494`） | `TBaseObject.SendRefMsg`（`GameSvr/Actors/TBaseObject.cs:3987`） |
| `TCreature.SendRefBuff`（`0x7658E0`） | **C# 无此方法** |
| `TPlayer.SendRefMsg`（`0x6DC725`） | **C# 无 `TPlayObject` 覆写** |
| `TPlayer.SendRefBuff`（`0x6DCB89`） | **C# 无此方法** |
| `TPlayer.SendDirectClientMsg`（`0x6DC282`） | **C# 无此方法** |

`GameSvr/Players/TPlayObject.Message.cs` 里 `SendRefMsg` 只有**调用点**，没有定义，
也没有 `m_VisibleHumanList` 的任何引用。全仓 `m_VisibleHumanList` 的**消费循环只有一处**
（`TBaseObject.cs:4086`）。

⇒ **禁改文件不需要动，也不需要主代理接线。** 族 B 只需要接这一处，本轮已接：

```csharp
for (var nC = 0; nC < m_VisibleHumanList.Count; nC++)
{
    BaseObject = m_VisibleHumanList[nC];
    if (!IsNativeCellObjectValid(BaseObject)) { continue; }   // 只跳过，不删表项
    if (BaseObject.m_boGhost) { continue; }
    ...
}
```

`IsNativeCellObjectValid` 对 `null` 返回 false，一并覆盖了原生 `0x765487` 那条
「item = nil 也跳过」的独立臂。O(1)、零分配，无 try / 无装箱 / 无 LINQ。

### 4.3 明确没有接到 `TBaseObject.cs:4031`

那是 `m_VisibleHumanList` 的**重建**扫描（60 秒时限那条）。原生的重建函数
`sub_7651EC` 在 `0x765263`–`0x76528A` 只做 `Clear` + 调 `Envirnoment VMT+0x1C`，
**根本不调 `sub_765D64`**。探针在**消费侧**，不在重建侧。接上去就是自造行为，
本轮按前作结论回避。

### 4.4 族 B 顺带发现（登记，未改）

1. **幽灵处置不同**：原生在这条循环里对幽灵是 `TList.Delete`（`0x7654FA` / `0x765946`），
   C# 只 `continue`。缓存表项会一直留着，直到下次 500 ms 重建。
2. **遍历方向不同**：原生 `Count-1 → 0` 倒序（`0x765468 8B D8 / 4B`），C# 正序。
   在「边遍历边删」的原生实现里倒序是必须的；C# 不删所以看不出差异，但一旦要补 §4.4.1
   就必须先改方向。

两条都超出本条范围，只登记。

---

## 5. 族 B 的另一变体：AI 选靶两处（**位置 UNPROVEN，未接**）

`0x6F4499`（`sub_6F43C8`）/ `0x6F4882`（`sub_6F4790`）是族 B 的「整段放弃」变体：
`je` 而非 `jne`，**无日志、无删表**，只是把当前候选整个丢掉。本轮新取的证据：

```
6F4866  E8 4D E6 07 00          call 0x772EB8            ; ObMode/状态
6F486B  84 C0 / 0F 85 F0 00..   jne 0x6F4963             ; 放弃候选
6F4873  80 BB E3 02 00 00 00    cmp byte [ebx+0x2E3],0
6F487A  0F 85 E3 00 00 00       jne 0x6F4963             ; 放弃候选
6F4880  8B C3
6F4882  E8 DD 14 07 00          call 0x765D64            ; ★ 有效性谓词
6F4887  84 C0 / 0F 84 D4 00..   je  0x6F4963             ; 无效 -> 放弃候选
6F488F  8B D3 / 8B C7
6F4893  E8 F0 F9 07 00          call 0x774288            ; 潜行/隐身过滤（与 0x6DC6F1 同一个）
```

`0x6F445D`–`0x6F4499` 那一处除了少一个 `+0x73` 前置检查外完全同形。
两个函数各只有一个调用点，都在 `sub_6D7D68`（一个 13 KB 的大 switch 派发器）里：
`0x6DB1A8 call 0x6F4790`（带 `eax=Self, edx=[ebp-8]` 一个串、`cx=[..+6]` 一个 word）、
`0x6DB1B5 call 0x6F43C8`（只带 `eax=Self`），两条紧邻，且都 `jmp 0x6DBC2C` 回汇合点。

**C# 落点仍未定位 ⇒ UNPROVEN，不接。** 这两处也**不属于**任务书点名的族 B 三项。

---

## 6. 另两项判定

### 6.1 `0x77A3A6` 那条 `[Exception] … CName = 空` 诊断日志 —— **建议不接**（且六处同理）

- 原生调用形状：`mov eax,[0x7D5ECC] / mov eax,[eax] / mov cl,1 / call 0x79DF74`，
  即「日志管理器单例 . Add(msg, level=1)」。C# 侧现成的对应物就是 `M2Share.ErrorMessage(...)`，
  `TBaseObject.cs` / `Envirnoment.cs` 里到处都在用 —— **前作担心的「要动日志管理器接线」
  其实不存在**，接进去只是一行。
- 但仍建议不接，理由是**两族的风险不对称**：
  - **族 A**（摘链）：日志被摘链天然限流，每个坏节点只报一次。接与不接都安全，
    但**零 gameplay 可观察面**——它只影响日志文件内容。
  - **族 B**（不删表项）：原生对**同一个**坏表项**每次广播都要报一次**。
    `SendRefMsg` 是每 tick 多次的热路径，一旦真的出现坏表项就是日志洪水。
    这是原生的一个疣，不值得为了「一比一」把它搬过来。
- 结论：**默认不接**。若主代理出于运维诊断需要，**只接族 A 五处**（自限流），
  族 B 那处坚决不接。

### 6.2 `[Player.VMT+0x1BC]`（`0x6E21F8`）内部的可见性过滤 —— **UNPROVEN，未动**

前作 §7 的观察本轮原样登记，未新增取证：

```
6E2267  C7 45 E8 01 00 00 00     [ebp-0x18] := 1
6E226E  8B 45 FC / 80 78 73 00   cmp byte [obj+0x73],0    ; 幽灵
6E2275  0F 85 A6 04 00 00        jne 0x6E2721             ; -> 退出
6E227B  C7 45 E8 02 00 00 00     [ebp-0x18] := 2
6E2282  C6 80 E9 02 00 00 01     mov byte [obj+0x2E9],1   ; ★ 副作用：在目标身上置标志
6E2289  80 B8 E3 02 00 00 00     cmp byte [obj+0x2E3],0
6E2290  74 0D                    je 0x6E229F              ; 非零 -> 返回 False
```

- 原生这段**开头没有 `+0x74`（`m_boDeath`）测试**，C# 把过滤内联在循环里且**含**
  `m_boDeath`；
- 原生有 `mov byte [obj+0x2E9],1` 的**写副作用**，C# 完全没有；
- `+0x2E3` / `+0x2E9` 的**字段身份本轮未取证**。

⇒ 属 SPWN-55 / `UpdateVisibleGay` 地界，**UNPROVEN 登记，本轮不动**。

---

## 7. 交主代理的清单

### 7.1 需要接线的（本轮 fail-closed 未动）

| # | 原生 | C# 文件 + 位置 | 需要做什么 |
|---|---|---|---|
| 1 | `sub_7776EC` `TEnvironment.AddToMap`，谓词 `0x7777EA` | `GameSvr/Maps/Envirnoment.cs:198` `public object AddToMap(int nX, int nY, CellType btType, object pRemoveObject)`，插入点在 `if (!bo1E) { OSObject = new CellObject { ... }; MapCellInfo.ObjList.Add(OSObject); ... }` 之前 | **先补原生缺失的去重扫描**（遍历本格链，`node.CellObj == pRemoveObject` 就 `node.dwAddTime = <now>` 并 `return pRemoveObject`，不再插入），谓词才有地方挂。属另一条账，不是纯接线 |
| 2 | 同上 | 同上，方法签名 | 原生还多一个「存活秒数」入参，`dwAddTime := now + sec*1000 − 600000`（`0x77774D` / `0x777753`）；C# 签名里没有 |
| 3 | `sub_76533C` / `sub_765790` 的幽灵删表项 | `GameSvr/Actors/TBaseObject.cs:4108` `if (BaseObject.m_boGhost) { continue; }`（在 `TBaseObject.SendRefMsg` 的 `m_VisibleHumanList` 消费循环里） | 原生是 `TList.Delete`（`0x7654FA` / `0x765946`），C# 只 `continue`；同时要把该循环改成倒序（原生 `0x765468 8B D8 / 4B`） |
| 4 | `sub_77A990` 的 `TList` 出参 | `GameSvr/Actors/TBaseObject.ViewRange.cs:149` `SearchViewRange()` | 原生 `0x77ABC9 call 0x424AB8` 把非 creature 收进调用方传入的表，C# 无此出参 |

### 7.2 **不需要**主代理接线的（澄清任务书的顾虑）

- 族 B 的 `TPlayer.SendRefMsg` / `SendRefBuff` / `SendDirectClientMsg` 三处
  （`0x6DC725` / `0x6DCB89` / `0x6DC282`）：**C# 里这三个方法一个都不存在**，
  没有 `TPlayObject` 覆写，全部合流到已接的 `TBaseObject.SendRefMsg`。
  **`TPlayObject.Message.cs` 不需要动。**

### 7.3 仍 UNPROVEN

| 项 | 状态 |
|---|---|
| `0x6F4499` / `0x6F4882`（`sub_6F43C8` / `sub_6F4790`，AI 选靶「整段放弃」变体） | 原生形状已取证（§5），**C# 落点未定位** |
| `[VMT+0x1BC] = 0x6E21F8` 内部的可见性过滤：无 `+0x74`、有 `mov byte [obj+0x2E9],1` 副作用 | `+0x2E3` / `+0x2E9` **字段身份未取证**，属 SPWN-55 |
| `sub_77A990` 的搜索半径全局 `[0x7D6754]` 解引用出的那个 int | 身份未取证（只知道是个整数半径，用于夹取扫描窗） |

---

## 8. 方法与可复现

- 反汇编：仓内 `tools/m2_disasm.py`（capstone x86-32，`off = VA − 0x400000`）。
- 临时脚本置于 `%TEMP%`，未入库：
  `v56b_win.py`（**对齐窗口反汇编**：在 `[target−back, target)` 里逐字节试起点，
  取第一个能让指令流正好落在 target 上的起点，解决 Delphi 代码段线性反汇编错位）、
  `v56b_fnstart.py`（函数入口反推）、`v56b_callers.py`（`E8 rel32` 调用点普查）、
  `v56b_str.py`（Delphi 长串 `[addr-4]` 长度前缀 + GBK 解码）、
  `v56b_dwref.py`（dword 全镜像引用）。
- **函数命名一律不靠猜**：从失败臂里的 `push imm32` 取串，`[addr-4]` 校长度、
  `[addr-8]` refcount = −1 校形状，串里就是作者写的函数名。族 A 六处全部命中。
- 构建：`dotnet build GameSvr/GameSvr.csproj` → 0 错误 / 15 警告，与基线逐条相同。
