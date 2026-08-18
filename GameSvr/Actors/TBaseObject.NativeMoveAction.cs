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
                return false;
            }

            var result = false;
            var sourceEnvironment = m_PEnvir;
            var sourceX = m_nCurrX;
            var sourceY = m_nCurrY;
            try
            {
                var mapCell = false;
                var mapCellInfo = sourceEnvironment.GetMapCellInfo(sourceX,
                    sourceY, ref mapCell);
                if (!mapCell || mapCellInfo.ObjList == null)
                {
                    return result;
                }

                var hasInactiveDigOutZombi = false;
                var eventNodes = mapCellInfo.ObjList;
                var eventIndex = 0;
                while (eventIndex < eventNodes.Count)
                {
                    var cellObject = eventNodes[eventIndex];
                    if (cellObject?.CellType != CellType.OS_EVENTOBJECT
                        || cellObject.CellObj is not Event mapEvent)
                    {
                        eventIndex++;
                        continue;
                    }

                    if (mapEvent.NativeAppliesOnLanding)
                    {
                        mapEvent.ApplyTo(this);
                    }
                    else if (mapEvent.m_nEventType ==
                             Grobal2.ET_DIGOUTZOMBI)
                    {
                        hasInactiveDigOutZombi = true;
                    }

                    // 0x778F51 reloads currentNode.Next after ApplyTo returns.
                    // Keep following the live cell chain: a callback may remove
                    // its successor, and an insertion at the head must not make
                    // the current event run twice.
                    var currentIndex = eventNodes.IndexOf(cellObject);
                    eventIndex = currentIndex >= 0
                        ? currentIndex + 1
                        : Math.Min(eventIndex, eventNodes.Count);
                }

                // 0x778F60..0x778F6F: events apply to every actor, but only a
                // TPlayer proceeds to DROPTOMAP or gate handling.
                if (m_btRaceServer != Grobal2.RC_PLAYOBJECT
                    || this is not TPlayObject player)
                {
                    return result;
                }

                // 0x778F75..0x778F8E: DROPTOMAP plus a non-landing type-1
                // event invokes sub_768C7C and still returns zero.
                if (sourceEnvironment.Flag.boDROPTOMAP
                    && hasInactiveDigOutZombi)
                {
                    NativeDropToMapRandomMove(
                        sourceEnvironment.Flag.sDropToMap);
                    return result;
                }

                // Native starts a second pass from the current cell head after
                // all event callbacks and selects the first non-null gate.
                mapCell = false;
                mapCellInfo = sourceEnvironment.GetMapCellInfo(sourceX,
                    sourceY, ref mapCell);
                if (!mapCell || mapCellInfo.ObjList == null)
                {
                    return result;
                }

                TGateObj gate = null;
                for (var i = 0; i < mapCellInfo.Count; i++)
                {
                    var cellObject = mapCellInfo.ObjList[i];
                    if (cellObject?.CellType == CellType.OS_GATEOBJECT
                        && cellObject.CellObj is TGateObj candidate)
                    {
                        gate = candidate;
                        result = true; // 0x778FBF, before either admission call
                        break;
                    }
                }
                if (gate == null)
                {
                    return result;
                }

                // sub_779064 first resolves the door number on the actor's
                // exact cell through sub_778E48. A closed door in a neighboring
                // cell is unrelated; only a closed current-cell door blocks.
                // The false arm keeps result=true and therefore suppresses the
                // turn broadcast even though no transfer occurs.
                var currentDoor = sourceEnvironment.GetDoor(sourceX, sourceY);
                if (currentDoor?.Status != null
                    && !currentDoor.Status.boOpened)
                {
                    return result;
                }

                // sub_77C1AC clears the result only for NEEDHOLE without the
                // type-1 event, or for a rejected active-point admission.
                if (gate.DEnvir.Flag.boNEEDHOLE
                    && !hasInactiveDigOutZombi)
                {
                    result = false;
                    return result;
                }
                if (!NativeMapActivePointLoader.CanEnterActiveMap(
                        player, gate.DEnvir))
                {
                    result = false;
                    return result;
                }

                // sub_78FE80 is `mov al,1; ret` in this image, so the local
                // VMT+0x9C arm is unconditional. EnterAnotherMap's return is
                // discarded; gate admission alone keeps result=true.
                _ = EnterAnotherMap(gate.DEnvir, gate.nDMapX, gate.nDMapY);
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

        private void NativeDropToMapRandomMove(string mapName)
        {
            var targetEnvironment = M2Share.MapManager?.FindMap(mapName);
            if (targetEnvironment == null)
            {
                return;
            }

            // sub_768CBA draws Y first from Height, then X from Width, before
            // calling VMT+0x1C0 with showMode=0.
            var targetY = unchecked((short)M2Share.RandomNumber.Random(
                targetEnvironment.wHeight));
            var targetX = unchecked((short)M2Share.RandomNumber.Random(
                targetEnvironment.wWidth));
            // sub_768C7C calls TPlayer VMT+0x1C0 directly. That implementation
            // has no nServerIndex dispatch, so even a remote-index map object
            // takes the ordinary local landing transaction here.
            TrySpaceMoveToEnvironment(targetEnvironment, targetX, targetY, 0,
                requireLocalServerIndex: false);
        }
    }
}
