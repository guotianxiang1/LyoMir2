namespace GameSvr
{
    // Dormant, fail-closed models of five PAS-script ("TPsNpc") native handler
    // clusters of the 战神 M2Server binary (M2Server_unpacked_fixed.exe, image
    // base 0x00400000, SHA256 5540f43bc58d…c049670b14e). These are the
    // "category-A" clusters from staging/pas_divergence_census_20260801.md: each
    // has a real runtime body (bound name→body by sub_731350) AND an existing
    // disassembly dump, so the exact decision ladder is modeled here without any
    // fresh idat pass.
    //
    // Every model captures only the PURE decision ladder. Each side-effect
    // executor — the authen validators (sub_6F9994/sub_6F9C64/sub_6F9A28/
    // sub_6F9D58/sub_6F9CF8/sub_61827C/sub_6180E8/sub_618438), the SendMsg
    // dispatch (sub_768BE0 / creature vtable[0xD4]), the YB-deal precondition and
    // cancel executors (sub_6C7D88 / sub_6D3694 / vtable[0x244]), the camp-animal
    // spawn (sub_67DA68), and the corps/guild store writers (corps-manager
    // vtable[+8], guild-manager vtable[0x3C]) — is abstracted as an INPUT. Nothing
    // here mutates state; it is a reference oracle for a later wiring pass and for
    // AuditTools.
    //
    // COORDINATION: CreateSelfCorps / CreateSelfGild dispatch into the
    // guild/corps social-org persistence (gamedata.* writes) owned by the guild
    // domain. Only the script-API WRAPPER ladder is modeled here (the gate + the
    // client result code); the actual store write is left abstract — coordinate
    // with the guild domain before wiring so the persistence is not double-modeled.

    // =====================================================================
    // CLUSTER 1 — AUTH / 授权 family
    //   ActiveAuthen        function, wrapper sub_6F977C (name→body @0x00732283)
    //   ActiveDelAuthen     function, wrapper sub_6F9888 (name→body @0x00732294)
    //   AuthByHelped        function, wrapper sub_6F9AB0 (name→body @0x007322C7)
    //   HelpOtherAuthen     function, wrapper sub_6F9BC8 (name→body @0x007322B6)
    //   shared order validator sub_6F9994 (codes 1/2/3/persist)
    //   Evidence: staging/ida_auth_by_helped_exact_20260719.txt,
    //             staging/ida_auth_derived_arrays_20260719.txt
    //   Currently: PasApiBridge activeauthen/activedelauthen/authbyhelped/
    //   helpotherauthen => RejectUnsupportedNativeApi (activeauthen/helpotherauthen
    //   are MIXED — one dispatch table already stubs, one rejects).
    // =====================================================================

    /// <summary>Shared authen "enabled" service gate (byte [*off_7D6534 + 8]).
    /// When false, ActiveAuthen/ActiveDelAuthen do nothing and return 0 with NO
    /// message (native `cmp byte [eax+8],0` / `jz` at 0x006F97A6-AA).</summary>
    public static class NativeAuthenServiceGate
    {
        public const int GlobalManagerPtr = 0x007D6534;   // ds:off_7D6534
        public const int EnabledByteOffset = 0x08;        // [*off_7D6534 + 8]
    }

    /// <summary>Result codes emitted by the shared authen order validator
    /// sub_6F9994 (and its delete-mirror sub_6F9D58).</summary>
    public static class NativeAuthenResultCode
    {
        public const int Success = 1;         // authed (or authed-without-commit)
        public const int AlreadyAuthed = 2;   // bit already set for this level
        public const int InvalidOrder = 3;    // AuthenOrder not in 1..3
        // any other value == the persist result returned by sub_618438
    }

    /// <summary>
    /// Dormant model of the shared authen order validator sub_6F9994
    /// (0x006F9994). Pure decision over the per-order auth bitmask; the actual
    /// bit set/clear and the persistence call sub_618438 are abstracted.
    /// order = AuthenOrder (edx, valid 1..3); level = the level bit index (cl);
    /// commit = the a4 "persist now" flag.
    /// </summary>
    public static class NativeAuthenOrderValidator
    {
        public const int WrapperAddress = 0x006F9994;
        public const int DeleteMirrorAddress = 0x006F9D58;  // sub_6F9D58
        public const int PersistAddress = 0x00618438;       // sub_618438

        /// <param name="order">AuthenOrder (native `(order-1) unsigned > 2`).</param>
        /// <param name="alreadyAuthed">bit test [self+order+0x193B]:level already set.</param>
        /// <param name="commit">a4 flag: persist immediately.</param>
        /// <param name="persistResult">sub_618438(order, self) result; only read
        /// when <paramref name="commit"/> is true (abstracted input).</param>
        public static int Validate(int order, bool alreadyAuthed, bool commit,
            int persistResult)
        {
            if ((uint)(order - 1) > 2)
                return NativeAuthenResultCode.InvalidOrder;      // 3
            if (alreadyAuthed)
                return NativeAuthenResultCode.AlreadyAuthed;     // 2
            // native sets the level bit here (side effect, abstracted)
            if (!commit)
                return NativeAuthenResultCode.Success;           // 1
            return persistResult;   // 1 == ok; otherwise caller restores the mask
        }
    }

    /// <summary>Outcome of ActiveAuthen (sub_6F977C) / ActiveDelAuthen
    /// (sub_6F9888) — structurally identical wrappers.</summary>
    public enum NativeActiveAuthenOutcome
    {
        /// <summary>Service disabled ([*off_7D6534+8]==0): no validator run, no
        /// message, function returns 0.</summary>
        Disabled,
        /// <summary>Validator returned 1: success post-processing (sub_6F9FFC
        /// [+ sub_6FA080 for ActiveAuthen only]) then the success SendMsg.</summary>
        Success,
        /// <summary>Validator returned non-1: the failure SendMsg carrying the
        /// code.</summary>
        Failure
    }

    /// <summary>Dormant model of ActiveAuthen (sub_6F977C) and its delete-mirror
    /// ActiveDelAuthen (sub_6F9888). The level==100 branch selects a special
    /// validator (sub_6F9C64 / sub_6F9A28) instead of sub_6F9994 / sub_6F9D58;
    /// both return a code and are abstracted as <c>validatorResult</c>. The
    /// success post-processors and SendMsg (sub_768BE0) are abstracted.</summary>
    public static class NativeActiveAuthenPlanner
    {
        public const int ActiveAuthenAddress = 0x006F977C;
        public const int ActiveDelAuthenAddress = 0x006F9888;
        public const int SpecialValidatorActive = 0x006F9C64;   // sub_6F9C64 (lvl 100)
        public const int SpecialValidatorDelete = 0x006F9A28;   // sub_6F9A28 (lvl 100)
        public const int SendMsgAddress = 0x00768BE0;           // sub_768BE0
        public const int SendMsgSubtype = 0x5F;                 // dx '_' (both wrappers)
        public const int SpecialLevel = 100;                    // ecx == 100 selector

        /// <param name="serviceEnabled">NativeAuthenServiceGate state.</param>
        /// <param name="validatorResult">code from the selected validator
        /// (sub_6F9994/6F9D58 normal, or 6F9C64/6F9A28 when level==100).</param>
        public static NativeActiveAuthenOutcome Plan(bool serviceEnabled,
            int validatorResult)
        {
            if (!serviceEnabled)
                return NativeActiveAuthenOutcome.Disabled;
            if (validatorResult == NativeAuthenResultCode.Success)
                return NativeActiveAuthenOutcome.Success;
            return NativeActiveAuthenOutcome.Failure;
        }

        /// <summary>The integer the native function returns (0 when disabled).</summary>
        public static int ResolveReturn(bool serviceEnabled, int validatorResult)
            => serviceEnabled ? validatorResult : 0;
    }

    /// <summary>Outcome of AuthByHelped (sub_6F9AB0).</summary>
    public enum NativeAuthByHelpedOutcome
    {
        /// <summary>No pending helped-auth ([self+0x193E]==0): return 5.</summary>
        NoPending,
        /// <summary>Precheck sub_6F9CF8(self,lv,order) true (already/invalid):
        /// return 5.</summary>
        PrecheckBlocked,
        /// <summary>Eligibility sub_61827C(mgr,self) false: return 4.</summary>
        Ineligible,
        /// <summary>All gates pass: delegate to ActiveAuthen (sub_6F977C); on
        /// result 1 clear the pending flag [self+0x193E]=0 and send success msg,
        /// else send failure msg. Return the delegated code.</summary>
        Delegated
    }

    /// <summary>Dormant model of AuthByHelped (sub_6F9AB0). sub_6F9CF8,
    /// sub_61827C, sub_6F977C (ActiveAuthen) and SendMsg are abstracted.</summary>
    public static class NativeAuthByHelpedPlanner
    {
        public const int WrapperAddress = 0x006F9AB0;
        public const int PendingFlagOffset = 0x193E;            // byte [self+0x193E]
        public const int PrecheckAddress = 0x006F9CF8;          // sub_6F9CF8
        public const int EligibilityAddress = 0x0061827C;       // sub_61827C
        public const int DelegateAddress = 0x006F977C;          // ActiveAuthen
        public const int SendMsgSubtype = 0x5E;                 // dx '^'
        public const int NoPendingOrBlockedCode = 5;
        public const int IneligibleCode = 4;

        public static NativeAuthByHelpedOutcome Plan(bool pending,
            bool precheckBlocks, bool eligible)
        {
            if (!pending)
                return NativeAuthByHelpedOutcome.NoPending;       // 5
            if (precheckBlocks)
                return NativeAuthByHelpedOutcome.PrecheckBlocked; // 5
            if (!eligible)
                return NativeAuthByHelpedOutcome.Ineligible;      // 4
            return NativeAuthByHelpedOutcome.Delegated;
        }

        /// <summary>The integer the native function returns.</summary>
        /// <param name="activeAuthenResult">ActiveAuthen delegated code; only
        /// meaningful on the Delegated path.</param>
        public static int ResolveReturn(NativeAuthByHelpedOutcome outcome,
            int activeAuthenResult)
        {
            return outcome switch
            {
                NativeAuthByHelpedOutcome.NoPending => NoPendingOrBlockedCode,
                NativeAuthByHelpedOutcome.PrecheckBlocked => NoPendingOrBlockedCode,
                NativeAuthByHelpedOutcome.Ineligible => IneligibleCode,
                _ => activeAuthenResult
            };
        }
    }

    /// <summary>Dormant model of HelpOtherAuthen (sub_6F9BC8): a thin delegate to
    /// the "help other" executor sub_6180E8(mgr, [self+0x588], [self+0x58C]); the
    /// function returns that code and, when it is 1, sends a success SendMsg. The
    /// executor and SendMsg are abstracted.</summary>
    public static class NativeHelpOtherAuthenPlanner
    {
        public const int WrapperAddress = 0x006F9BC8;
        public const int ExecutorAddress = 0x006180E8;          // sub_6180E8
        public const int SendMsgSubtype = 0x5E;                 // dx '^'

        /// <returns>true when the native sends the success message (result==1).</returns>
        public static bool SendsSuccessMessage(int executorResult)
            => executorResult == NativeAuthenResultCode.Success;

        public static int ResolveReturn(int executorResult) => executorResult;
    }

    // =====================================================================
    // CLUSTER 2 — ClientSellerCancelYbDeal (元宝寄售卖家取消)
    //   procedure, wrapper sub_6CB9F0 (name→body @0x00732XXX)
    //   Evidence: staging/pas-finish/ida-dynroom-lifecycle.txt (0x006CB9F0)
    //   Currently: PasApiBridge clientsellercancelybdeal => RejectUnsupportedNativeApi.
    // =====================================================================

    /// <summary>Outcome of ClientSellerCancelYbDeal (sub_6CB9F0).</summary>
    public enum NativeYbDealSellerCancelOutcome
    {
        /// <summary>Any precondition fails — no active cancelable seller deal:
        /// silent no-op. Preconditions (all required):
        /// sub_6C7D88(self,1) true, [self+0x75C] &gt; 0 (signed), and
        /// [self+0x758] != 0 (unsigned). (native 0x006CBA01-15).</summary>
        NoCancelableDeal,
        /// <summary>Preconditions pass AND vtable[0x244]() true: execute the
        /// cancel sub_6D3694(0, 0, [self+0x758]) (wIdent 0x75).</summary>
        ExecuteCancel,
        /// <summary>Preconditions pass but vtable[0x244]() false: send the
        /// "cannot cancel now" sysmsg via vtable[0xD4] (color 0x38FF,
        /// dword_6CBA64).</summary>
        RejectNotCancelable
    }

    /// <summary>Dormant model of ClientSellerCancelYbDeal (sub_6CB9F0). The
    /// precondition probe sub_6C7D88, the can-cancel check vtable[0x244], the
    /// cancel executor sub_6D3694 and the sysmsg vtable[0xD4] are abstracted.</summary>
    public static class NativeYbDealSellerCancelPlanner
    {
        public const int WrapperAddress = 0x006CB9F0;
        public const int PreconditionProbeAddress = 0x006C7D88;  // sub_6C7D88
        public const int CancelExecutorAddress = 0x006D3694;     // sub_6D3694
        public const int CountOffset = 0x75C;                    // [self+0x75C] signed
        public const int DealIdOffset = 0x758;                   // [self+0x758] unsigned
        public const int CanCancelVtableSlot = 0x244;            // vtable[0x244]
        public const int SysMsgVtableSlot = 0xD4;                // vtable[0xD4]
        public const int CancelWIdent = 0x75;                    // 'u'
        public const int RejectSysMsgColor = 0x38FF;

        /// <param name="hasCancelable">sub_6C7D88(self,1) result.</param>
        /// <param name="count">[self+0x75C] (signed; native `jle`).</param>
        /// <param name="dealId">[self+0x758] (unsigned; native `jbe` vs 0).</param>
        /// <param name="canCancelNow">vtable[0x244]() result.</param>
        public static NativeYbDealSellerCancelOutcome Plan(bool hasCancelable,
            int count, uint dealId, bool canCancelNow)
        {
            if (!hasCancelable || count <= 0 || dealId == 0)
                return NativeYbDealSellerCancelOutcome.NoCancelableDeal;
            if (canCancelNow)
                return NativeYbDealSellerCancelOutcome.ExecuteCancel;
            return NativeYbDealSellerCancelOutcome.RejectNotCancelable;
        }
    }

    // =====================================================================
    // CLUSTER 3 — CreateCampAnimal (阵营守护创建)
    //   procedure, wrapper sub_6EB7D8 (name→body @0x00732XXX)
    //   Evidence: staging/pas-finish/ida-dynroom-lifecycle.txt (0x006EB7D8)
    //   Currently: PasApiBridge createcampanimal => RejectUnsupportedNativeApi.
    // =====================================================================

    /// <summary>
    /// Dormant model of CreateCampAnimal (sub_6EB7D8). There is NO decision gate:
    /// the wrapper unconditionally (1) spawns via sub_67DA68(0, name, x, y, …args)
    /// then (2) formats a 3-argument system message from the spawned handle and
    /// sends it via creature vtable[0xD4] with wIdent 0xFFDB. Both the spawn
    /// executor and the SendSysMsg are abstracted; this type only records the
    /// fixed dispatch facts so a wiring pass reproduces them exactly.
    /// </summary>
    public static class NativeCreateCampAnimalPlanner
    {
        public const int WrapperAddress = 0x006EB7D8;
        public const int SpawnExecutorAddress = 0x0067DA68;     // sub_67DA68
        public const int MessageFormatAddress = 0x0040DCC0;     // sub_40DCC0 (3 args)
        public const int SysMsgVtableSlot = 0xD4;               // vtable[0xD4]
        public const int NotifyWIdent = 0xFFDB;                 // LOWORD(-37)
        public const int MessageArgCount = 3;

        /// <summary>The wrapper always dispatches (no gate).</summary>
        public static bool AlwaysDispatches => true;
    }

    // =====================================================================
    // CLUSTER 4 — CreateSelfCorps (自建军团)
    //   function, wrapper sub_6ADD08 (name→body @0x007321D9)
    //   Evidence: staging/ida_checkauthen_deep_20260716.txt (0x006ADD08)
    //   Currently: PasApiBridge createselfcorps => RejectUnsupportedNativeApi.
    //   STORE WRITE (corps-manager vtable[+8]) = guild/corps domain — abstract only.
    // =====================================================================

    /// <summary>Outcome of CreateSelfCorps (sub_6ADD08).</summary>
    public enum NativeCreateSelfCorpsOutcome
    {
        /// <summary>Caller already owns a corps ([self+0x0AE8] != 0): result code
        /// 3, NO store write (native `cmp [ebx+0AE8h],0` / `jnz` 0x006ADD3B-42).</summary>
        AlreadyHasCorps,
        /// <summary>No existing corps: delegate to the corps-manager creator
        /// vtable[+8](order, builtParams); the result code is whatever it returns.
        /// (Store write abstracted — guild/corps domain.)</summary>
        DelegateCreate
    }

    /// <summary>Dormant model of CreateSelfCorps (sub_6ADD08). Regardless of the
    /// branch the wrapper then sends the client result via creature vtable[+592]
    /// (wIdent 0x11AC / 4524) carrying the code, and runs post-processing
    /// sub_6AEE04. The corps-manager write and the SendMsg are abstracted.</summary>
    public static class NativeCreateSelfCorpsPlanner
    {
        public const int WrapperAddress = 0x006ADD08;
        public const int ExistingCorpsOffset = 0x0AE8;          // [self+0x0AE8]
        public const int ManagerLookupAddress = 0x006ADA3C;     // sub_6ADA3C
        public const int CreatorVtableSlot = 0x08;              // corps-mgr vtable[+8]
        public const int ResultMsgVtableSlot = 0x250;           // vtable[+592]
        public const int ResultWIdent = 0x11AC;                 // 4524
        public const int PostProcessAddress = 0x006AEE04;       // sub_6AEE04
        public const int AlreadyHasCorpsCode = 3;

        /// <param name="alreadyHasCorps">[self+0x0AE8] != 0.</param>
        public static NativeCreateSelfCorpsOutcome Plan(bool alreadyHasCorps)
        {
            if (alreadyHasCorps)
                return NativeCreateSelfCorpsOutcome.AlreadyHasCorps;
            return NativeCreateSelfCorpsOutcome.DelegateCreate;
        }

        /// <summary>The result code carried to the client (and returned).</summary>
        /// <param name="managerResult">corps-manager creator result; only read on
        /// the DelegateCreate path (abstracted input).</param>
        public static int ResolveReturn(bool alreadyHasCorps, int managerResult)
            => alreadyHasCorps ? AlreadyHasCorpsCode : managerResult;
    }

    // =====================================================================
    // CLUSTER 5 — CreateSelfGild (自建行会)
    //   function, wrapper sub_6ADDA8 (name→body @0x007321EA)
    //   Evidence: staging/ida_checkauthen_deep_20260716.txt (0x006ADDA8)
    //   Currently: PasApiBridge createselfgild => RejectUnsupportedNativeApi.
    //   STORE WRITE (guild-manager vtable[0x3C]) = guild domain — abstract only.
    // =====================================================================

    /// <summary>
    /// Dormant model of CreateSelfGild (sub_6ADDA8). There is NO wrapper-level
    /// gate: it unconditionally resolves the guild manager (sub_6ADA3C) and calls
    /// the creator vtable[0x3C](order, [self+0x588], [self+0x58C]); the returned
    /// code is sent to the client via vtable[+592] (wIdent 4564) and returned.
    /// All eligibility (already in a guild, name/cost checks) lives INSIDE the
    /// abstracted manager write — coordinate with the guild domain before wiring.
    /// </summary>
    public static class NativeCreateSelfGildPlanner
    {
        public const int WrapperAddress = 0x006ADDA8;
        public const int ManagerLookupAddress = 0x006ADA3C;     // sub_6ADA3C
        public const int CreatorVtableSlot = 0x3C;              // guild-mgr vtable[0x3C]
        public const int NameFieldOffset = 0x588;               // [self+0x588]
        public const int NameField2Offset = 0x58C;              // [self+0x58C]
        public const int ResultMsgVtableSlot = 0x250;           // vtable[+592]
        public const int ResultWIdent = 4564;                   // 0x11D4

        /// <summary>The wrapper always delegates (no gate).</summary>
        public static bool AlwaysDelegates => true;

        /// <param name="managerResult">guild-manager creator result (abstracted).</param>
        public static int ResolveReturn(int managerResult) => managerResult;
    }
}
