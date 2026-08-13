# 缺失魔法技能审计补全报告 (2026-08-13)

底本 `staging/_reunpack_work/flat_image.bin` (base 0x400000)。分支 `w/magic-skills`。
roadmap: `staging/missing_items_implementation_roadmap_20260810.md` §Magic Skills。

## 结论概览

roadmap 列 24 个 SEARCH_FAILURE 技能 ID **全部已在早批实现**（`GameSvr/Spells/MagicManager.cs`
分派 switch + `GameSvr/Actors/TBaseObject.NativeSkill*.cs` handler），本轮**逐一核对确认为真实现**（非空壳
case），跳过。唯一真缺口为 **MAGIC-U0 (ID 200 hijack)** —— 早批标 BLOCKED，本轮完成反汇编取证并给出
可取证部分的 1:1 复刻 + 不可取证部分（TFireFlower 子系统）的 fail-closed。

## 24 个 SEARCH_FAILURE ID 现状（全部已实现）

分派入口：`MagicManager.DoSpell` (`sub_6ED62C`) 内 `switch(UserMagic.MagicInfo.wMagicID)`。

| ID | 现状 | 原生 handler / 语义 | C# 落点 |
|----|------|---------------------|---------|
| 59 | 已实现 | TABLE2 slot 0x6ED81D→0x6EDD27，与 63 共用一条指令流；push targetY/0x258，call sub_76F33C 丢弃结果，永不硬拒 | `TryProduceNativeMagic59` (MagicManager.cs:1694) |
| 62 | 已实现 | 0x6EDC71：30s 冷却闸 + amulet type-1 + VMT+0x124 producer (sub_73EA20 cl=1 edx=2000) | `TryProduceNativeMagic62` (MagicManager.cs:2120) |
| 63 | 已实现 | 同 59（byte-proven alias，slot 0x6ED82D 也持 0x6EDD27） | 同 59 case |
| 66 | 已实现 | 66/67 共用 handler，slot 0x6ED839/0x6ED83D→0x6EDE39→sub_745744，结果反相入 [ebp-6] | `TryActivateNativeSkill66Or67` |
| 67 | 已实现 | 同 66 | 同上 |
| 111 | 已实现 | 0x6EDE4F：冰眼巨魔召唤，10min 冷却，sub_74633C，nExpLevel=10 royalty=300s | `TryActivateNativeSkill111IceEyeTrollSummon` |
| 117 | 已实现 | 常真 stub sub_6EEE34 (558becb0015dc20400)，xor al,1→boSpellFail=0→发 0x27E，静默成功 | case break |
| 118 | 已实现 | 常假 stub sub_6EEE28 (558bec33c05dc20800)，结果被存→硬拒，发 0x27F | `boSpellFail=true` |
| 125 | 已实现 | 常真 stub sub_6EEE40，静默成功 | case break |
| 126 | 已实现 | 常真 stub sub_6EEE4C，静默成功 | case break |
| 127 | 已实现 | 常真 stub sub_6EEE58，静默成功 | case break |
| 128 | 已实现 | 常假 stub sub_6EEE64，结果被存→硬拒 | `boSpellFail=true` |
| 151 | 已实现 | 0x6EDD70→sub_745A20，xor al,1→硬拒；VMT+0x1F4=sub_748288 keyed 冷却查询 key 0x97 | `TryActivateNativeSkill151` |
| 154 | 已实现 | 0x6EDD83→sub_74588C，key 0x9A | `TryActivateNativeSkill154` |
| 167 | 已实现 | 0x6EDEE1→0x6EEE70，结果同时入 boSpellFire/boSpellFail | `TryActivateNativeSkill167Prison` |
| 191 | 已实现 | 0x6EDFCF→TPlayer VMT+0x148=0x6EF340（凝冰），结果反相入 [ebp-6] | `TryActivateNativeSkill191Freeze` |
| 213 | 已实现 | byte-proven alias of 48：slot 0x6ED7F1→0x6EDE0F，0x6ED8C7 sub eax,0x16 je 0x6EDE0F | `MagGroupAmyounsul` (与 GROUPAMYOUNSUL 共用) |
| 231 | 已实现 | 0x6EDE66：amulet 闸 sub_73E93C(edx=1)，成功后 sub_76F8BC 恒 FALSE→清目标+成功（吃符 no-op） | amulet 检查+清目标 |
| 232 | 已实现 | 0x6EDEA7：sub_76F8A8 结果丢弃(jmp 0x6EE04B 不写 [ebp-6])，不拒不清目标 | case break |
| 236 | 已实现 | 0x6EDFB4：sub_76F8B8 恒 FALSE(33c0c3)，仅决定是否清目标→清目标+成功 | `TargeTBaseObject=null` |
| 288 | 已实现 | TPlayer VMT+0x224=0x6ED26C(33c0c3 空)，trampoline 反相存→硬拒 | `boSpellFail=true` |
| 289 | 已实现 | TPlayer VMT+0x228=0x6ED270(带栈帧空体 ret 4)→硬拒 | `boSpellFail=true` |
| 315 | 已实现 | TPlayer VMT+0x234=0x6ED290(33c0c3)→硬拒；0x6ED904 sub eax,0x13B je 0x6EE024 | `boSpellFail=true` |
| 316 | 已实现 | TPlayer VMT+0x238=0x6ED294(33c0c3)→硬拒 | `boSpellFail=true` |

