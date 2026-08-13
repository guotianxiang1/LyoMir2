# 眼神 盘古3 ·「装备提升人物爆率 + _A值 + _B值」落地报告

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\ys-droprate`　分支：`w/ys-droprate`
- 建树基线 `38c5f107`（下文所有对跑数字都以它为准）；成文时 master 已推进，
  已 `git rebase master` 到 `eec4b571`，**无冲突**，rebase 后 `dotnet build GameSvr` 0 错、
  `AuditTools/Yanshen*` 仍 19/19 PASS
- 底本：
  - M2Server 平坦镜像 `D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`
  - 眼神 2.0.8 转储 `D:/loym2/staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin`，基址 `0x10000000`
    （**不是** `..._delayed_...` 那份，后者已重定位到 `0x57C40000`）
- 工具：python 3.11 + capstone 5.0.7，脚本在 `%TEMP%\ysdr_*.py`
- 前置：`docs/ys_b1_pangu3_20260814.md` §3.2（`w/ys-missing` 交接）

---

## 0. 结论摘要

| 项 | 结论 |
|---|---|
| 46 个 dword 桩体 | **独立复现，与交接报告逐字节一致** |
| `+0x2A4` = CC 下限（刺术下限） | **成立**，且 C# 早有权威字段 `m_NativeCoreWorkingAbility.CCLow` |
| 交接报告的「中间积是 64 位」 | **需订正：是 32 位**（见 §2.3） |
| 交接报告的「`LoWord(killer.m_WAbil.CC)`」 | **需订正：原生按 dword 读整个 `+0x2A4`**（见 §3） |
| helper | 已落地 `GameSvr/Plugins/YanshenEquipDropBoost.cs`，**真实可工作**（CC 源头是活的，见 §3.2） |
| 非禁改锚点 | **不存在**，论证见 §5；接线点报告见 §6 |
| 新审计 | `AuditTools/YanshenEquipDropBoostCheck` **PASS**（21 条契约） |

---

## 1. 安装器语义（先把 `sub_10032FD0` 读懂，否则无法判定桩体）

30 个站点共用的 trampoline 安装器。参数（`__cdecl`，右→左压栈）：

```
sub_10032FD0(vec* ret      @[ebp+0x08],
             void* start   @[ebp+0x0C],   ; E9 写在这里
             void* base    @[ebp+0x10],   ; VirtualProtect 基址（= start）
             void* end     @[ebp+0x14],   ; NOP 补齐到这里，也是桩体末尾回跳目标
             int*  arr     @[ebp+0x18],
             int   n       @[ebp+0x1C])
```

编码规则（`0x100330D0..0x1003317B` 主循环）：

```
0x100330E0  cmp ecx,0xE9 / je      ; ecx = arr[i]
0x100330E8  cmp ecx,0xE8 / jne 1字节支
0x100330F0  cmp dword [arr+4*(i+1)],0xFF / jle 1字节支   ; 后继必须是真 VA
0x100330FA  cmp i, n-1 / jge 1字节支
   5 字节支：发 opcode + rel32(arr[i+1] 重定位)，吃掉 2 个 dword
   1 字节支：发 cl，吃掉 1 个 dword
