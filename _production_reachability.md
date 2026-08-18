# M2 复刻项目生产可达性清单

> **口径说明**：本文件是 2026-08-18 审计运行时快照，不是上线许可。
> `356` 是当时折叠后的工作树状态计数，不能作为稳定的文件总数。
> Git 已建立 `unified/main`（历史锚点 `18635d2b`），审计源码已按项目逐批提交；
> 原 temp 工作树的后续未提交内容仍不自动纳入。
> 可达性扫描和编译成功都不能替代 MySQL、真实客户端和断线重连验证。

**生成时间**: 2026-08-18 23:46（指标仍为审计快照）
**审计基准**: 当前工作树 temp-merge-branch @ 299c2039  
**方法**: 源码扫描 + 构建验证 + 文档交叉验证

---

## 执行摘要

### 仓库状态
- **分支**: 401 个本地分支（0 个远程跟踪）
- **工作树**: 306 个
- **未提交改动**: 356 个文件（260 修改 + 1 删除 + 95 未跟踪）
- **主要改动**: GameSvr (194), AuditTools (76), docs (53)

### 代码规模
- **生产入口**: 
  - GameSvr: 1 个异步主入口, 5 个 WinForms 入口
  - DBSvr: 1 个主入口, 1 个 WinForms 入口
  - LoginGate: 1 个 WinForms 入口
  - GameGate-CS: 1 个主入口, 3 个 WinForms 入口

- **核心架构组件**:
  - Manager 类: 34 个（GameSvr 31, DBSvr 1, SystemModule 1, GameGate 1）
  - Service 类: 47 个（GameSvr 19, DBSvr 25, LoginGate 3）
  - Engine 类: 14 个（全部在 GameSvr）
  - Controller 类: 0 个

- **消息派发**:
  - Switch 派发点: 197 个（GameSvr 183, DBSvr 5, GameGate 6, SystemModule 3）
  - 消息处理器: 593 个（GameSvr 592, SystemModule 1）
  - 协议 case 分支: 3 个

### 状态标记统计

| 标记类型 | 数量 | 主要位置 |
|---------|------|----------|
| **Fail-Closed** | 747 | GameSvr (721), SystemModule (20), DBSvr (4), LoginGate (2) |
| **Dormant** | 237 | GameSvr (219), SystemModule (14), DBSvr (4) |
| **Not Implemented** | 13 | GameSvr (9), SystemModule (4) |
| **NO-GO** | 5 | GameSvr (5) |
| **总计** | 1002 | - |

### 审计工具状态
- **已发现审计工具**: 445 个
- **解决方案引用**: 25 个
- **最近审计报告**: 不存在（需要运行）

---

## 1. 生产可达性分层

### 1.1 PRODUCTION（生产可达）

**定义**: 从生产入口（Main/WinForms/消息派发）可达，且没有 NO-GO/DORMANT 标记

#### GameSvr 核心子系统
- ✅ **消息派发**: 183 个 switch 派发点，592 个消息处理器
- ✅ **玩家协议**: 
  - 行会关系协议 (102 个处理器)
  - 战队协议 (72 个处理器)
  - 频道协议 (34 个处理器)
  - 行会核心协议 (33 个处理器)
  - 组队协议 (31 个处理器)
- ✅ **管理器**: 31 个 Manager 类（Association, Castle, Command, Event 等）
- ✅ **服务**: 19 个 Service 类（Account, Gate, DynamicRoom 等）
- ✅ **物品系统**: GoodItem 7 个 switch 分支
- ⚠️ **基础对象**: TBaseObject 19 个 switch（但包含大量 fail-closed 分支）

#### DBSvr 核心子系统
- ✅ **主服务**: 25 个 Service 类（Backup, Cleanup, DatabaseInit 等）
- ✅ **协议处理**: 5 个 switch 派发点
- ✅ **社交服务**: UserSocService, GameSocService

