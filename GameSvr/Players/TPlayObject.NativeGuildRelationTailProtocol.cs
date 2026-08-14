using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeGuildMemberSize = 31;

        private bool TryHandleNativeGuildRelationProtocol(
            TProcessMessage processMessage)
        {
            if (processMessage == null) return false;
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_GILD_QUERY_REQUEST_JOIN_LIST:
                    SendNativeGuildRequestListPage(processMessage,
                        Grobal2.SM_GILD_QUERY_REQUEST_JOIN_LIST, false);
                    return true;
                case Grobal2.CM_GILD_QUERY_REQUEST_UNION_LIST:
                    SendNativeGuildRequestListPage(processMessage,
                        Grobal2.SM_GILD_QUERY_REQUEST_UNION_LIST, true);
                    return true;
                case Grobal2.CM_GILD_REFUSE_REQUEST:
                    HandleNativeGildRefuseRequest(processMessage);
                    return true;
                case Grobal2.CM_GILD_REQUEST_UNION:
                    HandleNativeGildRequestUnion(processMessage);
                    return true;
                case Grobal2.CM_GILD_BREAK_UNION:
                    HandleNativeGildBreakUnion(processMessage);
                    return true;
                case Grobal2.CM_GILD_QUERY_UNION:
                    SendNativeGuildRelationPage(processMessage,
                        Grobal2.SM_GILD_QUERY_UNION,
                        NativeCorpsService.GildUnion);
                    return true;
                case Grobal2.CM_GILD_CONCERN_GILD_ID:
                    HandleNativeGildAddConcernById(processMessage);
                    return true;
                case Grobal2.CM_GILD_QUERY_CONCERN:
                    SendNativeGuildConcernPage(processMessage);
                    return true;
                case Grobal2.CM_GILD_CANCLE_CONCERN:
                    // The no-store fail-closed SendUnsupportedNativeGuildIdOperation
                    // fallback is kept INLINE here so the static
                    // NativeGildCancelConcernExactCheck boundary stays green: its
                    // slice requires this branch to retain that helper, and the
                    // older dormant cancel-concern transaction stays referenced by
                    // no live source (the wired path uses the newer concern ladder
                    // in HandleNativeGildCancelConcern instead).
                    if (CorpsService.SupportsGildWrites)
                        HandleNativeGildCancelConcern(processMessage);
                    else
                        SendUnsupportedNativeGuildIdOperation(processMessage,
                            Grobal2.SM_GILD_CANCLE_CONCERN, false);
                    return true;
                case Grobal2.CM_GILD_DECLARE_WAR:
                    HandleNativeGildDeclareWar(processMessage);
                    return true;
                case Grobal2.CM_GILD_QUERY_HOSTILE:
                    SendNativeGuildRelationPage(processMessage,
                        Grobal2.SM_GILD_QUERY_HOSTILE,
                        NativeCorpsService.GildHostile);
                    return true;
                case Grobal2.CM_GILD_ENABLE_UNION:
                    HandleNativeGildEnableUnion(processMessage);
                    return true;
                case Grobal2.CM_GILD_QUERY_LOG:
                    SendUnsupportedNativeGuildPage(processMessage,
                        Grobal2.SM_GILD_QUERY_LOG, true);
                    return true;
                case Grobal2.CM_GILD_EXIT:
                    HandleNativeGildExit();
                    return true;
                case Grobal2.CM_GILDMEMBER_LIST:
                    SendNativeGuildMemberList();
                    return true;
                case Grobal2.CM_GILD_DECLARE_WAR_NAME:
                    HandleNativeGildDeclareWarByName(processMessage);
                    return true;
                case Grobal2.CM_GILD_CONCERN_GILD_NAME:
                    HandleNativeGildAddConcernByName(processMessage);
                    return true;
                case Grobal2.CM_GILD_VICECAPTAIN_STEPDOWN:
                    HandleNativeGildViceStepDown();
                    return true;
                case Grobal2.CM_GILD_DISMISS_VICECAPTAIN:
                    HandleNativeGildDismissVice(processMessage);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryHandleNativeGuildTailProtocol(
            TProcessMessage processMessage)
        {
            if (processMessage == null) return false;
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_GILD_ACCEPT_REQUEST:
                    HandleNativeGildAcceptRequest(processMessage);
                    return true;
                case Grobal2.CM_FIND_CORPS_BYNAME:
                    FindNativeCorpsByName(processMessage);
                    return true;
                case Grobal2.CM_FIND_GILD_BYNAME:
                    FindNativeGuildByName(processMessage);
                    return true;
                case Grobal2.CM_GILD_CANCEL_JOIN:
                    HandleNativeGildCancelJoin();
                    return true;
                case Grobal2.CM_REFRESH_CORPSINFO:
                    SendNativePlayerCorps(Grobal2.SM_REFRESH_CORPSINFO);
                    return true;
                case Grobal2.CM_REFRESH_GILDINFO:
                    SendNativePlayerGuildRefresh();
                    return true;
                default:
                    return false;
            }
        }

        private void SendUnsupportedNativeGuildPage(
            TProcessMessage processMessage, int ident, bool logPage)
        {
            var requested = unchecked((ushort)(logPage
                ? processMessage.wParam
                : processMessage.nParam3));
            if (requested > NativeCorpsService.MaximumPageSize) return;
            SendNativeCorpsStatus(ident, NativeCorpsService.UnknownError);
        }

        private void SendUnsupportedNativeGuildDecision(
            TProcessMessage processMessage, int ident)
        {
            if (!NativeCorpsWireCodec.TryReadId(
                    GetNativeCorpsBody(processMessage), out var id))
                return;

            SendNativeCorpsPacket(BuildNativeCorpsHeader(ident,
                    processMessage.nParam1,
                    NativeCorpsService.UnknownError, 0, 0),
                NativeCorpsWireCodec.EncodeId(id));
        }

        private void SendUnsupportedNativeGuildNameOperation(
            TProcessMessage processMessage, int ident, int minimumBytes)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (body.Length < minimumBytes
                || !NativeCorpsWireCodec.TryDecodeRawText(body,
                    out var name)
                || string.IsNullOrEmpty(name))
                return;

            SendNativeCorpsStatus(ident, NativeCorpsService.UnknownError);
        }

        // 4575 CM_GILD_QUERY_UNION / 4580 CM_GILD_QUERY_HOSTILE live read (native
        // sub_6F64D8 / sub_6F6A7C): the caller gild's allied / hostile gild list,
        // paginated. Page index = nParam2 (Tag), page size = nParam3 (Series),
        // guarded to <= 32 (native `if (pageSize <= 32)`; a larger size gets NO
        // reply). The reply mirrors the native +0x254 send
        // (Recog 0, Param = page index, Tag = records-in-page, Series = result):
        // result 12 = the caller has no gild, else 0 (an empty past-the-end page is
        // still 0 — these handlers, unlike the gild LIST, have no "30" code).
        // Records are 24 bytes {gild id, gild name}.
        private void SendNativeGuildRelationPage(TProcessMessage processMessage,
            int ident, byte relation)
        {
            var page = unchecked((ushort)processMessage.nParam2);
            var requested = unchecked((ushort)processMessage.nParam3);
            if (requested > NativeCorpsService.MaximumPageSize) return;

            var gilds = CorpsService.GetGildRelationPage(
                GetCachedNativeUserId(), relation, page, requested,
                out var result);
            SendNativeCorpsPacket(BuildNativeCorpsHeader(ident, 0, page,
                    gilds.Count, result),
                NativeCorpsWireCodec.EncodeGildRelationSummaries(gilds));
        }

        // 4577 CM_GILD_QUERY_CONCERN live read (native sub_6F6784): the caller gild's
        // concern set (gild+44), paginated. Same page params / guard / reply frame as
        // the relation queries; records are 32 bytes {gild id, gild name,
        // caller<->concerned relation byte}.
        private void SendNativeGuildConcernPage(TProcessMessage processMessage)
        {
            var page = unchecked((ushort)processMessage.nParam2);
            var requested = unchecked((ushort)processMessage.nParam3);
            if (requested > NativeCorpsService.MaximumPageSize) return;

            var gilds = CorpsService.GetGildConcernPage(
                GetCachedNativeUserId(), page, requested, out var result);
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_GILD_QUERY_CONCERN, 0, page, gilds.Count,
                    result),
                NativeCorpsWireCodec.EncodeGildConcernSummaries(gilds));
        }

        // 4570 CM_GILD_QUERY_REQUEST_JOIN_LIST / 4571 CM_GILD_QUERY_REQUEST_UNION_LIST live read (native
        // sub_6F6064 / sub_6F61BC): the caller gild's pending JOIN / UNION request list (from the in-memory
        // request ledger, ledger-owned), paginated. HEADER-only page params (NO body decode, NO 8-byte
        // length gate). These idents are NOT cased in ProcessUserMessage -> they hit the DEFAULT
        // SendMsg(PlayObject, Ident, Series, Recog, Param, Tag) path, so on TProcessMessage: wParam=Series,
        // nParam3=Tag. codec-fidelity: page SIZE = Tag (=nParam3, cap 32); page INDEX = Series (=wParam).
        // Reply frame = Recog 0, Param = page index, Tag = records-in-page, Series = result (12 = caller has
        // no gild, else 0). Records are 56-byte sub_70839C {[0]requester id, [8]UNIQUE request id, [16]name1,
        // [32]name2 owner/leader, [48]resolved flag}. The ledger fills once create (4560/4573) runs.
        private void SendNativeGuildRequestListPage(
            TProcessMessage processMessage, int ident, bool union)
        {
            var requested = unchecked((ushort)processMessage.nParam3);   // Tag = page size (default case)
            var page = unchecked((ushort)processMessage.wParam);         // Series = page index (default case)
            var service = CorpsService;
            var userId = GetCachedNativeUserId();
            var result = 0;
            IReadOnlyList<(long SecondaryKey, long UniqueId, string Name,
                    string OwnerName, int Flag)> requests =
                Array.Empty<(long, long, string, string, int)>();
            if (requested <= NativeCorpsService.MaximumPageSize)
            {
                requests = union
                    ? service.GetGildUnionRequestPage(userId, page, requested,
                        out result)
                    : service.GetGildJoinRequestPage(userId, page, requested,
                        out result);
            }
            // An oversized page size sends an EMPTY page (0 records, result 0), not a dropped packet.
            SendNativeCorpsPacket(
                BuildNativeCorpsHeader(ident, 0, page, requests.Count, result),
                NativeCorpsWireCodec.EncodeGildRequestSummaries(requests));
        }

        // 4573 CM_GILD_REQUEST_UNION live routing. ADDITIVE + gated on SupportsGildWrites (no store ->
        // original SendUnsupportedNativeGuildNameOperation, Param=1000). Client sends the target gild NAME
        // (>= 8 bytes); routed through NativeCorpsService.ApplyGildRequestUnion (name resolve -> 12;
        // president-only ladder 5/12/25/19/34/15/33/8/0; Relation=3 PENDING publish into the relation map
        // + fail-safe INSERT BEFORE the dup probe, mirroring the native order). Replies SM 4573 via SendDefMessage
        // (Param=result), matching native sub_6F6390's +592 reply.
        private void HandleNativeGildRequestUnion(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildNameOperation(processMessage,
                    Grobal2.SM_GILD_REQUEST_UNION, 8);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (body.Length < 8
                || !NativeCorpsWireCodec.TryDecodeRawText(body, out var name)
                || string.IsNullOrEmpty(name))
                return;
            SendNativeCorpsStatus(Grobal2.SM_GILD_REQUEST_UNION,
                service.ApplyGildRequestUnion(GetCachedNativeUserId(), name));
        }

        // 4572 CM_GILD_REFUSE_REQUEST live routing. ADDITIVE + gated on SupportsGildWrites (no store ->
        // original SendUnsupportedNativeGuildDecision, Param=1000, id echoed). Reads the request id from the
        // body and runs NativeCorpsService.ApplyGildRefuseRequest (role×type cascade + subtype refuse ladder
        // -> ledger.Remove; see its IDAT-ASSUMPTION on the sub_6A5284 lookup key/scope and the DEFERRED
        // applicant notify). Echoes the id with the result (Recog = request nParam1), matching native
        // sub_6F6340's buffered reply.
        private void HandleNativeGildRefuseRequest(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildDecision(processMessage,
                    Grobal2.SM_GILD_REFUSE_REQUEST);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var requestId)) return;
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_GILD_REFUSE_REQUEST, processMessage.nParam1,
                    service.ApplyGildRefuseRequest(GetCachedNativeUserId(),
                        requestId), 0, 0),
                NativeCorpsWireCodec.EncodeId(requestId));
        }

        // 4611 CM_GILD_ACCEPT_REQUEST live routing. ADDITIVE + gated on SupportsGildWrites (no store ->
        // original SendUnsupportedNativeGuildDecision, Param=1000, id echoed). Reads the UNIQUE request id
        // from the body (echoed by the client from the 4570/4571 listing; the president identity comes from
        // the CONNECTION, not the body) and runs NativeCorpsService.ApplyGildAcceptRequest (role×type cascade
        // + subtype accept: JOIN add-to-gild sub_706264 / UNION save_relation DELETE-3+INSERT-1 sub_708168,
        // then ledger.RemoveByUniqueId). Echoes the id with the result (Recog = request nParam1), matching
        // native sub_6F62F0's buffered SM 0x1203 reply. DEFERRED (native follow-up, tracked): the SM 4612
        // push to the accepted party — the applicant currently learns on re-query, not push.
        private void HandleNativeGildAcceptRequest(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildDecision(processMessage,
                    Grobal2.SM_GILD_ACCEPT_REQUEST);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var requestId)) return;
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_GILD_ACCEPT_REQUEST, processMessage.nParam1,
                    service.ApplyGildAcceptRequest(GetCachedNativeUserId(),
                        requestId), 0, 0),
                NativeCorpsWireCodec.EncodeId(requestId));
        }

        // 4627 CM_GILD_CANCEL_JOIN live routing (cancel my OWN pending gild
        // join/union request). ADDITIVE + gated on SupportsGildWrites (no store ->
        // original fail-closed SM_GILD_CANCEL_JOIN/1000). No wire body — the request
        // is resolved from the caller's identity. The reversed handler gate (not in
        // a corps -> 5), the ledger lookup (not found -> 10) and the subtype cancel
        // (sub_7084A8: unlink only, the pending Relation=3 pair is deliberately left
        // standing) all run inside ApplyGildCancelJoin. Replies SM_GILD_CANCEL_JOIN with the result,
        // matching native sub_6ADB60's SendDefMessage(0,4627,0,0,0,result).
        private void HandleNativeGildCancelJoin()
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendNativeCorpsStatus(Grobal2.SM_GILD_CANCEL_JOIN,
                    NativeCorpsService.UnknownError);
                return;
            }
            SendNativeCorpsStatus(Grobal2.SM_GILD_CANCEL_JOIN,
                service.ApplyGildCancelJoin(GetCachedNativeUserId()));
        }

        // 4574 break-union (president-only) live routing. ADDITIVE + gated on
        // service.SupportsGildWrites: with NO gild store the branch reproduces
        // the exact original SendUnsupportedNativeGuildIdOperation (Param=1000,
        // empty body), so every dormancy guard stays green. With a store, the
        // reversed NativeGildUnionConcernTransaction.BreakUnion classifies the
        // live union relation and the real code is returned; the in-memory
        // relation removal + DELETE gamedata.gildrelation happen fail-safe
        // inside NativeCorpsService.ApplyGildBreakUnion. Wire body = target
        // GILD id.
        private void HandleNativeGildBreakUnion(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildIdOperation(processMessage,
                    Grobal2.SM_GILD_BREAK_UNION, false);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var targetGildId))
                return;
            SendNativeCorpsStatus(Grobal2.SM_GILD_BREAK_UNION,
                service.ApplyGildBreakUnion(GetCachedNativeUserId(),
                    targetGildId));
        }

        // 4579 declare-war-by-id (president-only) live routing. ADDITIVE + gated
        // on SupportsGildWrites (no store → original fail-closed 1000, empty
        // body). Wire body = target GILD id. The reversed 30000-gold gate +
        // deduction live HERE (player state m_nGold): hasGold gates the
        // transaction (insufficient → 36, strategy not reached, no relation),
        // and on a DeductsGold(0) result 30000 gold is removed + broadcast —
        // AFTER the war relation was published in-memory + pushed to the store
        // fail-safe (an async SQL failure neither refunds gold nor rolls back
        // the relation). SM reply id is SM_GILD_DECLARE_WAR (4579). The name
        // variant 4585 is deferred (target name-resolution not recoverable from
        // the current dump — see staging/gild_wiring_applied_20260731.md §8).
        private void HandleNativeGildDeclareWar(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildIdOperation(processMessage,
                    Grobal2.SM_GILD_DECLARE_WAR, false);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var targetGildId))
                return;
            var hasGold = m_nGold >=
                          NativeGildDeclareWarTransaction.GoldThreshold;
            var result = service.ApplyGildDeclareWar(
                NativeGildDeclareWarOp.DeclareWarId, GetCachedNativeUserId(),
                targetGildId, hasGold);
            if (NativeGildDeclareWarTransaction.DeductsGold(result))
            {
                m_nGold -= NativeGildDeclareWarTransaction.GoldCost;
                GoldChanged();
            }
            SendNativeCorpsStatus(Grobal2.SM_GILD_DECLARE_WAR, result);
        }

        // 4576 add-concern-by-id (president-only) live routing, gated + fail-safe.
        // Wire body = target GILD id.
        private void HandleNativeGildAddConcernById(
            TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildIdOperation(processMessage,
                    Grobal2.SM_GILD_CONCERN_GILD_ID, false);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var targetGildId))
                return;
            SendNativeCorpsStatus(Grobal2.SM_GILD_CONCERN_GILD_ID,
                service.ApplyGildAddConcernById(GetCachedNativeUserId(),
                    targetGildId));
        }

        // 4586 add-concern-by-name: resolve the target GILD name in the registry,
        // then the 4576 ladder; replies SM 4576. Gated + fail-safe.
        private void HandleNativeGildAddConcernByName(
            TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildNameOperation(processMessage,
                    Grobal2.SM_GILD_CONCERN_GILD_ID, 1);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (body.Length < 1
                || !NativeCorpsWireCodec.TryDecodeRawText(body, out var name)
                || string.IsNullOrEmpty(name))
                return;
            SendNativeCorpsStatus(Grobal2.SM_GILD_CONCERN_GILD_ID,
                service.ApplyGildAddConcernByName(GetCachedNativeUserId(),
                    name));
        }

        // 4578 cancel-concern (president-only). The no-store fallback is inlined
        // in the switch branch (static-boundary guard); here the store is present.
        private void HandleNativeGildCancelConcern(
            TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var targetGildId))
                return;
            SendNativeCorpsStatus(Grobal2.SM_GILD_CANCLE_CONCERN,
                CorpsService.ApplyGildCancelConcern(GetCachedNativeUserId(),
                    targetGildId));
        }

        // 4581 enable-union (president or vice). Session-only flag; desired value
        // is the first body byte (0/1). Gated + fail-safe.
        private void HandleNativeGildEnableUnion(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendNativeCorpsStatus(Grobal2.SM_GILD_ENABLE_UNION,
                    NativeCorpsService.UnknownError);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            var desiredEnabled = body.Length > 0 && body[0] != 0;
            SendNativeCorpsStatus(Grobal2.SM_GILD_ENABLE_UNION,
                service.ApplyGildEnableUnion(GetCachedNativeUserId(),
                    desiredEnabled));
        }

        // 4585 declare-war-by-name: resolve the target GILD name, then the 4579
        // declare-war ladder incl. the 30000-gold gate/deduction; replies SM 4579.
        private void HandleNativeGildDeclareWarByName(
            TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildNameOperation(processMessage,
                    Grobal2.SM_GILD_DECLARE_WAR, 1);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (body.Length < 1
                || !NativeCorpsWireCodec.TryDecodeRawText(body, out var name)
                || string.IsNullOrEmpty(name))
                return;
            var hasGold = m_nGold >=
                          NativeGildDeclareWarTransaction.GoldThreshold;
            var result = service.ApplyGildDeclareWarByName(
                GetCachedNativeUserId(), name, hasGold);
            if (NativeGildDeclareWarTransaction.DeductsGold(result))
            {
                m_nGold -= NativeGildDeclareWarTransaction.GoldCost;
                GoldChanged();
            }
            SendNativeCorpsStatus(Grobal2.SM_GILD_DECLARE_WAR, result);
        }

        // 4583 gild-exit (a member CORPS leaving the Gild) live routing.
        // ADDITIVE + gated on SupportsGildWrites. The handler zone gates mirror
        // the sibling native corps-exit ExitNativeCorps EXACTLY, using the same
        // reversed live accessors: safe-zone (InSafeZone — native sub_76858C,
        // whose 38 mirrors the corps-exit InSafeZone->37 sibling), map fight-zone
        // (m_PEnvir.Flag.boFightZone -> 28) and castle-war (free-PK-area +
        // any-castle-under-war OR the current castle under war -> 29). The
        // in-a-gild(12) gate, the strategy ladder (5/12/18/1000/0) and the DELETE
        // gamedata.gildmember all run inside ApplyGildExit fail-safe (no
        // rollback). With NO store the branch keeps the exact original
        // fail-closed response (SM_GILD_EXIT, UnknownError=1000).
        private void HandleNativeGildExit()
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendNativeCorpsStatus(Grobal2.SM_GILD_EXIT,
                    NativeCorpsService.UnknownError);
                return;
            }

            var castleManager = M2Share.CastleManager;
            var currentCastle = castleManager?.InCastleWarArea(this);
            var inFightZone = m_PEnvir != null && m_PEnvir.Flag.boFightZone;
            var castleWarBlocked =
                (m_boInFreePKArea && castleManager?.AnyCastleUnderWar == true)
                || currentCastle?.m_boUnderWar == true;
            SendNativeCorpsStatus(Grobal2.SM_GILD_EXIT,
                service.ApplyGildExit(GetCachedNativeUserId(), InNativeSafeZone12(),
                    inFightZone, castleWarBlocked));
        }

        // 4587 vice self-stepdown live routing (no wire target — the caller is
        // the current vice). ADDITIVE + gated + fail-safe; with NO store the
        // branch keeps the original SM_GILD_VICECAPTAIN_STEPDOWN/1000.
        private void HandleNativeGildViceStepDown()
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendNativeCorpsStatus(
                    Grobal2.SM_GILD_VICECAPTAIN_STEPDOWN,
                    NativeCorpsService.UnknownError);
                return;
            }
            var result = service.ApplyGildVice(NativeGildViceOp.SelfStepDown,
                GetCachedNativeUserId(), 0);
            if (result == NativeGildViceTransaction.Success)
                NotifyNativeGildViceStepDown(service);
            SendNativeCorpsStatus(Grobal2.SM_GILD_VICECAPTAIN_STEPDOWN,
                result);
        }

        // 4588 president-dismiss-vice live routing. ADDITIVE + gated + fail-safe.
        // Wire body = the vice CORPS id. With NO store the branch keeps the
        // original SendUnsupportedNativeGuildIdOperation (reads the id, replies
        // Param=1000 with an empty body).
        private void HandleNativeGildDismissVice(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildIdOperation(processMessage,
                    Grobal2.SM_GILD_DISMISS_VICECAPTAIN, false);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var targetCorpsId))
                return;
            var result = service.ApplyGildVice(
                NativeGildViceOp.PresidentDismiss,
                GetCachedNativeUserId(), targetCorpsId);
            if (result == NativeGildViceTransaction.Success)
                NotifyNativeGildDismissVice(service, targetCorpsId);
            SendNativeCorpsStatus(Grobal2.SM_GILD_DISMISS_VICECAPTAIN,
                result);
        }

        private void SendNativeGuildMemberList()
        {
            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsStatus(Grobal2.SM_GILDMEMBER_LIST,
                    NativeCorpsService.UnknownError);
                return;
            }
            if (!service.TryGetGildForPlayer(GetCachedNativeUserId(),
                    out var gild))
            {
                SendNativeCorpsStatus(Grobal2.SM_GILDMEMBER_LIST, 12);
                return;
            }

            var online = CaptureNativeCorpsOnlineIds();
            var members = new List<(NativeCorpsMemberSnapshot Member,
                byte Position, bool Online)>();
            foreach (var corpsId in gild.CorpsIds)
            {
                if (!service.TryGetCorps(corpsId, out var corps)) continue;
                members.AddRange(corps.Members.Select(member => (member,
                    service.GetPosition(corps, member.MemberId),
                    online.Contains(member.MemberId))));
            }

            var ordered = members.OrderByDescending(member => member.Position)
                .ToArray();
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_GILDMEMBER_LIST, 0, 0, ordered.Length, 0),
                EncodeNativeGuildMembers(ordered));
        }

        private static byte[] EncodeNativeGuildMembers(
            IReadOnlyList<(NativeCorpsMemberSnapshot Member, byte Position,
                bool Online)> members)
        {
            if (members.Count == 0) return Array.Empty<byte>();

            var corpsRecords = NativeCorpsWireCodec.EncodeCorpsMembers(
                members);
            var body = new byte[checked(members.Count *
                                        NativeGuildMemberSize)];
            for (var index = 0; index < members.Count; index++)
            {
                var source = corpsRecords.AsSpan(
                    index * NativeCorpsWireCodec.CorpsMemberSize,
                    NativeCorpsWireCodec.CorpsMemberSize);
                var target = body.AsSpan(index * NativeGuildMemberSize,
                    NativeGuildMemberSize);
                source[..26].CopyTo(target);
                target[26] = source[28];
                target[27] = source[29];
                target[28] = source[30];
                source.Slice(26, 2).CopyTo(target.Slice(29, 2));
            }
            return body;
        }

        private void FindNativeCorpsByName(TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (body.Length == 0
                || !NativeCorpsWireCodec.TryDecodeRawText(body,
                    out var name)
                || string.IsNullOrEmpty(name))
                return;

            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsStatus(Grobal2.SM_FIND_CORPS_BYNAME,
                    NativeCorpsService.UnknownError);
                return;
            }

            var matches = service.SnapshotCorps()
                .Where(corps => corps.Name.Contains(name,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_FIND_CORPS_BYNAME, 0, 0, 0,
                    matches.Length),
                EncodeNativeCorpsDescriptions(service, matches));
        }

        private void FindNativeGuildByName(TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (body.Length == 0
                || !NativeCorpsWireCodec.TryDecodeRawText(body,
                    out var name)
                || string.IsNullOrEmpty(name))
                return;

            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsStatus(Grobal2.SM_FIND_GILD_BYNAME,
                    NativeCorpsService.UnknownError);
                return;
            }

            var matches = service.SnapshotGilds()
                .Where(gild => gild.Name.Contains(name,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_FIND_GILD_BYNAME, 0, 0, 0,
                    matches.Length),
                EncodeNativeGuildDescriptions(service, matches));
        }

        private void SendNativePlayerGuildRefresh()
        {
            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsStatus(Grobal2.SM_REFRESH_GILDINFO,
                    NativeCorpsService.UnknownError);
                return;
            }

            var playerId = GetCachedNativeUserId();
            if (!service.TryGetPlayerCorps(playerId, out _))
            {
                SendNativeCorpsStatus(Grobal2.SM_REFRESH_GILDINFO, 5);
                return;
            }
            if (!service.TryGetGildForPlayer(playerId, out var gild))
            {
                SendNativeCorpsStatus(Grobal2.SM_REFRESH_GILDINFO, 12);
                return;
            }

            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_REFRESH_GILDINFO, 0, 0, 0, 0),
                EncodeNativeGuildDescription(service, gild,
                    CaptureNativeCorpsOnlineIds()));
        }
    }
}
