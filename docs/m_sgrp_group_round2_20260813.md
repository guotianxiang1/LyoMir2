# 组队域第二轮：两条真红定性 + 下线清槽定案

日期：2026-08-13　工作树 `D:\loym2\.claude\wt2\m-sgrp`　分支 `w/m-sgrp`
镜像 `D:\loym2\staging\_reunpack_work\flat_image.bin`　ImageBase `0x400000`
**未执行 `dotnet build` 或任何审计工具**（编译锁由主代理持有）。
复现脚本全部在 `tools/sgrp_re/`（q1..q15，含 `.txt` 输出）。

---

## 判定计数

| 判定 | 数量 |
|---|---|
| 审计钉错（工具断言非原生契约，已按字节改正） | 2 |
| DIVERGENT（已修） | 3 |
| INVENTED（已删） | 2 |
| MISSING（未修，给了落地方案） | 1 |
| BLOCKED（本轮解掉） | 1 |
| BLOCKED（本轮新记） | 0 |

---

## 一、`InProcSocialRunCheck` FAIL —— **审计钉错**

失败断言：`real ClientCreateGroup formed a 2-member group with m1.m_GroupOwner=leader`

### 1.1 原生 1020 根本不建组

`sub_6C341C`（CM 1020）穷举 E8 被调用者（`tools/sgrp_re/q1_group.txt`）：

```
0x405500 0x4059C0 0x40C140 0x652784 0x6C3380 0x6C33CC 0x6F39B4
```

`sub_6C34EC`（CM 1021）：

```
0x405500 0x4059C0 0x40C140 0x652784 0x6B7BAC 0x6BBE84 0x6C33CC 0x6F39B4
```

两个集合都**不含** `sub_726B80`（TGroup.Create）、`sub_7272EC`（插入成员）、
`sub_6C3648`（accept 建组）。唯一状态变更：

```
0x6C348A  E8 25 05 03 00   call 0x6F39B4      ; 1020，目标在 eax（6C3488 mov eax,esi）
0x6C3572  E8 3D 04 03 00   call 0x6F39B4      ; 1021
```

`sub_6F39B4` 只是把一条 `{requester, type, tick}` 12 字节记录挂进 `[target+0xA78]`
（`0x6F3A59 mov eax,0xC / call 0x402FA0` 分配、`0x6F3A80 call 0x424AB8` 入表），
上限 10（`0x6F3A3A cmp [eax+8],0xA / jl`），并下发 `0x6F3AA8 mov dx,0x113C` = SM 4412 通知。

### 1.2 `m_GroupOwner` 的赋值时机和指向（逐字节）

组只从 CM 4412 的**同意**分支产生。`sub_6F3EA8`：

```
0x6F3ED6  call 0x652784                 ; edi := GetPlayObject(包体名) = 邀请人
0x6F3F11  80 EB 01  sub bl,1 / jb       ; 回复 type 0
0x6F3F21  83 BF 80 0A 00 00 00  cmp [edi+0xA80],0
0x6F3F28  75 0B                 jne 0x6F3F35      ; 邀请人已有队 -> sub_6C3838 加入
0x6F3F2A  8B D6 / 8B C7         edx=esi(同意者), eax=edi(邀请人)
0x6F3F2E  E8 15 F7 FC FF        call 0x6C3648
```

`sub_6C3648`（`eax`=邀请人存进 `[ebp-4]`，`edx`=同意者存进 `ebx`）：

