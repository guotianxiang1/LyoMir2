# DROP-34 / DROP-35 —— 兄弟调用次序与门控归位 + 段3「世界掉落」整条移植

- 日期：2026-08-14
- 分支：`w/droporder`（基线 `ec462a77`，含 `drop33b`）
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`）
- 工具：`tools/m2_disasm.py`、`tools/drop33b_xref.py`、`tools/drop33b_ctx.py`、
  `tools/drop33b_dword.py`、新增 `tools/droporder_str.py`（Delphi AnsiString 头 + GBK）、
  `tools/droporder_dwords.py`（dword 窗口 + Delphi VMT 自指针标注）

## 结论摘要

| 条目 | 判定 | 提交 |
|---|---|---|
| `RunInNativeOrder` 次序（`62478ccf` 回归） | **已修**：controlled 先、ordinary 后 | `d6cebb7f` |
| 三道门的作用域 | **已修**：只管 ordinary 一侧，`m_boNoItem` 单独提到两者之上 | `d6cebb7f` |
| 掉落控制不进散落账 | **已修**：`TryScatter` 不再写 `scatteredItems` | `d6cebb7f` |
| `NativeDropRngSequenceCheck` 编译错 | **已修**（仍卡在与本分支无关的产品缺陷上） | `5134748f` |
| 段3「世界掉落」`TWorldScatterMgr` | **已移植**，含新审计工具 | `96d59350` |
| `g_Config.nDropItemRage` 字段 | **已删** | `d427f714` |

---

## 1. 真实调用图（自行复核，非引用前作）

`sub_71F46C` 整个函数体只有两条 `call`，掉落控制在前：

```
0071F46C  55 8B EC 53 56 57      push ebp / mov ebp,esp / push ebx,esi,edi
0071F472  8B F9 / 8B F2 / 8B D8  edi:=ecx / esi:=edx / ebx:=eax
0071F478  8B CF / 8B D6 / 8B C3  原样转发
0071F47E  E8 F5 0D 00 00         call 0x720278   ; ① 掉落控制四相
0071F483  8A 45 0C 50 8A 45 08 50                ; 两个字节栈参再压一遍
0071F491  E8 8A 05 00 00         call 0x71FA20   ; ② 段1/段2/段3/金币/@AfterScatterItems
0071F49A  C2 08 00               ret 8
```

`tools/drop33b_xref.py 0x71FA20 0x720278 0x71F46C 0x752CAC` 实测：

```
=== target 0071FA20 : 1 hits ===   0071F491  call
=== target 00720278 : 1 hits ===   0071F47E  call
=== target 0071F46C : 3 hits ===   005FAB29 / 0066C989 / 0066D27A  call
=== target 00752CAC : 1 hits ===   0071FEC8  call
```

两个被调各只有一个 E8 调用点、0 个 dword 引用（VMT 槽里没有它们），所以这一处字节
**唯一确定**先后。`sub_71F46C` 的三个直接调用者都是子类 override 里的 `inherited`
（`0x5FAB04` / `0x66C97C` / `0x66D270` 三个短壳，各自把栈参原样转发），不改次序。

怪物 Die 派发到的是 `sub_71F46C` 而不是 `sub_71FA20`：

```
0071E3D2  FF 96 FC 01 00 00      call dword [esi+0x1FC]
0071E3EF  FF 96 FC 01 00 00      call dword [esi+0x1FC]   ; 另一臂（ebx 为 nil 时）
```

### 各自的进入条件

`sub_720278` 从序言到第一相**逐条无条件跳转**：

```
00720278  55 8B EC 83 C4 F0 53 56    序言
00720280  33 DB ... 89 45 F8 ...     存参
0072029E  E8 9D 80 CE FF             call 0x408340        ; now
007202A5  8B 83 28 01 00 00 / 8B 40 2C
007202B0  B2 01 / E8 85 C2 05 00     dl=1 / call 0x77C53C ; 第一相
```

`0x720278..0x7202B2` 里一条 `Jcc` 也没有（`try/finally` 框架除外）。

---

## 2. 三道门各自守谁

三道门全在 `sub_71FA20` 内部，失败臂一律 `jmp 0x720092`，那是 `sub_71FA20`
**自己的框架出口**——它在 `0x7200B7 ret` 之后，管得住段1-4 与
`@AfterScatterItems` 回调，管不着**已经返回**的 `0x71F47E`。

### 门 A —— 一次性哨兵 `[self+0x47F]`

```
0071FA50  80 B8 7F 04 00 00 00   cmp byte [eax+0x47F],0
0071FA57  0F 85 35 06 00 00      jne 0x720092
0071FA6C  C6 80 7F 04 00 00 01   mov byte [eax+0x47F],1   ; 无条件置位
```

守的是 `sub_71FA20`，且置位点在 `0x720278` 返回之后。兄弟点 `0x71ECBE`
（采集腿 `sub_71EC88`）共用同一字节。

### 门 B —— 空掉落表 `[self+0x474]`

```
0071FA8A  83 B8 74 04 00 00 00   cmp dword [eax+0x474],0
0071FA91  0F 84 FB 05 00 00      je 0x720092
```

守的是 `sub_71FA20`（连同它的金币结算与回调）。**注意它在散落账
`TStringList` 建出来之前**——账本建于：

```
0071FA97  B2 01
0071FA99  A1 F0 24 42 00         mov eax,[0x4224F0]       ; TStringList
0071FA9E  E8 BD 4B CE FF         call 0x404660            ; Create
0071FAA3  89 45 C8               mov [ebp-0x38],eax
```

### 门 C —— 防沉迷三连

```
0071FAB4  83 7D F8 00 / 74 74        cmp [ebp-8],0 / je 0x71FB2E      ; 杀手为 nil 则整段门不生效
0071FACE  80 B8 78 01 00 00 00 / 75 57                                ; 非玩家种族同上
0071FADA  80 BB 28 18 00 00 03 / 74 14   cmp byte [killer+0x1828],3
0071FAE3  80 BB 29 18 00 00 03 / 74 0B   cmp byte [killer+0x1829],3
0071FAEC  8B C3 / E8 95 7C FB FF         call 0x6D7788                ; 状态 25
0071FAF3  84 C0 / 74 27
0071FB19  E9 74 05 00 00                 jmp 0x720092
```

守的还是 `sub_71FA20`。

### 唯一同时守两者的门

在 Die 里，比虚派发还高一层：

```
0071E3B7  80 B8 7D 04 00 00 00   cmp byte [eax+0x47D],0    ; m_boNoItem
0071E3BE  75 35                  jne 0x71E3F5              ; 连 [VMT+0x1FC] 都不调
```

---

## 3. 次序与门控的联合方案

### 3.1 `RunInNativeOrder` 改成兄弟结构的编码器

```csharp
internal static bool RunInNativeOrder(Action controlledDrop,
    Func<bool> ordinaryBlocked, Action ordinaryDrop)
{
    controlledDrop?.Invoke();                                   // 0x71F47E
    var blocked = ordinaryBlocked == null || ordinaryBlocked();
    if (!blocked) ordinaryDrop?.Invoke();                       // 0x71F491
    return blocked;
}
```

三件事一次编码完：**controlled 先跑**；**ordinary 的门在 controlled 跑完之后才求值**
（哨兵置位 `0x71FA6C` 本来就发生在 `0x720278` 返回以后）；**门的结果回传**给同受
`0x720092` 约束的金币结算与脚本回调。调用方无法把次序写反，也无法把门漏到
controlled 一侧。

### 3.2 Die 阶梯

```csharp
var nativeDieDropSuppressed = m_boNoItem;                      // 0x71E3B7，管两个兄弟
var scatterBlocked = NativeDropControlRuntime.RunInNativeOrder(
    controlledDrop: () => { if (!nativeDieDropSuppressed)
                                NativeDropControlRuntime.TryScatter(this, AttackBaseObject, null); },
    ordinaryBlocked: () => nativeDieDropSuppressed
        || !TryEnterNativeScatter()                            // 门 A
        || M2Share.UserEngine == null                          // C# fail-closed
        || !M2Share.UserEngine.NativeHasMonsterDropTable(m_sCharName)   // 门 B
        || NativeAfterScatterItemsBlocked(AttackBaseObject),   // 门 C
    ordinaryDrop: () => { TraverseMonItemsTree(...); MonGetRandomItems(...); });
