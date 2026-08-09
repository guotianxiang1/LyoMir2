namespace GameSvr
{
    // Dormant model of the CM_STRENGTHEN_EQUIP = 4466 synthesis APPLY stage (second half),
    // completing NativeStrengthenEquipExec (front-half). Hex-Rays verified.
    //
    // Original evidence (unpacked M2Server.exe, image base 0x00400000, baseline SHA-256 5540F43B...):
    //   free  path : sub_60FFDC @0x0060FFDC (synchronous; cost <= 0)
    //   paid  path : sub_60FF28 @0x0060FF28 (schedules a pending record via sub_49F650)
    //   async apply: sub_60FC1C @0x0060FC1C (via sub_711630 -> callback sub_61055C)
    //   create result std item : sub_74DE54;  delete item : player.[vtbl+0x268];
    //   add item : player.[vtbl+0x248];  SendDefMessage : player.[vtbl+0x250].
    //
    // Client-facing result of a completed synthesis is wParam = 1 (SUCCESS) on both paths —
    // NOT 0. The front-half FrontHalfPassed(0) only means "entered the apply stage".
    //   free  success            -> SM(4466, wParam=1), consume base+materials, add result
    //   free  result-create fail -> SM(4466, wParam=-1), nothing consumed
    //   async material vanished  -> local notice only (sub_768BE0), NO SM(4466)
    //   async result-create fail -> local notice only, NO SM(4466), nothing consumed
    //   async success            -> SM(4466, wParam=1), consume base+materials, add result
    //
    // Dormant: not wired; depends on the synthesis config loader and the RandSeed owner cutover.

    public enum NativeStrengthenEquipApplyBranch
    {
        FreeSuccess,
        FreeResultCreateFailed,
        AsyncSuccess,
        AsyncMaterialGone,
        AsyncResultCreateFailed,
    }

    public sealed class NativeStrengthenEquipApplyOutcome
    {
        public NativeStrengthenEquipApplyBranch Branch { get; init; }
        /// <summary>Whether SM_STRENGTHEN_EQUIP(4466) is sent to the client for this branch.</summary>
        public bool SendsStrengthenMessage { get; init; }
        /// <summary>wParam of the sent SM (1 success / -1 free-path result-create fail); valid when sent.</summary>
        public int MessageWParam { get; init; }
        /// <summary>Whether the base item and all materials are consumed (deleted).</summary>
        public bool ConsumesBaseAndMaterials { get; init; }
        /// <summary>Whether the result item is added to the player.</summary>
        public bool AddsResultItem { get; init; }
    }

    public static class NativeStrengthenEquipApply
    {
        public const int Ident = 4466;               // SM_STRENGTHEN_EQUIP
        public const int SuccessWParam = 1;          // both paths: success signal
        public const int FreeFailWParam = -1;        // sub_60FFDC default return
        public const int VtblDeleteItem = 0x268;     // player.[vtbl+0x268]
        public const int VtblAddItem = 0x248;        // player.[vtbl+0x248]
        public const int VtblSendDefMessage = 0x250; // player.[vtbl+0x250]
        public const int AsyncFlagOffset = 0xBA6;    // byte[player+0xBA6] cleared by sub_60FC1C

        /// <summary>
        /// Model the apply stage. <paramref name="isPaidPath"/> selects async (paid) vs synchronous
        /// (free). <paramref name="resultItemCreated"/> is sub_74DE54 success. <paramref name="asyncMaterialsPresent"/>
        /// is only meaningful on the paid/async path (materials are re-resolved when the callback runs).
        /// </summary>
        public static NativeStrengthenEquipApplyOutcome Evaluate(
            bool isPaidPath, bool resultItemCreated, bool asyncMaterialsPresent)
        {
            if (!isPaidPath)
            {
                // sub_60FFDC synchronous: result created -> consume + add + return 1; else return -1.
                if (resultItemCreated)
                    return Make(NativeStrengthenEquipApplyBranch.FreeSuccess, true, SuccessWParam, true, true);
                return Make(NativeStrengthenEquipApplyBranch.FreeResultCreateFailed, true, FreeFailWParam, false, false);
            }

            // sub_60FC1C async: re-resolve materials first; any gone -> local notice, no SM.
            if (!asyncMaterialsPresent)
                return Make(NativeStrengthenEquipApplyBranch.AsyncMaterialGone, false, 0, false, false);
            // result create fail -> local notice, no SM, nothing consumed (delete loop is guarded by result).
            if (!resultItemCreated)
                return Make(NativeStrengthenEquipApplyBranch.AsyncResultCreateFailed, false, 0, false, false);
            // success -> consume + add + SM(4466, wParam=1).
            return Make(NativeStrengthenEquipApplyBranch.AsyncSuccess, true, SuccessWParam, true, true);
        }

        private static NativeStrengthenEquipApplyOutcome Make(
            NativeStrengthenEquipApplyBranch branch, bool sends, int wParam, bool consumes, bool adds)
        {
            return new NativeStrengthenEquipApplyOutcome
            {
                Branch = branch,
                SendsStrengthenMessage = sends,
                MessageWParam = wParam,
                ConsumesBaseAndMaterials = consumes,
                AddsResultItem = adds,
            };
        }
    }
}
