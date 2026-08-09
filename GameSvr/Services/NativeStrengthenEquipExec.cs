using System.Collections.Generic;

namespace GameSvr
{
    // Dormant model of the CM_STRENGTHEN_EQUIP = 4466 (0x1172) synthesis EXECUTION front-half:
    // material classification + validation + cost + result-code ladder. The apply/async second
    // stage (sub_60FF28 paid / sub_60FFDC free / sub_711630 -> sub_61055C -> sub_60FC1C) is a
    // separate reverse and is deliberately NOT modeled here.
    //
    // Original evidence (unpacked M2Server.exe, image base 0x00400000, baseline SHA-256 5540F43B...,
    // Hex-Rays verified). See staging/strengthen_equip_exec_4466_evidence_20260731.md.
    //   handler sub_60F7AC @0x0060F7AC; dispatcher sub_6102E0 @0x006102E0.
    //   recipe  sub_60F504 (by first-material name); key = word[recipe+0x08];
    //   guard   sub_60F3E8 (server-open-limit >= recipeKey); unit price sub_60F55C([mgr+0x28][key-1]);
    //   result  sub_60F74C: Random(word[recipe+0x0A]) weighted pick over [recipe+0x04]{weight,id};
    //   base    stditem[0x14]==0x40; base match byte stditem[0x15]; gold [player+0x760];
    //   send    player.[vtbl+0x250] SendDefMessage(0x1172, wParam=code, 0,0,0) only when code != 0.
    //
    // The recipe is a required set: exactly one base item plus >= 2 DISTINCT INSTANCES of a single
    // material type (same name, different [item+0x20]). Mixing material names yields code 9; the same
    // physical instance is de-duplicated.
    //
    // Dormant: C# has no synthesis config loader (recipe/price/weight tables), so the live 4466 case
    // stays a fail-closed stub. This models only the deterministic front-half contract; recipe data
    // and the RNG owner (RandSeed cutover) are not invented or wired.

    /// <summary>Raw 4466 front-half result codes (var_34) of sub_60F7AC.</summary>
    public enum NativeStrengthenEquipExecCode
    {
        /// <summary>All front-half checks passed; control proceeds to the (deferred) apply stage.
        /// No error is sent to the client here (paid path is async; free path sends sub_60FFDC's code).</summary>
        FrontHalfPassed = 0,
        /// <summary>sub_60F74C selected no result item (v34[0] == 0).</summary>
        ResultNotSelected = 2,
        /// <summary>Post-loop validation failed: need >=2 distinct materials, exactly one base, a first material.</summary>
        ValidationFailed = 3,
        /// <summary>sub_60F504 null — no recipe for the first material name.</summary>
        RecipeNotFound = 4,
        /// <summary>Cost &gt; player gold [player+0x760].</summary>
        InsufficientGold = 5,
        /// <summary>sub_60F3E8 false — recipe key exceeds the server-open limit.</summary>
        GuardRejected = 6,
        /// <summary>Base stditem[0x15] != recipe key word[recipe+0x08].</summary>
        BaseKeyMismatch = 7,
        /// <summary>A material item is locked/timed (word[item+0x34] != 0).</summary>
        MaterialLocked = 8,
        /// <summary>A material name differs from an already-collected material (mixed material types).</summary>
        MaterialNameMismatch = 9,
        /// <summary>A submitted id resolved to no item in the player list (sub_73CF08 null).</summary>
        MaterialNotFound = 10,
    }

    /// <summary>One resolved submission slot (an id from the client array, looked up via sub_73CF08).</summary>
    public sealed class NativeStrengthenEquipSlot
    {
        /// <summary>sub_73CF08(player, id) != null.</summary>
        public bool Found { get; init; }
        /// <summary>stditem[0x14] == 0x40 — this is the base item to be strengthened.</summary>
        public bool IsBase { get; init; }
        /// <summary>word[item+0x34] != 0 — locked/timed; only checked for non-base items.</summary>
        public bool Locked { get; init; }
        /// <summary>sub_784568 item name — materials must all share one name.</summary>
        public string Name { get; init; }
        /// <summary>[item+0x20] — per-instance key used to de-duplicate the same physical item.</summary>
        public int InstanceKey { get; init; }
        /// <summary>stditem[0x15] (byte) — only meaningful for the base; compared to the recipe key.</summary>
        public int BaseMatchWord { get; init; }
    }

    /// <summary>Config/recipe/gold facts the original reads after a valid material set is collected.</summary>
    public sealed class NativeStrengthenEquipExecContext
    {
        /// <summary>Client submission ids resolved to slots, in order.</summary>
        public IReadOnlyList<NativeStrengthenEquipSlot> Slots { get; init; }
        /// <summary>sub_60F504(firstMaterialName) != null.</summary>
        public bool RecipeFound { get; init; }
        /// <summary>word[recipe+0x08] — recipe key.</summary>
        public int RecipeKey { get; init; }
        /// <summary>sub_60F55C(recipeKey) — per-unit gold price.</summary>
        public int UnitPrice { get; init; }
        /// <summary>sub_60F3E8(recipeKey, player).</summary>
        public bool GuardPassed { get; init; }
        /// <summary>sub_60F74C selected a result (v34[0] != 0).</summary>
        public bool ResultSelected { get; init; }
        /// <summary>Player gold [player+0x760].</summary>
        public int PlayerGold { get; init; }
    }

