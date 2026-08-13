using GameSvr.Services;
using SystemModule;

CheckStateBitsAndMutations();
CheckServiceResults();
CheckRawGbkNames();
CheckWireRecords();
CheckSourceContracts();

Console.WriteLine("NativeRelationProtocolCheck PASS");

static void CheckStateBitsAndMutations()
{
    Equal(0x01u, NativeRelationStateBits.ForOwner(
        NativeRelationKind.Friend, true), "friend first bit");
    Equal(0x01u, NativeRelationStateBits.ForOwner(
        NativeRelationKind.Friend, false), "friend second bit");
    Equal(0x02u, NativeRelationStateBits.ForOwner(
        NativeRelationKind.Attention, true), "attention first bit");
    Equal(0x04u, NativeRelationStateBits.ForOwner(
        NativeRelationKind.Attention, false), "attention second bit");
    Equal(0x08u, NativeRelationStateBits.ForOwner(
        NativeRelationKind.Blacklist, true), "blacklist first bit");
    Equal(0x10u, NativeRelationStateBits.ForOwner(
        NativeRelationKind.Blacklist, false), "blacklist second bit");

    var store = new FakeRelationStore();
    var service = new NativeRelationService(store);
    var first = Player(101, "first", 35, 1);
    var second = Player(202, "second", 42, 2);

    Equal(0, service.AddDirected(first, second,
        NativeRelationKind.Attention), "first attention add");
    Equal(0x02u, store.OnlyRow.State, "first attention state");
    Equal(byte.MaxValue, store.OnlyRow.SecondFocus,
        "first attention colors target");

    Equal(0, service.AddDirected(second, first,
        NativeRelationKind.Attention), "second attention add");
    Equal(0x06u, store.OnlyRow.State, "two-way attention state");
    Equal(byte.MaxValue, store.OnlyRow.FirstFocus,
        "second attention colors target");

    Equal(0, service.AddDirected(first, second,
        NativeRelationKind.Blacklist), "first blacklist add");
    Equal(0x0Eu, store.OnlyRow.State, "first blacklist state");
    Equal(0, service.AddDirected(second, first,
        NativeRelationKind.Blacklist), "second blacklist add");
    Equal(0x1Eu, store.OnlyRow.State, "two-way blacklist state");

    Equal(0, service.AcceptFriend(first, second), "friend accept");
    Equal(0x1Fu, store.OnlyRow.State, "all relation bits");

    Equal(0, service.Remove(first.UserId, second.Name,
        NativeRelationKind.Friend), "friend remove");
    Equal(0x1Eu, store.OnlyRow.State, "friend bit cleared only");
    Equal(0, service.Remove(first.UserId, second.Name,
        NativeRelationKind.Attention), "first attention remove");
    Equal(0x1Cu, store.OnlyRow.State,
        "first attention direction cleared only");
    Equal(0, service.Remove(second.UserId, first.Name,
        NativeRelationKind.Attention), "second attention remove");
    Equal(0x18u, store.OnlyRow.State,
        "second attention direction cleared only");
    Equal(0, service.Remove(first.UserId, second.Name,
        NativeRelationKind.Blacklist), "first blacklist remove");
    Equal(0x10u, store.OnlyRow.State,
        "first blacklist direction cleared only");
    Equal(0, service.Remove(second.UserId, first.Name,
        NativeRelationKind.Blacklist), "second blacklist remove");
    Equal(0, store.Rows.Count, "zero-state row deleted");
}

