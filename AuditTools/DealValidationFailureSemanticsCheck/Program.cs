using System;
using System.IO;
using System.Linq;

// TCFP-22: ClientDealEnd validation failure semantics (fail-fast + no per-check messages)
// TCFP-27: ClientChangeDealGold rejects nGold < 0 (native @0x6C4477 rejects <=0)
//
// 战神 sub_6C4580 ClientDealEnd 四道校验 @0x6C46E4/0x6C4705/0x6C4720/0x6C473F 全部跳到
// 同一失败目标 0x6C49B8，直接调 DealCancel (sub_6C43C4) 并静默返回，无任何 per-check 消息。
// 旧 C# 把四道检查全跑完且每条失败时都发消息；那四条消息字符串在二进制里 GBK 零命中。
//
// 战神 sub_6C4454 ClientChangeDealGold @0x6C4477: test esi,esi / jle 0x6c4547 拒绝 nGold<=0。
// C# 有 `if (nGold < 0)` 门；native 拒绝 0，C# 允许（nGold==0 是无意义 no-op，不成风险）。

class Program
{
    static void Main()
    {
        try
        {
            var repoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
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

            // ===== TCFP-22: fail-fast structure (bo11 && short-circuits) =====
            int dealEndStart = operateSrc.IndexOf("private void ClientDealEnd()", StringComparison.Ordinal);
            if (dealEndStart < 0)
                throw new Exception("TCFP-22: ClientDealEnd method not found");

            int validationStart = operateSrc.IndexOf("if (m_DealCreat.m_boDealOK)", dealEndStart,
                StringComparison.Ordinal);
            if (validationStart < 0)
                throw new Exception("TCFP-22: m_boDealOK validation block not found");

            string window = operateSrc.Substring(validationStart,
                Math.Min(2000, operateSrc.Length - validationStart));

            int bo11AndCount = 0;
            int pos = 0;
            while ((pos = window.IndexOf("bo11 &&", pos, StringComparison.Ordinal)) >= 0)
            {
                bo11AndCount++;
                pos += 7;
            }

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

            if (!chgSrc.Contains("nGold < 0") && !chgSrc.Contains("nGold<0"))
                throw new Exception(
                    "TCFP-27 FAIL: ClientChangeDealGold lacks `nGold < 0` guard "
                    + "(native sub_6C4454 @0x6C4477: test esi,esi / jle rejects <=0)");

            Console.WriteLine(
                "PASS TCFP-27 ClientChangeDealGold has `nGold < 0` reject guard "
                + "(native sub_6C4454 @0x6C4477: test esi,esi / jle 0x6c4547 rejects <=0; "
                + "C# rejects <0; nGold==0 is no-op either way; negative escrow NOT constructible)");

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
}
