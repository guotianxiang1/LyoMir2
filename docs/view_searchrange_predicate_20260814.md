# SPWN-56 —— SearchViewRange 有效性谓词 `sub_765D64` 　2026-08-14

- 工作树：`D:\loym2\.claude\wt3\view56`　分支：`w/view56`　基线：`69f049b6`
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`，file_off = VA − 0x400000）
- 工具：capstone x86-32 线性反汇编 + `E8 rel32` 目标匹配普查 + Delphi 长串 `[addr-4]` 长度前缀 GBK 解码
- 铁律：所有判定附 VA + 字节 + 反汇编；无原生字节证据一律 fail-closed

> **结论**：`sub_765D64` 是**对象有效性/悬挂探针**（`CName` 非空 ∧ `PEnvir` 非空 ∧
> `PEnvir.MapName` 非空），返回 **真 = 有效**。它**不是**死亡谓词——死亡/幽灵谓词是相邻的
> 另一个函数 `sub_765D94`。C# 侧在这条循环里**根本没有对应谓词**，摘链完全靠一条移植期
> 自造的 60 秒 `dwAddTime` 时限。已把忠实谓词落地为 O(1)、零分配的独立文件，并以**并联**
> （`||`）而非替换的方式接进全部 3 份 `SearchViewRange` 拷贝。

---

## 1. `sub_765D64` 完整语义还原

函数体 47 字节（`0x765D64`–`0x765D92`，`0x765D93` 是 `90` 对齐填充）：

```
765D64  55                       push ebp
765D65  8B EC                    mov  ebp,esp
765D67  53                       push ebx
765D68  56                       push esi
765D69  8B F0                    mov  esi,eax           ; esi = Self（Delphi register 调用约定，EAX 收 Self）
765D6B  33 DB                    xor  ebx,ebx           ; Result := False
765D6D  80 BE 06 01 00 00 00     cmp  byte [esi+0x106],0
765D74  74 17                    je   0x765D8D          ; Length(CName) = 0  -> False
765D76  83 BE 28 01 00 00 00     cmp  dword [esi+0x128],0
765D7D  74 0E                    je   0x765D8D          ; PEnvir = nil       -> False
765D7F  8B 86 28 01 00 00        mov  eax,[esi+0x128]
765D85  83 78 44 00              cmp  dword [eax+0x44],0
765D89  74 02                    je   0x765D8D          ; PEnvir.MapName = '' -> False
765D8B  B3 01                    mov  bl,1              ; Result := True
765D8D  8B C3                    mov  eax,ebx
765D8F  5E                       pop  esi
765D90  5B                       pop  ebx
765D91  5D                       pop  ebp
765D92  C3                       ret
```

等价 Pascal：

```pascal
function TCreature.<Valid>: Boolean;                    // sub_765D64
begin
  Result := (Length(Self.CName) <> 0)
        and (Self.PEnvir <> nil)
        and (Pointer(Self.PEnvir.MapName) <> nil);      // AnsiString 指针 nil ⟺ 串为 ''
