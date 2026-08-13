# NPC 脚本引擎逐项对账（2026-08-13，第二轮）

- 工作树 `D:\loym2\.claude\wt2\m-npcscript`，分支 `w/m-npcscript`，已 `git merge --no-edit master`
- 镜像 `staging/_reunpack_work/flat_image.bin`，基址 `0x400000`
- 复现工具链：`tools/npcscript_re/`，按顺序跑
  `_reg_walk.py` → `_reg_names.py` → `_builtins.py` → `_cs_cases2.py` → `_final_recon2.py`
  （`_dis.py` 是 capstone 封装，`BASE = 0x400000`）。整条链已在该目录下实跑通过
- 机器可读产物：`docs/m_npcscript_native_registry_20260813.txt`（654 条原生注册全集）
- **未执行任何编译命令**

---

## 0. 先纠一条任务前提：本引擎没有 `#IF` / `#ACT` 脚本

任务书让我对账「段 `[@main]` 怎么切分、`#IF` 与 `#ACT` 怎么配对」。**战神引擎 M2Server 里不存在这套
经典 QuestDiary 脚本**，所以这一项无从对账。全镜像多编码扫描（ASCII 大小写不敏感）：

| 记号 | 命中 |
|---|---|
| `#ACT` `#SAY` `#ELSEACT` `#ELSESAY` `#CALL` `#GOTO` `#ELSEGOTO` | **各 0** |
| `[@main]` `[@buy]` `[@sell]` `[@exit]` | **各 0** |
| `CHECKITEM` `MAPMOVE` `TAKEGOLD` `CHECKGOLD` `CHECKGENDER` `CHECKJOB` `QUESTDIARY` `SENDMSG` | **各 0** |
| `#IF` | 4 处，全部落在高熵压缩数据里（例 `0x12E941B` 前后 80 字节全是随机字节，非字符串区） |

对照组（证明扫描器没坏）：`@main` 67 命中、`@buy` 1（`0x6D5910`）、`@sell` 1（`0x7CA223`）。

**真实的脚本引擎是 RemObjects PascalScript**（运行时在 `0x50xxxx`）。所谓「段」就是 PAS 里的
`procedure`，所谓「命令」就是宿主注册给编译器的类方法 / 类属性 / 全局函数。下面全部按这个事实对账。

---

## 1. 解析器与「段」的真实语义

### 1.1 标签调用器 `sub_63DC98`（本轮完整反出来）

签名：`GotoLable(eax=Self:TPsNpc, edx=player, ecx=?, [ebp+8]=参数, [ebp+0xC]=过程名)`

```
0063DCD9  8D 83 10 18 00 00   lea eax,[ebx+0x1810]      ; player+0x1810 <- 过程名
0063DCE2  E8 6D 78 DC FF      call 0x405554             ; LStrAsg，调用【之前】就写
0063DCF2  8B 80 70 05 00 00   mov eax,[Self+0x570]      ; 脚本引擎对象
0063DCFE  FF 56 44            call [esi+0x44]           ; vtbl+0x44 -> al = 成功与否
0063DD01  88 45 FB            mov [ebp-5],al
```

日志段用 `'NPC='`(`0x63DEB8`) / `' 过程='`(`0x63DEC8`) / `' 参数='`(`0x63DED8`) 拼串，
成功打 `'点击NPC成功'`(`0x63DEE8`)，失败打 `'点击NPC失败'`(`0x63DEFC`)。

### 1.2 失败路径有两件事，前一轮的结论要订正

前一轮文档 §1 N8 写「`sub_63DC98`：…失败打失败日志，**不向玩家发气泡**」。字节显示失败路径还有两段：

```
0063DDB7  80 7D FB 00         cmp byte [ebp-5],0
0063DDBB  75 5C               jne 0x63DE19             ; 成功 -> 直接收尾
0063DDBD  85 DB               test ebx,ebx / je 0x63DDFE
0063DDC1  80 BB 75 06 00 00 03  cmp byte [ebx+0x675],3 ; m_btPermission
0063DDC8  76 34               jbe 0x63DDFE             ; <=3 静默
0063DDCA  68 10 DF 63 00      push 0x63DF10            ; '[ExecScript Fail]: '
0063DDD8  68 2C DF 63 00      push 0x63DF2C            ; ' : '
0063DDF0  66 B9 FF 38         mov cx,0x38FF
0063DDF8  FF 93 D4 00 00 00   call [ebx+0xD4]          ; 发给玩家
   ; 然后【无论权限】都走：
0063DDFE  E8 C6 25 00 00      call 0x6403CC            ; TPsNpc.ReInitialize
0063DE0F  BA 38 DF 63 00      mov edx,0x63DF38
0063DE14  E8 5B 01 16 00      call 0x79DF74            ; '--ReInitializet-- 因为点击NPC失败，重新刷新NPC...'
```

