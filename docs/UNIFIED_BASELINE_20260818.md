# Unified Git Baseline

Snapshot time: 2026-08-18 23:46 +08:00

## Authoritative Line

- Branch: `unified/main`
- History-anchor commit: `18635d2b39dd4249a208abb2e2cb94d1fa18d096`
- First parent: `temp-merge-branch @ 299c20390250075217d7e257738c60c249317191`
- Second parent: `master @ e7a2429b1b62abdd665cdbf2b205be79b8888cd4`
- Source-integration tip before this document update: `21254b9c3a76bc619f98b8a332bc1626172a39d0`
- Source-integration tree before this document update: `ad6d6bedfff6c71c04b6061e36eb35f071fbe67b`

The history-anchor merge anchors both histories while retaining the temp committed tree
exactly. Subsequent commits integrate the reviewed source snapshot in project-sized
batches; generated output and temporary audit runners were intentionally left out.

## Preservation

- Recovery snapshot: `D:\loym2\workspace-archive\git-rescue\LyoMir2_rescue_20260818_230609`
- Reachable-ref bundle: `refs.bundle`
- Bundle SHA-256: `4496383DE8EACD14202801E1BF7D4E11A3295BC1CF7521C7808FC592FA11B590`
- Master protection tag: `rescue/pre-unification-master-20260818`
- Temp protection tag: `rescue/pre-unification-temp-20260818`

The recovery snapshot contains the shared Git metadata, the old root `.git.broken`
metadata, per-worktree identity/status files, binary tracked diffs for dirty worktrees,
and copies of all non-ignored untracked files found during the snapshot pass.

## Audit Evidence

- Result file: `_audit_report_20260818_full.json`
- Result SHA-256: `C7CE1A6B3517EDDF52353070E5C2CC6B85523E078EECCC04C5470A2D22B21A55`
- Totals: `PASS=415`, `FAIL=27`, `INCOMPLETE=3`
- Failure classification: `_audit_failures_20260818.md`
- Failure file SHA-256: `5CDC05C8D07E9CA37219E7846CB3AB8B53D12AA76BB5B44514BDC1B184BA34F6`

These are audit-tool outcomes, not a project completion percentage and not a release gate.

## Integration Status

- The tracked source snapshot from the temp carrier was integrated in commits
  `119abe2e`, `b5b49094`, `51ed8d79`, `cce83b4d`, `2161dfe9`, and `8b05e6c6`.
- Five server projects were rebuilt after integration with zero compiler errors.
- New audit projects and the three recovered GuildBase seed files were committed;
  generated binaries, `_toolruns_root`, and temporary probe files were not committed.
- `GameSvr/Plugins/YanshenProvableRegistry.cs` was deliberately retained because the
  carrier's deletion was not independently reviewed.
- The original temp carrier remains dirty and continues to be preserved separately;
  it is not the authoritative integration line.

## Safety Rules

Until full-stack acceptance is complete:

- Do not delete branches, worktrees, stashes, tags, or recovery files.
- Do not run `git gc`, `git prune`, `git clean`, or destructive reset operations.
- Do not infer that a branch is disposable merely because it is an ancestor of the
  history-anchor merge.
- Do not treat build success or audit pass rate as production readiness.

## Verification

Run these read-only checks from the repository:

```powershell
git rev-parse unified/main
git rev-parse 'unified/main^{tree}'
git merge-base --is-ancestor master unified/main
git merge-base --is-ancestor temp-merge-branch unified/main
git bundle verify D:\loym2\workspace-archive\git-rescue\LyoMir2_rescue_20260818_230609\refs.bundle
```

Both ancestor checks must return exit code `0`. The current tree is obtained from
`git rev-parse 'unified/main^{tree}'`; do not compare it to the historical anchor tree
after source-integration commits.
