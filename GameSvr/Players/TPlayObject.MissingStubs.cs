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

        /// <summary>
        /// From commit 14718608 (STATE-53). Clears legacy status slots.
        /// NOTE: Requires TBaseObject.LegacyStatusSlotCount and related methods.
        /// </summary>
        private void ClearLegacyStatusSlots()
        {
            // TODO: Re-enable when TBaseObject.LegacyStatusSlotCount is available
        }

        /// <summary>
        /// From commit f3d12db7 (SM-C 3554). Sends native logon state sync.
        /// NOTE: Requires BuildNativeTimedAbilitySnapshot.
        /// </summary>
        private void SendNativeLogonStateSync()
        {
            // TODO: Re-enable when BuildNativeTimedAbilitySnapshot is available
        }

        /// <summary>
        /// From commit 20cf6591. Native skill 68 charge landing handler.
        /// Relocates the actor after charge movement completes.
        /// </summary>
        private void ProcessNativeSkill68ChargeLanding(int landX, int landY, int direction, object envirPayload)
        {
            var envir = m_PEnvir;
            // 0x6EC8F5: verify environment hasn't changed
            if (envir == null || !ReferenceEquals(envirPayload, envir))
            {
                return;
            }
            // 0x6EC922: move without re-testing occupancy (6A 01)
            if (envir.MoveToMovingObject(m_nCurrX, m_nCurrY, this, (short)landX,
                    (short)landY, true) <= 0)
            {
                return;
            }
            m_nCurrX = (short)landX;
            m_nCurrY = (short)landY;
            m_btDirection = (byte)direction;
        }

        private void ClientNativeCheatSelfReport(int param1, int param2)
        {
            // TODO: 从相关 commit 提取
        }

        private bool IsNativeMoveBlockedByPassengerState()
        {
            // TODO: 从 MOVE 系列 commit 提取
            return false;
        }

        /// <summary>
        /// From MOVE-11. Native sub_7742C0 - breaks stealth on action.
        /// NOTE: Requires RemoveTimedAbilityInternal to be accessible.
        /// </summary>
        private void BreakNativeStealthOnAction()
        {
            // TODO: Re-enable when RemoveTimedAbilityInternal is accessible
        }

        /// <summary>Gate ladder passed; call ClientHitXY.</summary>
        internal const int NativeHitGateProceed = 0;

        /// <summary>0x6D9EC3 - consume message in silence.</summary>
        internal const int NativeHitGateConsume = 1;

        /// <summary>0x6D9F5F - take 0x276 refusal block.</summary>
        internal const int NativeHitGateRefuse = 2;

        /// <summary>
        /// From commit b5cdba83 (HIT-ARM). Native hit arm gates dispatcher.
        /// NOTE: Requires IsNativeHitBlockedByMountState and CancelNativeActionChannels.
        /// </summary>
        private int RunNativeHitArmGates(int ident)
        {
            // TODO: Re-enable when dependencies are available
            return NativeHitGateProceed;
        }

        private const byte NativeHideState = 0x3C;
        private const int NativeRevealExemptIdent = 0x10B;

        /// <summary>
        /// From commit b5cdba83 (HIT-ARM). Native sub_6F2D48 - action reveal hook.
        /// NOTE: Requires BreakNativeHideOnAction.
        /// </summary>
        private void NotifyNativeActionReveal(int actionType)
        {
            // TODO: Re-enable when BreakNativeHideOnAction is available
        }

        /// <summary>
        /// From commit 597075b9 (MOVE-90). Native NOMAGIC map flag checker.
        /// Tests byte [PEnvir+0x81] to forbid spell casting on NOMAGIC maps.
        /// </summary>
        private bool NativeNoMagicMapForbidsSpell()
        {
            // native 0x6DA125 mov eax,[Self+0x128] / 0x6DA12B cmp byte [eax+0x81],0
            // Null map fails OPEN (safe direction - allows casting)
            return m_PEnvir != null && m_PEnvir.Flag != null && m_PEnvir.Flag.boNOMAGIC;
        }


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