- **① GM（权限 > 3）会收到红字** `'[ExecScript Fail]: <脚本名> : <过程名>'`，`cx=0x38FF`，
  与点击器找不到 NPC 那条（`0x6B8CB8 '找不到NPC'`）同形。判定 **DIVERGENT**（与 N6 同类，同一处 fix）。
- **② 任何一次标签调用失败，原生都会重载该 NPC 的脚本。** `0x6403CC` 的身份由它自己的异常
  格式串钉死：`0x6409A0 '[Execption]: %d: TPsNpc.ReInitialize -- %s : %s'`；函数体里有
  `'--ReLoadScript--'`(`0x640900`) / `' 重新载入成功'`(`0x64091C`) / `' 重新载入失败：'`(`0x640934`)，
  并重新按名解析五个入口点（见 §4）。判定 **MISSING**：C# `TryCallLabelCore` 失败只打
  `[PasEngine] Error` 并 `return false`，不重载。玩家可见后果：改完脚本后原生点一次坏标签就自愈，
  C# 必须手动 `@reloadquest`。

### 1.3 `player+0x1810` 是跨调用状态，不是日志

写点两处（`0x63DCD9` GotoLable、`0x7204A5`），另有 `0x636C52`、`0x63D9ED` 两处 `lea`。
**读点一处**：`0x63EEB2 8B 80 10 18 00 00 mov eax,[eax+0x1810]` → `0x63EEB8 mov edx,0x63F114`
→ `call 0x40BD78`，`0x63F114` = `'@fail_s_repair'`。即修理失败分支靠它判断「玩家当前在哪个过程里」。
C# 全树 `0x1810` / `CurrentLabel` **0 命中** → **MISSING（低优先级，只有一个消费者）**。

### 1.4 大小写 / 注释 / 分词

`@main` 的实际入口是 `sub_63DC74`，一个把 `label` 写死成 `'@main'`(`0x63DC90`) 的薄壳：

```
0063DC77  68 90 DC 63 00   push 0x63DC90    ; '@main'
0063DC7C  6A 00            push 0
0063DC7E  33 C9            xor ecx,ecx
0063DC80  E8 13 00 00 00   call 0x63DC98
```

- 大小写：PascalScript 标识符不区分大小写；宿主侧的按名查找走 `0x4EB054`。
  C# 用 `ToLowerInvariant()` + `switch` 复刻，**方向正确**——但这也意味着任何带大写字母的
  `case` 标签是死代码，本轮据此抓到两条（见 §3.4）。
- 注释 / 空白 / 分词：**BLOCKED**。我没有把 `0x50xxxx` 的词法器与 C# `PasLexer` 逐 token 对过，
  沿用上一轮 B1。生产脚本能跑不等于边角等价。

---

## 2. 命令全集：从注册点扒出来的 654 条

### 2.1 三个注册helper（本轮定位）

| VA | 形状 | 语义 | 站点数 |
|---|---|---|---|
| `0x510F00` | `mov edx,<decl>; mov eax,ebx; call` | `RegisterMethod(Decl)` | **503** |
| `0x510FFC` | `push acc; mov ecx,<type>; mov edx,<name>; mov eax,ebx; call` | `RegisterProperty(Name,Type,Access)` | **101** |
| `0x513A7C` | `mov edx,<Ptr>; mov ecx,<decl>; mov eax,ebx; call` | `AddFunction(Ptr,Decl)` 全局函数 | **50** |

`0x510FFC` 内部 `0x51103F mov [ebx+0x20],al` 存的就是 `push` 的那个字节：`0`=读写、`1`=只读。
类由 `0x50F0E4 AddClassN(Self, InheritsFrom, Name)` 建立、`0x50F1C4 FindClass(Name)` 取回。

604 个 method/property 站点**全部**用固定形状 `BA imm32 / 8B C3 / E8` 反向解码成功，
`weird = 0`；三段注册过程做过对齐校验（`hit=391/50/224/8, missed=0`），没有漏站点。

### 2.2 类结构与条数

| 编译期类 | 继承自 | 方法 | 属性 | 脚本里的接收者 |
|---|---|---|---|---|
| `TOBJECT` | — | 2 | — | 任意 |
| `TBaseObj` | TOBJECT | 0 | 0 | — |
| `TCreature` | TBaseObj | 8 | 0 | Player/NPC/Animal |
| `THumanKind` | TCreature | 0 | 0 | — |
| `TPlayer` | THumanKind | **282** | **77** | `This_Player` |
| `TPsNpc` | TCreature | **200** | 9 | `This_NPC` |
| `TAnimal` | TCreature | 0 | 6 | `This_Animal` |
| `TBaseItem` | TBaseObj | 0 | 7 | `This_Item` |
| `TMySQLDB` | TBaseObj | 9 | 2 | `This_DB` |
| `TBaseGroup` | TObject | 2 | — | `MyGroup` |
| 全局函数 | — | 50 | — | 裸调用 |
| **合计** | | **503** | **101** | **+50 = 654** |

另有 PascalScript 运行时自带 built-in **39 个**（`0x4F9320` / `0x50ED24` 消费的声明串，
如 `inttostr` `0x50D0B8`、`CompareText` `0x50D67C`、`Random` `0x50D61C`）。

