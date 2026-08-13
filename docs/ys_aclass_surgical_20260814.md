# 眼神 A 类 28 条外科缺口 · 逐条裁决

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\ys-aclass`　分支：`w/ys-aclass`　基线：`be5126b0`（建树时的 `master`）
- 上游：`docs/yanshen_completeness_audit_20260814.md` §5 A 类
- **注意**：建树之后 `master` 又推进了 11 个提交（`be5126b0..fb71c026`），其中
  `94ede6dd`/`285add26`/`b676be88`/`bd77fcaa`/`d72cc932` 与本分支处理同一批 A 类条目。
  本文与之**有一处实质冲突**（§3.4，带毒五键的字节佐证）和若干重复结论；
  唯一在 master 上仍未修的是 A8（§8，`Yanshen207ProtocolCheck` 至今还红着）。
- 底本：`staging/_reunpack_work/flat_image.bin`（base `0x400000`）、
  `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin`（base `0x10000000`）、
  同目录 `M2Server_exe.memory.bin`（活进程转储，base `0x400000`）
- 工具：python 3.11 + capstone 5.0.7

---

## 0. 本轮最大的一件事：trampoline 安装器 ABI 被解开，桩体可以逐字节重建

在此之前，眼神的 93 个 trampoline 只能靠「哪个键挂哪个 VA」这一层元数据描述，
桩体里到底干了什么全靠推断。本轮把两个 builder 反完，桩体现在可以**机械重建**。

### 0.1 ABI

`0x10032CC0`（71 站点）与 `0x10032FD0`（30 站点）同签名：

```
[ebp+08] 出参结构   [ebp+0C] patch VA   [ebp+10] target VA
[ebp+14] resume VA  [ebp+18] 模板指针   [ebp+1C] 模板元素个数
```

### 0.2 模板编码

模板是「**一个 dword 装一个字节**」的数组。装配循环 `0x10032DB0`：

```
10032DB0  mov  ebx,[ebx+edi*4]        ; 取元素
10032DB3  cmp  ebx,0xE9 / 0xE8        ; 是 jmp/call 操作码？
10032DC9  mov  ecx,[tmpl+edi*4+4]     ; 取下一个元素
10032DCD  cmp  ecx,0xFF / jle 跳过    ; >0xFF 才是绝对 VA
10032DD5  lea  eax,[esi-1]/cmp edi,eax/jge 跳过   ; 最后一个元素不重定位
10032DE2  sub  ecx,edx                ; - 当前输出偏移
10032DE4  sub  ecx,[stub base]        ; - 桩体基址
10032DEA  add  ecx,-5                 ; rel32 = tgt - (base+off) - 5
```

即：元素为 `0xE8/0xE9` 且下一元素 `> 0xFF` 时，下一元素是绝对 VA，被改写成 rel32。
**最后一个元素**（通常是 `0xE9`）的 rel32 由后段用 `[ebp+0x14]` 补，所以模板尾部的
`0xE9` 看上去是「孤儿」。

### 0.3 模板从哪来

两种，都可回收：

1. **movaps 常量拼装**（小桩体）：若干 `movaps xmm0,[.rdata]` + `movups [ebp-X],xmm0`，
   外加零星 `mov dword [ebp-Y],imm` / `xorps` 清零。
2. **`rep movsd` 整块拷贝**（大桩体）：`mov esi,<rdata VA>` / `lea edi,[ebp-X]` /
   `mov ecx,<count>` / `rep movsd`。

复现脚本 `%TEMP%\ys_stub.py`（只读；给它一个 builder 调用点 VA 就吐出反汇编后的桩体）。

### 0.4 校准

用 `@OnDie`(`0x100AD427`) 做对照：重建出的 40 字节桩体在 `+0x22` 处原样重放了被覆盖的
`5F 5E 5B 59 59`，与注册表记的 `Action = Notify` 一致；而 `@OnBackButton` 重建出的
37 字节桩体里**没有**被覆盖的那条 call —— 方法能区分两者，不是把所有桩体都读成同一种。

---

## 1. 逐条判定（28 条）

| 组 | 条目 | 判定 | 依据 |
|---|---|---|---|
| A1 | 回城按钮触发 | **已接线（并更正语义）** | §2.1 |
| A1 | 上线触发 @initys | 证据齐、**落点缺失** | §2.2 |
| A1 | 复活触发脚本 @OnDia | 证据齐、**宿主子系统 C# 未实现** | §2.3 |
| A1 | 英雄穿戴 / 新穿戴 | 仍 BLOCKED（参数字段切分无据） | §2.4 |
| A1 | 心灵启示 / 被击杀 / 捡物 / 攻击 / 魔法攻击 / 盘古魔法攻击 / 英雄倍攻和暴击 | 未解（桩体未重建或落点未定） | §2.5 |
| A2 | 刀刀切割 @Cutting | 未解 | §2.5 |
| A3 | 带毒五键 | **fail-closed，且更正一处「五键同构」的错记** | §3 |
| A4 | 野蛮麻痹 | **fail-closed** | §4 |
| A5 | 噬魂沼泽绿毒修复 | 桩体+宿主全解，**C# 合击区域管线形状不同**，不接 | §5 |
| A6 | S(1,1..150) 播种 | **内容已逐字节证实，时机不可证** | §6 |
| A7 | 激光 S(1,82) | **已接线** | §7.1 |
| A7 | 激光 S(1,81) | **已证实，但 C# 无对应槽，不接** | §7.2 |
| A8 | 装备槽 StdMode 裁决 | **已裁决：扩展号无据** | §8 |
| A9 | 四个审计工具 | **四个全绿** | §9 |

---

## 2. A1 触发族

### 2.1 回城按钮触发 @OnBackButton —— 已接线，且原记的 `Notify` 是错的

安装参数（`0x100AD5BB..0x100AD628`）：`push 0x21`(33 元素) / `push lea[ebp-0x7F4]` /
`push 0x6DBB85` / `push 0x6DBB80` ×2 / `push lea[ebp-0xC4]`。

重建出的 37 字节桩体：

```
+000 60              pushal
+001 8BD0            mov edx,eax             ; This_Player = 分发器 [ebp-4]
+003 A1205D7D00      mov eax,[0x7D5D20]      ; TTaskAdmin
+008 8B00            mov eax,[eax]
+00A 8BF0            mov esi,eax
+00C 8B7E08          mov edi,[esi+8]         ; TSTDScript
+00F 68 <@OnBackButton>
+014 6A00            push 0
+016 33C9            xor ecx,ecx
+018 8BC7            mov eax,edi
+01A 8B18            mov ebx,[eax]
+01C FF5344          call [ebx+0x44]         ; Plain 槽
+01F 61              popal
+020 E9 -> 0x6DBB85
```

被覆盖的 5 字节是 `0x6DBB80 E8 E7 D6 01 00 call 0x6F926C`，桩体**不重放**；
续跑点 `0x6DBB85` 正是分发器默认的 `jmp 0x6DBC2C`。⇒ **顶掉型**。

落点：`sub_6F926C` 全镜像只有 1 个 rel32 调用者（就是 `0x6DBB80`），因此
「在唯一调用点跳过这次 call」≡「进函数就返回」。门放在
`TPlayObject.ClientClickBackHome()` 第一条语句，不必改 `TPlayObject.Message.cs`。
宿主↔C# 的对应由三道门互证：`[map+0x7C]`/`[map+0x6C]` 两 bool、状态 `0x33` 配
`[player+0x3C0]`、状态 `0x34`。

### 2.2 上线触发 @initys —— 桩体全解，但 C# 没有等价落点

81 字节桩体（builder site `0x100D5968`）：

```
+000 cmp [ebp+4],0x6542D2   jne out     ; 只认 0x6542CD 那一次调用
+00D cmp ebx,0x410000       jb  out
+019 cmp [ebx],0x6AC8C8     jne out     ; 只对 TPlayObject
+025 pushal / pushfd
+027 mov edx,ebx                        ; This_Player
+029 …取 TSTDScript… push '@initys' / push 0 / xor ecx,ecx / call [ebx+0x44]
+045 popfd / popal
+047 5F 5E 5B 8B E5                     ; 重放被覆盖的 5 字节
+04C E9 -> 0x6548C2
```

`sub_654748` 全镜像只有 1 个 rel32 调用者（`0x6542CD`），所以 `[ebp+4]` 那道门是冗余的。
**但 `sub_654748` 本身尚未定名**：它的调用者 `sub_654140` 用
`movzx eax, word [[ebp-4]]` / `cmp eax,0x61` 做分发，与桩体要求的
`[ebx]==0x6AC8C8`（同一个 `[ebp-4]`）表面矛盾，说明该 switch 中途重写过 `[ebp-4]`，
本轮没有把这条 arm 走通。在把 `sub_654748` 定名到具体的 C# 上线阶段之前接 `@initys`，
等于猜一个时机 —— 不做。

> 注册表里原写的「【故意不接】等回收链修好」这条理由**已经失效**（M1 在 `186ef170`
> 已接通），现在挡路的是落点未定，不是风险。

### 2.3 复活触发脚本 @OnDia —— 宿主子系统 C# 根本没有

63 字节桩体（builder site `0x100D1DE6`）：先重放被覆盖的 6 字节
`31 D2 52 50 8B C6`（注意用的是 `31 D2` 而不是原生的 `33 D2`，同义不同编码），
再 `test ebx,ebx / je out`、`cmp [ebx],0x6AC8C8 / jne out`，然后 This_Player=ebx
发 `@OnDia`，末尾 `jmp 0x73C48A`。⇒ **Notify**，且重放在派发之前。

宿主 `sub_73C208`（3 个调用者：`0x68A45A` / `0x68A5B8` / `0x6B2DE0`）是**复活祝福**
的周期处理：`+0x1B8` 是否拥有、`+0x450` 起算 tick、`+0x594`/`+0x595` 两个一次性提示位、
`0xE678`(59 s) 与 `0xEA60`(60 s) 两道门，文案是
`"您将在 " + N + " 秒后获得一次复活机会"` 与
`"您获得了复活的祝福，死亡后将获得一次复活机会"`。

**这两句话在 C# 全树 0 命中，整个复活祝福子系统没有实现。** 没有宿主就没有同位点，
接 `@OnDia` 只能是凭空造一个时机。保持不接。

### 2.4 英雄穿戴 / 新穿戴 —— BLOCKED 理由未变

六个 Variant 里 ②`dword [item+0x28]`、③`dword [item+0x2C]`、④`dword [[item+0x1C]+0x14]`
的字段切分仍无逐字节证据（②③跨 208 字节物品记录的 `DuraMax`/`btValue` 边界）。
本轮没有新增证据，维持只留档不发射。

### 2.5 其余 7 条 + @Cutting

| 条目 | 宿主 | 现状 |
|---|---|---|
| 心灵启示触发 | `0x6EDC2B`（顶掉 `call 0x774424`） | 桩体未重建；2 参未解 |
| 被击杀触发 | `0x766624` | 同上（2 参） |
| 捡物触发 | `0x6B770C` | 同上（2 参） |
| 攻击触发 | `0x76E35D` | 同上（4 参） |
| 魔法攻击触发 | `0x76DE84`（紧接 `call [esi+0x104]` 伤害虚调用之后） | 同上（5 参） |
| 盘古魔法攻击触发 | `0x76E1AF` / `0x76DEC0`（两处都是 `cmp byte [obj+0x1B6],0`） | 同上（3 参） |
| 英雄倍攻和暴击 | `0x76C816`（`sub_76C804` = GetAttackPower 入口，覆盖 7 字节 `cmp [ebx+0x84],0`） | 模板 354 元素 @ `.rdata 0x102C8DA0`，`rep movsd` 已支持，本轮未走完 |
| 刀刀切割 @Cutting | `0x767BAE` | 703 dword 大桩体，主体是就地算伤害 |

`%TEMP%\ys_stub.py` 现在支持这两类模板，重建这 8 条是纯机械工作，只是本轮没排上。

---

## 3. A3 带毒五键 —— fail-closed，并更正「五键同构」

### 3.1 四键确实同构

`半月带毒`(`0x7720FB`) / `物功带毒`(`0x76E2BC`) / `雷电带毒`(`0x76EB1D`) /
`法师群毒`(`0x76E1A9`) 的桩体主体逐字节相同（106 字节）：

```
mov eax,1 / call 0x403B4C          ; Random(1) —— 恒 0
test eax,eax / jne 跳过绿毒
mov eax,[<玩家>+0x18CC] / and eax,2 / test / je 跳过绿毒
push 0x1F / push 0x0F / push <玩家> / push 5 / push 0 / push 0x3E8
mov cx,0x283C / mov eax,<对方> / call 0x766060
--- 绿毒结束 ---
mov eax,5 / call 0x403B4C          ; Random(5) —— 1/5 命中
test eax,eax / jne 跳过红毒
mov eax,[<玩家>+0x18CC] / and eax,4 / test / je 跳过红毒
push 0x1E / push 0x0F / push <玩家> / push 5 / push 0 / push 0x3E8
mov cx,0x283C / mov eax,<对方> / call 0x766060
```

`sub_766060` 的签名由其序言定死（`0x76606D mov [ebp-4],edx`、`0x766069 mov [ebp-6],cx`、
`0x7660B9 mov ax,[ebp+8]` → `word [rec+0x16]`）：

```
SendDelayMsg(Self=eax, BaseObject=edx, wIdent=cx,
             wParam=[ebp+1C], nParam1=[ebp+18], nParam2=[ebp+14],
             nParam3=[ebp+10], sMsg=[ebp+0C], dwDelay=[ebp+08])
