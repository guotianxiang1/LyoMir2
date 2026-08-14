using System.Globalization;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Production owner for dynamic-room definitions, physical instances, and
    /// activation entry points. All public operations resolve exact environment
    /// references; static map-name lookup is never used for room actors.
    /// </summary>
    public sealed class NativeDynamicRoomService
    {
        private sealed class DefinitionEntry
        {
            public NativeDynamicRoomDefinition Definition { get; init; }
            public NativeDynamicRoomDynamicNpcScriptBinding[] Bindings { get; init; }
            public int TargetPhysicalCount { get; init; }
            public int MinimumActiveMinutes { get; init; }
            public int NextPhysicalInstanceId { get; set; }
            public List<PhysicalEntry> PhysicalRooms { get; } = new();
        }

        private sealed class PhysicalEntry
        {
            public DefinitionEntry DefinitionEntry { get; init; }
            public Envirnoment Environment { get; init; }
            public int PhysicalInstanceId { get; init; }
            public NativeDynamicRoomPhysicalNpcOwnership Ownership { get; init; }
            public NativeDynamicRoomMaterializedNpc[] Npcs { get; init; }
        }

        private readonly object _syncRoot = new();
        private readonly NativeDynamicRoomManager _manager;
        private readonly NativeDynamicRoomRuntime _runtime;
        private readonly NativeDynamicRoomNpcOwner _npcOwner;
        private readonly INativeDynamicRoomNpcMaterializer _materializer;
        private readonly EventManager _eventManager;
        private readonly ObjectManager _objectManager;
        private readonly UserEngine _userEngine;
        private readonly Dictionary<string, DefinitionEntry> _definitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<Envirnoment, PhysicalEntry> _physicalRooms =
            new(ReferenceEqualityComparer.Instance);
        private string _mapDirectory;
        private int _serverIndex;
        private bool _initialized;

        public NativeDynamicRoomService(NativeDynamicRoomManager manager,
            NativeDynamicRoomRuntime runtime,
            NativeDynamicRoomNpcOwner npcOwner,
            INativeDynamicRoomNpcMaterializer materializer,
            EventManager eventManager, ObjectManager objectManager,
            UserEngine userEngine)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(runtime);
            ArgumentNullException.ThrowIfNull(npcOwner);
            ArgumentNullException.ThrowIfNull(materializer);
            ArgumentNullException.ThrowIfNull(eventManager);
            ArgumentNullException.ThrowIfNull(objectManager);
            ArgumentNullException.ThrowIfNull(userEngine);
            _manager = manager;
            _runtime = runtime;
            _npcOwner = npcOwner;
            _materializer = materializer;
            _eventManager = eventManager;
            _objectManager = objectManager;
            _userEngine = userEngine;
        }

        public bool IsInitialized
        {
            get
            {
                lock (_syncRoot) return _initialized;
            }
        }

        public int DefinitionCount
        {
            get
            {
                lock (_syncRoot) return _definitions.Count;
            }
        }

        public int PhysicalRoomCount
        {
            get
            {
                lock (_syncRoot) return _physicalRooms.Count;
            }
        }

        public bool TryInitializeFromFiles(string envirDirectory,
            string mapDirectory, int serverIndex,
            out IReadOnlyList<string> diagnostics)
        {
            var messages = new List<string>();
            diagnostics = Array.Empty<string>();
            if (serverIndex < 0 || string.IsNullOrWhiteSpace(envirDirectory)
                || string.IsNullOrWhiteSpace(mapDirectory))
            {
                diagnostics = new[]
                {
                    "dynamic room startup paths or server index are invalid"
                };
                return false;
            }

            string envirRoot;
            string mapRoot;
            try
            {
                envirRoot = Path.GetFullPath(envirDirectory);
                mapRoot = Path.GetFullPath(mapDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or NotSupportedException
                                       or PathTooLongException)
            {
                diagnostics = new[]
                {
                    $"dynamic room startup path is invalid ({ex.GetType().Name})"
                };
                return false;
            }

            lock (_syncRoot)
            {
                if (_initialized)
                {
                    diagnostics = Array.Empty<string>();
                    return true;
                }

                if (!NativeDynamicRoomDefinitionLoader.TryLoad(
                        Path.Combine(envirRoot, "PsDynNpc.txt"),
                        out var definitions, out var loadErrors))
                {
                    diagnostics = loadErrors;
                    return false;
                }
                var mapErrors = NativeDynamicRoomDefinitionLoader
                    .ValidateMapFiles(definitions, mapRoot);
                if (mapErrors.Count > 0)
                {
                    diagnostics = mapErrors;
                    return false;
                }
                if (!NativeDynamicRoomDynamicNpcScriptBindingPlanner
                        .TryPlanBindings(definitions, envirRoot, serverIndex,
                            out var bindings, out var bindingErrors))
                {
                    diagnostics = bindingErrors;
                    return false;
                }

                var seenNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var definition in definitions)
                {
                    if (definition == null
                        || !seenNames.Add(definition.RoomName)
                        || !TryParseStartupNumber(definition.RawRoomCount,
                            minimum: 1, maximum: 1024,
                            out var physicalCount)
                        || !TryParseStartupNumber(
                            definition.RawBalanceCount, minimum: 0,
                            maximum: int.MaxValue / 60_000,
                            out var minimumActiveMinutes))
                    {
                        messages.Add(
                            $"room {definition?.RoomName ?? "<null>"}: invalid name, room count, or balance time");
                        continue;
                    }

                    var exactBindings = bindings.Where(binding =>
                            ReferenceEquals(binding.Definition, definition))
                        .ToArray();
                    var definitionEntry = new DefinitionEntry
                    {
                        Definition = definition,
                        Bindings = exactBindings,
                        TargetPhysicalCount = physicalCount,
                        MinimumActiveMinutes = minimumActiveMinutes
                    };
                    _definitions.Add(definition.RoomName, definitionEntry);
                }
                if (messages.Count > 0
                    || _definitions.Count != definitions.Count)
                {
                    diagnostics = messages.AsReadOnly();
                    return false;
                }

                _mapDirectory = mapRoot;
                _serverIndex = serverIndex;
                foreach (var definitionEntry in _definitions.Values)
                {
                    for (var index = 0;
                         index < definitionEntry.TargetPhysicalCount; index++)
                    {
                        if (!TryCreatePhysicalRoomLocked(definitionEntry,
                                out var error))
                        {
                            messages.Add(error);
                            diagnostics = messages.AsReadOnly();
                            return false;
                        }
                    }
                }

                _initialized = true;
                diagnostics = Array.Empty<string>();
                return true;
            }
        }

        public bool TryReserveActivatedRoom(string roomName,
            TPlayObject owner, out int roomIndex)
        {
            roomIndex = -1;
            if (owner != null && !IsEligiblePlayer(owner))
                return false;
            if (TryReserveActivatedLease(roomName, owner, out var lease))
            {
                roomIndex = lease.Index;
                return true;
            }

            BroadcastNativeDynamicRoomUnavailable(roomName);
            return false;
        }

        public int FlyToDynamicRoom(TPlayObject player, string roomName,
            int x, int y)
        {
            if (!IsEligiblePlayer(player)
                || !TryReserveActivatedLease(roomName, player, out var lease))
            {
                BroadcastNativeDynamicRoomUnavailable(roomName);
                return -1;
            }

            player.TrySpaceMoveToEnvironment(lease.Environment,
                unchecked((short)x), unchecked((short)y), 0);
            return lease.Index;
        }

        public bool FlyToDynamicRoomIndex(TPlayObject player,
            string roomName, int roomIndex, int x, int y)
        {
            if (!IsEligiblePlayer(player)
                || !_manager.TryGetActiveRoom(roomName, roomIndex,
                    out var environment))
                return false;

            player.TrySpaceMoveToEnvironment(environment,
                unchecked((short)x), unchecked((short)y), 0);
            // Native reports successful dispatch after exact active lookup; the
            // lower movement method owns coordinate fallback and rollback.
            return true;
        }

        public bool GroupFlyToDynamicRoom(TPlayObject caller,
            string roomName, int roomIndex)
        {
            if (caller == null) return false;
            var groupLeader = caller.m_GroupOwner ?? caller;
            if (groupLeader == null) return true;
            var sourceEnvironment = groupLeader.m_PEnvir;
            if (sourceEnvironment == null) return true;

            TPlayObject[] members;
            try
            {
                members = groupLeader.m_GroupMembers.ToArray();
            }
            catch
            {
                return false;
            }
            foreach (var member in members)
            {
                if (!IsEligiblePlayer(member)
                    || !ReferenceEquals(member.m_PEnvir, sourceEnvironment))
                    continue;
                FlyToDynamicRoomIndex(member, roomName, roomIndex, 0, 0);
            }
            return true;
        }

        public int CreateDynamicRoomMonsters(string roomName, int roomIndex,
            int x, int y, int range, string monsterName, int count)
        {
            if (count < 1 || !_manager.TryGetActiveRoom(roomName, roomIndex,
                    out var environment))
                return 0;

            var created = 0;
            for (var attempt = 0; attempt < count; attempt++)
            {
                var spawnX = x;
                var spawnY = y;
                if (range > 0)
                {
                    var width = unchecked(range * 2 + 1);
                    if (width > 0)
                    {
                        spawnX = unchecked(x
                            + M2Share.RandomNumber.Random(width) - range);
                        spawnY = unchecked(y
                            + M2Share.RandomNumber.Random(width) - range);
                    }
                }
                if (_userEngine.RegenMonsterByName(environment,
                        unchecked((short)spawnX), unchecked((short)spawnY),
                        monsterName) != null)
                    created++;
            }
            return created;
        }

        public int GetDynamicRoomPlayerCount(string roomName, int roomIndex)
        {
            return _manager.TryGetActiveRoom(roomName, roomIndex,
                out var environment)
                ? environment.DynamicRoomPlayerCount
                : 0;
        }

        public int GetDynamicRoomCount(string roomName)
        {
            lock (_syncRoot)
                return _definitions.TryGetValue(roomName ?? string.Empty,
                    out var definition)
                    ? definition.PhysicalRooms.Count
                    : 0;
        }

        public bool HasFreeDynamicRoom(string roomName)
        {
            lock (_syncRoot)
            {
                return _definitions.TryGetValue(roomName ?? string.Empty,
                           out var definition)
                       && definition.PhysicalRooms.Any(room =>
                           room.Environment.DynamicRoomState == 0
                           && !room.Environment.DynamicRoomBlocked
                           && room.Environment.DynamicRoomPlayerCount == 0);
            }
        }

        public bool IsDynamicRoomValid(string roomName, int roomIndex)
        {
            return _manager.TryGetActiveRoom(roomName, roomIndex, out _);
        }

        private bool TryReserveActivatedLease(string roomName,
            TPlayObject owner, out NativeDynamicRoomActivationLease lease)
        {
            lease = null;
            DefinitionEntry definitionEntry;
            lock (_syncRoot)
            {
                if (!_initialized || string.IsNullOrEmpty(roomName)
                    || !_definitions.TryGetValue(roomName,
                        out definitionEntry))
                    return false;
            }

            if (!_runtime.TryReserveIdleRoomLease(roomName, owner, out lease))
            {
                lock (_syncRoot)
                {
                    if (definitionEntry.PhysicalRooms.Count
                            >= definitionEntry.TargetPhysicalCount
                        || !TryCreatePhysicalRoomLocked(definitionEntry,
                            out _))
                        return false;
                }
                if (!_runtime.TryReserveIdleRoomLease(roomName, owner,
                        out lease))
                    return false;
            }

            PhysicalEntry physical;
            lock (_syncRoot)
            {
                if (!_physicalRooms.TryGetValue(lease.Environment,
                        out physical)
                    || !ReferenceEquals(physical.DefinitionEntry,
                        definitionEntry))
                {
                    _manager.TryAbortReservedRoomLease(lease);
                    lease = null;
                    return false;
                }
            }

            var registrations = physical.Npcs.Select(materialized =>
                    new NativeDynamicRoomPasRouteRegistration(
                        materialized.Npc, materialized.Binding))
                .ToArray();
            var eventAdapter = new NativeDynamicRoomEventActivationAdapter(
                _eventManager, lease.Environment);
            if (!_runtime.TryCommitReservedActivation(lease, eventAdapter,
                    registrations, out _)
                || !_runtime.TryGetCommittedRouteHandles(lease,
                    out var routeHandles)
                || !_npcOwner.TryAttachActivationBinding(
                    physical.Ownership, lease, registrations, routeHandles,
                    out _))
            {
                lease.Environment.DynamicRoomBlocked = true;
                lease = null;
                return false;
            }
            return true;
        }

        private bool TryCreatePhysicalRoomLocked(
            DefinitionEntry definitionEntry, out string error)
        {
            error = null;
            var definition = definitionEntry.Definition;
            var physicalInstanceId = definitionEntry.NextPhysicalInstanceId++;
            if (!NativeDynamicRoomEnvironmentFactory
                    .TryCreateDormantEnvironment(definition, _mapDirectory,
                        _serverIndex, out var environment,
                        out var environmentErrors))
            {
                error = $"room {definition.RoomName}[{physicalInstanceId}]: "
                        + string.Join(" | ", environmentErrors);
                return false;
            }

            _ = NativeDropControlLoader.TryLoadMap(M2Share.sRootPath,
                definition.RoomName, environment.NativeDropControl, out _);
            _ = NativeMapRunPermission.TryLoad(M2Share.g_Config.sEnvirDir,
                environment,
                M2Share.ServerSwitches?.IsBitSet(2, 0x80) == true, out _);

            if (!_manager.RegisterIdleRoom(definition, physicalInstanceId,
                    environment, definitionEntry.MinimumActiveMinutes,
                    TryBeginClosingCleanup, TryFinalizeIdleCleanup,
                    _runtime.TryCloseActivationEvents))
            {
                error = $"room {definition.RoomName}[{physicalInstanceId}]: manager registration failed";
                return false;
            }

            if (!_materializer.TryPrepare(definition, environment,
                    physicalInstanceId, definitionEntry.Bindings,
                    out var journal, out var materializationError))
            {
                error = $"room {definition.RoomName}[{physicalInstanceId}]: {materializationError}";
                return false;
            }
            if (!journal.TryCommit())
            {
                journal.TryRollback();
                error = $"room {definition.RoomName}[{physicalInstanceId}]: NPC publication failed";
                return false;
            }
            if (!_npcOwner.TryAdoptCommittedPublication(definition,
                    environment, physicalInstanceId,
                    definitionEntry.Bindings, journal,
                    out var ownership))
            {
                journal.TryRollback();
                error = $"room {definition.RoomName}[{physicalInstanceId}]: NPC ownership adoption failed";
                return false;
            }

            var physical = new PhysicalEntry
            {
                DefinitionEntry = definitionEntry,
                Environment = environment,
                PhysicalInstanceId = physicalInstanceId,
                Ownership = ownership,
                Npcs = journal.Npcs.ToArray()
            };
            if (!_manager.TryAttachPhysicalOwnership(environment, definition,
                    physicalInstanceId, ownership,
                    permit => TryDestroyPhysicalRoom(physical, permit)))
            {
                error = $"room {definition.RoomName}[{physicalInstanceId}]: physical ownership attach failed";
                return false;
            }

            definitionEntry.PhysicalRooms.Add(physical);
            _physicalRooms.Add(environment, physical);
            return true;
        }

        private bool TryDestroyPhysicalRoom(PhysicalEntry physical,
            INativeDynamicRoomPhysicalRetirementPermit permit)
        {
            if (physical == null
                || !_npcOwner.TryFullDestroy(physical.Ownership, permit))
                return false;
            lock (_syncRoot)
            {
                if (_physicalRooms.TryGetValue(physical.Environment,
                        out var current)
                    && ReferenceEquals(current, physical))
                {
                    _physicalRooms.Remove(physical.Environment);
                    physical.DefinitionEntry.PhysicalRooms.Remove(physical);
                }
            }
            return true;
        }

        private bool TryBeginClosingCleanup(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null
                || !_runtime.TryBeginClosingCleanup(lease)
                || !_npcOwner.TryRetireActivationBinding(lease))
                return false;
            return TryCleanupEnvironmentActors(lease.Environment,
                relocateMasterOwned: false);
        }

        private bool TryFinalizeIdleCleanup(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null || !_runtime.TryFinalizeIdleCleanup(lease)
                || !TryCleanupEnvironmentActors(lease.Environment,
                    relocateMasterOwned: true))
                return false;
            lease.Environment.RemoveItemObjectsEverywhere();
            return !lease.Environment.ContainsItemObjects()
                   && ContainsOnlyPhysicalActors(lease.Environment);
        }

        private bool TryCleanupEnvironmentActors(Envirnoment environment,
            bool relocateMasterOwned)
        {
            PhysicalEntry physical;
            lock (_syncRoot)
            {
                if (!_physicalRooms.TryGetValue(environment, out physical))
                    return false;
            }

            var actors = new HashSet<TBaseObject>(
                _objectManager.SnapshotEnvironmentObjects(environment),
                ReferenceEqualityComparer.Instance);
            actors.UnionWith(environment.SnapshotMovingObjectsExact());
            foreach (var actor in actors)
            {
                if (actor == null
                    || _npcOwner.ContainsPhysicalNpcExact(
                        physical.Ownership, actor as NormNpc))
                    continue;
                if (actor.CountsAsPlayerPresence) return false;
                if (actor.m_Master != null)
                {
                    if (!relocateMasterOwned) continue;
                    if (!NativeDynamicRoomMasterRelocation.TryRelocate(actor))
                        return false;
                    continue;
                }
                if (!TryRemoveActorExact(environment, actor)) return false;
            }
            return true;
        }

        private bool ContainsOnlyPhysicalActors(Envirnoment environment)
        {
            PhysicalEntry physical;
            lock (_syncRoot)
            {
                if (!_physicalRooms.TryGetValue(environment, out physical))
                    return false;
            }
            var actors = new HashSet<TBaseObject>(
                _objectManager.SnapshotEnvironmentObjects(environment),
                ReferenceEqualityComparer.Instance);
            actors.UnionWith(environment.SnapshotMovingObjectsExact());
            return actors.All(actor => actor is NormNpc npc
                && _npcOwner.ContainsPhysicalNpcExact(
                    physical.Ownership, npc));
        }

        private bool TryRemoveActorExact(Envirnoment environment,
            TBaseObject actor)
        {
            try
            {
                actor.MakeGhost();
            }
            catch
            {
                actor.m_boGhost = true;
                actor.m_dwGhostTick = HUtil32.GetTickCount();
            }
            environment.RemoveMovingObjectEverywhereExact(actor,
                notifyDynamicRoomLifecycle: false);
            _objectManager.Remove(actor.ObjectId, actor);
            return !environment.ContainsMovingObjectEverywhereExact(actor)
                   && !ReferenceEquals(_objectManager.Get(actor.ObjectId),
                       actor);
        }

        private static bool IsEligiblePlayer(TPlayObject player)
        {
            return player != null && !player.m_boGhost
                                  && player.m_btRaceServer
                                  == Grobal2.RC_PLAYOBJECT;
        }

        private static bool TryParseStartupNumber(string value, int minimum,
            int maximum, out int number)
        {
            return int.TryParse(value, NumberStyles.None,
                       CultureInfo.InvariantCulture, out number)
                   && number >= minimum && number <= maximum;
        }

        // 战神 sub_5FEBD0 @0x005FEBD0 / sub_5FF2E0 @0x005FF2E0 失败支：
        // LStrCatN("[Error]:无可用的房间:", roomName) -> 0x79DF74 (cl=1 广播)。
        internal static void BroadcastNativeDynamicRoomUnavailable(string roomName)
        {
            M2Share.MainOutMessage("[Error]:无可用的房间:" + (roomName ?? string.Empty));
        }
    }
}
