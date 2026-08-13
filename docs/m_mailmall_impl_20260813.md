# 邮件与商城系统 1:1 对账报告

日期：2026-08-13
工作树：`D:\loym2\.claude\wt2\m-mailmall`，分支 `w/m-mailmall`
镜像：`D:\loym2\staging\_reunpack_work\flat_image.bin`（M2Server 1.0.1.135，ImageBase `0x400000`）
复现工具：`D:\loym2\staging\mailmall_re\m2.py` + `q01..qNN` 脚本（只读，可复跑）

> 本报告每一条结论都给 VA + 原始字节。台账、既有 staging 文档、代码注释一律复核，
> 复核中推翻的前人结论单列一章。

---

## 0. 生产流量基线（判定优先级的一手证据）

`D:\光头卧龙\mud2.0\GateServer\GameGate2\procMsgLog\` 计数器：

| ident | 方向 | 次数 | 含义 |
|---|---|---:|---|
| `1046` | client | **50,039** | `CM_REQSEESHOP` 拉商城列表 |
| `1048` | client | **290,008** | `CM_DOSHOP` 购买 |
| `812` | srv | 29,358 | `SM_SHOPITEMS` |
| `815` | srv | 44,268 | `SM_FIRSTSHOP` 热销 |
| `4497` | srv | 4,111 | 商城配置刷新完成广播 |
| `1047` | client | **0** | `CM_RENEWSEESHOP` 未出现 |
| `813 / 814 / 816` | srv | **0** | 刷新 OK/FAIL、`SM_DOSHOP_FAIL` 均未出现 |
| `4462 / 4463 / 4468 / 4495 / 4496` | 双向 | **0** | 原生邮件客户端协议在本部署完全未使用 |

**结论：商城是本部署最热的写路径之一（29 万次购买），邮件客户端协议一次都没跑过。**
优先级必须据此排：商城 > 邮件。

`816` 线上 0 次这一条同时说明：原生购买在生产里几乎从不走失败分支。

---

## 1. 商城：原生真相（逐字节）

### 1.1 调用链总览

```
CM_DOSHOP 1048 ──> sub_6CB7E4 ──(确认闸 sub_6C7D88)──> PAS @ClientBuy(名, 数量, 需格数, 荣誉点)
                                                          │
                        本地无任何扣款 ────────────────────┤ YBNum >= Price 判定在脚本里
                                                          │ PsYBConsumEx(2,'YBShopBuy_YB',…) 异步扣元宝
                                                          ↓ 外部元宝库回调
                                                    PAS YBShopBuy_YB ──> Give/GiveBindItem
                                                          
