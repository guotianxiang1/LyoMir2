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
| 战斗主干五项 | **五项全部坐实到"隧道 + 操作码 + 元数 + 开关 + 实现函数"级**；同时查出 **4** 处真实偏差 |
| 元数交叉验证 | 17 个操作码对撞 `AllFuc.pas` 声明：**13 精确相等 / 4 原生更宽 / 0 矛盾** |

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
| `0x102BE894` 定义伤害 | `0x1005EDB3` | `68 94 E8 2B 10` | — |
| `0x102BE8A4` 英雄极品 | `0x1005EF8B` | `68 A4 E8 2B 10` | — |
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

```
1005DD13  FF 24 BD D8 E3 05 10   jmp dword [edi*4 + 0x1005E3D8]
```

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
| 25 | `ys_herojp` | `GetHeroExtreme` | 中文 英雄极品 | — | 0x102BE8A4 | 0x1005EF8B | — |
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

### 5.3 查出的真实偏差（3 处结构性 + 1 处见 §5.5）

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

### 5.4 元数交叉验证 —— 17 个操作码，0 处矛盾

把 `AllFuc.pas` 声明的形参个数，与实现体开头"token 数下限检查"解出的必填参个数逐个对撞
（token 数 = `(end-begin)/24`，前两个 token 恒为 `!!!!集成函数` 与操作码，故 必填参 = 下限 − 2）：

| 操作码 | `AllFuc.pas` 声明 | 实现 VA | 原生下限 | ⇒必填参 | 声明参 | 判定 |
|---:|---|---|---:|---:|---:|---|
| 3 | `ys_myjn_plus2/effect/undead/super/delay` | 0x1006DAB0 | 11 | 9 | 9 | **相等** |
| 4 | `ys_JiTui / ys_JiTui2` | 0x100700A0 | 10 | 8 | 8 | **相等** |
| 5 | `ys_ShiDu / ys_ShiDu_effect` | 0x100706A0 | 11 | 9 | 9 | **相等** |
| 8 | `ys_XiXue` | 0x10070E70 | 4 | 2 | 2 | **相等** |
| 9 | `ys_TuiTui / ys_TuiTui2` | 0x10070FD0 | 10 | 8 | 8 | **相等** |
| 12 | `ys_DoEffect` | 0x1006FDE0 | 7 | 5 | 5 | **相等** |
| 21 | `ys_CheckWupinIsBind` | 0x10073440 | 3 | 1 | 1 | **相等** |
| 26 | `Ys_TanTanSkill` | 0x100740B0 | 12 | 10 | 10 | **相等** |
| 27 | `Ys_NewXiGuai` | 0x10074C60 | 5 | 3 | 3 | **相等** |
| 32 | `Ys_GetOther` | 0x10075B70 | 6 | 4 | 4 | **相等** |
| 34 | `ys_Cutting` | 0x1006E8D0 | 12 | 10 | 10 | **相等** |
| 37 | `ys_Magic_huoqiang` | 0x1006F2C0 | 9 | 7 | 7 | **相等** |
| 39 | `ys_DecExp` | 0x1006F790 | 5 | 3 | 3 | **相等** |
| 13 | `ys_Healing` | 0x10071A70 | 9 | 7 | 8 | 原生更宽（第 8 参可选） |
| 35 | `ys_BBflowme` | 0x1006F0E0 | 2 | 0 | 1 | 原生更宽 |
| 36 | `ys_getFzhong` | 0x1006F1C0 | 2 | 0 | 1 | 原生更宽 |
| 41 | `ys_PlayerOut` | 0x1006FD00 | 2 | 0 | 1 | 原生更宽 |

**13 个精确相等，4 个"原生下限低于声明"（尾参可选），0 个"原生要求多于声明"。**
可选参阶梯也逐个对上：`4`/`5`/`9` 号各多一个可选参（正是 `ys_JiTui2` 的 `roleid`、
`ys_ShiDu_effect` 的 `effect`、`ys_TuiTui2` 的 `roleid`），`3` 号多六个（§5.2）。

⇒ 这 17 组独立数据全部自洽，**`AllFuc.pas` ↔ 操作码 ↔ 实现函数的对应关系不是猜的**。

参数不足时的返回值也拿到了（两种）：

