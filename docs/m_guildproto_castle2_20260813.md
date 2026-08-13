# 城堡域第二轮：两条红的定性、旗帜/日切/CastleLog 取证、InCastleWarArea 解封

日期：2026-08-13
工作树：`D:\loym2\.claude\wt2\m-guildproto`，分支 `w/m-guildproto`（`git merge --no-edit master` 后基线 `2f8931f1`）
镜像：`D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`
中间产物：`D:/loym2/staging/_gp2/*.py` / `*.txt`
**未执行任何编译命令。**

提交（2 个）：

| SHA | 标题 |
|---|---|
| `cdfc69cb` | GILD-21: seed the union-enable cell TRUE, and unbreak the audit that asserted it |
| `31844376` | CASTLE: the war area is a hard-coded absolute rectangle, not a radius around Home |

---

## 0. 判定计数

| 判定 | 数量 | 条目 |
|---|---|---|
| `FAITHFUL` | 3 | GetCastle 存盘序、WineCount、沙巴克.txt 七键落盘 |
| `DIVERGENT` | 1 | `InCastleWarArea`（已修） |
| `MISSING` | 6 | 旗帜系统、`castledetail` 日切 SQL、`guild.CastleLog`×2、`OfficeMin/Max` 复活盒、`FlyStoneCount` |
| `INVENTED` | 3 | `m_EnvirList` 六张地图（已删）、`m_nWarRangeX/Y`（已删）、`NativeGildUnionFlagCell` 的 false 默认（已修） |
| 工具缺陷 | 2 | `CastleOwnerTransitionCheck` 假红、`NativeGildConcernStateCompatCheck` 自相矛盾 |
| `BLOCKED` | 2 | 见 §7（原 `InCastleWarArea` 一条已解封） |

---

## 1. 任务一A：`CastleOwnerTransitionCheck` 的 `Missing: SaveAttackSabukWall();`

### 结论：**假红。C# 没有漏存盘。这是审计工具的仓库根解析踩到了一棵陈旧工作树。**

#### 1.1 先把原生钉死：`0x65A3B8` 是 Save，不是 Load

历史上有两份互相矛盾的材料——`staging/castle_subsystem_final.md` 第五节写「`0x65BF80` 是 Load 非 Save — 已闭」，
主工作树 `D:\loym2\LyoMir2-master\GameSvr\Castle\UserCastle.cs:594` 的注释也写
「`0x65BF80 'call 0x65A3B8' is LoadAttackSabukWall, not Save`」。**两条都是错的。** 逐字节：

```
0065A3F9  8B 83 8C 00 00 00  mov eax,[ebx+0x8C]      ; AttackWarList
0065A3FF  8B 70 08           mov esi,[eax+8]         ; Count
0065A41A  FF 75 FC           push [ebp-4]            ; 累加器          (拼接第 1 段)
0065A420  FF 70 08           push [rec+8]            ; 行会名          (第 2 段)
0065A423  68 C8 A4 65 00     push 0x65A4C8           ; '       "'      (第 3 段, len=8)
0065A433  B8 DC A4 65 00     mov eax,0x65A4DC        ; 'YYYY-MM-DD'    (len=10)
0065A438  E8 FF 66 DB FF     call 0x410B3C           ; FormatDateTime
0065A43D  FF 75 F0           push [ebp-0x10]         ; 日期串          (第 4 段)
0065A440  68 F0 A4 65 00     push 0x65A4F0           ; '"\r\n'         (第 5 段, len=3)
0065A44D  E8 3E B4 DA FF     call 0x405890           ; _LStrCatN(dest,5)
0065A461  B9 FC A4 65 00     mov ecx,0x65A4FC        ; 'AttackSabukWall.txt' (len=19)
0065A46E  E8 5D B3 DA FF     call 0x4057D0           ; Length()
0065A48A  E8 7D 30 14 00     call 0x79D50C           ; 写文件
```

写文件端有 `Length()` 和一个 19 字符的文件名常量；**没有 `FileExists`、没有 `TStringList`、没有 `LoadFromFile`**。

真正的 Load 是 `0x65B22C`，形状完全相反：

```
0065B253  B9 C0 B2 65 00     mov ecx,0x65B2C0        ; 文件名
0065B260  E8 C7 1C DB FF     call 0x40CF2C           ; FileExists
0065B265  84 C0 / 74 2C      test al,al / je 退出
0065B26B  A1 3C EB 49 00     mov eax,[0x49EB3C]      ; TStringList VMT
0065B270  E8 EB 93 DA FF     call 0x404660           ; Create
0065B280  FF 51 68           call [ecx+0x68]         ; LoadFromFile
0065B288  E8 7B 16 00 00     call 0x65C908           ; 解析
0065B290  E8 8F 99 DB FF     call 0x414C24           ; FreeAndNil
```

E8 调用者也对上了：`0x65A3B8` 有四个（`0x65B6B1` 报名 / `0x65B789` 点名 / **`0x65BF80` 易主** / `0x65C1AC` 停战），
`0x65B22C` **只有一个**（`0x65AAD6`，init）。审计工具断言里写的「loader 0x65B22C has a single E8 xref from init 0x65AAD6」逐字属实。

