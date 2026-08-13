# 眼神战斗主干五项 —— 伤害公式层逐指令反演

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt2\ys-formula`　分支：`w/ys-formula`　基线：`fb71c026`（建树时的 `master`）
- 上游：`docs/yanshen_evidence_20260814.md` §5.6 明确把这一层记为空白
  （"五项的内部数值语义本轮没有反演到位，C# 现有公式仍无原生字节背书"）。本报告收敛的就是这一条。
- 纪律：逐指令反演、每条结论附字节；拿不到字节的一律写"仍不可证"，不臆造。

---

## 0. 结论先行

| 指标 | 值 |
|---|---:|
| 五项实现体反演 | **5 / 5 完整到 `ret`**（含 Themida 代码搬迁段，见 §1.2） |
| 原生公式落到字节 | **5 / 5** |
| 逐条判定行 | **52**（施毒 9 + 麻痹 8 + 吸血 5 + 切割 12 + 自定义伤害 18） |
| 其中 FAITHFUL（原本就对） | **5** |
| **非 FAITHFUL 的数值语义差异** | **47** |
| 本轮已按字节修正 | **38** |
| 仍 fail-closed / 只做到部分 | **9 条判定行**，归并为 §8 的 **7 个条目** + 1 条结构性不可达（`-888` 元数哨兵） |

**一句话**：五项的伤害层根本不是 C# 现在写的样子。C# 的 `CalcDamage = max(0,DC-AC) + baseHp*(magicLv+1)/10 + cuttingV`
只对了 `baseHp*(magicLv+1)/10` 这**一个加项**；原生既不做 `max(0,DC-AC)`，也不掷 AC，
而是 **`攻高 - Random(攻高-攻低-命中) - 防高`**，随后串三级宿主管线
（`sub_767BA8` 致命一击 → `VMT+0x1B0` DamageHealth → `SendDelayMsg(10101)`），
且 `cuttingV` 是在**魔法护盾之后**才加的。四项返回值也全错（C# 返回"命中个数/伤害总和"，原生返回**参数本身**或负数错误码）。

好消息：本仓 `TBaseObject` 早已把这三级管线逐字节移植好了
（`ApplyNativePhysicalCritical` = `sub_767BA8`、`DamageHealth` = `sub_767D14`、
`ApplyNativeBubbleDefence` = `sub_76FFE8`、`MakePosion` = `VMT+0xC8`、`IncHealthSpell` = `sub_769DB4`），
所以修正是**纯组合**，不新增任何状态、不改任何宿主原语。

---

## 1. 底本与方法

### 1.1 底本

```
ys208(已重定位) : staging\yanshen208_strparam_runtime_dump_20260719\yanshen2_0_8_dll.memory.bin   base 0x10000000
ys208(delayed)  : staging\yanshen208_strparam_runtime_dump_delayed_20260719\…                     PE base 0x57C40000（未重定位）
m2  主底本      : staging\_reunpack_work\flat_image.bin                                           base 0x00400000
明文            : staging\_ys208_plain\Envir__CommonScripts__眼神专用__AllFuc.pas
```

### 1.2 关键发现：`切割` / `自定义伤害` 的函数体被 Themida **搬走**了，但 delayed 转储里有

上一轮用的"已重定位"那份，在两处会突然断流：

```
1006EABF  68 44 F8 2B 10   push 0x102BF844
1006EAC4  E9 D8 97 E4 00   jmp  0x10EB82A1      ← 切割：解析完参数就跳走
1006DE60  68 30 F8 2B 10   push 0x102BF830
1006DE65  E9 65 85 30 01   jmp  0x113763CF      ← 自定义伤害：同一形状
1006CFEE  68 1C F8 2B 10   push 0x102BF81C
1006CFF3  E9 E5 82 1D 01   jmp  0x112452E4      ← 逐格取对象 sub_1006CF80
```

这三个跳转目标在**已重定位**那份里 **全零**（所以过去读到这里就以为"虚拟化了，不可证"）。
在 **delayed** 那份里它们**有内容**：

```
RVA 0x00EB82A1  ys208(已重定位) nonzero=0   ys208(delayed) 8D 64 24 04 0F BF F7 66 8B F5 8B 0D E4 C0 F5 57
RVA 0x012452E4  ys208(已重定位) nonzero=0   ys208(delayed) 8D 64 24 04 80 FB 83 A1 E4 C0 F5 57 4E 66 0F BA
```

⇒ 这不是虚拟化，是 Themida 的**代码搬迁 + 垃圾指令混淆**：`push <token>; jmp <far>`，
远端块以 `lea esp,[esp+4]`（丢掉 token）开头，夹杂死指令，末尾再 `jmp` 回 `.text`。
两份转储 **RVA 布局完全相同**，只有绝对操作数差一个 `0x47C40000`，
所以用 delayed 那份读、把落在 `[0x57C40000, +size)` 的绝对量减 `0x47C40000` 即可（= 装载器重定位干的事）。
本报告的 `切割` / `自定义伤害` 搬迁段就是这么读出来的，工具 `%TEMP%\ysfm_dly.py` / `ysfm_cfg.py`。

`切割` 的完整搬迁链（每块只有 1～3 条真指令）：

```
1006EAC4 jmp 0x10EB82A1
10EB82A1  8D 64 24 04        lea esp,[esp+4]
10EB82AB  8B 0D E4 C0 F5 57  mov ecx,[0x1031C0E4]        ; (重定位后)
10EB82B1  E9 AF A3 E5 FF     jmp 0x10D12665
10D12665  8B 71 18           mov esi,[ecx+0x18]
10D12668  E9 89 EA 10 00     jmp 0x10E210F6
10E210F6  6A 0F              push 0xF
10E210F8  E9 BD BC 15 00     jmp 0x10F7CDBA
10F7CDBA  E8 D1 67 0B FF     call 0x10033590             ; vector::operator[](15)
10F7CDBF  C1 E6 08           shl esi,8
10F7CDCA  33 30              xor esi,[eax]
10F7CDD2  81 F6 D9 E0 CE F0  xor esi,0xF0CEE0D9
10F7CDD8  E9 A6 60 CB FF     jmp 0x10C32E83
10C32E83  89 75 88           mov [ebp-0x78],esi          ; ← 解出来的就是 0x76920C（特效发送器）
10C32E94  8B 83 2C 01 00 00  mov eax,[ebx+0x12C]         ; caster.CurrX
10C32EA2  8B 83 30 01 00 00  mov eax,[ebx+0x130]         ; caster.CurrY
10C32EBA  E9 D2 61 37 00     jmp 0x10FA9091
10FA9091  90 / E9 70 5A 0C FF                            jmp 0x1006EB07  ← 回主体
```

⇒ 搬迁段只干两件事：**解密一个宿主函数指针**、**取施法者坐标**。公式本身全在 `.text` 里。

### 1.3 五项共用的宿主原语（全部已在 M2 主底本逐字节反演）

| 原生 VA | 语义 | 关键字节 | 本仓 C# 对应 |
|---|---|---|---|
| `0x7784A8` | `GetMovingObject(Envir; x,y,…)` | 调用点 `FF 55 94` / `FF 55 A4` | `Envirnoment.GetMovingObject` |
| `0x767498` | `IsProperTarget(self, target)` | 调用点 `FF 55 B4` / `FF 55 90` | `TBaseObject.IsProperTarget` |
| `0x7776A8` | `GetMapCellInfo(Envir; x,y; out cell)` | `1006EBD1 FF 55 A4` | `Envirnoment.GetMapCellInfo` |
| `0x767BA8` | **致命一击/暴击调制** | 见 §2.1 | `TBaseObject.ApplyNativePhysicalCritical` |
| `VMT+0x1B0 = 0x767D14` | **DamageHealth**（落血） | 见 §2.2 | `TBaseObject.DamageHealth` |
| `0x76FFE8` | 魔法护盾/元素后处理 | 见 §2.3 | `TBaseObject.ApplyNativeBubbleDefence` |
| `VMT+0xC8 = 0x76B3C8` | `MakeStatus(btState, wSecs, wValue)`（秒 → ×1000 → `VMT+0x1EC`） | `76B413 69 C8 E8 03 00 00 imul ecx,eax,0x3E8` | `TBaseObject.MakePosion(nType,nTime,nPoint)`（`nType = 31 - state`） |
| `0x766060` | `SendDelayMsg(Self; wIdent; wParam,p1,p2,p3; sMsg; dwDelay)` | `ret 0x18` | `TBaseObject.SendDelayMsg` |
| `0x769DB4` | `IncHealthSpell(nHP,nMP)` | `769DF2 8B 93 AC 02 00 00` | `TBaseObject.IncHealthSpell` |
| `0x76920C` | 特效广播（ident `0x27E`=638） | `769241 66 BA 7E 02` | 无（§8 F-6） |
| `0x76C804`（VMT+0xCC） | `GetAttackPower(base,spread)` | — | `TBaseObject.GetAttackPower` |

字段（本仓既有证据 `docs/eqv_shard10_20260814.md` §POIS-17 已钉死 `+0x2AC`=HP / `+0x2B0`=MaxHP）：

| 偏移 | 语义 | 本轮字节佐证 |
|---|---|---|
| `+0x128` | `m_PEnvir` | `10070898 8B 98 28 01 00 00` |
| `+0x12C` / `+0x130` | `CurrX` / `CurrY` | `100708A1` / `100708AA` |
| `+0x154` | 朝向 `Dir` | `1006EDEF 8A 98 54 01 00 00` |
| `+0x194`(w) `+0x198`(d) | 施法者 致命几率 / 致命伤害增加 | `767BBF` / `767BC6` |
| `+0x19C`(w) `+0x1A0`(w) | 目标 防致命几率 / 致命伤害减少 | `767BCF` / `767BD6` |
| `+0x1A4`(w) | 施法者 **麻痹时间增加**（秒） | M2 自身麻痹戒指路径 `76DEE9 66 8B 8B A4 01 00 00` + `66 83 C1 05`（+5 秒） |
| `+0x27C/0x280` | 目标 AC lo/hi | `1006E0F8 8B 9C FA 7C 02 00 00`（idx=0） |
| `+0x284/0x288` | 目标 MAC lo/hi | 同表 idx=1 |
| `+0x28C/0x290` | 施法者 DC lo/hi | `1006E0D0 8B 9C FA 8C 02 00 00`（idx=0） |
| `+0x294/0x298` `+0x29C/0x2A0` | 施法者 MC / SC lo/hi | 同表 idx=1 / 2 |
| `+0x2AC/0x2B0/0x2B4/0x2B8` | HP / MaxHP / MP / MaxMP | 既有证据 |
| `+0x2EE` | 不死族标志（`==1`） | `1006E20D 8A 98 EE 02 00 00` |
| `+0x34C` | LastHiter | `1006ECE4 83 B8 4C 03 00 00 00` |
| `+0x3A4` | 麻痹免疫截止 tick | `773C82 3B 86 A4 03 00 00` |
| `+0x84` | 命中/掷点档位（与 9 比较） | `1006E071 8B 9A 84 00 00 00`；**类归属未定，见 §8 F-4** |

---

## 2. 三级宿主管线的字节真值

### 2.1 `sub_767BA8` —— 致命一击调制（`eax=目标, edx=施法者, ecx=伤害`；`ret`，无栈参）

```
767BB1  89 4D F8                    mov [ebp-8],ecx           ; 伤害
767BB7  85 D2 / 0F 84 DB 00 00 00   test edx,edx / je → 原样返回
767BBF  66 8B B2 94 01 00 00        mov si,  word [施法者+0x194]   ; A 致命几率
767BC6  8B 92 98 01 00 00           mov edx, dword[施法者+0x198]   ; P 致命伤害增加
767BCF  66 8B B8 9C 01 00 00        mov di,  word [目标  +0x19C]   ; B 防致命几率
767BD6  66 8B 98 A0 01 00 00        mov bx,  word [目标  +0x1A0]   ; D 致命伤害减少
767BDD..767BF2                      三者任一 <0（有符号 16 位）→ 原样返回
767BF8/767C03/767C0E                D / A / B 各钳到 0x2710 = 10000
767C1D  DF 45 F0                    fild word [B]
767C20  D8 35 A4 7C 76 00           fdiv  dword[0x767CA4] = 100.0
767C26  D8 2D A4 7C 76 00           fsubr dword[0x767CA4]            ; 100.0 - B/100.0
767C30  DF 45 EC                    fild word [A]
767C33  D8 35 A4 7C 76 00           fdiv  100.0
767C39  DE C9                       fmulp                             ; ×
767C3B  E8 34 B9 C9 FF              call 0x403574                     ; @ROUND
767C42  B8 10 27 00 00 / E8 …       eax=10000; Random(10000)
767C4C  0F BF D6 / 3B C2 / 7F 47    movsx edx,si ; cmp eax,edx ; jg → 原样返回   ← Random(10000) > 阈值 则不触发
767C53  DB 45 F4 / D8 35 A8 7C 76 00 / D8 05 AC 7C 76 00
                                    P/10000.0 + 1.5
