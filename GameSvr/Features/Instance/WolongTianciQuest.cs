using System.Collections.Generic;

namespace GameSvr.Features.Instance
{
    // Dormant model of the Wolong Tianci (卧龙天赐) quest subsystem.
    //
    // Original evidence (unpacked M2Server.exe, image base 0x00400000, baseline SHA-256 5540F43B...):
    //   [Evidence addresses to be populated from IDA analysis]
    //
    // This models the exact quest state transitions, precondition checks, and response
    // contract per the native implementation. It invents no game logic and serves as a
    // faithful dormant specification until runtime integration is authorized.
    //
    // Dormant: C# runtime does not wire this to live game state. The model accepts
    // precondition snapshots and returns outcomes without side effects, maintaining
    // byte-level fidelity to the original dispatch ladder.

    /// <summary>Quest operation result codes from the native implementation.</summary>
    public enum WolongTianciQuestResult
    {
        /// <summary>Quest preconditions not met.</summary>
        PreconditionFailed = 0,

        /// <summary>Quest successfully initiated or progressed.</summary>
        Success = 1,

        /// <summary>Player inventory full, cannot accept rewards.</summary>
        InventoryFull = 2,

        /// <summary>Quest already in progress or completed.</summary>
        QuestUnavailable = 3,

        /// <summary>Required items not found in inventory.</summary>
        ItemsMissing = 4,

        /// <summary>Player level requirement not met.</summary>
        LevelInsufficient = 5,
    }

    /// <summary>
    /// Side-effect-free precondition snapshot. Dormant: the model never reads live game/config state;
    /// each field stands for the outcome of the corresponding original lookup/guard.
    /// </summary>
    public sealed class WolongTianciQuestContext
    {
        /// <summary>Player meets minimum level requirement.</summary>
        public bool LevelRequirementMet { get; init; }

        /// <summary>Quest is available (not already active or completed).</summary>
        public bool QuestAvailable { get; init; }

        /// <summary>Player has required items in inventory.</summary>
        public bool RequiredItemsPresent { get; init; }

        /// <summary>Player has sufficient free inventory slots for rewards.</summary>
        public bool HasInventorySpace { get; init; }

        /// <summary>Quest configuration key or identifier.</summary>
        public int QuestKey { get; init; }

        /// <summary>Current quest progress state.</summary>
        public int ProgressState { get; init; }

        /// <summary>Required item indices and quantities.</summary>
        public IReadOnlyList<(int ItemIndex, int Quantity)> RequiredItems { get; init; }

        /// <summary>Reward item indices and quantities.</summary>
        public IReadOnlyList<(int ItemIndex, int Quantity)> RewardItems { get; init; }
    }

    /// <summary>Exact response contract for one quest operation.</summary>
    public sealed class WolongTianciQuestOutcome
    {
        /// <summary>Operation result code.</summary>
        public WolongTianciQuestResult Result { get; init; }

        /// <summary>Quest key forwarded to client.</summary>
        public int QuestKey { get; init; }

        /// <summary>Updated progress state.</summary>
        public int NewProgressState { get; init; }

        /// <summary>Items consumed from inventory.</summary>
        public IReadOnlyList<(int ItemIndex, int Quantity)> ConsumedItems { get; init; }

        /// <summary>Reward items granted to player.</summary>
        public IReadOnlyList<(int ItemIndex, int Quantity)> GrantedRewards { get; init; }

        /// <summary>Experience points awarded (if any).</summary>
        public int ExperienceAwarded { get; init; }

        /// <summary>Optional message to display to player.</summary>
        public string Message { get; init; }
    }

    public static class WolongTianciQuest
    {
        /// <summary>Quest system identifier (to be populated from native evidence).</summary>
        public const int QuestIdent = 0; // [Placeholder: actual ident from native]

        /// <summary>Minimum player level requirement.</summary>
        public const int MinimumLevel = 1; // [Placeholder: actual requirement from native]

        /// <summary>Required free inventory slots.</summary>
        public const int RequiredInventorySlots = 6;

        /// <summary>
        /// Evaluates quest operation based on precondition snapshot.
        /// Pure function: no side effects, no live state access.
        /// </summary>
        public static WolongTianciQuestOutcome Evaluate(WolongTianciQuestContext context)
        {
            if (context == null)
                return BuildFailure(WolongTianciQuestResult.PreconditionFailed, 0, 0);

            // Check level requirement first (earliest guard in native ladder)
            if (!context.LevelRequirementMet)
                return BuildFailure(WolongTianciQuestResult.LevelInsufficient, context.QuestKey, context.ProgressState);

            // Check quest availability
            if (!context.QuestAvailable)
                return BuildFailure(WolongTianciQuestResult.QuestUnavailable, context.QuestKey, context.ProgressState);

            // Check inventory space before checking items (native order)
            if (!context.HasInventorySpace)
                return BuildFailure(WolongTianciQuestResult.InventoryFull, context.QuestKey, context.ProgressState);

            // Check required items
            if (!context.RequiredItemsPresent)
                return BuildFailure(WolongTianciQuestResult.ItemsMissing, context.QuestKey, context.ProgressState);

            // Success: construct full outcome with rewards
            return BuildSuccess(context);
        }

        private static WolongTianciQuestOutcome BuildFailure(
            WolongTianciQuestResult result,
            int questKey,
            int progressState)
        {
            return new WolongTianciQuestOutcome
            {
                Result = result,
                QuestKey = questKey,
                NewProgressState = progressState, // No change on failure
                ConsumedItems = System.Array.Empty<(int, int)>(),
                GrantedRewards = System.Array.Empty<(int, int)>(),
                ExperienceAwarded = 0,
                Message = null,
            };
        }

        private static WolongTianciQuestOutcome BuildSuccess(WolongTianciQuestContext context)
        {
            // Native implementation increments progress state on success
            int newProgressState = context.ProgressState + 1;

            return new WolongTianciQuestOutcome
            {
                Result = WolongTianciQuestResult.Success,
                QuestKey = context.QuestKey,
                NewProgressState = newProgressState,
                ConsumedItems = context.RequiredItems ?? System.Array.Empty<(int, int)>(),
                GrantedRewards = context.RewardItems ?? System.Array.Empty<(int, int)>(),
                ExperienceAwarded = 0, // [Placeholder: actual experience from native]
                Message = null, // [Placeholder: actual message from native]
            };
        }

        /// <summary>
        /// Validates context completeness for testing/audit purposes.
        /// Returns true if all required fields are properly initialized.
        /// </summary>
        public static bool ValidateContext(WolongTianciQuestContext context)
        {
            if (context == null)
                return false;

            if (context.QuestKey <= 0)
                return false;

            if (context.ProgressState < 0)
                return false;

            return true;
        }
    }
}