易主现场的三明治结构也和 C# 逐行对应：

```
0065BF77  8B C7 / E8 82 AB 0A 00   mov eax,edi(oldGuild) / call 0x706B00   ; 旧行会 RefMemberName
0065BF80  E8 33 E4 FF FF           call 0x65A3B8                           ; SAVE
0065BF85  8B 43 48 / E8 73 AB 0A 00 mov eax,[ebx+0x48] / call 0x706B00     ; 新行会 RefMemberName
```

对应 `GameSvr/Castle/UserCastle.cs:689 / 695 / 697`。**判定 `FAITHFUL`。**

#### 1.2 那红是怎么来的：`AuditRepoRoot` 的兜底路径

`AuditTools/Shared/AuditRepoRoot.cs:31` `HardcodedFallback = @"D:\loym2\LyoMir2-master"`。
那棵主工作树现在停在 **`fix/mine-21-tier2-halfspeed` @ `5b01d4fe`**（一条与本次无关的老分支），
它的 `UserCastle.cs:601` 里 `GetCastle` 调的还是 `LoadAttackSabukWall();`。
工具二进制一旦解析到那里，`At(getCastle, "SaveAttackSabukWall();")` 必然抛 `Missing:`。

同一个坑的另一面也现场抓到了：`staging/_afbase/runs/CastleOwnerTransitionCheck.txt`（20:33 那批）里
报的是**反过来的** `Missing: LoadAttackSabukWall();`，且抬头写着 `AUDIT_REPO_ROOT=D:\loym2\.claude\wt2\m-auditfail1`。
那棵树 `7db67d02` 分叉自 `b349d15e`，**早于**我上一轮的 `00bd9ee9`/`225b841b`，
所以它的 `Program.cs:20` 还在断言旧契约。也就是说这条红在两个方向上各假了一次。

`AuditRepoRoot` 的注释说「名叫 LyoMir2-master 的兄弟目录在向上遍历时永不探测」——
遍历阶段确实排除了，但**第 4 步的硬编码兜底没排除**，`a25ff854` 那次修补留了这个口子。

#### 1.3 我用脚本原样复算了工具的全部断言

`staging/_gp2/sim_castle_audit.py` 逐字复刻 `Slice`/`At`/`Assert` 的字符串逻辑，跑在两棵树上：

```
ROOT master(m-guildproto)
  SaveAttackSabukWall();  idx=1878     LoadAttackSabukWall();  idx=-1
  ORDER1 cs<ob<rs : True   ORDER2 rs<rg<rn<ro : True   ORDER3 ro<sa<rf : True
  NEG !contains LoadAttackSabukWall(); : True
  NEG !contains m_MasterGuild = oldGuild; : True
  NEG !contains if (!SaveConfigFile())  : True
  date fmt present: True
```

**master 上全部断言通过。** 不需要改 C#。

#### 建议

给主代理：这条红不要按代码缺陷排期。要根治的是工具，两处二选一——
① 删掉 `AuditRepoRoot.HardcodedFallback`，解析不到就抛（fail-loud 好过读错树）；
② sweep 脚本统一显式传 `argv[0]=<repo root>` 或设 `M2_REPO_ROOT`。
在此之前，**任何来自 `_afbase` 的红都要先核对输出抬头里的 `AUDIT_REPO_ROOT`**，否则会拿别人分支的代码当缺陷修。

---

## 2. 任务一B：`NativeGildConcernStateCompatCheck` 的 union flag 默认值

### 2.1 原生：构造器种 TRUE

`sub_7062D0` 是行会（Gild）构造器（`test dl,dl / je` + `call 0x404A08` 的 Delphi 形状，`ret 0xC`）：

```
007062E6  8B F8              mov edi, eax            ; EDI = 实例
0070633A  C6 47 28 01        mov byte ptr [edi+0x28], 1
```

`gild+0x28` 全镜像只有这一处 imm 写入（`C6 /r disp8=0x28 imm8` 全扫），另有一处寄存器写
`0x704EE3 88 5E 28`，就是 4581 处理器。

### 2.2 极性由消费端钉死：0 = 拒绝联盟

`sub_704494`（4573 申请联盟）：

```
007044C1  E8 6A 7B EE FF     call 0x5EC030            ; 解析玩家 -> 空则 5
007044D4  8B 58 04           mov ebx,[eax+4]          ; 自己的行会 -> 空则 12
007044F2  E8 DD 31 EE FF     call 0x5E76D4            ; 按名找目标行会 -> 空则 25 (0x19)
00704507  3B F3 / 75 0A      cmp esi,ebx / jne        ; 目标==自己 -> 19 (0x13)
00704515  80 7E 28 00        cmp byte ptr [esi+0x28], 0
00704519  75 0A              jne 0x704525             ; 非零 -> 放行
0070451B  BF 22 00 00 00     mov edi, 0x22            ; 零 -> 拒绝，码 34
```

所以 **flag=0 的含义是「本行会不接受联盟申请」**。C# 默认 false ⇒ 每个刚载入的行会都静默变成不可申请联盟，
直到有人手动点一次 4581。因为这个字节没有 `gamedata.Gild` 列（DDL `0x5E79EC` 六列里没有它），
每次重启都会复现。

