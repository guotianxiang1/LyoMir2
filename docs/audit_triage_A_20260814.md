# 审计工具分诊报告 A（atriageA · 22 个工具）

- 分支：`w/atriageA`（基于 master `69f049b6`）
- worktree：`D:\loym2\.claude\wt3\atriageA`
- 底本：`staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`）
- 分诊辅助脚本：`tools/atriage_dis.py`（反汇编/裸转储）、`tools/atriage_find.py`（字面量与引用）、
  `tools/atriage_run.ps1`（单工具构建+运行）

## 0. 结论速览

| 定性 | 个数 | 工具 |
|---|---|---|
| (a) 陈旧测试 | 13 | ChatShieldExact / ChgMonItemPercentStatic / HeroLifecycle / DbGateRegression / InProcEngineRun / DispatcherProtocolExact / DispatcherStateExact / HeroDb / HeroPasAdmin / HeroUnionState / AddPlayerAbilType6Compat / DealValidationFailureSemantics / GloryMutationCompat |
| (b) 真回归 | 1 | ExactEnvironmentMove（传送提交点）|
| (c) 环境/harness | 7 | ActiveOutgoingProtocol / HeroTailProtocol / DbSvrServiceRegression / CSharpGateM2Integration / DeathDropPolicy / DelphiRandomDormantCompat / DynRoomEventActivation |
| (d) 真臆造 | 0 | —（HeroPasAdmin 钉的臆造 API 早已被代码删干净，属 (a)）|
| BLOCKED | 1 | GateLegacyType18Compat |

> 计数说明：DispatcherProtocolExact / DispatcherStateExact 同时含 (a) 与 (c) 两类缺陷，按主因计入 (a)；
> DeathDropPolicy / DelphiRandomDormant 的问题是闸门扫到了注释，按 (c) 计。合计 13+1+7+1 = 22。

**修复后状态：20/22 退出码 0；1 个 exit=2 是工具自身的 INCOMPLETE 语义（见 §3）；1 个 BLOCKED。**

## 1. 真回归（最重要）

### R1 — 传送的提交点划在通知之后，通知失败会把已完成的移动回滚

- 位置：`GameSvr/Actors/TBaseObject.cs` `TrySpaceMoveToEnvironment`
- 抓到它的工具：`ExactEnvironmentMoveCheck`（`post-commit message exception reported failure`）
- 现象：`committed = true` 原本写在 `RM_NATIVE_CLEAROBJECTS` / `RM_NATIVE_CHANGEMAP` /
  `RM_SPACEMOVE_SHOW` 三条通知【之后】。于是排队消息阶段的任何异常都会走 `finally`
  把对象从目标图摘掉、搬回原图 —— 即「通知失败 ⇒ 移动撤销」。
- 字节证据（战神在这条路径上根本没有回滚能力）：

```
006BD294  55                    push ebp
006BD295  8B EC                 mov  ebp,esp
006BD297  81 C4 F4 FE FF FF     add  esp,-0x10C
006BD29D  53 / 56 / 57          push ebx / esi / edi
```

  `sub_6BD294`（TPlayer 的 VMT+0x1C0 传送臂）序言里**没有 SEH 帧**
  （无 `push <handler>` / `push dword fs:[0]` / `mov fs:[0],esp`），
  且全镜像对 `"SpaceMove"` 异常串 **0 命中**。原生一旦 `DeleteFromMap → AddToMap` 走完，
  就不存在任何把对象搬回去的路径。
- 处置：把 `committed = true` 上移到 `OnEnvirnomentChanged()` 之后、第一条 `SendMsg` 之前。
  `AddToMap` 之前/之中失败仍回滚（这是 C# 自加的稳健层，保留）；通知阶段失败不再回滚。
- 回归面：`ExactEnvironmentMoveCheck` PASS；另跑 `MovementCollisionCheck` /
  `NativeMoveGateCheck` / `DynRoomMasterRelocationCheck` / `DynRoomRuntimeTransactionCheck` /
  `ExactEnvironmentRemoveEverywhereCheck` / `InProcEngineRunCheck` 全 PASS。
  （`MovementReliveCheck` 仍红，但改动前就红，见 §4 的「权限不够!!!」同源问题。）
- 已修复：✅ commit `7f322f3f`

### 未构成回归但需主代理定夺的两条「无字节锚」发现

见 §4。

## 2. 逐工具

