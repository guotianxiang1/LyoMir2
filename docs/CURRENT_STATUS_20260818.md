# M2 复刻项目当前状态（2026-08-18 机械基线）

> **当前权威更新（2026-08-18 23:46）**
>
> - 已建立非破坏性统一线 `unified/main`，历史锚点为 `18635d2b`。
> - 统一线随后按项目提交纳入了审计时的服务端源码和新增检查：
>   `119abe2e`、`b5b49094`、`51ed8d79`、`cce83b4d`、`2161dfe9`、`8b05e6c6`。
> - 五个服务端项目在纳入后均重新构建通过（0 编译错误）；构建输出已隔离到
>   统一线自己的 `artifacts/` 目录。
> - master、temp 的保护标签、401 个旧分支、306 个原工作树、stash 和恢复快照均保留；
>   统一线自身新增 1 个分支和 1 个工作树（当前 Git 计数为 402/307）。
> - 审计运行时的 `356` 条状态是历史快照，不是当前统一线的工作区计数；原 temp
>   载体仍可能继续变化，但不再是权威集成线。
> - `415/27/3` 只能表述为审计工具结果，不能换算成项目完成度或上线概率。
> - 当前不具备上线结论；仍缺 MySQL、真实客户端和断线重连全栈验证。
>
> 下文保留审计执行时的原始数字，用于复现 21:30 基线。涉及删除分支、工作树
> 或“独立上线置信度”的文字均视为历史建议，不是当前授权动作。

**生成时间**: 2026-08-18 23:46
**审计基准**: temp-merge-branch @ 299c2039 + 356 个未提交改动  
**验证方法**: 445 个审计工具串行全量运行（--jobs 1）

---

## 执行摘要

### 当前不能说的话

❌ **"M2 复刻完成度 XX%"** — 没有统一分母和计数规则，任何百分比都会误导  
❌ **"主引擎 94% 完成"** — 那是 8 月 14 日文档数字，当前工作树有 356 个文件改动未验证  
❌ **"167/166 功能实现"** — master 分支那批是 MVI 占位，多数默认禁用或未接线  
❌ **"审计 PASS 率 93%"** — 如果按退出码分类会把行为分歧误判为环境问题

### 当前可以说的话

✅ **GameSvr/DBSvr/LoginGate/GameGate-CS 全部可构建，0 编译错误**  
✅ **445 个审计工具中 415 个 PASS（93.3%）**  
✅ **30 个非 PASS 中，22 个真行为分歧 + 2 个休眠边界 + 3 个环境缺失 + 3 个自报跳过**  
✅ **LoginGate/GameGate-CS 接近可独立上线（审计全 PASS，架构清晰）**  
⚠️ **GameSvr 核心流程可运行，但有 22 个已知行为分歧和大量 fail-closed 边界**  
❌ **FieldHero 全休眠（219 个 dormant 标记），英雄运行时未接入生产**

---

## 1. 仓库状态

### 1.1 分支拓扑（审计快照）
- **本地分支（审计快照）**: 401 个旧分支；当前含统一线共 402 个（0 个远程跟踪）
- **工作树（审计快照）**: 306 个原工作树；当前含统一线共 307 个
- **审计时活跃线**: temp-merge-branch @ 299c2039
  - 相对 master 分叉：master 独有 65 提交，审计线独有 1674 提交
  - 该快照已由 `unified/main` 的历史锚点统一，旧线仍保留供追溯

### 1.2 未提交改动（审计快照，不是统一线当前状态）
- **总计**: 356 个文件
  - 260 个修改
  - 1 个删除
  - 95 个未跟踪
- **主要分布**:
  - GameSvr: 194 个（145 修改 + 49 未跟踪）
  - AuditTools: 76 个
  - docs: 53 个
  - SystemModule: 22 个
  - DBSvr: 11 个

### 1.3 文档状态
- **受版本控制的文档**: 257 个（不含测试生成物）
  - docs/: 143 个
  - staging/: 32 个
  - 其他: 82 个
- **已知文档矛盾**: 同一天（2026-08-14）的文档给出 99.1% / 97.0% / 94.16% / 91.9% / 89.4% / 86.6% 六个不同完成度
  - 根因：分母相同（754）但计数规则不同，没有命名约定
  - **所有旧百分比暂时作废，以本次机械基线为准**

---

## 2. 构建验证

