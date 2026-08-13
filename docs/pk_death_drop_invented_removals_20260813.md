# 死亡爆装 / PK 链：INVENTED 清除、原生缺陷照抄、爆率分母定案（2026-08-13 第二轮）

镜像 `D:/loym2/staging/_reunpack_work/flat_image.bin`（17,661,952 字节），ImageBase `0x400000`。
脚本 `staging/_pkd2/`（只读）：`m2dis.py` 反汇编、`zeroscan.py` 多编码零命中、
`scan_race10.py` / `scan_gold.py` / `scan_golddrop.py` / `scan_18c.py` / `scan_fields2.py` /
`dump_73d500.py` / `find_switch.py` / `decode_attrtable.py` / `finish_chain.py`、
`bracecheck.py`（剥注释与字符串的括号平衡检查，代替编译）。

本轮执行 `REPLICATION_RULES.md` §3.1 的用户裁决：**原版的缺陷照抄；原版没有、但删掉玩家会
当场吃亏的保护性代码属于 INVENTED，要删。**

前两份报告：`docs/pk_death_drop_chain_20260813.md`、`docs/m_ident_pk_death_drop_20260813.md`。
本轮把它们登记的 6 处 INVENTED、2 处待裁决、1 条 BLOCKED 全部处理完，另外顺手发现并处理了
3 处同类项。共 11 个提交，每项一个，可单独回退。

---

## 0. 扫描器与阳性对照

零命中扫描对每个名字跑三路：**GBK**、**裸 ASCII**（纯 ASCII 名走大小写不敏感）、**UTF-16LE**。
同一次运行里的阳性对照（证明扫描器不是在静默返回 0）：

| 对照名 | 命中数 | VA |
|---|---|---|
| `ONLYDROPSPEC` | 2 | `0x775FDC`、`0x776F54` |
| `LIMITBAGITEMDROP` | 3 | `0x6983C4`、`0x775FF4`、`0x776F6C` |
| `FREEPK` | 2 | `0x775C68`、`0x776B8C` |
| `GuildPK` | 3 | `0x776EF0`、`0x77D2BD`、`0x77D535` |
| `MyPKpoint` | 2 | `0x6AD2BF`（RTTI）、`0x72C350` |

这些 VA 与前两份报告独立给出的地址一致。

---

## 1. 六处 INVENTED 的删除（任务一）

### PKD-12 `boPKLevelProtect` 新人保护段 — `45bdd75f`

`sub_6C175C` 全函数 255 字节（`0x6C175C..0x6C185A`），在 `0x6C17B1` 的免战门与 `0x6C182C`
的三秒门之间**只有两条**等级梯，门槛都是同一个立即数 `0x14`：

```
6C17B1  80 BB C4 04 00 00 00 / 75 72   [self+0x4C4] != 0 -> 两条梯全跳过
6C17BA  A1 AC 5F 7D 00 / 8B 00         eax := [[0x7D5FAC]] = 200
6C17C1  3B 83 60 01 00 00 / 7F 2A      jg  -> 第二梯   (self.PK  < 200)
6C17C9  66 83 BB 78 02 00 00 14 / 76   jbe -> 第二梯   (self.Lv  <= 20)
6C17D3  66 83 BF 78 02 00 00 14 / 77   ja  -> 第二梯   (tgt.Lv   > 20)
6C17DD  8B 87 60 01 00 00 / 3B 02 / 7D jge -> 第二梯   (tgt.PK   >= 200)
6C17ED  C6 45 FF 00                    受保护
6C17F3  66 83 BB 78 02 00 00 14 / 77   ja  -> 三秒门   (self.Lv  > 20)
6C1804  3B 83 60 01 00 00 / 7E 20      jle -> 三秒门   (self.PK  >= 200)
6C180C  8B 87 60 01 00 00 / 3B 02 / 7C jl  -> 三秒门   (tgt.PK   < 200)
6C181C  66 83 BF 78 02 00 00 14 / 76   jbe -> 三秒门   (tgt.Lv   <= 20)
6C1826  C6 45 FF 00                    受保护
```

没有第三条梯，也没有任何一处读 `[+0x4B9]`（`m_boPKFlag`）。

