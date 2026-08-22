using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("NpcHit", "让自身附近的可见的NPC做一个活动的动作", "", 4)]
    public sealed class NpcHitCommand : BaseCommond
    {
        [DefaultCommand]
        public void NpcHit(TPlayObject player)
        {
            var visibleActors = player?.m_VisibleActors;
            if (visibleActors == null)
            {
                return;
            }

            for (var i = 0; i < visibleActors.Count; i++)
            {
                var npc = visibleActors[i]?.BaseObject;
                if (npc == null || npc.m_btRaceServer != Grobal2.RC_NPC)
                {
                    continue;
                }

                npc.SendRefMsg(Grobal2.RM_HIT, npc.m_btDirection,
                    npc.m_nCurrX, npc.m_nCurrY, 0, string.Empty);
            }
        }
    }
}
