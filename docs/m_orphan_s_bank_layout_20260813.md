# 原生 S / V 变量银行的内部布局（含裸偏移 → `S(group,index)` 换算表）

> 证据源：`D:/loym2/staging/_reunpack_work/flat_image.bin`（M2Server，ImageBase `0x400000`）、
> `D:/loym2/staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin`
> （眼神 2.0.8 脱壳内存转储，基址 `0x10000000`）。
> 复现脚本全部在 `D:/loym2/staging/_sbank/`（`q01`…`q19`，capstone，只读）。
>
> **本文所有结论都带 VA + 字节 + 反汇编。没有一条是推测。**
> 关键的换算公式不是从「应该是这样」推出来的，而是被眼神自己内嵌在桩体里的
> **13 个期望键常量**逐一验证过（§7，13/13 命中，0 失配）。

---

## 1. 一句话结论

`TPlayer` 的 `+0x804` 是一个 **Delphi 动态数组** `TScriptTagArr`，元素是 8 字节
`record Key, Value: Integer end`，**按 Key 升序排序**，`Key = group*1000 + index`。
数组**没有头部字段**：长度是 Delphi 动态数组自带的 `[ptr-4]`，条目从 `[ptr+0]` 开始。

因此：

```
S(1, i)  的 Key   在  bank + (i-1)*8
S(1, i)  的 Value 在  bank + (i-1)*8 + 4
```

反过来，裸偏移 `N`：`slot = N div 8`，`Key = 1001 + slot`，即 `S(1, slot+1)`；
`N mod 8 == 0` 取到的是 Key，`== 4` 取到的是 Value。

这个换算之所以成立，是因为眼神在角色首次加载时把 `S(1,1..150)` **全部灌满**，
使键 1001..1150 连续占据槽位 0..149（§6）。桩体自己也在校验这个前提（§7）。

---

## 2. 容器类型：`TScriptTagArr`，元素 8 字节（RTTI 实证）

`sub_6E4270`（查找）和 `sub_6E4140`（写入）都先用 `sub_406A88` 取长度：

```
0x406A88  85 C0        test eax, eax
0x406A8A  74 03        je   0x406A8F
0x406A8C  8B 40 FC     mov  eax, [eax-4]      ; Delphi @DynArrayLength
0x406A8F  C3           ret
```

`[ptr-4]` = 长度 —— 这就是 Delphi 动态数组。**银行内没有自定义头部**，
第 0 个条目就在 `[ptr+0]`。

`SetLength` 用的 TypeInfo 在 `[0x78D908]`：

```
0x6E416F  8B 15 08 D9 78 00    mov edx, [0x78D908]     ; sub_6E4140 空数组分支
0x6E41ED  8B 15 08 D9 78 00    mov edx, [0x78D908]     ; sub_6E4140 插入分支
0x6E4586  8B 15 08 D9 78 00    mov edx, [0x78D908]     ; 存档解码 type0
0x6E463A  8B 15 08 D9 78 00    mov edx, [0x78D908]     ; 存档解码 type1
```

`[0x78D908] = 0x0078D90C`，解出的 `TTypeInfo`：

```
0x78D90C  11 0D 54 53 63 72 69 70 74 54 61 67 41 72 72 08 00 00 00 ...
          ^kind ^len<---- "TScriptTagArr" ---->  ^elSize = 8
```

| 字段 | 值 |
|---|---|
| Kind | `0x11` = 17 = **tkDynArray** |
| Name | `TScriptTagArr`（13 字符） |
| **elSize** | **8** |
| elType | `0x00000000`（无托管字段 → 纯 `record K,V: Integer`） |
| varType | -1 |

`[esi+eax*8]` 取 Key、`[esi+eax*8+4]` 取 Value（`0x6E42A2` / `0x6E42A7`），
与 elSize=8 完全自洽。

## 2.1 字段属于哪个类：`TPlayer`（VMT `0x6AC8C8`）

`vmtInitTable`（VMT-64）指向 `0x6ACB60`，那是这个类的托管字段表
（`Kind=0x0E` tkRecord、匿名、Count=`0x15`=21，条目从 `0x6ACB6A` 起，每条 8 字节
`{PPTypeInfo, Offset}`）：

```
0x6ACB7A  E8 10 40 00  00 08 00 00     -> String,        offset 0x800
0x6ACB82  08 D9 78 00  04 08 00 00     -> TScriptTagArr, offset 0x804   <<< S 银行
0x6ACB8A  08 D9 78 00  08 08 00 00     -> TScriptTagArr, offset 0x808   <<< V 银行
0x6ACB92  E8 10 40 00  B4 09 00 00     -> String,        offset 0x9B4
```

VMT 负槽位：

| 槽 | VA | 值 |
|---|---|---|
| vmtSelfPtr (-76) | `0x6AC87C` | `0x006AC8C8` |
| vmtInitTable (-64) | `0x6AC888` | `0x006ACB60` |
| vmtClassName (-44) | `0x6AC89C` | `0x006ACC72` → ShortString **`TPlayer`** |
| vmtInstanceSize (-40) | `0x6AC8A0` | `0x1948` = 6472 |
| vmtParent (-36) | `0x6AC8A4` | → VMT `0x73BC34` = `THumanKind` |

