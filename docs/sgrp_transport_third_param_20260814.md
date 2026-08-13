# 跨服传输层第三个整型参数 (sgrp3rd, 20260814)

底本 `staging\_reunpack_work\flat_image.bin`，ImageBase `0x400000`，
`file_off = VA - 0x400000`。分支 `w/sgrp3rd`，基线 master `56c3dcf5`。
工具：前作 `tools/mirror2_*.py` + 本次新增 `tools/sgrp3_{scan,win,pre}.py`。

前作 `docs/mirror_crossserver_20260814.md` 的形参坐标结论本次**逐条复核通过**；
但它对「帧」的结构描述有一处需要订正，见 §1.4。

---

## 1. 原生帧头完整布局（本次钉死）

前作只走到「派发器的三个形参从哪来」。本次把**收发两侧**都走完了。

### 1.1 传输帧（socket 字节流层）

收流解析器在 `sub_7133xx` 内，逐字节扫魔数：

```
00713461  8b4348          mov eax,[ebx+0x48]        ; 接收缓冲基址
00713464  8d3c30          lea edi,[eax+esi]         ; 扫描游标
00713467  813f77bbaa33    cmp dword [edi],0x33AABB77 ; ← 魔数
0071346D  7556            jne 0x7134C5              ; 不匹配则前进 1 字节
0071346F  8d460c          lea eax,[esi+0xC]         ; 净荷偏移 = 游标 + 12
00713472  8945f8          mov [ebp-8],eax
00713475  8b4708          mov eax,[edi+8]           ; ← 净荷长度
00713478  0345f8          add eax,[ebp-8]
0071347B  3b4344          cmp eax,[ebx+0x44]        ; 收够了没有
0071347E  7f54            jg  0x7134D4              ; 没收够就等
...
007134A3  8b4708          mov eax,[edi+8]
007134A6  50              push eax                  ; len
007134A7  8b4348          mov eax,[ebx+0x48]
007134AA  8b55f8          mov edx,[ebp-8]
007134AD  8d0c10          lea ecx,[eax+edx]         ; 净荷指针
007134B0  668b5704        mov dx,word [edi+4]       ; ← kind
007134B4  8bc3            mov eax,ebx
007134B6  e829030000      call 0x7137E4             ; Enqueue
007134BB  8b4708          mov eax,[edi+8]
007134BE  83c00c          add eax,0xC
007134C1  03f0            add esi,eax               ; 前进 12 + len
```

**传输帧 = 12 字节头 + 净荷：**

| 偏移 | 宽度 | 含义 |
|---|---|---|
| `+0x00` | dword | 魔数 `0x33AABB77`（小端，磁盘字节序 `77 BB AA 33`）|
| `+0x04` | word | kind（1 / 2 / 3）|
| `+0x06` | word | 未被解析器读取 |
| `+0x08` | dword | 净荷长度 |
| `+0x0C` | — | 净荷 |

### 1.2 入队与出队

`sub_7137E4(eax=Self, edx=kind, ecx=srcBuf, [ebp+8]=len)` 建 16 字节队列节点：

```
007137F5  b810000000  mov eax,0x10 / call 0x402FA0   ; GetMem(16)
00713803  66897b08    mov word [node+8], di          ; kind
0071380A  897b04      mov [node+4], edi              ; len
0071380F  call 0x402FA0 / 00713814 8903 mov [node],eax ; 复制一份净荷
0071381E  call 0x403260                              ; Move(src, dst, len)
00713825  89430c      mov [node+0xC], 0              ; next
0071383D  call 0x408210 ... 0x713877 call 0x4083A8   ; 临界区内挂到 [sess+0x7C/0x80]
```

出队在 `sub_713DDC`（虚方法，VMT 槽 `0x712CE4`）。它按 kind 分头长：

```
00713E68  668b4308  mov ax,word [node+8]
00713E6C  dec ax / je 0x713E80    ; kind 1 -> 头长 0x48
00713E71  dec ax / je 0x713EC0    ; kind 2 -> 头长 0x0C   ← 本任务
00713E76  dec ax / je 0x713EF7    ; kind 3 -> 头长 0x40
```

kind 2 那一臂：

```
00713EC0  837b040c  cmp dword [node+4],0xC / jl     ; 净荷 >= 12
00713EC6  8b4304 / 83e80c  eax = len - 0xC
00713ED5  8b03 / 83c00c    eax = 净荷基址 + 0xC
00713EE4  push [ebp-0xC]   ; = len - 0xC
00713EE8  8b13             mov edx,[node]           ; 净荷基址 = ISM 帧
00713EEA  8b4df8           mov ecx,[ebp-8]          ; 帧基址 + 0xC = body
00713EF0  e8d3efffff       call 0x712EC8
```

### 1.3 ISM 帧（kind 2 的净荷）

`sub_712EC8` 里 `ebx = edx = 净荷基址`：

```
00712EF6  0fb713    movzx edx,word [ebx]     ; ← route
00712EF9..00712F25  route 分派: 0x69/0x6F/0x72/0x74/0x13E/0x1A4
00712F3F  8b4308    mov eax,[ebx+8]  / push  ; → [ebp+0x10]
00712F43  8b45fc    mov eax,[ebp-4]  / push  ; → [ebp+0xC]   body 指针
00712F47  56        push esi                 ; → [ebp+8]     body 长度
00712F4F  8b4b04    mov ecx,[ebx+4]          ; → ecx（派发器不读）
00712F52  668b5302  mov dx,word [ebx+2]      ; ← ident
00712F56  e8b541f4ff call 0x657110           ; ProcessOthGsMsg
```

