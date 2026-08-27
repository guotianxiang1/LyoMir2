// Soul-wash recompute (sub_747CF4) - Zhanshen equivalence audit.
//
// Native, in order:
//   0x747CFF  cmp [ebx+0x610],0 / jg 0x747D24
//   0x747D08    byte[ebx+0x5BC]=0 ; [ebx+0x59C]=0 ; [ebx+0x5A0]=0 ; return
//               (note: [ebx+0x5A4] is NOT touched on this leg)
//   0x747D24  byte[ebx+0x5BC]=0 ; esi=0
//   0x747D2D  loop: if word[ebx+esi*2+0x5A8] <> 0 -> inc byte[ebx+0x5BC]
//   0x747D40    eax = movzx byte[ebx+0x5BC]
//   0x747D46    edx = [0x7D5AEC] ; cmp eax,[edx] ; jle 0x747D71 (next slot)
//   0x747D50    eax = 10 - esi ; edx = eax*2
//   0x747D5B    FillChar(&word[ebx+esi*2+0x5A8], edx, 0)
//   0x747D69    dec byte[ebx+0x5BC] ; break
//   0x747D77  esi=0 ; [ebx+0x59C]=0
//   0x747D81  [ebx+0x59C] += popcount([ebx+0x60C] & 0x555555) << 2 * 5   (= *20)
//   0x747D9D  [ebx+0x59C] += popcount([ebx+0x60C] & 0xAAAAAA) << 3 * 5   (= *40)
//   0x747DB9  [ebx+0x59C] += esi                                        (esi==0, no-op)
//   0x747DBF  cmp byte[ebx+0x5BC],0 / jbe 0x747E25 -> [ebx+0x5A0]=0
//   0x747DC8  else: zero a 10-word scratch, compact the non-zero slots into it
//   0x747DFB    call 0x747B38(self, count=byte[ebx+0x5BC], scratch, 0x14)
//   0x747E11    jl 0x747E1B -> [ebx+0x5A0]=0     ; a table miss is NOT fatal
//   0x747E13    else [ebx+0x5A0]=result
//   0x747E2D  if [ebx+0x5A4] < 0 -> [ebx+0x5A4]=0
//   0x747E3E  if [ebx+0x5A4]+[ebx+0x5A0] > [ebx+0x59C]
//   0x747E56     -> [ebx+0x5A4] = [ebx+0x59C] - [ebx+0x5A0]
//
// The truncation at 0x747D50 rewrites the persisted slot window, so it has to be
// observable in the SM 4033 body the very same call emits.
using System.Buffers.Binary;
using System.Reflection;
using DBSvr.Core;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntime();

    CheckStoredPrereqIsHealedSoTheZeroLegIsDead();
    CheckCapFormula();
    CheckSlotCountAndBase();
    CheckTableMissYieldsZeroBaseInsteadOfSilence();
    CheckTruncationAtTheGlobalCap();
    CheckTruncationWipesTrailingSlotsEvenWhenZero();
    CheckNegativeCurrentIsFloored();
    CheckCurrentIsClampedToCapMinusBase();
    CheckStateFrameLayout();
    CheckTagMarksTheHeroRace();

    Console.WriteLine(
        "PASS NativeSoulWashRecompute stored prereq<=0 healed to 1 (0x747D08 dead); " +
        "cap = 20*popcount(mask&0x555555) + 40*popcount(mask&0xAAAAAA); " +
        "count>cap -> FillChar from the offending slot to slot 9 then count--; " +
        "0x747B38 miss -> base 0 (not silence); current clamped to [0, cap-base]; " +
        "SM 4033 body = {cur,base,cap,word[10] slots} post-truncation");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeSoulWashRecomputeCheck FAIL: {exception}");
    return 1;
}

// ---------------------------------------------------------------------------
// 0x747CFF: the prereq gate.
// ---------------------------------------------------------------------------