**这同时解释了眼神桩体里那句 `cmp [ebp+0x18], 0x6AC8C8`** —— 它在判「对象是不是 TPlayer」。

## 2.2 相邻布局

```
+0x800            String
+0x804            TScriptTagArr   S 银行（动态数组指针）
+0x808            TScriptTagArr   V 银行（动态数组指针）
+0x80C .. +0x99B  Integer × 100   V(0, 1..100) 内联槽
```

内联槽的证据是 GetV/SetV 的 group==0 快路：

```
GetV 0x6DF20F  8B 84 83 08 08 00 00   mov eax, [ebx + eax*4 + 0x808]
SetV 0x6DF2A8  89 84 B3 08 08 00 00   mov [ebx + esi*4 + 0x808], eax
```

`index` 取 1..100，所以实际地址是 `+0x80C .. +0x998`，即 Delphi 的
`array[1..100] of Integer` 起始于 `+0x80C`（编译器把基址算成 `+0x808 = 0x80C - 1*4`）。
**S 没有这个内联区**（`GetS`/`SetS` 直接拒绝 group ≤ 0）。

---

## 3. 键合成：`sub_6E42CC` = `group*1000 + index`

```
0x6E42CC  69 C2 E8 03 00 00    imul eax, edx, 0x3E8
0x6E42D2  03 C1                add  eax, ecx
0x6E42D4  C3                   ret
```

Delphi 寄存器约定：`eax = Self`、`edx = 参数1`、`ecx = 参数2`。
所以 `Key = 参数1 * 1000 + 参数2`。

**哪个是 group、哪个是 index，由眼神侧的字节钉死**（不是照搬假设）。
眼神 SetS 包装器 `0x10065F40` 做 MSVC __fastcall → Delphi 寄存器的搬运：

```
0x10065F49  89 55 FC     mov [ebp-4], edx      ; MSVC 参数2
0x10065F4C  89 4D F8     mov [ebp-8], ecx      ; MSVC 参数1 = player
0x10065F57  8B 55 FC     mov edx, [ebp-4]      ; Delphi edx = MSVC 参数2
0x10065F5A  8B 4D 08     mov ecx, [ebp+8]      ; Delphi ecx = MSVC 参数3
0x10065F5D  8B 45 F8     mov eax, [ebp-8]      ; Delphi eax = Self
0x10065F60  FF 75 0C     push [ebp+0xC]        ; value
0x10065F63  FF 55 F4     call [ebp-0xC]        ; = 0x6DF240 SetS
```

三个常量调用现场（与随包文档的 `SetS(1,110)` / `SetS(6,1)` / `SetS(6,2)` 对得上）：

```
0x100697F6  6A 00        push 0        ; value
0x100697F8  6A 6E        push 0x6E     ; 参数2 = 110
0x100697FA  BA 01000000  mov edx, 1    ; 参数1 = 1        -> SetS(1,110)

0x1006983D  6A 01        push 1        ; 参数2 = 1
0x1006983F  BA 06000000  mov edx, 6    ; 参数1 = 6        -> SetS(6,1)

0x1009010E  6A 02        push 2        ; 参数2 = 2
0x10090107  BA 06000000  mov edx, 6    ; 参数1 = 6        -> SetS(6,2)
```

⇒ **参数1(edx) = group，参数2(ecx) = index，`Key = group*1000 + index`。**

推论：`group ≥ 1 且 index ≥ 1` ⇒ **最小可能的 Key 是 1001**。

> ⚠ index 没有上界。`SetS(1,1500)` 与 `SetS(2,500)` 都合成 Key 2500 —— 键空间会撞车。
> 这是原版行为，C# 侧不要「修」它。

---

## 4. 查找：`sub_6E4270`（二分，升序，命中取 +4）

签名：`eax = Self（未使用）`、`edx = 数组指针`、`ecx = Key`。返回 Value，miss 返回 -1。

```
0x6E4270  55                   push ebp
0x6E4271  8B EC                mov  ebp, esp
0x6E4273  51 56 57             push ecx / esi / edi
0x6E4276  8B F9                mov  edi, ecx            ; edi = Key
0x6E4278  8B F2                mov  esi, edx            ; esi = 数组基址
0x6E427A  C7 45 FC FF FF FF FF mov  [ebp-4], -1         ; 结果种子 = -1
0x6E4281  8B C6                mov  eax, esi
0x6E4283  E8 00 28 D2 FF       call 0x406A88            ; n = Length(arr)
0x6E4288  85 C0                test eax, eax
0x6E428A  74 35                je   0x6E42C1            ; 空数组 -> -1
0x6E428C  33 D2                xor  edx, edx            ; lo = 0
0x6E428E  8B C8                mov  ecx, eax
0x6E4290  49                   dec  ecx                 ; hi = n-1
0x6E4291  3B CA                cmp  ecx, edx
0x6E4293  7C 2C                jl   0x6E42C1
0x6E4295  8B C1                mov  eax, ecx            ; --- 循环头 ---
0x6E4297  2B C2                sub  eax, edx
0x6E4299  D1 F8                sar  eax, 1
0x6E429B  79 03                jns  0x6E42A0
0x6E429D  83 D0 00             adc  eax, 0              ; Delphi 有符号 div 2
0x6E42A0  03 C2                add  eax, edx            ; mid = lo + (hi-lo) div 2
0x6E42A2  3B 3C C6             cmp  edi, [esi + eax*8]  ; Key vs arr[mid].Key
0x6E42A5  75 09                jne  0x6E42B0
0x6E42A7  8B 44 C6 04          mov  eax, [esi+eax*8+4]  ; 命中 -> Value
0x6E42AB  89 45 FC             mov  [ebp-4], eax
0x6E42AE  EB 11                jmp  0x6E42C1
0x6E42B0  3B 3C C6             cmp  edi, [esi + eax*8]
0x6E42B3  7E 05                jle  0x6E42BA            ; Key <= arr[mid] -> hi = mid-1
0x6E42B5  8D 50 01             lea  edx, [eax+1]        ; 否则 lo = mid+1
0x6E42B8  EB 03                jmp  0x6E42BD
0x6E42BA  8B C8                mov  ecx, eax
0x6E42BC  49                   dec  ecx
0x6E42BD  3B CA                cmp  ecx, edx
0x6E42BF  7D D4                jge  0x6E4295
0x6E42C1  8B 45 FC             mov  eax, [ebp-4]
0x6E42C8  C3                   ret
```

