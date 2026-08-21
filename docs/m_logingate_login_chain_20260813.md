# 登录链路协议对账（Client ↔ LoginGate ↔ LoginSvr / GameGate ↔ GameSvr）

日期：2026-08-13
工作树：`D:\loym2\.claude\wt2\m-logingate`（分支 `w/m-logingate`）
证据基座：`D:\loym2\reference\m2\lg-source\LoginGate\Source\*.pas`（及 `staging\lg_src_utf8` UTF-8 副本）
本轮**未执行任何编译命令**。

---

## 0. 链路时序（文字）

MOBILE_USER ON、ResSocket OFF、LoginCenterAuth ON、SDOBASE OFF。这是 `LoginGate.map` 与 `Build.inc` 交叉确认过的本构建开关集合。

```
Client                         LoginGate                         LoginSvr/DBServer
  |                                |                                    |
  |-- TCP connect :7000 ---------->|                                    |
  |                                |  Initialize: FEnCodeIdx=-1         |
  |                                |  无握手超时；无客户端心跳            |
  |                                |                                    |
  |-- TClientMessage --------------->|                                    |
  |   Sign=$FF44FF44               |                                    |
  |   userType=2, Cmd=24           |  LM_GET_ENCRYPT                    |
  |   DataLength=0                 |  DataIndex = 区号 (AreaIdx)        |
  |   DataIndex=AreaIdx            |                                    |
  |                                |  MOBILE: FIsMobile=True            |
  |                                |  FDynEnCode/DeCode = NullDynCode   |
  |                                |  FEnCodeIdx = -999                 |
  |                                |  下发 4001 服务器列表 (Cmd=23)      |
  |<- 4001 SM_SERVER_LIST ---------|  Recog=0 Tag=0 Param=组数          |
  |   + N × TClientGroupInfo(40)   |  明文（NullDynCode 恒等）           |
  |                                |                                    |
  |   [用户看列表，无超时]           |                                    |
  |                                |                                    |
  |-- Cmd=23, Ident=4002 --------->|  CM_SELECT_SERVER                  |
  |   Recog=客户端版本              |  body = 组名 C 字符串              |
  |   Param=1 则跳过 PK 警告        |                                    |
  |                                |  SelectServer → wRes 1/2/3/4       |
  |                                |  wRes≠1: 12B SM_SELECT_SERVER      |
  |                                |           Series=wRes，结束         |
  |                                |  wRes=1:                            |
  |                                |    ciSessionID = inc, min 1000     |
  |                                |    SecondZone? xor $A5A5A5A5       |
  |                                |                                    |
  |                                |-- 1001 GDM_SELECT_SERVER (28B) --->|
  |                                |   TSelectGroupInfo                 |
  |                                |                                    |
  |                                |<-- 2001 DGM_SELECT_SERVER (28B) ---|
  |                                |   DB 填 wGatePort / ciGateIP       |
  |                                |   ciSessionID=0 → 失败             |
  |                                |     Series=bErrorType (2满/3维护)  |
  |<- 32B TSelectServerMsg --------|   成功: Recog=session              |
  |   Ident=4002                   |   Param=port (±$8000 SecondZone)   |
  |   Tag=IP低16 Series=IP高16     |   AreaID / GroupID / szSuffix      |
  |                                |                                    |
  |   客户端改连 GameGate:port      |                                    |
  |                                |                                    |
  |                                |<-- 2000 DGM_PING (40 或 68) -------|
  |                                |-- 1000 GDM_PING (空) ------------->|
  |                                |   只在首次 2000 做 DB 名校验/踢旧连  |
  |                                |                                    |
  |                                |<-- 2018 TLoginCenterAuthInfo(136)--|
  |                                |-- 1003(124) 成功 / 1004(12) 失败 ->|
  |                                |   20s 超时兜底 1004 nResult=-2     |

Client                         GameGate                            GameSvr
  |                                |                                    |
  |-- 手游 0xFF44FF44 DATA ------->|  原样转发 12B ClientPacket+body     |
  |   4004 认证 / 4012 建角        |  Field20 = payload.Length           |
  |   4013 删角 / 4017 选角        |  动作族不再折 Param 进 Recog        |
  |                                |-- GM_DATA 77BBAA33 :5000 --------->|
  |                                |                                    |
  |                                |  选角成功后 GameSvr 发 SM_STARTPLAY |
  |                                |  进入地图，CM_* 继续走同一跳        |
```

心跳：LoginGate↔DB 是 2000/1000，由 **DB 主动发**。Client↔LoginGate **没有心跳**。GameGate↔Client 有 MARKER_PING / Tiger CMD 29，属 GameGate 2025 侧，不在 LoginGate 源码里。

断线：客户端断开只更新连接计数（`uGateListen.pas:93-97`），不通知 DB。DB 断开发 `WM_GROUP_DISCONNECT`。同名 DB 重连踢旧连接（`uServerInfo.pas:196-197`）。

---

## 1. 加密方案