0x1003318D  循环后再追加 4 字节 rel32 → end，配合数组最后那个 0xE9
0x10033250  mov eax,[ebp+0xC] / mov byte[eax],0xE9 / 写 rel32 / NOP 到 [ebp+0x14]
```

⇒ 「一 dword 一字节」，但 `E9`/`E8` 后跟 `>0xFF` 时会变成 5 字节 rel32。
本站点的两个 `0xE9`（idx 25、idx 45）后继分别是 `0x8B` 和「无」，都走 1 字节支，
所以桩体里的 `F7 E9` 是 `imul ecx`，末尾的 `E9` 才由安装器补 rel32。

---

## 2. 站点 `0x100B9F9E`：46 个 dword 独立复现

### 2.1 调用参数

```
100B9F34  6A 2E              push 0x2E        ; n = 46
100B9F3C  8D 85 08 FE FF FF  lea eax,[ebp-0x1F8]
100B9F42  50                 push eax         ; arr
100B9F61  68 3D FD 71 00     push 0x71FD3D    ; end / resume
100B9F6D  68 37 FD 71 00     push 0x71FD37    ; VirtualProtect base
100B9F80  68 37 FD 71 00     push 0x71FD37    ; start
100B9F8C  50                 push eax         ; ret = [ebp-0x2A0]
100B9F9E  E8 2D 90 F7 FF     call 0x10032FD0
```

⇒ 补丁区间 `0x71FD37..0x71FD3D`，**6 字节**（`E9 rel32` + 一个 `0x90`）。

### 2.2 数组来源（38 个常量 + 8 个运行期字节）

```
100B9E64  push [edi+0x664] / 100B9E6A call 0x1022DC49   ; A = atoi(装备提升人物爆率_A值)
100B9E6F  push [edi+0x668] / 100B9E7B call 0x1022DC49   ; B = atoi(..._B值)
100B9E80  mov ecx,[ebp-0x2F0]  ; ecx = A      100B9E86  mov edx,eax   ; edx = B
100B9E92..100B9EBD  movzx/sar 拆 A → [ebp-0x1A8/-0x1A4/-0x1A0/-0x19C]   (idx 20..23)
100B9EC3..100B9F36  movzx/sar 拆 B → [ebp-0x168/-0x164/-0x160/-0x15C]   (idx 36..39)
movaps 常量（每条填 4 个 dword）：
  [ebp-0x1F8]←0x102D37E0 idx 0..3    [ebp-0x1E8]←0x102D29E0 idx 4..7
  [ebp-0x1D8]←0x102D2210 idx 8..11   [ebp-0x1C8]←0x102D1E70 idx 12..15
  [ebp-0x1B8]←0x102D3280 idx 16..19  [ebp-0x198]←0x102D2550 idx 24..27
  [ebp-0x188]←0x102D3140 idx 28..31  [ebp-0x178]←0x102D32A0 idx 32..35
  [ebp-0x158]←0x102D37F0 idx 40..43
