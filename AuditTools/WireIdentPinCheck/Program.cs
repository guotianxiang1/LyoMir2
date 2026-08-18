// Pins Grobal2.cs to the wire-ident set extracted from the original M2Server image.
//
// WHY THIS EXISTS
// ---------------
// OPEN_FINDINGS F-2: an internal RM queue tag was declared with an SM_ prefix and
// then handed to SendDefMessage, so the server put 12326 on the wire for the
// fixed-coord reply. The original client does not know that number -- it only ever
// sees 3420, which the RM handler emits at 0x6B6051 `66 BA 5C 0D mov dx,0xD5C`.
// A second instance of the same shape was found on w/m-sm1: SM_LINGFU_CHANGED held
// 10054, an enqueue-only tag whose wire ident is 1202.
//
// THE TEST FOR "IS THIS A WIRE IDENT"
// -----------------------------------
// A constant is a wire ident only if it reaches a send slot. Sends leave M2Server
// through the procedure-typed fields [obj+0x250] (sMsg tail) and [obj+0x254]
// (Buf/Len tail), and through the virtual slot [vmt+0xE0]. Enumerating all 817 such
// call sites and back-solving the ident register (DX, plus CX for the wrapper
// family headed by 0x6BCE54) yields the table below.
//
// Signatures, re-verified for this tool:
//   [obj+0x250]  SendDefMessage(Self=eax, wIdent=dx, nRecog=ecx, Param, Tag, Series, sMsg)
//   [obj+0x254]  same, last two stack args replaced by (Buf, Len)
//   [vmt+0x0E0]  virtual sender, one extra trailing flag
//
// TWO KNOWN LIMITS, both deliberately handled by not over-claiming:
//   1. Some arms forward the ident out of the queued record (`mov edx,[eax]`) or out
//      of a stack local (`mov dx,[ebp-0x10]`) rather than loading an immediate, so the
//      static set is a LOWER BOUND. Idents 32 and 34 reach the wire that way and are
//      invisible to the scan; the YB list quartet 3001/3002/3005/3006 was recovered by
//      hand and lives in WireStackLocal.
//   2. [vmt+0xE0] is a low VMT index that every Delphi class has, so VCL forms
//      produce false hits. Sites below 0x600000 are therefore dropped; that removes
//      exactly one value (0) and nothing else.
// Because of limit 1 the set is never used on its own to prove a value is NOT an
// ident. Production traffic from the original Delphi deployment
// (GameGate2/procMsgLog/srv_AppearTimes.ini) is carried as a second, independent
// corroborating source, and the unconfirmed set is a frozen baseline rather than a
// hard failure.
//
// Regenerate with staging/_sm1_work/s08_final.py + s14_report.py + s16_gentool.py.
using System.Reflection;
using SystemModule;

try
{
    var consts = LoadIntConstants();
    // A reflection typo must not silently pass (REPLICATION_RULES 4.17).
    if (consts.Count < 900)
        return Fail($"reflection over Grobal2 returned only {consts.Count} int constants");

    var failures = 0;
    failures += CheckEmbeddedTables();
    failures += CheckPinnedIdents(consts);
    failures += CheckNoSmInsideRmTagSpace(consts);
    failures += CheckNoNewUnconfirmedSm(consts);

    if (failures > 0)
    {
        Console.Error.WriteLine($"WireIdentPinCheck FAIL: {failures} violation(s)");
        return 1;
    }

    var smCount = consts.Count(p => p.Key.StartsWith("SM_", StringComparison.Ordinal));
    Console.WriteLine(
        $"PASS WireIdentPinCheck wire={WireTables.Wire.Length} " +
        $"stack-local={WireTables.WireStackLocal.Length} " +
        $"traffic={WireTables.Traffic.Length} sm={smCount} rm-space-collisions=0 " +
        $"baseline={WireTables.BaselineUnconfirmedSm.Length}");
    return 0;
}
catch (Exception exception)
{
    // Exit 2 = the tool itself broke. Exit 1 = a contract was violated. Keeping them
    // apart matters; sharing a code turns a real failure into a blind spot.
    Console.Error.WriteLine($"WireIdentPinCheck ERROR: {exception}");
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine("WireIdentPinCheck FAIL: " + message);
    return 1;
}

