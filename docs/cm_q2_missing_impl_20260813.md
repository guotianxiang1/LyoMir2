# CM 缺失处理器 · 第 2 片（升序第 26..50 个）· ident 1265..3179

日期：2026-08-13
镜像：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`（文件偏移 = VA − 0x400000）
工作树 / 分支：`D:\loym2\.claude\wt2\cm-2` / `w/cm-2`
capstone 解释器：`C:\Users\Administrator\AppData\Local\hermes\hermes-agent\venv\Scripts\python.exe`（Python 3.11.15，capstone 5.0.7）
权威工具：`tools/cm2_table.py`（native `build()`：分发树逐 opcode 求解 leaf VA）、`tools/cm2_csharp_cover.py`（C# `covered()`）、`tools/cm2_missing.py`（缺失集合 + 四等分）、`tools/cm2_arm.py`（单臂反汇编）、`tools/cm2_triage.py`（被调函数分诊）、`tools/cm2_q2_probe.py` / `cm2_q2_dump.py`（本片专用批量取证）
**全程未执行 `dotnet build`，未运行任何审计工具。**

---

## 0. 缺失集合与边界复现

`cm2_missing.py` 的判定：native 分发臂（`sub_6D7D68`，选择子根 `0x6D805C`，读 `word[record+4]`=Ident，共享出口 `0x6DBC2C`）逐 opcode 求解，去掉落到共享出口的臂，得 **311** 个真实臂；再减去 C# 已 `case` 覆盖的集合，得 **缺失 99 个**。

四等分用 `bounds = [round(99*i/4) for i in range(5)] = [0, 25, 50, 74, 99]`，故：

| 片 | 切片 | 数量 | 区间 | 归属 |
|---|---|---|---|---|
| Q1 | `missing[0:25]`  | 25 | 1054..1260 | cm-1 |
| **Q2** | **`missing[25:50]`** | **25** | **1265..3179** | **本片（cm-2）** |
| Q3 | `missing[50:74]` | 24 | 3180..4124 | cm-3 |
| Q4 | `missing[74:99]` | 25 | 4125..4651 | cm-4（已完成） |

第 2 片 = 升序第 26..50 个。复现命令：`python tools/cm2_missing.py`（其 `q==1` 段即本片，逐行打印 `ident  leafVA`）。

---

## 1. ProcessMsg 字段映射（`sub_6D7D68` 帧，实测）

线格式头 12 字节（`TDefaultMessage`），分发器把 msg 记录指针放在 `[ebp-0x34]`，self 放在 `[ebp-4]`，body 串指针放在 `[ebp-8]`，BodyLen 放在 ESI/EDI：

| 线偏移 | native 字段 | C# `TProcessMessage` |
|---|---|---|
| `[msg+0]` dword | Recog  | `nParam1` |
| `[msg+4]` word  | Ident  | `wIdent` |
| `[msg+6]` word  | Param  | `nParam2` |
| `[msg+8]` word  | Tag    | `nParam3` |
| `[msg+0xA]` word | Series | `wParam` |
| body 串 | — | `sMsg` |
| 总长 − 0x0C | BodyLen | `nBodyLen` |

`[self+0xBB0]` = 英雄对象（C# `m_HeroObject`）。`0x408D40` = `MakeLong(ax=low, dx=high)` → `ax|(dx<<16)`；`0x408EC4` 同义（先落栈再转发）；`0x408D68` = `HiWord`。
回包发送腿：`[vmt+0x250]`（`sub_6D7CB0`，`ret 0x10`）、`[vmt+0x254]`（`sub_6D7BF8`，`ret 0x14`，带 body 指针+长度）、`[vmt+0x24C]`（对英雄发）、`[vmt+0xD4]`（系统文本提示 SysMsg，携带 GBK 字符串）。

---

## 2. 权威清单（25 项，全部 fail-closed）

判定复核逐个反汇编被调函数体，不盲信旧产物。结论：**25 项无一可在不移植子系统的前提下逐字节等价**——回包 body 取自未建模的玩家字段 / 被调子系统全局单例，忠实实现需先移植子系统；强行拼包会捏造无法验证的线格式，违反铁律。**忠实 builder：0 个；原生 no-op：0 个；fail-closed：25 个**（其中 6 项含镜像可求值的静默前置门，已 1:1 复现为提前静默返回）。

| CM ident | leaf VA | worker VA | 子系统 | 处置 |
|---|---|---|---|---|
| 1265 | 0x6DA710 | 0x6E8564 | 元宝交易设置（挂单集合 `[self+0x192C]`） | fail-closed |
| 1280 | 0x6DA8F3 | 0x6E9208 | 自身对象回显（body=`[self+0x554]` 0x1C，门=Recog==self 指针） | fail-closed |
| 1291 | 0x6DA3CA | 0x69059C | 英雄灵珠（开启/取经验，物品类 `[0x780A74]`） | fail-closed（门：无英雄→静默） |
| 1300 | 0x6DAA17 | 0x63D980 | 分身/机器人点击 NPC（`[self+0xCD8]`） | fail-closed |
| 1301 | 0x6DAA72 | 0x63DC98 | 分身/机器人执行 NPC 脚本过程（`[self+0xCD8]`） | fail-closed |
| 1316 | 0x6DAACF | 0x746908 | 英雄生肖/神佑袋镶嵌（`[self+0x60C]` 位掩码） | fail-closed（门：Series==1&&有英雄→否则静默） |
| 1320 | 0x6DAB6A | 0x765E68 | 分身会话请求入队（`[self+0xCD8]`） | fail-closed |
| 1350 | 0x6DAC8E | 0x6F09C4 | 元宝寄售·写（管理器 `[0x7D5D98]`，req SM 0x136/ack 0x4E2） | fail-closed（门：BodyLen>=0x20） |
| 1351 | 0x6DACA7 | 0x6F0A98 | 元宝寄售·写（req 0x137/ack 0x4E3） | fail-closed |
| 1352 | 0x6DACD0 | 0x6F0B84 | 元宝寄售·上架（req 0x138/ack 0x4E4，含物品模板 body） | fail-closed |
| 1353 | 0x6DACE4 | 0x6F0E0C | 元宝寄售·写（req 0x139/ack 0x4ED） | fail-closed |
| 1354 | 0x6DACF6 | 0x6F0E64 | 元宝寄售·写（req 0x13A/ack 0x4E7） | fail-closed |
| 1355 | 0x6DAD08 | 0x6F0EBC | 元宝寄售·写（req 0x13B/ack 0x4E5） | fail-closed（门：BodyLen>=0x0C） |
| 1356 | 0x6DAD21 | 0x6F0F28 | 元宝寄售·写（req 0x13C/ack 0x4E6） | fail-closed |
| 1357 | 0x6DAD33 | 0x6F0F80 | 元宝寄售·写（req 0x13D/ack 0x4EE） | fail-closed |
| 1358 | 0x6DAD45 | 0x6F0FD8 | 元宝寄售·写（req 0x13E/ack 0x4E8） | fail-closed |
| 1359 | 0x6DAD57 | 0x6F1028 | 元宝寄售·取回（cl=1，req 0x13F/ack 0x4E9，提示腿 0x38FF/0xFFDB） | fail-closed |
| 1360 | 0x6DAD6B | 0x6F1028 | 元宝寄售·取回（cl=0，req 0x140/ack 0x4E9，提示腿同上） | fail-closed |
| 1361 | 0x6DAD7F | 0x6F110C | 元宝寄售·写（req 0x141/ack 0x4EB） | fail-closed |
| 1362 | 0x6DAD91 | 0x6F1164 | 元宝寄售·写（req 0x142/ack 0x4EC） | fail-closed |
| 1363 | 0x6DADA3 | 0x6F11BC | 元宝寄售·写（req 0x143/ack 0x4EF） | fail-closed |
| 1364 | 0x6DADB5 | 0x6F120C | 元宝寄售·写（req 0x146，无 ack） | fail-closed（门：Param>=5&&Tag>0x1E） |
| 1376 | 0x6DAFF3 | 0x6F2E44 | 坐骑马牌（当前物品类 `[0x75DC48]`，SM 0x50A/提示"请放入马牌"） | fail-closed |
| 2815 | 0x6D9B52 | 0x6D4E4C | 消息板/relay（管理器 `[0x7D60FC]`，SM 0xAFF） | fail-closed（门：BodyLen<=0x40） |
| 3179 | 0x6DA3F3 | 0x6E320C | 商人/物品字节查询（`[item+0x1C]/+0x44/+0x37`，SM 0x6BE） | fail-closed |

---

## 3. 逐项三件套证据（字节 + 反汇编 + 语义 + 精确缺口）

### 3.1 元宝寄售·写族（1350..1364，worker `0x6F0xxx`）

**共同锚点：**
- 忙碌门 `0x6F0A24`：`cmp byte[self+0x18C8],0` / `mov edx,[0x7D7038];test byte[edx+3],0x80` / `mov eax,[self+0x128];cmp byte[eax+0x82],0`——挂单 pending 标志、全局配置、地图标志三者皆未建模。多数 worker 首先调用它，忙则整臂静默。
- 请求转发器 `0x6D3694`：把 body 前缀 4 段玩家字段 `{[self+0xAF4]/10, [self+0xB09]/20, [self+0x106]/15=角色名, [self+0xB33]/15}` 后接调用方 body，经单例 `[0x7D5D98]`（`0x637A00`）派发；SM id 由 esi（`dx`）带入。玩家字段与该单例均未建模。

> 说明：C# 已建模元宝寄售**读**侧（CM 1252/1253/1256/1257，见 `TPlayObject.NativeYbConsignment.cs`，走本地 `NativeYbConsignmentQuery` 存储）；本族是**写**侧，走上述跨进程管理器 `[0x7D5D98]`，与读侧不同路，未建模。

| ident | worker | 关键字节 / 语义 | req SM | ack SM | 缺口 |
|---|---|---|---|---|---|
| 1350 | 0x6F09C4 | leaf `83 FF 20 / 0F 82.. jb` BodyLen>=0x20；worker 忙门后 `[body+0x18]>0`、`[body+0x1C]-1∈{0,1,2}` | 0x136 | 0x4E2(Recog=-1) | body 结构 + 管理器 |
| 1351 | 0x6F0A98 | leaf MakeLong(Tag,Series)→edx，Recog→ecx，push 1；worker 忙门后 `Series%0x74`、坐标门 `[+0x18A0]/[+0x18A4]` | 0x137 | 0x4E3 | 坐标/寄售格 + 管理器 |
| 1352 | 0x6F0B84 | 忙门→`0x73CF08(self,Recog)`取背包物品→StdItem表`[0x7D5D6C]`→构 0x10A body（物品模板+角色名+`[self+0x278]`+`[item+0x20]` 0xD0 字节）→req | 0x138 | 0x4E4 | 物品模板 body + 管理器 |
| 1353 | 0x6F0E0C | 忙门→`Recog>0`→req | 0x139 | 0x4ED | 管理器 |
| 1354 | 0x6F0E64 | 同 1353 | 0x13A | 0x4E7 | 管理器 |
| 1355 | 0x6F0EBC | leaf BodyLen>=0x0C；worker 忙门→`[body+4]>0`、`[body]>0`→req；ack 带 `HiWord([body])`、`word[body]` | 0x13B | 0x4E5 | body 结构 + 管理器 |
| 1356 | 0x6F0F28 | 同 1353 | 0x13C | 0x4E6 | 管理器 |
| 1357 | 0x6F0F80 | 同 1353 | 0x13D | 0x4EE | 管理器 |
| 1358 | 0x6F0FD8 | 同 1353（ack Recog=Recog） | 0x13E | 0x4E8 | 管理器 |
| 1359 | 0x6F1028 cl=1 | `0x76858C`安全区门→假则提示 SM 0x38FF"在非安全区不能取回物品"；`0x7441D8`背包空位(`0x30-[[self+0x508]+8]`)<=0→提示 SM 0xFFDB"你的背包位置不足"；否则 req | 0x13F | 0x4E9 | 管理器（提示腿依赖安全区判定 + SysMsg 发送腿） |
| 1360 | 0x6F1028 cl=0 | 同 1359，cl=0 走 SM 0x140 | 0x140 | 0x4E9 | 同上 |
| 1361 | 0x6F110C | 同 1353 | 0x141 | 0x4EB | 管理器 |
| 1362 | 0x6F1164 | 同 1353 | 0x142 | 0x4EC | 管理器 |
| 1363 | 0x6F11BC | 同 1353（ack push 5） | 0x143 | 0x4EF | 管理器 |
| 1364 | 0x6F120C | 无忙门；leaf push Tag，cx=Param，edx=Recog；worker `Param>=5 && Tag>0x1E`→MakeLong(Param,Tag)→req，无 ack | 0x146 | — | 管理器 |

**可求值静默前置门（已 1:1 复现）**：1350 `nBodyLen<0x20→静默`；1355 `nBodyLen<0x0C→静默`；1364 `nParam2<5 || nParam3<=0x1E→静默`。其余成员的首门是未建模忙门 `0x6F0A24`，无法求值，直接 fail-closed。1359/1360 的两条提示腿（安全区 / 背包空位 → SysMsg 文本 `[vmt+0xD4]`）带 GBK 串、且依赖安全区判定 `0x76858C`（含地图标志 + 管理器 `[0x7D660C]`），非"裸回包腿"，登记待移植。

### 3.2 独立项

- **1265 / 0x6E8564**：`esi=[self+0x192C]`（挂单集合）为空则仅服务端日志 `[Error]: 交易设置信息读取失败`、**不回包**；非空则 `0x712BC4(集合,Param)` 查得后 SysMsg"元宝交易设置"并 `esi=0`，未查得 `esi=-1`，末尾 `[vmt+0x250]` 发 SM 0xBC7(Recog=esi，空 body)。挂单集合 `[self+0x192C]` 未建模（等价于空）→原生对客户端静默。缺口：挂单集合子对象。
- **1280 / 0x6E9208**：worker 仅 `cmp edx,eax`（Recog==self 指针）返回 bool；leaf 命中则 `[vmt+0x254]` 发 SM 0xCDB，body=`[self+0x554]` 0x1C 字节。门=客户端 Recog 等于服务端对象指针（C# 无同表示指针身份），body 取自未建模 `[self+0x554..0x56F]`。缺口：指针身份门 + 该 0x1C 字段块。
- **1291 / 0x69059C**：leaf `[self+0xBB0]`（英雄）为空→静默（已复现）；worker `0x69079C(hero)`假→SysMsg"您的英雄已经无法再获得提升"；`0x73CF08(hero,Recog)`取英雄背包物品，类 `[0x780A74]` 校验，消耗荣耀点`[hero+0x68C]`开启"白日门灵珠"或发 SM 0xA 取经验。缺口：英雄灵珠物品链 + 荣耀点字段。
- **1300 / 0x63D980**：`[self+0xCD8]` 为空→静默；非空则对 `[self+0x570]` vmt+0x48 派发（点击 NPC 函数），SysMsg"点击NPC成功/失败 NPC=.. 函数=.."。`[self+0xCD8]`=分身/机器人会话，未建模。缺口：分身会话对象。
- **1301 / 0x63DC98**：同 1300，走 `[self+0x570]` vmt+0x44（执行 NPC 过程+参数），失败发 SM 0x38FF"[ExecScript Fail]"。缺口：分身会话对象 + 脚本引擎回执。
- **1316 / 0x746908**：leaf `Series==1 && [self+0xBB0]`（已复现）；worker `0x73CF08(hero,Recog)`取物品，类 `[0x7825C8]` 校验，按 `[item+0x1C]+0x15`（生肖 shape）置位 `[self+0x60C]`，SysMsg"您在（极品）神佑袋中镶嵌了：/ 只能镶嵌对应属相 / 找不到镶嵌的饰物"，末 `0x747CF4`+`0x74730C` 重算后 `[vmt+0x250]/+0x24C]` 发 SM 0xCFD（body=`[self+0x60C]`掩码 + `word[self+0x610]`）。缺口：神佑袋掩码/生肖字段 + 镶嵌链。
- **1320 / 0x765E68**：leaf `[self+0xCD8]!=0` && 同图(`[+0x128]==[+0x128]`) && `0x7743E0(self,obj,0xF)` && `Param∈{1,2,3}`；worker 构 0x28 字节记录（Series/参数）入队 `[obj]vmt+0x18` 或 `0x76C11C`，SM 0x27A3。缺口：分身会话对象 + 记录格式。
- **1376 / 0x6F2E44**：`0x73CF08(self,Param)`取背包物品，类 `[0x75DC48]`（马牌）校验，`0x7632E4/0x7632E0` 处理，成功 `[vmt+0x250]` 发 SM 0x50A(Recog=`[item+0x18]`)，否则 SysMsg 0x38FF"请放入马牌"。缺口：坐骑马牌物品类 + `0x7632E0/E4` 语义。
- **2815 / 0x6D4E4C**：leaf `movzx ecx,si`(BodyLen)；worker `BodyLen<=0x40` 门（`dec ecx;sub ecx,0x40;jae` 出口，已复现 `>0x40→静默`）；解析 body（`0x405708`），取 `word[self+0x9E4]/+0x9E6`（坐标）、`[self+0xB33]/+0xB09`（串），经单例 `[0x7D60FC]`（`0x6A4144`）后 `[vmt+0x250]` 发 SM 0xAFF。缺口：单例 `[0x7D60FC]` + 玩家字段。
- **3179 / 0x6E320C**：`0x73CF08(self,Recog)`取背包物品；取 `[item+0x1C]→+0x44→+0x14` 字节数组，索引 `byte[item+0x37]`，越界或缺失则 `edi=-1`；末 `[vmt+0x250]` 发 SM 0x6BE(Recog=edi)。查得物品时返回真实字节，未建模其扩展子对象 `[item+0x1C]` 链，无法求值成功值；返回 -1 会捏造返回码 → fail-closed。缺口：物品扩展子对象链。

---

## 4. 集成挂钩说明

处理器分发方法 `TryHandleNativeCmQ2(TProcessMessage)` 位于 `GameSvr/Players/TPlayObject.NativeCmProtocol_Q2.cs`（`partial class TPlayObject`），返回 `true` 表示本片已消费该 ident。

**本子代理不编辑 `Operate()` 分发 switch。** 集成方按既有 native 分片惯例（如 master 上 `TryHandleNativeCmTailProtocol` 由集成方另行挂）在 `TPlayObject.Message.cs::Operate` 的 `default:` 腿或 social 链末尾追加一条短路调用即可：

```csharp
default:
    if (TryHandleNativeCmQ2(ProcessMsg)) break;      // ← 集成方追加这一行
    if (!TryHandleNativeSocialProtocol(ProcessMsg))
    {
        result = base.Operate(ProcessMsg);
    }
    break;
```

常量追加在 `SystemModule/Grobal2.cs` 末尾，批次标记 `// === CM missing Q2 ... ===`；fail-closed 登记表 `GameSvr/Services/NativeCmQ2FailClosed.cs`（方法 `Q2Drop`）。
