# 三条残口收口 — DROP-33 own-table 腿 / SPWN-56 视野谓词 / POIS-11　2026-08-14

- 工作树：`D:\loym2\.claude\wt2\drop-view`　分支：`w/drop-view`　基线：`80c18a75`
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase=0x400000），capstone 5.0.7 独立反汇编
- 来源：`docs/eqv_shard19_20260814.md`（DROP-33）、`docs/eqv_shard25_20260814.md`（SPWN-56）、`docs/eqv_shard09_20260814.md`（POIS-11）
- 铁律：所有判定均附 VA + 字节 + 反汇编；无确凿证据 fail-closed；热点/牵连过广只报不改

> **结论**：DROP-33 已外科修复并落地；SPWN-56 复核确认 DIVERGENT 且**根因比原判定更深**（不是谓词写错，是托管对象模型缺失原生谓词赖以工作的拆解信号），维持只报不改；POIS-11 复核后**上修为 FAITHFUL**——分片里"结构 fork 仍存"的注记已过时，那个并行存储早已删除。

---

## 1. DROP-33 own-table 腿 —— **已修（FIX）**

### 1.1 原生语义

`sub_71FA20` 段2（怪物自有掉落表，`0x71FCFF`–`0x71FEA1`）的形状：

```
71FCFF  8B 45 FC              mov eax,[ebp-4]              ; self
71FD02  8B 80 74 04 00 00     mov eax,[eax+0x474]          ; 掉落表 TList
71FD08  8B 58 08              mov ebx,[eax+8]              ; count
71FD0E  0F 8C 93 01 00 00     jl  0x71FEA7                 ; 空表 -> 段3
71FD22  E8 25 50 D0 FF        call 0x424D4C                ; TList.Get(i) -> rec
71FD37  8B 40 14              mov eax,[rec+0x14]           ; MaxPoint
71FD3A  F7 6D D4              imul [ebp-0x2C]              ; * 防沉迷倍率
71FD3D  E8 0A 3E CE FF        call 0x403B4C                ; Random
71FD45  3B 42 10              cmp eax,[rec+0x10]           ; SelPoint
71FD48  0F 8F 51 01 00 00     jg  0x71FE9F                 ; 即 <= 才过
71FD89  E8 2E 1E 03 00        call 0x751BBC                ; CopyToUserItemFromName
71FDA2  FF 51 28              call [item.vmt+0x28]         ; 耐久初始化
71FDA5  80 7D 0C 00 / 74 7F   cmp byte[ebp+0xC],0 / je 0x71FE2A
```

两条落地臂，半径都是立即数 **3**：

```
; 臂A（[ebp+0xC]≠0，"(爆天赐)" 全服播报，0x720100 len=8 GBK "(爆天赐)"）
71FDCF  B9 03 00 00 00        mov ecx,3
71FDDA  E8 C1 8A 04 00        call 0x7688A0
; 臂B（0x720120 len=9 GBK "怪物死亡:"）
71FE46  B9 03 00 00 00        mov ecx,3
71FE51  E8 4A 8A 04 00        call 0x7688A0
```

**ecx 就是半径**，链条闭合：`sub_7688A0` 序言 `0x7688B4 8B D9 mov ebx,ecx` → `0x768907 53 push ebx` → `sub_768688` 的 `[ebp+0x10]` → `0x7686B5 8B 45 10 mov eax,[ebp+0x10]` / `0x7686BA 0F 8E A6 00 00 00 jle 0x768766`，即那圈求空地的环界。

**关键结构事实**：段2 **就地落地**，不经任何背包。`sub_71FA20` 全函数（0x71FA20–0x720098）只读 `[self+0x474]`（`0x71FA8A` / `0x71FD02` / `0x71FD1A` 三处），**零次**读背包 `[self+0x508]`（背包偏移由 `sub_740078 @0x7400C7 8B 86 08 05 00 00` 坐实）；怪物 `Die`（`sub_71E2BC`，0x71E2BC–0x71E480）同样零次。**原生怪物死亡根本没有"散背包"这一步。**

### 1.2 C# 侧现状（改前）