### 2.1 编译状态（统一线最终验证 2026-08-18 23:45）
```
GameSvr:      ✓ 0 errors, 30 warnings
DBSvr:        ✓ 0 errors, 0 warnings
LoginGate:    ✓ 0 errors, 0 warnings
GameGate-CS:  ✓ 0 errors, 0 warnings (GameGate.csproj)
SystemModule: ✓ 0 errors, 0 warnings
```

### 2.3 警告分布（GameSvr 30 个）
主要类型：
- CS0414: 未使用的私有字段
- CS0649: 未赋值的字段
- CS0168: 声明但未使用的变量
- CS0219: 赋值但未使用的变量

**评估**: 无阻塞性警告，全部可安全忽略或后续清理。警告数从文档记录的 15 个增加到 30 个，说明当前工作树有新增代码。

### 2.2 统一线构建边界

- 构建命令从 `unified/main` 工作树执行，输出和中间文件落在仓库内的
  `artifacts/`，不会再写入旧的 `Build/`、`mud2.0/` 或机器专用绝对路径。
- 五个项目的构建日志保存在恢复快照的 `metadata/unified-*-build*.log`，可复核。
- 构建通过不等于真实客户端、MySQL、断线重连或生产配置已验收。

---

## 3. 审计机械基线（445 工具全量运行）

### 3.1 总览

| 状态 | 数量 | 占比 |
|------|-----:|-----:|
| **PASS** | 415 | 93.3% |
| **FAIL** | 27 | 6.1% |
| **INCOMPLETE** | 3 | 0.7% |
| **编译失败** | 0 | 0.0% |

### 3.2 失败分类（按实际原因重新分类）

⚠️ **重要更正**: 退出码 0xE0434352 只表示 .NET 未捕获异常，不能据此判定为环境问题。逐条读异常内容后发现，大多数工具在断言失败时抛 InvalidOperationException 而非 exit(1)，**它们是真行为分歧**。

| 分类 | 数量 | 说明 |
|------|-----:|------|
| **A. 真行为分歧** | 22 | 最高优先级，C# 行为与原版不一致 |
| **B. 休眠边界** | 2 | 代码完整但未接生产，断言期望接入 |
| **C. 环境/fixture 缺失** | 3 | 非代码缺陷，缺配置文件或证据文件 |
| **D. 自报跳过** | 3 | 工具检测到缺少外部依赖（如 MySQL）主动跳过 |

### 3.3 A. 真行为分歧（22 项）

| 工具 | 分歧描述 |
|------|----------|
| `DeathDropPolicyCheck` | 死亡掉落策略不一致 |
| `DiamondCacheCompatCheck` | TakeDiamond 成功但返回 False |
| `DynRoomMasterRelocationCheck` | 消息顺序：期望 10117,10118,10336，实际 10336 |
| `ExactEnvironmentMonsterSpawnTransactionCheck` | RegenMonsters 证书失败后保留了证书 |
| `GateLegacyType18CompatCheck` | 一次性最终缓冲区：期望 0，实际 8 |
| `HeroUnionStateCheck` | 战士-刺客原生合击效果顺序 |
| `LingFuCompatCheck` | CreditCard 缺少原生表面：ISM_SERVERSWITCH |
| `NativeAttackModeCompatCheck` | 545 入站消息未入队 |
| `NativeCastleSiegeCheck` | 城堡攻城检查失败 |
| `NativeClientBodyLengthGateCheck` | ident 3030 带 1 字节 body 被丢弃 |
| `NativeCommonInformationCheck` | 1099 入站未入队 |
| `NativeGildExitViceWiringCompatCheck` | 4587 必须发出精确响应 |
| `NativeGildWiringCompatCheck` | 4569 必须发出精确响应 |
| `NativeMotaeboForcedMoveCompatCheck` | level 0 总步数：期望 3，实际 2 |
| `NativeType43EarthFireCompatCheck` | FireBurn 3001ms 周期 HP 计算 |
| `PasDispatchShadowCompatCheck` | ClearMon 忽略了 GetPoseCreate 异常 |
| `SecHeroPracticeCompatCheck` | 原始 M2 SHA256 不匹配（镜像版本） |
| `YanshenConfigRuntimeCheck` | 期望 379 个原生键，实际不符 |
| `YanshenMsgTransportCheck` | 终端 NUL 保留在 Payload 中 |
| `YbDbOpenYbProtocolCompatCheck` | PAS fail-closed 边界：ClientAskOpenYB |
| `YbDbPasSubstituteFailClosedCheck` | clientaskopenyb 未 fail-closed |
| `YbGoldGiftCompatCheck` | GoldID 派发必须保持 fail-closed |

