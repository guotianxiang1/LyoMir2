using System.Collections.Generic;

namespace GameSvr
{
    // Dormant model of CM_STRENGTHEN_EQUIP_QUEST = SM_STRENGTHEN_EQUIP_QUEST = 4465 (0x1171):
    // the equipment-synthesis recipe QUERY handler (read-only; no RNG, no persistence).
    //
    // Original evidence (unpacked M2Server.exe, image base 0x00400000, baseline SHA-256 5540F43B...):
    //   handler : sub_60F5C0 @0x0060F5C0
    //   find    : sub_73CF08 @0x0073CF08  (target item by id in player list [player+0x508])
    //   recipe  : sub_60F504 @0x0060F504  (recipe by item name in config mgr dword_7DC210)
    //   key     : word [recipe+0x08]      (esi; forwarded as response wParam)
    //   guard   : sub_60F3E8 @0x0060F3E8  (mgr, recipeKey, id) -> bool
    //   extra   : sub_60F55C @0x0060F55C  (mgr, recipeKey) -> word (var_C)
    //   mats    : len([recipe+0x04]) via sub_406A88; per material -> GetStdItem
    //             sub_74C2D4(off_7D5D6C) first word ("look") into a word[count] body
    //   send    : player.[vtbl+0x254](wIdent=0x1171, wParam=key, body=word[count], count, extra, result)
    //
    // The result code (EDX) is placed after all other args; distinct raw values:
    //   0 target-not-found | 2 no-recipe | 3 guard-rejected | 4 no-materials | 1 success.
    //
    // Dormant: C# has no synthesis config loader (dword_7DC210), so the live 4465 case stays a
    // fail-closed stub ("装备合成功能当前不可用"). This models only the exact result ladder,
    // per-branch field population and response body shape; it invents no recipe/config data and
    // is not wired to the PAS-driven strengthen path.

    /// <summary>Raw 4465 result codes (EDX) sent by the original sub_60F5C0.</summary>
    public enum NativeStrengthenEquipQuestResult
    {
        /// <summary>sub_73CF08 null — target item not in player list (xor edx,edx).</summary>
        TargetNotFound = 0,
        /// <summary>Recipe resolved and material looks returned.</summary>
        Success = 1,
        /// <summary>sub_60F504 null — no synthesis recipe for the item name.</summary>
        RecipeNotFound = 2,
        /// <summary>sub_60F3E8 false — recipe guard rejected the item.</summary>
        GuardRejected = 3,
        /// <summary>Recipe material count &lt;= 0.</summary>
        NoMaterials = 4,
    }

    /// <summary>
    /// Side-effect-free precondition snapshot. Dormant: the model never reads live game/config state;
    /// each field stands for the outcome of the corresponding original lookup/guard.
    /// </summary>
    public sealed class NativeStrengthenEquipQuestContext
    {
        /// <summary>sub_73CF08(player, id) != null.</summary>
        public bool TargetFound { get; init; }
        /// <summary>sub_60F504(mgr, itemName) != null.</summary>
        public bool RecipeFound { get; init; }
        /// <summary>word [recipe+0x08] — recipe key (esi), forwarded as response wParam.</summary>
        public int RecipeKey { get; init; }
        /// <summary>sub_60F3E8(mgr, recipeKey, id).</summary>
        public bool GuardPassed { get; init; }
        /// <summary>sub_60F55C(mgr, recipeKey) — var_C; only its low word is sent.</summary>
        public int ExtraValue { get; init; }
        /// <summary>Per-material std-item "look" words; count is the recipe material count.</summary>
        public IReadOnlyList<int> MaterialLooks { get; init; }
    }

    /// <summary>Exact response contract for one 4465 query.</summary>
    public sealed class NativeStrengthenEquipQuestOutcome
    {
        public NativeStrengthenEquipQuestResult Result { get; init; }
        /// <summary>ecx = esi (recipe key); 0 on target-not-found and no-recipe (esi set only after recipe).</summary>
        public int DispatchWParam { get; init; }
        /// <summary>var_C low word; 0 until the guard passes.</summary>
        public int ExtraWord { get; init; }
        /// <summary>edi — material count; 0 unless the guard passed.</summary>
        public int ReturnedCount { get; init; }
        /// <summary>var_14 = edi*2 — body byte length; nonzero only on success.</summary>
        public int BodyByteLength { get; init; }
        /// <summary>var_10 — word body (one "look" per material); populated only on success.</summary>
        public IReadOnlyList<int> ReturnedLooks { get; init; }
    }

    public static class NativeStrengthenEquipQuest
    {
        /// <summary>CM/SM_STRENGTHEN_EQUIP_QUEST = 0x1171. sub_60F5C0: mov dx, 1171h.</summary>
        public const int Ident = 4465;
        /// <summary>player.[vtbl+0x254] — buffered SendDefMessage variant (word-array body).</summary>
        public const int VtblSendBuffer = 0x254;
        /// <summary>word [recipe+0x08] — recipe key.</summary>
        public const int RecipeKeyOffset = 0x08;
        /// <summary>[recipe+0x04] — material entry array.</summary>
        public const int RecipeMaterialsOffset = 0x04;

        public static NativeStrengthenEquipQuestOutcome Evaluate(NativeStrengthenEquipQuestContext context)
        {
            // sub_73CF08 null -> loc_60F6F6: xor edx,edx (result 0); esi/var_C/edi/var_14 all still 0.
            if (context == null || !context.TargetFound)
                return Build(NativeStrengthenEquipQuestResult.TargetNotFound, 0, 0, 0, null);

            // sub_60F504 null -> loc_60F6EF: edx=2; esi still 0 (set only after recipe resolves).
            if (!context.RecipeFound)
                return Build(NativeStrengthenEquipQuestResult.RecipeNotFound, 0, 0, 0, null);

            int key = context.RecipeKey; // esi = word[recipe+8]

            // sub_60F3E8 false -> loc_60F6E8: edx=3; var_C still 0, edi still 0.
            if (!context.GuardPassed)
                return Build(NativeStrengthenEquipQuestResult.GuardRejected, key, 0, 0, null);

            int extra = context.ExtraValue & 0xFFFF; // var_C low word (set at 0x0060F670)
            int count = context.MaterialLooks?.Count ?? 0;

            // edi <= 0 -> loc_60F6E1: edx=4; no body.
            if (count <= 0)
                return Build(NativeStrengthenEquipQuestResult.NoMaterials, key, extra, 0, null);

            // success -> edx=1; body = one word "look" per material, var_14 = edi*2.
            var looks = new List<int>(count);
            foreach (int look in context.MaterialLooks)
                looks.Add(look & 0xFFFF);

            return new NativeStrengthenEquipQuestOutcome
            {
                Result = NativeStrengthenEquipQuestResult.Success,
                DispatchWParam = key,
                ExtraWord = extra,
                ReturnedCount = count,
                BodyByteLength = count * 2,
                ReturnedLooks = looks,
            };
        }

        private static NativeStrengthenEquipQuestOutcome Build(
            NativeStrengthenEquipQuestResult result, int wParam, int extra, int count,
            IReadOnlyList<int> looks)
        {
            return new NativeStrengthenEquipQuestOutcome
            {
                Result = result,
                DispatchWParam = wParam,
                ExtraWord = extra,
                ReturnedCount = count,
                BodyByteLength = count * 2,
                ReturnedLooks = looks,
            };
        }
    }
}