### 2.1 ActiveOutgoingProtocolCheck — (c) 环境
- 原状：NOEXE（sweep 找不到 exe）。
- 根因：csproj 把 `OutputPath` 指到仓库【外】的 `..\..\..\tmp\active_producer2\`，
  既让 sweep 的三处候选路径都落空，工具自身的 `FindRepositoryRoot` 向上走也走不回 checkout
  （直接跑会抛 `repository root not found`）。
- 处置：删掉这条 OutputPath 覆盖，落回 `bin\Debug\net8.0`。断言未动。
- 已修复：✅ `3e283c9c`… 实际在 `4c92c44b`

### 2.2 AddPlayerAbilType6CompatCheck — (a)
- 报错：`expired timed state is not notified before it is marked dirty`
- 根因：正则要求 `SendTimedAbilityState(node,true); RemoveTimedAbilityCompanion(...); if(...)`
  三句紧邻，而代码在中间插了 `OnNativeTimedStateLost(node.InternalType);`。
- 字节证据（该 hook 是原生的）：

```
0x77337C sub_77337C          ; 状态丢失虚函数
0x773386 push 0              ; LOST 标志
0x773388 xor ecx,ecx         ; 丢失侧秒数恒 0
0x742692 33 C0               xor eax,eax
0x742694 8A C3               mov al,bl              ; 节点类型
0x742696 83 C0 F2            add eax,-0xE
0x742699 83 F8 5C            cmp eax,0x5C
0x74269C 0F 87 A0 05 00 00   ja  0x742C42           ; 域外静默
0x7426A2 FF 24 85 A9 26 74 00 jmp [eax*4+0x7426A9]
```
- 处置：正则收进 `OnNativeTimedStateLost`，等于**额外钉住**该调用点（比原断言更强）。
- 已修复：✅ `ca432edc`

### 2.3 ChatShieldExactCheck — (a)（主代理已预判）
- 报错：`native mask load: expected=2783973130, actual=0`
- 字节证据：

```
0x6B12A0 8B 83 9C 0B 00 00  mov eax,[ebx+0xB9C]
0x6B12A6 89 86 F8 04 00 00  mov [esi+0x4F8],eax     ; 存
0x6B029C 8B 80 F8 04 00 00  mov eax,[eax+0x4F8]     ; 取
0x6B02A5 89 82 9C 0B 00 00  mov [edx+0xB9C],eax
```
- 处置：`const int offset = 0x500` → `0x4F8`，PASS 横幅同步。
- 已修复：✅ `3e283c9c`

### 2.4 ChgMonItemPercentStaticCheck — (a)（主代理预判为真回归，实为陈旧正则）
- 报错：`native monster drop selection changed: the roll must stay Random(<MaxPoint-derived denominator>) <= MonItem.SelPoint`
- 根因：断言用 `Random\(MonItem\.MaxPoint\b[^)]*\)` 这种**平坦**正则，`[^)]*` 跨不过嵌套括号；
  掉落分母外面套了 `Plugins.YanshenEquipDropBoost.Denominator(MonItem.MaxPoint * penalty, killer)` 就假红。
- 字节证据（原生形状与 C# 一致）：

```
0071FD34 8B 45 E4           mov eax,[ebp-0x1C]      ; MonItem
0071FD37 8B 40 14           mov eax,[eax+0x14]      ; MaxPoint
0071FD3A F7 6D D4           imul dword [ebp-0x2C]   ; × 疲劳系数(obj+0x1828 派生)
0071FD3D E8 0A 3E CE FF     call 0x403B4C           ; Random(eax)
0071FD42 8B 55 E4           mov edx,[ebp-0x1C]
0071FD45 3B 42 10           cmp eax,[edx+0x10]      ; SelPoint
0071FD48 0F 8F 51 01 00 00  jg  0x71FE9F            ; 只保留 <=
```
  另：眼神装备爆率补丁的安装点「0x71FD37 起 6 字节」正是 `8B 40 14 F7 6D D4`，与代码注释一致。
- 处置：改成**括号配平**扫描，且额外钉住 RNG 接收者 `M2Share.RandomNumber`（比原断言更强）。
- 已修复：✅ `3e283c9c`

### 2.5 CSharpGateM2IntegrationCheck — (c) 夹具参数
- 报错：`OperationCanceledException` @ 等 0xB2 标记帧。
- 根因：`GateService` 的门限是 `nCheckBlock*10` 字节，夹具每帧 `HEADER_SIZE+1 = 17` 字节。
  夹具设 `nCheckBlock=4`(=40B) 时真实时序是「A、B、探针、C」，而紧随的断言描述的是
  「A、探针、B、探针、C」—— 该场景**从建立起就没发生过**。
- 处置：`nCheckBlock` 4 → 1（=10B），每帧各自触发逐帧门，正是断言描述的时序。断言一字未动。
  顺手把 `PASS M2 compact ACK stays 14 bytes` 的过期文案改成打印常量（现为 16）。
- 已修复：✅ `2da7457c`
- ⚠ 移交：门限算术**无字节锚**，见 §4.1。

### 2.6 DbGateRegressionCheck — (a) ×3
1. **77BBAA33 超长帧头**：断言把超长长度写在 `+0x0C`，可 `+0x0C` 是 Cmd、长度在 `+0x0E`，
   于是那个「超长帧头」实际 BodyLen=0，被解析成一个完全合法的 16 字节帧，闸门从未触发。

```
0x5F6679 66 81 78 0E 00 30  cmp word [eax+0x0E],0x3000
0x63A674 8D 46 10           lea eax,[esi+0x10]
0x63A677 0F B7 57 0E        movzx edx,word [edi+0x0E]
0x63A67B 03 C2              add eax,edx        ; 总长 = 0x10 + BodyLen
```
2. **仓库容量 24**：原生容器构造即 48，装载只在存档值【严格大于】48 时才覆盖。

```
0x6AD8EB C7 46 08 30 00 00 00  mov dword [esi+8],0x30   ; ctor = 48
0x6B0CBC 66 8B 80 16 05 00 00  mov ax,[eax+0x516]       ; == 记录 0x50E
0x6B0CC3 66 83 F8 30           cmp ax,0x30
0x6B0CC7 76 0F                 jbe 0x6B0CD8             ; <=48 保持 48
0x6B0CD5 89 42 08              mov [container+8],eax
0x6B112B 66 8B 40 08           mov ax,[eax+8]           ; 存盘侧
0x6B112F 66 89 86 0E 05 00 00  mov [esi+0x50E],ax
```
   （装载基址比存盘基址低 8 字节，故 C# 的 `StorageSpaceCountOffset=0x50E` 是对的。）
   处置：roundtrip 夹具改成 96（高于地板，真正验证透传），并**新增** 24/48/49/192 四点边界表
   钉死 `jbe 0x30` 地板 —— 比原来的单点断言更强。
3. **`removed eye sidecar must not restore stale fields`**：此断言此前被前两处失败挡住从未执行。
   眼神字段已双写进原生 208 字节物品记录，而该记录逐字节 `rep movsd` 搬运：

```
SAVE 0x6B170F 8D 70 20 / 0x6B1712 B9 34 00 00 00 / 0x6B1717 F3 A5   (0x34 dword = 208)
     0x6B171B 83 FF 30  cmp edi,0x30                                 (48 格循环)
