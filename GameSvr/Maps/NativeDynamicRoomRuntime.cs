namespace GameSvr
{
    public sealed class NativeDynamicRoomPasRouteRegistration
    {
        public NativeDynamicRoomPasRouteRegistration(NormNpc npc,
            NativeDynamicRoomDynamicNpcScriptBinding binding)
        {
            Npc = npc;
            Binding = binding;
        }

        public NormNpc Npc { get; }
        public NativeDynamicRoomDynamicNpcScriptBinding Binding { get; }
    }

    /// <summary>
    /// Transaction and execution gate for production dynamic-room activation.
    /// </summary>
    public sealed class NativeDynamicRoomRuntime
    {
        private sealed class ActivationSession
        {
            public NativeDynamicRoomActivationLease Lease { get; init; }
            public NativeDynamicRoomEventActivationAdapter EventAdapter { get; init; }
            public List<NativeDynamicRoomPasScriptBindingHandle> RouteHandles { get; } = new();
            public List<(NormNpc Npc,
                NativeDynamicRoomDynamicNpcScriptBinding Binding)> RouteRequests
                { get; } = new();
            public bool RoutesRetired { get; set; }
            public bool Committed { get; set; }
        }

        private readonly object _gate = new();
        private readonly NativeDynamicRoomManager _manager;
        private readonly NativeDynamicRoomPasScriptRouteTable _routeTable;
        private readonly string _eventScriptDirectory;
        private readonly Dictionary<Envirnoment, ActivationSession> _sessions =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, int> _activePasDepthByThread = new();
        private readonly Dictionary<int, List<Envirnoment>>
            _activePasEnvironmentsByThread = new();
        private readonly List<Envirnoment> _pendingPlayerRemoved = new();
        private int _activePasExecutions;
        private bool _pendingRun;
        private Action _mutationWaitCheckpointForTests = null;

        public NativeDynamicRoomRuntime(NativeDynamicRoomManager manager,
            NativeDynamicRoomPasScriptRouteTable routeTable,
            string eventScriptDirectory)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(routeTable);
            if (string.IsNullOrWhiteSpace(eventScriptDirectory))
                throw new ArgumentException("Event script directory is required.",
                    nameof(eventScriptDirectory));

            _manager = manager;
            _routeTable = routeTable;
            _eventScriptDirectory = Path.GetFullPath(eventScriptDirectory);
            if (!_manager.TryAttachRuntime(this))
                throw new InvalidOperationException(
                    "dynamic room manager already has a lifecycle owner");
        }

        public bool TryReserveIdleRoomLease(string roomName, TPlayObject owner,
            out NativeDynamicRoomActivationLease lease)
        {
            return TryReserveIdleRoomLeaseFromManager(_manager, roomName,
                owner, out lease);
        }

        internal bool TryReserveIdleRoomLeaseFromManager(
            NativeDynamicRoomManager manager, string roomName,
            TPlayObject owner, out NativeDynamicRoomActivationLease lease)
        {
            if (!ReferenceEquals(manager, _manager))
            {
                lease = null;
                return false;
            }
            lock (_gate)
            {
                if (!IsCurrentThreadExecutingPasLocked())
                    WaitForNoActivePasLocked();
                // A dynamic PAS callback may synchronously acquire a different
                // idle room. The selected state-0 environment cannot be the
                // state-2 environment whose PAS is currently executing.
                return _manager.TryReserveIdleRoomLeaseUnderRuntime(this,
                    roomName, owner, out lease);
            }
        }

        public bool TryCommitReservedActivation(
            NativeDynamicRoomActivationLease lease,
            NativeDynamicRoomEventActivationAdapter eventAdapter,
            IReadOnlyList<NativeDynamicRoomPasRouteRegistration> registrations,
            out IReadOnlyList<string> diagnostics)
        {
            var messages = new List<string>();
            lock (_gate)
            {
                if (IsCurrentThreadExecutingEnvironmentLocked(
                        lease?.Environment))
                {
                    messages.Add(
                        "dynamic room activation cannot mutate its executing PAS environment");
                    diagnostics = messages.AsReadOnly();
                    return false;
                }
                if (!IsCurrentThreadExecutingPasLocked())
                    WaitForNoActivePasLocked();
                if (!IsExactActiveModelLease(lease)
                    || eventAdapter == null
                    || !ReferenceEquals(eventAdapter.Environment,
                        lease.Environment)
                    || registrations == null)
                {
                    messages.Add(
                        "dynamic room runtime requires an exact active model lease, event adapter, and route list");
                    diagnostics = messages.AsReadOnly();
                    return false;
                }

                if (_sessions.TryGetValue(lease.Environment, out var existing))
                {
                    diagnostics = messages.AsReadOnly();
                    return ReferenceEquals(existing.Lease, lease)
                           && ReferenceEquals(existing.EventAdapter, eventAdapter)
                           && existing.Committed
                           && MatchesRouteRequests(existing, registrations);
                }

                var session = new ActivationSession
                {
                    Lease = lease,
                    EventAdapter = eventAdapter
                };
                for (var index = 0; index < registrations.Count; index++)
                {
                    var registration = registrations[index];
                    session.RouteRequests.Add((registration?.Npc,
                        registration?.Binding));
                }
                _sessions.Add(lease.Environment, session);

                try
                {
                    for (var index = 0; index < registrations.Count; index++)
                    {
                        var registration = registrations[index];
                        if (!TryRegisterRoute(lease, registration, session,
                                out var routeError))
                        {
                            messages.Add($"route {index}: {routeError}");
                            return FailActivation(session, messages,
                                out diagnostics);
                        }
                    }

                    if (!IsExactActiveModelLease(lease))
                    {
                        messages.Add("dynamic room lease changed before event commit");
                        return FailActivation(session, messages,
                            out diagnostics);
                    }

                    if (!eventAdapter.TryActivate(lease,
                            _manager.TryMarkActivationEventsCreated,
                            _eventScriptDirectory, lease.RoomName,
                            out var eventDiagnostics))
                    {
                        if (eventDiagnostics != null)
                            messages.AddRange(eventDiagnostics);
                        messages.Add("dynamic room event activation did not commit");
                        return FailActivation(session, messages,
                            out diagnostics);
                    }

                    // Event commit is the final fallible operation.
                    session.Committed = true;
                    diagnostics = messages.AsReadOnly();
                    return true;
                }
                catch (Exception ex)
                {
                    messages.Add(
                        $"dynamic room activation failed ({ex.GetType().Name})");
                    return FailActivation(session, messages, out diagnostics);
                }
            }
        }

        public bool TryExecuteExpectedPas(NormNpc npc,
            NativeDynamicRoomPasScriptBindingHandle expectedHandle,
            Func<string, bool> execute)
        {
            if (npc == null || expectedHandle == null || execute == null)
                return false;

            string exactScriptPath;
            var threadId = Thread.CurrentThread.ManagedThreadId;
            lock (_gate)
            {
                var lease = expectedHandle.ActivationLease;
                if (lease?.Environment == null
                    || !_sessions.TryGetValue(lease.Environment,
                        out var session)
                    || !ReferenceEquals(session.Lease, lease)
                    || !session.Committed
                    || session.RoutesRetired
                    || !ContainsExactRouteHandle(session, expectedHandle)
                    || !IsExactActiveModelLease(lease)
                    || !_routeTable.ValidateExpected(npc, expectedHandle,
                        out exactScriptPath))
                    return false;

                _activePasExecutions++;
                _activePasDepthByThread.TryGetValue(threadId, out var depth);
                _activePasDepthByThread[threadId] = depth + 1;
                if (!_activePasEnvironmentsByThread.TryGetValue(threadId,
                        out var executingEnvironments))
                {
                    executingEnvironments = new List<Envirnoment>();
                    _activePasEnvironmentsByThread.Add(threadId,
                        executingEnvironments);
                }
                executingEnvironments.Add(lease.Environment);
            }

            try
            {
                return execute(exactScriptPath);
            }
            finally
            {
                CompletePasExecution(threadId);
            }
        }

        public bool TryBeginClosingCleanup(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null) return false;
            lock (_gate)
            {
                if (IsCurrentThreadExecutingEnvironmentLocked(
                        lease.Environment))
                    return false;
                if (!IsCurrentThreadExecutingPasLocked())
                    WaitForNoActivePasLocked();
                if (!_manager.IsCurrentLeaseInState(lease, 1)) return false;
                if (!TryGetExactSession(lease, out var session,
                        out var conflictingSession))
                    return !conflictingSession;

                RetireRoutes(session);
                return true;
            }
        }

        public bool TryFinalizeIdleCleanup(
            NativeDynamicRoomActivationLease lease)
        {
            lock (_gate)
            {
                if (IsCurrentThreadExecutingPasLocked()) return false;
                WaitForNoActivePasLocked();
                if (!_manager.IsCurrentLeaseInState(lease, 1)) return false;
                if (!TryGetExactSession(lease, out var session,
                        out var conflictingSession))
                    return !conflictingSession;

                RetireRoutes(session);
                if (!session.EventAdapter.HasActivationEvents
                    && !NativeDynamicRoomEventActivationAdapter
                        .HasUnresolvedRollback(lease.Environment))
                    RemoveExactSession(session);
                return true;
            }
        }

        public bool TryCloseActivationEvents(
            NativeDynamicRoomActivationLease lease)
        {
            lock (_gate)
            {
                if (IsCurrentThreadExecutingPasLocked()) return false;
                WaitForNoActivePasLocked();
                if (!_manager.IsCurrentLeaseInState(lease, 1)) return false;
                if (!TryGetExactSession(lease, out var session,
                        out var conflictingSession))
                {
                    return !conflictingSession
                           && !NativeDynamicRoomEventActivationAdapter
                               .HasUnresolvedRollback(lease?.Environment);
                }

                RetireRoutes(session);
                if (!session.EventAdapter.TryFinalizeActivation(lease, out _))
                    return false;
                if (!NativeDynamicRoomEventActivationAdapter
                        .HasUnresolvedRollback(lease.Environment))
                    RemoveExactSession(session);
                return true;
            }
        }

        public void Run()
        {
            RunFromManager(_manager);
        }

        internal void RunFromManager(NativeDynamicRoomManager manager)
        {
            if (!ReferenceEquals(manager, _manager)) return;

            lock (_gate)
            {
                if (IsCurrentThreadExecutingPasLocked())
                {
                    _pendingRun = true;
                    return;
                }
                WaitForNoActivePasLocked();
                _manager.RunUnderRuntime(this);
            }
        }

        internal void NotifyPlayerRemovedFromManager(
            NativeDynamicRoomManager manager, Envirnoment environment)
        {
            if (!ReferenceEquals(manager, _manager) || environment == null)
                return;

            lock (_gate)
            {
                if (IsCurrentThreadExecutingPasLocked())
                {
                    EnqueuePendingPlayerRemovedLocked(environment);
                    return;
                }
                WaitForNoActivePasLocked();
                _manager.NotifyPlayerRemovedUnderRuntime(this, environment);
            }
        }

        internal bool TryAbortReservedRoomLeaseFromManager(
            NativeDynamicRoomManager manager,
            NativeDynamicRoomActivationLease lease)
        {
            if (!ReferenceEquals(manager, _manager) || lease == null)
                return false;

            lock (_gate)
            {
                if (IsCurrentThreadExecutingEnvironmentLocked(
                        lease.Environment))
                    return false;
                if (!IsCurrentThreadExecutingPasLocked())
                    WaitForNoActivePasLocked();
                if (_sessions.TryGetValue(lease.Environment, out var session)
                    && ReferenceEquals(session.Lease, lease)
                    && session.Committed)
                    return false;
                return _manager.TryAbortReservedRoomLeaseUnderRuntime(this,
                    lease);
            }
        }

        private bool TryRegisterRoute(NativeDynamicRoomActivationLease lease,
            NativeDynamicRoomPasRouteRegistration registration,
            ActivationSession session, out string error)
        {
            error = null;
            var npc = registration?.Npc;
            var binding = registration?.Binding;
            if (npc == null || binding == null)
            {
                error = "NPC or planned binding is null";
                return false;
            }
            if (!ReferenceEquals(binding.Definition, lease.Definition)
                || !ReferenceEquals(npc.m_PEnvir, lease.Environment)
                || npc.m_boGhost
                || M2Share.ObjectManager == null
                || !ReferenceEquals(M2Share.ObjectManager.Get(npc.ObjectId), npc))
            {
                error = "NPC, definition, environment, or ObjectManager identity is stale";
                return false;
            }

            var handle = _routeTable.Register(npc, lease, binding);
            session.RouteHandles.Add(handle);
            if (handle == null
                || handle.Released
                || !ReferenceEquals(handle.Npc, npc)
                || handle.NpcObjectId != npc.ObjectId
                || !ReferenceEquals(handle.ActivationLease, lease)
                || !handle.BoundToCurrentActivation
                || !handle.DefinitionMatchesActivation
                || !handle.BoundToLeaseEnvironment
                || !handle.HasCanonicalScriptPath)
            {
                error = "route handle did not preserve exact activation identity";
                return false;
            }

            if (binding.HasScript
                && !_routeTable.ValidateExpected(npc, handle, out _))
            {
                error = "planned PAS script was unavailable at commit time";
                return false;
            }

            return true;
        }

        private bool FailActivation(ActivationSession session,
            List<string> messages, out IReadOnlyList<string> diagnostics)
        {
            RetireRoutes(session);
            var unresolved = NativeDynamicRoomEventActivationAdapter
                .HasUnresolvedRollback(session.Lease.Environment);
            if (!unresolved)
            {
                RemoveExactSession(session);
                if (!_manager.TryAbortReservedRoomLeaseUnderRuntime(this,
                        session.Lease)
                    && session.Lease.Environment.DynamicRoomState == 2
                    && session.Lease.IsCurrentActive())
                {
                    messages.Add(
                        "dynamic room activation rollback could not abort the exact lease");
                }
            }
            else
            {
                messages.Add(
                    "dynamic room event rollback remains pending exact cleanup");
            }

            diagnostics = messages.AsReadOnly();
            return false;
        }

        private void RetireRoutes(ActivationSession session)
        {
            if (session.RoutesRetired) return;
            for (var index = session.RouteHandles.Count - 1; index >= 0; index--)
                _routeTable.Unregister(session.RouteHandles[index]);
            session.RoutesRetired = true;
        }

        private bool TryGetExactSession(NativeDynamicRoomActivationLease lease,
            out ActivationSession session, out bool conflictingSession)
        {
            session = null;
            conflictingSession = false;
            if (lease == null) return false;
            if (!_sessions.TryGetValue(lease.Environment, out var current))
                return false;
            if (!ReferenceEquals(current.Lease, lease))
            {
                conflictingSession = true;
                return false;
            }
            session = current;
            return true;
        }

        private void RemoveExactSession(ActivationSession session)
        {
            if (_sessions.TryGetValue(session.Lease.Environment, out var current)
                && ReferenceEquals(current, session))
                _sessions.Remove(session.Lease.Environment);
        }

        private static bool MatchesRouteRequests(ActivationSession session,
            IReadOnlyList<NativeDynamicRoomPasRouteRegistration> registrations)
        {
            if (registrations == null
                || session.RouteRequests.Count != registrations.Count)
                return false;
            for (var index = 0; index < registrations.Count; index++)
            {
                var registration = registrations[index];
                var expected = session.RouteRequests[index];
                if (registration == null
                    || !ReferenceEquals(expected.Npc, registration.Npc)
                    || !ReferenceEquals(expected.Binding,
                        registration.Binding))
                    return false;
            }
            return true;
        }

        private static bool ContainsExactRouteHandle(ActivationSession session,
            NativeDynamicRoomPasScriptBindingHandle expectedHandle)
        {
            for (var index = 0; index < session.RouteHandles.Count; index++)
            {
                if (ReferenceEquals(session.RouteHandles[index], expectedHandle))
                    return true;
            }
            return false;
        }

        private void CompletePasExecution(int threadId)
        {
            lock (_gate)
            {
                if (!_activePasDepthByThread.TryGetValue(threadId,
                        out var depth)
                    || depth <= 0
                    || _activePasExecutions <= 0)
                    throw new InvalidOperationException(
                        "dynamic room PAS execution accounting is unbalanced");

                _activePasExecutions--;
                if (!_activePasEnvironmentsByThread.TryGetValue(threadId,
                        out var executingEnvironments)
                    || executingEnvironments.Count == 0)
                    throw new InvalidOperationException(
                        "dynamic room PAS environment accounting is unbalanced");
                executingEnvironments.RemoveAt(
                    executingEnvironments.Count - 1);
                if (executingEnvironments.Count == 0)
                    _activePasEnvironmentsByThread.Remove(threadId);
                if (depth == 1)
                    _activePasDepthByThread.Remove(threadId);
                else
                    _activePasDepthByThread[threadId] = depth - 1;

                if (_activePasExecutions != 0) return;
                Monitor.PulseAll(_gate);
                DrainPendingLifecycleLocked();
            }
        }

        private void WaitForNoActivePasLocked()
        {
            while (_activePasExecutions > 0)
            {
                _mutationWaitCheckpointForTests?.Invoke();
                Monitor.Wait(_gate);
            }
        }

        private bool IsCurrentThreadExecutingPasLocked()
        {
            return _activePasDepthByThread.TryGetValue(
                       Thread.CurrentThread.ManagedThreadId, out var depth)
                   && depth > 0;
        }

        private bool IsCurrentThreadExecutingEnvironmentLocked(
            Envirnoment environment)
        {
            if (environment == null
                || !_activePasEnvironmentsByThread.TryGetValue(
                    Thread.CurrentThread.ManagedThreadId,
                    out var executingEnvironments))
                return false;
            for (var index = executingEnvironments.Count - 1; index >= 0;
                 index--)
            {
                if (ReferenceEquals(executingEnvironments[index], environment))
                    return true;
            }
            return false;
        }

        internal bool TryGetCommittedRouteHandles(
            NativeDynamicRoomActivationLease lease,
            out IReadOnlyList<NativeDynamicRoomPasScriptBindingHandle> handles)
        {
            handles = Array.Empty<NativeDynamicRoomPasScriptBindingHandle>();
            if (lease == null) return false;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(lease.Environment,
                        out var session)
                    || !ReferenceEquals(session.Lease, lease)
                    || !session.Committed || session.RoutesRetired)
                    return false;
                handles = Array.AsReadOnly(session.RouteHandles.ToArray());
                return true;
            }
        }

        private void EnqueuePendingPlayerRemovedLocked(
            Envirnoment environment)
        {
            for (var index = 0; index < _pendingPlayerRemoved.Count; index++)
            {
                if (ReferenceEquals(_pendingPlayerRemoved[index], environment))
                    return;
            }
            _pendingPlayerRemoved.Add(environment);
        }

        private void DrainPendingLifecycleLocked()
        {
            if (_activePasExecutions != 0) return;
            if (_pendingPlayerRemoved.Count == 0 && !_pendingRun) return;

            var notifications = _pendingPlayerRemoved.ToArray();
            var run = _pendingRun;
            _pendingPlayerRemoved.Clear();
            _pendingRun = false;
            try
            {
                for (var index = 0; index < notifications.Length; index++)
                {
                    _manager.NotifyPlayerRemovedUnderRuntime(this,
                        notifications[index]);
                }
            }
            finally
            {
                if (run) _manager.RunUnderRuntime(this);
            }
        }

        private bool IsExactActiveModelLease(
            NativeDynamicRoomActivationLease lease)
        {
            return lease != null
                   && lease.Definition != null
                   && lease.Environment != null
                   && lease.Environment.IsDynamicRoom
                   && lease.Environment.DynamicRoomState == 2
                   && _manager.IsCurrentActiveLease(lease);
        }
    }
}
