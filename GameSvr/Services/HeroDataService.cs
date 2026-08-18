using System.Buffers.Binary;
using System.Collections.Concurrent;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    internal static class HeroDataService
    {
        private const int LoadTimeout = 10_000;
        private const int SaveInterval = 15 * 60 * 1000;

        private sealed class PendingLoad
        {
            public int QueryId;
            public int OwnerId;
            public string Account;
            public string MasterName;
            public byte HeroKind;
            public byte HeroSlot;
            public int CreatedTick;
        }

        private sealed class LoadCompletion
        {
            public PendingLoad Pending;
            public NativeHeroLoadResponse Response;
            public string Error;
        }

        private sealed class PendingCreate
        {
            public long ReservationKey;
            public int QueryId;
            public int OwnerId;
            public string Account;
            public string MasterName;
            public string HeroName;
            public ushort HeroType;
            public int Code;
            public int CreatedTick;
        }

        private sealed class CreateCompletion
        {
            public PendingCreate Pending;
            public NativeHeroCreateResponse Response;
            public string Error;
        }

        private sealed class PendingRename
        {
            public int QueryId;
            public int OwnerId;
            public int CallbackNpcId;
            public bool CallbackRequiresExactPasRoute;
            public PasEngine.NpcPasScriptInteractionHandle CallbackPasInteraction;
            public string MasterName;
            public string OldHeroName;
            public string NewHeroName;
            public int Code;
            public int CreatedTick;
        }

        private sealed class RenameCompletion
        {
            public PendingRename Pending;
            public NativeHeroRenameResponse Response;
            public string Error;
        }

        private sealed class PendingSave
        {
            public string HeroName;
            public byte[] Frame;
        }

        private static readonly ConcurrentDictionary<int, PendingLoad> PendingLoads = new();
        private static readonly ConcurrentDictionary<int, int> PendingOwners = new();
        private static readonly ConcurrentQueue<LoadCompletion> LoadCompletions = new();
        private static readonly ConcurrentDictionary<int, PendingCreate> PendingCreates = new();
        private static readonly ConcurrentDictionary<long, byte> PendingCreateKeys = new();
        private static readonly ConcurrentQueue<CreateCompletion> CreateCompletions = new();
        private static readonly ConcurrentDictionary<int, PendingRename> PendingRenames = new();
        private static readonly ConcurrentDictionary<int, int> PendingRenameOwners = new();
        private static readonly ConcurrentQueue<RenameCompletion> RenameCompletions = new();
        // sub_713CBC queues every offline hero frame in send order. A keyed
        // dictionary would let a later ordinary save overwrite the switch frame
        // whose embedded +4694 slave was already written and consumed.
        private static readonly ConcurrentQueue<PendingSave> PendingSaves = new();
        private static readonly object PendingSaveFlushLock = new();
        private static readonly ConcurrentDictionary<int, int> LastSaveTicks = new();

        public static bool RequestLoad(TPlayObject owner, byte heroKind, byte heroSlot)
        {
            if (owner == null || owner.m_boGhost || owner.m_HeroObject != null ||
                M2Share.DataServer == null || PendingOwners.ContainsKey(owner.ObjectId)
                || PendingRenameOwners.ContainsKey(owner.ObjectId))
                return false;

            var request = new NativeHeroLoadRequest
            {
                HeroKind = heroKind,
                HeroSlot = heroSlot,
                Account = owner.m_sUserID,
                MasterName = owner.m_sCharName
            };
            if (!NativeHeroDbFrameCodec.TryEncodeLoadRequest(request, out var frame, out var error))
            {
                M2Share.ErrorMessage($"[HeroDB] 英雄加载请求编码失败 {owner.m_sCharName}: {error}");
                return false;
            }

            var queryId = M2Share.DataServer.NextQueryId();
            var pending = new PendingLoad
            {
                QueryId = queryId,
                OwnerId = owner.ObjectId,
                Account = owner.m_sUserID,
                MasterName = owner.m_sCharName,
                HeroKind = heroKind,
                HeroSlot = heroSlot,
                CreatedTick = HUtil32.GetTickCount()
            };
            if (!PendingOwners.TryAdd(owner.ObjectId, queryId))
                return false;
            if (!PendingLoads.TryAdd(queryId, pending))
            {
                RemoveOwnerPending(pending);
                return false;
            }

            if (M2Share.DataServer.SendNativeFrame(frame))
                return true;

            RemovePending(pending);
            M2Share.ErrorMessage($"[HeroDB] DBServer未连接，英雄加载请求未发送: {owner.m_sCharName}");
            return false;
        }

        public static int RequestCreate(TPlayObject owner, string heroName,
            int heroType, int code)
        {
            if (owner == null || heroType is < 1 or > 2)
                return -4;
            if (HasHeroType(owner, heroType))
                return SendCreateLocalResult(owner, -1);
            if (code is < 1 or > 6)
                return SendCreateLocalResult(owner, -2);
            if (heroType == 1 && !ValidatePrimaryHeroName(heroName))
                return SendCreateLocalResult(owner, -3);

            var request = new NativeHeroCreateRequest
            {
                HeroType = (ushort)heroType,
                Code = code,
                Account = owner.m_sUserID,
                MasterName = owner.m_sCharName,
                HeroName = heroName
            };
            if (!NativeHeroDbFrameCodec.TryEncodeCreateRequest(
                    request, out var frame, out var error))
            {
                M2Share.ErrorMessage(
                    $"[HeroDB] 英雄创建请求编码失败 {owner.m_sCharName}/{heroName}: {error}");
                return SendCreateLocalResult(owner, -3);
            }

            var dataServer = M2Share.DataServer;
            if (dataServer == null)
                return SendCreateLocalResult(owner, -1);
            var reservationKey = CreateReservationKey(owner.ObjectId, heroType);
            if (!PendingCreateKeys.TryAdd(reservationKey, 0))
                return SendCreateLocalResult(owner, -1);

            var queryId = dataServer.NextQueryId();
            var pending = new PendingCreate
            {
                ReservationKey = reservationKey,
                QueryId = queryId,
                OwnerId = owner.ObjectId,
                Account = owner.m_sUserID,
                MasterName = owner.m_sCharName,
                HeroName = heroName,
                HeroType = (ushort)heroType,
                Code = code,
                CreatedTick = HUtil32.GetTickCount()
            };
            if (!PendingCreates.TryAdd(queryId, pending))
            {
                RemoveCreateReservation(pending);
                return SendCreateLocalResult(owner, -1);
            }

            if (!dataServer.SendNativeFrame(frame))
            {
                PendingCreates.TryRemove(queryId, out _);
                RemoveCreateReservation(pending);
                M2Share.ErrorMessage(
                    $"[HeroDB] DBServer未连接，英雄创建请求未发送: {owner.m_sCharName}/{heroName}");
                return SendCreateLocalResult(owner, -1);
            }
            return SendCreateLocalResult(owner, 0);
        }

        public static bool RequestDelete(TPlayObject owner)
        {
            if (owner == null || owner.m_boGhost || owner.m_HeroObject != null
                || (owner.m_btNativeHeroState & 0x03) == 0
                || PendingRenameOwners.ContainsKey(owner.ObjectId))
                return false;

            var request = new NativeHeroDeleteRequest
            {
                Account = owner.m_sUserID,
                MasterName = owner.m_sCharName,
                HeroName = string.Empty
            };
            if (!NativeHeroDbFrameCodec.TryEncodeDeleteRequest(
                    request, out var frame, out var error))
            {
                M2Share.ErrorMessage(
                    $"[HeroDB] 英雄删除请求编码失败 {owner.m_sCharName}: {error}");
                return false;
            }

            var dataServer = M2Share.DataServer;
            if (dataServer == null) return false;
            if (!dataServer.SendNativeFrame(frame))
            {
                M2Share.ErrorMessage(
                    $"[HeroDB] DBServer未连接，英雄删除请求未发送: {owner.m_sCharName}");
                return false;
            }

            owner.m_btNativeHeroState &= 0xFC;
            if (!owner.PersistNativeHeroState())
            {
                M2Share.ErrorMessage(
                    $"[HeroDB] 人物原生ScriptData过短，英雄删除状态无法持久化: {owner.m_sCharName}");
            }
            return true;
        }

        public static bool RequestRename(TPlayObject owner, string oldHeroName,
            string newHeroName, NormNpc callbackNpc = null)
        {
            if (owner == null || owner.m_boGhost || owner.m_HeroObject != null
                || (owner.m_btNativeHeroState & 0x03) == 0
                || string.IsNullOrEmpty(oldHeroName) || string.IsNullOrEmpty(newHeroName)
                || PendingOwners.ContainsKey(owner.ObjectId))
                return false;

            var dataServer = M2Share.DataServer;
            if (dataServer == null || !PendingRenameOwners.TryAdd(owner.ObjectId, 0))
                return false;

            var queryId = dataServer.NextQueryId();
            var request = new NativeHeroRenameRequest
            {
                SelectionMode = 1,
                Code = callbackNpc?.ObjectId ?? 0,
                OldHeroName = oldHeroName,
                MasterName = owner.m_sCharName,
                NewHeroName = newHeroName
            };
            if (!NativeHeroDbFrameCodec.TryEncodeRenameRequest(
                    request, out var frame, out var error))
            {
                PendingRenameOwners.TryRemove(owner.ObjectId, out _);
                M2Share.ErrorMessage(
                    $"[HeroDB] 英雄改名请求编码失败 {owner.m_sCharName}/{oldHeroName}: {error}");
                return false;
            }

            var pending = new PendingRename
            {
                QueryId = queryId,
                OwnerId = owner.ObjectId,
                CallbackNpcId = callbackNpc?.ObjectId ?? 0,
                MasterName = owner.m_sCharName,
                OldHeroName = oldHeroName,
                NewHeroName = newHeroName,
                Code = request.Code,
                CreatedTick = HUtil32.GetTickCount()
            };
            if (callbackNpc != null && M2Share.PasEngine != null)
            {
                M2Share.PasEngine.TryCaptureNpcInteraction(owner, callbackNpc,
                    out pending.CallbackPasInteraction,
                    out var callbackRouteKind);
                pending.CallbackRequiresExactPasRoute = callbackRouteKind
                    != PasEngine.NpcPasScriptResolutionKind.Legacy;
            }
            PendingRenameOwners[owner.ObjectId] = queryId;
            if (!PendingRenames.TryAdd(queryId, pending))
            {
                RemoveRenameOwner(pending);
                return false;
            }

            if (dataServer.SendNativeFrame(frame))
                return true;

            PendingRenames.TryRemove(queryId, out _);
            RemoveRenameOwner(pending);
            M2Share.ErrorMessage(
                $"[HeroDB] DBServer未连接，英雄改名请求未发送: {owner.m_sCharName}/{oldHeroName}");
            return false;
        }

        public static bool TryAddNativeResponse(byte[] wire)
        {
            if (!LegacyDbServerFrameCodec.TryDecode(wire, out var frame, out _)
                || frame.Type != 1
                || frame.Payload.Length < NativeHeroDbFrameCodec.MessageHeaderSize)
                return false;

            var command = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload);
            switch (command)
            {
                case NativeHeroDbFrameCodec.LoadResponseCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeLoadResponse(
                            wire, out var loadResponse, out var loadError))
                    {
                        M2Share.ErrorMessage(
                            "[HeroDB] 原生英雄加载响应解码失败: " + loadError);
                        return true;
                    }
                    if (!TryTakePendingLoad(loadResponse, out var pendingLoad))
                        return true;
                    RemoveOwnerPending(pendingLoad);
                    LoadCompletions.Enqueue(new LoadCompletion
                    {
                        Pending = pendingLoad,
                        Response = loadResponse
                    });
                    return true;

                case NativeHeroDbFrameCodec.CreateResponseCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeCreateResponse(
                            wire, out var createResponse, out var createError))
                    {
                        M2Share.ErrorMessage(
                            "[HeroDB] 原生英雄创建响应解码失败: " + createError);
                        return true;
                    }
                    if (!TryTakePendingCreate(createResponse, out var pendingCreate))
                        return true;
                    CreateCompletions.Enqueue(new CreateCompletion
                    {
                        Pending = pendingCreate,
                        Response = createResponse
                    });
                    return true;

                case NativeHeroDbFrameCodec.DeleteResponseCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeDeleteResponse(
                            wire, out _, out var deleteError))
                    {
                        M2Share.ErrorMessage(
                            "[HeroDB] 原生英雄删除响应解码失败: " + deleteError);
                    }
                    return true;

                case NativeHeroDbFrameCodec.RenameResponseCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeRenameResponse(
                            wire, out var renameResponse, out var renameError))
                    {
                        M2Share.ErrorMessage(
                            "[HeroDB] 原生英雄改名响应解码失败: " + renameError);
                        return true;
                    }
                    if (!TryTakePendingRename(renameResponse, out var pendingRename))
                        return true;
                    RenameCompletions.Enqueue(new RenameCompletion
                    {
                        Pending = pendingRename,
                        Response = renameResponse
                    });
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryTakePendingLoad(NativeHeroLoadResponse response,
            out PendingLoad pending)
        {
            foreach (var pair in PendingLoads)
            {
                if (!string.Equals(response.MasterName, pair.Value.MasterName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (((ICollection<KeyValuePair<int, PendingLoad>>)PendingLoads)
                    .Remove(pair))
                {
                    pending = pair.Value;
                    return true;
                }
            }
            pending = null;
            return false;
        }

        private static bool TryTakePendingCreate(NativeHeroCreateResponse response,
            out PendingCreate pending)
        {
            foreach (var pair in PendingCreates)
            {
                var candidate = pair.Value;
                if (response.HeroType != candidate.HeroType
                    || !string.Equals(response.MasterName, candidate.MasterName,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(response.HeroName, candidate.HeroName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (((ICollection<KeyValuePair<int, PendingCreate>>)PendingCreates)
                    .Remove(pair))
                {
                    pending = candidate;
                    return true;
                }
            }
            pending = null;
            return false;
        }

        private static bool TryTakePendingRename(NativeHeroRenameResponse response,
            out PendingRename pending)
        {
            foreach (var pair in PendingRenames)
            {
                var candidate = pair.Value;
                if (response.Code != candidate.Code
                    || !string.Equals(response.MasterName, candidate.MasterName,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(response.NewHeroName, candidate.NewHeroName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (((ICollection<KeyValuePair<int, PendingRename>>)PendingRenames)
                    .Remove(pair))
                {
                    pending = candidate;
                    return true;
                }
            }
            pending = null;
            return false;
        }

        public static bool QueueSave(HeroObject hero, ushort saveMode = 0)
        {
            if (hero == null)
                return false;
            var nativeSwitchSlave = hero.GetNativeSwitchSlaveForSave();
            if (!NativeHeroRuntimeCodec.TryCreateSnapshot(hero, out var record,
                    out var dynamicData, out var error))
            {
                M2Share.ErrorMessage($"[HeroDB] 拒绝保存英雄 {hero.m_sCharName}: {error}");
                return false;
            }

            // sub_689034 calls VMT+0x84 immediately after the fixed +4694
            // record is complete. Its caller only copies the dynamic payload
            // and invokes sub_713554 afterwards. Keep TryCreateSnapshot pure
            // for rollback callers, and perform the side effect here before
            // the outbound request is encoded or queued.
            nativeSwitchSlave?.Die();

            var request = new NativeHeroSaveRequest
            {
                SaveMode = saveMode,
                Param1 = 0,
                Param2 = 0,
                Record = record,
                DynamicData = dynamicData
            };
            if (!NativeHeroDbFrameCodec.TryEncodeSaveRequest(request, out var frame, out error))
            {
                M2Share.ErrorMessage($"[HeroDB] 英雄保存请求编码失败 {hero.m_sCharName}: {error}");
                return false;
            }

            PendingSaves.Enqueue(new PendingSave
            {
                HeroName = record.HeroName,
                Frame = frame
            });
            if (saveMode != 0)
                LastSaveTicks.TryRemove(hero.ObjectId, out _);
            return true;
        }

        public static void QueuePeriodicSave(HeroObject hero, int currentTick)
        {
            if (hero == null || hero.m_boGhost || hero.NativeHeroState == null)
                return;
            var lastTick = LastSaveTicks.GetOrAdd(hero.ObjectId, currentTick);
            if (currentTick - lastTick < SaveInterval)
                return;
            if (QueueSave(hero))
                LastSaveTicks.TryUpdate(hero.ObjectId, currentTick, lastTick);
        }

        public static void Process(UserEngine userEngine)
        {
            ProcessCreateCompletions();
            ProcessRenameCompletions();
            ProcessLoadCompletions(userEngine);
            ExpireLoads();
            ExpireCreates();
            ExpireRenames();
            FlushSaves();
        }

        public static void FlushPendingSaves()
        {
            FlushSaves();
        }

        public static bool FlushPendingSavesAndWait(int timeoutMilliseconds)
        {
            var deadline = Environment.TickCount64 + Math.Max(0, timeoutMilliseconds);
            do
            {
                FlushSaves();
                if (PendingSaves.IsEmpty)
                    return true;
                M2Share.DataServer?.CheckConnected();
                Thread.Sleep(25);
            } while (Environment.TickCount64 < deadline);

            M2Share.ErrorMessage(
                $"[HeroDB] 英雄保存帧未进入DBService队列: pending={PendingSaves.Count}");
            return false;
        }

        public static void NotifyDisconnected()
        {
            foreach (var pair in PendingCreates)
            {
                if (!PendingCreates.TryRemove(pair.Key, out var pending))
                    continue;
                CreateCompletions.Enqueue(new CreateCompletion
                {
                    Pending = pending,
                    Error = "DBServer connection was lost"
                });
            }
            foreach (var pair in PendingRenames)
            {
                if (!PendingRenames.TryRemove(pair.Key, out var pending))
                    continue;
                RenameCompletions.Enqueue(new RenameCompletion
                {
                    Pending = pending,
                    Error = "DBServer connection was lost"
                });
            }
        }

        private static void ProcessLoadCompletions(UserEngine userEngine)
        {
            while (LoadCompletions.TryDequeue(out var completion))
            {
                var pending = completion.Pending;
                var owner = M2Share.ObjectManager?.Get(pending.OwnerId) as TPlayObject;
                if (owner == null || owner.m_boGhost || owner.m_HeroObject != null ||
                    !string.Equals(owner.m_sUserID, pending.Account, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(owner.m_sCharName, pending.MasterName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(completion.Error))
                {
                    M2Share.ErrorMessage($"[HeroDB] 英雄加载响应无效 {pending.MasterName}: {completion.Error}");
                    continue;
                }

                var response = completion.Response;
                if (response == null || response.Status != 1)
                {
                    M2Share.ErrorMessage($"[HeroDB] 英雄加载失败 {pending.MasterName}: status={response?.Status ?? 0}");
                    continue;
                }
                if (!string.Equals(response.MasterName, pending.MasterName, StringComparison.OrdinalIgnoreCase))
                {
                    M2Share.ErrorMessage($"[HeroDB] 英雄加载主人不匹配: {response.MasterName}/{pending.MasterName}");
                    continue;
                }

                var hero = new HeroObject();
                if (!NativeHeroRuntimeCodec.TryApply(hero, response.Record, response.DynamicData, out var error))
                {
                    M2Share.ErrorMessage($"[HeroDB] 英雄记录还原失败 {pending.MasterName}: {error}");
                    hero.m_boGhost = true;
                    hero.ReleaseRuntimeReferences();
                    M2Share.ObjectManager?.Remove(hero.ObjectId);
                    continue;
                }
                if (!userEngine.RegisterHero(owner, hero))
                {
                    M2Share.ErrorMessage($"[HeroDB] 英雄注册失败 {response.HeroName}");
                    continue;
                }

                LastSaveTicks[hero.ObjectId] = HUtil32.GetTickCount();
                SetHeroTypeState(owner, hero.HeroType);
                if (hero.HeroType == 2 && owner.m_btSecHeroPracticeCostTier != 0)
                    owner.StopSecHeroPractice();
                hero.SendHeroLogon();
            }
        }

        private static void ProcessCreateCompletions()
        {
            while (CreateCompletions.TryDequeue(out var completion))
            {
                var pending = completion.Pending;
                try
                {
                    var owner = M2Share.ObjectManager?.Get(pending.OwnerId) as TPlayObject;
                    if (owner == null || owner.m_boGhost
                        || !string.Equals(owner.m_sUserID, pending.Account,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(owner.m_sCharName, pending.MasterName,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(completion.Error))
                    {
                        M2Share.ErrorMessage(
                            $"[HeroDB] 英雄创建响应无效 {pending.MasterName}/{pending.HeroName}: {completion.Error}");
                        owner.SysMsg("您的英雄创建失败，稍后再试...",
                            MsgColor.Red, MsgType.Hint);
                        continue;
                    }

                    var response = completion.Response;
                    if (response == null
                        || response.HeroType != pending.HeroType
                        || !string.Equals(response.MasterName, pending.MasterName,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(response.HeroName, pending.HeroName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        M2Share.ErrorMessage(
                            $"[HeroDB] 英雄创建响应身份不匹配: {pending.MasterName}/{pending.HeroName}");
                        owner.SysMsg("您的英雄创建失败，稍后再试...",
                            MsgColor.Red, MsgType.Hint);
                        continue;
                    }
                    if (response.Result > 0)
                    {
                        SetHeroTypeState(owner, response.HeroType);
                    }
                    var message = response.Result switch
                    {
                        > 0 => "您的英雄创建成功！",
                        -1 => "英雄的名字不能包含非法的字符。",
                        -2 => "英雄的名字不能与其他玩家同名。",
                        -3 => "这个名字已经被其他英雄使用了。",
                        -4 => "您已经有英雄了。",
                        _ => "您的英雄创建失败，稍后再试..."
                    };
                    owner.SysMsg(message,
                        response.Result > 0 ? MsgColor.Green : MsgColor.Red, MsgType.Hint);
                    if (response.Result <= 0)
                    {
                        M2Share.ErrorMessage(
                            $"[HeroDB] 英雄创建失败 master={pending.MasterName} hero={pending.HeroName} result={response.Result}");
                    }
                }
                finally
                {
                    RemoveCreateReservation(pending);
                }
            }
        }

        private static void ProcessRenameCompletions()
        {
            while (RenameCompletions.TryDequeue(out var completion))
            {
                var pending = completion.Pending;
                try
                {
                    var owner = M2Share.ObjectManager?.Get(pending.OwnerId) as TPlayObject;
                    if (owner == null || owner.m_boGhost
                        || !string.Equals(owner.m_sCharName, pending.MasterName,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    var response = completion.Response;
                    if (!string.IsNullOrEmpty(completion.Error)
                        || response == null
                        || response.Code != pending.Code
                        || !string.Equals(response.MasterName, pending.MasterName,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(response.NewHeroName, pending.NewHeroName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        M2Share.ErrorMessage(
                            $"[HeroDB] 英雄改名响应无效 {pending.MasterName}/{pending.OldHeroName}: " +
                            (completion.Error ?? "response identity mismatch"));
                        SendRenameResult(owner, pending, 0);
                        continue;
                    }

                    if (response.Result == 1 && owner.m_HeroObject != null
                        && string.Equals(owner.m_HeroObject.m_sCharName,
                            pending.OldHeroName, StringComparison.OrdinalIgnoreCase)
                        && !NativeHeroRuntimeCodec.TryRename(owner.m_HeroObject,
                            pending.NewHeroName, out var renameError))
                    {
                        M2Share.ErrorMessage(
                            $"[HeroDB] 英雄改名已入库但运行时快照同步失败 " +
                            $"{pending.MasterName}/{pending.OldHeroName}: {renameError}");
                    }
                    SendRenameResult(owner, pending, response.Result);
                }
                finally
                {
                    RemoveRenameOwner(pending);
                }
            }
        }

        private static void ExpireLoads()
        {
            var currentTick = HUtil32.GetTickCount();
            foreach (var pair in PendingLoads)
            {
                if (currentTick - pair.Value.CreatedTick < LoadTimeout ||
                    !PendingLoads.TryRemove(pair.Key, out var pending))
                    continue;
                RemoveOwnerPending(pending);
                M2Share.ErrorMessage($"[HeroDB] 英雄加载超时: {pending.MasterName}");
            }
        }

        private static void ExpireCreates()
        {
            var currentTick = HUtil32.GetTickCount();
            foreach (var pair in PendingCreates)
            {
                if (currentTick - pair.Value.CreatedTick < LoadTimeout
                    || !PendingCreates.TryRemove(pair.Key, out var pending))
                    continue;
                M2Share.ErrorMessage(
                    $"[HeroDB] 英雄创建响应超时: {pending.MasterName}/{pending.HeroName}");
                CreateCompletions.Enqueue(new CreateCompletion
                {
                    Pending = pending,
                    Error = "native hero create response timed out"
                });
            }
        }

        private static void ExpireRenames()
        {
            var currentTick = HUtil32.GetTickCount();
            foreach (var pair in PendingRenames)
            {
                if (currentTick - pair.Value.CreatedTick < LoadTimeout
                    || !PendingRenames.TryRemove(pair.Key, out var pending))
                    continue;
                M2Share.ErrorMessage(
                    $"[HeroDB] 英雄改名响应超时: {pending.MasterName}/{pending.OldHeroName}");
                RenameCompletions.Enqueue(new RenameCompletion
                {
                    Pending = pending,
                    Error = "native hero rename response timed out"
                });
            }
        }

        private static void FlushSaves()
        {
            var dataServer = M2Share.DataServer;
            if (dataServer == null) return;
            lock (PendingSaveFlushLock)
            {
                while (PendingSaves.TryPeek(out var pending))
                {
                    try
                    {
                        if (!dataServer.SendNativeFrame(pending.Frame))
                            return;
                    }
                    catch (Exception ex)
                    {
                        M2Share.ErrorMessage($"[HeroDB] 英雄保存发送失败 {pending.HeroName}: {ex.Message}");
                        return;
                    }

                    // QueueSave only appends; the flush lock ensures that the
                    // successfully accepted head is the exact frame removed.
                    if (!PendingSaves.TryDequeue(out var accepted)
                        || !ReferenceEquals(accepted, pending))
                    {
                        throw new InvalidOperationException(
                            "hero save FIFO head changed during flush");
                    }
                }
            }
        }

        private static void RemovePending(PendingLoad pending)
        {
            PendingLoads.TryRemove(pending.QueryId, out _);
            RemoveOwnerPending(pending);
        }

        private static void RemoveOwnerPending(PendingLoad pending)
        {
            ((ICollection<KeyValuePair<int, int>>)PendingOwners)
                .Remove(new KeyValuePair<int, int>(pending.OwnerId, pending.QueryId));
        }

        private static bool HasHeroType(TPlayObject owner, int heroType)
            => (owner.m_btNativeHeroState & HeroPresenceMask(heroType)) != 0
               || owner.m_HeroObject?.HeroType == heroType;

        private static long CreateReservationKey(int ownerId, int heroType)
            => ((long)(uint)ownerId << 32) | (uint)heroType;

        private static void RemoveCreateReservation(PendingCreate pending)
        {
            if (pending == null) return;
            PendingCreateKeys.TryRemove(pending.ReservationKey, out _);
        }

        private static void RemoveRenameOwner(PendingRename pending)
        {
            if (pending == null) return;
            ((ICollection<KeyValuePair<int, int>>)PendingRenameOwners)
                .Remove(new KeyValuePair<int, int>(pending.OwnerId, pending.QueryId));
        }

        private static void SendRenameResult(TPlayObject owner, PendingRename pending,
            int result)
        {
            var callbackNpc = pending.CallbackNpcId > 0
                ? M2Share.ObjectManager?.Get(pending.CallbackNpcId) as NormNpc
                : null;
            if (callbackNpc != null)
            {
                if (pending.CallbackPasInteraction != null
                    && M2Share.PasEngine?.TryCallNpcProcedure(
                        pending.CallbackPasInteraction,
                        new[]
                        {
                            "_TriggerHeroRename", "TriggerHeroRename"
                        }, out _, PasEngine.PasValue.FromInt(result),
                        PasEngine.PasValue.FromString(pending.NewHeroName))
                    == true)
                {
                    return;
                }
                if (!pending.CallbackRequiresExactPasRoute
                    && pending.CallbackPasInteraction == null
                    && callbackNpc.TryCallPascalCallback(owner,
                             "TriggerHeroRename",
                             PasEngine.PasValue.FromInt(result),
                             PasEngine.PasValue.FromString(
                                 pending.NewHeroName)))
                {
                    return;
                }
            }

            var message = result switch
            {
                1 => $"英雄已重命名为 {pending.NewHeroName}。",
                2 => "英雄的名字不能包含非法字符。",
                3 => "英雄的名字不能与其他玩家同名。",
                4 => "这个名字已经被其他英雄使用了。",
                _ => "英雄改名失败，请稍后再试。"
            };
            owner.SysMsg(message, result == 1 ? MsgColor.Green : MsgColor.Red, MsgType.Hint);
        }

        private static bool ValidatePrimaryHeroName(string heroName)
        {
            if (string.IsNullOrEmpty(heroName)) return false;
            var byteCount = System.Text.Encoding.GetEncoding(936).GetByteCount(heroName);
            return byteCount is >= 4 and <= 14
                   && heroName[0] is not ('+' or '-' or '/' or '\\');
        }

        private static byte HeroTypeMask(int heroType) => heroType == 1 ? (byte)1 : (byte)2;

        private static byte HeroPresenceMask(int heroType)
            // native sub_6C9C00 uses mask 0x0B = (0x01|0x02|0x08) for type-2:
            // bit 0x01 = type-1 already owned → blocks type-2 creation while type-1 exists.
            // Live was 0x0A (2|8), missing 0x01, allowing a dual-hero bug.
            => heroType == 1 ? (byte)(1 | 4) : (byte)(1 | 2 | 8);

        private static void SetHeroTypeState(TPlayObject owner, int heroType)
        {
            owner.m_btNativeHeroState |= HeroTypeMask(heroType);
            if (!owner.PersistNativeHeroState())
            {
                M2Share.ErrorMessage(
                    $"[HeroDB] 人物原生ScriptData过短，英雄状态无法持久化: {owner.m_sCharName}");
            }
        }

        private static int SendCreateLocalResult(TPlayObject owner, int result)
        {
            owner.SendDefMessage(Grobal2.SM_BUILDHERO, result, 0, 0, 0, string.Empty);
            return result;
        }
    }
}
