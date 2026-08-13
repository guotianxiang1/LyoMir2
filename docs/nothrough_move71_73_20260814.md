# NOTHROUGH 穿透系统 — MOVE-71 / MOVE-72 / MOVE-73 复刻结论

- 工作树：`D:\loym2\.claude\wt2\move-nothrough`
- 分支：`w/move-nothrough`
- 提交：`aeadf8f5`（MOVE-71/72 判定+缓存+walk 接线，2 文件）
- 构建：`dotnet build GameSvr` 全量 Rebuild = 0 错误
- 底本：`D:\loym2\staging\_reunpack_work\flat_image.bin`（ImageBase=0x400000）
- 反汇编：capstone 5.0.7 / Python311
- 注：`D:\loym2` 非 git 仓库（仅 worktree 受版控），本报告为独立产物。

---

## 一、原版机制（二进制核实）

### 判定链（MOVE-71 极性 + 链路）
```
Envir[+0x84]=NOTHROUGH → sub_768454 → 缓存 Obj[+0x3FE] → mover 第三参 → MoveToMovingObject.boIgnoreOccupancy
```
- NOTHROUGH 解析：`0x774E0B  c6 83 84 00 00 00 01  mov byte [ebx+0x84],1`（token "NOTHROUGH"，
  字面量 0x775C24；比较 `0x774DFF mov edx,0x775C24` / `0x774E04 call 0x40591C`）。
- NOTHROUGH 全镜像**唯一读点**：`0x76846B  80 B8 84 00 00 00 00  cmp byte [eax+0x84],0`（在 sub_768454 内，
  确非移动路径上——正是 MOVE-71 强调「勿内联进 walkability」的原因）。
- 占用扫描跳过闸：`0x779870 cmp byte [ebp+8],0 / 0x779874 jne 0x7799C6`（boIgnoreOccupancy 非 0 → 跳过整段扫描）。
- 极性：NOTHROUGH 置位→判定 FALSE→缓存=0→查占用（撞人撞怪）；清零且其它条件成立→判定 TRUE→缓存=1→跳过扫描（穿人）。

### sub_768454（MOVE-72，判定本体，0x768454）
```
0x76845C call 0x772EB8      ; 无条件穿透授予 → 立即 TRUE（0x768498 mov al,1）
0x76846B cmp [Envir+0x84],0 ; NOTHROUGH 置位 → FALSE（0x768493 xor eax,eax）
0x768474 mov eax,[0x7D6970]/[eax] ; ThroughRange（安全区穿人范围）
0x76848A call 0x7684DC      ; sub_7684DC(Self, X=Obj+0x12C, Y=Obj+0x130, ThroughRange)
```
- sub_772EB8(0x772EB8)：`cmp [ebx+0x2E2],0`(m_boObMode) 或 `sub_772960(dl=0x3C)`（体状态 60）→ 授予穿透。
  C# 已建模为 `TBaseObject.HasNativeCellPassThroughGrant()`（`m_boObMode || HasNativeActiveState(0x3C)`）。

### sub_7684DC（MOVE-72，安全区子判定，0x7684DC，ret 4）
```
0x7684F6 mov bl,[Envir+0x5C]   ; SAFE：置位→bl≠0→TRUE
0x768505 call 0x7684A0         ; SAFE 清零→SafeZoneList 多边形(sub_696D7C)，与 nRange 无关
0x76850C test bl / jne         ; bl≠0 → TRUE
0x768510 test edi / jle        ; nRange<=0 → 到此为止（跳过 RedHome/起点两臂）
0x76851D edx=0x768588("3")     ; 地图名 == "3" → RedHome 半径臂
0x76852C sub eax,0x34D(845)    ; |X-845|<=nRange（cmp/jl 含界）
0x76853D sub eax,0x2A2(674)    ; |Y-674|<=nRange（cmp/jl 含界）
0x768567 call 0x696E48         ; 皆否→本图起点表半径 nRange 扫描
```
- ThroughRange = `*(*(int**)0x7D6970)`。GM `@ThroughRange` 写：`0x6252F0 mov eax,[0x7D6970] / 0x6252F5 mov [eax],ebx`
  （0..50，`0x6252E7 cmp ebx,0x32`）。全镜像仅此写点与 sub_768454 读点两处引用，**无启动初始化**在 dump 区间内。

### 缓存刷新（MOVE-73，玩家 tick sub_6B2D38）
```
0x6B308E call 0x768454          ; al = 重算判定
0x6B3096 cmp al,[edx+0x3FE]     ; 与旧缓存比较
0x6B309C je  0x6B30E1           ; 未变 → 不写、不发消息
0x6B30A3 mov [ecx+0x3FE],dl     ; 变了才回写
                                ; 并广播 SM_2821(0xB05)：TRUE push 6/1/0/0，FALSE push 6/0/0/0（vmt+0x250）
```
判定是**每 tick 刷新的缓存字段**，不是移动时的实时测试（MOVE-73 要点）。

### 缓存消费者（全镜像 `[reg+0x3FE]` 扫描）
walk mover：`0x6BBD0C mov cl,[ebx+0x3FE]`（sub_6BBCD8/…→WalkTo 第三参，sub_741224 于 0x74122D 存、0x7412B3 压→MoveToMovingObject）。
run mover sub_76756C：`0x7675BA`（探测 sub_777EF8，即 MOVE-50 的只读占用探测）、`0x767601`（移动 sub_7797CC）。
另有多处技能位移读同一缓存（0x673xxx/0x68Bxxx/0x71Axxx/0x6EECE1 等）。写点唯一（tick 0x6B30A3）。

