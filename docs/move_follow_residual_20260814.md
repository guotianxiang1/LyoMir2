# 三条跟进残口收口（MOVE-41 / MOVE-11 复核 / MFLG-pickup）

底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase 0x400000，17,661,952 B）。
分支 `w/move-follow`，从 master `be5126b0` 开出。

## 判定总表

| # | 前序分片给的说法 | 复核结论 | 处置 |
|---|---|---|---|
| 1 | CM_RUN(3013) mover 尾 `0x767683` 也调 `sub_6BBEE4`，`RunTo` 缺同伴跟随 | **成立** | 已修（MOVE-41，1 行） |
| 2a | `0x6D98DF` 是「双人坐骑相关臂」，未核 | **标注有误**：是 `CM_HERO_POWERUP(1108)` 臂，且 C# **早已接** | 不动 |
| 2b | `0x6DA017` 是 `CM_SPELL 3017` | **标注有误**：CM_SPELL(3017) 的臂是 `0x6DA04A` 且根本不调该钩子；`0x6DA017` 属 **CM 4105** 臂，已在 fail-closed 名单 | 不动 |
| 2c | `0x6EC635` 未核 | 在 `sub_6EC5D8` 内，唯一调用者是 **CM 3344** leaf，已在 fail-closed 名单 | 不动 |
| 3a | `pickup` 原生是前缀比较、C# 用全等 | **成立**（`0x775A8E mov ecx,6`，非 `0x775A93`） | 已修 |
| 3b | 包装器原生测 `Trim(attr)` 空、C# 用 `IsNullOrEmpty` | **成立**，且 Delphi `Trim` 的空白集与 .NET 不同 | 已修 |

另有一条**本轮新证实**的残口（HIT 族），见文末，未动。

---

## 条目 1 — MOVE-41：CM_RUN(3013) 的双人坐骑同伴跟随

### 1.1 原生

`sub_6BBEE4` 的直接 xref 恰好 4 处（rel32 全扫），全在坐骑簇，与 MOVE-39 记录一致：

| VA | 所属 | 本端 |
|---|---|---|
| `0x6EE8DC` | 接受双人坐骑，乘客搬到驾驶者格 | 已有 `MoveToNativeHorseDriver` |
| `0x741350` | 人形 walk mover `sub_741224` 尾 | MOVE-39 已补 |
| `0x767683` | **CM_RUN(3013) mover `sub_76756C` 尾（本条）** | 改前缺失 |
| `0x7677B4` | CM_RUN3(4108) mover `sub_767694` 尾 | 已有 `SyncNativeHorsePartnerAfterRun3` |

两个 run mover 的成功尾部：

```
; sub_76756C（×2 格，ident 0x0D）            ; sub_767694（×3 格，ident 0xD58）
0x767625 mov [ebx+0x12C],edi   ; 提交 X      0x76774F 同
0x76762B mov [ebx+0x130],eax   ; 提交 Y      0x767755 同
0x767634 mov dl,0x17 / call 0x76B4D0         0x76775E 同        ; 清定时状态 0x17
0x76763F mov dx,0x0D  / call 0x765154        0x767769 mov dx,0xD58 / call 0x765154
0x76764A mov byte [ebp-1],1    ; result=1    0x767774 同
0x767656 call 0x778EC0         ; 落格/传送门  0x767780 同
0x76765B mov dl,0x33 / call 0x772960         0x767785 同        ; InBodyState(0x33)
0x767666 je  0x767688                        0x767790 je
0x767668 cmp dword [ebx+0x3C0],0             0x767792 同        ; 双人坐骑同伴指针
0x76766F je  0x767688                        0x767799 je
0x767671 mov al,[ebx+0x154]    ; 自己朝向     0x76779B 同
0x767678 mov ecx,[ebp-8]       ; 新 Y        0x7677A2 mov ecx,[ebx+0x130]
0x76767B mov edx,edi           ; 新 X        0x7677A8 mov edx,[ebx+0x12C]
0x76767D mov eax,[ebx+0x3C0]   ; 接收者=同伴  0x7677AE 同
0x767683 call 0x6BBEE4                       0x7677B4 call 0x6BBEE4
```

