# 眼神 A 类外科修复 · 第二批（ysa2）

- 日期：2026-08-14
- 工作树：`D:\loym2\.claude\wt3\ysa2`　分支：`w/ysa2`　基线：`69f049b6`（建树时的 `master`）
- 上游：`docs/yanshen_completeness_audit_20260814.md` §5 A 类、`docs/ys_aclass_surgical_20260814.md`（第一批）
- 底本：
  - 插件 `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin`（base `0x10000000`）
  - 宿主 `staging/_reunpack_work/flat_image.bin`（base `0x400000`）
- 工具：python 3.11 + capstone 5.0.7。本轮新写的只读脚本都在 `%TEMP%\ysa2\`：
  `ysa2lib.py`（镜像读取 + 快速对齐）、`ysstub2.py`（桩体重建）、`ysfeat.py` / `ysmaster.py`
  （安装臂证据提取）、`yshostfn.py`（rel32 调用目标普查→宿主函数边界）、`yscallers.py`、
  `ysjt.py`（跳转表）、`ysinvent.py` + `yslabelproof.py`（反臆造普查）。

---

## 0. 结论先行

| 项 | 值 |
|---|---|
| 本轮队列（第一批未覆盖的 A 类） | **8 条** |
| 已落地（FIXED，接线并通过审计） | **3 条** —— `攻击触发` / `魔法攻击触发` / `盘古魔法攻击触发` |
| 已在 master 上被他人接线（复核为 FAITHFUL） | **3 条** —— `被击杀触发` / `捡物触发` / `英雄倍攻和暴击` |
| 仍 BLOCKED（但卡点已收窄成硬事实） | **2 条** —— `心灵启示触发` / `刀刀切割` |
| 本轮清除的自造项 | **0 条**（21 条触发键名 + 21 个脚本标签逐条字节验证通过） |
| 附带更正 | 2 处（`刀刀切割` 的 C# 落点错记；审计唯一那条 INVENTED 是扫描器假阳性） |
| 严格完成度 | `45.2% → 50.2%`（其中本轮 `+0.45pp`，余者为 `186ef170..69f049b6` 间他人工作） |

提交：

| SHA | 说明 |
|---|---|
| `5bcc9333` | 接线 `@MyAttack` / `@MyMagicAttack` / `@MagicAttack` |
| `08bf601f` | 收窄 `@Revelation` 与 `@Cutting` 的卡点；更正 `@Cutting` 的 C# 落点 |

---

## 1. 工作队列：第一批未覆盖的 A 类剩余条目

第一批（`ys_aclass_surgical_20260814.md`）对 28 条 A 类逐条裁决，其 §2.5 明确把 8 条记为
**「未解（桩体未重建或落点未定）」**，并写「重建这 8 条是纯机械工作，只是本轮没排上」。
这 8 条就是本轮队列，逐条如下（安装点取自 `docs/ys_patch_label_atlas.tsv`）：

| # | 键 | 宿主挂载→续跑 | 安装器站点 | 第一批状态 |
|---|---|---|---|---|
| 1 | 心灵启示触发 | `0x6EDC2B → 0x6EDC30` | `0x100AE7F5`(tramp2) / `0x100AE93D`(memcpy) | 桩体未重建，2 参未解 |
| 2 | 被击杀触发 | `0x766624 → 0x766629` | `0x100D26FD` / `0x100D282C` | 同上（2 参） |
| 3 | 捡物触发 | `0x6B770C → 0x6B7711` | `0x100D2BA2` / `0x100D2CD4` | 同上（2 参） |
| 4 | 攻击触发 | `0x76E35D → 0x76E362` | `0x100D2D50` / `0x100D2E7F` | 同上（4 参） |
| 5 | 魔法攻击触发 | `0x76DE84 → 0x76DE8A` | `0x100D2F0B` / `0x100D305F` | 同上（5 参） |
| 6 | 盘古魔法攻击触发 | `0x76E1AF → 0x76E1B6`、`0x76DEC0 → 0x76DEC7` | `0x100ADDBF` / `0x100ADE0E`（+2 memcpy） | 同上（3 参） |
| 7 | 英雄倍攻和暴击 | `0x76C816 → 0x76C81D` | `0x100D49B4` / `0x100D4AF9` | 模板 354 元素，未走完 |
| 8 | 刀刀切割 | `0x767BAE → 0x767BB4` | `0x100CF36E` / `0x100CF496` | 703 dword 大桩体 |

**队列开工前的一处修正**：审计（基线 `186ef170`）把这些键统计为 `Wired=false`，但建树时的
`master`（`69f049b6`）上，#2 / #3 / #7 已被他人接线。本轮对这三条只做证据复核，不重复接线。

**8 条桩体本轮全部重建成功**（`ysstub2.py`，逐条给出字节数）：

```
心灵启示触发     88 元素 →  92 B     被击杀触发   196 元素 → 209 B
捡物触发        140 元素 → 153 B     攻击触发     206 元素 → 222 B
魔法攻击触发     255 元素 → 277 B     盘古魔法#1   143 元素 → 150 B
盘古魔法#2      140 元素 → 147 B     英雄倍攻和暴击 354 元素 → 361 B
刀刀切割        703 元素 → 707 B
```

每对安装点里**后一个是 `0x10033340` 裸 memcpy 的还原臂**，它写回的字面量就是被覆盖的原始
宿主字节 —— 这给了一个独立于桩体的交叉校验：桩体是否重放这些字节，直接决定
Notify / Replace 的归类。逐条对照全部自洽：

| 键 | 还原臂写回的原始字节 | 反汇编 | 桩体是否重放 | 归类 |
|---|---|---|---|---|
| 心灵启示触发 | `E8 F4 67 08 00` | `call 0x774424` | **否** | Replace（顶掉） |
| 被击杀触发 | `8B 45 FC 8B 10` | `mov eax,[ebp-4]` / `mov edx,[eax]` | 是（开头） | Notify |
| 捡物触发 | `8B 55 FC 8B C3` | `mov edx,[ebp-4]` / `mov eax,ebx` | 是（开头） | Notify |
| 攻击触发 | `68 C8 00 00 00` | `push 0xC8` | 是（结尾） | Notify |
| 魔法攻击触发 | `8B F0 85 F6 7E 2C` | `mov esi,eax` / `test esi,esi` / `jle` | 是（拆两半） | Notify |
| 盘古魔法 #1/#2 | `80 BE B6 01 …` / `80 BB B6 01 …` | `cmp byte [esi\|ebx+0x1B6],0` | 是（结尾） | Notify |
| 英雄倍攻和暴击 | `83 BB 84 00 00 00 00` | `cmp dword [ebx+0x84],0` | 是（结尾） | Notify（带改写） |
| 刀刀切割 | `53 56 57 89 4D F8` | `push ebx/esi/edi` / `mov [ebp-8],ecx` | 是（结尾） | Notify（带改写） |

---

## 2. 方法学：本轮真正解开卡点的那一步

第一批把 #4/#5/#6 卡在同一句话上：**「宿主函数未定名、`[ebp-4]`/`[ebp-8]` 语义未定」**，
并记「`0x76DA00..0x76DE90` 无可辨函数边界」。

那句话不成立。用**全镜像 rel32 调用目标普查**（`yshostfn.py`：扫描整幅 17.6 MB 宿主镜像里
每一条 `E8 rel32`，把落点收集起来 —— 落点即函数入口）立刻得到边界：

```
call targets in [0076D800,0076E800):
  0076DE1C  callers=1    bytes=558BEC83
  0076DF5C  callers=1    bytes=558BEC83
  0076E0B4  callers=1    bytes=558BEC83
  0076E268  callers=29   bytes=558BEC83
