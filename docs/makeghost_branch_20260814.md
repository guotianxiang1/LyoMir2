# MakeGhost 的 `m_boCanReAlive` 分叉裁决（2026-08-14）

- 工作树：`D:\loym2\.claude\wt2\makeghost`
- 分支：`w/makeghost`（基于 `master` = `38c5f107`）
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`，ImageBase `0x400000`
- 判定：**INVENTED（上游 C# 自造）→ 已移除**

---

## 1. 结论摘要

C# `TBaseObject.MakeGhost()` 里那条 `if (m_boCanReAlive) { m_boInvisible = true; ... }`
分叉，**在战神二进制、GameOfMir Delphi、EM2Engine Delphi 三个来源里都不存在**，
来自上游 LyoMir2 的 C#。它配套的消费端（`ReAliveEx` 原地复活）已于 2026-08-03 按字节删除，
只剩这半截分叉悬空，且**当前是一条活的运行期缺陷**（见 §5）。已按原生塌缩为无分叉形态。

---

## 2. 原生真值：`sub_768060` = `TCreature.MarkDelete`

身份由自带异常串坐实（Delphi 长串，长度前缀在 `ptr-4`）：

| VA | 串（GBK） |
|---|---|
| `0x768138` | `[Exception]: TCreature.MarkDelete Cret的地图无效` |
| `0x768174` | `[Exception]: TCreature.MarkDelete Cret的地图不有效 OwnMap = ` |

全函数 `0x768060..0x76812E`（含 SEH 框架）逐字节：

```
0x768060  55 / 8B EC                push ebp / mov ebp,esp
0x768063  6A 00 ×3                  三个局部串槽 (ebp-4 / -8 / -0xC)
0x76806B  8B D8                     mov ebx,eax            ; ebx = Self
0x768070  68 22 81 76 00 …          SEH frame
0x76807B  8B B3 28 01 00 00         mov esi,[ebx+0x128]    ; m_PEnvir
0x768081  85 F6 / 75 15             test esi,esi / jne 0x76809A
0x768085  …  BA 38 81 76 00         edx := 0x768138        ; 地图为空 → 记日志
0x768093  E8 DC 5E 03 00            call 0x79DF74          ; MainOutMessage
0x768098  EB 4F                     jmp 0x7680E9           ; 【仍然继续置 ghost】
0x76809A  83 7E 44 00 / 75 49       cmp [esi+0x44],0 / jne 0x7680E9
0x7680A0..0x7680E4                  拼 "OwnMap = " + 地图名，记第二条日志
--- 以上全部只是日志，不改任何状态 ---
0x7680E9  80 7B 73 00               cmp byte [ebx+0x73],0  ; 幂等门：已是 ghost
0x7680ED  75 18                     jne 0x768107           ;   → 整段跳过
0x7680EF  C6 43 73 01               mov byte [ebx+0x73],1  ; m_boGhost = TRUE
0x7680F3  E8 48 02 CA FF            call 0x408340          ; GetTickCount
0x7680F8  89 83 4C 01 00 00         mov [ebx+0x14C],eax    ; m_dwGhostTick
0x7680FE  33 D2                     xor edx,edx            ; 实参 = FALSE
0x768100  8B C3 / E8 AD 00 00 00    mov eax,ebx / call 0x7681B4
0x768107  …                         SEH 拆解 + 局部串释放 + ret
```

**全函数只有 `0x7680E9` 这一个字节测试，读的是 `+0x73` 自身（幂等门）。
没有任何"可复活"标志的读取，`m_boGhost` 是无条件写入。**

### 2.1 `sub_7681B4` = `DisappearA`

```
0x7681BA  8B DA / 8B F8             ebx = 参数(bool) / edi = Self
0x7681BE  8B B7 28 01 00 00         esi = m_PEnvir
0x7681C6  74 4D                     je 0x768215            ; 无地图 → 错误分支
0x7681C9  8B 8F 30 01 00 00         ecx = [edi+0x130]      ; m_nCurrY
0x7681CF  8B 97 2C 01 00 00         edx = [edi+0x12C]      ; m_nCurrX
0x7681D7  E8 CC 12 01 00            call 0x7794A8          ; DeleteFromMap → al
0x7681DC  84 C0 / 74 35             test al,al / je 0x768215
0x7681EE  66 BA 1E 00               mov dx,0x1E            ; RM_DISAPPEAR
0x7681F6  FF 96 E0 00 00 00         call [vmt+0xE0]        ; SendRefMsg
```

与两份 Delphi 参考逐语句同构：

```pascal
procedure TBaseObject.DisappearA();
begin
  m_PEnvir.DeleteFromMap(m_nCurrX, m_nCurrY, OS_MOVINGOBJECT, Self);
  SendRefMsg(RM_DISAPPEAR, 0, 0, 0, 0, '');
