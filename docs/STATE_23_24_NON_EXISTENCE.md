# STATE-23/24 复核报告（原「不存在性」结论已撤回）

> **状态：RETRACTED（2026-08-13 复核）**
>
> 本文件原为 2026-08-10 的「不存在性穷尽报告」，结论为 STATE-23 与 STATE-24
> 在战神引擎中不存在。**该结论是错误的**：两者都存在，且 C# 侧已经实现。
> 原报告引用的关键字节（`0x7732DA` 处的 `DD 98 A4 03 00 00`）在镜像中不存在，
> 相关反汇编片段无法复现。本文件已整体重写为逐字节复核结果。
>
> 复核镜像：`flat_image.bin`（16.8 MB 平坦镜像，ImageBase = 0x400000）
> 复核工具：capstone 5.0.7 线性反汇编 + 原始字节模式穷尽搜索

---

## 结论对照

| 条目 | 原结论 | 复核结论 | 判定依据 |
|---|---|---|---|
| STATE-23 | 不存在，bit 0x10 从未被检查 | **存在**，且恰好阻止 8 个状态 | `0x773C51`、`0x76B466` |
| STATE-24 | 幽灵字段，零读取 | **存在**，读取点驱动石化免疫窗口 | `0x773C82` 读、`0x77330E` 写 |

两项在 C# 侧均已实现（`IsBlockedByNativeState16`、`IsNativeState26DeadlineActive`），
**不得按原报告的建议删除**。

---

## 一、位集访问的全局事实（决定性前提）

整个 16.8 MB 镜像中，触及 `obj+0x168` 位集的指令**只有三条**，且全部使用
**寄存器**作为位索引：

```
0x772968  0F A3 90 68 01 00 00   bt  dword ptr [eax+0x168], edx   ; 读 (sub_772960)
0x77299B  0F AB 9E 68 01 00 00   bts dword ptr [esi+0x168], ebx   ; 置位 (sub_772974)
0x7729B9  0F B3 9E 68 01 00 00   btr dword ptr [esi+0x168], ebx   ; 清位 (sub_7729A8)
```

立即数形式（`0F BA /4../7`，即 `bt/bts/btr/btc [reg+0x168], imm8`）在全镜像的
命中数为 **0**——对 0..111 的**任意**状态号都为 0。

读取器 `sub_772960` 全文：

```
0x772960  80 FA 6F              cmp dl, 0x6F        ; 状态号上界 111
0x772963  77 0A                 ja  0x77296F
0x772965  83 E2 7F              and edx, 0x7F
0x772968  0F A3 90 68 01 00 00  bt  dword ptr [eax+0x168], edx
0x77296F  0F 92 C0              setb al
0x772972  C3                    ret
```

**原报告方法学失效的根因**：报告搜索 `bt [reg+0x168], 0x10` 的立即数编码并得到
零命中，据此推断 bit 0x10 未被检查。但引擎把 100% 的位集访问都收敛到上述三个
以寄存器传参的访问器，立即数形式对任何状态号都必然零命中。该搜索**在结构上
不可能命中**，其零结果不构成任何证据。

原报告第 51 行「`bt [reg+0x168], immediate` 形式：仅发现 bit 0x00-0x0F 和
0x1E-0x35 的检查」所描述的命中**并不存在**——该编码形式在镜像中一处都没有。

---

## 二、STATE-23：state 0x10 确实阻止 8 个传入状态

state 0x10（16）通过**两条**独立路径参与传入状态的否决，二者都在
apply gate `sub_772F84` 的调用链上。

### 路径 A —— 值闸（阻止状态 45、53）

`sub_772F84` 在 `0x772FA1` 调用 `sub_76B460`：

```
0x76B460  55                    push ebp
0x76B463  53                    push ebx
0x76B464  8B D8                 mov ebx, eax
0x76B466  B2 10                 mov dl, 0x10        ; *** state 0x10 ***
0x76B468  8B C3                 mov eax, ebx
0x76B46A  E8 7D 87 00 00        call 0x773BEC       ; GetTimedAbilityValue(0x10)
0x76B46F  83 F8 05              cmp eax, 5
0x76B472  0F 9D C0              setge al            ; 返回 value >= 5
0x76B477  C3                    ret
```

调用点消费其结果：