static Dictionary<string, long> LoadIntConstants()
{
    var result = new Dictionary<string, long>(StringComparer.Ordinal);
    foreach (var field in typeof(Grobal2).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (!field.IsLiteral || field.IsInitOnly) continue;
        var raw = field.GetRawConstantValue();
        if (raw is int or short or ushort or uint or byte or sbyte or long)
            result[field.Name] = Convert.ToInt64(raw);
    }
    return result;
}

// Guards the embedded tables themselves against a careless edit.
static int CheckEmbeddedTables()
{
    var bad = 0;
    if (WireTables.Wire.Length != WireTables.ExpectedWireCount)
        bad += Fail($"wire table has {WireTables.Wire.Length} entries, expected " +
                    $"{WireTables.ExpectedWireCount}");
    if (WireTables.Traffic.Length != WireTables.ExpectedTrafficCount)
        bad += Fail($"traffic table has {WireTables.Traffic.Length} entries, expected " +
                    $"{WireTables.ExpectedTrafficCount}");
    if (WireTables.WireStackLocal.Length != WireTables.ExpectedWireStackLocalCount)
        bad += Fail($"stack-local wire table has {WireTables.WireStackLocal.Length} entries, " +
                    $"expected {WireTables.ExpectedWireStackLocalCount}");

    // Anchors that must be present: every one was read off the image by hand.
    var mustHave = new (int Ident, string Where)[]
    {
        (3420, "0x6B6051 mov dx,0xD5C  fixed-coord reply"),
        (1202, "0x6B4E3A mov dx,0x4B2  capital info"),
        (41,   "0x6F2E2B mov dx,0x29   feature changed"),
        (1230, "0x690DE6 mov dx,0x4CE  physical att"),
        (629,  "0x6D9CD0 mov dx,0x275  act good"),
        (630,  "0x6D9B9E mov dx,0x276  act fail"),
        (3001, "0x6E80F8 mov [ebp-0x10],0xBB9 -> 0x6E82D7 call [ebx+0x254]"),
        (3002, "0x6E8109 mov [ebp-0x10],0xBBA -> same sink"),
        (3005, "0x6E811A mov [ebp-0x10],0xBBD -> same sink"),
        (3006, "0x6E8123 mov [ebp-0x10],0xBBE -> same sink"),
    };
    foreach (var entry in mustHave)
        if (!WireTables.WireSet.Contains(entry.Ident))
            bad += Fail($"wire table lost anchor {entry.Ident} ({entry.Where})");

    // Anchors that must be absent: internal tags and the VCL false positive.
    var mustNotHave = new (int Ident, string Why)[]
    {
        (12326, "F-2 fixed-coord queue tag, enqueued at 0x6B2414/0x6E9CFE"),
        (10054, "capital-info queue tag, enqueued at 0x6B99F3"),
        (4,     "was SM_41; no send-slot site anywhere"),
        (0,     "VCL [vmt+0xE0] false positive, all sites below 0x600000"),
    };
    foreach (var entry in mustNotHave)
        if (WireTables.WireSet.Contains(entry.Ident))
            bad += Fail($"wire table wrongly contains {entry.Ident} ({entry.Why})");
    return bad;
}