```

`m_boNoItem` 从原来的第四位提到**最左**：原生里它挡掉整个虚派发，`0x71FA6C`
根本没机会跑；放在 `!TryEnterNativeScatter()` 后面会让 `m_boNoItem` 的怪也烧掉哨兵。

### 3.3 掉落控制不再写散落账

`sub_72016C` 只有三个寄存器参、以裸 `ret` 收尾（`0x720274`），没有账本形参：

```
0072016C  55 8B EC 83 C4 E4 53 56 57
00720175  89 4D F4               mov [ebp-0xC],ecx   ; 落点宿主（-> 0x7688A0 的 eax）
00720178  89 55 F8               mov [ebp-8],edx     ; item creator（-> [ebp+0xC]）
0072017B  89 45 FC               mov [ebp-4],eax     ; 待发列表
...
00720274  C3                     ret                 ; 无 ret N，无栈参
```

而账本 `[ebp-0x38]` 建于 `0x71FA9E`，那时 `0x720278` 早已返回。所以原生的掉落控制
落地物**不进 `@AfterScatterItems` 的清单**，C# 侧改传 `null`。

### 3.4 三输入等价论证

记：S = 哨兵未烧，T = 有自有掉落表，F = 防沉迷放行，N = `m_boNoItem`。

| 输入 | 原生 | 改后 C# |
|---|---|---|
| **有掉落控制记录，一切正常**（`¬N`, S, T, F） | `0x71F47E` 跑四相 → `0x71F491` 进 `sub_71FA20`，段1/2/3/金币/回调全跑 | `controlledDrop` 跑 `TryScatter`；`ordinaryBlocked` = false；段1/2 跑，`scatterBlocked=false` 让段3/金币/回调跑 |
| **无掉落控制记录**（`m_DropItemControl` 空） | 四相各自 `SelectMap`/`SelectWorld` 返回空表，`sub_72016C` 在 `0x720183 cmp [ebp-4],0 / je 0x72026E` 或 `0x720195 dec eax / jl` 空转；`sub_71FA20` 照常跑 | `TryScatter` 的四个 `ScatterPhase` 收到空 `pending` 列表，`foreach` 零轮；其余同上 |
| **防沉迷触发**（`¬N`, S, T, `¬F`） | `0x71F47E` **照常跑完四相**；`0x71F491` 进 `sub_71FA20` 后在 `0x71FADA/E3/EC` 命中 → `0x71FB19 jmp 0x720092`，段1-4 与回调全跳过。**哨兵已在 `0x71FA6C` 烧掉**（它在门 C 之前） | `controlledDrop` 先跑完 `TryScatter`；`ordinaryBlocked` 从左往右求值：`TryEnterNativeScatter()` 先执行并置位（哨兵照烧），再由 `NativeAfterScatterItemsBlocked` 返回 true → `blocked=true`，段1/2 不跑，`scatterBlocked=true` 掐掉段3/金币/回调 |

第四种输入（`m_boNoItem`）一并对齐：原生连 `[VMT+0x1FC]` 都不调，两个兄弟都不跑、
哨兵不烧；C# 因 `nativeDieDropSuppressed` 在最左而短路，`TryEnterNativeScatter()`
不执行。

**为什么不会落到「第三种状态」**：段内三道门与 `m_boNoItem` 被拆成两个作用域，
分别对应 `sub_71FA20` 内部与 `[VMT+0x1FC]` 之上，恰好是原生的两层。改前的 C# 把
四者混在一个 `scatterBlocked` 里并让 `TryScatter` 也受其约束；只搬次序会得到
「controlled 先跑但仍受段内三门约束」——那才是第三种状态。

### 3.5 `rng-order` 断言的重钉

`AuditTools/NativeDropControlRuntimeCheck` 原断言：

```csharp
// sub_71FA20 runs the monster's own table (segment 2 …) before
// the controlled world drop (segment 3, head 0x71FEA7).
Equal(new[] { "ordinary:11", "controlled:22" }, …, "shared RNG generation order");
```

断言自带的理由就是被推翻的那条归属（controlled = 段3）。段3 走的是
`0x71FEC8 call 0x752CAC` + 单例 `[0x7D71F4]`，与本类的四相
Select/Materialize 结构无关；本类的记录布局逐字段对上 `sub_77C580`/`sub_77C738`、
落地经 `sub_72016C`。归属换掉之后，次序由 `0x71F47E < 0x71F491` 决定，
所以断言写反了，改为 `controlled:11, ordinary:22`，并新增两条钉子：

- 门在 controlled 之后求值（`gateEvaluatedAfter == 1`）；
- 门为 true 时 controlled 仍然跑（`blockedOrder == ["controlled"]`）。

PASS 横幅相应改为 `rng-order=controlled,ordinary gate-scope=ordinary-only`。

---

## 4. 段3「世界掉落」整条

### 4.1 身份

| 项 | 值 | 取证 |
|---|---|---|
| 类名 | `TWorldScatterMgr` | VMT `0x74B678`，`[V-0x2C]=0x74B64C -> 0x74B69E`，ShortString `10 'TWorldScatterMgr'`；自指针 `[V-0x4C]=0x74B62C=0x74B678` 通过 |
| 实例大小 | `0x30` | `[V-0x28] = 0x74B650 = 0x00000030` |
| 单例 | `[0x7D71F4] -> 0x7DCB8C`，对象在 `[0x7DCB8C]` | `0x71FEC1 A1 F4 71 7D 00 / 8B 00` |
| 注册 | GBK「世界暴率管理」 | `0x7568CA mov edx,0x7DCB8C / 0x7568CF mov ecx,0x7568FC / 0x7568D4 mov eax,[0x74B62C] / call 0x4D94C4` |
| 段3 标签 | GBK「世界掉落」 | `0x720134` 字节 `CA C0 BD E7 B5 F4 C2 E4` |

VMT 槽：`+0x04 = 0x75307C`（计时）、`+0x08 = 0x752D40`（载入）、
`+0x0C = 0x752B8C`（清表）、`Destroy = 0x752C7C`；非虚：`0x752CAC`（查询）、
`0x753124`（逐条匹配）、`0x7530D8`（逐条累计）、`0x7531D0`（地图串切词）。

### 4.2 记录布局（步长 24）

```
00752CEF  8B 45 F4               mov eax,[ebp-0xC]      ; i
00752CF2  8D 04 40               lea eax,[eax+eax*2]    ; i*3
00752CF5  8B 53 2C               mov edx,[self+0x2C]    ; 动态数组基址
00752CF8  8D 04 C2               lea eax,[edx+eax*8]    ; base + i*24
```

| 偏移 | 字段 | 写者 |
|---|---|---|
| `+0x00` word | `minLevel` | `752E48 66 89 06` |
| `+0x02` word | `maxPile` | `752E72 66 89 46 02` |
| `+0x04` int | `secSpace` | `752E5D 89 46 04` |
| `+0x08` int | `lastTick` | `7530EA 89 4B 08` / `75319F 89 43 08` |
| `+0x0C` obj | prize 表（`TStringList`） | `752E33 89 46 0C` |
| `+0x10` int | `pending` | `753116 89 7B 10` / `753197 89 43 10`（清零） |
| `+0x14` obj | map 表（可 nil） | `753210 89 43 14` |

### 4.3 配置来源

载入器 `sub_752D40`：

```
00752D64  8D 4D F4 / BA A8 2F 75 00 / 8B 45 FC / E8 C4 6F D8 FF
              call 0x4D9D38(self, "世界爆率文件1"(0x752FA8), @path)
