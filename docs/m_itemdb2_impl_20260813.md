# 物品数据库与掉落系统对账 — m-itemdb 第二轮 / 2026-08-13

分支 `w/m-itemdb`，工作树 `D:\loym2\.claude\wt2\m-itemdb`。
镜像 `staging/_reunpack_work/flat_image.bin`（ImageBase `0x400000`）。**未执行任何编译命令。**

第一轮（`staging/m_itemdb_20260813.md`，ITEMDB-01..04）已并入 master，本轮不重复其结论，
只复核并补齐它没覆盖的四块：**StdMode 分类与构造器分派**、**堆叠语义的全路径扫描**、
**掉落表的格式/解析/概率/顺序**、**耐久**。

新脚本与原始输出：`staging/_itd2_*.py` / `staging/_itd2_*.txt`。

---

## 0. 摘要

| 项 | 结果 |
|---|---|
| 物品工厂 `sub_74C338` | 全 256×256 (StdMode, Shape) 组合**符号执行求解完毕**，落到 61 个构造器 / 141 个类引用，类名与实例大小从 Delphi VMT 直读 |
| 堆叠类全集 | **15 个**（VMT 父链终于 `TBasePileItem` `0x781C24`），其中 **1 个 StdMode < 150** |
| 判定 | `FAITHFUL` 11、`DIVERGENT` 4（全部已修）、`MISSING` 0、`INVENTED` 0、`BLOCKED` 3 |
| 提交 | `0b7194d5`（ITEMDB-05）、`7f307638`（ITEMDB-06），均未编译 |
| 推翻前人 | 2 处（见 §7） |

---

## 1. `TStdItem` 字段布局

第一轮 §1.2 的 42 项线体/对象映射我逐条对照过通道函数 `sub_7512B4`，**全部成立，不推翻**。
这里只补它没给的三件事。

### 1.1 复核确认的关键偏移（独立第二证据）

第一轮是从 type2 接收器 `sub_7512B4` 的写入侧推的。我从**读取侧**的访问器再核一遍，两条独立：

| 字段 | obj 偏移 | 访问器 VA | 字节 |
|---|---|---|---|
| `Name` | `+0x04` ShortString[15] | `sub_784568` | `784573 8b 53 1c` / `784576 83 c2 04` / `784579 call 0x405774`（`_LStrFromString`，读长度前缀） |
| `Stdmode` | `+0x14` | `sub_784A02` | `784A04 8b 40 1c` / `784A07 8a 40 15` ← 注意这个访问器取的是 **`+0x15` Shape** |
| `Weight` | `+0x1A` | `sub_7845B0` | `7845B0 8b 40 1c` / `7845B3 66 8b 40 1a` |
| `DuraMax` | `+0x1C` | 构造器 | `7837E2 66 8b 46 1c` |
| `NeedConf` | `+0x02`（u16） | 见 §5 | `7837C4 f6 40 03 08` 等 15 个位测试点 |

第一轮那条订正（`byte[std+0x13]` 不是 Weight，Weight 是 `word[std+0x1A]`）**独立复现成立**。

### 1.2 StdMode → 类 → 构造器（本轮新增，全表）

`sub_74C338` 是**物品工厂**，不是「取类别号」的辅助函数。签名：

```
sub_74C338(eax = TUserEngine, edx = TStdItem*, ecx = MakeIndex)
0074C349  85 c9              test ecx,ecx
0074C34B  75 07              jne  0x74C354
0074C34D  e8 7e 17 00 00     call 0x74DAD0          ; ecx==0 -> 生成新 MakeIndex
0074C354  8a 43 14           mov  al, byte[std+0x14]   ; StdMode
0074C35B  81 fa 9f 00 00 00  cmp  edx,0x9F
0074C361  0f 87 17 13 00 00  ja   0x74D67E             ; > 159 走 DEFAULT
0074C367  8a 92 74 c3 74 00  mov  dl, byte[0x74C374 + StdMode]   ; 160 项字节索引表
0074C36D  ff 24 95 14 c4 74  jmp  dword[0x74C414 + cls*4]        ; 44 项跳表
```

每条臂形状一致：

```
push ecx                     ; MakeIndex
mov  dl,1                    ; Delphi 构造标志
mov  eax,[<vmtSelfPtr 槽>]   ; 类引用
mov  ecx,ebx                 ; TStdItem*
call <构造器>
mov  esi,eax
jmp  0x74D6A6                ; return esi
```

第二级几乎都按 `byte[std+0x15]`（Shape）再分。我写了一个 x86 子集解释器
（`staging/_itd2_04_emu.py`）把 256×256 个 (StdMode, Shape) 全跑了一遍，
类名/父类/实例大小从 Delphi 7 VMT 负偏移直读（**`vmtSelfPtr -76` / `vmtClassName -44` /
`vmtInstanceSize -40` / `vmtParent -36`，`vmtParent` 是 `PPClass`，要双重解引用**）。
完整结果在 `staging/_itd2_05_classes.txt`（141 个类引用 + 逐 StdMode 的 shape 区间表）。

**分类语义（按构造器族归纳）**

