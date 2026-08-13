using System;
using System.IO;
using System.Reflection;
using GameSvr;
using SystemModule;

// State 0x33 (single-seat mount) / 0x34 (two-seat mount) gates.
//
// This audit used to assert a single public TPlayObject.IsStoneParalyzed() with
// pure-OR semantics (0x33 || 0x34). Neither half survives the image:
//
//   * There is no single predicate. Native has TWO shapes.
//     Shape A, the only one that exists as a named function, is sub_6BBE84:
//       0x6BBE8A  B2 33                 mov dl,0x33
//       0x6BBE8E  E8 CD 6A 0B 00        call 0x772960          ; HasState(0x33)
//       0x6BBE93  84 C0 / 74 09         test al,al / je 0x6BBEA0
//       0x6BBE97  83 BB C0 03 00 00 00  cmp dword [ebx+0x3C0],0
//       0x6BBE9E  75 12                 jne 0x6BBEB2           ; -> TRUE
//       0x6BBEA0  B2 34                 mov dl,0x34
//       0x6BBEA4  E8 B7 6A 0B 00        call 0x772960          ; HasState(0x34)
//       0x6BBEAB  75 05                 jne 0x6BBEB2           ; -> TRUE
//       0x6BBEAD  33 C0                 xor eax,eax            ; -> FALSE
//     i.e. (0x33 AND [self+0x3C0] != 0) OR 0x34. Solo-mounted with no partner is
//     ALLOWED. The same shape is inlined at 0x6DADE3-0x6DAE0D and 0x78A032-0x78A055.
//
//     Shape B is a pure OR with no partner test, inlined at 0x6D8DC8:
//       0x6D8DC8  B2 33 / call 0x772960 / test al,al / jne 0x6DBC2C
//       0x6D8DDA  B2 34 / call 0x772960 / test al,al / jne 0x6DBC2C
//
//   * Pure OR is therefore correct only for the shape-B family. Asserting it for
//     everything demanded that the three shape-A ports drop the +0x3C0 conjunct,
//     which would let a two-seat passenger through gates native closes and close
//     gates native leaves open for a solo rider.
//
// So this now pins shape A on the two product predicates that implement it, with
// the +0x3C0 conjunct as an explicit assertion in both directions.
//
// Coverage census (measured on flat_image.bin, ImageBase 0x400000): `mov dl,0x33`
// immediately followed by a direct `call 0x772960` occurs 40 times and
// `mov dl,0x34` 41 times; SET is 0x6EE37E / 0x6EE8AF and CLEAR is 0x6EE48C /
// 0x6EEBC2. C# carries four gates. That 40:4 gap is a real product hole, but
// closing it needs a per-site census this tool cannot stand in for.

try
{
    Diagnose("enter-main");
    Diagnose("prepare-runtime-config");
    PrepareRuntimeConfig();
    Diagnose("before-new-TPlayObject");
    _ = CreateTestPlayer();
    Diagnose("after-new-TPlayObject");
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "INCOMPLETE: TPlayObject construction/type-init failed before state33/34 assertions.");
    Console.Error.WriteLine(ex.ToString());
    return 2;
}

var groupRestricted = typeof(TPlayObject).GetMethod("IsNativeGroupRestricted",
    BindingFlags.NonPublic | BindingFlags.Static);
if (groupRestricted == null)
    throw new Exception("TPlayObject.IsNativeGroupRestricted is missing");
if (groupRestricted.ReturnType != typeof(bool))
    throw new Exception("IsNativeGroupRestricted must return bool");

var fixedCoordBlocked = typeof(TPlayObject).GetMethod("IsNativeFixedCoordMountBlocked",
    BindingFlags.NonPublic | BindingFlags.Instance);
if (fixedCoordBlocked == null)
    throw new Exception("TPlayObject.IsNativeFixedCoordMountBlocked is missing");
if (fixedCoordBlocked.ReturnType != typeof(bool))
    throw new Exception("IsNativeFixedCoordMountBlocked must return bool");

var partnerField = typeof(TPlayObject).GetField("m_NativeHorsePartner",
    BindingFlags.NonPublic | BindingFlags.Instance)
    ?? typeof(TPlayObject).GetField("m_NativeHorsePartner",
        BindingFlags.Public | BindingFlags.Instance);
if (partnerField == null)
    throw new Exception("TPlayObject.m_NativeHorsePartner is missing "
        + "(native [self+0x3C0], read at 0x6BBE97)");
if (partnerField.FieldType != typeof(TPlayObject))
    throw new Exception("m_NativeHorsePartner must be the partner actor pointer, "
        + "not a scalar: 0x6C5A99 dereferences it and reads the name at +0x106");

CheckShapeA("IsNativeGroupRestricted",
    player => (bool)groupRestricted!.Invoke(null, new object[] { player })!);
CheckShapeA("IsNativeFixedCoordMountBlocked",
    player => (bool)fixedCoordBlocked!.Invoke(player, null)!);
CheckMountStateFlags();

Console.WriteLine(
    "PASS state33/34=mount-gates shapeA=(0x33&&partner)||0x34@sub_6BBE84 "
    + "shapeB=0x33||0x34@0x6D8DC8 predicates=group+fixedcoord "
    + "binary-sites=40x0x33+41x0x34 csharp-gates=4");