`UserEngine.MonGetRandomItems`（`UsrEngn.cs:2480-2535`）不落地，而是 `mon.m_ItemList.Add(UserItem)`（2529）；再由 `TBaseObject.Base.cs:1254` 的 `ScatterBagItems` 统一落地，半径 `DropWide = _MIN(g_Config.nDropItemRage, 7)`。即段2 的落地半径被换成了配置值。

### 1.3 该配置旋钮无原生依据

| 检查 | 结果 |
|---|---|
| `DropItemRage` / `nDropItemRage` / `DropItemRange` 全镜像 ASCII（大小写不敏感） | **0 命中** |
| 同上 UTF-16LE | **0 命中** |
| 全镜像 15 个 `sub_7688A0` 调用点的半径 | 只有 `0x64E79D`（`sub_64E6F4`，脚本按名掉物，`edx` 名与 `0x64E7E0` "金币" 比对后走 `sub_768AAC`）用 `mov ecx,[ebp-8]` 取调用方参数；其余 14 处全是立即数（2/3/4/5） |

判据强度与代码库既有先例一致——`TPlayObject.ScatterBagItems` 里删掉 `DieScatterBagRate` 用的就是同一套三编码零命中论证。

### 1.4 全镜像半径普查（本轮取证副产物）

| 调用点 | 所属函数 | 半径 | 用途 |
|---|---|---|---|
| `0x71FC0D` / `0x71FC84` | `sub_71FA20` 段1 | 5 | MonItemsTree 专属链 |
| `0x71FDDA` / `0x71FE51` | `sub_71FA20` 段2 | **3** | **怪物自有掉落表（本条）** |
| `0x71FF48` | `sub_71FA20` 段3 | 3 | 世界掉落（前代理已修 4→3） |
| `0x72000A` | `sub_71FA20` 金币 | 3 | `sub_768AAC` |
| `0x71F7DB` | `sub_71F740` | 3 | "(爆天赐)" 另一支 |
| `0x73FEFE` | `sub_73FC70` | 2 | 玩家死亡掉落（"死亡爆出-"） |
| `0x740236` | `sub_740078` | 2 | 玩家死亡散包 |
| `0x740482` | `sub_740300` | 2 | TSpecialDropItem worker |
| `0x748DE2` | `sub_748D48` | 2 | 按图配额 worker |
| `0x72021D` | `sub_72016C` | 4 | 堆叠拆分落地 |
| `0x682CE3` | `sub_682CA4` | 2 | "朱火碎片" |
| `0x64E79D` | `sub_64E6F4` | `[ebp-8]` | 脚本按名掉物（唯一调用方传参） |

### 1.5 修复与隔离性

- 文件：`GameSvr/Actors/TBaseObject.Base.cs`
- 私有 `ScatterBagItems(TBaseObject, IList<…>)` 增第三形参 `int DropWide`；`TBaseObject.Base.cs:1254`（怪物腿）传新增常量 `NativeMonsterOwnTableScatterRange = 3`（带上述字节证据注释）。
- 1-arg 虚入口 `ScatterBagItems(ItemOfCreat)` 保持 `_MIN(nDropItemRage,7)` 原状。
- **玩家路径零触碰**：`TPlayObject.Base.cs:2212` 的 override 自带 `const int DropWide = 2`（对上原生 `sub_740078 @0x740236 mov ecx,2`），走的是虚派发，与本次改动的私有 3-arg 重载无关。
- **英雄/宝宝零触碰**：1254 上游门是 `m_Master == null`，英雄（`RC_HEROOBJECT=54`，`HeroObject : AnimalObject`）与召唤宝宝在世时 `m_Master != null`，本就不到这条腿。
- **默认配置逐位不变**：`GameSvrConfig.cs:1453 nDropItemRage = 3`，默认下新旧同值；只有运营改过该值的服会被拉回原生。

**提交**：`20690091`　`dotnet build GameSvr`：**0 错**（17 警告，皆为改前既有）。

### 1.6 顺带发现（只报，不在本条范围）