CM_REQSEESHOP 1046 ─> sub_63A254 ─> sub_63CD0C(填每人限购) ─> 812 / 815
CM_RENEWSEESHOP 1047 > sub_63A32C ─> sub_63CD0C            ─> 813 / 814
商品表加载 SendYBShopConfig ─> sub_636D68 ─> PAS @GetYBShopConfig ─> 1104/1101 缓存
发货核心 sub_6CC420（纯发放，只发 816，从不扣款）
```

### 1.2 【MALL-01】商品表契约：分隔符是 `$`，字段是 10 个 —— C# 用 `,` / 11 个，完全对不上

原生加载器 `sub_636D68`：

```
00636DC7  b1 7c                   mov cl, 0x7c        ; 商品名列表分隔符 '|'
00636F46  b9 50 73 63 00          mov ecx, 0x637350   ; '@GetYBShopConfig'（长串 len=16）
00636F40  6a 07                   push 7              ; 8 个参数(0..7)
00636F4F  e8 84 fc ff ff          call 0x636bd8       ; PAS 标签调用器
00636F8F  b1 24                   mov cl, 0x24        ; 配置字段分隔符 '$'   <<<<<<
00636FA7  43                      inc ebx             ; 字段序号，1 起
00636FC6  83 f8 0a                cmp eax, 0xa        ; 只认 1..10
00636FC9  0f 87 cb 00 00 00       ja 0x63709a         ; 第 11 个字段起被丢弃
00636FCF  ff 24 85 d6 6f 63 00    jmp dword [eax*4 + 0x636fd6]
```

字段跳表 `0x636FD6`（11 项）解出的映射，与生产脚本 `GetYBShopConfig` 的拼装顺序逐项吻合：

| # | 臂 VA | 落点 | 取值 | 生产脚本变量 |
|---:|---|---|---|---|
| 1 | `0x637002` | `[ebp-0x18]` 串 | 直接赋值 | `vClassName` |
| 2 | `0x637012` | `[ebp-0x1C]` 串 | 直接赋值 | `vItemList` |
| 3 | `0x63701F` | `[ebp-0x20]` 整 | `StrToIntDef(s,-1)` | `vGoodsIdx` |
| 4 | `0x63702F` | `[ebp-0x24]` 整 | 同上 | `vSrcPrice` |
| 5 | `0x63703F` | `[ebp-0x28]` 整 | 同上 | `vCurPrice` |
| 6 | `0x63704F` | `[ebp-0x34]` 整 | 同上 | `vLimitType` |
| 7 | `0x63705F` | `[ebp-0x38]` 整 | 同上 | `vLimitCount` |
| 8 | `0x63706F` | `[ebp-0x2C]` 整 | 同上 | `vEffectImg` |
| 9 | `0x63707F` | `[ebp-0x30]` 整 | 同上 | `vEffectCount` |
| 10 | `0x63708F` | `[ebp-0x3C]` 串 | 直接赋值 | `vGoodsExplain` |

每个整数臂都是 `83 ca ff  or edx,0xFFFFFFFF`（默认 **-1**）后 `call 0x40ca18`（`StrToIntDef`）。

**接收侧校验**（`0x6370AA..0x6370E0`，命中任一条就 `je 0x637213` 丢弃并打日志）：

```
006370AA  83 7d e8 00     cmp [ebp-0x18], 0     ; vClassName 为空       -> 丢
006370B4  83 7d e0 ff     cmp [ebp-0x20], -1    ; vGoodsIdx    非法      -> 丢
006370BE  83 7d dc ff     cmp [ebp-0x24], -1    ; vSrcPrice    非法      -> 丢
006370C8  83 7d d8 ff     cmp [ebp-0x28], -1    ; vCurPrice    非法      -> 丢
006370D2  83 7d d4 ff     cmp [ebp-0x2c], -1    ; vEffectImg   非法      -> 丢
006370DC  83 7d d0 ff     cmp [ebp-0x30], -1    ; vEffectCount 非法      -> 丢
```

**注意：`vLimitType` / `vLimitCount` 不在校验清单里**，它们可以停在 -1 而记录仍被接受。

记录分配与落位（`0x6370E6` 起，`0x10C` = 268 字节，先整块清零）：

```
006370E6  b8 0c 01 00 00  mov eax, 0x10C            ; 记录 268 字节
006370FD  e8 2a ca dc ff  call 0x403b2c             ; FillChar 0
0063710E  89 10           mov [rec+0], edx          ; +0   dword = vGoodsIdx
00637131  ... b1 4f       cl=0x4F -> [rec+8]        ; +8   ShortString[79] = vItemList
00637139  8d 70 58        lea esi, [rec+0x58]       ; +0x58 起是 180 字节 TClientShop
```

### 1.3 【MALL-02】180 字节客户端记录：C# 的 `+46` / `+48` 两个字段写错了

原生填充（`esi` = 记录 +0x58）：

| 偏移(十进制) | 字节证据 | 内容 |
|---:|---|---|
| +0 | `0x637157 b1 0f` | 商品名 ShortString[15] |
| +16 | `0x63717A b1 0f` | 分类名 ShortString[15] |
| +32 | `0x637187 66 c7 46 20 00 00` | Looks，置 0（由 1101 处理器回填） |
| +34 | `0x637181 66 c7 46 22 00 00` | page/分类号，置 0（后填） |
| +36 | `0x637191 66 89 46 24` | `vSrcPrice` |
| +38 | `0x637199 66 89 46 26` | `vCurPrice` |
| +40 | `0x6371A1 66 89 46 28` | `vLimitType` |
| +42 | `0x6371A9 66 89 46 2a` | `vLimitCount` |
| +44 | `0x6371AD 66 c7 46 2c 00 00` | 置 0，**发包前由 `sub_63CD0C` 填每人当前限购数** |
| +46 | `0x6371BD 66 89 46 2e` | **`vEffectCount`** |
| +48 | `0x6371B6 89 46 30`（dword） | **`vEffectImg`** |
| +52 | `0x6371DF b1 7f` | 描述 ShortString[127] |

C# `GameSvr/Players/TPlayObject.Mall.cs:87-98` 写的是：

```
+44 = GetCurrentLimitValue(...)   ✓ 与 sub_63CD0C 语义一致
+46 = AlignWriter 补 0            ✗ 原生是 vEffectCount
+48 = (uint)item.CurrencyType     ✗ 原生是 vEffectImg（dword）
```

**玩家可见后果**：客户端 `mir2.data.shop` 按 +46/+48 取特效张数与特效图号做商品展示，
生产脚本里这两个值是 `520/18`、`410/10`、`380/1`。C# 送 `0` 和 `货币类型(0..4)`，
商品图标/特效在白猪客户端会渲染错。属协议字段错位，违反 §1.4。

### 1.4 【MALL-03】`+44` 的填法：只在 `limitType > 0` 时回调脚本

`sub_63CD0C`（被 `0x63A2AD` / `0x63A2F4` / `0x63A364` 调用，即 812/815/813/814 四条发包路径）：

```
0063CD52  b9 b4 00 00 00     mov ecx, 0xB4          ; 按 180 字节步进
0063CD58  f7 f9              idiv ecx               ; 记录条数 = 载荷长度 / 180
0063CD68  66 83 78 28 00     cmp word [rec+0x28], 0 ; limitType
0063CD6D  76 63              jbe 0x63cdd2           ; == 0 -> 整条跳过，+0x2C 不动
0063CD97  b9 20 ce 63 00     mov ecx, 0x63ce20      ; '@GetLimitValue'
0063CDC3  66 89 42 2c        mov [rec+0x2c], ax     ; 脚本返回值 -> +44
0063CDCC  66 c7 40 2c 00 00  mov word [rec+0x2c], 0 ; 脚本无返回 -> 0
0063CDD2  81 45 fc b4 00 00 00  add [ebp-4], 0xB4
```

C# 无条件调 `GetCurrentLimitValue`；因为 `GetCurrentLimit` 在 `LimitType==0` 时返回 0，
结果等价，判 **FAITHFUL**。

（顺带记一条原生怪癖：`sub_63CD0C` 是**就地改全局缓存**，不是每人副本。C# 每人重建 body，
不会串写。这个差异只在并发下可观测，原生单线程发包，不构成缺陷。）

### 1.5 【MALL-04 · CRITICAL】生产环境下 C# 商城加载 0 个商品，整条最热路径是死的

生产脚本 `D:\光头卧龙\mud2.0\Mir200\Envir\YBShop\YBShopScript.pas`
（md5 `EB28955539FAD9B5F84B4D5F7D1A23D4`，12,001 字节，GBK）的形状是：

```pascal
const
  C_NeedLoadGoodsNames_001 = '贵族斗笠|五倍经验卷|…';
  C_NeedLoadGoodsNames_002 = '…';
