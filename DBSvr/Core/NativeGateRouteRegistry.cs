using System;
using System.Collections.Generic;

namespace DBSvr.Core
{
    /// <summary>
    /// Per-gate native route table. The 2.08 container accepts zero and
    /// duplicate WORD keys, inserts at the bucket head, and removes the newest
    /// matching entry.
    /// </summary>
    public sealed class NativeGateRouteRegistry
    {
        private readonly object _sync = new();
        private readonly Dictionary<ushort, List<TUserInfo>> _routes = new();

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    var count = 0;
                    foreach (var values in _routes.Values) count += values.Count;
                    return count;
                }
            }
        }

        public void Register(ushort routeId, TUserInfo user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            lock (_sync)
            {
                if (!_routes.TryGetValue(routeId, out var values))
                {
                    values = new List<TUserInfo>();
                    _routes.Add(routeId, values);
                }
                values.Insert(0, user);
            }
        }

        public bool TryGetNewest(ushort routeId, out TUserInfo user)
        {
            lock (_sync)
            {
                if (_routes.TryGetValue(routeId, out var values)
                    && values.Count != 0)
                {
                    user = values[0];
                    return true;
                }
            }
            user = null;
            return false;
        }

        public bool TryRemoveNewest(ushort routeId, out TUserInfo user)
        {
            lock (_sync)
            {
                if (_routes.TryGetValue(routeId, out var values)
                    && values.Count != 0)
                {
                    user = values[0];
                    values.RemoveAt(0);
                    if (values.Count == 0) _routes.Remove(routeId);
                    return true;
                }
            }
            user = null;
            return false;
        }

        public void Clear()
        {
            lock (_sync) _routes.Clear();
        }
    }
}
