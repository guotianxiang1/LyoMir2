using GameSvr.PasEngine;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal void WantNativeMagicTowerWarMon(NormNpc npc)
        {
            WantNativeMagicTowerWarMon(npc,
                NextNativeMagicTowerWarMonsterRandom,
                (environment, monsterName, x, y) =>
                    M2Share.UserEngine?.RegenNativeMagicTowerWarMonster(
                        environment, monsterName, x, y));
        }

        internal void WantNativeMagicTowerWarMon(NormNpc npc,
            Func<int, int> random,
            Action<Envirnoment, string, short, short> spawn)
        {
            random ??= _ => 0;
            spawn ??= (_, _, _, _) => { };

            if (m_btNativeMagicTowerPhase == 3)
            {
                m_btNativeMagicTowerPhase = 4;
                SplitNativeMagicTowerDescriptor(
                    m_sNativeMagicTowerChallengeMonsters ?? string.Empty,
                    '/', out var first, out var second);
                SpawnNativeMagicTowerWarMonsterGroup(first, npc, random,
                    spawn);
                SpawnNativeMagicTowerWarMonsterGroup(second, npc, random,
                    spawn);
            }

            if (npc != null)
                SendMsg(npc, Grobal2.RM_MERCHANTDLGCLOSE, 0, npc.ObjectId,
                    0, 0, string.Empty);
        }

        private void SpawnNativeMagicTowerWarMonsterGroup(string descriptor,
            NormNpc npc, Func<int, int> random,
            Action<Envirnoment, string, short, short> spawn)
        {
            SplitNativeMagicTowerDescriptor(descriptor, ':',
                out var monsterName, out var countText);
            if (string.IsNullOrEmpty(monsterName) || npc == null) return;

            if (!PasApiBridge.TryParseNativeDelphiInteger(countText,
                    out var count))
                count = 1;
            if (count <= 0) return;

            for (var index = 0; index < count; index++)
            {
                var offset = random(5);
                var x = unchecked((short)(npc.m_nCurrX + offset));
                var y = unchecked((short)(npc.m_nCurrY + offset));
                spawn(m_PEnvir, monsterName, x, y);
            }
        }

        private static void SplitNativeMagicTowerDescriptor(string value,
            char separator, out string left, out string right)
        {
            value ??= string.Empty;
            var position = value.IndexOf(separator);
            if (position < 0)
            {
                left = value;
                right = string.Empty;
                return;
            }

            left = value[..position];
            right = value[(position + 1)..];
        }

        private static int NextNativeMagicTowerWarMonsterRandom(int range)
        {
            return (M2Share.RandomNumber ?? RandomNumber.GetInstance())
                .Random(range);
        }
    }
}
