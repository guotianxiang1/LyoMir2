# DURA-37..44 · 物品耐久 `[item+0x26]` 写者全镜像穷举普查

- 日期：2026-08-14 ／ 任务代号 `durawr`
- 分支：`w/durawr`（自 `master` `69f049b6` 切出）
- 底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`，平映射（file_off = VA − 0x400000），17,661,952 B
- 工具：capstone 5.0.7 x86-32，脚本全部入库于 `tools/durawr_re/`（可复现）
- 前置报告：`docs/dura_deep_writers_20260814.md`（本报告**订正其第 2 节的错误结论**，见 §6）

---

## 0. 结论摘要

1. **受击掉装备耐久在原生中确实存在**，走 `sub_73F9FC → sub_73FBE8 → sub_75EBC0 → sub_75EA40`，
   `sub_75EBC0` 就是 `inc esi; cmp esi,0x10` 的 **16 槽循环**。前置报告称「native 中不存在」并建议
   复检删除 C# 侧循环 —— 该结论为误读，若照办会造成破坏性回退。详见 §3、§6。
2. 槽 **0/2/3/7/8/10/15 确认掉耐久**；槽 **9(U_BUJUK)、12(U_CHARM) 确认不掉耐久** —— 排除不是按
   槽号写死的，而是每件装备的虚谓词 `[item_vmt+0x74]`（TEquipItem 虚槽 29）按**类**决定。详见 §3.3。
3. 全镜像 `word [reg+0x26]` 写指令穷举：**201 条**落在合法指令边界，其中 word 宽 **161** 条；
   剔除 `TAbility+0x26`(MAC.max) 假阳性 5 条与 VCL 1 条后，**155 条**是物品耐久写，分布在 **125 个函数**。
   前置报告的「~20 个写者」严重低估。
4. 已落地外科修复 3 条（全部有字节证据），commit `fa74f229`。
5. 仍 BLOCKED 3 条，全部因为**缺物品类模型 / 效果本体未移植**，按铁律不猜。

---

## 1. 方法学（可复现，脚本已入库）

前置报告用「只看已点名的 5 个 VA」的做法会漏掉两类写者，本次改为穷举：

| 阶段 | 脚本 | 做法 | 产出 |
|---|---|---|---|
| 1 扫描 | `tools/durawr_re/census.py` | `0x26` 只有紧跟 ModRM/SIB 才可能是 disp8，故对**每个** `0x26` 字节回溯 1..7 个起点解码，保留内存操作数 `disp==0x26` 者；另扫 disp32 `26 00 00 00` | 3974 条触碰 / 1663 条写 |
| 2 定界 | `validate.py` | 以 `E8/E9 rel32` 目标 **∪ 全部 VMT 虚方法槽**（74,115 个种子）为起点线性扫描，建立合法指令边界集（1,771,714 个），取交集剔除数据区误解码 | 201 写（161 word 宽）|
| 3 归属 | `classify.py` | 函数入口同上。<br>**只用 call 目标会误判**：`0x788DAD` 会被挂到 `0x788C5C`，实际属 `TTaoFaLingAddExpItem` 虚槽 6 = `0x788CA8` | 每条写者 → 函数 + 类名#槽号 |
| 4 去伪 | `triage.py` | 剔除 `TAbility+0x26` 假阳性（见 §2.2） | 156 候选 |
| 5 定性 | `finalize.py` / `ctx.py` / `slots.py` | 逐条取写点前 5 后 2 指令；另做 `GetUseItems` 全调用点 + 槽号普查（107 站点） | 算术 / 门 / 槽位 |

VMT 索引（`vmt.py`）用 Delphi 自指针性质 `[V-0x4C] == V` 精确识别，全镜像 **1349 个类**，
`-0x2C` 类名、`-0x28` 实例大小、`-0x24` 父类（**双重解引用**，`vmtParent` 是 `PPointer`）。

> **方法学陷阱（本次踩到并修正，后续勿重犯）**：阶段 2 的种子若只用 `E8/E9 rel32` 目标，
> **只经 VMT 分派的方法整个函数体都不会被扫描**，其中的写者会被静默丢弃。首轮因此漏了 12 条，
> 包括 `TLuckOil#6 @0x7858E6`、`TRepairOil#6 @0x7859BF/@0x7859FF`、`TRope#6` 3 条、
> `TGroupAddExpItem#6 @0x788F2D`（`sub 0xA`，全镜像唯一的 −10 算术），以及前置报告点名过的
> `TLevelBuffItem#6 @0x78B1FE`。把 VMT 槽并入种子集后 149 → 161。