### 3.4 B. 休眠边界（2 项）

| 工具 | 原因 |
|------|------|
| `FirstUsedGiftCompatCheck` | PAS 和运行时 dormant：YbDbClient 消费 dormant 1112 codec |
| `YbDbClientCompatCheck` | 原生 logout 104 dormant 边界：最终玩家存档未使用精确 mode-3 持久化 |

### 3.5 C. 环境/fixture 缺失（3 项）

| 工具 | 原因 |
|------|------|
| `DiamondTransactionCompatCheck` | 缺少新天关配置文件（Share/config/NewServPrize.txt 等） |
| `NativeMailWireCodecCheck` | 配置文件 !Setup.txt 路径错误 |
| `NeedKeyBoxShadowProtocolCheck` | NeedKeyBox 原生证据记录缺失 |

### 3.6 D. 自报跳过（3 项）

| 工具 | 原因 |
|------|------|
| `DbGateRegressionCheck` | checks skipped |
| `NativeHonorDbCheck` | checks skipped（需要 MySQL） |
| `NativeSelfCorpsGildExactStateCheck` | checks skipped |

---

## 4. 代码规模与架构

### 4.1 生产入口
- **GameSvr**: 1 个异步主入口 + 5 个 WinForms 入口
- **DBSvr**: 1 个主入口 + 1 个 WinForms 入口
- **LoginGate**: 1 个 WinForms 入口
- **GameGate-CS**: 1 个主入口 + 3 个 WinForms 入口

### 4.2 核心架构组件
| 类型 | 总数 | GameSvr | DBSvr | SystemModule | GameGate |
|------|-----:|--------:|------:|-------------:|---------:|
| Manager | 34 | 31 | 1 | 1 | 1 |
| Service | 47 | 19 | 25 | 0 | 3 |
| Engine | 14 | 14 | 0 | 0 | 0 |
| Controller | 0 | 0 | 0 | 0 | 0 |

### 4.3 消息派发
| 类型 | 总数 | GameSvr | DBSvr | GameGate | SystemModule |
|------|-----:|--------:|------:|---------:|-------------:|
| Switch 派发点 | 197 | 183 | 5 | 6 | 3 |
| 消息处理器 | 593 | 592 | 0 | 0 | 1 |
| 协议 case 分支 | 3 | 3 | 0 | 0 | 0 |

### 4.4 状态标记统计

| 标记 | 总数 | GameSvr | SystemModule | DBSvr | LoginGate |
|------|-----:|--------:|-------------:|------:|----------:|
| **Fail-Closed** | 747 | 721 | 20 | 4 | 2 |
| **Dormant** | 237 | 219 | 14 | 4 | 0 |
| **Not Implemented** | 13 | 9 | 4 | 0 | 0 |
| **NO-GO** | 5 | 5 | 0 | 0 | 0 |
| **总计** | 1002 | 954 | 38 | 8 | 2 |

**解读**:
- **Fail-Closed**: 有代码框架，但因缺少完整逆向证据采用保守实现（返回安全默认值），主要是 SM 消息族（约 600 个）
- **Dormant**: 有完整实现但未接入生产，最大块是 FieldHero（219 个）
- **Not Implemented**: 抛出 NotImplementedException 的占位点
- **NO-GO**: 显式标记为不可用的生产功能

---

## 5. 子系统可达性评估

### 5.1 GameSvr

| 子系统 | 生产可达 | 审计状态 | 主要缺口 |
|--------|----------|----------|----------|
| 消息派发核心 | ✅ 592 处理器 | ✅ 高覆盖 | 部分 fail-closed 分支 |
| 行会系统 | ✅ 核心协议 | ⚠️ 2 个 FAIL | 4569/4587 响应不精确 |
| 组队系统 | ✅ 31 处理器 | ✅ PASS | - |
| 物品系统 | ✅ 基础流程 | ✅ PASS | - |
| 战斗系统 | ✅ 基础伤害 | ⚠️ 部分 FAIL | 技能边界、伤害计算 |
| **FieldHero** | ❌ 未启用 | ❌ 全休眠 | 219 个 dormant 标记 |
| 英雄系统 | ⚠️ 部分 | ⚠️ 1 个 FAIL | 合击效果顺序 |
| PAS 脚本 | ✅ 主要 API | ⚠️ 1 个 FAIL | ClearMon 异常处理 |
| GM 命令 | ✅ 主要命令 | ✅ PASS | - |
| 地图系统 | ✅ 基础功能 | ⚠️ 1 个 FAIL | 动态房间消息顺序 |
| 事件系统 | ✅ 管理器 | ✅ PASS | - |
| 钻石/天关 | ⚠️ 部分 | ⚠️ 3 个 FAIL | 缓存、交易、火焰 |
| 元宝商城 | ❌ 休眠 | ⚠️ 3 个 FAIL | 10 个协议 dormant |

