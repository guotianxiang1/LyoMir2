using System;
using System.Collections.Generic;

namespace DBSvr.Core
{
    public enum NativeAdmissionQueueActionKind
    {
        Position,
        Terminal4018
    }

    public sealed class NativeAdmissionQueueAction
    {
        public NativeAdmissionQueueAction(TUserInfo user,
            NativeAdmissionQueueActionKind kind, ushort position,
            ushort queueCount, ushort series)
        {
            User = user;
            Kind = kind;
            Position = position;
            QueueCount = queueCount;
            Series = series;
        }

        public TUserInfo User { get; }
        public NativeAdmissionQueueActionKind Kind { get; }
        public ushort Position { get; }
        public ushort QueueCount { get; }
        public ushort Series { get; }
    }

    /// <summary>
    /// Socket-free native admission queue. The queue lock only protects list
    /// state and action snapshots; callers perform all wire I/O afterward.
    /// </summary>
    public sealed class NativeAdmissionQueue
    {
        private const uint RefreshIntervalMilliseconds = 7000;
        private const uint MinimumSampleMilliseconds = 60000;
        private const uint MaximumSampleMilliseconds = 18000000;
        private const ushort MinimumSecondsPerPosition = 60;
        private const ushort MaximumSecondsPerPosition = 10800;
        private const int HistoryCapacity = 5000;
        private const int HistoryTrimStart = 2499;
        private const int HistoryTrimCount = 2499;

        private readonly object _sync = new();
        private readonly List<TUserInfo> _users = new();
        private readonly List<HistoryEntry> _history = new(HistoryCapacity);
        private ushort _secondsPerPosition;
        private uint _lastRefreshTick;
        private bool _dirty;

        private readonly struct HistoryEntry
        {
            public HistoryEntry(uint enqueueTick, uint removalTick)
            {
                EnqueueTick = enqueueTick;
                RemovalTick = removalTick;
            }

            public uint EnqueueTick { get; }
            public uint RemovalTick { get; }
        }

        public int Count
        {
            get { lock (_sync) return _users.Count; }
        }

        public int HistoryCount
        {
            get { lock (_sync) return _history.Count; }
        }

        public ushort SecondsPerPosition
        {
            get { lock (_sync) return _secondsPerPosition; }
        }

        public uint LastRefreshTick
        {
            get { lock (_sync) return _lastRefreshTick; }
        }

        public bool Dirty
        {
            get { lock (_sync) return _dirty; }
        }

        public static bool ShouldQueue(bool enabled, int ownerCount,
            int countLimit, int loginGateCapacity) =>
            enabled && (ownerCount >= countLimit
                        || ownerCount >= loginGateCapacity);

        public IReadOnlyList<NativeAdmissionQueueAction> Enqueue(
            TUserInfo user, uint tick)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            lock (_sync)
            {
                user.NativeQueueEnqueueTick = tick;
                var found = false;
                foreach (var current in _users)
                    if (ReferenceEquals(current, user))
                    {
                        found = true;
                        break;
                    }
                if (!found) _users.Add(user);
                var position = unchecked((ushort)_users.Count);
                var queueCount = position;
                user.NativeQueuePosition = position;
                var action = CreatePositionAction(user, position,
                    queueCount, _secondsPerPosition);
                _dirty = true;
                if (position > 10)
                    return new[] { action };
                return new[]
                {
                    action,
                    CreatePositionAction(user, position, queueCount,
                        _secondsPerPosition)
                };
            }
        }

        public IReadOnlyList<NativeAdmissionQueueAction> RemoveForConnection(
            TUserInfo user, uint tick)
        {
            if (user == null || !user.NativeAdmissionManaged
                             || user.NativeQueueBypass)
                return Array.Empty<NativeAdmissionQueueAction>();

            lock (_sync)
            {
                _dirty = true;
                if (_users.Count == 0)
                    return Array.Empty<NativeAdmissionQueueAction>();

                var index = user.NativeQueuePosition == 0
                    ? 0
                    : user.NativeQueuePosition - 1;
                if (index < 0 || index >= _users.Count)
                    return Array.Empty<NativeAdmissionQueueAction>();
                if (user.NativeQueuePosition != 0
                    && !ReferenceEquals(_users[index], user))
                    return Array.Empty<NativeAdmissionQueueAction>();

                var actions = new List<NativeAdmissionQueueAction>();
                var oldCount = unchecked((ushort)_users.Count);
                var removed = _users[index];
                removed.NativeQueuePosition = 0;
                actions.Add(CreatePositionAction(removed, 0, oldCount,
                    _secondsPerPosition));
                _users.RemoveAt(index);
                if (index == 0)
                    RecordHeadRemoval(removed.NativeQueueEnqueueTick, tick);

                var newCount = unchecked((ushort)_users.Count);
                for (var i = index; i < _users.Count; i++)
                {
                    var position = unchecked((ushort)(i + 1));
                    _users[i].NativeQueuePosition = position;
                    if (position <= 10)
                        actions.Add(CreatePositionAction(_users[i], position,
                            newCount, _secondsPerPosition));
                }
                return actions;
            }
        }