static void CheckStoredPrereqIsHealedSoTheZeroLegIsDead()
{
    LoadConfig(null);

    // The decoder heals a stored prereq of 0 or less to 1 (0x6B060A `test eax,eax` /
    // `jg` then 0x6B0611 `mov [obj+0x610],1`), so after login [+0x610] is always >= 1
    // and 0x747D08 is unreachable. Both of these must therefore behave identically.
    var healed = Recompute(prereq: 0, mask: 0x000003u, current: 5, slots: NoSlots());
    var positive = Recompute(prereq: 1, mask: 0x000003u, current: 5, slots: NoSlots());

    Equal(positive.Cap, healed.Cap, "stored 0 is healed to 1, so the cap is computed");
    Equal(60, healed.Cap, "the healed record takes the normal 0x747D24 path");
    Equal(positive.Current, healed.Current, "healed and stored-1 agree on the current");

    var negative = Recompute(prereq: -9, mask: 0x000003u, current: 5, slots: NoSlots());
    Equal(60, negative.Cap, "a negative stored prereq is healed the same way");
}

// ---------------------------------------------------------------------------
// 0x747D81..0x747DB3: the cap formula.
// ---------------------------------------------------------------------------

static void CheckCapFormula()
{
    // bit 0 is an even bit -> 20; bit 1 is an odd bit -> 40.
    Equal(20, Recompute(1, 0x000001u, 0, NoSlots()).Cap, "one even bit -> 20");
    Equal(40, Recompute(1, 0x000002u, 0, NoSlots()).Cap, "one odd bit -> 40");
    Equal(60, Recompute(1, 0x000003u, 0, NoSlots()).Cap, "both -> 60");

    // The masks only span bits 0..23, so anything above is ignored outright.
    Equal(0, Recompute(1, 0xFF000000u, 0, NoSlots()).Cap, "bits 24..31 are outside both masks");

    // All 24 modelled bits: 12 even * 20 + 12 odd * 40 = 720.
    Equal(720, Recompute(1, 0xFFFFFFu, 0, NoSlots()).Cap, "all 24 bits -> 720");
}

// ---------------------------------------------------------------------------
// 0x747D2D..0x747D38 and 0x747DFB: the slot count drives the base sum.
// ---------------------------------------------------------------------------

static void CheckSlotCountAndBase()
{
    LoadConfig(slotCapLine: null, "1|5|0|x", "2|50|0|y", "3|7|0|z");

    Equal(0, Recompute(1, 0xFFFFFFu, 0, NoSlots()).Base,
        "no slot -> 0x747B38 is never called (0x747DC6 jbe)");
    Equal(55, Recompute(1, 0xFFFFFFu, 0, new[] { 1, 2 }).Base, "base sums table[+4]");
    Equal(57, Recompute(1, 0xFFFFFFu, 0, new[] { 0, 3, 0, 2 }).Base,
        "gaps are compacted away before the sum (0x747DDB)");
}

static void CheckTableMissYieldsZeroBaseInsteadOfSilence()
{
    LoadConfig(slotCapLine: null, "1|5|0|x");

    var frame = Recompute(1, 0xFFFFFFu, 0, new[] { 1, 999 });
    Equal(0, frame.Base,
        "an id absent from the table makes 0x747B38 return -1 and 0x747E1B stores 0");
    Assert(frame.Sent, "the recompute still answers; the miss is not fail-closed");
}

// ---------------------------------------------------------------------------
// 0x747D40..0x747D69: the destructive slot-cap truncation.
// ---------------------------------------------------------------------------

static void CheckTruncationAtTheGlobalCap()
{
    // Cap is 4 whenever the file loaded at all (0x7553F9).
    LoadConfig(slotCapLine: "=4", "1|1|0|a", "2|2|0|b", "3|4|0|c", "4|8|0|d", "5|16|0|e");
    Equal(4, NativeShenYouAttributeConfig.Shared.SlotCap, "cap seeded to 4");

    var frame = Recompute(1, 0xFFFFFFu, 0, new[] { 1, 2, 3, 4, 5 });
    AssertSlots(frame, new[] { 1, 2, 3, 4, 0, 0, 0, 0, 0, 0 },
        "the fifth non-zero slot and everything after it is wiped");
    Equal(1 + 2 + 4 + 8, frame.Base, "the wiped slot no longer contributes to the base");

    // Exactly at the cap nothing is touched.
    var atCap = Recompute(1, 0xFFFFFFu, 0, new[] { 1, 2, 3, 4 });
    AssertSlots(atCap, new[] { 1, 2, 3, 4, 0, 0, 0, 0, 0, 0 }, "count == cap survives");
}