| 族 | StdMode | 基类 | 说明 |
|---|---|---|---|
| 药品/消耗 | 0（按 Shape 分 18 个子类）、3 的部分 Shape | `TDrug` | 构造器 `0x784EC0`，`byte[obj+0x14] = 1`（`0x784D67`） |
| 装备 | 5,6（`TLWeapon`/`TSpade`）、10,11（衣服）、15（头盔）、16（面罩）、19-21（项链）、22,23（戒指）、24,26（手镯）、25（部分 Shape，护身/毒/魂）、27（腰带）、28（鞋）、29（战鼓）、30（`TRWeapon`）、34（马牌）、7（`TCharm` 族） | `TEquipItem` / `TClothes` / `TEquipBujuk` | 各族独立构造器，实例大小 264/268（基类 256） |
| **堆叠** | **3/Shape 4**、150、151、152、153、154、155（Shape 1-5,7-11）、156（Shape 1,2）、157、158、**160-255** | `TBasePileItem` | 见 §3 |
| 其它道具 | 其余全部 | `TBaseItem` | 构造器 `0x783788`，`byte[obj+0x14] = 0` |
| **不生成物品** | **159（全 Shape）**、31（Shape 0,4,12-14,18-80,151-255）、56（Shape 0,1,4+）、155（Shape 0,6,12+）、156（Shape 0,3+） | — | 臂直接 `jmp 0x74D6A6`，`esi` 仍是 0 → 返回 `nil` |

**DEFAULT 臂 `0x74D67E` 有一道额外的门，这是本轮最重要的分类事实**：

```
0074D67E  3c 96              cmp al,0x96          ; al 仍是 StdMode
0074D680  72 13              jb  0x74D695
0074D682..0074D68C           mov eax,[0x781BD8] ; TBasePileItem
                             call 0x7880F0       ; 堆叠构造器
0074D695..0074D69F           mov eax,[0x77D758] ; TBaseItem
                             call 0x783788       ; 基类构造器
```

即：**跳表落到 cls 0 的 StdMode，≥150 走堆叠、<150 走基类**；StdMode 160-255（超出 160 项
索引表）也统统落这里，因此全部是堆叠。

`obj+0x14` 是**运行期类别字节**，不是存下来的（记录窗口从 `obj+0x20` 起）。取值全集
（全镜像 `mov byte [reg+0x14], imm8` 扫描，reg 域 0..7 全枚举）：

| 值 | 写入点 | 类 |
|---|---|---|
| 0 | `0x7837AE c6 43 14 00` | `TBaseItem` |
| 1 | `0x784D67` / `0x761A96` | `TDrug` 族 / 装备族 |
| 2 | `0x787A70` | `TExpBall` |
| 3 | `0x787C66` | `TWine` |
| 4 | `0x787CB5` | `TDragonSeal` |
| 5 | `0x787CFE` | `TCloseAttrItm` |
| 6 | `0x787F04` | `TForceBall` |
| **7** | `0x788118` + 6 处子类重写 | **堆叠族** |
| 8 | `0x78869E` | `THeroExpBall` |

### 1.3 C# 对照

`GameSvr/Items/NativeItemFactory.cs` 的 `GetClassName` 与原生逐 (StdMode, Shape) 比对，
**唯一差异是 StdMode 5 / Shape 6**：C# 有 `duraMax == 100 → "TBrokenWeapon"`，我的解释器
在那条臂上 bail（它读了 `word[std+0x1C]`，解释器没建模）。属于 C# 建模了我没验的分支，
**不判 DIVERGENT，标 `BLOCKED`（IT2-B1）**。其余 65535 个组合逐一相符 → `FAITHFUL`。

---

## 2. `TUserItem` 208 字节记录

第一轮 §2.3 的 25 个字段组我全部复核，**24 组成立**，1 组语义要订正。

### 2.1 尾部零校验范围复核 —— 当前是对的，不要再动

`GameSvr/DataStores/LegacyUserItem208Codec.cs:173-186`：

```csharp
for (var i = UnownedSpanStart /* 0x56 */; i < record.Length /* 208 = 0xD0 */; i++)
{
    if (i == BindOffset /* 0xB8 */) continue;
    ...
}
```

覆盖 `0x56..0xB7` 与 `0xB9..0xCF`，与任务书给的正确范围**逐字节一致**。`FAITHFUL`，无需改动。
`NativeStallItemRecordCodec` 的第二份拷贝（ITEMDB-04）同样已修。

### 2.2 逐字段（本轮从**访问器侧**重核的部分）

| rec | obj | 宽 | 语义 | 本轮证据（与第一轮的写入侧证据互相独立） |
|---|---|---|---|---|
| `0x00` | `0x20` | u32 | MakeIndex | 构造器 `7837D8 89 43 20`（源是 `[ebp+8]`，即工厂的 ecx 参数） |
| `0x04` | `0x24` | u16 | wIndex | 读访问器 `sub_784560`：`784560 66 8b 40 24` |
| `0x06` | `0x26` | u16 | Dura | 读 `sub_7845A0`：`7845A0 66 8b 40 26`；写 `sub_784584`（**clamp 到 DuraMax**） |
| `0x08` | `0x28` | u16 | DuraMax | 读 `sub_7845A8`：`7845A8 66 8b 40 28`；写 `sub_784598`：`784598 66 89 50 28`（**不 clamp**） |
| `0x14`..`0x15` | `0x34` | u16 | 绑定/锁定字 | 读 `sub_784710 66 8b 40 34` / 写 `sub_784718 66 89 50 34` |
| **`0x18`** | **`0x38`** | **u8** | **暴击等级（订正，见 §7）** | 构造器归零 `7837F5 c6 43 38 00`；加载期校验 `sub_7845B8` |
| `0x27` | `0x47` | u8 | 武器升级标志 | 不变 |
| `0xB8` | `0xD8` | u8 | 赠品 | 构造器归零 `7837EE c6 83 d8 00 00 00 00` |