```
0x6C36A1  8B 4D FC              mov ecx,[ebp-4]        ; ecx = 邀请人 = owner 实参
0x6C36A4  B2 01                 mov dl,1
0x6C36A6  A1 B8 6A 72 00        mov eax,[0x726AB8]     ; TGroup 类
0x6C36AB  E8 D0 34 06 00        call 0x726B80          ; TGroup.Create
0x6C36B0  8B F0                 mov esi,eax
0x6C36B2  8B 45 FC              mov eax,[ebp-4]
0x6C36B5  89 B0 80 0A 00 00     mov [eax+0xA80],esi    ; ← 邀请人先挂上组
0x6C36BB  8B C6 / 8B D3         eax=组, edx=同意者
0x6C36BF  E8 28 3C 06 00        call 0x7272EC          ; 插入同意者
0x6C36C4  84 C0 / 0F 84 92..    test al,al / je 失败臂
0x6C36D4  C6 80 A1 0B 00 00 01  mov byte [eax+0xBA1],1 ; 邀请人 m_boAllowGroup := 1
0x6C36E5  66 BA 94 02           mov dx,0x294           ; SM 660 → 邀请人（6C36E9 eax=[ebp-4]）
```

`TGroup.Create sub_726B80`（`ecx` = owner）：

```
0x726BA3  89 5B 38              mov [ebx+0x38],ebx     ; group.Handle := group
0x726BA6  89 7B 3C              mov [ebx+0x3C],edi     ; group.Owner  := 邀请人
0x726BA9  C7 43 44 01 00 00 00  mov [ebx+0x44],1       ; 成员数 := 1
0x726BB3  89 87 7C 0A 00 00     mov [edi+0xA7C],eax    ; owner 记住 group.Handle
0x726BC7..0x726BDB             11 个槽 call 0x728404
0x726BE2  E8 31 19 00 00        call 0x728518          ; 槽 0 := owner
```

`AddMember sub_7272EC`（`eax`=组，`edx`=新成员）：

```
0x72732A  83 7B 44 0B / 0F 8D   cmp [ebx+0x44],0xB / jge 失败
0x727359  E8 BA 11 00 00        call 0x728518          ; 找到的空槽 := 成员
0x72735E  FF 43 44              inc [ebx+0x44]
0x72739A  89 98 80 0A 00 00     mov [eax+0xA80],ebx    ; ← 成员挂上同一个组
0x7273A3  89 90 7C 0A 00 00     mov [eax+0xA7C],edx    ; edx = group.Handle
```

**结论：终态断言本身是真契约**——组建成后两边的 `[+0xA80]` 都指向同一个组，
而 `group+0x3C` 是邀请人，即 C# 的 `m_GroupOwner == leader`。
**错的是入口**：断言挂在 CM 1020 上，而 1020 只排队。

### 1.3 定性与修法

**审计钉错。** 而且是仓库内自相矛盾：同一个 `AuditTools/` 里的
`NativeGroupConsentCorpsChatCheck` 早就按同一批字节钉住了「1020/1021 只排队」，
两个工具此前互斥，`InProcSocialRunCheck` 钉的是 C# 修复**前**的强制入队行为。

**没有弱化任何断言。** 改法是让 harness 跑完整链路：

- `miCreateGroup.Invoke` 之后**新增**两条断言：不许建组、必须把 type-0 请求排在**目标**身上
- 再用真 `Operate(NativeMessage(CM_REPLY_GROUP_MESSAGE, 1, 0, leader))` 走 4412 同意
- 终态断言（2 人组 + `m1.m_GroupOwner == leader`）**原文保留**
- 1021 同样拆成「排队 → 同意」两步，同样加中间断言

净效果：断言数从 2 条增到 6 条。

文件：`AuditTools/InProcSocialRunCheck/Program.cs`（提交 `ba5683df`）。

---

## 二、`PasGroupSetVCompatCheck` FAIL —— **审计钉错 + 三处真缺陷**

失败断言：`player-function leader zero removal unexpectedly changed the V variable`

### 2.1 先定位原生 `GroupSetV` 到底是什么

不是组队侧的问题，也不是「V 银行写入端擦除」的残留——**是脚本 API 侧**。

注册点（`tools/sgrp_re/q3_groupsetv.txt`，`0x732A98` 这个 dword 全镜像只有 1 处引用）：

```
0x7318AF  BA 30 08 6E 00   mov edx,0x6E0830    ; handler
0x7318B4  B9 98 2A 73 00   mov ecx,0x732A98    ; 名字块 "GroupSetV"
0x7318B9  8B C3            mov eax,ebx         ; TPlayer PAS 面
0x7318BB  E8 C0 28 DC FF   call 0x4F4180
```

