using System.Runtime.CompilerServices;

namespace GameSvr
{
    public sealed class NativeDynamicRoomEventActivationAdapter
    {
        private sealed class StagedEventReference
        {
            public StagedEventReference(Event value, Envirnoment environment,
                int x, int y)
            {
                Value = value;
                Environment = environment;
                X = x;
                Y = y;
            }

            public Event Value { get; }
            public Envirnoment Environment { get; }
            public int X { get; }
            public int Y { get; }
            public bool Mounted { get; set; }
        }

        private sealed class EnvironmentActivationState
        {
            public object SyncRoot { get; } = new();
            public EventManager EventManager { get; set; }
            public NativeDynamicRoomActivationLease ActivationLease { get; set; }
            public NativeDynamicRoomActivationLease RollbackFailureLease;
            public byte F9 { get; set; }
            public int EventCount { get; set; }
            public int MountedEventCount { get; set; }
            public IReadOnlyList<StagedEventReference> StagedEvents { get; set; }
        }

        public const int MaximumDurationMilliseconds = 720_000;

        private static readonly ConditionalWeakTable<Envirnoment,
            EnvironmentActivationState> ActivationStates = new();

        private readonly EventManager _eventManager;
        private readonly Envirnoment _environment;
        private readonly EnvironmentActivationState _state;

        public NativeDynamicRoomEventActivationAdapter(EventManager eventManager,
            Envirnoment environment)
        {
            ArgumentNullException.ThrowIfNull(eventManager);
            ArgumentNullException.ThrowIfNull(environment);
            _eventManager = eventManager;
            _environment = environment;
            _state = ActivationStates.GetValue(environment,
                static _ => new EnvironmentActivationState());
        }

        public Envirnoment Environment => _environment;

        public byte F9
        {
            get
            {
                lock (_state.SyncRoot) return _state.F9;
            }
        }

        public bool HasActivationEvents => F9 != 0;

        public int ActivationEventCount
        {
            get
            {
                lock (_state.SyncRoot) return _state.EventCount;
            }
        }

        public int MountedActivationEventCount
        {
            get
            {
                lock (_state.SyncRoot) return _state.MountedEventCount;
            }
        }

