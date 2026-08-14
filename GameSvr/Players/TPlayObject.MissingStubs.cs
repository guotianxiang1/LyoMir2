using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 临时占位符：来自分支但未完全合并的方法和字段。
    /// 这些需要从正确的 commit 中提取完整实现。
    /// </summary>
    public partial class TPlayObject
    {
        // ====== 字段占位符 ======
        public bool m_boNativeHeroCapHintEnabled;
        public bool m_boNativeHeroRecordShared;
        public int WhisperMonitorFColor;
        public int WhisperMonitorBColor;
        public string WhisperMonitorPrefix = string.Empty;

        // ====== 方法占位符 ======

        private bool IsNativeBlinkPickupLocked(int x, int y, int currentTick)
        {
            // TODO: 从 MOVE-71/72/73/74 系列 commit 提取
            return false;
        }

        private void ClearLegacyStatusSlots()
        {
            // TODO: 从相关 commit 提取
        }

        private void SendNativeLogonStateSync()
        {
            // TODO: 从相关 commit 提取
        }

        private void ProcessNativeSkill68ChargeLanding(int param1, int param2, int param3, object payload)
        {
            // TODO: 从相关 commit 提取
        }

        private void ClientNativeCheatSelfReport(int param1, int param2)
        {
            // TODO: 从相关 commit 提取
        }

        private void ClientYbConsignmentQuery(int queryType)
        {
            // TODO: 从相关 commit 提取
        }

        private bool IsNativeMoveBlockedByPassengerState()
        {
            // TODO: 从 MOVE 系列 commit 提取
            return false;
        }

        private void BreakNativeStealthOnAction()
        {
            // TODO: 从相关 commit 提取
        }

        private const int NativeHitGateConsume = 1;
        private const int NativeHitGateProceed = 2;

        private int RunNativeHitArmGates(int ident)
        {
            // TODO: 从相关 commit 提取
            return NativeHitGateProceed;
        }

        private void NotifyNativeActionReveal(int actionType)
        {
            // TODO: 从相关 commit 提取
        }

        private bool NativeNoMagicMapForbidsSpell()
        {
            // TODO: 从相关 commit 提取
            return false;
        }

        private void ClientNativeCm3290ClockSnapshot()
        {
            // TODO: 从相关 commit 提取
        }

        private void ClientNativeCm4629GroupPositions()
        {
            // TODO: 从相关 commit 提取
        }

        private bool TryHandleInlayCm(TProcessMessage msg) => false;
        private bool TryHandleQiankunCm(TProcessMessage msg) => false;
        private bool TryHandleItemTransferCm(TProcessMessage msg) => false;
        private bool TryHandleStallWriteCm(TProcessMessage msg) => false;
        private bool TryHandleEquipLockCm(TProcessMessage msg) => false;
        private bool TryHandleQuizBroadcastCm(TProcessMessage msg) => false;
        private bool TryHandleCloneNpcCm(TProcessMessage msg) => false;
        private bool TryHandleMallCm(TProcessMessage msg) => false;
        private bool TryHandleNameQueryCm(TProcessMessage msg) => false;
        private bool TryHandleNewbieQuestCm(TProcessMessage msg) => false;
        private bool TryHandleSoulWashCm(TProcessMessage msg) => false;
        private bool TryHandleYbConsignWriteCm(TProcessMessage msg) => false;
        private bool TryHandleMemberRosterCm(TProcessMessage msg) => false;
        private bool TryHandleHeroSpiritBeadCm(TProcessMessage msg) => false;
        private bool TryHandleRewardCm(TProcessMessage msg) => false;
        private bool TryHandleMessageBoardCm(TProcessMessage msg) => false;
        private bool TryHandleFreeRecycleCm(TProcessMessage msg) => false;
        private bool TryHandleTimedActivityCm(TProcessMessage msg) => false;
        private bool TryHandleSkillStoneCm(TProcessMessage msg) => false;
        private bool TryHandleHeroNotifyCm(TProcessMessage msg) => false;
        private bool TryHandleHorseTokenCm(TProcessMessage msg) => false;
        private bool TryHandleCmMiscTail(TProcessMessage msg) => false;
        private bool TryHandleTaskBoardScriptCm(TProcessMessage msg) => false;
        private bool TryHandleNativeCmTailProtocol(TProcessMessage msg) => false;
        private bool TryHandleNativeCmQ1(TProcessMessage msg) => false;
        private bool TryHandleNativeCmQ2(TProcessMessage msg) => false;
        private bool TryHandleNativeCmQ3(TProcessMessage msg) => false;

        private void ApplyChatShieldMaskToAllowFlags()
        {
            // TODO: 从相关 commit 提取
        }

        private bool HasNativeCellPassThroughGrant()
        {
            // TODO: 从 MOVE-71/72 穿透系统 commit 提取
            return false;
        }

        private string NativeMapDescOf(Envirnoment envir)
        {
            // TODO: 从相关 commit 提取
            return envir?.sMapName ?? string.Empty;
        }
    }
}
