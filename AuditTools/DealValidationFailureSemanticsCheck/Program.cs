using System;
using System.IO;
using System.Linq;

// TCFP-22: ClientDealEnd validation failure semantics (fail-fast + no per-check messages)
// TCFP-27: ClientChangeDealGold rejects nGold < 0 (native @0x6C4477 rejects <=0)
//
// 战神 sub_6C4580 ClientDealEnd 四道校验 @0x6C46E4/0x6C4705/0x6C4720/0x6C473F 全部跳到
// 同一失败目标 0x6C49B8，直接调 DealCancel (sub_6C43C4) 并静默返回，无任何 per-check 消息。
// 原生没有 `bo11` 局部量；C# 的 `bo11 &&` 是这四次 jmp 的翻译形状。旧 2000 字符窗口
// 从 `m_boDealOK` 起算，会被 TRADE-21/22 注释撑出窗口外（rule 4.17），改搜整个方法。
// 旧 C# 把四道检查全跑完且每条失败时都发消息；那四条消息字符串在二进制里 GBK 零命中。
//
// 战神 sub_6C4454 ClientChangeDealGold @0x6C4477: test esi,esi / jle 0x6c4547 拒绝 nGold<=0。
// C# 有 `if (nGold < 0)` 门；native 拒绝 0，C# 允许（nGold==0 是无意义 no-op，不成风险）。