end;
```

### 2.2 32 个调用者：无一分流

全镜像 `E8` 直调 `0x768060` 共 **32 处**，`E9` 跳转 **0 处**：

```
0x5FDDC6 0x5FDE20 0x606694 0x606A1C 0x60AA97 0x60ABAC 0x64714A 0x64930D
0x667B46 0x66A38C 0x66A5F9 0x66A66E 0x66B7ED 0x66C8D1 0x67571E 0x67C6E6
0x681803 0x681A31 0x68A06C 0x6B2CD9 0x6B3453 0x6B3C03 0x6B6748 0x6CCACE
0x71B60D 0x71C703 0x71D43C 0x76651B 0x76669A 0x7682CF 0x779E79 0x77BBBC
```

逐点扫描调用前 0x40 字节内的字节标志测试，命中的偏移只有
`+0x73`（m_boGhost 自身，避免重复置位）、`+0x74`（m_boDeath），
以及各派生类自己的状态字节（`+0x178` / `+0x2C` / `+0x4E9` / `+0x4EC` / `+0x4BB..0x4BD`）。
**没有任何一个调用点因为某个"可复活"标志改走另一个函数**——
也不存在与 MarkDelete 配对的"只置隐身"的第二个 sink。

---

## 3. 原生对"可复活"对象走的是哪条路

**答：原生没有"对象复活"这回事。刷怪点补怪 = 销毁旧对象 + 工厂造一只新的。**
`m_boGhost` 的消费端就是这条链：

1. **刷怪工厂 `sub_67C9E0`** 落地新怪时，写到对象身上的**只有** `word[obj+0x38]`
   （尸体存留秒数，`0x67CA49 cmp [ebx+0x28],0 / je` → `0x67CA56 mov [eax+0x38],dx`）。
   **不写**任何 can-realive 标志、**不写** `m_pMonGen` 回指针、**不写** `m_dwReAliveTick`。
   随后 `0x67CA92 mov [certArray+edi*4],edx` 挂进 CertList、`0x67CA9B inc [ebx+0x2C]`、
   `0x67CAA1 inc [engine+0x34]`。
   → **原生怪物身上根本不存在 `m_boCanReAlive` 这个状态位。**

2. **逐怪 tick 循环 `ProcessMon sub_67C150`** 的 CertList 遍历
   （`0x67C354..0x67C4A8`），每只怪**只有三个出口**：

   ```
   0x67C381  cmp [ebp-0x18],0 / je 0x67C4A2      ; 槽为 null → 跳过
   0x67C38E  cmp byte [obj+0x73],0               ; m_boGhost
   0x67C392  jne 0x67C46F                        ;   → 回收臂
   0x67C39E  sub edx,[obj+0x340] (m_dwRunTick)
   0x67C3A7  cmp edx,[obj+0x33C] (m_nRunTime) / jbe 跳过
   0x67C3D2  call [vmt+0x88]                     ; Run()
   ; 回收臂 0x67C46F：
   0x67C472  dec [engine+0x34]                   ; nMonsterCount--
   0x67C47E  call 0x67D8F0                       ; 入全局延迟释放 FIFO
   0x67C491  mov [certArray+eax*4],0             ; NULL 掉 CertList 槽
   0x67C497  dec [gen+0x2C]                      ; CertCount--
   0x67C49F  call [vmt+0x7C]                     ; 离开世界钩子
   ```

   **判据只有 `+0x73`。无 `m_boDeath` 测试、无 can-realive 读取、无复活臂。**

3. **补怪**：`sub_67C9E0` 在 `0x67CA15 cmp dword [eax+edi*4],0 / jne` 处跳过仍被占用的槽，
   只往上一步腾空的槽里塞**新造**的对象（工厂 `sub_679F8C`）。

闭环：`m_boGhost` 置位 → 回收臂腾槽 → 工厂补新怪。**中间没有任何"救活旧对象"的分支。**

---

## 4. C# 那条分叉的来历

| 来源 | `MakeGhost` 形态 | 有无 `m_boCanReAlive` 分叉 |
|---|---|---|
| 战神 `sub_768060` | 幂等门 + 无条件 `m_boGhost=1` + `DisappearA` | **无** |
| `staging/ref-MIR2/GameOfMir/M2Server/ObjBase.pas:20510` | 三行 | **无** |
| `staging/ref-MirServer-Delphi/EM2Engine/ObjBase.pas:18605` | 三行 | **无** |
| `staging/upstream-LyoMir2/GameSvr/Actors/TBaseObject.Base.cs:1117` | 两臂分叉 | **有** ← 来源 |

两份 Delphi 参考的实现完全一致：

```pascal
procedure TBaseObject.MakeGhost();
begin
  m_boGhost := True;
  m_dwGhostTick := GetTickCount();
  DisappearA();