| 路径 | 原版 | C# | 判定 |
|---|---|---|---|
| 手游 `userType=2` | `NullDynCode`：对 `DataIndex+payload` 调用但恒返回 True、不改字节（`uGateListen.pas:258-261, 285-287`） | 不加密，直接写 `0xFF44FF44` 帧 | **FAITHFUL**（部署约束：现网是手游） |
| PC `userType≠2` | `LM_GET_ENCRYPT` 下发动态编解码 blob（`uEnDeFuncMemManager.pas`，最多 16KB，zlib/LHA），随后 Cmd=23 的载荷从 `TClientMessage.DataIndex` 起加密 | `TryParseConnectRequest` 要求 Flag=2，直接拒绝 | **BLOCKED-3**（见下） |
| 加密范围 | `FDynEnCodeProc(@Data[sizeof(TClientMessage)-4], BufLen+4)` 即 **DataIndex(4)+payload** | 无 | 手游恒等，不影响 |
| 入站上限 | `MAX_RECEIVE_LENGTH=256`，`PackageLen >= 256` 丢弃。最大 DataLength = **243 = 0xF3** | `ClientInboundMaximumPayloadSize = 0xF3` | **FAITHFUL** |

GM IP（`FEnCodeIdx mod 100 = 0`）走无 PK 警告、可跳过版本检查的分支。手游 `-999 mod 100 = -99 ≠ 0`，走普通路径。

---

## 2. 每阶段消息与字段对照

### 2.1 Client ↔ LoginGate（`TClientMessage` 12B + payload）

| 阶段 | 方向 | Cmd | Ident | payload | 原版 | C# 修后 |
|---|---|---|---|---|---|---|
| CONNECT | C→G | 24 | — | 空 | `LM_GET_ENCRYPT`，`userType` 只分流 | 仍要求 Flag=2 |
| 服务器列表 | G→C | 23 | 4001 | 12+N×40 | Param=组数；GroupName[16]+GroupDesc[24] 但 `StrPLCopy(...,15)` | **FAITHFUL**（16+16 有效字节 + 8 零） |
| 选服 | C→G | 23 | 4002 | 12+组名+\0 | Recog=版本；Param=1 跳过 PK 警告 | 忽略版本/PK（本 ini `PK_Warning=0`，手游 EncodeIdx=-999 不触发版本门） |
| 选服成功 | G→C | 23 | 4002 | **32** `TSelectServerMsg` | Recog=session；Param=port；Tag=IP低；Series=IP高；AreaID@12；GroupID@16；BoSDOA@20；szSuffix@24 | **本轮改为走 1001/2001 再组此帧** |
| 选服失败 | G→C | 23 | 4002 | **12** | Series=2/3/4 | **本轮补上**（修前抛异常断连） |

`TSelectGroupInfo` 28B（LoginGate↔DB）：

| off | 宽 | 字段 | 谁写 |
|---|---|---|---|
| 0 | 4 | ciSessionID | LG；DB 置 0 = 失败 |
| 4 | 4 | iEnCodeIdx | LG（手游 -999） |
| 8 | 2 | wSocketHandle | LG（找回客户端；失败帧也靠它） |
| 10 | 2 | wGatePort | **DB** |
| 12 | 4 | ciGateIP | **DB** |
| 16 | 2 | wAreaID | LG，DB 可覆写 |
| 18 | 1 | bGroupNo | LG，DB 可覆写 |
| 19 | 1 | bErrorType | **DB**（2 满员 / 3 维护） |
| 20 | 8 | szPostfix | LG（ini Suffix，`StrPLCopy(...,7)`） |

### 2.2 账号验证（DB → LG → DB）

| Cmd | 载荷 | 原版 | C# |
|---|---|---|---|
| 2018 | 136 `TLoginCenterAuthInfo` | 唯一活认证 | **FAITHFUL** |
| 1003 | 124 | wAuthType=6，nResult=0，szPTID@12 | **FAITHFUL**（上轮已修 GateIdx 回显） |
| 1004 | **12** | nResult ∈ {-1..-5}，20s 超时 -2 | **FAITHFUL**（上轮已修） |
| 2011 / 2013 | 136 | 必回 1004/-2（SDK 未加载） | **本轮补上** |
| 2012 | 76 或 124（**尺寸推算**） | 同上 | 按推算长度回 1004/-2，见 BLOCKED-2 |

### 2.3 选人 / 进入游戏（GameGate ↔ DBSvr/GameSvr）

LoginGate **不处理**选人。客户端拿 4002 跳转后，在 GameGate 上发：

| 手游 Ident | 映射到 LyoMir2 | 含义 |
|---|---|---|
| 4004 | 4004 | 登录认证（ticket body） |
| 4010 | SM_QUERYCHR=520 | 角色列表下行 |
| 4012 | CM_NEWCHR=101 | 建角 |
| 4013 | CM_DELCHR=102 | 删角 |
| 4017 | CM_SELCHR=103 | 选人进入 |
| — | SM_STARTPLAY=525 → 4017 | 进入成功 |

