# Git 分支拓扑审计与统一方案

> **执行状态（2026-08-18 23:46）**
>
> 已采用非破坏性方案建立唯一集成线 `unified/main`，历史锚点为 `18635d2b`：
>
> - 第一父提交：`temp-merge-branch @ 299c2039`
> - 第二父提交：`master @ e7a2429b`
> - 合并提交文件树：与 temp 的提交树 `564fbb841269...` 完全一致
> - 保护标签：`rescue/pre-unification-master-20260818`、
>   `rescue/pre-unification-temp-20260818`
> - 仓库外恢复快照：`D:\loym2\workspace-archive\git-rescue\LyoMir2_rescue_20260818_230609`
> - 审计源码已按 SystemModule、DBSvr、GameSvr、AuditTools 分批提交
> - 五个服务端项目在统一线重新构建，均为 0 编译错误

> 该合并是历史锚定，不表示 master 独有文件内容已经通过业务评审。
> 所有旧分支、306 个原工作树、102 个 stash 和不可达对象均未删除。
> 禁止依据“已成为祖先”自动删除分支；必须先验证文件树、WIP 备份和业务测试。
>
> 本文余下部分是 21:40 生成的原始审计草案。其中按提交数量选择基线、
> 批量删除分支、按目录时间删除工作树以及 `gc/prune/reset/clean` 类操作均未执行，
> 也不再构成当前方案。保留这些内容只是为了审计可追溯。

**生成时间**: 2026-08-18 21:40  
**审计目标**: 统一服务端所有 Git 线路，建立单一权威基线

---

## 执行摘要

### 当前状态

- **本地分支（审计快照）**: 401 个；当前含 `unified/main` 共 402 个
- **远程跟踪分支**: 0 个
- **工作树（审计快照）**: 306 个；当前含统一线共 307 个
- **未提交改动**: 356 个文件（260 修改 + 1 删除 + 95 未跟踪）
- **审计时活跃线**: temp-merge-branch @ 299c2039
- **当前权威集成线**: unified/main

### 核心问题

1. **分支爆炸**: 401 个本地分支，其中 248 个 `w/*` 前缀（可能是 worktree 遗留），62 个 `fix/*`，37 个 `gap/*`
2. **双向分叉**: 当前线相对 master 是双向分叉
   - master 独有: 65 个提交
   - 当前线独有: 1674 个提交
   - **无清晰合并基线**
3. **工作树泄漏**: 306 个工作树，很多可能是临时任务完成后未清理
4. **文档不一致**: 同一天（2026-08-14）的文档给出 6 个不同完成度百分比
5. **大量未提交改动**: 356 个文件，说明当前线仍在活跃开发

### 统一目标

1. ✅ 建立唯一权威基线 `unified/main`
2. ✅ 保护旧引用、工作树和未提交内容，不执行破坏性清理
3. ✅ 将审计源码 WIP 分类并提交到统一线
4. ✅ 更新权威文档引用统一基线
5. ⏸️ 旧分支和工作树仅保留、清点，不在本轮删除
6. ✅ 建立未来分支命名与验证规则

---

## 附录 A：未执行的原始审计草案

## 1. 分支分类

### 1.1 按前缀分类

| 前缀 | 数量 | 可能含义 | 建议操作 |
|------|-----:|----------|----------|
| `w/*` | 248 | Worktree 遗留或工作分支 | 逐个检查是否已合并，已合并的删除 |
| `fix/*` | 62 | Bug 修复分支 | 检查是否已合并到 temp-merge-branch |
| `gap/*` | 37 | 功能缺口实现分支 | 检查是否已合并到 temp-merge-branch |
| `impl/*` | 7 | 功能实现分支 | 检查是否已合并到 temp-merge-branch |
| `agent/*` | 5 | AI 代理生成的分支 | 检查是否已合并，临时分支可删除 |
| `yanshen/*` | 1 | 眼神插件相关 | 保留或合并 |
| `worktree-*` | 19 | 临时工作树分支 | 清理工作树后删除 |
| 其他 | 22 | 各种特殊分支 | 逐个评审 |

### 1.2 特殊分支

