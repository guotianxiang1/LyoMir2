# 社交三块对账（组队 / 好友 / 行会协议）

日期：2026-08-13  
工作树：`D:\loym2\.claude\wt2\m-sgrp`  分支 `w/m-sgrp`  
镜像：`D:\loym2\staging\_reunpack_work\flat_image.bin`  ImageBase `0x400000`  
未执行 `dotnet build`。

---

## 判定计数

| 判定 | 数量 |
|---|---|
| FAITHFUL | 18 |
| DIVERGENT（已修） | 4 |
| DIVERGENT（未改，已记录） | 2 |
| MISSING | 2 |
| INVENTED（已删） | 1 |
| BLOCKED | 3 |

---

## 1. 组队

### 1.1 协议对照

| 动作 | ident | 原生 VA + 字节 | C# | 判定 |
|---|---|---|---|---|
| 允许组队开关 | CM 1019 / SM 659 | `0x6D9061 66 BA 93 02` SM 659；关组 `0x6C3140` | `TPlayObject.Message.cs` CM_GROUPMODE | FAITHFUL |
| 建队邀请 | CM 1020 | `0x6C341C` → 只 `call 0x6F39B4` 排队，不建队。失败 `0x6C34B2 66 BA 95 02` SM 661 | `ClientCreateGroup` | FAITHFUL |
| 邀请入队 | CM 1021 | `0x6C34EC` 同样只排队。满员 `0x6C3534 83 78 44 0B / 7D` → -5。失败 `0x6C35AC 66 BA 98 02` SM 664 | `ClientAddGroupMember` | FAITHFUL |
| 同意后建队 | SM 660 | `0x6C36E5 66 BA 94 02` 只在 accept 路径 | `CreateNativeGroup` | FAITHFUL |
| 删人/自退 | CM 1022 | `0x6C3CF0`：队长 **或** 名字==自己 → 允许。`0x6C3D84 66 BA 97 02` SM 663；失败 `0x6C3DA9 66 BA 99 02` SM 665 | `ClientDelGroupMember` | FAITHFUL |
| 解散回执 | SM 666 | `0x6B547D 66 BA 9A 02` 后清 `[+0xBA1]` 再发 SM 659 | `RM_GROUPCANCEL` | FAITHFUL |
| 成员列表 | SM 667 | `0x7272CF 66 BA 9B 02`，54 字节记录，`imul eax,n,0x36` | `BroadcastNativeGroupMembers` | 修前 DIVERGENT（斜杠拼名），已改为 54 字节 |
| 入队申请 | CM 4413 | `0x6F430D` / `0x6C39A3` | `HandleNativeJoinGroup` | FAITHFUL |
| 回复邀请 | CM 4412 | `0x6F3EA8` | `HandleNativeGroupReply` | FAITHFUL |
| 成员上限 | 11 | 槽数组 `cmp esi,0xB` @ `0x726CC1 83 FE 0B`；加人 `cmp [group+0x44],0xB` @ `0x6C3534` | `NativeGroupMaxMembers=11` / `GROUPMAX=11` | FAITHFUL |
| 跨地图 | 同图才进经验池 | `0x726C8D 8B 87 28 01 00 00 / 3B 83 28 01 00 00 / 75` | `m_PEnvir == member.m_PEnvir` | FAITHFUL |
| 死亡 | **不退组** | `726E68` 仅 2 个 E8：`0x6C3181`、`0x6C3D73` | 已删死亡 `DelMember`；**第二轮又删了 30 秒 tick 扫描**（`TBaseObject.Base.cs` 原 587-605），它一直在抵消本条 | 原 INVENTED，已修（两处） |
| 下线 | 见 BLOCKED | 上述两处不含 logout | `Disappear` 仍 `DelMember` | BLOCKED |
| 队长转移 | `0x727FB0` | `0x727FE2..0x72800B` 找下一槽；串 `0x7280AC` ShortString len=16「 提升为小队队长!」 | `DelMember` 已按此转移 | 原 DIVERGENT，已修 |

队长自关组被拒：`0x6C318F 66 B9 FF 38 / BA D8 31 6C 00` → 串 `0x6C31D8`「如果你想退出，使用编组功能（删除按钮）」。C# `ClientGroupClose` 已对齐。