### 2.3 加载期 sanity 校验 `sub_7845B8`（本轮新增，两条硬契约）

```
007845D0  8b 43 1c           mov eax,[item+0x1C]        ; std
007845D3  8a 40 14           mov al, byte[std+0x14]     ; StdMode
007845D6  04 fb              add al,0xFB                ; -5
007845D8  2c 02              sub al,2
007845DA  73 5c              jae 0x784638               ; StdMode ∉ {5,6} -> 跳到清零
007845DE  8a 43 38           mov al, byte[item+0x38]
007845E1  8b 15 30 68 7d 00  mov edx,[0x7D6830]
007845E7  3b 42 04           cmp eax,[edx+4]
007845EA  7e 50              jle 0x78463C
              ; 超限 -> 日志「异常道具」(0x7846C4) +「暴击等级出错」(0x7846D8)
00784633  88 43 38           mov byte[item+0x38], al    ; 夹到配置上限
00784638  c6 43 38 00        mov byte[item+0x38], 0     ; 非武器 -> 强制 0
0078463C  80 7b 14 07        cmp byte[item+0x14],7      ; 堆叠？
00784640  75 50              jne 0x784692
00784642  66 8b 43 26        mov ax, word[item+0x26]    ; Dura
00784646  66 3b 43 28        cmp ax, word[item+0x28]    ; DuraMax
0078464A  76 46              jbe 0x784692
              ; 日志「异常道具」+「叠加道具持久出错」(0x7846FC)
0078468A  66 8b 43 28        mov ax, word[item+0x28]
0078468E  66 89 43 26        mov word[item+0x26], ax    ; Dura = DuraMax
```

结论两条：
1. **`rec[0x18]` 是暴击等级，只有 StdMode 5/6（武器）能带，其它类别加载时强制清 0**，
   上限来自 `[0x7D6830]+4` 的配置 dword。
2. **堆叠物的 DuraMax 就是堆叠上限**：加载时 `Dura > DuraMax` 会被夹回并记一行日志。

---

## 3. 堆叠语义与全仓构造路径扫描

### 3.1 原生：堆叠 = 类，不是 StdMode 区间

`TBasePileItem.Create` `sub_7880F0`：

```
007880F0  55 8b ec 53 56 ...
0078810D  e8 76 b6 ff ff     call 0x783788              ; 根构造器，Dura = DuraMax = std.DuraMax
00788112  66 c7 46 26 01 00  mov word [esi+0x26], 1     ; Dura = 1，无条件
00788118  c6 46 14 07        mov byte [esi+0x14], 7     ; 堆叠标记
```

任务书给的三个 VA **逐字节复核成立**。另有 6 个子类构造器 chain 它之后**再写一遍**同样两句
（`0x788BD4/0x788C5C/0x78B254/0x78B2B0/0x78B300/0x78B51C`，写入点
`0x788C01/0x788C84/0x78B27C/0x78B2D8/0x78B328/0x78B544`），所以**没有例外**。

按 VMT 父链求解，堆叠类全集 **15 个**：

| 类引用 | 类 | 工厂可达的 (StdMode, Shape) |
|---|---|---|
| `0x781BD8` | `TBasePileItem` | 150, 152(≠16), 153, 157, 158, 160-255 |
| **`0x781CAC`** | **`TLuckOil`** | **3/Shape 4**、154 |
| `0x781D78` | `TPneumaStone` | 155/11 |
| `0x781E4C` | `TTaoFaLingAddExpItem` | 155/10 |
| `0x781F30` | `TGoldAcus` | 151 |
| `0x781FFC` | `TShiMenCall` | 155/3 |
| `0x7820CC` | `TSuperExpItem` | 155/4 |
| `0x7821A0` | `TLevelBuffItem` | 155/5 |
| `0x782274` | `TNewHappyCake` | 155/1 |
| `0x782348` | `THeroJingmaiDrug` | 155/2 |
| `0x782424` | `TPileFlower` | 156/1,2 |
| `0x782920` | `THeroHypericum` | 155/7 |
| `0x7829F4` | `THeroFileDragonScroll` | 155/8 |
| `0x782AD8` | `THeroExpScroll` | 155/9 |
| `0x782D64` | `TJingXiuBook` | 152/16 |

**十五个类的 `[VMT+0x28]` 全是 `0x7882B4`，那是一条裸 `ret`** —— 掉落钩子对堆叠物什么都不做，
Dura 停在构造器的 1。

### 3.2 DIVERGENT 清单（全部已修）

**IT2-01 `DIVERGENT` — `IsPileItem` 用 `StdMode >= 150`，漏掉 `TLuckOil`**（已修，`0b7194d5`）

- 位置：`GameSvr/Items/NativeItemFactory.cs:243`（原 `item.StdMode >= 150 && GetClassName(item) != null`）
- 原生：`0074CCE2 51 / B2 01 / A1 AC 1C 78 00 / 8B CB / E8 FF B3 03 00` ——
  **StdMode 3 / Shape 4 直接调堆叠构造器 `0x7880F0`**