---

## 2. 基础事实（全部亲验）

### 2.1 物品对象布局

| 偏移 | 含义 | 证据 |
|---|---|---|
| `+0x1C` | `pStdItem` | `0x7845B0`：`mov eax,[eax+0x1c]; mov ax,[eax+0x1a]; ret` |
| `+0x20` | **内嵌 `TUserItem` 记录起点** | `0x75F4CA`：`add eax,0x20`，随后 `[eax+6]`=Dura、`[eax+8]`=DuraMax |
| `+0x24` | wIndex（= 记录 +4） | `0x783788`：`mov ax,[esi]; mov [ebx+0x24],ax`（esi=StdItem）|
| **`+0x26`** | **Dura (word)**（= 记录 +6） | `0x784584 SetDura`：`mov cx,[eax+0x28]; cmp dx,cx; jbe; mov [eax+0x26],cx` / `mov [eax+0x26],dx` |
| `+0x28` | **DuraMax (word)**（= 记录 +8） | 同上；`0x7845A8 GetDuraMax` |
| `+0xFC` | 强制磨损标志（绕过 1/8 门） | `0x75EA74`：`cmp byte [ebx+0xfc],0; jne` |
| `+0x104` | 特殊分类位图（bit 0..7） | `0x75FF7C`：`cmp dl,7; ja; and edx,0x7f; bt [eax+0x104],edx; setb al` |

> 因为记录内嵌在 `+0x20`，**独立的 `pTUserItem` 指针其 Dura 在 `+6`**。凡以 `+0x26` 寻址的，
> base 必是**物品对象**而非记录指针 —— 这是本次判定 base 身份的硬依据。

构造器 `0x783788`（TBaseItem.Create）在 `0x7837E6` 处 `mov ax,[esi+0x1c]` → `[ebx+0x26]` 与 `[ebx+0x28]`，
即**建物时 `Dura = DuraMax = word[StdItem+0x1C]`**。

### 2.2 假阳性：`TAbility+0x26` = MAC.max

`TEquipItem` 虚槽 12 = `0x75FB14` 证明另一个结构在同偏移有别的字段：

```
0075FB43  add word ptr [edi+0x22], ax   ; AC.max   <- item[+0x2A]
0075FB4C  add word ptr [edi+0x26], ax   ; MAC.max  <- item[+0x2B]
0075FB55  add word ptr [edi+0x2a], ax   ; DC.max   <- item[+0x2C]
0075FB5E  add word ptr [edi+0x2e], ax   ; MC.max   <- item[+0x2D]
0075FB67  add word ptr [edi+0x32], ax   ; SC.max   <- item[+0x2E]
```

五个属性各占一个 dword（低半字=min、高半字=max），写的是**高半字**。故
`0x637199`、`0x699E9B`、`0x751387`、`0x75FB4C`、`0x760AC3` **不是耐久写**，已剔除。
另 `0x439227`（fn `0x4390AC`，VCL Graphics 单元）亦非物品。

### 2.3 装备容器

`GetUseItems(container=eax, slot=dl)` = `0x75EC20`：
`0x75EC48` 判 `slot < 0x10`，然后 `mov edi,[esi+eax*4+8]` —— **容器 +8 起的 16 项指针数组**，
`container+4` = 宿主对象（`0x75EA40`/`0x75F49C` 都从这里取 owner 判 `is THumanKind`）。

### 2.4 物品类继承（VMT 索引实测）

```
TObject → TBaseObj → TBaseItem(VMT 20 槽) → TEquipItem(VMT 30 槽)
                                              ├─ TClothes/TManClothes/TWomanClothes/TLWeapon/
                                              │  TRWeapon/TBrokenWeapon/TSpade/THelmet/THeadMask/
                                              │  TNecklace/TRing/TArmRing/TBelt/TBoots/TMaPai …
                                              ├─ TCharm → THPCharm/TMPCharm/THPMPCharm/
                                              │           TCryCharm/TMarkStoneCharm
                                              └─ TEquipBujuk → TBujuk/TDragonHeart/TSuperDragonHeart/
                                                               TPoisons/TVessel/TUnionItem
```

**`TBaseItem` 的 VMT 只有 20 槽，虚槽 29 不存在** ⇒ 能进装备容器的只可能是 `TEquipItem` 后代
（否则 `sub_75EA40 @0x75EA69` 的 `call [vmt+0x74]` 会越界）。这是 §3.3 类别排除成立的结构前提。