```
0x772FA1  E8 BA 84 FF FF        call 0x76B460
0x772FA6  84 C0                 test al, al
0x772FA8  74 0A                 je   0x772FB4       ; 为假则跳过本闸
0x772FAA  8B C3                 mov  eax, ebx       ; ebx = 传入状态号
0x772FAC  2C 2D                 sub  al, 0x2D
0x772FAE  74 15                 je   0x772FC5       ; == 45 -> 拒绝
0x772FB0  2C 08                 sub  al, 8
0x772FB2  74 11                 je   0x772FC5       ; == 53 -> 拒绝
```

### 路径 B —— 免疫表（阻止 8 个状态）

`sub_772F84` 在 `0x772FB8` 调用 `sub_773C44`（ImmuneCheck），其第一段：

```
0x773C51  B2 10                 mov dl, 0x10        ; *** state 0x10 ***
0x773C55  E8 06 ED FF FF        call 0x772960       ; HasNativeActiveState(0x10)
0x773C5A  84 C0                 test al, al
0x773C5C  74 12                 je  0x773C70
0x773C5E  8A 55 FF              mov dl, byte ptr [ebp-1]   ; 传入状态号
0x773C63  E8 C4 E9 FF FF        call 0x77262C       ; 可阻止表
0x773C68  84 C0                 test al, al
0x773C6A  74 04                 je  0x773C70
0x773C6C  B3 01                 mov bl, 1           ; 免疫 -> 拒绝
```

`sub_77262C` 的可阻止表逐字节展开：

```
0x77262C  84 D2        test dl, dl      ; == 0        -> 阻止
0x772630  80 EA 0D     sub  dl, 0x0D    ; == 13       -> 阻止
0x772635  80 EA 0B     sub  dl, 0x0B    ; == 24       -> 阻止
0x77263A  80 EA 02     sub  dl, 2       ; == 26       -> 阻止
0x77263F  80 C2 FE     add  dl, 0xFE
0x772642  80 EA 04     sub  dl, 4
0x772645  72 03        jb   0x77264A    ; 28,29,30,31 -> 阻止
0x772647  33 C0        xor  eax, eax    ; 其余放行
0x77264A  B0 01        mov  al, 1
```

集合 = **{0, 13, 24, 26, 28, 29, 30, 31}**，共 **8 个状态**——与原报告标题
「state 0x10 阻止 8 个传入状态」的假设完全一致。原报告否定的正是它自己
正确描述的机制。

**C# 现状**：`TBaseObject.NativeState26.cs` 的 `IsBlockedByNativeState16` 已
实现同一集合，`CanAddNativeTimedAbility` 已实现值闸。无需改动。

---

## 三、STATE-24：obj+0x3A4 有写有读，冷却逻辑成立

### 原报告引用字节无法复现

| 原报告主张 | 镜像实测 |
|---|---|
| `0x7732DA` = `DD 98 A4 03 00 00`（`fstp qword [eax+0x3A4]`） | 实际字节 `82 82 00 00 00 0F`，且 0x7732DA **不是指令边界** |
| `0x7732CE` = `fld ds:dbl_73E8E8` | 实际 `66 8B 8B 6C 02 00 00` = `mov cx, word ptr [ebx+0x26C]` |
| 该字段为 TDateTime（double） | 全镜像 `fstp qword [reg+0x3A4]` 命中 **0**；任意 x87 指令（D8–DF）引用 +0x3A4 命中 **0** |
| xrefs 仅 1 处 | 原始 dword `A4 03 00 00` 出现 14 次，其中 10 处可解码为指令操作数 |
| 写入点后紧接栈清理 + `retn` | `0x773314  EB 4B  jmp 0x773361` |

0x7732DA 落在一条 6 字节指令内部：

```
0x7732D5  66 83 F9 7D           cmp cx, 0x7D
0x7732D9  0F 82 82 00 00 00     jb  0x773361     <-- 0x7732DA 在此指令内部
0x7732DF  0F B7 C1              movzx eax, cx
```

### 真实写入点：0x77330E（32 位整数）

