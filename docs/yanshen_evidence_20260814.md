# 眼神(Yanshen) 98 条「证据债」收敛报告

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\ys-evidence`　分支：`w/ys-evidence`　基线：`39ebe3b9`（建树时的 `master`）
- 仓库真实根：`D:\loym2\LyoMir2-master`（`D:\loym2` 本身不是 git 仓库，`.git.broken-20260810` 是废弃空库）
- 目标：收敛 `docs/yanshen_completeness_audit_20260814.md` §0.3 认定的 **98 条 `UNPROVEN-IMPL`**
  （F2 脚本 API 68 + F6 `!!!!` 命令隧道 30）——"C# 已实现并接线，但在本仓找不到原生 VA 佐证"。
- 纪律：**未改任何 `.cs`**，本轮只增本文档。所有结论逐条落到 VA + 字节；找不到就写"仍不可证"。

---

## 0. 结论先行

| 指标 | 值 |
|---|---:|
| 本轮**新坐实**（拿到原生 VA + 字节） | **78 / 98** |
| 仍不可证 | **20 / 98**（18 条本仓三份底本全无痕迹 + 2 条 C# 自造别名） |
| 收敛后严格有据完成度 | **376 / 660 = 57.0%** |
| 收敛后真值区间 | **57.0% ~ 59.7%**（原 45.2% ~ 60.0%，区间宽度 14.8pt → **2.7pt**） |
| 战斗主干五项 | **五项全部坐实到"隧道 + 操作码 + 元数 + 开关 + 实现函数"级**；同时查出 3 处真实偏差 |

**一句话**：98 条证据债里 78 条不是"证不出来"，是**过去找错了底本**。眼神的脚本 API 不是原生导出符号，
而是随包 `AllFuc.pas` 里的 Pascal 薄包装——它们把参数编码成 `!!!!` 前缀的魔法串，
经宿主 `GetBagItemCount` / `Give` / `GetSignInActPrizer` 递进插件，由插件内一个 41 路 + 一个 38 路跳转表分派。
**这两张表就在 2.08 转储里，完整、未虚拟化、可逐字节引用。**

---

## 1. 底本判定（第一步，也是过去卡住的原因）

### 1.1 M2 主底本里没有眼神

对 `staging/_reunpack_work/flat_image.bin`（ImageBase `0x400000`，17,661,952 B）做 GBK 特征串普查：

```
施毒术 0   眼神 0   盘古 0   刀刀切割 0   半月带毒 0   武器绿毒 0
AllFuc 0   NpcFuc 0   yanshen 0   集成函数 0   爱心分割 0
```

98 条里的 18 个"官方例子名"（`ysgetg`/`yssay`/`ysattact` …）在主底本中同样 **0 命中**；
主底本里 7 个形如 `ys*` 的 token（`ysdzc` `ysffd` `ysiyh` `yslzr` `ysmxt` `ysrzmhs` `ysugr`）
全部落在 VMProtect 打包段的随机字节里，与眼神无关。

⇒ **眼神证据不在 M2 主底本。** 这一条本身就解释了审计为什么"在本仓找不到 VA"。

### 1.2 正确底本：眼神 2.0.8 DLL 转储（且要选对那一份）

| 转储 | 大小 | PE 首选基址 | **实际装载基址** | 用途 |
|---|---:|---|---|---|
| `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin` | 45,821,952 | `0x57C40000` | **`0x10000000`**（已重定位） | **本报告全部 VA 的底本** |
| `staging/yanshen208_strparam_runtime_dump_delayed_20260719/…` | 45,821,952 | `0x57C40000` | `0x57C40000`（未重定位） | 只用来解运行期填充的全局 |
| `staging/questinfo_runtime_dump/yanshen2_0_7_dll.memory.bin` | 28,446,720 | `0x7A760000` | `0x10000000` | 2.07 交叉核对 |

两份 2.08 转储 **逐字节差 19,162,403 处、343,040 段，且几乎每段恰好 2 字节**——
这正是同一映像在 `0x10000000` 与 `0x57C40000` 两个基址下重定位的指纹（`00 00 00 10` vs `00 00 C4 57`）。
过去把 delayed 那份当同基址读，会得到满屏错位地址。**本报告一律用非 delayed 那份，base = `0x10000000`，file offset = RVA。**

节区可读性（非 delayed 份）：`.text` RVA `0x1000` len `0x27BC40` zero=0.093，`.rdata` RVA `0x27D000` len `0x91154` zero=0.334
—— 代码与字符串池完整；`.tvmp`/`.p7q` 全零（Themida VM 段，见 §6）。

### 1.3 关键转折：`AllFuc.pas` 不是"声明"，是**实现**

盘上有加密原本与已解密副本：

- 生产加密本：`D:\光头卧龙\mud2.0\Mir200\Envir\CommonScripts\眼神专用\AllFuc.pas`（31,250 B，GBK 密文）
- 解密副本：`D:\loym2\staging\_ys208_plain\Envir__CommonScripts__眼神专用__AllFuc.pas`（32,635 B，明文）

解密本里每个 `ys_*` 都是**薄包装**，把实参拼成 `!!!!` 魔法串再走宿主标准 API：

```pascal
function ys_ShiDu(Player:TPlayer;shijian,leix,hp,gailv,fanwei,TargetX,TargetY,Canl,isqun:integer):integer;
begin
  res:='!!!!集成函数,5,'+inttostr(shijian)+','+…+','+inttostr(isqun)+'$';
  result:=Player.GetBagItemCount(res);      // ← 宿主 API 被插件挂钩