767C69  DB 2D B0 7C 76 00           fld xword[0x767CB0] = 5e-05（80 位 Extended）
767C71  DE E9                       fsubp                             ; −  D*5e-05
767C73..767C8B                      −  (P/10000.0)*D/10000.0
767C8D  DB 45 F8 / DE C9            × 伤害
767C92  E8 DD B8 C9 FF              call 0x403574                     ; @ROUND
```

常量（原始字节）：

```
0x767CA4  00 00 C8 42                            float32 = 100.0
0x767CA8  00 40 1C 46                            float32 = 10000.0
0x767CAC  00 00 C0 3F                            float32 = 1.5
0x767CB0  2C 65 19 E2 58 17 B7 D1 F0 3F          ext80   = 5.0000000000000002396e-05
0x403574  83 EC 08 / DF 3C 24 / 9B / 58 / 5A / C3   = @ROUND（fistp qword，就近取偶）
```

⇒ `阈值 = Round((100 − B/100) × (A/100))`；命中后
`结果 = Round( 伤害 × ( (P/10000 + 1.5) − D·5e-05 − (P/10000)·D/10000 ) )`。
**两次都是 `fistp`（就近取偶），不是截断。** 本仓 `ApplyNativePhysicalCritical` 逐项一致，`RoundNativeX87` 用的正是 `MidpointRounding.ToEven`。

### 2.2 `VMT+0x1B0 = sub_767D14` —— DamageHealth（`eax=self, edx=伤害`）

```
767D1F  B2 3F / E8 …  IsStatus(0x3F) → esi -= esi div 2   （即 ceil(dmg/2)）
767D37  8A 8B DF 03 00 00   cl = byte[self+0x3DF]; 0 < cl < 100 → esi -= (esi*cl) div 100
767D5E  cmp [self+0x2B4](MP),0 > 0 时三选一的护盾吸收：
        [+0x1D3]≠0 → 全额转 MP
        [+0x1BA]≠0 → edi=Round(esi×1.5)；MP 吸；余下 esi=Round(edi/1.5)   （0x767E5C = 1.5）
        [+0x1BB]≠0 → edi = esi shr 1（**无符号**）；MP 吸