```

代入 = `SendDelayMsg(…, wIdent=10300, wParam=31/30, nParam1=15, nParam2=玩家,
nParam3=5, '', 1000)`。与 `docs/state19_rm_poison_faithful_20260814.md` 已钉死的
「原生毒系 = 延迟消息(10300,1000ms) → MakePosion → AddState」完全对得上，
`31/30` 就是该文 §3 里 `31-nType` 的净落态（31=绿，30=红）。

### 3.2 但 `武器绿毒` **不**同构 —— 上游记错了

`武器绿毒`（builder site `0x100B2199`，与 `物功带毒` 同宿主 `0x76E2BC`）的桩体只有
50 字节，**没有 Random、没有 `+0x18CC`**：

```
+002 cmp byte [ebx+0x2D4],0x64   jne 跳过
+00B push 0x1F / push 0x0F / push ebx / push 5 / push 0 / push 0x3E8
+019 mov cx,0x283C / mov eax,esi / call 0x766060
+026 cmp byte [ebx+0x1B4],0      ; 重放被覆盖的 7 字节
+02D E9 -> 0x76E2C3
```

⇒ 上游 `ys_skills_impl §1.1`「同构于半月带毒」与主审计 §5「RNG 顺序已定：先 Random(1)
… 再 Random(5)」对 `武器绿毒` 这一条**不成立**。两键打同一个 VA 但装的是两套不同桩体，
「后安装的覆盖先安装的」这条互斥要求因此比原先记的更要紧。

### 3.3 为什么仍然 fail-closed

四键同构桩体的毒源开关是 `[player+0x18CC]` 的 bit1 / bit2。对这个位移做全量普查：

| 镜像 | `disp32 == 0x18CC` 的指令数 |
|---|---:|
| M2Server 宿主 `flat_image.bin` | **0** |
| 眼神 2.0.8 转储（45 MB） | **1**（`0x1008FFD3 mov edx,[eax+0x18cc]`，是个读） |

宿主一次都不碰，插件镜像里也只有一处读、**零处写**。写点只可能在 Themida 虚拟化区
（C1）或运行期现搭的桩体里，静态不可得。⇒ C# 无从知道这两个位何时置起来，
接线就得自己发明一套"武器带毒位"的来源 = 臆造。**保持不接。**

（`武器绿毒` 的 `[ebx+0x2D4]` 倒是宿主真字段——`docs/eqv_shard09` 已把
`+0x2C4/+0x2C8/+0x2CC/+0x2D0/+0x2D4/+0x2D8` 钉成三对负重字段，`+0x2D4` = `HandWeight`。
但 `cmp byte [ebx+0x2D4],0x64` 是拿 32 位负重的**低字节**和 100 比，语义讲不通，
本轮不敢据此接线。）

### 3.4 ⚠ 与 master 上已落地的 `YanshenPoisonKeys.cs`（`94ede6dd`）冲突

该文件的类注释写着：

> 审计设想的「`[+0x18CC]&2/&4` + `Random(1)/Random(5)` + `call 0x766060` +
> `ident 0x283C`」绿/红毒机制，在本转储中**位移 0x18CC 命中 0 次、ident 0x283C 命中 0 次**
> （`cc180000`/`3c280000` 全库零命中）→ 无字节佐证 → fail-closed，不得凭空实现。

**这个「0 命中」是搜索方法造成的假阴性，机制确实存在。** 原因：这些字节根本不以
机器码形态存在于镜像里 —— 它们躺在 `.rdata` 的**模板**里，一个 dword 只装一个字节，
而且以 16 字节为一块被 `movaps` 分散拼装（见 §0.2/§0.3）。搜 4 字节的机器码形态
必然落空。

把 `半月带毒`(`0x100B2E4A`) 的模板按实际写入顺序打印出来，`0x18CC` 就在明处：

```
elem[ 12..] <- 26000000 8B000000 83000000 CC000000
elem[ 16..] <- 18000000 00000000 00000000 83000000     ; 8B 83 CC 18 00 00 = mov eax,[ebx+0x18CC]
elem[ 20..] <- E0000000 02000000 85000000 C0000000     ; 83 E0 02 85 C0    = and eax,2 / test eax,eax
elem[ 24..] <- 74000000 19000000 6A000000 1F000000     ; 74 19             = je 跳过绿毒
elem[ 40..] <- 66000000 B9000000 3C000000 28000000     ; 66 B9 3C 28       = mov cx,0x283C
elem[ 44..] <- 8B000000 C6000000 E8000000 60607600     ; 8B C6 / E8 -> 0x00766060
elem[ 60..] <- 83000000 CC000000 18000000 00000000     ; 红支同一读，and eax,4
```

三个独立可复跑的佐证：

1. 直接搜 dword-elem 形态的 `CC000000 18000000`（即 `0x18CC` 被拆成两个元素）
   命中 `0x102D1324` / `0x102D1334` —— 正是上表跨块的那两处。
2. 搜「`0xE8` 元素 + 绝对 VA `0x00766060` 元素」这一对（builder 在 `0x10032DE2`
   把它改写成 rel32）：`.rdata` 命中 4 处 `0x102D27E0 / 0x102D30E4 / 0x102D3C08 / 0x102D3C18`。
3. 重建出的四个桩体各自**反汇编成连贯合法的 x86**（含正确的相对跳转落点），
   噪声拼不出这个。

⚠ 因此 master 上把 `武器绿毒/物功带毒/法师群毒/雷电带毒/半月带毒` 接到宿主**原生**的
state-26 管线（`[atk+0x1B4/0x1B5/0x1B6]` 标志 + `sub_772598` 概率 + `VMT+0xC8`）这件事，
门控的是**另一套机制**。原生那套是对的、本来就在；插件叠加的是第二套
（`Random(1)/Random(5)` + `[player+0x18CC]` 位 + `SendDelayMsg(10300)`），两者并存。
同理 `YanshenPoisonKeys.cs` 说「半月 arg0=0 故不触发」「雷电 `0x76EB1D` 是练技能非毒」
—— 对**未打补丁的宿主**成立，但那三处正是 trampoline 的挂载点，打上之后行为就变了。

**建议主代理复核 `94ede6dd`。** 本轮仍然不接线，但理由从「机制无字节佐证」
更正为「机制已证，缺的是 `[player+0x18CC]` 那两个位的**写点**」——
写点在宿主镜像 0 命中、在插件镜像只有 1 处读 0 处写，落在 Themida 区（C1）。
这是个更窄、更可攻的缺口：重抓一份带 VM 段的转储就能解。

---

## 4. A4 野蛮麻痹 —— fail-closed

42 字节桩体（builder site `0x100B381E`，宿主 `0x6BC9E2`）：

```
+000 60 9C                pushal / pushfd
+002 3E 8B 45 B4          mov eax, ds:[ebp-0x4C]
+006 85C0 / 74 14         test eax,eax / je 出口
+00A 6A00                 push 0                     ; nPoint
+00C B9 03000000          mov ecx,3                  ; dwPoisonTime
+011 BA 1A000000          mov edx,0x1A               ; nPoisonType = 26
+016 8B38 / FF97C8000000  mov edi,[eax] / call [edi+0xC8]   ; MakePosion
+01E 9D 61                popfd / popal
+020 B8 03000000          mov eax,3                  ; 重放被覆盖的 5 字节
+025 E9 -> 0x6BC9E7
```

`VMT+0xC8 = MakePosion` 由 `state19_rm_poison_faithful` §2.4 已钉死。
挂载点在 `0x6BC9D5 call 0x73F200`（冲撞本体）返回 true 之后、原生
`Random(3)+1 / TrainSkill` 之前 —— 这一段的 C# 等价物是
`TPlayObject.Attack.cs:671` 的 `TryStartNativeMotaeboForcedMove(...)` 成功分支。

**卡在 `[ebp-0x4C]`：不知道被麻痹的是谁。** 该位移在 `0x6BC000..0x6BCD10` 整段里
一次都没有出现（既没有写也没有别的读），而线性反汇编在 `0x6BB000..0x6BC9E2` 区间内
找不到任何能走到 `0x6BC9E2` 的 `push ebp/mov ebp,esp` 序言，所以这一帧的布局本轮
没有确立。目标对象不明 ⇒ 不接。

---

## 5. A5 噬魂沼泽绿毒修复 —— 桩体全解，落点待定

43 字节桩体（builder site `0x100B2468`，宿主 `0x691E2E`，覆盖 6 字节，续跑 `0x691E34`）：

```
+000 60 9C            pushal / pushfd
+002 8B 5D F8         mov ebx,[ebp-8]
+005 6A1F             push 0x1F        ; wParam = 31 (绿毒)
+007 6A7F             push 0x7F        ; nParam1 = 127
+009 53               push ebx         ; nParam2
+00A 6A05             push 5           ; nParam3
+00C 6A00             push 0           ; sMsg
+00E 68E8030000       push 0x3E8       ; dwDelay = 1000
+013 66B93C28         mov cx,0x283C    ; wIdent = 10300
+017 8BC6             mov eax,esi      ; Self
+019 E8 -> 0x766060
+01E 9D 61            popfd / popal
+020 8BCF 8BD6 8BC3   mov ecx,edi / mov edx,esi / mov eax,ebx   ; 重放 6 字节
+026 E9 -> 0x691E34
```

**无条件**（与主审计一致），唯一与四键版的差别是 `nParam1` 从 `0x0F`(15) 变成 `0x7F`(127)。

### 5.1 宿主已定名：`sub_691BF0` = 法道合击的「噬魂沼泽」臂

```
00691BF0 55 8BEC 83C4D4 …    sub_691BF0(eax=self, edx=参与者A, ecx=参与者B)
                             唯一 rel32 调用者 call@0x6940B2