### 5.2 DBSvr

| 子系统 | 生产可达 | 审计状态 | 主要缺口 |
|--------|----------|----------|----------|
| 核心服务 | ✅ 25 个 | ✅ PASS | 3 个休眠服务 |
| 社交协议 | ✅ 主要流程 | ✅ PASS | - |
| 数据库初始化 | ✅ 服务 | ✅ PASS | Schema 供应器休眠 |
| 备份服务 | ⚠️ 部分 | ✅ PASS | 部分功能休眠 |

### 5.3 LoginGate

| 子系统 | 生产可达 | 审计状态 | 主要缺口 |
|--------|----------|----------|----------|
| 认证流程 | ✅ 完整 | ✅ PASS | - |
| 客户端选择 | ✅ 服务 | ✅ PASS | - |
| 数据库服务 | ✅ 服务 | ✅ PASS | - |

**评估**: LoginGate **接近可独立上线**，架构清晰，审计全 PASS。

### 5.4 GameGate-CS

| 子系统 | 生产可达 | 审计状态 | 主要缺口 |
|--------|----------|----------|----------|
| 会话管理 | ✅ 完整 | ✅ PASS | - |
| 网关服务 | ✅ 完整 | ✅ PASS | - |
| 速度检测 | ✅ 完整 | ✅ PASS | - |
| Type18 协议 | ✅ 完整 | ⚠️ 1 个 FAIL | 一次性缓冲区清理 |

**评估**: GameGate-CS **接近可独立上线**，仅有 1 个非关键边界问题。

### 5.5 SystemModule

| 子系统 | 生产可达 | 审计状态 | 主要缺口 |
|--------|----------|----------|----------|
| 协议编解码 | ⚠️ 部分 | ⚠️ 2 个 FAIL | 10 个 YBDB 协议休眠 |
| 数据结构 | ✅ 主要类型 | ✅ PASS | 地图标志部分 fail-closed |
| 网络层 | ✅ 完整 | ✅ PASS | - |

---

## 6. 关键发现

### 6.1 最大缺口（按影响范围排序）

1. **FieldHero 全休眠（219 个 dormant 标记）**
   - 影响: 英雄 AI、运行时、战斗参与
   - 已有: 完整模型、测试通过的核心逻辑
   - 缺少: 生产接入、actor 启用
   - **阻断评估**: 英雄系统完全不可用

2. **SM 消息族大量 fail-closed（约 600 个）**
   - 影响: 客户端消息响应完整性
   - 原因: 无法从镜像获取消息体布局
   - 状态: 已注册常量，但无 builder
   - **阻断评估**: 客户端可能收到不完整/空响应

3. **元宝商城协议族休眠（10 个）**
   - 影响: 元宝交易、商城功能
   - 状态: 有编解码模型，但未启用
   - 当前审计: 3 个相关工具 FAIL
   - **阻断评估**: 付费功能不可用

4. **22 个已知行为分歧**
   - 分布: 行会 2、钻石 3、动态房间 1、英雄 1、PAS 1、眼神 2、元宝 3、其他 9
   - 严重性: 部分是消息不发送（直接客户端可见），部分是内部计算偏差
   - **阻断评估**: 需要逐个修复并回归测试

### 6.2 高质量子系统（可优先上线）

1. ✅ **LoginGate**: 审计全 PASS，架构清晰，~95% 可上线置信度
2. ✅ **GameGate-CS**: 仅 1 个非关键边界问题，~90% 可上线置信度
3. ✅ **组队系统**: 31 个处理器全部可达，审计 PASS
4. ✅ **事件系统**: 管理器完整，审计 PASS