1. **怪物"背包散落"整体无原生对应**（见 1.1）。C# 在 1254 把怪物 `m_ItemList` 整体散掉；对段2 攒进来的物件这是正确的落地通道，但对怪物**原本就携带**的背包物件（`AddItemToBag` 等途径）则是净额外掉落——原生 `sub_71FA20` 一次都没读过 `[self+0x508]`。要收口就得把段2 改成"边掷边落"、并把背包散落整条删掉，牵动掉落限制表（`g_MonDropLimitLIst`）、`scatteredItems` 键值与落地次序，超出外科范围。
2. `TBaseObject.DropGoldDown`（`TBaseObject.cs:1486`）里的 `DropWide` 是**死局部**——算出来后 1496 行直接硬编码 3 给 `GetDropPosition`。无行为影响，未动。

---

## 2. SPWN-56 视野谓词 —— **DIVERGENT，只报不改（fail-closed）**

> **2026-08-14 已被 `docs/view_searchrange_predicate_20260814.md` 取代（本节结论已推翻）。**
> 本节 §2.3 的「照搬即净回归」只对**替换**成立，对**并联**不成立：`age>=60s || !Valid`
> 是单调的——`Valid` 为真时行为逐位不变，60 秒 GC 完整保留；`Valid` 为假时才新增摘链，
> 而那正是原生 `0x77A2EB` 要做的事。谓词已按并联落地（`w/view56`），并接入全部 4 个拷贝。
> 另：本节把 C# 的 `m_boDeath` 排除在分歧点之外是对的，但没指出 `TPlayObject.SearchViewRange`
> 这个真正跑玩家路径的 override 压根没有 `m_boDeath`。

### 2.1 原生语义（本轮独立复核，且比分片记载更明确）

`sub_765D64` 逐字节：

```
765D64  55 8B EC 53 56           push ebp / mov ebp,esp / push ebx / push esi
765D69  8B F0                    mov esi,eax
765D6B  33 DB                    xor ebx,ebx
765D6D  80 BE 06 01 00 00 00     cmp byte [esi+0x106],0     ; Length(CName)
765D74  74 17                    je  0x765D8D
765D76  83 BE 28 01 00 00 00     cmp dword [esi+0x128],0    ; PEnvir
765D7D  74 0E                    je  0x765D8D
765D7F  8B 86 28 01 00 00        mov eax,[esi+0x128]
765D85  83 78 44 00              cmp dword [eax+0x44],0     ; PEnvir.MapName
765D89  74 02                    je  0x765D8D
765D8B  B3 01                    mov bl,1
765D8D  8B C3 5E 5B 5D C3        mov eax,ebx / pop / pop / pop / ret
```

三个槽的身份都已坐实，不是猜的：

- `+0x106` 是 **ShortString**（偏移非 4 对齐）。段1 `0x71FB31 8B 55 FC / 0x71FB34 81 C2 06 01 00 00 add edx,0x106 / 0x71FB3A E8 … call 0x405774`，而 `sub_405774` 就是 Delphi `@LStrFromString`（`31 C9 / 8A 0A mov cl,[edx]` 取长度字节 / `42 inc edx` / `E9 … jmp 0x4055F0`）。故 `cmp byte[+0x106],0` = `Length(CName)=0`。
- `+0x128` = `PEnvir`（`sub_7688A0 @0x768926 8B 86 28 01 00 00` 后接 `call [envir.vmt+0x28]` AddToMap）。
- `[PEnvir+0x44]` 是 **AnsiString 地图名**：段3 `0x71FEB4 8B 48 44 mov ecx,[eax+0x44]` 传给 `sub_752CAC`，后者序言 `0x752CB5 89 4D F8 / 0x752CC0 E8 FB 2C CB FF call 0x4059C0`（`@LStrAddRef`）。

**决定性证据是日志文本本身。** 调用点失败臂 `0x77A3A6 call 0x79DF74` 的格式串首段 `0x77A81C`（len=81，GBK）逐字为：

> `[Exception]: TEnvironment.DoPlayerSearchViewRange Curr^.POject.CName = 空 Curr = `

后接 `0x77A878 " Curr^.POject = "`、`0x77A894 " paramX = "`、`0x77A8A8 " paramY = "`、`0x77A8BC " paramSeeZone = "`。

所以调用点的形状是：