### 1.2 经验分配（逐条 VA）

函数 `sub_726C3C`（`EAX=group, EDX=self, ECX=dwExp`），由 `0x6F7A02 E8 35 F2 02 00 call 0x726C3C` 唯一调用。

**收集轮** `0x726C72..0x726CC4`（定长 11 槽）：

| 门 | VA + 字节 | 语义 |
|---|---|---|
| 空槽 | `726C7E 85 DB / 74 3E` | `[slot+0x10]==0` 跳过 |
| 死亡 | `726C84 E8 1F C1 04 00 call 0x772DA8`（`mov al,[eax+0x74]; ret`）`726C8B 75 33` | 只测 IsDead，不测 ghost |
| 同图 | `726C8D 8B 87 28 01 00 00 / 726C93 3B 83 28 01 00 00 / 726C99 75 25` | `[+0x128]` |
| 距离 | `726C9B 66 B9 0C 00` + `726CA3 E8 38 D7 04 00 call 0x7743E0` | `|dX|<=12 && |dY|<=12` |

回落单人：`726CC6 83 7D F0 00 / 0F 8E` sumlv<=0；`726CD0 83 7D EC 01 / 0F 8E` n<=1；`726CDA 83 7D EC 0B / 0F 8F` n>11。

**池加成** `0x726CE4..0x726CF9`：

```
726CE7  8B 04 85 50 3E 7D 00   mov eax, [eax*4+0x7D3E50]   ; bonusX10[n]
726CEE  F7 6D F8               imul [ebp-8]                 ; × dwExp
726CF6  99                     cdq                          ; 丢高 32 位
726CF7  F7 F9                  idiv ecx                     ; /10
```

表 `@0x7D3E50` 实测 dword：`{600,10,12,13,14,15,16,17,18,19,20,21}`（idx12=50 属邻表，不可达）。

**份额** `0x726D35..0x726D46`：

```
726D3E  F7 6D E8   imul [ebp-0x18]     ; memberLvl × expB
726D41  99         cdq
726D42  F7 7D F0   idiv [ebp-0x10]     ; / sumlv
726D45  40         inc eax             ; +1
```

即 `share = (int)(lvl * expB) / sumlv + 1`（32 位截断，先乘后除）。

**等级差** `0x726D57 E8 C8 13 00 00 call 0x728124`：

```
728138  8D 46 0A   lea eax,[esi+0xA]   ; selfLvl+10
72813B  3B F8      cmp edi,eax
72813D  7D 04      jge 惩罚            ; otherLvl >= selfLvl+10
728149  D8 35 80 81 72 00  fdiv [0x728180]  ; float 15.0 = 00 00 70 41
72815C  E8 13 B4 CD FF     call 0x403574    ; Round (banker's)
72816B  B8 01 00 00 00     下限 1
```

`share = share - Round(share/15.0*(otherLvl-(selfLvl+10)))`，否则不动。

**夹取** `726D62 3B 45 F8 / 7E 06`：`if (share > dwExp) share = dwExp`（夹的是原始 dwExp，不是 expB）。

**×1.5** `726D70 80 78 74 00` 测 **group+0x74**（征召令），再 `fild/fmul/call 0x403574`。C# 组对象无此字段；原生 ctor `726BE7 C6 43 74 00` 默认关，省略即忠实。

**发放门**（已计入 n/sumlv 仍可被跳过）：`726D1B call 0x6D7788` 状态 25；`726D28 80 BB 29 18 00 00 03` `[+0x1829]==3`。C# 无对应字段，默认不跳过 = 原生未置位。

C# `TPlayObject.GainExp` 已按上式落地。**FAITHFUL**（征召令 / 防沉迷两门记缺口，缺省行为一致）。

### 1.3 GroupFly

包装 `sub_6E0678`：`0x6E0683 E8 24 75 FD FF call 0x6B7BAC`（是队长？）假则静默；真则 `0x6E0694 E8 2B 6F 04 00 call 0x7275C4`。

`sub_7275C4` 判定顺序：