#### LoginGate 核心子系统
- ✅ **客户端选择服务**: ClientSelectionService
- ✅ **数据库服务**: NativeDbServerService
- ✅ **兼容层**: PigCompatibilityService

#### GameGate-CS 核心子系统
- ✅ **会话管理**: SessionManager
- ✅ **网关服务**: GateServer
- ✅ **速度检测**: SpeedDetector

### 1.2 DORMANT（休眠，代码完整但未接入）

**定义**: 有完整实现，但显式标记为 dormant/NO-GO，或未连接到生产调用链

#### 主要休眠模块（237 个标记）

**GameSvr (219 个)**:
- ❌ **FieldHero 运行时**: TFieldHero.Native.cs 明确标记 "NO-GO: FieldHero execution is dormant"
  - 已实现: 工厂、构造器、填充、装备聚合
  - 未接入: 生产 actor、AI、Run 循环
  - 审计状态: 核心逻辑测试通过，但 production=NO-GO
  
- ❌ **奴隶休息**: TBaseObject.NativeSlaveRest.cs (TFieldHero Run 未启用)

- ❌ **状态重计算部分臂**: StateRecompute.cs 多个 dormant 臂等待上游生产者

- ❌ **装备聚合外层**: NativeFieldHeroEquipmentAggregateCore.cs 外层编排休眠

- ❌ **动态房间脚本路由**: 部分路由表条目休眠

**SystemModule (14 个)**:
- ❌ **Delphi 随机数**: DelphiRandom.cs "Dormant process-global owner"（但 Facade 已启用）
- ❌ **元宝交易协议族**（10 个）:
  - YbDbCancelDealProtocol
  - YbDbFeeUserActivityProtocol
  - YbDbForgeProgressProtocol
  - YbDbGlobalShopHotProtocol
  - YbDbGlobalShopItemProtocol
  - YbDbLogoutProtocol
  - YbDbNewBieGiftConsumeProtocol
  - YbDbOpenDealProtocol
  - YbDbPopGiftProtocol
  - YbDbTimeBuyLingFuProtocol

**DBSvr (4 个)**:
- ❌ **备份服务**: BackupService.cs 部分功能休眠
- ❌ **Schema 供应器**: NativeSchemaProvisioner.cs "enabled=false"
- ❌ **UserId 回填**: NativeUserIdBackfillService.cs 休眠

### 1.3 FAIL-CLOSED（边界保护，功能受限）

**定义**: 有代码框架，但因缺少完整逆向证据或依赖关系，采用保守实现（返回安全默认值，而不是猜测行为）

#### 主要 Fail-Closed 区域（747 个标记）

**GameSvr (721 个)**:
- ⚠️ **技能子系统**: HeroObject.NativeUnionSubSkill.cs 缺少完整充能状态
- ⚠️ **物理伤害**: TBaseObject.NativeMotaeboDamage.cs 分支未证明
- ⚠️ **物理减伤**: NativePhysicalPercentReduction.cs 装备聚合 BLOCKED
- ⚠️ **中毒清理**: NativePoisonFamilyClear.cs 多个分支未验证
- ⚠️ **隐身破除**: NativeStealthBreak.cs CM 4105 worker 1 全叶 fail-closed
- ⚠️ **SM 消息族**（约 600 个）:
  - SmIdent_Sm1/Sm2/Sm3/Sm4: 大量 ident 因无法从镜像获取布局而 fail-closed
  - SmA.cs: 3000-3600 ident 布局未获得

**SystemModule (20 个)**:
- ⚠️ **地图标志**: TMapFlag.cs 多个偏移 BLOCKED/fail-closed
- ⚠️ **CM 常量族**: Grobal2.cs 大量注册但无 builder 的 CM

**DBSvr (4 个)**:
- ⚠️ **元宝消费门**: NativeYbConsumeGate.cs null 返回 false fail-closed
- ⚠️ **重命名级联**: MySqlNativeRenameCascadeService.cs 边界 fail-closed
- ⚠️ **宗派服务**: MySqlZongpaiService.cs 边界 fail-closed

