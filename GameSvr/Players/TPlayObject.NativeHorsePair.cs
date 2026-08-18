using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private void ClientNativeHorseInvite(int targetObjectId)
        {
            if (!HasNativeActiveState(NativeHorseMountedState) ||
                HasNativeActiveState(NativeHorseBlockedState) ||
                m_NativeHorsePartner != null ||
                !m_boNativeHorsePairReady)
            {
                return;
            }

            var target = M2Share.ObjectManager?.Get(targetObjectId)
                as TPlayObject;
            if (!IsNativeHorsePartnerInRange(target, 4, true) ||
                target.m_boDeath || target.m_boGhost)
            {
                return;
            }

            if (target.m_btGender != PlayGender.Man)
            {
                SendNativeHorseSystemMessage("只能对玩家进行邀请！");
                return;
            }

            if (target.HasNativeActiveState(NativeHorseMountedState) ||
                target.HasNativeActiveState(NativeHorseBlockedState))
            {
                SendNativeHorseSystemMessage(
                    "被邀请人在坐骑状态,无法接受双人坐骑!");
                return;
            }

            if (target.m_boNativeHorsePassengerActive)
            {
                SendNativeHorseSystemMessage("对方正被别人邀请上马！");
                return;
            }

            if (!IsNativeHorsePartnerInRange(target, 3))
            {
                SendNativeHorseSystemMessage(
                    "被邀请人不在3格范围内,无法接受双人坐骑!");
                return;
            }

            target.SendDefMessage((short)Grobal2.SM_INVITE_HORSE,
                ObjectId, 0, 0, 0, string.Empty);
            m_boNativeHorsePairReady = false;
            target.m_boNativeHorsePassengerActive = true;
        }

        private void ClientNativeHorseInviteResponse(int driverObjectId,
            int acceptFlag)
        {
            var driver = M2Share.ObjectManager?.Get(driverObjectId)
                as TPlayObject;
            if (!IsNativeHorsePartnerInRange(driver, 4, true))
            {
                return;
            }

            driver.m_boNativeHorsePairReady = true;
            if (driver.m_btGender != PlayGender.Man || driver.m_boDeath ||
                driver.m_boGhost)
            {
                return;
            }

            m_boNativeHorsePassengerActive = false;
            if (acceptFlag == 0)
            {
                driver.SendNativeHorseSystemMessage("对方拒绝上马邀请");
                return;
            }

            var mount = driver.m_UseItems != null &&
                        driver.m_UseItems.Length > Grobal2.U_MOUNT
                ? driver.m_UseItems[Grobal2.U_MOUNT]
                : null;
            if (mount == null)
            {
                return;
            }

            var mountType = ResolveNativeMountType(mount);
            if (HasNativeActiveState(NativeHorseBlockedState) ||
                HasNativeActiveState(NativeHorseMountedState))
            {
                return;
            }

            if (!driver.HasNativeActiveState(NativeHorseMountedState) ||
                driver.HasNativeActiveState(NativeHorseBlockedState) ||
                driver.m_NativeHorsePartner != null)
            {
                SendNativeHorseSystemMessage(
                    "对方不在单人坐骑状态,无法接受双人坐骑");
                return;
            }

            if (!IsNativeHorsePartnerInRange(driver, 3))
            {
                SendNativeHorseSystemMessage(
                    "对方不在3格范围内,无法接受双人坐骑!");
                return;
            }

            m_NativeHorsePartner = driver;
            m_btHorseType = mountType;
            SetNativeActiveState(NativeHorseBlockedState);
            SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED, 0,
                unchecked((ushort)m_nHitSpeed), 0, 0, string.Empty,
                GetBodyStateBuffer());
            SendSocket(Grobal2.MakeDefaultMsg(3555, 0,
                NativeHorseBlockedState, 0, 0));
            m_boOnHorse = true;
            m_btDirection = driver.m_btDirection;
            MoveToNativeHorseDriver(driver);
            SendNativeHorsePairPacket(driver, this, this);

            driver.m_NativeHorsePartner = this;
            driver.m_btHorseType = m_btHorseType;
            driver.SendNativeHorsePairPacket(driver, this, driver);
        }

        private bool IsNativeHorsePartnerInRange(TPlayObject partner,
            int range, bool requireExactMapCell = false)
        {
            if (partner == null || m_PEnvir == null ||
                !ReferenceEquals(m_PEnvir, partner.m_PEnvir))
            {
                return false;
            }

            if (!requireExactMapCell)
            {
                return Math.Max(Math.Abs(m_nCurrX - partner.m_nCurrX),
                    Math.Abs(m_nCurrY - partner.m_nCurrY)) <= range;
            }

            for (var x = m_nCurrX - range; x <= m_nCurrX + range; x++)
            {
                for (var y = m_nCurrY - range;
                     y <= m_nCurrY + range; y++)
                {
                    var success = false;
                    var cell = m_PEnvir.GetMapCellInfo(x, y,
                        ref success);
                    if (!success || cell.ObjList == null)
                    {
                        continue;
                    }

                    for (var index = 0; index < cell.Count; index++)
                    {
                        var cellObject = cell.ObjList[index];
                        if (cellObject?.CellType ==
                                CellType.OS_MOVINGOBJECT &&
                            ReferenceEquals(cellObject.CellObj, partner) &&
                            !partner.m_boGhost)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void MoveToNativeHorseDriver(TPlayObject driver)
        {
            if (!m_PEnvir.NativeRelocateMovingObjectNodeExact(
                    m_nCurrX, m_nCurrY, this,
                    driver.m_nCurrX, driver.m_nCurrY))
            {
                return;
            }

            m_nCurrX = driver.m_nCurrX;
            m_nCurrY = driver.m_nCurrY;
            RemoveNativeMovementTimedState(23);
            ProcessNativeMoveActionWithoutBroadcast();
            SendMapDescription();
        }

        private void SendNativeHorsePairPacket(TPlayObject driver,
            TPlayObject passenger, TPlayObject featureOwner)
        {
            var body = BuildNativeHorsePairBody(driver, passenger,
                featureOwner);
            SendRefMsg(Grobal2.RM_NATIVE_SHANGMA_OK2, 1, body.Length, 0,
                HUtil32.MakeWord(m_btHorseType, (byte)m_btGender),
                string.Empty, body);
        }

        internal static byte[] BuildNativeHorsePairBody(TPlayObject driver,
            TPlayObject passenger, TPlayObject featureOwner)
        {
            var body = new byte[68];
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, 4),
                driver?.ObjectId ?? 0);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4),
                passenger?.ObjectId ?? 0);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, 4),
                driver?.m_nCurrX ?? 0);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12, 4),
                driver?.m_nCurrY ?? 0);
            body[16] = driver?.m_btDirection ?? 0;

            var name = HUtil32.GbkEncoding.GetBytes(
                featureOwner?.GetShowName() ?? string.Empty);
            var nameLength = Math.Min(40, name.Length);
            body[17] = (byte)nameLength;
            Buffer.BlockCopy(name, 0, body, 18, nameLength);
            var feature = featureOwner?.GetMobileFeature()
                          ?? Array.Empty<byte>();
            Buffer.BlockCopy(feature, 0, body, 58,
                Math.Min(10, feature.Length));
            return body;
        }
    }
}