### 2.3 写入语义：改变才落盘

`sub_704EAC`（4581），两个策略槽 `+0x58` 都指向它（dword 引用 `0x7018B8` / `0x701990`，即会长与副会长都能改）：

```
00704EC0  call 0x5EC030 / test eax,eax / jne          ; 无玩家 -> 5
00704ED0  mov esi,[eax+4] / test esi,esi / jne        ; 无行会 -> 12
00704EDE  3A 5E 28           cmp bl, byte ptr [esi+0x28]   ; 请求值 vs 当前值
00704EE1  74 1F              je  0x704F02                  ; 相同 -> 直接返回 0，不落盘
00704EE3  88 5E 28           mov byte ptr [esi+0x28], bl   ; 不同 -> 写入
00704EE6..0x704EFD                                          ; 再发标准三列 UPDATE
00704F02  33 C0              xor eax,eax                   ; 两条路都返回 0
```

### 2.4 生产路径本来就是对的，错的只有休眠模型

| 位置 | 现状 | 判定 |
|---|---|---|
| `GameSvr/Services/NativeCorpsWireCodec.cs:54` `UnionEnabled { get; set; } = true` | 活路径，`NativeCorpsService.cs:1297/1969` 消费 | `FAITHFUL` |
| `GameSvr/Services/NativeGildConcernState.cs:208` `private bool _enabled;` | 休眠模型，全仓只有两个审计工具引用 | `DIVERGENT`，**已修** |

`gild_21_27_review_report.txt`（2026-08-11）已经诊断出这一条并开好了药方，但**漏了下面这半个坑**。

### 2.5 审计工具自相矛盾——这是修不动的真正原因

```csharp
Equal(flag.Enabled, true,  "union flag defaults true (native 0x70633A ...)");   // 172
Equal(flag.Set(false), NoChange, "set false==current -> NoChange (no UPDATE)"); // 173
```

第 173 行的 `NoChange` 只有在默认 **false** 时才成立。只改字段会让失败从 172 行挪到 173 行。
`2eaf560` 那次同步更新了姐妹工具 `NativeGildConcernWarWiringCompatCheck`（450/474 行已按 default=true 写，
`_afbase` 里它是 PASS），却漏改了这支状态机走查。

我把起点改成原生态并重排走查。**没有削弱任何断言**——`sub_704EAC` 是对称的，
两个 `NoChange` 分支和两个 `Resave` 分支仍然全被覆盖，断言条数从 6 增到 7：

```
Enabled == true            (构造器默认)
Set(true)  -> NoChange     (0x704EE1 je)
Set(false) -> Resave       (0x704EE3 写 + 重发 UPDATE)
Enabled == false
Set(false) -> NoChange
Set(true)  -> Resave
Enabled == true            (回到默认)
```

### 2.6 边界

这条只动了 `NativeGildUnionFlagCell` 一个字段初始值 + 对应审计。
**没有碰 `w/m-gild` 的行会业务或持久化**：`NativeCorpsService` / `NativeGildMySqlStore` / `Associations/Guild.cs` 一行未改。

---

## 3. 任务三（原 BLOCKED）：`InCastleWarArea` —— **已解封**

### 3.1 原生是一个硬编码的绝对矩形

`sub_659FD4`，13 个 E8 调用者，函数体 67 字节，可整段读完：

```
00659FD4  55 8B EC              push ebp / mov ebp,esp
00659FD9  8B F9                 mov edi, ecx            ; X
00659FDB  8B 75 08              mov esi, [ebp+8]        ; Y
00659FDE  33 C9                 xor ecx, ecx            ; result = False
00659FE0  3B 50 20              cmp edx, [eax+0x20]     ; PalaceMap
00659FE3  74 2A                 je  .true
00659FE5  3B 50 24              cmp edx, [eax+0x24]     ; SecretMap
00659FE8  74 25                 je  .true
00659FEA  3B 50 1C              cmp edx, [eax+0x1c]     ; CastleMap
00659FED  75 22                 jne .false
00659FEF  81 FF 05 02 00 00     cmp edi, 0x205  / 7E 1A jle .false     ; X > 517
00659FF7  81 FF EA 02 00 00     cmp edi, 0x2EA  / 7D 12 jge .false     ; X < 746
00659FFF  81 FE BC 00 00 00     cmp esi, 0x0BC  / 7E 0A jle .false     ; Y > 188
0065A007  81 FE 90 01 00 00     cmp esi, 0x190  / 7D 02 jge .false     ; Y < 400
0065A00F  B1 01                 .true: mov cl, 1
0065A011  8B C1 / 5F 5E 5D      mov eax,ecx / pop...
0065A016  C2 04 00              ret 4
```

四个边界**全是开区间**（`jle`/`jge` 把等号排除掉）。

三个地图字段由 `Initialize` `0x65AA90` 钉死：

| 字段 | 赋值点 | 来源 |
|---|---|---|
| `[castle+0x20]` PalaceMap | `0x65AB0E 89 73 20` | `FindMap('0150')`（`0x65AB02`） |
| `[castle+0x24]` SecretMap | `0x65AB47 89 73 24` | `WayMap`，默认 `'D701'`（`0x65927E`） |
| `[castle+0x1C]` CastleMap | `0x65ABB2 89 73 1C` | `CastleMap`/`DefMapStr`，默认 `'3'` |