- 玩家可见后果：幸运油类物品经**任何**发放路径（制造、NPC 给予、脚本 give、GM `@MakeItem`、
  商城、邮件附件、魔塔奖励、新手礼包…33 个调用点）都会发出 `DuraMax` 个而不是 1 个 —— **刷物点**。
  同时 `NativeItemPlus28.ApplyOnDrop` 会落到 `default:` 分支多抽一次 `Random(80)` 并覆盖 Dura，
  **既刷物又打乱 RNG 序列**。
- 修法：改成「类名属于 15 个堆叠类之一」

**IT2-02 `DIVERGENT` — 掉落控制（段 3）手搓 `Dura = DuraMax`**（已修，`7f307638`）

- 位置：`GameSvr/Maps/NativeDropControlRuntime.cs:228`
- 原生段 3 走 `0x71FEC8 call 0x752CAC` 取表，再 `0x71FF24 call 0x74DE54`
  （`MakeItemByName` = `sub_74C2D4` 查名 + `sub_74C338` 工厂），**由类构造器定种子**
- C# 手搓 `Dura = stdItem.DuraMax`，而 `InitializeForDrop` 恰恰因为「堆叠物不掷耐久」提前 return，
  **修不回来**
- 后果：地图/全服掉落控制里配置的堆叠物，每次掉落给 `DuraMax` 个

**IT2-03 `DIVERGENT` — 勋章铸造手搓 `Dura = DuraMax`**（已修，`7f307638`）

- 位置：`GameSvr/Players/TPlayObject.NativeMedal.cs:104`
- 勋章下标 311-330 / 697-701 / 4335-4339 在现网目录里都不是堆叠类，**当前 0 触发**；
  作为同类缺陷一并收口

**IT2-04 `DIVERGENT` — `MonItems` 行过滤用「以 `;` 开头」，原生是「整行等于 `;`」**（已修，`0b7194d5`）

- 位置：`GameSvr/LocalDB.cs:466`
- 原生 `sub_6799E0`：`0x679AE4 call 0x40C140`（Trim，`0x40C171`/`0x40C193` `cmp byte,0x20 / jbe`）
  → `0x679AE9 cmp [ebp-0x14],0 / je`（空行）
  → `0x679AF6 mov edx,0x679CD4`（长度前缀 1 的 `";"`）→ `0x679AFB call 0x40591C`（`_LStrCmp`）
  → `0x679B00 je`（**整行相等才跳过**）
- 生产实测：363 个文件 14848 行，`;` 开头的只有 4 行（全是韩文残留注释，物品名查不到 StdItem，
  原生同样丢弃），首尾带空白的 0 行 → **本次改动对该部署零可观测差异**

### 3.3 全仓构造路径扫描结果（任务 3 的正式答复）

C# 里能造出一个新 `TUserItem` 并给 Dura 赋初值的路径**只有三条**：

| # | 路径 | 状态 |
|---|---|---|
| 1 | `UserEngine.CopyToUserItemFromName`（**33 个调用点**：掉落 `MonGetRandomItems`、MonItemsTree 链、NPC 脚本 give、`NormNpc.GotoLable`、制造 `NativeMakeItemUseDiamHost`、GM `@MakeItem`、商城 `MallManager`、邮件 `MailService`、魔塔奖励、新手礼包/金币礼包、头盔、英雄、机器人、AI 配置、挖矿五种矿石、眼神 API ×4） | 修 IT2-01 后**全部正确** |
| 2 | `NativeDropControlRuntime.CreateNativeItem` | IT2-02 修好 |
| 3 | `TPlayObject.NativeMedal.TryCreateNativeMedal` | IT2-03 修好 |

其余 `new TUserItem(...)` 全是**拷贝构造**（存档解码、摆摊拆分、邮件附件、交易快照）或死赋值
（`RobotPlayObject.cs:1680` 紧接着被覆盖），不铸造新数量。

**结论：修完这三条之后，全仓已无「按 `DuraMax` 发放堆叠物」的刷物点。**

顺带一条对既有代码的肯定：`NativeDropControlRuntime.UsesPileInitialization` 里那份 15 名单
**本来就是对的**，比 `IsPileItem` 更准 —— 同一条原生事实有两个 C# 权威（规则 §4.18），
本轮已收敛成一个。

---

## 4. 掉落表

### 4.1 配置格式与解析（`sub_6799E0`，唯一调用者 `0x6799A3`）

文件：`<EnvirDir>\MonItems\<怪物名>.txt`（目录字面量 `0x679CB0`，扩展名 `0x679CC4`）。
每行 `Rate/Nth<分隔>物品名<分隔>数量`。

> 注意镜像里有**两份**同形状的加载器：`sub_60593C`（唯一调用者 `0x713A34`）和
> `sub_6799E0`（唯一调用者 `0x6799A3`，重建 `monInfo[0x48]`，正是运行期
> `mon[0x474]` 的来源）。**掉落走的是后者**，前一份不要拿来当证据。

分词器 `sub_4C6BA4`（`GetValidStr3`），栈参 `[ebp+0xC]` 是**分隔符个数减一**
（`0x4C6BF0 mov eax,[ebp+0xC]` / `inc eax`）：