1. `group+0x3C` 队长空 → 整段返回（`7275D4 85 C0 / 74 47`）
2. 记下队长旧图 `[leader+0x128]`
3. 队长：`7275F1 E8 86 16 04 00 call 0x768C7C`（`Random([map+0x40])` 再 `Random([map+0x3C])`，**2 次**抽签）
4. 槽 0..10：空槽 / 是队长 → 跳过
5. 其余：`727614 E8 BB 76 FA FF call 0x6CECD4`

`sub_6CECD4` 逐人门（失败 **continue，无回执**）：

| 序 | VA + 字节 | 门 |
|---|---|---|
| 1 | `6CECE0 85 FF / 74 69` | 旧 Envir==0 |
| 2 | `6CECE4 83 7D 08 00 / 74 63` | 地图名==0 |
| 3 | `6CECEA 85 DB / 74 5F` | 成员==0 |
| 4 | `6CECEE 80 BB 78 01 00 00 00 / 75 56` | `[+0x178]` race != 0 |
| 5 | `6CECF7 3B F3 / 74 52` | 成员==队长 |
| 6 | `6CECFD E8 A6 40 0A 00 call 0x772DA8 / 75 47` | IsDead |
| 7 | `6CED06 80 7B 73 00 / 75 41` | ghost `[+0x73]` |
| 8 | `6CED0C 3B BB 28 01 00 00 / 75 39` | 仍须在**旧图** |

落点（**先 Y 后 X，各 Random(9) 一次，共 2 次**）：

```
6CED14  B8 09 00 00 00   mov eax,9
6CED19  E8 2E 4E D3 FF   call 0x403B4C
6CED1E  8B 96 30 01 00 00 mov edx,[esi+0x130]  ; owner Y
6CED24  83 C2 04         add edx,4
6CED27  2B D0            sub edx,eax          ; Y = ownerY+4-Random(9)
6CED2E  B8 09 00 00 00   mov eax,9
6CED33  E8 14 4E D3 FF   call 0x403B4C
6CED38  8B 8E 2C 01 00 00 mov ecx,[esi+0x12C]  ; owner X
6CED3E  83 C1 04         add ecx,4
6CED41  2B C8            sub ecx,eax          ; X = ownerX+4-Random(9)
6CED48  E8 9F 9F 09 00   call 0x768CEC        ; 地图解析失败则该人跳过
```

被跳过成员：**无 SM / 无 SysMsg**。C# `MoveCurrentGroupToMap` 门、抽签次数与顺序、静默跳过均对齐。**FAITHFUL**。

`GroupFlyInRange` `sub_727678`：每人 `Random(2*r)` ×2，无死亡门。C# 已按此。**FAITHFUL**。

`GroupFlyEx` 再调 `0x727A74` 数「!dead && !ghost && 地图名相等」的人数。C# `CountCurrentGroupOnMap` 同形。**FAITHFUL**。

---

## 2. 好友

| 动作 | ident | 原生发送点 | C# | 判定 |
|---|---|---|---|---|
| 查好友 | 4430 | `0x6FF6B4 66 BA 4E 11` Series=条数，记录 36 字节 | `CM/SM_QUERY/SEND_RELATION_FRIEND` | FAITHFUL |
| 查关注 | 4431 | `0x6FFFEB` | 4431 | FAITHFUL |
| 查黑名单 | 4432 | `0x700AB5` | 4432 | FAITHFUL |
| 加好友 OK | 4433 | `0x6F4E08` / `0x6F4E20` | `SM_ADD_RELATION_FRIEND_OK` | FAITHFUL |
| 加好友失败 | 4434 | `0x6F4C29 66 BA 52 11` | `SM_ADD_RELATION_FRIEND_FAIL` | FAITHFUL |
| 加关注 | 4435 | `0x6F4EEA` | 4435 | FAITHFUL |
| 加黑名单 | 4436 | `0x6F5066` | 4436 | FAITHFUL |
| 删好友 | 4437 | `0x6F4CEB` | 4437 | FAITHFUL |
| 删关注 | 4438 | `0x6F4F3D` | 4438 | FAITHFUL |
| 删黑名单 | 4439 | `0x6F50C9` | 4439 | FAITHFUL |
| 关注颜色 | 4440 | `0x6F4FC1` | 4440 | FAITHFUL |
| 邀请 type=2 | 与组队共用 `0x6F39B4` | `QueueNativeGroupRequest(..., 2)` | FAITHFUL |

