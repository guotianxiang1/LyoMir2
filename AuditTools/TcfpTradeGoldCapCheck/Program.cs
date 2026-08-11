// TcfpTradeGoldCapCheck (TCFP-06) — pins the fact that both gold-credit
// sites in the CM_DEALOK commit path use IncGold() (the capped setter,
// sub_6D791C) and do NOT write m_nGold directly.
//
// Native truth (sub_6D791C, EA 0x6D791C, verified 2026-08-11 by reading
// D:/loym2/staging/_reunpack_work/flat_image.bin, ImageBase 0x400000,
// backing M2Server_reunpacked_20260803.exe):
//
//   prologue:  55 8B EC                       push ebp / mov ebp,esp
//   6D7920:    53                             push ebx
//   6D7921:    33 C9                          xor ecx,ecx    (return false accumulator)
//   6D7923:    85 D2                          test edx,edx   (delta)
//   6D7925:    7E 1D                          jle +0x1D      (skip if delta<=0)
//   6D7927:    8B 98 5C 01 00 00              mov ebx,[eax+0x15C]   (GoldNum)
//   6D792D:    03 DA                          add ebx,edx
//   6D792F:    3B 98 8C 06 00 00              cmp ebx,[eax+0x68C]   (MaxLimitGold)
//   6D7935:    7F 0D                          jg +0x0D       (skip if over cap)
//   6D7937:    01 90 5C 01 00 00              add [eax+0x15C],edx   (GoldNum += delta)
//   6D793D:    E8 73 A0 FE FF                 call GoldChanged (0x6D01B4)
//   6D7942:    B1 01                          mov cl,1       (success)
//   6D7944:    8B C1                          mov eax,ecx    (return cl)
//   6D7946:    5B                             pop ebx
//   6D7947:    5D                             pop ebp
//   6D7948:    C3                             ret
//
// Both gold credit sites in sub_6C4580 (op 1030 commit, 0x6C4580) call
// sub_6D791C to credit gold. A direct write bypasses the MaxLimitGold cap
// and is structurally incorrect: the commit body must contain NO direct
// writes to m_nGold.

using System.Runtime.CompilerServices;

int failures = 0;

void Check(bool condition, string label)
{
    if (condition) return;
    Console.WriteLine($"  FAIL: {label}");
    failures++;
}

string root = FindRepositoryRoot();
string operateSrc = File.ReadAllText(
    Path.Combine(root, "GameSvr", "Players", "TPlayObject.Operate.cs"));

// Slice the commit block: from the pre-flight cap-check to SM_DEALSUCCESS.
// This captures both gold-credit sites in the deal-commit success path.
int blockStart = operateSrc.IndexOf(
    "m_DealCreat.m_nGoldMax - m_DealCreat.m_nGold < m_nDealGolds",
    StringComparison.Ordinal);
int blockEnd = operateSrc.IndexOf(
    "SendDefMessage(Grobal2.SM_DEALSUCCESS, 0, 0, 0, 0, \"\");",
    blockStart < 0 ? 0 : blockStart, StringComparison.Ordinal);

Check(blockStart >= 0, "cap pre-check anchor found in Operate.cs");
Check(blockEnd > blockStart, "SM_DEALSUCCESS anchor found after cap pre-check");

string block = blockStart >= 0 && blockEnd > blockStart
    ? operateSrc[blockStart..blockEnd]
    : string.Empty;

// Strip comment lines so a commented-out bare += does not satisfy the negative gate.
string live = string.Join('\n', block.Split('\n')
    .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

// Negative control: bare direct write must NOT appear (either direction).
// Native sub_6C4580 has two `call sub_6D791C` (0x6C47D9 and 0x6C48F1);
// C# must mirror this by using IncGold(), not bare +=.
Check(!live.Contains("m_DealCreat.m_nGold +=", StringComparison.Ordinal),
    "deal commit: m_DealCreat.m_nGold += must NOT appear (bypasses cap)");
Check(!live.Contains("m_nGold += m_DealCreat", StringComparison.Ordinal),
    "deal commit: m_nGold += m_DealCreat must NOT appear (bypasses cap)");

// Positive control: capped setter must be called for both directions.
Check(live.Contains("(m_DealCreat as TPlayObject).IncGold(", StringComparison.Ordinal),
    "deal commit: (m_DealCreat as TPlayObject).IncGold( called for counterparty");
Check(live.Contains("IncGold(m_DealCreat.m_nDealGolds)", StringComparison.Ordinal),
    "deal commit: IncGold(m_DealCreat.m_nDealGolds) called for self");

Console.WriteLine(failures == 0
    ? "TcfpTradeGoldCapCheck: PASS"
    : $"TcfpTradeGoldCapCheck: FAIL ({failures})");
return failures == 0 ? 0 : 1;

static string FindRepositoryRoot([CallerFilePath] string callerFilePath = "")
{
    var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LyoMir2.sln")))
        dir = dir.Parent;
    if (dir == null)
        throw new InvalidOperationException("repository root not found");
    return dir.FullName;
}