767E04  esi>0 → esi<HP ? HP-=esi : HP=0 ；否则按 MaxHP 上钳回血
767E44  call 0x7693E8                      （置脏位）
767E49  BA 01 / 8B C6 / E8 …0x4C7004       return Max(esi, 1)
0x4C7004  3B D0 / 7C 02 / 8B C2 / C3       = Max(eax, edx)
```

### 2.3 `sub_76FFE8` —— 魔法护盾/元素后处理（`eax=目标, edx=技能id, ecx=伤害`）

```
76FFF7  call 0x76FFD4       技能 id 门；不通过 → 原样返回
770000  mov dl,7 / IsStatus(7) → dmg = dmg*3 div 10
77001C  否则取 20 号状态节点；dmg>0 且节点在 →
        lvl==4 → dmg*3 div 10
        否则   → ((lvl+2)*dmg) shl 3 div 100
770064  81 68 02 B8 0B 00 00   [rec+2] -= 0xBB8（扣 3000 ms 护盾时长）
```

### 2.4 `VMT+0xC8 = sub_76B3C8` —— MakeStatus（秒 → 毫秒）

```
76B3D8  call 0x773C44        免疫检查 → 直接返回（其中 `773C8A cmp [ebp-1],0x1A` +
                             `773C82 cmp eax,[esi+0x3A4]` = 麻痹免疫窗口，**证明 state 0x1A(26) 就是麻痹**）
76B3E1  mov dl,0x34 / IsStatus(0x34) → 直接返回
76B3EE  id==0x12 且 IsStatus(0x1A) → RemoveState(0x1A)
76B409  push word[ebp+8]（value/level）/ push 0
76B413  69 C8 E8 03 00 00    imul ecx, eax, 0x3E8      ← 秒 × 1000
76B41F  FF 93 EC 01 00 00    call [vmt+0x1EC] = 0x7730D0（AddState，毫秒制、按 value 强度覆盖）
```

---

## 3. 施毒 `sub_100706A0`（`ys_ShiDu` / `ys_ShiDu_effect`，操作码 5）

### 3.1 原生公式（含字节）

形参（`AllFuc.pas`：`shijian,leix,hp,gailv,fanwei,TargetX,TargetY,Canl,isqun[,effect]`）：

| token | 形参 | 落点 | 字节 |
|---:|---|---|---|
| — | 元数下限 11，不足返回 **-888** | — | `10070700 83 F8 0B` / `10070714 B8 88 FC FF FF` |
| 2 | `shijian` | `[ebp-0x48]` | `10070727 6A 02` |
| 3 | `leix` | `ebx`，随后 **`+30`** | `10070742 6A 03` / `10070859 83 C3 1E` |
| 4 | `hp` | `[ebp-0x50]` | `1007075C 6A 04` |
| 5 | `gailv` | `[ebp-0x30]` | `10070777 6A 05` |
| 6 | `fanwei` | `esi`/`[ebp-0x54]` | `10070792 6A 06` |
| 7 | `TargetX` | `[ebp-0x28]` | `100707AF 6A 07` |
| 8 | `TargetY` | `edi`/`[ebp-0x40]` | `100707CA 6A 08` |
| 9 | `Canl` | `[ebp-0x2C]` | `100707E7 6A 09` |
| 10 | `isqun` | `[ebp-0x44]` | `10070802 6A 0A` |
| 11 | `effect`（可选，缺省 0） | `[ebp-0x3C]` | `10070839 83 F8 0C` / `10070840 6A 0B` |

```
; 距离门（Canl 是「离施法者的最大距离」，不是"是否含玩家"）
100708BB  85 C9 / 7E 35              if Canl <= 0 跳过
100708BF  8B 45 A8 / 2B C3 / 99 / 33 C2 / 2B C2 / 3B C1 / 7E 19
                                     |CurrX-TargetX| > Canl → 返回 0xFFFFFC19 = -999
100708E6  同上对 Y                    |CurrY-TargetY| > Canl → -999
100708DC  B8 19 FC FF FF             mov eax,-999

; 单体（fanwei <= 0）
10070904  B8 64 00 00 00 / FF 15 C4 BC 31 10   Random(100)
1007090F  3B 45 D0 / 7D 72                     rnd >= gailv → 跳过        （即 命中条件 rnd < gailv）
10070922  eax=[caster+0x128] / FF 55 94        GetMovingObject(Envir,TargetX,TargetY, 0,0,0,1)
1007092E  74 53                                nil → 跳过
10070933  3B 45 DC / 74 4E                     **target == caster → 跳过**
10070942  FF 55 90                             IsProperTarget(caster,target)，假 → 跳过
10070949..10070963                             SendDelayMsg：
          push [leix+30] / push shijian / push caster / push hp / push 0 / push 0x3E8
          mov cx,0x283C(=10300) / eax=target / call 0x766060
10070966  8B 45 B8 / 89 45 B4                  返回值 = shijian
1007096C  83 7D C4 00 / 7E 14                  effect>0 → call 0x76920C(caster,target,3,TargetY,TargetX,effect)

; 群体（fanwei > 0）
100709BE  83 7D BC 01 / 0F 85 …      isqun == 1 ? 逐格掷点 : 全区一次掷点
                                     （isqun==1 分支 0x100709F8 每格 Random(100)；
                                       else 分支 0x10070ACA 整个区域只掷一次）
