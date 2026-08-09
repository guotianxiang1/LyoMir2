using System.Collections.Generic;
using System.IO;
using SystemModule;

namespace GameSvr.Services
{
    // ================================================================================================
    // StallTradConf.txt loader (task #83) — the stall tier / affordability-fee config table.
    //
    // NATIVE: sub_61B710 (from sub_61D0B8) loads "StallTradConf.txt" via sub_40581C (LoadFromFile) from the
    // Envir config dir (string @0x0061BA48). Per-line: skip ';' comment lines (native `*v27 != 59`), then
    // tab(9)/space(32) tokenize (sub_4C6BA4 n9=9/n32=32 + sub_405598) into 8 fields, allocate a 56-byte tier
    // record (sub_402FA0(56)) with 4 int fields (sub_40C9D8) + 2 string fields (sub_40CA18), and append it to
    // the TStallTradMgr list (mgr+0x20). The loader MOVES NO money/items — it is pure startup config; the
    // affordability gate sub_61D6B8 later READS this table (fee×level compare, non-consuming).
    // Dumps: staging/ida_stall_audit.txt:1097-1218 (full decompile); shipped file D:/lyom2Release/mud2.0/
    // Mir200/Envir/StallTradConf.txt (GBK).
    //
    // GROUND-TRUTH COLUMNS (GBK header of the shipped file, decoded — NOT guessed):
    //   [0] 摊位等级   tier            (1..4)
    //   [1] 单位时长   unit duration   (1)
    //   [2] 最大时长   max duration hrs(12) — the stall duratime cap (matches `duratime` in the stall SQL)
    //   [3] 摊位格子数量 slot count     (5 / 10 / 15 / 20)
    //   [4] 摊位材料1  material-1 name (元宝)
    //   [5] 材料数量1  material-1 qty  (2000 / 4000 / 7000 / 12000)
    //   [6] 材料2      material-2 name (empty in the shipped file)
    //   [7] 材料数量2  material-2 qty  (empty in the shipped file)
    //
    // GATE-FIELD BINDING — CONFIRMED (codec-fidelity, 2026-08-01, one serial idat run). The native 56-byte
    //   tier record maps to these columns: +0x00 col[0] tier / +0x04 col[1] unitDur / +0x08 col[2] maxDur(12)
    //   / +0x0C col[3] slotCount / +0x10 col[4] mat1name / +0x20 col[5] mat1qty (parsed from a string token
    //   but stored+used as an INT — this resolved the earlier string/int contradiction) / +0x24 col[6]
    //   mat2name / +0x34 col[7] mat2qty. sub_61D6B8 gates: requestedDuration > MaxDurationHours(col[2]) => -2;
    //   requestedDuration × Material1Qty(col[5]) > GoldNum => -1 (can't afford); a name-length gate on col[4]
    //   (byte+1 vs dword_61D728) => -3. NATIVE QUIRK: the fee compares against GoldNum (regular gold,
    //   Self+0x15C) even though the material is labeled 元宝 — the gate is CHECK-ONLY and never deducts (Δ=0).
    //   RESIDUAL (cheap codec-fidelity follow-up): how sub_61D730 SELECTS the tier row (by stall Level? index?)
    //   is not yet nailed — non-economy since nothing is charged, but needed for a faithful CHECK.
    //
    // STATE: INERT. Nothing calls Load() yet — the stall subsystem is fully dormant (task #83). The gate wires
    // this in as part of the booth-setup leaf (SetTimeLevel/START), after the money-semantics confirmations.
    // ================================================================================================

    /// <summary>One parsed StallTradConf.txt tier row (the 56-byte native tier record, ground-truth columns).</summary>
    public sealed class NativeStallTradTier
    {
        public int Tier { get; init; }                 // [0] 摊位等级
        public int UnitDurationHours { get; init; }    // [1] 单位时长
        public int MaxDurationHours { get; init; }     // [2] 最大时长 (duratime cap)
        public int SlotCount { get; init; }            // [3] 摊位格子数量
        public string Material1Name { get; init; } = string.Empty; // [4] 摊位材料1 (元宝)
        public int Material1Qty { get; init; }         // [5] 材料数量1
        public string Material2Name { get; init; } = string.Empty; // [6] 材料2 (empty)
        public int Material2Qty { get; init; }         // [7] 材料数量2 (empty)

        /// <summary>The raw whitespace-split tokens (for codec-fidelity's column→gate-field binding).</summary>
        public string[] RawTokens { get; init; } = System.Array.Empty<string>();
    }

    /// <summary>
    /// Faithful loader for StallTradConf.txt (native sub_61B710). Fail-safe: a missing/empty/garbled file
    /// yields an empty tier list (logged), never throws — matching the native "empty table" fallback. Pure
    /// config: no money, no items, no persistence.
    /// </summary>
    public static class NativeStallTradConf
    {
        public const string FileName = "StallTradConf.txt";
        private const char CommentPrefix = ';';   // native `*v27 != 59`

        /// <summary>
        /// Parse StallTradConf.txt from the Envir config dir. Each non-';'-comment, non-empty line is
        /// whitespace-tokenized (tab/space, native n9=9/n32=32) into the ground-truth columns above.
        /// </summary>
        public static IReadOnlyList<NativeStallTradTier> Load(string envirDir)
        {
            var tiers = new List<NativeStallTradTier>();
            if (string.IsNullOrEmpty(envirDir))
                return tiers;

            var path = Path.Combine(envirDir, FileName);
            if (!File.Exists(path))
            {
                M2Share.MainOutMessage($"StallTradConf.txt not found: {path}");
                return tiers;
            }

            try
            {
                // Native reads GBK bytes (战神/传奇 config files are GBK); decode with the same codec used
                // for the GBK-stored DB columns (CommonDB.TryGetGbkStoredString / HUtil32.GbkEncoding).
                var text = HUtil32.GbkEncoding.GetString(File.ReadAllBytes(path));
                foreach (var rawLine in text.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    if (line.Length == 0)
                        continue;
                    // native: parse only when the FIRST non-empty token doesn't start with ';'
                    var tokens = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0 || tokens[0].Length == 0 || tokens[0][0] == CommentPrefix)
                        continue;
                    if (tokens.Length < 6)
                        continue;   // a malformed row lacking the 6 populated columns — skip (fail-safe)

                    tiers.Add(new NativeStallTradTier
                    {
                        Tier = ParseInt(tokens, 0),
                        UnitDurationHours = ParseInt(tokens, 1),
                        MaxDurationHours = ParseInt(tokens, 2),
                        SlotCount = ParseInt(tokens, 3),
                        Material1Name = Token(tokens, 4),
                        Material1Qty = ParseInt(tokens, 5),
                        Material2Name = Token(tokens, 6),
                        Material2Qty = ParseInt(tokens, 7),
                        RawTokens = tokens,
                    });
                }
            }
            catch (System.Exception ex)
            {
                M2Share.MainOutMessage($"StallTradConf.txt load failed: {ex.Message}");
                return new List<NativeStallTradTier>();   // fail-safe: empty table, never partial-garbage
            }

            return tiers;
        }

        private static string Token(string[] tokens, int i) => i < tokens.Length ? tokens[i] : string.Empty;

        private static int ParseInt(string[] tokens, int i) =>
            i < tokens.Length && int.TryParse(tokens[i], out var v) ? v : 0;
    }
}
