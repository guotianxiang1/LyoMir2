# 跨服 MirrorMessage / ProcessOthGsMsg 逐条复核 (mirror2, 20260814)

底本 `staging\_reunpack_work\flat_image.bin`，ImageBase `0x400000`，
`file_off = VA - 0x400000`。工具见 `tools/mirror2_*.py`（本次新增，可复跑）。
分支 `w/mirror2`，基线 master `69f049b6`。

---

## 1. 派发器结构（订正任务书）

任务书说「跳表在 `0x657110`」。**`0x657110` 不是跳表，是函数体首字节。**
`0x6570D8..0x65710F` 是 Delphi 类 `TOtherGSMsg` 的 VMT 尾 + 类名短串：

```
006570D8  04 71 65 00   vmtClassName -> 0x657104 = "\x0BTOtherGSMsg"
006570DC  04 00 00 00   vmtInstanceSize = 4
006570E0  24 11 40 00   vmtParent
006570E4..00657100      SafeCallException/AfterConstruction/BeforeDestruction/
                        Dispatch/DefaultHandler/NewInstance/FreeInstance/Destroy
                        = 0x4048EC/0x4048F8/0x4048FC/0x404900/0x4048F4/
                          0x404628/0x404644/0x404680  (TObject 缺省实现)
00657104  0B 54 4F ...  类名短串（该类无自有虚方法，故串紧贴 VMT 指针位）
```

真正的两级派发表在函数体内：

```
00657140  0fb755fe        movzx edx, word [ebp-2]      ; ident
00657144  81c236ffffff    add   edx, 0xFFFFFF36        ; edx = ident - 202
0065714A  83fa37          cmp   edx, 0x37              ; 55
0065714D  0f874d020000    ja    0x6573A0               ; default error sink
00657153  8a9260716500    mov   dl, byte [edx+0x657160]  ; 索引表, 56 字节
00657159  ff249598716500  jmp   dword [edx*4+0x657198]   ; 地址表, 28 dword
```

- **索引基数 = 202**，跨度 **202..257**（56 个），与任务书一致。
- 索引表 `0x657160..0x657197`（56 B），地址表 `0x657198..0x657207`（28 项）。
- 索引 0 = `0x6573A0` = 缺省错误臂；索引 1..0x1B = 27 个真 handler。
- **27 REAL / 29 SINK**，与 `Grobal2.cs` 既有注释逐条吻合（该常量层判定 FAITHFUL）。

索引表原始字节：

```
01 02 00 00 00 03 00 04 05 06 07 08 09 00 0a 0b
0c 0d 0e 0f 10 00 11 00 12 13 14 00 00 00 00 00
00 00 00 00 00 00 15 16 00 17 00 00 00 18 00 19
00 1a 00 00 00 00 00 1b
```

### 全表 dump（ident → stub → handler）

| ident | slot | stub | handler | stub 传参 |
|---|---|---|---|---|
| 202 | 1 | 0x657208 | sub_658384 | push[ebp+8]; ecx=[ebp+0x10]; edx=[ebp+0xC] |
| 203 | 2 | 0x65721C | sub_6582B0 | push[ebp+0x10]; ecx=[ebp+8]; edx=[ebp+0xC] |
| 207 | 3 | 0x657230 | sub_658114 | edx=[ebp+0x10] |
| 209 | 4 | 0x65723D | sub_6580B8 | ecx=[ebp+0x10]; edx=[ebp+0xC] |
| 210 | 5 | 0x65724D | sub_657FF8 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 211 | 6 | 0x65725D | sub_657810 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 212 | 7 | 0x65726D | sub_6577B0 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 213 | 8 | 0x65727D | sub_657F28 | (无参) |
| 214 | 9 | 0x657287 | sub_6579B0 | edx=[ebp+0x10] |
| 216 | 10 | 0x657294 | sub_6579D8 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 217 | 11 | 0x6572A4 | sub_657CF0 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 218 | 12 | 0x6572B4 | sub_657AC0 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 219 | 13 | 0x6572C4 | sub_6581A4 | push ecx; push[ebp+8]; cl=[ebp+0x10]; edx=[ebp+0xC] |
| 220 | 14 | 0x6572D9 | sub_657E08 | push[ebp+8]; edx=[ebp+0xC] |
| 221 | 15 | 0x6572EA | sub_6575D8 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 222 | 16 | 0x6572FA | sub_657700 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 224 | 17 | 0x65730A | sub_6574B4 | push[ebp+8]; ecx=[ebp+0x10]; edx=[ebp+0xC] |
| 226 | 18 | 0x65731E | sub_657888 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 227 | 19 | 0x65732E | sub_657670 | ecx=[ebp+8]; edx=[ebp+0xC] |
| 228 | 20 | 0x65733E | sub_657BCC | push[ebp+8]; ecx=[ebp+0x10]; edx=[ebp+0xC] |
| 240 | 21 | 0x65734F | sub_657F3C | ecx=[ebp+8]; edx=[ebp+0xC] |
| 241 | 22 | 0x65735C | sub_655A18 | eax=[[0x7D6D50]] (UserEngine) |
| 243 | 23 | 0x65736A | sub_655A74 | eax=[[0x7D6D50]] |
| 247 | 24 | 0x657378 | sub_65805C | ecx=[ebp+8]; edx=[ebp+0xC] |
| 249 | 25 | 0x657385 | sub_658094 | edx=[ebp+0x10] |
| 251 | 26 | 0x65738F | sub_658048 | (无参) |
| 257 | 27 | 0x657396 | (内联) | `mov eax,[0x7D6324]; mov byte[eax],1` |

