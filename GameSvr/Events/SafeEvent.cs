using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Not a native map-event class. Image scan of TSafeEvent / SafeEvent /
    /// SAFEEVENT in ASCII and UTF-16LE is 0 hits on flat_image.bin, and the
    /// VMT sweep of flat_image (1410 self-pointers, delta 76) lists eleven
    /// direct children of TMapEvent — TCakeFireEvent, TDebuffTrapEvent,
    /// TFireBurnEvent, TFireDragonPoint, TFireworksEvent, THolyCurtainEvent,
    /// TMapScriptEvt, TOnceDamageTrapEvent, TPileStones, TPrisonEvent,
    /// TStallEvent — with no safe-zone class among them. Native safe-zone
    /// logic is InSafeZone / TSafeZoneArea / SafeZone.txt.
    /// <para>
    /// KEPT, and the "no live consumers" note that used to sit here was wrong.
    /// <c>git grep -n SafeEvent</c> with no pathspec finds a real caller:
    /// MapManager.cs:137 constructs one per start point and registers it with
    /// M2Share.EventManager. Under REPLICATION_RULES this is INVENTED and would
    /// normally be removed, but removing the type without first replacing that
    /// call site is exactly the deletion that broke the build once already.
    /// Removal has to start at MapManager, not here.
    /// </para>
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