遍历 x∈[TargetX-fanwei, TargetX+fanwei]、y∈[TargetY-fanwei, TargetY+fanwei] 的**方框**，
逐格取对象；obj≠nil 且 obj≠caster → call 0x10066EB0（与单体同一 SendDelayMsg 形状）
```

`sub_10066EB0`（`1006EE1 66 B9 3C 28`）与单体路径**逐参一致**，返回 `shijian`。

### 3.2 1000 ms 之后发生什么 —— ident 10300 的处理器

派发树 `sub_766A7C`：`0x766B09 sub eax,0x27C1` → `0x766B14 sub eax,0x7B` → `je 0x766E9F`（`0x27C1+0x7B = 0x283C`）。

```
766E9F  8B 46 08            eax = [rec+8] = nParam2 = 施法者
766EB4  call 0x767498       IsProperTarget；假 → 跳过仇恨，仍然上毒
766ECF/766F54/766F60        call 0x76719C（SetTargetCreat，仇恨）
766F6A  call 0x767504       SetLastHiter
766F6F  0F B7 46 0C / 50    push word [rec+0xC] = nParam3 = hp   ← 作为状态 value/强度
766F74  66 8B 4E 04         cx = word [rec+4] = nParam1 = shijian（秒）
766F78  8A 56 02            dl = byte [rec+2] = wParam = leix+30 ← **状态号**
766F7F  FF 93 C8 00 00 00   call [vmt+0xC8]   ← 与麻痹同一个 MakeStatus
```

M2 自身的武器带毒路径给出同一形状的独立佐证（`0x76E620`）：

```
76E673  6A 1E               push 0x1E = 30      ← 绿毒
76E689  68 E8 03 00 00      push 0x3E8 = 1000ms
76E68E  66 B9 3C 28         mov cx,0x283C
—— 另一条 0x76E561：76E5C9 6A 1F  push 0x1F = 31  ← 红毒
```

⇒ **`leix+30` 就是原生状态号，`hp` 是状态强度，`shijian` 是秒，整条链有 1000 ms 延迟。**
本仓 `MakePosion(nType,…)` 的 `nType = 31 - state`，故 `leix=0 → nType=1 = POISON_DAMAGEARMOR`、
`leix=1 → nType=0 = POISON_DECHEALTH` —— C# 现有映射**恰好正确**。

### 3.3 C# 现状 → 判定

| # | 项 | 原生 | C#（`YanshenApi.Poison/PoisonCore`） | 判定 |
|---|---|---|---|---|
| P-1 | 返回值 | `shijian`（未命中 0） | 命中个数 | **DIVERGENT → 已修** |
| P-2 | `Canl` | 离施法者最大距离，超出 **-999** | 当成 `players` 布尔传给 `FindTargets` | **DIVERGENT → 已修** |
| P-3 | `isqun` | `==1` **逐格**掷点（格内所有对象共用一次）；否则全区一次，掷不中整体返回 | 完全忽略，**逐目标**掷点 | **DIVERGENT → 已修**（掷点在 x/y 双重循环体内、取对象 `sub_1006CF80` 之前，逐目标掷会多耗随机数并改变命中分布，故 C# 侧改成按格枚举） |
| P-4 | 自身排除 | `target == caster → 跳过`（`10070933`） | 无 | **DIVERGENT → 已修** |
| P-5 | `IsProperTarget` | 单体路径必过（`10070942`） | 无 | **DIVERGENT → 已修** |
| P-6 | 毒型映射 | `leix+30` → `nType = 1-leix` | `type==0?DAMAGEARMOR:DECHEALTH` | **FAITHFUL** |
| P-7 | 1000 ms 延迟 + 仇恨 | `SendDelayMsg(…,0x3E8)` → `766E9F` 上毒并设仇恨 | 立即 `MakePosion` | **FAIL-CLOSED**（§8 F-1） |
| P-8 | `effect` | `>0` 时广播 638 | 形参吃掉不用 | **FAIL-CLOSED**（§8 F-6） |
| P-9 | `-888` 元数哨兵 | token<11 → -888 | 静态签名，无此路径 | **PARTIAL**（沿用上一轮记账） |

---

## 4. 麻痹 `sub_1006D690`（`Ys_Mymabi`，操作码 2）

### 4.1 原生公式（含字节）

形参：`timer,rand,round,TargetX,TargetY,Canl,isqun`（7 个）。

**该实现体开头没有任何元数下限检查** —— `1006D6D6` 直接 `push 0xA; push 2` 取 token 2，
与 3/5/8/34 号都不同（它们都有 `cmp eax,N` + `-888`）。

| token | 形参 | 落点 |
|---:|---|---|
| 2 | `timer` | `[ebp-0x40]` |
| 3 | `rand` | `[ebp-0x34]` |
| 4 | `round` | `esi`/`[ebp-0x48]` |
| 5 | `TargetX` | `[ebp-0x2C]` |
| 6 | `TargetY` | `edi`/`[ebp-0x44]` |
| 7 | `Canl` | `[ebp-0x28]` |
| 8 | `isqun` | `edx`/`[ebp-0x5C]` |

```
1006D7F2  85 C9 / 7E 3F      Canl>0 才做距离门
1006D804  C7 45 D0 19 FC FF FF   越界 → -999
1006D835  85 F6 / 0F 8F …    round <= 0 → 单体；否则群体
; 单体
1006D83E  B8 64 / FF 15 C4 BC 31 10   Random(100)
1006D849  3B 45 CC / 7D 55            rnd >= rand → 跳过
1006D85F  eax=[caster+0x128] / FF 55 A0   GetMovingObject(Envir,TargetX,TargetY,0,0,0,1)
1006D877  FF 55 9C                    IsProperTarget
1006D87E  6A 00                       push 0                      ← value/强度 = 0
1006D883  66 8B 8B A4 01 00 00        cx = word[caster+0x1A4]     ← 施法者「麻痹时间增加」
1006D88A  8B 45 C0 / 66 03 C8         cx += timer                 ← **16 位加法**
1006D890  B2 1A                       dl = 0x1A = 26              ← 麻痹状态
1006D897  FF 97 C8 00 00 00           call [vmt+0xC8]
1006D8A0  8B 45 C0 / 89 45 D0         返回值 = timer
; 群体：isqun==1 → 逐格掷点（0x1006D911）；否则全区一次掷点（0x1006D9C1）
;       方框遍历 + 逐格取对象 + sub_1006D130（与单体同一形状）
```

`sub_1006D130`：`1006D14C 66 8B 8B A4 01 00 00` / `1006D159 B2 1A` / `1006D160 FF 97 C8 00 00 00`，返回 `timer`。
**注意群体路径没有 `IsProperTarget`，也没有自身排除。**

### 4.2 C# 现状 → 判定

| # | 项 | 原生 | C#（`YanshenApi.Paralysis`） | 判定 |
|---|---|---|---|---|
| M-1 | 开关门 | 2 号臂**无门**（`100769B9` 开头就是 `mov ecx,[ebp+8]`） | `if (!Enabled("麻痹概率")) return 0;` | **DIVERGENT**（上一轮已记 偏差 1）→ 本轮不动，见 §8 F-7 |
| M-2 | 返回值 | `timer` | 命中个数 | **DIVERGENT → 已修** |
| M-3 | `Canl` | 距离门 / -999 | 当成 `players` 布尔 | **DIVERGENT → 已修** |
| M-4 | `isqun` | `==1` **逐格**（格内共用一次）；否则全区一次 | 忽略 | **DIVERGENT → 已修**（同 P-3） |
| M-5 | `IsProperTarget` | 单体路径必过 | 无 | **DIVERGENT → 已修** |
| M-6 | 状态号 | 26 | `POISON_STONE=5` → `31-5=26` | **FAITHFUL** |
| M-7 | 时长 | `word(caster[+0x1A4] + timer)` 秒 | `timerSec` | **FAIL-CLOSED**（§8 F-2：本仓无该聚合字段） |
| M-8 | 强度 value | `0` | `MakePosion(...,0)` | **FAITHFUL** |

---

## 5. 吸血 `sub_10070E70`（`ys_XiXue`，操作码 8）

### 5.1 原生公式（含字节）—— 全函数只有 12 行有效指令

```
10070EC8  83 F8 04                     token 数 < 4 → 
10070EDC  B8 88 FC FF FF               -888
10070EF6  6A 02 …                      esi = token2 = hp
10070F13  6A 03 …                      eax = token3 = bf_hp
10070F30  C7 45 DC B4 9D 76 00         [ebp-0x24] = 0x769DB4   ← IncHealthSpell
10070F37  89 75 EC                     返回值初值 = hp
10070F3A  85 C0 / 7E 37                bf_hp <= 0 → 跳过百分比项
10070F42  8B 9B B0 02 00 00            ebx = [caster+0x2B0] = MaxHP
10070F4C  66 0F 6E C8 / F3 0F E6 C9    xmm1 = (double)bf_hp
10070F54  F2 0F 5E 0D 40 89 2C 10      xmm1 /= [0x102C8940] = 100.0
10070F5C  66 0F 6E 45 E8 / F3 0F E6 C0 xmm0 = (double)MaxHP
10070F65  F2 0F 59 C8                  xmm1 *= xmm0
10070F69  F2 0F 2C C1                  **cvttsd2si**  ← 向零截断，不是取整
10070F6D  03 F0                        esi = hp + 上式
10070F76  8B 45 E4 / 8B 55 EC / 33 C9  eax=caster, edx=总量, ecx=0
10070F7E  FF 55 DC                     call 0x769DB4 = IncHealthSpell(nHP=总量, nMP=0)
10070FB8  8B C6                        返回 总量
```

常量：`0x102C8940 = 00 00 00 00 00 00 59 40` = **100.0**（不是 1000.0）。

⇒ **`总量 = hp + trunc(bf_hp / 100.0 × MaxHP)`，然后走 `IncHealthSpell(总量, 0)`，返回总量。**

`IncHealthSpell = 0x769DB4`：
```
769DC9  test esi,esi / jl → 直接返回        （任一为负直接返回）
769DD1  mov dl,0x66 / call 0x772960         IsStatus(0x66)=真 → **两个量各自减半**（`D1 FE / 79 03 / 83 D6 00`）
769DF2  HP  = min(HP+n, MaxHP)
769E16  MP  = min(MP+n, MaxMP)
769E3C  call 0x7693E8                       置脏位
```

### 5.2 C# 现状 → 判定

| # | 项 | 原生 | C#（`YanshenApi.LifeSteal`） | 判定 |
|---|---|---|---|---|
| X-1 | 百分比基数 | `/ 100.0`（**百分比**） | `* percentHp / 1000`（**千分比**） | **DIVERGENT → 已修**（生产回血量差 10 倍） |
| X-2 | 运算与取整 | `double` 乘除 + `cvttsd2si` 截断 | `long` 整数乘除 | **DIVERGENT → 已修** |
| X-3 | 落地 | `IncHealthSpell`（含 `IsStatus(0x66)` 减半 + 置脏位） | 直接写 `m_WAbil.HP` | **DIVERGENT → 已修** |
| X-4 | 返回值 | 总量（= `hp + 百分比项`） | `steal`（同义） | **FAITHFUL** |
| X-5 | 负值 | `IncHealthSpell` 对负值直接 return | `ClampAbility` 后仍写 | **DIVERGENT → 已修**（由 X-3 顺带修好） |

---

## 6. 切割 `sub_1006E8D0`（`ys_Cutting`，操作码 34）

### 6.1 原生公式（含字节）

形参：`round,TargetX,TargetY,Canl,types,cuttingV,lei,effect,AttactId,delay`（10 个，token 2..11）。

```
1006E928  83 F8 0C          token 数 < 12 → 1006E93C  B8 88 FC FF FF  = -888
1006E953  C7 45 98 FF…      [ebp-0x68] = -1（内部 MgId 占位，本函数不用）
token 2→[ebp-0x8C]=round   3→[ebp-0x40]/[ebp-0x30]=TargetX   4→[ebp-0x28]/[ebp-0x60]=TargetY
      5→[ebp-0x84]=Canl    6→[ebp-0x80]=types                7→[ebp-0x64]=cuttingV
      8→[ebp-0x58]=lei     9→[ebp-0x34]=effect              10→[ebp-0x48]=AttactId  11→[ebp-0x70]=delay
