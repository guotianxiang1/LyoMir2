using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeHorseMountedState = 51;
        private const int NativeHorseBlockedState = 52;
        private const int NativeMountTypeOffset = 0x33;

        private void ClientNativeHorseReady()
        {
            if (HasNativeActiveState(NativeHorseBlockedState) ||
                HasNativeActiveState(NativeHorseMountedState) ||
                !m_boNativeHorseCallPending)
            {
                return;
            }

            var mount = m_UseItems != null &&
                        m_UseItems.Length > Grobal2.U_MOUNT
                ? m_UseItems[Grobal2.U_MOUNT]
                : null;
            if (mount == null || mount.wIndex == 0)
            {
                SendNativeHorseSystemMessage(
                    "您无主宰者马牌,无法召唤坐骑！");
                ClearNativeHorseCallPending();
                return;
            }

            var elapsed = unchecked((uint)HUtil32.GetTickCount() -
                                    m_dwNativeHorseCallTick);
            if (elapsed < m_wNativeHorseCallDelay)
            {
                return;
            }

            SetNativeActiveState(NativeHorseMountedState);
            SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED, 0,
                unchecked((ushort)m_nHitSpeed), 0, 0, string.Empty,
                GetBodyStateBuffer());
            SendSocket(Grobal2.MakeDefaultMsg(3555, 0,
                NativeHorseMountedState, 0, 0));
            m_btHorseType = ResolveNativeMountType(mount);
            m_boOnHorse = true;
            m_boNativeHorsePairReady = true;
            SendNativeHorseSystemMessage("成功召唤坐骑！");
            FeatureChanged();
            SendRefMsg(Grobal2.RM_SHANGMA_OK, 1, 0, 0, 0,
                string.Empty, GetMobileFeature());
            ClearNativeHorseCallPending();
        }

        private void SendNativeHorseSystemMessage(string message)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0,
                message);
        }

        private static byte ResolveNativeMountType(TUserItem mount)
        {
            if (mount?.NativeRecord == null ||
                mount.NativeRecord.Length <= NativeMountTypeOffset)
            {
                return 0;
            }

            var mountType = mount.NativeRecord[NativeMountTypeOffset];
            if (mountType == 0)
            {
                mount.NativeRecord[NativeMountTypeOffset] = 1;
            }
            return mountType;
        }

        private void ClearNativeHorseCallPending()
        {
            m_boNativeHorseCallPending = false;
            m_dwNativeHorseCallTick = 0;
            m_wNativeHorseCallDelay = 0;
        }

        private bool ClientNativeRun3(int destinationX, int destinationY)
        {
            if (!HasNativeActiveState(NativeHorseMountedState) ||
                HasNativeActiveState(45) ||
                HasNativeActiveState(29) ||
                HasNativeActiveState(1) ||
                HasNativeActiveState(26) ||
                HasNativeActiveState(24) ||
                HasNativeActiveState(62) ||
                m_boDeath || m_nNativeForcedMoveRemaining != 0 ||
                m_PEnvir == null)
            {
                return false;
            }

            m_bo316 = false;
            var useMainRun = (!M2Share.ServerSwitches.IsBitSet(2, 0x80) ||
                              m_PEnvir.NativeCanRunWhileOverweight ||
                              m_WAbil.Weight < m_WAbil.MaxWeight) &&
                             !HasNativeActiveState(67) &&
                             !HasNativeActiveState(13);
            if (!useMainRun)
            {
                return ClientNativeRun3Fallback(destinationX, destinationY);
            }

            var direction = M2Share.GetNextDirection(m_nCurrX, m_nCurrY,
                destinationX, destinationY);
            if (!NativeRun3To(direction))
            {
                return false;
            }

            m_nHealthTick -= 60;
            m_nSpellTick = HUtil32._MAX(0, m_nSpellTick - 10);
            DecreaseHealthSpellRecoveryStep(1);

            var result = m_bo316 ||
                         m_nCurrX == destinationX &&
                         m_nCurrY == destinationY;
            if (result)
            {
                m_dwActionTick = HUtil32.GetTickCount();
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }

        private bool ClientNativeRun3Fallback(int destinationX,
            int destinationY)
        {
            var adjustedX = destinationX <= m_nCurrX
                ? destinationX + 1
                : destinationX - 1;
            var adjustedY = destinationY <= m_nCurrY
                ? destinationY + 1
                : destinationY - 1;
            adjustedX = HUtil32._MAX(0, adjustedX);
            adjustedY = HUtil32._MAX(0, adjustedY);

            var direction = M2Share.GetNextDirection(m_nCurrX, m_nCurrY,
                adjustedX, adjustedY);
            if (!NativeRun3FallbackWalk(direction))
            {
                return false;
            }

            m_nHealthTick -= 10;
            var result = m_bo316 ||
                         m_nCurrX == adjustedX && m_nCurrY == adjustedY;
            if (result)
            {
                m_dwActionTick = HUtil32.GetTickCount();
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }

        private bool NativeRun3FallbackWalk(byte direction)
        {
            if (direction >= 8 || m_PEnvir == null)
            {
                return false;
            }

            var offsetX = direction switch
            {
                Grobal2.DR_UPRIGHT or Grobal2.DR_RIGHT or
                    Grobal2.DR_DOWNRIGHT => 1,
                Grobal2.DR_DOWNLEFT or Grobal2.DR_LEFT or
                    Grobal2.DR_UPLEFT => -1,
                _ => 0
            };
            var offsetY = direction switch
            {
                Grobal2.DR_DOWNRIGHT or Grobal2.DR_DOWN or
                    Grobal2.DR_DOWNLEFT => 1,
                Grobal2.DR_UPLEFT or Grobal2.DR_UP or
                    Grobal2.DR_UPRIGHT => -1,
                _ => 0
            };

            var oldX = m_nCurrX;
            var oldY = m_nCurrY;
            var nextX = oldX + offsetX;
            var nextY = oldY + offsetY;
            m_btDirection = direction;
            if (nextX <= 0 || nextX >= m_PEnvir.wWidth ||
                nextY <= 0 || nextY >= m_PEnvir.wHeight ||
                m_PEnvir.MoveToMovingObject(oldX, oldY, this, nextX, nextY,
                    m_boInSafeArea) <= 0)
            {
                return false;
            }

            m_nCurrX = (short)nextX;
            m_nCurrY = (short)nextY;
            CompleteNativeRun3Move(Grobal2.RM_WALK);
            return true;
        }

        private void CompleteNativeRun3Move(int movementIdent)
        {
            RemoveNativeMovementTimedState(23);
            SendRefMsg(movementIdent, m_btDirection, m_nCurrX, m_nCurrY,
                0, string.Empty);
            ProcessNativeMoveActionWithoutBroadcast();
            SyncNativeHorsePartnerAfterRun3();
        }

        private bool NativeRun3To(byte direction)
        {
            if (direction >= 8 || m_PEnvir == null)
            {
                return false;
            }

            var offsetX = direction switch
            {
                Grobal2.DR_UPRIGHT or Grobal2.DR_RIGHT or
                    Grobal2.DR_DOWNRIGHT => 1,
                Grobal2.DR_DOWNLEFT or Grobal2.DR_LEFT or
                    Grobal2.DR_UPLEFT => -1,
                _ => 0
            };
            var offsetY = direction switch
            {
                Grobal2.DR_DOWNRIGHT or Grobal2.DR_DOWN or
                    Grobal2.DR_DOWNLEFT => 1,
                Grobal2.DR_UPLEFT or Grobal2.DR_UP or
                    Grobal2.DR_UPRIGHT => -1,
                _ => 0
            };

            m_btDirection = direction;
            var ignoreObjects = M2Share.g_Config.boDiableHumanRun ||
                                m_btPermission > 9 &&
                                M2Share.g_Config.boGMRunAll;
            if (!m_PEnvir.CanWalkEx(m_nCurrX + offsetX,
                    m_nCurrY + offsetY, ignoreObjects))
            {
                return false;
            }

            var destinationX = m_nCurrX + 3 * offsetX;
            var destinationY = m_nCurrY + 3 * offsetY;
            if (!CommitRunMove(destinationX, destinationY))
            {
                return false;
            }

            m_nCurrX = (short)destinationX;
            m_nCurrY = (short)destinationY;
            CompleteNativeRun3Move(Grobal2.RM_RUN3);
            return true;
        }
    }
}