LOAD 0x74DB3A 8D 7B 20 / 0x74DB3D B9 34 00 00 00 / 0x74DB42 F3 A5
```
   而 ScriptData 类型 `0x79` 根本不是原生分节：
   `0x6E4510 83 F8 08 cmp eax,8 / 0x6E4513 0F 87 3D 03 00 00 ja 0x6E4856`，
   跳表 `0x6E4520` 只覆盖类型 0..8。所以 0x79 是迁移期覆盖层，抹掉它不该连原生记录里的
   来源块一起抹掉。断言拆成三条且不弱化：清空后 `ys1..ys17/jp1..jp6` 必须全 0（`Pack` 无条件
   重写这些，这才是「陈旧字段复活」闸门）；清空后必须产不出 0x79 分节；再存再读必须是不动点。
- 已修复：✅ `43659d30`（残余 exit=2 见 §3）

### 2.7 DbSvrServiceRegressionCheck — (c) 环境
- 报错：`Could not find a part of the path '...\bin\Debug\net8.0-windows\DBSvr\Forms\MainForm.cs'`
- 根因：10 处 `Directory.GetCurrentDirectory()` 里 9 处是仓库相对源码探针；更糟的是
  `TestNativeNameValidation` 自己会 `chdir` 到临时过滤词目录，随后 L1575 用 `originalDirectory`
  拼路径，那同样是 bin 目录。
- 处置：统一改走 `RepoRoot()`（从 `AppContext.BaseDirectory` 起向上找 `DBSvr.csproj + GameSvr.csproj`，
  不受 chdir 影响）。断言未动。42 项全 PASS。
- 已修复：✅ `4c92c44b`

### 2.8 DealValidationFailureSemanticsCheck — (a)
- 报错：`ClientChangeDealGold lacks 'nGold < 0' guard`
- 字节证据：门是 `jle`，即拒绝 `<= 0`，不是 `< 0`。

```
0x6C4477 85 F6              test esi,esi        ; esi = nGold
0x6C4479 0F 8E C8 00 00 00  jle 0x6C4547        ; <=0 -> SM_DEALCHGGOLD_FAIL
0x6C4547 80 7D FF 00        cmp byte [ebp-1],0
0x6C456B 66 BA AD 02        mov dx,0x2AD        ; SM_DEALCHGGOLD_FAIL
```
  代码已按 `<= 0` 改对；`< 0` 会让 `nGold==0` 落到成功分支（ident 不同 + 可无限重置
  `m_DealLastTick` 拖单）。
- 处置：断言改成钉 `nGold <= 0`，并**新增**反向断言禁止退回 `< 0`。
- 已修复：✅ `ca432edc`

### 2.9 DeathDropPolicyCheck — (c) 闸门在扫注释
- 报错：`0x7400F8 mov eax,3: 背包爆率分母硬编码 3，不得引入配置旋钮`
- 根因：反臆造闸门 `!bagSource.Contains("nDieScatterBagRate")` 命中的是**注释**
  （`TPlayObject.Base.cs` 里那段解释「这里原先读 g_Config.nDieScatterBagRate…」的说明文字）。
  代码本身用的正是 `M2Share.RandomNumber.Random(3)`。
- 处置：该条改成只读去注释后的代码。断言语义未动。
- 已修复：✅ `f690c015`

### 2.10 DelphiRandomDormantCompatCheck — (c) 闸门在扫注释
- 报错：`gameplay facade took its own System.Random back`
- 根因：同上。`SystemModule/RandomNumber.cs` 的类头注释里写着
  「…the \`private static Random random\` field this class no longer owns」，
  `Reject()` 用裸 `Contains` 就把这句**说明其已被移除**的句子当成了它的复活。
- 处置：`Reject()` 内部先去注释。（`Require()` 未动。）
- 已修复：✅ `f690c015`

### 2.11 DispatcherProtocolExactCheck — (a) + (c)
- 报错：`RM_10414 param: expected=1, actual=52719`（52719 = 0xCDEF = HP 低字）
- **关键事实**：Delphi register 调用约定把溢出到栈的尾部实参**按左到右压栈**。
  姊妹臂 `SM_OPENHEALTH` 把这一点钉死：它压 `HP.lo / MaxHP.lo / 0 / 0` 进
  `(wParam, wTag, wSeries, sMsg)`；若按右到左读，字符串形参会收到一个 HP 数值，不可能。
- 三处 push 序列：

```
RM_10414               0x6B5C38 66 8B 86 AC 02 00 00 mov ax,[esi+0x2AC] / 50 push  ; Param=HP.lo
                       0x6B5C40 66 8B 86 B0 02 00 00 mov ax,[esi+0x2B0] / 50 push  ; Tag=MaxHP.lo
                       0x6B5C48 6A 01 push 1                                        ; Series
                       0x6B5C4A 8D 45 F0 / 50 (Buf) / 0x6B5C4E 6A 08 (Len)
                       0x6B5C53 66 BA 4F 04 mov dx,0x44F                            ; 1103