（搬迁段取出 caster.CurrX→[ebp-0x1C]、caster.CurrY→[ebp-0x20]、0x76920C→[ebp-0x78]）

1006EB10  85 DB / 7E 45     Canl>0 才做距离门；越界 1006EB42 B8 19 FC FF FF = -999
1006EB5C  8B 45 80 / 83 C0 FD / 83 F8 05 / 77 20 / FF 24 85 BC F0 06 10
                            types-3 ∈ [0,5] → 跳表 0x1006F0BC（6 臂）
     臂[0](types=3)         1006EB6E mov ebx,1 → [ebp-0x24]=1
     臂[1..2](types=4,5)    1006EB78 mov eax,1 → [ebp-0x24]=1
     臂[3..5](types=6,7,8)  1006EB7F mov eax,2 → [ebp-0x24]=2
                            其余 types → [ebp-0x24]=0
1006EC8E  cmp ebx,1 / 1006EC93 cmp [ebp-0x44],0x6AC8C8 / jne → 是玩家就 1006ECAB B8 F7 FC FF FF = **-777**
1006ECC2  cmp ebx,2 / 1006ECC7 同上 / jne → 不是玩家就 -777
                            ⇒ 过滤器 1 = 排除玩家；2 = 只打玩家；0 = 不过滤
                            （0x6AC8C8 = 玩家类 VMT）

; round <= 0：单体/链式，最多 30 个（1006EB9D B8 1E 00 00 00 = 0x1E）
1006EBB0  [ebp-0x48](AttactId) ≠ 0 → 1006EC2D 直接把 **AttactId 当对象指针**用（不是 RoleId！）
1006EBD1  否则 GetMapCellInfo(Envir,TargetX,TargetY,&cell)，走 cell.ObjList 链表
1006EBFC  83 B8 AC 02 00 00 00 / 7E   HP <= 0 跳过
1006EC10  IsProperTarget，假 → 该目标作废
1006EC65  一个都没取到 → B8 66 FD FF FF = **-666**

; 伤害
1006ECD0  8B 45 9C / 89 45 D4 / 85 C0 / 7F 07 / C7 45 D4 01 00 00 00
                            dmg = cuttingV；**cuttingV <= 0 时钳成 1**
1006ECE4  83 B8 4C 03 00 00 00 / 75 / 89 90 4C 03 00 00
                            target.LastHiter(+0x34C) 为空 → 设成 caster
1006ED02  8B 4D D4 / 8B 45 EC / 8B 55 E8 / 68 D8 0E 14 00 / FF 55 94
                            call 0x767BA8(目标, 施法者, dmg)   （`push 0x140ED8` 随后 `1006ED19 59 pop ecx` 丢弃，是噪声）
1006ED23  FF 91 B0 01 00 00  call [vmt+0x1B0] = DamageHealth  → 实际落血（返回 Max(n,1)）
1006ED34  FF 55 8C           call 0x76B4F8(目标, 施法者, 实际值, delay)
                            = SendDelayMsg(目标, ident 0x2775=10101, wParam=实际值, p1=实际值,
                                           p2=0, p3=施法者, sMsg=nil, dwDelay=delay)
                            （0x76B4F8 里 `push ecx; push ecx` 就是前两个实参，`edx=0x2724=10020` 写进 rec+0x24）
1006ED37  83 7D CC 00 / 7E   effect>0 → 0x76920C(caster,target,3, target.CurrY, target.CurrX, effect)
1006ED5E  83 7D B8 00 / 75   **AttactId ≠ 0 → 只打一个就停**；否则继续链表直到 30 次用尽
1006ED89  8B 45 D4           返回 = max(cuttingV, 1)

; round > 0：方框 AoE
1006ED9E  83 7D A8 01 / 75   **lei == 1 → 以施法者坐标为中心**；否则以 (TargetX,TargetY) 为中心
1006EDDE  83 7D A8 01 / 0F 85 …  lei == 1 时再按 `byte[caster+0x154]`（朝向 0..7）做 8 向直线/扇形筛格
                            （1006EDEF 8A 98 54 01 00 00 取朝向；1006EE07 起 8 个分支）
                            lei ≠ 1 → 整个方框全收
每格同样走 30 次链表上限 + 上面同一条伤害管线；AoE 分支的特效用**格子坐标**而非目标坐标（1006F060/1006F063）
```

### 6.2 C# 现状 → 判定

C# `HolyDamage(range,tx,ty,canl,types,cuttingV,lei,effect,attId,delayMs)`：

```csharp
foreach (var t in FindTargets(tx, ty, range, canl != 0))
    if (cuttingV > 0) { t.m_WAbil.HP -= Math.Min(t.m_WAbil.HP, cuttingV); total += cuttingV; }
