using System.Globalization;

namespace GameSvr.Services
{
    public sealed class NativeChannelCreateResult
    {
        internal NativeChannelCreateResult(int code, int channelId, byte type)
        {
            Code = code;
            ChannelId = channelId;
            Type = type;
        }

        public int Code { get; }

        public int ChannelId { get; }

        public byte Type { get; }
    }

    public sealed class NativeChannelEnterResult
    {
        internal NativeChannelEnterResult(int code, int channelId, byte type)
        {
            Code = code;
            ChannelId = channelId;
            Type = type;
        }

        public int Code { get; }

        public int ChannelId { get; }

        public byte Type { get; }
    }

    public sealed class NativeScopedChannelEnterResult
    {
        internal NativeScopedChannelEnterResult(NativeChannelEnterResult enter,
            bool createAttempted, int createCode)
        {
            Enter = enter;
            CreateAttempted = createAttempted;
            CreateCode = createCode;
        }

        public NativeChannelEnterResult Enter { get; }

        public bool CreateAttempted { get; }

        public int CreateCode { get; }
    }

    public sealed class NativeChannelQueryResult
    {
        internal NativeChannelQueryResult(int code,
            NativeChannelSnapshot snapshot)
        {
            Code = code;
            Snapshot = snapshot;
        }

        public int Code { get; }

        public NativeChannelSnapshot Snapshot { get; }
    }

    public sealed class NativeChannelManager
    {
        private const int MaximumPublicChannels = 50;
        private const byte OrganizationCapacity = byte.MaxValue;

        private readonly object _sync = new object();
        private readonly Dictionary<int, Channel> _channels = new();
        private readonly Dictionary<string, int> _channelsByScope =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<int> _type0Order = new();
        private readonly List<int> _type1Order = new();
        private readonly Dictionary<long, int> _actorChannels = new();
        private readonly Dictionary<long, NativeChannelActor> _actors = new();
        private readonly bool[] _nativeSlots = new bool[51];
        private int _lastChannelId = 999;
        private INativeChannelMembershipResolver _membershipResolver;

        public NativeChannelManager(
            INativeChannelMembershipResolver membershipResolver = null)
        {
            _membershipResolver = membershipResolver
                ?? new ExistingSocialMembershipResolver();
        }

        public static NativeChannelManager Shared { get; } = new();

        public INativeChannelMembershipResolver MembershipResolver
        {
            get
            {
                lock (_sync) return _membershipResolver;
            }
            set
            {
                lock (_sync)
                {
                    _membershipResolver = value
                        ?? new ExistingSocialMembershipResolver();
                }
            }
        }

        public bool TryResolveMembership(NativeChannelActor actor, byte type,
            out NativeChannelMembership membership)
        {
            membership = default;
            INativeChannelMembershipResolver resolver;
            lock (_sync) resolver = _membershipResolver;
            return resolver != null
                && resolver.TryResolve(actor, type, out membership);
        }

        public NativeChannelCreateResult CreatePublic(NativeChannelActor owner,
            NativeChannelCreateRequest request)
        {
            if (request == null)
                return new NativeChannelCreateResult(-99, 0, 0);
            if (owner == null || owner.Level < 35)
                return new NativeChannelCreateResult(-3, 0, request.Type);

            lock (_sync)
            {
                // Native create strategy rejects an actor already in a channel with -4 (before the
                // -1 name / -2 capacity / create core), mirroring NativeChannelWriteTransaction.Create
                // (Role != NotInChannel -> -4). Live membership = _actorChannels[owner.Identity] > 0.
                if (_actorChannels.TryGetValue(owner.Identity, out var id)
                    && id > 0)
                    return new NativeChannelCreateResult(-4, 0, request.Type);
                if (FindPublicByNameLocked(request.Name) != null)
                    return new NativeChannelCreateResult(-1, 0, request.Type);
                if (_type0Order.Count + _type1Order.Count
                    >= MaximumPublicChannels)
                {
                    return new NativeChannelCreateResult(-2, 0, request.Type);
                }

                var channel = CreateLocked(request.Type, request.Name,
                    request.Name, owner, request.Capacity, request.Password,
                    AllocateNativeSlotLocked());
                if (request.Type == 0)
                    _type0Order.Add(channel.Id);
                else
                    _type1Order.Add(channel.Id);
                return new NativeChannelCreateResult(0, channel.Id,
                    channel.Type);
            }
        }

