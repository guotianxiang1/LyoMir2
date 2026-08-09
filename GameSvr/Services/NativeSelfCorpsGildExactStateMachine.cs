namespace GameSvr.Services
{
    public enum NativeSelfSocialRole
    {
        NoCorps,
        Member,
        CorpsViceOwner,
        CorpsOwner,
        GildViceOwner,
        GildOwner
    }

    public enum NativeSelfSocialWriteKind
    {
        InsertCorps,
        InsertCorpsMember,
        InsertGildMember,
        InsertGild
    }

    public sealed class NativeSelfSocialActor
    {
        public NativeSelfSocialActor(long id, string name, ushort level,
            byte sex, byte job)
        {
            Id = id;
            Name = name ?? string.Empty;
            Level = level;
            Sex = sex;
            Job = job;
        }

        public long Id { get; }
        public string Name { get; }
        public ushort Level { get; }
        public byte Sex { get; }
        public byte Job { get; }
    }

    public sealed class NativeSelfSocialCorps
    {
        public NativeSelfSocialCorps(long id, DateTime createTime,
            ReadOnlyMemory<byte> nameGbk, long ownerId)
        {
            Id = id;
            CreateTime = createTime;
            NameGbk = nameGbk.ToArray();
            OwnerId = ownerId;
        }

        public long Id { get; }
        public DateTime CreateTime { get; }
        public ReadOnlyMemory<byte> NameGbk { get; }
        public long OwnerId { get; }
    }

    public sealed class NativeSelfSocialGild
    {
        public NativeSelfSocialGild(long id, DateTime createTime,
            ReadOnlyMemory<byte> nameGbk, long ownerCorpsId)
        {
            Id = id;
            CreateTime = createTime;
            NameGbk = nameGbk.ToArray();
            OwnerCorpsId = ownerCorpsId;
        }

        public long Id { get; }
        public DateTime CreateTime { get; }
        public ReadOnlyMemory<byte> NameGbk { get; }
        public long OwnerCorpsId { get; }
    }

    public sealed class NativeSelfSocialLegacyWriteCommand
    {
        private NativeSelfSocialLegacyWriteCommand(
            NativeSelfSocialWriteKind kind, NativeSelfSocialCorps corps,
            NativeSelfSocialGild gild, NativeSelfSocialActor actor,
            long corpsId)
        {
            Kind = kind;
            Corps = corps;
            Gild = gild;
            Actor = actor;
            CorpsId = corpsId;
        }

        public NativeSelfSocialWriteKind Kind { get; }
        public NativeSelfSocialCorps Corps { get; }
        public NativeSelfSocialGild Gild { get; }
        public NativeSelfSocialActor Actor { get; }
        public long CorpsId { get; }

        public static NativeSelfSocialLegacyWriteCommand InsertCorps(
            NativeSelfSocialCorps corps) => new(
            NativeSelfSocialWriteKind.InsertCorps,
            corps ?? throw new ArgumentNullException(nameof(corps)),
            null, null, corps.Id);

        public static NativeSelfSocialLegacyWriteCommand InsertCorpsMember(
            NativeSelfSocialCorps corps, NativeSelfSocialActor actor) => new(
            NativeSelfSocialWriteKind.InsertCorpsMember,
            corps ?? throw new ArgumentNullException(nameof(corps)), null,
            actor ?? throw new ArgumentNullException(nameof(actor)), corps.Id);

        public static NativeSelfSocialLegacyWriteCommand InsertGildMember(
            NativeSelfSocialGild gild, long corpsId) => new(
            NativeSelfSocialWriteKind.InsertGildMember, null,
            gild ?? throw new ArgumentNullException(nameof(gild)), null,
            corpsId);

        public static NativeSelfSocialLegacyWriteCommand InsertGild(
            NativeSelfSocialGild gild) => new(
            NativeSelfSocialWriteKind.InsertGild, null,
            gild ?? throw new ArgumentNullException(nameof(gild)), null,
            gild.OwnerCorpsId);
    }

    public interface INativeSelfCorpsGildLegacyWriteQueue
    {
        void Enqueue(NativeSelfSocialLegacyWriteCommand command);
    }

    public interface INativeSelfCorpsGildLegacyWriteExecutor
    {
        bool TryExecute(NativeSelfSocialLegacyWriteCommand command,
            out string error);
        void ReportFailure(NativeSelfSocialLegacyWriteCommand command,
            string error);
    }

    /// <summary>
    /// One shared FIFO of independent writes. Each item is removed before it is
    /// executed; failure is logged and never rolls back memory or later items.
    /// </summary>
    public sealed class NativeSelfCorpsGildLegacyWriteQueue :
        INativeSelfCorpsGildLegacyWriteQueue
    {
        private readonly object _sync = new();
        private readonly object _processSync = new();
        private readonly Queue<NativeSelfSocialLegacyWriteCommand> _pending =
            new();
        private readonly INativeSelfCorpsGildLegacyWriteExecutor _executor;

        public NativeSelfCorpsGildLegacyWriteQueue(
            INativeSelfCorpsGildLegacyWriteExecutor executor)
        {
            _executor = executor ??
                        throw new ArgumentNullException(nameof(executor));
        }

        public int PendingCount
        {
            get
            {
                lock (_sync) return _pending.Count;
            }
        }

        public void Enqueue(NativeSelfSocialLegacyWriteCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            lock (_sync) _pending.Enqueue(command);
        }

        public bool ProcessNext()
        {
            lock (_processSync) return ProcessNextSerialized();
        }

        private bool ProcessNextSerialized()
        {
            NativeSelfSocialLegacyWriteCommand command;
            lock (_sync)
            {
                if (_pending.Count == 0) return false;
                command = _pending.Dequeue();
            }

            string error;
            try
            {
                if (_executor.TryExecute(command, out error)) return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            try
            {
                _executor.ReportFailure(command,
                    string.IsNullOrEmpty(error)
                        ? "legacy social write failed"
                        : error);
            }
            catch
            {
                // Logging cannot stop the original shared FIFO.
            }
            return true;
        }
    }

    public interface INativeSelfCorpsGildExactHost
    {
        bool HasPlayerCorpsPointer(NativeSelfSocialActor actor);
        bool IsMemberIndexed(NativeSelfSocialActor actor);
        bool CorpsNameExists(ReadOnlyMemory<byte> normalizedNameGbk);
        bool GildNameExists(ReadOnlyMemory<byte> normalizedNameGbk);
        NativeSelfSocialRole GetRole(NativeSelfSocialActor actor);
        bool IsActorOnline(NativeSelfSocialActor actor);
        bool TryGetDynamicCorpsId(NativeSelfSocialActor actor,
            out long corpsId);
        bool CorpsHasGild(long corpsId);

        NativeSelfSocialCorps AllocateCorps(ReadOnlyMemory<byte> nameGbk,
            NativeSelfSocialActor owner);
        NativeSelfSocialGild AllocateGild(ReadOnlyMemory<byte> nameGbk,
            long ownerCorpsId);
        void PublishCorps(NativeSelfSocialCorps corps);
        void AssignOwnerMemberCorps(NativeSelfSocialActor owner,
            NativeSelfSocialCorps corps);
        void AddOwnerMemberToCorps(NativeSelfSocialCorps corps,
            NativeSelfSocialActor owner);
        void PublishMemberIndex(NativeSelfSocialActor owner,
            NativeSelfSocialCorps corps);
        bool TryBindOnlinePlayerCorps(NativeSelfSocialActor owner,
            NativeSelfSocialCorps corps);
        void PublishGild(NativeSelfSocialGild gild, long ownerCorpsId);

        void SendPlayerCorps(NativeSelfSocialActor actor);
        void BroadcastCorpsCreated(NativeSelfSocialCorps corps);
        void SendPlayerGild(NativeSelfSocialActor actor);
        void BroadcastGildCreated(NativeSelfSocialGild gild);
        void SendCreateStatus(int ident, int result);
        void SendSocialRoleRefresh(NativeSelfSocialActor actor);
    }

    /// <summary>
    /// Exact, dormant model of the native CreateSelfCorps/CreateSelfGild path.
    /// The caller must serialize manager access as the original game loop did.
    /// </summary>
    public static class NativeSelfCorpsGildExactStateMachine
    {
        public const int CorpsCreateIdent = 4524;
        public const int GildCreateIdent = 4564;
        public const int PermissionDenied = 555;

        private static readonly uint[] InvalidCorpsAsciiBitmap =
        {
            0xFFFFFFFFu, 0xD400FFFFu, 0x10000000u, 0x10000000u
        };

        public static int CreateSelfCorps(
            INativeSelfCorpsGildExactHost host,
            INativeSelfCorpsGildLegacyWriteQueue writes,
            NativeSelfSocialActor actor, ReadOnlyMemory<byte> nameGbk)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (writes == null) throw new ArgumentNullException(nameof(writes));
            if (actor == null) throw new ArgumentNullException(nameof(actor));

            int result;
            if (host.HasPlayerCorpsPointer(actor))
            {
                result = 3;
            }
            else if (IsInvalidCorpsName(nameGbk.Span))
            {
                result = 1;
            }
            else
            {
                var normalized = NormalizeLegacyName(nameGbk.Span);
                if (host.CorpsNameExists(normalized))
                {
                    result = 2;
                }
                else if (host.IsMemberIndexed(actor))
                {
                    result = 3;
                }
                else
                {
                    var corps = host.AllocateCorps(nameGbk, actor);
                    if (corps == null)
                        throw new InvalidOperationException(
                            "native Corps allocator returned null");

                    host.PublishCorps(corps);
                    writes.Enqueue(
                        NativeSelfSocialLegacyWriteCommand.InsertCorps(corps));
                    host.AssignOwnerMemberCorps(actor, corps);
                    host.AddOwnerMemberToCorps(corps, actor);
                    writes.Enqueue(
                        NativeSelfSocialLegacyWriteCommand.InsertCorpsMember(
                            corps, actor));
                    host.PublishMemberIndex(actor, corps);
                    result = 0;

                    if (host.TryBindOnlinePlayerCorps(actor, corps))
                    {
                        host.SendPlayerCorps(actor);
                        host.BroadcastCorpsCreated(corps);
                    }
                }
            }

            host.SendCreateStatus(CorpsCreateIdent, result);
            host.SendSocialRoleRefresh(actor);
            return result;
        }

        public static int CreateSelfGild(
            INativeSelfCorpsGildExactHost host,
            INativeSelfCorpsGildLegacyWriteQueue writes,
            NativeSelfSocialActor actor, ReadOnlyMemory<byte> nameGbk)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (writes == null) throw new ArgumentNullException(nameof(writes));
            if (actor == null) throw new ArgumentNullException(nameof(actor));

            var role = host.GetRole(actor);
            if (!CanCreateGild(role))
                return FinishGildPreGate(host, PermissionDenied);
            if (!host.IsActorOnline(actor))
                return FinishGildPreGate(host, 4);
            if (!host.TryGetDynamicCorpsId(actor, out var corpsId))
                return FinishGildPreGate(host, 5);
            if (host.CorpsHasGild(corpsId))
                return FinishGildPreGate(host, 6);

            int result;
            NativeSelfSocialGild gild = null;
            var normalized = NormalizeLegacyName(nameGbk.Span);
            if (host.GildNameExists(normalized))
            {
                result = 2;
            }
            else
            {
                gild = host.AllocateGild(nameGbk, corpsId);
                if (gild == null)
                    throw new InvalidOperationException(
                        "native Gild allocator returned null");

                host.PublishGild(gild, corpsId);
                result = 0;
                writes.Enqueue(
                    NativeSelfSocialLegacyWriteCommand.InsertGildMember(
                        gild, corpsId));
                writes.Enqueue(
                    NativeSelfSocialLegacyWriteCommand.InsertGild(gild));
            }

            host.SendPlayerGild(actor);
            host.SendSocialRoleRefresh(actor);
            if (result == 0) host.BroadcastGildCreated(gild);
            host.SendCreateStatus(GildCreateIdent, result);
            return result;

            int FinishGildPreGate(INativeSelfCorpsGildExactHost exactHost,
                int code)
            {
                exactHost.SendCreateStatus(GildCreateIdent, code);
                return code;
            }
        }

        public static byte[] NormalizeLegacyName(ReadOnlySpan<byte> nameGbk)
        {
            var normalized = nameGbk.ToArray();
            for (var index = 0; index < normalized.Length; index++)
            {
                if (normalized[index] is >= (byte)'a' and <= (byte)'z')
                    normalized[index] -= (byte)('a' - 'A');
            }
            return normalized;
        }

        public static bool IsInvalidCorpsName(ReadOnlySpan<byte> nameGbk)
        {
            if (nameGbk.Length == 0) return true;
            foreach (var value in nameGbk)
            {
                if (value > 0x7F) continue;
                var mask = 1u << (value & 31);
                if ((InvalidCorpsAsciiBitmap[value >> 5] & mask) != 0)
                    return true;
            }
            return false;
        }

        private static bool CanCreateGild(NativeSelfSocialRole role) =>
            role is NativeSelfSocialRole.CorpsOwner or
                NativeSelfSocialRole.GildViceOwner or
                NativeSelfSocialRole.GildOwner;
    }
}