return total;
```

| # | 项 | 原生 | C# | 判定 |
|---|---|---|---|---|
| C-1 | 落血 | 致命一击 → `DamageHealth`（`0x3F` 减半 / `+0x3DF` 百分比减免 / 三档魔法盾 / 置脏位） | 直接 `HP -= min(HP,cuttingV)` | **DIVERGENT → 已修** |
| C-2 | `cuttingV<=0` | 钳成 **1** | `cuttingV>0` 才打 | **DIVERGENT → 已修** |
| C-3 | 返回值 | `max(cuttingV,1)`；错误码 -999/-777/-666 | 伤害总和 | **DIVERGENT → 已修** |
| C-4 | `canl` | 离施法者最大距离 / -999 | 当成 `players` 布尔 | **DIVERGENT → 已修** |
| C-5 | `types` | 3/4/5 排除玩家、6/7/8 只打玩家、否则不过滤。**单格链表路径**首个不匹配即 -777（`1006EC93`/`1006ECC7`）；**方框路径**只跳过该格（`1006EFB0`/`1006EFBA` 都是 `je/jne 下一个`） | 完全忽略 | **DIVERGENT → 已修**（两条路径的不同返回行为一并复刻） |
| C-6 | `attId` | **对象指针**，且命中后只打一个 | 忽略 | **部分修 → §8 F-3**（行为按 ObjectId 复刻，指针语义不可复刻） |
| C-7 | `delay` | `SendDelayMsg(10101, …, delay)` | 忽略 | **DIVERGENT → 已修** |
| C-8 | `lei==1` | 以施法者为中心 + 8 向朝向筛格 | 忽略 | **部分修**：中心切换已修；朝向筛格 **FAIL-CLOSED**（§8 F-5） |
| C-9 | LastHiter | 为空则设 caster | 无 | **DIVERGENT → 已修** |
| C-10 | 30 次链表上限 | 每格最多 30（`1006EB9D B8 1E 00 00 00`） | 无 | **DIVERGENT → 已修**（`NativeChainWalkCap`，只在 `round<=0` 的单格链表路径生效） |
| C-11 | HP<=0 跳过 | `1006EBFC` | 无 | **DIVERGENT → 已修** |
| C-12 | `IsProperTarget` | 必过 | 无 | **DIVERGENT → 已修** |

---

## 7. 自定义伤害 `sub_1006DAB0`（`ys_MyJn_plus2/effect/undead/super/delay`，操作码 3）

### 7.1 形参阶梯（复核上一轮结论，字节一致）

必填 9（token 2..10），可选 6（token 11..16），下限 11 否则 **-888**（`1006DB0C 83 F8 0B` / `1006DB20 B8 88 FC FF FF`）。

| token | 形参 | 落点 | 缺省 |
|---:|---|---|---|
| 2 | `magicLV` | `[ebp-0x6C]` | — |
| 3 | `baseHP` | `[ebp-0xAC]` | — |
| 4 | `round` | `edi`/`[ebp-0xCC]` | — |
| 5 | `TargetX` | `[ebp-0x44]`/`[ebp-0x78]` | — |
| 6 | `TargetY` | `[ebp-0x20]`/`[ebp-0x7C]` | — |
| 7 | `Canl` | `[ebp-0x80]` | — |
| 8 | `types` | `[ebp-0x84]` | — |
| 9 | `cuttingV` | `[ebp-0xC0]` | — |
| 10 | `lei` | `[ebp-0x68]` | — |
| 11 | `effect` | `[ebp-0x60]` | `0` |
| 12 | `undead` | `[ebp-0x90]` | `0` |
| 13 | `MgId` | `[ebp-0x5C]` | **`-1`**（`1006DB38 C7 45 A4 FF FF FF FF`） |
| 14 | `AttactId` | `[ebp-0x3C]` | `0` |
| 15 | `double` | `[ebp-0x94]` | `0` |
| 16 | `delay` | `[ebp-0x9C]` | **`200`**（`1006DC61 … C8 00 00 00`） |

### 7.2 `types` 的三重含义（跳表 `0x1006E8B0`）

```
1006DEFD  83 FB 01 / 74 05 / 83 FB 02 / 75 07 / C7 45 CC 01 00 00 00
                                   types ∈ {1,2} → 防御索引 [ebp-0x34] = 1
1006DF0E  8D 43 FD / 83 F8 05 / 77 3F / FF 24 85 B0 E8 06 10   types-3 ∈ [0,5] → 跳表
   臂[0] types=3   1006DF1D  [ebp-0x34]=0（AC）, [ebp-0x30]=1
   臂[1,2] 4,5     1006DF2E  eax=1 → [ebp-0x30]=1, [ebp-0x34]=1（MAC）
   臂[3] 6         1006DF35  [ebp-0x34]=0（AC）, [ebp-0x30]=2
   臂[4,5] 7,8     1006DF46  eax=2 → [ebp-0x30]=2, [ebp-0x34]=1（MAC）
1006DF55  B8 55 55 55 55 / F7 EB / 2B D3 / D1 FA / … / 8D 04 19 / 8D 04 48
          [ebp-0x8C] = **types mod 3**   （magic-number 除 3；已对 0..8 逐值验算）
```

⇒ 三重含义：
- **攻击属性索引 = `types mod 3`** → `caster[0x28C + idx*8]`：0=DC、1=MC、2=SC；
- **防御属性索引 = `[ebp-0x34]`** → `target[0x27C + idx*8]`：0=AC、1=MAC；
- **目标类过滤 = `[ebp-0x30]`**：1 排除玩家（否则 -777）、2 只打玩家、0 不过滤。

> 边角：`types >= 9` 时跳表不走，防御索引/过滤器保持 0，但 `types mod 3` 照算。
> 例如 `types=10` → 攻 MC、防 AC、不过滤。这是原生行为，照抄。

### 7.3 取属性（`1006E054` / `1006E0C7` 两份同源副本）

```
1006E05D  8B 9C FA 8C 02 00 00   [ebp-0x54] = caster[0x28C + atkIdx*8]   ; 攻低
1006E067  8B 9C FA 90 02 00 00   [ebp-0x2C] = caster[0x290 + atkIdx*8]   ; 攻高
1006E071  8B 9A 84 00 00 00      [ebp-0x50] = caster[0x84]               ; 命中档位
1006E07D  8B 3A                  [ebp-0x4C] = [target]                   ; 目标 VMT（给类过滤用）
1006E085  8B 9C FA 7C 02 00 00   [ebp-0x58] = target[0x27C + defIdx*8]   ; 防低
1006E08F  8B 9C FA 80 02 00 00   [ebp-0x48] = target[0x280 + defIdx*8]   ; 防高
```

### 7.4 掷点与公式（`1006E18D` .. `1006E2C6`）

```
1006E190  83 F9 09 / 7D 0B        命中档位 < 9 → t = 攻高 - 攻低
1006E1A0  8B 45 E4                否则 t = [ebp-0x1C]（**上一目标的残值**，首次为 0；见 §8 F-4）
1006E1A3  2B C1                   t -= 命中档位
1006E1A8  79 07 / 33 C0           t < 0 → t = 0（**不掷点**）
1006E1B4  FF 15 C4 BC 31 10       否则 t = Random(t)
1006E1C0  29 45 D4                攻高 -= t                       ← 攻击掷点结果