static void CheckServiceResults()
{
    var first = Player(1, "one", 1, 0);
    var second = Player(2, "two", 2, 1);
    var store = new FakeRelationStore();
    var service = new NativeRelationService(store);

    Equal(0, service.CheckFriendRequest(first, second),
        "friend request valid");
    store.ForcedInspectCount = NativeRelationService.Limit;
    Equal(-4, service.CheckFriendRequest(first, second),
        "friend request full");
    store.ForcedInspectContains = true;
    Equal(-3, service.CheckFriendRequest(first, second),
        "friend duplicate precedes full");
    store.FailInspect = true;
    Equal(-1, service.CheckFriendRequest(first, second),
        "friend inspect failure closes request");

    store = new FakeRelationStore
    {
        ForcedDirectedResult = NativeRelationStoreResult.Full
    };
    service = new NativeRelationService(store);
    Equal(-4, service.AddDirected(first, second,
        NativeRelationKind.Attention), "attention full");
    store.ForcedDirectedResult = NativeRelationStoreResult.Duplicate;
    Equal(-5, service.AddDirected(first, second,
        NativeRelationKind.Attention), "attention duplicate");
    store.ForcedDirectedResult = NativeRelationStoreResult.Failed;
    Equal(NativeRelationService.NoResponse,
        service.AddDirected(first, second, NativeRelationKind.Attention),
        "attention persistence failure has no invented status");

    store = new FakeRelationStore();
    service = new NativeRelationService(store);
    Equal(-2, service.Remove(first.UserId, second.Name,
        NativeRelationKind.Friend), "friend missing");
    Equal(-1, service.Remove(first.UserId, second.Name,
        NativeRelationKind.Attention), "attention missing");
    store.ForcedRemoveResult = NativeRelationStoreResult.Failed;
    Equal(NativeRelationService.NoResponse,
        service.Remove(first.UserId, second.Name,
            NativeRelationKind.Blacklist),
        "delete persistence failure has no invented status");

    store = new FakeRelationStore
    {
        ForcedColorResult = NativeRelationStoreResult.Missing
    };
    service = new NativeRelationService(store);
    Equal(-1, service.UpdateAttentionColor(first.UserId, second.Name, 7),
        "color missing");
    store.ForcedColorResult = NativeRelationStoreResult.Failed;
    Equal(-2, service.UpdateAttentionColor(first.UserId, second.Name, 7),
        "color persistence failure");
    store.ForcedColorResult = NativeRelationStoreResult.Success;
    Equal(0, service.UpdateAttentionColor(first.UserId, second.Name, 7),
        "color success");

    store = new FakeRelationStore
    {
        ForcedFriendResult = NativeRelationStoreResult.Duplicate
    };
    service = new NativeRelationService(store);
    Equal(-3, service.AcceptFriend(first, second), "accept duplicate");
    store.ForcedFriendResult = NativeRelationStoreResult.Full;
    Equal(-4, service.AcceptFriend(first, second), "accept full");
    store.ForcedFriendResult = NativeRelationStoreResult.Failed;
    Equal(-5, service.AcceptFriend(first, second), "accept SQL failure");
}

static void CheckRawGbkNames()
{
    var expected = "测试A";
    var raw = HUtil32.GbkEncoding.GetBytes(expected)
        .Concat(new byte[] { 0, (byte)'x' }).ToArray();
    Equal(true, NativeRelationWireCodec.TryDecodeName(raw, out var decoded),
        "GBK payload accepted");
    Equal(expected, decoded, "GBK payload decoded before NUL");
    Equal(true, NativeRelationWireCodec.TryDecodeName(Array.Empty<byte>(),
        out var empty), "empty payload accepted");
    Equal(string.Empty, empty, "empty payload value");
    Equal(false, NativeRelationWireCodec.TryDecodeName("not raw", out _),
        "non-byte payload rejected");
    Equal(false, NativeRelationWireCodec.TryDecodeName(new byte[] { 0x81 },
        out _), "malformed GBK rejected");
}

static void CheckWireRecords()
{
    var queryHeader = Grobal2.MakeDefaultMsg(
        Grobal2.SM_SEND_RELATION_FRIEND, 0, 0, 0, 2);
    Equal((ushort)Grobal2.SM_SEND_RELATION_FRIEND, queryHeader.Ident,
        "query header ident");
    Equal(0, queryHeader.Recog, "query header Recog");
    Equal((ushort)0, queryHeader.Param, "query header Param");
    Equal((ushort)0, queryHeader.Tag, "query header Tag");
    Equal((ushort)2, queryHeader.Series, "query count in Series");

    var failureHeader = Grobal2.MakeDefaultMsg(
        Grobal2.SM_ADD_RELATION_FRIEND_FAIL, -4, 0, 0, 0);
    Equal(-4, failureHeader.Recog, "status result in Recog");
    Equal((ushort)0, failureHeader.Param, "status Param zero");
    Equal((ushort)0, failureHeader.Tag, "status Tag zero");
    Equal((ushort)0, failureHeader.Series, "status Series zero");

    var friend = NativeRelationWireCodec.Encode(NativeRelationKind.Friend,
        new[]
        {
            new NativeRelationWireEntry("好友", 0x1234, 2, 99,
                "Guild", true)
        });
    Equal(NativeRelationWireCodec.FriendRecordSize, friend.Length,
        "friend record size");
    FixedGbk(friend, 0, "好友", "friend name");
    Equal((byte)0, friend[15], "friend name pad");
    Equal((byte)0x34, friend[16], "friend level low");
    Equal((byte)0x12, friend[17], "friend level high");
    Equal((byte)2, friend[18], "friend job");
    FixedGbk(friend, 19, "Guild", "friend guild");
    Equal((byte)0, friend[34], "friend guild pad");
    Equal((byte)1, friend[35], "friend online");

    var attention = NativeRelationWireCodec.Encode(
        NativeRelationKind.Attention, new[]
        {
            new NativeRelationWireEntry("focus", 513, 1, 255,
                string.Empty, false)
        });
    Equal(NativeRelationWireCodec.AttentionRecordSize, attention.Length,
        "attention record size");
    Equal((byte)1, attention[16], "attention level low");
    Equal((byte)2, attention[17], "attention level high");
    Equal((byte)1, attention[18], "attention job");
    Equal((byte)255, attention[19], "attention color");
    Equal((byte)0, attention[20], "attention offline");
    Equal((byte)0, attention[21], "attention tail pad");

    var blacklist = NativeRelationWireCodec.Encode(
        NativeRelationKind.Blacklist, new[]
        {
            new NativeRelationWireEntry("blocked", 300, 2, 12,
                "ignored", true)
        });
    Equal(NativeRelationWireCodec.BlacklistRecordSize, blacklist.Length,
        "blacklist record size");
    Equal((byte)44, blacklist[16], "blacklist level low");
    Equal((byte)1, blacklist[17], "blacklist level high");
    Equal((byte)1, blacklist[18], "blacklist online");
    Equal((byte)0, blacklist[19], "blacklist tail pad");

    Equal(0, NativeRelationWireCodec.Encode(NativeRelationKind.Friend,
        Array.Empty<NativeRelationWireEntry>()).Length, "empty wire body");
}