---

## 3. 受击掉耐久：16 槽循环（DURA-39 核心，本次取证解闭）

### 3.1 调用链（逐跳亲验）

```
sub_73F9FC  StruckDamage (THumanKind/TPlayer/TGdMsgGMAgent 虚槽 42；怪物侧对应 0x767A18)
  @0x73FB51  test esi,esi ; jle 0x73FBBD        ; 伤害<=0 直接返回
  @0x73FB55  mov edx,[ebp-8]                    ; nDam = Random(10)+5 (@0x73FA30)
  @0x73FB58  mov eax,ebx                        ; self = 被击者
  @0x73FB5A  call 0x73FBE8
sub_73FBE8   (整函数只有 5 条指令)
  @0x73FBEB  mov eax,[eax+0x4C0]                ; 装备容器
  @0x73FBF1  call 0x75EBC0                      ; edx=nDam 原样传递
sub_75EBC0   16 槽循环
  @0x75EBD2  xor esi,esi
  @0x75EBD7  mov eax,[eax+esi*4+8]              ; container[i]
  @0x75EBDD  je  next                           ; 空槽跳过
  @0x75EBDF  push ebp                           ; 传父帧（nDam 在 [父ebp-4]）
  @0x75EBE0  lea edx,[ebp-0xA]                  ; out: 通知标志
  @0x75EBE3  call 0x75EA40
  @0x75EBEB  cmp byte [ebp-0xA],0 ; je
  @0x75EBF6  call 0x75F49C                      ; SendDuraChange(container, i)
  @0x75EC03  inc esi ; cmp esi,0x10 ; jne       ; ← 16 槽
  @0x75EC0D  je  skip
  @0x75EC12  call 0x75EE78                      ; RecalcAbilitys（仅当有物品损毁）
```

### 3.2 单件磨损 `sub_75EA40` 完整语义

```
0075EA62  mov byte [esi],0                      ; *pNotify = false
0075EA69  call dword [item_vmt+0x74]            ; 虚槽 29：本件是否受击磨损
0075EA6E  je  exit(false)
0075EA74  cmp byte [item+0xFC],0 ; jne 0x75EA8F ; 强制磨损标志 → 跳过随机门
0075EA7D  mov eax,8 ; call Random               ;
0075EA89  jne exit(false)                       ; ← 1/8 概率门
0075EA8F  movzx eax,word [item+0x26]            ; Dura
0075EA96  cmp eax,[父ebp-4]                     ; 与 nDam 比
0075EA99  jle 0x75EAD1                          ; Dura <= nDam → 损毁臂
;--- 磨损臂 ---
0075EAA4  sub word [item+0x26],ax               ; Dura -= nDam
0075EAAA  mov ecx,0x3E8 ; cdq ; idiv ecx        ; oldPoint = oldDura / 1000  (截断)
0075EAB8  mov ebx,0x3E8 ; xor edx,edx ; div ebx ; newPoint = newDura / 1000  (截断)
0075EAC1  cmp ecx,eax ; je exit(false)          ; 显示点未变 → 不通知
0075EAC9  mov byte [esi],1                      ; *pNotify = true，返回 false
;--- 损毁臂 ---
0075EAD1  owner = [container+4]
0075EAE2  if owner is THumanKind:
0075EB10     log(owner, 0x43, "持久耗尽"+itemname)      ; 0x768BE0
0075EB4B     broadcast(owner, 0xFFDB, "您的"+itemname+"失效了")
0075EB51  mov word [item+0x26],0                ; Dura = 0
0075EB57  mov byte [esi],1                      ; *pNotify = true（无条件）
0075EB5A  mov byte [ebp-1],1                    ; 返回 true → 触发 RecalcAbilitys
```

**原生在损毁臂里既不删物品、也不清 wIndex** —— `0x75F49C` 是发包不是删除（见 3.4）。
佐证：`RecalcAbilitys` = `0x75EE78` 遍历 16 槽时 `call GetDura; test ax,ax; jbe skip`
（`0x75EE93-0x75EE9B`），**耐久归零的装备留在身上但不计入属性**。

### 3.3 哪些槽真的掉耐久 —— 按类不按槽

虚槽 29（`[vmt+0x74]`）全镜像只有三种实现：