声明文本 `0x72DA45`：
`function GroupSetV(const nTaskNo, nFieldNo, nValue: integer): Boolean;`
（前导 `FF FF FF FF 46 00 00 00` = AnsiString 常量头，长度 0x46）

对照 `SetV` 的注册 `0x73153B mov edx,0x6DF288 / mov ecx,0x73260C`——同一张表，
所以 `sub_6E0830` 是 `GroupSetV` 的 handler，可确证。

### 2.2 handler 逐字节

```
0x6E0830  55 8B EC 53 56
0x6E0835  33 DB                  xor ebx,ebx            ; result := False
0x6E0837  8B B0 80 0A 00 00      mov esi,[eax+0xA80]    ; caller 的 TGroup
0x6E083D  85 F6                  test esi,esi
0x6E083F  74 0D                  je 0x6E084E            ; 无队 → 直接出口，返回 False
0x6E0841  8B 5D 08               mov ebx,[ebp+8]        ; nValue
0x6E0844  53                     push ebx
0x6E0845  8B C6                  mov eax,esi
0x6E0847  E8 08 6F 04 00         call 0x727754          ; edx/ecx 原样透传 = group/index
0x6E084C  8B D8                  mov ebx,eax
0x6E084E  8B C3                  mov eax,ebx
0x6E0853  C2 04 00               ret 4
```

`TGroup` 侧 `sub_727754`：

```
0x72775C  89 4D F4               mov [ebp-0xC],ecx      ; nFieldNo
0x72775F  89 55 F8               mov [ebp-8],edx        ; nTaskNo
0x727765  C6 45 F3 01            mov byte [ebp-0xD],1   ; result 预置 True
0x72776C  8B 58 44               mov ebx,[eax+0x44]     ; 循环上界 = 成员数
0x72776F  4B / 85 DB / 7C 35     dec ebx / test / jl    ; 空组直接到出口（仍返回 True）
0x72777A  8B 44 B0 48            mov eax,[eax+esi*4+0x48]  ; slot[i]
0x72777E  8B 40 10               mov eax,[eax+0x10]     ; slot.Player
0x727787  E8 C0 D0 CD FF         call 0x40484C          ; as TPlayObject
0x72778C  85 C0 / 74 15          test eax,eax / je      ; 空槽跳过
0x727790  80 78 73 00            cmp byte [eax+0x73],0
0x727794  75 0F                  jne 0x7277A5           ; ghost 跳过
0x7277A0  E8 E3 7A FB FF         call 0x6DF288          ; 逐成员 SetV
0x7277A9  8A 45 F3               mov al,[ebp-0xD]       ; 预置从不清零
```

### 2.3 零值：`sub_6E4140` 对「值」一个分支都没有

`SetV sub_6DF288` 把 `{Key,Value}` 放进两个栈槽再调 upsert：

```
0x6DF2C1  E8 06 50 00 00   call 0x6E42CC   ; Key = group*1000 + index
                                           ; (6E42CC 69 C2 E8 03 00 00 imul eax,edx,0x3E8 / 03 C1)
0x6DF2C6  89 45 F8         mov [ebp-8],eax   ; Key
0x6DF2CC  89 45 FC         mov [ebp-4],eax   ; Value  ← 直接来自 [ebp+8]
0x6DF2CF  8D 93 08 08 00 00 lea edx,[ebx+0x808]  ; V 银行（S 是 +0x804）
0x6DF2DA  E8 61 4E 00 00   call 0x6E4140
```

`sub_6E4140` 全函数对 `[ebp-4]`（Value）**只有存储，没有任何 test/cmp**；
所有比较都是对 `[ebp-8]`（Key）：

| 路径 | 写点 |
|---|---|
| 空数组首插 | `0x6E4182 mov [eax],edx` / `0x6E4187 mov [eax+4],edx` |
| 二分命中键 | `0x6E41C2 89 54 D8 04  mov [eax+ebx*8+4],edx` |
| 未命中，插在右侧 | `0x6E422A` / `0x6E4231` |
| 未命中，插在左侧 | `0x6E425A` / `0x6E4260` |

