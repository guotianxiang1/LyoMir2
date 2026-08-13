// NativeSlaveRestToggleCheck
//
// Pins the @Rest slave-rest switch, [owner+0x4C7], against the binary.
//
// The switch is a single byte on the OWNING PLAYER. It has exactly one writer in the whole
// image and five readers; a linear scan of every disp32 == 0x4C7 in 0x1000..0x7A0000 finds
// only these six real instructions (everything above 0x85xxxx decodes inside data):
//
//   0x623A73  80 B0 C7 04 00 00 01  xor byte [eax+0x4C7],1   WRITER, the @Rest arm
//   0x60ABBB  80 B8 C7 04 00 00 00  cmp byte [eax+0x4C7],0   TFieldHero-family Run sub_60AA20
//   0x666563  80 BB C7 04 00 00 00  cmp byte [ebx+0x4C7],0   sub_66622C, recall gate
//   0x66663E  80 B8 C7 04 00 00 00  cmp byte [eax+0x4C7],0   sub_66622C, think gate
//   0x6736FB  80 B8 C7 04 00 00 00  cmp byte [eax+0x4C7],0   sub_6736E0, the predicate
//   0x767337  80 B8 C7 04 00 00 00  cmp byte [eax+0x4C7],0   sub_7671F0, target filter
//
// The arm is dispatch index 27 of the single @-command switch sub_622820. Registry record
// @0x7B6394 in the stride-0x120 command table (name ShortString at +0, dispatch index dword
// at +0x18, required permission dword at +0x1C, help ShortString at +0x20):
//
//   0x7B6394  04 52 65 73 74 00 ...   len 4, 'Rest'
//   0x7B63AC  1B 00 00 00             +0x18 = 27
//   0x7B63B0  00 00 00 00             +0x1C = 0   (no permission requirement)
//   0x7B63B4  1A ...                  help '设置宠物休息或攻击？'
//   0x622B15  FF 24 B5 1C 2B 62 00    jmp dword [esi*4 + 0x622B1C]
//   0x622B1C + 27*4 = 0x622B88 = 42 3A 62 00 -> 0x623A42
//
// Arm body, in native order — this order is what the behavioural assertions below lock:
//
//   0x623A45  8B 80 FC 04 00 00     mov eax,[eax+0x4FC]      ; slave TList
//   0x623A4B  83 78 08 00           cmp dword [eax+8],0      ; TList.FCount
//   0x623A4F  7F 10                 jg  0x623A61
//   0x623A54  83 B8 A8 18 00 00 00  cmp dword [eax+0x18A8],0 ; dead field, see below
//   0x623A5B  0F 84 EB 7B 00 00     je  0x62B64C             ; silent no-op
//   0x623A64  8B 80 28 01 00 00     mov eax,[eax+0x128]      ; map flag record
//   0x623A6A  80 78 05 00           cmp byte [eax+5],0       ; the DARE flag
//   0x623A6E  75 48                 jne 0x623AB8             ; refuse, BEFORE the toggle
//   0x623A73  80 B0 C7 04 00 00 01  xor byte [eax+0x4C7],1   ; toggle; 1 = resting
//   0x623A7D  80 B8 C7 04 00 00 00  cmp byte [eax+0x4C7],0   ; receipt keyed on the NEW value
//   0x623A84  74 19                 je  0x623A9F
//   0x623A8A  BA D8 B8 62 00        mov edx,0x62B8D8         ; '下属行动: 休息'
//   0x623AA3  BA F0 B8 62 00        mov edx,0x62B8F0         ; '下属行动: 攻击'
//   0x623ABC  BA 08 B9 62 00        mov edx,0x62B908         ; '该地图无法使用'
//
// [player+0x18A8] is dead: the only store in the image is 0x6B3BC6 `mov [eax+0x18A8],edx`
// with edx zeroed one instruction earlier by 0x6B3BC4 `xor edx,edx`, so the second disjunct
// of the pre-gate can never open and the gate reduces to the slave count.
//
// [map+5] is the DARE map flag. The flag record base is the pointer the arm loads from
// [player+0x128]: sub_690300 does `0x69030B 8B 90 28 01 00 00 mov edx,[eax+0x128]` then
// `0x690315 66 83 BA C0 00 00 00 00 cmp word [edx+0xC0],0` against the already modelled
// LIMITHEROLEVEL word, so [map+n] and [flag+n] name the same base. Byte 5 has exactly two
// writers, both on a DARE arm of a map-flag parser:
//   0x774F55 mov edx,0x775C94 ('DARE') / 0x774F6A C6 43 05 01 mov byte [ebx+5],1
//                                        0x774F7B C6 43 05 00 mov byte [ebx+5],0
//   0x77616B mov edx,0x776BB8 ('DARE') / 0x77617C C6 43 05 01 mov byte [ebx+5],1
//
// Receipts are Delphi long strings; the length dword sits at ptr-4 and both toggle receipts
// use an ASCII colon 0x3A plus an ASCII space 0x20, not a full-width colon:
//   0x62B8D4 = 14, 0x62B8D8  CF C2 CA F4 D0 D0 B6 AF 3A 20 D0 DD CF A2
//   0x62B8EC = 14, 0x62B8F0  CF C2 CA F4 D0 D0 B6 AF 3A 20 B9 A5 BB F7
//   0x62B904 = 14, 0x62B908  B8 C3 B5 D8 CD BC CE DE B7 A8 CA B9 D3 C3
//
// The predicate sub_6736E0 is three conjuncts over the resolved owner:
//   0x6736E8  FF 92 B4 00 00 00     call dword [edx+0xB4]    ; resolve owner
//   0x6736F0  74 14                 je   0x673706            ; no owner        -> false
//   0x6736F2  80 B8 78 01 00 00 00  cmp  byte [eax+0x178],0  ; m_btRaceServer
//   0x6736F9  75 0B                 jne  0x673706            ; not a player    -> false
//   0x6736FB  80 B8 C7 04 00 00 00  cmp  byte [eax+0x4C7],0
//   0x673702  74 02                 je   0x673706            ; not resting     -> false
//   0x673704  B3 01                 mov  bl,1
// Owner resolution (VMT slot 0xB4) has three bodies: 0x6C185C is a bare `C3 ret` for
// TPlayer, i.e. Delphi `Result := Self`; 0x769910 returns nil when [self+0x38C] is nil and
// otherwise recurses into the master; 0x686BDC returns [hero+0x68C] for the hero classes.
//
// @RestHero (index 28, arm 0x623AD1) is a DIFFERENT command acting on the hero object
// [player+0xBB0] via sub_688650 and never touches [player+0x4C7].

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using GameSvr;
using SystemModule;

