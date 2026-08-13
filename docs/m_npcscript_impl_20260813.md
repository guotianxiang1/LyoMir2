# NPC 系统 + PAS 脚本引擎对账（2026-08-13）

- 工作树：`D:\loym2\.claude\wt2\m-npcscript`，分支 `w/m-npcscript`
- 镜像：`staging/_reunpack_work/flat_image.bin`，基址 `0x400000`
- 生产脚本：`D:\光头卧龙`（3426 个 `.pas/.txt/.inc`，不含 `staging/_ys_out` 等派生分析产物）
- **未执行任何编译命令**

已采信：原生声明表是带长度前缀的 Delphi 长串（例 `0x735578` `CheckMapMonByName`、`0x73689C` `GetCastleGuildName`）；PAS 随机走 `NativePasRandomContract`；`PasScriptHost` 成功路径不得另立第二套 `m_NPC` 权威。

---

## 0. 判定计数

对账单位是**可独立验证的契约项**（加载/点击/跳转/商店/生命周期 + 词法引擎 + 函数面 + 失败语义），不是 541 个函数名各算一条。函数名覆盖单独见 §2。

| 判定 | 项数 |
|---|---|
| FAITHFUL | **18** |
| DIVERGENT | **5**（其中 3 项本轮已修，见 §6） |
| MISSING | **4**（3 个真脚本 API + 1 个 `Free` 析构） |
| INVENTED | **12**（全镜像 0 命中的登记名/别名；已 fail-closed 或仅别名） |
| BLOCKED | **6** |

生产热路径（`Give` 112 文件 / `Take` 72 / `DecGold` 70 / `AddGold` 20）的失败语义与守恒，字节上与 C# **一致**（见 §3、§4）。

---

## 1. NPC 系统对账表

