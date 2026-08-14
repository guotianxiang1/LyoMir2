using System;
using System.Collections.Generic;
using System.Linq;

namespace GameSvr.Features.Instance
{
    /// <summary>
    /// Tracks and categorizes error paths for dynamic room operations.
    /// Provides diagnostic information for failure scenarios across initialization,
    /// activation, lifecycle, and cleanup phases.
    /// </summary>
    public sealed class DynamicRoomErrorPath
    {
        /// <summary>
        /// Error phase categories for dynamic room operations.
        /// </summary>
        public enum ErrorPhase
        {
            Initialization,
            DefinitionLoad,
            EnvironmentCreation,
            PhysicalInstantiation,
            Activation,
            Reservation,
            NpcMaterialization,
            EventBinding,
            Cleanup,
            Retirement,
            Lifecycle
        }

        /// <summary>
        /// Severity level for error tracking and reporting.
        /// </summary>
        public enum ErrorSeverity
        {
            Information,
            Warning,
            Error,
            Critical
        }

        private sealed class ErrorEntry
        {
            public ErrorPhase Phase { get; init; }
            public ErrorSeverity Severity { get; init; }
            public string Message { get; init; }
            public string RoomName { get; init; }
            public int? PhysicalInstanceId { get; init; }
            public long Timestamp { get; init; }
            public string Context { get; init; }
        }

        private readonly object _syncRoot = new();
        private readonly List<ErrorEntry> _errors = new();
        private readonly Func<long> _timestampProvider;
        private readonly int _maxHistorySize;

        public DynamicRoomErrorPath(int maxHistorySize = 1000,
            Func<long> timestampProvider = null)
        {
            if (maxHistorySize < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHistorySize),
                    "Maximum history size must be at least 1");

            _maxHistorySize = maxHistorySize;
            _timestampProvider = timestampProvider
                ?? (() => Environment.TickCount64);
        }

        /// <summary>
        /// Records an error encountered during dynamic room operations.
        /// </summary>
        public void RecordError(ErrorPhase phase, ErrorSeverity severity,
            string message, string roomName = null,
            int? physicalInstanceId = null, string context = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var entry = new ErrorEntry
            {
                Phase = phase,
                Severity = severity,
                Message = message,
                RoomName = roomName,
                PhysicalInstanceId = physicalInstanceId,
                Timestamp = _timestampProvider(),
                Context = context
            };

            lock (_syncRoot)
            {
                _errors.Add(entry);
                while (_errors.Count > _maxHistorySize)
                {
                    _errors.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Records an initialization failure.
        /// </summary>
        public void RecordInitializationFailure(string message,
            string context = null)
        {
            RecordError(ErrorPhase.Initialization, ErrorSeverity.Critical,
                message, context: context);
        }

        /// <summary>
        /// Records a definition load failure.
        /// </summary>
        public void RecordDefinitionLoadFailure(string roomName, string message,
            string context = null)
        {
            RecordError(ErrorPhase.DefinitionLoad, ErrorSeverity.Error,
                message, roomName, context: context);
        }

        /// <summary>
        /// Records an environment creation failure.
        /// </summary>
        public void RecordEnvironmentCreationFailure(string roomName,
            int? physicalInstanceId, string message, string context = null)
        {
            RecordError(ErrorPhase.EnvironmentCreation, ErrorSeverity.Critical,
                message, roomName, physicalInstanceId, context);
        }

        /// <summary>
        /// Records a physical instantiation failure.
        /// </summary>
        public void RecordPhysicalInstantiationFailure(string roomName,
            int physicalInstanceId, string message, string context = null)
        {
            RecordError(ErrorPhase.PhysicalInstantiation,
                ErrorSeverity.Critical, message, roomName, physicalInstanceId,
                context);
        }

        /// <summary>
        /// Records an activation failure.
        /// </summary>
        public void RecordActivationFailure(string roomName, int roomIndex,
            string message, string context = null)
        {
            RecordError(ErrorPhase.Activation, ErrorSeverity.Error, message,
                roomName, roomIndex, context);
        }

        /// <summary>
        /// Records a reservation failure.
        /// </summary>
        public void RecordReservationFailure(string roomName, string message,
            string context = null)
        {
            RecordError(ErrorPhase.Reservation, ErrorSeverity.Warning, message,
                roomName, context: context);
        }

        /// <summary>
        /// Records an NPC materialization failure.
        /// </summary>
        public void RecordNpcMaterializationFailure(string roomName,
            int physicalInstanceId, string message, string context = null)
        {
            RecordError(ErrorPhase.NpcMaterialization, ErrorSeverity.Error,
                message, roomName, physicalInstanceId, context);
        }

        /// <summary>
        /// Records an event binding failure.
        /// </summary>
        public void RecordEventBindingFailure(string roomName,
            int physicalInstanceId, string message, string context = null)
        {
            RecordError(ErrorPhase.EventBinding, ErrorSeverity.Error, message,
                roomName, physicalInstanceId, context);
        }

        /// <summary>
        /// Records a cleanup failure.
        /// </summary>
        public void RecordCleanupFailure(string roomName, int roomIndex,
            string message, string context = null)
        {
            RecordError(ErrorPhase.Cleanup, ErrorSeverity.Warning, message,
                roomName, roomIndex, context);
        }

        /// <summary>
        /// Records a retirement failure.
        /// </summary>
        public void RecordRetirementFailure(string roomName,
            int physicalInstanceId, string message, string context = null)
        {
            RecordError(ErrorPhase.Retirement, ErrorSeverity.Error, message,
                roomName, physicalInstanceId, context);
        }

        /// <summary>
        /// Records a lifecycle state transition failure.
        /// </summary>
        public void RecordLifecycleFailure(string roomName, int roomIndex,
            string message, string context = null)
        {
            RecordError(ErrorPhase.Lifecycle, ErrorSeverity.Warning, message,
                roomName, roomIndex, context);
        }

        /// <summary>
        /// Gets the total number of errors recorded.
        /// </summary>
        public int ErrorCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _errors.Count;
                }
            }
        }

