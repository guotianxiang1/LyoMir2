using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // Gated live handler for CM_STRENGTHEN_EQUIP_QUEST = 4465 (read-only recipe query).
        //
        // Returns FALSE (the caller then runs the existing fail-closed stub) UNLESS the feature gate
        // NativeStrengthenRecipeStore.SupportsStrengthenRecipes is ON *and* the recipe store is loaded.
        // The gate is OFF by default, so this is DORMANT: it returns false immediately and the live
        // CM_STRENGTHEN_EQUIP_QUEST case in TPlayObject.Message.cs behaves exactly as before.
        //
        // Feeds the already-verified dormant model NativeStrengthenEquipQuest.Evaluate. The pieces that
        // are ASSUMED pending the sub_7548D8 idat confirmation are marked inline; because the gate stays
        // OFF until they are confirmed, none of them affects runtime yet:
        //   (b) recipe key = the store's col3 Param;
        //   (c) guard = strengthen-limit >= key — deferred; ASSUMED passed when a recipe exists;
        //   (c) var_C "extra" = unit-price/[mgr+0x28] — deferred; ASSUMED 0 (no price column in the file);
        //   (d) per-material "look" word body + the vtbl+0x254 buffered send — deferred; the first cut
        //       sends the exact result-code ladder via the plain SendDefMessage and omits the word body.
        internal bool TryClientStrengthenEquipQuestGated(TProcessMessage processMsg)
        {
            if (!NativeStrengthenRecipeStore.SupportsStrengthenRecipes)
                return false;

            var store = NativeStrengthenRecipeStore.Shared;
            if (store == null || store.Count == 0)
                return false;

            // sub_73CF08: target item resolved by the client id in the query.
            var targetItem = FindOwnedItemByClientId(processMsg.nParam1);
            bool targetFound = targetItem != null;
            string itemName = targetFound ? ItmUnit.GetItemName(targetItem) : string.Empty;

            // sub_60F504: recipe by item name (ASSUMED key = base name).
            NativeStrengthenRecipe recipe = null;
            bool recipeFound = targetFound && store.TryGetRecipe(itemName, out recipe);

            int key = recipe?.Key ?? 0;                                   // (b) word[recipe+0x08]
            bool guardPassed = recipeFound;                               // (c) real guard deferred
            int extra = 0;                                                // (c) var_C/price deferred

            // count = required-material count; per-material "look" words deferred -> placeholder zeros
            // (keeps the model's count-driven result ladder correct without touching StdItem lookups).
            int matCount = recipeFound && recipe.RequiredMaterials != null
                ? recipe.RequiredMaterials.Count
                : 0;
            var looks = new List<int>(matCount);
            for (int i = 0; i < matCount; i++)
                looks.Add(0);

            var context = new NativeStrengthenEquipQuestContext
            {
                TargetFound = targetFound,
                RecipeFound = recipeFound,
                RecipeKey = key,
                GuardPassed = guardPassed,
                ExtraValue = extra,
                MaterialLooks = looks,
            };
            var outcome = NativeStrengthenEquipQuest.Evaluate(context);

            // (d) first-cut send: the modeled result ladder via the plain SendDefMessage; the word-array
            // body is deferred. Arg mapping ASSUMED: wParam=key, nParam1=extra, nParam2=count,
            // nParam3=resultCode.
            SendDefMessage(Grobal2.SM_STRENGTHEN_EQUIP_QUEST, outcome.DispatchWParam,
                outcome.ExtraWord, outcome.ReturnedCount, (int)outcome.Result, string.Empty);
            return true;
        }
    }
}