**上下线**：没有独立好友 SM。登录 `0x6B1DE6 B9 9C 25 6B 00 / 66 BA 4C 00 / call 0x768BE0` 是 ident **0x4C** 的「名字+上线」喊话；下线 `0x6B2650 B9 68 29 6B 00` 同形「下线」。4430 全镜像只 1 个发送点（查询回包）。在线状态在 36 字节记录 **offset 35**。C# 查询时用 `GetPlayObject` 填 Online。**FAITHFUL**（无推送好友上线包）。

**落盘**：原生 `0x6FCC0C` CREATE：

```
relation (
  Idx int(11) NOT NULL auto_increment,
  RelationState int(11) unsigned NOT NULL default 0,
  FirstPlayerID bigi...
)
```

C# `NativeRelationMySqlStore` 列：`Idx, RelationState, FirstPlayerID, FirstChrName, FirstLevel, FirstJob, FirstFocusColor, SecPlayerID, SecChrName, SecLevel, SecJob, SecFocusColor`。位：好友 `0x01`，关注 `0x02/0x04`，黑名单 `0x08/0x10`。上限 200（`0x6F4BEB 3D C8 00 00 00`）。**FAITHFUL**。

线记录：好友 36B（名15 + level u16 + job + 行会名15 + online）；关注 22B；黑名单 20B。

流量：`client 4430=580 / 4433=68 / 4435=1`；`srv 4430=51491 / 4431=50911 / 4432=50911 / 4433=66`。

---

## 3. 行会协议（只协议层）

旧 CM 1035–1041 / 1044–1045 与旧 SM 753–761 / 768–771：游戏逻辑区 imm16 **0 命中**；`client_AppearTimes.ini` / `srv_AppearTimes.ini` **无这些键（0 次）**。C# Grobal2 已无这些常量，CM 分支已删。**FAITHFUL（已清）**。

现体系（CM=SM 同号，回包 Recog/Param 见各 handler）：

| ident | 含义 | 原生发送点 | 广播范围 | C# |
|---|---|---|---|---|
| 4500 | 自己的行会快照 | `0x6F0826 66 BA 94 11`；登录 `0x6B24D9` 必发 | 单播自己 | `SendNativePlayerGuild` |
| 4560 | 申请加入 | `0x6F59A6` | 单播申请人；body=目标 GildID | `HandleNativeGildRequestJoin` |
| 4562 | 行会列表 | `0x6AE5CE` | 单播；Param=页, Tag=result, Series=请求数 | `SendNativeGuildList` |
| 4563 | 读/写公告 | `0x6F5A6F` | 单播；mode 在 nParam3 | `HandleNativeGuildNotice` |
| 4564 | 创建 | `0x6ADDDA 66 BA D4 11`；成功/重名先 4500+4628 再本包 | 单播创建者 | `HandleNativeGildCreate` |
| 4565 | 查下属战队 | `0x6F5BC8` | 单播 | `SendNativeGuildCorps` |
| 4566 | 查会长 | 跳表 default `0x6DBC2C` | — | C# 故意不派发 |
| 4567 | 解散战队 | `0x6F5C30` | 单播操作者 | leadership |
| 4568 | 转让会长 | `0x6F5FE7` | 单播 | leadership |
| 4569 | 任命副会长 | `0x6F602C` | 单播 | leadership |
| 4570–4584 / 4587–4588 | 申请列表/联盟/敌对/关注/宣战/退出/成员列表/副职 | 各 1 个 `66 BA` 点（见 re2） | 单播操作者；成员列表 4584=`0x6AED58` | `NativeGuildRelationTailProtocol` |
| 4585 / 4586 | 按名宣战/关注 | 游戏逻辑 imm16 **0**（与 4579/4576 共用回包号） | — | 常量在，CM 有分支 |
| 4611 | 同意申请 | `0x6F6328` | 单播 | accept |
| 4627 | 取消申请 | `0x6ADBA2` | 单播 | cancel |
| 4628 | 社交角色刷新 | 登录 `0x6B24F5`；创建行会后 | 单播自己 | `SendNativeSocialRoleRefresh` |

