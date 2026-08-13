# NPC 脚本引擎第三轮：四个触发点 + 接收者归位 + fail-closed + GetG

- 工作树 `D:\loym2\.claude\wt2\m-npcscript`，分支 `w/m-npcscript`，已 `git merge --no-edit master`
- 镜像 `staging/_reunpack_work/flat_image.bin`，基址 `0x400000`
- 复现工具链在 `tools/npcscript_re/`：`_b8_*.py`（触发点）、`_wrongrecv*.py`（接收者）、
  `_invented_items.py`（四重 0 命中）、`_brace_check.py`（改完的结构自检）
- **未执行任何编译命令**

---

## 0. 一句话结论

四项全部落地。其中**第一项的结论与我上一轮报告不一致**，而且差别很大：
`@OnEnter` / `@OnLeave` / `@OnDie` **不是通用地图钩子，只在 `TDynEnvir` 动态地图上存在**；
普通地图（`TEnvironment`）四个里只有 `@OnReLive` 一个。上一轮那张表把四个并列成
「地图对象上的四个钩子」，是错的，本轮用 VMT 字节推翻。

---

## 一、BLOCKED-B8 解掉：四个触发点的上层调用者与时机

### 1.1 类树（VMT SelfPtr 反解，`vmtSelfPtr = -0x4C`、`vmtClassName = -0x2C`、`vmtParent = -0x24`）

```
TObject
 └ TEnvironment              VMT=0x77477C  InstanceSize=212 (0xD4)
    ├ TDynEnvir              VMT=0x5FB264  InstanceSize=264 (0x108)
    │   ├ TDynSuperForceMapEnvir   VMT=0x5F7B58  InstanceSize=296
    │   └ TFoxBossDungeonDynEnvir  VMT=0x5F9934  InstanceSize=288
    └ TArenaRoom             VMT=0x612C70  InstanceSize=260 (0x104)
```

构造点各一个，且都是从 SelfPtr 槽取类引用（所以按立即数扫 VMT 地址会 0 命中，这是上一轮
没找到构造点的原因）：

| 类 | 构造点 | 字节 |
|---|---|---|
| `TEnvironment` | `0x695B8E` | `A1 30 47 77 00 mov eax,[0x774730]` → `0x695B93 E8 60 EE 0D 00 call 0x7749F8` |
| `TDynEnvir` | `0x5FE2E9` | `A1 18 B2 5F 00 mov eax,[0x5FB218]` → `0x5FE2EE E8 55 F1 FF FF call 0x5FD448` |
| `TArenaRoom` | `0x613751` | `A1 24 2C 61 00` |
| `TDynSuperForceMapEnvir` | `0x5F84B5` | `A1 0C 7B 5F 00` |
| `TFoxBossDungeonDynEnvir` | `0x5FB1D1` | `A1 E8 98 5F 00` |

**`0x695B8E` 是全镜像唯一的 `TEnvironment` 构造点**，它在地图管理器里 —— 普通地图是
`TEnvironment`，这一点是定死的。

### 1.2 VMT 槽位对照（决定性证据）

| 槽 | TEnvironment | TDynEnvir | TArenaRoom | 语义 |
|---|---|---|---|---|
| +0x00 | `0x77A014` | **`0x5FD574`** | `0x613D6C` | DeleteObject → `@OnLeave` |
| +0x04 | `0x779F68` | **`0x5FD534`** | `0x613D3C` | AddObject → `@OnEnter` |
| +0x08 | `0x779F64` = 裸 `C3` | **`0x5FD4D4`** | 继承（裸 `C3`） | ObjectDied → `@OnDie` |
| +0x10 | **`0x77BB38`** | **`0x5FD384`** | 继承 `0x77BB38` | AutoRelive → `@OnReLive` |
| +0x28 | `0x7776EC` | `0x5FD65C` | 继承 | AddToMap |

四个派发器的调用点（全镜像 `E8` 反查，无遗漏）：

| 派发器 | VA | 调用点 |
|---|---|---|
| `@OnEnter` | `0x6468C8` | `0x5FD56A`（仅此一处） |
| `@OnLeave` | `0x6468F8` | `0x5FD5A3`（仅此一处） |
| `@OnDie` | `0x646928` | `0x5FD50A`（仅此一处） |
| `@OnReLive` | `0x646954` | `0x5FD3B2`、`0x77BB66` |

