# M2 复刻项目文档索引

**最后更新**: 2026-08-18 23:46

---

## ⚠️ 重要声明

**唯一权威现状文档**: [CURRENT_STATUS_20260818.md](CURRENT_STATUS_20260818.md)

其他包含“完成度百分比”的文档目前仍可能存在于历史分支和工作树中，并未物理删除
或全部移动。它们只可作为历史证据，不得作为当前状态或上线依据。

Git 的唯一集成线是 `unified/main`（历史锚点 `18635d2b`，源码同步提交见权威基线文档）。
旧分支和工作树均被保留；“统一”表示建立新的权威集成入口，不表示已经删除旧线路。
审计时的源码 WIP 已按项目提交，生成物和未审删除仍不纳入主线。

---

## 当前状态（权威）

### 核心文档

| 文档 | 用途 | 更新频率 |
|------|------|----------|
| [UNIFIED_BASELINE_20260818.md](UNIFIED_BASELINE_20260818.md) | 统一线、恢复快照与安全边界 | 基线变更时 |
| [CURRENT_STATUS_20260818.md](CURRENT_STATUS_20260818.md) | 项目当前状态（机械基线） | 每次全量审计后 |
| [PRODUCTION_REACHABILITY.md](../_production_reachability.md) | 生产可达性分析 | 按需 |
| [GIT_TOPOLOGY_AUDIT.md](GIT_TOPOLOGY_AUDIT.md) | Git 仓库统一方案 | 一次性 |

### 关键指标（来自 CURRENT_STATUS_20260818.md）

- **构建状态**: 5 个项目全部 ✓ 0 错误
- **审计基线**: 445 工具，415 PASS (93.3%)，27 FAIL，3 INCOMPLETE
- **真行为分歧**: 22 个（最高优先级修复）
- **代码标记**: 1002 个（747 fail-closed + 237 dormant + 13 stub + 5 NO-GO）
- **FieldHero 状态**: ❌ 全休眠（219 个 dormant 标记）
- **元宝商城状态**: ❌ 10 个协议 dormant

**结论**: 不能说"94% 完成"，只能说"93% 审计通过，有 22 个已知分歧和大量边界保护"。

---

## 子系统文档

### GameSvr
- 待补充

### DBSvr
- 待补充

### LoginGate
- 待补充

### GameGate-CS
- 待补充

---

## 逆向发现文档（保持现有）

这些文档记录原版行为的逆向分析，保持不变：

- `discovery_*.md` - 原版功能发现
- `eqv_*.md` - 等价性分析
- `ys_*.md` - 眼神插件分析
- 其他逆向文档

**注意**: 这些文档描述的是"原版有什么"，不是"C# 实现了什么"。

---

## 归档文档（历史参考）

### 2026-08-14 归档

以下文档已过期，仅供历史参考：

| 文档 | 原声称 | 问题 |
|------|---------|------|
| `completeness_refresh_20260814.md` | 94.16% / 94.43% | 基于 8 月 14 日，当前工作树有 356 个文件改动 |
| `completeness_audit_20260814.md` | 99.1% / 97.0% | 同一天给出不同数字，计数规则不明 |
| `completeness_audit_v2_20260814.md` | 91.9% | 同上 |
| `yanshen_completeness_audit_20260814.md` | 89.4% / 86.6% | 眼神插件专项，已被机械审计覆盖 |

**为什么过期**:
1. 当前工作树有 356 个未提交改动（260 修改 + 1 删除 + 95 未跟踪）
2. 同一天不同文档给出 6 个不同百分比，没有统一分母和计数规则
3. 文档数字是手工维护，容易与代码脱节
4. 新的机械基线（445 审计工具）提供可复现验证

**如何使用归档文档**:
- ✅ 可以参考其中的逆向分析、技术细节、实现笔记
- ❌ 不要引用其中的百分比数字
- ❌ 不要基于它们做上线决策

---

## 文档维护规则

### 1. 百分比使用规则

**禁止**: 在任何文档中手写完成度百分比

**允许**: 
- 引用 `CURRENT_STATUS_YYYYMMDD.md` 中的数字
- 引用 `_audit_report_YYYYMMDD.json` 中的统计

**示例**:

❌ 错误:
```markdown
GameSvr 当前完成度约 95%
```