要点：

- **升序排序**（`jle` → 取左半，**有符号**比较）。
- **miss 返回 -1**，不是 0。
- 全镜像只有两个调用者：`0x6DF1D7`（GetS）与 `0x6DF22D`（GetV 键控分支）。

## 4.1 `GetS` = `sub_6DF1B4`

```
0x6DF1B9  8B D8                mov  ebx, eax             ; Self
0x6DF1BB  83 CE FF             or   esi, -1              ; 结果 = -1
0x6DF1BE  85 C9                test ecx, ecx             ; index
0x6DF1C0  7E 1C                jle  0x6DF1DE             ; <=0 -> 返回 -1
0x6DF1C2  85 D2                test edx, edx             ; group
0x6DF1C4  7E 18                jle  0x6DF1DE             ; <=0 -> 返回 -1
0x6DF1C6  8B C3                mov  eax, ebx
0x6DF1C8  E8 FF 50 00 00       call 0x6E42CC             ; Key = group*1000+index
0x6DF1CD  8B C8                mov  ecx, eax
0x6DF1CF  8B 93 04 08 00 00    mov  edx, [ebx + 0x804]   ; <<< S 银行
0x6DF1D5  8B C3                mov  eax, ebx
0x6DF1D7  E8 94 50 00 00       call 0x6E4270
0x6DF1DC  8B F0                mov  esi, eax
```

## 4.2 `SetS` = `sub_6DF240`

```
0x6DF24F  33 C0                xor  eax, eax             ; 结果 = False
0x6DF251  85 FF                test edi, edi             ; index <=0 -> False
0x6DF255  85 F6                test esi, esi             ; group <=0 -> False
0x6DF25F  E8 68 50 00 00       call 0x6E42CC             ; Key
0x6DF264  89 45 F8             mov  [ebp-8], eax         ; 局部 record.Key
0x6DF267  8B 45 08             mov  eax, [ebp+8]         ; value（栈参）
0x6DF26A  89 45 FC             mov  [ebp-4], eax         ; 局部 record.Value
0x6DF26D  8D 93 04 08 00 00    lea  edx, [ebx + 0x804]   ; @Self.FSBank（字段地址，不是值）
0x6DF273  8D 4D F8             lea  ecx, [ebp-8]         ; @局部 record
0x6DF276  8B C3                mov  eax, ebx
0x6DF278  E8 C3 4E 00 00       call 0x6E4140
0x6DF283  C2 04 00             ret  4
```

局部 record 的布局 `[ebp-8]=Key, [ebp-4]=Value` 再次确认 **Key 在前、Value 在后**。

V 侧完全对称：`GetV 0x6DF1E4`（`[ebx+0x808]`）、`SetV 0x6DF288`（`lea edx,[ebx+0x808]`）。

---

## 5. 写入：`sub_6E4140`（二分 + 有序插入 / 原地更新）

签名：`eax = Self`、`edx = @数组字段`、`ecx = @{Key,Value}`。恒返回 True。

