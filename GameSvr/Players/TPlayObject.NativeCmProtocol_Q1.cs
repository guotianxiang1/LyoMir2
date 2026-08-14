using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// CM missing-set Quarter 1 — idents 1054..1260, the lowest 25 of the 99-item gap
    /// between 战神's CM dispatch arms (sub_6D7D68, selector root 0x6D805C) and the C#
    /// Operate() coverage. The split is the sibling-consistent build()/covered() quarter
    /// (25/25/24/25); quarter 4 = 4125..4651 matches cm-4's merged range, proving the
    /// boundaries line up. See tools/cm1_re/_cm1_q1_opcodes.txt for the derivation.
    ///
    /// Dispatcher frame (established at 0x6D7D68..0x6D7D97), and how it lands in
    /// TProcessMessage (verified mapping, ProcessUserMessage DEFAULT case + the CM_SPELL
    /// note in UsrEngn.cs): [ebp-4]=Self, [ebp-0x34]=wire record, [ebp-8]=body string,
    /// ESI=body length. Record fields -> message: [rec+0]=Recog=nParam1,
    /// word[rec+6]=Param=nParam2, word[rec+8]=Tag=nParam3, word[rec+0xA]=Series=wParam,
    /// body=sMsg/Payload, body length=nBodyLen.
    ///
    /// Disposition: all 25 are fail-closed. Every leaf reaches a REAL worker (never the
    /// 0x6DBC2C no-op sink — the missing set excludes no-ops), but every worker's terminal
    /// action and reply body are a function of runtime subsystem state that is not a
    /// constant in the image (shop/mall mgr [[0x7D5D98]], 元宝寄售 mgr [[0x7D6ABC]],
    /// booth/stall mgr [[0x7D7190]], quiz/broadcast mgr [[0x7D62DC]], std-item/strengthen
    /// tables [[0x7D5D6C]]/[[0x7D6630]]/[[0x7D5F20]], the task-board & piece-up scripts,
    /// and the equip-secret lock [player+0x711]). Reproducing a reply would put invented
    /// bytes on the wire, so each is dropped through <see cref="NativeCmQ1FailClosed"/>,
    /// which records the gap once per ident. This mirrors cm-4 (whole quarter fail-closed)
    /// and the C# port's own dormancy of these write-side subsystems (NativeStallWriteGate
    /// off; NativeYbDealPurchaseStateMachine dormant; only the read-only 元宝 views
    /// CM 1252/1253/1256/1257 are wired). Pre-gates that read modelled state only are
    /// reproduced: CM 1061/1080 empty-body silence is already handled upstream by
    /// NativeClientBodyLengthGate; CM 1248's body&lt;0x20 silence is reproduced here from
    /// nBodyLen.
    ///
    /// INTEGRATOR HOOKUP (single line, do NOT touch the Operate() switch otherwise):
    /// in TPlayObject.Message.cs Operate()'s `default:` arm, call this first so a handled
    /// ident short-circuits before the social/base fallthrough —
    ///
    ///     default:
    ///         if (!TryHandleNativeCmQ1(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeSocialProtocol(ProcessMsg))
    ///         {
    ///             result = base.Operate(ProcessMsg);
    ///         }
    ///         break;
    /// </summary>
    public partial class TPlayObject
    {
        private bool TryHandleNativeCmQ1(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1054:
                    ClientNativeMallSubmitCountdown();
                    return true;
                case Grobal2.CM_1055:
                    ClientNativeMallSubmitTier();
                    return true;
                case Grobal2.CM_1056:
                    ClientNativeMallSubmitGated56();
                    return true;
                case Grobal2.CM_1057:
                    ClientNativeMallSubmitGated57();
                    return true;
                case Grobal2.CM_1059:
                    ClientNativeActivityConfirm();
                    return true;
                case Grobal2.CM_1061:
                    ClientNativeSkillStoneCopy();
                    return true;
                case Grobal2.CM_1068:
                    ClientNativeEquipSecretInput();
                    return true;
                case Grobal2.CM_1084:
                    ClientNativeEquipSecretTimer();
                    return true;
                case Grobal2.CM_1080:
                    ClientNativeStrengthenTableOp();
                    return true;
                case Grobal2.CM_1090:
                    ClientNativeQuizAnswerSelf();
                    return true;
                case Grobal2.CM_1200:
                    ClientNativeQuizAnswerBody();
                    return true;
                case Grobal2.CM_1217:
                    ClientNativeBroadcastSubmit();
                    return true;
                case Grobal2.CM_1210:
                    ClientNativeBoothTrade10();
                    return true;
                case Grobal2.CM_1211:
                    ClientNativeBoothTrade11();
                    return true;
                case Grobal2.CM_1212:
                    ClientNativeBoothTrade12();
                    return true;
                case Grobal2.CM_1213:
                    ClientNativeBoothTrade13();
                    return true;
                case Grobal2.CM_1214:
                    ClientNativeBoothTrade14();
                    return true;
                case Grobal2.CM_1248:
                    ClientNativePieceUpSynth(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_1250:
                    ClientNativeTaskBoardList();
                    return true;
                case Grobal2.CM_1251:
                    ClientNativeYbConsignmentGated();
                    return true;
                case Grobal2.CM_1254:
                    ClientNativeYbConsignmentAccept();
                    return true;
                case Grobal2.CM_1255:
                    ClientNativeYbConsignment55();
                    return true;
                case Grobal2.CM_1258:
                    ClientNativeYbConsignment58();
                    return true;
                case Grobal2.CM_1259:
                    ClientNativeYbConsignmentOpenSubmit();
                    return true;
                case Grobal2.CM_1260:
                    ClientNativeYbSetTradeAmount();
                    return true;
                default:
                    return false;
            }
        }

        // ================================================================================
        // 商城/mall submit — shopMgr [[0x7D5D98]] (named by NativeGmItemCommands, case
        // 0x624DD6 sub_63B4E4(shopMgr@off_7D5D98,...)); the submit wrapper 0x6D3694 packs
        // {name obj+0xAF4, obj+0xB09, map obj+0x106, obj+0xB33 [+ body]} and calls
        // 0x637A00(shopMgr, subcmd in ESI). The manager's runtime state decides the reply.
        // ================================================================================

        /// <summary>
        /// CM 1054, leaf 0x6D942F (`E8 0C EF D2 FF` call GetTickCount 0x408340). The leaf
        /// throttles on <c>[self+0x788]</c> — <c>0x6D943D cmp eax,0x7D0 / 0x6D9442 jb
        /// 0x6DBC2C</c> drops when the last submit was under 2000 ms ago — then stamps
        /// <c>[self+0x788]</c> and calls the submit wrapper 0x6D3694 with subcmd ESI=0x7B
        /// and an extra arg from 0x7481F4. 0x6D3694 hands the packed record to
        /// shopMgr [[0x7D5D98]] 0x637A00; a busy result answers SM 0x38FF (0x6DBF88
        /// '…请稍候'). Neither <c>[self+0x788]</c> nor the shop manager is modelled here.
        /// </summary>
        private void ClientNativeMallSubmitCountdown()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1054, m_sCharName);
        }

        /// <summary>
        /// CM 1055, leaf 0x6D9492. Same <c>[self+0x788]</c>&lt;2000 ms throttle as CM 1054,
        /// then maps Param (word[rec+6]) 1..4 to subcmd {0x6F,0x75,0x7A,0x7D} (0x6D94C0
        /// four `dec ax`); a Param outside 1..4 lands on <c>0x6D94F8 je 0x6DBC2C</c>
        /// (silent). The chosen subcmd is submitted via 0x6D3694 (dx=0x6B) to shopMgr
        /// [[0x7D5D98]]. The throttle field and the manager are unmodelled, so the Param
        /// gate is never reachable faithfully.
        /// </summary>
        private void ClientNativeMallSubmitTier()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1055, m_sCharName);
        }

        /// <summary>
        /// CM 1056, leaf 0x6D953A -> worker 0x6CB9B4. The worker gates on 0x6C7D88(self,1)
        /// and <c>[self+0x758]&gt;0</c> (0x6CB9C9), then submits subcmd 0x76 via 0x6D3694 to
        /// shopMgr [[0x7D5D98]]. <c>[self+0x758]</c> and the manager are unmodelled.
        /// </summary>
        private void ClientNativeMallSubmitGated56()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1056, m_sCharName);
        }

        /// <summary>
        /// CM 1057, leaf 0x6D9547 -> worker 0x6CB9F0. Gates 0x6C7D88(self,1),
        /// <c>[self+0x75C]&gt;0</c>, <c>[self+0x758]&gt;0</c>, then <c>[vmt+0x244]</c>: on
        /// success submits subcmd 0x75, on failure answers SM 0x38FF (0x6CBA64
        /// '[失败]…单位不足，无法领取。'). The counters <c>[self+0x758]/[0x75C]</c> and
        /// shopMgr [[0x7D5D98]] are unmodelled, so the pass/fail split cannot be derived.
        /// </summary>
        private void ClientNativeMallSubmitGated57()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1057, m_sCharName);
        }

        /// <summary>
        /// CM 1059, leaf 0x6D9554 -> worker 0x6D7794. Throttles on <c>[self+0x744]</c>
        /// (10000 ms, 0x6D77B3 cmp edx,0x2710) behind the one-shot flag <c>[self+0x757]</c>,
        /// then calls 0x6E3944(self, dl=1). The throttle/flag fields and 0x6E3944's target
        /// subsystem are unmodelled.
        /// </summary>
        private void ClientNativeActivityConfirm()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1059, m_sCharName);
        }

        /// <summary>
        /// CM 1061, leaf 0x6D9579 -> worker 0x6CBDD4 (Recog, body string, body length).
        /// The empty-body silence (<c>66 85 F6 test si,si / 0F 86 jbe 0x6DBC2C</c>) is
        /// already reproduced upstream by NativeClientBodyLengthGate[1061]. For a non-empty
        /// body the worker requires <c>body&gt;=0x3C</c> (0x6CBE0D), copies a 0x3C-byte
        /// record, walks the bag list <c>[self+0x508]</c> matching <c>[item+0x18]</c>, and
        /// through managers [[0x7D5F20]]/[[0x7D6630]] copies a skill stone, answering
        /// SM 0x38FF/0xFFDB. The 0x3C record format and both managers are unmodelled.
        /// </summary>
        private void ClientNativeSkillStoneCopy()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1061, m_sCharName);
        }

        // ================================================================================
        // 装备密码锁 (equip-secret confirmation lock). NativeMakeItemUseDiamHost records
        // that this lock [player+0x711], SM_LOCKEQUIP(689) and the CM 1068/1084 handlers
        // were verified ABSENT from this C# server; the confirm-pending flag is therefore
        // permanently 0. The handlers themselves are unported, so their replies are withheld.
        // ================================================================================

        /// <summary>
        /// CM 1068, leaf 0x6D959B -> worker 0x6D1780. The worker copies the body, then
        /// dispatches on Param (word[rec+6]) through a 12-arm jump table at 0x6D17F8
        /// (0x6D17E8 cmp eax,0xB / ja default), driving the equipment-secret lock input;
        /// the default arm answers SM 0xFCFF (0x6D1A98 '系统已禁能交易输入…'). The lock
        /// state <c>[player+0x711]</c> and this whole subsystem are absent from this server.
        /// </summary>
        private void ClientNativeEquipSecretInput()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1068, m_sCharName);
        }

        /// <summary>
        /// CM 1084, leaf 0x6D95C9 -> worker 0x6D1AB8. Gates on <c>[self+0xB78]&gt;0</c> and
        /// 0x6C7D88(self,1), computes the remaining lock seconds
        /// <c>(0x2BF20-(now-[self+0x740]))/1000</c> and answers SM 0x2733/0x2737. The lock
        /// timer fields <c>[self+0xB78]/[0xB7B]/[0x74C]/[0x740]</c> are absent from this server.
        /// </summary>
        private void ClientNativeEquipSecretTimer()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1084, m_sCharName);
        }

        // ================================================================================
        // 强化/标准物品表 [[0x7D5D6C]] (off_7D5D6C: std-item + PowerupItem table, named by
        // NativeGmItemCommands / NativeStrengthenRecipeStore) with [[0x7D6630]].
        // ================================================================================

        /// <summary>
        /// CM 1080, leaf 0x6D95D6 -> worker 0x6CF49C (body string, body length). The
        /// empty-body silence is reproduced upstream by NativeClientBodyLengthGate[1080].
        /// For a non-empty body the worker requires <c>body&gt;=0x28</c>, then gates on four
        /// flags <c>[self+0xD48]/[0xD5D]/[0xF29]/[0xF14]</c> all being 0, resolves two
        /// names through [[0x7D5D6C]] 0x74C1E0, matches a bag item on <c>[self+0x508]</c>,
        /// runs [[0x7D6630]] 0x600F6C, and answers SM 0x3B7. The four gate flags and both
        /// tables are unmodelled.
        /// </summary>
        private void ClientNativeStrengthenTableOp()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1080, m_sCharName);
        }

        // ================================================================================
        // 答题/广播提交 — broadcast/quiz manager [[0x7D62DC]] (off_7D62DC, named by
        // NativeGmItemExtraCommands: reloadStditem sub_713094([off_7D62DC],...)), with the
        // answer-state block [self+0x7B0..0x7C4] and result mgr [[0x7D5D6C]].
        // ================================================================================

        /// <summary>
        /// CM 1090, leaf 0x6D9732 (`33 C9 / 33 D2` ecx=edx=0) -> worker 0x6BD674 with cl=0.
        /// The cl=0 arm validates the answer state <c>[self+0x7C3]/[0x7C4]</c>, resets
        /// <c>[self+0x7B0]/[0x7B4]</c>, resolves through [[0x7D5D6C]] 0x750F3C + 0x6C87B4
        /// and answers '回答正确, 请稍后再来' (0xFFDB) or a limit notice (0x38FF), pushing a
        /// broadcast via [[0x7D62DC]] 0x71315C. The answer-state fields and both managers
        /// are unmodelled.
        /// </summary>
        private void ClientNativeQuizAnswerSelf()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1090, m_sCharName);
        }

        /// <summary>
        /// CM 1200, leaf 0x6DA21F -> the same worker 0x6BD674 as CM 1090, here with
        /// cl=(Param==1) (0x6DA236 cmp word[rec+6],1 / sete cl) and the body string as the
        /// answer. The cl=1 arm enforces the speak-count cap on <c>[self+0x7B4]</c>
        /// (&gt;3 -&gt; SM 0x38FF '你超过发言次数…') before submitting via [[0x7D62DC]]
        /// 0x71315C. The answer-state block and manager are unmodelled.
        /// </summary>
        private void ClientNativeQuizAnswerBody()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1200, m_sCharName);
        }

        /// <summary>
        /// CM 1217, leaf 0x6DA372 (`6A 00 6A 00 / 66 B9 65 01` subcmd 0x165, edx=0) ->
        /// worker 0x6C53B8. Packs {name obj+0xAF4, map obj+0x106, body string} and submits
        /// subcmd 0x165 to [[0x7D62DC]] 0x71315C. The manager is unmodelled.
        /// </summary>
        private void ClientNativeBroadcastSubmit()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1217, m_sCharName);
        }

        // ================================================================================
        // 摆摊交易 (personal booth/stall trade) — manager [[0x7D7190]] operating on the
        // owner's map/envir [self+0x128]; leaf-level mode flag [self+0x1899]. The C# port's
        // stall WRITE side (NativeStall*) is dormant by design (NativeStallWriteGate off),
        // so these live write ops stay fail-closed.
        // ================================================================================

        /// <summary>
        /// CM 1210, leaf 0x6DA418. Leaf gate <c>[self+0x1899]==0</c> (0x6DA41B cmp/jne
        /// 0x6DBC2C). Worker 0x6E3974(Param as cl, Series, body): when cl==1 and the map
        /// flag <c>[envir+0x7C]==0</c> it calls [[0x7D7190]] 0x612F6C; a result &lt;=0
        /// answers SM 0x6C2. The stall manager and <c>[self+0x1899]</c> are unmodelled.
        /// </summary>
        private void ClientNativeBoothTrade10()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1210, m_sCharName);
        }

        /// <summary>
        /// CM 1211, leaf 0x6DA45D. Leaf gate <c>[self+0x1899]==0</c>. Worker 0x6E39C8: when
        /// <c>[envir+0x7C]==0</c> calls [[0x7D7190]] 0x6131A0; result &lt;=0 answers SM
        /// 0x6C3, &lt;0 also runs 0x6137E0. Manager [[0x7D7190]] unmodelled.
        /// </summary>
        private void ClientNativeBoothTrade11()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1211, m_sCharName);
        }

        /// <summary>
        /// CM 1212, leaf 0x6DA49B. Leaf gate <c>[self+0x1899]==0</c>. Worker 0x6E3A34
        /// forwards Param to [[0x7D7190]] 0x6137E0. Manager [[0x7D7190]] unmodelled.
        /// </summary>
        private void ClientNativeBoothTrade12()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1212, m_sCharName);
        }

        /// <summary>
        /// CM 1213, leaf 0x6DA4BF. Leaf gate <c>[self+0x1899]!=0</c> (0x6DA4C9 je 0x6DBC2C).
        /// For Tag==1 it first runs the trade-fee gate 0x6151CC (reads <c>[self+0xD8]/[0xED]/
        /// [0xF7]</c>, '交易费') and 0x6152B8, then 0x6E3A4C(-&gt;0x613B40) answering SM
        /// 0x6C6/0x6CA; Tag!=1 goes straight to 0x6E3A4C. The trade-fee fields and the booth
        /// envir state are unmodelled.
        /// </summary>
        private void ClientNativeBoothTrade13()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1213, m_sCharName);
        }

        /// <summary>
        /// CM 1214, leaf 0x6DA529. Leaf gate <c>[self+0x1899]!=0</c>. Worker 0x6E3A88
        /// (-&gt;0x613A88(envir,...)) answers SM 0x6C8. The booth envir state is unmodelled.
        /// </summary>
        private void ClientNativeBoothTrade14()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1214, m_sCharName);
        }

        // ================================================================================
        // 碎片拼合 / 任务发布板 — script-driven.
        // ================================================================================

        /// <summary>
        /// CM 1248, leaf 0x6DA58E (ecx=body length, body string) -> worker 0x6E5384. The
        /// worker's first act is <c>cmp edi,0x20 / jl 0x6E556D</c> (0x6E53AF): a body under
        /// 0x20 bytes is genuine native silence, reproduced here from nBodyLen. Otherwise it
        /// matches 8 ids against the bag <c>[self+0x508]</c>, checks the counters
        /// <c>[self+0x9C0]/[0x9C4]/[0x9C8]</c>, runs the script '@AckPieceUp' (0x6E55B0) via
        /// the script object <c>[self+0xCD8]</c>, deletes the pieces and answers SM 0xB88.
        /// The counters and the script object are unmodelled, so the settlement is withheld.
        /// </summary>
        private void ClientNativePieceUpSynth(int nBodyLen)
        {
            // 0x6E53AF `cmp edi,0x20 / jl 0x6E556D` — native does nothing for a short body.
            if ((nBodyLen & 0xFFFF) < 0x20)
            {
                return;
            }

            NativeCmQ1FailClosed.Drop(Grobal2.CM_1248, m_sCharName);
        }

        /// <summary>
        /// CM 1250, leaf 0x6DA5A1 -> worker 0x6E1CEC. When the task-board script object
        /// <c>[[0x7D5D20]]+0</c> is non-null it runs the board script 0x6996E8 to build the
        /// listing and answers SM 0xB31 with it. The board script object [[0x7D5D20]] and its
        /// @Main procedures are not ported (the same object cm-4 withholds for CM 4150/4151/
        /// 4651), so the body cannot be derived.
        /// </summary>
        private void ClientNativeTaskBoardList()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1250, m_sCharName);
        }

        // ================================================================================
        // 元宝寄售 (YB consignment) — manager [[0x7D6ABC]] (named by NativeYbConsignmentQuery:
        // "the MANAGER at [[0x7D6ABC]], a singleton"). The C# read-only views CM 1252/1253/
        // 1256/1257 are wired; this write/deal/config side is deliberately dormant
        // (NativeYbDealPurchaseStateMachine is host-driven and off), so it stays fail-closed.
        // ================================================================================

        /// <summary>
        /// CM 1251, leaf 0x6DA66A (Tag, body length, body string) -> worker 0x6E7E0C. Gated
        /// by 0x6F9594 (the 元宝-system-open check) and then 0x6CB94C. The manager write side
        /// and the open-state flag are unmodelled.
        /// </summary>
        private void ClientNativeYbConsignmentGated()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1251, m_sCharName);
        }

        /// <summary>
        /// CM 1254, leaf 0x6DA69F (Recog) -> worker 0x6F9538, which calls [[0x7D6ABC]]
        /// 0x6326F4 with {map obj+0x106, Recog} — the classic CM 1254 consignment ACCEPT
        /// callback. C# models it in NativeYbDealPurchaseStateMachine, but that machine is
        /// host-driven and dormant, so the outcome code cannot be derived here.
        /// </summary>
        private void ClientNativeYbConsignmentAccept()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1254, m_sCharName);
        }

        /// <summary>
        /// CM 1255, leaf 0x6DA6B1 (Recog) -> worker 0x6E8350, which calls [[0x7D6ABC]]
        /// 0x632B4C with {map obj+0x106, Recog}. The consignment manager write side is dormant.
        /// </summary>
        private void ClientNativeYbConsignment55()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1255, m_sCharName);
        }

        /// <summary>
        /// CM 1258, leaf 0x6DA6C3 (Recog) -> worker 0x6E82F4, which calls [[0x7D6ABC]]
        /// 0x632FC4 with {map obj+0x106, Recog}. The consignment manager write side is dormant.
        /// </summary>
        private void ClientNativeYbConsignment58()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1258, m_sCharName);
        }

        /// <summary>
        /// CM 1259, leaf 0x6DA6EF -> worker 0x6E8454. Passes the 元宝-system-open gate
        /// 0x6F9594, then submits subcmd 0x70 via 0x6D3694 to shopMgr [[0x7D5D98]]; a closed
        /// system answers '元宝系统暂时关闭…' (0x6E8494). The open-state and the manager are
        /// unmodelled.
        /// </summary>
        private void ClientNativeYbConsignmentOpenSubmit()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1259, m_sCharName);
        }

        /// <summary>
        /// CM 1260, leaf 0x6DA6FC (Param -> dx) -> worker 0x6E84BC. With dx defaulted to
        /// 0xFFFF when 0, it either (first-time, <c>[self+0x4B7]==0</c>) writes
        /// <c>[self+0x18A0]</c> and answers SM 0xBC2, or (already set) writes
        /// <c>[self+0x18A2]</c> and runs 0x6D64B8 (logs '修改元宝交易金额'). <c>[self+0x18A0]</c>
        /// has a persistence model (m_nNativeTradeProtectAmount), but the branch gate
        /// <c>[self+0x4B7]</c>, the twin field <c>[self+0x18A2]</c> and the 0x6D64B8 path are
        /// unmodelled, so which branch runs — and thus the live mutation and reply — cannot
        /// be derived.
        /// </summary>
        private void ClientNativeYbSetTradeAmount()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1260, m_sCharName);
        }
    }
}