**推论**：`0x5FD5xx` 三个宿主全在 `TDynEnvir` 的 VMT 里；`TEnvironment` 的对应槽
（`0x77A014` / `0x779F68` / `0x779F64`）逐字节读过，**一次都不碰派发器**，其中
`0x779F64` 整个函数就是一个 `C3`（后面 `8D 40 00` 是对齐填充）。
旁证：`[map+0xD8]` 这个人数字段落在 `0xD4..0x108`，**TEnvironment 的 212 字节实例里没有
这个偏移**，只有 TDynEnvir 才有。

### 1.3 上层调用者与调用时机

#### `@OnEnter` ← `TEnvironment.AddToMap`（VMT+0x28 = `0x7776EC`）

```
00777A72  B8 10 00 00 00        mov eax,0x10 / call 0x402FA0   ; 新建 16 字节格子节点
00777A7F  8B 45 0C              mov eax,[ebp+0xC]              ; AddObj
00777A82  8A 40 04              mov al,[obj+4]                 ; 格子类型字节
00777A88  88 02                 mov [node+0],al
00777A90  89 50 04              mov [node+4],edx               ; node.CellObj = AddObj
00777AB1  8B 45 0C              mov eax,[ebp+0xC]
00777AB4  8A 40 04              mov al,[obj+4]
00777AB7  FE C8 / 75 0E         dec al / jne 0x777AC9          ; 只有 OS_MOVINGOBJECT
00777ABB  8B 4D E8              mov ecx,node
00777ABE  8B 55 0C              mov edx,AddObj
00777AC6  FF 53 04              call [map_vmt+0x04]
```

宿主 `TDynEnvir.AddObject 0x5FD534`：

```
005FD541  E8 22 CA 17 00        call 0x779F68            ; inherited 先跑（inc [map+0xC4]）
005FD546  85 F6 / 74 25         test esi,esi / je
005FD550  80 B8 78 01 00 00 00  cmp byte [obj+0x178],0   ; m_btRaceServer
005FD557  75 16                 jne -> 跳过              ; 只有 RC_PLAYOBJECT(0)
005FD559  FF 83 D8 00 00 00     inc dword [map+0xD8]     ; 人数 +1
005FD55F  8B 93 A4 00 00 00     mov edx,[map+0xA4]       ; QuestNPC
005FD565  85 D2 / 74 06         test edx,edx / je
005FD569  92                    xchg edx,eax
005FD56A  E8 59 93 04 00        call 0x6468C8
```

**时机**：对象被放进地图格子时；**人数先加再派发**，脚本读到的在场人数**已经包含自己**。

#### `@OnLeave` ← `TEnvironment.DeleteFromMap`（`0x7794A8`）

```
00779517  80 38 01              cmp byte [node],1        ; OS_MOVINGOBJECT
0077951F  3B 58 04              cmp ebx,[node+4]         ; 命中要摘的对象
00779546  FF 11                 call [map_vmt+0x00]
```

宿主 `TDynEnvir.DeleteObject 0x5FD574`：

```
005FD57D  80 3E 01              cmp byte [node],1
005FD589  80 B8 78 01 00 00 00  cmp byte [obj+0x178],0 / 75 3B jne
005FD592  FF 8B D8 00 00 00     dec dword [map+0xD8]     ; 人数 -1
005FD598  8B 93 A4 00 00 00     mov edx,[map+0xA4]
005FD5A3  E8 50 93 04 00        call 0x6468F8
005FD5A8  83 BB D8 00 00 00 00  cmp dword [map+0xD8],0 / 7F 1C jg
005FD5D1  E8 3E CA 17 00        call 0x77A014            ; inherited，**在派发之后**
```

**时机**：对象被摘出地图格子时；**人数先减再派发**，脚本读到的人数**已经排除自己**。

注意 inherited 的位置与 `@OnEnter` 相反。这一点我特意逐字节核了基类
`TEnvironment.DeleteObject`（`0x77A014..0x77A172` 全读完）：它**从不递减 `[map+0xC4]`**，
只做「摘哈希 `0x49EEE4`、摘 NPC 表、`[map+0xBE]`/`[map+0xC0]` 等级复检」。所以两个计数器
的相对顺序对脚本不可观测，C# 侧不必为此调换 `AddDynamicRoomPlayer` / `AddObject` 的顺序。