RM_SENDGOODSLIST       0x6B5277 66 8B 43 08 / 50 (Param) / 0x6B527C 6A 00 (Tag)
                       0x6B527E 6A 01 (Series) / 0x6B528B 66 BA 85 02 mov dx,0x285  ; 645
RM_SENDDETAILGOODSLIST 0x6B538F 66 8B 43 08 / 50 (Param) / 0x6B5394 66 8B 43 0C / 50 (Tag)
                       0x6B5399 6A 00 (Series) / 0x6B53A6 66 BA 8C 02 mov dx,0x28C  ; 652
```
- 处置：三处 `Packet(...)` 期望与三处源码文本断言把 Param / Series 换回正确位置。
- 另 (c)：`FindRepoRoot()` 只从 CWD / `AppContext.BaseDirectory` 往上找 `LyoMir2.sln`，
  而 csproj 把 OutputPath 指到仓库外的 `..\..\..\Build\AuditTools\`，必然找不到根；
  补一条 `[CallerFilePath]` 兜底起点。
- 已修复：✅ `6f01d51d`

### 2.12 DispatcherStateExactCheck — (a) + (c)
- 报错：`SM_SENDUSERSTATE param: expected=1, actual=0`
- 同上的左到右压栈：

```
0x6B7119 6A 00 push 0            ; Param
0x6B711B 6A 00 push 0            ; Tag
0x6B711D 6A 01 push 1            ; Series
0x6B711F 8B 45 F8 / 50           ; Buf
0x6B712D 50                      ; Len
0x6B712E 33 C9 xor ecx,ecx       ; Recog = 0
0x6B7130 66 BA EF 02 mov dx,0x2EF ; 751
```
- 处置：`Packet(...)` 与源码文本断言均改为 `Param=0 / Tag=0 / Series=1`；`FindRepoRoot` 同样补
  `[CallerFilePath]` 兜底。
- 已修复：✅ `6f01d51d`

### 2.13 DynRoomEventActivationCheck — (c) 夹具触不到被测路径
- 报错：`closed activation event did not reach the manager closed list`
- 根因：`EventManager.Run()` 只在距上次扫描 > 250 ms 时才走活动列表，而构造函数用
  `GetTickCount()` 播种该时间戳。测试创建 manager 后**微秒级**就调 `Run()`，那一趟必然空转，
  关闭事件的迁移从未执行。
- 处置：夹具反射把 `_runTick` 老化 251 ms，让被断言的那一趟真正执行。断言未动。
- 已修复：✅ `0fb7b0f6`
- ⚠ 移交：那个 250 ms 节流**无字节锚**，见 §4.2。

### 2.14 ExactEnvironmentMoveCheck — (b) 真回归
见 §1 R1。已修复 ✅ `7f322f3f`

### 2.15 GateLegacyType18CompatCheck — 部分修复 + **BLOCKED**
- 报错（原）：`internal frame length at discriminator: expected 32, got 18`
- 已修复的一半 (a)：断言仍按旧布局（24 字节头 / 长度在 +0x0C / Cmd 在 +0x0E）写死
  `[12..13]==32`。现布局是 16 字节头、`+0x0C`=Cmd、`+0x0E`=BodyLen
  （`0x637AC1 mov [eax],77BBAA33` / `0x637AC7 66 89 78 0C mov [eax+0x0C],di` /
  `0x637AD7 66 89 58 0E mov [eax+0x0E],bx` / `0x637ADE add [x+0x184],0x10`）。
  改成断言 `[12..13]==18`（Cmd）与 `[14..15]==8`（BodyLen，低于 legacy 的 12 字节
  `ClientPacketSize`，这正是它保持 internal 的原因）。
- **仍 BLOCKED 的一半**：改完后暴露出下一条 —— `frames.Count` 期望 1，实得 0，且 8 字节残留。
  `GameGateServerFrameParser` 对「Cmd==18 但 payloadLength < 12」的处理是
  `scan = marker + 1` 逐字节重同步，于是撞上载荷里刻意埋的 `77 BB AA 33`，把该帧整个吞掉。
  测试期望的是「投递为 internal 帧」。
  - 两边都**没有字节锚**：legacy type-18 与 InternalPacket77 共用同一 16 字节信封与同一判别位，
    区分只能靠 body 长度；「短 body ⇒ 视为损坏并重同步」还是「⇒ 投递为 internal」是 C# 自定的恢复策略。
  - 另注：GameGate 2025 转储（`_gg_reunpack_work\dump_gg2025\flat_image.bin`，34 MB）里
    字节序列 `77 BB AA 33` **0 命中**，即该网关底本根本不说 77BBAA33，无法从网关侧取证。
  - 可达性：生产流上 Cmd 只取 `GM_*`(1..7)，`Cmd==18` 只可能是 legacy 帧，故此场景不可达，
    两种策略风险都为零 —— 但按铁律不做无据裁定。
  - 建议：由网关车道在有 GameGate 原生底本时定夺；或明确记为「C# 恢复策略，无原生对应」。
- 已修复：部分（第一条），工具仍 FAIL。

### 2.16 GloryMutationCompatCheck — (a) 夹具绑错字段
- 报错：`generic Give GloryPoint business log count: expected 2, actual 1`
- 根因：夹具只设了脚本上下文 `bridge.CurrentNpc`，没设 `player.m_NPC`。原生的审计门读的是
  被点击的 NPC（player+0xCD8）：

```
0x6DF341 83 BF D8 0C 00 00 00  cmp dword [edi+0xCD8],0
0x6DF348 0F 84 06 01 00 00     je  0x6DF454      ; nil -> 不发 经验/内功经验/荣耀点 审计
0x6DF34E 8B 87 D8 0C 00 00     mov eax,[edi+0xCD8]
```
  而点击处理器是在派发 vcall **之后**才写该字段：

```
0x6B8BA2 E8 9D 78 06 00        call 0x720444
0x6B8BA7 89 B3 D8 0C 00 00     mov [ebx+0xCD8],esi
```
- 处置：夹具同时绑 `player.m_NPC`。断言未动。
- 已修复：✅ `c2f07a0d`

### 2.17 HeroDbCheck — (a) 夹具触不到被测原生路径
- 报错：`truncated dynData must keep already-parsed sections (0x68B0C9 jl 0x68B354)`
- 根因：夹具只砍掉最后一字节却不改根长度字段，于是先撞上**上一行自己刚断言过的**
  「根长度不符必须拒绝」，分节级边界检查从未执行。
- 字节证据（分节级边界确如断言所述）：

```
0x68B097 BE 07 00 00 00     mov esi,7                  ; 头 7 = 魔数4+长度2+类型1
0x68B0A9 3B F3 / 0F 8D ...  cmp esi,ebx / jge 0x68B3F3 ; ebx = 声明的根长度
0x68B0B3 81 38 AA EF CD AB  cmp dword [eax],0xABCDEFAA
0x68B0B9 0F 85 D7 02 00 00  jne 0x68B396               ; 坏魔数 -> 记日志退出
0x68B0C1 0F B7 40 04        movzx eax,word [eax+4]
0x68B0C5 03 C6              add eax,esi
0x68B0C7 3B D8              cmp ebx,eax
0x68B0C9 0F 8C 85 02 00 00  jl  0x68B354               ; 载荷不足 -> 记日志退出
```
  两条退出都保留已解析分节，C# 解码器行为一致。
- 处置：截断后同步修正根长度字段。断言未动。
- 已修复：✅ `631c4012`

### 2.18 HeroLifecycleCheck — (a)（主代理预判为真回归，实为陈旧断言）
- 报错：`PAS HeroRename no longer enters the native 0x164 path`
- 根因：断言要求 `CallStandaloneFunction` **且** `CallPlayerFunc` 都调 `RequestRename`，
  而代码已把 `herorename` 搬到 `CallNpcFunc`。
- 字节证据（HeroRename 确实是 TPsNpc 的方法，不是全局函数也不是 TPlayer 方法）：

```
0x73466F 8B 5E 1C           mov ebx,[esi+0x1C]         ; 编译器
0x734674 BA B4 52 73 00     mov edx,0x7352B4           ; 'TCreature'（父类）
0x734682 B9 C8 52 73 00     mov ecx,0x7352C8           ; 'TPsNpc'
0x734687 E8 58 AA DD FF     call (AddClassN)
0x73468C 8B D8              mov ebx,eax                ; ebx = TPsNpc 编译期类
  —— 区间 0x73468C..0x734EA0 内 0 处 `8B 5E`（重载编译器）、仅 1 处 `8B D8`，
     其间 167 条 `mov edx,<decl> / mov eax,ebx / call 0x510F00` 方法声明 ——
