# 眼神插件补丁图谱重建 —— 全库 `patch_target_vas` 与完成度口径重算

日期 2026-08-14 · 分支 `w/ys-atlas` · 不改任何 `.cs`，纯取证与工具

## 摘要

用 ys-page2 建立的状态标签判据重建了全库补丁图谱，407 个安装点全部归属到 107 个特性，
并把每个站点改写的 M2Server 地址与字节数解到位。

**主要结论与立项时的假设相反，如实记录**：`_ysgui2/g09.json` 确实少了 101 个 trampoline
站点，但这 101 个站点**没有带来任何一个新的补丁目标地址**，因此 `patch_target_vas`
的键级覆盖面**一个键都没有增加**（前后都是 214 个键）。原因见下节，是插件的
apply/revert 结构决定的，不是漏检。

真正的产出是另外两项：

1. 把 `patch_target_vas` 从「地址集合」升级成**逐站点底账**（arm / 目标 VA / 补丁字节数 /
   状态字面量），其中 76 个特性的 apply 载荷是 trampoline，此前只有 revert 的还原字节。
2. 据此给 380 个键重新定级，分出 **61 个真实缺口**和 **82 个确认无需移植**，此前的
   184 个 `LABEL_ONLY` 被拆开了。

## 一、复核：407 → 107 可复现

原样复跑 `tools/ys_page1_census.py`（capstone 5.0.7，Python 3.11）：

```
delayed dump Themida region 0x10400000..0x11400000: 16506511/16777216 nonzero bytes
keys with a resolved field: 372   patch feature labels: 107   installer sites: 407
```

产出与 master 上的 `docs/ys_patch_label_atlas.tsv` **逐字节一致**（仅 git 检出的
CRLF 差异）。站点构成：

| 安装器 | 函数 | 站点数 |
|---|---|---|
| 裸字节写 | `sub_10033340` | 306 |
| trampoline builder 1 | `sub_10032CC0` | 71 |
| trampoline builder 2 | `sub_10032FD0` | 30 |

跨版本佐证：107 个特性名里 **106 个**在 2.0.7 运行期转储
（`staging\questinfo_runtime_dump\yanshen2_0_7_dll.memory.bin`，28.4 MB）中同样以状态
字面量形式存在；唯一缺席的是 `获取玩家对象函数`，即 2.0.8 新增特性。

## 二、把「站点 → 它改写的地址」解准

ys-page2 的取址规则是「call 前 14 条指令内任意 `0x400000..0x800000` 的 `push imm`」。
这对 306 个 memcpy 站点成立，但漏掉 26 个 trampoline 站点——模板构造块夹在实参 push
和 call 之间，把实参挤出了窗口。

从 `sub_10032CC0` 序言读出实参形状，并在 `0x100cf09d` 处逐字节确认：

```
0x100cf078  6A 3B                push 0x3b            ; arg5 模板 dword 数
0x100cf07a  8D 85 44 FB FF FF    lea eax, [ebp-0x4bc]
0x100cf080  50                   push eax             ; arg4 模板
0x100cf081  68 77 79 6B 00       push 0x6b7977        ; arg3 hook end
0x100cf086  68 6B 79 6B 00       push 0x6b796b        ; arg2 hook start
0x100cf08b  68 6B 79 6B 00       push 0x6b796b        ; arg1 hook start
0x100cf090  8D 85 68 5E FF FF    lea eax, [ebp-0xa198]
0x100cf096  50                   push eax             ; arg0 出参对象
0x100cf097  8D 8D 38 5F FF FF    lea ecx, [ebp-0xa0c8]
0x100cf09d  E8 1E 3C F6 FF       call 0x10032cc0
```

即倒数第 1 个 host push 是 hook start、倒数第 3 个是 hook end。把窗口放宽到**上一个
安装点**（防止跨特性污染）再取这个三元组：

* 两条规则都触发的 **67** 个站点，结果完全一致；
* **8** 个站点是严格扩展，补回被 14 条窗口切掉的 hook end，无一处冲突；
* 剩下 **26** 个全部解出。407/407 站点均有目标 VA。

`sub_10033340(payload, len, va, va)` 的字节数同样解出（两个 VA push 之后的立即数），
例如 `0x100a9807: push 0x6e7930 / push 0x6e7930 / push 8` 即在 `0x6e7930` 写 8 字节。

**只有 hook start 是补丁目标**——被改写的是 `[start, end)` 区间，`end` 只是区间上界，
故单列 `span_end` 而不并入目标地址集合。这也正是 g09.json 用 `va` / `va_end` 两个字段
表达的含义。

## 三、与 `g09.json` 逐条 diff

`docs/ys_patch_atlas_diff.tsv`，407 行：

| | 站点数 |
|---|---|
| 与 g09 共有（`SAME`） | 306 |
| 重建独有（`NEW`） | 101（tramp1 71 + tramp2 30）|
| 标签冲突 | **0** |
| 目标 VA 冲突 | **0** |
| 字节数冲突 | **0**（305 可比，1 个未解见下）|