```
1007101F …  83 F8 0A     cmp eax,0xA        ; 操作码 9
10071023    73 26        jae 0x1007104B
10071034    B8 88 FC FF FF  mov eax,0xFFFFFC88   ; -888
------------------------------------------------------------
10071AAE    83 F8 09     cmp eax,9          ; 操作码 13
10071AB1    73 1E        jae 0x10071AD1
10071AB3    C7 45 A8 FF FF FF FF  mov [ebp-0x58],-1   ; -1
```

### 5.5 由元数检查直接推出的一条死调用

`AllFuc.pas` 的 `ys_DingShen(Player;shijian)` 发的是 `'!!!!集成函数,9,'+shijian+'$'`，只有 **3** 个 token；
而 `9` 号实现 `sub_10070FD0` 在 `0x10071020` 要求 `>= 0xA` 个 token，否则 `0x10071034` 直接返回 **-888**。

⇒ **`ys_DingShen` 在 2.08 原生上永远走不到正文，恒返回 -888。**
C# `YanshenApi.RootTarget`（`YanshenApi.cs:1231-1235`）却真的去写
`_player.m_wStatusTimeArr[Grobal2.STATE_LOCKRUN]`。这是一条**C# 比原生多做事**的偏差
（与 §5.3 偏差 1 的方向相同）。

### 5.6 必须写清的证据边界

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
| C4 | 五项内部数值语义 | 见 §5.6，需逐函数反演 |

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
> 本轮只有 17 个操作码的元数契约（§5.2 / §5.4）与两张跳转表的分派结构达标，
> 战斗五项的公式层仍是空白（§5.6）。**这两个数字不要混用。**

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

---

## 附录 A（后续轮次）—— 中文隧道键名证伪与自造别名清理

本节结清 §4.1 与 §6「附带发现」两处挂账，并修正 §4 里被记成「坐实」的两条键名。
底本：`yanshen208_strparam_runtime_dump_delayed_20260719`（绝对操作数已重定位到
`0x57C40000`，减 `0x47C40000` 还原成 `0x10000000` 基；Themida 搬走的函数体只有这份完整）
与 `questinfo_runtime_dump\yanshen2_0_7_dll.memory.bin`。

### A.1 序列化器解法的可证伪性

全镜像按三种编码扫 `cmp dword ptr [esi+disp],0x1F4`
（`81 3E` / `81 7E dd` / `81 BE dddddddd` + `F4 01 00 00`），2.0.8 与 2.0.7 各 **75 处**，
分两段 run（2.0.8：`0x100057FE..0x100065D4`、`0x10009EB3..0x1000A5E3`）。
每处后面第一个 `push <字面量VA>` 就是该字段的键名。

「严格 CMP→KEY 交替」不再是假设，可直接证：把 75 条按 CMP 地址排序后，
**相邻两条的键串在 `.rdata` 里首尾相接、4 字节对齐，缺口数 0**
（`0x102B005C → … → 0x102B0338`，`0x102B1524 → … → 0x102B1694`），
两条 run 之间才有一次跳跃。中间塞不进任何被跳过的键。
线性反汇编与字节模式扫描两种取法给出同一张 75 条表；两份 2.0.8 转储解出的结果一致。

### A.2 中文隧道整表（5 道门 / 6 个命令）

入口选择器 `sub_1005E4D0` 的门是一条链，每道门的 `jle` 正好跳到下一道门：

| 命令 | 门读点（2.0.8） | `jle` 落点 | cfg 偏移 | 序列化器 cmp/push | 键名 | 2.0.7 偏移 |
|---|---|---|---|---|---|---|
| `!!!!hq取sj戳` | `0x1005E650` | `0x1005E6C5` | `+0x538` | `0x1000A313` / `0x1000A345` | `毫秒级cd记录` | `+0x518` |
| `!!!!zd义回收` | `0x1005E6C5` | `0x1005E737` | `+0x954` | `0x1000A453` / `0x1000A485` | `高级回收` | `+0x930` |
| `!!!!给与元素` | `0x1005E752` | `0x1005EDA3` | `+0x664` | `0x100057FE` / `0x10005822` | `自定义元素` | `+0x644` |
| `!!!!获取元素` | 同上（共用） | 同上 | `+0x664` | 同上 | `自定义元素` | `+0x644` |
| `!!!!定义伤害` | `0x1005EDA3` | `0x1005EF7B` | `+0x510` | `0x10009EB3` / `0x10009EE5` | **`自定义伤害`** | `+0x4F0` |
| `!!!!英雄极品` | `0x1005EF7B` | `0x1005F1D6` | `+0x514` | `0x1000A043` / `0x1000A075` | **`英雄读取极品`** | `+0x4F4` |