**ISM 帧 = 12 字节头 + body：**

| 偏移 | 宽度 | 含义 | 到达 handler 的方式 |
|---|---|---|---|
| `+0x00` | word | route（`0x6F`=111 才走 `ProcessOthGsMsg`）| — |
| `+0x02` | word | **ident**（202..257）| `dx` |
| `+0x04` | dword | **P1** | `ecx`；`0x657110` 序言只存 `dx`，**从不读 ecx** |
| `+0x08` | dword | **P2 = 本任务要补的「第三个 dword」** | `[ebp+0x10]` |
| `+0x0C` | — | body（长度 = 净荷长度 − 12）| `[ebp+0xC]` / `[ebp+8]` |

字节序全部小端（x86 原生 `mov`，无任何 `bswap`/`xchg`）。

### 1.4 对前作的一处订正

前作把 `[ebx+4]` 记为「帧总长」、`[ebx+8]` 记为「帧头第三个 dword」。

**`[ebx+4]` 不是总长。** 前作把两个不同的 `ebx` 混为一谈了：

- `sub_713DDC` 里的 `ebx` = **队列节点**，`[node+4]` 才是长度；
- `sub_712EC8` 里的 `ebx` = **ISM 帧基址**，`[frame+4]` 是 ISM 头里一个独立的
  dword（上表的 P1）。

证据：`0x712F82` 那条 route（`0x13E`）把同一个 `[ebx+4]` 当**对象指针**用 ——
`0x712FD5 mov ebx,[ebx+4]` 之后 `0x712FE7 cmp byte [ebx+0x675],4`（GMLevel）、
`0x713023 call dword [ebx+0xD4]`（SendMsg 虚调）。长度不可能这样用。

形参坐标本身（`[ebp+8]`=len / `[ebp+0xC]`=body / `[ebp+0x10]`=P2）**复核通过**，
前作的自纠是对的。

### 1.5 「第三个 dword」在发送端是谁写的、写什么

原生的 ISM 发送编组函数是 `sub_713890`：

```
00713890  55 8B EC ...
00713897  66894dfe  mov word [ebp-2],cx
0071389B  8bfa      mov edi,edx            ; ident
0071389F  8b7508    mov esi,[ebp+8]        ; body : AnsiString
007138A2  8b450c    mov eax,[ebp+0xC]      ; ← nParam
007138A5  50        push eax               ;   → 0x7138CC 的 [ebp+0x10]
007138A6  8bc6 / e82321cfff  call 0x4059D0 ; PChar(body)
007138AD  50        push eax               ;   → [ebp+0xC]
007138AE  8bc6 / e81b1fcfff  call 0x4057D0 ; Length(body)
007138B5  50        push eax               ;   → [ebp+8]
007138B6  668b4dfe  mov cx,[ebp-2]
007138BA  8bd7      mov edx,edi
007138BE  e809000000 call 0x7138CC
```

**编组出的三个栈槽与 handler 的 `[ebp+8]/[ebp+0xC]/[ebp+0x10]` 逐槽同构。**
即：第三个 dword 就是发送方 `SendOthGsMsg(ident, cx, body, nParam)` 的
**最后一个整型实参**，语义完全由 ident 决定。

**但 `0x7138CC` 是空桩**：

```
007138CC  55 8B EC 5D C2 0C 00     push ebp / mov ebp,esp / pop ebp / ret 0xC
```

所以本 build 上**全部 26 个 `sub_713890` 调用点都是编组后丢弃**，M2Server 自身
发不出任何 ISM 帧。独立佐证：route 111 在整个镜像里**没有任何构造者** ——
`mov word [x],0x6F` 0 命中、`mov dx,0x6F` 0 命中；两个 kind=2 帧构造器
`sub_713094` / `sub_7130E8` 的 13 个调用点用的 route 是
60 / 62 / 66 / 375 / 384..391 / 401（DBServer 请求族，会话对象 `[0x7D62DC]`），
无一为 111。且这两个构造器**只写 ISM `+0`/`+4`/`+8`，不写 `+2`**（Delphi 记录
`Word; Integer; Integer` 的自然对齐，`+2` 是填充），也就是说它们本来就不是给
需要 ident 的 route 111 用的。

结论：**原生 ISM 帧只可能来自外部对端进程**；本仓因此**不新增 C# 发送侧**。

### 1.6 原生发送侧清单（26 个 `sub_713890` 调用点）

即使落空桩，这张表仍是「每个 ident 的 nParam 该放什么」的权威依据。
调用点压栈次序：先压 nParam（`[ebp+0xC]`），后压 body（`[ebp+8]`）。