零命中：`boPKLevelProtect` / `PKLevelProtect` / `nPKProtectLevel` / `PKProtectLevel`
四个名字三路皆 0。两个配置字段无加载器、无其他消费者，随块删除。

### PKD-13 背包 worker 的不死戒指早退 — `2adfed89`

`sub_740078` 从入口到**第一条条件跳转**全是栈帧与 SEH：

```
740078  55 8B EC 81 C4 24 FF FF FF     栈帧
740081  53 56 57                       push ebx/esi/edi
740084  33 D2 89 95 24 FF FF FF 89 55 F4   两个局部清零
74008F  8B F0                          esi := self
740091  33 C0 55 68 B4 02 74 00 64 FF 30 64 89 20   SEH 帧
74009F  8D 85 28 FF FF FF 33 C9 BA C8 00 00 00 E8   FillChar(200)
7400B1  A1 AC 5F 7D 00 8B 00           eax := [[0x7D5FAC]]
7400B8  3B 86 60 01 00 00 0F 9E 45 FF  setle -> 红名
7400C7  8B 86 08 05 00 00 8B 78 08 4F  edi := bag.Count - 1
7400D1  83 FF 00 0F 8C 8C 01 00 00     jl 0x740266   ← 全函数第一条条件跳转
```

上游策略梯 `sub_741368 @0x7413F6..0x741492` 只读六个地图旗标字节：

```
7413F6  80 7B 5D 00 / 75 12    FIGHT             [flag+0x5D]
7413FC  80 7B 5E 00 / 75 0C    FIGHT3            [flag+0x5E]
741405  E8 82 71 02 00         sub_76858C        安全区
741417  80 78 76 00 / 75 0F    ONLYDROPSPEC      [flag+0x76]
741426  80 78 77 00 / 74 59    LIMITBAGITEMDROP  [flag+0x77]
741435  80 B8 8C 00 00 00 00   天空三态          [flag+0x8C]
741461  E8 0A E8 FF FF         call sub_73FC70   装备
741469  E8 0A EC FF FF         call sub_740078   背包
```

零命中：`AngryRing` / `boAngryRing` / `NoDropItem` / `boNoDropItem` / `NODROPITEM` 三路皆 0。

### PKD-14 / PKD-14b 装备 worker 的不死戒指早退 — `91be1220`、`9f81902b`

`sub_73FC70` 同形，第一条条件跳转是 `0x73FCB6 7D 09 jge 0x73FCC1`（红名阈值），之前全是
栈帧、SEH 与七个局部清零。零命中：`NoDropUseItem` / `boNoDropUseItem` / `DropUseItem` /
`NODROPUSEITEM` 三路皆 0。

PKD-14 删的是 `TPlayObject` 覆写里那一个；**PKD-14b 补删基类 `TBaseObject.Base.cs` 里同样的一个**
（英雄与怪物走的是基类那条）。英雄确实在这条链上：`sub_741368` 恰好两个 `E8` 调用者
——`0x6C07D8`（TPlayer.Die）与 `0x687125`（THeroAct.Die）。

### PKD-15 `InDisableTakeOffList` — `3f1455f6`

装备 worker 循环体 `0x73FD29..0x73FF73` 全程没有任何按物品编号查表的动作，抽签之后紧接分流：

```
73FD2D  8B 86 C0 04 00 00 / E8 E8 EE 01 00   sub_75EC20(容器, ebx)
73FD3A  85 FF / 0F 84 2D 02 00 00            空格 -> 下一格
73FD46  8B 47 1C / F6 40 02 08 / 74 47       [std+2]&8 -> 销毁支
73FD96  8B 45 F8 / E8 AE 3D CC FF            Random(K)   ← 唯一抽签
73FDA2  80 BF FC 00 00 00 00                 [item+0xFC] 必爆类
73FDAF  80 BE 78 01 00 00 00 / 0F 85         非玩家 -> 落地支
73FDBC  A1 34 65 7D 00 … B1 04 … E8          sub_617A38(mgr, self, cl=4)
73FDD0  80 BF D8 00 00 00 00 / 0F 84         [item+0xD8] 赠品
```

其余过滤全是 `[std+2]` / `[std+3]` 的位测试与 `sub_78389C` 模式 5。
零命中：`DisableTakeOffList.txt` / `DisableTakeOffList` / `TakeOffList` 三路皆 0。