实例绑定由 `0x514170 SetVarToInstance` 完成，共 5 个脚本全局变量：
`This_DB`(`0x733D30`)、`This_Item`(`0x733D40`)、`This_Player`(`0x733D54`)、
`This_NPC`(`0x73AA14`)、`This_Animal`(`0x73B0C8`)。

上一轮报的「M2 API 541 个」是按**声明串**数的，漏掉了 50 个全局函数、把
`TPsNpc` 与 `TPlayer` 的同名重载算成一条，也没有 property/method 之分。**以本轮 654 为准。**

---

## 3. 与 C# 对账

C# 侧口径：`PasApiBridge` 的 13 张 `switch` + `PasInterpreter.ExecuteBuiltinFunction/Procedure`
+ `TryCallYanshenFunc`。解释器**不跨接收者回落**（`PasInterpreter.cs:903` 只试 Player 两张表，
`:916` 只试 NPC 两张表，`:778` 只试 Standalone），所以「名字在别的表里」不等于能调到。

### 3.1 MISSING —— 原生注册了、C# 任何一张表都不认（**23 条，全部要补**）

| 类 | 名 | 声明 VA | 声明 |
|---|---|---|---|
| TPlayer | `AccumulateFlag` | `0x72AF6A` | `: Boolean` 读写 |
| TPlayer | `AccumulatedTime` | `0x72AF57` | `: Double` 只读 |
| TPlayer | `BeginTransLog` | `0x72BACC` | `procedure BeginTransLog(Logtype:integer);` |
| TPlayer | `BoReferUser` | `0x72AB09` | `: Boolean` 只读 |
| TPlayer | `BoSpreader` | `0x72AB1C` | `: Boolean` 只读 |
| TPlayer | `CurGlory` | `0x72AF90` | `: Integer` 只读 |
| TPlayer | `EndTransLog` | `0x72BAD8` | `procedure EndTransLog;` |
| TPlayer | `EnterMapTick` | `0x72AFDC` | `: longword` 只读 |
| TPlayer | `GuildLord` | `0x72ADDB` | `: string` 只读 |
| TPlayer | `HeroTypeExt` | `0x72AE86` | `: Integer` 只读 |
| TPlayer | `IsDeleted` | `0x72ABC7` | `: Boolean` 只读 |
| TPlayer | `IsNetCafeUser` | `0x72AF1E` | `: Boolean` 只读 |
| TPlayer | `LevelOrder` | `0x72AFA3` | `: Word` 只读 |
| TPlayer | `LingXiValue` | `0x72AE3A` | `: Integer` **读写** |
| TPlayer | `MyAttackMode` | `0x72AF44` | `: byte` 只读 |
| TPlayer | `MyGroup` | `0x72B015` | `: TBaseGroup` 只读（`TBaseGroup.GetMember/GetMemberCount` 因此也不可达） |
| TPlayer | `MyUsedLfNum` | `0x72AC39` | `: Integer` 只读 |
| TPlayer | `OpenMilRank` | `0x72BAB4` | `procedure OpenMilRank;` |
| TPlayer | `PeiouName` | `0x72AF7D` | `: string` 只读 |
| TPlayer | `TryExchangeItemMode` | `0x72AFC9` | `: Cardinal` **读写** |
| TPlayer | `TryExchangeItemName` | `0x72AFB6` | `: string` **读写** |
| TPlayer | `UsedYBNum` | `0x72AAAA` | `: Integer` 只读 |
| TPsNpc | `InputButton` | `0x73470E` | `: Word` 只读 |

生产脚本对这 24 条**全部 0 命中**（扫了 `D:\光头卧龙` 下 458 个 `.pas/.inc` + 解密后的眼神脚本），
所以优先级低——但 `MyGroup` 连带废掉整个 `TBaseGroup`，建议先补它。

不在这 23 条里、但同样没有实现的两条，单独记：

- `TPlayer.GetMyLeiTaiFlag`（`0x72B3B1`）：本轮把死标签改小写后名字能解析了，
  但体是 `RejectUnsupportedNativeApi` —— 属 **fail-closed 桩**，不是名字缺失。
- `TPsNpc.DoShowNpcEx`（`0x734F98`）：可达标签是 reject，实现躺在不可达标签里，见 §3.4。

> 订正一处我自己的误报：`TPsNpc.GetCelebName`（`0x73495C`）声明串首字母是大写 `Function`，
> 提取器的关键字正则区分大小写，把整条声明当成了函数名，一度把它算进 MISSING。
> C# `CallNpcFunc`/`CallNpcMethod` 都有 `getcelebname`，判 **FAITHFUL（名字面）**。
> `tools/npcscript_re/_reg_names.py` 已加 `re.IGNORECASE` 并留注释，避免重犯。

### 3.2 WRONG RECEIVER —— 名字 C# 认识，但挂错了接收者（**36 条**）