### 3.2 「配置键零命中」的真正含义

我独立复扫（ASCII 大小写不敏感 + UTF-16LE，全 16.8 MB）：

```
castlewarrange   0    warrangex     0    warrangey     0
castlewarrangex  0    castlewarrangey 0
castlehomex      0    castlehomey   0    castlehomemap 0    castletaxrate 0
castlename       1 (0x659B84)       castledir     2
```

**不是「范围从别处算」，是根本没有范围这个概念**——矩形是编译期常量。

### 3.3 C# 差在哪

```csharp
// 修改前
if (envir == m_MapCastle && Math.Abs(m_nHomeX - nX) < m_nWarRangeX &&
    Math.Abs(m_nHomeY - nY) < m_nWarRangeY) return true;
```

`m_nHomeX=0x2BC=700`、`m_nHomeY=0x190=400`、range=100 ⇒ **X∈(600,800), Y∈(300,500)**。
原生是 **X∈(517,746), Y∈(188,400)**。X 偏 +83、Y 偏 +100。

面积上重叠只有一部分：原生 228×211 的框里，约三分之一落在 C# 框外；C# 框里也有大片不该算的区域。
更刺眼的是 Y 上界：原生 400 是开区间，而 HomeY 恰好就是 400 —— **原生的「城堡家坐标」本身不在攻城区内**，
C# 却把它当成圆心。

玩家可见后果（全部经 `CastleManager.InCastleWarArea`）：
进区强制红名（`TPlayObject.Message.cs:393`）、自由 PK 区名字颜色（`TBaseObject.cs:3382`）、
行会战击杀归属（`TBaseObject.Base.cs:1057`）、`@MakeItem` 禁用（`MakeItemCommand.cs:34`）、
攻城期禁跑（`Envirnoment.cs:626`）、`NormNpc` 归属城堡（`NormNpc.cs:1474`）。

### 3.4 顺带清掉两处 INVENTED

- **`m_EnvirList` 的六张地图**：`CastleManager.Initialize` 硬塞 `"0151".."0156"`。
  全镜像 **raw ASCII 0 命中、UTF-16LE 0 命中**；对照组 `'0150'`（`0x65B0A4`，xref `0x65AB03`）
  和 `'D701'`（`0x659BDC`，xref `0x65927E`）都在。而且 `sub_659FD4` 整个函数**不遍历任何列表**。
  已删除 seeding。字段本身暂留——`AuditTools/NativeCorpsProtocolCheck/Program.cs:834` 会给它赋值，
  删字段要连那支工具一起改，属于另一次提交。
- **`m_nWarRangeX/m_nWarRangeY`**：除这一处外零引用，已删。
  `GameSvrConfig.nCastleWarRangeX/Y` 留着（配置域不是我的地盘），但它们现在无人读。

### 3.5 一个我**没有**改回去的地方，请主代理复核

原生对 `envir` **没有空指针检查**。C# 原来有 `if (envir == null) return false;`，我按字节去掉了。
在不承载城堡地图的服务器上，三个地图字段都是 nil（原生的 `Initialize` 同样有服务器序号门），
此时原生对 nil envir 会**返回 true**，C# 现在的引用比较复刻了这个行为。
这是原版的怪癖，不是我引入的；但它只在 `m_PEnvir == null` 的对象上才可见。
如果主代理认为风险大于忠实度，加回一行 guard 即可，我把证据摆在这里由你裁决。

---

## 4. 任务二-2：WineCount —— **穷尽扫描，原生根本没有消费点**

上一轮记的是「消费语义未找到」。这一轮把 `castle+4` 的**所有**字节访问形式扫完
（`0F B6` / `0F BE` / `8A` / `88` / `C6` / `FE` / `80` / `00` / `28` / `38` / `2A` / `3A`，
modrm mod=01、disp8=0x04），castle 模块段 `0x655000..0x660000` 内命中六条，逐条定性：

| VA | 字节 | 身份 |
|---|---|---|
| `0x659931` | `88 46 04` | **加载**：`沙巴克.txt` `[setup] WineCount` → castle+4（键在 `0x659921`） |
| `0x65A62A` | `8A 46 04` | **保存**：castle+4 → `沙巴克.txt`（键在 `0x65A62E`） |
| `0x65BBC3` | `C6 43 04 14` | **日切**：写 20 |
| `0x65CCBF` | `8A 43 04` | **上报**：`castledetail` SQL 的第 4 个参数 |
| `0x658127` | `8A 46 04` | 假阳性 |
| `0x658138` | `88 46 04` | 假阳性 |

两条假阳性已排除：`sub_658114` 的基址是全局 `[0x7D7038]`（`0x65811C mov esi,[0x7D7038]`），
函数体是对一个 5 字节结构做 `bt` 位集差分（`0x658146 bt [ebp-5],eax`，循环上界 `0x65818A cmp bl,0x25`），
与城堡无关。

**结论：M2Server 里 WineCount 只被「载入 / 存盘 / 日切置 20 / 写进日切 SQL」四件事碰过，
没有任何自减、比较或阈值判断。** 这不再是「找不到」，是**已证否**。
C# 现状（`m_btWineCount` 只做持久化 + 日切重置）因此判 **`FAITHFUL`**。