    public sealed class NativeStrengthenEquipExecOutcome
    {
        public NativeStrengthenEquipExecCode Code { get; init; }
        /// <summary>(5 - distinctMaterialCount) * unitPrice; valid once the ladder reaches the cost step.</summary>
        public int Cost { get; init; }
        /// <summary>Front-half passed and cost &gt; 0 — the async paid apply path (no immediate send).</summary>
        public bool IsPaidPath { get; init; }
        /// <summary>Distinct material instances collected (n2).</summary>
        public int DistinctMaterials { get; init; }
        /// <summary>Base items seen (v38); a valid set requires exactly one.</summary>
        public int BaseCount { get; init; }
        /// <summary>Client wParam: the code is sent verbatim only when non-zero.</summary>
        public int DispatchWParam => (int)Code;
    }

    public static class NativeStrengthenEquipExec
    {
        public const int Ident = 4466;              // 0x1172
        public const int VtblSendDefMessage = 0x250;
        public const int PlayerGoldOffset = 0x760;  // v39[472]
        public const int BaseStdItemFlagOffset = 0x14;   // == 0x40 marks the base
        public const int BaseStdItemFlagValue = 0x40;
        public const int BaseMatchByteOffset = 0x15;      // stditem[0x15] vs recipe key
        public const int ItemLockWordOffset = 0x34;       // word[item+0x34] != 0 -> locked
        public const int ItemInstanceKeyOffset = 0x20;    // [item+0x20]
        public const int RecipeKeyOffset = 0x08;          // word[recipe+0x08]
        public const int RecipeWeightBoundOffset = 0x0A;  // Random(word[recipe+0x0A]) in sub_60F74C
        public const int MaxDistinctSlots = 5;            // v28[5]

        public static NativeStrengthenEquipExecOutcome Evaluate(NativeStrengthenEquipExecContext context)
        {
            var slots = context?.Slots ?? new List<NativeStrengthenEquipSlot>();

            int code = 0;                                   // n8
            int baseCount = 0;                              // v38
            bool basePresent = false;                       // n8_3 != 0
            int distinctCount = 0;                          // n2
            bool firstMaterial = false;                     // n8_4 != 0
            var distinct = new List<NativeStrengthenEquipSlot>(); // v28

            // Material loop (last error among 8/9/10 wins; loop never breaks early on error).
            foreach (var slot in slots)
            {
                if (slot == null || !slot.Found)
                {
                    code = 10;
                    continue;
                }
                if (slot.IsBase)
                {
                    baseCount++;
                    basePresent = true;
                    continue;
                }
                if (slot.Locked)
                {
                    code = 8;
                    continue;
                }
                if (distinctCount != 0)
                {
                    bool sameInstance = false;
                    foreach (var existing in distinct)
                    {
                        if (!string.Equals(existing.Name, slot.Name))
                            code = 9; // mixed material name
                        if (existing.InstanceKey == slot.InstanceKey)
                        {
                            sameInstance = true;
                            break;
                        }
                    }
                    if (!sameInstance && distinctCount < MaxDistinctSlots)
                    {
                        distinct.Add(slot);
                        distinctCount++;
                    }
                    else if (!sameInstance)
                    {
                        distinctCount++; // faithful counter; storage array caps at v28[5]
                    }
                }
                else
                {
                    distinct.Add(slot);
                    firstMaterial = true;
                    distinctCount++;
                }
            }

            if (code != 0)
                return Fail((NativeStrengthenEquipExecCode)code, 0, distinctCount, baseCount);

            // Post-loop validation ladder.
            if (!(distinctCount >= 2 && basePresent && baseCount == 1 && firstMaterial))
                return Fail(NativeStrengthenEquipExecCode.ValidationFailed, 0, distinctCount, baseCount);
            if (!context.RecipeFound)
                return Fail(NativeStrengthenEquipExecCode.RecipeNotFound, 0, distinctCount, baseCount);

            int cost = (5 - distinctCount) * context.UnitPrice; // (6 - n2 - 1) * unitPrice

            if (!context.GuardPassed)
                return Fail(NativeStrengthenEquipExecCode.GuardRejected, cost, distinctCount, baseCount);

            var baseSlot = FindBase(slots);
            int baseMatch = (baseSlot?.BaseMatchWord ?? 0) & 0xFFFF;
            if (baseMatch != (context.RecipeKey & 0xFFFF))
                return Fail(NativeStrengthenEquipExecCode.BaseKeyMismatch, cost, distinctCount, baseCount);

            if (!context.ResultSelected)
                return Fail(NativeStrengthenEquipExecCode.ResultNotSelected, cost, distinctCount, baseCount);

            if (cost > context.PlayerGold)
                return Fail(NativeStrengthenEquipExecCode.InsufficientGold, cost, distinctCount, baseCount);

            // Front-half passed. cost>0 -> paid async apply; cost<=0 -> free direct apply (both deferred).
            return new NativeStrengthenEquipExecOutcome
            {
                Code = NativeStrengthenEquipExecCode.FrontHalfPassed,
                Cost = cost,
                IsPaidPath = cost > 0,
                DistinctMaterials = distinctCount,
                BaseCount = baseCount,
            };
        }

        private static NativeStrengthenEquipSlot FindBase(IReadOnlyList<NativeStrengthenEquipSlot> slots)
        {
            foreach (var s in slots)
                if (s != null && s.Found && s.IsBase)
                    return s;
            return null;
        }

        private static NativeStrengthenEquipExecOutcome Fail(
            NativeStrengthenEquipExecCode code, int cost, int distinctCount, int baseCount)
        {
            return new NativeStrengthenEquipExecOutcome
            {
                Code = code,
                Cost = cost,
                IsPaidPath = false,
                DistinctMaterials = distinctCount,
                BaseCount = baseCount,
            };
        }
    }
}
