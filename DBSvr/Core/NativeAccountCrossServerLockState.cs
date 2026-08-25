using System.Collections.Concurrent;

namespace DBSvr.Core
{
    /// <summary>
    /// Transient mirror of the native account record byte at +0x1E.
    /// The flag is changed only by the native 0x019E set/clear command.
    /// </summary>
    public sealed class NativeAccountCrossServerLockState
    {
        private readonly ConcurrentDictionary<string, byte> _lockedAccounts =
            new(System.StringComparer.Ordinal);

        public void SetLocked(string ptid, bool locked)
        {
            var key = NativeType3Protocol.NormalizePtidKey(ptid);
            if (string.IsNullOrEmpty(key)) return;

            if (locked)
                _lockedAccounts[key] = 1;
            else
                _lockedAccounts.TryRemove(key, out _);
        }

        public bool IsLocked(string ptid)
        {
            var key = NativeType3Protocol.NormalizePtidKey(ptid);
            return !string.IsNullOrEmpty(key)
                   && _lockedAccounts.TryGetValue(key, out _);
        }
    }
}