五处门前一律 `A1 FC BE 31 10` = `mov eax,[0x1031BEFC]`。
`+0x664` 那道 `jle` 直接跳到 `+0x510` 的门，越过 `给与元素` 与 `获取元素` 两个处理体
—— 一门管两命令由此坐实。

**§4 原表把 `定义伤害` / `英雄极品` 记为「坐实」，但只坐实了标记串与比对点，
没坐实键名。C# 当时挂的 `刀刀切割` / `自定义元素` 两条都是错的。**

三重印证：
1. 序列化器解出 `自定义伤害` / `英雄读取极品`；
2. 原版 `_ys208_runtime\config.json` 里这两个键实际存在（`自定义伤害=1`、
   `英雄读取极品=0`），而 `刀刀切割` 是同文件里另一把开关（`=0`）——
   C# 旧映射会让 `!!!!定义伤害` 在原版默认配置下被错误关闭；
3. 2.0.7 独立复算：配置结构体挪过位，五个偏移全不同，同一解法解出的键名逐条相同。

### A.3 五个自造别名的判定

两份运行期转储全镜像五编码（ascii / GBK / UTF-16LE / UTF-8 / Big5）扫描：

| C# 命令名 | 2.0.8 | 2.0.7 | 判定 |
|---|---|---|---|
| `plus伤害` | 0 | 0 | 自造（Plus/PLUS/`lus伤害` 变体同 0） |
| `攻击伤害` | GBK 2 处，`0x102BC84E`/`0x102BCA36` | GBK 2 处，`0x102A8CDE`/`0x102A8EC6` | 自造（命中全在 GUI 帮助文案正文） |
| `hq取sj间` | 0 | 0 | 自造 |
| `zd回收` | 0 | 0 | 自造（ZD/Zd 变体同 0） |
| `给予元素` | 0 | 0 | 自造（原生是「与」不是「予」） |

穷举佐证：全镜像里 NUL 结尾、以 `!!!!` 打头的 GBK 串**两版各只有 8 条**，
每条只被入口选择器里的一处 `push` 引用：

```
2.0.8  102BE81C 集成函数 push@1005E578   102BE82C 爱心分割 push@1005E58C
       102BE83C hq取sj戳 push@1005E65C   102BE84C zd义回收 push@1005E6D1
       102BE870 给与元素 push@1005E794   102BE880 获取元素 push@1005E7A5
       102BE894 定义伤害 push@1005EDB3   102BE8A4 英雄极品 push@1005EF8B
2.0.7  102AA8E8/8F8/908/918/928/938/94C/95C  push@10051F68/1F7C/204C/20C1/2173/2184/2792/296A
```

标记表里 `0x102BE85C` 那 20 字节空档确实被 `0x1005E762 push 0x102BE85C` 引用，
但紧接的 `jmp 0x108D484A` 落点第一条就是 `lea esp,[esp+4]`，把刚压的值丢掉 ——
Themida 垃圾对，不是字面量引用。该空档在两份 2.0.8 转储里逐字节相同，
不是延迟解密的串。

磁盘上的 `yanshen2.0.7.dll` / `2.0.8.dll` 被 Themida 压着，连 `定义伤害` 这种
已坐实的串都 0 命中，**磁盘扫描对本题无判定力**，只以运行期转储为准。

### A.4 落地

- `YanshenCommands._chineseToggles`：`定义伤害` → `自定义伤害`、
  `英雄极品` → `英雄读取极品`；删掉 5 个自造别名的门。
- `YanshenCommands.ExecuteChinese`：删掉 5 条自造别名的 `case`。
- `PluginManager.ParseTunnelCommand` 的 `knownNames`：`plus伤害` 摘掉，剩 6 个。
- `Yanshen207ProtocolCheck`：`plus伤害` 的正向断言换成五个自造名的回归守卫。

五个名字打过来现在走 `EnsureCommandEnabled` 的「命令未登记」分支抛出（fail-closed）。
原生那边是前缀比不中、原样落到宿主真正的 `GetBagItemCount` 返回背包计数 ——
这层宿主回落路径本轮没有复刻，登记为待办。**该挂账已由附录 B 结清。**

### A.5 顺带核对：五个「键名未证」偏移仍然无条目

75 条表里出现过的偏移不含 `+0x084`、`+0x0FC`、`+0x940`、`+0x1B4`、`+0x6E0`、`+0x60`，
`YanshenCommands._toggles` 里那五条「键名未证」的登记与 `爱心分割` §那条
`cfg+0x60` 的说明**依旧成立**，本轮不动。