因为解释器不跨表回落，这 36 条在脚本里按原生写法调用会抛「函数找不到」并**中断整个标签**。

原生挂 `TPsNpc`、C# 只在 Player/Standalone 表里（20 条）：
`ChangeGPSwitch 0x734ADC`、`EAOrderIsStart 0x734E54`、`GetAroundMonNum 0x7349EC`、
`GetCurrentEAIdxByName 0x734E00`、`GetCurrentEANameByIdx 0x734E0C`、`GetCurrentEAPeriod 0x734DF4`、
`GetCurrentEAScoreByIdx 0x734E18`、`GetEAOrderInfo 0x734E48`、`GetGuildWarGold 0x734D34`、
`GetLastEAIdxByName 0x734E24`、`GetLastEANameByIdx 0x734E30`、`GetLastEAScoreByIdx 0x734E3C`、
`GetSomeGuildPoint 0x734AD0`、`GetTreatWine 0x734E78`、`HeroRename 0x734E90`、
`MoveAllHumInMap 0x734ECC`、`NewFullMailEx 0x735070`、`SetWineTreat 0x734E6C`、
`UpdateEverydayActOrder 0x734DE8`、`UseGuildPoint 0x734AC4`

原生挂 `TPlayer`、C# 只在 NPC/Standalone 表里（8 条）：
`BuildGuild 0x72B1DD`、`ChgEquipmentBreakLevel 0x72B741`、`GiveItemsToOther 0x72B7E9`、
`InputDialog 0x72B910`、`QueryTaskDispatch 0x72B9B8`、`ReqPieceUpNewYearPicture 0x72B65D`、
`RequestGuildWar 0x72B321`、`StartPaodian 0x72BAC0`

原生是全局函数、C# 挂在 Player/NPC 表上（7 条）：
`EAOrderIsStart 0x729A3A`、`GetCurrentEANameByIdx 0x7299F6`、`GetEAOrderInfo 0x729A18`、
`GetScoreByName 0x729A29`、`KickAllHumToMap 0x7299E5`、`PlayerCry 0x729A4B`、`PlayerGive 0x729A5C`

`TAnimal.Level 0x73AED7`：C# 只有 `GetPlayerProperty` 里的 `level`，`GetAnimalProperty` 没有。

**注意**：`InputDialog` 与 `NewFullMailEx` 原生在 TPlayer 和 TPsNpc **两个类上都注册了**
（`0x72B910`+`0x734944`、`0x72BBF8`+`0x735070`），C# 各只覆盖一半。生产实测
`This_Npc.InputDialog` 114 处、`This_Player.NewFullMailEx` 13 处——**都恰好落在 C# 覆盖的那一半**，
所以线上无感知。其余 34 条生产 0 命中。**这解释了为什么这批缺陷至今没暴露，但不代表可以不补。**

### 3.3 INVENTED —— C# 有 `case`、原生注册表没有、全镜像 0 命中（**74 条**）

判定链：不在 654 条注册表里 → 不在 39 个 PS built-in 里 → `flat_image.bin` ASCII（大小写不敏感）
与 UTF-16LE **双双 0 命中** → 解密后的眼神脚本 0 命中 → `D:\光头卧龙` 生产树 0 命中。

其中 **49 条带真实实现**（不是 `RejectUnsupportedNativeApi`），按风险排：

- **物品/金钱相关（高危，建议优先 fail-closed 或删）**：
  `psshopbuygoods`（`CallPlayerMethod:2203` / `CallNpcMethod:5358`，会走购买发货）、
  `psshopgetgoodslist`（`:2200`/`:5348`）、`takeherobagexitem`（`:3095`）、
  `takefromherobagex`（`:3096`）、`getherobagexitemcount(ex)`（`:4758`/`:4759`）、
  `checkgamegold`（`:3799`）、`getstorageitemcount`（`:4424`，stub）、`openstoragemax`（`:2308`）
- 变量/状态写入：`groupsets`（`:3315`/`:3638`/`:7787`，成组写 S）、`inc_self_lv`（`:2369`）、
  `chgpkselfzero`、`chgskillv`、`setvexptobeconverted`、`incvexptobeconverted`
- 传送/攻城：`mapmove`（`:2088`）、`startcastlewar`/`endcastlewar`/`startsiege`/`endsiege`
- 眼神味的臆造名：`lmsetysid` `lmgetysid` `lmgetitemid` `lmcheckmapmon` `npc_creatmons`(见下)
- 属性臆造：`gamegold` `gamepoint` `paymentpoint` `my_lfnum` `peiyouname` `max_hp` `max_mp`
  `targetlevel` `targetgoldnum` `targetmapname` `racetype` `creditpoint`

**典型错拼**（原生有正名，C# 拼错另立一条，等于两边都不通）：