00752D77  E8 B0 A1 CB FF   call 0x40CF2C          ; FileExists
00752D7E  0F 84 E6 01 00 00 je 0x752F6A           ; 不存在 -> 静默返回，连 ini 都不建
00752D89  A1 A0 C8 44 00 / E8 BD 9B CF FF         ; TIniFile.Create(path)
00752DA4  6A 00 / B9 C0 2F 75 00 / BA D0 2F 75 00 / FF 53 08
              ReadInteger("setting","typeNum",0)
00752DBD  89 58 1C         mov [self+0x1C],ebx    ; 记录数
00752DC2  0F 8E 64 01 00 00 jle 0x752F2C          ; <=0 -> 日志「[Error]:找不到世界爆率文件」
00752DC9  ... 8D 55 08 B6 74 00 / E8 85 3E CB FF  ; DynArraySetLength(self.records, typeNum)
```

每节 `typeN`（`0x752FE0` = `"type"` 拼 `IntToStr(i+1)`）：

| ini 键 | 字面量 | 默认 | 落点 |
|---|---|---|---|
| `minLevel` | `0x752FF0` | 0 | `word [rec+0]` |
| `secSpace` | `0x753004` | 0 | `[rec+4]` |
| `maxPile` | `0x753018` | **1** | `word [rec+2]` |
| `map` | `0x753028` | `""` | `sub_7531D0` 切词入 `[rec+0x14]` |
| `prize` / `prize1`..`prize99` | `0x753034` | `""` | 逐条 `TStrings.Add` 入 `[rec+0xC]` |

prize 的读法是「先读裸键，再在循环里读带序号的」：

```
00752E95  ...ReadString(section,"prize","") -> [ebp-0x14]
00752EAA  BB 01 00 00 00    mov ebx,1
00752EAF  83 7D EC 00 / 74 3E   空串即停
00752EB5  8B 46 0C / FF 51 38   [rec+0xC].Add(值)
00752ECB  E8 CC 99 CB FF        IntToStr(ebx)
00752ED6  BA 34 30 75 00        "prize" + n
00752EEE  83 FB 64 / 75 BC      ebx 走到 0x64 停 -> 最多 prize99
```

地图串切词 `sub_7531D0`，分隔符就地拼在栈上，随 `push 3`（Delphi 开放数组的 high）
交给 `sub_4C6BA4`：

```
0075321E  6A 03                 push 3
00753224  C6 45 F0 20           byte [ebp-0x10] := ' '
00753228  C6 45 F1 7C           byte [ebp-0x0F] := '|'
0075322C  C6 45 F2 09           byte [ebp-0x0E] := TAB
00753230  C6 45 F3 2C           byte [ebp-0x0D] := ','
00753259  E8 5E 8A CB FF        UpperCase(token)
00753266  FF 51 38              [rec+0x14].Add
```

日志两条：成功 `0x752EFF` 拼 `"已加载"(0x753044) + path + "！"(0x753054)`；
`typeNum<=0` 走 `0x752F2C` 拼 `"[Error]:找不到世界爆率文件"(0x753060) + path`，
都交给 `0x79DF74`（`eax=[[0x7D5ECC]]`、`cl=1`，主窗口输出）。

**文件名可证，目录不可证。** `sub_4D9D38` 先在模块设置表 `[self+0x14]` 里按名查值；
查不到就往 `<模块目录>main.ini` 写一份默认描述：

```
004D9E43  B9 18 9F 4D 00   mov ecx,0x4D9F18   ; ".txt"
004D9E48  8B 55 F8         mov edx,[ebp-8]    ; 键 "世界爆率文件1"
004D9E4B  E8 CC B9 F2 FF   call 0x40581C      ; 值 = 键 + ".txt"
004D9E54  B9 28 9F 4D 00   mov ecx,0x4D9F28   ; "FileName"
004D9E61  FF 53 04         call [ini+4]       ; WriteString(键,"FileName",值)
004D9E64  68 3C 9F 4D 00   push 0x4D9F3C      ; "True"
004D9E69  B9 4C 9F 4D 00   mov ecx,0x4D9F4C   ; "AutoLoad"
```

即**默认文件名由二进制自己写出来**：`世界爆率文件1.txt`。目录一侧是
`sub_4D9F58`：

```
004D9F67  A1 2C 6B 7D 00   mov eax,[0x7D6B2C] / FF 30   push [框架根目录]
004D9F6E  68 9C 9F 4D 00   push 0x4D9F9C      ; "EngineConfig\"
004D9F73  FF 73 18         push [self+0x18]   ; 模块名
004D9F76  68 B4 9F 4D 00   push 0x4D9FB4      ; "\"
004D9F7D  BA 04 00 00 00 / E8 09 B9 F2 FF     ; _LStrCatN 四段
```

`[self+0x18]`（模块名）与 `[0x7D6B2C]`（框架根）都在本子系统之外；返回值还要过
`0x4D9EB2 cmp byte [Objects[i]+0x100],0`，为 0 就 `0x4D9EBE call 0x405500` 把路径清空，
这个旗标本轮没能定性。**目录标 BLOCKED**，C# 取本仓既有的 Envir 目录作替身。

### 4.4 触发条件

**计时** `sub_75307C`（VMT +0x04，模块框架传 `now`）：

```
0075308B  2B 43 24         sub eax,[self+0x24]
0075308E  3D E8 03 00 00   cmp eax,0x3E8
00753093  7E 3D            jle 出                       ; 严格 >1000ms
00753095  C6 43 28 00      mov byte [self+0x28],0       ; 总闸先清零
00753099  89 43 24         mov [self+0x24],eax          ; 记这一轮
007530B6  E8 1D 00 00 00   call 0x7530D8                ; 逐条累计
007530BB  84 C0 / 75 0A / 80 7B 28 00 / 75 04 / 33 C0 / EB 02 / B0 01
007530CB  88 43 28         mov byte [self+0x28],al      ; armed |= ready
```

构造函数把「上次计时」推到 30 分钟以后，所以**开服头半小时整个子系统静默**：

```
00752C31  C6 46 28 00      byte [self+0x28] := 0
00752C35  E8 06 57 CB FF   call GetTickCount
00752C3A  05 40 77 1B 00   add eax,0x1B7740             ; +1800000 ms
00752C3F  89 46 24         mov [self+0x24],eax
```

**逐条累计** `sub_7530D8`：

```
007530E4  83 7B 08 00 / 75 05     cmp [rec+8],0
007530EA  89 4B 08                首见只播种时间戳，返回 false
007530EF  83 7B 04 00 / 7E 29     secSpace<=0 -> false
007530F7  2B 43 08                eax = now - lastTick
007530FA  B9 E8 03 00 00 / 33 D2 / F7 F1   无符号 div 1000 -> 秒
00753107  99 / F7 7B 04           有符号 idiv secSpace
0075310B  0F B7 53 02             movzx edx,word [rec+2]   ; maxPile
0075310F  E8 F8 3E D7 FF          call 0x4C700C = _MIN(eax,edx)
                                  （`3B D0 / 7F 02 / 8B C2 / C3`）