| VA | dx | ident | cx | nParam | body |
|---|---|---|---|---|---|
| 0x6D4AA2 | 0xCA | **202** | 0 | `esi` 惩罚时长 | `[player+0xB33]` 账号串 |
| 0x652DDA | 0xCB | 203 | `word[ebp-8]+1` | — | catN(5) |
| 0x62E951 | 0xCF | **207** | 0 | `[[0x7D7038]]` 位图 dword | 0 |
| 0x6C4F16 | 0xD0 | 208 | 0 | — | `[ebp-0x128]` |
| 0x6BF2B8 | 0xD1 | **209** | 0 | `esi` 秒数 | `ebx` 角色名 |
| 0x6C9189 | 0xD1 | **209** | 0 | `esi` | `[player+0x106]` |
| 0x6BF372 | 0xD2 | **210** | 0 | 0 | `edi` 角色名 |
| 0x65BFEB | 0xD3 | 211 | 0 | 0 | `[ebx+0x44]` |
| 0x65B6CD / 0x65BCDD | 0xD4 | 212 | 0 | 0 | `[esi+0x10]` / 0 |
| 0x625DC1 | 0xD5 | 213 | 0 | 0 | 0 |
| 0x625650 | 0xD6 | **214** | 0 | `ebx` 模式 0/1/2 | 0 |
| 0x6C5330 | 0xD8 | 216 | `word[ebp-4]+1` | 0 | `[ebp-8]` |
| 0x641A19 | 0xDA | 218 | `word[ebp-0x14]+1` | — | `[ebp-0x1C]` |
| 0x6CD2E0 | 0xDB | 219 | `word[ebp-0xC]+1` | `byte[ebp-0xD]` | `[ebp-8]` |
| 0x6BE826 | 0xDC | 220 | `word[ebp-0xC]+1` | — | `[ebp-0x128]` |
| 0x657EDD | 0xDD | 221 | `edi` | 0 | catN(3) |
| 0x6BDEC0 | 0xE0 | **224** | `word[ebp-0xC]+1` | `byte[ebp-6]` 声望点 | `[self+0xC58]`+`/`+`[self+0x106]` |
| 0x658272 | 0xE3 | 227 | `word[ebp+0xC]` | 0 | catN(3) |
| 0x626126 | 0xF1 | 241 | 0 | 0 | 0 |
| 0x62609C | 0xF3 | 243 | 0 | 0 | 0 |
| 0x72578C | 0xF6 | 246 | 0 | 0 | 0 |
| 0x62EBC0 | 0xF9 | 249 | 0 | `esi` | catN(3) |
| 0x714F68 | 0xFB | 251 | `word[[0x7D7024]]+2` | 0 | 0 |
| 0x72904A | 0x101 | 257 | 3 | 0 | 0 |
| 0x6C6034 | `esi` | 变量 | 0 | — | `[ebp-0x18]` |

（「—」= nParam 的压栈点在取窗之外，未逐条展开；均非本次接线目标。）

**两条交叉验证**：

- **214**：发送侧 `0x6255FE` `StrToIntDef(arg,0)` → `ebx`，`ebx==0/1/2` 时本地
  `[[0x7D6010]] = 1/2/3`，越界则 `ebx:=0` 且置 1，然后 `push ebx` 发 214。
  接收侧 `sub_6579B0` `sub edx,1 / jb→1 / je→2 / dec/je→3`，即 nParam
  **0/1/2 → 模式 1/2/3**，与发送侧逐值吻合。
  （前作记为「三路 switch 写 1/2/3」，入参值域 0/1/2 这一点本次补上。）
- **224**：发送侧 body = `[self+0xC58]` + `'/'`（`0x6BDF94` 是长度 1 的
  ShortString `/`）+ `[self+0x106]`；接收侧 `sub_6574B4` 取首段做
  `GetPlayObject`、余段进提示文案。首段=师父、余段=徒弟，两侧吻合。

### 1.7 C# 的 `serverNum` 在原生对应什么

**不对应任何东西。** 原生 ISM 头的 4 个槽是 route / ident / P1 / P2，没有服务器
索引；`0x657110` 序言只保存 `dx`，P1（`ecx`）连读都不读。

本仓的 `serverNum` 是传输层自加的，且**确实需要**：C# 的 hub
（`SnapsmService.DecodeSocStr_SendOtherServer`）把收到的整串**无差别转发**给
其余所有节点，包含发送者自己那一路以外的全部连接。多个 handler 靠
`sNum == M2Share.nServerIndex` 决定「这条是不是发给我的」（`MsgGetWhisper` /
`MsgGetRecall` / `MsgGetLoverLogin` …）。原生不需要它，是因为原生的对端由外部
进程做定向投递。**不能与第三个 dword 合并**：两者在已接线的 ident 上会同时出现
（如 224 既要 serverIdx 路由、又要声望点数），且 `serverNum` 的取值域包含
`-1`（`Str_ToInt(sNumStr, -1)` 的失败值），与 P2 的语义无交集。**保留。**

---

## 2. 线格式扩展方案

### 2.1 现状

```
发: UsrEngn.SendServerGroupMsg -> "(" + nCode + "/" + nServerIdx + "/" + sMsg + ")"
收: SnapsmService.DecodeSocStr / SnapsmClient.DecodeSocStr
      Body = GetValidStr3(Str,  ref Head,    "/")   // Head    = ident
      Body = GetValidStr3(Body, ref sNumStr, "/")   // sNumStr = serverIdx
      ProcessData(Ident, sNum, Body)                // Body    = 其余整串
```

关键性质：收侧**恰好拆两次**，`Body` 保留其后所有 `/`。所以任何扩展只能长在
`Body` 内部。

### 2.2 方案

对**「原生 handler 确实读 `[ebp+0x10]`」且「本仓已按原生落地」**的 ident，
线格式变为：