100B9F50  mov dword [ebp-0x148],0xF9   (idx 44)
100B9F94  mov dword [ebp-0x144],0xE9   (idx 45)
```

九条 `.rdata` 常量的实测内容：

```
0x102D37E0  8B 40 14 F7      0x102D29E0  6D D4 81 7D      0x102D2210  F8 00 00 41
0x102D1E70  00 0F 82 1A      0x102D3280  00 00 00 B9      0x102D2550  F7 E9 8B 55
0x102D3140  F8 8B 92 A4      0x102D32A0  02 00 00 B9      0x102D37F0  01 D1 99 F7
```

### 2.3 展开后的 50 字节桩体（capstone 复验，A=B=10）

```
+0x000  8B 40 14               mov eax,[eax+0x14]         ; 回放 MonItem.MaxPoint
+0x003  F7 6D D4               imul dword [ebp-0x2C]      ; 回放 × 防沉迷倍率
+0x006  81 7D F8 00 00 41 00   cmp dword [ebp-8],0x410000 ; 凶手是不是真对象
+0x00D  0F 82 1A 00 00 00      jb  +0x2D                  ; 不是 → 原样返回
+0x013  B9 0A 00 00 00         mov ecx, A
+0x018  F7 E9                  imul ecx
+0x01A  8B 55 F8               mov edx,[ebp-8]
+0x01D  8B 92 A4 02 00 00      mov edx,[edx+0x2A4]
+0x023  B9 0A 00 00 00         mov ecx, B
+0x028  01 D1                  add ecx,edx
+0x02A  99                     cdq
+0x02B  F7 F9                  idiv ecx
+0x02D  E9 <rel32 → 0x71FD3D>
```

**订正交接报告一条：算术是 32 位，不是 64 位。**
`F7 E9 imul ecx` 是单操作数形式，只吃 `eax` —— 前一条 `imul` 的高半 `edx` 在这一步就被
覆盖丢弃；紧接着 `8B 55 F8` 又把 `edx` 写成凶手指针；最后 `99 cdq` 从 `eax`
重新符号扩展。所以：

```
denom = ((int32)(MaxPoint × penalty) × A) / (B + CC)     两次乘法都只留低 32 位
```

按 64 位中间积实现会在 `MaxPoint × penalty × A` 越过 `int32` 时给出完全不同的分母
（审计里用 `basePoints=0x20000000, A=10` 钉住：正解 `107374182`，64 位解 `536870912`）。

### 2.4 宿主侧被覆盖处

```
0071FD34  8B 45 E4          mov eax,[ebp-0x1C]   ; 当前 TMonItem
0071FD37  8B 40 14          mov eax,[eax+0x14]   ; MaxPoint      ← 补丁起点
0071FD3A  F7 6D D4          imul dword [ebp-0x2C]
0071FD3D  E8 0A 3E CE FF    call 0x403B4C        ; Random(eax)   ← resume
0071FD42  8B 55 E4          mov edx,[ebp-0x1C]
0071FD45  3B 42 10          cmp eax,[edx+0x10]   ; SelPoint
0071FD48  0F 8F 51 01 00 00 jg  0x71FE9F         ; 不掉
```

`0x71FD37` 处实际字节 `8B 40 14 F7 6D D4`，与桩体前 6 字节的回放**逐字节相等**。

宿主函数 `sub_71FA20`（= `@AfterScatterItems`）的三个寄存器参数与两个局部：

```
0071FA36  mov [ebp-0x0C],ecx   0071FA39  mov [ebp-8],edx   0071FA3C  mov [ebp-4],eax
0071FA62  mov dword [ebp-0x2C],1     ; 倍率缺省
0071FB27  mov dword [ebp-0x2C],2     ; 防沉迷二档 → 折半
0071FAB4  cmp dword [ebp-8],0        ; 别处对同一个 [ebp-8] 的空指针测试
0071FAC0  add edx,0x106 / call 0x405774   ; [ebp-8]+0x106 = m_sCharName ⇒ 它是 TBaseObject
```

`sub_71FA20` 全镜像**只有一个 rel32 调用者** `0x71F491`（0 个 dword 引用），
其外层 `sub_71F46C` 是 VMT 槽（125 个 dword 引用遍布怪物 VMT），`eax/edx/ecx` 原样转发。
C# 侧对应 `TBaseObject.Base.cs:1254` 的 `MonGetRandomItems(this, AttackBaseObject)`
（`AttackBaseObject = m_ExpHitter` 或其 `m_Master`）—— 即 `[ebp-8] == killer`。

### 2.5 开关与卸载

```
安装门 100B9E4A  cmp dword [edi+0x660],0 / je 0x100BA057     ; 装备提升人物爆率
已装缓存 100B9E57 cmp dword [eax+0x82C],0 / jg 0x100BA04E
卸载   100BA04E  cmp [edi+0x660],0 / jne  →  100BA057 cmp [eax+0x82C],0x64 / jne
       100BA07A  mov dword [ebp-0x2E8],0xF714408B
       100BA084  mov word  [ebp-0x2E4],0xD46D            ; 8B 40 14 F7 6D D4
       100BA0AE  call 0x10033340(len=6, va=0x71FD37)     ; 写回原字节