```
77A2D6  8B 06 / 8B 40 04         eax := Curr^.POject
77A2EB  E8 74 BA FE FF           call 0x765D64          ; 有效性
77A2F2  0F 85 B8 00 00 00        jne 0x77A3B0           ; 有效 -> 正常可见处理
; 无效：摘链 + 记异常 + continue
77A2F8  83 7D DC 00 / 74 0B      prev = nil ?
77A2FE  89 50 0C                 prev^.next := saved_next
77A30F  89 50 08                 cell^.head := saved_next
77A312  B3 01                    bl := 1（已摘）
77A3A6  E8 C9 3B 02 00           call 0x79DF74          ; 上面那条异常日志
77A3AB  E9 68 03 00 00           jmp 0x77A718           ; continue
```

**判定：`sub_765D64` 是一条"悬挂/脏格子项检测"，失败即认定链上挂了一个已被拆解的对象，摘链并报异常。它不是死亡谓词，也不是可见性谓词。**（这也解释了 SPWN-57：该臂确实不 `Free`——原生认为对象已经不归自己所有了。）

### 2.2 C# 侧现状

`TBaseObject.ViewRange.cs:201-227`（`SearchViewRange`）与 `:304-317`（`SearchViewRange_Death`）：

```csharp
if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
{
    if ((HUtil32.GetTickCount() - OSObject.dwAddTime) >= 60 * 1000)
    {
        OSObject = null;
        MapCellInfo.Remove(nIdx);      // 摘链
        …
    }
    BaseObject = OSObject.CellObj as TBaseObject;
    if (BaseObject != null && !BaseObject.m_boDeath && !BaseObject.m_boInvisible) { … }
}
```

要分清两个谓词：
- **摘链谓词** = `age(dwAddTime) >= 60000`　←→ 原生 `!Valid(POject)`。**这一条不等价，SPWN-56 成立。**
- **可见性谓词** = `!m_boDeath && !m_boInvisible && …`　←→ 原生 0x77A3B0 之后的有效臂。这一条不是 SPWN-56 的分歧点。

同一条 60s 摘链规则在树里复制了 5 份：`TBaseObject.ViewRange.cs:203`、`:306`、`TBaseObject.cs:4000`、`TPlayObject.Base.cs:1790`、`RobotPlayObject.Base.cs:458`。

### 2.3 为什么不能照搬（fail-closed 的具体理由）

原生谓词能工作，靠的是 **Delphi 侧对象被拆解后那三个槽会失效**（名字被清空 / `PEnvir` 被置 nil / 内存已释放）。托管移植**没有这个信号**：

- 全树 `m_PEnvir = null` 的写点只有 `MapPoint.cs:92`（另一个类）；`TBaseObject.m_PEnvir` 除 `TBaseObject.cs:322` 的字段初值外**从不被置回 null**。
- `m_sCharName` 全树**没有任何清空点**。
- 托管引用不会悬挂：一个"孤儿"格子项会把 `TBaseObject` 一直钉在内存里，三个槽全部保持有效。

所以把 `sub_765D64` 机械搬过来，谓词会**恒为真**：摘链臂变成死代码，而它是托管移植里 `OS_MOVINGOBJECT` 孤儿格子项的**唯一 GC**。后果是格子链只增不减，而这条循环是全服最热的路径之一（每 actor 每 tick 扫 `(2R+1)²` 格 × 每格链长）。这是净回归，不是等价化。

要真做，前置条件是先把 Delphi 的对象拆解语义补齐（`Free`/`MakeGhost` 时清 `m_sCharName` 与 `m_PEnvir`），那会动到全树对象生命周期——属"牵连过广"。

**另需注意：当前 60s 规则并不误伤活体。** `Envirnoment.VerifyMapTime`（`Envirnoment.cs:1251-1277`）会把活体格子项的 `dwAddTime` 刷新，唯一调用点 `TBaseObject.Base.cs:618` 位于 `m_dwVerifyTick` 块内，周期 `> 30 * 1000`（`TBaseObject.Base.cs:583`）。30s 刷新 < 60s 回收，故只有"宿主已停止 `Run()` 满 60 秒"的项会被摘——与原生谓词瞄准的人群大体重合，机制不同。

**残余可观察差异**：(a) 回收时机——原生即时，C# 最多迟 60 秒；(b) C# 不发那条 `TEnvironment.DoPlayerSearchViewRange Curr^.POject.CName = 空` 异常日志。

**判定：DIVERGENT，只报不改。** 维持分片结论，根因由"谓词写错"修正为"原生谓词依赖的对象拆解信号在托管模型里不存在"。