```
0x6E4149  8B F1                mov esi, ecx
0x6E414B  8D 7D F8             lea edi, [ebp-8]
0x6E414E  A5 A5                movsd; movsd          ; 拷 8 字节到 [ebp-8]/[ebp-4]
0x6E4150  8B F2                mov esi, edx          ; esi = @数组字段
0x6E4152  C6 45 F7 01          mov byte [ebp-9], 1   ; 结果恒 True
0x6E4156  8B 06 / E8 …6A88     n = Length(arr)
0x6E4164  7F 29                jg  0x6E418F
   ; ---- 空数组 ----
0x6E4166  6A 01                push 1                ; SetLength(arr, 1)
0x6E4175  E8 EA 2A D2 FF       call 0x406C64
0x6E4182  89 10                mov [eax], edx        ; arr[0].Key
0x6E4187  89 50 04             mov [eax+4], edx      ; arr[0].Value
   ; ---- 非空：二分 ----
0x6E41B3  8B 0E / 8B 0C D9     mov ecx, [arr + mid*8]
0x6E41B8  3B 4D F8             cmp ecx, [ebp-8]
0x6E41BB  75 0E                jne 0x6E41CB
0x6E41C2  89 54 D8 04          mov [arr + mid*8 + 4], edx   ; 键已存在 -> 原地改 Value
0x6E41D0  3B 4D F8             cmp ecx, [ebp-8]
0x6E41D3  7E 05                jle 0x6E41DA                 ; arr[mid] <= new -> lo=mid+1
   ; ---- 未命中：扩容 1 格并插入，保持升序 ----
0x6E41E1  8B 45 F0 / 40 / 50   push n+1
0x6E41F3  E8 6C 2A D2 FF       call 0x406C64                ; SetLength(arr, n+1)
0x6E4200  3B 45 F8             cmp eax, [ebp-8]
0x6E4203  7D 32                jge 0x6E4237
   ;   arr[mid].Key < newKey -> 插在 mid+1
0x6E4216  8D 54 D8 10          lea edx, [arr + mid*8 + 0x10]   ; dst = &arr[mid+2]
0x6E421C  8D 44 D8 08          lea eax, [arr + mid*8 + 8]      ; src = &arr[mid+1]
0x6E4220  E8 3B F0 D1 FF       call 0x403260                   ; Move
0x6E422A  89 54 D8 08          mov [arr+mid*8+8],  Key
0x6E4231  89 54 D8 0C          mov [arr+mid*8+0xC], Value
0x6E4237: ;   否则插在 mid
0x6E4247  8D 54 D8 08          lea edx, [arr + mid*8 + 8]      ; dst = &arr[mid+1]
0x6E424D  8D 04 D8             lea eax, [arr + mid*8]          ; src = &arr[mid]
0x6E4250  E8 0B F0 D1 FF       call 0x403260                   ; Move
0x6E425A  89 14 D8             mov [arr+mid*8],   Key
0x6E4260  89 54 D8 04          mov [arr+mid*8+4], Value
```

**排序约束（硬契约）**：数组任何时刻都按 Key 升序。这是 `sub_6E4270` 二分能工作的前提，
也是裸偏移能被反解的前提。

**没有零值特例**：四个 Value 存储点（`0x6E4187` / `0x6E41C2` / `0x6E4231` / `0x6E4260`）
都原样写入，写 0 就存 0。

---

## 6. 存档 codec：整块 memcpy，落盘顺序 == 内存顺序

### 6.1 解码 `sub_6E448C`（唯一调用者 `0x6547D1`）

7 字节段头：

```
0x6E44EE  81 38 AA EF CD AB    cmp dword [p], 0xABCDEFAA   ; magic
0x6E44FC  0F B7 40 04          movzx eax, word [p+4]       ; payload 字节长度
0x6E450C  0F B6 40 06          movzx eax, byte [p+6]       ; 段类型
0x6E4510  83 F8 08             cmp eax, 8
0x6E4513  0F 87 3D 03 00 00    ja  0x6E4856                ; 只认 0..8
0x6E4519  FF 24 85 20 45 6E 00 jmp [eax*4 + 0x6E4520]
```

跳表 `0x6E4520`（原始字节
`44 45 6E 00 | F7 45 6E 00 | A9 46 6E 00 | 56 48 6E 00 | 56 48 6E 00 | 56 48 6E 00 | 15 47 6E 00 | 81 47 6E 00 | ED 47 6E 00`）：

| type | 目标 | 含义 |
|---|---|---|
| 0 | `0x6E4544` | **S 银行**（`+0x804`），名字串 `'act'` |
| 1 | `0x6E45F7` | **V 银行**（`+0x808`），名字串 `'task'` |
| 2 | `0x6E46A9` | shenYou |
| 3/4/5 | `0x6E4856` | 已废弃 → 越界臂 |
| 6 | `0x6E4715` | bodyState |
| 7 | `0x6E4781` | coldTime |
| 8 | `0x6E47ED` | FirstDoSome |

type0 臂：

```
0x6E4553  66 83 78 04 00       cmp  word [p+4], 0
0x6E4558  76 58                jbe  0x6E45B2               ; 长度 0 -> 报错
0x6E4561  F7 7D EC             idiv dword [ebp-0x14]       ; [ebp-0x14] 恒 = 8
0x6E4564  85 D2 / 75 4A        test edx,edx / jne 0x6E45B2 ; 长度必须是 8 的整数倍
0x6E456F  F7 7D EC             idiv 8                      ; count = len / 8
0x6E4578  50                   push eax                    ; SetLength(FSBank, count)
0x6E4579  8B 45 FC             mov  eax, [ebp-4]
0x6E457C  05 04 08 00 00       add  eax, 0x804             ; @Self.FSBank
0x6E4586  8B 15 08 D9 78 00    mov  edx, [0x78D908]        ; 同一个 TScriptTagArr TypeInfo
0x6E458C  E8 D3 26 D2 FF       call 0x406C64
0x6E4596  0F B7 48 04          movzx ecx, word [p+4]       ; 字节数
0x6E459D  8B 90 04 08 00 00    mov  edx, [eax + 0x804]     ; dst = 数组基址
0x6E45A5  83 C0 07             add  eax, 7                 ; src = 段头之后
0x6E45A8  E8 B3 EC D1 FF       call 0x403260               ; Move —— 原样整块拷贝
```

