# DROP-33 —— 掉落散落半径全表复核（own-table 腿 / 世界腿 / 玩家死亡腿）

- 日期：2026-08-14
- 分支：`w/drop33b`（基线 `69f049b6`）
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`）
- 工具：`tools/drop33b_xref.py`（E8/E9 相对调用普查）、`tools/drop33b_ctx.py`（调用点对齐反汇编）、
  `tools/drop33b_owner.py`（归属函数）、`tools/drop33b_dword.py`（VMT 槽）、
  `tools/drop33b_strings.py`（四路编码字面量探测）、`staging\_eqv25_tools\dx.py`

## 结论摘要

| 条目 | 判定 |
|---|---|
| own-table 腿（段2）半径 = 3 | **已 FAITHFUL**，`9584a44f` 已修，账本 `eqv_shard19` 该条为陈旧快照 |
| 玩家死亡散落半径 = 2 | **FAITHFUL**，且 C# 早已是两条独立路径，不存在任务书假设的「共享半径耦合」 |
| `nDropItemRage` | **INVENTED**，四路编码全镜像 0 命中，本轮删净两处读取 |
| `NativeDropControlRuntime.ScatterRange` | **发现回归**：`f3354457` 误把 4 改成 3，本轮撤回 |
| `RunInNativeOrder` 次序 | **发现回归**（`62478ccf`），证据齐但**未改**，见 §6 |

---

## 1. 任务书前提的订正

任务书写道「C# 走共享函数 `ScatterBagItems`，半径 `_MIN(nDropItemRage,7)`，且该函数同时服务
玩家死亡散落，改半径会波及玩家路径」。**这个前提在 `69f049b6` 上已不成立**：

- `9584a44f`（2026-08-14 01:23，已在 master）给私有 `ScatterBagItems` 加了显式
  `int DropWide` 形参，怪物 own-table 调用点（`TBaseObject.Base.cs:1265`）传常量
  `NativeMonsterOwnTableScatterRange = 3`。
- 玩家死亡走的是 `TPlayObject.ScatterBagItems` 的 **override**（`TPlayObject.Base.cs:2219`，
  `const int DropWide = 2`），与怪物腿调用的私有 3-arg 重载**不是同一个方法**，虚派发也
  到不了对方。

所以「共享热点」在代码层面早就拆开了，两条腿各自持有自己的半径。任务书依据的
`docs/eqv_shard19_20260814.md` 第 52 行是修复前的快照。

残留的真问题是第三处：基类 1-arg 虚入口仍读 `_MIN(nDropItemRage,7)`（见 §4）。

---

## 2. 三条腿的原生半径（逐字节）

### 2.1 段2 —— 怪物自有掉落表（own-table）：**3**

```
0071FDC2  6A 01                  push 1              ; 第5参=1，绕过 0x78389C mode-5 否决
0071FDC4  6A 00                  push 0
0071FDC6  8B 45 F8 / 50          push [ebp-8]
0071FDCA  68 00 01 72 00         push 0x720100       ; "(爆天赐)"
0071FDCF  B9 03 00 00 00         mov ecx,3           ; ← 半径
0071FDD4  8B 55 D8               mov edx,[ebp-0x28]  ; item
0071FDD7  8B 45 F4               mov eax,[ebp-0xc]   ; creator
0071FDDA  E8 C1 8A 04 00         call 0x7688A0