var checks = 0;

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    PrepareRuntimeFiles();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.PluginManager = null;

    // -----------------------------------------------------------------------------------
    // 1) Receipt strings, byte-exact against the Delphi long strings.
    // -----------------------------------------------------------------------------------
    AssertGbk(M2Share.sPetRest,
        new byte[] { 0xCF, 0xC2, 0xCA, 0xF4, 0xD0, 0xD0, 0xB6, 0xAF, 0x3A, 0x20, 0xD0, 0xDD, 0xCF, 0xA2 },
        "0x62B8D8 '下属行动: 休息'");
    AssertGbk(M2Share.sPetAttack,
        new byte[] { 0xCF, 0xC2, 0xCA, 0xF4, 0xD0, 0xD0, 0xB6, 0xAF, 0x3A, 0x20, 0xB9, 0xA5, 0xBB, 0xF7 },
        "0x62B8F0 '下属行动: 攻击'");
    AssertGbk(M2Share.sPetRestMapForbidden,
        new byte[] { 0xB8, 0xC3, 0xB5, 0xD8, 0xCD, 0xBC, 0xCE, 0xDE, 0xB7, 0xA8, 0xCA, 0xB9, 0xD3, 0xC3 },
        "0x62B908 '该地图无法使用'");

    // The separator really is ASCII, which is the part the old mangled literals lost. GBK byte
    // offsets 8 and 9 are UTF-16 char indices 4 and 5, the four leading hanzi being 2 bytes each.
    Equal(':', M2Share.sPetRest[4], "0x62B8D8 byte 8 is ASCII colon 0x3A");
    Equal(' ', M2Share.sPetRest[5], "0x62B8D8 byte 9 is ASCII space 0x20");
    Equal(':', M2Share.sPetAttack[4], "0x62B8F0 byte 8 is ASCII colon 0x3A");
    Equal(' ', M2Share.sPetAttack[5], "0x62B8F0 byte 9 is ASCII space 0x20");
    Equal(8, M2Share.sPetRest.Length, "0x62B8D8 is 14 GBK bytes = 8 UTF-16 chars");
    Equal(8, M2Share.sPetAttack.Length, "0x62B8F0 is 14 GBK bytes = 8 UTF-16 chars");
    Equal(7, M2Share.sPetRestMapForbidden.Length, "0x62B908 is 14 GBK bytes = 7 UTF-16 chars");

    // -----------------------------------------------------------------------------------
    // 2) Registry / dispatch geometry.
    // -----------------------------------------------------------------------------------
    var rest = NativeGmSystemCommands.Find("Rest");
    Equal(27, rest.DispatchIndex, "record 0x7B6394 +0x18 dispatch index");
    Equal(0, rest.RequiredPerm, "record 0x7B6394 +0x1C required permission");
    Equal(0x00623A42u, rest.HandlerAddress, "arm entry 0x623A42");
    Equal(0x00622B88u, rest.JumpSlotAddress, "jump-table slot 0x622B88");
    Equal(0x00622B1Cu, NativeSystemAdminCommand.JumpTableBase, "jump-table base 0x622B1C");
    Equal(0x00622B88u,
        NativeSystemAdminCommand.JumpTableBase + (uint)(27 * 4),
        "0x622B1C + 27*4 == 0x622B88");

    // -----------------------------------------------------------------------------------
    // 3) The map gate's flag is the DARE token, not a newly invented one.
    // -----------------------------------------------------------------------------------
    var sceneFlag = typeof(Maps).GetMethod("TryApplySceneFlag",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingMethodException(typeof(Maps).FullName, "TryApplySceneFlag");

    var dareFlag = new TMapFlag();
    Equal(true, (bool)sceneFlag.Invoke(null, new object[] { dareFlag, "DARE" }),
        "'DARE' is a recognised map token");
    Equal(true, dareFlag.boDARE, "'DARE' sets [flag+5]");

    var otherFlag = new TMapFlag();
    sceneFlag.Invoke(null, new object[] { otherFlag, "MONATTACK" });
    Equal(false, otherFlag.boDARE, "a neighbouring token must not set [flag+5]");
    Equal(false, new TMapFlag().boDARE, "[flag+5] default is 0 (zero-filled instance)");

    // -----------------------------------------------------------------------------------
    // 4) The arm: pre-gate, map gate, toggle, receipts — and their ORDER.
    // -----------------------------------------------------------------------------------

    // 0x623A4B/0x623A4F/0x623A5B: no slaves and the dead [+0x18A8] disjunct -> silent no-op.
    var noSlaves = NewPlayer();
    Invoke(noSlaves);
    Equal(false, noSlaves.m_boSlaveRelax, "pre-gate: no slaves must not toggle");
    Equal(0, noSlaves.m_MsgList.Count, "pre-gate: no slaves must send nothing");

    // The pre-gate runs before the map gate, so a DARE map with no slaves is still silent.
    var noSlavesOnDare = NewPlayer();
    noSlavesOnDare.m_PEnvir.Flag.boDARE = true;
    Invoke(noSlavesOnDare);
    Equal(false, noSlavesOnDare.m_boSlaveRelax, "pre-gate precedes the map gate: no toggle");
    Equal(0, noSlavesOnDare.m_MsgList.Count, "pre-gate precedes the map gate: no message");

    // 0x623A6E jne 0x623AB8 fires BEFORE 0x623A73, so the flag must survive untouched. Both
    // starting values are exercised: an implementation that toggled first and refused after
    // would pass one of these and fail the other.
    var blockedFromAttack = WithSlaves(NewPlayer());
    blockedFromAttack.m_PEnvir.Flag.boDARE = true;
    Invoke(blockedFromAttack);
    Equal(false, blockedFromAttack.m_boSlaveRelax, "DARE gate precedes the toggle (from attack)");
    Equal(1, blockedFromAttack.m_MsgList.Count, "DARE refusal sends exactly one message");
    Equal(Grobal2.RM_SYSMESSAGE, blockedFromAttack.m_MsgList[0].wIdent, "DARE refusal ident");
    Equal(M2Share.sPetRestMapForbidden, blockedFromAttack.m_MsgList[0].Buff,
        "DARE refusal text is 0x62B908");

    var blockedFromRest = WithSlaves(NewPlayer());
    blockedFromRest.m_boSlaveRelax = true;
    blockedFromRest.m_PEnvir.Flag.boDARE = true;
    Invoke(blockedFromRest);
    Equal(true, blockedFromRest.m_boSlaveRelax, "DARE gate precedes the toggle (from rest)");
    Equal(M2Share.sPetRestMapForbidden, blockedFromRest.m_MsgList[0].Buff,
        "DARE refusal text is 0x62B908 from the rest state too");

    // 0x623A73 is an xor, i.e. a true toggle in both directions, and 0x623A7D keys the
    // receipt on the value AFTER the flip.
    var player = WithSlaves(NewPlayer());
    Invoke(player);
    Equal(true, player.m_boSlaveRelax, "0x623A73 xor: attack -> rest");
    Equal(1, player.m_MsgList.Count, "rest receipt is exactly one message");
    Equal(Grobal2.RM_SYSMESSAGE, player.m_MsgList[0].wIdent, "rest receipt ident");
    Equal(M2Share.sPetRest, player.m_MsgList[0].Buff, "post-toggle 1 -> 0x62B8D8 receipt");

    Invoke(player);
    Equal(false, player.m_boSlaveRelax, "0x623A73 xor: rest -> attack");
    Equal(2, player.m_MsgList.Count, "attack receipt is exactly one more message");
    Equal(M2Share.sPetAttack, player.m_MsgList[1].Buff, "post-toggle 0 -> 0x62B8F0 receipt");

    // Order of the two receipts across a full cycle, so a swapped pair cannot pass.
    Equal(M2Share.sPetRest, player.m_MsgList[0].Buff, "cycle order: 休息 first");
    Equal(M2Share.sPetAttack, player.m_MsgList[1].Buff, "cycle order: 攻击 second");
    Assert(M2Share.sPetRest != M2Share.sPetAttack,
        "the two receipts must be distinct strings");

    // The receipt is unconditional once the pre-gate passed: it does not re-test the count.
    var oneSlave = WithSlaves(NewPlayer());
    Invoke(oneSlave);
    Equal(1, oneSlave.m_MsgList.Count, "receipt is sent whenever the toggle happened");

    // -----------------------------------------------------------------------------------
    // 5) sub_6736E0: the three conjuncts, in order.
    // -----------------------------------------------------------------------------------

    // Conjunct 1 — 0x6736F0: no owner at all.
    var orphan = NewMonster();
    Equal(false, orphan.IsMasterResting(), "predicate: no owner -> false");

    // 0x769910 returns nil, not the intermediate, when the chain ends at a masterless
    // creature, so a slave-of-a-slave-of-nobody is still ownerless.
    var midSlave = NewMonster();
    var deepOrphan = NewMonster();
    deepOrphan.m_Master = midSlave;
    Equal(false, deepOrphan.IsMasterResting(),
        "predicate: chain ending at a masterless creature -> false");

    // Conjunct 2 — 0x6736F9: the resolved owner's race must be RC_PLAYOBJECT (0).
    var wrongRaceOwner = NewPlayer();
    wrongRaceOwner.m_btRaceServer = Grobal2.RC_HEROOBJECT;
    wrongRaceOwner.m_boSlaveRelax = true;
    var ofWrongRace = NewMonster();
    ofWrongRace.m_Master = wrongRaceOwner;
    Equal(false, ofWrongRace.IsMasterResting(),
        "predicate: owner race != RC_PLAYOBJECT -> false even while resting");

    // Conjunct 3 — 0x673702: the owner's [+0x4C7].
    var owner = NewPlayer();
    var slave = NewMonster();
    slave.m_Master = owner;
    Equal(false, slave.IsMasterResting(), "predicate: owner not resting -> false");
    owner.m_boSlaveRelax = true;
    Equal(true, slave.IsMasterResting(), "predicate: owner resting -> true");

    // 0x769910 recursion: a two-level chain resolves to the same player.
    var inner = NewMonster();
    inner.m_Master = owner;
    var outer = NewMonster();
    outer.m_Master = inner;
    Equal(true, outer.IsMasterResting(), "predicate: two-level chain resolves to the player");

    // 0x6C185C is a bare `C3 ret`, i.e. TPlayer's owner is itself — asking a player directly
    // must report the player's own switch, not null.
    Equal(true, owner.IsMasterResting(), "predicate: TPlayer VMT[0xB4] returns Self (resting)");
    owner.m_boSlaveRelax = false;
    Equal(false, owner.IsMasterResting(), "predicate: TPlayer VMT[0xB4] returns Self (attacking)");
    Equal(false, slave.IsMasterResting(), "predicate tracks the owner's live value");

    // 0x686BDC returns [hero+0x68C], the owning player, which HeroObject.m_Master models.
    var heroOwner = NewPlayer();
    heroOwner.m_boSlaveRelax = true;
    var hero = (HeroObject)RuntimeHelpers.GetUninitializedObject(typeof(HeroObject));
    hero.m_btRaceServer = Grobal2.RC_HEROOBJECT;
    hero.m_Master = heroOwner;
    Equal(true, hero.IsMasterResting(), "predicate: hero resolves its owner through m_Master");
    heroOwner.m_boSlaveRelax = false;
    Equal(false, hero.IsMasterResting(), "predicate: hero owner not resting -> false");

    Console.WriteLine(
        $"PASS NativeSlaveRestToggleCheck checks={checks} " +
        "writer=0x623A73 arm=@Rest/idx27/perm0 mapgate=DARE([flag+5])" +
        "-before-toggle receipts=0x62B8D8/0x62B8F0/0x62B908 predicate=sub_6736E0/3-conjuncts");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeSlaveRestToggleCheck FAIL after {checks} checks: {exception}");
    return 1;
}