| 字段 | 分隔符 | VA |
|---|---|---|
| 1、2 | `{09, '/', 20}` 三个（`push 2`） | `0x679B06-0x679B21` / `0x679B31-0x679B4C` |
| 3、4 | `{09, 20}` 两个（`push 1`） | `0x679B5C-0x679B73` / `0x679B83-0x679B9A` |

**物品名可以含 `/`，不能含空格或 TAB；名字不做去引号处理。**

记录 32 字节（`0x679BC7 mov eax,0x20` → `SysGetMem`）：

| off | 来源 | VA |
|---|---|---|
| `0x00` | `ShortString[15]` 物品名（`_PStrNCpy`，`cl=0x0F`） | `0x679C01` |
| `0x10` | `StrToIntDef(tok1, 1) - 1` ← **减一** | `0x679C06` / `0x679C13 48 dec eax` / `0x679C17` |
| `0x14` | `StrToIntDef(tok2, 1)` | `0x679C1A` / `0x679C2A` |
| `0x18` | `TStdItem*`（`0x679BB4 call 0x74C2D4` 按名查表） | `0x679BDA` |
| `0x1C` | `StrToIntDef(tok4, 1)`（**缺省 1**） | `0x679C2D` / `0x679C3D` |

行接受条件**只有一条**：物品名能查到 `TStdItem`（`0x679BC1 cmp [ebp-0x2C],0 / je` →
`0x679BDD cmp [ebp-8],0 / je 0x679C4E`）。**没有任何 `Rate>0` / `Nth>0` 的数值门。**

C# `GameSvr/LocalDB.cs:447 LoadMonitems` 逐项对上 → `FAITHFUL`（`;` 那条已按 IT2-04 修）。

### 4.2 概率

```
0071FD34  8b 45 e4        mov eax,[node]
0071FD37  8b 40 14        mov eax,[node+0x14]     ; Nth
0071FD3A  f7 6d d4        imul dword [ebp-0x2C]   ; × 惩罚倍数（1 或 2）
0071FD3D  e8 0a 3e ce ff  call 0x403B4C           ; Random(Nth × mult)
0071FD42  8b 55 e4        mov edx,[node]
0071FD45  3b 42 10        cmp eax,[node+0x10]     ; Rate-1
0071FD48  0f 8f 51 01 00 00  jg 0x71FE9F          ; 大于则跳过
```

即命中条件 **`Random(Nth × mult) <= Rate-1`**，等价于 `mult==1` 时的 `Random(Nth) < Rate`。
`1/100` 就是精确的 1%。**分母是 `Nth`，比较方向是 `<=`（对 `Rate-1`），没有保底。**

惩罚倍数 `[ebp-0x2C]`：默认 1，`0x71FB1E cmp byte[killer+0x1828],2 / jne` 时置 2
（防沉迷二档 → 概率减半、金币减半）。注意它**同时**乘进分母又在结算时
`0x71FFD1 idiv dword[ebp-0x2C]` 除金币，两处都要有。

C# `UsrEngn.cs:2472`：`Random(MonItem.MaxPoint * penalty) <= MonItem.SelPoint`，
`SelPoint = n18 - 1` → `FAITHFUL`。

### 4.3 数量与金币

- **非金币行的 `Count`（`node[0x1C]`）在 MonItems 主表里根本不读** —— 命中一次就造一件
  （`0x71FD53..0x71FD8E` 没有循环）。生产 14848 行里 14616 行本来就没写第四列。
- **金币行**（判据是 `0x71FD5D cmp word ptr [std],0` —— **StdItem 的 wire index == 0**，
  不是按名字比对）：
  ```
  0071FD66  8b 78 1c   mov edi,[node+0x1C]     ; N
  0071FD6B  e8 ..      call 0x403B4C           ; Random(N)
  0071FD70  d1 ff      sar edi,1
  0071FD72  79 03 / 83 d7 00   jns / adc edi,0 ; N div 2，朝零取整
  0071FD77  03 c7      add eax,edi
  0071FD79  01 45 ec   add [ebp-0x14],eax      ; 累加到共享金币池
  ```
  C# `Count / 2 + Random(Count)` —— 只有一次抽签，`+` 从左到右求值不影响 RNG 序列，
  C# 整数除法与 `sar+jns+adc` 同为朝零取整 → `FAITHFUL`。
  唯一形式差异：C# 用 `ItemName == "金币"` 而原生用 `wireIndex == 0`。给定现网目录里
  金币就是 index 0，两者同解；`LocalDB.cs:545 ResolvesToStdItemName` 的注释已经把这条说清楚了。

### 4.4 多段顺序（`sub_71FA20`，共 4 段 + 收尾）

