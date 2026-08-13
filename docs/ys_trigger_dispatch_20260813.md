# 眼神「触发」族：回调派发子系统

证据源：眼神脱壳转储 `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin`
（基址 `0x10000000`）、M2Server 平坦镜像 `staging/_reunpack_work/flat_image.bin`（基址 `0x400000`）、
生产配置 `D:/光头卧龙/mud2.0/Mir200/Gs1/config.json`（380 键，GBK）。
复现脚本 `staging/_ys_trig/`（只读）。

---

## 一、机制的统一形状

这一族开关**一个补丁站点都没有**，全部走 REPLICATION_RULES §5.1.0.6 的第三种手段
——trampoline。所以「按补丁站点数点名，发现零命中，判死键」这条推理对整族无效。

### 1. 桩体是怎么产生的

安装器两个：`0x10032CC0`（71 站点）、`0x10032FD0`（30 站点）。参数形状（由
`0x10032CEE mov esi,[ebp+8]` / `0x10032CF4 mov eax,[ebp+0x10]` / `0x10032CF7 mov ebx,[ebp+0x18]` /
`0x10032D9E cmp [ebp+0x1C],edx` 反出）：

| 槽 | 含义 |
|---|---|
| `[ebp+0x08]` | `&out`（12 字节输出结构，记录代码块地址） |
| `[ebp+0x0C]` / `[ebp+0x10]` | 宿主挂载 VA（两份，前者用于算位移） |
| `[ebp+0x14]` | 续跑 VA |
| `[ebp+0x18]` | `&code`（dword 数组） |
| `[ebp+0x1C]` | count |

编码规则（`0x10032DB0..0x10032E40`）：逐个 dword 取**低字节**写成机器码；只有
「`0xE9`/`0xE8` 后紧跟一个 `> 0xFF` 的 dword」才被当作绝对目标转成 rel32（`0x10032DB3 cmp ebx,0xE9` /
`0x10032DBB cmp ebx,0xE8` / `0x10032DCD cmp ecx,0xFF / jle`）。数组末尾恒为 `0xE9`，
安装器在 `0x10032E4C..0x10032E87` 补上回跳 resume 的 rel32。

数组本身来自 `.rdata` 的 16 字节常量块（`movaps/movups` 拼装）或 `rep movsd` 单块模板。
**模板是纯数据，没被 Themida 虚拟化**，所以整族可以直接解码。

### 2. 脚本标签是运行期回填进去的

`.rdata` 模板里那条 `push imm32` 的立即数是**占位符**（例如 `召唤骷髅触发` 与 `挖矿触发`
共用同一个 `0x015D0100`），不是事件号。真串由插件在栈上现搭 Delphi 长串记录再注册：

```
0x100AE325  C7 85 60 FF FF FF FF FF FF FF   mov [ebp-0xA0], 0xFFFFFFFF   ; refcount = -1
0x100AE32F  C7 85 64 FF FF FF 0C 00 00 00   mov [ebp-0x9C], 0xC          ; length = 12
0x100AE339  C7 85 68 FF FF FF 40 53 75 6D   mov [ebp-0x98], '@Sum'
0x100AE343  C7 85 6C FF FF FF 6D 6F 6E 53   mov [ebp-0x94], 'monS'
0x100AE353  C7 85 70 FF FF FF 6B 65 6C 65   mov [ebp-0x90], 'kele'       ; "@SummonSkele"
...
0x100AE39E  E8 .. call 0x10033450
```

`0x10033450(this=游标, &记录, 字节数, 0x100, backoff, 代码末尾, 上次块)`：
`0x10033524 memcpy(cursor+0xA, &记录, 字节数)` 把记录拷进 VirtualAlloc 池，
`0x10033536 movzx eax,[edi+4] / sub esi,eax` 算出**字符指针**，
`0x10033551 sub eax,[ebp+0x14] / 0x10033554 mov [eax-4],esi` 把它写到
`代码末尾 - backoff - 4` —— 实测正好落在刚生成那条 `push` 的 imm32 上。
（校验：`@SummonSkele` backoff=0x12，桩体 35+4=39 字节，`39-0x12-4 = 0x11` = push 的 imm32 位置；
`@BBupr` backoff=154，桩体 194+4=198，`198-158 = 0x28`，与 `0x027 68 …` 的 imm32 对齐。）

### 3. 派发本体

每个桩体的尾巴都是同一段（以 `召唤神兽触发` 的完整 35 字节为例）：

