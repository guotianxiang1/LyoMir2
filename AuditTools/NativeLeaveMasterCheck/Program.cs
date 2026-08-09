// 战神 sub_6C5EC8 (dissolve apprenticeship, student side) equivalence audit.
//
// One routine, four native call sites, mode passed in edx:
//   0x6BDF1D  fn~0x6BDCA8  mode=1  reputation path
//   0x6C5E36  fn~0x6C5E08  mode=0  GM @LeaveTech (dispatch index 125, perm 4)
//   0x6CB017  fn~0x6CAFF0  mode=0  PAS NpcLeaveTec
//   0x6CCEBB  fn~0x6CCE40  mode=1  login reconciliation, graduation leg
//
// Structure:
//   0x6C5EF1  cmp byte [ebx+0xB95],0 / je 0x6C60BB   entry gate (only skip)
//   0x6C5F1A  empty master name -> jump straight to teardown
//   0x6C5F37  je 0x6C5FD0   master OFFLINE -> emit 0x0152 subcmd 1 or 4
//   0x6C5F3D..0x6C5FCB      master ONLINE  -> mutate in memory, no frame
//   0x6C605A..0x6C60B4      student teardown, runs on every non-gated path
//
// Record offsets (SAVE sub_6B0FF0):
//   obj+0xB91 -> 0xDA   obj+0xB94 -> 0xDB   obj+0xB95 -> 0xDC
//   obj+0xB96 -> 0xDF   obj+0xB97 -> 0xE0   obj+0xB98 -> 0xE1
using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

// Touching M2Share runs its static ctor, which loads config files off disk.
PrepareRuntimeFiles();
M2Share.ProcessMsgCriticalSection = new object();

var failures = new List<string>();

void Assert(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

void Equal(object expected, object actual, string message)
{
    if (!Equals(expected, actual))
        failures.Add($"{message}: expected {expected}, got {actual}");
}

string Read(string relative)
{
    var root = FindRepoRoot();
    return File.ReadAllText(Path.Combine(root, relative));
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null && !File.Exists(Path.Combine(dir, "LyoMir2.sln")))
        dir = Path.GetDirectoryName(dir);
    if (dir == null) throw new InvalidOperationException("repo root not found");
    return dir;
}

// ---------------------------------------------------------------------------
// 1. Subcommand constants -- 0x6C603F `mov si,1` / 0x6C6045 `mov si,4`.
// ---------------------------------------------------------------------------
Equal((ushort)1, NativeMasterRelationFrameCodec.StudentLeftSubcommand,
    "StudentLeftSubcommand (0x6C603F mov si,1)");
Equal((ushort)4, NativeMasterRelationFrameCodec.StudentGraduatedSubcommand,
    "StudentGraduatedSubcommand (0x6C6045 mov si,4)");
Equal((ushort)0x0152, NativeMasterRelationFrameCodec.RequestCommand,
    "0x6C604C mov cx,0x152");

// ---------------------------------------------------------------------------
// 2. Payload layout -- sub_6C53B8 0x6C53C7..0x6C5416.
//      [0x00] cmd   [0x02] subcmd   [0x04] dword 0
//      [0x10] account  ShortString(20)   0x6C53E2 mov cl,0x14
//      [0x25] selfName ShortString(15)   0x6C53F2 mov cl,0x0F
//      [0x35] target   ShortString(15)   0x6C5414 mov cl,0x0F
// ---------------------------------------------------------------------------
Equal(0x48, NativeMasterRelationFrameCodec.PayloadSize,
    "0x6C5465 mov edx,0x48 payload size");
Equal(0x10, NativeMasterRelationFrameCodec.AccountOffset, "account offset");
Equal(0x25, NativeMasterRelationFrameCodec.MasterNameOffset, "self offset");
Equal(0x35, NativeMasterRelationFrameCodec.StudentNameOffset, "target offset");