---

## 附录 B —— 未登记 `!!!!` 前缀的宿主回落路径

结清 A.4 挂的那笔账。底本同 A：`yanshen208_strparam_runtime_dump_delayed_20260719`
（`yanshen2_0_8_dll.memory.bin` 基 `0x57C40000`，减 `0x47C40000` 还原成 `0x10000000` 基；
文件偏移直接等于 RVA），宿主用 `staging\_reunpack_work\flat_image.bin`（基 `0x400000`），
另有 2.0.7 运行期转储 `questinfo_runtime_dump` 交叉验证。

### B.1 选择器的收尾：`-1656`

`sub_1005E4D0` 的八条比对全不中（或对应的 `cmp [cfg+disp],0x1F4` 门关着而被跳过）时，
每条失配支路都先析构自己那个临时 `std::string`，然后**无一例外**落到同一个收尾：

```
1005EF6A / 1005F1CA …  析构 → 落到 1005F1D6
1005F1D6  C6 45 FC 02              (析构 3 个 std::string)
1005F200  EB 0D                    jmp 0x1005F20F
1005F20F  B8 88 F9 FF FF           mov eax,0xFFFFF988      ; = -1656
1005F214  …                        SEH 收尾 → ret
```

五道门的 `jle` 落点连成一条链，末端就是这里：
`0x1005E65A → 0x1005E6C5 → 0x1005E6CF → 0x1005E737 → 0x1005E75C → 0x1005EDA3
→ 0x1005EDAD → 0x1005EF7B → 0x1005EF85 → 0x1005F1D6`。

### B.2 `-1656` 是谁在看：`GetBagItemCount` 的 5 字节 detour

`sub_1005E4D0` 全镜像**只有一处**调用点 —— `0x1005F2FF`，在 `ret 0xC` 的三参包装
`sub_1005F2D0` 里（`arg3 → edx` = player，`arg1/arg2` 拼成命令串）。
`sub_1005F2D0` 又**只有一处**调用点，在 Themida 搬走的钩子体里：

```
58A05256  E8 75 A0 29 FF     call 0x57C9F2D0        ; = sub_1005F2D0
58A0525B  89 44 24 1C        mov [esp+0x1C],eax
58A05264  3D 88 F9 FF FF     cmp eax,0xFFFFF988     ; ← 全镜像唯一一处
58A05269  E9 87 58 1B 00     jmp 0x58BBAAF5
58BBAAF5  0F 84 B7 FC 1F 00  je  0x58DBA7B2         ; -1656 → 跑原函数
58BBAAFB/AAFD                 …  → 0x57C82F95       ; 否则拿隧道的返回值
```

`0x58DBA7B2` 是这样一段：

```
58DBA7B2  55 8B EC 33 C9     push ebp / mov ebp,esp / xor ecx,ecx
58DBA7B7  FF 35 A4 B9 F5 57  push dword [0x1031B9A4]
58DBA7BD  C3                 ret                     ; 尾跳到保存下来的续址
```

前 5 字节 `55 8B EC 33 C9` 就是宿主 `TPlayObject.GetBagItemCount` `0x007447C0`
被 `E9 <rel32>` 覆盖掉的那 5 字节，续址即 `0x007447C5`。三条独立佐证：

1. **宿主注册表**：`0x0073140E` `mov ecx,0x73249C`（`"GetBagItemCount"`）/
   `mov edx,0x7447C0` / `call 0x4F4180`（RegisterMethod）。
   `0x007447C0` 的头 5 字节正是 `55 8B EC 33 C9`。
2. **插件的宿主挂钩目标表** `0x102B2100..0x102B2700` 共 170 条宿主地址，
   `0x102B22F4`/`0x102B22F8` 两格就是 `0x007447C0`；这 170 条里**只有它**
   开头是 `55 8B EC 33 C9`（其余 `55 8B EC` 开头的九条后面分别是
   `83 C4 EC` / `53 8B D8` / `53 56` / `6A 00` / `51 53` / `51 53 56 57` ×2 /
   `81 C4` / `83 C4 F8`），重放字节唯一对得上。
3. **2.0.7 运行期直接观测**：`questinfo_runtime_dump\m2_yanshen_hooks.txt`
   里有 `HOOK source=0x007447C0 target=0x7A7B2D06`。

同一个 `0x58DBA7B2` 还有第二个入口 —— 连 `!!!!` 都不打头的普通物品名：