**范围**：只删了掉落路径上这一处。`M2Share.InDisableTakeOffList` 本体与另外三个调用者
（`TPlayObject.Operate.cs:604` / `:713`、`HeroObject.cs:2826`，都在**脱装备**路径上）保留
——那是另一条契约，本轮没查。另记：该 helper 目前是被掏空的桩，恒返回 false，所以这次删除
**当下无行为变化**，删掉的是「谁把函数体补回来，装备就会开始被豁免」的隐患。

### PKD-16 `RC_NPC` 也算 PK — `66ca8fda`

`RC_NPC` 是常量不是字符串，所以零命中改在字节层做，把 `[obj+0x178] == 10` 的所有编码都扫了：

| 扫法 | 结果 |
|---|---|
| A. `cmp byte [reg+0x178], 0x0A`（`80 /7 disp32 imm8`，八个 rm 全试） | 全镜像 **6 处**：`0x62E76D`、`0x62E80F`、`0x62EA9F`、`0x6E1D8E`、`0x6E8A82`、`0x6E9441`，**无一在死亡链内** |
| B. 同位移 + 0x80 组任意 opcode + imm8 0x0A | 同样 6 处，无新增 |
| C. 全镜像 419 处 `78 01 00 00` 位移逐个反汇编，向后六条指令找对 `0xA` 的 `cmp`/`sub`（先取值后比较那种形态） | **0** |
| D. 谋杀门 `0x6C081A..0x6C0891` 反汇编后筛 `cmp`/`test` | 八条：`test ebx,ebx` / `[+0x5F]` / `[+0x5D]` / `[+0x5E]` / PK 阈值 / `test eax,eax` / `[+0x73]` / 自杀。**没有种族比较** |

这个构造在二进制里确实存在（A/B 找到 6 处），所以它在这里的缺席是有意义的，不是扫描器伪影。
原生凶手身份完全由 `0x6C086B FF 92 B4 00 00 00 call [LastHiter.vmt+0xB4]` 的责任玩家解析决定。

### PKD-17 `boVentureServer` 门 — `06c12f7b`

`0x6C081A..0x6C0865` 只有三个地图旗标字节加一个阈值；整段唯一的绝对地址读是
`0x6C085D 8B 15 AC 5F 7D 00 mov edx,[0x7D5FAC]`（PK 阈值指针，不是布尔），
`0x6C081A..0x6C0891` 之间没有任何 `cmp byte [0x7Dxxxx],0`。
零命中：`VentureServer` / `boVentureServer` 三路皆 0。

**范围**：只删了这一处调用点。配置字段与另外六个消费者
（`UsrEngn.cs:553`/`:1730`、`TPlayObject.Base.cs:1335`、`TBaseObject.Base.cs:884`/`:944`/`:967`、
`GameApp.cs:442`）属于本轮没审的子系统，且 `ProtocolRegressionCheck` 用反射切换该字段，字段保留。
**留给接手的人**：这个名字三路零命中，那六处极可能同样是臆造的。

---

## 2. 两处按 §3.1 对齐（任务二）

### PKD-18 删 `boDieDropGold` — `144da8d2`

三条独立判据：

1. **掉金币到地上的例程是 `sub_768AAC`**（怪物结算 `sub_71FA20 @0x72000A E8 9D 8A 04 00`
   调它，之前是 `0x71FFBD` 上钳 `0xBB8`、`0x71FFD1 idiv`、`0x71FFDC` 按 `0x7D0` 分堆）。
   它全镜像**只有 6 个 `E8` 调用者、0 个字面 dword 引用**（不经虚表派发）：
   `0x64E74A`、`0x64E765`、`0x64F5C0`、`0x64F5DB`、`0x6C30F9`、`0x72000A`。
   其中 `0x6C30F9` 是玩家手动丢金币的短函数（`0x6C30E1` 与 `0x6C3102` 两处
   `29 B3 5C 01 00 00 sub [ebx+0x15C],esi`，`0x6C3112 ret`），与死亡无关。
   **六个里没有一个在死亡链上。**
2. **金币字段 `[obj+0x15C]` 的位移字节 `5C 01 00 00` 全镜像出现 103 次**，落在
   `sub_6C07A0` / `sub_741368` / `sub_73FC70` / `sub_740078` 里的是 **0** 次。