static void CheckTruncationWipesTrailingSlotsEvenWhenZero()
{
    LoadConfig(slotCapLine: "=4", "1|1|0|a", "2|2|0|b", "3|4|0|c", "4|8|0|d", "9|99|0|i");

    // The FillChar length is (10 - i) words from the OFFENDING slot, so slot 9 goes
    // even though the scan would never have reached it.
    var frame = Recompute(1, 0xFFFFFFu, 0,
        new[] { 1, 2, 3, 4, 5, 0, 0, 0, 0, 9 });
    AssertSlots(frame, new[] { 1, 2, 3, 4, 0, 0, 0, 0, 0, 0 },
        "FillChar spans slot i..9, not just the non-zero ones");
}

// ---------------------------------------------------------------------------
// 0x747E2D..0x747E58: the current-point clamp.
// ---------------------------------------------------------------------------

static void CheckNegativeCurrentIsFloored()
{
    LoadConfig(null);
    Equal(0, Recompute(1, 0x000001u, -5, NoSlots()).Current, "0x747E36 floors at zero");
}

static void CheckCurrentIsClampedToCapMinusBase()
{
    LoadConfig(null, "1|5|0|x");

    // cap 20, base 5 -> the ceiling is 15.
    Equal(15, Recompute(1, 0x000001u, 999, new[] { 1 }).Current, "0x747E56 clamps to cap-base");
    Equal(3, Recompute(1, 0x000001u, 3, new[] { 1 }).Current, "under the ceiling is untouched");

    // The clamp is a plain subtraction, so a base above the cap drives it negative.
    LoadConfig(null, "1|500|0|x");
    Equal(20 - 500, Recompute(1, 0x000001u, 0, new[] { 1 }).Current,
        "base > cap yields a negative current; native does not re-floor it");
}

// ---------------------------------------------------------------------------
// 0x74730C: the frame the recompute feeds.
// ---------------------------------------------------------------------------

static void CheckStateFrameLayout()
{
    LoadConfig(null, "1|5|0|x", "2|50|0|y");

    var frame = Recompute(1, 0x000003u, 9, new[] { 1, 2 });
    Equal((ushort)Grobal2.SM_4033, frame.Ident, "SM 4033 (0xFC1)");
    Equal(0x20, frame.Body.Length, "0x74735A push 0x20");
    Equal(frame.Current, BinaryPrimitives.ReadInt32LittleEndian(frame.Body.AsSpan(0)),
        "body+0x00 = [+0x5A4]");
    Equal(frame.Base, BinaryPrimitives.ReadInt32LittleEndian(frame.Body.AsSpan(4)),
        "body+0x04 = [+0x5A0]");
    Equal(frame.Cap, BinaryPrimitives.ReadInt32LittleEndian(frame.Body.AsSpan(8)),
        "body+0x08 = [+0x59C]");
    Equal(55, frame.Base, "base is the summed table value");
    Equal(60, frame.Cap, "cap is 20 + 40");
    Equal(5, frame.Current, "current clamped to 60 - 55");
}

static void CheckTagMarksTheHeroRace()
{
    LoadConfig(null);
    Equal((ushort)0, Recompute(1, 0u, 0, NoSlots(), race: Grobal2.RC_PLAYOBJECT).Tag,
        "player race -> Tag 0 (0x747343 cmp bl,0x36)");
    Equal((ushort)1, Recompute(1, 0u, 0, NoSlots(), race: 0x36).Tag, "hero race -> Tag 1");
}

// ---------------------------------------------------------------------------
// Harness.
// ---------------------------------------------------------------------------

static int[] NoSlots() => Array.Empty<int>();

