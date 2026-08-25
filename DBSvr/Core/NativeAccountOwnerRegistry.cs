using System;
using System.Collections.Generic;

namespace DBSvr.Core
{
    public sealed class NativeAccountOwnerTakeover
    {
        internal NativeAccountOwnerTakeover(string key, long token,
            TUserInfo displacedOwner, TUserInfo candidate)
        {
            Key = key;
            Token = token;
            DisplacedOwner = displacedOwner;
            Candidate = candidate;
        }

        internal string Key { get; }
        internal long Token { get; }
        public TUserInfo DisplacedOwner { get; }
        public TUserInfo Candidate { get; }
    }

    /// <summary>
    /// Native DBServer account-owner hash table. The original container
    /// permits duplicate keys, inserts at the bucket head, and removes the
    /// first matching key without checking the stored object identity.
    /// Account bytes fold ASCII A-Z only; all other GBK bytes remain intact.
    /// </summary>
    public sealed class NativeAccountOwnerRegistry
    {
        private sealed class PendingTakeover
        {
            public TUserInfo Candidate;
            public long Token;
        }

        private readonly object _sync = new();
        private readonly Dictionary<string, List<TUserInfo>> _owners =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingTakeover> _pending =
            new(StringComparer.Ordinal);
        private long _nextToken;

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    var count = 0;
                    foreach (var values in _owners.Values)
                        count += values.Count;
                    return count;
                }
            }
        }

        public bool TryClaim(string account, TUserInfo candidate,
            out TUserInfo existingOwner)
        {
            existingOwner = null;
            if (candidate == null || !TryCreateKey(account, out var key))
                return false;

            lock (_sync)
            {
                if (_pending.TryGetValue(key, out var pending))
                {
                    existingOwner = pending.Candidate;
                    return false;
                }

                if (_owners.TryGetValue(key, out var values)
                    && values.Count != 0)
                {
                    existingOwner = values[0];
                    return ReferenceEquals(existingOwner, candidate);
                }

                _owners[key] = new List<TUserInfo> { candidate };
                return true;
            }
        }

        public bool HasRegisteredAccount(string account)
        {
            if (!TryCreateKey(account, out var key)) return false;
            lock (_sync)
                return _owners.TryGetValue(key, out var values)
                       && values.Count != 0;
        }

        public bool TryBeginObservedTakeover(string account,
            TUserInfo candidate, out NativeAccountOwnerTakeover takeover)
        {
            takeover = null;
            if (candidate == null || !TryCreateKey(account, out var key))
                return false;

            lock (_sync)
            {
                if (_pending.ContainsKey(key)) return false;
                TUserInfo displaced = null;
                if (_owners.TryGetValue(key, out var values)
                    && values.Count != 0)
                {
                    displaced = values[0];
                }

                var token = ++_nextToken;
                _pending.Add(key, new PendingTakeover
                {
                    Candidate = candidate,
                    Token = token
                });
                takeover = new NativeAccountOwnerTakeover(key, token,
                    displaced, candidate);
                return true;
            }
        }

        public bool CompleteTakeover(NativeAccountOwnerTakeover takeover)
        {
            if (takeover == null) return false;
            lock (_sync)
            {
                if (!_pending.TryGetValue(takeover.Key, out var pending)
                    || pending.Token != takeover.Token
                    || !ReferenceEquals(pending.Candidate,
                        takeover.Candidate))
                    return false;

                if (!_owners.TryGetValue(takeover.Key, out var values))
                {
                    values = new List<TUserInfo>();
                    _owners.Add(takeover.Key, values);
                }
                values.Insert(0, takeover.Candidate);
                _pending.Remove(takeover.Key);
                return true;
            }
        }

        public bool CancelTakeover(NativeAccountOwnerTakeover takeover)
        {
            if (takeover == null) return false;
            lock (_sync)
            {
                if (!_pending.TryGetValue(takeover.Key, out var pending)
                    || pending.Token != takeover.Token
                    || !ReferenceEquals(pending.Candidate,
                        takeover.Candidate))
                    return false;
                _pending.Remove(takeover.Key);
                return true;
            }
        }

        public bool ReleaseForConnection(string account, TUserInfo connection)
        {
            if (!TryCreateKey(account, out var key)) return false;
            lock (_sync)
            {
                var changed = false;
                if (_pending.TryGetValue(key, out var pending)
                    && ReferenceEquals(pending.Candidate, connection))
                {
                    _pending.Remove(key);
                    changed = true;
                }

                if (_owners.TryGetValue(key, out var values)
                    && values.Count != 0)
                {
                    values.RemoveAt(0);
                    if (values.Count == 0) _owners.Remove(key);
                    changed = true;
                }
                return changed;
            }
        }

        public bool ReleaseRegisteredOwnerByKey(string account)
        {
            if (!TryCreateKey(account, out var key)) return false;
            lock (_sync)
            {
                if (!_owners.TryGetValue(key, out var values)
                    || values.Count == 0)
                    return false;
                values.RemoveAt(0);
                if (values.Count == 0) _owners.Remove(key);
                return true;
            }
        }

        public IReadOnlyList<string> SnapshotOwnerIps()
        {
            lock (_sync)
            {
                var result = new List<string>();
                foreach (var values in _owners.Values)
                    foreach (var owner in values)
                        if (owner != null)
                            result.Add(owner.sUserIPaddr ?? string.Empty);
                return result;
            }
        }

        public void WithOwnerIpSnapshot(
            Action<IReadOnlyList<string>> consume)
        {
            if (consume == null)
                throw new ArgumentNullException(nameof(consume));
            lock (_sync)
            {
                var result = new List<string>();
                foreach (var values in _owners.Values)
                    foreach (var owner in values)
                        if (owner != null)
                            result.Add(owner.sUserIPaddr ?? string.Empty);
                consume(result);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _owners.Clear();
                _pending.Clear();
            }
        }

        public static string CanonicalizeAccountBytes(
            ReadOnlySpan<byte> accountBytes)
        {
            if (accountBytes.Length == 0) return string.Empty;
            var normalized = accountBytes.ToArray();
            for (var i = 0; i < normalized.Length; i++)
                if (normalized[i] is >= (byte)'A' and <= (byte)'Z')
                    normalized[i] += 0x20;
            return Convert.ToHexString(normalized);
        }

        private static bool TryCreateKey(string account, out string key)
        {
            key = string.Empty;
            if (string.IsNullOrEmpty(account)) return false;
            try
            {
                key = CanonicalizeAccountBytes(LegacyGbkText.Encode(account));
                return key.Length != 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