// The individual facts this sweep established, pinned by value.
static int CheckPinnedIdents(Dictionary<string, long> consts)
{
    var bad = 0;
    var pins = new (string Name, long Expected, string Evidence)[]
    {
        ("RM_LINGFU_CHANGED", 10054L,
         "enqueue 0x6B99F3 `66 B9 46 27 mov cx,0x2746` -> call 0x765E68"),
        ("SM_GETDIAMNUM_EXT", 1202L,
         "RM 10054 arm 0x6B4DED sends 0x6B4E3A `66 BA B2 04 mov dx,0x4B2`"),
        ("SM_FIXEDCOORD", 3420L, "RM handler 0x6B6051 `66 BA 5C 0D mov dx,0xD5C`"),
        ("CM_SETFIXEDCOORD", 3420L, "dispatch 0x6D873F sub eax,0xD5C"),
        ("SM_FEATURECHANGED", 41L,
         "0x6F2E2B `66 BA 29 00 mov dx,0x29` -> 0x6F2E33 call [ebx+0x254]"),
        ("SM_PHYSICAL_ATT", 1230L,
         "6 sites, first 0x690DE6 `66 BA CE 04 mov dx,0x4CE` -> call [esi+0xE0]"),
        // 4415-4650 slice, round 2: the six traffic-bearing MISSING senders,
        // back-solved to their native VMT classes. Deferred (no C# emit) but the
        // wire fact is frozen here so a later port cannot drift the ident.
        ("SM_RELATION_FRIEND_ENTRY", 4441L,
         "TFriendRelation entry fn 0x6FF4D1 `66 BA 59 11 mov dx,0x1159` -> [ebx+0x254], Len 0x24"),
        ("SM_RELATION_ATTENTION_ENTRY", 4442L,
         "TAttentionRelation entry fn 0x6FFE28 `66 BA 5A 11 mov dx,0x115A`, Len 0x16"),
        ("SM_RELATION_BLACKLIST_ENTRY", 4443L,
         "TNormalBlackRelation entry fn 0x700910 `66 BA 5B 11 mov dx,0x115B`, Len 0x14"),
        ("SM_RELATION_FRIEND_LOGON", 4444L,
         "TFriendRelation logon broadcast 0x6FE921 `66 BA 5C 11 mov dx,0x115C` -> [ebx+0x250]"),
        ("SM_RELATION_FRIEND_LOGOFF", 4445L,
         "TFriendRelation logoff broadcast 0x6FE85D `66 BA 5D 11 mov dx,0x115D` -> [ebx+0x250]"),
        ("SM_YBDEAL_SET_NOTIFY", 4446L,
         "YB TYBDealSetInfo notify 0x6F75E7 `66 BA 5E 11 mov dx,0x115E`, Recog=[player+0x192C][+0xC]+0x26"),
        // The four YB list replies. Their ident never appears as `mov dx,imm`; see
        // WireStackLocal below for the full selector -> ident -> sink chain.
        ("SM_YB_CONSIGN_INBOX", 3001L,
         "CM 1252 -> 0x632A14, 0x632B0E `B9 7A 04 00 00 mov ecx,0x47A` -> emitter arm 0x6E80F8"),
        ("SM_YB_CONSIGN_OUTBOX", 3002L,
         "CM 1253 -> 0x632E7C, 0x632F86 `B9 7B 04 00 00 mov ecx,0x47B` -> emitter arm 0x6E8109"),
        ("SM_YB_DEAL_BUY_HISTORY", 3005L,
         "CM 1256 -> 0x632BEC, 0x632CF3 `B9 80 04 00 00 mov ecx,0x480` -> emitter arm 0x6E811A"),
        ("SM_YB_DEAL_SELL_HISTORY", 3006L,
         "CM 1257 -> 0x632D34, 0x632E3E `B9 81 04 00 00 mov ecx,0x481` -> emitter arm 0x6E8123"),
    };
    foreach (var pin in pins)
    {
        if (!consts.TryGetValue(pin.Name, out var actual))
            bad += Fail($"Grobal2.{pin.Name} is missing (expected {pin.Expected}; " +
                        $"{pin.Evidence})");
        else if (actual != pin.Expected)
            bad += Fail($"Grobal2.{pin.Name} = {actual}, expected {pin.Expected} " +
                        $"({pin.Evidence})");
    }

    // These names were removed because they invited putting a non-ident on the wire.
    var banned = new (string Name, string Why)[]
    {
        ("SM_LINGFU_CHANGED",
         "10054 is an enqueue-only tag; the SM_ prefix is the F-2 hazard"),
        ("SM_41", "held 4 while the name promised wire ident 41"),
    };
    foreach (var entry in banned)
        if (consts.ContainsKey(entry.Name))
            bad += Fail($"Grobal2.{entry.Name} is back ({entry.Why})");
    return bad;
}