```
58E64C9E  81 3A 21 21 21 21  cmp dword [edx],'!!!!'   ; 全镜像唯一一处 0x21212121 立即数
58E64CA4  E9 16 3A F5 FF     jmp 0x58DB86BF
58DB86BF  0F 85 ED 20 00 00  jne 0x58DBA7B2           ; 不是 !!!! 打头 → 跑原函数
```

⇒ 钩子的形状是「不是 `!!!!` 打头 → 原函数；是 → 进选择器；选择器给 -1656 → 原函数」。
2.0.7 同一处 `cmp` 在 `0x7AE3DEEC`，也只有一处。

**两份转储都是钩子尚未装上的状态**（`0x007447C0` 与 `flat_image.bin` 逐字节相同，
`[0x1031B9A4]` 读出来是 0，与 §6-C1 记的 `cfg+4` 槽为 0 一致），
所以 detour 只能静态判读；三条佐证互不依赖。

### B.3 回落之后宿主返回什么：0

`sub_7447C0` = `xor ecx,ecx` + 调 `sub_7447CC`（`ecx` 是第三个过滤参数，0 = 不过滤）：

```
007447DB  33 C0                    xor eax,eax
007447DD  89 45 F4                 mov [ebp-0xC],eax        ; 计数槽 = 0
007447E0  A1 6C 5D 7D 00           mov eax,[0x7D5D6C]       ; std 物品表
007447E7  E8 F4 79 00 00           call 0x74C1E0            ; GetStdItemIdx(name)
007447EF  83 7D F0 00              cmp [ebp-0x10],0
007447F3  7E 73                    jle 0x744868             ; ← 索引 <= 0 直接跳出口
…
00744868  8B 45 F4                 mov eax,[ebp-0xC]        ; 返回计数槽
```

`sub_74C1E0` 起手 `or esi,-1`，只有在 `sub_49F5F4` 查到条目时才用 `movzx esi,word [eax]`
覆盖，查不到就返回 **-1**；`-1 <= 0` 命中 `jle`，出口读的是从未被加过的计数槽。

⇒ **未登记 `!!!!` 前缀的原生真值 = 按物品名查背包，查不到 → 返回 `0`**，
不报错、不产生副作用、不写日志。

### B.4 两个比对函数的语义（决定「哪些串算命中」）

八条比对分两种，都尾调 `_Traits_compare` `0x10018E20`：

| 比对点 | 命令 | 函数 | 语义 |
|---|---|---|---|
| `0x1005E5A0` | `!!!!集成函数` | `sub_10064BD0` `ret 0xC` | 前缀 |
| `0x1005E613` | `!!!!爱心分割` | `sub_10064BD0` | 前缀 |
| `0x1005E67A` | `!!!!hq取sj戳` | `sub_10043E20` `ret 4` | **全等** |
| `0x1005E6EF` | `!!!!zd义回收` | `sub_10043E20` | **全等** |
| `0x1005E7C1` | `!!!!给与元素` | `sub_10064BD0` | 前缀 |
| `0x1005EACB` | `!!!!获取元素` | `sub_10064BD0` | 前缀 |
| `0x1005EDCF` | `!!!!定义伤害` | `sub_10064BD0` | 前缀 |
| `0x1005EFA7` | `!!!!英雄极品` | `sub_10064BD0` | 前缀 |

`sub_10043E20` = MSVC `compare(const basic_string&)`：两侧长度都进 `_Traits_compare`，
长度不等即非 0 ⇒ 全等。AllFuc.pas 那两条也确实是不带参数的整串
（`GetBagItemCount('!!!!hq取sj戳')` / `('!!!!zd义回收')`）。

`sub_10064BD0` = `compare(_Off, _Nx, _Right)` 的编译期 `_Off==0` 特化：
第一个实参在函数体里根本没用，`0x10064BE5 cmp [ecx+0x10],edx` + `cmovb` 把
比较长度夹成 `min(自身长, prefixLen)` ⇒ 前缀比对。

还有一条容易漏的：`0x1005E737` 在 `zd义回收` 之后、`给与元素` 之前把命令串
**砍掉最后 1 字节**（`mov ecx,[ebp-0x1C]` / `dec ecx` / `mov byte [eax+ecx],0`），
所以后四条前缀比对看到的是短一个字节的串 —— 串正好等于 12 字节前缀（无参数）时
`min` 夹出 11 < 12，`_Traits_compare` 拿长度差判负，**反而比不中**。
AllFuc.pas 这四条一律带参数，正常调用不受影响。

