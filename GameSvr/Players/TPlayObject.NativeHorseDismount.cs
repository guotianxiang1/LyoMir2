using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        protected void RemoveNativeHorseTimedState(byte internalType)
        {
            ClearNativeActiveState(internalType);
            SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED, 0,
                unchecked((ushort)m_nHitSpeed), 0, 0, string.Empty,
                GetBodyStateBuffer());
            SendSocket(Grobal2.MakeDefaultMsg(3555, 0, internalType, 0, 0));
        }
    }

    public partial class TPlayObject
    {
        internal bool m_boNativeHorsePairReady;
        internal bool m_boNativeHorsePassengerActive;

        private void CleanupNativeHorseOnExit()
        {
            if (HasNativeActiveState(NativeHorseMountedState))
            {
                ClientNativeHorseDismount();
            }
            if (HasNativeActiveState(NativeHorseBlockedState))
            {
                NativeHorseRiderDownCore();
            }
        }

        internal void CleanupNativeHorseBeforeSpaceMove()
        {
            var mounted = HasNativeActiveState(NativeHorseMountedState);
            if ((!mounted || m_NativeHorsePartner == null) &&
                !HasNativeActiveState(NativeHorseBlockedState))
            {
                return;
            }

            if (mounted)
            {
                ClientNativeHorseDismount();
            }
            if (HasNativeActiveState(NativeHorseBlockedState))
            {
                NativeHorseRiderDownCore();
            }
        }

        public override void Die()
        {
            if (HasNativeActiveState(NativeHorseMountedState))
            {
                ClientNativeHorseDismount();
            }
            base.Die();
            OnNativeHostPlayerDeath();
            // 死亡触发 @OnDie：眼神把 trampoline 挂在 TPlayer.Die(0x6C03F8) 的唯一 epilogue
            // 0x6C09B5，即所有死亡处理与 SEH finally 汇合、函数返回之前无条件发一次。
            // 惰性门在 FireOnDie 内（插件缺席时零派发）。见 YanshenTriggerDispatch。
            GameSvr.Plugins.YanshenTriggerDispatch.FireOnDie(this);
        }

        private void ClientNativeHorseDismount()
        {
            if (!HasNativeActiveState(NativeHorseMountedState))
            {
                return;
            }

            RemoveNativeHorseTimedState(NativeHorseMountedState);
            ClearNativeHorseCallPending();
            m_btHorseType = 0;
            m_boOnHorse = false;
            SendNativeHorseDismountPacket(Grobal2.RM_NATIVE_XIAMA_OK);
            FeatureChanged();
            SearchViewRange();

            var partner = m_NativeHorsePartner;
            if (partner != null)
            {
                partner.NativeHorseRiderDownCore();
                m_NativeHorsePartner = null;
            }
        }

        private void ClientNativeHorseRiderDown()
        {
            if (!HasNativeActiveState(NativeHorseBlockedState))
            {
                return;
            }

            NativeHorseRiderDownCore();
        }

        private void NativeHorseRiderDownCore()
        {
            RemoveNativeHorseTimedState(NativeHorseBlockedState);
            m_btHorseType = 0;
            m_boOnHorse = false;
            SendNativeHorseDismountPacket(Grobal2.RM_NATIVE_XIAMA_2);
            FeatureChanged();
            SearchViewRange();

            m_boNativeHorsePassengerActive = false;
            MoveNativeHorsePassengerOffSharedCell();

            var driver = m_NativeHorsePartner;
            if (driver != null &&
                driver.HasNativeActiveState(NativeHorseMountedState))
            {
                driver.m_NativeHorsePartner = null;
                driver.m_boNativeHorsePairReady = true;
                driver.SendNativeHorseDismountPacket(
                    Grobal2.RM_NATIVE_XIAMA_2);
                SearchViewRange();
                FeatureChanged();
            }

            m_NativeHorsePartner = null;
        }

        private void MoveNativeHorsePassengerOffSharedCell()
        {
            if (m_PEnvir == null)
            {
                return;
            }

            for (var offset = 0; offset < 8; offset++)
            {
                var direction = (byte)((m_btDirection + offset) % 8);
                var nextX = m_nCurrX + (direction switch
                {
                    Grobal2.DR_UPRIGHT or Grobal2.DR_RIGHT or
                        Grobal2.DR_DOWNRIGHT => 1,
                    Grobal2.DR_DOWNLEFT or Grobal2.DR_LEFT or
                        Grobal2.DR_UPLEFT => -1,
                    _ => 0
                });
                var nextY = m_nCurrY + (direction switch
                {
                    Grobal2.DR_DOWNRIGHT or Grobal2.DR_DOWN or
                        Grobal2.DR_DOWNLEFT => 1,
                    Grobal2.DR_UPLEFT or Grobal2.DR_UP or
                        Grobal2.DR_UPRIGHT => -1,
                    _ => 0
                });
                if (!CanNativeHorsePassengerDismountAt(nextX, nextY))
                {
                    continue;
                }
                if (NativeRun3FallbackWalk(direction))
                {
                    return;
                }
            }
        }

        private bool CanNativeHorsePassengerDismountAt(int x, int y)
        {
            var success = false;
            var cell = m_PEnvir.GetMapCellInfo(x, y, ref success);
            if (!success || cell.ObjList == null)
            {
                return true;
            }

            for (var index = 0; index < cell.Count; index++)
            {
                var cellObject = cell.ObjList[index];
                if (cellObject.CellType == CellType.OS_GATEOBJECT)
                {
                    return false;
                }
                if (cellObject.CellType != CellType.OS_MOVINGOBJECT ||
                    cellObject.CellObj is not TBaseObject actor)
                {
                    continue;
                }
                if (!actor.m_boGhost && actor.bo2B9 && !actor.m_boDeath &&
                    !actor.m_boFixedHideMode && !actor.m_boObMode)
                {
                    return false;
                }
            }
            return true;
        }

        private void SendNativeHorseDismountPacket(int refMessage)
        {
            var body = BuildNativeHorseDismountBody(this);
            SendRefMsg(refMessage, 1, 51, 0, 1, string.Empty, body);
        }

        internal static byte[] BuildNativeHorseDismountBody(
            TPlayObject player)
        {
            var body = new byte[51];
            var feature = player?.GetMobileFeature() ?? Array.Empty<byte>();
            Buffer.BlockCopy(feature, 0, body, 0,
                Math.Min(10, feature.Length));

            var name = HUtil32.GbkEncoding.GetBytes(
                player?.GetShowName() ?? string.Empty);
            var length = Math.Min(40, name.Length);
            body[10] = (byte)length;
            Buffer.BlockCopy(name, 0, body, 11, length);
            return body;
        }
    }
}