// The F-2 defect pattern itself: an SM_ constant whose value lives in the internal
// RM queue-tag space. The bands are derived from the RM_ constants at runtime, so
// this keeps working as the RM table grows.
static int CheckNoSmInsideRmTagSpace(Dictionary<string, long> consts)
{
    var rm = consts.Where(p => p.Key.StartsWith("RM_", StringComparison.Ordinal))
                   .Select(p => p.Value).Distinct().OrderBy(v => v).ToArray();
    if (rm.Length < 100)
        return Fail($"only {rm.Length} RM_ constants found; band derivation is unsafe");

    var bands = new List<(long Lo, long Hi)>();
    long lo = rm[0], prev = rm[0];
    foreach (var v in rm.Skip(1))
    {
        if (v - prev > 300) { bands.Add((lo, prev)); lo = v; }
        prev = v;
    }
    bands.Add((lo, prev));

    var bad = 0;
    var sm = consts.Where(p => p.Key.StartsWith("SM_", StringComparison.Ordinal))
                   .OrderBy(p => p.Value);
    foreach (var entry in sm)
    {
        if (WireTables.WireSet.Contains(entry.Value)) continue;
        if (WireTables.TrafficSet.Contains(entry.Value)) continue;
        foreach (var band in bands)
            if (entry.Value >= band.Lo && entry.Value <= band.Hi)
            {
                bad += Fail(
                    $"Grobal2.{entry.Key} = {entry.Value} sits inside the internal RM tag " +
                    $"band [{band.Lo}..{band.Hi}] and has neither a send-slot site nor " +
                    "production traffic. If it really is a queue tag, name it RM_; if it " +
                    "really goes on the wire, give the VA of the `mov dx,imm` that emits it.");
                break;
            }
    }
    return bad;
}

// Ratchet. Everything currently unconfirmed is frozen by name; the list may shrink
// but must not grow. This is deliberately not a hard failure on today's contents --
// other quarters of the SM space are still being worked, and the static set is a
// lower bound (see the header) -- but a NEW unconfirmed SM_ constant is a red flag
// that has to be justified with a VA.
static int CheckNoNewUnconfirmedSm(Dictionary<string, long> consts)
{
    var allowed = new HashSet<string>(WireTables.BaselineUnconfirmedSm, StringComparer.Ordinal);
    var bad = 0;
    var sm = consts.Where(p => p.Key.StartsWith("SM_", StringComparison.Ordinal))
                   .OrderBy(p => p.Key, StringComparer.Ordinal);
    foreach (var entry in sm)
    {
        if (WireTables.WireSet.Contains(entry.Value)) continue;
        if (WireTables.TrafficSet.Contains(entry.Value)) continue;
        if (allowed.Contains(entry.Key)) continue;
        bad += Fail(
            $"Grobal2.{entry.Key} = {entry.Value} is a new SM_ constant with no send-slot " +
            "site in the image and no production traffic. Add the emitting VA to the wire " +
            "table, or name it RM_ if it is an internal tag.");
    }
    return bad;
}

internal static class WireTables
{
    public const int ExpectedWireCount = 503;
    public const int ExpectedTrafficCount = 198;
    public const int ExpectedWireStackLocalCount = 4;

