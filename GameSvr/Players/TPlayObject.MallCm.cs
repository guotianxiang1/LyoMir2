using SystemModule;

namespace GameSvr
{
    // =====================================================================================
    // === Mall subsystem ===  CM 1054 / 1055 / 1056 / 1057  (商城/mall 提交 —— 元宝寄售 client submit)
    //
    // Faithful 1:1 port of the four CM handlers cm-1 previously routed to
    // NativeCmQ1FailClosed.Drop. Ground truth from flat_image.bin (ImageBase 0x400000).
    //
    // WHAT THE NATIVE HANDLERS ACTUALLY ARE
    // -------------------------------------
    // All four pack a fixed record and push it into the shop/元宝 manager's OUTBOUND
    // message channel [[0x7D5D98]] via the submit wrapper sub_6D3694, which calls the
    // channel enqueue sub_637A00. The enqueue is NOT shop business logic — it is a generic
    // framed-message enqueue into a 32 KB rolling send buffer that a socket-writer thread
    // (sub_4C93F8) flushes to an EXTERNAL 元宝/寄售 process. That external process is the
    // authoritative party; it later answers asynchronously. This C# port does not host that
    // process (NativeShopWriteTransaction: the 元宝/金刚石 settlement chain is "6108-blocked /
    // NO-GO"; NativeYbDealPurchaseStateMachine is host-driven and dormant; there is no
    // channel-establishing code and no [worker+0x2c] activation anywhere in the port). The
    // channel is therefore provably INACTIVE here — see NativeMallSubmitChannel below.
    //
    // sub_637A00 (channel enqueue) — decoded @0x637A00:
    //   xor eax,eax
    //   cmp byte [chan+0x2c],0 / je return-false      ; +0x2c = channel ACTIVE flag (config/link gate)
    //   … build 0x10-byte framed header + payload into the 32 KB buffer [chan+0x180]/[chan+0x184]
    //       dword[rec+0]  = 0x33AABB77                 ; frame magic
    //       dword[rec+4]  = ecx  (sub-parameter)
    //       dword[rec+8]  = arg3 (sub_7481F4(self) for 1054/1055, [self+0x758] for 1056/1057)
    //       word [rec+0xC]= subcmd (edx)
    //       word [rec+0xE]= payload length (0x40 + bodylen)
    //   mov al,1 / return-true                          ; enqueue succeeded
    //   => returns TRUE iff the channel is ACTIVE ([chan+0x2c]!=0); FALSE when the link is down.
    //
    // sub_6D3694 (submit wrapper) — decoded @0x6D3694: builds the 0x40-byte header, then
    //   `mov eax,[0x7D5D98] / mov eax,[eax] / call 0x637A00`. Header layout (record+off):
    //       +0x00 (10) self+0xAF4  account   (ShortString, sub_4039E4 cl=0x0A)
    //       +0x0B (20) self+0xB09            (ShortString, cl=0x14)
    //       +0x20 (15) self+0x106  map name (ShortString, cl=0x0F)
    //       +0x30 (15) self+0xB33            (ShortString, cl=0x0F)
    //       +0x40 (..) body                  (Move, bodylen bytes; 0 for 1054-1057)
    //   Returns 0x637A00's result. (Only subcmd 0x7D sets the [self+0xBA6] one-shot flag —
    //   none of 1054-1057 uses subcmd 0x7D, so [0xBA6] is irrelevant here.)
    //
    // DERIVABLE PLAYER-STATE (offset → C# model)
    // ------------------------------------------
    //   [self+0x788] submit throttle tick (2000 ms)  -> _nativeMallSubmitTick (this file)
    //   [self+0x758] DealId  (unsigned; native jbe)   -> not populated (dormant) => 0
    //   [self+0x75C] Count   (signed;   native jle)   -> not populated (dormant) => 0
    //   sub_6C7D88(self,1) equip-secret-lock gate     -> NativeMakeItemUseDiamHost verified the
    //                                                    lock [self+0x711] is ABSENT in this
    //                                                    server, so the gate is unconditionally
    //                                                    true (TryEnterPlayerState => true).
    //   [vmt+0xD4] SysMsg(cx,msg): cx unpacks FColor=cx&0xFF / BColor=cx>>8; cx=0x38FF == Red.
    //       -> SysMsg(text, MsgColor.Red, MsgType.Hint)
    //
    // OBSERVABLE RESULT IN THIS PORT (channel inactive, deal state empty)
    // ------------------------------------------------------------------
    //   1054/1055: throttle + (1055) Param-map gate pass, enqueue returns FALSE (link down),
    //              native answers SM_SYSMESSAGE "网络故障，请稍候..." (dword_6DBF88) — reproduced.
    //   1056/1057: the per-player deal-state gate ([self+0x758]/[self+0x75C] > 0) is never
    //              satisfied (no active deal exists in the dormant subsystem), so the native
    //              worker returns silently BEFORE any enqueue or reply — reproduced as a no-op.
    //   The only fail-closed boundary is the channel enqueue's SUCCESS path (active link →
    //   external async reply), which is unreachable while the channel is inactive.
    //
    // INTEGRATOR HOOKUP (do NOT edit the Operate() switch here — that is the shared main body):
    //   In TPlayObject.Message.cs Operate()'s `default:` arm, call TryHandleMallCm FIRST so a
    //   handled ident short-circuits BEFORE TryHandleNativeCmQ1 (which currently fail-closes
    //   1054-1057). i.e. replace
    //       if (!TryHandleNativeSocialProtocol(ProcessMsg)
    //           && !TryHandleNativeCmTailProtocol(ProcessMsg)
    //           && !TryHandleNativeCmQ1(ProcessMsg) …)
    //   with
    //       if (!TryHandleMallCm(ProcessMsg)                     // <-- insert before Q1
    //           && !TryHandleNativeSocialProtocol(ProcessMsg)
    //           && !TryHandleNativeCmTailProtocol(ProcessMsg)
    //           && !TryHandleNativeCmQ1(ProcessMsg) …)
    //   Once wired, CM_1054..CM_1057 in TryHandleNativeCmQ1 become dead arms (safe to leave).
    // =====================================================================================
    public partial class TPlayObject
    {
        /// <summary>[self+0x788]: last mall-submit tick, shared by CM 1054 and CM 1055
        /// (both leaves throttle on the same field, native 0x6D9437 / 0x6D949A).</summary>
        private int _nativeMallSubmitTick;