```
0x7732CE  66 8B 8B 6C 02 00 00  mov  cx, word ptr [ebx+0x26C]   ; 抗性
0x7732D5  66 83 F9 7D           cmp  cx, 0x7D                   ; < 125 则不设
0x7732D9  0F 82 82 00 00 00     jb   0x773361
0x7732DF  0F B7 C1              movzx eax, cx
0x7732E2  83 E8 7D              sub  eax, 0x7D                  ; -125
0x7732E5  B9 19 00 00 00        mov  ecx, 0x19                  ; /25
0x7732EB  F7 F9                 idiv ecx
0x7732EF  83 C7 04              add  edi, 4                     ; +4
0x7732F2  83 FF 0A              cmp  edi, 0x0A                  ; 上限 10
0x7732F5  7E 0A                 jle  0x773301
0x7732F7  0F B7 BB AA 01 00 00  movzx edi, word ptr [ebx+0x1AA]
0x7732FE  83 C7 0A              add  edi, 0x0A
0x773301  E8 3A 50 C9 FF        call 0x408340                   ; GetTickCount
0x773306  69 D7 E8 03 00 00     imul edx, edi, 0x3E8            ; 秒 -> 毫秒
0x77330C  03 C2                 add  eax, edx
0x77330E  89 83 A4 03 00 00     mov  dword ptr [ebx+0x3A4], eax ; *** 写入 ***
```

全镜像共 10 处可解码的 `+0x3A4` 引用。落在状态子系统地址区间
（0x772900–0x773D00）内的**恰好两处**：`0x77330E` 写、`0x773C82` 读。
其余 8 处（0x74B930、0x74BE4F、0x7551CD、0x755230/44/58、0x7559F9、0x755A14）
不在本子系统范围内，未作归属判定。

### 真实读取点：0x773C82（ImmuneCheck 第二段）

```
0x773C70  B2 12                 mov  dl, 0x12                   ; state 18
0x773C74  E8 E7 EC FF FF        call 0x772960
0x773C7B  75 0D                 jne  0x773C8A                   ; 有 18 则跳过时限比较
0x773C7D  E8 BE 46 C9 FF        call 0x408340                   ; GetTickCount
0x773C82  3B 86 A4 03 00 00     cmp  eax, dword ptr [esi+0x3A4] ; *** 读取 ***
0x773C88  73 08                 jae  0x773C92                   ; 已过期 -> 不免疫
0x773C8A  80 7D FF 1A           cmp  byte ptr [ebp-1], 0x1A     ; 传入状态 == 26 ?
0x773C8E  75 02                 jne  0x773C92
0x773C90  B3 01                 mov  bl, 1                      ; 免疫 -> 拒绝
```

原报告称 `cmp reg, [reg+0x3A4]` 零命中，并把该模式列为已搜索形式；
`0x773C82` 正是该模式（modrm `86` = esi 基址）。

由于 `0x773C7B` 的 `jne` 绕过时限比较，该段的判定式为：

```
免疫 = (传入状态 == 26) AND (state 18 存在 OR now < obj[0x3A4])
```

注意是 **OR**，不是 AND-NOT。本次复核据此修正了 C# 的 `IsImmuneToTimedAbility`。

**C# 现状**：`m_dwNativeState26Deadline` 对应 `obj+0x3A4`，写入逻辑见
`ApplyNativeTimedAbilityMutation`（与 0x7732CE–0x77330E 逐项吻合：抗性字段
`obj+0x26C`、阈值 125、除数 25、加 4、上限 10、加成字段 `obj+0x1AA`、乘 1000），
读取逻辑见 `IsNativeState26DeadlineActive`。均已实现，**不得删除**。

---

## 四、对 C# 复刻的影响（修正后）

| 条目 | 原报告建议 | 修正后建议 |
|---|---|---|
| STATE-23 | 标记 NOT_FOUND，无需实现 | 已实现且正确，保留 |
| STATE-24 | 保留写入但**不得加**读取/冷却检查 | 读取即冷却检查，**必须保留**；照原建议删除会造成背离 |

---

## 五、复现方法

```
python tools/m2_disasm.py 0x772F84    # apply gate 三条否决路径
python tools/m2_disasm.py 0x773C44    # ImmuneCheck
python tools/m2_disasm.py 0x76B460    # state 0x10 值闸
python tools/m2_disasm.py 0x77262C    # 可阻止表
python tools/m2_statescan.py          # 位集 / 0x3A4 全镜像穷尽扫描
```

镜像路径默认取 `D:/loym2/staging/_reunpack_work/flat_image.bin`，
可用环境变量 `M2_FLAT_IMAGE` 覆盖。

置信度：Tier-1（逐字节，全部结论均可由上述命令复现）。