残留问题（登记为 `BLOCKED`，见 §7 B1）：那么谁扣它？只可能在引擎之外——眼神插件或 NPC 脚本。
这需要扫 `D:/光头卧龙` 的脚本树与眼神转储，不在本轮授权范围。

---

## 5. 任务二-1：旗帜系统（`MISSING`，完整证据）

### 5.1 每 tick 重生：`sub_65CF98`，唯一调用者 `0x65BBDB`

关键定位：`0x65BBDB` 在日切分支 `0x65BB9E je 0x65BBC7` 的**汇合点之后**，
所以它**每个 Run tick 都跑**，不是日切才跑。（对照：`0x65BBA2 call 0x65CBB8`（日切 SQL）
和 `0x65BBC3`（WineCount=20）在分支**里面**。）

```
0065CFA3  8B 47 14           mov eax,[edi+0x14]        ; 旗帜列表 (TList)
0065CFA6  8B 70 08           mov esi,[eax+8]           ; Count
0065CFC0  E8 87 7D DC FF     call 0x424D4C             ; rec = list[i]
0065CFCB  8B 58 20           mov ebx,[rec+0x20]        ; 活体对象
0065CFCE  85 DB / 74 36      test ebx,ebx / je 重生
0065CFD2  80 7B 73 00        cmp byte ptr [ebx+0x73],0 ; ghost 字节
0065CFD6  75 30              jne 重生
        ; --- 活着：把活体的持有者名同步回记录 ---
0065CFDB  8B 40 14           mov eax,[rec+0x14]
0065CFDE  8B 93 D8 04 00 00  mov edx,[obj+0x4D8]
0065CFE4  E8 33 89 DA FF     call 0x40591C             ; 比较；相同则跳过
0065D001  E8 4E 85 DA FF     call 0x405554             ; 不同则 rec+0x14 := obj+0x4D8
        ; --- 重生 ---
0065D00B  push [rec+0x00] / push [rec+0x04] / push 1 / push 1 / push 0
0065D01E  8B 48 18           mov ecx,[rec+0x18]        ; 旗帜怪物名
0065D021  8B 57 1C           mov edx,[castle+0x1C]     ; 城堡地图
0065D02B  E8 9C ED 01 00     call 0x67BDCC             ; RegenMonster
0065D035  89 58 20           mov [rec+0x20],ebx
0065D040  E8 D3 63 02 00     call 0x683418             ; (obj, rec+0x14) 设持有者
0065D056  E8 F9 84 DA FF     call 0x405554             ; obj+0x4F0 := rec+0x1C  (旗帜名)
0065D066  89 90 F4 04 00 00  mov [obj+0x4F4],edx       ; := rec+0x00  (X)
0065D072  89 90 F8 04 00 00  mov [obj+0x4F8],edx       ; := rec+0x04  (Y)
```

注意 `0x65CFD2` 用的是 **`[obj+0x73]` ghost**（REPLICATION_RULES §4.6），不是 `+0x74` 死亡。
即「对象为空 **或** 已标记删除」才重生。

### 5.2 旗帜记录布局

| 偏移 | 内容 | 证据 |
|---|---|---|
| `rec+0x00` | X (int) | `0x65D00B` push / `0x65D060` → `obj+0x4F4` |
| `rec+0x04` | Y (int) | `0x65D011` push / `0x65D06F` → `obj+0x4F8` |
| `rec+0x14` | 持有者行会名 (string) | `0x65CFDB` / `0x65D03B` / 存盘 `0x65A834` |
| `rec+0x18` | 重生用的怪物名 | `0x65D01E` |
| `rec+0x1C` | 旗帜名 | `0x65D053` → `obj+0x4F0` |
| `rec+0x20` | 活体对象指针 | `0x65CFCB` / `0x65D035` |

旗帜活体对象侧：`obj+0x4D8` 持有者行会名、`obj+0x4E0` 持有者军团、`obj+0x4F0` 旗帜名、`obj+0x4F4/0x4F8` X/Y。

### 5.3 落盘：`flagOwnerN` 是 **1-based**，且空槽**整条不写**

存盘（`SaveCastleInfo` 尾部 `0x65A80A..0x65A86B`）：

```
0065A80A  8B 46 14           mov eax,[esi+0x14]
0065A82B  83 78 20 00        cmp dword ptr [rec+0x20], 0
0065A82F  74 36              je 0x65A867          ; 无活体 -> 这一条键根本不写
0065A837  8B 80 D8 04 00 00  mov eax,[obj+0x4D8]  ; 值 = 持有者行会名
0065A841  8D 43 01           lea eax,[ebx+1]      ; <-- i+1，1-based
0065A844  E8 53 20 DB FF     call 0x40C89C        ; IntToStr
0065A84F  BA 48 AA 65 00     mov edx,0x65AA48     ; 'flagOwner'
0065A854  E8 C3 AF DA FF     call 0x40581C        ; key = 'flagOwner' + IntToStr(i+1)
0065A864  FF 57 04           call [edi+4]         ; WriteString
```

读盘（`0x659A69..0x659AD9`）**无条件**读，缺省 `''`，直接写进 `rec+0x14`：