```

⇒ **关 = 宿主原样**，helper 关闭时必须恒等返回。

---

## 3. `+0x2A4` 判定复核

### 3.1 它就是 CC 下限，按 dword 读

职业攻击端点选择器 `sub_76CD8C(eax=self, dl=mode)`：

```
0076CD93  mov dl,[eax+0x72] / sub dl,1
0076CD99  jb  → 0x76CDA7   job 0  mov edx,[eax+0x28C] / mov eax,[eax+0x290]   DC
0076CD9B  je  → 0x76CDBD   job 1  [eax+0x294] / [eax+0x298]                   MC
0076CD9F  je  → 0x76CDD3   job 2  [eax+0x29C] / [eax+0x2A0]                   SC
0076CDA3  je  → 0x76CDE9   job 3  [eax+0x2A4] / [eax+0x2A8]                   CC
0076CDFF  其它 xor eax,eax
0076CD5C  sub_76CD5C(hi@eax, lo@edx, &mode): mode!=0→hi; lo>=hi→lo; 否则 lo+Random(hi-lo)
```

四支全部 `mov r32, dword ptr [...]`，与桩体的 `8B 92 A4 02 00 00` 宽度一致。
**订正交接报告一条**：不是 `LoWord(...)`，原生读的是整个 dword。

面板绿字自证（`YanshenLegacy23ReplicaPanels.cs:528`）：
「数据库配置任意装备CC字段属性，即[刺术下限]，每加1点，提升爆率约10%……实际爆率：(B+CC)/N\*A」
—— B=10 时 CC=1 恰好 `(10+1)/10` = +10%，与「原始 CC 点数」而非任何缩放值吻合。

### 3.2 C# 权威字段是活的，不是空壳

全镜像扫 `disp == 0x2A4` 的 35 条指令，落在角色对象上的**写**点只有两处，都在
`RecalcAbilitys sub_73D500` 内：

```
0073D9C4-0073DA30  装备聚合块回填（步长 8 的 lo/hi 表，与对象 +0x27C 起的表一一对应）
    [edi+0x0C]→[esi+0x27C] AC低   [edi+0x14]→[esi+0x284] MAC低
    [edi+0x1C]→[esi+0x28C] DC低   [edi+0x24]→[esi+0x294] MC低
    [edi+0x2C]→[esi+0x29C] SC低   [edi+0x34]→[esi+0x2A4] CC低   ← 0x73DA10
    [edi+0x10]→[esi+0x280] AC高 …
0073DE23  call 0x772960(dl=6) / test al,al / je 0x73DE90     ; 状态 6 门
0073DE2C-0073DE8F   门内：DC低/CC低 = _MIN(v,300)×50、DC高/CC高 = _MIN(v,300)×100
                    （0x4C700C = `cmp edx,eax / jg / mov eax,edx / ret` = _MIN）
```

⇒ 常规路径下 `[self+0x2A4]` = 基础 CC + 装备聚合 CC 低端点。

C# 侧的权威是 `TBaseObject.m_NativeCoreWorkingAbility.CCLow`
（`GameSvr/Actors/TBaseObject.NativeFixedAbility.cs:26`）。**这条对应不是本轮新造的**：
`TBaseObject.NativeSkill66Or67.cs:137-139` 的 id 68 分支已经注明
`// 68: 0x74449F [ebx+0x2A8] / [ebx+0x2A4]` 并直接读这两个字段。

该字段有三条真实喂食路径，都已接线：

| 喂食点 | 代码 | 对应 |
|---|---|---|
| 职业 3 基础值 | `NativeFixedAbility.cs:43` `CCLow = Max(Level/5-1, 1)` | 基础能力 |
| 固定能力记录 | `NativeFixedAbility.cs:70` `+= record[0x7C]` | 类初始化 |
| 装备 CC 字段 | `NativeCoreWorkingAbility.cs:18` `+= item.Cc`（`GoodItem.Cc`，StdItem +0x38） | 「数据库配置任意装备CC字段」 |
| 扩展属性 111 | `NativeCoreWorkingAbility.cs:106` `case 111: CCLow += value` | **「刺术下限」** |

属性号 111 就是 `NativeType2StdItemSnapshotState.cs:404` 名表里「刺术下限」的编码
（下标 110 → code 111），由 `ApplyNativeEffectItemParameters` 在装备重算时逐槽喂入。

⇒ helper 不是恒 0 的假实现：玩家穿了带 CC / 刺术下限的装备，`CCLow` 就是非零，
分母就真的会变。

---

## 4. 交付物

