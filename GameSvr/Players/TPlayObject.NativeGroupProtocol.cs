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

        private readonly struct NativeGroupOutgoingRequest
        {
            public NativeGroupOutgoingRequest(TPlayObject target, byte type)
            {
                Target = target;
                Type = type;
            }

            public TPlayObject Target { get; }
            public byte Type { get; }
        }

        private static readonly ConditionalWeakTable<TPlayObject,
            List<NativeGroupOutgoingRequest>> NativeGroupOutgoingRequests = new();

        private const string NativeGroupCancelRequestOk =
            "请求已取消";
        private const string NativeGroupCancelRequestFail =
            "取消请求失败";
        private const string NativeGroupTransferLeaderOk =
            "队长已转让";
        private const string NativeGroupTransferLeaderFail =
            "转让队长失败";

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
                case Grobal2.CM_1089:
                    HandleNativeGroupLeaderBroadcast(processMessage);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 战神 sub_6AF078 @0x006AF078（GM idx 654 @0x0062A1E2 同体）。无独立 CM leaf；
        /// 供 GM/脚本宿主调用。
        /// </summary>
        internal void ExecuteNativeCancelGroupOutgoingRequest(byte type)
        {
            HandleNativeCancelGroupRequest(new TProcessMessage
            {
                nParam1 = type
            });
        }

        /// <summary>
        /// 战神 sub_6AFA7C @0x006AFA7C（GM idx 658 @0x0062A226 同体，member 名在 body）。
        /// </summary>
        internal void ExecuteNativeTransferGroupLeader(string memberName)
        {
            HandleNativeTransferGroupLeader(new TProcessMessage
            {
                Payload = EncodeNativeGroupText(memberName ?? string.Empty)
            });
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
            TrackNativeGroupOutgoingRequest(requester, this, type);
            SendNativeGroupPacket(this,
                BuildNativeGroupHeader(Grobal2.SM_NOTIFY_GROUP_MESSAGE, 1,
                    type, 0, 0),
                EncodeNativeGroupText(requester.m_sCharName));
            SendNativeGroupRequestConfirmation(requester, type);
            return true;
        }

        private static bool IsNativeGroupRequestExpired(int currentTick,
            int createdTick)
        {
            // sub_6C3ABC @6C3B03..6C3B13 uses DWORD subtraction followed by
            // unsigned JBE, so equality survives and TickCount wrap is intentional.
            return unchecked((uint)(currentTick - createdTick)) > 10000u;
        }

        private void RunNativeGroupRequestExpiry()
        {
            // sub_6C3ABC @6C3AE3..6C3C65 walks the TList backwards. Every
            // expired record first retracts its 4412 UI entry, then notifies
            // the requester, and is removed even when its requester is nil.
            var pending = NativeGroupPendingRequests.GetValue(this,
                static _ => new List<NativeGroupRequest>());
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var request = pending[i];
                if (!IsNativeGroupRequestExpired(HUtil32.GetTickCount(),
                        request.CreatedTick))
                    continue;

                var requester = request.Requester;
                if (requester != null)
                {
                    SendNativeGroupExpiryPacket(
                        BuildNativeGroupHeader(
                            Grobal2.SM_NOTIFY_GROUP_MESSAGE, 0,
                            request.Type, 0, 0),
                        EncodeNativeGroupText(requester.m_sCharName));

                    var suffix = request.Type switch
                    {
                        0 => "未响应您的组队邀请。",
                        1 => "未响应您的组队申请。",
                        2 => "未响应您的好友申请。",
                        _ => null
                    };
                    if (suffix != null)
                    {
                        requester.SysMsg(m_sCharName + suffix,
                            MsgColor.Red, MsgType.Hint);
                    }
                }

                pending.RemoveAt(i);
                ClearNativeGroupOutgoingRequest(requester, this,
                    request.Type);
            }
        }

        protected virtual void SendNativeGroupExpiryPacket(
            ClientPacket header, byte[] body)
        {
            SendNativeGroupPacket(this, header, body);
        }

        // 战神 sub_6AF078 @0x006AF078：push [self+0x58C]/[self+0x588] -> sub_6ADA3C ->
        // vtable+0x14；成功 SysMsg 0x6AF0D0「请求已取消」，失败 0x6AF0E4「取消请求失败」
        // (cx=0x38FF)。GM 表 idx 654 @0x0062A1E2 亦落此体；C# 折到 outgoing 表 + 目标 pending 撤单。
        private void HandleNativeCancelGroupRequest(TProcessMessage processMessage)
        {
            var type = unchecked((byte)processMessage.nParam1);
            if (!TryCancelNativeGroupOutgoingRequest(type, out var canceled))
            {
                SysMsg(NativeGroupCancelRequestFail, MsgColor.Red, MsgType.Hint);
                return;
            }

            if (canceled != null && !canceled.m_boGhost)
            {
                canceled.RemoveNativeGroupRequest(this, type);
                SendNativeGroupPacket(canceled,
                    BuildNativeGroupHeader(Grobal2.SM_NOTIFY_GROUP_MESSAGE,
                        0, type, 0, 0),
                    EncodeNativeGroupText(m_sCharName));
            }
            SysMsg(NativeGroupCancelRequestOk, MsgColor.Red, MsgType.Hint);
        }

        // 战神 sub_6AFA7C @0x006AFA7C：[self+0xAE8] 经 sub_705660 解目标 ->
        // sub_6ADA3C vtable+0x1C；成功 0x6AFADC「队长已转让」/ 失败 0x6AFAF0「转让队长失败」。
        // GM idx 658 @0x0062A226 带 memberId 实参；CM 体为 ShortString 成员名。
        private void HandleNativeTransferGroupLeader(TProcessMessage processMessage)
        {
            if (!ReferenceEquals(m_GroupOwner, this))
            {
                SysMsg(NativeGroupTransferLeaderFail, MsgColor.Red, MsgType.Hint);
                return;
            }

            if (!TryDecodeNativeGroupText(
                    DecodeNativeSocialBody(processMessage.Payload),
                    out var memberName))
            {
                SysMsg(NativeGroupTransferLeaderFail, MsgColor.Red, MsgType.Hint);
                return;
            }

            var member = M2Share.UserEngine?.GetPlayObject(memberName);
            if (member == null || member.m_boGhost
                || member.m_GroupOwner != this
                || ReferenceEquals(member, this))
            {
                SysMsg(NativeGroupTransferLeaderFail, MsgColor.Red, MsgType.Hint);
                return;
            }

            if (!TryTransferNativeGroupLeader(member))
            {
                SysMsg(NativeGroupTransferLeaderFail, MsgColor.Red, MsgType.Hint);
                return;
            }

            SysMsg(NativeGroupTransferLeaderOk, MsgColor.Red, MsgType.Hint);
        }

        private static void TrackNativeGroupOutgoingRequest(TPlayObject requester,
            TPlayObject target, byte type)
        {
            if (requester == null || target == null || type > 1)
                return;

            var outgoing = NativeGroupOutgoingRequests.GetValue(requester,
                static _ => new List<NativeGroupOutgoingRequest>());
            for (var i = outgoing.Count - 1; i >= 0; i--)
            {
                if (outgoing[i].Type == type
                    && ReferenceEquals(outgoing[i].Target, target))
                    return;
            }
            outgoing.Add(new NativeGroupOutgoingRequest(target, type));
        }

        private bool TryCancelNativeGroupOutgoingRequest(byte type,
            out TPlayObject target)
        {
            target = null;
            if (type > 1)
                return false;

            var outgoing = NativeGroupOutgoingRequests.GetValue(this,
                static _ => new List<NativeGroupOutgoingRequest>());
            for (var i = outgoing.Count - 1; i >= 0; i--)
            {
                if (outgoing[i].Type != type)
                    continue;

                target = outgoing[i].Target;
                outgoing.RemoveAt(i);
                return true;
            }
            return false;
        }

        private bool TryTransferNativeGroupLeader(TPlayObject newLeader)
        {
            if (newLeader == null || m_GroupMembers == null
                || !ReferenceEquals(m_GroupOwner, this)
                || !m_GroupMembers.Contains(newLeader))
                return false;

            var members = m_GroupMembers
                .Where(member => member != null)
                .Take(NativeGroupMaxMembers)
                .ToList();
            if (!members.Remove(this) || !members.Remove(newLeader))
                return false;

            members.Insert(0, newLeader);
            members.Insert(1, this);

            m_GroupMembers.Clear();
            foreach (var member in members)
                m_GroupMembers.Add(member);

            m_GroupOwner = newLeader;
            foreach (var member in m_GroupMembers)
                member.m_GroupOwner = newLeader;

            BroadcastNativeGroupMembers(newLeader);
            return true;
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

            if (removed)
                ClearNativeGroupOutgoingRequest(requester, type);
            return removed;
        }

        private static void ClearNativeGroupOutgoingRequest(
            TPlayObject requester, byte type)
        {
            if (requester == null)
                return;

            var outgoing = NativeGroupOutgoingRequests.GetValue(requester,
                static _ => new List<NativeGroupOutgoingRequest>());
            for (var i = outgoing.Count - 1; i >= 0; i--)
            {
                if (outgoing[i].Type == type)
                    outgoing.RemoveAt(i);
            }
        }

        private static void ClearNativeGroupOutgoingRequest(
            TPlayObject requester, TPlayObject target, byte type)
        {
            if (requester == null || target == null)
                return;

            var outgoing = NativeGroupOutgoingRequests.GetValue(requester,
                static _ => new List<NativeGroupOutgoingRequest>());
            for (var i = outgoing.Count - 1; i >= 0; i--)
            {
                if (outgoing[i].Type == type
                    && ReferenceEquals(outgoing[i].Target, target))
                    outgoing.RemoveAt(i);
            }
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
                // 族 B 的「整段放弃」变体（je 而非 jne，无日志、无删表）——
                // 战神 sub_6F4790 的候选过滤链，逐条：
                //   6F4844  A1 50 6D 7D 00        eax := [[0x7D6D50]]      ; UserEngine 单例
                //   6F484B  E8 34 DF F5 FF        call 0x652784            ; GetPlayObject(name)
                //   6F4850  8B D8 / 85 DB
                //   6F4854  0F 84 09 01 00 00     je  0x6F4963             ; nil -> 放弃候选
                //   6F485A  80 7B 73 00 / 0F 85.. cmp byte [ebx+0x73],0    ; 幽灵 -> 放弃
                //   6F4866  E8 4D E6 07 00        call 0x772EB8            ; [+0x2E2] || HasState(0x3C)
                //   6F486D  0F 85 F0 00 00 00     jne 0x6F4963
                //   6F4873  80 BB E3 02 00 00 00  cmp byte [ebx+0x2E3],0 / jne 0x6F4963
                //   6F4882  E8 DD 14 07 00        call 0x765D64            ; ★ 有效性谓词
                //   6F4889  0F 84 D4 00 00 00     je  0x6F4963             ; 无效 -> 放弃候选
                //   6F4893  E8 F0 F9 07 00        call 0x774288            ; 潜行/隐身
                //   6F48A0  8B 87 28 01 00 00 / 3B 83 28 01 00 00 / 74 15  ; 同图才收
                // 函数身份：sub_6D7D68 大 switch 的跳表 0x6D8867 第 3 项（[0x6D8873] =
                // 0x6DB19A），索引式 0x6D8852 add eax,-0x113B / 0x6D8857 cmp eax,0x33
                // ⇒ ident = 0x113B+3 = 4414 = CM_QUERY_NEARBYPLAYER。
                if (player == null || !IsNativeCellObjectValid(player)
                    || IsNativeGroupRestricted(player)
                    || !ReferenceEquals(player.m_PEnvir, m_PEnvir))
                    continue;

                var record = BuildNativeGroupPlayerRecord(player,
                    GetNativeGroupGuildName(player));
                response.Write(record, 0, record.Length);
            }

            var body = response.ToArray();
            var count = body.Length / NativeGroupPlayerRecordSize;
            // 战神 sub_6F4790 tail: 6F496F 66 8B 45 F8 mov ax,[ebp-8] (the accepted-record
            // counter) / 50 push eax => Param, then 6A 00 / 6A 00 => Tag = Series = 0, and
            // only the payload length is scaled: 6F497F C1 E0 02 shl eax,2 / 6F4982 8D 04 C0
            // lea eax,[eax+eax*8] => Len = count*36. Param carries the COUNT, not the byte
            // length, and Series is zero - this had them the other way round.
            SendNativeGroupPacket(this,
                BuildNativeGroupHeader(Grobal2.CM_QUERY_NEARBYPLAYER, 0,
                    count, 0, 0), body);
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
                // 同一条「整段放弃」变体，战神 sub_6F43C8 的候选过滤链
                // （遍历的正是本函数用的这张表：0x6F442F mov eax,[self+0x380] /
                //   0x6F4435 mov esi,[eax+8] 取 Count，0x6F4455 call 0x424D4C = TList.Get）：
                //   6F445C  85 DB / 0F 84 BC 01.. je  0x6F4620             ; nil -> 放弃候选
                //   6F4464  80 BB 78 01 00 00 00  cmp byte [ebx+0x178],0   ; m_btRaceServer
                //   6F446B  0F 85 AF 01 00 00     jne 0x6F4620             ; 非玩家 -> 放弃
                //   6F4471  80 7B 73 00 / 0F 85.. cmp byte [ebx+0x73],0    ; 幽灵 -> 放弃
                //   6F447D  E8 36 EA 07 00        call 0x772EB8 / jne 0x6F4620
                //   6F448A  80 BB E3 02 00 00 00  cmp byte [ebx+0x2E3],0 / jne 0x6F4620
                //   6F4499  E8 C6 18 07 00        call 0x765D64            ; ★ 有效性谓词
                //   6F44A0  0F 84 7A 01 00 00     je  0x6F4620             ; 无效 -> 放弃候选
                //   6F44AB  E8 D8 FD 07 00        call 0x774288            ; 潜行/隐身
                // 函数身份：跳表 0x6D8867 第 4 项（[0x6D8877] = 0x6DB1B2）
                // ⇒ ident = 0x113B+4 = 4415 = CM_QUERY_NEARBYGROUP。
                if (m_VisibleHumanList[i] is not TPlayObject candidate
                    || !IsNativeCellObjectValid(candidate)
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
            // 战神 sub_6F43C8 tail: ebx is the accepted-record counter (6F463A xor ebx,ebx /
            // 6F46CA inc ebx) and 6F46D1 push ebx => Param, 6A 00 / 6A 00 => Tag = Series = 0.
            // Only Len is scaled: 6F46DC C1 E0 02 shl eax,2 / 6F46DF 8D 04 C0 lea eax,[eax+eax*8].
            // Same Param/Series swap as 4414.
            SendNativeGroupPacket(this,
                BuildNativeGroupHeader(Grobal2.CM_QUERY_NEARBYGROUP, 0,
                    count, 0, 0), body);
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

        // 战神 CM 1089 (0x441) 组长广播 —— dispatch @0x6D970A（VA+字节+反汇编）：
        //   6D970A 8B 45 FC              mov  eax,[ebp-4]           ; self
        //   6D970D E8 9A E4 FD FF        call 0x6B7BAC              ; IsGroupLeader?(self)->al
        //   6D9712 84 C0                 test al,al
        //   6D9714 0F 84 12 25 00 00     je   0x6DBC2C              ; 非组长 -> DEFAULT 静默丢弃
        //   6D971A 8B 45 CC              mov  eax,[ebp-0x34]        ; msg 记录指针
        //   6D971D 8B 10                 mov  edx,[eax]             ; edx = Recog ([msg+0], dword)
        //   6D971F 8B 45 FC              mov  eax,[ebp-4]           ; self
        //   6D9722 8B 80 80 0A 00 00     mov  eax,[eax+0xA80]       ; group 对象
        //   6D9728 E8 FB DE 04 00        call 0x727628             ; sub_727628(group, Recog)
        //   6D972D E9 FA 24 00 00        jmp  0x6DBC2C              ; -> DEFAULT
        // 门 sub_6B7BAC @0x6B7BAC：group=[self+0xA80]!=0 且 sub_726C14 @0x726C14
        //   (3B 50 3C cmp edx,[eax+0x3C] / 0F 94 C0 sete al) 判 self==[group+0x3C]（组长）。
        //   C# 折叠群对象到组长：m_GroupOwner 即 [self+0xA80] 兼 [group+0x3C]，故门=self 是组长。
        // sub_727628(group=eax, Recog=edx) @0x727628：
        //   72763C 89 50 40             mov  [group+0x40],edx       ; 缓存 Recog
        //   727641 循环 i=0..10（727670 cmp ebx,0xB）：
        //     727644 8B 44 98 48         mov eax,[group+ebx*4+0x48] ; 成员记录
        //     727648 8B 40 10            mov eax,[eax+0x10]         ; [rec+0x10]=playobj
        //     72764B 85 C0 / 74 1D       test eax,eax / je 跳过     ; 空槽
        //     72764F 80 78 73 00 / 75 17 cmp [obj+0x73],0 / jne 跳过; 鬼魂
        //     727655 6A00x4 / 8B4DF8 mov ecx,[Recog] / 66BA C503 mov dx,0x3C5
        //     727664 8B30 / FF96 50020000 call [obj+0x250]         ; SendDefMessage
        //       = SendDefMessage(SM 965, Recog, 0,0,0,"")；含组长自身（slot0）。
        private void HandleNativeGroupLeaderBroadcast(TProcessMessage processMessage)
        {
            if (!ReferenceEquals(m_GroupOwner, this))
                return;

            var members = m_GroupMembers;
            if (members == null)
                return;

            var recog = processMessage.nParam1; // [msg+0] Recog -> nParam1

            m_NativeGroupBroadcastRecog = recog; // 72763C mov [group+0x40],edx

            for (var i = 0; i < members.Count && i < NativeGroupMaxMembers; i++)
            {
                var member = members[i];
                if (member == null || member.m_boGhost)
                    continue;

                member.SendDefMessage((short)Grobal2.SM_965, recog, 0, 0, 0,
                    string.Empty);
            }
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

        internal void RefreshNativeGroupWire()
        {
            BroadcastNativeGroupMembers(this);
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
