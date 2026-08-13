# STATE-19 复核：毒系施加管道（legacy `RM_POISON` 8037）→ **FAITHFUL**

底本：`D:/loym2/staging/_reunpack_work/flat_image.bin`（ImageBase 0x400000，flat：file_off = VA − 0x400000）。
分支：`w/state19`（基于 master `fd0555e8`）。工具：Python311 + capstone 5.0.7。

> **判定：FAITHFUL（不改代码）。** eqv-11 曾判 DIVERGENT，其两条前提经二进制复核**均不成立**：
> 1. 「原生施毒**直调** `MakePosion`(VMT+0xC8)，无内部消息」——**错**。原生施毒经 **1000ms 延迟自消息 10300(0x283C)** → `RunMsg`(VMT+0x18) 的 10300 臂 → `MakePosion`(VMT+0xC8) → `AddState`(VMT+0x1EC)，与 C# 走 `SendDelayMsg(RM_POISON,…,1000) → MakePosion` **同构**。
> 2. 「C# 用 legacy 计时槽 `m_wStatusTimeArr` 施毒」——**已在 master 删除**。`m_wStatusTimeArr` 现为 `LegacyStatusTimeView` 纯转发视图（无自有存储），`MakePosion` 已改走**唯一权威** `AddTimedAbilityInternal`。
>
> `RM_POISON=8037` 与原生 `10300` 是**同一条服务器内部延迟消息**的不同编号；该号永不序列化上线（仅进 `m_MsgList`／`RunMsg`），数值差**行为不可见**。

---

## 1. 全镜像确认：立即数 8037(0x1F65) 零命中

指令级扫描（可加载的立即数形式），全 0；对照组证明扫描器可信：

| 模式 | 命中 |
|---|---|
| `push 0x1F65` / `mov r32,0x1F65` / `mov r16,0x1F65`（全部寄存器） | **0** |
| dword `65 1F 00 00` | **0** |
| 对照 `mov cx,0x278D`(RM_DURACHANGE=10125) | 24（含 `0x6ED9A3`，即施毒术里那条） |

（原始 word 双字节 `65 1F` 有 157 处，均为数据/相对偏移巧合，非立即数加载——指令级扫描才是判据。）

结论：**原生二进制不存在 8037 作为消息号的加载**。

## 2. 原生施毒管道（全链，均有 VA）

### 2.1 施毒术 `sub_6ED945`（内联取药 + 按 Shape 分派）
```
6ED949 mov dl,9              ; 只读 U_BUJUK(槽9)
6ED951 call 0x75EC20         ; GetUseItem(9)；nil→fail
6ED966 mov edx,[0x75E4E8]/call 0x404828 ; Delphi is TPoisons；非→fail
6ED97C cmp word [eax+0x26],0x64 / 6ED981 jb  ; Dura<100 跳过扣减但仍施毒
6ED986 sub word [eax+0x26],0x64             ; 固定扣 100
6ED9B6 mov al,[eax+0x15]     ; StdItem.Shape
6ED9B9 cmp al,1 / 6ED9C6 call [edi+0x110]   ; Shape1 绿毒 applier(VMT+0x110)
6ED9D3 cmp al,2 / 6ED9E0 call [edi+0x114]   ; Shape2 红毒 applier(VMT+0x114)
6ED9F0 call 0x73CC18         ; 无条件耗药钩子(施毒后)
```

### 2.2 绿/红毒 applier（VMT+0x110=`0x76E540` / VMT+0x114=`0x76E620`）
两者同构，核心在 applier **内部发延迟消息**，而非直调 MakePosion：
```
; 绿毒 0x76E540
76E56E mov edx,0x1E / mov eax,esi(target) / call 0x4C8764  ; 时长基
76E58A call 0x764D14 / 76E592 add eax,eax / 76E594 add edi,eax ; nParam1=时长
76E5C3 call [edi+0xCC]        ; VMT+0xCC=0x767F10（即时效果，返回值→nParam3）
76E5C9 push 0x1F(=31 bodyState) ; → 消息 wParam
76E5D5 push 0x3E8(=1000)      ; → 延迟 1000ms
76E5DA mov cx,0x283C(=10300)  ; → 消息 wIdent
76E5E1 call 0x766060          ; = SendDelayMsg
; 红毒 0x76E620 同构：76E673 push 0x1E(=30)；76E68E mov cx,0x283C；76E696 call 0x766060
```
`0x766060 = SendDelayMsg` 证据：写节点 `node+0x16 = [ebp+8] = 0x3E8`（延迟位）；对照 `0x765E68 = SendMsg` 在 `0x765EB9` 恒置 `node+0x16 = 0`。

### 2.3 `RunMsg`(VMT+0x18=`0x766a7c`) 对 wIdent 的比较链 → 10300 落点
```
766AA3 movzx eax,word[esi]        ; wIdent
766AA6 cmp eax,0x27B1 / jg 0x766AFC
766AFC 高支: sub eax,0x27C1(10177) / je 0x766D90   ; 10177=延迟AoE臂
766B14 sub eax,0x7B(+123 → 10300) / je 0x766E9F    ; **wIdent==10300 → 0x766E9F**
```