        /// <summary>
        /// Gets the count of errors for a specific phase.
        /// </summary>
        public int GetPhaseErrorCount(ErrorPhase phase)
        {
            lock (_syncRoot)
            {
                return _errors.Count(e => e.Phase == phase);
            }
        }

        /// <summary>
        /// Gets the count of errors for a specific severity.
        /// </summary>
        public int GetSeverityErrorCount(ErrorSeverity severity)
        {
            lock (_syncRoot)
            {
                return _errors.Count(e => e.Severity == severity);
            }
        }

        /// <summary>
        /// Gets the count of errors for a specific room.
        /// </summary>
        public int GetRoomErrorCount(string roomName)
        {
            if (string.IsNullOrEmpty(roomName))
                return 0;

            lock (_syncRoot)
            {
                return _errors.Count(e => e.RoomName == roomName);
            }
        }

        /// <summary>
        /// Gets recent errors within the specified time window.
        /// </summary>
        public IReadOnlyList<string> GetRecentErrors(long milliseconds)
        {
            if (milliseconds <= 0)
                return Array.Empty<string>();

            lock (_syncRoot)
            {
                var cutoff = _timestampProvider() - milliseconds;
                return _errors
                    .Where(e => e.Timestamp >= cutoff)
                    .Select(e => FormatError(e))
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Gets errors for a specific phase.
        /// </summary>
        public IReadOnlyList<string> GetPhaseErrors(ErrorPhase phase)
        {
            lock (_syncRoot)
            {
                return _errors
                    .Where(e => e.Phase == phase)
                    .Select(e => FormatError(e))
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Gets errors for a specific room.
        /// </summary>
        public IReadOnlyList<string> GetRoomErrors(string roomName)
        {
            if (string.IsNullOrEmpty(roomName))
                return Array.Empty<string>();

            lock (_syncRoot)
            {
                return _errors
                    .Where(e => e.RoomName == roomName)
                    .Select(e => FormatError(e))
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Gets errors matching a specific severity level.
        /// </summary>
        public IReadOnlyList<string> GetErrorsBySeverity(
            ErrorSeverity severity)
        {
            lock (_syncRoot)
            {
                return _errors
                    .Where(e => e.Severity == severity)
                    .Select(e => FormatError(e))
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Gets all recorded errors formatted for display.
        /// </summary>
        public IReadOnlyList<string> GetAllErrors()
        {
            lock (_syncRoot)
            {
                return _errors
                    .Select(e => FormatError(e))
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Generates a diagnostic summary of error patterns.
        /// </summary>
        public string GetDiagnosticSummary()
        {
            lock (_syncRoot)
            {
                if (_errors.Count == 0)
                    return "No errors recorded";

                var phaseGroups = _errors.GroupBy(e => e.Phase)
                    .OrderByDescending(g => g.Count());
                var severityGroups = _errors.GroupBy(e => e.Severity)
                    .OrderByDescending(g => g.Count());

                var lines = new List<string>
                {
                    $"Total errors: {_errors.Count}",
                    $"Errors by phase:"
                };

                foreach (var group in phaseGroups)
                {
                    lines.Add($"  {group.Key}: {group.Count()}");
                }

                lines.Add($"Errors by severity:");
                foreach (var group in severityGroups)
                {
                    lines.Add($"  {group.Key}: {group.Count()}");
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

        /// <summary>
        /// Clears all recorded errors.
        /// </summary>
        public void Clear()
        {
            lock (_syncRoot)
            {
                _errors.Clear();
            }
        }

        private static string FormatError(ErrorEntry entry)
        {
            var parts = new List<string>
            {
                $"[{entry.Phase}]",
                $"[{entry.Severity}]"
            };

            if (!string.IsNullOrEmpty(entry.RoomName))
            {
                if (entry.PhysicalInstanceId.HasValue)
                    parts.Add(
                        $"{entry.RoomName}[{entry.PhysicalInstanceId.Value}]");
                else
                    parts.Add(entry.RoomName);
            }

            parts.Add(entry.Message);

            if (!string.IsNullOrEmpty(entry.Context))
                parts.Add($"({entry.Context})");

            return string.Join(" ", parts);
        }
    }
}
