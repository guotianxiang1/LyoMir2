using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeMagicTowerLingFuRequiredMessage =
            "你给我的灵符在哪呢？要不你先去兑换一些？";
        internal const string NativeMagicTowerRoomFullMessage =
            "天关房间满员,请稍候再试...";
        internal const string NativeMagicTowerPendingArcherMessage =
            "你尚有一次召唤弓箭手的机会\\您可以选择“摆放弓箭手位置”进行摆放。";
        internal const string NativeMagicTowerArcherLimitMessage =
            "你已经拥有了10个弓箭手，不能继续。";
        internal const string NativeMagicTowerEngageLingFuMessage =
            "召唤弓箭手需要1张灵符";
        private const string NativeMagicTowerLingFuLogReason =
            "魔王岭消耗灵符";
        private const string NativeSkyGateLingFuLogReason =
            "闯天关消耗灵符";

        internal byte m_btNativeMagicTowerPhase;
        internal bool m_boNativeMagicTowerHundredth;
        internal byte m_btNativeMagicTowerSpecialRoute;
        internal byte m_btNativeMagicTowerRoomKind;
        internal byte m_btNativeMagicTowerEngageChance;
        internal sbyte m_sbNativeMagicTowerArcherCount;
        private readonly byte[] m_btNativeMagicTowerArcherSlots = new byte[10];

        internal bool HasNativeMagicTowerArcher(int index)
        {
            var slot = unchecked((uint)(index - 1));
            return slot < m_btNativeMagicTowerArcherSlots.Length
                   && m_btNativeMagicTowerArcherSlots[(int)slot] != 0;
        }

        internal bool GetNativeMagicTowerEngageChance(NormNpc npc)
        {
            if (npc == null || !npc.HasNativePasProperty(12)) return false;

            string failureMessage;
            lock (m_CreditCard.SyncRoot)
            {
                if (m_btNativeMagicTowerEngageChance != 0)
                {
                    failureMessage = NativeMagicTowerPendingArcherMessage;
                }
                else if (m_sbNativeMagicTowerArcherCount >= 10)
                {
                    failureMessage = NativeMagicTowerArcherLimitMessage;
                }
                else
                {
                    var service = M2Share.CreditCardService ??
                                  NativeCreditCardService.Disabled;
                    var creditBalance = 0;
                    if (service.Enabled && m_CreditCard.Loaded)
                    {
                        creditBalance = unchecked(m_CreditCard.Value +
                                                  m_CreditCard.Value2);
                        if (creditBalance <= 0) creditBalance = 0;
                    }

                    var balance = unchecked(m_nLingFu + creditBalance);
                    if (balance < 1)
                    {
                        failureMessage = NativeMagicTowerEngageLingFuMessage;
                    }
                    else
                    {
                        if (creditBalance > 0)
                        {
                            var remaining = 1;
                            if (remaining > m_CreditCard.Value2)
                            {
                                remaining = unchecked(remaining -
                                                      m_CreditCard.Value2);
                                m_CreditCard.Value2 = 0;
                            }
                            else
                            {
                                m_CreditCard.Value2 = unchecked(
                                    m_CreditCard.Value2 - remaining);
                                remaining = 0;
                            }

                            if (remaining > 0)
                            {
                                m_CreditCard.Value = unchecked(
                                    m_CreditCard.Value - remaining);
                                m_CreditCard.UsedValue = unchecked(
                                    m_CreditCard.UsedValue + remaining);
                            }
                            m_CreditCard.Dirty = true;
                            m_CreditCard.DirtyVersion++;
                        }
                        else
                        {
                            m_nLingFu = unchecked(m_nLingFu - 1);
                        }

                        m_nUsedLingFu = unchecked(m_nUsedLingFu + 1);
                        RefreshNativeLingFu();
                        AddNativeMagicTowerLingFuUsage();
                        var npcDescription = (npc.m_sCharName ?? string.Empty) +
                                             "-" +
                                             (npc.m_sMapName ?? string.Empty);
                        // 战神 sub_646F40 @0x646F89 `mov dl,1` — the reason argument passed
                        // into sub_6D23E8 is 1 (archer engage), not 0.  Inside sub_6D23E8 that
                        // same `ebx` drives three things: the session accumulator
                        // (@0x6D245C `inc [esi+edi*4+0x7C8]`), the persistent one
                        // (@0x6D2463 `inc [esi+edi*4+0xBE0]`, both indexed by `ebx & 0x7F`
                        // @0x6D2459), and the 4-way log-text ladder at @0x6D246C.  The two
                        // accumulators here already use 1 (AddNativeMagicTowerLingFuUsage ->
                        // AddNativeLingFuReasonUsage(1, 1)); only this log field was left at 0.
                        M2Share.AddGameDataLog(string.Join('\t', 0x65,
                            m_sMapName, m_nCurrX, m_nCurrY, m_sCharName,
                            NativeMagicTowerLingFuLogReason, 1, 1,
                            npcDescription));
                        m_btNativeMagicTowerEngageChance = 1;
                        return true;
                    }
                }
            }

            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc.m_sCharName ?? string.Empty) + "/" + failureMessage);
            return false;
        }

        internal void EnterNativeMagicTowerRoute(NormNpc npc,
            NativeMagicTowerRouteSequencer sequencer)
        {
            EnterNativeMagicTowerRouteEx(npc, sequencer, false);
        }

        internal void EnterNativeMagicTowerRouteEx(NormNpc npc,
            NativeMagicTowerRouteSequencer sequencer, bool freeEntry)
        {
            if (!freeEntry
                && (!TryGetNativeLingFuBalance(out var balance) || balance <= 0))
            {
                m_NPC = npc;
                SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                    (npc.m_sCharName ?? string.Empty) + "/" +
                    NativeMagicTowerLingFuRequiredMessage);
                return;
            }

            NativeMagicTowerRouteEntry entry;
            if (freeEntry)
            {
                entry = sequencer.ResolveCurrent();
            }
            else
            {
                DebitNativeMagicTowerLingFu(npc, 0,
                    NativeSkyGateLingFuLogReason);
                entry = sequencer.Enter(false);
            }

            m_btNativeMagicTowerPhase = 1;
            m_btNativeMagicTowerSpecialRoute = entry.SpecialRoute;
            lock (m_CreditCard.SyncRoot)
            {
                m_boNativeMagicTowerHundredth = m_nUsedLingFu % 100 == 0;
                if (m_boNativeMagicTowerHundredth)
                    m_nUsedLingFu = unchecked(m_nUsedLingFu + 1);
            }

            SpaceMove("D5071~0", 11, 13, 0);
        }

        internal void EnterNativeMagicTowerGuan(NormNpc npc,
            NativeDynamicRoomService dynamicRooms)
        {
            if (m_btNativeMagicTowerPhase != 1) return;

            if (dynamicRooms.FlyToDynamicRoom(this, "Sky", 28, 20) < 0)
            {
                m_NPC = npc;
                SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                    (npc.m_sCharName ?? string.Empty) + "/" +
                    NativeMagicTowerRoomFullMessage);
                return;
            }

            m_btNativeMagicTowerPhase = 2;
            m_btNativeMagicTowerRoomKind = 1;
        }
    }
}