foreach (var (subcommand, label) in new[]
         {
             (NativeMasterRelationFrameCodec.StudentLeftSubcommand, "left"),
             (NativeMasterRelationFrameCodec.StudentGraduatedSubcommand, "grad"),
         })
{
    Assert(NativeMasterRelationFrameCodec.TryEncode(subcommand, "acct",
            "student", "master", out var frame, out var error),
        $"TryEncode({label}) failed: {error}");
    if (frame == null) continue;

    // The envelope is LegacyDbServerFrameCodec's; locate the 0x48 payload by
    // its 0x0152 header rather than assuming a wrapper size.
    var at = IndexOfPayload(frame);
    Assert(at >= 0, $"{label}: 0x0152 payload not found in frame");
    if (at < 0) continue;

    Equal(0x0152, frame[at] | (frame[at + 1] << 8), $"{label} cmd word");
    Equal((int)subcommand, frame[at + 2] | (frame[at + 3] << 8),
        $"{label} subcmd word");
    Equal(0, BitConverter.ToInt32(frame, at + 4), $"{label} 0x04 must be 0");
    Equal(4, (int)frame[at + 0x10], $"{label} account length byte");
    Equal(7, (int)frame[at + 0x25], $"{label} selfName length byte");
    Equal(6, (int)frame[at + 0x35], $"{label} target length byte");
}

// Over-long names must be refused, not truncated (0x6C53F2 cl=15 bound).
Assert(!NativeMasterRelationFrameCodec.TryEncode(
        NativeMasterRelationFrameCodec.StudentLeftSubcommand, "acct",
        new string('x', 16), "master", out _, out _),
    "16-byte self name must be rejected (15-byte ShortString bound)");

// ---------------------------------------------------------------------------
// 3. The gold charge -- 0x6CB003 `mov edx,0xC350`, and DecGold == sub_6C7D64.
// ---------------------------------------------------------------------------
var bridge = Read(Path.Combine("GameSvr", "ScriptSystem", "PasEngine",
    "PasApiBridge.cs"));
Assert(bridge.Contains("NativeLeaveTecGoldCost = 0xC350"),
    "NpcLeaveTec gold cost must be the literal 0xC350 from 0x6CB003");
Assert(bridge.Contains("CurrentPlayer.DecGold(NativeLeaveTecGoldCost)"),
    "NpcLeaveTec must charge via DecGold (native sub_6C7D64) so a poor "
    + "student is not billed");
Assert(bridge.Contains("CurrentPlayer.NativeLeaveMaster(0)"),
    "NpcLeaveTec must call the shared routine with mode 0 (0x6CB013 xor edx,edx)");
Assert(!bridge.Contains("已与你解除师徒关系"),
    "the invented 已与你解除师徒关系 message must be gone -- native sends "
    + "\"你的徒弟 X 自行离开师门！\" (0x6C60F4 + 0x6C6108)");
Assert(bridge.Contains("你尚无师承或携带的金币不够, 不能离开！"),
    "gate failure must send the native string at 0x6CB048");

// ---------------------------------------------------------------------------
// 4. GoldChanged is emitted INSIDE the deduct/credit, not by callers.
//      0x6C7D7B call 0x6C19B4  (DecGold)
//      0x6D793C call 0x6C19B4  (IncGold)
//    sub_6C19B4 == SendMsg(cx=0x2798) == RM_GOLDCHANGED 10136.
// ---------------------------------------------------------------------------
Equal(10136, Grobal2.RM_GOLDCHANGED, "sub_6C19B4 cx=0x2798");
var playObject = Read(Path.Combine("GameSvr", "Players", "TPlayObject.cs"));
var decGold = Slice(playObject, "public bool DecGold(int nGold)", "public ");
Assert(decGold.Contains("GoldChanged();"),
    "DecGold must call GoldChanged() on success (0x6C7D7B)");
var incGold = Slice(playObject, "public bool IncGold(int tGold)", "public ");
Assert(incGold.Contains("GoldChanged();"),
    "IncGold must call GoldChanged() on success (0x6D793C)");
// Both are success-path only: native reaches the call only after the store.
Assert(decGold.IndexOf("m_nGold -= nGold;", StringComparison.Ordinal)
       < decGold.IndexOf("GoldChanged();", StringComparison.Ordinal),
    "DecGold GoldChanged() must follow the deduction, not precede it");
Assert(decGold.IndexOf("if (nGold < 0)", StringComparison.Ordinal)
       < decGold.IndexOf("GoldChanged();", StringComparison.Ordinal),
    "DecGold negative-amount gate must still short-circuit before the send");

// ---------------------------------------------------------------------------
// 5. Login reconciliation leg B now has native's graduation branch.
//      0x6CCEA3 movzx eax,word [ebx+0x278] / 0x6CCEB0 cmp / jl 0x6CCEC2
//      0x6CCEB4 mov edx,1 / 0x6CCEBB call sub_6C5EC8
// ---------------------------------------------------------------------------
var social = Read(Path.Combine("GameSvr", "Players",
    "TPlayObject.NativeSocialSlots.cs"));