        /// <summary>2000 ms (native 0x6D943D <c>cmp eax,0x7D0 / jb drop</c>).</summary>
        private const int NativeMallSubmitThrottleMs = 0x7D0;

        /// <summary>cx passed to [vmt+0xD4] on a busy channel: FColor 0xFF / BColor 0x38 == Red.</summary>
        private const int NativeMallBusyColor = 0x38FF;

        /// <summary>dword_6DBF88, sent by CM 1054/1055 when the channel enqueue fails.</summary>
        private const string NativeMallBusyText = "网络故障，请稍候...";

        /// <summary>dword_6CBA64, the CM 1057 "cannot cancel now" refusal (vtable[0x244] false).</summary>
        private const string NativeMallSellerCancelRejectText = "[失败]：包裹栏空位不足，不能取回。";

        /// <summary>
        /// Q1-range mall submit dispatch (CM 1054-1057). Returns true when the ident was one
        /// of ours (handled), so the caller stops before the Q1 fail-closed arm. Signature and
        /// field access mirror TryHandleNativeCmQ1.
        /// </summary>
        private bool TryHandleMallCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1054:
                    ClientNativeMallSubmit1054();
                    return true;
                case Grobal2.CM_1055:
                    ClientNativeMallSubmit1055Tier(processMessage.nParam2);
                    return true;
                case Grobal2.CM_1056:
                    ClientNativeMallSubmit1056Deal();
                    return true;
                case Grobal2.CM_1057:
                    ClientNativeMallSubmit1057SellerCancel();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Native leaf 0x6D942F. Throttle 2000 ms on [self+0x788]; on pass, submit subcmd 0x7B
        /// (ecx=0, arg3=sub_7481F4(self)) through the channel. Enqueue false → SM_SYSMESSAGE
        /// "网络故障，请稍候..." (native 0x6D9471 test al,al / jne done / 0x6D9479 cx=0x38FF).
        /// </summary>
        private void ClientNativeMallSubmit1054()
        {
            if (!NativeMallSubmitThrottlePasses())
            {
                return; // native 0x6D9442 jb 0x6DBC2C — silent drop within 2000 ms
            }

            if (!NativeMallSubmitChannel.TrySubmit(this, 0x7B))
            {
                SysMsg(NativeMallBusyText, MsgColor.Red, MsgType.Hint);
            }
        }