0x734E89 BA A0 7A 73 00     mov edx,0x737AA0
         0x737AA0 = 'function HeroRename(player: TPlayer; const oldName, newName: string): Integer;'
0x734E90 E8 6B C0 DD FF     call 0x510F00              ; RegisterMethod
0x739213 B9 AC A4 73 00     mov ecx,0x73A4AC ('HeroRename')  ; 运行期唯一名字绑定
```
- 处置：断言改为钉 `CallNpcFunc`，并**新增**反向断言禁止它再被导出到 standalone / player 面。
- 已修复：✅ `3e283c9c`

### 2.19 HeroPasAdminCheck — (a)（断言钉的是臆造 API）
- 报错：`TakeFromHeroBagEx did not share the native mutation path`
- 字节证据：

```
TakeFromHeroBagEx   0 命中
TakeHeroBagExItem   0 命中
0x72D439 'function GetHeroBagItemCount(const ...'   / 0x732894 'GetHeroBagItemCount'
0x72D481 'function TakeFromHeroBag(const ItemName: string; ItemCount: Byte): Boolean'
0x7328B0 'TakeFromHeroBag'
```
  代码把 Ex 两个名字 fail-closed 是对的。
- 处置：断言反过来钉「必须拒绝且不得改背包」，并把随后的存档活动记录数 1 → 2
  （夹具连带值：四条入包，只接受了 `TakeFromHeroBag(普通药,2)`）；PASS 横幅 `ex=shared` → `ex=fail-closed(0-hit)`。
- 已修复：✅ `631c4012`

### 2.20 HeroTailProtocolCheck — (c) 环境
- 报错：`Could not find a part of the path '...\bin\Debug\net8.0\DBSvr\DB\impl\MySqlHeroRecordService.cs'`
- 根因：`CheckPersistenceSource` 用 `Directory.GetCurrentDirectory()` 读 DBSvr 源码，
  而 sweep 把工作目录设成 exe 自己的 bin 目录。
- 处置：补 `FindRepositoryRoot()`。断言未动。
- 已修复：✅ `4c92c44b`

### 2.21 HeroUnionStateCheck — (a)
- 报错：`expected=权限不够!!!, actual=该命令需要5级GM才能使用`
- 字节证据：`"权限不够"` 全镜像 **0 命中**；原生回复是两段相邻常量拼接：

```
0x62B760 FF FF FF FF 0A 00 00 00 B8 C3 C3 FC C1 EE D0 E8 D2 AA        = "该命令需要"  (10)
0x62B774 FF FF FF FF 0C 00 00 00 BC B6 47 4D B2 C5 C4 DC CA B9 D3 C3  = "级GM才能使用" (12)
```
- 处置：断言改为按原生形态拼接，等级取命令自身的 `nPermissionMin`。
- 已修复：✅ `631c4012`
- 备注：`MovementReliveCheck`（不在本队列）红的是**同源**问题
  （`SetNoKillMapLv GM permission contract changed`），可照此修。

### 2.22 InProcEngineRunCheck — (a) ×3
1. **怪物 race**：直接 `new Monster` 不走 `MonInitialize`，保留 `TMonster.Create` 自己的默认 80，
   不是父类写的 50。

```
TBaseObject 0x764E5F C6 86 78 01 00 00 32  mov byte [esi+0x178],0x32  ; 50
TAnimal     0x71D851 C6 87 78 01 00 00 32  mov byte [edi+0x178],0x32  ; 50
TMonster    0x666162 C6 86 78 01 00 00 50  mov byte [esi+0x178],0x50  ; 80
```
2/3. **属性定价 / 堆叠定价**：测试那份「独立复算」用的是裸模板价，漏了原生的 ×1.1 回退。
   夹具 NPC 没有价格表行，原生走 `sub_63F3B4` 的模板价分支：

```
0x63F411 7F 2F              jg 0x63F442            ; 表价 >0 才原样用
0x63F42F DB 40 3C           fild dword [eax+0x3C]  ; 模板 Price
0x63F432 DB 2D 68 F4 63 00  fld xword [0x63F468]   ; CD CC CC CC CC CC CC 8C FF 3F = 1.1
0x63F438 DE C9              fmulp st(1)
0x63F43A E8 35 41 DC FF     call 0x403574          ; @ROUND
```
   故基础价是 `ROUND(1000*1.1)=1100` 与 `ROUND(100*1.1)=110`：
   属性剑 n14=1 → `1100 + (1100 div 5)*1 = 1320`（不是 1200）；堆叠药 → `110*qty`；非堆叠剑 → 1100。
   被测算术本身（`0x783E86 03 F8 add edi,eax` 的 `n10 + (n10 div 5)*n14`、`0x63F458 imul` 的
   `× 数量`）一字未动，仍逐条钉住。
   后两条此前被第一条挡住，从未执行。
- 已修复：✅ `5598e383`

## 3. 残余非零退出码（不是断言失败）

`DbGateRegressionCheck` 现在 in-process 42 项全过，退出码 2 来自它**自己的 INCOMPLETE 语义**：

```
INCOMPLETE: 0 failed, 4 of the 4 live-DB / native-round-trip checks never ran:
  - live DB canonical character index (read-only)      (needs --db-ini <value>)
  - native Gs1 character records (read-only)           (needs --native-port <value>)
  - native character save/reload preservation          (needs --native-write-port <value>)
  - native startup cleanup boundary                    (needs --native-cleanup-port <value>)