| 文件 | 性质 | 说明 |
|---|---|---|
| `GameSvr/Plugins/YanshenEquipDropBoost.cs` | 新增 | helper 本体，`NativeDenominator`（纯算术）+ `Denominator`（含开关/凶手门） |
| `GameSvr/Plugins/YanshenApi.cs` | 改 3 行 | `BoostDropRateA/B` 从 `GetParam→double` 改成 `ParamAtoi→int` |
| `GameSvr/Properties/AssemblyInfo.cs` | 加 1 行 | `InternalsVisibleTo("YanshenEquipDropBoostCheck")` |
| `AuditTools/YanshenEquipDropBoostCheck/` | 新增 | 21 条契约 |

`BoostDropRateA/B` 改 atoi 的依据：`0x100B9E6A` / `0x100B9E7B` 调的是
`0x1022DC49`（`strtol base 10`，`YanshenApi.cs:283` 早已定名），不是 `atof`。
差异是可观测的：`atoi("3.9")=3` 而 `double.TryParse` 得 `3.9`；
`atoi("")=0` 而 `GetParam("")` 回落缺省 10（**缺键**才该回落缺省，空串不该）。
缺省 `10` 取自页面对象构造函数 `[edi+0x664]='10'` / `[edi+0x668]='10'`。
改前 `BoostDropRateA/B` 在全仓库**零调用者**，改返回类型无连带影响。

---

## 5. 为什么不能搬到非禁改文件（fail-closed）

`0x71FD37` 落在 `sub_71FA20` **段2 循环体内部**（`0x71FCFF-0x71FEA1`），
夹在「取本件 `MaxPoint`」与「`call Random`」之间，是**逐件**生效的。
C# 里唯一表达这个位置的表达式就是 `UsrEngn.cs:2499`。逐条排除：

1. **上提到唯一调用者** `TBaseObject.Base.cs:1254`。
   `MonGetRandomItems` 生产侧确实只有这一个调用者（另一个是
   `AuditTools/RobotUnknownItemInitCheck:363`），但分母含 `MonItem.MaxPoint`，
   逐件不同，且 `(x·A)/(B+CC)` 与 `x·(A/(B+CC))` 在整除下不等价。**不等价，弃**。
2. **在新 partial 里复制一份循环**。会让掉落主循环出现两份权威（本轮审计的
   `wired && stock` 断言正是防这个），且 `RobotUnknownItemInitCheck:322` 用
   文本解析 `UsrEngn.cs` 里的 `MonGetRandomItems` 正文。**弃**。
3. **把 `TMonItem.MaxPoint` 改成带环境变量的属性**。`TMonItem` 在
   `SystemModule/Data/`，同一份 `TMonInfo.ItemList` 还被段1 `TraverseMonItemsTree`
   与 `UserEngine.NativeButcherDeliver` 读；原生补丁只作用于段2。
   而且那等于给共享配置对象挂隐式全局。**弃**。
4. **包 `M2Share.RandomNumber`**。全仓库 100+ 处调用共用。**弃**。

⇒ 没有等价锚点。helper 落地、接线点上报，三键在接线前保持 MISSING。

---

## 6. 接线点（禁改文件，请主代理执行）

**文件**：`GameSvr/UsrSystem/UsrEngn.cs`
**方法**：`UserEngine.MonGetRandomItems(TBaseObject mon, TBaseObject killer = null)`
**行**：`2499`（就是 `MonGetRandomItems` 循环体内唯一那行
`Random(...) <= MonItem.SelPoint`；行号按 `38c5f107`/`eec4b571` 都是 2499，
但认表达式比认行号可靠）

现状：

```csharp
                    if (M2Share.RandomNumber.Random(MonItem.MaxPoint * penalty) <= MonItem.SelPoint)
```

改为：

```csharp
                    // 眼神「装备提升人物爆率」把 0x71FD37 的 6 字节换成 trampoline，
                    // 分母改成 (MaxPoint×倍率×A)/(B+凶手CC下限)；关/凶手为空时恒等。
                    if (M2Share.RandomNumber.Random(
                            Plugins.YanshenEquipDropBoost.Denominator(
                                MonItem.MaxPoint * penalty, killer))
                        <= MonItem.SelPoint)
```