✅ 正确:
```markdown
GameSvr 构建 0 错误，审计 PASS 率见 [CURRENT_STATUS_20260818.md](docs/CURRENT_STATUS_20260818.md)
```

### 2. 更新频率

| 文档类型 | 更新时机 | 归档时机 |
|----------|----------|----------|
| `CURRENT_STATUS_YYYYMMDD.md` | 每次运行 445 审计后 | 14 天后 |
| 子系统文档 | 重大功能变更后 | 不归档（持续更新）|
| 逆向发现文档 | 发现新行为后 | 不归档（持久有效）|
| Git 拓扑审计 | 一次性（统一完成后） | 完成后归档 |

### 3. 文档命名规则

- 现状文档: `CURRENT_STATUS_YYYYMMDD.md`
- 子系统文档: `subsystems/{SubSystem}/README.md`
- 逆向发现: `discovery_{feature}_YYYYMMDD.md`
- 一次性分析: `{topic}_ANALYSIS_YYYYMMDD.md`

### 4. 归档流程

```bash
# 当 CURRENT_STATUS 超过 14 天
mkdir -p docs/archives/2026-08-14
mv docs/CURRENT_STATUS_20260814.md docs/archives/2026-08-14/
echo "本文档已归档，请参考 docs/CURRENT_STATUS_20260818.md" > docs/archives/2026-08-14/README.md
```

---

## 快速导航

### 我想知道...

**当前可以上线吗？**
→ [CURRENT_STATUS_20260818.md § 7 风险评估](CURRENT_STATUS_20260818.md#7-风险评估)

**哪些子系统可以独立上线？**
→ [CURRENT_STATUS_20260818.md § 6.2 高质量子系统](CURRENT_STATUS_20260818.md#62-高质量子系统可优先上线)

**还有多少功能缺失？**
→ [CURRENT_STATUS_20260818.md § 3.3 真行为分歧](CURRENT_STATUS_20260818.md#33-a-真行为分歧22-项)

**如何统一 Git 分支？**
→ [GIT_TOPOLOGY_AUDIT.md § 5 统一基线方案](GIT_TOPOLOGY_AUDIT.md#5-统一基线方案)

**FieldHero 什么时候能用？**
→ [CURRENT_STATUS_20260818.md § 6.1 最大缺口](CURRENT_STATUS_20260818.md#61-最大缺口按影响范围排序)

**元宝商城为什么不能用？**
→ [CURRENT_STATUS_20260818.md § 5.5 SystemModule](CURRENT_STATUS_20260818.md#55-systemmodule)

**构建失败了怎么办？**
→ [CURRENT_STATUS_20260818.md § 2 构建验证](CURRENT_STATUS_20260818.md#2-构建验证)

**哪些审计工具失败了？**
→ [CURRENT_STATUS_20260818.md § 3.3-3.6](CURRENT_STATUS_20260818.md#33-a-真行为分歧22-项)

---

## 贡献指南

### 提交代码

1. **分支命名**: 遵循 [GIT_TOPOLOGY_AUDIT.md § 6.1](GIT_TOPOLOGY_AUDIT.md#61-命名规则)
2. **提交前检查**:
   ```bash
   # 构建
   dotnet build GameSvr/GameSvr.csproj
   
   # 相关审计（不是全部 445 个）
   python AuditTools/run_audits.py --filter <相关工具>
   ```
3. **提交信息**: 使用常规提交格式
   ```
   feat(GameSvr): 简短描述
   fix(audit): 修复审计工具
   docs: 更新文档
   ```

### 更新文档

1. **不要手写百分比**: 引用 `CURRENT_STATUS_YYYYMMDD.md`
2. **技术细节可以写**: 实现笔记、逆向分析、架构设计
3. **运行审计后更新现状**: 
   ```bash
   python AuditTools/run_audits.py --jobs 1 --report _audit_report_$(date +%Y%m%d).json
   # 然后更新 CURRENT_STATUS_YYYYMMDD.md
   ```

### 报告问题

**标题格式**: `[子系统] 简短描述`

**必须包含**:
- 复现步骤
- 预期行为 vs 实际行为
- 相关审计工具名称（如果有）
- 原版行为证据（截图/日志/逆向分析）

---

## 联系方式

- **项目仓库**: （待补充）
- **文档维护**: （待补充）
- **技术问题**: （待补充）

---

**最后更新**: 2026-08-18  
**下次更新**: 运行 445 审计后或重大功能变更后