00691C36 call 0x78FE88 / cmp eax,9 / jg 出口      ; 两个参与者都得离 [self+0x344] ≤ 9 格
00691C93 push 0x691EEC                            ; 字符串 "toSelf"
00691CB6 call 0x769258 ×2                         ; 两个参与者各放一次特效
00691D29 call 0x68EEDC ×2 -> [ebp-0x10]           ; 两份合击伤害求和（同 HeroObject.cs:1417）
00691D7B 5×5 区域双重循环（[ebp-0x1C]±2 × [ebp-0x20]±2）
00691DAA   call 0x7784A8 -> esi                   ; 取该格对象
00691DB5   call 0x767498 / je 跳过                ; IsProperTarget
00691DE1   call [edi+0x198] -> edi                ; 伤害管线
00691E2C   push 1
00691E2E   【钩子】mov ecx,edi / mov edx,esi / mov eax,ebx
00691E34   call 0x76FE44                          ; 区域法术产生器
```

GUI 自己写明了它属于哪一族（`YanshenLegacy23ReplicaPanels.cs:504`）：
「末日审判 噬魂沼泽 劈星斩 雷霆一击(**法道合击**类技能算法)，合击技能仅支持到4级」。
`call 0x68EEDC` 正是 `HeroObject.cs:1417` 已注明的 “final union damage”。

⇒ 语义完全确定：**在 5×5 区域里每命中一个合法目标，就在产生器调用之前给它挂一次绿毒**，
下毒人是第二个合击参与者 `[ebp-8]`，中毒者是该格对象 `esi`。

### 5.2 毒参数到 C# 的映射已经可对照（不是新造）

C# 的 `RM_POISON` 消费者 `TBaseObject.Base.cs:2062` 是
`M2Share.ObjectManager.Get(ProcessMsg.nParam2)`，而既有生产者一律写成

```csharp
victim.SendDelayMsg(caster, Grobal2.RM_POISON, Grobal2.POISON_DECHEALTH,
                    nPower, caster.ObjectId, nLevel, "", 1000);
