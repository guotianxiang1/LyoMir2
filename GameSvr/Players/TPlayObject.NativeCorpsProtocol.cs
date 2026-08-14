using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private NativeCorpsService _nativeCorpsService;
        private Action<ClientPacket, byte[]> _nativeCorpsPacketSink;
        private Func<TPlayObject> _nativeCorpsDirectTargetResolver;

        private const int NativeSocialRoleRefreshIdent = Grobal2.SM_REFRESH_SOCIAL_ROLE;

        private NativeCorpsService CorpsService =>
            _nativeCorpsService ?? M2Share.CorpsService ??
            NativeCorpsService.Unavailable;

        internal void SetNativeCorpsServiceForTests(
            NativeCorpsService service,
            Action<ClientPacket, byte[]> packetSink = null,
            Func<TPlayObject> directTargetResolver = null)
        {
            _nativeCorpsService = service ??
                                  throw new ArgumentNullException(
                                      nameof(service));
            _nativeCorpsPacketSink = packetSink;
            _nativeCorpsDirectTargetResolver = directTargetResolver;
        }

        private bool TryHandleNativeCorpsCoreProtocol(
            TProcessMessage processMessage)
        {
            if (processMessage == null) return false;
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_PLAYER_CORPS:
                    SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
                    return true;
                case Grobal2.CM_CORPS_LIST:
                    SendNativeCorpsList(processMessage);
                    return true;
                case Grobal2.CM_CORPS_QUERY_JOIN:
                    SendNativeCorpsJoinTarget();
                    return true;
                case Grobal2.CM_CORPS_REQUEST_JOIN:
                    RequestNativeCorpsJoin(processMessage);
                    return true;
                case Grobal2.CM_CORPS_CANCEL_JOIN:
                    SendNativeCorpsStatus(Grobal2.SM_CORPS_CANCEL_JOIN,
                        CorpsService.CancelJoin(GetCachedNativeUserId()));
                    return true;
                case Grobal2.CM_CORPS_CREATE:
                    CreateNativeCorps(processMessage);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryHandleNativeCorpsAdminProtocol(
            TProcessMessage processMessage)
        {
            if (processMessage == null) return false;
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_CORPS_MEMBER_LIST:
                    SendNativeCorpsMemberList(processMessage);
                    return true;
                case Grobal2.CM_CORPS_SET_MEMBER_TITLE:
                    SetNativeCorpsMemberTitle(processMessage);
                    return true;
                case Grobal2.CM_CORPS_DISMISS_MEMBER:
                    RunNativeCorpsIdOperation(processMessage,
                        Grobal2.SM_CORPS_DISMISS_MEMBER,
                        CorpsService.DismissMember);
                    return true;
                case Grobal2.CM_CORPS_TRANSFER_CAPTAIN:
                    RunNativeCorpsIdOperation(processMessage,
                        Grobal2.SM_CORPS_TRANSFER_CAPTAIN,
                        CorpsService.TransferCaptain);
                    return true;
                case Grobal2.CM_CORPS_APPOINT_VICE_CAPTAIN:
                    RunNativeCorpsIdOperation(processMessage,
                        Grobal2.SM_CORPS_APPOINT_VICE_CAPTAIN,
                        CorpsService.AppointViceCaptain);
                    return true;
                case Grobal2.CM_CORPS_STEPDOWN:
                    var result = CorpsService.StepDown(GetCachedNativeUserId());
                    SendNativeCorpsStatus(Grobal2.SM_CORPS_STEPDOWN, result);
                    if (result == 0)
                    {
                        SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
                        SendNativeSocialRoleRefresh();
                    }
                    return true;
                case Grobal2.CM_CORPS_GET_RECRUIT_CONDITION:
                    SendNativeCorpsRecruitCondition(processMessage);
                    return true;
                case Grobal2.CM_CORPS_SET_RECRUIT_CONDITION:
                    SetNativeCorpsRecruitCondition(processMessage);
                    return true;
                case Grobal2.CM_CORPS_DIRECT_ADD_MEMBER:
                    DirectAddNativeCorpsMember();
                    return true;
                case Grobal2.CM_CORPS_QUERY_REQUESTS:
                    SendNativeCorpsRequests(processMessage);
                    return true;
                case Grobal2.CM_CORPS_ACCEPT_REQUEST:
                    ProcessNativeCorpsRequests(processMessage, true);
                    return true;
                case Grobal2.CM_CORPS_REFUSE_REQUEST:
                    ProcessNativeCorpsRequests(processMessage, false);
                    return true;
                case Grobal2.CM_CORPS_QUERY_LOG:
                    SendNativeCorpsLogs(processMessage);
                    return true;
                case Grobal2.CM_CORPS_EXIT:
                    ExitNativeCorps();
                    return true;
                case Grobal2.CM_CORPS_NOTICE:
                    HandleNativeCorpsNotice(processMessage);
                    return true;
                case Grobal2.CM_CORPS_DISMISS_VICE_CAPTAIN:
                    RunNativeCorpsIdOperation(processMessage,
                        Grobal2.SM_CORPS_DISMISS_VICE_CAPTAIN,
                        CorpsService.DismissViceCaptain);
                    return true;
                default:
                    return false;
            }
        }

        private void ExitNativeCorps()
        {
            var playerId = GetCachedNativeUserId();
            var service = CorpsService;
            int result;
            // 0x6F57AE call sub_76858C(self) — not sub_7684DC.
            if (!InNativeSafeZone12())
            {
                result = 37;
            }
            else if (service.TryGetPlayerCorps(playerId, out _))
            {
                var castleManager = M2Share.CastleManager;
                var currentCastle = castleManager?.InCastleWarArea(this);
                if (m_PEnvir != null && (m_PEnvir.Flag.boFightZone || m_PEnvir.Flag.boFight3Zone))
                {
                    result = 28;
                }
                else if ((m_boInFreePKArea
                          && castleManager?.AnyCastleUnderWar == true)
                         || currentCastle?.m_boUnderWar == true)
                {
                    result = 29;
                }
                else
                {
                    result = service.Exit(playerId);
                }
            }
            else
            {
                result = service.Exit(playerId);
            }
            SendNativeCorpsStatus(Grobal2.SM_CORPS_EXIT, result);
            if (result != 0) return;
            SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
            SendNativeSocialRoleRefresh();
        }

        private void DirectAddNativeCorpsMember()
        {
            var service = CorpsService;
            var operatorId = GetCachedNativeUserId();
            var result = NativeCorpsService.UnknownError;
            TPlayObject target = null;
            NativeCorpsSnapshot operatorCorps = null;

            if (!service.IsAvailable)
            {
                result = NativeCorpsService.UnknownError;
            }
            else if (!service.TryGetPlayerCorps(operatorId,
                         out operatorCorps))
            {
                result = 5;
            }
            else
            {
                target = ResolveNativeCorpsDirectTarget();
                if (target == null || target.m_boGhost)
                {
                    result = 22;
                }
                else
                {
                    var targetId = target.GetCachedNativeUserId();
                    if (service.TryGetPlayerCorps(targetId, out _))
                    {
                        result = 3;
                    }
                    else if (!target.m_boAllowGuild)
                    {
                        result = 35;
                    }
                    else
                    {
                        result = service.DirectAddMember(operatorId,
                            target.CaptureNativeCorpsActor());
                    }
                }
            }

            if (result == 0)
            {
                target.SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
                if (service.TryGetGildForCorps(operatorCorps.Id, out _))
                    target.SendNativePlayerGuild();
            }
            SendNativeCorpsStatus(Grobal2.SM_CORPS_DIRECT_ADD_MEMBER, result);
        }

        private TPlayObject ResolveNativeCorpsDirectTarget()
        {
            return _nativeCorpsDirectTargetResolver != null
                ? _nativeCorpsDirectTargetResolver()
                : GetPoseCreate() as TPlayObject;
        }

        private void SendNativeSocialRoleRefresh()
        {
            var role = 0;
            var service = CorpsService;
            var playerId = GetCachedNativeUserId();
            if (service.TryGetPlayerCorps(playerId, out var corps))
            {
                role = service.GetPosition(corps, playerId);
                if (role == 0) role = 1;
            }
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                NativeSocialRoleRefreshIdent, 0, 0, role, 0),
                Array.Empty<byte>());
        }

        private void SendNativePlayerCorps(int ident)
        {
            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsStatus(ident,
                    NativeCorpsService.UnknownError);
                return;
            }

            if (!service.TryGetPlayerCorps(GetCachedNativeUserId(),
                    out var corps))
            {
                SendNativeCorpsStatus(ident, 5);
                return;
            }

            SendNativeCorpsPacket(BuildNativeCorpsHeader(ident, 0, 0, 0, 0),
                EncodeNativeCorpsDescription(service, corps,
                    CaptureNativeCorpsOnlineIds()));
        }

        private void SendNativeCorpsList(TProcessMessage processMessage)
        {
            var page = unchecked((ushort)processMessage.nParam2);
            var requested = unchecked((ushort)processMessage.nParam3);
            if (requested > NativeCorpsService.MaximumPageSize) return;

            var service = CorpsService;
            var corps = service.GetCorpsPage(page, requested, out var result);
            var body = EncodeNativeCorpsDescriptions(service, corps);
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                Grobal2.SM_CORPS_LIST, page, result, requested, corps.Count),
                body);
        }

        private void SendNativeCorpsJoinTarget()
        {
            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsStatus(Grobal2.SM_CORPS_QUERY_JOIN,
                    NativeCorpsService.UnknownError);
                return;
            }

            var body = service.TryGetApplicationCorps(GetCachedNativeUserId(),
                out var corps)
                ? EncodeNativeCorpsDescription(service, corps,
                    CaptureNativeCorpsOnlineIds())
                : new byte[NativeCorpsWireCodec.CorpsDescSize];
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                Grobal2.SM_CORPS_QUERY_JOIN, 0, 0, 0, 0), body);
        }

        private void RequestNativeCorpsJoin(TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var corpsId)) return;

            var result = CorpsService.RequestJoin(CaptureNativeCorpsActor(),
                corpsId);
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_CORPS_REQUEST_JOIN, 0, result, 0, 0),
                NativeCorpsWireCodec.EncodeId(corpsId));
        }

        // 4524 CM_CORPS_CREATE (建队) live routing. ADDITIVE + gated on
        // SupportsGildWrites (the corps + gild MySQL stores are co-injected; with
        // no store this keeps the original fail-closed SM_CORPS_CREATE/1000). Wire
        // body = the corps NAME (GBK text, 6-bit-decoded like the other social text
        // bodies via GetNativeCorpsBody). The reversed create ladder (3 already-in-
        // a-corps / 1 invalid name / 2 duplicate name / 0 success) + the two
        // fire-and-forget INSERTs run inside NativeCorpsService.ApplyCorpsCreate.
        // Native (sub_6ADD08) always replies SM_CORPS_CREATE with the result in the
        // last integer (Param); an unreadable/empty name falls through to the
        // manager's own name gate (-> 1), matching native (it copies whatever
        // AnsiString arrived). On success the player's corps view + social role are
        // refreshed, mirroring the native create refresh chain (sub_6F071C + role
        // refresh) and the sibling ExitNativeCorps.
        private void CreateNativeCorps(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendNativeCorpsStatus(Grobal2.SM_CORPS_CREATE,
                    NativeCorpsService.UnknownError);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryDecodeRawText(body, out var name))
                name = string.Empty;
            var result = service.ApplyCorpsCreate(CaptureNativeCorpsActor(),
                name);
            SendNativeCorpsStatus(Grobal2.SM_CORPS_CREATE, result);
            if (result != 0) return;
            SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
            SendNativeSocialRoleRefresh();
        }

        // PAS script entry for This_Player.CreateSelfCorps(name) — the last
        // un-wired member of the corps/gild write family. Native reuses the SAME
        // wrapper sub_6ADD08 for the 4524 CM opcode AND the PAS registration
        // (staging/ida_self_corps_gild_exact_20260720.txt CALLERS: sub_6D7D68 CM +
        // sub_731350 PAS). The wrapper takes the corps NAME as its edx arg (a2 —
        // the script argument here; the wire body for CM) and builds the founder
        // MEMBER record from self via sub_6ADC60/BuildCorpsMemberRecord — so the
        // name is the script arg and the founder is this player. Mirrors
        // CreateNativeCorps (4524) exactly: gate -> ApplyCorpsCreate (which itself
        // reproduces the wrapper's [self+0x0AE8]!=0 -> 3 already-in-a-corps gate,
        // then the manager ladder 1/2/0) -> SendNativeCorpsStatus(SM_CORPS_CREATE)
        // -> on 0 also refresh player corps + social role -> return the native
        // code. ADDITIVE + fail-safe: with NO store (SupportsGildWrites false) it
        // returns false so the PAS bridge stays fail-closed exactly as before (no
        // packet, script sees an unsupported call), never a store-absent regression.
        internal bool TryCreateNativeCorpsFromScript(string name, out int result)
        {
            result = 0;
            var service = CorpsService;
            if (!service.SupportsGildWrites) return false;

            result = service.ApplyCorpsCreate(CaptureNativeCorpsActor(),
                name ?? string.Empty);
            SendNativeCorpsStatus(Grobal2.SM_CORPS_CREATE, result);
            if (result == 0)
            {
                SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
                SendNativeSocialRoleRefresh();
            }
            return true;
        }

        private void SendNativeCorpsMemberList(
            TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var corpsId)) return;

            var requested = unchecked((ushort)processMessage.nParam3);
            if (requested > NativeCorpsService.MaximumPageSize) return;
            var page = unchecked((ushort)processMessage.wParam);
            var listType = processMessage.nParam1;
            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_CORPS_MEMBER_LIST, listType,
                    NativeCorpsService.UnknownError, 0, page),
                    Array.Empty<byte>());
                return;
            }

            var members = service.GetMemberPage(corpsId, page, requested,
                out var result);
            var responseBody = Array.Empty<byte>();
            if (result == 0 && service.TryGetCorps(corpsId, out var corps))
            {
                var online = CaptureNativeCorpsOnlineIds();
                responseBody = NativeCorpsWireCodec.EncodeCorpsMembers(
                    members.Select(member => (member,
                        service.GetPosition(corps, member.MemberId),
                        online.Contains(member.MemberId))).ToArray());
            }
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                Grobal2.SM_CORPS_MEMBER_LIST, listType, result, members.Count,
                page), responseBody);
        }

        private void SetNativeCorpsMemberTitle(
            TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryDecodeMemberTitle(body,
                    out var memberId, out var title))
                return;
            var result = CorpsService.SetMemberTitle(GetCachedNativeUserId(),
                memberId, title);
            SendNativeCorpsStatus(Grobal2.SM_CORPS_SET_MEMBER_TITLE, result);
        }

        private void RunNativeCorpsIdOperation(TProcessMessage processMessage,
            int ident, Func<long, long, int> operation)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var memberId)) return;
            var result = operation(GetCachedNativeUserId(), memberId);
            SendNativeCorpsStatus(ident, result);
            if (result != 0) return;
            SendNativePlayerCorps(Grobal2.SM_PLAYER_CORPS);
            SendNativeSocialRoleRefresh();
        }

        private void SendNativeCorpsRecruitCondition(
            TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var corpsId)) return;

            var service = CorpsService;
            var result = NativeCorpsService.UnknownError;
            var responseBody = Array.Empty<byte>();
            if (service.IsAvailable)
            {
                result = service.TryGetCorps(corpsId, out var corps) ? 0 : 5;
                if (result == 0)
                    responseBody = NativeCorpsWireCodec
                        .EncodeRecruitCondition(corps);
            }
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                Grobal2.SM_CORPS_GET_RECRUIT_CONDITION, 0, result, 0, 0),
                responseBody);
        }

        private void SetNativeCorpsRecruitCondition(
            TProcessMessage processMessage)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryDecodeRecruitCondition(body,
                    out var condition))
                return;
            SendNativeCorpsStatus(Grobal2.SM_CORPS_SET_RECRUIT_CONDITION,
                CorpsService.SetRecruitCondition(GetCachedNativeUserId(),
                    condition));
        }

        private void SendNativeCorpsRequests(TProcessMessage processMessage)
        {
            var page = unchecked((ushort)processMessage.nParam2);
            var requested = unchecked((ushort)processMessage.nParam3);
            if (requested > NativeCorpsService.MaximumPageSize) return;

            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_CORPS_QUERY_REQUESTS, 0,
                    NativeCorpsService.UnknownError, 0, page),
                    Array.Empty<byte>());
                return;
            }
            var requests = service.GetRequestPage(GetCachedNativeUserId(),
                page, requested);
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_CORPS_QUERY_REQUESTS, 0, 0, requests.Count,
                    page),
                NativeCorpsWireCodec.EncodeCorpsRequests(requests));
        }

        private void ProcessNativeCorpsRequests(
            TProcessMessage processMessage, bool accept)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (body.Length < NativeCorpsWireCodec.GuildIdSize) return;
            var count = unchecked((ushort)processMessage.nParam2);
            var required = (long)count * NativeCorpsWireCodec.GuildIdSize;
            if (body.Length < required) return;

            var ident = accept ? Grobal2.SM_CORPS_ACCEPT_REQUEST
                : Grobal2.SM_CORPS_REFUSE_REQUEST;
            var operatorId = GetCachedNativeUserId();
            for (var index = 0; index < count; index++)
            {
                if (!NativeCorpsWireCodec.TryReadId(body, out var memberId,
                        index * NativeCorpsWireCodec.GuildIdSize))
                    return;
                var result = accept
                    ? CorpsService.AcceptRequest(operatorId, memberId)
                    : CorpsService.RefuseRequest(operatorId, memberId);
                SendNativeCorpsPacket(BuildNativeCorpsHeader(
                        ident, 0, result, 0, 0),
                    NativeCorpsWireCodec.EncodeId(memberId));
            }
        }

        private void SendNativeCorpsLogs(TProcessMessage processMessage)
        {
            var type = unchecked((ushort)processMessage.nParam2);
            var page = unchecked((ushort)processMessage.nParam3);
            var requested = unchecked((ushort)processMessage.wParam);
            var entries = CorpsService.GetLogPage(GetCachedNativeUserId(),
                type, page, requested, out var result);
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_CORPS_QUERY_LOG, 0, result, entries.Count, 0),
                NativeCorpsWireCodec.EncodeLogs(entries));
        }

        private void HandleNativeCorpsNotice(TProcessMessage processMessage)
        {
            var mode = unchecked((ushort)processMessage.nParam3);
            var service = CorpsService;
            var result = NativeCorpsService.UnknownError;
            var body = Array.Empty<byte>();
            if (service.IsAvailable)
            {
                if (mode == 0)
                {
                    result = service.TryGetPlayerCorps(GetCachedNativeUserId(),
                        out var corps) ? 0 : 5;
                    if (result == 0)
                        body = (byte[])(corps.Notice
                            ?? Array.Empty<byte>()).Clone();
                }
                else
                {
                    result = service.SetNotice(GetCachedNativeUserId(),
                        GetNativeCorpsBody(processMessage));
                }
            }
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                Grobal2.SM_CORPS_NOTICE, 0, result, mode, 0), body);
        }

        private NativeCorpsActor CaptureNativeCorpsActor()
        {
            return new NativeCorpsActor(GetCachedNativeUserId(), m_sCharName,
                m_Abil?.Level ?? (ushort)0, unchecked((byte)m_btGender),
                m_btJob);
        }

        private byte[] EncodeNativeCorpsDescriptions(
            NativeCorpsService service,
            IReadOnlyList<NativeCorpsSnapshot> corps)
        {
            if (corps.Count == 0) return Array.Empty<byte>();
            var online = CaptureNativeCorpsOnlineIds();
            var body = new byte[checked(corps.Count *
                                        NativeCorpsWireCodec.CorpsDescSize)];
            for (var index = 0; index < corps.Count; index++)
            {
                var record = EncodeNativeCorpsDescription(service,
                    corps[index], online);
                Buffer.BlockCopy(record, 0, body,
                    index * NativeCorpsWireCodec.CorpsDescSize,
                    record.Length);
            }
            return body;
        }

        private static byte[] EncodeNativeCorpsDescription(
            NativeCorpsService service, NativeCorpsSnapshot corps,
            HashSet<long> online)
        {
            var gildName = service.TryGetGildForCorps(corps.Id, out var gild)
                ? gild.Name
                : string.Empty;
            var onlineCount = corps.Members.Count(member =>
                online.Contains(member.MemberId));
            return NativeCorpsWireCodec.EncodeCorpsDescription(corps,
                gildName, service.GetCaptainName(corps), onlineCount);
        }

        private static HashSet<long> CaptureNativeCorpsOnlineIds()
        {
            var result = new HashSet<long>();
            var players = M2Share.UserEngine?.PlayObjects;
            if (players == null) return result;
            foreach (var player in players)
            {
                if (player == null || player.m_boGhost) continue;
                var playerId = player.GetCachedNativeUserId();
                if (playerId != 0) result.Add(playerId);
            }
            return result;
        }

        // corps/gild body reader — routes through the shared native-social 6-bit decoder
        // (DecodeNativeSocialBody, TPlayObject.NativeSocialDecode.cs). GateService delivers
        // Payload = the ENCODED wire body; this returns the DECODED bytes so TryReadId reads
        // a binary int64 @0 and TryDecodeRawText GBK-decodes only the name slice.
        private static byte[] GetNativeCorpsBody(
            TProcessMessage processMessage)
            => DecodeNativeSocialBody(processMessage.Payload);

        internal static ClientPacket BuildNativeCorpsHeader(int ident,
            int recog, int result, int tag, int series)
        {
            return Grobal2.MakeDefaultMsg(ident, recog, result, tag, series);
        }

        private void SendNativeCorpsStatus(int ident, int result)
        {
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                ident, 0, result, 0, 0), Array.Empty<byte>());
        }

        private void SendNativeCorpsPacket(ClientPacket header, byte[] body)
        {
            body ??= Array.Empty<byte>();
            if (_nativeCorpsPacketSink != null)
                _nativeCorpsPacketSink(header, body);
            else
                SendSocket(header, body);
        }
    }
}
