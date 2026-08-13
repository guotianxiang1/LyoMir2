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

---

## 3. 邮件：逐项对账（全部自行反出，未采信前人结论）

生产流量里邮件族 4462/4463/4468/4495/4496 **一次都没出现**，所以这部分是忠实度维护，不是上线阻塞。

### 3.1 【MAIL-01 · FAITHFUL】领取阶梯 `sub_70B664`

```
0070B68A  83 ce ff              or  esi, -1              ; result := -1
0070B68D  80 7b 4d 02           cmp byte [mail+0x4D], 2  ; AttachStatus 已领
0070B693  be fe ff ff ff        mov esi, -2              ;   -> -2 直接返回
0070B6A2  ff 52 14              call [attachList vmt+0x14]  ; 附件条数 -> edi
0070B6AA  e8 45 cb 03 00        call 0x7481F4            ; 背包空格数
0070B6AF  3b f8 / 0f 8f         cmp edi, eax / jg 0x70B874 ; 条数 > 空格 -> 返回 -1
0070B6B7  83 cf ff              or  edi, -1              ; orderId := -1
0070B6BA  83 7b 54 00 / 7e      cmp [mail+0x54],0 / jle  ; MoneyCount > 0 才 INSERT Money_order
0070B6C4  e8 eb 09 00 00        call 0x70C0B4
0070B6CB  83 7b 50 01 / 75      cmp [mail+0x50],1 / jne  ; MoneyType == 1（元宝）
0070B6D5  83 7b 54 00 / 0f 8e   cmp [mail+0x54],0 / jle  ;   且 MoneyCount > 0
0070B79A  e8 45 5d 00 00        call 0x7114E4            ;   -> 异步入队
0070B79F  33 f6                 xor esi, esi             ;   -> result := 0（不同步回包）
0070B7A6  83 7b 50 00 / 0f 85   cmp [mail+0x50],0 / jne 0x70B83E
0070B7C0  e8 83 c1 fc ff        call 0x6D7948            ; 金币溢出测试
0070B7C9  be fd ff ff ff        mov esi, -3              ;   溢出 -> -3
0070B7DB  ff 91 8c 02 00 00     call [player vmt+0x28C]  ; IncGold
0070B83E  83 7b 50 00 / 75 30   cmp [mail+0x50],0 / jne 0x70B874   ; <<< 关键
0070B84A  e8 35 0c 00 00        call 0x70C484            ; Money_order.moneyStatus := 1
0070B84F  80 7b 08 04 / 75      cmp byte [mail+8],4      ; MailType == 4
0070B85F  e8 c0 12 00 00        call 0x70CB24 (dl=2)     ;   AttachStatus := 2
0070B864  be 01 00 00 00        mov esi, 1               ;   result := 1
0070B86D  e8 e6 fb ff ff        call 0x70B458            ; 否则走附件发放循环
```

`0x70B83E` 那条 `jne` 是全函数最容易读反的一处：**只有 `MoneyType == 0` 才会走到发货**。
`MoneyType >= 2` 与「`MoneyType == 1` 但 `MoneyCount <= 0`」两种情况都在这里被弹回，
带着开头的 `esi = -1` 返回。C# `FetchNativeMailAttachments` 的
`if (record.MoneyCount <= 0) return -1;` 与 `if (record.MoneyType != 0) return -1;`
正是这两条，判定 **FAITHFUL**。

`orderId` 在 MoneyType 校验**之前**就 INSERT 了（`0x70B6BA`），所以 MoneyType >= 2 时
Money_order 留下孤儿行——这是原生行为，C# 一致，**不要"修"**。

### 3.2 【MAIL-02 · FAITHFUL】背包门：原生把 48 写死在指令里

```
007481F4  e8 dc bf ff ff  call 0x7441D8        ; 纯 thunk
007441D8  8b 80 08 05 00 00  mov eax, [player+0x508]   ; 背包列表
007441DE  ba 30 00 00 00     mov edx, 0x30             ; 48，立即数
007441E3  2b 50 08           sub edx, [list+8]         ; 48 - Count
```