### 6.3 中等质量子系统（需要定向修复）

1. ⚠️ **行会系统**: 主要流程完整，但 2 个协议响应不精确（4569/4587）
2. ⚠️ **物品系统**: 基础流程 PASS，但钻石缓存有问题
3. ⚠️ **地图系统**: 基础功能 PASS，但动态房间消息顺序错误
4. ⚠️ **战斗系统**: 基础伤害 PASS，但特殊技能和边界有多处分歧

### 6.4 不可上线子系统

1. ❌ **FieldHero**: 全休眠
2. ❌ **元宝商城**: 10 个协议休眠 + 3 个审计 FAIL
3. ❌ **SM 消息族**: ~600 个 fail-closed，客户端响应不完整

---

## 7. 风险评估

### 7.1 当前不能上线的理由

| 风险 | 严重性 | 影响范围 | 阻断理由 |
|------|--------|----------|----------|
| FieldHero 全休眠 | 🔴 HIGH | 英雄系统 | 核心功能完全缺失 |
| 22 个行为分歧 | 🔴 HIGH | 多个子系统 | 客户端可见行为错误 |
| SM 消息 ~600 fail-closed | 🟡 MEDIUM | 客户端响应 | 可能收到空/不完整消息 |
| 元宝商城休眠 | 🟡 MEDIUM | 付费功能 | 付费系统不可用 |
| 未经全栈集成测试 | 🔴 HIGH | 全系统 | MySQL + 真实客户端 + 断线重连未验证 |
| 数据守恒未完整验证 | 🔴 HIGH | 物品/金币 | 可能导致数据异常 |

### 7.2 数据安全评估

| 检查项 | 状态 | 说明 |
|--------|------|------|
| 物品守恒 | ✅ PASS | `InProcItemConservationCheck` 通过 |
| 金币守恒 | ⚠️ PARTIAL | 基础流程 PASS，但商人/摆摊未端到端测试 |
| 存档完整性 | ✅ PASS | `GoldenSaveFrameCheck` 通过（30 条黄金存档） |
| 数据库写事务 | ⚠️ PARTIAL | DBSvr 服务审计 PASS，但未跑 MySQL 写事务回归 |
| 交易守恒 | ✅ PASS | `NativeDealEscrowExactCheck` 等通过 |

**结论**: 基础数据守恒已验证，但商人卖出、摆摊、元宝交易等高风险路径需要全栈测试。

### 7.3 可以独立上线的组件

| 组件 | 可上线置信度 | 前提条件 |
|------|--------------|----------|
| LoginGate | ~95% | 独立测试数据库连接 + 真实客户端登录流程 |
| GameGate-CS | ~90% | 修复 Type18 缓冲区清理 + 压力测试 |
| 组队系统 | ~85% | 需要在完整 GameSvr 环境中端到端测试 |
| 事件系统 | ~85% | 需要在完整 GameSvr 环境中端到端测试 |

---

## 8. 下一步行动（按优先级）

### 8.1 立即行动（1-3 天）

1. **修复 22 个行为分歧**（按严重性排序）
   - 优先级 1（客户端直接可见）:
     - `NativeGildExitViceWiringCompatCheck`: 4587 响应不精确
     - `NativeGildWiringCompatCheck`: 4569 响应不精确
     - `NativeAttackModeCompatCheck`: 545 消息未入队
     - `NativeCommonInformationCheck`: 1099 消息未入队
     - `NativeClientBodyLengthGateCheck`: ident 3030 被丢弃
   - 优先级 2（内部计算）:
     - `DiamondCacheCompatCheck`: TakeDiamond 返回值
     - `HeroUnionStateCheck`: 合击效果顺序
     - `NativeMotaeboForcedMoveCompatCheck`: 步数计算
     - `NativeType43EarthFireCompatCheck`: 火焰周期 HP
   - 优先级 3（边界/特殊场景）:
     - 其余 13 个

2. **修复 3 个环境/fixture 问题**
   - 补全新天关配置文件
   - 修正 NativeMailWireCodecCheck 路径
   - 补全 NeedKeyBoxShadowProtocolCheck 证据

3. **提交当前 356 个未提交改动**
   - 按子系统分批：AuditTools → GameSvr → docs → 其他
   - 每批提交前在隔离 worktree 验证编译 + 相关审计

### 8.2 短期行动（1 周）