        public NativeChannelEnterResult Enter(NativeChannelActor actor,
            int channelId, long password)
        {
            lock (_sync)
            {
                return EnterLocked(actor, channelId, password);
            }
        }

        public NativeScopedChannelEnterResult EnterScoped(
            NativeChannelActor actor, byte type,
            NativeChannelMembership membership)
        {
            lock (_sync)
            {
                var channel = FindScopedLocked(type, membership.Key);
                var createAttempted = channel == null;
                var createCode = 0;
                if (createAttempted)
                {
                    if (type < 2 || type > 4
                        || string.IsNullOrEmpty(membership.Key))
                    {
                        createCode = -1;
                    }
                    else if (FindScopedLocked(type, membership.Key) != null)
                    {
                        createCode = -1;
                    }
                    else
                    {
                        channel = CreateLocked(type, membership.ChannelName,
                            membership.Key, actor, OrganizationCapacity, 0, 0);
                    }
                }

                if (createCode != 0 || channel == null)
                {
                    return new NativeScopedChannelEnterResult(
                        new NativeChannelEnterResult(-99, 0, type),
                        createAttempted, createCode);
                }

                return new NativeScopedChannelEnterResult(
                    EnterLocked(actor, channel.Id, 0), createAttempted,
                    createCode);
            }
        }

        public int Exit(NativeChannelActor actor)
        {
            lock (_sync)
            {
                if (!TryTrackActorLocked(actor)) return -99;
                if (!_actorChannels.TryGetValue(actor.Identity,
                        out var channelId) || channelId <= 0)
                    return -13;
                if (!_channels.TryGetValue(channelId, out var channel))
                    return -11;
                if (!channel.MemberIds.Contains(actor.Identity)) return -12;

                RemoveMemberLocked(channel, actor.Identity);
                return 0;
            }
        }

        public int ChangeMode(NativeChannelActor actor, int channelId,
            byte mode)
        {
            lock (_sync)
            {
                if (!TryTrackActorLocked(actor)) return -99;
                if (!_actorChannels.TryGetValue(actor.Identity,
                        out var currentChannelId)
                    || channelId != currentChannelId)
                    return -15;
                if (!_channels.TryGetValue(currentChannelId, out var channel))
                    return -16;
                if (!channel.OwnerIds.Contains(actor.Identity)) return -14;

                channel.Mode = mode;
                return 0;
            }
        }

        public int Kick(NativeChannelActor actor, int channelId,
            NativeChannelActor target)
        {
            lock (_sync)
            {
                if (!TryTrackActorLocked(actor)) return -99;
                TryTrackActorLocked(target);
                if (!_actorChannels.TryGetValue(actor.Identity,
                        out var currentChannelId)
                    || channelId != currentChannelId)
                    return -18;
                if (!_channels.TryGetValue(currentChannelId, out var channel))
                    return -20;
                if (!channel.OwnerIds.Contains(actor.Identity)) return -17;
                if (target == null
                    || !channel.MemberIds.Contains(target.Identity))
                    return -19;
                if (channel.OwnerIds.Contains(target.Identity)) return -21;

                return ExitLocked(target);
            }
        }

        public int ChangeMute(NativeChannelActor actor, int channelId,
            NativeChannelActor target, bool muted)
        {
            lock (_sync)
            {
                if (!TryTrackActorLocked(actor)) return -99;
                TryTrackActorLocked(target);
                if (!_actorChannels.TryGetValue(actor.Identity,
                        out var currentChannelId)
                    || channelId != currentChannelId)
                    return -24;
                if (!_channels.TryGetValue(currentChannelId, out var channel))
                    return -23;
                if (!channel.OwnerIds.Contains(actor.Identity)) return -22;
                if (target == null
                    || !channel.MemberIds.Contains(target.Identity))
                    return -25;
                if (channel.OwnerIds.Contains(target.Identity)) return -26;

                if (muted)
                    channel.MutedIds.Add(target.Identity);
                else
                    channel.MutedIds.Remove(target.Identity);
                return 0;
            }
        }

        public IReadOnlyList<NativeChannelSnapshot> GetPublicChannels()
        {
            lock (_sync)
            {
                var result = new List<NativeChannelSnapshot>(
                    _type0Order.Count + _type1Order.Count);
                AppendPublicSnapshotsLocked(_type0Order, result);
                AppendPublicSnapshotsLocked(_type1Order, result);
                return result;
            }
        }