| # | 项 | 判定 | 原生证据 | C# | 玩家可见后果 |
|---|---|---|---|---|---|
| N1 | 商人加载 `Merchant.txt` | FAITHFUL | 字段序 script/map/x/y/name/flag/appr/castle/canMove/moveTime 与战神商人表惯例一致；本轮未重反加载器 | `LocalDB.LoadMerchant` `LocalDB.cs:218-280` | 对不上地图的商人初始化失败并剔除 |
| N2 | PAS NPC 加载 `PsNpcScript.txt` | FAITHFUL | 生产树存在该文件；C# 按空格切 script/map/x/y/name/dir/appr/castle | `LocalDB.LoadPsNpcScriptNpcs` `LocalDB.cs:283` | PAS NPC 进 QuestNPC 列表 |
| N3 | 商人/NPC 入图 | FAITHFUL | `m_boAddtoMapSuccess && !m_boIsHide` 失败则剔除 | `UserEngine.MerchantInitialize` `UsrEngn.cs:431-464` | 坐标非法的商人不上图 |
| N4 | 点击查找 + 同图 + 距离 ≤15 | FAITHFUL | 点击器 `sub_6B8B28`：`0x6B8B78` 比 `[esi+0x128]` 地图；`0x6B8B94 call 0x76B4A4` / `0x6B8B99 cmp eax,0xF` / `ja`；另一臂 `0x6B8C36 call 0x7743E0`（`cx=0xF`）。`0x7743E0` 是切比雪夫：`abs(dx)`、`abs(dy)` 均 `<=` 半径才 `al=1` | `TPlayObject.ClientClickNPC` `TPlayObject.cs:1201` `Math.Abs<=15` | 超 15 格点不到 |
| N5 | 点击后写 `player+0xCD8` | FAITHFUL（本轮修） | `0x6B8BA2 call 0x720444` **之后** `0x6B8BA7 mov [ebx+0xCD8],esi`；`0x6B8C43 call 0x63DC74` **之后** `0x6B8C48 mov [ebx+0xCD8],esi`。全镜像 `disp 0xCD8` 只有这两处直接写 + setter `0x63DFAF`。**没有任何写 0** | 曾在 `TryCallNpcLabel` 调用前写入、失败再清空。现：`ClientClickNPC` 在 `Click()` **返回后** 赋值；`PasScriptHost` 不再碰该字段 | 第一次点 NPC 时 `@main` 里的 `Give` 审计看的是旧/空 `m_NPC`（原生如此）；标签未命中不再丢掉绑定 |
| N6 | 点击失败给 GM 回「找不到NPC」 | DIVERGENT | 两臂查找失败：`0x6B8BB2` / `0x6B8C50` `cmp byte [ebx+0x675],3` / `jbe` 静默；`>3` 则 `0x6B8BCF mov edx,0x6B8CB8` `'找不到NPC'` + `cx=0x38FF` + vtbl `+0xD4`。`[esi+0x675]` 与给物错误臂 `0x6C8AEC` 同字段，C# 给物已按 `m_btPermission>3` 对齐 | `ClientClickNPC` 查找失败对所有权限静默 | 仅 GM（权限>3）点无效 ident 时少一条红字；普通玩家两边都静默 |
| N7 | 隐藏 NPC 跳过距离门 | BLOCKED | 第二臂 `0x6B8C17 cmp byte [esi+0x45C],0` / `je 0x6B8C3F` 跳过地图与 `0x7743E0`。C# 无条件检查距离 | 缺 `+0x45C` 字段身份（不是 `m_boIsHide` 的现成对应，未在本轮钉死） | 若该字节=0 的 NPC 可被远处点击，C# 点不到 |
| N8 | `GotoLable` / 菜单跳转 | FAITHFUL | `sub_63DC98`：调脚本引擎，成功打「点击NPC成功」日志，失败打失败日志，**不向玩家发气泡**。C# `SendNpcFallbackDialog` 已掏空 | `NormNpc.GotoLable` → `TryGotoPascalLabel`；`Merchant.UserSelect` 先 `TryGotoPascalLabel` 再硬编码 `@buy` 等 | 无 `@main` 时玩家无气泡（与原生一致） |
| N9 | 商店 `@buy/@sell/@repair/@storage` | FAITHFUL | 镜像有 `@buy` `0x6D5910`、`@sell` `0x7CA223`、`@repair` `0x63F148`、`@storage` `0x7BF612`。声明表另有 `Click_Buy` 等 PAS 方法 | `Merchant.UserSelect` `Merchant.cs:1316`：PAS 标签优先，未命中再走 `m_boBuy` 等开关 | 生产脚本用 PAS；硬编码是 PAS 未处理时的原生回落 |
| N10 | 卖价 × 堆叠 | FAITHFUL | `sub_63F3B4` `0x63F442 cmp byte [eax+0x14],7` 后 `imul` 数量。已在 `Merchant.GetUserItemPrice` | `Merchant.cs:1645` 注释带字节 | 堆叠物按数量计价 |
| N11 | 动态 NPC 生命周期 | FAITHFUL | 有独立物化/回滚日志 + 审计工具 `DynRoomDynamicNpcScriptBindingPlannerCheck` / `DynRoomPasScriptRouteCheck` | `NativeDynamicRoomNpcMaterializer` 等 | 动态房 NPC 提交失败会补偿，不留半成品 |
| N12 | 脚本绑定 `ResolveNpcScript` | FAITHFUL | 动态房走路由表；普通 NPC 按 `PsNpcscripts` 名解析 | `PasScriptHost.ResolveNpcScript` | 动态房 NPC 找不到绑定时 `DynamicUnavailable`，不误跑默认脚本 |

---

## 2. 内置函数集对比

权威源：镜像 Delphi 长串（refcount=`-1`）里 `function`/`procedure` 声明。`0x50xxxx` = PascalScript 运行时；`>=0x729B0C` = M2 游戏 API。

| 口径 | 数量 |
|---|---|
| 声明串总数 | 623（去重名 603） |
| PascalScript 运行时（VA `<0x600000`） | 62 |
| M2 游戏 API（VA `>=0x720000`） | **541** |
| C# `case` 标签能对上 M2 名 | **537 / 541** |
| M2 名完全不在 C# 派发 | **4** |
| C# 解释器 builtins | funcs 91 + procs 15（与 M2 派发有重叠） |

### 2.1 MISSING（M2 声明有、C# 零 case）