Assert(social.Contains("NativeLeaveMaster(1)"),
    "HealNativeRelationFlags leg B must graduate at/above nMasterOKLevel "
    + "(0x6CCEBB, mode 1) instead of silently doing nothing");
Assert(social.Contains("m_Abil.Level >= M2Share.g_Config.nMasterOKLevel"),
    "leg B graduation gate must be the CHUSHI level compare at 0x6CCEB0");
var legB = Slice(social, "// Leg B", "// Leg C");
Assert(legB.IndexOf("IsNativeMasterSlotEmpty()", StringComparison.Ordinal)
       < legB.IndexOf("nMasterOKLevel", StringComparison.Ordinal),
    "empty-slot heal (0x6CCEA1 je 0x6CCED3) must be tested BEFORE the level "
    + "compare, so a student with an empty slot never graduates");

// ---------------------------------------------------------------------------
// 6. The shared routine's shape.
// ---------------------------------------------------------------------------
var leave = Read(Path.Combine("GameSvr", "Players",
    "TPlayObject.NativeLeaveMaster.cs"));
Assert(leave.Contains("if (!m_boStudent)"),
    "0x6C5EF1 entry gate on obj+0xB95 must be the only path that skips teardown");
Assert(leave.Contains("ApplyNativeLeaveMasterOffline"),
    "the offline-master leg (0x6C5FD0) must exist -- it is NOT a no-op");
Assert(leave.Contains("ApplyNativeLeaveMasterSelfTeardown"),
    "teardown (0x6C605A) must be a separate unconditional step");
// Teardown must be reached even when the master name is empty (0x6C5F1E) and
// when the online slot scan bails (0x6C5F4C / 0x6C5F58): in native all three
// jump to the SAME label 0x6C605A.
var body = Slice(leave, "internal void NativeLeaveMaster(int mode)",
    "/// <summary>");
Assert(body.IndexOf("if (masterName.Length != 0)", StringComparison.Ordinal)
       < body.IndexOf("ApplyNativeLeaveMasterSelfTeardown",
           StringComparison.Ordinal),
    "empty master name must fall through to teardown (0x6C5F1E je 0x6C605A)");
Assert(leave.Contains("sub_7138CC"),
    "the dead sub_713890/sub_7138CC notification leg must be documented as "
    + "dead rather than invented");
Assert(!leave.Contains("NotifyNativeOfflineMasterRelation"),
    "must not emit the sub_713890 notification -- sub_7138CC is an empty stub "
    + "(push ebp / mov ebp,esp / pop ebp / ret 0xC)");

// Graduation keeps the master name slot; only walking out clears it (0x6C60B4
// is the mode!=1 arm of `cmp [ebp-4],1` at 0x6C6099).
var teardown = Slice(leave, "ApplyNativeLeaveMasterSelfTeardown(int mode",
    "/// <summary>");
Assert(teardown.Contains("恭喜：你成功出师！"),
    "mode 1 must send the 0x6C6138 string");
Assert(teardown.IndexOf("恭喜：你成功出师！", StringComparison.Ordinal)
       < teardown.IndexOf("m_sMasterName = string.Empty;",
           StringComparison.Ordinal),
    "0x6C6099: graduation must NOT clear the master name slot; only the "
    + "mode-0 arm reaches 0x6C60B4");

// Record-blob mirrors: 0xDC/0xDF have DTO members, 0xE1 does NOT and would be
// clone-carried stale without an explicit store.
Equal(0x00DC, TPlayObjectOffset("NativeStudentFlagRecordOffset"),
    "obj+0xB95 -> rec 0xDC (SAVE 0x6B1210/0x6B1216)");
Equal(0x00DF, TPlayObjectOffset("NativeStudentOrderRecordOffset"),
    "obj+0xB96 -> rec 0xDF (SAVE 0x6B121C/0x6B1222)");
Equal(0x00E1, TPlayObjectOffset("NativeStudentAuxRecordOffset"),
    "obj+0xB98 -> rec 0xE1 (SAVE 0x6B1240/0x6B1246)");

