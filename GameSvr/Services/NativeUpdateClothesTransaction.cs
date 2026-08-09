using SystemModule;

namespace GameSvr
{
    // Exact dormant model of the original CM_UPDATE_CLOTHES = SM_UPDATE_CLOTHES = 4637 (0x121D)
    // equipment-upgrade transaction.
    //
    // Original evidence (unpacked M2Server.exe, image base 0x00400000,
    // baseline SHA-256 5540F43B... == code-identical to RandSeed baseline CC505716...):
    //   dispatch : sub_6FAC50 @0x006FAC50  (client message handler for wIdent 0x121D)
    //   core     : sub_6A3928 @0x006A3928  (the transaction; result in EAX)
    //   validate : sub_6A2FAC @0x006A2FAC  (-1 / -2 / -3)
    //   name-in  : sub_6A3148 @0x006A3148  (target item name present in [mgr+0x1C] config)
    //   mats     : sub_6A3260 @0x006A3260  (all 3 material ids present -> else -4)
    //   qty      : sub_6A330C @0x006A330C  (durability/quantity vs [mgr+0x1C] table -> else -5)
    //   find     : sub_73CF08 @0x0073CF08  (find item in player list [player+0x508] by field +0x18 == id)
    //   apply    : sub_6A3634 @0x006A3634  (on success: category increments + notify 0x38FF)
    //   category : sub_6A3580 @0x006A3580  (config entry byte [+0x14] -> 1/2/3)
    //   consume  : sub_6A3494 @0x006A3494  (delete the 3 material ids), sub_6A34E8 @0x006A34E8 (reduce 4th)
    //   Randomize: sub_4034AC (System.Randomize),   Random(n): sub_403B4C (bounded RandSeed)
    //
    // This is the ONLY runtime System.Randomize consumer in the whole server
    // (RandSeed production-cutover audit step 3, staging/delphi_randseed_production_cutover_audit_20260731.md).
    // It is intentionally kept DORMANT and is not wired to the live 4637 case in
    // TPlayObject.Message.cs: enabling it in production is blocked on the global RandSeed
    // owner cutover, because a runtime Randomize()+Random(800) executed off the original
    // single game-loop thread order would pollute the entire global random sequence.

    /// <summary>
    /// Exact raw result codes returned by the original core sub_6A3928 (value == wire value
    /// placed into SendDefMessage wParam by dispatch sub_6FAC50).
    /// </summary>
    public enum NativeUpdateClothesResult
    {
        /// <summary>Random(800) &lt; 100. item[+0x49]++ and category stat increments applied.</summary>
        Success = 0,
        /// <summary>sub_6A2FAC: target id not found in player list [player+0x508] (0FFFFFFFFh).</summary>
        TargetNotFound = -1,
        /// <summary>sub_6A2FAC: sub_6A3148 false — item name not in upgradeable-target config (0FFFFFFFEh).</summary>
        TargetNotUpgradable = -2,
        /// <summary>sub_6A2FAC: item[+0x49] &gt;= 3 already at max level (0FFFFFFFDh).</summary>
        TargetMaxLevel = -3,
        /// <summary>sub_6A3260 false — the 3 required material ids are not all present (0FFFFFFFCh).</summary>
        MaterialsMissing = -4,
        /// <summary>sub_6A330C false — material durability/quantity below the config requirement (0FFFFFFFBh).</summary>
        MaterialsInsufficient = -5,
        /// <summary>Random(800) &gt;= 100 — upgrade roll failed (0FFFFFFFAh).</summary>
        RandomFail = -6,
        /// <summary>Core entry guard: player == null (0FFFFFF9Dh, EBX default).</summary>
        NoPlayer = -99,
    }

    /// <summary>
    /// sub_6A3580 category (config entry byte [+0x14]); selects which stat byte a success increments.
    /// </summary>
    public enum NativeUpdateClothesCategory
    {
        None = 0,
        One = 1,
        Two = 2,
        Three = 3,
    }