```
0x000  60                     pushal
0x001  9C                     pushfd
0x002  8B D3                  mov edx, ebx              ; This_Player
0x004  A1 20 5D 7D 00         mov eax, [0x7D5D20]
0x009  8B 00                  mov eax, [eax]            ; TTaskAdmin 实例
0x00B  8B F0                  mov esi, eax
0x00D  8B 7E 08               mov edi, [esi+8]          ; TTaskAdmin+8 = TSTDScript
0x010  68 00 01 17 03         push <'@SummonShinsu'>    ; 装载时回填
0x015  6A 00                  push 0
0x017  33 C9                  xor ecx, ecx              ; This_Item = nil
0x019  8B C7                  mov eax, edi
0x01B  8B 18                  mov ebx, [eax]
0x01D  FF 53 44               call [ebx+0x44]
0x020  9D                     popfd
0x021  61                     popal
0x022  E9 <rel32>             jmp 0x6EDC63
```

这与宿主自己发 `@PlayerActiveValidate` 的 `sub_69B22C` **逐字节同形**：

```
0x0069B231  8B 70 08         mov esi,[eax+8]
0x0069B238  68 54 B2 69 00   push 0x69B254        ; Delphi 长串 '@PlayerActiveValidate'
0x0069B23D  6A 00            push 0
0x0069B23F  33 C9            xor ecx,ecx
0x0069B241  8B C6            mov eax,esi
0x0069B243  8B 18            mov ebx,[eax]
0x0069B245  FF 53 44         call [ebx+0x44]
```

**对象身份是可证的，不是猜的**：

- `[0x7D5D20] = 0x7DC4A4`（全镜像唯一引用），`0x7DC4A4` 是全局变量本体。
  `0x792A08 mov eax,[0x69861C]`（classref → VMT `0x698668` = `TTaskAdmin`，size 0x30）
  → `0x792A0D call 0x69870C`（ctor）→ `0x792A12 mov edx,[0x7D5D20] / 0x792A18 mov [edx],eax`。
- `TTaskAdmin+8` 由 `0x69A7EF mov eax,[0x728640]`（classref → VMT `0x72868C` = `TSTDScript`，
  父类 `TPSScript`，size 0xA8）→ `0x69A7F4 call 0x7295B0` → `0x69A7FE mov [eax+8],ebx` 定案。

### 4. 两个派发槽的封送约定

**TSTDScript VMT+0x44 = `sub_733D84`（无参）**

```
0x733DB6  mov ecx,[[0x7D5C40]]            0x733DBE mov edx,0x733FCC  'This_DB'
0x733DCB  mov ecx,esi (= 入口 ecx)        0x733DCD mov edx,0x733FDC  'This_Item'
0x733DDA  mov ecx,[ebp-8] (= 入口 edx)    0x733DDD mov edx,0x733FF0  'This_Player'
0x733DED  cmp 标签,'@Main'  / je 0x733F6F      ← 自递归门
0x733E01  cmp 标签,'@_Main' / je 0x733F6F      ← 自递归门
0x733E1F  cmp byte [edi],0x40                  ← 首字符必须是 '@'
0x733E26  Pos('~', 标签)                       ← '~' 之后是参数
```

调用约定：`eax`=Self、`edx`=This_Player、`ecx`=This_Item、栈上先压标签（`[ebp+0x0C]`）
再压一个 0（`[ebp+0x08]`）。

**TSTDScript VMT+0x48 = `sub_733B98`（带 `array of Variant`）**

```
[ebp+0x14] = 标签（同样 0x733C3F cmp byte [esi],0x40）
[ebp+0x10] = 数组指针
[ebp+0x0C] = High(数组)
[ebp+0x08] = @Result（Variant 返回值，0x733BF6 call 0x41F660 = VarClear）
0x733BB2  shl esi,2 / add esi,3   ← 元素宽度 16 字节 = TVarData
0x733BE1  call 0x4062C4           ← 开放数组转动态数组的 RTL 助手（edx=[0x401104] TypeInfo）
```

元素由三个 RTL 助手写入：`0x41AFE4`（整数 → Variant，默认 `mov word[edi],3` = varInteger、
`mov [edi+8],esi` = 值）、`0x405774`（ShortString → AnsiString）、`0x41B238`（字符串 → Variant）。
这就是转储文件名里 `strparam` 抓的那套字符串封送。

### 5. 回调返回值能不能取消原动作

**返回值一律被丢弃**（桩体在 `call` 之后立刻 `popfd/popal`）。能不能取消，取决于
**桩体有没有重放被覆盖的那几个字节**：

