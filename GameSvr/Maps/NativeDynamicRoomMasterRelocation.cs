namespace GameSvr
{
    public static class NativeDynamicRoomMasterRelocation
    {
        public static bool TryRelocate(TBaseObject actor)
        {
            if (actor == null || actor.m_boDeath || actor.m_boGhost)
                return false;

            var master = actor.m_Master;
            if (master == null || master.m_boDeath || master.m_boGhost)
                return false;

            var targetEnvironment = master.m_PEnvir;
            if (targetEnvironment == null) return false;

            short targetX = master.m_nCurrX;
            short targetY = master.m_nCurrY;
            master.GetFrontPosition(ref targetX, ref targetY);

            if (!ReferenceEquals(actor.m_Master, master)
                || !ReferenceEquals(master.m_PEnvir, targetEnvironment)
                || actor.m_boDeath || actor.m_boGhost
                || master.m_boDeath || master.m_boGhost)
                return false;

            // Native sub_766214 yields the master's front cell. Blocked-cell
            // fallback stays with the existing transaction; no claim is made
            // that its search order is identical to the native implementation.
            return actor.TrySpaceMoveToEnvironment(targetEnvironment,
                targetX, targetY, 1);
        }
    }
}
