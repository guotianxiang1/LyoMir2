using System.Runtime.CompilerServices;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeGroupMaxMembers = 11;
        private const int NativeGroupMaxPendingRequests = 10;
        private const int NativeGroupPlayerRecordSize = 36;
        private const int NativeGroupMemberRecordSize = 54;

        private readonly struct NativeGroupRequest
        {
            public NativeGroupRequest(TPlayObject requester, byte type)
            {
                Requester = requester;
                Type = type;
                CreatedTick = HUtil32.GetTickCount();
            }

            public TPlayObject Requester { get; }
            public byte Type { get; }
            public int CreatedTick { get; }
        }

        private static readonly ConditionalWeakTable<TPlayObject,
            List<NativeGroupRequest>> NativeGroupPendingRequests = new();

        private bool TryHandleNativeGroupProtocol(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_REPLY_GROUP_MESSAGE:
                    HandleNativeGroupReply(processMessage);
                    return true;
                case Grobal2.CM_JOINGROUP:
                    HandleNativeJoinGroup(processMessage);
                    return true;
                case Grobal2.CM_QUERY_NEARBYPLAYER:
                    HandleNativeNearbyPlayerQuery(processMessage);
                    return true;
                case Grobal2.CM_QUERY_NEARBYGROUP:
                    HandleNativeNearbyGroupQuery();
                    return true;
                case Grobal2.CM_QUERY_GROUP_MEMBERS:
                    HandleNativeGroupMembersQuery();
                    return true;
                default:
                    return false;
            }
        }

        internal bool QueueNativeGroupRequest(TPlayObject requester, byte type)
        {
            if (requester == null || ReferenceEquals(requester, this))
                return false;

            var pending = NativeGroupPendingRequests.GetValue(this,
                static _ => new List<NativeGroupRequest>());
            for (var i = 0; i < pending.Count; i++)
            {
                if (ReferenceEquals(pending[i].Requester, requester)
                    && pending[i].Type == type)
                {
                    // 战神 sub_6F3BFC hit => sub_6F39B4 @6F39F5 lea edx,[esi+0x106] (TARGET
                    // name) / 6F3A03 mov edx,0x6F3B30 / 6F3A0B mov cl,0x2E (ShortString cap 46)
                    // / 6F3A20 mov cx,0x38FF / call vtable+0xD4 -> the REQUESTER.
                    // Literal 0x6F3B30 verified byte-for-byte (declen 32).
                    requester.SysMsg(m_sCharName
                        + "未回复您的邀请，请十秒后再尝试。",
                        MsgColor.Red, MsgType.Hint);
                    return false;
                }
            }

            if (pending.Count >= NativeGroupMaxPendingRequests)
            {
                // 战神 sub_6F39B4 @6F3A3A cmp dword [eax+8],0xA / jl -> 6F3A40 mov cx,0xFFDB
                // with edx=0x6F3B5C, sent to the REQUESTER. Literal verified (declen 24).
                // cx unpacks as FColor = cx & 0xFF, BColor = cx >> 8, so 0xFFDB is
                // 0xDB/0xFF == MsgColor.Green (MsgColor.Red would be 0x38FF).
                requester.SysMsg("对方正忙，请稍后再请求。", MsgColor.Green,
                    MsgType.Hint);
                return false;
            }

            pending.Add(new NativeGroupRequest(requester, type));
            SendNativeGroupPacket(this,
                BuildNativeGroupHeader(Grobal2.SM_NOTIFY_GROUP_MESSAGE, 1,
                    type, 0, 0),
                EncodeNativeGroupText(requester.m_sCharName));
            SendNativeGroupRequestConfirmation(requester, type);
            return true;
        }

        private void HandleNativeGroupReply(TProcessMessage processMessage)
        {
            if (processMessage.nParam1 is not (0 or 1))
                return;

            if (!TryDecodeNativeGroupText(
                    DecodeNativeSocialBody(processMessage.Payload),
                    out var requesterName))
                return;

            var requester = M2Share.UserEngine?.GetPlayObject(requesterName);
            if (requester == null || ReferenceEquals(requester, this))
            {
                SysMsg("请求玩家不存在或已经离线。", MsgColor.Red,
                    MsgType.Hint);
                return;
            }

            var type = unchecked((byte)processMessage.nParam2);
            if (processMessage.nParam1 == 0)
            {
                if (RemoveNativeGroupRequest(requester, type))
                    SendNativeGroupRequestRejected(requester, type);
                return;
            }

            if (!HasNativeGroupRequest(requester, type))
            {
                SysMsg("该请求已经失效。", MsgColor.Red, MsgType.Hint);
                return;
            }

            RemoveNativeGroupRequest(requester, type);
            switch (type)
            {
                case 0:
                    if (requester.m_GroupOwner != null)
                        AddNativeGroupMember(requester, this, 0);
                    else
                        CreateNativeGroup(requester, this);
                    break;
                case 1:
                    if (m_GroupOwner != null)
                        AddNativeGroupMember(this, requester, 1);
                    else
                        SysMsg("当前没有可加入成员的队伍。", MsgColor.Red,
                            MsgType.Hint);
                    break;
                case 2:
                    requester.AcceptNativeFriend(this);
                    break;
            }
        }

        private void HandleNativeJoinGroup(TProcessMessage processMessage)
        {
            if (!TryDecodeNativeGroupText(
                    DecodeNativeSocialBody(processMessage.Payload),
                    out var targetName))
                return;

            var error = 0;
            if (!CanJoinNativeGroup(this) || m_GroupOwner != null)
            {
                error = -3;
            }
            else
            {
                var target = M2Share.UserEngine?.GetPlayObject(targetName);
                if (target == null)
                {
                    error = -1;
                }
                else if (ReferenceEquals(target, this))
                {
                    error = -10;
                }
                else if (target.m_GroupOwner == null)
                {
                    error = -2;
                }
                else
                {
                    var leader = target.m_GroupOwner as TPlayObject;
                    if (leader == null || leader.m_GroupMembers == null
                        || !ReferenceEquals(leader.m_GroupOwner, leader))
                    {
                        error = -99;
                    }
                    else if (leader.m_GroupMembers.Count >=
                             NativeGroupMaxMembers)
                    {
                        error = -4;
                    }
                    else
                    {
                        leader.QueueNativeGroupRequest(this, 1);
                    }
                }
            }

            if (error != 0)
            {
                SendNativeGroupPacket(this,
                    BuildNativeGroupHeader(Grobal2.CM_JOINGROUP, error,
                        0, 0, 0), Array.Empty<byte>());
            }
        }

        private void HandleNativeNearbyPlayerQuery(TProcessMessage processMessage)
        {
            var requestedCount = unchecked((ushort)processMessage.nParam2);
            if (m_PEnvir == null || requestedCount == 0)
                return;

            // 6-bit-decode the ENCODED wire body first, then read the 16-byte name records
            // from the true bytes (was reading raw encoded Payload = garbage names).
            var requestBody = DecodeNativeSocialBody(processMessage.Payload);
            using var response = new MemoryStream();
            for (var index = 0; index < requestedCount; index++)
            {
                var offset = index * 16;
                if (offset + 16 > requestBody.Length
                    || !TryReadNativeGroupShortString(requestBody, offset, 15,
                        out var playerName))
                    continue;

                var player = M2Share.UserEngine?.GetPlayObject(playerName);
                if (player == null || IsNativeGroupRestricted(player)
                    || !ReferenceEquals(player.m_PEnvir, m_PEnvir))
                    continue;

                var record = BuildNativeGroupPlayerRecord(player,
                    GetNativeGroupGuildName(player));
                response.Write(record, 0, record.Length);
            }

            var body = response.ToArray();
            var count = body.Length / NativeGroupPlayerRecordSize;
            SendNativeGroupPacket(this,
                BuildNativeGroupHeader(Grobal2.CM_QUERY_NEARBYPLAYER, 0,
                    body.Length, 0, count), body);
        }

        private void HandleNativeNearbyGroupQuery()
        {
            if (m_PEnvir == null || m_VisibleHumanList == null)
                return;

            var ownLeader = m_GroupOwner as TPlayObject;
            var seenLeaders = new HashSet<TPlayObject>();
            var groups = new List<(TPlayObject Leader, long Distance)>();
            for (var i = 0; i < m_VisibleHumanList.Count; i++)
            {
                if (m_VisibleHumanList[i] is not TPlayObject candidate
                    || IsNativeGroupRestricted(candidate)
                    || !ReferenceEquals(candidate.m_PEnvir, m_PEnvir))
                    continue;

                var leader = candidate.m_GroupOwner as TPlayObject;
                if (leader == null || ReferenceEquals(leader, ownLeader)
                    || leader.m_GroupMembers == null
                    || !ReferenceEquals(leader.m_GroupOwner, leader)
                    || IsNativeGroupRestricted(leader)
                    || !seenLeaders.Add(leader))
                    continue;

                var dx = (long)candidate.m_nCurrX - m_nCurrX;
                var dy = (long)candidate.m_nCurrY - m_nCurrY;
                groups.Add((leader, dx * dx + dy * dy));
            }

            if (groups.Count == 0)
                return;

            using var response = new MemoryStream();
            foreach (var group in groups.OrderBy(group => group.Distance))
            {
                var record = BuildNativeNearbyGroupRecord(group.Leader,
                    group.Leader.m_GroupMembers.Count,
                    GetNativeGroupGuildName(group.Leader));
                response.Write(record, 0, record.Length);
            }

            var body = response.ToArray();
            var count = body.Length / NativeGroupPlayerRecordSize;
            SendNativeGroupPacket(this,
                BuildNativeGroupHeader(Grobal2.CM_QUERY_NEARBYGROUP, 0,
                    body.Length, 0, count), body);
        }

        private void HandleNativeGroupMembersQuery()
        {
            if (m_GroupOwner is not TPlayObject leader)
                return;

            var body = BuildNativeGroupMembersBody(leader);
            var count = body.Length / NativeGroupMemberRecordSize;
            SendNativeGroupPacket(this,
                BuildNativeGroupHeader(Grobal2.SM_GROUPMEMBERS, count,
                    body.Length, 0, 0), body);
        }

        private static void CreateNativeGroup(TPlayObject leader,
            TPlayObject member)
        {
            var error = -99;
            if (leader == null || member == null
                || ReferenceEquals(leader, member)
                || leader.m_GroupMembers == null)
            {
                error = -4;
            }
            else if (IsNativeGroupRestricted(leader))
            {
                error = -6;
            }
            else if (leader.m_GroupOwner != null)
            {
                error = -1;
            }
            else if (!CanJoinNativeGroup(member))
            {
                error = -4;
            }
            else if (member.m_GroupOwner != null)
            {
                error = -3;
            }
            else
            {
                leader.m_GroupMembers.Clear();
                leader.m_GroupMembers.Add(leader);
                leader.m_GroupMembers.Add(member);
                leader.m_GroupOwner = leader;
                member.m_GroupOwner = leader;
                leader.m_boAllowGroup = true;
                BroadcastNativeGroupMembers(leader);
                SendNativeGroupPacket(leader,
                    BuildNativeGroupHeader(Grobal2.SM_CREATEGROUP_OK, 0,
                        0, 0, 0), Array.Empty<byte>());
                member.DismissNativeGroupRequests(0);
                member.DismissNativeGroupRequests(1);
                return;
            }

            SendNativeGroupPacket(member,
                BuildNativeGroupHeader(Grobal2.SM_CREATEGROUP_FAIL, error,
                    0, 0, 0), Array.Empty<byte>());
        }

        private static void AddNativeGroupMember(TPlayObject groupActor,
            TPlayObject member, byte requestType)
        {
            var error = -99;
            var leader = groupActor?.m_GroupOwner as TPlayObject;
            if (groupActor == null || IsNativeGroupRestricted(groupActor))
            {
                error = -99;
            }
            else if (leader == null || leader.m_GroupMembers == null
                     || !ReferenceEquals(leader.m_GroupOwner, leader))
            {
                error = -1;
            }
            else if (leader.m_GroupMembers.Count >= NativeGroupMaxMembers)
            {
                error = -5;
            }
            else if (!CanJoinNativeGroup(member))
            {
                error = -4;
            }
            else if (member.m_GroupOwner != null)
            {
                error = -3;
            }
            else
            {
                leader.m_GroupMembers.Add(member);
                member.m_GroupOwner = leader;
                BroadcastNativeGroupMembers(leader);
                SendNativeGroupPacket(groupActor,
                    BuildNativeGroupHeader(Grobal2.SM_GROUPADDMEM_OK, 0,
                        0, 0, 0), Array.Empty<byte>());
                member.DismissNativeGroupRequests(0);
                member.DismissNativeGroupRequests(1);
                return;
            }

            var recipient = requestType == 0 ? member : groupActor;
            var ident = requestType == 0
                ? Grobal2.SM_GROUPADDMEM_FAIL
                : Grobal2.CM_JOINGROUP;
            SendNativeGroupPacket(recipient,
                BuildNativeGroupHeader(ident, error, 0, 0, 0),
                Array.Empty<byte>());
        }

        private bool HasNativeGroupRequest(TPlayObject requester, byte type)
        {
            var pending = NativeGroupPendingRequests.GetValue(this,
                static _ => new List<NativeGroupRequest>());
            for (var i = 0; i < pending.Count; i++)
            {
                if (ReferenceEquals(pending[i].Requester, requester)
                    && pending[i].Type == type)
                    return true;
            }
            return false;
        }

        private bool RemoveNativeGroupRequest(TPlayObject requester, byte type)
        {
            var pending = NativeGroupPendingRequests.GetValue(this,
                static _ => new List<NativeGroupRequest>());
            var removed = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(pending[i].Requester, requester)
                    || pending[i].Type != type)
                    continue;

                pending.RemoveAt(i);
                removed = true;
                SendNativeGroupPacket(this,
                    BuildNativeGroupHeader(Grobal2.SM_NOTIFY_GROUP_MESSAGE,
                        0, type, 0, 0),
                    EncodeNativeGroupText(requester.m_sCharName));
            }
            return removed;
        }

        private void DismissNativeGroupRequests(byte type)
        {
            var pending = NativeGroupPendingRequests.GetValue(this,
                static _ => new List<NativeGroupRequest>());
            for (var i = 0; i < pending.Count; i++)
            {
                var request = pending[i];
                if (request.Type != type)
                    continue;

                SendNativeGroupPacket(this,
                    BuildNativeGroupHeader(Grobal2.SM_NOTIFY_GROUP_MESSAGE,
                        0, type, 0, 0),
                    EncodeNativeGroupText(request.Requester.m_sCharName));
                // 战神 sub_6F4038 @6F4104 switches on entry[+4] and appends, cx=0x38FF:
                //   type 0 -> 0x6F4208, type 1 -> 0x6F4220, type 2 -> 0x6F4238 (each
                //   ShortString declen 20, cap cl=0x22 at 6F412C/6F416A/6F41A6).
                // NOTE: 0x6F4220 really is 组队申请 in the image, not 入队申请 - verified
                // byte-for-byte (ceb4cfecd3a6c4fab5c4 d7e9b6d3 c9eac7eb a1a3).
                var suffix = type switch
                {
                    0 => "未响应您的组队邀请。",
                    1 => "未响应您的组队申请。",
                    2 => "未响应您的好友申请。",
                    _ => null
                };
                if (suffix == null)
                    continue;
                request.Requester.SysMsg(m_sCharName + suffix,
                    MsgColor.Red, MsgType.Hint);
            }
        }

        private static bool CanJoinNativeGroup(TPlayObject player)
        {
            // Mirrors 战神 sub_6C33CC's -4 leg, whose three conditions are !m_boAllowGroup
            // (6C33D8), the map no-group byte (6C33E7) and sub_6BBE84 (6C33EF). The map byte was
            // previously missing here, so a party could form on a map 战神 forbids it on.
            return player != null && player.m_boAllowGroup
                && !IsNativeGroupMapDenied(player)
                && !IsNativeGroupRestricted(player);
        }

        /// <summary>
        /// Exact port of 战神 <c>sub_6BBE84</c> (bounded 0x6BBE84..0x6BBEB6, single ret pair):
        /// <c>6BBE8A mov dl,0x33 / 6BBE8E call sub_772960 / 6BBE93 test al,al /
        /// 6BBE95 je 0x6BBEA0 / 6BBE97 cmp dword [ebx+0x3c0],0 / 6BBE9E jne 0x6BBEB2</c>
        /// then <c>6BBEA0 mov dl,0x34 / 6BBEA4 call sub_772960 / 6BBEAB jne 0x6BBEB2</c>,
        /// i.e. <c>(state 0x33 &amp;&amp; [self+0x3C0] != 0) || state 0x34</c> — a pure mount gate:
        /// 0x33 = single-seat mount (SET <c>6EE37E mov dl,0x33</c> / <c>6EE382 call sub_772974</c>,
        /// CLEAR 0x6EE48C-0x6EE490), 0x34 = two-seat mount (SET <c>6EE8AF</c>/<c>6EE8B3</c>,
        /// CLEAR 0x6EEBC2-0x6EEBC6). <c>[+0x3C0]</c> is the two-seat mount PARTNER
        /// POINTER, mirrored as <see cref="m_NativeHorsePartner"/>: all 9 of its writers
        /// sit in the horse cluster (0x6EE398 / 0x6EE560 / 0x6EE8A0 / 0x6EEAA7 /
        /// 0x6EED51 / 0x6EEDF4 / 0x74BCD5) and its readers dereference it as an actor —
        /// 0x6C5A99 <c>mov eax,[ebx+0x3c0]</c> then <c>lea edx,[eax+0x106]</c> into a
        /// ShortString copy (the partner's name), and 0x6E651E the same. It is not an
        /// int counter, so <c>m_nNativeUnionActivationCarrier</c> cannot represent it.
        /// Read with the identical <c>state 0x33 &amp;&amp; partner != null</c> shape by
        /// <see cref="IsNativeFixedCoordMountBlocked"/>.
        /// <para>
        /// This previously stood in as <c>m_boDeath || m_boGhost</c>, which was wrong twice over:
        /// it missed the mount gate entirely, and neither group precheck tests death/ghost —
        /// <c>sub_6C3380</c> is {sub_6BBE84, [map+0x7C], [self+0xA80]} and <c>sub_6C33CC</c> is
        /// {[self+0xBA1], [map+0x7C], sub_6BBE84, [self+0xA80]}, verified byte-for-byte. So a
        /// dead/ghost player was blocked from grouping where 战神 allows it, and a mounted one
        /// was allowed where 战神 forbids it.
        /// </para>
        /// </summary>
        private static bool IsNativeGroupRestricted(TPlayObject player)
        {
            if (player == null)
            {
                return true;
            }
            // 6BBE8A / 6BBE97: both halves required, else fall through to the 0x34 test.
            if (player.HasNativeActiveState(NativeGroupMountedState) &&
                player.m_NativeHorsePartner != null)
            {
                return true;
            }
            // 6BBEA0: two-seat mount blocks on its own, no partner test.
            return player.HasNativeActiveState(NativeGroupTwoSeatMountState);
        }

        private const int NativeGroupMountedState = 0x33;
        private const int NativeGroupTwoSeatMountState = 0x34;

        /// <summary>
        /// 战神 map "no group" gate. Both group prechecks read the same map byte:
        /// self  <c>sub_6C3380</c>: <c>6C339B mov eax,[edi+0x128] / 6C33A1 cmp byte [eax+0x7C],0</c>
        /// target <c>sub_6C33CC</c>: <c>6C33E1 mov eax,[esi+0x128] / 6C33E7 cmp byte [eax+0x7C],0</c>
        /// The map-attribute parser <c>sub_774D98</c> sets that byte for the token
        /// <c>BLACKROOM</c> (token string @0x775DC4, store <c>775318 mov byte [ebx+0x7C],1</c> /
        /// clear <c>775329 mov byte [ebx+0x7C],0</c>), i.e. <c>[+0x7C] == boBLACKROOM</c>.
        /// Offset-namespace cross-check: the same parser sets <c>[ebx+0x60]</c> for token
        /// <c>QUIZ</c> (@0x774F3B) and the native shout gate reads <c>[map+0x60]</c>
        /// (<c>6BB777 cmp byte [eax+0x60],0</c>) — which is exactly the flag C# already uses
        /// there (<c>Flag.boQUIZ</c>), so parser-base and runtime-read offsets share one namespace.
        /// </summary>
        private static bool IsNativeGroupMapDenied(TPlayObject player)
        {
            return player?.m_PEnvir != null && player.m_PEnvir.Flag.boBLACKROOM;
        }

        private static void BroadcastNativeGroupMembers(TPlayObject leader)
        {
            if (leader?.m_GroupMembers == null)
                return;

            var body = BuildNativeGroupMembersBody(leader);
            var count = body.Length / NativeGroupMemberRecordSize;
            var header = BuildNativeGroupHeader(Grobal2.SM_GROUPMEMBERS,
                count, body.Length, 0, 0);
            var recipients = leader.m_GroupMembers
                .Where(member => member != null)
                .Take(NativeGroupMaxMembers)
                .ToArray();
            for (var i = 0; i < recipients.Length; i++)
                SendNativeGroupPacket(recipients[i], header, body);
        }

        internal static byte[] BuildNativeGroupMembersBody(TPlayObject leader)
        {
            if (leader?.m_GroupMembers == null)
                return Array.Empty<byte>();

            using var stream = new MemoryStream();
            foreach (var member in leader.m_GroupMembers
                         .Where(member => member != null)
                         .Take(NativeGroupMaxMembers))
            {
                var record = BuildNativeGroupMemberRecord(member, leader);
                stream.Write(record, 0, record.Length);
            }
            return stream.ToArray();
        }

        internal static byte[] BuildNativeGroupPlayerRecord(TPlayObject player,
            string guildName)
        {
            using var stream = new MemoryStream(NativeGroupPlayerRecordSize);
            using var writer = new BinaryWriter(stream);
            WriteClientShortString(writer, player?.m_sCharName, 15);
            writer.Write((ushort)(player?.m_Abil?.Level ?? 0));
            writer.Write((byte)(player?.m_btGender ?? 0));
            writer.Write((byte)(player?.m_btJob ?? 0));
            WriteClientShortString(writer, guildName, 15);
            return stream.ToArray();
        }

        internal static byte[] BuildNativeNearbyGroupRecord(TPlayObject leader,
            int memberCount, string guildName)
        {
            using var stream = new MemoryStream(NativeGroupPlayerRecordSize);
            using var writer = new BinaryWriter(stream);
            WriteClientShortString(writer, leader?.m_sCharName, 15);
            writer.Write((ushort)(leader?.m_Abil?.Level ?? 0));
            writer.Write(unchecked((byte)memberCount));
            WriteClientShortString(writer, guildName, 15);
            writer.Write((byte)0);
            return stream.ToArray();
        }

        internal static byte[] BuildNativeGroupMemberRecord(TPlayObject member,
            TPlayObject leader)
        {
            using var stream = new MemoryStream(NativeGroupMemberRecordSize);
            using var writer = new BinaryWriter(stream);
            WriteClientShortString(writer, member?.m_sCharName, 15);
            writer.Write((ushort)(member?.m_Abil?.Level ?? 0));
            writer.Write((byte)(member?.m_btGender ?? 0));
            writer.Write((byte)1);
            WriteClientShortString(writer,
                member?.m_PEnvir?.sMapName ?? member?.m_sMapName, 31);
            writer.Write(ReferenceEquals(member, leader) ? (byte)1 : (byte)0);
            writer.Write((byte)0);
            return stream.ToArray();
        }

        internal static ClientPacket BuildNativeGroupHeader(int ident,
            int recog, int param, int tag, int series)
        {
            return Grobal2.MakeDefaultMsg(ident, recog, param, tag, series);
        }

        internal static bool TryReadNativeGroupShortString(byte[] body,
            int offset, int capacity, out string value)
        {
            value = string.Empty;
            if (body == null || capacity < 0 || offset < 0
                || offset + capacity + 1 > body.Length)
                return false;

            var length = body[offset];
            if (length > capacity)
                return false;
            value = HUtil32.GbkEncoding.GetString(body, offset + 1, length);
            return value.IndexOf('\0') < 0;
        }

        private static bool TryDecodeNativeGroupText(object payload,
            out string value)
        {
            value = string.Empty;
            if (payload is not byte[] body || body.Length == 0)
                return false;
            value = HUtil32.GbkEncoding.GetString(body);
            return value.Length > 0 && value.IndexOf('\0') < 0;
        }

        private static byte[] EncodeNativeGroupText(string value)
        {
            return HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
        }

        private static string GetNativeGroupGuildName(TPlayObject player)
        {
            if (player == null || M2Share.CorpsService == null)
                return string.Empty;
            return M2Share.CorpsService.GetPlayerGildName(
                player.GetCachedNativeUserId()) ?? string.Empty;
        }

        private static void SendNativeGroupPacket(TPlayObject recipient,
            ClientPacket header, byte[] body)
        {
            recipient?.SendSocket(header, body ?? Array.Empty<byte>());
        }

        private static void SendNativeGroupRequestConfirmation(
            TPlayObject requester, byte type)
        {
            // 战神 sub_6F39B4 @6F3AB6 switches on the queued record's type byte [rec+4] and
            // hints the REQUESTER with cx=0xFFDB via vtable+0xD4, NO name prefix (each branch
            // pushes only edx=<literal>):
            //   type 0 -> 6F3AC9 edx=0x6F3B80, type 1 -> 6F3ADF edx=0x6F3BAC,
            //   type 2 -> 6F3AF5 edx=0x6F3BD8   (AnsiString declen 34 each).
            var text = type switch
            {
                0 => "您已提交组队邀请，请等待对方回应。",
                1 => "您已提交入队申请，请等待对方回应。",
                2 => "您已提交好友申请，请等待对方回应。",
                _ => null
            };
            if (text != null)
                requester.SysMsg(text, MsgColor.Green, MsgType.Hint);
        }

        private void SendNativeGroupRequestRejected(TPlayObject requester,
            byte type)
        {
            // 战神 sub_6F3C54 @6F3C97 switches on cl and builds self.name + <literal>, sent
            // cx=0x38FF via vtable+0xD4 to the REQUESTER (6F3CD6/6F3D13/6F3D4C mov eax,esi):
            //   type 0 -> 0x6F3D88, type 1 -> 0x6F3DA0, type 2 -> 0x6F3DB8
            //   (each ShortString declen 20, cap cl=0x22 at 6F3CBD/6F3CF8/6F3D33).
            // The image stores WHOLE sentences; do not rebuild them from a noun + template
            // (the previous C# did "拒绝了你的" + noun, which is not the native wording).
            var text = type switch
            {
                0 => "拒绝了您的组队邀请。",
                1 => "拒绝了您的入队申请。",
                2 => "拒绝了您的好友申请。",
                _ => null
            };
            if (text != null)
            {
                requester.SysMsg(m_sCharName + text, MsgColor.Red, MsgType.Hint);
            }
        }
    }
}