end;
```

**返回值语义：真（`al = 1`）= 有效。** 13 个调用点全部 `test al,al` 后按
「非零走正常臂 / 零走异常臂」分流，无一例外（见 §3）。

### 1.1 三个槽的身份（逐项独立取证，非推测）

| 槽 | 宽 | 身份 | 证据 |
|---|---|---|---|
| `+0x106` | byte | `CName` **ShortString 的长度字节** | `0x71FB31 8B 55 FC` / `0x71FB34 81 C2 06 01 00 00 add edx,0x106` / `0x71FB3A E8 … call 0x405774`；而 `0x405774` 实测为 `31 C9 xor ecx,ecx / 8A 0A mov cl,[edx] / 42 inc edx / E9 72 FE FF FF jmp 0x4055F0` —— 正是 Delphi `@LStrFromString`，`cl` 取的就是 `+0x106` 那个长度字节 |
| `+0x128` | dword | `PEnvir`（地图对象指针） | `0x77AE80 89 B3 28 01 00 00 mov [ebx+0x128],esi`，`esi` 是刚解析出的地图对象；同一段 `0x77AE5D`–`0x77AE7B` 还把 `[esi+0x44]` 拷进 `[npc+0x115]` |
| `[PEnvir+0x44]` | dword | 地图名 **AnsiString** | `0x77AE5D 8B 56 44 mov edx,[esi+0x44]` → `0x77AE65 call 0x4057AC` → `0x77AE79 B1 0F mov cl,0x0F` / `0x77AE7B call 0x4039E4`（拷 15 字节 ShortString）；另有 `0x71FEB4 8B 48 44 mov ecx,[eax+0x44]` 传给 `sub_752CAC`，后者序言 `0x752CC0 E8 FB 2C CB FF call 0x4059C0` = `@LStrAddRef` |

### 1.2 作者自己给的标签（六处独立佐证）

失败臂拼的诊断串，`[addr-4]` 长度前缀校长 + GBK 解码：

| 串 VA | len | 内容 |
|---|---|---|
| `0x77A81C` | 81 | `[Exception]: TEnvironment.DoPlayerSearchViewRange Curr^.POject.CName = 空 Curr = ` |
| `0x7777EA` 用 | — | `[Exception]: TEnvironment.AddToMap Pt.POject.CName = 空 Pt = ` |
| `0x778030` 用 | — | `[Exception]: TEnvironment.CanWalk Pt.POject.CName = 空 Pt = ` |
| `0x7788F9` 用 | — | `[Exception]: TEnvironment.GetMovObjCount Pt.POject.CName = 空 Pt = ` |
| `0x7798C0` 用 | — | `[Exception]: TEnvironment.CreatureMoveTo Curr.POject.CName = 空 Curr = ` |
| `0x77AB07` 用 | — | `[Exception]: TEnvironment.DoSearchTargetList Pt.POject.CName = 空 Pt = ` |
| `0x6DC4C8` | 64 | `[Exception]: TPlayer.SendDirectClientMsg Cret.CName = 空 Cret = ` |
| `0x6DC93C` | 55 | `[Exception]: TPlayer.SendRefMsg Cret.CName = 空 Cret = ` |
| `0x6DCD9C` | 56 | `[Exception]: TPlayer.SendRefBuff Cret.CName = 空 Cret = ` |
| `0x7656D4` | 55 | `[Exception]: TCreature.SendRefMsg Obj.CName = 空 Obj = ` |
| `0x765B1C` | 56 | `[Exception]: TCreature.SendRefBuff Obj.CName = 空 Obj = ` |

**原作者对这条谓词失败的措辞一律是「CName = 空」**，而且是当作 `[Exception]` 报的。
这是一条「这个指针指的东西已经不像个活对象了」的卫生检查，不是业务谓词。

### 1.3 它**不是**死亡谓词——死亡谓词在隔壁

紧邻的下一个函数 `sub_765D94` 才是死/幽灵/隐身那一族：

```
765D94  55 8B EC 53 56           序言
765D99  8B F0                    mov esi,eax
765D9B  B3 01                    mov bl,1
765D9D  80 7E 73 00 / 75 2A      cmp byte [esi+0x73],0  / jne 走 True   ; 幽灵
765DA3  80 BE E6 02 00 00 00     cmp byte [esi+0x2E6],0 / je  走 True
765DAC  8B C6 / E8 -> 0x772DA8   call sub_772DA8 = `8A 40 74 mov al,[eax+0x74]; C3 ret`  ; 死亡
765DB7  80 BE E3 02 00 00 00     cmp byte [esi+0x2E3],0 / jne 走 True
765DC0  8B C6 / E8 -> 0x772EB8   call sub_772EB8（读 [+0x2E2] + HasState(0x3C)）
765DCB  33 DB                    xor ebx,ebx（全不中才 False）
```

`m_boDeath` 对应的是 `[+0x74]`（经 `sub_772DA8`），**只出现在 `sub_765D94` 里，`sub_765D64`
里一个字节都没有**。两个函数只差 0x30 字节，容易看混——这正是旧账本把 SPWN-56 记成
「C# 用 `m_boDeath`」的来源。

### 1.4 这三项在原生里什么时候会真的为假

全镜像扫描（capstone 线性反汇编 + 操作数 disp 匹配）：

| 检查 | 结果 |
|---|---|
| `mov byte [reg+0x106], imm`（清 CName 长度字节） | **0 命中** |
| `mov dword [reg+0x128], <reg/imm>` | 25 命中，源全是刚算出的地图指针，**无一写常量 0** |

所以原生也**从不主动**清这三个槽。三项唯一可能为假的时机是：

1. 对象刚 `Create`、还没命名/还没入图（Delphi `InitInstance` 把实例整块清零，长度字节自然是 0）；
2. 对象已被 `Free`，那块内存的内容已不再像个合法对象。

即：**这是一条「格子链上挂了个不是活体的东西」的探针**。它在正常运行期恒为真，只有链表
出现悬挂/半构造项时才会掉下来。这一点决定了移植策略（见 §5）。

---

## 2. 调用点上下文：`sub_77A178` = `TEnvironment.DoPlayerSearchViewRange`

函数起点 `0x77A178`（`55 8B EC` + 0x11 组 `push 0/push 0` 清栈 + 双层 SEH），
签名从栈帧读出为 `(Self=EAX, Player=EDX, [ebp+8]=nowTick, [ebp+0xC]=seeZone, [ebp+0x10]=paramY)`
——由 `0x77A354/0x77A367/0x77A37A` 三段把 `[ebp+0xC]`/`[ebp+0x10]` 分别贴上
`" paramX = "` / `" paramY = "` / `" paramSeeZone = "` 标签坐实。

### 2.1 循环骨架

```
0x77A1C2-0x77A23C  按 seeZone 夹取 X/Y 扫描窗（下限 1、上限 [Self+0x3C]-1 / [Self+0x40]-1）
0x77A243  E8 2C A7 FF FF   call 0x774974            ; 预分配一个复用记录 -> [ebp-0x30]
0x77A261  X 外层循环头
0x77A26D  lea eax,[eax+eax*2] / 0x77A276 lea eax,[edx+eax*4]
                                                    ; cell = Envir[+0x38] + (y*W + x)*12