```

这是**诚实**的设计（明说「没跑活库就不算建立字节等价」），我**没有**把它改成 exit 0 —— 那会造假绿。
建议改 sweep 侧：`_run_audittools.ps1` 把「退出码 2 且 stderr 以 `INCOMPLETE:` 开头」归为 SKIP 而非 FAIL。

## 4. 移交主代理的「无字节锚」发现（未改代码）

### 4.1 网关闸门配置整片疑似非战神
战神 `!Setup.txt` 的键表在 `0x794560` 起，逐条是
`ServerIndex / Server / ServerName / GMSuperCode / TestServer / DBAddr / DBPort / GCAddr /
GCPort / YBDBAddr / LogServerAddr / LogServerPort / BaseDir / Share / GuildDir / ...`
—— **没有 CheckBlock**。全镜像对
`CheckBlock / SendBlock / AvailableBlock / GateLoad / HumLimit / SocLimit / UserFull / ZenFastStep`
**均 0 命中**，只有 `ServerName` 命中 1 次。
即 `GameSvrConfig` 里这一整片闸门/上限配置疑似来自别的 Mir2 分支（GameOfMir/ref-MIR2）而非战神，
连带 `GateService.DrainPendingSendBuffersCoreLocked` 的 `nCheckBlock * 10` 门限也无锚。建议网关车道复核。

### 4.2 EventManager 的 250 ms 节流无字节锚
`GameSvr/Events/EventManager.cs:18` 的 `if ((uint)(currentTick - _runTick) > 250u)`：
全镜像搜 `cmp reg,0xFA`（`83 F8 FA` / `3D FA 00 00 00` / `83 FB FA` / `83 FE FA`，
以及 image-wide `83 F8 FA` + 条件跳）**均 0 命中**。同一函数里下半段的回收循环
（`sub_718724`，`0x718761 cmp eax,0x493E0` = 300000 ms、`0x7187CE cmp edi,0x0A` = 每轮 10 条）
是有锚的，唯独这道 250 ms 前置门没有。另外构造函数用 `GetTickCount()` 播种 `_runTick`
（Delphi 对象字段是零初始化，原生首帧应当立即执行）。建议事件车道复核。

### 4.3 `TrySpaceMoveToEnvironment` 的整套 try/回滚本身无原生对应
见 §1：`sub_6BD294` 无 SEH 帧、镜像无 `"SpaceMove"` 异常串。本次只把**提交点**挪到正确位置
（这条有证据支持），没有拆掉 C# 自加的稳健层。是否整体退成「无回滚」由移动车道定夺。

## 5. 提交清单（分支 `w/atriageA`）

| SHA | 说明 |
|---|---|
| `3e283c9c` | ChatShield 偏移 0x4F8 / 掉落分母括号配平 / HeroRename 注册面；另加三个分诊脚本 |
| `4c92c44b` | 三个 harness 路径缺陷（ActiveOutgoingProtocol / HeroTailProtocol / DbSvrServiceRegression）|
| `43659d30` | DbGateRegressionCheck 三处陈旧断言 |
| `5598e383` | InProcEngineRunCheck 三处陈旧断言 |
| `2da7457c` | CSharpGateM2IntegrationCheck 流控夹具参数 |
| `6f01d51d` | Dispatcher 两工具 Param/Tag/Series 次序 + FindRepoRoot 兜底 |
| `631c4012` | Hero 三工具（HeroDb / HeroPasAdmin / HeroUnionState）|
| `ca432edc` | AddPlayerAbilType6 / DealValidation |
| `f690c015` | DeathDropPolicy / DelphiRandomDormant 反臆造闸门改成只读代码 |
| `0fb7b0f6` | DynRoomEventActivation 夹具老化 `_runTick` |
| `7f322f3f` | **fix(MOVE)** 传送提交点（唯一的代码改动）|
| `c2f07a0d` | GloryMutationCompat 夹具绑 `player.m_NPC` |

## 6. 铁律遵守说明

- 全程**没有**为了变绿而删除或弱化断言。改测试的每一处都附了战神字节，且多处是**加强**：
  - `ChgMonItemPercentStaticCheck` 额外钉住 RNG 接收者 `M2Share.RandomNumber`；
  - `DbGateRegressionCheck` 把单点仓库容量断言扩成 24/48/49/192 四点边界表；
  - `HeroLifecycleCheck` 新增「不得再导出到 standalone/player 面」的反向断言；
  - `HeroPasAdminCheck` 新增 `TakeHeroBagExItem` 也必须 fail-closed；
  - `DealValidationFailureSemanticsCheck` 新增「不得退回 `< 0`」反向断言；
  - `AddPlayerAbilType6CompatCheck` 正则额外钉住 `OnNativeTimedStateLost` 调用点。
- 无法用字节裁定的两处（GateLegacyType18 的短 body 恢复策略、DbGate 的活库检查）
  **保持红/保持 INCOMPLETE**，没有粉饰。
- 未触碰热点文件 `SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、
  `GameSvr/UsrSystem/UsrEngn.cs`。