门与被调例程完全一样。唯一差别：3013 传**刚算出的局部量**（`edi` / `[ebp-8]`），4108 **回读刚提交的** `[ebx+0x12C]`/`[ebx+0x130]` —— 因为 `0x767625`/`0x76762B` 就是拿这两个局部量提交的，两者同值。所以不是逐字节同构，但**语义等价**，可以复用同一个移植体，不写第二份。

### 1.2 可达性（为什么这条真的会跑到）

`sub_76756C` 的 rel32 xref 有 4 处，但其中两处是**编译器留下的死代码**，恒不可达：

```
0x67524A c645f600  mov byte [ebp-0xA],0     0x71AE96 c645f700  mov byte [ebp-9],0
0x67524E 807df600  cmp byte [ebp-0xA],0     0x71AE9A 807df700  cmp byte [ebp-9],0
0x675252 740d      je  0x675261  ; 恒跳     0x71AE9E 740d      je  0x71AEAD  ; 恒跳
0x675258 call 0x76756C   ; 不可达           0x71AEA4 call 0x76756C   ; 不可达
```

活的只有两处：

- `0x6BC062`，在 run 原语 `sub_6BBFBC` 里：`0x6BC059 call 0x764A90`(GetNextDirection) → `0x6BC060 mov eax,ebx` → `0x6BC062 call 0x76756C`。这正是 CM_RUN(3013) 的路径。
- `0x68BD3E`，在 `sub_68BD28`（先清状态 0x17 再跑，成功后 `call 0x76BECC(dx=0x3C,ecx=0xA)` / `call 0x76BEC8(dx=1)`），被 `0x68AA72`、`0x68B9E1` 调用。不是玩家 CM_RUN 路径，本条不涉及。

本端 CM_RUN(3013) 的链条：`Message.cs` `case CM_RUN` → `ClientRunXY`(`TPlayObject.Attack.cs:749`) → 梯子 `IsNativeRunLadderAllowed()`（= `0x6BBFCB..0x6BBFFF`）→ 不过则 `ClientNativeRun3Fallback`（= `0x6BC001` 夹取降级走 `sub_6BBCD8`）→ 过则 `GetNextDirection` → **`RunTo`**(`TPlayObject.cs:988`)。`RunTo` 全仓只有这一个调用者，与原生一一对应。

### 1.3 改动

`GameSvr/Players/TPlayObject.cs` `RunTo`，`if (Walk(Grobal2.RM_RUN))` 成功臂内、`result = true` 之前加一行 `SyncNativeHorsePartnerAfterRun3();`。位置与 MOVE-39 在 `WalkTo` 的挂法一致：原生 `sub_6BBEE4` 在广播（`0x76763F`）与落格 `sub_778EC0`（`0x767656`）之后，而 `Walk()` 把这两步合并了；`Walk()` 返回 false 时本端要回滚坐标，所以只挂成功臂。

作用域天然精确：`RunTo` 是 `TPlayObject` 的 private，`m_NativeHorsePartner` 也只在 `TPlayObject` 上，不需要像 MOVE-39 那样走虚方法。

### 1.4 明确没做

- `RobotPlayObject.cs:2616-2626` 有一份 stock-Mir2 形状的 2 格 run 拷贝（仍用 `boDiableHumanRun || boGMRunAll`，master 已判定为 stock 污染并从真实路径移除）。它是机器人压测脚手架、无原生对应物，不动。
- `sub_68BD28`（`0x68BD3E`）那条 run 入口未归因，未接。

---

## 条目 2 — `sub_6BCE2C` 剩余调用点复核：三处都不该接

`sub_6BCE2C` 的 rel32 xref 恰好 8 处，与 MOVE-11 记录一致：
`0x6D98DF` / `0x6D9BEC` / `0x6D9D08` / `0x6D9ED3` / `0x6D9F7D` / `0x6DA017` / `0x6EC635` / `0x6EE201`。

先补一条对函数本身的更正：`sub_6BCE2C(eax=Self, edx=wIdent)` 的**第二个参数是死参**。它只被转发给第三个调用 `0x6BCE43 mov edx,esi / 0x6BCE49 call [ecx+0x1D8]`，而 TPlayer 的该槽 `sub_6EE2AC` 开头就 `0x6EE2B0 mov edx,eax` 把它覆盖掉了。前两个调用（`0x6EE128`、`0x6EF5D0`）只吃 `eax`。所以本端 `CancelNativeActionChannels()` 不带参是忠实的，`0x6EC631 xor edx,edx` 那种传 0 的写法也无意义。