| 名 | VA | 声明 | 生产命中 | 说明 |
|---|---|---|---|---|
| `BeginTransLog` | `0x7304C8` | `procedure BeginTransLog(Logtype:integer);` | 0 | 事务日志起止，生产未用 |
| `EndTransLog` | `0x7304FC` | `procedure EndTransLog;` | 0 | 同上 |
| `OpenMilRank` | `0x730474` | `procedure OpenMilRank;` | 0 | 打开军衔 UI，生产未用 |
| `Free` | `0x72BE44` | `procedure Free` | — | TObject 析构，不是脚本会调的 API |

### 2.2 名字在 C# 里、行为是 `RejectUnsupportedNativeApi`

`CallPlayerFunc` 优先于 `CallPlayerMethod`。同一名字两边都有 case 时，**Func 的实现盖掉 Method 的 reject**（例 `AddGloryPoint`：Func `PasApiBridge.cs:3775` 有实现，Method `:2467` 的 reject 是死代码）。

本轮**未**把全部 541 个名字拆成「Func 实现 / Method 死 reject / 两边都 reject」。能钉死的 fail-closed 例子：

| 名 | 原生声明 VA | C# | 判定 |
|---|---|---|---|
| `UseGuildPoint` / `GetSomeGuildPoint` / `SetWineTreat` / `GetTreatWine` / `ConvertVExp` | 均在 `>=0x720000` 声明表 | `CallStandaloneFunction` 直接 `RejectUnsupportedNativeApi` | DIVERGENT（脚本拿到 false/空，原生会改状态） |
| `UpdateEverydayActOrder` | 声明表有 | 同上 | DIVERGENT |
| `CreditPoint` / `GuildPoint` 属性 | 声明/RTTI 需再钉 | 属性 getter reject | BLOCKED（见 B3） |

### 2.3 INVENTED（声明表无 + 全镜像 ASCII/UTF-16LE 子串 0）

这些 C# 有 `case`，但 `flat_image.bin` ascii=0 且 utf-16le=0，生产 ident 边界也是 0。C# 多数已 `RejectUnsupportedNativeApi`，运行期等于没有。

`npc_creatmons`、`lmcreatemon`、`lmsetysid`、`lmcheckmapmon`、`lmgetitemid`、`lmgetysid`、`serveraay`（`ServerSay` 的错拼）、`chgmonitempercent`、`adddiamond`、`max_hp`、`max_mp`、`my_lfnum`、`peiyou_name`

`lingfuvalue` 镜像 ascii 子串=1，未达「三者皆 0」，**不**标 INVENTED（可能是别的串的片段）。

PascalScript 运行时 62 名里 C# builtins 只覆盖 20。缺的 42 个包括 `Sin/Cos/Pi/PadL/Assigned/RaiseException/...`。生产 PAS 几乎不裸调这些；缺了会在 `ExecuteCall` 里抛「函数找不到」中断该标签。标 **MISSING（运行时子集）**，不并进上面 4 个 M2 MISSING。

### 2.4 已核实的热路径名字

`Give` `0x72C818`、`BindGive` `0x72C864`、`LoopGive` `0x72C8B4`、`Take` `0x72C91C`、`AddGold` `0x72D07C`、`DecGold` `0x72D0B0`、`CheckMapMonByName` `0x735578`、`GetCastleGuildName` 在声明表（此前被误判眼神臆造）。C# 均有 case。

---

## 3. 失败语义表（重点）

「脚本继续」= 不抛异常、后续语句仍执行。给物/给钱的 **Boolean 返回值** 是另一条轴。