0071FE2A  6A 01 / 6A 00 / push [ebp-8] / (0x720120 "怪物死亡:" 拼串)
0071FE46  B9 03 00 00 00         mov ecx,3           ; ← 半径（另一臂）
0071FE51  E8 4A 8A 04 00         call 0x7688A0
```

段2 的身界：头 `0x71FCFF`（`mov eax,[self+0x474]` 取自有表），空表早退
`0x71FD0E 0F 8C 93 01 00 00 jl 0x71FEA7`，循环尾 `0x71FEA1 0F 85 70 FE FF FF jne 0x71FD17`。

### 2.2 段3 —— **"世界掉落"**：**3**（与 C# `NativeDropControlRuntime` 无关，见 §5）

```
0071FEA7  8D 45 E8 / 50          lea eax,[ebp-0x18] / push eax   ; &outCount
0071FEAE  8B 80 28 01 00 00      mov eax,[self+0x128]            ; PEnvir
0071FEB4  8B 48 44               mov ecx,[eax+0x44]              ; 地图名 AnsiString
0071FEBA  0F B7 90 78 02 00 00   movzx edx,word [self+0x278]
0071FEC1  A1 F4 71 7D 00         mov eax,[0x7D71F4] / 8B 00      ; 单例
0071FEC8  E8 DF 2D 03 00         call 0x752CAC                   ; 世界掉落表查询
...
0071FF24  E8 2B DF 02 00         call 0x74DE54                   ; MakeItemByName
0071FF38  68 34 01 72 00         push 0x720134                   ; "世界掉落"
0071FF3D  B9 03 00 00 00         mov ecx,3                       ; ← 半径
0071FF48  E8 53 89 04 00         call 0x7688A0
```

字面量 `0x720134` 的原始字节 `CA C0 BD E7 B5 F4`（GBK）= **"世界掉落"** ——
段3 在原生里就叫这个名字。C# **没有**这条腿的实现（`[0x7D71F4]` 单例、`sub_752CAC`
在全树零引用）。

### 2.3 玩家死亡散落：**2**（四个 worker 一律 2）

调用链：`TPlayer.Die sub_6C07A0 @0x6C07D8 → sub_741368`（策略梯，按六个地图旗标字节四选一）：

```
00741447  E8 B4 EE FF FF   call 0x740300   ; TSpecialDropItem worker
00741457  E8 EC 78 00 00   call 0x748D48   ; 按图配额 worker
00741461  E8 0A E8 FF FF   call 0x73FC70   ; 装备掉落 ("死亡爆出-")
00741469  E8 0A EC FF FF   call 0x740078   ; 背包散落
```

四个 worker 的半径：

```
00740479  B9 02 00 00 00   mov ecx,2   -> 00740482  call 0x7688A0   (sub_740300)
00748DD9  B9 02 00 00 00   mov ecx,2   -> 00748DE2  call 0x7688A0   (sub_748D48)
0073FEF5  B9 02 00 00 00   mov ecx,2   -> 0073FEFE  call 0x7688A0   (sub_73FC70)
0074022D  B9 02 00 00 00   mov ecx,2   -> 00740236  call 0x7688A0   (sub_740078)
```

`sub_740078` = `TPlayObject.ScatterBagItems` 本体，其 override 的 `const int DropWide = 2`
正对上 `0x74022D`。**玩家死亡散落在原生就是独立的一条腿，半径 2，不与怪物腿共享任何东西。**

---

## 3. 全镜像散落半径普查

半径参数一路怎么走到求空地循环（三条腿共用同一条通路）：

```
007688B4  8B D9              mov ebx,ecx        ; sub_7688A0 序言存半径
00768907  53                 push ebx           ; -> sub_768688 的 [ebp+0x10]
0076891E  E8 65 FD FF FF     call 0x768688      ; GetDropPosition
007686B5  8B 45 10           mov eax,[ebp+0x10] ; 环数
007686BA  0F 8E A6 00 00 00  jle 0x768766       ; <=0 直接放弃
```

`sub_768688` 里 `[ebp-0x18]` 从 1 递增走 `[ebp+0x10]` 轮，每轮扫 `[-r,+r]×[-r,+r]`
（`0x7686CA..0x768760`），末尾 `0x76876C cmp [ebp-0x14],8 / jge` 是「最优代价 ≥8 就用原地」
的兜底。所以这个数是**向外扩几圈找空地**。

`sub_7688A0` 全镜像 E8 调用点 **15 个**，逐点取证：

| 调用点 | 所属函数 | 半径 | 路径 | 标签串 |
|---|---|---|---|---|
| `0x64E79D` | `sub_64E6F4` | `[ebp-8]` | 脚本按名掉物（**唯一**调用方传参） | — |
| `0x682CE3` | `sub_682CA4` | 2 | 单品定点掉落 | `0x682D14` "朱火碎片" |
| `0x6E632D` | `sub_6E626C` | 2 | 按名从背包剔除并落地 | — |
| `0x71F7DB` | `sub_71F740` | 3 | 怪物 Die `0x71E3AB` 前置支 | `0x71F818` "(爆天赐)" |
| `0x71FC0D` | `sub_71FA20` 段1 | 5 | MonItemsTree 专属链 | — |
| `0x71FC84` | `sub_71FA20` 段1 | 5 | 同上（另一臂） | — |
| **`0x71FDDA`** | **`sub_71FA20` 段2** | **3** | **怪物自有掉落表** | `0x720100` "(爆天赐)" |
| **`0x71FE51`** | **`sub_71FA20` 段2** | **3** | 同上（另一臂） | `0x720120` "怪物死亡:" |
| `0x71FF48` | `sub_71FA20` 段3 | 3 | 世界掉落（C# 无实现） | `0x720134` "世界掉落" |
| **`0x72021D`** | **`sub_72016C`** | **4** | **掉落控制四相 = `NativeDropControlRuntime`** | nil |
| `0x73CE13` | `sub_73CC98` | 1 | 玩家手动丢物 CM_DROPITEM | — |
| `0x73FEFE` | `sub_73FC70` | 2 | 玩家死亡：装备掉落 | `0x74006C` "死亡爆出-" |
| **`0x740236`** | **`sub_740078`** | **2** | **玩家死亡：背包散落** | — |
| `0x740482` | `sub_740300` | 2 | 玩家死亡：TSpecialDropItem worker | — |
| `0x748DE2` | `sub_748D48` | 2 | 玩家死亡：按图配额 worker | — |

金币走另一个子程序 `sub_768AAC`（6 个调用点：`0x64E74A` / `0x64E765` / `0x64F5C0` /
`0x64F5DB` / `0x6C30F9` / `0x72000A`），半径同样是立即数：

```
00768ADC  6A 03              push 3
00768AF4  E8 8F FB FF FF     call 0x768688
```

**14/15 是立即数，唯一变量半径来自脚本 API `sub_64E6F4` 的 ecx 形参**
（`0x64E796 8B 4D F8 mov ecx,[ebp-8]`，`edx` 名与 `0x64E7E0`「金币」比对后改走
`sub_768AAC`）。没有任何一条腿读配置。

---

## 4. `nDropItemRage` —— INVENTED，本轮删净

本轮独立复核（`tools/drop33b_strings.py`，四路编码）：

| 字面量 | ASCII | ASCII 大小写不敏感 | UTF-16LE | GBK |
|---|---|---|---|---|
| `DropItemRage` | 0 | 0 | 0 | 0 |
| `nDropItemRage` | 0 | 0 | 0 | 0 |
| `DropItemRange` | 0 | 0 | 0 | 0 |
| `ItemRage` | 0 | 0 | 0 | 0 |
| `DropWide` | 0 | 0 | 0 | 0 |

`GameSvrConfig` 里它也**没有任何 ini 解析入口**，只有 `617: public int nDropItemRage;`
与 `1453: nDropItemRage = 3;`。两处读取本轮删除：

1. **`TBaseObject.DropGoldDown`（`TBaseObject.cs:1490`）的死局部**。
   `int DropWide = _MIN(nDropItemRage,7)` 全文件仅此一次出现，方法体到 `return` 为止
   再未被引用；落点实际由 `GetDropPosition(..., 3, ...)` 决定，与原生
   `0x768ADC push 3` 一致。删除，行为零变化。

2. **基类 1-arg 虚入口** `TBaseObject.ScatterBagItems(ItemOfCreat)`，改为常量 2。
   **该入口在当前树上不可达**：唯一调用点是死亡分支 `m_btRaceServer == RC_PLAYOBJECT`
   那一支的 `ScatterBagItems(null)`，而 `RC_PLAYOBJECT` 全树只有 `TPlayObject` 构造
   函数会赋（`TPlayObject.Base.cs:802`；`UsrEngn.cs:565` 赋值对象也是 `TPlayObject`），
   虚派发必然落到 `TPlayObject` 的 override。`TPlayCloneObject : TPlayObject` 带的是
   `RC_PLAYCLONE`，走的是死亡分支的**怪物支**（`m_btRaceServer != RC_PLAYOBJECT`），
   到 `1265` 的私有 3-arg 重载（半径 3），并被 `1617` 的
   `RC_PLAYCLONE && m_Master != null` 早退挡住。
   取 2 的依据：这条不可达的路唯一可能代表的原生腿就是玩家死亡散落
   （`sub_740078 @0x74022D mov ecx,2`）。行为零变化，但去掉了一个 fail-open 地雷。

新增 `GameSvr/Actors/TBaseObject.NativeScatterRange.cs`（partial class）集中存放
半径常量与 §3 全表；`NativeMonsterOwnTableScatterRange` 从 `TBaseObject.Base.cs` 迁入，
值与注释不变。

---

## 5. 【回归】`NativeDropControlRuntime.ScatterRange` 应为 4，不是 3

### 5.1 归属搞错了

`f3354457` 把 `ScatterRange` 从 4 改成 3，理由是「本类 = `sub_71FA20` 段3，段3 半径是
`0x71FF3D mov ecx,3`」。**归属是错的。** C# 的 `NativeDropControlRuntime` 对应的是
`sub_720278`（掉落控制四相派发器），与段3 是两套互不相干的子系统。

归属由**记录布局**唯一确定。`NativeDropControlRecord.ToNativeLayout()` 写出的偏移
（`NativeSize = 104`）逐字段对上 `sub_77C580`（Counted）与 `sub_77C738`（Timed）：

| C# 字段 | 偏移 | 原生取证 |
|---|---|---|
| MonsterName | +0x00 | 桶键 |
| ItemName | +0x29 | `77C62F 83 C2 29 add edx,0x29` |
| Quantity | +0x52 | `77C641 66 8B 40 52 mov ax,word [rec+0x52]` |
| PeriodOrRange | +0x54 | `77C7AD 69 40 54 E8 03 00 00 imul eax,[rec+0x54],0x3E8` |
| ItemIndex | +0x58 | `77C639 8B 40 58 mov eax,[rec+0x58]` |
| Counter | +0x5C | `77C5ED 66 FF 40 5C inc word [rec+0x5C]` |
| RandomThreshold | +0x5E | `77C5F9 66 3B 42 5E cmp ax,word [rec+0x5E]` |
| Tick | +0x60 | `77C608 89 50 60 mov [rec+0x60],edx` / `77C7A8 2B 50 60 sub edx,[rec+0x60]` |

`SelectTimed` 的形状也逐条对上：`elapsed = now - Tick`；`interval = PeriodOrRange*1000`
（`imul ...,0x3E8`）；`if (elapsed < interval) return`（`77C7B6 0F 82` = **无符号** `jb`，
C# 侧正是 `unchecked((uint)...)`）。

四相次序对上 `TryScatter`：

```
; sub_720278
0072029E  E8 9D 80 CE FF   call 0x408340                      ; now
007202A5  8B 83 28 01 00 00 / 8B 40 2C   mov eax,[PEnvir+0x2C] ; 地图级状态
007202B0  B2 01 / E8 85 C2 05 00         dl=1 / call 0x77C53C  ; SelectMap(Timed)
007202BD  E8 AA FE FF FF                 call 0x72016C         ; ScatterPhase
007202CD  B2 02 / E8 68 C2 05 00         dl=2 / call 0x77C53C  ; SelectMap(Counted)
007202DA  E8 8D FE FF FF                 call 0x72016C
007202E4  E8 D7 C1 04 00                 call 0x76C4C0         ; 取怪物名
007202ED  A1 B4 5E 7D 00                 mov eax,[0x7D5EB4]    ; 世界级单例
007202F6  B2 01 / E8 53 5C 03 00         dl=1 / call 0x755F50  ; SelectWorld(Timed)
00720303  E8 64 FE FF FF                 call 0x72016C
0072031F  B2 02 / E8 2A 5C 03 00         dl=2 / call 0x755F50  ; SelectWorld(Counted)
0072032C  E8 3B FE FF FF                 call 0x72016C
```

`sub_77C53C` / `sub_755F50` 都是 `dec dl; je` 二选一派发器（1=Timed、2=Counted），
`sub_755F50` 多带一个 `[ebp+8]` 名字串 —— 正是 `SelectWorld` 比 `SelectMap` 多的那个
`monsterName` 形参。

### 5.2 `sub_72016C` = `Materialize`，半径是立即数 4

```
007201DC  80 7B 14 07        cmp byte [item+0x14],7      ; StdMode==7 (pile)
007201E0  75 14              jne 0x7201F6
007201E2  66 8B 40 08 / 66 89 43 26   word[item+0x26] := word[node+8]   ; Dura := remaining
007201EC  66 C7 40 08 00 00           word[node+8] := 0                 ; remaining := 0
007201F4  EB 06              jmp
007201F6  66 FF 48 08        dec word [node+8]                          ; remaining--
00720204  8B 08 / FF 51 28   call [item_vmt+0x28]                       ; InitializeForDrop
00720213  B9 04 00 00 00     mov ecx,4                                  ; ← 半径
0072021D  E8 7E 86 04 00     call 0x7688A0
00720257  66 83 78 08 00     cmp word [node+8],0
0072025C  0F 87 59 FF FF FF  ja 0x7201BB                                ; while (remaining != 0)
```

与 `Materialize` 一一对应（pile 分支写 `userItem.Dura = remaining` 并把 `remaining` 清零、
否则 `remaining--`、`InitializeForDrop` 走 `[VMT+0x28]`、落地失败也不回滚 —— C# 的
`continue` 对上 `0x720226 call 0x404690` 释放后继续循环）。

### 5.3 既有钉子早就在喊

`AuditTools/NativeDropControlRuntimeCheck/Program.cs:104`：

```
Equal(4, failedRange, "native fixed scatter range");
```

`f3354457` 只改了常量、没改这条断言，于是该工具在 master 上一直是红的：

```
Unhandled exception. System.InvalidOperationException:
  native fixed scatter range: expected=4, actual=3