共有站点上标签、目标 VA、字节数三项全部与独立提取的 g09 吻合，是对本方法的强佐证。
未解的 1 个：`指定地图编号摆摊 @0x100acf06 -> 0x6e7934`，长度不是立即数，留空不猜。

### 新增 101 站点归属哪些键

101 个站点分布在 **76 个特性**上。逐一列出见 `docs/ys_patch_sites_atlas.tsv`
（筛 `kind != memcpy`），站点数最多的几个：

| 特性 | trampoline apply 站点数 | 目标 VA |
|---|---|---|
| `永久属性` | 12 | `0x73d9cf` … `0x73da3a` |
| `全屏拾取` | 4 | `0x6b795c` `0x6b796b` `0x6b7a25` `0x6b7a2f` |
| `复活戒指改cd` | 3 | `0x73c4f2` `0x73c47a` `0x743751` |
| `中毒时间上限`/`新穿戴触发`/`特殊宝宝`/`特殊属性`/`盘古穿戴触发`/`盘古魔法攻击触发`/`英雄穿戴触发`/`装备来源`/`冰咆哮主属性切换`（9 个）| 各 2 | — |
| 其余 64 个特性 | 各 1 | — |

合计 `12 + 4 + 3 + 9×2 + 64 = 101` 站点，落在 `3 + 9 + 64 = 76` 个特性上。

### 为什么新增 101 站点却没有新增地址

按 arm 分类（状态字面量 `已*` 为 apply、`未*`/`待*` 为 revert）：

| 安装器 | APPLY | REVERT |
|---|---|---|
| memcpy | 107 | 199 |
| tramp1 | 71 | 0 |
| tramp2 | 30 | 0 |

**101 个 trampoline 站点全部是 apply 臂，无一例外。** 每个特性的 revert 臂一律用 memcpy
把原字节写回同一地址，所以 99 个 trampoline 目标 VA **全部**已被某个 memcpy 站点覆盖
（差集为空）。g09 枚举了全部 306 个 memcpy 站点，也就等于已经拿到了每一个被改写的地址。

特性级的 apply/revert 组合：

| apply 臂 | revert 臂 | 特性数 |
|---|---|---|
| tramp1 | memcpy | 49 |
| tramp2 | memcpy | 25 |
| memcpy | memcpy | 25 |
| memcpy | 无 | 5（`刺杀剑术`/`基本剑术`/`施毒术`/`烈火剑法`/`逐日剑法`，均为 `已重设`，写值不可逆）|
| memcpy+tramp2 | memcpy | 2 |

**增量在载荷不在地址**：76 个特性的 apply 载荷是 trampoline 模板，g09 对这些特性只记录了
revert 的还原字节，新装的代码此前无处可查。

## 四、权威映射不是单一来源

`docs/ys_patch_target_vas.tsv`（214 个键）按来源拆分：

| 来源 | 键数 | 机制 |
|---|---|---|
| `label-atlas` | 106 | 三个安装器的调用点（本次重建）|
| `extreme-map` | 96 | apply 臂用 `mov [绝对地址], eax` 直写，**没有安装点调用** |
| `legacy-only`（g11）| 12 | 立即数宽度改写（width 1/4），同样没有安装点调用 |

**标签图谱不是 g09/g11 的超集**，不能用它替换现有来源。12 个 g11 独有键已核实全部来自
`g11.json`（如 `攻城修改 site=0x100b32c9 target=0x65c3b1 width=4`），g09 中 0 条。
权威映射必须是三者并集，`patch_source` 列记录每个键的来源。

## 五、完成度口径重算

`ys_gui_matrix.py` 的 `state` 只看本仓 C# 源码，不回答「原插件到底改没改 M2Server」，
所以一个 `LABEL_ONLY` 既可能是真缺口，也可能这个开关本来就不做事。补上二进制侧两根轴：

* **原生补丁**：键出现在上述三来源并集中；
* **插件读取**：`cmp dword [reg+OFF], 0x1F4` 全库单遍扫描按 `OFF` 归桶（原为每键一次
  45 MB 扫描），`OFF` 由序列化器 `sub_10004140` 给出，排除序列化器与加载器自身区间。
  与 ys-page2 逐键扫描在第 1 页 34 个键上比对，**0 处不一致**。
  「无消费者」只对 delayed 转储断言（另一份那 16 MB Themida 区全零）。

`docs/ys_patch_completeness.tsv`，380 个键：