00753116  89 7B 10                [rec+0x10] := 结果
0075311B  0F 9F C0                返回 pending > 0
```

**查询** `sub_752CAC(eax=self, edx=word[怪物+0x278]=等级, ecx=[PEnvir+0x44]=地图名,
[ebp+8]=&outCount)`：

```
00752CD5  80 7B 28 00 / 74 3D     总闸没开 -> 返回 nil
00752CDB  8B 73 1C                记录数
00752D01  E8 1E 04 00 00          call 0x753124   ; 逐条匹配
00752D0C  C6 43 28 00             命中即把总闸关掉 -> 每个 Run 周期全服最多一次
```

**逐条匹配** `sub_753124`：

```
00753152  0F B7 03 / 3B 45 FC / 7F 48   minLevel > 等级 -> 不匹配（即门是 <=）
0075315A  83 7B 10 00 / 7E 42          pending <= 0 -> 不匹配
00753160  8B 73 14 / 85 F6 / 74 23     map 表为 nil -> 匹配（不限地图）
0075316B  FF 52 14 / 48 / 7C 19        map 表为空 -> 匹配
00753177  E8 40 8B CB FF               UpperCase(地图名)
00753184  FF 51 54                     TStringList.IndexOf
00753187  40 / 7E 18                   IndexOf == -1 -> 不匹配
0075318A  8B 7B 0C                     返回 prize 表
0075318D  8B 43 10 / 8B 55 08 / 89 02  *outCount := pending
00753197  89 43 10                     pending := 0
0075319A  E8 A1 51 CB FF / 89 43 08    lastTick := **新取的** GetTickCount
```

### 4.5 落地路径

```
0071FEA7  8D 45 E8 / 50                lea eax,[ebp-0x18] / push eax  ; &outCount
0071FEAE  8B 80 28 01 00 00 / 8B 48 44 ecx = [PEnvir+0x44]            ; 地图名
0071FEBA  0F B7 90 78 02 00 00         edx = word [self+0x278]        ; 等级
0071FEC1  A1 F4 71 7D 00 / 8B 00
0071FEC8  E8 DF 2D 03 00               call 0x752CAC
0071FED0  83 7D D0 00 / 0F 84 D3 00 00 00   nil -> 直接去金币
0071FEDA  83 7D E8 00 / 0F 8E C9 00 00 00   outCount<=0 -> 同上
0071FEE4  8B 5D E8                     外层 = outCount 圈
0071FEF6  FF 52 14                     内层 = prize 表 Count
0071FF17  FF 57 0C                     Strings[i]
0071FF24  E8 2B DF 02 00               call 0x74DE54  = MakeItemByName
0071FF30  74 74                        造不出来 -> je 0x71FFA6（外层 dec ebx，整圈作废）
0071FF32  6A 01 / 6A 00 / 6A 00        [ebp+0x14]=1 / [ebp+0x10]=0 / [ebp+0xC]=0
0071FF38  68 34 01 72 00               [ebp+8] = "世界掉落"
0071FF3D  B9 03 00 00 00               mov ecx,3                       ; 半径
0071FF45  8B 45 F4                     eax = [ebp-0xC]                 ; 落点宿主
0071FF48  E8 53 89 04 00               call 0x7688A0
0071FF4F  74 43                        失败 -> 0x71FF94 FreeAndNil，只丢这一件
0071FF57  E8 0C 46 06 00               call 0x784568                   ; 取物品名
0071FF7A  8D 45 84 / BA 03 / E8 09 59 CE FF   _LStrCatN(name, "=", "1")
0071FF8F  FF 51 38                     账本 [ebp-0x38].Add
```

两条与其他腿不同的地方：

1. **`[ebp+0xC]`（item creator）压的是 0**，而段2 压的是杀手
   （`0x71FDC6 8B 45 F8 / 50 push [ebp-8]`）。
2. **不调 `[item vmt+0x28]`**。`0x71FF24` 到 `0x71FF48` 之间没有虚调用，所以世界掉落物的
   耐久停在构造函数给的值，与掉落控制那条腿（`0x720204 FF 51 28`）相反。

账本行的拼法：`_LStrCatN` 的拷贝循环 `0x4058E6 mov eax,[esp+ebx*4+0x18]` 自
`ebx = argCnt` 递减，先拷第一个被 push 的实参，所以成串是 `<物品名>=1`，
与金币行 `0x720028 mov edx,0x720148` 的 `"金币=" + 数额` 同形，也就是本仓
`KeyValuePair<name, count>` 的约定。

### 4.6 C# 落点

新文件 `GameSvr/Maps/NativeWorldScatter.cs`：

| C# | 原生 |
|---|---|
| `NativeWorldScatterRecord` | 24 字节记录 |
| `NativeWorldScatterIni` | `TIniFile`（只读、无副作用；本仓 `ConfFile` 会在文件缺失时 `File.Create`，原生这条路连 ini 对象都不建） |
| `NativeWorldScatterMgr.LoadConfig` | `sub_752D40` |
| `NativeWorldScatterMgr.Run` | `sub_75307C` |
| `NativeWorldScatterMgr.TickRecord` | `sub_7530D8` |
| `NativeWorldScatterMgr.Query` / `MatchRecord` | `sub_752CAC` / `sub_753124` |
| `NativeWorldScatterMgr.ParseMapList` | `sub_7531D0` |
| `NativeWorldScatterMgr.Clear` | `sub_752B8C` |
| `NativeWorldScatter.Scatter` | 段3 本体 `0x71FEA7-0x71FFA7` |

接入点在 `TBaseObject.Base.cs` 的 Die 阶梯，`ScatterBagItems`（段2 落地）之后、
`ScatterGolds` 之前，同在 `if (!scatterBlocked)` 内——段3 与金币同受那三道门。

---

## 5. `g_Config.nDropItemRage` 字段删除的证据

| 检查 | 结果 |
|---|---|
| 全工作树 `nDropItemRage` / `DropItemRage` | 删除前只剩 `GameSvrConfig.cs:617`（声明）与 `:1453`（默认赋值），其余全是记录来历的注释与 docs |
| ini 解析入口 | 无：该名不出现在任何 `ReadString`/`ReadInteger` 调用或配置模板里 |
| 反射往返 | 无：`GameSvr` 全树无 `GetFields(` / `GetProperties(` / `typeof(GameSvrConfig)`，也没有任何序列化器碰 `g_Config` |
| 原生依据 | `DropItemRage` / `nDropItemRage` / `DropItemRange` / `ItemRage` / `DropWide` 五个名在 ASCII、大小写不敏感 ASCII、UTF-16LE、GBK 四路全镜像 **0 命中**；15 个 `sub_7688A0` 调用点里 14 个是立即数半径 |

删除后 `dotnet build GameSvr` 0 错、15 警告。

---

## 6. 验证

**全部审计工具用 `-o` 隔离目录构建后再跑**（共享 `D:\loym2\.claude\wt2\Build\...`
的 exe 不可信，见 `docs/ys_baselines_reference.md` §5）。异常堆栈里的源码路径均为
`D:\loym2\.claude\wt3\droporder\...`，确认跑的是本树的二进制。

| 工具 | 隔离目录 | 结果 |
|---|---|---|
| `NativeDropControlRuntimeCheck` | `%TEMP%\droporder_audit1` | **PASS** `timed-uint-rollover map-equality-reset world-greater-reset bucket-isolation chain=A,C,B rng-order=controlled,ordinary gate-scope=ordinary-only scatter-range=4 failure-lossy=true type7-final-dura=virtual pile-init=noop` |
| `NativeWorldScatterCheck`（新增） | `%TEMP%\droporder_audit3` | **PASS** `scatter-range=3 warmup=1800000ms run-gate=1000ms prize-keys=prize..prize99 min-level=le map-optional consume-disarms one-drop-per-run` |
| `NativeDropRngSequenceCheck` | `%TEMP%\droporder_audit2` | 编译错已修，**运行仍失败**（见 §7.2） |

`dotnet build GameSvr/GameSvr.csproj`：**0 错误 / 15 警告**（与基线一致，未新增）。

---

## 7. BLOCKED 与遗留

### 7.1 段3 的两处 BLOCKED

1. **配置目录**。原生是
   `<[[0x7D6B2C]]> + "EngineConfig\" + <模块名 [self+0x18]> + "\"`
   （`sub_4D9F58`，四段 `_LStrCatN`），且解析结果还要过
   `0x4D9EB2 cmp byte [Objects[i]+0x100],0` 这个未定性旗标，为 0 就清空路径。
   模块框架整体不在本仓，C# 用 `Path.Combine(M2Share.sConfigPath,
   g_Config.sEnvirDir, "世界爆率文件1.txt")` 替身。
   **文件名不 BLOCKED**——`0x4D9E43` 把 `.txt` 拼在键名后写进 `main.ini`。
2. **每秒计时的驱动方**。原生由模块框架调 `VMT+0x04`；本仓对应的每 tick 入口是
   `UserEngine.Run`（`UsrEngn.cs:2321` 的 `NativeStallExpiryTick.Run()` 旁边），
   而 `UsrEngn.cs` 是本分支的禁改文件。现状是散落路径在查询前自驱一次
   `Run(GetTickCount())`。`Run` 自带 `0x75308E` 的 1000ms 闸，所以：

   **交主代理的一行接线方案**：在 `GameSvr/UsrSystem/UsrEngn.cs`
   `UserEngine.Run()` 里 `NativeStallExpiryTick.Run();` 之后加

   ```csharp
   NativeWorldScatter.Instance.Run(HUtil32.GetTickCount());
   ```

   接上以后 `NativeWorldScatter.Scatter` 里的自驱调用即成空转（同一毫秒内
   `now - _lastRunTick <= 1000`），无需再改散落路径。

### 7.2 `NativeDropRngSequenceCheck` 的产品缺陷（与本分支无关）

编译错已修（`Program.cs:109` 的方法组缺 `()`）。修好后仍在 `PinJewelTableWrite`
崩：

```
System.IndexOutOfRangeException
  at GameSvr.NativeJewelStoneTable.Apply(TUserItem, Int32)
     in GameSvr\Items\NativeItemPlus28.cs:line 482
```

`NativeItemPlus28.cs:479-484` 先按 `NativeRecordSize = 208 (0xD0)` 分配
`NativeRecord`，再往 `ItemPlus100RecordOffset = 0xE0 (224)` 及 `+1/+2` 写。
全树其他编解码器（`LegacyUserItem208Codec` / `NativeMailAttachmentCodec` /
`NativeMerchantGoodsCodec` / `NativeAccountStorageClient.ItemSize`）一致认为
物品记录就是 208 字节，那么以 `item+0x20` 为基址的记录放不下 `item+0x100`。
只要 `btValue[12]`（宝石类型）非 0，这条路必抛。要坐实是偏移错还是记录长度错，
需要原生物品记录的实际跨度，超出本分支证据范围，**留 BLOCKED**。
另注：该工具需要在 exe 同目录放一个内容为 `[Server]` 的 `!Setup.txt`。

### 7.3 其他

- 段3 的 `[ebp+8]` 标签串「世界掉落」未建模：本仓 `DropItemDown` 没有标签形参，
  日志前缀由 `boDieDrop` 推出（`TBaseObject.cs:1027`），与
  「(爆天赐)」「怪物死亡:」「死亡爆出-」三个同类标签的既有处理一致。
- `TWorldScatterMgr` 的 `[self+0x20]`（构造函数 `0x752C29` 清零）在本轮读到的四个
  例程里没有第二个访问点，未建模。
- `docs/eqv_shard19_20260814.md` 第 66-67、169 行与
  `docs/completeness_audit_20260814.md` 第 188 行仍按旧归属描述
  DROP-33（own-table 走共享 `ScatterBagItems`、世界腿半径 3），已被
  `drop33_owntable_scatter_20260814.md` 与本报告推翻，待重写。