```
nCode / nServerIdx / nParam / sMsg
```

- **发**：`SendServerGroupMsg` 增加一个 4 参重载（3 参重载**逐字节不变**）。
- **收**：两个 socket 类**不动**（它们只拆两次）；由 `ProcessData` 在 switch
  之前按 ident 剥掉前置整型字段。
- **中转**：`SnapsmService.DecodeSocStr_SendOtherServer(ps, Str)` 原样转发
  `Str` 整串，四字段帧穿过 hub 不变形，**hub 无需改动**。

取用集合（`MirrorMessage.CarriesNativeParam`）目前**只有 209 与 224**。

### 2.3 向后兼容论证

要证明的是：**现有已跑通的 ident 行为一字不变。**

1. **发送侧零变更**：3 参重载的字符串拼接原样保留，所有既有调用点
   （`UsrEngn.cs:1568` 的 202、`CreditCardCommand.cs:31` 的 207、
   `MirrorMessage` 内部的 222/236 等）产生的字节流完全不变。
2. **接收侧按 ident 白名单剥离**：`CarriesNativeParam` 默认 `false`，不在集合里
   的 ident 走的代码路径与改动前**逐语句相同**（`Body` 未被触碰）。
3. **集合内的两个 ident 没有既有发送方**。全仓 `SendServerGroupMsg(` 调用点穷举
   （含 `SS_*` 别名常量与跨行写法）后，实际发出的 ident 集合是：

   | ident | 发送点 |
   |---|---|
   | 201 | `UsrEngn.cs:859` `ISM_USERLOGON` |
   | 202 | `UsrEngn.cs:1439` `SS_202`、`UsrEngn.cs:1568` `ISM_USERLOGOUT` |
   | 203 | `TPlayObject.Chat.cs:77` `ISM_WHISPER` |
   | 204 | `UserCastle.cs:466/506/705/757` `SS_204` |
   | 205 | `GuildOfficial.cs:149` `SS_205` |
   | 207 | `TPlayObject.Operate.cs:2206-2238` / `TPlayObject.cs:4425-4426` `SS_207`；`CreditCardCommand.cs:31`、`MapDropItemCommand.cs:30` `ISM_SERVERSWITCH` |
   | 208 | `TPlayObject.Message.cs:486`、`TPlayObject.Chat.cs:221/228` `SS_208` |
   | 211 | `UserCastle.cs:710` `SS_211` |
   | 212 | `UserCastle.cs:462/1154` `SS_212` |
   | 213 | `ReLoadAdminCommand.cs:18`（字面量 213）|
   | 216 | `PasApiBridge.cs:1464` `ISM_DIVORCE` |
   | 222 | `MirrorMessage.cs:392` `ISM_CHANGESERVERRECIEVEOK` |
   | 241 / 243 | `CreditCardCommand.cs:71/52` |
   | 249 | `SetNickLFCommand.cs:31` `ISM_SETNICKLF` |
   | 257 | `PasApiBridge.cs:8152` `ISM_MAKE_CATTLE_CRAZY` |
   | 其余 | `ISM_USERSERVERCHANGE`（`UsrEngn.Switch.cs:92`）、`ISM_LM_LOGIN_REPLY`（`MirrorMessage.cs:798`）|

   **209 / 210 / 224 / 227 / 228 一个都不在表内。** 所以不存在「旧格式帧被新收侧
   误拆」的现场。（`Grobal2.SS_209` / `SS_210` 常量虽然存在，但无任何发送点引用。）
4. **兜底**：`TakeNativeParam` 用 `int.TryParse` 判首字段。首字段不是整数时
   判定为老三字段帧，`nParam = 0` 且 `Body` 原样返回。即使将来有人用 3 参重载
   发 209/224，也只会退化成 `nParam = 0`，不会把 body 切碎。
5. **刻意排除的 7 个**：原生读 `[ebp+0x10]` 的 stub 共 9 个
   （202 `0x657208` / 203 `0x65721C` / 207 `0x657230` / 209 `0x65723D` /
   214 `0x657287` / 219 `0x6572C4` / 224 `0x65730A` / 228 `0x65733E` /
   249 `0x657385`）。其余 7 个**不纳入**，理由分两类：
   - **有在用的三字段发送方，加字段会改变现有行为**：
     207 的 body 是纯数字（信用卡 switchWord），一旦剥离会把 body 清空、
     switchWord 丢失；249 的 body 也是纯数字（昵称灵符倍数）；203 的 body 是
     `姓名/正文`，剥离会吃掉姓名；202 的 body 是角色名，虽然 `TryParse` 兜底，
     但纯数字角色名会误判。
   - **仍 BLOCKED，纳入也无用**：214 / 228（见 §4）。

### 2.4 跨服对端如何解析

对端就是**同一套 C# 二进制**的另一个节点（`SnapsmService` 是 hub、
`SnapsmClient` 是叶子），走的是同一份 `MirrorMessage.ProcessData`，
`CarriesNativeParam` 表两端一致，**自然对称**。

对**原生 M2Server 对端**：不互通，且本来就不互通 —— 原生走的是
§1.1 的二进制帧（魔数 `0x33AABB77` + kind + 定长 12 字节 ISM 头），C# 走的是
`(文本/文本/文本)`，两者在传输层就无法握手。本方案没有让这一点变好或变坏。