…
function GetYBShopConfig(GoodsName: string; out vClassName …): string;
begin
  case GoodsName of
    '贵族斗笠': begin vClassName := '装饰'; vItemList := '贵族斗笠:1'; vGoodsIdx := 222; … end;
  end;
  if (vClassName <> '') and IsUsingGoodsName(GoodsName) then
    Result := vClassName + '$' + vItemList + '$' + IntToStr(vGoodsIdx) + '$' + …
end;
```

C# `MallManager.LoadPasMallItems` 用的三条正则实测（`staging/mailmall_re/q12_parsersim.py`，可复跑）：

| 正则 | 生产脚本 | 另一变体 `pas-include-context-20260714` |
|---|---|---|
| `C_NeedLoadGoodsNames\s*=\s*'…'` | **NO** → 抛 `InvalidDataException` | YES |
| `'名':\s*Result\s*:=\s*'…';` | **0 条** | 35 条 |
| `SetYBShopRefreshTime\('…'\)` | 3 条 | 3 条 |
| `FPayTask='g,i'` | NO → group=0/index=0 | YES |

正则 `C_NeedLoadGoodsNames\s*=` 要求 `=` 紧跟名字，而生产里是 `C_NeedLoadGoodsNames_001 =`，
中间隔着 `_001`，**全文件 5 处出现全部不匹配**。

于是 `LoadPasMallItems` 抛异常 → `LoadMallItems` 的 `catch` 吞掉 → 返回 `false`，
`_mallItems` 永远是空表，且因为 `_lastLoadTime` 没被更新，**每次调用都会重读文件、重跑正则、重抛异常**。

**玩家可见后果**（对照上面的流量计数器）：

- `1046`（线上 50,039 次）→ `GetItemsForClientType` 空表 → `recordCount==0` → **812 不发、815 不发**，商城面板全空。
- `1048`（线上 **290,008 次**）→ `GetItemByName` 返回 null → `failureCode=-5` → 每次都回 `SM_DOSHOP_FAIL(816)`。
  而原生 `816` 在生产计数器里是 **0 次**。等于把一条 29 万次/周期的成功路径整条翻成失败。

C# 的解析器是照着 `pas-include-context-20260714` 那个**改造过的变体**写的
（该变体确实有 `C_NeedLoadGoodsNames =` 单常量 + 35 条逗号配置表），
但那不是生产脚本，也不是本镜像的引擎契约。

**根因是架构性的**：原生根本不解析脚本文本，它是**调用脚本函数** `@GetYBShopConfig`
（`0x636F46 mov ecx,0x637350` / `0x636F4F call 0x636BD8`）拿返回串再按 `$` 切。
任何静态正则都只是替身，替身跟错了脚本变体就整体失效。

### 1.6 【MALL-05 · INVENTED】货币类型 0..4 在原生商品表里根本没有这个字段

原生 10 个字段（§1.2）里**没有货币类型、没有绑定标志、没有全服广播标志**。
生产脚本的付款判定只有一句：

```pascal
if This_Player.YBNum >= Price then          // 只认元宝
  … This_Player.PsYBConsumEx(2, 'YBShopBuy_YB', GoodsName, vGoodsIdx, vCurPrice, WantNum)
