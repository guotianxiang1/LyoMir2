using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private bool TryHandleNativeGuildCoreProtocol(
            TProcessMessage processMessage)
        {
            if (processMessage == null) return false;
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_PLAYER_GILD:
                    SendNativePlayerGuild();
                    return true;
                case Grobal2.CM_GILD_REQUEST_JOIN:
                    HandleNativeGildRequestJoin(processMessage);
                    return true;
                case Grobal2.CM_GILD_LIST:
                    SendNativeGuildList(processMessage);
                    return true;
                case Grobal2.CM_GILD_NOTICE:
                    HandleNativeGuildNotice(processMessage);
                    return true;
                case Grobal2.CM_GILD_CREATE:
                    HandleNativeGildCreate(processMessage);
                    return true;
                case Grobal2.CM_GILD_QUERY_CORPS:
                    SendNativeGuildCorps();
                    return true;
                case Grobal2.CM_GILD_DISMISS_CORPS:
                    HandleNativeGildLeadership(processMessage,
                        Grobal2.SM_GILD_DISMISS_CORPS,
                        NativeGildLeadershipOp.DismissCorps);
                    return true;
                case Grobal2.CM_GILD_TRANSFER_PRESIDENT:
                    HandleNativeGildLeadership(processMessage,
                        Grobal2.SM_GILD_TRANSFER_PRESIDENT,
                        NativeGildLeadershipOp.TransferPresident);
                    return true;
                case Grobal2.CM_GILD_APPOINT_VICE_PRESIDENT:
                    HandleNativeGildLeadership(processMessage,
                        Grobal2.SM_GILD_APPOINT_VICE_PRESIDENT,
                        NativeGildLeadershipOp.AppointVice);
                    return true;
                default:
                    return false;
            }
        }

        private void SendNativePlayerGuild()
        {
            var service = CorpsService;
            if (!service.IsAvailable)
            {
                SendNativeCorpsStatus(Grobal2.SM_PLAYER_GILD,
                    NativeCorpsService.UnknownError);
                return;
            }

            var playerId = GetCachedNativeUserId();
            if (!service.TryGetPlayerCorps(playerId, out _))
            {
                SendNativeCorpsStatus(Grobal2.SM_PLAYER_GILD, 5);
                return;
            }
            if (!service.TryGetGildForPlayer(playerId, out var gild))
            {
                SendNativeCorpsStatus(Grobal2.SM_PLAYER_GILD, 12);
                return;
            }

            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_PLAYER_GILD, 0, 0, 0, 0),
                EncodeNativeGuildDescription(service, gild,
                    CaptureNativeCorpsOnlineIds()));
        }

        private void SendNativeGuildList(TProcessMessage processMessage)
        {
            var page = unchecked((ushort)processMessage.nParam2);
            var requested = unchecked((ushort)processMessage.nParam3);
            if (requested > NativeCorpsService.MaximumPageSize) return;

            var service = CorpsService;
            var result = 0;
            IReadOnlyList<NativeGildSnapshot> pageItems =
                Array.Empty<NativeGildSnapshot>();
            if (!service.IsAvailable)
            {
                result = NativeCorpsService.UnknownError;
            }
            else
            {
                var gilds = service.SnapshotGilds();
                var start = (long)page * requested;
                if (start >= gilds.Count)
                {
                    if (gilds.Count != 0 || page != 0) result = 30;
                }
                else
                {
                    pageItems = gilds.Skip(unchecked((int)start))
                        .Take(requested).ToArray();
                }
            }

            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_GILD_LIST, page, result, requested,
                    pageItems.Count),
                EncodeNativeGuildDescriptions(service, pageItems));
        }

        private void HandleNativeGuildNotice(TProcessMessage processMessage)
        {
            var mode = unchecked((ushort)processMessage.nParam3);
            var service = CorpsService;
            var result = NativeCorpsService.UnknownError;
            var body = Array.Empty<byte>();
            if (service.IsAvailable && mode == 0)
            {
                var playerId = GetCachedNativeUserId();
                if (!service.TryGetGildForPlayer(playerId,
                             out var gild))
                {
                    result = 12;
                }
                else
                {
                    result = 0;
                    body = (byte[])gild.Notice.Clone();
                }
            }
            else if (service.IsAvailable && mode != 0)
            {
                var playerId = GetCachedNativeUserId();
                result = service.SetGildNotice(playerId,
                    GetNativeCorpsBody(processMessage));
            }

            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                Grobal2.SM_GILD_NOTICE, 0, result, mode, 0), body);
        }

        private void SendNativeGuildCorps()
        {
            var service = CorpsService;
            var result = NativeCorpsService.UnknownError;
            var corps = new List<NativeCorpsSnapshot>();
            if (service.IsAvailable)
            {
                var playerId = GetCachedNativeUserId();
                if (!service.TryGetPlayerCorps(playerId, out _))
                {
                    result = 5;
                }
                else if (!service.TryGetGildForPlayer(playerId,
                             out var gild))
                {
                    result = 12;
                }
                else
                {
                    result = 0;
                    foreach (var corpsId in gild.CorpsIds)
                    {
                        if (service.TryGetCorps(corpsId,
                                out var corpsItem))
                            corps.Add(corpsItem);
                    }
                }
            }

            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_GILD_QUERY_CORPS, 0, result, corps.Count, 0),
                EncodeNativeCorpsDescriptions(service, corps));
        }

        private void SendUnsupportedNativeGuildIdOperation(
            TProcessMessage processMessage, int ident, bool echoId)
        {
            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var id)) return;

            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    ident, 0, NativeCorpsService.UnknownError, 0, 0),
                echoId ? NativeCorpsWireCodec.EncodeId(id)
                    : Array.Empty<byte>());
        }

        // 4560 CM_GILD_REQUEST_JOIN live routing. ADDITIVE + gated on SupportsGildWrites: with NO gild
        // store the branch reproduces the exact original SendUnsupportedNativeGuildIdOperation (echoes the
        // target id, Param = UnknownError 1000). With a store the reversed
        // NativeCorpsService.ApplyGildRequestJoin creates the in-memory pending join request (ledger only —
        // pending requests are runtime-only, never persisted) and the real result code
        // (12/555/6/8/0) is echoed back with the target id, matching native sub_6F5958's buffered reply.
        // Wire body = the target GILD id.
        private void HandleNativeGildRequestJoin(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildIdOperation(processMessage,
                    Grobal2.SM_GILD_REQUEST_JOIN, true);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var targetGildId))
                return;
            SendNativeCorpsPacket(BuildNativeCorpsHeader(
                    Grobal2.SM_GILD_REQUEST_JOIN, 0,
                    service.ApplyGildRequestJoin(GetCachedNativeUserId(),
                        targetGildId), 0, 0),
                NativeCorpsWireCodec.EncodeId(targetGildId));
        }

        // 4567/4568/4569 Gild leadership (president-only) live routing.
        // ADDITIVE + FAIL-SAFE: replaces only the write-op short-circuit, and
        // only when a Gild write store is configured. With NO store the branch
        // reproduces the exact original fail-closed response
        // (SendUnsupportedNativeGuildIdOperation -> header Param =
        // UnknownError(1000), empty body); that 1000 is the genuine "no
        // Gild-write store configured" result, so NativeCorpsProtocolCheck's
        // dormant-ABI assertion legitimately still holds. With a store the
        // reversed NativeGildLeadershipTransaction classifies the live state
        // and the real result code is returned; the in-memory mutation +
        // INativeGildStore write happen inside ApplyGildLeadership (fail-safe,
        // no rollback). The wire body carries the target CORPS id.
        private void HandleNativeGildLeadership(TProcessMessage processMessage,
            int ident, NativeGildLeadershipOp op)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendUnsupportedNativeGuildIdOperation(processMessage, ident,
                    false);
                return;
            }

            var body = GetNativeCorpsBody(processMessage);
            if (!NativeCorpsWireCodec.TryReadId(body, out var targetCorpsId))
                return;
            var result = service.ApplyGildLeadership(op,
                GetCachedNativeUserId(), targetCorpsId);
            if (result == NativeGildLeadershipTransaction.Success)
                NotifyNativeGildLeadershipChanged(service, op, targetCorpsId);
            SendNativeCorpsStatus(ident, result);
        }

        // sub_706BB4 @0x706BBF mov dx,0x68 (SM_GUILDMESSAGE) + caller mov cx,0xFFD4
        // @0x703BDA/0x704860/0x704DEA — fan-out sub_706D14 skips sender @0x706DF4.
        private const int NativeGildBroadcastColor = 0xFFD4;

        // GBK literals verified via sfind/dstr on flat_image.bin (ImageBase 0x400000).
        private const string NativeGildAppointViceMiddle = "战队被任命为";   // 0x703C30
        private const string NativeGildAppointViceSuffix = "行会副会长";     // 0x703C48
        private const string NativeGildTransferMiddle = "行会的所有权被移交到了"; // 0x704904
        private const string NativeGildTransferSuffix = "战队首领";           // 0x704924
        private const string NativeGildViceStepDownSuffix = "战队卸任了副会长"; // 0x704E40
        private const string NativeGildDismissViceSuffix = "战队被免除副会长职务"; // 0x704424

        // 4568/4569 success tails: sub_7039F8 @0x703B4B/0x703B63 call sub_6F5B0C,
        // @0x703B6A call sub_6AEE04; sub_7046A8 @0x70489C call sub_6AEE04 then
        // sub_706BB4 @0x704866. Wired after ApplyGildLeadership Success (result 0).
        private void NotifyNativeGildLeadershipChanged(
            NativeCorpsService service, NativeGildLeadershipOp op,
            long targetCorpsId)
        {
            if (!service.TryGetGildForPlayer(GetCachedNativeUserId(),
                    out var gild))
                return;
            if (!service.TryGetCorps(targetCorpsId, out var targetCorps))
                return;

            switch (op)
            {
                case NativeGildLeadershipOp.AppointVice:
                    // 0x703B4B: operator guild snapshot; 0x703B63/0x703B6A: target captain.
                    SendNativePlayerGuild();
                    TrySendNativePlayerGuildToCaptain(service, targetCorps);
                    TrySendNativeSocialRoleRefreshToCaptain(targetCorps);
                    BroadcastNativeGildMessage(service, gild,
                        targetCorps.Name + NativeGildAppointViceMiddle
                            + gild.Name + NativeGildAppointViceSuffix);
                    break;
                case NativeGildLeadershipOp.TransferPresident:
                    // 0x70489C: new president captain social-role refresh (SM 4628).
                    TrySendNativeSocialRoleRefreshToCaptain(targetCorps);
                    TrySendNativePlayerGuildToCaptain(service, targetCorps);
                    BroadcastNativeGildMessage(service, gild,
                        gild.Name + NativeGildTransferMiddle
                            + targetCorps.Name + NativeGildTransferSuffix);
                    break;
            }
        }

        // 4587 sub_704CC0 @0x704D9B call sub_6AEE04 + @0x704DF0 call sub_706BB4.
        internal void NotifyNativeGildViceStepDown(NativeCorpsService service)
        {
            if (!service.TryGetPlayerCorps(GetCachedNativeUserId(),
                    out var callerCorps))
                return;
            if (!service.TryGetGildForPlayer(GetCachedNativeUserId(),
                    out var gild))
                return;
            SendNativeSocialRoleRefresh();
            BroadcastNativeGildMessage(service, gild,
                callerCorps.Name + NativeGildViceStepDownSuffix);
        }

        // 4588 sub_704228 @0x704342 call sub_6AEE04 + @0x7043CD call sub_706BB4.
        internal void NotifyNativeGildDismissVice(NativeCorpsService service,
            long targetCorpsId)
        {
            if (!service.TryGetGildForPlayer(GetCachedNativeUserId(),
                    out var gild))
                return;
            if (!service.TryGetCorps(targetCorpsId, out var targetCorps))
                return;
            TrySendNativeSocialRoleRefreshToCaptain(targetCorps);
            BroadcastNativeGildMessage(service, gild,
                targetCorps.Name + NativeGildDismissViceSuffix);
        }

        // Captain = the corps member whose MemberId == corps.OwnerId (same
        // resolution as NativeCorpsService owner-name lookup); then map to an
        // online object via UserEngine.GetPlayObject(name).
        private static TPlayObject ResolveOnlineCorpsCaptain(
            NativeCorpsSnapshot corps)
        {
            var userEngine = M2Share.UserEngine;
            if (userEngine == null || corps?.Members == null)
                return null;
            for (var i = 0; i < corps.Members.Count; i++)
            {
                var member = corps.Members[i];
                if (member == null || member.MemberId != corps.OwnerId
                    || string.IsNullOrEmpty(member.Name))
                    continue;
                var player = userEngine.GetPlayObject(member.Name);
                return (player == null || player.m_boGhost) ? null : player;
            }
            return null;
        }

        private static void TrySendNativePlayerGuildToCaptain(
            NativeCorpsService service, NativeCorpsSnapshot corps)
        {
            ResolveOnlineCorpsCaptain(corps)?.SendNativePlayerGuild();
        }

        private static void TrySendNativeSocialRoleRefreshToCaptain(
            NativeCorpsSnapshot corps)
        {
            ResolveOnlineCorpsCaptain(corps)?.SendNativeSocialRoleRefresh();
        }

        private void BroadcastNativeGildMessage(NativeCorpsService service,
            NativeGildSnapshot gild, string text)
        {
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return;
            foreach (var corpsId in gild.CorpsIds)
            {
                if (!service.TryGetCorps(corpsId, out var corps)) continue;
                for (var i = 0; i < corps.Members.Count; i++)
                {
                    var member = corps.Members[i];
                    if (member == null
                        || string.IsNullOrEmpty(member.Name))
                        continue;
                    var player = userEngine.GetPlayObject(member.Name);
                    if (player == null || player.m_boGhost) continue;
                    if (ReferenceEquals(player, this)) continue;
                    player.SendDefMessage(Grobal2.SM_GUILDMESSAGE, 0,
                        NativeGildBroadcastColor, 0, 0, text);
                }
            }
        }

        // 4564 create-gild live routing. ADDITIVE + gated on SupportsGildWrites +
        // fail-safe. Wire body = the new gild NAME (GBK). Routed through
        // guild-store's reversed NativeGildCreateContract (ladder 555/4/5/6/2/0;
        // no gold gate, no name-validity gate — only dup-name) into
        // NativeCorpsService.ApplyGildCreate, which allocates the composite
        // GildID, publishes the in-memory gild + registry, and pushes INSERT
        // gildmember THEN INSERT Gild to INativeGildStore fail-safe (no rollback).
        // With NO store the branch keeps the exact original fail-closed response
        // (SM_GILD_CREATE, UnknownError=1000) — so NativeCorpsProtocolCheck's
        // no-store 4564->1000 ABI assertion still holds unchanged. The create
        // ladder has NO name-validity gate, so the raw GBK name is passed as-is
        // (empty/undecodable -> empty), matching the reversed contract.
        private void HandleNativeGildCreate(TProcessMessage processMessage)
        {
            var service = CorpsService;
            if (!service.SupportsGildWrites)
            {
                SendNativeCorpsStatus(Grobal2.SM_GILD_CREATE,
                    NativeCorpsService.UnknownError);
                return;
            }

            NativeCorpsWireCodec.TryDecodeRawText(
                GetNativeCorpsBody(processMessage), out var name);
            var result = service.ApplyGildCreate(GetCachedNativeUserId(),
                name ?? string.Empty);
            // Native sub_702F8C (the +0x3C create strategy) sends the player-gild
            // snapshot (sub_6F07CC -> SM_PLAYER_GILD 4500) then the social-role
            // refresh (sub_6AEE04 -> 4628) from INSIDE the strategy — i.e. BEFORE
            // the wrapper sub_6ADDA8 emits SM_GILD_CREATE (0x6ADDE2, dx=0x11D4).
            // Both fire on the AddGild-reached path: result 0 (success) OR 2
            // (duplicate name); the 4/5/6/555 pre-gates return before any refresh.
            // The success-only broadcast (sub_76C4C0/sub_7209EC/sub_706BB4) is
            // inter-server registration, not a creator-facing packet, so — like
            // the corps sibling CreateNativeCorps — it is not replicated. Order
            // verified by disassembly
            // (staging/ida_gild_create_refresh_disasm_20260803.txt) and the
            // dormant exact-state model (send:player-gild -> refresh -> status;
            // audited by NativeSelfCorpsGildExactStateCheck).
            if (result == 0 || result == 2)
            {
                SendNativePlayerGuild();
                SendNativeSocialRoleRefresh();
            }
            SendNativeCorpsStatus(Grobal2.SM_GILD_CREATE, result);
        }

        // PAS script entry for This_Player.CreateSelfGild(name) — the last
        // un-wired member of the corps/gild write family. Native reuses the SAME
        // wrapper sub_6ADDA8 for the 4564 CM opcode AND the PAS registration
        // (staging/ida_self_corps_gild_exact_20260720.txt + gild_create_4564:
        // CALLERS sub_6D7D68 CM + sub_731350 PAS). The wrapper takes the gild NAME
        // as its edx arg (a2 — the script argument here; the wire body for CM) and
        // resolves the FOUNDER from the player identity key [self+0x588]/[self+0x58C]
        // (strategy vtable[0x3C]=sub_702F8C -> sub_656C14) — so the name is the
        // script arg and the founder is this player. Mirrors HandleNativeGildCreate
        // (4564) exactly: gate -> ApplyGildCreate (reversed contract 555/4/5/6/2/0;
        // no gold gate, no name-validity gate, only dup-name) ->
        // SendNativeCorpsStatus(SM_GILD_CREATE) -> return the native code. ADDITIVE
        // + fail-safe: with NO store it returns false so the PAS bridge stays
        // fail-closed exactly as before, never a store-absent regression.
        internal bool TryCreateNativeGildFromScript(string name, out int result)
        {
            result = 0;
            var service = CorpsService;
            if (!service.SupportsGildWrites) return false;

            result = service.ApplyGildCreate(GetCachedNativeUserId(),
                name ?? string.Empty);
            // Native reuses the SAME wrapper (sub_6ADDA8) for the 4564 CM opcode
            // and the PAS registration, so the +0x3C create strategy emits the
            // player-gild snapshot (4500) + social-role refresh (4628) before the
            // SM_GILD_CREATE status on the AddGild-reached path (result 0 or 2).
            // Mirror HandleNativeGildCreate exactly.
            if (result == 0 || result == 2)
            {
                SendNativePlayerGuild();
                SendNativeSocialRoleRefresh();
            }
            SendNativeCorpsStatus(Grobal2.SM_GILD_CREATE, result);
            return true;
        }

        private static byte[] EncodeNativeGuildDescriptions(
            NativeCorpsService service,
            IReadOnlyList<NativeGildSnapshot> gilds)
        {
            if (gilds.Count == 0) return Array.Empty<byte>();
            var online = CaptureNativeCorpsOnlineIds();
            var body = new byte[checked(gilds.Count *
                                        NativeCorpsWireCodec.GuildDescSize)];
            for (var index = 0; index < gilds.Count; index++)
            {
                var record = EncodeNativeGuildDescription(service,
                    gilds[index], online);
                Buffer.BlockCopy(record, 0, body,
                    index * NativeCorpsWireCodec.GuildDescSize,
                    record.Length);
            }
            return body;
        }

        private static byte[] EncodeNativeGuildDescription(
            NativeCorpsService service, NativeGildSnapshot gild,
            HashSet<long> online)
        {
            var presidentName = string.Empty;
            var playerCount = 0;
            var onlineCount = 0;
            foreach (var corpsId in gild.CorpsIds)
            {
                if (!service.TryGetCorps(corpsId, out var corps)) continue;
                if (corpsId == gild.OwnerCorpsId)
                    presidentName = service.GetCaptainName(corps);
                playerCount += corps.Members.Count;
                onlineCount += corps.Members.Count(member =>
                    online.Contains(member.MemberId));
            }

            return NativeCorpsWireCodec.EncodeGuildDescription(gild,
                presidentName, playerCount, onlineCount);
        }
    }
}
