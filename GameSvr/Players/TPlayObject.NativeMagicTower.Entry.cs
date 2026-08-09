using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeMagicTowerLevelRequiredMessage =
            "你的等级不够，不能去魔王岭拦截怪物。";
        internal const string NativeMagicTowerNextLingFuRequiredMessage =
            "你至少需要1张灵符";

        internal byte m_btNativeMagicTowerNextGate;
        internal byte m_btNativeMagicTowerMysteryFlag;
        internal byte m_btNativeMagicTowerDefeatedMonsterCount;

        // Original +0x181E only participates in this level gate. Its producer
        // has not been identified, so keep an exact-width carrier here.
        internal ushort m_wNativeMagicTowerEntryLevelGate = 0;

        internal void ResetNativeMagicTowerArcherState()
        {
            lock (m_CreditCard.SyncRoot)
            {
                m_btNativeMagicTowerDefeatedMonsterCount = 0;
                m_sbNativeMagicTowerArcherCount = 0;
                m_btNativeMagicTowerEngageChance = 1;
                Array.Clear(m_btNativeMagicTowerArcherSlots, 0,
                    m_btNativeMagicTowerArcherSlots.Length);
            }
        }

        internal void EnterNativeMagicTowerNewGuan(NormNpc npc,
            NativeDynamicRoomService dynamicRooms)
        {
            if (m_btNativeMagicTowerPhase != 1) return;

            if (!MeetsNativeMagicTowerEntryLevel())
            {
                SendNativeMagicTowerEntryMessage(npc,
                    NativeMagicTowerLevelRequiredMessage);
                return;
            }

            if (dynamicRooms.FlyToDynamicRoom(this, "NewSky", 40, 40) < 0)
            {
                SendNativeMagicTowerEntryMessage(npc,
                    NativeMagicTowerRoomFullMessage);
                return;
            }

            m_btNativeMagicTowerPhase = 2;
            ClearNativeMagicTowerCrossbowTokens();
            m_btNativeMagicTowerRoomKind = 2;
            ResetNativeMagicTowerArcherState();
        }

        internal void EnterNativeMagicTowerNext(NormNpc npc, bool fromNext2,
            NativeDynamicRoomService dynamicRooms,
            NativeMagicTowerRouteSequencer sequencer)
        {
            if (!fromNext2) m_btNativeMagicTowerNextGate = 0;

            if (!MeetsNativeMagicTowerEntryLevel())
            {
                SendNativeMagicTowerEntryMessage(npc,
                    NativeMagicTowerLevelRequiredMessage);
                return;
            }

            if (GetNativeMagicTowerLingFuBalance() < 1)
            {
                SendNativeMagicTowerEntryMessage(npc,
                    NativeMagicTowerNextLingFuRequiredMessage);
                return;
            }

            if (dynamicRooms.FlyToDynamicRoom(this, "NewSky", 40, 40) < 0)
            {
                SendNativeMagicTowerEntryMessage(npc,
                    NativeMagicTowerRoomFullMessage);
                return;
            }

            DebitNativeMagicTowerLingFu(npc, 1,
                NativeMagicTowerLingFuLogReason);
            var entry = sequencer.Enter(false);
            m_btNativeMagicTowerSpecialRoute = entry.SpecialRoute;
            lock (m_CreditCard.SyncRoot)
            {
                m_boNativeMagicTowerHundredth = m_nUsedLingFu % 100 == 0;
                if (m_boNativeMagicTowerHundredth)
                    m_nUsedLingFu = unchecked(m_nUsedLingFu + 1);
            }
            RefreshNativeLingFu();
            ClearNativeMagicTowerCrossbowTokens();
            m_btNativeMagicTowerPhase = 2;
            m_btNativeMagicTowerRoomKind = 2;
            ResetNativeMagicTowerArcherState();
        }

        internal void EnterNativeMagicTowerNext2(NormNpc npc,
            NativeDynamicRoomService dynamicRooms,
            NativeMagicTowerRouteSequencer sequencer)
        {
            if (m_btNativeMagicTowerNextGate == 0) return;

            m_btNativeMagicTowerNextGate = 0;
            m_btNativeMagicTowerMysteryFlag = 1;
            EnterNativeMagicTowerNext(npc, true, dynamicRooms, sequencer);
        }

        private bool MeetsNativeMagicTowerEntryLevel()
        {
            return m_Abil.Level >= 25
                   || m_wNativeMagicTowerEntryLevelGate == 0;
        }

        private int GetNativeMagicTowerLingFuBalance()
        {
            var service = M2Share.CreditCardService ??
                          NativeCreditCardService.Disabled;
            lock (m_CreditCard.SyncRoot)
            {
                var creditBalance = service.Enabled
                    ? unchecked(m_CreditCard.Value + m_CreditCard.Value2)
                    : 0;
                return creditBalance > 0
                    ? unchecked(m_nLingFu + creditBalance)
                    : m_nLingFu;
            }
        }

        private void DebitNativeMagicTowerLingFu(NormNpc npc, int reason,
            string reasonText)
        {
            var service = M2Share.CreditCardService ??
                          NativeCreditCardService.Disabled;
            lock (m_CreditCard.SyncRoot)
            {
                var creditBalance = service.Enabled
                    ? unchecked(m_CreditCard.Value + m_CreditCard.Value2)
                    : 0;
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
                AddNativeLingFuReasonUsage(reason, 1);
                var npcDescription = (npc?.m_sCharName ?? string.Empty) +
                                     "-" +
                                     (npc?.m_sMapName ?? string.Empty);
                M2Share.AddGameDataLog(string.Join('\t', 0x65,
                    m_sMapName, m_nCurrX, m_nCurrY, m_sCharName,
                    reasonText, reason, 1, npcDescription));
            }
        }

        private void ClearNativeMagicTowerCrossbowTokens()
        {
            while (true)
            {
                var item = CheckItems("弩牌");
                if (item == null) return;
                SendDelItems(item);
                if (!DelBagItem(item.MakeIndex, "弩牌")) return;
            }
        }

        private void SendNativeMagicTowerEntryMessage(NormNpc npc,
            string message)
        {
            m_NPC = npc;
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc?.m_sCharName ?? string.Empty) + "/" + message);
        }
    }
}