| 判定 | 键数 | 含义 |
|---|---|---|
| `NATIVE_OK` | 153 | 插件改了 M2Server，本仓也实现了 |
| `NATIVE_GAP` | **61** | 插件改了，本仓无引擎层落点 —— 真实缺口 |
| `PLUGIN_SIDE_ONLY` | 47 | 不改 M2Server，但插件自己读这个键 |
| `PARAM_OF_PATCHED` | 37 | 自身无补丁，但它是某个已打补丁特性的参数（如 `刺杀剑术_A值`），是载荷里的数值，不独立移植 |
| `EQUIVALENT_BY_ABSENCE` | **82** | 45 MB 内既无补丁也无一次读 —— 没有可移植的东西 |

对账：`NATIVE_OK 153 + NATIVE_GAP 61 = 214`，与有补丁目标的键数一致；五项合计 380。

### 口径变化

* 原 **184 个 `LABEL_ONLY`** 拆为：51 真实缺口 / 35 插件侧自用 / 24 已打补丁特性的参数 /
  74 确认无需移植。**多数 `LABEL_ONLY` 不是缺口。**
* 唯一的 **`MISSING`**（`获取玩家对象函数`）反而是缺口第 2 名：4 个 apply 站点 177 字节，
  且是 2.0.8 相对 2.0.7 的新增特性。
* 原判 `IMPLEMENTED` 的键里有 **4 个**落到 `EQUIVALENT_BY_ABSENCE`（`最大装备数量`
  `红名K值` `非红名K值` `随机极品`），即本仓实现了插件既不打补丁也不读取的东西。
  **只报不改**，需人工确认是否属于原生 M2Server 行为而非插件行为——若属原生行为则
  本仓实现正确，本判定只说明它不该记在「眼神插件复刻」的账上。

## 六、下一轮优先级建议

排序口径：生产配置里开着的优先（参考部署 `D:\光头卧龙\mud2.0\Mir200\Gs1\config.json`），
再按 apply 侧补丁字节数。61 个缺口中 **31 个在生产配置里是开着的**。完整队列见
`ys_patch_completeness.tsv`，前 12 名：

| # | 键 | state | apply 站点 | 字节 | 页 |
|---|---|---|---|---|---|
| 1 | `屏蔽属性提升提示` | LABEL_ONLY | 31 | 215 | 盘古1 |
| 2 | `获取玩家对象函数` | MISSING | 4 | 177 | 扩展/脚本 |
| 3 | `盘古高级属性` | LABEL_ONLY | 3 | 68 | 盘古1 |
| 4 | `随身仓库` | LABEL_ONLY | 3 | 57 | 盘古1 |
| 5 | `免毒符` | LABEL_ONLY | 12 | 42 | 配置2 |
| 6 | `全屏拾取` | SCRIPT_ONLY | 4 | 36 | 配置1 |
| 7 | `施毒术` | SCRIPT_ONLY | 1 | 31 | 盘古3 |
| 8 | `禁止发言不提示` | LABEL_ONLY | 3 | 18 | 配置2 |
| 9 | `ServerSay函数` | LABEL_ONLY | 1 | 12 | 盘古2 |
| 10 | `中毒时间上限` | LABEL_ONLY | 2 | 10 | 盘古3 |
| 11 | `装备来源` | SCRIPT_ONLY | 2 | 10 | 配置1 |
| 12 | `武器绿毒` | LABEL_ONLY | 1 | 7 | 盘古2 |

补充建议：

* 第 24–31 名（`战士合击` `攻城修改` `无极真气` `法道合击` 及 4 个盘古范围键）字节数为 0
  是因为它们走 g11 立即数改写、没有安装点，**不代表工作量小**，应按立即数语义单独处理。
* `永久属性`（12 站点 107 字节）虽然生产配置关着，但补丁面是全库第 3 大，建议在开着的
  一批清完后立刻接手。
* 反编译远端函数体时用 **delayed** 那份转储：非 delayed 那份绝对操作数未重定位
  （`push 0x102b02e4` vs `push 0x57ef02e4`，差 `0x47C40000`），且被 Themida 搬走的函数体
  只在 delayed 那份有内容。本次站点枚举用的是非 delayed 那份（安装点都在主模块内，
  两份一致），消费者否定结论用的是 delayed 那份。

## 七、复跑

```
python tools/ys_page1_census.py          # 407 站点 / 107 特性，写标签图谱
python tools/ys_patch_atlas_rebuild.py   # 逐站点底账 + 与 g09/g11 的 diff + 权威映射
python tools/ys_patch_completeness.py    # 380 键重新定级 + 移植队列
```

产物：

| 文件 | 行数 | 内容 |
|---|---|---|
| `docs/ys_patch_sites_atlas.tsv` | 407 | 逐站点底账：arm / 目标 VA / span / 状态字面量 |
| `docs/ys_patch_target_vas.tsv` | 214 | 键 → 补丁目标 VA 权威映射 + 来源 + 与现用图谱 delta |
| `docs/ys_patch_atlas_diff.tsv` | 407 | 逐站点 SAME/NEW 与两侧目标 VA、字节数 |
| `docs/ys_patch_completeness.tsv` | 380 | 每键判定、补丁面、消费者数、移植队列排序依据 |