混版滚动升级（旧收侧 + 新发侧）不被支持：旧收侧会把 `nParam` 当成 body 的第一段。
但 209/224 在旧版里一个是坏占位、一个是空函数，实际无行为可破坏。

---

## 3. `UsrEngn.cs:2757` 的完整替换代码块（禁改文件，交主代理执行）

把 `GameSvr/UsrSystem/UsrEngn.cs` 现有的

```csharp
        public void SendServerGroupMsg(int nCode, int nServerIdx, string sMsg)
        {
            if (M2Share.nServerIndex == 0)
            {
                SnapsmService.Instance.SendServerSocket(nCode + "/" + nServerIdx + "/" + sMsg);
            }
            else
            {
                SnapsmClient.Instance.SendSocket(nCode + "/" + nServerIdx + "/" + sMsg);
            }
        }
```

整体替换为：

```csharp
        public void SendServerGroupMsg(int nCode, int nServerIdx, string sMsg)
        {
            if (M2Share.nServerIndex == 0)
            {
                SnapsmService.Instance.SendServerSocket(nCode + "/" + nServerIdx + "/" + sMsg);
            }
            else
            {
                SnapsmClient.Instance.SendSocket(nCode + "/" + nServerIdx + "/" + sMsg);
            }
        }

        /// <summary>
        /// 带「原生帧头第三个 dword」的跨服发送。
        ///
        /// 战神 ISM 帧头是定长 12 字节 —— word route @+0 / word ident @+2 /
        /// dword P1 @+4 / dword P2 @+8 —— 其中 P2 由 0x712F3F `mov eax,[ebx+8]`
        /// 压栈, 到达 handler 时是 [ebp+0x10]。发送侧编组函数 sub_713890 @0x713890
        /// 压的三个栈槽 (0x7138A2 nParam / 0x7138AD PChar(body) / 0x7138B5
        /// Length(body)) 与之逐槽同构, 即 P2 就是发送方的最后一个整型实参。
        ///
        /// 本仓的文本线格式据此扩成 nCode/nServerIdx/nParam/sMsg。收侧
        /// MirrorMessage.ProcessData 只对 CarriesNativeParam(ident) 为真的 ident
        /// 剥离该字段, 故三参重载的既有调用点行为完全不变。
        /// 详见 docs/sgrp_transport_third_param_20260814.md。
        /// </summary>
        public void SendServerGroupMsg(int nCode, int nServerIdx, int nParam, string sMsg)
        {
            var line = nCode + "/" + nServerIdx + "/" + nParam + "/" + sMsg;
            if (M2Share.nServerIndex == 0)
            {
                SnapsmService.Instance.SendServerSocket(line);
            }
            else
            {
                SnapsmClient.Instance.SendSocket(line);
            }
        }
```

该重载目前**无调用方**（原生 `0x7138CC` 是空桩，本仓刻意不新增发送侧），
C# 对 public 方法不报 unused 警告，故不影响 15 条警告基线。
若主代理希望零新增代码，也可以先不加这个重载 —— 收侧已经能解四字段帧，
接线随时可补。

> **主代理裁决（2026-08-14）：不加这个重载，只保留收侧。**
> 理由是本报告自己给出的证据：`0x7138CC` 是单条 `ret 0xC` 空桩，26 个调用点
> 全部编组后丢弃；且全镜像的帧构造者用的都是 DBServer 路由
> （60/62/66/375/384-391/401），**没有任何一处构造 route 111**。
> 即原生 M2Server 只**接收**不**发送** ISM 帧。按「无原生依据不落地」的铁律，
> 补一个 C# 发送侧等于给本仓添一项原生不具备的能力，属于臆造方向的改动。
> 收侧解析必须补（外部对端确实会发），已随 `b15d4953` 落地。
> 将来若确认某个对端实现依赖 M2 回发，再按那时的证据补，届时本节代码块可直接取用。

---

## 4. 六个被卡 ident 的逐条现状

| ident | native | 现状 | 说明 |
|---|---|---|---|
| **209** | `sub_6580B8` | **已移植** | 见 §4.1 |
| **224** | `sub_6574B4` | **已移植** | 见 §4.2 |
| 202 | `sub_658384`→`sub_653ED0` | **BLOCKED** | 缺字段 `[+0x180C]` |
| 207 | `sub_658114` | **BLOCKED** | 缺全局 40-bit 位图模型 |
| 214 | `sub_6579B0` | **BLOCKED** | 缺全局 `[[0x7D6010]]` 模型 |
| 228 | `sub_657BCC` | **证据齐备，按指示未动** | 见 §6 |

### 4.1 209 已移植

```
stub 0065723D  8B 4D 10  mov ecx,[ebp+0x10]     ; 第三个 dword
     00657240  8B 55 0C  mov edx,[ebp+0xC]      ; body
     00657243  E8 70 0E 00 00  call 0x6580B8
sub_6580B8
     006580D6  E8 2D D6 DA FF  call 0x405708     ; _LStrFromPChar -> 裸角色名
     006580DE  A1 04 71 7D 00  mov eax,[0x7D7104]
     006580E3  8B 00           mov eax,[eax]     ; 禁言管理器单例
     006580E5  8B CE           mov ecx,esi       ; = 第三个 dword
     006580E7  E8 28 9A FC FF  call 0x621B14     ; Add
```

`sub_621B14(mgr, edx=name, cx=seconds)`：