### 2a. `0x6D98DF` —— 是 CM_HERO_POWERUP(1108)，且**早已接线**

派发表：`0x6D81A5 add eax,-0x413` / `0x6D81AA cmp eax,0x48` / `0x6D81B3 jmp [eax*4+0x6D81BA]`，即 ident 1043..1115。`0x6D98B1` 落在 idx 65 → **ident 1108 = `CM_HERO_POWERUP`**。

```
0x6D98B1 mov dl,0x33 / call 0x772960 / test al,al / jne 0x6DBC2C   ; 骑乘态 -> 整臂放弃
0x6D98C3 mov dl,0x34 / call 0x772960 / test al,al / jne 0x6DBC2C   ; 乘客态 -> 整臂放弃
0x6D98DF call 0x6BCE2C                                             ; 三连
0x6D98E9 call 0x772A50 ...
```

本端 `TPlayObject.Message.cs:3024-3029` 一字不差：

```csharp
case Grobal2.CM_HERO_POWERUP:
    if (HasNativeActiveState(0x33) || HasNativeActiveState(0x34))
        break;
    CancelNativeChannelMagic();
    CancelNativeLocationChannelMagic();
    CancelNativeType51PendingForTimedAbility();
```

两道门在前、三连在后，顺序一致。**已覆盖，不动。** MOVE-11 注释里把这处记成「双人坐骑相关臂」、又把 `0x6EE201` 记成「CM_HERO_POWERUP 臂」，两处归属写反了（`0x6EE201` 在 `sub_6EE174` 内，由 CM 4105 进入）；结论「已在 CM_HERO_POWERUP 臂按同一顺序连用」本身是对的，只是行号 3011-3013 已过期。

### 2b. `0x6DA017` —— 不是 CM_SPELL，是 CM 4105，且在 fail-closed 名单

CM_SPELL(3017) 走的是另一张表：`0x6D857D add eax,-0xBC2` / `cmp eax,0x10` / `jmp [eax*4+0x6D8592]`，idx 7 → **`0x6DA04A`**。该臂是 `call 0x6F2D48` + `InBodyState(0x33)` 判定，**通篇没有 `sub_6BCE2C`**。

`0x6DA017` 所在的 `0x6DA005` 不在跳表里，由比较链 `0x6D8733 cmp eax,0x100A` → `jg` 分支外侧 `0x6D875F sub eax,3 / 0x6D8762 je 0x6DA005` 进入，反推 ident = **0x1009 = 4105**（同链锚点校验：`0x6D8778 dec eax / je 0x6D9D99` = 4108 = CM_RUN3，与既有结论吻合）。

```
0x6DA005 mov eax,[ebp-4] / 0x6DA008 call 0x7742C0   ; worker 1
0x6DA017 call 0x6BCE2C                              ; worker 2（本条）
0x6DA026 call 0x6EE174                              ; worker 3（内含 0x6EE201 的第二次三连）
```

本端 `Grobal2.CM_4105 = 0x1009` 已登记，且 `NativeCmQ3FailClosed` 第 110 行把整条 leaf 列为未移植：`sub_7742C0` 刷的 `[+0x12C/0x130/0x154/0x388/0x178/0x270/0x272]` 与 `sub_6EE174` 的 `[+0x4C0]/[+0xA24]/[+0x1914]` 子系统都未建模，`Q3Cm4105()` 直接 `Q3Drop`。

**不动。** 只补三连而不补另外两个 worker，会让一条整体被 fail-closed 的命令单独发出 SM `0x4D0`/`0x4D2`/`0xD57` 取消包 —— 那既不是原版的整臂行为，也违反 fail-closed。

### 2c. `0x6EC635` —— 在 `sub_6EC5D8` 内，唯一入口是 CM 3344，同样 fail-closed

`sub_6EC5D8` 起于 `0x6EC5D8`，`0x6EC635` 在其体内；rel32 全扫其调用者**只有一个**：`0x6DADD9`，即 CM 3344 的 leaf `0x6DADD6`。

```
0x6EC609 call [vmt+0xE8] (dx=0x78) -> [ebp-8]；为 0 则 0x6EC6FA 退出
0x6EC625 call [vmt+0x1F4] (edx=0x78) -> esi；非 0 则跳 0x6EC691
0x6EC635 call 0x6BCE2C            ; 本条
0x6EC63E call [vmt+0x290]
0x6EC656 ... dx=0x2905 发包
```