SINK（落 `0x6573A0`）：204 205 206 208 215 223 225 229..239 242 244 245 246 248 250 252..256。

任务书给的 6 条线索 **全部复核通过**（202/207/217/218/241/247）。

---

## 2. 形参坐标（本次订正的关键事实）

派发器唯一调用点 `0x712F56`，其所在函数 `sub_712EC8` 的唯一调用点 `0x713EF0`。
上游 `0x713EC0..0x713EF0` 把帧拆成「净荷指针 + 净荷长度」：

```
00713EC0  cmp dword [ebx+4], 0xC / jl skip     ; 帧总长 >= 12
00713EC6  mov eax,[ebx+4] / sub eax,0xC        ; 净荷长度 = 总长 - 12
00713ED5  mov eax,[ebx]   / add eax,0xC        ; 净荷指针 = 帧基址 + 12
00713EE4  push [ebp-0xC]                       ; 长度  -> sub_712EC8 的 [ebp+8]
00713EE8  mov edx,[ebx]                        ; 帧基址
00713EEA  mov ecx,[ebp-8]                      ; 净荷指针
00713EF0  call 0x712EC8
```

`sub_712EC8` 再转发（`0x712F3F..0x712F56`，压栈次序 `[ebx+8]` → 净荷 → 长度）：

| 派发器槽位 | 内容 |
|---|---|
| `[ebp+8]` | **净荷长度** = `[ebx+4] - 0xC` |
| `[ebp+0xC]` | **净荷指针** = `帧基址 + 0xC` |
| `[ebp+0x10]` | **帧头第三个 dword** = `[ebx+8]` |
| `dx` | ident = `word[ebx+2]` |
| `ecx`（未被派发器读） | `[ebx+4]` = 帧总长 |

两条订正：

1. **native 派发器根本没有 serverIdx 形参。** C# `ProcessData(Ident, serverNum, Body)`
   的 `serverNum` 是本仓传输层自加的（`UsrEngn.SendServerGroupMsg` 拼
   `nCode+"/"+nServerIdx+"/"+sMsg`）。因此所有 `sNum == M2Share.nServerIndex`
   守卫都是 C# 扩展，不是 native 语义 —— 这一点 `MsgGetDivorce` 的既有注释已经
   说对了，此处给出结构性依据。
2. 我在本次工作中途一度把 `[ebp+8]` 误读为 serverIdx（因为多数 handler 只拿它做
   `>0` 判定），并据此给 221 写了一道 `serverIndex <= 0` 门。`cmp ecx,0x24`（222，
   0x24 恰是其记录长度）与 `cmp ebx,0xC`（247）证伪了该读法，已在 commit
   `4f6db7e1` 订正。**既有 in-tree 注释「[ebp+8]=len」是对的。**