```
00659A87  6A 00              push 0               ; Default = ''
00659A96  8D 43 01           lea eax,[ebx+1]      ; 同样 1-based
00659AAA  BA 9C 9F 65 00     mov edx,0x659F9C     ; 'flagOwner'
00659AC2  FF 17              call [edi]           ; ReadString
00659ACD  83 C0 14           add eax,0x14
00659AD0  E8 7F BA DA FF     call 0x405554        ; rec+0x14 := value
```

两侧都以 `[castle+0x14]` 的 **Count** 为循环上界——列表本身来自
`沙巴克基础配置.txt` 的 `flag` / `flagName` 键（`0x65970B` / `0x65975A`）。
即：**基础配置声明旗帜的位置与名字，运行态 `沙巴克.txt` 只记谁占着。**

生产 `D:\光头卧龙\mud2.0\Mir200\Envir\Castle\沙巴克.txt`（466 字节）里没有任何 `flagOwner` 键，
与「列表为空 ⇒ `0x65A810 dec eax / 7C 58 jl` 直接跳过整个循环」一致。

### 5.4 旗帜易主会写 `guild.CastleLog`

```
006834C1  8B 83 E0 04 00 00  mov eax,[ebx+0x4E0] / mov eax,[eax+4]
006834CA  E8 3D 22 08 00     call 0x70570C          ; 取军团长名到 rec+0x10
006834CF  8B 4D DC           mov ecx,[ebp-0x24]     ; = lordName
006834D2  8B 93 D8 04 00 00  mov edx,[ebx+0x4D8]    ; = guildName
006834D8  8B 83 F0 04 00 00  mov eax,[ebx+0x4F0]    ; = name（旗帜名）
006834DE  E8 81 59 FD FF     call 0x658E64          ; CastleLog
...
0068351C  66 BA 36 00        mov dx, 0x36           ; 日志类别 54
00683517  B9 78 35 68 00     mov ecx, 0x683578      ; '沙巴克旗帜'（len=10，5 汉字）
```

### 5.5 C# 现状

`flagOwner` / `CastleFlag` 在全仓只出现在 `CastleConfManager.cs` 的文档注释里，**零实现**。
判定 **`MISSING`**（整套：列表、基础配置解析、每 tick 重生、`flagOwnerN` 读写、占领 CastleLog）。

**好消息是它不会毁数据。** `SystemModule/Common/IniFile.cs` 的 `Save()` 输出整个 `iniCahce`，
而 `iniCahce` 由构造期 `Load()` 从磁盘全量填充，所以即便 C# 不认识 `flagOwnerN`，
也会原样带过去，不会静默删键。（这一条我特意查了——按 §1.4「存档记录布局同样算协议」，
如果它是 write-only 缓存就会是一个当场丢数据的单向门。）

因此本项**不必抢时间落地**，可以整块排期。最小实现顺序建议：
基础配置 `flag`/`flagName` 解析 → 列表 → `flagOwnerN` 读写（先做到往返一致）→ 每 tick 重生 → 占领 CastleLog。

---

## 6. 任务二-3：`castledetail` 日切 SQL 与 `guild.CastleLog`

两者在 C# 全仓 **0 命中**，判定 **`MISSING`**。

### 6.1 `sub_65CBB8` —— 日切 `castledetail` INSERT

调用点 `0x65BBA2`，在日切分支内、清零 `TodayIncome` **之前**（顺序重要：上报的是**昨天**的数）。

两道前置门：

```
0065CBEB  A1 40 5C 7D 00     mov eax,[0x7D5C40] / cmp [eax],0 / je 退出   ; DB 管理器在不在
0065CBF9  83 7B 48 00        cmp dword ptr [ebx+0x48], 0 / je 退出        ; 无主城堡不写
```

先拼 `warinfo`：遍历 `[ebx+0x8C]`（攻城报名表），每条非空记录追加 5 段
（累加器 + `[rec+8]` 行会名 + `'   '`（`0x65CE20`，3 空格）+ `FormatDateTime('YYYY-MM-DD')`（`0x65CE2C`）+ `'|'`（`0x65CE40`））。

然后按长度分流：

```
0065CC71  E8 5A 8B DA FF     call 0x4057D0            ; Length(warinfo)
0065CC78  81 FE 00 08 00 00  cmp esi, 0x800           ; 2048
0065CC7E  7F 0D              jg  0x65CC8D             ; >2048 -> 内联字段留空
0065CC86  (LStrAsg)                                   ; <=2048 -> 内联
```

八个参数（`0x65CD2D mov ecx,7` 即 0..7）：

| # | 值 | 类型标记 |
|---|---|---|
| 0 | `[ebx+0x44]` OwnGuild | `0x0B` AnsiString |
| 1 | `[ebx+0x80]` TotalGold | `0x00` Integer |
| 2 | `[ebx+0x84]` TodayIncome | `0x00` |
| 3 | `movzx [ebx+4]` WineCount | `0x00` |
| 4 | `FormatDateTime('yyyy-mm-dd', [ebx+0x68])` changeDate | `0x0B` |
| 5 | `FormatDateTime('yyyy-mm-dd', [ebx+0x70])` WarDate | `0x0B` |
| 6 | `FormatDateTime('yyyy-mm-dd HH:MM:SS', [ebx+0x78])` IncomeToday | `0x0B` |
| 7 | warinfo | `0x0B` |