// ---------------------------------------------------------------------------
// 7. Runtime behaviour over the four native paths.
// ---------------------------------------------------------------------------
CheckOnlineWalkOutClearsMasterSlot();
CheckOnlineGraduationKeepsMasterSlotAndLatches();
CheckEntryGateSkipsEverything();
CheckStudentCountZeroSkipsSlotScan();
CheckOfflineMasterEmitsFrameNotNoop();
CheckTeardownZeroesAuxRecordByte();

// 0x6C5F37 je 0x6C5FD0 -- an offline master must still reach the 0x0152 emitter
// (subcmd 1 for mode 0, subcmd 4 for mode 1).  The frame goes to DBServer, which
// this harness cannot observe, so assert on the recorded branch selector.  The
// old C# called GetPlayObject and did nothing on null, stranding the student in
// the offline master's slot forever; that regression must not come back.
void CheckOfflineMasterEmitsFrameNotNoop()
{
    foreach (var (mode, expected) in new[]
             {
                 (0, (int)NativeMasterRelationFrameCodec.StudentLeftSubcommand),
                 (1, (int)NativeMasterRelationFrameCodec.StudentGraduatedSubcommand),
             })
    {
        var student = NewPlayer("orphan" + mode);
        student.m_boStudent = true;
        student.m_sMasterName = "ghostmaster";   // never Register()ed -> offline
        SetSlotLengthByte(student, TPlayObject.NativeMasterSlotOffset, 11);

        student.NativeLeaveMaster(mode);

        Equal(expected, student.LastNativeMasterRelationSubcommand,
            $"offline master, mode {mode}: 0x6C6039 must select subcmd "
            + $"{expected} and emit -- not silently no-op");
        Assert(!student.m_boStudent,
            $"offline master, mode {mode}: teardown must still run");
    }

    // An ONLINE master must NOT emit the frame (native mutates memory instead).
    var master = NewPlayer("master5");
    var pupil = NewPlayer("student5");
    master.m_sStudentNames[0] = "student5";
    master.m_nStudentCount = 1;
    SetStudentSlotLengthByte(master, 0, 8);
    pupil.m_boStudent = true;
    pupil.m_sMasterName = "master5";
    SetSlotLengthByte(pupil, TPlayObject.NativeMasterSlotOffset, 7);
    Register(master);

    pupil.NativeLeaveMaster(0);

    Equal(-1, pupil.LastNativeMasterRelationSubcommand,
        "online master must NOT emit 0x0152 -- 0x6C5F3D mutates in memory");
}

// 0x6C6068 mov byte [ebx+0xB98],0 -> record 0xE1.  0xE1 has no DTO member, so
// the codec clone-carries it: without an explicit store the stale value survives.
void CheckTeardownZeroesAuxRecordByte()
{
    var student = NewPlayer("student6");
    student.m_boStudent = true;
    student.m_sMasterName = string.Empty;   // straight to teardown (0x6C5F1E)
    student.m_btStudentOrder = 9;
    student.m_NativeHumanData[0x00DC] = 1;
    student.m_NativeHumanData[0x00DF] = 9;
    student.m_NativeHumanData[0x00E1] = 0x7F;
    // A neighbour byte that native does NOT touch, to prove the store is not a
    // blanket wipe of the region.
    student.m_NativeHumanData[0x00E2] = 0x5A;

    student.NativeLeaveMaster(0);

    Equal(0, RecordByte(student, 0x00DC), "0x6C605A -> rec 0xDC");
    Equal(0, RecordByte(student, 0x00DF), "0x6C6061 -> rec 0xDF");
    Equal(0, RecordByte(student, 0x00E1), "0x6C6068 -> rec 0xE1");
    Equal(0x5A, RecordByte(student, 0x00E2),
        "rec 0xE2 is not part of the teardown and must be left alone");
}