static void CheckSourceContracts()
{
    var root = FindRoot();
    var player = ReadSource(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeRelationProtocol.cs"));
    var store = ReadSource(Path.Combine(root, "GameSvr", "Services",
        "NativeRelationStore.cs"));

    Require(player, "TryHandleNativeRelationProtocol(", "relation route");
    Require(player, "DecodeNativeSocialBody(payload)", "decoded payload ABI");
    Require(player, "(byte)processMessage.nParam2", "Param/color ABI");
    Require(player, "GetCachedNativeUserId()", "native user ID");
    Require(player, "QueueNativeGroupRequest(this, 2)",
        "friend request type 2");
    Require(player, "AcceptNativeFriend(TPlayObject accepter)",
        "group acceptance API");
    Require(player, "Grobal2.MakeDefaultMsg(opcode, 0, 0, 0,",
        "query count in Series");
    Forbid(player, "ObjectId", "relation player IDs must not use ObjectId");
    Forbid(player, "case Grobal2.SM_ADD_RELATION_FRIEND_FAIL",
        "4434 is response-only");

    Require(store, "BeginTransaction(", "relation mutations open a transaction");
    Require(store, "IsolationLevel.Serializable",
        "relation mutations use SERIALIZABLE isolation");
    Forbid(store, "IsolationLevel.ReadCommitted",
        "relation mutations dropped to read-committed");
    Forbid(store, "IsolationLevel.ReadUncommitted",
        "relation mutations dropped to read-uncommitted");
    Require(store, "FOR UPDATE", "locked relation rows");
    Require(store, "transaction.Commit()", "transaction commit");
    Require(store, "transaction.Rollback()", "transaction rollback");
    Require(store, "state == 0", "zero-state deletion branch");
    Require(store, "DELETE FROM gamedata.relation WHERE Idx=@idx",
        "relation row deletion");
    Require(store, "MySqlDbType.VarBinary", "GBK binary parameters");
    Require(store, "SecFocusColor=@color", "first-owner target color");
    Require(store, "FirstFocusColor=@color", "second-owner target color");
}

static string ReadSource(string path)
{
    return File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");
}

static NativeRelationPlayer Player(long id, string name, ushort level,
    byte job) => new(id, name, level, job);

static void FixedGbk(byte[] buffer, int offset, string expected, string label)
{
    var bytes = HUtil32.GbkEncoding.GetBytes(expected);
    for (var i = 0; i < bytes.Length; i++)
        Equal(bytes[i], buffer[offset + i], label + $" byte {i}");
}

static string FindRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory,
                 AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("LyoMir2 repository root not found");
}