| 实现 VA | 代码 | 语义 | 使用者 |
|---|---|---|---|
| `0x75F6C8` | `cmp word [eax+0x26],0; jbe →0; mov al,1` | **`Dura > 0`** | TEquipItem 及全部普通装备类（衣服/左右手武器/头盔/面罩/项链/戒指/手镯/腰带/靴子/马牌…）|
| `0x763344` | `xor eax,eax; ret` | **恒 false** | TCharm 族（TCharm/THPCharm/TMPCharm/THPMPCharm/TCryCharm/TMarkStoneCharm）|
| `0x762C18` | `xor eax,eax; ret` | **恒 false** | TEquipBujuk 族（TBujuk/TDragonHeart/TSuperDragonHeart/TPoisons/TVessel/TUnionItem）|

结论（对应任务第 5 项）：

| 槽 | 常量 | 受击掉耐久 | 依据 |
|---|---|---|---|
| 0 | U_DRESS | **是** | 16 槽循环第 0 次迭代；装备类谓词 = `Dura>0` |
| 1 | U_WEAPON | **是**（另有攻击侧 `DoDamageWeapon` 单独磨损）| 同上 + `0x73E810` |
| 2 | U_RIGHTHAND | **是** | 同上 |
| 3 | U_NECKLACE | **是** | 同上 |
| 4,5,6,11 | 头盔/手镯/靴 | **是** | 同上 |
| 7 | U_RING_L | **是** | 同上 |
| 8 | U_RING_R | **是** | 同上 |
| 9 | U_BUJUK | **否** | 该槽物为 TEquipBujuk 族，谓词 `0x762C18` 恒 false |
| 10 | U_BELT | **是** | 同上 |
| 12 | U_CHARM | **否** | 该槽物为 TCharm 族，谓词 `0x763344` 恒 false |
| 13,14,15 | — | **是**（若槽内是 TEquipItem 后代）| 循环覆盖 0..15，无按槽特判 |

> 循环本身对 16 个槽**一视同仁**，没有任何按槽号的分支；排除完全由物品类的虚谓词完成。

### 3.4 `0x75F49C` 是 SendDuraChange，不是删除

```
0075F4A9  mov edx,[0x73BBE8]      ; VMT:THumanKind
0075F4AF  call 0x404828           ; owner is THumanKind ?
0075F4BC  call 0x75EC20           ; item = GetUseItems(container, slot)
0075F4CA  add eax,0x20            ; → 内嵌 TUserItem 记录
0075F4D2  movzx ecx,word [eax+6]  ; Dura（当前值）
0075F4D7  movzx eax,word [eax+8]  ; DuraMax
0075F4E2  mov cx,0x278D           ; = 10125 SM_DURACHANGE
0075F4EA  call 0x765E68
```

### 3.5 C# 现状对照与判定

C# `TBaseObject.StruckDamage`（`GameSvr/Actors/TBaseObject.cs`，DURA-16 域）：

| # | 项 | 原生 | C#（修复前） | 判定 | 处置 |
|---|---|---|---|---|---|
| 1 | 16 槽循环存在性 | 存在 | 存在 | **FAITHFUL** | 保留（前置报告曾建议删除，**驳回**）|
| 2 | 1/8 概率门 | `Random(8)!=0` → skip | `Random(8)==0` → 执行 | FAITHFUL | — |
| 3 | 磨损算术 | `Dura -= nDam` | 同 | FAITHFUL | — |
| 4 | 损毁判据 | `Dura > nDam` 否则损毁 | `nDura-nDam <= 0` 损毁 | FAITHFUL（等价）| — |
| 5 | RecalcAbilitys 时机 | 仅有物品损毁时 | `if (bo19)`，bo19 只在损毁置位 | FAITHFUL | — |
| 6 | 参与门 | 虚谓词（装备类 = `Dura>0`）| `wIndex > 0` | **DIVERGENT** | ✅ 已补 `Dura>0` |
| 7 | 显示点比较 | `idiv/div 0x3E8` 截断 | `HUtil32.Round(x/1000.0)` 银行家舍入 | **DIVERGENT** | ✅ 已改截断 |
| 8 | 损毁臂通知 | 无条件发包，报当前 Dura(=0) | 仅当舍入点变化才发，且报负数 | **DIVERGENT** | ✅ 已改 |
| 9 | 类别排除（槽 9/12 不磨损）| 谓词恒 false | 无此概念，槽 9/12 照样磨损 | **DIVERGENT** | ⛔ BLOCKED（见 §5.1）|
| 10 | 损毁后是否删物 | **不删**，留在身上 Dura=0 | `SendDelItems` + `wIndex=0` + `FeatureChanged` | **DIVERGENT** | ⛔ BLOCKED（见 §5.2）|
| 11 | `+0xFC` 强制磨损标志 | 绕过 1/8 门 | 未建模 | **MISSING** | ⛔ 承前 fail-closed |
| 12 | 损毁日志/广播 | `"持久耗尽"`(0x43) + `"您的X失效了"`(0xFFDB) | gamedata `'3'` 日志 | **DIVERGENT** | ⛔ 报告不改 |

