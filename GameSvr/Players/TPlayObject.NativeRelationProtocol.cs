using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private NativeRelationService _nativeRelationService;
        private Action<ClientPacket, byte[]> _nativeRelationPacketSink;

        private NativeRelationService RelationService =>
            _nativeRelationService ??= new NativeRelationService(
                new NativeRelationMySqlStore(
                    () => M2Share.g_Config?.sConnctionString));

        internal void SetNativeRelationServiceForTests(
            NativeRelationService service,
            Action<ClientPacket, byte[]> packetSink = null)
        {
            _nativeRelationService = service ??
                                     throw new ArgumentNullException(
                                         nameof(service));
            _nativeRelationPacketSink = packetSink;
        }

        private bool TryHandleNativeRelationProtocol(
            TProcessMessage processMessage)
        {
            if (processMessage == null) return false;
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_QUERY_RELATION_FRIEND:
                    SendNativeRelationList(NativeRelationKind.Friend,
                        Grobal2.SM_SEND_RELATION_FRIEND);
                    return true;
                case Grobal2.CM_QUERY_RELATION_ATTENTION:
                    SendNativeRelationList(NativeRelationKind.Attention,
                        Grobal2.SM_SEND_RELATION_ATTENTION);
                    return true;
                case Grobal2.CM_QUERY_RELATION_NORMBLACKLIST:
                    SendNativeRelationList(NativeRelationKind.Blacklist,
                        Grobal2.SM_SEND_RELATION_NORMBLACKLIST);
                    return true;
                case Grobal2.CM_ADD_RELATION_FRIEND:
                    RequestNativeFriend(processMessage.Payload);
                    return true;
                case Grobal2.CM_ADD_RELATION_ATTENTION:
                    AddNativeDirectedRelation(processMessage.Payload,
                        NativeRelationKind.Attention,
                        Grobal2.SM_ADD_RELATION_ATTENTION);
                    return true;
                case Grobal2.CM_ADD_RELATION_NORMBLACKLIST:
                    AddNativeDirectedRelation(processMessage.Payload,
                        NativeRelationKind.Blacklist,
                        Grobal2.SM_ADD_RELATION_NORMBLACKLIST);
                    return true;
                case Grobal2.CM_DEL_RELATION_FRIEND:
                    DeleteNativeRelation(processMessage.Payload,
                        NativeRelationKind.Friend,
                        Grobal2.SM_DEL_RELATION_FRIEND);
                    return true;
                case Grobal2.CM_DEL_RELATION_ATTENTION:
                    DeleteNativeRelation(processMessage.Payload,
                        NativeRelationKind.Attention,
                        Grobal2.SM_DEL_RELATION_ATTENTION);
                    return true;
                case Grobal2.CM_DEL_RELATION_NORMBLACKLIST:
                    DeleteNativeRelation(processMessage.Payload,
                        NativeRelationKind.Blacklist,
                        Grobal2.SM_DEL_RELATION_NORMBLACKLIST);
                    return true;
                case Grobal2.CM_UPDATE_ATTENTION_COLOR:
                    UpdateNativeAttentionColor(processMessage.Payload,
                        (byte)processMessage.nParam2);
                    return true;
                default:
                    return false;
            }
        }

        private void SendNativeRelationList(NativeRelationKind kind,
            int opcode)
        {
            if (!RelationService.TryQuery(GetCachedNativeUserId(), kind,
                    out var entries) || entries == null)
                entries = Array.Empty<NativeRelationEntry>();

            var wireEntries = new List<NativeRelationWireEntry>(entries.Count);
            foreach (var entry in entries)
            {
                var online = M2Share.UserEngine?.GetPlayObject(entry.Name);
                wireEntries.Add(online == null
                    ? new NativeRelationWireEntry(entry.Name, entry.Level,
                        entry.Job, entry.FocusColor, string.Empty, false)
                    : new NativeRelationWireEntry(online.m_sCharName,
                        online.m_Abil?.Level ?? entry.Level, online.m_btJob,
                        entry.FocusColor,
                        online.m_MyGuild?.sGuildName ?? string.Empty, true));
            }

            var body = NativeRelationWireCodec.Encode(kind, wireEntries);
            var header = Grobal2.MakeDefaultMsg(opcode, 0, 0, 0,
                wireEntries.Count);
            SendNativeRelationPacket(header, body);
        }

        private void RequestNativeFriend(object payload)
        {
            if (!NativeRelationWireCodec.TryDecodeName(
                    DecodeNativeSocialBody(payload),
                    out var targetName))
                return;

            var target = M2Share.UserEngine?.GetPlayObject(targetName);
            var result = target == null
                ? -1
                : ReferenceEquals(this, target)
                    ? -2
                    : RelationService.CheckFriendRequest(
                        CaptureNativeRelationPlayer(),
                        target.CaptureNativeRelationPlayer());
            if (result != 0)
            {
                SendNativeRelationStatus(
                    Grobal2.SM_ADD_RELATION_FRIEND_FAIL, result);
                return;
            }

            _ = target.QueueNativeGroupRequest(this, 2);
        }

        private void AddNativeDirectedRelation(object payload,
            NativeRelationKind kind, int opcode)
        {
            if (!NativeRelationWireCodec.TryDecodeName(
                    DecodeNativeSocialBody(payload),
                    out var targetName))
                return;

            int result;
            if (targetName.Length == 0)
            {
                result = -1;
            }
            else
            {
                var target = M2Share.UserEngine?.GetPlayObject(targetName);
                if (target == null)
                    result = -2;
                else if (ReferenceEquals(this, target))
                    result = -3;
                else
                    result = RelationService.AddDirected(
                        CaptureNativeRelationPlayer(),
                        target.CaptureNativeRelationPlayer(), kind);
            }

            if (result != NativeRelationService.NoResponse)
                SendNativeRelationStatus(opcode, result);
        }

        private void DeleteNativeRelation(object payload,
            NativeRelationKind kind, int opcode)
        {
            if (!NativeRelationWireCodec.TryDecodeName(
                    DecodeNativeSocialBody(payload),
                    out var targetName))
                return;

            var result = kind == NativeRelationKind.Friend
                         && string.Equals(targetName, m_sCharName,
                             StringComparison.OrdinalIgnoreCase)
                ? -1
                : RelationService.Remove(GetCachedNativeUserId(), targetName,
                    kind);
            if (result != NativeRelationService.NoResponse)
                SendNativeRelationStatus(opcode, result);
        }

        private void UpdateNativeAttentionColor(object payload, byte color)
        {
            if (!NativeRelationWireCodec.TryDecodeName(
                    DecodeNativeSocialBody(payload),
                    out var targetName))
                return;

            var result = RelationService.UpdateAttentionColor(
                GetCachedNativeUserId(), targetName, color);
            SendNativeRelationStatus(Grobal2.SM_UPDATE_ATTENTION_COLOR, result);
        }

        internal int AcceptNativeFriend(TPlayObject accepter)
        {
            if (accepter == null) return -5;
            var result = RelationService.AcceptFriend(
                CaptureNativeRelationPlayer(),
                accepter.CaptureNativeRelationPlayer());
            if (result != 0)
            {
                accepter.SendNativeRelationStatus(
                    Grobal2.SM_ADD_RELATION_FRIEND_FAIL, result);
                return result;
            }

            SendNativeRelationPacket(Grobal2.MakeDefaultMsg(
                    Grobal2.SM_ADD_RELATION_FRIEND_OK, 0, 0, 0, 0),
                NativeRelationWireCodec.EncodeName(accepter.m_sCharName));
            accepter.SendNativeRelationStatus(
                Grobal2.SM_ADD_RELATION_FRIEND_OK, 0);
            return 0;
        }

        private NativeRelationPlayer CaptureNativeRelationPlayer()
        {
            return new NativeRelationPlayer(GetCachedNativeUserId(),
                m_sCharName, m_Abil?.Level ?? (ushort)0, m_btJob);
        }

        private void SendNativeRelationStatus(int opcode, int result)
        {
            SendNativeRelationPacket(
                Grobal2.MakeDefaultMsg(opcode, result, 0, 0, 0),
                Array.Empty<byte>());
        }

        private void SendNativeRelationPacket(ClientPacket header, byte[] body)
        {
            if (_nativeRelationPacketSink != null)
            {
                _nativeRelationPacketSink(header, body ?? Array.Empty<byte>());
                return;
            }
            SendSocket(header, body ?? Array.Empty<byte>());
        }
    }
}