3. 策略梯三条出口在 `0x741498` 汇合，之后只有 `0x7414DB [self+0x37C] := 0` 与
   `0x741514 dx=0x2725`(10021) 一个包。

零命中：`DieDropGold` / `boDieDropGold` 三路皆 0。字段默认 false 所以线上休眠，
但 §3.1 的判据是「原生有没有」，不是「默认开不开」——留着等于给运营一个原生做不出来的开关。

### PKD-19 照抄装备销毁支的原生悬垂 — `ab194282`

装备 worker 的实名认证 / 赠品销毁支末尾：

```
73FEB4  8B 4D 94 66 BA 5E 00 8B C6
73FEBD  E8 1E 8D 02 00        call sub_768BE0     ; 日志 dx=0x5E
73FEC2  8B C7                 mov eax,edi         ; edi = 该 TUserItem
73FEC4  E8 C7 47 CC FF        call sub_404690     ; TObject.Free
73FEC9  E9 A1 00 00 00        jmp 0x73FF6F        ; -> inc ebx，下一格
```

Free 之后直接跳到 `inc ebx`，`[self+0x4C0]` 的第 ebx 格仍指向已释放对象。
**上一轮不敢照抄，理由是「要么原生就有这个悬垂，要么我漏读了一条清槽路径」。是前者**，
因为同一条链上另外三条支路都清：

| 支路 | 清槽指令 |
|---|---|
| `Reserved02&8` | `0x73FD86 mov eax,[esi+0x4C0]` / `0x73FD8C call sub_75F27C`，`sub_75F27C @0x75F2BB 89 54 83 08 mov [ebx+eax*4+8],0` |
| 落地 | `0x73FF0B mov eax,[esi+0x4C0]` / `0x73FF11 call sub_75F3E8`，`sub_75F3E8 @0x75F40F 89 54 86 08 mov [esi+eax*4+8],0` |
| **背包**销毁 | `0x74019D E8 8E 49 CE FF call sub_424B30` 先从 `[self+0x508]` 摘除，`0x74021E` 才 Free |

三个兄弟都清，只有这一条不清，而它是一段以无条件 `jmp` 结尾的五指令尾巴——不存在「漏读」的空间。

**顺带订正一处张冠李戴**：原来 C# 里 `m_UseItems[nC] = null;` 的注释引用 `0x73FF0B call sub_75F3E8`
当依据，而那个 VA 属于**落地支**，不是销毁支（§4.6 那类错误）。

C# 里照抄的方式是保留槽位引用。这样做在这里是安全的：`TBaseObject.Dispose(object obj)` 的函数体
就是 `{ obj = null; }`，只赋值形参，对象既不入池也不复用，保留的引用不可能与将来的物品别名。
玩家可见后果：未验证 / 赠品装备被判销毁时，10148 包（原生就在这条支路里
`0x73FE15 8B 47 18 / 8B 55 F4 / 89 44 95 A0` 攒的数组）告诉客户端它没了，
而服务端那一格仍然装着它，属性重算（`0x73FFA3 [vmt+0x1CC]`）与存档都还算它。**原版就是这样。**

---

## 3. 爆装分母定案（任务三）— PKD-20 `8dedcdc2`、PKD-21 `0d721cc5`

### 3.1 `[self+0x18C]` 的完整来源链

**唯一写入点、两个读取点**（全镜像）：

| | VA | 字节 |
|---|---|---|
| 写 | `0x73DAC5` | `89 86 8C 01 00 00`（在 `sub_73D500` 重算里） |
| 读 | `0x73FCC1` | 非红名分母 |
| 读 | `0x743E18` | `66 8B 83 8C 01 00 00` → `0x743E1F` 存进 `sub_743C50` 那个 0xB8 字节记录的 `+0x8C`（四个调用者 `0x689727`/`0x68978F` 英雄、`0x6B4D2A`/`0x6B4D71` 玩家；同一记录里 `+0x94` 是 `MyPKpoint`） |

写入点的算式是**无符号截断**除法：