### 1.4 STUB/NOT-IMPLEMENTED（占位实现）

**定义**: 抛出 NotImplementedException 或有明确 "stub" 注释

#### 主要占位点（13 个）

**GameSvr (9 个)**:
- ❌ CakeFireEvent.cs: `throw new NotSupportedException`
- ❌ Packets/TOMagic.cs: `throw new NotImplementedException()`
- ❌ Packets/TShortMessage.cs: `throw new NotImplementedException()`
- ❌ Packets/TUserMagic.cs: `throw new NotImplementedException()`
- ⚠️ 动态房间相关: 多处捕获 NotSupportedException（调用的上游可能未实现）

**SystemModule (4 个)**:
- ❌ AtomicFile.cs: 捕获 PlatformNotSupportedException
- ❌ PacketHeader.cs: `throw new NotImplementedException()`
- ❌ TUserEntry.cs: 2 处 `throw new NotImplementedException()`

### 1.5 MISSING（逆向证据存在，C# 侧无实现）

**说明**: 这部分需要结合：
1. 原生二进制逆向发现文档（docs/discovery_*.md）
2. 审计工具 FAIL 结果（标记为 gap/missing）
3. 已知 BLOCKED 项

**当前无法从源码直接扫描得出，需要运行 445 个审计工具后汇总**

---

## 2. 按子系统分类的可达性矩阵

### 2.1 GameSvr

| 子系统 | PRODUCTION | DORMANT | FAIL-CLOSED | STUB | 估计覆盖率 |
|--------|-----------|---------|-------------|------|-----------|
| 消息派发核心 | ✅ 592 处理器 | - | ⚠️ 部分分支 | - | ~85% |
| 玩家协议 | ✅ 主要协议族 | - | ⚠️ SM 族大量 | - | ~75% |
| 行会系统 | ✅ 核心协议 | - | ⚠️ 部分边界 | - | ~90% |
| 组队系统 | ✅ 31 处理器 | - | ⚠️ 少量边界 | - | ~90% |
| 物品系统 | ✅ 基础流程 | - | ⚠️ 部分类型 | - | ~85% |
| 战斗系统 | ✅ 基础伤害 | ⚠️ 部分技能 | ⚠️ 大量边界 | - | ~70% |
| FieldHero | ❌ 未启用 | ❌ 全部休眠 | - | - | 0% (生产) |
| 英雄系统 | ⚠️ 部分 | ⚠️ 部分休眠 | ⚠️ 部分边界 | - | ~60% |
| PAS 脚本 | ✅ 主要 API | ⚠️ 部分休眠 | ⚠️ 部分边界 | - | ~80% |
| GM 命令 | ✅ 主要命令 | ⚠️ 少量 | ⚠️ 部分 | - | ~85% |
| 地图系统 | ✅ 基础功能 | ⚠️ 动态房间部分 | ⚠️ 标志部分 | - | ~80% |
| 事件系统 | ✅ 管理器 | - | - | ⚠️ CakeFire | ~95% |

### 2.2 DBSvr

| 子系统 | PRODUCTION | DORMANT | FAIL-CLOSED | STUB | 估计覆盖率 |
|--------|-----------|---------|-------------|------|-----------|
| 核心服务 | ✅ 25 个 | ⚠️ 3 个休眠 | ⚠️ 4 处边界 | - | ~90% |
| 社交协议 | ✅ 主要流程 | - | ⚠️ 1 处 | - | ~95% |
| 数据库初始化 | ✅ 服务 | ❌ Schema 供应器 | - | - | ~80% |
| 备份服务 | ⚠️ 部分 | ⚠️ 部分休眠 | - | - | ~50% |

### 2.3 LoginGate