| C# 错名 | 原生正名 | 原生 VA |
|---|---|---|
| `minusdatetime` | `minusDataTime` | `0x72986F`（全局） |
| `serveraay` | `ServerSay` | `0x7297A3`（全局） |
| `chgskillv` | `ChgSkillLv` | TPlayer |
| `addsskskillexp` | `AddSkillExp` | TPlayer |
| `click_repair_ex` | `Click_RepairEx` | TPsNpc |
| `getdynroomhumcnt` / `gethavedynroomcnt` | `GetDynRoomCnt` / `GetDynRoomHumNum` | `0x729990` / `0x72997F` |
| `notifyclientupditem(ex)` | `NotifyClientUpdBagItem` | TPsNpc |
| `takefromherobagex` | `TakeFromHeroBag` | TPlayer |
| `openstoragemax` | `OpenStorage` | TPlayer |
| `isstudent` | `IsAStudent`（属性） | TPlayer |
| `lmcreatemon` | `CreateMon` | TPlayer |

**不算 INVENTED 的 7 条**（引擎 0 命中，但在解密后的眼神脚本里存在，属插件 API）：
`ys_setcd` `ys_setcd_min` `ys_cmptime` `ys_cmptime_min` `ys_gethowtime`（`AllFuc使用例子.pas`）、
`npc_creatmons`（`NpcFuc.pas`）、`mastername`（`AllFuc.pas`）。
上一轮把 `npc_creatmons` 判成 INVENTED，**按本轮证据翻案**。

### 3.4 死 `case` 标签（本轮修 2 条，留 1 条）

`switch` 的判别式是 `name.ToLowerInvariant()`，带大写字母的标签永远不匹配。

| 标签 | 位置 | 处置 |
|---|---|---|
| `case "antiMagic"` | `GetPlayerProperty:559` | 死 + `antimagic` 全镜像 0 命中 → **已删**（`3a4bec23`） |
| `case "getmyleitaiFlag"` | `CallPlayerFunc:5034` | 死，但原生 `0x72B3B1` 真有 → **已改小写**（`3a4bec23`） |
| `case "doshow npcex"` | `CallNpcMethod:6208` | 标签含空格，永不匹配；**真实现在这里**，而可达的 `case "doshownpcex"`(`:7005`) 是 `RejectUnsupportedNativeApi`。原生 `TPsNpc.DoShowNpcEx` 注册在 `0x734F98`。**未改**——两种改法（激活未验证的实现 / 删死代码）都需要先反 `0x734F98`，按 fail-closed 保持现状并上报 |

`case "getdate num"`(`:7889`) 与 `case "getopen gametime"`(`:7876`) 同样含空格，但各自紧跟着正确的
`getdatenum`/`getopengametime` 并 fall-through 到同一段体，**无害**。

### 3.5 PascalScript 运行时 built-in

原生 39 个，C# 认 11 个，缺 28 个：
`setlength` `assigned` `insert` `delete` `strtoint64` `int64tostr` `gettickcount` `sizeof`
`strset` `vartype` `varisnull` `varisempty` `null` `unassigned` `raiseexception`
`raiselastexception` `exceptiontype` `exceptionparam` `exceptionproc` `exceptionpos`
`idispatchinvoke` `_t` `year` `mon` `day` `hour` `min` `dayofweek`

生产脚本几乎不裸调这些（时间族走 M2 的 `GetYear/GetMonth/...` 全局函数，C# 在
`PasInterpreter.ExecuteBuiltinFunction:1667-1682` 有实现）。判 **MISSING（低优先级）**。

---

## 4. 脚本触发时机

### 4.1 原生的全集：6 个入口点

`TPsNpc.Initialize`(`0x640C67..`) 和 `TPsNpc.ReInitialize`(`0x640672..`) 都用
`0x4EB054`（按名查过程）解析五个名字，`cmp eax,-1 / setne al` 把「有没有这个过程」存成一个字节：

| 标签 | 存在位 | 名字串 | 触发点 | 触发条件 |
|---|---|---|---|---|
| `@main` | — | `0x63DC90` | `0x6B8C43` → `sub_63DC74` | 玩家点击 NPC（同图 + 切比雪夫距离 ≤15） |
| `@Execute` | `[npc+0x594]` | `0x63E98C` | `0x63E62A` | NPC 自身 tick，见 §4.2 |
| `@OnEnter` | `[npc+0x595]` | `0x6468EC` | `0x5FD56A`（分派器 `0x6468C8`） | 玩家进入该地图 |
| `@OnLeave` | `[npc+0x596]` | `0x64691C` | `0x5FD5A3`（`0x6468F8`） | 玩家离开该地图 |
| `@OnDie` | `[npc+0x597]` | `0x64694C` | `0x5FD50A`（`0x646928`） | 玩家在该地图死亡 |
| `@OnReLive` | `[npc+0x598]` | `0x646978` | `0x5FD3B2`、`0x77BB66`（`0x646954`） | 玩家复活 |

四个 `On*` 分派器形状完全一致，例（OnEnter）：