**解码不做任何重排**。所以磁盘上的字节序列必须已经是升序的，否则加载后二分查找失效。
type1 臂 `0x6E45F7` 完全同构，只是 `add eax, 0x808` / `shr eax,3`。

### 6.2 编码（`0x6E4CFC` 起，段发射在 `0x6E4DD3..0x6E4ECF`）

长度计算：

```
0x6E4CFE  E8 85 1D D2 FF       call 0x406A88          ; Length(FSBank)
0x6E4D03  8B F0                mov esi, eax
0x6E4D05  C1 E6 03             shl esi, 3             ; esi = 条目数 * 8
0x6E4D16  8B 80 08 08 00 00    mov eax, [eax+0x808]
0x6E4D1C  E8 67 1D D2 FF       call 0x406A88          ; Length(FVBank)
0x6E4D23  C1 E7 03             shl edi, 3
```

发射：

```
0x6E4DD3  C7 00 AA EF CD AB    mov dword [eax], 0xABCDEFAA
0x6E4DD9  66 89 70 04          mov word  [eax+4], si          ; 字节长度
0x6E4DDD  C6 40 06 00          mov byte  [eax+6], 0           ; type 0
0x6E4DE7  8B 80 04 08 00 00    mov eax, [eax+0x804]           ; src = S 银行
0x6E4DEF  E8 6C E4 D1 FF       call 0x403260                  ; Move
0x6E4E05  C7 00 AA EF CD AB    mov dword [eax], 0xABCDEFAA
0x6E4E0F  C6 40 06 01          mov byte  [eax+6], 1           ; type 1
0x6E4E19  8B 80 08 08 00 00    mov eax, [eax+0x808]           ; src = V 银行
```

段顺序固定为 **0, 1, 2, 6, 7, 8**（`0x6E4DD3` / `0x6E4E07` / `0x6E4E3B` / `0x6E4E6D` /
`0x6E4E9F` / `0x6E4ED1`），长度为 0 的段整段省略（`test esi,esi / jle`）。

**结论：条目顺序约束 = 「按 Key 升序」，写侧和读侧都不重排，全靠 `sub_6E4140` 维持。**

---

## 7. 裸偏移能被反解的前提：眼神把 `S(1,1..150)` 灌满

`0x100CE4EA` 起是一段一次性初始化（函数 `0x100CE479`）：

```
0x100CE4EA  6A 31                push 0x31            ; index = 49
0x100CE4EC  BA 01 00 00 00       mov  edx, 1          ; group = 1
0x100CE4F3  E8 48 7B F8 FF       call 0x10056040      ; GetS(1,49)
0x100CE4FB  3D 22 05 00 00       cmp  eax, 0x522      ; == 1314 ?
0x100CE500  74 60                je   0x100CE562      ; 已初始化 -> 整段跳过
0x100CE502  BE 01 00 00 00       mov  esi, 1          ; i = 1
0x100CE50D  81 FE 96 00 00 00    cmp  esi, 0x96       ; i > 150 ?
0x100CE513  7F 4D                jg   0x100CE562
0x100CE517  83 FE 31             cmp  esi, 0x31       ; i == 49 ?
0x100CE51A  75 11                jne  0x100CE52D
0x100CE51C  68 22 05 00 00       push 0x522           ;   写 1314（无条件）
0x100CE522  E8 D9 FC FF FF       call 0x100CE200      ;   SetS(1, i, ...)
0x100CE52D  56                   push esi
0x100CE533  E8 08 7B F8 FF       call 0x10056040      ; GetS(1, i)
0x100CE53B  85 C0                test eax, eax
0x100CE53D  79 20                jns  0x100CE55F      ; 当前值 >= 0 -> 不动
0x100CE541  83 FE 09             cmp  esi, 9
0x100CE544  7D 0E                jge  0x100CE554
0x100CE546  6A FF                push -1              ;   i < 9  -> 写 -1
0x100CE549  E8 B2 FC FF FF       call 0x100CE200
0x100CE554  6A 00                push 0               ;   i >= 9 -> 写 0
0x100CE557  E8 A4 FC FF FF       call 0x100CE200
```

**为什么这保证了连续性**：`SetS` 只拒绝 `group<=0 / index<=0`，**对 value 没有任何检查**
（§4.2）。所以「写 -1」同样会创建键。而循环的跳过条件是「当前值 >= 0」，
而 `GetS` 只在**键存在**时才可能返回 >= 0（miss 恒 -1，`0x6DF1BB`）。
两种情况取并集：键要么已存在，要么被这一轮创建。

⇒ **跑过一次之后，键 1001..1150 全部存在且连续**，占据槽位 0..149。

group ≥ 1 且 index ≥ 1 ⇒ 最小键 1001，所以槽位 0 一定是 `S(1,1)`，不会有更小的键插到前面。
`S(1,151+)`、`S(2,*)`、`S(6,*)` 的键都 > 1150，只会排在 150 号槽之后，**不影响前 150 格**。

---

## 8. 换算表：眼神桩体里的裸偏移 → `S(group,index)`

### 8.1 公式

