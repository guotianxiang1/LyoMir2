# 背包容量 / 负重 / 眼神大背包 对账（w/m-bagcap）

日期：2026-08-13  
镜像：`D:\loym2\staging\_reunpack_work\flat_image.bin` 基址 `0x400000`  
眼神转储：`yanshen2_0_8_dll.memory.bin` 基址 `0x10000000`  
**未执行** `dotnet build`。

---

## 判定计数

| 判定 | 条数 |
|---|---|
| FAITHFUL | 12 |
| DIVERGENT（已修） | 2 |
| DIVERGENT（未改，fail-closed / 非本闸） | 1 |
| MISSING | 0 |
| INVENTED | 0 |
| BLOCKED | 3 |

---

## 1. 原生背包容量 `sub_6D0AE8`（已复核）

```
6D0AE8  8B 80 08 05 00 00  mov eax,[eax+0x508]   ; bag TList
6D0AEE  8B 40 08           mov eax,[eax+8]       ; Count
6D0AF1  81 E2 FF 00 00 00  and edx,0xFF
6D0AF7  03 C2              add eax,edx
6D0AF9  83 F8 30           cmp eax,0x30          ; 48
6D0AFC  0F 9E C0           setle al              ; Count+(edx&0xFF) <= 48
6D0AFF  C3                 ret
```

这是 **VMT+0x244**（两份 VMT `0x62EF8C` / `0x6AC8C8` 的 +0x244 都是 `0x6D0AE8`）。E8 直调 **0**。

内层加物 `sub_73D078`：

```
73D084  B2 01              mov dl,1
73D08A  FF 91 44 02 00 00  call [ecx+0x244]
73D090  84 C0              test al,al / je fail
73D09C  E8 …               call 0x73CEA8         ; TList.Add
73D0A7  FF 91 58 02 00 00  call [ecx+0x258]
73D0AF  E8 30 FE FF FF     call 0x73CEE4         ; WeightChanged
```

外层加物 `sub_6B7378`（VMT+0x248，68 个虚调用）无条件落到 `call 0x73D078`。

**加物是否都过这道闸：** 主路径是。另有：

| 路径 | VA | 是否过 6D0AE8 |
|---|---|---|
| 内层 Add `73D078` | `mov dl,1` + VMT+0x244 | 是 |
| 无重量刷新 `73D0C0`（4 个 E8：`6D0BBA` 英雄→人物等） | 同样 VMT+0x244 | 是 |
| 英雄加物 `73D0F4`（E8 `6D0A5E`） | VMT+0x244 | 是（英雄对象自己的 VMT） |
| 直接 `cmp [bag+8],0x30 / jge` | `6BDAA5`、`6DD6B5` | 等价 Count>=48 拒绝，然后仍 `call [vmt+0x248]` |
| 存档循环 | `6B171B 83 FF 30 / 75 C9` | 写满 48 槽停止（静默截断） |
| 上线装填 `UsrEngn` `m_ItemList.Add` | 无闸 | 与原生一致（存档已裁 48） |

C#：`AddItemToBag` → `Count < BagCapacity.Of(this)`；无插件时 Of=48。**FAITHFUL**。

---

## 2. 负重

### 物品重量

运行时 `TStdItem` **word `[std+0x1A]`**（`NativeType2StdItemRuntimeAppend.Weight => ReadUInt16(0x1A)`）。  
`sub_73E8D4` RecalcBagWeight：

```
73E90A  80 78 14 07        cmp byte [item+0x14],7   ; StdMode 7 堆叠
73E910  8B 40 1C           mov eax,[item+0x1C]      ; StdItem
73E913  0F B7 40 1A        movzx eax,word [eax+0x1A]
73E917  01 45 FC           add [ebp-4],eax          ; 非堆叠：直接加
73E91C  … imul Weight, Dura ; 堆叠：Weight * Dura
```