        /// <summary>
        /// Native leaf 0x6D9492. Same [self+0x788] throttle, then maps Param (word[rec+6])
        /// 1..4 to subcmd {0x6F,0x75,0x7A,0x7D} (native 0x6D94C0 four `dec ax`); Param outside
        /// 1..4 is a silent drop (0x6D94F8 je 0x6DBC2C). The chosen value rides in ecx while
        /// the channel subcmd is dx=0x6B. Enqueue false → "网络故障，请稍候...".
        /// </summary>
        private void ClientNativeMallSubmit1055Tier(int param)
        {
            if (!NativeMallSubmitThrottlePasses())
            {
                return; // 0x6D94A5 jb 0x6DBC2C
            }

            var subParam = param switch
            {
                1 => 0x6F,
                2 => 0x75,
                3 => 0x7A,
                4 => 0x7D,
                _ => -1,
            };
            if (subParam < 0)
            {
                return; // 0x6D94F8 je 0x6DBC2C — Param not in 1..4, silent drop
            }

            if (!NativeMallSubmitChannel.TrySubmit(this, 0x6B, subParam))
            {
                SysMsg(NativeMallBusyText, MsgColor.Red, MsgType.Hint);
            }
        }

        /// <summary>
        /// Native worker sub_6CB9B4 (leaf 0x6D953A). Gate sub_6C7D88(self,1) (always true here)
        /// AND [self+0x758] (DealId, unsigned) > 0, then submit subcmd 0x76 (arg3=[self+0x758]);
        /// the enqueue result is ignored (no reply either way). The DealId is never populated in
        /// this dormant port, so native `jbe 0x6CB9EA` returns silently before the enqueue.
        /// </summary>
        private void ClientNativeMallSubmit1056Deal()
        {
            // sub_6C7D88(self,1): equip-secret lock [self+0x711] is absent in this server, gate true.
            var dealId = NativeMallActiveDealId; // [self+0x758]; 0 in the dormant subsystem
            if (dealId <= 0)
            {
                return; // 0x6CB9D0 jbe 0x6CB9EA — no active deal, silent no-op
            }

            NativeMallSubmitChannel.TrySubmit(this, 0x76, 0, dealId); // native ignores the result
        }