```

三个单调用者函数的调用点 `0x766E06` / `0x766E36` / `0x766E6A` 都落在
`sub_766878` 内，而 `sub_766878` 由它自己引用的异常串
`[Exception]:TCreature.GetAndExecMsg1=` / `…2=` 定名为 **`TCreature.GetAndExecMsg`**。
分发臂：

```
00766DA0  sub edx,4 / jne 0x766E10        → 0x766E06 call sub_76DE1C     (category 4)
00766E10  cmp ax,2  / jne 0x766E40        → 0x766E36 call sub_76DF5C     (category 2)
00766E40  cmp ax,3  / jne 0x767166        → 0x766E6A call sub_76E0B4     (category 3)
```

这与 C# 的 `TBaseObject.ProcessNativeMagicEffectMessage` 的 `switch (message.wParam)`
逐路对上（`1/5 → Single`、`2 → Line`、`3 → Area`，另有 `4` 走
`ApplyNativeDirectMagicEffect`）。三个宿主函数的 C# 端口因此确定：

| 宿主 | 调用者 | C# 端口 |
|---|---|---|
| `sub_76DE1C` | `0x766E06`（唯一） | `TBaseObject.ApplyNativeSingleMagicEffect` |
| `sub_76E0B4` | `0x766E6A`（唯一） | `TBaseObject.ApplyNativeAreaMagicEffect` |
| `sub_76E268` | 29 个 | `TBaseObject.ApplyNativeDirectMagicEffect` |

第三个不是新发现 —— `TBaseObject.NativeState26Effects.cs` 早就按
`0x76E284 / 0x76E2B2 / 0x76E357 / 0x76E35D` 逐行注释移植了它，**只是没人把它和触发键连起来**。

---

## 3. 逐条判定

### 3.1 攻击触发 `@MyAttack` —— **FIXED（已落地）**

- 键名证据：状态标签 `0x102C6E58 = '攻击触发(已启动)'`、`0x102C6E6C = '攻击触发(未启动)'`
  （安装臂 `0x100D2E19` / `0x100D2E9F` 各 `call 0x100F018C`）。
- 标签证据：安装器栈上现搭 —— `mov dword [ebp-0x2B8],0xFFFFFFFF` / `len 9` /
  `0x41794D40`(`@MyA`) / `0x63617474`(`ttac`) / `0x6B`(`k`) ⇒ **`@MyAttack`**。
- 宿主帧（`sub_76E268` 序言，逐字节）：

```
0076E271  894DFC     mov [ebp-4],ecx
0076E274  8BF2       mov esi,edx        ; 被打者
0076E276  8BD8       mov ebx,eax        ; 攻击者
0076E2A9  FF9704010000  call [edi+0x104]   ; = target.ResolveFullMagicDamage
0076E2AF  8945F8     mov [ebp-8],eax     ; [ebp-8] = 伤害
0076E357  837DF800   cmp [ebp-8],0
0076E35B  7E11       jle 0x76E36E
0076E35D  68C8000000 push 0xC8           ; 【钩子】
0076E362  8B4DF8     mov ecx,[ebp-8]
0076E369  E88AD1FFFF call 0x76B4F8       ; 落延时受击消息
```

- 桩体 222 B，**无门**，四个 Variant：

```
+01C 8B55F8   mov edx,[ebp-8]  / +01F B1FC mov cl,0xFC / +021 call 0x41AFE4   ; ① 伤害
+03D 8D9606010000 lea edx,[esi+0x106] / +043 call 0x405774                    ; ② 被打者名
+06A 8B9328010000 mov edx,[ebx+0x128] / +070 8B5248 mov edx,[edx+0x48]        ; ③ 攻击者 sMapDesc
+08F C7459C02000000 mov [ebp-0x64],2   ; varSmallint
+096 813EC8C86A00   cmp [esi],0x6AC8C8
+0A2 C745A401000000 mov [ebp-0x5C],1                                          ; ④ 被打者是玩家 1/0
+0A9 8BD3 mov edx,ebx  / +0B2 6A03 push 3(高位下标) / +0CA FF5348 call [ebx+0x48]
+0D4 68C8000000 push 0xC8  / +0D9 jmp 0x76E362                                ; 重放 → Notify
```

  `cl=0xFC` 是有符号 `-4`，Delphi `_VarFromInt` 的 `Range` 约定里 `-4` = 有符号 4 字节 =
  `varInteger`；与另两个手搓 Variant 的 `VType=2`(varSmallint)/`3`(varInteger) 并存自洽。
- **落地**：`ApplyNativeDirectMagicEffect` 的 `if (damage > 0)` 内、`SendDelayMsg` 之前。

### 3.2 魔法攻击触发 `@MyMagicAttack` —— **FIXED（已落地）**

- 键名：`0x102C6E80 = '魔法攻击触发(已启动)'` / `0x102C6E98 = '(未启动)'`。
- 标签：`len 14` / `0x4D794D40`(`@MyM`) `0x63696761`(`agic`) `0x61747441`(`Atta`) `0x6B63`(`ck`)
  ⇒ **`@MyMagicAttack`**。
- **`[ebp-8]` 的身份**（第一批的核心卡点）由这一串定死：

```
0076DE24  894DF8     mov [ebp-8],ecx
0076DE27  8955FC     mov [ebp-4],edx        ; 被打者
0076DE7E  FF9604010000  call [esi+0x104]    ; = target.ResolveFullMagicDamage
0076DE84  8BF0       mov esi,eax            ; 【钩子】esi = 伤害
0076DE86  85F6 7E2C  test esi,esi / jle 0x76DEB6
0076DEAB  668B55F8   mov dx,word [ebp-8]
0076DEB1  E8B2450000 call 0x772468          ; = ConsumeNativeOneShotMagicDamage(payload.SkillId)
```

  `0x772468` 的 C# 端口是 `ConsumeNativeOneShotMagicDamage(payload.SkillId)`，而它的唯一实参
  就是 `word [ebp-8]` ⇒ **`[ebp-8]` = SkillId**。
- 桩体 277 B，五个 Variant = 伤害 / 被打者名 / 施法者 `sMapDesc` / 被打者是玩家 / SkillId；
  `push 4`（高位下标 4 ⇒ 5 个元素）。尾部把 `test esi,esi / jle` 用两条 jmp 复现。
- **落地**：`ApplyNativeSingleMagicEffect` 里 `ResolveFullMagicDamage` 返回之后、
  `if (damage > 0)` 之前 —— **伤害为 0 或负也发**，这是桩体位置决定的，不是选择。

### 3.3 盘古魔法攻击触发 `@MagicAttack` —— **FIXED（已落地）**

第一批的卡点原话是「第一个站点 `0x76E1AF` 的桩体缓冲基址尚未解出来，**无法确认两处参数向量
完全一致**」。本轮两个站点都重建了，逐槽对照：

| | 站点 #1 `0x76E1AF`（`sub_76E0B4`） | 站点 #2 `0x76DEC0`（`sub_76DE1C`） |
|---|---|---|
| This_Player | `mov edx,esi` → esi = 施法者 | `mov edx,ebx` → ebx = 施法者 |
| 被打者 | `ebx`（循环里的当前目标） | `[ebp-4]` |
| ① varString `0x100` | `lea edx,[ebx+0x106]` → `call 0x405774` | `lea edx,[[ebp-4]+0x106]` → 同 |
| ② varSmallint `2` | `cmp [ebx],0x6AC8C8` → 1 | `cmp [[ebp-4]],0x6AC8C8` → 1 |
| ③ varInteger `3` | `mov eax,[ebp+0x14]` | `mov eax,[ebp-8]` |
| 数组 | `push 2`（3 元素） | `push 2`（3 元素） |
| 尾部重放 | `cmp byte [esi+0x1B6],0` | `cmp byte [ebx+0x1B6],0` |

③ 看着不同源，其实是同一个量 —— 回到调用者就闭合了：

```
00766DFD  0FB74B24  movzx ecx,word [ebx+0x24]   ; → sub_76DE1C 的 [ebp-8]
00766E04  8BC7      mov eax,edi
00766E06  E811700000 call 0x76DE1C
...
00766E4F  0FB74324  movzx eax,word [ebx+0x24]   ; → sub_76E0B4 的 [ebp+0x14]
00766E50  50        push eax
00766E6A  E845720000 call 0x76E0B4
```

**同一个 `[ebx+0x24]`。参数向量确认完全一致 ⇒ 卡点解除。**
`sub_76E0B4` 的 `[ebp-4]`/`[ebp-8]` 则是 X/Y（`0x76E14B cmp [tgt+0x12C],[ebp-4]` /
`0x76E156 cmp [tgt+0x130],[ebp-8]` 就是 `isCenter` 的判据），与 C# 的
`isCenter = target.m_nCurrX == payload.X && target.m_nCurrY == payload.Y` 逐条对上。

- 键名：`0x102C4F84 = '盘古魔法攻击触发(已启动)'` / `0x102C4FA0 = '(未启动)'`。
- 标签：`len 12` / `0x67614D40`(`@Mag`) `0x74416369`(`icAt`) `0x6B636174`(`tack`) ⇒ **`@MagicAttack`**。
- **落地**：两处各一次 —— `ApplyNativeSingleMagicEffect` 的 `if (payload.Arg0)` 内、
  `ApplyNativeAreaMagicEffect` 的 `if (isCenter)` 内，都在 `TryApplyNativeState26Single` 之前
  （桩体尾部重放的那条 `cmp byte [+0x1B6],0` 正是 state-26 臂的第一条指令）。

### 3.4 被击杀触发 / 捡物触发 / 英雄倍攻和暴击 —— **FAITHFUL（复核通过，未改动）**

三条已由 `YanshenTriggerDispatch.Wave2.cs` 接线。本轮独立重建桩体复核，全部与既有注记吻合：

- `@MyKill`：标签 `len 7` / `0x4B794D40`(`@MyK`) `0x6C69`(`il`) `0x6C`(`l`) ✓；
  三道门 `cmp edx,0x6AC8C8` / `cmp ebx,0x400000` / `cmp [ebx],0x6AC8C8`，
  `ebx = [victim+0x34C]` = m_ExpHitter ✓。
- `@pickpre`：标签 `len 8` / `0x63697040`(`@pic`) `0x6572706B`(`kpre`) ✓，**且 `.rdata 0x10310A74`
  另有一份连续常量**（21 条里唯一一条两种形态都在的）；无门 ✓。
- `@Herobaoji`：标签 `len 10` / `0x72654840`(`@Her`) `0x6F61626F`(`obao`) `0x696A`(`ji`) ✓；
  类门排除 `0x6AC8C8`/`0x660E80`、只放行 `0x685CA0`/`0x685968`/`0x685FD8` ✓；
  银行先验槽 `key [bank+0x180]==0x419 && value [bank+0x184]==0x522` ✓。

  顺带：这条桩体和 §3.6 的刀刀切割桩体**互相独立地**印证了 S 银行的布局 ——
  槽 `i-1` 在 `(i-1)*8`，`key` 在 `+0`、`value` 在 `+4`：
  `0x180 → key 0x419 = 1049 = S(1,49)`，`0x200 → key 0x429 = 1065 = S(1,65)`，
  `(1065-1049)*8 = 0x80 = 0x200-0x180` ✓。

### 3.5 心灵启示触发 `@Revelation` —— **BLOCKED（卡点已收窄成硬事实）**

第一批的卡点是「分发器起点未定名、`[ebp-4]` 与 `[ebp-0xC]` 的语义未反出来」。本轮全部反出来了，
结论却是**这条根本不该接**。

- 键名：`0x102C5044 = '心灵启示触发(已启动)'` / `0x102C505C = '(未启动)'`。
- 标签：`0x100AE824` 起 `refcnt=-1` / `len 0x0B` / `0x76655240`(`@Rev`) `0x74616C65`(`elat`)
  `0x6F69`(`io`) `0x6E`(`n`) ⇒ **`@Revelation`**（11 字符）。
- 分发器定名：**`sub_6ED62C`**（全镜像唯一 rel32 调用者 `0x6BCCB3`）：

```
006ED635  894DFC        mov [ebp-4],ecx
006ED638  8BF2          mov esi,edx          ; TUserMagic
006ED63A  8BD8          mov ebx,eax          ; 施法者
006ED676  E80D280A00    call 0x78FE88        ; GetDistance(eax=[ebp-4], edx=[ebp+0xC],
                                             ;             ecx=[ebx+0x12C], push [ebx+0x130])
