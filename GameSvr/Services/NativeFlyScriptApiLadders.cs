namespace GameSvr
{
    // Dormant, fail-closed models of the PAS-script ("TPsNpc" procedure)
    // "Fly / teleport" family native handlers of the 战神 M2Server binary
    // (M2Server_unpacked_fixed.exe, image base 0x00400000,
    //  SHA256 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e).
    //
    // Runtime callables are bound name->body by the registrar sub_731350
    // (`mov edx, offset <body>; mov ecx, offset <nameString>; call sub_4F4180`),
    // mirrored by the secondary registrar sub_109DF50 at 0x0109xxxx. The PAS
    // *compiler* signatures are declared separately by sub_72A930 (and its
    // mirror sub_1097530) via sub_510F00 — a signature declaration alone does
    // NOT bind an executable body.
    //
    // These wrappers are currently either RejectUnsupportedNativeApi() or loose
    // approximations in GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs. Peer
    // dynroom (NativeEventClusterScriptLadders.cs) already modeled
    // GroupFlyToDynRoomInRange / AddGuanMoPoint / RandomFlyTo / CoupleFly /
    // DoRelive; those are NOT repeated here.
    //
    // Every model captures only the PURE decision ladder of a wrapper. Each
    // polymorphic side-effect executor — the precise mover sub_6BE4D0, the
    // fallback/random mover sub_768C7C, the group-eligibility predicate
    // sub_6B7BAC, the group-to-map movers sub_7275C4 / sub_727A74, the shared
    // group range-mover sub_727678, the map-envir resolver sub_696228 and the
    // group-to-dynroom mover sub_727884 — is abstracted as an INPUT, never
    // synthesised here. Nothing in this file mutates game state; it is a
    // reference oracle for a later wiring pass and for AuditTools.

    // =====================================================================
    // 1. Flyto  (wrapper sub_6DEF8C, declared at 0x0072B07A;
    //            registered name->body at 0x00731491)
    //    procedure Flyto(const MapName: string; x: Word; y: Word);
    //    Currently: PasApiBridge case "flyto" => SpaceMove(map, x, y, 0)
    //               unconditionally (an approximation).
    // =====================================================================

    /// <summary>Outcome of the sub_6DEF8C decision ladder.</summary>
    public enum NativeFlytoOutcome
    {
        /// <summary>Both coordinates are non-zero in their low 16 bits
        /// (native `test si,si` / `test di,di` at 0x006DEFB0-B8 both fall
        /// through): move to the exact tile via sub_6BE4D0. x and y are first
        /// passed through the Word-conversion helper sub_40C89C.</summary>
        PreciseMove,

        /// <summary>x == 0 OR y == 0 (either `jz short loc_6DEFE3` taken):
        /// fall back to the map mover sub_768C7C invoked with ecx = 1 and one
        /// pushed argument = 1 (the same executor CoupleFly's sub_6CEF14 uses
        /// for a self map-move, here with the 1/1 arguments rather than
        /// 0/0).</summary>
        FallbackMove
    }

    /// <summary>Dormant model of Flyto (sub_6DEF8C). The two movers
    /// (sub_6BE4D0 precise, sub_768C7C fallback) and the coordinate helper
    /// sub_40C89C are abstracted inputs; only the branch is modeled.</summary>
    public static class NativeFlytoPlanner
    {
        public const int WrapperAddress = 0x006DEF8C;
        public const int RegisterSiteAddress = 0x00731491;   // sub_731350
        public const int PreciseMoverAddress = 0x006BE4D0;   // sub_6BE4D0
        public const int FallbackMoverAddress = 0x00768C7C;  // sub_768C7C
        /// <summary>The placeholder coordinate the native passes to the fallback
        /// mover sub_768C7C(self, map, ecx=1, push 1) on the x==0||y==0 branch.
        /// The live PasApiBridge "flyto" mirrors this as SpaceMove(map, 1, 1).</summary>
        public const int FallbackPlaceholderCoord = 1;
        public const int CoordHelperAddress = 0x0040C89C;    // sub_40C89C

        /// <summary>The native tests are 16-bit (`test si,si` / `test di,di`);
        /// x and y are declared as Word. Only the low 16 bits participate.</summary>
        public static NativeFlytoOutcome Plan(int x, int y)
        {
            bool xNonZero = (x & 0xFFFF) != 0;
            bool yNonZero = (y & 0xFFFF) != 0;
            if (xNonZero && yNonZero)
                return NativeFlytoOutcome.PreciseMove;
            return NativeFlytoOutcome.FallbackMove;
        }
    }

    // =====================================================================
    // 2. GroupFly  (wrapper sub_6E0678, declared at 0x0072B296;
    //               registered name->body at 0x0073177D)
    //    procedure GroupFly(const sTargetMap : string);
    //    Currently: PasApiBridge case "groupfly" => MoveCurrentGroupToMap()
    //               unconditionally (an approximation; no eligibility gate).
    // =====================================================================

    /// <summary>Outcome of the sub_6E0678 decision ladder.</summary>
    public enum NativeGroupFlyOutcome
    {
        /// <summary>The native group-fly eligibility predicate sub_6B7BAC(self)
        /// returned false (native `test al,al` / `jz short loc_6E0699` at
        /// 0x006E0688-8A): silent no-op — the group is NOT moved.</summary>
        NotEligible,

        /// <summary>Predicate true: move the whole group ([self+0x0A80]) to
        /// sTargetMap via sub_7275C4.</summary>
        MoveGroup
    }

    /// <summary>Dormant model of GroupFly (sub_6E0678). The eligibility
    /// predicate sub_6B7BAC and the group-to-map mover sub_7275C4 are
    /// abstracted inputs.</summary>
    public static class NativeGroupFlyPlanner
    {
        public const int WrapperAddress = 0x006E0678;
        public const int RegisterSiteAddress = 0x0073177D;    // sub_731350
        public const int EligibilityPredicateAddress = 0x006B7BAC; // sub_6B7BAC
        public const int GroupMapMoverAddress = 0x007275C4;   // sub_7275C4

        /// <param name="eligible">sub_6B7BAC(self) result.</param>
        public static NativeGroupFlyOutcome Plan(bool eligible)
        {
            if (!eligible)
                return NativeGroupFlyOutcome.NotEligible;
            return NativeGroupFlyOutcome.MoveGroup;
        }
    }

    // =====================================================================
    // 3. GroupFlyEx  (wrapper sub_6E06A0, declared at 0x0072B2A2;
    //                 registered name->body at 0x0073178E)
    //    function GroupFlyEx(const sTargetMap : string): word;
    //    Currently: PasApiBridge case "groupflyex" => MoveCurrentGroupToMap()
    //               then result = CountCurrentGroupOnMap() unconditionally.
    // =====================================================================

    /// <summary>Outcome of the sub_6E06A0 decision ladder.</summary>
    public enum NativeGroupFlyExOutcome
    {
        /// <summary>Eligibility predicate sub_6B7BAC(self) false (native
        /// `jz short loc_6E06D0` at 0x006E06B2): no move; the function result
        /// is forced to 0 (native `xor eax,eax` at 0x006E06D0).</summary>
        NotEligible,

        /// <summary>Predicate true: move the group via sub_7275C4, then the
        /// function result is the word returned by sub_727A74([self+0x0A80],
        /// sTargetMap) — the moved/present count reporter.</summary>
        MoveGroupAndReport
    }

    /// <summary>Dormant model of GroupFlyEx (sub_6E06A0). Same eligibility gate
    /// as GroupFly; additionally returns the sub_727A74 word. The movers and
    /// the reporter are abstracted inputs.</summary>
    public static class NativeGroupFlyExPlanner
    {
        public const int WrapperAddress = 0x006E06A0;
        public const int RegisterSiteAddress = 0x0073178E;    // sub_731350
        public const int EligibilityPredicateAddress = 0x006B7BAC; // sub_6B7BAC
        public const int GroupMapMoverAddress = 0x007275C4;   // sub_7275C4
        public const int GroupReportAddress = 0x00727A74;     // sub_727A74

        public static NativeGroupFlyExOutcome Plan(bool eligible)
        {
            if (!eligible)
                return NativeGroupFlyExOutcome.NotEligible;
            return NativeGroupFlyExOutcome.MoveGroupAndReport;
        }

        /// <summary>The word the native function actually returns.</summary>
        /// <param name="eligible">sub_6B7BAC(self) result.</param>
        /// <param name="reportValue">sub_727A74 return value (only meaningful
        /// when <paramref name="eligible"/> is true; abstracted input).</param>
        public static int ResolveResult(bool eligible, int reportValue)
        {
            if (!eligible)
                return 0;
            return reportValue & 0xFFFF;   // declared result type is word
        }
    }

    // =====================================================================
    // 4. GroupFlyInRange  (wrapper sub_6E07B4, declared at 0x0072B6DA;
    //                      registered name->body at 0x00731D11;
    //                      shared executor sub_727678)
    //    procedure GroupFlyInRange(mapName: string; x, y, iRange: Integer);
    //    Currently: PasApiBridge case "groupflyinrange" iterates
    //               m_GroupMembers and SpaceMoves each with
    //               Random(r*2+1) (an approximation — see divergences below).
    // =====================================================================

    /// <summary>Outcome of the sub_6E07B4 decision ladder.</summary>
    public enum NativeGroupFlyInRangeOutcome
    {
        /// <summary>Caller has no group object ([self+0x0A80] == 0): silent
        /// no-op (native `cmp dword ptr [ebx+0A80h],0` / `jz short loc_6E080A`
        /// at 0x006E07D7-DE).</summary>
        NoGroup,

        /// <summary>Group exists but sub_696228(mapMgr, mapName) resolved no
        /// environment — the static map name has no live envir (native
        /// `test eax,eax` / `jz short loc_6E080A` at 0x006E07EF-F1). mapMgr is
        /// ds:off_7D660C (the map/envir manager, NOT the dynamic-room manager
        /// off_7D6728 used by GroupFlyToDynRoomInRange).</summary>
        MapNotFound,

        /// <summary>Both gates pass: dispatch the shared group range-mover
        /// sub_727678 against the resolved environment.</summary>
        DispatchRangeMove
    }

    /// <summary>
    /// Resolved dispatch descriptor for GroupFlyInRange -&gt; sub_727678.
    ///
    /// CERTAIN (from disassembly of sub_727678, 0x00727678-0x0072773B):
    /// for the leader ([group+0x3C]) and every non-null, non-leader member
    /// slot (indices 0..10 at [group+0x48+idx*4], member at slot+0x10) the
    /// executor calls the creature move method vtable[0x1C0](Envir, X, Y, 1, 0)
    /// with
    ///   X = centerA - radius + Random(2*radius)   (centerA = sub_727678 ECX,
    ///                                               X = the vtable ECX arg)
    ///   Y = centerB - radius + Random(2*radius)   (centerB = sub_727678 arg_4,
    ///                                               Y = the first pushed arg)
    ///   radius = sub_727678 arg_0
    /// (`add esi,esi` then Random via sub_403B4C makes arg_0 both the ±offset
    /// and half the random span, i.e. definitionally the radius.)
    ///
    /// CERTAIN (caller sub_6E07B4 wiring, 0x006E07F3-0805): sub_727678 receives
    /// ECX = the wrapper's incoming ECX register, arg_4 (first pushed) = the
    /// wrapper's stack arg_4, arg_0 (last pushed) = the wrapper's stack arg_0.
    ///
    /// RESOLVED (Delphi register/pascal ABI: the first three params go in
    /// EAX/EDX/ECX, remaining params are pushed LEFT-TO-RIGHT so the leftmost
    /// stack param sits at the highest [ebp] offset). For
    /// GroupFlyInRange(mapName, x, y, iRange): EDX=mapName, ECX=x, and the two
    /// stack extras push as y then iRange, giving arg_4 = y and arg_0 = iRange.
    /// Therefore sub_727678 sees centerA(ECX)=x, centerB(arg_4)=y,
    /// radius(arg_0)=iRange — i.e. the intuitive mapping centerX=x, centerY=y,
    /// radius=iRange.
    ///
    /// This resolves the ambiguity peer dynroom flagged on the shared executor.
    /// The sibling GroupFlyToDynRoomInRange (sub_6E0734) wires the SAME executor
    /// with EDX=roomName, ECX=roomIdx, stack extras x,y,iRange -&gt; arg_8=x,
    /// arg_4=y, arg_0=iRange, and forwards ECX&lt;-arg_8(x), so it too yields
    /// centerX=x, centerY=y, radius=iRange. Both wrappers agree under the
    /// left-to-right convention; the alternative (right-to-left) would make the
    /// authored script argument x behave as the radius and iRange as the X
    /// center, which is not a sane content-authoring contract. The intuitive
    /// mapping is adopted with high confidence; the raw register/stack SOURCES
    /// are retained below so a wiring pass can re-audit independently.
    /// </summary>
    public readonly struct NativeGroupFlyInRangeDispatch
    {
        /// <summary>Declared param routed to sub_727678 ECX (centerA, MoveTo X).</summary>
        public const string EcxSource = "x (centerX)";
        /// <summary>Declared param pushed FIRST -> sub_727678 arg_4 (centerB, MoveTo Y).</summary>
        public const string FirstPushedSource = "y (centerY)";
        /// <summary>Declared param pushed LAST -> sub_727678 arg_0 (radius).</summary>
        public const string LastPushedSource = "iRange (radius)";

        /// <summary>Number of fixed member slots scanned at [group+0x48+idx*4]
        /// (native `cmp esi,0Bh` at 0x0072772D).</summary>
        public const int MemberSlotCount = 11;

        public int CenterX { get; init; }
        public int CenterY { get; init; }
        public int Radius { get; init; }

        /// <summary>Lower bound of the per-axis random target (inclusive):
        /// center - radius (native `sub edx,[arg_0]`).</summary>
        public int AxisLowerBound(int center) => center - Radius;

        /// <summary>Exclusive upper bound of the per-axis random span:
        /// center - radius + 2*radius = center + radius. The random draw is
        /// Random(2*radius) (native `add esi,esi` before sub_403B4C), so the
        /// realized target is in [center-radius, center+radius) — the upper
        /// edge center+radius is NOT reachable.</summary>
        public int AxisRandomSpan() => 2 * Radius;
    }

    /// <summary>Dormant model of GroupFlyInRange (sub_6E07B4). The map-envir
    /// resolver sub_696228 and the shared range-mover sub_727678 are abstracted
    /// inputs; only the gate ladder and the resolved dispatch mapping are
    /// modeled.</summary>
    public static class NativeGroupFlyInRangePlanner
    {
        public const int WrapperAddress = 0x006E07B4;
        public const int RegisterSiteAddress = 0x00731D11;    // sub_731350
        public const int MapResolverAddress = 0x00696228;     // sub_696228
        public const int RangeMoverAddress = 0x00727678;      // sub_727678

        /// <param name="hasGroup">[self+0x0A80] != 0.</param>
        /// <param name="mapResolvesToEnv">sub_696228(mapMgr, mapName) != null.</param>
        public static NativeGroupFlyInRangeOutcome Plan(
            bool hasGroup, bool mapResolvesToEnv)
        {
            if (!hasGroup)
                return NativeGroupFlyInRangeOutcome.NoGroup;
            if (!mapResolvesToEnv)
                return NativeGroupFlyInRangeOutcome.MapNotFound;
            return NativeGroupFlyInRangeOutcome.DispatchRangeMove;
        }

        /// <summary>Builds the resolved executor dispatch for the
        /// DispatchRangeMove case. Does NOT perform the move.</summary>
        public static NativeGroupFlyInRangeDispatch BuildDispatch(
            int x, int y, int iRange)
        {
            return new NativeGroupFlyInRangeDispatch
            {
                CenterX = x,        // ECX  (centerA -> MoveTo X)
                CenterY = y,        // arg_4 (first pushed, centerB -> MoveTo Y)
                Radius = iRange     // arg_0 (last pushed, radius)
            };
        }
    }

    // =====================================================================
    // 5. GroupFlyToDynRoom  (wrapper sub_6E06D8, declared at 0x0072B34A;
    //                        registered name->body at 0x0073187C)
    //    procedure GroupFlyToDynRoom(roomName: string; roomIdx: Integer);
    //    Currently: PasApiBridge case "groupflytodynroom" =>
    //               DynamicRoomService.GroupFlyToDynamicRoom() (dynroom-domain
    //               approximation). Modeled here only at the wrapper-gate level;
    //               the executor sub_727884 is owned by the dynroom domain.
    // =====================================================================

    /// <summary>Outcome of the sub_6E06D8 decision ladder.</summary>
    public enum NativeGroupFlyToDynRoomOutcome
    {
        /// <summary>Caller has no group object ([self+0x0A80] == 0): silent
        /// no-op (native `test eax,eax` / `jz short loc_6E070F` at
        /// 0x006E0701-03). Unlike GroupFlyToDynRoomInRange this wrapper does
        /// NOT resolve/validate the room name before dispatch — the room
        /// resolution happens inside the executor sub_727884.</summary>
        NoGroup,

        /// <summary>Group exists: dispatch the whole-group-to-dynroom mover
        /// sub_727884([self+0x0A80], roomName, roomIdx). There is no range
        /// jitter (contrast sub_727678).</summary>
        DispatchGroupToRoom
    }

    /// <summary>Dormant model of GroupFlyToDynRoom (sub_6E06D8). The dynroom
    /// group mover sub_727884 is an abstracted input owned by the dynamic-room
    /// domain.</summary>
    public static class NativeGroupFlyToDynRoomPlanner
    {
        public const int WrapperAddress = 0x006E06D8;
        public const int RegisterSiteAddress = 0x0073187C;    // sub_731350
        public const int GroupRoomMoverAddress = 0x00727884;  // sub_727884

        /// <param name="hasGroup">[self+0x0A80] != 0.</param>
        public static NativeGroupFlyToDynRoomOutcome Plan(bool hasGroup)
        {
            if (!hasGroup)
                return NativeGroupFlyToDynRoomOutcome.NoGroup;
            return NativeGroupFlyToDynRoomOutcome.DispatchGroupToRoom;
        }
    }

    // =====================================================================
    // 6. Declared-only PAS APIs (NO runtime body bound)
    //    FlyToObserverMap / FlyToWeSpot / SouthWildStartConvoy /
    //    SouthWildStartMonAttack.
    //
    //    dynroom flagged these four as "declared-but-unlocated". They are now
    //    LOCATED as declarations only: the sole cross-reference of each
    //    signature string is the PAS *compiler* type-registrar sub_72A930
    //    (`mov edx, offset <signature>; mov eax, ebx; call sub_510F00`),
    //    mirrored by sub_1097530 (call sub_E7DB00). There is:
    //      * NO name->body registration in sub_731350 / sub_109DF50,
    //      * NO bare callable-name string (e.g. "SouthWildStartConvoy") in the
    //        image for such a registration to load into ECX, and
    //      * NO named function (get_name_ea_simple == BADADDR) and no body
    //        function pointer near any signature reference.
    //    => These procedures compile in scripts but bind to no executable body;
    //    invoking them is a no-op at the native layer. PasApiBridge answering
    //    them with RejectUnsupportedNativeApi() is therefore FAITHFUL, not a
    //    coverage gap. This mirrors the Deny*Logon family (declared/absent).
    // =====================================================================

    /// <summary>Registration state of a PAS native API name.</summary>
    public enum NativePasApiBinding
    {
        /// <summary>Signature declared to the compiler AND a runtime body bound
        /// (name->body via sub_731350/sub_4F4180).</summary>
        DeclaredAndBound,

        /// <summary>Signature declared to the compiler (sub_72A930/sub_510F00)
        /// but NO runtime body bound. Faithful behavior: unsupported / no-op.</summary>
        DeclaredOnly
    }

    /// <summary>The declared-only signature record for one flagged handler.</summary>
    public readonly struct NativeDeclaredOnlyPasRecord
    {
        public string Name { get; init; }
        public string Signature { get; init; }
        /// <summary>Primary signature-string address (compiler registrar site
        /// sub_72A930).</summary>
        public int SignatureAddressPrimary { get; init; }
        /// <summary>Mirror signature-string address (sub_1097530 at 0x0109xxxx).</summary>
        public int SignatureAddressMirror { get; init; }
        /// <summary>Compiler-registrar `mov edx, offset <sig>` site (sub_72A930).</summary>
        public int DeclarationSite { get; init; }
    }

    /// <summary>Dormant registry proving the four dynroom-flagged handlers are
    /// declared-only (no bound body). Use to assert PasApiBridge's reject is the
    /// faithful outcome.</summary>
    public static class NativeDeclaredOnlyPasApi
    {
        public const int CompilerRegistrarAddress = 0x0072A930;  // sub_72A930
        public const int CompilerDeclareCallee = 0x00510F00;     // sub_510F00
        public const int PrimaryBodyRegistrarAddress = 0x00731350;   // sub_731350
        public const int SecondaryBodyRegistrarAddress = 0x0109DF50; // sub_109DF50

        public static readonly NativeDeclaredOnlyPasRecord FlyToObserverMap =
            new()
            {
                Name = "FlyToObserverMap",
                Signature = "procedure FlyToObserverMap();",
                SignatureAddressPrimary = 0x0072DA90,
                SignatureAddressMirror = 0x0109A690,
                DeclarationSite = 0x0072B37A
            };

        public static readonly NativeDeclaredOnlyPasRecord FlyToWeSpot =
            new()
            {
                Name = "FlyToWeSpot",
                Signature = "procedure FlyToWeSpot;",
                SignatureAddressPrimary = 0x007307E4,
                SignatureAddressMirror = 0x0109D3E4,
                DeclarationSite = 0x0072BB79
            };

        public static readonly NativeDeclaredOnlyPasRecord SouthWildStartConvoy =
            new()
            {
                Name = "SouthWildStartConvoy",
                Signature = "procedure SouthWildStartConvoy(guildName: string);",
                SignatureAddressPrimary = 0x0072BF8C,
                SignatureAddressMirror = 0x01098B8C,
                DeclarationSite = 0x0072A9D3
            };

        public static readonly NativeDeclaredOnlyPasRecord SouthWildStartMonAttack =
            new()
            {
                Name = "SouthWildStartMonAttack",
                Signature = "procedure SouthWildStartMonAttack;",
                SignatureAddressPrimary = 0x0072BFC8,
                SignatureAddressMirror = 0x01098BC8,
                DeclarationSite = 0x0072A9DF
            };

        private static readonly NativeDeclaredOnlyPasRecord[] All =
        {
            FlyToObserverMap, FlyToWeSpot,
            SouthWildStartConvoy, SouthWildStartMonAttack
        };

        /// <summary>Case-insensitive lookup by callable name.</summary>
        public static bool TryGet(string name, out NativeDeclaredOnlyPasRecord record)
        {
            foreach (var r in All)
            {
                if (string.Equals(r.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    record = r;
                    return true;
                }
            }
            record = default;
            return false;
        }

        /// <summary>Binding state of a name. Returns <see cref="NativePasApiBinding.DeclaredOnly"/>
        /// for the four flagged handlers, <see cref="NativePasApiBinding.DeclaredAndBound"/>
        /// otherwise (the caller is expected to pass PAS API names).</summary>
        public static NativePasApiBinding ClassifyBinding(string name)
        {
            return TryGet(name, out _)
                ? NativePasApiBinding.DeclaredOnly
                : NativePasApiBinding.DeclaredAndBound;
        }

        /// <summary>Whether the faithful native behavior for this name is
        /// "unsupported / no-op" (i.e. PasApiBridge reject is correct).</summary>
        public static bool IsFaithfulReject(string name) => TryGet(name, out _);
    }
}