### B.5 另外两个寄生入口

- **`Give`**（注册于 `0x0073142B`，实现 `0x006DF2E8`）：`0x006DF2E8` **不在**插件那
  170 条挂钩目标表里，且 `AllFuc.pas` 的写法是 `ItemName+'!!!!'+…`，`!!!!` 永远在**串中**
  而不是串首。全镜像 `!!!!` 字面量只有那 8 条命令名（每条只被选择器里的一处 `push`
  引用），`0x21212121` 立即数只有 `0x58E64C9E` 一处且属于 `GetBagItemCount` 钩子。
  ⇒ **`Give` 上不存在「`!!!!` 前缀未命中」这条路径**，本轮无可复刻。
  它的物品元素解析走的是另一套后缀标签（`0x102BE6D0` 的
  `data`/`,`/`jp2ys`/`zdyly`/`……`，xref `0x10058051`/`0x1005818D`/`0x10058422`/`0x1005845E`，
  由 `cfg+0x664`「自定义元素」把门），不经过 `sub_1005E4D0`。
- **`GetSignInActPrizer`**（注册于 `0x00731B6E`，实现 `0x006E2E34` → 尾调
  `TUserEngine` 的 `0x00616D88`）：两处都被挂钩（表里有 `0x006E2E3B` 与 `0x00616DAA`，
  2.0.7 观测到同样两条 `HOOK source=0x006E2E3B` / `0x00616DAA`）。
  它的选择器不是 `!!!!` 前缀，而是**第二实参哨兵**：`sub_10087990` 里
  `0x100879D8 mov edx,0x102C02EC`（`"lucker2"`）起手的内联 `strcmp`，
  不等就 `0x10087A07 jne 0x10087C30` → `0x10087C37 xor eax,eax` **返回 0**；
  `libmysql` 那条 `0x10087DD8` 的选择器助手用 `0x10087E00 cmovne esi,[ebp+8]`
  在不等时改用默认值。**两条都不用 `-1656`** —— 全镜像 `cmp eax,0xFFFFF988` 只有一处。
  ⇒ `-1656` 回落协议是 `GetBagItemCount` 钩子独有的，不能往这两个入口上套。

顺带：`GetBagItemCountEx`（注册于 `0x00731419`，实现 `0x00744874`）**不在**挂钩表里，
`0x00744874` 在插件镜像里 0 命中 ⇒ 原生根本不从 `Ex` 走隧道。
C# 目前把 `getbagitemcountex` 也接到隧道上，属既有分歧，
按「只处理前缀未命中」的边界本轮不动，登记为待办。

### B.6 落地

- 新增 `PluginManager.IsNativeSelectorHit(string)`：复刻 B.4 那张表（含全等/前缀
  两种语义与 `0x1005E737` 的截尾），并把原先内联在 `ParseTunnelCommand` 里的
  6 条中文名提升成 `NativeChineseCommandNames`，两处共用一份名单。
- `PasApiBridge.TryExecuteTunnelCommand`：`IsTunnelCommand` 之后加一道
  `IsNativeSelectorHit`，比不中就 `return false` —— 调用点随即落到
  `CountBagItem(itemName)`，那正是 B.3 的原函数体（此仓早已按
  `sub_7447CC` 逐条复刻，`GetStdItemIdx <= 0 → 0` 也在内）。
  这一道同时覆盖原生的两个回落出口（`cmp dword [edx],'!!!!'` 与 `cmp eax,-1656`），
  对任何输入的结果与原生一致。
- `YanshenCommands` 里「五个自造名会走『命令未登记』抛出」的注释改成实况。
- 回归网：`Yanshen207ProtocolCheck` 加 `CheckSelectorFallback`（8 命中 / 11 不命中，
  含两条全等命令加一个字符、四条截尾边界、裸数字与 `#ys` 串）；
  `YanshenApiAccessCheck` 加 `FabricatedTunnel`
  （`GetBagItemCount('!!!!plus伤害1:2:…')`），在开关缺失 / 关闭 / 打开三种状态下
  都断言返回 0 —— 前缀链在所有门之前，三态必须同值。

**边界（本轮刻意不动）**：门关着时原生同样落 `-1656` 回宿主，而本仓对
「已登记命令、开关缺失或关闭」一贯 `EnsureCommandEnabled` fail-closed 抛出。
这条差异与前缀未命中是两回事，按任务边界保留原状，登记为待办。