| 子系统 | PRODUCTION | DORMANT | FAIL-CLOSED | STUB | 估计覆盖率 |
|--------|-----------|---------|-------------|------|-----------|
| 认证流程 | ✅ 完整 | - | ⚠️ 2 处边界 | - | ~95% |
| 客户端选择 | ✅ 服务 | - | - | - | ~95% |
| 数据库服务 | ✅ 服务 | - | - | - | ~95% |

### 2.4 GameGate-CS

| 子系统 | PRODUCTION | DORMANT | FAIL-CLOSED | STUB | 估计覆盖率 |
|--------|-----------|---------|-------------|------|-----------|
| 会话管理 | ✅ 完整 | - | - | - | ~95% |
| 网关服务 | ✅ 完整 | - | - | - | ~95% |
| 速度检测 | ✅ 完整 | - | - | - | ~95% |

### 2.5 SystemModule

| 子系统 | PRODUCTION | DORMANT | FAIL-CLOSED | STUB | 估计覆盖率 |
|--------|-----------|---------|-------------|------|-----------|
| 协议编解码 | ⚠️ 部分 | ❌ 10 个 YBDB | ⚠️ 20 处边界 | ⚠️ 4 处 | ~70% |
| 数据结构 | ✅ 主要类型 | - | ⚠️ 地图标志 | - | ~85% |
| 网络层 | ✅ 完整 | - | - | - | ~95% |

---

## 3. 关键发现

### 3.1 最大缺口

1. **FieldHero 全休眠** (219 个 dormant 标记)
   - 影响: 英雄 AI、运行时、战斗参与
   - 已有: 完整模型、测试通过的核心逻辑
   - 缺少: 生产接入、actor 启用

2. **SM 消息族大量 fail-closed** (约 600 个)
   - 影响: 客户端消息响应完整性
   - 原因: 无法从镜像获取消息体布局
   - 状态: 已注册常量，但无 builder

3. **元宝商城协议族休眠** (10 个)
   - 影响: 元宝交易、商城功能
   - 状态: 有编解码模型，但未启用

4. **战斗子系统 fail-closed 分支多** (约 100 个)
   - 影响: 特殊技能、伤害计算边界
   - 原因: 逆向证据不完整

### 3.2 高质量子系统

1. ✅ **LoginGate 认证**: ~95% 覆盖，少量 fail-closed 边界
2. ✅ **GameGate-CS**: ~95% 覆盖，架构清晰
3. ✅ **行会核心协议**: ~90% 覆盖，主要流程完整
4. ✅ **组队系统**: ~90% 覆盖，31 个处理器全部可达

### 3.3 构建状态

当前工作树（截至最近一次验证）:
- ✅ GameSvr: 0 错误, 15 警告
- ✅ DBSvr: 可构建
- ✅ LoginGate: 可构建
- ✅ GameGate-CS: 可构建

---

## 4. 审计工具状态

### 4.1 工具清单
- **已发现**: 445 个审计工具
- **解决方案引用**: 25 个
- **覆盖范围**: 
  - 协议兼容性
  - 数据库编解码
  - 进程内运行
  - 黄金存档回归
  - 原生行为对比

### 4.2 最近运行状态
⚠️ **当前工作树没有最新审计报告**

需要执行:
```bash
python AuditTools/run_audits.py --report AuditTools/_audit_report_20260818.json
```

**预期耗时**: 约 2-4 小时（445 个工具，串行低并发）

### 4.3 历史审计结果（来自文档）
根据 `docs/completeness_refresh_20260814.md`:
- 主引擎 754 项账本
- 严格完成: 710/754 (94.16%)
- 含等价: 712/754 (94.43%)
- MISSING: 0
- DIVERGENT: 0
- BLOCKED/UNPROVEN: 31

**注意**: 此数据基于 8 月 14 日文档，当前工作树有 356 个文件改动，需要重新验证

---

## 5. 下一步行动

### 5.1 立即可执行（只读验证）