字段顺序以各 `EncodeNative*` / `NativeCorpsWireCodec` 为准，本次未改业务。

流量：`client 4562=113`；`srv 4562=113 / 4564=8`。旧号 0 次。

---

## 4. 本次改动

1. **删死亡退组**（`TBaseObject.Base.cs`）：原生 `726E68` 只有关组/删人两处调用。
2. **退组文案** `LeaveGroup`：`已经退出了本组.`（全镜像 0 命中）→ ` 退出小组`（`0x6C32C4`）。
3. **解散文案** `CancelGroup`：`你的小组被解散了.` → `-你的小组被解散了。`（`0x7271BC` AnsiString len=19）。
4. **队长离开改转移**：`DelMember` 对齐 `0x727FB0`，广播「 提升为小队队长!」（`0x7280AC`）。
5. **离队后 SM 667** 改走 54 字节 `RefreshNativeGroupWire`，不再斜杠拼名。

---

## 5. BLOCKED

1. ~~**下线是否从槽里摘掉**~~ —— **2026-08-13 第二轮已解，见 `docs/m_sgrp_group_round2_20260813.md` §3。**
   定案：原生**不摘**。三条证据：`[player+0xA80]` 全镜像写形式只有 5 处（登录重挂
   `0x6B9EE7`、主动离队 `0x6C3278`、建组 `0x6C331D`/`0x6C36B5`、AddMember `0x72739A`），
   槽写入口 `sub_728518` 只有 3 个 E8 调用者、清槽 `sub_7284E8` 也只有 3 个，
   下线路径一个都不在；而且原生存在**登录重挂**机制
   （`0x6B9EE2 call 0x7282C8` 按 Handle+名字恢复 `[+0xA80]`，
   `0x6B24FC call 0x6F5168 → 0x7280C0` 按 64 位角色 id 把新对象指针写回槽），
   这套机制只有在「下线保槽」的前提下才有意义。
   C# `Disappear → DelMember` 判 **DIVERGENT**，登录重挂判 **MISSING**，
   两半必须一起落地（方案见 round2 §3.3），本轮未改。
2. **BLACKROOM 图上 tick 退组**：`0x6B3C11 80 78 7C 00` 后 `call 0x6C3200`。
   补充（round2）：`sub_6C3200` 会清 `[+0xA80]` **和** `[+0xA7C]`
   （`0x6C3278` / `0x6C3280`），并在 `[group+0x44] > 1` 时先广播「退出小组」文案
   （`0x6C3252 edx=0x6C32C4`, `0x6C3271 call 0x727068`），最后 `0x6C329A call 0x765E68`
   发 RM `0x279C`。它确实**不** compact 槽——这与「原生靠 ghost 门过滤、不摘槽」
   一致，不再是缺口。**仍 BLOCKED 的只剩「谁最终回收槽」**：候选是
   `sub_726E68` 内的 `0x726F4D call 0x7284E8`，但从 `sub_6C3200` 到它没有直连边。
3. **`0x6F4B4C` 登录钩子**：在「上线」喊话之后，调 `0x6A6340` / `0x6F6CB8`，不像 4430 推送。未追完，不当好友上线包。

---

## 6. 前人/台账订正

- 「死亡立即退组防刷经验」是 C# 自造。原生用收集轮 IsDead 门，尸体仍在队。
- 「已经退出了本组」不是战神串；真串是「 退出小组」。
- 队长离开原生是**转移**不是解散。
- 好友「上线了/下线了」不是好友协议；是 ident `0x4C` 的登录喊话。
- 旧行会 1035–1045 / 753–771 静态 0 + 流量 0，维持删除。
- **订正（第二轮）**：本文上面把 `[player+0xA7C]` 当成「队长镜像」是错的。
  它存的是 `group+0x38` = **组自身的 Handle**（`0x726BA3 mov [ebx+0x38],ebx`
  写进 group，`0x726BB3` / `0x7273A3` 复制给成员），用途是登录时给 `sub_7282C8`
  做组身份匹配，与「谁是队长」（`group+0x3C`）是两个字段。
