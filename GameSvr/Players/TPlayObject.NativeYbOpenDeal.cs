using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// NPC 开启元宝交易系统对话展示 — native 0x00637BEC.
        /// Branch ladder (0x637C25 add edi,3 then dec/dec/dec/sub 2):
        ///   result 2  -> success dialog + SM 0xBC1(3009) + capital refresh (0x6D340C)
        ///   result -1 -> insufficient yuanbao (0x637CF8)
        ///   result -2 -> already opened (0x637D24)
        ///   result -3 -> no recharge (0x637D5C)
        ///   else      -> failure (0x637D94)
        /// </summary>
        internal void ApplyNativeOpenYbDealNpcResult(NormNpc npc, int nativeResultCode)
        {
            if (npc == null) return;
            m_NPC = npc;

            if (nativeResultCode == 2)
            {
                SendNativeYbNpcDialog(npc,
                    YbDbOpenDealProtocol.GetDialog(YbDbOpenDealProtocol.SuccessResult));
                foreach (var packet in BuildNativeYbDealPackets(m_wNativeYbDealProtect))
                    SendSocket(packet, System.Array.Empty<byte>());
                SendNativeCapitalInfo();
                return;
            }

            var dialog = nativeResultCode switch
            {
                -1 => YbDbOpenDealProtocol.GetDialog(
                    YbDbOpenDealProtocol.InsufficientYuanbaoResult),
                -2 => YbDbOpenDealProtocol.GetDialog(
                    YbDbOpenDealProtocol.AlreadyOpenedResult),
                -3 => YbDbOpenDealProtocol.GetDialog(
                    YbDbOpenDealProtocol.NoRechargeResult),
                _ => YbDbOpenDealProtocol.GetDialog(0)
            };
            SendNativeYbNpcDialog(npc, dialog);
        }

        /// <summary>
        /// PAS ClientAskOpenYB — submits ident 112 to YBDB; on link-down shows the native
        /// unavailable dialog (0x637D94 family via RequestUnavailableDialog).
        /// </summary>
        internal void ClientAskOpenYb(NormNpc npc)
        {
            if (npc == null) return;
            m_NPC = npc;

            // YbDbClient keeps this request behind its disabled authority gate. The
            // native unavailable dialog is only a response to that closed boundary;
            // no local success or currency mutation is substituted.
            if (!YbDbClient.Instance.TryRequestOpenDeal(this))
                SendNativeYbNpcDialog(npc,
                    YbDbOpenDealProtocol.RequestUnavailableDialog);
        }

        /// <summary>
        /// YBDB 1112 response path — maps protocol ResultCode to the 637BEC native ladder
        /// (protocol 1 -> native 2 on success).
        /// </summary>
        internal void ApplyNativeOpenYbDealDbResult(YbDbOpenDealResult result)
        {
            if (result == null) return;

            m_nGameGold = result.CurrentYuanbao;
            m_nNativeYbTotalConsumed = result.TotalConsumed;
            m_nNativeYbRemainingSeconds = result.RemainingSeconds;
            m_nNativeYbDividendConsumed = result.DividendConsumed;
            RefreshNativeLingFu();

            var nativeCode = result.ResultCode switch
            {
                YbDbOpenDealProtocol.SuccessResult => 2,
                YbDbOpenDealProtocol.NoRechargeResult => -3,
                YbDbOpenDealProtocol.InsufficientYuanbaoResult => -1,
                YbDbOpenDealProtocol.AlreadyOpenedResult => -2,
                _ => 0
            };

            if (result.OpensDeal)
                m_boNativeYbDealOpened = true;

            if (m_NPC is NormNpc npc)
                ApplyNativeOpenYbDealNpcResult(npc, nativeCode);
        }

        internal void ApplyNativePrefreezeBillingSuccess(int billingGeneration)
        {
            _ = billingGeneration;
        }

        internal void ApplyNativePrefreezeBillingFailure(int billingGeneration)
        {
            _ = billingGeneration;
        }

        internal void ApplyNativeAccountUnfreezeSuccess()
        {
        }

        private void SendNativeYbNpcDialog(TBaseObject npc, string message)
        {
            if (npc == null) return;
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                npc.m_sCharName + '/' + message);
        }
    }
}