```
73DAB8  0F B7 47 5E        movzx eax, word [edi+0x5E]
73DABC  B9 0A 00 00 00     mov ecx,0xA
73DAC1  33 D2 / F7 F1      xor edx,edx / div ecx      ← div 不是 idiv
73DAC5  89 86 8C 01 00 00
```

`edi` 是栈上 432 字节局部（`0x73D52E lea edi,[ebp-0x1B8]`，`0x73D554` FillChar `0x1B0`），
内容是装备容器聚合块的一份拷贝：

```
73D621  8B 86 C0 04 00 00  eax := [self+0x4C0]      ; 装备容器
73D629  8D 70 48           lea esi,[eax+0x48]
73D62C  B9 6C 00 00 00     0x6C dwords = 0x1B0 字节
73D631  F3 A5              rep movsd
```

⇒ **`[self+0x18C] = word[容器 + 0x48 + 0x5E] / 10`**。

> **订正上一轮**：上一份报告写「`edi = [esi+0x1B0]` 是 0x1B0 字节的装备加成聚合块」是错的。
> `0x73D542` / `0x73D63D` / `0x73D69E` / `0x73D8F2` 用的全是 **`lea`**，所以 `+0x1B0` 是**对象内嵌的
> 0x36 字节记录**（`0x73D542` 处 FillChar 的长度就是 `0x36`），而且它是第二次 `rep movsd`
> （`0x73D63D..0x73D650`，源是容器 `+0x1F8`）的**目的地**，不是任何东西的源。

容器布局（由 `sub_75F3E8 @0x75F40F mov [esi+eax*4+8],0` 与上面两组 lea/movsd 推出）：

```
+0x00 头 8 字节 | +0x08..+0x47 十六个 TUserItem 指针 | +0x48 起 0x1B0 聚合块 | +0x1F8 起 0x36 副块
```

聚合块由 `sub_75EE78` 重建：`sub_75F4F8` 清零 → 对 16 格里 `sub_7845A0(item) > 0` 的调
`sub_75EE04` → 后者以 `edx = 容器+0x48` 调 `[item.vmt+0x5C]` 与 `sub_75FE20`。

聚合块 `+0x5E` 的喂养者是**装备扩展属性分发表的一条臂**：

```
7620DA  8A 43 11                 al := byte [attr+0x11]     ; 属性类型
7620DD  83 C0 C7                 eax += -0x39               ; 偏置 57
7620E0  3D 97 00 00 00 / 0F 87   只认 57..208，越界忽略
7620EB  8A 80 F8 20 76 00        al := byte [eax+0x7620F8]  ; 152 项类型->槽号表
7620F1  FF 24 85 90 21 76 00     jmp [eax*4+0x762190]       ; 33 条臂
臂 0x7623B0（表项 0x7621FC，槽 27）：
7623B0  33 C0 / 8A 43 13         al := byte [attr+0x13]     ; 属性值
7623B5  66 01 46 5E              add word [esi+0x5E], ax
```

反查 152 项槽号表：落到槽 27 的**只有属性类型 201 (0xC9)**，别无第二个（邻居 202 落到 `+0x60`）。