    // Idents recovered from the image by back-solving every send-slot call site.
    public static readonly int[] Wire =
    {
        2, 6, 7, 8, 9, 10, 11, 13, 14, 15, 16, 17, 18, 19,
        20, 21, 22, 23, 24, 25, 26, 27, 28, 30, 31, 33, 35, 37,
        40, 41, 42, 44, 45, 46, 50, 51, 52, 53, 54, 56, 60, 61,
        62, 66, 70, 71, 72, 73, 100, 102, 103, 104, 105, 106, 108, 200,
        201, 202, 203, 210, 211, 212, 213, 214, 528, 539, 543, 545, 546, 551,
        554, 600, 601, 610, 611, 612, 614, 615, 616, 619, 620, 621, 622, 624,
        625, 626, 627, 628, 629, 630, 633, 634, 635, 636, 637, 638, 639, 640,
        641, 642, 643, 644, 645, 646, 647, 648, 649, 650, 651, 652, 653, 654,
        655, 656, 657, 658, 659, 660, 661, 662, 663, 664, 665, 666, 667, 668,
        669, 670, 671, 673, 674, 675, 676, 681, 682, 684, 685, 686, 687, 689,
        700, 701, 702, 703, 704, 705, 706, 707, 708, 709, 710, 711, 712, 713,
        714, 716, 717, 718, 751, 762, 763, 767, 773, 790, 800, 801, 803, 804,
        805, 806, 807, 812, 813, 814, 815, 816, 817, 818, 819, 820, 821, 888,
        889, 896, 897, 898, 899, 900, 902, 903, 904, 905, 906, 907, 908, 909,
        910, 911, 912, 913, 914, 915, 916, 917, 918, 919, 920, 921, 922, 923,
        924, 925, 950, 951, 952, 953, 959, 960, 961, 962, 963, 965, 966, 971,
        1100, 1101, 1102, 1103, 1104, 1105, 1107, 1108, 1109, 1200, 1201, 1202, 1215, 1216,
        1230, 1232, 1233, 1234, 1250, 1251, 1252, 1253, 1254, 1255, 1256, 1257, 1258, 1259,
        1260, 1261, 1262, 1263, 1264, 1265, 1281, 1290, 1504, 1505, 1506, 1507, 1508, 1530,
        1726, 1727, 1729, 1730, 1731, 1732, 1733, 1734, 1735, 1736, 1737, 1738, 2812, 2813,
        2815, 2819, 2820, 2821, 2828, 2830, 2831, 2843, 2844, 2845, 2846, 2850, 2865, 2875,
        2878, 2880, 2881, 2885, 2896, 2897, 2898, 2951, 2952, 2953, 2956, 2957, 2958, 2960,
        2968, 2969, 2970, 2972, 3003, 3004, 3007, 3009, 3010, 3015, 3283, 3289, 3291, 3310,
        3312, 3313, 3322, 3324, 3325, 3332, 3340, 3341, 3367, 3412, 3413, 3414, 3415, 3417,
        3418, 3419, 3420, 3452, 3554, 3555, 3557, 3558, 4032, 4033, 4034, 4035, 4037, 4038,
        4070, 4106, 4117, 4205, 4206, 4230, 4331, 4339, 4340, 4348, 4349, 4350, 4351, 4352,
        4361, 4363, 4407, 4408, 4409, 4410, 4411, 4412, 4413, 4414, 4415, 4418, 4419, 4420,
        4421, 4422, 4424, 4425, 4426, 4427, 4428, 4429, 4430, 4431, 4432, 4433, 4434, 4435,
        4436, 4437, 4438, 4439, 4440, 4441, 4442, 4443, 4444, 4445, 4446, 4447, 4448, 4449,
        4450, 4451, 4452, 4453, 4454, 4455, 4456, 4457, 4458, 4459, 4460, 4461, 4462, 4463,
        4464, 4465, 4466, 4467, 4468, 4469, 4470, 4480, 4481, 4495, 4496, 4499, 4500, 4501,
        4520, 4521, 4522, 4523, 4524, 4525, 4526, 4527, 4528, 4529, 4530, 4531, 4532, 4533,
        4534, 4535, 4536, 4537, 4538, 4539, 4540, 4560, 4562, 4563, 4564, 4565, 4567, 4568,
        4569, 4570, 4571, 4572, 4573, 4574, 4575, 4576, 4577, 4578, 4579, 4580, 4581, 4582,
        4583, 4584, 4587, 4588, 4610, 4611, 4612, 4613, 4614, 4615, 4616, 4617, 4626, 4627,
        4628, 4629, 4631, 4632, 4634, 4635, 4636, 4637, 4638, 4646, 4647, 4649, 4650,
    };

