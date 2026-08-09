using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    // ================================================================================================
    // CM_1017 (0x3F9) item-stack MERGE — gated, faithful wiring of native sub_6D5E50 @0x006D5E50.
    //
    // DIVERGENCE (task #75 / A1): the legacy live case is a stale fixed ack SendDefMessage(1,0,0,0,0,"")
    // inherited from the GameOfMir ancestor. In THIS binary CM_1017 -> sub_6D5E50 consolidates duplicate
    // bag stacks into the use-item in slot U_BUJUK(9): it DELETES each merged stack server-side and folds
    // +100 (fixed) per stack into the target's count, clamped to the cap, then pushes two client updates.
    // Evidence: staging/ida_pileup_response_immediates.txt:30924 (full Hex-Rays + disasm); model
    // GameSvr/Services/NativeItemMerge.cs (result ladder + offsets); cm_1017_item_merge_20260801.md.
    //
    // Native<->C# mapping (confirmed):
    //   Recog (msg int @+0) = ProcessMsg.nParam1 ; wParam (msg word @+6) = ProcessMsg.nParam2
    //     (per the UsrEngn.ProcessUserMessage default decode: Recog->nParam1, Param->nParam2, Series->wParam)
    //   target = m_UseItems[U_BUJUK(9)] (native sub_75EC20(*(Self+0x4C0=1216),9)); item+0x18 = ClientItemID
    //   candidates = m_ItemList (native *(Self+0x508)) scanned HIGH index -> low
    //   delete idiom = Dispose(item)+m_ItemList.RemoveAt(i) (native sub_404690 + sub_424B30) — see
    //     TPlayObject.Base.cs:1945-1961 for the identical existing pattern
    //   removed-set send  = RM_SENDDELITEMLIST (native sub_765F6C, cx=0x27A4=10148)
    //   stack-update send = RM_DURACHANGE slot 9 (native sub_765E68, cx=0x278D=10125)
    //
    // ---- DUPE/LOSS AUDIT (per team-lead mandate) --------------------------------------------------
    //  gate-fail paths (wParam!=9 / slot-9 empty / Recog!=target.ClientItemID / kind-guard fail /
    //    nothing mergeable): native returns SILENT — no send, no mutation. No dup/loss.
    //  merge path: each candidate is recorded in the removed-set AND removed from m_ItemList EXACTLY once
    //    (high->low delete never shifts unvisited lower indices); the client deletes exactly those
    //    ClientItemIDs; the target's new count is pushed via RM_DURACHANGE. Server bag == client bag =>
    //    NO duplication. The +100 is a FIXED per-stack fold-in (native ignores the candidate's own count);
    //    the post-loop clamp count=min(count,cap) discards overflow — a real but FAITHFUL value-clamp
    //    (native @006D5F9C); the overshooting candidate is fully consumed even if only part of its 100
    //    lands — also faithful (native deletes it in-iteration then clamps). Break is checked at the TOP
    //    of each iteration => at most one candidate overshoots. No unfaithful loss.
    //  => The MACHINERY is provably dup/loss-safe. The ONLY residual risk is the item-SELECTION
    //    predicate: too broad => the player LOSES items native would keep; too narrow => under-merges.
    //
    // ---- SELECTION PREDICATE (codec-fidelity confirmed all fields from existing dumps, zero idat) -----
    //   (a) def[+0x00]=std WireIndex=the TUserItem's wIndex; def[+0x1E]=std AniCount(ushort). Reciprocal:
    //       targetStd.AniCount==cand.wIndex && candStd.AniCount==target.wIndex  (NativeType2StdItemRuntime
    //       Append WireIndex@0x00 / AniCount@0x1E; e.g. 泉水罐 1229/1245 <-> 泉水 1245/1229).
    //   (b) off_75E628 = the TVessel class VMT; sub_404828 = Delphi `is` => "target IS-A TVessel" ==
    //       NativeItemFactory.GetClassName(std)=="TVessel" (StdMode 25 && Shape 8).
    //   (c) item+0x34 (word) = TUserItem.btValue[10..11] (reused via TryGetNativePileCompatibility).
    // The MACHINERY is proven dup/loss-safe (audit above). The predicate is now filled.
    //
    // GATE IS LIVE (2026-08-01) — both flip pre-reqs are satisfied:
    //   (1) AniCount width: DONE. `CommonDB.cs:55` loads it as `(ushort)` into `GoodItem.AniCount`,
    //       which IS declared `ushort` (GameSvr/Items/GoodItem.cs:14) — that runtime class is what the
    //       merge predicate reads, so pair-ids >255 (泉水罐 1229/1245 <-> 泉水 1245/1229) match correctly.
    //       NOTE for future passes: `SystemModule/Packet/TStdItem.AniCount` is deliberately still `byte`
    //       — that is the WIRE/packet struct (ReadByte/Write) and must stay byte-wide for DBServer
    //       byte-compatibility. Do NOT "widen" it; it is not the field this predicate uses.
    //   (2) Close-review: done; predicate byte-confirmed, 泉水罐↔泉水 pair verified.
    // Still worth a full-stack item-merge smoke test before production (it is a bag write that deletes
    // stacks). See memory cm1017-merge-wiring-blocked + the report.
    // ================================================================================================
    public partial class TPlayObject
    {
        /// <summary>
        /// Master gate for the CM_1017 native item-merge. LIVE: the selection predicate is fully mapped
        /// and byte-confirmed, the AniCount-width pre-req is satisfied (see file header), and the
        /// machinery is dup/loss-audited. It is still a bag WRITE that deletes stacks, so any change
        /// here needs the conservation audit re-run.
        /// </summary>
        internal static bool NativeItemMergeEnabled { get; set; } = true; // CM_1017 item-merge LIVE (2026-08-01): predicate byte-confirmed + faithful (泉水罐↔泉水 verified), machinery dup/loss-audited. HIGHEST-RISK enable — flag for full-stack item-merge test before production.

        /// <summary>
        /// Faithful port of sub_6D5E50 (CM_1017): consolidate duplicate bag stacks into the U_BUJUK(9)
        /// use-item. Returns true when the merge path OWNS the request (caller suppresses the legacy ack);
        /// false only when the feature is disabled (caller keeps the legacy stale ack).
        /// </summary>
        private bool TryClientNativeItemMergeGated(int recog, int wParam)
        {
            if (!NativeItemMergeEnabled)
                return false;                                   // dormant: caller keeps the legacy stale ack

            // ---- native gate ladder: each early-out returns SILENTLY, matching sub_6D5E50 ----
            if (wParam != NativeItemMerge.WParamGate)           // n9 must be 9
                return true;
            if (m_UseItems == null || Grobal2.U_BUJUK >= m_UseItems.Length)
                return true;
            var target = m_UseItems[Grobal2.U_BUJUK];           // sub_75EC20(useItems, 9)
            if (target == null || target.wIndex <= 0)           // no use-item in slot 9
                return true;
            if (recog != target.ClientItemID)                   // request Recog != target item+0x18
                return true;
            if (!NativeItemMergeTargetKindGuard(target))        // sub_404828(target, off_75E628): target IS-A TVessel
                return true;
            if (m_ItemList == null)
                return true;

            // ---- scan the main bag HIGH->low, folding mergeable stacks into the target ----
            List<TDeleteItem> removed = null;
            for (var i = m_ItemList.Count - 1; i >= 0; i--)
            {
                if (target.DuraMax <= target.Dura)              // native: break at TOP when the target is full
                    break;
                var candidate = m_ItemList[i];
                if (candidate == null)
                    continue;
                if (!IsNativeItemMergeCandidate(target, candidate))   // reciprocal pair + special-excl + merge-gate
                    continue;

                (removed ??= new List<TDeleteItem>()).Add(new TDeleteItem
                {
                    sItemName = M2Share.UserEngine.GetStdItemName(candidate.wIndex),
                    MakeIndex = candidate.MakeIndex,
                    ClientItemID = EnsureClientItemId(candidate),
                });
                Dispose(candidate);                             // sub_404690 (free the merged stack)
                m_ItemList.RemoveAt(i);                         // sub_424B30 (TList.Delete)
                target.Dura = (ushort)(target.Dura + NativeItemMerge.MergeIncrement);   // += 100 per stack
            }

            if (removed != null && removed.Count > 0)
            {
                // sub_765F6C: push the removed/merged set (client main-bag delete).
                SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0, removed.Count, 0, 0, "", removed);
                if (target.DuraMax < target.Dura)               // clamp count to cap (native @006D5F9C)
                    target.Dura = target.DuraMax;
                // sub_765E68(...,9): push the updated slot-9 stack count.
                SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK, target.Dura, target.DuraMax, 0, "");
            }
            // else: no merge => native sends nothing (silent).
            return true;
        }

        // Target kind-guard sub_404828(target, off_75E628) = Delphi `is` operator: "target IS-A TVessel".
        // TVessel <=> the target std-item instantiates as class TVessel (NativeItemFactory: StdMode==25 &&
        // Shape==8; eng047 items 祝福罐/魔令包/泉水罐). Codec-fidelity confirmed off_75E628 == TVessel VMT.
        private static bool NativeItemMergeTargetKindGuard(TUserItem target)
        {
            var std = M2Share.UserEngine.GetStdItem(target.wIndex);
            return std != null && NativeItemFactory.GetClassName(std) == "TVessel";
        }

        // Native "mergeable" predicate (codec-fidelity confirmed the field mappings from existing dumps):
        //   reciprocal std pair : targetStd.AniCount==cand.wIndex && candStd.AniCount==target.wIndex
        //     (def[+0x00]=WireIndex=wIndex ; def[+0x1E]=AniCount ushort — NativeType2StdItemRuntimeAppend)
        //   special exclusion   : NOT(candStd.StdMode==2 && candStd.Shape==10 && cand.Dura!=0)
        //   merge-gate          : native item+0x34 (word) == 0 == TUserItem.btValue[10..11]
        //                         (reused via the pileup handler's TryGetNativePileCompatibility)
        // See the flip pre-req note in the file header (CommonDB AniCount width) — harmless while gated.
        private static bool IsNativeItemMergeCandidate(TUserItem target, TUserItem candidate)
        {
            var targetStd = M2Share.UserEngine.GetStdItem(target.wIndex);
            var candStd = M2Share.UserEngine.GetStdItem(candidate.wIndex);
            if (targetStd == null || candStd == null)
                return false;
            // reciprocal std-shape pair-match (native def[+0x1E] <-> def[+0x00] both ways)
            if (targetStd.AniCount != candidate.wIndex || candStd.AniCount != target.wIndex)
                return false;
            // native special exclusion: a filled StdMode-2/Shape-10 stack is NOT mergeable
            if (candStd.StdMode == NativeItemMerge.SpecialStdMode &&
                candStd.Shape == NativeItemMerge.SpecialShape &&
                candidate.Dura != 0)
                return false;
            // merge-gate word (native item+0x34 == btValue[10..11]) must be 0
            return TryGetNativePileCompatibility(candidate, out var mergeGate) && mergeGate == 0;
        }
    }
}