```
00621B4D  call 0x40BCBC             ; UpperCase(name) —— 比较大小写不敏感
00621B5F/00621B6A  遍历 [mgr+0x20] 链表, 0x405774 取节点名 + 0x40591C 比较
00621B71  0fb745fa  movzx eax,word [ebp-6]      ; ← 只取低 16 位
00621B77  014214    add [node+0x14],eax          ; 命中: 累加, 不写标志/不落盘
00621B93  b820000000 mov eax,0x20 / call 0x402FA0 ; 未命中: GetMem(32)
00621BCD  call 0x4039E4 (cl=0x0F)   ; 存 UpperCase 名, ShortString[15]
00621BD6  894314    mov [node+0x14],eax          ; 剩余秒数
00621BE6  call 0x408340 / 894318                 ; [node+0x18] = GetTickCount
00621BFA  ff4624    inc [mgr+0x24]
00621C10  c680990b000001  mov byte [player+0xB99],1   ; 在线才写
00621C19  e8120a0000      call 0x622630               ; Save
```

单位是**秒**，由清扫 `sub_622040` 定：

```
00622085  2b4318    sub eax,[node+0x18]
0062208A  b9e8030000 mov ecx,0x3E8
00622091  f7f1      div ecx
00622093  294314    sub [node+0x14],eax     ; 每 1000 ms 扣 1
```

C# 落点：`[[0x7D7104]]` 的身份本仓**早已坐实并写在
`Services/NativeGmDenyListCommands.cs` 的头注释里**（`off_7D7104 -> the singleton
manager instance`，并列出 `sub_621B14`=Add / `sub_621CE4`=Delete /
`sub_622040`=Tick / `sub_622630`=Save，甚至写了
「`sub_657110` ProcessOthGsMsg cases 209/210 -> cross-server Add/Delete
replication」）—— 本次就是把那两条接线补上。

活存储是 `M2Share.g_DenySayMsgList`（`角色名 → 到期 tick(ms)`），
`byte[player+0xB99]` 在 C# 不是独立字段，而是由字典成员资格派生
（`TPlayObject.NativeCorpsChat.cs` 的 `IsNativeChatMuted()`），故增删字典项即
等价于置/清 `[+0xB99]`。

新增 `GameSvr/Services/NativeMirrorChatBan.cs`。原实现是
`M2Share.CommandSystem.ExecCmd("Shutup", null)` —— 传 `null` 参数的坏占位。

已记录差异（均为既有存储的性质，非本次引入）：`g_DenySayMsgList` 用序数比较器
（原生大小写不敏感）；不落盘（原生每次增删写 `BlockUsers.Dat`，完整编解码已在
dormant 的 `NativeGmBlockUserList` 里建模）。

### 4.2 224 已移植

```
stub 0065730A  8B 45 08 / 50      push [ebp+8]        ; len
     0065730E  8B 4D 10           mov ecx,[ebp+0x10]  ; 第三个 dword
     00657311  8B 55 0C           mov edx,[ebp+0xC]   ; body
     00657314  E8 9B 01 00 00     call 0x6574B4
sub_6574B4
     006574DA  call 0x405708                     ; _LStrFromPChar
     006574E6  B1 2F                mov cl,0x2F   ; '/'
     006574EB  call 0x4C6AEC                     ; 拆首段/余段
     006574FB  83 7D F8 00 / 74 5C  cmp [ebp-8],0 / je   ; 首段(师父名)非空
     0065750B  call 0x652784                     ; GetPlayObject(首段)
     00657512  85 DB / 74 47        test ebx,ebx / je
     00657516  85 F6 / 7E 43        test esi,esi / jle   ; 第三个 dword > 0
     0065751A  01 B3 F0 04 00 00    add [ebx+0x4F0],esi  ; 声望 += n
     00657520..00657547             _LStrCatN(5)
     0065754F  66 B9 FF FC          mov cx,0xFCFF
     00657557  FF 93 D4 00 00 00    call [vmt+0xD4]      ; SysMsg
```

五段字面量（GBK，长度前缀已核）：`0x657590` len 0x10 `恭喜：您的徒弟: ` /
余段（徒弟名）/ `0x6575AC` len 0x17 ` 等级提升，给您带来了: ` /
`IntToStr(esi)` / `0x6575CC` len 0x0B ` 点声望增加`。

**`[+0x4F0]` = 声望，本次坐实**（前作记为「无成员」，是漏查）：
`@ChgSwTo`「调整玩家声望」（命令表 idx 107、处理器 `0x0062513F` → `sub_6C2148`，
已记于 `NativeGmPlayerAttrCommands.cs`）在

```
006C21A3  8b90f0040000  mov edx,[eax+0x4F0]     ; 读旧值
006C21AC  8998f0040000  mov [eax+0x4F0],ebx     ; 写新值
```

C# 的声望标量是 `TPlayObject.m_nShengWan`（`PasApiBridge` 的 `myshengwan`、
`Give "声望"`、`SetShengWan`、存档 `HumData.nShengWan`），故
`[+0x4F0] ↔ m_nShengWan`。

**注意 native 224 不发 `RM_ABILITY`**（`0x65751A` 之后直接拼串发 SysMsg，无属性
刷新虚调），所以实现里直接加字段，**不能**走 `SetShengWan()`（那会多发一条
`RM_ABILITY`）。

