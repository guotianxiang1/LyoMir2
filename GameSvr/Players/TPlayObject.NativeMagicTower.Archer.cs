using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const byte NativeMagicTowerArcherRace = 99;
        internal const string NativeMagicTowerArcherName =
            "魔王岭弓箭手";
        internal const string NativeMagicTowerArcherOccupiedMessage =
            "该位置已有弓箭手，请重新选择。";
        internal const string NativeMagicTowerArcherReadyMessage =
            "您召唤的弓箭手已经就位";

        internal void EngageNativeMagicTowerArcher(NormNpc npc, int index)
        {
            if (npc == null || !npc.HasNativePasProperty(12)) return;

            lock (m_CreditCard.SyncRoot)
            {
                var slot = unchecked((uint)(index - 1));
                if (slot >= m_btNativeMagicTowerArcherSlots.Length ||
                    m_btNativeMagicTowerEngageChance == 0)
                {
                    CloseNativeMagicTowerArcherDialog(npc);
                    return;
                }

                if (m_sbNativeMagicTowerArcherCount >= 10)
                {
                    SendNativeMagicTowerArcherDialog(npc,
                        NativeMagicTowerArcherLimitMessage);
                    return;
                }

                if (m_btNativeMagicTowerArcherSlots[(int)slot] != 0)
                {
                    SendNativeMagicTowerArcherDialog(npc,
                        NativeMagicTowerArcherOccupiedMessage);
                    return;
                }

                if (!TryGetNativeMagicTowerArcherCoordinates(index,
                        out var x, out var y))
                    return;

                var archer = M2Share.UserEngine?
                    .RegenNativeMagicTowerArcher(m_PEnvir, x, y);
                if (archer == null) return;

                archer.m_Master = this;
                m_btNativeMagicTowerEngageChance = 0;
                m_btNativeMagicTowerArcherSlots[(int)slot] = 1;
                m_sbNativeMagicTowerArcherCount = unchecked(
                    (sbyte)(m_sbNativeMagicTowerArcherCount + 1));

                if (m_btNativeMagicTowerPhase == 2)
                    npc.StartNativeMagicTowerChallenge(this);

                CloseNativeMagicTowerArcherDialog(npc);
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0,
                    NativeMagicTowerArcherReadyMessage);
            }
        }

        // sub_646BAC @0x00646BAC EngageArcher — coords from table @0x007D2A8C
        internal static bool TryGetNativeMagicTowerArcherCoordinates(
            int index, out short x, out short y) =>
            NativeCastleHostRuntime.TryGetEngageArcherCoordinates(index, out x, out y);

        private void SendNativeMagicTowerArcherDialog(NormNpc npc,
            string message)
        {
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc.m_sCharName ?? string.Empty) + "/" + message);
        }

        private void CloseNativeMagicTowerArcherDialog(NormNpc npc)
        {
            SendMsg(npc, Grobal2.RM_MERCHANTDLGCLOSE, 0, npc.ObjectId,
                0, 0, string.Empty);
        }
    }
}