        public NativeChannelQueryResult QueryById(int channelId)
        {
            lock (_sync)
            {
                if (!_channels.TryGetValue(channelId, out var channel)
                    || channel.IsClosed)
                    return new NativeChannelQueryResult(-29, null);
                return new NativeChannelQueryResult(0,
                    SnapshotLocked(channel));
            }
        }

        public NativeChannelQueryResult QueryScoped(byte type,
            NativeChannelMembership membership)
        {
            lock (_sync)
            {
                var channel = FindScopedLocked(type, membership.Key);
                if (channel == null || channel.IsClosed)
                    return new NativeChannelQueryResult(-29, null);
                return new NativeChannelQueryResult(0,
                    SnapshotLocked(channel));
            }
        }

        private NativeChannelEnterResult EnterLocked(NativeChannelActor actor,
            int channelId, long password)
        {
            if (!_channels.TryGetValue(channelId, out var channel))
                return new NativeChannelEnterResult(-7, channelId, 0);
            if (channel.IsClosed)
                return new NativeChannelEnterResult(-30, channel.Id,
                    channel.Type);
            if (!TryTrackActorLocked(actor))
                return new NativeChannelEnterResult(-99, channel.Id,
                    channel.Type);
            if (channel.Type == 1 && channel.Password != password)
                return new NativeChannelEnterResult(-8, channel.Id,
                    channel.Type);
            if (channel.MemberIds.Count >= channel.Capacity)
                return new NativeChannelEnterResult(-10, channel.Id,
                    channel.Type);

            LeaveForEnterLocked(actor.Identity);
            AddMemberLocked(channel, actor);
            return new NativeChannelEnterResult(0, channel.Id, channel.Type);
        }

        private int ExitLocked(NativeChannelActor actor)
        {
            if (!TryTrackActorLocked(actor)) return -99;
            if (!_actorChannels.TryGetValue(actor.Identity, out var channelId)
                || channelId <= 0)
                return -13;
            if (!_channels.TryGetValue(channelId, out var channel)) return -11;
            if (!channel.MemberIds.Contains(actor.Identity)) return -12;
            RemoveMemberLocked(channel, actor.Identity);
            return 0;
        }

        private void LeaveForEnterLocked(long identity)
        {
            if (!_actorChannels.TryGetValue(identity, out var channelId)
                || channelId <= 0)
                return;
            if (_channels.TryGetValue(channelId, out var channel)
                && channel.MemberIds.Contains(identity))
                RemoveMemberLocked(channel, identity);
            else
                _actorChannels[identity] = 0;
        }

        private void AddMemberLocked(Channel channel, NativeChannelActor actor)
        {
            if (channel.MemberIds.Add(actor.Identity))
                channel.MemberOrder.Add(actor.Identity);
            channel.MemberNames[actor.Identity] = actor.Name;
            if (actor.Identity == channel.OwnerIdentity)
                channel.OwnerIds.Add(actor.Identity);
            _actorChannels[actor.Identity] = channel.Id;
        }

        private void RemoveMemberLocked(Channel channel, long identity)
        {
            if (!channel.MemberIds.Remove(identity)) return;
            channel.MemberOrder.Remove(identity);
            channel.MemberNames.Remove(identity);
            channel.OwnerIds.Remove(identity);
            channel.MutedIds.Remove(identity);
            _actorChannels[identity] = 0;
            if (channel.MemberIds.Count == 0) channel.IsClosed = true;
        }

        private bool TryTrackActorLocked(NativeChannelActor actor)
        {
            if (actor == null || actor.Identity <= 0 || !actor.IsOnline)
                return false;
            _actors[actor.Identity] = actor;
            if (!_actorChannels.ContainsKey(actor.Identity))
                _actorChannels.Add(actor.Identity, 0);
            return true;
        }

        private Channel CreateLocked(byte type, string name, string scopeKey,
            NativeChannelActor owner, byte capacity, long password,
            ushort nativeSlot)
        {
            var channel = new Channel
            {
                Id = NextChannelIdLocked(),
                Type = type,
                Name = name ?? string.Empty,
                ScopeKey = scopeKey ?? string.Empty,
                OwnerIdentity = owner?.Identity ?? 0,
                OwnerName = owner?.Name ?? string.Empty,
                Capacity = capacity,
                Password = password,
                NativeSlot = nativeSlot
            };
            _channels.Add(channel.Id, channel);
            _channelsByScope[ScopeIndex(type, channel.ScopeKey)] = channel.Id;
            return channel;
        }