新增 `GameSvr/Players/TPlayObject.NativeMirrorParam.cs`。原实现是空的
`MsgGetMarketOpen(true)`。225（native SINK）此前与它共用同一个空方法，现就地
内联为空处理。

### 4.3 202 BLOCKED — 缺字段 `[+0x180C]`

```
00653F69  7e68              jle 0x653FD3                  ; time > 0 分支
00653F6B  c6832918000003    mov byte [ebx+0x1829],3       ; 惩罚档位
00653F72  8bc3
00653F74  e84b040800        call 0x6D43C4                 ; 天数换算 helper
00653F79  83c007            add eax,7
00653F7C  2b45f8            sub eax,[ebp-8]               ; - time
00653F7F  89830c180000      mov [ebx+0x180c],eax          ; ← 到期天数
```

- `[+0x1829]` 有 C# 成员（`m_btNativeCheatPenaltyTier`，`TPlayObject.Base.cs:476`）。
- **`[+0x180C]` 全仓无对应成员**（grep 只命中注释）。
- `sub_6D43C4`（天数换算，前作记为「日期基址 `[+0x780]`」）未定名、未建模。

第三个 dword 的载体现在有了，但**落点字段没有**。补一个新字段还要连带补存档
（`HumData`）与到期扫描，都无字节证据支撑其 C# 形态 —— 按铁律不硬塞。

### 4.4 207 BLOCKED — 缺全局 40-bit 位图模型

```
0065811C  8b3538707d00  mov esi,[0x7D7038]
00658122  8b06          mov eax,[esi]        / 00658124 8945fb  mov [ebp-5],eax
00658127  8a4604        mov al,byte [esi+4]  / 0065812A 8845ff  mov [ebp-1],al
00658133  8906          mov [esi],eax        ; ← 新掩码 = 第三个 dword
00658135  8a45f4        mov al,byte [ebp-0xC]
00658138  884604        mov [esi+4],al       ; ← 第 5 字节
0065813F  3c27          cmp al,0x27 / 00658146 0fa345fb bt [ebp-5],eax  ; 旧位
00658155  0fa306        bt [esi],eax                                     ; 新位
0065815C  e8adffffff    call 0x658110                                    ; 逐位回调
```

三重障碍：

1. 位图是 **40 位**（`dword[esi]` + `byte[esi+4]`），C# 的 `nParam` 是 32 位，
   载不下。且第 5 字节的来源 `[ebp-0xC]` 在本 stub 路径下**是未初始化栈**
   （`0x657230` 只置 `edx`，`sub_658114` 只把 `edx` 存进 `[ebp-0x10]`），
   即原生这一路本身就写入垃圾字节。
2. 全局 `[0x7D7038]` 在 C# 无模型。
3. 逐位回调 `sub_658110` **是空函数**（`0x658110 C3` 单字节 `ret`）——
   顺带更正前作把它当作有效回调的表述。

另有既有约束：207 在本仓有**在用**的三字段发送方
（`CreditCardCommand.cs:31`，body 是纯数字 switchWord），加参数字段会破坏它。

### 4.5 214 BLOCKED — 缺全局 `[[0x7D6010]]` 模型

收发两侧都已完全解出（§1.6 的交叉验证），**唯一缺的是 C# 落点**。

`[0x7D6010] = 0x007D3A8C`（静态记录指针）。全镜像对 `0x7D6010` 只有 8 处引用：
4 处 setter 臂 + 1 处 GM 回显（`0x625655`）+ 3 处 214 的 handler 臂。

对目标字节 `[0x7D3A8C]` 的消费者有 2 个：

```
006D8CD9  A0 8C 3A 7D 00        mov al,byte [0x7D3A8C]
006D8CDE  88 82 29 18 00 00     mov [edx+0x1829],al     ; 播种玩家的惩罚档位
006CD426  A0 8C 3A 7D 00        mov al,byte [0x7D3A8C]
          8B 15 EC 6F 7D 00     mov edx,[0x7D6FEC]
          FF 34 82              push [edx+eax*4]        ; 按模式值索引名称表
```

即它是**全局「外挂惩罚策略档位」**，登录/自举时播种进每个玩家的 `[+0x1829]`，
并有一张显示名表 `[0x7D6FEC]`。C# 有**每玩家**的
`m_btNativeCheatPenaltyTier`（`NativeCheatSelfReport.cs` 已把
`0x6D8CD9` 那条播种线写成常量 `NativeCheatReportPolicyTier`），但**没有可写的
全局策略变量**，也没有名称表。

只为 214 造一个全局字节而无任何消费者 = 死代码，按铁律不做。
**这一条离解封最近**：只要主代理认可把 `NativeCheatReportPolicyTier` 从常量改成
可写的全局策略（并把 `0x6CD426` 的名称表一并建模），214 立刻可以接线，
传输载体这边已经就绪。

---

## 5. 210 的落地结果（顺手做掉的那条）

```
stub 0065724D  8B 4D 08  mov ecx,[ebp+8]     ; 长度（handler 不读）
     00657250  8B 55 0C  mov edx,[ebp+0xC]   ; body
     00657253  E8 A0 0D 00 00  call 0x657FF8
sub_657FF8
     00657FFE  8B DA           mov ebx,edx   ; 序言只有这一句 -> 不读 ecx
     00658013  E8 F0 D6 DA FF  call 0x405708 ; _LStrFromPChar -> 裸角色名
     0065801B  A1 04 71 7D 00  mov eax,[0x7D7104]
     00658020  8B 00           mov eax,[eax]
     00658022  E8 BD 9C FC FF  call 0x621CE4 ; Delete
```