006ED6FF  FF248506D76E00 jmp dword [eax*4+0x6ED706]   ; 按 wMagicID 的跳转表
```

  ⇒ **`[ebp-4]` = 目标 X、`[ebp+0xC]` = 目标 Y**。跳转表第 28 项 = `0x6EDC24`，正是本挂载点所在臂，
  而 C# `SpellsDef.SKILL_SHOWHP == 28` —— 与键名「心灵启示」吻合。
- 被顶掉的 `0x774424` 也解开了：`Random(100) < (magicLevel+1)*5+10` 命中则对目标
  `call 0x76B4D0(dl=0x1D)` 挂 state 29。
- **新卡点（硬事实）**：第二个 Variant 取的 `[ebp-0xC]`，在通往臂 28 的整条路径上**从未被写过**。
  序言 `0x6ED62C..0x6ED6FF` 只写 `[ebp-4]` 与 `[ebp-8]` 那个 dword 的四个字节
  （`[ebp-5]=0` / `[ebp-6]=0` / `[ebp-7]=1` / `[ebp-8]=0`）；`[ebp-0xC]` 的唯一写点是
  `0x6ED956`（臂 6 的 `call 0x75EC20` 取物品），与臂 28 经跳转表**互斥**。

  ⇒ 原生在这里读的是**未初始化的栈残值**。C# 不存在等价物，填任何值都是臆造。
  这是原生自身的缺陷，不是移植缺口 —— **保持不发射**。

### 3.6 刀刀切割 `@Cutting` —— **BLOCKED（但更正了一处错记，并解出全部银行槽）**

- 键名：`0x102C6CB0 = '刀刀切割(已启动)'` / `0x102C6CC4 = '(未启动)'`。
- 标签：`0x100CF39D` 起 `refcnt=-1` / `len 8` / `0x74754340`(`@Cut`) `0x676E6974`(`ting`)
  ⇒ **`@Cutting`**。
- **更正 ①：C# 落点错记。** 注册表原写「C# 落点是刀刀切割伤害就地计算处（YanshenApi 切割实现，
  非原生 StruckDamage 链）」。实际 `0x767BAE` 是 **`sub_767BA8` 的函数入口**：

```
00767BA8  55 8BEC 83C4EC   push ebp / mov ebp,esp / add esp,-0x14   ; 6 B 序言
00767BAE  53 56 57 894DF8  push ebx/esi/edi / mov [ebp-8],ecx       ; 【被覆盖的 6 B】
00767BB4  894DFC           mov [ebp-4],ecx                          ; 续跑点
```

  桩体改写 `ecx` 后重放那 6 字节再跳 `0x767BB4`，所以改写值**同时**落进 `[ebp-8]` 与 `[ebp-4]`。
  而 `sub_767BA8` 早已由 `YanshenApi.cs:1083` 定名为「致命一击调制」，其 C# 端口就是
  **`TBaseObject.ApplyNativePhysicalCritical(source, damage)`** —— 逐槽吻合：

  | 原生 | C# |
  |---|---|
  | `word [edx+0x194]` | `source.m_sNativeCriticalChance` |
  | `word [eax+0x19C]` | `m_sNativeAntiCriticalChance` |
  | `word [eax+0x1A0]` | `m_sNativeCriticalDamageReduction` |
  | `dword [edx+0x198]` | `source.m_nNativeCriticalDamageIncrease` |
  | `0x767CA4=100.0` / `0x767CA8=10000.0` / `0x767CAC=1.5` | 同名三个常量 |

  ⇒ **切割加成加在致命一击倍率之前**。落点是明确的，缺的不是落点。
- 707 字节桩体的**十个 S 银行槽全部解出**（按 §3.4 证实的布局换算）：

```
PvE 支  [+0x4C]=S(1,10) 千分比 × [target+0x2B0](MaxHP)   [+0x19C]=S(1,52) 概率门
        [+0x54]=S(1,11) 定值                             [+0x1EC]=S(1,62) 概率门