| 命令 | 原生失败怎么走 | C# | 判定 |
|---|---|---|---|
| 未知全局函数 | PascalScript 抛异常；`GotoLable` `0x63DC98` 有 `fs:` 帧，吞掉后打失败日志，**不给玩家气泡**；已执行的副作用保留 | `PasInterpreter.ExecuteCall` 抛 `PasRuntimeException`；`TryCallLabelCore` catch 后打 `[PasEngine] Error` 并 `return false` | FAITHFUL |
| 未知 `This_Player.X` | 同上，中断当前标签 | `ExecuteMethodCall` 抛 `函数找不到: 'This_Player.X'`，同样被 host catch | FAITHFUL |
| `Give` 空名 / count≤0 | `sub_6DF2E8` `test esi,esi` / `je` → 返回 false | `TryNativeGive` 空名 false | FAITHFUL |
| `Give('金币',n)` | `0x6C8951 add [esi+0x15C],ebx` **无上限**、不调 `GoldChanged` | `m_nGold = unchecked(m_nGold + count)` | FAITHFUL（与 `AddGold` 不是同一条路） |
| `Give('经验'/荣耀点/…)` | 走专用臂，`[ebp-6]` 在解析后已是 1，跳到 `0x6C8B27`；经验/荣耀点 **不**发「恭喜：你获得了：」（`0x6C8B2D`/`0x6C8B3C` `je` 跳过） | `showSuccess=false` 对经验/荣耀点 | FAITHFUL |
| `Give` 物品名不存在 | `0x6C8994 mov byte [ebp-6],0` 后查找 `je 0x6C8AEC`；权限>3 发 `'[错误]：不存在的奖品：'` `cx=0x38FF`；返回 false | `TryGiveNativeItems` `gaveAny=false`；configPrize + permission>3 发同样文案 | FAITHFUL |
| `Give` 背包满（一件都塞不进） | `0x6C8994` 先清结果；`AddItemToBag` 失败 `0x6C8A1C je 0x6C8ADA` → `FreeAndNil` `0x414C24` → `jmp 0x6C8B27`，结果仍为 0 → **无恭喜、返回 false、不掉地上** | `AddItemToBag` 失败 `Dispose` + `break`，`return gaveAny` | FAITHFUL |
| `Give` 部分成功 | 第一次成功 `0x6C8AD4 mov byte [ebp-6],1`；再失败则停循环、**已给的保留**、发恭喜、返回 true | 同样：成功置 `gaveAny`，失败 break | FAITHFUL |
| `LoopGive` | `0x6DF4F8`：名空或计数≤0 → false；否则循环调 inner，**丢弃 inner 的 al**，最后 `0x6DF530 mov byte [ebp-5],1` 恒 true | `TryNativeLoopGive` 同样丢弃 `TryNativeGive` 返回值后 `return true` | FAITHFUL |
| `Take` count≤0 | `0x6DF815` 非正计数 **返回 TRUE、不扫包** | `TakeItemsCore` `count<=0 return true` | FAITHFUL |
| `Take` 数量不够 | `0x6DF854..0x6DF862` 预检不足则 **零突变、返回 false**（全有或全无） | `CountBagItem < count return false` | FAITHFUL |
| `Take` 未知物品名 | `0x6DF842` 无名 → false | `GetStdItemIdx<=0 return false` | FAITHFUL |
| `AddGold` | `sub_6D791C`：`test edx,edx / jle` 拒 ≤0；`cmp ebx,[eax+0x68C] / jg` 超 `m_nGoldMax` 拒；成功才 `add` + `GoldChanged` `0x6C19B4` | `TPlayObject.IncGold` | FAITHFUL |
| `DecGold` | `sub_6C7D64`：`jl` 拒负数（**不改金币**）；`cmp edx,[eax+0x15C] / jg` 不够则 false；成功才 `sub` + `GoldChanged` | `TPlayObject.DecGold` | FAITHFUL |
| `AddGold`/`DecGold` 脚本层再调一次 `GoldChanged` | 原生只在 Inc/Dec 内部发一次 | `CallPlayerFunc` 成功后再 `GoldChanged()`。`RM_GOLDCHANGED` 无增量，是让客户端重读，双发冗余不是错账 | 可记 DIVERGENT-轻微 / 不修 |
| 标签无过程 | `GotoLable` 失败日志，玩家静默 | `TryCallNpcLabel` false；`SendNpcFallbackDialog` 已空操作 | FAITHFUL |
| `@main` 未发对话 | 原生静默 | 同上 | FAITHFUL |

`Give` 审计（经验/内功经验/荣耀点）原生读 `player+0xCD8`（`0x6DF341 cmp [edi+0xCD8],0 / je 0x6DF454`）。本轮已改为读 `m_NPC` 而非脚本 `CurrentNpc`。第一次点击的 `@main` 里该审计会被跳过——这是原生行为，不是漏日志。