`sub_621CE4(mgr, edx=name)`：

```
00621D14  call 0x40BCBC                    ; UpperCase
00621D32  call 0x405774 / 00621D3D call 0x40591C   ; 逐节点取名 + 比较
00621D44..00621D63                         ; 命中: 双向链表摘链
00621D66  ba20000000 / call 0x402FD0       ; FreeMem(node, 0x20)
00621D7D  call 0x652784                    ; GetPlayObject(name)
00621D88  c687990b000000  mov byte [player+0xB99],0   ; 在线才写
00621D8F  ff4e24          dec [mgr+0x24]
00621D94  e897080000      call 0x622630               ; Save
```

未命中则**什么都不做**（连 Save 都没有）。

落地：`MsgGetChatProhibitionCancel(Body)` → `NativeMirrorChatBan.Remove(Body)`
→ `M2Share.g_DenySayMsgList.TryRemove(name)`（临界区与
`ShutupReleaseCommand` 一致）。原实现是**完全空的函数体**。

`0x40BCBC` 本次确认是 `UpperCase`（`0x40BCFA cmp byte [ebp-9],0x41` 起的
经典 Delphi 大写循环）。

---

## 6. 对 227 / 228 的影响面评估

**本方案对这两条零影响**：`CarriesNativeParam` 不含 227/228，它们的
`Body` 未被触碰，`case` 分支一字未改，发送侧（不存在）更未触碰。

同时报告一个与前作**相反**的事实，供主代理决策：

> 前作称「227/228 的 C# 实现都不是空壳而是在用功能，且删掉会破坏现有发送侧」。
> **全仓不存在 227 / 228 的发送方。** 依据是 §2.3 第 3 点那张穷举表 —— 对全部
> `SendServerGroupMsg(` 调用点（含 `SS_*` 别名与跨行写法）逐个解析实参后，
> 发出的 ident 集合里没有 227 也没有 228；对
> `ISM_PLAYER_NOTICE` / `ISM_RELOADMAKEITEMLIST`（227）与
> `ISM_MENTOR_RECHARGE_REWARD` / `ISM_GUILDMEMBER_RECALL`（228）的按名检索也只
> 命中 `Grobal2.cs` 的常量声明与 `MirrorMessage.cs` 的 `case` 本身。
> 也就是说这两个接收分支目前**不可达**，替换它们不会打断任何在用功能。
>
> 前作那句判断应当是把「`MsgGetReloadMakeItemList` / `MsgGetGuildMemberRecall`
> 的函数体不是空的」误当成「有发送方在用」了 —— 函数体确实有内容，但没有任何
> 东西能触发它们。

228 的原生语义本次已完全解出，若主代理放行可直接落地：

```
sub_657BCC(ecx=第三个 dword, edx=body)
00657C03  call 0x4C6AEC (cl=0x2F)          ; body 按 '/' 拆首段/余段
00657C08  83 7D FC 00 / 74 6C              ; 首段非空
00657C0E  83 7D F8 00 / 74 66              ; 余段非空
00657C14  81 FE E8 03 00 00 / 7C 5E        ; 第三个 dword >= 1000
00657C26  call 0x652784                    ; GetPlayObject(首段)
00657C3D  call 0x6C03F8(eax=玩家, edx=n, cl=0, 栈 0/1/0)   ; 加经验([+0x2BC])
00657C42..00657C64  _LStrCatN(4)
00657C6C  66 B9 FF FC / 00657C74 FF 93 D4 00 00 00        ; SysMsg cx=0xFCFF
```

文案 = `恭喜，您曾经的徒弟`(`0x657CAC` len 0x12) + 余段 +
`实力又进一步，“比奇国王”特赠您经验值`(`0x657CC8` len 0x26) + `IntToStr(n)`。
即「徒弟充值/成长 → 给曾经的师父发经验」，与 C# 现有的「行会成员召回 + 空间
传送」完全无关。落地前还需给 `sub_6C03F8`（经验加账，含 `[+0x2BC]` 溢出钳位）
定名并对上 C# 的加经验入口 —— 这一步本次未做。

227（`sub_657670`）= 给指定玩家发文本通知（同 221 去掉 GM 门），C# 现实现是
`LocalDB.LoadMakeItem()`，语义无关但同样不可达。

**按任务书要求，两条均未改动，交主代理定夺。**

---

## 7. 构建

`dotnet build GameSvr/GameSvr.csproj` → **0 错误 / 15 警告**，与基线一致
（无新增）。

## 8. 本次改动

| commit | 内容 |
|---|---|
| `153910ee` | `tools/sgrp3_{scan,win,pre}.py` —— 字节模式 / 双模式窗口 / 前文反汇编扫描器 |
| `f0719cdf` | 传输层第三参 + 209 / 210 / 224 落地；225 就地内联 |

新增文件：
- `GameSvr/Services/NativeMirrorChatBan.cs`（209 / 210）
- `GameSvr/Players/TPlayObject.NativeMirrorParam.cs`（224，partial class）

修改文件：`GameSvr/Snaps/MirrorMessage.cs`（未触碰任何禁改文件）。