static void Require(string text, string value, string label)
{
    if (!text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(label + " missing: " + value);
}

static void Forbid(string text, string value, string label)
{
    if (text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(label + " found: " + value);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, actual {actual}");
}

sealed class FakeRelationStore : INativeRelationStore
{
    internal readonly List<FakeRow> Rows = new();
    internal FakeRow OnlyRow => Rows.Single();
    internal int? ForcedInspectCount;
    internal bool? ForcedInspectContains;
    internal bool FailInspect;
    internal NativeRelationStoreResult? ForcedDirectedResult;
    internal NativeRelationStoreResult? ForcedFriendResult;
    internal NativeRelationStoreResult? ForcedRemoveResult;
    internal NativeRelationStoreResult? ForcedColorResult;

    public bool TryLoad(long ownerId, NativeRelationKind kind,
        out IReadOnlyList<NativeRelationEntry> entries)
    {
        entries = Rows.Where(row => row.Has(ownerId, kind))
            .Select(row => row.Other(ownerId)).OrderByDescending(row => row.Level)
            .ToArray();
        return true;
    }

    public bool TryInspect(long ownerId, long targetId,
        NativeRelationKind kind, out int count, out bool contains)
    {
        count = ForcedInspectCount ??
                Rows.Count(row => row.Has(ownerId, kind));
        contains = ForcedInspectContains ?? Rows.Any(row =>
            row.IsPair(ownerId, targetId) && row.Has(ownerId, kind));
        return !FailInspect;
    }

    public NativeRelationStoreResult TryAddDirected(NativeRelationPlayer owner,
        NativeRelationPlayer target, NativeRelationKind kind, byte focusColor,
        int limit)
    {
        if (ForcedDirectedResult.HasValue)
            return ForcedDirectedResult.Value;
        if (Rows.Count(row => row.Has(owner.UserId, kind)) >= limit)
            return NativeRelationStoreResult.Full;
        var row = Rows.FirstOrDefault(item =>
            item.IsPair(owner.UserId, target.UserId));
        if (row != null && row.Has(owner.UserId, kind))
            return NativeRelationStoreResult.Duplicate;
        if (row == null)
        {
            row = new FakeRow(owner, target);
            Rows.Add(row);
        }
        var ownerFirst = row.First.UserId == owner.UserId;
        row.State |= NativeRelationStateBits.ForOwner(kind, ownerFirst);
        if (kind == NativeRelationKind.Attention)
        {
            if (ownerFirst) row.SecondFocus = focusColor;
            else row.FirstFocus = focusColor;
        }
        return NativeRelationStoreResult.Success;
    }

    public NativeRelationStoreResult TryAddFriend(
        NativeRelationPlayer requester, NativeRelationPlayer accepter,
        int limit)
    {
        if (ForcedFriendResult.HasValue) return ForcedFriendResult.Value;
        var row = Rows.FirstOrDefault(item =>
            item.IsPair(requester.UserId, accepter.UserId));
        if (row != null && row.Has(requester.UserId,
                NativeRelationKind.Friend))
            return NativeRelationStoreResult.Duplicate;
        if (Rows.Count(item => item.Has(requester.UserId,
                    NativeRelationKind.Friend)) >= limit
            || Rows.Count(item => item.Has(accepter.UserId,
                    NativeRelationKind.Friend)) >= limit)
            return NativeRelationStoreResult.Full;
        if (row == null)
        {
            row = new FakeRow(requester, accepter);
            Rows.Add(row);
        }
        row.State |= NativeRelationStateBits.Friend;
        return NativeRelationStoreResult.Success;
    }

    public NativeRelationStoreResult TryRemove(long ownerId,
        string targetName, NativeRelationKind kind)
    {
        if (ForcedRemoveResult.HasValue) return ForcedRemoveResult.Value;
        var row = Rows.FirstOrDefault(item => item.Has(ownerId, kind)
            && string.Equals(item.Other(ownerId).Name, targetName,
                StringComparison.OrdinalIgnoreCase));
        if (row == null) return NativeRelationStoreResult.Missing;
        row.State &= ~NativeRelationStateBits.ForOwner(kind,
            row.First.UserId == ownerId);
        if (row.State == 0) Rows.Remove(row);
        return NativeRelationStoreResult.Success;
    }

    public NativeRelationStoreResult TryUpdateAttentionColor(long ownerId,
        string targetName, byte color)
    {
        if (ForcedColorResult.HasValue) return ForcedColorResult.Value;
        var row = Rows.FirstOrDefault(item =>
            item.Has(ownerId, NativeRelationKind.Attention)
            && string.Equals(item.Other(ownerId).Name, targetName,
                StringComparison.OrdinalIgnoreCase));
        if (row == null) return NativeRelationStoreResult.Missing;
        if (row.First.UserId == ownerId) row.SecondFocus = color;
        else row.FirstFocus = color;
        return NativeRelationStoreResult.Success;
    }
}

sealed class FakeRow
{
    internal FakeRow(NativeRelationPlayer first, NativeRelationPlayer second)
    {
        First = first;
        Second = second;
    }

    internal NativeRelationPlayer First;
    internal NativeRelationPlayer Second;
    internal uint State;
    internal byte FirstFocus;
    internal byte SecondFocus;

    internal bool IsPair(long first, long second) =>
        First.UserId == first && Second.UserId == second
        || First.UserId == second && Second.UserId == first;

    internal bool Has(long owner, NativeRelationKind kind)
    {
        if (First.UserId != owner && Second.UserId != owner) return false;
        var bit = NativeRelationStateBits.ForOwner(kind,
            First.UserId == owner);
        return (State & bit) == bit;
    }

    internal NativeRelationEntry Other(long owner)
    {
        var ownerFirst = First.UserId == owner;
        var other = ownerFirst ? Second : First;
        return new NativeRelationEntry(other.UserId, other.Name, other.Level,
            other.Job, ownerFirst ? SecondFocus : FirstFocus);
    }
}