```
006468C8  55                     push ebp
006468CB  80 B8 95 05 00 00 00   cmp byte [eax+0x595],0
006468D2  74 0E                  je 0x6468E2          ; 没这个过程 -> 什么都不做
006468D4  68 EC 68 64 00         push 0x6468EC        ; '@OnEnter'
006468D9  6A 00                  push 0               ; 参数 = ''
006468DB  33 C9                  xor ecx,ecx
006468DD  E8 B6 73 FF FF         call 0x63DC98        ; GotoLable
```

`@OnEnter`/`@OnLeave` 挂在**地图对象**上：`0x5FD559 inc dword [ebx+0xD8]`（本图人数 +1）之后
才 `0x5FD55F mov edx,[ebx+0xA4]`（本图绑定的 QuestNPC）/ `test edx,edx / je` / 调用。
`@OnLeave` 对称：`0x5FD592 dec dword [ebx+0xD8]` **先减再调**，且调用后
`0x5FD5A8 cmp dword [ebx+0xD8],0 / jg` 才做后续清理。**顺序是「先改人数再触发脚本」**——
脚本里读到的在场人数已经包含/排除了自己。

`@OnReLive` 分派前多两道门：`0x5FD392 cmp byte [ebx+0x178],0 / jne 跳过`（对象已删）、
`0x5FD39B cmp byte [ebx+0x73],0 / jne 跳过`。

### 4.2 `@Execute` 的节流是秒、且时间戳在调用【之前】盖

```
0063E5EF  80 BB 94 05 00 00 00   cmp byte [ebx+0x594],0
0063E5F6  74 37                  je  0x63E62F            ; 无 @Execute
0063E5F8  83 BB E4 05 00 00 00   cmp dword [ebx+0x5E4],0
0063E5FF  76 2E                  jbe 0x63E62F            ; 间隔 <=0 -> 永不执行
0063E603  2B 83 A4 05 00 00      sub eax,[ebx+0x5A4]     ; eax = now - 上次
0063E609  69 93 E4 05 00 00 E8 03 00 00   imul edx,[ebx+0x5E4],0x3E8   ; 间隔(秒)*1000
0063E613  3B C2                  cmp eax,edx
0063E615  72 18                  jb  0x63E62F            ; elapsed < 间隔 -> 跳过（即 >= 才跑）
0063E617  89 B3 A4 05 00 00      mov [ebx+0x5A4],esi     ; 【先】盖时间戳
0063E62A  E8 69 F6 FF FF         call 0x63DC98           ; 【后】调 '@Execute'，player = nil (0x63E626 xor edx,edx)
```

### 4.3 C# 对照

| 入口 | C# | 判定 |
|---|---|---|
| `@main` | `TPlayObject.ClientClickNPC` → `NormNpc.GotoLable` → `TryGotoPascalLabel` | FAITHFUL（上一轮已核） |
| `@Execute` | `PasScriptHost.ProcessAutoScripts:2154` + `CallExecute:2139` | **FAITHFUL**：`:2169 now - LastExecuteTick < IntervalSeconds*1000L → continue`（等价 `jb`）、`:2170` 时间戳在 `:2173 CallExecute` **之前**盖、`CallExecute(path, null, npc)` 传 player=null（对应 `0x63E626 xor edx,edx`）。三点全对 |
| `@OnEnter` | 全树 0 命中 | **MISSING** |
| `@OnLeave` | 全树 0 命中 | **MISSING** |
| `@OnDie` | 全树 0 命中 | **MISSING** |
| `@OnReLive` | 全树 0 命中 | **MISSING** |
| 失败重载脚本 | 无 | **MISSING**（§1.2） |

扫描口径：`GameSvr/**/*.cs` 正则 `(?i)"@?(onenter|onleave|onrelive|ondie|execute)"`，
唯一命中是 `PasScriptHost.cs:2145` 的 `"Execute"`。

**玩家可见后果**：任何依赖「进图/离图/死亡/复活」自动触发的脚本（活动开场、地图公告、
死亡惩罚、复活补给）在 C# 上完全不执行，而且**不报错**——脚本文件里的 `procedure _OnEnter`
就静静躺着没人调。这是本轮影响面最大的一条。

---

## 5. 变量系统：全仓越权扫描结果

### 5.1 结论：**产品代码里没有越权访问点**

扫描口径：`*.cs` 全仓，正则 `\* 1000 \+|\*1000\+|group \* 1000` 与
`m_ScriptVVars|m_ScriptSVars`。

自己算扁平键并直接索引字典的地方，产品代码里只有 `TPlayObject.Base.cs:278 / :305`，
而那两行**就在唯一权威 `TryGetScriptVar` / `SetScriptVar` 内部**：

```264:284:GameSvr/Players/TPlayObject.Base.cs
        public bool TryGetScriptVar(char bank, int group, int index, out int value)
        {
            var upper = char.ToUpperInvariant(bank);
            if (upper == 'V' && group == 0)
            {
                // ... group-0 inline slots, index 1..100 ...
            }
            var store = upper == 'V' ? m_ScriptVVars : m_ScriptSVars;
            if (store != null && store.TryGetValue(group * 1000 + index, out value))
            {
                return true;
            }
            value = 0;
            return false;
        }
```