**所以 `SetV(group,index,0)` 原样落成 `{key, 0}`，永不删除。**
工具此前断言「写 0 会移除该键」，钉的正是 §4.19（QST-30）记录的 C# 自造擦除。
C# `TPlayObject.SetScriptVar`（`TPlayObject.Base.cs:308`）现在是对的，工具是错的。

### 2.4 顺带查出的三处真缺陷（都在脚本 API 侧）

| # | 判定 | 原生 | 修前 C# | 玩家可见后果 |
|---|---|---|---|---|
| 1 | **INVENTED** | `0x6E083F je` → 无队写零字节、返回 False | `if (members == null \|\| members.Count == 0) return SetPlayerVar(self,...)` 单人回退 | 单人玩家调 `GroupSetV` 会给自己写变量，原版不会。任务脚本用它做「全队打标记」时，单人也能拿到本该组队才有的标记 |
| 2 | **MISSING** | `0x727790 cmp [eax+0x73],0 / jne` 跳过 ghost | 无 ghost 门 | 已下线（ghost）成员被写入 V 变量并会随存档落盘 |
| 3 | **DIVERGENT** | 有队恒 True、无队恒 False | 三个 case 站点各自造 Boolean（`args.Count>=3` / `CurrentPlayer!=null`），从不反映真实结果 | 脚本 `if GroupSetV(...) then` 的分支永远走 True，单人时也走 True |

### 2.5 修了什么

`GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs`：

- `SetGroupPlayerVar`：删单人回退、加 ghost 门、返回值改为「有队 = true」
- 三个 `case "groupsetv"` 站点透传真实返回值

`AuditTools/PasGroupSetVCompatCheck/Program.cs`：按字节改正断言，并**新增**
ghost 跳过、无队返回 False、无队零写入三组断言。断言数 12 → 17。

提交 `49567a9a`（含 `AUDIT_INVENTORY.md` 的 PASS 文案同步）。

---

## 三、BLOCKED「下线清槽」—— 解掉，定为 DIVERGENT + MISSING

上一轮记的是：「`726E68` 不含 logout，C# `Disappear` 仍 `DelMember`，没有析构/ghost
清槽的完整链，不敢删」。本轮把链追完了。

### 3.1 原生：下线**不**摘槽（三条独立证据）

**① `[player+0xA80]` 全镜像写点普查**（`tools/sgrp_re/q7_a80.txt`，69 条指令里写形式 5 条）：

| VA | 指令 | 归属 |
|---|---|---|
| `0x6B9EE7` | `89 83 80 0A 00 00 mov [ebx+0xA80],eax` | **登录**重挂（见 ③） |
| `0x6C3278` | `89 83 80 0A 00 00 mov [ebx+0xA80],eax`（eax=0） | `sub_6C3200` 主动离队 |
| `0x6C331D` | `89 83 80 0A 00 00 mov [ebx+0xA80],eax` | 建组（`0x6C32D0` 分支） |
| `0x6C36B5` | `89 B0 80 0A 00 00 mov [eax+0xA80],esi` | accept 建组 |
| `0x72739A` | `89 98 80 0A 00 00 mov [eax+0xA80],ebx` | AddMember |

唯一清零点 `sub_6C3200` 的 3 个 E8 调用者：`0x6B3C26`（BLACKROOM 图 tick）、
`0x726F64` / `0x72716E`（都在 `TGroup.DelMember` 内部）。**没有一个是下线/析构。**

`TGroup.DelMember sub_726E68` 的 E8 调用者仍然只有 2 个：`0x6C3181`、`0x6C3D73`。

**② 槽写入口 `sub_728518` 只有 3 个 E8 调用者**：`0x726BE2`（ctor 槽 0）、
`0x727359`（AddMember）、`0x7280F9`（登录重绑，见 ③）。
清槽 `sub_7284E8` 只有 3 个：`0x726F4D`、`0x72717E`（都在 DelMember 内）、
`0x72841B`（槽构造）。**下线路径一个都没有。**