**净荷是字节缓冲，但绝大多数 handler 按文本解读**：203/209/210/211/212/216/217/
218/219/220/221/224/226/227/228/240 全部走 `0x405708`（`_LStrFromPChar`，扫 NUL
定长）。只有 **222** 走 `0x405774`（`_LStrFromShortString`，读长度前缀）+ 定偏移
`word[+0x20]/[+0x22]`，以及 **247** 直接取三个 dword —— 这两个才是真二进制帧。
故 in-tree 关于 222/247「二进制不可表示」的 fail-closed 成立，而**不能**把同一
理由外推到别的 ident。

辅助函数身份（本次坐实）：

| VA | 身份 | 约定 |
|---|---|---|
| 0x405708 | `_LStrFromPChar` | eax=dest, edx=PChar；扫 NUL 求长 |
| 0x405774 | `_LStrFromShortString` | eax=dest, edx=ShortString（长度字节在前）|
| 0x40581C | `_LStrCat3` | dest=eax, **结果 = edx + ecx** |
| 0x405890 | `_LStrCatN` | eax=dest, edx=段数，段在栈上（先压者在左）|
| 0x4C6AEC | 单字符拆分 | eax=源, edx=@首段, cl=分隔符, 压栈=@余段 |
| 0x4C6BA4 | 字符集拆分 | eax=源, edx=@首段, ecx=@字符集, 压栈=@余段 |
| 0x652784 | `UserEngine.GetPlayObject` | eax=UserEngine, edx=名 |
| 0x6C614C | 徒弟槽查找 | eax=师父, edx=徒弟名, ecx=@idx；返回 al |
| 0x713890 → 0x7138CC | **空桩** | `55 8B EC 5D C2 0C 00`，编组后丢弃 |

`0x7138CC` 是空桩这一点已被 `NativeLeaveMaster.cs` 记录，本次独立复验通过。
它决定了 **219 的第二条腿与 220 的全部输出都不可观测**。

---

## 3. 逐条判定

| ident | native | Grobal2 名 | 真实语义 | C# 现状 | 判定 |
|---|---|---|---|---|---|
| 202 | sub_658384 | ANTICHEAT_PENALTY | 反作弊惩罚（名单串 + 时长=第三 dword）| 保留登出接收 | **BLOCKED** |
| 203 | sub_6582B0 | WHISPER | 私聊转发；`word[ebp+8]`=第三 dword 低字=发信人等级 | 有实现，Tag 恒 0 | **DIVERGENT(已记录)** |
| 207 | sub_658114 | SINGLEQUOTE_SCAN | 全局 `[0x7D7038]` 40-bit 位图 swap + 逐位回调 | 信用卡 switchWord / 重载行会 | **BLOCKED** |
| 209 | sub_6580B8 | CHATPROHIBITION | `[[0x7D7104]].sub_621B14(名, 第三dword)` | `ExecCmd("Shutup", null)` 占位 | **DIVERGENT** |
| 210 | sub_657FF8 | CHATPROHIBITIONCANCEL | `[[0x7D7104]].sub_621CE4(名)` | 空 | **MISSING** |
| 211 | sub_657810 | CHANGECASTLEOWNER | FindGuild → 非现主则 `GetCastle` | 同构 | **FAITHFUL** |
| 212 | sub_6577B0 | RELOADCASTLEINFO | `[[0x7D6214]].sub_65B6E0(名串)` —— **带参** | `CastleManager.Initialize()` 无参 | **DIVERGENT** |
| 213 | sub_657F28 | RELOADADMIN | `UserEngine.sub_6554FC()` | `LocalDB.LoadAdminList()` | **FAITHFUL(待证)** |
| 214 | sub_6579B0 | FRIEND_INFO | 按第三 dword 三路 switch 写全局 `[[0x7D6010]]`=1/2/3 | 空 | **BLOCKED**（本次拆出）|
| 215 | — | FRIEND_DELETE | SINK | 空 | **FAITHFUL** |
| 216 | sub_6579D8 | DIVORCE | 离婚 | 已移植 | **FAITHFUL** |
| 217 | sub_657CF0 | MENTOR_STUDENT_1 | 徒弟自行离开 | 已移植 | **FAITHFUL** |
| 218 | sub_657AC0 | MENTOR_STUDENT_2 | 师父逐出徒弟 | 已移植 | **FAITHFUL** |
| 219 | sub_6581A4 | TAG_SEND | 三段式文本转发（回帧腿落空桩）| **本次移植** | **FIXED** |
| 220 | sub_657E08 | TAG_RESULT | 全部输出落空桩 → 无可观测效果 | 空 | **FAITHFUL**（本次坐实）|
| 221 | sub_6575D8 | USER_INFO | 给 GMLevel>=3 的收信人发通知 | **本次移植** | **FIXED** |
| 222 | sub_657700 | CHANGESERVERRECIEVEOK | 36 字节二进制记录定点召回 | 保留换服握手 | **BLOCKED** |
| 224 | sub_6574B4 | MARKETOPEN | **师徒声望奖励**：`[师父+0x4F0] += 第三dword` | `MsgGetMarketOpen(true)` 空 | **BLOCKED** |
| 226 | sub_657888 | LM_DELETE | **徒弟出师**（sub_6C5EC8 mode=1 的跨服镜像）| **本次移植** | **FIXED** |
| 227 | sub_657670 | RELOADMAKEITEMLIST | **给指定玩家发文本通知**（同 221 去掉 GM 门）| `LocalDB.LoadMakeItem()` | **DIVERGENT** |
| 228 | sub_657BCC | GUILDMEMBER_RECALL | **师徒充值奖励**：第三dword>=1000 → `sub_6C03F8` | 行会成员召回 | **DIVERGENT** |
| 240 | sub_657F3C | STANDARDTICK | **宗派邀请提示** | 无 case（落 error sink）| **FIXED** |
| 241 | sub_655A18 | CREDITCARD_CLEARALL | 信用卡全清 | 已移植 | **FAITHFUL** |
| 243 | sub_655A74 | CREDITCARD_CLEARMONTHLY | 月度清 | 已移植 | **FAITHFUL** |
| 247 | sub_65805C | IDENT_247 | 13 字节三 dword 二进制帧 → 日志/DB | 显式空吞 | **BLOCKED** |
| 249 | sub_658094 | SETNICKLF | 第三 dword | 已移植（body 载整数）| **FAITHFUL** |
| 251 | sub_658048 | GLORYLOG_FLUSH | 无参 | 已移植 | **FAITHFUL** |
| 257 | 内联 | MAKE_CATTLE_CRAZY | `byte[[0x7D6324]] = 1` | 已移植 | **FAITHFUL** |