PvP 支  [+0x44]=S(1,9)（==100 则免疫）                    [+0x18C]=S(1,50) 千分比
        [+0x1A4]=S(1,53) 概率门                          [+0x194]=S(1,51) 定值
        [+0x1F4]=S(1,63) 概率门
派发门  key [+0x200]==0x429 且 value [+0x204]==100  = S(1,65)
```

- **仍 BLOCKED 的两条**：
  1. 概率门不是 `Random`，而是 `((([atk+0x18] & 0xFFF) + [atk+0x470]) & 0xFFF)` 这个由**对象字段
     合成的伪随机数**（`+0x18` 与 `+0x470` 两个字段 C# 无模型）；
  2. 整条链前置 `S(1,65)==100`，依赖 A6 的 `S(1,1..150)` 播种，而第一批已判「播种时机不可证」，
     至今未实现 —— 门永远不成立。

  任一未解都会把**每一刀的伤害**算错，故 fail-closed。

---

## 4. 反臆造自查

按要求把本轮涉及的每一个键名 / 命令名 / 标签，逐个回到 DLL 字节里验证。做法不是搜连续字符串
（会漏 —— 见下），而是两条独立路径普查（`ysinvent.py` + `yslabelproof.py`）。

### 4.1 配置键名：21 / 21 通过

从镜像里普查所有 `<键名>(已启动)` / `(未启动)` GBK 状态标签串，得到插件自报的
**263 个配置键**。注册表 21 条的 `ConfigKey` **逐条命中**，无一自造。

### 4.2 脚本标签：21 / 21 通过

**这里有个必须写下来的陷阱**：21 个标签里只有 **8 个**在 `.rdata` 里有带合法 Delphi 头的连续
ASCII 常量（`@HeroEquiepchange` `@Herobaoji` `@MyAttack` `@MyEquiepchange` `@MyKill`
`@MyMagicAttack` `@OnDie` `@baoji`）。**其余 13 个**由安装器在**栈上现搭**成 AnsiString
（`mov [ebp-A],-1` / `len` / 若干 `mov dword|word|byte` 字符块），
**只搜连续字符串会得到 13 个假阴性**（本轮第一遍普查就正好漏报了其中 8 个）。
正确判据是找那串 `mov` 立即数并核对长度头。

逐条证据（`group0 → group1` 地址相邻 + 长度头匹配）：

| 标签 | group0 | group1 | 长度头 | 结论 |
|---|---|---|---|---|
| `@BBupr` | `@BBu`@`0x100D485D` | `pr`@`0x100D486E` | `len 6` | ✓ |
| `@BBKill` | `@BBK`@`0x100D4BBE` | `il`+`l`@`0x100D4BC8/D1` | `len 7` | ✓ |
| `@initys` | `@ini`@`0x100D59B1` | `ty`+`s`@`0x100D59BB/C4` | `len 7` | ✓ |
| `@OnDig` | `@OnD`@`0x100AE12A` | `ig`@`0x100AE135` | `len 6` | ✓ |
| `@OnDia` | `@OnD`@`0x100D1E2F` | `ia`@`0x100D1E40` | `len 6` | ✓ |
| `@Revelation` | `@Rev`@`0x100AE83E` | `elat`@`0x100AE848` | `len 11` | ✓ |
| `@Cutting` | `@Cut`@`0x100CF3B7` | `ting`@`0x100CF3C7` | `len 8` | ✓ |
| `@pickpre` | `@pic`@`0x100D2BEB` | `kpre`@`0x100D2BFB` | `len 8` | ✓（另有 `.rdata 0x10310A74`） |

其余 13 条（`@SummonShinsu` `@SummonSkele` `@HeroEquiepchange` `@MyEquiepchange` `@OnDie`
`@OnBackButton` `@MyKill` `@MyAttack` `@MyMagicAttack` `@ChangeEquip` `@MagicAttack`
`@baoji` `@Herobaoji`）由栈搭普查或 `.rdata` 常量表直接命中。

### 4.3 本轮发现并清除的自造项：**0 条**

没有需要删的。为免这一节被读成「没查」，把普查的两个反向面也写明：

- 插件 `.rdata` 里共 **27 个 `@` 常量**，其中 **19 个**（`@Attack` `@DoMySkill`
  `@DoMySkill_plus` `@MyAttract` `@MyBlocking` `@MyCutting` `@MyFanShang` `@MyHeroAttack`
  `@MyHeroMagicAttack` `@MyHuSheng` `@Myattacked` `@Mymonitems` `@NewCutting` `@New_BBKill`
  `@OnKill` `@SetNoKillMapLv` `@Super_Attack` `@my_shanghai_1` `@plus_ChangeEquip`）
  **不在注册表里** —— 这是「原生有、C# 未登记」的缺口方向，**不是**自造，登记于此供后续排期。
- 插件自报 263 个配置键，注册表只覆盖 21 个；两个集合是包含关系，无越界项。

### 4.4 附带更正：审计那条唯一的 INVENTED 是假阳性

审计 §0.5 报 `道士合击系数_数值`（`YanshenFixedReplicaPanels.cs:484`）为 INVENTED。
实际该行是 `AddValue("道士合击系数_数值" + index, …)`，`index` 跑 1..5，
运行期生成的是 `道士合击系数_数值1..5`，而这五个键在转储里**都在**
（`0x102B13F4 / 0x102B1408 / 0x102B141C / 0x102B1430 / 0x102B1444`）。
矩阵生成器看不穿字符串拼接才误报。**代码无需改动**，审计的 INVENTED 计数应记 0。

---

## 5. 仍 BLOCKED 项与卡点

| 条目 | 卡点（本轮收窄后） | 解法 |
|---|---|---|
| 心灵启示触发 | 第二个 Variant 读 `sub_6ED62C` 的 `[ebp-0xC]`，该槽在臂 28 路径上**从未初始化**（唯一写点 `0x6ED956` 属互斥的臂 6）。原生读栈残值。 | 无解 —— 这是原生缺陷，C# 无等价物。除非产品上接受「传 0」这一明确的非 1:1 折衷，否则应永久保持不发射。 |
| 刀刀切割 | ①概率门是 `((([atk+0x18]&0xFFF)+[atk+0x470])&0xFFF)`，两个字段 C# 无模型；②前置 `S(1,65)==100` 依赖未实现的 `S(1,1..150)` 播种（A6，时机不可证）。 | 先定名 `[obj+0x18]`/`[obj+0x470]`（宿主镜像内定点反演即可，不需要新转储）；A6 需要一份带 VM 段的转储才能定播种时机。 |

另记一条不属本轮队列、但本轮证据触及的事：`sub_76E268` 的 `0x76E2BC`
（`cmp byte [ebx+0x1B4],0`）正是 A3「武器绿毒 / 物功带毒」的挂载点，而
`ApplyNativeDirectMagicEffect` 的 `if (arg0) TryApplyNativeState26Direct(target)`
就是它的 C# 端口 —— 与第一批 §3.4 对 `94ede6dd` 的复核请求方向一致，本轮不动。

---

## 6. 严格完成度更新

本轮在 `w/ysa2` 上重跑 `tools/ys_gui_matrix.py`（同一把尺子、同一份生产 config）：

```
keys 380   IMPLEMENTED 210   SCRIPT_ONLY 20   LABEL_ONLY 149   MISSING 1   INVENTED 1
prod-on 215   prod-on w/o behav 57   prod-on script-only 9
```

对照审计基线 `186ef170` 的 `IMPLEMENTED 184 / LABEL_ONLY 175 / 生产开启无行为 71`。

再套用审计 §2.2 的降级规则（注册表 `Wired=false` 且行为文件只有注册表的键降 PARTIAL）：
当前 21 条里 6 条未接（`英雄穿戴触发` `新穿戴触发` `上线触发` `复活触发脚本` `心灵启示触发`
`刀刀切割`），其中 `刀刀切割` 另有真实落点故保留 IMPLEMENTED ⇒ **降级 5 条**（审计当时是 12 条）。

| 面 | 条目 | 审计 FAITHFUL | 本轮 FAITHFUL |
|---|---:|---:|---:|
| F1 配置键 | 380 | 172 | **205**（210 − 5） |
| F2 脚本 API | 125 | 39 | 39 |
| F3 S 变量 | 34 | 2 | 2 |
| F4 回收 | 28 | 24 | 24 |
| F5 协议 | 21 | 20 | 20 |
| F6 命令隧道 | 72 | 41 | 41 |
| **合计** | **660** | **298** | **331** |

- 严格有据完成度：`331 / 660 = **50.2%**`（审计 45.2%）
- PARTIAL `34 → 27`，MISSING `213 → 187`，FAIL-CLOSED 2 与 UNPROVEN 113 未变
  （校验 `331+27+187+2+113 = 660` ✓）

**其中属于本轮的是 3 条**（`攻击触发` / `魔法攻击触发` / `盘古魔法攻击触发`）：
同一棵树上把这三条的接线摘掉重算是 `328/660 = 49.7%`，接上是 `331/660 = 50.2%`
⇒ **本轮 +0.45pp**；`45.2% → 49.7%` 的那 4.5pp 是 `186ef170..69f049b6` 之间其他分支的成果。

区间口径同步收窄：`UNPROVEN-IMPL` 仍是 98 条未动，故真值区间由审计的
`45.2% ~ 60.0%` 变为 **`50.2% ~ 65.0%`**；把 98 条全算未成的最保守口径为
`(331+2+15)/660 = 52.7%`。

---

## 7. 审计工具

`dotnet run --project AuditTools/YanshenTriggerDispatchCheck` ⇒ **PASS**
（`注册表 21 个触发点，已接通 15 个`；接线前为 12）。期望表已同步三条 `Wired` 翻转。
`GameSvr` 构建 0 错误。