---

## 4. 写者全普查表

155 条物品耐久写分布在 125 个函数。按**机制**分组；每组给出代表 VA + 字节 + 算术 + 判定。
算术分布：`mov` 96、`sub` 39、`dec` 15、`add` 6；`sub/add` 的立即数只有
`0x3E8`(=1 显示点) ×19、`0x64` ×4、`0x7D0` ×1、`0xA` ×1，其余为寄存器变量。

### 4.1 战斗 / 生命周期路径（关键组）

| VA | 字节 | 反汇编 | 函数 / 类·虚槽 | 触发 | 算术 | 槽位 | 判定 |
|---|---|---|---|---|---|---|---|
| `0x75EAA4` | `66294326` | `sub word [ebx+0x26],ax` | `sub_75EA40` | **受击**（经 `73F9FC→73FBE8→75EBC0`）| `Dura -= nDam`，门=虚槽29 + (1/8 或 `+0xFC`) | **全 0..15** | FAITHFUL（本次补齐 3 处细节）|
| `0x75EB51` | `66c743260000` | `mov word [ebx+0x26],0` | `sub_75EA40` | 同上，`Dura<=nDam` | `Dura=0`，广播"您的X失效了"，**不删物** | 全 0..15 | DIVERGENT#10 |
| `0x73E850` | `66c743260000` | `mov word [ebx+0x26],0` | `sub_73E810` DoDamageWeapon | **攻击方**武器磨损 | `Dura=0`（耗尽）+ SM_DURACHANGE | **slot 1** | FAITHFUL |
| `0x73E88C` | `66897326` | `mov word [ebx+0x26],si` | `sub_73E810` | 同上 | `Dura -= nDura`；显示点变化才发包 | slot 1 | FAITHFUL |
| `0x73EC73` | `66816e26e803` | `sub word [esi+0x26],0x3e8` | `sub_73EC40`（`sub_73ED28` 的内嵌局部过程）| **复活戒指/重生戒指** | `Dura -= 1000`，`<=1000` 则 `=0` | **扫 0..15 取首个命中** | **MISSING**（§5.3）|
| `0x73EC7B` | `66c746260000` | `mov word [esi+0x26],0` | 同上 | 同上 | `Dura=0` + 广播"失效了" | 同上 | MISSING |

`sub_73ED28(player, mode)` 全貌：`xor esi,esi` → `GetUseItems(container, esi)` →
谓词 `sub_73EBF0`（`item≠nil` ∧ `Dura>0` ∧ 分类位命中）→ 扣 1000 → 发 SM_DURACHANGE +
日志 `"死亡复活-"`(msg 0x13) → **`jmp` 退出（只扣一件）**；未命中则 `inc esi; cmp esi,0x10; jne`。
两个调用者：`0x74379A`（`edx=0`，"靠戒指的力量，您复活了。"）与 `0x743867`（`edx=1`，"靠戒指的力量,您获得了重生。"）。
`mode 0` 查 `+0x104` bit0；`mode 1` 查 bit1 或 bit2（`sub_75FF7C`）。

### 4.2 修理 / 批量设置