⇒ **`[self+0x18C] = (身上所有装备的扩展属性 201 之和) / 10**。
分母是它 +90，所以属性 201 只会让装备**更不容易**掉，**90 是地板**。

### 3.2 `[LastHiter+0x579]` — 三处引用，闭合

| | VA | 字节 |
|---|---|---|
| 写 | `0x73D578` | `C6 86 79 05 00 00 00` := 0（重算开头） |
| 写 | `0x73DECF` | `C6 86 79 05 00 00 0A` := 10 |
| 读 | `0x73FD02` | `8A 83 79 05 00 00` 分母减项 |

写 10 受一道门控，同一道门还给 `[+0x2DC]` 加 20：

```
73DEBE  80 BE D5 01 00 00 00       cmp byte [self+0x1D5],0
73DEC5  74 0F                      je 0x73DED6
73DEC7  66 83 86 DC 02 00 00 14    add word [self+0x2DC],0x14
73DECF  C6 86 79 05 00 00 0A       mov byte [self+0x579],0xA
```

`[+0x2DC]` 是**百分比减伤**（`sub_73F8E0 @0x73F903 mov cx,[edi+0x2DC]` / `jle` 跳过 / `imul` /
`idiv 100` / 上钳 `0x4E20` / `sub esi,eax`；另一处 `sub_746130 @0x746177`）。
**它不是幸运**：`AddBodyLuck = sub_7698BC` 写的是 `[obj+0x164]`
（`0x7698D7 add [ebx+0x164],eax`，`0x7698E3`/`0x7698F4` 钳到 `[+5,-10]`）。

### 3.3 分母本体

```
73FCA9  A1 AC 5F 7D 00 / 8B 00        eax := [[0x7D5FAC]] = 200
73FCB0  3B 86 60 01 00 00 / 7D 09     jge -> 非红   （严格 PK > 200）
73FCB8  C7 45 F8 15 00 00 00          K := 21       （红名）
73FCC1  8B 86 8C 01 00 00 / 83 C0 5A  K := [self+0x18C] + 90
73FD02  8A 83 79 05 00 00 / 2B 45 F8  K -= byte [LastHiter+0x579]
73FD0B  83 7D F8 00 / 7D 04 / 33 C0   K < 0 -> 0
73FD99  E8 AE 3D CC FF                Random(K)
```

`0x73FCEF` 那道 `is THumanKind`（类指针 `[0x73BBE8]`）决定要不要减：怪物凶手不减。

### 3.4 C# 落地

新文件 `GameSvr/Actors/TBaseObject.NativeDeathDropDenominator.cs`：

- `m_nNativeDropRareBase`（`[+0x18C]`）、`m_btNativeDropRareKillerBonus`（`[+0x579]`）两个载体
- `NativeEquipDropRareAggregate()` / `NativeDropRareKillerBonusGate()` 两个可覆写钩子承载未解的输入
- `NativeRecalcDropRareFields()` 复刻 `0x73D578` / `0x73DAC5` / `0x73DECF`，由 `RecalcAbilitys` 驱动
- `NativeDeathEquipDropDenominator()` 复刻 `0x73FCA9..0x73FD0D`，含 THumanKind 判据
  （`IsNativeHumanKind` 在 `TPlayObject` 与 `HeroObject` 上覆写为 true，与 `sub_741368`
  恰好两个 `E8` 调用者一致）

两个 worker 都改用它，替换掉：

| 位置 | 原来 | 现在 |
|---|---|---|
| `TPlayObject.Message.cs` | `nDieRedDropUseItemRate`(15) / `nDieDropUseItemRate`(30) | 21 / `base+90` |
| `TBaseObject.Base.cs`（英雄与基类那份） | 硬编码 15 / 30，红名判据 `PKLevel() > 2`（= PK >= 300，把 201..299 整段判错） | 同上，红名判据改 `0x73FCB6` 的严格 `> 200` |
| `TPlayObject.Base.cs` 背包 | `g_Config.nDieScatterBagRate` | 硬编码 3（`0x7400F8 B8 03 00 00 00`） |

眼神「人物爆率调整」补丁路径未动，生效时仍然优先。

零命中：`DieDropUseItemRate` / `DieRedDropUseItemRate` / `nDieDropUseItemRate` /
`DieScatterBagRate` 三路皆 0，四个配置字段随之删除。

**玩家可见后果**：非红名装备掉落概率从 1/30 变 1/90，红名从 1/15 变 1/21。背包不变（默认值本来就是 3）。

### 3.5 残留 BLOCKED，以及误差方向

1. **装备扩展属性子系统（类型 57..208 → 33 个聚合槽）C# 整套没有**，所以
   `NativeEquipDropRareAggregate()` 恒 0、非红分母恒 90。对**没有属性 201 装备**的玩家
   这就是原生值；对有的玩家 C# 会比原生**更容易**掉装备。
   误差是**单边有界**的：永远不会比原生掉得少。
2. **`[self+0x1D5]` 在全镜像只有一处引用**，就是 `0x73DEBE` 那次读，**一个写入点都没有**。
   它只能来自人物存档记录的整块装载。在定位到那条装载路径之前
   `NativeDropRareKillerBonusGate()` 恒 false，`[+0x579]` 恒 0。

---

## 4. 本轮新发现、已登记但**未**处理的项

按范围纪律没动，列在这里给接手的人。

| 项 | 证据 | 建议 |
|---|---|---|
| 地图旗标 `boNODROPITEM` / `boNOTHROWITEM` / `boNODROPUSEITEM` 整套 | 三个 token 全镜像三路皆 0 命中，而同批的 `ONLYDROPSPEC` / `LIMITBAGITEMDROP` / `FREEPK` 都命中。`TMapFlag.cs:90` 有 `boNODROPITEM`，`Envirnoment.cs:1997` 还把它写进存盘 | 很可能整组 INVENTED，但涉及 mapinfo 解析与存盘往返，要单独排期 |
| `TBaseObject.Base.cs:1251` `if (!m_boNoItem \|\| !m_PEnvir.Flag.boNODROPITEM)` 玩家掉落总门 | 策略梯只读六个旗标字节，没有玩家侧布尔 | 与上一条同批处理 |
| `boVentureServer` 另外六个消费者 | 名字三路零命中 | 逐个子系统核 |
| `TPlayObject` 覆写的 `DropUseItems` **没有**实名认证/赠品销毁支 | 基类那份有（`TBaseObject.Base.cs`），但玩家走的是覆写那份；原生 `sub_73FC70` 由玩家与英雄共用 | `MISSING`，玩家的未验证/赠品装备现在会落地而不是销毁 |
| 装备 worker 的「重算属性」触发槽位集在原生就不一致 | `Reserved02&8` 支与落地支都是 `{0,1,4,13}`（`0x73FD55`/`0x73FF1C` 的 `sub eax,2 / jb`、`sub eax,2 / je`、`sub eax,9 / jne`），**销毁支只有 `{0,1}`**（`0x73FE0A` 只有第一个 `sub eax,2 / jae`） | 又一处原生不一致，照 §3.1 照抄，不许统一 |
| 落地支尾巴 `0x73FF3B..0x73FF61` | `cmp byte [esi+0x4C6],0 / je` → `[std+3]&2` → `sub_784568` → `sub_743F14`，C# 无对应 | `MISSING` |
| `IsProtectTarget` 第二套等级门用 `nRedPKProtectLevel`（默认 10），原生四处都是立即数 `0x14` = 20 | `0x6C17C9`/`0x6C17D3`/`0x6C17F3`/`0x6C181C` 全是 `66 83 B? 78 02 00 00 14` | `DIVERGENT`，改成立即数 20 即可，本轮没动是因为不在指派的六项里 |
| `IsProtectTarget` 把原生的「责任玩家 `edi`」与「原始目标 `esi`」合并成一个参数 | 安全区两测用 `ebx`/`esi`，等级梯用 `ebx`/`edi`，三秒门用 `ebx`/`esi` | 已登记的 `DIVERGENT`（C# 无 `[vmt+0xB4]` 递归解析器） |

---

## 5. 对既有材料的订正

1. **上一轮说 `[+0x18C]` 的聚合块经 `[esi+0x1B0]` 这个指针取到，是错的。** 那四处全是 `lea`，
   `+0x1B0` 是对象内嵌的 0x36 字节记录，是第二次 `rep movsd` 的**目的地**。真正的聚合块在
   **装备容器 `[self+0x4C0]` 的 `+0x48`**，432 字节，经 `0x73D631 rep movsd` 拷到栈上。
   照错版去找字段会一直找不到写入者。
2. **上一轮把装备销毁支不清槽位标成「不敢照抄」。** 它是真的原生悬垂，证据是同链另外三条
   支路都清（见 §2 的 PKD-19 表）。
3. **C# 里 `m_UseItems[nC] = null;` 的注释引用 `0x73FF0B` 当依据是张冠李戴**，那个 VA 属于落地支。
4. **`[+0x2DC]` 不是幸运。** 幸运是 `[obj+0x164]`（`sub_7698BC` 唯一写者）。`[+0x2DC]` 是百分比减伤。
5. **`RC_NPC` 这条的零命中不能只扫名字。** 它是常量，必须扫 `[obj+0x178] == 10` 的字节形态；
   扫下来这个构造在镜像里存在 6 处，恰好都不在死亡链上——这比「名字找不到」强得多。
6. **`InDisableTakeOffList` 目前是掏空的桩**，所以 PKD-15 当下无行为变化；
   任何「删了它玩家会多掉装备」的担心在当前树上不成立。