**③ 决定性证据：原生存在「下线保槽 → 登录重挂」机制**

槽的布局（从 `sub_7284E8` 反推空槽形态）：

```
0x7284EE  C7 43 08 FF FF FF FF   mov [ebx+8],-1     ┐ 64 位角色 id
0x7284F5  C7 43 0C FF FF FF FF   mov [ebx+0xC],-1   ┘（= [player+0x588]/[+0x58C]）
0x7284FE  89 43 10               mov [ebx+0x10],eax(0)   ; 对象指针
0x728501  lea eax,[ebx+0x14] / mov edx,0x34 / call 0x403B2C  ; 名字等 0x34 字节
```

即**槽的身份是持久的 64 位角色 id，对象指针只是瞬态缓存**。

登录两步重挂：

```
第一步  0x6B9ED5  8B 93 7C 0A 00 00  mov edx,[ebx+0xA7C]   ; 记住的 group.Handle
        0x6B9EC7  lea edx,[ebx+0x106]                       ; 自己的名字 → ecx
        0x6B9EE2  E8 E1 E3 06 00     call 0x7282C8
        0x6B9EE7  89 83 80 0A 00 00  mov [ebx+0xA80],eax    ; 恢复组指针
```

`sub_7282C8`（唯一调用者就是上面这处）遍历组管理器列表，
`0x728318 mov eax,[eax+0x38] / 0x72831B cmp eax,[ebp-4]` 按 Handle 匹配，
再 `0x72832D call 0x72792C` 按**名字**确认自己还在花名册里（`sub_72792C` 扫 11 个槽，
`0x72795F call 0x72843C` 取槽名、`0x72798C call 0x40591C` 比字符串）。

```
第二步  0x6B24FC  call 0x6F5168        ; 登录爆发序列里的一步
        0x6F516E  mov eax,[ebx+0xA80] / test / je
        0x6F517A  E8 41 2F 03 00       call 0x7280C0
        0x6F517F  C6 83 A1 0B 00 00 01 mov byte [ebx+0xBA1],1   ; m_boAllowGroup := 1
        0x6F5190  66 BA 93 02          mov dx,0x293             ; SM 659
```

`sub_7280C0` 扫 11 个槽，按 **64 位角色 id** 匹配
（`0x7280D4/0x7280D7` 取槽的 `[+8]/[+0xC]`，`0x7280DC/0x7280E2` 取 `[player+0x588]/[+0x58C]`，
`0x7280E8/0x7280EE` 两个 dword 都比），命中就
`0x7280F9 call 0x728518` **把新对象指针写回槽**，再 `0x728100/0x728111` 重发 SM 667。

**这套重挂机制只有在「下线不摘槽」的前提下才有意义。** 反过来说，它的存在本身
就是「原生下线保留成员」的正面证明，不只是「找不到摘槽代码」的反面推断。

**④ 花名册包不排除离线成员**：SM 667 构造器 `sub_7271D0`

```
0x727202  83 79 0C 00 / 75 08     cmp [ecx+0xC],0    ┐ 槽占用判据 = 64 位 id
0x727208  83 79 08 00 / 76 6A     cmp [ecx+8],0      ┘ 不是对象指针
0x727212  8B 71 10                mov esi,[ecx+0x10]
0x727215  85 F6 / 74 1C           test esi,esi / je 0x727235
0x72723E  C6 84 45 D2 FD FF FF 00 mov byte [...],0   ; 指针为空 → 「是队长」字节写 0
0x727246  ...                     ; **记录照样拷进包体**
0x727282  6B 45 F4 36             imul eax,[ebp-0xC],0x36   ; 54 字节/条
```

发送轮 `0x72728B..0x7272E1` 才按 `0x727295 test esi,esi / je` 跳过离线成员。
即：**离线成员出现在包里，只是收不到包**。

### 3.2 C# 现状与定性

