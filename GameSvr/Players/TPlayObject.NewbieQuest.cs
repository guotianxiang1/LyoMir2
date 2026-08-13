using System;
using System.Collections.Generic;
using System.Linq;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// NewbieQuest / PageList subsystem — faithful ports of the two CM idents that
    /// cm-4 had previously left fail-closed:
    ///
    ///   • CM 4496 (0x6DBBDC -> sub_6FAC8C) 新手任务 / FreshmanTaskCommand
    ///   • CM 4626 (0x6DB394 -> sub_6AE260) 分页列表查询 / recruiting-corps list
    ///
    /// Everything here is derived instruction-by-instruction from the flat image
    /// (base 0x400000); no field semantics are invented. Where the native leaf runs
    /// into a subsystem this port cannot evaluate (the task-board @Main script), the
    /// packet is withheld exactly as the sibling task-board handlers already do.
    ///
    /// Wiring (INTENTIONALLY left to the integrator so this file does not touch the
    /// main dispatcher). TryHandleNewbieQuestCm must run BEFORE the cm-4 fail-closed
    /// tail so it intercepts 4496/4626 before ClientNativeFreshmanTaskCommand /
    /// ClientNativePagedListQuery. The one-line insertion in TPlayObject.Message.cs
    /// (the Operate default arm, currently
    ///     if (!TryHandleNativeSocialProtocol(ProcessMsg)
    ///         &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg))
    /// ) becomes
    ///     if (!TryHandleNativeSocialProtocol(ProcessMsg)
    ///         &amp;&amp; !TryHandleNewbieQuestCm(ProcessMsg)
    ///         &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg))
    /// Once wired, CM 4626 goes live and the cm-4 NativeCmTailFailClosed entry for
    /// 4626 is superseded (4496 stays fail-closed either way).
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>
        /// Entry hook for the NewbieQuest / PageList idents. Returns true when it has
        /// consumed the message. See the class remark for the intended wiring point.
        /// </summary>
        private bool TryHandleNewbieQuestCm(TProcessMessage processMessage)
        {
            if (processMessage == null)
            {
                return false;
            }

            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4496:
                    ClientNewbieFreshmanTaskCommand();
                    return true;
                case Grobal2.CM_4626:
                    SendNativeRecruitingCorpsList(processMessage);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 4496, native leaf 0x6DBBDC (`8B 45 CC / 8B 10` Recog=[record], `8B 45 FC`
        /// Self, `E8 .. call 0x6FAC8C`), worker sub_6FAC8C.
        ///
        /// The worker (8-local SEH frame) does exactly this:
        ///   0x6FACBF  call 0x41AFE4        ; format Recog (EDX) into local[ebp-0x30]
        ///   0x6FACC8  movsd ×4             ; copy 16 bytes -> local[ebp-0x20]
        ///   0x6FACDA  mov eax,[0x7D5D20] / mov eax,[eax]   ; task-board admin object
        ///   0x6FACE5  call 0x69AEB8        ; run @Main proc, name = AnsiString
        ///                                  ;   "FreshmanTaskCommand" (@0x6FAD61),
        ///                                  ;   args = (Self, Recog-string, out[ebp-0x10])
        ///   0x6FACF8  call 0x41F6FC / 0x4177C0  ; esi = StrToInt(script result), else -1
        ///   0x6FAD13  mov dx,0x1190 / call [vmt+0x250]  ; SM_4496, Recog=esi, rest 0
        ///
        /// So the entire reply — SM 0x1190 with Recog = the integer the
        /// FreshmanTaskCommand @Main procedure returns — is produced by the task-board
        /// script object [[0x7D5D20]] (TTaskAdmin; [[0x7D5D20]]=0x7DC4A4, and its live
        /// instance is empty in the runtime dump). That @Main script machinery is not
        /// modelled in this port (same blocker as CM 4150/4151/4417/4651). The return
        /// value cannot be derived from the image without inventing the script's
        /// behaviour, so the packet is withheld (fail-closed) rather than guessed —
        /// emitting Recog=-1 would falsely assert "script absent / command failed".
        /// </summary>
        private void ClientNewbieFreshmanTaskCommand()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4496, m_sCharName);
        }

        /// <summary>
        /// CM 4626, native leaf 0x6DB394 (`0x6AE260(Self, Param=word[record+6],
        /// Tag=word[record+8])`), worker sub_6AE260 — the paged RECRUITING-corps list.
        ///
        /// sub_6AE260 is byte-for-byte the sibling of the CM_CORPS_LIST/4520 worker
        /// sub_6AE108; the ONLY differences are the reply ident (0x1212 vs 0x11A8) and
        /// the list it walks: 4520 reads TCorpsManager[+0x24] (the master, all corps),
        /// 4626 reads TCorpsManager[+0x28]. sub_5EC0D8 rebuilds [+0x28] from [+0x24]
        /// keeping only the corps for which predicate sub_705690 is FALSE, and
        ///   sub_705690:  mov eax,[corps+0x30] ; MemberList (TList)
        ///                cmp [eax+8],0x1E     ; MemberList.Count >= 30 ?
        ///                setge al
        /// i.e. [+0x28] = { corps : Members.Count &lt; 30 } — the non-full / recruitable
        /// corps (MaximumMembers = 30). Hence this is the recruiting-corps page.
        ///
        /// Pagination + status are identical to CM 4520 / NativeCorpsService.GetCorpsPage
        /// (verified against sub_6AE260):
        ///   0x6AE285  cmp Tag,0x20 / jg exit       -> pageSize &gt; 32: send nothing
        ///   0x6AE298  esi = Param * Tag            -> start = page * pageSize
        ///   start &gt;= count -> empty page; status stays 0x1E(30) unless
        ///   (count==0 &amp;&amp; Param==0) -> 0; start &lt; count -> status 0, clamp to pageSize
        ///   reply SM 0x1212 via [vmt+0x254]: Recog=Param(page), Param=status,
        ///   Tag=pageSize, Series=pageItemCount, body=pageItemCount × 64-byte records.
        ///
        /// The 64-byte record built by sub_7060B8 maps field-for-field onto the corps
        /// team's proven NativeCorpsWireCodec.EncodeCorpsDescription (CorpsDescSize=64):
        ///   +0x00 int64 Id      &lt;- src[+0x10]/[+0x14]   (two dwords = corps Id lo/hi)
        ///   +0x08 str15 Name    &lt;- src[+0x08]           (corps name)
        ///   +0x18 str15 gild    &lt;- [src[+0x04](TGild)+0x10] (parent-guild name)
        ///   +0x28 str15 captain &lt;- src[+0x30][0][+0x10]  (first member = captain)
        ///   +0x38 byte  members &lt;- src[+0x30].Count
        ///   +0x39 byte  online  &lt;- sub_7056C4 (members with [member+0x28]!=0)
        /// so the whole packet is reproduced by reusing EncodeNativeCorpsDescriptions
        /// on the recruiting page. Because both 4520 and 4626 read the SAME corps
        /// objects, the canonical ordering is deferred to the corps team's model
        /// (SnapshotCorps -> OrderBy(Id)); this only adds the proven member-count gate.
        /// </summary>
        private void SendNativeRecruitingCorpsList(TProcessMessage processMessage)
        {
            var page = unchecked((ushort)processMessage.nParam2);
            var requested = unchecked((ushort)processMessage.nParam3);
            if (requested > NativeCorpsService.MaximumPageSize)
            {
                return; // native 0x6AE285: Tag > 0x20 -> silent, no packet
            }

            var service = CorpsService;
            var corps = GetNativeRecruitingCorpsPage(service, page, requested,
                out var result);
            var body = EncodeNativeCorpsDescriptions(service, corps);
            SendNativeCorpsPacket(
                BuildNativeCorpsHeader(Grobal2.SM_4626, page, result, requested,
                    corps.Count),
                body);
        }

        /// <summary>
        /// Recruiting-corps page = NativeCorpsService.GetCorpsPage restricted to
        /// TCorpsManager[+0x28] (corps with fewer than MaximumMembers members). The
        /// status/clamp logic is copied verbatim from GetCorpsPage so 4626 and 4520
        /// stay in lockstep; only the .Where(...) member-count gate — proven from
        /// sub_705690/sub_5EC0D8 — narrows the source list.
        /// </summary>
        private static IReadOnlyList<NativeCorpsSnapshot> GetNativeRecruitingCorpsPage(
            NativeCorpsService service, int page, int pageSize, out int result)
        {
            result = 0;
            if (!service.IsAvailable)
            {
                result = NativeCorpsService.UnknownError;
                return Array.Empty<NativeCorpsSnapshot>();
            }

            var ordered = service.SnapshotCorps()
                .Where(corps => corps.Members.Count < NativeCorpsService.MaximumMembers)
                .ToArray();
            var start = (long)page * pageSize;
            if (start >= ordered.Length)
            {
                if (ordered.Length != 0 || page != 0)
                {
                    result = 30; // native 0x1E: page out of range
                }

                return Array.Empty<NativeCorpsSnapshot>();
            }

            return ordered.Skip(unchecked((int)start)).Take(pageSize).ToArray();
        }
    }
}