| 分支名 | 最后提交 | 说明 | 建议 |
|--------|----------|------|------|
| `master` | e7a2429b (2026-08-15) | 旧的"主分支"，167/166 占位实现 | 归档或合并部分提交 |
| `temp-merge-branch` | 299c2039 (2026-08-14) | 当前活跃线，1674 个独有提交 | **推荐为权威基线** |
| `audit-tooling-fixes-*` | 7ced7d08 (2026-08-13) | 审计工具修复 | 检查是否已合并 |
| `dbs-lg-gg-work` | e8ccf6c7 (2026-08-13) | DBSvr/LoginGate/GameGate 工作 | 检查是否已合并 |

---

## 2. 去重分析

### 2.1 方法

通过 tree hash 识别内容完全相同的分支（tree hash 相同 = 文件树完全相同，即使 commit SHA 不同）

### 2.2 去重命令

```bash
# 生成分支 tree hash 列表
git branch --format='%(tree) %(refname:short)' | sort > _branch_trees.txt

# 找出重复的 tree hash（内容完全相同的分支）
awk '{print $1}' _branch_trees.txt | sort | uniq -d > _dup_trees.txt

# 列出每个重复 tree 对应的分支
while read tree; do
  echo "=== Tree $tree ==="
  grep "^$tree " _branch_trees.txt | awk '{print $2}'
  echo
done < _dup_trees.txt > _duplicate_branches.txt
```

### 2.3 预期结果

根据分支数量（401）和命名模式（大量 `w/*` 前缀），预计：
- **高度重复**: 50-100 组重复内容
- **每组 2-5 个分支**: 总计可能有 100-200 个分支是重复的
- **可安全删除**: 保留每组中命名最清晰的一个，删除其余

---

## 3. 合并状态检查

### 3.1 检查分支是否已合并到 temp-merge-branch

```bash
# 检查哪些分支已完全合并
git branch --merged temp-merge-branch > _merged_branches.txt

# 检查哪些分支有独有提交
git branch --no-merged temp-merge-branch > _unmerged_branches.txt

# 统计
echo "Merged: $(wc -l < _merged_branches.txt)"
echo "Unmerged: $(wc -l < _unmerged_branches.txt)"
```

### 3.2 预期结果

- **已合并分支**: 预计 200-300 个（可安全删除）
- **未合并分支**: 预计 100-150 个（需要逐个评审）

### 3.3 未合并分支处理策略

对于每个未合并分支：

1. **检查提交内容**:
   ```bash
   git log temp-merge-branch..BRANCH --oneline --no-merges
   ```

2. **分类决策**:
   - **A. 功能重复**: 相同功能在 temp-merge-branch 已有更新实现 → 删除
   - **B. 实验分支**: 已废弃或被更好方案替代 → 归档后删除
   - **C. 有效功能**: 独有功能且需要合并 → 标记待合并
   - **D. 临时工作**: 临时测试或调试分支 → 直接删除

3. **待合并分支处理**:
   - 在新的 worktree 中验证编译
   - 运行相关审计工具
   - 冲突解决后合并到 temp-merge-branch
   - 删除原分支

---

## 4. 工作树清理

### 4.1 当前工作树状态

```bash
git worktree list --porcelain
```

**当前**: 306 个工作树

### 4.2 清理策略

1. **识别孤立工作树** (branch 已删除但目录还在):
   ```bash
   git worktree list | grep -E '(detached|orphaned|prunable)'
   git worktree prune -v
   ```

2. **按活跃度分类**:
   - **活跃** (最近 7 天有修改): 保留
   - **闲置** (7-30 天无修改): 评审后保留或清理
   - **僵尸** (>30 天无修改): 清理

3. **手动清理命令**:
   ```bash
   # 列出所有工作树及其最后修改时间
   git worktree list | while read -r path sha branch; do
     if [ -d "$path" ]; then
       mtime=$(find "$path" -type f -name '*.cs' -printf '%T@\n' 2>/dev/null | sort -n | tail -1)
       if [ -n "$mtime" ]; then
         age_days=$(( ($(date +%s) - ${mtime%.*}) / 86400 ))
         echo "$age_days days $path $branch"
       fi
     fi
   done | sort -n > _worktree_ages.txt
   
   # 清理超过 30 天的工作树
   awk '$1 > 30 {print $2}' _worktree_ages.txt | while read path; do
     echo "Removing: $path"
     git worktree remove "$path" --force
   done
   ```