void CheckOnlineWalkOutClearsMasterSlot()
{
    var master = NewPlayer("master");
    var student = NewPlayer("student");
    master.m_sStudentNames[0] = "student";
    master.m_nStudentCount = 1;
    SetStudentSlotLengthByte(master, 0, 7);
    student.m_boStudent = true;
    student.m_sMasterName = "master";
    SetSlotLengthByte(student, TPlayObject.NativeMasterSlotOffset, 6);
    Register(master);

    student.NativeLeaveMaster(0);

    Equal(0, master.m_nStudentCount, "walk-out: 0x6C5F5E storedCount--");
    Equal("", master.m_sStudentNames[0], "walk-out: slot 0 cleared");
    Assert(!master.m_boMaster,
        "walk-out must NOT latch obj+0xB91 -- that is the mode-1 arm at 0x6C5F93");
    Assert(!student.m_boStudent, "walk-out: 0x6C605A boStudent := false");
    Equal(0, (int)student.m_btStudentOrder, "walk-out: 0x6C6061 order := 0");
    Equal("", student.m_sMasterName, "walk-out: 0x6C60B4 clears the name");
    Equal(0, SlotLengthByte(student, TPlayObject.NativeMasterSlotOffset),
        "walk-out: 0x6C60B4 zeroes the master slot length byte");
    Equal(0, RecordByte(student, 0x00E1),
        "walk-out: 0x6C6068 zeroes obj+0xB98 -> rec 0xE1");
}

void CheckOnlineGraduationKeepsMasterSlotAndLatches()
{
    var master = NewPlayer("master2");
    var student = NewPlayer("student2");
    master.m_sStudentNames[0] = "student2";
    master.m_nStudentCount = 1;
    SetStudentSlotLengthByte(master, 0, 8);
    var before = ApprenticeNum(master);
    student.m_boStudent = true;
    student.m_sMasterName = "master2";
    SetSlotLengthByte(student, TPlayObject.NativeMasterSlotOffset, 7);
    Register(master);

    student.NativeLeaveMaster(1);

    Assert(master.m_boMaster, "graduation: 0x6C5F93 latches obj+0xB91");
    Equal(before + 1, ApprenticeNum(master),
        "graduation: 0x6C5FB4 inc dword [esi+0xBF4] -> rec 0x174");
    Assert(!student.m_boStudent, "graduation: teardown still runs");
    Equal("master2", student.m_sMasterName,
        "graduation must KEEP the master name -- 0x6C6099 skips 0x6C60B4");
    Equal(7, SlotLengthByte(student, TPlayObject.NativeMasterSlotOffset),
        "graduation must leave the master slot length byte intact");
}

void CheckEntryGateSkipsEverything()
{
    var student = NewPlayer("student3");
    student.m_boStudent = false;
    student.m_sMasterName = "ghost";
    student.m_btStudentOrder = 3;

    student.NativeLeaveMaster(0);

    Equal("ghost", student.m_sMasterName,
        "0x6C5EF8 je 0x6C60BB: a non-student must be left completely alone");
    Equal(3, (int)student.m_btStudentOrder,
        "0x6C5EF8: entry gate must skip the 0x6C6061 order store too");
}

void CheckStudentCountZeroSkipsSlotScan()
{
    // sub_6C614C 0x6C6173 `cmp byte [esi+0xB97],0` / 0x6C617A `jbe` is UNSIGNED:
    // a stored count of 0 skips the scan even though slot 0 holds the name, so
    // the master keeps its (stale) slot and only the student tears down.
    var master = NewPlayer("master4");
    var student = NewPlayer("student4");
    master.m_sStudentNames[0] = "student4";
    master.m_nStudentCount = 0;
    SetStudentSlotLengthByte(master, 0, 8);
    student.m_boStudent = true;
    student.m_sMasterName = "master4";
    SetSlotLengthByte(student, TPlayObject.NativeMasterSlotOffset, 7);
    Register(master);

    student.NativeLeaveMaster(0);

    Equal("student4", master.m_sStudentNames[0],
        "0x6C617A jbe: count 0 must skip the slot scan (slot survives)");
    Assert(!master.m_boMaster, "count 0: no latch");
    Assert(!student.m_boStudent,
        "count 0: student-side teardown still runs (0x6C5F4C je 0x6C605A)");
}