1006E1C3  8B 45 B8 / 2B 45 A8     u = 防高 - 防低
1006E1CC  79 07 / C7 45 C0 0      u < 0 → 0
1006E1D8  FF 15 C4 BC 31 10       u = Random(u)   ← **算了但从不使用**（[ebp-0x40] 之后无读者）

1006E1E1  0F 28 D0                xmm2 = xmm0 = 1.0        （0x102C8910 = 1.0）
1006E1F6  81 7D B4 C8 C8 6A 00    undead>0 且 目标非玩家 且 byte[target+0x2EE]==1 →
1006E226  F2 0F 5E D3             xmm2 = undead / 1000.0   （xmm3 = 0x102C8950 = 1000.0）
1006E232  0F 28 C8                xmm1 = 1.0
1006E243  85 C0 / 7E / 3D E8 03 00 00 / 74
                                  double>0 且 double≠1000 →
1006E256  F2 0F 5E CB             xmm1 = double / 1000.0

1006E262  8B 4D 94 / 41           ecx = magicLV + 1
1006E266  0F AF 8D 54 FF FF FF    ecx *= baseHP
1006E26D  B8 67 66 66 66 / F7 E9 / C1 FA 02 / …
                                  eax = (baseHP*(magicLV+1)) **div 10**（有符号，向零截断）
1006E27E  2B 45 B8                eax -= 防高          ← **减的是防高，不是掷出来的防御**
1006E281  03 45 D4                eax += 攻高（已扣掷点）
1006E284  66 0F 6E C0 / F3 0F E6 C0   xmm0 = (double)eax
1006E28C  F2 0F 59 C2             *= xmm2 (undead)
1006E290  F2 0F 59 C1             *= xmm1 (double)
1006E294  F2 0F 2C D0             **cvttsd2si edx** ← 向零截断
1006E29B  FF 75 A4 / 8B 4D EC / E8 CA 7C FF FF
                                  edx = sub_10065F70(target, edx, MgId) → 0x76FFE8 魔法护盾
1006E2A9  8B D8 / 03 9D 40 FF FF FF   ebx = 上式 + cuttingV      ← **cuttingV 在护盾之后才加**
1006E2B7  85 DB / 7F 0B / B8 01…  <= 0 → 钳成 1
1006E2C9  83 B8 4C 03 00 00 00    LastHiter 为空 → 设 caster
1006E2F3  68 D8 0E 14 00 / FF 95 50 FF FF FF   call 0x767BA8 致命一击
1006E311  FF 91 B0 01 00 00       call [vmt+0x1B0] DamageHealth
1006E325  FF 95 4C FF FF FF       call 0x76B4F8(…, delay) → SendDelayMsg(10101)
1006E32B  83 7D A0 00 / 7E 21     effect>0 → 0x76920C(caster,target, magicLV, target.CurrY, target.CurrX, effect)
```

**原生真值（一行）**

```
atk  = 攻高 − ( (命中<9 ? 攻高−攻低 : 残值) − 命中 <= 0 ? 0 : Random(该值) )
raw  = (baseHP × (magicLV+1)) div 10 − 防高 + atk
dmg  = trunc( raw × mUndead × mDouble )         mUndead = (undead>0 且目标是不死族怪) ? undead/1000 : 1
                                                mDouble = (double>0 且 ≠1000) ? double/1000 : 1