| VA | 反汇编 | 函数 | 语义 | 判定 |
|---|---|---|---|---|
| `0x75F602` | `mov word [eax+0x26],di` | `sub_75F5A0(container, slot)` | 单槽修满：`Dura<DuraMax` → `Dura=DuraMax` + SendDuraChange。唯一调用者 `0x6BE185` | 待接线核对 |
| `0x6F3356` | `mov word [eax+0x26],dx` | `sub_6F3324(player, value)` | **16 槽全设为常量**（`inc edi; cmp edi,0x10`）。唯一调用者 `0x623BED` | 待接线核对 |
| `0x746517` | `mov word [esi+0x26],ax` | `sub_7464DC` | `Dura != DuraMax` → `Dura = DuraMax` | 待接线核对 |
| `0x7858E6` | `dec word [eax+0x26]` | `TLuckOil#6` | 幸运油：取 **slot 1** 武器（`mov dl,1; call GetUseItems`），成功后油本身 `dec`，归零删物 | 待接线核对 |
| `0x7859BF` | `add word [ebx+0x26],cx` | `TRepairOil#6` | **修复油**：`Dura += n` 同时 `DuraMax -= n/30`（`mov edi,0x1e; cdq; idiv edi`）| 待接线核对 |
| `0x7859FF` | `mov word [ebx+0x26],ax` | `TRepairOil#6` | 特殊分支 `al==0xA`：`Dura = DuraMax`（不掉上限）| 待接线核对 |
| `0x78458D`/`0x784592` | `mov [eax+0x26],cx/dx` | `0x784584 SetDura` | 通用设置器，`min(v, DuraMax)` 封顶 | FAITHFUL |

### 4.3 物品使用处理器（`TEquipItem` 虚槽 6/7）

按类逐条列出（`dec` = 扣 1 raw，`sub 0x3e8` = 扣 1 显示点）：

| VA | 类·虚槽 | 算术 / 门 |
|---|---|---|
| `0x763410` / `0x763418` | `TCryCharm#6` | `Dura>=1000 → -=1000`，否则 `=0` |
| `0x76349E` | `THPCharm#7` | `dec`（每次回血 tick）|
| `0x76351B` | `TMPCharm#7` | `dec` |
| `0x7635AC` / `0x7635D0` | `THPMPCharm#7` | `dec`（HP 臂 / MP 臂各一）|
| `0x764184` / `0x7641EE` | `TMarkStoneCharm#7` | `dec`（两个分支各一，门 `sub_764544` 真）|
| `0x76362C` | `TVessel#6` | 门 `Dura>=0x64` → `-=100` |
| `0x7872B6`/`0x787365`/`0x787424` | `TFireFlower#6` | 三个分支各 `-=1000`（门 `Dura>=0x3e8`）|
| `0x7883EE` | `TGoldAcus#6` | `dec` |
| `0x788511` | `TNewHappyCake#6` | `dec` |
| `0x788DAD` | `TTaoFaLingAddExpItem#6` | `dec`，写 `player+0xBC4/+0xBC8` |
| `0x7891EC` | `TCastleCityFlyStone#6` | 门：所在地图 == 城堡地图 → `-=100` |
| `0x78936B` | `TDreamFlyStone#6` | `-=1000` |
| `0x7896C6` | `TTimerBomb#6` | `-=1000` |
| `0x78A770` | `TBufferFlower#6` | `dec` |
| `0x78AAF9` | `TPileFlower#6` | `dec` |
| `0x78BAC2` | `THorseYearBadge#6` | 门 `Dura>=0x3e8` → `-=1000` |
| `0x786B0D`/`0x786C2A`/`0x786D0B` | `TRope#6` | 三个分支各 `-=1000` |
| `0x786E71` | `TRndFlyStone#6` | `-=1000` |
| `0x788AF0` | `TNewPlayerBox#6` | `Dura = di`（由 `0x7520C4` 返回值决定）|
| `0x788F2D` | `TGroupAddExpItem#6` | 门 `sub_727C1C` 真 → **`Dura -= 10`**（全镜像唯一的 −10）|
| `0x78B1FE` | `TLevelBuffItem#6` | 门 `sub_746888(player) > 0` → `dec` |
| `0x78B61A`/`0x78B6BE`/`0x78B762`/`0x78B80F`/`0x78B8CE` | `THuoYuan/TShuiYuan/TMuYuan/TJinYuan/TTuYuanMedicament#6` | 五行药：`Dura=0`（整件耗尽）|
| `0x78B9AD` | `TAutoTransScore#6` | `Dura=0` |
| `0x789163` | `TCastleCityFlyStone#12` | 过期检查：`Dura=0; DuraMax=0` |
| `0x76019A` / `0x76030E` | `TTemporaryManClothes#12` / `TTemporaryWomanClothes#12` | 临时装到期（`fcomp qword [+0xA]` 比时间）：`Dura=0; DuraMax=0` |
| `0x783A32` / `0x783A3A` | `TEquipItem#11`（TBaseItem 级） | 门 `StdMode(byte[StdItem+0x14])==0x28` → `Dura -= 2000`，否则 `Dura=0` |
| `0x7849CD` | `TEquipItem#16` | `Dura=0; DuraMax=0` |
| `0x78468E` | `TEquipItem#15` | `Dura = DuraMax` |
| `0x783F2F` | `TEquipItem#10` | `Dura = round(n / K * ratio)`（`fdiv [0x783F38]`）|

