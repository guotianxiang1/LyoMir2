namespace GameSvr
{
    /// <summary>
    /// Prison cell event for magic 167 (画地为牢).
    /// Native handler @ 0x6EEE70, creates TPrisonEvent objects of type
    /// <c>0x1D</c> (<see cref="SystemModule.Grobal2.ET_PRISON"/>) in a
    /// Chebyshev radius-3 ring (24 cells) around the caster.
    /// Each cell lasts 5000ms (<c>0x1388</c> at 0x6EEF13).
    /// </summary>
    public class PrisonEvent : Event
    {
        public PrisonEvent(Envirnoment envir, int nX, int nY, int nType, int nTime)
            : base(envir, nX, nY, nType, nTime, true)
        {
        }
    }
}