本端 `NativeCmQ3FailClosed` 第 102-103 行早已把 CM 3344 记为未移植，并且**注释里就写了「链 0x6BCE2C/0x741698」**。`[+0x1F0]/[+0x1F4]/[+0x290]` 未建模。**不动**，理由同 2b。

### 2d. 本轮新证实的残口（未动，留给专项）

`0x6D9ED3` 与 `0x6D9F7D` 这两处，前序说法是「已接」，实测**没接**：全仓 `CancelNativeActionChannels()` 只有 `TPlayObject.Attack.cs:767`（run 3013）和 `:905`（walk 3011）两个调用点，HIT 族一处也没有。

原生 HIT 族共享臂 `0x6D9EAF`（跳表 idx 4/5/6/8/9/14/15/16 → ident 3014/3015/3016/3018/3019/3024/3025/3026）：

```
0x6D9EB4 call 0x6F2D48
0x6D9EBC call 0x6BBEB8 / 0x6D9EC3 jne 0x6DBC2C   ; 骑乘门（本端 IsNativeHitBlockedByMountState 已有）
0x6D9ED3 call 0x6BCE2C                            ; ← 缺
0x6D9EDF mov dl,1 / call [vmt+0x40]               ; can-act，失败跳 0x6D9F0F
0x6D9F06 call 0x6EC078                            ; = ClientHitXY
```

本端 `TPlayObject.Message.cs:1702-1710` 是 `IsNativeHitBlockedByMountState()` 之后直接 `ClientHitXY(...)`，中间什么都没有。

**为什么这轮不顺手补**：CASE2（`0x6D9F4B` 那条，CM_3037/3027）把同一个钩子放在 **can-act 之后**（`0x6D9F6C call [ecx+0x40]` → `0x6D9F7D call 0x6BCE2C`），而 CASE1 放在 **can-act 之前**。C# 把两者合并进同一个 `case` 块，单一插入点无法同时忠实两种顺序；而且本端这条 `case` 目前根本没有建模 `[vmt+0x40]` 那道 can-act 门。要修就得先把 can-act 门取证落地、再按 ident 分叉，属于独立一条，不在本轮三条契约内，按 fail-closed 只报不改。

---

## 条目 3 — `@TempSetMapParam`：`pickup` 前缀比较 + `Trim` 空串判定

### 3.1 比较器 `sub_4C6E94` 到底是什么

先纠正一处引用：`mov ecx,6` 在 **`0x775A8E`**，`0x775A93` 是 `mov edx,0x775FCC`。字面串 `0x775FCC` 经 Delphi 长串头（len 在 -4）读出确为 `'pickup'`（len 6）。

`sub_4C6E94(eax=s1, edx=s2, ecx=n)` 全文：

```
0x4C6EC3 xor ebx,ebx                       ; result := False
0x4C6EC5 test esi,esi / jle 0x4C6F12       ; n<=0 -> False
0x4C6ECC call 0x4057D0 (Length s1)
0x4C6ED1 cmp esi,eax / jg 0x4C6F12         ; n > Len(s1) -> False
0x4C6ED8 call 0x4057D0 (Length s2)
0x4C6EDD cmp esi,eax / jg 0x4C6F12         ; n > Len(s2) -> False
0x4C6EE1 mov bl,1                          ; result := True
0x4C6EEC..0x4C6F10  逐字符：两边各过 0x4034D4 再 cmp，不等则 bl:=0
```

关键：**只有 `Len >= n`，没有 `Len == n`**。所以它是「比前 n 个字符」的**前缀**判定。`0x4034D4` 是 `@UpCase`（`cmp al,0x61/jb; cmp al,0x7A/ja; sub al,0x20`），纯 ASCII 大写化 —— 本端 `HUtil32.CompareLStr` 连同 `NativeAsciiUpCase` 早已是这套语义的忠实移植（eqv-17 已做过）。

于是 `attr = "pickupXYZ"` 在原版命中，C# 的 `string.Equals(..., OrdinalIgnoreCase)` 不命中。**成立。**

### 3.2 改成前缀是否安全（这条必须先证）