| 项 | 判定 | C# 位置 | 说明 |
|---|---|---|---|
| `Disappear` → `m_GroupOwner.DelMember(this)` | **DIVERGENT** | `TPlayObject.Message.cs:3032` | 原生下线不摘槽 |
| 登录重挂（`sub_7282C8` + `sub_6F5168`） | **MISSING** | `TPlayObject.Base.cs:1417` 之后 | 见下 |
| 30 秒 tick 扫 dead/ghost 成员 | **INVENTED（本轮已删）** | `TBaseObject.Base.cs:587-605` | 见 §4 |

C# 登录爆发序列（`TPlayObject.Base.cs:1398-1417`）已经逐条对齐了
`0x6B24D9`(4500) / `0x6B24E0`(4613) / `0x6B24E7`(4615) / `0x6B24EE`(4612) / `0x6B24F5`(4628)，
**紧接着的 `0x6B24FC call 0x6F5168` 没有 C# 对应体**——缺口位置精确到一行。

### 3.3 为什么本轮**不**落地这两条

C# 的 `m_GroupMembers` 是 `IList<TPlayObject>`，条目就是活对象，没有持久 id。
只删 `Disappear` 里的 `DelMember` 而不补重挂，结果是：队长名单里永久挂一个死对象
（既泄漏引用，又在 SM 667 里显示一个永不回来的幽灵成员），**比现状更糟**。
两半必须一起上。

**最小落地方案**（留给下一轮或主代理裁决）：

1. 把 `m_GroupMembers` 的条目从裸 `TPlayObject` 换成 `{ long CharId, string Name, TPlayObject Live }`
   ——`CharId` 对应 `[player+0x588]/[+0x58C]`，C# 已有 `GetCachedNativeUserId()`；
   `Name` 对应槽 `+0x14` 的 15 字符拷贝。空槽 = `CharId == -1`。
2. `Disappear` 只置 `Live = null`，不移除条目（对应原生「指针悬空、id 保留」）。
3. 在 `TPlayObject.Base.cs:1417` `SendNativeSocialRoleRefresh()` 之后补
   `RebindNativeGroupSlot()`：按 `CharId` 找回条目 → 写 `Live` → `m_boAllowGroup = true`
   → 发 SM 659 → `RefreshNativeGroupWire()`。这是 `sub_6F5168` 的逐行对应。
4. `BuildNativeGroupMemberRecord` 对 `Live == null` 的条目仍出记录、「是队长」字节写 0
   （`0x72723E`）；`BroadcastNativeGroupMembers` 只发给 `Live != null`（`0x727295`）。
5. 组对象本身在 C# 里没有实体（`m_GroupOwner` 兼任），所以 `[+0xA7C]` 的 Handle 语义
   可以用队长引用代替，`sub_7282C8` 那一步在 C# 里退化为「队长仍在线且名单里有我」。

---

## 四、顺带查出并已修：第三处自造的组队拆解

`TBaseObject.Base.cs` 的 30 秒 verify 块里有：

```csharp
if (m_GroupOwner != null && (m_GroupOwner.m_boDeath || m_GroupOwner.m_boGhost))
    m_GroupOwner = null;
if (m_GroupOwner == this)
    for (...) if (BaseObject.m_boDeath || BaseObject.m_boGhost) m_GroupMembers.RemoveAt(i);
```

原生没有这道扫描。除了 §3.1① 的写点普查，还有一条直接对照：**同一个 30 秒块的
原生对应体**（`tools/sgrp_re/q15_30s.txt`）

```
0x6B3B54  81 FA 30 75 00 00   cmp edx,0x7530        ; 30000 ms，tick 字段 [self+0x73C]
0x6B3B73  E8 30 F2 0B 00      call 0x772DA8         ; IsDead
0x6B3B7C  80 7B 73 00 / 74 0B cmp [ebx+0x73],0
0x6B3B87  89 90 AC 0B 00 00   mov [eax+0xBAC],0     ; 只清这一个对端指针
0x6B3BB2..0x6B3BC6                                   ; 另一个：只清 [self+0x18A8]
```