    /// <summary>
    /// Side-effect-free precondition snapshot. Dormant: the model never reads live game state; each
    /// flag stands for the boolean outcome of the corresponding original guard, evaluated in order.
    /// </summary>
    public readonly struct NativeUpdateClothesContext
    {
        /// <summary>sub_6A3928 top / sub_6A2FAC top: the acting TPlayObject is non-null.</summary>
        public bool HasPlayer { get; init; }
        /// <summary>sub_73CF08(player, id) != null — target item exists in the player list.</summary>
        public bool TargetFound { get; init; }
        /// <summary>sub_6A3148: target item name is present in the upgradeable-target config [mgr+0x1C].</summary>
        public bool TargetInConfig { get; init; }
        /// <summary>Current value of target item[+0x49] (upgrade level); success requires &lt; 3.</summary>
        public int TargetLevel { get; init; }
        /// <summary>sub_6A3260: all three material ids are present in the player list.</summary>
        public bool MaterialsPresent { get; init; }
        /// <summary>sub_6A330C: material durability/quantity satisfies the config requirement.</summary>
        public bool MaterialsSufficient { get; init; }
        /// <summary>sub_6A3580 category resolved for the target on the success path.</summary>
        public NativeUpdateClothesCategory Category { get; init; }
    }

    /// <summary>
    /// Result of one modeled transaction: the raw code plus the exact byte-field mutations the
    /// original apply (sub_6A3634) performs on success, and the dispatch wParam.
    /// </summary>
    public readonly struct NativeUpdateClothesOutcome
    {
        public NativeUpdateClothesResult Result { get; init; }
        /// <summary>Value of item[+0x49] after the transaction (== input level, +1 only on success).</summary>
        public int NewTargetLevel { get; init; }
        /// <summary>item[+0x2A] increment (always +1 on success, 0 otherwise).</summary>
        public int Delta2A { get; init; }
        /// <summary>item[+0x2B] increment (always +1 on success, 0 otherwise).</summary>
        public int Delta2B { get; init; }
        /// <summary>item[+0x2C] increment (+1 iff success and category == 1).</summary>
        public int Delta2C { get; init; }
        /// <summary>item[+0x2D] increment (+1 iff success and category == 2).</summary>
        public int Delta2D { get; init; }
        /// <summary>item[+0x2E] increment (+1 iff success and category == 3).</summary>
        public int Delta2E { get; init; }

        /// <summary>
        /// dispatch sub_6FAC50: SendDefMessage(SM_UPDATE_CLOTHES, wParam=result, 0, 0, 0, "").
        /// The raw core result is forwarded verbatim as wParam.
        /// </summary>
        public int DispatchWParam => (int)Result;
    }

    public static class NativeUpdateClothesTransaction
    {
        /// <summary>CM_UPDATE_CLOTHES / SM_UPDATE_CLOTHES = 0x121D. dispatch sub_6FAC50: mov dx, 121Dh.</summary>
        public const int Ident = 4637;

        /// <summary>Random(800). core sub_6A3928: mov eax, 320h; call sub_403B4C.</summary>
        public const int RandomBound = 800;

        /// <summary>Success iff roll &lt; 100. core: cmp eax, 64h; jge fail. (=> ~12.5% success).</summary>
        public const int SuccessThreshold = 100;

        /// <summary>Level cap. sub_6A2FAC: cmp byte ptr [item+0x49], 3; jnb -&gt; -3.</summary>
        public const int MaxLevel = 3;

        // Field offsets on the target item, verified from the disassembly (documented, not dereferenced here).
        public const int ItemLevelOffset = 0x49; // sub_6A2FAC guard, sub_6A3928 success inc
        public const int ItemStatAOffset = 0x2A; // sub_6A3634 all categories
        public const int ItemStatBOffset = 0x2B; // sub_6A3634 all categories
        public const int ItemStatCOffset = 0x2C; // sub_6A3634 category 1
        public const int ItemStatDOffset = 0x2D; // sub_6A3634 category 2
        public const int ItemStatEOffset = 0x2E; // sub_6A3634 category 3

