// NativeDealEscrowExactCheck (TRADE-56/57/58/59/60) — pins the five escrow
// contracts reconciled on 2026-08-13 against the canonical image
// D:/loym2/staging/_reunpack_work/flat_image.bin (ImageBase 0x400000,
// capstone 5.0.7; scripts under staging/_trade3dis/, all re-runnable).
//
// The trade path is the highest-risk item/gold surface in the server, so every
// assertion below quotes the bytes it is standing on. If an assertion fails,
// re-read the bytes BEFORE relaxing the assertion (REPLICATION_RULES §4.17:
// never weaken an assertion to make the tool green).
//
// ---------------------------------------------------------------- TRADE-56
// ClientChangeDealGold = sub_6C4454. The zero-amount gate is `<= 0`, not `< 0`:
//   0x6C4477  85 F6                 test esi, esi        ; esi = nGold
//   0x6C4479  0F 8E C8 00 00 00     jle  0x6C4547        ; <= 0 -> failure arm
//   0x6C4547  80 7D FF 00           cmp  byte [ebp-1], 0 ; bo09 still 0
//   0x6C456B  66 BA AD 02           mov  dx, 0x2AD       ; SM_DEALCHGGOLD_FAIL
// With `< 0` the nGold==0 packet reached the success arm and emitted
// SM_DEALCHGGOLD_OK + SM_DEALREMOTECHGGOLD plus two m_DealLastTick refreshes,
// which also let a client stall the dwDealOKTime gate indefinitely.
//
// ---------------------------------------------------------------- TRADE-57
// GetBackDealItems = sub_6C4114 walks the escrow list DOWNWARD:
//   0x6C4128  8B F0 / 4E            mov esi,eax / dec esi      ; i := Count-1
//   0x6C4130  8B D6                 mov edx,esi
//   0x6C4145  E8 6E 09 D6 FF        call 0x424AB8              ; m_ItemList.Add
//   0x6C414A  4E / 83 FE FF / 75 E0 dec esi / cmp esi,-1 / jne 0x6C4130
// and then credits the escrow gold with a RAW add, deliberately NOT IncGold:
//   0x6C415B  8B 83 E0 06 00 00     mov eax,[ebx+0x6E0]
//   0x6C4161  01 83 5C 01 00 00     add [ebx+0x15C],eax
// The raw add is faithful (the gold was debited by the equally-raw
// 0x6C44D4 write when it was escrowed, so returning it cannot exceed the
// pre-escrow value). Converting it to IncGold would ALSO silently swallow the
// deposit whenever IncGold's `jle` / cap arm returns false.
//
// ---------------------------------------------------------------- TRADE-58
// The post-success clear is sub_6C4A98, invoked partner-first:
//   0x6C49A4  mov eax,[ebx+0xBAC] / call 0x6C4A98
//   0x6C49AF  mov eax,ebx         / call 0x6C4A98
// and its eighth and last statement is the bag-weight recompute:
//   0x6C4AF3  8B C3                 mov eax, ebx
//   0x6C4AF5  E8 EA 83 07 00        call 0x73CEE4
//   sub_73CEE4: call 0x73E8D4 / mov [ebx+0x2C4],eax / mov byte [ebx+0x458],1
// This repo maps sub_73CEE4 to WeightChanged() (same mapping at
// TBaseObject.Base.cs and HeroObject). Note sub_6C4114 (TRADE-57) has NO such
// call -- the recompute belongs to the success clear only.
//
// ---------------------------------------------------------------- TRADE-59
// sub_6C4580 gates 3 and 5 read +0x73, which is m_boGhost, NOT m_boDeath:
//   0x6C45BC  80 7B 73 00           cmp byte [ebx+0x73], 0
//   0x6C45D9  80 78 73 00           cmp byte [eax+0x73], 0
// Whole-image write-site census (REPLICATION_RULES §4.6, re-run this round):
//   `C6 43 73 01` -> 1 hit  @0x7680EF (MakeGhost/MarkDelete, never cleared)
//   `C6 43 74 01` -> 5 hits (0x60B31F 0x66A5E8 0x68168F 0x766323 0x7682BE)
//
// ---------------------------------------------------------------- TRADE-60
// ClientAddDealItem = sub_6C417C carries the no-trade classifier's return code
// in the failure packet's Recog, and the slot is written at exactly one site:
//   0x6C41A8  33 C0 / 89 45 F0      mov [ebp-0x10], 0
//   0x6C4235  89 45 F0              mov [ebp-0x10], eax    ; sub_78389C result
//   0x6C4238  83 7D F0 00 / 7F 56   cmp [ebp-0x10],0 / jg 0x6C4294
//   0x6C42A2  8B 4D F0              mov ecx, [ebp-0x10]    ; ecx = Recog
//   0x6C42A5  66 BA A4 02           mov dx, 0x2A4          ; SM_DEALADDITEM_FAIL
// Every other failure reason therefore reports Recog 0.