        public IReadOnlyList<NativeAdmissionQueueAction> Refresh(uint tick)
        {
            lock (_sync)
            {
                var elapsed = unchecked(tick - _lastRefreshTick);
                if (elapsed <= RefreshIntervalMilliseconds)
                    return Array.Empty<NativeAdmissionQueueAction>();

                _lastRefreshTick = tick;
                _secondsPerPosition = CalculateSecondsPerPosition();
                if (_users.Count == 0)
                    return Array.Empty<NativeAdmissionQueueAction>();

                var loopCount = _users.Count;
                // The periodic native refresh calls the packet builder directly
                // for every queued record. The <=10 limit belongs to the
                // position-setter notification path, not this replay loop.
                var actions = new List<NativeAdmissionQueueAction>(loopCount);
                for (var i = 0; i < loopCount; i++)
                {
                    var user = _users[i];
                    actions.Add(CreatePositionAction(user,
                        user.NativeQueuePosition,
                        unchecked((ushort)_users.Count),
                        _secondsPerPosition));
                }
                return actions;
            }
        }

        public IReadOnlyList<NativeAdmissionQueueAction> Drain()
        {
            lock (_sync)
            {
                if (_users.Count == 0)
                    return Array.Empty<NativeAdmissionQueueAction>();
                var queueCount = unchecked((ushort)_users.Count);
                var actions = new List<NativeAdmissionQueueAction>(
                    _users.Count * 2);
                foreach (var user in _users)
                {
                    user.NativeQueuePosition = 0;
                    actions.Add(CreatePositionAction(user, 0, queueCount,
                        _secondsPerPosition));
                    actions.Add(new NativeAdmissionQueueAction(user,
                        NativeAdmissionQueueActionKind.Terminal4018,
                        0, queueCount, 0));
                }
                _users.Clear();
                return actions;
            }
        }

        private void RecordHeadRemoval(uint enqueueTick, uint removalTick)
        {
            _history.Add(new HistoryEntry(enqueueTick, removalTick));
            if (_history.Count < HistoryCapacity) return;

            // Native 0x5A2723 deliberately keeps count=2499 after moving
            // 2501 slots. Only source entries 2499..4997 remain logically live.
            var retained = _history.GetRange(HistoryTrimStart,
                HistoryTrimCount);
            _history.Clear();
            _history.AddRange(retained);
        }

        private ushort CalculateSecondsPerPosition()
        {
            uint minimum = 0;
            uint maximum = 0;
            foreach (var entry in _history)
            {
                var duration = unchecked(entry.RemovalTick - entry.EnqueueTick);
                if (!IsValidDuration(duration)) continue;
                if (minimum == 0 || duration < minimum) minimum = duration;
                if (duration > maximum) maximum = duration;
            }

            uint sum = 0;
            uint count = 0;
            foreach (var entry in _history)
            {
                var duration = unchecked(entry.RemovalTick - entry.EnqueueTick);
                if (duration <= minimum || duration >= maximum
                    || !IsValidDuration(duration))
                    continue;
                sum = unchecked(sum + duration);
                count = unchecked(count + 1);
            }

            var rounded = count > 0
                ? RoundUnsignedRatioToEven(sum, (ulong)count * 1000UL)
                : RoundUnsignedRatioToEven(minimum + maximum, 2000UL);
            var seconds = unchecked((ushort)rounded);
            if (seconds > 0 && seconds < MinimumSecondsPerPosition)
                return MinimumSecondsPerPosition;
            if (seconds > MaximumSecondsPerPosition)
                return MaximumSecondsPerPosition;
            return seconds;
        }

        private static bool IsValidDuration(uint duration) =>
            duration > MinimumSampleMilliseconds
            && duration < MaximumSampleMilliseconds;

        private static uint RoundUnsignedRatioToEven(uint numerator,
            ulong denominator)
        {
            if (denominator == 0) return 0;
            var quotient = (ulong)numerator / denominator;
            var remainder = (ulong)numerator % denominator;
            var doubled = remainder * 2UL;
            if (doubled > denominator
                || (doubled == denominator && (quotient & 1UL) != 0))
                quotient++;
            return unchecked((uint)quotient);
        }

        private static NativeAdmissionQueueAction CreatePositionAction(
            TUserInfo user, ushort position, ushort queueCount,
            ushort secondsPerPosition)
        {
            var product = (uint)secondsPerPosition * position;
            var series = product > ushort.MaxValue
                ? ushort.MaxValue
                : unchecked((ushort)product);
            return new NativeAdmissionQueueAction(user,
                NativeAdmissionQueueActionKind.Position, position,
                queueCount, series);
        }
    }
}