end;
```

⇒ **F2（脚本 API 名）与 F6（`!!!!` 隧道）根本是同一张证据面**，审计把它们当两面各自记债，所以两面同时"无证"。
只要证到隧道解析器，两面一起坐实。

---

## 2. 原生锚点（本轮逐字节亲验，capstone 5.0.7）

### 2.1 `!!!!` 标记串表 —— `.rdata` `0x102BE81C .. 0x102BE8B0`

16 字节对齐、NUL 补齐的 GBK 字面量。原始 hexdump：

```
102BE81C  21 21 21 21 BC AF B3 C9 BA AF CA FD 00 00 00 00   "!!!!集成函数"
102BE82C  21 21 21 21 B0 AE D0 C4 B7 D6 B8 EE 00 00 00 00   "!!!!爱心分割"
102BE83C  21 21 21 21 68 71 C8 A1 73 6A B4 C1 00 00 00 00   "!!!!hq取sj戳"
102BE84C  21 21 21 21 7A 64 D2 E5 BB D8 CA D5 00 00 00 00   "!!!!zd义回收"
102BE870  21 21 21 21 B8 F8 D3 EB D4 AA CB D8 00 00 00 00   "!!!!给与元素"
102BE880  21 21 21 21 BB F1 C8 A1 D4 AA CB D8 00 00 00 00   "!!!!获取元素"
102BE890  3A 00 00 00                                       ":"   ← 四条中文隧道的分隔符
102BE894  21 21 21 21 B6 A8 D2 E5 C9 CB BA A6 00 00 00 00   "!!!!定义伤害"
102BE8A4  21 21 21 21 D3 A2 D0 DB BC AB C6 B7 00 00 00 00   "!!!!英雄极品"
```

全镜像 `!!!!` 只有这 **8** 处命中，即**插件认得的 `!!!!` 前缀恰好 8 个**，一个不多。
紧邻的 `":"` 字面量与 `AllFuc.pas` 里 `给与元素/获取元素/定义伤害/英雄极品` 四条用 `':'` 分隔的写法严丝合缝。

### 2.2 隧道派发器 `sub_1005E4D0` —— 8 个标记各一处代码引用

xref 普查：8 个标记串各 **恰好 1 处** dword 引用，且全部落在同一函数内。

| 标记 VA | 比对点 VA | 字节 | 命中后动作 |
|---|---|---|---|
| `0x102BE81C` 集成函数 | `0x1005E578` | `68 1C E8 2B 10` | `mov ebx,[0x1031BFB8]` / `call dword [ebx]`（= cfg+4 槽） |
| `0x102BE82C` 爱心分割 | `0x1005E58C` | `68 2C E8 2B 10` | `call 0x1005E470` → `sub_1005DBA0` |
| `0x102BE83C` hq取sj戳 | `0x1005E65C` | `68 3C E8 2B 10` | 门 `[cfg+0x538]>0x1F4`；返回 `[player+0xE0]` |
| `0x102BE84C` zd义回收 | `0x1005E6D1` | `68 4C E8 2B 10` | 门 `[cfg+0x954]>0x1F4`；`call [[0x1031BFB4]]` |
| `0x102BE870` 给与元素 | `0x1005E794` | `68 70 E8 2B 10` | 门 `[cfg+0x664]>0x1F4`；按 `":"` 切分 |
| `0x102BE880` 获取元素 | `0x1005E7A5` | `68 80 E8 2B 10` | 同上 |
| `0x102BE894` 定义伤害 | `0x1005EDB4` | `68 94 E8 2B 10` | — |
| `0x102BE8A4` 英雄极品 | `0x1005EF8C` | `68 A4 E8 2B 10` | — |
| `0x102BE890` `":"` | `0x1005E7F5` 等 9 处 | `68 90 E8 2B 10` | 分隔符 |

`0x1031BFB8` / `0x1031BFB4` 由 `0x100016F0` / `0x100016E0` 两个 accessor 桩赋值为 `cfg+4` / `cfg+0`：

```
100016F0  A1 FC BE 31 10   mov eax,[0x1031BEFC]     ; cfg
100016F5  83 C0 04         add eax,4
100016F8  A3 B8 BF 31 10   mov [0x1031BFB8],eax     ; = cfg+4
```

⇒ `集成函数` 走 `cfg+4` 里的运行期函数指针（两份转储中该槽均为 0，见 §6-C1）；
`爱心分割` 是**直调**，可完整静态跟踪。

### 2.3 `集成函数` 操作码分派 —— `sub_100761A0` 内，41 路跳转表 `0x10077A78`

```
100766C0  6A 0A                    push 0xA                      ; 十进制
100766C7  E8 74 C2 F9 FF           call 0x10012940               ; StrToInt
100766E7  83 E8 01                 sub  eax,1                    ; N-1 → 0 基
100766F0  83 BD AC FE FF FF 28     cmp  [ebp-0x154],0x28         ; 上界 40
100766F7  0F 87 F5 12 00 00        ja   0x100779F2               ; default
10076703  FF 24 8D 78 7A 07 10     jmp  dword [ecx*4+0x10077A78] ; 41 臂
```

**`sub 1` + `cmp 0x28` ⇒ 合法操作码恰为 1..41**，与 `AllFuc.pas` 用到的 `集成函数,1` .. `集成函数,41` 完全吻合。

### 2.4 `爱心分割` 操作码分派 —— `sub_1005DBA0` 内，38 路跳转表 `0x1005E3D8`

链路可完整静态跟踪：
标记 `0x102BE82C` → 比对 `0x1005E58C` → `call 0x1005E470`（蹦床，`sub_1005E470` 尾部 `call 0x1005DBA0`）
→ `sub_1005DBA0` → `jmp @0x1005DD13` → 表 `0x1005E3D8`（38 臂，`^1^ .. ^38^`）。

### 2.5 `GetSignInActPrizer` 通道 —— `lucker2` / `libmysql` 哨兵

```
102C02EC  "lucker2"    ← 唯一 xref 0x100879D8:  BA EC 02 2C 10   mov edx,0x102C02EC
100879E0  8A 08        mov cl,[eax]
100879E2  3A 0A        cmp cl,[edx]          ; 逐字节串比
102C0324  "libmysql"   ← 唯一 xref 0x10087DD9
```

与 `AllFuc.pas` 里 `lucker2:='lucker2'` / `lucker2:='libmysql'` 的第二实参哨兵完全对应。

### 2.6 物品给予后缀标签表 —— `0x102BE6D0`

```
102BE6D0  64 61 74 61 00 00 00 00   "data"    xref 0x10058051
102BE6D8  2C 00 00 00               ","
102BE6DC  6A 70 32 79 73 00 00 00   "jp2ys"   xref 0x1005818D
102BE6E4  7A 64 79 6C 79 00 00 00   "zdyly"   xref 0x10058422
102BE6EC  A1 AD A1 AD 00 00 00 00   "……"      xref 0x1005845E
```

四个 xref 全部落在 `Player.Give` 挂钩 `0x10058051..0x1005845E` 内，对应 `AllFuc.pas` 的
`$data` / `$jp2ys` / `$zdyly` / `……` 四种给予后缀。

### 2.7 开关门的真实形状

臂内门的统一样式（以 `施毒` 臂为例）：

```
10076AD9  A1 44 C2 31 10        mov eax,[0x1031C244]
10076ADE  81 38 F4 01 00 00     cmp dword [eax],0x1F4     ; > 500 才算开
10076AE4  7E 07                 jle skip
10076AE6  C7 45 E8 64 00 00 00  mov [ebp-0x18],0x64
10076AEE  83 7D E8 64           cmp [ebp-0x18],0x64
10076AF2  75 48                 jne skip
```

门全局由 accessor 桩解出所属配置字段：

```
100021D0  A1 E0 C0 31 10 / 05 24 05 00 00 / A3 40 C2 31 10   → 0x1031C240 = cfg2 + 0x524
100021E0  A1 E0 C0 31 10 / 05 1C 01 00 00 / A3 44 C2 31 10   → 0x1031C244 = cfg2 + 0x11C
```

⇒ **41 个操作码里 33 个共用同一道门 `cfg2+0x11C`**；`3` 号自定义伤害另用 `cfg2+0x524`；
`25/26/28/30/31/37` 各有专用门；**`2` 号麻痹臂完全无门**。

---

## 3. F2 —— 68 条脚本 API 逐条判定

### 3.1 汇总

| 归类 | 数 | 证据强度 |
|---|---:|---|
| `集成函数` 隧道（操作码 + 臂 VA + 实现 VA 齐全） | **35** | A |
| `爱心分割` 隧道（`^N^` 臂 VA + 实现 VA 齐全） | **5** | A |
| `GetSignInActPrizer` / `lucker2` 通道 | **5** | A |
| `GetSignInActPrizer` / `libmysql` 通道 | **1** | A |
| 中文隧道（标记串 + 比对点） | **3** | A |
| 物品给予标签表 | **1** | A |
| **小计：新坐实** | **50** | |
| `AllFuc.pas` 无声明、三份底本零命中 | **18** | **仍不可证** |

### 3.2 逐条表（新坐实 50 条）

`标记 VA` = §2.1/2.5/2.6 的字面量；`臂 VA` = 跳转表对应臂；`实现 VA` = 臂内调用的实现函数。

| # | 脚本 API | C# 实现 | 隧道 | 操作码 | 标记 VA | 臂 VA | 实现 VA |
|---:|---|---|---|---:|---|---|---|
| 1 | `ys_addhp` | `AddMaxHp` | 集成函数 | 11 | 0x102BE81C | 0x10076D4E | 0x10071920 |
| 2 | `ys_addmp` | `AddMaxMp` | 集成函数 | 11 | 0x102BE81C | 0x10076D4E | 0x10071920 |
| 3 | `ys_addshuxing` | `AddTempAttr` | 集成函数 | 14 | 0x102BE81C | 0x10076E8B | 0x10071F10 |
| 4 | `ys_addshuxing_pro` | `AddTempAttrPro` | 集成函数 | 14 | 0x102BE81C | 0x10076E8B | 0x10071F10 |
| 5 | `ys_bbflowme` | `PetFollowAttack` | 集成函数 | 35 | 0x102BE81C | 0x10077728 | 0x1006F0E0 |
| 6 | `ys_checkwupinisbind` | `CheckItemBind` | 集成函数 | 21 | 0x102BE81C | 0x1007716A | 0x10073440 |
| 7 | `ys_chgbigbag` | `ChangeBigBag` | 爱心分割 | ^2^ | 0x102BE82C | 0x1005DD42 | 0x10059060 |
| 8 | `ys_cutting` | `HolyDamage` | 集成函数 | **34** | 0x102BE81C | 0x100776BF | **0x1006E8D0** |
| 9 | `ys_decexp` | `DecExp` | 集成函数 | 39 | 0x102BE81C | 0x100778BD | 0x1006F790 |
| 10 | `ys_dingshen` | `RootTarget` | 集成函数 | 9 | 0x102BE81C | 0x10076C7C | 0x10070FD0 |
| 11 | `ys_doeffect` | `PlayEffect` | 集成函数 | 12 | 0x102BE81C | 0x10076DB7 | 0x1006FDE0 |
| 12 | `ys_geta` | `GetSkillDmgReduction` | 集成函数 | 40 | 0x102BE81C | 0x10077926 | 0x1006F8E0 |
| 13 | `ys_getdatabyclientitemid` | `GetItemDataByClientId` | lucker2 `^4^` | — | 0x102C02EC | 0x100879D8 | — |
| 14 | `ys_getitemdbdata` | `GetItemDbData` | 爱心分割 | ^38^ | 0x102BE82C | 0x1005E33C | 0x1005D9F0 |
| 15 | `ys_getmember_playername` | `GetGroupMemberName` | lucker2 `^5^` | — | 0x102C02EC | 0x100879D8 | — |
| 16 | `ys_getmember_roleid` | `GetGroupMemberRoleId` | 集成函数 | 38 | 0x102BE81C | 0x10077854 | 0x1006F630 |
| 17 | `ys_getmembercount` | `GetGroupMemberCount` | 集成函数 | 38 | 0x102BE81C | 0x10077854 | 0x1006F630 |
| 18 | `ys_getother` | `GetOther` | 集成函数 | 32 | 0x102BE81C | 0x100775ED | 0x10075B70 |
| 19 | `ys_getpis` | `GetPis` | 中文 获取元素 | — | 0x102BE880 | 0x1005E7A5 | — |
| 20 | `ys_getshuxing` | `GetCreatureAttr` | 爱心分割 | ^31^ | 0x102BE82C | 0x1005E218 | 0x1005C4E0 |
| 21 | `ys_giveexp` | `GiveExp` | 集成函数 | 29 | 0x102BE81C | 0x100774B2 | 0x10075090 |
| 22 | `ys_givenewitem` | `GiveNewItem` | 给予标签 `$` | — | 0x102BE6D0 表 | 0x10058051 | — |
| 23 | `ys_givepis` | `GivePis` | 中文 给与元素 | — | 0x102BE870 | 0x1005E794 | — |
| 24 | `ys_healing` | `Healing` | 集成函数 | 13 | 0x102BE81C | 0x10076E21 | 0x10071A70 |
| 25 | `ys_herojp` | `GetHeroExtreme` | 中文 英雄极品 | — | 0x102BE8A4 | 0x1005EF8C | — |
| 26 | `ys_jitui` | `PushEnemy` | 集成函数 | 4 | 0x102BE81C | 0x10076A6F | 0x100700A0 |
| 27 | `ys_jitui2` | `PushEnemy2` | 集成函数 | 4 | 0x102BE81C | 0x10076A6F | 0x100700A0 |
| 28 | `ys_myjn_delay` | `CustomDamageDelay` | 集成函数 | **3** | 0x102BE81C | 0x10076A06 | **0x1006DAB0** |
| 29 | `ys_myjn_effect` | `CustomDamageEffect` | 集成函数 | **3** | 0x102BE81C | 0x10076A06 | **0x1006DAB0** |
| 30 | `ys_myjn_plus2` | `CustomDamage2` | 集成函数 | **3** | 0x102BE81C | 0x10076A06 | **0x1006DAB0** |
| 31 | `ys_myjn_super` | `CustomDamageSuper` | 集成函数 | **3** | 0x102BE81C | 0x10076A06 | **0x1006DAB0** |
| 32 | `ys_myjn_undead` | `CustomDamageUndead` | 集成函数 | **3** | 0x102BE81C | 0x10076A06 | **0x1006DAB0** |
| 33 | `ys_mymabi` | `Paralysis` | 集成函数 | **2** | 0x102BE81C | 0x100769B9 | **0x1006D690** |
| 34 | `ys_myskillexp` | `SetSkillExp` | 集成函数 | 10 | 0x102BE81C | 0x10076CE5 | 0x10071710 |
| 35 | `ys_myysjn` | `SuperDamage14` | 集成函数 | 1 | 0x102BE81C | 0x1007670A | 见 §6-C1 |
| 36 | `ys_newxiguai` | `VacuumMonstersEx` | 集成函数 | 27 | 0x102BE81C | 0x100773E0 | 0x10074C60 |
| 37 | `ys_senddbmsg` | `SendDbMsg` | 爱心分割 | ^3^ | 0x102BE82C | 0x1005DD6A | 0x10059160 |
| 38 | `ys_seta` | `SetSkillDmgReduction` | 集成函数 | 40 | 0x102BE81C | 0x10077926 | 0x1006F8E0 |
| 39 | `ys_shidu` | `Poison` | 集成函数 | **5** | 0x102BE81C | 0x10076AD8 | **0x100706A0** |
| 40 | `ys_shidu_effect` | `PoisonEffect` | 集成函数 | **5** | 0x102BE81C | 0x10076AD8 | **0x100706A0** |
| 41 | `ys_sqldbinsert` | `SqlDbInsert` | 爱心分割 | ^1^ | 0x102BE82C | 0x1005DD1A | 0x10058ED0 |
| 42 | `ys_sqldbselect` | `SqlDbSelect` | libmysql | — | 0x102C0324 | 0x10087DD9 | — |
| 43 | `ys_subshuxing` | `SubTempAttr` | 集成函数 | 14 | 0x102BE81C | 0x10076E8B | 0x10071F10 |
| 44 | `ys_tantanskill` | `BounceSkill` | 集成函数 | 26 | 0x102BE81C | 0x10077377 | 0x100740B0 |
| 45 | `ys_tuitui` | `PullEnemy` | 集成函数 | 9 | 0x102BE81C | 0x10076C7C | 0x10070FD0 |
| 46 | `ys_tuitui2` | `PullEnemy2` | 集成函数 | 9 | 0x102BE81C | 0x10076C7C | 0x10070FD0 |
| 47 | `ys_wupingetdata` | `GetItemDataByMakeIndex` | lucker2 `^2^` | — | 0x102C02EC | 0x100879D8 | — |
| 48 | `ys_wupingetdata2take` | `GetItemDataAndRecycle` | lucker2 `^3^` | — | 0x102C02EC | 0x100879D8 | — |
| 49 | `ys_wupinmakeindex` | `GetBagMakeIndexList` | lucker2 `^1^` | — | 0x102C02EC | 0x100879D8 | — |
| 50 | `ys_xixue` | `LifeSteal` | 集成函数 | **8** | 0x102BE81C | 0x10076C13 | **0x10070E70** |

另有 5 条属于 68 之外、但本轮顺带在插件帮助串里拿到**完整 Pascal 签名字面量**的（可用于核对形参序）：

```
0x102BC1C1  "Ys_TanTanSkill(Player:TPlayer;MagicId,x,y,roleid,times,round,double,cutting,effectid,js:integer)"
0x102BBE82  "使用函数function ys_SubShuxing(Player:TPlayer;round,TargetX,TargetY,value,time,pid,roleid,effect:integer):integer;"
0x102C3A03  "通常有function Ys_GetOther(Player:TPlayer;itemid,id,val,types:integer):integer;返回极品或者元素属性"
0x102BC88E  "使用方式ys_SetA(This_Player,'技能免伤',id,value)。"
0x102BC933  "使用方式ys_GetA(This_Player,'技能免伤',id)返回这个值"
```

### 3.3 仍不可证的 18 条

`ysattact` `ysbinditem` `yschangerole` `ysfindplayerbyname` `ysgetbodyitem` `ysgetg`
`ysgetheroshuxing` `ysgetitem` `ysgetitemid` `ysgetonlineplayernum` `ysgetstr` `yskillmon`
`yskillrole` `ysnewtuitui` `yssafezone` `yssay` `yssetg` `yssetstr`

判据（三项同时成立，故如实标"不可证"，**不臆造对应关系**）：

1. **不在 `AllFuc.pas`**（解密本 292 行逐个函数头解析，无这些名字），故没有 `!!!!` 编码可循；
2. 名字串在 **ys208 / ys207 / M2 三份底本全部 0 命中**（含首字母大写、全大写变体）；
3. 它们的唯一出处是官方《AllFuc 使用例子》文档，`PasApiBridge.Yanshen.cs:11-13` 自己也写明这一点。

⇒ 这 18 条**继续记 `UNPROVEN-IMPL`**。要解只能靠一份"插件已注册脚本函数表"的活进程快照，静态转储解不了。

---

## 4. F6 —— 30 条 `!!!!` 命令隧道逐条判定

审计的 30 条 = 数字 ID 17（19 减去工具已覆盖的 16/22）+ `^N^` 6 + 中文 7。

| 段 | 条目 | 结论 | 证据 |
|---|---|---|---|
| 数字 ID `1 2 4 8 9 10 11 12 13 21 26 27 29 35 38 39 40` | **17** | **全部坐实** | 41 路表 `0x10077A78`，每 ID 有臂 VA + 实现 VA（§3.2 同表） |
| `^N^` `1 2 3 29 31 38` | **6** | **全部坐实** | 38 路表 `0x1005E3D8`：`^1^`0x1005DD1A/0x10058ED0、`^2^`0x1005DD42/0x10059060、`^3^`0x1005DD6A/0x10059160、`^29^`0x1005E1C2/0x1005C220、`^31^`0x1005E218/0x1005C4E0、`^38^`0x1005E33C/0x1005D9F0 |
| 中文 `给与元素` `定义伤害` `英雄极品` `hq取sj戳` `zd义回收` | **5** | **坐实** | §2.2 标记串 + 比对点 |
| 中文 `攻击伤害` `hq取sj间` | **2** | **C# 自造别名，无原生对应** | 见下 |

**⇒ F6：28 条坐实，2 条判为自造别名。**

### 4.1 两条自造别名（`YanshenCommands.cs:56,58,362,365`）

```csharp
["定义伤害"]="刀刀切割",["攻击伤害"]="刀刀切割",
["hq取sj戳"]="毫秒级cd记录",["hq取sj间"]="毫秒级cd记录",
…
case "定义伤害":
case "攻击伤害": _api.DirectAttack(P(cmd,0),P(cmd,1)); return 0;
case "hq取sj戳":
case "hq取sj间": return Environment.TickCount;
```

- `攻击伤害`：GBK 串在 ys208 只有 2 处命中（`0x102BC84E` / `0x102BCA36`），**都在 GUI 帮助文案里**
  （"对敌人攻击伤害…"），**不在 `0x102BE81C` 标记表**，也无任何代码引用作前缀比对。
- `hq取sj间`：ys208 / ys207 **0 命中**；只有 `hq取sj戳`（`0x102BE83C`）存在。

两条都是无害的**死臂**（别名指向同一真实处理器，`AllFuc.pas` 永远不会产出这两个串），
但**不能记 FAITHFUL**，本报告归入 `PARTIAL`。

---

## 5. 战斗主干五项 —— 深度结论

### 5.1 五项的原生落点

| 项 | 脚本入口 | 操作码 | 臂 VA | **实现 VA** | 原生门 |
|---|---|---:|---|---|---|
| 施毒 | `ys_shidu` / `ys_shidu_effect` | 5 | `0x10076AD8` | **`0x100706A0`** | `[cfg2+0x11C] > 500` |
| 麻痹 | `Ys_Mymabi` | 2 | `0x100769B9` | **`0x1006D690`** | **无门** |
| 吸血 | `ys_XiXue` | 8 | `0x10076C13` | **`0x10070E70`** | `[cfg2+0x11C] > 500` |
| 切割 | `ys_Cutting` | 34 | `0x100776BF` | **`0x1006E8D0`** | `[cfg2+0x11C] > 500` |
| 自定义伤害五变体 | `ys_myjn_plus2/effect/undead/super/delay` | 3 | `0x10076A06` | **`0x1006DAB0`** | `[cfg2+0x524] > 500` |

### 5.2 五变体的「元数阶梯」——本轮最硬的一条证据

`sub_1006DAB0` 先算 token 数（`std::vector<std::string>`，元素 24 B）：

```
1006DAF5  8B 4D 0C              mov ecx,[ebp+0xC]         ; end
1006DAF8  2B 4D 08              sub ecx,[ebp+8]           ; - begin
1006DAFB  B8 AB AA AA 2A        mov eax,0x2AAAAAAB
1006DB00  F7 E9                 imul ecx
1006DB02  C1 FA 02              sar edx,2                 ; /24  → token 数
1006DB0C  83 F8 0B              cmp eax,0xB               ; 下限 11
1006DB0F  73 27                 jae 0x1006DB38
1006DB20  B8 88 FC FF FF        mov eax,0xFFFFFC88        ; = -888  ← 参数不足的返回值
```

token 0 = `!!!!集成函数`，token 1 = `3`，token 2.. = 实参。
⇒ 下限 11 = 2 + **9** 个必填参 = **正好是 `ys_myjn_plus2` 的 9 参**。

其后是完全规整的「可选参阶梯」，读第 k 号 token 需 `count >= k+1`：

```
1006DC8C  83 F8 0C   cmp eax,0xC   jb→跳过   push 0xA / push 0x0B  → 第10参 effect
1006DCC7  83 F8 0D   cmp eax,0xD   jb→跳过   push 0xA / push 0x0C  → 第11参 undead
1006DD05  83 F8 0E   cmp eax,0xE   jb→跳过   push 0xA / push 0x0D  → 第12参 MgId
1006DD40  83 F8 0F   cmp eax,0xF   jb→跳过   push 0xA / push 0x0E  → 第13参 AttactId
1006DD7D  83 F8 10   cmp eax,0x10  jb→跳过   push 0xA / push 0x0F  → 第14参 double
1006DDB9  83 F8 11   cmp eax,0x11  jb→跳过   push 0xA / push 0x10  → 第15参 delay
```

（每块 = `push 0xA`(基数) + `push idx` + `lea ecx,[ebp+8]` + `call 0x10018460`(vector 取元素) + `call 0x10012940`(StrToInt)。）

⇒ **一个实现体、9 个必填 + 6 个可选，`AllFuc.pas` 的 9/10/11/13/15 五种调用形正好是这条阶梯上的五个取样点。**
这就是"五变体"的原生真相：**不是五个函数，是一个函数的五种实参长度**。C# 用五个方法建模，
形参个数 9/10/11/13/15 与原生阶梯逐位对齐 —— **接口契约判 FAITHFUL**。

`ys_tuitui` / `ys_tuitui2` / `ys_dingshen` 同理：三者都编码为 **操作码 9**，共用臂 `0x10076C7C` 与实现 `0x10070FD0`，
靠实参个数区分（8 / 9 / 1）。

### 5.3 查出的 3 处真实偏差

**偏差 1 —— 麻痹被 C# 多加了一道门。**
原生 `2` 号臂 `0x100769B9` 从 `mov ecx,[ebp+8]` 直接进正文，**臂内没有任何 `cmp …,0x1F4` 门**
（对照 `3`/`5`/`8`/`34` 号臂开头一律是 `A1 …C2 31 10 / 81 38 F4 01 00 00 / 7E 07`）。
C# `YanshenApi.cs:1039` 却有 `if (!Enabled("麻痹概率")) return 0;`。
⇒ 生产 `麻痹概率` 若为 0，C# 静默不施放，原生照放。**这是 C# 侧多出来的 fail-closed 门。**

**偏差 2 —— 开关粒度整体不符。**
原生 41 个操作码里 **33 个共用同一道门 `cfg2+0x11C`**（`0x1031C244`，33 处 xref）；
C# 给每个函数各配一把不同的键：

| 操作码 | C# 门（`YanshenApi.cs`） | 原生门 |
|---:|---|---|
| 5 施毒 | `Enabled("施毒术")` :1056 | `cfg2+0x11C` |
| 8 吸血 | `Enabled("攻击吸血")` :1244 | `cfg2+0x11C` |
| 34 切割 | `Enabled("刀刀切割")` :999 | `cfg2+0x11C` |
| 4 击退 | `Enabled("野蛮麻痹")` :1155 | `cfg2+0x11C` |
| 9 拉近 | `EnabledAll("眼神特殊函数","super攻击触发")` :1186 | `cfg2+0x11C` |
| 9 定身 | `Enabled("野蛮麻痹")` :1233 | `cfg2+0x11C` |
| 3 自定义伤害 | 三种不同门（见下） | `cfg2+0x524`（**五变体共用一把**） |

尤其 `9` 号：原生是**一个臂一道门**，C# 却给 `PullEnemy` 和 `RootTarget` 配了两把不同的键。
同理 `3` 号五变体原生共用 `cfg2+0x524`，C# 却是：`CustomDamage2`→`Enabled("刀刀切割")`（经 `CustomDamage`）、
`CustomDamageEffect`→`EnabledAll("眼神特殊函数","自定义伤害_plus","super攻击触发")`、
`CustomDamageUndead/Super/Delay`→经 `CustomDamage` 的 `刀刀切割`。
⇒ **门的拓扑与原生不同**：原生"一族一门"，C# "一函一门"。生产上只要这些键取值不一致就会出现行为分叉。

**偏差 3 —— `-888` 哨兵未复刻。**
原生 `3` 号在 token 数 < 11 时返回 `0xFFFFFC88 = -888`（`0x1006DB20`）。
C# 靠静态签名保证元数，不存在"参数不足"路径，也就没有 `-888` 这个可观测返回值。
脚本若依赖 `if ys_MyJn_plus2(...) = -888` 判错，C# 侧永远不成立。

### 5.4 必须写清的证据边界

上面坐实的是**隧道 / 操作码 / 元数 / 开关 / 实现函数入口**这一层。
**五项的内部数值语义（伤害公式、毒跳间隔、吸血比例、切割判定）本轮没有反演到位**，
C# 现有公式（如 `CalcDamage = max(0,DC-AC) + baseHp*(magicLv+1)/10 + cuttingV`，`YanshenApi.cs:938-945`）
**仍无原生字节背书**。实现函数体已定位（`0x100706A0` / `0x1006D690` / `0x10070E70` / `0x1006E8D0` / `0x1006DAB0`），
逐条反演是下一轮的活，不在本轮承诺内。

---

## 6. 仍不可证清单（本轮如实记账）

| # | 条目 | 障碍 |
|---|---|---|
| C1 | `集成函数` 一级派发指针 `cfg+4`（`[0x1031BFB8]`） | 两份 2.08 转储该槽均为 `0`（运行期填充）。**但不影响结论**：41 路表已由 `sub_100761A0` 内的 `0x10076703` 直接坐实 |
| C2 | 操作码 `1`（`ys_myysjn`）实现体 | 臂 `0x1007670A` 走 `push 0x102BF97C` + `E8 4E D3 D7 00 → call 0x10DF3A91`，落进 Themida VM 段（`.p7q` 在本转储 zero=1.000） |
| C3 | 18 条"官方例子名" | 见 §3.3，三份底本零命中，静态不可判 |
| C4 | 五项内部数值语义 | 见 §5.4，需逐函数反演 |

**附带发现（不在 98 条内，登记以免遗漏）**：
`AllFuc.pas` 的 `ys_MyJn_plus` 发的是 `'!!!!plus伤害'+…`，
但 `plus伤害` 的 GBK 串在 **ys208 与 ys207 均 0 命中**，`0x102BE81C` 标记表里也没有它。
⇒ 该 Pascal 函数在 2.07/2.08 上**没有原生解析器**，串会原样落到宿主真正的 `GetBagItemCount`（返回背包计数）。
C# `CustomDamage`（`YanshenApi.cs:958`）把它实现成了真实伤害。此条属审计的"已佐证 40"组，
不改本报告分母，但建议下一轮复核。

---

## 7. 收敛后的完成度

按审计原口径（`FAITHFUL = 有原生字节/地址佐证 且 C# 有活消费者`）重算：