空格数就是 `48 - 件数`，48 是 `ba 30 00 00 00` 的立即数，不是任何容量查询。
C# 用 `BagCapacity.Of(this) - m_ItemList.Count`，在没有大背包时 `Of` 返回 `NativeSlots = 48`，
逐字节等价；装上大背包时按项目既定的单一权威扩容。判 **FAITHFUL**。

### 3.3 【MAIL-03 · FAITHFUL，附一条已知差异】金币溢出 `sub_6D7948`

```
006D7948  85 d2                 test edx, edx
006D794A  7c 11                 jl 0x6D795D                ; amount < 0  -> TRUE（拒绝）
006D794C  03 90 5c 01 00 00     add edx, [player+0x15C]    ; gold + amount
006D7952  3b 90 8c 06 00 00     cmp edx, [player+0x68C]    ; vs 金币上限
006D7958  7f 03                 jg 0x6D795D                ; >  上限 -> TRUE
006D795A  33 c0 / c3            xor eax,eax / ret          ; 否则 FALSE
```

比较方向是 `>`（`jg`），C# `(long)m_nGold + record.MoneyCount > m_nGoldMax` 一致。
`amount < 0` 那条臂在上游 `0x70B7B0 cmp [mail+0x54],0 / jle` 已经挡掉，不可达。

**已知差异（不建议改）**：原生 `add edx,[+0x15C]` 是 32 位加法，会回绕；回绕成负数时
`jg` 不成立，等于放行。C# 提到 64 位算，不会回绕。要触发差异需要 `gold + moneyCount`
接近 2^31，而 `gold <= [+0x68C]` 恒成立，所以只能由数据库里一个接近 2^31 的 moneyCount 触发。
C# 这一侧更严，且严的方向是**拒绝**而不是发放，不构成刷子。

### 3.4 【MAIL-04 · FAITHFUL】tag 闸就是 1..6

```
0070DBCC  80 fa 07              cmp dl, 7
0070DBCF  77 0a                 ja 0x70DBDB            ; > 7 -> CF=0 -> 拒
0070DBD1  83 e2 7f              and edx, 0x7F
0070DBD4  0f a3 15 e8 3d 7d 00  bt dword [0x7D3DE8], edx
0070DBDB  0f 92 c0              setb al
```

`0x7D3DE8` 实读 `7e 8d 40 00`，低字节 `0x7E` = bit 1..6；bit 0 与 bit 7 为 0，
所以 tag 0 与 tag 7 都被拒。**全镜像对 `0x7D3DE8` 只有 `0x70DBD7` 这一处引用**（自行复扫确认），
没有任何代码写这个掩码。名字表 `0x7D3DEC` 实读：
`[1]系统 [2]任务奖励 [3]离线补偿 [4]物品售卖 [5]过期返还 [6]摊位留言 [7]用户邮件`。
tag 7 有名字但 bit 是 0，仍然被拒。三处 `IsSupportedTag(tag) => tag is >= 1 and <= 6` **FAITHFUL**。

### 3.5 【MAIL-05 · FAITHFUL】过期清理的四个常量与比较方向全部对得上

`sub_70D0F4`：

```
0070D107  e8 c0 0a 00 00        call 0x70DBCC            ; 先过 tag 闸
0070D114  c7 45 fc 1e 00 00 00  mov [ebp-4], 0x1E        ; 上限 30 封
0070D11B  c7 45 f8 07 00 00 00  mov [ebp-8], 7           ; 保留 7 天
0070D122  80 fb 06 / 75 0e      cmp bl, 6 / jne          ; tag == 6
0070D127  c7 45 fc 14 00 00 00  mov [ebp-4], 0x14        ;   上限 20 封
0070D12E  c7 45 f8 03 00 00 00  mov [ebp-8], 3           ;   保留 3 天
```

