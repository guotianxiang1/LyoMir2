using System;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 金刚石抽奖领奖 / 藏宝图合成 subsystem — CM 4646..4650.
    ///
    /// This is the faithful upgrade of the five idents cm-4 previously fail-closed
    /// (see <see cref="NativeCmTailFailClosed"/>). It supersedes cm-4's
    /// ClientNativePrize* stubs when <see cref="TryHandleRewardCm"/> is hooked
    /// ahead of TryHandleNativeCmTailProtocol (see the hook note at the bottom).
    ///
    /// Native dispatch tree (root sub_6D7D68, selector 0x6D805C):
    ///   CM 4646  leaf 0x6DBBEB -> worker 0x6FBB90  领奖列表 (list)
    ///   CM 4647  leaf 0x6DBBF5 -> worker 0x6FB6FC  领奖前置校验/抽奖 (precheck + draw)
    ///   CM 4648  leaf 0x6DBBFF -> worker 0x6FB874  领奖结算 (settle)
    ///   CM 4649  leaf 0x6DBC09 -> worker 0x6FBB28  领奖(含删物品) (claim + item delete)
    ///   CM 4650  leaf 0x6DBC18 -> worker 0x6FB51C  藏宝图合成 (treasure-map synthesis)
    ///
    /// ── Data structures (disasm byte → native offset → C# mapping) ─────────────
    ///
    /// Pending-reward array, [self+0x62C .. +0x657] = 11 dwords (index 0..10):
    ///   4647 store  0x6FB7A4  89 84 93 2C 06 00 00  mov [ebx+edx*4+0x62C],eax
    ///   4648 read   0x6FB8BA  66 8B 94 B3 2C 06 00 00 mov dx,[ebx+esi*4+0x62C]
    ///   4648 read   0x6FB8C7  66 8B 84 B3 2E 06 00 00 mov ax,[ebx+esi*4+0x62E]
    ///   4646 read   0x6FBBF0  8B 94 9E 2C 06 00 00  mov edx,[esi+ebx*4+0x62C]
    ///   Each element is a DWORD packed as (value &lt;&lt; 16) | nameId:
    ///     low  word (+0x62C+i*4) = nameId  — matches the prize-manager list element
    ///                                        id at [elem+0x16] (sub_69C57C / sub_69C5E4)
    ///     high word (+0x62E+i*4) = value   — the credited amount (sub_69C54C packs
    ///                                        elem[+0x18] into the high word)
    ///   1-INDEXED in the working routines: 4647 stores at index=[+0x658] AFTER the
    ///   ++ (0x6FB76C inc / 0x6FB79E read), 4648 walks esi=count..1 downward. Index 0
    ///   is never written. (4646 alone reads 0..count-1 — see its note.)
    ///     -> C# <see cref="m_RewardList"/> (int[11]).
    ///
    /// Pending-reward count, [self+0x658]:
    ///   0x6FB705  83 BB 58 06 00 00 0A  cmp [ebx+0x658],0xA   (cap 10)
    ///     -> C# <see cref="m_nRewardCount"/>.
    ///
    /// Prize-manager list element ([[0x7D605C]].[+0x1C0], stride 0x1C = 28 bytes;
    /// index math i*7*4 at 0x69C59F/0x69C5B9):
    ///   +0x00 ShortString[20] name (21 bytes, AssignShortString maxlen 0x14)
    ///   +0x16 word id             (matched by nameId)
    ///   +0x18 word value          (draw amount)
    ///   +0x1A word cumulative-threshold (weighted Random(300) select, sub_69BF14)
    ///   The list + its secondary name table [+0x1C4] are RUNTIME data (loaded from
    ///   config/DB, never in the image) — so the manager is NOT modelled here.
    ///
    /// Currency fields referenced by settle/precheck (already modelled in C#):
    ///   [self+0x15C] 金币 gold        -> m_nGold        (0x6FB73C add,0xC350 vs +0x68C)
    ///   [self+0x68C] 金币上限 goldMax  -> m_nGoldMax
    ///   [self+0x4F0] 声望 reputation   -> (settle-only, see 4648 note)
    ///   金刚石 diamond                 -> bag items named "金刚石" (sub_6DD5A8 sums
    ///                                    word[item+0x26]=Dura), NOT a scalar field.
    ///
    /// ── Fail-closed (unprovable from the image) ───────────────────────────────
    ///   4646 count&gt;0 : native worker heap-overflows (writes each 24-byte record at
    ///                    base+i*576 into a base+count*24 buffer, 0x6FBC0F..0x6FBC28)
    ///                    and reads the never-written index-0 slot, so the sent body
    ///                    is uninitialised-stack garbage — not reproducible.
    ///   4647 (draw)    : selecting the reward needs the weighted-random draw over
    ///                    the runtime prize-manager list (sub_69BF14) + register
    ///                    (sub_69C54C) + SM 4647 body (sub_69C514). Manager unmodelled.
    ///   4648 (settle)  : crediting needs id→name resolution against the manager
    ///                    (sub_69C5E4). Manager unmodelled.
    ///   4650 (synth)   : the six-way result of the synthesis state machine
    ///                    (sub_69C03C, jump table [eax*4+0x6FB569]) parses the client
    ///                    body against the item-template db and treasure-map recipe;
    ///                    not derivable.
    /// The gate/silence/empty-list paths that ARE derivable are reproduced faithfully.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>
        /// Native [self+0x62C .. +0x657] — the pending-reward array, 11 dwords.
        /// Element = (value &lt;&lt; 16) | nameId. Used 1-indexed ([1..10]); index 0
        /// is the never-written slot 4646 erroneously reads. Session-only: the
        /// populate (4647 draw) and settle (4648) terminals are fail-closed because
        /// the runtime prize manager [[0x7D605C]] is not modelled, so this stays
        /// empty; it exists to mirror the native memory layout 1:1.
        /// </summary>
        private readonly int[] m_RewardList = new int[11];

        /// <summary>Native [self+0x658] — pending-reward count (native cap 0xA=10).</summary>
        private int m_nRewardCount;

        // Native immediates, named for the gates below.
        private const int RewardPendingCap = 0xA;          // 0x6FB705 cmp,0xA
        private const int RewardDiamondClaimCost = 0xA;    // 0x6FB772 mov edx,0xA (deducted on draw)
        private const int RewardDiamondGateMin = 0xA;      // 0x6FB72D cmp eax,0xA
        private const int RewardGoldHeadroom = 0xC350;     // 0x6FB73C add eax,0xC350 (50000)
        private const int RewardBagCapacity = 0x30;        // 0x7441DE mov edx,0x30 (48)
        private const int RewardDiamondGrantAmount = 0x24C;// 0x69C4F0 mov edx,0x24C (588)
        private const int RewardSysMsgParam = 0x38FF;      // vtbl+0xD4 SysMsg wParam
        private const string RewardDiamondItemName = "金刚石"; // @0x6DD648 / @0x6DD728

        /// <summary>
        /// Entry point for the reward/treasure-map idents. Returns true when the
        /// ident belongs to this subsystem (handled or deliberately dropped).
        ///
        /// HOOK: this must run BEFORE cm-4's TryHandleNativeCmTailProtocol so the
        /// faithful arms below win over the fail-closed stubs. The one-line wire-in
        /// (left for the integrator, since Message.cs / NativeCmTailProtocol.cs are
        /// not this agent's files) is, in the Operate default arm:
        ///     if (TryHandleRewardCm(ProcessMsg)) { result = true; break; }
        /// placed ahead of the existing
        ///     TryHandleNativeSocialProtocol/TryHandleNativeCmTailProtocol chain.
        /// </summary>
        private bool TryHandleRewardCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4646:
                    RewardListSend();
                    return true;
                case Grobal2.CM_4647:
                    RewardPrecheckAndDraw();
                    return true;
                case Grobal2.CM_4648:
                    RewardSettle();
                    return true;
                case Grobal2.CM_4649:
                    RewardClaimWithItemDelete(processMessage.nParam1);
                    return true;
                case Grobal2.CM_4650:
                    RewardTreasureMapSynth();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 4646, worker 0x6FBB90 — 领奖列表.
        ///
        /// count==0: SetLength(0) leaves a nil buffer, the fill loop is skipped
        /// (0x6FBBDE dec edi / 0x6FBBE1 jl), and the worker still fires SM 0x1226
        /// through [obj+0x254] with body_ptr=nil, body_len=0 (0x6FBC31..0x6FBC4C).
        /// That header-only reply IS reproducible, so it is sent faithfully.
        ///
        /// count&gt;0: the worker strides the destination by 0x240 per element while
        /// the buffer is only count*0x18 (0x6FBC0F shl eax,3 / two lea *3 / lea
        /// edx,[edx+eax*8] = base + i*576), a native heap overflow, and it resolves
        /// the never-written index-0 slot through the unmodelled manager, so the
        /// sent bytes are uninitialised-stack garbage. Not reproducible -> dropped.
        /// </summary>
        private void RewardListSend()
        {
            if (m_nRewardCount <= 0)
            {
                // 0x6FBC44 mov dx,0x1226 ; ecx=0 (Recog); Param/Tag/Series all 0.
                SendDefMessage((short)Grobal2.SM_REWARDLIST, 0, 0, 0, 0, string.Empty);
                return;
            }

            NativeCmTailFailClosed.Drop(Grobal2.CM_4646, m_sCharName);
        }

        /// <summary>
        /// CM 4647, worker 0x6FB6FC — 领奖前置校验 + 抽奖.
        ///
        /// The four gates are evaluated in native order and each failing gate emits
        /// its own SysMsg (vtbl+0xD4, wParam 0x38FF) and stops — all faithful. Only
        /// when every gate passes does 战神 deduct 10 金刚石 (sub_6DD650) and draw a
        /// reward from the runtime prize manager (sub_69BF14 weighted random,
        /// sub_69C54C register, sub_69C514 SM 0x1227 body). The draw depends on the
        /// unmodelled manager, so the terminal action (deduction + draw + reply) is
        /// withheld rather than invented.
        /// </summary>
        private void RewardPrecheckAndDraw()
        {
            // 0x6FB705 cmp [ebx+0x658],0xA ; jl -> continue. count>=10: 请先领取奖励.
            if (m_nRewardCount >= RewardPendingCap)
            {
                RewardSysMsg("请先领取奖励"); // @0x6FB814
                return;
            }

            // 0x6FB728 call sub_6DD5A8 (sum of 金刚石 Dura) ; cmp eax,0xA ; jl -> 你没有足够的金刚石.
            if (NativeCountDiamonds() < RewardDiamondGateMin)
            {
                RewardSysMsg("你没有足够的金刚石"); // @0x6FB860
                return;
            }

            // 0x6FB736 eax=[+0x15C]+0xC350 ; cmp eax,[+0x68C] ; jle 0x6FB761 (ok).
            // Reproduce the native signed 32-bit add + signed compare exactly (the
            // gold cap makes the wraparound path unreachable in practice anyway).
            if (unchecked(m_nGold + RewardGoldHeadroom) > m_nGoldMax)
            {
                RewardSysMsg("你携带的金币太多了"); // @0x6FB82C
                return;
            }

            // 0x6FB761 call sub_7441D8 (0x30 - bag.Count) ; test/jle -> 你的包裹太满了.
            if (RewardBagCapacity - (m_ItemList?.Count ?? 0) <= 0)
            {
                RewardSysMsg("你的包裹太满了"); // @0x6FB848
                return;
            }

            // All gates pass: native would inc count, deduct 10 diamonds, draw and
            // reply SM 0x1227. The reward selection needs the unmodelled manager.
            NativeCmTailFailClosed.Drop(Grobal2.CM_4647, m_sCharName);
        }

        /// <summary>
        /// CM 4648, worker 0x6FB874 — 领奖结算.
        ///
        /// count&lt;1: 0x6FB8A3 cmp esi,1 / jl 0x6FBA6A jumps straight to the epilogue
        /// with no packet — faithful silence.
        ///
        /// count&gt;=1: for each pending entry the worker resolves its name from the
        /// runtime manager (sub_69C5E4) and credits 声望 [self+0x4F0] / 金币
        /// [self+0x15C] (ceiling [self+0x68C]) / an item, with a SysMsg per reward,
        /// then zeroes the top slot and subtracts the credited count. Every branch
        /// keys off the manager-resolved name, which is unmodelled -> withheld.
        /// </summary>
        private void RewardSettle()
        {
            if (m_nRewardCount < 1)
            {
                return; // native: no reply, no side effect
            }

            NativeCmTailFailClosed.Drop(Grobal2.CM_4648, m_sCharName);
        }

        /// <summary>
        /// CM 4649, worker 0x6FBB28 -> sub_69C47C — 领奖(含删物品).
        ///
        /// clientId = the wire record Recog (leaf 0x6DBC0C mov edx,[record+0]).
        /// sub_69C47C ignores its manager `this` and only touches the player's bag:
        /// it scans [self+0x508] from the end (0x69C49D..0x69C507), matches
        /// word[item+0x24] = wIndex (sub_784560) against clientId, and on the first
        /// hit removes it (TList.Delete 0x424B30), sends SM_DELITEM/202 keyed on
        /// [item+0x18]=ClientItemID (vtbl+0x268 sub_73CBAC), frees it, then grants
        /// diamonds via sub_6DD674(self, 0x24C). It returns true iff an item was
        /// deleted. The worker replies SM 0x1229 through [obj+0x250] with
        /// Recog = deleted ? 0 : 1 (0x6FBB47 test al / 0x6FBB4B xor esi,esi;
        /// esi initialised to 1 at 0x6FBB32), empty body.
        /// </summary>
        private void RewardClaimWithItemDelete(int clientId)
        {
            var deleted = NativeDeleteBagItemByWIndex(clientId);

            // esi = 1 default; al!=0 -> esi = 0. Recog = deleted ? 0 : 1.
            var recog = deleted ? 0 : 1;
            SendDefMessage((short)Grobal2.SM_REWARDCLAIMITEM, recog, 0, 0, 0,
                string.Empty);
        }

        /// <summary>
        /// CM 4650, worker 0x6FB51C -> sub_69C648 + synthesis state machine
        /// sub_69C03C — 藏宝图合成.
        ///
        /// The worker resolves the treasure-map material through the item-template
        /// db [[0x7D5D6C]] (sub_69C648), runs the synthesis state machine over the
        /// client body (sub_69C03C: builds a word array from the body, walks the bag
        /// [self+0x508], consumes materials, validates the 藏宝图 recipe) and uses
        /// its return (0..5) to index the reply jump table [eax*4+0x6FB569]. The six
        /// outcomes and which of the six texts (@0x6FB628/644/65C/678/6AC/6CC) plus
        /// the SM 0x122A Recog (0 when sub_69C03C returns 0, else 1) depend entirely
        /// on that state machine and the recipe data — not derivable from the image.
        /// </summary>
        private void RewardTreasureMapSynth()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4650, m_sCharName);
        }

        /// <summary>
        /// sub_6DD5A8 — sums the Dura (word[item+0x26], sub_7845A0) of every bag
        /// item [self+0x508] whose std-item name equals "金刚石" (CompareStr against
        /// @0x6DD648 is case-sensitive). Native scans from the end; the sum is
        /// order-independent. Used as the 4647 diamond gate.
        /// </summary>
        private int NativeCountDiamonds()
        {
            var items = m_ItemList;
            if (items == null)
            {
                return 0;
            }

            var total = 0;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (item == null)
                {
                    continue;
                }

                var std = M2Share.UserEngine?.GetStdItem(item.wIndex);
                if (std != null && string.Equals(std.Name, RewardDiamondItemName,
                        StringComparison.Ordinal))
                {
                    total += item.Dura;
                }
            }

            return total;
        }

        /// <summary>
        /// sub_69C47C body — deletes the first bag item (scanning from the end)
        /// whose wIndex matches <paramref name="clientId"/>. On success: SendDelItems
        /// (SM_DELITEM keyed on ClientItemID, = vtbl+0x268), Dispose, then the
        /// diamond grant sub_6DD674(0x24C). Returns whether an item was deleted.
        /// </summary>
        private bool NativeDeleteBagItemByWIndex(int clientId)
        {
            var items = m_ItemList;
            if (items == null)
            {
                return false;
            }

            // 0x69C49D esi = bag.Count-1 ; 0x69C4A6..0x69C507 walk downward.
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (item == null)
                {
                    continue;
                }

                // 0x69C4BE call sub_784560 -> movzx word[item+0x24]=wIndex ; cmp clientId.
                if (item.wIndex != clientId)
                {
                    continue;
                }

                items.RemoveAt(i);          // 0x69C4D5 TList.Delete
                SendDelItems(item);          // 0x69C4E1 vtbl+0x268 -> SM_DELITEM/202
                Dispose(item);               // 0x69C4EB free
                NativeGrantDiamonds();       // 0x69C4F8 sub_6DD674(self,0x24C)
                return true;                 // 0x69C4FD result = true
            }

            return false;
        }

        /// <summary>
        /// sub_6DD674(self, 0x24C) — distributes 588 金刚石 (@0x6DD728) into the bag
        /// as DuraMax-sized stacks, creating one item per stack (sub_74C2FC), until
        /// the amount is exhausted or the bag reaches 48 items (0x6DD6B5
        /// cmp [bag+8],0x30 / jge). Each item's Dura is set to min(remaining,
        /// DuraMax) (0x6DD6E1 / 0x6DD6ED) and added via vtbl+0x248 with reason 0 and
        /// stampEnable=0 (0x6DD6F8), which does not emit SM_ADDITEM — matching native.
        /// </summary>
        private void NativeGrantDiamonds()
        {
            var userEngine = M2Share.UserEngine;
            if (userEngine == null)
            {
                return;
            }

            var remaining = RewardDiamondGrantAmount;
            // Native bounds the outer loop at 0x24C iterations (esi at 0x6DD6A3).
            for (var iter = 0; iter < RewardDiamondGrantAmount && remaining > 0;
                iter++)
            {
                // 0x6DD6AC cmp [bag+8],0x30 ; jge return.
                if ((m_ItemList?.Count ?? int.MaxValue) >= RewardBagCapacity)
                {
                    break;
                }

                TUserItem item = null;
                // sub_74C2D4 (find by name) + sub_74C2FC (make). Missing template ->
                // native keeps spinning to no effect; break is equivalent.
                if (!userEngine.CopyToUserItemFromName(RewardDiamondItemName, ref item)
                    || item == null)
                {
                    break;
                }

                int duraMax = item.DuraMax;
                if (duraMax >= remaining)
                {
                    item.Dura = (ushort)remaining;      // 0x6DD6E1 mov [item+0x26],remaining
                    AddItemToBag(item, 0, false);        // 0x6DD6F8 vtbl+0x248
                    return;                              // done flag -> 0x6DD70D
                }

                item.Dura = (ushort)duraMax;             // 0x6DD6ED mov [item+0x26],DuraMax
                AddItemToBag(item, 0, false);
                remaining -= duraMax;                    // 0x6DD6F5 sub remaining,DuraMax

                if (duraMax <= 0)
                {
                    break; // native would spin (DuraMax is never 0 for 金刚石); guard anyway.
                }
            }
        }

        /// <summary>
        /// vtbl+0xD4 SysMsg (sub_73C8F4): emits SM_SYSMESSAGE with wParam 0x38FF and
        /// the text body, all other params 0 (verified via sub_765E68 arg map). The
        /// C# RM_SYSMESSAGE routing encodes to the same wire ident 战神's cx=0x2774
        /// does; wParam carries the 0x38FF the caller passes.
        /// </summary>
        private void RewardSysMsg(string text)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, RewardSysMsgParam,
                0, 0, 0, text, BuildNativeTerminatedTextBody(text));
        }
    }
}
