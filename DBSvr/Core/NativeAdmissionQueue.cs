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
        private readonly object _sync = new();
        private readonly List<TUserInfo> _users = new();

        public int Count
        {
            get { lock (_sync) return _users.Count; }
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
                    queueCount, 0);
                if (position > 10)
                    return new[] { action };
                return new[]
                {
                    action,
                    CreatePositionAction(user, position, queueCount, 0)
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
                actions.Add(CreatePositionAction(removed, 0, oldCount, 0));
                _users.RemoveAt(index);

                var newCount = unchecked((ushort)_users.Count);
                for (var i = index; i < _users.Count; i++)
                {
                    var position = unchecked((ushort)(i + 1));
                    _users[i].NativeQueuePosition = position;
                    if (position <= 10)
                        actions.Add(CreatePositionAction(_users[i], position,
                            newCount, 0));
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
                    actions.Add(CreatePositionAction(user, 0, queueCount, 0));
                    actions.Add(new NativeAdmissionQueueAction(user,
                        NativeAdmissionQueueActionKind.Terminal4018,
                        0, queueCount, 0));
                }
                _users.Clear();
                return actions;
            }
        }

        private static NativeAdmissionQueueAction CreatePositionAction(
            TUserInfo user, ushort position, ushort queueCount,
            ushort secondsPerPosition)
        {
            var series = Math.Min(ushort.MaxValue,
                secondsPerPosition * position);
            return new NativeAdmissionQueueAction(user,
                NativeAdmissionQueueActionKind.Position, position,
                queueCount, unchecked((ushort)series));
        }
    }
}
