using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Not a native map-event class. Image scan of TSafeEvent / SafeEvent /
    /// SAFEEVENT in ASCII and UTF-16LE is 0 hits on flat_image.bin.
    /// Native safe-zone logic is InSafeZone / TSafeZoneArea / SafeZone.txt,
    /// not a TMapEvent subclass. Kept because a prior deletion broke compile
    /// when stale callers still named the type; currently the class has no
    /// live GameSvr consumers.
    /// </summary>
    public class SafeEvent : Event
    {
        public SafeEvent(Envirnoment Envir, int nX, int nY, int nType) : base(Envir, nX, nY, nType, HUtil32.GetTickCount(), true)
        {

        }

        public override void Run(int currentTick)
        {
            m_dwOpenStartTick = currentTick;
            base.Run(currentTick);
        }
    }
}