dmg  = BubbleDefence(MgId, dmg)                 ; sub_76FFE8
dmg  = max(dmg + cuttingV, 1)
落地 = DamageHealth( Crit(target, caster, dmg) )
返回 = dmg（最后一个命中目标的值；一个都没命中 → -666 / -777 / -999 / -888）
```

### 7.5 C# 现状 → 判定

```csharp
int CalcDamage(int magicLv, int baseHp, int cuttingV, TBaseObject target) {
    var raw = Math.Max(0, (int)_player.m_WAbil.DC - (int)target.m_WAbil.AC)
            + (baseHp * (magicLv + 1)) / 10 + cuttingV;
    return Math.Max(0, raw);
}
```

| # | 项 | 原生 | C# | 判定 |
|---|---|---|---|---|
| D-1 | 攻击项 | `攻高 − Random(攻高−攻低−命中)`，属性由 `types mod 3` 选 DC/MC/SC | `m_WAbil.DC` 整个打包 int 直接当数值用（**连 LoWord/HiWord 都没拆**） | **DIVERGENT → 已修** |
| D-2 | 防御项 | `− 防高`，由 `types` 选 AC/MAC | `max(0, DC-AC)` 的 `max(0,…)` 与 AC 打包 int | **DIVERGENT → 已修** |
| D-3 | `baseHp*(magicLv+1)/10` | 同 | 同 | **FAITHFUL**（唯一对上的一项） |
| D-4 | `cuttingV` 位置 | 魔法护盾**之后** | 与其它项同级相加，护盾前 | **DIVERGENT → 已修** |
| D-5 | 下限 | `max(dmg,1)` | `max(raw,0)` | **DIVERGENT → 已修** |
| D-6 | `undead` | 千分比 double，且**仅对 `+0x2EE==1` 的非玩家**，在护盾前 | `d * undead / 1000`（对总和、整数、无条件） | **DIVERGENT → 已修** |
| D-7 | `double` | 千分比 double，`==1000` 视同 1.0，在护盾前 | `* double_ / 1000` 对总和 | **DIVERGENT → 已修** |
| D-8 | `MgId` | 传给 `sub_76FFE8` 做技能门（127/221 跳过） | 忽略 | **DIVERGENT → 已修** |
| D-9 | 致命一击 | `sub_767BA8` | 无 | **DIVERGENT → 已修** |
| D-10 | 落血 | `DamageHealth` | `t.m_WAbil.HP -= min(HP,dmg)` | **DIVERGENT → 已修** |
| D-11 | 返回值 | 最后一个目标的 `dmg`；错误码 -666/-777/-999 | 伤害总和 | **DIVERGENT → 已修** |
| D-12 | `Canl` | 距离门 / -999 | 当 `players` 布尔 | **DIVERGENT → 已修** |
| D-13 | `types` 类过滤 | 1 排除玩家 / 2 只打玩家。单格路径不匹配 → -777（`1006E14F`/`1006E184`）；方框路径只跳过（`1006E5E3`/`1006E5ED`） | 忽略 | **DIVERGENT → 已修** |
| D-14 | `delay` | `SendDelayMsg(10101,…,delay)`，缺省 200 | 忽略 | **DIVERGENT → 已修** |
| D-15 | `AttactId` | 对象指针，命中后只打一个 | 忽略 | **部分修 → §8 F-3** |
| D-16 | LastHiter | 为空则设 caster | 无 | **DIVERGENT → 已修** |
| D-17 | `lei` | **不参与选属性**（防御索引来自 `types`），但**不是纯占位**：`round > 0` 的方框路径上 `1006E38E 8B 75 98 / 83 FE 01 / 75 0E` 判 `lei == 1`，`1006E396` 把中心 `[ebp-0x44]/[ebp-0x20]` 覆盖成 `[ebp-0x24]/[ebp-0x28]` = 施法者坐标（Themida 搬迁块 `10CB4E0E 89 45 DC` / `10CB4E13 8B 83 30 01 00 00`，源是 `[ebx+0x12C]`/`[ebx+0x130]`），随后 `1006E3E2 cmp esi,1 / jne 1006E534` 还有 8 向筛格 —— 与切割 `1006ED9E` 完全同形 | 注释写成"半径类型(0=圆形 1=直线)" | **DIVERGENT → 已修**（圆心切换已落地；朝向筛格并入 §8 F-5） |
| D-18 | 命中档位残值 | `[ebp-0x1C]` 跨目标复用 | — | **FAIL-CLOSED**（§8 F-4） |

---

## 8. 仍 fail-closed 的 7 条

| # | 条目 | 障碍 | 方案 |
|---|---|---|---|
| **F-1** | 施毒的 **1000 ms 延迟 + 仇恨/LastHiter 副作用** | 原生走 `SendDelayMsg(ident 10300)`，本仓 `ProcessMsg` **没有 10300 分支**（全仓 `= 10300` 零命中）。硬发一条无人处理的延迟消息会让施毒**彻底失效**，比现在的"立刻上毒"更坏 | 先在 `TBaseObject` 的延迟消息处理器里补 `RM_` 10300 分支（照抄 `0x766E9F`：`IsProperTarget → SetTargetCreat → SetLastHiter → MakePosion(31-wParam, p1, p3)`），再把 `Poison` 改成发延迟消息。属"改消息主干"，超出本轮外科范围 |
| **F-2** | 麻痹时长的 `word[caster+0x1A4]`（施法者「麻痹时间增加」） | 本仓只有物品属性名 `"麻痹时间增加"`（`NativeType2StdItemSnapshotState.cs:401`），**没有 `TBaseObject` 上的聚合字段**。凭空加字段＝造状态 | 先在 `RecalcAbilitys` 里按原生 `+0x1A4` 的写入点补聚合（需另行反演写入点），再在此处相加。已在代码里留 `TODO(F-2)` 锚点 |
| **F-3** | `AttactId` 的**原始指针语义** | 原生 `1006EC2D mov [ebp-0x14],eax` 把 `AttactId` 直接当 `TBaseObject*`。C# 不能把 int 当对象引用 | 已按 **ObjectId** 解析（`M2Share.ObjectManager.Get`）实现"指定单一目标 + 命中后只打一个"这两个可观测行为；**指针数值本身不可复刻**，且 `AllFuc.pas` 侧也从未真的传过合法指针 |
| **F-4** | `caster[+0x84]`（命中档位）与 `[ebp-0x1C]` 跨目标残值 | `+0x84` 在 M2 里被 12 个类共用，本轮无法唯一归属；残值路径只在 `+0x84 >= 9` 时才走 | 实现取 `命中档位 = 0`（`< 9` 分支，即 `t = 攻高 − 攻低`），这是 `+0x84` 未被写入时的原生行为。`>= 9` 分支留 `TODO(F-4)`，并在代码注释里标明残值语义 |
| **F-5** | **切割与自定义伤害** `lei==1` 的 **8 向朝向筛格** | 切割 8 个分支（`1006EE07`..`1006EF22`）语义已读出，但每支的边界符号（`jns` / `js` / `jne`）组合复杂，且依赖 `byte[caster+0x154]` 的方向编码与本仓 `DR_*` 的对齐关系未逐值验证；自定义伤害 `1006E3E2` 起的同族分支同理 | 两处都已实现"以施法者为中心"这一半；筛格未做（等价于 `lei==1` 时打满方框，**打得比原生多**）。要修需先把 `DR_*` 与 `+0x154` 的 8 个取值逐值对齐 |
| **F-6** | 特效广播 `0x76920C`（ident 638） | 原生 `76922F mov eax,ebx` 里的 `ebx = ecx`，而**三个调用点都没有设置 ecx** —— 传进 `0x408D18` 的是垃圾值。无法确定该字段的真实内容 | 纯表现层，不影响数值。保持不发，登记在案 |
| **F-7** | 麻痹的 C# 侧多余门 `Enabled("麻痹概率")` | 上一轮 §5.3 偏差 1 已认定：原生 2 号臂无门。但直接删门会让生产上"关掉麻痹概率"的服务器突然开始麻痹 | 属**开关拓扑**问题（上一轮偏差 2 同族），需要和 `cfg2+0x11C` / `cfg2+0x524` 的整体映射一起改，不在本轮单点动 |

---

## 9. 本轮改动落点

只动了 `GameSvr/Plugins/YanshenApi.cs` 一个文件；`Grobal2.cs` / `TPlayObject.Message.cs` / `UsrEngn.cs` 未触碰，
`TBaseObject` 的宿主原语一行没改 —— 修正全部是**组合既有的、已逐字节验证的原语**。

| 提交 | 内容 |
|---|---|
| `d955b694` | 本文档 |
| `9c1b7602` | 吸血：百分比口径 + `IncHealthSpell` 落地 |
| `75342460` | 自定义伤害 + 切割：原生公式与三级落地管线、距离门/类过滤/错误码/`AttactId`/`delay` |
| `89628128` | 施毒 + 麻痹：距离门、逐格掷点、单体路径、原生返回值 |

新增的私有原语（都在 `YanshenApi.cs` 内，供五项共用）：

| 成员 | 对应原生 |
|---|---|
| `YsErrRange` / `YsErrClass` / `YsErrNoTarget` | `-999` / `-777` / `-666` 三个哨兵 |
| `NativeCanlGateFails` | `100708BB` / `1006EB10` / `1006DEAE` 的 `Canl` 距离门 |
| `NativeTypeClassFilter` / `NativeClassFilterAccepts` | 跳表 `0x1006F0BC` / `0x1006E8B0` 的目标类过滤 |
| `NativeCollectTargets` + `NativeChainWalkCap` | 单格链表（上限 30）/ 方框取目标，含 `HP>0` 与 `IsProperTarget` |
| `NativeEnumerateAreaCells` | 施毒/麻痹的**按格**枚举（掷点粒度所必需） |
| `NativeRollHit` | `Random(100) < 概率` |
| `NativeLandDamage` | `LastHiter` → `sub_767BA8` → `DamageHealth` → `SendDelayMsg(10101)` |

删除：`CalcDamage`（全仓已无引用）。`FindTargets` 保留 —— 它还有 9 个非战斗调用者。

验证：`dotnet build GameSvr\GameSvr.csproj` **0 错误**（15 条既有 warning，均与本轮无关）。

---

## 10. 复现

工具（均在 `%TEMP%`，只读）：

| 脚本 | 用途 |
|---|---|
| `ysfm_tool.py` | ys208（已重定位）反汇编 / hexdump / xref |
| `ysfm_m2.py` | M2 主底本反汇编（含 Delphi 短串注解） |
| `ysfm_dly.py` | **delayed 转储读取器**：按 RVA 读、绝对操作数自动 `-0x47C40000` 回到 `0x10000000` 空间 |
| `ysfm_cfg.py` | 跨搬迁块的 CFG 遍历（`切割` / `自定义伤害` 全靠它） |
| `ysfm_vmt.py` | Delphi VMT 定位（`vmtSelfPtr` 自校验）+ 槽位解引用 |
| `ysfm_field.py` | 按结构体偏移做全 `.text` 访问点普查 |
| `ysfm_imm.py` | 按立即数做全 `.text` 普查（ident 10300 就是这么找到的） |

关键复现命令：

```
python ysfm_vmt.py "TCreature" C8,1B0,1EC      → VMT=0x764608 / 0x76B3C8 / 0x767D14 / 0x7730D0
python ysfm_imm.py m2 283C                     → 0x6822F4 `sub cx,0x283C` + M2 自身两处带毒
python ysfm_cfg.py 1006E8D0                    → 切割全体（含 5 个搬迁块）
python ysfm_cfg.py 1006DAB0                    → 自定义伤害全体
```