void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected=[{expected}], actual=[{actual}]");
}

void Assert(bool condition, string label)
{
    checks++;
    if (!condition)
        throw new InvalidOperationException(label);
}

void AssertGbk(string value, byte[] expected, string label)
{
    checks++;
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var actual = Encoding.GetEncoding(936).GetBytes(value ?? string.Empty);
    if (!actual.AsSpan().SequenceEqual(expected))
    {
        throw new InvalidOperationException(
            $"{label}: expected=[{Convert.ToHexString(expected)}] ({expected.Length} bytes), " +
            $"actual=[{Convert.ToHexString(actual)}] ({actual.Length} bytes) from \"{value}\"");
    }
}

static void Invoke(TPlayObject player) =>
    new ChangeSalveStatusCommand().ChangeSalveStatus(player);

static TPlayObject NewPlayer()
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(typeof(TPlayObject));
    player.m_PEnvir = new Envirnoment { Flag = new TMapFlag() };
    player.m_MsgList = new List<SendMessage>();
    player.m_SlaveList = new List<TBaseObject>();
    player.m_btRaceServer = Grobal2.RC_PLAYOBJECT;
    player.m_boSlaveRelax = false;
    return player;
}

static TPlayObject WithSlaves(TPlayObject player)
{
    player.m_SlaveList.Add(NewMonster());
    return player;
}

static Monster NewMonster()
{
    var monster = (Monster)RuntimeHelpers.GetUninitializedObject(typeof(Monster));
    monster.m_btRaceServer = Grobal2.RC_ANIMAL;
    return monster;
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