return 0;

// sub_6BBE84 as a truth table. Case 2 is the one that separates the two native
// shapes: under shape B it would be blocked, under shape A it is not.
void CheckShapeA(string label, Func<TPlayObject, bool> predicate)
{
    var idle = CreateTestPlayer();
    if (predicate(idle))
        throw new Exception($"{label}: no mount state must not block (0x6BBEAD xor eax,eax)");

    var soloRider = CreateTestPlayer();
    if (!soloRider.SetNativeActiveState(0x33))
        throw new Exception($"{label}: could not set state 0x33");
    if (predicate(soloRider))
        throw new Exception($"{label}: 0x33 with a null partner must NOT block "
            + "(0x6BBE97 cmp dword [ebx+0x3C0],0 / 0x6BBE9E jne)");

    var pairedRider = CreateTestPlayer();
    if (!pairedRider.SetNativeActiveState(0x33))
        throw new Exception($"{label}: could not set state 0x33");
    partnerField!.SetValue(pairedRider, CreateTestPlayer());
    if (!predicate(pairedRider))
        throw new Exception($"{label}: 0x33 plus a partner must block (0x6BBE9E jne 0x6BBEB2)");

    var passenger = CreateTestPlayer();
    if (!passenger.SetNativeActiveState(0x34))
        throw new Exception($"{label}: could not set state 0x34");
    if (!predicate(passenger))
        throw new Exception($"{label}: 0x34 must block on its own (0x6BBEAB jne 0x6BBEB2)");

    // 0x6BBEA0 is reached with no partner test, so 0x34 blocks regardless.
    var passengerWithPartner = CreateTestPlayer();
    if (!passengerWithPartner.SetNativeActiveState(0x34))
        throw new Exception($"{label}: could not set state 0x34");
    partnerField!.SetValue(passengerWithPartner, CreateTestPlayer());
    if (!predicate(passengerWithPartner))
        throw new Exception($"{label}: 0x34 plus a partner must block");

    var both = CreateTestPlayer();
    both.SetNativeActiveState(0x33);
    both.SetNativeActiveState(0x34);
    if (!predicate(both))
        throw new Exception($"{label}: both states set must block");

    var cleared = CreateTestPlayer();
    cleared.SetNativeActiveState(0x33);
    cleared.SetNativeActiveState(0x34);
    partnerField!.SetValue(cleared, CreateTestPlayer());
    if (!cleared.ClearNativeActiveState(0x33))
        throw new Exception($"{label}: could not clear state 0x33");
    if (!cleared.ClearNativeActiveState(0x34))
        throw new Exception($"{label}: could not clear state 0x34");
    if (predicate(cleared))
        throw new Exception($"{label}: clearing both states must unblock even with "
            + "a stale partner pointer");
}

static void CheckMountStateFlags()
{
    // 0x33 = 51: word 1, bit 19. 0x34 = 52: word 1, bit 20. Native indexes the
    // obj+0x168 bitset register-wise (bt 0x772968 / bts 0x77299B / btr 0x7729B9)
    // and RM_SPELL reads exactly this pair as (1<<19)|(1<<20) on GetBodyStateWord(1)
    // in TPlayObject.Message.cs.
    var player = CreateTestPlayer();

    player.SetNativeActiveState(0x33);
    var wordWith33 = player.GetBodyStateWord(1);
    if ((wordWith33 & (1 << 19)) == 0)
        throw new Exception("State 0x33 should set bit 19 in word 1");
    if ((wordWith33 & (1 << 20)) != 0)
        throw new Exception("State 0x34 bit should not be set when only 0x33 is active");

    player.SetNativeActiveState(0x34);
    var wordWithBoth = player.GetBodyStateWord(1);
    if ((wordWithBoth & (1 << 19)) == 0)
        throw new Exception("State 0x33 bit should still be set");
    if ((wordWithBoth & (1 << 20)) == 0)
        throw new Exception("State 0x34 should set bit 20 in word 1");
}

static TPlayObject CreateTestPlayer() => new TPlayObject
{
    m_btJob = 0,
    m_boGhost = false,
    m_PEnvir = new Envirnoment()
};

static void Diagnose(string step)
{
    Console.WriteLine("DIAG step=" + step);
    Console.Out.Flush();
    Console.Error.Flush();
}

// M2Share's static constructor resolves !Setup.txt against AppContext.BaseDirectory
// (M2Share.cs:1682). The previous body called M2Share.Init() / new TConfig() /
// new TObjectManager() / new TEnvirnoment(), none of which exist on this tree,
// so the tool could not even compile. Same skeleton the other audits lay down.
static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(
        Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(
        Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(
        Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(
        Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);

    // TBaseObject's ctor ends in M2Share.ObjectManager.RegisterConstructed(this)
    // (TBaseObject.cs:903), so the singleton must exist before CreateTestPlayer can
    // build a real actor. Same minimal set the InProc harnesses boot: no engine
    // threads, no network.
    M2Share.g_Config ??= new GameSvrConfig();
    M2Share.RandomNumber ??= RandomNumber.GetInstance();
    M2Share.ObjectManager ??= new ObjectManager();
}