#### `@OnDie` ← `sub_76631C`（置死亡位的那个函数）

```
0076631C  55 8B EC 53 56        prologue
00766321  8B D8                 mov ebx,eax
00766323  C6 43 74 01           mov byte [obj+0x74],1    ; m_boDeath := TRUE
00766327  E8 14 20 CA FF        call 0x408340            ; GetTickCount
0076632C  89 83 30 03 00 00     mov [obj+0x330],eax      ; m_dwDeathTick
00766341  8B B3 28 01 00 00     mov esi,[obj+0x128]      ; PEnvir
00766347  85 F6 / 74 09         test esi,esi / je
00766351  FF 51 08              call [map_vmt+0x08]
00766358  E8 07 00 00 00        call 0x766364            ; 之后才做清理
```

**时机**：写完死亡位与死亡时间戳**立刻**派发，早于任何清理。脚本看到的是「已经死了」的状态。

顺带这也钉死了 **`PEnvir = [obj+0x128]`**（两处独立佐证：`0x766341` 与 QuestNPC 绑定器的
`0x77AE80 mov [npc+0x128],esi`）。

#### `@OnReLive` ← `sub_7436F8`（复活裁决，THumanKind VMT+0x08）

```
00743921  84 DB / 75 23         test bl,bl / jne         ; 已经靠戒指等复活过就不走这条
00743925  8B 86 28 01 00 00     mov eax,[obj+0x128]
0074392B  80 78 7E 00           cmp byte [map+0x7E],0    ; AUTORELIVE 地图标记
0074392F  74 17                 je  -> 跳过
0074393B  FF 51 10              call [map_vmt+0x10]
0074393E  83 BE AC 02 00 00 00  cmp dword [obj+0x2AC],0  ; HP
00743945  0F 9F C3              setg bl
```

宿主 `0x77BB38`（TEnvironment）与 `0x5FD384`（TDynEnvir）**字节级同构**：

```
0077BB46  80 BB 78 01 00 00 00  cmp byte [obj+0x178],0 / 75 26 jne -> false
0077BB4F  80 7B 73 00           cmp byte [obj+0x73],0  / 75 20 jne -> false   ; ghost 位
0077BB55  83 B8 A4 00 00 00 00  cmp dword [map+0xA4],0 / 74 17 je  -> false
0077BB66  E8 E9 AD EC FF        call 0x646954
0077BB6B  83 BB AC 02 00 00 00  cmp dword [obj+0x2AC],0
0077BB72  0F 9F C2              setg dl                  ; 返回 HP > 0
```

**时机**：玩家 HP 归零后的复活裁决里，且只在 `AUTORELIVE` 地图上。
**语义**：脚本负责把血加回来，宿主只回报「加成功了没有」。
`[map+0x7E]` = `AUTORELIVE`，两个解析器都写它：`0x77567F BA 78 5E 77 00`（GM 开关臂，
`0x775E78 "AUTORELIVE"`）→ `0x775694 C6 43 7E 01`；`0x7766EF BA B8 6D 77 00`
（MapInfo.txt 臂）→ `0x776700 C6 43 7E 01`。

这条同时填上了 `TBaseObject.NativeRevive.cs` 里那句「Envir vtbl slot +0x10 is not
resolved」的 BLOCKED。

### 1.4 `[map+0xA4]`（QuestNPC）怎么解析

**两条绑定路径，都逐字节反出来了。**

① **普通地图：MapInfo.txt 的 `CHECKQUEST(<名>)` 标记**

```
00776312  B9 0A 00 00 00        mov ecx,0xA
00776317  BA 40 6C 77 00        mov edx,0x776C40      ; "CHECKQUEST"（10 字符）
0077631C  E8 7A 0B D5 FF        call 0x4C6E94         ; 前缀比较
00776333  E8 2C 06 D5 FF        call 0x4C6964         ; 取 '(' 与 ')' 之间的文本
0077633D  E8 92 4A 00 00        call 0x77ADD4
00776342  89 83 A4 00 00 00     mov [map+0xA4],eax
```

`0x77ADD4` 的内容：