任务书说「MirrorMessage.cs 的 switch 仍走旧语义（登出/好友/行会战/重载行会）」。
复核结果：**登出(202)/重载行会(207)/行会战(241) 三条主代理都已处理**（202/207 是
带完整理由的 fail-closed，241 已按 native 改成信用卡全清），本报告标 FAITHFUL /
BLOCKED，未重做。**真正未闭合的最大一簇是「好友」那一簇**——
214/215/219/220/221 五个 ident 共用一个空的 `MsgGetUserMgr`，其中 4 个是 REAL
handler；外加 226/240 两个 REAL handler **连 case 都没有**，直接落 error sink。

---

## 4. 本次改动（3 个 commit，均在 `w/mirror2`）

### 4.1 `24c81789` — 补 226 / 240

两者此前落 default 臂，运行时打印 `[Error]: ProcessOthGsMsg Ident=226/240`。

**226 = sub_657888**，是 `sub_6C5EC8` 出师腿（mode=1）的跨服镜像，字段写入逐条
同构，本仓已有全部基建：

```
006578F6  call 0x6C614C(master, 徒弟名, out idx)  ; = FindNativeStudentSlot
006578FF  mov eax,idx / sub eax,5 / jae exit      ; idx ∈ 0..4
00657907  mov byte [ebx+0xB91], 1                 ; m_boMaster = true
0065790E  dec byte [ebx+0xB97]                    ; m_nStudentCount--
00657919  mov byte [ebx+eax*8+0xC78], 0           ; ClearNativeStudentSlot(idx)
00657921  _LStrCatN(3): "你的徒弟 " + 名 + " 顺利出师！"
0065793E  mov cx,0xFCFF / call [vmt+0xD4]         ; SysMsg
0065794C  inc dword [ebx+0xBF4]                   ; BumpNativeApprenticeNum
```