---

## 4. 给物 / 给钱守恒

| 路径 | 守恒结论 | 证据 |
|---|---|---|
| `Give` 物品 | **部分发放 + 失败件 FreeAndNil，不掉地** | `0x6C8A14 call [edi+0x248]` AddItemToBag；失败 `0x6C8ADA call 0x414C24` | C# `Dispose`+`break` |
| `Give('金币')` | **无上限累加**，可超过 `m_nGoldMax` | `0x6C8951 add [esi+0x15C],ebx` 无 `jg` | 与 `AddGold`/`IncGold` 分流，禁止「修」成有上限 |
| `AddGold` | **有上限**，超则整笔拒绝、金币不变 | `0x6D7930 cmp ebx,[eax+0x68C] / jg 0x6D7943` | |
| `DecGold` | 不够则 **整笔拒绝、不扣**；负数拒绝（否则会变成加钱） | `0x6C7D69 test edx,edx / jl`；`cmp / jg` | |
| `Take` | **预检全有或全无**；堆叠可扣 Dura 留格 | `0x6DF854` 预检；`0x6DF93B jl` 部分扣耐久 | |
| `GotoLable_GiveItem` | 与 `Give` 物品同一发放器语义 | 注释对齐 `0x6C87B4` | `NormNpc.GotoLable.cs:96-135` |

生产用法几乎全是语句 `This_Player.Give('金条',1)`（解释器先走 `CallPlayerFunc`，Boolean 被丢掉）。背包满时原生/C# 都不给、不掉地、不中断后续脚本。

---

## 5. PAS 引擎（词法 / 作用域 / 控制流）

| # | 项 | 判定 | 说明 |
|---|---|---|---|
| P1 | 词法/语法 | BLOCKED | C# 是 PascalScript **再实现**，不是原生字节码解释器。没有把 `PasLexer`/`PasParser` 与 `0x50xxxx` 运行时逐 token 对过。生产脚本能跑不等于边角（`{$I}`、变参、`with`）逐字节等价 |
| P2 | `{$I}` 包含 | FAITHFUL-candidate | `PasIncludeResolver` + `PasScriptHost` 预处理；生产 100% 经 `{$I 眼神专用\AllFuc.pas}` 调眼神 API | 未反原生预处理器 |
| P3 | V/S 作用域 | FAITHFUL（本轮修） | 原生 GetV `0x6DF203 test esi,esi` → 组 0 走 `player+0x808`（`0x6DF20F mov eax,[ebx+eax*4+0x808]`），未写槽是 **0** 不是 -1。组≠0 走 `group*1000+index`，miss=-1（`0x6DF1F1`）。C# 曾在 `GetPlayerVar` 里自己算扁平键。现统一 `TryGetScriptVar`/`SetScriptVar`，门仍是 `NativeScriptVarArgsAccepted`（组 0 只收 V 的 index 1..100） |
| P4 | `PlayDice` | FAITHFUL（本轮修） | `0x645237 xor edx,edx` / `0x64523B call 0x6DF1E4` 读 GetV(0,1..10)。`PackDiceValues` 现走 `TryGetScriptVar('V',0,index)` |
| P5 | `SetV(n,f,0)` | FAITHFUL | upsert `sub_6E4140` 无零值删除；C# `SetScriptVar` 原样写 0 |
| P6 | 随机 | FAITHFUL | 已走共享 `RandSeed`（任务给定，未重开） |
| P7 | 标签名 `_main` / `main` / `@main` | FAITHFUL | `ExecuteLabel` 先加 `_` 再裸名 | 生产标签扫描：`_prefix` 2405、special 304、UNRESOLVED 39 |
| P8 | `This_NPC` 注册 | FAITHFUL | 原生 `0x73A9AC mov edx,0x73AA14` `'This_NPC'` | `PasScriptHost` 执行时注入 |

---

## 6. 本轮落地的修复（3 个提交）

