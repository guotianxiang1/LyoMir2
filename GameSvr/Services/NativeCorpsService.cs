using SystemModule;

namespace GameSvr.Services
{
    internal sealed class NativeSocialPersistenceQueue
    {
        private sealed class WorkItem
        {
            internal WorkItem(NativeCorpsSnapshot corps)
            {
                Corps = corps;
            }

            internal WorkItem(NativeGildSnapshot gild)
            {
                Gild = gild;
            }

            internal NativeCorpsSnapshot Corps { get; }
            internal NativeGildSnapshot Gild { get; }
        }

        private readonly object _sync = new();
        private readonly Queue<WorkItem> _work = new();
        private readonly ManualResetEventSlim _idle = new(true);
        private readonly INativeCorpsStore _store;
        private bool _workerRunning;
        private bool _accepting = true;

        internal NativeSocialPersistenceQueue(INativeCorpsStore store)
        {
            _store = store;
        }

        internal void Enqueue(NativeCorpsSnapshot corps) =>
            Enqueue(new WorkItem(Clone(corps)));

        internal void Enqueue(NativeGildSnapshot gild)
            => Enqueue(new WorkItem(Clone(gild)));

        private void Enqueue(WorkItem item)
        {
            lock (_sync)
            {
                if (!_accepting)
                    throw new InvalidOperationException(
                        "native social persistence queue is closed");
                _idle.Reset();
                _work.Enqueue(item);
                if (_workerRunning) return;
                _workerRunning = true;
                _ = Task.Run(Drain);
            }
        }

        internal bool WaitForIdle(TimeSpan timeout) => _idle.Wait(timeout);

        internal void CompleteAndDrain()
        {
            lock (_sync) _accepting = false;
            _idle.Wait();
        }

        private void Drain()
        {
            while (true)
            {
                WorkItem item;
                lock (_sync)
                {
                    if (_work.Count == 0)
                    {
                        _workerRunning = false;
                        _idle.Set();
                        return;
                    }
                    item = _work.Dequeue();
                }

                string error;
                try
                {
                    var persisted = item.Corps != null
                        ? _store.TryUpdateCorps(item.Corps, out error)
                        : _store.TryUpdateGild(item.Gild, out error);
                    if (persisted) continue;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                try
                {
                    M2Share.MainOutMessage("执行sql失败:" + error);
                }
                catch
                {
                    // Persistence logging must not stop the FIFO worker.
                }
            }
        }

        private static NativeCorpsSnapshot Clone(
            NativeCorpsSnapshot source) => new()
        {
            Id = source.Id,
            CreateTime = source.CreateTime,
            Name = source.Name,
            OwnerId = source.OwnerId,
            ViceOwner1Id = source.ViceOwner1Id,
            ViceOwner2Id = source.ViceOwner2Id,
            BanRecruit = source.BanRecruit,
            RecruitLevelLimit = source.RecruitLevelLimit,
            RecruitJobSet = source.RecruitJobSet,
            Notice = (byte[])(source.Notice ?? Array.Empty<byte>()).Clone()
        };

        private static NativeGildSnapshot Clone(NativeGildSnapshot source)
        {
            var snapshot = new NativeGildSnapshot
            {
                Id = source.Id,
                CreateTime = source.CreateTime,
                Name = source.Name,
                OwnerCorpsId = source.OwnerCorpsId,
                ViceOwnerId = source.ViceOwnerId,
                Notice = (byte[])source.Notice.Clone()
            };
            snapshot.CorpsIds.AddRange(source.CorpsIds);
            return snapshot;
        }
    }

    internal sealed class NativeCorpsService
    {
        internal const byte GildUnion = 1;
        internal const byte GildHostile = 2;
        // Pending (unaccepted) union relation type. save_relation(sub_5E6E60) writes this on a 4573
        // request BEFORE acceptance. It goes into the relation map like every other type
        // (0x5E6F1B `8ACB mov cl,bl` / 0x5E6F23 `call 0x49F9C8`) but takes neither list: the dispatch at
        // 0x5E6F45/0x5E6F49 is two `FECB dec bl` + `je`, so only 1 and 2 reach a list append.
        internal const byte GildPendingUnion = 3;
        internal const int UnknownError = 1000;
        internal const int PermissionDenied = 555;
        internal const int MaximumPageSize = 32;
        internal const int MaximumMembers = 30;
        internal const int MaximumCorpsNoticeInputBytes = 500;
        internal const int MaximumCorpsNoticeStoredBytes = 230;
        internal const int MaximumGildNoticeBytes = 200;

        private sealed class Application
        {
            internal Application(long corpsId, NativeCorpsActor actor)
            {
                CorpsId = corpsId;
                Actor = actor;
                CreatedUtc = DateTime.UtcNow;
            }

            internal long CorpsId { get; }
            internal NativeCorpsActor Actor { get; }
            internal DateTime CreatedUtc { get; }
        }

        private readonly object _sync = new();
        private readonly INativeCorpsStore _store;
        private readonly INativeGildStore _gildStore;
        private readonly NativeSocialPersistenceQueue _persistence;
        private readonly Dictionary<long, NativeCorpsSnapshot> _corpsById;
        private readonly Dictionary<long, NativeGildSnapshot> _gildById;
        private readonly Dictionary<(ulong First, ulong Second), (byte Relation, DateTime CreateTime)>
            _gildRelations;
        private readonly Dictionary<long, long> _memberToCorps = new();
        private readonly Dictionary<long, long> _corpsToGild = new();
        private readonly Dictionary<long, Application> _applications = new();
        private readonly Dictionary<(long CorpsId, int Type),
            List<NativeCorpsLogEntry>> _logs = new();
        private readonly Dictionary<long, NativeGildConcernSet> _gildConcerns =
            new();
        private readonly Dictionary<string, long> _gildIdByUpperName = new();
        private readonly NativeGildIdAllocator _gildIdAllocator = new();
        private readonly NativeGildRequestLedger _requestLedger = new();

        private NativeCorpsService()
        {
            _corpsById = new Dictionary<long, NativeCorpsSnapshot>();
            _gildById = new Dictionary<long, NativeGildSnapshot>();
            _gildRelations = new Dictionary<(ulong, ulong), (byte, DateTime)>();
        }

        private NativeCorpsService(INativeCorpsStore store,
            NativeCorpsDataSnapshot snapshot, INativeGildStore gildStore)
        {
            _store = store;
            _gildStore = gildStore;
            _persistence = new NativeSocialPersistenceQueue(store);
            _corpsById = snapshot.CorpsById;
            _gildById = snapshot.GildById;
            _gildRelations = snapshot.GildRelations;
            RebuildIndexes();
            SeedGildConcernsLocked(snapshot.GildConcerns);
        }

        internal static NativeCorpsService Unavailable { get; } = new();

        internal bool IsAvailable => _store != null;
        internal bool SupportsGildWrites => _gildStore != null;
        internal int CorpsCount => _corpsById.Count;
        internal int GildCount => _gildById.Count;

        internal static bool TryCreate(INativeCorpsStore store,
            out NativeCorpsService service, out string error,
            INativeGildStore gildStore = null)
        {
            service = Unavailable;
            error = string.Empty;
            if (store == null)
            {
                error = "native Corps store is missing";
                return false;
            }
            if (!store.TryLoad(out var snapshot, out error)) return false;
            try
            {
                service = new NativeCorpsService(store, snapshot, gildStore);
                return true;
            }
            catch (Exception ex)
            {
                service = Unavailable;
                error = "native Corps index rebuild failed: " + ex.Message;
                return false;
            }
        }

        // GILD-27: Expire wars based on CreateTime + duration. Wars (Relation=2) expire
        // after dwGuildWarTime (default 3 hours). This is called from GameServer Phase4.
        // The native equivalent is AssociationManager.Run() line ~159 which checks
        // (GetTickCount()-dwWarTick) > dwWarTime for the file-based system.
        internal void ExpireGildWars(int durationMs)
        {
            if (!SupportsGildWrites) return;

            lock (_sync)
            {
                var now = DateTime.Now;
                var expired = NativeGildWarExpiry.GetExpired(_gildRelations, now, durationMs);

                foreach (var war in expired)
                {
                    var relationKey = NativeCorpsDataSnapshot.GildRelationKey(
                        war.FirstGildId, war.SecondGildId);
                    RemoveGildRelationLocked(relationKey);
                }
            }
        }

        internal string GetPlayerGildName(long playerId)
        {
            lock (_sync)
            {
                return TryGetPlayerGildLocked(playerId, out var gild)
                    ? gild.Name
                    : string.Empty;
            }
        }

        internal bool TryGetPlayerCorps(long playerId,
            out NativeCorpsSnapshot corps)
        {
            lock (_sync)
            {
                corps = null;
                return _memberToCorps.TryGetValue(playerId, out var corpsId)
                       && _corpsById.TryGetValue(corpsId, out corps);
            }
        }

        internal bool TryGetCorps(long corpsId, out NativeCorpsSnapshot corps)
        {
            lock (_sync) return _corpsById.TryGetValue(corpsId, out corps);
        }

        internal bool TryGetGildForPlayer(long playerId,
            out NativeGildSnapshot gild)
        {
            lock (_sync) return TryGetPlayerGildLocked(playerId, out gild);
        }

        internal bool TryGetGildForCorps(long corpsId,
            out NativeGildSnapshot gild)
        {
            lock (_sync)
            {
                gild = null;
                return _corpsToGild.TryGetValue(corpsId, out var gildId)
                       && _gildById.TryGetValue(gildId, out gild);
            }
        }

        internal void GetCombatRelation(long selfPlayerId,
            long targetPlayerId, out bool selfHasCorps,
            out bool targetHasCorps, out bool sameCorps,
            out bool selfHasGild, out bool targetHasGild,
            out bool sameGild, out byte gildRelation)
        {
            lock (_sync)
            {
                selfHasCorps = false;
                targetHasCorps = false;
                sameCorps = false;
                selfHasGild = false;
                targetHasGild = false;
                sameGild = false;
                gildRelation = 0;
                if (selfPlayerId == 0 || targetPlayerId == 0) return;

                selfHasCorps = _memberToCorps.TryGetValue(selfPlayerId,
                    out var selfCorpsId);
                targetHasCorps = _memberToCorps.TryGetValue(
                    targetPlayerId, out var targetCorpsId);
                sameCorps = selfHasCorps && targetHasCorps
                            && selfCorpsId == targetCorpsId;

                var selfGildId = 0L;
                var targetGildId = 0L;
                selfHasGild = selfHasCorps
                               && _corpsToGild.TryGetValue(selfCorpsId,
                                   out selfGildId);
                targetHasGild = targetHasCorps
                                 && _corpsToGild.TryGetValue(targetCorpsId,
                                     out targetGildId);
                if (!selfHasGild || !targetHasGild) return;
                sameGild = selfGildId == targetGildId;
                if (sameGild) return;
                _gildRelations.TryGetValue(
                    NativeCorpsDataSnapshot.GildRelationKey(selfGildId,
                        targetGildId), out var relationTuple);
                gildRelation = relationTuple.Relation;
            }
        }

        internal IReadOnlyList<NativeCorpsSnapshot> GetCorpsPage(int page,
            int pageSize, out int result)
        {
            lock (_sync)
            {
                result = 0;
                if (!IsAvailable)
                {
                    result = UnknownError;
                    return Array.Empty<NativeCorpsSnapshot>();
                }
                var ordered = _corpsById.Values.OrderBy(value => value.Id)
                    .ToArray();
                var start = (long)page * pageSize;
                if (start >= ordered.Length)
                {
                    if (ordered.Length != 0 || page != 0) result = 30;
                    return Array.Empty<NativeCorpsSnapshot>();
                }
                return ordered.Skip(unchecked((int)start)).Take(pageSize)
                    .ToArray();
            }
        }

        internal IReadOnlyList<NativeCorpsMemberSnapshot> GetMemberPage(
            long corpsId, int page, int pageSize, out int result)
        {
            lock (_sync)
            {
                result = 0;
                if (!_corpsById.TryGetValue(corpsId, out var corps))
                {
                    result = 5;
                    return Array.Empty<NativeCorpsMemberSnapshot>();
                }
                var start = (long)page * pageSize;
                if (start >= corps.Members.Count)
                    return Array.Empty<NativeCorpsMemberSnapshot>();
                return corps.Members.Skip(unchecked((int)start))
                    .Take(pageSize)
                    .OrderBy(member => GetPositionLocked(corps, member.MemberId))
                    .ThenBy(member => member.MemberId).ToArray();
            }
        }

        internal byte GetPosition(NativeCorpsSnapshot corps, long memberId)
        {
            lock (_sync) return GetPositionLocked(corps, memberId);
        }