```
0077AE09  68 40 AF 77 00        push 0x77AF40         ; 'PsMapQuest\'（11）
0077AE0F  68 54 AF 77 00        push 0x77AF54         ; '.pas'（4）
0077AE1C  E8 6F AA C8 FF        call 0x405890         ; 拼 <基目录>PsMapQuest\<名>.pas
0077AE24  E8 03 21 C9 FF        call 0x40CF2C         ; FileExists
0077AE2B  0F 84 AB 00 00 00     je  0x77AEDC          ; 不存在 -> 打 0x77AF64 '[缺MapQuest脚本]:'
0077AE33  A1 A8 CF 63 00        mov eax,[0x63CFA8]    ; TPsNpc 类
0077AE38  E8 0B 2A EC FF        call 0x63D848         ; TPsNpc.Create
0077AE42  C6 83 5D 04 00 00 02  mov byte [npc+0x45D],2   ; 脚本种类 = 2（CHECKQUEST）
0077AE52  ... [npc+0x458] = 全路径
0077AE7B  ... [npc+0x115] = [map+0x44]（地图名，15 字符）
0077AE80  89 B3 28 01 00 00     mov [npc+0x128],esi      ; npc.PEnvir = map
0077AEB6  ... [npc+0x106] = 括号里的名字（14 字符）
0077AED7  FF 52 78              call [npc_vmt+0x78]      ; Initialize：解析五个入口点、
                                                          ; 写 [npc+0x594..0x598] 存在位
```

② **动态地图：无条件创建**

```
005FE342  A1 A8 CF 63 00        mov eax,[0x63CFA8]    ; TPsNpc
005FE347  E8 FC F4 03 00        call 0x63D848         ; TPsNpc.Create
005FE351  89 98 A4 00 00 00     mov [map+0xA4],ebx
005FE362  C6 83 5D 04 00 00 01  mov byte [npc+0x45D],1   ; 脚本种类 = 1（动态房间）
005FE373  89 83 28 01 00 00     mov [npc+0x128],eax
005FE382  ... [npc+0x458] = [roomdef+0x18]（脚本路径来自房间定义）
```

清理点：`0x5FDDE0 89 90 A4 00 00 00 mov [map+0xA4],edx`（edx=0）。

### 1.5 生产实测（决定优先级的关键事实）