    // Idents that the generated table above structurally cannot contain -- this is
    // header limit 1, hit for real. Kept in a separate array so `Wire` stays exactly
    // what s08_final.py emits and regenerating it does not clobber hand work.
    //
    // 元宝寄售/交易四条列表回包 share one emitter, sub_6E80CC(Self=eax, rec=edx,
    // selector=ecx, count=[ebp+8]). The selector is translated to the ident by a
    // four-arm chain and parked in a stack local:
    //   0x6E80E1  2D 7A 04 00 00        sub eax,0x47A
    //   0x6E80F8  C7 45 F0 B9 0B 00 00  mov [ebp-0x10],0xBB9   -> 3001
    //   0x6E8109  C7 45 F0 BA 0B 00 00  mov [ebp-0x10],0xBBA   -> 3002
    //   0x6E811A  C7 45 F0 BD 0B 00 00  mov [ebp-0x10],0xBBD   -> 3005
    //   0x6E8123  C7 45 F0 BE 0B 00 00  mov [ebp-0x10],0xBBE   -> 3006
    // All four arms converge on the one send slot at the tail of the row loop:
    //   0x6E82CE  66 8B 55 F0           mov dx,[ebp-0x10]
    //   0x6E82D7  FF 93 54 02 00 00     call [ebx+0x254]
    // The backward decode in s08_final.py sees a memory operand at 0x6E82CE and files
    // the sink as dynamic; the forward simulator then discards it because it only
    // promotes ARG-carried idents, and being path-insensitive it would in any case
    // see just the last arm's write. Hence: recovered by hand, pinned here.
    //
    // Reachability, back-solved at each of the four direct callers of sub_6E80CC
    // (the manager at [[0x7D6ABC]]; CM thunks per the jump table at 0x6D8315):
    //   CM 1252 0x6E7E3C -> 0x632A14, 0x632B0E mov ecx,0x47A, call 0x632B17 -> 3001
    //   CM 1253 0x6E7E90 -> 0x632E7C, 0x632F86 mov ecx,0x47B, call 0x632F8F -> 3002
    //   CM 1256 0x6E83AC -> 0x632BEC, 0x632CF3 mov ecx,0x480, call 0x632CFC -> 3005
    //   CM 1257 0x6E8400 -> 0x632D34, 0x632E3E mov ecx,0x481, call 0x632E47 -> 3006
    public static readonly int[] WireStackLocal = { 3001, 3002, 3005, 3006 };

    // Idents the original Delphi deployment actually put on the wire, from
    // GameGate2/procMsgLog/srv_AppearTimes.ini (UpdateDate 2020/1/23).
    public static readonly int[] Traffic =
    {
        6, 9, 10, 11, 13, 14, 17, 20, 21, 22, 23, 27, 30, 31,
        32, 34, 40, 41, 42, 44, 45, 46, 50, 51, 52, 53, 54, 56,
        100, 102, 103, 104, 106, 108, 200, 201, 202, 203, 210, 211, 213, 545,
        600, 601, 610, 611, 612, 614, 615, 616, 619, 620, 621, 622, 624, 625,
        626, 627, 629, 630, 633, 634, 635, 636, 638, 639, 640, 641, 642, 643,
        644, 645, 646, 647, 648, 649, 650, 651, 652, 653, 656, 657, 658, 659,
        660, 661, 662, 663, 664, 666, 667, 668, 669, 670, 671, 673, 674, 675,
        681, 682, 687, 701, 704, 705, 706, 707, 708, 709, 712, 714, 716, 718,
        751, 767, 790, 800, 801, 804, 805, 806, 807, 812, 815, 821, 888, 889,
        960, 1103, 1104, 1105, 1108, 1202, 1230, 1264, 1281, 2820, 2821, 2831, 2953, 2957,
        3290, 3322, 3324, 3341, 3554, 3555, 4003, 4004, 4010, 4012, 4013, 4014, 4017, 4018,
        4031, 4039, 4040, 4041, 4042, 4230, 4332, 4412, 4413, 4414, 4415, 4418, 4430, 4431,
        4432, 4433, 4434, 4435, 4441, 4442, 4444, 4445, 4446, 4460, 4469, 4470, 4497, 4500,
        4501, 4520, 4522, 4524, 4531, 4562, 4564, 4610, 4612, 4613, 4615, 4628, 4629, 4634,
        4635, 4636,
    };