```
slot   = 裸偏移 div 8
field  = Key   (偏移 mod 8 == 0)
       | Value (偏移 mod 8 == 4)
Key    = 1001 + slot
S 坐标 = S(1, slot + 1)

反向： S(1, i)  ->  Key 在 (i-1)*8 ，Value 在 (i-1)*8 + 4
```

### 8.2 公式的验证（这不是推测）

眼神每个桩体在取值前，都会先把该槽的 **Key 字段**读出来和一个内嵌常量比对
——这个常量就是它自己认为该槽应有的键。把 13 处逐一核对：

| 访问 VA | 裸偏移 | slot | 公式预测 Key | 桩体内嵌 `cmp` 常量 | |
|---|---|---|---|---|---|
| `0x1007AF32` | `0x180` | 48 | 1049 | 1049 (`81 FB 19 04 00 00`) | OK |
| `0x1007AF4E` | `0x398` | 115 | 1116 | 1116 (`81 FB 5C 04 00 00`) | OK |
| `0x1007AF98` | `0x180` | 48 | 1049 | 1049 | OK |
| `0x1007AFB4` | `0x3A0` | 116 | 1117 | 1117 (`81 FB 5D 04 00 00`) | OK |
| `0x1007AFFD` | `0x180` | 48 | 1049 | 1049 | OK |
| `0x1007B019` | `0x3A8` | 117 | 1118 | 1118 (`81 FB 5E 04 00 00`) | OK |
| `0x1007B063` | `0x180` | 48 | 1049 | 1049 | OK |
| `0x1007B07F` | `0x3B0` | 118 | 1119 | 1119 (`81 FB 5F 04 00 00`) | OK |
| `0x1007B0C6` | `0x180` | 48 | 1049 | 1049 | OK |
| `0x1007B0E2` | `0x3B8` | 119 | 1120 | 1120 (`81 FB 60 04 00 00`) | OK |
| `0x100DBA78` | `0x2E8` | 93 | 1094 | 1094 (`81 FF 46 04 00 00`) | OK |
| `0x100DBB23` | `0x2F8` | 95 | 1096 | 1096 (`3D 48 04 00 00`) | OK |
| `0x100DBB3E` | `0x328` | 101 | 1102 | 1102 (`3D 4E 04 00 00`) | OK |

**13 命中 / 0 失配。** 复跑：`_sbank/q12_final.py`。

另外每个桩体还会检 `bank[0x184] == 0x522`（1314）—— 即 `S(1,49).Value == 1314`，
正是 §7 那个初始化标记。**桩体自己在运行期断言「布局就是这张表」。**

### 8.3 全部裸偏移访问点（`.text` 里全集）

`+0x804` 在眼神 `.text` 里一共 13 处访问，其中 4 处是**同名不同物**，必须排除：

| VA | 指令 | 判定 |
|---|---|---|
| `0x100B805A` | `cmp dword [eax+0x804], 0` | **不是 S 银行**：基址来自 `[0x1031C514]` 单例 |
| `0x100B8465` | `mov dword [eax+0x804], 0x64` | 同上，`0x64`=100 是 §4.22 的「已打补丁标记」 |
| `0x100B8491` | `cmp dword [eax+0x804], 0x64` | 同上 |
| `0x100B84A0` | `mov dword [eax+0x804], 0` | 同上（成对还原） |
| `0x10007598` | `imul dword [esi+0x804]` | 线性反汇编错位产物，非真实指令 |

真正的银行指针加载 8 处，解出 8 个坐标：

| 桩体 / 加载 VA | 读到的坐标 | 裸偏移(Key/Value) | 语义（随包 S 变量表独立佐证） |
|---|---|---|---|
| `0x1007AF24` | `S(1,116)` | `0x398` / `0x39C` | 冰咆哮切割 |
| `0x1007AF8A` | `S(1,117)` | `0x3A0` / `0x3A4` | 火墙切割 |
| `0x1007AFEF` | `S(1,118)` | `0x3A8` / `0x3AC` | 烈火切割 |
| `0x1007B055` | `S(1,119)` | `0x3B0` / `0x3B4` | 雷电术切割 |
| `0x1007B0B8` | `S(1,120)` | `0x3B8` / `0x3BC` | 灵魂火符切割 |
| `0x100DBA72` / `0x100DBA86` | `S(1,94)` | `0x2E8` / `0x2EC` | 施毒术自定义毒范围 |
| `0x100DBB1D` | `S(1,96)` | `0x2F8` / `0x2FC` | 施毒术每次掉血量 |
| `0x100DBB1D` | `S(1,102)` | `0x328` / `0x32C` | 施毒术中毒时间 |
| （全部 8 处共用） | `S(1,49)` | `0x180` / `0x184` | 初始化标记 = 1314，仅作守卫 |

语义来自随包 `pay.510youxi.com（内部S变量使用表）.txt` 第 82/84/85/96-100 行，
与字节行为一致（`S(1,94)` 在 `0x100DBA97..0x100DBAB7` 被用来构造 `2*S+1` 的方框范围，
`S(1,116..120)` 在 `add [ebp+0x10], ebx` 处直接加进伤害）。

### 8.4 桩体形状（以 `0x1007AF21` 为例，五个切割臂完全同构）