写入 `player+0x2C4`（`73CEF1 mov [ebx+0x2C4],eax`）。  
**前人 `WEIGHT_CALCULATION_ANALYSIS.md` 把 Weight 写成 `byte [std+0x13]` / 函数 `0x76443C`，与 RecalcBagWeight 字节不符。**

### 人物负重上限

等级公式（`RecalcLevelAbilitys`，战士例 `50 + ROUND(lv/3*lv)`，钳 `0xFFDC`），再 `RecalcAbilitys`：装备 `m_AddAbil.Weight`，肌肉戒指 **加倍**：

```
6BE1A3  8B 83 C8 02 00 00  mov eax,[ebx+0x2C8]   ; MaxWeight
6BE1A9  01 83 C8 02 00 00  add [ebx+0x2C8],eax   ; *=2
```

随后同样加倍 `+0x2D0` Wear、`+0x2D8` Hand。C# `m_boMuscleRing` 分支一致。**FAITHFUL**。

### 超重后果

只出现在 **跑** 原语，走不受影响：

```
6BBFE5  8B 83 C4 02 00 00  mov eax,[ebx+0x2C4]
6BBFEB  3B 83 C8 02 00 00  cmp eax,[ebx+0x2C8]
6BBFF1  7D 0E              jge 0x6BC001          ; Weight >= MaxWeight → 降成走
```

门：全局开关 `[0x7D7038]+2 bit7`、地图 `envir+0xB0`（RUNFLAG）为真则跳过。降级走 `sub_6BBCD8`，不是拒走。C# `IsNativeRunLadderAllowed` 已接。**FAITHFUL**。

### 重算时机

`sub_73CEE4`：`call 0x73E8D4` → 写 `+0x2C4` → `mov byte [ebx+0x458],1`。  
E8 调用点 **34** 个；内层 Add 成功后 `73D0AF` 是其中之一。  
`+0x458` 在 Run 里清掉并 `mov cx,0x2783` 发包。C# `WeightChanged()` 当场 `RM_WEIGHTCHANGED`，可观测等价。

**1034 制造** `0x63FF2E call [edi+0x248]`，全函数 **无** `0x73C950`。C# `ClientMakeDrugItem` 只 `AddItemToBag`。**不要加负重门。FAITHFUL。**

### 负重判定 DIVERGENT（已修）

```
73C950  8B 90 C4 02 00 00  mov edx,[eax+0x2C4]  ; 覆盖调用方传入的 dx
73C956  3B 90 C8 02 00 00  cmp edx,[eax+0x2C8]
73C95C  0F 9C C0           setl al               ; Weight < MaxWeight
```

仅 **3** 个 E8：`63EBD9` 商店买、`6B768F` 拾取、`6C2EBD` 取仓。调用方把物品重量放进 dx，但被覆盖。

旧 C#：`Weight + nWeight < MaxWeight`（更严）。拾取路径先 `DeleteFromMap` 再判定失败 `Dispose(UserItem)` → **吞物**。

已改为 `Weight < MaxWeight`；拾取改为闸门在 `DeleteFromMap` 之前，失败 `SysMsg("无法再拾取更多物品。")`（`0x6B7868` GBK len=20），不 Dispose。

---

## 3. 各容器容量

| 容器 | 阈值 | 判定点 | C# | 判定 |
|---|---|---|---|---|
| 人物背包 | 48 | `6D0AF9 cmp eax,0x30 / setle`；存档 `6B171B` | `BagCapacity.NativeSlots` / `Of` | FAITHFUL |
| 身上装备 | 16（0..15） | `75EEA9 83 FB 10 / 75 DB` | `HUMAN_EQUIPPED_ITEM_COUNT=16` | FAITHFUL |
| 仓库 | `Count >= SpaceCount` | `74B0A4`：`[list+0xC]+8` vs `[list+8]` / `setge`；SpaceCount 存档 `+0x050E`，夹 24..192，页 48 | `m_nStorageSpaceCount` 夹 24..192 | FAITHFUL |
| 英雄背包（存档） | 40 | `NativeHeroDbFrameCodec.BagItemCount=40` | 同 | FAITHFUL |
| 英雄背包（运行） | C# 10/20/30/35/40（等级） | 加物 `73D0F4` 走 VMT+0x244 | `GetHeroBagCapacity` | **BLOCKED** 等级曲线无独立 VA；运行加物闸是 48 |