- 重放 → 纯通知，宿主行为一字未改（绝大多数）。
- 不重放 → 原生动作被顶掉。全族只有三条：`召唤神兽触发`（顶掉 `0x6EDC5E call 0x76EE7C`）、
  `召唤骷髅触发`（顶掉 `0x6EDB44 call 0x76EDFC`）、`心灵启示触发`（`0x6EDC2B`）。
  这三条落在魔法分发臂上，续跑点直接进 DEFAULT 汇聚 `0x6EE04B`（§4.12：静默成功）。

`0x76EE7C` / `0x76EDFC` 全镜像**各只有一个调用者**（分别是 `0x6EDC5E` / `0x6EDB44`），
所以「拦调用点」与「拦生产函数入口」等价 —— C# 侧因此可以把门放在
`MagMakeSinSuSlave` / `MagMakeSlave` 的第一行。

---

## 二、全族清单（21 个开关 / 25 个 builder 站点 / 25 个标签注册点）

（101 个 trampoline 站点里属于本族的是 25 个；`英雄穿戴` / `新穿戴` / `盘古穿戴` /
`盘古魔法攻击` 各占两个站点，所以站点数 25、开关数 21。）

| 配置键 | 脚本标签 | 安装器 | 宿主挂载 → 续跑 | 槽 | 参数 | 宿主动作 | 生产值 |
|---|---|---|---|---|---|---|---|
| 召唤神兽触发 | `@SummonShinsu` | `0x10032FD0` | `0x6EDC5E → 0x6EDC63` | 0x44 | 0 | **顶掉** | 0 |
| 召唤骷髅触发 | `@SummonSkele` | `0x10032FD0` | `0x6EDB44 → 0x6EDB49` | 0x44 | 0 | **顶掉** | 0 |
| BB杀怪触发 | `@BBupr` | `0x10032CC0` | `0x71F467 → 0x71F46C` | 0x48 | 2 | 通知 | 0 |
| BB死亡触发 | `@BBKill` | `0x10032CC0` | `0x76631C → 0x766321` | 0x48 | 1 | 通知 | 0 |
| 英雄穿戴触发 | `@HeroEquiepchange` | `0x10032CC0` | `0x75F08C → 0x75F093`、`0x75EA31 → 0x75EA37` | 0x48 | 6 | 通知 | 0 |
| 新穿戴触发 | `@MyEquiepchange` | `0x10032CC0` | `0x75F085 → 0x75F08C`、`0x75EA37 → 0x75EA3C` | 0x48 | 6 | 通知 | 0 |
| 上线触发 | `@initys` | `0x10032CC0` | `0x6548BD → 0x6548C2` | 0x44 | 0 | 通知 | 0 |
| 死亡触发 | `@OnDie` | `0x10032FD0` | `0x6C09B5 → 0x6C09BA` | 0x44 | 0 | 通知 | **1** |
| 回城按钮触发 | `@OnBackButton` | `0x10032FD0` | `0x6DBB80 → 0x6DBB85` | 0x44 | 0 | 通知 | 0 |
| 挖矿触发 | `@OnDig` | `0x10032FD0` | `0x6EC111 → 0x6EC116` | 0x44 | 0 | 通知 | **1** |
| 心灵启示触发 | `@Revelation` | `0x10032FD0` | `0x6EDC2B → 0x6EDC30` | 0x48 | 2 | **顶掉** | 0 |
| 复活触发脚本 | `@OnDia` | `0x10032CC0` | `0x73C484 → 0x73C48A` | 0x44 | 0 | 通知 | 0 |
| 被击杀触发 | `@MyKill` | `0x10032CC0` | `0x766624 → 0x766629` | 0x48 | 2 | 通知 | 0 |
| 捡物触发 | `@pickpre` | `0x10032CC0` | `0x6B770C → 0x6B7711` | 0x48 | 2 | 通知 | 0 |
| 攻击触发 | `@MyAttack` | `0x10032CC0` | `0x76E35D → 0x76E362` | 0x48 | 4 | 通知 | 0 |
| 魔法攻击触发 | `@MyMagicAttack` | `0x10032CC0` | `0x76DE84 → 0x76DE8A` | 0x48 | 5 | 通知 | 0 |
| 盘古穿戴触发 | `@ChangeEquip` | `0x10032FD0` | `0x6D8E35 → 0x6D8E3A`、`0x6D8E4D → 0x6D8E52` | 0x44 | 0 | 通知 | **1** |
| 盘古魔法攻击触发 | `@MagicAttack` | `0x10032FD0` | `0x76E1AF → 0x76E1B6`、`0x76DEC0 → 0x76DEC7` | 0x48 | 3 | 通知 | 0 |
| 刀刀切割 | `@Cutting` | `0x10032CC0` | `0x767BAE → 0x767BB4` | 0x48 | 0 | 通知 | 0 |
| 新倍攻和暴击 | `@baoji` | `0x10032CC0` | `0x76C88B → 0x76C890` | 0x44 | 0 | 通知 | **1** |
| 英雄倍攻和暴击 | `@Herobaoji` | `0x10032CC0` | `0x76C816 → 0x76C81D` | 0x44 | 0 | 通知 | 0 |

