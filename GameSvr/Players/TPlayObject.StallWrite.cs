using System.Collections.Generic;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    // ================================================================================================
    // 摆摊·写侧 (booth-trade WRITE) — CM 1210/1211/1212/1213/1214.
    //
    // WHAT THIS IS (evidence, not guess). These five idents dispatch through 战神's CM tree
    // (sub_6D7D68) to their leaves 0x6DA418/0x6DA45D/0x6DA49B/0x6DA4BF/0x6DA529, whose workers
    // (0x6E3974/0x6E39C8/0x6E3A34/0x6E3A4C/0x6E3A88) call the SINGLETON booth-trade manager
    // [[0x7D7190]] (methods 0x612F6C/0x6131A0/0x6137E0/0x613B40/0x613A88) operating on the player's
    // live trade-session object [self+0x128]. This is a STATEFUL, TWO-PARTY, money- AND item-moving
    // trade/booth session with its own state machine at [session+0xF7], party slots at
    // [session+0xD8]/[session+0xDC], per-party ready bytes [session+0xEC..0xEF], a bitmask
    // [session+0xF2], confirm flags [session+0xF3]/[session+0xF4], and a timer [session+0xE8].
    //
    // DISTINCT FROM THE EXISTING NativeStall* (do NOT conflate). The wired stall write side
    // (TPlayObject.NativeStall.cs, CM 4418-4467) is the PERSISTENT, DB-backed 摆摊 keyed by owner
    // char-name via NativeStallManager (sub_49F5F4, tables stall/stallitem/buyer_order/stallmsglst).
    // The [[0x7D7190]] manager reached here is a DIFFERENT object: an in-memory live trade-session
    // machine on [self+0x128]. Neither the manager [[0x7D7190]] nor the session object nor the leaf
    // mode flag [self+0x1899] is modelled in this port, so there is no existing model to route into.
    //
    // WRITE-SIDE DORMANCY (faithful default, do NOT flip on). Mirroring the CM 4418-4467 write ops,
    // this router is gated on the SAME master switch NativeStallWriteGate.Enabled (SupportsStallWrites
    // && Store), which is OFF by default. Dormant => this returns false and the packet falls through
    // to the existing NativeCmQ1FailClosed drop — nothing on the wire, behaviour IDENTICAL to today.
    // This is the "写侧默认休眠" the port already enforces for stalls; it is NOT changed here.
    //
    // FAIL-CLOSED (per the fidelity rules — 有据不臆造). Every worker's terminal action and its SM
    // reply are a function of the unmodelled [[0x7D7190]] session state (see the per-op notes below).
    // Even if the reviewer flips NativeStallWriteGate live, we REFUSE to fabricate the money/item
    // settlement or the reply bytes, so the terminal action is recorded once and dropped (no invented
    // bytes on the wire). The SM constants themselves already exist (Grobal2.SM_1729..SM_1738, added
    // by the SM batches) and the CM constants (Grobal2.CM_1210..CM_1214, added by cm-1) — nothing is
    // duplicated here.
    //
    // Wire -> TProcessMessage (verified, NativeStallWireCodec header): Recog(i32@0)->nParam1,
    // Param(u16@6)->nParam2, Tag(u16@8)->nParam3, Series(u16@0xA)->wParam, body->Payload (6-bit
    // encoded). The booth-trade bodies (e.g. CM 1210 item-listing data) are consumed inside the
    // unmodelled manager, so they are NOT decoded/interpreted here.
    // ================================================================================================
    public partial class TPlayObject
    {
        /// <summary>
        /// 摆摊写侧 CM 路由入口 (booth-trade WRITE, CM 1210-1214). Returns true only when this handler
        /// has consumed the packet; false lets the caller keep walking the dispatch chain (so a dormant
        /// write side falls through to the existing Q1 fail-closed drop, unchanged).
        ///
        /// INTEGRATOR HOOKUP (one line, insert BEFORE the Q1 arm so a live booth-trade op short-circuits
        /// ahead of the Q1 drop; keep everything else in TPlayObject.Message.cs Operate()'s default arm
        /// untouched):
        ///
        ///     default:
        ///         if (!TryHandleNativeSocialProtocol(ProcessMsg)
        ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
        ///             &amp;&amp; !TryHandleStallWriteCm(ProcessMsg)      // &lt;-- add this line, before Q1
        ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
        ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
        ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
        ///         {
        ///             result = base.Operate(ProcessMsg);
        ///         }
        ///         break;
        /// </summary>
        internal bool TryHandleStallWriteCm(TProcessMessage msg)
        {
            if (msg == null)
                return false;

            switch (msg.wIdent)
            {
                case Grobal2.CM_1210:   // leaf 0x6DA418 -> 0x6E3974 -> [[0x7D7190]] 0x612F6C
                case Grobal2.CM_1211:   // leaf 0x6DA45D -> 0x6E39C8 -> [[0x7D7190]] 0x6131A0 (+0x6137E0)
                case Grobal2.CM_1212:   // leaf 0x6DA49B -> 0x6E3A34 -> [[0x7D7190]] 0x6137E0
                case Grobal2.CM_1213:   // leaf 0x6DA4BF -> fee gate 0x6151CC/0x6152B8 -> 0x6E3A4C -> 0x613B40
                case Grobal2.CM_1214:   // leaf 0x6DA529 -> 0x6E3A88 -> [[0x7D7190]] 0x613A88
                    return TryRouteBoothTradeWrite(msg);
                default:
                    return false;       // not a booth-trade write ident — keep walking the chain
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Per-op data flow (offset -> intended C# mapping -> disposition). Every SM here is empty-body via
        // the [obj+0x250] slot => SendDefMessage(Recog, ident, 0,0,0,""); the mapping is documented so a
        // future live port lands the exact reply, but every reply CODE depends on unmodelled session state.
        //
        // CM 1210 (leaf 0x6DA418): leaf gate [self+0x1899]==0 (jne no-op sink 0x6DBC2C). Worker
        //   0x6E3974(self, edx=Recog=nParam1, cl=Param=nParam2, [ebp+8]=body-string=Payload,
        //   [ebp+0xC]=Series=wParam): requires cl==1 AND [envir+0x7C]==0 ([self+0x128]+0x7C), then
        //   0x612F6C([[0x7D7190]], self, body, Recog, Series, ParamByte). ParamByte==1 => BUY branch:
        //   Recog(price) > [self+0x15C](gold=m_nGold) -> return -1; else 0x612D44 (gold-out) + 0x6C7D64,
        //   return 0. Reply SM 0x6C2 (SM_1730) Recog=return, sent ONLY when return<=0. -> gold move via
        //   unmodelled [[0x7D7190]] 0x612D44. FAIL-CLOSED.
        //
        // CM 1211 (leaf 0x6DA45D): leaf gate [self+0x1899]==0. Worker 0x6E39C8(self, edx=Recog,
        //   cx=Param, [ebp+8]=body): [envir+0x7C]==0 then 0x6131A0([[0x7D7190]], self, Param, body) ->
        //   esi. Reply SM 0x6C3 (SM_1731) Recog=esi when esi<=0; when esi<0 ALSO 0x6137E0 (the 8-record
        //   list rebuild that fires the BLOCKED SM 0x6C1/SM_1729 body @0x613925). 0x6131A0 walks the
        //   session list (0x61369C lookup, 0x614268 mutate, 0x612D30 remove). FAIL-CLOSED.
        //
        // CM 1212 (leaf 0x6DA49B): leaf gate [self+0x1899]==0. Worker 0x6E3A34(self, dx=Param) ->
        //   0x6137E0([[0x7D7190]], self, Param): rebuilds the 8-item page from the two session lists
        //   ([mgr+0x20]/[mgr+0x24], 0x424D4C indexer, 0x613788 record serializer) and sends the BLOCKED
        //   SM 0x6C1 (SM_1729, Buf=&local Len=0xE0) — body not resolvable at the slot. FAIL-CLOSED.
        //
        // CM 1213 (leaf 0x6DA4BF): leaf gate [self+0x1899]!=0 (je no-op sink). For Tag(nParam3)==1 the
        //   leaf first runs the TRADE-FEE gate 0x6151CC([session], self): needs [session+0xF7]==2 and
        //   0x6151B0(session)>2, matches self against [session+0xD8]/[session+0xDC], checks
        //   [session+0xED]/[session+0xEF]==0xFF, then pops the '交易费' confirm dialog (0x6DFA40 / notice
        //   0x6DF62C, strings @0x615278/0x615288/0x6152A4); false => silent (je sink). On pass, 0x6152B8
        //   clears [session+0xED]/[session+0xEF] and sends SM 0x6CA (SM_1738). Then (both Tag arms)
        //   0x6E3A4C(self, dl=Param) -> 0x613B40([session], self, cl=Param): state==2 + slot-bit maths on
        //   [session+0xEC..0xEF]/[session+0xF2], and when all 4 ready-slots fill, 0x613C20 advances state
        //   to 3, stamps timer [session+0xE8]=now+0x5DC and calls item-DB 0x765E68 x4 (the FEE/stake
        //   deduction). Reply SM 0x6C6 (SM_1734) Param=Param when 0x613B40 returns nonzero. -> item/fee
        //   move via unmodelled session + fields [self+0xD8/0xED/0xF7]. FAIL-CLOSED.
        //
        // CM 1214 (leaf 0x6DA529): leaf gate [self+0x1899]!=0. Worker 0x6E3A88(self, dl=Param) ->
        //   0x613A88([session], self, cl=Param): state==4 + slot maths, sets final-confirm
        //   [session+0xF3]/[session+0xF4]; when BOTH set, 0x614BB0 finalizes the trade (settlement).
        //   Reply SM 0x6C8 (SM_1736) Param=Param when 0x613A88 returns nonzero. FAIL-CLOSED.
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Route a booth-trade WRITE op to the (dormant) [[0x7D7190]] session executor. The write side is
        /// OFF by default (NativeStallWriteGate.Enabled — the same master switch as the CM 4418-4467 stall
        /// writes), so this returns false and the op falls through to the existing Q1 drop, unchanged.
        ///
        /// When the reviewer flips the write gate live, the [[0x7D7190]] trade-session manager, the session
        /// object [self+0x128] and the leaf mode flag [self+0x1899] are STILL unmodelled in this port, so
        /// the terminal action (money/item settlement + SM reply) cannot be reproduced from image bytes
        /// without fabrication. We therefore fail-closed: record the gap once and drop (nothing on the
        /// wire). This is the flip point where a future faithful [[0x7D7190]] model would take over.
        /// </summary>
        private bool TryRouteBoothTradeWrite(TProcessMessage msg)
        {
            // 写侧默认休眠：与 CM 4418-4467 摆摊写操作共用同一主开关，默认关。关 => 交回调用链，落既有 Q1 drop。
            if (!NativeStallWriteGate.Enabled)
                return false;

            // 门已开但 [[0x7D7190]] 会话管理器/[self+0x128] 会话对象/[self+0x1899] 模式标志均未建模：
            // 语义不可从镜像证得，绝不捏造金币/物品结算或 SM 回包 —— fail-closed 记录并丢弃。
            NativeStallWriteCmFailClosed.Drop(msg.wIdent, m_sCharName);
            return true;
        }
    }

    /// <summary>
    /// Once-per-ident recorder for the booth-trade WRITE set (CM 1210-1214) when the write gate is live
    /// but the [[0x7D7190]] trade-session manager is unmodelled. Kept in this file (anti-conflict: only
    /// TPlayObject.StallWrite.cs is authored by this task) and named distinctly so it never collides with
    /// NativeCmQ1FailClosed / NativeCmTailFailClosed. Mirrors their "drop + log once, never invent a
    /// reply" posture. In the default dormant state this is never reached (the router returns false and
    /// the Q1 drop handles the log); it only fires on the future live-gate path.
    /// </summary>
    internal static class NativeStallWriteCmFailClosed
    {
        private readonly struct Entry
        {
            public Entry(uint leafVa, uint workerVa, uint managerVa, string blocker)
            {
                LeafVa = leafVa;
                WorkerVa = workerVa;
                ManagerVa = managerVa;
                Blocker = blocker;
            }

            public uint LeafVa { get; }
            public uint WorkerVa { get; }
            public uint ManagerVa { get; }
            public string Blocker { get; }
        }

        private static readonly Dictionary<int, Entry> Entries = new()
        {
            [Grobal2.CM_1210] = new Entry(0x006DA418, 0x006E3974, 0x00612F6C,
                "leaf 门 [self+0x1899]==0；0x6E3974(cl=Param==1 且 [envir+0x7C]==0) 调 [[0x7D7190]] 0x612F6C " +
                "购买分支 Recog>[self+0x15C](金币)回-1 否则 0x612D44 扣币；回 SM 0x6C2(SM_1730)；" +
                "[[0x7D7190]] 会话/[self+0x128]/[self+0x1899] 未建模"),
            [Grobal2.CM_1211] = new Entry(0x006DA45D, 0x006E39C8, 0x006131A0,
                "leaf 门 [self+0x1899]==0；0x6E39C8 在 [envir+0x7C]==0 时调 [[0x7D7190]] 0x6131A0，" +
                "结果<=0 回 SM 0x6C3(SM_1731)、<0 再调 0x6137E0(触发 BLOCKED SM 0x6C1/SM_1729)；会话未建模"),
            [Grobal2.CM_1212] = new Entry(0x006DA49B, 0x006E3A34, 0x006137E0,
                "leaf 门 [self+0x1899]==0；0x6E3A34 以 Param 调 [[0x7D7190]] 0x6137E0 重建 8 项分页并发 " +
                "BLOCKED SM 0x6C1(SM_1729, Buf=&local Len=0xE0)；两会话列表未建模"),
            [Grobal2.CM_1213] = new Entry(0x006DA4BF, 0x006E3A4C, 0x00613B40,
                "leaf 门 [self+0x1899]!=0；Tag==1 先过交易费门 0x6151CC(读 [session+0xF7]/0xD8/0xED/0xEF '交易费')" +
                "与 0x6152B8(回 SM 0x6CA/SM_1738)，再 0x6E3A4C->0x613B40 状态机(满 4 槽经 0x613C20 扣费/物品-DB " +
                "0x765E68)回 SM 0x6C6(SM_1734)；交易费字段与会话状态未建模"),
            [Grobal2.CM_1214] = new Entry(0x006DA529, 0x006E3A88, 0x00613A88,
                "leaf 门 [self+0x1899]!=0；0x6E3A88->0x613A88 状态机(state==4，双确认 [session+0xF3]/0xF4 满时 " +
                "0x614BB0 终局结算)回 SM 0x6C8(SM_1736)；会话状态与结算未建模"),
        };

        private static readonly HashSet<int> Reported = new();
        private static readonly object Gate = new();

        internal static void Drop(int ident, string charName)
        {
            if (!Entries.TryGetValue(ident, out var entry))
                return;

            lock (Gate)
            {
                if (!Reported.Add(ident))
                    return;
            }

            M2Share.MainOutMessage(
                $"[CM未移植/摆摊写侧] CM {ident} (摆摊交易) 已丢弃; " +
                $"leaf=0x{entry.LeafVa:X6} worker=0x{entry.WorkerVa:X6} mgr=[[0x7D7190]]:0x{entry.ManagerVa:X6}; " +
                $"角色={(string.IsNullOrEmpty(charName) ? "<unknown>" : charName)}; " +
                $"缺口={entry.Blocker}");
        }
    }
}
