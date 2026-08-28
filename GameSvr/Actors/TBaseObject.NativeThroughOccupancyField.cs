namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// Native <c>obj+0x3FE</c> — the pass-through-occupancy decision cache.
        ///
        /// Declared here rather than on TPlayObject because 0x3FE lands in
        /// TCreature's own band <c>[0x00C, 0x450)</c>, and native confirms the
        /// sharing directly: <c>sub_68BEC0</c> reads <c>byte [Self+0x3FE]</c> at
        /// <c>0x68BEEF</c> with a <b>hero</b> as Self, on the way to
        /// <c>TEnvironment.CanWalk</c>. A player-only field could not be read there.
        ///
        /// Ownership of the WRITE is unchanged and stays a player concern: the whole
        /// image has 28 accesses and exactly one writer, the player tick
        /// <c>sub_6B2D38</c> at <c>0x6B30A3</c>, which only writes when the recomputed
        /// value differs and then broadcasts SM_2821. That path is reproduced by
        /// <c>NativeTickThroughOccupancyTransition()</c>; every mover, hero included,
        /// only ever reads this field (MOVE-73).
        /// </summary>
        public bool m_boThroughOccupancyCache;
    }
}