        public bool TryActivate(NativeDynamicRoomActivationLease lease,
            Func<NativeDynamicRoomActivationLease, bool> tryCommitEventsCreated,
            string scriptDirectory, string roomName,
            out IReadOnlyList<string> diagnostics)
        {
            var activationDiagnostics = new List<string>();
            lock (_state.SyncRoot)
            {
                if (lease == null || tryCommitEventsCreated == null
                    || !ReferenceEquals(lease.Environment, _environment)
                    || !_environment.IsDynamicRoom
                    || _environment.DynamicRoomState != 2
                    || !lease.IsCurrentActive())
                {
                    activationDiagnostics.Add(
                        "dynamic room events require the exact active room lease");
                    diagnostics = activationDiagnostics.AsReadOnly();
                    return false;
                }

                if (_state.F9 != 0)
                {
                    diagnostics = activationDiagnostics.AsReadOnly();
                    return ReferenceEquals(_state.ActivationLease, lease)
                           && System.Threading.Volatile.Read(
                               ref _state.RollbackFailureLease) == null;
                }

                if (!TryPublishPendingRollback(lease))
                {
                    activationDiagnostics.Add(
                        "dynamic room event activation could not guard the exact lease");
                    diagnostics = activationDiagnostics.AsReadOnly();
                    return false;
                }

                try
                {
                    if (!NativeDynamicRoomEventDescriptorLoader.TryLoad(
                            scriptDirectory, roomName, out var descriptors,
                            out var loadDiagnostics))
                    {
                        ClearPendingRollback(lease);
                        diagnostics = loadDiagnostics;
                        return false;
                    }
                    activationDiagnostics.AddRange(loadDiagnostics);

                    var stagedEvents = new List<StagedEventReference>();
                    try
                    {
                        foreach (var descriptor in descriptors)
                        {
                            if (descriptor == null)
                            {
                                activationDiagnostics.Add(
                                    "dynamic room event descriptor was null");
                                RollbackWithCompensation(lease, stagedEvents,
                                    activationDiagnostics);
                                diagnostics = activationDiagnostics.AsReadOnly();
                                return false;
                            }

                            var durationMilliseconds = GetDurationMilliseconds(
                                descriptor.DurationSeconds);
                            foreach (var coordinate in descriptor.Coordinates)
                            {
                                if (!IsValidCoordinate(coordinate))
                                {
                                    activationDiagnostics.Add(
                                        $"line {descriptor.SourceLine}: event coordinate is outside the target environment");
                                    continue;
                                }

                                var activationEvent = CreateActivationEvent(
                                    descriptor, coordinate,
                                    durationMilliseconds, stagedEvents,
                                    activationDiagnostics);
                                if (!activationEvent.Mounted)
                                {
                                    activationDiagnostics.Add(
                                        $"line {descriptor.SourceLine}: event map attachment failed; registered with zero duration");
                                }
                            }
                        }

                        foreach (var activationEvent in stagedEvents)
                        {
                            _eventManager.AddEvent(activationEvent.Value);
                            if (!_eventManager.ContainsEventExact(
                                    activationEvent.Value))
                            {
                                throw new InvalidOperationException(
                                    "event manager did not retain an activation event");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RollbackWithCompensation(lease, stagedEvents,
                            activationDiagnostics);
                        activationDiagnostics.Add(
                            $"dynamic room event activation failed ({ex.GetType().Name})");
                        diagnostics = activationDiagnostics.AsReadOnly();
                        return false;
                    }

                    if (stagedEvents.Count == 0)
                    {
                        _state.ActivationLease = lease;
                        _state.EventManager = null;
                        _state.EventCount = 0;
                        _state.MountedEventCount = 0;
                        _state.StagedEvents = Array.Empty<StagedEventReference>();
                        _state.F9 = 0;
                        ClearPendingRollback(lease);
                        diagnostics = activationDiagnostics.AsReadOnly();
                        return true;
                    }

                    var committedDiagnostics =
                        activationDiagnostics.AsReadOnly();
                    var committed = false;
                    try
                    {
                        committed = tryCommitEventsCreated(lease);
                    }
                    catch (Exception ex)
                    {
                        activationDiagnostics.Add(
                            $"dynamic room event commit failed ({ex.GetType().Name})");
                    }

                    if (!committed)
                    {
                        RollbackWithCompensation(lease, stagedEvents,
                            activationDiagnostics);
                        diagnostics = committedDiagnostics;
                        return false;
                    }

                    _state.ActivationLease = lease;
                    _state.EventManager = _eventManager;
                    _state.EventCount = stagedEvents.Count;
                    _state.MountedEventCount = stagedEvents.Count(value =>
                        value.Mounted);
                    _state.StagedEvents = stagedEvents.ToArray();
                    _state.F9 = 1;
                    ClearPendingRollback(lease);
                    diagnostics = committedDiagnostics;
                    return true;
                }
                catch (Exception ex)
                {
                    if (_state.F9 == 0
                        || !ReferenceEquals(_state.ActivationLease, lease))
                        ClearPendingRollback(lease);
                    activationDiagnostics.Add(
                        $"dynamic room event descriptor load failed ({ex.GetType().Name})");
                    diagnostics = activationDiagnostics.AsReadOnly();
                    return false;
                }
            }
        }

        public bool TryFinalizeActivation(NativeDynamicRoomActivationLease lease,
            out int closedCount)
        {
            lock (_state.SyncRoot)
            {
                closedCount = 0;
                if (lease == null
                    || !ReferenceEquals(lease.Environment, _environment)
                    || !ReferenceEquals(_state.ActivationLease, lease)
                    || !_environment.IsDynamicRoom
                    || _environment.DynamicRoomState is not (0 or 1))
                {
                    return false;
                }

                if (_state.F9 == 0) return true;
                if (_state.EventManager == null || _state.StagedEvents == null)
                    return false;

                var exactEvents = _state.StagedEvents;
                var newlyClosedCount = exactEvents.Count(value =>
                    value?.Value != null && !value.Value.m_boClosed);
                var cleanupDiagnostics = new List<string>();
                if (!Rollback(_state.EventManager, exactEvents,
                        cleanupDiagnostics))
                    return false;

                closedCount = newlyClosedCount;
                _state.EventManager = null;
                _state.EventCount = 0;
                _state.MountedEventCount = 0;
                _state.StagedEvents = null;
                _state.F9 = 0;
                if (ReferenceEquals(System.Threading.Volatile.Read(
                        ref _state.RollbackFailureLease), lease))
                {
                    ClearPendingRollback(lease);
                }
                return true;
            }
        }

        internal static bool HasUnresolvedRollback(Envirnoment environment)
        {
            return environment != null
                   && ActivationStates.TryGetValue(environment, out var state)
                   && System.Threading.Volatile.Read(
                       ref state.RollbackFailureLease) != null;
        }

        private StagedEventReference CreateActivationEvent(
            NativeDynamicRoomEventDescriptor descriptor,
            NativeDynamicRoomEventCoordinate coordinate,
            int durationMilliseconds,
            IList<StagedEventReference> stagedEvents,
            ICollection<string> diagnostics)
        {
            var activationEvent = new Event(_environment,
                coordinate.X, coordinate.Y,
                descriptor.EffectiveEventType,
                durationMilliseconds, true);
            var stagedEvent = new StagedEventReference(activationEvent,
                _environment, coordinate.X, coordinate.Y);
            stagedEvents.Add(stagedEvent);

            stagedEvent.Mounted = ReferenceEquals(activationEvent.m_Envir,
                                       _environment)
                                   && _environment.ContainsEventAtExact(
                                       coordinate.X, coordinate.Y,
                                       activationEvent);
            if (stagedEvent.Mounted) return stagedEvent;

            if (!CleanupStagedEvent(_eventManager, stagedEvent, diagnostics))
                throw new InvalidOperationException(
                    "failed event map attachment could not be compensated");
            stagedEvents.Remove(stagedEvent);

            activationEvent = new Event(_environment,
                coordinate.X, coordinate.Y,
                descriptor.EffectiveEventType, 0, false);
            stagedEvent = new StagedEventReference(activationEvent,
                _environment, coordinate.X, coordinate.Y);
            stagedEvents.Add(stagedEvent);
            return stagedEvent;
        }

        private bool IsValidCoordinate(
            NativeDynamicRoomEventCoordinate coordinate)
        {
            return coordinate != null
                   && coordinate.X > 0 && coordinate.Y > 0
                   && coordinate.X < _environment.wWidth
                   && coordinate.Y < _environment.wHeight;
        }

        private static int GetDurationMilliseconds(int durationSeconds)
        {
            var milliseconds = unchecked(durationSeconds * 1000);
            return milliseconds > MaximumDurationMilliseconds
                ? MaximumDurationMilliseconds
                : milliseconds;
        }

        private bool RetainFailedRollback(
            NativeDynamicRoomActivationLease lease,
            IReadOnlyCollection<StagedEventReference> stagedEvents)
        {
            if (!IsExactCompensationLease(lease))
            {
                ClearPendingRollback(lease);
                return false;
            }

            _state.ActivationLease = lease;
            _state.EventManager = _eventManager;
            _state.EventCount = stagedEvents.Count;
            _state.MountedEventCount = stagedEvents.Count(value =>
                value.Mounted);
            _state.StagedEvents = stagedEvents.ToArray();
            _state.F9 = 1;
            _environment.DynamicRoomBlocked = true;
            return true;
        }

        private bool RollbackWithCompensation(
            NativeDynamicRoomActivationLease lease,
            IReadOnlyCollection<StagedEventReference> stagedEvents,
            ICollection<string> diagnostics)
        {
            var pendingPublished = ReferenceEquals(
                System.Threading.Volatile.Read(
                    ref _state.RollbackFailureLease), lease);
            var complete = Rollback(_eventManager, stagedEvents, diagnostics);
            if (complete)
            {
                if (pendingPublished) ClearPendingRollback(lease);
                return true;
            }

            if (!pendingPublished
                || !RetainFailedRollback(lease, stagedEvents))
            {
                diagnostics.Add(
                    "dynamic room event rollback compensation rejected a stale lease");
            }
            return false;
        }

        private bool TryPublishPendingRollback(
            NativeDynamicRoomActivationLease lease)
        {
            if (!IsExactActiveLease(lease)) return false;

            var existing = System.Threading.Interlocked.CompareExchange(
                ref _state.RollbackFailureLease, lease, null);
            if (existing != null && !ReferenceEquals(existing, lease))
                return false;

            if (IsExactActiveLease(lease)) return true;
            ClearPendingRollback(lease);
            return false;
        }

        private void ClearPendingRollback(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null) return;
            System.Threading.Interlocked.CompareExchange(
                ref _state.RollbackFailureLease, null, lease);
        }

        private bool IsExactActiveLease(
            NativeDynamicRoomActivationLease lease)
        {
            return lease != null
                   && ReferenceEquals(lease.Environment, _environment)
                   && _environment.IsDynamicRoom
                   && _environment.DynamicRoomState == 2
                   && lease.IsCurrentActive();
        }

        private bool IsExactCompensationLease(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null
                || !ReferenceEquals(System.Threading.Volatile.Read(
                    ref _state.RollbackFailureLease), lease)
                || !ReferenceEquals(lease.Environment, _environment)
                || !_environment.IsDynamicRoom)
                return false;

            // The exact marker was acquired in state 2 and prevents reuse;
            // state 1 is therefore the same activation closing in place.
            return _environment.DynamicRoomState == 1
                   || IsExactActiveLease(lease);
        }