// ---------------------------------------------------------------------------
// 14) Notice colour: native sends cx=0xFCFF at 0x6C5FBA (master notice) and
//     0x6C609F (self 恭喜 line).  cx packs as FColor = cx & 0xFF and
//     BColor = cx >> 8 (see the playernotice bridge in PasApiBridge), i.e.
//     FColor 0xFF / BColor 0xFC.  In GameSvrConfig that is the *Blue* pair
//     (btBlueMsgFColor 0xFF / btBlueMsgBColor 0xFC); MsgColor.Red is 0x38FF
//     (FColor 0xFF / BColor 0x38) and would be the wrong channel.  These
//     assertions pin the derivation, not just the literal, so the constants
//     cannot drift apart silently.
// ---------------------------------------------------------------------------
{
    const int NativeNoticeCx = 0xFCFF;
    var config = new GameSvr.GameSvrConfig();
    Equal(NativeNoticeCx & 0xFF, (int)config.btBlueMsgFColor,
        "0xFCFF low byte == btBlueMsgFColor (MsgColor.Blue is the 0xFCFF channel)");
    Equal((NativeNoticeCx >> 8) & 0xFF, (int)config.btBlueMsgBColor,
        "0xFCFF high byte == btBlueMsgBColor");
    Assert(config.btRedMsgBColor != ((NativeNoticeCx >> 8) & 0xFF),
        "MsgColor.Red must NOT collide with the 0xFCFF notice channel");

    var leaveMasterSource = Read("GameSvr/Players/TPlayObject.NativeLeaveMaster.cs");
    Assert(leaveMasterSource.Contains("master.SysMsg(sayMsg, MsgColor.Blue, MsgType.Hint);",
            StringComparison.Ordinal),
        "0x6C5FBA master notice must use the Blue (0xFCFF) channel, not Red (0x38FF)");
    Assert(leaveMasterSource.Contains(
            "SysMsg(\"恭喜：你成功出师！\", MsgColor.Blue, MsgType.Hint);",
            StringComparison.Ordinal),
        "0x6C609F self graduation line must use the Blue (0xFCFF) channel");
    // Scan CODE lines only -- the comments above each call deliberately name
    // MsgColor.Red to explain why it is wrong, and matching those would be the
    // Forbid-scanned-a-comment false red this project has hit before.
    var redSendLines = leaveMasterSource
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
                       && !line.StartsWith("///", StringComparison.Ordinal)
                       && line.Contains("MsgColor.Red", StringComparison.Ordinal))
        .ToArray();
    Equal(0, redSendLines.Length,
        "no 0xFCFF site in NativeLeaveMaster may send on the Red channel"
        + (redSendLines.Length > 0 ? " -- found: " + redSendLines[0] : ""));
}

// ---------------------------------------------------------------------------
// 15) @LeaveTech (dispatch 125, perm 4, case@0x006252B0 -> sub_6C5E08) must stay
//     wired to the shared dissolve core with edx = 0.  Mode 1 would make the GM
//     command graduate the student (latch the master's 0xB91, bump 0xBF4 and KEEP
//     the master name) instead of walking them out -- native's `xor edx,edx` at
//     0x6C5E32 is unambiguous.  Also guards against the file reverting to
//     fail-closed, which the command census cannot see (it only forbids
//     NativeCommandFailure.Report in files it already lists as implemented).
// ---------------------------------------------------------------------------
{
    var commandSource = Read("GameSvr/Command/Commands/LeaveTechCommand.cs");
    Assert(commandSource.Contains("target.NativeLeaveMaster(0);", StringComparison.Ordinal),
        "@LeaveTech must call the shared dissolve core with mode 0 (0x6C5E32 xor edx,edx)");
    Assert(!commandSource.Contains("NativeLeaveMaster(1)", StringComparison.Ordinal),
        "@LeaveTech must NOT graduate the student -- 0x6C5E32 passes edx = 0");
    Assert(!commandSource.Contains("NativeCommandFailure.Report", StringComparison.Ordinal),
        "@LeaveTech reverted to fail-closed even though sub_6C5E08 is reversed");
    Assert(commandSource.Contains("target == null || !target.m_boStudent", StringComparison.Ordinal),
        "@LeaveTech must collapse not-found and not-student onto one 0x6C5E63 line");
    // 0x6C5E3B then 0x6C5E4E: the accepted line goes to the target AND the GM.
    var acceptedSends = System.Text.RegularExpressions.Regex.Matches(
        commandSource, @"SysMsg\(NativeAcceptedMsg,").Count;
    Equal(2, acceptedSends,
        "0x6C5E48 + 0x6C5E5B send the accepted line twice (target, then GM)");
    Assert(commandSource.Contains("[GameCommand(\"LeaveTech\"", StringComparison.Ordinal)
           && System.Text.RegularExpressions.Regex.IsMatch(commandSource,
               "GameCommand\\(\"LeaveTech\",[\\s\\S]{0,120}?4\\)"),
        "@LeaveTech must keep dispatch registration at native permission 4");
}