把 parser A（`sub_774D98`，`0x774D98..0x775BD0`）里所有 `call 0x4C6E94`（前缀）与 `call 0x40BD78`（全等）的字面串全扫了一遍，共 51 个 token：

```
SAFE DARK FIGHT FIGHT3 FREEPK DAY QUIZ DARE MONATTACK OLDSKY NEWSKY MULSKY
NORECONNECT DROPTOMAP CHECKQUEST NEEDHOLE NORECALL NORANDOMMOVE NODRUG MINE
NOPOSITIONMOVE MINGJIANG HACKQUEST BLACKROOM NOEXPLORE MAPFIREWALLBURN MapSign
FLYDROPITEM RELIVEBACK RUNFLAG BREAKLEVEL CRAZYBREAKLEVEL AUTORELIVE
NOEQUIPRELIVE NOC2C NOHERO DREAMCASTLEMAP UNIFIEDLEVEL LIMITPLAYERLEVEL
LIMITHEROLEVEL NOMAGIC TRIGGERBOMB FOXMAP UserNoKill LimitSkill NEWMJNORMALPRIZE
NoRelive LimitItemMove pickup ONLYDROPSPEC LIMITBAGITEMDROP
```

除 `pickup` 自己外，**没有第二个以 `pickup` 开头的字面串**（不分大小写）。所以改成前缀不可能抢走原版路由到别的臂的输入；反向也一样，`pickup` 后面的 `ONLYDROPSPEC`/`LIMITBAGITEMDROP` 都不以 `pickup` 开头。本端只实现了其中 9 个臂，顺序与原生链一致，不受影响。

### 3.3 包装器的空串判定

`sub_774D24(eax=envir, edx=attr, ecx=state)`：

```
0x774D4B lea edx,[ebp-8] / 0x774D4E mov eax,esi / 0x774D50 call 0x40C140   ; Trim(attr)->[ebp-8]
0x774D55 cmp dword [ebp-8],0 / 0x774D59 je 0x774D70                        ; 空 -> 返 0
0x774D5D sub eax,2 / 0x774D60 jae 0x774D70                                 ; 无符号 state>=2 -> 返 0
0x774D64 mov edx,esi                                                       ; ★ 传给 parser 的是未 Trim 的原串
0x774D68 call 0x774D98
```

两点：

1. `0x40C140` 是 Delphi `Trim`，两端各一处 `cmp byte [..],0x20 / jbe`（`0x40C171`、`0x40C193`）—— 掐的是**所有 <= 0x20 的字节**。这和 .NET 的 Unicode 空白集不等价：.NET 不掐 `#01..#08`，却掐 `U+00A0`。所以既不能用 `IsNullOrEmpty`（漏掉纯空白），也不能直接用 `IsNullOrWhiteSpace`（边界不同），得按字节阈值自己判。
2. **Trim 的结果只用于这道判定**，`0x774D64` 传下去的仍是原串；回显消息取的也是原串（caller `0x62996B`、`0x629A30` 同取 `[ebp-0x38]`）。所以下游一律不 Trim —— `" pickup"`（带前导空格）在原版是**不命中**的。

返回码到消息的映射（唯一 caller `0x629970`）：

| result | 原生 | 本端 |
|---|---|---|
| 1 | state=0 → `取消地图属性=`+attr+`，操作成功`（`0x62DB30`+`0x62DB48`）；state=1 → `增加地图属性=`…；其余静默 | 一致 |
| 0x64 | `该GM命令目前不支持此地图属性=`+attr（`0x62DB74`） | 一致 |
| 其它（含 0） | state=0/1 → `…地图属性=`+attr+`，操作失败`（`0x62DB9C`）；其余静默 | 一致 |

即纯空白 attr 在原版走的是「**操作失败**」，本端改前走「不支持此地图属性」。

### 3.4 改动

`GameSvr/Command/Commands/TempSetMapParamCommand.cs`：

- `ApplyPickupAttribute` 的入口门：`string.IsNullOrEmpty(attribute)` → `attribute == null || IsNativeTrimEmpty(attribute)`。
- `pickup` 臂：`string.Equals(attribute, "pickup", OrdinalIgnoreCase)` → `HUtil32.CompareLStr(attribute, "pickup", "pickup".Length)`，与同函数里另外 8 个已证 token 用同一个助手。
- 新增 private `IsNativeTrimEmpty(string)`：任一字符 `> ' '` 即非空。