        private bool Rollback(EventManager eventManager,
            IEnumerable<StagedEventReference> stagedEvents,
            ICollection<string> diagnostics)
        {
            var complete = true;
            foreach (var stagedEvent in stagedEvents.Reverse())
            {
                if (!CleanupStagedEvent(eventManager, stagedEvent,
                        diagnostics))
                    complete = false;
            }
            return complete;
        }

        private static bool CleanupStagedEvent(EventManager eventManager,
            StagedEventReference stagedEvent,
            ICollection<string> diagnostics)
        {
            if (eventManager == null || stagedEvent?.Value == null
                || stagedEvent.Environment == null)
            {
                diagnostics?.Add(
                    "dynamic room event rollback encountered a missing exact reference");
                return false;
            }

            var value = stagedEvent.Value;
            try
            {
                value.Close();
            }
            catch (Exception ex)
            {
                diagnostics?.Add(
                    $"dynamic room event close failed ({ex.GetType().Name})");
            }

            try
            {
                stagedEvent.Environment.RemoveEventEverywhereExact(value);
            }
            catch (Exception ex)
            {
                diagnostics?.Add(
                    $"dynamic room event map removal failed ({ex.GetType().Name})");
            }

            var originalCellAbsent = false;
            try
            {
                originalCellAbsent = !stagedEvent.Environment
                    .ContainsEventAtExact(stagedEvent.X, stagedEvent.Y, value);
            }
            catch (Exception ex)
            {
                diagnostics?.Add(
                    $"dynamic room event map verification failed ({ex.GetType().Name})");
            }

            var mapAbsent = false;
            if (originalCellAbsent)
            {
                try
                {
                    mapAbsent = !stagedEvent.Environment
                        .ContainsEventEverywhereExact(value);
                }
                catch (Exception ex)
                {
                    diagnostics?.Add(
                        $"dynamic room event map verification failed ({ex.GetType().Name})");
                }
            }

            try
            {
                eventManager.DiscardEventExact(value);
            }
            catch (Exception ex)
            {
                diagnostics?.Add(
                    $"dynamic room event manager discard failed ({ex.GetType().Name})");
            }

            var managerAbsent = false;
            try
            {
                managerAbsent = !eventManager.ContainsEventExact(value);
            }
            catch (Exception ex)
            {
                diagnostics?.Add(
                    $"dynamic room event manager verification failed ({ex.GetType().Name})");
            }

            if (mapAbsent && managerAbsent)
            {
                value.m_boClosed = true;
                value.m_boActive = false;
                value.m_boVisible = false;
                value.m_Envir = null;
                return true;
            }

            diagnostics?.Add(
                "dynamic room event rollback retained an exact staged reference");
            return false;
        }
    }
}