// ---------------------------------------------------------------------------
// Report
// ---------------------------------------------------------------------------
if (failures.Count > 0)
{
    foreach (var failure in failures) Console.WriteLine("FAIL " + failure);
    throw new InvalidOperationException(
        $"NativeLeaveMaster audit failed with {failures.Count} finding(s)");
}

Console.WriteLine(
    "PASS NativeLeaveMaster sub_6C5EC8 4-entry(mode 0/1) "
    + "gate=0xB95@0x6C5EF1 offline=0x0152 subcmd1/4 online=in-memory "
    + "teardown=0xB95/0xB96/0xB98->rec 0xDC/0xDF/0xE1 "
    + "grad-keeps-name@0x6C6099 gold=0xC350 DecGold/IncGold->GoldChanged "
    + "legB-grad@0x6CCEBB dead-leg sub_7138CC not emitted "
    + "notice-cx=0xFCFF->Blue gm@LeaveTech=125/perm4/sub_6C5E08 mode0 x2-accepted");

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------
static int IndexOfPayload(byte[] frame)
{
    for (var i = 0; i + NativeMasterRelationFrameCodec.PayloadSize <= frame.Length; i++)
    {
        if (frame[i] == 0x52 && frame[i + 1] == 0x01) return i;
    }
    return -1;
}

static string Slice(string source, string from, string until)
{
    var start = source.IndexOf(from, StringComparison.Ordinal);
    if (start < 0) return string.Empty;
    var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
    return end < 0 ? source[start..] : source[start..end];
}

static int TPlayObjectOffset(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
    if (field == null) throw new InvalidOperationException(name + " missing");
    return (int)field.GetRawConstantValue();
}

static void PrepareRuntimeFiles()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static TPlayObject NewPlayer(string name)
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    player.m_PEnvir = new Envirnoment { Flag = new TMapFlag() };
    player.m_MsgList = new List<SendMessage>();
    // RefShowName -> SendRefMsg walks the visible-actor list off the map grid.
    player.m_VisibleHumanList = new List<TBaseObject>();
    player.m_sCharName = name;
    player.m_sUserID = "acct";
    player.m_sStudentNames = new string[5];
    for (var i = 0; i < 5; i++) player.m_sStudentNames[i] = string.Empty;
    // A record blob long enough to hold the social block and the scalars.
    player.m_NativeHumanData = new byte[0x0800];
    player.m_btRaceServer = Grobal2.RC_PLAYOBJECT;
    // GetUninitializedObject skips field initializers, so the "never emitted"
    // sentinel has to be set by hand here (it would otherwise read 0, which is
    // also a valid subcommand and would mask a missing emit).
    player.LastNativeMasterRelationSubcommand = -1;
    return player;
}

// GetPlayObject walks UserEngine.m_PlayObjectList. Build the engine without its
// ctor (which touches config/threads) and inject the list directly.
static void Register(TPlayObject player)
{
    if (M2Share.UserEngine == null)
    {
        var engine = (UserEngine)RuntimeHelpers.GetUninitializedObject(
            typeof(UserEngine));
        var listField = typeof(UserEngine).GetField("m_PlayObjectList",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (listField == null)
            throw new InvalidOperationException("m_PlayObjectList missing");
        listField.SetValue(engine, new List<TPlayObject>());
        M2Share.UserEngine = engine;
    }
    var list = (IList<TPlayObject>)typeof(UserEngine)
        .GetField("m_PlayObjectList", BindingFlags.Instance | BindingFlags.NonPublic)
        .GetValue(M2Share.UserEngine);
    list.Add(player);
}

static void SetSlotLengthByte(TPlayObject player, int recordOffset, byte length)
{
    player.m_NativeHumanData[recordOffset] = length;
}

static int SlotLengthByte(TPlayObject player, int recordOffset)
    => player.m_NativeHumanData[recordOffset];

static void SetStudentSlotLengthByte(TPlayObject player, int index, byte length)
    => player.m_NativeHumanData[TPlayObject.NativeStudentSlotBaseOffset
        + index * TPlayObject.NativeSocialSlotStride] = length;

static int RecordByte(TPlayObject player, int offset)
    => player.m_NativeHumanData[offset];

static int ApprenticeNum(TPlayObject player)
    => BitConverter.ToInt32(player.m_NativeHumanData, 0x0174);