```

PASS 横幅 `Program.cs:145` 里也仍写着 `scatter-range=4`。**本轮撤回 4 后该工具 PASS。**

---

## 6. 【回归，只报不改】`RunInNativeOrder` 的次序

同一处误归属还导致了 `62478ccf`「Run the monster's own drop table before the controlled
world drop」把次序从 controlled-first 改成 ordinary-first。真实调用图是**兄弟调用**，
掉落控制整个跑在 `sub_71FA20` **之前**：

```
; sub_71F46C —— 怪物 VMT 槽 +0x1FC
0071F46C  55 8B EC 53 56 57      push ebp / mov ebp,esp / push ebx,esi,edi
0071F472  8B F9 / 8B F2 / 8B D8  edi:=ecx / esi:=edx / ebx:=eax
0071F478  8B CF / 8B D6 / 8B C3  原样转发
0071F47E  E8 F5 0D 00 00         call 0x720278   ; ← 掉落控制四相
0071F483  8A 45 0C / 50 / 8A 45 08 / 50          ; 两个栈参再压一遍
0071F491  E8 8A 05 00 00         call 0x71FA20   ; ← 段1/段2/段3/金币
0071F49A  C2 08 00               ret 8
```

三条独立佐证：

1. `sub_71FA20` 全镜像**只有 `0x71F491` 这一个 E8 调用点，0 个 dword 引用**
   （`tools/drop33b_xref.py` + `tools/drop33b_dword.py`），不可能先于 `sub_720278` 跑。
2. `0x71F46C` 有 **123 个 dword 引用**，抽样 5 个（`0x607118` / `0x6073C8` / `0x65E22C` /
   `0x66107C` / `0x5F9830`）全部满足 `slot - 0x1FC = VMT` 且 Delphi 自指针自检
   `dword[VMT-0x4C] == VMT` 通过 —— 它就是 VMT 槽 `+0x1FC`。
3. 怪物 Die `0x71E3D2` / `0x71E3EF` 的 `FF 96 FC 01 00 00 call [esi+0x1FC]` 派发到的
   因此是 `sub_71F46C`，不是 `sub_71FA20`。

**为什么本轮不改**：`sub_720278` 在 `sub_71FA20` **之外**，所以不受段内那几道门约束——
`0x71FA6C` 一次性哨兵、`0x71FA8A cmp dword [self+0x474],0 / je 0x720092` 空掉落表早退、
`0x71FADA` / `0x71FAE3` / `0x71FAEC` 防沉迷三门。而 C# 把 `TryScatter` 放在
`scatterBlocked` 里边（`TBaseObject.Base.cs:1236-1258`）。`sub_720278` 自身从序言到
第一相之间**没有任何条件跳转**（`0x720278..0x7202B2` 逐条已核），唯一在它上游的门是
怪物 Die 的 `0x71E3B7 cmp byte [self+0x47D],0 / jne 0x71E3F5`（= `m_boNoItem`，它同时
挡掉 VMT 调用本身）。

也就是说：次序与门控是同一个契约的两半。只搬次序不动门控，会落到「既不是原生也不是
现状」的第三种状态。**这需要独立立项**（次序 + 门控一起改 + 重钉
`NativeDropControlRuntimeCheck` 的 `rng-order` 断言），不在 DROP-33 半径范围内。
本轮只把 `RunInNativeOrder` 的注释从错误归属订正为真实调用图，行为一字未动。

---

## 7. 本轮落地

| 提交 | 内容 |
|---|---|
| `51b3ea2e` | `NativeDropControlRuntime.ScatterRange` 3 → **4**，重写归属注释（`sub_720278`/`sub_72016C` 字节证据），订正 `RunInNativeOrder` 注释 |
| `6dfda5b0` | 删两处 `nDropItemRage` 读取；新增 `TBaseObject.NativeScatterRange.cs`（partial，半径常量 + §3 全表）；迁入 `NativeMonsterOwnTableScatterRange`；新增 5 个取证脚本 |

**受影响面**

- `ScatterRange` 4：只影响掉落控制（`m_DropItemControl` / 世界掉落控制配置）落地时的
  找空地环数，从 3 圈恢复为 4 圈。`internal const`，单一引用点（`Materialize`）。
- 基类 1-arg 虚入口半径：**不可达路径**，运行时零影响。
- `DropGoldDown` 的 `DropWide`：死局部，运行时零影响。
- **玩家死亡散落完全未触碰**：`TPlayObject.ScatterBagItems` 的 override 与它的
  `const int DropWide = 2` 本轮一个字节没动。
- **怪物 own-table 腿未触碰**：`NativeMonsterOwnTableScatterRange = 3` 只是换了个文件放。

**验证**

- `dotnet build GameSvr`：0 错。
- `NativeDropControlRuntimeCheck` PASS（`scatter-range=4` 断言恢复绿）。
- `DeathDropPolicyCheck` / `NativeDropControlParserCheck` / `NativeMapDropItemCommandCheck` /
  `Drop39WeightPolarityCheck` / `MonsterDomainRaceAndDropCheck` /
  `YanshenEquipDropBoostCheck` 全 PASS。

---

## 8. 接线需求 / 遗留（只报）

1. **`RunInNativeOrder` 次序 + 门控**（高优先，见 §6）。需独立立项：把
   `TryScatter` 提到 `TraverseMonItemsTree` 之前，并按 `sub_720278` 在
   `sub_71FA20` 之外的事实重划它的门控（只受 `m_boNoItem` 约束，不受哨兵 /
   空掉落表 / 防沉迷三门约束），同时重钉 `NativeDropControlRuntimeCheck` 的
   `rng-order` 断言。
2. **段3「世界掉落」整条未移植**（`sub_752CAC` @ 单例 `[0x7D71F4]`，键
   `[PEnvir+0x44]` 地图名 + `word[self+0x278]`，半径 3）。C# 全树零对应物。
   注意别再把它和 `NativeDropControlRuntime`（= `sub_720278` 掉落控制）搞混 ——
   两者字面量分别是 `0x720134`「世界掉落」与四相桶配置，是两个功能。
3. **`g_Config.nDropItemRage` 字段本身**（`GameSvrConfig.cs:617/1453`）现已零读取。
   没有 ini 解析入口，删除它不影响配置往返，但会动到 `GameSvrConfig.cs`，留给
   配置面的专项处理。
4. **`AuditTools/NativeDropRngSequenceCheck` 双重损坏**（与本轮改动无关，
   `git status` 证实该文件本分支未触碰）：
   - 编译错：`Program.cs:109` 把 `HelmetUnknown08` 当参数传，缺 `()`
     （`Program.cs:174` 声明为 `static int[] HelmetUnknown08()`，第 35/39 行都带括号）。
     现象是 CS1503「方法组无法转换为 int[]」+ 两条 CS8422 连带错。
   - 修好编译后仍崩：`NativeJewelStoneTable.Apply`（`NativeItemPlus28.cs:482`）
     `IndexOutOfRangeException`；另需在工作目录放 `!Setup.txt`（内容 `[Server]`）。
5. `docs/eqv_shard19_20260814.md` 第 47-52 行（DROP-33 判定）与第 135-142 行
   （FIX-1）已被本轮推翻，需重写。