end;
```

`boCanReAlive` 这个标识符在 `staging/ref-MIR2` 与 `staging/ref-MirServer-Delphi`
两棵 Delphi 树里 **0 命中**——它不是 Delphi 血统，是上游 C# 端新造的字段。

上游 C# 版本：

```csharp
if (m_boCanReAlive) {
    m_boInvisible = true;                                   // ← 自造
    m_dwGhostTick = HUtil32.GetTickCount();
    m_PEnvir.DeleteFromMap(...); SendRefMsg(RM_DISAPPEAR, ...);
} else {
    m_boGhost = true; m_dwGhostTick = ...; DisappearA();    // ← 与原生一致的那臂
}
```

`git log -L 1518,1534:GameSvr/Actors/TBaseObject.Base.cs` 只回溯到
`d5d00744 Baseline: 战神 M2Server 1:1 C# rewrite tree`——基线是压平快照，
这条分叉自上游带入后**从未被复核过**。

> 与 ghost-timing 代理查出的 SPAWN-32 同型：那条源自 LOMCN Delphi，这条更进一步——
> 连 Delphi 都没有，纯上游 C# 自造。

---

## 5. 为什么它现在是活缺陷（不是无害残留）

`m_boCanReAlive` 在 C# 里**只有一个置真点**：`UsrEngn.cs:3410`，
即 `CreateGeneratedMonster` —— **所有刷怪点生成的怪物**。
`TBaseObject.Base.cs:867`（Die）与 `TBaseObject.cs:7173`（OnEnvirnomentChanged）
只在"怪物已不在其刷怪点所属地图"时才清掉它，正常在本图死亡的怪一直为真。

于是在**修改前**：

1. 怪物死亡 → `TBaseObject.Base.cs:140` 尸体秒数到期 → `MakeGhost()`
2. 走 `m_boCanReAlive` 臂 → 只置 `m_boInvisible`，**`m_boGhost` 永远为 false**
3. 回收循环 `UsrEngn.cs:1803` 的唯一判据就是 `!Monster.m_boGhost`
   → 尸体被永远当成"活怪"，**继续每 tick 跑 `Run()` / `SearchViewRange()`**
4. 永不入延迟释放 FIFO、**CertList 槽永不腾出**、`nActiveCount` 不回落
   → **刷怪点永不补怪**，且怪物对象无限累积

配套的"原地复活"消费端 `ReAliveEx` 已于 2026-08-03 按字节证据删除
（`TBaseObject.cs:6902` 定义仍在但**全库零调用者**，按 §3 属死代码 = `MISSING`），
所以这条臂现在既不复活、也不回收，是纯泄漏。

> 这不是 §3.1 意义上的"原版缺陷照抄"——原版没有这条臂，缺陷是 C# 侧独有的。

---

## 6. 改动