C# `DefaultMaximumMails=30 / DefaultRetentionDays=7 / SystemMaximumMails=20 /
SystemRetentionDays=3`，且都按 `tag == 6` 分档 —— 四个数、一个分档条件全中。

天数换秒再比：

```
0070D183  c1 e0 03 / 8d 04 40   shl eax,3 / lea eax,[eax+eax*2]  ; days * 24
0070D189  6b c0 3c              imul eax, eax, 0x3C              ; * 60
0070D18C  6b c0 3c              imul eax, eax, 0x3C              ; * 60 -> 秒
0070D19B  77 24                 ja 0x70D1C1                      ; 保留期 >  年龄 -> 跳过
0070D1A1  7f 1e                 jg 0x70D1C1
```

跳过条件是「保留期 > 年龄」，所以**删除条件是 `年龄 >= 保留期`**。
C# 写的是 `if (age.TotalDays < retentionDays) continue;`，同为 `>=`。
这正是 §4.7 最容易写成严格 `>` 的地方，现有代码是对的。

条数超限的裁剪：`0x70D1DB cmp eax,[ebp-4] / 0x70D1DE jle 0x70D265`，
即 `count <= max` 就收工，与 C# `if (category.Count <= maximumMails) return;` 一致。

### 3.6 【MAIL-06 · FAITHFUL】清理资格判定 `sub_70D0CC` 与 C# 逐条相同

```
0070D0CC  80 78 4c 02  cmp byte [mail+0x4C], 2   ; MailStatus == 2
0070D0D2  8a 50 4d     mov dl, [mail+0x4D]
0070D0D5  80 c2 fe     add dl, 0xFE              ; -2
0070D0D8  80 ea 02     sub dl, 2
0070D0DB  72 04        jb  -> dl := 1            ; 借位 => AttachStatus ∈ {2,3}
0070D0E3  8a 40 08     mov al, [mail+8]          ; MailType
0070D0E6  2c 04 / 74   sub al,4 / je  -> dl := 1 ; MailType == 4
0070D0EA  2c 02 / 75   sub al,2 / jne            ; MailType == 6
```

即 `(MailStatus==2 && AttachStatus ∈ {2,3}) || MailType ∈ {4,6}`，
与 `NativeMailCacheService.IsCleanupEligible` 一字不差。

### 3.7 【MAIL-07 · FAITHFUL】清空 `sub_70D2D0`：逆序 + 首个非 1 立即中止

```
0070D2DD  c7 45 f8 ff ff ff ff  mov [ebp-8], -1          ; result := -1
0070D2E9  e8 de 08 00 00        call 0x70DBCC / je 0x70D344  ; tag 不合法 -> 返回 -1
0070D302  4b                    dec ebx                  ; 从 count-1 起**逆序**
0070D318  80 78 4c 02 / 75 20   cmp [mail+0x4C],2 / jne  ; MailStatus == 2
0070D31E  add dl,0xFE / sub dl,2 / 73 15 jae             ; AttachStatus ∈ {2,3}
0070D330  e8 1b 00 00 00        call 0x70D350            ; 删除
0070D338  83 7d f8 01 / 75 06   cmp [ebp-8],1 / jne 0x70D344 ; 返回值 != 1 -> 中止
```

`ClientClearAllNativeMail` 的逆序循环、同一资格判定、`result` 默认 -1、
非 1 立即 `break` —— 全部对上。

### 3.8 【MAIL-08 · FAITHFUL】排序比较器 `sub_709648`

先比 `[+0x4C]`（MailStatus）：1 在 2 之前；同状态再按 `sub_49E40C([+0x40], Now)` 的
年龄**升序**（越新越前）。C# `SortCategory` 是 MailStatus 升序 + CreateDate 秒级降序，
两者同义。

---

## 4. 守恒论证（逐处）

### 4.1 商城购买

改动后的顺序是：数量合法性 → 售罄 → 解析标准物品 → 背包空间 → 限购 → 总价
→ **结算闸（恒失败）** → 建物品 → 入包 → 写限购 → 扣库存 → 写日志。

- **不会「扣钱不给物」**：唯一的扣款入口 `TrySettleYuanbaoPayment` 在返回 false 之前
  没有任何写操作（余额判定是只读的 `m_nGameGold <` 比较），也不存在别的扣款点——
  `MallManager.cs` 已无 `m_nGold -=` / `m_nGameGold -=` / `SetShengWan` / `SetPlayerVariable(…,'V',…)`。
- **不会「给物不扣钱」**：物品对象在结算闸**之后**才创建，入包更在其后；
  闸恒 false，所以 `player.m_ItemList.Add` 不可达。
- 移除的三条本地扣款（金币/声望/充值点）本身就是「给物不扣钱（对原生而言）」：
  原生只收元宝且在外部结算，用金币换到的物品在原生侧凭空产生。
- 限购计数只在结算成功后写（脚本第 10 步），闸恒 false 时不写，不会出现
  「限购扣了但没买到」。

**残余风险：零。** 代价是商城在 C# GameSvr 上买不了东西——这是 fail-closed 的既定代价，
而不是回归：在此之前它同样买不了（商品表加载 0 条，见 §1.5），只是失败得更晚、
而且对 1/3/4 三种货币会真的发货。

### 4.2 商城列表渲染

`ClientQueryWhitePigMall` / `ClientRefreshWhitePigMall` 只读。去掉
`ResetDailyLimitIfNeeded` 之后这条路径**不再有任何写**，包括脚本变量写。
原生 `sub_63A254` / `sub_63CD0C` 同样只读玩家状态。守恒上无风险。

### 4.3 邮件领取

- 背包门（`0x70B6AF cmp edi,eax / jg`）在**任何**副作用之前，附件装不下就整件拒绝，
  不会出现「发了一半」。
- 金币溢出门（`0x70B7C0`）在 `IncGold` 之前，返回 -3 时金币未动、附件未发、
  `AttachStatus` 未改，邮件仍可领 —— 与原生一致。
- 发放循环 `sub_70B458` 之后**无条件**写 `AttachStatus := 2`（`0x70B5E3`），
  这是「附件最多发一次」的全部保证。单件 `AddItemToBag` 失败时原生丢弃那一件
  （损失窗口），但绝不重复发放。C# 现状与之一致。
- 元宝分支是异步的，原生自己就带一个崩溃窗口（`yb_user_data` 已提交而 `attachStatus`
  仍为 1 → 重领可重复得元宝）。这是原生设计，C# 复刻，**不要修**。
  这条与回收系统「有元宝产出就整件不回收」的判断同源：元宝无法与同步删除同事务。

### 4.4 邮件清理

`sub_70D0F4` 的删除对象必须同时满足资格判定（§3.6），即要么已读且附件已领/已弃，
要么是 `MailType 4/6`（售卖、摊位留言，本就无附件）。所以清理不会吞掉
未领取的附件。C# 一致。

---

## 5. 判定汇总（本轮独立复核后）

| 判定 | 数量 | 条目 |
|---|---:|---|
| `FAITHFUL` | 16 | MAIL-01..08 领取/清理/tag；MAIL-10 发送（仅脚本 `NewFullMailEx`、附件 `/` 组数 `>6` 整封拒、无金币上限）；MAIL-11 落盘三表；MALL-01 `$`/10 字段；MALL-03 +44；MALL-07 发货阶梯；MALL-09 首次出现分类号；MALL-10 总价 `vCurPrice`；MALL-13 生产脚本 10/10 解析 |
| `DIVERGENT` | 0（本轮已修 2） | MAIL-09 领取金币曾走 `m_nGold +=`（已改 `IncGold`）；MALL-14 Looks 曾在 `stdItem==null \|\| Looks==0` 时整条丢（已改 `vEffectImg` 回退） |
| `MISSING` | 1 | MALL-06 灵符发放（`0x6CC504 add [esi+0xBD8],eax`）。购买闸 fail-closed，这条目前不可达；接线 `PsYBConsumEx` 之前不许单独实现发放 |
| `INVENTED` | 0（前轮已撤） | 货币类型 1/3/4、S(300/301/302) 限购坐标、固定分类名表 |
| `BLOCKED` | 3 | 见 §6.1 / §6.2 / §6.3。原 §6.4 Looks 回退已解 |

---

## 6. BLOCKED

### 6.1 商城商品表的最终形态：必须由 PAS 引擎调 `@GetYBShopConfig`

原生不解析脚本文本。当前替身已能解生产 `YBShopScript.pas` 的 10 条（见 §8.1），
但仍无法处理：`Execute` 按日期在 `_001`/`_002` 之间切换（本生产文件两份常量**内容相同**，
所以今天无差）、`IsUsingGoodsName` 运行期过滤、分支里带表达式的赋值。

缺什么：把 `YBShopScript.pas` 接进 PasEngine，按 `sub_636D68` 的形状调 `@GetYBShopConfig`。

### 6.2 `EverydayClearLimitValue` / `GetDateNum`

生产 `ClientBuy` **每次购买尝试**的第一步都会：
`GetS(80,40) <> GetDateNum(GetNow)` → `EverydayClearLimitValue` → `SetS(80,40,today)`。
循环是 `for I:=1 to 50: SetV(91,I,0); if GetV(89,I)<0 then SetV(89,I,0)`。
生产 `GetLimitValue` 是 `Result := 0` 空桩，限购读数恒 0，所以**限购效果**目前为零；
但原生仍会写 V/S，属 §1.4 存档布局。C# 购买闸 fail-closed，这条目前也不可达。

缺什么：`GetDateNum` 的编码（日序号怎么从日期算）未反。不确定就不写存档。

### 6.3 元宝结算 `PsYBConsumEx` 的外部链路

`This_Player.PsYBConsumEx(2, 'YBShopBuy_YB', …)` 走外部元宝库。
没有它，商城购买无法忠实完成，只能 fail-closed：**不扣任何货币，也不发放任何物品**。

---

## 7. 建议的优先级

1. **6.3 元宝结算**：29 万次/周期的购买路径在 C# 上故意买不成。接线前保持 fail-closed。
2. **6.1 PAS `@GetYBShopConfig`**：替身已能解本生产文件；换脚本变体仍会再死。
3. **MALL-06 灵符**：只允许出现在结算成功之后的发货臂，禁止提前实现。
4. 邮件族：线上零流量。本轮只修领取金币的 `IncGold` 门（刷钱方向 fail-closed）。

---

## 8. 本轮独立复核（不采信前报告）

镜像 `flat_image.bin`，基址 `0x400000`。脚本在 `docs/mailmall_re/q20..q23`。

### 8.1 生产配置解析：10/10，字段逐条对照

文件 `D:\光头卧龙\mud2.0\Mir200\Envir\YBShop\YBShopScript.pas`（12001 字节，GBK）。
分隔符 `$`（`0x636F8F b1 24`），10 字段（`0x636FC6 83 f8 0a` / `ja 0x63709A`）。
分类按首次出现：装饰=0，强化=1（**不是**写死表里的强化=2）。
全部 `vLimitType=0` / `vLimitCount=0`。`GetLimitValue` 活坐标 0 条。

| # | 名 | 分类 | cid | idx | src | cur | lt | lc | img | ec |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 贵族斗笠 | 装饰 | 0 | 222 | 2000 | 2000 | 0 | 0 | 520 | 18 |
| 2 | 五倍经验卷 | 装饰 | 0 | 222 | 1000 | 1000 | 0 | 0 | 520 | 18 |
| 3 | 三倍经验卷 | 装饰 | 0 | 222 | 500 | 500 | 0 | 0 | 520 | 18 |
| 4 | 技巧项链 | 装饰 | 0 | 222 | 500 | 500 | 0 | 0 | 520 | 18 |
| 5 | 随机传送石 | 强化 | 1 | 247 | 10 | 10 | 0 | 0 | 380 | 1 |
| 6 | 盟重传送石 | 强化 | 1 | 218 | 10 | 10 | 0 | 0 | 410 | 10 |
| 7 | 修复神水 | 强化 | 1 | 248 | 10 | 10 | 0 | 0 | 380 | 1 |
| 8 | 魔血石 | 强化 | 1 | 249 | 300 | 300 | 0 | 0 | 380 | 1 |
| 9 | 太阳水包 | 强化 | 1 | 250 | 10 | 10 | 0 | 0 | 380 | 1 |
| 10 | 疗伤药包 | 强化 | 1 | 251 | 50 | 50 | 0 | 0 | 380 | 1 |

`_001` 与 `_002` 常量内容相同；`Execute` 的日期切换对本文件无差。

### 8.2 【MAIL-09 · 已修】领取金币必须走 `IncGold`

```
0070B7C0  e8 83 c1 fc ff        call 0x6D7948            ; 溢出测试
0070B7C9  be fd ff ff ff        mov esi, -3              ; 溢出 -> -3，金币/附件都不动
0070B7DB  ff 91 8c 02 00 00     call [vmt+0x28C]          ; IncGold = 0x6D791C
0070B7E1  84 c0 / 74 59         test al,al / je 0x70B83E ; false 不改成 -3，仍去发附件
006D7922  85 d2 / 7e 1d         test edx,edx / jle        ; IncGold: <=0 拒绝
006D792E  3b 98 8c 06 00 00     cmp ebx,[eax+0x68C]       ; vs m_nGoldMax
006D7934  7f 0d                 jg 0x6D7943               ; > 上限拒绝
006D7936  01 90 5c 01 00 00     add [eax+0x15C],edx       ; 才加金币
006D793C  e8 73 a0 fe ff        call 0x6C19B4             ; GoldChanged 在 IncGold 内
```

C# 曾 `m_nGold += record.MoneyCount`，绕过 `IncGold` 的 `<=0` 门和 `GoldChanged` 内联。
已改为 64 位溢出预检（比原生 32 位加法更严，拒绝方向）+ `IncGold(record.MoneyCount)`。

### 8.3 【MALL-14 · 已修】Looks 回退是 `rec+0x30` 低字 = `vEffectImg`

前报告 §6.4 标 BLOCKED。本轮反出：

```
00639DB5  e8 1a 25 11 00        call 0x74C2D4             ; 按名查标准物品
00639DC1  74 10                 je  0x639DD3              ; 未找到 -> 回退
00639DC6  66 8b 40 18           mov ax, [std+0x18]        ; Looks
00639DCD  66 89 42 20           mov [rec+0x20], ax
00639DD3  …未找到…
00639DD6  66 8b 40 30           mov ax, [rec+0x30]        ; vEffectImg 低字
00639DDD  66 89 42 20           mov [rec+0x20], ax
```

`+0x18` 与 `NativeItemFactory` 的 `StdMode +0x14 / Shape +0x15` 对齐：`string[19]` 后 Looks 在 +0x18。
C# 曾 `if (stdItem == null || stdItem.Looks == 0) continue;` 把原生仍会下发的记录丢掉。
已改为：命中用 `stdItem.Looks`，未命中用 `item.EffectImg`，`Looks==0` 照发。
这是列表渲染（只读），购买仍在结算闸 fail-closed。

### 8.4 【MAIL-10 · FAITHFUL】谁能发、附件上限、金币上限

客户端协议**没有**发信 ident。`NewFullMailEx` 全镜像仅 3 个调用点：
`0x649118`（全局 PAS）、`0x6E759C`（`This_Player` PAS）、`0x708CD0`（7 参包装）。
玩家不能从客户端写信。tag 7「用户邮件」有名字但 `dword_7D3DE8` bit 为 0，领取/清理都拒。

附件格数：

```
0070907C  b2 2f                 mov dl, 0x2F              ; '/' 切组
00709301  e8 42 fd ff ff        call 0x709048             ; TStringList.Count
00709306  83 f8 06              cmp eax, 6
00709309  7e 18                 jle 0x709323              ; <=6 继续
00709314  ba 58 95 70 00        mov edx, 0x709558          ; 长串 len=35
```

`0x709558` 实读 `'[Error] 不能发送超过6个附件的邮件！'`。`>6` 整封不写。
C# `TryParseItemInfo`：`groups.Length > 6` 返回 false。

金币上限：**发送侧没有**。`sub_70CF34` 是 `mov [mail+0x54], ecx`（原样写入 `moneyCount`），
`test ecx,ecx / jle` 仅在 `>0` 时把 `AttachStatus` 置 1。脚本可以塞任意 int。
不发明上限。领取侧才走 `IncGold` 的 `m_nGoldMax` 门。

条数上限在**清理**不在发送：普通 tag 30 封 / tag 6 为 20 封（`0x70D114 mov [ebp-4],0x1E`）。
发送可暂时超过，下次清理裁。

### 8.5 【MAIL-11 · FAITHFUL】落盘布局

```
0070C844  INSERT INTO %s.mailitem(sendId,sendName,recvName,recvid,title,context,
          mailType,mailstatus,attachstatus,moneytype,moneyCount,attachNum,createDate)
          VALUES(%d,%s,%s,%d,%s,%s,%d,%d,%d,%d,%d,%d,%s);