---

## 4. 眼神大背包与 `BagCapacity` 三方法

### 容量从哪来

`0x1007E370`：`esi=0x30`，读 `无限背包_是否固定` / `固定格子` / `额外格子`，否则 `GetV(v1,v2)` 默认组 10 下标 1；`test eax,eax / jle` 保持 48，否则 `lea esi,[eax+0x30]`。返回 **48+extra**。

`0x1007EF00`：同样算式，`cmp ebx, 48+extra` / `cmovle` ——「这个下标放不放得下」。

C#：

| 方法 | 对应原生 |
|---|---|
| `NativeSlots` | 存档槽 48（`6B171B` / `6D0AF9` 的 0x30） |
| `Of` | `0x1007E370` 的 48+extra，且 `min(extra, PersistableExtraSlots)`；持久层未接时 extra 不发放 |
| `PersistableOf` | 48+`PersistableExtraSlots`（能落盘多少，不是配置宣称多少） |

`Of <= PersistableOf` 由 `Of` 取小强制。**FAITHFUL**（相对已还原的四份插件算式）。

### 关掉插件会不会丢物品（关键）

1. **原版人物存档只有 48 槽**（`6B171B`）。第 49 件从未进 HumData。关掉插件后原版 M2 **读不到** 扩展格。
2. 扩展格只在 `Gs1\MyJson\bags\<角色名>.bin`（零字节 RLE + 16 字节头 + N×208）。与 HumData **布局不兼容**。
3. 插件 `0x1007ED74 83 7D 10 30 7F 44`：`[ebp+0x10] > 0x30` 则 **跳过** 删 `MyJson\bags\`（串 `0x102BFBC4`）。容量回到 48 时会走进删除臂（`call 0x10233D94`）。**关掉/把额外格子收成 0，插件会删掉 bags 目录 → 扩展格永久丢失。**
4. 前 48 格仍在 HumData，不受影响。
5. C# `PersistableExtraSlots` 默认 0：不发第 49 格，也 **不会** 去删 `.bin`（比插件关开关更保守）。`.bin` 读写层尚未接到上下线（零调用者，除非别处已接）。

**结论：落盘格式与原版不兼容。关插件后扩展格从游戏里消失；插件自己在容量≤48 时还会删 `.bin`。不要在 C# 里复刻「关开关就删目录」。**

---

## 5. 已改

1. `IsAddWeightAvailable`：`Weight < MaxWeight`，忽略 `nWeight`（对齐 `0x73C950`）。
2. 拾取：袋/负重闸在 `DeleteFromMap` 之前；失败发 `无法再拾取更多物品。`；不再 `Dispose(UserItem)`。
3. `Drop39WeightPolarityCheck`：断言改为「不加 nWeight」。
4. 新 `AuditTools/BagCapWeightCheck`：钉 48、Of 闸、1034 无负重、拾取不 Dispose、装备 16、英雄存档 40。

---

## 6. BLOCKED / 未改

| ID | 内容 | 缺什么 |
|---|---|---|
| B-hero-runtime | 英雄运行容量 10/20/30/35/40 的独立 VA | `73D0F4` 只见 VMT+0x244；等级阶梯未找到。C# 更严（≤40），存档 40，fail-closed，未改 |
| B-BagBig2 | MySQL `BagBig2` 行粒度 | 转储 0 条 SQL。本服走 `.bin` |
| B-0x2C | 大背包记录 `+0x2C` dword | 样本像指针，无写入点。透传 |
| Robot pickup | `RobotPlayObject` 负重失败仍 `Dispose(UserItem)` | AI 路径，非 `6B74D8`。未改 |

**1034 未加负重门。**