---

## 3. POIS-11 —— **上修为 FAITHFUL**

分片记为 `DIVERGENT（已桥接·毒代理域）`，注"行为侧已对齐；结构 fork 仍存"。本轮复核：**那个 fork 已经不在了**。

### 3.1 原生：0x1F 与 0x1E 确为两种效果（复核吻合）

毒 tick 阶梯（`0x76BD4F`–`0x76BDF5`），四档 if/else-if，每档 `sub_772960`(HasState) + `sub_773B98`(取节点)：

```
76BD4F  B2 06 / E8 -> 0x772960     0x06：node.Value := MIN(MaxHP,0x4C4B40) idiv 0x64
76BD8C  B2 01 / E8 -> 0x772960     0x01：node.Value := MIN(MaxHP,0x4C4B40) idiv 0x1E
76BDC5  B2 1C / E8 -> 0x772960     0x1C：保留施法者供量
76BDE0  B2 1F / E8 -> 0x772960     0x1F：保留施法者供量
76BDF5  三跳汇合 -> 76BDFB mov edi,[rec+0x0A] / inc edi / 76BE0C call [ecx+0x1B0]
```

入伤缩放（`0x767A94`–`0x767AD9`）：

```
767A94  B2 1E / E8 -> 0x772960     HasState(0x1E)；未中则整段跳过（767A9F je 0x767AD9）
767AA5  E8 -> 0x773BEC             取 level
767AAA  83 F8 04 / 75 15           level==4 ?
767AB5  D8 0D 3C 7B 76 00          fmul float32[0x767B3C] = 1.25
767ACA  DB 2D 40 7B 76 00          fld  ext80  [0x767B40] = 1.2
```

两个状态、两个互不相干的消费者 ⇒ 确为两种效果。✓

### 3.2 C#：桥接不只是"边界糊一层"，而是逐点对上了原生

`TBaseObject.cs:6174 AddTimedAbilityInternal((byte)(31 - nType), nPoint, nTime*1000, 0)`：

| C# 常量 | 值 | 31 − n | 原生状态 |
|---|---|---|---|
| `POISON_DECHEALTH` | 0 | 31 = **0x1F** | DoT 毒（tick 阶梯末档） |
| `POISON_DAMAGEARMOR` | 1 | 30 = **0x1E** | 防御毒（入伤缩放） |
| `POISON_STONE` | 5 | 26 = **0x1A** | 石化/麻痹 |

映射不是推的，是用全镜像 `call [reg+0xC8]`（MakePosion VMT 槽）站点普查对出来的——63 个站点里游戏对象类那批的 `mov dl,imm8` 与 C# 调用点逐一吻合：

| 原生 | C# 调用点 | 31−n |
|---|---|---|
| `0x666D48 B2 1F` → `0x666D4E call [ebx+0xC8]` | `TBaseObject.Attack.cs:956 MakePosion(POISON_DECHEALTH,30,1)` | 0x1F ✓ |
| `0x680AD2 B2 1F` / `0x680AE6 B2 1A` | `CentipedeKingMonster.cs:96 MakePosion(POISON_DECHEALTH,60,3)` / `:109 MakePosion(POISON_STONE,5,0)` | 0x1F / 0x1A ✓ |
| `0x6670BD B2 1A` | `GasAttackMonster.cs:32 MakePosion(POISON_STONE,5,0)` | 0x1A ✓ |
| `0x766F78 8A 56 02 mov dl,[esi+2]` | 数据驱动（状态 id 来自记录 +2、时长 +4、值 +0xC） | 说明 0x1E 等任意 id 在原生也走数据路径可达 |

消费端读的是**原生状态 id**，不是 legacy 槽号：

- `TBaseObject.NativePoisonTick.cs:49-54` 把四档常量钉成 `0x06 / 0x01 / 0x1C / 0x1F`，`TryResolveNativePoisonTickDamage` 的优先级顺序、前两档覆写 `node.Value`、末档 `damage = node.Value + 1` 与 `0x76BD4F`–`0x76BE0C` 逐句对应。
- `TBaseObject.NativeMagicMidStates.cs:242-262 ApplyNativeStruckAmplifyStates` 读状态 30：`state53 → 1.3`，否则 `level==4 ? 1.25 : 1.2`，level 由 `GetNativeRedPoisonLevel()`（`TBaseObject.NativeRedPoisonLevel.cs:41`）取，写入端是 `MakePosion` 的 `RecordNativeRedPoisonLevel(nPoint)`（`TBaseObject.cs:6199`）。