0070BEAC  INSERT INTO %s.attachitem(mailid,CreateDate) VALUES(%d,%s);
0070C334  INSERT INTO %s.Money_order(...,moneyStatus,createDate) VALUES(...,%d,Now());
0070AD7C  INSERT INTO %s.mailitem_b(...) SELECT ... FROM mailitem  ; 归档
```

schema 名 `gamedata`（`0x70C814` len=8）。C# `NativeMailStore.NativeSchemaStatements` 同构。
无事务：`MailService` 注释已写明后步失败时已写入的行保留，与原生一致。

### 8.6 领取原子性

- 背包门（`0x70B6AF cmp edi,eax / jg`）在**任何**副作用之前：附件数 > 空格 → 返回 -1，
  金币未加、`AttachStatus` 未改。不存在「发了一半」。
- 金币溢出（`0x70B7C0`）在 `IncGold` 之前：返回 -3 时金币未动、附件未发、邮件仍可领。
- 发放循环之后**无条件** `AttachStatus := 2`（`0x70B5E3`）：单件 `AddItemToBag` 失败则丢那一件
  （损失窗口），绝不重复发放。
- 元宝分支异步：`yb_user_data` 已提交而 `attachStatus` 仍为 1 时可重领。原生如此，不修。
- 中断「扣了邮件没给物」：溢出/背包门都在标记之前，**不会**。反向「给了物没扣邮件」被
  无条件 `AttachStatus:=2` 关掉（用损失窗口换一次领取）。

### 8.7 购买流程原子性

生产 `ClientBuy`：日志 → `PsYBConsumEx` 异步扣元宝 → 回调 `YBShopBuy_YB` 才 `Give`。
引擎 `sub_6CB7E4` / `sub_6CC420` **零条减法**。C# `TrySettleYuanbaoPayment` 恒 false，
且位于建物品 / 入包 / 写限购之前。两个方向都闭：

- 不会扣钱不给物（本进程不扣款）
- 不会给物不扣钱（入包不可达）

价格取 `vCurPrice`（脚本 `Price := WantNum * vCurPrice`，`0x637199 66 89 46 26` 是字段 5）。
数量 `(WantNum > 0) and (WantNum < 1000)` 硬拒绝，不夹取。
货币只有元宝。限购计数在生产是空桩，跨下线无状态可保留。

### 8.8 本轮改动

- `TPlayObject.Mail.cs`：领取金币改 `IncGold`
- `TPlayObject.Mall.cs`：Looks 回退 `vEffectImg`，不再因 Looks==0 丢行
- `MallCurrency4CompatCheck`：生产 10 条字段级断言 + IncGold/Looks/vCurPrice 源码钉
- `InProcMailRunCheck`：溢出返回 -3 且金币/AttachStatus 不动