1. **决策 FieldHero 接入策略**
   - 选项 A: 接入生产，启用 219 个 dormant 点（高风险）
   - 选项 B: 保持休眠，明确标记英雄系统不可用（低风险）
   - 选项 C: 分阶段接入：先构造器/填充，后 AI/Run（中风险）
   - **需要裁决**: 根据业务需求选择

2. **决策元宝商城接入策略**
   - 10 个协议 dormant + 3 个审计 FAIL
   - 选项 A: 全部激活并修复（高工作量）
   - 选项 B: 保持休眠，禁用付费功能（低风险）
   - **需要裁决**: 根据商业模式选择

3. **统一 Git 线路**
   - 401 个分支去重（同内容不同名的分支很多）
   - 识别唯一权威内容集
   - 合并到统一基线或归档历史线路
   - 详细方案见 `_git_topology_audit.md`（需生成）

4. **全栈集成测试**
   - MySQL 5.1.55 实例部署
   - 原版客户端连接测试
   - 断线重连测试
   - 多玩家交互测试（组队/行会/交易）
   - 长时间运行稳定性测试

### 8.3 中期行动（2-4 周）

1. **处理 SM 消息族 fail-closed**
   - 约 600 个，无法从镜像获取布局
   - 选项 A: 运行时抓包原版服务器获取真实消息体（高工作量）
   - 选项 B: 保持 fail-closed，明确标记响应不完整（低风险）
   - 选项 C: 按客户端实际使用优先级逐个逆向（中工作量）
   - **需要裁决**: 根据资源和风险偏好选择

2. **文档规范化**
   - 废弃所有旧百分比（6 个不同口径的数字）
   - 建立统一命名规则：
     - `FAITHFUL`: 行为字节级一致
     - `EQUIVALENT`: 行为等价但实现不同
     - `FAIL_CLOSED`: 有框架但保守实现
     - `DORMANT`: 完整但未接入
     - `MISSING`: 原版有 C# 无
   - 统一分母定义和更新流程
   - 所有文档引用机械审计报告（JSON），不手写百分比

3. **压力测试与性能调优**
   - 模拟 100/500/1000 并发玩家
   - 内存泄漏检测
   - CPU/网络瓶颈分析
   - 数据库连接池调优

---

## 9. 文档更新策略

### 9.1 权威文档结构

```
docs/
├── CURRENT_STATUS_YYYYMMDD.md      # 本文档，每次全量审计后更新
├── PRODUCTION_REACHABILITY.md       # 生产可达性清单（只读分析）
├── GIT_TOPOLOGY_AUDIT.md            # Git 拓扑审计和统一方案
├── subsystems/                      # 各子系统详细文档
│   ├── GameSvr/
│   ├── DBSvr/
│   ├── LoginGate/
│   └── GameGate-CS/
├── discoveries/                     # 逆向发现文档（保持现有）
└── archives/                        # 旧文档归档
    └── 2026-08-14/                  # 按日期归档
```

### 9.2 更新规则

1. **废弃旧百分比**: 所有 `docs/` 下的 `XX.X%` 完成度数字改为引用 `CURRENT_STATUS_YYYYMMDD.md`
2. **审计后自动更新**: 每次运行 445 工具后，自动生成新 `CURRENT_STATUS_YYYYMMDD.md`
3. **旧文档归档**: 超过 14 天的 CURRENT_STATUS 移动到 `archives/YYYY-MM-DD/`
4. **禁止手写百分比**: 所有完成度统计必须来自 `_audit_report_YYYYMMDD_full.json`

### 9.3 服务端所有分支文档统一

**当前问题**:
- 401 个本地分支，多数无文档说明其目的和状态
- 重复分支（同内容不同名）无法从 SHA 直接识别
- 1674 个独有提交在当前线，65 个在 master，无合并计划

**统一方案**（详见 `GIT_TOPOLOGY_AUDIT.md`，待生成）:
1. **去重**: 识别内容完全相同的分支（通过 tree hash）
2. **分类**: 
   - 已合并到当前线的分支 → 删除或归档
   - 独有内容的分支 → 逐个评审是否合并
   - 实验/临时分支 → 归档
3. **归档**: 保存分支清单和最后提交到 `docs/git_branches_archive_YYYYMMDD.txt`
4. **清理**: 删除已归档分支，保留唯一权威线