### 3.3 "结构 fork" 已经消失

分片写的 fork 指的是那个与原生并行的第二份存储 `ushort[12]`（按秒、自带每秒倒计时循环）。当前 master 里它**已被删除**：`TBaseObject.LegacyStatusTimeView.cs` 把 `m_wStatusTimeArr` 变成一个**不持有任何存储**的门面（`:28-33` 明写 "There is no second array … This type deletes the second store"），每次读写都落到唯一权威 `Self+0xDC` 节点链（`:97-131` 的 indexer 走 `FindTimedAbilityInternal(31 - slot)`）。槽↔状态映射 `31 - i` 在该文件 `:37-48` 有三条独立证明（`GetCharStatus` 的 `0x80000000 >> i` 位投影、`MakePosion` 转发、状态消失消息表 `0x742692` 的跳表把 state 22 对上 slot 9）。

所以剩下的只是 **`MakePosion(int nType,…)` 这个公开形参的命名约定**——一个纯源码层约定，运行期没有任何表示：所有写在边界转成原生 id，所有读直接用原生 id，脚本 API（`YanshenApi.PoisonCore:1065`）也先转成同一批常量。

### 3.4 判定

**FAITHFUL。** 0x1F 与 0x1E 在 C# 里保持互不混淆、落到正确的原生状态号、被正确的原生消费者消费；单一权威、无并行存储。分片的 `DIVERGENT（已桥接）` 标签按当前 master 应改判 FAITHFUL，"结构 fork 仍存"一句已过时。

**顺带记一条边界（不属 POIS-11）**：`MakePosion` 的入口门是 `nType < MAX_STATUS_ATTRIBUTE`（`Grobal2.cs:99` = 12），故该 API 只能触达原生状态 `0x14..0x1F`。原生另有 `0x717D8B B2 11 mov dl,0x11`（状态 17）经同一 VMT 槽施加，落在该窗口之外，C# 这条 API 够不到；若将来要移植那个站点，需直接走 `AddTimedAbilityInternal`。（`Grobal2.cs` 禁改，仅记录。）

---

## 4. 改动清单

| 文件 | 改动 | 提交 |
|---|---|---|
| `GameSvr/Actors/TBaseObject.Base.cs` | 新增 `NativeMonsterOwnTableScatterRange = 3`（附字节证据）；私有 `ScatterBagItems` 增 `int DropWide` 形参；1-arg 虚入口显式传 `_MIN(nDropItemRage,7)`；怪物 own-table 调用点传 3 | `20690091` |
| `docs/drop_view_residual_20260814.md` | 本报告 | 见尾 |

**未改（有据）**：SPWN-56（托管对象模型缺失原生谓词依赖的拆解信号，照搬即净回归）；POIS-11（复核为 FAITHFUL，无缺口）；1.6 两条顺带发现（其一牵连过广，其二死代码无行为影响）。

**构建**：`dotnet build GameSvr\GameSvr.csproj` → **0 错**，17 警告（全部为改前既有）。

---

## 5. 方法与可复现

- 反汇编脚本置于 `%TEMP%`（`dv_disasm.py` / `dv_census.py` / `dv_fnstart.py`），未入库。
- 所有 VA 以 `flat_image.bin`（off = VA − 0x400000）直读字节 + capstone 5.0.7 反汇编复核。
- Delphi 字符串以长度前缀 `[addr-4]` 校长度、GBK 解码校内容（`0x720100` / `0x720120` / `0x720134` / `0x64E7E0` / `0x74006C` / `0x77A81C` 等）。
- 调用点普查用 `E8 rel32` 目标匹配（`sub_7688A0` / `sub_768AAC` 共 21 处）与 `FF 9x C8 00 00 00` 模式匹配（VMT+0xC8 共 63 处）；函数起点用 `55 8B EC` 序言回溯 + 反汇编对齐校验。
- 配置名普查用 ASCII（大小写不敏感）与 UTF-16LE 双路全镜像扫描。