| 段 | 范围 | 内容 |
|---|---|---|
| 门 | `0x71FA50 cmp byte[mon+0x47F],0 / jne 0x720092` | 重入锁，`0x71FA6C` 置 1 |
| 门 | `0x71FA8A cmp dword[mon+0x474],0 / je 0x720092` | **没有掉落表 → 整个函数直接退出**，后面三段和金币结算全都不跑 |
| 门 | `0x71FADA/0x71FAE3 cmp byte[killer+0x1828 / +0x1829],3` 或 `0x71FAEE call 0x6D7788` | 命中 → 发 ident `0xA2` 提示后整体中止 |
| **1** | `0x71FB2E..0x71FCFF` | **MonItemsTree 独占链**（`0x71FB49 call 0x67B2B0` 按怪名取链头）。每节点：金币臂 = `Random(N)+N/2` 后 **`0x71FB8D jmp 0x71FCFF` 直接终止整条链**；物品臂循环 `node[0x1C]` 次，落地半径 **5** |
| **2** | `0x71FCFF..0x71FEA1` | **怪物自己的 MonItems 表**，逐行独立掷。落地半径 **3** |
| **3** | `0x71FEA7..0x71FFA6` | **掉落控制**（`0x71FEC8 call 0x752CAC`），按返回的次数×条目走 `0x74DE54 MakeItemByName` |
| **4** | `0x71FFAD..0x720047` | **金币结算**：`jle` 则跳过；`[ebp+8]` 为真时上限 `0xBB8`(3000)；`idiv` 惩罚倍数；每堆最多 `0x7D0`(2000)，最多 **16** 堆（`esi=0x10`） |
| 收尾 | `0x720049..0x720072` | `@AfterScatterItems` 脚本回调（字面量 `0x720158`） |

C# `TBaseObject.Base.cs:1187-1234` 的顺序与门位置与之相符 → `FAITHFUL`。
段 1 的半径 5 / 段 2 的半径 3 也已在 `UserEngine.MonItemsTree.cs:30` 钉住。

### 4.5 RNG 抽签次数

一次怪物死亡的抽签序列（`mult=1`）：

```
段1: sub_67B2B0 内部选链（本轮未展开，见 BLOCKED IT2-B2）
     每个物品节点：[VMT+0x28] 钩子（堆叠 0 次 / 基类 1 次 / 装备族更多）× RepeatCount
     金币节点：Random(N) × 1，然后整条链终止
段2: 每行 1 次 Random(Nth) 判定；命中且非金币 → [VMT+0x28] 钩子；命中且金币 → Random(N)
段3: 掉落控制的 counted 规则各自 Random(PeriodOrRange)；每件 → [VMT+0x28] 钩子
段4: 金币结算 0 次
```

`[VMT+0x28]` 各族的抽签次数由另一位代理负责（`NativeItemPlus28.cs`）。我只核了两件事，
都成立：**堆叠族 `[VMT+0x28] == 0x7882B4` 是裸 `ret`（0 次）**；基类 `sub_783EFC` 恰好 1 次
`Random(80)`。

---

## 5. 物品定义侧控制「极品」的字段位

**极品门就是 `NeedConf`（`TStdItem` obj `+0x02`，u16）的 bit `0x0040`。**

六条装备族 `[VMT+0x28]` 覆盖里各有一处，字节完全一致：

| 类 | 覆盖 VA | 门 |
|---|---|---|
| `TLWeapon`/`TSpade` | `0x7608D4` | `0x760904 f6 40 02 40` |
| 衣服族 | `0x7639DC → 0x783F40` | `0x783F4A f6 40 02 40` |
| `THelmet` | `0x7611C8` | `0x76120F f6 40 02 40` |
| `TNecklace` | `0x76178C` | `0x7617C6 f6 40 02 40` |
| `TRing` | `0x761CC4` | `0x761D12 f6 40 02 40` |
| `TArmRing` | `0x7625BC` | `0x76260A f6 40 02 40` |

**判定顺序很要紧**（武器臂为例）：

```
007608E2  call 0x783EFC          ; 先掷耐久 Random(80)
007608E7  84 db / 75 ..          ; edx(bl) != 0 -> 整段跳过（只有掉落路径传 edx=0）
007608EF  b8 0a 00 00 00
007608F4  call 0x403B4C          ; Random(10)  ← 无条件抽，在读 NeedConf 之前
007608F9  85 c0 / 75 ..
00760901  8b 47 1c               ; std
00760904  f6 40 02 40 / 74 ..    ; NeedConf & 0x40
```

**`Random(10)` 在 NeedConf 判定之前抽**，所以「不能出极品的物品」照样消耗那一次抽签。
C# `NativeItemPlus28.cs:87` 写作 `if (Random(10) != 0 || !HasExtraAttrFlag(std)) return;`，
`||` 短路从左到右 → 抽签次数与顺序一致，`FAITHFUL`。`ExtraAttrFlag = 0x40` 作用在
`GoodItem.NativeReserved02`（= NeedConf）上，也对。

**顺带扫全了 `NeedConf` 的 16 个位在镜像里的测试点**（`test byte [reg+2/3], imm8`，
`/0` reg 域，范围 `0x600000..0x7A0000`，共 60 处），已定语义的：

| 位 | 语义 | 决定性证据 |
|---|---|---|
| `0x0040` | **允许随机极品属性** | 上表六处 |
| `0x0800` | **构造时自动绑定** | `0x7837C4 f6 40 03 08` → `dx=1` → `0x784718 mov word[obj+0x34],dx` |
| `0x8000` | **时效物品**（`btValue[0..7]` 变成一个 `TDateTime`） | `sub_7849E4`：`0x7849EB f6 42 03 80 / setne al`；构造器 `0x7837FB` 调它，真则把 `[0x7D6A88]` 的 8 字节写进 `obj+0x2A` |
| `0x4000` | 与绑定并列的第二个「锁」谓词 | `sub_784720`：`0x784723 f6 40 03 40 / setne al` |
| `0x0020`/`0x0080`/`0x0200`/`0x0400` | 「能否移动/交易/丢弃/入库/出售」判定树的分支（`sub_78389x`，返回 0..6 的拒绝码） | `0x78396E` / `0x78395E` / `0x78391D`+`0x783933`+`0x78394C` / `0x783955` |