```
0065CD32  B8 7C CE 65 00     mov eax, 0x65CE7C   ; len=147
  insert into castledetail(OwnGuild,TotalGold,TodayIncome,WineCount,changeDate,
  WarDate,IncomeToday,warinfo)Values("%s",%d,%d,%d,"%s","%s","%s","%s");
0065CD48  E8 FB 80 0C 00     call 0x724E48       ; exec, cl=1
```

`warinfo > 2048` 的补写路径（**成功后才走**，`0x65CD4D test al,al / je`）：

```
0065CD60  BA 18 CF 65 00     mov edx, 0x65CF18   ; 'select Last_Insert_id() from castledetail;'
0065CD74  E8 6F 7E 0C 00     call 0x724BE8       ; 取 id，<=0 则放弃
0065CD98  68 4C CF 65 00     push 0x65CF4C       ; 'select idx,warinfo from castledetail where idx='
0065CDAA  68 84 CF 65 00     push 0x65CF84       ; ';'
0065CDCD  B9 90 CF 65 00     mov ecx, 0x65CF90   ; 'warinfo'
0065CDD5  E8 42 89 0C 00     call 0x72571C       ; 长字段写入
```

顺带产出**城堡字段偏移表**（与生产 `沙巴克.txt` 的 `[setup]` 七键逐一对上）：

| 偏移 | 字段 |
|---|---|
| `+0x04` | WineCount (byte) |
| `+0x08` | 当日秒数（`0x49E39C`） |
| `+0x14` | 旗帜列表 |
| `+0x1C/0x20/0x24` | CastleMap / PalaceMap / SecretMap |
| `+0x28/0x29/0x2A/0x2B` | StartWar / UnderWar / ShowOverMsg / ForceWar |
| `+0x44` | OwnGuild (string) |
| `+0x48` | 主行会对象 |
| `+0x68` / `+0x70` / `+0x78` | changeDate / WarDate / IncomeToday（TDateTime double） |
| `+0x80` / `+0x84` | TotalGold / TodayIncome |
| `+0x8C` / `+0x90` | 攻城报名表 / 在宫行会表 |
| `+0x294/0x296` | OfficeDoorX/Y (word) |
| `+0x298/0x29A/0x29C/0x29E` | OfficeMinX/MinY/MaxX/MaxY (word) |

### 6.2 `sub_658E64` —— `guild.CastleLog`

```
00658E8D  C6 45 E8 0B        [ebp-0x1C] = eax  类型 0x0B   ; name
00658E94  C6 45 F0 0B        [ebp-0x14] = esi  类型 0x0B   ; guildName
00658E9B  C6 45 F8 0B        [ebp-0x0C] = edi  类型 0x0B   ; guildLordName
00658EA2  B9 02 00 00 00     mov ecx, 2                     ; 3 个参数
00658EA7  B8 F0 8E 65 00     mov eax, 0x658EF0   ; len=125
  replace into guild.CastleLog(name, guildName, guildLordName,takeDay, takeTime)
  values("%s", "%s", "%s", CURRENT_DATE, Now());
00658EBD  E8 86 BF 0C 00     call 0x724E48       ; cl=1
```

两个调用点：

| 调用点 | name | guildName | lordName |
|---|---|---|---|
| `0x65C009`（**易主**，GetCastle 末尾） | `0x65C078` 字面量 `'沙巴克'`（len=6，3 汉字）——**不是 `m_sName`** | `[ebx+0x44]` | `0x70570C([ebx+0x48]→[+4])` 的 `rec+0x10` |
| `0x6834DE`（**旗帜易主**） | `[obj+0x4F0]` 旗帜名 | `[obj+0x4D8]` | 同上，取自 `[obj+0x4E0]` |

`0x65C009` 位于 GetCastle 的 SS_211 分支**之后**，即每次易主都会写，与 `notifyServerGroup` 无关。

### 6.3 可落地性

C# 已有同族基础设施（`NativeGildMySqlStore` / `NativeCorpsStore` / `NativeMailStore`，
外加 `AuditTools/NativeSqlVerbatimCheck` 逐字钉 SQL 文本）。所以做法是现成的：
新建一个 castle store，把上面两条 SQL 原文照抄进去，并把 `>2048` 的 `Last_Insert_id()` 分流一起搬。
**我没有落地**：需要接 DB 管理器与新工程引用，而我这一轮不能编译，摸黑改这类接线不划算。
留给能跑 build 的一轮，规格已在上面写全。

---

## 7. `BLOCKED` 清单

| # | 项 | 缺什么 |
|---|---|---|
| B1 | 谁扣 WineCount | 引擎侧已证否（§4）。剩下的可能性在 `D:/光头卧龙` 脚本树和眼神转储（`0x10000000` 基址），需要单独一轮授权去扫「酒 / Wine / 城主」相关的脚本 API 与 caret/数字隧道 |
| B2 | `FlyStoneCount` 与 `沙巴克随机石` | 基础配置 `[setup] FlyStoneCount`（`0x6591B2`）C# 未读；消费点疑在 `0x65C883`（`'沙巴克随机石'`，`0x65C8F8` len=12）。这一轮没展开该函数 |