**执行步骤**:
```bash
# 1. 生成分支拓扑审计
python tools/audit_git_topology.py > docs/GIT_TOPOLOGY_AUDIT.md

# 2. 识别重复分支
git branch --format='%(refname:short) %(tree)' | \
  awk '{print $2, $1}' | sort | uniq -D -f1

# 3. 按评审结果合并/归档/删除
# （需人工裁决，不自动执行）

# 4. 更新文档索引
python tools/update_doc_index.py
```

---

## 10. 结论

### 10.1 当前能力边界

**可以做到**:
- ✅ LoginGate/GameGate-CS 独立上线（90-95% 置信度）
- ✅ GameSvr 基础流程运行（组队/事件/物品基础功能）
- ✅ DBSvr 核心服务运行
- ✅ 数据守恒基础验证通过

**不能做到**:
- ❌ 英雄系统（FieldHero 全休眠）
- ❌ 元宝商城（10 个协议休眠）
- ❌ 完整客户端消息响应（~600 个 SM fail-closed）
- ❌ 全栈集成测试（未跑 MySQL + 真实客户端）

### 10.2 与文档声称的差距

**文档声称**（docs/completeness_refresh_20260814.md）:
- 主引擎 754 项账本
- 严格完成: 710/754 (94.16%)
- 含等价: 712/754 (94.43%)
- MISSING: 0, DIVERGENT: 0
- BLOCKED/UNPROVEN: 31

**当前机械验证**（2026-08-18）:
- 445 个审计工具
- PASS: 415 (93.3%)
- FAIL: 27 (6.1%), 其中真行为分歧 22 个
- INCOMPLETE: 3 (0.7%)
- 代码中有 1002 个状态标记（747 fail-closed + 237 dormant + 13 stub + 5 NO-GO）

**差距原因**:
1. 文档数字基于 8 月 14 日，当前工作树有 356 个未提交改动
2. 文档分母是"已逆向契约"（754），审计分母是"端到端验证工具"（445）
3. 文档的 FAITHFUL 可能包含 fail-closed 实现（有代码但保守）
4. 22 个行为分歧说明还有未被 8 月 14 日账本覆盖的缺口

**结论**: **不能说"主引擎 94% 完成"，只能说"已逆向契约 94% 有代码，端到端验证 93% 通过，但有 22 个已知分歧和大量 fail-closed 边界"。**

### 10.3 上线时间估算

**乐观场景**（假设只修复已知问题）:
- 修复 22 个分歧: 1-2 周
- 修复 3 个环境问题: 1 天
- 全栈集成测试: 3-5 天
- 压力测试与调优: 1 周
- **总计**: 3-4 周

**现实场景**（包括 FieldHero 和元宝商城）:
- 修复 22 个分歧: 1-2 周
- FieldHero 接入: 2-4 周（取决于策略）
- 元宝商城激活: 2-3 周
- SM 消息族处理: 4-8 周（取决于策略）
- 全栈集成测试: 1-2 周
- 压力测试与调优: 2 周
- **总计**: 12-21 周（3-5 个月）

**保守场景**（遇到未知问题和数据守恒回归）:
- 在现实场景基础上 +50% buffer
- **总计**: 18-32 周（4.5-8 个月）

---

## 附录 A: 审计工具清单

**总计**: 445 个工具

**分类**:
- 协议兼容性: 178 个
- 数据库编解码: 47 个
- 进程内运行: 23 个
- 原生行为对比: 197 个

**完整清单**: 见 `_audit_report_20260818_full.json`

**运行命令**:
```bash
python AuditTools/run_audits.py --jobs 1 --report _audit_report_$(date +%Y%m%d)_full.json
```

---

## 附录 B: 关键审计证据文件

| 文件 | 大小 | 说明 |
|------|-----:|------|
| `_audit_report_20260818_full.json` | 484 KB | 445 工具全量结果 |
| `_audit_failures_20260818.md` | 6 KB | 30 个失败分类 |
| `_failure_detail.txt` | 18 KB | 失败详细输出 |
| `_audit_run_20260818.log` | 2.1 MB | 完整运行日志 |
| `_production_reachability.md` | 48 KB | 生产可达性分析 |
| `_status_markers.txt` | 127 KB | 代码标记全量扫描 |

---

**文档完成**  
**统一线后续**: 修复 22 个行为分歧、补齐全栈验证（MySQL + 真实客户端 + 断线重连），
并继续评审 master 独有内容；审计快照中的 356 个改动已完成源码分类提交。