using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

int failures = 0;

void Check(bool condition, string label)
{
    if (condition)
    {
        return;
    }
    Console.WriteLine($"  FAIL: {label}");
    failures++;
}

string root = FindRepositoryRoot();
string operateFile = Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Operate.cs");
string playerFile = Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs");

Check(File.Exists(operateFile), "TPlayObject.Operate.cs exists");
Check(File.Exists(playerFile), "TPlayObject.cs exists");

string operate = File.Exists(operateFile) ? File.ReadAllText(operateFile) : string.Empty;
string player = File.Exists(playerFile) ? File.ReadAllText(playerFile) : string.Empty;

// Slice [start, next member) so "inside method X" is a real claim rather than a
// whole-file substring hit, and drop comment lines so a commented-out statement
// can never satisfy a gate.
string Body(string text, string startMarker, string endMarker)
{
    int a = text.IndexOf(startMarker, StringComparison.Ordinal);
    if (a < 0) return string.Empty;
    int b = text.IndexOf(endMarker, a, StringComparison.Ordinal);
    if (b <= a) return string.Empty;
    return text[a..b];
}

string Live(string body) => string.Join('\n', body.Split('\n')
    .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

// ------------------------------------------------- TRADE-56 (sub_6C4454 jle)
string chgGold = Live(Body(operate,
    "private void ClientChangeDealGold(int nGold)",
    "private void ClientDealEnd()"));
Check(chgGold.Length > 0, "TRADE-56: ClientChangeDealGold body located");
Check(chgGold.Contains("if (nGold <= 0)", StringComparison.Ordinal),
    "TRADE-56: zero-amount gate is `nGold <= 0` (native 0x6C4479 `jle`)");
Check(!chgGold.Contains("if (nGold < 0)", StringComparison.Ordinal),
    "TRADE-56: the old `nGold < 0` gate is gone");
// The deposit-may-only-rise gate (0x6C4463 `cmp esi,[ebx+0x6E0]` / 0x6C4469
// `jge`) must still precede it, and must cancel rather than reply.
int lowerIdx = chgGold.IndexOf("nGold < m_nDealGolds", StringComparison.Ordinal);
int zeroIdx = chgGold.IndexOf("nGold <= 0", StringComparison.Ordinal);
Check(lowerIdx >= 0 && zeroIdx > lowerIdx,
    "TRADE-56: the `nGold < m_nDealGolds` cancel still precedes the zero gate "
    + "(native 0x6C4463 before 0x6C4477)");
// 0x6C44D4 `mov [ebx+0x15C],eax` is a RAW write; it must not become IncGold.
Check(chgGold.Contains("m_nGold = m_nGold + m_nDealGolds - nGold",
        StringComparison.Ordinal),
    "TRADE-56: escrow re-balance stays the native raw write (0x6C44D4), not IncGold");

// ------------------------------------------ TRADE-57 (sub_6C4114 downward)
string getBack = Live(Body(player,
    "public void GetBackDealItems()",
    "public override string GeTBaseObjectInfo"));
Check(getBack.Length > 0, "TRADE-57: GetBackDealItems body located");
Check(getBack.Contains("for (var i = m_DealItemList.Count - 1; i >= 0; i--)",
        StringComparison.Ordinal),
    "TRADE-57: escrow return walks DOWNWARD (native 0x6C412A `dec esi` .. "
    + "0x6C414B `cmp esi,-1`)");
Check(!getBack.Contains("for (var i = 0; i < m_DealItemList.Count; i++)",
        StringComparison.Ordinal),
    "TRADE-57: the old ascending loop is gone");
Check(getBack.Contains("m_nGold += m_nDealGolds", StringComparison.Ordinal),
    "TRADE-57: escrow gold refund stays the native RAW add (0x6C4161), not IncGold");
Check(!getBack.Contains("IncGold", StringComparison.Ordinal),
    "TRADE-57: GetBackDealItems does not route the refund through IncGold");
Check(!getBack.Contains("WeightChanged", StringComparison.Ordinal),
    "TRADE-57: GetBackDealItems has no weight recompute (sub_6C4114 has no 0x73CEE4)");

// --------------------------- TRADE-58 / 59 / 60 (sub_6C4580 + sub_6C417C)
string dealEnd = Live(Body(operate,
    "private void ClientDealEnd()", "private void ClientGetMinMap()"));
Check(dealEnd.Length > 0, "TRADE-58/59: ClientDealEnd body located");

// TRADE-59: gates 3 and 5 read +0x73 == ghost.
Check(dealEnd.Contains("if (m_boGhost)", StringComparison.Ordinal),
    "TRADE-59: self gate reads m_boGhost (native 0x6C45BC `cmp byte [ebx+0x73],0`)");
Check(dealEnd.Contains("if (m_DealCreat.m_boGhost)", StringComparison.Ordinal),
    "TRADE-59: partner gate reads m_boGhost (native 0x6C45D9 `cmp byte [eax+0x73],0`)");
Check(!dealEnd.Contains("m_boDeath", StringComparison.Ordinal),
    "TRADE-59: no m_boDeath gate survives in ClientDealEnd (+0x74 is death, "
    + "+0x73 is ghost -- REPLICATION_RULES §4.6)");
// The mutual-pointer gate (0x6C45E3) is what keeps a dangling m_DealCreat from
// settling an already-refunded escrow twice. Losing it re-opens a dupe.
Check(dealEnd.Contains("m_DealCreat.m_DealCreat != this", StringComparison.Ordinal),
    "TRADE-59: the mutual-pointer gate (native 0x6C45E3 `cmp ebx,[eax+0xBAC]`) "
    + "is still present");

// TRADE-58: both post-success clears end with the weight recompute.
int clearIdx = dealEnd.IndexOf("PlayObject.m_boDealOK = false;", StringComparison.Ordinal);
Check(clearIdx >= 0, "TRADE-58: partner clear block located");
Check(dealEnd.Contains("PlayObject.WeightChanged();", StringComparison.Ordinal),
    "TRADE-58: partner clear ends with WeightChanged() (native 0x6C4AF5 call 0x73CEE4)");
// The self clear's WeightChanged() must come after the self m_boDealOK reset,
// mirroring sub_6C4A98's statement order.
int selfDealOk = dealEnd.LastIndexOf("m_boDealOK = false;", StringComparison.Ordinal);
int selfWeight = dealEnd.LastIndexOf("WeightChanged();", StringComparison.Ordinal);
Check(selfDealOk >= 0 && selfWeight > selfDealOk,
    "TRADE-58: self clear ends with WeightChanged() after m_boDealOK reset");
// Partner is cleared before self (native 0x6C49A4 then 0x6C49AF).
Check(clearIdx >= 0 && selfDealOk > clearIdx,
    "TRADE-58: partner is cleared before self (native 0x6C49A4 -> 0x6C49AF)");

// The two gold hand-overs must stay on IncGold: [0x6AC8C8+0x28C] == 0x6D791C,
// i.e. native reaches the capped credit through the vcall, and a raw `+=` here
// WOULD bypass `cmp ebx,[eax+0x68C] / jg` (the per-character m_nGoldMax gate).
Check(dealEnd.Contains("IncGold(m_nDealGolds)", StringComparison.Ordinal),
    "TRADE-58: self->partner gold uses IncGold (native 0x6C4847 `call [ecx+0x28C]`)");
Check(dealEnd.Contains("IncGold(m_DealCreat.m_nDealGolds)", StringComparison.Ordinal),
    "TRADE-58: partner->self gold uses IncGold (native 0x6C496D `call [ecx+0x28C]`)");
Check(!dealEnd.Contains("m_nGold +=", StringComparison.Ordinal) &&
      !dealEnd.Contains("m_nGold = m_nGold", StringComparison.Ordinal),
    "TRADE-58: ClientDealEnd never writes m_nGold directly (no m_nGoldMax bypass)");
// Both capacity ladders must stay ahead of every transfer: native checks all
// four at 0x6C46E4..0x6C4752 and jumps to the single DealCancel at 0x6C49B8.
int firstCap = dealEnd.IndexOf("BagCapacity.Of(this)", StringComparison.Ordinal);
int firstXfer = dealEnd.IndexOf("m_DealCreat.AddItemToBag", StringComparison.Ordinal);
Check(firstCap >= 0 && firstXfer > firstCap,
    "TRADE-58: all four capacity checks precede the first item hand-over "
    + "(there is no rollback after 0x6C475E -- the pre-checks are the only guard)");

// TRADE-60: the failure packet carries the classifier's code.
string addDeal = Live(Body(operate,
    "private void ClientAddDealItem(int nItemIdx, string sItemName)",
    "private void ClientDelDealItem"));
Check(addDeal.Length > 0, "TRADE-60: ClientAddDealItem body located");
Check(addDeal.Contains("SendDefMessage(Grobal2.SM_DEALADDITEM_FAIL, dealAddFailCode",
        StringComparison.Ordinal),
    "TRADE-60: SM_DEALADDITEM_FAIL Recog carries the classifier code "
    + "(native 0x6C42A2 `mov ecx,[ebp-0x10]`)");
Check(addDeal.Contains("var dealAddFailCode = 0;", StringComparison.Ordinal),
    "TRADE-60: the code slot starts at 0 (native 0x6C41A8), so every other "
    + "failure reason still reports Recog 0");
Check(addDeal.Contains("dealAddFailCode > 0", StringComparison.Ordinal),
    "TRADE-60: the reject test is `> 0` (native 0x6C4238 `jg`), not `!= 0`");
// The no-trade classifier itself must stay wired with mode 2.
Check(addDeal.Contains("NativeItemDropDestroy.TransferModeTrade", StringComparison.Ordinal),
    "TRADE-60: the escrow gate still calls sub_78389C with mode 2 (0x6C4229 "
    + "`mov edx,2`)");
// Native gates staging on the PARTNER's lock byte (0x6C41CE `cmp byte
// [eax+0x684],0`), never on the actor's own -- you may still edit your side
// after you confirmed, as long as the partner has not.
Check(addDeal.Contains("!m_DealCreat.m_boDealOK", StringComparison.Ordinal),
    "TRADE-60: staging is gated on the PARTNER's m_boDealOK (native 0x6C41CE), "
    + "not on the actor's own");
Check(addDeal.Contains("m_DealItemList.Count < 12", StringComparison.Ordinal),
    "TRADE-60: escrow slot cap stays 12 (native 0x6C425A `cmp dword [eax+8],0xC` / `jge`)");

// --------------------------------------------------- classifier return codes
// The Recog values TRADE-60 now forwards are produced here; if the ladder is
// renumbered the wire contract changes, so pin the two reachable codes.
// NativeItemDropDestroy is `internal static class` in namespace GameSvr, so it
// cannot be named at compile time from this assembly -- go through the runtime
// type. (REPLICATION_RULES §4.17: a `GetMethod` that silently returns null is a
// tool that passes without testing anything, so the null itself is asserted.)
Type classifierType = typeof(TPlayObject).Assembly
    .GetType("GameSvr.NativeItemDropDestroy");
Check(classifierType != null, "GameSvr.NativeItemDropDestroy type is resolvable");
MethodInfo classifier = classifierType?.GetMethod(
    "CheckTransferPermission",
    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
Check(classifier != null,
    "NativeItemDropDestroy.CheckTransferPermission is resolvable");
Check(classifier == null || classifier.GetParameters().Length == 3,
    "CheckTransferPermission(item, stdItem, mode) still takes three parameters");
// A null item is the "nothing to classify" case and must stay code 0, otherwise
// every unrelated staging failure would start reporting a non-zero Recog.
Check(classifier == null ||
      (int)classifier.Invoke(null, new object[] { null, null, 2 }) == 0,
    "CheckTransferPermission(null,null,2) == 0 so unrelated failures keep Recog 0");

MethodInfo classifierCore = classifierType?.GetMethod(
    "CheckTransferPermissionCore",
    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
Check(classifierCore != null && classifierCore.GetParameters().Length == 4,
    "sub_78389C core exposes mode 3's native cl flag");

int Classify(int mode, ushort reserved = 0, byte classFc = 0,
    byte mode3Flag = 0)
{
    if (classifierCore == null) return int.MinValue;
    var item = new TUserItem { NativeClassFc = classFc };
    var stdItem = new GoodItem { NativeReserved02 = reserved };
    return (int)classifierCore.Invoke(null,
        new object[] { item, stdItem, mode, mode3Flag });
}

Check(Classify(0) == 0, "sub_78389C mode 0 returns 0");
Check(Classify(1, 0x0100) == 2,
    "sub_78389C mode 1 maps std[+3]&1 to code 2");
Check(Classify(2, classFc: 1) == 3
      && Classify(2, 0x0200) == 3,
    "sub_78389C mode 2 maps +0xFC/std[+3]&2 to code 3");
Check(Classify(3, 0x0200, mode3Flag: 0) == 0
      && Classify(3, 0x0200, mode3Flag: 1) == 4,
    "sub_78389C mode 3 alone consumes cl and returns code 4");
Check(Classify(4, classFc: 1) == 5
      && Classify(4, 0x0200) == 5
      && Classify(4, 0x0400) == 5
      && Classify(4, 0x0080) == 5,
    "sub_78389C mode 4 owns the +0xFC/0200/0400/0080 ladder");
Check(Classify(5, 0x0020) == 6,
    "sub_78389C mode 5 maps std[+2]&0x20 to code 6");
Check(Classify(5, 0x0200) == 0
      && Classify(5, 0x0400) == 0
      && Classify(5, 0x0080) == 0
      && Classify(5, classFc: 1) == 0,
    "sub_78389C mode 5 must not execute mode 4's ladder");
Check(Classify(5, 0x0800) == 1
      && Classify(5, 0x4000) == 1,
    "sub_78389C common pre-ladder still returns code 1");
Check(Classify(6) == 0 && Classify(-1) == 0,
    "sub_78389C unsigned mode>5 path returns 0");

var wrapperItem = new TUserItem();
var wrapperStd = new GoodItem { NativeReserved02 = 0x0020 };
Check(classifier == null ||
      (int)classifier.Invoke(null,
          new object[] { wrapperItem, wrapperStd, 5 }) == 6,
    "drop wrapper selects jump-table index 5, not index 4");

// ----------------------------------------------------------- gold-cap anchor
// IncGold is the only capped credit; its two gates are 0x6D7924 `jle` (amount
// <= 0 rejected) and 0x6D7934 `jg` against [eax+0x68C] = the PER-CHARACTER
// m_nGoldMax. The deal-completion credits above depend on both.
MethodInfo incGold = typeof(TPlayObject).GetMethod("IncGold",
    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
Check(incGold != null, "TPlayObject.IncGold is resolvable");
Check(incGold != null && incGold.ReturnType == typeof(bool),
    "IncGold returns bool (native `mov eax,ecx` with ecx = 0/1)");
Check(typeof(TBaseObject).GetField("m_nGoldMax",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null,
    "m_nGoldMax is a per-actor field (native [eax+0x68C]), not a global");

Console.WriteLine(failures == 0
    ? "NativeDealEscrowExactCheck: PASS"
    : $"NativeDealEscrowExactCheck: FAIL ({failures})");
return failures == 0 ? 0 : 1;

// Anchor on the compile-time source path, not AppContext.BaseDirectory: the
// build output can be redirected outside the repository tree.
static string FindRepositoryRoot([CallerFilePath] string callerFilePath = "")
{
    var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
    while (dir != null &&
           !File.Exists(Path.Combine(dir.FullName, "LyoMir2.sln")))
    {
        dir = dir.Parent;
    }
    if (dir == null)
    {
        throw new InvalidOperationException("repository root not found");
    }
    return dir.FullName;
}
