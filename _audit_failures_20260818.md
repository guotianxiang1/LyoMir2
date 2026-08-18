# 审计失败分类（2026-08-18 全量 445 工具，当前工作树）

基线：`temp-merge-branch` @ `299c2039` + 356 个未提交改动。运行方式：`--jobs 1`（串行）。

**总计：PASS 415 / FAIL 27 / INCOMPLETE 3。编译失败 0。**

## 重要更正

退出码 `3762504530` (`0xE0434352`) 只表示 .NET 未捕获异常，**不能据此判定为环境问题**。
逐条读异常内容后发现：这些工具大多在断言失败时直接抛 `InvalidOperationException` 而非干净 `exit(1)`，
因此它们是**真行为分歧**。按退出码分类会严重低估缺口数。


## A. 真行为分歧（最高优先级） — 22 项

| 工具 | 断言/原因 |
|---|---|
| `DeathDropPolicyCheck` |  |
| `DiamondCacheCompatCheck` | Unhandled exception. System.InvalidOperationException: successful TakeDiamond returned False |
| `DynRoomMasterRelocationCheck` | Unhandled exception. System.InvalidOperationException: message order: expected 10117,10118,10336, actual 10336 |
| `ExactEnvironmentMonsterSpawnTransactionCheck` | [2026-08-18 20:51:10.331] [PasEngine] Monster script initialize failed TransactionOma: expected OnInitialize failure |
| `GateLegacyType18CompatCheck` | Unhandled exception. System.InvalidOperationException: one-shot final buffer: expected 0, got 8 |
| `HeroUnionStateCheck` | Unhandled exception. System.InvalidOperationException: warrior-assassin native union effect order |
| `LingFuCompatCheck` | Unhandled exception. System.InvalidOperationException: CreditCard missing native surface: ISM_SERVERSWITCH |
| `NativeAttackModeCompatCheck` | Unhandled exception. System.InvalidOperationException: 545 ingress message was not queued |
| `NativeCastleSiegeCheck` |  |
| `NativeClientBodyLengthGateCheck` | Unhandled exception. System.InvalidOperationException: ident 3030 with a 1-byte body was dropped |
| `NativeCommonInformationCheck` | Unhandled exception. System.InvalidOperationException: 1099 ingress was not queued |
| `NativeGildExitViceWiringCompatCheck` | NativeGildExitViceWiringCompatCheck FAIL: System.InvalidOperationException: 4587 must emit exactly one packet: expected=1, actual=2 |
| `NativeGildWiringCompatCheck` | NativeGildWiringCompatCheck FAIL: System.InvalidOperationException: 4569 must emit exactly one packet: expected=1, actual=2 |
| `NativeMotaeboForcedMoveCompatCheck` | Unhandled exception. System.InvalidOperationException: level 0 total steps: expected=3, actual=2 |
| `NativeType43EarthFireCompatCheck` | Unhandled exception. System.InvalidOperationException: FireBurn 3001ms periodic HP: expected=425, actual=500 |
| `PasDispatchShadowCompatCheck` | Unhandled exception. System.InvalidOperationException: ClearMon ignored GetPoseCreate exclusion: expected 105, actual 0 |
| `SecHeroPracticeCompatCheck` | Unhandled exception. System.InvalidOperationException: original M2 SHA256: expected=CC505716AEB2FDB09C96B805D06C1DDDCD70DB0F331EF42AE1338C71766B452F a |
| `YanshenConfigRuntimeCheck` | YanshenConfigRuntimeCheck FAIL: System.InvalidOperationException: expected 379 native keys, got 380 |
| `YanshenMsgTransportCheck` | FAIL terminal NUL is retained in Payload and removed from text only: System.InvalidOperationException: queued message count: expected 1, got 0 |
| `YbDbOpenYbProtocolCompatCheck` | Unhandled exception. System.InvalidOperationException: PAS fail-closed boundary: ClientAskOpenYB is not fail-closed |
| `YbDbPasSubstituteFailClosedCheck` | Unhandled exception. System.InvalidOperationException: clientaskopenyb is not fail-closed |
| `YbGoldGiftCompatCheck` | Unhandled exception. System.InvalidOperationException: GoldID dispatch must remain fail closed: expected 2, actual 1 |

## B. 休眠边界断言（代码有，未接生产） — 2 项

| 工具 | 断言/原因 |
|---|---|
| `FirstUsedGiftCompatCheck` | Unhandled exception. System.InvalidOperationException: PAS and runtime dormant: YbDbClient consumes dormant 1112 codec |
| `YbDbClientCompatCheck` | Unhandled exception. System.InvalidOperationException: native logout 104 dormant boundary: final player save does not use exact mode-3 persistence: ex |

## C. 环境/fixture 缺失（非代码缺陷） — 3 项

| 工具 | 断言/原因 |
|---|---|
| `DiamondTransactionCompatCheck` | Unhandled exception. System.InvalidOperationException: StdMode=7 TakeDiamond returned False |
| `NativeMailWireCodecCheck` | Unhandled exception. System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation. |
| `NeedKeyBoxShadowProtocolCheck` | Unhandled exception. System.IO.FileNotFoundException: NeedKeyBox native evidence transcript is missing |

## D. 自报跳过 — 3 项

| 工具 | 断言/原因 |
|---|---|
| `DbGateRegressionCheck` | self-reported skip |
| `NativeHonorDbCheck` | self-reported skip |
| `NativeSelfCorpsGildExactStateCheck` | self-reported skip |
