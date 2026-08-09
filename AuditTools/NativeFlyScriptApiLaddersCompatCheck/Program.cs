using System;
using System.Collections.Generic;
using GameSvr;

// Dormant-model compat check for NativeFlyScriptApiLadders.cs — the PAS-script
// "Fly / teleport" family native handler decision ladders and the declared-only
// (no-body) registry. Every branch of every modeled ladder is asserted.
//
// Single generic assertion helper (no overloaded local Equal).

int checks = 0;

void Equal<T>(T actual, T expected, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.Error.WriteLine(
            $"FAIL: {label}: expected <{expected}>, got <{actual}>");
        Environment.Exit(1);
    }
}

// ---------------------------------------------------------------------------
// 1. Flyto (sub_6DEF8C): (x!=0 && y!=0) ? PreciseMove : FallbackMove.
//    Tests are 16-bit (test si,si / test di,di), so only low 16 bits count.
// ---------------------------------------------------------------------------
Equal(NativeFlytoPlanner.Plan(5, 5),
    NativeFlytoOutcome.PreciseMove, "Flyto(5,5) precise");
Equal(NativeFlytoPlanner.Plan(0, 5),
    NativeFlytoOutcome.FallbackMove, "Flyto(0,5) fallback (x==0)");
Equal(NativeFlytoPlanner.Plan(5, 0),
    NativeFlytoOutcome.FallbackMove, "Flyto(5,0) fallback (y==0)");
Equal(NativeFlytoPlanner.Plan(0, 0),
    NativeFlytoOutcome.FallbackMove, "Flyto(0,0) fallback");
Equal(NativeFlytoPlanner.Plan(0x10000, 5),
    NativeFlytoOutcome.FallbackMove, "Flyto(0x10000,5) fallback (low16 x==0)");
Equal(NativeFlytoPlanner.Plan(0x10001, 5),
    NativeFlytoOutcome.PreciseMove, "Flyto(0x10001,5) precise (low16 x!=0)");
Equal(NativeFlytoPlanner.WrapperAddress, 0x006DEF8C, "Flyto wrapper addr");
// Live PasApiBridge "flyto" implements: FallbackMove -> SpaceMove(map, 1, 1).
Equal(NativeFlytoPlanner.FallbackPlaceholderCoord, 1, "Flyto fallback placeholder coord = 1 (SpaceMove 1,1)");

// ---------------------------------------------------------------------------
// 2. GroupFly (sub_6E0678): eligible ? MoveGroup : NotEligible.
// ---------------------------------------------------------------------------
Equal(NativeGroupFlyPlanner.Plan(false),
    NativeGroupFlyOutcome.NotEligible, "GroupFly !eligible");
Equal(NativeGroupFlyPlanner.Plan(true),
    NativeGroupFlyOutcome.MoveGroup, "GroupFly eligible");
Equal(NativeGroupFlyPlanner.WrapperAddress, 0x006E0678, "GroupFly wrapper addr");

// ---------------------------------------------------------------------------
// 3. GroupFlyEx (sub_6E06A0): eligible ? MoveGroupAndReport : NotEligible;
//    result = eligible ? (report & 0xFFFF) : 0.
// ---------------------------------------------------------------------------
Equal(NativeGroupFlyExPlanner.Plan(false),
    NativeGroupFlyExOutcome.NotEligible, "GroupFlyEx !eligible");
Equal(NativeGroupFlyExPlanner.Plan(true),
    NativeGroupFlyExOutcome.MoveGroupAndReport, "GroupFlyEx eligible");
Equal(NativeGroupFlyExPlanner.ResolveResult(false, 7),
    0, "GroupFlyEx result forced 0 when !eligible");
Equal(NativeGroupFlyExPlanner.ResolveResult(true, 7),
    7, "GroupFlyEx result = report when eligible");
Equal(NativeGroupFlyExPlanner.ResolveResult(true, 0x10005),
    5, "GroupFlyEx result word-masked");
Equal(NativeGroupFlyExPlanner.WrapperAddress, 0x006E06A0, "GroupFlyEx wrapper addr");

// ---------------------------------------------------------------------------
// 4. GroupFlyInRange (sub_6E07B4): !hasGroup -> NoGroup;
//    !mapResolves -> MapNotFound; else DispatchRangeMove.
//    + resolved sub_727678 dispatch mapping (centerX=x, centerY=y, radius=iRange).
// ---------------------------------------------------------------------------
Equal(NativeGroupFlyInRangePlanner.Plan(false, false),
    NativeGroupFlyInRangeOutcome.NoGroup, "GroupFlyInRange no group");
Equal(NativeGroupFlyInRangePlanner.Plan(false, true),
    NativeGroupFlyInRangeOutcome.NoGroup, "GroupFlyInRange no group (map ok)");
Equal(NativeGroupFlyInRangePlanner.Plan(true, false),
    NativeGroupFlyInRangeOutcome.MapNotFound, "GroupFlyInRange map not found");
Equal(NativeGroupFlyInRangePlanner.Plan(true, true),
    NativeGroupFlyInRangeOutcome.DispatchRangeMove, "GroupFlyInRange dispatch");