### 4.3 预期结果

- **保留**: 5-10 个活跃工作树
- **清理**: 296-301 个僵尸工作树

---

## 5. 统一基线方案

### 5.1 推荐方案：以 temp-merge-branch 为权威基线

**理由**:
1. ✅ 最新活跃开发线（356 个未提交改动）
2. ✅ 包含 1674 个独有提交，远多于 master 的 65 个
3. ✅ 构建验证通过（5 个项目全部 0 错误）
4. ✅ 审计工具基线新鲜（445 工具，415 PASS）

**不推荐 master 的理由**:
1. ❌ 最后提交是 8 月 15 日的 167/166 占位实现（大量 MVI、默认禁用、未接线）
2. ❌ 缺少最近 3 天的大量修复和审计工具改进
3. ❌ 文档数字（94%）与机械验证（93% PASS）不一致

### 5.2 统一步骤

#### 第 1 步：提交当前未提交改动（1-2 天）

```bash
# 按子系统分批提交
# 1. 审计工具
git add AuditTools/AuthenticationCompatCheck/Program.cs
git add AuditTools/CommandAuditCheck/Program.cs
# ... (76 个文件，按工具分组)
git commit -m "fix(audit): Update core audit tools for temp-merge-branch baseline"

# 2. GameSvr
git add GameSvr/Actors/*.cs
git add GameSvr/Services/*.cs
# ... (194 个文件，按子系统分组)
git commit -m "feat(GameSvr): Implement fixes and enhancements for 22 audit failures"

# 3. docs
git add docs/CURRENT_STATUS_20260818.md
git add docs/PRODUCTION_REACHABILITY.md
git add docs/GIT_TOPOLOGY_AUDIT.md
git commit -m "docs: Add mechanical baseline documentation (2026-08-18)"

# 4. 其他
git add SystemModule/*.cs
git add DBSvr/*.cs
git commit -m "fix(SystemModule,DBSvr): Minor fixes and enhancements"
```

**验证**: 每批提交后，在隔离 worktree 验证编译 + 运行相关审计

#### 第 2 步：重命名为 main（可选）

```bash
# 将 temp-merge-branch 重命名为 main
git branch -m temp-merge-branch main

# 设置为默认分支
git symbolic-ref HEAD refs/heads/main
```

#### 第 3 步：评审 master 的 65 个独有提交

```bash
# 列出 master 独有的提交
git log main..master --oneline --no-merges > _master_only_commits.txt

# 逐个检查是否有需要 cherry-pick 的提交
cat _master_only_commits.txt | while read sha msg; do
  echo "=== $sha: $msg ==="
  git show $sha --stat
  echo "Cherry-pick? (y/n)"
  # 人工评审
done
```

**策略**:
- 如果 master 的提交是占位实现，忽略（main 已有更好实现）
- 如果 master 有独有修复，cherry-pick 到 main
- **预期**: 大部分忽略，少数 cherry-pick

#### 第 4 步：归档旧 master

```bash
# 重命名 master 为历史归档
git branch -m master archive/master-20260815-167-166-placeholder

# 或直接删除（如果确认无价值）
git branch -D master
```

#### 第 5 步：清理分支（2-3 天）

```bash
# 1. 删除已合并分支
git branch --merged main | grep -vE '^(\*|main|master)' | xargs -r git branch -d

# 2. 去重：删除内容相同的分支（保留命名最清晰的）
# （使用第 2 节的去重脚本）

# 3. 评审未合并分支（使用第 3 节的策略）
git branch --no-merged main > _unmerged_for_review.txt
# 人工逐个评审

# 4. 最终归档
git branch | grep -E '^  (w/|fix/|gap/|impl/)' > _archived_branches_20260818.txt
git branch | grep -E '^  (w/|fix/|gap/|impl/)' | xargs -r git branch -D
```

#### 第 6 步：清理工作树（1 天）