辅助方法均为独立 partial 真实现（非 throw stub），已核验存在：
`GameSvr/Actors/TBaseObject.NativeSkill{111,151,154,66Or67,167Prison,191Freeze}.cs`。

---

## MAGIC-U0 (ID 200 hijack) —— 完整反汇编取证

早批 `staging/magic_200_201_detailed_report.md` 标 BLOCKED（5 个依赖函数未逆向）。本轮完成取证。

### 触发与调用链（byte-proven）

CM_SPELL 消息处理器 `sub_6DAxxxX` 在 0x6DA0DD / 0x6DA153 调用 ClientSpellXY (`sub_6BC510`)：

```asm
006DA0BE  movzx eax,[msg+6]  ; Param  → push (Y)
006DA0C9  movzx eax,[msg+8]  ; Tag    → push (X)
006DA0D1  mov ecx,[msg+0]    ; Recog  → ecx (=花 key, hijacker param3)
006DA0D6  mov dx,[msg+0xa]   ; Series → dx  (=nKey, 与 200 比较)
006DA0DD  call 0x6BC510      ; ClientSpellXY
006DA0E2  test al,al / je 0x6DA104
006DA0E6  ...  dx=0x275  call [VMT+0x250]   ; TRUE  → 成功 ack
006DA104  ...  dx=0x276  call [VMT+0x250]   ; FALSE → 失败
```

ClientSpellXY (`sub_6BC510`) 顶部（GetMagicInfo/skill-forbid 闸之前）先调劫持器：

```asm
006BC52F  call 0x6BCD48      ; hijacker(Self, dx=nKey, ecx=Recog, X, Y)
006BC534  test al,al / je 0x6BC541
006BC538  mov [ebp-5],1      ; result=TRUE
006BC53C  jmp 0x6BCD02       ; 跳过全部正常派发
```

### 劫持器 sub_6BCD48（完整语义）

```asm
006BCD66  mov [ebp-5],0                ; result=false
006BCD6A  cmp dx,0xC8                  ; nKey==200 ?
006BCD6F  jne 0x6BCDF5                 ; 否 → 返回 false（放行正常派发）
006BCD75  mov [ebp-5],1               ; ★ result=TRUE（无条件！之后所有失败分支不重置）
006BCD7E  call 0x73CF08               ; obj = FindFlowerByKey(Self, ecx=Recog)
006BCD87  test ebx,ebx / je 0x6BCDF5  ; 未找到 → 返回 TRUE（无副作用）
006BCD8B  mov edx,[0x7804A4]          ; TFireFlower 类引用 (VMT 0x7804F0)
006BCD91  call 0x404828 (Delphi `is`) ; obj is TFireFlower ?
006BCD98  je 0x6BCDF5                 ; 否 → 返回 TRUE
006BCDA8  call 0x784C78              ; Validate(obj, Self, X, Y)
006BCDAF  je 0x6BCDF5                 ; 否 → 返回 TRUE
006BCDBA  call [Self.VMT+0x268]      ; = 0x73CBAC (见下) 生成 202 号魔法效果
006BCDD0  call 0x784568              ; Format(&s, "0", 1, obj[0x20])
006BCDDF  call 0x768BE0             ; SendMsg(Self, ident=0xB, s)
006BCDE9  call 0x73D140             ; RemoveFlowerFromList(Self, obj)  (sub_425020 TList.Remove)
006BCDF0  call 0x404690             ; obj.Free
006BCDF5  ...                        ; 返回 [ebp-5]
```