Equal(NativeGroupFlyInRangePlanner.WrapperAddress, 0x006E07B4,
    "GroupFlyInRange wrapper addr");
Equal(NativeGroupFlyInRangePlanner.RangeMoverAddress, 0x00727678,
    "GroupFlyInRange shared range-mover addr");

var disp = NativeGroupFlyInRangePlanner.BuildDispatch(100, 200, 5);
Equal(disp.CenterX, 100, "dispatch centerX = x");
Equal(disp.CenterY, 200, "dispatch centerY = y");
Equal(disp.Radius, 5, "dispatch radius = iRange");
Equal(disp.AxisLowerBound(100), 95, "dispatch axis lower bound = center-radius");
Equal(disp.AxisLowerBound(200), 195, "dispatch axis lower bound Y = center-radius");
Equal(disp.AxisRandomSpan(), 10, "dispatch random span = 2*radius");
Equal(NativeGroupFlyInRangeDispatch.MemberSlotCount, 11, "member slot count");
Equal(NativeGroupFlyInRangeDispatch.EcxSource, "x (centerX)", "ECX source resolved");
Equal(NativeGroupFlyInRangeDispatch.FirstPushedSource, "y (centerY)",
    "first-pushed source resolved");
Equal(NativeGroupFlyInRangeDispatch.LastPushedSource, "iRange (radius)",
    "last-pushed source resolved");

// ---------------------------------------------------------------------------
// 5. GroupFlyToDynRoom (sub_6E06D8): hasGroup ? DispatchGroupToRoom : NoGroup.
// ---------------------------------------------------------------------------
Equal(NativeGroupFlyToDynRoomPlanner.Plan(false),
    NativeGroupFlyToDynRoomOutcome.NoGroup, "GroupFlyToDynRoom no group");
Equal(NativeGroupFlyToDynRoomPlanner.Plan(true),
    NativeGroupFlyToDynRoomOutcome.DispatchGroupToRoom, "GroupFlyToDynRoom dispatch");
Equal(NativeGroupFlyToDynRoomPlanner.WrapperAddress, 0x006E06D8,
    "GroupFlyToDynRoom wrapper addr");
Equal(NativeGroupFlyToDynRoomPlanner.GroupRoomMoverAddress, 0x00727884,
    "GroupFlyToDynRoom executor addr");

// ---------------------------------------------------------------------------
// 6. Declared-only (no body) flagged handlers: reject is faithful.
// ---------------------------------------------------------------------------
Equal(NativeDeclaredOnlyPasApi.TryGet("FlyToObserverMap", out var r1), true,
    "FlyToObserverMap is declared-only");
Equal(r1.SignatureAddressPrimary, 0x0072DA90, "FlyToObserverMap sig addr");
Equal(r1.SignatureAddressMirror, 0x0109A690, "FlyToObserverMap mirror sig addr");
Equal(r1.DeclarationSite, 0x0072B37A, "FlyToObserverMap declaration site");

Equal(NativeDeclaredOnlyPasApi.TryGet("flytowespot", out var r2), true,
    "FlyToWeSpot is declared-only (case-insensitive)");
Equal(r2.SignatureAddressPrimary, 0x007307E4, "FlyToWeSpot sig addr");

Equal(NativeDeclaredOnlyPasApi.TryGet("SouthWildStartConvoy", out var r3), true,
    "SouthWildStartConvoy is declared-only");
Equal(r3.DeclarationSite, 0x0072A9D3, "SouthWildStartConvoy declaration site");
Equal(r3.SignatureAddressPrimary, 0x0072BF8C, "SouthWildStartConvoy sig addr");

Equal(NativeDeclaredOnlyPasApi.TryGet("SouthWildStartMonAttack", out var r4), true,
    "SouthWildStartMonAttack is declared-only");
Equal(r4.SignatureAddressPrimary, 0x0072BFC8, "SouthWildStartMonAttack sig addr");

Equal(NativeDeclaredOnlyPasApi.TryGet("GroupFly", out _), false,
    "GroupFly is NOT declared-only (it has a bound body)");
Equal(NativeDeclaredOnlyPasApi.TryGet("Flyto", out _), false,
    "Flyto is NOT declared-only (it has a bound body)");

Equal(NativeDeclaredOnlyPasApi.ClassifyBinding("FlyToObserverMap"),
    NativePasApiBinding.DeclaredOnly, "FlyToObserverMap binding = DeclaredOnly");
Equal(NativeDeclaredOnlyPasApi.ClassifyBinding("GroupFly"),
    NativePasApiBinding.DeclaredAndBound, "GroupFly binding = DeclaredAndBound");

Equal(NativeDeclaredOnlyPasApi.IsFaithfulReject("FlyToWeSpot"), true,
    "reject FlyToWeSpot is faithful");
Equal(NativeDeclaredOnlyPasApi.IsFaithfulReject("SouthWildStartMonAttack"), true,
    "reject SouthWildStartMonAttack is faithful");
Equal(NativeDeclaredOnlyPasApi.IsFaithfulReject("Flyto"), false,
    "reject Flyto would NOT be faithful (it has a body)");

Console.WriteLine($"PASS NativeFlyScriptApiLaddersCompatCheck: {checks} checks");
return 0;