`GameSvr/Actors/TBaseObject.Base.cs:1518`，塌缩为原生形态（两臂原本只差
`m_boInvisible` / `m_boGhost` 一个赋值，其余三行完全相同）：

```csharp
public virtual void MakeGhost()
{
    m_boGhost = true;
    m_dwGhostTick = HUtil32.GetTickCount();
    RemoveFromMapForGhost();
    SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
}
```

（+42 / −14 行，其中 38 行是注明原生 VA 与字节的证据注释，防止下一个人再把分叉加回来。）

---

## 7. 波及面

### 7.1 `m_boGhost`

新增置真的对象**仅限**`m_boCanReAlive == true` 的刷怪点怪物；
玩家（默认 false）、英雄（`HeroObject.cs:147` 显式置 false）行为完全不变。
对这批怪来说，`m_boGhost` 从"永远 false"变成"到点为 true"，
正好接通 `UsrEngn.cs:1803` 那条本来就在等它的回收臂。

`m_boGhost` 的其它置真点（英雄退休 `UsrEngn.cs:1114`、`HeroDataService.cs:587`、
动态房间 NPC `NativeDynamicRoomNpcMaterializer.cs:253`、`NativeDynamicRoomService.cs:590`、
PAS `PasApiBridge.cs:6140`、`LocalDB.cs:930`）都不经过 `MakeGhost`，不受影响。

### 7.2 `m_boInvisible`

移除后，`m_boInvisible` **在 GameSvr 里已无任何置真点**，
剩下 `TBaseObject.cs:327` 的声明（false）与 `TBaseObject.cs:7160` 的置假
（在死代码 `ReAliveEx` 内）。两个活读点因此恒为 false：

- `TBaseObject.ViewRange.cs:217` —— 该处同一个 if 链的下一层就是 `!m_boGhost`
  （line 219），ghost 本来就被挡住，`m_boInvisible` 项失效不改变可见性结果。
- `TPlayObject.Base.cs:1809`

**这不构成回归**（原本被这个标志挡住的对象，现在改由 `m_boGhost` 挡住，语义更接近原生）。
但 `m_boInvisible` 这个字段本身是否有原生对应物，属独立课题，见 §9。

### 7.3 依赖现行为的审计工具

逐个查过，**没有任何审计工具断言这条分叉的行为**：

- `PasDispatchShadowCompatCheck:1582-1585` 自己手工设 `m_boCanReAlive`/`m_boInvisible`，
  但只断言 `m_dwGhostTick != 0`（line 1619）——两臂都写这个字段，不受影响。
- `MovementCollisionCheck:553` / `:890` 用的是 `TPlayObject`（`m_boCanReAlive` 默认 false），
  本来就走 else 臂。
- `HeroLifecycleCheck:255,292` 的 `m_boGhost` 断言对象是英雄/玩家，同样走 else 臂。
- `NativeCorpseGhostTimingCheck:235` 恰恰**禁止**谓词里出现 `m_boCanReAlive`
  （`Assert(!Regex.IsMatch(body, @"…|m_boCanReAlive"))`）——与本次改动同向。

---

## 8. 审计对照

A/B 用 `git stash` 对照法，同一次持锁内先跑 HEAD 再跑改动版，
**工作目录固定为仓库根**（`NativeProperTargetGateCheck` 的通过与否依赖 CWD，
不固定会产生假回归）。

| 工具 | HEAD（改前） | 改后 | 差异 |
|---|---|---|---|
| MovementCollisionCheck | PASS | PASS | same |
| HeroLifecycleCheck | FAIL | FAIL | same |
| NativeCorpseGhostTimingCheck | PASS | PASS | same |
| ExactEnvironmentMonsterSpawnCheck | PASS | PASS | same |
| ExactEnvironmentMonsterSpawnTransactionCheck | PASS | PASS | same |
| PasDispatchShadowCompatCheck | FAIL | FAIL | same |
| DynRoomFullDestroyStaticCheck | PASS | PASS | same |
| NativeHeroBehaviourCheck | PASS | PASS | same |
| NativeProperTargetGateCheck | FAIL | FAIL | same |
| DeathDropPolicyCheck | FAIL | FAIL | same |
| NativeFloorItemOwnerExpiryCheck | FAIL | FAIL | same |
| NativeDealEscrowExactCheck | PASS | PASS | same |
| NativeMagicTowerWantWarMonCheck | PASS | PASS | same |
| InProcHeroRunCheck | PASS | PASS | same |