其余位（`0x0001/0x0002/0x0004/0x0008/0x0010/0x0100/0x1000/0x2000`）有测试点但本轮未定语义，
见 BLOCKED IT2-B3。

---

## 6. 耐久

### 6.1 原语（全部是单指令访问器，可直接当契约用）

| 操作 | VA | 字节 | 行为 |
|---|---|---|---|
| `GetDura` | `0x7845A0` | `66 8b 40 26` | 读 `word[item+0x26]` |
| **`SetDura`** | `0x784584` | `66 8b 48 28 / 66 3b d1 / 76 05 / 66 89 48 26 / c3 / 66 89 50 26` | **写之前先 clamp 到 DuraMax** |
| `GetDuraMax` | `0x7845A8` | `66 8b 40 28` | 读 `word[item+0x28]` |
| **`SetDuraMax`** | `0x784598` | `66 89 50 28` | **不 clamp** |

### 6.2 上限来自哪里

| 阶段 | 值 | 证据 |
|---|---|---|
| 构造（所有非堆叠类） | `Dura = DuraMax = std.DuraMax` | `0x7837E2 66 8b 46 1c` / `E6 66 89 43 26` / `EA 66 89 43 28`（**同一个 `ax` 写两处**） |
| 构造（堆叠类） | `Dura = 1`，`DuraMax` 保持 `std.DuraMax`（= 堆叠上限） | `0x788112` |
| 掉落（基类） | `Dura = ROUND(DuraMax / 100.0 × (20 + Random(80)))` | `sub_783EFC`：`0x783F05 mov eax,0x50` / `0x783F0A call Random` / `0x783F0F add eax,0x14` / `0x783F22 fdiv [0x783F38]`（实测 **100.0f**）/ `0x783F28 fmulp` / `0x783F2A call 0x403574` = **@ROUND 半偶入**，不是截断 |
| 掉落（极品加成） | 装备族命中极品体时**同时抬高 Dura 与 DuraMax**，上限 65000 | `NativeItemPlus28.AddDura`（另一代理领域） |
| 掉落（堆叠） | 不动 | `[VMT+0x28] = 0x7882B4` 裸 `ret` |
| 加载 | 堆叠物 `Dura > DuraMax` → 夹回 + 日志 | `sub_7845B8` @`0x784642..0x78468E` |

### 6.3 消耗与损坏（武器为例）

```
0073E829  0f b7 73 26        movzx esi, word[weapon+0x26]
0073E82D  85 f6 / 0f 8e ..   test esi,esi / jle 退出        ; Dura<=0 什么也不做
0073E838  db 45 f4           fild  [ebp-0xC]                ; 旧 Dura
0073E83B  d8 35 d0 e8 73 00  fdiv  [0x73E8D0]               ; 实测 1000.0f
0073E841  e8 2e 4d cc ff     call  0x403574                 ; @ROUND -> 旧「可见耐久」
0073E849  2b 75 fc           sub   esi,[ebp-4]              ; 扣量
0073E84C  85 f6 / 7f 3c      test esi,esi / jg 0x73E88C
0073E850  66 c7 43 26 00 00  mov   word[weapon+0x26], 0     ; 归零 = 损坏
0073E85C  e8 17 06 02 00     call  0x75EE78                 ; 卸下
0073E865  ff 92 8c 00 00 00  call  [vmt+0x8C]               ; 重算属性
0073E87D  66 b9 8d 27        mov   cx, 0x278D               ; SM 10125，带 DuraMax/Dura/1
0073E88C  66 89 73 26        mov   word[weapon+0x26], si    ; 否则写回
```

**可见耐久 = `ROUND(Dura / 1000.0)`（半偶入，不是截断）**，内部单位是千分之一点。
`≤ 0` 才算损坏（`jg` 走保留分支）。C# `TBaseObject.cs:3242-3268` 与
`TBaseObject.cs:1904/2724/5798/5833` 的 `HUtil32.Round(nDura / 1000.0)` 形状与之一致。

### 6.4 未在本轮验证的部分（诚实标注）

修理（`Merchant.cs:2489/2496` 的 `Dura = DuraMax`、`nRepairItemDecDura=30`）、
各装备槽消耗率差异、`boDecLampDura` / `HPStoneDecDura` / `MPStoneDecDura` 这些配置项的
原生消费点，本轮**没有逐字节核**。已有的 C# 注释里带 VA 的部分未经我复核，按规则不背书。

---

## 7. 推翻/订正前人结论

**① `staging/m_itemdb_20260813.md` §2.3：`rec[0x18..0x1B]`「保留，恒 0」——语义错了。**
`rec[0x18]`（= `obj+0x38`）是**暴击等级**：加载期校验 `sub_7845B8` 对 StdMode∈{5,6} 拿它跟
配置 `[0x7D6830]+4` 比（`0x7845E7 3b 42 04`），超了就记「异常道具 / 暴击等级出错」
（字面量 `0x7846C4` / `0x7846D8`）并夹回（`0x784633 88 43 38`）；非武器则强制清零
（`0x784638 c6 43 38 00`）。金语料 1363 条全 0 是**该语料没有带暴击的武器**，不是「保留字段」。
布局不变，但「恒 0」这个描述会误导后来者去删掉这个字节的处理。