```
0x1007AF12  81 B9 88 06 00 00 F4 01 00 00  cmp dword [ecx+0x688], 0x1F4  ; 开关（§4.22 编码区间中点）
0x1007AF1C  7E 4F                          jle skip
0x1007AF21  8B 55 0C                       mov edx, [ebp+0xC]            ; player
0x1007AF24  8B B2 04 08 00 00              mov esi, [edx+0x804]          ; S 银行指针
0x1007AF2A  81 FE 00 00 41 00              cmp esi, 0x410000
0x1007AF30  72 38                          jb  skip                      ; 空/野指针守卫
0x1007AF32  8B 9E 80 01 00 00              mov ebx, [esi+0x180]          ; slot 48 .Key
0x1007AF38  81 FB 19 04 00 00              cmp ebx, 0x419                ; == 1049 ? (S(1,49))
0x1007AF3E  75 2A                          jne skip
0x1007AF40  8B 9E 84 01 00 00              mov ebx, [esi+0x184]          ; slot 48 .Value
0x1007AF46  81 FB 22 05 00 00              cmp ebx, 0x522                ; == 1314 ? (已初始化)
0x1007AF4C  75 1C                          jne skip
0x1007AF4E  8B 9E 98 03 00 00              mov ebx, [esi+0x398]          ; slot 115 .Key
0x1007AF54  81 FB 5C 04 00 00              cmp ebx, 0x45C                ; == 1116 ? (S(1,116))
0x1007AF5A  75 0E                          jne skip
0x1007AF5C  8B 9E 9C 03 00 00              mov ebx, [esi+0x39C]          ; slot 115 .Value  <<< 取值
0x1007AF62  83 FB 00                       cmp ebx, 0
0x1007AF65  7E 03                          jle skip                      ; <=0 整段跳过（§5.1.0.4）
0x1007AF67  01 5D 10                       add [ebp+0x10], ebx           ; 加进伤害
```

五个臂由 `[ebp+0x1C]`（技能 id）分派：

```
0x1007AEAD  cmp [ebp-0x200], 0x16    / je 0x1007AF72  -> S(1,117)  (id 22)
0x1007AECF  sub [ebp-0x1F0], 0x0B    / je 0x1007B03D  -> S(1,119)  (id 11)
0x1007AEDC  sub [ebp-0x1F0], 2       / je 0x1007B0A0  -> S(1,120)  (id 13)
0x1007AEEE  cmp [ebp-0x200], 0x21    / je 0x1007AF0C  -> S(1,116)  (id 33)
0x1007AEF7  cmp [ebp-0x200], 0x3EF   / je 0x1007AFD8  -> S(1,118)  (id 1007)
```

`0x100DBA50` 那个是标准 trampoline：`cmp [0x1031C5F4],0x64` 门控 → `pushal/pushfd`
→ 干活 → `popfd/popal` → `jmp dword [0x10311048]`。安装现场：

```
0x100DCE49  68 50 BA 0D 10   push 0x100DBA50      ; 桩体
0x100DCE4E  68 FA D9 6E 00   push 0x6ED9FA        ; 续跑点
0x100DCE53  68 F5 D9 6E 00   push 0x6ED9F5        ; 宿主
0x100DCE58  68 F5 D9 6E 00   push 0x6ED9F5
0x100DCE5D  E8 9E 5D F5 FF   call 0x10032C00      ; 安装器
```

宿主 `0x6ED9F5` 原字节 `E9 51 06 00 00 = jmp 0x6EE04B`（魔法分发器的 DEFAULT 汇聚点，
§4.12），正好 5 字节，被整条替换 —— 与 `[0x10311048]` 转储值 `0x006EE04B` 自洽。

---

## 9. 六组英雄功能：换算已解，但取值点仍不可读（BLOCKED，原因已定性）

任务点名的 `英雄倍攻和暴击` / `英雄攻速移速` / `英雄施法速度` 在随包表里是：

| 坐标 | 说明 | 裸偏移（Key/Value） |
|---|---|---|
| `S(1,42)` | 英雄攻击速度（每 100 点 +0.1 秒） | `0x148` / `0x14C` |
| `S(1,43)` | 英雄移动速度 | `0x150` / `0x154` |
| `S(1,44)` | 英雄倍攻值（1 = 原伤害 1%） | `0x158` / `0x15C` |
| `S(1,45)` | 英雄暴击倍数（100 = 1 倍） | `0x160` / `0x164` |
| `S(1,46)` | 暴击概率（100 = 百分百） | `0x168` / `0x16C` |
| `S(1,47)` | 英雄施法速度 | `0x170` / `0x174` |

**换算这一层已经不再是阻塞点** —— 上表的偏移可以直接用。

但是**取值点本身在这份转储里读不到**，两条独立扫描都是 0 命中：

1. 包装器路径：`0x10056040` / `0x10065F40` / `0x100CE200` 共 64 个调用点，
   **没有任何一个推入 42..47 之间的 index**（`_sbank/q19_wrapper_sites.py`）。
2. 裸银行路径：`.text` 里 `+0x804` 只有上述 8 个真实加载点，
   `0x148..0x174` 这些位移**一个都没出现**（`_sbank/q12_final.py`）。