配置键全部在生产 `config.json` 里实测存在（380 键；`触发` 子串共 31 键）。

> **`@initys` 解开了 §5.2 的一条悬案。** 该节写「`initys` 在生产树 3326 个文件里 0 命中，
> 所以回收系统整体静默失效」。`initys` 既不是文件名也不是 API 名，是**上线触发**
> 发出的脚本标签 `@initys`（`0x100D5968` 建桩、`0x100D5A01` 回填，宿主 `0x6548BD`，
> 门 `[ebp+4]==0x6542D2`）。生产值是 0，所以这条链在该部署上确实没通。

---

## 三、C# 派发层

新增 `GameSvr/Plugins/YanshenTriggerDispatch.cs`：

- `Registry` —— 上表的全部原生事实（挂载 VA、续跑 VA、标签、槽、参数个数、顶掉/通知），
  纯静态数据，供审计与后续接线用。
- `Armed` —— 惰性门。插件缺席时只读 `M2Share.PluginManager` 与 `PluginInfo.State`
  两个字段就返回 false：不分配、不查配置、不碰脚本引擎。
- `DispatchPlain` → `M2Share.g_FunctionNPC.GotoLable(player, "@X", false)`
  —— 与宿主自身 `@PlayerActiveValidate` / `@GroupCreate` 走的**同一条**路，
  不新建第二套权威。
- `DispatchWithParams` → `NormNpc.TryCallPascalCallback`（`_名字 / 名字` 解析，
  与 `@` 标签解析一致），承载 0x48 槽的 `array of Variant`。
- `DispatchCount` / `LastDispatchedLabel` —— 只读诊断计数器，产品逻辑不读，
  存在的意义是让审计能把「插件关闭时零派发」变成可断言事实。

已接通四个触发点：

| 触发点 | C# 落点 | 参数 |
|---|---|---|
| 召唤神兽触发 | `MagicManager.MagMakeSinSuSlave` 首行，返回 true 则整段生产被跳过 | — |
| 召唤骷髅触发 | `MagicManager.MagMakeSlave` 首行，同上 | — |
| BB杀怪触发 | `TBaseObject.GainSlaveExp` 末尾（原生挂在收尾 `0x71F467`） | `m_btSlaveExpLevel`、宠物在主人 `m_SlaveList` 里的序号+1 |
| BB死亡触发 | `TBaseObject.Die` 首行（原生改写序言 `0x76631C`） | 死者 `m_sCharName` |

BB杀怪的序号搜索照抄了 `0x71F058..0x71F07B` 的两个边角：`m_SlaveList` 为空时
（原生 `ecx` 减到 -1，`jl` 生效）整条事件跳过；自顶向下扫到 0 仍未命中时
（循环靠 `jg` 落空退出，`ecx` 停在 0）**照发**，序号按 0 算。

### 惰性怎么证

`AuditTools/YanshenTriggerDispatchCheck` 三段：

1. `M2Share.PluginManager = null` → `Armed == false`；四个 Fire 全跑一遍后
   `DispatchCount` 一动不动；两个召唤门返回 false（= 原生造宠照常执行）。
2. 插件 Running 但开关全 0 → `DispatchCount` 仍不动。
3. 开关全 1 → 恰好 4 次派发，标签依次为 `@SummonShinsu` / `@SummonSkele` /
   `@BBupr` / `@BBKill`；两个召唤门返回 true。
   随后玩家自己死、无主怪死、无主怪打怪各一次 → 计数器不动（四道原生门）。

外加注册表逐字段比对，以及「覆盖长度 ∈ [5,16]」「挂载点落在宿主代码区」两条结构校验。

---

## 四、BLOCKED

### 英雄穿戴触发 / 新穿戴触发的第 2、3、4 个 Variant