`UsrEngn.cs` 的 `namespace` 是 `GameSvr`，helper 在 `GameSvr.Plugins`，
所以要么写 `Plugins.YanshenEquipDropBoost`，要么在文件头加 `using GameSvr.Plugins;`。
本轮**已试接、`dotnet build GameSvr` 0 错、审计识别为「已接线」后回滚**，
提交里 `UsrEngn.cs` 保持未改。

接线后 `YanshenEquipDropBoostCheck` 的末尾行会自动从
「尚未接线」变成「已接线（0x71FD37）」，两种形态都 PASS，
但**同时出现两种形态会 FAIL**（防双权威）。

---

## 7. 验证

- `dotnet build GameSvr`：**0 错**（未接线态与试接态各验一次）。
- `AuditTools/Yanshen*`：本分支 **19/19 PASS**；基线 `38c5f107` 18/18 PASS。
  新增的 `YanshenEquipDropBoostCheck` 是第 19 个，无回归。
  （附带一条订正：交接报告说 `Yanshen207ProtocolCheck` 因 `belt StdMode 54` FAIL，
  在 `38c5f107` 上它 **PASS** —— 那条已在 master 上被修掉。）
- 掉落类审计：在**同一台机器、同一组命令**下对本分支与基线
  `38c5f107`（`.claude/wt2/ys-droprate-base` detached 工作树）逐项对跑，
  过滤器 `Drop` / `InProc` / `Robot` / `ChgMon` 共 17 个项目：

  | 项目 | 基线 | 本分支 |
  |---|---|---|
  | `NativeDropRngSequenceCheck` | BUILD-ERROR | BUILD-ERROR |
  | `DeathDropPolicyCheck` | FAIL | FAIL |
  | `NativeDropControlRuntimeCheck` | FAIL | FAIL |
  | `InProcEngineRunCheck` | FAIL | FAIL |
  | `RobotUnknownItemInitCheck` | FAIL | FAIL |
  | `Drop39WeightPolarityCheck` / `MonsterDomainRaceAndDropCheck` / `NativeDropControlParserCheck` / `NativeMapDropItemCommandCheck` | PASS | PASS |
  | `InProcCorpsGuildRunCheck` / `InProcDynRoomRunCheck` / `InProcHeroRunCheck` / `InProcItemConservationCheck` / `InProcMailRunCheck` / `InProcSocialRunCheck` | PASS | PASS |
  | `RobotDirectionObjectCountCheck` / `ChgMonItemPercentStaticCheck` | PASS | PASS |
  | `YanshenEquipDropBoostCheck` | 无 | **PASS** |

  **失败集合逐项相同，零回归**，差额只有新增的那一个 PASS。
  `NativeDropRngSequenceCheck` 的 BUILD-ERROR 出在它自己的
  `Program.cs:106/109`（`CS8422` 静态本地函数引用 `this`、`CS1503` 方法组→`int[]`），
  与本轮无关。

---

## 8. 给审计报告的订正清单

1. `yanshen_completeness_audit_20260814.md` §5 B1「缺 `+0x2A4` 累计爆率加成字段」
   → 改为「`+0x2A4` = `m_WAbil` 的 CC 低端点（刺术下限），
   C# 权威 `m_NativeCoreWorkingAbility.CCLow`，字段与四条喂食路径俱全」。
2. 同上「分母改成 `MaxPoint×倍率×A÷(B+[player+0x2A4])`」的算术需注明
   **两次乘法均为 32 位截断**，除法为 `idiv`（向零截断）。
3. `ys_b1_pangu3_20260814.md` §3.2 的「中间积是 64 位」与
   「`LoWord(killer.m_WAbil.CC)`」两处措辞按本文 §2.3 / §3.1 订正。
4. `ys_gui_matrix` 里 `装备提升人物爆率` / `_A值` / `_B值` 三行在**接线后**
   才可从 `LABEL_ONLY` 改判；本轮 helper 已就位但故意未接，维持 MISSING。
5. C2「8 个 trampoline 模板未回收」名单里 `装备提升人物爆率` 可关闭
   —— 46 个 dword 已两次独立回收（`ys_gui_extreme §5` 与本文 §2）。
