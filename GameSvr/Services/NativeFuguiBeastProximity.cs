using System;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 富贵兽 proximity gate <c>sub_787918</c>：5 格内 race 0x96 且非 ghost 的怪。
    /// </summary>
    public static class NativeFuguiBeastProximity
    {
        public const uint ProximityEa = 0x00787918;
        public const byte FuguiRace = 0x96;
        public const int MaxChebyshevDistance = 5;
        private const string DenialMsg = "你周围并没有富贵兽"; // 0x787A34

        /// <summary>
        /// Returns true when a qualifying beast exists; otherwise sets denial message.
        /// </summary>
        public static bool TryFindNearbyBeast(TPlayObject player, int centerX,
            int centerY, out TBaseObject beast, out string denialMessage)
        {
            beast = null;
            denialMessage = null;
            if (player?.m_PEnvir == null)
            {
                denialMessage = DenialMsg;
                return false;
            }

            var objList = new System.Collections.Generic.List<TBaseObject>();
            if (!player.m_PEnvir.GetMapBaseObjects((short)centerX, (short)centerY,
                    MaxChebyshevDistance, objList))
            {
                denialMessage = DenialMsg;
                return false;
            }

            foreach (var target in objList)
            {
                if (target == null || target == player)
                    continue;
                if (target.m_boGhost || target.m_boDeath)
                    continue;
                // 0x787967 cmp [target+0x178],0x96
                if (target.m_btRaceServer != FuguiRace)
                    continue;
                if (Chebyshev(centerX, centerY, target.m_nCurrX, target.m_nCurrY) >
                    MaxChebyshevDistance)
                    continue;
                beast = target;
                return true;
            }

            denialMessage = DenialMsg;
            return false;
        }

        private static int Chebyshev(int x0, int y0, int x1, int y1) =>
            Math.Max(Math.Abs(x0 - x1), Math.Abs(y0 - y1));
    }
}
