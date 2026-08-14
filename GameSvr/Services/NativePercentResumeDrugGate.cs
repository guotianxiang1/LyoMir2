using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// <c>TPercentResumeDrug</c> 使用前 gate = 战神 <c>sub_78B38C</c> (VMT 0x77E4D0
    /// slot +0x28) + 冷却/配额刷新 <c>sub_747E80</c>。
    /// </summary>
    public static class NativePercentResumeDrugGate
    {
        public const uint GateEa = 0x0078B38C;
        public const uint CooldownEa = 0x00747E80;
        public const int CooldownMs = 0x493E0; // 300000 ms @0x747E9B

        private const string FrozenMsg =
            "你被冻结,无法使用"; // 0x78B460
        private const string CooldownFmt =
            "%s现在还无法使用"; // 0x78B47C — native Format with item name

        /// <summary>
        /// Mirrors 0x78B38C..0x78B452. Returns true when use may proceed.
        /// </summary>
        public static bool TryAllowUse(TPlayObject player, GoodItem stdItem,
            out string denialMessage, out byte denialColorF, out byte denialColorB)
        {
            denialMessage = null;
            denialColorF = 0xFF;
            denialColorB = 0x38;

            if (player == null || stdItem == null)
                return false;

            // 0x78B3AC cmp [player+0x2AC],0 — current HP must be non-zero.
            if (player.m_WAbil.HP <= 0)
                return false;

            // 0x78B3BB B2 3E / call 0x772960 — body state 0x3E (frozen).
            if (player.HasNativeActiveState(0x3E))
            {
                denialMessage = FrozenMsg;
                denialColorF = 0xFF;
                denialColorB = 0x38; // cx=0x38FF @0x78B3C8
                return false;
            }

            // 0x78B3EE call 0x747E80(player, std+0x24, std+0x2C).
            if (!TryRefreshCooldown(player, stdItem))
            {
                denialMessage = string.Format(CooldownFmt.Replace("%s", "{0}"),
                    stdItem.Name);
                denialColorF = 0xFF;
                denialColorB = 0xDB; // cx=0xFFDB @0x78B419
                return false;
            }

            return true;
        }

        /// <summary>
        /// <c>sub_747E80</c>: if (now - [player+0x454]) &lt; 0x493E0 return false;
        /// else stamp tick, scale std Ac/Mac by MaxHP/MaxMP percent, call IncHealthSpell.
        /// </summary>
        internal static bool TryRefreshCooldown(TPlayObject player, GoodItem stdItem)
        {
            var now = HUtil32.GetTickCount();
            if (unchecked((uint)(now - player.m_dwNativePercentResumeDrugTick)) <
                CooldownMs)
                return false;

            player.m_dwNativePercentResumeDrugTick = now;

            // 0x747EAD..0x747ED4: deltaHP = MaxHP * word[std+0x24] / 100,
            // deltaMP = MaxMP * word[std+0x2C] / 100, then sub_769DB4.
            var deltaHp = player.m_WAbil.MaxHP * stdItem.Ac / 100;
            var deltaMp = player.m_WAbil.MaxMP * stdItem.Mac / 100;
            player.IncHealthSpell(deltaHp, deltaMp);
            return true;
        }
    }
}