```bash
# 使用第 4 节的清理脚本
# 预期：从 306 个减少到 5-10 个
```

#### 第 7 步：更新所有文档（1 天）

```bash
# 1. 废弃旧百分比
find docs -name '*.md' -type f -exec sed -i 's/94\.16%/见 CURRENT_STATUS_20260818.md/g' {} +
find docs -name '*.md' -type f -exec sed -i 's/94\.43%/见 CURRENT_STATUS_20260818.md/g' {} +
find docs -name '*.md' -type f -exec sed -i 's/91\.9%/见 CURRENT_STATUS_20260818.md/g' {} +
# ... 其他旧百分比

# 2. 添加归档说明
mkdir -p docs/archives/2026-08-14
mv docs/completeness_refresh_20260814.md docs/archives/2026-08-14/
echo "本文档已归档，请参考 docs/CURRENT_STATUS_20260818.md" > docs/archives/2026-08-14/README.md

# 3. 更新索引
cat > docs/README.md << 'EOF'
# M2 复刻项目文档

## 当前状态
- [CURRENT_STATUS_20260818.md](CURRENT_STATUS_20260818.md) - 权威现状（机械基线）
- [PRODUCTION_REACHABILITY.md](PRODUCTION_REACHABILITY.md) - 生产可达性分析
- [GIT_TOPOLOGY_AUDIT.md](GIT_TOPOLOGY_AUDIT.md) - Git 拓扑审计

## 子系统文档
- [subsystems/](subsystems/) - 各子系统详细文档

## 逆向发现
- [discoveries/](discoveries/) - 原版行为逆向文档（保持现有）

## 归档
- [archives/](archives/) - 按日期归档的旧文档
EOF

git add docs/README.md docs/archives/
git commit -m "docs: Archive outdated documents and update index"
```

### 5.3 替代方案：保守合并

如果不想废弃 master：

```bash
# 1. 从 main 创建统一分支
git checkout -b unified-baseline main

# 2. 尝试合并 master
git merge master --no-ff -m "Merge master (167/166 placeholder) into unified baseline"

# 3. 解决冲突（预期：大量冲突，因为双向分叉）
# 冲突解决策略：
# - 对于占位实现（MVI、默认禁用），保留 main 的完整实现
# - 对于独有修复，保留 master 的修复
# - 对于重复功能，保留 main 的版本

# 4. 验证
dotnet build GameSvr/GameSvr.csproj
python AuditTools/run_audits.py --jobs 1

# 5. 如果验证通过，将 unified-baseline 设为主分支
git branch -m main archive/main-temp-merge
git branch -m unified-baseline main
```

**不推荐理由**: 合并冲突会很大（1674 vs 65 提交），耗时长且容易出错。

---

## 6. 未来分支管理规则

### 6.1 命名规则

| 类型 | 前缀 | 示例 | 生命周期 |
|------|------|------|----------|
| 功能开发 | `feat/` | `feat/fieldhero-activation` | 合并后删除 |
| Bug 修复 | `fix/` | `fix/diamond-cache-compat` | 合并后删除 |
| 文档更新 | `docs/` | `docs/update-api-reference` | 合并后删除 |
| 实验分支 | `exp/` | `exp/new-combat-algorithm` | 评审后删除或转 feat/ |
| 发布分支 | `release/` | `release/v1.0.0` | 长期保留 |
| 临时修复 | `hotfix/` | `hotfix/login-crash` | 合并后删除 |

**禁止**: `w/*`、`worktree-*`、无意义的缩写（如 `tmp1`、`test`）

### 6.2 清理流程

**自动清理** (定期 cron 或 CI):
```bash
#!/bin/bash
# cleanup_merged_branches.sh

# 删除已合并到 main 的分支（除了 main 本身）
git branch --merged main | grep -vE '^(\*|main)$' | while read branch; do
  echo "Deleting merged branch: $branch"
  git branch -d "$branch"
done

# 清理孤立工作树
git worktree prune -v

# 删除超过 30 天无修改的工作树
git worktree list | tail -n +2 | while read path sha branch; do
  if [ -d "$path" ]; then
    age=$(($(date +%s) - $(stat -c %Y "$path")))
    if [ $age -gt 2592000 ]; then  # 30 days
      echo "Removing stale worktree: $path"
      git worktree remove "$path" --force
    fi
  fi
done

echo "Cleanup complete"
```