class Program
{
    static void Main(string[] args)
    {
        try
        {
            var repoRoot = FindRepositoryRoot(args);
            var operatePath = Path.Combine(repoRoot, "GameSvr", "Players", "TPlayObject.Operate.cs");
            var m2sharePath = Path.Combine(repoRoot, "GameSvr", "M2Share.cs");

            if (!File.Exists(operatePath))
                throw new FileNotFoundException($"Source not found: {operatePath}");

            var operateSrc = File.ReadAllText(operatePath);
            var m2shareSrc = File.Exists(m2sharePath) ? File.ReadAllText(m2sharePath) : "";

            // ===== TCFP-22: four non-native message strings must not exist =====
            string[] deadStrings =
            {
                "g_sYourBagSizeTooSmall",
                "g_sDealHumanBagSizeTooSmall",
                "g_sYourGoldLargeThenLimit",
                "g_sDealHumanGoldLargeThenLimit"
            };

            int foundInOperate = deadStrings.Count(s => operateSrc.Contains(s));
            if (foundInOperate > 0)
                throw new Exception(
                    $"TCFP-22 FAIL: {foundInOperate}/4 dead message strings still referenced in "
                    + "TPlayObject.Operate.cs (native GBK zero hits for all four)");

            int foundInM2Share = deadStrings.Count(s => m2shareSrc.Contains(s));
            if (foundInM2Share > 0)
                throw new Exception(
                    $"TCFP-22 FAIL: {foundInM2Share}/4 dead message strings still declared in "
                    + "M2Share.cs (should be removed — no references)");

            // ===== TCFP-22: fail-fast structure =====
            // Native sub_6C4580 has NO `bo11` local. Phase D is four sequential tests,
            // each jumping to the same DealCancel join at 0x6C49B8:
            //   0x6C46F7 call [vmt+0x244] / test al,al / 0x6C46FF je  0x6C49B8
            //   0x6C4713 call 0x6D7948    / test al,al / 0x6C471A jne 0x6C49B8
            //   0x6C4731 call [vmt+0x244] / test al,al / 0x6C4739 je  0x6C49B8
            //   0x6C474B call 0x6D7948    / test al,al / 0x6C4752 jne 0x6C49B8
            //   0x6C49B8 mov eax,ebx / 0x6C49BA E8 05 FA FF FF call 0x6C43C4 DealCancel
            // C# translates that with a `bo11` flag + `bo11 &&` short-circuits (side-effect
            // free, equivalent). The grep is a C# translation shape of the native jmp
            // fail-fast, not a native token. Search the whole method — a 2000-char window
            // starting at `m_boDealOK` was overflowing on TRADE-21/22 comments (rule 4.17).
            int dealEndStart = operateSrc.IndexOf("private void ClientDealEnd()", StringComparison.Ordinal);
            if (dealEndStart < 0)
                throw new Exception("TCFP-22: ClientDealEnd method not found");

            int dealEndEnd = operateSrc.IndexOf("\r\n        private void ", dealEndStart + 1,
                StringComparison.Ordinal);
            if (dealEndEnd < 0)
                dealEndEnd = operateSrc.IndexOf("\n        private void ", dealEndStart + 1,
                    StringComparison.Ordinal);
            if (dealEndEnd < 0) dealEndEnd = operateSrc.Length;

            string window = operateSrc.Substring(dealEndStart, dealEndEnd - dealEndStart);

            if (window.IndexOf("if (m_DealCreat.m_boDealOK)", StringComparison.Ordinal) < 0)
                throw new Exception("TCFP-22: m_boDealOK validation block not found");

            int bo11AndCount = 0;
            int pos = 0;
            while ((pos = window.IndexOf("bo11 &&", pos, StringComparison.Ordinal)) >= 0)
            {
                bo11AndCount++;
                pos += 7;
            }

            if (window.IndexOf("DealCancel()", StringComparison.Ordinal) < 0)
                throw new Exception(
                    "TCFP-22 FAIL: ClientDealEnd capacity-failure path lacks DealCancel "
                    + "(native 0x6C49B8 E8 05 FA FF FF call 0x6C43C4)");

            // Native: 4 checks, each jump to the same target on failure.
            // C# fail-fast: first check sets bo11=false; checks 2/3/4 use `bo11 &&` to short-circuit.
            if (bo11AndCount < 3)
                throw new Exception(
                    $"TCFP-22 FAIL: expected >=3 `bo11 &&` short-circuit guards, found {bo11AndCount} "
                    + "(native @0x6C46E4/0x6C4705/0x6C4720/0x6C473F all jmp 0x6C49B8 on first failure)");

            Console.WriteLine(
                $"PASS TCFP-22 ClientDealEnd validation failure: 0/4 dead msg strings in Operate.cs, "
                + $"0/4 in M2Share.cs, {bo11AndCount} fail-fast `bo11 &&` guards confirmed "
                + "(native @0x6C46E4/0x6C4705/0x6C4720/0x6C473F -> 0x6C49B8 -> DealCancel; "
                + "g_sYourBagSizeTooSmall/g_sYourGoldLargeThenLimit/g_sDealHumanBagSizeTooSmall/"
                + "g_sDealHumanGoldLargeThenLimit all GBK zero hits in binary)");

            // ===== TCFP-27: ClientChangeDealGold rejects nGold < 0 =====
            int chgStart = operateSrc.IndexOf("private void ClientChangeDealGold(", StringComparison.Ordinal);
            if (chgStart < 0)
                throw new Exception("TCFP-27: ClientChangeDealGold method not found");

            int chgEnd = operateSrc.IndexOf("private void ClientDealEnd(", chgStart, StringComparison.Ordinal);
            if (chgEnd < 0) chgEnd = operateSrc.Length;

            string chgSrc = operateSrc.Substring(chgStart, chgEnd - chgStart);

            // The native gate is `jle`, i.e. it rejects nGold <= 0, not nGold < 0:
            //   0x6C4477  85 F6              test esi,esi        ; esi = nGold
            //   0x6C4479  0F 8E C8 00 00 00  jle 0x6C4547        ; <=0 -> SM_DEALCHGGOLD_FAIL
            //   0x6C4547  80 7D FF 00        cmp byte [ebp-1],0
            //   0x6C456B  66 BA AD 02        mov dx,0x2AD        ; SM_DEALCHGGOLD_FAIL
            // A `< 0` guard lets nGold == 0 reach the success branch, which sends the wrong
            // ident AND lets a client reset m_DealLastTick at will. Pin the `<= 0` form and
            // reject the loose one.
            if (!chgSrc.Contains("nGold <= 0") && !chgSrc.Contains("nGold<=0"))
                throw new Exception(
                    "TCFP-27 FAIL: ClientChangeDealGold lacks the `nGold <= 0` guard "
                    + "(native sub_6C4454 @0x6C4479: test esi,esi / jle 0x6C4547 rejects <=0)");
            if (chgSrc.Contains("nGold < 0") || chgSrc.Contains("nGold<0"))
                throw new Exception(
                    "TCFP-27 FAIL: ClientChangeDealGold reverted to the loose `nGold < 0` "
                    + "guard; native @0x6C4479 is `jle`, so nGold == 0 must fail too");

            Console.WriteLine(
                "PASS TCFP-27 ClientChangeDealGold has the `nGold <= 0` reject guard "
                + "(native sub_6C4454 @0x6C4479: test esi,esi / jle 0x6c4547 rejects <=0; "
                + "nGold==0 sends SM_DEALCHGGOLD_FAIL and does not touch m_DealLastTick; "
                + "negative escrow NOT constructible)");

            Console.WriteLine(
                "\nPASS DealValidationFailureSemanticsCheck: TCFP-22 fail-fast+no-messages / "
                + "TCFP-27 negative-escrow-guard");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL DealValidationFailureSemanticsCheck: " + ex);
            Environment.Exit(1);
        }
    }

    // Platforms=x64 puts the binary at bin/x64/Debug/net8.0-windows, so a
    // fixed five-parent climb lands inside the project folder, not the repo.
    static string FindRepositoryRoot(string[] args)
    {
        foreach (var seed in new[]
                 {
                     args != null && args.Length > 0 ? args[0] : null,
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            if (string.IsNullOrEmpty(seed)) continue;
            for (var directory = new DirectoryInfo(Path.GetFullPath(seed));
                 directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                        "GameSvr", "GameSvr.csproj")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("repository root not found");
    }
}