    // SM_ constants that today have neither a send-slot site nor production traffic.
    // Most belong to other components (the 500-537 login band never reaches GameSvr)
    // or to quarters of the SM space other agents are still working. Frozen so the
    // set cannot grow silently.
    public static readonly string[] BaselineUnconfirmedSm =
    {
        "SM_43", "SM_ACTION2_MAX", "SM_ACTION2_MIN",
        "SM_ACTION_MAX", "SM_ACTION_MIN", "SM_ADJUST_BONUS",
        "SM_AREASTATE", "SM_BIGMONMAGIC", "SM_CANCEL_STALL",
        "SM_CERTIFICATION_FAIL", "SM_CERTIFICATION_SUCCESS", "SM_CHANGEGUILDNAME",
        "SM_CHGPASSWD_FAIL", "SM_CHGPASSWD_SUCCESS", "SM_DEALDELITEM_FAIL",
        "SM_DEALDELITEM_OK", "SM_DEALREMOTEDELITEM", "SM_DELCHR_FAIL",
        "SM_DELCHR_SUCCESS", "SM_DLGMSG", "SM_DONATE_OK",
        "SM_DRINKEXP_STATUS", "SM_DRINK_DRUG_STATUS", "SM_DRINK_STATUS",
        "SM_EXCHGTAKEON_FAIL", "SM_EXCHGTAKEON_OK", "SM_FIREON",
        "SM_GAMEGOLDNAME", "SM_GETBACKPASSWD_FAIL", "SM_GETBACKPASSWD_SUCCESS",
        "SM_GETREGINFO", "SM_GILD_CONCERN_GILD_NAME", "SM_GILD_DECLARE_WAR_NAME",
        "SM_GILD_QUERY_PRESIDENT", "SM_GROUPMESSAGE", "SM_HERO_RUSH",
        "SM_HERO_RUSHKUNG", "SM_HERO_SUBABILITY", "SM_HIDE",
        "SM_HORIZONHIT", "SM_HORSERUN", "SM_HWID",
        "SM_HundredHit", "SM_ID_NOTFOUND", "SM_ITEMUPDATE",
        "SM_LNGHITONOFF", "SM_MONSTERSAY",
        "SM_MOVEMESSAGE", "SM_NEEDPASSWORD", "SM_NEEDUPDATE_ACCOUNT",
        "SM_NEWCHR_FAIL", "SM_NEWCHR_SUCCESS", "SM_NEWID_FAIL",
        "SM_NEWID_SUCCESS", "SM_NPCWALK", "SM_OPENDOOR_LOCK",
        "SM_PASSOK_SELECTSERVER", "SM_PASSWD_FAIL", "SM_PASSWORDSTATUS",
        "SM_PLAYERCONFIG", "SM_QUERYCHR", "SM_QUERYCHR_FAIL",
        "SM_QUERYDELCHR", "SM_QUERYDELCHR_FAIL", "SM_RECONNECT",
        "SM_RENAMECHR4016", "SM_RESDELCHR_FAIL", "SM_RESDELCHR_SUCCESS",
        "SM_RUN3", "SM_RUNGATELOGOUT", "SM_SELECTSERVER_OK",
        "SM_SELECT_SERVER", "SM_SENDGAMELIST", "SM_SEND_TITLEINFO",
        "SM_SERVERCONFIG", "SM_SERVER_LIST", "SM_SHOWBODY_EFFECT",
        "SM_SITDOWN", "SM_SLAVE_BORN", "SM_SLAVE_VANISH",
        "SM_SPELL2", "SM_SQUARE_HIT", "SM_STARTFAIL",
        "SM_STARTPLAY", "SM_SUBABILITY", "SM_TASK_LIST_CHANGED",
        "SM_TEST", "SM_TIMECHECK_MSG", "SM_UPDATEID_FAIL",
        "SM_UPDATEID_SUCCESS", "SM_VERSION_FAIL", "SM_V_POWERSTONE",
        "SM_WIDEHITONOFF",
    };

    public static readonly HashSet<long> WireSet =
        new(Wire.Concat(WireStackLocal).Select(v => (long)v));
    public static readonly HashSet<long> TrafficSet = new(Traffic.Select(v => (long)v));
}