0x77A291  Y 内层循环头
0x77A294  8B 40 08         eax := cell[+8]          ; 链表头
0x77A297  89 06            [esi] := eax             ; Curr
0x77A29B  89 45 DC         [ebp-0x24] := 0          ; prev := nil
0x77A29E  83 3E 00 / 0F 84 88 04 00 00  空链 -> 0x77A72F（下一格）
0x77A2A7  节点循环头
0x77A2A9  8B 40 0C         [ebp-0x2C] := Curr^.Next ; 先存 next（因为可能摘链）
0x77A2AF  33 DB            bl := 0                  ; 「本轮已摘链」标志
0x77A2B3  0F B6 00         al := byte [Curr]        ; CellType
0x77A2BD  FE C8 / 74 15    1 -> 0x77A2D6            ; OS_MOVINGOBJECT
0x77A2C1  FE C8 / 0F 84 …  2 -> 0x77A3D9            ; 地面物
0x77A2C9  FE C8 / 0F 84 …  3 -> 0x77A480            ; 事件
0x77A2D1  E9 84 03 00 00   其它 -> 0x77A65A
```

尾部推进（`0x77A718`）：

```
77A718  84 DB                test bl,bl
77A71A  75 05                jne 0x77A721            ; 摘过链 -> prev 不前进
77A71C  8B 06 / 89 45 DC     prev := Curr
77A721  8B 45 D4 / 89 06     Curr := saved_next
77A726  83 3E 00 / 0F 85 …   非空 -> 回 0x77A2A7
77A72F  add [ebp-0x20],0xC / inc [ebp-0x14] / dec [ebp-0x4C] / jne 0x77A291   ; Y++
77A73F  inc [ebp-0x10] / dec [ebp-0x48] / jne 0x77A261                        ; X++
```

### 2.2 类型 1 臂 —— 本条

```
77A2D6  8B 06 / 8B 40 04      eax := Curr^.POject          ; node+4
77A2DB  89 45 CC              [ebp-0x34] := eax
77A2DE  83 7D CC 00
77A2E2  0F 84 30 04 00 00     je  0x77A718                 ; POject = nil -> 纯 continue，不摘链
77A2E8  8B 45 CC              eax := POject
77A2EB  E8 74 BA FE FF        call 0x765D64                ; ★ 有效性谓词
77A2F0  84 C0                 test al,al
77A2F2  0F 85 B8 00 00 00     jne 0x77A3B0                 ; 有效 -> 正常可见性处理
; ---- 无效臂 ----
77A2F8  83 7D DC 00 / 74 0B   prev = nil ?
77A2FE  8B 45 DC / 8B 55 D4 / 89 50 0C   prev^.Next := saved_next
77A307  EB 09                 jmp 0x77A312
77A309  8B 45 E0 / 8B 55 D4 / 89 50 08   cell^.head := saved_next
77A312  B3 01                 bl := 1                      ; 抑制 prev 前进
77A314-77A38A                 拼 11 段诊断串（0x77A81C/0x77A878/0x77A894/0x77A8A8/0x77A8BC）
77A390  BA 0B 00 00 00        mov edx,0xB / call 0x405890  ; LStrCatN 11 段
77A39D  A1 CC 5E 7D 00        mov eax,[0x7D5ECC]           ; 日志管理器单例
77A3A4  B1 01                 mov cl,1
77A3A6  E8 C9 3B 02 00        call 0x79DF74                ; 记异常
77A3AB  E9 68 03 00 00        jmp 0x77A718                 ; ★ continue，不是 break
```

**三点要记住：**

1. `POject = nil`（`0x77A2E2`）与 `Valid = False`（`0x77A2F2`）是**两条不同的臂**：前者只跳过
   且节点保持挂着（`bl` 仍是 0，该节点会成为 prev），后者才摘链。
2. 无效臂是 **continue**（`jmp 0x77A718`），不是 break，扫描继续。
3. 无效臂**不 Free 任何东西**——既不 `call 0x404690`（对象）也不
   `mov edx,0x10 / call 0x402FD0`（16 字节节点）。作为对照，同函数类型 2 的过期臂
   `0x77A422 call 0x404690` + `0x77A427 mov edx,0x10 / 0x77A42E call 0x402FD0`
   **两样都 Free**。这个对照坐实了类型 1 无效臂的语义是「这东西已经不归我管了」，
   而不是「回收它」。（即 SPWN-57 的 BLOCKED 注记，本轮附带确认。）

### 2.3 有效臂

```
77A3B0  8B 45 D0 / 50            push [ebp-0x30]        ; 复用记录
77A3B4  33 C9                    xor ecx,ecx            ; cl = 0（表示"这是个 actor"）
77A3B6  8B 55 CC                 edx := POject
77A3B9  8B 45 FC / 8B 38         eax := Player, edi := Player.VMT
77A3BE  FF 97 BC 01 00 00        call [VMT+0x1BC]
77A3C4  84 C0 / 0F 84 4C 03 00 00  false -> 0x77A718
77A3CC  E8 A3 A5 FF FF           call 0x774974          ; 记录被消费了，再拿一个
77A3D1  89 45 D0
77A3D4  E9 3F 03 00 00           jmp 0x77A718
```

同一个 `[VMT+0x1BC]` 在 `0x77A45E` 以 `cl=1` 被地面物臂复用。
`TPlayObject` 的该槽 = `[0x6ACA84] = 0x6E21F8`（VMT 基址 `0x6AC8C8`，由已验的
`TPlayer VMT+0xA4 = [0x6AC96C] = 0x6BFD1C` 反推）。**可见性过滤（幽灵 `+0x73`、
`+0x2E3` 等）在这个虚函数里面，不在本循环里**——见 §7 的顺带观察。

---

## 3. 全部 13 个调用点普查（交叉验证）

普查方法：全镜像 `E8 rel32` 目标匹配（非指针跟踪，不会漏）。命中恰 13 处，
按用法分成语义一致的两族。

### 族 A —— 格子链清道夫（6 处，全在 `TEnvironment`）

形状逐字节相同：`cmp byte [node],1`（CellType）→ `mov esi,[node+4]` → `test esi,esi/je`
→ `call 0x765D64` → `jne <正常臂>` → 摘链（`prev[+0xC] := next` 或 `cell[+8] := next`）+ 记异常 + continue。

| 调用点 | 所属函数（作者自述） | 有效臂 | 无效臂 |
|---|---|---|---|
| `0x7777EA` | `TEnvironment.AddToMap` | `jne 0x777898` | 摘链+日志 |
| `0x778030` | `TEnvironment.CanWalk` | `jne 0x7780DE` | 摘链+日志 |
| `0x7788F9` | `TEnvironment.GetMovObjCount` | `jne 0x7789A6` | 摘链+日志 |
| `0x7798C0` | `TEnvironment.CreatureMoveTo` | `jne 0x779998` | 摘链+日志（= 旧账本 MOVE-31） |
| `0x77A2EB` | `TEnvironment.DoPlayerSearchViewRange` | `jne 0x77A3B0` | 摘链+日志（**本条**） |
| `0x77AB07` | `TEnvironment.DoSearchTargetList` | `jne 0x77ABB5` | 摘链+日志 |

### 族 B —— 取用前的「这指针能碰吗」门（7 处）

| 调用点 | 所属函数 | 无效时做什么 |
|---|---|---|
| `0x6DC282` | `TPlayer.SendDirectClientMsg` | `jne 0x6DC2D4` 正常；否则记日志后跳过 |
| `0x6DC725` | `TPlayer.SendRefMsg` | `jne 0x6DC777` 正常；否则记日志后跳过 |
| `0x6DCB89` | `TPlayer.SendRefBuff` | `jne 0x6DCBDB` 正常；否则记日志后跳过 |
| `0x765494` | `TCreature.SendRefMsg`（`sub_76533C`） | `jne 0x7654E6` 正常；否则 `0x7654DF call 0x79DF74` 后 `jmp 0x765527` 跳过 |
| `0x7658E0` | `TCreature.SendRefBuff`（`sub_765790`） | 同上 |
| `0x6F4499` | `sub_6F43C8`（AI 选靶） | **`je 0x6F4620` 整段放弃**（无日志） |
| `0x6F4882` | `sub_6F4790`（AI 选靶） | **`je 0x6F4963` 整段放弃**（无日志） |

族 B 的两点交叉验证价值：

1. `0x6F448A cmp byte [ebx+0x2E3],0 / jne 0x6F4620` **紧挨在** `0x6F4499 call 0x765D64` 前面。
   状态位检查与有效性检查是**串联的两道独立门**，直接证明 `sub_765D64` 不管状态位。
2. `TCreature.SendRefMsg`（`0x765440`–`0x765527`）先 `call [envir.VMT+0x1C]` 刷缓存，
   再倒序遍历 `[self+0x380]`（`TList`，`0x76547F call 0x424D4C = TList.Get`）：
   **无效 → 只记日志 + 跳过，不从表里删**；而**幽灵**（`0x7654E9 cmp byte [obj+0x73],0`）
   → 才 `TList.Delete`。两条处置不同，再次说明有效性 ≠ 死亡/幽灵。

---

## 4. C# 现状与精确差异

### 4.1 先订正账本

`docs/eqv_shard25_20260814.md` 的 SPWN-56 行写「C# 视野扫描用 `m_boDeath` + 60s 计时器，
谓词与原生不等价」。这句把**两条不同的谓词**混成了一条，应拆开：

| | 原生 | C# |
|---|---|---|
| **摘链谓词** | `!Valid(POject)`（`0x77A2EB`） | `age(dwAddTime) >= 60000` |
| **可见性谓词** | `[Player.VMT+0x1BC]` 内部（`0x6E2271` 幽灵 `+0x73`、`0x6E2289` `+0x2E3`） | 循环内联 `!m_boDeath && !m_boInvisible && !m_boGhost && …` |

`m_boDeath` 属**第二行**，对应原生 `sub_765D94`/`sub_772DA8`（`[+0x74]`）那一族，
和 `sub_765D64` 无关。**SPWN-56 真正的缺口是第一行：C# 在这条循环里没有任何有效性谓词。**

还有一处细节值得记：`m_boDeath` 只出现在**基类** `TBaseObject.SearchViewRange`
（`TBaseObject.ViewRange.cs:217`）；真正跑玩家路径的 override
`TPlayObject.SearchViewRange`（`TPlayObject.Base.cs:1809`）只测 `!m_boInvisible`，**没有**
`m_boDeath`。两份拷贝本身就不一致。（不属本条范围，只登记。）

### 4.2 两者何时判定不同、可观察后果

| 场景 | 原生 | C#（改前） | 可观察差异 |
|---|---|---|---|
| 格子链上挂着一个未命名 / 未入图 / 图名为空的 actor | 立即摘链 + 记 `[Exception] … CName = 空` | 继续当活体处理，最多 60 秒后才因超时被摘 | 该对象在最长 60 秒内会被推进观察者的可见列表、收发消息；原生一进来就把它踢出格子 |
| 正常活体（有名、已入图、图名非空） | 谓词恒真，永不摘链 | 靠 `Envirnoment.VerifyMapTime` 每 <30s 刷新 `dwAddTime`（`TBaseObject.Base.cs:618`，周期门 `:583` 的 `>30*1000`），故不会被 60s 规则误伤 | 无 |
| 宿主已停止 `Run()` 超过 60 秒的孤儿格子项 | 谓词仍为真（托管对象三个槽都还在），**不摘** | 摘链 | C# 多摘。这是移植期为了替代 Delphi 手工 `Free` 而自造的 GC，**必须保留** |

三项条件在 C# 侧的可达性（全仓 grep 复核）：

- `m_sCharName`：`TBaseObject.cs:14 public string m_sCharName;` **无初值 ⇒ 默认 `null`**。
  所有已知入图路径都是先命名后 `AddToMap`（`UsrEngn.cs:3194→3274`、`:4938→5008`、
  `TPlayCloneObject.cs:14→22`），与原生同序——这正是原生探针平时不响的原因。
- `m_PEnvir`：全仓 `m_PEnvir = null` 只有 `TBaseObject.cs:322` 的字段初值一处
  （`MapPoint.cs:78/92` 是另一个类 `TPointManager` 的同名字段）。
- `Envirnoment.sMapName`：`Envirnoment.cs:20` 默认 `string.Empty`，`:94` 也置空——
  未加载的地图对象图名为空，与原生 `[map+0x44] = nil` 同构。

---

## 5. 已落地内容

### 5.1 谓词本体（新文件）

`GameSvr/Actors/TBaseObject.NativeCellObjectValidity.cs`，`partial class TBaseObject`：

- `public static bool IsNativeCellObjectValid(TBaseObject actor)` —— `sub_765D64` 的一比一移植，
  三项短路合取，注释里逐行贴了 VA + 字节 + 字段身份取证。
- `protected static bool IsNativeStaleCellActor(object cellObj)` —— 调用点侧的摘链谓词。
  对 `null` / 非 `TBaseObject` 载荷返回 `false`（不摘链），**忠实于 `0x77A2E2` 那条
  「`POject = nil` 只跳过、不摘链」的独立臂**。

热点约束：两个方法都是 O(1)、零分配（`string.IsNullOrEmpty` 只读 `Length`），
与原生的三次内存比较同量级；无 `try`、无装箱、无 LINQ、无闭包。

### 5.2 接线：并联而非替换

三份 `SearchViewRange` 拷贝的摘链条件由 `age >= 60s` 改成 `age >= 60s || IsNativeStaleCellActor(...)`：

| 文件:行 | 方法 |
|---|---|
| `GameSvr/Players/TPlayObject.Base.cs:1797` | `TPlayObject.SearchViewRange`（**主路径**，对应原生 `DoPlayerSearchViewRange`） |
| `GameSvr/Actors/TBaseObject.ViewRange.cs:206` | `TBaseObject.SearchViewRange`（怪/NPC 基类版） |
| `GameSvr/Actors/TBaseObject.ViewRange.cs:311` | `TBaseObject.SearchViewRange_Death` |
| `GameSvr/RobotPlay/RobotPlayObject.Base.cs:458` | `RobotPlayObject.SearchViewRange` |

**为什么是并联（`||`）而不是替换：**

- 原生的摘链条件是 `!Valid`；C# 的是 `age >= 60s`。**替换**会删掉 60 秒规则，而那是托管移植
  里孤儿格子项的**唯一回收通道**（原生靠 Delphi 手工 `Free` 免费拿到这个效果，见 §1.4）。
  删了它，格子链只增不减，而这条循环是每 actor 每 tick 扫 `(2R+1)²` 格 × 每格链长——净回归。
- **并联是单调的**：`Valid` 为真时行为逐位不变（仍走 60 秒）；`Valid` 为假时才新增摘链，
  而那正是原生要做的事。它**不可能**摘掉一个「有名 + 已入图 + 图名非空」的活体。
  所以并联只会**减少**分歧，不会引入新分歧。

**未复刻**：`0x77A3A6 call 0x79DF74` 那条 `[Exception] … CName = 空` 诊断日志。
原生的日志频率被摘链自然限流（每个坏节点只报一次），移植过来不难，但它是纯诊断、
无 gameplay 可观察面，且要动日志管理器的接线——留给主代理决定。

### 5.3 构建

`dotnet build GameSvr\GameSvr.csproj` → **0 错误 / 15 警告**（全部为改前既有，无一来自本轮文件）。

---

## 6. 受影响面清单（接线需求，交主代理）

已落地的 4 处都在族 A 语义之内、且是同一个原生调用点 `0x77A2EB` 的拷贝。以下是**没动**的：

| # | 原生调用点 | 原生函数 | C# 落点 | 为什么没动 |
|---|---|---|---|---|
| 1 | `0x7777EA` | `TEnvironment.AddToMap` | `Envirnoment.AddToMap`（`Envirnoment.cs:~525`） | 族 A 同构，但入图路径的摘链会影响 `AddToMap` 返回的节点身份，牵连坐标/占位语义，属另一条账 |
| 2 | `0x778030` | `TEnvironment.CanWalk` | `Envirnoment.cs:~715/768` 一族 | 走路可行性热点，摘链副作用要单独取证 |
| 3 | `0x7788F9` | `TEnvironment.GetMovObjCount` | `Envirnoment.cs:~1082/1499` 一族 | 同上 |
| 4 | `0x7798C0` | `TEnvironment.CreatureMoveTo` | `Envirnoment.MoveToMovingObject` | 旧账本 MOVE-31 已单独结论为「可观测等价、维持不改」（`docs/move_misc_residual_20260814.md`）。**该结论的前提是三项在 C# 不可达**；本轮证明 `m_sCharName` 默认为 `null` ⇒ 前提只是「实践上不可达」而非「结构上不可达」。建议按本条同样并联处理 |
| 5 | `0x77AB07` | `TEnvironment.DoSearchTargetList` | `Envirnoment.GetMapBaseObjects`（`Envirnoment.cs:1853`）一族 | 族 A 同构，未接 |
| 6 | `0x765494` / `0x7658E0` | `TCreature.SendRefMsg` / `SendRefBuff` | **`TBaseObject.cs:4086-4100`** 的 `m_VisibleHumanList` 消费循环 | 族 B，语义是**只记日志+跳过、不删表项**（`0x7654DF` → `jmp 0x765527`）。注意**不要**接到 `TBaseObject.cs:4031` 那个格子扫描上——原生的重建函数 `sub_7651EC` 根本不调 `sub_765D64`，探针在消费侧 |
| 7 | `0x6DC282` / `0x6DC725` / `0x6DCB89` | `TPlayer.SendDirectClientMsg` / `SendRefMsg` / `SendRefBuff` | `TPlayObject` 广播族 | 族 B。`TPlayObject.Message.cs` 属禁改文件，若落点在那里需主代理接 |
| 8 | `0x6F4499` / `0x6F4882` | `sub_6F43C8` / `sub_6F4790`（AI 选靶） | 未定位 | 族 B 的「整段放弃」变体（`je` 而非 `jne`），且无日志。C# 落点未定位，未接 |

**禁改文件影响**：本轮 0 触碰 `SystemModule/Grobal2.cs`、`GameSvr/Players/TPlayObject.Message.cs`、
`GameSvr/UsrSystem/UsrEngn.cs`。上表第 7 项若要落地可能需要动 `TPlayObject.Message.cs`。

---

## 7. 顺带观察（只报，不属本条，未取证到可落地程度）

原生 `[Player.VMT+0x1BC] = 0x6E21F8` 的 `cl=0`（actor）臂开头：

```
6E2267  C7 45 E8 01 00 00 00     [ebp-0x18] := 1
6E226E  8B 45 FC / 80 78 73 00   cmp byte [obj+0x73],0    ; 幽灵
6E2275  0F 85 A6 04 00 00        jne 0x6E2721             ; -> 退出
6E227B  C7 45 E8 02 00 00 00     [ebp-0x18] := 2
6E2282  C6 80 E9 02 00 00 01     mov byte [obj+0x2E9],1   ; 在目标身上置一个标志
6E2289  80 B8 E3 02 00 00 00     cmp byte [obj+0x2E3],0
6E2290  74 0D                    je 0x6E229F              ; 非零 -> xor eax,eax 返回 False
```

即原生的可见性过滤只测 `+0x73`（幽灵）与 `+0x2E3`，**开头没有 `+0x74`（`m_boDeath`）测试**，
而且它有个副作用：`mov byte [obj+0x2E9],1`。C# 把过滤内联在循环里、含 `m_boDeath`、
且无 `+0x2E9` 副作用。这属 SPWN-55 / `UpdateVisibleGay` 的地界，且 `+0x2E3` / `+0x2E9`
的字段身份本轮未取证 ⇒ **UNPROVEN，只登记，不动**。

---

## 8. 方法与可复现

- 反汇编：仓内 `tools/m2_disasm.py`（capstone x86-32，`off = VA − 0x400000`）；
  临时脚本置于 `%TEMP%`（`v56_census.py` 调用点普查 / `v56_fnstart.py` 函数入口反推 /
  `v56_sites.py` 调用点窗口 / `v56_writers.py` 字段写点扫描 / `v56_names.py` 诊断串反查函数名 /
  `v56_str.py` Delphi 长串解码），未入库。
- **调用点普查**用全镜像 `E8/E9 rel32` 目标匹配，不用指针跟踪。
- **函数入口反推**用「全镜像所有 `E8` 目标集合 ∩ [va−0x3000, va]，取最大者」，
  比 `55 8B EC` 序言回溯可靠（后者在本函数上给出 11 个假候选）。
- **函数命名**不靠猜：从调用点向后扫第一个能解成 Delphi 长串（`[addr-4]` 长度前缀 +
  `[addr-8]` refcount = −1）的 `push imm32`，串里就是作者写的函数名。13 个站点里 11 个命中。
- Delphi 串一律 `[addr-4]` 校长度 + GBK 解码。