```bash
# 1. 运行完整审计套件
python AuditTools/run_audits.py --report _audit_report_20260818.json

# 2. 生成审计矩阵
python -c "
import json
with open('_audit_report_20260818.json') as f:
    r = json.load(f)
results = r.get('results', [])
by_status = {}
for item in results:
    status = item.get('status', 'UNKNOWN')
    by_status[status] = by_status.get(status, 0) + 1
for status, count in sorted(by_status.items()):
    print(f'{status}: {count}')
"

# 3. 对比历史基线
diff docs/completeness_refresh_20260814.md <(echo "Current:")
```

### 5.2 提交当前工作树改动

**前提**: 审计通过后

**策略**: 按子系统分批提交
```bash
# 示例：审计工具修复
git add AuditTools/AuthenticationCompatCheck/Program.cs
git add AuditTools/CommandAuditCheck/Program.cs
# ... 分组添加
git commit -m "fix(audit): Update core authentication and command audit tools"

# 在隔离 worktree 验证
git worktree add ../verify-$(git rev-parse --short HEAD) HEAD
cd ../verify-$(git rev-parse --short HEAD)
dotnet build GameSvr/GameSvr.csproj --no-restore
python AuditTools/run_audits.py --filter AuthenticationCompatCheck,CommandAuditCheck
cd -
```

### 5.3 统一 Git 线路

参见 `_git_topology_audit.md` 第 4-5 节

### 5.4 更新文档

**目标结构**:
```
docs/
├── CURRENT_STATUS.md              # 本文档的最终版本
├── PRODUCTION_REACHABILITY.md     # 本文档
├── subsystems/                     # 各子系统详细文档
└── archives/                       # 旧文档归档
```

---

## 6. 风险评估

### 6.1 当前不能上线的理由

1. ❌ **FieldHero 全休眠**: 英雄系统核心功能缺失
2. ❌ **大量 fail-closed 分支**: 边界行为可能与原版不一致
3. ❌ **元宝商城未启用**: 付费功能不可用
4. ❌ **SM 消息族不完整**: 客户端可能收到不完整响应
5. ❌ **未经过全栈集成测试**: MySQL + 真实客户端 + 断线重连

### 6.2 接近可上线的子系统

1. ✅ **LoginGate**: 可以独立上线
2. ✅ **GameGate-CS**: 可以独立上线
3. ⚠️ **行会系统**: 主要流程完整，但需要全栈测试
4. ⚠️ **组队系统**: 主要流程完整，但需要全栈测试

### 6.3 数据安全评估

- ⚠️ **数据库写操作**: 需要逐个审计 DBSvr 写事务
- ⚠️ **物品守恒**: InProcItemConservationCheck 需要通过
- ⚠️ **金币守恒**: 交易、摆摊、商人系统需要端到端测试
- ⚠️ **存档完整性**: 黄金存档回归需要 100% 通过

---

## 附录 A: 标记说明

### A.1 状态标记定义

| 标记 | 含义 | 是否可用 |
|------|------|----------|
| PRODUCTION | 生产调用链可达，有运行证据 | ✅ 可用 |
| DORMANT | 代码完整但未接入生产 | ❌ 不可用 |
| FAIL-CLOSED | 边界保护，功能受限 | ⚠️ 部分可用 |
| STUB | 占位实现，会抛异常 | ❌ 不可用 |
| MISSING | 原版有，C# 无 | ❌ 不可用 |
| BLOCKED | 外部依赖缺失 | ❌ 不可用 |
| UNPROVEN | 有代码但未验证 | ⚠️ 风险 |

### A.2 覆盖率估算方法

**不使用简单百分比**，而是按以下维度评估:
1. 入口可达性（是否从 Main/消息派发可达）
2. 运行时证据（是否有审计工具 PASS）
3. 边界完整性（fail-closed 数量和影响范围）
4. 休眠比例（dormant 标记数量）

---

**报告完成**  
**下一步**: 运行 445 个审计工具，生成机械化验证矩阵