### 2.4 10300 接收臂 `0x766e9f` → `MakePosion`(VMT+0xC8=`0x76B3C8`)
```
766E9F mov eax,[esi+8](nParam2) ; 取对象；766EA9 je 0x766F8A（null 分支）
766EB4 call 0x767498 / je 0x766F6F        ; IsProperTarget
766ECF call 0x76719C                        ; SetTargetCreat
766EEB/766F1B call [ecx+0xAC](VMT+0xAC)     ; SetPKFlag
766F6A call 0x767504                         ; SetLastHiter
766F6F mov ax,[esi+0xC](nParam3)/push / mov cx,[esi+4](nParam1) / mov dl,[esi+2](wParam)
766F7F call [ebx+0xC8]                       ; MakePosion(dl=wParam,ecx=nParam1,push nParam3)
766F8A (null 分支同样) call [ebx+0xC8]        ; MakePosion
```

### 2.5 `MakePosion`(VMT+0xC8=`0x76B3C8`) = 秒→毫秒包一层，转唯一权威 `AddState`
```
76B3D8 call 0x773C44 / jne ret       ; ImmuneCheck→中止
76B3E1 mov dl,0x34 / call 0x772960 / jne ret ; HasState(52)全局否决→中止
76B3EE cmp bl,0x12 / …               ; nType==0x12 且 HasState(0x1A)→RemoveState(0x1A)
76B413 imul ecx,eax,0x3E8            ; nTime 秒→毫秒
76B41F call [ebx+0x1EC]              ; AddState(edx=nType, ecx=ms, push 0, push nPoint)
```

**原生毒系 = 延迟消息(10300,1000ms) → MakePosion → AddState 计时节点**（单一权威、秒→毫秒）。

## 3. C# 侧：与原生 10300 链逐指令对应

- 定义：`SystemModule/Grobal2.cs:1650 public const int RM_POISON = 8037;`（内部号）。
- 发送：`GameSvr/Spells/MagicManager.cs:325/329/777/1446/1450/1929` `SendDelayMsg(…, RM_POISON, POISON_*, …, 1000/650)`。
- 接收：`GameSvr/Actors/TBaseObject.Base.cs:2036 case RM_POISON` —— **等价于原生 `0x766e9f` 臂**：

| 原生 10300 臂 | C# `case RM_POISON` |
|---|---|
| `mov eax,[esi+8]`(nParam2)→对象；`je 0x766F8A`(null) | `Get(ProcessMsg.nParam2)`；`else`→MakePosion |
| `call 0x767498`；`je 0x766F6F` | `if (IsProperTarget(...))` |
| `call 0x76719C` | `SetTargetCreat` |
| `call [ecx+0xAC]`(VMT+0xAC) | `SetPKFlag`（双方玩家时） |
| `call 0x767504` | `SetLastHiter` |
| `call [ebx+0xC8]`（两处，proper/null） | `MakePosion(wParam,nParam1,nParam3)`（两处） |

- `MakePosion`：`GameSvr/Actors/TBaseObject.cs:6163` → `AddTimedAbilityInternal((byte)(31-nType), nPoint, nTime*1000, 0)`。
  - 秒→毫秒（`*1000`）= 原生 `imul …,0x3E8`。
  - `31-nType`：C# 内部消息 wParam 用毒型 0/1，原生 10300 消息 wParam 用 bodyState 31/30；接收侧 C# 以 `31-nType` 折算，净落态 = 31/30，**与原生一致**（差异仅在内部消息编码位置，行为不可见）。
  - 原生的 ImmuneCheck(0x773C44)/HasState(52)/`0x12→移除0x1A` 三道门已并入 `CanAddNativeTimedAbility`/`AddTimedAbilityInternal`（见该文件与 `NativeState26.cs`）。
  - `nType<12` 门：原生 MakePosion 无此门，但 C# 所有 MakePosion 调用方均传毒型 0/1（<12），门恒过；需绕过的态 id(0x11/0x18，DebuffTrap)另走 `ApplyNativeStateSeconds` 直入 `AddTimedAbilityInternal`。故该门对所有真实调用方为 no-op，无行为差。

## 4. legacy `m_wStatusTimeArr` 已非第二权威（master 现状）

`GameSvr/Actors/TBaseObject.LegacyStatusTimeView.cs`：`m_wStatusTimeArr` 是**只读转发属性**，返回 `LegacyStatusTimeView`（无自有存储），读写全部落到唯一节点链 `Self+0xDC`（`FindTimedAbilityInternal`/`AddTimedAbilityInternal`/`RemoveTimedAbilityInternal`）；旧 `ushort[12]`（秒、独立每秒循环）第二权威已删除。`MakePosion`(TBaseObject.cs:6188-6195 注释)明确删除了原先跟在其后的 `max()-and-stamp` 二次写。→ eqv-11 担心的「双计时槽施毒漂移」不复存在。

`8037` 全解决方案用点仅 5 处（Grobal2 定义 / MagicManager 6 发 / TBaseObject.Base 1 收 + 2 次 MakePosion / AuditTools 1 锚点），均服务器内部，无网络序列化、无与其它消息号冲突。

## 5. 结论与范围外备注

- **STATE-19 = FAITHFUL。** 不改代码（且铁律禁改 Grobal2.cs；把 8037 改成 10300 属行为不可见的内部号改动，有冲突/破坏 AuditTools 锚点风险，无收益）。
- 范围外观察（交毒系调参车道 pois-bonus/poison/pois11 处理，不在 STATE-19 管道范围）：绿/红毒 applier 里 `nParam1`(时长)与 `nParam3`(值)的**算子来源**在 C#(MagicManager: `nPower` / `Round(level/3·power/point)`)与原生(`0x4C8764(target,30)+2·0x764D14(caster)` / `VMT+0xCC=0x767F10` 返回)由不同助手链计算。这是毒系数值调参，非本管道分歧；本复核不触碰。
