namespace GameSvr
{
    /// <summary>
    /// Prison cell event for magic 167 (画地为牢).
    /// Native handler @ 0x6EEE70, creates ET_PRISON (29) objects in a
    /// Chebyshev radius-3 ring (24 cells) around the caster.
    /// Each cell lasts 5000ms and blocks movement.
    /// </summary>
    public class PrisonEvent : Event
    {
        public PrisonEvent(Envirnoment envir, int nX, int nY, int nType, int nTime)
            : base(envir, nX, nY, nType, nTime, true)
        {
        }
    }
}