```

逐槽对上原生：`Self=victim`、`wIdent=0x283C(10300)`、`wParam=31/30`（经
`state19` §3 的 `31-nType` 折算即 `POISON_DECHEALTH`/`POISON_DAMAGEARMOR`）、
`nParam1=0x7F(127)`、`nParam2=` 下毒人（原生传对象指针，C# 传 `ObjectId`，
这是本仓既有约定）、`nParam3=5`、`sMsg=''`、`dwDelay=1000`。

### 5.3 仍然不接的原因

C# 侧合击族的**管线形状不同**：`HeroObject.DealNativeUnionMagicAreaHit` 是
「取一格的对象、命中第一个就 return」，走 `SendNativeUnionStruck`；原生这里是
5×5 双重循环喂 `sub_76FE44` 区域产生器。两者不是同一个循环，`0x691E2E` 在 C# 里
没有一一对应的语句位置。把毒挂在形状不同的循环上，命中次数就会和原生不一致 ——
那不是 1:1。要接得先把合击区域管线对齐，超出「外科补」的范围。

---

## 6. A6 S(1,1..150) 播种 —— 内容已证，时机不可证

播种体 `sub_100CE240(ecx = player)`，`0x100CE4EA` 起逐条：

```
100CE4EA push 0x31 / mov edx,1 / mov ecx,edi / call GetS   ; GetS(1,49)
100CE4FB cmp eax,0x522(1314) / je 全部跳过                  ; 已播种 -> 整个函数无操作
100CE502 esi = 1
loop:
100CE50D cmp esi,0x96(150) / jg 结束
100CE517 cmp esi,0x31(49)  / jne 普通支
          SetS(1,49,1314)                                   ; 无条件，不做负值判断
          inc esi ; continue