        /// <summary>
        /// Native worker sub_6CB9F0 (leaf 0x6D9547) — 元宝寄售卖家取消 / ClientSellerCancelYbDeal.
        /// Reuses <see cref="NativeYbDealSellerCancelPlanner"/> (the port's existing dormant model
        /// of this exact wrapper). Gates: sub_6C7D88(self,1) true, [self+0x75C] Count > 0 (signed),
        /// [self+0x758] DealId != 0 (unsigned); then vtable[0x244] decides submit subcmd 0x75 vs
        /// the refusal SysMsg (0x38FF, dword_6CBA64). Count/DealId are never populated here, so the
        /// planner returns NoCancelableDeal → native returns silently (0x6CBA0C jle / 0x6CBA15 jbe).
        /// </summary>
        private void ClientNativeMallSubmit1057SellerCancel()
        {
            var outcome = NativeYbDealSellerCancelPlanner.Plan(
                hasCancelable: true,                     // sub_6C7D88(self,1): no equip-secret lock => true
                count: NativeMallSellerDealCount,         // [self+0x75C] (signed) — 0 in dormant port
                dealId: (uint)NativeMallActiveDealId,     // [self+0x758] (unsigned) — 0 in dormant port
                canCancelNow: false);                     // vtable[0x244] — unreachable while gates fail

            switch (outcome)
            {
                case NativeYbDealSellerCancelOutcome.ExecuteCancel:
                    NativeMallSubmitChannel.TrySubmit(this, NativeYbDealSellerCancelPlanner.CancelWIdent,
                        0, NativeMallActiveDealId);
                    break;
                case NativeYbDealSellerCancelOutcome.RejectNotCancelable:
                    SysMsg(NativeMallSellerCancelRejectText, MsgColor.Red, MsgType.Hint);
                    break;
                case NativeYbDealSellerCancelOutcome.NoCancelableDeal:
                default:
                    break; // silent no-op (the only reachable outcome in the dormant port)
            }
        }

        /// <summary>
        /// [self+0x758] DealId (unsigned). The 元宝寄售 deal subsystem that assigns it is dormant
        /// (NativeYbDealPurchaseStateMachine host-driven/off; PAS clientsellercancelybdeal rejected),
        /// so no path ever sets a live deal on a player: it is always 0 here.
        /// </summary>
        private int NativeMallActiveDealId => 0;

        /// <summary>[self+0x75C] Count (signed). Same dormant subsystem — always 0 here.</summary>
        private int NativeMallSellerDealCount => 0;

        /// <summary>[self+0x788] throttle: (uint)(now-last) &lt; 2000 ms drops; else stamp and pass.</summary>
        private bool NativeMallSubmitThrottlePasses()
        {
            var now = HUtil32.GetTickCount();
            if ((uint)(now - _nativeMallSubmitTick) < NativeMallSubmitThrottleMs)
            {
                return false;
            }
            _nativeMallSubmitTick = now;
            return true;
        }
    }

    /// <summary>
    /// Model of the shop/元宝 manager OUTBOUND message channel [[0x7D5D98]] and its enqueue
    /// sub_637A00. The channel forwards framed records to an external 元宝/寄售 process over a
    /// socket-writer thread (sub_4C93F8); sub_637A00 returns TRUE only while the channel is
    /// ACTIVE ([chan+0x2c] != 0).
    /// <para>
    /// This port does not host that external process and contains no code that establishes the
    /// link or sets the active flag (consistent with NativeShopWriteTransaction's "NO-GO" 元宝/
    /// 金刚石 settlement boundary and the dormant NativeYbDeal* models). The channel is therefore
    /// permanently INACTIVE here and <see cref="TrySubmit"/> fail-closes to false — never putting
    /// invented bytes on the external wire and never fabricating the async success reply. Wiring a
    /// real channel later is the single point that flips <see cref="IsActive"/> and enqueues the
    /// record documented in TPlayObject.MallCm.cs.
    /// </para>
    /// </summary>
    internal static class NativeMallSubmitChannel
    {
        /// <summary>Native [chan+0x2c] — the channel active/link gate. Dormant in this port.</summary>
        internal static bool IsActive => false;

        /// <summary>
        /// Models sub_6D3694 → sub_637A00. Returns the native enqueue result: true iff the
        /// channel is active. While dormant it is always false, so 1054/1055 answer the native
        /// busy SysMsg and 1056/1057 (which ignore the result and only reach here past a
        /// deal-state gate that never opens) enqueue nothing.
        /// </summary>
        internal static bool TrySubmit(TPlayObject self, int subcmd, int subParam = 0, int arg3 = 0)
        {
            // Channel inactive => native sub_637A00 `cmp byte [chan+0x2c],0 / je return-false`.
            return IsActive;
        }
    }
}