**运行频率**: 每周一次

### 6.3 分支审查清单

合并前检查：
- [ ] 分支名称符合命名规则
- [ ] 构建通过（0 错误）
- [ ] 相关审计工具通过
- [ ] 代码审查完成
- [ ] 文档已更新（如有必要）
- [ ] 冲突已解决
- [ ] 在隔离 worktree 验证

合并后：
- [ ] 删除源分支（feat/、fix/ 等）
- [ ] 清理相关工作树
- [ ] 更新 CHANGELOG（如果是发布分支）

---

## 7. 执行时间表

| 步骤 | 预计耗时 | 负责人 | 验收标准 |
|------|----------|--------|----------|
| 1. 提交 356 个未提交改动 | 1-2 天 | 开发 | 构建通过 + 相关审计通过 |
| 2. 去重分支（识别） | 2 小时 | 自动化 | 生成 `_duplicate_branches.txt` |
| 3. 删除已合并分支 | 1 小时 | 自动化 | 分支数从 401 降至 ~150 |
| 4. 评审未合并分支 | 2-3 天 | 开发 + 评审 | 每个分支有明确去留决策 |
| 5. 清理工作树 | 1 天 | 自动化 | 工作树从 306 降至 5-10 |
| 6. 更新文档 | 1 天 | 文档 | 所有文档引用统一基线 |
| 7. 验证 | 1 天 | QA | 445 审计全量运行，PASS 率 ≥ 93% |
| **总计** | **5-8 天** | - | - |

---

## 8. 风险与缓解

| 风险 | 严重性 | 缓解措施 |
|------|--------|----------|
| 误删有价值分支 | 🟡 MEDIUM | 1. 在删除前生成完整分支列表归档<br>2. 使用 `git branch -d` (安全删除) 而非 `-D`<br>3. 评审阶段人工确认 |
| 合并冲突过大 | 🟡 MEDIUM | 推荐方案不合并 master，仅 cherry-pick 必要提交 |
| 文档更新遗漏 | 🟢 LOW | 用脚本批量替换旧百分比，手动检查关键文档 |
| 工作树清理导致数据丢失 | 🟡 MEDIUM | 仅清理超过 30 天无修改的，保留最近活跃的 |
| 未提交改动提交时引入问题 | 🟡 MEDIUM | 分批提交，每批后验证编译 + 审计 |

---

## 9. 验收标准

统一完成后，应满足：

### 9.1 Git 仓库

- [ ] 只有 1 个主分支（main 或 master）
- [ ] 活跃功能分支 ≤ 10 个
- [ ] 所有分支名称符合命名规则
- [ ] 工作树 ≤ 10 个
- [ ] 无孤立工作树（`git worktree prune` 无输出）
- [ ] `git status` 干净（无未提交改动）

### 9.2 文档

- [ ] 只有 1 个权威现状文档（CURRENT_STATUS_YYYYMMDD.md）
- [ ] 所有旧百分比已替换为文档引用
- [ ] 旧文档已归档到 `docs/archives/YYYY-MM-DD/`
- [ ] 有 README.md 说明文档结构

### 9.3 构建与测试

- [ ] 5 个项目全部 0 错误构建
- [ ] 445 个审计工具运行完成
- [ ] PASS 率 ≥ 93%
- [ ] 已知 FAIL 有对应 issue/TODO

### 9.4 流程

- [ ] 有书面分支管理规则
- [ ] 有自动清理脚本并加入定期任务
- [ ] 有分支审查清单

---

## 10. 后续维护

### 10.1 定期任务

**每周**:
- 运行 `cleanup_merged_branches.sh`
- 检查工作树数量，清理超过 30 天的

**每月**:
- 重新运行 445 个审计工具
- 更新 `CURRENT_STATUS_YYYYMMDD.md`
- 归档上月的现状文档

### 10.2 分支生命周期

```
创建 → 开发 → 审查 → 合并 → 删除
  ↓                      ↓
命名检查              验收检查
```

