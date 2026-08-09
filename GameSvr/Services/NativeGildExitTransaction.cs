namespace GameSvr
{
    // Dormant model of Gild exit op 4583 (a member leaving the Gild). Handler sub_6F6BF8 @0x006F6BF8
    // applies zone gates, then the role strategy sub_703418 @0x00703418 (gild roles [+0x44]) runs the
    // removal. SendDefMessage(4583, wParam=result, 0,0,0). Hex-Rays verified. WIRED (gated on
    // SupportsGildWrites): NativeCorpsService.ApplyGildExit builds this context from live state and,
    // on Success, removes the caller's corps + DELETE gamedata.gildmember fail-safe; with no Gild
    // store the live handler keeps the original fail-closed 1000.
    //
    //   handler gates: not allowed to leave (sub_76858C) 38; no gild membership (sub_6ADAE4) 12;
    //     in a fight zone 28; blocked by castle war 29; else -> strategy.
    //   strategy sub_703418: no player 5; no gild 12; not a valid member 18; remove failed 1000;
    //     else 0 (clear vice pointer if leaver was vice + DELETE GildMember + refresh).

    public sealed class NativeGildExitContext
    {
        public bool CanLeave { get; init; } = true;         // sub_76858C
        public bool HasGildMembership { get; init; }         // sub_6ADAE4
        public bool InFightZone { get; init; }               // byte[player[296]+0x94]
        public bool CastleWarBlocked { get; init; }          // global castle-war + free-pk/castle-area
        public bool HasPlayer { get; init; } = true;         // sub_5EC030
        public bool HasGild { get; init; }                   // player[4]
        public bool ValidMember { get; init; }               // sub_7063E8 >= 0
        public bool RemoveOk { get; init; }                  // sub_706464
    }

    public static class NativeGildExitTransaction
    {
        public const int Ident = 4583;
        public const int VtblStrategy = 0x44;
        public const int VtblSendDefMessage = 0x250;

        public const int NotAllowed = 38;
        public const int NoMembership = 12;
        public const int InFightZone = 28;
        public const int CastleWar = 29;
        public const int NoPlayer = 5;
        public const int NoGild = 12;
        public const int NotMember = 18;
        public const int WriteFailed = 1000;
        public const int Success = 0;

        public static int Evaluate(NativeGildExitContext c)
        {
            // handler sub_6F6BF8 zone gates
            if (!c.CanLeave) return NotAllowed;
            if (!c.HasGildMembership) return NoMembership;
            if (c.InFightZone) return InFightZone;
            if (c.CastleWarBlocked) return CastleWar;

            // strategy sub_703418
            if (!c.HasPlayer) return NoPlayer;
            if (!c.HasGild) return NoGild;
            if (!c.ValidMember) return NotMember;
            return c.RemoveOk ? Success : WriteFailed;
        }
    }
}