普通支:
100CE52D GetS(1,esi)
100CE53B test eax,eax / jns 不动                            ; 值 >= 0 一律保持原样
100CE541 cmp esi,9 / jge 写 0
          SetS(1,esi,-1)                                    ; 1..8
写 0:     SetS(1,esi,0)                                     ; 9..150（49 已被上面截走）
结束:
100CE562 eax=[0x1031C5E0] ; cmp [eax+0x988],0x1F4 / jle 收尾  ; 某开关（>500 = 开）
100CE573 GetS(6,2) -> 非 0 且非 -1 时 [player+0x450] = 它
```

比主审计 §5 的一句话精确的地方：①`S(1,49)` 是在循环里**无条件**写 1314 的，不参与
「负值才写」的判断；②函数开头那次 `S(1,49)==1314` 是**整体早退**；③尾巴上还有一段
把 `S(6,2)` 回填进 `[player+0x450]`（正是 §2.3 复活祝福的起算 tick），受
配置单例 `+0x988` 那个开关控制。

**不接线的原因是时机不可证**：调用链止步于 `sub_100CE5E0`
（`push ebp/mov ebp,esp/…/mov ecx,[ebp+8]/call sub_100CE240/ret 4`，标准 stdcall 单参包装器），
它在 45 MB 转储里 **0 个 rel32 调用者、0 个 dword 引用**；`sub_100CE240` /
`SetS 助手 0x100CE200` / `GetS 助手 0x10056040` 同样各 0 个 dword 引用。
唯一入口只能在 Themida 虚拟化区或运行期现搭的桩体里。

「登录时播种」是合理推测但**没有字节支撑**，而播种时机直接决定
`S(1,49)==1314` 这道门何时对下游（原生切割 `0x1007AF24`）成立。不猜。

---

## 7. A7 激光两槽

### 7.1 S(1,82) 激光命中概率 —— 已接线

要点：**所有 fallback 落到 `mov eax,1`，不是还原 `mov eax,3`**，
所以开关一开就再也回不到原生的 `Random(3)+1`。

落点（2026-08-14 复核补记）：本条的接线在 `master` 上是由并行分支落的，读取器是
`GameSvr/Plugins/YanshenLaserSlots.cs` 的 `TrainRandomArg`，消费在
`GameSvr/Spells/MagicManager.cs`：`laserTrainRandomArg` 在 `DoMagic` 开头默认取
`NativeTrainRandom`(=3)，**只在 `SKILL_SHOOTLIGHTEN` 段**被改写，尾部共享的
`TrainSkill` 处执行 `Random(laserTrainRandomArg)+1`。
（本文早先写的「见提交 `801944bb`」是另一条分支上的等价实现，那个提交没有进 `master`，
按提交号找不到东西 —— 以上面两个文件为准。）

宿主那道 `cmp [ebp+4],0x6EDA54` 的门**等价于「这条法术是激光」**，因此「按 magicId 收窄」
与「只认那一个调用点」严格同义，证明如下：`0x6ED6FF` 的
`FF 24 85 06 D7 6E 00  jmp dword [eax*4+0x6ED706]` 直接拿 `wMagicID` 当下标
（`0x6ED6E3 0F B7 40 10 movzx eax,word [eax+0x10]`，**没有减基**，
上游只有 `cmp eax,0x25/jg`、`je`、`cmp eax,0x24/ja` 三道范围门），
下标 10 的表项在 `0x6ED72E`，值正是 `0x6EDA41`；而 `0x6EDA41` 在全镜像作为 dword
**只出现这一次**，其块尾 `0x6EDA4E ff 97 20 01 00 00 call [edi+0x120]` 的返回地址
就是 `0x6EDA54`。C# 侧 `SpellsDef.SKILL_SHOOTLIGHTEN == 10`。

键编码也对得上：C# 的 `group*1000+index` 使 S(1,82) → 1082 = `0x43A`，
正是桩体 `+03F cmp edx,0x43A` 比对的 tag。

审计：`AuditTools/YanshenLaserSlotsCheck`（本轮新增）把两槽的分支逐条钉住，
尤其是「开且 S≤0 / 缺键 → 1 而非 3」这三条 fallback。PASS。

### 7.2 S(1,81) —— 已证实，但 C# 无对应槽

180 字节桩体（builder site `0x100D931A`，宿主 `0x76EA07`，覆盖 6 字节
`6A 01 8B CF 33 D2`）：

```
+000 6A 01                    push 1              ; 先原样重放被覆盖的 arg
+002 三道门（[ebp+4]==0x6EDA54 / ebx>=0x410000 / [ebx]==0x6AC8C8）
+029 mov esi,[ebx+0x804]
+03B tag [esi+0x280]==0x439(1081) 且 val [esi+0x284]>0
+05C   mov [ebp-0x38],edx     ; ← 把 S(1,81) 写进 [ebp-0x38]
+05F..+0A6 S(1,89)：tag [esi+0x2C0]==0x441(1089) 且 >0 时按百分比缩放 edi（伤害）
+0AB 8BCF 31D2                mov ecx,edi / xor edx,edx   ; 重放余下 4 字节
+0AF E9 -> 0x76EA0D
```

`[ebp-0x38]` 是什么，可以用帧算术定死：宿主激光体 `sub_76E994` 的序言是
`add esp,-8`（2 个局部）+ `push ebx/esi/edi`，到 `0x76EA07` 之前
`0x76E9E5..0x76EA06` 已压了 8 个实参，故 `esp = ebp-0x34`；桩体自己重放的那条
`push 1` 再压一格，落点正是 **`ebp-0x38`**。
⇒ `mov [ebp-0x38],edx` **就是把刚压进去的 arg 覆盖成 S(1,81)**，
上游「写 `0x76FE44` 的 arg0 低 8 位」这句从此由推断升级为已证
（低 8 位来自 `sub_76FE44` 内的 `8A 45 08 mov al,[ebp+8]`）。

**但接不上**：`sub_76FE44` 是 9 个栈参的区域法术产生器，而 C# 激光走的是 7 参的
`TBaseObject.MagPassThroughMagic`，这个槽在 C# 路径上根本不存在。硬塞就是造槽。
维持 `ys_skills_impl §5` 的既有决定：不接。

---

## 8. A8 装备槽 StdMode 裁决 —— 扩展号无据，`M2Share` 是对的

见提交 `db8aa088` 与 `AuditTools/Yanshen207ProtocolCheck/Program.cs` 的注释。三段证据：

1. **派发**：`0x74C374[StdMode]` 取臂号、`0x74C414[臂号]` 取构造臂。
   `27→11h→0074D141`(TBelt)、`28→12h→0074D157`(TBoots)、`7→07h→0074CE9E`(TCharm 族)；
   `51/52/53/54/63/64` **全部** `→00h→` 默认臂 `0074D67E`(TBasePileItem)，
   `62→20h→0074D3E2`(TAnimalMascot)。
2. **资格**：`00762D30 cmp dl,0Ah` / `007630CC cmp dl,0Bh` / `00763390 cmp dl,0Ch`
   （均 `sete al/ret`），各只认一个槽号。TBasePileItem VMT `0x781C24` 与
   TAnimalMascot VMT `0x782614` 的 `+0x60` 分别落在类名串 `0x781C88` / `0x782678`，
   父链不含 `TEquipItem`，**没有谓词槽**。
3. **插件没扩展它**：上述 20 个 VA（两张派发表 / 四条构造臂 / 三个谓词体 /
   四个 classref 全局 / 四张 VMT / `TEquipItem` 基类谓词 `0x75FE18`）在 45 MB
   插件转储里绝对 dword 引用 **0** 命中；三条谓词的字节签名作为补丁 blob 也 0 命中。
   **对照组**（插件确实要打的挂载点）同一把尺子各有 4~10 处引用：
   `0x76C88B=8 0x76C816=6 0x6EDC5E=4 0x6EC111=4 0x6C09B5=4
   0x76E2BC=8 0x767BAE=10 0x6BC9E2=4 0x691E2E=5 0x7720FB=4`。

> 附带结论：`M2Server_exe.memory.bin`（活进程转储）在 9 个已知眼神挂载点上
> **一处都没打补丁**，与 `flat_image.bin` 逐字节相同。它不是「插件已生效」的快照，
> 不能拿来当"打了补丁长什么样"的判据。两份镜像整体有 740 处差异段，但没有一段
> 落在 StdMode 派发区 `0x74C338..0x74D6C0` 或那四张 VMT 里。

---

## 9. A9 四个审计工具

| 工具 | 本轮 | 说明 |
|---|---|---|
| `YanshenHalfMoonCompatCheck` | PASS | 已由 master `a9bd2e90` 修复，本轮复跑验证 |
| `YanshenTriggerDispatchCheck` | PASS | 已由 master `ea320491` 修复；本轮又随 §2.1 同步了期望表并加了 3 条断言 |
| `YanshenMonsterAttrCheck` | PASS | 已由 master `6c56f080` 修复，本轮复跑验证 |
| `Yanshen207ProtocolCheck` | PASS（21/21） | 本轮修，见 §8 |

---

## 10. 附：MFLG-24 UserNoKill 残留

解析侧与运行期切换侧在 master 上**已经补齐**：
`TMapFlag.boUserNoKill` / `TMapFlag.UserNoKillLevelCap` 两个字段、
`Maps.cs:766` 的 token 解析、`TempSetMapParamCommand.cs:140` 的 `@TempSetMapParam` 臂。

仍然缺的是**消费者**：`boUserNoKill` 在全树只有 2 处写、**0 处读**。
本轮在宿主镜像里找 `byte [map+0x71]` 的读点（判据 = 前后 ±0x60 内至少命中 2 个已知
地图旗标位移 `0x5C/0x65/0x67/0x68/0x6B/0x6C/0x70/0x7C/0x84/0x9C`），3 个候选全是误报
——最像的 `0x609117 mov dl,[esi+0x71]` 位于 `0x60xxxx` 的 VCL 区，同一函数里还有
`[esi+0x63C]/[esi+0x644]/[esi+0xE4]`，那不是 `TEnvirnoment`。

⇒ 与主审计的判断一致：**无已证 C# 消费者**，`word[+0x74]` 等级上限的语义也仍未证。
不硬补。