**② `staging/m_itemdb_20260813.md` §2.2：「`obj+0x14` 由 StdMode 派生，`sub_74C338` 是查表」——
描述不完整，会低估这个函数。** `sub_74C338` 是**物品工厂本身**（唯一的物品对象创建入口，
8 个调用点：`0x74C328` / `0x74DB2A`（存档加载）/ `0x74DE77`（按名创建）/ `0x74E20B` /
`0x74E2BE` / `0x74E36E` / `0x750C05` / `0x751BC6`（掉落）），第二级还按 **Shape** 分派，
且 DEFAULT 臂 `0x74D67E` 自带 `cmp al,0x96 / jb` 的堆叠/基类二分。把它当「派生 obj+0x14」看，
就发现不了 IT2-01。

**③ `AuditTools/InProcEngineRunCheck/Program.cs:919` 与 `:972/:984` 的断言文案说
「`+0x14 == 7` 的集合恰好是 StdMode>=150」——不成立**（`TLuckOil` 反例）。
断言本身仍然通过（fixture 是 StdMode 150），我**没有改** AuditTools，只在此登记文案已过时。

---

## 8. BLOCKED

**IT2-B1 `TBrokenWeapon`（StdMode 5 / Shape 6 / DuraMax==100）那条臂。**
缺：`0x74CE16` 类 06 臂里 shape 6 分支的完整反汇编。我的符号解释器在那里 bail（它要读
`word[std+0x1C]`，解释器只建模了 `+0x14`/`+0x15`）。C# 已经建模了这个分支，看起来合理，
但我没有字节复核，**不判 FAITHFUL 也不判 DIVERGENT**。补法：手工反汇编
`0x74CE1A..0x74CE88` 那一段。

**IT2-B2 MonItemsTree（段 1）的选链算法 `sub_67B2B0`。**
本轮只确认了「它按怪名返回一条**已经选好的**链表，调用方边走边 `FreeMem(0x24)`」，
概率/权重在函数内部，未展开。C# `MonItemsTreeLoader` / `TraverseMonItemsTree` 的
**遍历**侧我核过（金币臂终止、半径 5、`RepeatCount` 循环都对），**选取**侧未核。
补法：反 `sub_67B2B0` 与 `sub_67AEC0`（加载器），对齐 `MonItemsTree.txt` 的列语义。

**IT2-B3 `NeedConf` 剩余 8 个位的语义。**
`0x0001/0x0002/0x0004/0x0008/0x0010/0x0100/0x1000/0x2000` 在镜像里都有测试点
（`staging/_itd2_22.txt` 列了全部 60 处），但要逐个跟到消费者才能命名。
另：我尝试从生产 `MySQL/data/mir3/stditems.MYD` 统计各位的使用率，**MyISAM 行长猜错，
解析结果是垃圾（出现 4437 行 StdMode 32、名字全是空格），已作废，本报告不引用任何来自它的数字**。
补法：读 `.frm` 拿准确列宽，或直接起 MySQL 导出。

---

## 9. 建议优先级

| 序 | 项 | 理由 |
|---|---|---|
| 1 | **IT2-01（已提交）** | 刷物点，且同时污染掉落 RNG 序列；影响面是全部 33 个发放路径 |
| 2 | **IT2-02（已提交）** | 同类刷物点，触发条件是「掉落控制里配了堆叠物」 |
| 3 | IT2-04（已提交） | 忠实度提升，实测零可观测差异，可放心合 |
| 4 | IT2-03（已提交） | 现网 0 触发，属类别收口 |
| 5 | IT2-B2 | MonItemsTree 是独占爆，选链算法不对会直接体现在爆率上；建议单开任务 |
| 6 | IT2-B1 / IT2-B3 | 补证据，不阻塞 |

---

## 附：本轮脚本

| 文件 | 内容 |
|---|---|
| `_itd2_01_class.py` / `_itd2_02_arms.py` | `sub_74C338` 分发器、160 项索引表、44 项跳表 |
| `_itd2_04_emu.py` | x86 子集符号解释器，求解全部 256×256 组合 |
| `_itd2_05_classes.py` | Delphi VMT 类名/父类/实例大小解析 + 逐 StdMode 类表 |
| `_itd2_07_ctors.py` / `_itd2_10_pilector.py` | 基类/堆叠/药品/装备构造器 |
| `_itd2_08_pile.py` / `_itd2_27_vmt28.py` | 堆叠族父链求解、`[VMT+0x28]` 全查 |
| `_itd2_09_marker.py` | `obj+0x14` 全编码读写扫描 |
| `_itd2_11..14` | `sub_71FA20` 四段掉落、`sub_74DE54`、`sub_6799E0` 解析器 |
| `_itd2_18/20/23/24` | Delphi RTL 助手、`NeedConf` 位测试上下文、加载期校验、耐久访问器 |
| `_itd2_21_prodscan.py` | 363 个生产 MonItems 文件的边界情况普查 |
| `_itd2_22_needconf.py` | `NeedConf` 16 位在镜像里的全部 60 个测试点 |