```

引擎侧同样没有任何扣款：`sub_6CB7E4`（1048）只做确认闸 + 调 `@ClientBuy`；
发货核心 `sub_6CC420` 通篇没有一条对金币 `[player+0x15C]`、声望或脚本变量的减法。

C# `MallManager.DeductCurrency` 却实现了 5 种本地扣款：

| CurrencyType | C# 行为 | 原生 | 判定 |
|---:|---|---|---|
| 0 元宝 | 已 fail-closed（-3） | 外部元宝库异步结算 | 屏蔽正确，见 §3.1 |
| 1 金币 | `m_nGold -= amount` 后发货 | **无此路径** | `INVENTED` |
| 2 灵符 | 已 fail-closed（-3） | 见 §1.7，原生只有"发"没有"扣" | 屏蔽正确 |
| 3 声望 | `SetShengWan(-amount)` 后发货 | **无此路径** | `INVENTED` |
| 4 充值点 | 扣 `V(group,index)` 后发货 | **无此路径** | `INVENTED` |

`CurrencyType` 来自 C# 自己切出来的 `fields[7]`。在生产脚本里第 7 个 `$` 字段是
`vLimitCount`，第 8 个是 `vEffectImg` —— 就算解析器修好，`fields[7]` 也不是货币类型。

**这三条是"给物不扣钱（对原生而言）"的物品产出口**：玩家用原生从不接受的货币换到真实物品。
按 §1.3「原版没有但 C# 有 → 移除或屏蔽」，1 / 3 / 4 应当与 0 / 2 一样 fail-closed。

### 1.7 【MALL-06 · MISSING】灵符：原生有"发放"，C# 整条缺失

`sub_6CC420` 对商品名等于 `灵符`（长串 `0x6CC768`，len=4，GBK `c1 e9 b7 fb`）的分支：

```
006CC4EE  8b 45 e0        mov eax, [ebp-0x20]     ; 本 token 的商品名
006CC4F1  ba 68 c7 6c 00  mov edx, 0x6cc768       ; '灵符'
006CC4F6  e8 21 94 d3 ff  call 0x40591c           ; 比较
006CC4FB  0f 85 c9 00 00 00  jne 0x6cc5ca         ; 不是灵符 -> 走物品路径
006CC501  8b 45 e4        mov eax, [ebp-0x1c]     ; count
006CC504  01 86 d8 0b 00 00  add [esi+0xbd8], eax ; 玩家灵符余额 += count（无上限检查）
006CC50F  e8 18 af 00 00  call 0x6d742c           ; 余额变更通知
006CC52B  66 ba 33 00     mov dx, 0x33            ; 日志类型 51
006CC531  e8 aa c6 09 00  call 0x768be0           ; AddLogRec(51, '灵符', 222222, count, '商城购入')
```

`0x6CC518 push 0x3640E` = **222222**，是灵符发放日志的 GoodsIdx 常量。
（对照生产脚本 `YBShopBuy_YB` 里物品发放走的是 `AddLogRec(51, 名, 333333, 数量, '商城购入'+DescName)`。）

C# 没有任何"发放灵符"路径，只有一条把灵符当**支付**货币的 fail-closed 分支——方向反了。

### 1.8 【MALL-07】发货核心 `sub_6CC420` 的确切阶梯（C# 需照抄的语义）

```
006CC465  c7 45 f0 01 00 00 00  mov [ebp-0x10], 1     ; result := 1
006CC482  b1 2f                 mov cl, 0x2f          ; token 分隔符 '/'      <<< 不是 ';'
006CC4A1  b8 5c c7 6c 00        mov eax, 0x6cc75c     ; ':'  名/数量分隔
006CC4BC  b9 08 00 00 00        mov ecx, 8            ; 数量子串只取 8 个字符
006CC4E6  e8 2d 05 d4 ff        call 0x40ca18 (edx=1) ; StrToIntDef(数量, 1)
006CC5D1  85 c0 / 7e            test eax,eax / jle    ; count <= 0 -> 整个 token 跳过
006CC5FF  80 7b 14 07           cmp byte [item+0x14],7; StdMode==7 才堆叠
006CC609  3b 45 e4              cmp eax,[ebp-0x1c]    ; 单堆上限 vs 剩余
006CC612  66 89 43 26           mov [item+0x26], ax   ; 写堆叠数
006CC628  29 45 e4              sub [ebp-0x1c], eax   ; 剩余 -= 上限
006CC62B  6a 00 / b1 01         push 0 / mov cl,1     ; AddItemToBag(item, cl=1, 0)
006CC635  ff 97 48 02 00 00     call [vmt+0x248]
006CC63D  0f 84 84 00 00 00     je 0x6cc6c7           ; 失败
006CC6C9  e8 c2 7f d3 ff        call 0x404690         ;   -> item.Free（物品直接销毁）
006CC6CE  c7 45 f0 fb ff ff ff  mov [ebp-0x10], -5    ;   -> result := -5
006CC6D5  eb 0f                 jmp 0x6cc6e6          ;   -> 只中断本 token 的数量循环
006CC6E6  83 7d fc 00 / 75      cmp [ebp-4],0 / jne   ;      后续 token 仍然继续处理
006CC6F0  83 7d f0 00 / 7d      cmp [ebp-0x10],0/jge  ; result >= 0 -> 什么都不发
006CC701  66 ba 30 03           mov dx, 0x330         ; 816
006CC709  ff 93 50 02 00 00     call [vmt+0x250]      ; SendDefMessage(816, recog=result)
```

要点三条：
1. **失败码是 `-5`**，不是 -4；且只有 `-5` 这一个负码，成功时**一个包都不发**。
2. **背包满时原生把已建好的物品对象 `Free` 掉**（`0x6CC6C9 call 0x404690`），
   不掉地、不退款、不留邮件 —— 与 §4.5 里"背包满掉地上是发明的"那条一致。
3. 失败**不终止整串**，只终止当前 token 的剩余数量；后面的 token 照发。

---

## 2. 复核中推翻的前人结论

### 2.1 `NativeShopWriteTransaction.cs` 的两处描述与字节矛盾

该文件是"dormant 逆向基准模型"，被 `NativeShopWriteCompatCheck` 钉住，
但它的两条描述是错的，照它实现会走偏：

**(a) 第 46 行：`For each ';'-delimited "name:count" token`**

字节是 `0x6CC482 b1 2f  mov cl, 0x2f` —— **`'/'`（0x2F）**，不是 `';'`。
生产脚本 `YBShopBuy_YB` 里也写着 `GetValidStr(Str, TempStr, '/')`，两侧互证。

**(b) 第 34-40 行：`1048 DOSHOP … performs NO write` / `builds+sends an NPC confirm dialog (option code -4) via sub_636BD8`**

`sub_636BD8` 不是"发 NPC 确认框"，它是 **PAS 标签调用器**。1048 的实际动作：

```
006CB816  e8 6d c5 ff ff  call 0x6c7d88        ; 确认闸，返回 0 就整个跳过
006CB81D  0f 84 b2 00 00 00  je 0x6cb8d5
…（组装 4 个 variant 实参）…
006CB8C9  b9 40 b9 6c 00  mov ecx, 0x6cb940    ; 长串 '@ClientBuy'（len=10）
006CB8BC  6a 03           push 3               ; 高位下标 3 => 4 个参数
006CB8D0  e8 03 b3 f6 ff  call 0x636bd8        ; 调用脚本 ClientBuy
```

四个实参与生产脚本 `procedure ClientBuy(const GoodsName; const WantNum, NeedNum: Integer;
const IsUseGloryPoint: Boolean)` 逐个对上，其中 **NeedNum 是引擎算好的**：
`0x6CB87C call 0x6373E8`，而 `sub_6373E8` = `记录[+4] * wantNum` 再过 `sub_635988`。

所以 1048 **就是**写路径入口（限购、总价、元宝校验、日志、异步扣费全在 `ClientBuy` 里）。
"1048 不写"这个判断把整个商城写链路的入口判没了。

### 2.2 `ybdb_1101_1104_global_shop_exact_audit_20260720.md` 的 180 字节表把 +44 / +46 写反

该文档写 `+44 = effect count`、`+46 = alignment padding`。
字节是 `0x6371AD 66 c7 46 2c 00 00`（+44 置 0）、`0x6371BD 66 89 46 2e`（+46 = vEffectCount）。
+44 的真身是**每人当前限购数**，由 `sub_63CD0C` 在发包前回填（§1.4）。

### 2.3 `mail_system_gaps.md` 列的两个阻塞器已经修好了

- P0「物品重复窗口」：`TPlayObject.Mail.cs:267-299` 现在是无条件 `SetNativeMailAttachStatus(…,2)`，
  与 `0x70B5E3` 一致，`deliveredAll` 守卫已删。
- P1「邮件类型 2/3 被拒」：三处 `IsSupportedTag` 现均为 `tag is >= 1 and <= 6`。

这两条不要再当待办。