        internal string GetCaptainName(NativeCorpsSnapshot corps)
        {
            lock (_sync)
            {
                return corps?.Members.FirstOrDefault(member =>
                           member.MemberId == corps.OwnerId)?.Name
                       ?? string.Empty;
            }
        }

        internal bool TryGetApplicationCorps(long playerId,
            out NativeCorpsSnapshot corps)
        {
            lock (_sync)
            {
                corps = null;
                return _applications.TryGetValue(playerId, out var application)
                       && _corpsById.TryGetValue(application.CorpsId,
                           out corps);
            }
        }

        internal int RequestJoin(NativeCorpsActor actor, long corpsId)
        {
            lock (_sync)
            {
                if (!IsAvailable || actor.Id == 0) return UnknownError;
                if (_memberToCorps.ContainsKey(actor.Id)) return 3;
                if (!_corpsById.TryGetValue(corpsId, out var corps)) return 7;
                if (_applications.ContainsKey(actor.Id)) return 8;
                if (corps.BanRecruit
                    || actor.Level < corps.RecruitLevelLimit
                    || (corps.RecruitJobSet != 0
                        && (corps.RecruitJobSet & (1 << actor.Job)) == 0))
                    return 9;
                if (corps.Members.Count >= MaximumMembers) return 16;
                _applications.Add(actor.Id, new Application(corpsId, actor));
                AddLogLocked(corpsId, 1,
                    $"{actor.Name} requested membership");
                return 0;
            }
        }

        internal int CancelJoin(long playerId)
        {
            lock (_sync)
            {
                if (!IsAvailable || playerId == 0) return UnknownError;
                if (!_applications.Remove(playerId, out var application))
                    return 10;
                AddLogLocked(application.CorpsId, 1,
                    $"{application.Actor.Name} canceled membership request");
                return 0;
            }
        }