| 判定 | 审计原值 | 本轮变动 | 新值 |
|---|---:|---|---:|
| FAITHFUL | 298 | +78（F2 50 + F6 28） | **376** |
| PARTIAL | 34 | +2（两条自造别名） | **36** |
| MISSING | 213 | — | **213** |
| FAIL-CLOSED | 2 | — | **2** |
| UNPROVEN | 113 | −80 | **33**（BLOCKED 15 + IMPL 18） |
| 合计 | 660 | | **660** ✓（`376+36+213+2+33=660`） |

- **严格有据完成度 `376 / 660 = 57.0%`**
- 若剩余 18 条 `UNPROVEN-IMPL` 将来全部证真：`394 / 660 = 59.7%`
- 若全部证伪：`376 / 660 = 57.0%`

⇒ **真值区间由 `45.2% ~ 60.0%`（宽 14.8pt）收敛到 `57.0% ~ 59.7%`（宽 2.7pt）。**

> **口径提醒**：这 57.0% 与审计一样，是"有原生佐证 + 有活消费者"的口径，
> **不等于"数值语义已 1:1 验证"**。若把标准提高到"实现体已逐条反演"，
> 本轮只有自定义伤害的元数阶梯（§5.2）与两张跳转表的分派结构达标，
> 战斗五项的公式层仍是空白（§5.4）。**这两个数字不要混用。**