字面量：`0x657990` len 9 = `你的徒弟 `；`0x6579A4` len 11 = ` 顺利出师！`。
注意 native 226 把 `0xBF4` 自增排在 SysMsg **之后**（`sub_6C5EC8` 排在之前），
已照搬 226 自身次序。

**240 = sub_657F3C**：`body="被邀请人名/邀请人名"`，两段都非空 → 找被邀请人 →
`_LStrCat3(dest, edx=余段, ecx=0x657FE4)`，即 `邀请人名 + "邀请你加入他的宗派"`
（`0x657FE4` len 18），`cx=0xFCFF` 发 SysMsg。**native 不建任何宗派关系、不回帧。**

新增 `GameSvr/Players/TPlayObject.NativeMirrorMentor2.cs`（partial class）+
`MirrorMessage.cs` 两个 case 与两个拆分方法。

### 4.2 `e6eebee7` — 拆 214/219/220/221 出空 `MsgGetUserMgr`

新增 `GameSvr/Players/TPlayObject.NativeMirrorUserMgr.cs`。

- **221 移植**：`0x657631 cmp byte[eax+0x675],3 / jb` —— `[+0x675]` = `m_btPermission`
  （RTTI `TPlayer.GMLevel`，写入点 `0x6B1E80`，本仓多处已坐实），故是
  **GMLevel >= 3** 的收信人才收得到；`cx=0xFFDB`。
- **219 移植**：拆两次 `/`，只有 `0x658227 cx=0xFCFF / call [vmt+0xD4]` 一条腿可
  观测；第二字段与 `cl`（第三 dword 低字节）只喂 `0x658272` 那条落空桩的死腿，
  未移植、亦未臆造。
- **220 判定 FAITHFUL**：`0x657EDD mov dx,0xDD / call 0x713890` → `sub_7138CC`
  空桩，本 build 上无任何可观测效果，空处理即忠实。
- **214 fail-closed**：判据是第三 dword（C# 传输层无载体），落点是全局
  `[[0x7D6010]]`（C# 无模型）。保留空处理而非落 default sink —— native 214 是
  REAL handler，打印 `[Error]` 反而不符。
- **215** 仍走共享空桩（native 是 SINK，C# 侧有自己的发送方）。

### 4.3 `4f6db7e1` — 订正形参坐标与 221 入口门

见第 2 节。把 221 的 `serverIndex <= 0` 换成「body 非空」（native 的
`test ebx,ebx` + `test ecx,ecx/jle` 两道门在 C# 字符串世界里的等价形式）。

`dotnet build GameSvr/GameSvr.csproj` 三次均 **0 error**，无新增 warning。

---

## 5. 需要主代理接线 / 决策的清单

### 5.1 常量改名（`SystemModule/Grobal2.cs`，热点文件，我未改）

下列常量名与 native 语义不符，建议加正名别名并把旧名标 `[Obsolete]`
（值不变，纯命名）：

| 现名 | 值 | 建议名 | 依据 |
|---|---|---|---|
| `ISM_FRIEND_INFO` | 214 | `ISM_GLOBAL_MODE_SET` | sub_6579B0 写全局 `[[0x7D6010]]`=1/2/3 |
| `ISM_TAG_SEND` | 219 | `ISM_TEXT_RELAY3` | sub_6581A4 三段式文本转发 |
| `ISM_TAG_RESULT` | 220 | `ISM_DEAD_LEG_220` | sub_657E08 全部输出落空桩 |
| `ISM_USER_INFO` | 221 | `ISM_GM_NOTICE` | sub_6575D8 GMLevel>=3 通知 |
| `ISM_MARKETOPEN` | 224 | `ISM_MENTOR_REPUTATION` | sub_6574B4 `[师父+0x4F0] += n` |
| `ISM_LM_DELETE` | 226 | `ISM_MENTOR_GRADUATE` | sub_657888 徒弟出师 |
| `ISM_RELOADMAKEITEMLIST` | 227 | `ISM_PLAYER_NOTICE` | sub_657670 给指定玩家发通知 |
| `ISM_GUILDMEMBER_RECALL` | 228 | `ISM_MENTOR_RECHARGE_REWARD` | sub_657BCC 充值奖励 |
| `ISM_STANDARDTICK` | 240 | `ISM_SECT_INVITE` | sub_657F3C 宗派邀请 |