### 4.4 建物 / 升级 / 属性生成（`Min(65000, Dura+delta)` 家族）

`0x760A3C`(TLWeapon#10) `0x761330`(THelmet#10) `0x761481`(THelmet#2) `0x761E18` `0x761F20`(TRing#2)
`0x762710` `0x76283F`(TArmRing#2) `0x784050` —— 统一形态：

```
movzx edx, word [item+0x26] ; add edx, <delta> ; mov eax,0xFDE8 ; call 0x4C700C ; mov word [item+0x26],ax
```
即 **`Dura = Min(65000, Dura + delta)`**。属装备生成/强化路径，不是掉耐久。

### 4.5 构造器默认值

`0x787A6A` `0x787CB9` `0x787EFE` `0x788112` `0x788BFB` `0x788C7E` `0x789A82` `0x78B276` `0x78B2D2`
`0x78B322` `0x78B53E` `0x78B5AA` `0x78B64E` `0x78B6F2` `0x78B796` `0x78B846` `0x78B902` `0x78BA22`
`0x760E06` `0x7636DE` `0x763B3E` 等 —— 全部紧跟 `call 0x783788`(TBaseItem.Create) 或
`call 0x7880F0`，在 `fn+0x22` 处 `mov word [esi+0x26], 0/1` 覆盖默认 Dura。**非耐久损耗**。

### 4.6 背包栈 / 取物 / 合并 / 英雄（承前置报告，本次复核无误）

`0x6DF96D`(Take) `0x740C8D` `0x740DE4` `0x74061A` `0x746FC3` `0x74723A` `0x6DFCFB` `0x6FC2CF`
—— 同一形态 `sub word [item+0x26],di` + 耗尽删物，属 Take/TakeExpand 家族；
`0x6D5F4E`(TVessel 合并 `+=100`)、`0x6E9D0B`(定位石 `dec`)、`0x73E9A4`/`0x73EB0A`(护身符 slot9)、
`0x68686E`/`0x68F4CA`(英雄龙之心)、`0x68C9DE`(毒药 `-=100`)、`0x6A0B11`/`0x6A0BB4`/`0x6A0C64`(技能石复制 `-=1000`×3)、
`0x6CF2B2`/`0x6CF30E`/`0x6CF37D`(群体效果 `-=1000`) —— 判定与前置报告一致，不重复。

### 4.7 本次新发现、前置报告未列的写者（节选）

`0x6EB97B`(TMicroWhelk `-=1000`)、`0x73EC73`/`0x73EC7B`(复活/重生戒指)、`0x75EAA4`/`0x75EB51`(**受击磨损**)、
护符家族 7 条(`0x76349E`/`0x76351B`/`0x7635AC`/`0x7635D0`/`0x763410`/`0x764184`/`0x7641EE`)、
`TFireFlower` 3 条、`TRope` 3 条、`0x78813C` 家族 4 条(栈合并 `add`/`sub`)、
`0x783A32`/`0x783A3A`(StdMode 0x28 装备 `-=2000`)、`0x6F3356`(16 槽批量设置)、`0x75F602`(单槽修满)、
`TLuckOil`/`TRepairOil` 3 条、`TGroupAddExpItem`(`-=10`)、`TRndFlyStone`、`TNewPlayerBox` 等。
合计 **130+ 条**为前置报告所无；前置报告 20 条中唯一本次首轮漏检的 `0x78B1FE` 已在阶段 2 修种子后补回。

---

## 5. 仍 BLOCKED 条目

### 5.1 槽 9 / 槽 12 的类别排除 —— BLOCKED

原生靠虚槽 29 按类恒 false 排除；C# 没有物品类模型。要忠实实现需先移植
**StdMode/Shape → 物品类工厂**（原生 `0x74CE00-0x74D1A0`，`case byte[StdItem+0x14]` 套
`case byte[StdItem+0x15]` 双层跳表，实测静态可解，类引用形如 `mov eax,[0x75E3F8]`）。
该工厂的完整提取超出本任务范围。**不猜代用（例如"按槽号 9/12 硬排除"）**——那是推断不是证据。

### 5.2 损毁后是否删除装备 —— BLOCKED（建议 DURA-16 属主裁决）

原生 `sub_75EA40` 损毁臂只写 `Dura=0`，不清 wIndex、不删物；`RecalcAbilitys`(`0x75EE78`)
以 `GetDura()>0` 跳过它。C# 额外做 `SendDelItems` + `wIndex=0` + `FeatureChanged`。
证据充分，但删除既有 C# 行为会改变物品生命周期（涉及背包同步与客户端删除包），
属破坏性变更，本报告只出具证据不擅动。

### 5.3 复活/重生戒指的 16 槽扣耐久 —— BLOCKED（效果本体未移植）

`sub_73ED28` 的触发链（`0x74379A` / `0x743867`）依赖 `+0x104` 分类位图，而该位图的**写入点**
未在本次范围内定性；孤立补 `Dura-=1000` 会给一个 C# 尚未落地的复活戒指机制造扣减。按铁律 fail-closed。

### 5.4 `+0xFC` 强制磨损标志 —— 承前 fail-closed

`sub_74DAE4 @0x74DC58..0x74DDF0` 的越界属性检测负责置位；未移植，维持现状。

---

## 6. 对 `docs/dura_deep_writers_20260814.md` 的订正

| 前置报告原文 | 实际 | 证据 |
|---|---|---|
| §2「装备槽 0/2/3/7/8/10/12/15 的受击掉耐久——native 中不存在」 | **错**。存在，且覆盖全部 16 槽 | `0x75EBC0` 的 `inc esi; cmp esi,0x10; jne 0x75EBD4` |
| §2「`call sub_73FBE8`(仅 RecalcAbilitys 包装)」 | **错**。`0x73FBE8` 只有 5 条指令，`@0x73FBF1 call 0x75EBC0` 进入 16 槽循环；RecalcAbilitys 是该循环的**尾调用**(`@0x75EC12`) | 见 §3.1 |
| §2「⚠️分歧候选：C# 16 槽循环疑为 GameOfMir 祖本遗留，建议 DURA-16 复检（删除）」 | **驳回**。该循环是忠实移植，删除将造成破坏性回退 | 同上 |
| §0「全部 ~20 个 `+0x26` 写者」 | 低估。合法边界上 word 宽写 161 条，扣除假阳性后 **155 条 / 125 个函数** | §1 阶段 1-4 |
| §1.1 表未含 `0x75EAA4` / `0x75EB51` | 这两条恰是受击磨损的核心写者 | §3.2 |
| §0「165 命中」 | 未做指令边界校验，含大量数据区误解码；本次校验后为 201（word 宽 161） | `validate.py` |
| 未识别 `TAbility+0x26` 假阳性 | 5 条需剔除 | §2.2 |

其余（Take 家族、护身符、定位石、TVessel 合并、技能石复制等）本次复核**与前置报告一致**，
其 fail-closed 判定成立，予以保留。

---

## 7. 已落地改动

分支 `w/durawr`：

| commit | 内容 |
|---|---|
| `0020c204` | `tools/durawr_re/` 普查工具链（census/validate/classify/triage/finalize/slots/vmt/ctx）|
| `aa77113f` | 检查点：受击链存在性取证 |
| `fa74f229` | `fix(DURA-39)`：新增 `GameSvr/Actors/TBaseObject.NativeStruckDurability.cs`（partial），并对 `TBaseObject.cs` 受击循环做 3 处外科修复 |

修复明细（全部有字节证据）：

1. **参与门补 `Dura > 0`** —— 证据 `0x75F6C8`：`cmp word [item+0x26],0; jbe → false`。
   修复前耐久已为 0 的装备会再次进入磨损路径并在下一次受击时被判损毁，原生绝不会。
2. **显示点改截断整除** —— 证据 `0x75EAB0 idiv 0x3E8` / `0x75EABF div 0x3E8`；
   原 `HUtil32.Round`（银行家舍入）会在与原生不同的命中上发/不发 `RM_DURACHANGE`。
3. **损毁臂无条件发包并报当前耐久** —— 证据 `0x75EB57` 无条件置标志、`0x75F4D2/0x75F4D7`
   读的是物品**当前** `+0x26`/`+0x28`；原实现只在舍入点变化时发包，且把负的运行值当耐久发出。

`dotnet build GameSvr/GameSvr.csproj` 通过（0 错误，15 条既有告警）。

未触碰热点文件 `SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、
`GameSvr/UsrSystem/UsrEngn.cs`。