原 BLOCKED「`InCastleWarArea` / `CastleWarRangeX` 的原生公式」**已解封**，见 §3。

---

## 8. 复核中发现的前人/工具错误（按价值排序）

1. **`staging/castle_subsystem_final.md` 第五节「`0x65BF80` 是 Load 非 Save — 已闭」是错的**，
   主工作树 `UserCastle.cs:594` 的同款注释也是错的。字节见 §1.1。
   这条错误还被 memory `castlewar-five-items-closed` 记成了「已闭合」，属于**错误结论被归档为定论**，
   建议把那条 memory 一并订正。
2. **`AuditRepoRoot` 的硬编码兜底会让审计读到别的分支**（§1.2）。已实测同一支工具在两棵陈旧树上
   给出两个方向相反的假红。这是 §4.17「审计工具本身可能是坏的」的新样本，建议写回 REPLICATION_RULES。
3. **`NativeGildConcernStateCompatCheck` 自相矛盾**（§2.5）：172 行断言默认 TRUE，
   173 行断言的状态转移只在默认 FALSE 时成立。只按 `gild_21_27_review_report.txt` 的药方改字段，
   会把失败挪到下一行然后以为药方无效。
4. **线性反汇编在 castle 段极易失步**，本轮踩了两次（`0x65CB40`、`0x659F30` 都解成垃圾）。
   Delphi 在函数间塞了字符串常量与 `8D 40 00` 填充。**从 E8 目标地址或 `55 8B EC` 序言起手，
   不要从「大概附近」起手**；不确定时先 hexdump。
5. **「立即数常量扫描」在城堡域同样有假阳性**：`castle+4` 的 6 个命中里 2 个是无关全局
   `[0x7D7038]` 的第 5 字节（§4）。必须回溯到序言确认对象身份，光看偏移不够。

---

## 9. 建议优先级

| 序 | 项 | 理由 |
|---|---|---|
| P0 | 修 `AuditRepoRoot` 兜底 / sweep 显式传 repo root | 不修的话后面每一轮都会有人去修不存在的缺陷；本轮已浪费一条红 |
| P1 | `InCastleWarArea`（**已修，待编译验证**） | 影响红名、名字颜色、行会战归属、禁跑、`@MakeItem`，且是天天可见的判定 |
| P1 | union flag 默认值（**已修，待编译验证**） | 每次重启后所有行会静默不可申请联盟 |
| P2 | `guild.CastleLog` 易主写入 | 攻城结果的唯一审计轨迹；缺了运营查不到城主变更史 |
| P2 | `castledetail` 日切 SQL | 同上，且带 `warinfo` 攻城报名快照 |
| P3 | 旗帜系统整块 | 生产 `沙巴克.txt` 列表为空 ⇒ 当前部署零影响；且 IniFile 会带过未知键，不丢数据。可以整块排期 |
| P3 | `OfficeMinX/MinY/MaxX/MaxY` + 皇宫复活盒（`0x65A044` 的 `0x65A16A..0x65A1A8`） | 城主府内死亡的复活点判定，低频 |
| P3 | 删 `m_EnvirList` 字段 + 改 `NativeCorpsProtocolCheck:834` | 纯清理，需一次能编译的提交 |

---

## 10. 中间产物

| 文件 | 内容 |
|---|---|
| `_gp2/m2dis.py` | capstone 助手（注意：不能叫 `dis.py`，会和标准库循环导入） |
| `_gp2/sim_castle_audit.py/.txt` | 逐字复刻 `CastleOwnerTransitionCheck` 的字符串断言，跑两棵树 |
| `_gp2/q1_save.py/.txt` | `0x65A3B8` Save vs `0x65B22C` Load + 四个调用者 |
| `_gp2/q2_gild.py/.txt` | 行会构造器 `0x7062D0` + `0x70633A` |
| `_gp2/q3_flag.py/.txt` | `+0x28` 的全镜像读/写/比较扫描 |
| `_gp2/q4_flaguse.py/.txt` | `0x704494` 消费端 + `0x706944` 快照 |
| `_gp2/q5_setter.py/.txt` | `0x704EAC` 4581 处理器 + 两个策略槽 |
| `_gp2/q6_flags.py/.txt` | `0x65CF98` 旗帜重生 + 日切块 + `0x658E64` |
| `_gp2/q7_more.py/.txt` | `0x65CBB8` 完整 SQL + 两个 CastleLog 调用点 |
| `_gp2/q8_conf.py/.txt` | 配置键字符串 + Delphi 长度前缀 + xref |
| `_gp2/q9_persist.py/.txt`, `q9_flagio.txt` | `SaveCastleInfo` 全文 + `flagOwner` 读写循环 |
| `_gp2/q10..q19` | 战区 abs 扫描、WineCount 穷尽扫描、`0x659FD4`、`0151..0156` 零命中、配置键零命中 |
| `_gp2/prod_sabak.txt` | 生产 `沙巴克.txt` 转 UTF-8 |
| `_gp2/msg1.txt`, `msg2.txt` | 两条提交信息 |