其余引用全部是 `AuditTools/*` 与 `ProtocolRegressionCheck` 的测试夹具，不在交付路径。
`YanshenApi.cs:1796/1810/1822` 也已经走权威入口。

### 5.2 §4.19 三条路径一致性：核过，一致

| 路径 | 位置 | group-0 处理 | 零值 |
|---|---|---|---|
| 读 | `TPlayObject.Base.cs:264` | 走 `m_ScriptVGroup0[1..100]`，越界 false | 未写槽读回 0 |
| 写 | `TPlayObject.Base.cs:291` | V 组 0 写内联；**S 组 0 直接丢弃**（对应 `0x6DF251/0x6DF255`） | 原样写 0 |
| 存 | `TPlayObject.cs:3354 CopyKeyedScriptVars` | `key < 1001` 跳过 | 保留 |
| 载 | `UsrEngn.cs:3944-3962` | `key < 1001` 跳过 | 保留（注释明确写了「先前这里跳过 0…两处必须同时成立」） |
| 编码 | `DBSvr/Core/NativeHumanDataCodec.cs:1087 MergeKeyValues` | `key < 1001` 跳过 | 保留 |

**落盘升序**：`MergeKeyValues` 用 `SortedDictionary<int,int>`（`:1103`）后顺序写出，
满足原生二分查找 `sub_6E4270`（`0x6E42A2 cmp edi,[esi+eax*8]`）的前提。**FAITHFUL。**

### 5.3 顺带查到一条 G bank 的 DIVERGENT（不在任务清单里，但同族）

原生 `GetG`（声明 `0x72A098`，实现 `0x699198`，thunk `0x728FB8`）：

```
006991C4  83 FB 01      cmp ebx,1        ; ebx = index
006991C7  0F 8C C3000000 jl 0x699290     ; index < 1 -> 返回默认
006991CD  83 FB 32      cmp ebx,0x32
006991D0  0F 8F BA000000 jg 0x699290     ; index > 50 -> 返回默认
006991DF  6B 55 FC 64   imul edx,[ebp-4],0x64   ; paramNo * 100
006991E3  03 D3         add edx,ebx             ; + index
006991BF  BE FE FF FF FF mov esi,0xFFFFFFFE     ; 默认 = -2
```
底层容器是按整数键的查找表（`0x69B01C` → `0x49F0EC` 查、`0x69B04C` → `0x49EE8C`/`0x49EC5C` 存），
miss 分支 `0x69B040 mov eax,0xFFFFFFFE` 同样是 **-2**。

C# `PasApiBridge.GetGlobalVar:8213`：乘数 `100` **对**，但
（a）没有 `1 <= index <= 50` 这道门，（b）miss 返回 **0** 而不是 **-2**。
脚本里 `if GetG(a,b) = 0 then` 这类判断会被反过来。判 **DIVERGENT**，未修（不在本轮授权范围，
且改默认值要同时看消费端，属 §4.19 三路径题）。

---

## 6. 物品 / 金钱发放命令

### 6.1 堆叠物 `Dura = 1` 的构造契约（任务给定，本轮字节复核通过，并补一条）

```
007880F0  55                 push ebp
0078810D  E8 76 B6 FF FF     call 0x783788           ; 根构造器
00788112  66 C7 46 26 01 00  mov word [esi+0x26],1   ; Dura = 1，无条件覆盖
00788118  C6 46 14 07        mov byte [esi+0x14],7   ; StdMode = 7（堆叠族）
```

任务书只给了 `0x788112`；**紧跟着的 `0x788118` 把 `StdMode` 写成 7** 是同一条契约的另一半，
它同时解释了卖价链路 `sub_63F3B4` 的 `0x63F442 cmp byte [eax+0x14],7`。
C# 侧这条已经落地且注释带字节（`Merchant.cs:1679`、`UsrEngn.cs:2544`、`PasApiBridge.cs:8578`），
`StdMode == 7 ? item.Dura : 1` 的计数惯例在 12 个文件里一致。判 **FAITHFUL**。

### 6.2 上一轮已用字节钉死、本轮未改动的守恒结论

`Give` 物品的部分发放 + 失败件 `FreeAndNil`（`0x6C8A14` / `0x6C8ADA call 0x414C24`）、
`Give('金币')` 无上限（`0x6C8951 add [esi+0x15C],ebx`，与 `AddGold` 的
`0x6D7930 cmp ebx,[eax+0x68C] / jg` 分流）、`DecGold` 负数拒绝、`Take` 预检全有或全无
（`0x6DF854`）、`LoopGive` 恒真（`0x6DF530`）——本轮**没有改动这些路径**，沿用上一轮结论。

### 6.3 本轮新增的物品/金钱风险项

