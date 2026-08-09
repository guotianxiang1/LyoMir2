namespace GameSvr
{
    public sealed class NativeDynamicRoomPhysicalRetirementPermit
        : INativeDynamicRoomPhysicalRetirementPermit
    {
        private readonly NativeDynamicRoomManager _manager;
        private readonly object _registrationIdentity;

        internal NativeDynamicRoomPhysicalRetirementPermit(
            NativeDynamicRoomManager manager, object registrationIdentity,
            NativeDynamicRoomPhysicalNpcOwnership physicalOwnership,
            NativeDynamicRoomDefinition definition, string roomName,
            Envirnoment environment, int physicalInstanceId)
        {
            _manager = manager;
            _registrationIdentity = registrationIdentity;
            PhysicalOwnership = physicalOwnership;
            Definition = definition;
            RoomName = roomName;
            Environment = environment;
            PhysicalInstanceId = physicalInstanceId;
        }

        public NativeDynamicRoomPhysicalNpcOwnership PhysicalOwnership { get; }
        public NativeDynamicRoomDefinition Definition { get; }
        public string RoomName { get; }
        public Envirnoment Environment { get; }
        public int PhysicalInstanceId { get; }

        public bool IsRetiredExact => _manager.IsCurrentPhysicalRetirement(
            this, _registrationIdentity);
    }

    public sealed class NativeDynamicRoomManager
    {
        private const long EmptyReservationFloorMilliseconds = 2 * 60 * 1000;
        private const long ClosingMilliseconds = 10 * 60 * 1000;
        // Native sub_5FD764 compares the state-0 idle tick with 0x36EE80.
        private const long IdleRetirementMilliseconds = 60 * 60 * 1000;

        private sealed class RoomRegistration
        {
            public string RoomName { get; init; }
            public NativeDynamicRoomDefinition Definition { get; init; }
            public Envirnoment Environment { get; init; }
            public int PhysicalInstanceId { get; init; }
            public int MinimumActiveMinutes { get; init; }
            // Native +F8 gates the state-2 to state-1 actor cleanup.
            public bool BeginClosingCleanupEnabled { get; init; }
            // Cleanup hooks are retried after failure and therefore must be idempotent.
            public Func<NativeDynamicRoomActivationLease, bool>
                BeginClosingCleanup { get; init; }
            public Func<NativeDynamicRoomActivationLease, bool>
                FinalizeIdleCleanup { get; init; }
            // This hook owns the native type-0 (wildcard) event cleanup boundary.
            public Func<NativeDynamicRoomActivationLease, bool> CloseActivationEvents { get; init; }
            public NativeDynamicRoomPhysicalNpcOwnership PhysicalOwnership { get; set; }
            public Func<INativeDynamicRoomPhysicalRetirementPermit, bool>
                FullDestroy { get; set; }
            public long ActiveTick { get; set; }
            public long ClosingTick { get; set; }
            public long IdleTick { get; set; }
            // Native +F9 means this activation created at least one room event.
            public bool ActivationEventsCreated { get; set; }
            public bool BeginClosingCleanupComplete { get; set; }
            public bool FinalizeIdleCleanupComplete { get; set; }
            public bool ActivationEventCleanupComplete { get; set; }
            public bool BeginClosingCleanupInProgress { get; set; }
            public bool FinalizeIdleCleanupInProgress { get; set; }
            public NativeDynamicRoomActivationLease CurrentLease { get; set; }
            public bool Retiring { get; set; }
            public bool RetirementInProgress { get; set; }
            public NativeDynamicRoomPhysicalRetirementPermit RetirementPermit { get; set; }
        }

        private sealed class RoomPool
        {
            public NativeDynamicRoomDefinition Definition { get; init; }
            public List<RoomRegistration> Rooms { get; } = new();
        }

        private readonly object _syncRoot = new();
        private readonly Dictionary<string, RoomPool> _pools = new(StringComparer.Ordinal);
        private readonly Dictionary<Envirnoment, RoomRegistration> _registeredRooms = new();
        private readonly NativeDynamicRoomLeaseOwner _leaseOwner = new();
        private readonly Func<long> _tickProvider;
        private NativeDynamicRoomRuntime _runtime;
        private bool _runtimeAttachClosed;

        public NativeDynamicRoomManager(Func<long> tickProvider = null)
        {
            _tickProvider = tickProvider ?? (() => Environment.TickCount64);
        }

        internal bool TryAttachRuntime(NativeDynamicRoomRuntime runtime)
        {
            if (runtime == null) return false;

            lock (_syncRoot)
            {
                if (_runtime != null) return ReferenceEquals(_runtime, runtime);
                if (_runtimeAttachClosed) return false;
                _runtime = runtime;
                return true;
            }
        }

        public bool RegisterIdleRoom(string roomName, int physicalInstanceId,
            Envirnoment environment)
        {
            return RegisterIdleRoom(roomName, physicalInstanceId, environment, 0, null);
        }

        public bool RegisterIdleRoom(string roomName, int physicalInstanceId,
            Envirnoment environment,
            int minimumActiveMinutes, Func<Envirnoment, bool> tryPrepareForReuse)
        {
            Func<NativeDynamicRoomActivationLease, bool> beginClosingCleanup =
                tryPrepareForReuse == null
                    ? null
                    : lease => tryPrepareForReuse(lease.Environment);
            return RegisterIdleRoom(roomName, physicalInstanceId, environment,
                minimumActiveMinutes, beginClosingCleanup, null, null, true);
        }

        public bool RegisterIdleRoom(string roomName, int physicalInstanceId,
            Envirnoment environment,
            int minimumActiveMinutes,
            Func<NativeDynamicRoomActivationLease, bool> beginClosingCleanup,
            Func<NativeDynamicRoomActivationLease, bool> finalizeIdleCleanup,
            Func<NativeDynamicRoomActivationLease, bool> closeActivationEvents,
            bool beginClosingCleanupEnabled = true)
        {
            return RegisterIdleRoomCore(roomName, null, physicalInstanceId,
                environment, minimumActiveMinutes, beginClosingCleanup,
                finalizeIdleCleanup, closeActivationEvents,
                beginClosingCleanupEnabled);
        }

        public bool RegisterIdleRoom(NativeDynamicRoomDefinition definition,
            int physicalInstanceId, Envirnoment environment,
            int minimumActiveMinutes,
            Func<NativeDynamicRoomActivationLease, bool> beginClosingCleanup,
            Func<NativeDynamicRoomActivationLease, bool> finalizeIdleCleanup,
            Func<NativeDynamicRoomActivationLease, bool> closeActivationEvents,
            bool beginClosingCleanupEnabled = true)
        {
            if (definition == null) return false;
            return RegisterIdleRoomCore(definition.RoomName, definition,
                physicalInstanceId, environment, minimumActiveMinutes,
                beginClosingCleanup, finalizeIdleCleanup,
                closeActivationEvents, beginClosingCleanupEnabled);
        }

        private bool RegisterIdleRoomCore(string roomName,
            NativeDynamicRoomDefinition definition, int physicalInstanceId,
            Envirnoment environment, int minimumActiveMinutes,
            Func<NativeDynamicRoomActivationLease, bool> beginClosingCleanup,
            Func<NativeDynamicRoomActivationLease, bool> finalizeIdleCleanup,
            Func<NativeDynamicRoomActivationLease, bool> closeActivationEvents,
            bool beginClosingCleanupEnabled)
        {
            if (environment == null || string.IsNullOrEmpty(roomName)
                || physicalInstanceId < 0 || minimumActiveMinutes < 0)
                return false;

            lock (_syncRoot)
            {
                if (_registeredRooms.ContainsKey(environment)) return false;

                if (!_pools.TryGetValue(roomName, out var pool))
                {
                    var definitionRegistered = definition == null
                        ? _leaseOwner.TryRegisterDefinition(roomName)
                        : _leaseOwner.TryRegisterDefinitionModel(definition);
                    if (!definitionRegistered) return false;
                    pool = new RoomPool { Definition = definition };
                    _pools.Add(roomName, pool);
                }
                else if (!ReferenceEquals(pool.Definition, definition))
                {
                    return false;
                }

                if (pool.Rooms.Any(room =>
                        room.PhysicalInstanceId == physicalInstanceId))
                    return false;
                var now = _tickProvider();
                if (!_leaseOwner.TryAppendEnvironment(roomName, environment)) return false;

                var registration = new RoomRegistration
                {
                    RoomName = roomName,
                    Definition = definition,
                    Environment = environment,
                    PhysicalInstanceId = physicalInstanceId,
                    MinimumActiveMinutes = minimumActiveMinutes,
                    BeginClosingCleanupEnabled = beginClosingCleanupEnabled,
                    BeginClosingCleanup = beginClosingCleanup,
                    FinalizeIdleCleanup = finalizeIdleCleanup,
                    CloseActivationEvents = closeActivationEvents,
                    IdleTick = now
                };
                environment.ConfigureDynamicRoom(roomName, physicalInstanceId, this);
                pool.Rooms.Add(registration);
                _registeredRooms.Add(environment, registration);
                return true;
            }
        }

        public bool TryAttachPhysicalOwnership(Envirnoment environment,
            NativeDynamicRoomDefinition expectedDefinition,
            int expectedPhysicalInstanceId,
            NativeDynamicRoomPhysicalNpcOwnership physicalOwnership,
            Func<INativeDynamicRoomPhysicalRetirementPermit, bool> fullDestroy)
        {
            if (environment == null || expectedDefinition == null
                || expectedPhysicalInstanceId < 0 || physicalOwnership == null
                || fullDestroy == null)
                return false;

            lock (_syncRoot)
            {
                if (!_registeredRooms.TryGetValue(environment,
                        out var registration)
                    || !_pools.TryGetValue(expectedDefinition.RoomName,
                        out var pool)
                    || !ReferenceEquals(pool.Definition, expectedDefinition)
                    || !pool.Rooms.Contains(registration)
                    || registration.PhysicalInstanceId
                    != expectedPhysicalInstanceId
                    || registration.PhysicalOwnership != null
                    || registration.FullDestroy != null
                    || registration.Retiring
                    || registration.CurrentLease != null
                    || registration.ActivationEventsCreated
                    || registration.BeginClosingCleanupInProgress
                    || registration.FinalizeIdleCleanupInProgress
                    || environment.DynamicRoomState != 0
                    || environment.DynamicRoomPlayerCount != 0
                    || environment.DynamicRoomBlocked
                    || NativeDynamicRoomEventActivationAdapter
                        .HasUnresolvedRollback(environment)
                    || physicalOwnership.IsDestroyed
                    || physicalOwnership.DestroyPending
                    || !ReferenceEquals(physicalOwnership.Definition,
                        expectedDefinition)
                    || !ReferenceEquals(physicalOwnership.Environment,
                        environment)
                    || physicalOwnership.PhysicalInstanceId
                    != expectedPhysicalInstanceId)
                    return false;

                registration.PhysicalOwnership = physicalOwnership;
                registration.FullDestroy = fullDestroy;
                return true;
            }
        }

        public bool TryReserveIdleRoom(string roomName, TPlayObject owner, out int roomIndex)
        {
            roomIndex = -1;
            if (!TryReserveIdleRoomLease(roomName, owner, out var lease)) return false;
            roomIndex = lease.Index;
            return true;
        }

        public bool TryReserveIdleRoomLease(string roomName, TPlayObject owner,
            out NativeDynamicRoomActivationLease lease)
        {
            NativeDynamicRoomRuntime runtime;
            lock (_syncRoot)
            {
                runtime = _runtime;
                if (runtime == null) _runtimeAttachClosed = true;
            }
            return runtime == null
                ? TryReserveIdleRoomLeaseCore(roomName, owner, out lease)
                : runtime.TryReserveIdleRoomLeaseFromManager(this, roomName,
                    owner, out lease);
        }

        internal bool TryReserveIdleRoomLeaseUnderRuntime(
            NativeDynamicRoomRuntime runtime, string roomName,
            TPlayObject owner, out NativeDynamicRoomActivationLease lease)
        {
            lock (_syncRoot)
            {
                if (!ReferenceEquals(_runtime, runtime))
                {
                    lease = null;
                    return false;
                }
            }
            return TryReserveIdleRoomLeaseCore(roomName, owner, out lease);
        }

        private bool TryReserveIdleRoomLeaseCore(string roomName,
            TPlayObject owner, out NativeDynamicRoomActivationLease lease)
        {
            lease = null;
            if (string.IsNullOrEmpty(roomName)) return false;

            // Native default room classes use the base selector. Special room
            // types 100, 101, and 110 interpret the optional owner separately.
            _ = owner;
            lock (_syncRoot)
            {
                if (!_pools.TryGetValue(roomName, out var pool)) return false;

                foreach (var registration in pool.Rooms)
                {
                    var room = registration.Environment;
                    if (!room.IsDynamicRoom || room.DynamicRoomState != 0
                        || room.DynamicRoomBlocked
                        || room.DynamicRoomPlayerCount > 0)
                        continue;

                    var now = _tickProvider();
                    if (!_leaseOwner.TryActivate(roomName, room, out lease))
                        continue;
                    ResetActivationLocked(registration, lease, now);
                    return true;
                }
            }
            return false;
        }

        public bool TryGetActiveRoom(string roomName, int roomIndex,
            out Envirnoment environment)
        {
            environment = null;
            if (string.IsNullOrEmpty(roomName)) return false;

            lock (_syncRoot)
            {
                if (!_leaseOwner.TryGetActiveEnvironment(roomName, roomIndex,
                        out var active)
                    || !_registeredRooms.TryGetValue(active, out var registration)
                    || active.DynamicRoomState != 2
                    || active.DynamicRoomBlocked
                    || registration.CurrentLease == null
                    || registration.CurrentLease.Index != roomIndex
                    || !registration.CurrentLease.IsCurrentActive())
                    return false;
                environment = active;
                return true;
            }
        }

        internal bool TrySnapshotRooms(string roomName,
            out IReadOnlyList<Envirnoment> environments)
        {
            environments = Array.Empty<Envirnoment>();
            if (string.IsNullOrEmpty(roomName)) return false;

            lock (_syncRoot)
            {
                if (!_pools.TryGetValue(roomName, out var pool)) return false;
                environments = pool.Rooms
                    .Select(room => room.Environment)
                    .ToArray();
                return true;
            }
        }

        internal bool IsCurrentActiveLease(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null) return false;

            lock (_syncRoot)
            {
                var environment = lease.Environment;
                return environment != null
                       && _registeredRooms.TryGetValue(environment,
                           out var registration)
                       && ReferenceEquals(registration.CurrentLease, lease)
                       && environment.DynamicRoomState == 2
                       && !environment.DynamicRoomBlocked
                       && lease.IsCurrentActive();
            }
        }

        internal bool IsCurrentPhysicalRetirement(
            NativeDynamicRoomPhysicalRetirementPermit permit,
            object registrationIdentity)
        {
            if (permit == null || registrationIdentity == null) return false;

            lock (_syncRoot)
            {
                var environment = permit.Environment;
                return environment != null
                       && _registeredRooms.TryGetValue(environment,
                           out var registration)
                       && ReferenceEquals(registration, registrationIdentity)
                       && ReferenceEquals(registration.RetirementPermit, permit)
                       && ReferenceEquals(registration.PhysicalOwnership,
                           permit.PhysicalOwnership)
                       && _pools.TryGetValue(permit.RoomName, out var pool)
                       && ReferenceEquals(pool.Definition, permit.Definition)
                       && pool.Rooms.Contains(registration)
                       && registration.PhysicalInstanceId
                       == permit.PhysicalInstanceId
                       && registration.Retiring
                       && registration.CurrentLease == null
                       && !registration.ActivationEventsCreated
                       && environment.DynamicRoomState == 0
                       && environment.DynamicRoomBlocked
                       && environment.DynamicRoomPlayerCount == 0
                       && !NativeDynamicRoomEventActivationAdapter
                           .HasUnresolvedRollback(environment);
            }
        }

        internal bool IsCurrentLeaseInState(
            NativeDynamicRoomActivationLease lease, int expectedState)
        {
            if (lease == null || expectedState is not (1 or 2)) return false;

            lock (_syncRoot)
            {
                var environment = lease.Environment;
                return environment != null
                       && _registeredRooms.TryGetValue(environment,
                           out var registration)
                       && ReferenceEquals(registration.CurrentLease, lease)
                       && environment.DynamicRoomState == expectedState;
            }
        }

        // Mark only after at least one descriptor-backed room event is attached.
        public bool TryMarkActivationEventsCreated(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null) return false;

            lock (_syncRoot)
            {
                var environment = lease.Environment;
                if (!_registeredRooms.TryGetValue(environment, out var registration)
                    || environment.DynamicRoomState != 2
                    || !ReferenceEquals(registration.CurrentLease, lease)
                    || !lease.IsCurrentActive())
                    return false;

                registration.ActivationEventsCreated = true;
                registration.ActivationEventCleanupComplete = false;
                return true;
            }
        }

        public bool TryAbortReservedRoomLease(
            NativeDynamicRoomActivationLease lease)
        {
            NativeDynamicRoomRuntime runtime;
            lock (_syncRoot)
            {
                runtime = _runtime;
                if (runtime == null) _runtimeAttachClosed = true;
            }
            return runtime == null
                ? TryAbortReservedRoomLeaseCore(lease)
                : runtime.TryAbortReservedRoomLeaseFromManager(this, lease);
        }

        internal bool TryAbortReservedRoomLeaseUnderRuntime(
            NativeDynamicRoomRuntime runtime,
            NativeDynamicRoomActivationLease lease)
        {
            lock (_syncRoot)
            {
                if (!ReferenceEquals(_runtime, runtime)) return false;
            }
            return TryAbortReservedRoomLeaseCore(lease);
        }

        private bool TryAbortReservedRoomLeaseCore(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null) return false;

            lock (_syncRoot)
            {
                var room = lease.Environment;
                if (!_registeredRooms.TryGetValue(room, out var registration)
                    || !ReferenceEquals(registration.CurrentLease, lease)
                    || room.DynamicRoomState != 2
                    || room.DynamicRoomPlayerCount > 0
                    || room.DynamicRoomBlocked
                    || NativeDynamicRoomEventActivationAdapter
                        .HasUnresolvedRollback(room)
                    || registration.ActivationEventsCreated
                    || !_leaseOwner.TryAbortActivation(lease))
                    return false;

                ResetAbortedActivationLocked(registration, _tickProvider());
                return true;
            }
        }

        public void NotifyPlayerRemoved(Envirnoment environment)
        {
            NativeDynamicRoomRuntime runtime;
            lock (_syncRoot)
            {
                runtime = _runtime;
                if (runtime == null) _runtimeAttachClosed = true;
            }
            if (runtime == null)
                NotifyPlayerRemovedCore(environment);
            else
                runtime.NotifyPlayerRemovedFromManager(this, environment);
        }

        internal void NotifyPlayerRemovedUnderRuntime(
            NativeDynamicRoomRuntime runtime, Envirnoment environment)
        {
            lock (_syncRoot)
            {
                if (!ReferenceEquals(_runtime, runtime)) return;
            }
            NotifyPlayerRemovedCore(environment);
        }

        private void NotifyPlayerRemovedCore(Envirnoment environment)
        {
            RoomRegistration closingRoom = null;
            NativeDynamicRoomActivationLease closingLease = null;
            var now = _tickProvider();
            lock (_syncRoot)
            {
                if (!_registeredRooms.TryGetValue(environment, out var registration)
                    || environment.DynamicRoomState != 2
                    || environment.DynamicRoomPlayerCount > 0)
                    return;

                var minimumActiveMilliseconds =
                    registration.MinimumActiveMinutes * 60_000L;
                if (now - registration.ActiveTick <= minimumActiveMilliseconds) return;
                BeginClosingLocked(registration, now);
                if (environment.DynamicRoomState == 1
                    && TryStartBeginClosingCleanupLocked(registration,
                        out closingLease))
                    closingRoom = registration;
            }
            if (closingRoom != null)
                PrepareForReuse(closingRoom, closingLease);
        }

        public void Run()
        {
            NativeDynamicRoomRuntime runtime;
            lock (_syncRoot)
            {
                runtime = _runtime;
                if (runtime == null) _runtimeAttachClosed = true;
            }
            if (runtime == null)
                RunCore();
            else
                runtime.RunFromManager(this);
        }

        internal void RunUnderRuntime(NativeDynamicRoomRuntime runtime)
        {
            lock (_syncRoot)
            {
                if (!ReferenceEquals(_runtime, runtime)) return;
            }
            RunCore();
        }

        private void RunCore()
        {
            var now = _tickProvider();
            List<(RoomRegistration Registration,
                NativeDynamicRoomActivationLease Lease)> closingRooms = null;
            List<(RoomRegistration Registration,
                NativeDynamicRoomActivationLease Lease)> finalizingRooms = null;
            List<(RoomRegistration Registration,
                NativeDynamicRoomPhysicalRetirementPermit Permit)>
                retiringRooms = null;
            lock (_syncRoot)
            {
                foreach (var registration in _registeredRooms.Values)
                {
                    var room = registration.Environment;
                    if (registration.Retiring)
                    {
                        if (!registration.RetirementInProgress)
                        {
                            registration.RetirementInProgress = true;
                            (retiringRooms ??= new()).Add((registration,
                                registration.RetirementPermit));
                        }
                    }
                    else if (room.DynamicRoomState == 0)
                    {
                        if (TryStartPhysicalRetirementLocked(registration, now,
                                out var retirementPermit))
                            (retiringRooms ??= new()).Add((registration,
                                retirementPermit));
                    }
                    else if (room.DynamicRoomState == 2
                             && room.DynamicRoomPlayerCount <= 0)
                    {
                        var elapsed = now - registration.ActiveTick;
                        var minimumActiveMilliseconds =
                            registration.MinimumActiveMinutes * 60_000L;
                        if (elapsed > EmptyReservationFloorMilliseconds
                            && elapsed > minimumActiveMilliseconds)
                        {
                            BeginClosingLocked(registration, now);
                            if (room.DynamicRoomState == 1
                                && TryStartBeginClosingCleanupLocked(registration,
                                    out var closingLease))
                                (closingRooms ??= new()).Add((registration,
                                    closingLease));
                        }
                    }
                    else if (room.DynamicRoomState == 1
                             && room.DynamicRoomPlayerCount <= 0)
                    {
                        if (!registration.BeginClosingCleanupComplete)
                        {
                            if (TryStartBeginClosingCleanupLocked(registration,
                                    out var closingLease))
                                (closingRooms ??= new()).Add((registration,
                                    closingLease));
                        }
                        else if (now - registration.ClosingTick > ClosingMilliseconds
                                 && TryStartFinalizeIdleCleanupLocked(registration,
                                     out var finalizingLease))
                        {
                            (finalizingRooms ??= new()).Add((registration,
                                finalizingLease));
                        }
                    }
                }
            }

            if (closingRooms != null)
            {
                foreach (var work in closingRooms)
                    PrepareForReuse(work.Registration, work.Lease);
            }

            if (finalizingRooms != null)
            {
                foreach (var work in finalizingRooms)
                    FinalizeIdleCleanup(work.Registration, work.Lease);
            }

            if (retiringRooms != null)
            {
                foreach (var work in retiringRooms)
                    RetirePhysicalRoom(work.Registration, work.Permit);
            }
        }

        private bool TryStartPhysicalRetirementLocked(
            RoomRegistration registration, long now,
            out NativeDynamicRoomPhysicalRetirementPermit permit)
        {
            permit = null;
            var room = registration.Environment;
            // Legacy/unattached rooms have no complete physical destroy
            // transaction and therefore remain registered fail closed.
            if (registration.PhysicalOwnership == null
                || registration.FullDestroy == null
                || registration.Retiring
                || registration.RetirementInProgress
                || registration.CurrentLease != null
                || registration.ActivationEventsCreated
                || registration.BeginClosingCleanupInProgress
                || registration.FinalizeIdleCleanupInProgress
                || room.DynamicRoomState != 0
                || room.DynamicRoomPlayerCount != 0
                || room.DynamicRoomBlocked
                || now - registration.IdleTick <= IdleRetirementMilliseconds
                || NativeDynamicRoomEventActivationAdapter
                    .HasUnresolvedRollback(room)
                || !_pools.TryGetValue(registration.RoomName, out var pool)
                || !ReferenceEquals(pool.Definition, registration.Definition)
                || !pool.Rooms.Contains(registration))
                return false;

            permit = new NativeDynamicRoomPhysicalRetirementPermit(this,
                registration, registration.PhysicalOwnership,
                registration.Definition, registration.RoomName, room,
                registration.PhysicalInstanceId);
            if (!_leaseOwner.TryBeginPhysicalRetirement(
                    registration.RoomName, room, permit))
            {
                permit = null;
                return false;
            }

            registration.RetirementPermit = permit;
            registration.Retiring = true;
            registration.RetirementInProgress = true;
            room.DynamicRoomBlocked = true;
            return true;
        }

        private void RetirePhysicalRoom(RoomRegistration registration,
            NativeDynamicRoomPhysicalRetirementPermit permit)
        {
            Func<INativeDynamicRoomPhysicalRetirementPermit, bool> fullDestroy;
            lock (_syncRoot)
            {
                if (!IsCurrentPhysicalRetirement(permit, registration)
                    || !registration.RetirementInProgress)
                    return;
                fullDestroy = registration.FullDestroy;
            }

            var destroyed = false;
            try
            {
                destroyed = fullDestroy?.Invoke(permit) == true;
            }
            catch (Exception ex)
            {
                ReportCleanupFailure(registration, "FullDestroy", ex);
            }

            lock (_syncRoot)
            {
                if (!IsCurrentPhysicalRetirement(permit, registration)
                    || !registration.RetirementInProgress)
                    return;

                registration.RetirementInProgress = false;
                registration.Environment.DynamicRoomBlocked = true;
                if (!destroyed
                    || !ReferenceEquals(registration.PhysicalOwnership,
                        permit.PhysicalOwnership)
                    || !registration.PhysicalOwnership.IsDestroyed
                    || registration.PhysicalOwnership.DestroyPending)
                    return;

                if (!_pools.TryGetValue(registration.RoomName, out var pool)
                    || !ReferenceEquals(pool.Definition,
                        registration.Definition)
                    || !pool.Rooms.Contains(registration)
                    || !_registeredRooms.TryGetValue(
                        registration.Environment, out var current)
                    || !ReferenceEquals(current, registration))
                    return;

                // The exact definition owner survives while any physical
                // environment remains and is removed with its final room.
                var retireDefinition = pool.Rooms.Count == 1;
                if (!_leaseOwner.TryCompletePhysicalRetirement(
                        registration.RoomName, registration.Environment,
                        permit, retireDefinition,
                        out var definitionRetired)
                    || definitionRetired != retireDefinition)
                    return;

                pool.Rooms.Remove(registration);
                _registeredRooms.Remove(registration.Environment);
                if (retireDefinition
                    && _pools.TryGetValue(registration.RoomName,
                        out var currentPool)
                    && ReferenceEquals(currentPool, pool))
                    _pools.Remove(registration.RoomName);

                registration.Retiring = false;
                registration.RetirementPermit = null;
            }
        }

        private static void ResetActivationLocked(RoomRegistration registration,
            NativeDynamicRoomActivationLease lease, long now)
        {
            registration.CurrentLease = lease;
            registration.ActiveTick = now;
            registration.ClosingTick = 0;
            registration.ActivationEventsCreated = false;
            registration.BeginClosingCleanupComplete = false;
            registration.FinalizeIdleCleanupComplete = false;
            registration.ActivationEventCleanupComplete = false;
            registration.BeginClosingCleanupInProgress = false;
            registration.FinalizeIdleCleanupInProgress = false;
            registration.Environment.DynamicRoomBlocked = false;
            registration.Environment.SetDynamicRoomLeaseIndex(lease.Index);
            registration.Environment.DynamicRoomState = 2;
        }

        private void BeginClosingLocked(RoomRegistration registration, long now)
        {
            if (registration.CurrentLease == null
                || !_leaseOwner.TrySetLeaseState(registration.CurrentLease, 1))
            {
                registration.Environment.DynamicRoomBlocked = true;
                return;
            }

            registration.Environment.DynamicRoomState = 1;
            registration.ClosingTick = now;
            registration.BeginClosingCleanupComplete =
                !registration.BeginClosingCleanupEnabled
                || registration.BeginClosingCleanup == null;
            registration.FinalizeIdleCleanupComplete = false;
            registration.ActivationEventCleanupComplete = false;
            registration.BeginClosingCleanupInProgress = false;
            registration.FinalizeIdleCleanupInProgress = false;
            registration.Environment.DynamicRoomBlocked =
                !registration.BeginClosingCleanupComplete;
        }

        private static void ResetAbortedActivationLocked(
            RoomRegistration registration, long now)
        {
            registration.CurrentLease = null;
            registration.ActiveTick = 0;
            registration.ClosingTick = 0;
            registration.ActivationEventsCreated = false;
            registration.BeginClosingCleanupComplete = false;
            registration.FinalizeIdleCleanupComplete = false;
            registration.ActivationEventCleanupComplete = false;
            registration.BeginClosingCleanupInProgress = false;
            registration.FinalizeIdleCleanupInProgress = false;
            registration.IdleTick = now;
            registration.Environment.DynamicRoomState = 0;
            registration.Environment.DynamicRoomBlocked = false;
        }

        private static bool TryStartBeginClosingCleanupLocked(
            RoomRegistration registration,
            out NativeDynamicRoomActivationLease activationLease)
        {
            activationLease = registration.CurrentLease;
            if (registration.BeginClosingCleanupComplete
                || registration.BeginClosingCleanupInProgress
                || activationLease == null
                || registration.Environment.DynamicRoomState != 1)
                return false;

            registration.BeginClosingCleanupInProgress = true;
            return true;
        }

        private static bool TryStartFinalizeIdleCleanupLocked(
            RoomRegistration registration,
            out NativeDynamicRoomActivationLease activationLease)
        {
            activationLease = registration.CurrentLease;
            if (registration.FinalizeIdleCleanupInProgress
                || activationLease == null
                || registration.Environment.DynamicRoomState != 1)
                return false;

            registration.FinalizeIdleCleanupInProgress = true;
            registration.Environment.DynamicRoomBlocked = true;
            return true;
        }

        private void PrepareForReuse(RoomRegistration registration,
            NativeDynamicRoomActivationLease activationLease)
        {
            lock (_syncRoot)
            {
                if (registration.Environment.DynamicRoomState != 1
                    || !ReferenceEquals(registration.CurrentLease,
                        activationLease)
                    || !registration.BeginClosingCleanupInProgress)
                    return;
            }

            var prepared = false;
            try
            {
                prepared = registration.BeginClosingCleanup?.Invoke(
                    activationLease) != false;
            }
            catch (Exception ex)
            {
                ReportCleanupFailure(registration, "BeginClosingCleanup", ex);
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (registration.Environment.DynamicRoomState == 1
                        && ReferenceEquals(registration.CurrentLease,
                            activationLease))
                    {
                        registration.BeginClosingCleanupInProgress = false;
                        registration.BeginClosingCleanupComplete = prepared;
                        registration.Environment.DynamicRoomBlocked = !prepared;
                    }
                }
            }
        }

        private void FinalizeIdleCleanup(RoomRegistration registration,
            NativeDynamicRoomActivationLease activationLease)
        {
            bool finalized;
            lock (_syncRoot)
            {
                if (activationLease == null
                    || registration.Environment.DynamicRoomState != 1
                    || !ReferenceEquals(registration.CurrentLease,
                        activationLease)
                    || !registration.FinalizeIdleCleanupInProgress)
                    return;
                finalized = registration.FinalizeIdleCleanupComplete;
            }

            try
            {
                if (!finalized)
                {
                    try
                    {
                        finalized = registration.FinalizeIdleCleanup?.Invoke(
                            activationLease) != false;
                    }
                    catch (Exception ex)
                    {
                        ReportCleanupFailure(registration, "FinalizeIdleCleanup", ex);
                    }

                    if (finalized)
                    {
                        var idleTick = _tickProvider();
                        lock (_syncRoot)
                        {
                            if (registration.Environment.DynamicRoomState == 1
                                && ReferenceEquals(registration.CurrentLease,
                                    activationLease))
                            {
                                registration.FinalizeIdleCleanupComplete = true;
                                registration.IdleTick = idleTick;
                            }
                            else
                            {
                                finalized = false;
                            }
                        }
                    }
                }

                var eventsCleaned = false;
                if (finalized)
                {
                    lock (_syncRoot)
                    {
                        if (registration.Environment.DynamicRoomState != 1
                            || !ReferenceEquals(registration.CurrentLease,
                                activationLease))
                        {
                            finalized = false;
                        }
                        else
                        {
                            var cleanupRequired =
                                registration.ActivationEventsCreated
                                || NativeDynamicRoomEventActivationAdapter
                                    .HasUnresolvedRollback(
                                        registration.Environment);
                            eventsCleaned = !cleanupRequired
                                            || registration
                                                .ActivationEventCleanupComplete;
                        }
                    }

                    if (finalized && !eventsCleaned
                        && registration.CloseActivationEvents != null)
                    {
                        try
                        {
                            eventsCleaned = registration.CloseActivationEvents(
                                activationLease);
                        }
                        catch (Exception ex)
                        {
                            ReportCleanupFailure(registration, "CloseActivationEvents", ex);
                        }

                        if (eventsCleaned)
                        {
                            lock (_syncRoot)
                            {
                                if (registration.Environment.DynamicRoomState == 1
                                    && ReferenceEquals(
                                        registration.CurrentLease,
                                        activationLease))
                                    registration.ActivationEventCleanupComplete = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (registration.Environment.DynamicRoomState == 1
                        && ReferenceEquals(registration.CurrentLease,
                            activationLease))
                    {
                        registration.FinalizeIdleCleanupInProgress = false;
                        var room = registration.Environment;
                        if (room.DynamicRoomState == 1)
                        {
                            if (registration.BeginClosingCleanupComplete
                                && registration.FinalizeIdleCleanupComplete
                                && (!registration.ActivationEventsCreated
                                    || registration.ActivationEventCleanupComplete)
                                && !NativeDynamicRoomEventActivationAdapter
                                    .HasUnresolvedRollback(room)
                                && room.DynamicRoomPlayerCount <= 0)
                            {
                                if (_leaseOwner.TrySetLeaseState(
                                        activationLease, 0))
                                {
                                    registration.CurrentLease = null;
                                    registration.ActivationEventsCreated = false;
                                    room.DynamicRoomState = 0;
                                    room.DynamicRoomBlocked = false;
                                }
                                else
                                {
                                    room.DynamicRoomBlocked = true;
                                }
                            }
                            else
                            {
                                room.DynamicRoomBlocked = true;
                            }
                        }
                    }
                }
            }
        }

        private static void ReportCleanupFailure(RoomRegistration registration,
            string stage, Exception exception)
        {
            try
            {
                M2Share.ErrorMessage(
                    $"动态房回收失败({stage}): {registration.Environment.DynamicRoomName}" +
                    $"[{registration.Environment.DynamicRoomIndex}] {exception.Message}");
            }
            catch
            {
                // Cleanup progress must not depend on diagnostics being available.
            }
        }
    }
}