按 §5.1.0.3 的判据，这属于**第 ② 类：函数被 Themida 逐函数虚拟化，跳进
`0x400000..0x1400000` 的零页**，本转储永久不可判。
**要落地这六组，缺的是一份包含 VM 段的新转储，不是缺分析。**

（`特殊宝宝` 同理：随包表里没有对应条目，包装器与裸银行两侧也都没有命中。）

---

## 10. C# 现状

| 项 | 判定 | 位置 / 证据 |
|---|---|---|
| `Key = group*1000+index` | `FAITHFUL` | `PasApiBridge.cs:8241-8245` 已按 `sub_6E42CC` 写对，注释也对 |
| miss = -1 | `FAITHFUL` | `PasApiBridge.cs:8274` `NativeScriptVarMiss = -1` |
| `group<=0 / index<=0` 拒绝 | `FAITHFUL` | `PasApiBridge.cs:8286-8311` |
| V(0,1..100) 内联区 | `FAITHFUL` | `TPlayObject.Base.cs:245` `m_ScriptVGroup0`，含 `+0x80C` 推导 |
| 写 0 存 0（无零值特例） | `FAITHFUL` | `NativeHumanDataCodec.cs:1123-1127` 引 `0x6E4187` 等四点 |
| 存档段升序 | `FAITHFUL` | `NativeHumanDataCodec.cs:1103` `SortedDictionary` → 升序落盘 |
| 键 < 1001 不落盘 | `FAITHFUL` | `NativeHumanDataCodec.cs:1106`、`UsrEngn.cs:3951/3959` |
| **`S(1,1..150)` 一次性灌种** | **`MISSING`** | 全库搜 `1314` / `S(1,49)` 无命中；无任何等价实现 |
| **`S(1,116..120)` 五种切割** | **`MISSING`** | 全库 0 命中 |
| **`S(1,94)/(1,96)/(1,102)` 施毒术三项** | **`MISSING`** | 全库 0 命中 |
| 存档解码不重排 | `DIVERGENT`（良性） | C# 用 `Dictionary`，顺序无关；原生 `0x6E45A8` 是裸 `Move`，乱序 blob 会让原生二分失效而 C# 仍能读。**不建议改**：收紧只会在换回 Delphi 时暴露旧数据 |

### 落地顺序建议

灌种（`S(1,1..150)`）必须**先**落地。原因不是性能，是**语义**：
§5.1.0.4 已经指出 `S(1,9..150)` 在插件跑过之后读回的是 `0` 而不是 `-1`，
而这 150 个键**会真的落盘**（`Length(FSBank)` 直接进 type0 段长度）。
先做消费端、后做灌种，会出现「C# 存出来的角色档比原版少 150 个键」，
换回 Delphi 跑一次就产生行为差异。

---

## 11. 对既有材料的订正

1. **§5.1.0.5 的「58 个调用点」表可以确认为 58 个，但索引清单需要三处订正**
   （复跑 `_sbank/q19_wrapper_sites.py`）：
   - 漏了 `S(1,49)` —— `0x100CE4F3` 就是那个初始化标记探测，是真实读点。
   - `202` 被列在 group 1 下，实际 `0x1008541C` 的 group 不是常量
     （没有 `mov edx,imm`），应记为「变址 group / index 202」。
   - 表里只写了 `SetS(6,1)`，实际还有两个 **读** 点：`0x100697B7` 读 `S(6,1)`、
     `0x100CE57C` 读 `S(6,2)`。
2. **`[0x1031C514]+0x804` 与 `[player+0x804]` 是两回事。** 前者是眼神单例的
   「已打补丁标记」字段（写 `0x64`=100、成对还原写 0），后者才是 S 银行。
   四处（`0x100B805A/8465/8491/84A0`）按位移正查会被误收进 S 银行清单。
3. **`0x78D908` 不是 TypeInfo 本身，是指向 TypeInfo 的指针。**
   真正的 `TTypeInfo` 在 `0x78D90C`。按 `0x78D908` 直接解 RTTI 会读到错位的 kind 字节。

---

## 12. 复现

```
py = C:\Users\Administrator\AppData\Local\hermes\hermes-agent\venv\Scripts\python.exe
cd D:\loym2\staging\_sbank
%py% q01_lookup.py        # sub_6E4270 全函数 + 调用者
%py% q02_accessors.py     # GetS/GetV/SetS/SetV + sub_6E42CC + sub_406A88
%py% q03_setter.py        # sub_6E4140 + 解码器头
%py% q04_rtti.py          # TScriptTagArr RTTI + 段跳表
%py% q05_encoder.py       # 段名串 + 0xABCDEFAA 全部出现点
%py% q06_fieldtable.py    # TPlayer 托管字段表 + 编码器
%py% q07_enchead.py       # 编码器长度计算
%py% q08_wrapper.py       # 眼神四个包装器 + 常量调用现场
%py% q09_scan804.py       # 眼神 .text 里全部 +0x804
%py% q10_clusters.py      # 两个真实簇
%py% q12_final.py         # 换算表 + 13/13 键校验    <<< 核心
%py% q15_class.py         # TPlayer VMT 证明
%py% q16_seed.py          # S(1,1..150) 灌种循环
%py% q17_hosts.py         # trampoline 宿主
%py% q19_wrapper_sites.py # 64 个包装器调用点 + 42..47 的 0 命中证明
```