依赖语义（已逆向）：
- **sub_73CF08** = 遍历 `[Self+0x508]` TList，按 `item[0x18]==key` 查找并返回对象（sub_424D4C=TList.Items）。
- **类 0x7804F0 = `TFireFlower`**（vmtClassName@0x7804C4 解析，火焰花对象）。工厂在 0x74CD27/0x74CD53
  两处以类引用 0x7804A4 构造 TFireFlower（对象工厂，sub_783788）。
- **Self.VMT+0x268 = 0x73CBAC**：`push 0/1/0; cx=0xCA(=202); edx=obj[0x18]; call [Self.VMT+0x250]`
  —— 以 magic-id **202** + 花的 key 生成魔法效果（对应早批发现的 200→201→202 状态机）。
- **sub_73D140** = `TList.Remove([Self+0x508], obj)`（从花列表移除）。
- **sub_404690** = `TObject.Free`。
- **sub_768BE0** = 发送 ident **0xB** 消息（自定义协议）。
- **sub_784C78** = 位置/状态校验（obj, Self, X, Y）。

### 结论与实现判定

**可取证 1:1 复刻的部分（本轮已实现）：**
劫持契约 = 「nKey==200 → 无条件 result=TRUE，跳过正常魔法派发」（0x6BCD75 在任何花查找之前
即置 result=1，所有失败分支 `je 0x6BCDF5` 均不重置）。**所有副作用（生成 202 效果 / ident-0xB 消息 /
移除+释放花）全部位于「找到匹配 TFireFlower」分支内。**

**fail-closed 的部分（不臆造）：**
副作用依赖整套 **TFireFlower 对象子系统**，C# 端完全未建模：
1. `TFireFlower` 类与每玩家 `[player+0x508]` 花列表；
2. 花对象工厂创建点（0x74CDxx）；
3. VMT+0x250 的 magic-202 效果生成 / 200→201→202 状态机；
4. ident-0xB 自定义协议；
5. 连**输入管线**都缺：C# `ClientSpellXY` 签名不含 Recog（花 key，native ecx）——
   `TPlayObject.Message.cs:1787` 仅传 wParam(nKey)/nParam1/nParam2/nParam3(→目标对象)。

**关键等价性**：在「无任何 TFireFlower 被放置」的世界里（正是 C# 端现状），native 劫持器的行为
**恰好等于**：返回 TRUE、无副作用（sub_73CF08 返回 nil → 直接跳 epilogue）。故本轮实现
「nKey==200 → return true（swallow）」是对当前子系统状态下 native 行为的**精确 1:1 复刻，非臆造**。
detonation 分支以证据注释 fail-closed，待 TFireFlower 子系统移植后再启用。

若不复刻此 swallow，C# 现状为：`GetMagicInfo(200)` 多半 null → ClientSpellXY 返回 false →
客户端收到失败响应（native 为成功 ack 0x275），属可观测偏差。

**落点**（最小化、放独立 partial）：
- `GameSvr/Players/TPlayObject.NativeMagic200Hijack.cs`（新增，含本 handler + 全部证据注释）；
- `TPlayObject.Attack.cs::ClientSpellXY` 在 skill-forbid 闸（IsSkillAllowedAt）之前加一处 guard 调用
  （对应 native 劫持器在 sub_772A50 之前的顺序）。
