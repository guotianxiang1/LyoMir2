using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private bool TryHandleNativeChannelProtocol(
            TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_CHANNEL_CREATE:
                    HandleNativeChannelCreate(processMessage);
                    return true;
                case Grobal2.CM_CHANNEL_ENTER:
                    HandleNativeChannelEnter(processMessage);
                    return true;
                case Grobal2.CM_CHANNEL_EXIT:
                    SendNativeChannelResult(Grobal2.SM_CHANNEL_EXIT,
                        NativeChannelManager.Shared.Exit(
                            NativeChannelActor.FromPlayer(this)));
                    return true;
                case Grobal2.CM_CHANNEL_CHANGE_MODE:
                    SendNativeChannelResult(Grobal2.SM_CHANNEL_CHANGE_MODE,
                        NativeChannelManager.Shared.ChangeMode(
                            NativeChannelActor.FromPlayer(this),
                            processMessage.wParam,
                            unchecked((byte)processMessage.nParam1)));
                    return true;
                case Grobal2.CM_CHANNEL_CHANGE_MUTE:
                    HandleNativeChannelMute(processMessage);
                    return true;
                case Grobal2.CM_CHANNEL_KICK_OUT:
                    HandleNativeChannelKick(processMessage);
                    return true;
                case Grobal2.CM_QUERY_CHANNEL_LIST:
                    HandleNativeChannelList();
                    return true;
                case Grobal2.CM_QUERY_CHANNEL_MEMBERS:
                    HandleNativeChannelMembers(processMessage);
                    return true;
                default:
                    return false;
            }
        }

        private void HandleNativeChannelCreate(TProcessMessage processMessage)
        {
            // Channel body is 6-bit-ENCODED on the wire (non-raw ident): decode
            // before the codec reads it, else the raw Payload = garbage.
            var payload = DecodeNativeSocialBody(processMessage.Payload);
            if (!NativeChannelWireCodec.TryDecodeCreatePayload(payload,
                    out var request, out var errorCode))
            {
                SendNativeChannelResult(Grobal2.SM_CHANNEL_CREATE, errorCode);
                return;
            }

            var actor = NativeChannelActor.FromPlayer(this);
            var create = NativeChannelManager.Shared.CreatePublic(actor, request);
            NativeChannelEnterResult enter = null;
            if (create.Code == 0)
            {
                enter = NativeChannelManager.Shared.Enter(actor,
                    create.ChannelId, request.Password);
                if (enter.Code == 0)
                    SendNativeChannelMembersById(enter.ChannelId, 1,
                        enter.Type);
            }

            SendNativeChannelResult(Grobal2.SM_CHANNEL_CREATE, create.Code);
            if (create.Code == 0)
            {
                SendNativeChannelResult(Grobal2.SM_CHANNEL_ENTER,
                    enter?.Code ?? -99);
            }
        }

        private void HandleNativeChannelEnter(TProcessMessage processMessage)
        {
            var actor = NativeChannelActor.FromPlayer(this);
            var requestedType = unchecked((byte)processMessage.nParam1);
            NativeChannelEnterResult enter;

            switch (requestedType)
            {
                case 0:
                    enter = NativeChannelManager.Shared.Enter(actor,
                        processMessage.wParam, 0);
                    break;
                case 1:
                    var password = NativeChannelWireCodec.ParseInt64OrDefault(
                        DecodeNativeSocialBody(processMessage.Payload), -1);
                    enter = NativeChannelManager.Shared.Enter(actor,
                        processMessage.wParam, password);
                    break;
                case 2:
                case 3:
                case 4:
                    if (!NativeChannelManager.Shared.TryResolveMembership(actor,
                            requestedType, out var membership))
                    {
                        SendNativeChannelResult(Grobal2.SM_CHANNEL_ENTER, -9);
                        return;
                    }

                    var scoped = NativeChannelManager.Shared.EnterScoped(actor,
                        requestedType, membership);
                    if (scoped.CreateAttempted)
                    {
                        SendNativeChannelResult(Grobal2.SM_CHANNEL_CREATE,
                            scoped.CreateCode);
                    }
                    enter = scoped.Enter;
                    break;
                default:
                    SendNativeChannelResult(Grobal2.SM_CHANNEL_ENTER, -99);
                    return;
            }

            if (enter.Code == 0)
                SendNativeChannelMembersById(enter.ChannelId, 1, enter.Type);
            SendNativeChannelResult(Grobal2.SM_CHANNEL_ENTER, enter.Code);
        }

        private void HandleNativeChannelMute(TProcessMessage processMessage)
        {
            if (processMessage.nParam1 != 0 && processMessage.nParam1 != 1)
                return;

            var target = ResolveNativeChannelTarget(
                DecodeNativeSocialBody(processMessage.Payload));
            if (target == null)
            {
                SendNativeChannelResult(Grobal2.SM_CHANNEL_CHANGE_MUTE, -27);
                return;
            }

            var result = NativeChannelManager.Shared.ChangeMute(
                NativeChannelActor.FromPlayer(this), processMessage.wParam,
                target, processMessage.nParam1 == 1);
            SendNativeChannelResult(Grobal2.SM_CHANNEL_CHANGE_MUTE, result);
        }

        private void HandleNativeChannelKick(TProcessMessage processMessage)
        {
            var target = ResolveNativeChannelTarget(
                DecodeNativeSocialBody(processMessage.Payload));
            if (target == null)
            {
                SendNativeChannelResult(Grobal2.SM_CHANNEL_KICK_OUT, -27);
                return;
            }

            var result = NativeChannelManager.Shared.Kick(
                NativeChannelActor.FromPlayer(this), processMessage.wParam,
                target);
            SendNativeChannelResult(Grobal2.SM_CHANNEL_KICK_OUT, result);
        }

        private void HandleNativeChannelList()
        {
            if (m_boGhost) return;
            var channels = NativeChannelManager.Shared.GetPublicChannels();
            var payload = NativeChannelWireCodec.EncodeChannelList(channels);
            SendNativeChannelPacket(Grobal2.SM_SEND_CHANNEL_LIST, 0,
                payload.Length, 0, channels.Count, payload);
        }

        private void HandleNativeChannelMembers(TProcessMessage processMessage)
        {
            if (m_boGhost) return;
            var requestedType = unchecked((byte)processMessage.nParam1);
            NativeChannelQueryResult query;

            if (requestedType < 2)
            {
                query = NativeChannelManager.Shared.QueryById(
                    processMessage.wParam);
            }
            else if (requestedType <= 4)
            {
                var actor = NativeChannelActor.FromPlayer(this);
                if (!NativeChannelManager.Shared.TryResolveMembership(actor,
                        requestedType, out var membership))
                {
                    SendNativeChannelPacket(
                        Grobal2.SM_SEND_CHANNEL_MEMBERS, -28, 0, 0,
                        requestedType, Array.Empty<byte>());
                    return;
                }
                query = NativeChannelManager.Shared.QueryScoped(requestedType,
                    membership);
            }
            else
            {
                query = new NativeChannelQueryResult(-29, null);
            }

            SendNativeChannelMembers(query, 0, requestedType);
        }

        private void SendNativeChannelMembersById(int channelId, int tag,
            byte fallbackType)
        {
            SendNativeChannelMembers(
                NativeChannelManager.Shared.QueryById(channelId), tag,
                fallbackType);
        }

        private void SendNativeChannelMembers(NativeChannelQueryResult query,
            int tag, byte fallbackType)
        {
            if (query.Code != 0 || query.Snapshot == null)
            {
                SendNativeChannelPacket(Grobal2.SM_SEND_CHANNEL_MEMBERS,
                    query.Code == 0 ? -29 : query.Code, 0, tag,
                    fallbackType, Array.Empty<byte>());
                return;
            }

            var payload = NativeChannelWireCodec.EncodeMembers(query.Snapshot,
                out var onlineMemberCount);
            var series = NativeChannelWireCodec.BuildMembersSeries(
                query.Snapshot.Type, onlineMemberCount);
            SendNativeChannelPacket(Grobal2.SM_SEND_CHANNEL_MEMBERS, 0,
                payload.Length, tag, series, payload);
        }

        private static NativeChannelActor ResolveNativeChannelTarget(
            byte[] payload)
        {
            var targetName = NativeChannelWireCodec.DecodeText(payload);
            var target = M2Share.UserEngine?.GetPlayObject(targetName);
            return target == null || target.m_boGhost
                ? null
                : NativeChannelActor.FromPlayer(target);
        }

        private void SendNativeChannelResult(int ident, int code)
        {
            SendNativeChannelPacket(ident, code, 0, 0, 0,
                Array.Empty<byte>());
        }

        private void SendNativeChannelPacket(int ident, int recog, int param,
            int tag, int series, byte[] payload)
        {
            var header = Grobal2.MakeDefaultMsg(ident, recog, param, tag,
                series);
            SendSocket(header, payload ?? Array.Empty<byte>());
        }
    }
}