两处都是「单个对端指针」的清理惯用法，**都没碰 `[self+0xA80]`，也没碰槽数组 `[group+0x48]`**。

这道扫描一直在**抵消上一轮「死亡不退组」那条修复**——死者 30 秒内照样掉队，
所以上一轮的修复在线上其实观察不到。已删除（提交 `1325b7e6`）。
`m_DealCreat` 的 `ghost||death` 清理保持原样（`sub_6B3EAC`，被 `InProcEngineRunCheck` 钉住）。

---

## 五、新记的 INVENTED 候选（未删，交主代理裁决）

**`GroupSetS` 全镜像 0 命中。**

| 来源 | 命中 |
|---|---|
| `flat_image.bin` ASCII 精确 | 0 |
| 同上 GBK | 0 |
| 同上 UTF-16LE | 0 |
| 同上 ASCII 大小写不敏感 | 0 |
| 眼神 `AllFuc_208_DECRYPTED.txt`（搜 `GroupSet` 全词） | 0 |
| 生产脚本 `D:\光头卧龙\mud2.0\Mir200`（搜 `GroupSet`） | 0 |

对照：`GroupSetV` 在镜像里有 2 处（`0x72DA49` 声明文本、`0x732A98` 注册表名字块），
同一张注册表里 `GroupSetV` 的邻居是 `GroupCallOut`(`0x732A80`) 和 `PsConsumeYb`(`0x732AAC`)
——**这张表里没有 `GroupSetS` 的位置**。

而且 C# 的实现形状也不对：三个 `case "groupsets"` 都调 `SetPlayerVar`（只写自己），
根本不是组操作。

未删的原因：删它要同步改 `PasInterpreter.cs:55` 的函数名表、
`AuditTools/PasScriptAudit/Program.cs:1721` 的白名单和 3 个 bridge case，
这三处都在 npcscript 域，怕与并行代理冲突。**触点已列全，可直接落地。**

---

## 六、前人/工具结论订正

1. **`InProcSocialRunCheck` 与 `NativeGroupConsentCorpsChatCheck` 此前互相矛盾**，
   同一个 `AuditTools/` 里一个钉「1020 建组」、一个钉「1020 只排队」。字节支持后者。
   凡是引用前者 TEAM 段结论的材料都作废。
2. **「V 银行零值移除」不是组队侧问题，也不是写入端残留**——是 `PasGroupSetVCompatCheck`
   这个工具本身钉了 C# 自造的擦除语义。`SetScriptVar` 早已改对。
3. **`GroupSetV` 无队时不回退到 `SetV`**。C# 的 solo fallback 从基线提交
   `d5d00744` 就在，从来没有字节支撑。
4. **上一轮「死亡不退组」的修复此前被 30 秒扫描抵消**，等于没生效。这类「同一语义分散在
   多个执行点」的情况，只改一处不够——和 §4.19 三条路径的教训同构。
5. **`[player+0xA7C]` 不是「队长镜像」**（上一轮我这么写过），它存的是
   `group+0x38` = **组自身的 Handle**（`0x726BA3 mov [ebx+0x38],ebx`），
   用途是登录时给 `sub_7282C8` 做身份匹配。已订正。

---

## 七、建议优先级

| 序 | 项 | 理由 |
|---|---|---|
| 1 | 本轮三个提交先过编译 | 两条真红转绿的前提 |
| 2 | 下线保槽 + 登录重挂（§3.3 五步） | 玩家每次掉线重连就掉队，是最高频的可见缺陷；且缺口位置已精确到一行 |
| 3 | `GroupSetS` 裁决（§5） | 面小，但要跨域协调 |

---

## 八、本轮提交

| SHA | 内容 |
|---|---|
| `49567a9a` | `GroupSetV`：删单人回退、加 ghost 门、保留零值；工具断言按字节改正并加钉 |
| `ba5683df` | `InProcSocialRunCheck`：驱动完整两步建组链，终态断言原样保留 |
| `1325b7e6` | 删除 30 秒 dead/ghost 组队扫描（原生无对应） |