| SHA | 修的 DIVERGENT | 字节 |
|---|---|---|
| `46652e21` | `GetPlayerVar`/`SetPlayerVar`/`PackDiceValues` 自己算 `group*1000+index` | `0x6DF20F` 组 0 不在字典；扁平键 `<1000` 只能是组 0 |
| `8cf97526` | `m_NPC` 在标签调用前写入、失败再清空 | `0x6B8BA7`/`0x6B8C48` 在 vcall **之后**；从不写 0 |
| `7d447b7b` | `Give` 审计用 `CurrentNpc` | `0x6DF341 cmp [edi+0xCD8],0` |

未修、仍挂在上面的 DIVERGENT：N6（GM「找不到NPC」）、N7（`+0x45C` 跳过距离，BLOCKED）、fail-closed 的真原生 API（`UseGuildPoint` 等）。

---

## 7. BLOCKED

| ID | 缺什么 | 卡在哪一层 |
|---|---|---|
| B1 | `PasLexer`/`PasParser` 与原生 PascalScript 运行时逐 token/异常文本 | 没有把 `0x50xxxx` 解释器对照 C# 再实现；不能宣称词法 1:1 |
| B2 | NPC `+0x45C` 字节身份 | 点击器第二臂用它跳过距离；C# 没有对应字段名。需要对象布局/RTTI，不是字符串扫描 |
| B3 | `CallPlayerFunc` vs `CallPlayerMethod` 双表 | 同一名字可能 Func 已实现、Method 仍 reject（死代码）。要逐名字看 Func 是否先命中，不能按 Method 的 reject 计 MISSING |
| B4 | 541 个 M2 API 的**实现**（参数、失败、副作用） | 本轮只对热路径 Give/Take/Gold/GetV 和点击器做了字节。其余只有「名字在 case 里」 |
| B5 | `0x63DFAF` setter 的 110 个调用点 | 哪些子系统合法写 `m_NPC`（商城、元宝购买等 C# 已有独立写入）未逐个对 |
| B6 | 商店硬编码臂与 PAS `Click_Buy` 在原生里的优先级 | C# 是 PAS 先、硬编码后；未反 `Merchant.UserSelect` 的原生过程确认顺序 |

---

## 8. 前人/注释订正

1. 「`PasScriptHost` 只在成功时写 `m_NPC`」——改之前的代码是**调用前写、失败清空**，与 `0x6B8BA7` 不符。现已按点击器「vcall 之后写、GotoLable 不碰」收口。
2. 台账/注释里把 `0x6DF4F8` 叫 Give：该函数是 **LoopGive**（`ret 4` 第三参、循环丢 inner 返回值）。真正的单次 Give inner 是 `0x6DF2E8` → `0x6C87B4`。Give 声明串在 `0x72C818`，注册点 `0x72B032`。
3. `0x6C8828 mov byte [ebp-6],1` 不是「解析完就成功」。物品臂在 `0x6C8994` **先清回 0**，只有 `AddItemToBag` 成功才在 `0x6C8AD4` 再置 1。
4. `Give('金币')` 不是 `IncGold`。用 `IncGold` 的上限去「修」Give 会少给超上限的脚本金币。

---

## 9. 建议优先级

1. 已落地的三处保持，不要把 `m_NPC` 写回 `TryCallNpcLabel`。
2. B3：按 `CallPlayerFunc` 为权威扫一遍 fail-closed 真 API，排出生产有调用的再补实现。
3. N6：GM 点击失败回 `'找不到NPC'`（`0x6B8CB8`，`cx=0x38FF`），权限门已有给物侧旁证（`+0x675` = `m_btPermission`）。
4. B1/B2/B4 保持 BLOCKED，不要用「C# 有 case」充 FAITHFUL。
5. `BeginTransLog`/`OpenMilRank` 生产 0 命中，补实现优先级低。

---

## 10. 提交

```
7d447b7b Give audit reads player.m_NPC (0x6DF341), not script CurrentNpc.
8cf97526 Bind player.m_NPC after NPC click vcall, matching 0x6B8BA7/0x6B8C48.
46652e21 Route GetV/SetV/PlayDice through TryGetScriptVar (0x6DF20F inline group-0).
```