| 探针 | 结果 |
|---|---|
| `D:\光头卧龙\mud2.0\Mir200\Envir\mapinfo.txt` 里的 `CHECKQUEST` | **0 处**（全文件 4029 行） |
| 同文件里的 `AUTORELIVE` | **0 处** |
| `Envir\PsMapQuest\` 目录 | 存在，8 个 `.pas`（`RunQuest.pas`、`LogonQuest.pas` 等眼神触发脚本） |
| `Envir\` 下的 `DynRoom*` 配置目录 | **不存在** |

所以在这套生产部署上，四个触发点**当前都够不着**：普通地图没有 `CHECKQUEST` 就没有
QuestNPC，动态房间没有配置就不会被创建。`PsMapQuest\` 里那 8 个脚本是眼神走
`YanshenTriggerDispatch` 那条路的，不是这四个入口。

**这不改变要不要补的判断**（原版有就得有，见 §1.3 铁律），但改变了紧急度：
上一轮我写「这是本轮影响面最大的一条」，按生产证据应当下调 —— 它是**能力缺口**，
不是**当前正在丢事件**。

---

## 二、落地情况

### 2.1 四个触发点（提交 `8e34a088`）

新增 `GameSvr/Maps/Envirnoment.MapQuestTriggers.cs`，把 `Envirnoment` 改成 `partial`。
四个方法逐条对应上面的字节，门也照抄：

| C# 方法 | 对应原生 | 门 |
|---|---|---|
| `NativeDynEnvirAddObjectTrigger` | `0x5FD534` 尾部 | `IsDynamicRoom` + `m_btRaceServer == RC_PLAYOBJECT` + `QuestNPC != null` |
| `NativeDynEnvirDeleteObjectTrigger` | `0x5FD574` | 同上 |
| `NativeDynEnvirObjectDiedTrigger` | `0x5FD4D4` | 同上 + `!m_boGhost`（`[obj+0x73]`） |
| `NativeEnvirAutoReliveSlot` | `0x77BB38` / `0x5FD384` | **无类门** + race + `!m_boGhost` + QuestNPC，返回 `m_WAbil.HP > 0` |

接入点：

| 位置 | 对应原生 |
|---|---|
| `Envirnoment.AddToMap`（`OS_MOVINGOBJECT` 分支，两个计数器之后） | `0x777AC6` |
| `Envirnoment.RemoveMovingObjectRegistration`（人数摘除成功后） | `0x779546` |
| `TBaseObject.Base.cs` `Die()`，`m_boDeath = true` + `m_dwDeathTick` 之后 | `0x766351` |
| `TBaseObject.NativeRevive.cs`，`!revived && flag.boAUTORELIVE` 臂 | `0x74393B` |

「过程存在位 `[npc+0x595..0x598]`」在 C# 没有独立存储，但语义等价：`NormNpc.GotoLable`
走 `PasEngine.TryCallNpcLabel`，过程不存在就返回 false 且对非 `@main` 标签不发气泡，
与 `0x6468D2 je` 的静默返回一致。

**C# 侧仍缺的一环（新 BLOCKED-B9）**：动态房间在 C# 里**不绑 `Envirnoment.QuestNPC`**。
`Envirnoment.QuestNPC` 只在 `Maps.cs:371 AddMapInfo(..., QuestNPC)` 被赋值，来源是
`CHECKQUEST`。动态房间那侧 `NativeDynamicRoomDynamicNpcScriptBindingPlanner` 只算出
`DynRoomScripts/DNpc_<房间名>.pas` 的路径，没有物化成 NormNpc、也没写回 `QuestNPC`。
所以 `@OnEnter`/`@OnLeave`/`@OnDie` 这三条现在是**接好了线但没有电**。补它属动态房间
子系统的地盘，我没有跨界改。

### 2.2 34 个名字的接收者归位（提交 `14218420`）

上一轮说「36 条」，那是**注册条目数**；去重后是 **34 个不同的名字**
（`EAOrderIsStart` / `GetCurrentEANameByIdx` / `GetEAOrderInfo` 三个各自在
TPsNpc 与 global 上**都**注册，被数了两次）。

搬到 `CallNpcFunc`（原生 TPsNpc）20 条：`ChangeGPSwitch 0x734ADC`、
`GetAroundMonNum 0x7349EC`、`GetCurrentEAPeriod 0x734DF4`、`GetCurrentEAIdxByName 0x734E00`、
`GetCurrentEANameByIdx 0x734E0C`、`GetCurrentEAScoreByIdx 0x734E18`、
`GetLastEAIdxByName 0x734E24`、`GetLastEANameByIdx 0x734E30`、`GetLastEAScoreByIdx 0x734E3C`、
`GetEAOrderInfo 0x734E48`、`EAOrderIsStart 0x734E54`、`GetGuildWarGold 0x734D34`、
`UseGuildPoint 0x734AC4`、`GetSomeGuildPoint 0x734AD0`、`SetWineTreat 0x734E6C`、
`GetTreatWine 0x734E78`、`HeroRename 0x734E90`、`MoveAllHumInMap 0x734ECC`、
`NewFullMailEx 0x735070`、`UpdateEverydayActOrder 0x734DE8`

搬到 `CallPlayerFunc`（原生 TPlayer）8 条：`BuildGuild 0x72B1DD`、
`ChgEquipmentBreakLevel 0x72B741`、`GiveItemsToOther 0x72B7E9`、`InputDialog 0x72B910`、
`QueryTaskDispatch 0x72B9B8`、`ReqPieceUpNewYearPicture 0x72B65D`、
`RequestGuildWar 0x72B321`、`StartPaodian 0x72BAC0`

搬到 `CallStandaloneFunction`（原生 `AddFunction` 全局）7 条：`GetScoreByName 0x729A29`、
`KickAllHumToMap 0x7299E5`、`PlayerCry 0x729A4B`、`PlayerGive 0x729A5C`，
外加 `EAOrderIsStart 0x729A3A` / `GetCurrentEANameByIdx 0x7299F6` / `GetEAOrderInfo 0x729A18`
的全局那一半

`GetAnimalProperty` 新增 `Level`（`TAnimal.Level 0x73AED7`，与 `TPlayer.Level 0x72AB2F`
是两条独立注册）。

双注册的四个名字现在两半都在：`InputDialog`（TPlayer 3 参 / TPsNpc 4 参）、
`NewFullMailEx`（TPlayer 7 参 / TPsNpc 8 参）、`GetAroundMonNum`、`UpdateEverydayActOrder`。

**两个没有照搬、改成 fail-closed 的，理由在字节上**：

| 名字 | 原生声明 | 原来那份 C# 体 | 处置 |
|---|---|---|---|
| `ChangeGPSwitch` | `function ChangeGPSwitch(Player: TPlayer): Integer` | 把 `args[0]` 当整数写进 `V(25,11)`，不吃玩家对象也不返回值 | 与声明矛盾，搬过去等于把错误的实参解读扩散到第二个接收者 → `RejectUnsupportedNativeApi` |
| `GetAroundMonNum` | TPsNpc `(sMonName: string): Integer`；TPlayer `(sMonName: string; x,y,Rang: Integer): Integer` | 把 `args[0]` 当整数范围读，只打日志不返回计数 | 同上 → NPC 侧 `RejectUnsupportedNativeApi`；TPlayer 那份原样留着（它的接收者本来就对，签名问题归 B4） |

自检：`tools/npcscript_re/_wrongrecv_verify.py` 报 **32/32** 名字落在恰好正确的接收者
集合上，且没有任何一个 switch 出现重复 case 标签。

### 2.3 九条物品/金钱 API 改 fail-closed（提交 `abf7e48f`）

四重探针**全部 0 命中**（`tools/npcscript_re/_invented_items.py` 可复跑，
生产树扫了 3611 个 `.pas/.inc/.txt/.ini`）：

| 名字 | 注册表 | 裸 ASCII | UTF-16LE | 生产树 | 原可达位置 |
|---|---|---|---|---|---|
| `PsShopBuyGoods` | 0 | 0 | 0 | 0 | `CallPlayerMethod` + `CallNpcMethod` |
| `PsShopGetGoodsList` | 0 | 0 | 0 | 0 | `CallPlayerMethod` + `CallNpcMethod` |
| `TakeHeroBagExItem` | 0 | 0 | 0 | 0 | `CallPlayerMethod` |
| `TakeFromHeroBagEx` | 0 | 0 | 0 | 0 | `CallPlayerMethod` |
| `GetHeroBagExItemCount` | 0 | 0 | 0 | 0 | `CallPlayerFunc` |
| `GetHeroBagExItemCountEx` | 0 | 0 | 0 | 0 | `CallPlayerFunc` |
| `CheckGameGold` | 0 | 0 | 0 | 0 | `CallPlayerFunc` |
| `GetStorageItemCount` | 0 | 0 | 0 | 0 | `CallPlayerFunc`（恒返回 0） |
| `OpenStorageMax` | 0 | 0 | 0 | 0 | `CallPlayerMethod` |

其中三条是原生正名的错拼，而 C# **已经实现了正名**，所以改掉不丢功能：

| 错拼 | 原生正名 | C# 已有 |
|---|---|---|
| `TakeFromHeroBagEx` | `TakeFromHeroBag 0x72B285` | `case "takefromherobag"` |
| `GetHeroBagExItemCount` | `GetHeroBagItemCount 0x72B279` | `case "getherobagitemcount"` |
| `OpenStorageMax` | `OpenStorage 0x72BA3C` | `case "openstorage"` |

**没有动 Mall / YBShop 子系统的任何代码**，只动了 `PasApiBridge` 里的派发标签。

### 2.4 `GetG` 补门 + miss 改 -2（提交 `2a3ab7cb`）

本轮把 `sub_699198` 整个读完了，比上一轮多出几件事：

```
006991BF  BE FE FF FF FF        mov esi,0xFFFFFFFE       ; 默认 -2
006991C4  83 FB 01 / 0F 8C ..   cmp ebx,1  / jl  0x699290
006991CD  83 FB 32 / 0F 8F ..   cmp ebx,0x32 / jg 0x699290
006991D6  80 3D B0C47D00 00     cmp byte [0x7DC4B0],0    ; 缓存启用开关
006991DF  6B 55 FC 64           imul edx,[ebp-4],0x64    ; ParamNo * 100
006991E3  03 D3                 add edx,ebx              ; + index
006991E7  E8 30 1E 00 00        call 0x69B01C            ; 缓存查找（miss 也是 -2：
                                                          ; 0x69B040 B8 FE FF FF FF）