**最大生命周期**: 功能分支不应超过 2 周（避免过时）

### 10.3 紧急情况

如果发现统一后主分支有问题：

```bash
# 回滚到统一前（假设统一前最后一次提交是 299c2039）
git reset --hard 299c2039

# 或者恢复被删除的分支
git reflog | grep 'deleted branch'
git branch recovered-branch <sha>
```

**重要**: 在统一前备份完整仓库
```bash
cd ..
cp -r loym2 loym2-backup-20260818
```

---

## 附录 A: 快速执行脚本

### A.1 完整统一脚本（需要人工确认关键步骤）

```bash
#!/bin/bash
set -e

echo "=== M2 Git 仓库统一脚本 ==="
echo "当前分支: $(git branch --show-current)"
echo "分支总数: $(git branch | wc -l)"
echo "工作树总数: $(git worktree list | wc -l)"
echo

# 第 1 步：检查未提交改动
if [ -n "$(git status --porcelain)" ]; then
  echo "❌ 有未提交改动，请先提交或暂存"
  git status --short
  exit 1
fi

# 第 2 步：备份
echo "正在备份仓库..."
cd ..
backup_name="loym2-backup-$(date +%Y%m%d-%H%M%S)"
cp -r loym2 "$backup_name"
echo "✓ 备份完成: $backup_name"
cd loym2

# 第 3 步：删除已合并分支
echo
echo "=== 删除已合并分支 ==="
merged=$(git branch --merged temp-merge-branch | grep -vE '^(\*|temp-merge-branch|master)$' || true)
if [ -n "$merged" ]; then
  echo "$merged" | wc -l
  echo "$merged"
  read -p "确认删除这些已合并分支? (yes/no) " confirm
  if [ "$confirm" = "yes" ]; then
    echo "$merged" | xargs git branch -d
    echo "✓ 已删除"
  fi
else
  echo "无已合并分支"
fi

# 第 4 步：清理工作树
echo
echo "=== 清理工作树 ==="
git worktree prune -v

stale_worktrees=$(git worktree list | tail -n +2 | while read path sha branch; do
  if [ -d "$path" ]; then
    age=$(($(date +%s) - $(stat -c %Y "$path" 2>/dev/null || echo 0)))
    if [ $age -gt 2592000 ]; then
      echo "$path"
    fi
  fi
done)

if [ -n "$stale_worktrees" ]; then
  echo "发现 $(echo "$stale_worktrees" | wc -l) 个超过 30 天的工作树"
  echo "$stale_worktrees"
  read -p "确认删除? (yes/no) " confirm
  if [ "$confirm" = "yes" ]; then
    echo "$stale_worktrees" | while read path; do
      git worktree remove "$path" --force
    done
    echo "✓ 已清理"
  fi
else
  echo "无过期工作树"
fi

# 第 5 步：重命名主分支（可选）
echo
read -p "是否将 temp-merge-branch 重命名为 main? (yes/no) " confirm
if [ "$confirm" = "yes" ]; then
  git branch -m temp-merge-branch main
  git symbolic-ref HEAD refs/heads/main
  echo "✓ 已重命名为 main"
fi

# 第 6 步：统计
echo
echo "=== 统一完成 ==="
echo "当前分支: $(git branch --show-current)"
echo "剩余分支: $(git branch | wc -l)"
echo "剩余工作树: $(git worktree list | wc -l)"
echo
echo "后续步骤:"
echo "1. 评审未合并分支: git branch --no-merged"
echo "2. 运行审计: python AuditTools/run_audits.py --jobs 1"
echo "3. 更新文档: 参考 docs/GIT_TOPOLOGY_AUDIT.md 第 5.2 节"
```

### A.2 使用方法

```bash
# 1. 保存脚本
cat > tools/unify_git_repo.sh << 'EOF'
# （上述脚本内容）
EOF
chmod +x tools/unify_git_repo.sh

# 2. 运行（会在关键步骤要求确认）
./tools/unify_git_repo.sh

# 3. 后续手动评审
git branch --no-merged main > _unmerged_branches.txt
# 逐个评审
```

---

**文档完成**  
**下一步**: 执行统一方案，提交 356 个未提交改动