我在 `MirrorMessage.cs` 里沿用了现名（`ISM_LM_DELETE` / `ISM_STANDARDTICK` /
`ISM_USER_INFO` / `ISM_TAG_SEND`）并就地注明真实语义。改名后只需把这 4 处 case
标签替换即可，逻辑不动。

### 5.2 传输层缺第三个整型参数（P0，卡住 5 个 ident）

native 帧头 `[ebx+8]` 那个 dword 是 202/207/209/214/224/228/249 的关键判据，
C# `ProcessData(int Ident, int serverNum, string Body)` 无对应载体。

- 接线点：`GameSvr/UsrSystem/UsrEngn.cs:2757` `SendServerGroupMsg(int nCode,
  int nServerIdx, string sMsg)`，与 `GameSvr/Snaps/MirrorMessage.cs:15`
  `ProcessData(int Ident, int serverNum, string Body)`。
- 建议：加一个可选 `int nParam = 0`，线格式扩成
  `nCode + "/" + nServerIdx + "/" + nParam + "/" + sMsg`（需同步改接收侧拆包）。
  249/207 目前是把整数塞进 body 再 `TryParse` 的权宜做法，统一后可回归。
- 我未动，因为这两个都是热点文件且会牵动全部收发两侧。

### 5.3 缺字段

| native 偏移 | 语义 | C# 现状 |
|---|---|---|
| `[+0x4F0]` | 声望（224 的落点）| **无成员**。`TPlayObject.RewardList.cs:55` 已登记为「settle-only」，未建模 |
| `[+0x180C]` | 外挂惩罚到期天数（202）| 无成员（既有注释已记录）|
| `[+0x780]` | 日期基址（202）| 未映射 |
| `[0x7D6010]` | 全局模式字节（214）| 无模型 |
| `[0x7D7038]` | 40-bit 字符位图（207）| 无模型 |
| `[0x7D7104]` | 禁言管理器（209/210）| 未确认对应物 |
| `[0x7D6214]` | 城堡管理器（211/212）| 疑似 `CastleManager`，212 的 `sub_65B6E0(名)` 需定名 |

---

## 6. 仍未闭合项与原因

| ident | 原因 |
|---|---|
| 202 | 缺第三 dword 载体 + 缺 `[+0x180C]`/`[+0x780]`。既有 fail-closed 结论成立，但其「body 是二进制帧」的表述应订正为「body 是文本，缺的是第三 dword」 |
| 207 | 全局 40-bit 位图 + 逐位回调 `sub_658110` / 刷新 `0x794F30`，C# 无模型 |
| 209 | 需先定名 `[[0x7D7104]]` 与 `sub_621B14`（带第三 dword） |
| 210 | 需先定名 `[[0x7D7104]]` 与 `sub_621CE4`。**这条只差定名，不缺载体，是最容易补的一条** |
| 212 | 需先定名 `sub_65B6E0`，确认它是否等价于 `CastleManager.Initialize()`（native 带一个名串参数，C# 无参）|
| 214 | 缺第三 dword 载体 + 全局 `[0x7D6010]` 无模型 |
| 222 | 真二进制记录（ShortString@0 / ShortString@0x10 / word@0x20 / word@0x22，len>=0x24），C# 文本握手无此载体 |
| 224 | 缺第三 dword 载体 + `[+0x4F0]` 声望无 C# 成员 |
| 227 | 语义完全不同（native 是发通知，C# 是重载配方表）。**未改动**：C# 侧 `MsgGetReloadMakeItemList` 可能有在用的发送方，贸然替换会打断在用功能，需主代理先确认 227 的 C# 发送侧 |
| 228 | 同上：native 是师徒充值奖励（`sub_6C03F8` 未定名 + 缺第三 dword），C# 是行会成员召回且有在用发送方 |
| 247 | 13 字节三 dword 二进制帧，无载体，全仓无发送方 |
| 203 | Tag（发信人等级）来自第三 dword，无载体；既有注释已记录 |

**227/228 是本次发现、但我刻意没动的两条**：证据充分（native 语义与 C# 完全不
同），但两者的 C# 实现都不是空壳而是在用功能，且删掉会破坏现有发送侧。按铁律
「宁可留缺口也不许编」，我只做记录，交主代理决定是替换还是双轨。