C# `MobileCmdMap.cs` 与上表一致。会话在 **4004 成功时** `OpenMobileSession(account, ip, sessionId)` 建立；sessionId 来自跳转帧 Recog（即 1001 的 ciSessionID）。同账号不同 sessionId 会 `SS_KICKUSER` 顶号（`LoginSocService.cs:791-826`）。GameSvr `AccountService.NewSession` 同样踢旧 session（`:269-285`）。

异常断开：LoginGate 侧无会话表可清。GameSvr `DelSession` / `CloseUser` 走 `GM_CLOSE`。C# 与此同构，未改。

---

## 3. GameGate ↔ GameSvr

动作族方言（Param 折进 Recog 高字、Series 复制到 Tag）**当前代码已不存在**：

`GameGate-CS/Core/GateServer.cs:66-74` `CreateGameSvrClientPacket` 原样拷贝 Recog/Param/Tag/Series。
GameSvr `UsrEngn.cs:2644-2675` 按原生 `0x6D9EAF` 读 Param=Y、Series&7=方向、Recog=X。

body 长度：C# GameGate `CreateGameDataPacket` 设 `Field20 = payload.Length`，`FrameLen = 24 + payload.Length`。GameSvr `GateService.cs:151` 用 `body.Length`（来自 FrameLen）作 `nMsgLen`，**不读 Field20**。C#↔C# 两值恒等。原生 Field20 能否与 FrameLen-24 不一致 → **BLOCKED-4**。

网关仍会改写个别 body（1018 认证串注入、聊天过滤改长度）。1018 注入是否等于原生 GameGate 2025 → **BLOCKED-5**（需 GG 2025 镜像，不在本次 LoginGate 源码范围内）。

---

## 4. 本轮改了什么

| 文件 | 改动 | 证据 |
|---|---|---|
| `LoginGateWireProtocol.cs` | 1001/2001 正名为选服；补 `TSelectGroupInfo` 组包、12B 失败帧；跳转帧允许负 Recog（SecondZone xor） | `uTypes.pas:74/86/153`；`uMainThread.pas:195,281`；`uGateListen.pas:221` |
| `NativeDbServerService.cs` | **删除注册/心跳上的随机 1001**；选服时发真 1001、等 2001；失败 2001 按 `wSocketHandle` 找回；2011/2013（及 2012 推算长度）回 1004/-2；畸形 2001 只打日志不断连 | `uDBListen.pas:231,306`；`uMainThread.pas:194`；`uSDKAuth.pas:607` |
| `ClientSelectionService.cs` | 选服不再本地 `FindRoute`；错误 Series=2/3/4 发给客户端 | 同上 + `uServerInfo.pas:117-180` |
| `LoginGateSelfTests.cs` | 时序改为 2000→1000，选服才出现 1001 | — |
| `NativeLoginGateProtocol.cs` | 2001 回显请求的 wAreaID/bGroupNo，只填 GameGate IP/端口 | `uMainThread.pas:206-207` 用的是 DB 回的区/组 |

C#↔C# 仍然连通：DBSvr 本来就会应答 1001 并填 GameGate 地址。差别是 1001 现在带着真实 session/区/组，原版 DBServer 能建会话。

---

## 5. BLOCKED

| ID | 缺什么 | 卡在哪 |
|---|---|---|
| BLOCKED-1 | `TStatusAuthResult.UIDSet` 按 1 对齐还是 2 对齐（@75 vs @76） | 两种模型都是 124 字节。需原版 DBServer 读 1003 的字节或一份真实 1003 抓包 |
| BLOCKED-2 | `TOldDynamicAuthInfo`=76 / `TDynamicAuthInfo`=124 只有对齐推算 | 2012 长度门用了这两个数。需 DBServer 反汇编或抓包 |
| BLOCKED-3 | 现网是否存在 `userType≠2` 的 PC 客户端 | 有则 C# 必须补动态加解密下发 |
| BLOCKED-4 | 原生 GameGate 的 Field20 是否恒等于 FrameLen-24 | 需 GG 2025 `flat_image` 写 Field20 的现场或 pcap |
| BLOCKED-5 | GameGate-CS 在 ident 1018 上注入 `**acct/chr/sid/...` 认证串，是否等于原生 GG | LoginGate 源码覆盖不到这一跳 |
| BLOCKED-6 | 原版 DBServer 收到 1001 后如何建会话（字段、超时、与 4004 的关系） | DBServer.exe 加壳；脱壳产物在 `staging\_dbsvr_reunpack_work\`，本任务未拆 |

未落地（需主控确认配置后再 fail-closed）：DB 名必须在 ini 的 `group{J}DBS` 里否则踢连；同名 DB 重连踢旧连接；单帧上限 8175 vs 32752；畸形帧重同步不断连。客户端 30s 空闲超时原版 LoginGate 源码里没有（可能在缺失的 `IocpSocket.pas`），未改。