        internal IReadOnlyList<NativeCorpsActor> GetRequestPage(
            long operatorId, int page, int pageSize)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps)
                    || !IsOfficer(corps, operatorId))
                    return Array.Empty<NativeCorpsActor>();
                var start = (long)page * pageSize;
                if (start > int.MaxValue)
                    return Array.Empty<NativeCorpsActor>();
                return _applications.Values
                    .Where(value => value.CorpsId == corps.Id)
                    .OrderBy(value => value.CreatedUtc)
                    .Skip(unchecked((int)start)).Take(pageSize)
                    .Select(value => value.Actor).ToArray();
            }
        }

        internal int AcceptRequest(long operatorId, long applicantId)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (!IsOfficer(corps, operatorId)) return PermissionDenied;
                if (!_applications.TryGetValue(applicantId,
                        out var application)
                    || application.CorpsId != corps.Id)
                    return 10;
                if (_memberToCorps.ContainsKey(applicantId)) return 3;
                if (corps.Members.Count >= MaximumMembers) return 16;

                var member = new NativeCorpsMemberSnapshot
                {
                    MemberId = application.Actor.Id,
                    Name = application.Actor.Name,
                    Level = application.Actor.Level,
                    Sex = application.Actor.Sex,
                    Job = application.Actor.Job,
                    LastLoginTime = DateTime.Now
                };
                if (!_store.TryInsertMember(corps.Id, member, out _))
                    return UnknownError;
                corps.Members.Add(member);
                _memberToCorps.Add(member.MemberId, corps.Id);
                _applications.Remove(applicantId);
                AddLogLocked(corps.Id, 1,
                    $"{member.Name} joined the Corps");
                return 0;
            }
        }

        internal int DirectAddMember(long operatorId, NativeCorpsActor actor)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (!IsOfficer(corps, operatorId)) return PermissionDenied;
                if (actor.Id == 0) return UnknownError;
                if (_memberToCorps.ContainsKey(actor.Id)) return 3;
                if (corps.Members.Count >= MaximumMembers) return 16;

                var member = new NativeCorpsMemberSnapshot
                {
                    MemberId = actor.Id,
                    Name = actor.Name,
                    Level = actor.Level,
                    Sex = actor.Sex,
                    Job = actor.Job,
                    LastLoginTime = DateTime.Now
                };
                if (!_store.TryInsertMember(corps.Id, member, out _))
                    return UnknownError;
                corps.Members.Add(member);
                _memberToCorps.Add(member.MemberId, corps.Id);
                _applications.Remove(member.MemberId);
                AddLogLocked(corps.Id, 1,
                    $"{member.Name} joined the Corps");
                return 0;
            }
        }

        internal int RefuseRequest(long operatorId, long applicantId)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (!IsOfficer(corps, operatorId)) return PermissionDenied;
                if (!_applications.TryGetValue(applicantId,
                        out var application)
                    || application.CorpsId != corps.Id)
                    return 10;
                _applications.Remove(applicantId);
                AddLogLocked(corps.Id, 1,
                    $"{application.Actor.Name} membership request refused");
                return 0;
            }
        }

        internal int SetMemberTitle(long operatorId, long memberId,
            string title)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (!IsOfficer(corps, operatorId)) return PermissionDenied;
                var member = corps.Members.FirstOrDefault(value =>
                    value.MemberId == memberId);
                if (member == null) return 18;
                if (!_store.TryUpdateMemberTitle(memberId, title, out _))
                    return UnknownError;
                member.Title = title ?? string.Empty;
                AddLogLocked(corps.Id, 1,
                    $"{member.Name} title changed");
                return 0;
            }
        }

        internal int DismissMember(long operatorId, long memberId)
        {
            lock (_sync)
            {
                if (operatorId == 0 || memberId == 0) return PermissionDenied;
                if (operatorId == memberId) return 19;
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (!IsOfficer(corps, operatorId)) return PermissionDenied;
                if (memberId == corps.OwnerId || memberId == corps.ViceOwner1Id
                    || memberId == corps.ViceOwner2Id)
                    return PermissionDenied;
                var member = corps.Members.FirstOrDefault(value =>
                    value.MemberId == memberId);
                if (member == null) return 18;
                if (!_store.TryDeleteMember(memberId, out _))
                    return UnknownError;
                corps.Members.Remove(member);
                _memberToCorps.Remove(memberId);
                AddLogLocked(corps.Id, 1,
                    $"{member.Name} dismissed from the Corps");
                return 0;
            }
        }

        internal int TransferCaptain(long operatorId, long memberId)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (corps.OwnerId != operatorId) return PermissionDenied;
                if (operatorId == memberId) return 40;
                if (!corps.Members.Any(value => value.MemberId == memberId))
                    return 40;

                var oldOwner = corps.OwnerId;
                var oldVice1 = corps.ViceOwner1Id;
                var oldVice2 = corps.ViceOwner2Id;
                corps.OwnerId = memberId;
                if (corps.ViceOwner1Id == memberId)
                    corps.ViceOwner1Id = 0;
                else if (corps.ViceOwner2Id == memberId)
                    corps.ViceOwner2Id = 0;
                if (!_store.TryUpdateCorps(corps, out _))
                {
                    corps.OwnerId = oldOwner;
                    corps.ViceOwner1Id = oldVice1;
                    corps.ViceOwner2Id = oldVice2;
                    return UnknownError;
                }
                AddLogLocked(corps.Id, 1,
                    $"captain transferred to member {memberId}");
                return 0;
            }
        }

        internal int AppointViceCaptain(long operatorId, long memberId)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (corps.OwnerId != operatorId) return PermissionDenied;
                if (!corps.Members.Any(value => value.MemberId == memberId))
                    return 18;
                if (memberId == corps.OwnerId || memberId == corps.ViceOwner1Id
                    || memberId == corps.ViceOwner2Id)
                    return 31;

                var oldVice1 = corps.ViceOwner1Id;
                var oldVice2 = corps.ViceOwner2Id;
                if (corps.ViceOwner1Id == 0)
                    corps.ViceOwner1Id = memberId;
                else if (corps.ViceOwner2Id == 0)
                    corps.ViceOwner2Id = memberId;
                else
                    return 21;
                if (!_store.TryUpdateCorps(corps, out _))
                {
                    corps.ViceOwner1Id = oldVice1;
                    corps.ViceOwner2Id = oldVice2;
                    return UnknownError;
                }
                AddLogLocked(corps.Id, 1,
                    $"member {memberId} appointed vice captain");
                return 0;
            }
        }

        internal int DismissViceCaptain(long operatorId, long memberId)
        {
            lock (_sync)
            {
                if (memberId == 0) return 18;
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (corps.OwnerId == operatorId)
                {
                    if (operatorId == memberId) return PermissionDenied;
                    if (!corps.Members.Any(value =>
                            value.MemberId == memberId))
                        return 18;
                }
                else if ((corps.ViceOwner1Id == operatorId
                          || corps.ViceOwner2Id == operatorId)
                         && operatorId == memberId)
                {
                    // Vice captains may use 4540 to demote themselves.
                }
                else
                {
                    return PermissionDenied;
                }
                var oldVice1 = corps.ViceOwner1Id;
                var oldVice2 = corps.ViceOwner2Id;
                if (corps.ViceOwner1Id == memberId)
                    corps.ViceOwner1Id = 0;
                else if (corps.ViceOwner2Id == memberId)
                    corps.ViceOwner2Id = 0;
                else
                    return UnknownError;
                if (!_store.TryUpdateCorps(corps, out _))
                {
                    corps.ViceOwner1Id = oldVice1;
                    corps.ViceOwner2Id = oldVice2;
                    return UnknownError;
                }
                AddLogLocked(corps.Id, 1,
                    $"member {memberId} dismissed as vice captain");
                return 0;
            }
        }

        internal int StepDown(long operatorId)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (corps.OwnerId == operatorId) return PermissionDenied;
                var oldVice1 = corps.ViceOwner1Id;
                var oldVice2 = corps.ViceOwner2Id;
                if (corps.ViceOwner1Id == operatorId)
                    corps.ViceOwner1Id = 0;
                else if (corps.ViceOwner2Id == operatorId)
                    corps.ViceOwner2Id = 0;
                else
                    return UnknownError;
                if (!_store.TryUpdateCorps(corps, out _))
                {
                    corps.ViceOwner1Id = oldVice1;
                    corps.ViceOwner2Id = oldVice2;
                    return UnknownError;
                }
                AddLogLocked(corps.Id, 1,
                    $"member {operatorId} stepped down");
                return 0;
            }
        }

        internal int SetRecruitCondition(long operatorId,
            NativeCorpsRecruitCondition condition)
        {
            lock (_sync)
            {
                if (operatorId == 0) return PermissionDenied;
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return PermissionDenied;
                if (!IsOfficer(corps, operatorId)) return PermissionDenied;
                corps.RecruitJobSet = unchecked((byte)(condition.Jobs & 0x07));
                corps.RecruitLevelLimit = condition.Level;
                corps.NoticeText = condition.Notice;
                _persistence.Enqueue(corps);
                return 0;
            }
        }

        internal int SetNotice(long operatorId, byte[] noticeBody)
        {
            noticeBody ??= Array.Empty<byte>();
            var length = Array.IndexOf(noticeBody, (byte)0);
            if (length < 0) length = noticeBody.Length;
            var result = length > MaximumCorpsNoticeInputBytes ? 24 : 0;
            var storedLength = Math.Min(length,
                MaximumCorpsNoticeStoredBytes);
            var notice = new byte[storedLength];
            for (var index = 0; index < storedLength; index++)
            {
                var value = noticeBody[index];
                notice[index] = value == 0x27 ? (byte)0x60 : value;
            }

            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (!IsOfficer(corps, operatorId)) return PermissionDenied;
                corps.Notice = notice;
                _persistence.Enqueue(corps);
                return result;
            }
        }

        internal int SetGildNotice(long operatorId, string notice) =>
            SetGildNotice(operatorId, HUtil32.GbkEncoding.GetBytes(
                notice ?? string.Empty));

        internal int SetGildNotice(long operatorId, byte[] noticeBody)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps)
                    || !_corpsToGild.TryGetValue(corps.Id, out var gildId)
                    || !_gildById.ContainsKey(gildId))
                    return 5;
                var position = GetPositionLocked(corps, operatorId);
                if (position != 3 && position != 4)
                    return PermissionDenied;
            }

            noticeBody ??= Array.Empty<byte>();
            var length = Array.IndexOf(noticeBody, (byte)0);
            if (length < 0) length = noticeBody.Length;
            if (length > MaximumGildNoticeBytes) return 24;
            var notice = new byte[length];
            for (var index = 0; index < length; index++)
            {
                var value = noticeBody[index];
                notice[index] = value == 0x27 ? (byte)0x60 : value;
            }

            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(operatorId, out var corps))
                    return 5;
                if (!_corpsToGild.TryGetValue(corps.Id, out var gildId)
                    || !_gildById.TryGetValue(gildId, out var gild))
                    return 12;
                gild.Notice = notice;
                _persistence.Enqueue(gild);
                return 0;
            }
        }

        internal bool WaitForGildPersistenceForTests(TimeSpan timeout) =>
            _persistence == null || _persistence.WaitForIdle(timeout);

        internal bool WaitForCorpsPersistenceForTests(TimeSpan timeout) =>
            _persistence == null || _persistence.WaitForIdle(timeout);

        internal void ShutdownAndDrainGildPersistence() =>
            _persistence?.CompleteAndDrain();

        // Live routing for the president-only Gild leadership write ops
        // 4567 dismiss-corps / 4568 transfer-president / 4569 appoint-vice
        // (native sub_704AF8 / sub_7046A8 / sub_7039F8, gild_owner strategy
        // slots +0x54 / +0x48 / +0x50). The wire target is a CORPS id. The
        // context is built from live state and classified by the reversed pure
        // ladder NativeGildLeadershipTransaction.Evaluate; on Success the
        // in-memory Gild is mutated and the change is pushed to
        // INativeGildStore fail-safe: a failure/exception is logged as
        // "[SQL Failed] " with NO rollback (the original only checks the
        // ExecuteScript boolean and never rolls back the already-published
        // in-memory change). Callers gate on SupportsGildWrites, so a server
        // with no Gild store keeps the original fail-closed response.
        internal int ApplyGildLeadership(NativeGildLeadershipOp op,
            long operatorId, long targetCorpsId)
        {
            lock (_sync)
            {
                var context = BuildGildLeadershipContextLocked(operatorId,
                    targetCorpsId, out var gild);
                var result = NativeGildLeadershipTransaction.Evaluate(op,
                    context);
                if (result != NativeGildLeadershipTransaction.Success)
                    return result;

                switch (op)
                {
                    case NativeGildLeadershipOp.DismissCorps:
                        gild.CorpsIds.Remove(targetCorpsId);
                        _corpsToGild.Remove(targetCorpsId);
                        DeleteGildMemberFailSafe(gild.Id, targetCorpsId);
                        break;
                    case NativeGildLeadershipOp.TransferPresident:
                        gild.OwnerCorpsId = targetCorpsId;
                        if (gild.ViceOwnerId == targetCorpsId)
                            gild.ViceOwnerId = 0;
                        SaveGildFailSafe(gild);
                        break;
                    case NativeGildLeadershipOp.AppointVice:
                        gild.ViceOwnerId = targetCorpsId;
                        SaveGildFailSafe(gild);
                        break;
                }
                return NativeGildLeadershipTransaction.Success;
            }
        }

        // Derives the reversed NativeGildLeadershipContext from live Corps/Gild
        // state for caller <paramref name="operatorId"/> acting on target CORPS
        // <paramref name="targetCorpsId"/>. Role is the caller's Gild position
        // (GetPositionLocked: 4=president, 3=vice); the transaction only lets
        // GildOwner through, matching the native gild_owner strategy dispatch.
        private NativeGildLeadershipContext BuildGildLeadershipContextLocked(
            long operatorId, long targetCorpsId, out NativeGildSnapshot gild)
        {
            gild = null;
            var hasCaller = TryGetPlayerCorpsLocked(operatorId,
                out var callerCorps);
            NativeGildSnapshot callerGild = null;
            var hasGild = hasCaller
                          && _corpsToGild.TryGetValue(callerCorps.Id,
                              out var callerGildId)
                          && _gildById.TryGetValue(callerGildId,
                              out callerGild);
            gild = callerGild;

            var position = hasCaller
                ? GetPositionLocked(callerCorps, operatorId)
                : (byte)0;
            var role = !hasGild
                ? (hasCaller ? NativeGildRole.Corps : NativeGildRole.NoCorps)
                : position == 4
                    ? NativeGildRole.GildOwner
                    : position == 3
                        ? NativeGildRole.GildVice
                        : NativeGildRole.GildMember;

            var targetInThisGild = hasGild
                                    && callerGild.CorpsIds.Contains(
                                        targetCorpsId);
            var targetSameGild = hasGild
                                  && _corpsToGild.TryGetValue(targetCorpsId,
                                      out var targetGildId)
                                  && targetGildId == callerGild.Id;

            return new NativeGildLeadershipContext
            {
                Role = role,
                ValidArgs = targetCorpsId != 0,
                HasPlayer = hasCaller,
                IsPresident = position == 4,
                HasGild = hasGild,
                ViceOccupied = hasGild && callerGild.ViceOwnerId != 0,
                TargetIsSelf = hasCaller && targetCorpsId == callerCorps.Id,
                TargetFound = _corpsById.ContainsKey(targetCorpsId),
                TargetSameGild = targetSameGild,
                TargetIsMember = targetInThisGild,
                TargetIsLeadership = hasGild
                                     && (targetCorpsId
                                             == callerGild.OwnerCorpsId
                                         || targetCorpsId
                                             == callerGild.ViceOwnerId),
                TargetRemovable = targetInThisGild,
                RemoveOk = targetInThisGild
            };
        }

        private void SaveGildFailSafe(NativeGildSnapshot gild)
        {
            try
            {
                if (!_gildStore.TrySaveGild(gild.Id, gild.OwnerCorpsId,
                        gild.ViceOwnerId, gild.Notice ?? Array.Empty<byte>(),
                        out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        private void DeleteGildMemberFailSafe(long gildId, long corpsId)
        {
            try
            {
                if (!_gildStore.TryDeleteGildMember(gildId, corpsId,
                        out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        // Live routing for 4574 break-union (native sub_703CEC, gild_owner
        // strategy slot +0x68; president-only — the vice +0x68 slot is a 555
        // stub). The wire target is another GILD id (sub_5E76D4 gild lookup).
        // Context is built from live state and classified by the reversed pure
        // ladder NativeGildUnionConcernTransaction.Evaluate; on Success the
        // in-memory union relation is removed and a DELETE gamedata.gildrelation
        // is pushed to INativeGildStore fail-safe (no rollback). The relation
        // key is normalized GildID1<=GildID2 at this service layer (matching the
        // native save_relation swap and NativeCorpsDataSnapshot.GildRelationKey);
        // the store DELETEs exactly that ordered pair. Requires a Gild store;
        // callers gate on SupportsGildWrites for the fail-closed fallback.
        internal int ApplyGildBreakUnion(long operatorId, long targetGildId)
        {
            lock (_sync)
            {
                var context = BuildBreakUnionContextLocked(operatorId,
                    targetGildId, out var relationKey);
                var result = NativeGildUnionConcernTransaction.Evaluate(
                    NativeGildUnionConcernOp.BreakUnion, context);
                if (result != NativeGildUnionConcernTransaction.Success)
                    return result;

                RemoveGildRelationLocked(relationKey);
                return NativeGildUnionConcernTransaction.Success;
            }
        }

        private NativeGildUnionConcernContext BuildBreakUnionContextLocked(
            long operatorId, long targetGildId,
            out (ulong First, ulong Second) relationKey)
        {
            relationKey = default;
            var hasCaller = TryGetPlayerCorpsLocked(operatorId,
                out var callerCorps);
            NativeGildSnapshot callerGild = null;
            var hasGild = hasCaller
                          && _corpsToGild.TryGetValue(callerCorps.Id,
                              out var callerGildId)
                          && _gildById.TryGetValue(callerGildId,
                              out callerGild);

            var position = hasCaller
                ? GetPositionLocked(callerCorps, operatorId)
                : (byte)0;
            var role = !hasGild
                ? (hasCaller ? NativeGildRole.Corps : NativeGildRole.NoCorps)
                : position == 4
                    ? NativeGildRole.GildOwner
                    : position == 3
                        ? NativeGildRole.GildVice
                        : NativeGildRole.GildMember;

            // sub_5E76D4 target-gild lookup (self is NOT excluded here; a self
            // target simply has no union relation and falls through to 27).
            var otherGildFound = hasGild
                                  && _gildById.ContainsKey(targetGildId);
            var allied = false;
            if (otherGildFound)
            {
                relationKey = NativeCorpsDataSnapshot.GildRelationKey(
                    callerGild.Id, targetGildId);
                allied = _gildRelations.TryGetValue(relationKey, out var relationTuple)
                         && relationTuple.Relation == GildUnion;
            }

            return new NativeGildUnionConcernContext
            {
                Role = role,
                HasPlayer = hasCaller,
                HasGild = hasGild,
                OtherGildFound = otherGildFound,
                Allied = allied,
                // A present union relation is always removable in-memory, so
                // the reversed WriteFailed(1000) branch is unreachable here
                // (dictionary removal cannot fail) — the store DELETE is
                // fire-and-forget fail-safe, exactly as the original.
                RelationRemovable = allied
            };
        }

        // delete_relation sub_5E90A4: 0x5E9105 `8A45F8 mov al,[ebp-8]` /
        // 0x5E9108 `2C04 sub al,4` / 0x5E910A `jae` bails only on >= 4, so the
        // whole 0..3 domain is removed from the relation map
        // (0x5E9116 `call 0x49FBD4`) and DELETEd. The union/hostile list
        // unlink at 0x5E9161/0x5E9165 (`FEC8 dec al` / `je`) is 1/2 only, and
        // in C# those lists are derived from the same dictionary, so the
        // single Remove covers both.
        private void RemoveGildRelationLocked(
            (ulong First, ulong Second) relationKey)
        {
            _gildRelations.Remove(relationKey);
            DeleteGildRelationFailSafe(relationKey);
        }

        private void DeleteGildRelationFailSafe(
            (ulong First, ulong Second) relationKey)
        {
            try
            {
                if (!_gildStore.TryDeleteGildRelation(
                        unchecked((long)relationKey.First),
                        unchecked((long)relationKey.Second), out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        // Live routing for 4579 declare-war-by-id (native sub_6F68F0 -> gild_owner
        // strategy slot +0x6C sub_703F74; president-only). The wire target is
        // another GILD id. Context is built from live state and classified by
        // NativeGildDeclareWarTransaction.Evaluate. The 30000-gold gate + the
        // deduction are the CALLER's responsibility (player state m_nGold): the
        // caller passes hasGold and, on a DeductsGold result, removes the gold —
        // matching the native order (gold gate 36 BEFORE role dispatch; the
        // relation INSERT happens inside the strategy; gold is deducted only on
        // success; an async SQL failure neither refunds gold nor rolls back the
        // in-memory relation). On the success code this publishes the war
        // relation (Relation=2) and pushes INSERT gamedata.gildrelation fail-safe.
        // Relation key is normalized GildID1<=GildID2 at this layer (rule #4).
        internal int ApplyGildDeclareWar(NativeGildDeclareWarOp op,
            long operatorId, long targetGildId, bool hasGold)
        {
            lock (_sync)
                return ApplyGildDeclareWarLocked(op, operatorId, targetGildId,
                    hasGold, nameResolved: true);
        }

        // 4585 declare-war-by-name: sub_5E76F0 resolves the target GILD by NAME
        // (case-insensitive, over the full in-memory gild registry) before the
        // 4579 ladder. An unresolved name is code 12 (NameUnresolved).
        internal int ApplyGildDeclareWarByName(long operatorId, string name,
            bool hasGold)
        {
            lock (_sync)
            {
                var resolved = _gildIdByUpperName.TryGetValue(
                    NativeGildNameResolver.Normalize(name ?? string.Empty),
                    out var targetGildId);
                return ApplyGildDeclareWarLocked(
                    NativeGildDeclareWarOp.DeclareWarName, operatorId,
                    targetGildId, hasGold, resolved);
            }
        }

        private int ApplyGildDeclareWarLocked(NativeGildDeclareWarOp op,
            long operatorId, long targetGildId, bool hasGold, bool nameResolved)
        {
            var context = BuildDeclareWarContextLocked(op, operatorId,
                targetGildId, hasGold, nameResolved, out var relationKey);
            var result = NativeGildDeclareWarTransaction.Evaluate(op, context);
            if (!NativeGildDeclareWarTransaction.InsertsRelation(result))
                return result;

            _gildRelations[relationKey] = (GildHostile, DateTime.Now);
            InsertGildRelationFailSafe(relationKey, GildHostile, DateTime.Now);
            return result;
        }

        private NativeGildDeclareWarContext BuildDeclareWarContextLocked(
            NativeGildDeclareWarOp op, long operatorId, long targetGildId,
            bool hasGold, bool nameResolved,
            out (ulong First, ulong Second) relationKey)
        {
            relationKey = default;
            var hasCaller = TryGetPlayerCorpsLocked(operatorId,
                out var callerCorps);
            NativeGildSnapshot callerGild = null;
            var hasGild = hasCaller
                          && _corpsToGild.TryGetValue(callerCorps.Id,
                              out var callerGildId)
                          && _gildById.TryGetValue(callerGildId,
                              out callerGild);
            var role = ResolveGildRoleLocked(hasCaller, hasGild,
                hasCaller ? GetPositionLocked(callerCorps, operatorId)
                    : (byte)0);

            var targetFound = hasGild && _gildById.ContainsKey(targetGildId);
            var targetIsSelf = hasGild && targetGildId == callerGild.Id;
            var relationState = 0;
            if (targetFound && !targetIsSelf)
            {
                relationKey = NativeCorpsDataSnapshot.GildRelationKey(
                    callerGild.Id, targetGildId);
                if (_gildRelations.TryGetValue(relationKey, out var relationTuple))
                    relationState = relationTuple.Relation;
            }

            return new NativeGildDeclareWarContext
            {
                Role = role,
                // 4579 (by id) ignores NameResolved; 4585 (by name) sets it from
                // the gild-name registry lookup (unresolved -> 12).
                NameResolved = nameResolved,
                HasGold = hasGold,
                CallerKeyPresent = operatorId != 0,
                PlayerResolved = hasCaller,
                HasGild = hasGild,
                TargetGildFound = targetFound,
                TargetIsSelf = targetIsSelf,
                RelationState = relationState,
                // save_relation(type=2) re-reads the pair through sub_49FCB8 and
                // returns 15 for any existing 1/2/3; 0x704060 `8BF8 mov edi,eax`
                // propagates that verbatim. Only a pair reading 0 INSERTs.
                RelationHelperResult = relationState == 0
                    ? NativeGildDeclareWarTransaction.Success
                    : NativeGildDeclareWarTransaction
                        .SaveRelationRelationExists
            };
        }

        private void InsertGildRelationFailSafe(
            (ulong First, ulong Second) relationKey, int relation, DateTime createTime)
        {
            try
            {
                if (!_gildStore.TryInsertGildRelation(
                        unchecked((long)relationKey.First),
                        unchecked((long)relationKey.Second), relation,
                        createTime, out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        // 4576 add-concern-by-id / 4586 add-concern-by-name / 4578 cancel-concern
        // (native gild_owner strategy slots +0x5C / +0x60; owner-only). Routed
        // through the reversed NativeGildConcernLadder against the live per-gild
        // concern set (gild+0x2C); on Success the set is mutated and INSERT/DELETE
        // gamedata.gildconcern is pushed fail-safe (no rollback). Callers gate on
        // SupportsGildWrites for the fail-closed fallback.
        internal int ApplyGildAddConcernById(long operatorId, long targetGildId)
        {
            lock (_sync)
                return ApplyGildConcernLocked(
                    NativeGildConcernOp.AddConcernById, operatorId,
                    targetGildId, nameResolved: true);
        }

        internal int ApplyGildAddConcernByName(long operatorId, string name)
        {
            lock (_sync)
            {
                var resolved = _gildIdByUpperName.TryGetValue(
                    NativeGildNameResolver.Normalize(name ?? string.Empty),
                    out var targetGildId);
                return ApplyGildConcernLocked(
                    NativeGildConcernOp.AddConcernByName, operatorId,
                    targetGildId, resolved);
            }
        }

        internal int ApplyGildCancelConcern(long operatorId, long targetGildId)
        {
            lock (_sync)
                return ApplyGildConcernLocked(
                    NativeGildConcernOp.CancelConcern, operatorId,
                    targetGildId, nameResolved: true);
        }

        private int ApplyGildConcernLocked(NativeGildConcernOp op,
            long operatorId, long targetGildId, bool nameResolved)
        {
            var hasCaller = TryGetPlayerCorpsLocked(operatorId,
                out var callerCorps);
            NativeGildSnapshot callerGild = null;
            var hasGild = hasCaller
                          && _corpsToGild.TryGetValue(callerCorps.Id,
                              out var callerGildId)
                          && _gildById.TryGetValue(callerGildId,
                              out callerGild);
            var role = ResolveGildRoleLocked(hasCaller, hasGild,
                hasCaller ? GetPositionLocked(callerCorps, operatorId)
                    : (byte)0);

            var concernSet = hasGild ? GetConcernSetLocked(callerGild.Id) : null;
            var present = concernSet != null
                          && concernSet.Contains(targetGildId);
            var context = new NativeGildConcernContext
            {
                Role = role,
                NameResolved = nameResolved,
                PlayerResolved = hasCaller,
                HasGild = hasGild,
                TargetGildFound = hasGild
                                  && _gildById.ContainsKey(targetGildId),
                TargetIsSelf = hasGild && targetGildId == callerGild.Id,
                ConcernAlreadyPresent = present,
                ConcernPresentForRemove = present
            };
            var result = NativeGildConcernLadder.Evaluate(op, context);
            if (result != NativeGildConcernLadder.Success) return result;

            if (op == NativeGildConcernOp.CancelConcern)
            {
                GetOrCreateConcernSetLocked(callerGild.Id)
                    .TryRemove(targetGildId);
                DeleteGildConcernFailSafe(callerGild.Id, targetGildId);
            }
            else
            {
                GetOrCreateConcernSetLocked(callerGild.Id).TryAdd(targetGildId);
                InsertGildConcernFailSafe(callerGild.Id, targetGildId);
            }
            return result;
        }

        // 4581 enable-union (native gild_owner/gild_vice slot +0x58 sub_704EAC).
        // Session-only flag on the gild (gild+0x28, NO gamedata column): on a
        // change it re-emits the standard 3-column Gild UPDATE via the existing
        // TrySaveGild (a no-op side effect for the flag), matching sub_704EAC —
        // no schema change and no new store method. Ladder 5/12/0 (owner+vice).
        internal int ApplyGildEnableUnion(long operatorId, bool desiredEnabled)
        {
            lock (_sync)
            {
                var hasCaller = TryGetPlayerCorpsLocked(operatorId,
                    out var callerCorps);
                NativeGildSnapshot callerGild = null;
                var hasGild = hasCaller
                              && _corpsToGild.TryGetValue(callerCorps.Id,
                                  out var callerGildId)
                              && _gildById.TryGetValue(callerGildId,
                                  out callerGild);
                var role = ResolveGildRoleLocked(hasCaller, hasGild,
                    hasCaller ? GetPositionLocked(callerCorps, operatorId)
                        : (byte)0);

                var result = NativeGildUnionFlagLadder.Evaluate(role, hasCaller,
                    hasGild);
                if (result != NativeGildUnionFlagLadder.Success) return result;

                if (callerGild.UnionEnabled != desiredEnabled)
                {
                    callerGild.UnionEnabled = desiredEnabled;
                    SaveGildFailSafe(callerGild);
                }
                return result;
            }
        }

        // 4583 gild-exit: a member CORPS leaves the Gild (native handler
        // sub_6F6BF8 zone gates -> role strategy sub_703418). The zone gates
        // (safe-zone / fight-zone / castle-war) are player-object state supplied
        // by the caller — identical to the sibling native corps-exit
        // ExitNativeCorps — so this method only fills the gild-state gates and
        // runs the reversed NativeGildExitTransaction ladder (38/12/28/29 handler
        // + 5/12/18/1000/0 strategy). On Success the caller's corps is removed
        // from the gild in-memory (clearing the vice pointer if the leaver was
        // the vice) and DELETE gamedata.gildmember is pushed to INativeGildStore
        // fail-safe (no rollback, matching the reversed original). Callers gate
        // on SupportsGildWrites.
        internal int ApplyGildExit(long playerId, bool canLeave,
            bool inFightZone, bool castleWarBlocked)
        {
            lock (_sync)
            {
                var hasGild = TryGetPlayerGildLocked(playerId, out var gild);
                var corpsId = 0L;
                var validMember = hasGild
                                  && _memberToCorps.TryGetValue(playerId,
                                      out corpsId)
                                  && gild.CorpsIds.Contains(corpsId);

                var context = new NativeGildExitContext
                {
                    CanLeave = canLeave,
                    HasGildMembership = hasGild,
                    InFightZone = inFightZone,
                    CastleWarBlocked = castleWarBlocked,
                    HasPlayer = playerId != 0,
                    HasGild = hasGild,
                    ValidMember = validMember,
                    // In-memory removal cannot fail, so the reversed
                    // WriteFailed(1000) branch is unreachable here (matching the
                    // break-union model); the gildmember DELETE is fail-safe.
                    RemoveOk = validMember
                };
                var result = NativeGildExitTransaction.Evaluate(context);
                if (result != NativeGildExitTransaction.Success) return result;

                gild.CorpsIds.Remove(corpsId);
                _corpsToGild.Remove(corpsId);
                if (gild.ViceOwnerId == corpsId) gild.ViceOwnerId = 0;
                DeleteGildMemberFailSafe(gild.Id, corpsId);
                return result;
            }
        }

        // 4587 vice self-stepdown / 4588 president-dismiss-vice (native handlers
        // sub_6F7968 / sub_6F79A4 -> sub_6ADA3C role dispatch -> strategy slots
        // +0x78 / +0x74). Context is built from live Corps/Gild state and
        // classified by the reversed NativeGildViceTransaction ladder (4587:
        // 555/5/12/555/0; 4588: 555/5/12/555/22/22/0). On Success the gild vice
        // pointer is cleared in-memory and make-save-gild (TrySaveGild with
        // ViceGuild=0) is pushed fail-safe (no rollback). 4587 has no wire target
        // (the caller IS the vice); 4588's target is the vice CORPS id. Callers
        // gate on SupportsGildWrites.
        internal int ApplyGildVice(NativeGildViceOp op, long operatorId,
            long targetCorpsId)
        {
            lock (_sync)
            {
                var hasCaller = TryGetPlayerCorpsLocked(operatorId,
                    out var callerCorps);
                NativeGildSnapshot gild = null;
                var hasGild = hasCaller
                              && _corpsToGild.TryGetValue(callerCorps.Id,
                                  out var callerGildId)
                              && _gildById.TryGetValue(callerGildId, out gild);
                var role = ResolveGildRoleLocked(hasCaller, hasGild,
                    hasCaller ? GetPositionLocked(callerCorps, operatorId)
                        : (byte)0);

                var gildHasVice = hasGild && gild.ViceOwnerId != 0;
                // The native compares the vice/president CORPS captain id
                // (*(corps+24)) against the caller id; a corps captain is its
                // OwnerId, so require the caller to be that corps' captain.
                var callerIsTheVice = gildHasVice
                                      && callerCorps.Id == gild.ViceOwnerId
                                      && callerCorps.OwnerId == operatorId;
                var callerIsPresident = hasGild
                                        && callerCorps.Id == gild.OwnerCorpsId
                                        && callerCorps.OwnerId == operatorId;
                var targetFound = hasGild
                                  && _corpsById.ContainsKey(targetCorpsId);
                var targetIsVice = gildHasVice
                                   && targetCorpsId == gild.ViceOwnerId;

                var context = new NativeGildViceContext
                {
                    Role = role,
                    HasPlayer = operatorId != 0,
                    HasGild = hasGild,
                    GildHasVice = gildHasVice,
                    CallerIsTheVice = callerIsTheVice,
                    CallerIsPresident = callerIsPresident,
                    TargetFound = targetFound,
                    TargetIsVice = targetIsVice
                };
                var result = NativeGildViceTransaction.Evaluate(op, context);
                if (result != NativeGildViceTransaction.Success) return result;

                gild.ViceOwnerId = 0;
                SaveGildFailSafe(gild);
                return result;
            }
        }

        // 4564 create-gild live routing target (native sub_6ADDA8 -> role
        // strategy[+0x3C] sub_702F8C -> AddGild sub_5E752C). Classified by
        // guild-store's reversed NativeGildCreateContract (ladder 555/4/5/6/2/0;
        // NO gold gate, NO name-validity gate — only AddGild dup-name). On
        // Success a composite GildID is allocated (NativeGildIdAllocator), the
        // gild + indexes are published in-memory, and the two fire-and-forget
        // writes run in the reversed order INSERT gildmember THEN INSERT Gild via
        // INativeGildStore, fail-safe (no rollback). The single GildID is shared
        // by the registry entry and both INSERTs, so memory and gamedata.Gild
        // agree by construction. Routes through the new create-contract model (NOT the
        // dormancy-guarded legacy self-corps/gild exact create state machine). Callers gate on
        // SupportsGildWrites.
        internal int ApplyGildCreate(long operatorId, string name)
        {
            lock (_sync)
            {
                var hasCorps = TryGetPlayerCorpsLocked(operatorId,
                    out var callerCorps);
                var position = hasCorps
                    ? GetPositionLocked(callerCorps, operatorId)
                    : (byte)0;
                var role = MapSelfSocialRoleLocked(hasCorps, position);
                var corpsAlreadyInGild = hasCorps
                                         && _corpsToGild.ContainsKey(
                                             callerCorps.Id);
                var normalized = NativeGildNameResolver.Normalize(
                    name ?? string.Empty);
                var gildNameExists = _gildIdByUpperName.ContainsKey(normalized);

                var result = NativeGildCreateContract.Evaluate(role,
                    operatorId != 0, hasCorps, corpsAlreadyInGild,
                    gildNameExists);
                if (!NativeGildCreateContract.EnqueuesCreateWrites(result))
                    return result;

                var gildId = AllocateGildIdLocked();
                var gild = new NativeGildSnapshot
                {
                    Id = gildId,
                    CreateTime = DateTime.Now,
                    Name = name ?? string.Empty,
                    OwnerCorpsId = callerCorps.Id,
                    ViceOwnerId = NativeGildCreateContract.CreateViceOwnerId,
                    Notice = Array.Empty<byte>()
                };
                gild.CorpsIds.Add(callerCorps.Id);

                _gildById[gildId] = gild;
                _corpsToGild[callerCorps.Id] = gildId;
                if (!string.IsNullOrEmpty(normalized))
                    _gildIdByUpperName[normalized] = gildId;

                // NativeGildCreateContract.SuccessWriteOrder: gildmember first,
                // then the Gild row. Fail-safe (no rollback).
                InsertGildMemberFailSafe(gildId, callerCorps.Id);
                CreateGildFailSafe(gild);
                return result;
            }
        }

        // Composite GildID via the reversed NativeGildIdAllocator (sub_5E665C):
        // byte layout + 0xFF sequence/tick-advance preserved. Documented benign
        // divergence (lead-approved): the 40-bit scaled timestamp uses
        // ms-since-epoch(2015-12-30) instead of the exact sub_403574 unit, and
        // serverId is 0 — the GildID is an opaque unique PK (nothing reconstructs
        // the timestamp). serverId=0 also confines generated ids to [0,2^48),
        // disjoint from loaded native ids whose serverId word is non-zero.
        private long AllocateGildIdLocked()
        {
            var epoch = new DateTime(NativeGildIdAllocator.EpochYear,
                NativeGildIdAllocator.EpochMonth,
                NativeGildIdAllocator.EpochDay, 0, 0, 0, DateTimeKind.Utc);
            for (var attempt = 0; attempt < 1024; attempt++)
            {
                var elapsedMs =
                    (long)(DateTime.UtcNow - epoch).TotalMilliseconds;
                if (elapsedMs < 0) elapsedMs = 0;
                var ticks40 = (ulong)elapsedMs & 0xFF_FFFF_FFFFUL;
                var id = _gildIdAllocator.Allocate(
                    (uint)(ticks40 & 0xFFFF_FFFFUL),
                    (byte)((ticks40 >> 32) & 0xFF), 0, out _);
                if (!_gildById.ContainsKey(id)) return id;
            }
            var max = _gildById.Count == 0 ? 0L : _gildById.Keys.Max();
            return max + 1;
        }

        // Maps live Corps/Gild position (GetPositionLocked) to the create
        // contract's NativeSelfSocialRole: 4=gild owner, 3=gild vice, 2=corps
        // owner, 1=corps vice, 0=member; no corps = NoCorps.
        private static NativeSelfSocialRole MapSelfSocialRoleLocked(
            bool hasCorps, byte position) =>
            !hasCorps
                ? NativeSelfSocialRole.NoCorps
                : position switch
                {
                    4 => NativeSelfSocialRole.GildOwner,
                    3 => NativeSelfSocialRole.GildViceOwner,
                    2 => NativeSelfSocialRole.CorpsOwner,
                    1 => NativeSelfSocialRole.CorpsViceOwner,
                    _ => NativeSelfSocialRole.Member
                };

        // sub_4C70AC forbidden-ASCII corps-name bitmap @0x004C70F0 (little-endian
        // dwords of "ff ff ff ff ff ff 00 d4 00 00 00 10 00 00 00 10"). Any ASCII
        // byte (<= 0x7F) whose bit is set makes the name invalid; high (GBK) bytes
        // are always allowed. Control chars, space, most punctuation and : < > ? \ |
        // are forbidden. UNLIKE create-gild, create-corps HAS this name gate.
        private static readonly uint[] InvalidCorpsNameBitmap =
            { 0xFFFFFFFFu, 0xD400FFFFu, 0x10000000u, 0x10000000u };

        // 4524 create-corps (建队) live routing target (native handler sub_6ADD08
        // -> role strategy[+0x08] sub_701A74 -> CreateCorpsManager sub_5EA28C;
        // image base 0x00400000). The wire body is the corps NAME. Reversed
        // sub_5EA28C ladder: 3 already-in-a-corps (native handler a1[698]!=0 gate
        // BEFORE dispatch, and the manager repeats it as its own [ctx+8]!=0
        // defensive re-check) / 1 invalid name (sub_4C70AC forbidden-ASCII bitmap,
        // empty name invalid) / 2 duplicate corps name (sub_49F5F4 name index,
        // ASCII-uppercased) / 0 success. NO gold gate (create is free). On success
        // a composite CorpsID is allocated (the SAME synthetic-id allocator native
        // shares between Corps + Gild, sub_5E665C), the corps + founder owner-member
        // + indexes are published in-memory, then the two fire-and-forget writes run
        // in the reversed order INSERT Corps THEN INSERT CorpsMember, fail-safe (no
        // rollback, matching the native async off_7D5AC8 queue). Routes through this
        // new create path (NOT the dormancy-guarded legacy self-corps/gild exact
        // create state machine), exactly like ApplyGildCreate. Callers gate on
        // SupportsGildWrites (the corps + gild MySQL stores are co-injected).
        internal int ApplyCorpsCreate(NativeCorpsActor founder, string name)
        {
            lock (_sync)
            {
                if (!IsAvailable || founder.Id == 0) return UnknownError;
                // Already-in-a-corps gate (native a1[698] != 0 -> 3).
                if (_memberToCorps.ContainsKey(founder.Id)) return 3;
                // sub_4C70AC forbidden-ASCII / empty name gate -> 1.
                if (IsInvalidNativeCorpsName(name)) return 1;
                // sub_49F5F4 duplicate corps-name (ASCII-uppercased) -> 2.
                var normalized = NativeGildNameResolver.Normalize(
                    name ?? string.Empty);
                foreach (var existing in _corpsById.Values)
                    if (NativeGildNameResolver.Normalize(existing.Name)
                        == normalized)
                        return 2;

                var corpsId = AllocateCorpsIdLocked();
                var corps = new NativeCorpsSnapshot
                {
                    Id = corpsId,
                    CreateTime = DateTime.Now,
                    Name = name ?? string.Empty,
                    OwnerId = founder.Id
                };
                var member = new NativeCorpsMemberSnapshot
                {
                    MemberId = founder.Id,
                    Name = founder.Name,
                    Level = founder.Level,
                    Sex = founder.Sex,
                    Job = founder.Job,
                    Title = string.Empty,
                    LastLoginTime = DateTime.Now
                };
                corps.Members.Add(member);

                _corpsById[corpsId] = corps;
                _memberToCorps[founder.Id] = corpsId;
                _applications.Remove(founder.Id);

                // sub_5EA28C success order: INSERT Corps first, then INSERT
                // CorpsMember. Fail-safe (no rollback of the published memory).
                InsertCorpsFailSafe(corps);
                InsertCorpsMemberFailSafe(corpsId, member);
                return 0;
            }
        }

        // sub_4C70AC: true when the corps name is invalid (native result code 1).
        // Operates on the GBK bytes of the name; an empty name is invalid.
        private static bool IsInvalidNativeCorpsName(string name)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(name ?? string.Empty);
            if (bytes.Length == 0) return true;
            foreach (var value in bytes)
            {
                if (value > 0x7F) continue;
                if ((InvalidCorpsNameBitmap[value >> 5]
                     & (1u << (value & 31))) != 0)
                    return true;
            }
            return false;
        }

        // Composite CorpsID via the reversed shared allocator (sub_5E665C, the SAME
        // generator native uses for both Corps and Gild). Reuses the gild allocator
        // instance (monotonic sequence advances each call) and checks _corpsById for
        // uniqueness; same documented benign timestamp/serverId divergence as
        // AllocateGildIdLocked (the CorpsID is an opaque unique PK).
        private long AllocateCorpsIdLocked()
        {
            var epoch = new DateTime(NativeGildIdAllocator.EpochYear,
                NativeGildIdAllocator.EpochMonth,
                NativeGildIdAllocator.EpochDay, 0, 0, 0, DateTimeKind.Utc);
            for (var attempt = 0; attempt < 1024; attempt++)
            {
                var elapsedMs =
                    (long)(DateTime.UtcNow - epoch).TotalMilliseconds;
                if (elapsedMs < 0) elapsedMs = 0;
                var ticks40 = (ulong)elapsedMs & 0xFF_FFFF_FFFFUL;
                var id = _gildIdAllocator.Allocate(
                    (uint)(ticks40 & 0xFFFF_FFFFUL),
                    (byte)((ticks40 >> 32) & 0xFF), 0, out _);
                if (!_corpsById.ContainsKey(id)) return id;
            }
            var max = _corpsById.Count == 0 ? 0L : _corpsById.Keys.Max();
            return max + 1;
        }

        private void InsertCorpsFailSafe(NativeCorpsSnapshot corps)
        {
            try
            {
                if (!_store.TryInsertCorps(corps, out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        private void InsertCorpsMemberFailSafe(long corpsId,
            NativeCorpsMemberSnapshot member)
        {
            try
            {
                if (!_store.TryInsertMember(corpsId, member, out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        private void InsertGildMemberFailSafe(long gildId, long corpsId)
        {
            try
            {
                if (!_gildStore.TryInsertGildMember(gildId, corpsId,
                        out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        private void CreateGildFailSafe(NativeGildSnapshot gild)
        {
            try
            {
                if (!_gildStore.TryCreateGild(gild.Id,
                        gild.Name ?? string.Empty, gild.OwnerCorpsId,
                        gild.ViceOwnerId, out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        private void InsertGildConcernFailSafe(long gildId, long dstGildId)
        {
            try
            {
                if (!_gildStore.TryInsertGildConcern(gildId, dstGildId,
                        out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        private void DeleteGildConcernFailSafe(long gildId, long dstGildId)
        {
            try
            {
                if (!_gildStore.TryDeleteGildConcern(gildId, dstGildId,
                        out var error))
                    M2Share.MainOutMessage("执行sql失败:" + error);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("执行sql失败:" + ex.Message);
            }
        }

        internal int Exit(long playerId)
        {
            lock (_sync)
            {
                if (!TryGetPlayerCorpsLocked(playerId, out var corps))
                    return 5;
                if (corps.OwnerId == playerId) return 39;
                var member = corps.Members.FirstOrDefault(value =>
                    value.MemberId == playerId);
                if (member == null) return 18;

                var oldVice1 = corps.ViceOwner1Id;
                var oldVice2 = corps.ViceOwner2Id;
                var wasVice = oldVice1 == playerId || oldVice2 == playerId;
                if (oldVice1 == playerId)
                    corps.ViceOwner1Id = 0;
                else if (oldVice2 == playerId)
                    corps.ViceOwner2Id = 0;
                if (!_store.TryExitMember(playerId, corps, wasVice, out _))
                {
                    corps.ViceOwner1Id = oldVice1;
                    corps.ViceOwner2Id = oldVice2;
                    return UnknownError;
                }
                corps.Members.Remove(member);
                _memberToCorps.Remove(playerId);
                AddLogLocked(corps.Id, 1,
                    $"{member.Name} left the Corps");
                return 0;
            }
        }

        internal IReadOnlyList<NativeCorpsLogEntry> GetLogPage(long playerId,
            int type, int page, int pageSize, out int result)
        {
            lock (_sync)
            {
                result = 0;
                if (!TryGetPlayerCorpsLocked(playerId, out var corps))
                {
                    result = 5;
                    return Array.Empty<NativeCorpsLogEntry>();
                }
                if (!_logs.TryGetValue((corps.Id, type), out var logs))
                {
                    result = 30;
                    return Array.Empty<NativeCorpsLogEntry>();
                }
                var start = (long)page * pageSize;
                if (start >= logs.Count)
                {
                    result = 30;
                    return Array.Empty<NativeCorpsLogEntry>();
                }
                return logs.Skip(unchecked((int)start)).Take(pageSize)
                    .ToArray();
            }
        }

        internal IReadOnlyList<NativeGildSnapshot> SnapshotGilds()
        {
            lock (_sync) return _gildById.Values.OrderBy(x => x.Id).ToArray();
        }

        internal IReadOnlyList<NativeCorpsSnapshot> SnapshotCorps()
        {
            lock (_sync) return _corpsById.Values.OrderBy(x => x.Id).ToArray();
        }

        // 4575 CM_GILD_QUERY_UNION / 4580 CM_GILD_QUERY_HOSTILE read side (native
        // sub_6F64D8 / sub_6F6A7C). Returns one (id, name) per gild that the caller's
        // gild holds the given relation with (GildUnion=1 allies / GildHostile=2
        // enemies), paginated exactly like the native handler: pageStart =
        // page*pageSize; result = 12 when the caller has no gild, otherwise 0 (a
        // past-the-end page returns 0 + an empty slice — these handlers have no
        // gild-list "30" code). Order is by CreateTime ascending: the native per-gild
        // TLists (gild+48 / gild+52) are in insertion order, which corresponds to
        // CreateTime.
        internal IReadOnlyList<(long Id, string Name)> GetGildRelationPage(
            long playerId, byte relation, int page, int pageSize, out int result)
        {
            lock (_sync)
            {
                result = 0;
                if (!TryGetPlayerGildLocked(playerId, out var gild))
                {
                    result = 12;
                    return Array.Empty<(long, string)>();
                }

                var self = unchecked((ulong)gild.Id);
                var matches = new List<(long Id, string Name, DateTime CreateTime)>();
                foreach (var pair in _gildRelations)
                {
                    if (pair.Value.Relation != relation) continue;
                    long other;
                    if (pair.Key.First == self)
                        other = unchecked((long)pair.Key.Second);
                    else if (pair.Key.Second == self)
                        other = unchecked((long)pair.Key.First);
                    else
                        continue;
                    if (_gildById.TryGetValue(other, out var otherGild))
                        matches.Add((other, otherGild.Name, pair.Value.CreateTime));
                }

                matches.Sort((left, right) => left.CreateTime.CompareTo(right.CreateTime));
                var start = (long)page * pageSize;
                if (start >= matches.Count)
                    return Array.Empty<(long, string)>();
                return matches.Skip(unchecked((int)start)).Take(pageSize)
                    .Select(m => (m.Id, m.Name)).ToArray();
            }
        }

        // 4577 CM_GILD_QUERY_CONCERN read side (native sub_6F6784). One (id, name,
        // relation) per gild in the caller gild's concern set (gild+44), where
        // relation is the caller<->concerned relation byte (sub_5E7890). The concern
        // set preserves native TList (insertion) order; pagination and the 12/0
        // result gate match the relation queries above.
        internal IReadOnlyList<(long Id, string Name, byte Relation)>
            GetGildConcernPage(long playerId, int page, int pageSize,
                out int result)
        {
            lock (_sync)
            {
                result = 0;
                if (!TryGetPlayerGildLocked(playerId, out var gild))
                {
                    result = 12;
                    return Array.Empty<(long, string, byte)>();
                }

                var matches = new List<(long Id, string Name, byte Relation)>();
                var concerns = GetConcernSetLocked(gild.Id);
                if (concerns != null)
                {
                    foreach (var dstId in concerns.DestinationGildIds)
                    {
                        if (!_gildById.TryGetValue(dstId, out var dstGild))
                            continue;
                        var relation = (byte)0;
                        if (_gildRelations.TryGetValue(
                            NativeCorpsDataSnapshot.GildRelationKey(gild.Id,
                                dstId), out var relationTuple))
                            relation = relationTuple.Relation;
                        matches.Add((dstId, dstGild.Name, relation));
                    }
                }

                var start = (long)page * pageSize;
                if (start >= matches.Count)
                    return Array.Empty<(long, string, byte)>();
                return matches.Skip(unchecked((int)start)).Take(pageSize)
                    .ToArray();
            }
        }

        // 4560 CM_GILD_REQUEST_JOIN write (native sub_6F5958 -> sub_703624): create an in-memory pending
        // JOIN request (a corps captain enrolling their corps into the target gild). Classified by the
        // reversed NativeGildRequestJoinTransaction (12 target-not-found / 555 non-captain / 6 already-in-a-
        // gild / 8 duplicate / 0). The request's [4,5] secondary/dedup key = the caller's CORPS id (native
        // dup probe sub_6A52A0); [6,7] target = the target gild. NO persistence (pending requests are
        // runtime-only). The role reaching sub_703624 is any CORPS OWNER (native VMTs corps_owner /
        // gild_vice_owner / gild_owner) — modeled as GildMember; corps vice/members hit the 555 stub.
        // Callers gate on SupportsGildWrites for the fail-closed fallback.
        internal int ApplyGildRequestJoin(long operatorId, long targetGildId)
        {
            lock (_sync)
            {
                var hasCorps = TryGetPlayerCorpsLocked(operatorId, out var corps);
                var context = new NativeGildRequestJoinContext
                {
                    Role = hasCorps && corps.OwnerId == operatorId
                        ? NativeGildRole.GildMember
                        : hasCorps
                            ? NativeGildRole.Corps
                            : NativeGildRole.NoCorps,
                    TargetGildFound = _gildById.ContainsKey(targetGildId),
                    HasPlayer = true,
                    HasGild = hasCorps && _corpsToGild.ContainsKey(corps.Id),
                    DuplicateRequest = hasCorps
                        && _requestLedger.HasPendingForSecondaryKey(
                            targetGildId, corps.Id),
                    ManagerResult = 0
                };
                var result = NativeGildRequestJoinTransaction.Evaluate(context);
                if (NativeGildRequestJoinTransaction.CreatesPendingRequest(
                        result))
                    result = _requestLedger.Add(new NativeGildPendingRequest
                    {
                        // [8,C] UniqueId = the generated global-registry key (THE accept/refuse lookup id).
                        // [2,3] RequestId = the applicant CharID (record requester-display only, not the key).
                        UniqueId = _requestLedger.NextUniqueId(),
                        RequestId = operatorId,
                        SecondaryKey = corps.Id,
                        TargetKey = targetGildId,
                        Kind = NativeGildRequestKind.JoinGild,
                        CreatedTime = DateTime.Now
                    });
                return result;
            }
        }

        // 4573 CM_GILD_REQUEST_UNION write (native sub_6F6390 -> sub_704494): the client sends a gild NAME;
        // sub_5E76F0 resolves it (unresolved -> 12). President-only. Classified by the reversed
        // NativeGildRequestUnionTransaction (5/12/25/19/34/15/33/8/0). On reaching the create region the
        // native calls save_relation(sub_5E6E60, dl=3) BEFORE the dup probe and DISCARDS its result
        // (0x7045CC has no `mov edi,eax`, unlike 0x704060 and 0x708241). save_relation publishes the type
        // into the SAME relation map every other path reads — 0x5E6F19 `33C9 xor ecx,ecx` / 0x5E6F1B
        // `8ACB mov cl,bl` / 0x5E6F23 `call 0x49F9C8` with bl = the raw type — so a pending 3 IS in memory,
        // not DB-only. Its own gate 0x5E6F0D `48 dec eax` / `2C03 sub al,3` / `73 jae` passes only when the
        // pair currently reads 0, and both the map write and the INSERT sit past that gate.
        // An orphaned pending row on a dup-reject (8) is faithful (native order). Secondary/
        // dedup key = the caller's OWN gild id (native dup probe sub_7065B0); target = the resolved gild.
        // Callers gate on SupportsGildWrites (so _gildStore is non-null on this path).
        internal int ApplyGildRequestUnion(long operatorId, string targetName)
        {
            lock (_sync)
            {
                var nameResolved = _gildIdByUpperName.TryGetValue(
                    NativeGildNameResolver.Normalize(
                        targetName ?? string.Empty), out var targetGildId);
                var hasCorps = TryGetPlayerCorpsLocked(operatorId,
                    out var callerCorps);
                NativeGildSnapshot ownGild = null;
                var hasGild = hasCorps
                    && _corpsToGild.TryGetValue(callerCorps.Id,
                        out var ownGildId)
                    && _gildById.TryGetValue(ownGildId, out ownGild);
                var position = hasCorps
                    ? GetPositionLocked(callerCorps, operatorId) : (byte)0;
                _gildById.TryGetValue(targetGildId, out var targetGild);
                byte relation = 0;
                if (hasGild && targetGild != null)
                {
                    if (_gildRelations.TryGetValue(
                        NativeCorpsDataSnapshot.GildRelationKey(ownGild.Id,
                            targetGildId), out var relationTuple))
                        relation = relationTuple.Relation;
                }

                var context = new NativeGildRequestUnionContext
                {
                    Role = position == 4
                        ? NativeGildRole.GildOwner
                        : NativeGildRole.GildMember,
                    PreconditionMet = nameResolved,
                    HasPlayer = true,
                    HasGild = hasGild,
                    TargetGildFound = targetGild != null,
                    TargetIsOwnGild = hasGild && targetGild != null
                        && targetGildId == ownGild.Id,
                    TargetAllowsUnion = targetGild != null
                        && targetGild.UnionEnabled,
                    ExistingRelation = relation,
                    DuplicatePending = hasGild
                        && _requestLedger.HasPendingForSecondaryKey(
                            targetGildId, ownGild.Id),
                    ManagerResult = 0
                };
                var result = NativeGildRequestUnionTransaction.Evaluate(
                    context);

                // Create region reached (all gates passed): the Relation=3 publish fires BEFORE the dup
                // probe, whether or not a duplicate is then found (native order; orphan on 8 is faithful).
                // The ladder itself only rejects 1 and 2 (0x704540/0x704544 `FEC8 dec al` + `je`), so a pair
                // already holding 3 arrives here; save_relation's own 0x5E6F0D gate then rejects it, which
                // is why nothing is written for a non-zero existing relation — that is also what keeps the
                // (GildID1,GildID2) UNIQUE KEY from ever seeing a second INSERT.
                if (result == NativeGildRequestUnionTransaction.Success
                    || result == NativeGildRequestUnionTransaction
                        .DuplicatePendingRequest)
                {
                    if (relation == 0)
                    {
                        var pendingKey = NativeCorpsDataSnapshot
                            .GildRelationKey(ownGild.Id, targetGildId);
                        var pendingTime = DateTime.Now;
                        _gildRelations[pendingKey] =
                            (GildPendingUnion, pendingTime);
                        InsertGildRelationFailSafe(pendingKey,
                            GildPendingUnion, pendingTime);
                    }
                    if (result == NativeGildRequestUnionTransaction.Success)
                        result = _requestLedger.Add(
                            new NativeGildPendingRequest
                            {
                                // [8,C] UniqueId = the generated global-registry key (accept/refuse lookup
                                // id); [2,3] RequestId = the requesting president CharID (display only).
                                UniqueId = _requestLedger.NextUniqueId(),
                                RequestId = operatorId,
                                SecondaryKey = ownGild.Id,
                                TargetKey = targetGildId,
                                Kind = NativeGildRequestKind.Union,
                                CreatedTime = DateTime.Now
                            });
                }
                return result;
            }
        }

        // 4570 CM_GILD_QUERY_REQUEST_JOIN_LIST read (native sub_6F6064): pending JOIN requests targeting
        // the caller's gild, in native TList (timestamp) order; result 12 when the caller has no gild,
        // else 0 (empty past-the-end page still 0).
        internal IReadOnlyList<(long SecondaryKey, long UniqueId, string Name,
                string OwnerName, int Flag)>
            GetGildJoinRequestPage(long playerId, int page, int pageSize,
                out int result)
        {
            lock (_sync)
                return GetGildRequestPageLocked(playerId,
                    NativeGildRequestKind.JoinGild, page, pageSize,
                    out result);
        }

        // 4571 CM_GILD_QUERY_REQUEST_UNION_LIST read (native sub_6F61BC): pending UNION requests targeting
        // the caller's gild. Same frame as 4570. NOTE: native sub_70839C resolves the record's [4,5] via
        // the CORPS registry, but a union request's [4,5] is a GILD id, so the name/captain/count come out
        // empty here (mirroring native) — flagged for review pending an idat confirm of the union request's
        // display fields.
        internal IReadOnlyList<(long SecondaryKey, long UniqueId, string Name,
                string OwnerName, int Flag)>
            GetGildUnionRequestPage(long playerId, int page, int pageSize,
                out int result)
        {
            lock (_sync)
                return GetGildRequestPageLocked(playerId,
                    NativeGildRequestKind.Union, page, pageSize, out result);
        }

        private IReadOnlyList<(long SecondaryKey, long UniqueId, string Name,
                string OwnerName, int Flag)>
            GetGildRequestPageLocked(long playerId,
                NativeGildRequestKind kind, int page, int pageSize,
                out int result)
        {
            result = 0;
            if (!TryGetPlayerGildLocked(playerId, out var gild))
            {
                result = 12;
                return Array.Empty<(long, long, string, string, int)>();
            }

            var pending = _requestLedger.Snapshot(request =>
                request.Kind == kind && request.TargetKey == gild.Id);
            var start = (long)page * pageSize;
            if (start >= pending.Count)
                return Array.Empty<(long, long, string, string, int)>();
            return pending.Skip(unchecked((int)start)).Take(pageSize)
                .Select(BuildGildRequestSummaryLocked).ToArray();
        }

        // native sub_70839C record: [0..7]=[4,5] requester/secondary key, [8..15]=[8,C] UNIQUE request id
        // (the accept/refuse key the client echoes back in 4611/4572), then the requester resolved via the
        // CORPS registry (sub_5EA444) for [16..31] name1 / [32..47] name2 (owner/leader) / [48] resolved
        // FLAG (1 if the requester resolved, else 0). Empty names + flag 0 for a non-corps [4,5] (e.g. a
        // union request's requesting-gild id, which the corps registry does not resolve).
        private (long SecondaryKey, long UniqueId, string Name,
            string OwnerName, int Flag) BuildGildRequestSummaryLocked(
                NativeGildPendingRequest request)
        {
            var name = string.Empty;
            var owner = string.Empty;
            var flag = 0;
            if (_corpsById.TryGetValue(request.SecondaryKey, out var corps))
            {
                name = corps.Name;
                owner = corps.Members
                    .FirstOrDefault(member =>
                        member.MemberId == corps.OwnerId)?.Name
                    ?? string.Empty;
                flag = 1;
            }
            return (request.SecondaryKey, request.UniqueId, name, owner, flag);
        }

        // 4572 CM_GILD_REFUSE_REQUEST write (native sub_6F6340 -> role strategy +0x04 -> subtype refuse).
        // Resolves the pending request by the client-echoed id, runs the reversed role×type cascade +
        // subtype refuse ladder (NativeGildRequestResponseWiredTransaction: 10 not-found / 555 role-too-low
        // / 23 wrong-type; then the subtype ladder 555/12/5/0), and on success removes the request.
        //
        // LOOKUP KEY (sub_6A5284 — CONFIRMED by codec-fidelity): the client-echoed leading int64 of the
        // body is the APPLICANT's CharID, and the request container is PER-GUILD, so the lookup is the
        // caller's OWN gild container keyed by that CharID (TryGetByApplicant). No cross-gild reach is
        // possible — the per-guild container IS the scope. Only type-1 (join-gild) / type-2 (union)
        // requests live in this gild ledger; type-0 (corp-join) is refused via the corps path (4536).
        // (Residual: the 23 `(**rec)()` guard may be a separate already-processed flag beyond the cascade's
        // WrongType=23 — rides codec-fidelity's next idat; WrongType likely already covers it.)
        //
        // DEFERRED (FLAGGED): the applicant notify (native SM 4612 if online, else the offline-notice queue
        // sub_6A52BC) is NOT sent here — it needs the SM 4612 reply-frame confirmation + the offline-notice
        // store (unmodeled). The request removal (the observable state change) is complete; the applicant
        // currently learns the outcome only on re-query. Follow-up.
        //
        // TWO REQUEST FAMILIES via the GENERIC 4572 refuse + the caller's role-strategy cascade
        // (codec-fidelity: NO separate union opcode; 4572 = sub_6F6340 -> role slot +0x50). JOIN requests
        // (type()=0/1) refuse via the join-refuse ladder (555/12/5/0), no relation write. UNION requests
        // (TUnionGildRequest, type()=2) are reachable ONLY by a PRESIDENT (sub_70443C -> sub_708004); a
        // non-president hits WrongType-23. The cascade already returns the right ladder per role+type; on a
        // UNION success we run delete_relation on the Relation=3 pending pair that 4573 created
        // (sub_5E90A4 @0x70809E) — refuse ONLY deletes, NO re-insert (accept does DELETE-3 + INSERT-1).
        // Lookup = the per-guild ledger by applicant CharID (= sub_6A5284 Self[+0x1C]);
        // guild[+0x24] is the president's UI copy only. break-union (4574) is unrelated (dissolves an
        // established relation-1 alliance). [union notify SM 4612 tag-3 / mail type-3 = DEFERRED, as above.]
        internal int ApplyGildRefuseRequest(long operatorId,
            long uniqueRequestId)
        {
            lock (_sync)
            {
                // GLOBAL registry lookup by the unique id from the CM body; the president (operatorId, from
                // the CONNECTION) must own the target gild the request was filed against (req[6,7]) — a
                // president only ever holds ids from their OWN gild's 4570/4571 listing. Role resolves via
                // the caller's gild slots (sub_6ADA3C); level-0 (no gild / plain member) -> 555 per cascade.
                TryGetPlayerCorpsLocked(operatorId, out var callerCorps);
                var position = callerCorps != null
                    ? GetPositionLocked(callerCorps, operatorId) : (byte)0;
                var role = MapRequestResponseRoleLocked(callerCorps != null,
                    position);

                var hasGild = TryGetPlayerGildLocked(operatorId,
                    out var callerGild);
                var found = _requestLedger.TryGetByUniqueId(uniqueRequestId,
                        out var request)
                    && hasGild && callerGild.Id == request.TargetKey;
                NativeGildSnapshot targetGild = null;
                var gildFound = found && _gildById.TryGetValue(
                    request.TargetKey, out targetGild);

                var context = new NativeGildRequestSubtypeContext
                {
                    RequestPresent = found,
                    GildFound = gildFound,
                    GildHasOwnerCorp = gildFound
                        && targetGild.OwnerCorpsId != 0
                        && _corpsById.ContainsKey(targetGild.OwnerCorpsId)
                };
                var result = NativeGildRequestResponseWiredTransaction.Evaluate(
                    NativeGildRequestSubtypeOp.Refuse, role,
                    found ? (int)request.Kind : 0, found, context);
                if (result == 0)
                {
                    // UNION refuse (sub_708004): delete_relation (sub_5E90A4) on the canonical
                    // (requesterGild,targetGild) pair — refuse ONLY deletes, no re-insert. That helper
                    // drops the map entry as well as the row, so the pending 3 must leave _gildRelations
                    // here or the pair stays permanently un-warrable. Join requests carry no relation row.
                    // ledger.Remove consumes the record (native: remove from gild pending list + global
                    // registry).
                    if (request.Kind == NativeGildRequestKind.Union)
                        RemoveGildRelationLocked(
                            NativeCorpsDataSnapshot.GildRelationKey(
                                request.SecondaryKey, request.TargetKey));
                    _requestLedger.RemoveByUniqueId(uniqueRequestId);
                }
                return result;
            }
        }

        // 4611 CM_GILD_ACCEPT_REQUEST write (native sub_6F62F0 -> role strategy slot +0x00 -> the request
        // subtype's accept method[5]). SAME generic opcode + president role cascade as refuse: a president
        // (sub_7039A0) reaches Union(2)/JoinGild(1)/JoinCorps(0); a vice reaches 1/0; a corps owner reaches
        // 0; non-corps/member -> 555; an unreachable type -> WrongType-23. Lookup = the per-guild ledger by
        // applicant CharID (= sub_6A5284 Self[+0x1C]); the accepting gild is the request's TargetKey [6,7].
        //
        // MUTATION-DURING-EVALUATION: the accept ladders' terminal code depends on the WRITE result
        // (JoinGild AddToGildOk -> 0/1000; Union save_relation SaveRelationResult -> 0/...). So we compute
        // the pre-mutation gate inputs, perform the write ONLY when the role-gated ladder reaches it
        // (AcceptReachesMutation, native order), feed the result back, then Evaluate for the final code.
        //   JoinGild accept (sub_707D9C -> add sub_706264): the applicant CORPS ([4,5]) joins the accepting
        //     gild ([6,7] = callerGild): CorpsIds.Add + _corpsToGild + InsertGildMemberFailSafe (fail-safe DB).
        //   Union accept (sub_708168 -> save_relation sub_5E6E60 n4=1): DELETE the pending Relation-3 row +
        //     INSERT the union Relation-1 row on the SAME canonical (min,max) (requesterGild [4,5],
        //     acceptingGild [6,7]) pair (NO in-place UPDATE); type-1 IS tracked in _gildRelations.
        // On success the request record is consumed (ledger.Remove = native remove-from-list + registry).
        //
        // FLAGS (do NOT block the dormant model; codec-fidelity confirmed the gild layout: +0x0C members,
        // +0x20 pending-JOIN list, +0x24 pending-UNION list; sub_706290 = APPLY-phase publish of the request
        // into +0x20/+0x24, i.e. already modeled here as _requestLedger.Add at request time, NOT a separate
        // accept write; accept/refuse only REMOVE from the pending list via ledger.Remove; sub_706264 =
        // accept-phase add-member to +0x0C):
        //  - LOOKUP KEY (wiring-TBD, codec-fidelity confirming — do NOT hard-commit): this resolves the
        //    request PER-GUILD (callerGild.Id, applicantCharId). Native sub_6A5284 uses a global registry
        //    (off_7D727C); if it keyed by applicant CharID ALONE a president could match a request meant for
        //    another guild, so the per-guild key here is the CONSERVATIVE choice (disambiguates by guild).
        //  - Whether any CM query reads +0x20/+0x24 is a benign-gap Q3 (the 4570/4571 reads already expose
        //    the ledger). SM 4612 / mail notify = DEFERRED. Union save_relation 15 (count-limit) edge = 0.
        // Callers gate on SupportsGildWrites; the live 4611 hook stays HELD until my review + the wiring TBDs
        // (key + opcode). The #90 decode fix is already IN (build + audit green).
        internal int ApplyGildAcceptRequest(long operatorId, long uniqueRequestId)
        {
            lock (_sync)
            {
                TryGetPlayerCorpsLocked(operatorId, out var callerCorps);
                var position = callerCorps != null
                    ? GetPositionLocked(callerCorps, operatorId) : (byte)0;
                var role = MapRequestResponseRoleLocked(callerCorps != null,
                    position);

                var hasGild = TryGetPlayerGildLocked(operatorId,
                    out var callerGild);
                // GLOBAL registry lookup by the unique id from the CM body; the president (operatorId, from
                // the CONNECTION) must own the target gild the request was filed against (req[6,7]) — a
                // president only ever holds ids from their OWN gild's 4570/4571 listing.
                var found = _requestLedger.TryGetByUniqueId(uniqueRequestId,
                        out var request)
                    && hasGild && callerGild.Id == request.TargetKey;
                var kind = found ? (int)request.Kind : 0;

                // Field map: [4,5] = applicant CORPS (join) / requesting GILD (union); [6,7] = accepting
                // GILD (= callerGild). Relation pair = (requesting gild [4,5], accepting gild [6,7]).
                var secondaryKey = found ? request.SecondaryKey : 0;
                var relationKey = found
                    ? NativeCorpsDataSnapshot.GildRelationKey(
                        request.SecondaryKey, request.TargetKey)
                    : default;

                // Pre-mutation gate inputs (no write yet). Locals are reused verbatim in both the
                // reaches-mutation probe and the final Evaluate context (consistency).
                var requestPresent = found;
                var gildFound = found
                    && _gildById.ContainsKey(request.TargetKey);
                var memberLimit = found && callerGild != null
                    && callerGild.CorpsIds.Count > 7;             // sub_7065FC -> 13
                var applicantFound = found
                    && _corpsById.ContainsKey(secondaryKey);      // join: applicant corps [4,5]
                var applicantInGild = found
                    && _corpsToGild.ContainsKey(secondaryKey);    // join: already in a gild -> 6
                var otherGildFound = found
                    && _gildById.ContainsKey(secondaryKey);       // union: requesting gild [4,5]
                var acceptorIsOwner = position == 4;              // union: owner of the accepting gild

                var gates = new NativeGildRequestSubtypeContext
                {
                    RequestPresent = requestPresent,
                    GildFound = gildFound,
                    GildMemberLimitReached = memberLimit,
                    ApplicantFound = applicantFound,
                    ApplicantAlreadyInGild = applicantInGild,
                    OtherGildFound = otherGildFound,
                    AcceptorIsGildOwner = acceptorIsOwner
                };

                var addToGildOk = false;
                var saveRelationResult = 0;
                if (NativeGildRequestResponseWiredTransaction
                        .AcceptReachesMutation(role, kind, found, gates))
                {
                    if (request.Kind == NativeGildRequestKind.JoinGild)
                    {
                        // add-corps-to-gild (sub_706264): the in-memory add is authoritative; the
                        // gildmember INSERT is fail-safe (best-effort, no rollback).
                        callerGild.CorpsIds.Add(secondaryKey);
                        _corpsToGild[secondaryKey] = callerGild.Id;
                        InsertGildMemberFailSafe(callerGild.Id, secondaryKey);
                        addToGildOk = true;
                    }
                    else if (request.Kind == NativeGildRequestKind.Union)
                    {
                        // 0x70821C `call 0x5E90A4` delete_relation then 0x70823A `B201 mov dl,1` /
                        // 0x70823C `call 0x5E6E60` save_relation: DELETE-3 then INSERT-1 on the canonical
                        // pair (no in-place UPDATE). The delete is what lets save_relation's 0x5E6F0D gate
                        // see a 0 for a pair that is currently holding the pending 3.
                        RemoveGildRelationLocked(relationKey);
                        var unionTime = DateTime.Now;
                        _gildRelations[relationKey] = (GildUnion, unionTime);
                        InsertGildRelationFailSafe(relationKey, GildUnion, unionTime);
                        saveRelationResult = 0;
                    }
                }

                var context = new NativeGildRequestSubtypeContext
                {
                    RequestPresent = requestPresent,
                    GildFound = gildFound,
                    GildMemberLimitReached = memberLimit,
                    ApplicantFound = applicantFound,
                    ApplicantAlreadyInGild = applicantInGild,
                    OtherGildFound = otherGildFound,
                    AcceptorIsGildOwner = acceptorIsOwner,
                    AddToGildOk = addToGildOk,
                    SaveRelationResult = saveRelationResult
                };
                var result = NativeGildRequestResponseWiredTransaction.Evaluate(
                    NativeGildRequestSubtypeOp.Accept, role, kind, found,
                    context);
                if (result == 0)
                    _requestLedger.RemoveByUniqueId(uniqueRequestId);
                return result;
            }
        }

        // sub_6A52A0(player CharID): the login SM 4613 sender (0x6F7638) looks up the
        // caller's own pending request by [obj+0x588/+0x58C]. Native holds at most one
        // (player[0xBA6] is a single pointer); the ledger entry is RequestId == CharID.
        internal bool TryGetOwnPendingRequest(long playerId,
            out NativeGildPendingRequest request)
        {
            request = null;
            if (playerId == 0) return false;
            var pending = _requestLedger.Snapshot(r => r.RequestId == playerId);
            if (pending.Count == 0) return false;
            request = pending[0];
            return true;
        }

        // 4627 CM_GILD_CANCEL_JOIN write (cancel my OWN pending gild join/union request) — native handler
        // sub_6ADB60 -> role strategy[+0x70] sub_703754. Handler gate: caller NOT in a corps (a1[698]==0)
        // -> 5 (only a corps captain / gild president ever files a gild request, so a player with no corps
        // has none to cancel). Strategy: sub_6A52A0 looks up the CALLER's OWN pending request; not found
        // -> 10; found -> the request's polymorphic subtype cancel (request.[vtbl+0x1C]) which removes it
        // and returns the subtype code (the normal self-cancel path returns 0). Classified by the reversed
        // pure ladder NativeGildCancelJoinTransaction (5/10/subtype). The caller's own request is the ledger
        // entry whose RequestId == the caller CharID (a player holds at most one pending request — native
        // player[0xBA6] is a single pointer). On success the request is removed from the ledger; a UNION
        // request does NOT release its pending Relation=3 pair: the union subtype's cancel is VMT
        // 0x707334+0x1C = sub_7084A8, whose whole body is 555/12/5 guards then sub_706608 (unlink from the
        // gild's pending list) + sub_6A5190 (unlink from the global registry) + `33C0 xor eax,eax`. It
        // never calls delete_relation, and sub_5E90A4 has exactly four callers image-wide (0x5E9208 war
        // expiry, 0x703DA3 break-union, 0x70809E refuse, 0x70821C accept) with zero dword references, so
        // the caller set is closed. A cancelled proposal therefore keeps blocking declare-war until the
        // pair is accepted, refused or broken.
        // FLAGGED (does NOT block the wiring): the polymorphic subtype cancel code is modeled as its 0
        // success value (the observable self-cancel outcome — request removed); the native subtype method's
        // rarer 12/555 edges + the pending-request UI clear (sub_6F769C) / applicant notify are DEFERRED,
        // exactly as the sibling accept/refuse SM 4612 notify. Callers gate on SupportsGildWrites.
        internal int ApplyGildCancelJoin(long operatorId)
        {
            lock (_sync)
            {
                var hasCorps = TryGetPlayerCorpsLocked(operatorId, out _);
                NativeGildPendingRequest ownRequest = null;
                if (hasCorps)
                    foreach (var request in _requestLedger.Snapshot(
                                 candidate => candidate.RequestId == operatorId))
                    {
                        ownRequest = request;
                        break;
                    }

                var outcome = NativeGildCancelJoinTransaction.Evaluate(
                    new NativeGildCancelJoinContext
                    {
                        HasPending = hasCorps,
                        RequestResolved = ownRequest != null,
                        SubtypeCancelResult = 0
                    });
                if (outcome.Result != 0) return outcome.Result;

                _requestLedger.RemoveByUniqueId(ownRequest.UniqueId);
                return outcome.Result;
            }
        }

        // Maps the caller's corps position to the request-response cascade role (native sub_6ADA3C 6-way):
        // 4=gild owner, 3=gild vice, 2=corps owner (corps_owner, cascade level 1), 1=corps vice
        // (corps_vice_owner, level 1), 0=member (level 0 -> 555); no corps -> NoCorps (555).
        private static NativeGildRole MapRequestResponseRoleLocked(
            bool hasCorps, byte position) =>
            !hasCorps
                ? NativeGildRole.NoCorps
                : position switch
                {
                    4 => NativeGildRole.GildOwner,
                    3 => NativeGildRole.GildVice,
                    2 => NativeGildRole.GildMember,
                    1 => NativeGildRole.Corps,
                    _ => NativeGildRole.Member
                };

        private void RebuildIndexes()
        {
            foreach (var corps in _corpsById.Values)
            foreach (var member in corps.Members)
            {
                if (member.MemberId == 0
                    || !_memberToCorps.TryAdd(member.MemberId, corps.Id))
                    throw new InvalidDataException(
                        $"duplicate Corps member ID {member.MemberId}");
            }

            foreach (var gild in _gildById.Values)
            foreach (var corpsId in gild.CorpsIds)
            {
                if (!_corpsById.ContainsKey(corpsId)
                    || !_corpsToGild.TryAdd(corpsId, gild.Id))
                    throw new InvalidDataException(
                        $"duplicate or missing Gild Corps ID {corpsId}");
            }

            // Gild-name -> id registry for the by-name resolvers (4585/4586,
            // native sub_5E76F0): case-insensitive (ASCII-uppercased) lookup over
            // the full in-memory gild set. Names are unique (AddGild rejects dups).
            _gildIdByUpperName.Clear();
            foreach (var gild in _gildById.Values)
            {
                var key = NativeGildNameResolver.Normalize(gild.Name);
                if (!string.IsNullOrEmpty(key))
                    _gildIdByUpperName[key] = gild.Id;
            }
        }

        private void SeedGildConcernsLocked(
            Dictionary<long, List<long>> gildConcerns)
        {
            if (gildConcerns == null) return;
            foreach (var (gildId, destinationGildIds) in gildConcerns)
            {
                if (!_gildById.ContainsKey(gildId)) continue;
                var set = GetOrCreateConcernSetLocked(gildId);
                foreach (var destinationGildId in destinationGildIds)
                    set.SeedFromLoad(destinationGildId);
            }
        }

        private NativeGildConcernSet GetConcernSetLocked(long gildId) =>
            _gildConcerns.TryGetValue(gildId, out var set) ? set : null;

        private NativeGildConcernSet GetOrCreateConcernSetLocked(long gildId)
        {
            if (!_gildConcerns.TryGetValue(gildId, out var set))
            {
                set = new NativeGildConcernSet();
                _gildConcerns[gildId] = set;
            }
            return set;
        }

        // Shared role classification for the gild write ops: position 4 = gild
        // president, 3 = gild vice (GetPositionLocked); anything in a gild but
        // lower is GildMember; not in a gild is Corps/NoCorps.
        private static NativeGildRole ResolveGildRoleLocked(bool hasCaller,
            bool hasGild, byte position) =>
            !hasGild
                ? (hasCaller ? NativeGildRole.Corps : NativeGildRole.NoCorps)
                : position == 4
                    ? NativeGildRole.GildOwner
                    : position == 3
                        ? NativeGildRole.GildVice
                        : NativeGildRole.GildMember;

        private bool TryGetPlayerCorpsLocked(long playerId,
            out NativeCorpsSnapshot corps)
        {
            corps = null;
            return _memberToCorps.TryGetValue(playerId, out var corpsId)
                   && _corpsById.TryGetValue(corpsId, out corps);
        }

        private bool TryGetPlayerGildLocked(long playerId,
            out NativeGildSnapshot gild)
        {
            gild = null;
            return _memberToCorps.TryGetValue(playerId, out var corpsId)
                   && _corpsToGild.TryGetValue(corpsId, out var gildId)
                   && _gildById.TryGetValue(gildId, out gild);
        }

        private byte GetPositionLocked(NativeCorpsSnapshot corps,
            long memberId)
        {
            if (corps == null) return 0;
            if (memberId == corps.OwnerId)
            {
                if (_corpsToGild.TryGetValue(corps.Id, out var gildId)
                    && _gildById.TryGetValue(gildId, out var gild))
                {
                    if (gild.OwnerCorpsId == corps.Id) return 4;
                    if (gild.ViceOwnerId == corps.Id) return 3;
                }
                return 2;
            }
            return memberId == corps.ViceOwner1Id
                   || memberId == corps.ViceOwner2Id
                ? (byte)1
                : (byte)0;
        }

        private static bool IsOfficer(NativeCorpsSnapshot corps,
            long memberId) => corps.OwnerId == memberId
                              || corps.ViceOwner1Id == memberId
                              || corps.ViceOwner2Id == memberId;

        // ------------------------------------------------------------------------------------------------
        // GILD-10: expiry of pending join-corps / join-gild / UNION (alliance) requests.
        //
        // Native purge = sub_6A5D6C @0x006A5D6C, reached from the wrapper sub_6A6058 (its only caller,
        // 0x006A6062). Tier-1 byte evidence:
        //   * TIME-OF-DAY GATE, before any work: FormatDateTime('hh:mm', Now) — literal 'hh:mm' at
        //     0x006A5FD8 (mov eax,6A5FD8) — is compared against the literal '03:03' at 0x006A5FE8
        //     (mov edx,6A5FE8) via the string-compare at 0x006A5DAD, then `0f 85 e3 01 00 00` (jnz ->
        //     return). So the sweep only ever runs during the 03:03 minute, ONCE PER DAY.
        //   * EXPIRY TEST, per entry: fld qword[eax+0x28] (the request timestamp) ... fsub
        //     dword[0x006A5FF0] where that float32 is 00 00 40 40 = 3.0 DAYS, then fcomp + jbe. Entry
        //     expires iff (Now - 3.0) > CreatedTime.
        //   * The list is walked BACKWARDS (Count-1 down to -1: `83 fb ff`) and each victim is torn down
        //     through the SAME helpers accept/refuse use (sub_6A60A4 @0x006A5EF0 + sub_6A5070 @0x006A5EF9).
        //   * The tally is logged only when at least one entry was dropped (`83 7d f8 00 / 7e 3b` =
        //     if count <= 0 skip) using the literals 0x006A5FFC + 0x006A6040.
        //
        // The sweep touches NO gild relation. Its per-victim teardown is only sub_6A60A4 (@0x6A5EF0) +
        // sub_6A5070 (@0x6A5EF9), which construct a status object and relink the list; delete_relation
        // sub_5E90A4 is absent from the whole 0x6A5Dxx..0x6A5Fxx body and its four image-wide callers
        // (0x5E9208 war expiry, 0x703DA3 break-union, 0x70809E refuse, 0x70821C accept) are all elsewhere,
        // with zero dword references to it. So an expired UNION request leaves its pending Relation=3 pair
        // in place — that pending 3 keeps blocking declare-war through save_relation's 0x5E6F0D gate until
        // the pair is accepted, refused or broken.
        internal int PurgeExpiredRequests(DateTime now)
        {
            lock (_sync)
            {
                return _requestLedger.RemoveExpired(now).Count;
            }
        }

        private void AddLogLocked(long corpsId, int type, string text)
        {
            var key = (corpsId, type);
            if (!_logs.TryGetValue(key, out var logs))
            {
                logs = new List<NativeCorpsLogEntry>();
                _logs.Add(key, logs);
            }
            logs.Insert(0, new NativeCorpsLogEntry(DateTime.Now, text));
            if (logs.Count > 512) logs.RemoveRange(512, logs.Count - 512);
        }
    }
}
