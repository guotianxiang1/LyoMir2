using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        protected void RemoveNativeMovementTimedState(byte internalType)
        {
            RemoveTimedAbilityInternal(internalType);
        }

        protected bool ProcessNativeMoveActionWithoutBroadcast()
        {
            const string exceptionMessage =
                "[Exception] TBaseObject::ProcessNativeMoveActionWithoutBroadcast {0} {1} {2}:{3}";
            if (m_PEnvir == null)
            {
                return true;
            }

            var result = true;
            try
            {
                var mapCell = false;
                var mapCellInfo = m_PEnvir.GetMapCellInfo(m_nCurrX,
                    m_nCurrY, ref mapCell);
                if (!mapCell || mapCellInfo.ObjList == null)
                {
                    return result;
                }

                for (var i = 0; i < mapCellInfo.Count; i++)
                {
                    var cellObject = mapCellInfo.ObjList[i];
                    switch (cellObject.CellType)
                    {
                        case CellType.OS_GATEOBJECT:
                            var gate = (TGateObj)cellObject.CellObj;
                            if (gate == null)
                            {
                                break;
                            }
                            if (m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                            {
                                result = false;
                                break;
                            }
                            if (!m_PEnvir.ArroundDoorOpened(m_nCurrX,
                                    m_nCurrY) ||
                                gate.DEnvir.Flag.boNEEDHOLE &&
                                M2Share.EventManager.GetEvent(m_PEnvir,
                                    m_nCurrX, m_nCurrY,
                                    Grobal2.ET_DIGOUTZOMBI) == null)
                            {
                                break;
                            }
                            if (M2Share.nServerIndex ==
                                gate.DEnvir.nServerIndex)
                            {
                                if (!EnterAnotherMap(gate.DEnvir,
                                        gate.nDMapX, gate.nDMapY))
                                {
                                    result = false;
                                }
                            }
                            else if (TryBeginCrossServerTransfer(gate.DEnvir,
                                         gate.nDMapX, gate.nDMapY))
                            {
                                return result;
                            }
                            break;
                        case CellType.OS_EVENTOBJECT:
                            ((Event)cellObject.CellObj).ApplyTo(this);
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                M2Share.ErrorMessage(format(exceptionMessage,
                    new object[]
                    {
                        m_sCharName, m_sMapName, m_nCurrX, m_nCurrY
                    }));
                M2Share.ErrorMessage(exception.Message);
            }
            return result;
        }
    }
}