---

## 二、C# 侧核对与修正

| 项 | 原版 | C# 修正前 | 处置 |
|----|------|-----------|------|
| NOTHROUGH 解析 | Envir[+0x84]=1 | `Maps.cs:540 boNOTHROUGH=true` 解析后**全无消费点** | 建立唯一消费链 ✔ |
| 判定 sub_768454 | 授予/NOTHROUGH/安全区 | **缺失** | 新增 `NativeComputeThroughOccupancy` ✔ |
| 安全区子判定 sub_7684DC | SAFE/多边形/nRange闸/RedHome/起点 | 仅 `InSafeZone`(range=nSafeZoneSize，无 nRange 闸) | 新增 range 参版 `NativeSafeZoneThroughTest` ✔ |
| 缓存 Obj[+0x3FE] | tick 刷新 | **缺失** | 新增 `m_boThroughOccupancyCache` + `NativeRefreshThroughOccupancyCache` ✔ |
| walk boFlag | 缓存判定(0x6BBD0C) | `ClientWalkXY: WalkTo(n14, false)` 恒 false | 改传判定 ✔ |
| ThroughRange 全局 | *off_7D6970 | 仅 GM 模型层，无实时存储 | 新增 `NativeSafeZoneThroughRange`（fail-closed 默认 0）✔ |

修改文件（提交 `aeadf8f5`）：
- 新增 `GameSvr/Players/TPlayObject.NativePassThrough.cs`
- 改 `GameSvr/Players/TPlayObject.Attack.cs`（`ClientWalkXY`）

RedHome 常量 `g_Config.sRedHomeMap="3" / nRedHomeX=845 / nRedHomeY=674`
与 0x768588/0x34D/0x2A2 逐字节吻合（沿用 MFLG-17 既定约定）。`dotnet build GameSvr` = 0 错误。

---

## 三、逐条契约结论

- **MOVE-71（NOTHROUGH 极性与链路）**：**已复刻**。建立
  `Flag.boNOTHROUGH → NativeComputeThroughOccupancy → 缓存 → WalkTo(boFlag) → MoveToMovingObject.boIgnoreOccupancy`
  的唯一消费链；极性与占用扫描跳过闸(0x779870)一致；未内联进 walkability（MOVE-71 硬性要求）。证据链（walk mover sub_741224）完整覆盖。
- **MOVE-72（判定内部 + 硬编码地图"3"特例）**：**已复刻**。sub_768454 三分支（授予/NOTHROUGH/安全区）
  与 sub_7684DC 五臂（SAFE/多边形/nRange 闸/RedHome"3"半径/起点）逐条对应，地图"3"+845/674 半径特例保留。
- **MOVE-73（判定为 tick 缓存而非实时测试）**：**部分复刻/有界偏差**。已提供缓存字段与刷新方法承载模型形状；
  但铁律禁改 `TPlayObject.Message.cs`（`Run()` 即 sub_6B2D38），无法在 tick 内挂刷新，故刷新改在移动使用点即时调用
  （语义≈本 tick 首次移动前刷新，稳态移动一致）；**SM_2821(0xB05) 变化广播暂未复刻**。此为已记录的有界偏差（判定值一致，仅刷新时机/通知消息不同），非 fail-open 捏造。

---

## 四、已证但未接线的偏差（fail-closed 记录，跟进项）

**run 路径未使用穿透判定。** 原版 run mover sub_76756C 的占用探测(`0x7675BA`→sub_777EF8)与实际移动
(`0x767601`→sub_7797CC) **都**以缓存判定 Obj[+0x3FE] 作 boIgnoreOccupancy。C# 侧：
- `TPlayObject.RunTo` 的 `boFlag` 形参为**死参**，其 16 处 `CanWalkEx` 探测与 `CommitRunMove`(0x…MoveToMovingObjectForRun)
  改用 `boDiableHumanRun || (perm>9 && boGMRunAll)`。
- 默认 `boDiableHumanRun=false`、`boGMRunAll=true`：普通玩家 run boFlag=false（安全区**不可**穿人，与原版分歧）；GM=true（超集）。

未在本次接线的原因（fail-closed）：
1. MOVE-71 证据链只点名 walk mover(sub_741224)，run mover 属额外事实、非本三契约的既定链路；
2. 忠实修法应为 `boFlag = 判定 || boDiableHumanRun || (perm>9 && boGMRunAll)`（默认配置下普通玩家=判定，GM 超集保留），
   但涉及 `RunTo` 16 处内联 + `CommitRunMove` + `TBaseObject.CanRun`（与怪物共用）约 30+ 点，
   且 `boDiableHumanRun/boGMRunAll` 可能属另一 run 契约模型，单方面替换风险大；
3. 无「应删除 vs 应 OR」的确凿证据前，按「无证据 fail-closed」不擅动他契约模型。

建议：单开 run-through 契约，将上述 OR 修法一次性铺到 run 全路径并复核 `CanRun` 怪物侧。

**其它未接线消费者**：多处技能位移（0x673xxx/0x68Bxxx/0x71Axxx 等）同读 Obj[+0x3FE]，同属跟进项，
应待缓存在 tick 真实回写（需可改 Run 的集成方）后统一接线。