§3.3 那 8 条**带真实实现、原生注册表却没有**的物品/金钱 API 是刷物刷钱面。
最需要裁决的是 `psshopbuygoods`：C# 在 `CallPlayerMethod:2203` 与 `CallNpcMethod:5358` 两处
都可达，而原生 654 条注册里**没有任何 `PsShopBuyGoods`**，全镜像 ASCII/UTF-16LE 双 0 命中，
生产脚本 0 命中。按 §1.3「原生没有但 C# 有 → 移除或屏蔽」，**建议改成
`RejectUnsupportedNativeApi`**。我没有直接改，因为它跨 Mall/YBShop 子系统，
属别的代理的地盘，先上报。

---

## 7. 判定计数

| 判定 | 条数 | 说明 |
|---|---|---|
| FAITHFUL（名字 + 接收者对得上） | **595** | 654 − 23 MISSING − 36 WRONG_RECEIVER |
| MISSING | **23 + 4 触发 + 2 状态 + 28 built-in** | §3.1 / §4.3 / §1.2+§1.3 / §3.5 |
| DIVERGENT | **3** | GM 失败红字（§1.2①）、`GetG` 门与默认值（§5.3）、`doshow npcex` 死标签（§3.4） |
| INVENTED | **74** | §3.3，其中 49 条带活实现、8 条涉物品金钱 |
| BLOCKED | 4 | 见 §8 |
| 越权变量访问 | **0** | §5.1 |

「FAITHFUL 519」只代表**名字与接收者都对得上**，不代表实现等价——那是 B4。

---

## 8. BLOCKED

| ID | 缺什么 | 卡在哪一层 |
|---|---|---|
| B1 | `PasLexer`/`PasParser` 与 `0x50xxxx` 运行时逐 token、注释形式、分词规则 | 没反原生词法器；不能宣称词法 1:1（沿用上一轮） |
| B4 | 654 条 API 的**实现**（参数序、失败语义、副作用） | 本轮只把注册面对齐；实现只核了 Give/Take/Gold/GetV/GetG/触发点 |
| B7 | `TPsNpc.DoShowNpcEx` `0x734F98` 的实现 | 决定 §3.4 那条死标签该激活还是该删，两种改法都需要它 |
| B8 | `0x5FD3xx` 那三个触发调用点各自的**上层调用者与顺序** | 已确认是地图对象上的进/离/死/复活四个钩子，但没往上追到「谁在什么时刻调它们」，补 C# 实现时需要 |

---

## 9. 建议优先级

1. **`@OnEnter` / `@OnLeave` / `@OnDie` / `@OnReLive` 四个触发点**（§4）——影响面最大，
   而且是「静默不执行」，最难被发现。分派器形状极简单（`0x6468C8` 十条指令），
   照 `ProcessAutoScripts` 已经验证过的 `@Execute` 模式补即可。
2. **`psshopbuygoods` 等 8 条物品/金钱 INVENTED**（§6.3）—— 刷物面，建议 fail-closed。
3. **36 条 WRONG RECEIVER**（§3.2）—— 纯粹是把 `case` 搬到正确的 `switch`，无逻辑风险；
   `InputDialog` / `NewFullMailEx` 只需补另一半。
4. **失败重载脚本**（§1.2②）与 **GM 失败红字**（§1.2①）—— 与上一轮 N6 是同一处 fix，合并做。
5. `MyGroup` + `TBaseGroup`（§3.1）—— 一条属性带活两个方法。
6. `GetG` 的 `1..50` 门与 `-2` 默认（§5.3）—— 要配套查消费端，别只改一头。
7. B1/B4 保持 BLOCKED，**不要拿「C# 有 case」当 FAITHFUL 用**。

---

## 10. 对前人结论的订正

1. **上一轮「M2 游戏 API 541 个」的口径不完整。** 那是数声明串数出来的，漏了 50 个
   `AddFunction` 全局函数，也没区分 method / property，更没把 `TPlayer` 与 `TPsNpc` 的
   同名重载分开。正确数字是 **654**（503 + 101 + 50），且每条都有注册站点 VA。
2. **上一轮 N8「`GotoLable` 失败不向玩家发气泡」不完整。** `0x63DDC1 cmp byte [ebx+0x675],3 / jbe`
   之后 GM 会收到 `'[ExecScript Fail]: …'`；而且失败还会**无条件重载 NPC 脚本**（`0x6403CC`）。
3. **上一轮把 `npc_creatmons` 判成 INVENTED——翻案。** 它在生产 `NpcFuc.pas`（解密后）里存在，
   属眼神插件 API，不是 C# 臆造。同批 `ys_setcd` 等 5 条同理。
4. **`0x788112` 那条契约还有下半句。** 紧邻的 `0x788118 C6 46 14 07` 把 `StdMode` 写成 7，
   两条要一起看，否则「什么算堆叠物」这个判定无处落地。
5. **任务书假设的 `#IF`/`#ACT` 脚本在本引擎不存在**（§0），相关对账项作废，不是「没做」。

---

## 11. 本轮提交

```
3a4bec23 Fix two unreachable script-API case labels (ToLowerInvariant switch).
```

（外加本文件与 `docs/m_npcscript_native_registry_20260813.txt`。）