---

## 8. 复现方式

底本（只读）：

```
ys208 : D:\loym2\staging\yanshen208_strparam_runtime_dump_20260719\yanshen2_0_8_dll.memory.bin   base 0x10000000
ys207 : D:\loym2\staging\questinfo_runtime_dump\yanshen2_0_7_dll.memory.bin                      base 0x10000000
m2    : D:\loym2\staging\_reunpack_work\flat_image.bin                                           base 0x00400000
明文  : D:\loym2\staging\_ys208_plain\Envir__CommonScripts__眼神专用__AllFuc.pas
```

本轮脚本（均在 `%TEMP%`，只读）：
`ysev_tool.py`（hexdump/xref/disasm 工具箱）、`ysev_fn.py`（带串注解的反汇编窗口）、
`ysev_marker.py`（标记串普查）、`ysev_xref.py`（标记 xref）、`ysev_jt.py`（跳转表扫描）、
`ysev_allarms.py`（41+38 臂的门与实现提取）、`ysev_gates.py`（门全局 → 配置字段）、
`ysev_census.py`（复现审计的 68 条 F2 名单）、`ysev_table.py`（主表生成）。

`ysev_census.py` 在本工作树上复跑的结果与审计完全一致，可作为分母未漂移的凭据：

```
dispatch arms                : 108
arms whose impl cites a VA   : 40
arms whose impl has no VA    : 68
arms with unresolved member  : 0
```