static Frame Recompute(int prereq, uint mask, int current, int[] slots,
    byte race = Grobal2.RC_PLAYOBJECT)
{
    var player = new ProbePlayer
    {
        m_boOffLineFlag = true,
        m_sCharName = "probe",
        m_btRaceServer = race,
        m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize],
        m_NativeShenYouBlock = new byte[0x18]
    };

    // obj+0x60C <-> rec+0x580 and obj+0x610 <-> rec+0x57C (enc 0x6B13DF / 0x6B13EB).
    BinaryPrimitives.WriteUInt32LittleEndian(player.m_NativeHumanData.AsSpan(0x580), mask);
    BinaryPrimitives.WriteInt32LittleEndian(player.m_NativeHumanData.AsSpan(0x57C), prereq);

    BinaryPrimitives.WriteInt32LittleEndian(player.m_NativeShenYouBlock.AsSpan(0), current);
    for (var i = 0; i < slots.Length && i < 10; i++)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(
            player.m_NativeShenYouBlock.AsSpan(4 + i * 2), (ushort)slots[i]);
    }

    var message = new TProcessMessage
    {
        wIdent = Grobal2.CM_4127,
        nParam3 = 0,
        sMsg = string.Empty
    };
    var handler = typeof(TPlayObject).GetMethod("TryHandleSoulWashCm",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryHandleSoulWashCm missing");
    Assert((bool)handler.Invoke(player, new object[] { message }),
        "CM 4127 is claimed by the soul-wash dispatcher");

    if (player.RawSocketMessages.Count == 0)
        return new Frame { Sent = false, Player = player };

    var (packet, body) = player.RawSocketMessages[0];
    return new Frame
    {
        Sent = true,
        Player = player,
        Ident = packet.Ident,
        Tag = packet.Tag,
        Body = body,
        Current = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0)),
        Base = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)),
        Cap = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8))
    };
}

static void AssertSlots(Frame frame, int[] expected, string label)
{
    Assert(frame.Sent, label + " (no frame was sent)");
    for (var i = 0; i < expected.Length; i++)
    {
        // The wire body carries the post-truncation window, and so does the object.
        var onWire = BinaryPrimitives.ReadUInt16LittleEndian(frame.Body.AsSpan(12 + i * 2));
        var onObject = BinaryPrimitives.ReadUInt16LittleEndian(
            frame.Player.m_NativeShenYouBlock.AsSpan(4 + i * 2));
        Equal((ushort)expected[i], onWire, $"{label}: wire slot {i}");
        Equal((ushort)expected[i], onObject, $"{label}: persisted slot {i}");
    }
}

static NativeShenYouAttributeConfig LoadConfig(string slotCapLine, params string[] rows)
{
    var dir = FreshDir("rows");
    var file = Path.Combine(dir, "shenyou.txt");
    var lines = slotCapLine == null ? rows : rows.Prepend(slotCapLine).ToArray();
    File.WriteAllText(file, string.Join(Environment.NewLine, lines) + Environment.NewLine,
        HUtil32.GbkEncoding);
    File.WriteAllText(
        Path.Combine(dir, NativeShenYouAttributeConfig.AbilSwitchFileName),
        "[setup]" + Environment.NewLine + "ShenYouAbilSwitch=0" + Environment.NewLine,
        HUtil32.GbkEncoding);

    var config = NativeShenYouAttributeConfig.Shared;
    if (!config.Reload(file, dir, out var error))
        throw new InvalidOperationException("seed load failed: " + error);
    return config;
}

static void PrepareRuntime()
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

    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.LogonCostLogList = new System.Collections.ArrayList();

    // Every case seeds its own rows; start from a known-loaded table.
    LoadConfig(null);
}

static string FreshDir(string name)
{
    var root = Path.Combine(Path.GetTempPath(), "NativeSoulWashRecomputeCheck");
    Directory.CreateDirectory(root);
    var dir = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: {actual}, expected {expected}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class Frame
{
    internal bool Sent;
    internal ProbePlayer Player;
    internal ushort Ident;
    internal ushort Tag;
    internal byte[] Body = Array.Empty<byte>();
    internal int Current;
    internal int Base;
    internal int Cap;
}

sealed class ProbePlayer : TPlayObject
{
    internal List<(ClientPacket Packet, byte[] Body)> RawSocketMessages { get; } = new();

    internal override void SendSocket(ClientPacket defMsg, byte[] body)
        => RawSocketMessages.Add((defMsg, body));

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
    }
}