两个桩体逐字节同形，只差两处：`0x75EA31` 那支要求
`[ebp+4]==0x75EF64` 且 `[[ebp+0x10]] != 0x6AC8C8`（对象不是 `TPlayer` ⇒ 英雄），
This_Player 取 `[hero+0x68C]`；`新穿戴` 那支要求 `== 0x6AC8C8`，This_Player 直接用 `esi`。

六个 Variant 的来源都读得出来：

| # | 来源 | 状态 |
|---|---|---|
| 1 | `pushal` 保存的 EDX（`[ebp-0x1C]`）= 装备位置 | 已定 |
| 2 | dword `[item+0x28]` | **BLOCKED** |
| 3 | dword `[item+0x2C]` | **BLOCKED** |
| 4 | dword `[[item+0x1C]+0x14]` | **BLOCKED** |
| 5 | `[item+0x1C]+4` 处 ShortString（`0x405774` → `0x41B238`） | 已定 = StdItem 名 |
| 6 | `VType=2`（varSmallint）、值 0 | 已定 |

`item` 是 `TEquipItem`（VMT `0x75CAC8`，父 `TBaseItem`，size 0x108，子类 `TClothes` /
`THelmet` / `TBoots` / `TBelt` / `TCharm` …），容器是 `TEquipContainer`（VMT `0x75CBC0`），
装备槽在 `container+8+slot*4`。`0x75F714 lea esi,[eax+0x20] / mov ecx,0x34 / rep movsd`
证明 `item+0x20` 起是那份 **208 字节定长记录**，所以 `+0x28` / `+0x2C` 落在 blob 偏移
`0x08` / `0x0C`，也就是**跨字段的整 dword**（`DuraMax` 与 `btValue[0..5]` 混在一起）；
`+0x14` 落在 `TStdItem` 内部。把这三个 dword 拆成具名字段没有逐字节证据，凑数就是臆造，
**所以这两条只在注册表里留档，运行期不发射**。

补齐需要：`TStdItem` 的原生逐字节布局（`+2` 是 word、`+4` 起是 ShortString、
`+0x15` 是 StdMode 类字节、`+0x60` 起是 word 数组，这四点已知，中间没连起来）。

### 英雄野蛮

按任务边界，两个宿主函数指针在 Themida 虚拟化前半段赋值、落在全零区，未投入人力。

---

## 五、边界问题：`修改召唤神兽` 走的是哪套机制

**结论：它是配置驱动的运行期取值，不是补丁站点。零命中是正确结果，不是漏扫。**

`修改召唤神兽` 的 GBK 串在 `0x102B1658`，交叉引用两处：`0x1000A4D6`（GUI 侧）与
`0x100BAC04`。后者位于一长串**配置键 → 单例字段**的装载序列里，形状完全一致：

```
0x100BABFE  68 64 16 2B 10        push 0x102B1664          ; 默认值串
0x100BAC03  68 58 16 2B 10        push 0x102B1658          ; "修改召唤神兽"
0x100BAC08  8D 4D D8              lea ecx,[ebp-0x28]
0x100BAC0B  89 86 60 06 00 00     mov [esi+0x660], eax     ; ← 上一个键的落点
0x100BAC11  E8 CA 5F 02 00        call 0x100E0BE0          ; json 取值
```

同一段序列里的邻居可以互相印证：`装备吸血`→`+0x438`、`获取玩家对象函数`→`+0x4C0`、
`脚本控制人物爆率`→`+0x798`、`人物爆率调整`→`+0x548`、`装备提升人物爆率`→`+0x5D0`、
`无极真气_A值`→`+0x710`。

顺带订正一条容易搞混的：**常量改写用的键叫 `召唤神兽` / `召唤骷髅`，不叫 `修改召唤神兽`**。
那两条在 `0x100A9E60..0x100AA0F6`，用 `call 0x10033340` 改写宿主常量
（`push 0x76EE99` ×2 = 数量 imm8；名字串 `0x76EEEC`），日志串 `0x102C4A48`「召唤神兽(已启动)」
在 `0x100A9EE6` 引用。C# `MagicManager` 里既有的
`IsSummonShenShou()` / `ShenShouName()` / `ShenShouSlaveCount()` 注释与此吻合，不需要改。

所以「触发」和「修改」是**两套互不相干的机制打在同一个技能上**：
`召唤神兽`（常量改写，改名字与数量）vs `召唤神兽触发`（trampoline，整条顶掉交给脚本）。
生产配置里前者=0、后者=0，`修改召唤神兽`=1。