        private int NextChannelIdLocked()
        {
            unchecked
            {
                _lastChannelId++;
            }
            if (_lastChannelId < 1000) _lastChannelId = 1000;
            return _lastChannelId;
        }

        private ushort AllocateNativeSlotLocked()
        {
            for (ushort slot = 1; slot <= MaximumPublicChannels; slot++)
            {
                if (_nativeSlots[slot]) continue;
                _nativeSlots[slot] = true;
                return slot;
            }
            return 0;
        }

        private Channel FindPublicByNameLocked(string name)
        {
            var key0 = ScopeIndex(0, name);
            if (_channelsByScope.TryGetValue(key0, out var channelId)
                && _channels.TryGetValue(channelId, out var channel))
                return channel;
            var key1 = ScopeIndex(1, name);
            return _channelsByScope.TryGetValue(key1, out channelId)
                && _channels.TryGetValue(channelId, out channel)
                ? channel
                : null;
        }

        private Channel FindScopedLocked(byte type, string key)
        {
            return _channelsByScope.TryGetValue(ScopeIndex(type, key),
                    out var channelId)
                && _channels.TryGetValue(channelId, out var channel)
                ? channel
                : null;
        }

        private static string ScopeIndex(byte type, string key)
        {
            return type.ToString(CultureInfo.InvariantCulture) + "\0"
                + (key ?? string.Empty);
        }

        private void AppendPublicSnapshotsLocked(IEnumerable<int> order,
            ICollection<NativeChannelSnapshot> destination)
        {
            foreach (var channelId in order)
            {
                if (_channels.TryGetValue(channelId, out var channel)
                    && !channel.IsClosed)
                    destination.Add(SnapshotLocked(channel));
            }
        }

        private NativeChannelSnapshot SnapshotLocked(Channel channel)
        {
            var members = new List<NativeChannelMemberSnapshot>(
                channel.MemberOrder.Count);
            foreach (var identity in channel.MemberOrder)
            {
                channel.MemberNames.TryGetValue(identity, out var name);
                var online = _actors.TryGetValue(identity, out var actor)
                    && actor.IsOnline;
                if (online && actor != null) name = actor.Name;
                members.Add(new NativeChannelMemberSnapshot(identity, name,
                    channel.OwnerIds.Contains(identity),
                    channel.MutedIds.Contains(identity), online));
            }
            return new NativeChannelSnapshot(channel.Id, channel.Type,
                channel.Name, channel.OwnerName, channel.Mode,
                channel.Capacity, channel.NativeSlot, channel.IsClosed,
                members);
        }

        private sealed class Channel
        {
            internal int Id;
            internal byte Type;
            internal string Name;
            internal string ScopeKey;
            internal long OwnerIdentity;
            internal string OwnerName;
            internal byte Mode;
            internal byte Capacity;
            internal long Password;
            internal ushort NativeSlot;
            internal bool IsClosed;
            internal readonly List<long> MemberOrder = new();
            internal readonly HashSet<long> MemberIds = new();
            internal readonly Dictionary<long, string> MemberNames = new();
            internal readonly HashSet<long> OwnerIds = new();
            internal readonly HashSet<long> MutedIds = new();
        }

        private sealed class ExistingSocialMembershipResolver
            : INativeChannelMembershipResolver
        {
            public bool TryResolve(NativeChannelActor actor, byte type,
                out NativeChannelMembership membership)
            {
                membership = default;
                var player = actor?.Player;
                if (player == null) return false;

                if (type == 3)
                {
                    var guildName = player.m_MyGuild?.sGuildName;
                    if (string.IsNullOrEmpty(guildName)) return false;
                    membership = new NativeChannelMembership(guildName,
                        guildName);
                    return true;
                }

                if (type != 4
                    || player.m_GroupOwner is not TPlayObject groupOwner)
                    return false;
                var ownerIdentity = groupOwner.GetCachedNativeUserId();
                if (ownerIdentity <= 0) return false;
                var key = ownerIdentity.ToString(CultureInfo.InvariantCulture);
                membership = new NativeChannelMembership(key, key);
                return true;
            }
        }
    }
}