`environment.Flag.boPICKUP = state == 1;` 这行原样保留（`NativeTempSetMapParamPickupCheck.VerifySourceBoundary` 按字面串锁它）。

---

## 验证

`dotnet build GameSvr/GameSvr.csproj`（含 `-t:Rebuild` 全量）：**0 错误**，15 警告全部是 master 既有。

`AuditTools/NativeTempSetMapParamPickupCheck`：改前改后**同一处**失败，且与本轮无关 —— `Program.cs:77` 断言权限拒绝文案 `权限不够!!!`，运行期实际是 `该命令需要5级GM才能使用`（该工具 `PrepareRuntimeFiles` 造的 `Command.conf` 没设这条，属既有环境问题）。已用「把该文件 `git checkout --` 回 master 版本再跑一遍」确认失败点完全一致。它之前的全部断言（`TARGET PICKUP 1`、`target PiCkUp 0`、state `-1`/`2` 静默、`TARGET SAFE 0` → 不支持、缺图、缺参帮助）在改后依旧通过。

另外用一个仓外临时宿主（`%TEMP%\mfcheck`，反射直调 `ApplyPickupAttribute`）实测净行为，17 项全中：

| attr | state | result | 说明 |
|---|---|---|---|
| `pickup` / `PICKUP` / `PiCkUp` | 1 | 1 | 大小写不敏感，改前也过 |
| `pickupXYZ` | 1 | **1** | 改前 100 —— 本条修的就是它 |
| `pickup123` | 0 | **1** | 同上 |
| `pick` | 1 | 100 | 短于 n，原生也不命中 |
| `SAFE` / `ONLYDROPSPEC` | 1 | 100 | 未移植 token 的既有边界不变 |
| `""` | 1 | 0 | 改前也是 0 |
| `"   "` / `"\t"` / `"\x01"` | 1 | **0** | 改前 100；Delphi Trim 掐 <=0x20 |
| `"\u00A0"` | 1 | 100 | Delphi **不**掐（0xA0>0x20），故不是空 |
| `" pickup"` | 1 | 100 | parser 收到的是未 Trim 原串 |
| `pickup` | 2 / -1 | 0 | state 门不变 |

---

## 改动清单

| 文件 | 改动 |
|---|---|
| `GameSvr/Players/TPlayObject.cs` | `RunTo` 成功臂加 `SyncNativeHorsePartnerAfterRun3();`（+9 行，含取证注释） |
| `GameSvr/Command/Commands/TempSetMapParamCommand.cs` | 入口门改 Delphi-Trim 语义；`pickup` 改前缀比较；新增 `IsNativeTrimEmpty`（+38/-3） |
| `docs/move_follow_residual_20260814.md` | 本文 |

条目 2 的三处**零改动**。

## 接线点（本轮新增，供后续排查）

- `TPlayObject.RunTo` → `SyncNativeHorsePartnerAfterRun3()`：CM_RUN(3013) 成功移动后推动双人坐骑同伴。同伴指针 `m_NativeHorsePartner`（原生 `[+0x3C0]`）的写点仍全在坐骑簇，本条只加读点。
- `TempSetMapParamCommand.ApplyPickupAttribute`：`pickup` 判定从全等放宽到前缀，`pickupXYZ` 这类输入现在会真的翻 `Flag.boPICKUP`。`boPICKUP` 的下游消费者未变。

## 未做（明确留口）

1. **HIT 族 `0x6D9ED3` / `0x6D9F7D` 的 `sub_6BCE2C` 未接**（见 2d）。需先取证 `[vmt+0x40]` can-act 门，再按 CASE1/CASE2 分叉，独立一条。
2. `sub_68BD28`（`0x68BD3E` 调 run mover）未归因。
3. `sub_779CD8` vs `sub_7797CC`：`sub_6BBEE4` 用的是前者（实测无地形/占用判定），本端 `SyncNativeHorsePartnerAfterRun3` 仍用 `MoveToMovingObject(..., boFlag: true)` 近似。这是 master 在 4108 分支就做的既有选择，本轮沿用未改；要精确得单独落一个「无判定重定位」原语。
4. `@TempSetMapParam` 其余约 42 个 token 仍未移植（原版返 1，本端返 0x64），是刻意缩窄的既有边界，由 `NativeTempSetMapParamPickupCheck` 用 `SAFE` 锁住。