**9 PASS / 5 FAIL，两侧完全一致；逐工具比对输出文本亦一致**
（唯一差异是 `PasDispatchShadowCompatCheck` 里一行 MySQL 连接失败日志的时间戳）。

5 项 FAIL 全部是既有的、与 ghost 无关的：

| 工具 | 既有失败原因 |
|---|---|
| HeroLifecycleCheck | `PAS HeroRename no longer enters the native 0x164 path`（Program.cs:93） |
| PasDispatchShadowCompatCheck | `Give experience multi-level result: expected 3, actual 2`（Program.cs:409） |
| NativeProperTargetGateCheck | 环境问题：`bin\Debug\Share\PlayerUpgradeExp.ini` 不存在（M2Share 静态构造抛出） |
| DeathDropPolicyCheck | `0x7400F8 mov eax,3: 背包爆率分母硬编码 3，不得引入配置旋钮` |
| NativeFloorItemOwnerExpiryCheck | `TPlayObject.Base.cs: 0x7839C1 归属作废判据是 m_boGhost` |

> 工具调用两点更正（对后续代理有用）：
> 1. `HeroLifecycleCheck` 需要显式传 GameSvr 构建目录，否则退出码 2 报 INCOMPLETE
>    ——那是**空跑**，不是断言失败。有效目录是 `D:\loym2\.claude\wt2\Build\Mir200`
>    （Debug `OutputPath` = `..\..\Build\Mir200`，`GameSvr\bin` 在正常构建下根本不存在）。
> 2. `dotnet run --project X -- <arg>` 在本环境**不转发 argv**，必须先 `dotnet build`
>    再直接执行 `bin\Debug\net8.0-windows\X.exe <arg>`。

构建验证：`dotnet build GameSvr\GameSvr.csproj` → **0 个警告，0 个错误**。

---

## 9. 遗留项（本次未动，证据已备）

1. **`0x7680E9` 幂等门 C# 侧缺失**（`DIVERGENT`）。原生对已是 ghost 的对象**整段跳过**，
   不刷新 `m_dwGhostTick`、不重发 `RM_DISAPPEAR`；C# 无此门，重复调用会把
   `m_dwGhostTick` 推后，理论上可无限推迟 5 分钟释放。
   **本次未加**：属 ghost-timing 代理的既定范围，且需单独跑
   `MovementCollisionCheck:562-564`（该处专门测重复 `MakeGhost` 的幂等性）对照。
   已核实**塌缩分叉不会让这条新增危害**：置 `m_boGhost` 后回收循环
   （`UsrEngn.cs:1803`）不再调 `Run()`，PAS `ClearMon` 也跳过既有 ghost
   （`PasDispatchShadowCompatCheck:1613` 钉住），因此没有重复调用源。

2. **`RM_DISAPPEAR` 在原生是有条件发的**（`DIVERGENT`）。
   `0x7681DC test al,al / je 0x768215` —— `DeleteFromMap` 返回 false 时**不发**
   `RM_DISAPPEAR`，改记 `[Exception]: TCreature.DeleteFromMap …`（`0x768244` / `0x768284`）。
   C# `MakeGhost` 无条件发。属 MovementCollision 范围（`RemoveFromMapForGhost` 的语义
   与 `sub_7681B4` 也不完全一致），需与该子系统一并裁决。

3. **`m_boInvisible` 字段本身**（待判）。移除本分叉后它在 GameSvr 无置真点。
   需要单独确认原生是否存在对应的隐身字节及其写入点；若没有，则整个字段
   连同两个读点属 `INVENTED`。**本次不动**——它牵涉 ViewRange / 可见性子系统。

4. **`ReAliveEx`（`TBaseObject.cs:6902`）是死代码**，全库零调用者，按 §3 判 `MISSING`。
   `m_dwReAliveTick` / `m_pMonGen` 亦随之只剩写入无消费。建议一并清理，但属刷怪子系统。