006991EE  83 FE FE / 0F 85 ..   cmp esi,-2 / jne 收尾
   ; 仍是 -2 -> 查数据库：'select g<index> from MirParams where ParamNo = <n>;'
   ;   0x6992C4 'g'、0x6992D0 'select '、0x6992E0 ' from MirParams where ParamNo = '、0x69930C ';'
00699248  E8 9B B9 08 00        call 0x724BE8            ; query
0069924D  85 C0 / 7E 30         test eax,eax / jle       ; 0 行 -> 保持 -2
00699277  83 CA FF              or edx,-1                ; StrToIntDef(field, -1)
0069928B  E8 BC 1D 00 00        call 0x69B04C            ; 写回缓存
006992B2  8B C6                 mov eax,esi              ; 返回值（在 SEH finally 之后）
```

**`0x6992B2 mov eax,esi` 这一句是关键**：`0x699290 xor eax,eax` 是 SEH 拆解，不是返回值，
只看前半段会误以为恒返回 0。

新发现：**`SetG`（`sub_699310`）有同一道门**，上一轮没查：

```
006993FD  83 FE 01 / 0F 8C 97000000   cmp esi,1     / jl 0x69949D
00699406  83 FE 32 / 0F 8F 8E000000   cmp esi,0x32  / jg 0x69949D
```

按 §4.19「读/写/持久化一起看」，C# 两侧都补了门：
`GetGlobalVar` 越界返回 `-2`（对应「那个 ParamNo 行不存在」），`SetGlobalVar` 越界返回 false。
`PasApiBridge:"getg"` 与 `PasInterpreter:"getg"` 的低参数兜底也从 0 改成 -2。

**消费端逐个核过再改的**：

| 消费端 | 影响 |
|---|---|
| `querytaskdispatch`（`GetGlobalVar(100,2)` / `(100,3)`，比较 `>=`） | index 2/3 在门内；set/unset 四种组合下分支结果全部不变 |
| `YanshenReplicaConfigForm:568`（GUI 显示 `G(1,index)`） | 显示 -2；原生 GM `@getg` 对同样输入也报 -2 |
| `NativeGmSystemCommands.EvalGetg` | 纯模型，不调 `GetGlobalVar`，不受影响 |
| AuditTools | 无任何工具断言 miss 值为 0（`YSGetG` 那些命中是眼神的字符串键全局，另一套 API） |

---

## 三、判定计数（本轮变化量）

| 判定 | 上一轮 | 本轮 | 说明 |
|---|---|---|---|
| MISSING（四个触发点） | 4 | **0**（代码已接）/ 3 条待通电 | 见 B9 |
| WRONG_RECEIVER | 36 条目 / 34 名字 | **0** | 32/32 自检通过 |
| INVENTED（物品金钱） | 8 | **0** | 实际改了 9 条（`GetHeroBagExItemCountEx` 是第 9 条） |
| DIVERGENT（`GetG`） | 1 | **0（门与默认值）** / 1 残留 | 「从未写过的槽读回 0 而非 -2」仍在 |
| BLOCKED | B1 / B4 / B7 / **B8** | B1 / B4 / B7 / **B9（新）** | B8 已解 |

---

## 四、剩余 BLOCKED

| ID | 缺什么 | 卡在哪一层 |
|---|---|---|
| **B1** | `PasLexer`/`PasParser` 与 `0x50xxxx` 运行时逐 token、注释形式、分词规则 | 没反原生词法器（沿用） |
| **B4** | 654 条 API 的**实现**（参数序、失败语义、副作用） | 本轮只对齐了注册面与接收者面。**本轮新增两个实证样本**：`ChangeGPSwitch` 与 `GetAroundMonNum` 的 C# 体与原生声明的参数形状直接矛盾 —— 说明「接收者对了」远不等于「实现对了」，这类矛盾很可能不止这两个 |
| **B7** | `TPsNpc.DoShowNpcEx 0x734F98` 的实现 | 决定那条含空格的死标签该激活还是该删（沿用） |
| **B9（新）** | C# 动态房间不给 `Envirnoment.QuestNPC` 赋值 | `NativeDynamicRoomDynamicNpcScriptBindingPlanner` 只算出 `DynRoomScripts/DNpc_<房间名>.pas` 的路径，没物化成 NormNpc。原生 `0x5FE351` 是**无条件** `TPsNpc.Create` 并写 `[map+0xA4]`。不补这一环，`@OnEnter`/`@OnLeave`/`@OnDie` 三条永远不会触发。属动态房间子系统 |
| **B10（新）** | `GlobalVal` 是预分配 `int[20000]`，没有「写过没有」的标记 | 所以「从未写过的槽」读回 0 而不是 -2。要忠实建模需要在读/写/INI 持久化三条路径上加一个 presence 集合，比本轮的改动大一档 |

---

## 五、对前人结论的订正（含我自己上一轮的）

1. **【最重要】上一轮 §4.1 那张表把四个 `@On*` 并列成「地图对象上的四个钩子」——错的。**
   `@OnEnter`/`@OnLeave`/`@OnDie` 的宿主只存在于 `TDynEnvir` 的 VMT 里；
   `TEnvironment` 的同槽实现 `0x77A014`/`0x779F68`/`0x779F64` 一次都不碰派发器，其中
   `0x779F64` 就是一个裸 `C3`。只有 `@OnReLive` 在所有地图上都有（`0x77BB66`）。
   判据：`[map+0xD8]` 越过了 `TEnvironment` 的 212 字节实例边界。
2. **上一轮把 `0x5FD392 cmp byte [ebx+0x178],0` 注为「对象已删」——错的。**
   `[obj+0x178]` 是 `m_btRaceServer`（0=玩家、0x36=英雄、>=0x32 为怪），这道门的意思是
   **只有玩家触发**。真正的「已删」是同一段里的 `[obj+0x73]`（ghost 位）。
3. **上一轮说 `@OnReLive` 的触发条件是「玩家复活」——不准确。**
   它是**死亡后的自动复活裁决**的一环，前提是地图有 `AUTORELIVE` 标记
   （`[map+0x7E]`）且此前没有别的东西（如重生戒指）已经复活过，返回值是
   `HP > 0`，也就是**脚本负责加血**。
4. **上一轮「36 条 WRONG RECEIVER」是注册条目数，不是名字数。** 去重后 34 个名字。
5. **`GetG` 只看前半段会读错返回值。** `0x699290 xor eax,eax` 是 SEH 拆解，
   真正的 `mov eax,esi` 在 SEH finally 之后的 `0x6992B2`。
6. **`GameSvrConfig.cs:1407` 上 `GlobalVal` 的注释「nTaskNo*1000+nFieldNo」与字节矛盾。**
   `GetG`/`SetG` 的乘数是 **100**（`0x6991DF 6B 55 FC 64`）。该注释要么是别的子系统的，
   要么就是错的；`GlobalVal` 同时被 `NormNpc.cs:1336/1364` 用 `nVarValue-100` / `-700`
   的第三套索引方式访问，属 §4.18「一个存储三个权威」，值得单独排一次。
7. **`SetGlobalVar(200, 1, ...)`（`PasApiBridge.cs:6218`）恒失败。**
   `flat = 200*100+1 = 20001 >= GlobalVal.Length(20000)`。既有缺陷，不是本轮引入的，
   但它说明「群号上限 199」这条隐含约束没人知道。
8. **生产 `mapinfo.txt` 里 `CHECKQUEST` 与 `AUTORELIVE` 都是 0 处。**
   上一轮把四个触发点判为「本轮影响面最大」，按生产证据应下调为能力缺口而非在线故障。

---

## 六、建议优先级

1. **B9：给动态房间绑 `QuestNPC`** —— 不做这一步，本轮接好的三条线不通电。
2. **B4 抽样**：`ChangeGPSwitch` / `GetAroundMonNum` 两个已证实的签名矛盾说明
   「C# 有 case」离「实现等价」还很远。建议按**原生声明串的参数个数与类型**对全部 654 条
   做一次机器比对（声明串已经在 `docs/m_npcscript_native_registry_20260813.txt` 里），
   这是低成本高产出的一轮。
3. **B10 / §5.6 的 `GlobalVal` 三套索引** —— 属 §4.18 双权威隐患，动之前要三条路径一起看。
4. B1 / B7 保持 BLOCKED。

---

## 七、本轮提交

```
8e34a088  Wire up the four map-quest script triggers; resolve Envir VMT+0x10.
14218420  Put 34 script APIs back on the receivers native registers them on.
abf7e48f  Fail-close nine invented item/money script APIs.
2a3ab7cb  Give GetG its 1..50 index gate and its -2 miss value.
```

**未执行任何编译命令**；改完用 `tools/npcscript_re/_brace_check.py` 对四个被改文件做了
花括号/圆括号配平自检（对照 `git HEAD`），全部 `bal=0 / pbal=0`。这不等于能编译过，
请主代理串行编译并回传错误。