        // TPlayObject vtable slots exercised by this transaction (documented for the eventual wiring).
        public const int VtblSendDefMessage = 0x250; // dispatch sub_6FAC50: call [player+0x250]
        public const int VtblSendDelItem = 0x24C;    // sub_6A3494 / sub_6A34E8: remove consumed material
        public const int VtblSendUpdateItem = 0x260; // sub_6A34E8: reduce 4th material durability
        public const int NotifyAddStatIdent = 0x38FF; // sub_6A3634: call [player+0xD4] with wIdent 0x38FF

        /// <summary>
        /// Evaluate the original result ladder in exact source order. <paramref name="postRandomizeSeed"/>
        /// is the RandSeed value the original Random(800) advances from (i.e. the seed just after the
        /// original's Randomize()). The model does NOT itself call Randomize(): the process-global
        /// perf-counter reseed only becomes correct once the RandSeed owner cutover lands, so the
        /// caller injects the seed to keep the boundary deterministically verifiable.
        /// </summary>
        public static NativeUpdateClothesOutcome Evaluate(in NativeUpdateClothesContext context, uint postRandomizeSeed)
        {
            // sub_6A3928: test esi,esi; jz -> EBX(-99)
            if (!context.HasPlayer)
                return Reject(NativeUpdateClothesResult.NoPlayer, context.TargetLevel);

            // sub_6A2FAC: find target; not found -> or ebx,-1
            if (!context.TargetFound)
                return Reject(NativeUpdateClothesResult.TargetNotFound, context.TargetLevel);
            // sub_6A2FAC: sub_6A3148 false -> ebx = -2
            if (!context.TargetInConfig)
                return Reject(NativeUpdateClothesResult.TargetNotUpgradable, context.TargetLevel);
            // sub_6A2FAC: cmp byte[item+0x49],3; jnb -> ebx = -3
            if (context.TargetLevel >= MaxLevel)
                return Reject(NativeUpdateClothesResult.TargetMaxLevel, context.TargetLevel);

            // sub_6A3260 false -> loc_6A3A18: ebx = -4
            if (!context.MaterialsPresent)
                return Reject(NativeUpdateClothesResult.MaterialsMissing, context.TargetLevel);
            // sub_6A330C false -> loc_6A3A11: ebx = -5
            if (!context.MaterialsSufficient)
                return Reject(NativeUpdateClothesResult.MaterialsInsufficient, context.TargetLevel);

            // sub_6A3928: Randomize(); eax=Random(800); cmp eax,64h; jge -> ebx = -6
            int roll = Roll(postRandomizeSeed);
            if (roll >= SuccessThreshold)
                return Reject(NativeUpdateClothesResult.RandomFail, context.TargetLevel);

            // success: inc byte[item+0x49]; sub_6A3634 applies category increments; ebx = 0
            return ApplySuccess(context);
        }

        /// <summary>
        /// One bounded RandSeed draw matching sub_403B4C exactly, seeded from
        /// <paramref name="postRandomizeSeed"/>. Returns the 0..799 roll.
        /// </summary>
        public static int Roll(uint postRandomizeSeed)
        {
            DelphiRandom.Seed = postRandomizeSeed;
            return DelphiRandom.Random(RandomBound);
        }

        private static NativeUpdateClothesOutcome Reject(NativeUpdateClothesResult result, int level)
        {
            return new NativeUpdateClothesOutcome
            {
                Result = result,
                NewTargetLevel = level,
            };
        }

        private static NativeUpdateClothesOutcome ApplySuccess(in NativeUpdateClothesContext context)
        {
            // sub_6A3928: inc byte ptr [item+0x49]
            // sub_6A3634: all categories inc [+0x2A] and [+0x2B]; then one of [+0x2C]/[+0x2D]/[+0x2E]
            return new NativeUpdateClothesOutcome
            {
                Result = NativeUpdateClothesResult.Success,
                NewTargetLevel = context.TargetLevel + 1,
                Delta2A = 1,
                Delta2B = 1,
                Delta2C = context.Category == NativeUpdateClothesCategory.One ? 1 : 0,
                Delta2D = context.Category == NativeUpdateClothesCategory.Two ? 1 : 0,
                Delta2E = context.Category == NativeUpdateClothesCategory.Three ? 1 : 0,
            };
        }
    }
}
